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
    End Sub

    ''' <summary>Tesseract language string for the configured source language.</summary>
    Public Function OcrLanguages() As String
        Return TessLanguages(SourceLang)
    End Function

    ''' <summary>Maps an app/source language code to the tesseract code list.</summary>
    Public Shared Function TessLanguages(sourceCode As String) As String
        Dim s As String = If(sourceCode, "").Trim().ToLowerInvariant()
        Select Case s
            Case "", "auto" : Return "eng+rus+ukr"
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
