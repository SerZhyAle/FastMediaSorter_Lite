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

    Private Sub Form1_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1240: keyb: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

    Public Function GetWas_slideshow() As Boolean
        Return Is_slide_show_mode
    End Function

    Public Sub KeybUse(e As KeyEventArgs, was_Slide_Show_Mode As Boolean)
        SlideShowStop()
        is_Slide_Show_Random_Mode = False

        If cmbox_Media_Folder.Visible AndAlso Me.cmbox_Media_Folder.Focused Then
            If e.KeyCode = Keys.Enter AndAlso cmbox_Media_Folder.Text <> "" Then
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1300: Enter pressed")
                Current_Folder_Path = cmbox_Media_Folder.Text
                ReadShowMediaFile(Mode_FolderAndFile)
            Else
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1302: key skip - in editing")
            End If
            Exit Sub
        End If

#If Not NETFRAMEWORK Then
        ' Modern: NumPad +/-/*// zoom (spec 4.3). Checked before the maps below and
        ' independent of Shift, and it only claims those four grey keys - NumPad 0..9
        ' stay with sorting, the arrows stay navigation. Returns False for anything
        ' else, so every historical binding falls through untouched.
        If TryHandleZoomKey(e) Then
            e.Handled = True
            Exit Sub
        End If

        ' Modern: A / V cycle the audio / subtitle track (O-ISO-7). Both only fire
        ' while VLC is playing a file that HAS a choice - TryHandleTrackKey returns
        ' False otherwise, so neither letter is stolen from a future binding, and on
        ' an image they do nothing at all.
        If TryHandleTrackKey(e) Then
            e.Handled = True
            Exit Sub
        End If
#End If

        ' Set for every key this method claims; the suppression at the bottom depends on
        ' it (see there for why).
        Dim key_Handled As Boolean = True

        If e.Shift Then
            Select Case e.KeyCode
                Case Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1600: +100")
                    JumpBy(100, "+100 файлов")
                Case Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1610: -100")
                    JumpBy(-100, "-100 файлов")
                Case Keys.R
                    ' Counter-clockwise rotate (stable binding, independent of OCR).
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1615: Shift+R rotate CCW")
                    RotateActiveImage(False)
                Case Keys.T
                    ' Toggle OCR auto-mode (enables the feature if it was off).
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1616: Shift+T toggle auto OCR")
                    If ocr_Settings IsNot Nothing Then ToggleOcrAutoMode()
#If Not NETFRAMEWORK Then
                ' Shift + digit COPIES into the same slot the bare digit moves into: the
                ' one thing that used to require a trip to the settings window and back.
                ' Top row only, deliberately - with NumLock on, Windows drops NumLock
                ' while Shift is held, so Shift+NumPad1 arrives as Keys.End and
                ' Shift+NumPad0 as Keys.Insert. Promising numpad here would be a lie, so
                ' the help says "Shift + a top-row digit".
                Case Keys.D1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1401: copy 01")
                    ExecuteRecipientAction(1, RecipientActionKind.Copy)
                Case Keys.D2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1411: copy 02")
                    ExecuteRecipientAction(2, RecipientActionKind.Copy)
                Case Keys.D3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1421: copy 03")
                    ExecuteRecipientAction(3, RecipientActionKind.Copy)
                Case Keys.D4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1431: copy 04")
                    ExecuteRecipientAction(4, RecipientActionKind.Copy)
                Case Keys.D5
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1441: copy 05")
                    ExecuteRecipientAction(5, RecipientActionKind.Copy)
                Case Keys.D6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1451: copy 06")
                    ExecuteRecipientAction(6, RecipientActionKind.Copy)
                Case Keys.D7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1461: copy 07")
                    ExecuteRecipientAction(7, RecipientActionKind.Copy)
                Case Keys.D8
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1471: copy 08")
                    ExecuteRecipientAction(8, RecipientActionKind.Copy)
                Case Keys.D9
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1481: copy 09")
                    ExecuteRecipientAction(9, RecipientActionKind.Copy)
                Case Keys.D0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1491: copy 0")
                    ExecuteRecipientAction(10, RecipientActionKind.Copy)
#End If
                Case Else
                    key_Handled = False
            End Select
        Else
            Select Case e.KeyCode
                ' Keys.N is deliberately NOT here: it used to be, and since the first
                ' matching Case wins, its own branch below ("jump to file number") was
                ' unreachable. Space/Right/PageDown already cover "next file".
                Case Keys.Space, Keys.Right, Keys.BrowserForward, Keys.Next, Keys.PageDown
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1310: to next file")
                    ReadShowMediaFile(Mode_Next)
                Case Keys.P, Keys.Left, Keys.B, Keys.BrowserBack, Keys.Back, Keys.PageUp
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1320: to prev file")
                    ReadShowMediaFile(Mode_Prev)
                Case Keys.Y
                    ReadShowMediaFile(Mode_ForRandom)
                Case Keys.S
                    ' Pass what the slideshow state was BEFORE this method stopped it.
                    SetSlideShow(was_Slide_Show_Mode)
                Case Keys.F6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1340: to rename")
                    If Not String.IsNullOrEmpty(Current_File_Name) Then
                        RenameCurrentFile()
                    Else
                        lbl_Status.Text = Localization.T("! Нет файла для переименования")
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1350: No file to rename")
                    End If

                Case Keys.F7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1645: F7 - Toggle fullscreen")
                    ToggleFullScreenMode()

                Case Keys.F11
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1645: F11 - Toggle super fullscreen")
                    ToggleSuperFullScreenMode()

                Case Keys.I, Keys.F5
                    SetRandomSlideShow(was_Slide_Show_Mode)
                Case Keys.Home, Keys.H, Keys.BrowserHome
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1370: to first file")
                    JumpTo(0, "первый файл")
                Case Keys.End, Keys.E, Keys.L, Keys.BrowserStop
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1380: to last file")
                    JumpTo(total_File_Count - 1, "последний файл")
                Case Keys.F, Keys.F4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1385: choose file")
                    Choose_file()
                Case Keys.N
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1386: jump to number")
                    Jump_To_file_Number()
                Case Keys.D, Keys.Delete
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1390: to delete")
                    ReadShowMediaFile(Mode_Delete)
                Case Keys.D1, Keys.NumPad1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1400: 01")
                    PoMove(1)
                Case Keys.D2, Keys.NumPad2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1410: 02")
                    PoMove(2)
                Case Keys.D3, Keys.NumPad3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1420: 03")
                    PoMove(3)
                Case Keys.D4, Keys.NumPad4
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1430: 04")
                    PoMove(4)
                Case Keys.D5, Keys.NumPad5
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1440: 05")
                    PoMove(5)
                Case Keys.D6, Keys.NumPad6
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1450: 06")
                    PoMove(6)
                Case Keys.D7, Keys.NumPad7
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1460: 07")
                    PoMove(7)
                Case Keys.D8, Keys.NumPad8
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1470: 08")
                    PoMove(8)
                Case Keys.D9, Keys.NumPad9
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1480: 09")
                    PoMove(9)
                Case Keys.D0, Keys.NumPad0
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1490: 0")
                    PoMove(10)
                Case Keys.R
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1500: Rotate CW")
                    RotateActiveImage(True)
                Case Keys.T
                    ' When the OCR feature is on, T runs OCR/translate (or toggles
                    ' the overlay). When it is off, T keeps its legacy meaning of
                    ' counter-clockwise rotate (also always available on Shift+R).
                    If ocr_Settings IsNot Nothing AndAlso ocr_Settings.Enabled Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: T -> OCR/translate")
                        TriggerOcrHotkey()
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1540: T -> Rev Rotate (OCR off)")
                        RotateActiveImage(False)
                    End If
                Case Keys.Up
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1580: -10")
                    JumpBy(-10, "-10 файлов")
                Case Keys.Down
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1590: +10")
                    JumpBy(10, "+10 файлов")
                Case Keys.F1
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1620: F1 help")
                    lbl_Help_Info.Visible = True
                    lbl_Help_Info.BringToFront()
                Case Keys.F2
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F2")
                    ' Recreate if a previous modeless Show(Me) (toolbar / btn_Move_Table)
                    ' disposed the cached instance, and never ShowDialog an already-visible
                    ' one (it would throw) - just bring it to the front instead.
                    If Table_Form Is Nothing OrElse Table_Form.IsDisposed Then Table_Form = New Table_Form()
                    If Table_Form.Visible Then
                        Table_Form.Activate()
                    Else
                        Table_Form.PrepareForDisplay()
                        ' Modal is no protection: an owned window still has to be put
                        ' into the pinned band by hand (Main_Form.WindowPinning.vb).
                        PinToViewerBand(Table_Form)
                        Table_Form.ShowDialog(Me)
                    End If
                Case Keys.F3
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1630: F5")
                    ShowImagePanelForm()
                Case Keys.U
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1640: UnDo")
                    Undo()
                Case Keys.Escape, Keys.X, Keys.Q
                    If is_Full_Screen_Mode Then
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1641: ESC to normal")
                        is_Full_Screen_Mode = False
                        SetViewSizes()
                    Else
                        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1642: ESC to close")
                        Me.Close()
                    End If
                Case Else
                    key_Handled = False
            End Select
        End If

        ' KeyPreview only lets the form see the key FIRST - without this it travels on
        ' to the focused control as well. Toolbar buttons keep focus after a click, so
        ' Space used to flip the file here and then fire the button's Click: two actions
        ' from one press (with btn_Slideshow: stop, flip, start again).
        If key_Handled Then
            e.Handled = True
            e.SuppressKeyPress = True
        End If
    End Sub

    ''' <summary>F7 - the window fills the screen. Friend, and a method rather than two
    ''' lines repeated: the video's right-click menu offers the same thing, and the pair
    ''' must not drift apart.</summary>
    Friend Sub ToggleFullScreenMode()
        is_Full_Screen_Mode = Not is_Full_Screen_Mode
        SetViewSizes()
    End Sub

    ''' <summary>F11 - full screen with the panels gone too.</summary>
    Friend Sub ToggleSuperFullScreenMode()
        is_Full_Screen_Mode = Not is_Full_Screen_Mode
        is_Super_Full_Screen_Mode = Not is_Super_Full_Screen_Mode
        SetViewSizes()
    End Sub

    ''' <summary>Rotates the active picture 90°; clears any OCR overlay (the
    ''' pixel grid changed, so cached boxes no longer align).</summary>
    Private Sub RotateActiveImage(clockwise As Boolean)
        Dim rot As RotateFlipType = If(clockwise, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipNone)
        Try
            If is_Second_PictureBox_Active Then
                If is_PictureBox2_Visible AndAlso Picture_Box_2.Image IsNot Nothing Then
                    Picture_Box_2.Image.RotateFlip(rot)
                    Picture_Box_2.Invalidate()
                End If
            Else
                If is_PictureBox1_Visible AndAlso Picture_Box_1.Image IsNot Nothing Then
                    Picture_Box_1.Image.RotateFlip(rot)
                    Picture_Box_1.Invalidate()
                End If
            End If
            current_Overlay_Document = Nothing
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1505: rotated " & If(clockwise, "CW", "CCW"))
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1530: ERR: " & ex.Message)
            ReportOperationError("E012", ex)
        End Try
    End Sub

    ' Called only from the settings grid double-click, where row z is bound to slot
    ' z (row 0 = DEL, row 10 = key "0"). Dispatch by the same rule as the keyboard:
    ' PoMove(row) directly - the former PoMove(row+1) hit the wrong slot and row 10
    ' ran PoMove(11), past the array's upper bound.
    Public Sub DoKey(ByVal keyIndex As Integer)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1860: DoKey")

        If keyIndex = 0 Then
            ReadShowMediaFile(Mode_Delete)
        Else
            PoMove(keyIndex)
        End If
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' The same grid row, asked to COPY - Shift + double-click on the key number. Row 0
    ''' is DEL and keeps its single meaning: a deletion has no copy.
    ''' </summary>
    Public Sub DoKeyAsCopy(ByVal keyIndex As Integer)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1861: DoKeyAsCopy")

        If keyIndex = 0 Then
            ReadShowMediaFile(Mode_Delete)
        Else
            ExecuteRecipientAction(keyIndex, RecipientActionKind.Copy)
        End If
    End Sub
#End If

    Private Sub Picture_Box_1_KeyDown(sender As Object, e As KeyEventArgs) Handles Picture_Box_1.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1248: keyb on P1: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

    Private Sub Picture_Box_2_KeyDown(sender As Object, e As KeyEventArgs) Handles Picture_Box_2.KeyDown
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1249: keyb on P2: " & e.KeyCode.ToString)
        KeybUse(e, GetWas_slideshow())
    End Sub

End Class
