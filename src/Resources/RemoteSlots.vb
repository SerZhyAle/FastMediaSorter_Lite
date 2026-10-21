#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading

''' <summary>
''' Where a file lands when a recipient slot is sent a file - a folder on this PC, or a folder of a
''' saved resource (007 §4.2: <c>Hardkeys_to_move_mediafile</c> holds a string per slot, so a slot may
''' hold a locator). Every caller that used to glue "folder &amp; \ &amp; name" asks here instead, so a
''' server folder never turns into "sftp://..\name". Pure - strings in, strings out.
''' </summary>
Friend Module SlotPaths

    ''' <summary>The file <paramref name="fileName"/> inside <paramref name="folder"/>.</summary>
    Friend Function Combine(folder As String, fileName As String) As String
        If ResourceLocator.IsLocator(folder) Then Return ResourceLocator.AppendName(folder, fileName)
        Return Path.Combine(If(folder, ""), If(fileName, ""))
    End Function

    ''' <summary>The folder a file path or a file locator sits in; "" when there is none.</summary>
    Friend Function FolderOf(filePath As String) As String
        If ResourceLocator.IsLocator(filePath) Then Return ResourceLocator.ParentOf(filePath)
        Try
            Return If(Path.GetDirectoryName(filePath), "")
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>A file's own name - decoded, for a locator: the name as the server spells it.</summary>
    Friend Function FileNameOf(filePath As String) As String
        If ResourceLocator.IsLocator(filePath) Then Return ResourceLocator.LeafName(filePath)
        Try
            Return Path.GetFileName(If(filePath, ""))
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Do two file paths sit in the same folder? A locator and a Windows path never do.</summary>
    Friend Function SameFolder(fileA As String, fileB As String) As Boolean
        Dim remoteA As Boolean = ResourceLocator.IsLocator(fileA)
        Dim remoteB As Boolean = ResourceLocator.IsLocator(fileB)
        If remoteA <> remoteB Then Return False
        If remoteA Then Return ResourceLocator.SameFolder(FolderOf(fileA), FolderOf(fileB))
        Return String.Equals(FolderOf(fileA), FolderOf(fileB), StringComparison.OrdinalIgnoreCase)
    End Function

End Module

''' <summary>Why a slot that names a resource cannot even be asked about yet - each one a thing the
''' user can do something about, so each gets its own sentence (Main_Form.SlotHealth.vb).</summary>
Friend Enum RemoteSlotProblem
    None = 0
    ''' <summary>A plain folder: not this layer's business.</summary>
    NotRemote
    ''' <summary>The address does not parse.</summary>
    Invalid
    ''' <summary>No saved resource is that server - nowhere to take a password or a pinned key from.</summary>
    UnknownResource
    ''' <summary>The server's key has never been verified. A slot never asks the trust question: that
    ''' is asked once, by opening the resource, where the fingerprint can be read and compared.</summary>
    NotPinned
    ''' <summary>Nothing stored and nothing typed this run: the user must be asked.</summary>
    NoCredential
    ''' <summary>The resource was saved as read-only: nothing may be written into it.</summary>
    ReadOnlyResource
End Enum

''' <summary>The pure rules of a remote slot: which failure is which verdict, and how long a probe
''' of one may take.</summary>
Friend Module RemoteSlotPolicy

    ''' <summary>
    ''' The probe bound for a slot. A plain folder keeps the historical two seconds (D2). A server
    ''' has to be dialled first - TCP, the SSH handshake, the key check and the login - and the
    ''' connect alone has a 5 s budget (007 §2.2), so its probe gets that plus the plain one; a
    ''' bound shorter than the connect would call every cold server unreachable.
    ''' </summary>
    Friend Function ProbeTimeoutMs(destination As String) As Integer
        If ResourceLocator.IsLocator(destination) Then Return Remote_Probe_Timeout_Ms
        Return SlotHealthPolicy.Probe_Timeout_Ms
    End Function

    Friend Const Remote_Probe_Timeout_Ms As Integer = 7000

    ''' <summary>What a failed question to the server means for the slot. A refused credential, a
    ''' changed key and a permission are something that answered and said no; a dropped link, a
    ''' timeout and a cancel are nothing answering - the retry case; "no such file" is missing.</summary>
    Friend Function StateFor(failure As ResourceFailure) As SlotState
        If failure Is Nothing Then Return SlotState.Unreachable
        Select Case failure.Kind
            Case ResourceFailureKind.Auth, ResourceFailureKind.KeyMismatch, ResourceFailureKind.CredentialConflict
                Return SlotState.Denied
            Case ResourceFailureKind.SftpStatus
                If failure.StatusCode = 2 Then Return SlotState.Missing
                Return SlotState.Denied
            Case ResourceFailureKind.Unsupported
                Return SlotState.Invalid
            Case Else
                Return SlotState.Unreachable
        End Select
    End Function

    ''' <summary>
    ''' The verdict for a server folder, from the two questions the probe asks: the folder itself, and
    ''' - only when it is missing - its parent. Mirrors the local probe (D6): only the last segment may
    ''' be created, and only when the parent answered.
    ''' </summary>
    Friend Function Verdict(folder As StatResult, parent As StatResult, canWrite As Boolean,
                            canCreateFolder As Boolean, allowCreate As Boolean, nowUtc As DateTime) As SlotVerdict
        If Not canWrite Then Return SlotVerdict.ForState(SlotState.Denied, nowUtc, "read-only resource")
        If folder Is Nothing Then Return SlotVerdict.ForState(SlotState.Unreachable, nowUtc, "no answer")
        Select Case folder.Outcome
            Case StatOutcome.Found
                If folder.Entry IsNot Nothing AndAlso Not folder.Entry.IsDirectory Then
                    Return SlotVerdict.ForState(SlotState.Invalid, nowUtc, "the path names a file")
                End If
                Return SlotVerdict.ForState(SlotState.Ready, nowUtc)
            Case StatOutcome.Denied
                Return Reasoned(SlotVerdict.ForState(SlotState.Denied, nowUtc, "folder: " & folder.Failure?.ToString()), folder.Failure)
            Case StatOutcome.Failed
                Return Reasoned(SlotVerdict.ForState(StateFor(folder.Failure), nowUtc, "folder: " & folder.Failure?.ToString()), folder.Failure)
        End Select

        ' Missing: the parent is the honest witness.
        If parent Is Nothing Then Return SlotVerdict.ForState(SlotState.Missing, nowUtc)
        Select Case parent.Outcome
            Case StatOutcome.Found
                If parent.Entry IsNot Nothing AndAlso Not parent.Entry.IsDirectory Then
                    Return SlotVerdict.ForState(SlotState.Invalid, nowUtc, "parent is a file")
                End If
                Return SlotVerdict.ForState(If(allowCreate AndAlso canCreateFolder, SlotState.WillBeCreated, SlotState.Missing),
                                            nowUtc, "leaf missing, parent reachable")
            Case StatOutcome.Missing
                Return SlotVerdict.ForState(SlotState.Missing, nowUtc)
            Case StatOutcome.Denied
                Return Reasoned(SlotVerdict.ForState(SlotState.Denied, nowUtc, "parent: " & parent.Failure?.ToString()), parent.Failure)
            Case Else
                Return Reasoned(SlotVerdict.ForState(StateFor(parent.Failure), nowUtc, "parent: " & parent.Failure?.ToString()), parent.Failure)
        End Select
    End Function

    ''' <summary>The verdict with the server's own reason as a finished sentence, so the status line can say
    ''' "the server did not accept the login" instead of a bare "denied". The sentence comes from
    ''' <see cref="ResourceMessages"/> - it is chosen by the failure KIND, never read from an exception's text.</summary>
    Friend Function Reasoned(verdict As SlotVerdict, failure As ResourceFailure) As SlotVerdict
        If failure Is Nothing Then Return verdict
        Return verdict.WithReason(ResourceMessages.ForFailure(failure))
    End Function

End Module

''' <summary>
''' The connections behind recipient slots that name a saved resource (007 §4.2) - one per resource,
''' made on first need and kept for the run, separate from the session of whatever resource is open
''' as the CURRENT folder: a slot is a destination, and closing the folder on screen must not take the
''' destination away from a transfer still in the queue.
'''
''' It never asks the user anything. A slot is a key pressed in the middle of sorting, so everything
''' that needs a person happens before a probe starts, on the UI thread (Main_Form.SlotHealth.vb):
''' the password is asked there and handed in through <see cref="RememberCredential"/>, and the trust
''' question is never asked from a slot at all - a server whose key nobody has verified is refused
''' (<see cref="RemoteSlotProblem.NotPinned"/>) until it has been opened once as a resource.
'''
''' Thread-safe: the probe, the file-operation worker and the batch planner all reach it off the UI
''' thread. A password typed here lives in memory for the run only, like the manager's.
''' </summary>
Friend NotInheritable Class RemoteSlotConnections
    Implements IDisposable

    ''' <summary>The instance the viewer runs with; the routing of FileManager's verbs reads it from the
    ''' worker thread. Nothing until the viewer sets it - a test sets its own.</summary>
    Friend Shared Property Current As RemoteSlotConnections

    ''' <summary>Opens one connection: the descriptor, the password, a cancel. The viewer passes
    ''' <see cref="ResourceConnect"/> with a trust prompt that always says no and a pin store that never
    ''' stores - a slot only ever talks to a server whose key is already pinned.</summary>
    Friend Delegate Function Dialer(descriptor As ResourceDescriptor, password As String, ct As CancellationToken) As ConnectOutcome

    Private ReadOnly load_Resources As Func(Of List(Of ResourceDescriptor))
    Private ReadOnly stored_Credential As Func(Of ResourceDescriptor, String)
    Private ReadOnly dial As Dialer
    Private ReadOnly acquire_Smb As Func(Of String, String, String, ResourceResult(Of SmbConnectionLease))

    Private ReadOnly gate As New Object()
    Private ReadOnly typed_Credentials As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly refused_Credentials As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly connections As New Dictionary(Of String, IFileSystemProvider)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly dial_Locks As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly smb_Leases As New Dictionary(Of String, IDisposable)(StringComparer.OrdinalIgnoreCase)
    Private is_Disposed As Boolean

    ''' <param name="loadResources">The saved resources, read afresh each time - an edit in the
    ''' manager must be seen at once.</param>
    ''' <param name="storedCredential">The credential kept for a resource (DPAPI), or Nothing.</param>
    ''' <param name="acquireSmb">The SMB session for a share (root, user, password); Nothing in tests
    ''' that have no share.</param>
    Friend Sub New(loadResources As Func(Of List(Of ResourceDescriptor)),
                   storedCredential As Func(Of ResourceDescriptor, String),
                   dial As Dialer,
                   Optional acquireSmb As Func(Of String, String, String, ResourceResult(Of SmbConnectionLease)) = Nothing)
        If loadResources Is Nothing Then Throw New ArgumentNullException(NameOf(loadResources))
        If dial Is Nothing Then Throw New ArgumentNullException(NameOf(dial))
        load_Resources = loadResources
        stored_Credential = If(storedCredential, Function(d As ResourceDescriptor) CType(Nothing, String))
        Me.dial = dial
        acquire_Smb = acquireSmb
    End Sub

    ' ------------------------------------------------------------ what a slot names ----

    ''' <summary>
    ''' The saved resource a slot path belongs to, and why it cannot be used yet if it cannot. A
    ''' locator matches through <see cref="ResourceMatching.FindForAddress"/> - the same rule a typed
    ''' address follows. A UNC path matches a saved credentialed share whose root it is under; any
    ''' other path is <see cref="RemoteSlotProblem.NotRemote"/>.
    ''' </summary>
    Friend Function Locate(slotPath As String, ByRef descriptor As ResourceDescriptor,
                           ByRef remotePath As String) As RemoteSlotProblem
        descriptor = Nothing
        remotePath = ""
        If String.IsNullOrWhiteSpace(slotPath) Then Return RemoteSlotProblem.Invalid
        Dim trimmed As String = slotPath.Trim()

        If ResourceLocator.IsLocator(trimmed) Then
            Dim user As String = "", host As String = "", port As Integer = 0, path As String = "/"
            If Not ResourceLocator.TryParse(trimmed, user, host, port, path) Then Return RemoteSlotProblem.Invalid
            remotePath = path
            descriptor = ResourceMatching.FindForAddress(LoadResources(), host, port, user, path)
            If descriptor Is Nothing Then Return RemoteSlotProblem.UnknownResource
            If String.IsNullOrEmpty(descriptor.PinnedFingerprint) Then Return RemoteSlotProblem.NotPinned
        Else
            descriptor = SmbResourceFor(trimmed)
            If descriptor Is Nothing Then Return RemoteSlotProblem.NotRemote
            remotePath = trimmed
        End If

        If descriptor.ReadOnlyHint Then Return RemoteSlotProblem.ReadOnlyResource
        If Not HasCredential(descriptor) Then Return RemoteSlotProblem.NoCredential
        Return RemoteSlotProblem.None
    End Function

    ''' <summary>The saved credentialed share a UNC path is under: the deepest root wins.</summary>
    Private Function SmbResourceFor(uncPath As String) As ResourceDescriptor
        If Not uncPath.StartsWith("\\", StringComparison.Ordinal) Then Return Nothing
        Dim best As ResourceDescriptor = Nothing
        For Each r As ResourceDescriptor In LoadResources()
            If r Is Nothing OrElse r.Kind <> ResourceKind.Smb OrElse String.IsNullOrWhiteSpace(r.RootPath) Then Continue For
            Dim root As String = r.RootPath.TrimEnd("\"c)
            Dim under As Boolean = String.Equals(uncPath.TrimEnd("\"c), root, StringComparison.OrdinalIgnoreCase) OrElse
                                   uncPath.StartsWith(root & "\", StringComparison.OrdinalIgnoreCase)
            If under AndAlso (best Is Nothing OrElse root.Length > best.RootPath.TrimEnd("\"c).Length) Then best = r
        Next
        Return best
    End Function

    Private Function LoadResources() As List(Of ResourceDescriptor)
        Try
            Return If(load_Resources(), New List(Of ResourceDescriptor)())
        Catch ex As Exception
            AppFileLogger.WriteLine("Remote slot: the resource list could not be read - " & ex.GetType().Name)
            Return New List(Of ResourceDescriptor)()
        End Try
    End Function

    ''' <summary>The name the overlay and the menus show for a slot path - "«Name» /folder" for a
    ''' server folder, "" for anything else (the caller then shows the path as before).</summary>
    Friend Function Caption(slotPath As String) As String
        If Not ResourceLocator.IsLocator(slotPath) Then Return ""
        Dim descriptor As ResourceDescriptor = Nothing
        Dim remotePath As String = ""
        Locate(slotPath, descriptor, remotePath)
        If descriptor Is Nothing Then Return ""
        Return "«" & descriptor.DisplayName & "» " & remotePath
    End Function

    ' ------------------------------------------------------------------ credentials ----

    ''' <summary>Is there a credential worth trying? Not the stored one once the server has refused
    ''' it this run: that one is asked for again (the trust in it is gone until the user types).</summary>
    Friend Function HasCredential(descriptor As ResourceDescriptor) As Boolean
        Return CredentialOf(descriptor) IsNot Nothing
    End Function

    ''' <summary>A password typed for a slot, kept in memory for the run - never written here; the
    ''' caller stores it through DPAPI when the user asked to remember it.</summary>
    Friend Sub RememberCredential(resourceId As String, password As String)
        If String.IsNullOrEmpty(resourceId) Then Return
        SyncLock gate
            typed_Credentials(resourceId) = If(password, "")
            refused_Credentials.Remove(resourceId)
        End SyncLock
    End Sub

    ''' <summary>Drops a credential the server refused, and the connection made with it - the next
    ''' press asks again instead of trying the same wrong password forever. A stored credential is
    ''' not deleted (the manager owns it); it is only not offered again this run.</summary>
    Friend Sub ForgetCredential(resourceId As String)
        If String.IsNullOrEmpty(resourceId) Then Return
        Dim dropped As IFileSystemProvider = Nothing
        SyncLock gate
            typed_Credentials.Remove(resourceId)
            refused_Credentials.Add(resourceId)
            If connections.TryGetValue(resourceId, dropped) Then connections.Remove(resourceId)
        End SyncLock
        TryCast(dropped, IDisposable)?.Dispose()
    End Sub

    ''' <summary>Did the server refuse this resource's credential this run (and nothing new was typed)?</summary>
    Friend Function WasRefused(resourceId As String) As Boolean
        If String.IsNullOrEmpty(resourceId) Then Return False
        SyncLock gate
            Return refused_Credentials.Contains(resourceId)
        End SyncLock
    End Function

    ''' <summary>Is there a working connection (or share session) for this resource - the proof a
    ''' typed password may now be remembered?</summary>
    Friend Function IsConnected(resourceId As String) As Boolean
        If String.IsNullOrEmpty(resourceId) Then Return False
        SyncLock gate
            Return connections.ContainsKey(resourceId) OrElse smb_Leases.ContainsKey(resourceId)
        End SyncLock
    End Function

    Private Function CredentialOf(descriptor As ResourceDescriptor) As String
        If descriptor Is Nothing Then Return Nothing
        SyncLock gate
            Dim typed As String = Nothing
            If typed_Credentials.TryGetValue(descriptor.Id, typed) Then Return typed
            If refused_Credentials.Contains(descriptor.Id) Then Return Nothing
        End SyncLock
        Return StoredCredentialOf(descriptor)
    End Function

    Private Function StoredCredentialOf(descriptor As ResourceDescriptor) As String
        Try
            Return stored_Credential(descriptor)
        Catch ex As Exception
            AppFileLogger.WriteLine("Remote slot: the stored credential could not be read - " & ex.GetType().Name)
            Return Nothing
        End Try
    End Function

    ' ------------------------------------------------------------------ connections ----

    ''' <summary>
    ''' The transfer endpoint behind a file locator: the connected provider of its resource and the
    ''' file's path on the server. Connects when there is no connection yet. Throws a
    ''' <see cref="ResourceFailureException"/> carrying the class of what went wrong.
    ''' </summary>
    Friend Function Endpoint(fileLocator As String, Optional ct As CancellationToken = Nothing) As TransferEndpoint
        Dim descriptor As ResourceDescriptor = Nothing
        Dim remotePath As String = ""
        If Not ResourceLocator.IsLocator(fileLocator) Then
            Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "not a server address"))
        End If
        Select Case Locate(fileLocator, descriptor, remotePath)
            Case RemoteSlotProblem.None, RemoteSlotProblem.ReadOnlyResource
                ' A read-only resource still connects: reading out of it (an undo bringing a file
                ' back) is allowed, and a write is refused by the provider's own capabilities.
            Case RemoteSlotProblem.NoCredential
                Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth, Nothing, "no credential"))
            Case Else
                Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "resource not usable as a slot"))
        End Select
        Return New TransferEndpoint(ProviderFor(descriptor, ct), remotePath, descriptor.Id)
    End Function

    ''' <summary>The connection for a resource, dialled at most once at a time.</summary>
    Private Function ProviderFor(descriptor As ResourceDescriptor, ct As CancellationToken) As IFileSystemProvider
        Dim dialLock As Object = Nothing
        SyncLock gate
            If is_Disposed Then Throw New ObjectDisposedException(NameOf(RemoteSlotConnections))
            Dim existing As IFileSystemProvider = Nothing
            If connections.TryGetValue(descriptor.Id, existing) Then Return existing
            If Not dial_Locks.TryGetValue(descriptor.Id, dialLock) Then
                dialLock = New Object()
                dial_Locks(descriptor.Id) = dialLock
            End If
        End SyncLock

        SyncLock dialLock
            SyncLock gate
                Dim raced As IFileSystemProvider = Nothing
                If connections.TryGetValue(descriptor.Id, raced) Then Return raced
            End SyncLock

            Dim password As String = CredentialOf(descriptor)
            If password Is Nothing Then Throw New ResourceFailureException(New ResourceFailure(ResourceFailureKind.Auth, Nothing, "no credential"))
            Dim outcome As ConnectOutcome = dial(descriptor, password, ct)
            If outcome Is Nothing OrElse Not outcome.Succeeded Then
                Dim failure As ResourceFailure = If(outcome?.Failure, New ResourceFailure(ResourceFailureKind.Transport))
                If failure.Kind = ResourceFailureKind.Auth Then ForgetCredential(descriptor.Id)
                Throw New ResourceFailureException(failure)
            End If

            SyncLock gate
                If is_Disposed Then
                    TryCast(outcome.Provider, IDisposable)?.Dispose()
                    Throw New ObjectDisposedException(NameOf(RemoteSlotConnections))
                End If
                connections(descriptor.Id) = outcome.Provider
            End SyncLock
            Return outcome.Provider
        End SyncLock
    End Function

    ''' <summary>
    ''' Makes sure this process has a session on the credentialed share a UNC slot is under, so the
    ''' plain-folder probe and the plain file operations that follow work unchanged (007 §5). Nothing
    ''' for a path that is not under a saved share; the failure's class when the share said no.
    ''' </summary>
    Friend Function EnsureSmbSession(uncPath As String) As ResourceFailure
        If acquire_Smb Is Nothing OrElse ResourceLocator.IsLocator(uncPath) Then Return Nothing
        Dim descriptor As ResourceDescriptor = Nothing
        Dim ignored As String = ""
        Dim problem As RemoteSlotProblem = Locate(uncPath, descriptor, ignored)
        If problem = RemoteSlotProblem.NotRemote OrElse descriptor Is Nothing Then Return Nothing
        SyncLock gate
            If smb_Leases.ContainsKey(descriptor.Id) Then Return Nothing
        End SyncLock
        Dim password As String = CredentialOf(descriptor)
        If password Is Nothing Then Return New ResourceFailure(ResourceFailureKind.Auth, Nothing, "no credential")

        Dim lease As ResourceResult(Of SmbConnectionLease)
        Try
            lease = acquire_Smb(descriptor.RootPath, descriptor.UserName, password)
        Catch ex As Exception
            Return ResourceFailure.FromException(ex)
        End Try
        If Not lease.Succeeded Then
            If lease.Failure.Kind = ResourceFailureKind.Auth Then ForgetCredential(descriptor.Id)
            Return lease.Failure
        End If
        SyncLock gate
            If is_Disposed OrElse smb_Leases.ContainsKey(descriptor.Id) Then
                lease.Value.Dispose()
            Else
                smb_Leases(descriptor.Id) = lease.Value
            End If
        End SyncLock
        Return Nothing
    End Function

    ' ------------------------------------------------------------------ the questions ----

    ''' <summary>
    ''' The probe of a server folder (007 §4.2): connect if needed, then ask the folder - and its
    ''' parent when the folder is missing - with one stat each. One round trip on a live connection.
    ''' </summary>
    Friend Function Probe(folderLocator As String, allowCreate As Boolean, Optional ct As CancellationToken = Nothing) As SlotVerdict
        Dim now_Utc As DateTime = DateTime.UtcNow
        Dim descriptor As ResourceDescriptor = Nothing
        Dim remotePath As String = ""
        Select Case Locate(folderLocator, descriptor, remotePath)
            Case RemoteSlotProblem.None
            Case RemoteSlotProblem.Invalid, RemoteSlotProblem.NotRemote
                Return SlotVerdict.ForState(SlotState.Invalid, now_Utc, "invalid resource locator")
            Case RemoteSlotProblem.ReadOnlyResource
                Return SlotVerdict.ForState(SlotState.Denied, now_Utc, "read-only resource")
            Case Else
                Return SlotVerdict.ForState(SlotState.Denied, now_Utc, "resource not usable as a slot")
        End Select

        Dim provider As IFileSystemProvider
        Try
            provider = ProviderFor(descriptor, ct)
        Catch ex As Exception
            Dim failure As ResourceFailure = ResourceFailure.FromException(ex)
            Return RemoteSlotPolicy.Reasoned(SlotVerdict.ForState(RemoteSlotPolicy.StateFor(failure), now_Utc, "connect: " & failure.ToString()), failure)
        End Try

        Dim folder As StatResult = ResourceProviderCalls.Stat(provider, remotePath)
        Dim parent As StatResult = Nothing
        If folder.Outcome = StatOutcome.Missing Then
            Dim above As String = ResourceTransfer.ParentOf(remotePath)
            If Not String.IsNullOrEmpty(above) AndAlso Not String.Equals(above, remotePath, StringComparison.Ordinal) Then
                parent = ResourceProviderCalls.Stat(provider, above)
            End If
        End If
        If folder.Outcome = StatOutcome.Failed AndAlso folder.Failure IsNot Nothing AndAlso
           folder.Failure.Kind = ResourceFailureKind.LocalIO Then
            ' A provider that was disposed under us (the manager removed the resource): drop it, so
            ' the next probe dials afresh rather than asking a dead connection forever.
            DropConnection(descriptor.Id)
        End If
        Return RemoteSlotPolicy.Verdict(folder, parent, provider.Capabilities.CanWrite,
                                        provider.Capabilities.CanCreateFolder, allowCreate, now_Utc)
    End Function

    ''' <summary>Is there a file at <paramref name="fileLocator"/>? Throws when the server could not
    ''' say - an overwrite decision must never rest on an error read as "free".</summary>
    Friend Function FileExists(fileLocator As String, Optional ct As CancellationToken = Nothing) As Boolean
        Dim target As TransferEndpoint = Endpoint(fileLocator, ct)
        Dim result As StatResult = ResourceProviderCalls.Stat(target.Provider, target.Path)
        Select Case result.Outcome
            Case StatOutcome.Found : Return True
            Case StatOutcome.Missing : Return False
            Case Else : Throw New ResourceFailureException(result.Failure)
        End Select
    End Function

    ''' <summary>Creates the LAST segment of a server folder when it is missing (D6 - the probe found
    ''' the parent answering). True when it was created by this call.</summary>
    Friend Function EnsureFolder(folderLocator As String, Optional ct As CancellationToken = Nothing) As Boolean
        Dim target As TransferEndpoint = Endpoint(folderLocator, ct)
        Dim result As StatResult = ResourceProviderCalls.Stat(target.Provider, target.Path)
        Select Case result.Outcome
            Case StatOutcome.Found : Return False
            Case StatOutcome.Missing
                target.Provider.CreateDirectory(target.Path)
                Return True
            Case Else
                Throw New ResourceFailureException(result.Failure)
        End Select
    End Function

    ''' <summary>Deletes the file at a locator - an undo of a copy, through the provider.</summary>
    Friend Sub DeleteFile(fileLocator As String, Optional ct As CancellationToken = Nothing)
        Dim target As TransferEndpoint = Endpoint(fileLocator, ct)
        target.Provider.DeleteFile(target.Path)
    End Sub

    Private Sub DropConnection(resourceId As String)
        Dim dropped As IFileSystemProvider = Nothing
        SyncLock gate
            If connections.TryGetValue(resourceId, dropped) Then connections.Remove(resourceId)
        End SyncLock
        TryCast(dropped, IDisposable)?.Dispose()
    End Sub

    ''' <summary>
    ''' Lets go of every connection - after the resource list was edited, so a changed address, key or
    ''' credential is dialled afresh. Typed credentials are kept: they are the user's answer for the run.
    ''' A transfer still holding an old provider fails with its own error and is reported as one.
    ''' </summary>
    Friend Sub Reset()
        Dim dropped As List(Of IDisposable)
        SyncLock gate
            dropped = connections.Values.OfType(Of IDisposable)().ToList()
            dropped.AddRange(smb_Leases.Values)
            connections.Clear()
            smb_Leases.Clear()
        End SyncLock
        For Each item As IDisposable In dropped
            Try
                item.Dispose()
            Catch ex As Exception
                AppFileLogger.WriteLine("Remote slot: closing a connection failed - " & ex.GetType().Name)
            End Try
        Next
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            If is_Disposed Then Return
            is_Disposed = True
        End SyncLock
        Reset()
        SyncLock gate
            typed_Credentials.Clear()
        End SyncLock
    End Sub

End Class
#End If
