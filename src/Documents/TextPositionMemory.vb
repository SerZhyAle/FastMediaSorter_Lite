#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading

' Specification 277 section 12.1, TextRememberPosition: where the user stopped reading a text file.

''' <summary>The place a document was left at: the window of a paged file, with its size because an index means another
''' stretch of the file under another size, and the logical line and column of the first row in view.</summary>
Public NotInheritable Class TextPlace
    Public ReadOnly Window As Integer
    Public ReadOnly WindowSize As Integer
    Public ReadOnly Line As Integer
    Public ReadOnly Column As Integer

    Public Sub New(window As Integer, windowSize As Integer, line As Integer, column As Integer)
        Me.Window = Math.Max(0, window)
        Me.WindowSize = Math.Max(0, windowSize)
        Me.Line = Math.Max(0, line)
        Me.Column = Math.Max(0, column)
    End Sub

    ''' <summary>The start of the file is where a file opens anyway, so it needs no record.</summary>
    Public ReadOnly Property IsStart As Boolean
        Get
            Return Window = 0 AndAlso Line = 0 AndAlso Column = 0
        End Get
    End Property
End Class

''' <summary>
''' One line per document, the newest last, at most <see cref="EntryLimit"/>. Unlike a remembered encoding a place survives
''' a change of the file: a log that grew is still read from the same line, and a line past the end lands on the last row.
''' Remember only changes the list, because it runs each time a text file is left; <see cref="Save"/> does the writing and
''' the caller runs it off the UI thread. An unreadable or oversized file is left untouched and the memory starts empty.
''' </summary>
Public NotInheritable Class TextPositionMemory
    Public Const EntryLimit As Integer = 500
    Public Const MaxFileBytes As Integer = 1048576
    Private Const Header As String = "fms-text-positions 1"

    Private Shared ReadOnly lazyCurrent As New Lazy(Of TextPositionMemory)(
        Function()
            Dim memory As New TextPositionMemory(DefaultPath())
            memory.Load()
            Return memory
        End Function, LazyThreadSafetyMode.ExecutionAndPublication)

    Private NotInheritable Class Entry
        Public Place As TextPlace
        Public Path As String
        Public Provenance As String
    End Class

    Private ReadOnly statePath As String
    Private ReadOnly gate As New Object()
    Private ReadOnly writeGate As New Object()
    Private ReadOnly entries As New List(Of Entry)()
    Private locked As Boolean
    Private dirty As Boolean
    Private diagnosticText As String = ""

    Public Sub New(path As String)
        statePath = If(path, "")
    End Sub

    Public Shared Function DefaultPath() As String
        Return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "SZA", "FastMediaSorter", "documents", "text-positions-v1.txt")
    End Function

    ''' <summary>The viewer's memory: created and loaded once, on first use.</summary>
    Public Shared ReadOnly Property Current As TextPositionMemory
        Get
            Return lazyCurrent.Value
        End Get
    End Property

    ''' <summary>A short English phrase for the last load or save problem, never a path; empty when all is well.</summary>
    Public ReadOnly Property Diagnostic As String
        Get
            SyncLock gate
                Return diagnosticText
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property Count As Integer
        Get
            SyncLock gate
                Return entries.Count
            End SyncLock
        End Get
    End Property

    Public Sub Load()
        SyncLock gate
            entries.Clear()
            locked = False
            dirty = False
            diagnosticText = ""
            Dim lines As String()
            Try
                If statePath.Length = 0 OrElse Not File.Exists(statePath) Then Return
                If New FileInfo(statePath).Length > MaxFileBytes Then
                    locked = True
                    diagnosticText = "too-large: the position memory file is over its size limit and was kept unchanged"
                    Return
                End If
                lines = File.ReadAllLines(statePath, New UTF8Encoding(False))
            Catch ex As Exception
                locked = True
                diagnosticText = "unreadable: the position memory file could not be read and was kept unchanged"
                Return
            End Try
            If lines.Length = 0 OrElse lines(0) <> Header Then
                locked = True
                diagnosticText = "unknown-format: the position memory file has another format and was kept unchanged"
                Return
            End If
            For index As Integer = 1 To lines.Length - 1
                Dim parsed As Entry = ParseLine(lines(index))
                If parsed Is Nothing Then Continue For
                entries.RemoveAll(Function(item) Same(item, parsed.Path, parsed.Provenance))
                entries.Add(parsed)
                ' Trimmed line by line: a damaged file of many short lines would otherwise make the scan above quadratic.
                If entries.Count > EntryLimit Then entries.RemoveAt(0)
            Next
        End SyncLock
    End Sub

    ''' <summary>The place this document was left at, or Nothing.</summary>
    Public Function Lookup(identity As DocumentIdentity) As TextPlace
        If Not Usable(identity) Then Return Nothing
        SyncLock gate
            For index As Integer = entries.Count - 1 To 0 Step -1
                If Same(entries(index), identity.Path, If(identity.Provenance, "")) Then Return entries(index).Place
            Next
        End SyncLock
        Return Nothing
    End Function

    ''' <summary>Records a place in memory only. A document left at its very start loses its record instead.</summary>
    Public Sub Remember(identity As DocumentIdentity, place As TextPlace)
        If Not Usable(identity) OrElse place Is Nothing Then Return
        Dim provenance As String = If(identity.Provenance, "")
        SyncLock gate
            If locked Then Return
            Dim removed As Integer = entries.RemoveAll(Function(item) Same(item, identity.Path, provenance))
            If place.IsStart Then
                If removed > 0 Then dirty = True
                Return
            End If
            entries.Add(New Entry With {.Place = place, .Path = identity.Path, .Provenance = provenance})
            If entries.Count > EntryLimit Then entries.RemoveRange(0, entries.Count - EntryLimit)
            dirty = True
        End SyncLock
    End Sub

    Public Sub Clear()
        SyncLock gate
            entries.Clear()
            locked = False
            dirty = True
        End SyncLock
    End Sub

    ''' <summary>Writes what changed since the last write; with nothing changed it touches nothing. The list is copied under
    ''' the gate and written outside it, so a Remember on the UI thread never waits for the disk.</summary>
    Public Sub Save()
        SyncLock writeGate
            Dim content As String
            SyncLock gate
                If locked OrElse Not dirty OrElse statePath.Length = 0 Then Return
                content = BuildFile()
                dirty = False
            End SyncLock
            Dim problem As String = WriteFile(content)
            SyncLock gate
                diagnosticText = problem
                If problem.Length > 0 Then dirty = True
            End SyncLock
        End SyncLock
    End Sub

    ' A tab, CR or LF would break the line format; Windows paths cannot carry them, so such an identity is refused.
    Private Shared Function Usable(identity As DocumentIdentity) As Boolean
        If identity Is Nothing OrElse String.IsNullOrWhiteSpace(identity.Path) Then Return False
        Dim forbidden As Char() = {ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}
        Return identity.Path.IndexOfAny(forbidden) < 0 AndAlso If(identity.Provenance, "").IndexOfAny(forbidden) < 0
    End Function

    Private Shared Function Same(item As Entry, path As String, provenance As String) As Boolean
        Return String.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(item.Provenance, provenance, StringComparison.Ordinal)
    End Function

    ' window TAB windowSize TAB line TAB column TAB provenance TAB path
    Private Shared Function ParseLine(line As String) As Entry
        Dim parts As String() = line.Split(ControlChars.Tab)
        If parts.Length <> 6 Then Return Nothing
        Dim window As Integer
        Dim windowSize As Integer
        Dim lineIndex As Integer
        Dim column As Integer
        If Not Integer.TryParse(parts(0), NumberStyles.None, CultureInfo.InvariantCulture, window) OrElse
           Not Integer.TryParse(parts(1), NumberStyles.None, CultureInfo.InvariantCulture, windowSize) OrElse
           Not Integer.TryParse(parts(2), NumberStyles.None, CultureInfo.InvariantCulture, lineIndex) OrElse
           Not Integer.TryParse(parts(3), NumberStyles.None, CultureInfo.InvariantCulture, column) OrElse
           String.IsNullOrWhiteSpace(parts(5)) Then
            Return Nothing
        End If
        Return New Entry With {.Place = New TextPlace(window, windowSize, lineIndex, column), .Provenance = parts(4), .Path = parts(5)}
    End Function

    Private Function BuildFile() As String
        Dim builder As New StringBuilder()
        builder.Append(Header).Append(ControlChars.Lf)
        For Each item As Entry In entries
            builder.Append(item.Place.Window.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                .Append(item.Place.WindowSize.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                .Append(item.Place.Line.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                .Append(item.Place.Column.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                .Append(item.Provenance).Append(ControlChars.Tab) _
                .Append(item.Path).Append(ControlChars.Lf)
        Next
        Return builder.ToString()
    End Function

    Private Function WriteFile(content As String) As String
        Dim temp As String = Nothing
        Try
            Dim folder As String = System.IO.Path.GetDirectoryName(statePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            temp = statePath & "." & Guid.NewGuid().ToString("N") & ".tmp"
            File.WriteAllText(temp, content, New UTF8Encoding(False))
            If File.Exists(statePath) Then
                File.Replace(temp, statePath, Nothing)
            Else
                File.Move(temp, statePath)
            End If
            temp = Nothing
            Return ""
        Catch ex As Exception
            Return "save-failed: the position memory could not be written and stays in memory only"
        Finally
            If temp IsNot Nothing Then
                Try
                    File.Delete(temp)
                Catch ex As Exception
                    ' A leftover temp file is harmless; the next save uses a new name.
                End Try
            End If
        End Try
    End Function
End Class
#End If
