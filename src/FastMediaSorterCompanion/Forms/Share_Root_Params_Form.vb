Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "Параметры ресурса" / "Resource options" - the per-shared-root settings the
''' .fmscfg schema v2 export carries to the phone (Android contract S1002,
''' COMPANION_EXPORT_SPEC §9): resource name, type/profile, exact media set,
''' scan conditions, destination flag + color, comment, PIN and slideshow
''' interval. Opened from the
''' Share tab ("Настроить.." / double-click a folder row). Built fully in code;
''' modal. Edits a copy - the caller persists <see cref="Result"/> on OK only.
''' Everything left at its default is simply not exported (the file stays v1).
''' </summary>
Public Class Share_Root_Params_Form
    Inherits Form

    ''' <summary>§3.1 profile tokens, index-aligned with the combo items.</summary>
    Private Shared ReadOnly ProfileTokens As String() =
        {"none", "audio_library", "video_library", "photo_storage", "documents", "all_files"}

    ''' <summary>§3.2 media-type tokens, index-aligned with the checked list items.</summary>
    Private Shared ReadOnly MediaTokens As String() =
        {"image", "video", "audio", "gif", "text", "pdf", "epub", "office"}

    Private ReadOnly _folderName As String
    Private ReadOnly _params As ShareRootParams

    Private txtLabel As TextBox
    Private cmbProfile As ComboBox
    Private clbMedia As CheckedListBox
    Private chkScanSub As CheckBox
    Private chkSubItems As CheckBox
    Private chkHidden As CheckBox
    Private chkAllFiles As CheckBox
    Private chkReadOnly As CheckBox
    Private chkSoftReadOnly As CheckBox
    Private chkDestination As CheckBox
    Private lblDestNote As Label

    ''' <summary>Guards the two-way sync between "read-only" and "destination"
    ''' (a destination is inherently writable, so the two are mutually exclusive) so
    ''' setting one checkbox's state from the other's handler does not recurse.</summary>
    Private _syncingWritability As Boolean
    Private btnColor As Button
    Private pnlColor As Panel
    Private lnkColorReset As LinkLabel
    Private txtComment As TextBox
    Private txtPin As TextBox
    Private numSlide As NumericUpDown
    Private btnOk As Button
    Private btnCancel As Button

    ''' <summary>The edited params (valid after ShowDialog returned OK).</summary>
    Public ReadOnly Property Result As ShareRootParams
        Get
            Return _params
        End Get
    End Property

    Public Sub New(folderName As String, current As ShareRootParams)
        _folderName = If(folderName, "")
        _params = If(current, New ShareRootParams()).Clone()
        BuildUi()
        LoadParams()
    End Sub

    Private Sub BuildUi()
        Dim rus As Boolean = Is_Russian_Language

        Me.Text = (If(rus, "Параметры ресурса - ", "Resource options - ")) & _folderName
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(432, 560)
        Me.Font = New Font("Segoe UI", 9.0F)

        Dim tip As New ToolTip()

        ' 1. Resource name (highest priority, §9).
        Controls.Add(New Label With {.Left = 12, .Top = 16, .Width = 180, .Height = 16,
            .Text = If(rus, "Название на телефоне:", "Name on the phone:")})
        txtLabel = New TextBox With {.Left = 196, .Top = 12, .Width = 222}
        Controls.Add(txtLabel)
        tip.SetToolTip(txtLabel, If(rus, "Наименование ресурса в приложении. Пусто = имя папки.",
                                        "Resource name in the app. Empty = the folder name."))

        ' 2. Type / profile (highest priority, §9).
        Controls.Add(New Label With {.Left = 12, .Top = 46, .Width = 180, .Height = 16,
            .Text = If(rus, "Тип ресурса:", "Resource type:")})
        cmbProfile = New ComboBox With {.Left = 196, .Top = 42, .Width = 222, .DropDownStyle = ComboBoxStyle.DropDownList}
        If rus Then
            cmbProfile.Items.AddRange(New Object() {"Обычная папка (по умолчанию)", "Аудиотека", "Видеотека",
                "Фотохранилище", "Документы", "Все файлы"})
        Else
            cmbProfile.Items.AddRange(New Object() {"Regular folder (default)", "Audio library", "Video library",
                "Photo storage", "Documents", "All files"})
        End If
        Controls.Add(cmbProfile)
        tip.SetToolTip(cmbProfile, If(rus, "Готовый набор типов медиа и настроек (например, аудиотека).",
                                          "A preset of media types and settings (e.g. audio library)."))

        ' 3. Exact media set (advanced override of the profile).
        Controls.Add(New Label With {.Left = 12, .Top = 74, .Width = 180, .Height = 16,
            .Text = If(rus, "Точный набор типов:", "Exact media set:")})
        Controls.Add(New Label With {.Left = 12, .Top = 92, .Width = 180, .Height = 44, .ForeColor = Color.DimGray,
            .Text = If(rus, "Необязательно. Пусто = решает тип ресурса.", "Optional. Empty = the type decides.")})
        ' 4 rows tall so the 8 tokens fit in two 106 px columns with NO horizontal
        ' scrollbar (a shorter box hides half the tokens behind a tiny scrollbar).
        clbMedia = New CheckedListBox With {.Left = 196, .Top = 72, .Width = 222, .Height = 80,
            .MultiColumn = True, .ColumnWidth = 106, .CheckOnClick = True, .IntegralHeight = False}
        If rus Then
            clbMedia.Items.AddRange(New Object() {"Изображения", "Видео", "Аудио", "GIF", "Текст", "PDF", "EPUB", "Office"})
        Else
            clbMedia.Items.AddRange(New Object() {"Images", "Video", "Audio", "GIF", "Text", "PDF", "EPUB", "Office"})
        End If
        Controls.Add(clbMedia)

        ' 4. Scan conditions.
        Controls.Add(New Label With {.Left = 12, .Top = 166, .Width = 180, .Height = 16,
            .Text = If(rus, "Условия сканирования:", "Scan conditions:")})
        chkScanSub = New CheckBox With {.Left = 196, .Top = 162, .Width = 222, .Height = 20,
            .Text = If(rus, "Сканировать подпапки", "Scan subfolders"), .Checked = True}
        chkSubItems = New CheckBox With {.Left = 196, .Top = 184, .Width = 222, .Height = 20,
            .Text = If(rus, "Подпапки как элементы", "Subfolders as items")}
        chkHidden = New CheckBox With {.Left = 196, .Top = 206, .Width = 222, .Height = 20,
            .Text = If(rus, "Показывать скрытые файлы", "Show hidden files")}
        chkAllFiles = New CheckBox With {.Left = 196, .Top = 228, .Width = 222, .Height = 20,
            .Text = If(rus, "Все файлы (не только медиа)", "All files (not only media)")}
        AddHandler chkScanSub.CheckedChanged, Sub() chkSubItems.Enabled = chkScanSub.Checked
        Controls.AddRange(New Control() {chkScanSub, chkSubItems, chkHidden, chkAllFiles})

        ' 5a. HARD read-only - the SFTP SERVER blocks writes (§4.5.4 п.1). Default off;
        ' a destination forces it off. Enforced, not just advertised.
        chkReadOnly = New CheckBox With {.Left = 12, .Top = 254, .Width = 406, .Height = 20,
            .Text = If(rus, "Недоступно для записи на уровне сервера",
                           "Not writable at the server level"), .Checked = False}
        AddHandler chkReadOnly.CheckedChanged, AddressOf OnReadOnlyToggled
        Controls.Add(chkReadOnly)
        tip.SetToolTip(chkReadOnly, If(rus, "Сервер физически запрещает телефону менять файлы в этой папке (загрузка, переименование, удаление). Папка-получатель всегда доступна на запись.",
                                            "The server physically blocks the phone from changing files here (upload, rename, delete). A destination folder is always writable."))

        ' 5b. SOFT read-only - a CLIENT HINT only (§4.5.4 п.2): the phone shows the
        ' resource read-only, but the server is not locked (safe direction).
        chkSoftReadOnly = New CheckBox With {.Left = 12, .Top = 276, .Width = 406, .Height = 20,
            .Text = If(rus, "Публиковать в режиме «только чтение» (подсказка приложению)",
                           "Publish as read-only (client hint)")}
        Controls.Add(chkSoftReadOnly)
        tip.SetToolTip(chkSoftReadOnly, If(rus, "Телефону сообщается, что ресурс только для чтения, но сам сервер запись не блокирует. Полезно, чтобы приложение не показывало кнопки изменения. Для настоящего запрета включите «Недоступно для записи на уровне сервера».",
                                                "The phone is told the resource is read-only, but the server itself does not block writes. Handy so the app hides edit actions. For a real lock, tick 'Not writable at the server level'."))

        ' 5c. Destination (+ optional chip color).
        chkDestination = New CheckBox With {.Left = 12, .Top = 300, .Width = 406, .Height = 20,
            .Text = If(rus, "Папка-получатель (копирование и перемещение с телефона)",
                           "Destination folder (copy and move from the phone)")}
        lblDestNote = New Label With {.Left = 30, .Top = 322, .Width = 388, .Height = 16, .ForeColor = Color.DimGray,
            .Text = If(rus, "Общая папка станет доступна на запись.", "The shared folder becomes writable.")}
        btnColor = New Button With {.Left = 30, .Top = 342, .Width = 110, .Height = 24,
            .Text = If(rus, "Цвет метки..", "Chip color..")}
        pnlColor = New Panel With {.Left = 146, .Top = 344, .Width = 40, .Height = 20, .BorderStyle = BorderStyle.FixedSingle}
        lnkColorReset = New LinkLabel With {.Left = 194, .Top = 346, .Width = 160, .Height = 16,
            .Text = If(rus, "авто (задаст приложение)", "auto (app decides)")}
        AddHandler chkDestination.CheckedChanged, AddressOf OnDestinationToggled
        AddHandler btnColor.Click, AddressOf OnPickColor
        AddHandler lnkColorReset.LinkClicked, AddressOf OnResetColor
        Controls.AddRange(New Control() {chkDestination, lblDestNote, btnColor, pnlColor, lnkColorReset})
        tip.SetToolTip(chkDestination, If(rus, "Ресурс попадёт в получатели копирования/перемещения. Цвет метки - как пожелание: приложение может назначить свой.",
                                              "The resource is registered as a copy/move destination. The chip color is best-effort: the app may assign its own."))

        ' 6. Comment.
        Controls.Add(New Label With {.Left = 12, .Top = 378, .Width = 180, .Height = 16,
            .Text = If(rus, "Комментарий:", "Comment:")})
        txtComment = New TextBox With {.Left = 196, .Top = 374, .Width = 222}
        Controls.Add(txtComment)

        ' 7. PIN + the §6 safeguard.
        Controls.Add(New Label With {.Left = 12, .Top = 408, .Width = 180, .Height = 16,
            .Text = If(rus, "PIN для ресурса:", "Resource PIN:")})
        txtPin = New TextBox With {.Left = 196, .Top = 404, .Width = 100}
        Controls.Add(txtPin)
        tip.SetToolTip(txtPin, If(rus, "PIN хранится в файле и QR-коде открытым текстом - его увидит любой получатель файла.",
                                      "The PIN travels in the file and QR code in plaintext - anyone who gets the file sees it."))

        ' 8. Slideshow interval.
        Controls.Add(New Label With {.Left = 12, .Top = 462, .Width = 180, .Height = 16,
            .Text = If(rus, "Слайд-шоу, секунд:", "Slideshow, seconds:")})
        numSlide = New NumericUpDown With {.Left = 196, .Top = 458, .Width = 70, .Minimum = 1, .Maximum = 3600, .Value = 10}
        Controls.Add(numSlide)

        ' OK / Cancel.
        btnOk = New Button With {.Left = 248, .Top = 514, .Width = 82, .Height = 28, .Text = "OK"}
        btnCancel = New Button With {.Left = 336, .Top = 514, .Width = 82, .Height = 28,
            .Text = If(rus, "Отмена", "Cancel"), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf OnOk
        Controls.Add(btnOk)
        Controls.Add(btnCancel)
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

    ' --- load / save -----------------------------------------------------------

    Private Sub LoadParams()
        txtLabel.Text = If(_params.Label.Trim().Length > 0, _params.Label, _folderName)

        Dim profIdx As Integer = Array.IndexOf(ProfileTokens, If(_params.Profile, "none"))
        cmbProfile.SelectedIndex = If(profIdx >= 0, profIdx, 0)

        If _params.MediaTypes IsNot Nothing Then
            For Each t As String In _params.MediaTypes
                Dim i As Integer = Array.IndexOf(MediaTokens, t)
                If i >= 0 Then clbMedia.SetItemChecked(i, True)
            Next
        End If

        chkScanSub.Checked = _params.ScanSubdirectories
        chkSubItems.Checked = _params.ShowSubfoldersAsItems
        chkSubItems.Enabled = chkScanSub.Checked
        chkHidden.Checked = _params.ShowHiddenFiles
        chkAllFiles.Checked = _params.AllFiles

        ' Effective writability: read-only unless explicitly writable or a destination
        ' (the latter also normalizes a legacy destination whose IsReadOnly stayed True).
        Dim writable As Boolean = _params.IsWritable()
        _syncingWritability = True
        chkReadOnly.Checked = Not writable
        chkDestination.Checked = _params.IsDestination
        _syncingWritability = False
        chkSoftReadOnly.Checked = _params.SoftReadOnly
        UpdateColorUi()

        txtComment.Text = _params.Comment
        txtPin.Text = _params.AccessPin

        If _params.SlideshowInterval >= numSlide.Minimum AndAlso _params.SlideshowInterval <= numSlide.Maximum Then
            numSlide.Value = _params.SlideshowInterval
        End If
        UpdateDestinationEnabled()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' "" when the name was left as the folder name, so a later folder rename
        ' is not shadowed by a stale stored label.
        Dim label As String = txtLabel.Text.Trim()
        _params.Label = If(label.Length = 0 OrElse label = _folderName, "", label)

        Dim profIdx As Integer = cmbProfile.SelectedIndex
        _params.Profile = ProfileTokens(If(profIdx >= 0, profIdx, 0))

        Dim media As New List(Of String)()
        For i As Integer = 0 To MediaTokens.Length - 1
            If clbMedia.GetItemChecked(i) Then media.Add(MediaTokens(i))
        Next
        _params.MediaTypes = media

        _params.ScanSubdirectories = chkScanSub.Checked
        _params.ShowSubfoldersAsItems = chkSubItems.Checked
        _params.ShowHiddenFiles = chkHidden.Checked
        _params.AllFiles = chkAllFiles.Checked
        _params.IsReadOnly = chkReadOnly.Checked
        _params.SoftReadOnly = chkSoftReadOnly.Checked
        _params.IsDestination = chkDestination.Checked
        ' HasDestinationColor / DestinationColorArgb are maintained by the color
        ' picker handlers; a non-destination root drops the color entirely.
        If Not _params.IsDestination Then _params.HasDestinationColor = False

        _params.Comment = txtComment.Text.Trim()
        _params.AccessPin = txtPin.Text.Trim()
        _params.SlideshowInterval = CInt(numSlide.Value)

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    ' --- destination color -------------------------------------------------------

    ''' <summary>Read-only and destination are mutually exclusive (a destination is
    ''' writable). Checking read-only clears the destination.</summary>
    Private Sub OnReadOnlyToggled(sender As Object, e As EventArgs)
        If _syncingWritability Then Return
        _syncingWritability = True
        If chkReadOnly.Checked Then chkDestination.Checked = False
        _syncingWritability = False
        UpdateDestinationEnabled()
    End Sub

    ''' <summary>Enabling a destination clears read-only (the folder must accept
    ''' writes) and enables the chip-color controls.</summary>
    Private Sub OnDestinationToggled(sender As Object, e As EventArgs)
        If Not _syncingWritability Then
            _syncingWritability = True
            If chkDestination.Checked Then chkReadOnly.Checked = False
            _syncingWritability = False
        End If
        UpdateDestinationEnabled()
    End Sub

    Private Sub UpdateDestinationEnabled()
        Dim dest As Boolean = chkDestination.Checked
        lblDestNote.Enabled = dest
        btnColor.Enabled = dest
        pnlColor.Enabled = dest
        lnkColorReset.Enabled = dest AndAlso _params.HasDestinationColor
    End Sub

    Private Sub OnPickColor(sender As Object, e As EventArgs)
        Using dlg As New ColorDialog()
            If _params.HasDestinationColor Then dlg.Color = Color.FromArgb(_params.DestinationColorArgb)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ' Force full alpha: the contract expects an opaque ARGB chip color.
            _params.DestinationColorArgb = Color.FromArgb(255, dlg.Color).ToArgb()
            _params.HasDestinationColor = True
        End Using
        UpdateColorUi()
    End Sub

    Private Sub OnResetColor(sender As Object, e As LinkLabelLinkClickedEventArgs)
        _params.HasDestinationColor = False
        UpdateColorUi()
    End Sub

    Private Sub UpdateColorUi()
        Dim rus As Boolean = Is_Russian_Language
        If _params.HasDestinationColor Then
            pnlColor.BackColor = Color.FromArgb(_params.DestinationColorArgb)
            lnkColorReset.Text = If(rus, "сбросить (авто)", "reset (auto)")
        Else
            pnlColor.BackColor = SystemColors.Control
            lnkColorReset.Text = If(rus, "авто (задаст приложение)", "auto (app decides)")
        End If
        lnkColorReset.Enabled = chkDestination.Checked AndAlso _params.HasDestinationColor
    End Sub

End Class
