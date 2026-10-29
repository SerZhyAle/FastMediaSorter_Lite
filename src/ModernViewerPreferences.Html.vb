#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Globalization

' Specification 275 section 12: global HTML viewer preferences. Session approvals (a broader
' root, remote resource origins, a redirect), runtime paths and reading history are never
' stored here - HtmlReaderState owns the history and the session owns its approvals, so an
' imported profile cannot grant an origin, a root or start Runtime/network work.
Partial Public NotInheritable Class ModernViewerPreferences
    Public Const HtmlFontSizeMin As Integer = 10
    Public Const HtmlFontSizeMax As Integer = 48
    Public Const HtmlColumnWidthMin As Integer = 40
    Public Const HtmlColumnWidthMax As Integer = 120
    Public Const HtmlMarginMax As Integer = 96
    Public Const HtmlZoomMin As Integer = 25
    Public Const HtmlZoomMax As Integer = 500
    Public Const HtmlHistoryLimitMax As Integer = 2000
    Public Const HtmlSnapshotCacheMbMin As Integer = 32
    Public Const HtmlSnapshotCacheMbMax As Integer = 256

    Public Property HtmlViewerEnabled As Boolean
    Public Property HtmlOnlinePagesEnabled As Boolean
    Public Property HtmlPresentation As String = "original"
    Public Property HtmlTheme As String = "system"
    Public Property HtmlFontFamily As String = "authored"
    Public Property HtmlFontSize As Integer = 18
    Public Property HtmlLineHeight As Double = 1.6
    Public Property HtmlColumnWidth As Integer = 80
    Public Property HtmlMargin As Integer = 16
    Public Property HtmlZoom As Integer = 100
    Public Property HtmlWrapWideContent As Boolean
    Public Property HtmlRememberPosition As Boolean = True
    Public Property HtmlRememberAppearance As Boolean = True
    Public Property HtmlFindCase As Boolean
    Public Property HtmlFindWholeWord As Boolean
    Public Property HtmlExternalLinks As String = "ask"
    Public Property HtmlHistoryLimit As Integer = 500
    Public Property HtmlSnapshotCacheMb As Integer = 128

    ' The stored names, in one list: Load, Save and the appearance reset read only these.
    Friend Shared ReadOnly HtmlPreferenceNames As String() = (
        "HtmlViewerEnabled HtmlOnlinePagesEnabled HtmlPresentation HtmlTheme HtmlFontFamily HtmlFontSize HtmlLineHeight " &
        "HtmlColumnWidth HtmlMargin HtmlZoom HtmlWrapWideContent HtmlRememberPosition HtmlRememberAppearance HtmlFindCase " &
        "HtmlFindWholeWord HtmlExternalLinks HtmlHistoryLimit HtmlSnapshotCacheMb").Split(" "c)

    ' The values Reset appearance returns to their defaults (positions and history stay).
    Friend Shared ReadOnly HtmlAppearanceNames As String() = {"HtmlPresentation", "HtmlTheme", "HtmlFontFamily", "HtmlFontSize",
        "HtmlLineHeight", "HtmlColumnWidth", "HtmlMargin", "HtmlZoom", "HtmlWrapWideContent"}

    Private Sub LoadHtmlPreferences()
        For Each name In HtmlPreferenceNames
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
        NormalizeHtmlPreferences()
    End Sub

    Private Sub SaveHtmlPreferences()
        NormalizeHtmlPreferences()
        For Each name In HtmlPreferenceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            If prop.PropertyType Is GetType(Boolean) Then
                WriteBool(name, CBool(prop.GetValue(Me)))
            Else
                WriteString(name, Convert.ToString(prop.GetValue(Me), CultureInfo.InvariantCulture))
            End If
        Next
    End Sub

    ' Load and import clamp the same way; an unknown enum falls back to its default.
    Friend Sub NormalizeHtmlPreferences()
        HtmlPresentation = Choice(HtmlPresentation, "original", "original", "reader")
        HtmlTheme = Choice(HtmlTheme, "system", "system", "light", "dark", "sepia", "black")
        HtmlFontFamily = NormalizeHtmlFontFamily(HtmlFontFamily)
        HtmlFontSize = Clamp(HtmlFontSize, HtmlFontSizeMin, HtmlFontSizeMax)
        HtmlLineHeight = If(Double.IsNaN(HtmlLineHeight) OrElse Double.IsInfinity(HtmlLineHeight), 1.6,
            Math.Round(Math.Max(1, Math.Min(3, HtmlLineHeight)), 1, MidpointRounding.AwayFromZero))
        HtmlColumnWidth = If(HtmlColumnWidth = 0, 0, Clamp(HtmlColumnWidth, HtmlColumnWidthMin, HtmlColumnWidthMax))
        HtmlMargin = Clamp(HtmlMargin, 0, HtmlMarginMax)
        HtmlZoom = Clamp(HtmlZoom, HtmlZoomMin, HtmlZoomMax)
        HtmlExternalLinks = Choice(HtmlExternalLinks, "ask", "ask", "block")
        HtmlHistoryLimit = Clamp(HtmlHistoryLimit, 0, HtmlHistoryLimitMax)
        HtmlSnapshotCacheMb = Clamp(HtmlSnapshotCacheMb, HtmlSnapshotCacheMbMin, HtmlSnapshotCacheMbMax)
    End Sub

    ' A font choice is a keyword or an installed family name; it is passed to fixed host code as
    ' JSON data, never spliced into CSS, but it is still bounded to a plain name here.
    Friend Shared Function NormalizeHtmlFontFamily(value As String) As String
        If String.IsNullOrWhiteSpace(value) Then Return "authored"
        Dim trimmed = value.Trim()
        If trimmed.Length > 128 Then Return "authored"
        For Each ch In trimmed
            If Char.IsControl(ch) OrElse ch = """"c OrElse ch = "'"c OrElse ch = "\"c OrElse ch = ";"c OrElse ch = "{"c OrElse ch = "}"c OrElse ch = "<"c OrElse ch = ">"c Then Return "authored"
        Next
        Return trimmed
    End Function

    Public Sub ResetHtmlAppearance()
        Dim defaults As New ModernViewerPreferences()
        For Each name In HtmlAppearanceNames
            Dim prop = GetType(ModernViewerPreferences).GetProperty(name)
            prop.SetValue(Me, prop.GetValue(defaults))
        Next
    End Sub
End Class
#End If
