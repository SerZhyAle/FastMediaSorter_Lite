#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Xunit

''' <summary>
''' The panel's translucency is the fill's, never the ink's: a floating panel set to 40 % over a bright
''' picture still shows its glyphs and captions at full strength, because they are drawn into the ink
''' layer (a per-pixel-alpha window over the panel) and not into the panel's own faded window.
''' Pure paint checks on a surface and its ink - no window is shown.
''' </summary>
Public Class ChromeInkLayerTests

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(15000), "Chrome ink check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function NewSurface() As ChromeSurface
        Dim surface As New ChromeSurface()
        surface.Size = New Size(180, 44)
        Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.FromArgb(40, 40, 40), Color.White, False)
        ' The surface keeps the fonts it is handed (it owns none), so they must outlive it.
        surface.Configure(theme, 1.0F, False,
                          New Font("Segoe UI", 20.0F, FontStyle.Regular, GraphicsUnit.Pixel),
                          New Font("Segoe UI Symbol", 20.0F, FontStyle.Regular, GraphicsUnit.Pixel))
        surface.SetItems({New ChromeItem("open", ChromeItemKind.Button) With {.Caption = "Open"}})
        Return surface
    End Function

    Private Shared Function InteriorHasInk(bitmap As Bitmap, panelFill As Color) As Boolean
        For y As Integer = 5 To bitmap.Height - 6
            For x As Integer = 5 To bitmap.Width - 6
                If bitmap.GetPixel(x, y).ToArgb() <> panelFill.ToArgb() Then Return True
            Next
        Next
        Return False
    End Function

    <Fact>
    Public Sub The_ink_layer_carries_the_caption_at_full_strength_and_none_of_the_fill()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface()
                      Using ink As New Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb)
                          Using g As Graphics = Graphics.FromImage(ink)
                              g.Clear(Color.Transparent)
                              surface.PaintInk(g)
                          End Using
                          Dim strongest As Integer = 0
                          Dim fillLeaks As Integer = 0
                          For y As Integer = 0 To ink.Height - 1
                              For x As Integer = 0 To ink.Width - 1
                                  Dim a As Integer = ink.GetPixel(x, y).A
                                  strongest = Math.Max(strongest, a)
                                  ' A faint, wide wash is the fill; antialiasing only ever fringes the glyph.
                                  If a > 0 AndAlso a < 255 AndAlso (x < 3 OrElse y < 3 OrElse x > ink.Width - 4 OrElse y > ink.Height - 4) Then fillLeaks += 1
                              Next
                          Next
                          Assert.True(strongest >= 250, "No fully opaque ink pixel: the glyphs would fade with the panel (max alpha " & strongest & ").")
                          Assert.Equal(0, fillLeaks)
                          Assert.Equal(0, ink.GetPixel(1, 1).A)
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub With_the_ink_external_the_panel_paints_its_fill_only()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface()
                      Dim fill As Color = surface.Theme.PanelFill
                      Using own As New Bitmap(surface.Width, surface.Height)
                          surface.DrawToBitmap(own, New Rectangle(Point.Empty, surface.Size))
                          Assert.True(InteriorHasInk(own, fill), "Ordinary paint must still draw the caption itself.")

                          surface.InkExternal = True
                          Using fillOnly As New Bitmap(surface.Width, surface.Height)
                              surface.DrawToBitmap(fillOnly, New Rectangle(Point.Empty, surface.Size))
                              Assert.False(InteriorHasInk(fillOnly, fill), "The caption must be left to the ink layer, or it is drawn twice (once faded).")
                          End Using
                      End Using
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_ink_follows_the_panels_fade_and_never_its_translucency()
        OnSta(Sub()
                  Using host As New ChromeHost()
                      host.UseInkLayer = True
                      Assert.Equal(1.0R, host.InkOpacity)
                      host.Opacity = 0.4R
                      Assert.Equal(1.0R, host.InkOpacity)
                      host.InkOpacity = 0.5R
                      Assert.Equal(0.5R, host.InkOpacity)
                      host.InkOpacity = 7.0R
                      Assert.Equal(1.0R, host.InkOpacity)
                  End Using
              End Sub)
    End Sub

    ''' <summary>A surface on the panel colour model's theme (L0303), the black scheme: deterministic tone and fill.</summary>
    Private Shared Function NewModelSurface(setOpacityPercent As Integer) As ChromeSurface
        Dim surface As New ChromeSurface()
        surface.Size = New Size(180, 44)
        Dim colours As PanelColours = PanelColourModel.ForSample(Color.Black, PanelColourMetrics.BlackScheme, Nothing,
                                                                 setOpacityPercent, Color.FromArgb(0, 120, 215), False)
        surface.Configure(ChromeTheme.FromColours(colours), 1.0F, False,
                          New Font("Segoe UI", 20.0F, FontStyle.Regular, GraphicsUnit.Pixel),
                          New Font("Segoe UI Symbol", 20.0F, FontStyle.Regular, GraphicsUnit.Pixel))
        surface.SetItems({New ChromeItem("open", ChromeItemKind.Button) With {.Caption = "Open"}})
        Return surface
    End Function

    ''' <summary>The alpha the ink layer carries at a point of the item clear of the caption (its top strip).</summary>
    Private Shared Function ScrimAlphaPainted(surface As ChromeSurface) As Integer
        Dim bounds As Rectangle = surface.Items(0).Bounds
        Using ink As New Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb)
            Using g As Graphics = Graphics.FromImage(ink)
                g.Clear(Color.Transparent)
                surface.PaintInk(g)
            End Using
            Return ink.GetPixel(bounds.X + bounds.Width \ 2, bounds.Y + 2).A
        End Using
    End Function

    <Fact>
    Public Sub Below_the_needed_opacity_the_ink_layer_lays_the_items_fill_as_an_exact_scrim()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewModelSurface(0)
                      Dim theme As ChromeTheme = surface.Theme
                      surface.ScrimOpacity = 0.0R
                      Dim expected As Integer = CInt(Math.Round(PanelColourModel.ScrimAlpha(theme.PanelFill, theme.Foreground, PanelColourMetrics.TextFloor, 0.0R) * 255.0R))
                      Assert.True(expected > 0, "A fully see-through panel needs a scrim under its caption.")
                      Assert.InRange(ScrimAlphaPainted(surface), expected - 1, expected + 1)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub At_or_above_the_needed_opacity_there_is_no_scrim()
        OnSta(Sub()
                  Using surface As ChromeSurface = NewModelSurface(100)
                      Dim theme As ChromeTheme = surface.Theme
                      surface.ScrimOpacity = Math.Min(1.0R, PanelColourModel.OpacityNeeded(theme.PanelFill, theme.Foreground, PanelColourMetrics.TextFloor) + 0.01R)
                      Assert.Equal(0, ScrimAlphaPainted(surface))
                      surface.ScrimOpacity = 1.0R
                      Assert.Equal(0, ScrimAlphaPainted(surface))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_panels_ink_layer_carries_no_outline_ring()
        Dim host As String = File.ReadAllText(Path.Combine(SourceDir(), "Chrome", "ChromeHost.vb"))
        Assert.DoesNotMatch("ChromeInkOutline\.Compose|OutlineMask", host)
        Assert.Matches("panelSurface\.ScrimOpacity\s*=", host)
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        Assert.Matches("HasNativeEdit, Math\.Max\(chrome_Theme\.PanelOpacity, chrome_Theme\.EditFloorOpacity\)", chrome)
    End Sub

    <Fact>
    Public Sub The_recipients_table_draws_its_captions_on_the_ink_layer_while_see_through()
        ' L0303 S-5 (R-12): the table's own controls paint into the ink layer through IRecipientsInk; the window
        ' walks them at their places, the rows lay their own fill as the scrim, no outline ring is drawn.
        Dim overlay As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.RecipientsOverlay.vb"))
        Assert.Matches("Protected Overrides Sub PaintInkLayer\(g As Graphics, scrimBase As Double\)", overlay)
        Assert.Matches("Implements IRecipientsInk\.PaintInk", overlay)
        Assert.Matches("PanelColourModel\.ScrimAlpha\(fill, ForeColor, InkFloor, scrimBase\)", overlay)
        Assert.Matches("PanelColourModel\.ScrimAlpha\(fill, glyphInk, PanelColourMetrics\.GlyphFloor, scrimBase\)", overlay)
        Assert.Matches("host\.RequestInk\(\)", overlay)
        Assert.DoesNotMatch("ChromeInkOutline", overlay)
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        Assert.Matches("recipients_Overlay\.SetInkLayer\(Not chrome_High_Contrast AndAlso configured < 0\.999R\)", chrome)
        Assert.Matches("recipients_Overlay\.InkOpacity = Math\.Max\(0\.02R, Math\.Min\(1\.0R, recipients_Alpha\)\)", chrome)
        Assert.Matches("recipients_Overlay\.RefreshInkIfScrimMoved\(\)", chrome)
    End Sub

    <Fact>
    Public Sub Both_viewers_call_sites_turn_the_layer_on_only_for_a_translucent_panel()
        Dim chrome As String = File.ReadAllText(Path.Combine(SourceDir(), "Main_Form.Chrome.vb"))
        Assert.Matches("UseInkLayer\s*=\s*Not chrome_High_Contrast AndAlso chrome_Theme\.PanelOpacity\s*<\s*0\.999R", chrome)
        Assert.Matches("InkOpacity\s*=\s*Math\.Max\(0\.02R, Math\.Min\(1\.0R, p\.Alpha\)\)", chrome)
        Dim menu As String = File.ReadAllText(Path.Combine(SourceDir(), "Chrome", "ChromeMenu.vb"))
        Assert.Matches("UseInkLayer\s*=\s*Not theme\.IsHighContrast AndAlso theme\.PanelOpacity\s*<\s*0\.999R", menu)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_folder_field_owns_its_caption_in_both_paint_modes(external As Boolean)
        OnSta(Sub()
                  Using surface As ChromeSurface = NewSurface()
                      Dim item As ChromeItem = surface.Items(0)
                      Using field As New ChromePathBox With {.Bounds = item.Bounds, .Text = "C:\Media"}
                          surface.Controls.Add(field)
                          surface.InkExternal = external
                          Using baseline As New Bitmap(surface.Width, surface.Height)
                              Using actual As New Bitmap(surface.Width, surface.Height)
                                  Dim paint As Action(Of Bitmap) =
                                      Sub(bitmap)
                                          Using g As Graphics = Graphics.FromImage(bitmap)
                                              If external Then
                                                  surface.PaintInk(g)
                                              Else
                                                  Dim method = GetType(ChromeSurface).GetMethod("OnPaint", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
                                                  Using args As New System.Windows.Forms.PaintEventArgs(g, New Rectangle(Point.Empty, surface.Size))
                                                      method.Invoke(surface, {args})
                                                  End Using
                                              End If
                                          End Using
                                      End Sub
                                  item.Caption = ""
                                  paint(baseline)
                                  item.Caption = "Hidden duplicate folder caption"
                                  paint(actual)
                                  For y As Integer = 0 To surface.Height - 1
                                      For x As Integer = 0 To surface.Width - 1
                                          Assert.Equal(baseline.GetPixel(x, y).ToArgb(), actual.GetPixel(x, y).ToArgb())
                                      Next
                                  Next
                              End Using
                          End Using
                      End Using
                  End Using
              End Sub)
    End Sub

    Private Shared Function SourceDir() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("Could not locate the src\ directory from the test binary.")
    End Function

End Class
#End If
