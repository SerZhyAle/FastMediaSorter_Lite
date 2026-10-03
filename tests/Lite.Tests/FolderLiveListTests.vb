#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' 020_SPECIFICATION_VIEWER_CORE_S9_MODERN_FOLDER.md - U-04 (the live folder list) and U-05 (a big
''' folder is sorted).
'''
''' The rule under every case: after a batch the list is the list a rescan would have produced
''' (same order, same tie-break), except that the file on screen has not moved. So the main test
''' does not assert positions by hand - it applies a random batch to a sorted list and compares the
''' result with sorting the whole folder from scratch, for every sort the box offers.
''' </summary>
Public Class FolderLiveListTests

    Private Const Folder As String = "C:\fms-live-test\"

    Private Shared ReadOnly AllKinds As LiveSortKind() = CType([Enum].GetValues(GetType(LiveSortKind)), LiveSortKind())

    Private Shared Function Fact(name As String, length As Long, minutes As Integer) As LiveFileFact
        Return New LiveFileFact(Folder & name, length, New Date(2026, 1, 1).AddMinutes(minutes))
    End Function

    ''' <summary>The scan's own ordering - the oracle the incremental result is held against.</summary>
    Private Shared Function FullSort(facts As IEnumerable(Of LiveFileFact), kind As LiveSortKind) As List(Of String)
        ' "rnd" has no order to hold anything against: the rows stay as they came.
        If kind = LiveSortKind.Unordered Then Return facts.Select(Function(f) f.Path).ToList()
        Dim comparer As Func(Of LiveFileFact, LiveFileFact, Integer) =
            Function(a, b) FolderLiveList.Compare(kind, a.Path, a, b.Path, b)
        ' A stable sort by the same comparison: ties keep their incoming order, as OrderBy does.
        Dim indexed = facts.Select(Function(f, i) New With {f, i}).ToList()
        indexed.Sort(Function(x, y)
                         Dim c As Integer = comparer(x.f, y.f)
                         Return If(c <> 0, c, x.i.CompareTo(y.i))
                     End Function)
        Return indexed.Select(Function(x) x.f.Path).ToList()
    End Function

    Private Shared Function RandomFacts(rng As Random, count As Integer, prefix As String) As List(Of LiveFileFact)
        Dim facts As New List(Of LiveFileFact)()
        For i As Integer = 0 To count - 1
            ' Few distinct sizes and times on purpose: ties are where a stable merge goes wrong.
            facts.Add(Fact(prefix & rng.Next(0, 400).ToString() & "_" & i.ToString() & If(rng.Next(0, 2) = 0, ".JPG", ".png"),
                           rng.Next(1, 8) * 1000L, rng.Next(0, 6)))
        Next
        Return facts
    End Function

    Private Shared Function Lookup(facts As IEnumerable(Of LiveFileFact)) As Func(Of String, LiveFileFact)
        Dim table As Dictionary(Of String, LiveFileFact) = facts.ToDictionary(Function(f) f.Path, StringComparer.OrdinalIgnoreCase)
        Return Function(path As String)
                   Dim f As LiveFileFact = Nothing
                   Return If(table.TryGetValue(path, f), f, Nothing)
               End Function
    End Function

    <Fact>
    Public Sub U04_a_batch_leaves_the_list_a_rescan_would_have_built_for_every_sort()
        For Each kind As LiveSortKind In AllKinds
            For seed As Integer = 1 To 40
                Dim rng As New Random(seed)
                Dim original As List(Of LiveFileFact) = RandomFacts(rng, rng.Next(1, 60), "a")
                Dim newcomers As List(Of LiveFileFact) = RandomFacts(rng, rng.Next(1, 25), "z")
                Dim gone As List(Of LiveFileFact) = original.Where(Function(f) rng.Next(0, 4) = 0).ToList()

                Dim rows As List(Of String) = FullSort(original, kind)
                ' Unordered rows keep whatever order they were in; the oracle for them is "survivors in
                ' place, newcomers after them in arrival order".
                Dim table As New List(Of LiveFileFact)(original)
                table.AddRange(newcomers)
                Dim change As LiveListChange = FolderLiveList.Apply(rows, -1, newcomers, gone.Select(Function(f) f.Path), kind, Lookup(table))

                Dim survivors As List(Of LiveFileFact) = original.Where(Function(f) Not gone.Contains(f)).ToList()
                Dim expected As List(Of String)
                If kind = LiveSortKind.Unordered Then
                    expected = FullSort(original, kind).Where(Function(p) Not gone.Any(Function(g) g.Path = p)).ToList()
                    expected.AddRange(newcomers.Select(Function(f) f.Path))
                Else
                    expected = FullSort(survivors.Concat(newcomers), kind)
                End If

                Assert.True(expected.SequenceEqual(rows), $"{kind}, seed {seed}: the list drifted from a full sort")
                Assert.Equal(gone.Count, change.Removed)
                Assert.Equal(newcomers.Count, change.Added)
            Next
        Next
    End Sub

    <Fact>
    Public Sub U04_the_file_on_screen_does_not_move_whatever_arrives_or_leaves()
        For Each kind As LiveSortKind In AllKinds
            For seed As Integer = 1 To 40
                Dim rng As New Random(seed * 7)
                Dim original As List(Of LiveFileFact) = RandomFacts(rng, rng.Next(3, 50), "a")
                Dim rows As List(Of String) = FullSort(original, kind)
                Dim current As Integer = rng.Next(0, rows.Count)
                Dim shown As String = rows(current)
                Dim newcomers As List(Of LiveFileFact) = RandomFacts(rng, rng.Next(1, 20), "m")
                ' Everything but the shown file may be reported gone - including the shown file itself.
                Dim gone As List(Of String) = rows.Where(Function(p) rng.Next(0, 3) = 0).ToList()
                Dim table As New List(Of LiveFileFact)(original)
                table.AddRange(newcomers)

                Dim change As LiveListChange = FolderLiveList.Apply(rows, current, newcomers, gone, kind, Lookup(table))

                Assert.Equal(shown, rows(change.CurrentIndex))
            Next
        Next
    End Sub

    <Fact>
    Public Sub U04_a_file_reported_gone_while_it_is_on_screen_is_left_for_the_display_path()
        Dim rows As New List(Of String) From {Folder & "a.jpg", Folder & "b.jpg", Folder & "c.jpg"}
        Dim change As LiveListChange = FolderLiveList.Apply(rows, 1, Nothing, {Folder & "b.jpg", Folder & "a.jpg"}, LiveSortKind.NameAscending, Nothing)
        Assert.Equal(1, change.Removed)
        Assert.Equal(New List(Of String) From {Folder & "b.jpg", Folder & "c.jpg"}, rows)
        Assert.Equal(0, change.CurrentIndex)
    End Sub

    <Fact>
    Public Sub U04_what_is_already_listed_is_not_added_twice_and_what_is_not_listed_is_not_removed()
        Dim rows As New List(Of String) From {Folder & "a.jpg", Folder & "b.jpg"}
        Dim again As LiveFileFact = Fact("A.JPG", 10, 0)   ' the same file, other case
        Dim twice As LiveFileFact = Fact("n.jpg", 10, 0)
        Dim change As LiveListChange = FolderLiveList.Apply(rows, 0, {again, twice, twice}, {Folder & "nope.jpg"}, LiveSortKind.NameAscending, Nothing)
        Assert.Equal(1, change.Added)
        Assert.Equal(0, change.Removed)
        Assert.Equal(3, rows.Count)
        Assert.Equal(0, change.CurrentIndex)
    End Sub

    <Fact>
    Public Sub U04_a_newcomer_goes_in_by_the_sort_not_at_the_end()
        Dim rows As New List(Of String) From {Folder & "a.jpg", Folder & "c.jpg", Folder & "e.jpg"}
        Dim change As LiveListChange = FolderLiveList.Apply(rows, 2, {Fact("b.jpg", 1, 0), Fact("d.jpg", 1, 0), Fact("0.jpg", 1, 0)},
                                                              Nothing, LiveSortKind.NameAscending, Nothing)
        Assert.Equal(New List(Of String) From {Folder & "0.jpg", Folder & "a.jpg", Folder & "b.jpg", Folder & "c.jpg", Folder & "d.jpg", Folder & "e.jpg"}, rows)
        ' e.jpg was at 2; three newcomers went in at or before it.
        Assert.Equal(5, change.CurrentIndex)
    End Sub

    <Fact>
    Public Sub U04_natural_order_puts_img10_after_img9()
        Dim rows As New List(Of String) From {Folder & "img2.jpg", Folder & "img9.jpg", Folder & "img11.jpg"}
        FolderLiveList.Apply(rows, 0, {Fact("img10.jpg", 1, 0)}, Nothing, LiveSortKind.NaturalAscending, Nothing)
        Assert.Equal(New List(Of String) From {Folder & "img2.jpg", Folder & "img9.jpg", Folder & "img10.jpg", Folder & "img11.jpg"}, rows)
    End Sub

    <Fact>
    Public Sub U04_a_random_order_takes_newcomers_at_the_end()
        Dim rows As New List(Of String) From {Folder & "q.jpg", Folder & "a.jpg"}
        FolderLiveList.Apply(rows, 0, {Fact("b.jpg", 1, 0), Fact("z.jpg", 1, 0)}, Nothing, LiveSortKind.Unordered, Nothing)
        Assert.Equal(New List(Of String) From {Folder & "q.jpg", Folder & "a.jpg", Folder & "b.jpg", Folder & "z.jpg"}, rows)
    End Sub

    <Fact>
    Public Sub U04_a_thousand_arrivals_are_one_merge_and_still_in_order()
        Dim rng As New Random(11)
        Dim original As List(Of LiveFileFact) = RandomFacts(rng, 2000, "a")
        Dim newcomers As List(Of LiveFileFact) = RandomFacts(rng, 1000, "n")
        Dim rows As List(Of String) = FullSort(original, LiveSortKind.NameAscending)
        FolderLiveList.Apply(rows, 500, newcomers, Nothing, LiveSortKind.NameAscending, Nothing)
        Assert.True(FullSort(original.Concat(newcomers), LiveSortKind.NameAscending).SequenceEqual(rows))
    End Sub

    <Fact>
    Public Sub U04_the_sort_labels_map_to_the_orders_GetFiles_builds()
        Assert.Equal(LiveSortKind.NameAscending, FolderLiveList.SortKindOf("abc"))
        Assert.Equal(LiveSortKind.NameDescending, FolderLiveList.SortKindOf("xyz"))
        Assert.Equal(LiveSortKind.NaturalAscending, FolderLiveList.SortKindOf("<0123"))
        Assert.Equal(LiveSortKind.NaturalDescending, FolderLiveList.SortKindOf(">3210"))
        Assert.Equal(LiveSortKind.SizeDescending, FolderLiveList.SortKindOf(">size"))
        Assert.Equal(LiveSortKind.SizeAscending, FolderLiveList.SortKindOf("<size"))
        Assert.Equal(LiveSortKind.TimeDescending, FolderLiveList.SortKindOf(">time"))
        Assert.Equal(LiveSortKind.TimeAscending, FolderLiveList.SortKindOf("<time"))
        Assert.Equal(LiveSortKind.Unordered, FolderLiveList.SortKindOf("rnd"))
        Assert.Equal(LiveSortKind.Unordered, FolderLiveList.SortKindOf(Nothing))
        ' Only the size and time orders need a table of facts.
        For Each kind As LiveSortKind In AllKinds
            Assert.Equal(kind.ToString().StartsWith("Size") OrElse kind.ToString().StartsWith("Time"), FolderLiveList.NeedsMetadata(kind))
        Next
    End Sub

    ' ------------------------------------------------------------------------ Classify ----

    Private Shared Function MakeFolder() As String
        Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-live-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Return dir
    End Function

    <Fact>
    Public Sub U04_classify_reports_what_is_true_of_each_touched_path_now()
        Dim dir As String = MakeFolder()
        Try
            Dim there As String = Path.Combine(dir, "there.jpg")
            Dim empty As String = Path.Combine(dir, "empty.jpg")
            Dim text As String = Path.Combine(dir, "notes.txt")
            Dim editor_Temp As String = Path.Combine(dir, "photo.fms-tmp.jpg")
            Dim gone As String = Path.Combine(dir, "gone.jpg")
            Dim sub_Dir As String = Path.Combine(dir, "album.jpg")   ' a directory that looks like a file
            File.WriteAllBytes(there, New Byte() {1, 2, 3})
            File.WriteAllBytes(empty, New Byte() {})
            File.WriteAllText(text, "x")
            File.WriteAllBytes(editor_Temp, New Byte() {1})
            Directory.CreateDirectory(sub_Dir)

            Dim present As New List(Of LiveFileFact)()
            Dim absent As New List(Of String)()
            FolderLiveList.Classify({there, empty, text, editor_Temp, gone, sub_Dir},
                                    New HashSet(Of String) From {".jpg", ".png"}, present, absent)

            Assert.Equal({there}, present.Select(Function(f) f.Path).ToArray())
            Assert.Equal(3L, present(0).Length)
            ' Only a vanished media file is "absent": a directory, a no-bytes file, a temp file and a
            ' kind the viewer does not list say nothing.
            Assert.Equal({gone}, absent.ToArray())
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    ' ----------------------------------------------------------- U-05 and the wiring ----

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

    <Fact>
    Public Sub U05_a_big_folder_is_sorted_on_the_mainline_and_keeps_its_array_branch()
        Dim scanning As String = ReadSource("Main_Form.FileScanning.vb")
        Dim start As Integer = scanning.IndexOf("Private Function GetFiles", StringComparison.Ordinal)
        Dim body As String = scanning.Substring(start, scanning.IndexOf("End Function", start, StringComparison.Ordinal) - start)

        ' The sort comes BEFORE the choice between list and array, so both are ordered ...
        Dim sort_At As Integer = body.IndexOf("Select Case sort_Label", StringComparison.Ordinal)
        Dim array_At As Integer = body.IndexOf("orderedEntries.ToArray()", StringComparison.Ordinal)
        Assert.True(sort_At > 0 AndAlso array_At > sort_At, "the array must be built from the sorted entries")
        ' ... and the unsorted array of the old limit survives only in the x86 seam.
        Dim unsorted As String = "Return file_Entry_List.ToArray()"
        Dim unsorted_At As Integer = body.IndexOf(unsorted, StringComparison.Ordinal)
        Assert.True(unsorted_At > 0, "the x86 build keeps the file-system order past its limit")
        Assert.True(body.LastIndexOf("#If NETFRAMEWORK Then", unsorted_At, StringComparison.Ordinal) >
                    body.LastIndexOf("#End If", unsorted_At, StringComparison.Ordinal),
                    "the unsorted array must sit inside #If NETFRAMEWORK")
    End Sub

    <Fact>
    Public Sub U05_the_mainline_limit_is_a_guard_not_the_old_100k()
        Dim main As String = ReadSource("Main_Form.vb")
        Dim modern As String = main.Substring(main.IndexOf("#Else", main.IndexOf("max_Number_Of_Files_For_List", StringComparison.Ordinal) - 400, StringComparison.Ordinal))
        Assert.Contains("max_Number_Of_Files_For_List As Integer = 2000000", modern)
        Assert.Contains("max_Number_Of_Files_For_List As Integer = 100000", main)
    End Sub

    <Fact>
    Public Sub U04_the_list_edits_wait_for_whatever_else_is_editing_it()
        Dim watch As String = ReadSource("Main_Form.FolderWatch.vb")
        ' Re-armed, not applied, while a file operation is queued ...
        Assert.Contains("file_Op_Queue.PendingCount > 0", watch)
        ' ... and a verdict is dropped when the list was replaced while the disk was being asked.
        Assert.Contains("list_Stamp <> file_List_Generation", watch)
        ' The share is asked from the pool, never the UI thread.
        Assert.Contains("Task.Run(Function() BuildFolderWatcher(full))", watch)
        Assert.Contains("FolderLiveList.Classify(touched, supported, present, absent)", watch)
    End Sub

    <Fact>
    Public Sub U04_the_watcher_follows_the_list_that_is_accepted_and_stops_with_it()
        Dim loading As String = ReadSource("Main_Form.MediaLoading.vb")
        Dim mark As String = loading.Substring(loading.IndexOf("Private Sub MarkFolderListLoaded", StringComparison.Ordinal))
        mark = mark.Substring(0, mark.IndexOf("End Sub", StringComparison.Ordinal))
        Assert.Contains("SyncFolderWatcher()", mark)

        Dim one_File As String = loading.Substring(loading.IndexOf("Private Sub InstallSingleFileList", StringComparison.Ordinal))
        one_File = one_File.Substring(0, one_File.IndexOf("End Sub", StringComparison.Ordinal))
        Assert.Contains("StopFolderWatcher()", one_File)

        Assert.Contains("StopFolderWatcher()", ReadSource("Main_Form.Lifecycle.vb"))
    End Sub

End Class
#End If
