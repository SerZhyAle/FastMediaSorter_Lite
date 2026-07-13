Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "Параметры ресурса" - the per-shared-root settings the .fmscfg schema v2 export
''' carries to the phone: resource name, type/profile, exact media set, scan
''' conditions, the two read-only meanings (hard = server-enforced, soft = client
''' hint), destination flag, comment, PIN and slideshow interval. Opened right after
''' a folder is added, and from "Настроить.." / double-click. Modal; edits a copy -
''' the caller persists <see cref="Result"/> on OK only. Everything at its default is
''' not exported (the file stays v1). Each option carries a short "what it does" note.
''' The destination CHIP COLOUR is intentionally NOT offered here - the Android app
''' decides the colour, so a picker on our side only misleads.
''' </summary>
Public Class Share_Root_Params_Form
    Inherits Form

    Private Shared ReadOnly ProfileTokens As String() =
        {"none", "audio_library", "video_library", "photo_storage", "documents", "all_files"}

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
    Private _syncingWritability As Boolean
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

    Private Function Hint(x As Integer, y As Integer, w As Integer, text As String) As Label
        Return New Label With {.Left = x, .Top = y, .Width = w, .Height = 32, .ForeColor = Color.DimGray, .Text = text}
    End Function

    Private Sub BuildUi()
        Dim rus As Boolean = Is_Russian_Language

        Me.Text = (If(rus, "Параметры ресурса - ", "Resource options - ")) & _folderName
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.ClientSize = New Size(572, 646)
        Me.Font = New Font("Segoe UI", 10.0F)

        Dim tip As New ToolTip()
        Const lx As Integer = 18
        Const ix As Integer = 240
        Const iw As Integer = 314

        ' 1. Resource name.
        Controls.Add(New Label With {.Left = lx, .Top = 22, .Width = 214, .Height = 20,
            .Text = If(rus, "Название на телефоне:", "Name on the phone:")})
        txtLabel = New TextBox With {.Left = ix, .Top = 18, .Width = iw}
        Controls.Add(txtLabel)
        Controls.Add(Hint(ix, 44, iw, If(rus, "Как ресурс называется в приложении. Пусто = имя папки.",
                                              "The resource name in the app. Empty = the folder name.")))

        ' 2. Type / profile.
        Controls.Add(New Label With {.Left = lx, .Top = 76, .Width = 214, .Height = 20,
            .Text = If(rus, "Тип ресурса:", "Resource type:")})
        cmbProfile = New ComboBox With {.Left = ix, .Top = 72, .Width = iw, .DropDownStyle = ComboBoxStyle.DropDownList}
        If rus Then
            cmbProfile.Items.AddRange(New Object() {"Обычная папка (по умолчанию)", "Аудиотека", "Видеотека",
                "Фотохранилище", "Документы", "Все файлы"})
        Else
            cmbProfile.Items.AddRange(New Object() {"Regular folder (default)", "Audio library", "Video library",
                "Photo storage", "Documents", "All files"})
        End If
        Controls.Add(cmbProfile)
        Controls.Add(Hint(ix, 98, iw, If(rus, "Как приложение покажет папку и какие файлы возьмёт (напр. «Видеотека» - только видео).",
                                              "How the app shows the folder and which files it takes (e.g. Video library - videos only).")))

        ' 3. Exact media set.
        Controls.Add(New Label With {.Left = lx, .Top = 132, .Width = 214, .Height = 20,
            .Text = If(rus, "Точный набор типов:", "Exact media set:")})
        Controls.Add(Hint(lx, 154, 214, If(rus, "Необязательно. Переопределяет тип. Пусто = решает тип.",
                                                 "Optional. Overrides the type. Empty = the type decides.")))
        clbMedia = New CheckedListBox With {.Left = ix, .Top = 130, .Width = iw, .Height = 96,
            .MultiColumn = True, .ColumnWidth = 157, .CheckOnClick = True, .IntegralHeight = False}
        If rus Then
            clbMedia.Items.AddRange(New Object() {"Изображения", "Видео", "Аудио", "GIF", "Текст", "PDF", "EPUB", "Office"})
        Else
            clbMedia.Items.AddRange(New Object() {"Images", "Video", "Audio", "GIF", "Text", "PDF", "EPUB", "Office"})
        End If
        Controls.Add(clbMedia)

        ' 4. Scan conditions.
        Controls.Add(New Label With {.Left = lx, .Top = 238, .Width = 214, .Height = 20,
            .Text = If(rus, "Условия сканирования:", "Scan conditions:")})
        chkScanSub = New CheckBox With {.Left = ix, .Top = 236, .Width = iw, .Height = 22,
            .Text = If(rus, "Сканировать подпапки", "Scan subfolders"), .Checked = True}
        chkSubItems = New CheckBox With {.Left = ix, .Top = 260, .Width = iw, .Height = 22,
            .Text = If(rus, "Показывать подпапки как элементы", "Show subfolders as items")}
        chkHidden = New CheckBox With {.Left = ix, .Top = 284, .Width = iw, .Height = 22,
            .Text = If(rus, "Показывать скрытые файлы", "Show hidden files")}
        chkAllFiles = New CheckBox With {.Left = ix, .Top = 308, .Width = iw, .Height = 22,
            .Text = If(rus, "Все файлы (не только медиа)", "All files (not only media)")}
        AddHandler chkScanSub.CheckedChanged, Sub() chkSubItems.Enabled = chkScanSub.Checked
        Controls.AddRange(New Control() {chkScanSub, chkSubItems, chkHidden, chkAllFiles})

        ' 5. Access section.
        Controls.Add(New Label With {.Left = lx, .Top = 346, .Width = 534, .Height = 20,
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(rus, "Доступ:", "Access:")})
        Controls.Add(Hint(lx, 368, 536, If(rus, "По умолчанию телефон может добавлять, переименовывать и удалять файлы в папке.",
                                                 "By default the phone can add, rename and delete files in the folder.")))
        chkReadOnly = New CheckBox With {.Left = lx, .Top = 396, .Width = 534, .Height = 22,
            .Text = If(rus, "Недоступно для записи на уровне сервера - сервер запрещает изменения",
                           "Not writable at the server level - the server blocks changes"), .Checked = False}
        AddHandler chkReadOnly.CheckedChanged, AddressOf OnReadOnlyToggled
        Controls.Add(chkReadOnly)
        tip.SetToolTip(chkReadOnly, If(rus, "Настоящий запрет: сервер физически не даёт телефону менять файлы (загрузка, переименование, удаление).",
                                            "A real lock: the server physically prevents the phone from changing files (upload, rename, delete)."))

        chkSoftReadOnly = New CheckBox With {.Left = lx, .Top = 420, .Width = 534, .Height = 22,
            .Text = If(rus, "Публиковать как «только чтение» - подсказка приложению (сервер не блокирует)",
                           "Publish as read-only - a hint to the app (the server does not block)")}
        Controls.Add(chkSoftReadOnly)
        tip.SetToolTip(chkSoftReadOnly, If(rus, "Приложение спрячет кнопки изменения, но сам сервер запись не запрещает. Для настоящего запрета включите пункт выше.",
                                                "The app hides edit buttons, but the server itself does not block writes. For a real lock, tick the option above."))

        chkDestination = New CheckBox With {.Left = lx, .Top = 444, .Width = 534, .Height = 22,
            .Text = If(rus, "Папка-получатель - в неё можно копировать и переносить с телефона",
                           "Destination folder - the phone can copy and move files into it")}
        lblDestNote = New Label With {.Left = lx + 22, .Top = 468, .Width = 512, .Height = 18, .ForeColor = Color.DimGray,
            .Text = If(rus, "Ресурс попадёт в список получателей; папка станет доступна на запись. Цвет метки выберет приложение.",
                            "The resource joins the destinations list; the folder becomes writable. The app picks the chip colour.")}
        AddHandler chkDestination.CheckedChanged, AddressOf OnDestinationToggled
        Controls.AddRange(New Control() {chkDestination, lblDestNote})

        ' 6. Comment / PIN / slideshow.
        Controls.Add(New Label With {.Left = lx, .Top = 500, .Width = 214, .Height = 20,
            .Text = If(rus, "Комментарий:", "Comment:")})
        txtComment = New TextBox With {.Left = ix, .Top = 496, .Width = iw}
        Controls.Add(txtComment)

        Controls.Add(New Label With {.Left = lx, .Top = 534, .Width = 214, .Height = 20,
            .Text = If(rus, "PIN для ресурса:", "Resource PIN:")})
        txtPin = New TextBox With {.Left = ix, .Top = 530, .Width = 140}
        Controls.Add(txtPin)
        Controls.Add(New Label With {.Left = ix + 150, .Top = 534, .Width = iw - 150, .Height = 32, .ForeColor = Color.DimGray,
            .Text = If(rus, "Если задан - приложение попросит его при открытии.", "If set - the app asks for it on open.")})

        Controls.Add(New Label With {.Left = lx, .Top = 574, .Width = 214, .Height = 20,
            .Text = If(rus, "Слайд-шоу, секунд:", "Slideshow, seconds:")})
        numSlide = New NumericUpDown With {.Left = ix, .Top = 570, .Width = 90, .Minimum = 1, .Maximum = 3600, .Value = 10}
        Controls.Add(numSlide)
        Controls.Add(New Label With {.Left = ix + 100, .Top = 574, .Width = iw - 100, .Height = 20, .ForeColor = Color.DimGray,
            .Text = If(rus, "как часто листать фото", "how often to advance photos")})

        ' OK / Cancel.
        btnOk = New Button With {.Left = 380, .Top = 606, .Width = 86, .Height = 32, .Text = "OK"}
        btnCancel = New Button With {.Left = 474, .Top = 606, .Width = 86, .Height = 32,
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

        Dim writable As Boolean = _params.IsWritable()
        _syncingWritability = True
        chkReadOnly.Checked = Not writable
        chkDestination.Checked = _params.IsDestination
        _syncingWritability = False
        chkSoftReadOnly.Checked = _params.SoftReadOnly

        txtComment.Text = _params.Comment
        txtPin.Text = _params.AccessPin

        If _params.SlideshowInterval >= numSlide.Minimum AndAlso _params.SlideshowInterval <= numSlide.Maximum Then
            numSlide.Value = _params.SlideshowInterval
        End If
        UpdateDestinationEnabled()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
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
        ' Chip colour is decided by the Android app (no picker here), so never export one.
        _params.HasDestinationColor = False

        _params.Comment = txtComment.Text.Trim()
        _params.AccessPin = txtPin.Text.Trim()
        _params.SlideshowInterval = CInt(numSlide.Value)

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    ' --- read-only <-> destination mutual exclusion -----------------------------

    ''' <summary>Hard read-only and destination are mutually exclusive (a destination is
    ''' writable). Ticking hard read-only clears the destination.</summary>
    Private Sub OnReadOnlyToggled(sender As Object, e As EventArgs)
        If _syncingWritability Then Return
        _syncingWritability = True
        If chkReadOnly.Checked Then chkDestination.Checked = False
        _syncingWritability = False
        UpdateDestinationEnabled()
    End Sub

    ''' <summary>Enabling a destination clears hard read-only (the folder must accept writes).</summary>
    Private Sub OnDestinationToggled(sender As Object, e As EventArgs)
        If Not _syncingWritability Then
            _syncingWritability = True
            If chkDestination.Checked Then chkReadOnly.Checked = False
            _syncingWritability = False
        End If
        UpdateDestinationEnabled()
    End Sub

    Private Sub UpdateDestinationEnabled()
        lblDestNote.Visible = chkDestination.Checked
    End Sub

End Class
