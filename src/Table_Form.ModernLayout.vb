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

        For Each page As TabPage In New TabPage() {Tab_Page_1, Tab_Page_2, Tab_Page_3, Tab_Page_4, Tab_Page_5, modernSftpPage, modernAboutPage}
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
                                     "OCR & translation", "Android / SFTP", "About"}
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
        modernSftpPage = New TabPage With {.Name = "Tab_Page_Sftp", .Text = "Android / SFTP"}
        modernAboutPage = New TabPage With {.Name = "Tab_Page_About", .Text = "About"}
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
        RefreshModernSettingsHeader()
    End Sub

    Private Sub ModernSettingsContentResized(sender As Object, e As EventArgs)
        LayoutModernSettingsPages()
    End Sub

    ''' <summary>Called by PrepareForDisplay after the existing localization code.</summary>
    Private Sub LocalizeModernSettingsLayout()
        If Not modernSettingsBuilt Then Return

        Dim ru As Boolean = Is_Russian_Language
        Dim navLabels As String() = If(ru,
            {"Получатели", "Просмотр", "Видео", "Файлы", "OCR и перевод", "Android / SFTP", "О программе"},
            {"Destinations", "Viewing", "Video", "Files", "OCR & translation", "Android / SFTP", "About"})
        For index As Integer = 0 To modernSettingsNavButtons.Length - 1
            modernSettingsNavButtons(index).Text = navLabels(index)
        Next

        LinkLabel1.Text = ShortSettingsVersion()
        modernSidebarSectionLabel.Text = If(ru, "Настройки", "Settings")
        LocalizeModernSettingRows()
        RefreshModernSettingsHeader()
    End Sub

    Private Sub RefreshModernSettingsHeader()
        If Not modernSettingsBuilt Then Return
        Dim ru As Boolean = Is_Russian_Language
        Dim index As Integer = Math.Max(0, Tab_Control.SelectedIndex)
        Dim titles As String() = If(ru,
            {"Каталоги-получатели", "Просмотр", "Видео и качество", "Файлы и система", "OCR и перевод", "Android и SFTP", "О программе"},
            {"Destination folders", "Viewing", "Video and quality", "Files and system", "OCR & translation", "Android & SFTP", "About"})
        Dim subtitles As String() = If(ru,
            {"Назначьте папки для быстрого перемещения и копирования.",
             "Настройте фон, информацию на экране и слайдшоу.",
             "Качество изображения и привычное поведение видео.",
             "Операции с файлами, интеграция и язык интерфейса.",
             "Распознавайте текст на изображениях и переводите его.",
             "SFTP-сервер и мобильное приложение Android.",
             "Версия приложения, документация и ссылки проекта."},
            {"Assign folders for quick moving and copying.",
             "Tune the background, on-screen information and slideshow.",
             "Image quality and familiar video behaviour.",
             "File operations, integration and interface language.",
             "Recognize text on images and translate it.",
             "SFTP server and the Android mobile app.",
             "Application version, documentation and project links."})

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
                    If button IsNot Nothing Then button.Text = If(ru, "Открыть параметры Windows", "Open Windows settings")
                Case "sftp_manager"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = If(ru, "Управление SFTP", "Manage SFTP")
                Case "ocr_server"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = If(ru, "Установить", "Install")
                    If buttons.Count > 1 Then buttons(1).Text = If(ru, "Запустить", "Start")
                Case "ocr_model"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = If(ru, "Загрузить", "Download")
            End Select
            For Each link As LinkLabel In FindModernControls(Of LinkLabel)(row.Host)
                If link IsNot row.Title Then link.Text = If(ru, "Открыть", "Open")
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
            Case "recipients_overlay" : Return If(ru, "Таблица получателей поверх изображения", "Destination table over the image")
            Case "section_overlay_layout" : Return If(ru, "Вид таблицы получателей", "Destination table appearance")
            Case "recipients_position" : Return If(ru, "Положение таблицы", "Table position")
            Case "recipients_width" : Return If(ru, "Ширина таблицы", "Table width")
            Case "recipients_font" : Return If(ru, "Размер текста", "Text size")
            Case "recipients_opacity" : Return If(ru, "Непрозрачность", "Opacity")
            Case "recipients_rows" : Return If(ru, "Видимые строки", "Visible rows")
            Case "section_background" : Return If(ru, "Фон изображения", "Image background")
            Case "background_color" : Return If(ru, "Цвет фона", "Background colour")
            Case "perspective" : Return If(ru, "Перспективный фон", "Perspective background")
            Case "dynamic_perspective" : Return If(ru, "Динамический ореол", "Dynamic halo")
            Case "animated_perspective" : Return If(ru, "Анимация ореола", "Halo animation")
            Case "section_information" : Return If(ru, "Информация и управление", "Information and controls")
            Case "show_picture_size" : Return If(ru, "Размеры изображения", "Image dimensions")
            Case "show_file_size" : Return If(ru, "Размер файла", "File size")
            Case "show_file_date" : Return If(ru, "Дата изменения файла", "File modification date")
            Case "show_info_overlay" : Return If(ru, "Информация поверх изображения", "Information over the image")
            Case "wheel_zooms" : Return If(ru, "Масштабирование колёсиком", "Zoom with the mouse wheel")
            Case "slideshow_interval" : Return If(ru, "Интервал слайдшоу", "Slideshow interval")
            Case "thumbnail_size" : Return If(ru, "Размер карточки изображения", "Image card size")
            Case "section_accessibility" : Return If(ru, "Комфорт просмотра", "Viewing comfort")
            Case "new_image_scale" : Return If(ru, "Масштаб нового изображения", "New image scale")
            Case "reduce_motion" : Return If(ru, "Уменьшать анимацию", "Reduce motion")
            Case "section_slideshow_behavior" : Return If(ru, "Поведение слайд-шоу", "Slideshow behaviour")
            Case "slideshow_random_order" : Return If(ru, "Порядок показа", "Playback order")
            Case "stop_slideshow_manual" : Return If(ru, "Останавливать при ручной навигации", "Stop on manual navigation")
            Case "slideshow_ui" : Return If(ru, "Интерфейс во время слайд-шоу", "UI during slideshow")
            Case "exif_rotate" : Return If(ru, "Автоповорот по EXIF", "EXIF auto-rotation")
            Case "hq_scaling" : Return If(ru, "Качественное масштабирование", "High-quality scaling")
            Case "video_loop" : Return If(ru, "Повторять видео", "Loop video")
            Case "video_mute" : Return If(ru, "Запускать без звука", "Start muted")
            Case "video_volume" : Return If(ru, "Громкость видео", "Video volume")
            Case "section_video_behavior" : Return If(ru, "Поведение видео", "Video behaviour")
            Case "video_autoplay" : Return If(ru, "Запускать видео автоматически", "Autoplay video")
            Case "video_controls_delay" : Return If(ru, "Задержка скрытия панели, с", "Controls hide delay, s")
            Case "video_controls_paused" : Return If(ru, "Показывать панель при паузе", "Show controls while paused")
            Case "video_click_action" : Return If(ru, "Одиночный клик по видео", "Single click on video")
            Case "video_end_action" : Return If(ru, "После окончания видео", "After video ends")
            Case "preferred_audio_language" : Return If(ru, "Предпочтительный язык звука", "Preferred audio language")
            Case "preferred_subtitle_language" : Return If(ru, "Предпочтительный язык субтитров", "Preferred subtitle language")
            Case "copy_mode" : Return If(ru, "Копировать вместо перемещения", "Copy instead of moving")
            Case "no_confirmation" : Return If(ru, "Не запрашивать подтверждение", "Do not ask for confirmation")
            Case "image_associations" : Return If(ru, "Форматы изображений", "Image file types")
            Case "video_associations" : Return If(ru, "Форматы видео", "Video file types")
            Case "interface_language" : Return If(ru, "Язык интерфейса", "Interface language")
            Case "section_file_behavior" : Return If(ru, "Поведение файлов", "File behaviour")
            Case "name_collision" : Return If(ru, "Совпадение имён", "Name collision")
            Case "after_file_operation" : Return If(ru, "После копирования или перемещения", "After copying or moving")
            Case "include_subfolders" : Return If(ru, "Просматривать вложенные папки", "Include subfolders")
            Case "included_extensions" : Return If(ru, "Типы файлов", "File types")
            Case "recent_files_limit" : Return If(ru, "Размер истории файлов", "Recent-files limit")
            Case "recent_folders_limit" : Return If(ru, "Размер истории папок", "Recent-folders limit")
            Case "startup_open" : Return If(ru, "При запуске открывать", "Open at startup")
            Case "ocr_enabled" : Return If(ru, "Включить OCR", "Enable OCR")
            Case "ocr_auto" : Return If(ru, "Запускать автоматически", "Run automatically")
            Case "section_translation" : Return If(ru, "Перевод", "Translation")
            Case "ocr_provider" : Return If(ru, "Сервис перевода", "Translation provider")
            Case "ocr_endpoint" : Return If(ru, "Адрес сервиса", "Service address")
            Case "ocr_server" : Return If(ru, "Локальный сервер Ollama", "Local Ollama server")
            Case "ocr_model" : Return If(ru, "Модель перевода", "Translation model")
            Case "ocr_api" : Return If(ru, "API-ключ", "API key")
            Case "ocr_target" : Return If(ru, "Язык перевода", "Translation language")
            Case "section_recognition" : Return If(ru, "Распознавание текста", "Text recognition")
            Case "ocr_source" : Return If(ru, "Язык исходного текста", "Source text language")
            Case "ocr_quality" : Return If(ru, "Качество распознавания", "Recognition quality")
            Case "ocr_mode" : Return If(ru, "Режим OCR", "OCR mode")
            Case "ocr_download" : Return If(ru, "Языковые данные", "Language data")
            Case "section_overlay" : Return If(ru, "Панель перевода", "Translation panel")
            Case "ocr_opacity" : Return If(ru, "Непрозрачность панели", "Panel opacity")
            Case "ocr_overlay_visible" : Return If(ru, "Показывать панель перевода", "Show translation overlay")
            Case "ocr_disk_cache" : Return If(ru, "Кэшировать результаты на диске", "Cache results on disk")
            Case "ocr_cache_limit" : Return If(ru, "Максимальный размер OCR-кэша, МБ", "Maximum OCR cache size, MB")
            Case "sftp_intro" : Return If(ru, "Доступ к медиатеке с телефона", "Access your media library from a phone")
            Case "sftp_manager" : Return If(ru, "Публикация папок по SFTP", "Publish folders over SFTP")
            Case "sftp_guide" : Return If(ru, "Инструкция по подключению", "Connection guide")
            Case "android_app" : Return If(ru, "Приложение для Android", "Android application")
            Case "about_intro" : Return "Fast Media Sorter Lite"
            Case "doc_html_intro" : Return "Doc HTML Translate"
            Case "doc_html_site" : Return If(ru, "Сайт Doc HTML Translate", "Doc HTML Translate website")
            Case "project_site" : Return If(ru, "Сайт проекта", "Project website")
            Case "project_github" : Return "GitHub"
            Case "project_releases" : Return If(ru, "Новые версии", "Releases")
            Case "project_privacy" : Return If(ru, "Политика конфиденциальности", "Privacy policy")
            Case "project_email" : Return If(ru, "Связаться с автором", "Contact the author")
            Case Else : Return key
        End Select
    End Function

    Private Function ModernSettingDescription(key As String, ru As Boolean) As String
        Select Case key
            Case "recipients_overlay" : Return If(ru, "Показывает компактный список папок в левом верхнем углу просмотрщика.", "Shows a compact folder list in the viewer's upper-left corner.")
            Case "recipients_position" : Return If(ru, "Угол области просмотра, в котором будет показана таблица.", "Viewer corner where the table is shown.")
            Case "recipients_width" : Return If(ru, "Ширина панели в пикселях.", "Panel width in pixels.")
            Case "recipients_font" : Return If(ru, "Размер текста в пунктах.", "Text size in points.")
            Case "recipients_opacity" : Return If(ru, "Прозрачность фона таблицы в процентах.", "Table background opacity in percent.")
            Case "recipients_rows" : Return If(ru, "Лишние строки будут доступны прокруткой.", "Additional rows remain available by scrolling.")
            Case "background_color" : Return If(ru, "Выберите, как вычислять цвет свободной области вокруг фотографии.", "Choose how the empty area around a photo is coloured.")
            Case "perspective" : Return If(ru, "Продолжает края фотографии на свободную область экрана.", "Extends the photo edges into the empty screen area.")
            Case "dynamic_perspective" : Return If(ru, "Плавно смешивает продолжение изображения с выбранным фоном.", "Smoothly blends the extended image into the selected background.")
            Case "animated_perspective" : Return If(ru, "Проявляет ореол короткой анимацией при открытии следующего файла.", "Reveals the halo with a short animation when the next file opens.")
            Case "show_picture_size" : Return If(ru, "Показывает ширину и высоту текущего изображения.", "Shows the width and height of the current image.")
            Case "show_file_size" : Return If(ru, "Добавляет размер текущего файла в информационную строку.", "Adds the current file size to the information line.")
            Case "show_file_date" : Return If(ru, "Добавляет дату и время последнего изменения файла.", "Adds the file's last modification date and time.")
            Case "show_info_overlay" : Return If(ru, "Показывает имя файла и позицию в списке прямо на изображении.", "Shows the file name and list position directly over the image.")
            Case "wheel_zooms" : Return If(ru, "Колесо меняет масштаб; при отключении оно листает файлы.", "The wheel changes zoom; when disabled it navigates between files.")
            Case "slideshow_interval" : Return If(ru, "Базовая пауза между изображениями, в секундах.", "Base delay between images, in seconds.")
            Case "thumbnail_size" : Return If(ru, "Размер карточек в панели предварительного просмотра.", "Size of cards in the preview panel.")
            Case "new_image_scale" : Return If(ru, "Начальный масштаб при открытии следующего изображения.", "Initial scale when opening the next image.")
            Case "reduce_motion" : Return If(ru, "Отключает декоративные переходы и анимацию ореола.", "Disables decorative transitions and halo animation.")
            Case "slideshow_random_order" : Return If(ru, "Выберите обычный, случайный или перемешанный порядок.", "Choose normal, random, or shuffled order.")
            Case "stop_slideshow_manual" : Return If(ru, "Ручной переход останавливает таймер слайд-шоу.", "Manual navigation stops the slideshow timer.")
            Case "slideshow_ui" : Return If(ru, "Управление и статус можно временно скрывать для просмотра.", "Controls and status can be hidden temporarily while viewing.")
            Case "exif_rotate" : Return If(ru, "Учитывает ориентацию, записанную камерой или телефоном.", "Uses the orientation stored by the camera or phone.")
            Case "hq_scaling" : Return If(ru, "Делает уменьшенные изображения резче, используя качественную интерполяцию.", "Makes downscaled images sharper using high-quality interpolation.")
            Case "video_loop" : Return If(ru, "После окончания видео запускается снова.", "Restarts a video after it reaches the end.")
            Case "video_mute" : Return If(ru, "Каждое видео начинает воспроизводиться с выключенным звуком.", "Every video starts playing with sound muted.")
            Case "video_volume" : Return If(ru, "Начальная громкость воспроизведения, от 0 до 100 %.", "Initial playback volume, from 0 to 100%.")
            Case "video_autoplay" : Return If(ru, "Если выключено, видео открывается на паузе.", "When off, a video opens paused.")
            Case "video_controls_delay" : Return If(ru, "Через сколько секунд бездействия скрывать панель управления.", "Seconds of inactivity before controls hide.")
            Case "video_controls_paused" : Return If(ru, "Не скрывает управление, пока видео поставлено на паузу.", "Keeps controls visible while video is paused.")
            Case "video_click_action" : Return If(ru, "Действие левой кнопки мыши на поверхности видео.", "Left-click action on the video surface.")
            Case "video_end_action" : Return If(ru, "Что делать, когда воспроизведение достигло конца.", "What to do when playback reaches the end.")
            Case "preferred_audio_language", "preferred_subtitle_language" : Return If(ru, "Код языка, например ru, en или rus. Оставьте пустым для выбора плеера.", "Language code such as ru, en, or rus. Leave blank for player choice.")
            Case "copy_mode" : Return If(ru, "Исходные файлы сохраняются, а в папке-получателе создаются копии.", "Keeps source files and creates copies in the destination folder.")
            Case "no_confirmation" : Return If(ru, "Файловые операции выполняются сразу. Используйте осторожно.", "File operations run immediately. Use with care.")
            Case "image_associations" : Return If(ru, "Открывает системные параметры приложений по умолчанию для изображений.", "Opens system default-app settings for image formats.")
            Case "video_associations" : Return If(ru, "Открывает системные параметры приложений по умолчанию для видео.", "Opens system default-app settings for video formats.")
            Case "interface_language" : Return If(ru, "Переключает интерфейс приложения на английский язык.", "Switches the application interface to Russian.")
            Case "name_collision" : Return If(ru, "Что делать, если в папке-получателе уже есть файл с тем же именем.", "What to do when the destination already has the same file name.")
            Case "after_file_operation" : Return If(ru, "Выберите, что показывать после успешной операции.", "Choose what to show after a successful operation.")
            Case "include_subfolders" : Return If(ru, "Добавляет подходящие файлы из всех вложенных папок.", "Adds matching files from all nested folders.")
            Case "included_extensions" : Return If(ru, "Расширения через точку с запятой; пустое поле — все поддерживаемые.", "Semicolon-separated extensions; empty means all supported types.")
            Case "recent_files_limit", "recent_folders_limit" : Return If(ru, "0 отключает сохранение новых записей.", "0 stops storing new entries.")
            Case "startup_open" : Return If(ru, "Что будет показано при обычном запуске без файла в командной строке.", "What opens on a normal start without a command-line file.")
            Case "ocr_enabled" : Return If(ru, "Разрешает распознавание текста на открытых изображениях.", "Allows text recognition on open images.")
            Case "ocr_auto" : Return If(ru, "Распознаёт текст без отдельной команды после открытия изображения.", "Recognizes text without a separate command after an image opens.")
            Case "ocr_provider" : Return If(ru, "Выберите локальный или сетевой движок, который выполнит перевод.", "Choose the local or online engine that performs translation.")
            Case "ocr_endpoint" : Return If(ru, "URL API выбранного сервиса перевода.", "API URL of the selected translation service.")
            Case "ocr_server" : Return If(ru, "Установите или запустите Ollama для локального перевода.", "Install or start Ollama for local translation.")
            Case "ocr_model" : Return If(ru, "Модель Ollama; отсутствующую модель можно загрузить этой же строкой.", "Ollama model; a missing model can be downloaded from this row.")
            Case "ocr_api" : Return If(ru, "Ключ доступа нужен только сервисам, которые его требуют.", "An access key is only needed by providers that require one.")
            Case "ocr_target" : Return If(ru, "Язык, на который будет переведён распознанный текст.", "Language into which recognized text will be translated.")
            Case "ocr_source" : Return If(ru, "Язык надписей на изображении; автоопределение подходит большинству случаев.", "Language shown in the image; automatic detection suits most cases.")
            Case "ocr_quality" : Return If(ru, "Баланс между скоростью обработки и точностью результата.", "Balances processing speed against result accuracy.")
            Case "ocr_mode" : Return If(ru, "Выберите подходящий способ разметки текста на изображении.", "Choose a layout mode suitable for the text in the image.")
            Case "ocr_download" : Return If(ru, "Загрузите недостающие данные для выбранного языка OCR.", "Downloads missing OCR data for the selected language.")
            Case "ocr_opacity" : Return If(ru, "Непрозрачность фона перевода: от 30 до 100 %.", "Translation background opacity: from 30 to 100%.")
            Case "ocr_overlay_visible" : Return If(ru, "Показывает или скрывает уже распознанный перевод поверх изображения.", "Shows or hides the already recognized translation over the image.")
            Case "ocr_disk_cache" : Return If(ru, "Сохраняет распознанный текст, чтобы повторно не обрабатывать файл.", "Keeps recognized text so the file does not need processing again.")
            Case "ocr_cache_limit" : Return If(ru, "0 означает без ограничения; очистка по LRU выполняется после записи.", "0 means unlimited; LRU cleanup runs after writing.")
            Case "sftp_intro" : Return If(ru, "Опубликуйте выбранные папки встроенным SFTP-сервером и открывайте их в мобильном приложении.", "Publish selected folders with the built-in SFTP server and browse them in the mobile app.")
            Case "sftp_manager" : Return If(ru, "Открывает отдельное приложение управления SFTP-доступом и опубликованными папками.", "Opens the separate app for managing SFTP access and published folders.")
            Case "sftp_guide" : Return If(ru, "Пошаговая настройка сервера, сети и подключения мобильного клиента.", "Step-by-step setup for the server, network and mobile client.")
            Case "android_app" : Return If(ru, "Страница мобильного клиента Fast Media Sorter для Android.", "Fast Media Sorter mobile client page for Android.")
            Case "about_intro" : Return If(ru, "Современный просмотрщик и сортировщик фото и видео." & Environment.NewLine & "Версия " & ShortSettingsVersion(),
                                                   "A modern photo and video viewer and sorter." & Environment.NewLine & "Version " & ShortSettingsVersion())
            Case "doc_html_intro" : Return If(ru,
                                                   "Преобразует EPUB, PDF и другие документы в локальный HTML с оглавлением и переводом в браузере. Для изображений, сканов и комиксов создаёт переводимый OCR-слой.",
                                                   "Converts EPUB, PDF, and other documents to local HTML with a table of contents and browser translation. For images, scans, and comics it creates a translatable OCR layer.")
            Case "doc_html_site" : Return If(ru,
                                                  "Описание возможностей, поддерживаемых форматов, установки и работы с документами и изображениями.",
                                                  "Features, supported formats, installation, and workflows for documents and images.")
            Case "project_site" : Return If(ru, "Описание возможностей, инструкции и материалы проекта.", "Features, instructions and project resources.")
            Case "project_github" : Return If(ru, "Исходный код, задачи и техническая документация.", "Source code, issues and technical documentation.")
            Case "project_releases" : Return If(ru, "Список опубликованных версий и файлов для установки.", "Published versions and installation downloads.")
            Case "project_privacy" : Return If(ru, "Как приложение обрабатывает пользовательские данные.", "How the application handles user data.")
            Case "project_email" : Return If(ru, "Написать автору проекта по электронной почте.", "Send an email to the project author.")
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
        Dim gridHost As New Panel With {.Height = 382, .BackColor = SettingsSurface, .Padding = New Padding(12)}
        ReparentModernSettingsControl(lbl_Grid_Hint, gridHost)
        lbl_Grid_Hint.Dock = DockStyle.Bottom
        lbl_Grid_Hint.Height = 34
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
        AddSettingRow(files, "copy_mode", chkbox_Copy_Mode, 34, True)
        ' .NET 10 always uses the background file-operation queue. Do not expose a
        ' switch that cannot change anything; the hidden control remains for net48
        ' persistence compatibility.
        chkbox_Independent_Thread_For_File_Operation.Visible = False
        AddSettingRow(files, "no_confirmation", chkb_no_request_before_file_operation, 34, True)
        AddSettingRow(files, "image_associations", btn_Set_As_Default, 230, True)
        AddSettingRow(files, "video_associations", btn_Set_As_Default_Video, 230, True)
        AddSettingRow(files, "interface_language", btn_Language, 100, True)

        Dim ocr As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_5)
        ocrInner.Visible = False
        AddSettingRow(ocr, "ocr_enabled", chkOcrEnabled, 34, True)
        AddSettingRow(ocr, "ocr_auto", chkOcrAuto, 34, True)
        AddSectionHeader(ocr, "section_translation")
        AddSettingRow(ocr, "ocr_provider", cmbOcrProvider, 260, True)
        AddSettingRow(ocr, "ocr_endpoint", txtOcrEndpoint, 300, True)
        AddSettingRow(ocr, "ocr_server", MakeModernControlStrip({btnOcrInstallOllama, btnOcrStartOllama}, 310), 310, True)
        AddSettingRow(ocr, "ocr_model", MakeModernControlStrip({cmbOcrModelName, btnOcrPullModel}, 310), 310, True)
        AddSettingRow(ocr, "ocr_api", txtOcrApi, 300, True)
        AddSettingRow(ocr, "ocr_target", cmbOcrTarget, 300, True)
        AddSectionHeader(ocr, "section_recognition")
        AddSettingRow(ocr, "ocr_source", cmbOcrSource, 300, True)
        AddSettingRow(ocr, "ocr_quality", cmbOcrQuality, 300, True)
        AddSettingRow(ocr, "ocr_mode", cmbOcrMode, 300, True)
        AddSettingRow(ocr, "ocr_download", btnOcrDownload, 300, True)
        AddSectionHeader(ocr, "section_overlay")
        AddSettingRow(ocr, "ocr_opacity", MakeModernOpacityStrip(), 330, True)
        AddSettingRow(ocr, "ocr_overlay_visible", chkOcrOverlayVisible, 34, True)
        AddSettingRow(ocr, "ocr_disk_cache", chkOcrDisk, 34, True)
        ReparentModernSettingsControl(lblOcrStatus, ocr)
        lblOcrStatus.AutoSize = False
        lblOcrStatus.Height = 34
        lblOcrStatus.Margin = New Padding(8, 4, 8, 8)

        BuildModernSftpPage()
        BuildModernAboutPage()
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
        AddProjectLinkRow(flow, "android_app", If(Is_Russian_Language,
            "https://serzhyale.github.io/FastMediaSorter_mob_v2/index-ru.html",
            "https://serzhyale.github.io/FastMediaSorter_mob_v2/"))
    End Sub

    Private Sub BuildModernAboutPage()
        Dim flow As FlowLayoutPanel = CreateModernPageFlow(modernAboutPage)
        AddInformationBlock(flow, "about_intro")
        AddInformationBlock(flow, "doc_html_intro")
        AddProjectLinkRow(flow, "doc_html_site", "https://serzhyale.github.io/doc-html-translate/")
        AddProjectLinkRow(flow, "project_site", "https://serzhyale.github.io/FastMediaSorter_Lite/")
        AddProjectLinkRow(flow, "project_github", "https://github.com/SerZhyAle/FastMediaSorter_Lite")
        AddProjectLinkRow(flow, "project_releases", "https://github.com/SerZhyAle/FastMediaSorter_Lite/releases")
        AddProjectLinkRow(flow, "project_privacy", "https://serzhyale.github.io/FastMediaSorter_Lite/privacy.html")
        AddProjectLinkRow(flow, "project_email", "mailto:sza@ukr.net?subject=Fast Media Sorter for Windows")
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
