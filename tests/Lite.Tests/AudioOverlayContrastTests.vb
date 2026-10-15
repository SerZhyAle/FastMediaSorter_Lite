#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit
Imports Xunit.Abstractions

''' <summary>
''' APP-STYLE rule 5 and its 0.10 amendment (condition 3) for the one surface of the viewer that paints text over
''' content it does not choose: the audio plate over a cover or over the WAVE-PARTICLES backdrop. The surface
''' declares itself outside the theme (AudioOverlayColours), carries its own ink, and this holds the pair to the
''' bars of WINDOWS-UI section 6 item 4 - 4.5 : 1 for text, 3 : 1 for strokes - over the worst content there is: a
''' pure white and a pure black cover, and every grey and corner of the colour cube between.
''' </summary>
Public Class AudioOverlayContrastTests

    Private ReadOnly output As ITestOutputHelper

    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    ''' <summary>The content the plate may lie over: the greys (the brightest is the worst case for a light ink) and
    ''' the eight corners of the colour cube (the saturated worst cases of a coloured cover).</summary>
    Private Shared Function ContentSamples() As List(Of Color)
        Dim samples As New List(Of Color)()
        For level As Integer = 0 To 255 Step 15
            samples.Add(Color.FromArgb(level, level, level))
        Next
        samples.Add(Color.White)
        For Each r As Integer In {0, 255}
            For Each g As Integer In {0, 255}
                For Each b As Integer In {0, 255}
                    samples.Add(Color.FromArgb(r, g, b))
                Next
            Next
        Next
        Return samples
    End Function

    <Fact>
    Public Sub The_text_ink_over_the_plate_over_a_white_and_a_black_cover_is_at_least_4_5()
        Dim report As New List(Of String)()
        For Each ink As Color In AudioOverlayColours.TextInks().Distinct()
            Dim overWhite As Double = AudioOverlayColours.InkContrast(ink, Color.White)
            Dim overBlack As Double = AudioOverlayColours.InkContrast(ink, Color.Black)
            report.Add(String.Format("{0}: {1:0.00}:1 over a white cover (plate -> {2}), {3:0.00}:1 over a black cover",
                                     ink.Name, overWhite, AudioOverlayColours.PlateOver(Color.White).Name, overBlack))
            Assert.True(overWhite >= AudioOverlayColours.TextContrastFloor,
                        ink.Name & " over the plate over a white cover is " & overWhite.ToString("0.00") & ":1")
            Assert.True(overBlack >= AudioOverlayColours.TextContrastFloor,
                        ink.Name & " over the plate over a black cover is " & overBlack.ToString("0.00") & ":1")
        Next
        output.WriteLine(String.Join(Environment.NewLine, report))
    End Sub

    <Fact>
    Public Sub No_content_at_all_takes_a_text_ink_under_4_5_or_a_stroke_under_3()
        Dim worstText As Double = Double.MaxValue
        Dim worstStroke As Double = Double.MaxValue
        Dim textContent As Color = Color.Empty
        Dim strokeContent As Color = Color.Empty
        For Each content As Color In ContentSamples()
            For Each ink As Color In AudioOverlayColours.TextInks()
                Dim ratio As Double = AudioOverlayColours.InkContrast(ink, content)
                If ratio < worstText Then worstText = ratio : textContent = content
            Next
            For Each ink As Color In AudioOverlayColours.StrokeInks()
                Dim ratio As Double = AudioOverlayColours.InkContrast(ink, content)
                If ratio < worstStroke Then worstStroke = ratio : strokeContent = content
            Next
        Next
        output.WriteLine("worst text " & worstText.ToString("0.00") & ":1 over " & textContent.Name &
                         "; worst stroke " & worstStroke.ToString("0.00") & ":1 over " & strokeContent.Name)
        Assert.True(worstText >= AudioOverlayColours.TextContrastFloor, "text: " & worstText.ToString("0.00") & ":1 over " & textContent.Name)
        Assert.True(worstStroke >= AudioOverlayColours.StrokeContrastFloor, "stroke: " & worstStroke.ToString("0.00") & ":1 over " & strokeContent.Name)
    End Sub

    <Fact>
    Public Sub The_gate_can_fail_a_plate_that_is_too_thin_and_an_ink_that_is_too_quiet()
        ' The same arithmetic with a scrim of 60/255: white ink over a white cover is about 1.8 : 1.
        Dim thin As Color = Color.FromArgb(60, 0, 0, 0)
        Dim ratio As Double = Utils.ContrastRatio(Color.White, AudioOverlayColours.Composite(thin, Color.White))
        Assert.True(ratio < AudioOverlayColours.TextContrastFloor, "a 60/255 plate measured " & ratio.ToString("0.00"))
        ' And a dark ink on the real plate over a black cover is lost.
        Assert.True(AudioOverlayColours.InkContrast(Color.FromArgb(40, 40, 40), Color.Black) < AudioOverlayColours.StrokeContrastFloor)
    End Sub

    <Fact>
    Public Sub The_plate_is_the_declared_scrim_and_a_quiet_ink_stays_a_text()
        Assert.Equal(175, AudioOverlayColours.PlateAlpha)
        Assert.Equal(Color.FromArgb(175, 0, 0, 0), AudioOverlayColours.PlateBacking)
        Assert.Equal(Color.White, AudioOverlayColours.TitleInk)
        Assert.Equal(Color.Gainsboro, AudioOverlayColours.FormatInk)
        ' White content under it is 80/80/80: what the 8.1 : 1 and 5.9 : 1 of the report above stand on.
        Assert.Equal(Color.FromArgb(255, 80, 80, 80), AudioOverlayColours.PlateOver(Color.White))
    End Sub

    ''' <summary>
    ''' A RECORDED FINDING, not a bar the viewer meets. The lines and particles of the WAVE-PARTICLES backdrop are
    ''' the catalog's own colours and opacities (rule 16 caps nothing at more than 0.7 of an opacity that starts at
    ''' 0.28) - ambient, decorative, translucent by design - and are not a text or a meaningful glyph, so the
    ''' overlay's 3 : 1 is not asked of them. The numbers are measured here so the registry's row says what is true
    ''' and a catalog change that moves them is seen: at full opacity the weakest hue of a stroke over its wash is
    ''' the first of the two figures below; at the catalog's own opacity it is the second.
    ''' </summary>
    <Fact>
    Public Sub The_ambient_strokes_of_the_catalog_are_measured_against_their_wash_and_the_finding_is_recorded()
        For Each light As Boolean In {False, True}
            Dim wash As Color = If(light, Color.FromArgb(WaveParticlesConstants.WashLightChannel, WaveParticlesConstants.WashLightChannel, WaveParticlesConstants.WashLightChannel),
                                   Color.FromArgb(WaveParticlesConstants.WashDarkChannel, WaveParticlesConstants.WashDarkChannel, WaveParticlesConstants.WashDarkChannel))
            Dim lineLightness As Double = If(light, WaveParticlesConstants.LineLightnessLight, WaveParticlesConstants.LineLightnessDark)
            Dim lineOpacity As Double = (WaveParticlesConstants.LineOpacityBase + WaveParticlesConstants.LineOpacityGain) * WaveParticlesConstants.OpacityScale
            Dim weakestFull As Double = Double.MaxValue
            Dim weakestAtOpacity As Double = Double.MaxValue
            For hue As Integer = 0 To 359
                weakestFull = Math.Min(weakestFull, Utils.ContrastRatio(
                    WaveParticlesSession.HslColour(hue, WaveParticlesConstants.LineSaturation, lineLightness, 1.0R), wash))
                weakestAtOpacity = Math.Min(weakestAtOpacity, Utils.ContrastRatio(
                    AudioOverlayColours.Composite(WaveParticlesSession.HslColour(hue, WaveParticlesConstants.LineSaturation, lineLightness, lineOpacity), wash), wash))
            Next
            output.WriteLine((If(light, "light", "dark")) & " wash: weakest line hue " & weakestFull.ToString("0.00") & ":1 at full opacity, " &
                             weakestAtOpacity.ToString("0.00") & ":1 at the catalog's opacity " & lineOpacity.ToString("0.000"))
            ' The floor of the recorded finding: a catalog change that makes a stroke weaker than this is news.
            Assert.True(weakestFull >= 2.0, "the weakest line hue is " & weakestFull.ToString("0.00") & ":1 over its wash at full opacity")
            Assert.True(weakestAtOpacity > 1.0, "the translucent lines must still differ from the wash")
        Next
    End Sub

    ' ------------------------------------------------------------------ the declaration ----

    Private Shared Function ReadSource(name As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", name)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & name & " not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>A colour written into the paint code: a named colour, a brush or pen of one, a system colour, or
    ''' FromArgb with a number. FromArgb of an expression (the wash's own alpha) is arithmetic, not a literal.</summary>
    Private Shared ReadOnly LiteralColour As New Regex(
        "\bColor\.(?!Empty\b|Transparent\b|FromArgb\b)[A-Z]\w+|\bColor\.FromArgb\(\s*\d|\bBrushes\.|\bPens\.|\bSystemColors\.|\bSystemBrushes\.|\bSystemPens\.",
        RegexOptions.Compiled)

    Friend Shared Function ColourLiterals(source As String) As List(Of String)
        Dim result As New List(Of String)()
        Dim text As String = LocalizationCoverageTests.BlankComments(source)
        Dim lines As String() = text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        For index As Integer = 0 To lines.Length - 1
            For Each m As Match In LiteralColour.Matches(lines(index))
                result.Add("line " & (index + 1).ToString() & ": " & m.Value)
            Next
        Next
        Return result
    End Function

    <Fact>
    Public Sub The_audio_surface_paints_with_no_colour_literal_of_its_own()
        Dim literals As List(Of String) = ColourLiterals(ReadSource("Main_Form.AudioView.vb"))
        Assert.True(literals.Count = 0,
                    "Main_Form.AudioView.vb draws a colour that is not in AudioOverlayColours:" & vbLf & String.Join(vbLf, literals))
        Dim source As String = ReadSource("Main_Form.AudioView.vb")
        For Each role As String In {"PlateBacking", "TitleInk", "DetailInk", "FormatInk", "StrokeInk"}
            Assert.Contains("AudioOverlayColours." & role, source)
        Next
    End Sub

    <Fact>
    Public Sub The_overlay_declares_itself_outside_the_theme_where_its_colours_are_defined()
        Dim source As String = ReadSource("AudioOverlayColours.vb")
        Assert.Contains("DECLARED OUTSIDE THE THEME", source)
        Assert.Contains("APP-STYLE rule 5", source)
        Assert.Contains("a content overlay", source)
    End Sub

    <Fact>
    Public Sub The_literal_scanner_finds_a_planted_colour_and_lets_arithmetic_through()
        Assert.NotEmpty(ColourLiterals("    g.DrawString(t, f, Brushes.White, r)"))
        Assert.NotEmpty(ColourLiterals("    brush = New SolidBrush(Color.FromArgb(175, 0, 0, 0))"))
        Assert.NotEmpty(ColourLiterals("    pen = New Pen(Color.Gainsboro, 1.0F)"))
        Assert.Empty(ColourLiterals("    fill.Color = Color.FromArgb(CInt(Math.Round(washAlpha * 255.0R)), session.WashColour)"))
        Assert.Empty(ColourLiterals("    ' Brushes.White was here"))
        Assert.Empty(ColourLiterals("    Dim c As Color = Color.Empty"))
    End Sub

End Class
#End If
