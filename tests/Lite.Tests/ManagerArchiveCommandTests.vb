#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Threading
Imports Xunit

Public Class ManagerArchiveCommandTests
    Implements IDisposable
    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-manager-archive-test-" & Guid.NewGuid().ToString("N"))
    Public Sub New()
        Directory.CreateDirectory(root)
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Directory.Delete(root, True)
    End Sub

    Private Function Prepare(source As ManagerFakeProvider, destination As ManagerFakeProvider, Optional remove As Boolean = True,
                             Optional collision As String = "rename", Optional extract As Boolean = False) As ManagerArchiveJob
        Dim request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source),
            {If(extract, "/root/input.zip", "/root/tree")}, ManagerTestLease(destination, "dest"), collision:=collision, leaf:="output.zip")
        Return ManagerArchiveJob.Prepare(request, New ManagerArchiveOptions With {.DeleteOriginals = remove, .Extract = extract}, root)
    End Function
    Private Shared Function SourceTree() As ManagerFakeProvider
        Dim provider As New ManagerFakeProvider()
        provider.PutDirectory("/root/tree")
        provider.PutDirectory("/root/tree/empty")
        provider.PutFile("/root/tree/.hidden.txt", "one")
        provider.Entries("/root/tree/.hidden.txt").IsHidden = True
        provider.PutFile("/root/tree/opaque.fd-sec", "sealed bytes")
        Return provider
    End Function

    <Fact>
    Public Sub Archive_dialog_never_preselects_cleanup_and_disables_it_for_readonly_sources()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         Dim place As New ManagerPlace("dialog", "Local fixture", ResourceKind.Local, root)
                                         For Each allowed As Boolean In {False, True}
                                             Using dialog As New ManagerArchiveForm(ManagerLocation.AtRoot(place), Function() Nothing, allowed)
                                                 Assert.False(dialog.DeleteOriginals)
                                                 Dim check As System.Windows.Forms.CheckBox = dialog.Controls(0).Controls.OfType(Of System.Windows.Forms.CheckBox)().Single()
                                                 Assert.Equal(allowed, check.Enabled)
                                                 Assert.False(check.Checked)
                                                 Assert.EndsWith(".zip", dialog.ArchiveName, StringComparison.Ordinal)
                                                 Assert.Equal(System.Windows.Forms.DialogResult.Cancel, DirectCast(dialog.CancelButton, System.Windows.Forms.Button).DialogResult)
                                             End Using
                                         Next
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)))
        If failure IsNot Nothing Then Throw failure
    End Sub
    Private Shared Function WithCleanup(job As ManagerArchiveJob) As BatchPlan
        Return job.AddCleanup(BuildManagerTestPlan(job.Request),
            Function(lease, entry) New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.UserAsked})
    End Function
    Private Sub Execute(op As BatchOp)
        op.ManagerOperation.Execute(Path.Combine(root, "transfer"))
    End Sub

    <Fact>
    Public Sub Default_options_keep_sources_and_verified_remote_zip_matches_the_complete_manifest()
        Assert.False(New ManagerArchiveOptions().DeleteOriginals)
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        Dim job As ManagerArchiveJob = Prepare(source, destination, remove:=False)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            Assert.Single(plan.Operations)
            Execute(plan.Operations(0))
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
            Using stream As New MemoryStream(destination.Bytes("/root/output.zip")), zip As New ZipArchive(stream, ZipArchiveMode.Read)
                Assert.Equal({"tree/", "tree/empty/", "tree/.hidden.txt", "tree/opaque.fd-sec"}.OrderBy(Function(s) s), zip.Entries.Select(Function(e) e.FullName).OrderBy(Function(s) s))
                Using reader As New StreamReader(zip.GetEntry("tree/opaque.fd-sec").Open())
                    Assert.Equal("sealed bytes", reader.ReadToEnd())
                End Using
            End Using
        End Using
        Assert.Empty(Directory.GetDirectories(root, "manager-archive-*"))
    End Sub

    <Fact>
    Public Sub Authorized_cleanup_removes_only_archived_files_then_empty_directories()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        Dim job As ManagerArchiveJob = Prepare(source, destination)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            For Each op As BatchOp In plan.Operations
                Execute(op)
            Next
            Assert.Equal(2, source.DeleteCalls)
            Assert.Equal(2, source.RemoveDirectoryCalls)
            Assert.False(source.Entries.ContainsKey("/root/tree"))
            Assert.True(destination.Entries.ContainsKey("/root/output.zip"))
        End Using
    End Sub

    <Theory>
    <InlineData(True)> <InlineData(False)>
    Public Sub Failed_or_cancelled_publication_never_authorizes_cleanup(corrupt As Boolean)
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider With {.CorruptUpload = corrupt}
        Dim job As ManagerArchiveJob = Prepare(source, destination)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            If Not corrupt Then request.Cancel()
            For Each op As BatchOp In plan.Operations
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
        End Using
    End Sub

    <Theory>
    <InlineData("changed")> <InlineData("added")> <InlineData("cancel")> <InlineData("archive-changed")>
    Public Sub A_change_or_cancellation_after_publication_preserves_the_entire_source_tree(change As String)
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        Dim job As ManagerArchiveJob = Prepare(source, destination)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            Execute(plan.Operations(0))
            Select Case change
                Case "changed" : source.PutFile("/root/tree/.hidden.txt", "new")
                Case "added" : source.PutFile("/root/tree/added", "new")
                Case "cancel" : request.Cancel()
                Case Else : destination.PutFile("/root/output.zip", "replaced")
            End Select
            For Each op As BatchOp In plan.Operations.Skip(1)
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
            Assert.True(destination.Entries.ContainsKey("/root/output.zip"))
        End Using
    End Sub

    <Fact>
    Public Sub A_change_during_partial_cleanup_blocks_the_rest_of_that_tree()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        Dim job As ManagerArchiveJob = Prepare(source, destination)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            Execute(plan.Operations(0))
            Execute(plan.Operations(1))
            source.PutFile("/root/tree/opaque.fd-sec", "changed")
            For Each op As BatchOp In plan.Operations.Skip(2)
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.Equal(1, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Corrupt_final_readback_is_reported_as_failed_publication_with_zero_source_deletes()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        destination.DownloadHook = Sub(path, local)
                                       If path = "/root/output.zip" Then File.WriteAllText(local, "broken readback")
                                   End Sub
        Dim job As ManagerArchiveJob = Prepare(source, destination)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            For Each op As BatchOp In plan.Operations
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.False(plan.Operations(0).ManagerOperation.Completed)
            Assert.Null(plan.Operations(0).ManagerOperation.UndoData)
            Assert.Contains("/root/output.zip", BatchReport.DetailText(New BatchItemResult With {
                .State = BatchItemState.Failed, .Detail = "readback failed", .Op = plan.Operations(0)}))
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal(0, source.RemoveDirectoryCalls)
            Assert.True(destination.Entries.ContainsKey("/root/output.zip"))
        End Using
    End Sub

    <Fact>
    Public Sub A_source_changed_during_download_fails_the_complete_archive_before_publication()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        source.DownloadHook = Sub(path, local) source.PutFile(path, "changed during archive read")
        Assert.Equal(ManagerFailure.SourceChanged, Assert.Throws(Of ManagerCommandException)(Function() Prepare(source, destination)).Reason)
        Assert.False(destination.Entries.ContainsKey("/root/output.zip"))
        Assert.Equal(0, source.DeleteCalls)
        Assert.Empty(Directory.GetDirectories(root, "manager-archive-*"))
    End Sub

    <Fact>
    Public Sub Skipped_zip_collision_preserves_every_original_and_existing_archive()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider()
        destination.PutFile("/root/output.zip", "existing")
        Dim job As ManagerArchiveJob = Prepare(source, destination, collision:="skip")
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            Assert.Single(plan.Skipped)
            For Each op As BatchOp In plan.Operations
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.Equal(0, source.DeleteCalls)
            Assert.Equal("existing", Text.Encoding.UTF8.GetString(destination.Bytes("/root/output.zip")))
        End Using
    End Sub

    <Fact>
    Public Sub Readonly_destination_and_output_inside_selected_tree_are_refused_before_any_deletion()
        Dim source As ManagerFakeProvider = SourceTree()
        Dim destination As New ManagerFakeProvider With {.Caps = New ProviderCapabilities(canWrite:=False)}
        Assert.Throws(Of ManagerCommandException)(Function() Prepare(source, destination))
        source = SourceTree()
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/tree"},
            ManagerTestLease(source), "/root/tree", leaf:="output.zip")
            Assert.Throws(Of ManagerCommandException)(Function() ManagerArchiveJob.Prepare(request, New ManagerArchiveOptions(), root))
            Assert.Equal(0, source.DeleteCalls)
        End Using
    End Sub

    Private Shared Sub PutZip(provider As ManagerFakeProvider, ParamArray names As String())
        Using bytes As New MemoryStream()
            Using zip As New ZipArchive(bytes, ZipArchiveMode.Create, True)
                For Each name As String In names
                    Using writer As New StreamWriter(zip.CreateEntry(name).Open())
                        writer.Write("extracted")
                    End Using
                Next
            End Using
            provider.PutFile("/root/input.zip")
            provider.Bytes("/root/input.zip") = bytes.ToArray()
            provider.Entries("/root/input.zip").Length = bytes.Length
        End Using
    End Sub

    <Fact>
    Public Sub Remote_extraction_preserves_input_and_reports_partial_publication_without_target_rollback()
        Dim source As New ManagerFakeProvider()
        PutZip(source, "folder/readme.txt", "second.exe")
        Dim destination As New ManagerFakeProvider()
        destination.PutFile("/root/unrelated", "keep")
        Dim job As ManagerArchiveJob = Prepare(source, destination, extract:=True)
        Using request As ManagerCommandRequest = job.Request
            Dim plan As BatchPlan = WithCleanup(job)
            Execute(plan.Operations(0))
            request.Cancel()
            For Each op As BatchOp In plan.Operations.Skip(1)
                Assert.NotNull(Xunit.Record.Exception(Sub() Execute(op)))
            Next
            Assert.Equal(1, plan.Operations.Where(Function(op) op.ManagerOperation.Completed).Count())
            Assert.True(source.Entries.ContainsKey("/root/input.zip"))
            Assert.Equal(0, source.DeleteCalls)
            Assert.True(destination.Entries.ContainsKey("/root/unrelated"))
        End Using
    End Sub

    <Fact>
    Public Sub Extract_here_cannot_replace_its_own_input_archive()
        Dim source As New ManagerFakeProvider()
        PutZip(source, "input.zip")
        Using request As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, ManagerTestLease(source), {"/root/input.zip"}, ManagerTestLease(source), collision:="replace")
            Assert.Throws(Of ManagerCommandException)(Function() ManagerArchiveJob.Prepare(request, New ManagerArchiveOptions With {.Extract = True}, root))
            Assert.Equal(0, source.DeleteCalls)
            Assert.True(source.Entries.ContainsKey("/root/input.zip"))
        End Using
    End Sub
End Class

<Collection("ResourceRouting")>
Public Class ManagerArchiveLiveTests
    Implements IClassFixture(Of WritableServerFixture)
    Private ReadOnly server As WritableServerFixture
    Public Sub New(server As WritableServerFixture)
        Me.server = server
    End Sub

    <WritableLiveFact>
    Public Sub Live_ManagerArchive_zip_extract_and_verified_cleanup_use_real_SFTP_and_local_endpoints()
        Dim work As String = Path.Combine(Path.GetTempPath(), "fms-260-live-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(work)
        Try
            Dim descriptor As New ResourceDescriptor With {.Id = "archive-live", .DisplayName = "Archive fixture", .Kind = ResourceKind.Sftp,
                .Host = "127.0.0.1", .Port = server.Port, .UserName = server.User, .RootPath = "/MOV", .PinnedFingerprint = server.Fingerprint}
            Dim connected As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, server.Password, Function(fp, ct) False,
                Function(fp) False, Function() False, CancellationToken.None).GetAwaiter().GetResult()
            Assert.NotNull(connected.Provider)
            Dim place As New ManagerPlace("local-archive-live", "Local fixture", ResourceKind.Local, work)
            Using local As New ManagerEndpointLease(place, ManagerOpenResult.Ok(New LocalProvider(Function() False))),
                  remote As New ManagerEndpointLease(ManagerPlace.ForResource(descriptor), ManagerOpenResult.Ok(DirectCast(connected.Provider, IManagerDirectoryProvider), DirectCast(connected.Provider, IDisposable)))
                Dim target As String = "/MOV/archive-" & Guid.NewGuid().ToString("N")
                remote.Provider.CreateDirectory(target)
                remote.Provider.CreateDirectory(target & "/unpacked")
                Dim tree As String = Path.Combine(work, "tree")
                Directory.CreateDirectory(Path.Combine(tree, "empty"))
                File.WriteAllText(Path.Combine(tree, "readme.txt"), "archive live payload")
                File.WriteAllBytes(Path.Combine(tree, "zero.bin"), Array.Empty(Of Byte)())
                Dim options As New ManagerArchiveOptions With {.DeleteOriginals = True}
                Dim create As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, local.Fork(), {tree}, remote.Fork(), target, leaf:="verified.zip")
                Dim job As ManagerArchiveJob = ManagerArchiveJob.Prepare(create, options, Path.Combine(work, "scratch"))
                Using publication As ManagerCommandRequest = job.Request
                    Dim plan As BatchPlan = job.AddCleanup(BuildManagerTestPlan(publication),
                        Function(lease, entry) New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.UserAsked})
                    ExecuteManagerTestPlan(plan)
                End Using
                Assert.False(Directory.Exists(tree))
                Assert.Equal(StatOutcome.Found, remote.Directory.StatEntry(target & "/verified.zip").Outcome)
                Dim extract As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, remote.Fork(), {target & "/verified.zip"}, remote.Fork(), target & "/unpacked")
                job = ManagerArchiveJob.Prepare(extract, New ManagerArchiveOptions With {.Extract = True}, Path.Combine(work, "scratch"))
                Using publication As ManagerCommandRequest = job.Request
                    For Each row As ManagerEntry In publication.Entries
                        Dim current As ResourceEntry = ManagerTreePlan.RequireEntry(publication.Source, row.ProviderPath)
                        Assert.Equal(row.ModifiedUtc, current.LastWriteUtc)
                        Assert.Equal(row.VersionTag, current.VersionTag)
                        Assert.Equal(row.IsDirectory, current.IsDirectory)
                    Next
                    ExecuteManagerTestPlan(job.AddCleanup(BuildManagerTestPlan(publication), Nothing))
                End Using
                Dim checkedFile As String = Path.Combine(work, "readback")
                remote.Provider.Download(target & "/unpacked/tree/readme.txt", checkedFile, Nothing, CancellationToken.None)
                Assert.Equal("archive live payload", File.ReadAllText(checkedFile))
                Assert.True(remote.Directory.StatEntry(target & "/unpacked/tree/empty").Entry.IsDirectory)
                Assert.Equal(0L, remote.Directory.StatEntry(target & "/unpacked/tree/zero.bin").Entry.Length)
                Assert.Equal(StatOutcome.Found, remote.Directory.StatEntry(target & "/verified.zip").Outcome)
                ' Archive remote inputs back to local storage and clean only those unchanged inputs.
                create = MakeManagerRequest(ManagerCommandKind.Copy, remote.Fork(), {target & "/unpacked/tree"}, local.Fork(), work, leaf:="roundtrip.zip")
                job = ManagerArchiveJob.Prepare(create, options, Path.Combine(work, "scratch"))
                Using publication As ManagerCommandRequest = job.Request
                    ExecuteManagerTestPlan(job.AddCleanup(BuildManagerTestPlan(publication),
                        Function(lease, entry) New DeleteDecision With {.Outcome = DeleteOutcome.Permanent, .Reason = PermanentReason.UserAsked}))
                End Using
                Assert.Equal(StatOutcome.Missing, remote.Directory.StatEntry(target & "/unpacked/tree").Outcome)
                Using zip As ZipArchive = ZipFile.OpenRead(Path.Combine(work, "roundtrip.zip"))
                    Using reader As New StreamReader(zip.GetEntry("tree/readme.txt").Open())
                        Assert.Equal("archive live payload", reader.ReadToEnd())
                    End Using
                End Using
            End Using
        Finally
            Directory.Delete(work, True)
        End Try
    End Sub
End Class
#End If

