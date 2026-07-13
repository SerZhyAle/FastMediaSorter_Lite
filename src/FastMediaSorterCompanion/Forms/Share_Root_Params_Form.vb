Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "Параметры ресурса" - the per-shared-root settings the .fmscfg schema v2 export
''' carries to the phone. Built with a TableLayoutPanel + AutoSize (NOT absolute
''' pixel coordinates) so it scales cleanly at any display scaling (125/150/175%) -
''' the layout reflows from control sizes, which grow with the font. Media types are
''' plain check boxes in a wrapping panel (not a fixed-column CheckedListBox), so all
''' eight always fit and stay readable at high DPI. Each option carries a short
''' "what it does" note. The chip colour is NOT offered - the Android app decides it.
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
    Private ReadOnly _mediaChecks(MediaTokens.Length - 1) As CheckBox
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
    Private ReadOnly _rus As Boolean = Is_Russian_Language

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

    ' --- UI builders (all AutoSize / layout-panel based - DPI-robust) -----------

    Private Function Cap(text As String) As Label
        Return New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 6, 12, 0), .Text = text}
    End Function

    Private Function Note(text As String) As Label
        Return New Label With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 0, 0, 6),
            .ForeColor = Color.DimGray, .Text = text}
    End Function

    Private Sub BuildUi()
        Me.Text = (If(_rus, "Параметры ресурса - ", "Resource options - ")) & _folderName
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        Dim tip As New ToolTip()

        Dim tlp As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Padding = New Padding(18, 16, 18, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim r As Integer = 0

        ' 1. Name.
        txtLabel = New TextBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0)}
        tlp.Controls.Add(Cap(If(_rus, "Название на телефоне:", "Name on the phone:")), 0, r)
        tlp.Controls.Add(txtLabel, 1, r) : r += 1
        AddFullRow(tlp, Note(If(_rus, "Как ресурс называется в приложении. Пусто = имя папки.",
                                     "The resource name in the app. Empty = the folder name.")), r) : r += 1

        ' 2. Type / profile.
        cmbProfile = New ComboBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .DropDownStyle = ComboBoxStyle.DropDownList}
        If _rus Then
            cmbProfile.Items.AddRange(New Object() {"Обычная папка (по умолчанию)", "Аудиотека", "Видеотека", "Фотохранилище", "Документы", "Все файлы"})
        Else
            cmbProfile.Items.AddRange(New Object() {"Regular folder (default)", "Audio library", "Video library", "Photo storage", "Documents", "All files"})
        End If
        tlp.Controls.Add(Cap(If(_rus, "Тип ресурса:", "Resource type:")), 0, r)
        tlp.Controls.Add(cmbProfile, 1, r) : r += 1
        AddFullRow(tlp, Note(If(_rus, "Как приложение покажет папку и какие файлы возьмёт (напр. «Видеотека» - только видео).",
                                     "How the app shows the folder and which files it takes (e.g. Video library - videos only).")), r) : r += 1

        ' 3. Exact media set - plain check boxes in a wrapping panel (all 8 fit, DPI-safe).
        Dim mediaFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 2, 0, 0), .WrapContents = True, .MaximumSize = New Size(440, 0)}
        Dim names As String() = If(_rus,
            New String() {"Изображения", "Видео", "Аудио", "GIF", "Текст", "PDF", "EPUB", "Office"},
            New String() {"Images", "Video", "Audio", "GIF", "Text", "PDF", "EPUB", "Office"})
        For i As Integer = 0 To MediaTokens.Length - 1
            _mediaChecks(i) = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 18, 2), .Text = names(i)}
            mediaFlow.Controls.Add(_mediaChecks(i))
        Next
        Dim mediaCap As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0, 3, 12, 0)}
        mediaCap.Controls.Add(Cap(If(_rus, "Точный набор типов:", "Exact media set:")))
        mediaCap.Controls.Add(Note(If(_rus, "Необязательно. Пусто = решает тип.", "Optional. Empty = the type decides.")))
        tlp.Controls.Add(mediaCap, 0, r)
        tlp.Controls.Add(mediaFlow, 1, r) : r += 1

        ' 4. Scan conditions.
        Dim scanFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 2, 0, 0), .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        chkScanSub = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(_rus, "Сканировать подпапки", "Scan subfolders"), .Checked = True}
        chkSubItems = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(_rus, "Показывать подпапки как элементы", "Show subfolders as items")}
        chkHidden = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(_rus, "Показывать скрытые файлы", "Show hidden files")}
        chkAllFiles = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(_rus, "Все файлы (не только медиа)", "All files (not only media)")}
        AddHandler chkScanSub.CheckedChanged, Sub() chkSubItems.Enabled = chkScanSub.Checked
        scanFlow.Controls.AddRange(New Control() {chkScanSub, chkSubItems, chkHidden, chkAllFiles})
        tlp.Controls.Add(Cap(If(_rus, "Условия сканирования:", "Scan conditions:")), 0, r)
        tlp.Controls.Add(scanFlow, 1, r) : r += 1

        ' 5. Access section (all full-width, AutoSize -> never clipped).
        AddFullRow(tlp, New Label With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 2),
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(_rus, "Доступ:", "Access:")}, r) : r += 1
        AddFullRow(tlp, Note(If(_rus, "По умолчанию телефон может добавлять, переименовывать и удалять файлы в папке.",
                                     "By default the phone can add, rename and delete files in the folder.")), r) : r += 1
        chkReadOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = If(_rus, "Недоступно для записи на уровне сервера - сервер запрещает изменения",
                            "Not writable at the server level - the server blocks changes")}
        AddHandler chkReadOnly.CheckedChanged, AddressOf OnReadOnlyToggled
        tip.SetToolTip(chkReadOnly, If(_rus, "Настоящий запрет: сервер физически не даёт телефону менять файлы.", "A real lock: the server physically prevents changes."))
        AddFullRow(tlp, chkReadOnly, r) : r += 1
        chkSoftReadOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = If(_rus, "Публиковать как «только чтение» - подсказка приложению (сервер не блокирует)",
                            "Publish as read-only - a hint to the app (the server does not block)")}
        tip.SetToolTip(chkSoftReadOnly, If(_rus, "Приложение спрячет кнопки изменения, но сервер запись не запрещает.", "The app hides edit buttons, but the server does not block writes."))
        AddFullRow(tlp, chkSoftReadOnly, r) : r += 1
        chkDestination = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0),
            .Text = If(_rus, "Папка-получатель - в неё можно копировать и переносить с телефона",
                            "Destination folder - the phone can copy and move files into it")}
        AddHandler chkDestination.CheckedChanged, AddressOf OnDestinationToggled
        AddFullRow(tlp, chkDestination, r) : r += 1
        lblDestNote = Note(If(_rus, "Папка станет доступна на запись; ресурс попадёт в список получателей. Цвет метки выберет приложение.",
                                    "The folder becomes writable; the resource joins the destinations list. The app picks the chip colour."))
        lblDestNote.Margin = New Padding(24, 0, 0, 6)
        AddFullRow(tlp, lblDestNote, r) : r += 1

        ' 6. Comment / PIN / slideshow.
        txtComment = New TextBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 8, 0, 0)}
        tlp.Controls.Add(Cap(If(_rus, "Комментарий:", "Comment:")), 0, r)
        tlp.Controls.Add(txtComment, 1, r) : r += 1

        Dim pinFlow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .WrapContents = False}
        txtPin = New TextBox With {.Width = 150, .Margin = New Padding(0, 0, 10, 0)}
        pinFlow.Controls.Add(txtPin)
        pinFlow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 0), .ForeColor = Color.DimGray,
            .Text = If(_rus, "если задан - приложение попросит его при открытии", "if set - the app asks for it on open")})
        tlp.Controls.Add(Cap(If(_rus, "PIN для ресурса:", "Resource PIN:")), 0, r)
        tlp.Controls.Add(pinFlow, 1, r) : r += 1

        Dim slideFlow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .WrapContents = False}
        numSlide = New NumericUpDown With {.Width = 90, .Minimum = 1, .Maximum = 3600, .Value = 10, .Margin = New Padding(0, 0, 10, 0)}
        slideFlow.Controls.Add(numSlide)
        slideFlow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 5, 0, 0), .ForeColor = Color.DimGray,
            .Text = If(_rus, "как часто листать фото", "how often to advance photos")})
        tlp.Controls.Add(Cap(If(_rus, "Слайд-шоу, секунд:", "Slideshow, seconds:")), 0, r)
        tlp.Controls.Add(slideFlow, 1, r) : r += 1

        ' Buttons.
        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Right, .Margin = New Padding(0, 16, 0, 0),
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        btnOk = New Button With {.Width = 96, .Height = 34, .Text = "OK", .Margin = New Padding(0, 0, 8, 0)}
        btnCancel = New Button With {.Width = 96, .Height = 34, .Text = If(_rus, "Отмена", "Cancel"), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf OnOk
        btnFlow.Controls.Add(btnOk)
        btnFlow.Controls.Add(btnCancel)
        AddFullRow(tlp, btnFlow, r) : r += 1

        Me.Controls.Add(tlp)
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel
    End Sub

    ''' <summary>Adds a control spanning both columns of the layout.</summary>
    Private Shared Sub AddFullRow(tlp As TableLayoutPanel, c As Control, row As Integer)
        tlp.Controls.Add(c, 0, row)
        tlp.SetColumnSpan(c, 2)
    End Sub

    ' --- load / save -----------------------------------------------------------

    Private Sub LoadParams()
        txtLabel.Text = If(_params.Label.Trim().Length > 0, _params.Label, _folderName)

        Dim profIdx As Integer = Array.IndexOf(ProfileTokens, If(_params.Profile, "none"))
        cmbProfile.SelectedIndex = If(profIdx >= 0, profIdx, 0)

        If _params.MediaTypes IsNot Nothing Then
            For Each t As String In _params.MediaTypes
                Dim i As Integer = Array.IndexOf(MediaTokens, t)
                If i >= 0 Then _mediaChecks(i).Checked = True
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
            If _mediaChecks(i).Checked Then media.Add(MediaTokens(i))
        Next
        _params.MediaTypes = media

        _params.ScanSubdirectories = chkScanSub.Checked
        _params.ShowSubfoldersAsItems = chkSubItems.Checked
        _params.ShowHiddenFiles = chkHidden.Checked
        _params.AllFiles = chkAllFiles.Checked
        _params.IsReadOnly = chkReadOnly.Checked
        _params.SoftReadOnly = chkSoftReadOnly.Checked
        _params.IsDestination = chkDestination.Checked
        _params.HasDestinationColor = False   ' the Android app decides the chip colour

        _params.Comment = txtComment.Text.Trim()
        _params.AccessPin = txtPin.Text.Trim()
        _params.SlideshowInterval = CInt(numSlide.Value)

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    ' --- read-only <-> destination mutual exclusion -----------------------------

    Private Sub OnReadOnlyToggled(sender As Object, e As EventArgs)
        If _syncingWritability Then Return
        _syncingWritability = True
        If chkReadOnly.Checked Then chkDestination.Checked = False
        _syncingWritability = False
        UpdateDestinationEnabled()
    End Sub

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
