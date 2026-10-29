#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Globalization
Imports System.Linq

' Specification 277 section 12.1: global text reader and editor preferences. The three opt-ins
' are independent stored values; editing and translation apply only while the full reader is on,
' which their consumers check, so turning the reader off and on again keeps the other two.
' Remembered encodings and positions are history and live in their own stores, not here.
Partial Public NotInheritable Class ModernViewerPreferences
    Public Const TextFontSizeMin As Single = 6.0F
    Public Const TextFontSizeMax As Single = 48.0F
    Public Const TextTabWidthMin As Integer = 1
    Public Const TextTabWidthMax As Integer = 16
    Public Const TextLineSpacingMin As Single = 1.0F
    Public Const TextLineSpacingMax As Single = 3.0F
    Public Const TextReadWindowMbMin As Integer = 1
    Public Const TextReadWindowMbMax As Integer = 16
    Public Const TextEditingMaxMbMin As Integer = 1
    Public Const TextEditingMaxMbMax As Integer = 100
    Public Const TextRecoveryIntervalSecondsMin As Integer = 10
    Public Const TextRecoveryIntervalSecondsMax As Integer = 300
    Public Const TextBackupVersionsMin As Integer = 1
    Public Const TextBackupVersionsMax As Integer = 10

    Public Property FullTextReaderEnabled As Boolean
    Public Property TextEditingEnabled As Boolean
    Public Property TextTranslationEnabled As Boolean
    Public Property TextFontFamilyProportional As String = "Segoe UI"
    Public Property TextFontFamilyMonospace As String = "Consolas"
    Public Property TextFontSize As Single = 12.0F
    Public Property TextTheme As String = "system"
    Public Property TextWrap As Boolean = True
    Public Property TextLineNumbers As Boolean
    Public Property TextTabWidth As Integer = 4
    Public Property TextLineSpacing As Single = 1.2F
    Public Property TextWhitespace As Boolean
    Public Property TextStatusBar As Boolean = True
    ' 0 is the system ANSI code page; otherwise one of TextEncodingDetector.ManualCandidates.
    Public Property TextEncodingFallbackCodePage As Integer
    Public Property TextRememberEncoding As Boolean = True
    Public Property TextRememberPosition As Boolean = True
    Public Property TextSearchCase As Boolean
    Public Property TextSearchWholeWord As Boolean
    Public Property TextRegexSearch As Boolean
    Public Property TextReadWindowMb As Integer = 4
    Public Property TextEditingMaxMb As Integer = 16
    Public Property TextSyntaxHighlighting As Boolean = True
    Public Property TextMarkdownPresentation As String = "source"
    Public Property TextRecoveryAutosave As Boolean = True
    Public Property TextRecoveryIntervalSeconds As Integer = 30
    Public Property TextBackupOnSave As Boolean = True
    Public Property TextBackupVersions As Integer = 1
    Public Property TextTranslationScope As String = "selection_or_buffer"

    ' The stored names, in one list: Load, Save and the profile reset scope read only these.
    Friend Shared ReadOnly TextPreferenceNames As String() = (
        "FullTextReaderEnabled TextEditingEnabled TextTranslationEnabled TextFontFamilyProportional TextFontFamilyMonospace " &
        "TextFontSize TextTheme TextWrap TextLineNumbers TextTabWidth TextLineSpacing TextWhitespace TextStatusBar " &
        "TextEncodingFallbackCodePage TextRememberEncoding TextRememberPosition TextSearchCase TextSearchWholeWord " &
        "TextRegexSearch TextReadWindowMb TextEditingMaxMb TextSyntaxHighlighting TextMarkdownPresentation " &
        "TextRecoveryAutosave TextRecoveryIntervalSeconds TextBackupOnSave TextBackupVersions TextTranslationScope").Split(" "c)

    Private Sub LoadTextPreferences()
        For Each name In TextPreferenceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            Dim fallback = Convert.ToString(prop.GetValue(Me), CultureInfo.InvariantCulture)
            Dim value = ReadString(name, fallback)
            Try
                If prop.PropertyType Is GetType(Boolean) Then
                    Dim parsed As Boolean
                    If value = "1" Then
                        prop.SetValue(Me, True)
                    ElseIf value = "0" Then
                        prop.SetValue(Me, False)
                    ElseIf Boolean.TryParse(value, parsed) Then
                        prop.SetValue(Me, parsed)
                    End If
                Else
                    prop.SetValue(Me, Convert.ChangeType(value, prop.PropertyType, CultureInfo.InvariantCulture))
                End If
            Catch ex As FormatException
            Catch ex As OverflowException
            End Try
        Next
        NormalizeTextPreferences()
    End Sub

    Private Sub SaveTextPreferences()
        NormalizeTextPreferences()
        For Each name In TextPreferenceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            If prop.PropertyType Is GetType(Boolean) Then
                WriteBool(name, CBool(prop.GetValue(Me)))
            Else
                WriteString(name, Convert.ToString(prop.GetValue(Me), CultureInfo.InvariantCulture))
            End If
        Next
    End Sub

    ' Load and import clamp the same way; an unknown choice falls back to its default.
    Friend Sub NormalizeTextPreferences()
        TextFontFamilyProportional = NormalizeTextFontFamily(TextFontFamilyProportional, "Segoe UI")
        TextFontFamilyMonospace = NormalizeTextFontFamily(TextFontFamilyMonospace, "Consolas")
        TextFontSize = ClampSingle(TextFontSize, TextFontSizeMin, TextFontSizeMax, 12.0F, 1)
        TextTheme = Choice(TextTheme, "system", "system", "light", "dark", "sepia", "black")
        TextTabWidth = Clamp(TextTabWidth, TextTabWidthMin, TextTabWidthMax)
        TextLineSpacing = ClampSingle(TextLineSpacing, TextLineSpacingMin, TextLineSpacingMax, 1.2F, 2)
        If TextEncodingFallbackCodePage <> 0 AndAlso
           Not TextEncodingDetector.ManualCandidates.Any(Function(candidate) candidate.CodePage = TextEncodingFallbackCodePage) Then
            TextEncodingFallbackCodePage = 0
        End If
        TextReadWindowMb = Clamp(TextReadWindowMb, TextReadWindowMbMin, TextReadWindowMbMax)
        TextEditingMaxMb = Clamp(TextEditingMaxMb, TextEditingMaxMbMin, TextEditingMaxMbMax)
        TextMarkdownPresentation = Choice(TextMarkdownPresentation, "source", "source", "rendered")
        TextRecoveryIntervalSeconds = Clamp(TextRecoveryIntervalSeconds, TextRecoveryIntervalSecondsMin, TextRecoveryIntervalSecondsMax)
        TextBackupVersions = Clamp(TextBackupVersions, TextBackupVersionsMin, TextBackupVersionsMax)
        TextTranslationScope = Choice(TextTranslationScope, "selection_or_buffer", "selection_or_buffer", "file")
    End Sub

    ' A family name goes to Font construction only; it is still bounded to a plain name here.
    Private Shared Function NormalizeTextFontFamily(value As String, defaultValue As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return defaultValue
        Dim trimmed = value.Trim()
        If trimmed.Length > 128 Then Return defaultValue
        For Each ch In trimmed
            If Char.IsControl(ch) Then Return defaultValue
        Next
        Return trimmed
    End Function

    Private Shared Function ClampSingle(value As Single, minimum As Single, maximum As Single, defaultValue As Single, digits As Integer) As Single
        If Single.IsNaN(value) OrElse Single.IsInfinity(value) Then Return defaultValue
        Return CSng(Math.Round(Math.Max(minimum, Math.Min(maximum, value)), digits, MidpointRounding.AwayFromZero))
    End Function
End Class
#End If
