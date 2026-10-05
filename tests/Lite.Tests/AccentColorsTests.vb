#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports Xunit

''' <summary>Ticket 124 UX-215 (Q124-4): one accent source for the chrome and the dark palette - read from Windows, with
''' the colour each consumer used before as its fallback.</summary>
Public Class AccentColorsTests

    Private Shared Function Abgr(value As UInteger) As Integer
        Return BitConverter.ToInt32(BitConverter.GetBytes(value), 0)
    End Function

    ''' <summary>Windows 11's default blue is stored as 0xFFD77800 (alpha, blue, green, red).</summary>
    <Fact>
    Public Sub The_stored_value_is_read_as_alpha_blue_green_red()
        Dim accent As Color? = AccentColors.FromDwmAccent(Abgr(&HFFD77800UI))
        Assert.True(accent.HasValue)
        Assert.Equal(Color.FromArgb(255, 0, 120, 215), accent.Value)
    End Sub

    <Fact>
    Public Sub A_missing_value_is_nothing_and_the_fallback_wins()
        Assert.False(AccentColors.FromDwmAccent(Nothing).HasValue)
        Assert.Equal(Color.Firebrick, AccentColors.Resolve(Nothing, Color.Firebrick))
    End Sub

    <Fact>
    Public Sub A_stored_value_wins_over_the_fallback_and_is_opaque()
        Dim accent As Color = AccentColors.Resolve(Abgr(&H00403020UI), Color.Firebrick)
        Assert.Equal(255, accent.A)
        Assert.Equal(Color.FromArgb(255, &H20, &H30, &H40), accent)
    End Sub

    ''' <summary>The dark palette writes white on the accent: a user accent too light for that is darkened until it
    ''' reads, and one that already reads is left alone.</summary>
    <Theory>
    <InlineData(255, 235, 59)>
    <InlineData(255, 255, 0)>
    <InlineData(200, 200, 200)>
    <InlineData(0, 103, 192)>
    <InlineData(30, 30, 30)>
    Public Sub White_ink_always_reads_on_the_darkened_accent(r As Integer, g As Integer, b As Integer)
        Dim accent As Color = AccentColors.Darkened(Color.FromArgb(r, g, b))
        Assert.True(ChromeTheme.ContrastRatio(Color.White, accent) >= 4.5, accent.ToString())
    End Sub

    <Fact>
    Public Sub An_accent_that_already_reads_is_not_changed()
        Dim blue As Color = Color.FromArgb(0, 103, 192)
        Assert.Equal(blue, AccentColors.Darkened(blue))
    End Sub

    ''' <summary>Whatever this machine's accent is, the two consumers resolve it through one source and the dark
    ''' palette's ink still reads on it.</summary>
    <Fact>
    Public Sub The_dark_palette_accent_keeps_its_ink_readable_on_this_machine()
        Dim p As ThemePalette = ThemePalette.Dark
        Assert.True(ChromeTheme.ContrastRatio(p.AccentInk, p.Accent) >= 4.5)
    End Sub

    <Fact>
    Public Sub The_chrome_accent_is_the_systems_accent_not_a_second_blue()
        Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, highContrast:=False)
        Dim expected As Color = Color.FromArgb(255, AccentColors.ForChrome(SystemColors.Highlight))
        Assert.Equal(expected, theme.Accent)
    End Sub

End Class
#End If
