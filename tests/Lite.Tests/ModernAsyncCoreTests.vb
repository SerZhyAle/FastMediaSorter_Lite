Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' 014_SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md, the two parts that were left: U-03(c) the
''' folder walk off the UI thread, and U-01 the decoded-picture cache.
'''
''' The pure halves are asserted directly. What cannot be asserted without a window - that the
''' pipeline asks the scan before it reads the list, that a continuation does not take "the user
''' navigated" a second time, that the pool half touches no control, that every place a box loses
''' its picture goes through the one retire point - is held by reading the source, the same way the
''' audit tests of this project hold their rules (NavigationAndLoadingAuditTests).
''' </summary>
Public Class ModernAsyncCoreTests

    Private Shared ReadOnly AllModes As String() = {
        NavigationRules.ReadMode_Next, NavigationRules.ReadMode_Prev, NavigationRules.ReadMode_SetFile,
        NavigationRules.ReadMode_Files, NavigationRules.ReadMode_FolderAndFile,
        NavigationRules.ReadMode_FolderAndKnownFile, NavigationRules.ReadMode_Delete,
        NavigationRules.ReadMode_InSlideShow, NavigationRules.ReadMode_ForRandom,
        NavigationRules.ReadMode_ForSlideShow, NavigationRules.ReadMode_AfterUndo,
        NavigationRules.ReadMode_JumpBy, NavigationRules.ReadMode_JumpTo}

    ' ------------------------------------------------------------- U-03(c) ----

    <Fact>
    Public Sub S11_only_a_call_that_replaces_the_list_may_run_while_a_scan_is_in_flight()
        Dim replacing As New HashSet(Of String)(StringComparer.Ordinal) From {
            NavigationRules.ReadMode_Files, NavigationRules.ReadMode_FolderAndFile,
            NavigationRules.ReadMode_FolderAndKnownFile}
        For Each mode As String In AllModes
            Assert.Equal(replacing.Contains(mode), NavigationRules.IsListReplacingMode(mode))
        Next
        ' A mode nobody knows is not allowed through either.
        Assert.False(NavigationRules.IsListReplacingMode("NoSuchMode"))
    End Sub

    <Fact>
    Public Sub S11_a_folder_load_and_a_rescan_always_walk_the_folder()
        For Each loaded As Boolean In {True, False}
            For Each external As Boolean In {True, False}
                Assert.True(NavigationRules.ModeReadsFolder(NavigationRules.ReadMode_Files, loaded, external))
                Assert.True(NavigationRules.ModeReadsFolder(NavigationRules.ReadMode_FolderAndFile, loaded, external))
            Next
        Next
    End Sub

    <Fact>
    Public Sub S11_slideshow_and_random_walk_the_folder_only_when_the_list_is_not_its_scan()
        For Each mode As String In {NavigationRules.ReadMode_ForRandom, NavigationRules.ReadMode_ForSlideShow}
            Assert.True(NavigationRules.ModeReadsFolder(mode, False, False))
            Assert.False(NavigationRules.ModeReadsFolder(mode, True, False))
        Next
    End Sub

    <Fact>
    Public Sub S11_a_flip_walks_the_folder_only_while_an_external_file_is_the_whole_list()
        For Each mode As String In {NavigationRules.ReadMode_Next, NavigationRules.ReadMode_Prev}
            Assert.True(NavigationRules.ModeReadsFolder(mode, False, True))
            Assert.False(NavigationRules.ModeReadsFolder(mode, True, False))
            Assert.False(NavigationRules.ModeReadsFolder(mode, False, False))
        Next
    End Sub

    <Fact>
    Public Sub S11_nothing_else_walks_the_folder()
        ' An external open reads its folder at the first flip, not itself - and a delete, a jump,
        ' a tick of the running slideshow or the display after a file operation never do.
        For Each mode As String In {NavigationRules.ReadMode_FolderAndKnownFile, NavigationRules.ReadMode_Delete,
                                    NavigationRules.ReadMode_InSlideShow, NavigationRules.ReadMode_AfterUndo,
                                    NavigationRules.ReadMode_SetFile, NavigationRules.ReadMode_JumpBy,
                                    NavigationRules.ReadMode_JumpTo, "NoSuchMode"}
            For Each loaded As Boolean In {True, False}
                For Each external As Boolean In {True, False}
                    Assert.False(NavigationRules.ModeReadsFolder(mode, loaded, external), mode)
                Next
            Next
        Next
    End Sub

    <Fact>
    Public Sub S11_the_gate_stands_before_the_view_count_and_the_throttle()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "ReadShowMediaFile")
        Dim gate As Integer = body.IndexOf("IsFolderScanRunning()", StringComparison.Ordinal)
        Dim viewCount As Integer = body.IndexOf("media_View_Count += 1", StringComparison.Ordinal)
        Dim throttle As Integer = body.IndexOf("MonotonicClock.NowMs()", StringComparison.Ordinal)
        Assert.True(gate > 0, "the gate is missing")
        ' A refused call must not count as a view (the figure is persisted) nor stamp the throttle.
        Assert.True(gate < viewCount)
        Assert.True(gate < throttle)
        Assert.Contains("NavigationRules.IsListReplacingMode(read_Mode_Type)", body)
        ' The continuation is the user's one action: counted once, not throttled.
        Assert.Contains("If Not is_Continuation Then media_View_Count += 1", body)
        Assert.Contains("Not is_Auto_Skip AndAlso Not is_Continuation", body)
    End Sub

    <Fact>
    Public Sub S11_the_pipeline_asks_the_scan_before_it_reads_the_list_and_a_continuation_skips_the_navigation_stamp()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "UpdateFileIndexAndList")
        Dim ensure As Integer = body.IndexOf("EnsureFolderList(read_Mode_Type, is_Continuation)", StringComparison.Ordinal)
        Dim firstCase As Integer = body.IndexOf("Select Case read_Mode_Type", StringComparison.Ordinal)
        Assert.True(ensure > 0, "EnsureFolderList is not called")
        Assert.True(ensure < firstCase, "the scan must be asked before any case reads the list")
        Assert.Contains("If Not EnsureFolderList(read_Mode_Type, is_Continuation) Then Return False", body)

        ' open_Coordinator.NoteNavigation() inside the continuation would cancel a file that was
        ' double-clicked in Explorer while the folder was being read.
        Dim noteAt As Integer = body.IndexOf("open_Coordinator.NoteNavigation()", StringComparison.Ordinal)
        Dim guardAt As Integer = body.LastIndexOf("If Not is_Continuation Then", noteAt, StringComparison.Ordinal)
        Assert.True(guardAt >= 0 AndAlso guardAt < noteAt)
    End Sub

    <Fact>
    Public Sub S11_a_listing_nobody_took_does_not_survive_the_call()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "ReadShowMediaFile")
        Dim update As Integer = body.IndexOf("UpdateFileIndexAndList(", StringComparison.Ordinal)
        Dim clear As Integer = body.IndexOf("folder_Scan_Ready = Nothing", StringComparison.Ordinal)
        Assert.True(update > 0 AndAlso clear > update)
    End Sub

    <Fact>
    Public Sub S11_the_pool_half_of_the_scan_touches_no_window_state()
        Dim source As String = ReadSource("Main_Form.FolderScanAsync.vb")
        Dim pool As String = MethodBody(source, "ListFolderInBackground")
        ' The filter, the sort and the metadata table read state the UI thread owns, so they stay
        ' in GetFiles; the pool thread lists and nothing else.
        For Each forbidden As String In {"lbl_", "Current_Folder_Path", "all_Supported_Extensions", "cmbox_", "Picture_Box",
                                         "files_List", "files_Array", "modern_Preferences"}
            Assert.DoesNotContain(forbidden, pool)
        Next
        Assert.Contains("local_Provider.ListEntries(folder, token)", pool)
    End Sub

    <Fact>
    Public Sub S11_a_finished_scan_always_lowers_the_flag_before_it_may_return_early()
        ' If any early return stood before the flag came down, a scan that landed for a folder the
        ' window had since left would keep the gate up and refuse every flip for good.
        Dim body As String = MethodBody(ReadSource("Main_Form.FolderScanAsync.vb"), "FinishFolderScanAsync")
        Dim lowered As Integer = body.IndexOf("folder_Scan_Running = False", StringComparison.Ordinal)
        Dim stale As Integer = body.IndexOf("If Not folder_Scan_Requests.IsCurrent(ticket) Then Return", StringComparison.Ordinal)
        Assert.True(stale > 0 AndAlso lowered > stale, "a superseded scan leaves the state to its superseder")
        For Each early As String In {"If result Is Nothing", "IsDisposed OrElse is_Form_Closed",
                                     "Current_Folder_Path, folder, StringComparison.OrdinalIgnoreCase"}
            Dim at As Integer = body.IndexOf(early, StringComparison.Ordinal)
            Assert.True(at > lowered, early & " must come after the flag is lowered")
        Next
    End Sub

    <Fact>
    Public Sub S11_a_scan_failure_reaches_GetFiles_to_be_classified_like_a_synchronous_one()
        Dim source As String = ReadSource("Main_Form.FolderScanAsync.vb")
        Assert.Contains("ExceptionDispatchInfo.Capture(ready.Failure).Throw()", MethodBody(source, "TakeScannedListing"))
        Dim provider As String = MethodBody(ReadSource("Main_Form.Resource.vb"), "ProviderFileEntries")
        Assert.Contains("TakeScannedListing(folderPath)", provider)
        ' ...and the synchronous walk stays as the fallback when nothing is waiting.
        Assert.Contains("local_Provider.ListEntries(folderPath, Threading.CancellationToken.None)", provider)
    End Sub

    <Fact>
    Public Sub S11_only_a_plain_folder_is_walked_off_the_ui_thread()
        Dim body As String = MethodBody(ReadSource("Main_Form.FolderScanAsync.vb"), "CanScanInBackground")
        Assert.Contains("archive_Session IsNot Nothing", body)
        Assert.Contains("ActiveResourceSession() IsNot Nothing", body)
        Assert.Contains("Is_No_Background_Tasks", body)
    End Sub

    <Fact>
    Public Sub S11_the_x86_project_compiles_the_new_files_and_the_seams_hold()
        Dim vbproj As String = File.ReadAllText(Path.Combine(FindSourceDir(), "FastMediaSorter.vbproj"))
        For Each name As String In {"Main_Form.FolderScanAsync.vb", "MediaCache.vb", "Main_Form.MediaCache.vb"}
            Assert.Contains("<Compile Include=""" & name & """", vbproj)
            ' Whole-file modern seam: the file is one runtime's only.
            Dim text As String = ReadSource(name)
            Assert.StartsWith("#If Not NETFRAMEWORK Then", text.TrimStart(ChrW(&HFEFF)).TrimStart())
        Next
    End Sub

    ' ---------------------------------------------------------------- U-01 ----

    <Fact>
    Public Sub S11_prefetch_goes_the_way_the_user_is_going_and_wraps_at_both_ends()
        Assert.Equal(6, NavigationRules.PrefetchIndex(5, 10, False))
        Assert.Equal(4, NavigationRules.PrefetchIndex(5, 10, True))
        Assert.Equal(0, NavigationRules.PrefetchIndex(9, 10, False))
        Assert.Equal(9, NavigationRules.PrefetchIndex(0, 10, True))
        ' One file: it names itself, which the worker reads as "nothing to do".
        Assert.Equal(0, NavigationRules.PrefetchIndex(0, 1, False))
        Assert.Equal(0, NavigationRules.PrefetchIndex(0, 1, True))
        Assert.Equal(-1, NavigationRules.PrefetchIndex(0, 0, False))
        ' An index outside the list (a list that shrank under it) is held to the list.
        Assert.Equal(0, NavigationRules.PrefetchIndex(50, 3, False))
        Assert.Equal(1, NavigationRules.PrefetchIndex(-4, 3, False))
    End Sub

    <Fact>
    Public Sub S11_a_slideshow_and_a_freshly_opened_folder_or_file_both_start_going_forward()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "UpdateFileIndexAndList")
        For Each caseHead As String In {"Case Mode_InSlideShow", "Case Mode_FolderAndFile", "Case Mode_FolderAndKnownFile"}
            Dim at As Integer = body.IndexOf(caseHead, StringComparison.Ordinal)
            Assert.True(at > 0, caseHead)
            Dim nextCase As Integer = body.IndexOf(vbLf & "            Case ", at + caseHead.Length, StringComparison.Ordinal)
            Dim section As String = body.Substring(at, nextCase - at)
            Assert.Contains("is_Nav_Backwards = False", section)
        Next
    End Sub

    <Fact>
    Public Sub S11_the_net48_prefetch_keeps_decoding_the_file_after_this_one()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "UpdateCurrentFileAndDisplay")
        Dim modern As Integer = body.IndexOf("next_File_After_Current = PrefetchNeighbourPath()", StringComparison.Ordinal)
        Dim legacy As Integer = body.IndexOf("files_List(current_File_Index + 1)", StringComparison.Ordinal)
        Dim seamElse As Integer = body.LastIndexOf("#Else", legacy, StringComparison.Ordinal)
        Dim seamIf As Integer = body.LastIndexOf("#If Not NETFRAMEWORK Then", modern, StringComparison.Ordinal)
        Assert.True(modern > 0 AndAlso legacy > modern)
        Assert.True(seamIf > 0 AndAlso seamIf < modern AndAlso seamElse > modern AndAlso seamElse < legacy)
    End Sub

    <Fact>
    Public Sub S11_a_box_loses_its_picture_to_the_cache_only_through_the_retire_point()
        Dim placing As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "PlaceDecodedImage")
        Assert.Contains("RetireBoxPicture(1)", placing)
        Assert.Contains("RetireBoxPicture(2)", placing)
        Dim completed As String = MethodBody(ReadSource("Main_Form.FileScanning.vb"), "BgWorker_RunWorkerCompleted")
        Assert.Contains("RetireBoxPicture(1)", completed)
        Assert.Contains("RetireBoxPicture(2)", completed)
        Assert.Contains("box1_Origin = result.Origin", completed)
        Assert.Contains("box2_Origin = result.Origin", completed)

        ' The two sites that must NOT keep a picture dispose it themselves and forget the origin.
        Assert.Contains("ForgetBoxOrigin(box_Index)", MethodBody(ReadSource("Main_Form.FileOperations.vb"), "ReleasePictureBoxMedia"))
        Assert.Contains("media_Cache.Evict(Current_File_Name)", MethodBody(ReadSource("Main_Form.FileOperations.vb"), "ReleaseActiveMedia"))
    End Sub

    <Fact>
    Public Sub S11_the_origin_is_asked_before_the_decode_and_a_rotation_marks_the_picture()
        Dim scanning As String = ReadSource("Main_Form.FileScanning.vb")
        Dim doWork As String = MethodBody(scanning, "BgWorker_DoWork")
        Dim origin As Integer = doWork.IndexOf("OriginOf(next_File_After_Current_in_worker)", StringComparison.Ordinal)
        Dim decode As Integer = doWork.IndexOf("LoadImageWithStream(next_File_After_Current_in_worker)", StringComparison.Ordinal)
        Assert.True(origin > 0 AndAlso origin < decode, "a stamp taken after the decode could be NEWER than the pixels")

        Dim rotate As String = MethodBody(ReadSource("Main_Form.KeyboardInput.vb"), "RotateActiveImage")
        Assert.Contains("MarkBoxPictureMutated(1)", rotate)
        Assert.Contains("MarkBoxPictureMutated(2)", rotate)
    End Sub

    <Fact>
    Public Sub S11_a_new_list_and_the_shutdown_empty_the_cache()
        Assert.Contains("media_Cache.Clear()", MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "NoteFileListReplaced"))
        Assert.Contains("media_Cache.Clear()", ReadSource("Main_Form.Lifecycle.vb"))
    End Sub

#If Not NETFRAMEWORK Then

    ' ------------------------------------------------------- MediaCache ----

    Private Shared ReadOnly Stamp As New DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc)

    Private Shared Function Entry(path As String, Optional side As Integer = 10,
                                  Optional stampOffsetSeconds As Integer = 0) As MediaEntry
        Return New MediaEntry With {
            .Path = path,
            .WriteTimeUtc = Stamp.AddSeconds(stampOffsetSeconds),
            .Length = 1234,
            .ExifAutoRotate = True,
            .Picture = New Bitmap(side, side),
            .Data = New MemoryStream(New Byte() {1, 2, 3})
        }
    End Function

    ''' <summary>True when both halves of an entry, kept by the test BEFORE the offer, are
    ''' disposed (MediaEntry.Dispose also nulls its own properties, so they cannot be asked).</summary>
    Private Shared Function IsDisposed(picture As Image, data As MemoryStream) As Boolean
        Dim pictureGone As Boolean = False
        Try
            If picture.Width < 0 Then Return False
        Catch ex As ArgumentException
            pictureGone = True
        End Try
        Return pictureGone AndAlso Not data.CanRead
    End Function

    <Fact>
    Public Sub S11_a_picture_comes_back_once_and_with_both_halves()
        Dim cache As New MediaCache()
        Dim e As MediaEntry = Entry("C:\f\a.jpg")
        Dim picture As Image = e.Picture
        Dim data As MemoryStream = e.Data
        cache.Offer(e)
        Assert.Equal(1, cache.Count)

        Dim taken As MediaEntry = cache.TryTake("C:\f\a.jpg", Stamp, 1234, True)
        Assert.NotNull(taken)
        Assert.Same(picture, taken.Picture)
        Assert.Same(data, taken.Data)
        ' It left the cache: the box that receives it is its only owner now.
        Assert.Equal(0, cache.Count)
        Assert.Equal(0L, cache.TotalBytes)
        Assert.Null(cache.TryTake("C:\f\a.jpg", Stamp, 1234, True))
        taken.Dispose()
    End Sub

    <Fact>
    Public Sub S11_a_path_is_matched_the_way_windows_matches_it()
        Dim cache As New MediaCache()
        cache.Offer(Entry("C:\Photos\IMG_1.JPG"))
        Dim taken As MediaEntry = cache.TryTake("c:\photos\img_1.jpg", Stamp, 1234, True)
        Assert.NotNull(taken)
        taken.Dispose()
    End Sub

    <Fact>
    Public Sub S11_a_file_rewritten_on_disk_is_never_shown_from_memory()
        For Each scenario As Integer In {0, 1, 2}
            Dim cache As New MediaCache()
            Dim e As MediaEntry = Entry("C:\f\a.jpg")
            Dim picture As Image = e.Picture
            Dim data As MemoryStream = e.Data
            cache.Offer(e)

            Dim answer As MediaEntry =
                If(scenario = 0, cache.TryTake("C:\f\a.jpg", Stamp.AddSeconds(1), 1234, True),    ' another write time
                If(scenario = 1, cache.TryTake("C:\f\a.jpg", Stamp, 4321, True),                  ' another length
                                cache.TryTake("C:\f\a.jpg", Stamp, 1234, False)))                ' another decode setting
            Assert.Null(answer)
            ' The stale entry is disposed and gone, not kept for the next look.
            Assert.Equal(0, cache.Count)
            Assert.True(IsDisposed(picture, data))
        Next
    End Sub

    <Fact>
    Public Sub S11_the_oldest_picture_is_evicted_and_disposed_when_the_count_is_exceeded()
        Dim cache As New MediaCache(capacity:=3, budgetBytes:=Long.MaxValue)
        Dim pictures As New List(Of Image)()
        Dim streams As New List(Of MemoryStream)()
        For i As Integer = 1 To 5
            Dim e As MediaEntry = Entry("C:\f\" & i.ToString() & ".jpg")
            pictures.Add(e.Picture)
            streams.Add(e.Data)
            cache.Offer(e)
        Next
        Assert.Equal(3, cache.Count)
        Assert.Equal({"C:\f\5.jpg", "C:\f\4.jpg", "C:\f\3.jpg"}, cache.Paths())
        ' The two oldest went, both halves; the three newest are still alive.
        Assert.True(IsDisposed(pictures(0), streams(0)))
        Assert.True(IsDisposed(pictures(1), streams(1)))
        For i As Integer = 2 To 4
            Assert.True(pictures(i).Width > 0)
            Assert.True(streams(i).CanRead)
        Next
        cache.Clear()
    End Sub

    <Fact>
    Public Sub S11_the_pixel_budget_holds_even_below_the_count()
        ' 100x100 costs 40 000 bytes; a budget of 100 000 holds two.
        Dim cache As New MediaCache(capacity:=5, budgetBytes:=100000L)
        For i As Integer = 1 To 4
            cache.Offer(Entry("C:\f\" & i.ToString() & ".jpg", side:=100))
        Next
        Assert.Equal(2, cache.Count)
        Assert.Equal(80000L, cache.TotalBytes)
        Assert.Equal({"C:\f\4.jpg", "C:\f\3.jpg"}, cache.Paths())
        cache.Clear()
        Assert.Equal(0L, cache.TotalBytes)
    End Sub

    <Fact>
    Public Sub S11_a_picture_that_alone_exceeds_the_budget_is_dropped_without_emptying_the_cache()
        Dim cache As New MediaCache(capacity:=5, budgetBytes:=100000L)
        cache.Offer(Entry("C:\f\small.jpg", side:=50))
        Dim huge As MediaEntry = Entry("C:\f\huge.jpg", side:=400)    ' 640 000 bytes
        Dim picture As Image = huge.Picture
        Dim data As MemoryStream = huge.Data
        cache.Offer(huge)
        Assert.Equal({"C:\f\small.jpg"}, cache.Paths())
        Assert.True(IsDisposed(picture, data))
        cache.Clear()
    End Sub

    <Fact>
    Public Sub S11_offering_a_path_again_replaces_and_disposes_the_older_entry()
        Dim cache As New MediaCache()
        Dim older As MediaEntry = Entry("C:\f\a.jpg")
        Dim olderPicture As Image = older.Picture
        Dim olderData As MemoryStream = older.Data
        cache.Offer(older)
        cache.Offer(Entry("C:\f\a.jpg", stampOffsetSeconds:=5))
        Assert.Equal(1, cache.Count)
        Assert.True(IsDisposed(olderPicture, olderData))
        cache.Clear()
    End Sub

    <Fact>
    Public Sub S11_evict_and_clear_dispose_what_they_drop()
        Dim cache As New MediaCache()
        Dim a As MediaEntry = Entry("C:\f\a.jpg")
        Dim b As MediaEntry = Entry("C:\f\b.jpg")
        Dim aPicture As Image = a.Picture
        Dim aData As MemoryStream = a.Data
        Dim bPicture As Image = b.Picture
        Dim bData As MemoryStream = b.Data
        cache.Offer(a)
        cache.Offer(b)

        cache.Evict("C:\f\A.JPG")
        Assert.Equal({"C:\f\b.jpg"}, cache.Paths())
        Assert.True(IsDisposed(aPicture, aData))

        cache.Clear()
        Assert.Equal(0, cache.Count)
        Assert.Equal(0L, cache.TotalBytes)
        Assert.True(IsDisposed(bPicture, bData))
    End Sub

    <Fact>
    Public Sub S11_a_cache_of_no_capacity_keeps_nothing_and_leaks_nothing()
        Dim cache As New MediaCache(capacity:=0)
        Dim e As MediaEntry = Entry("C:\f\a.jpg")
        Dim picture As Image = e.Picture
        Dim data As MemoryStream = e.Data
        cache.Offer(e)
        Assert.Equal(0, cache.Count)
        Assert.True(IsDisposed(picture, data))
    End Sub

    <Fact>
    Public Sub S11_what_may_be_kept_is_decided_by_three_refusals()
        Assert.True(MediaCache.IsCacheable(True, False, False))
        Assert.False(MediaCache.IsCacheable(False, False, False), "no origin: nothing to compare against later")
        Assert.False(MediaCache.IsCacheable(True, True, False), "an animation's frame position is state")
        Assert.False(MediaCache.IsCacheable(True, False, True), "a rotated picture is no longer the file's pixels")
    End Sub

    <Fact>
    Public Sub S11_a_gif_is_recognised_as_animated_and_a_png_is_not()
        Using bmp As New Bitmap(8, 8)
            Using gifStream As New MemoryStream()
                bmp.Save(gifStream, ImageFormat.Gif)
                gifStream.Position = 0
                Using gif As Image = Image.FromStream(gifStream)
                    Assert.True(MediaCache.IsAnimatedFormat(gif))
                End Using
            End Using
            Using pngStream As New MemoryStream()
                bmp.Save(pngStream, ImageFormat.Png)
                pngStream.Position = 0
                Using png As Image = Image.FromStream(pngStream)
                    Assert.False(MediaCache.IsAnimatedFormat(png))
                End Using
            End Using
        End Using
        Assert.False(MediaCache.IsAnimatedFormat(Nothing))
    End Sub

#End If

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function FindSourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function ReadSource(fileName As String) As String
        Return File.ReadAllText(Path.Combine(FindSourceDir(), fileName)).Replace(vbCr, "")
    End Function

    ''' <summary>The text from a Sub/Function declaration to its End.</summary>
    Private Shared Function MethodBody(source As String, name As String) As String
        Dim m As Match = Regex.Match(source, "(Sub|Function)\s+" & Regex.Escape(name) & "\b[\s\S]*?\n    End (Sub|Function)")
        Assert.True(m.Success, name & " not found")
        Return m.Value
    End Function

End Class
