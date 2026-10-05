#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

' The F3 panel's thumbnail surface (005_SPECIFICATION_THUMBNAIL_GRID_TRIAGE_DOTNET10.md
' PG-2..PG-4): ONE owner-drawn control instead of a FlowLayoutPanel with a PictureBox per
' file.
'
' Why that is the right shape, not just a different one: a card used to be a real window
' handle plus a decoded bitmap, which is what the old ceiling of 600 live cards, the window
' that slid over the folder and the "trim whole rows" arithmetic were all there to survive. Here
' the files are rows of data, the control paints only what is in view, bitmaps live in a
' budgeted cache, and rubber-band selection and hit-testing are arithmetic on a grid.
'
' The control holds NO selection logic. Every gesture is a call into SelectionModel (PB-1);
' the details list is the other renderer of the same model.

Partial Public Class Image_Panel_Form

    Friend NotInheritable Class ThumbnailGrid
        Inherits Control

        Private Const Margin_Px As Integer = 10
        Private Const Gap_Px As Integer = 10
        Private Const Caption_Px As Integer = 20
        Private Const Drag_Threshold_Px As Integer = 4

        Private model As New SelectionModel()
        Private rows As IReadOnlyList(Of PanelFileRow) = New List(Of PanelFileRow)()
        Private memory As ThumbnailMemoryCache
        Private no_Picture As HashSet(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private card As Integer = 128
        Private target_Path As String = ""

        Private ReadOnly scrollbar As New VScrollBar()
        Private scroll_Y As Integer
        Private last_First As Integer = -2
        Private last_Last As Integer = -2

        Private hover_Index As Integer = -1

        ' --- rubber band ---------------------------------------------------------------
        Private press_Pending As Boolean
        Private press_Point As Point
        Private band_Active As Boolean
        Private band_Additive As Boolean
        Private band_Origin As Point        ' content coordinates (scroll-independent)
        Private band_Current As Point
        Private ReadOnly band_Timer As New Timer() With {.Interval = 40}

        Private glyph_Image As Bitmap
        Private glyph_Video As Bitmap
        Private glyph_For As String = ""

        ''' <summary>Something changed in what is on screen (scroll, resize, new data): the
        ''' panel refills the loader's queue from <see cref="VisibleRows"/>.</summary>
        Friend Event ViewChanged As EventHandler
        ''' <summary>A mouse gesture changed the selection or the focus.</summary>
        Friend Event SelectionGesture As EventHandler
        ''' <summary>Double click (or Enter on the focused card): open it in the main window.</summary>
        Friend Event ItemActivated As EventHandler(Of Integer)
        ''' <summary>The card under the mouse changed; -1 when none.</summary>
        Friend Event HoverChanged As EventHandler(Of Integer)

        Friend Sub New()
            SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable Or
                     ControlStyles.Opaque, True)
            TabStop = True
            DoubleBuffered = True

            scrollbar.Dock = DockStyle.Right
            scrollbar.Visible = False
            scrollbar.TabStop = False
            AddHandler scrollbar.Scroll, Sub(s, e) SetScroll(e.NewValue)
            Controls.Add(scrollbar)

            AddHandler band_Timer.Tick, AddressOf OnBandTick
        End Sub

        ' ------------------------------------------------------------------ data ----

        Friend Sub Bind(selection As SelectionModel, displayRows As IReadOnlyList(Of PanelFileRow),
                        cache As ThumbnailMemoryCache, unavailable As HashSet(Of String))
            model = selection
            rows = displayRows
            memory = cache
            no_Picture = unavailable
            hover_Index = -1
            RelayoutAndNotify()
        End Sub

        ''' <summary>The card side in pixels (96/128/192/256).</summary>
        Friend Property CardSize As Integer
            Get
                Return card
            End Get
            Set(value As Integer)
                If value = card Then Return
                Dim anchor_Row As Integer = If(Cols() > 0, FirstVisibleIndex() \ Cols(), 0)
                card = value
                DisposeGlyphs()
                RelayoutAndNotify()
                ' Keep roughly the same place in the folder across a size change.
                SetScroll(Math.Min(MaxScroll(), anchor_Row * CellH()))
            End Set
        End Property

        ''' <summary>The card the panel was opened on - drawn with a red frame, as it always was.</summary>
        Friend Property TargetPath As String
            Get
                Return target_Path
            End Get
            Set(value As String)
                target_Path = If(value, "")
                Invalidate()
            End Set
        End Property

        ' --------------------------------------------------------------- geometry ----

        Private Function CellW() As Integer
            Return card + Dp(Gap_Px)
        End Function

        Private Function CellH() As Integer
            Return card + Dp(Caption_Px) + Dp(Gap_Px)
        End Function

        Private Function Dp(logical As Integer) As Integer
            Return LogicalToDeviceUnits(logical)
        End Function

        Private Function ViewWidth() As Integer
            ' The scrollbar's width is reserved always: whether the bar is needed depends on
            ' the column count, which depends on the width - reserving it breaks the circle.
            Return Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth)
        End Function

        ''' <summary>Cards per row.</summary>
        Friend Function Cols() As Integer
            Return Math.Max(1, (ViewWidth() - 2 * Dp(Margin_Px) + Dp(Gap_Px)) \ CellW())
        End Function

        ''' <summary>Whole rows that fit - for Page Up/Down.</summary>
        Friend Function PageRows() As Integer
            Return Math.Max(1, ClientSize.Height \ CellH())
        End Function

        Private Function ContentHeight() As Integer
            Dim count As Integer = rows.Count
            If count = 0 Then Return 0
            Dim lines As Integer = (count + Cols() - 1) \ Cols()
            Return 2 * Dp(Margin_Px) + lines * CellH()
        End Function

        Private Function MaxScroll() As Integer
            Return Math.Max(0, ContentHeight() - ClientSize.Height)
        End Function

        Private Function CellRect(index As Integer) As Rectangle
            Dim c As Integer = Cols()
            Dim row As Integer = index \ c
            Dim col As Integer = index Mod c
            Return New Rectangle(Dp(Margin_Px) + col * CellW(),
                                 Dp(Margin_Px) + row * CellH() - scroll_Y,
                                 card, card + Dp(Caption_Px))
        End Function

        ''' <summary>The card at a client point, or -1 for empty space (the gaps, the margin,
        ''' below the last row) - the places a rubber band may start from.</summary>
        Friend Function IndexAt(p As Point) As Integer
            If rows.Count = 0 Then Return -1
            Dim x As Integer = p.X - Dp(Margin_Px)
            Dim y As Integer = p.Y + scroll_Y - Dp(Margin_Px)
            If x < 0 OrElse y < 0 Then Return -1
            Dim col As Integer = x \ CellW()
            Dim row As Integer = y \ CellH()
            If col >= Cols() Then Return -1
            If (x Mod CellW()) >= card OrElse (y Mod CellH()) >= card + Dp(Caption_Px) Then Return -1
            Dim index As Integer = row * Cols() + col
            Return If(index < rows.Count, index, -1)
        End Function

        Private Function FirstVisibleIndex() As Integer
            Return Math.Max(0, (scroll_Y - Dp(Margin_Px)) \ CellH()) * Cols()
        End Function

        ''' <summary>The rows from a little before the screen to a little after it, in the order
        ''' the loader should work: what is visible now first, then the margins.</summary>
        Friend Function VisibleRows() As List(Of PanelFileRow)
            Dim result As New List(Of PanelFileRow)()
            If rows.Count = 0 Then Return result
            Dim c As Integer = Cols()
            Dim first_Row As Integer = Math.Max(0, (scroll_Y - Dp(Margin_Px)) \ CellH())
            Dim last_Row As Integer = Math.Max(first_Row, (scroll_Y + ClientSize.Height - Dp(Margin_Px)) \ CellH())
            Dim from As Integer = first_Row * c
            Dim upto As Integer = Math.Min(rows.Count - 1, (last_Row + 1) * c - 1)
            For i As Integer = from To upto
                result.Add(rows(i))
            Next
            ' One row of look-ahead each way: a slow scroll finds its cards already made.
            Dim ahead_To As Integer = Math.Min(rows.Count - 1, upto + c)
            For i As Integer = upto + 1 To ahead_To
                result.Add(rows(i))
            Next
            Dim behind_From As Integer = Math.Max(0, from - c)
            For i As Integer = from - 1 To behind_From Step -1
                result.Add(rows(i))
            Next
            Return result
        End Function

        ' ------------------------------------------------------------------ scroll ----

        Private Sub RelayoutAndNotify()
            Dim max As Integer = MaxScroll()
            If scroll_Y > max Then scroll_Y = max
            With scrollbar
                .Visible = max > 0
                If .Visible Then
                    .Minimum = 0
                    .Maximum = ContentHeight() - 1
                    .LargeChange = Math.Max(1, ClientSize.Height)
                    .SmallChange = Math.Max(1, CellH() \ 2)
                    .Value = Math.Min(scroll_Y, Math.Max(0, .Maximum - .LargeChange + 1))
                End If
            End With
            Invalidate()
            NotifyView()
        End Sub

        Private Sub SetScroll(value As Integer)
            Dim clamped As Integer = Math.Max(0, Math.Min(MaxScroll(), value))
            If clamped = scroll_Y Then Return
            scroll_Y = clamped
            If scrollbar.Visible Then
                scrollbar.Value = Math.Min(clamped, Math.Max(0, scrollbar.Maximum - scrollbar.LargeChange + 1))
            End If
            Invalidate()
            NotifyView()
        End Sub

        Private Sub NotifyView()
            Dim first As Integer = FirstVisibleIndex()
            Dim last As Integer = Math.Min(rows.Count - 1, first + (PageRows() + 2) * Cols())
            If first = last_First AndAlso last = last_Last Then Return
            last_First = first
            last_Last = last
            RaiseEvent ViewChanged(Me, EventArgs.Empty)
        End Sub

        ''' <summary>Scrolls just enough to show a card (keyboard focus, the opening target).</summary>
        Friend Sub EnsureVisible(index As Integer)
            If index < 0 OrElse index >= rows.Count Then Return
            Dim top As Integer = Dp(Margin_Px) + (index \ Cols()) * CellH()
            Dim bottom As Integer = top + CellH()
            If top < scroll_Y Then
                SetScroll(Math.Max(0, top - Dp(Margin_Px)))
            ElseIf bottom > scroll_Y + ClientSize.Height Then
                SetScroll(bottom - ClientSize.Height + Dp(Margin_Px))
            End If
        End Sub

        ''' <summary>Puts a card in the middle of the view when it is not already visible.</summary>
        Friend Sub CenterOn(index As Integer)
            If index < 0 OrElse index >= rows.Count Then Return
            Dim top As Integer = Dp(Margin_Px) + (index \ Cols()) * CellH()
            SetScroll(top - (ClientSize.Height - CellH()) \ 2)
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            RelayoutAndNotify()
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            Dim lines As Integer = Math.Max(1, SystemInformation.MouseWheelScrollLines)
            ' A "line" of a grid is a third of a row: three notches move one row.
            SetScroll(scroll_Y - CInt(e.Delta / 120.0 * lines * CellH() / 3.0))
        End Sub

        ' ------------------------------------------------------------------- input ----

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData And Keys.KeyCode
                Case Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                    Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If e.Button <> MouseButtons.Left Then Return

            Dim ctrl As Boolean = (ModifierKeys And Keys.Control) = Keys.Control
            Dim shift As Boolean = (ModifierKeys And Keys.Shift) = Keys.Shift
            Dim index As Integer = IndexAt(e.Location)

            If index >= 0 Then
                If ctrl AndAlso shift Then
                    model.CtrlShiftClick(index)
                ElseIf shift Then
                    model.ShiftClick(index)
                ElseIf ctrl Then
                    model.CtrlClick(index)
                Else
                    model.Click(index)
                End If
            End If

            ' A press that then MOVES becomes a rubber band, from a card or from empty space
            ' alike - in a packed grid there is hardly any empty space to start from.
            press_Pending = True
            press_Point = e.Location
            band_Origin = New Point(e.X, e.Y + scroll_Y)
            band_Additive = ctrl OrElse shift
            If index < 0 AndAlso Not band_Additive Then model.SelectNone()
            Invalidate()
            RaiseEvent SelectionGesture(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)

            If press_Pending AndAlso Not band_Active AndAlso e.Button = MouseButtons.Left Then
                If Math.Abs(e.X - press_Point.X) > Dp(Drag_Threshold_Px) OrElse Math.Abs(e.Y - press_Point.Y) > Dp(Drag_Threshold_Px) Then
                    band_Active = True
                    model.BeginBand(band_Additive)
                    band_Timer.Start()
                End If
            End If

            If band_Active Then
                band_Current = New Point(e.X, e.Y + scroll_Y)
                UpdateBand()
                Return
            End If

            Dim index As Integer = IndexAt(e.Location)
            If index <> hover_Index Then
                Dim old As Integer = hover_Index
                hover_Index = index
                If old >= 0 AndAlso old < rows.Count Then Invalidate(Rectangle.Inflate(CellRect(old), 6, 6))
                If index >= 0 Then Invalidate(Rectangle.Inflate(CellRect(index), 6, 6))
                RaiseEvent HoverChanged(Me, index)
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If e.Button <> MouseButtons.Left Then Return
            If band_Active Then
                band_Active = False
                band_Timer.Stop()
                model.EndBand()
                Invalidate()
                RaiseEvent SelectionGesture(Me, EventArgs.Empty)
            End If
            press_Pending = False
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            If hover_Index >= 0 Then
                hover_Index = -1
                Invalidate()
                RaiseEvent HoverChanged(Me, -1)
            End If
        End Sub

        Protected Overrides Sub OnMouseDoubleClick(e As MouseEventArgs)
            MyBase.OnMouseDoubleClick(e)
            If e.Button <> MouseButtons.Left Then Return
            Dim index As Integer = IndexAt(e.Location)
            If index >= 0 Then RaiseEvent ItemActivated(Me, index)
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            If band_Active Then
                band_Active = False
                band_Timer.Stop()
                model.EndBand()
            End If
            press_Pending = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        ''' <summary>The band covers the cards its rectangle touches; the model does the rest.</summary>
        Private Sub UpdateBand()
            Dim band As Rectangle = BandRect()
            Dim covered As New List(Of Integer)()
            If rows.Count > 0 Then
                Dim c As Integer = Cols()
                Dim col_From As Integer = Math.Max(0, (band.Left - Dp(Margin_Px)) \ CellW())
                Dim col_To As Integer = Math.Min(c - 1, (band.Right - Dp(Margin_Px)) \ CellW())
                Dim row_From As Integer = Math.Max(0, (band.Top - Dp(Margin_Px)) \ CellH())
                Dim row_To As Integer = (band.Bottom - Dp(Margin_Px)) \ CellH()
                For row As Integer = row_From To row_To
                    For col As Integer = col_From To col_To
                        Dim index As Integer = row * c + col
                        If index >= rows.Count Then Continue For
                        ' The card itself, not its whole cell: grazing the gap selects nothing.
                        Dim card_Box As New Rectangle(Dp(Margin_Px) + col * CellW(), Dp(Margin_Px) + row * CellH(), card, card + Dp(Caption_Px))
                        If card_Box.IntersectsWith(band) Then covered.Add(index)
                    Next
                Next
            End If
            model.UpdateBand(covered)
            Invalidate()
            RaiseEvent SelectionGesture(Me, EventArgs.Empty)
        End Sub

        Private Function BandRect() As Rectangle
            Return Rectangle.FromLTRB(Math.Min(band_Origin.X, band_Current.X), Math.Min(band_Origin.Y, band_Current.Y),
                                      Math.Max(band_Origin.X, band_Current.X), Math.Max(band_Origin.Y, band_Current.Y))
        End Function

        ''' <summary>Dragging past the top or bottom edge scrolls, and the band keeps growing.</summary>
        Private Sub OnBandTick(sender As Object, e As EventArgs)
            If Not band_Active Then
                band_Timer.Stop()
                Return
            End If
            Dim mouse As Point = PointToClient(Cursor.Position)
            Dim step_Px As Integer = 0
            If mouse.Y < 0 Then
                step_Px = Math.Max(-CellH() \ 2, mouse.Y)
            ElseIf mouse.Y > ClientSize.Height Then
                step_Px = Math.Min(CellH() \ 2, mouse.Y - ClientSize.Height)
            End If
            If step_Px <> 0 Then SetScroll(scroll_Y + step_Px)
            band_Current = New Point(Math.Max(0, Math.Min(ClientSize.Width, mouse.X)), mouse.Y + scroll_Y)
            UpdateBand()
        End Sub

        ' ------------------------------------------------------------------ paint ----

        Private Sub DisposeGlyphs()
            glyph_Image?.Dispose()
            glyph_Video?.Dispose()
            glyph_Image = Nothing
            glyph_Video = Nothing
            glyph_For = ""
        End Sub

        Private Sub EnsureGlyphs()
            Dim stamp As String = card.ToString() & "|" & ForeColor.ToArgb().ToString()
            If stamp = glyph_For Then Return
            DisposeGlyphs()
            glyph_For = stamp
            Dim tier As Integer = 16
            For Each candidate As Integer In New Integer() {20, 24, 32, 40, 48}
                If candidate * 2 <= card Then tier = candidate
            Next
            Dim dim_Color As Color = Color.FromArgb(150, ForeColor)
            Try
                glyph_Image = GlyphRenderer.Render(IconMap.GetMeaning("thumbnail_Image_Missing").GlyphId, tier, dim_Color, AddressOf RuntimeBootstrap.OpenBundledAsset)
                glyph_Video = GlyphRenderer.Render(IconMap.GetMeaning("thumbnail_Video_Missing").GlyphId, tier, dim_Color, AddressOf RuntimeBootstrap.OpenBundledAsset)
            Catch
                ' A missing glyph resource must not take the panel down: the plate still says
                ' what the file is.
            End Try
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g As Graphics = e.Graphics
            g.Clear(BackColor)
            If rows.Count = 0 Then
                ' An empty folder says so (126 UX-609): a bare surface reads the same as
                ' "the panel has not finished opening".
                Using ink As New SolidBrush(Color.FromArgb(150, ForeColor))
                    Using centre As New StringFormat With {
                            .Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                        g.DrawString(Localization.T("Папка пуста"), Font, ink, ClientRectangle, centre)
                    End Using
                End Using
                Return
            End If

            EnsureGlyphs()
            Dim c As Integer = Cols()
            Dim first_Row As Integer = Math.Max(0, (scroll_Y - Dp(Margin_Px)) \ CellH())
            Dim last_Row As Integer = (scroll_Y + ClientSize.Height - Dp(Margin_Px)) \ CellH()
            Dim from As Integer = first_Row * c
            Dim upto As Integer = Math.Min(rows.Count - 1, (last_Row + 1) * c - 1)

            g.SmoothingMode = SmoothingMode.AntiAlias
            Using name_Format As New StringFormat(StringFormatFlags.NoWrap) With {
                .Trimming = StringTrimming.EllipsisCharacter, .LineAlignment = StringAlignment.Center}
                Using name_Brush As New SolidBrush(ForeColor)
                    For i As Integer = from To upto
                        PaintCell(g, i, name_Format, name_Brush)
                    Next
                End Using
            End Using

            If band_Active Then
                Dim shown As Rectangle = BandRect()
                shown.Offset(0, -scroll_Y)
                Using fill As New SolidBrush(Color.FromArgb(50, 51, 153, 255))
                    g.FillRectangle(fill, shown)
                End Using
                Using pen As New Pen(Color.FromArgb(200, 51, 153, 255), 1)
                    g.DrawRectangle(pen, shown.X, shown.Y, Math.Max(0, shown.Width - 1), Math.Max(0, shown.Height - 1))
                End Using
            End If
        End Sub

        Private Sub PaintCell(g As Graphics, index As Integer, nameFormat As StringFormat, nameBrush As Brush)
            Dim row As PanelFileRow = rows(index)
            Dim r As Rectangle = CellRect(index)
            Dim picture As New Rectangle(r.X, r.Y, card, card)
            Dim selected As Boolean = model.IsSelected(index)
            Dim is_Target As Boolean = target_Path.Length > 0 AndAlso String.Equals(row.Path, target_Path, StringComparison.OrdinalIgnoreCase)

            ' Selection and hover sit BEHIND the picture, around the whole cell.
            Dim frame As Rectangle = Rectangle.Inflate(r, 4, 4)
            If selected Then
                Using fill As New SolidBrush(Color.FromArgb(70, 51, 153, 255))
                    g.FillRectangle(fill, frame)
                End Using
            ElseIf index = hover_Index Then
                Using fill As New SolidBrush(Color.FromArgb(30, 255, 255, 255))
                    g.FillRectangle(fill, frame)
                End Using
            End If

            Dim image As Bitmap = If(memory Is Nothing, Nothing, memory.TryGet(row.Path))
            If image IsNot Nothing Then
                g.DrawImage(image, picture.X + (card - image.Width) \ 2, picture.Y + (card - image.Height) \ 2, image.Width, image.Height)
                If row.Kind = MediaKind.Video Then PaintPlayBadge(g, picture)
            Else
                PaintPlate(g, picture, row, no_Picture IsNot Nothing AndAlso no_Picture.Contains(row.Path))
            End If

            ' Name under the picture.
            Dim caption As New Rectangle(r.X, r.Y + card + 2, card, Dp(Caption_Px) - 2)
            g.DrawString(row.Name, Font, nameBrush, caption, nameFormat)

            If selected Then
                Using pen As New Pen(Color.FromArgb(51, 153, 255), 2)
                    g.DrawRectangle(pen, frame.X, frame.Y, frame.Width - 1, frame.Height - 1)
                End Using
            End If
            If is_Target Then
                Using pen As New Pen(Color.Red, 2)
                    g.DrawRectangle(pen, picture.X - 1, picture.Y - 1, picture.Width + 1, picture.Height + 1)
                End Using
            End If
            If Focused AndAlso model.FocusIndex = index Then
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(frame, 1, 1), ForeColor, BackColor)
            End If
        End Sub

        ''' <summary>The card before its picture exists, or when none can be made: what the
        ''' file IS. Pictures and videos keep the glyph they always had; the rest get a plate
        ''' with the extension, which is the one thing every file has.</summary>
        Private Sub PaintPlate(g As Graphics, picture As Rectangle, row As PanelFileRow, finished As Boolean)
            Using fill As New SolidBrush(Color.FromArgb(34, 128, 128, 128))
                g.FillRectangle(fill, picture)
            End Using

            If row.Kind = MediaKind.Image OrElse row.Kind = MediaKind.Video Then
                ' While the picture is still coming the plate stays bare - a glyph that then
                ' gives way to the picture is flicker. Once it is known none will come, the
                ' glyph says what the file is.
                If finished Then
                    Dim glyph As Bitmap = If(row.Kind = MediaKind.Video, glyph_Video, glyph_Image)
                    If glyph IsNot Nothing Then
                        g.DrawImage(glyph, picture.X + (card - glyph.Width) \ 2, picture.Y + (card - glyph.Height) \ 2, glyph.Width, glyph.Height)
                    End If
                End If
                Return
            End If

            Dim ext As String = ""
            Try
                ext = Path.GetExtension(row.Path).TrimStart("."c).ToUpperInvariant()
            Catch
            End Try
            If ext.Length = 0 Then ext = "?"
            Using plate_Font As New Font(Font.FontFamily, Math.Max(9.0F, card / 7.0F), FontStyle.Bold, GraphicsUnit.Pixel)
                Using plate_Format As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                    Using ink As New SolidBrush(Color.FromArgb(190, ForeColor))
                        g.DrawString(ext, plate_Font, ink, picture, plate_Format)
                    End Using
                End Using
            End Using
        End Sub

        Private Sub PaintPlayBadge(g As Graphics, picture As Rectangle)
            Dim d As Integer = Math.Max(18, card \ 6)
            Dim badge As New Rectangle(picture.X + 6, picture.Bottom - d - 6, d, d)
            Using fill As New SolidBrush(Color.FromArgb(150, 0, 0, 0))
                g.FillEllipse(fill, badge)
            End Using
            Using tri As New SolidBrush(Color.White)
                Dim inset As Integer = d \ 4
                g.FillPolygon(tri, New Point() {
                    New Point(badge.X + inset + 2, badge.Y + inset),
                    New Point(badge.X + inset + 2, badge.Bottom - inset),
                    New Point(badge.Right - inset + 1, badge.Y + d \ 2)})
            End Using
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                band_Timer.Dispose()
                DisposeGlyphs()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Class
#End If
