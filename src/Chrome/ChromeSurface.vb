#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' The one painted control inside a chrome host (002 section 2.2): it paints every item of its panel
''' itself - rounded fills, glyphs, captions, a focus ring - tracks hover and press, and turns a click
''' into the item's action. It is never selectable, so a click on it cannot take the keyboard focus
''' from the viewer (invariant 2); keyboard use is the deliberate focus mode of section 5, driven from
''' the main window.
'''
''' It owns no fonts and no theme: the controller hands them over, so a language or colour-scheme
''' change is one call and nothing here caches a colour of a previous scheme.
''' </summary>
Friend NotInheritable Class ChromeSurface
    Inherits Control

    Private ReadOnly itemList As New List(Of ChromeItem)()
    Private ReadOnly accessibleChildren As New Dictionary(Of ChromeItem, ItemAccessibleObject)()
    Private ReadOnly toolTip As New ToolTip() With {.ShowAlways = True, .OwnerDraw = True}

    Private chromeTheme As ChromeTheme = ChromeTheme.FromScheme(Color.FromArgb(32, 32, 32), Color.Gainsboro, False)
    Private currentScale As Single = 1.0F
    Private isRightToLeft As Boolean
    Private textFont As Font
    Private symbolFont As Font
    Private animated As Boolean = True
    Private inkExternalMode As Boolean
    Private inkPainting As Boolean

    ''' <summary>Raised at the end of a paint while the ink is external: the host then redraws its ink layer
    ''' from the same state, so the two never disagree.</summary>
    Friend Event InkNeeded As EventHandler

    ''' <summary>True while the glyphs and captions are drawn by the host's ink layer
    ''' (<see cref="PaintInk"/>) instead of here: the fills stay translucent with the window, the ink does not.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property InkExternal As Boolean
        Get
            Return inkExternalMode
        End Get
        Set(value As Boolean)
            If inkExternalMode = value Then Return
            inkExternalMode = value
            For Each child As Control In Controls
                Dim source As IChromeInkSource = TryCast(child, IChromeInkSource)
                If source IsNot Nothing Then source.InkModeChanged(value)
            Next
            Invalidate(True)
        End Set
    End Property

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property VerticalLayout As Boolean
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property StatusLayout As Boolean
    ''' <summary>The corner pin sits at the panel's physical START edge instead of its end (188 §3.1):
    ''' the window-controls panel keeps Minimize, Maximize/Restore and Close in physical order with
    ''' Close rightmost, so its pin leads the row whatever the reading direction.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property PinAtStart As Boolean
    ''' <summary>Extra logical padding at a horizontal panel's START/END edges (188 §3.1): the
    ''' window-controls panel's free background is its drag grip, so it breathes wider than the
    ''' toolbar rows. Vertical breathing is <see cref="ExtraPaddingVertical"/> and stays at zero,
    ''' so panels standing at the same top edge keep one height.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ExtraPadding As Integer

    ''' <summary>Extra logical padding at a horizontal panel's top and bottom edges. Zero - the
    ''' standard padding - keeps the height every other panel at that edge shows.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property ExtraPaddingVertical As Integer
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property OverflowId As String = "btn_Overflow"
    ''' <summary>The pin button of this panel's corner (the end of the row, the start edge in a
    ''' right-to-left language), or Nothing for a panel with none. It is laid out apart from the row, so
    ''' the row's overflow never takes it into the "more" menu.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property PinItem As ChromeItem
        Get
            Return pinButton
        End Get
        Set(value As ChromeItem)
            If ReferenceEquals(pinButton, value) Then Return
            If pinButton IsNot Nothing Then itemList.Remove(pinButton)
            pinButton = value
        End Set
    End Property
    Private pinButton As ChromeItem
    Private overflowItem As New ChromeItem("btn_Overflow", ChromeItemKind.MenuButton) With {.Caption = "»"}
    Private ReadOnly overflowList As New List(Of ChromeItem)()
    Public Event OverflowRequested As EventHandler
    Public Event LayoutChanged As EventHandler
    Public ReadOnly Property OverflowItems As IReadOnlyList(Of ChromeItem)
        Get
            Return overflowList
        End Get
    End Property

    Private hotItem As ChromeItem
    Private pressedItem As ChromeItem
    Private focusedItem As ChromeItem
    Private inKeyboardMode As Boolean
    Private shownTip As String = ""
    Private holdElapsed As Integer
    Private holdInvoked As Boolean
    ''' <summary>The press that is running came from a touch screen or a pen, so a long press may fire the
    ''' secondary action (125 Q125-1). A mouse press never does: it has the right button.</summary>
    Private holdArmed As Boolean
    ' A menu's scrolled window onto this surface (set by ChromeMenu): where the visible part starts and
    ' how tall it is, so the scroll cues can be painted at its edges (125 UX-329).
    Private viewOffset As Integer
    Private viewHeight As Integer

    ''' <summary>A test seam: what the pointer says about where the press came from. Nothing = ask Windows.</summary>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property PressExtraInfo As Func(Of Long)

    <DllImport("user32.dll")>
    Private Shared Function GetMessageExtraInfo() As IntPtr
    End Function

    ''' <summary>Raised after an item's action ran, so the owner can re-assert z-order or reposition.</summary>
    Public Event ItemInvoked As EventHandler(Of ChromeItem)

    ''' <summary>A left press on the panel's free background - no item under the pointer. The
    ''' window-controls panel turns it into a native window move (188 §5); nothing else listens.</summary>
    Public Event MoveGripPress As EventHandler

    ''' <summary>The second background press of a double click, near the first. The window-controls
    ''' panel toggles maximize with it (188 §5); the first press has already run its own event.</summary>
    Public Event MoveGripDoubleClick As EventHandler

    ''' <summary>The last move-grip press (free background or a grip slot), for the double-click
    ''' rule: its tick (<see cref="Integer.MinValue"/> when there was none in memory) and its
    ''' screen position.</summary>
    Private lastMovePressTick As Integer = Integer.MinValue
    Private lastMovePressScreen As Point

    Public Sub New()
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or
                 ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        ' Never selectable: a click here must not take the keyboard from the viewer.
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
        Dock = DockStyle.Fill
        overflowItem.Action = Sub() RaiseEvent OverflowRequested(Me, EventArgs.Empty)
        AddHandler toolTip.Popup, Sub(sender, args)
                                     args.ToolTipSize = TextRenderer.MeasureText(shownTip, If(textFont, Font),
                                         New Size(Px(ChromeMetrics.TooltipWidth), Integer.MaxValue), TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix) +
                                         New Size(2 * PanelPadding(), 2 * PanelPadding())
                                 End Sub
        AddHandler toolTip.Draw, Sub(sender, args)
                                    args.Graphics.Clear(chromeTheme.PanelFill)
                                    Using pen As New Pen(chromeTheme.Hairline)
                                        args.Graphics.DrawRectangle(pen, 0, 0, args.Bounds.Width - 1, args.Bounds.Height - 1)
                                    End Using
                                    Dim area As Rectangle = Rectangle.Inflate(args.Bounds, -PanelPadding(), -PanelPadding())
                                    TextRenderer.DrawText(args.Graphics, args.ToolTipText, If(textFont, Font), area, chromeTheme.Foreground,
                                        TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or If(isRightToLeft, TextFormatFlags.RightToLeft, TextFormatFlags.Default))
                                End Sub
    End Sub

    ' --- state handed over by the controller --------------------------------------------------

    Public ReadOnly Property Items As IReadOnlyList(Of ChromeItem)
        Get
            Return itemList
        End Get
    End Property

    Public ReadOnly Property Theme As ChromeTheme
        Get
            Return chromeTheme
        End Get
    End Property

    Public ReadOnly Property ScaleFactor As Single
        Get
            Return currentScale
        End Get
    End Property

    Public ReadOnly Property KeyboardMode As Boolean
        Get
            Return inKeyboardMode
        End Get
    End Property

    Public Sub SetItems(newItems As IEnumerable(Of ChromeItem))
        overflowItem = New ChromeItem(OverflowId, ChromeItemKind.MenuButton) With {.Caption = "»", .Action = Sub() RaiseEvent OverflowRequested(Me, EventArgs.Empty)}
        itemList.Clear()
        accessibleChildren.Clear()
        hotItem = Nothing
        pressedItem = Nothing
        focusedItem = Nothing
        If newItems IsNot Nothing Then itemList.AddRange(newItems)
        LayoutItems()
    End Sub

    ''' <summary>Theme, scale, direction and fonts in one call: they only ever change together.</summary>
    Public Sub Configure(theme As ChromeTheme, scale As Single, rightToLeft As Boolean,
                         font As Font, symbols As Font)
        chromeTheme = If(theme, chromeTheme)
        currentScale = If(scale > 0.0F, scale, 1.0F)
        isRightToLeft = rightToLeft
        textFont = font
        symbolFont = symbols
        animated = chromeTheme.AllowsAnimation
        LayoutItems()
        Invalidate()
    End Sub

    ''' <summary>The user asked for no motion, or the theme forbids it: levels then jump to their targets.</summary>
    Public Sub SetAnimationEnabled(enabled As Boolean)
        animated = enabled AndAlso chromeTheme.AllowsAnimation
        If Not animated Then SettleLevels()
    End Sub

    ' --- measuring and layout ---------------------------------------------------------------

    Private Function Px(logical As Integer) As Integer
        Return UiScale.Px(logical, currentScale)
    End Function

    ''' <summary>How wide an item wants to be: the item height for a picture (square, never below the
    ''' pointer minimum), the measured caption plus padding for a word (a German caption is 30-40 %
    ''' longer than a Russian one, so it is measured, never assumed).</summary>
    Public Function MeasureItemWidth(item As ChromeItem) As Integer
        If item.IsSeparator Then Return 0
        Dim square As Integer = Math.Max(Px(ChromeMetrics.ItemHeight), MinTargetPx())
        If item.PreferredWidth > 0 Then Return Px(item.PreferredWidth)
        Dim chevron As Integer = If(ShowsMenuChevron(item), ChevronStrip(), 0)
        ' A text that reserves a width (a zoom value that is not on screen right now) is measured, not a bare square.
        If Not VerticalLayout AndAlso Not item.ShowCaptionWithGlyph AndAlso
           (item.Image IsNot Nothing OrElse item.GlyphId.Length > 0 OrElse (item.Caption.Length = 0 AndAlso item.ReservedText.Length = 0)) Then Return square + chevron
        Dim font As Font = FontFor(item)
        If font Is Nothing Then Return square
        Dim flags As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix
        Dim text As Integer = TextRenderer.MeasureText(item.Caption, font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width
        If item.ReservedText.Length > 0 Then
            text = Math.Max(text, TextRenderer.MeasureText(item.ReservedText, font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width)
        End If
        ' A menu row also carries its key hint and, for a submenu, the chevron, at its end.
        Dim tail As Integer = 0
        If VerticalLayout Then
            If item.Hint.Length > 0 Then tail += TextRenderer.MeasureText(item.Hint, font, New Size(Integer.MaxValue, Integer.MaxValue), flags).Width + 2 * Px(ChromeMetrics.CaptionPadding)
            If item.Chevron Then tail += ChevronStrip()
        End If
        Return Math.Max(square, text + tail + 2 * Px(ChromeMetrics.CaptionPadding) + chevron +
                        If((VerticalLayout OrElse item.ShowCaptionWithGlyph) AndAlso (item.Image IsNot Nothing OrElse item.GlyphId.Length > 0), square, 0))
    End Function

    ''' <summary>The smallest a hit target gets: <see cref="ChromeMetrics.MinTarget"/> scaled, but never under
    ''' the logical floor itself - the "small" panel size shrinks the look, not the 28 px that ICON-RENDER 0.15
    ''' rule 5 asks of a mouse target (125 UX-313e).</summary>
    Private Function MinTargetPx() As Integer
        Return Math.Max(ChromeMetrics.MinTarget, Px(ChromeMetrics.MinTarget))
    End Function

    ''' <summary>The "▾" of a menu button, at the end of its row: the item opens a menu (125 UX-305).</summary>
    Private Function ShowsMenuChevron(item As ChromeItem) As Boolean
        Return Not VerticalLayout AndAlso item.Kind = ChromeItemKind.MenuButton AndAlso item.Id <> OverflowId AndAlso item IsNot overflowItem
    End Function

    Private Function ChevronStrip() As Integer
        Return Px(ChromeMetrics.ChevronWidth)
    End Function

    Public Function ItemHeight() As Integer
        Return Math.Max(Math.Max(Px(ChromeMetrics.ItemHeight), MinTargetPx()), If(textFont Is Nothing, 0, textFont.Height + 2 * Px(ChromeMetrics.Gap)))
    End Function

    ''' <summary>The height of one row of a vertical layout: a separator is a hairline's worth.</summary>
    Private Function RowHeight(item As ChromeItem) As Integer
        Return If(item.IsSeparator, ChromeMenuRules.SeparatorHeight(Px(ChromeMetrics.Gap)), ItemHeight())
    End Function

    Public Function PanelPadding() As Integer
        Return Px(ChromeMetrics.PanelPadding + Math.Max(0, ExtraPadding))
    End Function

    ''' <summary>The padding at a horizontal panel's top and bottom edges.</summary>
    Public Function PanelPaddingVertical() As Integer
        Return Px(ChromeMetrics.PanelPadding + Math.Max(0, ExtraPaddingVertical))
    End Function

    ''' <summary>The width the corner pin takes out of a row, gap included; 0 when there is none (or in
    ''' a popup menu, which has no corner).</summary>
    Private Function PinReserve() As Integer
        If PinItem Is Nothing OrElse VerticalLayout Then Return 0
        Return MinTargetPx() + Px(ChromeMetrics.Gap)
    End Function

    ''' <summary>The size a panel with these items wants: one row, padded. The height comes from
    ''' the VERTICAL padding, so the window-controls panel - wider, but with the standard vertical
    ''' breathing - stands exactly as tall as the toolbar rows beside it (2026-10-12).</summary>
    Public Function PreferredPanelSize() As Size
        Dim slots As List(Of ChromeSlot) = BuildSlots(Nothing)
        Dim pad As Integer = PanelPadding()
        Dim verticalPad As Integer = PanelPaddingVertical()
        If VerticalLayout Then
            Dim heights As New List(Of Integer)()
            For Each item As ChromeItem In itemList
                If item.Visible AndAlso item IsNot overflowItem AndAlso item IsNot pinButton Then heights.Add(RowHeight(item))
            Next
            Dim total As Integer = 0
            ChromeMenuRules.StackRows(heights, pad, total)
            Return New Size(If(slots.Count = 0, Px(ChromeMetrics.MenuMinWidth), slots.Max(Function(slot) slot.Width)) + 2 * pad, total)
        End If
        Return New Size(ChromeLayout.NaturalWidth(slots, Px(ChromeMetrics.Gap)) + PinReserve() + 2 * pad, ItemHeight() + 2 * verticalPad)
    End Function

    ''' <summary>The narrowest this horizontal row may get without losing its priority core (188 §3.5):
    ''' <see cref="ChromeLayout.PinnedCoreWidth"/> over the panel's own slots. A host narrower than this
    ''' has hidden pinned content; the placer wraps the row below an obstacle instead.</summary>
    Public Function MinimumRowWidth() As Integer
        If VerticalLayout Then Return 0
        Return ChromeLayout.PinnedCoreWidth(BuildSlots(Nothing), Px(ChromeMetrics.Gap), ItemHeight())
    End Function

    Private Function BuildSlots(indexMap As List(Of ChromeItem)) As List(Of ChromeSlot)
        Dim slots As New List(Of ChromeSlot)()
        For Each item As ChromeItem In itemList
            If item Is overflowItem OrElse item Is pinButton Then Continue For
            ' A conditional button that is away still holds its place (empty air) in a horizontal row (125 UX-313b).
            Dim away As Boolean = Not item.Visible
            If away AndAlso (Not item.KeepsSlotWhenHidden OrElse VerticalLayout) Then Continue For
            slots.Add(New ChromeSlot(item.Id, MeasureItemWidth(item), If(item.MinimumWidth > 0, Px(item.MinimumWidth), -1),
                pinned:=Not away AndAlso (item.Id = "cmbox_Media_Folder" OrElse item.Id = "chkbox_Top_Most" OrElse item.Id = "cmbox_Sort"),
                soft:=away))
            If indexMap IsNot Nothing Then indexMap.Add(item)
        Next
        ' Air with no visible item after it holds nothing in place: it is not kept (and so never widens the row).
        While slots.Count > 0 AndAlso slots(slots.Count - 1).Soft
            slots.RemoveAt(slots.Count - 1)
            If indexMap IsNot Nothing Then indexMap.RemoveAt(indexMap.Count - 1)
        End While
        Return slots
    End Function

    ''' <summary>Places every visible item. Called when the items, the scale, the fonts or the size change.</summary>
    Public Sub LayoutItems()
        itemList.Remove(overflowItem)
        itemList.RemoveAll(Function(item) item.IsPin)
        overflowList.Clear()
        Dim order As New List(Of ChromeItem)()
        Dim slots As List(Of ChromeSlot) = BuildSlots(order)
        For Each item As ChromeItem In itemList
            item.Bounds = Rectangle.Empty
        Next
        If PinItem IsNot Nothing Then PinItem.Bounds = Rectangle.Empty
        If order.Count = 0 Then
            RaiseEvent LayoutChanged(Me, EventArgs.Empty)
            Return
        End If

        Dim pad As Integer = PanelPadding()
        Dim verticalPad As Integer = PanelPaddingVertical()
        Dim cornerReserve As Integer = PinReserve()
        Dim available As Integer = Math.Max(0, ClientSize.Width - 2 * pad - cornerReserve)
        ' The row sits beside the corner pin: after it in a right-to-left panel (the pin is on the left),
        ' and always after it when the pin leads the row (the window-controls panel, 188 §3.1).
        Dim rowStart As Integer = pad + If(PinAtStart OrElse isRightToLeft, cornerReserve, 0)
        If VerticalLayout Then
            Dim heights As New List(Of Integer)()
            For Each entry As ChromeItem In order
                heights.Add(RowHeight(entry))
            Next
            Dim stackTotal As Integer = 0
            Dim tops As Integer() = ChromeMenuRules.StackRows(heights, pad, stackTotal)
            For index As Integer = 0 To order.Count - 1
                order(index).Bounds = New Rectangle(pad, tops(index), available, heights(index))
            Next
            Invalidate()
            RaiseEvent LayoutChanged(Me, EventArgs.Empty)
            Return
        End If
        Dim row As ChromeRowLayout = ChromeLayout.ArrangeRow(slots, available, ItemHeight(), Px(ChromeMetrics.Gap),
                                                              ItemHeight(), isRightToLeft)
        If StatusLayout AndAlso slots.Count = 2 Then
            Dim contentWidth As Integer = Math.Max(0, available - Px(ChromeMetrics.Gap))
            Dim captionPad As Integer = 2 * Px(ChromeMetrics.CaptionPadding)
            Dim statusWidth As Integer = ChromeLayout.StatusCellWidth(contentWidth,
                TextRenderer.MeasureText(order(0).Caption, If(textFont, Font)).Width + captionPad,
                TextRenderer.MeasureText(order(1).Caption, If(textFont, Font)).Width + captionPad)
            Dim fileWidth As Integer = contentWidth - statusWidth
            slots = New List(Of ChromeSlot) From {
                New ChromeSlot(order(0).Id, fileWidth), New ChromeSlot(order(1).Id, statusWidth)}
            row = ChromeLayout.ArrangeRow(slots, available, ItemHeight(), Px(ChromeMetrics.Gap), ItemHeight(), isRightToLeft)
        End If
        Dim top As Integer = Math.Max(verticalPad, (ClientSize.Height - ItemHeight()) \ 2)
        For i As Integer = 0 To order.Count - 1
            ' A kept place (the item is away) is air: no bounds, never in the menu.
            If Not order(i).Visible Then Continue For
            If row.Overflowed(i) Then
                overflowList.Add(order(i))
                Continue For
            End If
            Dim r As Rectangle = row.Rects(i)
            order(i).Bounds = New Rectangle(rowStart + r.X, top, r.Width, r.Height)
        Next
        If row.HasOverflow Then
            overflowItem.Bounds = New Rectangle(rowStart + row.OverflowButton.X, top, row.OverflowButton.Width, row.OverflowButton.Height)
            overflowItem.AccessibleName = Localization.T("Ещё - кнопки, не поместившиеся в строку.")
            overflowItem.Tooltip = overflowItem.AccessibleName
            itemList.Add(overflowItem)
        End If
        If PinItem IsNot Nothing AndAlso cornerReserve > 0 Then
            Dim pinWidth As Integer = cornerReserve - Px(ChromeMetrics.Gap)
            If PinAtStart Then
                PinItem.Bounds = New Rectangle(pad, top, pinWidth, ItemHeight())
            Else
                PinItem.Bounds = New Rectangle(If(isRightToLeft, pad, ClientSize.Width - pad - pinWidth), top, pinWidth, ItemHeight())
            End If
            itemList.Add(PinItem)
        End If
        Invalidate()
        RaiseEvent LayoutChanged(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        LayoutItems()
    End Sub

    Private Function FontFor(item As ChromeItem) As Font
        Return If(item.CaptionIsSymbol, If(symbolFont, textFont), textFont)
    End Function

    ' --- painting ---------------------------------------------------------------------------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        g.Clear(chromeTheme.PanelFill)

        ' The hairline: the panel's edge. The host clips the corners with the same radius.
        Dim corner As Integer = Px(chromeTheme.PanelCornerRadius)
        Using path As GraphicsPath = RoundedRect(New RectangleF(0.5F, 0.5F, Width - 1.0F, Height - 1.0F), corner)
            Using pen As New Pen(chromeTheme.Hairline, 1.0F)
                g.DrawPath(pen, path)
            End Using
        End Using

        For Each item As ChromeItem In itemList
            If item.Visible AndAlso Not item.Bounds.IsEmpty Then
                PaintItemFill(g, item)
                ' The path field owns its text. GDI captions underneath it can spill past the child
                ' window's edge, leaving a second line of fragments below the folder address.
                If Not inkExternalMode AndAlso Not IsCoveredByInkChild(item.Bounds) Then PaintItemInk(g, item)
            End If
        Next
        If Not inkExternalMode Then PaintScrollCues(g)
        If inkExternalMode Then RaiseEvent InkNeeded(Me, EventArgs.Empty)
    End Sub

    ''' <summary>A menu taller than its window scrolls; this tells it where the visible part is, so a cue
    ''' (a small arrow at the edge that has more behind it) can be painted (125 UX-329).</summary>
    Public Sub SetViewport(offset As Integer, height As Integer)
        If offset = viewOffset AndAlso height = viewHeight Then Return
        viewOffset = offset
        viewHeight = height
        Invalidate()
    End Sub

    Private Sub PaintScrollCues(g As Graphics)
        If Not VerticalLayout OrElse viewHeight <= 0 Then Return
        Dim above As Boolean
        Dim below As Boolean
        ChromeMenuRules.ScrollCues(viewOffset, Height, viewHeight, above, below)
        If Not above AndAlso Not below Then Return
        Dim cue As Integer = Px(ChromeMetrics.ScrollCueHeight)
        Dim ink As Color = chromeTheme.Foreground
        If above Then PaintScrollCue(g, New Rectangle(0, viewOffset, Width, cue), ChrW(&H25B4), ink)
        If below Then PaintScrollCue(g, New Rectangle(0, viewOffset + viewHeight - cue, Width, cue), ChrW(&H25BE), ink)
    End Sub

    Private Sub PaintScrollCue(g As Graphics, area As Rectangle, symbol As Char, ink As Color)
        Using brush As New SolidBrush(chromeTheme.PanelFill)
            g.FillRectangle(brush, area)
        End Using
        PaintAccessory(g, symbol, area, ink)
    End Sub

    ''' <summary>
    ''' Everything the panel shows that is NOT fill: glyphs, captions, the focus ring. Drawn onto a
    ''' transparent surface with per-pixel alpha, origin at this control's top-left, so the host can
    ''' present it fully opaque over a translucent panel. Items an own-painting child control sits
    ''' on (the path box) are skipped in both paint modes: the child owns their text.
    ''' </summary>
    Friend Sub PaintInk(g As Graphics)
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.PixelOffsetMode = PixelOffsetMode.HighQuality
        ' GDI text writes no alpha, so the layered paint uses GDI+'s own grayscale antialiasing.
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
        inkPainting = True
        Try
            For Each item As ChromeItem In itemList
                If item.Visible AndAlso Not item.Bounds.IsEmpty AndAlso Not IsCoveredByInkChild(item.Bounds) Then PaintItemInk(g, item)
            Next
            PaintScrollCues(g)
            For Each child As Control In Controls
                Dim source As IChromeInkSource = TryCast(child, IChromeInkSource)
                If source Is Nothing OrElse Not child.Visible Then Continue For
                Dim state As GraphicsState = g.Save()
                Try
                    g.TranslateTransform(child.Left, child.Top)
                    g.IntersectClip(New Rectangle(0, 0, child.Width, child.Height))
                    source.PaintInk(g)
                Finally
                    g.Restore(state)
                End Try
            Next
        Finally
            inkPainting = False
        End Try
    End Sub

    Private Function IsCoveredByInkChild(bounds As Rectangle) As Boolean
        For Each child As Control In Controls
            If child.Visible AndAlso TypeOf child Is IChromeInkSource AndAlso child.Bounds = bounds Then Return True
        Next
        Return False
    End Function

    Private Sub PaintItemFill(g As Graphics, item As ChromeItem)
        Dim r As Rectangle = item.Bounds
        Dim itemCorner As Integer = Px(ChromeMetrics.ItemCorner)

        If item.IsPin Then
            ' The pin in the panel's corner is an ambient, low-profile control:
            ' no bright accent fill at rest, only a small subtle hover/press cue.
            If item.HoverLevel <= 0.0F AndAlso item.PressLevel <= 0.0F Then Return
            Dim size As Integer = Math.Min(r.Width, Math.Min(r.Height, Px(ChromeMetrics.PinButtonSize)))
            Dim box As New Rectangle(r.X + (r.Width - size) \ 2, r.Y + (r.Height - size) \ 2, size, size)
            Dim pinFill As Color = chromeTheme.ItemHover
            If item.PressLevel > 0.0F Then
                pinFill = ChromeTheme.Blend(pinFill, chromeTheme.ItemPressed, item.PressLevel)
            End If
            pinFill = ChromeTheme.Blend(chromeTheme.PanelFill, pinFill, Math.Max(item.HoverLevel, item.PressLevel))
            Using path As GraphicsPath = RoundedRect(New RectangleF(box.X, box.Y, box.Width, box.Height), itemCorner)
                Using brush As New SolidBrush(pinFill)
                    g.FillPath(brush, path)
                End Using
            End Using
            Return
        End If

        Dim rest As Color = chromeTheme.FillFor(item.Enabled, item.Checked, item.Destructive, False, False)
        Dim hot As Color = chromeTheme.FillFor(item.Enabled, item.Checked, item.Destructive, True, False)
        Dim down As Color = chromeTheme.FillFor(item.Enabled, item.Checked, item.Destructive, True, True)
        Dim fill As Color = If(rest.IsEmpty, chromeTheme.PanelFill, rest)
        Dim touched As Boolean = Not rest.IsEmpty
        If item.HoverLevel > 0.0F AndAlso Not hot.IsEmpty Then
            fill = ChromeTheme.Blend(fill, hot, item.HoverLevel) : touched = True
        End If
        If item.PressLevel > 0.0F AndAlso Not down.IsEmpty Then
            fill = ChromeTheme.Blend(fill, down, item.PressLevel) : touched = True
        End If
        If touched Then
            Using path As GraphicsPath = RoundedRect(New RectangleF(r.X, r.Y, r.Width, r.Height), itemCorner)
                Using brush As New SolidBrush(fill)
                    g.FillPath(brush, path)
                End Using
            End Using
        End If
    End Sub

    Private Sub PaintItemInk(g As Graphics, item As ChromeItem)
        Dim bounds As Rectangle = item.Bounds
        If item.IsSeparator Then
            PaintSeparator(g, bounds)
            Return
        End If
        ' What the glyph or caption is laid out in: the item, less the strip a menu button keeps for its chevron.
        Dim r As Rectangle = bounds
        Dim chevronRoom As Integer = If(ShowsMenuChevron(item), ChevronStrip(), 0)
        If chevronRoom > 0 AndAlso r.Width > chevronRoom Then r = New Rectangle(If(isRightToLeft, r.X + chevronRoom, r.X), r.Y, r.Width - chevronRoom, r.Height)
        Dim itemCorner As Integer = Px(ChromeMetrics.ItemCorner)
        Dim ink As Color = chromeTheme.InkFor(item.Enabled, item.Checked, item.Destructive, item.HoverLevel > 0.5F)
        ' A status line that carries a warning or an error is written in the theme's role colour, contrast-checked
        ' against the panel (124 UX-206, Q124-2); under high contrast the system pair is left alone.
        If item.Tone <> NotifyLevel.Info AndAlso item.Enabled AndAlso Not chromeTheme.IsHighContrast Then
            ink = NotifyRules.InkOnSurface(item.Tone, chromeTheme.PanelFill, chromeTheme.Foreground)
        End If
        Dim drawn As Boolean = False
        If item.IsPin Then
            Dim pinInk As Color
            If chromeTheme.IsHighContrast Then
                pinInk = If(item.Checked, SystemColors.HighlightText, SystemColors.WindowText)
            Else
                Dim restInk As Color = chromeTheme.PinRestInk(item.Checked)
                pinInk = If(item.HoverLevel > 0.0F, ChromeTheme.Blend(restInk, chromeTheme.Foreground, item.HoverLevel), restInk)
            End If
            PaintPin(g, item.Checked, r, pinInk, currentScale)
            drawn = True
        ElseIf item.CustomGlyph = ChromeCustomGlyph.OnTop Then
            PaintOnTop(g, item.Checked, r, ink, currentScale)
            drawn = True
        ElseIf item.CustomGlyph = ChromeCustomGlyph.WindowMinimize Then
            PaintWindowMinimize(g, r, ink, currentScale)
            drawn = True
        ElseIf item.CustomGlyph = ChromeCustomGlyph.WindowMaximize Then
            PaintWindowMaximize(g, r, ink, currentScale, False)
            drawn = True
        ElseIf item.CustomGlyph = ChromeCustomGlyph.WindowRestore Then
            PaintWindowMaximize(g, r, ink, currentScale, True)
            drawn = True
        ElseIf item.CustomGlyph = ChromeCustomGlyph.WindowClose Then
            PaintWindowClose(g, r, ink, currentScale)
            drawn = True
        ElseIf item.Kind = ChromeItemKind.Toggle AndAlso item.GlyphId.Length = 0 AndAlso item.Caption.Length = 0 Then
            Dim side As Integer = Px(ChromeMetrics.GlyphSize)
            Dim box As New Rectangle(r.X + (r.Width - side) \ 2, r.Y + (r.Height - side) \ 2, side, side)
            Using pen As New Pen(ink, Math.Max(1, Px(ChromeMetrics.FocusRingWidth)))
                g.DrawRectangle(pen, box)
                If item.Checked Then
                    g.DrawLine(pen, box.Left, box.Top + side \ 2, box.Left + side \ 3, box.Bottom)
                    g.DrawLine(pen, box.Left + side \ 3, box.Bottom, box.Right, box.Top)
                End If
            End Using
            drawn = True
        End If
        If item.Image IsNot Nothing Then
            Dim size As Integer = Px(ChromeMetrics.GlyphSize)
            g.DrawImage(item.Image, New Rectangle(If(VerticalLayout, If(isRightToLeft, r.Right - ItemHeight(), r.Left) + (ItemHeight() - size) \ 2, r.X + (r.Width - size) \ 2), r.Y + (r.Height - size) \ 2, size, size))
            drawn = True
        End If
        If Not drawn AndAlso item.GlyphId.Length > 0 Then drawn = PaintGlyph(g, item, r, ink)
        If VerticalLayout AndAlso (item.Hint.Length > 0 OrElse item.Chevron) Then r = PaintMenuRowTail(g, item, r, ink)
        If (VerticalLayout OrElse item.ShowCaptionWithGlyph) AndAlso drawn Then
            Dim glyphWidth As Integer = ItemHeight()
            Dim captionRect As New Rectangle(r.X + If(isRightToLeft, 0, glyphWidth), r.Y, Math.Max(0, r.Width - glyphWidth), r.Height)
            PaintCaption(g, item, captionRect, ink)
        ElseIf Not drawn AndAlso item.Caption.Length > 0 Then
            PaintCaption(g, item, r, ink)
        End If
        If chevronRoom > 0 Then PaintAccessory(g, ChrW(&H25BE), New Rectangle(If(isRightToLeft, bounds.X, bounds.Right - chevronRoom), bounds.Y, chevronRoom, bounds.Height), ink)

        If item.Checked AndAlso chromeTheme.IsHighContrast Then
            Using pen As New Pen(chromeTheme.Foreground, Math.Max(1, Px(ChromeMetrics.FocusRingWidth)))
                Dim box As Rectangle = Rectangle.Inflate(r, -Px(ChromeMetrics.FocusRingInset), -Px(ChromeMetrics.FocusRingInset))
                g.DrawRectangle(pen, box)
            End Using
        End If
        If item.SecondaryAction IsNot Nothing Then PaintSecondaryMark(g, bounds, ink, currentScale, isRightToLeft)
        If inKeyboardMode AndAlso item Is focusedItem Then
            Dim inset As Integer = Px(ChromeMetrics.FocusRingInset)
            Dim ring As Rectangle = Rectangle.Inflate(bounds, -inset + 1, -inset + 1)
            Dim ringWidth As Integer = Math.Max(1, Px(ChromeMetrics.FocusRingWidth))
            ' Two contours (UX-105 of 123): the foreground ink outside and the accent inside. A checked button
            ' (the slideshow, "on top") is filled with the accent and a bluish panel scheme is close to it, so
            ' a lone accent ring merged with the fill or the panel and the focus vanished; the ink contour
            ' always differs from both.
            Using outerPath As GraphicsPath = RoundedRect(New RectangleF(ring.X, ring.Y, ring.Width, ring.Height), itemCorner)
                Using pen As New Pen(chromeTheme.Foreground, ringWidth)
                    g.DrawPath(pen, outerPath)
                End Using
            End Using
            Dim innerRing As Rectangle = Rectangle.Inflate(ring, -ringWidth, -ringWidth)
            If innerRing.Width > 2 AndAlso innerRing.Height > 2 Then
                Using innerPath As GraphicsPath = RoundedRect(New RectangleF(innerRing.X, innerRing.Y, innerRing.Width, innerRing.Height), Math.Max(0, itemCorner - ringWidth))
                    Using pen As New Pen(chromeTheme.FocusRing, ringWidth)
                        g.DrawPath(pen, innerPath)
                    End Using
                End Using
            End If
        End If
    End Sub

    ''' <summary>The corner tick of an item that answers a right click (125 UX-306). Shared with the path box,
    ''' whose element is covered by a control of its own.</summary>
    Friend Shared Sub PaintSecondaryMark(g As Graphics, r As Rectangle, ink As Color, scale As Single, rightToLeft As Boolean)
        Dim size As Integer = UiScale.Px(ChromeMetrics.SecondaryMarkSize, scale)
        Dim gap As Integer = UiScale.Px(ChromeMetrics.Gap, scale)
        Dim x As Integer = If(rightToLeft, r.Left + gap \ 2, r.Right - size - gap \ 2)
        Dim y As Integer = r.Bottom - size - gap \ 2
        Using pen As New Pen(ink, Math.Max(1.0F, scale))
            g.DrawLine(pen, x, y, x + size \ 2, y + size \ 2)
            g.DrawLine(pen, x + size \ 2, y + size \ 2, x + size, y)
        End Using
    End Sub

    ''' <summary>"Always on top" from lines: two stacked windows, the front one filled while the mode is on
    ''' and only outlined while it is off. The catalogue has no glyph for it yet (125 UX-311).</summary>
    Friend Shared Sub PaintOnTop(g As Graphics, onTop As Boolean, r As Rectangle, ink As Color, scale As Single)
        Dim side As Integer = UiScale.Px(ChromeMetrics.GlyphSize, scale)
        Dim unit As Single = side / 16.0F
        Dim cx As Single = r.X + r.Width / 2.0F
        Dim cy As Single = r.Y + r.Height / 2.0F
        Dim back As New RectangleF(cx - 7.0F * unit, cy - 7.0F * unit, 10.0F * unit, 9.0F * unit)
        Dim front As New RectangleF(cx - 3.0F * unit, cy - 2.0F * unit, 10.0F * unit, 9.0F * unit)
        Using pen As New Pen(ink, Math.Max(1.0F, 1.2F * unit))
            g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height)
            If onTop Then
                Using brush As New SolidBrush(ink)
                    g.FillRectangle(brush, front)
                End Using
            Else
                g.DrawRectangle(pen, front.X, front.Y, front.Width, front.Height)
            End If
        End Using
    End Sub

    ''' <summary>The caption row's commands, drawn from lines like <see cref="PaintOnTop"/> (the catalogue
    ''' has no glyphs for them yet, 188 §5). They mean the physical window, so nothing here mirrors.</summary>
    Friend Shared Sub PaintWindowMinimize(g As Graphics, r As Rectangle, ink As Color, scale As Single)
        Dim side As Integer = UiScale.Px(ChromeMetrics.GlyphSize, scale)
        Dim unit As Single = side / 16.0F
        Dim cx As Single = r.X + r.Width / 2.0F
        Dim cy As Single = r.Y + r.Height / 2.0F
        Using pen As New Pen(ink, Math.Max(1.0F, 1.4F * unit))
            g.DrawLine(pen, cx - 5.0F * unit, cy + 4.0F * unit, cx + 5.0F * unit, cy + 4.0F * unit)
        End Using
    End Sub

    ''' <summary>Maximize: one square. <paramref name="restored"/> draws the restore pair instead -
    ''' the front square outlined in full, the back one as its top and right edges, so the pair reads
    ''' without any occlusion fill (188 §5).</summary>
    Friend Shared Sub PaintWindowMaximize(g As Graphics, r As Rectangle, ink As Color, scale As Single, restored As Boolean)
        Dim side As Integer = UiScale.Px(ChromeMetrics.GlyphSize, scale)
        Dim unit As Single = side / 16.0F
        Dim cx As Single = r.X + r.Width / 2.0F
        Dim cy As Single = r.Y + r.Height / 2.0F
        Using pen As New Pen(ink, Math.Max(1.0F, 1.2F * unit))
            If Not restored Then
                g.DrawRectangle(pen, cx - 5.0F * unit, cy - 5.0F * unit, 10.0F * unit, 10.0F * unit)
                Return
            End If
            Dim backX As Single = cx - 3.0F * unit
            Dim backY As Single = cy - 6.0F * unit
            Dim backRight As Single = cx + 5.0F * unit
            Dim backBottom As Single = cy + 2.0F * unit
            g.DrawLine(pen, backX, backY, backRight, backY)
            g.DrawLine(pen, backRight, backY, backRight, backBottom)
            g.DrawRectangle(pen, cx - 5.0F * unit, cy - 4.0F * unit, 8.0F * unit, 8.0F * unit)
        End Using
    End Sub

    ''' <summary>Close: the diagonal cross.</summary>
    Friend Shared Sub PaintWindowClose(g As Graphics, r As Rectangle, ink As Color, scale As Single)
        Dim side As Integer = UiScale.Px(ChromeMetrics.GlyphSize, scale)
        Dim unit As Single = side / 16.0F
        Dim cx As Single = r.X + r.Width / 2.0F
        Dim cy As Single = r.Y + r.Height / 2.0F
        Using pen As New Pen(ink, Math.Max(1.0F, 1.4F * unit))
            g.DrawLine(pen, cx - 4.5F * unit, cy - 4.5F * unit, cx + 4.5F * unit, cy + 4.5F * unit)
            g.DrawLine(pen, cx + 4.5F * unit, cy - 4.5F * unit, cx - 4.5F * unit, cy + 4.5F * unit)
        End Using
    End Sub

    Private Sub PaintSeparator(g As Graphics, r As Rectangle)
        Dim inset As Integer = Px(ChromeMetrics.CaptionPadding)
        Dim y As Integer = r.Y + r.Height \ 2
        Using pen As New Pen(chromeTheme.Hairline, 1.0F)
            g.DrawLine(pen, r.Left + inset, y, r.Right - inset, y)
        End Using
    End Sub

    ''' <summary>A small symbol in the symbol font, centred in a strip: a menu button's chevron, a submenu
    ''' row's arrow.</summary>
    Private Sub PaintAccessory(g As Graphics, symbol As Char, area As Rectangle, ink As Color)
        Dim font As Font = If(symbolFont, textFont)
        If font Is Nothing OrElse area.Width <= 0 Then Return
        If inkPainting Then
            DrawCaptionWithAlpha(g, symbol.ToString(), font, area, ink, True, False)
        Else
            TextRenderer.DrawText(g, symbol.ToString(), font, area, ink, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix)
        End If
    End Sub

    ''' <summary>Paints a menu row's key hint (dimmed) and its submenu arrow at the end of the row, and returns
    ''' the part of the row left for the caption.</summary>
    Private Function PaintMenuRowTail(g As Graphics, item As ChromeItem, r As Rectangle, ink As Color) As Rectangle
        Dim font As Font = FontFor(item)
        Dim pad As Integer = Px(ChromeMetrics.CaptionPadding)
        Dim remaining As Rectangle = r
        If item.Chevron Then
            Dim strip As Integer = ChevronStrip()
            Dim arrow As Char = If(isRightToLeft, ChrW(&H25C2), ChrW(&H25B8))
            PaintAccessory(g, arrow, New Rectangle(If(isRightToLeft, r.Left, r.Right - strip), r.Y, strip, r.Height), ink)
            remaining = New Rectangle(If(isRightToLeft, r.X + strip, r.X), r.Y, Math.Max(0, r.Width - strip), r.Height)
        End If
        If item.Hint.Length > 0 AndAlso font IsNot Nothing Then
            Dim hintWidth As Integer = TextRenderer.MeasureText(item.Hint, font, New Size(Integer.MaxValue, Integer.MaxValue),
                TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix).Width
            Dim hintInk As Color = If(item.Enabled, ChromeTheme.Blend(If(item.Destructive, chromeTheme.DestructiveForeground, chromeTheme.Foreground), chromeTheme.PanelFill, 0.45F), ink)
            Dim hintRect As New Rectangle(If(isRightToLeft, remaining.Left + pad, remaining.Right - pad - hintWidth), r.Y, hintWidth, r.Height)
            If inkPainting Then
                DrawCaptionWithAlpha(g, item.Hint, font, hintRect, hintInk, False, False)
            Else
                TextRenderer.DrawText(g, item.Hint, font, hintRect, hintInk, TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            End If
            remaining = New Rectangle(If(isRightToLeft, remaining.X + hintWidth + 2 * pad, remaining.X), remaining.Y, Math.Max(0, remaining.Width - hintWidth - 2 * pad), remaining.Height)
        End If
        Return remaining
    End Function

    ''' <summary>The catalog's push-pin, in the item's ink, centred in <paramref name="r"/>: the filled
    ''' <c>action.pin</c> while the panel is pinned and its outlined state variant <c>action.pin--off</c>
    ''' while it is free (ICON-SET: "an outlined pin means it is not pinned"). Upright in both states: the
    ''' line-drawn pin this replaced was tilted 45 degrees while free, which on a small button is the very
    ''' picture of the edit pencil beside it on the recipients table's header. Shared so the recipients
    ''' overlay's own pin button draws the very same glyph.</summary>
    Friend Shared Sub PaintPin(g As Graphics, pinned As Boolean, r As Rectangle, ink As Color, scale As Single)
        Dim size As Integer = Math.Max(8, UiScale.Px(ChromeMetrics.PinGlyphSize, scale))
        Dim glyphId As String = If(pinned, "action.pin", "action.pin--off")
        Try
            Using glyph As Bitmap = GlyphRenderer.Render(glyphId, size, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
                Dim x As Integer = r.X + (r.Width - size) \ 2
                Dim y As Integer = r.Y + (r.Height - size) \ 2
                g.DrawImage(glyph, New Rectangle(x, y, size, size), 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel)
            End Using
        Catch ex As Exception
            ' A glyph that cannot be drawn must not take the panel down; the button simply paints nothing.
            Debug.WriteLine("w2600: chrome glyph " & glyphId & " failed: " & ex.Message)
        End Try
    End Sub

    Private Function PaintGlyph(g As Graphics, item As ChromeItem, r As Rectangle, ink As Color) As Boolean
        Dim size As Integer = Px(ChromeMetrics.GlyphSize)
        Dim bitmap As Bitmap = Nothing
        Try
            bitmap = GlyphRenderer.Render(item.GlyphId, size, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
            Dim x As Integer = If(VerticalLayout OrElse item.ShowCaptionWithGlyph, If(isRightToLeft, r.Right - ItemHeight(), r.Left) + (ItemHeight() - size) \ 2, r.X + (r.Width - size) \ 2)
            Dim y As Integer = r.Y + (r.Height - size) \ 2
            If item.MirrorInRtl AndAlso isRightToLeft Then
                ' "Back" and "forward" follow the reading direction; every other glyph is left alone.
                Dim state As GraphicsState = g.Save()
                g.TranslateTransform(x + size, y)
                g.ScaleTransform(-1.0F, 1.0F)
                g.DrawImage(bitmap, 0, 0, size, size)
                g.Restore(state)
            Else
                g.DrawImage(bitmap, x, y, size, size)
            End If
            Return True
        Catch ex As Exception
            ' A glyph that cannot be drawn must not take the panel down: the caption stands in.
            Debug.WriteLine("w2600: chrome glyph " & item.GlyphId & " failed: " & ex.Message)
            Return False
        Finally
            If bitmap IsNot Nothing Then bitmap.Dispose()
        End Try
    End Function

    Private Sub PaintCaption(g As Graphics, item As ChromeItem, r As Rectangle, ink As Color)
        Dim font As Font = FontFor(item)
        If font Is Nothing Then Return
        Dim isLeftAligned As Boolean = item.AlignLeft OrElse VerticalLayout OrElse item.ShowCaptionWithGlyph
        Dim flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or
                                       TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                                       TextFormatFlags.NoPadding
        If isLeftAligned Then
            flags = flags Or If(isRightToLeft, TextFormatFlags.Right, TextFormatFlags.Left)
        Else
            flags = flags Or TextFormatFlags.HorizontalCenter
        End If
        If isRightToLeft AndAlso Not item.CaptionIsSymbol Then flags = flags Or TextFormatFlags.RightToLeft

        Dim textRect As Rectangle = r
        If item.AlignLeft AndAlso Not item.ShowCaptionWithGlyph Then
            Dim hPad As Integer = Px(ChromeMetrics.CaptionPadding)
            textRect = New Rectangle(r.X + hPad, r.Y, Math.Max(0, r.Width - 2 * hPad), r.Height)
        End If

        If inkPainting Then
            DrawCaptionWithAlpha(g, item.Caption, font, textRect, ink, (flags And TextFormatFlags.HorizontalCenter) <> 0,
                                 (flags And TextFormatFlags.RightToLeft) <> 0)
            Return
        End If
        TextRenderer.DrawText(g, item.Caption, font, textRect, ink, flags)
    End Sub

    ''' <summary>The caption on a transparent surface: GDI text (TextRenderer) leaves the alpha channel
    ''' empty, so it would vanish there. Same placement as the ordinary paint - centred, or on the
    ''' start edge in the reading direction; one line, ellipsis at the end, no prefix, no padding.</summary>
    Private Sub DrawCaptionWithAlpha(g As Graphics, text As String, font As Font, r As Rectangle, ink As Color,
                                     centered As Boolean, readsRightToLeft As Boolean)
        Using format As New StringFormat(StringFormat.GenericTypographic)
            format.FormatFlags = StringFormatFlags.NoWrap Or StringFormatFlags.LineLimit
            If readsRightToLeft Then format.FormatFlags = format.FormatFlags Or StringFormatFlags.DirectionRightToLeft
            format.Trimming = StringTrimming.EllipsisCharacter
            format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None
            format.LineAlignment = StringAlignment.Center
            ' In a right-to-left StringFormat "near" is the right edge; the ordinary paint puts a
            ' start-aligned caption on the right in a right-to-left panel and on the left otherwise.
            If centered Then
                format.Alignment = StringAlignment.Center
            ElseIf isRightToLeft Then
                format.Alignment = If(readsRightToLeft, StringAlignment.Near, StringAlignment.Far)
            Else
                format.Alignment = StringAlignment.Near
            End If
            Using brush As New SolidBrush(ink)
                g.DrawString(text, font, brush, New RectangleF(r.X, r.Y, r.Width, r.Height), format)
            End Using
        End Using
    End Sub

    Friend Shared Function RoundedRect(r As RectangleF, radius As Integer) As GraphicsPath
        Dim path As New GraphicsPath()
        Dim d As Single = Math.Min(radius * 2.0F, Math.Min(r.Width, r.Height))
        If d <= 0.0F Then
            path.AddRectangle(r)
            Return path
        End If
        path.AddArc(r.X, r.Y, d, d, 180, 90)
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    ' --- pointer ----------------------------------------------------------------------------

    Public Function HitTest(point As Point) As ChromeItem
        For Each item As ChromeItem In itemList
            If item.Visible AndAlso Not item.IsSeparator AndAlso Not item.Bounds.IsEmpty AndAlso item.Bounds.Contains(point) Then Return item
        Next
        Return Nothing
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim item As ChromeItem = HitTest(e.Location)
        ' A move grip is empty panel (188): it drags on press, but shows no hover fill and no tooltip.
        If item IsNot Nothing AndAlso item.IsMoveGrip Then item = Nothing
        SetHot(If(item IsNot Nothing AndAlso item.Enabled, item, Nothing))
        ' Disabled actions still explain their purpose, without a hover animation.
        UpdateTip(item)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        If pressedItem Is Nothing Then SetHot(Nothing)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button <> MouseButtons.Left Then Return
        Dim item As ChromeItem = HitTest(e.Location)
        ' A press on the free background or on a move-grip slot belongs to the panel's owner -
        ' the window drag (188 §5): no capture, no press level, no action, and the second press
        ' of a double click is told apart by the shared rule.
        If item Is Nothing OrElse item.IsMoveGrip Then
            NoteMoveGripPress()
            Return
        End If
        If Not item.Enabled Then Return
        pressedItem = item
        holdElapsed = 0
        holdInvoked = False
        ' A slow mouse click is not a different command (125 UX-307): the long press is for touch and pen.
        holdArmed = ChromeMenuRules.IsTouchOrPenExtraInfo(If(PressExtraInfo Is Nothing, GetMessageExtraInfo().ToInt64(), PressExtraInfo.Invoke()))
        item.IsPressed = True
        Capture = True
        RefreshLevels()
    End Sub

    ''' <summary>One press meant for the owner's move grip: a plain press or, within the system's
    ''' double-click time and rectangle of the previous one, the double click.</summary>
    Private Sub NoteMoveGripPress()
        Dim nowTick As Integer = Environment.TickCount
        Dim nowScreen As Point = Cursor.Position
        Dim isDoubleClick As Boolean = ChromeMenuRules.IsDoubleClickPress(
            lastMovePressTick, nowTick, nowScreen.X - lastMovePressScreen.X, nowScreen.Y - lastMovePressScreen.Y)
        lastMovePressTick = nowTick
        lastMovePressScreen = nowScreen
        If isDoubleClick Then
            RaiseEvent MoveGripDoubleClick(Me, EventArgs.Empty)
        Else
            RaiseEvent MoveGripPress(Me, EventArgs.Empty)
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        Dim item As ChromeItem = HitTest(e.Location)

        If e.Button = MouseButtons.Left Then
            Dim pressed As ChromeItem = pressedItem
            pressedItem = Nothing
            Capture = False
            If pressed IsNot Nothing Then pressed.IsPressed = False
            RefreshLevels()
            ' A press that slid off the item is not a click, exactly like a Button.
            If Not holdInvoked AndAlso pressed IsNot Nothing AndAlso pressed Is item AndAlso pressed.Enabled Then RunAction(pressed, pressed.Action)
        ElseIf e.Button = MouseButtons.Right Then
            If item IsNot Nothing AndAlso item.Enabled AndAlso item.SecondaryAction IsNot Nothing Then RunAction(item, item.SecondaryAction)
        End If

        ' After the press the pointer may no longer be over anything (the action moved the panel).
        ' The action may also have disposed this very surface (closing its panel) - then there
        ' is nothing left to update, and PointToClient would throw ObjectDisposedException
        ' into the user's face as an "Internal error" line.
        If pressedItem Is Nothing AndAlso Not IsDisposed Then
            Dim under As ChromeItem = HitTest(PointToClient(Cursor.Position))
            SetHot(If(under IsNot Nothing AndAlso under.Enabled, under, Nothing))
            UpdateTip(under)
        End If
    End Sub

    Private Sub RunAction(item As ChromeItem, act As Action)
        If act Is Nothing Then Return
        Try
            act()
        Catch ex As Exception
            AppFileLogger.LogException("Chrome item " & item.Id, ex)
        End Try
        RaiseEvent ItemInvoked(Me, item)
    End Sub

    Private Sub SetHot(item As ChromeItem)
        If item Is hotItem Then Return
        If hotItem IsNot Nothing Then hotItem.IsHot = False
        hotItem = item
        If hotItem IsNot Nothing Then hotItem.IsHot = True
        RefreshLevels()
        UpdateTip(item)
    End Sub

    Private Sub UpdateTip(item As ChromeItem)
        Dim text As String = If(item Is Nothing, "", item.Tooltip)
        If text = shownTip Then Return
        shownTip = text
        ' The one ToolTip keeps showing its old text while the pointer moves between two items: hide it, so
        ' the next one is raised with the new words (125 UX-313f).
        toolTip.Hide(Me)
        toolTip.SetToolTip(Me, text)
    End Sub

    ' --- easing -----------------------------------------------------------------------------

    ''' <summary>True while any item's hover or press level has not reached its target.</summary>
    Public ReadOnly Property NeedsAnimation As Boolean
        Get
            If pressedItem IsNot Nothing AndAlso pressedItem.SecondaryAction IsNot Nothing AndAlso holdArmed AndAlso Not holdInvoked Then Return True
            For Each item As ChromeItem In itemList
                If item.HoverLevel <> HoverTarget(item) OrElse item.PressLevel <> PressTarget(item) Then Return True
            Next
            Return False
        End Get
    End Property

    Private Shared Function HoverTarget(item As ChromeItem) As Single
        Return If(item.IsHot OrElse item.IsPressed, 1.0F, 0.0F)
    End Function

    Private Shared Function PressTarget(item As ChromeItem) As Single
        Return If(item.IsPressed, 1.0F, 0.0F)
    End Function

    Private Sub RefreshLevels()
        If Not animated Then SettleLevels()
        Invalidate()
    End Sub

    Private Sub SettleLevels()
        For Each item As ChromeItem In itemList
            item.HoverLevel = HoverTarget(item)
            item.PressLevel = PressTarget(item)
        Next
        Invalidate()
    End Sub

    ''' <summary>One step of the ease: every level moves toward its target by elapsed / ease time.</summary>
    Public Sub Animate(elapsedMilliseconds As Integer)
        If pressedItem IsNot Nothing AndAlso pressedItem.SecondaryAction IsNot Nothing AndAlso holdArmed AndAlso Not holdInvoked Then
            holdElapsed += Math.Max(0, elapsedMilliseconds)
            If holdElapsed >= ChromeMetrics.HoldMilliseconds Then
                holdInvoked = True
                Dim source As ChromeItem = pressedItem
                RunAction(source, source.SecondaryAction)
            End If
        End If
        If Not animated Then
            SettleLevels()
            Return
        End If
        Dim stepSize As Single = Math.Max(0.0F, elapsedMilliseconds) / CSng(ChromeMetrics.PressEaseMilliseconds)
        For Each item As ChromeItem In itemList
            item.HoverLevel = Approach(item.HoverLevel, HoverTarget(item), stepSize)
            item.PressLevel = Approach(item.PressLevel, PressTarget(item), stepSize)
        Next
        Invalidate()
    End Sub

    Private Shared Function Approach(current As Single, target As Single, stepSize As Single) As Single
        If current < target Then Return Math.Min(target, current + stepSize)
        If current > target Then Return Math.Max(target, current - stepSize)
        Return current
    End Function

    ' --- the keyboard (focus mode, 002 section 5) ------------------------------------------------

    Private Function IsReachable(item As ChromeItem) As Boolean
        Return item.Kind <> ChromeItemKind.Text AndAlso Not item.IsSeparator AndAlso item.Visible AndAlso item.Enabled AndAlso Not item.Bounds.IsEmpty
    End Function

    ''' <summary>Puts the focus ring on the first (or last) reachable item. False when there is none.</summary>
    Public Function EnterKeyboardMode(Optional last As Boolean = False) As Boolean
        inKeyboardMode = True
        focusedItem = Nothing
        Dim found As Boolean = If(last, MoveFocus(False), MoveFocus(True))
        Invalidate()
        Return found
    End Function

    Public Sub LeaveKeyboardMode()
        inKeyboardMode = False
        focusedItem = Nothing
        Invalidate()
    End Sub

    ''' <summary>The item that has the keyboard ring now, or Nothing (no ring, or none put on an item yet).</summary>
    Public ReadOnly Property FocusedRow As ChromeItem
        Get
            Return If(inKeyboardMode, focusedItem, Nothing)
        End Get
    End Property

    Public ReadOnly Property HasFocusedItem As Boolean
        Get
            Return inKeyboardMode AndAlso focusedItem IsNot Nothing
        End Get
    End Property

    ''' <summary>Puts the keyboard ring on one given item (type-ahead in a menu).</summary>
    Public Sub FocusItem(item As ChromeItem)
        If item Is Nothing OrElse Not IsReachable(item) Then Return
        inKeyboardMode = True
        focusedItem = item
        Invalidate()
        NotifyFocusChanged(item)
    End Sub

    ''' <summary>Moves the ring to the next or previous reachable item in reading order. False when it
    ''' would leave the panel (the controller then moves on to the next panel, or wraps).</summary>
    Public Function MoveFocus(forward As Boolean) As Boolean
        Dim index As Integer = If(focusedItem Is Nothing, If(forward, -1, itemList.Count), itemList.IndexOf(focusedItem))
        Dim stepBy As Integer = If(forward, 1, -1)
        Dim i As Integer = index + stepBy
        While i >= 0 AndAlso i < itemList.Count
            If IsReachable(itemList(i)) Then
                focusedItem = itemList(i)
                Invalidate()
                NotifyFocusChanged(focusedItem)
                Return True
            End If
            i += stepBy
        End While
        Return False
    End Function

    ''' <summary>Runs the focused item's action. False when nothing is focused.</summary>
    Public Function ActivateFocused() As Boolean
        If focusedItem Is Nothing OrElse Not focusedItem.Enabled Then Return False
        RunAction(focusedItem, focusedItem.Action)
        Return True
    End Function

    Public Function ActivateFocusedSecondary() As Boolean
        If focusedItem Is Nothing OrElse Not focusedItem.Enabled OrElse focusedItem.SecondaryAction Is Nothing Then Return False
        RunAction(focusedItem, focusedItem.SecondaryAction)
        Return True
    End Function

    Private Sub NotifyFocusChanged(item As ChromeItem)
        If IsHandleCreated AndAlso item IsNot Nothing Then
            ' The child index is the one GetChild hands out - the list without hidden and overflowed items -
            ' not the position in the full list (125 UX-313g).
            Dim index As Integer = AccessibleItems().IndexOf(item)
            If index >= 0 Then AccessibilityNotifyClients(AccessibleEvents.Focus, index)
        End If
    End Sub

    ' --- accessibility (002 section 5) -------------------------------------------------------------

    Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
        Return New SurfaceAccessibleObject(Me)
    End Function

    Friend Function AccessibleItems() As List(Of ChromeItem)
        Dim result As New List(Of ChromeItem)()
        For Each item As ChromeItem In itemList
            If item.Visible AndAlso Not item.Bounds.IsEmpty Then result.Add(item)
        Next
        Return result
    End Function

    Friend Function AccessibleFor(item As ChromeItem) As ItemAccessibleObject
        Dim existing As ItemAccessibleObject = Nothing
        If Not accessibleChildren.TryGetValue(item, existing) Then
            existing = New ItemAccessibleObject(Me, item)
            accessibleChildren(item) = existing
        End If
        Return existing
    End Function

    Friend Function IsFocusedItem(item As ChromeItem) As Boolean
        Return inKeyboardMode AndAlso item Is focusedItem
    End Function

    Private NotInheritable Class SurfaceAccessibleObject
        Inherits Control.ControlAccessibleObject

        Private ReadOnly surface As ChromeSurface

        Public Sub New(owner As ChromeSurface)
            MyBase.New(owner)
            surface = owner
        End Sub

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return If(surface.VerticalLayout, AccessibleRole.MenuPopup, AccessibleRole.ToolBar)
            End Get
        End Property

        Public Overrides Function GetChildCount() As Integer
            Return surface.AccessibleItems().Count
        End Function

        Public Overrides Function GetChild(index As Integer) As AccessibleObject
            Dim visible As List(Of ChromeItem) = surface.AccessibleItems()
            If index < 0 OrElse index >= visible.Count Then Return Nothing
            Return surface.AccessibleFor(visible(index))
        End Function

        Public Overrides Function HitTest(x As Integer, y As Integer) As AccessibleObject
            Dim item As ChromeItem = surface.HitTest(surface.PointToClient(New Point(x, y)))
            Return If(item Is Nothing, MyBase.HitTest(x, y), surface.AccessibleFor(item))
        End Function
    End Class

    ''' <summary>An item as a screen reader sees it: the localized name, a role that says whether it is
    ''' pressed or toggled, its screen rectangle, and the old control's Name as its description (the
    ''' stable id automation addresses it by).</summary>
    Friend NotInheritable Class ItemAccessibleObject
        Inherits AccessibleObject

        Private ReadOnly surface As ChromeSurface
        Private ReadOnly item As ChromeItem

        Public Sub New(owner As ChromeSurface, owned As ChromeItem)
            surface = owner
            item = owned
        End Sub

        Public Overrides Property Name As String
            Get
                Return If(item.AccessibleName.Length > 0, item.AccessibleName, item.Caption)
            End Get
            Set(value As String)
            End Set
        End Property

        ''' <summary>The longer text (the tooltip, which carries the key), not the technical id a screen reader
        ''' would spell out ("btn_Prev_File") - 125 UX-313g.</summary>
        Public Overrides ReadOnly Property Description As String
            Get
                Dim spoken As String = If(item.AccessibleName.Length > 0, item.AccessibleName, item.Caption)
                Return If(item.Tooltip = spoken, "", item.Tooltip)
            End Get
        End Property

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                If surface.VerticalLayout Then Return AccessibleRole.MenuItem
                Select Case item.Kind
                    Case ChromeItemKind.Toggle : Return AccessibleRole.CheckButton
                    Case ChromeItemKind.MenuButton : Return AccessibleRole.ButtonDropDown
                    Case ChromeItemKind.Text : Return AccessibleRole.StaticText
                    Case Else : Return AccessibleRole.PushButton
                End Select
            End Get
        End Property

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Dim s As AccessibleStates = AccessibleStates.None
                If item.Kind <> ChromeItemKind.Text Then s = s Or AccessibleStates.Focusable
                If Not item.Enabled Then s = s Or AccessibleStates.Unavailable
                If item.Checked Then s = s Or AccessibleStates.Checked
                If item.IsPressed Then s = s Or AccessibleStates.Pressed
                If surface.IsFocusedItem(item) Then s = s Or AccessibleStates.Focused
                If Not item.Visible Then s = s Or AccessibleStates.Invisible
                Return s
            End Get
        End Property

        Public Overrides ReadOnly Property Bounds As Rectangle
            Get
                Return surface.RectangleToScreen(item.Bounds)
            End Get
        End Property

        Public Overrides ReadOnly Property Parent As AccessibleObject
            Get
                Return surface.AccessibilityObject
            End Get
        End Property

        Public Overrides ReadOnly Property DefaultAction As String
            Get
                Return If(item.Kind = ChromeItemKind.Text, "", If(item.Kind = ChromeItemKind.Toggle, Localization.T("Переключить"), Localization.T("Нажать")))
            End Get
        End Property

        Public Overrides Sub DoDefaultAction()
            If item.Enabled AndAlso item.Visible Then surface.RunAction(item, item.Action)
        End Sub

        Public Overrides Function Navigate(navdir As AccessibleNavigation) As AccessibleObject
            Dim visible As List(Of ChromeItem) = surface.AccessibleItems()
            Dim at As Integer = visible.IndexOf(item)
            Select Case navdir
                Case AccessibleNavigation.Next, AccessibleNavigation.Right, AccessibleNavigation.Down
                    If at >= 0 AndAlso at < visible.Count - 1 Then Return surface.AccessibleFor(visible(at + 1))
                Case AccessibleNavigation.Previous, AccessibleNavigation.Left, AccessibleNavigation.Up
                    If at > 0 Then Return surface.AccessibleFor(visible(at - 1))
            End Select
            Return Nothing
        End Function
    End Class

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then toolTip.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If

