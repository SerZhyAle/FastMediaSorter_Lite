Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Registered resources as a side of a scheduled operation (ticket 222, 4.3 and 8): the unattended rules, the
''' read-only and PIN-locked refusals, the checked move and the permanent delete - against an in-memory server,
''' so no connection, no credential and no real share is involved.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledResourceTests

    ''' <summary>An SFTP-like server held in memory: "/dir/name" paths, listing under a folder, upload and download.</summary>
    Private NotInheritable Class FakeServer
        Implements IFileSystemProvider, ITypedStatProvider

        Friend ReadOnly Files As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
        Friend ReadOnly Times As New Dictionary(Of String, DateTime)(StringComparer.Ordinal)
        Friend ReadOnly Folders As New HashSet(Of String)(StringComparer.Ordinal) From {"/"}
        Friend Disposed As Integer

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property

        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Return New ProviderCapabilities(canWrite:=True, canDelete:=True, canRename:=True, canCreateFolder:=True,
                    hasRecycleBin:=False, needsMaterialisation:=True, supportsServerSideRename:=True,
                    costHint:=ProviderCostHint.Slow, supportsCheckedSourceDelete:=True)
            End Get
        End Property

        Friend Sub Put(path As String, content As String, Optional writtenUtc As DateTime = Nothing)
            Files(path) = System.Text.Encoding.UTF8.GetBytes(content)
            Times(path) = If(writtenUtc = Nothing, New DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), writtenUtc)
            Dim parent As String = path.Substring(0, path.LastIndexOf("/"c))
            If parent.Length = 0 Then parent = "/"
            Folders.Add(parent)
        End Sub

        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Dim prefix As String = If(dirPath.EndsWith("/", StringComparison.Ordinal), dirPath, dirPath & "/")
            Dim result As New List(Of ResourceEntry)()
            For Each pair As KeyValuePair(Of String, Byte()) In Files
                If Not pair.Key.StartsWith(prefix, StringComparison.Ordinal) Then Continue For
                result.Add(Entry(pair.Key))
            Next
            Return result
        End Function

        Private Function Entry(path As String) As ResourceEntry
            Return New ResourceEntry With {.FullPath = path, .Name = path.Substring(path.LastIndexOf("/"c) + 1),
                .Length = Files(path).Length, .LastWriteUtc = Times(path)}
        End Function

        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            entry = Nothing
            If Files.ContainsKey(path) Then
                entry = Me.Entry(path)
                Return True
            End If
            Return False
        End Function

        Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
            If Files.ContainsKey(path) Then Return StatResult.Found(Entry(path))
            If Folders.Contains(path.TrimEnd("/"c)) OrElse path = "/" Then
                Return StatResult.Found(New ResourceEntry With {.FullPath = path, .Name = path, .IsDirectory = True})
            End If
            Return StatResult.Missing()
        End Function

        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            File.WriteAllBytes(localPath, Files(remotePath))
            progress?.Report(Files(remotePath).Length)
        End Sub

        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            Files(remotePath) = File.ReadAllBytes(localPath)
            Times(remotePath) = DateTime.UtcNow
            progress?.Report(Files(remotePath).Length)
        End Sub

        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            Files.Remove(remotePath)
            Times.Remove(remotePath)
        End Sub

        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Files(newRemotePath) = Files(remotePath)
            Times(newRemotePath) = Times(remotePath)
            Files.Remove(remotePath)
            Times.Remove(remotePath)
        End Sub

        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Folders.Add(remotePath.TrimEnd("/"c))
        End Sub
    End Class

    ''' <summary>A connected side that counts its disposal.</summary>
    Private NotInheritable Class CountingOwner
        Implements IDisposable
        Friend Count As Integer
        Public Sub Dispose() Implements IDisposable.Dispose
            Count += 1
        End Sub
    End Class

    Private NotInheritable Class Kit
        Friend ReadOnly Server As New FakeServer()
        Friend ReadOnly Owner As New CountingOwner()
        Friend ReadOnly Services As ScheduledServices
        Friend Problem As SideProblem = SideProblem.Ok
        Friend Facts As New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS", .RootPath = "/data"}
        Friend ReadOnly Binned As New List(Of String)()

        Friend Sub New()
            Services = New ScheduledServices With {
                .Clock = Function() New DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified),
                .VolumeFacts = Function(path As String) New DeleteVolumeFacts With {.Kind = DeleteVolumeKind.FixedDisk},
                .SendToBin = Function(path As String) As Boolean
                                 Binned.Add(path)
                                 File.Delete(path)
                                 Return False
                             End Function,
                .ResourceFacts = Function(id As String) Facts,
                .OpenSide = Function(location As ScheduledLocation) As ScheduledSideOpen
                                If location.Kind = ScheduledLocationKind.Path Then
                                    Return ScheduledSideOpen.Opened(LocalSide.ForLocation(location))
                                End If
                                If Problem.IsProblem Then Return ScheduledSideOpen.Failed(Problem)
                                Return ScheduledSideOpen.Opened(New ProviderSide(Server, "r1", location.Path, "NAS",
                                                                                  New RecursionFlag With {.Value = location.Recursive}, Owner))
                            End Function}
        End Sub

        Friend Function Run(op As ScheduledOperation) As ScheduledRunResult
            Return New ScheduledExecutor(Services).Run(op, CancellationToken.None)
        End Function
    End Class

    Private Shared Function Resource(folder As String) As ScheduledLocation
        Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r1", .ResourceName = "NAS", .Path = folder}
    End Function

    Private Shared Function Operation(action As ScheduledAction, source As ScheduledLocation, Optional destination As ScheduledLocation = Nothing) As ScheduledOperation
        Return New ScheduledOperation With {.Id = "x", .Name = "n", .Action = action, .Source = source,
            .Destination = If(destination, New ScheduledLocation())}
    End Function

    Private Shared Function PathLocation(path As String) As ScheduledLocation
        Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Path, .Path = path}
    End Function

    ' --- transfers ------------------------------------------------------------------------------------

    <Fact>
    Public Sub Copy_from_a_resource_keeps_the_structure_and_leaves_the_server_as_it_was()
        Using tree As New ScratchTree()
            Dim kit As New Kit()
            kit.Server.Put("/data/a.txt", "A")
            kit.Server.Put("/data/sub/b.txt", "BB")
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Copy, Resource("/data"), PathLocation(tree.Dir("out"))))
            Assert.True(r.Outcome = ScheduledRunOutcome.Ok, r.Outcome.ToString() & " / " & r.FirstError & " / " & r.Detail)
            Assert.Equal(2, r.Files)
            Assert.Equal("A", tree.Read("out\a.txt"))
            Assert.Equal("BB", tree.Read("out\sub\b.txt"))
            Assert.True(kit.Server.Files.ContainsKey("/data/a.txt"))
            ' The session is released after the run.
            Assert.Equal(1, kit.Owner.Count)
        End Using
    End Sub

    <Fact>
    Public Sub Copy_to_a_resource_uploads_through_the_checked_executor()
        Using tree As New ScratchTree()
            Dim kit As New Kit()
            tree.Write("src\a.txt", "A")
            tree.Write("src\deep\b.txt", "BB")
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Copy, PathLocation(tree.Dir("src")), Resource("/data/backup")))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(2, r.Files)
            Assert.Equal("A", System.Text.Encoding.UTF8.GetString(kit.Server.Files("/data/backup/a.txt")))
            Assert.Equal("BB", System.Text.Encoding.UTF8.GetString(kit.Server.Files("/data/backup/deep/b.txt")))
            ' Nothing is left behind under a staging name on the server.
            Assert.DoesNotContain(kit.Server.Files.Keys, Function(k) k.Contains(".fms-stage") OrElse k.Contains(".fms-backup") OrElse k.Contains(".fms-part"))
        End Using
    End Sub

    <Fact>
    Public Sub A_move_out_of_a_resource_removes_the_source_only_after_the_copy_is_proven()
        Using tree As New ScratchTree()
            Dim kit As New Kit()
            kit.Server.Put("/data/a.txt", "AAAA")
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Move, Resource("/data"), PathLocation(tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
            Assert.Equal(1, r.Files)
            Assert.Equal("AAAA", tree.Read("out\a.txt"))
            Assert.False(kit.Server.Files.ContainsKey("/data/a.txt"))
        End Using
    End Sub

    <Fact>
    Public Sub Conflict_skip_leaves_an_existing_file_on_the_server()
        Using tree As New ScratchTree()
            Dim kit As New Kit()
            tree.Write("src\a.txt", "new")
            kit.Server.Put("/data/a.txt", "old")
            Dim op As ScheduledOperation = Operation(ScheduledAction.Copy, PathLocation(tree.Dir("src")), Resource("/data"))
            Dim r As ScheduledRunResult = kit.Run(op)
            Assert.Equal(1, r.Skipped)
            Assert.Equal("old", System.Text.Encoding.UTF8.GetString(kit.Server.Files("/data/a.txt")))
            op.Conflict = ScheduledConflict.KeepBoth
            Dim again As ScheduledRunResult = kit.Run(op)
            Assert.Equal(1, again.Files)
            Assert.Equal("new", System.Text.Encoding.UTF8.GetString(kit.Server.Files("/data/a (2).txt")))
        End Using
    End Sub

    ' --- delete ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Delete_on_a_resource_is_permanent_and_confirmed_and_asks_nothing()
        Dim kit As New Kit()
        kit.Server.Put("/data/a.tmp", "x")
        kit.Server.Put("/data/b.tmp", "y")
        Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Delete, Resource("/data")))
        Assert.Equal(ScheduledRunOutcome.Ok, r.Outcome)
        Assert.Equal(2, r.Files)
        Assert.Equal("permanent:VolumeUnknown", r.Verdict)
        Assert.Empty(kit.Server.Files)
        Assert.Empty(kit.Binned)
    End Sub

    <Fact>
    Public Sub A_file_that_changed_on_the_server_after_the_listing_is_not_deleted()
        Dim kit As New Kit()
        kit.Server.Put("/data/a.tmp", "x")
        kit.Services.BeforeItem = Sub(relative As String)
                                      kit.Server.Put("/data/a.tmp", "changed and longer", DateTime.UtcNow)
                                  End Sub
        Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Delete, Resource("/data")))
        Assert.Equal(0, r.Files)
        Assert.Equal(1, r.Skipped)
        Assert.True(kit.Server.Files.ContainsKey("/data/a.tmp"))
    End Sub

    ' --- the unattended rules (4.3) -------------------------------------------------------------------

    <Theory>
    <InlineData(3, "source-no-credential|NAS")>
    <InlineData(4, "source-key-not-pinned|NAS")>
    <InlineData(1, "source-unreachable|NAS")>
    Public Sub A_resource_that_cannot_be_used_unattended_is_a_wall_never_a_question(kind As Integer, expected As String)
        Dim kit As New Kit()
        kit.Problem = New SideProblem(CType(kind, SideProblemKind), "NAS")
        Using tree As New ScratchTree()
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Copy, Resource("/data"), PathLocation(tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.Equal(ScheduledHalt.Wall, r.Halt)
            Assert.Equal(expected, r.Detail)
            Assert.Equal(0, r.Files)
        End Using
    End Sub

    <Fact>
    Public Sub A_removed_resource_halts_the_operation_instead_of_deleting_it()
        Dim kit As New Kit()
        kit.Facts = New ScheduledResourceFacts With {.Exists = False}
        Using tree As New ScratchTree()
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Copy, Resource("/data"), PathLocation(tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Refused, r.Outcome)
            Assert.Contains(r.Issues, Function(i) i.Code = ScheduledIssueCode.ResourceMissing)
        End Using
    End Sub

    <Fact>
    Public Sub A_read_only_resource_is_only_ever_copied_out_of()
        Dim kit As New Kit()
        kit.Facts.IsReadOnly = True
        Using tree As New ScratchTree()
            Dim copy As List(Of ScheduledIssue) = ScheduledValidation.Validate(
                Operation(ScheduledAction.Copy, Resource("/data"), PathLocation(tree.Dir("out"))), Nothing, kit.Services.ResourceFacts)
            Assert.Empty(copy)
            Dim moved As List(Of ScheduledIssue) = ScheduledValidation.Validate(
                Operation(ScheduledAction.Move, Resource("/data"), PathLocation(tree.Dir("out"))), Nothing, kit.Services.ResourceFacts)
            Assert.Contains(moved, Function(i) i.Code = ScheduledIssueCode.ResourceReadOnly)
            Dim deleted As List(Of ScheduledIssue) = ScheduledValidation.Validate(
                Operation(ScheduledAction.Delete, Resource("/data")), Nothing, kit.Services.ResourceFacts)
            Assert.Contains(deleted, Function(i) i.Code = ScheduledIssueCode.ResourceReadOnly)
            Dim into As List(Of ScheduledIssue) = ScheduledValidation.Validate(
                Operation(ScheduledAction.Copy, PathLocation(tree.Dir("src")), Resource("/data")), Nothing, kit.Services.ResourceFacts)
            Assert.Contains(into, Function(i) i.Code = ScheduledIssueCode.ResourceReadOnly)
        End Using
    End Sub

    <Fact>
    Public Sub A_pin_locked_or_tunnelled_resource_is_refused_at_save()
        Dim kit As New Kit()
        kit.Facts.IsPinLocked = True
        Dim locked As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Copy, Resource("/data"), PathLocation("D:\out")), Nothing, kit.Services.ResourceFacts)
        Assert.Contains(locked, Function(i) i.Code = ScheduledIssueCode.ResourcePinLocked)
        kit.Facts = New ScheduledResourceFacts With {.Exists = True, .DisplayName = "link", .IsUnsupported = True}
        Dim linked As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Copy, Resource("/data"), PathLocation("D:\out")), Nothing, kit.Services.ResourceFacts)
        Assert.Contains(linked, Function(i) i.Code = ScheduledIssueCode.ResourceUnsupported)
    End Sub

    <Fact>
    Public Sub The_folder_must_lie_inside_the_resources_root()
        Dim kit As New Kit()
        Dim inside As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Copy, Resource("/data/photos"), PathLocation("D:\out")), Nothing, kit.Services.ResourceFacts)
        Assert.Empty(inside)
        Dim outside As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Copy, Resource("/etc"), PathLocation("D:\out")), Nothing, kit.Services.ResourceFacts)
        Assert.Contains(outside, Function(i) i.Code = ScheduledIssueCode.ResourceFolderOutsideRoot)
        ' A sibling with the same prefix, and a climbing path, are not inside.
        Assert.False(ScheduledValidation.InsideRoot("/data2", "/data"))
        Assert.False(ScheduledValidation.InsideRoot("/data/../etc", "/data"))
        Assert.True(ScheduledValidation.InsideRoot("/data", "/data"))
        Assert.True(ScheduledValidation.InsideRoot("\\NAS\share\sub", "\\nas\SHARE"))
    End Sub

    <Fact>
    Public Sub Source_and_destination_in_one_resource_folder_is_refused_and_archive_takes_paths_only()
        Dim kit As New Kit()
        Dim same As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Copy, Resource("/data/a"), Resource("/data/a/")), Nothing, kit.Services.ResourceFacts)
        Assert.Contains(same, Function(i) i.Code = ScheduledIssueCode.SameResourceFolder)
        Dim archive As List(Of ScheduledIssue) = ScheduledValidation.Validate(
            Operation(ScheduledAction.Archive, Resource("/data"), PathLocation("D:\out")), Nothing, kit.Services.ResourceFacts)
        Assert.Contains(archive, Function(i) i.Code = ScheduledIssueCode.ArchiveNeedsPaths)
    End Sub

    <Fact>
    Public Sub An_unreachable_resource_listing_is_a_wall_with_the_place_named()
        Dim kit As New Kit()
        ' A resource whose root is not there: the listing is empty but the root stat says Missing.
        Using tree As New ScratchTree()
            Dim r As ScheduledRunResult = kit.Run(Operation(ScheduledAction.Copy, Resource("/data/none"), PathLocation(tree.Dir("out"))))
            Assert.Equal(ScheduledRunOutcome.Wall, r.Outcome)
            Assert.StartsWith("source-unreachable|", r.Detail, StringComparison.Ordinal)
            Assert.Equal(1, kit.Owner.Count)
        End Using
    End Sub

    ' --- the one constant the shim carries -------------------------------------------------------------

    <Fact>
    Public Sub The_in_flight_suffix_is_the_viewers_own()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing AndAlso Not File.Exists(Path.Combine(dir.FullName, "src", "FileManager.vb"))
            dir = dir.Parent
        End While
        Assert.NotNull(dir)
        Dim viewer As String = File.ReadAllText(Path.Combine(dir.FullName, "src", "FileManager.vb"))
        Assert.Contains("Public Const Partial_Move_Suffix As String = """ & FileManager.Partial_Move_Suffix & """", viewer)
    End Sub

End Class
