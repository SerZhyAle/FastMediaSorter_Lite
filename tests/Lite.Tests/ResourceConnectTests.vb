#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 007 §2.2 / §3.3: how a resource is opened - the race over its addresses, what ends it, and where a
''' fingerprint the user accepted is written. A scripted dialer stands in for SSH, so what is asserted is
''' the policy (order, fatal versus retryable, one trust prompt) and never a socket.
''' </summary>
Public Class ResourceConnectTests

    Private Const PinA As String = "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk"

    ''' <summary>A connected session that is also a provider, so the outcome can hand it back as one.</summary>
    Private NotInheritable Class FakeSession
        Implements IFileSystemProvider
        Implements IDisposable

        Friend Disposed As Boolean
        Friend ReadOnly Endpoint As FmsEndpoint

        Friend Sub New(endpoint As FmsEndpoint)
            Me.Endpoint = endpoint
        End Sub

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property
        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Return New ProviderCapabilities()
            End Get
        End Property
        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Return New List(Of ResourceEntry)()
        End Function
        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            entry = Nothing
            Return False
        End Function
        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            Throw New NotSupportedException()
        End Sub
        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            Throw New NotSupportedException()
        End Sub
        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            Throw New NotSupportedException()
        End Sub
        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Throw New NotSupportedException()
        End Sub
        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Throw New NotSupportedException()
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    Private NotInheritable Class ScriptedDialer
        Implements IEndpointDialer

        Friend ReadOnly Dialled As New List(Of String)()
        Friend ReadOnly Script As New Dictionary(Of String, ResourceFailure)()
        Friend ReadOnly Sessions As New List(Of FakeSession)()

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            SyncLock Dialled
                Dialled.Add(endpoint.Key())
            End SyncLock
            Dim failure As ResourceFailure = Nothing
            If Script.TryGetValue(endpoint.Key(), failure) Then Return Task.FromResult(DialResult.Failed(failure))
            Dim session As New FakeSession(endpoint)
            SyncLock Sessions
                Sessions.Add(session)
            End SyncLock
            Return Task.FromResult(DialResult.Verified(session))
        End Function
    End Class

    Private Shared Function Resource(Optional pin As String = PinA) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "r", .Kind = ResourceKind.Sftp, .Host = "lan.invalid", .Port = 2022, .UserName = "fms",
            .RootPath = "/Photos", .PinnedFingerprint = pin,
            .Endpoints = New List(Of ResourceEndpoint) From {
                New ResourceEndpoint With {.Kind = "lan", .Host = "lan.invalid", .Port = 2022},
                New ResourceEndpoint With {.Kind = "portforward", .Host = "wan.invalid", .Port = 55001}}}
    End Function

    Private Shared Function Run(descriptor As ResourceDescriptor, dialer As IEndpointDialer) As ConnectOutcome
        Return ResourceConnect.ConnectAsync(descriptor, "pw", Nothing, Nothing, Nothing, CancellationToken.None, dialer).GetAwaiter().GetResult()
    End Function

    ' ----------------------------------------------------------------------- candidates ----

    <Fact>
    Public Sub Candidates_AResourceAddedByHand_HasExactlyItsOwnAddress_AndTheDefaultPort()
        Dim list As List(Of FmsEndpoint) = ResourceConnect.CandidatesOf(New ResourceDescriptor With {
            .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 0})

        Dim only As FmsEndpoint = Assert.Single(list)
        Assert.Equal("nas.local", only.Host)
        Assert.Equal(22, only.Port)
    End Sub

    <Fact>
    Public Sub Candidates_AnImportedResource_NamesItsOwnAddressFirst_ThenTheProducersOrder_WithoutDuplicates()
        Dim candidates As List(Of FmsEndpoint) = EndpointResolver.PlanCandidates(ResourceConnect.CandidatesOf(Resource()))

        ' The primary duplicates the first stored address and is dropped by the planner, never dialled twice.
        Assert.Equal({"lan.invalid:2022", "wan.invalid:55001"}, candidates.Select(Function(c) c.Key()).ToArray())
    End Sub

    ' --------------------------------------------------------------------------- race ----

    <Fact>
    Public Sub Connect_UsesTheFirstAddressThatAnswers_AndNeverDialsTheRest()
        Dim dialer As New ScriptedDialer()
        Dim outcome As ConnectOutcome = Run(Resource(), dialer)

        Assert.True(outcome.Succeeded)
        Assert.Equal("lan.invalid:2022", DirectCast(outcome.Provider, FakeSession).Endpoint.Key())
        Assert.DoesNotContain("wan.invalid:55001", dialer.Dialled)
    End Sub

    <Fact>
    Public Sub Connect_AStaleLanAddress_FallsThroughToThePortForward()
        Dim dialer As New ScriptedDialer()
        dialer.Script("lan.invalid:2022") = New ResourceFailure(ResourceFailureKind.Transport)

        Dim outcome As ConnectOutcome = Run(Resource(), dialer)

        Assert.True(outcome.Succeeded)
        Assert.Equal("wan.invalid:55001", DirectCast(outcome.Provider, FakeSession).Endpoint.Key())
    End Sub

    <Fact>
    Public Sub Connect_AWrongCredential_EndsTheWholeRound_AndIsNotRetriedOnAnotherAddress()
        Dim dialer As New ScriptedDialer()
        dialer.Script("lan.invalid:2022") = New ResourceFailure(ResourceFailureKind.Auth)

        Dim outcome As ConnectOutcome = Run(Resource(), dialer)

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, outcome.Failure.Kind)
        ' The same wrong password sent to a second address would count against the server's lockout.
        Assert.DoesNotContain("wan.invalid:55001", dialer.Dialled)
    End Sub

    <Fact>
    Public Sub Connect_AHostKeyThatIsNotThePin_EndsTheRound()
        Dim dialer As New ScriptedDialer()
        dialer.Script("lan.invalid:2022") = New ResourceFailure(ResourceFailureKind.KeyMismatch)

        Dim outcome As ConnectOutcome = Run(Resource(), dialer)

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.KeyMismatch, outcome.Failure.Kind)
        Assert.DoesNotContain("wan.invalid:55001", dialer.Dialled)
    End Sub

    <Fact>
    Public Sub Connect_EveryAddressDown_ReportsTheLastFailure_AndOpensNothing()
        Dim dialer As New ScriptedDialer()
        dialer.Script("lan.invalid:2022") = New ResourceFailure(ResourceFailureKind.Transport)
        dialer.Script("wan.invalid:55001") = New ResourceFailure(ResourceFailureKind.Timeout)

        Dim outcome As ConnectOutcome = Run(Resource(), dialer)

        Assert.False(outcome.Succeeded)
        Assert.Null(outcome.Provider)
        Assert.True(outcome.Failure.IsRetryable)
        Assert.Equal(2, dialer.Dialled.Count)
    End Sub

    <Fact>
    Public Sub Connect_AnUnpinnedServer_IsDialledOneAddressAtATime()
        ' One handshake in flight means one trust prompt: the lane count is 1 until a pin exists.
        Dim dialer As New ScriptedDialer()
        dialer.Script("lan.invalid:2022") = New ResourceFailure(ResourceFailureKind.Transport)

        Dim outcome As ConnectOutcome = Run(Resource(pin:=""), dialer)

        Assert.True(outcome.Succeeded)
        Assert.Equal({"lan.invalid:2022", "wan.invalid:55001"}, dialer.Dialled.ToArray())
    End Sub

    <Fact>
    Public Sub Connect_AFileShareKindThatIsNotAResource_IsRefusedByNameNotThrown()
        Dim outcome As ConnectOutcome = Run(New ResourceDescriptor With {.Id = "x", .Kind = ResourceKind.Local, .RootPath = "C:\"}, New ScriptedDialer())

        Assert.False(outcome.Succeeded)
        Assert.Equal(ResourceFailureKind.Unsupported, outcome.Failure.Kind)
    End Sub

    ' ---------------------------------------------------------------------------- pins ----

    Private NotInheritable Class Hive
        Friend ReadOnly Values As New Dictionary(Of String, String)()
        Friend FailWrites As Boolean
        Friend Function Read(name As String) As String
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function
        Friend Sub Write(name As String, value As String)
            If FailWrites Then Throw New System.IO.IOException("locked")
            Values(name) = value
        End Sub
    End Class

    <Fact>
    Public Sub Pins_AnAcceptedKey_IsWrittenForTheResourceAndItsSiblingsOfTheSameServer()
        Dim hive As New Hive()
        Dim a As ResourceDescriptor = Resource(pin:="")
        Dim sibling As ResourceDescriptor = Resource(pin:="")
        sibling.Id = "r2"
        sibling.RootPath = "/Video"
        Dim stranger As ResourceDescriptor = Resource(pin:="")
        stranger.Id = "r3"
        stranger.Host = "other.invalid"
        stranger.Endpoints = Nothing
        hive.Write("Resources", ResourceRegistry.Serialize({a, sibling, stranger}))

        Assert.True(ResourcePins.Persist(a, PinA, AddressOf hive.Read, AddressOf hive.Write))

        Dim stored As List(Of ResourceDescriptor) = ResourceRegistry.Parse(hive.Read("Resources"))
        Assert.Equal(PinA, stored.Single(Function(d) d.Id = "r").PinnedFingerprint)
        Assert.Equal(PinA, stored.Single(Function(d) d.Id = "r2").PinnedFingerprint)
        Assert.Equal("", stored.Single(Function(d) d.Id = "r3").PinnedFingerprint)
        Assert.Equal(PinA, a.PinnedFingerprint)
    End Sub

    <Fact>
    Public Sub Pins_AKeyAlreadyPinnedElsewhere_IsNeverOverwritten()
        Dim hive As New Hive()
        Dim a As ResourceDescriptor = Resource(pin:="")
        Dim pinned As ResourceDescriptor = Resource(pin:="SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")
        pinned.Id = "r2"
        hive.Write("Resources", ResourceRegistry.Serialize({a, pinned}))

        Assert.True(ResourcePins.Persist(a, PinA, AddressOf hive.Read, AddressOf hive.Write))

        Dim stored As List(Of ResourceDescriptor) = ResourceRegistry.Parse(hive.Read("Resources"))
        Assert.Equal("SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", stored.Single(Function(d) d.Id = "r2").PinnedFingerprint)
    End Sub

    <Fact>
    Public Sub Pins_ARegistryThatCannotBeWritten_ReportsFalse_SoAuthenticationDoesNotProceed()
        Dim hive As New Hive()
        Dim a As ResourceDescriptor = Resource(pin:="")
        hive.Write("Resources", ResourceRegistry.Serialize({a}))
        hive.FailWrites = True

        Assert.False(ResourcePins.Persist(a, PinA, AddressOf hive.Read, AddressOf hive.Write))
        Assert.Equal("", a.PinnedFingerprint)
    End Sub

    <Fact>
    Public Sub Pins_AResourceNotInTheListYet_IsPinnedInMemoryOnly()
        Dim hive As New Hive()
        Dim draft As ResourceDescriptor = Resource(pin:="")
        draft.Id = "draft"

        Assert.True(ResourcePins.Persist(draft, PinA, AddressOf hive.Read, AddressOf hive.Write))

        Assert.Equal(PinA, draft.PinnedFingerprint)
        Assert.Empty(hive.Values)
    End Sub

    <Fact>
    Public Sub Pins_AnUnusableFingerprint_IsRefused()
        Dim hive As New Hive()
        Assert.False(ResourcePins.Persist(Resource(pin:=""), "not-a-fingerprint", AddressOf hive.Read, AddressOf hive.Write))
    End Sub

End Class
#End If
