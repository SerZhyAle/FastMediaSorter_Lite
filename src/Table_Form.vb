Option Strict On
'sza130806
'sza250617
Imports System.ComponentModel
Imports System.Diagnostics.Eventing.Reader

Public Class Table_Form
    Private toolTip As ToolTip

    ''' <summary>Scales a design-time (96-dpi) pixel value to the current display DPI.
    ''' The OCR tab is built entirely in code with literal pixel coordinates; unlike
    ''' the Designer controls (auto-scaled via AutoScaleMode.Font)
    ''' they keep their 96-dpi size, yet their point-based fonts render ~1.75x larger
    ''' at 168 DPI - so text overflowed its box. Wrapping the literals in LU() sizes the
    ''' boxes to match the fonts. Identity at 96 DPI, so it is a no-op on standard
    ''' displays.</summary>
    Friend Function LU(px As Integer) As Integer
        Return LogicalToDeviceUnits(px)
    End Function

    Private Sub btn_Set_As_Default_Video_Click(sender As Object, e As EventArgs) Handles btn_Set_As_Default_Video.Click
        Main_Form.AssociateAllVideoFormatsWithThisApp()
    End Sub

    Private Sub Form2_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Destination folders may have been edited on the grid - rebuild the
        ' recipients overlay so it reflects the current set (the show/hide flag is
        ' persisted by Main_Form, not here anymore).
        Main_Form.ApplyRecipientsOverlay()
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
        toolTip.SetToolTip(Data_Grid_View, Localization.T("Двойной клик по номеру клавиши - выполнить действие." & vbCrLf & "Двойной клик по пути к папке - сменить её (одинарный клик она вежливо проигнорирует)."))
        toolTip.SetToolTip(chkbox_Copy_Mode, Localization.T("Если отмечено, файлы копируются, а не перемещаются - оригиналы никуда не денутся, что бы вы ни нажали дальше."))
        toolTip.SetToolTip(chkbox_Independent_Thread_For_File_Operation, Localization.T("Если отмечено, файловые операции уходят в фоновый поток, чтобы не заставлять вас смотреть на них."))

        ' --- TabPage 2: Settings ---
        toolTip.SetToolTip(cmbox_color_schema, Localization.T("Выберите цветовую схему фона для просмотра изображений."))
        toolTip.SetToolTip(chb_perspectiva, Localization.T("Включить перспективный фон: чёрные поля по краям превращаются в продолжение картинки. Чисто для красоты."))
        toolTip.SetToolTip(chk_Dynamic_Perspective, Localization.T("Полосы гаснут к цвету фона по мере удаления от фото - получается ореол, а не сплошная полоса. Применяется со следующего фото."))
        toolTip.SetToolTip(chk_Animated_Perspective, Localization.T("Ореол не появляется сразу, а вырастает от края фото к краю экрана - примерно за треть секунды. Снимите галку, и он будет просто нарисован мгновенно. В покое кадр в обоих случаях одинаковый. Растёт только на новом фото: ресайз окна и зум перерисовывают фон сразу."))
        toolTip.SetToolTip(chkb_show_pic_size, Localization.T("Показывать размеры изображения (ширина x высота)."))
        toolTip.SetToolTip(chkb_is_to_show_file_datetime, Localization.T("Показывать дату и время последнего изменения файла."))
        toolTip.SetToolTip(chkb_show_file_size, Localization.T("Показывать размер файла."))
        toolTip.SetToolTip(chkb_video_loop, Localization.T("Крутить видео по кругу - пока вам не надоест или не кончится терпение."))
        toolTip.SetToolTip(chkb_no_request_before_file_operation, Localization.T("Отключить подтверждения перед файловыми операциями. Программа поверит, что вы знаете, что делаете - смелое допущение."))
        toolTip.SetToolTip(cmb_Picture_Size, Localization.T("Выберите размер карточки для формы панели изображений"))

        toolTip.SetToolTip(chk_Exif_AutoRotate, Localization.T("Автоматически поворачивать фото по тегу EXIF Orientation (снимки с телефонов/камер)."))
        toolTip.SetToolTip(chk_Hq_Scaling, Localization.T("Качественное (бикубическое) масштабирование - резче при уменьшении крупных изображений."))
        toolTip.SetToolTip(chk_Show_Info_Overlay, Localization.T("Показывать имя файла и позицию (N/всего) поверх изображения. Удобно в полноэкранном режиме, где статусбар стыдливо прячется."))
        toolTip.SetToolTip(chk_Wheel_Zooms, Localization.T("Как в привычных просмотрщиках: колесо приближает и отдаляет под курсором. Выключено - колесо листает файлы, как было всегда. В любом случае Ctrl+колесо приближает, Shift+колесо даёт 100 %, Alt+колесо вписывает, а над видео колесо всегда листает. С клавиатуры: серые +/- приближают от курсора, / вписывает, * даёт 100 %."))
        toolTip.SetToolTip(num_Slideshow_Interval, Localization.T("Базовый интервал слайдшоу в секундах. Запустите слайдшоу ещё раз - и оно ускорится вдвое, будто куда-то опаздывает."))
        toolTip.SetToolTip(chk_Video_Mute, Localization.T("Запускать видео без звука - для просмотра в приличном обществе."))
        toolTip.SetToolTip(num_Video_Volume, Localization.T("Громкость видео по умолчанию (0-100%)."))
        toolTip.SetToolTip(SetOnTop, Localization.T("Плавающий список каталогов-получателей в левом верхнем углу поверх изображения/видео. Клик по строке - перенести/скопировать текущий файл в эту папку (или удалить). По умолчанию выключено."))

        toolTip.SetToolTip(btn_Language, Localization.T("Язык интерфейса"))

        toolTip.SetToolTip(btn_Set_As_Default, Localization.T("Открыть параметры Windows для назначения программы просмотра изображений по умолчанию."))

        If btn_Set_As_Default_Video IsNot Nothing Then
            toolTip.SetToolTip(btn_Set_As_Default_Video, Localization.T("Сделать эту программу видеоплеером по умолчанию - для текущего пользователя, без танцев с правами администратора."))
        End If

#If Not NETFRAMEWORK Then
        If btn_Share_Manager IsNot Nothing Then
            toolTip.SetToolTip(btn_Share_Manager, Localization.T("Открыть отдельное приложение управления SFTP-сервером и опубликованными папками."))
        End If
#End If

        toolTip.SetToolTip(LinkLabel1, Localization.T("Версия программы и адрес автора."))

        InitializeOcrTooltips()

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
        cmbox_color_schema.Items.Add(Localization.T("По углу")) '0
        cmbox_color_schema.Items.Add(Localization.T("Чёрный")) '1
        cmbox_color_schema.Items.Add(Localization.T("Белый")) '2
        cmbox_color_schema.Items.Add(Localization.T("По краю")) '3
        cmbox_color_schema.Items.Add(Localization.T("По верху")) '4
        cmbox_color_schema.Items.Add(Localization.T("По низу")) '5
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
#If NETFRAMEWORK Then
        ' The classic zoom model ships in the .NET 10 viewer only (the x86 build keeps
        ' the historical mechanics), so do not offer a switch that does nothing here.
        chk_Wheel_Zooms.Visible = False
#Else
        chk_Wheel_Zooms.Checked = Zoom_Wheel_Zooms
#End If

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

        chb_perspectiva.Text = Localization.T("Перспектива")
        chk_Dynamic_Perspective.Text = Localization.T("Динамическая перспектива")
        chk_Animated_Perspective.Text = Localization.T("Анимированная")
        btn_Language.Text = Localization.CurrentCode.ToUpperInvariant()

        chkbox_Copy_Mode.Checked = Is_Copying_not_Moving
        Data_Grid_View.Rows.Clear()
        Data_Grid_View.Rows.Add()
        Data_Grid_View.Item(0, 0).Value = "DEL"
        Data_Grid_View.Item(0, 0).ReadOnly = True
        Data_Grid_View.Item(1, 0).Value = Localization.T("Удаление файла")
        Data_Grid_View.Item(1, 0).ReadOnly = True
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
        lbl_Grid_Hint.Text = Localization.T("Двойной клик по номеру клавиши - выполнить действие. Двойной клик по пути - выбрать папку.")

        ' One set of assignments for all 13 languages - the Russian text is the key.
            Me.Text = Localization.T("Настройки")
            Data_Grid_View.Columns(0).HeaderText = Localization.T("клавиша")
            Data_Grid_View.Columns(1).HeaderText = Localization.T("каталог-получатель")

            Tab_Page_1.Text = Localization.T("Каталоги-получатели")
            Tab_Page_2.Text = Localization.T("Просмотр")
            Tab_Page_3.Text = Localization.T("Видео и качество")
            Tab_Page_4.Text = Localization.T("Файлы и система")
            Tab_Page_5.Text = Localization.T("OCR и перевод")

            grp_Background.Text = Localization.T("Фон")
            grp_OnScreen.Text = Localization.T("Информация на экране")
            grp_Slideshow.Text = Localization.T("Слайдшоу")
            grp_Panel.Text = Localization.T("Панель миниатюр")
            grp_Quality.Text = Localization.T("Качество изображения")
            grp_Video.Text = Localization.T("Видео")
            grp_FileOps.Text = Localization.T("Операции с файлами")
            grp_Integration.Text = Localization.T("Ассоциации и интеграция")
            grp_Language.Text = Localization.T("Язык")

            lbl_Color.Text = Localization.T("Цвет фона:")
            chkb_show_pic_size.Text = Localization.T("Показывать размер изображений")
            chkb_show_file_size.Text = Localization.T("Показывать размер файлов")
            chkb_is_to_show_file_datetime.Text = Localization.T("Показывать дату и время файла")
            chk_Show_Info_Overlay.Text = Localization.T("Имя файла и позиция поверх изображения")
            chk_Wheel_Zooms.Text = Localization.T("Колесо мыши приближает (иначе листает)")
            chk_Exif_AutoRotate.Text = Localization.T("Авто-поворот по EXIF")
            chk_Hq_Scaling.Text = Localization.T("Качественное масштабирование")
            chkb_video_loop.Text = Localization.T("Демонстрировать видео зациклено")
            chk_Video_Mute.Text = Localization.T("Без звука по умолчанию")
            lbl_Video_Volume.Text = Localization.T("Громкость по умолчанию (%):")
            lbl_Slideshow_Interval.Text = Localization.T("Интервал слайдшоу (с):")
            lbl_Picture_at_Panel_Size.Text = Localization.T("Размер карточки панели:")

            chkbox_Copy_Mode.Text = Localization.T("Режим копирования файлов (не перенос)")
            chkbox_Independent_Thread_For_File_Operation.Text = Localization.T("Использовать независимые потоки для операций с файлами")
            chkb_no_request_before_file_operation.Text = Localization.T("Не запрашивать подтверждение перед операцией с файлом")

            btn_Set_As_Default.Text = Localization.T("Зарегистрировать как программу просмотра изображений по умолчанию")
            btn_Set_As_Default_Video.Text = Localization.T("Зарегистрировать как видеопроигрыватель по умолчанию")
            SetOnTop.Text = Localization.T("Показывать таблицу получателей поверх медиафайла")

        PrepareOcrTabForDisplay()
#If Not NETFRAMEWORK Then
        ' The x86 viewer has no Share button - the Companion it opens cannot run on
        ' the OSes that exe serves. The Integration group simply keeps its original
        ' height there, with nothing missing to see.
        BuildShareLauncherButtonIfNeeded()
        LocalizeShareLauncherButton()
#End If

        ' The checkbox now toggles the recipients overlay on the main window (see its
        ' rename above), not this window's TopMost. State lives in Main_Form.
        SetOnTop.Checked = Is_Show_Recipients_Overlay

        chb_perspectiva.Checked = Is_Pespective
#If NETFRAMEWORK Then
        ' The halo ships in the .NET 10 viewer only (the x86 fallback keeps the flat
        ' bars), so do not offer a switch that does nothing here - same as chk_Wheel_Zooms.
        chk_Dynamic_Perspective.Visible = False
        chk_Animated_Perspective.Visible = False
#Else
        chk_Dynamic_Perspective.Checked = Is_Dynamic_Perspective
        chk_Animated_Perspective.Checked = Is_Animated_Perspective
#End If
        ' Checked above only raises CheckedChanged when the value actually changed, so
        ' the greying cannot rely on that handler alone having run.
        SyncDynamicPerspectiveEnabled()

        LinkLabel1.Text = Application.ProductVersion & " sza@ukr.net"

#If Not NETFRAMEWORK Then
        ' Keep the old controls and bindings, but host them in the .NET 10 shell
        ' built by Table_Form.ModernLayout.vb.  Calling this after localization
        ' gives the navigation/header the same RU/EN wording as the page content.
        BuildModernSettingsLayout()
        LocalizeModernSettingsLayout()
#End If
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
                folderBrowse.Description = Localization.TF("Укажите каталог переноса/копирования для клавиши {0}", textKey)
#If Not NETFRAMEWORK Then
                ' The Vista-style dialog .NET uses shows Description only as the title.
                folderBrowse.UseDescriptionForTitle = True
#End If
                If folderBrowse.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
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
        Is_Show_Recipients_Overlay = SetOnTop.Checked
        Main_Form.ApplyRecipientsOverlay()
    End Sub

    Private Sub Chk_Exif_AutoRotate_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Exif_AutoRotate.CheckedChanged
        Is_Exif_AutoRotate = chk_Exif_AutoRotate.Checked
    End Sub

    Private Sub Chk_Hq_Scaling_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Hq_Scaling.CheckedChanged
        Is_HighQuality_Scaling = chk_Hq_Scaling.Checked
        Main_Form.RepaintMedia()
    End Sub

    Private Sub Chk_Wheel_Zooms_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Wheel_Zooms.CheckedChanged
#If Not NETFRAMEWORK Then
        Zoom_Wheel_Zooms = chk_Wheel_Zooms.Checked
#End If
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
        SyncDynamicPerspectiveEnabled()
    End Sub

    ''' <summary>Greys the chain down: with no perspective there are no bars to fade, and
    ''' with no halo there is nothing to animate. Better than letting either read as a
    ''' setting that is doing something.</summary>
    Private Sub SyncDynamicPerspectiveEnabled()
        chk_Dynamic_Perspective.Enabled = chb_perspectiva.Checked
        chk_Animated_Perspective.Enabled = chb_perspectiva.Checked AndAlso chk_Dynamic_Perspective.Checked
    End Sub

    Private Sub Chk_Dynamic_Perspective_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Dynamic_Perspective.CheckedChanged
#If Not NETFRAMEWORK Then
        Is_Dynamic_Perspective = chk_Dynamic_Perspective.Checked
#End If
        SyncDynamicPerspectiveEnabled()
    End Sub

    Private Sub Chk_Animated_Perspective_CheckedChanged(sender As Object, e As EventArgs) Handles chk_Animated_Perspective.CheckedChanged
#If Not NETFRAMEWORK Then
        Is_Animated_Perspective = chk_Animated_Perspective.Checked
#End If
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

    ''' <summary>
    ''' The settings-window language button drops the same 13-language list as the main
    ''' toolbar (two entries on the x86 build). Both surfaces write one setting, so they
    ''' cannot disagree; Main_Form.SetUiLanguage does the applying and the saving.
    ''' </summary>
    Private Sub btn_Language_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2021: btn_Language")

        Dim menu As New ContextMenuStrip()
        menu.ShowImageMargin = True
        For i = 0 To Localization.Codes.Length - 1
            Dim code = Localization.Codes(i)
            Dim item As New ToolStripMenuItem(Localization.Names(i))
            item.Checked = (code = Localization.CurrentCode)
            item.Image = FlagImages.Get(code)
            item.Tag = code
            AddHandler item.Click,
                Sub(s2, e2)
                    Main_Form.SetUiLanguage(CStr(DirectCast(s2, ToolStripMenuItem).Tag))
                    LngCh()
                End Sub
            menu.Items.Add(item)
        Next
        menu.Show(btn_Language, New Point(0, btn_Language.Height))
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
        ' Explicit UseShellExecute: mailto needs the shell; net48 defaulted to True,
        ' .NET defaults to False (would throw Win32Exception on the modern build).
        System.Diagnostics.Process.Start(New System.Diagnostics.ProcessStartInfo("mailto:sza@ukr.net?subject=Fast Media Sorter for Windows:") With {.UseShellExecute = True})
    End Sub
End Class
