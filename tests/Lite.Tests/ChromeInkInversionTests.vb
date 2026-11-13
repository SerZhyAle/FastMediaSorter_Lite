#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' The retired 220 rule (L0303 Q303-3): no surface of the floating chrome flips its ink with its opacity
''' setting any more - the floating panels since S-2, the recipients table since S-5. The ink is the panel
''' colour model's, and over a see-through surface it keeps its contrast through the ink layer's scrim.
''' Pure colour checks and call-site guards; no window is shown.
''' </summary>
Public Class ChromeInkInversionTests

    <Fact>
    Public Sub High_contrast_stays_opaque()
        Dim theme As ChromeTheme = ChromeTheme.ForHighContrast()
        Assert.True(theme.IsHighContrast)
        Assert.Equal(1.0R, theme.PanelOpacity)
    End Sub

    <Theory>
    <InlineData(0, 0, 0, 0)>
    <InlineData(255, 255, 255, 0)>
    <InlineData(0, 205, 90, 0)>
    <InlineData(148, 132, 119, 0)>
    <InlineData(0, 0, 0, 1)>
    <InlineData(255, 255, 255, 2)>
    Public Sub The_panels_ink_never_follows_the_opacity_setting(r As Integer, g As Integer, b As Integer, scheme As Integer)
        ' L0303 Q303-3: the 220 flip is retired; 49 and 50 give the same inks.
        Dim sample As Color = Color.FromArgb(r, g, b)
        Dim accent As Color = Color.FromArgb(0, 120, 215)
        Dim at49 As ChromeTheme = ChromeTheme.FromColours(PanelColourModel.ForSample(sample, scheme, Nothing, 49, accent, False))
        Dim at50 As ChromeTheme = ChromeTheme.FromColours(PanelColourModel.ForSample(sample, scheme, Nothing, 50, accent, False))
        Assert.Equal(at50.Foreground.ToArgb(), at49.Foreground.ToArgb())
        Assert.Equal(at50.DisabledForeground.ToArgb(), at49.DisabledForeground.ToArgb())
        Assert.Equal(at50.DestructiveForeground.ToArgb(), at49.DestructiveForeground.ToArgb())
        Assert.Equal(at50.AccentForeground.ToArgb(), at49.AccentForeground.ToArgb())
        Assert.Equal(at50.PanelFill.ToArgb(), at49.PanelFill.ToArgb())
        Assert.Equal(0.49R, at49.PanelOpacity, 3)
    End Sub

    <Fact>
    Public Sub Neither_the_panels_nor_the_recipients_table_flip_their_ink()
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        ' L0303 S-2: no opacity-driven flip on the panels; their theme is the model's.
        Assert.DoesNotMatch("InvertsInk\(", chrome)
        Assert.DoesNotMatch("WithInvertedInk\(", chrome)
        Assert.Matches("PanelColourModel\.ForSample\(", chrome)
        Assert.Matches("ChromeTheme\.FromColours\(", chrome)
        ' RecolorChrome hands the sample on unaltered (no luma override on the mainline).
        Dim layout As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.ModernLayout.vb"))
        Assert.Matches("ChromeApplyScheme\(back_Color,\s*Form_Color_Scheme\)", layout)

        ' L0303 S-5 (R-12): the recipients table takes the model too, at its own opacity setting, in the
        ' panels' tone - its own flip, the outline ring and the app-theme reads are gone.
        Dim overlay As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.RecipientsOverlay.vb"))
        Assert.DoesNotMatch("InvertsInk|WithInvertedInk|InvertedTone|InkInverted|ChromeInkOutline", overlay)
        Assert.Matches("PanelColourModel\.ForSample\(chrome_Scheme_Back,\s*chrome_Scheme_Number,\s*chrome_Panel_Tone,\s*percent,", overlay)
        Assert.Matches("recipients_Theme = ChromeTheme\.FromColours\(colours\)", overlay)
        Assert.DoesNotMatch("chrome_Theme\.", overlay)
        Assert.DoesNotMatch("chrome_Base_Theme", overlay)
        Assert.DoesNotMatch("ThemeService\.IsDark", overlay)

        ' The flip's machinery itself is gone from the chrome.
        For Each name As String In {"ChromeTheme.vb", "ChromeMetrics.vb", "ChromeHost.vb"}
            Dim text As String = File.ReadAllText(Path.Combine(SourceDir(), "Chrome", name))
            Assert.DoesNotMatch("InvertsInk|InkInvertBelowPercent|WithInvertedInk|InvertedTone|InkInverted|ChromeInkOutline", text)
        Next

        ' L0303 S-6: the settings row's hint no longer names the flip, in any of the 13 languages.
        Dim hint As String = File.ReadAllText(Path.Combine(SourceDir(), "Localization", "Localization.Chrome.vb"))
        Assert.DoesNotMatch("Ниже 50 %|Below 50% the labels|инвертируются|invert", hint)
        Assert.Matches("Add\(""Чем меньше значение, тем прозрачнее плавающие панели\."", ""The lower the value, the more transparent the floating panels\.""", hint)
        Dim settings As String = File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb"))
        Assert.Matches("Case ""chrome_opacity"" : Return Localization\.T\(""Чем меньше значение, тем прозрачнее плавающие панели\.""\)", settings)
    End Sub

    Private Shared Function SourceDir() As String
        For Each start As String In {AppContext.BaseDirectory, Environment.CurrentDirectory}
            Dim dir As New DirectoryInfo(start)
            While dir IsNot Nothing
                Dim candidate As String = Path.Combine(dir.FullName, "src")
                If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
                dir = dir.Parent
            End While
        Next
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
#End If
