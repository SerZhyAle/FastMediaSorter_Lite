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

#If Not NETFRAMEWORK Then
        is_Slide_Show_Random_Mode = modern_Preferences IsNot Nothing AndAlso modern_Preferences.SlideshowRandomOrder <> "natural"
#Else
        is_Slide_Show_Random_Mode = False
#End If
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
#If Not NETFRAMEWORK Then
        ' A known-length audio track owns the slide interval; streams (unknown length)
        ' retain the ordinary interval so a radio URL cannot hold the slideshow forever.
        If IsAudioNowPlaying() AndAlso IsVlcActuallyPlaying() AndAlso vlc_Media_Player IsNot Nothing AndAlso vlc_Media_Player.Length > 0 Then Return
#End If
        ReadShowMediaFile(Mode_InSlideShow)
    End Sub

    Private Sub SlideShowStop()
        SlideShowTimer().Enabled = False
        Is_slide_show_mode = False
#If Not NETFRAMEWORK Then
        ' Leaving the slideshow restores the interface at once (§5.2) - the reveal flag
        ' goes back up BEFORE the applier reads it.
        slideshow_Chrome_Revealed = True
        slideshow_Chrome_Timer.Stop()
        ApplySlideshowChrome()
#End If
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
#If Not NETFRAMEWORK Then
        slideshow_Chrome_Revealed = False
        slideshow_Chrome_Timer.Stop()
        ApplySlideshowChrome()
#End If
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

#If Not NETFRAMEWORK Then

    ''' <summary>
    ''' The interface a running slideshow hides (§5.2), and the temporary reveal that
    ''' brings it back.
    '''
    ''' Two flags rather than one: SlideshowUiMode says WHAT may be hidden, this one says
    ''' whether the user has just asked to see it anyway. Both are read by
    ''' SlideshowHidesToolbar / SlideshowHidesStatus, which the single chrome-visibility
    ''' point in ISizeChanged consults - so full-screen, super-full-screen and the
    ''' slideshow can never each write their own answer over the others'.
    ''' </summary>
    Private slideshow_Chrome_Revealed As Boolean = True

    ''' <summary>What the layout was last told. The applier is called from a mouse-move
    ''' handler, so it has to be free when nothing actually changed.</summary>
    Private slideshow_Chrome_Hidden_Now As Boolean

    Private WithEvents slideshow_Chrome_Timer As New System.Windows.Forms.Timer() With {.Interval = 2500}

    Private Sub ApplySlideshowChrome()
        Dim hidden As Boolean = SlideshowHidesToolbar() OrElse SlideshowHidesStatus()
        If hidden = slideshow_Chrome_Hidden_Now Then Return
        slideshow_Chrome_Hidden_Now = hidden
        ' Showing or hiding a docked panel changes the media area, so this goes through
        ' the ordinary layout pass - the picture is re-fitted and the perspective bars
        ' rebuilt for the size actually on screen.
        ISizeChanged()
    End Sub

    ''' <summary>Movement or a keystroke brings the hidden chrome back for a few seconds
    ''' without stopping the slideshow. A no-op when nothing is hidden.</summary>
    Friend Sub RevealSlideshowChromeTemporarily()
        If Not Is_slide_show_mode Then Return
        If modern_Preferences Is Nothing OrElse modern_Preferences.SlideshowUiMode = "none" Then Return

        slideshow_Chrome_Revealed = True
        slideshow_Chrome_Timer.Stop()
        slideshow_Chrome_Timer.Start()
        ApplySlideshowChrome()
    End Sub

    Private Sub Slideshow_Chrome_Timer_Tick(sender As Object, e As EventArgs) Handles slideshow_Chrome_Timer.Tick
        slideshow_Chrome_Timer.Stop()
        slideshow_Chrome_Revealed = False
        ApplySlideshowChrome()
    End Sub

#Else

    ''' <summary>Nothing is ever hidden on the frozen x86 target, so the reveal has
    ''' nothing to do - the call site stays seam-free.</summary>
    Friend Sub RevealSlideshowChromeTemporarily()
    End Sub

#End If

End Class
