Option Strict On

Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Pluggable translation backend. Implementations must:
'''   * be awaitable / non-blocking (OCR + HTTP run off the UI thread);
'''   * honour the CancellationToken (navigation cancels stale work);
'''   * never throw for an unreachable backend — Probe returns False instead.
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
