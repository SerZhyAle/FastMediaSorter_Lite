#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class SftpProviderTests
    Implements IDisposable

    Private ReadOnly tempDir As String

    Public Sub New()
        tempDir = Path.Combine(Path.GetTempPath(), "fms-sftp-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(tempDir) Then Directory.Delete(tempDir, recursive:=True)
        Catch
        End Try
    End Sub

    <Fact>
    Public Sub SftpProvider_Capabilities_ReflectReadOnlyHint()
        Dim readOnlyDesc As New ResourceDescriptor With {
            .Id = "sftp-ro",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .ReadOnlyHint = True
        }
        Dim roProvider As New SftpProvider(readOnlyDesc, "pass", Nothing)
        Assert.Equal(ResourceKind.Sftp, roProvider.Kind)
        Assert.False(roProvider.Capabilities.CanWrite)
        Assert.False(roProvider.Capabilities.CanDelete)
        Assert.False(roProvider.Capabilities.SupportsCheckedSourceDelete)
        Assert.False(roProvider.Capabilities.CanRename)
        Assert.False(roProvider.Capabilities.HasRecycleBin)
        Assert.True(roProvider.Capabilities.NeedsMaterialisation)
        Assert.True(roProvider.Capabilities.SupportsServerSideRename)
        Assert.Equal(ProviderCostHint.Slow, roProvider.Capabilities.CostHint)

        Dim writableDesc As New ResourceDescriptor With {
            .Id = "sftp-rw",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .ReadOnlyHint = False
        }
        Dim rwProvider As New SftpProvider(writableDesc, "pass", Nothing)
        Assert.True(rwProvider.Capabilities.CanWrite)
        Assert.True(rwProvider.Capabilities.CanDelete)
        Assert.True(rwProvider.Capabilities.SupportsCheckedSourceDelete)
        Assert.False(rwProvider.Capabilities.SupportsStableSourceDelete)
        Assert.True(rwProvider.Capabilities.CanRename)
    End Sub

    <Fact>
    Public Sub SftpResourceConnector_FailsOnMissingOrInvalidSecret()
        Dim desc As New ResourceDescriptor With {
            .Id = "sftp-1",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media",
            .SecretHandle = "cred:missing"
        }
        Dim mockSecrets As New MockSecretStore()

        ' Decryption failure
        Dim result = SftpResourceConnector.Connect(desc, mockSecrets, Nothing,
            unprotect:=Function(blob) New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, ""))
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, result.Failure.Kind)
    End Sub

    <Fact>
    Public Sub SftpProvider_TryStat_ReturnsFalseOnDisposedOrEmpty()
        Dim desc As New ResourceDescriptor With {
            .Id = "sftp-1",
            .Kind = ResourceKind.Sftp,
            .Host = "127.0.0.1",
            .Port = 22,
            .UserName = "user",
            .RootPath = "/media"
        }
        Dim provider As New SftpProvider(desc, "pass", Nothing)
        provider.Dispose()

        Dim entry As ResourceEntry = Nothing
        Assert.False(provider.TryStat("", entry))
        Assert.False(provider.TryStat("/nonexistent", entry))
    End Sub

    <Fact>
    Public Sub Missing_host_key_gate_refuses_before_any_network_attempt()
        Using provider As New SftpProvider(New ResourceDescriptor With {.Host = "unreachable.invalid", .UserName = "user"}, "pass", Nothing)
            Dim ex = Assert.Throws(Of ResourceFailureException)(Sub() provider.EnsureConnected())
            Assert.Equal(ResourceFailureKind.KeyMismatch, ex.Failure.Kind)
            Assert.False(ex.Failure.IsRetryable)
        End Using
    End Sub

    <Fact>
    Public Sub Read_only_resource_refuses_every_write_before_dialling_or_reading_local_bytes()
        Using provider As New SftpProvider(New ResourceDescriptor With {.Host = "unreachable.invalid", .ReadOnlyHint = True}, "pass", Nothing)
            Dim actions As Action() = {
                Sub() provider.Upload("missing-local-file", "/file", Nothing, CancellationToken.None),
                Sub() provider.DeleteFile("/file"),
                Sub() provider.Rename("/file", "/renamed"),
                Sub() provider.CreateDirectory("/new")}
            For Each action As Action In actions
                Dim ex = Assert.Throws(Of ResourceFailureException)(action)
                Assert.Equal(ResourceFailureKind.Unsupported, ex.Failure.Kind)
            Next
        End Using
    End Sub

    <Fact>
    Public Sub Cancelled_listing_stops_before_dialling()
        Using provider As New SftpProvider(New ResourceDescriptor With {.Host = "unreachable.invalid"}, "pass", Nothing)
            Using cancel As New CancellationTokenSource()
                cancel.Cancel()
                Dim ex = Assert.Throws(Of ResourceFailureException)(Sub() provider.ListEntries("/", cancel.Token))
                Assert.Equal(ResourceFailureKind.Cancelled, ex.Failure.Kind)
            End Using
        End Using
    End Sub

    ' ---- SHARE-SESSION 1.4 item B: the mid-session reconnect (ReconnectAllLocked) at provider level (L0335) -------------------
    ' A claim that rules 7, 8 and 10 hold on the reconnect path rests on a run, not on reading the code. These facts drive the
    ' provider's own path with an injected round: the round stands for the race over EVERY stored path (ResourceConnect builds it),
    ' so what is pinned here is that a lost connection always asks the round, drops the old connection first, and never repeats a
    ' round after a refusal. The live scenario 5 (a real worker, a wrong host key at the redial) stays a hand run.

    ''' <summary>Records the moment the old connection's lease is released, in the order the round is entered.</summary>
    Private NotInheritable Class ReleaseRecorder
        Implements IDisposable

        Private ReadOnly events As List(Of String)

        Public Sub New(events As List(Of String))
            Me.events = events
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            events.Add("released")
        End Sub
    End Class

    ''' <summary>A clock that never waits: the backoff ladder is asserted by the number of rounds, not by elapsed time.</summary>
    Private NotInheritable Class InstantClock
        Implements IResolutionClock

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                Return 0L
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
            Return Task.CompletedTask
        End Function
    End Class

    Private Shared Function NewReconnectingProvider(round As Func(Of CancellationToken, Task(Of ResourceResult(Of SftpProvider)))) As SftpProvider
        Dim gate As New HostKeyGate("", Function(fingerprint, token) False, Function(fingerprint) True, Function() 0L)
        Dim provider As New SftpProvider(New ResourceDescriptor With {.Host = "unreachable.invalid", .UserName = "user", .Port = 22}, "pass", gate)
        provider.ReconnectRound = round
        provider.ReconnectClock = New InstantClock()
        Return provider
    End Function

    <Fact>
    Public Sub Mid_session_reconnect_releases_the_old_connection_before_the_first_round()
        Dim events As New List(Of String)()
        Using provider As SftpProvider = NewReconnectingProvider(
            Function(token)
                events.Add("round")
                Return Task.FromResult(ResourceResult(Of SftpProvider).Fail(New ResourceFailure(ResourceFailureKind.Auth)))
            End Function)
            provider.OwnedResource = New ReleaseRecorder(events)
            Assert.Throws(Of ResourceFailureException)(Sub() provider.EnsureConnected())
        End Using
        Assert.Equal(New String() {"released", "round"}, events.ToArray())
    End Sub

    <Theory>
    <InlineData(ResourceFailureKind.KeyMismatch)>
    <InlineData(ResourceFailureKind.Auth)>
    Public Sub Mid_session_reconnect_never_repeats_a_round_after_a_host_key_or_credential_refusal(kind As Integer)
        Dim rounds As Integer = 0
        Using provider As SftpProvider = NewReconnectingProvider(
            Function(token)
                rounds += 1
                Return Task.FromResult(ResourceResult(Of SftpProvider).Fail(New ResourceFailure(CType(kind, ResourceFailureKind))))
            End Function)
            Dim ex = Assert.Throws(Of ResourceFailureException)(Sub() provider.EnsureConnected())
            Assert.Equal(CType(kind, ResourceFailureKind), ex.Failure.Kind)
            Assert.False(ex.Failure.IsRetryable)
        End Using
        Assert.Equal(1, rounds)
    End Sub

    <Fact>
    Public Sub Mid_session_reconnect_asks_the_round_on_every_try_and_never_dials_its_own_endpoint()
        Dim rounds As Integer = 0
        Using provider As SftpProvider = NewReconnectingProvider(
            Function(token)
                rounds += 1
                ' Timeout is the round's own verdict: a provider that dialled its own (unresolvable) host would report Transport.
                Return Task.FromResult(ResourceResult(Of SftpProvider).Fail(New ResourceFailure(ResourceFailureKind.Timeout)))
            End Function)
            Dim ex = Assert.Throws(Of ResourceFailureException)(Sub() provider.EnsureConnected())
            Assert.Equal(ResourceFailureKind.Timeout, ex.Failure.Kind)
        End Using
        ' The initial round plus the three retries of the ladder (1, 4 and 10 s): every one is a whole race over all stored paths.
        Assert.Equal(ResolutionLimits.BackoffMs.Length + 1, rounds)
    End Sub

    <Theory>
    <InlineData(ResourceFailureKind.Transport, 0, False)>
    <InlineData(ResourceFailureKind.Timeout, 0, False)>
    <InlineData(ResourceFailureKind.Auth, 0, False)>
    <InlineData(ResourceFailureKind.KeyMismatch, 0, False)>
    <InlineData(ResourceFailureKind.Cancelled, 0, False)>
    <InlineData(ResourceFailureKind.LocalIO, 0, False)>
    <InlineData(ResourceFailureKind.SftpStatus, 2, True)>
    <InlineData(ResourceFailureKind.SftpStatus, 3, True)>
    <InlineData(ResourceFailureKind.SftpStatus, 4, False)>
    Public Sub Recursive_listing_only_skips_missing_or_denied_children(kind As Integer, status As Integer, expected As Boolean)
        Assert.Equal(expected, SftpProvider.IsSkippableSubfolderFailure(New ResourceFailureException(New ResourceFailure(CType(kind, ResourceFailureKind), status))))
    End Sub

    <Fact>
    Public Sub Large_batch_warns_for_either_slow_or_metered_endpoint()
        Dim local As New ProviderCapabilities(costHint:=ProviderCostHint.Local)
        Dim slow As New ProviderCapabilities(costHint:=ProviderCostHint.Slow)
        Dim metered As New ProviderCapabilities(costHint:=ProviderCostHint.Fast, isMetered:=True)
        Dim fast As New ProviderCapabilities(costHint:=ProviderCostHint.Fast)
        Dim large As Long = RemoteBatchPolicy.WarningThresholdBytes + 1
        Assert.True(RemoteBatchPolicy.NeedsWarning(large, slow, local))
        Assert.True(RemoteBatchPolicy.NeedsWarning(large, local, slow))
        Assert.True(RemoteBatchPolicy.NeedsWarning(large, metered, Nothing))
        Assert.True(RemoteBatchPolicy.NeedsWarning(large, Nothing, metered))
        Assert.False(RemoteBatchPolicy.NeedsWarning(large, local, fast))
        Assert.False(RemoteBatchPolicy.NeedsWarning(large, Nothing, Nothing))
        Assert.False(RemoteBatchPolicy.NeedsWarning(RemoteBatchPolicy.WarningThresholdBytes, slow, metered))
        Assert.False(RemoteBatchPolicy.NeedsWarning(0, slow, slow))
    End Sub

    Private Class MockSecretStore
        Implements IResourceSecretStore

        Private ReadOnly dict As New Dictionary(Of String, String)()

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            dict(handle) = protectedBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(dict.TryGetValue(handle, v), v, "blob-data")
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            dict.Remove(handle)
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return New List(Of String)(dict.Keys)
        End Function
    End Class

End Class
#End If
