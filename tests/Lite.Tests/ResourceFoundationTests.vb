#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Sockets
Imports System.Threading
Imports Xunit

''' <summary>
''' 100 §15.4 (T06): the typed failure/stat seam, the locator's URI rules, the DPAPI result
''' wrapper and the secret-then-registry commit order, each with failure injection at the write
''' it is about. Nothing here touches the real registry, DPAPI or a network.
''' </summary>
Public Class ResourceFoundationTests

    ' ------------------------------------------------------------------ doubles ----

    Private NotInheritable Class MemoryValues
        Friend ReadOnly Values As New Dictionary(Of String, String)()
        Friend FailWriteOf As String = ""
        Friend ReadOnly Writes As New List(Of String)()
        Friend Function Read(name As String) As String
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function
        Friend Sub Write(name As String, value As String)
            Writes.Add(name)
            If name = FailWriteOf Then Throw New IOException("injected write failure")
            Values(name) = value
        End Sub
    End Class

    Private NotInheritable Class MemorySecrets
        Implements IResourceSecretStore
        Friend ReadOnly Blobs As New Dictionary(Of String, String)()
        Friend FailWrite As Boolean
        Friend FailDelete As Boolean
        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            If FailWrite Then Throw New IOException("injected secret failure")
            Blobs(handle) = protectedBlob
        End Sub
        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(Blobs.TryGetValue(handle, v), v, "")
        End Function
        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            If FailDelete Then Throw New IOException("injected delete failure")
            Blobs.Remove(handle)
        End Sub
        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    ''' <summary>The scratch provider with a configurable typed answer for one path.</summary>
    Private NotInheritable Class TypedProvider
        Implements IFileSystemProvider, ITypedStatProvider
        Friend ReadOnly Inner As ResourceTestProvider
        Friend Answer As Func(Of String, StatResult)
        Friend Sub New(inner As ResourceTestProvider)
            Me.Inner = inner
        End Sub
        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return Inner.Kind
            End Get
        End Property
        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Return Inner.Capabilities
            End Get
        End Property
        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Return Inner.ListEntries(dirPath, ct)
        End Function
        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            Return Inner.TryStat(path, entry)
        End Function
        Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
            Return Answer(path)
        End Function
        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            Inner.Download(remotePath, localPath, progress, ct)
        End Sub
        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            Inner.Upload(localPath, remotePath, progress, ct)
        End Sub
        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            Inner.DeleteFile(remotePath)
        End Sub
        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Inner.Rename(remotePath, newRemotePath)
        End Sub
        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Inner.CreateDirectory(remotePath)
        End Sub
    End Class

    Private Shared Function Sftp(Optional id As String = "a", Optional host As String = "10.0.0.2") As ResourceDescriptor
        Return New ResourceDescriptor With {.Id = id, .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = host, .Port = 22, .UserName = "fms", .RootPath = "/MOV"}
    End Function

    Private Shared Function OkProtect(s As String) As SecretProtectionResult
        Return DpapiSecretChecks.ProtectChecked(s, Function(data) data.Select(Function(b) CByte(b Xor &H5A)).ToArray())
    End Function

    ' ----------------------------------------------------------- failure classes ----

    <Fact>
    Public Sub Only_transport_and_timeout_failures_are_retryable_and_auth_key_cancel_end_the_attempt()
        For Each kind As ResourceFailureKind In [Enum].GetValues(GetType(ResourceFailureKind))
            Dim f As New ResourceFailure(kind)
            Assert.Equal(kind = ResourceFailureKind.Transport OrElse kind = ResourceFailureKind.Timeout, f.IsRetryable)
        Next
        Assert.True(New ResourceFailure(ResourceFailureKind.KeyMismatch).IsFatalForAttempt)
        Assert.True(New ResourceFailure(ResourceFailureKind.Auth).IsFatalForAttempt)
        Assert.True(New ResourceFailure(ResourceFailureKind.Cancelled).IsFatalForAttempt)
        Assert.False(New ResourceFailure(ResourceFailureKind.Transport).IsFatalForAttempt)
        ' A status failure (permission, no such file) is neither retried nor an attempt-ender.
        Dim status As New ResourceFailure(ResourceFailureKind.SftpStatus, 3)
        Assert.False(status.IsRetryable)
        Assert.False(status.IsFatalForAttempt)
    End Sub

    <Fact>
    Public Sub Exceptions_map_to_a_class_and_an_unknown_one_is_never_taken_for_a_network_blip()
        Assert.Equal(ResourceFailureKind.Cancelled, ResourceFailure.FromException(New OperationCanceledException()).Kind)
        Assert.Equal(ResourceFailureKind.Timeout, ResourceFailure.FromException(New TimeoutException()).Kind)
        Dim socket As ResourceFailure = ResourceFailure.FromException(New SocketException(10061))
        Assert.Equal(ResourceFailureKind.Transport, socket.Kind)
        Assert.Equal(10061, socket.StatusCode)
        Assert.Equal(ResourceFailureKind.LocalIO, ResourceFailure.FromException(New UnauthorizedAccessException()).Kind)
        Assert.Equal(ResourceFailureKind.Unsupported, ResourceFailure.FromException(New NotSupportedException()).Kind)
        Assert.Equal(ResourceFailureKind.LocalIO, ResourceFailure.FromException(New InvalidOperationException("boom")).Kind)
        ' A typed failure keeps its own class through the exception form and an AggregateException wrapper.
        Dim keyed As New ResourceFailureException(New ResourceFailure(ResourceFailureKind.KeyMismatch))
        Assert.Equal(ResourceFailureKind.KeyMismatch, ResourceFailure.FromException(keyed).Kind)
        Assert.Equal(ResourceFailureKind.KeyMismatch, ResourceFailure.FromException(New AggregateException(keyed)).Kind)
    End Sub

    <Fact>
    Public Sub A_failure_prints_its_class_and_code_never_its_detail()
        Dim f As New ResourceFailure(ResourceFailureKind.SftpStatus, 3, "password=hunter2")
        Assert.Equal("SftpStatus (3)", f.ToString())
        Assert.DoesNotContain("hunter2", New ResourceFailureException(f).Message)
    End Sub

    <Fact>
    Public Sub A_failed_listing_is_a_failure_not_an_empty_success()
        Dim result = ResourceProviderCalls.ListChecked(New ThrowingLister(), "/x", CancellationToken.None)
        Assert.False(result.Succeeded)
        Assert.Equal(ResourceFailureKind.Transport, result.Failure.Kind)
        Assert.Null(result.Value)
    End Sub

    Private NotInheritable Class ThrowingLister
        Implements IFileSystemProvider
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
            Throw New SocketException(10060)
        End Function
        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            Throw New SocketException(10060)
        End Function
        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        End Sub
        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        End Sub
        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        End Sub
        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        End Sub
        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        End Sub
    End Class

    ' ----------------------------------------------------------------- typed stat ----

    <Fact>
    Public Sub A_legacy_provider_reads_false_as_Missing_and_a_throw_as_Failed_never_as_absence()
        Dim missing As StatResult = ResourceProviderCalls.Stat(New ResourceTestProvider(PrivateDir()), "/nope.jpg")
        Assert.Equal(StatOutcome.Missing, missing.Outcome)
        Assert.True(missing.IsDefinitelyMissing)

        Dim failed As StatResult = ResourceProviderCalls.Stat(New ThrowingLister(), "/x")
        Assert.Equal(StatOutcome.Failed, failed.Outcome)
        Assert.False(failed.IsDefinitelyMissing)
        Assert.Equal(ResourceFailureKind.Transport, failed.Failure.Kind)
    End Sub

    <Fact>
    Public Sub The_local_provider_tells_found_from_missing_and_a_folder_is_not_a_file()
        Dim dir As String = PrivateDir()
        Dim file As String = Path.Combine(dir, "a.jpg")
        System.IO.File.WriteAllBytes(file, New Byte() {1, 2, 3})
        Dim provider As New LocalProvider(Function() False)
        Dim found As StatResult = ResourceProviderCalls.Stat(provider, file)
        Assert.Equal(StatOutcome.Found, found.Outcome)
        Assert.Equal(3L, found.Entry.Length)
        Assert.Equal(StatOutcome.Missing, ResourceProviderCalls.Stat(provider, Path.Combine(dir, "gone.jpg")).Outcome)
        Assert.Equal(StatOutcome.Missing, ResourceProviderCalls.Stat(provider, Path.Combine(dir, "no-folder", "x.jpg")).Outcome)
        Assert.Equal(StatOutcome.Missing, ResourceProviderCalls.Stat(provider, dir).Outcome)
        Assert.Equal(StatOutcome.Missing, ResourceProviderCalls.Stat(provider, "").Outcome)
    End Sub

    <Theory>
    <InlineData(2)>
    <InlineData(3)>
    Public Sub An_upload_never_overwrites_or_proceeds_on_a_permission_error_or_a_dead_link(outcomeCode As Integer)
        Dim backing As New ResourceTestProvider(PrivateDir())
        Dim typed As New TypedProvider(backing) With {
            .Answer = Function(p) If(outcomeCode = CInt(StatOutcome.Denied), StatResult.Denied(), StatResult.Failed(New ResourceFailure(ResourceFailureKind.Timeout)))}
        Dim source As String = Path.Combine(PrivateDir(), "src.bin")
        System.IO.File.WriteAllBytes(source, New Byte() {1, 2, 3, 4})

        Assert.Throws(Of ResourceFailureException)(Sub() ResourceTransfer.UploadVerified(typed, source, "/in/a.bin", overwrite:=True))
        Assert.Equal(0, backing.UploadCalls) ' not one byte sent on an unknown answer
    End Sub

    <Fact>
    Public Sub A_definite_Missing_lets_the_upload_through_and_a_download_of_a_missing_source_is_FileNotFound()
        Dim backing As New ResourceTestProvider(PrivateDir())
        Dim source As String = Path.Combine(PrivateDir(), "src.bin")
        System.IO.File.WriteAllBytes(source, New Byte() {1, 2, 3, 4})
        ResourceTransfer.UploadVerified(backing, source, "/in/a.bin", overwrite:=False)
        Assert.True(System.IO.File.Exists(backing.BackingPath("/in/a.bin")))

        Assert.Throws(Of FileNotFoundException)(Sub() ResourceTransfer.DownloadVerified(backing, "/in/none.bin", Path.Combine(PrivateDir(), "d.bin"), True))
        Dim denied As New TypedProvider(backing) With {.Answer = Function(p) StatResult.Denied()}
        Assert.Throws(Of ResourceFailureException)(Sub() ResourceTransfer.DownloadVerified(denied, "/in/a.bin", Path.Combine(PrivateDir(), "d2.bin"), True))
    End Sub

    ' ------------------------------------------------------------------ versions ----

    <Fact>
    Public Sub Version_evidence_is_the_tag_when_either_side_has_one_else_size_and_write_time()
        Dim t As Date = New Date(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        Dim a As New ResourceEntry With {.Length = 5, .LastWriteUtc = t}
        Dim b As New ResourceEntry With {.Length = 5, .LastWriteUtc = t}
        Assert.True(a.SameVersionAs(b))
        b.LastWriteUtc = t.AddSeconds(1)
        Assert.False(a.SameVersionAs(b))
        a.VersionTag = "v1" : b.LastWriteUtc = t
        Assert.False(a.SameVersionAs(b)) ' one side tagged, the other not: not provably the same
        b.VersionTag = "v1"
        Assert.True(a.SameVersionAs(b))
        b.VersionTag = "v2"
        Assert.False(a.SameVersionAs(b))
        Assert.False(a.SameVersionAs(Nothing))
    End Sub

    ' ------------------------------------------------------------------- locator ----

    <Fact>
    Public Sub A_locator_round_trips_unicode_case_ipv6_and_awkward_characters_exactly()
        Dim cases As (host As String, user As String, port As Integer, path As String)() = {
            ("nas.local", "fms", 2022, "/Фото/Отпуск 2026/IMG_01.JPG"),
            ("10.0.0.2", "", 22, "/MOV"),
            ("fe80::1", "u@dom", 55259, "/a b/100%/#1/?x"),
            ("2001:db8::23", "fms", 0, "/"),
            ("Host.Example", "名前", 22, "/Mixed/CASE/Keeps")}
        For Each c In cases
            Dim d As New ResourceDescriptor With {.Id = "x", .Kind = ResourceKind.Sftp, .Host = c.host, .UserName = c.user, .Port = c.port, .RootPath = c.path}
            Dim locator As String = ResourceLocator.Build(d, "")
            Dim user As String = Nothing, host As String = Nothing, port As Integer, remote As String = Nothing
            Assert.True(ResourceLocator.TryParse(locator, user, host, port, remote), locator)
            Assert.Equal(c.user, user)
            Assert.Equal(c.host, host)
            Assert.Equal(c.port, port)
            Assert.Equal(c.path, remote)
        Next
    End Sub

    <Fact>
    Public Sub An_ipv6_host_is_bracketed_in_the_locator_so_the_port_is_unambiguous()
        Dim d As New ResourceDescriptor With {.Id = "x", .Kind = ResourceKind.Sftp, .Host = "2001:db8::23", .UserName = "fms", .Port = 2022, .RootPath = "/p"}
        Assert.Equal("sftp://fms@[2001:db8::23]:2022/p", ResourceLocator.Build(d, ""))
    End Sub

    <Theory>
    <InlineData("sftp://user:secret@host/p")>
    <InlineData("sftp://host/a/../b")>
    <InlineData("sftp://host/a/%2e%2e/b")>
    <InlineData("sftp://host/a/%2E/b")>
    <InlineData("sftp://host/a%2Fb")>
    <InlineData("sftp://host/a%5Cb")>
    <InlineData("sftp://host/a%00b")>
    <InlineData("sftp://host/a%0Ab")>
    <InlineData("sftp://host/a%zz")>
    <InlineData("sftp://host/a%2")>
    <InlineData("sftp://host/a%")>
    <InlineData("sftp://host/a//b")>
    <InlineData("sftp://host/a?x=1")>
    <InlineData("sftp://host/a#frag")>
    <InlineData("sftp://fe80::1/p")>
    <InlineData("sftp://[fe80::1/p")>
    <InlineData("sftp://[nothex]/p")>
    <InlineData("sftp://[::1]x/p")>
    <InlineData("sftp://host:0/p")>
    <InlineData("sftp://host:65536/p")>
    <InlineData("sftp://host:2x/p")>
    <InlineData("sftp://:22/p")>
    <InlineData("sftp://host%2Fx/p")>
    <InlineData("sftp://host/%C3%28")>
    Public Sub A_malformed_or_dangerous_locator_is_refused_not_repaired(locator As String)
        Dim user As String = Nothing, host As String = Nothing, port As Integer, remote As String = Nothing
        Assert.False(ResourceLocator.TryParse(locator, user, host, port, remote))
    End Sub

    <Fact>
    Public Sub Percent_escapes_are_decoded_exactly_once_and_case_is_preserved()
        Dim user As String = Nothing, host As String = Nothing, port As Integer, remote As String = Nothing
        Assert.True(ResourceLocator.TryParse("sftp://host/a%2520b/Caf%C3%A9/", user, host, port, remote))
        Assert.Equal("/a%20b/Café", remote) ' %25 -> '%', and the result is NOT decoded a second time
        Assert.True(ResourceLocator.TryParse("sftp://host", user, host, port, remote))
        Assert.Equal("/", remote)
    End Sub

    ' ---------------------------------------------------------------------- DPAPI ----

    <Fact>
    Public Sub Protecting_distinguishes_empty_input_success_and_failure()
        Assert.Equal(SecretProtectionStatus.EmptyInput, DpapiSecretChecks.ProtectChecked("").Status)
        Assert.Equal(SecretProtectionStatus.EmptyInput, DpapiSecretChecks.ProtectChecked(Nothing).Status)

        Dim ok As SecretProtectionResult = OkProtect("hunter2")
        Assert.True(ok.Succeeded)
        Assert.NotEqual("hunter2", ok.Value)

        Dim emptyBlob As SecretProtectionResult = DpapiSecretChecks.ProtectChecked("x", Function(d) Array.Empty(Of Byte)())
        Assert.Equal(SecretProtectionStatus.ProtectionFailed, emptyBlob.Status)
        Assert.Equal("", emptyBlob.Value)

        Dim thrown As SecretProtectionResult = DpapiSecretChecks.ProtectChecked("x", AddressOf Boom)
        Assert.Equal(SecretProtectionStatus.ProtectionFailed, thrown.Status)
    End Sub

    <Fact>
    Public Sub Unprotecting_failure_is_not_empty_and_the_result_never_prints_the_secret()
        Dim ok As SecretProtectionResult = OkProtect("hunter2")
        Dim back As SecretProtectionResult = DpapiSecretChecks.UnprotectChecked(ok.Value, Function(d) d.Select(Function(b) CByte(b Xor &H5A)).ToArray())
        Assert.True(back.Succeeded)
        Assert.Equal("hunter2", back.Value)
        Assert.DoesNotContain("hunter2", back.ToString())

        Assert.Equal(SecretProtectionStatus.EmptyInput, DpapiSecretChecks.UnprotectChecked("").Status)
        Assert.Equal(SecretProtectionStatus.ProtectionFailed, DpapiSecretChecks.UnprotectChecked("not base64 !!").Status)
        Assert.Equal(SecretProtectionStatus.ProtectionFailed, DpapiSecretChecks.UnprotectChecked(ok.Value, AddressOf Boom).Status)
    End Sub

    ' --------------------------------------------------------------- commit order ----

    Private Shared Function Commit(descriptor As ResourceDescriptor, secret As String, store As MemoryValues, secrets As MemorySecrets) As ResourceCommitResult
        Return ResourceCommit.Commit(descriptor, secret, AddressOf store.Read, AddressOf store.Write, secrets, AddressOf OkProtect)
    End Function

    <Fact>
    Public Sub A_new_resource_stores_the_secret_first_then_references_it_and_the_json_has_no_password()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim result = Commit(Sftp(), "hunter2", store, secrets)
        Assert.True(result.Succeeded)
        Assert.StartsWith("cred:", result.SecretHandle)
        Assert.True(secrets.Blobs.ContainsKey(result.SecretHandle))
        Dim json As String = store.Values("Resources")
        Assert.Contains(result.SecretHandle, json)
        Assert.DoesNotContain("hunter2", json)
        Assert.Equal(result.SecretHandle, ResourceRegistry.Parse(json).Single().SecretHandle)
    End Sub

    <Fact>
    Public Sub An_sftp_resource_without_a_credential_is_refused_and_an_empty_one_is_not_no_password()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Assert.Equal(ResourceCommitOutcome.SecretRequired, Commit(Sftp(), Nothing, store, secrets).Outcome)
        Assert.Equal(ResourceCommitOutcome.SecretRequired, Commit(Sftp(), "", store, secrets).Outcome)
        Assert.Empty(store.Writes)
        Assert.Empty(secrets.Blobs)
    End Sub

    <Fact>
    Public Sub A_protection_failure_writes_nothing_anywhere()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim result = ResourceCommit.Commit(Sftp(), "x", AddressOf store.Read, AddressOf store.Write, secrets,
                                           Function(s) DpapiSecretChecks.ProtectChecked(s, Function(d) Array.Empty(Of Byte)()))
        Assert.Equal(ResourceCommitOutcome.ProtectionFailed, result.Outcome)
        Assert.Empty(store.Writes)
        Assert.Empty(secrets.Blobs)
    End Sub

    <Fact>
    Public Sub A_failed_secret_stage_never_touches_the_registry()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets() With {.FailWrite = True}
        Assert.Equal(ResourceCommitOutcome.StageFailed, Commit(Sftp(), "x", store, secrets).Outcome)
        Assert.Empty(store.Writes)
    End Sub

    <Fact>
    Public Sub A_failed_registry_write_removes_only_the_secret_this_call_created_and_keeps_the_old_one()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim first = Commit(Sftp(), "old-pass", store, secrets)
        Dim before As String = store.Values("Resources")

        store.FailWriteOf = "Resources"
        Dim second = Commit(Sftp(), "new-pass", store, secrets)
        Assert.Equal(ResourceCommitOutcome.RegistryFailed, second.Outcome)
        Assert.Equal(before, store.Values("Resources"))   ' the previous registry value is intact
        Assert.Equal({first.SecretHandle}, secrets.Blobs.Keys.ToArray()) ' no orphan left; the old secret is still there
        Assert.Equal(first.SecretHandle, ResourceRegistry.Parse(store.Values("Resources")).Single().SecretHandle)
    End Sub

    <Fact>
    Public Sub Changing_a_secret_swaps_the_reference_then_removes_the_old_blob_and_keeps_the_list_position()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim first = Commit(Sftp("a"), "p1", store, secrets)
        Commit(Sftp("b", "10.0.0.3"), "p2", store, secrets)
        Dim changed = Commit(Sftp("a"), "p3", store, secrets)

        Assert.True(changed.Succeeded)
        Assert.NotEqual(first.SecretHandle, changed.SecretHandle)
        Assert.False(secrets.Blobs.ContainsKey(first.SecretHandle))
        Assert.Equal({"a", "b"}, ResourceRegistry.Parse(store.Values("Resources")).Select(Function(r) r.Id).ToArray())
    End Sub

    <Fact>
    Public Sub Editing_a_resource_without_a_new_secret_keeps_the_existing_credential()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim first = Commit(Sftp("a"), "p1", store, secrets)
        Dim renamed As ResourceDescriptor = Sftp("a")
        renamed.DisplayName = "Renamed"
        Dim result = Commit(renamed, Nothing, store, secrets)
        Assert.True(result.Succeeded)
        Assert.Equal(first.SecretHandle, result.SecretHandle)
        Assert.Equal("Renamed", ResourceRegistry.Parse(store.Values("Resources")).Single().DisplayName)
        Assert.Single(secrets.Blobs)
    End Sub

    <Fact>
    Public Sub A_credential_shared_by_two_roots_survives_the_removal_of_one()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim a = Commit(Sftp("a"), "p1", store, secrets)
        Dim sibling As ResourceDescriptor = Sftp("b")
        sibling.SecretHandle = a.SecretHandle
        Assert.True(Commit(sibling, Nothing, store, secrets).Succeeded)

        Assert.True(ResourceCommit.Remove("a", AddressOf store.Read, AddressOf store.Write, secrets).Succeeded)
        Assert.True(secrets.Blobs.ContainsKey(a.SecretHandle))            ' b still needs it
        Assert.True(ResourceCommit.Remove("b", AddressOf store.Read, AddressOf store.Write, secrets).Succeeded)
        Assert.Empty(secrets.Blobs)                                         ' the last reference took it
        Assert.Equal(ResourceCommitOutcome.NotFound, ResourceCommit.Remove("b", AddressOf store.Read, AddressOf store.Write, secrets).Outcome)
    End Sub

    <Fact>
    Public Sub A_failed_registry_write_on_remove_keeps_both_the_record_and_its_credential()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim a = Commit(Sftp("a"), "p1", store, secrets)
        store.FailWriteOf = "Resources"
        Assert.Equal(ResourceCommitOutcome.RegistryFailed, ResourceCommit.Remove("a", AddressOf store.Read, AddressOf store.Write, secrets).Outcome)
        Assert.True(secrets.Blobs.ContainsKey(a.SecretHandle))
        Assert.Single(ResourceRegistry.Parse(store.Values("Resources")))
    End Sub

    <Fact>
    Public Sub A_credential_that_will_not_delete_is_only_an_orphan_and_the_sweep_takes_it_later()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim a = Commit(Sftp("a"), "p1", store, secrets)
        secrets.FailDelete = True
        Assert.True(ResourceCommit.Remove("a", AddressOf store.Read, AddressOf store.Write, secrets).Succeeded) ' the removal still succeeds
        Assert.True(secrets.Blobs.ContainsKey(a.SecretHandle))

        secrets.FailDelete = False
        Assert.Equal(1, ResourceCommit.SweepOrphans(AddressOf store.Read, secrets))
        Assert.Empty(secrets.Blobs)
    End Sub

    <Fact>
    Public Sub The_sweep_never_removes_a_credential_a_record_still_references()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim a = Commit(Sftp("a"), "p1", store, secrets)
        secrets.PutBlob("cred:orphan", "zz")
        Assert.Equal(1, ResourceCommit.SweepOrphans(AddressOf store.Read, secrets))
        Assert.Equal({a.SecretHandle}, secrets.Blobs.Keys.ToArray())
    End Sub

    <Fact>
    Public Sub Reading_a_credential_reports_missing_and_undecryptable_without_destroying_the_blob()
        Dim store As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim a = Commit(Sftp("a"), "p1", store, secrets)
        Dim descriptor As ResourceDescriptor = ResourceRegistry.Parse(store.Values("Resources")).Single()

        Dim good = ResourceCommit.ReadCredential(descriptor, secrets,
            Function(s) DpapiSecretChecks.UnprotectChecked(s, Function(d) d.Select(Function(b) CByte(b Xor &H5A)).ToArray()))
        Assert.Equal(CredentialReadOutcome.Ok, good.Outcome)
        Assert.Equal("p1", good.Secret)

        Dim broken = ResourceCommit.ReadCredential(descriptor, secrets, Function(s) DpapiSecretChecks.UnprotectChecked(s, AddressOf Boom))
        Assert.Equal(CredentialReadOutcome.NeedsReentry, broken.Outcome)
        Assert.Equal("", broken.Secret)
        Assert.True(secrets.Blobs.ContainsKey(a.SecretHandle)) ' the old blob is the only recovery copy

        descriptor.SecretHandle = ""
        Assert.Equal(CredentialReadOutcome.Missing, ResourceCommit.ReadCredential(descriptor, secrets).Outcome)
        descriptor.SecretHandle = "cred:none"
        Assert.Equal(CredentialReadOutcome.Missing, ResourceCommit.ReadCredential(descriptor, secrets).Outcome)
    End Sub

    ' ------------------------------------------------------------- policy & leases ----

    <Fact>
    Public Sub Server_profile_and_root_policy_are_immutable_and_derive_capabilities()
        Dim ep1 As New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.100", .Port = 22}
        Dim ep2 As New FmsEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222}
        Dim profile As New ResourceServerProfile("prof-1", "fms-user", {ep1, ep2}, "cred:handle-1", "SHA256:8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk", "note")

        Assert.Equal("prof-1", profile.ProfileId)
        Assert.Equal("fms-user", profile.UserName)
        Assert.Equal(2, profile.Endpoints.Count)
        Assert.Equal("cred:handle-1", profile.SecretHandle)
        Assert.True(profile.IsPinned)
        Assert.True(profile.MatchesEndpoint("192.168.1.100", 22, "fms-user"))
        Assert.True(profile.MatchesEndpoint("WAN.EXAMPLE.COM", 2222, "fms-user"))
        Assert.False(profile.MatchesEndpoint("192.168.1.100", 22, "other-user"))
        Assert.False(profile.MatchesEndpoint("192.168.1.100", 80, "fms-user"))

        Dim baseCaps As New ProviderCapabilities(canWrite:=True, canDelete:=True, canRename:=True, canCreateFolder:=True,
                                                hasRecycleBin:=False, needsMaterialisation:=True,
                                                supportsServerSideRename:=True, supportsAtomicOverwriteRename:=True,
                                                supportsStableSourceDelete:=True)

        ' Read-only root
        Dim readOnlyRoot As New ResourceRootPolicy("root-1", "prof-1", "/Photos", label:="Photos", readOnlyRaw:=True, isDestination:=False)
        Assert.False(readOnlyRoot.EffectiveWritable)
        Dim roCaps As ProviderCapabilities = readOnlyRoot.DeriveCapabilities(baseCaps)
        Assert.False(roCaps.CanWrite)
        Assert.False(roCaps.CanDelete)
        Assert.False(roCaps.CanRename)
        Assert.False(roCaps.SupportsServerSideRename)

        ' Destination root overrides readOnlyRaw=True
        Dim destRoot As New ResourceRootPolicy("root-2", "prof-1", "/Inbox", label:="Inbox", readOnlyRaw:=True, isDestination:=True)
        Assert.True(destRoot.EffectiveWritable)
        Dim destCaps As ProviderCapabilities = destRoot.DeriveCapabilities(baseCaps)
        Assert.True(destCaps.CanWrite)
        Assert.True(destCaps.CanDelete)
        Assert.True(destCaps.CanRename)
        Assert.True(destCaps.SupportsServerSideRename)

        ' PIN-locked root disables all write operations regardless of isDestination
        Dim pinRoot As New ResourceRootPolicy("root-3", "prof-1", "/Secure", label:="Secure", readOnlyRaw:=False, isDestination:=True, hasPin:=True)
        Dim pinCaps As ProviderCapabilities = pinRoot.DeriveCapabilities(baseCaps)
        Assert.False(pinCaps.CanWrite)
        Assert.False(pinCaps.CanDelete)
        Assert.False(pinCaps.CanRename)
    End Sub

    <Fact>
    Public Sub ResourceMatching_matches_server_identity_and_exact_virtual_root()
        Dim list As New List(Of ResourceDescriptor) From {
            New ResourceDescriptor With {.Id = "1", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "fms", .RootPath = "/Photos"},
            New ResourceDescriptor With {.Id = "2", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "fms", .RootPath = "/Videos"},
            New ResourceDescriptor With {.Id = "3", .Kind = ResourceKind.Unc, .RootPath = "\\server\share"}
        }

        Dim matched1 = ResourceMatching.FindMatchingDescriptor(list, "NAS.LOCAL", 22, "fms", "/Photos/")
        Assert.NotNull(matched1)
        Assert.Equal("1", matched1.Id)

        Dim matchedCase = ResourceMatching.FindMatchingDescriptor(list, "nas.local", 22, "fms", "/photos")
        Assert.Null(matchedCase) ' POSIX paths are case-sensitive

        Dim matchedUnc = ResourceMatching.FindMatchingDescriptor(list, "", 0, "", "\\server\share")
        Assert.NotNull(matchedUnc)
        Assert.Equal("3", matchedUnc.Id)
    End Sub

    <Fact>
    Public Sub Session_leases_stop_admission_on_dispose_and_prevent_early_provider_disposal()
        Dim dir As String = PrivateDir()
        Dim cacheDir As String = Path.Combine(dir, "cache")
        Directory.CreateDirectory(cacheDir)
        Dim testProvider As New ResourceTestProvider(Path.Combine(dir, "remote"))
        testProvider.PutFile("/MOV/a.jpg", New Byte() {1, 2, 3})

        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheDir)
        Dim session As New ResourceSession(Sftp(), testProvider, sessionDir, 1024L * 1024L)
        session.LoadListing("/MOV", CancellationToken.None)

        Dim lease1 As IDisposable = session.TryAcquireLease()
        Assert.NotNull(lease1)

        Dim lease2 As IDisposable = Nothing
        Assert.True(session.TryAcquireLease(lease2))
        Assert.NotNull(lease2)

        ' Dispose session: admission is stopped, but provider is not disposed yet because leases are active
        session.Dispose()
        Assert.Null(session.TryAcquireLease())

        ' Release first lease: provider still held by lease2
        lease1.Dispose()
        Assert.True(Directory.Exists(sessionDir))

        ' Release second lease: now provider is released and session directory cleaned up
        lease2.Dispose()
        Assert.False(Directory.Exists(sessionDir))
    End Sub

    Private Shared Function Boom(data As Byte()) As Byte()
        Throw New InvalidOperationException("boom")
    End Function

    Private Shared Function PrivateDir() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-t06-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return dir
    End Function

End Class
#End If
