Option Strict On

Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms
Imports System.Diagnostics

Partial Public Class Main_Form

    ''' <summary>
    ''' The first-run help, one registered string per line. Lines rather than one big
    ''' block because a paragraph is far harder to translate consistently across 13
    ''' languages than a sentence, and because the mainline-only block has to be spliced
    ''' into the middle - see ModernHelpLines.
    ''' </summary>
    Private Shared ReadOnly Help_Lines_Before_Zoom As String() = {
        " Программа для быстрого переноса/копирования изображений по папкам. Да, та самая, которой вам не хватало.",
        "",
        "Сначала заполните таблицу каталогов-получателей по клавишам 1,2,3.. - 0. ",
        "Затем укажите каталог-источник для сортировки (тот самый, где творится беспорядок). ",
        "Продвигайтесь по файлам с помощью стрелок, P/N (PgDn/PgUp) или кликов/скролла мыши. ",
        "Стрелки вверх/вниз: -10/+10 и Shift+ PgDn/PgUp: +100/-100 файлов",
        "Y- случайно, S- слайдшоу, I- случайное слайдшоу. ",
        "R/Shift+R для поворота картинки, если фотограф держал камеру боком (T тоже, пока не включён OCR-перевод). "
    }

    Private Shared ReadOnly Help_Lines_After_Zoom As String() = {
        "F3 для просмотра панели изображений папки. ",
        "F6 для переименования файла. ",
        "Или раскидайте файлы по папкам клавишами (1,2,3.. - 0) - по одному нажатию на файл. ",
        "Или удалите текущий файл (del), если он того заслужил. ",
        "Окно таблицы можно закрепить и щёлкать мышью по колонке с цифрой. ",
        "(U) - вернуть последний перенесённый файл (на случай, если рука дрогнула). ",
        "",
        " Щёлкните на этот текст (F1), чтобы он деликатно исчез."
    }

    ''' <summary>
    ''' Re-caption the whole main window in the active UI language. Called on start and
    ''' whenever the language changes.
    ''' </summary>
    Public Sub LngCh()
        If lbl_Status.Text = "status" Then lbl_Status.Text = ""

        lbl_Folder.Text = Localization.T("Каталог:")
        btn_Prev_File.Text = Localization.T("<< пред(PgUp)")
        btn_Next_File.Text = Localization.T("след(PgDn) >>")
        bt_Delete.Text = Localization.T("удалить (del)")
        btn_Move_Table.Text = Localization.T("Настройки")

        Dim help As New StringBuilder()
        For Each line In Help_Lines_Before_Zoom
            help.AppendLine(Localization.T(line))
        Next
        help.Append(ModernHelpLines())
        For Each line In Help_Lines_After_Zoom
            help.AppendLine(Localization.T(line))
        Next
        lbl_Help_Info.Text = help.ToString()

        UpdateLanguageButtonCaption()
        ApplyScriptFont()
        ApplyRightToLeftText()

        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " n0030: UI language = " & Localization.CurrentCode)

#If Not NETFRAMEWORK Then
        LocalizeFolderMenu()
        LocalizeOpenUrlEntryPoint()
        LocalizeVideoTracks()
        LocalizeVideoControls()
#End If
        LocalizeToolbarOverflow()
        LocalizeBrowserTranslate()

        ' Button captions just changed width - re-flow the single-row toolbar.
        LayoutToolbar()
    End Sub

    ''' <summary>
    ''' The mainline-only lines of the first-run help, or "" where there is nothing to
    ''' promise. Zoom, the media menus, the video keys and the Shift+digit copy all ship
    ''' in the .NET 10 viewer only (the x86 one keeps the historical mechanics), so the
    ''' x86 help must not advertise keys that do nothing there. Ends with its own newline
    ''' so the surrounding text closes up when it is empty.
    ''' </summary>
    Private Function ModernHelpLines() As String
#If NETFRAMEWORK Then
        Return ""
#Else
        Dim sb As New StringBuilder()
        sb.AppendLine(Localization.T("Shift + цифра верхнего ряда - скопировать файл в ту же папку, куда цифра его переносит. "))
        sb.AppendLine(Localization.T("Масштаб: серый + и - (от курсора), серый / - вписать, серый * - 100 %; колесо можно переключить на масштаб в настройках. "))
        sb.AppendLine(Localization.T("Средняя кнопка мыши по картинке - меню со всеми действиями над ней (поворот, масштаб, переименовать, переместить, удалить). "))
        sb.AppendLine(Localization.T("У видео: клик по картинке - пауза/продолжить, правая кнопка - меню со всеми действиями над видео. "))
        sb.AppendLine(Localization.T("A - следующая звуковая дорожка, V - следующие субтитры (и кнопка ""Дорожки"", когда есть из чего выбрать). "))
        Return sb.ToString()
#End If
    End Function

    ''' <summary>
    ''' First-run UI language: follow the Windows display language. Kept as a thin
    ''' wrapper over Localization.DefaultCode for the call sites that only ever asked
    ''' the yes/no question.
    ''' </summary>
    Friend Shared Function SystemDefaultIsRussian() As Boolean
        Return Localization.DefaultCode() = "ru"
    End Function

    ' ------------------------------------------------------------------ picker ----

    ''' <summary>
    ''' The language button shows the ISO code of the active language in capitals -
    ''' always in Latin letters, even for Arabic and Urdu: an endonym does not fit a
    ''' 30-pixel toolbar button, and a two-letter Latin code is what every other app
    ''' puts there.
    ''' </summary>
    Friend Sub UpdateLanguageButtonCaption()
        btn_Language.Text = Localization.CurrentCode.ToUpperInvariant()
        toolTip.SetToolTip(btn_Language, Localization.T("Язык интерфейса"))
    End Sub

    ''' <summary>
    ''' Apply the script font where the pinned "Microsoft Sans Serif" cannot draw the
    ''' language: Devanagari and Bengali need Nirmala UI, Chinese needs Microsoft YaHei
    ''' UI, and Arabic/Urdu need Segoe UI (Microsoft Sans Serif has no Arabic at all).
    '''
    ''' The font is set on the individual text-bearing controls, never on the form: the
    ''' layout is DPI-unaware and pixel-tuned in the Designer, so swapping the form font
    ''' would move every control. The size stays at the Designer's 8.25pt.
    ''' </summary>
    Private Sub ApplyScriptFont()
        Dim family = Localization.FontFamily()
        Dim target As Font
        If family Is Nothing Then
            target = New Font("Microsoft Sans Serif", 8.25!, FontStyle.Regular, GraphicsUnit.Point)
        Else
            Try
                target = New Font(family, 8.25!, FontStyle.Regular, GraphicsUnit.Point)
                ' A missing family silently falls back to a substitute that may not
                ' cover the script either; better to notice than to draw boxes.
                If Not String.Equals(target.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase) Then
                    Debug.WriteLine("w0210: font '" & family & "' unavailable, got '" & target.FontFamily.Name & "'")
                End If
            Catch
                target = New Font("Microsoft Sans Serif", 8.25!, FontStyle.Regular, GraphicsUnit.Point)
            End Try
        End If

        For Each c As Control In LocalizedTextControls()
            If c IsNot Nothing Then c.Font = target
        Next
    End Sub

    ''' <summary>Controls whose caption is translated and therefore may need a script font.</summary>
    Private Function LocalizedTextControls() As Control()
        Return New Control() {
            lbl_Folder, lbl_Help_Info, lbl_Status, lbl_Current_File, lbl_File_Number,
            btn_Prev_File, btn_Next_File, bt_Delete, btn_Move_Table, btn_Rename,
            btn_Select_Folder, btn_choose_file, chkbox_Top_Most, cmbox_Sort, cmbox_Media_Folder}
    End Function

    ''' <summary>
    ''' Right-to-left TEXT for Arabic and Urdu - deliberately NOT RightToLeftLayout.
    ''' Mirroring the client area would move the media PictureBox, and Draw_Perspective,
    ''' the OCR overlay and PaintInfoOverlay all re-derive their geometry from that
    ''' rectangle (owner decision, SPECIFICATION_THIRTEEN_UI_LANGUAGES.md §2.6 г).
    ''' </summary>
    Private Sub ApplyRightToLeftText()
        Dim rtl = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        If Me.RightToLeft <> rtl Then Me.RightToLeft = rtl
        For Each c As Control In LocalizedTextControls()
            If c IsNot Nothing Then c.RightToLeft = rtl
        Next
    End Sub

    ''' <summary>
    ''' Switch the interface language: apply, persist, and re-caption both windows.
    ''' </summary>
    Friend Sub SetUiLanguage(code As String)
        If String.Equals(Localization.CurrentCode, code, StringComparison.OrdinalIgnoreCase) Then Return
        Localization.CurrentCode = code
        If Not MultiWindowPolicy.IsSecondaryInstance() Then Localization.SaveToSettings()
        LngCh()
        Table_Form.LngCh()
        RepaintMedia()
    End Sub

    ''' <summary>
    ''' The language button drops a list of every shipped language, each with its flag
    ''' and its own name. Two entries on the x86 build, thirteen on the mainline - the
    ''' menu is built from Localization.Codes, so neither number is written down here.
    ''' </summary>
    Private Sub ButtonLNG_Click(sender As Object, e As EventArgs) Handles btn_Language.Click
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2020: btn_Language")

        Dim menu As New ContextMenuStrip()
        ' A menu built per click has to be released per click, or every language switch
        ' leaks its strip plus 13 items. Disposing from inside Closed would pull the
        ' object out from under the notification, hence the BeginInvoke - the same
        ' pattern the recent-files menu already uses (Main_Form.vb, btn_RecentFiles).
        AddHandler menu.Closed, Sub(s, args)
                                    Dim m = DirectCast(s, ContextMenuStrip)
                                    Me.BeginInvoke(New Action(Sub() m.Dispose()))
                                End Sub
        menu.ShowImageMargin = True
        For i = 0 To Localization.Codes.Length - 1
            Dim code = Localization.Codes(i)
            Dim item As New ToolStripMenuItem(Localization.Names(i))
            item.Checked = (code = Localization.CurrentCode)
            item.Image = FlagImages.Get(code)
            item.Tag = code
            AddHandler item.Click, Sub(s2, e2) SetUiLanguage(CStr(DirectCast(s2, ToolStripMenuItem).Tag))
            menu.Items.Add(item)
        Next
        menu.Show(btn_Language, New Point(0, btn_Language.Height))
    End Sub

End Class
