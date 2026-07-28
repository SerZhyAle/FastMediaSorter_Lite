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
    Private _iconHandle As IntPtr

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
    Private _content As TableLayoutPanel   ' the scrollable settings grid
    Private _scrollHost As Panel           ' AutoScroll wrapper around _content
    Private _buttonBar As TableLayoutPanel ' pinned OK/Cancel bar (Dock=Bottom)

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
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.TF("Параметры ресурса - {0}", _folderName)
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed, Sub() ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        ' Fallback size; OnLoad measures the real content and caps it to the screen.
        Me.ClientSize = New Size(500, 620)

        Dim tip As New ToolTip()

        ' Not docked/AutoSize-Fill: it must be free to grow taller than the window so the
        ' AutoScroll host below can scroll it. Anchored top-left, sized to its content.
        Dim tlp As New TableLayoutPanel With {
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
            .ColumnCount = 2, .Padding = New Padding(18, 16, 18, 12), .GrowStyle = TableLayoutPanelGrowStyle.AddRows}
        _content = tlp
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        tlp.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim r As Integer = 0

        ' 1. Name.
        txtLabel = New TextBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0)}
        tlp.Controls.Add(Cap(Localization.T("Название на телефоне:")), 0, r)
        tlp.Controls.Add(txtLabel, 1, r) : r += 1
        AddFullRow(tlp, Note(Localization.T("Как ресурс называется в приложении. Пусто = имя папки.")), r) : r += 1

        ' 2. Type / profile.
        cmbProfile = New ComboBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbProfile.Items.AddRange(New Object() {
            Localization.T("Обычная папка (по умолчанию)"), Localization.T("Аудиотека"), Localization.T("Видеотека"),
            Localization.T("Фотохранилище"), Localization.T("Документы"), Localization.T("Все файлы")})
        tlp.Controls.Add(Cap(Localization.T("Тип ресурса:")), 0, r)
        tlp.Controls.Add(cmbProfile, 1, r) : r += 1
        AddFullRow(tlp, Note(Localization.T("Как приложение покажет папку и какие файлы возьмёт (напр. «Видеотека» - только видео).")), r) : r += 1

        ' 3. Exact media set - plain check boxes in a wrapping panel (all 8 fit, DPI-safe).
        Dim mediaFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 2, 0, 0), .WrapContents = True, .MaximumSize = New Size(440, 0)}
        Dim names As String() = {
            Localization.T("Изображения"), Localization.T("Видео"), Localization.T("Аудио"), "GIF",
            Localization.T("Текст"), "PDF", "EPUB", "Office"}
        For i As Integer = 0 To MediaTokens.Length - 1
            _mediaChecks(i) = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 18, 2), .Text = names(i)}
            mediaFlow.Controls.Add(_mediaChecks(i))
        Next
        Dim mediaCap As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 1, .Margin = New Padding(0, 3, 12, 0)}
        mediaCap.Controls.Add(Cap(Localization.T("Точный набор типов:")))
        mediaCap.Controls.Add(Note(Localization.T("Необязательно. Пусто = решает тип.")))
        tlp.Controls.Add(mediaCap, 0, r)
        tlp.Controls.Add(mediaFlow, 1, r) : r += 1

        ' 4. Scan conditions.
        Dim scanFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 2, 0, 0), .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        chkScanSub = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = Localization.T("Сканировать подпапки"), .Checked = True}
        chkSubItems = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = Localization.T("Показывать подпапки как элементы")}
        chkHidden = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = Localization.T("Показывать скрытые файлы")}
        chkAllFiles = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = Localization.T("Все файлы (не только медиа)")}
        AddHandler chkScanSub.CheckedChanged, Sub() chkSubItems.Enabled = chkScanSub.Checked
        scanFlow.Controls.AddRange(New Control() {chkScanSub, chkSubItems, chkHidden, chkAllFiles})
        tlp.Controls.Add(Cap(Localization.T("Условия сканирования:")), 0, r)
        tlp.Controls.Add(scanFlow, 1, r) : r += 1

        ' 5. Access section (all full-width, AutoSize -> never clipped).
        AddFullRow(tlp, New Label With {.AutoSize = True, .Margin = New Padding(0, 12, 0, 2),
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = Localization.T("Доступ:")}, r) : r += 1
        AddFullRow(tlp, Note(Localization.T("По умолчанию телефон может добавлять, переименовывать и удалять файлы в папке.")), r) : r += 1
        chkReadOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Недоступно для записи на уровне сервера - сервер запрещает изменения")}
        AddHandler chkReadOnly.CheckedChanged, AddressOf OnReadOnlyToggled
        tip.SetToolTip(chkReadOnly, Localization.T("Настоящий запрет: сервер физически не даёт телефону менять файлы."))
        AddFullRow(tlp, chkReadOnly, r) : r += 1
        chkSoftReadOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2),
            .Text = Localization.T("Публиковать как «только чтение» - подсказка приложению (сервер не блокирует)")}
        tip.SetToolTip(chkSoftReadOnly, Localization.T("Приложение спрячет кнопки изменения, но сервер запись не запрещает."))
        AddFullRow(tlp, chkSoftReadOnly, r) : r += 1
        chkDestination = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0),
            .Text = Localization.T("Папка-получатель - в неё можно копировать и переносить с телефона")}
        AddHandler chkDestination.CheckedChanged, AddressOf OnDestinationToggled
        AddFullRow(tlp, chkDestination, r) : r += 1
        lblDestNote = Note(Localization.T("Папка станет доступна на запись; ресурс попадёт в список получателей. Цвет метки выберет приложение."))
        lblDestNote.Margin = New Padding(24, 0, 0, 6)
        AddFullRow(tlp, lblDestNote, r) : r += 1

        ' 6. Comment / PIN / slideshow.
        txtComment = New TextBox With {.Width = 420, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 8, 0, 0)}
        tlp.Controls.Add(Cap(Localization.T("Комментарий:")), 0, r)
        tlp.Controls.Add(txtComment, 1, r) : r += 1

        Dim pinFlow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .WrapContents = False}
        txtPin = New TextBox With {.Width = 150, .Margin = New Padding(0, 0, 10, 0)}
        pinFlow.Controls.Add(txtPin)
        pinFlow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 0), .ForeColor = Color.DimGray,
            .Text = Localization.T("если задан - приложение попросит его при открытии")})
        tlp.Controls.Add(Cap(Localization.T("PIN для ресурса:")), 0, r)
        tlp.Controls.Add(pinFlow, 1, r) : r += 1

        Dim slideFlow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(0, 3, 0, 0), .WrapContents = False}
        numSlide = New NumericUpDown With {.Width = 90, .Minimum = 1, .Maximum = 3600, .Value = 10, .Margin = New Padding(0, 0, 10, 0)}
        slideFlow.Controls.Add(numSlide)
        slideFlow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 5, 0, 0), .ForeColor = Color.DimGray,
            .Text = Localization.T("как часто листать фото")})
        tlp.Controls.Add(Cap(Localization.T("Слайд-шоу, секунд:")), 0, r)
        tlp.Controls.Add(slideFlow, 1, r) : r += 1

        ' Buttons live in a PINNED bottom bar (Dock=Bottom) - always visible, so they can
        ' never scroll off the bottom of the screen when the content is tall at high DPI.
        Dim btnFlow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Anchor = AnchorStyles.Right, .Margin = New Padding(0),
            .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        btnOk = New Button With {.Width = 96, .Height = 34, .Text = "OK", .Margin = New Padding(0, 0, 8, 0)}
        btnCancel = New Button With {.Width = 96, .Height = 34, .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf OnOk
        btnFlow.Controls.Add(btnOk)
        btnFlow.Controls.Add(btnCancel)

        _buttonBar = New TableLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 1, .Padding = New Padding(18, 8, 18, 12)}
        _buttonBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        _buttonBar.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        _buttonBar.Controls.Add(btnFlow, 0, 0)

        ' Scrollable host: fills the space above the button bar and shows a vertical scrollbar
        ' when the settings are taller than the window (small screens / high display scaling).
        _scrollHost = New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        _scrollHost.Controls.Add(tlp)

        ' Fill host first, docked bar last, so the host takes the leftover above the bar.
        Me.Controls.Add(_scrollHost)
        Me.Controls.Add(_buttonBar)
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    ''' <summary>Sizes the window to its content but never larger than the monitor working area
    ''' (adding room for the scrollbar when the content must scroll), then re-centers on the
    ''' owner. Runs after MyBase.OnLoad so the measurements are already at the final DPI.</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Try
            Dim wa As Rectangle = DpiLayout.WorkingAreaFor(Me)
            Dim chromeW As Integer = Me.Width - Me.ClientSize.Width
            Dim chromeH As Integer = Me.Height - Me.ClientSize.Height
            ' Measure the TALLEST state: the destination note is shown/hidden at runtime,
            ' so reserve its height upfront. That way toggling the "destination" checkbox
            ' never grows the content past the window and no scrollbar ever appears (the
            ' fixed dialog cannot resize afterwards).
            Dim noteWasVisible As Boolean = lblDestNote.Visible
            lblDestNote.Visible = True
            Dim content As Size = _content.PreferredSize
            lblDestNote.Visible = noteWasVisible
            Dim barH As Integer = _buttonBar.PreferredSize.Height
            Dim desiredClientH As Integer = content.Height + barH
            Dim clientH As Integer = Math.Min(desiredClientH, wa.Height - chromeH)
            Dim needVScroll As Boolean = clientH < desiredClientH
            Dim clientW As Integer = content.Width + If(needVScroll, SystemInformation.VerticalScrollBarWidth, 0)
            clientW = Math.Min(clientW, wa.Width - chromeW)
            Me.ClientSize = New Size(clientW, clientH)
            DpiLayout.CenterOnOwner(Me)
        Catch
        End Try
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
