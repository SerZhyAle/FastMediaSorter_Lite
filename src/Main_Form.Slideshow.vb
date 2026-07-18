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

    ''' <summary>Starts (or re-starts) the sequential slideshow. was_Running is what the
    ''' keyboard path has to pass: KeybUse calls SlideShowStop() before dispatching any
    ''' key, so by the time S got here Is_slide_show_mode was already False and the
    ''' documented "press again to halve the interval" never happened from the keyboard -
    ''' only from the toolbar button. (That is exactly what KeybUse's unused
    ''' was_Slide_Show_Mode parameter was added for.)</summary>
    Private Sub SetSlideShow(Optional was_Running As Boolean = False)

        is_Slide_Show_Random_Mode = False
        Dim slide_show_new_interval = Slideshow_Base_Interval_Ms
        If Is_slide_show_mode OrElse was_Running Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile(Mode_ForSlideShow)
    End Sub

    Private Sub SlideShow_Elapsed() Handles SlideShowTimer.Tick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1890: SlideShowTimer")
        ReadShowMediaFile(Mode_InSlideShow)
    End Sub

    Private Sub SlideShowStop()
        SlideShowTimer().Enabled = False
        Is_slide_show_mode = False
        ' Clear it HERE, the one place every stop goes through. It used to be cleared
        ' only by KeybUse and SetSlideShow, while the mouse (clicks, toolbar buttons)
        ' just called this - so after stopping a RANDOM slideshow with the mouse the
        ' flag stayed on, and with it "no next file to prefetch": every flip from then
        ' on decoded on the UI thread, silently, until some key was pressed.
        is_Slide_Show_Random_Mode = False
        lbl_Slideshow_Time.Visible = False
    End Sub

    Private Sub Button8_Click(sender As Object, e As EventArgs) Handles btn_Next_Random.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1990: btn_Next_Random")
        SlideShowStop()
        ReadShowMediaFile(Mode_ForRandom)
    End Sub

    Private Sub Button9_Click(sender As Object, e As EventArgs) Handles btn_Random_Slideshow.Click
        SetRandomSlideShow()
    End Sub

    Private Sub SlideShowStart()
        SlideShowTimer.Enabled = True
        Is_slide_show_mode = True
        lbl_Slideshow_Time.Visible = True
    End Sub

    Private Sub SetRandomSlideShow(Optional was_Running As Boolean = False)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2000: btn_Random_Slideshow")
        is_Slide_Show_Random_Mode = True
        Dim slide_show_new_interval = Slideshow_Base_Interval_Ms
        If Is_slide_show_mode OrElse was_Running Then
            slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
            If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
        End If
        SlideShowStart()
        SlideShowTimer.Interval = slide_show_new_interval

        ReadShowMediaFile(Mode_ForSlideShow)
    End Sub

End Class
