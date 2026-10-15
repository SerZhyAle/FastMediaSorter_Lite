Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text

' The history and the mutation journal of scheduled operations (ticket 222, 6 and 7.5).
'
' TWO FILES, ON PURPOSE. Section 6 wants "every scheduled move/delete recorded" so a surface that shows
' a changed folder can reconcile; section 7.5 wants a history trimmed to the last 500 lines. One file
' cannot be both: a nightly delete of two thousand files would push every run summary out of a
' 500-line window within a day. So the history (one line per run, one per note) is capped as FMS_A caps
' its own, and the per-file mutations go to a second append-only file with a larger bound.
'
' THE PERSISTED LINE OUTLIVES A LANGUAGE CHANGE: it holds codes, numbers and invariant-culture times,
' never a sentence. The words are composed when the history is shown (ScheduledText).
Friend NotInheritable Class JournalEntry
    ''' <summary>Wall-clock time of the event.</summary>
    Friend Property Timestamp As DateTime
    ''' <summary>"run" for a finished run, "note" for everything else.</summary>
    Friend Property Kind As String = "run"
    ''' <summary>The outcome ("ok", "failed", "refused", "wall", "cancelled") or the note
    ''' ("missed", "late", "busy", "paused", "resumed", "resource-removed").</summary>
    Friend Property Code As String = ""
    Friend Property OperationId As String = ""
    Friend Property OperationName As String = ""
    ''' <summary>"schedule", "runNow", "runAll" or "catchUp".</summary>
    Friend Property Trigger As String = ""
    Friend Property Count As Integer
    Friend Property Failed As Integer
    Friend Property Skipped As Integer
    Friend Property Bytes As Long
    Friend Property DurationMs As Long
    ''' <summary>For a delete: "recycle", "permanent" or "mixed", with the reason when permanent.</summary>
    Friend Property Verdict As String = ""
    Friend Property Source As String = ""
    Friend Property Destination As String = ""
    ''' <summary>The first error, the wall's reason, or the slot a late run was due at - machine text.</summary>
    Friend Property Detail As String = ""
End Class

''' <summary>One mutation of one file, for the reconciliation journal.</summary>
Friend NotInheritable Class ItemJournalEntry
    Friend Property Timestamp As DateTime
    Friend Property OperationId As String = ""
    ''' <summary>"move", "delete" or "archive-delete" (the original removed after the archive was verified).</summary>
    Friend Property Verb As String = ""
    Friend Property Source As String = ""
    Friend Property Destination As String = ""
    Friend Property Verdict As String = ""
End Class

Friend NotInheritable Class ScheduledJournal

    Private Const HistoryFileName As String = "history.log"
    Private Const ItemsFileName As String = "items.log"
    Private Const LineVersion As String = "v1"
    ''' <summary>The cap of the history (7.5): 1 MB, trimmed to the last 500 lines.</summary>
    Friend Const HistoryMaxBytes As Long = 1024L * 1024L
    Friend Const HistoryKeepLines As Integer = 500
    ''' <summary>The cap of the mutation journal: larger, because one run can write thousands of lines.</summary>
    Friend Const ItemsMaxBytes As Long = 4L * 1024L * 1024L
    Friend Const ItemsKeepLines As Integer = 10000
    ''' <summary>How many lines the history window reads, newest first (7.5).</summary>
    Friend Const ReadWindow As Integer = 200

    Private ReadOnly folder As String
    Private ReadOnly gate As New Object()

    Friend Sub New(folder As String)
        Me.folder = folder
    End Sub

    Private ReadOnly Property HistoryPath As String
        Get
            Return Path.Combine(folder, HistoryFileName)
        End Get
    End Property

    Private ReadOnly Property ItemsPath As String
        Get
            Return Path.Combine(folder, ItemsFileName)
        End Get
    End Property

    Friend Sub Append(entry As JournalEntry)
        Dim fields() As String = {LineVersion, Stamp(entry.Timestamp), entry.Kind, entry.Code, entry.OperationId, entry.Trigger,
            entry.Count.ToString(CultureInfo.InvariantCulture), entry.Failed.ToString(CultureInfo.InvariantCulture),
            entry.Skipped.ToString(CultureInfo.InvariantCulture), entry.Bytes.ToString(CultureInfo.InvariantCulture),
            entry.DurationMs.ToString(CultureInfo.InvariantCulture), entry.Verdict, entry.OperationName,
            entry.Source, entry.Destination, entry.Detail}
        AppendLine(HistoryPath, fields, HistoryMaxBytes, HistoryKeepLines)
    End Sub

    Friend Sub AppendItem(entry As ItemJournalEntry)
        Dim fields() As String = {LineVersion, Stamp(entry.Timestamp), entry.OperationId, entry.Verb,
            entry.Source, entry.Destination, entry.Verdict}
        AppendLine(ItemsPath, fields, ItemsMaxBytes, ItemsKeepLines)
    End Sub

    Private Sub AppendLine(path As String, fields As String(), maxBytes As Long, keepLines As Integer)
        Try
            Dim line As String = String.Join(vbTab, fields.Select(AddressOf Escape))
            SyncLock gate
                Directory.CreateDirectory(folder)
                System.IO.File.AppendAllText(path, line & vbLf, New UTF8Encoding(False))
                TrimFile(path, maxBytes, keepLines)
            End SyncLock
        Catch ex As Exception
            ' A log that cannot be written must never fail a run.
            AppFileLogger.LogException("Scheduled operations: journal", ex)
        End Try
    End Sub

    ''' <summary>Past the cap, only the last lines stay (7.5).</summary>
    Private Shared Sub TrimFile(path As String, maxBytes As Long, keepLines As Integer)
        Dim info As New FileInfo(path)
        If Not info.Exists OrElse info.Length <= maxBytes Then Return
        Dim lines As String() = System.IO.File.ReadAllLines(path, Encoding.UTF8)
        If lines.Length <= keepLines Then Return
        System.IO.File.WriteAllLines(path, lines.Skip(lines.Length - keepLines), New UTF8Encoding(False))
    End Sub

    ''' <summary>The newest <paramref name="limit"/> history lines, newest first. Damaged lines are skipped.</summary>
    Friend Function ReadRecent(Optional limit As Integer = ReadWindow) As List(Of JournalEntry)
        Dim result As New List(Of JournalEntry)()
        Try
            SyncLock gate
                If Not System.IO.File.Exists(HistoryPath) Then Return result
                Dim lines As String() = System.IO.File.ReadAllLines(HistoryPath, Encoding.UTF8)
                For i As Integer = lines.Length - 1 To 0 Step -1
                    If result.Count >= limit Then Exit For
                    Dim entry As JournalEntry = ParseHistoryLine(lines(i))
                    If entry IsNot Nothing Then result.Add(entry)
                Next
            End SyncLock
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading the history", ex)
        End Try
        Return result
    End Function

    Friend Function ReadItems(Optional limit As Integer = 1000) As List(Of ItemJournalEntry)
        Dim result As New List(Of ItemJournalEntry)()
        Try
            SyncLock gate
                If Not System.IO.File.Exists(ItemsPath) Then Return result
                Dim lines As String() = System.IO.File.ReadAllLines(ItemsPath, Encoding.UTF8)
                For i As Integer = lines.Length - 1 To 0 Step -1
                    If result.Count >= limit Then Exit For
                    Dim parts As String() = lines(i).Split(vbTab(0))
                    If parts.Length < 7 OrElse parts(0) <> LineVersion Then Continue For
                    Dim at As DateTime? = ParseStamp(parts(1))
                    If Not at.HasValue Then Continue For
                    result.Add(New ItemJournalEntry With {.Timestamp = at.Value, .OperationId = Unescape(parts(2)),
                        .Verb = Unescape(parts(3)), .Source = Unescape(parts(4)), .Destination = Unescape(parts(5)),
                        .Verdict = Unescape(parts(6))})
                Next
            End SyncLock
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: reading the mutation journal", ex)
        End Try
        Return result
    End Function

    ''' <summary>"Clear history" is one action (7.5). The mutation journal is a different record
    ''' (it serves reconciliation) and is left as it is.</summary>
    Friend Sub ClearHistory()
        Try
            SyncLock gate
                If System.IO.File.Exists(HistoryPath) Then System.IO.File.Delete(HistoryPath)
            End SyncLock
        Catch ex As Exception
            AppFileLogger.LogException("Scheduled operations: clearing the history", ex)
        End Try
    End Sub

    ' --- the line --------------------------------------------------------------------------------

    Friend Shared Function ParseHistoryLine(line As String) As JournalEntry
        If String.IsNullOrEmpty(line) Then Return Nothing
        Dim parts As String() = line.Split(vbTab(0))
        If parts.Length < 16 OrElse parts(0) <> LineVersion Then Return Nothing
        Dim at As DateTime? = ParseStamp(parts(1))
        If Not at.HasValue Then Return Nothing
        Dim entry As New JournalEntry With {
            .Timestamp = at.Value, .Kind = Unescape(parts(2)), .Code = Unescape(parts(3)),
            .OperationId = Unescape(parts(4)), .Trigger = Unescape(parts(5)),
            .Verdict = Unescape(parts(11)), .OperationName = Unescape(parts(12)),
            .Source = Unescape(parts(13)), .Destination = Unescape(parts(14)), .Detail = Unescape(parts(15))}
        Integer.TryParse(parts(6), NumberStyles.Integer, CultureInfo.InvariantCulture, entry.Count)
        Integer.TryParse(parts(7), NumberStyles.Integer, CultureInfo.InvariantCulture, entry.Failed)
        Integer.TryParse(parts(8), NumberStyles.Integer, CultureInfo.InvariantCulture, entry.Skipped)
        Long.TryParse(parts(9), NumberStyles.Integer, CultureInfo.InvariantCulture, entry.Bytes)
        Long.TryParse(parts(10), NumberStyles.Integer, CultureInfo.InvariantCulture, entry.DurationMs)
        Return entry
    End Function

    ''' <summary>Invariant-culture, wall-clock: the line outlives a change of language.</summary>
    Private Shared Function Stamp(value As DateTime) As String
        Return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function ParseStamp(text As String) As DateTime?
        Dim parsed As DateTime
        If DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
            Return parsed
        End If
        Return Nothing
    End Function

    ''' <summary>One line per record: a tab or a line break inside a field cannot split it.</summary>
    Friend Shared Function Escape(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Return text.Replace("\", "\\").Replace(vbTab, "\t").Replace(vbCr, "\r").Replace(vbLf, "\n")
    End Function

    Friend Shared Function Unescape(text As String) As String
        If String.IsNullOrEmpty(text) OrElse text.IndexOf("\"c) < 0 Then Return If(text, "")
        Dim sb As New StringBuilder(text.Length)
        Dim i As Integer = 0
        While i < text.Length
            Dim c As Char = text(i)
            If c = "\"c AndAlso i + 1 < text.Length Then
                Select Case text(i + 1)
                    Case "t"c : sb.Append(vbTab(0))
                    Case "r"c : sb.Append(vbCr(0))
                    Case "n"c : sb.Append(vbLf(0))
                    Case "\"c : sb.Append("\"c)
                    Case Else
                        sb.Append(c)
                        sb.Append(text(i + 1))
                End Select
                i += 2
            Else
                sb.Append(c)
                i += 1
            End If
        End While
        Return sb.ToString()
    End Function

End Class
