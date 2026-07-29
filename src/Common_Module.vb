Imports System.Runtime.InteropServices

Module Common_Module
    Public Hardkeys_to_move_mediafile(10) As String

    ''' <summary>
    ''' Derived from the UI language, not a setting of its own any more: the app ships
    ''' 13 interface languages and the chosen one lives in Localization.CurrentCode
    ''' (registry value "UiLanguage" - see SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.4).
    '''
    ''' It is kept - and kept public - deliberately. Call sites that still ask
    ''' "is this Russian?" keep compiling and keep behaving sensibly: a German user gets
    ''' the English branch, never the Russian one. That is what made migrating the
    ''' strings possible file by file instead of in one 900-string diff. New code should
    ''' call Localization.T() instead; LocalizationParityTests watches for regressions.
    ''' </summary>
    Public ReadOnly Property Is_Russian_Language As Boolean
        Get
            Return Localization.CurrentCode = "ru"
        End Get
    End Property

    Public Is_Copying_not_Moving As Boolean
    Public Second_App_Name As String = "FastMediaSorter"
    Public App_name As String = "SZA"

    ''' <summary>
    ''' The author's address shown in the app and used by every "write to the author"
    ''' action, including the log package on the About tab. One constant, so the three
    ''' call sites cannot drift apart.
    ''' </summary>
    Public Const Author_Email As String = "sza@ukr.net"
    Public Is_No_Background_Tasks As Boolean = False
    Public Is_Pespective As Boolean = True
    Public Form_Color_Scheme As Integer = 0 ' 0 - dynamic, 1 - black, 2 - white, 3 - most
    Public Choosen_Picture_From_Panel As String = ""
    Public Current_File_Name As String
    Public Current_Image_Path As String
    Public Is_no_request_before_file_operation As Boolean = False
    Public Is_Video_Loop As Boolean = False
    Public Picture_Box_Width_At_Panel As Integer = 80
    Public Picture_Box_Height_At_Panel As Integer = 80

    ' --- Viewer options (settings window "Просмотр" tab) ---
    ' Auto-rotate photos by their EXIF Orientation tag on load.
    Public Is_Exif_AutoRotate As Boolean = True
    ' High-quality (bicubic) downscaling in the media picture boxes.
    Public Is_HighQuality_Scaling As Boolean = True
    ' On-image overlay showing the file name + position (N/total).
    Public Is_Show_Info_Overlay As Boolean = False
    ' Sub-option of Is_Pespective: the bars fade into the background colour the further
    ' they get from the photo - a halo, not a flat bar. Off by default, so an upgrade
    ' keeps the bars it has always had. Modern build only - the x86 viewer hides the
    ' checkbox (see Main_Form.PerspectiveHalo.vb).
    Public Is_Dynamic_Perspective As Boolean = False
    ' Sub-option of Is_Dynamic_Perspective: grow the halo out from the photo edge on each
    ' new photo instead of just having it there. Purely HOW the halo arrives - the resting
    ' frame is identical either way. On by default: unlike the two flags above it cannot
    ' surprise anyone on upgrade, since its parent is off until deliberately switched on,
    ' and the growth is the point of the effect.
    Public Is_Animated_Perspective As Boolean = True
    ' Mouse-wheel action over an image: False (default) = flip through files, exactly
    ' as it has always worked; True = zoom in/out under the cursor (the "classic
    ' viewer" behaviour), opt-in only. Ctrl/Shift/Alt+wheel keep their meaning in both
    ' modes, and the wheel over video always flips. Modern build only - the x86 viewer
    ' keeps the historical mechanics (SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md 4.4).
    Public Zoom_Wheel_Zooms As Boolean = False
    ' Floating list of destination folders (recipients) shown top-left over the
    ' media, so the current file can be sorted by clicking. Off by default (fresh
    ' install / reinstall) - see SPECIFICATION_RECIPIENTS_OVERLAY_DOTNET48.md.
    Public Is_Show_Recipients_Overlay As Boolean = False
    ' Base slideshow interval (ms); repeated start halves it down to the limit.
    Public Slideshow_Base_Interval_Ms As Integer = 10000

    ' --- WinAPI Declarations ---
    <DllImport("user32.dll")>
    Public Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Public Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Public Function GetForegroundWindow() As IntPtr
    End Function

    Public Const SW_SHOWNOACTIVATE As Integer = 4
End Module