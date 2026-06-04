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
        If (Control.ModifierKeys And Keys.Shift) = Keys.Shift Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 10 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Shift+LeftClick - jumping +10 files")
                    SlideShowStop()
                    current_File_Index += 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+10 файлов", "+10 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +10", "Not enough files for +10 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 10 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Shift+RightClick - jumping -10 files")
                    SlideShowStop()
                    current_File_Index -= 10
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-10 файлов", "-10 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -10", "Current index too low for -10 jump")
                End If
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 100 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Ctrl+LeftClick - jumping +100 files")
                    SlideShowStop()
                    current_File_Index += 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+100 файлов", "+100 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +100", "Not enough files for +100 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 100 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Ctrl+RightClick - jumping -100 files")
                    SlideShowStop()
                    current_File_Index -= 100
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-100 файлов", "-100 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -100", "Current index too low for -100 jump")
                End If
                Return
            End If
        End If

        If (Control.ModifierKeys And Keys.Alt) = Keys.Alt Then
            pending_Single_Click_Timer.Stop()
            pending_Single_Click_Event = Nothing

            If e.Button = MouseButtons.Left Then
                If total_File_Count > current_File_Index + 1000 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1151: Alt+LeftClick - jumping +1000 files")
                    SlideShowStop()
                    current_File_Index += 1000
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "+1000 файлов", "+1000 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Недостаточно файлов для перехода на +1000", "Not enough files for +1000 jump")
                End If
                Return
            ElseIf e.Button = MouseButtons.Right Then
                If current_File_Index >= 1000 Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1153: Alt+RightClick - jumping -1000 files")
                    SlideShowStop()
                    current_File_Index -= 1000
                    ReadShowMediaFile("SetFile")
                    lbl_Status.Text = If(Is_Russian_Language, "-1000 файлов", "-1000 files")
                Else
                    lbl_Status.Text = If(Is_Russian_Language, "Текущий индекс слишком мал для перехода на -1000", "Current index too low for -1000 jump")
                End If
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

            zoom_Scale = If(zoom_Scale = 0, 1, zoom_Scale) * zoom_Scale_Factor
            lbl_Zoom.Text = "" & zoom_Scale.ToString("F2")
        Else
            Select Case e.Delta
                Case Is < 0
                    ReadShowMediaFile("ReadNextFile")
                Case Is > 0
                    ReadShowMediaFile("ReadPrevFile")
                Case 0
                    Select Case e.Button
                        Case MouseButtons.Left
                            ReadShowMediaFile("ReadNextFile") ' next
                        Case MouseButtons.Right
                            If Not is_WebBrowser_Visible Then
                                ReadShowMediaFile("ReadPrevFile")
                            End If
                        Case Windows.Forms.MouseButtons.Middle
                            RenameCurrentFile()
                        Case Windows.Forms.MouseButtons.XButton1
                            ReadShowMediaFile("ReadNextFile")
                        Case Windows.Forms.MouseButtons.XButton2
                            ReadShowMediaFile("ReadPrevFile")
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

    Private Sub Pic_MouseMove(sender As Object, e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then
            If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2470: pic_MouseMove - no picture box visible")
                Exit Sub
            End If

            ' Add drag functionality when zoomed
            If zoom_Scale = 0 OrElse zoom_Scale > 1 Then
                If Not is_Dragging Then
                    ' Check if mouse has moved enough to be considered a drag
                    Dim drag_Threshold As Integer = 5 ' Increased threshold
                    Dim distance_Moved As Double = Math.Sqrt((e.X - mouse_Down_Start_Point.X) ^ 2 + (e.Y - mouse_Down_Start_Point.Y) ^ 2)

                    If distance_Moved >= drag_Threshold Then
                        ' Start dragging - store the original PictureBox position
                        is_Dragging = True
                        original_PictureBox_Left = Picture_Box_1.Left
                        original_PictureBox_Top = Picture_Box_1.Top
                        drag_Start_Point = e.Location ' Use current mouse position as start point
                        last_Drag_Update_Time = DateTime.Now ' Initialize the timer
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2475: pic_MouseMove - drag started at " & drag_Start_Point.ToString())
                    End If
                End If

                If is_Dragging Then
                    ' Check if enough time has passed since last update
                    Dim current_Time As DateTime = DateTime.Now
                    If (current_Time - last_Drag_Update_Time).TotalMilliseconds >= DRAG_UPDATE_INTERVAL_MS Then

                        ' Calculate movement delta from the original mouse down position
                        Dim delta_X As Integer = e.X - mouse_Down_Start_Point.X
                        Dim delta_Y As Integer = e.Y - mouse_Down_Start_Point.Y

                        ' Only move if there's actual movement to avoid unnecessary updates
                        If delta_X <> 0 OrElse delta_Y <> 0 Then
                            ' Calculate new position based on original position plus total movement
                            Dim new_Left As Integer = original_PictureBox_Left + delta_X
                            Dim new_Top As Integer = original_PictureBox_Top + delta_Y

                            ' Apply the new position to both picture boxes
                            Picture_Box_1.Left = new_Left
                            Picture_Box_1.Top = new_Top
                            Picture_Box_2.Left = new_Left
                            Picture_Box_2.Top = new_Top

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

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2480: pic_MouseMove - mouse moved in " & sender.ToString())
        Else
            ' Reset dragging when mouse button is released
            If is_Dragging Then
                is_Dragging = False
                last_Drag_Update_Time = DateTime.MinValue ' Reset the timer
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2477: pic_MouseMove - drag ended")
            End If
        End If
    End Sub

End Class
