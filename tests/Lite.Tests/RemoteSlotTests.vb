#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' A recipient slot that names a folder of a saved resource (007 §4.2): the address arithmetic, the
''' verdict rules, the slot connections (who is dialled, when, and what a refusal does), the routing
''' of the file verbs into a server folder and back out of it (the undo), and the batch plan.
'''
''' In the "ResourceRouting" collection: the routing reads one process-wide set of slot connections,
''' so the classes that set it must not run side by side.
''' </summary>
<Collection("ResourceRouting")>
Public Class RemoteSlotTests
    Implements IDisposable

    Private Const Slot As String = "sftp://fms@nas:2222/MOV/Sorted"
    Private ReadOnly workDir As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-remote-slot-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        RemoteSlotConnections.Current?.Dispose()
        RemoteSlotConnections.Current = Nothing
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    Private Shared Function Nas(Optional pinned As String = "SHA256:pinned", Optional readOnlyHint As Boolean = False) As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "nas-1", .DisplayName = "Home NAS", .Kind = ResourceKind.Sftp, .Host = "nas", .Port = 2222,
            .UserName = "fms", .RootPath = "/MOV", .PinnedFingerprint = pinned, .ReadOnlyHint = readOnlyHint}
    End Function

    Private Function NewProvider(Optional capabilities As ProviderCapabilities = Nothing) As ResourceTestProvider
        Return New ResourceTestProvider(Path.Combine(workDir, "remote"), capabilities)
    End Function

    Private Function LocalFile(name As String, size As Integer) As String
        Dim p As String = Path.Combine(workDir, "local", name)
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        File.WriteAllBytes(p, Enumerable.Range(0, size).Select(Function(i) CByte(i Mod 251)).ToArray())
        Return p
    End Function

    ''' <summary>Slot connections over one fake server. <paramref name="dials"/> counts the dials;
    ''' <paramref name="failWith"/>, when set, is what every dial answers.</summary>
    Private Shared Function NewSlots(provider As IFileSystemProvider, descriptors As IEnumerable(Of ResourceDescriptor),
                                     storedPassword As String, dials As List(Of String),
                                     Optional failWith As ResourceFailure = Nothing) As RemoteSlotConnections
        Dim list As List(Of ResourceDescriptor) = descriptors.ToList()
        Return New RemoteSlotConnections(
            Function() list.ToList(),
            Function(d As ResourceDescriptor) storedPassword,
            Function(d As ResourceDescriptor, password As String, ct As CancellationToken)
                dials.Add(password)
                If failWith IsNot Nothing Then Return New ConnectOutcome(Nothing, failWith)
                Return New ConnectOutcome(provider, Nothing)
            End Function)
    End Function

    ' ------------------------------------------------------------ addresses ----

    <Fact>
    Public Sub A_file_name_appended_to_a_slot_address_parses_back_unchanged()
        For Each name As String In {"a.jpg", "my photo #1.jpg", "фото 100%.png", "a:b.jpg"}
            Dim file As String = ResourceLocator.AppendName(Slot, name)
            Dim user As String = "", host As String = "", port As Integer = 0, remote As String = ""
            Assert.True(ResourceLocator.TryParse(file, user, host, port, remote), file)
            Assert.Equal("/MOV/Sorted/" & name, remote)
            Assert.Equal(name, ResourceLocator.LeafName(file))
            Assert.Equal(Slot, ResourceLocator.ParentOf(file))
        Next
    End Sub

    <Fact>
    Public Sub Appending_never_doubles_or_eats_a_slash()
        Assert.Equal("sftp://nas/a.jpg", ResourceLocator.AppendName("sftp://nas", "a.jpg"))
        Assert.Equal("sftp://nas/a.jpg", ResourceLocator.AppendName("sftp://nas/", "a.jpg"))
        Assert.Equal("sftp://nas/MOV/a.jpg", ResourceLocator.AppendName("sftp://nas/MOV/", "a.jpg"))
        Assert.Equal("sftp://nas/", ResourceLocator.ParentOf("sftp://nas/a.jpg"))
        Assert.Equal("sftp://nas/", ResourceLocator.ParentOf("sftp://nas/"))
    End Sub

    <Fact>
    Public Sub Same_folder_folds_the_host_and_the_default_port_but_not_the_path_or_the_user()
        Assert.True(ResourceLocator.SameFolder("sftp://fms@NAS/MOV", "sftp://fms@nas:22/MOV/"))
        Assert.False(ResourceLocator.SameFolder("sftp://fms@nas/MOV", "sftp://fms@nas/mov"))
        Assert.False(ResourceLocator.SameFolder("sftp://fms@nas/MOV", "sftp://other@nas/MOV"))
        Assert.False(ResourceLocator.SameFolder("sftp://fms@nas:2222/MOV", "sftp://fms@nas/MOV"))
    End Sub

    <Fact>
    Public Sub Slot_paths_glue_a_folder_on_this_PC_and_a_server_folder_each_its_own_way()
        Assert.Equal(Path.Combine("D:\Sorted", "a.jpg"), SlotPaths.Combine("D:\Sorted", "a.jpg"))
        Assert.Equal(Slot & "/a.jpg", SlotPaths.Combine(Slot, "a.jpg"))
        Assert.Equal(Slot, SlotPaths.FolderOf(Slot & "/a.jpg"))
        Assert.Equal("my photo.jpg", SlotPaths.FileNameOf(Slot & "/my%20photo.jpg"))
        Assert.True(SlotPaths.SameFolder(Slot & "/a.jpg", "sftp://fms@NAS:2222/MOV/Sorted/b.jpg"))
        Assert.False(SlotPaths.SameFolder(Slot & "/a.jpg", "D:\MOV\Sorted\a.jpg"))
        Assert.True(SlotPaths.SameFolder("D:\A\x.jpg", "d:\a\y.jpg"))
    End Sub

    ' ------------------------------------------------------------ policy ----

    <Fact>
    Public Sub A_server_folder_gets_the_connect_budget_on_top_of_the_plain_probe_bound()
        Assert.Equal(SlotHealthPolicy.Probe_Timeout_Ms, RemoteSlotPolicy.ProbeTimeoutMs("D:\Sorted"))
        Assert.True(RemoteSlotPolicy.ProbeTimeoutMs(Slot) > SlotHealthPolicy.Probe_Timeout_Ms + 5000 - 1)
    End Sub

    <Fact>
    Public Sub A_refusal_is_denied_a_dead_link_is_unreachable_and_no_such_file_is_missing()
        Assert.Equal(SlotState.Denied, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.Auth)))
        Assert.Equal(SlotState.Denied, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.KeyMismatch)))
        Assert.Equal(SlotState.Denied, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.SftpStatus, 3)))
        Assert.Equal(SlotState.Missing, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.SftpStatus, 2)))
        Assert.Equal(SlotState.Unreachable, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.Transport)))
        Assert.Equal(SlotState.Unreachable, RemoteSlotPolicy.StateFor(New ResourceFailure(ResourceFailureKind.Timeout)))
        Assert.Equal(SlotState.Unreachable, RemoteSlotPolicy.StateFor(Nothing))
    End Sub

    <Fact>
    Public Sub Only_the_last_segment_may_be_created_and_only_under_a_parent_that_answered()
        Dim now As DateTime = DateTime.UtcNow
        Dim folder As StatResult = StatResult.Found(New ResourceEntry With {.IsDirectory = True})
        Dim missing As StatResult = StatResult.Missing()
        Assert.Equal(SlotState.Ready, RemoteSlotPolicy.Verdict(folder, Nothing, True, True, True, now).State)
        Assert.Equal(SlotState.Invalid, RemoteSlotPolicy.Verdict(StatResult.Found(New ResourceEntry()), Nothing, True, True, True, now).State)
        Assert.Equal(SlotState.WillBeCreated, RemoteSlotPolicy.Verdict(missing, folder, True, True, True, now).State)
        Assert.Equal(SlotState.Missing, RemoteSlotPolicy.Verdict(missing, folder, True, True, False, now).State)
        Assert.Equal(SlotState.Missing, RemoteSlotPolicy.Verdict(missing, folder, True, False, True, now).State)
        Assert.Equal(SlotState.Missing, RemoteSlotPolicy.Verdict(missing, missing, True, True, True, now).State)
        Assert.Equal(SlotState.Unreachable, RemoteSlotPolicy.Verdict(missing, StatResult.Failed(New ResourceFailure(ResourceFailureKind.Transport)), True, True, True, now).State)
        ' A resource that cannot write is refused whatever the folder says.
        Assert.Equal(SlotState.Denied, RemoteSlotPolicy.Verdict(folder, Nothing, False, True, True, now).State)
    End Sub

    ' ------------------------------------------------------------ what a slot names ----

    <Fact>
    Public Sub A_slot_is_located_through_the_saved_resources_and_each_gap_has_its_own_name()
        Dim dials As New List(Of String)()
        Dim d As ResourceDescriptor = Nothing
        Dim remote As String = ""

        Assert.Equal(RemoteSlotProblem.NotRemote, NewSlots(NewProvider(), {Nas()}, "pw", dials).Locate("D:\Sorted", d, remote))
        Assert.Equal(RemoteSlotProblem.Invalid, NewSlots(NewProvider(), {Nas()}, "pw", dials).Locate("sftp://nas:99999/x", d, remote))
        Assert.Equal(RemoteSlotProblem.UnknownResource, NewSlots(NewProvider(), {Nas()}, "pw", dials).Locate("sftp://fms@other:2222/MOV", d, remote))
        Assert.Equal(RemoteSlotProblem.NotPinned, NewSlots(NewProvider(), {Nas(pinned:="")}, "pw", dials).Locate(Slot, d, remote))
        Assert.Equal(RemoteSlotProblem.ReadOnlyResource, NewSlots(NewProvider(), {Nas(readOnlyHint:=True)}, "pw", dials).Locate(Slot, d, remote))
        Assert.Equal(RemoteSlotProblem.NoCredential, NewSlots(NewProvider(), {Nas()}, Nothing, dials).Locate(Slot, d, remote))

        Assert.Equal(RemoteSlotProblem.None, NewSlots(NewProvider(), {Nas()}, "pw", dials).Locate(Slot, d, remote))
        Assert.Equal("nas-1", d.Id)
        Assert.Equal("/MOV/Sorted", remote)
        ' Locating never dials.
        Assert.Empty(dials)
    End Sub

    <Fact>
    Public Sub A_slot_is_captioned_with_its_resource_name_and_folder()
        Dim slots As RemoteSlotConnections = NewSlots(NewProvider(), {Nas()}, "pw", New List(Of String)())
        Assert.Equal("«Home NAS» /MOV/Sorted", slots.Caption(Slot))
        Assert.Equal("", slots.Caption("D:\Sorted"))
    End Sub

    ' ------------------------------------------------------------ the probe ----

    <Fact>
    Public Sub A_live_server_folder_is_ready_and_twenty_questions_dial_once()
        Dim provider As ResourceTestProvider = NewProvider()
        Directory.CreateDirectory(provider.BackingPath("/MOV/Sorted"))
        Dim dials As New List(Of String)()
        Dim slots As RemoteSlotConnections = NewSlots(provider, {Nas()}, "pw", dials)

        For i As Integer = 1 To 20
            Assert.Equal(SlotState.Ready, slots.Probe(Slot, allowCreate:=True).State)
        Next
        Assert.Equal({"pw"}, dials.ToArray())
        Assert.True(slots.IsConnected("nas-1"))
    End Sub

    <Fact>
    Public Sub A_missing_last_segment_under_a_live_parent_will_be_created_and_is_created_once()
        Dim provider As ResourceTestProvider = NewProvider()
        Directory.CreateDirectory(provider.BackingPath("/MOV"))
        Dim slots As RemoteSlotConnections = NewSlots(provider, {Nas()}, "pw", New List(Of String)())

        Assert.Equal(SlotState.WillBeCreated, slots.Probe(Slot, allowCreate:=True).State)
        Assert.Equal(SlotState.Missing, slots.Probe(Slot, allowCreate:=False).State)
        Assert.True(slots.EnsureFolder(Slot))
        Assert.True(Directory.Exists(provider.BackingPath("/MOV/Sorted")))
        Assert.False(slots.EnsureFolder(Slot))
    End Sub

    <Fact>
    Public Sub A_refused_password_is_denied_and_is_not_offered_again_until_a_new_one_is_typed()
        Dim dials As New List(Of String)()
        Dim slots As RemoteSlotConnections = NewSlots(NewProvider(), {Nas()}, "stored-wrong", dials,
                                                      failWith:=New ResourceFailure(ResourceFailureKind.Auth))

        Assert.Equal(SlotState.Denied, slots.Probe(Slot, allowCreate:=True).State)
        Assert.True(slots.WasRefused("nas-1"))
        Dim d As ResourceDescriptor = Nothing
        Dim remote As String = ""
        ' The stored one is not tried a second time: the gate asks the user instead.
        Assert.Equal(RemoteSlotProblem.NoCredential, slots.Locate(Slot, d, remote))
        Assert.Equal(1, dials.Count)

        slots.RememberCredential("nas-1", "typed")
        Assert.False(slots.WasRefused("nas-1"))
        Assert.Equal(RemoteSlotProblem.None, slots.Locate(Slot, d, remote))
    End Sub

    <Fact>
    Public Sub A_dead_link_is_unreachable_and_the_next_question_dials_again()
        Dim dials As New List(Of String)()
        Dim slots As RemoteSlotConnections = NewSlots(NewProvider(), {Nas()}, "pw", dials,
                                                      failWith:=New ResourceFailure(ResourceFailureKind.Transport))
        Assert.Equal(SlotState.Unreachable, slots.Probe(Slot, allowCreate:=True).State)
        Assert.Equal(SlotState.Unreachable, slots.Probe(Slot, allowCreate:=True).State)
        Assert.Equal(2, dials.Count)
        Assert.False(slots.WasRefused("nas-1"))
    End Sub

    <Fact>
    Public Sub A_read_only_resource_is_never_dialled_by_the_probe()
        Dim dials As New List(Of String)()
        Dim slots As RemoteSlotConnections = NewSlots(NewProvider(), {Nas(readOnlyHint:=True)}, "pw", dials)
        Assert.Equal(SlotState.Denied, slots.Probe(Slot, allowCreate:=True).State)
        Assert.Empty(dials)
    End Sub

    ' ------------------------------------------------------------ the verbs ----

    <Fact>
    Public Sub A_move_into_a_server_folder_is_verified_the_source_goes_and_undo_brings_it_back()
        Dim provider As ResourceTestProvider = NewProvider()
        Directory.CreateDirectory(provider.BackingPath("/MOV/Sorted"))
        RemoteSlotConnections.Current = NewSlots(provider, {Nas()}, "pw", New List(Of String)())
        Dim source As String = LocalFile("IMG_0001.jpg", 4000)
        Dim original As Byte() = File.ReadAllBytes(source)
        Dim destination As String = SlotPaths.Combine(Slot, "IMG_0001.jpg")

        Dim outcome As TransferOutcome = Nothing
        FileManager.MoveFileTracked(source, destination, False, outcome)

        Assert.Equal(TransferResult.Completed, outcome.Result)
        Assert.False(File.Exists(source))
        Assert.Equal(original, File.ReadAllBytes(provider.BackingPath("/MOV/Sorted/IMG_0001.jpg")))

        Dim record As UndoRecord = TransferUndo.Record(outcome)
        Assert.NotNull(record)
        Dim undone As TransferOutcome = TransferUndo.Undo(record, ResourceRouting.TransferScratchDir)
        Assert.Equal(TransferResult.Completed, undone.Result)
        Assert.Equal(original, File.ReadAllBytes(source))
        Assert.False(File.Exists(provider.BackingPath("/MOV/Sorted/IMG_0001.jpg")))
    End Sub

    <Fact>
    Public Sub A_copy_into_a_server_folder_keeps_the_source_and_refuses_to_overwrite()
        Dim provider As ResourceTestProvider = NewProvider()
        Directory.CreateDirectory(provider.BackingPath("/MOV/Sorted"))
        RemoteSlotConnections.Current = NewSlots(provider, {Nas()}, "pw", New List(Of String)())
        Dim source As String = LocalFile("a.jpg", 1500)
        Dim destination As String = SlotPaths.Combine(Slot, "a.jpg")

        FileManager.CopyFile(source, destination)
        Assert.True(File.Exists(source))
        Assert.Equal(1500L, New FileInfo(provider.BackingPath("/MOV/Sorted/a.jpg")).Length)
        Assert.True(RemoteSlotConnections.Current.FileExists(destination))
        Assert.False(RemoteSlotConnections.Current.FileExists(SlotPaths.Combine(Slot, "b.jpg")))

        ' Without "replace" an existing name is refused, and the file there is untouched.
        Dim other As String = LocalFile(Path.Combine("x", "a.jpg"), 99)
        Assert.ThrowsAny(Of Exception)(Sub() FileManager.CopyFile(other, destination))
        Assert.Equal(1500L, New FileInfo(provider.BackingPath("/MOV/Sorted/a.jpg")).Length)
    End Sub

    <Fact>
    Public Sub A_copy_in_a_server_folder_is_never_judged_through_FileInfo_for_its_undo()
        ' FileInfo would call the address "not there" - and so safe to delete.
        Assert.False(FileManager.CopyUndoMayDelete(LocalFile("a.jpg", 10), Slot & "/a.jpg"))
    End Sub

    <Fact>
    Public Sub Deleting_a_server_folder_file_goes_through_its_provider()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.PutFile("/MOV/Sorted/a.jpg", New Byte(9) {})
        RemoteSlotConnections.Current = NewSlots(provider, {Nas()}, "pw", New List(Of String)())
        FileManager.DeleteFile(Slot & "/a.jpg")
        Assert.False(File.Exists(provider.BackingPath("/MOV/Sorted/a.jpg")))
        Assert.Equal(1, provider.DeleteCalls)
    End Sub

    <Fact>
    Public Sub A_server_folder_with_no_slot_connections_fails_instead_of_writing_somewhere_else()
        RemoteSlotConnections.Current = Nothing
        Dim source As String = LocalFile("a.jpg", 10)
        Assert.Throws(Of ResourceFailureException)(Sub() FileManager.CopyFile(source, Slot & "/a.jpg"))
        Assert.True(File.Exists(source))
    End Sub

    ' ------------------------------------------------------------ the batch plan ----

    <Fact>
    Public Sub A_batch_into_a_server_folder_uses_the_real_names_and_the_servers_own_answer_about_them()
        Dim taken As New HashSet(Of String)(StringComparer.Ordinal) From {Slot & "/b.jpg"}
        Dim ctx As New BatchPlanContext With {
            .DestinationFolder = Slot,
            .SlotKey = "3",
            .CollisionPolicy = "rename",
            .FileExists = Function(p As String) If(ResourceLocator.IsLocator(p), taken.Contains(p), True),
            .TransferName = Function(p As String) Path.GetFileName(p).Substring(6)}   ' "00001_a.jpg" -> "a.jpg"

        Dim plan As BatchPlan = BatchPlan.Build({"C:\cache\00001_a.jpg", "C:\cache\00002_b.jpg"}, BatchIntent.CopyToSlot, ctx)

        Assert.Equal(2, plan.Operations.Count)
        Assert.Equal(Slot & "/a.jpg", plan.Operations(0).Destination)
        Assert.Equal(Slot & "/b%20(2).jpg", plan.Operations(1).Destination)
    End Sub

    <Fact>
    Public Sub A_batch_into_the_server_folder_the_files_came_from_is_skipped_as_already_there()
        Dim ctx As New BatchPlanContext With {
            .DestinationFolder = Slot,
            .FileExists = Function(p As String) Not ResourceLocator.IsLocator(p),
            .TransferName = Function(p As String) Path.GetFileName(p),
            .SourceLocation = Function(p As String) Slot & "/" & Path.GetFileName(p)}

        Dim plan As BatchPlan = BatchPlan.Build({"C:\cache\a.jpg"}, BatchIntent.MoveToSlot, ctx)

        Assert.Empty(plan.Operations)
        Assert.Equal(SkipReason.AlreadyInDestination, plan.Skipped.Single().Reason)
    End Sub

    <Fact>
    Public Sub A_free_name_in_a_server_folder_keeps_the_extension_and_the_escaping()
        Dim taken As New HashSet(Of String) From {Slot & "/a%20b.jpg", Slot & "/a%20b%20(2).jpg"}
        Assert.Equal(Slot & "/a%20b%20(3).jpg", BatchPlan.FreeName(Slot & "/a%20b.jpg", Function(p) taken.Contains(p), Nothing))
    End Sub

End Class
#End If
