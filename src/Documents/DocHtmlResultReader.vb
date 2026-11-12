#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text.Json

''' <summary>What one job folder holds after the converter has ended (specification 276 section 7.3,
''' OCR-INVOCATION 1.2 item C).</summary>
Public Enum DocHtmlResultState
    ''' <summary>No child directory carrying a converter record: nothing was produced.</summary>
    Missing
    ''' <summary>A record that cannot be parsed, is not the converter's, or names another source.</summary>
    Foreign
    ''' <summary>The record's schemaVersion is above the one this reader knows (item C rule 3).</summary>
    NewerSchema
    ''' <summary>The run never wrote <c>complete</c>: incomplete, never advertised as finished.</summary>
    Incomplete
    ''' <summary>Finished, but the entry is missing, escapes the folder, crosses a reparse point or the tree is too big.</summary>
    Unsafe
    ''' <summary>Finished and the entry verified.</summary>
    Completed
End Enum

''' <summary>The honest outcome of a job: the record and the exit code together (OCR-INVOCATION 1.2 item D), never
''' "exit 0" alone.</summary>
Public Enum DocHtmlJobOutcome
    Converted
    FullyTranslated
    PartiallyTranslated
    TranslationSkipped
    TranslationUnavailable
    Incomplete
    BadArguments
    IoError
    Unreadable
    Cancelled
    TimedOut
    NewerSchema
    Unsafe
    Failed
End Enum

''' <summary>The verified reading of one job folder.</summary>
Public NotInheritable Class DocHtmlResult
    Public Property State As DocHtmlResultState = DocHtmlResultState.Missing
    ''' <summary>The output directory (the single qualifying child of the job folder), or "".</summary>
    Public Property OutputDirectory As String = ""
    ''' <summary>The verified <c>index.html</c>, set only for <see cref="DocHtmlResultState.Completed"/>.</summary>
    Public Property EntryPath As String = ""
    Public Property SchemaVersion As Integer = 1
    Public Property ToolVersion As String = ""
    ''' <summary><c>none</c>, <c>google</c> or <c>ollama</c>; "" when the record has no completion.</summary>
    Public Property Engine As String = ""
    ''' <summary><c>none</c>, <c>full</c> or <c>partial</c>; "" when the record has no completion.</summary>
    Public Property Translation As String = ""
    ''' <summary>Recognition was requested (<c>options.ocr</c>) or forced (<c>ocrForced</c>); never "text was found".</summary>
    Public Property RecognitionRequested As Boolean
    Public Property CompletedUtc As DateTimeOffset?
End Class

''' <summary>Specification 276 section 7.3 on OCR-INVOCATION 1.2 items C and D: finds the output directory in a
''' fresh per-job <c>-folder</c> by enumeration (never by recomputing the converter's naming), reads
''' <c>.doc-html-translate.json</c> tolerantly (unknown members ignored, absent schemaVersion = 1, a higher one = not
''' completed), and re-checks the entry as any file the viewer did not write: it exists, it lies inside the output
''' directory, no reparse point on the way, and the tree is bounded. The record is evidence, not proof.</summary>
Public NotInheritable Class DocHtmlResultReader
    Public Const RecordFileName As String = ".doc-html-translate.json"
    Public Const EntryFileName As String = "index.html"
    Public Const ToolName As String = "doc-html-translate"
    ''' <summary>The record schema this reader knows (OCR-INVOCATION 1.2 item C).</summary>
    Public Const KnownSchemaVersion As Integer = 1
    ''' <summary>A record is a few hundred bytes; anything above this is not the converter's.</summary>
    Public Const MaxRecordBytes As Integer = 64 * 1024
    ''' <summary>Upper bounds of the output tree walked before it is opened (section 6: bounded output).</summary>
    Public Const MaxTreeFiles As Integer = 200000
    Public Const MaxTreeBytes As Long = 8L * 1024 * 1024 * 1024

    Private Sub New()
    End Sub

    ''' <summary>Reads the folder of one job. Never throws: an unreadable folder is <see cref="DocHtmlResultState.Missing"/>.</summary>
    Public Shared Function Read(jobRoot As String, expectedSource As String) As DocHtmlResult
        Dim result As New DocHtmlResult()
        Try
            If String.IsNullOrEmpty(jobRoot) OrElse Not Directory.Exists(jobRoot) Then Return result
            ' Item C: in a fresh, empty -folder a run creates at most one direct child, so the first child that
            ' carries a record is the output; a child without one is not the converter's.
            Dim candidate As String = ""
            For Each child In Directory.GetDirectories(jobRoot)
                If IsReparsePoint(child) Then Continue For
                If File.Exists(Path.Combine(child, RecordFileName)) Then
                    candidate = child
                    Exit For
                End If
            Next
            If candidate.Length = 0 Then Return result
            result.OutputDirectory = candidate

            Dim recordPath = Path.Combine(candidate, RecordFileName)
            If New FileInfo(recordPath).Length > MaxRecordBytes Then
                result.State = DocHtmlResultState.Foreign
                Return result
            End If
            ParseRecord(File.ReadAllText(recordPath), expectedSource, result)
            If result.State <> DocHtmlResultState.Completed Then Return result

            Dim entry = Path.Combine(candidate, EntryFileName)
            If Not IsSafeTree(candidate) OrElse Not File.Exists(entry) OrElse Not IsInside(candidate, entry) Then
                result.State = DocHtmlResultState.Unsafe
                Return result
            End If
            result.EntryPath = Path.GetFullPath(entry)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
                                   TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is ArgumentException OrElse
                                   TypeOf ex Is NotSupportedException
            If result.State = DocHtmlResultState.Completed Then
                result.State = DocHtmlResultState.Unsafe
            ElseIf result.OutputDirectory.Length = 0 Then
                result.State = DocHtmlResultState.Missing
            Else
                result.State = DocHtmlResultState.Foreign
            End If
            result.EntryPath = ""
        End Try
        Return result
    End Function

    ''' <summary>The pure half of <see cref="Read"/>: fills <paramref name="result"/> from the record's text.
    ''' <c>Completed</c> here means only "the record says finished"; the caller still checks the files.</summary>
    Public Shared Sub ParseRecord(json As String, expectedSource As String, result As DocHtmlResult)
        result.State = DocHtmlResultState.Foreign
        If String.IsNullOrWhiteSpace(json) Then Return
        Try
            Using doc = JsonDocument.Parse(json)
                Dim root = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return
                Dim schema As Integer = 1
                Dim schemaElement As JsonElement
                If root.TryGetProperty("schemaVersion", schemaElement) Then
                    If schemaElement.ValueKind <> JsonValueKind.Number OrElse Not schemaElement.TryGetInt32(schema) OrElse schema < 1 Then Return
                End If
                result.SchemaVersion = schema
                If Not String.Equals(StringMember(root, "tool"), ToolName, StringComparison.Ordinal) Then Return
                If schema > KnownSchemaVersion Then
                    result.State = DocHtmlResultState.NewerSchema
                    Return
                End If
                If Not SamePath(StringMember(root, "source"), expectedSource) Then Return

                Dim complete As JsonElement
                If Not root.TryGetProperty("complete", complete) OrElse complete.ValueKind <> JsonValueKind.Object Then
                    result.State = DocHtmlResultState.Incomplete
                    Return
                End If
                result.ToolVersion = StringMember(complete, "toolVersion")
                result.Translation = StringMember(complete, "translation")
                Dim options As JsonElement
                Dim ocr As Boolean
                If complete.TryGetProperty("options", options) AndAlso options.ValueKind = JsonValueKind.Object Then
                    result.Engine = StringMember(options, "engine")
                    ocr = BoolMember(options, "ocr")
                End If
                If result.Engine.Length = 0 Then result.Engine = "none"
                If result.Translation.Length = 0 Then result.Translation = "none"
                result.RecognitionRequested = ocr OrElse BoolMember(complete, "ocrForced")
                Dim completed As DateTimeOffset
                If DateTimeOffset.TryParse(StringMember(complete, "completed"), CultureInfo.InvariantCulture,
                                           DateTimeStyles.AssumeUniversal, completed) Then
                    result.CompletedUtc = completed.ToUniversalTime()
                End If
                result.State = DocHtmlResultState.Completed
            End Using
        Catch ex As JsonException
            result.State = DocHtmlResultState.Foreign
        End Try
    End Sub

    ''' <summary>OCR-INVOCATION 1.2 item D: the outcome from the reading, the exit code and how the process ended.
    ''' A cancel or a timeout wins over whatever the folder holds, because the viewer ended the run itself.</summary>
    Public Shared Function Outcome(result As DocHtmlResult, exitCode As Integer, cancelled As Boolean, timedOut As Boolean) As DocHtmlJobOutcome
        If timedOut Then Return DocHtmlJobOutcome.TimedOut
        If cancelled Then Return DocHtmlJobOutcome.Cancelled
        If result Is Nothing Then Return DocHtmlJobOutcome.Failed
        Select Case result.State
            Case DocHtmlResultState.NewerSchema : Return DocHtmlJobOutcome.NewerSchema
            Case DocHtmlResultState.Unsafe : Return DocHtmlJobOutcome.Unsafe
            Case DocHtmlResultState.Completed
                If exitCode <> 0 AndAlso exitCode <> 4 Then Return DocHtmlJobOutcome.Failed
                Dim translating = Not String.Equals(result.Engine, "none", StringComparison.Ordinal)
                If Not translating Then Return DocHtmlJobOutcome.Converted
                Select Case result.Translation
                    Case "full" : Return If(exitCode = 0, DocHtmlJobOutcome.FullyTranslated, DocHtmlJobOutcome.PartiallyTranslated)
                    Case "partial" : Return DocHtmlJobOutcome.PartiallyTranslated
                    Case Else : Return If(exitCode = 4, DocHtmlJobOutcome.TranslationUnavailable, DocHtmlJobOutcome.TranslationSkipped)
                End Select
        End Select
        Select Case exitCode
            Case 1 : Return DocHtmlJobOutcome.BadArguments
            Case 2 : Return DocHtmlJobOutcome.IoError
            Case 3 : Return DocHtmlJobOutcome.Unreadable
            Case 130 : Return DocHtmlJobOutcome.Cancelled
            Case 0, 4 : Return DocHtmlJobOutcome.Incomplete
            Case Else : Return DocHtmlJobOutcome.Failed
        End Select
    End Function

    ''' <summary>The outcomes that leave a page worth opening.</summary>
    Public Shared Function HasPage(outcome As DocHtmlJobOutcome) As Boolean
        Select Case outcome
            Case DocHtmlJobOutcome.Converted, DocHtmlJobOutcome.FullyTranslated, DocHtmlJobOutcome.PartiallyTranslated,
                 DocHtmlJobOutcome.TranslationSkipped, DocHtmlJobOutcome.TranslationUnavailable
                Return True
        End Select
        Return False
    End Function

    ''' <summary>Paths compared as Windows compares them: full form, case-insensitive, no trailing separator.</summary>
    Public Shared Function SamePath(a As String, b As String) As Boolean
        If String.IsNullOrWhiteSpace(a) OrElse String.IsNullOrWhiteSpace(b) Then Return False
        Try
            Dim fa = Path.GetFullPath(a).TrimEnd("\"c, "/"c)
            Dim fb = Path.GetFullPath(b).TrimEnd("\"c, "/"c)
            Return String.Equals(fa, fb, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return False
        End Try
    End Function

    ''' <summary>True when <paramref name="candidate"/> resolves strictly inside <paramref name="root"/>.</summary>
    Public Shared Function IsInside(root As String, candidate As String) As Boolean
        If String.IsNullOrWhiteSpace(root) OrElse String.IsNullOrWhiteSpace(candidate) Then Return False
        Try
            Dim fullRoot = Path.GetFullPath(root).TrimEnd("\"c, "/"c) & Path.DirectorySeparatorChar
            Dim full = Path.GetFullPath(candidate)
            Return full.Length > fullRoot.Length AndAlso full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is PathTooLongException
            Return False
        End Try
    End Function

    ''' <summary>The whole output tree: no reparse point at any level (a junction or symlink could point the entry or
    ''' a resource anywhere on the disk), and within the file and byte bounds.</summary>
    Private Shared Function IsSafeTree(root As String) As Boolean
        If IsReparsePoint(root) Then Return False
        Dim files As Integer
        Dim bytes As Long
        Dim pending As New Stack(Of String)()
        pending.Push(root)
        While pending.Count > 0
            Dim folder = pending.Pop()
            For Each entry In New DirectoryInfo(folder).EnumerateFileSystemInfos()
                If (entry.Attributes And FileAttributes.ReparsePoint) <> 0 Then Return False
                If TypeOf entry Is DirectoryInfo Then
                    pending.Push(entry.FullName)
                Else
                    files += 1
                    bytes += DirectCast(entry, FileInfo).Length
                    If files > MaxTreeFiles OrElse bytes > MaxTreeBytes Then Return False
                End If
            Next
        End While
        Return True
    End Function

    Private Shared Function IsReparsePoint(path As String) As Boolean
        Return (File.GetAttributes(path) And FileAttributes.ReparsePoint) <> 0
    End Function

    Private Shared Function StringMember(element As JsonElement, name As String) As String
        Dim value As JsonElement
        If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then Return If(value.GetString(), "")
        Return ""
    End Function

    Private Shared Function BoolMember(element As JsonElement, name As String) As Boolean
        Dim value As JsonElement
        Return element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.True
    End Function
End Class
#End If
