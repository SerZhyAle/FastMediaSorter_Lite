#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Dials ONE endpoint of an SFTP resource for <see cref="EndpointResolver"/>: connects, lets the
''' <see cref="HostKeyGate"/> judge the key BEFORE authentication, authenticates, and hands back the
''' connected provider. TCP reachability alone is never a success - the resolver's contract is a
''' VERIFIED session. One attempt, no backoff (<see cref="SftpProvider.ConnectOnce"/>): the race has its
''' own budget, and the reconnect with backoff belongs to a session that is already open.
''' </summary>
Friend NotInheritable Class SftpEndpointDialer
    Implements IEndpointDialer

    Private ReadOnly descriptor As ResourceDescriptor
    Private ReadOnly password As String
    Private ReadOnly gate As HostKeyGate
    Private ReadOnly includeSubfolders As Func(Of Boolean)

    Friend Sub New(descriptor As ResourceDescriptor, password As String, gate As HostKeyGate, includeSubfolders As Func(Of Boolean))
        Me.descriptor = descriptor
        Me.password = password
        Me.gate = gate
        Me.includeSubfolders = includeSubfolders
    End Sub

    Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
        Return Task.Run(Function() Dial(endpoint, ct))
    End Function

    Private Function Dial(endpoint As FmsEndpoint, ct As CancellationToken) As DialResult
        If ct.IsCancellationRequested Then Return DialResult.Failed(New ResourceFailure(ResourceFailureKind.Cancelled))
        ' The descriptor stays the resource's; only the address is this candidate's.
        Dim attempt As New ResourceDescriptor With {
            .Id = descriptor.Id, .DisplayName = descriptor.DisplayName, .Kind = ResourceKind.Sftp,
            .Host = endpoint.Host, .Port = endpoint.Port, .UserName = descriptor.UserName,
            .RootPath = descriptor.RootPath, .ReadOnlyHint = descriptor.ReadOnlyHint,
            .PinnedFingerprint = descriptor.PinnedFingerprint}
        Dim provider As SftpProvider = Nothing
        Try
            provider = New SftpProvider(attempt, password, gate, includeSubfolders:=includeSubfolders)
            provider.ConnectOnce(ct)
            If ct.IsCancellationRequested Then
                provider.Dispose()
                Return DialResult.Failed(New ResourceFailure(ResourceFailureKind.Cancelled))
            End If
            Return DialResult.Verified(provider)
        Catch ex As Exception
            provider?.Dispose()
            Return DialResult.Failed(ResourceFailure.FromException(ex))
        End Try
    End Function
End Class

''' <summary>What a connection attempt came to, with what the caller needs to explain it.</summary>
Friend NotInheritable Class ConnectOutcome
    Friend Sub New(provider As IFileSystemProvider, failure As ResourceFailure, Optional refusal As HostKeyDecision = Nothing)
        Me.Provider = provider
        Me.Failure = failure
        Me.HostKeyRefusal = refusal
    End Sub

    ''' <summary>The connected provider, or Nothing.</summary>
    Friend ReadOnly Property Provider As IFileSystemProvider
    Friend ReadOnly Property Failure As ResourceFailure
    ''' <summary>For a host-key refusal: what the pin said and what the server offered.</summary>
    Friend ReadOnly Property HostKeyRefusal As HostKeyDecision
    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Provider IsNot Nothing
        End Get
    End Property
End Class

''' <summary>
''' Opens a resource: the one place that turns a <see cref="ResourceDescriptor"/> and a credential into a
''' connected <see cref="IFileSystemProvider"/> (007 §2.2, §5). Everything that needs the user - the
''' trust prompt - comes in as a delegate, so the logic is the same for the viewer, the manager's
''' "Test" and the tests, and none of it touches a form.
''' </summary>
Friend Module ResourceConnect

    ''' <param name="consent">Asked once, for an unpinned server, with the offered fingerprint; True only on
    ''' an explicit accept. Cancel is the dialog's default (100 §14.1).</param>
    ''' <param name="persistPin">Stores an accepted fingerprint BEFORE authentication proceeds. False means it
    ''' was not stored, and the connection stops - there is no fall back to "accept any key".</param>
    Friend Async Function ConnectAsync(descriptor As ResourceDescriptor, password As String,
                                       consent As Func(Of String, CancellationToken, Boolean),
                                       persistPin As Func(Of String, Boolean),
                                       includeSubfolders As Func(Of Boolean),
                                       ct As CancellationToken,
                                       Optional dialerOverride As IEndpointDialer = Nothing,
                                       Optional tunnelSource As IExchangeTunnelSource = Nothing) As Task(Of ConnectOutcome)
        If descriptor Is Nothing Then Throw New ArgumentNullException(NameOf(descriptor))
        Select Case descriptor.Kind
            Case ResourceKind.Sftp
                Return Await ConnectSftpAsync(descriptor, password, consent, persistPin, includeSubfolders, ct, dialerOverride, tunnelSource).ConfigureAwait(False)
            Case ResourceKind.Smb
                Return Await Task.Run(Function() ConnectSmb(descriptor, password, includeSubfolders)).ConfigureAwait(False)
            Case Else
                Return New ConnectOutcome(Nothing, New ResourceFailure(ResourceFailureKind.Unsupported))
        End Select
    End Function

    Private Async Function ConnectSftpAsync(descriptor As ResourceDescriptor, password As String,
                                            consent As Func(Of String, CancellationToken, Boolean),
                                            persistPin As Func(Of String, Boolean),
                                            includeSubfolders As Func(Of Boolean),
                                            ct As CancellationToken,
                                            dialerOverride As IEndpointDialer,
                                            tunnelSource As IExchangeTunnelSource) As Task(Of ConnectOutcome)
        Dim clock As New SystemResolutionClock()
        Dim gate As New HostKeyGate(descriptor.PinnedFingerprint, consent, persistPin, Function() clock.NowMs)
        Dim dialer As IEndpointDialer = If(dialerOverride, New SftpEndpointDialer(descriptor, password, gate, includeSubfolders))

        Dim candidates As List(Of FmsEndpoint) = EndpointResolver.PlanCandidates(CandidatesOf(descriptor))
        If tunnelSource IsNot Nothing Then
            ' ticket 171 rule 6: the tunnel is one more candidate, LAST, with the longest head-start for the direct ones; dialling it is what
            ' creates the forwarder and asks the exchange for a stream, so a round that a direct address wins never touches the exchange
            candidates.Add(ExchangeTunnelRules.Candidate())
            dialer = New ExchangeTunnelDialer(dialer, tunnelSource)
        End If
        ' One handshake at a time while the server is unpinned: one trust prompt, never two.
        Dim lanes As Integer = If(String.IsNullOrEmpty(descriptor.PinnedFingerprint), 1, ResolutionLimits.MaxLanes)
        Dim outcome As ResolutionOutcome = Await EndpointResolver.ResolveAsync(
            candidates, dialer, clock, ct, lanes, AddressOf gate.ConsentMsSoFar).ConfigureAwait(False)

        If outcome.Succeeded Then
            Return New ConnectOutcome(DirectCast(outcome.Session, IFileSystemProvider), Nothing)
        End If
        Dim failure As ResourceFailure = If(outcome.Failure, New ResourceFailure(ResourceFailureKind.Transport))
        Return New ConnectOutcome(Nothing, failure, gate.LastRefusal)
    End Function

    ''' <summary>The addresses to race: the imported ones in the producer's order, or the one the user typed.</summary>
    Friend Function CandidatesOf(descriptor As ResourceDescriptor) As List(Of FmsEndpoint)
        Dim list As New List(Of FmsEndpoint)()
        Dim port As Integer = If(descriptor.Port > 0, descriptor.Port, 22)
        If descriptor.Endpoints IsNot Nothing AndAlso descriptor.Endpoints.Count > 0 Then
            ' The address the resource is named by goes first; the rest keep the producer's order.
            list.Add(New FmsEndpoint With {.Kind = "primary", .Host = descriptor.Host, .Port = port})
            For Each e As ResourceEndpoint In descriptor.Endpoints
                list.Add(New FmsEndpoint With {.Kind = e.Kind, .Host = e.Host, .Port = If(e.Port > 0, e.Port, 22)})
            Next
        Else
            list.Add(New FmsEndpoint With {.Kind = "manual", .Host = descriptor.Host, .Port = port})
        End If
        Return list
    End Function

    Private Function ConnectSmb(descriptor As ResourceDescriptor, password As String, includeSubfolders As Func(Of Boolean)) As ConnectOutcome
        Dim uncRoot As String = SmbPathUtil.ExtractUncRoot(descriptor.RootPath)
        If String.IsNullOrEmpty(uncRoot) Then
            Return New ConnectOutcome(Nothing, New ResourceFailure(ResourceFailureKind.Unsupported, Nothing, "invalid UNC root"))
        End If
        Try
            Dim lease As ResourceResult(Of SmbConnectionLease) =
                SmbConnectionManager.SharedInstance.AcquireLease(uncRoot, descriptor.UserName, If(password, ""))
            If Not lease.Succeeded Then Return New ConnectOutcome(Nothing, lease.Failure)
            Return New ConnectOutcome(New SmbProvider(descriptor, lease.Value, includeSubfolders:=includeSubfolders), Nothing)
        Catch ex As Exception
            Return New ConnectOutcome(Nothing, ResourceFailure.FromException(ex))
        End Try
    End Function

End Module

''' <summary>
''' Where an accepted host key is written (007 §3.3). It sits next to the resource it protects: the same
''' server reached through another root of the same document must not ask again, and a key learned for
''' one root is never written over a different key already pinned for another.
''' </summary>
Friend Module ResourcePins

    ''' <summary>Pins <paramref name="fingerprint"/> on <paramref name="descriptor"/> and on every other
    ''' resource that is the same server and user (they share an address) and has no pin yet. A resource
    ''' that is not in the list yet (a dialog still open) is pinned in memory only. False when the
    ''' registry could not be written - the caller must then NOT authenticate.</summary>
    Friend Function Persist(descriptor As ResourceDescriptor, fingerprint As String,
                            readValue As Func(Of String, String), writeValue As Action(Of String, String)) As Boolean
        If descriptor Is Nothing OrElse String.IsNullOrEmpty(fingerprint) Then Return False
        Dim digest As Byte() = Nothing
        If Not SshFingerprint.TryParse(fingerprint, digest) Then Return False
        Try
            Dim all As List(Of ResourceDescriptor) = ResourceRegistry.Parse(readValue("Resources"))
            Dim changed As Boolean
            For Each other As ResourceDescriptor In all
                If other.Kind <> ResourceKind.Sftp OrElse Not String.IsNullOrEmpty(other.PinnedFingerprint) Then Continue For
                Dim same As Boolean = String.Equals(other.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase) OrElse
                                      (String.Equals(other.UserName, descriptor.UserName, StringComparison.Ordinal) AndAlso SharesAddress(other, descriptor))
                If Not same Then Continue For
                other.PinnedFingerprint = fingerprint
                changed = True
            Next
            If changed Then ResourceRegistry.Save(all, readValue, writeValue)
            descriptor.PinnedFingerprint = fingerprint
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function SharesAddress(a As ResourceDescriptor, b As ResourceDescriptor) As Boolean
        For Each x As FmsEndpoint In ResourceConnect.CandidatesOf(a)
            For Each y As FmsEndpoint In ResourceConnect.CandidatesOf(b)
                If x.Key() = y.Key() Then Return True
            Next
        Next
        Return False
    End Function

End Module
#End If
