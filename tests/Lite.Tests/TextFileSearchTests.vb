#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Threading
Imports Xunit

' Specification 277 section 5.1 item 4 (plan step P3.9): the full-file search of a paged document finds
' every hit in every window, names it by the window, line and column the reader shows, stops at the
' match ceiling and on a cancellation, and refuses a file whose size moved. Small windows
' (MinWindowBytes) keep the generated files a few hundred KiB.
Public Class TextFileSearchTests

    Private Shared Function WriteTemp(bytes As Byte()) As String
        Dim path As String = IO.Path.Combine(IO.Path.GetTempPath(), "fms-search-" & Guid.NewGuid().ToString("N") & ".txt")
        File.WriteAllBytes(path, bytes)
        Return path
    End Function

    Private Shared Function Repeat(line As String, count As Integer) As String
        Dim text As New StringBuilder()
        For index As Integer = 0 To count - 1
            text.Append(line)
        Next
        Return text.ToString()
    End Function

    Private Shared Function Request(path As String, encoding As Encoding, query As String) As TextFileSearchRequest
        Return New TextFileSearchRequest With {
            .SourcePath = path,
            .SourceEncoding = encoding,
            .BomLength = 0,
            .WindowBytes = TextFilePager.MinWindowBytes,
            .ExpectedFileSize = New FileInfo(path).Length,
            .TabWidth = 4,
            .Query = query
        }
    End Function

    <Fact>
    Public Sub Every_hit_of_every_window_is_found_where_the_reader_shows_it()
        Dim content As String = Repeat("Строка журнала" & vbTab & "ошибка в модуле, ОШИБКА снова" & vbCrLf & "unix line" & vbLf, 3000)
        Dim encoding As New UTF8Encoding(False)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Dim progress As New List(Of Integer)()
            Dim result As TextFileSearchResult = TextFileSearch.Scan(Request(path, encoding, "ошибка"), CancellationToken.None,
                Sub(done) progress.Add(done))
            Assert.Null(result.Problem)
            Assert.True(result.TotalWindows > 3)
            Assert.Equal(6000, result.Hits.Count)
            Assert.Equal(result.TotalWindows, progress.Count)
            Assert.Equal(result.TotalWindows, progress(progress.Count - 1))

            Dim pager As New TextFilePager(path, encoding, 0, TextFilePager.MinWindowBytes)
            Dim windowsWithHits As New HashSet(Of Integer)()
            Dim lines As String() = Nothing
            Dim linesWindow As Integer = -1
            For i As Integer = 0 To result.Hits.Count - 1
                Dim hit As TextFileHit = result.Hits(i)
                If i > 0 Then
                    Dim before As TextFileHit = result.Hits(i - 1)
                    Assert.True(before.Window < hit.Window OrElse
                                (before.Window = hit.Window AndAlso (before.Match.Line < hit.Match.Line OrElse
                                 (before.Match.Line = hit.Match.Line AndAlso before.Match.Column < hit.Match.Column))))
                End If
                If hit.Window <> linesWindow Then
                    lines = TextReaderLayout.BuildLines(pager.ReadWindow(hit.Window), 4, False, hit.Window < pager.TotalWindows - 1).CopyLines
                    linesWindow = hit.Window
                End If
                Assert.Equal("ошибка", lines(hit.Match.Line).Substring(hit.Match.Column, hit.Match.Length).ToLowerInvariant())
                windowsWithHits.Add(hit.Window)
            Next
            Assert.Equal(result.TotalWindows, windowsWithHits.Count)
            ' The tab before the first hit is expanded: "Строка журнала" is 14 characters, the tab pads to 16.
            Assert.Equal(16, result.Hits(0).Match.Column)
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Match_case_and_whole_word_are_honoured()
        Dim content As String = Repeat("Error errors error ERROR" & vbLf, 2000)
        Dim encoding As New UTF8Encoding(False)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Dim exact As TextFileSearchRequest = Request(path, encoding, "error")
            exact.MatchCase = True
            exact.WholeWord = True
            Dim result As TextFileSearchResult = TextFileSearch.Scan(exact, CancellationToken.None, Nothing)
            Assert.Null(result.Problem)
            Assert.Equal(2000, result.Hits.Count)
            Assert.All(result.Hits, Sub(hit) Assert.Equal(13, hit.Match.Column))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub The_match_ceiling_stops_the_scan_with_the_first_hits()
        Dim content As String = Repeat("a" & vbLf, TextReaderLayout.MaxMatches + 5000)
        Dim encoding As New UTF8Encoding(False)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Dim result As TextFileSearchResult = TextFileSearch.Scan(Request(path, encoding, "a"), CancellationToken.None, Nothing)
            Assert.Equal("limit", result.Problem)
            Assert.Equal(TextReaderLayout.MaxMatches, result.Hits.Count)
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub An_invalid_pattern_and_a_moved_size_are_reported_without_hits()
        Dim content As String = Repeat("text" & vbLf, 100)
        Dim encoding As New UTF8Encoding(False)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Dim invalid As TextFileSearchRequest = Request(path, encoding, "(unclosed")
            invalid.UseRegex = True
            Dim result As TextFileSearchResult = TextFileSearch.Scan(invalid, CancellationToken.None, Nothing)
            Assert.Equal("pattern", result.Problem)
            Assert.Empty(result.Hits)

            Dim moved As TextFileSearchRequest = Request(path, encoding, "text")
            moved.ExpectedFileSize += 1
            result = TextFileSearch.Scan(moved, CancellationToken.None, Nothing)
            Assert.Equal("changed", result.Problem)
            Assert.Empty(result.Hits)
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_cancelled_scan_ends_with_OperationCanceledException()
        Dim content As String = Repeat("text" & vbLf, 20000)
        Dim encoding As New UTF8Encoding(False)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Using cancel As New CancellationTokenSource()
                Dim scanned As Integer = 0
                Assert.Throws(Of OperationCanceledException)(
                    Sub()
                        TextFileSearch.Scan(Request(path, encoding, "text"), cancel.Token,
                            Sub(done)
                                scanned = done
                                If done = 1 Then cancel.Cancel()
                            End Sub)
                    End Sub)
                Assert.Equal(1, scanned)
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Window_matches_are_the_hits_of_one_window()
        Dim hits As New List(Of TextFileHit) From {
            New TextFileHit(0, New TextMatch(3, 0, 2)),
            New TextFileHit(2, New TextMatch(1, 4, 2)),
            New TextFileHit(2, New TextMatch(7, 0, 2)),
            New TextFileHit(5, New TextMatch(0, 1, 2))
        }
        Dim first As Integer
        Dim matches As List(Of TextMatch) = TextFileSearch.WindowMatches(hits, 2, first)
        Assert.Equal(1, first)
        Assert.Equal(2, matches.Count)
        Assert.Equal(7, matches(1).Line)
        matches = TextFileSearch.WindowMatches(hits, 3, first)
        Assert.Equal(-1, first)
        Assert.Empty(matches)
        matches = TextFileSearch.WindowMatches(hits, 5, first)
        Assert.Equal(3, first)
        Assert.Single(matches)
        matches = TextFileSearch.WindowMatches(New List(Of TextFileHit)(), 0, first)
        Assert.Equal(-1, first)
    End Sub

    <Fact>
    Public Sub Stepping_follows_the_hit_or_the_reading_position()
        Dim hits As New List(Of TextFileHit) From {
            New TextFileHit(0, New TextMatch(3, 0, 2)),
            New TextFileHit(2, New TextMatch(1, 4, 2)),
            New TextFileHit(2, New TextMatch(7, 0, 2)),
            New TextFileHit(5, New TextMatch(0, 1, 2))
        }
        ' From a hit in the window on screen: one step, wrapping round.
        Assert.Equal(2, TextFileSearch.StepFrom(hits, 1, 2, 0, True))
        Assert.Equal(0, TextFileSearch.StepFrom(hits, 3, 5, 0, True))
        Assert.Equal(3, TextFileSearch.StepFrom(hits, 0, 0, 0, False))
        ' The reader moved to window 3 (no hits): forward goes on to window 5, backward back to window 2.
        Assert.Equal(3, TextFileSearch.StepFrom(hits, 1, 3, 0, True))
        Assert.Equal(2, TextFileSearch.StepFrom(hits, 1, 3, 0, False))
        ' No current hit, reading window 2 from line 5: forward the hit at line 7, backward the one at line 1.
        Assert.Equal(2, TextFileSearch.StepFrom(hits, -1, 2, 5, True))
        Assert.Equal(1, TextFileSearch.StepFrom(hits, -1, 2, 5, False))
        ' A hit on the reading line itself is the next one forward, and not the previous one backward.
        Assert.Equal(2, TextFileSearch.StepFrom(hits, -1, 2, 7, True))
        Assert.Equal(1, TextFileSearch.StepFrom(hits, -1, 2, 7, False))
        ' Past the last hit forward wraps to the first; before the first backward wraps to the last.
        Assert.Equal(0, TextFileSearch.StepFrom(hits, -1, 6, 0, True))
        Assert.Equal(3, TextFileSearch.StepFrom(hits, -1, 0, 0, False))
        Assert.Equal(-1, TextFileSearch.StepFrom(New List(Of TextFileHit)(), -1, 0, 0, True))
    End Sub

End Class
#End If
