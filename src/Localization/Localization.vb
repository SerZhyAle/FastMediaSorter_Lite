Option Strict On

Imports System.Collections.Generic
Imports System.Globalization

''' <summary>
''' The single localization layer for the viewer: main window, settings window and
''' every dialog.
'''
''' The key is the <b>Russian</b> source string, so call sites keep reading like plain
''' text and no resource-id indirection is needed; the value is one translation per
''' supported language. Adding a language means one column here instead of a ternary
''' in every file - which is exactly why the old
''' <c>If(Is_Russian_Language, "рус", "eng")</c> pattern is being replaced.
'''
''' See docs/specifications/SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.
'''
''' A key with no entry returns the Russian source unchanged: a new string is visible
''' but never crashes and never shows MISSING_KEY_42. LocalizationParityTests is what
''' turns that soft failure into a hard one at build time.
''' </summary>
Partial Public NotInheritable Class Localization

    Private Sub New()
    End Sub

    ''' <summary>
    ''' ISO 639-1 codes of the shipped UI languages. Index 0 is Russian - the KEY
    ''' language of the table, not a translation.
    '''
    ''' The x86/net48 fallback ships RU/EN only. Two reasons, and the second is
    ''' technical: the maintenance policy (CLAUDE.md) adds no features to that build,
    ''' and Nirmala UI - which draws Devanagari and Bengali - only arrived in
    ''' Windows 8, so on the Windows 7 machines that exe exists for, Hindi and Bengali
    ''' would render as boxes. Promising a language that cannot be read is worse than
    ''' not offering it.
    ''' </summary>
#If NETFRAMEWORK Then
    Public Shared ReadOnly Codes As String() = {"ru", "en"}
    Public Shared ReadOnly Names As String() = {"Русский", "English"}
#Else
    Public Shared ReadOnly Codes As String() = {
        "ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    ''' <summary>Endonyms - each language names itself, in the same order as Codes.</summary>
    Public Shared ReadOnly Names As String() = {
        "Русский", "English", "Українська", "Deutsch", "Italiano", "Español", "Français",
        "Português", "العربية", "हिन्दी", "বাংলা", "اردو", "中文"}
#End If

    ''' <summary>Index of English inside Codes - the fallback when a translation is empty.</summary>
    Private Const English As Integer = 1

    ''' <summary>
    ''' Russian source string -> one translation per language, EXCLUDING Russian
    ''' (index 0 is the key itself), so Values(i - 1) is the language at Codes(i).
    ''' Ordinal comparison: the keys are exact literals copied from the call sites.
    ''' </summary>
    Private Shared ReadOnly Map As New Dictionary(Of String, String())(StringComparer.Ordinal)

    Private Shared _currentCode As String = "en"

    Shared Sub New()
        ' Supplied by Localization.Registry.vb. A project that forgets to compile the
        ' registry partial fails here with BC30451 - the good kind of failure.
        RegisterStrings()
    End Sub

    ''' <summary>Every registered key with its translations - used by the parity test.</summary>
    Friend Shared ReadOnly Property All As IEnumerable(Of KeyValuePair(Of String, String()))
        Get
            Return Map
        End Get
    End Property

    ''' <summary>
    ''' The active UI language as an ISO code. Setting it to something this build does
    ''' not ship falls back to English rather than throwing - a registry value written
    ''' by a newer version must not stop an older exe from starting.
    ''' </summary>
    Public Shared Property CurrentCode As String
        Get
            Return _currentCode
        End Get
        Set(value As String)
            _currentCode = Normalize(value)
        End Set
    End Property

    ''' <summary>Index of the active language inside Codes.</summary>
    Public Shared ReadOnly Property CurrentIndex As Integer
        Get
            Return IndexOf(_currentCode)
        End Get
    End Property

    ''' <summary>The active language's endonym, for the picker and the settings combo.</summary>
    Public Shared ReadOnly Property CurrentName As String
        Get
            Return Names(CurrentIndex)
        End Get
    End Property

    Public Shared Function IndexOf(code As String) As Integer
        If code Is Nothing Then Return English
        For i = 0 To Codes.Length - 1
            If String.Equals(Codes(i), code, StringComparison.OrdinalIgnoreCase) Then Return i
        Next
        Return English
    End Function

    ''' <summary>A code this build ships, or "en".</summary>
    Public Shared Function Normalize(code As String) As String
        Return Codes(IndexOf(code))
    End Function

    ''' <summary>
    ''' Translate a Russian source string.
    '''
    ''' An unknown key returns the source unchanged; a language whose translation is
    ''' empty falls back to English rather than showing Russian to, say, a German user.
    ''' </summary>
    Public Shared Function T(ru As String) As String
        If ru Is Nothing OrElse ru.Length = 0 Then Return ru
        Dim index = CurrentIndex
        If index = 0 Then Return ru

        Dim values As String() = Nothing
        If Not Map.TryGetValue(ru, values) Then Return ru

        Dim value = values(index - 1)
        If value.Length = 0 Then value = values(English - 1)
        Return If(value.Length = 0, ru, value)
    End Function

    ''' <summary>
    ''' Translate and substitute. Every string that carries a runtime value goes through
    ''' here rather than through concatenation: word order differs per language, and in
    ''' Arabic and Urdu so does direction, so "Файл занят: " &amp; name cannot be translated
    ''' correctly - "Файл занят: {0}" can.
    ''' </summary>
    Public Shared Function TF(ru As String, ParamArray args As Object()) As String
        Dim pattern = T(ru)
        Try
            Return String.Format(pattern, args)
        Catch ex As FormatException
            ' A translation whose placeholders were mangled must not take the app down.
            ' The parity test checks placeholder arity, so this is belt-and-braces.
            Return String.Format(ru, args)
        End Try
    End Function

    ''' <summary>Arabic and Urdu are written right to left; WinForms has to be told.</summary>
    Public Shared Function IsRightToLeft() As Boolean
        Dim code = _currentCode
        Return code = "ar" OrElse code = "ur"
    End Function

    ''' <summary>
    ''' UI font family for a script the app's default font does not cover, or Nothing
    ''' when the default is fine.
    '''
    ''' Note this app is NOT on Segoe UI: ApplyApplicationDefaults pins
    ''' "Microsoft Sans Serif" 8.25 to keep the pixel-tuned Designer layout identical to
    ''' the net48 reference build. Microsoft Sans Serif has no Arabic, so unlike other
    ''' WinForms apps we must override for ar/ur as well as for the Indic and CJK scripts.
    ''' </summary>
    Public Shared Function FontFamily() As String
        Select Case _currentCode
            Case "hi", "bn" : Return "Nirmala UI"          ' Windows 8+
            Case "zh" : Return "Microsoft YaHei UI"
            Case "ar", "ur" : Return "Segoe UI"            ' naskh; Urdu Typesetting is not a UI font
            Case Else : Return Nothing
        End Select
    End Function

    ''' <summary>
    ''' Multiplier for fixed control widths. German, French, Spanish, Portuguese and
    ''' Italian run visibly longer than Russian for the same phrase, and the Indic
    ''' scripts need more room per glyph cluster.
    ''' </summary>
    Public Shared Function WidthScale() As Single
        Select Case _currentCode
            Case "de", "fr", "es", "pt", "it" : Return 1.15F
            Case "hi", "bn" : Return 1.1F
            Case "ar", "ur" : Return 1.05F
            Case Else : Return 1.0F
        End Select
    End Function

    ''' <summary>A fixed pixel width scaled for the active language.</summary>
    Public Shared Function Scaled(width As Integer) As Integer
        Return CInt(Math.Round(width * WidthScale()))
    End Function

    ''' <summary>
    ''' UI language for a fresh install (nothing saved yet): the language Windows' own
    ''' interface is in, when this build is translated into it - otherwise English,
    ''' never Russian (Russian is only the translation table's source language).
    '''
    ''' The user's DISPLAY language comes first (CurrentUICulture) and the language
    ''' Windows was installed in (InstalledUICulture) only after it: someone who
    ''' installed an English Windows and then switched the display language to German
    ''' is reading a German desktop, and that is what "the OS language" means to them.
    ''' Regional variants resolve to their language, so de-AT, pt-BR and zh-Hans-CN all
    ''' find their translation.
    ''' </summary>
    Public Shared Function DefaultCode() As String
        Try
            Dim ui = CultureInfo.CurrentUICulture
            Dim match = MatchCulture(ui)
            If match IsNot Nothing Then Return match

            match = MatchCulture(CultureInfo.InstalledUICulture)
            If match IsNot Nothing Then Return match
        Catch
            ' Culture probing can fail in a restricted configuration; English is the
            ' broader audience.
        End Try
        Return "en"
    End Function

    ''' <summary>The shipped code for a culture, or Nothing when there is no translation.</summary>
    Friend Shared Function MatchCulture(culture As CultureInfo) As String
        If culture Is Nothing OrElse culture.Name.Length = 0 Then Return Nothing
        Dim two = culture.TwoLetterISOLanguageName
        For i = 0 To Codes.Length - 1
            If String.Equals(Codes(i), two, StringComparison.OrdinalIgnoreCase) Then Return Codes(i)
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' Register one source string with its twelve translations, in Codes order after
    ''' Russian. Called only from the Localization.*.vb string tables.
    ''' </summary>
    Friend Shared Sub Add(ru As String, en As String, uk As String, de As String, it As String,
                          es As String, fr As String, pt As String, ar As String, hi As String,
                          bn As String, ur As String, zh As String)
        Map(ru) = New String() {en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh}
    End Sub

    ' ---------------------------------------------------------------- context ----
    ' Three Russian strings are genuinely two different things and already had two
    ' different English texts before this layer existed:
    '     "Загрузка: "            -> "Loading: "            (a file) / "Pulling: " (an Ollama model)
    '     "Перевод"               -> "Translate"            (button) / "Translation" (settings heading)
    '     "Подтверждение удаления"-> "Deletion Confirmation" / "Deletion.."
    ' A plain Russian-keyed dictionary would silently keep whichever was registered
    ' last and quietly change one of the two call sites. A context qualifier - the
    ' same idea as gettext's msgctxt - keeps them apart, and the parity test proves
    ' no other key needs one.

    ''' <summary>Separator between context and key. Never appears in UI text.</summary>
    Private Const ContextSeparator As Char = ChrW(1)

    ''' <summary>Register a translation that only applies in a particular context.</summary>
    Friend Shared Sub AddC(context As String, ru As String, en As String, uk As String, de As String,
                           it As String, es As String, fr As String, pt As String, ar As String,
                           hi As String, bn As String, ur As String, zh As String)
        Map(context & ContextSeparator & ru) = New String() {en, uk, de, it, es, fr, pt, ar, hi, bn, ur, zh}
    End Sub

    ''' <summary>
    ''' Translate within a context, falling back to the context-free entry and then to
    ''' the Russian source - so removing a context never breaks a call site.
    ''' </summary>
    Public Shared Function TC(context As String, ru As String) As String
        If ru Is Nothing OrElse ru.Length = 0 Then Return ru
        Dim index = CurrentIndex
        If index = 0 Then Return ru

        Dim values As String() = Nothing
        If Map.TryGetValue(context & ContextSeparator & ru, values) Then
            Dim scoped = values(index - 1)
            If scoped.Length = 0 Then scoped = values(English - 1)
            If scoped.Length > 0 Then Return scoped
        End If
        Return T(ru)
    End Function

End Class
