#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Specification 262 combined acceptance tests over the reproducible fixture tree (G1, G2, G3, G5, G8).
''' Consumes FMS262_FIXTURE_ROOT when invoked from tests\Integration\FileManagerScenes.ps1, or builds an
''' isolated disposable fixture when run standalone in Lite.Tests.
''' </summary>
Public Class ManagerCombinedAcceptanceTests
    Implements IDisposable

    Private ReadOnly workRoot As String

    Public Sub New()
        workRoot = Path.Combine(Path.GetTempPath(), "fms-262-combined-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workRoot)
        Dim supplied As String = Environment.GetEnvironmentVariable("FMS262_FIXTURE_ROOT")
        If Not String.IsNullOrWhiteSpace(supplied) AndAlso Directory.Exists(supplied) Then
            CopyDirectory(supplied, workRoot)
        Else
            SeedDefaultFixture(workRoot)
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workRoot) Then Directory.Delete(workRoot, recursive:=True)
        Catch
        End Try
    End Sub

    Private Shared Sub CopyDirectory(sourceDir As String, destDir As String)
        Directory.CreateDirectory(destDir)
        For Each filePath As String In Directory.GetFiles(sourceDir)
            Dim targetFile As String = Path.Combine(destDir, Path.GetFileName(filePath))
            File.Copy(filePath, targetFile, overwrite:=True)
            File.SetAttributes(targetFile, File.GetAttributes(filePath))
        Next
        For Each subDir As String In Directory.GetDirectories(sourceDir)
            Dim leaf As String = Path.GetFileName(subDir)
            If String.Equals(leaf, "scene_runner", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(leaf, "live_servers", StringComparison.OrdinalIgnoreCase) Then Continue For
            CopyDirectory(subDir, Path.Combine(destDir, leaf))
        Next
    End Sub

    Private Shared Sub SeedDefaultFixture(root As String)
        ' G1
        Directory.CreateDirectory(Path.Combine(root, "g1_nav", "empty_folder"))
        Directory.CreateDirectory(Path.Combine(root, "g1_nav", "sub_alpha"))
        Directory.CreateDirectory(Path.Combine(root, "g1_nav", "sub_beta"))
        File.WriteAllText(Path.Combine(root, "g1_nav", "sub_alpha", "item1.txt"), "alpha item 1")
        File.WriteAllText(Path.Combine(root, "g1_nav", "sub_beta", "item2.txt"), "beta item 2")

        ' G2
        Dim g2 As String = Path.Combine(root, "g2_activation")
        Directory.CreateDirectory(g2)
        File.WriteAllBytes(Path.Combine(g2, "sample_image.jpg"), New Byte() {&HFF, &HD8, &HFF, &HE0, 0, &H10, &H4A, &H46, &H49, &H46, 0, 1, 1, 1, 0, &H48, 0, &H48, 0, 0, &HFF, &HD9})
        File.WriteAllBytes(Path.Combine(g2, "sample_video.mp4"), New Byte() {0, 0, 0, &H18, &H66, &H74, &H79, &H70, &H69, &H73, &H6F, &H6D, 0, 0, 0, 1, &H69, &H73, &H6F, &H6D})
        File.WriteAllBytes(Path.Combine(g2, "sample_doc.pdf"), Encoding.ASCII.GetBytes("%PDF-1.4" & vbLf & "%EOF" & vbLf))
        File.WriteAllText(Path.Combine(g2, "readme.txt"), "Standard readable plain text document.")
        File.WriteAllText(Path.Combine(g2, "data.xyz123"), "Unknown binary or custom data extension.")
        File.WriteAllBytes(Path.Combine(g2, "blocked_app.exe"), New Byte() {&H4D, &H5A, &H90, 0, 3, 0, 0, 0})
        File.WriteAllText(Path.Combine(g2, "blocked_script.bat"), "@echo off" & vbCrLf & "echo test" & vbCrLf)
        File.WriteAllText(Path.Combine(g2, "blocked_ps.ps1"), "Write-Host ""test""" & vbCrLf)
        File.WriteAllText(Path.Combine(g2, "blocked_link.lnk"), "dummy-shortcut")
        File.WriteAllBytes(Path.Combine(g2, "protected_vault.fd-sec"), Encoding.ASCII.GetBytes("FDSEC-CONTAINER-ENCRYPTED-PAYLOAD-V1"))

        ' G3
        Dim g3a As String = Path.Combine(root, "g3_local", "folder_a")
        Directory.CreateDirectory(g3a)
        File.WriteAllText(Path.Combine(g3a, "doc_original.txt"), "Initial content for move/copy/rename")
        File.WriteAllText(Path.Combine(g3a, "case_rename.txt"), "Testing case-only rename")
        File.WriteAllText(Path.Combine(g3a, "to_delete.txt"), "Disposable item for recycle bin and permanent delete")
        Dim hiddenPath As String = Path.Combine(g3a, "hidden_child.txt")
        File.WriteAllText(hiddenPath, "Hidden file content")
        File.SetAttributes(hiddenPath, FileAttributes.Hidden)
        Directory.CreateDirectory(Path.Combine(root, "g3_local", "empty_child"))
        Directory.CreateDirectory(Path.Combine(root, "g3_local", "folder_b"))
        Directory.CreateDirectory(Path.Combine(root, "g3_local", "collision", "source"))
        Directory.CreateDirectory(Path.Combine(root, "g3_local", "collision", "target"))
        File.WriteAllText(Path.Combine(root, "g3_local", "collision", "source", "clash.txt"), "Newer version of file (source)")
        File.WriteAllText(Path.Combine(root, "g3_local", "collision", "target", "clash.txt"), "Existing version of file (destination)")

        ' G5
        Dim g5src As String = Path.Combine(root, "g5_archive", "source_files")
        Directory.CreateDirectory(Path.Combine(g5src, "empty_sub"))
        File.WriteAllText(Path.Combine(g5src, "file_one.txt"), "Archive candidate file 1")
        File.WriteAllText(Path.Combine(g5src, "file_two.txt"), "Archive candidate file 2")
        Dim safeZip As String = Path.Combine(root, "g5_archive", "safe_sample.zip")
        Using fs As FileStream = File.Create(safeZip),
              za As New IO.Compression.ZipArchive(fs, IO.Compression.ZipArchiveMode.Create)
            Using w1 As New StreamWriter(za.CreateEntry("inner_file.txt").Open())
                w1.Write("Content inside safe archive")
            End Using
            Using w2 As New StreamWriter(za.CreateEntry("sub/nested.txt").Open())
                w2.Write("Nested file inside archive")
            End Using
        End Using
        File.WriteAllBytes(Path.Combine(root, "g5_archive", "corrupt_readback.zip"), New Byte() {&H50, &H4B, 3, 4, 0, 0, 0, 0, &HDE, &HAD, &HBE, &HEF})
    End Sub

    <Fact>
    Public Async Function G1_RealFolderNavigation_ViewSortSelection_And_Bulk5000VirtualList() As Task
        Dim g1Root As String = Path.Combine(workRoot, "g1_nav")
        Dim place As ManagerPlace = ManagerPlace.ForDrive(workRoot, "G1")
        Dim nav As New ManagerNavigation()
        Using browser As New ManagerBrowser(Function(p, ct) ManagerOpenResult.Ok(New LocalProvider(Function() False)))
            ' Walk g1_nav -> sub_alpha -> sub_beta -> Back -> Forward -> Up -> empty_folder
            Dim r0 As ManagerListingResult = Await browser.Begin(ManagerIntent.Start, New ManagerLocation(place, g1Root))
            Assert.Equal(ManagerStatusKind.Ok, r0.Status)
            Assert.Equal(ManagerApply.Moved, nav.Apply(r0))
            Assert.Contains(r0.Entries, Function(e) e.IsDirectory AndAlso e.Name = "sub_alpha")
            Assert.Contains(r0.Entries, Function(e) e.IsDirectory AndAlso e.Name = "sub_beta")
            Assert.Contains(r0.Entries, Function(e) e.IsDirectory AndAlso e.Name = "empty_folder")

            Dim subAlpha As String = Path.Combine(g1Root, "sub_alpha")
            Dim rAlpha As ManagerListingResult = Await browser.Begin(ManagerIntent.Go, New ManagerLocation(place, subAlpha))
            Assert.Equal(ManagerApply.Moved, nav.Apply(rAlpha))
            Assert.Single(rAlpha.Entries)
            Assert.Equal("item1.txt", rAlpha.Entries(0).Name)

            Dim subBeta As String = Path.Combine(g1Root, "sub_beta")
            Dim rBeta As ManagerListingResult = Await browser.Begin(ManagerIntent.Go, New ManagerLocation(place, subBeta))
            Assert.Equal(ManagerApply.Moved, nav.Apply(rBeta))
            Assert.Single(rBeta.Entries)
            Assert.Equal("item2.txt", rBeta.Entries(0).Name)

            ' Back -> sub_alpha
            Dim rBack As ManagerListingResult = Await browser.Begin(ManagerIntent.Back, nav.PeekBack())
            Assert.Equal(ManagerApply.Moved, nav.Apply(rBack))
            Assert.True(nav.Current.SameAs(New ManagerLocation(place, subAlpha)))

            ' Forward -> sub_beta
            Dim rForward As ManagerListingResult = Await browser.Begin(ManagerIntent.Forward, nav.PeekForward())
            Assert.Equal(ManagerApply.Moved, nav.Apply(rForward))
            Assert.True(nav.Current.SameAs(New ManagerLocation(place, subBeta)))

            ' Up -> g1_nav
            Dim rUp As ManagerListingResult = Await browser.Begin(ManagerIntent.Up, nav.ParentTarget())
            Assert.Equal(ManagerApply.Moved, nav.Apply(rUp))
            Assert.True(nav.Current.SameAs(New ManagerLocation(place, g1Root)))

            ' Empty folder -> Ok status with 0 entries
            Dim emptyDir As String = Path.Combine(g1Root, "empty_folder")
            Dim rEmpty As ManagerListingResult = Await browser.Begin(ManagerIntent.Go, New ManagerLocation(place, emptyDir))
            Assert.Equal(ManagerStatusKind.Ok, rEmpty.Status)
            Assert.Empty(rEmpty.Entries)
            Assert.Equal(ManagerApply.Moved, nav.Apply(rEmpty))

            ' 20 mixed items: folders stay first under Name, Size, Date sort in both ascending and descending
            Dim mixedDir As String = Path.Combine(g1Root, "mixed_20")
            Directory.CreateDirectory(mixedDir)
            For i As Integer = 1 To 5
                Directory.CreateDirectory(Path.Combine(mixedDir, $"folder_{i:D2}"))
            Next
            For i As Integer = 1 To 15
                Dim f As String = Path.Combine(mixedDir, $"media_{i:D2}.jpg")
                File.WriteAllBytes(f, New Byte(i * 10 - 1) {})
                File.SetLastWriteTimeUtc(f, New DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i))
            Next
            Dim rMixed As ManagerListingResult = Await browser.Begin(ManagerIntent.Go, New ManagerLocation(place, mixedDir))
            Assert.Equal(20, rMixed.Entries.Count)
            Assert.Equal(ManagerApply.Moved, nav.Apply(rMixed))
            Dim selectedIds As String() = {rMixed.Entries(1).Id, rMixed.Entries(7).Id, rMixed.Entries(14).Id}
            nav.RememberSelection(selectedIds, selectedIds(1))
            Dim rows As List(Of PanelFileRow) = rMixed.Entries.Select(Function(e) e.ToRow()).ToList()

            For Each col As PanelSortColumn In {PanelSortColumn.Name, PanelSortColumn.Size, PanelSortColumn.Modified}
                For Each desc As Boolean In {False, True}
                    Dim sorted As List(Of PanelFileRow) = PanelSort.Order(rows, col, desc)
                    Assert.Equal(20, sorted.Count)
                    ' First 5 entries are always folders
                    For idx As Integer = 0 To 4
                        Assert.True(sorted(idx).IsDirectory)
                    Next
                    ' Selection ids survive view/sort switches unchanged
                    Dim kept As List(Of String) = nav.Reconcile(sorted.Select(Function(r) r.Identity))
                    Assert.Equal(3, kept.Count)
                    Assert.Equal(selectedIds(1), nav.FocusedId)
                Next
            Next

            ' 5,000-entry bulk folder check on a real folder + virtual ListView binding
            Dim bulkDir As String = Path.Combine(g1Root, "bulk_5000")
            If Not Directory.Exists(bulkDir) OrElse Directory.GetFiles(bulkDir).Length < 5000 Then
                Directory.CreateDirectory(bulkDir)
                Dim bytes As Byte() = Encoding.ASCII.GetBytes("FMS-BULK-DATA" & vbCrLf)
                For i As Integer = 1 To 5000
                    Dim p As String = Path.Combine(bulkDir, $"file_{i:D4}.txt")
                    If Not File.Exists(p) Then File.WriteAllBytes(p, bytes)
                Next
            End If

            Dim sw As Stopwatch = Stopwatch.StartNew()
            Dim rBulk As ManagerListingResult = Await browser.Begin(ManagerIntent.Go, New ManagerLocation(place, bulkDir))
            Dim visibleBulk As List(Of ManagerEntry) = rBulk.Entries.Where(Function(e) ManagerContentPolicy.IsVisible(e, ManagerContentMode.All, showHiddenFiles:=False)).ToList()
            sw.Stop()
            Assert.Equal(ManagerStatusKind.Ok, rBulk.Status)
            Assert.Equal(5000, visibleBulk.Count)
            Assert.True(sw.ElapsedMilliseconds < 5000, $"5,000-entry listing took {sw.ElapsedMilliseconds} ms")

            ' Verify WinForms VirtualMode ListView binding on STA thread
            Dim staError As Exception = Nothing
            Dim staThread As New Thread(Sub()
                                            Try
                                                Using lv As New ListView With {.View = View.Details, .VirtualMode = True, .VirtualListSize = rBulk.Entries.Count}
                                                    lv.Columns.Add("Name", 240)
                                                    lv.Columns.Add("Type", 100)
                                                    lv.Columns.Add("Size", 100)
                                                    lv.Columns.Add("Modified", 140)
                                                    AddHandler lv.RetrieveVirtualItem, Sub(s, e)
                                                                                           Dim row As ManagerEntry = rBulk.Entries(e.ItemIndex)
                                                                                           e.Item = New ListViewItem( New String() {row.Name, "TXT", row.SizeBytes.ToString(), row.ModifiedUtc.ToString("u")})
                                                                                       End Sub
                                                    lv.CreateControl()
                                                    Assert.Equal(5000, lv.VirtualListSize)
                                                    Assert.Equal("file_0001.txt", lv.Items(0).Text)
                                                    Assert.Equal("file_2500.txt", lv.Items(2499).Text)
                                                    Assert.Equal("file_5000.txt", lv.Items(4999).Text)
                                                End Using
                                            Catch ex As Exception
                                                staError = ex
                                            End Try
                                        End Sub)
            staThread.SetApartmentState(ApartmentState.STA)
            staThread.Start()
            Assert.True(staThread.Join(TimeSpan.FromSeconds(10)))
            If staError IsNot Nothing Then Throw staError
        End Using
    End Function

    <Fact>
    Public Async Function G2_RealFixtureActivation_MediaHandoff_SafeShellOpen_And_MandatoryDenySet() As Task
        Dim g2Dir As String = Path.Combine(workRoot, "g2_activation")
        Dim place As ManagerPlace = ManagerPlace.ForDrive(workRoot, "G2")
        Using browser As New ManagerBrowser(Function(p, ct) ManagerOpenResult.Ok(New LocalProvider(Function() True)))
            Dim listing As ManagerListingResult = Await browser.Begin(ManagerIntent.Start, New ManagerLocation(place, g2Dir))
            Assert.Equal(ManagerStatusKind.Ok, listing.Status)
            Dim byName As Dictionary(Of String, ManagerEntry) = listing.Entries.
                Where(Function(e) ManagerContentPolicy.IsVisible(e, ManagerContentMode.All, showHiddenFiles:=True)).
                ToDictionary(Function(e) e.Name, StringComparer.OrdinalIgnoreCase)

            ' Media and .fd-sec -> ViewerHandoff
            For Each mediaName As String In {"sample_image.jpg", "sample_video.mp4", "protected_vault.fd-sec"}
                Dim decision As ManagerOpenDecision = ManagerOpenPolicy.Evaluate(byName(mediaName), "default")
                Assert.Equal(ManagerOpenKind.ViewerHandoff, decision.Kind)
            Next

            ' Documents and unknown non-executable -> ExternalOpen, verified through LauncherSpy
            Dim launched As New List(Of Tuple(Of String, Boolean))()
            Dim prevSpy = ManagerShellOpen.LauncherSpy
            ManagerShellOpen.LauncherSpy = Function(path, usePicker, hwnd)
                                               launched.Add(Tuple.Create(path, usePicker))
                                               Return True
                                           End Function
            Try
                For Each docName As String In {"sample_doc.pdf", "readme.txt", "data.xyz123"}
                    Dim entry As ManagerEntry = byName(docName)
                    Dim decisionDefault As ManagerOpenDecision = ManagerOpenPolicy.Evaluate(entry, "default")
                    Assert.Equal(ManagerOpenKind.ExternalOpen, decisionDefault.Kind)
                    Assert.False(decisionDefault.UsePicker)
                    Assert.True(ManagerShellOpen.OpenFile(IntPtr.Zero, entry.DiskPath, decisionDefault.UsePicker))

                    Dim decisionPicker As ManagerOpenDecision = ManagerOpenPolicy.Evaluate(entry, "openWith")
                    Assert.Equal(ManagerOpenKind.ExternalOpen, decisionPicker.Kind)
                    Assert.True(decisionPicker.UsePicker)
                    Assert.True(ManagerShellOpen.OpenFile(IntPtr.Zero, entry.DiskPath, decisionPicker.UsePicker))
                Next
                Assert.Equal(6, launched.Count)

                ' Mandatory deny set (.exe, .bat, .ps1, .lnk) -> Blocked, and OpenFile refuses to launch
                launched.Clear()
                For Each blockedName As String In {"blocked_app.exe", "blocked_script.bat", "blocked_ps.ps1", "blocked_link.lnk"}
                    Dim entry As ManagerEntry = byName(blockedName)
                    Dim decision As ManagerOpenDecision = ManagerOpenPolicy.Evaluate(entry, "default")
                    Assert.Equal(ManagerOpenKind.Blocked, decision.Kind)
                    Assert.False(ManagerShellOpen.OpenFile(IntPtr.Zero, entry.DiskPath, usePicker:=False))
                    Assert.False(ManagerShellOpen.OpenFile(IntPtr.Zero, entry.DiskPath, usePicker:=True))
                Next
                Assert.Empty(launched)
            Finally
                ManagerShellOpen.LauncherSpy = prevSpy
            End Try
        End Using
    End Function

    <Fact>
    Public Sub G3_RealLocalCommands_CopyMoveRenameCaseOnly_HiddenFilter_Collisions_And_Delete()
        Dim g3Dir As String = Path.Combine(workRoot, "g3_local")
        Dim folderA As String = Path.Combine(g3Dir, "folder_a")
        Dim folderB As String = Path.Combine(g3Dir, "folder_b")
        Dim place As ManagerPlace = ManagerPlace.ForDrive(workRoot, "G3")

        ' Hidden files filter check on folder_a: ListChildren returns unfiltered entries with IsHidden=True,
        ' while ManagerContentPolicy.IsVisible filters hidden items when showHiddenFiles is False.
        Dim localProv As New LocalProvider(Function() False)
        Dim rawListing As DirectoryListing = DirectCast(localProv, IManagerDirectoryProvider).ListChildren(folderA, CancellationToken.None)
        Dim entries As List(Of ManagerEntry) = rawListing.Entries.Select(Function(r) ManagerEntry.FromResource(place, r)).ToList()
        Assert.Contains(entries, Function(e) String.Equals(e.Name, "hidden_child.txt", StringComparison.OrdinalIgnoreCase) AndAlso e.IsHidden)
        Assert.DoesNotContain(entries.Where(Function(e) ManagerContentPolicy.IsVisible(e, ManagerContentMode.All, showHiddenFiles:=False)),
                              Function(e) String.Equals(e.Name, "hidden_child.txt", StringComparison.OrdinalIgnoreCase))
        Assert.Contains(entries.Where(Function(e) ManagerContentPolicy.IsVisible(e, ManagerContentMode.All, showHiddenFiles:=True)),
                        Function(e) String.Equals(e.Name, "hidden_child.txt", StringComparison.OrdinalIgnoreCase))

        Using lease As New ManagerEndpointLease(place, ManagerOpenResult.Ok(New LocalProvider(Function() True)))
            ' 1. Copy doc_original.txt from folder_a to folder_b
            Dim docOrig As String = Path.Combine(folderA, "doc_original.txt")
            Using copyReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {docOrig}, lease.Fork(), folderB)
                ExecuteManagerTestPlan(BuildManagerTestPlan(copyReq))
            End Using
            Assert.True(File.Exists(docOrig))
            Assert.Equal("Initial content for move/copy/rename", File.ReadAllText(Path.Combine(folderB, "doc_original.txt")))

            ' 2. Case-only rename on Windows NTFS: case_rename.txt -> CASE_RENAME.txt
            Dim caseOrig As String = Path.Combine(folderA, "case_rename.txt")
            Using renameReq As New ManagerCommandRequest(ManagerCommandKind.Rename, New ManagerLocation(place, folderA), 1,
                {ManagerTestRow(lease, caseOrig)}, lease.Fork(), leafName:="CASE_RENAME.txt")
                ExecuteManagerTestPlan(BuildManagerTestPlan(renameReq))
            End Using
            Dim actualNames As String() = Directory.GetFiles(folderA).Select(AddressOf Path.GetFileName).ToArray()
            Assert.Contains("CASE_RENAME.txt", actualNames, StringComparer.Ordinal)
            Assert.Equal("Testing case-only rename", File.ReadAllText(Path.Combine(folderA, "CASE_RENAME.txt")))

            ' 3. Collision handling on g3_local/collision (skip, rename/keep-both, overwrite)
            Dim colSource As String = Path.Combine(g3Dir, "collision", "source", "clash.txt")
            Dim colTargetDir As String = Path.Combine(g3Dir, "collision", "target")
            Dim colTargetFile As String = Path.Combine(colTargetDir, "clash.txt")

            ' Skip keeps destination unchanged
            Using skipReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {colSource}, lease.Fork(), colTargetDir, collision:="skip")
                ExecuteManagerTestPlan(BuildManagerTestPlan(skipReq))
            End Using
            Assert.Equal("Existing version of file (destination)", File.ReadAllText(colTargetFile))

            ' Rename (Keep both) creates a sibling copy without overwriting destination
            Using renameColReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {colSource}, lease.Fork(), colTargetDir, collision:="rename")
                ExecuteManagerTestPlan(BuildManagerTestPlan(renameColReq))
            End Using
            Assert.Equal("Existing version of file (destination)", File.ReadAllText(colTargetFile))
            Dim targetFiles As String() = Directory.GetFiles(colTargetDir)
            Assert.Equal(2, targetFiles.Length)

            ' Replace overwrites destination content
            Using overwriteReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {colSource}, lease.Fork(), colTargetDir, collision:="replace")
                ExecuteManagerTestPlan(BuildManagerTestPlan(overwriteReq))
            End Using
            Assert.Equal("Newer version of file (source)", File.ReadAllText(colTargetFile))

            ' 4. Delete file and empty directory
            Dim toDelete As String = Path.Combine(folderA, "to_delete.txt")
            Using delFileReq As New ManagerCommandRequest(ManagerCommandKind.DeletePermanently, New ManagerLocation(place, folderA), 1,
                {ManagerTestRow(lease, toDelete)}, lease.Fork(), useRecycleBin:=False)
                ExecuteManagerTestPlan(BuildManagerTestPlan(delFileReq))
            End Using
            Dim emptyChild As String = Path.Combine(g3Dir, "empty_child")
            Using delDirReq As New ManagerCommandRequest(ManagerCommandKind.DeletePermanently, New ManagerLocation(place, g3Dir), 1,
                {ManagerTestRow(lease, emptyChild)}, lease.Fork(), useRecycleBin:=False)
                ExecuteManagerTestPlan(BuildManagerTestPlan(delDirReq))
            End Using
            Assert.False(File.Exists(toDelete))
            Assert.False(Directory.Exists(emptyChild))
        End Using
    End Sub

    <Fact>
    Public Sub G5_RealFixtureArchives_CreateVerifiedZip_ExtractSafeZip_And_RefuseCorruptZip()
        Dim g5Dir As String = Path.Combine(workRoot, "g5_archive")
        Dim sourceTree As String = Path.Combine(g5Dir, "source_files")
        Dim safeZip As String = Path.Combine(g5Dir, "safe_sample.zip")
        Dim corruptZip As String = Path.Combine(g5Dir, "corrupt_readback.zip")
        Dim place As ManagerPlace = ManagerPlace.ForDrive(workRoot, "G5")

        Using lease As New ManagerEndpointLease(place, ManagerOpenResult.Ok(New LocalProvider(Function() True)))
            ' 1. Create verified ZIP from g5_archive/source_files (preserving source)
            Dim createReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {sourceTree}, lease.Fork(), g5Dir, leaf:="created_verified.zip")
            Dim createJob As ManagerArchiveJob = ManagerArchiveJob.Prepare(createReq, New ManagerArchiveOptions With {.DeleteOriginals = False}, Path.Combine(workRoot, "scratch-create"))
            Using pub As ManagerCommandRequest = createJob.Request
                ExecuteManagerTestPlan(createJob.AddCleanup(BuildManagerTestPlan(pub), Nothing))
            End Using
            Dim createdZipPath As String = Path.Combine(g5Dir, "created_verified.zip")
            Assert.True(File.Exists(createdZipPath))
            Assert.True(Directory.Exists(sourceTree))

            ' 2. Extract safe_sample.zip into g5_archive/extracted_safe
            Dim extractOut As String = Path.Combine(g5Dir, "extracted_safe")
            Directory.CreateDirectory(extractOut)
            Dim extractReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {safeZip}, lease.Fork(), extractOut)
            Dim extractJob As ManagerArchiveJob = ManagerArchiveJob.Prepare(extractReq, New ManagerArchiveOptions With {.Extract = True}, Path.Combine(workRoot, "scratch-extract"))
            Using pub As ManagerCommandRequest = extractJob.Request
                ExecuteManagerTestPlan(extractJob.AddCleanup(BuildManagerTestPlan(pub), Nothing))
            End Using
            Assert.Equal("Content inside safe archive", File.ReadAllText(Path.Combine(extractOut, "inner_file.txt")))
            Assert.Equal("Nested file inside archive", File.ReadAllText(Path.Combine(extractOut, "sub", "nested.txt")))

            ' 3. Refuse corrupt_readback.zip with zero partial files published
            Dim corruptOut As String = Path.Combine(g5Dir, "extracted_corrupt")
            Directory.CreateDirectory(corruptOut)
            Using corruptReq As ManagerCommandRequest = MakeManagerRequest(ManagerCommandKind.Copy, lease.Fork(), {corruptZip}, lease.Fork(), corruptOut)
                Dim ex As Exception = Xunit.Record.Exception(
                    Sub()
                        Dim corruptJob As ManagerArchiveJob = ManagerArchiveJob.Prepare(corruptReq, New ManagerArchiveOptions With {.Extract = True}, Path.Combine(workRoot, "scratch-corrupt"))
                        Using pub As ManagerCommandRequest = corruptJob.Request
                            ExecuteManagerTestPlan(corruptJob.AddCleanup(BuildManagerTestPlan(pub), Nothing))
                        End Using
                    End Sub)
                Assert.NotNull(ex)
            End Using
            Assert.Empty(Directory.GetFileSystemEntries(corruptOut))
        End Using
    End Sub

    <Fact>
    Public Sub G8_WinFormsDialogs_ThemeAndRtlLayout_KeepTechnicalPathsLeftToRight()
        Dim staError As Exception = Nothing
        Dim staThread As New Thread(Sub()
                                        Try
                                            Dim place As ManagerPlace = ManagerPlace.ForDrive(workRoot, "Local Work")
                                            Dim loc As New ManagerLocation(place, workRoot)
                                            Using destForm As New ManagerDestinationForm({place}, loc),
                                                  archForm As New ManagerArchiveForm(loc, Function() Nothing, canDelete:=True)
                                                ' Simulate RTL locale (Arabic / Urdu) on dialog shell while technical path boxes stay LTR
                                                destForm.RightToLeft = RightToLeft.Yes
                                                destForm.RightToLeftLayout = True
                                                archForm.RightToLeft = RightToLeft.Yes
                                                archForm.RightToLeftLayout = True

                                                destForm.CreateControl()
                                                archForm.CreateControl()

                                                Dim destBoxes As TextBox() = destForm.Controls.OfType(Of TableLayoutPanel)().SelectMany(Function(t) t.Controls.OfType(Of TextBox)()).ToArray()
                                                Assert.Single(destBoxes)
                                                Assert.Equal(RightToLeft.No, destBoxes(0).RightToLeft)

                                                Dim archBoxes As TextBox() = archForm.Controls.OfType(Of TableLayoutPanel)().SelectMany(Function(t) t.Controls.OfType(Of TextBox)()).ToArray()
                                                Assert.NotEmpty(archBoxes)
                                                Assert.All(archBoxes, Sub(tb) Assert.Equal(RightToLeft.No, tb.RightToLeft))
                                            End Using
                                        Catch ex As Exception
                                            staError = ex
                                        End Try
                                    End Sub)
        staThread.SetApartmentState(ApartmentState.STA)
        staThread.Start()
        Assert.True(staThread.Join(TimeSpan.FromSeconds(10)))
        If staError IsNot Nothing Then Throw staError
    End Sub

End Class
#End If
