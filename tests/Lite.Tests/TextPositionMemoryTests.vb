#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Text
Imports Xunit

' Specification 277 section 12.1, TextRememberPosition: the store. Bringing the place back on screen is WinForms glue and
' is accepted by hand.
Public Class TextPositionMemoryTests
    Implements IDisposable

    Private ReadOnly folder As String = Path.Combine(Path.GetTempPath(), "fms-text-positions-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly store As String

    Public Sub New()
        Directory.CreateDirectory(folder)
        store = Path.Combine(folder, "positions.txt")
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Directory.Exists(folder) Then Directory.Delete(folder, True)
    End Sub

    Private Shared Function Doc(path As String, Optional provenance As String = "") As DocumentIdentity
        Return New DocumentIdentity With {.Path = path, .Provenance = provenance}
    End Function

    <Fact>
    Public Sub A_saved_place_comes_back_after_a_reload()
        Dim memory As New TextPositionMemory(store)
        memory.Remember(Doc("C:\a.txt"), New TextPlace(3, 4096, 120, 7))
        memory.Save()

        Dim reloaded As New TextPositionMemory(store)
        reloaded.Load()
        Dim place As TextPlace = reloaded.Lookup(Doc("c:\A.TXT"))
        Assert.NotNull(place)
        Assert.Equal(3, place.Window)
        Assert.Equal(4096, place.WindowSize)
        Assert.Equal(120, place.Line)
        Assert.Equal(7, place.Column)
        Assert.Equal("", reloaded.Diagnostic)
    End Sub

    <Fact>
    Public Sub A_place_survives_a_change_of_the_file_but_not_of_its_provenance()
        Dim memory As New TextPositionMemory(store)
        memory.Remember(New DocumentIdentity With {.Path = "C:\log.txt", .Length = 10, .LastWriteUtcTicks = 1}, New TextPlace(0, 0, 5, 0))
        Assert.NotNull(memory.Lookup(New DocumentIdentity With {.Path = "C:\log.txt", .Length = 99, .LastWriteUtcTicks = 2}))

        memory.Remember(Doc("C:\b.zip|x.txt", "archive:1"), New TextPlace(0, 0, 5, 0))
        Assert.Null(memory.Lookup(Doc("C:\b.zip|x.txt", "archive:2")))
    End Sub

    <Fact>
    Public Sub Leaving_a_document_at_its_start_removes_its_record()
        Dim memory As New TextPositionMemory(store)
        memory.Remember(Doc("C:\a.txt"), New TextPlace(0, 0, 9, 0))
        memory.Remember(Doc("C:\a.txt"), New TextPlace(0, 0, 0, 0))
        Assert.Null(memory.Lookup(Doc("C:\a.txt")))
        Assert.Equal(0, memory.Count)
    End Sub

    <Fact>
    Public Sub The_list_keeps_the_most_recent_documents()
        Dim memory As New TextPositionMemory(store)
        For index As Integer = 0 To TextPositionMemory.EntryLimit
            memory.Remember(Doc("C:\" & index.ToString() & ".txt"), New TextPlace(0, 0, 1, 0))
        Next
        Assert.Equal(TextPositionMemory.EntryLimit, memory.Count)
        Assert.Null(memory.Lookup(Doc("C:\0.txt")))
        Assert.NotNull(memory.Lookup(Doc("C:\1.txt")))

        ' A document read again is the newest one and outlives the older ones.
        memory.Remember(Doc("C:\1.txt"), New TextPlace(0, 0, 2, 0))
        memory.Remember(Doc("C:\new.txt"), New TextPlace(0, 0, 1, 0))
        Assert.NotNull(memory.Lookup(Doc("C:\1.txt")))
        Assert.Null(memory.Lookup(Doc("C:\2.txt")))
    End Sub

    <Fact>
    Public Sub A_damaged_file_of_many_lines_loads_only_its_newest_documents()
        Dim lines As New List(Of String) From {"fms-text-positions 1"}
        For index As Integer = 1 To 20000
            lines.Add("0" & vbTab & "0" & vbTab & "1" & vbTab & "0" & vbTab & vbTab & index.ToString())
        Next
        File.WriteAllLines(store, lines)
        Dim memory As New TextPositionMemory(store)
        memory.Load()
        Assert.Equal(TextPositionMemory.EntryLimit, memory.Count)
        Assert.NotNull(memory.Lookup(Doc("20000")))
        Assert.Null(memory.Lookup(Doc("1")))
    End Sub

    <Fact>
    Public Sub Save_writes_only_what_changed()
        Dim memory As New TextPositionMemory(store)
        memory.Save()
        Assert.False(File.Exists(store))

        memory.Remember(Doc("C:\a.txt"), New TextPlace(0, 0, 1, 0))
        memory.Save()
        Assert.True(File.Exists(store))

        File.Delete(store)
        memory.Save()
        Assert.False(File.Exists(store))
    End Sub

    <Fact>
    Public Sub A_file_of_another_format_is_kept_and_nothing_overwrites_it()
        Const foreign As String = "another program" & vbLf
        File.WriteAllText(store, foreign, New UTF8Encoding(False))
        Dim memory As New TextPositionMemory(store)
        memory.Load()
        Assert.StartsWith("unknown-format", memory.Diagnostic)

        memory.Remember(Doc("C:\a.txt"), New TextPlace(0, 0, 1, 0))
        memory.Save()
        Assert.Equal(foreign, File.ReadAllText(store))
        Assert.Null(memory.Lookup(Doc("C:\a.txt")))
    End Sub

    <Fact>
    Public Sub An_oversized_file_is_kept_unchanged()
        File.WriteAllText(store, New String("x"c, TextPositionMemory.MaxFileBytes + 1), New UTF8Encoding(False))
        Dim memory As New TextPositionMemory(store)
        memory.Load()
        Assert.StartsWith("too-large", memory.Diagnostic)
        memory.Save()
        Assert.Equal(CLng(TextPositionMemory.MaxFileBytes + 1), New FileInfo(store).Length)
    End Sub

    <Fact>
    Public Sub Clear_empties_the_memory_and_takes_back_a_file_it_refused()
        File.WriteAllText(store, "another program" & vbLf, New UTF8Encoding(False))
        Dim memory As New TextPositionMemory(store)
        memory.Load()
        memory.Clear()
        memory.Save()

        Dim reloaded As New TextPositionMemory(store)
        reloaded.Load()
        Assert.Equal("", reloaded.Diagnostic)
        Assert.Equal(0, reloaded.Count)
    End Sub

    <Theory>
    <InlineData("1" & vbTab & "2" & vbTab & "3" & vbTab & "4" & vbTab & vbTab)>
    <InlineData("x" & vbTab & "2" & vbTab & "3" & vbTab & "4" & vbTab & vbTab & "C:\a.txt")>
    <InlineData("-1" & vbTab & "2" & vbTab & "3" & vbTab & "4" & vbTab & vbTab & "C:\a.txt")>
    <InlineData("1" & vbTab & "2" & vbTab & "3" & vbTab & vbTab & "C:\a.txt")>
    Public Sub A_damaged_line_is_skipped_and_the_rest_stays(damaged As String)
        File.WriteAllText(store, "fms-text-positions 1" & vbLf & damaged & vbLf &
            "0" & vbTab & "0" & vbTab & "8" & vbTab & "0" & vbTab & vbTab & "C:\good.txt" & vbLf, New UTF8Encoding(False))
        Dim memory As New TextPositionMemory(store)
        memory.Load()
        Assert.Equal(1, memory.Count)
        Assert.Equal(8, memory.Lookup(Doc("C:\good.txt")).Line)
    End Sub

    <Fact>
    Public Sub A_path_that_would_break_the_line_format_is_refused()
        Dim memory As New TextPositionMemory(store)
        memory.Remember(Doc("C:\a" & vbTab & "b.txt"), New TextPlace(0, 0, 1, 0))
        memory.Remember(Doc("C:\a.txt", "x" & vbLf), New TextPlace(0, 0, 1, 0))
        memory.Remember(Nothing, New TextPlace(0, 0, 1, 0))
        memory.Remember(Doc("C:\a.txt"), Nothing)
        Assert.Equal(0, memory.Count)
        Assert.Null(memory.Lookup(Nothing))
    End Sub

    <Fact>
    Public Sub A_negative_place_counts_as_the_start()
        Assert.True(New TextPlace(-1, -1, -1, -1).IsStart)
    End Sub
End Class
#End If
