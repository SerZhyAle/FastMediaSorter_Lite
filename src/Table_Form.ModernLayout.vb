#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Diagnostics
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.Win32

' The x64 .NET 10 Settings window keeps the existing controls and event handlers,
' but gives them a new shell: navigation on the left, a clear context header and
' roomy card-like sections.  The old tab headers are deliberately hidden rather
' than replaced, so the existing five TabPages remain the source of truth for
' keyboard navigation, OCR activation and all legacy bindings.
Partial Public Class Table_Form

    Private SettingsCanvas As Color
    Private SettingsSurface As Color
    Private SettingsSidebar As Color
    Private SettingsSidebarMuted As Color
    Private SettingsAccent As Color
    Private SettingsLink As Color
    Private SettingsText As Color
    Private SettingsMutedText As Color
    Private SettingsBorder As Color
    Private settingsDarkMode As Boolean

    Private Const DwmwaUseImmersiveDarkMode As Integer = 20

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer,
                                                   ByRef value As Integer, valueSize As Integer) As Integer
    End Function

    Private modernSettingsBuilt As Boolean
    Private modernSettingsShell As Panel
    Private modernSettingsSidebar As Panel
    Private modernSettingsHeader As Panel
    Private modernSettingsContent As Panel
    Private modernSettingsFooter As Panel
    Private modernSettingsTitle As Label
    Private modernSettingsSubtitle As Label
    Private modernSettingsFooterText As Label
    Private modernSettingsNavButtons As Button()
    Private modernSidebarSectionLabel As Label
    Private modernProductLabel As Label
    Private modernDataLayoutBuilt As Boolean
    Private modernTranslationPage As TabPage
    Private modernSftpPage As TabPage
    Private modernAboutPage As TabPage
    Private ReadOnly modernSettingsRows As New List(Of ModernSettingRow)()
    Private ReadOnly modernPageFlows As New List(Of FlowLayoutPanel)()

    Private NotInheritable Class ModernSettingRow
        Public Key As String
        Public Host As Control
        Public Title As Label
        Public Description As Label
        Public Editor As Control
        Public Compact As Boolean
    End Class

    ''' <summary>
    ''' Creates the .NET 10-only visual shell. Existing controls are reparented,
    ''' not recreated, so no setting behaviour or Handles-based event wiring is
    ''' duplicated here.
    ''' </summary>
    Private Sub BuildModernSettingsLayout()
        If modernSettingsBuilt Then Return
        modernSettingsBuilt = True

        LoadSystemSettingsPalette()

        SuspendLayout()

        ' Compact by default, resizeable when a long translation/status needs it.
        FormBorderStyle = FormBorderStyle.Sizable
        MaximizeBox = True
        MinimizeBox = False
        MinimumSize = New Size(760, 520)
        ClientSize = New Size(900, 600)
        BackColor = SettingsCanvas
        Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)

        modernSettingsShell = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = SettingsCanvas,
            .Padding = Padding.Empty
        }
        modernSettingsSidebar = New Panel With {
            .Dock = DockStyle.Left,
            .Width = 218,
            .BackColor = SettingsSidebar,
            .Padding = New Padding(16, 20, 16, 16)
        }
        modernSettingsHeader = New Panel With {
            .Dock = DockStyle.Top,
            .Height = 68,
            .BackColor = SettingsSurface,
            .Padding = New Padding(22, 10, 22, 8)
        }
        modernSettingsFooter = New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = 1,
            .BackColor = SettingsSurface,
            .Visible = False
        }
        modernSettingsContent = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = SettingsCanvas,
            .Padding = New Padding(8, 6, 8, 6)
        }

        modernProductLabel = New Label With {
            .AutoSize = True,
            .Text = "Fast Media Sorter",
            .ForeColor = SettingsText,
            .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
            .Location = New Point(18, 18)
        }
        modernSidebarSectionLabel = New Label With {
            .AutoSize = True,
            .ForeColor = SettingsSidebarMuted,
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Regular),
            .Location = New Point(19, 43)
        }
        modernSettingsSidebar.Controls.Add(modernProductLabel)
        modernSettingsSidebar.Controls.Add(modernSidebarSectionLabel)

        modernSettingsTitle = New Label With {
            .AutoSize = False,
            .ForeColor = SettingsText,
            .Font = New Font("Segoe UI Semibold", 13.0F, FontStyle.Regular),
            .Location = New Point(20, 3),
            .Height = 36,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        modernSettingsSubtitle = New Label With {
            .AutoSize = False,
            .ForeColor = SettingsMutedText,
            .Font = New Font("Segoe UI", 8.5F, FontStyle.Regular),
            .Location = New Point(20, 39),
            .Height = 22,
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        modernSettingsHeader.Controls.Add(modernSettingsTitle)
        modernSettingsHeader.Controls.Add(modernSettingsSubtitle)

        ' Version/contact remains available without consuming a whole footer row.
        ReparentModernSettingsControl(LinkLabel1, modernSettingsSidebar)
        LinkLabel1.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        LinkLabel1.AutoSize = True
        LinkLabel1.ForeColor = SettingsLink
        LinkLabel1.Location = New Point(18, modernSettingsSidebar.ClientSize.Height - LinkLabel1.Height - 16)
        AddHandler modernSettingsSidebar.Resize,
            Sub()
                LinkLabel1.Left = 18
                LinkLabel1.Top = Math.Max(58, modernSettingsSidebar.ClientSize.Height - LinkLabel1.Height - 16)
            End Sub
        AddHandler modernSettingsHeader.Resize,
            Sub()
                Dim width As Integer = Math.Max(100, modernSettingsHeader.ClientSize.Width - 48)
                modernSettingsTitle.Width = width
                modernSettingsSubtitle.Width = width
            End Sub

        BuildModernSettingsNavigation()
        BuildExtraModernSettingsPages()

        ' Preserve the real TabControl for accessibility and existing event code,
        ' but remove its dated strip of tab headers. Navigation buttons select the
        ' same TabPages and Ctrl+Tab still works as a standard WinForms control.
        ReparentModernSettingsControl(Tab_Control, modernSettingsContent)
        Tab_Control.Dock = DockStyle.Fill
        Tab_Control.Appearance = TabAppearance.FlatButtons
        Tab_Control.SizeMode = TabSizeMode.Fixed
        Tab_Control.ItemSize = New Size(0, 1)
        Tab_Control.Padding = New Point(0, 0)
        Tab_Control.Multiline = True
        Tab_Control.BackColor = SettingsCanvas

        For Each page As TabPage In New TabPage() {Tab_Page_1, Tab_Page_2, Tab_Page_3, Tab_Page_4, Tab_Page_5, modernTranslationPage, modernSftpPage, modernAboutPage}
            page.BackColor = SettingsCanvas
            page.UseVisualStyleBackColor = False
            page.Padding = New Padding(0)
            page.AutoScroll = True
        Next

        ReparentModernSettingsControl(modernSettingsContent, modernSettingsShell)
        ReparentModernSettingsControl(modernSettingsHeader, modernSettingsShell)
        ReparentModernSettingsControl(modernSettingsSidebar, modernSettingsShell)
        Controls.Add(modernSettingsShell)
        modernSettingsShell.BringToFront()

        AddHandler Tab_Control.SelectedIndexChanged, AddressOf ModernSettingsTabChanged
        AddHandler modernSettingsContent.Resize, AddressOf ModernSettingsContentResized
        AddHandler SystemEvents.UserPreferenceChanged, AddressOf ModernSystemPreferenceChanged
        AddHandler FormClosed, AddressOf ModernSettingsFormClosed
        BuildModernDataLayout()
        StyleModernSettingsControls()
        LayoutModernSettingsPages()
        ApplySystemSettingsPalette()
        ResumeLayout(True)
    End Sub

    Private Sub ReparentModernSettingsControl(control As Control, target As Control)
        If control Is Nothing Then Return
        If control.Parent IsNot Nothing Then control.Parent.Controls.Remove(control)
        target.Controls.Add(control)
    End Sub

    Private Sub BuildModernSettingsNavigation()
        Dim top As Integer = 70
        Dim navLabels As String() = {"Destination folders", "Viewing", "Video and quality", "Files and system",
                                     "OCR", "Translation", "Android / SFTP", "About"}
        modernSettingsNavButtons = New Button(navLabels.Length - 1) {}

        For index As Integer = 0 To navLabels.Length - 1
            Dim button As New Button With {
                .Tag = index,
                .Text = navLabels(index),
                .FlatStyle = FlatStyle.Flat,
                .UseVisualStyleBackColor = False,
                .BackColor = SettingsSidebar,
                .ForeColor = SettingsSidebarMuted,
                .Font = New Font("Segoe UI Semibold", 8.5F, FontStyle.Regular),
                .TextAlign = ContentAlignment.MiddleLeft,
                .Height = 38,
                .Width = modernSettingsSidebar.ClientSize.Width - 24,
                .Location = New Point(12, top),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Padding = New Padding(12, 0, 8, 0),
                .Cursor = Cursors.Hand,
                .TabStop = True
            }
            button.FlatAppearance.BorderSize = 0
            AddHandler button.Click, AddressOf ModernSettingsNavClicked
            modernSettingsSidebar.Controls.Add(button)
            modernSettingsNavButtons(index) = button
            top += 42
        Next
    End Sub

    Private Sub BuildExtraModernSettingsPages()
        modernTranslationPage = New TabPage With {.Name = "Tab_Page_Translation", .Text = "Translation"}
        modernSftpPage = New TabPage With {.Name = "Tab_Page_Sftp", .Text = "Android / SFTP"}
        modernAboutPage = New TabPage With {.Name = "Tab_Page_About", .Text = "About"}
        Tab_Control.TabPages.Add(modernTranslationPage)
        Tab_Control.TabPages.Add(modernSftpPage)
        Tab_Control.TabPages.Add(modernAboutPage)
    End Sub

    Private Sub ModernSettingsNavClicked(sender As Object, e As EventArgs)
        Dim button As Button = TryCast(sender, Button)
        If button Is Nothing Then Return
        Dim index As Integer = CInt(button.Tag)
        If index >= 0 AndAlso index < Tab_Control.TabCount Then Tab_Control.SelectedIndex = index
    End Sub

    Private Sub ModernSettingsTabChanged(sender As Object, e As EventArgs)
        If Tab_Control.SelectedTab Is modernTranslationPage Then OnEnterOcrTab()
        RefreshModernSettingsHeader()
    End Sub

    Private Sub ModernSettingsContentResized(sender As Object, e As EventArgs)
        LayoutModernSettingsPages()
    End Sub

    ''' <summary>Called by PrepareForDisplay after the existing localization code.</summary>
    Private Sub LocalizeModernSettingsLayout()
        If Not modernSettingsBuilt Then Return

        Dim ru As Boolean = Is_Russian_Language
        Dim navLabels As String() = {Localization.T("Получатели"),
             Localization.T("Просмотр"),
             Localization.T("Видео"),
             Localization.T("Файлы"),
             Localization.T("OCR"),
             Localization.TC("settings", "Перевод"),
             Localization.T("Android / SFTP"),
             Localization.T("О программе")}
        For index As Integer = 0 To modernSettingsNavButtons.Length - 1
            modernSettingsNavButtons(index).Text = navLabels(index)
        Next

        LinkLabel1.Text = ShortSettingsVersion()
        modernSidebarSectionLabel.Text = Localization.T("Настройки")
        LocalizeModernSettingRows()
        RefreshModernSettingsHeader()
    End Sub

    Private Sub RefreshModernSettingsHeader()
        If Not modernSettingsBuilt Then Return
        Dim ru As Boolean = Is_Russian_Language
        Dim index As Integer = Math.Max(0, Tab_Control.SelectedIndex)
        Dim titles As String() = {Localization.T("Каталоги-получатели"),
             Localization.T("Просмотр"),
             Localization.T("Видео и качество"),
             Localization.T("Файлы и система"),
             Localization.T("OCR"),
             Localization.TC("settings", "Перевод"),
             Localization.T("Android и SFTP"),
             Localization.T("О программе")}
        Dim subtitles As String() = {Localization.T("Назначьте папки для быстрого перемещения и копирования."),
             Localization.T("Настройте фон, информацию на экране и слайдшоу."),
             Localization.T("Качество изображения и привычное поведение видео."),
             Localization.T("Операции с файлами, интеграция и язык интерфейса."),
             Localization.T("Распознавание текста на изображениях и параметры OCR."),
             Localization.T("Сервис и параметры перевода распознанного текста."),
             Localization.T("SFTP-сервер и мобильное приложение Android."),
             Localization.T("Версия приложения, документация и ссылки проекта.")}

        If index >= titles.Length Then index = 0
        modernSettingsTitle.Text = titles(index)
        modernSettingsSubtitle.Text = subtitles(index)

        For buttonIndex As Integer = 0 To modernSettingsNavButtons.Length - 1
            Dim selected As Boolean = buttonIndex = index
            Dim button As Button = modernSettingsNavButtons(buttonIndex)
            button.BackColor = If(selected, SettingsAccent, SettingsSidebar)
            button.ForeColor = If(selected, If(GetSettingsBrightness(SettingsAccent) >= 155, Color.Black, Color.White), SettingsSidebarMuted)
            button.FlatAppearance.MouseOverBackColor = If(selected, BlendSettingsColor(SettingsAccent, Color.White, 0.12F), BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.18F))
            button.FlatAppearance.MouseDownBackColor = If(selected, BlendSettingsColor(SettingsAccent, Color.Black, 0.12F), BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.28F))
        Next
    End Sub

    Private Sub LocalizeModernSettingRows()
        Dim ru As Boolean = Is_Russian_Language
        For Each row As ModernSettingRow In modernSettingsRows
            row.Title.Text = ModernSettingTitle(row.Key, ru)
            If row.Description IsNot Nothing Then row.Description.Text = ModernSettingDescription(row.Key, ru)
            For Each check As CheckBox In FindModernControls(Of CheckBox)(row.Host)
                ConfigureModernCheckbox(check, row)
            Next
            Select Case row.Key
                Case "image_associations", "video_associations"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = Localization.T("Открыть параметры Windows")
                Case "sftp_manager"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = Localization.T("Управление SFTP")
                Case "support_send_logs"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = Localization.T("Собрать и отправить")
                Case "ocr_server"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = Localization.T("Установить")
                    If buttons.Count > 1 Then buttons(1).Text = Localization.T("Запустить")
                Case "ocr_model"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = Localization.T("Загрузить")
            End Select
            For Each link As LinkLabel In FindModernControls(Of LinkLabel)(row.Host)
                If link IsNot row.Title Then link.Text = Localization.T("Открыть")
            Next
            If row.Description IsNot Nothing AndAlso toolTip IsNot Nothing Then toolTip.SetToolTip(row.Description, row.Description.Text)
            SizeModernSettingRow(row)
        Next
        LayoutModernSettingsPages()
    End Sub

    Private Shared Function FindModernControls(Of T As Control)(parent As Control) As IEnumerable(Of T)
        Dim found As New List(Of T)()
        For Each child As Control In parent.Controls
            If TypeOf child Is T Then found.Add(DirectCast(child, T))
            If child.HasChildren Then found.AddRange(FindModernControls(Of T)(child))
        Next
        Return found
    End Function

    Private Shared Sub ConfigureModernCheckbox(check As CheckBox, row As ModernSettingRow)
        ' PrepareForDisplay localizes the legacy controls on every opening and can
        ' therefore restore their old long captions. In the modern row the caption
        ' is already rendered by row.Title; keeping it on the tiny editor produces
        ' the stray first letters seen to the right of the checkbox.
        check.Text = String.Empty
        check.AutoSize = False
        check.Size = New Size(28, 28)
        check.CheckAlign = ContentAlignment.MiddleCenter
        check.TextAlign = ContentAlignment.MiddleCenter
        check.Padding = Padding.Empty
        check.Margin = Padding.Empty
        check.RightToLeft = RightToLeft.No
        check.AccessibleName = row.Title.Text
        check.AccessibleDescription = If(row.Description Is Nothing, String.Empty, row.Description.Text)
    End Sub

    Private Function ModernSettingTitle(key As String, ru As Boolean) As String
        Select Case key
            Case "recipients_overlay" : Return Localization.T("Таблица получателей поверх изображения")
            Case "section_overlay_layout" : Return Localization.T("Вид таблицы получателей")
            Case "recipients_position" : Return Localization.T("Положение таблицы")
            Case "recipients_width" : Return Localization.T("Ширина таблицы")
            Case "recipients_font" : Return Localization.T("Размер текста")
            Case "recipients_opacity" : Return Localization.T("Непрозрачность")
            Case "recipients_rows" : Return Localization.T("Видимые строки")
            Case "section_background" : Return Localization.T("Фон изображения")
            Case "background_color" : Return Localization.T("Цвет фона")
            Case "perspective" : Return Localization.T("Перспективный фон")
            Case "dynamic_perspective" : Return Localization.T("Динамический ореол")
            Case "animated_perspective" : Return Localization.T("Анимация ореола")
            Case "section_information" : Return Localization.T("Информация и управление")
            Case "show_picture_size" : Return Localization.T("Размеры изображения")
            Case "show_file_size" : Return Localization.T("Размер файла")
            Case "show_file_date" : Return Localization.T("Дата изменения файла")
            Case "show_info_overlay" : Return Localization.T("Информация поверх изображения")
            Case "wheel_zooms" : Return Localization.T("Масштабирование колёсиком")
            Case "slideshow_interval" : Return Localization.T("Интервал слайдшоу")
            Case "thumbnail_size" : Return Localization.T("Размер карточки изображения")
            Case "section_accessibility" : Return Localization.T("Комфорт просмотра")
            Case "new_image_scale" : Return Localization.T("Масштаб нового изображения")
            Case "reduce_motion" : Return Localization.T("Уменьшать анимацию")
            Case "section_slideshow_behavior" : Return Localization.T("Поведение слайд-шоу")
            Case "slideshow_random_order" : Return Localization.T("Порядок показа")
            Case "stop_slideshow_manual" : Return Localization.T("Останавливать при ручной навигации")
            Case "slideshow_ui" : Return Localization.T("Интерфейс во время слайд-шоу")
            Case "exif_rotate" : Return Localization.T("Автоповорот по EXIF")
            Case "hq_scaling" : Return Localization.T("Качественное масштабирование")
            Case "video_loop" : Return Localization.T("Повторять видео")
            Case "video_mute" : Return Localization.T("Запускать без звука")
            Case "video_volume" : Return Localization.T("Громкость видео")
            Case "section_video_behavior" : Return Localization.T("Поведение видео")
            Case "video_autoplay" : Return Localization.T("Запускать видео автоматически")
            Case "video_controls_delay" : Return Localization.T("Задержка скрытия панели, с")
            Case "video_controls_paused" : Return Localization.T("Показывать панель при паузе")
            Case "video_click_action" : Return Localization.T("Одиночный клик по видео")
            Case "video_end_action" : Return Localization.T("После окончания видео")
            Case "preferred_audio_language" : Return Localization.T("Предпочтительный язык звука")
            Case "preferred_subtitle_language" : Return Localization.T("Предпочтительный язык субтитров")
            Case "advance_after_copy" : Return Localization.T("Переходить к следующему файлу после копирования")
            Case "no_confirmation" : Return Localization.T("Не запрашивать подтверждение")
            Case "image_associations" : Return Localization.T("Форматы изображений")
            Case "video_associations" : Return Localization.T("Форматы видео")
            Case "interface_language" : Return Localization.T("Язык интерфейса")
            Case "section_file_behavior" : Return Localization.T("Поведение файлов")
            Case "name_collision" : Return Localization.T("Совпадение имён")
            Case "after_file_operation" : Return Localization.T("После копирования или перемещения")
            Case "include_subfolders" : Return Localization.T("Просматривать вложенные папки")
            Case "included_extensions" : Return Localization.T("Типы файлов")
            Case "recent_files_limit" : Return Localization.T("Размер истории файлов")
            Case "recent_folders_limit" : Return Localization.T("Размер истории папок")
            Case "startup_open" : Return Localization.T("При запуске открывать")
            Case "allow_new_windows" : Return Localization.T("Разрешать запуск в новых окнах")
            Case "secondary_instance_warning" : Return Localization.T("Это дополнительное окно. Изменения настроек действуют до его закрытия и не сохраняются - настройки хранит первое окно.")
            Case "ocr_enabled" : Return Localization.T("Включить OCR")
            Case "ocr_auto" : Return Localization.T("Запускать автоматически")
            Case "section_translation" : Return Localization.TC("settings", "Перевод")
            Case "ocr_provider" : Return Localization.T("Сервис перевода")
            Case "ocr_endpoint" : Return Localization.T("Адрес сервиса")
            Case "ocr_server" : Return Localization.T("Локальный сервер Ollama")
            Case "ocr_model" : Return Localization.T("Модель перевода")
            Case "ocr_api" : Return Localization.T("API-ключ")
            Case "ocr_target" : Return Localization.T("Язык перевода")
            Case "section_recognition" : Return Localization.T("Распознавание текста")
            Case "ocr_source" : Return Localization.T("Язык исходного текста")
            Case "ocr_quality" : Return Localization.T("Качество распознавания")
            Case "ocr_mode" : Return Localization.T("Режим OCR")
            Case "ocr_download" : Return Localization.T("Языковые данные")
            Case "section_overlay" : Return Localization.T("Панель перевода")
            Case "ocr_opacity" : Return Localization.T("Непрозрачность панели")
            Case "ocr_overlay_visible" : Return Localization.T("Показывать панель перевода")
            Case "ocr_disk_cache" : Return Localization.T("Кэшировать результаты на диске")
            Case "ocr_cache_limit" : Return Localization.T("Максимальный размер OCR-кэша, МБ")
            Case "sftp_intro" : Return Localization.T("Доступ к медиатеке с телефона")
            Case "sftp_manager" : Return Localization.T("Публикация папок по SFTP")
            Case "sftp_guide" : Return Localization.T("Инструкция по подключению")
            Case "android_app" : Return Localization.T("Приложение для Android")
            Case "about_intro" : Return "Fast Media Sorter Lite"
            Case "support_send_logs" : Return Localization.T("Отправить логи автору")
            Case "doc_html_intro" : Return "Doc HTML Translate"
            Case "doc_html_site" : Return Localization.T("Сайт Doc HTML Translate")
            Case "project_site" : Return Localization.T("Сайт проекта")
            Case "project_github" : Return "GitHub"
            Case "project_releases" : Return Localization.T("Новые версии")
            Case "project_privacy" : Return Localization.T("Политика конфиденциальности")
            Case "project_email" : Return Localization.T("Связаться с автором")
            Case Else : Return key
        End Select
    End Function

    Private Function ModernSettingDescription(key As String, ru As Boolean) As String
        Select Case key
            Case "recipients_overlay" : Return Localization.T("Показывает компактный список папок в левом верхнем углу просмотрщика.")
            Case "recipients_position" : Return Localization.T("Угол области просмотра, в котором будет показана таблица.")
            Case "recipients_width" : Return Localization.T("Ширина панели в пикселях.")
            Case "recipients_font" : Return Localization.T("Размер текста в пунктах.")
            Case "recipients_opacity" : Return Localization.T("Прозрачность фона таблицы в процентах.")
            Case "recipients_rows" : Return Localization.T("Лишние строки будут доступны прокруткой.")
            Case "background_color" : Return Localization.T("Выберите, как вычислять цвет свободной области вокруг фотографии.")
            Case "perspective" : Return Localization.T("Продолжает края фотографии на свободную область экрана.")
            Case "dynamic_perspective" : Return Localization.T("Плавно смешивает продолжение изображения с выбранным фоном.")
            Case "animated_perspective" : Return Localization.T("Проявляет ореол короткой анимацией при открытии следующего файла.")
            Case "show_picture_size" : Return Localization.T("Показывает ширину и высоту текущего изображения.")
            Case "show_file_size" : Return Localization.T("Добавляет размер текущего файла в информационную строку.")
            Case "show_file_date" : Return Localization.T("Добавляет дату и время последнего изменения файла.")
            Case "show_info_overlay" : Return Localization.T("Показывает имя файла и позицию в списке прямо на изображении.")
            Case "wheel_zooms" : Return Localization.T("Колесо меняет масштаб; при отключении оно листает файлы.")
            Case "slideshow_interval" : Return Localization.T("Базовая пауза между изображениями, в секундах.")
            Case "thumbnail_size" : Return Localization.T("Размер карточек в панели предварительного просмотра.")
            Case "new_image_scale" : Return Localization.T("Начальный масштаб при открытии следующего изображения.")
            Case "reduce_motion" : Return Localization.T("Отключает декоративные переходы и анимацию ореола.")
            Case "slideshow_random_order" : Return Localization.T("Выберите обычный, случайный или перемешанный порядок.")
            Case "stop_slideshow_manual" : Return Localization.T("Ручной переход останавливает таймер слайд-шоу.")
            Case "slideshow_ui" : Return Localization.T("Управление и статус можно временно скрывать для просмотра.")
            Case "exif_rotate" : Return Localization.T("Учитывает ориентацию, записанную камерой или телефоном.")
            Case "hq_scaling" : Return Localization.T("Делает уменьшенные изображения резче, используя качественную интерполяцию.")
            Case "video_loop" : Return Localization.T("После окончания видео запускается снова.")
            Case "video_mute" : Return Localization.T("Каждое видео начинает воспроизводиться с выключенным звуком.")
            Case "video_volume" : Return Localization.T("Начальная громкость воспроизведения, от 0 до 100 %.")
            Case "video_autoplay" : Return Localization.T("Если выключено, видео открывается на паузе.")
            Case "video_controls_delay" : Return Localization.T("Через сколько секунд бездействия скрывать панель управления.")
            Case "video_controls_paused" : Return Localization.T("Не скрывает управление, пока видео поставлено на паузу.")
            Case "video_click_action" : Return Localization.T("Действие левой кнопки мыши на поверхности видео.")
            Case "video_end_action" : Return Localization.T("Что делать, когда воспроизведение достигло конца.")
            Case "preferred_audio_language", "preferred_subtitle_language" : Return Localization.T("Код языка, например ru, en или rus. Оставьте пустым для выбора плеера.")
            Case "advance_after_copy" : Return Localization.T("После копирования сразу показывается следующий файл. Снимите галочку, чтобы остаться на текущем.")
            Case "no_confirmation" : Return Localization.T("Файловые операции выполняются сразу. Используйте осторожно.")
            Case "image_associations" : Return Localization.T("Открывает системные параметры приложений по умолчанию для изображений.")
            Case "video_associations" : Return Localization.T("Открывает системные параметры приложений по умолчанию для видео.")
            Case "interface_language" : Return Localization.T("Выберите, на каком языке показывать интерфейс.")
            Case "name_collision" : Return Localization.T("Что делать, если в папке-получателе уже есть файл с тем же именем.")
            Case "after_file_operation" : Return Localization.T("Выберите, что показывать после успешной операции.")
            Case "include_subfolders" : Return Localization.T("Добавляет подходящие файлы из всех вложенных папок.")
            Case "included_extensions" : Return Localization.T("Расширения через точку с запятой; пустое поле — все поддерживаемые.")
            Case "recent_files_limit", "recent_folders_limit" : Return Localization.T("0 отключает сохранение новых записей.")
            Case "startup_open" : Return Localization.T("Что будет показано при обычном запуске без файла в командной строке.")
            Case "allow_new_windows" : Return Localization.T("Обычно повторный запуск программы отдаёт файл уже открытому окну. Если отмечено, каждый запуск открывает своё окно.")
            Case "ocr_enabled" : Return Localization.T("Разрешает распознавание текста на открытых изображениях.")
            Case "ocr_auto" : Return Localization.T("Распознаёт текст без отдельной команды после открытия изображения.")
            Case "ocr_provider" : Return Localization.T("Выберите локальный или сетевой движок, который выполнит перевод.")
            Case "ocr_endpoint" : Return Localization.T("URL API выбранного сервиса перевода.")
            Case "ocr_server" : Return Localization.T("Установите или запустите Ollama для локального перевода.")
            Case "ocr_model" : Return Localization.T("Модель Ollama; отсутствующую модель можно загрузить этой же строкой.")
            Case "ocr_api" : Return Localization.T("Ключ доступа нужен только сервисам, которые его требуют.")
            Case "ocr_target" : Return Localization.T("Язык, на который будет переведён распознанный текст.")
            Case "ocr_source" : Return Localization.T("Язык надписей на изображении; автоопределение подходит большинству случаев.")
            Case "ocr_quality" : Return Localization.T("Баланс между скоростью обработки и точностью результата.")
            Case "ocr_mode" : Return Localization.T("Выберите подходящий способ разметки текста на изображении.")
            Case "ocr_download" : Return Localization.T("Загрузите недостающие данные для выбранного языка OCR.")
            Case "ocr_opacity" : Return Localization.T("Непрозрачность фона перевода: от 30 до 100 %.")
            Case "ocr_overlay_visible" : Return Localization.T("Показывает или скрывает уже распознанный перевод поверх изображения.")
            Case "ocr_disk_cache" : Return Localization.T("Сохраняет распознанный текст, чтобы повторно не обрабатывать файл.")
            Case "ocr_cache_limit" : Return Localization.T("0 означает без ограничения; очистка по LRU выполняется после записи.")
            Case "sftp_intro" : Return Localization.T("Опубликуйте выбранные папки встроенным SFTP-сервером и открывайте их в мобильном приложении.")
            Case "sftp_manager" : Return Localization.T("Открывает отдельное приложение управления SFTP-доступом и опубликованными папками.")
            Case "sftp_guide" : Return Localization.T("Пошаговая настройка сервера, сети и подключения мобильного клиента.")
            Case "android_app" : Return Localization.T("Страница мобильного клиента Fast Media Sorter для Android.")
            Case "about_intro" : Return Localization.TF("Современный просмотрщик и сортировщик фото и видео." & vbCrLf & "Версия {0}", ShortSettingsVersion())
            Case "support_send_logs" : Return Localization.T("Соберёт журналы работы программы в архив и создаст письмо автору. Ничего не отправляется без вашего подтверждения.")
            Case "doc_html_intro" : Return Localization.T("Преобразует EPUB, PDF и другие документы в локальный HTML с оглавлением и переводом в браузере. Для изображений, сканов и комиксов создаёт переводимый OCR-слой.")
            Case "doc_html_site" : Return Localization.T("Описание возможностей, поддерживаемых форматов, установки и работы с документами и изображениями.")
            Case "project_site" : Return Localization.T("Описание возможностей, инструкции и материалы проекта.")
            Case "project_github" : Return Localization.T("Исходный код, задачи и техническая документация.")
            Case "project_releases" : Return Localization.T("Список опубликованных версий и файлов для установки.")
            Case "project_privacy" : Return Localization.T("Как приложение обрабатывает пользовательские данные.")
            Case "project_email" : Return Localization.T("Написать автору проекта по электронной почте.")
            Case Else : Return String.Empty
        End Select
    End Function

    Private Shared Function ShortSettingsVersion() As String
        Return Application.ProductVersion.Split("+"c)(0)
    End Function

    Private Sub BuildModernDataLayout()
        If modernDataLayoutBuilt Then Return
        modernDataLayoutBuilt = True

        Dim destinations As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_1)
        AddSettingRow(destinations, "recipients_overlay", SetOnTop, 34)
        Dim gridHost As New Panel With {.Height = 398, .BackColor = SettingsSurface, .Padding = New Padding(12)}
        ReparentModernSettingsControl(lbl_Grid_Hint, gridHost)
        lbl_Grid_Hint.Dock = DockStyle.Bottom
        ' Two lines' worth: the hint names both actions now (move on a double-click, copy on
        ' Shift + double-click) and at 34 px the wrapped second line was simply cut off.
        lbl_Grid_Hint.Height = 50
        lbl_Grid_Hint.AutoSize = False
        lbl_Grid_Hint.Padding = New Padding(2, 8, 2, 0)
        ReparentModernSettingsControl(Data_Grid_View, gridHost)
        Data_Grid_View.Dock = DockStyle.Fill
        Data_Grid_View.ScrollBars = ScrollBars.Vertical
        destinations.Controls.Add(gridHost)

        Dim viewing As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_2)
        AddSectionHeader(viewing, "section_background")
        AddSettingRow(viewing, "background_color", cmbox_color_schema, 210)
        AddSettingRow(viewing, "perspective", chb_perspectiva, 34, True)
        AddSettingRow(viewing, "dynamic_perspective", chk_Dynamic_Perspective, 34, True)
        AddSettingRow(viewing, "animated_perspective", chk_Animated_Perspective, 34, True)
        AddSectionHeader(viewing, "section_information")
        AddSettingRow(viewing, "show_picture_size", chkb_show_pic_size, 34, True)
        AddSettingRow(viewing, "show_file_size", chkb_show_file_size, 34, True)
        AddSettingRow(viewing, "show_file_date", chkb_is_to_show_file_datetime, 34, True)
        AddSettingRow(viewing, "show_info_overlay", chk_Show_Info_Overlay, 34, True)
        AddSettingRow(viewing, "wheel_zooms", chk_Wheel_Zooms, 34, True)
        AddSettingRow(viewing, "slideshow_interval", num_Slideshow_Interval, 100, True)
        AddSettingRow(viewing, "thumbnail_size", cmb_Picture_Size, 180, True)

        Dim video As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_3)
        AddSettingRow(video, "exif_rotate", chk_Exif_AutoRotate, 34, True)
        AddSettingRow(video, "hq_scaling", chk_Hq_Scaling, 34, True)
        AddSettingRow(video, "video_loop", chkb_video_loop, 34, True)
        AddSettingRow(video, "video_mute", chk_Video_Mute, 34, True)
        AddSettingRow(video, "video_volume", num_Video_Volume, 100, True)

        Dim files As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_4)
        ' The global "copy mode" is gone from this window: copying is now asked for at the
        ' moment of the action (Shift+digit, the recipients overlay, both media menus,
        ' Shift+double-click in the grid), not switched on beforehand and forgotten about.
        ' The control itself stays hidden here for net48 persistence compatibility, exactly
        ' like the independent-thread checkbox below.
        ' SPECIFICATION_COPY_ACTIONS_REWORK.md §4.1.
        chkbox_Copy_Mode.Visible = False
        AddAdvanceAfterCopyRow(files)
        ' .NET 10 always uses the background file-operation queue. Do not expose a
        ' switch that cannot change anything; the hidden control remains for net48
        ' persistence compatibility.
        chkbox_Independent_Thread_For_File_Operation.Visible = False
        AddSettingRow(files, "no_confirmation", chkb_no_request_before_file_operation, 34, True)
        AddSettingRow(files, "image_associations", btn_Set_As_Default, 230, True)
        AddSettingRow(files, "video_associations", btn_Set_As_Default_Video, 230, True)
        AddSettingRow(files, "interface_language", btn_Language, 100, True)

        Dim ocr As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_5)
        Dim translation As FlowLayoutPanel = CreateModernPageFlow(modernTranslationPage)
        ocrInner.Visible = False
        AddSettingRow(ocr, "ocr_enabled", chkOcrEnabled, 34, True)
        AddSettingRow(ocr, "ocr_auto", chkOcrAuto, 34, True)
        AddSectionHeader(ocr, "section_recognition")
        AddSettingRow(ocr, "ocr_source", cmbOcrSource, 300, True)
        AddSettingRow(ocr, "ocr_quality", cmbOcrQuality, 300, True)
        AddSettingRow(ocr, "ocr_mode", cmbOcrMode, 300, True)
        AddSettingRow(ocr, "ocr_download", btnOcrDownload, 300, True)

        AddSectionHeader(translation, "section_translation")
        AddSettingRow(translation, "ocr_provider", cmbOcrProvider, 260, True)
        AddSettingRow(translation, "ocr_endpoint", txtOcrEndpoint, 300, True)
        AddSettingRow(translation, "ocr_server", MakeModernControlStrip({btnOcrInstallOllama, btnOcrStartOllama}, 310), 310, True)
        AddSettingRow(translation, "ocr_model", MakeModernControlStrip({cmbOcrModelName, btnOcrPullModel}, 310), 310, True)
        AddSettingRow(translation, "ocr_api", txtOcrApi, 300, True)
        AddSettingRow(translation, "ocr_target", cmbOcrTarget, 300, True)
        AddSectionHeader(translation, "section_overlay")
        AddSettingRow(translation, "ocr_opacity", MakeModernOpacityStrip(), 330, True)
        AddSettingRow(translation, "ocr_overlay_visible", chkOcrOverlayVisible, 34, True)
        AddSettingRow(translation, "ocr_disk_cache", chkOcrDisk, 34, True)
        ReparentModernSettingsControl(lblOcrStatus, translation)
        lblOcrStatus.AutoSize = False
        lblOcrStatus.Height = 34
        lblOcrStatus.Margin = New Padding(8, 4, 8, 8)

        BuildModernSftpPage()
        BuildModernAboutPage()
        If MultiWindowPolicy.IsSecondaryInstance() Then AddInformationBlock(files, "secondary_instance_warning")
        AddExpandedSettingsRows(destinations, viewing, video, files, ocr)

        For Each oldLabel As Control In New Control() {lbl_Color, lbl_Slideshow_Interval, lbl_Picture_at_Panel_Size,
                                                        lbl_Video_Volume, lblOcrTranslator, lblOcrEndpoint, lblOcrServer,
                                                        lblOcrModel, lblOcrApi, lblOcrTarget, lblOcrSource, lblOcrQuality,
                                                        lblOcrMode, lblOcrOpacity}
            oldLabel.Visible = False
        Next
        For Each oldGroup As GroupBox In New GroupBox() {grp_Background, grp_OnScreen, grp_Slideshow, grp_Panel,
                                                          grp_Quality, grp_Video, grp_FileOps, grp_Integration, grp_Language}
            oldGroup.Visible = False
        Next
    End Sub

    Private Function CreateModernPageFlow(page As TabPage) As FlowLayoutPanel
        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = True,
            .Padding = New Padding(6),
            .BackColor = SettingsCanvas
        }
        page.Controls.Add(flow)
        flow.BringToFront()
        modernPageFlows.Add(flow)
        AddHandler flow.ClientSizeChanged, Sub() ResizeModernPageItems(flow)
        Return flow
    End Function

    Private Sub ResizeModernPageItems(flow As FlowLayoutPanel)
        Dim width As Integer = Math.Max(460, flow.ClientSize.Width - flow.Padding.Horizontal - 18)
        Dim useTwoColumns As Boolean = width >= 820
        For Each child As Control In flow.Controls
            Dim compact As Boolean = String.Equals(TryCast(child.Tag, String), "modern:compact", StringComparison.Ordinal)
            child.Width = If(compact AndAlso useTwoColumns, Math.Max(300, (width - 8) \ 2), width)
        Next
    End Sub

    Private Sub AddSectionHeader(flow As FlowLayoutPanel, key As String)
        Dim host As New Panel With {.Height = 28, .Margin = New Padding(0, 6, 0, 0), .BackColor = SettingsCanvas, .Tag = "modern:full"}
        Dim title As New Label With {.AutoSize = False, .Dock = DockStyle.Fill, .Font = New Font("Segoe UI Semibold", 9.5F),
                                     .ForeColor = SettingsText, .TextAlign = ContentAlignment.MiddleLeft, .Padding = New Padding(4, 0, 0, 0)}
        host.Controls.Add(title)
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = title})
    End Sub

    Private Sub AddSettingRow(flow As FlowLayoutPanel, key As String, editor As Control, editorWidth As Integer, Optional compact As Boolean = False)
        Dim host As New Panel With {.Height = 56, .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12, 5, 12, 5),
                                   .Tag = If(compact, "modern:compact", "modern:full")}
        Dim title As New Label With {.AutoSize = False, .Location = New Point(14, 8), .Height = 20,
                                     .Font = New Font("Segoe UI Semibold", 8.75F), .ForeColor = SettingsText}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(14, 31), .Height = 22,
                                           .Font = New Font("Segoe UI", 8.0F), .ForeColor = SettingsMutedText,
                                           .AutoEllipsis = True}
        Dim row As New ModernSettingRow With {.Key = key, .Host = host, .Title = title, .Description = description, .Editor = editor, .Compact = compact}
        Dim check As CheckBox = TryCast(editor, CheckBox)
        ReparentModernSettingsControl(editor, host)
        editor.Width = editorWidth

        If check IsNot Nothing Then
            editor.Anchor = AnchorStyles.Top Or AnchorStyles.Left
            ConfigureModernCheckbox(check, row)
            title.Cursor = Cursors.Hand
            description.Cursor = Cursors.Hand
            title.Tag = check
            description.Tag = check
            AddHandler title.Click, AddressOf ToggleModernCheckboxFromText
            AddHandler description.Click, AddressOf ToggleModernCheckboxFromText
        ElseIf TypeOf editor Is Button Then
            editor.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            editor.Height = 34
        Else
            editor.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        End If

        host.Controls.Add(title)
        host.Controls.Add(description)
        title.BringToFront()
        description.BringToFront()
        AddHandler host.Resize,
            Sub()
                If check IsNot Nothing Then
                    editor.Left = 14
                    title.Left = editor.Right + 10
                    description.Left = title.Left
                    title.Width = Math.Max(180, host.ClientSize.Width - title.Left - 14)
                    description.Width = title.Width
                    editor.Top = title.Top + Math.Max(0, (title.Height - editor.Height) \ 2)
                Else
                    editor.Width = Math.Min(editorWidth, Math.Max(150, host.ClientSize.Width - 260))
                    editor.Left = Math.Max(230, Math.Min(420, host.ClientSize.Width - editor.Width - 12))
                    Dim labelWidth As Integer = Math.Max(180, editor.Left - 32)
                    title.Left = 14
                    description.Left = 14
                    title.Width = labelWidth
                    description.Width = labelWidth
                    editor.Top = Math.Max(10, (host.ClientSize.Height - editor.Height) \ 2)
                End If
            End Sub
        flow.Controls.Add(host)
        modernSettingsRows.Add(row)
        SizeModernSettingRow(row)
    End Sub

    Private Shared Sub ToggleModernCheckboxFromText(sender As Object, e As EventArgs)
        Dim label As Label = TryCast(sender, Label)
        Dim check As CheckBox = If(label Is Nothing, Nothing, TryCast(label.Tag, CheckBox))
        If check Is Nothing OrElse Not check.Enabled Then Return
        check.Checked = Not check.Checked
        check.Focus()
    End Sub

    Private Sub SizeModernSettingRow(row As ModernSettingRow)
        If row.Description Is Nothing Then Return
        Dim titleHeight As Integer = Math.Max(19, row.Title.Font.Height + 2)
        Dim lines As Integer = If(row.Key.EndsWith("_intro", StringComparison.Ordinal), 2, 1)
        Dim descriptionHeight As Integer = Math.Max(17, row.Description.Font.Height * lines + 2)
        row.Title.Top = 6
        row.Title.Height = titleHeight
        row.Description.Top = row.Title.Bottom + 1
        row.Description.Height = descriptionHeight
        row.Host.Height = row.Description.Bottom + 6
        Dim check As CheckBox = TryCast(row.Editor, CheckBox)
        If check IsNot Nothing Then
            check.Left = 14
            row.Title.Left = check.Right + 10
            row.Description.Left = row.Title.Left
            row.Title.Width = Math.Max(180, row.Host.ClientSize.Width - row.Title.Left - 14)
            row.Description.Width = row.Title.Width
            check.Top = row.Title.Top + Math.Max(0, (row.Title.Height - check.Height) \ 2)
        ElseIf row.Editor IsNot Nothing Then
            row.Editor.Top = Math.Max(10, (row.Host.ClientSize.Height - row.Editor.Height) \ 2)
        End If
    End Sub

    Private Function MakeModernControlStrip(controls As Control(), width As Integer) As FlowLayoutPanel
        Dim strip As New FlowLayoutPanel With {.Width = width, .Height = 34, .FlowDirection = FlowDirection.LeftToRight,
                                               .WrapContents = False, .Margin = Padding.Empty, .Padding = Padding.Empty,
                                               .BackColor = SettingsSurface}
        For index As Integer = 0 To controls.Length - 1
            Dim control As Control = controls(index)
            ReparentModernSettingsControl(control, strip)
            control.Margin = New Padding(0, 2, 6, 2)
            If controls.Length = 2 Then
                If index = 0 AndAlso TypeOf control Is ComboBox Then
                    control.Width = 208
                ElseIf index = 1 AndAlso TypeOf controls(0) Is ComboBox Then
                    control.Width = 96
                Else
                    control.Width = (width - 8) \ 2
                End If
            End If
            If TypeOf control Is Button Then control.Height = 30
        Next
        AddHandler strip.Resize, Sub() LayoutModernControlStrip(strip, controls)
        LayoutModernControlStrip(strip, controls)
        Return strip
    End Function

    Private Shared Sub LayoutModernControlStrip(strip As FlowLayoutPanel, controls As Control())
        If controls.Length <> 2 Then Return
        Dim available As Integer = Math.Max(120, strip.ClientSize.Width - 8)
        If TypeOf controls(0) Is ComboBox Then
            controls(1).Width = Math.Min(96, Math.Max(72, available \ 3))
            controls(0).Width = Math.Max(80, available - controls(1).Width)
        Else
            controls(0).Width = available \ 2
            controls(1).Width = available - controls(0).Width
        End If
    End Sub

    Private Function MakeModernOpacityStrip() As Panel
        Dim strip As New Panel With {.Width = 330, .Height = 48, .Margin = Padding.Empty, .Padding = Padding.Empty,
                                     .BackColor = SettingsSurface}
        ReparentModernSettingsControl(trkOcrOpacity, strip)
        trkOcrOpacity.Location = Point.Empty
        trkOcrOpacity.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        ReparentModernSettingsControl(lblOcrOpacityVal, strip)
        lblOcrOpacityVal.AutoSize = False
        lblOcrOpacityVal.Visible = True
        lblOcrOpacityVal.Width = 66
        lblOcrOpacityVal.Height = 34
        lblOcrOpacityVal.TextAlign = ContentAlignment.MiddleLeft
        lblOcrOpacityVal.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        lblOcrOpacityVal.BringToFront()
        AddHandler strip.Resize,
            Sub()
                lblOcrOpacityVal.Width = 66
                lblOcrOpacityVal.Left = Math.Max(0, strip.ClientSize.Width - lblOcrOpacityVal.Width)
                lblOcrOpacityVal.Top = 6
                trkOcrOpacity.Width = Math.Max(100, lblOcrOpacityVal.Left - 6)
            End Sub
        Return strip
    End Function

    Private Sub BuildModernSftpPage()
        Dim flow As FlowLayoutPanel = CreateModernPageFlow(modernSftpPage)
        AddInformationBlock(flow, "sftp_intro")
        If btn_Share_Manager IsNot Nothing Then AddSettingRow(flow, "sftp_manager", btn_Share_Manager, 230, True)
        AddProjectLinkRow(flow, "sftp_guide", "https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html")
        AddProjectLinkRow(flow, "android_app", Localization.T("https://serzhyale.github.io/FastMediaSorter_mob_v2/index-ru.html"))
    End Sub

    Private Sub BuildModernAboutPage()
        Dim flow As FlowLayoutPanel = CreateModernPageFlow(modernAboutPage)
        AddInformationBlock(flow, "about_intro")
        ' Support first, project links after: this row is looked for in trouble, the
        ' links out of curiosity (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md §2).
        EnsureSendLogsButton()
        AddSettingRow(flow, "support_send_logs", btn_Send_Logs, 230, True)
        AddInformationBlock(flow, "doc_html_intro")
        AddProjectLinkRow(flow, "doc_html_site", "https://serzhyale.github.io/doc-html-translate/")
        AddProjectLinkRow(flow, "project_site", "https://serzhyale.github.io/FastMediaSorter_Lite/")
        AddProjectLinkRow(flow, "project_github", "https://github.com/SerZhyAle/FastMediaSorter_Lite")
        AddProjectLinkRow(flow, "project_releases", "https://github.com/SerZhyAle/FastMediaSorter_Lite/releases")
        AddProjectLinkRow(flow, "project_privacy", "https://serzhyale.github.io/FastMediaSorter_Lite/privacy.html")
        AddProjectLinkRow(flow, "project_email", "mailto:" & Author_Email & "?subject=Fast Media Sorter for Windows")
    End Sub

    Private Sub AddInformationBlock(flow As FlowLayoutPanel, key As String)
        Dim host As New Panel With {.Height = 62, .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12), .Tag = "modern:full"}
        Dim title As New Label With {.AutoSize = False, .Location = New Point(12, 6), .Height = 20,
                                     .Font = New Font("Segoe UI Semibold", 9.5F), .ForeColor = SettingsText}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(12, 27), .Height = 30,
                                           .Font = New Font("Segoe UI", 8.0F), .ForeColor = SettingsMutedText, .AutoEllipsis = True}
        host.Controls.Add(title)
        host.Controls.Add(description)
        AddHandler host.Resize,
            Sub()
                title.Width = host.ClientSize.Width - 24
                description.Width = host.ClientSize.Width - 24
            End Sub
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = title, .Description = description})
    End Sub

    Private Sub AddProjectLinkRow(flow As FlowLayoutPanel, key As String, url As String)
        Dim host As New Panel With {.Height = 50, .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12), .Tag = "modern:compact"}
        Dim link As New LinkLabel With {.AutoSize = False, .Location = New Point(12, 6), .Height = 19,
                                        .Font = New Font("Segoe UI Semibold", 8.75F), .Tag = url,
                                        .TextAlign = ContentAlignment.MiddleLeft}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(12, 26), .Height = 18,
                                           .Font = New Font("Segoe UI", 8.0F), .ForeColor = SettingsMutedText, .AutoEllipsis = True}
        AddHandler link.LinkClicked, AddressOf ModernProjectLinkClicked
        host.Controls.Add(link)
        host.Controls.Add(description)
        AddHandler host.Resize,
            Sub()
                link.Width = host.ClientSize.Width - 24
                description.Width = host.ClientSize.Width - 24
            End Sub
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = link, .Description = description, .Compact = True})
    End Sub

    Private Sub ModernProjectLinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim link As LinkLabel = TryCast(sender, LinkLabel)
        If link Is Nothing Then Return
        Try
            Process.Start(New ProcessStartInfo(Convert.ToString(link.Tag)) With {.UseShellExecute = True})
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub LoadSystemSettingsPalette()
        If SystemInformation.HighContrast Then
            settingsDarkMode = GetSettingsBrightness(SystemColors.Window) < 128
            SettingsCanvas = SystemColors.Control
            SettingsSurface = SystemColors.Window
            SettingsSidebar = SystemColors.Control
            SettingsSidebarMuted = SystemColors.GrayText
            SettingsAccent = SystemColors.Highlight
            SettingsLink = SystemColors.HotTrack
            SettingsText = SystemColors.WindowText
            SettingsMutedText = SystemColors.GrayText
            SettingsBorder = SystemColors.ControlDark
            Return
        End If

        Dim appsUseLightTheme As Boolean = True
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                If key IsNot Nothing Then
                    Dim value As Object = key.GetValue("AppsUseLightTheme")
                    If value IsNot Nothing Then appsUseLightTheme = Convert.ToInt32(value) <> 0
                End If
            End Using
        Catch
            appsUseLightTheme = GetSettingsBrightness(SystemColors.Window) >= 128
        End Try

        settingsDarkMode = Not appsUseLightTheme
        SettingsAccent = SystemColors.Highlight
        If settingsDarkMode Then
            SettingsCanvas = Color.FromArgb(30, 31, 34)
            SettingsSurface = Color.FromArgb(39, 41, 45)
            SettingsSidebar = Color.FromArgb(25, 26, 29)
            SettingsSidebarMuted = Color.FromArgb(185, 188, 195)
            SettingsText = Color.FromArgb(244, 245, 247)
            SettingsMutedText = Color.FromArgb(177, 181, 189)
            SettingsBorder = Color.FromArgb(68, 71, 78)
            ' A standard accent blue is too dark and saturated for small text on
            ' a charcoal surface. Keep links recognisably blue, but use a calm,
            ' high-contrast tint that is comfortable in a long list of links.
            SettingsLink = Color.FromArgb(145, 205, 245)
        Else
            SettingsCanvas = Color.FromArgb(245, 246, 248)
            SettingsSurface = SystemColors.Window
            SettingsSidebar = Color.FromArgb(238, 240, 244)
            SettingsSidebarMuted = SystemColors.GrayText
            SettingsText = SystemColors.WindowText
            SettingsMutedText = SystemColors.GrayText
            SettingsBorder = Color.FromArgb(214, 217, 223)
            SettingsLink = SystemColors.HotTrack
        End If
    End Sub

    Private Sub ApplySystemSettingsPalette()
        If Not modernSettingsBuilt OrElse modernSettingsShell Is Nothing Then Return

        BackColor = SettingsCanvas
        modernSettingsShell.BackColor = SettingsCanvas
        modernSettingsSidebar.BackColor = SettingsSidebar
        modernSettingsHeader.BackColor = SettingsSurface
        modernSettingsContent.BackColor = SettingsCanvas
        modernSettingsFooter.BackColor = SettingsSurface
        Tab_Control.BackColor = SettingsCanvas
        modernProductLabel.ForeColor = SettingsText
        modernSidebarSectionLabel.ForeColor = SettingsSidebarMuted
        modernSettingsTitle.ForeColor = SettingsText
        modernSettingsSubtitle.ForeColor = SettingsMutedText
        LinkLabel1.LinkColor = SettingsLink
        LinkLabel1.ActiveLinkColor = SettingsLink
        LinkLabel1.VisitedLinkColor = SettingsLink

        For Each page As TabPage In Tab_Control.TabPages
            page.BackColor = SettingsCanvas
        Next
        For Each flow As FlowLayoutPanel In modernPageFlows
            flow.BackColor = SettingsCanvas
        Next
        For Each row As ModernSettingRow In modernSettingsRows
            row.Host.BackColor = If(row.Description Is Nothing, SettingsCanvas, SettingsSurface)
            row.Title.ForeColor = SettingsText
            If row.Description IsNot Nothing Then row.Description.ForeColor = SettingsMutedText
        Next

        ApplyModernPaletteToTree(modernSettingsShell)

        Data_Grid_View.BackgroundColor = SettingsSurface
        Data_Grid_View.GridColor = SettingsBorder
        Data_Grid_View.ColumnHeadersDefaultCellStyle.BackColor = BlendSettingsColor(SettingsSurface, SettingsCanvas, 0.7F)
        Data_Grid_View.ColumnHeadersDefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.BackColor = SettingsSurface
        Data_Grid_View.DefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.SelectionBackColor = BlendSettingsColor(SettingsAccent, SettingsSurface, 0.72F)
        Data_Grid_View.DefaultCellStyle.SelectionForeColor = SettingsText

        RefreshModernSettingsHeader()
        ApplySettingsTitleBarTheme()
        Invalidate(True)
    End Sub

    Private Sub ApplyModernPaletteToTree(parent As Control)
        For Each control As Control In parent.Controls
            If TypeOf control Is FlowLayoutPanel Then
                control.BackColor = If(modernPageFlows.Contains(DirectCast(control, FlowLayoutPanel)), SettingsCanvas, SettingsSurface)
            ElseIf TypeOf control Is Panel Then
                Dim row As ModernSettingRow = modernSettingsRows.Find(Function(candidate) candidate.Host Is control)
                If row IsNot Nothing Then
                    control.BackColor = If(row.Description Is Nothing, SettingsCanvas, SettingsSurface)
                ElseIf TypeOf control.Parent Is FlowLayoutPanel Then
                    control.BackColor = SettingsSurface
                End If
            ElseIf TypeOf control Is Button Then
                Dim button As Button = DirectCast(control, Button)
                If modernSettingsNavButtons Is Nothing OrElse Array.IndexOf(modernSettingsNavButtons, button) < 0 Then
                    button.UseVisualStyleBackColor = False
                    button.BackColor = SettingsSurface
                    button.ForeColor = SettingsLink
                    button.FlatAppearance.BorderColor = SettingsBorder
                    button.FlatAppearance.MouseOverBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.1F)
                    button.FlatAppearance.MouseDownBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.18F)
                End If
            ElseIf TypeOf control Is TextBox OrElse TypeOf control Is ComboBox OrElse TypeOf control Is NumericUpDown Then
                control.BackColor = If(settingsDarkMode, Color.FromArgb(50, 52, 57), SystemColors.Window)
                control.ForeColor = SettingsText
            ElseIf TypeOf control Is CheckBox Then
                control.BackColor = SettingsSurface
                control.ForeColor = SettingsText
            ElseIf TypeOf control Is LinkLabel Then
                Dim link As LinkLabel = DirectCast(control, LinkLabel)
                link.BackColor = SettingsSurface
                link.LinkColor = SettingsLink
                link.ActiveLinkColor = SettingsLink
                link.VisitedLinkColor = SettingsLink
            ElseIf TypeOf control Is Label Then
                ' Titles and descriptions already carry their semantic colour.
                If control IsNot modernSettingsTitle AndAlso control IsNot modernProductLabel Then
                    If Not modernSettingsRows.Exists(Function(row) row.Title Is control OrElse row.Description Is control) Then
                        control.ForeColor = SettingsMutedText
                    End If
                End If
            End If

            If control.HasChildren Then ApplyModernPaletteToTree(control)
        Next
    End Sub

    Private Sub ModernSystemPreferenceChanged(sender As Object, e As UserPreferenceChangedEventArgs)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        BeginInvoke(New MethodInvoker(
            Sub()
                If IsDisposed Then Return
                LoadSystemSettingsPalette()
                ApplySystemSettingsPalette()
            End Sub))
    End Sub

    Private Sub ModernSettingsFormClosed(sender As Object, e As FormClosedEventArgs)
        RemoveHandler SystemEvents.UserPreferenceChanged, AddressOf ModernSystemPreferenceChanged
    End Sub

    Private Sub ApplySettingsTitleBarTheme()
        If Not IsHandleCreated Then Return
        Try
            Dim enabled As Integer = If(settingsDarkMode, 1, 0)
            DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, enabled, Marshal.SizeOf(Of Integer)())
        Catch
            ' Older Windows builds may not support this DWM attribute.
        End Try
    End Sub

    Private Shared Function GetSettingsBrightness(color As Color) As Integer
        Return CInt((CInt(color.R) * 299 + CInt(color.G) * 587 + CInt(color.B) * 114) / 1000)
    End Function

    Private Shared Function BlendSettingsColor(baseColor As Color, overlay As Color, overlayAmount As Single) As Color
        Dim amount As Single = Math.Max(0.0F, Math.Min(1.0F, overlayAmount))
        Return Color.FromArgb(
            CInt(CSng(baseColor.R) + (CSng(overlay.R) - CSng(baseColor.R)) * amount),
            CInt(CSng(baseColor.G) + (CSng(overlay.G) - CSng(baseColor.G)) * amount),
            CInt(CSng(baseColor.B) + (CSng(overlay.B) - CSng(baseColor.B)) * amount))
    End Function

    Private Sub StyleModernSettingsControls()
        For Each group As GroupBox In New GroupBox() {grp_Background, grp_OnScreen, grp_Slideshow, grp_Panel, grp_Quality, grp_Video, grp_FileOps, grp_Integration, grp_Language}
            group.BackColor = SettingsSurface
            group.ForeColor = SettingsText
            group.Font = New Font("Segoe UI Semibold", 10.0F, FontStyle.Regular)
            group.FlatStyle = FlatStyle.Flat
            group.Padding = New Padding(16, 20, 16, 12)
            group.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            AddHandler group.Paint, AddressOf PaintModernSettingsCard
        Next

        Data_Grid_View.BackgroundColor = SettingsSurface
        Data_Grid_View.BorderStyle = BorderStyle.None
        Data_Grid_View.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        Data_Grid_View.GridColor = SettingsBorder
        Data_Grid_View.EnableHeadersVisualStyles = False
        Data_Grid_View.ColumnHeadersDefaultCellStyle.BackColor = BlendSettingsColor(SettingsSurface, SettingsCanvas, 0.7F)
        Data_Grid_View.ColumnHeadersDefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Regular)
        Data_Grid_View.DefaultCellStyle.BackColor = SettingsSurface
        Data_Grid_View.DefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.SelectionBackColor = BlendSettingsColor(SettingsAccent, SettingsSurface, 0.72F)
        Data_Grid_View.DefaultCellStyle.SelectionForeColor = SettingsText
        Data_Grid_View.RowTemplate.Height = 30
        Data_Grid_View.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right

        StyleModernSettingsControlTree(Tab_Page_1)
        StyleModernSettingsControlTree(Tab_Page_2)
        StyleModernSettingsControlTree(Tab_Page_3)
        StyleModernSettingsControlTree(Tab_Page_4)
        StyleModernSettingsControlTree(Tab_Page_5)
        StyleModernSettingsControlTree(modernTranslationPage)
        StyleModernSettingsControlTree(modernSftpPage)
        StyleModernSettingsControlTree(modernAboutPage)

        ' OCR is built in code and has its own inner tabs. Give that otherwise
        ' classic grey control the same light surface as the rest of Settings.
        If ocrInner IsNot Nothing Then
            ocrInner.BackColor = SettingsCanvas
            ocrInner.Appearance = TabAppearance.FlatButtons
            ocrTabTranslate.BackColor = SettingsSurface
            ocrTabTranslate.UseVisualStyleBackColor = False
            ocrTabRecognition.BackColor = SettingsSurface
            ocrTabRecognition.UseVisualStyleBackColor = False
        End If
    End Sub

    ''' <summary>Repaints the legacy GroupBox border as a quiet rounded settings card.
    ''' The child controls remain owned by the original GroupBox, so existing layout
    ''' and code-behind do not have to know that its visual chrome changed.</summary>
    Private Sub PaintModernSettingsCard(sender As Object, e As PaintEventArgs)
        Dim card As GroupBox = TryCast(sender, GroupBox)
        If card Is Nothing OrElse card.Width < 8 OrElse card.Height < 8 Then Return

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(1, 1, card.ClientSize.Width - 3, card.ClientSize.Height - 3)
        Using path As GraphicsPath = ModernRoundedRectangle(rect, 10)
            Using fill As New SolidBrush(SettingsSurface)
                e.Graphics.FillPath(fill, path)
            End Using
            Using border As New Pen(SettingsBorder)
                e.Graphics.DrawPath(border, path)
            End Using
        End Using

        Dim titleRect As New Rectangle(16, 7, Math.Max(0, card.ClientSize.Width - 32), 20)
        Using titleFont As New Font("Segoe UI Semibold", 10.0F, FontStyle.Regular)
            TextRenderer.DrawText(e.Graphics, card.Text, titleFont, titleRect, SettingsText,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
        End Using
    End Sub

    Private Shared Function ModernRoundedRectangle(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim d As Integer = Math.Min(Math.Min(radius * 2, rect.Width), rect.Height)
        Dim path As New GraphicsPath()
        If d <= 1 Then
            path.AddRectangle(rect)
            Return path
        End If

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Sub StyleModernSettingsControlTree(parent As Control)
        For Each control As Control In parent.Controls
            Dim button As Button = TryCast(control, Button)
            If button IsNot Nothing Then
                button.FlatStyle = FlatStyle.Flat
                button.FlatAppearance.BorderColor = SettingsBorder
                button.FlatAppearance.BorderSize = 1
                button.FlatAppearance.MouseOverBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.1F)
                button.FlatAppearance.MouseDownBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.18F)
                button.UseVisualStyleBackColor = False
                button.BackColor = SettingsSurface
                button.ForeColor = SettingsAccent
                button.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Regular)
            ElseIf TypeOf control Is ComboBox OrElse TypeOf control Is TextBox OrElse TypeOf control Is NumericUpDown Then
                control.BackColor = If(settingsDarkMode, Color.FromArgb(50, 52, 57), SystemColors.Window)
                control.ForeColor = SettingsText
                control.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            ElseIf TypeOf control Is CheckBox Then
                control.BackColor = SettingsSurface
                control.ForeColor = SettingsText
                control.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            ElseIf TypeOf control Is Label Then
                control.ForeColor = SettingsMutedText
                control.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            End If

            If control.HasChildren Then StyleModernSettingsControlTree(control)
        Next
    End Sub

    Private Sub LayoutModernSettingsPages()
        If Not modernSettingsBuilt OrElse Tab_Control Is Nothing Then Return
        For Each flow As FlowLayoutPanel In modernPageFlows
            ResizeModernPageItems(flow)
        Next
    End Sub
End Class
#End If
