Option Strict On

Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms

Partial Public Class Main_Form

    Private Sub ISizeChanged()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1935: ISizeChanged")

        ' Set flag to prevent resize event from triggering debounce timer
        is_Programmatic_Resize = True

        Dim needs_Form_State_Change As Boolean = (is_Full_Screen_Mode <> is_Last_Full_Screen_State)

        If needs_Form_State_Change Then
            If is_Full_Screen_Mode Then
                FormBorderStyle = FormBorderStyle.None
                WindowState = FormWindowState.Maximized
            Else
                FormBorderStyle = FormBorderStyle.Sizable
                WindowState = FormWindowState.Normal
            End If
            is_Last_Full_Screen_State = is_Full_Screen_Mode

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1936: Form state changed, border/window updated")
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1937: Form state unchanged, skipping border/window update")
        End If

        If is_Full_Screen_Mode Then
            Buttons_to_fullscreen()
        Else
            Buttons_to_normal()
        End If

        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location
        If vlc_Video_View IsNot Nothing Then
            vlc_Video_View.Size = Picture_Box_1.Size
            vlc_Video_View.Location = Picture_Box_1.Location
        End If
        If Picture_Box_1.Left >= 0 Then
            lbl_Help_Info.Location = Picture_Box_1.Location
            lbl_Help_Info.Size = Picture_Box_1.Size
        Else
            lbl_Help_Info.Location = New Point(0, 0)
            lbl_Help_Info.Size = Me.Size
        End If

        If is_form_shown Then Draw_Perspective()
        SkipZoom()

        ' Reset flag after layout is complete
        is_Programmatic_Resize = False
    End Sub

    Private Sub SetViewSizes()
        ISizeChanged()
    End Sub

    Private Sub Form1_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        ' Don't start debounce timer if this is a programmatic resize
        If Not is_Programmatic_Resize Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1126: Form Resize (user-initiated)")
            ResizeDebounceTimer.Stop()
            ResizeDebounceTimer.Start()
        Else
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1127: Form Resize (programmatic - ignored)")
        End If
    End Sub

    Private Sub ResizeDebounceTimer_Tick(sender As Object, e As EventArgs) Handles ResizeDebounceTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1128: ResizeDebounceTimer_Tick")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub

    Private Sub SkipZoom()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1172: zoom reset")


        Dim top_first_row = lbl_Status.Top + lbl_Status.Height

        If is_Full_Screen_Mode Then
            Picture_Box_1.Top = 0
            Picture_Box_1.Left = 0
            Picture_Box_1.Width = Me.Width
            Picture_Box_1.Height = Me.Height

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location
        Else

            Picture_Box_1.Top = top_first_row
            Picture_Box_1.Left = left_first_column
            Picture_Box_1.Width = Me.Width
            Picture_Box_1.Height = Me.Height - top_first_row

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location
        End If

        zoom_Scale = 1.0F
        lbl_Zoom.Text = ""

    End Sub

    Private Sub Button7_Click(sender As Object, e As EventArgs) Handles btn_Full_Screen.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1900: btn_Full_Screen")
        is_Full_Screen_Mode = Not is_Full_Screen_Mode
        SetViewSizes()
    End Sub

    Private Sub Buttons_to_fullscreen()
        Me.Focus()

        If is_Super_Full_Screen_Mode Then
            Dim screen_Bounds As Rectangle = Screen.FromControl(Me).Bounds

            Picture_Box_1.Top = 0
            Picture_Box_1.Left = 0
            Picture_Box_1.Width = screen_Bounds.Width
            Picture_Box_1.Height = screen_Bounds.Height
        Else
            Dim pic_Top As Integer = If(is_Super_Full_Screen_Mode, 0, lbl_Status.Top + lbl_Status.Height)

            Picture_Box_1.Top = pic_Top
            Picture_Box_1.Left = 0
            Picture_Box_1.Width = Me.ClientSize.Width
            Picture_Box_1.Height = Me.ClientSize.Height - pic_Top
        End If

        Picture_Box_2.Size = Picture_Box_1.Size
        Picture_Box_2.Location = Picture_Box_1.Location

        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location
        If vlc_Video_View IsNot Nothing Then
            vlc_Video_View.Size = Picture_Box_1.Size
            vlc_Video_View.Location = Picture_Box_1.Location
        End If

        lbl_Folder.Visible = False
        btn_Move_Table.Visible = False

        cmbox_Sort.Visible = False

        If is_Super_Full_Screen_Mode Then
            chkbox_Top_Most.Visible = False
            btn_choose_file.Visible = False
            btn_Select_Folder.Visible = False
            btn_Review.Visible = False
            btn_Panel.Visible = False
            btn_Prev_File.Visible = False
            btn_Next_File.Visible = False
            btn_Next_Random.Visible = False
            btn_Random_Slideshow.Visible = False
            btn_Slideshow.Visible = False
            lbl_Zoom.Visible = False
            btn_Rename.Visible = False
            bt_Delete.Visible = False
            lbl_Current_File.Visible = False
            btn_Full_Screen.Visible = False
            btn_RecentFiles.Visible = False
            cmbox_Media_Folder.Visible = False
            lbl_File_Number.Visible = False
            lbl_Status.Visible = False
            lbl_Info.Visible = False
            lbl_Slideshow_Time.Visible = False

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1912: super fullscreen - all controls hidden, picture at 0,0")

        Else
            lbl_Status.Visible = True
            Dim the_Font_For_Fullscreen = New Font("Arial", 6, FontStyle.Regular)

            With chkbox_Top_Most
                .Top = top_first_line - 4
                .Left = left_first_column - 4
                .Width = the_Width_For_buttons * 2
                .Height = btn_Select_Folder.Height
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_choose_file
                .Top = top_first_line
                .Left = chkbox_Top_Most.Left + chkbox_Top_Most.Width + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Select_Folder
                .Top = top_first_line
                .Left = btn_choose_file.Left + btn_choose_file.Width + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Review
                .Top = top_first_line
                .Left = btn_Select_Folder.Left + btn_Select_Folder.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Panel
                .Top = top_first_line
                .Left = btn_Review.Left + btn_Review.Width + 20
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Prev_File
                .Top = top_first_line
                .Left = btn_Panel.Left + btn_Panel.Width + 20
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Next_File
                .Top = top_first_line
                .Left = btn_Prev_File.Left + btn_Prev_File.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Next_Random
                .Top = top_first_line
                .Left = btn_Next_File.Left + btn_Next_File.Width + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Random_Slideshow
                .Top = top_first_line
                .Left = btn_Next_Random.Left + btn_Next_Random.Width + 20
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Slideshow
                .Top = top_first_line
                .Left = btn_Random_Slideshow.Left + btn_Random_Slideshow.Width + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With lbl_Zoom
                .Top = top_first_line
                .Left = btn_Slideshow.Left + btn_Slideshow.Width + 2
                .Width = the_Width_For_buttons
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With btn_Rename
                .Top = lbl_Status.Top + the_Height_For_buttons + 4
                .Left = left_first_column
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
            With bt_Delete
                .Top = btn_Rename.Top + the_Height_For_buttons + 4
                .Left = 0
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_Font_For_Fullscreen
                .Visible = True
            End With
        End If

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1910: buttons resaized to full screeen")
    End Sub

    Private Sub Buttons_to_normal()
        Me.Focus()

        Dim top_offset_normal = lbl_Status.Top + lbl_Status.Height
        is_Super_Full_Screen_Mode = False

        lbl_Status.Visible = True
        lbl_Info.Visible = True
        lbl_Folder.Visible = True
        btn_Move_Table.Visible = True

        If Not is_Super_Full_Screen_Mode Then
            chkbox_Top_Most.Visible = True
            btn_choose_file.Visible = True
            btn_Select_Folder.Visible = True
            btn_Review.Visible = True
            btn_Panel.Visible = True
            btn_Prev_File.Visible = True
            btn_Next_File.Visible = True
            btn_Next_Random.Visible = True
            btn_Random_Slideshow.Visible = True
            btn_Slideshow.Visible = True
            lbl_Zoom.Visible = True
            btn_Rename.Visible = True
            bt_Delete.Visible = True

            lbl_Current_File.Visible = True
            btn_Full_Screen.Visible = True
            btn_RecentFiles.Visible = True
            cmbox_Media_Folder.Visible = True
            btn_Full_Screen.Visible = True
            lbl_File_Number.Visible = True

        End If

        If Not Picture_Box_1.Top = top_offset_normal OrElse
            Not Picture_Box_1.Width = Me.Width OrElse
            Not Picture_Box_1.Height = Me.Height - top_offset_normal Then

            Picture_Box_1.Top = top_offset_normal
            Picture_Box_1.Left = left_first_column
            Picture_Box_1.Width = Me.Width
            Picture_Box_1.Height = Me.Height - top_offset_normal

            Picture_Box_2.Size = Picture_Box_1.Size
            Picture_Box_2.Location = Picture_Box_1.Location

            Dim the_font_for_normal = New Font("Arial", 7, FontStyle.Regular)

            With chkbox_Top_Most
                .Top = top_first_line - 4
                .Left = left_first_column - 4
                .Width = the_Width_For_buttons * 2
                .Height = btn_Select_Folder.Height
                .Font = the_font_for_normal
                .Visible = True
            End With
            With cmbox_Sort
                .Top = top_first_line
                .Left = chkbox_Top_Most.Left + chkbox_Top_Most.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = cmbox_Media_Folder.Height
                .Font = the_font_for_normal
                .Visible = True
            End With
            With lbl_Folder
                .Top = top_first_line
                .Left = cmbox_Sort.Left + cmbox_Sort.Width + 2
                .Height = cmbox_Media_Folder.Height
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_choose_file
                .Top = top_first_line
                .Left = cmbox_Media_Folder.Width + cmbox_Media_Folder.Left + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Select_Folder
                .Top = top_first_line
                .Left = btn_choose_file.Width + btn_choose_file.Left + 2
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Review
                .Top = top_first_line
                .Left = btn_Select_Folder.Left + btn_Select_Folder.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Panel
                .Top = top_first_line
                .Left = btn_Review.Left + btn_Review.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Full_Screen
                .Top = top_first_line
                .Left = btn_Panel.Left + btn_Panel.Width + 10
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With lbl_Slideshow_Time
                .Top = top_first_line
                .Left = btn_Full_Screen.Left + btn_Full_Screen.Width + 10
                .Width = the_Width_For_buttons * 4
                .Height = btn_Select_Folder.Height
                .Font = the_font_for_normal
                .Visible = Is_slide_show_mode
            End With
            With btn_Language
                .Top = top_first_line
                .Left = lbl_Slideshow_Time.Left + lbl_Slideshow_Time.Width + 4
                .Width = the_Width_For_buttons * 2
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With lbl_Info
                .Top = top_first_line
                .Left = btn_Language.Left + btn_Language.Width + 4
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With lbl_Zoom
                .Top = top_first_line
                .Left = lbl_Info.Left + lbl_Info.Width + 2
                .Width = the_Width_For_buttons
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            'second line
            With lbl_File_Number
                .Top = cmbox_Sort.Top + cmbox_Sort.Height + 2
                .Left = left_first_column
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Prev_File
                .Top = lbl_File_Number.Top
                .Left = cmbox_Media_Folder.Left
                .Width = the_Width_For_buttons * 6
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Next_File
                .Top = btn_Prev_File.Top
                .Left = btn_Prev_File.Left + btn_Prev_File.Width + 2
                .Width = the_Width_For_buttons * 6
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Next_Random
                .Top = btn_Next_File.Top
                .Left = btn_Next_File.Left + btn_Next_File.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Random_Slideshow
                .Top = btn_Next_Random.Top
                .Left = btn_Next_Random.Left + btn_Next_Random.Width + 20
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Slideshow
                .Top = btn_Random_Slideshow.Top
                .Left = btn_Random_Slideshow.Left + btn_Random_Slideshow.Width + 2
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Move_Table
                .Top = btn_Slideshow.Top
                .Left = btn_Slideshow.Left + btn_Slideshow.Width + 20
                .Width = the_Width_For_buttons * 7
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With btn_Rename
                .Top = btn_Move_Table.Top
                .Left = btn_Move_Table.Left + btn_Move_Table.Width + 20
                .Width = the_Width_For_buttons * 3
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With
            With bt_Delete
                .Top = btn_Rename.Top
                .Left = btn_Rename.Left + btn_Rename.Width + 20
                .Width = the_Width_For_buttons * 4
                .Height = the_Height_For_buttons
                .Font = the_font_for_normal
                .Visible = True
            End With

            btn_RecentFiles.Left = left_first_column
            btn_RecentFiles.Top = btn_Prev_File.Top + btn_Prev_File.Height + 2
            btn_RecentFiles.Width = the_Width_For_buttons
            btn_RecentFiles.Height = the_Height_For_buttons

            lbl_Current_File.Left = btn_RecentFiles.Left + btn_RecentFiles.Width + 2
            lbl_Current_File.Top = btn_RecentFiles.Top

            lbl_Status.Left = left_first_column
            lbl_Status.Top = btn_RecentFiles.Top + btn_RecentFiles.Height + 2

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1920: buttons resized to normal screen")
        End If


        Web_Browser.Size = Picture_Box_1.Size
        Web_Browser.Location = Picture_Box_1.Location
        If vlc_Video_View IsNot Nothing Then
            vlc_Video_View.Size = Picture_Box_1.Size
            vlc_Video_View.Location = Picture_Box_1.Location
        End If
    End Sub

End Class
