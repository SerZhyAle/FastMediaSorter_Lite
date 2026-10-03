#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

' The application theme's pure half (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md, contract
' APP-STYLE rules 2-4): the three modes, their stored spelling, how a mode resolves to light or
' dark, and the ONE table of colour roles. Nothing here touches a window or the registry, so all
' of it is unit-tested (ThemePaletteTests).

''' <summary>The three modes of APP-STYLE rule 2. System is the default.</summary>
Public Enum ThemeMode
    System = 0
    Light = 1
    Dark = 2
End Enum

''' <summary>
''' The contract's role vocabulary (APP-STYLE rule 4), spelled as an enum. Success, Warning and
''' Danger are the contract's PROPOSED roles - added here because this product has status lines
''' (the hosting group, the password dialog, the folder list) that were literal green and red.
''' </summary>
Public Enum ThemeRole
    None = 0
    SurfaceWindow
    SurfaceRaised
    SurfaceSunken
    SurfaceSelected
    Control
    ControlHover
    ControlPressed
    Border
    TextPrimary
    TextMuted
    TextDisabled
    Accent
    AccentInk
    Link
    Info
    Success
    Warning
    Danger
End Enum

''' <summary>The stored spelling of a mode and its resolution.</summary>
Public NotInheritable Class ThemeModes

    Private Sub New()
    End Sub

    ''' <summary>The registry value under the shared SZA\FastMediaSorter hive - one value,
    ''' read and written by both programs, like UiLanguage.</summary>
    Public Const SettingValue As String = "UiTheme"

    ''' <summary>Anything unknown - a newer build's value, a typo, nothing at all - is System.</summary>
    Public Shared Function Parse(stored As String) As ThemeMode
        Select Case If(stored, "").Trim().ToLowerInvariant()
            Case "light" : Return ThemeMode.Light
            Case "dark" : Return ThemeMode.Dark
            Case Else : Return ThemeMode.System
        End Select
    End Function

    Public Shared Function ToSetting(mode As ThemeMode) As String
        Select Case mode
            Case ThemeMode.Light : Return "light"
            Case ThemeMode.Dark : Return "dark"
            Case Else : Return "system"
        End Select
    End Function

    ''' <summary>
    ''' True for dark. High contrast wins over everything (spec 060 rule 6): the light palette IS
    ''' the system colours, which is what a high-contrast user has chosen.
    ''' </summary>
    Public Shared Function ResolveDark(mode As ThemeMode, systemAppsUseLight As Boolean, highContrast As Boolean) As Boolean
        If highContrast Then Return False
        Select Case mode
            Case ThemeMode.Light : Return False
            Case ThemeMode.Dark : Return True
            Case Else : Return Not systemAppsUseLight
        End Select
    End Function

End Class

''' <summary>
''' The single palette table of APP-STYLE rule 3: every role has exactly one light and one dark
''' value, declared here and nowhere else.
'''
''' The LIGHT values are the SystemColors the controls used before the theme existed (spec 060
''' rule 5), so light mode is the pre-060 look to the pixel. That is also why light values are
''' read live (a SystemColors property follows high contrast and the classic themes) rather than
''' captured once.
''' </summary>
Public NotInheritable Class ThemePalette

    Private ReadOnly _dark As Boolean

    Private Sub New(dark As Boolean)
        _dark = dark
    End Sub

    Public Shared ReadOnly Light As New ThemePalette(False)
    Public Shared ReadOnly Dark As New ThemePalette(True)

    Public Shared Function ForDark(dark As Boolean) As ThemePalette
        Return If(dark, ThemePalette.Dark, ThemePalette.Light)
    End Function

    Public ReadOnly Property IsDark As Boolean
        Get
            Return _dark
        End Get
    End Property

    ''' <summary>The value of a role in this palette. None has no colour (Color.Empty).</summary>
    Public Function ColorOf(role As ThemeRole) As Color
        Return If(_dark, DarkValue(role), LightValue(role))
    End Function

    Public ReadOnly Property SurfaceWindow As Color
        Get
            Return ColorOf(ThemeRole.SurfaceWindow)
        End Get
    End Property
    Public ReadOnly Property SurfaceRaised As Color
        Get
            Return ColorOf(ThemeRole.SurfaceRaised)
        End Get
    End Property
    Public ReadOnly Property SurfaceSunken As Color
        Get
            Return ColorOf(ThemeRole.SurfaceSunken)
        End Get
    End Property
    Public ReadOnly Property SurfaceSelected As Color
        Get
            Return ColorOf(ThemeRole.SurfaceSelected)
        End Get
    End Property
    Public ReadOnly Property ControlFace As Color
        Get
            Return ColorOf(ThemeRole.Control)
        End Get
    End Property
    Public ReadOnly Property ControlHover As Color
        Get
            Return ColorOf(ThemeRole.ControlHover)
        End Get
    End Property
    Public ReadOnly Property ControlPressed As Color
        Get
            Return ColorOf(ThemeRole.ControlPressed)
        End Get
    End Property
    Public ReadOnly Property Border As Color
        Get
            Return ColorOf(ThemeRole.Border)
        End Get
    End Property
    Public ReadOnly Property TextPrimary As Color
        Get
            Return ColorOf(ThemeRole.TextPrimary)
        End Get
    End Property
    Public ReadOnly Property TextMuted As Color
        Get
            Return ColorOf(ThemeRole.TextMuted)
        End Get
    End Property
    Public ReadOnly Property TextDisabled As Color
        Get
            Return ColorOf(ThemeRole.TextDisabled)
        End Get
    End Property
    Public ReadOnly Property Accent As Color
        Get
            Return ColorOf(ThemeRole.Accent)
        End Get
    End Property
    Public ReadOnly Property AccentInk As Color
        Get
            Return ColorOf(ThemeRole.AccentInk)
        End Get
    End Property
    Public ReadOnly Property Link As Color
        Get
            Return ColorOf(ThemeRole.Link)
        End Get
    End Property
    Public ReadOnly Property Info As Color
        Get
            Return ColorOf(ThemeRole.Info)
        End Get
    End Property
    Public ReadOnly Property Success As Color
        Get
            Return ColorOf(ThemeRole.Success)
        End Get
    End Property
    Public ReadOnly Property Warning As Color
        Get
            Return ColorOf(ThemeRole.Warning)
        End Get
    End Property
    Public ReadOnly Property Danger As Color
        Get
            Return ColorOf(ThemeRole.Danger)
        End Get
    End Property

    ' --- the table ----------------------------------------------------------------------
    ' One Select per theme, one line per role. The light column is the system colour the role
    ' replaced; success/warning/danger keep the literals the code used before (ForestGreen,
    ' the Share Manager's attention amber, Firebrick). The dark column is chosen here: a
    ' neutral charcoal ladder close to Windows 11's own dark surfaces, text at >= 4.5:1 on
    ' every surface it is drawn on, and status hues lightened until they read on charcoal.

    Private Shared Function LightValue(role As ThemeRole) As Color
        Select Case role
            Case ThemeRole.SurfaceWindow : Return SystemColors.Control
            Case ThemeRole.SurfaceRaised : Return SystemColors.Window
            Case ThemeRole.SurfaceSunken : Return SystemColors.ControlLight
            Case ThemeRole.SurfaceSelected : Return SystemColors.Info
            Case ThemeRole.Control : Return SystemColors.Control
            Case ThemeRole.ControlHover : Return SystemColors.ControlLight
            Case ThemeRole.ControlPressed : Return SystemColors.ControlDark
            Case ThemeRole.Border : Return SystemColors.ControlDark
            Case ThemeRole.TextPrimary : Return SystemColors.ControlText
            Case ThemeRole.TextMuted : Return SystemColors.GrayText
            Case ThemeRole.TextDisabled : Return SystemColors.GrayText
            Case ThemeRole.Accent : Return SystemColors.Highlight
            Case ThemeRole.AccentInk : Return SystemColors.HighlightText
            Case ThemeRole.Link : Return SystemColors.HotTrack
            Case ThemeRole.Info : Return System.Drawing.Color.FromArgb(0, 102, 204)
            Case ThemeRole.Success : Return System.Drawing.Color.ForestGreen
            ' ICON-RENDER 0.14 section 10 D pins the shared day tone of state.warning: #EF6C00,
            ' 3.08:1 on white (the previous #B06000 measured 4.4:1 but broke the shared hue).
            Case ThemeRole.Warning : Return System.Drawing.Color.FromArgb(239, 108, 0)
            Case ThemeRole.Danger : Return System.Drawing.Color.Firebrick
            Case Else : Return System.Drawing.Color.Empty
        End Select
    End Function

    Private Shared Function DarkValue(role As ThemeRole) As Color
        Select Case role
            Case ThemeRole.SurfaceWindow : Return System.Drawing.Color.FromArgb(32, 32, 32)
            Case ThemeRole.SurfaceRaised : Return System.Drawing.Color.FromArgb(43, 43, 43)
            Case ThemeRole.SurfaceSunken : Return System.Drawing.Color.FromArgb(26, 26, 26)
            Case ThemeRole.SurfaceSelected : Return System.Drawing.Color.FromArgb(38, 66, 96)
            Case ThemeRole.Control : Return System.Drawing.Color.FromArgb(55, 55, 55)
            Case ThemeRole.ControlHover : Return System.Drawing.Color.FromArgb(68, 68, 68)
            Case ThemeRole.ControlPressed : Return System.Drawing.Color.FromArgb(82, 82, 82)
            Case ThemeRole.Border : Return System.Drawing.Color.FromArgb(85, 85, 85)
            Case ThemeRole.TextPrimary : Return System.Drawing.Color.FromArgb(240, 240, 240)
            Case ThemeRole.TextMuted : Return System.Drawing.Color.FromArgb(170, 170, 170)
            Case ThemeRole.TextDisabled : Return System.Drawing.Color.FromArgb(118, 118, 118)
            Case ThemeRole.Accent : Return System.Drawing.Color.FromArgb(0, 103, 192)
            Case ThemeRole.AccentInk : Return System.Drawing.Color.White
            Case ThemeRole.Link : Return System.Drawing.Color.FromArgb(125, 185, 250)
            Case ThemeRole.Info : Return System.Drawing.Color.FromArgb(110, 175, 235)
            Case ThemeRole.Success : Return System.Drawing.Color.FromArgb(108, 203, 118)
            Case ThemeRole.Warning : Return System.Drawing.Color.FromArgb(240, 178, 82)
            Case ThemeRole.Danger : Return System.Drawing.Color.FromArgb(255, 140, 130)
            Case Else : Return System.Drawing.Color.Empty
        End Select
    End Function

    ''' <summary>Every role that has a colour - the tests walk it.</summary>
    Public Shared Function AllRoles() As IReadOnlyList(Of ThemeRole)
        Dim list As New List(Of ThemeRole)()
        For Each value As ThemeRole In [Enum].GetValues(GetType(ThemeRole))
            If value <> ThemeRole.None Then list.Add(value)
        Next
        Return list
    End Function

    ' --- the system-colour map ----------------------------------------------------------

    ''' <summary>
    ''' The role a colour a control carries stands for: a system colour by
    ''' <see cref="RoleForSystemColor"/>, and a status colour - the value of success, warning or
    ''' danger in EITHER palette - as that status. The second half is what lets a status line the
    ''' code painted ForestGreen before the theme existed turn the dark palette's green in dark
    ''' mode, and back, without the code knowing a theme exists. Any other colour is the code's
    ''' own: None.
    ''' </summary>
    Public Shared Function RoleForColor(value As Color, asBackground As Boolean) As ThemeRole
        Dim role As ThemeRole = RoleForSystemColor(value, asBackground)
        If role <> ThemeRole.None OrElse asBackground OrElse value.IsEmpty Then Return role
        For Each status As ThemeRole In {ThemeRole.Success, ThemeRole.Warning, ThemeRole.Danger}
            If SameRgb(value, LightValue(status)) OrElse SameRgb(value, DarkValue(status)) Then Return status
        Next
        ' The greys the forms used for secondary text before the palette existed (hints, notes,
        ' "not set yet" lines): each meant "muted", and on charcoal each is unreadable.
        If SameRgb(value, System.Drawing.Color.DimGray) OrElse SameRgb(value, System.Drawing.Color.Gray) OrElse
           SameRgb(value, System.Drawing.Color.DarkGray) OrElse SameRgb(value, DarkValue(ThemeRole.TextMuted)) Then
            Return ThemeRole.TextMuted
        End If
        Return ThemeRole.None
    End Function

    Private Shared Function SameRgb(a As Color, b As Color) As Boolean
        Return a.A = b.A AndAlso a.R = b.R AndAlso a.G = b.G AndAlso a.B = b.B
    End Function

    ''' <summary>A colour restated in this palette: its role's value when it has one, otherwise
    ''' itself. For owner-drawn code that was handed a colour (a summary tint, a row ink) and must
    ''' paint it in whichever theme is active when it paints.</summary>
    Public Function Restate(value As Color, asBackground As Boolean) As Color
        Dim role As ThemeRole = RoleForColor(value, asBackground)
        Return If(role = ThemeRole.None, value, ColorOf(role))
    End Function

    ''' <summary>
    ''' Which role a system colour stood for when a control carried it (spec 060 rule 7).
    ''' <paramref name="asBackground"/> separates the two readings of the same colour -
    ''' HighlightText is ink, Highlight a fill. Window - the field a user types or picks in, and
    ''' the list a user reads - is surface.raised, one step above the window in dark mode.
    ''' A colour that is not a system colour returns None: it is the code's own choice and the
    ''' theme leaves it alone.
    ''' </summary>
    Public Shared Function RoleForSystemColor(value As Color, asBackground As Boolean) As ThemeRole
        If Not value.IsSystemColor Then Return ThemeRole.None
        Dim known As KnownColor = value.ToKnownColor()
        If asBackground Then
            Select Case known
                Case KnownColor.Control, KnownColor.ButtonFace, KnownColor.Menu, KnownColor.MenuBar,
                     KnownColor.AppWorkspace, KnownColor.InactiveBorder, KnownColor.ActiveBorder,
                     KnownColor.GradientInactiveCaption, KnownColor.InactiveCaption
                    Return ThemeRole.SurfaceWindow
                Case KnownColor.Window, KnownColor.ControlLightLight, KnownColor.ButtonHighlight
                    Return ThemeRole.SurfaceRaised
                Case KnownColor.ControlLight
                    Return ThemeRole.SurfaceSunken
                Case KnownColor.Info
                    Return ThemeRole.SurfaceSelected
                Case KnownColor.Highlight, KnownColor.MenuHighlight, KnownColor.HotTrack, KnownColor.ActiveCaption
                    Return ThemeRole.Accent
                Case KnownColor.ControlDark, KnownColor.ControlDarkDark, KnownColor.ButtonShadow, KnownColor.WindowFrame
                    Return ThemeRole.Border
                Case Else
                    Return ThemeRole.None
            End Select
        End If
        Select Case known
            Case KnownColor.ControlText, KnownColor.WindowText, KnownColor.MenuText, KnownColor.InfoText,
                 KnownColor.ActiveCaptionText, KnownColor.InactiveCaptionText, KnownColor.ControlDarkDark
                Return ThemeRole.TextPrimary
            Case KnownColor.GrayText, KnownColor.ControlDark
                Return ThemeRole.TextMuted
            Case KnownColor.HighlightText
                Return ThemeRole.AccentInk
            Case KnownColor.HotTrack, KnownColor.Highlight
                Return ThemeRole.Link
            Case Else
                Return ThemeRole.None
        End Select
    End Function

End Class
#End If
