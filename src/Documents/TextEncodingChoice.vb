#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading

' Specification 277 section 6.2, step P3.4: the pure half of Re-open with Encoding - the groups the dialog
' shows, the five-line preview of a choice, and the per-document memory of a choice (section 6.1 tier 1).
' Nothing here writes the document: a choice changes only how its bytes are read.

Public Enum TextEncodingGroup
    Unicode
    Cyrillic
    Western
    EastAsian
End Enum

Public Structure TextEncodingPreview
    Public Lines As String()
    ''' <summary>Characters the choice cannot read in the probe: replacement characters and control characters.</summary>
    Public UnreadableCount As Integer
    Public HasBom As Boolean
End Structure

Public Module TextEncodingChoice
    Public Const PreviewLineCount As Integer = 5
    Public Const PreviewColumnLimit As Integer = 200
    ''' <summary>Bytes read from the start of the file for the preview; five lines fit in far less.</summary>
    Public Const PreviewProbeBytes As Integer = 16384

    Public Function GroupOf(codePage As Integer) As TextEncodingGroup
        Select Case codePage
            Case 65001, 1200, 1201, 12000, 12001
                Return TextEncodingGroup.Unicode
            Case 1251, 866, 20866, 21866
                Return TextEncodingGroup.Cyrillic
            Case 1252, 28591
                Return TextEncodingGroup.Western
            Case Else
                Return TextEncodingGroup.EastAsian
        End Select
    End Function

    Public Function IsManualCodePage(codePage As Integer) As Boolean
        Return TextEncodingDetector.ManualCandidates.Any(Function(candidate) candidate.CodePage = codePage)
    End Function

    ''' <summary>The choice the dialog starts on: the encoding in use when the list offers it, else UTF-8.</summary>
    Public Function InitialChoice(current As Encoding) As Integer
        If current IsNot Nothing AndAlso IsManualCodePage(current.CodePage) Then Return current.CodePage
        Return 65001
    End Function

    ''' <summary>
    ''' Decodes the start of a file with <paramref name="codePage"/> for the preview: the BOM of that encoding is
    ''' skipped, at most <see cref="PreviewLineCount"/> lines of at most <see cref="PreviewColumnLimit"/> characters
    ''' come back, and every character the choice cannot read (a replacement or a control character other than a
    ''' tab) is shown as U+FFFD and counted. A cut character at the end of a truncated probe is not counted.
    ''' </summary>
    Public Function Preview(bytes As Byte(), length As Integer, isTruncated As Boolean, codePage As Integer) As TextEncodingPreview
        Dim result As New TextEncodingPreview With {.Lines = New String() {}}
        length = If(bytes Is Nothing, 0, Math.Max(0, Math.Min(length, bytes.Length)))
        Dim encoding As Encoding = TextEncodingDetector.CreateEncoding(codePage, False)
        If encoding Is Nothing OrElse length = 0 Then Return result
        Dim skip As Integer = 0
        Dim preamble As Byte() = TextEncodingDetector.ByteOrderMarkOf(codePage, encoding)
        If preamble.Length > 0 AndAlso length >= preamble.Length Then
            Dim same As Boolean = True
            For index As Integer = 0 To preamble.Length - 1
                If bytes(index) <> preamble(index) Then same = False : Exit For
            Next
            If same Then
                skip = preamble.Length
                result.HasBom = True
            End If
        End If
        Dim text As String = encoding.GetString(bytes, skip, length - skip)
        If isTruncated AndAlso text.Length > 0 AndAlso text(text.Length - 1) = ChrW(&HFFFD) Then text = text.Substring(0, text.Length - 1)

        Dim lines As New List(Of String)()
        Dim builder As New StringBuilder()
        Dim position As Integer = 0
        While position < text.Length AndAlso lines.Count < PreviewLineCount
            Dim ch As Char = text(position)
            If ch = ControlChars.Cr OrElse ch = ControlChars.Lf Then
                If ch = ControlChars.Cr AndAlso position + 1 < text.Length AndAlso text(position + 1) = ControlChars.Lf Then position += 1
                lines.Add(builder.ToString())
                builder.Clear()
            ElseIf ch = ChrW(&HFFFD) OrElse (Char.IsControl(ch) AndAlso ch <> ControlChars.Tab) Then
                result.UnreadableCount += 1
                If builder.Length < PreviewColumnLimit Then builder.Append(ChrW(&HFFFD))
            ElseIf builder.Length < PreviewColumnLimit Then
                ' Never keep half of a surrogate pair at the column limit.
                If Not (Char.IsHighSurrogate(ch) AndAlso builder.Length = PreviewColumnLimit - 1) Then builder.Append(ch)
            End If
            position += 1
        End While
        If lines.Count < PreviewLineCount AndAlso builder.Length > 0 Then lines.Add(builder.ToString())
        ' The count covers the whole probe, not only the lines shown.
        While position < text.Length
            Dim ch As Char = text(position)
            If ch = ChrW(&HFFFD) OrElse (Char.IsControl(ch) AndAlso ch <> ControlChars.Tab AndAlso ch <> ControlChars.Cr AndAlso ch <> ControlChars.Lf) Then
                result.UnreadableCount += 1
            End If
            position += 1
        End While
        result.Lines = lines.ToArray()
        Return result
    End Function
End Module

''' <summary>
''' The encodings a user chose for documents (section 6.1 tier 1, <c>TextRememberEncoding</c>): one line per
''' document identity, the newest last, at most <see cref="EntryLimit"/>. A file whose size or write time changed is
''' a different identity, so its old choice no longer applies. The file is plain text in the user's local
''' application data; an unreadable or oversized file is left untouched and the memory starts empty.
''' </summary>
Public NotInheritable Class TextEncodingMemory
    Public Const EntryLimit As Integer = 500
    Public Const MaxFileBytes As Integer = 1048576
    Private Const Header As String = "fms-text-encodings 1"

    Private Shared ReadOnly lazyCurrent As New Lazy(Of TextEncodingMemory)(
        Function()
            Dim memory As New TextEncodingMemory(DefaultPath())
            memory.Load()
            Return memory
        End Function, LazyThreadSafetyMode.ExecutionAndPublication)

    Private NotInheritable Class Entry
        Public CodePage As Integer
        Public Identity As DocumentIdentity
    End Class

    Private ReadOnly statePath As String
    Private ReadOnly gate As New Object()
    Private ReadOnly entries As New List(Of Entry)()
    Private locked As Boolean
    Private diagnosticText As String = ""

    Public Sub New(path As String)
        statePath = If(path, "")
    End Sub

    Public Shared Function DefaultPath() As String
        Return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "SZA", "FastMediaSorter", "documents", "text-encodings-v1.txt")
    End Function

    ''' <summary>The viewer's memory: created and loaded once, on first use.</summary>
    Public Shared ReadOnly Property Current As TextEncodingMemory
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
            diagnosticText = ""
            Dim lines As String()
            Try
                If statePath.Length = 0 OrElse Not File.Exists(statePath) Then Return
                If New FileInfo(statePath).Length > MaxFileBytes Then
                    locked = True
                    diagnosticText = "too-large: the encoding memory file is over its size limit and was kept unchanged"
                    Return
                End If
                lines = File.ReadAllLines(statePath, New UTF8Encoding(False))
            Catch ex As Exception
                locked = True
                diagnosticText = "unreadable: the encoding memory file could not be read and was kept unchanged"
                Return
            End Try
            If lines.Length = 0 OrElse lines(0) <> Header Then
                locked = True
                diagnosticText = "unknown-format: the encoding memory file has another format and was kept unchanged"
                Return
            End If
            For index As Integer = 1 To lines.Length - 1
                Dim parsed As Entry = ParseLine(lines(index))
                If parsed Is Nothing Then Continue For
                entries.RemoveAll(Function(item) SameDocument(item.Identity, parsed.Identity))
                entries.Add(parsed)
                ' Trimmed line by line: a damaged file of many short lines would otherwise make the scan above quadratic.
                If entries.Count > EntryLimit Then entries.RemoveAt(0)
            Next
        End SyncLock
    End Sub

    ''' <summary>The code page chosen for exactly this identity, or 0.</summary>
    Public Function Lookup(identity As DocumentIdentity) As Integer
        If Not Usable(identity) Then Return 0
        SyncLock gate
            For index As Integer = entries.Count - 1 To 0 Step -1
                If entries(index).Identity.Matches(identity) Then Return entries(index).CodePage
            Next
        End SyncLock
        Return 0
    End Function

    ''' <summary>Remembers a choice and writes the file at once; the document's older choice is replaced.</summary>
    Public Sub Remember(identity As DocumentIdentity, codePage As Integer)
        If Not Usable(identity) OrElse Not TextEncodingChoice.IsManualCodePage(codePage) Then Return
        SyncLock gate
            If locked Then Return
            entries.RemoveAll(Function(item) SameDocument(item.Identity, identity))
            entries.Add(New Entry With {.CodePage = codePage, .Identity = New DocumentIdentity With {
                .Path = identity.Path, .LastWriteUtcTicks = identity.LastWriteUtcTicks, .Length = identity.Length,
                .Provenance = If(identity.Provenance, "")}})
            If entries.Count > EntryLimit Then entries.RemoveRange(0, entries.Count - EntryLimit)
            WriteFile()
        End SyncLock
    End Sub

    ''' <summary>Drops the choice of this document (every version of it), so detection decides again.</summary>
    Public Sub Forget(identity As DocumentIdentity)
        If Not Usable(identity) Then Return
        SyncLock gate
            If locked Then Return
            If entries.RemoveAll(Function(item) SameDocument(item.Identity, identity)) > 0 Then WriteFile()
        End SyncLock
    End Sub

    Public Sub Clear()
        SyncLock gate
            entries.Clear()
            locked = False
            WriteFile()
        End SyncLock
    End Sub

    ' A tab, CR or LF would break the line format; Windows paths cannot carry them, so such an identity is refused.
    Private Shared Function Usable(identity As DocumentIdentity) As Boolean
        If identity Is Nothing OrElse String.IsNullOrWhiteSpace(identity.Path) Then Return False
        Dim forbidden As Char() = {ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}
        Return identity.Path.IndexOfAny(forbidden) < 0 AndAlso If(identity.Provenance, "").IndexOfAny(forbidden) < 0
    End Function

    Private Shared Function SameDocument(a As DocumentIdentity, b As DocumentIdentity) As Boolean
        Return String.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) AndAlso
            String.Equals(a.Provenance, If(b.Provenance, ""), StringComparison.Ordinal)
    End Function

    ' codePage TAB length TAB lastWriteUtcTicks TAB provenance TAB path
    Private Shared Function ParseLine(line As String) As Entry
        Dim parts As String() = line.Split(ControlChars.Tab)
        If parts.Length <> 5 Then Return Nothing
        Dim codePage As Integer
        Dim length As Long
        Dim ticks As Long
        If Not Integer.TryParse(parts(0), NumberStyles.None, CultureInfo.InvariantCulture, codePage) OrElse
           Not TextEncodingChoice.IsManualCodePage(codePage) OrElse
           Not Long.TryParse(parts(1), NumberStyles.None, CultureInfo.InvariantCulture, length) OrElse
           Not Long.TryParse(parts(2), NumberStyles.None, CultureInfo.InvariantCulture, ticks) OrElse
           String.IsNullOrWhiteSpace(parts(4)) Then
            Return Nothing
        End If
        Return New Entry With {.CodePage = codePage, .Identity = New DocumentIdentity With {
            .Length = length, .LastWriteUtcTicks = ticks, .Provenance = parts(3), .Path = parts(4)}}
    End Function

    Private Sub WriteFile()
        Dim temp As String = Nothing
        Try
            If statePath.Length = 0 Then Return
            Dim builder As New StringBuilder()
            builder.Append(Header).Append(ControlChars.Lf)
            For Each item As Entry In entries
                builder.Append(item.CodePage.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                    .Append(item.Identity.Length.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                    .Append(item.Identity.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture)).Append(ControlChars.Tab) _
                    .Append(item.Identity.Provenance).Append(ControlChars.Tab) _
                    .Append(item.Identity.Path).Append(ControlChars.Lf)
            Next
            Dim folder As String = System.IO.Path.GetDirectoryName(statePath)
            If Not String.IsNullOrEmpty(folder) Then Directory.CreateDirectory(folder)
            temp = statePath & "." & Guid.NewGuid().ToString("N") & ".tmp"
            File.WriteAllText(temp, builder.ToString(), New UTF8Encoding(False))
            If File.Exists(statePath) Then
                File.Replace(temp, statePath, Nothing)
            Else
                File.Move(temp, statePath)
            End If
            temp = Nothing
            diagnosticText = ""
        Catch ex As Exception
            diagnosticText = "save-failed: the encoding memory could not be written and stays in memory only"
        Finally
            If temp IsNot Nothing Then
                Try
                    File.Delete(temp)
                Catch ex As Exception
                    ' A leftover temp file is harmless; the next save uses a new name.
                End Try
            End If
        End Try
    End Sub
End Class
#End If
