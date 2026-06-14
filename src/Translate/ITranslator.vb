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
    ''' Translate every input segment. Returns a list of the same length and
    ''' order. On a per-segment failure the original text is returned for that
    ''' slot so the overlay still shows something.
    ''' </summary>
    Function TranslateAsync(texts As List(Of String), sourceLang As String, targetLang As String, ct As CancellationToken) As Task(Of List(Of String))
End Interface

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
            Case "auto", "" : Return "the source language"
            Case Else : Return code
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
            Case "ar", "ara", "fa", "fas"
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

    Friend Function SymbolRatio(text As String) As Double
        Dim nonWhitespace As Integer = 0
        Dim noisy As Integer = 0

        For Each ch As Char In If(text, "")
            If Char.IsWhiteSpace(ch) Then Continue For
            nonWhitespace += 1
            If Char.IsLetterOrDigit(ch) Then Continue For
            If ".,!?;:'""-()[]{}«»“”„".IndexOf(ch) >= 0 Then Continue For
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
