#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' The listing engine of the file manager mode of the F3 panel (257_SPECIFICATION_FILE_MANAGER_NAVIGATION_AND_PROVIDER_CONTEXT.md,
' slice N3). It owns three things and no control: the asynchronous directory listing, the provider leases the manager connected for
' itself, and the rule that only the NEWEST listing is ever published. Everything it hands out is immutable. The panel marshals the
' result to its UI thread; nothing in here names a form. Modern build only.

''' <summary>A provider the manager opened for a place, with whatever must be disposed when the manager lets it go.</summary>
Friend NotInheritable Class ManagerOpenResult

    Private Sub New(provider As IManagerDirectoryProvider, owned As IDisposable, status As ManagerStatusKind, failure As ResourceFailure)
        Me.Provider = provider
        Me.Owned = owned
        Me.Status = status
        Me.Failure = failure
    End Sub

    ''' <summary>The provider to list through, or Nothing when the place could not be opened.</summary>
    Friend ReadOnly Property Provider As IManagerDirectoryProvider
    ''' <summary>What disposing the place's connection means (a server session, a share lease); Nothing for a plain disk.</summary>
    Friend ReadOnly Property Owned As IDisposable
    Friend ReadOnly Property Status As ManagerStatusKind
    Friend ReadOnly Property Failure As ResourceFailure
    Friend Property ConnectedDescriptor As ResourceDescriptor
    Private references As Integer = 1

    Friend Function Retain() As Boolean
        Dim count As Integer = Volatile.Read(references)
        While count > 0
            Dim prior As Integer = Interlocked.CompareExchange(references, count + 1, count)
            If prior = count Then Return True
            count = prior
        End While
        Return False
    End Function

    Friend Shared Function Ok(provider As IManagerDirectoryProvider, Optional owned As IDisposable = Nothing) As ManagerOpenResult
        Return New ManagerOpenResult(provider, owned, ManagerStatusKind.Ok, Nothing)
    End Function

    Friend Shared Function Fail(status As ManagerStatusKind, Optional failure As ResourceFailure = Nothing) As ManagerOpenResult
        Return New ManagerOpenResult(Nothing, Nothing, status, failure)
    End Function

    Friend Sub Release()
        If Interlocked.Decrement(references) <> 0 Then Return
        Try
            Owned?.Dispose()
        Catch ex As Exception
            ' A connection that will not close cleanly is already gone for the user's purposes.
        End Try
    End Sub
End Class

Friend NotInheritable Class ManagerBrowser
    Implements IDisposable

    Private ReadOnly opener As Func(Of ManagerPlace, CancellationToken, ManagerOpenResult)
    Private ReadOnly on_Fault As Action(Of Exception)

    ''' <summary>Guards the counters and the cancellation source.</summary>
    Private ReadOnly state_Lock As New Object()
    ''' <summary>Guards the lease table.</summary>
    Private ReadOnly lease_Lock As New Object()
    ''' <summary>One connection is opened at a time, and waiting for the turn is cancellable.</summary>
    Private ReadOnly open_Turn As New SemaphoreSlim(1, 1)
    Private ReadOnly leases As New Dictionary(Of String, ManagerOpenResult)(StringComparer.Ordinal)

    Private cancel_Source As CancellationTokenSource
    Private generation_Value As Integer
    Private busy_Generation As Integer
    Private is_Disposed As Boolean

    ''' <summary>Raised on a worker thread, once, for the newest listing only. A listing that was overtaken is dropped silently.</summary>
    Friend Event Completed As EventHandler(Of ManagerListingResult)

    ''' <param name="opener">Opens the provider for a place. Called on a worker with a token that a newer navigation cancels.</param>
    ''' <param name="onFault">Told about an exception the browser turned into a typed result (for the log); may be Nothing.</param>
    Friend Sub New(opener As Func(Of ManagerPlace, CancellationToken, ManagerOpenResult), Optional onFault As Action(Of Exception) = Nothing)
        If opener Is Nothing Then Throw New ArgumentNullException(NameOf(opener))
        Me.opener = opener
        Me.on_Fault = onFault
    End Sub

    ''' <summary>The counter of the newest listing asked for.</summary>
    Friend ReadOnly Property Generation As Integer
        Get
            Return Volatile.Read(generation_Value)
        End Get
    End Property

    ''' <summary>A listing is under way and nothing newer has replaced it.</summary>
    Friend ReadOnly Property IsBusy As Boolean
        Get
            SyncLock state_Lock
                Return busy_Generation <> 0 AndAlso busy_Generation = generation_Value
            End SyncLock
        End Get
    End Property

    Friend Function IsCurrent(generation As Integer) As Boolean
        Return generation = Volatile.Read(generation_Value)
    End Function

    Friend Function KnownCapabilities(place As ManagerPlace) As ProviderCapabilities
        SyncLock lease_Lock
            Dim opened As ManagerOpenResult = Nothing
            If leases.TryGetValue(place.Id, opened) Then Return TryCast(opened.Provider, IFileSystemProvider)?.Capabilities
        End SyncLock
        Return Nothing
    End Function

    Friend Function ConnectedDescriptor(place As ManagerPlace) As ResourceDescriptor
        SyncLock lease_Lock
            Dim opened As ManagerOpenResult = Nothing
            If leases.TryGetValue(place.Id, opened) Then Return opened.ConnectedDescriptor
        End SyncLock
        Return Nothing
    End Function

    ''' <summary>The places the manager holds a live connection for.</summary>
    Friend ReadOnly Property LeaseCount As Integer
        Get
            SyncLock lease_Lock
                Return leases.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' Starts listing <paramref name="target"/> and CANCELS whatever was listing before. The returned task completes with the result
    ''' whether or not it was overtaken; <see cref="Completed"/> fires only for the newest, so a slow A that finishes after B was asked
    ''' for never reaches the screen.
    ''' </summary>
    Friend Function Begin(intent As ManagerIntent, target As ManagerLocation) As Task(Of ManagerListingResult)
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        Dim gen As Integer
        Dim token As CancellationToken
        SyncLock state_Lock
            If is_Disposed Then
                Return Task.FromResult(New ManagerListingResult(generation_Value, intent, target, ManagerStatusKind.Cancelled, Nothing, Nothing))
            End If
            cancel_Source?.Cancel()
            cancel_Source = New CancellationTokenSource()
            token = cancel_Source.Token
            generation_Value += 1
            gen = generation_Value
            busy_Generation = gen
        End SyncLock
        Return Task.Run(Function() Run(gen, intent, target, token))
    End Function

    ''' <summary>Cancels the listing under way (Esc, a closing panel). Nothing is published for it.</summary>
    Friend Sub Cancel()
        SyncLock state_Lock
            cancel_Source?.Cancel()
            ' A cancel is a generation of its own: whatever was in flight is now stale, and nothing is busy.
            generation_Value += 1
            busy_Generation = 0
        End SyncLock
    End Sub

    ' ------------------------------------------------------------------ the worker ----

    Private Function Run(gen As Integer, intent As ManagerIntent, target As ManagerLocation, ct As CancellationToken) As ManagerListingResult
        Dim result As ManagerListingResult
        Try
            result = Resolve(gen, intent, target, ct)
        Catch ex As OperationCanceledException
            result = Make(gen, intent, target, ManagerStatusKind.Cancelled, Nothing, Nothing)
        Catch ex As Exception
            ReportFault(ex)
            result = Make(gen, intent, target, ManagerStatusKind.Disconnected, Nothing, ResourceFailure.FromException(ex))
        End Try

        Dim publish As Boolean = False
        SyncLock state_Lock
            If gen = generation_Value AndAlso Not is_Disposed Then
                busy_Generation = 0
                publish = True
            End If
        End SyncLock
        If publish Then
            Try
                RaiseEvent Completed(Me, result)
            Catch ex As Exception
                ReportFault(ex)
            End Try
        End If
        Return result
    End Function

    Private Function Resolve(gen As Integer, intent As ManagerIntent, target As ManagerLocation, ct As CancellationToken) As ManagerListingResult
        ct.ThrowIfCancellationRequested()
        Dim place As ManagerPlace = target.Place

        ' Lexical first: a ".." segment or a path outside the root never reaches a provider.
        Dim normalized As String = ManagerPathRules.Normalize(place.Kind, target.DirectoryPath)
        If normalized Is Nothing OrElse Not ManagerPathRules.Contains(place.Kind, place.RootPath, normalized) Then
            Return Make(gen, intent, target, ManagerStatusKind.OutsideRoot, Nothing, Nothing)
        End If

        Dim opened As ManagerOpenResult = Acquire(place, ct)
        If opened.Provider Is Nothing Then
            Return Make(gen, intent, target, opened.Status, Nothing, opened.Failure)
        End If

        ' Then the provider's own canonical word on containment.
        If Not opened.Provider.IsWithinRoot(place.RootPath, normalized) Then
            Return Make(gen, intent, target, ManagerStatusKind.OutsideRoot, Nothing, Nothing)
        End If

        Dim listing As DirectoryListing = opened.Provider.ListChildren(normalized, ct)
        ct.ThrowIfCancellationRequested()
        Dim committed As ManagerLocation = target.WithDirectory(normalized)

        Select Case listing.Outcome
            Case DirectoryOutcome.Listed
                Dim entries As New List(Of ManagerEntry)()
                For Each entry As ResourceEntry In listing.Entries
                    If entry Is Nothing Then Continue For
                    Dim name As String = If(entry.Name, "")
                    If name = "." OrElse name = ".." Then Continue For
                    entries.Add(ManagerEntry.FromResource(place, entry))
                Next
                Return Make(gen, intent, committed, If(listing.IsTruncated, ManagerStatusKind.Truncated, ManagerStatusKind.Ok), entries, Nothing)
            Case DirectoryOutcome.Missing
                Return Make(gen, intent, target, ManagerStatusKind.Missing, Nothing, listing.Failure)
            Case DirectoryOutcome.NotADirectory
                Return Make(gen, intent, target, ManagerStatusKind.NotADirectory, Nothing, listing.Failure)
            Case DirectoryOutcome.Denied
                Return Make(gen, intent, target, ManagerStatusKind.Denied, Nothing, listing.Failure)
            Case DirectoryOutcome.Cancelled
                Return Make(gen, intent, target, ManagerStatusKind.Cancelled, Nothing, listing.Failure)
            Case Else
                ' A link that did not answer: the connection behind it is not trusted any more, so the next try dials again.
                Retire(place.Id)
                Return Make(gen, intent, target, ManagerStatusKind.Disconnected, Nothing, listing.Failure)
        End Select
    End Function

    Private Shared Function Make(gen As Integer, intent As ManagerIntent, target As ManagerLocation, status As ManagerStatusKind,
                                 entries As IReadOnlyList(Of ManagerEntry), failure As ResourceFailure) As ManagerListingResult
        Return New ManagerListingResult(gen, intent, target, status, entries, failure)
    End Function

    Private Sub ReportFault(ex As Exception)
        Try
            on_Fault?.Invoke(ex)
        Catch
            ' The log must never break a listing.
        End Try
    End Sub

    ' ------------------------------------------------------------------- leases ----

    Friend Function CaptureEndpoint(place As ManagerPlace, ct As CancellationToken,
                                    Optional policy As Func(Of IFileSystemProvider, ProviderCapabilities) = Nothing) As ManagerEndpointLease
        Dim opened As ManagerOpenResult = Acquire(place, ct)
        If opened.Provider Is Nothing OrElse Not opened.Retain() Then Throw New IOException("The place is unavailable.")
        Try
            Return New ManagerEndpointLease(place, opened, policy)
        Catch
            opened.Release()
            Throw
        End Try
    End Function

    Private Function Acquire(place As ManagerPlace, ct As CancellationToken) As ManagerOpenResult
        open_Turn.Wait(ct)
        Try
            SyncLock lease_Lock
                Dim existing As ManagerOpenResult = Nothing
                If leases.TryGetValue(place.Id, existing) Then
                    If existing.ConnectedDescriptor IsNot Nothing AndAlso ManagerPlace.ForResource(existing.ConnectedDescriptor).ExchangeIdentity <> place.ExchangeIdentity Then
                        Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
                    End If
                    Return existing
                End If
            End SyncLock

            Dim opened As ManagerOpenResult = opener(place, ct)
            If opened Is Nothing Then Return ManagerOpenResult.Fail(ManagerStatusKind.Unavailable)
            If opened.Provider Is Nothing Then Return opened

            SyncLock lease_Lock
                If is_Disposed OrElse ct.IsCancellationRequested Then
                    opened.Release()
                    ct.ThrowIfCancellationRequested()
                    Return ManagerOpenResult.Fail(ManagerStatusKind.Cancelled)
                End If
                leases(place.Id) = opened
            End SyncLock
            Return opened
        Finally
            open_Turn.Release()
        End Try
    End Function

    ''' <summary>Lets go of one place's connection.</summary>
    Private Sub Retire(placeId As String)
        Dim gone As ManagerOpenResult = Nothing
        SyncLock lease_Lock
            If leases.TryGetValue(placeId, gone) Then leases.Remove(placeId)
        End SyncLock
        gone?.Release()
    End Sub

    ''' <summary>
    ''' After a navigation committed in <paramref name="placeId"/>: every other place's connection is stale and goes, off the UI thread
    ''' (closing a server session can take a moment). Called only for the newest, committed listing, so nothing in flight uses them.
    ''' </summary>
    Friend Sub KeepOnly(placeId As String)
        Dim stale As List(Of ManagerOpenResult)
        SyncLock lease_Lock
            stale = leases.Where(Function(p) Not String.Equals(p.Key, placeId, StringComparison.Ordinal)).Select(Function(p) p.Value).ToList()
            For Each key As String In leases.Keys.Where(Function(k) Not String.Equals(k, placeId, StringComparison.Ordinal)).ToList()
                leases.Remove(key)
            Next
        End SyncLock
        If stale.Count = 0 Then Return
        ThreadPool.QueueUserWorkItem(Sub(state)
                                         For Each lease As ManagerOpenResult In stale
                                             lease.Release()
                                         Next
                                     End Sub)
    End Sub

    ''' <summary>Cancels what is under way and lets go of every connection, synchronously: when this returns the manager holds none.</summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        Dim all As List(Of ManagerOpenResult)
        SyncLock state_Lock
            If is_Disposed Then Return
            is_Disposed = True
            cancel_Source?.Cancel()
            generation_Value += 1
            busy_Generation = 0
        End SyncLock
        SyncLock lease_Lock
            all = leases.Values.ToList()
            leases.Clear()
        End SyncLock
        For Each lease As ManagerOpenResult In all
            lease.Release()
        Next
    End Sub
End Class
#End If
