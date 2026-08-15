#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Windows.Forms

''' <summary>
''' The editor's drawing tools - phase Ф-2 of SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §6-§8:
''' the toolbar, the gestures, the rubber-band preview and the undo history. The window
''' around them (open, save, close) is Image_Editor_Form.vb.
'''
''' <b>The preview and the committed stroke are the same code.</b> PaintGesture draws in
''' IMAGE coordinates and is called twice: once into the bitmap on MouseUp, and once per
''' repaint onto the canvas through a scale transform. Two implementations - one for the
''' rubber band and one for the result - is how a preview starts lying about where the
''' line will land, and on a canvas scaled to 0.2 nobody would notice until it was saved.
'''
''' All drawing is in image pixels, never canvas pixels (§5): otherwise a rectangle drawn
''' in a small window would come out somewhere else in a large one.
'''
''' Modern-only, like the rest of the editor.
''' </summary>
Partial Friend NotInheritable Class Image_Editor_Form

    ''' <summary>The tools of Ф-2, plus crop from Ф-4. Text (Ф-3) joins this list, which is
    ''' why the toolbar is built from an array rather than a copy of the same code per
    ''' tool.</summary>
    Private Enum EditorTool
        Brush = 0
        RectangleOutline
        RectangleFilled
        EllipseOutline
        EllipseFilled
        ''' <summary>The odd one out: it draws no pixels. It puts a live frame on the canvas
        ''' and waits to be told to apply it (§6.1), because cutting the edge off a picture
        ''' on mouse-up would be the one gesture in this window that cannot be aimed.</summary>
        Crop
    End Enum

    ' --- what the tools share (§7) ------------------------------------------------
    '
    ' Colour, thickness and (from Ф-3) font size are deliberately NOT per-tool: someone
    ' who picked red for a circle means red for the arrow next to it.

    Private current_Tool As EditorTool = EditorTool.Brush
    Private current_Color As Color = Color.Red
    Private current_Thickness As Integer = Default_Thickness

    Private Const Default_Thickness As Integer = 4
    Private Const Min_Thickness As Integer = 1
    Private Const Max_Thickness As Integer = 64

    ''' <summary>Kept across editor windows but not across runs (§7): a session's worth
    ''' of mixed colours is worth carrying, a registry key for it is not.</summary>
    Private Shared session_Custom_Colors As Integer() = Nothing

    ''' <summary>Black, white, red, yellow, green, blue, magenta, grey - the eight the
    ''' specification names. Lime rather than Green: this is a marker colour meant to be
    ''' seen over a photograph, and the darker one disappears into foliage.</summary>
    Private Shared ReadOnly Swatch_Colors As Color() = {
        Color.Black, Color.White, Color.Red, Color.Yellow,
        Color.Lime, Color.Blue, Color.Magenta, Color.Gray}

    ' --- the gesture in flight, in IMAGE coordinates -----------------------------

    Private gesture_Active As Boolean
    Private gesture_Anchor As Point
    Private gesture_Current As Point

    ''' <summary>Every point the brush has passed through this gesture. The polyline is
    ''' laid down ONCE from all of them (§6) - a chain of independent segments with round
    ''' caps builds a visible bulge at every joint, and MouseMove arrives in jerks.</summary>
    Private ReadOnly brush_Points As New List(Of Point)()

    ''' <summary>Whether this gesture's undo snapshot actually made it onto the stack, so
    ''' a gesture that turns out to be a no-op takes back its own snapshot and not
    ''' somebody else's.</summary>
    Private gesture_Snapshot_Taken As Boolean

    ' --- the crop frame (§6.1), in IMAGE coordinates ------------------------------
    '
    ' It outlives the gesture that drew it: until it is applied, nothing has been cut, and
    ' the frame can be dragged and resized as many times as it takes to aim it.

    Private crop_Has_Frame As Boolean
    Private crop_Rect As Rectangle

    ''' <summary>Which grip the current drag grabbed, and the frame as it was when it was
    ''' grabbed. Both are needed because a resize is computed from the ORIGINAL frame plus
    ''' the current mouse position - deriving it from the frame as it stands would let
    ''' rounding accumulate over a slow drag.</summary>
    Private crop_Drag_Handle As EditorGeometry.CropHandle = EditorGeometry.CropHandle.None
    Private crop_Drag_Start_Rect As Rectangle
    Private crop_Drag_Origin As Point

    ''' <summary>How close to an edge counts as grabbing it, in SCREEN pixels. In image
    ''' pixels it would be unusable: on a 6000-wide photo fitted to 1200 the same eight
    ''' pixels are under two on screen.</summary>
    Private Const Crop_Grip_Tolerance As Integer = 7
    Private Const Crop_Grip_Size As Integer = 7

    ''' <summary>Under this, a drag was a click that missed - not a frame of a few pixels
    ''' nobody could have aimed at.</summary>
    Private Const Crop_Min_Drag_Pixels As Integer = 2

    Private ReadOnly undo_History As New EditorUndoStack()

    ''' <summary>Set by the first committed stroke and never cleared - including by Undo.
    ''' Undo can empty the stack without returning the picture to what is on disk (an
    ''' evicted step is gone), so "clean again" is not something this flag can honestly
    ''' claim. Its only job is to decide whether closing asks a question.</summary>
    Private dirty As Boolean

    ' --- chrome -------------------------------------------------------------------

    Private Const Tool_Bar_Height As Integer = 40
    Private Const Tool_Button_Size As Integer = 34
    Private Const Swatch_Size As Integer = 22

    Private tool_Bar As Panel
    Private ReadOnly tool_Buttons As New List(Of ToolButton)()
    Private ReadOnly tool_Tips As New ToolTip()
    Private WithEvents btn_Color As Button
    Private WithEvents num_Thickness As NumericUpDown
    Private WithEvents btn_Undo As Button
    ''' <summary>Shown only while a frame exists (§6.1). A permanently visible button that
    ''' does nothing most of the time is worse than one that appears when it can act - and
    ''' its appearing is also the clearest signal that the frame is now live.</summary>
    Private WithEvents btn_Apply_Crop As Button

    ' ---------------------------------------------------------------- toolbar ----

    ''' <summary>
    ''' Builds the one toolbar row (§5). Laid out by hand along a running x: the
    ''' application is pinned to HighDpiMode.DpiUnaware and this window inherits that, so
    ''' a pixel here is a pixel on screen and a FlowLayoutPanel would only add a layout
    ''' pass that has nothing to decide.
    ''' </summary>
    Private Function BuildToolBar() As Panel
        tool_Bar = New Panel With {
            .Name = "editor_Toolbar",
            .Dock = DockStyle.Top,
            .Height = Tool_Bar_Height
        }

        Dim x As Integer = 8
        Dim topOfButtons As Integer = (Tool_Bar_Height - Tool_Button_Size) \ 2

        For Each tool As EditorTool In New EditorTool() {EditorTool.Crop,
                                                         EditorTool.Brush,
                                                         EditorTool.RectangleOutline,
                                                         EditorTool.RectangleFilled,
                                                         EditorTool.EllipseOutline,
                                                         EditorTool.EllipseFilled}
            Dim button As New ToolButton(tool) With {
                .Location = New Point(x, topOfButtons),
                .Size = New Size(Tool_Button_Size, Tool_Button_Size),
                .AccessibleName = ToolName(tool)
            }
            tool_Tips.SetToolTip(button, ToolHint(tool))
            AddHandler button.Click, AddressOf ToolButton_Click
            tool_Bar.Controls.Add(button)
            tool_Buttons.Add(button)
            x += Tool_Button_Size + 4
        Next

        x += 6
        tool_Bar.Controls.Add(NewSeparator(x))
        x += 9

        btn_Color = New Button With {
            .Name = "btn_Editor_Color",
            .Location = New Point(x, topOfButtons),
            .Size = New Size(Tool_Button_Size, Tool_Button_Size),
            .FlatStyle = FlatStyle.Flat,
            .BackColor = current_Color,
            .UseVisualStyleBackColor = False,
            .TabStop = False,
            .AccessibleName = Localization.T("Выбрать цвет")
        }
        btn_Color.FlatAppearance.BorderSize = 1
        btn_Color.FlatAppearance.BorderColor = SystemColors.ControlDark
        tool_Tips.SetToolTip(btn_Color, Localization.T("Выбрать цвет"))
        tool_Bar.Controls.Add(btn_Color)
        x += Tool_Button_Size + 8

        ' The eight swatches next to the dialog button, not inside it: they are the
        ' answer nine times out of ten, and a dialog for "red" is three clicks too many.
        Dim topOfSwatches As Integer = (Tool_Bar_Height - Swatch_Size) \ 2
        For Each swatchColor As Color In Swatch_Colors
            Dim swatch As New Button With {
                .Location = New Point(x, topOfSwatches),
                .Size = New Size(Swatch_Size, Swatch_Size),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = swatchColor,
                .UseVisualStyleBackColor = False,
                .TabStop = False,
                .Tag = swatchColor,
                .AccessibleName = Localization.T("Быстрый выбор цвета")
            }
            swatch.FlatAppearance.BorderSize = 1
            swatch.FlatAppearance.BorderColor = SystemColors.ControlDark
            AddHandler swatch.Click, AddressOf Swatch_Click
            tool_Bar.Controls.Add(swatch)
            x += Swatch_Size + 3
        Next

        x += 6
        tool_Bar.Controls.Add(NewSeparator(x))
        x += 9

        Dim thicknessLabel As New Label With {
            .Name = "lbl_Editor_Thickness",
            .Text = Localization.T("Толщина:"),
            .AutoSize = False,
            .Width = Localization.Scaled(64),
            .Height = Tool_Button_Size,
            .Location = New Point(x, topOfButtons),
            .TextAlign = ContentAlignment.MiddleLeft
        }
        tool_Bar.Controls.Add(thicknessLabel)
        x += thicknessLabel.Width + 2

        num_Thickness = New NumericUpDown With {
            .Name = "num_Editor_Thickness",
            .Minimum = Min_Thickness,
            .Maximum = Max_Thickness,
            .Value = Default_Thickness,
            .Width = 56,
            .Location = New Point(x, (Tool_Bar_Height - 22) \ 2),
            .TabStop = False,
            .AccessibleName = Localization.T("Толщина линии в пикселях картинки")
        }
        tool_Tips.SetToolTip(num_Thickness, Localization.T("Толщина линии в пикселях картинки"))
        tool_Bar.Controls.Add(num_Thickness)
        x += num_Thickness.Width + 10

        btn_Apply_Crop = New Button With {
            .Name = "btn_Editor_Apply_Crop",
            .Text = Localization.T("Применить обрезку"),
            .AutoSize = False,
            .Width = Localization.Scaled(170),
            .Height = Tool_Button_Size,
            .Location = New Point(x, topOfButtons),
            .TabStop = False,
            .Visible = False
        }
        tool_Bar.Controls.Add(btn_Apply_Crop)

        ApplyToolSelection()
        Return tool_Bar
    End Function

    Private Shared Function NewSeparator(x As Integer) As Control
        Return New Label With {
            .AutoSize = False,
            .BorderStyle = BorderStyle.Fixed3D,
            .Location = New Point(x, 6),
            .Size = New Size(1, Tool_Bar_Height - 12)
        }
    End Function

    Private Shared Function ToolName(tool As EditorTool) As String
        Select Case tool
            Case EditorTool.RectangleOutline : Return Localization.T("Прямоугольник")
            Case EditorTool.RectangleFilled : Return Localization.T("Залитый прямоугольник")
            Case EditorTool.EllipseOutline : Return Localization.T("Овал")
            Case EditorTool.EllipseFilled : Return Localization.T("Залитый овал")
            Case EditorTool.Crop : Return Localization.T("Обрезка")
            Case Else : Return Localization.T("Кисть")
        End Select
    End Function

    ''' <summary>The name, plus the one thing about a tool that is not discoverable by
    ''' trying it - nobody holds Shift on the off chance, and nothing about a frame on
    ''' screen says which key applies it.</summary>
    Private Shared Function ToolHint(tool As EditorTool) As String
        If tool = EditorTool.Brush Then Return ToolName(tool)
        If tool = EditorTool.Crop Then Return Localization.T("Обрезка (Enter - применить, Esc - снять рамку)")
        Return Localization.TF("{0} (с Shift - квадрат или круг)", ToolName(tool))
    End Function

    Private Sub ApplyToolSelection()
        For Each button As ToolButton In tool_Buttons
            button.IsSelected = (button.Tool = current_Tool)
        Next
    End Sub

    Private Sub ToolButton_Click(sender As Object, e As EventArgs)
        Dim button = TryCast(sender, ToolButton)
        If button Is Nothing Then Return
        current_Tool = button.Tool
        ' Leaving the crop tool drops the frame. Keeping it would leave a frame on screen
        ' that Enter still applies while the toolbar says the brush is selected - and the
        ' first stroke would be drawn through a dimmed overlay nobody asked for.
        If current_Tool <> EditorTool.Crop Then ClearCropFrame()
        ApplyToolSelection()
    End Sub

    Private Sub Swatch_Click(sender As Object, e As EventArgs)
        Dim button = TryCast(sender, Button)
        If button Is Nothing OrElse Not (TypeOf button.Tag Is Color) Then Return
        current_Color = CType(button.Tag, Color)
        btn_Color.BackColor = current_Color
    End Sub

    ''' <summary>
    ''' Windows' own colour picker, opened all the way (§7): the rainbow, the brightness
    ''' bar and the R/G/B + HSL boxes are what "full colour choice" means, it is localized
    ''' by the system into all thirteen languages for free, and everyone has used it.
    ''' </summary>
    Private Sub btn_Color_Click(sender As Object, e As EventArgs) Handles btn_Color.Click
        Using dialog As New ColorDialog()
            dialog.FullOpen = True
            dialog.AnyColor = True
            dialog.Color = current_Color
            If session_Custom_Colors IsNot Nothing Then dialog.CustomColors = session_Custom_Colors
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            current_Color = dialog.Color
            session_Custom_Colors = dialog.CustomColors
        End Using
        btn_Color.BackColor = current_Color
    End Sub

    Private Sub num_Thickness_ValueChanged(sender As Object, e As EventArgs) Handles num_Thickness.ValueChanged
        current_Thickness = CInt(num_Thickness.Value)
    End Sub

    ' ---------------------------------------------------------------- gestures ----

    Private Sub WireCanvasGestures(surface As EditorCanvas)
        surface.Cursor = Cursors.Cross
        AddHandler surface.MouseDown, AddressOf Canvas_MouseDown
        AddHandler surface.MouseMove, AddressOf Canvas_MouseMove
        AddHandler surface.MouseUp, AddressOf Canvas_MouseUp
        AddHandler surface.MouseDoubleClick, AddressOf Canvas_MouseDoubleClick
        AddHandler surface.Paint, AddressOf Canvas_Paint
    End Sub

    Private Sub Canvas_MouseDown(sender As Object, e As MouseEventArgs)
        If saving OrElse gesture_Active OrElse image_Bitmap Is Nothing Then Return
        If e.Button <> MouseButtons.Left Then Return

        Dim fit As Rectangle = canvas.ImageRect()
        If fit.Width <= 0 OrElse fit.Height <= 0 Then Return

        gesture_Anchor = EditorGeometry.CanvasToImage(e.Location, fit, image_Bitmap.Size)
        gesture_Current = gesture_Anchor
        brush_Points.Clear()
        If current_Tool = EditorTool.Brush Then brush_Points.Add(gesture_Anchor)

        If current_Tool = EditorTool.Crop Then
            ' No snapshot here: the frame changes nothing. The one for the crop is taken
            ' when it is APPLIED - which is also the only moment there is anything to undo.
            gesture_Snapshot_Taken = False
            BeginCropDrag(e.Location, fit)
        Else
            ' Before the first pixel (§8). Taken even for a gesture that turns out to be a
            ' stray click - MouseUp gives it back - because by MouseMove it is too late.
            Dim before As Integer = undo_History.Count
            undo_History.Push(image_Bitmap)
            gesture_Snapshot_Taken = undo_History.Count > before
        End If

        gesture_Active = True
        canvas.Invalidate()
    End Sub

    ''' <summary>
    ''' Which of the two things a press on the crop tool means: grab the frame that is
    ''' already there, or start a new one. A press on the picture away from an existing
    ''' frame is the second - the frame is not modal, and re-aiming it from scratch must
    ''' not require dismissing it first.
    ''' </summary>
    Private Sub BeginCropDrag(canvasPoint As Point, fit As Rectangle)
        crop_Drag_Handle = EditorGeometry.CropHandle.None

        If crop_Has_Frame Then
            Dim on_Canvas As Rectangle = EditorGeometry.ImageToCanvas(crop_Rect, fit, image_Bitmap.Size)
            crop_Drag_Handle = EditorGeometry.CropHandleAt(on_Canvas, canvasPoint, Crop_Grip_Tolerance)
        End If

        If crop_Drag_Handle = EditorGeometry.CropHandle.None Then
            ' A new frame: it exists from the first pixel of the drag, so the dimming
            ' follows the mouse rather than appearing once the button comes up.
            crop_Has_Frame = True
            crop_Rect = EditorGeometry.ClampCropRect(New Rectangle(gesture_Anchor, New Size(1, 1)), image_Bitmap.Size)
        End If

        crop_Drag_Start_Rect = crop_Rect
        crop_Drag_Origin = gesture_Anchor
    End Sub

    Private Sub Canvas_MouseMove(sender As Object, e As MouseEventArgs)
        If Not gesture_Active OrElse image_Bitmap Is Nothing Then Return

        Dim fit As Rectangle = canvas.ImageRect()
        If fit.Width <= 0 OrElse fit.Height <= 0 Then Return

        Dim imagePoint As Point = EditorGeometry.CanvasToImage(e.Location, fit, image_Bitmap.Size)

        If current_Tool = EditorTool.Crop Then
            DragCropTo(imagePoint)
            gesture_Current = imagePoint
            canvas.Invalidate()
            Return
        End If

        If current_Tool = EditorTool.Brush Then
            ' The canvas is usually a fraction of the picture's size, so several mouse
            ' positions map onto one image pixel; storing each would only lengthen the
            ' polyline with segments of zero length.
            If brush_Points.Count = 0 OrElse brush_Points(brush_Points.Count - 1) <> imagePoint Then
                brush_Points.Add(imagePoint)
            End If
        ElseIf (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            imagePoint = EditorGeometry.ConstrainToSquare(gesture_Anchor, imagePoint)
        End If

        gesture_Current = imagePoint
        canvas.Invalidate()
    End Sub

    ''' <summary>
    ''' The frame under the mouse during a drag. Resizing is always computed from the frame
    ''' AS IT WAS GRABBED plus the current position: deriving each step from the previous
    ''' one lets the rounding of a canvas-to-image mapping accumulate, and on a photo scaled
    ''' to a fifth of its size a slow drag would creep.
    ''' </summary>
    Private Sub DragCropTo(imagePoint As Point)
        Select Case crop_Drag_Handle
            Case EditorGeometry.CropHandle.None
                ' Drawing a new frame - the same normalisation every shape tool uses, so
                ' dragging up and to the left works exactly as dragging down and right.
                Dim corner As Point = imagePoint
                If (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
                    corner = EditorGeometry.ConstrainToSquare(gesture_Anchor, imagePoint)
                End If
                crop_Rect = EditorGeometry.ClampCropRect(
                    EditorGeometry.NormalizeDrag(gesture_Anchor, corner), image_Bitmap.Size)

            Case EditorGeometry.CropHandle.Inside
                crop_Rect = EditorGeometry.MoveCrop(crop_Drag_Start_Rect,
                                                    imagePoint.X - crop_Drag_Origin.X,
                                                    imagePoint.Y - crop_Drag_Origin.Y,
                                                    image_Bitmap.Size)

            Case Else
                crop_Rect = EditorGeometry.ResizeCrop(crop_Drag_Start_Rect, crop_Drag_Handle,
                                                      imagePoint, image_Bitmap.Size)
        End Select

        ShowCropSize()
    End Sub

    Private Sub Canvas_MouseUp(sender As Object, e As MouseEventArgs)
        If Not gesture_Active OrElse e.Button <> MouseButtons.Left Then Return
        gesture_Active = False

        If current_Tool = EditorTool.Crop Then
            ' A click rather than a drag: the user pointed at the picture with the crop
            ' tool selected. A one-pixel frame is not what they meant, and leaving it there
            ' would put a dimmed screen in front of them with no way to read what happened.
            If crop_Rect.Width < Crop_Min_Drag_Pixels AndAlso crop_Rect.Height < Crop_Min_Drag_Pixels Then
                ClearCropFrame()
            Else
                UpdateCropChrome()
            End If
            crop_Drag_Handle = EditorGeometry.CropHandle.None
            canvas.Invalidate()
            Return
        End If

        ' A click with a shape tool selected is not a shape - it is a click. Committing a
        ' zero-sized rectangle would put nothing on screen and still cost an undo step,
        ' so the snapshot goes back where it came from.
        If Not GestureHasContent() Then
            DiscardGestureSnapshot()
            brush_Points.Clear()
            canvas.Invalidate()
            Return
        End If

        If image_Bitmap IsNot Nothing Then
            Using g As Graphics = Graphics.FromImage(image_Bitmap)
                PaintGesture(g)
            End Using
        End If

        brush_Points.Clear()
        dirty = True
        ' Not Invalidate: the pixels behind the canvas' scaled copy changed, so the copy
        ' has to go or the stroke would only appear on the next resize.
        canvas.PixelsChanged()
        ApplySaveAvailability()
    End Sub

    ''' <summary>Abandons a stroke in flight, snapshot included. True when there was one -
    ''' the caller uses that to decide whether Escape also closes the window.</summary>
    Private Function CancelGesture() As Boolean
        If Not gesture_Active Then Return False
        gesture_Active = False

        If current_Tool = EditorTool.Crop Then
            ' Escape in the middle of dragging a frame undoes THAT drag: a resize goes back
            ' to the frame as it was grabbed, while a frame being drawn from scratch has no
            ' earlier state to go back to and simply goes away.
            If crop_Drag_Handle = EditorGeometry.CropHandle.None Then
                ClearCropFrame()
            Else
                crop_Rect = crop_Drag_Start_Rect
                crop_Drag_Handle = EditorGeometry.CropHandle.None
                UpdateCropChrome()
            End If
            canvas.Invalidate()
            Return True
        End If

        DiscardGestureSnapshot()
        brush_Points.Clear()
        canvas.Invalidate()
        Return True
    End Function

    ' -------------------------------------------------------------------- crop ----

    Private Sub Canvas_MouseDoubleClick(sender As Object, e As MouseEventArgs)
        If current_Tool <> EditorTool.Crop OrElse Not crop_Has_Frame Then Return
        If e.Button <> MouseButtons.Left OrElse image_Bitmap Is Nothing Then Return

        ' Only INSIDE the frame (§6.1). A double click outside it has just drawn - and
        ' immediately redrawn - a new frame, and applying that would cut the picture down
        ' to whatever the second click happened to land on.
        Dim fit As Rectangle = canvas.ImageRect()
        Dim on_Canvas As Rectangle = EditorGeometry.ImageToCanvas(crop_Rect, fit, image_Bitmap.Size)
        If EditorGeometry.CropHandleAt(on_Canvas, e.Location, Crop_Grip_Tolerance) <> EditorGeometry.CropHandle.Inside Then Return

        ApplyCrop()
    End Sub

    Private Sub btn_Apply_Crop_Click(sender As Object, e As EventArgs) Handles btn_Apply_Crop.Click
        ApplyCrop()
    End Sub

    ''' <summary>
    ''' Cuts the picture down to the frame (§6.1) - the one edit in this window that
    ''' changes the picture's SIZE, which is why the canvas is handed a new bitmap rather
    ''' than told its pixels changed: it re-derives the fit from what it holds.
    '''
    ''' The undo snapshot is taken after the new bitmap exists and before the old one is
    ''' let go, so a failure half way through costs nothing at all - neither the picture
    ''' nor a history entry pointing at a bitmap that was never installed.
    ''' </summary>
    Private Sub ApplyCrop()
        If saving OrElse Not crop_Has_Frame OrElse image_Bitmap Is Nothing Then Return

        Dim rect As Rectangle = EditorGeometry.ClampCropRect(crop_Rect, image_Bitmap.Size)
        If rect.Width <= 0 OrElse rect.Height <= 0 Then Return

        ' A frame around the whole picture is not an edit. Applying it would still cost an
        ' undo step and mark the file changed, for a result identical to the original.
        If rect.Width = image_Bitmap.Width AndAlso rect.Height = image_Bitmap.Height Then
            ClearCropFrame()
            Return
        End If

        Dim cropped As Bitmap
        Try
            cropped = EditorImageOps.CropTo(image_Bitmap, rect)
        Catch ex As Exception
            AppFileLogger.LogException("Image editor: applying the crop", ex)
            SetStatus(Localization.TF("Не удалось обрезать: {0}", ex.Message))
            Return
        End Try
        If cropped Is Nothing Then Return

        undo_History.Push(image_Bitmap)

        Dim replaced As Bitmap = image_Bitmap
        image_Bitmap = cropped
        canvas.Bitmap = image_Bitmap
        replaced.Dispose()

        ClearCropFrame()
        dirty = True
        ApplySaveAvailability()
    End Sub

    ''' <summary>Takes the frame away if there is one, and says whether there was - which
    ''' is how Escape decides between dismissing the frame and closing the window.</summary>
    Private Function ClearCropFrameIfAny() As Boolean
        If Not crop_Has_Frame Then Return False
        ClearCropFrame()
        Return True
    End Function

    Private Sub ClearCropFrame()
        crop_Has_Frame = False
        crop_Rect = Rectangle.Empty
        crop_Drag_Handle = EditorGeometry.CropHandle.None
        UpdateCropChrome()
        If canvas IsNot Nothing Then canvas.Invalidate()
    End Sub

    ''' <summary>The "Apply crop" button exists exactly as long as the frame does, and the
    ''' status line says what leaving the picture would be - the two facts a person needs
    ''' before pressing Enter.</summary>
    Private Sub UpdateCropChrome()
        If btn_Apply_Crop IsNot Nothing Then btn_Apply_Crop.Visible = crop_Has_Frame AndAlso Not saving
        If crop_Has_Frame Then
            ShowCropSize()
        Else
            ApplySaveAvailability()
        End If
    End Sub

    Private Sub ShowCropSize()
        SetStatus(Localization.TF("обрезка: {0} × {1}", crop_Rect.Width, crop_Rect.Height))
    End Sub

    Private Sub DiscardGestureSnapshot()
        If Not gesture_Snapshot_Taken Then Return
        gesture_Snapshot_Taken = False
        Dim snapshot As Bitmap = undo_History.Pop()
        If snapshot IsNot Nothing Then snapshot.Dispose()
    End Sub

    Private Function GestureHasContent() As Boolean
        If current_Tool = EditorTool.Brush Then Return brush_Points.Count > 0
        Dim rect As Rectangle = EditorGeometry.NormalizeDrag(gesture_Anchor, gesture_Current)
        Return rect.Width > 0 OrElse rect.Height > 0
    End Function

    ' ----------------------------------------------------------------- drawing ----

    ''' <summary>
    ''' The gesture, drawn in image coordinates. Called with the bitmap's own Graphics to
    ''' commit it, and with the canvas' Graphics under a scale transform to preview it -
    ''' which is what guarantees the two agree.
    ''' </summary>
    Private Sub PaintGesture(g As Graphics)
        g.SmoothingMode = SmoothingMode.AntiAlias

        If current_Tool = EditorTool.Brush Then
            PaintBrush(g)
            Return
        End If

        Dim rect As Rectangle = EditorGeometry.NormalizeDrag(gesture_Anchor, gesture_Current)
        Select Case current_Tool
            Case EditorTool.RectangleFilled
                Using fill As New SolidBrush(current_Color)
                    g.FillRectangle(fill, rect)
                End Using
            Case EditorTool.EllipseFilled
                Using fill As New SolidBrush(current_Color)
                    g.FillEllipse(fill, rect)
                End Using
            Case EditorTool.EllipseOutline
                Using pen As New Pen(current_Color, current_Thickness)
                    g.DrawEllipse(pen, rect)
                End Using
            Case Else
                Using pen As New Pen(current_Color, current_Thickness)
                    g.DrawRectangle(pen, rect)
                End Using
        End Select
    End Sub

    Private Sub PaintBrush(g As Graphics)
        If brush_Points.Count = 0 Then Return

        ' A click, not a drag. DrawLines over two identical points draws nothing at all,
        ' so the dot has to be a dot: the same diameter the line would have had.
        If brush_Points.Count = 1 Then
            Dim radius As Single = current_Thickness / 2.0F
            Using fill As New SolidBrush(current_Color)
                g.FillEllipse(fill, brush_Points(0).X - radius, brush_Points(0).Y - radius,
                              current_Thickness, current_Thickness)
            End Using
            Return
        End If

        Using pen As New Pen(current_Color, current_Thickness)
            pen.StartCap = LineCap.Round
            pen.EndCap = LineCap.Round
            pen.LineJoin = LineJoin.Round
            g.DrawLines(pen, brush_Points.ToArray())
        End Using
    End Sub

    ''' <summary>
    ''' The rubber band (§6), painted over the picture and never into it until MouseUp.
    '''
    ''' The transform is what makes one drawing routine serve both: translate to where the
    ''' picture lies, scale by how much it was shrunk to fit, and image coordinates land
    ''' exactly where the committed pixels will. The pen scales with it, so a 4-pixel line
    ''' previews as the fraction of a screen pixel it honestly is on a 24-megapixel photo.
    ''' </summary>
    Private Sub Canvas_Paint(sender As Object, e As PaintEventArgs)
        If image_Bitmap Is Nothing Then Return

        Dim fit As Rectangle = canvas.ImageRect()
        If fit.Width <= 0 OrElse fit.Height <= 0 Then Return

        ' The crop frame is not a rubber band: it stays on screen after the mouse comes up,
        ' which is the whole point of it (§6.1), so it is painted whether a drag is in
        ' flight or not - and in CANVAS coordinates, because its grips are screen-sized.
        If current_Tool = EditorTool.Crop Then
            If crop_Has_Frame Then PaintCropFrame(e.Graphics, fit)
            Return
        End If

        If Not gesture_Active Then Return

        Dim state As GraphicsState = e.Graphics.Save()
        Try
            ' The bitmap's edges clip the committed stroke; the fitted rectangle has to
            ' clip the preview the same way, or half a thick line would show on the margin
            ' and then not be there after the mouse came up.
            e.Graphics.SetClip(fit)
            e.Graphics.TranslateTransform(fit.Left, fit.Top)
            e.Graphics.ScaleTransform(fit.Width / CSng(image_Bitmap.Width),
                                      fit.Height / CSng(image_Bitmap.Height))
            PaintGesture(e.Graphics)
        Finally
            e.Graphics.Restore(state)
        End Try
    End Sub

    ''' <summary>
    ''' The frame, what it keeps and what it throws away (§6.1). The dimming is what makes
    ''' it readable: an outline alone leaves "which side is being kept" to be worked out,
    ''' and on a busy photograph a thin rectangle is easy to lose entirely.
    '''
    ''' The outline is drawn twice, white then a black dash over it, so it is visible on
    ''' both a white sky and a dark suit - a single colour disappears into one of them.
    ''' </summary>
    Private Sub PaintCropFrame(g As Graphics, fit As Rectangle)
        Dim frame As Rectangle = EditorGeometry.ImageToCanvas(crop_Rect, fit, image_Bitmap.Size)

        Using shade As New SolidBrush(Color.FromArgb(120, 0, 0, 0))
            ' Four bands around the frame, clipped to the picture: the margin around a
            ' fitted picture is already the canvas' own dark background, and dimming that
            ' too would just make the window look broken.
            FillBand(g, shade, Rectangle.FromLTRB(fit.Left, fit.Top, fit.Right, frame.Top))
            FillBand(g, shade, Rectangle.FromLTRB(fit.Left, frame.Bottom, fit.Right, fit.Bottom))
            FillBand(g, shade, Rectangle.FromLTRB(fit.Left, frame.Top, frame.Left, frame.Bottom))
            FillBand(g, shade, Rectangle.FromLTRB(frame.Right, frame.Top, fit.Right, frame.Bottom))
        End Using

        Dim outline As New Rectangle(frame.Left, frame.Top, Math.Max(1, frame.Width - 1), Math.Max(1, frame.Height - 1))
        Using white As New Pen(Color.White), black As New Pen(Color.Black)
            black.DashStyle = DashStyle.Dash
            g.DrawRectangle(white, outline)
            g.DrawRectangle(black, outline)
        End Using

        PaintCropGrips(g, frame)
    End Sub

    Private Shared Sub FillBand(g As Graphics, brush As Brush, band As Rectangle)
        If band.Width <= 0 OrElse band.Height <= 0 Then Return
        g.FillRectangle(brush, band)
    End Sub

    ''' <summary>The eight grips, in screen pixels and centred on the corners and edge
    ''' midpoints - the same eight points <see cref="EditorGeometry.CropHandleAt"/> tests
    ''' for, so what is drawn is exactly what can be grabbed.</summary>
    Private Shared Sub PaintCropGrips(g As Graphics, frame As Rectangle)
        Dim middle_X As Integer = frame.Left + frame.Width \ 2
        Dim middle_Y As Integer = frame.Top + frame.Height \ 2
        Dim points As Point() = {
            New Point(frame.Left, frame.Top), New Point(middle_X, frame.Top), New Point(frame.Right, frame.Top),
            New Point(frame.Right, middle_Y), New Point(frame.Right, frame.Bottom),
            New Point(middle_X, frame.Bottom), New Point(frame.Left, frame.Bottom), New Point(frame.Left, middle_Y)}

        Dim half As Integer = Crop_Grip_Size \ 2
        Using fill As New SolidBrush(Color.White), pen As New Pen(Color.Black)
            For Each point As Point In points
                Dim grip As New Rectangle(point.X - half, point.Y - half, Crop_Grip_Size, Crop_Grip_Size)
                g.FillRectangle(fill, grip)
                g.DrawRectangle(pen, grip)
            Next
        End Using
    End Sub

    ' -------------------------------------------------------------------- undo ----

    Private Sub btn_Undo_Click(sender As Object, e As EventArgs) Handles btn_Undo.Click
        UndoLastEdit()
    End Sub

    ''' <summary>
    ''' Puts the previous snapshot back (§8). The picture can change size doing it (a crop
    ''' undone in Ф-4), which is why the canvas is handed the new bitmap rather than just
    ''' invalidated - it re-derives the fit from what it is holding.
    ''' </summary>
    Private Sub UndoLastEdit()
        If saving Then Return
        If gesture_Active Then CancelGesture()
        If Not undo_History.CanUndo Then Return

        ' The step being undone may be a crop, and then the picture is about to change
        ' size: a frame aimed at the small version means nothing on the large one.
        ClearCropFrame()

        Dim previous As Bitmap = undo_History.Pop()
        If previous Is Nothing Then Return

        Dim replaced As Bitmap = image_Bitmap
        image_Bitmap = previous
        canvas.Bitmap = image_Bitmap
        If replaced IsNot Nothing Then replaced.Dispose()

        canvas.Invalidate()
        ApplySaveAvailability()
    End Sub

    ' --------------------------------------------------------------- tool button ----

    ''' <summary>
    ''' A toolbar button that draws its own icon.
    '''
    ''' Not a glyph font: the application is pinned to Microsoft Sans Serif, which has no
    ''' outlined rectangle or filled ellipse, and the Segoe MDL2 codepoints that do are
    ''' easy to get subtly wrong - a wrong one renders as an empty box with nothing to
    ''' say it was wrong. Four GDI+ calls draw exactly the shape the tool produces.
    '''
    ''' Non-focusable for the same reason the viewer's NonFocusButton is: this window has
    ''' KeyPreview on and Ctrl+Z has to reach it, not re-press whatever was last clicked.
    ''' </summary>
    Private NotInheritable Class ToolButton
        Inherits Button

        Friend ReadOnly Tool As EditorTool
        Private is_Selected As Boolean

        Friend Sub New(forTool As EditorTool)
            Me.Tool = forTool
            Me.SetStyle(ControlStyles.Selectable, False)
            Me.TabStop = False
            Me.Text = ""
            Me.FlatStyle = FlatStyle.Flat
            Me.UseVisualStyleBackColor = False
            Me.BackColor = SystemColors.Control
            Me.FlatAppearance.BorderSize = 1
            Me.FlatAppearance.BorderColor = SystemColors.ControlDark
        End Sub

        Friend Property IsSelected As Boolean
            Get
                Return is_Selected
            End Get
            Set(value As Boolean)
                If is_Selected = value Then Return
                is_Selected = value
                Me.BackColor = If(value, SystemColors.Highlight, SystemColors.Control)
                Me.Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)

            Dim box As Rectangle = Rectangle.Inflate(Me.ClientRectangle, -10, -10)
            If box.Width <= 0 OrElse box.Height <= 0 Then Return

            Dim ink As Color = If(is_Selected, SystemColors.HighlightText, SystemColors.ControlText)
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias

            Using pen As New Pen(ink, 2.0F), fill As New SolidBrush(ink)
                Select Case Tool
                    Case EditorTool.RectangleOutline
                        e.Graphics.DrawRectangle(pen, box)
                    Case EditorTool.RectangleFilled
                        e.Graphics.FillRectangle(fill, box)
                    Case EditorTool.EllipseOutline
                        e.Graphics.DrawEllipse(pen, box)
                    Case EditorTool.EllipseFilled
                        e.Graphics.FillEllipse(fill, box)
                    Case EditorTool.Crop
                        ' The photographer's crop mark: two L-shaped corners facing each
                        ' other. A full rectangle would read as the rectangle TOOL, which
                        ' sits two buttons away and does something entirely different.
                        Dim arm As Integer = Math.Max(3, box.Width \ 2)
                        e.Graphics.DrawLines(pen, New Point() {
                            New Point(box.Left, box.Top + arm), New Point(box.Left, box.Top), New Point(box.Left + arm, box.Top)})
                        e.Graphics.DrawLines(pen, New Point() {
                            New Point(box.Right, box.Bottom - arm), New Point(box.Right, box.Bottom), New Point(box.Right - arm, box.Bottom)})
                    Case Else
                        ' A curve, not a zig-zag: at 14 pixels a polyline of straight
                        ' segments reads as the letter N rather than as a brush stroke.
                        pen.StartCap = LineCap.Round
                        pen.EndCap = LineCap.Round
                        e.Graphics.DrawCurve(pen, New Point() {
                            New Point(box.Left, box.Bottom),
                            New Point(box.Left + box.Width \ 3, box.Top + box.Height \ 4),
                            New Point(box.Right - box.Width \ 3, box.Bottom - box.Height \ 4),
                            New Point(box.Right, box.Top)})
                End Select
            End Using
        End Sub

    End Class

End Class
#End If
