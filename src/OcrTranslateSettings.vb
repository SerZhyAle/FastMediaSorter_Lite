Option Strict On

''' <summary>
''' Persisted configuration for the OCR + Translation overlay. Stored alongside
''' the rest of the app's settings (VB SaveSetting/GetSetting registry store,
''' SZA\FastMediaSorter). The cloud API key is kept DPAPI-encrypted at rest.
''' </summary>
Public Class OcrTranslateSettings

    Public Property Enabled As Boolean = False           ' OcrEnabled
    Public Property AutoMode As Boolean = False          ' OcrAutoMode
    Public Property Engine As String = "tesseract"       ' OcrEngine
    Public Property SourceLang As String = "auto"        ' OcrSourceLang
    Public Property Provider As String = "ollama"        ' TranslateProvider
    Public Property TargetLang As String = "en"          ' TranslateTargetLang
    Public Property Endpoint As String = ""              ' TranslateEndpoint
    Public Property ApiKey As String = ""                ' decrypted; persisted encrypted
    Public Property OverlayVisible As Boolean = True     ' OverlayVisible
    Public Property OverlayOpacity As Integer = 210      ' OverlayOpacity (alpha 0-255)
    Public Property DiskCache As Boolean = True          ' OcrDiskCache
    Public Property OllamaModel As String = "qwen2.5:3b" ' OllamaModel ("" = auto-detect)
    Public Property OcrModelQuality As String = "fast"   ' OcrModelQuality (fast/best)
    Public Property OcrPageMode As String = "auto"       ' OcrPageMode (auto/block/sparse/line/vertical)

    Public Sub Load(defaultTargetLang As String)
        Enabled = ReadBool("OcrEnabled", False)
        AutoMode = ReadBool("OcrAutoMode", False)
        Engine = ReadString("OcrEngine", "tesseract")
        SourceLang = ReadString("OcrSourceLang", "auto")
        Provider = ReadString("TranslateProvider", "ollama")
        TargetLang = ReadString("TranslateTargetLang", defaultTargetLang)
        Endpoint = ReadString("TranslateEndpoint", "")
        ApiKey = DpapiSecrets.Unprotect(ReadString("TranslateApiKey", ""))
        OverlayVisible = ReadBool("OverlayVisible", True)
        OverlayOpacity = ClampOpacity(ReadInt("OverlayOpacity", 210))
        DiskCache = ReadBool("OcrDiskCache", True)
        OllamaModel = ReadString("OllamaModel", "qwen2.5:3b")
        OcrModelQuality = ReadString("OcrModelQuality", "fast")
        OcrPageMode = ReadString("OcrPageMode", "auto")
    End Sub

    Public Sub Save()
        WriteBool("OcrEnabled", Enabled)
        WriteBool("OcrAutoMode", AutoMode)
        WriteString("OcrEngine", Engine)
        WriteString("OcrSourceLang", SourceLang)
        WriteString("TranslateProvider", Provider)
        WriteString("TranslateTargetLang", TargetLang)
        WriteString("TranslateEndpoint", Endpoint)
        WriteString("TranslateApiKey", DpapiSecrets.Protect(ApiKey))
        WriteBool("OverlayVisible", OverlayVisible)
        WriteString("OverlayOpacity", ClampOpacity(OverlayOpacity).ToString())
        WriteBool("OcrDiskCache", DiskCache)
        WriteString("OllamaModel", OllamaModel)
        WriteString("OcrModelQuality", OcrModelQuality)
        WriteString("OcrPageMode", OcrPageMode)
    End Sub

    ''' <summary>
    ''' Runtime OCR source hint. This is a single normalized code (for example
    ''' "auto", "rus", "ukr", "eng"), not a mixed-language list.
    ''' </summary>
    Public Function OcrSourceHint() As String
        Return NormalizeOcrSourceCode(SourceLang)
    End Function

    ''' <summary>
    ''' Ordered OCR attempt list for the configured source language. "auto"
    ''' intentionally prefers single-language passes before any broader fallback.
    ''' </summary>
    Public Function OcrLanguageAttempts() As List(Of String)
        Return OcrAttemptCodes(SourceLang)
    End Function

    ''' <summary>
    ''' Tesseract language string used for pre-downloading required language data.
    ''' This is for installation UX, not the runtime OCR attempt strategy.
    ''' </summary>
    Public Function OcrDownloadLanguages() As String
        Return TessLanguages(SourceLang)
    End Function

    Public Shared Function OcrAttemptCodes(sourceCode As String) As List(Of String)
        Dim attempts As New List(Of String)
        Dim primary As String = NormalizeOcrSourceCode(sourceCode)
        If primary <> "auto" Then AddUnique(attempts, primary)

        Select Case primary
            Case "auto"
                AddUnique(attempts, "rus")
                AddUnique(attempts, "ukr")
                AddUnique(attempts, "eng")
            Case "rus"
                AddUnique(attempts, "ukr")
                AddUnique(attempts, "eng")
            Case "ukr"
                AddUnique(attempts, "rus")
                AddUnique(attempts, "eng")
            Case "bel", "bul", "mkd"
                AddUnique(attempts, "rus")
                AddUnique(attempts, "eng")
            Case "eng"
                ' English-only by default.
            Case Else
                AddUnique(attempts, "eng")
        End Select

        Return attempts
    End Function

    Public Shared Function NormalizeOcrSourceCode(sourceCode As String) As String
        Dim s As String = If(sourceCode, "").Trim().ToLowerInvariant()
        If s.Length = 0 OrElse s = "auto" Then Return "auto"
        If s.IndexOf("+"c) >= 0 Then
            For Each part As String In s.Split("+"c)
                Dim normalizedPart As String = NormalizeOcrSourceCode(part)
                If normalizedPart <> "auto" Then Return normalizedPart
            Next
            Return "auto"
        End If
        Return TessSingleLanguage(s)
    End Function

    ''' <summary>Maps an app/source language code to the tesseract download list.</summary>
    Public Shared Function TessLanguages(sourceCode As String) As String
        Dim ordered As New List(Of String)
        For Each code As String In OcrAttemptCodes(sourceCode)
            If code <> "auto" Then AddUnique(ordered, code)
        Next
        If ordered.Count = 0 Then ordered.Add("eng")
        Return String.Join("+", ordered.ToArray())
    End Function

    Private Shared Function TessSingleLanguage(sourceCode As String) As String
        Dim s As String = If(sourceCode, "").Trim().ToLowerInvariant()
        Select Case s
            Case "en", "eng" : Return "eng"
            Case "ru", "rus" : Return "rus"
            Case "uk", "ukr" : Return "ukr"
            Case "be", "bel" : Return "bel"
            Case "de", "deu" : Return "deu"
            Case "fr", "fra" : Return "fra"
            Case "es", "spa" : Return "spa"
            Case "it", "ita" : Return "ita"
            Case "pt", "por" : Return "por"
            Case "nl", "nld" : Return "nld"
            Case "pl", "pol" : Return "pol"
            Case "cs", "ces" : Return "ces"
            Case "sk", "slk" : Return "slk"
            Case "sv", "swe" : Return "swe"
            Case "no", "nor" : Return "nor"
            Case "da", "dan" : Return "dan"
            Case "fi", "fin" : Return "fin"
            Case "tr", "tur" : Return "tur"
            Case "el", "ell" : Return "ell"
            Case "bg", "bul" : Return "bul"
            Case "ro", "ron" : Return "ron"
            Case "hu", "hun" : Return "hun"
            Case "ja", "jpn" : Return "jpn"
            Case "ko", "kor" : Return "kor"
            Case "zh", "chi_sim" : Return "chi_sim"
            Case "zh-tw", "chi_tra" : Return "chi_tra"
            Case "ar", "ara" : Return "ara"
            Case "he", "heb" : Return "heb"
            Case "hi", "hin" : Return "hin"
            Case "th", "tha" : Return "tha"
            Case "vi", "vie" : Return "vie"
            Case "id", "ind" : Return "ind"
            Case "fa", "fas" : Return "fas"
            Case Else : Return s
        End Select
    End Function

    Private Shared Sub AddUnique(items As List(Of String), value As String)
        If String.IsNullOrWhiteSpace(value) Then Return
        If items.Contains(value) Then Return
        items.Add(value)
    End Sub

    Public Shared Function ClampOpacity(value As Integer) As Integer
        If value < 40 Then Return 40
        If value > 255 Then Return 255
        Return value
    End Function

    ' --- registry helpers (SZA\FastMediaSorter) -------------------------------

    Private Shared Function ReadString(key As String, def As String) As String
        Return GetSetting(App_name, Second_App_Name, key, def)
    End Function

    Private Shared Function ReadBool(key As String, def As Boolean) As Boolean
        Return GetSetting(App_name, Second_App_Name, key, If(def, "1", "0")) = "1"
    End Function

    Private Shared Function ReadInt(key As String, def As Integer) As Integer
        Dim result As Integer = def
        Integer.TryParse(GetSetting(App_name, Second_App_Name, key, def.ToString()), result)
        Return result
    End Function

    Private Shared Sub WriteString(key As String, value As String)
        SaveSetting(App_name, Second_App_Name, key, If(value, ""))
    End Sub

    Private Shared Sub WriteBool(key As String, value As Boolean)
        SaveSetting(App_name, Second_App_Name, key, If(value, "1", "0"))
    End Sub

End Class
