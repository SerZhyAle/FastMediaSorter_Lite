#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic

''' <summary>Specification 276 section 7.2: the document command line of doc-html-translate, built from the
''' producer's documented flags only. The image route keeps its own builder (<c>DocHtmlTranslate.BuildArguments</c>,
''' OCR-INVOCATION 1.1) and is not touched. The list is handed to <c>ProcessStartInfo.ArgumentList</c>, so no value
''' is quoted by hand.</summary>
Public NotInheritable Class DocHtmlJobArguments
    ''' <summary>Convert or recognize, and let the browser translate the page (the default flow, section 4).</summary>
    Public Const ModeBrowser As String = "browser"
    ''' <summary>Convert or recognize only: <c>-notranslate</c>.</summary>
    Public Const ModeConvert As String = "convert"
    ''' <summary>The producer translates with its configured local Ollama service: <c>-ollama</c>.</summary>
    Public Const ModeLocal As String = "local"
    ''' <summary>The producer translates with its configured paid Google service: <c>-google</c> with a positive finite
    ''' <c>-max-cost</c>, which is both the guard and the approval (OCR-INVOCATION 1.2 item G). Without a cap this mode is
    ''' never built: the producer's own window carries its approval question instead.</summary>
    Public Const ModePaid As String = "paid"
    ''' <summary>The highest per-run cap, in US dollars, the viewer passes on.</summary>
    Public Const MaxPaidCostLimit As Double = 1000

    Private Sub New()
    End Sub

    Public Shared Function IsKnownMode(mode As String) As Boolean
        Return String.Equals(mode, ModeBrowser, StringComparison.Ordinal) OrElse
               String.Equals(mode, ModeConvert, StringComparison.Ordinal) OrElse
               String.Equals(mode, ModeLocal, StringComparison.Ordinal) OrElse
               String.Equals(mode, ModePaid, StringComparison.Ordinal)
    End Function

    ''' <summary>The arguments for one job, or Nothing when the request cannot be expressed safely: an unknown
    ''' mode, or an input or output root that is not an absolute local or UNC path. <paramref name="noOpen"/> sends
    ''' <c>-noopen</c> for a caller that opens the verified result itself (OCR-INVOCATION 1.2 item B rule 1, the
    ''' owned jobs of T-3); without it the converter opens its own output, the T-2 handoff.
    ''' The input goes last, so it can never be read as a flag.</summary>
    Public Shared Function Build(inputPath As String, outputRoot As String, mode As String, force As Boolean,
                                 Optional noOpen As Boolean = False, Optional options As DocHtmlJobOptions = Nothing) As IReadOnlyList(Of String)
        If Not IsKnownMode(mode) Then Return Nothing
        If Not IsAbsolutePath(inputPath) OrElse Not IsAbsolutePath(outputRoot) Then Return Nothing
        Dim o = If(options, New DocHtmlJobOptions())
        Dim local = String.Equals(mode, ModeLocal, StringComparison.Ordinal)
        Dim paid = String.Equals(mode, ModePaid, StringComparison.Ordinal)
        ' Section 5: zero is the producer's "no limit" and is never serialized; an unset cap launches no paid work.
        If paid AndAlso Not IsAcceptableCostLimit(o.PaidCostLimit) Then Return Nothing
        Dim args As New List(Of String) From {"-folder", outputRoot}
        If String.Equals(mode, ModeConvert, StringComparison.Ordinal) Then
            args.Add("-notranslate")
        ElseIf local Then
            args.Add("-ollama")
            If Not AddValue(args, "-ollama-model", o.LocalModel, AddressOf IsModelName) Then Return Nothing
            ' The languages shape the page only when the converter translates; it drops them otherwise (2.3).
            If Not AddValue(args, "-src", o.SourceLanguage, AddressOf IsLanguageList) Then Return Nothing
            If Not AddValue(args, "-dst", o.TargetLanguage, AddressOf IsLanguageList) Then Return Nothing
        ElseIf paid Then
            args.Add("-google")
            args.Add("-max-cost")
            args.Add(o.PaidCostLimit.ToString("0.##", Globalization.CultureInfo.InvariantCulture))
            If Not AddValue(args, "-src", o.SourceLanguage, AddressOf IsLanguageList) Then Return Nothing
            If Not AddValue(args, "-dst", o.TargetLanguage, AddressOf IsLanguageList) Then Return Nothing
        End If
        ' 7.2: -ocr only when recognition is forced; -ocr-lang only for an explicit choice, never an assumed ladder.
        If o.OcrForce Then args.Add("-ocr")
        If Not AddValue(args, "-ocr-lang", o.OcrLanguages, AddressOf IsLanguageList) Then Return Nothing
        If force Then args.Add("-force")
        If noOpen Then args.Add("-noopen")
        args.Add(inputPath)
        Return args
    End Function

    ''' <summary>Section 5 DocumentPaidCostLimit: a positive finite per-run cap of at least one cent and at most
    ''' <see cref="MaxPaidCostLimit"/>. Zero, a negative, NaN or an infinity is refused - zero would mean "no limit".</summary>
    Public Shared Function IsAcceptableCostLimit(value As Double) As Boolean
        Return Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value) AndAlso value >= 0.01 AndAlso value <= MaxPaidCostLimit
    End Function

    ''' <summary>Adds <c>flag value</c> for a chosen value; False when the value is chosen but not acceptable.</summary>
    Private Shared Function AddValue(args As List(Of String), flag As String, value As String, accept As Func(Of String, Boolean)) As Boolean
        If String.IsNullOrEmpty(value) Then Return True
        If Not accept(value) Then Return False
        args.Add(flag)
        args.Add(value)
        Return True
    End Function

    ''' <summary>A language code or a <c>+</c>/<c>,</c> list of them (<c>eng</c>, <c>rus+eng</c>, <c>pt-BR</c>): letters,
    ''' digits, <c>-</c>, <c>_</c>, <c>+</c>, <c>,</c>; never starting with <c>-</c>, at most 64 characters.</summary>
    Public Shared Function IsLanguageList(value As String) As Boolean
        If String.IsNullOrEmpty(value) OrElse value.Length > 64 OrElse value(0) = "-"c Then Return False
        For Each ch As Char In value
            If Not (IsAsciiLetterOrDigit(ch) OrElse ch = "-"c OrElse ch = "_"c OrElse ch = "+"c OrElse ch = ","c) Then Return False
        Next
        Return True
    End Function

    ''' <summary>An Ollama model name (<c>llama3.1:8b</c>, <c>library/qwen2.5</c>): letters, digits, <c>. _ - : /</c>;
    ''' never starting with <c>-</c>, at most 128 characters.</summary>
    Public Shared Function IsModelName(value As String) As Boolean
        If String.IsNullOrEmpty(value) OrElse value.Length > 128 OrElse value(0) = "-"c Then Return False
        For Each ch As Char In value
            If Not (IsAsciiLetterOrDigit(ch) OrElse ch = "."c OrElse ch = "_"c OrElse ch = "-"c OrElse ch = ":"c OrElse ch = "/"c) Then Return False
        Next
        Return True
    End Function

    Private Shared Function IsAsciiLetterOrDigit(ch As Char) As Boolean
        Return (ch >= "a"c AndAlso ch <= "z"c) OrElse (ch >= "A"c AndAlso ch <= "Z"c) OrElse (ch >= "0"c AndAlso ch <= "9"c)
    End Function

    ''' <summary>A drive-rooted (<c>C:\..</c>) or UNC (<c>\\server\share\..</c>) path with no control character.
    ''' A relative path would resolve against the producer's working directory, and such a path can never start
    ''' with <c>-</c>.</summary>
    Public Shared Function IsAbsolutePath(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) OrElse path.Length < 3 OrElse path.Length > 32767 Then Return False
        For Each ch As Char In path
            If Char.IsControl(ch) Then Return False
        Next
        Dim driveRooted As Boolean = Char.IsLetter(path(0)) AndAlso path(1) = ":"c AndAlso (path(2) = "\"c OrElse path(2) = "/"c)
        Dim unc As Boolean = path.StartsWith("\\", StringComparison.Ordinal) AndAlso path.Length > 2 AndAlso path(2) <> "\"c AndAlso path(2) <> "?"c AndAlso path(2) <> "."c
        Return driveRooted OrElse unc
    End Function
End Class
#End If
