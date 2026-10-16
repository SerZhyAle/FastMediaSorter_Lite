#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports Xunit

''' <summary>
''' 105 L02 (057.S15-4): the bytes that leave a live log are bounded by the length captured at
''' open, whatever the writer does meanwhile. The stream under test misbehaves on purpose -
''' it appends on every read, truncates, or swaps the file - so the cap is proven against a
''' source that never holds still, and the proof counts bytes actually read.
''' </summary>
Public Class LogTailSnapshotTests

    ''' <summary>A source that changes under the reader and counts what it hands out.</summary>
    Private NotInheritable Class RestlessStream
        Inherits Stream

        Private ReadOnly inner As FileStream
        Private ReadOnly path As String
        Private ReadOnly onEveryRead As Action(Of String)
        Friend BytesServed As Long
        Friend ReadCalls As Integer

        Friend Sub New(path As String, onEveryRead As Action(Of String))
            Me.path = path
            Me.onEveryRead = onEveryRead
            inner = New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean = True
        Public Overrides ReadOnly Property CanSeek As Boolean = True
        Public Overrides ReadOnly Property CanWrite As Boolean = False

        Public Overrides ReadOnly Property Length As Long
            Get
                Return inner.Length
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Return inner.Position
            End Get
            Set(value As Long)
                inner.Position = value
            End Set
        End Property

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            ReadCalls += 1
            If ReadCalls > 100000 Then Throw New InvalidOperationException("the read loop did not terminate")
            onEveryRead(path)
            Dim got As Integer = inner.Read(buffer, offset, count)
            BytesServed += got
            Return got
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Return inner.Seek(offset, origin)
        End Function

        Public Overrides Sub Flush()
        End Sub

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Throw New NotSupportedException()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then inner.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    Private Shared Function TempLog(content As Byte()) As String
        Dim p As String = Path.Combine(Path.GetTempPath(), "fms-tail-" & Guid.NewGuid().ToString("N") & ".log")
        File.WriteAllBytes(p, content)
        Return p
    End Function

    Private Shared Function Lines(count As Integer, Optional width As Integer = 40) As Byte()
        Dim sb As New StringBuilder()
        For i As Integer = 0 To count - 1
            sb.Append("L").Append(i.ToString("D5")).Append(" ").Append(New String("x"c, width)).Append(vbLf)
        Next
        Return Encoding.UTF8.GetBytes(sb.ToString())
    End Function

    Private Shared Sub AppendTo(path As String)
        Using f As New FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
            Dim junk As Byte() = Encoding.UTF8.GetBytes(New String("z"c, 4096) & vbLf)
            f.Write(junk, 0, junk.Length)
        End Using
    End Sub

    <Fact>
    Public Sub A_log_appended_on_every_read_never_yields_more_than_the_cap_and_the_read_ends()
        Dim path As String = TempLog(Lines(200))
        Try
            Const cap As Long = 1000
            Using source As New RestlessStream(path, AddressOf AppendTo)
                Dim snap As LogPackage.TailSnapshot = LogPackage.ReadTail(source, cap)
                Assert.True(snap.Truncated)
                Assert.True(source.BytesServed <= cap, "read " & source.BytesServed.ToString() & " original bytes for a cap of " & cap.ToString())
                Assert.True(snap.Bytes.Length <= cap)
                Assert.DoesNotContain(CByte(AscW("z"c)), snap.Bytes) ' nothing appended after the snapshot leaked in
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_log_shorter_than_the_cap_that_keeps_growing_is_read_up_to_its_captured_length_only()
        Dim original As Byte() = Lines(5)
        Dim path As String = TempLog(original)
        Try
            Using source As New RestlessStream(path, AddressOf AppendTo)
                Dim snap As LogPackage.TailSnapshot = LogPackage.ReadTail(source, 1024L * 1024L)
                Assert.False(snap.Truncated)
                Assert.Equal(original.Length, snap.Bytes.Length)
                Assert.Equal(CLng(original.Length), source.BytesServed)
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub The_partial_first_record_of_a_tail_is_dropped_and_counted_against_the_cap()
        Dim content As Byte() = Lines(100)
        Dim path As String = TempLog(content)
        Try
            Const cap As Long = 777
            Using source As New RestlessStream(path, Sub(p) Return)
                Dim snap As LogPackage.TailSnapshot = LogPackage.ReadTail(source, cap)
                Assert.True(snap.Truncated)
                Assert.Equal(cap, source.BytesServed) ' the discarded half-line was read, so it is inside the cap
                Dim text As String = Encoding.UTF8.GetString(snap.Bytes)
                Assert.StartsWith("L", text) ' starts on a record, not in the middle of one
                For Each line As String In text.Split({vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Assert.Equal(47, line.Length) ' every record whole
                Next
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_multibyte_character_cut_by_the_cap_is_dropped_not_decoded_as_garbage()
        ' "я" is D1 8F. The source ends after the lead byte - a writer caught mid-character, or a cap that falls inside one.
        Dim bytes As New List(Of Byte)(Encoding.UTF8.GetBytes("first" & vbLf & "second" & vbLf & "ok "))
        bytes.Add(&HD1)
        Dim path As String = TempLog(bytes.ToArray())
        Try
            Using source As New RestlessStream(path, Sub(p) Return)
                Dim snap As LogPackage.TailSnapshot = LogPackage.ReadTail(source, 4096)
                Dim text As String = Encoding.UTF8.GetString(snap.Bytes)
                Assert.False(text.Contains(ChrW(&HFFFD).ToString()), "a cut character was decoded as a replacement character")
                Assert.EndsWith("ok ", text)
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub A_complete_multibyte_character_at_the_end_is_kept()
        Dim bytes As Byte() = Encoding.UTF8.GetBytes("line" & vbLf & "конец")
        Assert.Equal(0, LogPackage.IncompleteUtf8TailLength(bytes, 0))
        Dim cutThreeByte As Byte() = Encoding.UTF8.GetBytes("a" & ChrW(&H20AC)).Take(3).ToArray() ' 'a' + E2 82 (euro cut before AC)
        Assert.Equal(2, LogPackage.IncompleteUtf8TailLength(cutThreeByte, 0))
    End Sub

    <Fact>
    Public Sub A_log_truncated_while_it_is_being_read_ends_in_a_short_read_without_a_fault()
        Dim path As String = TempLog(Lines(200))
        Try
            Dim cut As Boolean
            Using source As New RestlessStream(path, Sub(p)
                                                         If cut Then Return
                                                         cut = True
                                                         Using f As New FileStream(p, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite)
                                                         End Using
                                                     End Sub)
                Dim snap As LogPackage.TailSnapshot = LogPackage.ReadTail(source, 1000)
                Assert.True(snap.Bytes.Length <= 1000)
                Assert.True(source.ReadCalls < 100)
            End Using
        Finally
            File.Delete(path)
        End Try
    End Sub

    <Fact>
    Public Sub An_archive_entry_from_an_actively_written_log_holds_the_cut_marker_and_stays_within_the_cap()
        Dim path As String = TempLog(Lines(80000)) ' about 3.7 MB, more than the 2 MB cap
        Dim zipPath As String = path & ".zip"
        Try
            Using zip As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zip, ZipArchiveMode.Create)
                    Dim outcome As LogPackage.LogEntryOutcome = LogPackage.CopyLogEntry(archive, "current.log", path,
                                                                                        Function(p) New RestlessStream(p, AddressOf AppendTo))
                    Assert.Equal(LogPackage.LogSourceState.Included, outcome.State)
                    Assert.NotNull(outcome.Line)
                End Using
            End Using
            Using zip As New ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read)
                Dim entry As ZipArchiveEntry = zip.GetEntry("current.log")
                Using reader As New StreamReader(entry.Open(), Encoding.UTF8)
                    Dim text As String = reader.ReadToEnd()
                    Assert.StartsWith("L00000 ", text) ' the head of the log is kept
                    Assert.Contains("[Diag] LOG TRUNCATED | dropped_middle_bytes=", text)
                    Assert.Contains("L79999 ", text) ' and the last record that existed when the read began
                    Assert.DoesNotContain("zzzz", text) ' nothing the writer appended afterwards leaked in
                    Assert.True(Encoding.UTF8.GetByteCount(text) <= 2 * 1024 * 1024 + 200)
                End Using
            End Using
        Finally
            File.Delete(path)
            If File.Exists(zipPath) Then File.Delete(zipPath)
        End Try
    End Sub

    <Fact>
    Public Sub Recent_errors_are_read_from_the_same_bounded_snapshot()
        Dim sb As New StringBuilder()
        sb.Append(Encoding.UTF8.GetString(Lines(70000)))
        sb.Append("2026-10-02 FATAL Unhandled crash in the tail" & vbLf)
        Dim path As String = TempLog(Encoding.UTF8.GetBytes(sb.ToString()))
        Try
            Dim served As Long
            Dim found As List(Of String) = LogPackage.RecentErrorLines(
                Function(p)
                    Dim s As New RestlessStream(p, AddressOf AppendTo)
                    Return s
                End Function, path)
            Assert.Contains(found, Function(l) l.Contains("Unhandled crash in the tail"))
            Assert.DoesNotContain(found, Function(l) l.Contains("zzzz"))
            GC.KeepAlive(served)
        Finally
            File.Delete(path)
        End Try
    End Sub

End Class
#End If
