'sza130806lite
'sza240823 eto pizdec
'sza250411 random, filters, etc
'sza250502 refactor
'sza250506 grok
'FastMediaSorter
'sza2505207
'sza250606 gemini
'sza250608 copilot
'sza250609 gif fix
'sza250617 
'sza250721 choose file
'sza250723 LITE
'sza250808 zoom_Scale 
'sza251009 is_Super_Full_Screen_Mode

Option Strict On

Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports Microsoft.Win32
Imports System.Diagnostics ' Add this line with other imports


<ComVisible(True)>
Public Class Main_Form

    ' Explicit constructor (same body the VB compiler used to synthesize) so the
    ' modern build can neutralize the IE WebBrowser right after InitializeComponent:
    ' an invisible control never gets a Win32 handle, so its ActiveX host is never
    ' instantiated - safe on IE-less systems (SPECIFICATION_DOTNET10_MODERN_BUILD §6.2).
    Public Sub New()
        InitializeComponent()
#If Not NETFRAMEWORK Then
        Web_Browser.Visible = False
#End If
    End Sub

    Private Const slide_show_limit As Integer = 30
    Private Const max_Namber_of_Recent_Folders As Integer = 100
    ' FROZEN name - one mutex for both exes (see CLAUDE.md). Friend so that
    ' Application_Events can create it at Startup, which is the only moment early
    ' enough to close the window in which two launches can both become instances.
    Friend Const app_Mutex_Name As String = "FastMediaSorterSingleInstanceMutex"
    ' Sent over WM_COPYDATA instead of a file path when a bare second launch (no
    ' arguments) finds this instance already running: "bring your window back".
    ' Colons make it impossible to collide with a real path.
    Friend Const Show_Window_Command As String = "::fms-show-window::"
    Private Const max_Number_Of_Files_For_List As Integer = 100000 'after - the array without sorting
    Private Const height_For_instruments_on_WebPanel As String = "45"
    Private Const percent_of_second_Color_Point = 20
    Private Const step_size_while_color_Search = 100
    Private Const SW_SHOWNOACTIVATE As Integer = 4
    Private Const SW_RESTORE As Integer = 9
    Private Const percent_of_color_deviation As Integer = 4
    Private Const percent_of_color_smooth_To_Remove = 7
    Private Const first_Color_X As Integer = 0
    Private Const first_Color_Y As Integer = 0
    Private Const second_Color_X As Integer = 5
    Private Const second_Color_Y As Integer = 5
    Private Const first_run_top = 50
    Private Const first_run_left = 50
    Private Const first_run_width = 800
    Private Const first_run_height = 600
    ' No Top/Left ceilings any more: a saved position is validated against
    ' SystemInformation.VirtualScreen, i.e. the desktop the user actually has.
    Private Const main_form_position_Limit_Width = 3000
    Private Const main_form_position_Limit_Width_Low = 320
    Private Const main_form_position_Limit_Height = 3000
    Private Const main_form_position_Limit_Height_Low = 240
    Private Const the_Height_For_buttons = 20
    Private Const the_Width_For_buttons = 15
    Private Const top_first_line = 0
    Private Const left_first_column = 0
    Private Const biggest_slide_show_interval = 10000
    ' At or above this slideshow interval the background drawing (perspective bars +
    ' dynamic-colour analysis) is allowed; below it the flips come too fast to be worth
    ' the GDI+ pixel work, so it is forced off. Owner rule: works at 5 s and slower.
    Private Const slideshow_limit_to_change_color = 5000
    Private Const how_long_wait_before_draw_perspective = 50
    Private Const max_Number_Of_Recent_Media_Files As Integer = 50

#If NETFRAMEWORK Then
    ' x86/net48: no AVIF/HEIC decoder exists on its target OSes (Win 7/8.1 WIC has
    ' no HEIF codec), so those stay in web_specific_image_extensions - scanned and
    ' sortable with an honest "unsupported" status, never claimed as displayable.
    Public Image_File_Extensions As String() = {".jpg", ".gif", ".jpeg", ".png", ".bmp", ".tiff", ".ico", ".wmf", ".emf", ".exif", ".webp"}
    Private web_specific_image_extensions As New HashSet(Of String) From {".heic", ".heif", ".avif", ".svg"}
#Else
    ' Modern: AVIF/HEIC/HEIF decode via Magick.NET behind the IImageDecoder seam
    ' (epic O-3), so they are first-class displayable formats here. Both builds
    ' scan the same file set - the x86 exe keeps them in the web-specific list.
    Public Image_File_Extensions As String() = {".jpg", ".gif", ".jpeg", ".png", ".bmp", ".tiff", ".ico", ".wmf", ".emf", ".exif", ".webp", ".avif", ".heic", ".heif"}
    Private web_specific_image_extensions As New HashSet(Of String) From {".svg"}
#End If
    Private video_File_Extensions As New HashSet(Of String) From {".webm", ".ogg", ".3g2", ".mkv", ".3gp", ".mp4", ".m4v", ".m4a", ".mov", ".mp3", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".flv", ".wav", ".wma"}


    Public Current_Folder_Path As String = ""
    Public Is_slide_show_mode As Boolean = False
    Public Is_to_show_picture_sizes As Boolean = False
    Public Is_to_show_file_sizes As Boolean = True
    Public Is_to_show_file_datetime As Boolean = True

    Private recent_Media_File_List As New List(Of String)
    Private Image_Panel_Form As Image_Panel_Form
    Private toolTip As ToolTip
    Private zoom_Scale As Single = 1.0F
    Private smoothIndex As Double = 0.0006

    ' Add these missing variable declarations near the top of the class:
    'Private Current_File_Name As String = ""
    'Private Current_Image_Path As String = ""
    'Private App_name As String = "FastMediaSorter"
    'Private Second_App_Name As String = "Settings"
    'Private Form_Color_Scheme As Integer = 1
    'Private Is_Russian_Language As Boolean = False
    'Private Is_No_Background_Tasks As Boolean = False
    'Private Is_Copying_not_Moving As Boolean = False
    'Private Is_no_request_before_file_operation As Boolean = False
    'Private Is_Pespective As Boolean = True
    'Private Picture_Box_Width_At_Panel As Integer = 80
    'Private Picture_Box_Height_At_Panel As Integer = 80
    'Private Hardkeys_to_move_mediafile(10) As String
    'Private Choosen_Picture_From_Panel As String = ""
    Private Table_Form As Table_Form

    ' Remove this line (line 84):
    ' Private pending_Single_Click_Timer As New System.Windows.Forms.Timer()

    Private pending_Single_Click_Event As MouseEventArgs = Nothing
    Private WithEvents pending_Single_Click_Timer As New System.Windows.Forms.Timer()
    Private is_Programmatic_Resize As Boolean = False

    ' Add this timer tick handler:
    Private Sub Pending_Single_Click_Timer_Tick(sender As Object, e As EventArgs) Handles pending_Single_Click_Timer.Tick
        pending_Single_Click_Timer.Stop()

        If pending_Single_Click_Event IsNot Nothing Then
            ' Execute the delayed single-click action
            Dim delayed_Event As MouseEventArgs = pending_Single_Click_Event
            pending_Single_Click_Event = Nothing

            ' Only call MouseUse if not in zoom mode or not left button
            If zoom_Scale = 1 OrElse delayed_Event.Button <> MouseButtons.Left Then
                MouseUse(delayed_Event)
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1149: Delayed single-click executed")
        End If
    End Sub

    Private is_form_shown As Boolean = False
    Private last_Perspective_Draw_Time As DateTime
    ''' <summary>Held for the process lifetime; created in MyApplication_Startup.</summary>
    Friend Shared Single_Instance_Mutex As Mutex
    Private app_Run_Count As Integer
    Private media_View_Count As Integer
    Private is_Combo_Set_Auto As Boolean = False

    Private bgWorker_Pending_Args As PrefetchRequest = Nothing
    Private bgWorker_Has_Pending_Operation As Boolean = False

    Private is_File_Reseived_From_Outside As Boolean = False
    Private is_First_Scroll_Event As Boolean = False

    Private is_Second_PictureBox_Active As Boolean = False
    Private is_this_First_Picture_File_We_Show As Boolean = True
    Private is_First_Picture_Box_Need_To_Be_Cached As Boolean = False
    Private current_File_List As ReadOnlyCollection(Of String)
    Private current_File_Index As Integer
    Private current_Second_File_Name As String
    Private total_File_Count As Integer

    Private next_File_After_Current As String
    Private current_Loaded_File_Name As String
    Private history_File_Name As String
    Private current_Image_Scale As String = ""
    Private last_Back_Color As System.Drawing.Color

    Private history_Operation_Target_Path As String
    Private is_Image_Mode As Boolean = True

    Private files_List As List(Of String) = Nothing
    Private files_Array As String() = Nothing
    Private is_Files_Array_Active As Boolean = False
    ''' <summary>The folder the list in memory was read from - the honest answer to "is
    ''' the list loaded?", which used to be guessed from current_File_Index = 0.</summary>
    Private folder_List_Loaded_For As String = ""

    Private is_Dragging As Boolean = False
    Private last_Drag_Update_Time As DateTime = DateTime.MinValue
    Private Const DRAG_UPDATE_INTERVAL_MS As Integer = 16
    ''' <summary>Where inside the box the pan grabbed it, in panel_Media coordinates.
    ''' Replaces the old "box position at drag start + delta" pair: the delta was
    ''' measured in the moving box's own client coordinates, which made the picture
    ''' follow the hand at half speed.</summary>
    Private drag_Grab_Offset As Size

    Private is_Table_Form_Open As Boolean
    Private last_Action_Time As DateTime
    Private is_Full_Screen_Mode As Boolean
    Private is_Super_Full_Screen_Mode As Boolean
    Private is_External_Input_Received As Boolean = False
    Private was_External_Input_Previously As Boolean
    Private WithEvents SlideShowTimer As New System.Windows.Forms.Timer()
    Private is_Slide_Show_Random_Mode As Boolean
    ' One generator for the whole random feature. VB's Rnd() without a Randomize() call
    ' starts from the same seed in every process: the "random" slideshow replayed the
    ' same files in the same order every session (and the first file came from a second,
    ' time-seeded generator - two different sources for one feature).
    Private ReadOnly slideshow_Rng As New Random()
    Private is_WebBrowser_Visible As Boolean
    Private is_PictureBox1_Visible As Boolean
    Private is_PictureBox2_Visible As Boolean
    Private last_Loaded_Uri As String = ""
    Private is_Folder_Read_Required As Boolean = False
    Private total_Files_Count_Text As String = "0"
    Private mouse_Down_Start_Point As Point

    Private video_Volume_Level As Double = 1
    Private is_Video_Muted As Boolean = False
    Private is_TextBox_Editing As Boolean = False

    ' LibVLC fallback player (for formats the IE WebBrowser cannot decode: ZMBV/AVI, VP9, etc.)
    Private libVlc As LibVLCSharp.Shared.LibVLC = Nothing
    Private vlc_Video_View As LibVLCSharp.WinForms.VideoView = Nothing
    Private vlc_Media_Player As LibVLCSharp.Shared.MediaPlayer = Nothing
    ''' <summary>The one initialisation, shared by every caller (see EnsureVlcInitializedAsync).</summary>
    Private vlc_Init_Task As System.Threading.Tasks.Task(Of Boolean) = Nothing
    Private is_Vlc_Playing As Boolean = False
#If Not NETFRAMEWORK Then
    ''' <summary>Set before Play for the optional "open paused" behaviour; consumed
    ''' on VLC's Playing callback, where pausing is safe.</summary>
    Private pause_New_Video_When_Ready As Boolean
#End If

    ''' <summary>Bumped for every new media shown. Work started asynchronously in one
    ''' generation must fold quietly if the generation has moved on by the time it
    ''' resumes - nothing cancels it, so it has to check.</summary>
    Private media_Generation As Integer = 0

    Dim history_Source_File_Name As String = ""
    Dim history_Destination_File_Name As String = ""
    ' What the recorded operation actually WAS. Undo used to branch on the current
    ' Is_Copying_not_Moving instead: flip the mode to "copy" after moving a file and U
    ' deleted it at the destination - the only copy left - instead of moving it back.
    Private history_Was_Copy As Boolean
    Private WithEvents BgWorker As New BackgroundWorker()
    Private is_BgWorker_Online As Boolean

    ''' <summary>Signalled when the worker's DoWork actually leaves.
    '''
    ''' Why not BackgroundWorker.IsBusy: it is cleared by a completion callback posted
    ''' to the UI thread - and FormClosing IS the UI thread, spinning in Thread.Sleep
    ''' with no message pump. The callback could never arrive, so both waits burned
    ''' their whole timeout (1 s + 5 s) every time and then freed VLC, the picture boxes
    ''' and the streams under a worker that was still running.</summary>
    Private ReadOnly bgworker_Done As New ManualResetEventSlim(True)
    Private ReadOnly fileop_Done As New ManualResetEventSlim(True)

    Private bgWorker_Result As String = "EMPTY"
    Private pictureBox1_Stream As IO.MemoryStream
    Private pictureBox2_Stream As IO.MemoryStream
    Private Const WmCopyData As Integer = &H4A

    Private all_Supported_Extensions As New HashSet(Of String)()
    Private recent_Folder_List As New List(Of String)

#If Not NETFRAMEWORK Then
    ''' <summary>Additive .NET 10 preferences from the expanded Settings contract.</summary>
    Private modern_Preferences As ModernViewerPreferences
#End If

    Private WithEvents FileOperationWorker As New BackgroundWorker
    ' The operation currently handed to FileOperationWorker. Written and read on the UI
    ' thread only (the worker gets its own copy through e.Argument), so its completion
    ' handler always knows what it was - even on the error path, where e.Result throws.
    Private current_File_Op As FileOp

    Private WithEvents ResizeDebounceTimer As New System.Windows.Forms.Timer()
    Private is_Last_Full_Screen_State As Boolean = False

    Private WithEvents gif_Restart_Timer As New System.Windows.Forms.Timer()
    Private gif_Total_Duration_Ms As Integer = 0
    Private gif_Restart_Image_Ref As Image = Nothing




    Private last_Media_Area_Click_Time As DateTime = DateTime.MinValue
    Private last_Media_Area_Click_Button As MouseButtons = MouseButtons.None
    Private ReadOnly DoubleClickTimeThreshold As Integer = SystemInformation.DoubleClickTime


    Private Const WM_COPYDATA As Integer = &H4A




    Private Const minimum_time_before_next_media_file As Double = 0.04





    ' Add this handler in Main_Form
    Private Sub Image_Panel_Form_FormClosed(sender As Object, e As FormClosedEventArgs)
        If Not String.IsNullOrEmpty(Choosen_Picture_From_Panel) Then
            External_message(Choosen_Picture_From_Panel)
            Choosen_Picture_From_Panel = "" ' Optionally reset after use
        End If
    End Sub


    ' Removed with the long-run stability sweep: a WM_USER+1 receiver that mapped a shared
    ' section handed to it in WParam and then called CloseHandle on it. Nothing in the
    ' package has ever sent that message - cross-instance forwarding is WM_COPYDATA, in
    ' Application_Events.vb - so it was a live handle-closing path driven entirely by
    ' whatever else might post WM_USER+1 to our window.
    Protected Overrides Sub WndProc(ByRef m As Message)

        If m.Msg = WM_COPYDATA Then

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0888: WM_COPYDATA")
            Try
                Dim cds As COPYDATASTRUCT = CType(Marshal.PtrToStructure(m.LParam, GetType(COPYDATASTRUCT)), COPYDATASTRUCT)
                ' The sender (Application_Events) encodes the path as UTF-8. Decode it the
                ' same way: PtrToStringAnsi uses the system code page and silently mangles
                ' any non-ASCII filename, which then fails File.Exists ("file not found").
                Dim received_Data As String = ""
                If cds.cbData > 0 AndAlso cds.lpData <> IntPtr.Zero Then
                    Dim received_Bytes(cds.cbData - 1) As Byte
                    Marshal.Copy(cds.lpData, received_Bytes, 0, cds.cbData)
                    received_Data = Encoding.UTF8.GetString(received_Bytes)
                End If
                If String.IsNullOrEmpty(received_Data) Then
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0000: Error processing WM_COPYDATA - received data is null or empty")
                    Return
                End If

                External_message(received_Data)

            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0020: Error processing WM_COPYDATA - " & ex.Message)
            End Try
        End If

        MyBase.WndProc(m)
    End Sub








    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles btn_Select_Folder.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0305: btn_Select_Folder")
        SelectFolderViaDialog()
    End Sub

    ''' <summary>Asks for a folder and opens it. Shared by the toolbar button and the
    ''' folder box's right-click menu (Main_Form.FolderMenu.vb) - one behaviour, one
    ''' place.</summary>
    Friend Sub SelectFolderViaDialog()
        Using folder_Browser_Dialog As New FolderBrowserDialog()
            folder_Browser_Dialog.SelectedPath = Current_Folder_Path

            folder_Browser_Dialog.Description = Localization.T("Выберите папку с медиафайлами..")
#If Not NETFRAMEWORK Then
            ' The Vista-style dialog .NET uses shows Description only as the title.
            folder_Browser_Dialog.UseDescriptionForTitle = True
#End If

            If folder_Browser_Dialog.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                Current_Folder_Path = folder_Browser_Dialog.SelectedPath
                lbl_Status.Text = Localization.T("выбрана папка") & ": " & Current_Folder_Path
                Is_No_Background_Tasks = False
                ReadShowMediaFile(Mode_FolderAndFile)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0310: Folder read")
            End If
        End Using
    End Sub











    Private Sub Form1_DragEnter(sender As Object, e As DragEventArgs) Handles Me.DragEnter
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    Private Sub Form1_DragDrop(sender As Object, e As DragEventArgs) Handles Me.DragDrop
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim files() As String = CType(e.Data.GetData(DataFormats.FileDrop), String())
            For Each file As String In files
                ProcessArgument(file)
                Exit For
            Next
        End If
    End Sub

    Private Sub btn_RecentFiles_Click(sender As Object, e As EventArgs) Handles btn_RecentFiles.Click
        If recent_Media_File_List Is Nothing OrElse
            recent_Media_File_List.Count = 0 Then

            '   MessageBox.Show(Localization.T("Нет недавних файлов."), "Recent Files", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim menu As New ContextMenuStrip()
        For Each file In recent_Media_File_List.AsEnumerable().Reverse()
            Dim item = menu.Items.Add(System.IO.Path.GetFileName(file))
            item.Tag = file
        Next

        AddHandler menu.ItemClicked, Sub(s, args)
                                         Dim selectedFile = TryCast(args.ClickedItem.Tag, String)
                                         If Not String.IsNullOrEmpty(selectedFile) Then
                                             ProcessArgument(selectedFile)
                                         End If
                                     End Sub

        AddHandler menu.Closed, Sub(s, args)
                                    Dim m = DirectCast(s, ContextMenuStrip)
                                    Me.BeginInvoke(New Action(Sub() m.Dispose()))
                                End Sub

        menu.Show(btn_RecentFiles, New Point(0, btn_RecentFiles.Height))
    End Sub


    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles btn_Prev_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1130: btn_Prev_File")
        SlideShowStop()
        ReadShowMediaFile(Mode_Prev)
    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles btn_Next_File.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1140: btn_Next_File")
        SlideShowStop()
        ReadShowMediaFile(Mode_Next)
    End Sub











    Private Sub FirstRun_Click(sender As Object, e As EventArgs) Handles lbl_Help_Info.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1820: lbl_Help_Info clicked and hidden")
        lbl_Help_Info.Visible = False
        lbl_Help_Info.Hide()
    End Sub

    Private Sub lbl_Info_LinkClicked(sender As Object, e As System.Windows.Forms.LinkLabelLinkClickedEventArgs) Handles lbl_Info.LinkClicked
        ' Explicit UseShellExecute: mailto needs the shell; net48 defaulted to True,
        ' .NET defaults to False (would throw Win32Exception on the modern build).
        System.Diagnostics.Process.Start(New System.Diagnostics.ProcessStartInfo("mailto:" & Author_Email & "?subject=Fast Media Sorter for Windows:") With {.UseShellExecute = True})
    End Sub


    Private Sub ButI_Click(sender As Object, e As EventArgs) Handles btn_Review.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1850: btn_Review clicked")
        FolderSelected()
    End Sub

    Private Sub FolderSelected()
        Is_No_Background_Tasks = False
        If Current_Folder_Path <> "" Then
            ReadShowMediaFile(Mode_FolderAndFile)
        Else
            MsgBox(Localization.T("Сначала укажите каталог с медиафайлами.. Программа хороша, но не телепат."))
        End If
    End Sub


    Private Sub TextBox1_KeyPress(sender As Object, e As KeyPressEventArgs)
        If e.KeyChar = Convert.ToChar(Keys.Enter) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1870: Enter pressed in folder box")

            Current_Folder_Path = Me.cmbox_Media_Folder.Text
            FolderSelected()
        End If
    End Sub




    Private Sub Form1_ResizeEnd(sender As Object, e As EventArgs) Handles Me.ResizeEnd
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1930: Form_ResizeEnd")
        ResizeDebounceTimer.Stop()
        ISizeChanged()
    End Sub


    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles btn_Move_Table.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1960: btn_MoveTable")
        SlideShowStop()

        ' Check if Table_Form is disposed and recreate it if necessary
        If Table_Form Is Nothing OrElse Table_Form.IsDisposed Then
            Table_Form = New Table_Form()
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1961: Table_Form recreated")
        End If

        Table_Form.PrepareForDisplay()
        ' Show() on an already-visible form throws ("Form that is already visible
        ' cannot be displayed as a modal dialog box.") and, unhandled, kills the app -
        ' so a second click on "Настройки" while the window is open just re-surfaces it.
        If Table_Form.Visible Then
            If Table_Form.WindowState = FormWindowState.Minimized Then Table_Form.WindowState = FormWindowState.Normal
            Table_Form.Activate()
        Else
            Table_Form.Show(Me)
        End If
    End Sub

    ' lbl_Folder.MouseClick used to also run CopyFilePathToClipboard() here, which
    ' fired after Label1_Click and overwrote the clipboard with the FILE path - so a
    ' click on "Каталог:" copied the file, contradicting the "copy the folder path"
    ' tooltip. The file-path copy already lives on lbl_Current_File / lbl_Status; the
    ' folder label keeps only Label1_Click (folder path).

    Private Sub StatusL_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Status.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w1980: lbl_Status MouseClick")
        CopyFilePathToClipboard()
    End Sub





    Private Sub ChkTopMost_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Top_Most.CheckedChanged
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2010: chkTopMost_CheckedChanged")
        Me.TopMost = chkbox_Top_Most.Checked
    End Sub


    Private Sub Label3_MouseClick(sender As Object, e As MouseEventArgs) Handles lbl_Current_File.MouseClick
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2110: lbl_Current_File.MouseClick")
        CopyFilePathToClipboard()
    End Sub

    Private Sub TextBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Media_Folder.SelectedIndexChanged
        If Not is_TextBox_Editing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2130: cmbox_MediaFolder SelectedIndexChanged")

            If cmbox_Media_Folder.SelectedIndex >= 0 Then
                Is_No_Background_Tasks = False
                Current_Folder_Path = cmbox_Media_Folder.SelectedItem.ToString()

                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2140: currentFolderPath = " & Current_Folder_Path)
                ReadShowMediaFile(Mode_FolderAndFile)
            End If

            btn_Next_File.Focus()
        End If
    End Sub

    Private Sub SortComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_Sort.SelectedIndexChanged
        If Not is_TextBox_Editing Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2150: cmbox_Sort SelectedIndexChanged")

            If Not String.IsNullOrEmpty(Current_Folder_Path) Then
                ReadShowMediaFile(Mode_FolderAndFile)
            End If
        End If
    End Sub

    Private Sub Label1_Click(sender As Object, e As EventArgs) Handles lbl_Folder.Click
        If Not String.IsNullOrEmpty(cmbox_Media_Folder.Text) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2240: Folder sent to clipboard")
            CopyTextToClipboard(cmbox_Media_Folder.Text, lbl_Status, Localization.T("Имя папки скопировано в буфер"))
        End If
    End Sub

    Private Sub StatusL_Click(sender As Object, e As EventArgs) Handles lbl_Status.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2300: Visibility set: " & If(is_PictureBox1_Visible, "P1-YES ", "P1-NO ") & If(is_PictureBox2_Visible, "P2-YES ", "P2-NO ") & If(is_WebBrowser_Visible, "WB-YES ", "WB-NO "))
    End Sub













    ''' <summary>
    ''' Reports a caught operation failure without stopping the app.
    '''
    ''' Nine catch-alls in the navigation, file-operation and background-drawing paths used to
    ''' end in a MODAL MsgBox. In long use the causes of those catches repeat - a share that
    ''' stopped answering, a transient GDI+ error - and on a held-down navigation key the
    ''' dialogs arrived one per file, each one halting everything until it was dismissed. The
    ''' status line plus the log says the same thing and keeps the diagnostic code (E001..E105)
    ''' the user can quote, without taking the app hostage.
    ''' </summary>
    Private Sub ReportOperationError(diagnostic_Code As String, ex As Exception)
        Dim detail As String = diagnostic_Code & " " & If(ex Is Nothing, "", ex.Message)
        Try
            lbl_Status.Text = Localization.TF("Ошибка операции: {0}", detail)
        Catch
        End Try
        AppFileLogger.LogException("Operation failed (" & diagnostic_Code & ")", ex)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " " & detail)
    End Sub

    Private Sub Btn_Panel_Click(sender As Object, e As EventArgs) Handles btn_Panel.Click
        SlideShowStop()
        ShowImagePanelForm()
    End Sub

    Private Sub ShowImagePanelForm()
        If Image_Panel_Form Is Nothing OrElse Image_Panel_Form.IsDisposed Then
            Image_Panel_Form = New Image_Panel_Form()
            AddHandler Image_Panel_Form.FormClosed, AddressOf Image_Panel_Form_FormClosed
        End If
        Image_Panel_Form.PrepareForDisplay()
        Image_Panel_Form.ShowDialog(Me)
        ' A modally-closed form is NOT disposed, and this one is held in a field - so without
        ' this the last session's thumbnails (hundreds of MB on a big folder at a large card
        ' size) stayed resident behind a closed window for the rest of the viewer's run.
        ' PrepareForDisplay rebuilds everything from scratch, so there is nothing to keep.
        Image_Panel_Form.ReleaseAllCards()
    End Sub

    Private Sub Main_Form_Deactivate(sender As Object, e As EventArgs) Handles Me.Deactivate
    End Sub

    Private Sub btn_choose_file_Click(sender As Object, e As EventArgs) Handles btn_choose_file.Click
        Choose_file()
    End Sub

    Private Sub Choose_file()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2450: btn_choose_file clicked")
        SlideShowStop()
        Using openFileDialog As New OpenFileDialog()
            ' Build video extensions string for filter
            Dim videoExtensions As String = String.Join(";", video_File_Extensions.Select(Function(ext) "*" & ext))
            Dim imageExtensions As String = "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.heic;*.heif;*.avif;*.svg"

            openFileDialog.Filter = "All Supported Files|" & imageExtensions & ";" & videoExtensions &
                               "|Image Files|" & imageExtensions &
                               "|Video Files|" & videoExtensions &
                               "|JPEG Files|*.jpg;*.jpeg|PNG Files|*.png|GIF Files|*.gif|BMP Files|*.bmp|WebP Files|*.webp|HEIC Files|*.heic;*.heif|AVIF Files|*.avif|SVG Files|*.svg"
            openFileDialog.InitialDirectory = If(String.IsNullOrEmpty(Current_Folder_Path), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Current_Folder_Path)
            openFileDialog.Title = Localization.T("Выберите медиафайл")
            If openFileDialog.ShowDialog() = DialogResult.OK Then
                Dim selected_File_Path As String = openFileDialog.FileName
                Dim selected_Folder_Path As String = Path.GetDirectoryName(selected_File_Path)

                ' Set up the necessary state for external input processing
                Current_Folder_Path = selected_Folder_Path
                Current_Image_Path = selected_File_Path
                Current_File_Name = selected_File_Path

                ' Update the folder combo box
                is_TextBox_Editing = True
                cmbox_Media_Folder.Text = Current_Folder_Path
                is_TextBox_Editing = False

                ' Mark as external input to ensure proper processing
                is_External_Input_Received = True
                was_External_Input_Previously = True

                ' Reset file index and count for the new selection
                current_File_Index = 0
                total_File_Count = 1

                ReadShowMediaFile(Mode_FolderAndKnownFile)
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2455: File chosen - " & selected_File_Path)
            End If
        End Using
    End Sub


    Private Sub lbl_File_Number_Click(sender As Object, e As EventArgs) Handles lbl_File_Number.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2460: jump to file clicked")
        SlideShowStop()
        Jump_To_file_Number()
    End Sub


    Private Sub Jump_To_file_Number()

        Dim fileNumber As Integer
        Dim take_number As String

        take_number = InputBox(Localization.T("Введите номер файла:"), Localization.T("Перейти к файлу"), (current_File_Index + 1).ToString, 1, total_File_Count)

        ' Cancel / Esc gives back an empty string - that is someone changing their mind,
        ' not an error worth a modal box saying "Invalid file number".
        If String.IsNullOrEmpty(take_number) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2464: jump to file cancelled")
            Return
        End If

        If Integer.TryParse(take_number, fileNumber) Then

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2465: Jumping to file number " & fileNumber.ToString())

            If fileNumber > 0 AndAlso fileNumber <= total_File_Count Then
                ' Mode_JumpTo, not the "ReadForJumpToFile" that had no branch at all -
                ' it only ever worked because the index was moved here beforehand.
                JumpTo(fileNumber - 1)
            Else
                MessageBox.Show(Localization.T("Номер файла вне диапазона."), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If

        Else
            MessageBox.Show(Localization.T("Неверный номер файла."), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End If
    End Sub








End Class
