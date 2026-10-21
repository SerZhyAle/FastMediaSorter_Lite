#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The chrome's state marks (ticket 243 T-9, slice 4; BUTTONS 2.0 section 2 rule 5, section 3 rule 5, section 4):
''' a checked toggle says so without colour, a menu button exposes popup and expanded, and the sort button keeps the
''' room of its widest caption in every language so choosing another order moves nothing.
''' </summary>
' The accessibility checks open a real popup (the message filter watches the desktop foreground window), so this class
' shares the collection that keeps desktop-touching chrome tests from running side by side.
<Collection("ChromeMenuDesktop")>
Public Class ChromeStateMarkTests

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
        Assert.True(thread.Join(60000), "Chrome state mark check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Function FamilyFor(code As String) As String
        If code = "hi" OrElse code = "bn" Then Return "Nirmala UI"
        If code = "zh" Then Return "Microsoft YaHei UI"
        Return "Segoe UI"
    End Function

    ''' <summary>The words of one key in language number <paramref name="index"/> (0 = the Russian source).</summary>
    Private Shared Function Word(key As String, index As Integer) As String
        If index = 0 Then Return key
        Dim values As String() = Localization.All.Where(Function(pair) pair.Key = key).Select(Function(pair) pair.Value).FirstOrDefault()
        If values Is Nothing Then Return key
        Return If(values(index - 1).Length > 0, values(index - 1), values(0))
    End Function

    ''' <summary>A theme whose accent IS the panel fill and whose accent ink IS the plain ink: with it a checked item can differ from
    ''' an unchecked one only by a mark that does not depend on colour (the colour is converted to one value by construction).</summary>
    Private Shared Function ColourBlindTheme(dark As Boolean) As ChromeTheme
        Dim theme As ChromeTheme = If(dark, ChromeTheme.FromScheme(Color.FromArgb(32, 32, 32), Color.Gainsboro, False),
                                      ChromeTheme.FromScheme(Color.White, Color.Black, False))
        theme.Accent = theme.PanelFill
        theme.AccentForeground = theme.Foreground
        Return theme
    End Function

    Private Shared Function PaintFills(surface As ChromeSurface) As Bitmap
        Dim bmp As New Bitmap(Math.Max(1, surface.Width), Math.Max(1, surface.Height))
        Using g As Graphics = Graphics.FromImage(bmp)
            Dim pe As New PaintEventArgs(g, New Rectangle(0, 0, bmp.Width, bmp.Height))
            surface.GetType().GetMethod("OnPaint", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(surface, {pe})
        End Using
        Return bmp
    End Function

    Private Shared Function OneToggle(theme As ChromeTheme, scale As Single, rtl As Boolean, withGlyph As Boolean, checked As Boolean,
                                      ByRef bounds As Rectangle, ByRef panelHeight As Integer) As Bitmap
        Using font As New Font("Segoe UI", UiScale.Px(ChromeMetrics.TextSize, scale), GraphicsUnit.Pixel),
              symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, scale), GraphicsUnit.Pixel),
              surface As New ChromeSurface()
            surface.Configure(theme, scale, rtl, font, symbols)
            Dim item As New ChromeItem("btn_Slideshow", ChromeItemKind.Toggle) With {.Checked = checked}
            If withGlyph Then item.GlyphId = "media.previous" Else item.Caption = "Toggle"
            surface.SetItems(New List(Of ChromeItem) From {item})
            surface.Size = surface.PreferredPanelSize()
            surface.LayoutItems()
            bounds = item.Bounds
            panelHeight = surface.ItemHeight()
            Return PaintFills(surface)
        End Using
    End Function

    ' --- 1. the non-colour mark of a checked toggle ---------------------------------------------------------

    <Fact>
    Public Sub A_checked_toggle_differs_from_an_unchecked_one_by_a_bar_that_survives_one_colour_everywhere()
        OnSta(Sub()
                  Dim ltrOffset As New Dictionary(Of String, Point)()
                  For Each dark As Boolean In New Boolean() {False, True}
                      For Each scale As Single In New Single() {1.0F, 1.75F}
                          For Each withGlyph As Boolean In New Boolean() {False, True}
                              For Each rtl As Boolean In New Boolean() {False, True}
                                  Dim theme As ChromeTheme = ColourBlindTheme(dark)
                                  Dim label As String = "dark=" & dark.ToString() & " scale=" & scale.ToString() & " glyph=" & withGlyph.ToString() & " rtl=" & rtl.ToString()
                                  Dim offBounds, onBounds As Rectangle
                                  Dim height As Integer
                                  Using plain As Bitmap = OneToggle(theme, scale, rtl, withGlyph, False, offBounds, height),
                                        marked As Bitmap = OneToggle(theme, scale, rtl, withGlyph, True, onBounds, height)
                                      ' A state change moves nothing and resizes nothing.
                                      Assert.Equal(offBounds, onBounds)
                                      Assert.Equal(plain.Size, marked.Size)

                                      Dim bar As Rectangle = ChromeSurface.CheckedBarBounds(onBounds, scale, False, height, rtl)
                                      Assert.False(bar.IsEmpty, label)
                                      Assert.True(onBounds.Contains(bar), label & " bar leaves the item")

                                      ' Colour-blind comparison: a pixel is "ink" when it differs from the panel fill. The two silhouettes may
                                      ' differ only inside the bar, and everywhere in the bar.
                                      Dim fill As Integer = theme.PanelFill.ToArgb()
                                      Dim differing As Integer = 0
                                      For y As Integer = 0 To marked.Height - 1
                                          For x As Integer = 0 To marked.Width - 1
                                              Dim a As Boolean = plain.GetPixel(x, y).ToArgb() <> fill
                                              Dim b As Boolean = marked.GetPixel(x, y).ToArgb() <> fill
                                              If a <> b Then
                                                  differing += 1
                                                  Assert.True(bar.Contains(x, y), label & " silhouette differs outside the bar at " & x.ToString() & "," & y.ToString())
                                              End If
                                          Next
                                      Next
                                      Assert.Equal(bar.Width * bar.Height, differing)
                                      ' The bar is drawn in the item's ink.
                                      For y As Integer = bar.Top To bar.Bottom - 1
                                          For x As Integer = bar.Left To bar.Right - 1
                                              Assert.Equal(theme.Foreground.ToArgb(), marked.GetPixel(x, y).ToArgb())
                                          Next
                                      Next

                                      ' The same place inside the item whatever the reading direction.
                                      Dim key As String = dark.ToString() & scale.ToString() & withGlyph.ToString()
                                      Dim offset As New Point(bar.X - onBounds.X, bar.Y - onBounds.Y)
                                      If Not rtl Then
                                          ltrOffset(key) = offset
                                      Else
                                          Assert.Equal(ltrOffset(key), offset)
                                      End If
                                  End Using
                              Next
                          Next
                      Next
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub The_bar_is_painted_in_the_ink_layer_with_the_accent_ink_so_it_reads_on_a_translucent_face()
        OnSta(Sub()
                  For Each dark As Boolean In New Boolean() {False, True}
                      For Each scale As Single In New Single() {1.0F, 1.75F}
                          Dim theme As ChromeTheme = If(dark, ChromeTheme.FromScheme(Color.FromArgb(32, 32, 32), Color.Gainsboro, False),
                                                        ChromeTheme.FromScheme(Color.White, Color.Black, False))
                          theme.PanelOpacity = 0.4R
                          Using font As New Font("Segoe UI", UiScale.Px(ChromeMetrics.TextSize, scale), GraphicsUnit.Pixel),
                                symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, scale), GraphicsUnit.Pixel),
                                surface As New ChromeSurface()
                              surface.Configure(theme, scale, False, font, symbols)
                              Dim item As New ChromeItem("btn_Slideshow", ChromeItemKind.Toggle) With {.GlyphId = "media.previous", .Checked = True}
                              surface.SetItems(New List(Of ChromeItem) From {item})
                              surface.Size = surface.PreferredPanelSize()
                              surface.InkExternal = True
                              surface.LayoutItems()
                              Dim bar As Rectangle = ChromeSurface.CheckedBarBounds(item.Bounds, scale, False, surface.ItemHeight(), False)
                              Using ink As New Bitmap(surface.Width, surface.Height, Imaging.PixelFormat.Format32bppArgb)
                                  Using g As Graphics = Graphics.FromImage(ink)
                                      g.Clear(Color.Transparent)
                                      surface.PaintInk(g)
                                  End Using
                                  Dim centre As Color = ink.GetPixel(bar.Left + bar.Width \ 2, bar.Top + bar.Height \ 2)
                                  Assert.Equal(255, CInt(centre.A))
                                  Assert.Equal(theme.AccentForeground.ToArgb(), centre.ToArgb())
                              End Using
                              ' The fill layer holds the accent under it; the ink on it reads as the contract asks (3:1 for a meaningful mark).
                              Assert.True(ChromeTheme.ContrastRatio(theme.AccentForeground, theme.Accent) >= 3.0R,
                                          "accent ink on the accent fill: " & ChromeTheme.ContrastRatio(theme.AccentForeground, theme.Accent).ToString("0.00"))
                          End Using
                      Next
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub One_mark_per_checked_item_pin_checkbox_and_high_contrast_keep_theirs()
        Dim glyph As New ChromeItem("a", ChromeItemKind.Toggle) With {.GlyphId = "media.previous", .Checked = True}
        Dim word As New ChromeItem("b", ChromeItemKind.Toggle) With {.Caption = "Word", .Checked = True}
        Dim onTop As New ChromeItem("c", ChromeItemKind.Toggle) With {.CustomGlyph = ChromeCustomGlyph.OnTop, .Checked = True}
        Dim box As New ChromeItem("d", ChromeItemKind.Toggle) With {.Checked = True}
        Dim pin As New ChromeItem("e", ChromeItemKind.Toggle) With {.IsPin = True, .Checked = True}
        Dim off As New ChromeItem("f", ChromeItemKind.Toggle) With {.GlyphId = "media.previous"}
        Dim button As New ChromeItem("g", ChromeItemKind.Button) With {.GlyphId = "media.previous", .Checked = True}
        Dim menuRow As New ChromeItem("h", ChromeItemKind.Toggle) With {.Caption = "Row", .Checked = True}

        For Each item As ChromeItem In New ChromeItem() {glyph, word, onTop, menuRow}
            Assert.Equal(ChromeCheckedMark.Bar, ChromeSurface.CheckedMarkFor(item, False))
            Assert.Equal(ChromeCheckedMark.Frame, ChromeSurface.CheckedMarkFor(item, True))
        Next
        ' A bare check box is its own mark, in every theme: no bar and no frame on top of its tick.
        Assert.Equal(ChromeCheckedMark.OwnTick, ChromeSurface.CheckedMarkFor(box, False))
        Assert.Equal(ChromeCheckedMark.OwnTick, ChromeSurface.CheckedMarkFor(box, True))
        ' The corner pin keeps its picture, and under high contrast the frame it always had.
        Assert.Equal(ChromeCheckedMark.None, ChromeSurface.CheckedMarkFor(pin, False))
        Assert.Equal(ChromeCheckedMark.Frame, ChromeSurface.CheckedMarkFor(pin, True))
        ' Unchecked toggles and checked non-toggles carry nothing.
        Assert.Equal(ChromeCheckedMark.None, ChromeSurface.CheckedMarkFor(off, False))
        Assert.Equal(ChromeCheckedMark.None, ChromeSurface.CheckedMarkFor(button, True))
        Assert.Equal(ChromeCheckedMark.None, ChromeSurface.CheckedMarkFor(Nothing, False))
    End Sub

    <Fact>
    Public Sub The_bar_stays_inside_the_item_clear_of_the_focus_rings_and_a_menu_row_mirrors_it_under_the_glyph_column()
        For Each scale As Single In New Single() {1.0F, 1.25F, 1.5F, 1.75F}
            Dim height As Integer = Math.Max(UiScale.Px(ChromeMetrics.ItemHeight, scale), ChromeMetrics.MinTarget)
            Dim wide As New Rectangle(40, 10, 220, height)
            Dim narrow As New Rectangle(40, 10, Math.Max(UiScale.Px(ChromeMetrics.TransportButtonWidth, scale), ChromeMetrics.MinTarget), height)
            Dim rings As Integer = UiScale.Px(ChromeMetrics.FocusRingInset, scale) + UiScale.Px(ChromeMetrics.FocusRingWidth, scale)
            For Each bounds As Rectangle In New Rectangle() {wide, narrow}
                For Each startAligned As Boolean In New Boolean() {False, True}
                    Dim ltr As Rectangle = ChromeSurface.CheckedBarBounds(bounds, scale, startAligned, height, False)
                    Dim rtl As Rectangle = ChromeSurface.CheckedBarBounds(bounds, scale, startAligned, height, True)
                    Assert.False(ltr.IsEmpty)
                    Assert.True(bounds.Contains(ltr) AndAlso bounds.Contains(rtl))
                    Assert.True(bounds.Bottom - ltr.Bottom >= rings, "the bar must stand clear of both focus contours")
                    Assert.True(ltr.Width >= 1 AndAlso ltr.Height >= 1)
                    If startAligned Then
                        ' Reading order: the start side leads, so the right-to-left bar is the left-to-right one mirrored in the item.
                        Assert.Equal(ltr.Left - bounds.Left, bounds.Right - rtl.Right)
                    Else
                        ' A toolbar item: centred, the same bar whatever the direction.
                        Assert.Equal(ltr, rtl)
                        Assert.True(Math.Abs((ltr.Left - bounds.Left) - (bounds.Right - ltr.Right)) <= 1)
                    End If
                Next
            Next
        Next
        ' Too small to hold a bar: none, rather than one that crosses the edge.
        Assert.True(ChromeSurface.CheckedBarBounds(New Rectangle(0, 0, 40, 6), 1.0F, False, 32, False).IsEmpty)
    End Sub

    ' --- 2. the accessible state ---------------------------------------------------------------------------

    Private Shared Function Has(state As AccessibleStates, flag As AccessibleStates) As Boolean
        Return (state And flag) = flag
    End Function

    <Fact>
    Public Sub A_menu_button_exposes_popup_and_flips_expanded_with_its_menu_and_a_toggle_exposes_checked()
        OnSta(Sub()
                  Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
                  Using owner As New Form(),
                        font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel),
                        surface As New ChromeSurface()
                      surface.Configure(theme, 1.0F, False, font, font)
                      Dim entries As New List(Of ChromeMenuItem) From {
                          New ChromeMenuItem("a") With {.Caption = "Alpha"},
                          New ChromeMenuItem("move") With {.Caption = "Move to", .Submenu = New List(Of ChromeMenuItem) From {
                              New ChromeMenuItem("c1") With {.Caption = "One"}}}}
                      Dim menuButton As New ChromeItem("cmbox_Sort", ChromeItemKind.MenuButton) With {.Caption = "Name"}
                      menuButton.Action = Sub() ChromeMenu.ShowPopup(owner, New Point(100, 100), entries, theme, 1.0F, False, font, font, focusFirstRow:=True)
                      Dim plain As New ChromeItem("btn_Plain", ChromeItemKind.Button) With {.Caption = "Plain"}
                      Dim toggle As New ChromeItem("chkbox_Top_Most", ChromeItemKind.Toggle) With {.Caption = "Top"}
                      surface.SetItems(New List(Of ChromeItem) From {menuButton, plain, toggle})
                      surface.Size = surface.PreferredPanelSize()
                      surface.LayoutItems()

                      Dim menuObject As ChromeSurface.ItemAccessibleObject = surface.AccessibleFor(menuButton)
                      Dim plainObject As ChromeSurface.ItemAccessibleObject = surface.AccessibleFor(plain)
                      Dim toggleObject As ChromeSurface.ItemAccessibleObject = surface.AccessibleFor(toggle)
                      Try
                          ' Closed: a popup exists and is collapsed.
                          Assert.True(Has(menuObject.State, AccessibleStates.HasPopup))
                          Assert.True(Has(menuObject.State, AccessibleStates.Collapsed))
                          Assert.False(Has(menuObject.State, AccessibleStates.Expanded))
                          Assert.Equal(AccessibleRole.ButtonDropDown, menuObject.Role)
                          ' Nothing else claims a popup.
                          For Each other As ChromeSurface.ItemAccessibleObject In New ChromeSurface.ItemAccessibleObject() {plainObject, toggleObject}
                              Assert.False(Has(other.State, AccessibleStates.HasPopup))
                              Assert.False(Has(other.State, AccessibleStates.Expanded))
                              Assert.False(Has(other.State, AccessibleStates.Collapsed))
                          Next
                          Assert.False(menuObject.TakeStateChange())

                          ' Open: expanded, not collapsed, and a reader is told once.
                          menuObject.DoDefaultAction()
                          Assert.True(ChromeMenu.PopupVisible)
                          Assert.True(Has(menuObject.State, AccessibleStates.HasPopup))
                          Assert.True(Has(menuObject.State, AccessibleStates.Expanded))
                          Assert.False(Has(menuObject.State, AccessibleStates.Collapsed))
                          Assert.True(menuObject.TakeStateChange())
                          Assert.False(menuObject.TakeStateChange())

                          ' A submenu opening and closing does not collapse the button: its chain is still on screen.
                          Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                          Assert.True(ChromeMenu.HandlePopupKey(Keys.Right))
                          Assert.Equal(2, ChromeMenu.OpenLevels)
                          Assert.True(ChromeMenu.HandlePopupKey(Keys.Left))
                          Assert.Equal(1, ChromeMenu.OpenLevels)
                          Assert.True(Has(menuObject.State, AccessibleStates.Expanded))
                          Assert.False(menuObject.TakeStateChange())

                          ' Escape closes the chain: collapsed again.
                          Assert.True(ChromeMenu.HandlePopupKey(Keys.Escape))
                          Assert.False(ChromeMenu.PopupVisible)
                          Assert.True(Has(menuObject.State, AccessibleStates.Collapsed))
                          Assert.False(Has(menuObject.State, AccessibleStates.Expanded))
                          Assert.True(menuObject.TakeStateChange())

                          ' Opened again and dismissed from outside (a click elsewhere, the owner going away).
                          menuObject.DoDefaultAction()
                          Assert.True(Has(menuObject.State, AccessibleStates.Expanded))
                          ChromeMenu.DismissPopup()
                          Application.DoEvents()
                          Assert.True(Has(menuObject.State, AccessibleStates.Collapsed))
                          Assert.False(Has(menuObject.State, AccessibleStates.Expanded))

                          ' A toggle says whether it is on, and a reader is told when it flips.
                          Assert.Equal(AccessibleRole.CheckButton, toggleObject.Role)
                          Assert.False(Has(toggleObject.State, AccessibleStates.Checked))
                          Assert.False(toggleObject.TakeStateChange())
                          toggle.Checked = True
                          Assert.True(Has(toggleObject.State, AccessibleStates.Checked))
                          Assert.True(toggleObject.TakeStateChange())
                          Assert.False(toggleObject.TakeStateChange())
                          toggle.Checked = False
                          Assert.False(Has(toggleObject.State, AccessibleStates.Checked))
                          Assert.True(toggleObject.TakeStateChange())
                      Finally
                          ChromeMenu.DismissPopup()
                          Application.DoEvents()
                      End Try
                  End Using
              End Sub)
    End Sub

    ' --- 3. the sort button's reserved width ----------------------------------------------------------------

    <Fact>
    Public Sub The_sort_button_reserves_the_widest_caption_in_all_thirteen_languages_and_its_width_never_changes_with_the_order()
        Assert.Equal(13, Localization.Codes.Length)
        OnSta(Sub()
                  Dim languagesWhereTheCaptionsDiffer As Integer = 0
                  For index As Integer = 0 To Localization.Codes.Length - 1
                      Dim code As String = Localization.Codes(index)
                      Dim localIndex As Integer = index
                      Dim captions As String() = ChromeMenuRules.SortCaptions(Function(key) Word(key, localIndex))
                      Assert.Equal(ChromeMenuRules.SortOrderCount, captions.Length)
                      For Each caption As String In captions
                          Assert.False(String.IsNullOrEmpty(caption), code & " has an empty sort caption")
                      Next
                      For Each scale As Single In New Single() {1.0F, 1.75F}
                          Using font As New Font(FamilyFor(code), UiScale.Px(ChromeMetrics.TextSize, scale), GraphicsUnit.Pixel),
                                symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, scale), GraphicsUnit.Pixel),
                                surface As New ChromeSurface()
                              surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), scale, code = "ar" OrElse code = "ur", font, symbols)

                              ' Without the reservation the width follows the caption (so the test can fail); with it, it does not.
                              Dim bare As New ChromeItem("cmbox_Sort", ChromeItemKind.MenuButton)
                              Dim bareWidths As New HashSet(Of Integer)()
                              Dim reserved As New ChromeItem("cmbox_Sort", ChromeItemKind.MenuButton) With {.ReservedTexts = captions}
                              Dim reservedWidths As New HashSet(Of Integer)()
                              For Each caption As String In captions
                                  bare.Caption = caption
                                  bareWidths.Add(surface.MeasureItemWidth(bare))
                                  reserved.Caption = caption
                                  reservedWidths.Add(surface.MeasureItemWidth(reserved))
                              Next
                              If scale = 1.0F AndAlso bareWidths.Count > 1 Then languagesWhereTheCaptionsDiffer += 1
                              Assert.Single(reservedWidths)

                              ' That one width is the widest caption's own, measured the way the chrome measures.
                              Dim flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix
                              Dim widest As String = captions.OrderByDescending(
                                  Function(caption) TextRenderer.MeasureText(caption, font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width).First()
                              Dim probe As New ChromeItem("cmbox_Sort", ChromeItemKind.MenuButton) With {.Caption = widest}
                              Assert.Equal(surface.MeasureItemWidth(probe), reservedWidths.Single())
                              Assert.Equal(bareWidths.Max(), reservedWidths.Single())
                          End Using
                      Next
                  Next
                  Assert.True(languagesWhereTheCaptionsDiffer > 0, "the captions never differ in width, so the reservation is untested")
              End Sub)
    End Sub

    ''' <summary>Where every item of a laid-out row stands and what collapsed into the "more" menu.</summary>
    Private Shared Function RowSignature(surface As ChromeSurface) As String
        Dim parts As New List(Of String)()
        For Each item As ChromeItem In surface.Items
            parts.Add(item.Id & "@" & item.Bounds.ToString())
        Next
        parts.Add("overflow=" & String.Join(",", surface.OverflowItems.Select(Function(item) item.Id)))
        Return String.Join("|", parts)
    End Function

    <Fact>
    Public Sub Changing_the_sort_order_moves_no_neighbour_and_changes_no_overflow_and_the_pinned_core_keeps_the_button()
        OnSta(Sub()
                  For index As Integer = 0 To Localization.Codes.Length - 1
                      Dim code As String = Localization.Codes(index)
                      Dim localIndex As Integer = index
                      Dim captions As String() = ChromeMenuRules.SortCaptions(Function(key) Word(key, localIndex))
                      Using font As New Font(FamilyFor(code), UiScale.Px(ChromeMetrics.TextSize, 1.0F), GraphicsUnit.Pixel),
                            symbols As New Font("Segoe UI Symbol", UiScale.Px(ChromeMetrics.SymbolSize, 1.0F), GraphicsUnit.Pixel),
                            surface As New ChromeSurface()
                          surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, code = "ar" OrElse code = "ur", font, symbols)
                          Dim sort As New ChromeItem("cmbox_Sort", ChromeItemKind.MenuButton) With {.Caption = captions(0), .ReservedTexts = captions}
                          Dim items As New List(Of ChromeItem) From {
                              New ChromeItem("btn_choose_file", ChromeItemKind.Button) With {.GlyphId = "action.edit"},
                              New ChromeItem("cmbox_Media_Folder", ChromeItemKind.Button) With {.PreferredWidth = ChromeMetrics.PathWidth, .MinimumWidth = ChromeMetrics.PathMinWidth, .Caption = "C:\Pictures"},
                              New ChromeItem("chkbox_Top_Most", ChromeItemKind.Toggle) With {.CustomGlyph = ChromeCustomGlyph.OnTop},
                              sort,
                              New ChromeItem("btn_A", ChromeItemKind.Button) With {.GlyphId = "action.edit"},
                              New ChromeItem("btn_B", ChromeItemKind.Button) With {.GlyphId = "action.edit"},
                              New ChromeItem("btn_C", ChromeItemKind.Button) With {.GlyphId = "action.edit"},
                              New ChromeItem("btn_D", ChromeItemKind.Button) With {.GlyphId = "action.edit"}}
                          surface.SetItems(items)
                          Dim natural As Integer = surface.PreferredPanelSize().Width
                          Dim height As Integer = surface.PreferredPanelSize().Height
                          ' Roomy, tight enough to push the trailing buttons into the "more" menu, and as narrow as the pinned core goes.
                          For Each width As Integer In New Integer() {natural + 40, natural - 70, surface.MinimumRowWidth() + 2 * surface.PanelPadding()}
                              surface.Size = New Size(width, height)
                              Dim baseline As String = Nothing
                              For mode As Integer = 0 To captions.Length - 1
                                  sort.Caption = captions(mode)
                                  surface.LayoutItems()
                                  Dim signature As String = RowSignature(surface)
                                  If baseline Is Nothing Then baseline = signature
                                  Assert.True(baseline = signature, code & " width " & width.ToString() & " mode " & mode.ToString() & vbLf & baseline & vbLf & signature)
                                  ' The pinned core: the sort button is in the row, never in the menu, at every width.
                                  Assert.False(sort.Bounds.IsEmpty, code & " lost the sort button at width " & width.ToString())
                                  Assert.DoesNotContain(sort, surface.OverflowItems)
                              Next
                          Next
                      End Using
                  Next
              End Sub)
    End Sub

    ' --- the wiring in the viewer (the form is not constructed here) -------------------------------------------

    Private Shared Function ReadSource(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then Return File.ReadAllText(Path.Combine(candidate, fileName)).Replace(vbCr, "")
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub The_viewer_hands_the_sort_item_every_caption_and_builds_them_from_one_function()
        Dim chrome As String = ReadSource("Main_Form.Chrome.vb")
        Assert.Contains("If carrier Is cmbox_Sort Then item.ReservedTexts = SortReservedCaptions()", chrome)
        ' One source for the words: the button's caption and the reservation can never disagree.
        Assert.Contains("ChromeMenuRules.SortCaption(index, AddressOf Localization.T)", chrome)
        Assert.Contains("ChromeMenuRules.SortCaptions(AddressOf Localization.T)", chrome)
        ' A language switch builds the list again.
        Assert.Contains("chrome_Sort_Captions_Language <> Localization.CurrentCode", chrome)
    End Sub
End Class
#End If
