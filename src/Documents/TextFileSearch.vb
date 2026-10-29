#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Threading

' Specification 277 section 5.1 item 4: the full-file search of a paged document. Every window of
' the file is read through a pager of its own, laid out exactly as the reader lays it out (the same
' tab width, the same dropped trailing line), and searched with the reader's own matcher, so a hit
' names the same window, line and column the reader shows. Runs on the pool; the reader's pager is
' never touched. No WinForms reference; Main_Form.TextReader.vb starts it and shows the hits.

''' <summary>One hit of a full-file search: the window it is in and its place in that window's lines.</summary>
Public Structure TextFileHit
    Public ReadOnly Window As Integer
    Public ReadOnly Match As TextMatch

    Public Sub New(window As Integer, match As TextMatch)
        Me.Window = window
        Me.Match = match
    End Sub
End Structure

''' <summary>Everything the scan needs, captured on the UI thread.</summary>
Public NotInheritable Class TextFileSearchRequest
    Public Property SourcePath As String
    Public Property SourceEncoding As Encoding
    Public Property BomLength As Integer
    Public Property WindowBytes As Integer
    ''' <summary>The file size the reader's pager saw: a file of another size has other windows.</summary>
    Public Property ExpectedFileSize As Long
    Public Property TabWidth As Integer
    Public Property Query As String
    Public Property MatchCase As Boolean
    Public Property WholeWord As Boolean
    Public Property UseRegex As Boolean
End Class

''' <summary>What the scan found. <see cref="Problem"/> is Nothing for a complete scan, "limit" when it stopped at
''' <see cref="TextReaderLayout.MaxMatches"/> (the hits are the first ones), "timeout" or "pattern" when the regular
''' expression stopped it, "changed" when the file no longer has the size the reader saw.</summary>
Public NotInheritable Class TextFileSearchResult
    Public Property Hits As New List(Of TextFileHit)()
    Public Property Problem As String
    Public Property TotalWindows As Integer
End Class

Public Module TextFileSearch

    ''' <summary>
    ''' Searches every window of the file in order and returns the hits in reading order (window, line, column).
    ''' <paramref name="progress"/> hears the number of windows done after each one. A cancellation is checked
    ''' between windows and ends the scan with <see cref="OperationCanceledException"/>.
    ''' </summary>
    Public Function Scan(request As TextFileSearchRequest, cancel As CancellationToken, progress As Action(Of Integer)) As TextFileSearchResult
        Dim result As New TextFileSearchResult()
        Dim pager As New TextFilePager(request.SourcePath, request.SourceEncoding, request.BomLength, request.WindowBytes)
        result.TotalWindows = pager.TotalWindows
        If pager.FileSize <> request.ExpectedFileSize Then
            result.Problem = "changed"
            Return result
        End If
        For window As Integer = 0 To pager.TotalWindows - 1
            cancel.ThrowIfCancellationRequested()
            Dim lines As TextReaderLines = TextReaderLayout.BuildLines(pager.ReadWindow(window), request.TabWidth, False,
                window < pager.TotalWindows - 1)
            Dim problem As String = Nothing
            Dim matches As List(Of TextMatch) = TextReaderLayout.FindMatches(lines.CopyLines, request.Query,
                request.MatchCase, request.WholeWord, request.UseRegex, problem)
            Dim taken As Integer = Math.Min(TextReaderLayout.MaxMatches - result.Hits.Count, matches.Count)
            For i As Integer = 0 To taken - 1
                result.Hits.Add(New TextFileHit(window, matches(i)))
            Next
            ' Full: the hits are the first ones whenever anything could still follow.
            If result.Hits.Count >= TextReaderLayout.MaxMatches AndAlso
               (matches.Count > taken OrElse problem = "limit" OrElse window < pager.TotalWindows - 1) Then
                result.Problem = "limit"
                Return result
            End If
            If problem IsNot Nothing Then
                result.Problem = problem
                Return result
            End If
            If progress IsNot Nothing Then progress(window + 1)
        Next
        Return result
    End Function

    ''' <summary>The hits of one window as the reader's in-window matches, and the index of the first of them in
    ''' <paramref name="hits"/> (-1 when the window has none).</summary>
    Public Function WindowMatches(hits As IList(Of TextFileHit), window As Integer, ByRef firstIndex As Integer) As List(Of TextMatch)
        Dim matches As New List(Of TextMatch)()
        firstIndex = -1
        If hits Is Nothing OrElse hits.Count = 0 Then Return matches
        Dim low As Integer = 0
        Dim high As Integer = hits.Count
        While low < high
            Dim middle As Integer = (low + high) \ 2
            If hits(middle).Window < window Then low = middle + 1 Else high = middle
        End While
        Dim i As Integer = low
        While i < hits.Count AndAlso hits(i).Window = window
            matches.Add(hits(i).Match)
            i += 1
        End While
        If matches.Count > 0 Then firstIndex = low
        Return matches
    End Function

    ''' <summary>
    ''' The next or previous hit. From a hit in the window on screen it steps by one, wrapping round; otherwise
    ''' (the reader moved to another window since) it starts at the reading position - forward the first hit at
    ''' or after <paramref name="line"/> of <paramref name="window"/>, backward the last one before it - wrapping
    ''' round too. -1 when there are no hits.
    ''' </summary>
    Public Function StepFrom(hits As IList(Of TextFileHit), current As Integer, window As Integer, line As Integer,
                             forward As Boolean) As Integer
        If hits Is Nothing OrElse hits.Count = 0 Then Return -1
        If current >= 0 AndAlso current < hits.Count AndAlso hits(current).Window = window Then
            Return TextReaderLayout.StepMatch(hits.Count, current, forward)
        End If
        If forward Then
            For i As Integer = 0 To hits.Count - 1
                If hits(i).Window > window OrElse (hits(i).Window = window AndAlso hits(i).Match.Line >= line) Then Return i
            Next
            Return 0
        End If
        For i As Integer = hits.Count - 1 To 0 Step -1
            If hits(i).Window < window OrElse (hits(i).Window = window AndAlso hits(i).Match.Line < line) Then Return i
        Next
        Return hits.Count - 1
    End Function

End Module
#End If
