Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
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

    Private recipients_Overlay As Panel
    Private recipients_ToolTip As ToolTip

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
                If recipients_Overlay.Parent IsNot Nothing Then recipients_Overlay.Parent.Controls.Remove(recipients_Overlay)
                recipients_Overlay.Dispose()
            Catch
            End Try
            recipients_Overlay = Nothing
        End If

        If Not Is_Show_Recipients_Overlay Then Return
        If panel_Media Is Nothing Then Return

        Try
            BuildRecipientsOverlay()
            If recipients_Overlay Is Nothing Then Return
            panel_Media.Controls.Add(recipients_Overlay)
            PositionRecipientsOverlay()
            recipients_Overlay.BringToFront()
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2400: recipients overlay build failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Re-asserts the overlay's position and z-order after the media
    ''' surface changed (video/webbrowser can BringToFront over it). Cheap no-op
    ''' when the overlay is off.</summary>
    Friend Sub KeepRecipientsOverlayOnTop()
        If recipients_Overlay Is Nothing OrElse Not Is_Show_Recipients_Overlay Then Return
        Try
            PositionRecipientsOverlay()
            recipients_Overlay.BringToFront()
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
        rows.Add(Tuple.Create(-1, ChrW(215) & "  " & If(rus, "Удалить", "Delete")))

        If recipients_ToolTip Is Nothing Then recipients_ToolTip = New ToolTip()

        Dim ui_Font As New Font(Me.Font.FontFamily, RecipientsOverlayFontSize(), Me.Font.Style)
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

        Dim overlay As New Panel With {
            .Name = "recipients_Overlay",
            .BackColor = Color.FromArgb(RecipientsOverlayAlpha(), 32, 32, 32),
            .Padding = New Padding(0),
            .Margin = New Padding(0)
        }

        Dim y As Integer = 0
        Dim visibleRows As Integer = RecipientsOverlayVisibleRows()
        Dim scrollRows As Boolean = rows.Count > visibleRows
        overlay.AutoScroll = scrollRows
        For Each r In rows
            Dim isDelete As Boolean = (r.Item1 = -1)
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
                .Size = New Size(panelW, rowH),
                .ForeColor = If(isDelete, Color.White, Color.Gainsboro),
                .BackColor = If(isDelete, Color.FromArgb(RecipientsOverlayAlpha(), 96, 32, 32), Color.FromArgb(RecipientsOverlayAlpha(), 52, 52, 52))
            }
            b.FlatAppearance.BorderSize = 0
            b.FlatAppearance.MouseOverBackColor = If(isDelete, Color.FromArgb(140, 44, 44), Color.FromArgb(72, 72, 72))
            b.FlatAppearance.MouseDownBackColor = If(isDelete, Color.FromArgb(170, 50, 50), Color.FromArgb(96, 96, 96))
            ' Full path in the tooltip - the caption may be ellipsized.
            If Not isDelete Then recipients_ToolTip.SetToolTip(b, Hardkeys_to_move_mediafile(r.Item1))
            AddHandler b.Click, AddressOf RecipientOverlayButton_Click
            overlay.Controls.Add(b)
            y += rowH
        Next

        overlay.Size = New Size(panelW, If(scrollRows, rowH * visibleRows, y))
        recipients_Overlay = overlay
    End Sub

    Private Sub PositionRecipientsOverlay()
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return
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
        recipients_Overlay.Location = New Point(x, y)
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

End Class
