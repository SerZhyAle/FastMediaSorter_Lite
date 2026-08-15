Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics

Partial Public Class Main_Form

    Private Sub HandlePictureBoxMouseDown(sender As Object, e As MouseEventArgs)
        ' Store the initial mouse position for drag detection
        mouse_Down_Start_Point = e.Location

        ' PRIORITY 1: Check modifier keys FIRST (these execute immediately without delay)
        '
        ' All three jumps go through JumpBy: the index moves inside the pipeline, after
        ' the checks that can still refuse the call. Doing it here first meant a refused
        ' call (throttle, busy worker) left the index moved and the screen not - and the
        ' next delete then took out whatever the index now pointed at. The "not enough
        ' files" guards are gone with it: the pipeline clamps, exactly as Home/End
        ' always have.
        If (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Shift+LeftClick - jumping +10 files")
                SlideShowStop()
                JumpBy(10, "+10 файлов")
                Return
            ElseIf e.Button = MouseButtons.Right Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Shift+RightClick - jumping -10 files")
                SlideShowStop()
                JumpBy(-10, "-10 файлов")
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Ctrl+LeftClick - jumping +100 files")
                SlideShowStop()
                JumpBy(100, "+100 файлов")
                Return
            ElseIf e.Button = MouseButtons.Right Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Ctrl+RightClick - jumping -100 files")
                SlideShowStop()
                JumpBy(-100, "-100 файлов")
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Alt+LeftClick - jumping +1000 files")
                SlideShowStop()
                JumpBy(1000, "+1000 файлов")
                Return
            ElseIf e.Button = MouseButtons.Right Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Alt+RightClick - jumping -1000 files")
                SlideShowStop()
                JumpBy(-1000, "-1000 файлов")
                Return
            End If
        End If

        ' PRIORITY 2: DOUBLE-CLICK DETECTION (always active for left/middle buttons)
        If e.Button = MouseButtons.Left OrElse e.Button = MouseButtons.Middle Then
            Dim current_Click_Time As DateTime = DateTime.Now
            Dim time_Since_Last_Click As Double = (current_Click_Time - last_Media_Area_Click_Time).TotalMilliseconds

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1147: Click, time since last: " & time_Since_Last_Click.ToString("F0") & "ms (threshold: " & DoubleClickTimeThreshold.ToString() & "ms)")

            If time_Since_Last_Click < DoubleClickTimeThreshold AndAlso time_Since_Last_Click > 0 Then
                ' DOUBLE-CLICK DETECTED - toggle fullscreen
                pending_Single_Click_Timer.Stop()
                pending_Single_Click_Event = Nothing

                is_Full_Screen_Mode = Not is_Full_Screen_Mode
                If Not is_Full_Screen_Mode Then is_Super_Full_Screen_Mode = False
                SetViewSizes()
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1150: DOUBLE-CLICK - fullscreen toggled to: " & is_Full_Screen_Mode.ToString())

                last_Media_Area_Click_Time = DateTime.MinValue
                last_Media_Area_Click_Button = MouseButtons.None
                SlideShowStop()

                Return
            End If

            ' Single click - store timestamp for double-click detection
            last_Media_Area_Click_Time = current_Click_Time
            last_Media_Area_Click_Button = e.Button
        End If

        ' PRIORITY 3: DELAY LEFT-CLICK to allow double-click detection
        If e.Button = MouseButtons.Left AndAlso zoom_Scale = 1 Then
            ' Always delay to allow double-click detection (enter/exit fullscreen)
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = e
            pending_Single_Click_Timer.Start()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1148: Left-click delayed for double-click detection")
        Else
            ' Right-click, middle-click, or zoomed - execute immediately
            If zoom_Scale = 1 OrElse e.Button <> MouseButtons.Left Then
                MouseUse(e)
            End If
        End If
    End Sub

    Private Sub PictureBox1_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseDown
        HandlePictureBoxMouseDown(sender, e)
    End Sub

    Private Sub PictureBox2_MouseDown(sender As Object, e As MouseEventArgs) Handles Picture_Box_2.MouseDown
        HandlePictureBoxMouseDown(sender, e)
    End Sub

    Private Sub MouseUse(ByVal e As MouseEventArgs)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1170: MouseUse Delta: " & e.Delta.ToString)

        SlideShowStop()

#If Not NETFRAMEWORK Then
        ' Modern: one place decides what the wheel means (zoom vs flip) - see
        ' Main_Form.Zoom.vb. It consumes the event only when it actually zoomed, so
        ' everything below (including the historical modifier handling) still runs
        ' untouched when it declines.
        If TryHandleWheelZoom(e) Then Return
#End If

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            SkipZoom()

        ElseIf e.Delta <> 0 AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible) AndAlso (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            ' SHIFT + Scroll: Set to original 1:1 resolution
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1174: zoom to original 1:1 resolution")

            Dim active_Image As Image = Nothing
            If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                active_Image = Picture_Box_1.Image
            ElseIf is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                active_Image = Picture_Box_2.Image
            End If

            If active_Image IsNot Nothing Then
                ' Calculate position to center the image at original size.
                ' Picture-box coordinates are relative to panel_Media, so the
                ' top offset is 0 and the available space is the panel client.
                Dim top_first_row = 0
                Dim available_Width = panel_Media.ClientSize.Width
                Dim available_Height = panel_Media.ClientSize.Height

                ' Set to original image dimensions
                Dim new_Width As Integer = active_Image.Width
                Dim new_Height As Integer = active_Image.Height

                ' Center the image in the available space
                Dim new_Left As Integer = (available_Width - new_Width) \ 2
                Dim new_Top As Integer = top_first_row + (available_Height - new_Height) \ 2

                ' Ensure the image is not positioned off-screen
                new_Left = Math.Max(new_Left, -new_Width + 100) ' Allow some off-screen but keep 100px visible
                new_Top = Math.Max(new_Top, top_first_row)

                Picture_Box_1.Width = new_Width
                Picture_Box_1.Height = new_Height
                Picture_Box_1.Left = new_Left
                Picture_Box_1.Top = new_Top

                Picture_Box_2.Size = Picture_Box_1.Size
                Picture_Box_2.Location = Picture_Box_1.Location

                ' The box moved under a possibly-still-held button - re-anchor the pan.
                RebasePan(Picture_Box_1.Location)

                ' Set zoom_Scale to 0 as flag for 1:1 mode
                zoom_Scale = 0.0F
                lbl_Zoom.Text = "1:1"

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1175: 1:1 resolution set: " & new_Width.ToString & "x" & new_Height.ToString & " at " & new_Left.ToString & "," & new_Top.ToString)
            End If

        ElseIf e.Delta <> 0 AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible) AndAlso (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            Dim zoom_Scale_Factor As Single = If(e.Delta > 0, 1.1F, 0.9F)

            Dim old_Width As Integer = Picture_Box_1.Width
            Dim old_Height As Integer = Picture_Box_1.Height
            Dim old_Left As Integer = Picture_Box_1.Left
            Dim old_Top As Integer = Picture_Box_1.Top

            Dim new_Width As Integer = CInt(old_Width * zoom_Scale_Factor)
            Dim new_Height As Integer = CInt(old_Height * zoom_Scale_Factor)

            ' The wheel event is raised on the form, so its coordinates are in
            ' form-client space. Translate into panel_Media space to match the
            ' picture box's (panel-relative) Left/Top for cursor-centred zoom.
            Dim cursor_On_Panel As Point = panel_Media.PointToClient(Me.PointToScreen(e.Location))
            Dim mouse_X As Integer = cursor_On_Panel.X - old_Left
            Dim mouse_Y As Integer = cursor_On_Panel.Y - old_Top

            Dim relative_X As Single = CSng(mouse_X / old_Width)
            Dim relative_Y As Single = CSng(mouse_Y / old_Height)

            Dim new_Left As Integer = CInt(old_Left - (new_Width - old_Width) * relative_X)
            Dim new_Top As Integer = CInt(old_Top - (new_Height - old_Height) * relative_Y)

            Picture_Box_1.Width = new_Width
            Picture_Box_1.Height = new_Height
            Picture_Box_1.Left = new_Left
            Picture_Box_1.Top = new_Top

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1173: new size: " & new_Width.ToString & "-" & new_Height.ToString & " " & new_Left.ToString & "-" & new_Top.ToString)

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location

            ' The box moved under a possibly-still-held button - re-anchor the pan.
            RebasePan(Picture_Box_1.Location)

            zoom_Scale = If(zoom_Scale = 0, 1, zoom_Scale) * zoom_Scale_Factor
            lbl_Zoom.Text = "" & zoom_Scale.ToString("F2")
        Else
            Select Case e.Delta
                Case Is < 0
                    ReadShowMediaFile(Mode_Next)
                Case Is > 0
                    ReadShowMediaFile(Mode_Prev)
                Case 0
                    Select Case e.Button
                        Case MouseButtons.Left
                            ReadShowMediaFile(Mode_Next) ' next
                        Case MouseButtons.Right
                            If Not is_WebBrowser_Visible Then
                                ReadShowMediaFile(Mode_Prev)
                            End If
                        Case System.Windows.Forms.MouseButtons.Middle
#If NETFRAMEWORK Then
                            RenameCurrentFile()
#Else
                            ' Modern: the middle button opens the picture menu - rename is
                            ' one of its items, so the historical command is still here,
                            ' next to everything else that can be done to the picture. It
                            ' declines when there is no picture on screen (a playing video,
                            ' an empty window), and then the old rename stands.
                            If Not TryShowImageContextMenu() Then RenameCurrentFile()
#End If
                        Case System.Windows.Forms.MouseButtons.XButton1
                            ReadShowMediaFile(Mode_Next)
                        Case System.Windows.Forms.MouseButtons.XButton2
                            ReadShowMediaFile(Mode_Prev)
                    End Select
            End Select
        End If

    End Sub

    Private Sub Form1_MouseDown(sender As Object, e As MouseEventArgs) Handles MyBase.MouseDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1660: Form MouseDown")
        MouseUse(e)
    End Sub

    Private Sub Form1_MouseWheel(sender As Object, e As MouseEventArgs) Handles Me.MouseWheel
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1670: Form MouseWheel")
        MouseUse(e)
    End Sub

    Private Sub lbl_Zoom_MouseDown(sender As Object, e As MouseEventArgs) Handles lbl_Zoom.MouseDown
        SkipZoom()
    End Sub

    Private Sub Picture_Box_1_MouseMove(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseMove
        Pic_MouseMove(sender, e)
    End Sub

    Private Sub Picture_Box_2_MouseMove(sender As Object, e As MouseEventArgs) Handles Picture_Box_2.MouseMove
        Pic_MouseMove(sender, e)
    End Sub

    ''' <summary>Starts panning: remembers WHERE INSIDE the box the user grabbed it, in
    ''' panel coordinates (the panel does not move; the box does).</summary>
    Private Sub BeginPan()
        If panel_Media Is Nothing Then Return
        is_Dragging = True
        Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
        drag_Grab_Offset = New Size(cursor_On_Panel.X - Picture_Box_1.Left, cursor_On_Panel.Y - Picture_Box_1.Top)
        last_Drag_Update_Time = DateTime.Now
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2475: pic_MouseMove - drag started, grab offset " & drag_Grab_Offset.ToString())
    End Sub

    ''' <summary>Re-takes the grab offset after something OTHER than the drag moved the
    ''' box - a zoom step while the button is still held. Without it the next mouse move
    ''' hauls the box back to the pan's starting geometry and throws the zoom anchor
    ''' away.</summary>
    Private Sub RebasePan(box_Location As Point)
        If panel_Media Is Nothing OrElse Not is_Dragging Then Return
        Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
        drag_Grab_Offset = New Size(cursor_On_Panel.X - box_Location.X, cursor_On_Panel.Y - box_Location.Y)
    End Sub

    Private Sub EndPan()
        If Not is_Dragging Then Return
        is_Dragging = False
        last_Drag_Update_Time = DateTime.MinValue
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2477: pic_MouseMove - drag ended")
    End Sub

    ''' <summary>The drag ends when the button is released - even if the mouse never
    ''' moves again. That used to be noticed only lazily, by the next move without a
    ''' button: release without moving, press again, and the stale base teleported the
    ''' box back to where the previous drag had begun.</summary>
    Private Sub Picture_Box_MouseUp(sender As Object, e As MouseEventArgs) Handles Picture_Box_1.MouseUp, Picture_Box_2.MouseUp
        EndPan()
    End Sub

    Private Sub Pic_MouseMove(sender As Object, e As MouseEventArgs)
        ' Moving the mouse is how a slideshow with hidden chrome is asked to show it
        ' again for a moment - without stopping the slideshow, which any key would.
        RevealSlideshowChromeTemporarily()

        If e.Button = MouseButtons.Left Then
            If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2470: pic_MouseMove - no picture box visible")
                Exit Sub
            End If
            If panel_Media Is Nothing Then Exit Sub

            ' Add drag functionality when zoomed
            If zoom_Scale = 0 OrElse zoom_Scale > 1 Then
                If Not is_Dragging Then
                    ' Check if mouse has moved enough to be considered a drag
                    Dim drag_Threshold As Integer = 5 ' Increased threshold
                    Dim distance_Moved As Double = Math.Sqrt((e.X - mouse_Down_Start_Point.X) ^ 2 + (e.Y - mouse_Down_Start_Point.Y) ^ 2)

                    If distance_Moved >= drag_Threshold Then BeginPan()
                End If

                If is_Dragging Then
                    ' Check if enough time has passed since last update
                    Dim current_Time As DateTime = DateTime.Now
                    If (current_Time - last_Drag_Update_Time).TotalMilliseconds >= DRAG_UPDATE_INTERVAL_MS Then

                        ' Measured against panel_Media, which stands still. e.X/e.Y are
                        ' client coordinates OF THE VERY BOX BEING MOVED, so the frame of
                        ' reference travelled with the object: the applied offset followed
                        ' a(n) = D(n) - a(n-1), i.e. the picture crawled after the hand at
                        ' half speed, every second event moved it not at all (the stutter),
                        ' and the grab point slid out from under the cursor.
                        Dim cursor_On_Panel As Point = panel_Media.PointToClient(Cursor.Position)
                        Dim new_Left As Integer = cursor_On_Panel.X - drag_Grab_Offset.Width
                        Dim new_Top As Integer = cursor_On_Panel.Y - drag_Grab_Offset.Height

                        If new_Left <> Picture_Box_1.Left OrElse new_Top <> Picture_Box_1.Top Then
                            Picture_Box_1.Location = New Point(new_Left, new_Top)
                            Picture_Box_2.Location = Picture_Box_1.Location

                            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2476: pic_MouseMove - dragging to " & new_Left.ToString() & "," & new_Top.ToString())
                        End If

                        ' Update the last update time
                        last_Drag_Update_Time = current_Time

                        ' Set focus to the active picture box
                        If sender Is Picture_Box_1 Then
                            Picture_Box_1.Focus()
                        ElseIf sender Is Picture_Box_2 Then
                            Picture_Box_2.Focus()
                        End If
                    End If
                    ' If not enough time has passed, we simply ignore this mouse move event for dragging
                End If
            End If
        Else
            ' Safety net - MouseUp is the real end of the drag.
            EndPan()
        End If
    End Sub

End Class
