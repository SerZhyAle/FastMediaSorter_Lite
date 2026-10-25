Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Xunit

''' <summary>
''' Audit 048 (navigation, folder scanning and the decode pipeline). One test per finding
''' that has a pure core; the rest are the manual checks listed in the ticket's
''' implementation log. Runs under both target frameworks unless fenced.
''' </summary>
Public Class NavigationAndLoadingAuditTests

    ' ---------------------------------------------------------------- A-01 ----
    ' A prefetch result was accepted by file name alone and could land in the VISIBLE box.

    <Fact>
    Public Sub A01_a_prefetch_for_the_current_generation_and_the_hidden_box_is_taken()
        ' Box 1 on screen (is_Second_Box_Active = False): the request was for box 2.
        Assert.True(NavigationRules.IsPrefetchCurrent(7, 7, False, False, "C:\a\B.jpg", "c:\a\b.jpg"))
        ' Box 2 on screen: the request was for box 1.
        Assert.True(NavigationRules.IsPrefetchCurrent(7, 7, True, True, "C:\a\B.jpg", "C:\a\B.jpg"))
    End Sub

    <Fact>
    Public Sub A01_a_prefetch_from_an_older_generation_is_dropped_even_when_the_name_matches()
        ' The audit's sequence: Req3{next B -> P2} lands after the user went B, A again.
        Assert.False(NavigationRules.IsPrefetchCurrent(5, 7, False, False, "C:\a\B.jpg", "C:\a\B.jpg"))
    End Sub

    <Fact>
    Public Sub A01_a_prefetch_aimed_at_the_box_now_on_screen_is_dropped()
        ' Target box 2 while box 2 is the active (visible) one.
        Assert.False(NavigationRules.IsPrefetchCurrent(7, 7, False, True, "C:\a\B.jpg", "C:\a\B.jpg"))
        Assert.False(NavigationRules.IsPrefetchCurrent(7, 7, True, False, "C:\a\B.jpg", "C:\a\B.jpg"))
    End Sub

    <Fact>
    Public Sub A01_a_prefetch_for_another_file_or_no_file_is_dropped()
        Assert.False(NavigationRules.IsPrefetchCurrent(7, 7, False, False, "C:\a\C.jpg", "C:\a\B.jpg"))
        Assert.False(NavigationRules.IsPrefetchCurrent(7, 7, False, False, "", ""))
    End Sub

    ' ---------------------------------------------------------------- A-15 ----

    <Fact>
    Public Sub A15_only_a_report_for_the_generation_on_screen_is_written()
        Assert.True(NavigationRules.IsReportCurrent(3, 3))
        Assert.False(NavigationRules.IsReportCurrent(2, 3))
    End Sub

    <Fact>
    Public Sub A15_the_label_is_written_by_the_display_path_and_cleared_when_the_folder_empties()
        Dim source As String = ReadSource("Main_Form.MediaLoading.vb")
        Dim display As String = MethodBody(source, "UpdateCurrentFileAndDisplay")
        Assert.Contains("lbl_Current_File.Text = CurrentFileDisplayName(Current_File_Name)", display)
        Assert.Contains("lbl_Current_File.Text = """"", display)
        Assert.Contains(".Generation = media_Generation", display)

        Dim scanning As String = ReadSource("Main_Form.FileScanning.vb")
        Assert.Contains("NavigationRules.IsReportCurrent(report_Generation, media_Generation)",
                        MethodBody(scanning, "BgWorker_ProgressChanged"))
        Assert.Contains("NavigationRules.IsPrefetchCurrent(", MethodBody(scanning, "BgWorker_RunWorkerCompleted"))
    End Sub

    ' ---------------------------------------------------------------- A-04 ----

    <Fact>
    Public Sub A04_the_shuffle_cycle_visits_every_index_exactly_once_per_cycle()
        Dim rng As New Random(12345)
        Dim cycle As New ShuffleCycle()
        Dim current As Integer = 2
        For round As Integer = 1 To 5
            ' A cycle over five files, started while file 2 (then the last one shown) is on
            ' screen, holds the other four; from then on each cycle holds all but the file
            ' the previous cycle ended on.
            Dim seen As New HashSet(Of Integer)()
            For i As Integer = 1 To 4
                Dim nextIndex As Integer = SlideshowOrder.NextIndex(True, True, 5, current, rng, cycle)
                Assert.NotEqual(current, nextIndex)
                Assert.True(seen.Add(nextIndex), "index " & nextIndex.ToString() & " repeated within a cycle")
                current = nextIndex
            Next
            Assert.Equal(4, seen.Count)
        Next
    End Sub

    <Fact>
    Public Sub A04_the_running_slideshow_asks_the_same_order_as_its_start()
        Dim source As String = ReadSource("Main_Form.MediaLoading.vb")
        Dim update As String = MethodBody(source, "UpdateFileIndexAndList")
        Dim slideshowCase As String = update.Substring(update.IndexOf("Case Mode_InSlideShow", StringComparison.Ordinal))
        slideshowCase = slideshowCase.Substring(0, slideshowCase.IndexOf("Case Mode_FolderAndFile", StringComparison.Ordinal))
        Assert.Contains("SlideshowOrder.NextIndex(True, IsShuffleCycleOrder()", slideshowCase)
    End Sub

    <Fact>
    Public Sub A04_sequential_order_wraps_and_a_new_list_size_starts_a_new_cycle()
        Dim rng As New Random(1)
        Assert.Equal(0, SlideshowOrder.NextIndex(False, False, 3, 2, rng, Nothing))
        Assert.Equal(2, SlideshowOrder.NextIndex(False, False, 3, 1, rng, Nothing))

        Dim cycle As New ShuffleCycle()
        cycle.NextIndex(10, 0, rng)
        ' Three files left the folder: the rest of the old cycle must not name index 9.
        For i As Integer = 1 To 6
            Assert.InRange(cycle.NextIndex(7, 0, rng), 0, 6)
        Next
    End Sub

    ' ---------------------------------------------------------------- A-08 ----

    <Fact>
    Public Sub A08_a_navigation_stamp_from_the_future_does_not_block_navigation()
        ' The DST fall-back case: the last stamp is an hour ahead of now.
        Assert.True(MonotonicClock.HasElapsed(1000, 1000 + 3600000, 40))
        Assert.False(MonotonicClock.HasElapsed(1030, 1000, 40))
        Assert.True(MonotonicClock.HasElapsed(1040, 1000, 40))
        ' The "nothing yet" stamp cannot overflow the subtraction.
        Assert.True(MonotonicClock.HasElapsed(0, Long.MinValue \ 2, 40))
    End Sub

    <Fact>
    Public Sub A08_the_navigation_throttle_no_longer_reads_wall_time()
        Dim body As String = MethodBody(ReadSource("Main_Form.MediaLoading.vb"), "ReadShowMediaFile")
        Assert.DoesNotContain("As DateTime = DateTime.Now", body)
        Assert.DoesNotContain("last_Action_Time", body)
        Assert.Contains("MonotonicClock.HasElapsed(", body)
    End Sub

    ' ---------------------------------------------------------------- A-09 ----

    ''' <summary>Replays the chain the display path runs: every attempt at an unreadable
    ''' file removes it and continues; the bound is taken when the chain starts.</summary>
    Private Shared Function WalkChain(readable As List(Of Boolean), useChainStartBound As Boolean) As Integer
        ' The user flips onto index 0, which fails: the file is dropped, the chain starts.
        readable.RemoveAt(0)
        Dim index As Integer = 0
        Dim chain As Integer = 0
        Dim bound As Integer = 0
        Do
            If chain = 0 Then bound = AutoSkipPolicy.BoundAtChainStart(readable.Count)
            chain += 1
            Dim limit As Integer = If(useChainStartBound, bound, readable.Count)
            If Not AutoSkipPolicy.ShouldContinue(chain, limit) Then Return -1
            If readable(index) Then Return chain
            readable.RemoveAt(index)
            If readable.Count = 0 Then Return -1
            If index > readable.Count - 1 Then index = 0
        Loop
    End Function

    <Fact>
    Public Sub A09_sixty_unreadable_files_of_a_hundred_still_reach_the_first_readable_one()
        Dim files As New List(Of Boolean)()
        For i As Integer = 0 To 99
            files.Add(i >= 60)
        Next
        Assert.Equal(60, WalkChain(New List(Of Boolean)(files), useChainStartBound:=True))
        ' The old bound - the live, shrinking count - gave up half-way.
        Assert.Equal(-1, WalkChain(New List(Of Boolean)(files), useChainStartBound:=False))
    End Sub

    <Fact>
    Public Sub A09_a_folder_where_nothing_reads_still_ends()
        Dim files As New List(Of Boolean)()
        For i As Integer = 0 To 49
            files.Add(False)
        Next
        Assert.Equal(-1, WalkChain(files, useChainStartBound:=True))
        Assert.Equal(1, AutoSkipPolicy.BoundAtChainStart(0))
        Assert.Equal(100, AutoSkipPolicy.BoundAtChainStart(99))
    End Sub

    ' ---------------------------------------------------------------- A-10 ----

    <Fact>
    Public Sub A10_extension_keys_ignore_a_turkish_culture()
        Dim saved As CultureInfo = CultureInfo.CurrentCulture
        Try
            CultureInfo.CurrentCulture = New CultureInfo("tr-TR")
            ' The trap itself, so the test proves it is testing something.
            Assert.NotEqual(".heic", ".HEIC".ToLower())
            Assert.Equal(".heic", NavigationRules.ExtensionKey("C:\DCIM\IMG_1234.HEIC"))
            Assert.Equal(".gif", NavigationRules.ExtensionKey("A.GIF"))
            Assert.Equal(".avi", NavigationRules.ExtensionKey("MVI_0001.AVI"))
            Assert.Equal(".tiff", NavigationRules.ExtensionKey("scan.TIFF"))
            Assert.Equal("", NavigationRules.ExtensionKey("README"))
            Assert.Equal("", NavigationRules.ExtensionKey(Nothing))
        Finally
            CultureInfo.CurrentCulture = saved
        End Try
    End Sub

    <Fact>
    Public Sub A10_no_viewer_source_lowercases_with_the_current_culture()
        Dim srcDir As String = FindSourceDir()
        Dim culprits As New List(Of String)()
        Dim pattern As New Regex("\.ToLower\b(?!Invariant)")
        For Each file_Path As String In Directory.EnumerateFiles(srcDir, "*.vb", SearchOption.AllDirectories)
            If file_Path.IndexOf("FastMediaSorterCompanion", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If file_Path.IndexOf(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            If file_Path.IndexOf(Path.DirectorySeparatorChar & "bin" & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
            Dim lineNumber As Integer = 0
            For Each line As String In File.ReadAllLines(file_Path)
                lineNumber += 1
                If line.TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If pattern.IsMatch(line) Then culprits.Add(Path.GetFileName(file_Path) & ":" & lineNumber.ToString())
            Next
        Next
        Assert.True(culprits.Count = 0, "culture-sensitive ToLower at " & String.Join(", ", culprits))
    End Sub

    ' ---------------------------------------------------------------- A-07 ----

    <Fact>
    Public Sub A07_a_missing_root_throws_instead_of_reading_as_empty()
        Using dir As New TempDir()
            Dim missing As String = Path.Combine(dir.Directory_Path, "no-such-folder")
            Assert.Throws(Of DirectoryNotFoundException)(Function() FolderScan.EnumerateFiles(missing, False, CancellationToken.None))
            Assert.Throws(Of DirectoryNotFoundException)(Function() FolderScan.EnumerateFiles(missing, True, CancellationToken.None))
            ' Classified as the folder being gone - the "Каталог не найден" wording.
            Try
                FolderScan.EnumerateFiles(missing, False, CancellationToken.None)
            Catch ex As Exception
                Assert.Equal(PathFailureKind.Missing, PathFailure.Classify(ex))
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub A07_an_empty_folder_is_an_empty_list_and_a_file_is_not_a_folder()
        Using dir As New TempDir()
            Assert.Empty(FolderScan.EnumerateFiles(dir.Directory_Path, False, CancellationToken.None))
            Assert.Empty(FolderScan.EnumerateFiles("", False, CancellationToken.None))

            Dim a_File As String = Path.Combine(dir.Directory_Path, "a.jpg")
            File.WriteAllBytes(a_File, New Byte(9) {})
            Assert.Single(FolderScan.EnumerateFiles(dir.Directory_Path, False, CancellationToken.None))
            ' A path that names a FILE is a read error, not "empty".
            Assert.ThrowsAny(Of Exception)(Function() FolderScan.EnumerateFiles(a_File, False, CancellationToken.None))
        End Using
    End Sub

    ' ---------------------------------------------------------------- A-06 ----

    <Fact>
    Public Sub A06_a_locked_image_is_reported_as_locked_not_as_content()
        Using dir As New TempDir()
            Dim picture As String = Path.Combine(dir.Directory_Path, "copying.png")
            Using bmp As New Bitmap(4, 3)
                bmp.Save(picture, Imaging.ImageFormat.Png)
            End Using

            Using held As New FileStream(picture, FileMode.Open, FileAccess.Read, FileShare.None)
                Dim failure As PathFailureKind = PathFailureKind.None
                Dim loaded As Tuple(Of Image, MemoryStream) = FileManager.LoadImageWithStream(picture, failure)
                Assert.Null(loaded)
                Assert.Equal(PathFailureKind.Locked, failure)
                Assert.False(PathFailure.IsAboutTheFile(failure))
            End Using

            ' Released: it loads, and says so.
            Dim ok As PathFailureKind = PathFailureKind.Content
            Dim image_Data As Tuple(Of Image, MemoryStream) = FileManager.LoadImageWithStream(picture, ok)
            Assert.NotNull(image_Data)
            Assert.Equal(PathFailureKind.None, ok)
            image_Data.Item1.Dispose()
            image_Data.Item2?.Dispose()
        End Using
    End Sub

    <Fact>
    Public Sub A06_bytes_that_are_not_a_picture_are_content_and_a_missing_file_is_missing()
        Using dir As New TempDir()
            Dim junk As String = Path.Combine(dir.Directory_Path, "junk.jpg")
            File.WriteAllBytes(junk, New Byte() {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16})
            Dim failure As PathFailureKind = PathFailureKind.None
            Assert.Null(FileManager.LoadImageWithStream(junk, failure))
            Assert.Equal(PathFailureKind.Content, failure)

            Dim missing As PathFailureKind = PathFailureKind.None
            Assert.Null(FileManager.LoadImageWithStream(Path.Combine(dir.Directory_Path, "gone.jpg"), missing))
            Assert.Equal(PathFailureKind.Missing, missing)
        End Using
    End Sub

    ' ---------------------------------------------------------------- A-16 ----

    <Fact>
    Public Sub A16_the_header_decides_what_is_decoder_backed()
        Dim webp As Byte() = WebpFixture("StaticWebp")
        Assert.True(ImageDecoderProvider.LooksDecoderBacked(webp))
        Assert.False(ImageDecoderProvider.LooksDecoderBacked(New Byte() {&HFF, &HD8, &HFF, &HE0, 0, 0, 0, 0, 0, 0, 0, 0}))
        Assert.False(ImageDecoderProvider.LooksDecoderBacked(Nothing))
        Dim ftyp As Byte() = {0, 0, 0, &H18, &H66, &H74, &H79, &H70, &H68, &H65, &H69, &H63}
#If NETFRAMEWORK Then
        ' The x86 build decodes no HEIC at all, so a HEIC under any name stays unsupported.
        Assert.False(ImageDecoderProvider.LooksDecoderBacked(ftyp))
#Else
        Assert.True(ImageDecoderProvider.LooksDecoderBacked(ftyp))
#End If
    End Sub

#If Not NETFRAMEWORK Then
    <Fact>
    Public Sub A16_a_webp_saved_under_a_jpg_name_loads_on_the_mainline()
        Using dir As New TempDir()
            Dim disguised As String = Path.Combine(dir.Directory_Path, "from-the-browser.jpg")
            File.WriteAllBytes(disguised, WebpFixture("StaticWebp"))
            Dim failure As PathFailureKind = PathFailureKind.None
            Dim loaded As Tuple(Of Image, MemoryStream) = FileManager.LoadImageWithStream(disguised, failure)
            Assert.NotNull(loaded)
            Assert.Equal(New Size(120, 90), loaded.Item1.Size)
            Assert.Equal(PathFailureKind.None, failure)
            loaded.Item1.Dispose()
            loaded.Item2?.Dispose()
        End Using
    End Sub

    ' ---------------------------------------------------------------- A-17 ----

    <Fact>
    Public Sub A17_a_huge_canvas_animation_is_refused_from_its_header_quickly()
        ' The 120x90 three-frame fixture with its VP8X canvas claimed as 16384x16384:
        ' ~805 megapixels over three frames, a few hundred bytes on disk.
        Dim crafted As Byte() = CType(WebpFixture("AnimatedWebp").Clone(), Byte())
        Assert.Equal(CByte(&H56), crafted(12))   ' "VP8X"
        For i As Integer = 24 To 29
            crafted(i) = If((i - 24) Mod 3 = 2, CByte(0), CByte(&HFF))
        Next
        crafted(25) = &H3F : crafted(28) = &H3F   ' width-1 = height-1 = 0x3FFF

        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim payload As DecodedPayload
        Using stream As New MemoryStream(crafted)
            payload = New ModernMagickDecoder().DecodeToPayload(stream)
        End Using
        watch.Stop()

        Assert.Null(payload)
        Assert.True(watch.ElapsedMilliseconds < 5000, "refusal took " & watch.ElapsedMilliseconds.ToString() & " ms")
    End Sub

    <Fact>
    Public Sub A17_the_real_fixture_is_still_within_budget()
        Using stream As New MemoryStream(WebpFixture("AnimatedWebp"))
            Dim payload As DecodedPayload = New ModernMagickDecoder().DecodeToPayload(stream)
            Assert.NotNull(payload)
            Assert.True(payload.IsAnimation)
        End Using
    End Sub
#End If

    <Fact>
    Public Sub A17_the_decode_budget()
        Assert.True(DecodeBudget.IsWithinBudget(4032, 3024, 1))
        Assert.True(DecodeBudget.IsWithinBudget(20000, 10000, 1))        ' 200 MP
        Assert.False(DecodeBudget.IsWithinBudget(30000, 30000, 1))       ' 900 MP frame
        Assert.False(DecodeBudget.IsWithinBudget(10000, 10000, 100))     ' the audit's 40 GB case
        Assert.True(DecodeBudget.IsWithinBudget(500, 500, 100))
        Assert.False(DecodeBudget.IsWithinBudget(0, 100, 1))
        Assert.False(DecodeBudget.IsWithinBudget(100, 100, Long.MaxValue))
        Assert.True(DecodeBudget.IsWithinBudget(100, 100, 0))            ' no count = one frame
    End Sub

    ' ---------------------------------------------------------------- A-21 ----

    ''' <summary>A header-only JPEG: SOI, APP1 Exif with Orientation, SOF0 400x300, EOI.</summary>
    Private Shared Function JpegWithOrientation(orientation As Byte) As Byte()
        Dim bytes As New List(Of Byte) From {&HFF, &HD8}
        ' APP1, length 34 = 2 + "Exif\0\0"(6) + TIFF header(8) + count(2) + entry(12) + next(4).
        bytes.AddRange(New Byte() {&HFF, &HE1, 0, 34})
        bytes.AddRange(New Byte() {&H45, &H78, &H69, &H66, 0, 0})
        bytes.AddRange(New Byte() {&H49, &H49, &H2A, 0, 8, 0, 0, 0})
        bytes.AddRange(New Byte() {1, 0})
        bytes.AddRange(New Byte() {&H12, &H1, 3, 0, 1, 0, 0, 0, orientation, 0, 0, 0})
        bytes.AddRange(New Byte() {0, 0, 0, 0})
        ' SOF0: length 17, precision 8, height 300, width 400, 3 components.
        bytes.AddRange(New Byte() {&HFF, &HC0, 0, 17, 8, &H1, &H2C, &H1, &H90, 3,
                                   1, &H22, 0, 2, &H11, 1, 3, &H11, 1})
        bytes.AddRange(New Byte() {&HFF, &HD9})
        Return bytes.ToArray()
    End Function

    <Fact>
    Public Sub A21_a_portrait_phone_photo_reports_its_displayed_size()
        Using dir As New TempDir()
            Dim portrait As String = Path.Combine(dir.Directory_Path, "portrait.jpg")
            File.WriteAllBytes(portrait, JpegWithOrientation(6))
            Assert.Equal(New Size(300, 400), Utils.GetImageDimensions(portrait, True))
            Assert.Equal(New Size(400, 300), Utils.GetImageDimensions(portrait, False))

            Dim upright As String = Path.Combine(dir.Directory_Path, "upright.jpg")
            File.WriteAllBytes(upright, JpegWithOrientation(1))
            Assert.Equal(New Size(400, 300), Utils.GetImageDimensions(upright, True))

            Dim mirrored As String = Path.Combine(dir.Directory_Path, "mirrored.jpg")
            File.WriteAllBytes(mirrored, JpegWithOrientation(3))
            Assert.Equal(New Size(400, 300), Utils.GetImageDimensions(mirrored, True))
        End Using
    End Sub

    <Fact>
    Public Sub A21_a_malformed_exif_segment_reads_as_stored()
        Assert.Equal(1, Utils.ReadExifOrientation(Nothing))
        Assert.Equal(1, Utils.ReadExifOrientation(New Byte() {&H45, &H78, &H69, &H66, 0, 0, &H49, &H49, &H2A, 0, &HFF, &HFF, &HFF, &H7F}))
        Assert.True(Utils.IsQuarterTurnOrientation(5))
        Assert.True(Utils.IsQuarterTurnOrientation(8))
        Assert.False(Utils.IsQuarterTurnOrientation(4))
    End Sub

    ' ---------------------------------------------------------- A-18 / A-22 ----

    <Fact>
    Public Sub A18_A22_a_unc_path_is_network_and_a_drive_path_is_asked_separately()
        Assert.True(NavigationRules.IsUncPath("\\nas\photos\a.jpg"))
        Assert.True(NavigationRules.IsUncPath("\\wsl$\Ubuntu\home\a.jpg"))
        Assert.True(NavigationRules.IsUncPath("\\?\UNC\nas\photos\a.jpg"))
        Assert.False(NavigationRules.IsUncPath("\\?\C:\photos\a.jpg"))
        Assert.False(NavigationRules.IsUncPath("D:\Photos\Trip\a.jpg"))
        Assert.False(NavigationRules.IsUncPath(""))
    End Sub

    ' ---------------------------------------------------------- A-02/A-14/A-20 ----

    <Fact>
    Public Sub A02_A14_A20_the_list_state_is_reset_where_the_ticket_says()
        Dim lifecycle As String = ReadSource("Main_Form.Lifecycle.vb")
        Assert.Contains("InstallSingleFileList(Current_Image_Path)", lifecycle)
        Assert.DoesNotContain("files_List = New List(Of String) From {Current_Image_Path}", lifecycle)

        Dim loading As String = ReadSource("Main_Form.MediaLoading.vb")
        Dim install As String = MethodBody(loading, "InstallSingleFileList")
        Assert.Contains("is_Files_Array_Active = False", install)
        Assert.Contains("files_Array = Nothing", install)

        Dim update As String = MethodBody(loading, "UpdateFileIndexAndList")
        Assert.True(Regex.Matches(update, "ClearExternalInputState\(\)").Count >= 2)

        Dim display As String = MethodBody(loading, "UpdateCurrentFileAndDisplay")
        Assert.Contains("files_List.IndexOf(previous_File_Name)", display)
        Assert.DoesNotContain("New Uri(Current_File_Name).ToString()" & vbLf & vbLf & "#If Not NETFRAMEWORK", display)

        Assert.DoesNotContain("Sub TextBox1_KeyPress", ReadSource("Main_Form.vb"))
    End Sub

    <Fact>
    Public Sub A03_the_jump_dialog_passes_no_position()
        Dim jump As String = MethodBody(ReadSource("Main_Form.vb"), "Jump_To_file_Number")
        Assert.DoesNotContain(", 1, total_File_Count)", jump)
    End Sub

    ' ---------------------------------------------------------------- helpers ----

    Private Shared Function WebpFixture(name As String) As Byte()
        Dim field As FieldInfo = GetType(ImageDecoderSeamTests).GetField(name, BindingFlags.NonPublic Or BindingFlags.Static)
        Assert.NotNull(field)
        Return DirectCast(field.GetValue(Nothing), Byte())
    End Function

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

    Private NotInheritable Class TempDir
        Implements IDisposable

        Public ReadOnly Property Directory_Path As String

        Public Sub New()
            _Directory_Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fms-048-" & Guid.NewGuid().ToString("N"))
            System.IO.Directory.CreateDirectory(_Directory_Path)
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                System.IO.Directory.Delete(_Directory_Path, True)
            Catch
            End Try
        End Sub
    End Class

End Class
