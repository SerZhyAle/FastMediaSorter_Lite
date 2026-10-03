#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 100 §15.6 (T11): App-owned SMB connection leases, DPAPI protected credentials,
''' and Windows 1219 credential conflict handling without disconnecting external mappings.
''' Tests multiple resources/users, existing connections, permission failures and lease release.
''' </summary>
Public Class SmbConnectionLeaseTests
    Implements IDisposable

    Private ReadOnly tempRoot As String

    Public Sub New()
        tempRoot = Path.Combine(Path.GetTempPath(), "fms-smb-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempRoot)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(tempRoot) Then Directory.Delete(tempRoot, recursive:=True)
        Catch
        End Try
    End Sub

    ' ------------------------------------------------------------------ Doubles ----

    Private NotInheritable Class FakeWNetNative
        Implements IWNetNative

        Public ReadOnly AddCalls As New List(Of AddCallRecord)()
        Public ReadOnly CancelCalls As New List(Of CancelCallRecord)()

        Public OnAddConnection As Func(Of NETRESOURCE, String, String, Integer, Integer)
        Public OnCancelConnection As Func(Of String, Integer, Boolean, Integer)
        Public InspectionCode As Integer = SmbConnectionManager.ERROR_NOT_CONNECTED

        Public Function GetConnectedUser(uncRoot As String, ByRef userName As String) As Integer Implements IWNetNative.GetConnectedUser
            userName = If(InspectionCode = 0, "user", "")
            Return InspectionCode
        End Function

        Public Structure AddCallRecord
            Public RemoteName As String
            Public UserName As String
            Public Password As String
            Public Flags As Integer
        End Structure

        Public Structure CancelCallRecord
            Public Name As String
            Public Flags As Integer
            Public Force As Boolean
        End Structure

        Public Function WNetAddConnection2(ByRef netResource As NETRESOURCE, password As String, username As String, flags As Integer) As Integer Implements IWNetNative.WNetAddConnection2
            AddCalls.Add(New AddCallRecord With {
                .RemoteName = netResource.lpRemoteName,
                .UserName = username,
                .Password = password,
                .Flags = flags
            })
            If OnAddConnection IsNot Nothing Then
                Return OnAddConnection(netResource, password, username, flags)
            End If
            Return SmbConnectionManager.NO_ERROR
        End Function

        Public Function WNetCancelConnection2(name As String, flags As Integer, force As Boolean) As Integer Implements IWNetNative.WNetCancelConnection2
            CancelCalls.Add(New CancelCallRecord With {
                .Name = name,
                .Flags = flags,
                .Force = force
            })
            If OnCancelConnection IsNot Nothing Then
                Return OnCancelConnection(name, flags, force)
            End If
            Return SmbConnectionManager.NO_ERROR
        End Function
    End Class

    Private NotInheritable Class MemorySecretStore
        Implements IResourceSecretStore

        Public ReadOnly Blobs As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(Blobs.TryGetValue(handle, v), v, "")
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    ' ------------------------------------------------------------------ Tests ----

    <Fact>
    Public Sub Successful_add_of_an_existing_connection_does_not_take_ownership()
        Dim native As New FakeWNetNative With {.InspectionCode = 0}
        Dim manager As New SmbConnectionManager(native)
        Dim result = manager.AcquireLease("\\server\share", "user", "pass")
        Assert.True(result.Succeeded)
        Assert.False(result.Value.IsAppCreated)
        result.Value.Dispose()
        Assert.Empty(native.CancelCalls)
    End Sub

    <Fact>
    Public Sub Account_alias_lease_keeps_an_app_owned_share_connected_until_the_last_release()
        Dim native As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(native)
        Dim first = manager.AcquireLease("\\server\share", "user", "pass").Value
        native.InspectionCode = 0
        Dim second = manager.AcquireLease("\\server\share", "domain\user", "pass").Value
        Assert.True(second.IsAppCreated)
        first.Dispose()
        Assert.Empty(native.CancelCalls)
        second.Dispose()
        Assert.Single(native.CancelCalls)
    End Sub

    <Fact>
    Public Sub Unknown_connection_ownership_refuses_without_adding_or_cancelling()
        Dim native As New FakeWNetNative With {.InspectionCode = 5}
        Dim manager As New SmbConnectionManager(native)
        Dim result = manager.AcquireLease("\\server\share", "user", "pass")
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.LocalIO, result.Failure.Kind)
        Assert.Empty(native.AddCalls)
        Assert.Empty(native.CancelCalls)
    End Sub

    <Fact>
    Public Sub Read_only_Smb_provider_refuses_all_write_verbs_before_touching_paths()
        Using provider As New SmbProvider(New ResourceDescriptor With {.ReadOnlyHint = True}, Nothing)
            Dim actions As Action() = {
                Sub() provider.Upload("missing-local", "invalid-destination", Nothing, CancellationToken.None),
                Sub() provider.DeleteFile("invalid-source"),
                Sub() provider.Rename("invalid-source", "invalid-destination"),
                Sub() provider.CreateDirectory("invalid-destination")}
            For Each action As Action In actions
                Assert.Equal(ResourceFailureKind.Unsupported, Assert.Throws(Of ResourceFailureException)(action).Failure.Kind)
            Next
        End Using
    End Sub

    <Theory>
    <InlineData("\\server\share", "\\server\share")>
    <InlineData("\\SERVER\Share", "\\server\share")>
    <InlineData("\\server\share\subfolder\file.jpg", "\\server\share")>
    <InlineData("//server/share/subfolder", "\\server\share")>
    <InlineData("smb://nas.local/media/videos", "\\nas.local\media")>
    <InlineData("smb://192.168.1.100/photos", "\\192.168.1.100\photos")>
    <InlineData("  \\server\share\  ", "\\server\share")>
    Public Sub Normalize_unc_root_extracts_canonical_root(inputPath As String, expectedRoot As String)
        Dim root As String = SmbPathUtil.ExtractUncRoot(inputPath)
        Assert.Equal(expectedRoot, root)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("   ")>
    <InlineData("C:\local\folder")>
    <InlineData("\\server")>
    <InlineData("\\server\")>
    <InlineData("http://example.com")>
    Public Sub Normalize_unc_root_rejects_invalid_paths(invalidPath As String)
        Dim root As String = SmbPathUtil.ExtractUncRoot(invalidPath)
        Assert.Equal("", root)
    End Sub

    <Fact>
    Public Sub App_owned_connection_lease_acquires_and_cancels_on_final_release()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim result = manager.AcquireLease("\\nas\photos", "alice", "SecretPass")
        Assert.True(result.Succeeded)
        Assert.NotNull(result.Value)
        Assert.True(result.Value.IsAppCreated)
        Assert.Equal("\\nas\photos", result.Value.UncRoot)
        Assert.Equal("alice", result.Value.UserName)

        ' WNetAddConnection2 was called once with CONNECT_TEMPORARY
        Assert.Single(fakeWnet.AddCalls)
        Assert.Equal("\\nas\photos", fakeWnet.AddCalls(0).RemoteName)
        Assert.Equal("alice", fakeWnet.AddCalls(0).UserName)
        Assert.Equal("SecretPass", fakeWnet.AddCalls(0).Password)
        Assert.Equal(SmbConnectionManager.CONNECT_TEMPORARY, fakeWnet.AddCalls(0).Flags)

        Assert.Equal(1, manager.ActiveCount)
        Assert.Equal(1, manager.GetActiveLeaseCount("\\nas\photos", "alice"))

        ' Release lease
        result.Value.Dispose()

        Assert.Equal(0, manager.ActiveCount)
        Assert.Equal(0, manager.GetActiveLeaseCount("\\nas\photos", "alice"))

        ' WNetCancelConnection2 was called without force
        Assert.Single(fakeWnet.CancelCalls)
        Assert.Equal("\\nas\photos", fakeWnet.CancelCalls(0).Name)
        Assert.False(fakeWnet.CancelCalls(0).Force)
    End Sub

    <Fact>
    Public Sub Multiple_leases_on_same_resource_and_user_ref_count_correctly()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim lease1 = manager.AcquireLease("\\nas\media", "bob", "Pass1").Value
        Assert.NotNull(lease1)
        Assert.Equal(1, manager.GetActiveLeaseCount("\\nas\media", "bob"))
        Assert.Single(fakeWnet.AddCalls)

        ' Second lease for same share and user reuses the connection without extra WNetAddConnection2
        Dim lease2 = manager.AcquireLease("\\nas\media\subfolder\nested", "bob", "Pass1").Value
        Assert.NotNull(lease2)
        Assert.Equal(2, manager.GetActiveLeaseCount("\\nas\media", "bob"))
        Assert.Single(fakeWnet.AddCalls) ' Still only 1 WNet call

        ' Disposing lease 1 decrements count but keeps connection alive
        lease1.Dispose()
        Assert.Equal(1, manager.GetActiveLeaseCount("\\nas\media", "bob"))
        Assert.Empty(fakeWnet.CancelCalls)

        ' Disposing lease 2 drops count to 0 and cancels the app-created connection
        lease2.Dispose()
        Assert.Equal(0, manager.GetActiveLeaseCount("\\nas\media", "bob"))
        Assert.Single(fakeWnet.CancelCalls)
        Assert.Equal("\\nas\media", fakeWnet.CancelCalls(0).Name)
    End Sub

    <Fact>
    Public Sub Existing_external_connection_is_not_disconnected_on_release()
        Dim fakeWnet As New FakeWNetNative()
        ' Simulate existing connection in OS
        fakeWnet.OnAddConnection = Function(nr, pass, user, flags) SmbConnectionManager.ERROR_ALREADY_ASSIGNED
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim lease = manager.AcquireLease("\\server\share", "user", "").Value
        Assert.NotNull(lease)
        Assert.False(lease.IsAppCreated, "Must be marked as external (not app-created)")
        Assert.Equal(1, manager.GetActiveLeaseCount("\\server\share", "user"))

        ' Releasing external connection MUST NOT call WNetCancelConnection2
        lease.Dispose()
        Assert.Equal(0, manager.GetActiveLeaseCount("\\server\share", "user"))
        Assert.Empty(fakeWnet.CancelCalls)
    End Sub

    <Fact>
    Public Sub Conflict_1219_returns_typed_refusal_without_disconnecting_anything()
        Dim fakeWnet As New FakeWNetNative()
        fakeWnet.OnAddConnection = Function(nr, pass, user, flags) SmbConnectionManager.ERROR_SESSION_CREDENTIAL_CONFLICT
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim result = manager.AcquireLease("\\nas\share", "charlie", "DifferentPass")
        Assert.False(result.Succeeded)
        Assert.NotNull(result.Failure)
        Assert.Equal(ResourceFailureKind.CredentialConflict, result.Failure.Kind)
        Assert.Equal(1219, result.Failure.StatusCode)
        Assert.False(result.Failure.IsRetryable)
        Assert.True(result.Failure.IsFatalForAttempt)

        ' Invariant: 1219 must NEVER force cancel any external mapping
        Assert.Empty(fakeWnet.CancelCalls)
        Assert.Equal(0, manager.ActiveCount)
    End Sub

    <Theory>
    <InlineData(SmbConnectionManager.ERROR_ACCESS_DENIED, 5)>
    <InlineData(SmbConnectionManager.ERROR_LOGON_FAILURE, 1326)>
    <InlineData(SmbConnectionManager.ERROR_INVALID_PASSWORD, 86)>
    Public Sub Logon_and_permission_failures_return_typed_auth_failure(errorCode As Integer, expectedStatus As Integer)
        Dim fakeWnet As New FakeWNetNative()
        fakeWnet.OnAddConnection = Function(nr, pass, user, flags) errorCode
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim result = manager.AcquireLease("\\nas\secured", "user", "badpass")
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, result.Failure.Kind)
        Assert.Equal(expectedStatus, result.Failure.StatusCode)
        Assert.False(result.Failure.IsRetryable)
        Assert.Empty(fakeWnet.CancelCalls)
    End Sub

    <Theory>
    <InlineData(SmbConnectionManager.ERROR_BAD_NETPATH, 53)>
    <InlineData(SmbConnectionManager.ERROR_BAD_NET_NAME, 67)>
    <InlineData(SmbConnectionManager.ERROR_NO_NET_OR_BAD_PATH, 1203)>
    <InlineData(SmbConnectionManager.ERROR_BAD_PROVIDER, 1204)>
    <InlineData(SmbConnectionManager.ERROR_NETWORK_UNREACHABLE, 1231)>
    Public Sub Transport_and_unreachable_errors_return_typed_transport_failure(errorCode As Integer, expectedStatus As Integer)
        Dim fakeWnet As New FakeWNetNative()
        fakeWnet.OnAddConnection = Function(nr, pass, user, flags) errorCode
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim result = manager.AcquireLease("\\unreachable\share", "user", "pass")
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.Transport, result.Failure.Kind)
        Assert.Equal(expectedStatus, result.Failure.StatusCode)
        Assert.True(result.Failure.IsRetryable)
        Assert.Empty(fakeWnet.CancelCalls)
    End Sub

    <Fact>
    Public Sub Multiple_different_shares_or_servers_acquire_independent_leases()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim leaseA = manager.AcquireLease("\\server1\shareA", "alice", "passA").Value
        Dim leaseB = manager.AcquireLease("\\server2\shareB", "bob", "passB").Value

        Assert.Equal(2, manager.ActiveCount)
        Assert.Equal(2, fakeWnet.AddCalls.Count)

        ' Release share A: only share A is cancelled
        leaseA.Dispose()
        Assert.Equal(1, manager.ActiveCount)
        Assert.Single(fakeWnet.CancelCalls)
        Assert.Equal("\\server1\sharea", fakeWnet.CancelCalls(0).Name)

        ' Release share B: share B is cancelled
        leaseB.Dispose()
        Assert.Equal(0, manager.ActiveCount)
        Assert.Equal(2, fakeWnet.CancelCalls.Count)
        Assert.Equal("\\server2\shareb", fakeWnet.CancelCalls(1).Name)
    End Sub

    <Fact>
    Public Sub Double_dispose_is_safe_and_idempotent()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim lease = manager.AcquireLease("\\server\share", "user", "pass").Value
        Assert.False(lease.IsDisposed)

        lease.Dispose()
        Assert.True(lease.IsDisposed)
        Assert.Single(fakeWnet.CancelCalls)

        ' Second dispose does nothing
        lease.Dispose()
        Assert.True(lease.IsDisposed)
        Assert.Single(fakeWnet.CancelCalls)
    End Sub

    <Fact>
    Public Sub Protected_credentials_roundtrip_with_smb_resource_commit_and_connect()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)
        Dim secrets As New MemorySecretStore()
        Dim registryValues As New Dictionary(Of String, String)()

        Dim descriptor As New ResourceDescriptor With {
            .Id = "smb-nas-1",
            .DisplayName = "Home NAS",
            .Kind = ResourceKind.Smb,
            .RootPath = "\\nas\photos",
            .UserName = "admin"
        }

        ' 1. Commit with plaintext password into secret store via DPAPI (or checked protector)
        Dim commitResult = ResourceCommit.Commit(descriptor, "SuperSecret123",
            Function(k) If(registryValues.ContainsKey(k), registryValues(k), ""),
            Sub(k, v) registryValues(k) = v,
            secrets)

        Assert.True(commitResult.Succeeded)
        Assert.StartsWith("cred:", descriptor.SecretHandle)
        Assert.True(secrets.Blobs.ContainsKey(descriptor.SecretHandle))
        ' Verify raw password is NOT stored plaintext in blob
        Assert.DoesNotContain("SuperSecret123", secrets.Blobs(descriptor.SecretHandle))

        ' 2. Connect using SmbResourceConnector with stored protected credential
        Dim connectResult = SmbResourceConnector.Connect(descriptor, secrets, connectionManager:=manager)
        Assert.True(connectResult.Succeeded)
        Assert.NotNull(connectResult.Value)

        Dim provider As SmbProvider = connectResult.Value
        Assert.Equal(ResourceKind.Smb, provider.Kind)
        Assert.False(provider.Capabilities.NeedsMaterialisation, "SMB files are openable directly by UNC path")
        Assert.True(provider.Capabilities.SupportsServerSideRename, "SMB supports server-side rename")
        Assert.False(provider.Capabilities.HasRecycleBin, "Network shares do not have Recycle Bin")
        Assert.True(provider.Capabilities.CanWrite)

        ' WNet was called with decrypted password
        Assert.Single(fakeWnet.AddCalls)
        Assert.Equal("SuperSecret123", fakeWnet.AddCalls(0).Password)
        Assert.Equal("admin", fakeWnet.AddCalls(0).UserName)
        Assert.Equal("\\nas\photos", fakeWnet.AddCalls(0).RemoteName)

        ' 3. Dispose provider releases lease
        provider.Dispose()
        Assert.Single(fakeWnet.CancelCalls)
        Assert.Equal(0, manager.ActiveCount)
    End Sub

    <Fact>
    Public Sub SmbProvider_delegates_filesystem_operations_and_throws_when_disposed()
        Dim testFolder As String = Path.Combine(tempRoot, "share_root")
        Directory.CreateDirectory(testFolder)
        File.WriteAllText(Path.Combine(testFolder, "test.txt"), "hello smb")

        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)

        Dim lease = manager.AcquireLease("\\localhost\test", "user", "pass").Value
        Dim descriptor As New ResourceDescriptor With {
            .Id = "test-res",
            .RootPath = testFolder,
            .Kind = ResourceKind.Smb
        }

        Dim provider As New SmbProvider(descriptor, lease)

        ' Stat test
        Dim statResult = provider.Stat(Path.Combine(testFolder, "test.txt"))
        Assert.True(statResult.Exists)
        Assert.Equal("test.txt", statResult.Entry.Name)

        ' Listing test
        Dim entries = provider.ListEntries(testFolder, CancellationToken.None)
        Assert.Single(entries)
        Assert.Equal("test.txt", entries(0).Name)

        ' Disposing provider disposes lease and invalidates further provider calls
        provider.Dispose()
        Assert.True(lease.IsDisposed)

        Assert.Throws(Of ObjectDisposedException)(Sub() provider.ListEntries(testFolder, CancellationToken.None))
        Assert.Throws(Of ObjectDisposedException)(Sub() provider.Stat(Path.Combine(testFolder, "test.txt")))
    End Sub

    <Fact>
    Public Sub Concurrent_lease_acquisition_and_release_is_thread_safe()
        Dim fakeWnet As New FakeWNetNative()
        Dim manager As New SmbConnectionManager(fakeWnet)
        Const threadCount As Integer = 8
        Const iterationsPerThread As Integer = 50

        Parallel.For(0, threadCount, Sub(t)
            For i As Integer = 1 To iterationsPerThread
                Dim shareName As String = "\\server\share" & (i Mod 3).ToString()
                Dim leaseResult = manager.AcquireLease(shareName, "user" & (t Mod 2).ToString(), "pass")
                Assert.True(leaseResult.Succeeded)
                Thread.Sleep(1)
                leaseResult.Value.Dispose()
            Next
        End Sub)

        Assert.Equal(0, manager.ActiveCount)
    End Sub

End Class
#End If
