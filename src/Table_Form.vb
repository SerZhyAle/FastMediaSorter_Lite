Option Strict On
'sza130806
'sza250617
Imports System.ComponentModel
Imports System.Diagnostics.Eventing.Reader

Public Class Table_Form
    Private set_This_Form_Top_Most As Boolean = False
    Private toolTip As ToolTip

    Private Sub btn_Set_As_Default_Video_Click(sender As Object, e As EventArgs) Handles btn_Set_As_Default_Video.Click
        Main_Form.AssociateAllVideoFormatsWithThisApp()
    End Sub

    Private Sub btn_OcrTranslate_Click(sender As Object, e As EventArgs) Handles btn_OcrTranslate.Click
        Main_Form.ShowOcrTranslateSettings()
    End Sub

    Private Sub Form2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        SaveSetting(App_name, Second_App_Name, "SetOnTop", If(set_This_Form_Top_Most, "1", "0"))
        If toolTip IsNot Nothing Then
            toolTip.Dispose()
            toolTip = Nothing ' Устанавливаем переменную в Nothing после уничтожения
        End If
    End Sub

    Private Sub InitializeTooltips()
        If toolTip Is Nothing Then
            toolTip = New ToolTip()
            ' Optional: Customize tooltip appearance and behavior
            toolTip.AutoPopDelay = 7000 ' Linger time
            toolTip.InitialDelay = 700  ' Time before appearing
            toolTip.ReshowDelay = 500   ' Time before reappearing
            toolTip.ShowAlways = True   ' Show even if form is not active
        End If

        ' --- TabPage 1: Destination Folders ---
        toolTip.SetToolTip(Data_Grid_View, If(Is_Russian_Language,
        "Двойной клик по номеру клавиши для выполнения действия." & vbCrLf & "Двойной клик по пути к папке для её изменения.",
        "Double-click a key number to perform the action." & vbCrLf & "Double-click a folder path to change it."))
        toolTip.SetToolTip(chkbox_Copy_Mode, If(Is_Russian_Language, "Если отмечено, файлы будут копироваться, а не перемещаться.", "If checked, files will be copied instead of moved."))
        toolTip.SetToolTip(chkbox_Independent_Thread_For_File_Operation, If(Is_Russian_Language, "Если отмечено, файловые операции будут выполняться в фоновом режиме.", "If checked, file operations will run in the background."))

        ' --- TabPage 2: Settings ---
        toolTip.SetToolTip(cmbox_color_schema, If(Is_Russian_Language, "Выберите цветовую схему фона для просмотра изображений.", "Select the background color scheme for the image viewer."))
        toolTip.SetToolTip(chb_perspectiva, If(Is_Russian_Language, "Включить эффект фоновой перспективы для изображений.", "Enable the perspective background effect for images."))
        toolTip.SetToolTip(chkb_show_pic_size, If(Is_Russian_Language, "Показывать размеры изображения (ширина x высота).", "Show the dimensions (width x height) of the image."))
        toolTip.SetToolTip(chkb_is_to_show_file_datetime, If(Is_Russian_Language, "Показывать дату и время последнего изменения файла.", "Show the last modified date and time of the file."))
        toolTip.SetToolTip(chkb_show_file_size, If(Is_Russian_Language, "Показывать размер файла.", "Show the size of the file."))
        toolTip.SetToolTip(chkb_video_loop, If(Is_Russian_Language, "Демонстрировать видео зациклено.", "Loop video playback."))
        toolTip.SetToolTip(chkb_no_request_before_file_operation, If(Is_Russian_Language, "Если отмечено, приложение не будет запрашивать подтверждение перед операциями с файлами.", "If checked, the application will not ask for confirmation before file operations."))
        toolTip.SetToolTip(cmb_Picture_Size, If(Is_Russian_Language, "Выберите размер карточки для формы панели изображений", "Choose the size of the card for the image panel"))

        toolTip.SetToolTip(chk_Exif_AutoRotate, If(Is_Russian_Language, "Автоматически поворачивать фото по тегу EXIF Orientation (снимки с телефонов/камер).", "Auto-rotate photos by their EXIF Orientation tag (photos from phones/cameras)."))
        toolTip.SetToolTip(chk_Hq_Scaling, If(Is_Russian_Language, "Качественное (бикубическое) масштабирование - резче при уменьшении крупных изображений.", "High-quality (bicubic) scaling - sharper when downscaling large images."))
        toolTip.SetToolTip(chk_Show_Info_Overlay, If(Is_Russian_Language, "Показывать имя файла и позицию (N/всего) поверх изображения. Удобно в полноэкранном режиме.", "Show the file name and position (N/total) over the image. Useful in full-screen."))
        toolTip.SetToolTip(num_Slideshow_Interval, If(Is_Russian_Language, "Базовый интервал слайдшоу в секундах (повторный запуск ускоряет показ вдвое).", "Base slideshow interval in seconds (starting again halves it)."))
        toolTip.SetToolTip(chk_Video_Mute, If(Is_Russian_Language, "Запускать видео без звука по умолчанию.", "Start videos muted by default."))
        toolTip.SetToolTip(num_Video_Volume, If(Is_Russian_Language, "Громкость видео по умолчанию (0-100%).", "Default video volume (0-100%)."))
        toolTip.SetToolTip(SetOnTop, If(Is_Russian_Language, "Держать это окно поверх всех остальных окон.", "Keep this window always on top of other windows."))

        toolTip.SetToolTip(btn_Language, If(Is_Russian_Language, "Переключить язык интерфейса на английский", "Switch interface language to English"))

        If btn_Set_As_Default_Video IsNot Nothing Then
            toolTip.SetToolTip(btn_Set_As_Default_Video, If(Is_Russian_Language,
                "Сделать эту программу видеопроигрывателем по умолчанию для текущего пользователя.",
                "Make this application the default video player for the current user."))
        End If

        If btn_OcrTranslate IsNot Nothing Then
            toolTip.SetToolTip(btn_OcrTranslate, If(Is_Russian_Language,
                "Открыть параметры OCR и перевода (язык распознавания, язык перевода, переводчик)",
                "Open OCR & translation settings (recognition language, target language, translator)"))
        End If

    End Sub

    Private Sub Form2_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        PrepareForDisplay()
    End Sub
    Public Sub PrepareForDisplay()
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-2: the_Table_Form_Load")

        InitializeTooltips()

        ' Initialize DataGridView columns BEFORE adding rows
        If Data_Grid_View.Columns.Count = 0 Then
            Data_Grid_View.Columns.Clear()

            ' Add Key column
            Dim keyColumn As New DataGridViewTextBoxColumn()
            keyColumn.Name = "KeyColumn"
            keyColumn.HeaderText = "KEY"
            keyColumn.Width = 60
            keyColumn.ReadOnly = True
            Data_Grid_View.Columns.Add(keyColumn)

            ' Add Folder Path column
            Dim folderColumn As New DataGridViewTextBoxColumn()
            folderColumn.Name = "FolderColumn"
            folderColumn.HeaderText = "Destination Folder"
            folderColumn.Width = 300
            folderColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            Data_Grid_View.Columns.Add(folderColumn)

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n00-3: DataGridView columns initialized")
        End If

        cmbox_color_schema.Items.Clear()
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По углу", "By corner")) '0
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "Чёрный", "Black")) '1
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "Белый", "White")) '2
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По краю", "By side")) '3
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По верху", "By top")) '4
        cmbox_color_schema.Items.Add(If(Is_Russian_Language, "По низу", "By buttom")) '5
        cmbox_color_schema.SelectedIndex = Form_Color_Scheme

        cmb_Picture_Size.Items.Clear()
        cmb_Picture_Size.Items.Add("30x40")
        cmb_Picture_Size.Items.Add("50x50")
        cmb_Picture_Size.Items.Add("40x90")
        cmb_Picture_Size.Items.Add("90x40")
        cmb_Picture_Size.Items.Add("80x80")
        cmb_Picture_Size.Items.Add("100x100")
        cmb_Picture_Size.Items.Add("90x160")
        cmb_Picture_Size.Items.Add("160x90")
        cmb_Picture_Size.Items.Add("200x200")
        cmb_Picture_Size.Items.Add("340x200")

        If Picture_Box_Width_At_Panel = 30 AndAlso Picture_Box_Height_At_Panel = 40 Then
            cmb_Picture_Size.SelectedIndex = 0
        ElseIf Picture_Box_Width_At_Panel = 50 AndAlso Picture_Box_Height_At_Panel = 50 Then
            cmb_Picture_Size.SelectedIndex = 1
        ElseIf Picture_Box_Width_At_Panel = 40 AndAlso Picture_Box_Height_At_Panel = 90 Then
            cmb_Picture_Size.SelectedIndex = 2
        ElseIf Picture_Box_Width_At_Panel = 90 AndAlso Picture_Box_Height_At_Panel = 40 Then
            cmb_Picture_Size.SelectedIndex = 3
        ElseIf Picture_Box_Width_At_Panel = 80 AndAlso Picture_Box_Height_At_Panel = 80 Then
            cmb_Picture_Size.SelectedIndex = 4
        ElseIf Picture_Box_Width_At_Panel = 100 AndAlso Picture_Box_Height_At_Panel = 100 Then
            cmb_Picture_Size.SelectedIndex = 5
        ElseIf Picture_Box_Width_At_Panel = 90 AndAlso Picture_Box_Height_At_Panel = 160 Then
            cmb_Picture_Size.SelectedIndex = 6
        ElseIf Picture_Box_Width_At_Panel = 160 AndAlso Picture_Box_Height_At_Panel = 90 Then
            cmb_Picture_Size.SelectedIndex = 7
        ElseIf Picture_Box_Width_At_Panel = 200 AndAlso Picture_Box_Height_At_Panel = 200 Then
            cmb_Picture_Size.SelectedIndex = 8
        ElseIf Picture_Box_Width_At_Panel = 340 AndAlso Picture_Box_Height_At_Panel = 200 Then
            cmb_Picture_Size.SelectedIndex = 9
        Else
            Picture_Box_Width_At_Panel = 80
            Picture_Box_Height_At_Panel = 80
            cmb_Picture_Size.SelectedIndex = 4
        End If

        chkb_show_pic_size.Checked = Main_Form.Is_to_show_picture_sizes
        chkb_is_to_show_file_datetime.Checked = Main_Form.Is_to_show_file_datetime
        chkb_show_file_size.Checked = Main_Form.Is_to_show_file_sizes
        chkb_video_loop.Checked = Is_Video_Loop
        chkb_no_request_before_file_operation.Checked = Is_no_request_before_file_operation

        chk_Exif_AutoRotate.Checked = Is_Exif_AutoRotate
        chk_Hq_Scaling.Checked = Is_HighQuality_Scaling
        chk_Show_Info_Overlay.Checked = Is_Show_Info_Overlay

        Dim slideshow_Seconds As Integer = CInt(Slideshow_Base_Interval_Ms / 1000)
        If slideshow_Seconds < CInt(num_Slideshow_Interval.Minimum) Then slideshow_Seconds = CInt(num_Slideshow_Interval.Minimum)
        If slideshow_Seconds > CInt(num_Slideshow_Interval.Maximum) Then slideshow_Seconds = CInt(num_Slideshow_Interval.Maximum)
        num_Slideshow_Interval.Value = slideshow_Seconds

        ' Video audio defaults live in Main_Form (private fields); read via its
        ' accessors. Set mute first so the volume ValueChanged applies the pair.
        chk_Video_Mute.Checked = Main_Form.CurrentVideoMuted
        Dim video_Volume_Percent As Integer = Main_Form.CurrentVideoVolumePercent
        If video_Volume_Percent < CInt(num_Video_Volume.Minimum) Then video_Volume_Percent = CInt(num_Video_Volume.Minimum)
        If video_Volume_Percent > CInt(num_Video_Volume.Maximum) Then video_Volume_Percent = CInt(num_Video_Volume.Maximum)
        num_Video_Volume.Value = video_Volume_Percent

        chb_perspectiva.Text = If(Is_Russian_Language, "Перспектива", "Perspective")
        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")

        chkbox_Copy_Mode.Checked = Is_Copying_not_Moving
        Data_Grid_View.Rows.Clear()
        Data_Grid_View.Rows.Add()
        Data_Grid_View.Item(0, 0).Value = "DEL"
        Data_Grid_View.Item(0, 0).ReadOnly = True
        Data_Grid_View.Item(1, 0).Value = If(Is_Russian_Language, "Удаление файла", "Delete file")
        For z As Integer = 1 To 10
            Data_Grid_View.Rows.Add()
            Data_Grid_View.Item(0, z).Value = z.ToString()
            Data_Grid_View.Item(1, z).Value = If(Hardkeys_to_move_mediafile(z), "")
        Next

        Data_Grid_View.Item(0, 10).Value = "0"

        ' There are only 11 destination keys (DEL + 0..9), so shrink the grid to
        ' exactly those rows instead of stretching it down the whole tab (which
        ' left a large empty area below the last key).
        Dim grid_Height As Integer = Data_Grid_View.ColumnHeadersHeight + 3
        For Each grid_Row As DataGridViewRow In Data_Grid_View.Rows
            grid_Height += grid_Row.Height
        Next
        Data_Grid_View.Height = grid_Height
        lbl_Grid_Hint.Top = Data_Grid_View.Bottom + 10
        lbl_Grid_Hint.Text = If(Is_Russian_Language,
            "Двойной клик по номеру клавиши - выполнить действие. Двойной клик по пути - выбрать папку.",
            "Double-click a key number to run the action. Double-click a path to pick a folder.")

        If Is_Russian_Language Then
            Me.Text = "Настройки"
            Data_Grid_View.Columns(0).HeaderText = "клавиша"
            Data_Grid_View.Columns(1).HeaderText = "каталог-получатель"

            Tab_Page_1.Text = "Каталоги-получатели"
            Tab_Page_2.Text = "Просмотр"
            Tab_Page_3.Text = "Видео и качество"
            Tab_Page_4.Text = "Файлы и система"

            grp_Background.Text = "Фон"
            grp_OnScreen.Text = "Информация на экране"
            grp_Slideshow.Text = "Слайдшоу"
            grp_Panel.Text = "Панель миниатюр"
            grp_Quality.Text = "Качество изображения"
            grp_Video.Text = "Видео"
            grp_FileOps.Text = "Операции с файлами"
            grp_Integration.Text = "Ассоциации и интеграция"
            grp_Window.Text = "Окно"
            grp_Language.Text = "Язык"

            lbl_Color.Text = "Цвет фона:"
            chkb_show_pic_size.Text = "Показывать размер изображений"
            chkb_show_file_size.Text = "Показывать размер файлов"
            chkb_is_to_show_file_datetime.Text = "Показывать дату и время файла"
            chk_Show_Info_Overlay.Text = "Имя файла и позиция поверх изображения"
            chk_Exif_AutoRotate.Text = "Авто-поворот по EXIF"
            chk_Hq_Scaling.Text = "Качественное масштабирование"
            chkb_video_loop.Text = "Демонстрировать видео зациклено"
            chk_Video_Mute.Text = "Без звука по умолчанию"
            lbl_Video_Volume.Text = "Громкость по умолчанию (%):"
            lbl_Slideshow_Interval.Text = "Интервал слайдшоу (с):"
            lbl_Picture_at_Panel_Size.Text = "Размер карточки панели:"

            chkbox_Copy_Mode.Text = "Режим копирования файлов (не перенос)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Использовать независимые потоки для операций с файлами"
            chkb_no_request_before_file_operation.Text = "Не запрашивать подтверждение перед операцией с файлом"

            btn_Set_As_Default.Text = "Зарегистрировать как программу просмотра изображений по умолчанию"
            btn_Set_As_Default_Video.Text = "Зарегистрировать как видеопроигрыватель по умолчанию"
            btn_OcrTranslate.Text = "OCR и перевод"
            SetOnTop.Text = "Держать это окно поверх остальных"
        Else
            Me.Text = "Settings"
            Data_Grid_View.Columns(0).HeaderText = "KEY"
            Data_Grid_View.Columns(1).HeaderText = "destination folder"

            Tab_Page_1.Text = "Destination folders"
            Tab_Page_2.Text = "Viewing"
            Tab_Page_3.Text = "Video and quality"
            Tab_Page_4.Text = "Files and system"

            grp_Background.Text = "Background"
            grp_OnScreen.Text = "On-screen info"
            grp_Slideshow.Text = "Slideshow"
            grp_Panel.Text = "Thumbnail panel"
            grp_Quality.Text = "Image quality"
            grp_Video.Text = "Video"
            grp_FileOps.Text = "File operations"
            grp_Integration.Text = "Associations and integration"
            grp_Window.Text = "Window"
            grp_Language.Text = "Language"

            lbl_Color.Text = "Background color:"
            chkb_show_pic_size.Text = "Show picture sizes"
            chkb_show_file_size.Text = "Show file sizes"
            chkb_is_to_show_file_datetime.Text = "Show file datetime"
            chk_Show_Info_Overlay.Text = "File name and position over the image"
            chk_Exif_AutoRotate.Text = "Auto-rotate by EXIF"
            chk_Hq_Scaling.Text = "High-quality scaling"
            chkb_video_loop.Text = "Loop video playback"
            chk_Video_Mute.Text = "Muted by default"
            lbl_Video_Volume.Text = "Default volume (%):"
            lbl_Slideshow_Interval.Text = "Slideshow interval (s):"
            lbl_Picture_at_Panel_Size.Text = "Panel card size:"

            chkbox_Copy_Mode.Text = "COPY mode (files are not moving)"
            chkbox_Independent_Thread_For_File_Operation.Text = "Use independent thread for operations with files"
            chkb_no_request_before_file_operation.Text = "No request before file operation"

            btn_Set_As_Default.Text = "Register as default image viewer"
            btn_Set_As_Default_Video.Text = "Register as default video player"
            btn_OcrTranslate.Text = "OCR & Translate"
            SetOnTop.Text = "Keep this window on top of others"
        End If

        Dim SetOnTopS As String = GetSetting(App_name, Second_App_Name, "SetOnTop", "1")
        set_This_Form_Top_Most = SetOnTopS = "1"
        SetOnTop.Checked = set_This_Form_Top_Most
        Me.TopMost = set_This_Form_Top_Most

        chb_perspectiva.Checked = Is_Pespective

        LinkLabel1.Text = Application.ProductVersion & " sza@ukr.net"
    End Sub

    Private Sub DataGridView1_CellMouseDoubleClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles Data_Grid_View.CellMouseDoubleClick
        If e.ColumnIndex = 0 Then
            Main_Form.DoKey(e.RowIndex)
        Else
            If e.RowIndex > 0 Then
                Dim folderBrowse As New FolderBrowserDialog()
                folderBrowse.SelectedPath = Hardkeys_to_move_mediafile(e.RowIndex)
                Dim textKey As String = e.RowIndex.ToString
                If textKey = "10" Then textKey = "0"
                folderBrowse.Description = If(Is_Russian_Language, "Укажите каталог переноса/копирования для клавиши " + textKey, "Select dest folder for key " + textKey)
                If folderBrowse.ShowDialog() = Windows.Forms.DialogResult.OK Then
                    Hardkeys_to_move_mediafile(e.RowIndex) = folderBrowse.SelectedPath
                    Data_Grid_View.Item(1, e.RowIndex).Value = Hardkeys_to_move_mediafile(e.RowIndex)
                    Data_Grid_View.Refresh()
                End If
            End If
        End If
    End Sub

    Private Sub DataGridView1_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellEndEdit
        If Data_Grid_View.Item(1, e.RowIndex).Value Is Nothing Then
            Hardkeys_to_move_mediafile(e.RowIndex) = ""
        Else
            Hardkeys_to_move_mediafile(e.RowIndex) = Data_Grid_View.Item(1, e.RowIndex).Value.ToString()
        End If
    End Sub

    Private Sub SetOnTop_CheckedChanged(sender As Object, e As EventArgs) Handles SetOnTop.CheckedChanged
        set_This_Form_Top_Most = SetOnTop.Checked
        Me.TopMost = set_This_Form_Top_Most
    End Sub

    Private Sub Chk_Exif_AutoRotate_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Exif_AutoRotate.CheckedChanged
        Is_Exif_AutoRotate = chk_Exif_AutoRotate.Checked
    End Sub

    Private Sub Chk_Hq_Scaling_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Hq_Scaling.CheckedChanged
        Is_HighQuality_Scaling = chk_Hq_Scaling.Checked
        Main_Form.RepaintMedia()
    End Sub

    Private Sub Chk_Show_Info_Overlay_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Show_Info_Overlay.CheckedChanged
        Is_Show_Info_Overlay = chk_Show_Info_Overlay.Checked
        Main_Form.RepaintMedia()
    End Sub

    Private Sub Num_Slideshow_Interval_ValueChanged(sender As Object, e As EventArgs) Handles num_Slideshow_Interval.ValueChanged
        Slideshow_Base_Interval_Ms = CInt(num_Slideshow_Interval.Value) * 1000
    End Sub

    Private Sub Num_Video_Volume_ValueChanged(sender As Object, e As EventArgs) Handles num_Video_Volume.ValueChanged
        Main_Form.SetVideoAudioState(CDbl(num_Video_Volume.Value) / 100.0, chk_Video_Mute.Checked)
    End Sub

    Private Sub Chk_Video_Mute_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Video_Mute.CheckedChanged
        Main_Form.SetVideoAudioState(CDbl(num_Video_Volume.Value) / 100.0, chk_Video_Mute.Checked)
    End Sub

    Private Sub Form2_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        ' Don't forward key presses if user is editing a DataGridView cell
        If Data_Grid_View.IsCurrentCellInEditMode Then
            Return
        End If

        ' Don't forward key presses if any text input control has focus
        If TypeOf Me.ActiveControl Is TextBox OrElse
       TypeOf Me.ActiveControl Is ComboBox Then
            Return
        End If

        ' Forward key presses to Main_Form only when not editing
        Main_Form.KeybUse(e, Main_Form.GetWas_slideshow())
    End Sub

    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles chkbox_Copy_Mode.CheckedChanged
        Is_Copying_not_Moving = chkbox_Copy_Mode.Checked
    End Sub

    Private Sub Data_Grid_View_CellContentClick(sender As Object, e As DataGridViewCellEventArgs) Handles Data_Grid_View.CellContentClick

    End Sub

    Private Sub Cmbox_color_schema_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbox_color_schema.SelectedIndexChanged
        Form_Color_Scheme = cmbox_color_schema.SelectedIndex
    End Sub

    Private Sub Chb_perspectiva_CheckedChanged(sender As Object, e As EventArgs) Handles chb_perspectiva.CheckedChanged
        Is_Pespective = chb_perspectiva.Checked
    End Sub

    Private Sub Chkb_show_pic_size_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_show_pic_size.CheckedChanged
        Main_Form.Is_to_show_picture_sizes = chkb_show_pic_size.Checked
    End Sub

    Private Sub Chkb_is_to_show_file_datetime_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_is_to_show_file_datetime.CheckedChanged
        Main_Form.Is_to_show_file_datetime = chkb_is_to_show_file_datetime.Checked
    End Sub

    Private Sub Chkb_show_file_size_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_show_file_size.CheckedChanged
        Main_Form.Is_to_show_file_sizes = chkb_show_file_size.Checked
    End Sub

    Private Sub Chkb_video_loop_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_video_loop.CheckedChanged
        Is_Video_Loop = chkb_video_loop.Checked
    End Sub

    Private Sub Chkb_no_request_before_file_operation_CheckedChanged(sender As Object, e As EventArgs) Handles chkb_no_request_before_file_operation.CheckedChanged
        Is_no_request_before_file_operation = chkb_no_request_before_file_operation.Checked
    End Sub

    Private Sub Table_Form_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If ToolTip IsNot Nothing Then ToolTip.Dispose()
    End Sub

    Private Sub Cmb_Picture_Size_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmb_Picture_Size.SelectedIndexChanged
        Picture_Box_Width_At_Panel = CInt(cmb_Picture_Size.SelectedItem.ToString().Split("x"c)(0))
        Picture_Box_Height_At_Panel = CInt(cmb_Picture_Size.SelectedItem.ToString().Split("x"c)(1))
    End Sub

    Private Sub btn_Language_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2021: btn_Language")

        Is_Russian_Language = Not Is_Russian_Language
        btn_Language.Text = If(Is_Russian_Language, "EN", "RU")
        LngCh()
        Main_Form.LngCh()

    End Sub

    Public Sub LngCh()
        ' Update the form and controls to reflect the new language
        PrepareForDisplay()
        InitializeTooltips()

    End Sub

    Private Sub Btn_Set_As_Default_Click(sender As Object, e As EventArgs) Handles btn_Set_As_Default.Click
        Main_Form.AssociateAllImageFormatsWithThisApp()
    End Sub

    Private Sub LinkLabel1_LinkClicked(sender As Object, e As System.Windows.Forms.LinkLabelLinkClickedEventArgs) Handles LinkLabel1.LinkClicked
        System.Diagnostics.Process.Start("mailto:sza@ukr.net?subject=FastMediaSorter for Win:")
    End Sub
End Class