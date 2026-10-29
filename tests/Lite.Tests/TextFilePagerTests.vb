#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Text
Imports Xunit

' Specification 277 section 5.1 (plan step P3.1, VM-17 shape): windows join back to the whole
' file, cuts land on line starts or character boundaries, seek finds the window of an offset.
' Small windows (MinWindowBytes) keep the generated files a few hundred KiB.
Public Class TextFilePagerTests

    Private Shared Function WriteTemp(bytes As Byte()) As String
        Dim path As String = IO.Path.Combine(IO.Path.GetTempPath(), "fms-pager-" & Guid.NewGuid().ToString("N") & ".txt")
        File.WriteAllBytes(path, bytes)
        Return path
    End Function

    Private Shared Function Joined(pager As TextFilePager) As String
        Dim text As New StringBuilder()
        For index As Integer = 0 To pager.TotalWindows - 1
            text.Append(pager.ReadWindow(index))
        Next
        Return text.ToString()
    End Function

    Private Shared Function Repeat(line As String, count As Integer) As String
        Dim text As New StringBuilder()
        For index As Integer = 0 To count - 1
            text.Append(line)
        Next
        Return text.ToString()
    End Function

    <Fact>
    Public Sub Utf8_windows_join_back_and_start_on_lines()
        Dim content As String = Repeat("Строка журнала с текстом и € знаком" & vbCrLf & "unix line" & vbLf, 4000)
        Dim path As String = WriteTemp(New UTF8Encoding(False).GetBytes(content))
        Try
            Dim pager As New TextFilePager(path, New UTF8Encoding(False), 0, TextFilePager.MinWindowBytes)
            Assert.True(pager.TotalWindows > 3)
            Assert.Equal(content, Joined(pager))
            For index As Integer = 1 To pager.TotalWindows - 1
                Dim startOffset As Long, endOffset As Long
                pager.GetWindowBounds(index, startOffset, endOffset)
                Assert.True(startOffset <= TextFilePager.MinWindowBytes * CLng(index))
                Assert.True(TextFilePager.MinWindowBytes * CLng(index) - startOffset <= TextFilePager.SnapMarginBytes)
                Dim previous As String = pager.ReadWindow(index - 1)
                Assert.True(previous.EndsWith(vbLf, StringComparison.Ordinal))
            Next
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Utf8_without_line_breaks_never_splits_a_character()
        Dim content As String = Repeat("€жx", 20000)
        Dim path As String = WriteTemp(New UTF8Encoding(False).GetBytes(content))
        Try
            Dim pager As New TextFilePager(path, New UTF8Encoding(False), 0, TextFilePager.MinWindowBytes)
            For index As Integer = 0 To pager.TotalWindows - 1
                Assert.Equal(-1, pager.ReadWindow(index).IndexOf(ChrW(&HFFFD)))
            Next
            Assert.Equal(content, Joined(pager))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Utf16_with_bom_keeps_surrogate_pairs_together()
        ' Five code units per repeat: the 16 KiB cut falls on the low surrogate.
        Dim content As String = Repeat("A" & Char.ConvertFromUtf32(&H1F600) & "ЖЖ", 10000)
        Dim encoding As New UnicodeEncoding(False, True)
        Dim bytes As Byte() = New Byte(encoding.GetPreamble().Length + encoding.GetByteCount(content) - 1) {}
        encoding.GetPreamble().CopyTo(bytes, 0)
        encoding.GetBytes(content).CopyTo(bytes, 2)
        Dim path As String = WriteTemp(bytes)
        Try
            Dim pager As New TextFilePager(path, encoding, 2, TextFilePager.MinWindowBytes + 1)
            Assert.Equal(0, pager.WindowSize Mod 4)
            For index As Integer = 0 To pager.TotalWindows - 1
                Dim window As String = pager.ReadWindow(index)
                Assert.False(Char.IsLowSurrogate(window(0)))
                Assert.False(Char.IsHighSurrogate(window(window.Length - 1)))
            Next
            Assert.Equal(content, Joined(pager))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Shift_jis_without_line_breaks_cuts_after_a_single_byte_character()
#If Not NETFRAMEWORK Then
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
        Dim encoding As Encoding = Encoding.GetEncoding(932)
        Dim content As String = Repeat("日本語のテキスト1", 8000)
        Dim path As String = WriteTemp(encoding.GetBytes(content))
        Try
            Dim pager As New TextFilePager(path, encoding, 0, TextFilePager.MinWindowBytes)
            For index As Integer = 1 To pager.TotalWindows - 1
                Assert.StartsWith("日", pager.ReadWindow(index))
            Next
            Assert.Equal(content, Joined(pager))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Crlf_pair_is_never_split_between_windows()
        ' One leading byte puts every nominal cut between a CR and its LF.
        Dim content As String = "x" & Repeat(New String("a"c, 4094) & vbCrLf, 20)
        Dim path As String = WriteTemp(Encoding.ASCII.GetBytes(content))
        Try
            Dim pager As New TextFilePager(path, Encoding.ASCII, 0, TextFilePager.MinWindowBytes)
            For index As Integer = 1 To pager.TotalWindows - 1
                Assert.False(pager.ReadWindow(index).StartsWith(vbLf, StringComparison.Ordinal))
            Next
            Assert.Equal(content, Joined(pager))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Seek_returns_the_window_holding_the_offset()
        Dim content As String = Repeat("line of text number" & vbLf, 6000)
        Dim path As String = WriteTemp(Encoding.ASCII.GetBytes(content))
        Try
            Dim pager As New TextFilePager(path, Encoding.ASCII, 0, TextFilePager.MinWindowBytes)
            For index As Integer = 0 To pager.TotalWindows - 1
                Dim startOffset As Long, endOffset As Long
                pager.GetWindowBounds(index, startOffset, endOffset)
                Assert.Equal(index, pager.SeekToOffset(startOffset))
                Assert.Equal(index, pager.SeekToOffset(endOffset - 1))
            Next
            Assert.Equal(0, pager.SeekToOffset(-5))
            Assert.Equal(pager.TotalWindows - 1, pager.SeekToOffset(pager.FileSize + 100))
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub Empty_file_has_one_empty_window_and_window_mb_is_clamped()
        Dim path As String = WriteTemp(Array.Empty(Of Byte)())
        Try
            Dim pager As New TextFilePager(path, New UTF8Encoding(False), 3, TextFilePager.WindowBytesFromMb(4))
            Assert.Equal(1, pager.TotalWindows)
            Assert.Equal("", pager.ReadWindow(5))
            Assert.Equal(0, pager.CurrentWindowIndex)
            Assert.Equal(1048576, TextFilePager.WindowBytesFromMb(0))
            Assert.Equal(16 * 1048576, TextFilePager.WindowBytesFromMb(99))
        Finally
            File.Delete(path)
        End Try
    End Sub
End Class
#End If
