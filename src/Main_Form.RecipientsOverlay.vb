Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Floating "recipients" panel shown top-left over the media surface.
'
' It restores the original intent of the (now renamed) "keep on top" checkbox:
' while sorting, the user sees the destination folders over the image and clicks
' one to move/copy (or Delete) the current file - one hand on the mouse. See
' docs/specifications/SPECIFICATION_RECIPIENTS_OVERLAY_DOTNET48.md.
'
' The panel is a child of panel_Media (same pattern as the full-screen toolbar
' overlay), narrow-but-tall, sized to the number of *registered* destination
' folders + a Delete row. A click is exactly equivalent to pressing the matching
' key: recipients go through PoMove(slot), Delete through
' ReadShowMediaFile(Mode_Delete) - identical behaviour, no new file logic.
Partial Public Class Main_Form

    Private recipients_Overlay As RecipientsOverlayWindow
    Private recipients_ToolTip As ToolTip
    Private recipients_Overlay_Tracking As Boolean

    ''' <summary>The font the current overlay generation was built with. A Font assigned
    ''' to a control is NOT disposed by Control.Dispose, so without holding it here every
    ''' rebuild (settings closed, form resized, flag toggled) abandoned one.</summary>
    Private recipients_Overlay_Font As Font

    ''' <summary>Final teardown for the overlay's own long-lived bits - called from
    ''' Form1_FormClosing. ApplyRecipientsOverlay handles the per-rebuild half.</summary>
    Private Sub DisposeRecipientsOverlayResources()
        Try
            If recipients_Overlay IsNot Nothing Then
                recipients_Overlay.Close()
                recipients_Overlay.Dispose()
                recipients_Overlay = Nothing
            End If
            If recipients_ToolTip IsNot Nothing Then
                recipients_ToolTip.RemoveAll()
                recipients_ToolTip.Dispose()
                recipients_ToolTip = Nothing
            End If
            If recipients_Overlay_Font IsNot Nothing Then
                recipients_Overlay_Font.Dispose()
                recipients_Overlay_Font = Nothing
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Builds/shows the recipients overlay when <c>Is_Show_Recipients_Overlay</c>
    ''' is on, or tears it down when off. Single entry point - call it after the
    ''' flag changes, when destinations change (settings closed), and once the form
    ''' is shown/sized.
    ''' </summary>
    Friend Sub ApplyRecipientsOverlay()
        ' Tear down any existing overlay first (rebuild from current data).
        If recipients_Overlay IsNot Nothing Then
            Try
                ' RemoveAll BEFORE the window goes: a ToolTip keeps an entry per control it
                ' was set on, so without this it held every generation of dead buttons for
                ' the lifetime of the form.
                If recipients_ToolTip IsNot Nothing Then recipients_ToolTip.RemoveAll()
                recipients_Overlay.Close()
                recipients_Overlay.Dispose()
            Catch
            End Try
            recipients_Overlay = Nothing
        End If

        If recipients_Overlay_Font IsNot Nothing Then
            recipients_Overlay_Font.Dispose()
            recipients_Overlay_Font = Nothing
        End If

        If Not Is_Show_Recipients_Overlay Then Return
        If panel_Media Is Nothing OrElse Not Me.IsHandleCreated Then Return

        Try
            BuildRecipientsOverlay()
            If recipients_Overlay Is Nothing Then Return
            TrackRecipientsOverlayGeometry()
            recipients_Overlay.Owner = Me
            PositionRecipientsOverlay()
            recipients_Overlay.Show()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2400: recipients overlay build failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>The overlay is a window of its own, so - unlike a child panel - it does
    ''' not travel with the form for free. Subscribed once; the handlers no-op while the
    ''' overlay is off.</summary>
    Private Sub TrackRecipientsOverlayGeometry()
        If recipients_Overlay_Tracking Then Return
        recipients_Overlay_Tracking = True
        AddHandler Me.LocationChanged, Sub() KeepRecipientsOverlayOnTop()
        AddHandler Me.SizeChanged, Sub() KeepRecipientsOverlayOnTop()
    End Sub

    ''' <summary>Re-asserts the overlay's position and z-order after the media
    ''' surface changed (video/webbrowser can BringToFront over it). Cheap no-op
    ''' when the overlay is off.</summary>
    Friend Sub KeepRecipientsOverlayOnTop()
        If recipients_Overlay Is Nothing OrElse Not Is_Show_Recipients_Overlay Then Return
        Try
            PositionRecipientsOverlay()
        Catch
        End Try
    End Sub

    Private Sub BuildRecipientsOverlay()
        Dim rus As Boolean = Is_Russian_Language

        ' Rows: registered destinations (keys 1..9, then 0) + Delete last.
        ' Item1 = slot (1..10; -1 = delete). Runtime convention: key "k" -> slot k,
        ' key "0" -> slot 10 (matches the keyboard, NOT the legacy grid DoKey).
        Dim rows As New List(Of Tuple(Of Integer, String))()
        For k As Integer = 1 To 9
            Dim keyPath As String = Hardkeys_to_move_mediafile(k)
            If Not String.IsNullOrEmpty(keyPath) Then rows.Add(Tuple.Create(k, k.ToString() & "  " & keyPath))
        Next
        Dim zeroPath As String = Hardkeys_to_move_mediafile(10)
        If Not String.IsNullOrEmpty(zeroPath) Then rows.Add(Tuple.Create(10, "0  " & zeroPath))
        ' Delete is always available (a valid sort action even with no folders set).
        rows.Add(Tuple.Create(-1, ChrW(215) & "  " & Localization.T("Удалить")))

        If recipients_ToolTip Is Nothing Then recipients_ToolTip = New ToolTip()

        ' Held in a field so the NEXT rebuild can dispose it (see ApplyRecipientsOverlay).
        Dim ui_Font As New Font(Me.Font.FontFamily, RecipientsOverlayFontSize(), Me.Font.Style)
        recipients_Overlay_Font = ui_Font
        Dim rowH As Integer = ui_Font.Height + LogicalToDeviceUnits(10)

        ' Width from the widest caption, capped so it never eats more than half the
        ' media panel (longer paths ellipsize; full path stays in the tooltip).
        Dim maxTextW As Integer = 0
        For Each r In rows
            Dim w As Integer = TextRenderer.MeasureText(r.Item2, ui_Font).Width
            If w > maxTextW Then maxTextW = w
        Next
        Dim desiredW As Integer = LogicalToDeviceUnits(RecipientsOverlayWidth())
        Dim capW As Integer = Math.Min(LogicalToDeviceUnits(480), CInt(panel_Media.ClientSize.Width * 0.5))
        If capW < LogicalToDeviceUnits(120) Then capW = LogicalToDeviceUnits(120)   ' usable floor on tiny windows
        Dim panelW As Integer = Math.Min(desiredW, capW)

        ' WinForms does not alpha-compose a normal child Panel over its sibling
        ' PictureBox/VideoView, so the overlay needs a layered window to let the
        ' configured opacity reveal the media beneath it. A layered CHILD window is not
        ' it: WS_EX_LAYERED on a child fails CreateWindowEx outright here ("Error
        ' creating window handle" in current.log, reproduced on .NET 10 / Windows 11),
        ' which is why the table never appeared. A borderless owned top-level window is
        ' layered by the shell in the normal way - and it also floats over the LibVLC
        ' VideoView, which a sibling control cannot reliably do.
        Dim overlay As New RecipientsOverlayWindow With {
            .Name = "recipients_Overlay",
            .BackColor = Color.FromArgb(32, 32, 32),
            .Opacity = Math.Max(0.15R, Math.Min(1.0R, RecipientsOverlayAlpha() / 255.0R)),
            .Padding = New Padding(0)
        }

        ' Mainline: every recipient row carries a second, narrow zone that COPIES into the
        ' same folder - the row itself still moves, so the muscle memory is untouched and
        ' copying stops being a hidden global mode. The delete row stays one action: a
        ' deletion has no copy. On net48 the row keeps its single meaning, which the
        ' global copy-mode checkbox there decides (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.3).
        Dim copy_Caption As String = ""
        Dim copyW As Integer = 0
#If Not NETFRAMEWORK Then
        copy_Caption = Localization.T("копия")
        copyW = Math.Max(LogicalToDeviceUnits(38),
                         TextRenderer.MeasureText(copy_Caption, ui_Font).Width + LogicalToDeviceUnits(14))
        ' A narrow window must not lose the path to the copy zone: below a usable width the
        ' row goes back to move-only, and the copy stays reachable by Shift+digit.
        If copyW > panelW - LogicalToDeviceUnits(80) Then copyW = 0
#End If

        Dim y As Integer = 0
        Dim visibleRows As Integer = RecipientsOverlayVisibleRows()
        Dim scrollRows As Boolean = rows.Count > visibleRows
        overlay.AutoScroll = scrollRows
        For Each r In rows
            Dim isDelete As Boolean = (r.Item1 = -1)
            Dim showCopy As Boolean = Not isDelete AndAlso copyW > 0
            Dim b As New NonFocusButton() With {
                .Text = r.Item2,
                .Tag = r.Item1,
                .Font = ui_Font,
                .FlatStyle = FlatStyle.Flat,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Padding = New Padding(LogicalToDeviceUnits(6), 0, LogicalToDeviceUnits(2), 0),
                .AutoEllipsis = True,
                .TabStop = False,
                .UseVisualStyleBackColor = False,
                .Location = New Point(0, y),
                .Size = New Size(If(showCopy, panelW - copyW, panelW), rowH),
                .ForeColor = If(isDelete, Color.White, Color.Gainsboro),
                .BackColor = If(isDelete, Color.FromArgb(96, 32, 32), Color.FromArgb(52, 52, 52))
            }
            b.FlatAppearance.BorderSize = 0
            b.FlatAppearance.MouseOverBackColor = If(isDelete, Color.FromArgb(140, 44, 44), Color.FromArgb(72, 72, 72))
            b.FlatAppearance.MouseDownBackColor = If(isDelete, Color.FromArgb(170, 50, 50), Color.FromArgb(96, 96, 96))
            ' Full path in the tooltip - the caption may be ellipsized.
            If Not isDelete Then
                recipients_ToolTip.SetToolTip(b, If(showCopy,
                                                    Localization.TF("Перенести в: {0}", Hardkeys_to_move_mediafile(r.Item1)),
                                                    Hardkeys_to_move_mediafile(r.Item1)))
            End If
            AddHandler b.Click, AddressOf RecipientOverlayButton_Click
            overlay.Controls.Add(b)

#If Not NETFRAMEWORK Then
            If showCopy Then
                Dim c As New NonFocusButton() With {
                    .Text = copy_Caption,
                    .Tag = r.Item1,
                    .Font = ui_Font,
                    .FlatStyle = FlatStyle.Flat,
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .Padding = New Padding(0),
                    .AutoEllipsis = True,
                    .TabStop = False,
                    .UseVisualStyleBackColor = False,
                    .Location = New Point(panelW - copyW, y),
                    .Size = New Size(copyW, rowH),
                    .ForeColor = Color.Gainsboro,
                    .BackColor = Color.FromArgb(38, 62, 38)
                }
                c.FlatAppearance.BorderSize = 0
                c.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 88, 52)
                c.FlatAppearance.MouseDownBackColor = Color.FromArgb(66, 112, 66)
                recipients_ToolTip.SetToolTip(c, Localization.TF("Скопировать в: {0}", Hardkeys_to_move_mediafile(r.Item1)))
                AddHandler c.Click, AddressOf RecipientOverlayCopyButton_Click
                overlay.Controls.Add(c)
            End If
#End If

            y += rowH
        Next

        overlay.ClientSize = New Size(panelW, If(scrollRows, rowH * visibleRows, y))
        recipients_Overlay = overlay
    End Sub

    Private Sub PositionRecipientsOverlay()
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return

        ' A window of its own is not clipped by the form, so it must hide itself whenever
        ' the media surface is not on screen - minimized, or the form hidden.
        If Me.WindowState = FormWindowState.Minimized OrElse Not Me.Visible OrElse
           Not panel_Media.Visible OrElse panel_Media.ClientSize.Width <= 0 Then
            If recipients_Overlay.Visible Then recipients_Overlay.Visible = False
            Return
        End If
        If Not recipients_Overlay.Visible AndAlso recipients_Overlay.IsHandleCreated Then recipients_Overlay.Visible = True

        Dim margin As Integer = LogicalToDeviceUnits(8)
        Dim topOffset As Integer = margin
        ' In full-screen (not super) the floating toolbar sits over the top strip;
        ' drop the overlay below it so it never covers the toolbar buttons.
        If is_Full_Screen_Mode AndAlso Not is_Super_Full_Screen_Mode AndAlso
           flow_Toolbar IsNot Nothing AndAlso flow_Toolbar.Parent Is panel_Media Then
            topOffset = flow_Toolbar.Height + margin
        End If
        Dim x As Integer = margin
        Dim y As Integer = topOffset
        Select Case RecipientsOverlayPosition()
            Case "topRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
            Case "bottomLeft"
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
            Case "bottomRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
        End Select

        ' The corner is the media area's, expressed on screen - the overlay is a separate
        ' window, so its Location is in screen coordinates, not the form's client space.
        Dim onScreen As Point = panel_Media.PointToScreen(New Point(x, y))
        If recipients_Overlay.Location <> onScreen Then recipients_Overlay.Location = onScreen
    End Sub

    ''' <summary>A click on a recipient row is exactly the matching key press.</summary>
    Private Sub RecipientOverlayButton_Click(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b Is Nothing OrElse b.Tag Is Nothing Then Return
        Dim slot As Integer = CInt(b.Tag)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2410: recipients overlay click, slot=" & slot.ToString())

        ' Match the keyboard path: it stops the slideshow first (KeybUse).
        SlideShowStop()
        If slot = -1 Then
            ReadShowMediaFile(Mode_Delete)
        Else
            PoMove(slot)
        End If

        ' The media surface may have changed under us - stay clickable on top.
        KeepRecipientsOverlayOnTop()
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>A click on a row's copy zone is exactly Shift + that digit.</summary>
    Private Sub RecipientOverlayCopyButton_Click(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b Is Nothing OrElse b.Tag Is Nothing Then Return
        Dim slot As Integer = CInt(b.Tag)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2411: recipients overlay copy click, slot=" & slot.ToString())

        SlideShowStop()
        ExecuteRecipientAction(slot, RecipientActionKind.Copy)
        KeepRecipientsOverlayOnTop()
    End Sub
#End If

    ''' <summary>A flat button that never takes keyboard focus, so clicking a row
    ''' does not steal focus from the media surface (the app is keyboard-driven;
    ''' with focus here, Space/Enter would re-activate the button via KeyPreview).</summary>
    Private NotInheritable Class NonFocusButton
        Inherits Button

        Public Sub New()
            SetStyle(ControlStyles.Selectable, False)
            Me.TabStop = False
        End Sub
    End Class

    ''' <summary>
    ''' The overlay's host: a borderless owned window, so Form.Opacity gives it a real
    ''' compositor alpha channel over the media (a child control cannot alpha-compose over
    ''' a sibling PictureBox/VideoView, and WS_EX_LAYERED on a CHILD window fails to be
    ''' created at all - see the comment at BuildRecipientsOverlay). It never activates:
    ''' the app is keyboard-driven and a click here must not take focus from the viewer.
    ''' </summary>
    Private NotInheritable Class RecipientsOverlayWindow
        Inherits Form

        Private Const WS_EX_NOACTIVATE As Integer = &H8000000
        Private Const WS_EX_TOOLWINDOW As Integer = &H80

        Public Sub New()
            FormBorderStyle = FormBorderStyle.None
            StartPosition = FormStartPosition.Manual
            ShowInTaskbar = False
            ControlBox = False
            MinimizeBox = False
            MaximizeBox = False
            KeyPreview = False
            AutoScaleMode = AutoScaleMode.None
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim parameters As CreateParams = MyBase.CreateParams
                parameters.ExStyle = parameters.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
                Return parameters
            End Get
        End Property
    End Class

End Class
