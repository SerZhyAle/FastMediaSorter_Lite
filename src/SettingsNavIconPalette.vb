#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' The identity ink of every settings navigation page (SPECIFICATION 128 §3.2): one
''' stable hue per page, in a dark-theme and a light-theme value, so a row is
''' recognizable by its picture before its caption is read and the hue survives the
''' theme switch at a recognizable value. The glyph ids stay in <see cref="IconMap"/> -
''' this table adds only the colour.
'''
''' ICON-RENDER 0.16 section 11 permits navigation identity inks. The gates in
''' SettingsNavPaletteTests hold the table to 3 : 1 against its own theme's sidebar
''' fill and keep the eleven dark inks pairwise distinguishable.
''' </summary>
Public Module SettingsNavIconPalette

    Public NotInheritable Class NavInk
        Public Sub New(controlName As String, darkInk As Color, lightInk As Color)
            Me.ControlName = controlName
            Me.DarkInk = darkInk
            Me.LightInk = lightInk
        End Sub

        Public ReadOnly Property ControlName As String
        Public ReadOnly Property DarkInk As Color
        Public ReadOnly Property LightInk As Color

        Public ReadOnly Property Ink(darkTheme As Boolean) As Color
            Get
                Return If(darkTheme, DarkInk, LightInk)
            End Get
        End Property
    End Class

    Private ReadOnly table As NavInk() = LoadSettingsNavIdentityInks()

    ''' <summary>One entry per navigation row, in tab order. No nav code hardcodes a
    ''' hex: the row asks here, by control name, for the ink of its theme.</summary>
    Private Function LoadSettingsNavIdentityInks() As NavInk()
        ' dark ink / light ink per page - reviewed starting values of 128 §3.2.
        Return New NavInk() {
            New NavInk("settingsNavGeneral", Color.FromArgb(77, 208, 225), Color.FromArgb(0, 131, 143)),
            New NavInk("settingsNavRecipients", Color.FromArgb(240, 98, 146), Color.FromArgb(173, 20, 87)),
            New NavInk("settingsNavViewing", Color.FromArgb(102, 187, 106), Color.FromArgb(46, 125, 50)),
            New NavInk("settingsNavVideo", Color.FromArgb(239, 83, 80), Color.FromArgb(198, 40, 40)),
            New NavInk("settingsNavAudio", Color.FromArgb(171, 71, 188), Color.FromArgb(106, 27, 154)),
            New NavInk("settingsNavFiles", Color.FromArgb(255, 112, 67), Color.FromArgb(216, 67, 21)),
            New NavInk("settingsNavResources", Color.FromArgb(38, 166, 154), Color.FromArgb(0, 105, 92)),
            New NavInk("settingsNavOcr", Color.FromArgb(255, 167, 38), Color.FromArgb(225, 98, 0)),
            New NavInk("settingsNavTranslation", Color.FromArgb(92, 107, 192), Color.FromArgb(48, 63, 159)),
            New NavInk("settingsNavStorage", Color.FromArgb(120, 144, 156), Color.FromArgb(69, 90, 100)),
            New NavInk("settingsNavAbout", Color.FromArgb(158, 158, 158), Color.FromArgb(97, 97, 97))
        }
    End Function

    Public ReadOnly Property All As IEnumerable(Of NavInk)
        Get
            Return table
        End Get
    End Property

    Public Function TryGetInk(controlName As String, darkTheme As Boolean, ByRef ink As Color) As Boolean
        ink = Color.Empty
        If String.IsNullOrEmpty(controlName) Then Return False
        For Each entry As NavInk In table
            If entry.ControlName = controlName Then
                ink = entry.Ink(darkTheme)
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>Keep the destination hue while meeting contrast on all states of this row.
    ''' High contrast uses the system foreground selected by the caller.</summary>
    Public Function ResolveInk(controlName As String, darkTheme As Boolean, surfaces As Color(),
                               fallback As Color, highContrast As Boolean) As Color
        If highContrast Then Return fallback
        Dim ink As Color = fallback
        TryGetInk(controlName, darkTheme, ink)
        Dim endpoint As Color = If(darkTheme, Color.White, Color.Black)
        For stepIndex As Integer = 0 To 100
            Dim amount As Double = stepIndex / 100.0
            Dim candidate As Color = Color.FromArgb(
                CInt(Math.Round(ink.R + (CInt(endpoint.R) - CInt(ink.R)) * amount)),
                CInt(Math.Round(ink.G + (CInt(endpoint.G) - CInt(ink.G)) * amount)),
                CInt(Math.Round(ink.B + (CInt(endpoint.B) - CInt(ink.B)) * amount)))
            Dim passes As Boolean = True
            For Each surface As Color In surfaces
                If Utils.ContrastRatio(candidate, surface) < 3.0 Then passes = False
            Next
            If passes Then Return candidate
        Next
        Return fallback
    End Function
End Module
#End If
