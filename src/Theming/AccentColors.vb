#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Microsoft.Win32

''' <summary>
''' The one source of the accent colour (ticket 124 UX-215, Q124-4): what the user chose in Windows' Personalization
''' pages, read from the desktop window manager's own value, instead of two hard-coded blues - the chrome used
''' <c>SystemColors.Highlight</c> (a fixed blue on Windows 10/11, not the user's accent) while the dark palette of the
''' settings windows wrote (0, 103, 192) - so a window of either program sat beside the other in a different blue.
'''
''' Every consumer passes its OWN fallback, which is exactly the colour it used before: nothing changes on a machine
''' where the value is absent or unreadable, nor in high contrast (the callers never ask there). The ink on top of the
''' accent is the caller's business and is guarded here only for the one thing this class can break - a user accent
''' too light for white text (<see cref="Darkened"/>).
''' </summary>
Public NotInheritable Class AccentColors

    Private Sub New()
    End Sub

    ''' <summary>Contrast white text needs on a filled accent.</summary>
    Private Const WhiteInkContrast As Double = 4.5R

    ''' <summary>The colour Windows stores as <c>AccentColor</c> (0xAABBGGRR) turned into an opaque colour; Nothing for
    ''' a missing value.</summary>
    Public Shared Function FromDwmAccent(abgr As Integer?) As Color?
        If Not abgr.HasValue Then Return Nothing
        Dim v As UInteger = CUInt(abgr.Value And &HFFFFFFFFL)
        Dim r As Integer = CInt(v And &HFFUI)
        Dim g As Integer = CInt((v >> 8) And &HFFUI)
        Dim b As Integer = CInt((v >> 16) And &HFFUI)
        Return Color.FromArgb(255, r, g, b)
    End Function

    ''' <summary>The user's accent, or <paramref name="fallback"/> when Windows has none to give.</summary>
    Public Shared Function Resolve(abgr As Integer?, fallback As Color) As Color
        Dim chosen As Color? = FromDwmAccent(abgr)
        Return If(chosen.HasValue, chosen.Value, fallback)
    End Function

    ''' <summary>The same colour, darkened step by step until white text reads on it at 4.5 : 1 - for the places that
    ''' fill with the accent and write white on it (the dark palette). A colour that already reads is returned as is.</summary>
    Public Shared Function Darkened(accent As Color) As Color
        Dim result As Color = accent
        Dim steps As Integer = 0
        While Contrast(Color.White, result) < WhiteInkContrast AndAlso steps < 30
            result = Color.FromArgb(255, CInt(result.R * 0.92), CInt(result.G * 0.92), CInt(result.B * 0.92))
            steps += 1
        End While
        Return result
    End Function

    ''' <summary>The accent for a surface that follows the chrome's scheme (the viewer's floating panels).</summary>
    Public Shared Function ForChrome(fallback As Color) As Color
        Return Resolve(ReadDwmAccent(), fallback)
    End Function

    ''' <summary>The accent for the dark palette: the user's colour, kept dark enough for white ink, or the old fixed
    ''' blue when Windows gives none.</summary>
    Public Shared Function ForDarkSurface(fallback As Color) As Color
        Dim chosen As Color? = FromDwmAccent(ReadDwmAccent())
        Return If(chosen.HasValue, Darkened(chosen.Value), fallback)
    End Function

    Private Shared cachedRead As Boolean
    Private Shared cachedValue As Integer?

    ''' <summary>The stored accent, read once per process (a change takes effect at the next start - a surface that
    ''' repainted in a new colour while a window is open would be the surprise, not the feature).</summary>
    Public Shared Function ReadDwmAccent() As Integer?
        If cachedRead Then Return cachedValue
        cachedRead = True
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\DWM")
                Dim value As Object = If(key Is Nothing, Nothing, key.GetValue("AccentColor"))
                If value IsNot Nothing Then cachedValue = Convert.ToInt32(value)
            End Using
        Catch
            cachedValue = Nothing
        End Try
        Return cachedValue
    End Function

    Friend Shared Function Contrast(a As Color, b As Color) As Double
        Dim la As Double = Luminance(a)
        Dim lb As Double = Luminance(b)
        Return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05)
    End Function

    Private Shared Function Luminance(c As Color) As Double
        Return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B)
    End Function

    Private Shared Function Linear(channel As Integer) As Double
        Dim v As Double = channel / 255.0
        Return If(v <= 0.03928, v / 12.92, Math.Pow((v + 0.055) / 1.055, 2.4))
    End Function

End Class
#End If
