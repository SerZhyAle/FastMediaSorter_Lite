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

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles btn_Slideshow.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1880: btn_Slideshow")
        SetSlideShow()
    End Sub

    Private Sub SetSlideShow()

        is_Slide_Show_Random_Mode = False
        Dim slide_show_new_interval = biggest_slide_show_interval
        If Is_slide_show_mode Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile("ReadForSlideShow")
    End Sub

    Private Sub SlideShow_Elapsed() Handles SlideShowTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1890: SlideShowTimer")
        ReadShowMediaFile("InSlideShow")
    End Sub

    Private Sub SlideShowStop()
        SlideShowTimer().Enabled = False
        Is_slide_show_mode = False
        lbl_Slideshow_Time.Visible = False
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles btn_Next_Random.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1990: btn_Next_Random")
        SlideShowStop()
        ReadShowMediaFile("ReadForRandom")
    End Sub

    Private Sub Button9_Click(sender As Object, e As EventArgs) Handles btn_Random_Slideshow.Click
        SetRandomSlideShow()
    End Sub

    Private Sub SlideShowStart()
        SlideShowTimer.Enabled = True
        Is_slide_show_mode = True
        lbl_Slideshow_Time.Visible = True
    End Sub

    Private Sub SetRandomSlideShow()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2000: btn_Random_Slideshow")
        is_Slide_Show_Random_Mode = True
        Dim slide_show_new_interval = biggest_slide_show_interval
        If Is_slide_show_mode Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile("ReadForSlideShow")
    End Sub

End Class
