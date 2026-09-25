Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Pluggable translation backend. Implementations must:
'''   * be awaitable / non-blocking (OCR + HTTP run off the UI thread);
'''   * honour the CancellationToken (navigation cancels stale work);
'''   * never throw for an unreachable backend - Probe returns False instead.
''' </summary>
Public Interface ITranslator
    ReadOnly Property Name As String

    ''' <summary>Cheap availability check before attempting translation.</summary>
    Function ProbeAsync(ct As CancellationToken) As Task(Of Boolean)

    ''' <summary>
    ''' Translate every input segment. Returns one entry per input, in order. On a
    ''' per-segment failure the original text is returned for that slot so the overlay
    ''' still shows something - and the slot says it FAILED, which is what keeps a failure
    ''' out of the cache (audit 051 E-02: it used to be indistinguishable from a
    ''' translation and was cached as one, forever). An expired HTTP timeout is a failure
    ''' of the whole batch, never an OperationCanceledException: only the caller's token
    ''' may end a translation silently (E-04).
    ''' </summary>
    Function TranslateAsync(texts As List(Of String), sourceLang As String, targetLang As String, ct As CancellationToken) As Task(Of TranslationBatch)
End Interface

''' <summary>Why a batch translated nothing at all, when there is one reason for all of it.</summary>
Public Enum TranslationFailure
    None = 0
    ''' <summary>The backend did not answer within the HTTP timeout.</summary>
    Timeout
    ''' <summary>The backend cannot translate into (or out of) the requested language.</summary>
    UnsupportedLanguage
    ''' <summary>Anything else: no model, an HTTP error, a reply that could not be read.</summary>
    Other
End Enum

''' <summary>
''' What a translator returns: the text for every segment, whether that text really is a
''' translation, and - for a batch that failed as a whole - why.
''' </summary>
Public NotInheritable Class TranslationBatch
    Public ReadOnly Property Texts As New List(Of String)
    Public ReadOnly Property Translated As New List(Of Boolean)
    Public Property Failure As TranslationFailure = TranslationFailure.None
    ''' <summary>The model or engine that actually answered, when the backend can say.
    ''' Ollama may fall back from the configured model to another installed one.</summary>
    Public Property ModelUsed As String = ""

    Public Sub Add(text As String, ok As Boolean)
        Texts.Add(If(text, ""))
        Translated.Add(ok)
    End Sub

    ''' <summary>Every segment returned as its source, marked failed.</summary>
    Public Shared Function AllFailed(sources As IEnumerable(Of String), failure As TranslationFailure) As TranslationBatch
        Dim b As New TranslationBatch With {.Failure = failure}
        For Each s As String In sources
            b.Add(s, False)
        Next
        Return b
    End Function

    Public ReadOnly Property FailedCount As Integer
        Get
            Return Translated.Where(Function(t) Not t).Count()
        End Get
    End Property
End Class

''' <summary>Maps language codes to human names for LLM prompts.</summary>
Public Module TranslateLang

    Public Function DisplayName(code As String) As String
        If String.IsNullOrWhiteSpace(code) Then Return "English"
        Select Case code.Trim().ToLowerInvariant()
            Case "en", "eng" : Return "English"
            Case "ru", "rus" : Return "Russian"
            Case "uk", "ukr" : Return "Ukrainian"
            Case "de", "deu", "ger" : Return "German"
            Case "fr", "fra", "fre" : Return "French"
            Case "es", "spa" : Return "Spanish"
            Case "it", "ita" : Return "Italian"
            Case "pt", "por" : Return "Portuguese"
            Case "nl", "nld" : Return "Dutch"
            Case "pl", "pol" : Return "Polish"
            Case "cs", "ces" : Return "Czech"
            Case "sk", "slk" : Return "Slovak"
            Case "sv", "swe" : Return "Swedish"
            Case "no", "nor" : Return "Norwegian"
            Case "da", "dan" : Return "Danish"
            Case "fi", "fin" : Return "Finnish"
            Case "tr", "tur" : Return "Turkish"
            Case "el", "ell" : Return "Greek"
            Case "bg", "bul" : Return "Bulgarian"
            Case "ro", "ron" : Return "Romanian"
            Case "hu", "hun" : Return "Hungarian"
            Case "ja", "jpn" : Return "Japanese"
            Case "ko", "kor" : Return "Korean"
            Case "zh", "chi_sim", "chi-sim", "zh-cn" : Return "Chinese (Simplified)"
            Case "chi_tra", "chi-tra", "zh-tw" : Return "Chinese (Traditional)"
            Case "ar", "ara" : Return "Arabic"
            Case "he", "heb" : Return "Hebrew"
            Case "hi", "hin" : Return "Hindi"
            Case "th", "tha" : Return "Thai"
            Case "vi", "vie" : Return "Vietnamese"
            Case "id", "ind" : Return "Indonesian"
            Case "fa", "fas" : Return "Persian"
            ' Audit 051 E-06: all three are offered (be in the pickers, bn/ur as the default
            ' target of a Bengali or Urdu interface) and fell through to Case Else, so the
            ' prompt read "Translate .. into be" - an English word, not a language.
            Case "be", "bel" : Return "Belarusian"
            Case "bn", "ben" : Return "Bengali"
            Case "ur", "urd" : Return "Urdu"
            Case "auto", "" : Return "the source language"
            Case Else : Return code
        End Select
    End Function

    ''' <summary>
    ''' True for a language written right to left. The overlay's paragraph direction follows
    ''' the TRANSLATION, not the interface (audit 051 E-11): an Arabic translation shown in a
    ''' Russian interface still needs its "!" on the left.
    ''' </summary>
    Public Function IsRightToLeft(code As String) As Boolean
        Select Case If(code, "").Trim().ToLowerInvariant()
            Case "ar", "ara", "fa", "fas", "he", "heb", "ur", "urd" : Return True
            Case Else : Return False
        End Select
    End Function

    ''' <summary>
    ''' The code LibreTranslate (Argos) uses for one of ours, or "" when it has none - the
    ''' caller then refuses up front instead of sending a code the server answers 400 to
    ''' (audit 051 E-13). Unknown codes pass through lower-cased: servers load different
    ''' language sets, so only a code known to be missing everywhere is refused here.
    ''' </summary>
    Public Function LibreTranslateCode(code As String) As String
        Dim c As String = If(code, "").Trim().ToLowerInvariant()
        Select Case c
            Case "", "auto" : Return "auto"
            Case "eng" : Return "en"
            Case "rus" : Return "ru"
            Case "ukr" : Return "uk"
            Case "deu", "ger" : Return "de"
            Case "fra", "fre" : Return "fr"
            Case "spa" : Return "es"
            Case "ita" : Return "it"
            Case "jpn" : Return "ja"
            Case "kor" : Return "ko"
            Case "chi_sim", "chi-sim", "zh-cn" : Return "zh"
            Case "zh-tw", "chi_tra", "chi-tra" : Return "zt"
            Case "no", "nor", "nb" : Return "nb"
            Case "be", "bel" : Return ""
            Case Else : Return c
        End Select
    End Function

End Module

Friend Enum TextScriptFamily
    Unknown = 0
    Latin
    Cyrillic
    Greek
    Arabic
    Hebrew
    Devanagari
    Thai
    Cjk
End Enum

Friend Module TextScriptHeuristics

    Friend Function ExpectedScriptForLanguage(code As String) As TextScriptFamily
        Select Case If(code, "").Trim().ToLowerInvariant()
            Case "en", "eng",
                 "de", "deu", "ger",
                 "fr", "fra", "fre",
                 "es", "spa",
                 "it", "ita",
                 "pt", "por",
                 "nl", "nld",
                 "pl", "pol",
                 "cs", "ces",
                 "sk", "slk",
                 "sv", "swe",
                 "no", "nor",
                 "da", "dan",
                 "fi", "fin",
                 "tr", "tur",
                 "ro", "ron",
                 "hu", "hun",
                 "id", "ind",
                 "vi", "vie"
                    Return TextScriptFamily.Latin
            Case "ru", "rus",
                 "uk", "ukr",
                 "be", "bel",
                 "bg", "bul",
                 "mk", "mkd"
                    Return TextScriptFamily.Cyrillic
            Case "el", "ell"
                Return TextScriptFamily.Greek
            Case "ar", "ara", "fa", "fas", "ur", "urd"
                Return TextScriptFamily.Arabic
            Case "he", "heb"
                Return TextScriptFamily.Hebrew
            Case "hi", "hin"
                Return TextScriptFamily.Devanagari
            Case "th", "tha"
                Return TextScriptFamily.Thai
            Case "zh", "zh-cn", "zh-tw", "chi_sim", "chi-sim", "chi_tra", "chi-tra",
                 "ja", "jpn",
                 "ko", "kor"
                Return TextScriptFamily.Cjk
            Case Else
                Return TextScriptFamily.Unknown
        End Select
    End Function

    ''' <summary>
    ''' Whether a run of text lays out right to left: its letters are mostly Arabic or Hebrew
    ''' script. Text with no letters at all (digits, punctuation) takes the direction of the
    ''' language it was translated into.
    ''' </summary>
    Friend Function IsRightToLeftText(text As String, fallbackLanguage As String) As Boolean
        Dim dominant As TextScriptFamily = TextScriptFamily.Unknown
        Dim share As Double = 0.0
        Dim letters As Integer = 0
        Dim mixed As Integer = 0
        If Not TryGetDominantScript(text, dominant, share, letters, mixed) Then Return TranslateLang.IsRightToLeft(fallbackLanguage)
        Return dominant = TextScriptFamily.Arabic OrElse dominant = TextScriptFamily.Hebrew
    End Function

    Friend Function TryGetDominantScript(text As String,
                                         ByRef dominant As TextScriptFamily,
                                         ByRef dominantShare As Double,
                                         ByRef letterCount As Integer,
                                         ByRef mixedLetters As Integer) As Boolean
        Dim counts As New Dictionary(Of TextScriptFamily, Integer)
        letterCount = 0
        mixedLetters = 0
        dominant = TextScriptFamily.Unknown
        dominantShare = 0.0

        For Each ch As Char In If(text, "")
            If Not Char.IsLetter(ch) Then Continue For
            letterCount += 1
            Dim script As TextScriptFamily = ClassifyScript(ch)
            If script = TextScriptFamily.Unknown Then Continue For
            If Not counts.ContainsKey(script) Then counts(script) = 0
            counts(script) += 1
        Next

        If letterCount = 0 OrElse counts.Count = 0 Then Return False

        Dim bestCount As Integer = 0
        For Each kv As KeyValuePair(Of TextScriptFamily, Integer) In counts
            If kv.Value > bestCount Then
                bestCount = kv.Value
                dominant = kv.Key
            End If
        Next

        mixedLetters = Math.Max(0, letterCount - bestCount)
        dominantShare = bestCount / CDbl(letterCount)
        Return True
    End Function

    ''' <summary>
    ''' Punctuation that is part of ordinary text, not recognition noise. Built with ChrW,
    ''' never typed: the VB compiler reads the curly double quotes U+201C/U+201D as string
    ''' delimiters, so the literal this replaced held a doubled "" where the author wrote
    ''' “” and the curly quotes were never in the set (audit 051 E-10). The ellipsis and the
    ''' dashes are added because Russian dialogue is punctuated with them.
    ''' </summary>
    Private ReadOnly OrdinaryPunctuation As String =
        ".,!?;:'""-()[]{}«»" &
        ChrW(&H201C) & ChrW(&H201D) & ChrW(&H201E) &   ' curly double quotes: left, right, low
        ChrW(&H2018) & ChrW(&H2019) &                   ' curly single quotes
        ChrW(&H2026) & ChrW(&H2014) & ChrW(&H2013)      ' ellipsis, em dash, en dash

    Friend Function SymbolRatio(text As String) As Double
        Dim nonWhitespace As Integer = 0
        Dim noisy As Integer = 0

        For Each ch As Char In If(text, "")
            If Char.IsWhiteSpace(ch) Then Continue For
            nonWhitespace += 1
            If Char.IsLetterOrDigit(ch) Then Continue For
            If OrdinaryPunctuation.IndexOf(ch) >= 0 Then Continue For
            noisy += 1
        Next

        If nonWhitespace = 0 Then Return 0.0
        Return noisy / CDbl(nonWhitespace)
    End Function

    Private Function ClassifyScript(ch As Char) As TextScriptFamily
        Dim code As Integer = Convert.ToInt32(ch)

        If (code >= &H41 AndAlso code <= &H5A) OrElse
           (code >= &H61 AndAlso code <= &H7A) OrElse
           (code >= &HC0 AndAlso code <= &H24F) Then
            Return TextScriptFamily.Latin
        End If

        If (code >= &H400 AndAlso code <= &H52F) OrElse
           (code >= &H2DE0 AndAlso code <= &H2DFF) OrElse
           (code >= &HA640 AndAlso code <= &HA69F) Then
            Return TextScriptFamily.Cyrillic
        End If

        If code >= &H370 AndAlso code <= &H3FF Then Return TextScriptFamily.Greek
        If (code >= &H600 AndAlso code <= &H6FF) OrElse (code >= &H750 AndAlso code <= &H77F) Then Return TextScriptFamily.Arabic
        If code >= &H590 AndAlso code <= &H5FF Then Return TextScriptFamily.Hebrew
        If code >= &H900 AndAlso code <= &H97F Then Return TextScriptFamily.Devanagari
        If code >= &HE00 AndAlso code <= &HE7F Then Return TextScriptFamily.Thai

        If (code >= &H3040 AndAlso code <= &H30FF) OrElse
           (code >= &H3400 AndAlso code <= &H9FFF) OrElse
           (code >= &HAC00 AndAlso code <= &HD7A3) OrElse
           (code >= &HF900 AndAlso code <= &HFAFF) Then
            Return TextScriptFamily.Cjk
        End If

        Return TextScriptFamily.Unknown
    End Function

End Module
