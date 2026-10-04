Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>Read-only evidence of unfinished sessions; never diagnoses a crash.</summary>
Friend Module LogSessionDiagnostics
    Private ReadOnly Marker As New Regex("^Session (started|finished): (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4})(?:; PID=(\d+)(?:;.*)?)?$", RegexOptions.CultureInvariant)

    Friend Function UnfinishedSessions(text As String) As List(Of String)
        Dim pending As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim legacy As String = Nothing
        Using reader As New StringReader(If(text, ""))
            Dim line = reader.ReadLine()
            While line IsNot Nothing
                Dim match = Marker.Match(line.TrimStart(ChrW(65279))) ' UTF-8 byte-order mark, not a UI glyph.
                Dim stamp As DateTime
                If match.Success AndAlso DateTime.TryParseExact(match.Groups(2).Value, "yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture, DateTimeStyles.None, stamp) Then
                    Dim pid = match.Groups(3).Value
                    If match.Groups(1).Value = "started" Then
                        If pid = "" Then
                            legacy = match.Groups(2).Value
                        Else
                            pending(pid) = match.Groups(2).Value
                        End If
                    ElseIf pid <> "" Then
                        pending.Remove(pid)
                    Else
                        ' Old releases did not identify the exiting process. Resolve only an
                        ' unambiguous single session; never let one exit clear two processes.
                        If pending.Count = 1 AndAlso legacy Is Nothing Then pending.Clear()
                        If pending.Count = 0 Then legacy = Nothing
                    End If
                End If
                line = reader.ReadLine()
            End While
        End Using
        Dim result As New List(Of String)
        If legacy IsNot Nothing Then result.Add(Warning("unknown", legacy))
        For Each session In pending.OrderBy(Function(item) item.Value, StringComparer.Ordinal)
            result.Add(Warning(session.Key, session.Value))
        Next
        Return result
    End Function

    Private Function Warning(pid As String, started As String) As String
        Return "Previous session PID=" & pid & " started " & started &
            " did not finish (crash or kill); see Windows Error Reporting (Application events 1000/1001/1026) and %LOCALAPPDATA%\CrashDumps"
    End Function

    Friend Function ReadPreviousWarnings(logPath As String) As List(Of String)
        Try
            Using stream As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                Dim snapshotLength = stream.Length
                Dim offset = Math.Max(0L, snapshotLength - 8L * 1024L * 1024L)
                stream.Position = offset
                Dim bytes(CInt(snapshotLength - offset) - 1) As Byte
                Dim count = 0
                While count < bytes.Length
                    Dim read = stream.Read(bytes, count, bytes.Length - count)
                    If read = 0 Then Exit While
                    count += read
                End While
                Using reader As New StringReader(Encoding.UTF8.GetString(bytes, 0, count))
                    If offset > 0 Then reader.ReadLine() ' Discard the possibly partial first line.
                    Return UnfinishedSessions(reader.ReadToEnd()).Where(Function(line) Not SessionStillRunning(line)).ToList()
                End Using
            End Using
        Catch
            Return New List(Of String)
        End Try
    End Function

    Private Function SessionStillRunning(line As String) As Boolean
        Dim match = Regex.Match(line, "PID=(\d+) started (.{24})")
        If Not match.Success Then Return False
        Try
            Using process = Diagnostics.Process.GetProcessById(Integer.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture))
                Dim started = DateTime.ParseExact(match.Groups(2).Value, "yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture)
                ' A reused PID starts after the recorded session header.
                Return Not process.HasExited AndAlso process.StartTime <= started
            End Using
        Catch ex As ArgumentException
            Return False
        Catch
            Return True ' Cannot inspect a live process: do not call it unfinished.
        End Try
    End Function
End Module
