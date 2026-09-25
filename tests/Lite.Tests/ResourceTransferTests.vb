#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' The write path (PV-5), FileManager's routing into it (PV-4), the one refusal decision
''' (PV-7, acceptance 6), the resource registry (PV-2) and LocalProvider's parity with the
''' scan it replaced - 006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md.
'''
''' In the "ResourceRouting" collection with ResourceSessionTests: routing is one process-wide
''' attachment, so the classes that attach a session must not run side by side.
''' </summary>
<Collection("ResourceRouting")>
Public Class ResourceTransferTests
    Implements IDisposable

    Private ReadOnly workDir As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-transfer-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    Private Function LocalFile(name As String, size As Integer) As String
        Dim p As String = Path.Combine(workDir, "local", name)
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        File.WriteAllBytes(p, Enumerable.Repeat(CByte(7), size).ToArray())
        Return p
    End Function

    Private Function NewProvider(Optional capabilities As ProviderCapabilities = Nothing) As ResourceTestProvider
        Return New ResourceTestProvider(Path.Combine(workDir, "remote"), capabilities)
    End Function

    ' ------------------------------------------------------------------ PV-5 ----

    <Fact>
    Public Sub An_upload_lands_complete_under_its_final_name()
        Dim provider As ResourceTestProvider = NewProvider()
        ResourceTransfer.UploadVerified(provider, LocalFile("a.jpg", 1000), "/MOV/a.jpg", False)
        Assert.Equal(1000L, New FileInfo(provider.BackingPath("/MOV/a.jpg")).Length)
        Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg" & FileManager.Partial_Move_Suffix)))
    End Sub

    <Fact>
    Public Sub A_truncated_upload_never_gets_the_final_name_and_the_source_stays()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.TruncateUploadsTo = 10
        Dim source As String = LocalFile("a.jpg", 1000)

        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveInto(provider, source, "/MOV/a.jpg", False))

        Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
        ' Left for the sweep and reported - never resumed into a final name.
        Assert.True(File.Exists(provider.BackingPath("/MOV/a.jpg" & FileManager.Partial_Move_Suffix)))
        Assert.True(File.Exists(source))
    End Sub

    <Fact>
    Public Sub An_existing_destination_is_refused_before_a_byte_is_sent()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.PutFile("/MOV/a.jpg", New Byte(4) {})
        Assert.Throws(Of IOException)(Sub() ResourceTransfer.UploadVerified(provider, LocalFile("a.jpg", 10), "/MOV/a.jpg", False))
        Assert.Equal(0, provider.UploadCalls)
        Assert.Equal(5L, New FileInfo(provider.BackingPath("/MOV/a.jpg")).Length)
    End Sub

    <Fact>
    Public Sub A_move_out_downloads_verifies_and_only_then_deletes_the_remote()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.PutFile("/MOV/a.jpg", New Byte(299) {})
        Dim dest As String = Path.Combine(workDir, "out", "a.jpg")
        Directory.CreateDirectory(Path.GetDirectoryName(dest))

        ResourceTransfer.MoveOutOf(provider, "/MOV/a.jpg", dest, False)

        Assert.Equal(300L, New FileInfo(dest).Length)
        Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
    End Sub

    <Fact>
    Public Sub A_failed_move_out_keeps_the_remote_and_leaves_no_local_debris()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.PutFile("/MOV/a.jpg", New Byte(299) {})
        provider.TruncateDownloadsTo = 3
        Dim dest As String = Path.Combine(workDir, "out", "a.jpg")
        Directory.CreateDirectory(Path.GetDirectoryName(dest))

        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveOutOf(provider, "/MOV/a.jpg", dest, False))

        Assert.True(File.Exists(provider.BackingPath("/MOV/a.jpg")))
        Assert.False(File.Exists(dest))
        Assert.False(File.Exists(dest & FileManager.Partial_Move_Suffix))
        Assert.Equal(0, provider.DeleteCalls)
    End Sub

    <Fact>
    Public Sub A_move_within_uses_the_server_rename_when_there_is_one()
        Dim provider As ResourceTestProvider = NewProvider(New ProviderCapabilities(supportsServerSideRename:=True))
        provider.PutFile("/MOV/a.jpg", New Byte(49) {})
        ResourceTransfer.MoveWithin(provider, "/MOV/a.jpg", "/MOV/b.jpg", False, Path.Combine(workDir, "staging"))
        Assert.True(File.Exists(provider.BackingPath("/MOV/b.jpg")))
        Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
        Assert.Equal(0, provider.DownloadCalls)
    End Sub

    <Fact>
    Public Sub A_move_within_without_a_server_rename_goes_through_a_verified_pair()
        Dim provider As ResourceTestProvider = NewProvider(New ProviderCapabilities(supportsServerSideRename:=False))
        provider.PutFile("/MOV/a.jpg", New Byte(49) {})
        Dim staging As String = Path.Combine(workDir, "staging")
        ResourceTransfer.MoveWithin(provider, "/MOV/a.jpg", "/MOV/b.jpg", False, staging)
        Assert.Equal(50L, New FileInfo(provider.BackingPath("/MOV/b.jpg")).Length)
        Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
        Assert.Empty(Directory.GetFiles(staging))
    End Sub

    <Fact>
    Public Sub Provider_paths_split_and_join_with_their_own_separator()
        Assert.Equal("/MOV", ResourceTransfer.ParentOf("/MOV/a.jpg"))
        Assert.Equal("/", ResourceTransfer.ParentOf("/a.jpg"))
        Assert.Equal("\\nas\share", ResourceTransfer.ParentOf("\\nas\share\a.jpg"))
        Assert.Equal("/MOV/b.jpg", ResourceTransfer.Combine("/MOV", "b.jpg"))
        Assert.Equal("/b.jpg", ResourceTransfer.Combine("/", "b.jpg"))
        Assert.Equal("\\nas\share\b.jpg", ResourceTransfer.Combine("\\nas\share", "b.jpg"))
    End Sub

    ' ------------------------------------------------------- FileManager routing ----

    Private Function OpenRoutedSession(provider As ResourceTestProvider, slot As ResourceSessionSlot) As ResourceSession
        provider.PutFile("/MOV/a.jpg", New Byte(299) {})
        provider.PutFile("/MOV/b.jpg", New Byte(99) {})
        Dim session As New ResourceSession(
            New ResourceDescriptor With {.Id = "r", .Kind = ResourceKind.Sftp, .Host = "h", .RootPath = "/MOV"},
            provider, RemoteCacheStore.CreateSessionUnder(Path.Combine(workDir, "cache")), 0)
        session.LoadListing("/MOV", CancellationToken.None)
        slot.Enter(session)
        Return session
    End Function

    <Fact>
    Public Sub FileManager_moves_a_resource_file_out_on_the_resource_itself()
        Dim slot As New ResourceSessionSlot()
        Dim provider As ResourceTestProvider = NewProvider()
        Try
            Dim session As ResourceSession = OpenRoutedSession(provider, slot)
            Dim cached As String = session.Entries.Single(Function(e) e.Name = "a.jpg").LocalPath
            Dim dest As String = Path.Combine(workDir, "sorted", "a.jpg")
            Directory.CreateDirectory(Path.GetDirectoryName(dest))

            FileManager.MoveFile(cached, dest)

            Assert.Equal(300L, New FileInfo(dest).Length)
            Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
            Assert.False(File.Exists(cached))   ' invalidated

            ' And the undo carries it back - uploaded, verified, local copy gone.
            FileManager.MoveFile(dest, cached)
            Assert.Equal(300L, New FileInfo(provider.BackingPath("/MOV/a.jpg")).Length)
            Assert.False(File.Exists(dest))
        Finally
            slot.Leave()
        End Try
    End Sub

    <Fact>
    Public Sub FileManager_copies_delete_and_renames_through_the_resource()
        Dim slot As New ResourceSessionSlot()
        Dim provider As ResourceTestProvider = NewProvider()
        Try
            Dim session As ResourceSession = OpenRoutedSession(provider, slot)
            Dim a As String = session.Entries.Single(Function(e) e.Name = "a.jpg").LocalPath
            Dim b As String = session.Entries.Single(Function(e) e.Name = "b.jpg").LocalPath
            Dim copyDest As String = Path.Combine(workDir, "copies", "a.jpg")
            Directory.CreateDirectory(Path.GetDirectoryName(copyDest))

            FileManager.CopyFile(a, copyDest)
            Assert.Equal(300L, New FileInfo(copyDest).Length)
            Assert.True(File.Exists(provider.BackingPath("/MOV/a.jpg")))

            FileManager.DeleteFile(b)
            Assert.False(File.Exists(provider.BackingPath("/MOV/b.jpg")))

            Dim renamed As String = FileManager.RenameFile(a, "z.jpg")
            Assert.True(File.Exists(provider.BackingPath("/MOV/z.jpg")))
            Assert.False(File.Exists(provider.BackingPath("/MOV/a.jpg")))
            Assert.True(session.OwnsLocalPath(renamed))
            Assert.Equal("z.jpg", session.EntryAt(renamed).Name)
        Finally
            slot.Leave()
        End Try
    End Sub

    <Fact>
    Public Sub Without_a_resource_FileManager_is_plain_System_IO()
        Dim source As String = LocalFile("plain.jpg", 20)
        Dim dest As String = Path.Combine(workDir, "local", "plain2.jpg")
        Assert.False(ResourceRouting.TryCopy(source, dest, False))
        Assert.False(ResourceRouting.TryMove(source, dest, False))
        Assert.False(ResourceRouting.TryDelete(source))
        Dim renamed As String = Nothing
        Assert.False(ResourceRouting.TryRename(source, "x.jpg", renamed))
        Assert.True(File.Exists(source))
    End Sub

    ' ------------------------------------------------------ PV-7, acceptance 6 ----

    <Fact>
    Public Sub A_read_only_resource_refuses_every_verb_that_changes_it()
        Dim readOnlyCaps As New ProviderCapabilities(canWrite:=False, canDelete:=False,
                                                    canRename:=False, canCreateFolder:=False)
        Assert.False(ResourceRefusalPolicy.IsAllowed(readOnlyCaps, ResourceVerb.Move))
        Assert.False(ResourceRefusalPolicy.IsAllowed(readOnlyCaps, ResourceVerb.Delete))
        Assert.False(ResourceRefusalPolicy.IsAllowed(readOnlyCaps, ResourceVerb.Rename))
        Assert.False(ResourceRefusalPolicy.IsAllowed(readOnlyCaps, ResourceVerb.WriteBeside))
        ' Copying OUT only reads it.
        Assert.True(ResourceRefusalPolicy.IsAllowed(readOnlyCaps, ResourceVerb.Copy))

        For Each verb As ResourceVerb In [Enum].GetValues(GetType(ResourceVerb))
            Assert.True(ResourceRefusalPolicy.IsAllowed(ProviderCapabilities.LocalDisk, verb))
        Next
    End Sub

    ''' <summary>One refusal point, asked by every verb's entry: the archive-only function is
    ''' gone, and each surface that changes a file asks ResourceBlocksFileOperations.</summary>
    <Fact>
    Public Sub Every_file_verb_asks_the_one_refusal_point()
        Dim src As String = SourceDir()
        Dim all As String = String.Join(vbLf, Directory.GetFiles(src, "Main_Form*.vb").Select(AddressOf File.ReadAllText))
        Assert.DoesNotContain("ArchiveModeBlocksFileOperations", all)

        Assert.Contains("ResourceBlocksFileOperations(ResourceVerb.Rename)", File.ReadAllText(Path.Combine(src, "Main_Form.FileOperations.vb")))
        Assert.Contains("ResourceBlocksFileOperations(If(action = RecipientActionKind.Copy, ResourceVerb.Copy, ResourceVerb.Move))",
                        File.ReadAllText(Path.Combine(src, "Main_Form.FileOperations.vb")))
        Assert.Contains("ResourceBlocksFileOperations(ResourceVerb.Delete)", File.ReadAllText(Path.Combine(src, "Main_Form.MediaLoading.vb")))
        Assert.Contains("ResourceBlocksFileOperations(ResourceVerb.WriteBeside)", File.ReadAllText(Path.Combine(src, "Main_Form.ToVideo.vb")))
    End Sub

    ''' <summary>Acceptance 5's call-site half: every place the display path fetched an
    ''' archive entry now asks EnsureEntryLocal, and it comes before the existence check.</summary>
    <Fact>
    Public Sub The_display_path_fetches_before_it_checks_existence()
        Dim loading As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.MediaLoading.vb"))
        Assert.DoesNotContain("EnsureArchiveEntryOnDisk(", loading)
        Dim ensureAt As Integer = loading.IndexOf("EnsureEntryLocal(Current_File_Name)", StringComparison.Ordinal)
        Dim checkAt As Integer = loading.IndexOf("AndAlso Not File.Exists(Current_File_Name) Then", ensureAt, StringComparison.Ordinal)
        Assert.True(ensureAt >= 0, "EnsureEntryLocal is not called on the display path")
        Assert.True(checkAt > ensureAt, "the existence check must come after the fetch")
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Main_Form.Resource.vb")
            If File.Exists(candidate) Then Return Path.GetDirectoryName(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/Main_Form.Resource.vb not found above " & AppContext.BaseDirectory)
    End Function

    ' ------------------------------------------------------ LocalProvider parity ----

    <Fact>
    Public Sub LocalProvider_lists_what_the_old_scan_listed()
        Dim root As String = Path.Combine(workDir, "scan")
        Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"))
        File.WriteAllBytes(Path.Combine(root, "a.jpg"), New Byte(9) {})
        File.WriteAllBytes(Path.Combine(root, "b.txt"), New Byte(19) {})
        File.WriteAllBytes(Path.Combine(root, "sub", "c.png"), New Byte(29) {})
        File.WriteAllBytes(Path.Combine(root, "sub", "deeper", "d.gif"), New Byte(39) {})

        Dim flat = New LocalProvider(Function() False).ListEntries(root, CancellationToken.None)
        Dim expected = New DirectoryInfo(root).EnumerateFiles("*", SearchOption.TopDirectoryOnly).ToList()
        Assert.Equal(expected.Select(Function(f) f.FullName).OrderBy(Function(s) s),
                     flat.Select(Function(e) e.FullPath).OrderBy(Function(s) s))
        For Each f As FileInfo In expected
            Dim e As ResourceEntry = flat.Single(Function(x) x.FullPath = f.FullName)
            Assert.Equal(f.Name, e.Name)
            Assert.Equal(f.Length, e.Length)
            Assert.Equal(f.LastWriteTime, e.LastWriteUtc.ToLocalTime())
            Assert.Equal(f.FullName, e.LocalPath)
        Next

        Dim deep = New LocalProvider(Function() True).ListEntries(root, CancellationToken.None)
        Assert.Equal(4, deep.Count)
        Assert.Contains(deep, Function(e) e.Name = "d.gif")

        ' A missing root is a failure, not an empty folder (audit 048, A-07): it used to come
        ' back as an empty list, which the viewer reported as "the folder is empty".
        Assert.Throws(Of DirectoryNotFoundException)(
            Function() New LocalProvider(Function() True).ListEntries(Path.Combine(root, "missing"), CancellationToken.None))
        Assert.Throws(Of DirectoryNotFoundException)(
            Function() New LocalProvider(Function() False).ListEntries(Path.Combine(root, "missing"), CancellationToken.None))
        Assert.Empty(New LocalProvider(Function() False).ListEntries("", CancellationToken.None))
    End Sub

    ' --------------------------------------------------------- registry + locator ----

    <Fact>
    Public Sub The_registry_round_trips_and_refuses_what_it_must_not_hold()
        Dim stored As String = ResourceRegistry.Serialize({
            New ResourceDescriptor With {.Id = "a", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "10.0.0.2", .Port = 22, .UserName = "fms", .RootPath = "/MOV", .SecretHandle = "cred:a"},
            New ResourceDescriptor With {.Id = "b", .Kind = ResourceKind.Local, .RootPath = "C:\photos"},
            New ResourceDescriptor With {.Id = "c", .Kind = ResourceKind.Sftp, .Host = ""}
        })
        Dim back = ResourceRegistry.Parse(stored)
        Assert.Single(back)
        Assert.Equal("NAS", back(0).DisplayName)
        Assert.Equal(ResourceKind.Sftp, back(0).Kind)
        Assert.Equal("cred:a", back(0).SecretHandle)
        Assert.Contains("""Sftp""", stored)

        Assert.Empty(ResourceRegistry.Parse("{not json"))
        Assert.Empty(ResourceRegistry.Parse(""))
        Assert.Single(ResourceRegistry.Parse(
            "[{""Id"":""x"",""Kind"":""Sftp"",""Host"":""h""},{""Id"":""X"",""Kind"":""Sftp"",""Host"":""h2""}]"))

        ' A handle, never a secret: nothing on the descriptor can carry one.
        Dim names = GetType(ResourceDescriptor).GetProperties().Select(Function(p) p.Name.ToLowerInvariant())
        Assert.DoesNotContain(names, Function(n) n.Contains("password") OrElse n.Contains("key") OrElse n = "secret")
    End Sub

    <Fact>
    Public Sub A_locator_is_built_and_read_back()
        Dim resource As New ResourceDescriptor With {.Id = "a", .Kind = ResourceKind.Sftp, .Host = "192.168.1.100", .Port = 55259, .UserName = "fms", .RootPath = "/MOV"}
        Dim locator As String = ResourceLocator.Build(resource, "/MOV/2024")
        Assert.Equal("sftp://fms@192.168.1.100:55259/MOV/2024", locator)
        Assert.Equal("sftp://fms@192.168.1.100:55259/MOV", ResourceLocator.Build(resource, ""))

        Dim user As String = Nothing, host As String = Nothing, remote As String = Nothing
        Dim port As Integer
        Assert.True(ResourceLocator.TryParse(locator, user, host, port, remote))
        Assert.Equal("fms", user)
        Assert.Equal("192.168.1.100", host)
        Assert.Equal(55259, port)
        Assert.Equal("/MOV/2024", remote)

        Assert.False(ResourceLocator.TryParse("C:\photos", user, host, port, remote))
        Assert.False(ResourceLocator.TryParse("sftp://h:99999/x", user, host, port, remote))
        Assert.False(ResourceLocator.IsLocator("\\nas\share"))
    End Sub

End Class
#End If
