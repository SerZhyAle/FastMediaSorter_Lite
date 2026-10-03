#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
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

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property VerticalLayout As Boolean
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property StatusLayout As Boolean
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

    ''' <summary>Raised after an item's action ran, so the owner can re-assert z-order or reposition.</summary>
    Public Event ItemInvoked As EventHandler(Of ChromeItem)

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
        Dim square As Integer = Math.Max(Px(ChromeMetrics.ItemHeight), Px(ChromeMetrics.MinTarget))
        If item.PreferredWidth > 0 Then Return Px(item.PreferredWidth)
        If Not VerticalLayout AndAlso Not item.ShowCaptionWithGlyph AndAlso (item.Image IsNot Nothing OrElse item.GlyphId.Length > 0 OrElse item.Caption.Length = 0) Then Return square
        Dim font As Font = FontFor(item)
        If font Is Nothing Then Return square
        Dim text As Integer = TextRenderer.MeasureText(item.Caption, font, New Size(Integer.MaxValue, Integer.MaxValue),
                                                       TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix).Width
        Return Math.Max(square, text + 2 * Px(ChromeMetrics.CaptionPadding) + If((VerticalLayout OrElse item.ShowCaptionWithGlyph) AndAlso (item.Image IsNot Nothing OrElse item.GlyphId.Length > 0), square, 0))
    End Function

    Public Function ItemHeight() As Integer
        Return Math.Max(Math.Max(Px(ChromeMetrics.ItemHeight), Px(ChromeMetrics.MinTarget)), If(textFont Is Nothing, 0, textFont.Height + 2 * Px(ChromeMetrics.Gap)))
    End Function

    Public Function PanelPadding() As Integer
        Return Px(ChromeMetrics.PanelPadding)
    End Function

    ''' <summary>The width the corner pin takes out of a row, gap included; 0 when there is none (or in
    ''' a popup menu, which has no corner).</summary>
    Private Function PinReserve() As Integer
        If PinItem Is Nothing OrElse VerticalLayout Then Return 0
        Return Px(ChromeMetrics.MinTarget) + Px(ChromeMetrics.Gap)
    End Function

    ''' <summary>The size a panel with these items wants: one row, padded.</summary>
    Public Function PreferredPanelSize() As Size
        Dim slots As List(Of ChromeSlot) = BuildSlots(Nothing)
        Dim pad As Integer = PanelPadding()
        If VerticalLayout Then
            Return New Size(If(slots.Count = 0, Px(ChromeMetrics.MenuMinWidth), slots.Max(Function(slot) slot.Width)) + 2 * pad,
                            slots.Count * ItemHeight() + 2 * pad)
        End If
        Return New Size(ChromeLayout.NaturalWidth(slots, Px(ChromeMetrics.Gap)) + PinReserve() + 2 * pad, ItemHeight() + 2 * pad)
    End Function

    Private Function BuildSlots(indexMap As List(Of ChromeItem)) As List(Of ChromeSlot)
        Dim slots As New List(Of ChromeSlot)()
        For Each item As ChromeItem In itemList
            If Not item.Visible OrElse item Is overflowItem OrElse item Is pinButton Then Continue For
            slots.Add(New ChromeSlot(item.Id, MeasureItemWidth(item), If(item.MinimumWidth > 0, Px(item.MinimumWidth), -1),
                pinned:=item.Id = "cmbox_Media_Folder" OrElse item.Id = "chkbox_Top_Most" OrElse item.Id = "cmbox_Sort"))
            If indexMap IsNot Nothing Then indexMap.Add(item)
        Next
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
        Dim cornerReserve As Integer = PinReserve()
        Dim available As Integer = Math.Max(0, ClientSize.Width - 2 * pad - cornerReserve)
        ' The row sits beside the corner pin: after it in a right-to-left panel (the pin is on the left).
        Dim rowStart As Integer = pad + If(isRightToLeft, cornerReserve, 0)
        If VerticalLayout Then
            For index As Integer = 0 To order.Count - 1
                order(index).Bounds = New Rectangle(pad, pad + index * ItemHeight(), available, ItemHeight())
            Next
            Invalidate()
            RaiseEvent LayoutChanged(Me, EventArgs.Empty)
            Return
        End If
        Dim row As ChromeRowLayout = ChromeLayout.ArrangeRow(slots, available, ItemHeight(), Px(ChromeMetrics.Gap),
                                                              ItemHeight(), isRightToLeft)
        If StatusLayout AndAlso slots.Count = 2 Then
            Dim contentWidth As Integer = Math.Max(0, available - Px(ChromeMetrics.Gap))
            Dim statusWidth As Integer = Math.Min(contentWidth * 2 \ 3,
                TextRenderer.MeasureText(order(1).Caption, If(textFont, Font)).Width + 2 * Px(ChromeMetrics.CaptionPadding))
            Dim fileWidth As Integer = contentWidth - statusWidth
            slots = New List(Of ChromeSlot) From {
                New ChromeSlot(order(0).Id, fileWidth), New ChromeSlot(order(1).Id, statusWidth)}
            row = ChromeLayout.ArrangeRow(slots, available, ItemHeight(), Px(ChromeMetrics.Gap), ItemHeight(), isRightToLeft)
        End If
        Dim top As Integer = Math.Max(pad, (ClientSize.Height - ItemHeight()) \ 2)
        For i As Integer = 0 To order.Count - 1
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
            PinItem.Bounds = New Rectangle(If(isRightToLeft, pad, ClientSize.Width - pad - pinWidth), top, pinWidth, ItemHeight())
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
            If item.Visible AndAlso Not item.Bounds.IsEmpty Then PaintItem(g, item)
        Next
    End Sub

    Private Sub PaintItem(g As Graphics, item As ChromeItem)
        Dim r As Rectangle = item.Bounds
        Dim itemCorner As Integer = Px(ChromeMetrics.ItemCorner)

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

        Dim ink As Color = chromeTheme.InkFor(item.Enabled, item.Checked, item.Destructive, item.HoverLevel > 0.5F)
        Dim drawn As Boolean = False
        If item.IsPin Then
            PaintPin(g, item, r, ink)
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
        If (VerticalLayout OrElse item.ShowCaptionWithGlyph) AndAlso drawn Then
            Dim glyphWidth As Integer = ItemHeight()
            Dim captionRect As New Rectangle(r.X + If(isRightToLeft, 0, glyphWidth), r.Y, Math.Max(0, r.Width - glyphWidth), r.Height)
            PaintCaption(g, item, captionRect, ink)
        ElseIf Not drawn AndAlso item.Caption.Length > 0 Then
            PaintCaption(g, item, r, ink)
        End If

        If item.Checked AndAlso chromeTheme.IsHighContrast Then
            Using pen As New Pen(chromeTheme.Foreground, Math.Max(1, Px(ChromeMetrics.FocusRingWidth)))
                Dim box As Rectangle = Rectangle.Inflate(r, -Px(ChromeMetrics.FocusRingInset), -Px(ChromeMetrics.FocusRingInset))
                g.DrawRectangle(pen, box)
            End Using
        End If
        If item.SecondaryAction IsNot Nothing Then
            Dim size As Integer = Px(ChromeMetrics.Gap)
            Dim x As Integer = If(isRightToLeft, r.Left + size, r.Right - 2 * size)
            Dim y As Integer = r.Bottom - 2 * size
            Using pen As New Pen(ink)
                g.DrawLine(pen, x, y, x + size \ 2, y + size \ 2)
                g.DrawLine(pen, x + size \ 2, y + size \ 2, x + size, y)
            End Using
        End If
        If inKeyboardMode AndAlso item Is focusedItem Then
            Dim inset As Integer = Px(ChromeMetrics.FocusRingInset)
            Dim ring As Rectangle = Rectangle.Inflate(r, -inset + 1, -inset + 1)
            Using path As GraphicsPath = RoundedRect(New RectangleF(ring.X, ring.Y, ring.Width, ring.Height), itemCorner)
                Using pen As New Pen(chromeTheme.FocusRing, Math.Max(1, Px(ChromeMetrics.FocusRingWidth)))
                    g.DrawPath(pen, path)
                End Using
            End Using
        End If
    End Sub

    ''' <summary>A push-pin drawn from lines, in the item's ink: upright and filled while the panel is
    ''' pinned, tilted a quarter turn and only outlined while it is free. The vocabulary has no pin
    ''' glyph yet, so it is painted like the toggle's check box and the secondary-action tick are.</summary>
    Private Sub PaintPin(g As Graphics, item As ChromeItem, r As Rectangle, ink As Color)
        Dim unit As Single = Px(ChromeMetrics.PinGlyphSize) / 12.0F
        Dim state As GraphicsState = g.Save()
        Try
            g.TranslateTransform(r.X + r.Width / 2.0F, r.Y + r.Height / 2.0F)
            If Not item.Checked Then g.RotateTransform(45.0F)
            Dim head As New RectangleF(-2.5F * unit, -6.0F * unit, 5.0F * unit, 1.5F * unit)
            Dim body As PointF() = {New PointF(-2.0F * unit, -4.5F * unit), New PointF(2.0F * unit, -4.5F * unit),
                                    New PointF(3.0F * unit, 0.0F), New PointF(-3.0F * unit, 0.0F)}
            Using pen As New Pen(ink, Math.Max(1.0F, 1.2F * unit))
                If item.Checked Then
                    Using brush As New SolidBrush(ink)
                        g.FillRectangle(brush, head)
                        g.FillPolygon(brush, body)
                    End Using
                Else
                    g.DrawRectangle(pen, head.X, head.Y, head.Width, head.Height)
                    g.DrawPolygon(pen, body)
                End If
                g.DrawLine(pen, -4.0F * unit, 0.0F, 4.0F * unit, 0.0F)
                g.DrawLine(pen, 0.0F, 0.0F, 0.0F, 6.0F * unit)
            End Using
        Finally
            g.Restore(state)
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
        Dim flags As TextFormatFlags = TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or
                                       TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or
                                       TextFormatFlags.NoPadding
        If VerticalLayout OrElse item.ShowCaptionWithGlyph Then flags = (flags And Not TextFormatFlags.HorizontalCenter) Or If(isRightToLeft, TextFormatFlags.Right, TextFormatFlags.Left)
        If isRightToLeft AndAlso Not item.CaptionIsSymbol Then flags = flags Or TextFormatFlags.RightToLeft
        TextRenderer.DrawText(g, item.Caption, font, r, ink, flags)
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
            If item.Visible AndAlso Not item.Bounds.IsEmpty AndAlso item.Bounds.Contains(point) Then Return item
        Next
        Return Nothing
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim item As ChromeItem = HitTest(e.Location)
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
        If item Is Nothing OrElse Not item.Enabled Then Return
        pressedItem = item
        holdElapsed = 0
        holdInvoked = False
        item.IsPressed = True
        Capture = True
        RefreshLevels()
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
        If pressedItem Is Nothing Then
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
        toolTip.SetToolTip(Me, text)
    End Sub

    ' --- easing -----------------------------------------------------------------------------

    ''' <summary>True while any item's hover or press level has not reached its target.</summary>
    Public ReadOnly Property NeedsAnimation As Boolean
        Get
            If pressedItem IsNot Nothing AndAlso pressedItem.SecondaryAction IsNot Nothing AndAlso Not holdInvoked Then Return True
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
        If pressedItem IsNot Nothing AndAlso pressedItem.SecondaryAction IsNot Nothing AndAlso Not holdInvoked Then
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
        Return item.Kind <> ChromeItemKind.Text AndAlso item.Visible AndAlso item.Enabled AndAlso Not item.Bounds.IsEmpty
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
            AccessibilityNotifyClients(AccessibleEvents.Focus, itemList.IndexOf(item))
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

        Public Overrides ReadOnly Property Description As String
            Get
                Return item.Id
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
                Return If(item.Kind = ChromeItemKind.Text, "", If(item.Kind = ChromeItemKind.Toggle, "Toggle", "Press"))
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

