Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Companion package wizard - LEVEL 2 of the two-wizard model (§4.5): a ONE-SHOT act of
''' giving a specific recipient access. The resources are shown as a real editable GRID
''' (DataGridView): one row per served folder, one column per resource parameter. Click any
''' cell to change it (checkbox / dropdown / media-set popup / text / number). Cells start
''' from each folder's stored defaults; edits are PER-RECIPIENT and one-shot (§4.5.3) - they
''' ride only in this export (perRootParams) and never touch the folder's own defaults. Below
''' the grid: common per-export settings, address/host-key, QR and the action buttons.
''' AutoScaleMode.Font + app-wide default font (never Me.Font=) so it scales at any DPI.
''' </summary>
Public NotInheritable Class PackageWizardForm
    Inherits Form

    ' Profile + media tokens/displays kept identical to Share_Root_Params_Form so a folder's
    ' defaults round-trip and the .fmscfg emits the exact frozen tokens.
    Private Shared ReadOnly ProfileTokens As String() =
        {"none", "audio_library", "video_library", "photo_storage", "documents", "all_files"}
    Private Shared ReadOnly MediaTokens As String() =
        {"image", "video", "audio", "gif", "text", "pdf", "epub", "office"}

    Private ReadOnly _preselect As List(Of String)
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _config As ShareConfigResult
    Private _loading As Boolean
    Private _syncing As Boolean   ' guards the RO<->destination mutual-exclusion write-back
    Private _rebuildTimer As Timer   ' trailing debounce - coalesces per-keystroke QR re-renders
    Private _iconHandle As IntPtr

    ' per-EXPORT toggles (whole access code, NOT per folder)
    Private chkLanOnly As CheckBox
    Private chkNoPassword As CheckBox
    ' the resource grid
    Private dgv As DataGridView
    ' output surfaces
    Private picQr As PictureBox
    Private btnShowQr As Button
    Private btnCopyLogin As Button
    Private btnSave As Button
    Private btnEmail As Button
    Private btnClose As Button
    Private lblHint As Label
    Private toolTip As ToolTip
    Private _qrGlyph As Image
    ' Accent colours for the "Show QR" button - match the app's blue share glyph (ShareIcons).
    Private Shared ReadOnly QrAccent As Color = Color.FromArgb(30, 120, 220)
    Private Shared ReadOnly QrAccentDark As Color = Color.FromArgb(18, 78, 150)

    Public Sub New(preselect As List(Of String))
        _preselect = If(preselect, New List(Of String)())
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    Private Shared ReadOnly Property Rus As Boolean
        Get
            Return Is_Russian_Language
        End Get
    End Property

    ''' <summary>Per-row working state stored in DataGridViewRow.Tag: the folder identity plus a
    ''' live ShareRootParams (clone of the folder's defaults) edited by this recipient's cells.</summary>
    Private NotInheritable Class RowState
        Public ReadOnly HostPath As String
        Public ReadOnly FolderName As String
        Public ReadOnly P As ShareRootParams
        Public Sub New(hostPath As String, folderName As String, p As ShareRootParams)
            Me.HostPath = hostPath : Me.FolderName = folderName : Me.P = p
        End Sub
    End Class

    Private Shared Function ProfileDisplays() As String()
        Return If(Rus,
            New String() {"Обычная папка", "Аудиотека", "Видеотека", "Фотохранилище", "Документы", "Все файлы"},
            New String() {"Regular folder", "Audio library", "Video library", "Photo storage", "Documents", "All files"})
    End Function

    Private Shared Function MediaDisplays() As String()
        Return If(Rus,
            New String() {"Изображения", "Видео", "Аудио", "GIF", "Текст", "PDF", "EPUB", "Office"},
            New String() {"Images", "Video", "Audio", "GIF", "Text", "PDF", "EPUB", "Office"})
    End Function

    Private Sub BuildUi()
        Me.Text = If(Rus, "Поделиться - код доступа", "Share - access code")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = True
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        ' Fit-to-content, not maximized: FitToGrid (after the columns are populated) widens the
        ' window to exactly show ALL columns without horizontal scroll, capped to the screen.
        Me.MinimumSize = New Size(760, 520)
        Me.ClientSize = New Size(1400, 660)
        toolTip = New ToolTip()
        _rebuildTimer = New Timer With {.Interval = 250}
        AddHandler _rebuildTimer.Tick, AddressOf OnRebuildTick

        ' Header (top).
        Dim topBar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Padding = New Padding(16, 12, 16, 6)}
        topBar.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0), .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Папки и параметры этого кода доступа", "Folders and settings for this access code")})
        topBar.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0), .ForeColor = Color.DimGray,
            .Text = If(Rus, "Нажмите на любую ячейку, чтобы изменить значение. Значения взяты из настроек папки; правки - только для этого кода.",
                            "Click any cell to change a value. Values come from each folder's settings; edits apply only to this code.")})

        ' The resource grid (fill).
        BuildGrid()

        ' Below the grid: common settings + address/key (left) | QR (right), then action buttons.
        Dim below As New TableLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 2, .Margin = New Padding(0)}
        below.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        below.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim leftCol As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0, 0, 24, 0)}
        leftCol.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4), .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(Rus, "Общие настройки передачи:", "Common transfer settings:")})
        chkLanOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Только локальная сеть (без адреса из интернета)", "LAN only (no internet address)")}
        chkNoPassword = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 6), .Text = If(Rus, "Не включать пароль в файл/QR", "Do not include the password in the file/QR")}
        AddHandler chkLanOnly.CheckedChanged, AddressOf OnRebuildToggle
        AddHandler chkNoPassword.CheckedChanged, AddressOf OnRebuildToggle
        toolTip.SetToolTip(chkNoPassword, If(Rus, "Пароль не попадёт в файл/QR - телефон запросит его при импорте; передайте пароль отдельно.",
                                                 "The password stays out of the file/QR - the phone asks for it at import; pass it separately."))
        leftCol.Controls.AddRange(New Control() {chkLanOnly, chkNoPassword})
        ' NB: the LAN address + host-key fingerprint are deliberately NOT shown here. The
        ' package embeds ALL access paths (LAN + IPv6 + internet), so showing only the LAN
        ' address misleads the user into thinking the code is local-only; those details already
        ' live (copyable) in the main window's server panel.

        ' Right: a compact LIVE QR preview (click to enlarge) + a bright accent button - NOT the
        ' old 190x190 empty square. The preview shows the actual code so the user sees it at a
        ' glance; the button is the obvious, brightly-coloured tap target that opens it big for a
        ' phone camera. Both open the same zoom window.
        _qrGlyph = BuildQrGlyph()
        Dim rightCol As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0), .Anchor = AnchorStyles.Top}
        picQr = New PictureBox With {.Size = New Size(128, 128), .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.White, .BorderStyle = BorderStyle.FixedSingle, .Margin = New Padding(0, 0, 0, 6)}
        AddHandler picQr.Click, AddressOf OnShowQr
        AddHandler picQr.Paint, AddressOf OnQrPreviewPaint
        toolTip.SetToolTip(picQr, If(Rus, "Нажмите, чтобы увеличить", "Click to enlarge"))
        btnShowQr = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Font = New Font(Me.Font, FontStyle.Bold), .Margin = New Padding(0), .Padding = New Padding(10, 6, 12, 6),
            .Text = If(Rus, "Показать QR-код", "Show QR code"), .MinimumSize = New Size(128, 0),
            .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .FlatStyle = FlatStyle.Flat}
        btnShowQr.FlatAppearance.BorderSize = 1
        AddHandler btnShowQr.Click, AddressOf OnShowQr
        rightCol.Controls.Add(picQr)
        rightCol.Controls.Add(btnShowQr)
        SetQrAvailable(False)

        below.Controls.Add(leftCol, 0, 0)
        below.Controls.Add(rightCol, 1, 0)

        lblHint = New Label With {.AutoSize = True, .MaximumSize = New Size(980, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 8, 0, 4)}

        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = True, .Margin = New Padding(0, 4, 0, 0)}
        btnCopyLogin = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = If(Rus, "Скопировать логин/пароль", "Copy login/password"), .Enabled = False}
        btnSave = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = If(Rus, "Сохранить файл .fmscfg..", "Save .fmscfg file.."), .Enabled = False}
        btnEmail = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = If(Rus, "Отправить по почте..", "Send by email.."), .Enabled = False}
        btnClose = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 0, 2), .Padding = New Padding(16, 5, 16, 5), .Text = If(Rus, "Закрыть", "Close"), .DialogResult = DialogResult.Cancel}
        AddHandler btnCopyLogin.Click, AddressOf OnCopyLogin
        AddHandler btnSave.Click, AddressOf OnSaveConfig
        AddHandler btnEmail.Click, AddressOf OnEmail
        btnRow.Controls.AddRange(New Control() {btnCopyLogin, btnSave, btnEmail, btnClose})

        Dim bottomBar As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 1, .Padding = New Padding(16, 6, 16, 12)}
        bottomBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        AddStackRow(bottomBar, below)
        AddStackRow(bottomBar, lblHint)
        AddStackRow(bottomBar, btnRow)

        ' Add Fill FIRST (docked last => fills the leftover), then the edges.
        Me.Controls.Add(dgv)
        Me.Controls.Add(topBar)
        Me.Controls.Add(bottomBar)
        Me.CancelButton = btnClose

        _loading = True
        chkLanOnly.Checked = _settings.LanOnlyExport
        chkNoPassword.Checked = _settings.ExcludePasswordFromExport
        _loading = False

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosed, AddressOf HandleFormClosed
    End Sub

    ''' <summary>Baseline safety before anything else runs: relax an over-large (font-scaled)
    ''' minimum and cap the window to the monitor working area, so even when the server is not
    ''' running (FitToGrid never runs) the window and its bottom action buttons stay on-screen at
    ''' high display scaling.</summary>
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
    End Sub

    ''' <summary>Sizes the window to exactly fit the grid: wide enough for ALL columns (no
    ''' horizontal scroll), capped to ~97% of the working area in BOTH dimensions, then
    ''' re-centers on the parent. Measured from the actual rendered column widths, so it is
    ''' DPI-correct (both are in the form's device space). Called once the columns are populated.</summary>
    Private Sub FitToGrid()
        Try
            If dgv Is Nothing OrElse dgv.Columns.Count = 0 Then Return
            ' Force the AllCells auto-size NOW - right after populate the column widths are
            ' still at their defaults until a layout pass runs, which would mis-measure here.
            dgv.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells)
            Dim colsW As Integer = dgv.Columns.GetColumnsWidth(DataGridViewElementStates.Visible)
            Dim wa As Rectangle = Screen.FromHandle(Me.Handle).WorkingArea
            Dim nonClientW As Integer = Me.Width - Me.ClientSize.Width
            Dim nonClientH As Integer = Me.Height - Me.ClientSize.Height
            Dim desiredClientW As Integer = colsW + SystemInformation.VerticalScrollBarWidth + 8 + dgv.Margin.Horizontal
            Dim maxClientW As Integer = CInt(wa.Width * 0.97) - nonClientW
            Dim clientW As Integer = System.Math.Min(desiredClientW, maxClientW)
            clientW = System.Math.Max(clientW, Me.MinimumSize.Width - nonClientW)
            ' Height: keep the default unless it would exceed the screen (high DPI / small
            ' screen), in which case cap it so the bottom bar's action buttons stay visible.
            Dim maxClientH As Integer = CInt(wa.Height * 0.97) - nonClientH
            Dim clientH As Integer = System.Math.Min(Me.ClientSize.Height, maxClientH)
            clientH = System.Math.Max(clientH, Me.MinimumSize.Height - nonClientH)
            Me.ClientSize = New Size(clientW, clientH)
            Dim anchor As Rectangle = If(Me.Owner IsNot Nothing, Me.Owner.Bounds, wa)
            Dim x As Integer = anchor.Left + (anchor.Width - Me.Width) \ 2
            Dim y As Integer = anchor.Top + (anchor.Height - Me.Height) \ 2
            x = System.Math.Max(wa.Left, System.Math.Min(x, wa.Right - Me.Width))
            y = System.Math.Max(wa.Top, System.Math.Min(y, wa.Bottom - Me.Height))
            Me.Location = New Point(x, y)
        Catch
        End Try
    End Sub

    Private Shared Sub AddStackRow(tlp As TableLayoutPanel, c As Control)
        Dim r As Integer = tlp.RowCount
        tlp.RowCount = r + 1
        tlp.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        tlp.Controls.Add(c, 0, r)
    End Sub

    ' --- grid construction ------------------------------------------------------

    Private Sub BuildGrid()
        dgv = New DataGridView With {
            .Dock = DockStyle.Fill,
            .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False,
            .AllowUserToResizeRows = False,
            .RowHeadersVisible = False,
            .MultiSelect = False,
            .SelectionMode = DataGridViewSelectionMode.CellSelect,
            .EditMode = DataGridViewEditMode.EditOnEnter,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            .BackgroundColor = SystemColors.Window,
            .BorderStyle = BorderStyle.FixedSingle,
            .Margin = New Padding(16, 0, 16, 0)
        }

        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "inc", .HeaderText = If(Rus, "Вкл", "On")})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "folder", .HeaderText = If(Rus, "Папка", "Folder"), .[ReadOnly] = True, .MinimumWidth = 120})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "label", .HeaderText = If(Rus, "Имя на телефоне", "Name on phone"), .MinimumWidth = 130})

        Dim colProfile As New DataGridViewComboBoxColumn With {.Name = "profile", .HeaderText = If(Rus, "Тип", "Type"), .FlatStyle = FlatStyle.Flat, .MinimumWidth = 130}
        colProfile.Items.AddRange(DirectCast(ProfileDisplays(), Object()))
        dgv.Columns.Add(colProfile)

        dgv.Columns.Add(New DataGridViewButtonColumn With {.Name = "media", .HeaderText = If(Rus, "Типы медиа", "Media types"), .UseColumnTextForButtonValue = False, .FlatStyle = FlatStyle.Standard, .MinimumWidth = 120})

        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "scan", .HeaderText = If(Rus, "Скан подпапок", "Scan subfolders")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "subitems", .HeaderText = If(Rus, "Подпапки как элементы", "Subfolders as items")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "hidden", .HeaderText = If(Rus, "Скрытые", "Hidden")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "allfiles", .HeaderText = If(Rus, "Все файлы", "All files")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "ro", .HeaderText = If(Rus, "Только чтение", "Read-only")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "softro", .HeaderText = If(Rus, "RO-подсказка", "RO hint")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "dest", .HeaderText = If(Rus, "Приёмник", "Destination")})

        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "comment", .HeaderText = If(Rus, "Комментарий", "Comment"), .MinimumWidth = 140})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "pin", .HeaderText = "PIN", .MinimumWidth = 70})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "slide", .HeaderText = If(Rus, "Слайд-шоу, сек", "Slideshow, sec"), .MinimumWidth = 80})

        ' Header tooltips for the less-obvious columns.
        dgv.Columns("ro").ToolTipText = If(Rus, "Сервер запрещает изменения (настоящий запрет).", "The server blocks changes (a real lock).")
        dgv.Columns("softro").ToolTipText = If(Rus, "Приложение спрячет кнопки изменения, но сервер запись не запрещает.", "The app hides edit buttons, but the server does not block writes.")
        dgv.Columns("dest").ToolTipText = If(Rus, "Папка-получатель: в неё можно копировать/переносить с телефона (делает папку доступной на запись).", "Destination folder: the phone can copy/move into it (makes the folder writable).")
        dgv.Columns("media").ToolTipText = If(Rus, "Нажмите, чтобы выбрать точный набор типов. Пусто = решает тип.", "Click to pick the exact media set. Empty = the type decides.")

        AddHandler dgv.CurrentCellDirtyStateChanged, AddressOf OnGridDirty
        AddHandler dgv.CellValueChanged, AddressOf OnCellValueChanged
        AddHandler dgv.CellClick, AddressOf OnCellClick
        AddHandler dgv.DataError, Sub(s, e) e.ThrowException = False
    End Sub

    ' Commit checkbox/combo edits immediately so CellValueChanged fires on the click.
    Private Sub OnGridDirty(sender As Object, e As EventArgs)
        If dgv.IsCurrentCellDirty Then dgv.CommitEdit(DataGridViewDataErrorContexts.Commit)
    End Sub

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        SetHint(If(Rus, "Получение состояния..", "Fetching state.."))
        _status = Await ShareController.GetStatusAsync()
        ' The user may have closed the modal wizard during the pipe round-trip - the form
        ' (and its grid/timer) is then disposed, so bail before touching any control.
        If IsDisposed OrElse Disposing Then Return
        If _status Is Nothing OrElse Not _status.Running Then
            SetHint(If(Rus, "Сервер не запущен.", "The server is not running."))
            Return
        End If
        PopulateGrid()
        FitToGrid()
        Rebuild()
    End Sub

    ''' <summary>One grid row per served folder, cells pre-filled from the folder's defaults.</summary>
    Private Sub PopulateGrid()
        _loading = True
        dgv.SuspendLayout()
        Try
            dgv.Rows.Clear()
            Dim profDisplays As String() = ProfileDisplays()
            If _status.Roots IsNot Nothing Then
                For Each r As ShareFolder In _status.Roots
                    Dim host As String = If(r.hostPath, "")
                    If host.Length = 0 Then Continue For
                    Dim folderName As String = If(String.IsNullOrEmpty(r.name), host, r.name)
                    Dim p As ShareRootParams = ShareRootParamsStore.GetFor(host)

                    Dim idx As Integer = dgv.Rows.Add()
                    Dim row As DataGridViewRow = dgv.Rows(idx)
                    Dim included As Boolean = _preselect.Count = 0 OrElse _preselect.Exists(Function(pp) String.Equals(pp, host, StringComparison.OrdinalIgnoreCase))
                    row.Cells("inc").Value = included
                    row.Cells("folder").Value = folderName
                    row.Cells("folder").ToolTipText = host
                    row.Cells("label").Value = If(p.Label.Trim().Length > 0, p.Label, folderName)
                    Dim profIdx As Integer = Array.IndexOf(ProfileTokens, If(p.Profile, "none"))
                    row.Cells("profile").Value = profDisplays(If(profIdx >= 0, profIdx, 0))
                    row.Cells("media").Value = MediaSummary(p.MediaTypes)
                    row.Cells("scan").Value = p.ScanSubdirectories
                    row.Cells("subitems").Value = p.ShowSubfoldersAsItems
                    row.Cells("hidden").Value = p.ShowHiddenFiles
                    row.Cells("allfiles").Value = p.AllFiles
                    row.Cells("ro").Value = p.IsReadOnly
                    row.Cells("softro").Value = p.SoftReadOnly
                    row.Cells("dest").Value = p.IsDestination
                    row.Cells("comment").Value = p.Comment
                    row.Cells("pin").Value = p.AccessPin
                    row.Cells("slide").Value = p.SlideshowInterval.ToString()
                    row.Tag = New RowState(host, folderName, p)
                Next
            End If
        Finally
            dgv.ResumeLayout()
            _loading = False
        End Try
    End Sub

    Private Shared Function MediaSummary(tokens As List(Of String)) As String
        If tokens Is Nothing OrElse tokens.Count = 0 Then Return If(Rus, "по типу", "by type")
        Dim disp As String() = MediaDisplays()
        Dim parts As New List(Of String)()
        For Each t As String In tokens
            Dim i As Integer = Array.IndexOf(MediaTokens, t)
            If i >= 0 Then parts.Add(disp(i))
        Next
        Return If(parts.Count > 0, String.Join(", ", parts), If(Rus, "по типу", "by type"))
    End Function

    ' --- cell edits -> row params ----------------------------------------------

    Private Sub OnCellValueChanged(sender As Object, e As DataGridViewCellEventArgs)
        If _loading OrElse _syncing OrElse e.RowIndex < 0 Then Return
        Dim row As DataGridViewRow = dgv.Rows(e.RowIndex)
        Dim st As RowState = TryCast(row.Tag, RowState)
        If st Is Nothing Then Return
        Dim p As ShareRootParams = st.P
        Dim name As String = dgv.Columns(e.ColumnIndex).Name

        Select Case name
            Case "label"
                p.Label = CellText(row, "label")
            Case "profile"
                Dim disp As String = CellText(row, "profile")
                Dim i As Integer = Array.IndexOf(ProfileDisplays(), disp)
                p.Profile = ProfileTokens(If(i >= 0, i, 0))
            Case "scan"
                p.ScanSubdirectories = CellBool(row, "scan")
            Case "subitems"
                p.ShowSubfoldersAsItems = CellBool(row, "subitems")
            Case "hidden"
                p.ShowHiddenFiles = CellBool(row, "hidden")
            Case "allfiles"
                p.AllFiles = CellBool(row, "allfiles")
            Case "ro"
                p.IsReadOnly = CellBool(row, "ro")
                If p.IsReadOnly Then SetCellBool(row, "dest", False) : p.IsDestination = False
            Case "softro"
                p.SoftReadOnly = CellBool(row, "softro")
            Case "dest"
                p.IsDestination = CellBool(row, "dest")
                If p.IsDestination Then SetCellBool(row, "ro", False) : p.IsReadOnly = False
            Case "comment"
                p.Comment = CellText(row, "comment")
            Case "pin"
                p.AccessPin = CellText(row, "pin")
            Case "slide"
                Dim secs As Integer
                If Integer.TryParse(CellText(row, "slide"), secs) AndAlso secs >= 0 AndAlso secs <= 3600 Then
                    p.SlideshowInterval = secs
                End If
            Case Else
                ' "inc" / "folder" / "media" have no direct scalar to sync here.
        End Select

        ScheduleRebuild()
    End Sub

    Private Sub OnCellClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
        If dgv.Columns(e.ColumnIndex).Name <> "media" Then Return
        ShowMediaPicker(dgv.Rows(e.RowIndex))
    End Sub

    ''' <summary>Media set is multi-value, so its cell is a button that drops a checkable menu.</summary>
    Private Sub ShowMediaPicker(row As DataGridViewRow)
        Dim st As RowState = TryCast(row.Tag, RowState)
        If st Is Nothing Then Return
        Dim disp As String() = MediaDisplays()
        Dim menu As New ContextMenuStrip()
        ' Keep the menu open while toggling several types (multi-select in one open); it still
        ' closes on click-outside / Escape. Without this, one toggle auto-closes the dropdown.
        AddHandler menu.Closing, Sub(s As Object, ce As ToolStripDropDownClosingEventArgs)
                                     If ce.CloseReason = ToolStripDropDownCloseReason.ItemClicked Then ce.Cancel = True
                                 End Sub
        For i As Integer = 0 To MediaTokens.Length - 1
            Dim token As String = MediaTokens(i)
            Dim item As New ToolStripMenuItem(disp(i)) With {.CheckOnClick = True, .Checked = st.P.MediaTypes IsNot Nothing AndAlso st.P.MediaTypes.Contains(token), .Tag = token}
            AddHandler item.CheckedChanged, Sub()
                                                If st.P.MediaTypes Is Nothing Then st.P.MediaTypes = New List(Of String)()
                                                If item.Checked Then
                                                    If Not st.P.MediaTypes.Contains(token) Then st.P.MediaTypes.Add(token)
                                                Else
                                                    st.P.MediaTypes.Remove(token)
                                                End If
                                                row.Cells("media").Value = MediaSummary(st.P.MediaTypes)
                                                ScheduleRebuild()
                                            End Sub
            menu.Items.Add(item)
        Next
        ' Drop the menu just under the clicked cell.
        Dim rect As Rectangle = dgv.GetCellDisplayRectangle(row.Cells("media").ColumnIndex, row.Index, False)
        menu.Show(dgv, New Point(rect.Left, rect.Bottom))
        AddHandler menu.Closed, Sub() menu.Dispose()
    End Sub

    Private Shared Function CellText(row As DataGridViewRow, name As String) As String
        Return Convert.ToString(row.Cells(name).Value).Trim()
    End Function

    Private Shared Function CellBool(row As DataGridViewRow, name As String) As Boolean
        Dim v As Object = row.Cells(name).Value
        Return TypeOf v Is Boolean AndAlso CBool(v)
    End Function

    Private Sub SetCellBool(row As DataGridViewRow, name As String, value As Boolean)
        _syncing = True
        row.Cells(name).Value = value
        _syncing = False
    End Sub

    ' --- rebuild ----------------------------------------------------------------

    Private Sub OnRebuildTick(sender As Object, e As EventArgs)
        _rebuildTimer.Stop()
        Rebuild()
    End Sub

    Private Sub ScheduleRebuild()
        _rebuildTimer.Stop()
        _rebuildTimer.Start()
    End Sub

    ''' <summary>Commits any in-edit cell and runs the debounced Rebuild NOW, so an export
    ''' action (Save/Email/QR) never ships the stale _config from before the last edit -
    ''' the click handlers run synchronously without letting the 250ms timer tick.</summary>
    Private Sub FlushPendingRebuild()
        Try
            If dgv IsNot Nothing Then dgv.EndEdit()
        Catch
        End Try
        _rebuildTimer.Stop()
        Rebuild()
    End Sub

    ''' <summary>Rebuilds the .fmscfg + QR for the CHECKED rows, each folder carrying its OWN
    ''' full edited params (perRootParams) - a truly per-recipient resource configuration.</summary>
    Private Sub Rebuild()
        If _status Is Nothing Then Return
        Dim selected As New List(Of ShareFolder)()
        Dim perRoot As New Dictionary(Of String, ShareRootParams)(StringComparer.OrdinalIgnoreCase)
        For Each row As DataGridViewRow In dgv.Rows
            If Not CellBool(row, "inc") Then Continue For
            Dim st As RowState = TryCast(row.Tag, RowState)
            If st Is Nothing Then Continue For
            Dim rf As ShareFolder = Nothing
            If _status.Roots IsNot Nothing Then
                For Each x As ShareFolder In _status.Roots
                    If String.Equals(If(x.hostPath, ""), st.HostPath, StringComparison.OrdinalIgnoreCase) Then
                        rf = x
                        Exit For
                    End If
                Next
            End If
            If rf Is Nothing Then Continue For
            selected.Add(rf)
            ' A copy so the grid's live state is not consumed by the builder; normalize the
            ' label default (blank when the user left it as the folder name).
            Dim p As ShareRootParams = st.P.Clone()
            If p.Label.Trim().Length = 0 OrElse String.Equals(p.Label.Trim(), st.FolderName, StringComparison.Ordinal) Then p.Label = ""
            If Not perRoot.ContainsKey(st.HostPath) Then perRoot.Add(st.HostPath, p)
        Next

        If selected.Count = 0 Then
            _config = Nothing
            ShowQr(Nothing)
            EnableExport(False)
            SetHint(If(Rus, "Отметьте хотя бы одну папку.", "Check at least one folder."))
            Return
        End If

        Dim snapshot As New WorkerStatus With {
            .Running = _status.Running, .ListenPort = _status.ListenPort,
            .Username = _status.Username, .Password = _status.Password,
            .Fingerprint = _status.Fingerprint, .Reachability = _status.Reachability,
            .Roots = selected}

        _config = ShareConfigBuilder.Build(snapshot, includeExternal:=Not chkLanOnly.Checked,
                                           includePassword:=Not chkNoPassword.Checked, perRootParams:=perRoot)

        If _config Is Nothing Then
            ShowQr(Nothing)
            EnableExport(False)
            SetHint(If(Rus, "Нет доступного адреса для раздачи.", "No usable address to share."))
            Return
        End If

        ShowQr(_config)
        EnableExport(True)
        btnCopyLogin.Enabled = Not String.IsNullOrEmpty(_status.Password)

        If _config.QrOverflow Then
            SetHint(If(Rus, "Код слишком большой для QR - сохраните файл .fmscfg и передайте его.", "Too large for a QR - save the .fmscfg file and share that instead."))
        ElseIf chkNoPassword.Checked AndAlso Not String.IsNullOrEmpty(_status.Password) Then
            SetHint((If(Rus, "Пароль (передайте отдельно): ", "Password (pass separately): ")) & _status.Password)
        Else
            SetHint("")
        End If
    End Sub

    Private Sub OnRebuildToggle(sender As Object, e As EventArgs)
        If _loading Then Return
        _settings.LanOnlyExport = chkLanOnly.Checked
        _settings.ExcludePasswordFromExport = chkNoPassword.Checked
        Try : _settings.Save() : Catch : End Try
        ScheduleRebuild()
    End Sub

    Private Sub OnCopyLogin(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse String.IsNullOrEmpty(_status.Password) Then Return
        Try
            Dim user As String = If(String.IsNullOrEmpty(_status.Username), "fms", _status.Username)
            Clipboard.SetText((If(Rus, "Логин: ", "Login: ") & user & Environment.NewLine) & (If(Rus, "Пароль: ", "Password: ") & _status.Password))
            SetHint(If(Rus, "Логин и пароль скопированы.", "Login and password copied."))
        Catch
        End Try
    End Sub

    Private Sub OnShowQr(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        Qr_Zoom_Form.ShowZoomed(Me, picQr)
    End Sub

    Private Sub OnSaveConfig(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        If _config Is Nothing OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Using dlg As New SaveFileDialog() With {.Filter = "FMS config (*.fmscfg)|*.fmscfg", .FileName = "FastMediaSorter.fmscfg"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dlg.FileName, _config.ConfigJson, New UTF8Encoding(False))
                SetHint(If(Rus, "Файл сохранён.", "File saved."))
            Catch
                SetHint(If(Rus, "Не удалось сохранить файл.", "Could not save the file."))
            End Try
        End Using
    End Sub

    Private Sub OnEmail(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        If _config Is Nothing OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Try
            Dim dir As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter")
            Directory.CreateDirectory(dir)
            Dim cfgFile As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(cfgFile, _config.ConfigJson, New UTF8Encoding(False))
            Dim subject As String = If(Rus, "Доступ к папкам Fast Media Sorter", "Fast Media Sorter folder access")
            Dim body As String = If(Rus, "Импортируйте вложенный файл .fmscfg в приложении FastMediaSorter на Android.",
                                         "Import the attached .fmscfg file in the FastMediaSorter Android app.")
            If Not MailSender.SendFile(cfgFile, subject, body) Then
                SetHint(If(Rus, "Не удалось открыть почтовый клиент.", "Could not open the mail client."))
            End If
        Catch
            SetHint(If(Rus, "Не удалось отправить письмо.", "Could not send the email."))
        End Try
    End Sub

    Private Sub ShowQr(cfg As ShareConfigResult)
        Dim old As Image = picQr.Image
        Dim newImg As Image = Nothing
        Try
            If cfg IsNot Nothing AndAlso cfg.QrPng IsNot Nothing Then
                Using ms As New MemoryStream(cfg.QrPng)
                    Using tmp As Image = Image.FromStream(ms)
                        newImg = New Bitmap(tmp)
                    End Using
                End Using
            End If
        Catch
            newImg = Nothing
        End Try
        picQr.Image = newImg
        If old IsNot Nothing Then old.Dispose()
        picQr.Invalidate()   ' repaint the placeholder when the code was cleared
        SetQrAvailable(newImg IsNot Nothing)
    End Sub

    ''' <summary>Switches the QR preview + button between the bright, clickable "ready" look and a
    ''' muted disabled look (no code built yet / no usable address).</summary>
    Private Sub SetQrAvailable(available As Boolean)
        btnShowQr.Enabled = available
        picQr.Cursor = If(available, Cursors.Hand, Cursors.Default)
        btnShowQr.Image = If(available, _qrGlyph, Nothing)
        If available Then
            btnShowQr.BackColor = QrAccent
            btnShowQr.ForeColor = Color.White
            btnShowQr.FlatAppearance.BorderColor = QrAccentDark
        Else
            btnShowQr.BackColor = SystemColors.Control
            btnShowQr.ForeColor = SystemColors.GrayText
            btnShowQr.FlatAppearance.BorderColor = SystemColors.ControlDark
        End If
    End Sub

    ''' <summary>Placeholder text drawn in the QR preview box until a code has been built.</summary>
    Private Sub OnQrPreviewPaint(sender As Object, e As PaintEventArgs)
        If picQr.Image IsNot Nothing Then Return
        Dim txt As String = If(Rus, "QR-код" & Environment.NewLine & "появится здесь", "QR code" & Environment.NewLine & "appears here")
        TextRenderer.DrawText(e.Graphics, txt, Me.Font, picQr.ClientRectangle, Color.Silver,
            TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
    End Sub

    ''' <summary>A tiny QR glyph (three finder squares + a few modules) for the Show-QR button,
    ''' drawn white to sit on the blue accent - matches the app's other in-code glyphs.</summary>
    Private Shared Function BuildQrGlyph() As Bitmap
        Dim bmp As New Bitmap(16, 16)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.None
            g.Clear(Color.Transparent)
            Using pen As New Pen(Color.White, 1.0F), b As New SolidBrush(Color.White)
                DrawQrFinder(g, pen, b, 0, 0)
                DrawQrFinder(g, pen, b, 9, 0)
                DrawQrFinder(g, pen, b, 0, 9)
                g.FillRectangle(b, 10, 10, 2, 2)
                g.FillRectangle(b, 13, 11, 2, 2)
                g.FillRectangle(b, 11, 13, 2, 2)
                g.FillRectangle(b, 14, 14, 2, 2)
            End Using
        End Using
        Return bmp
    End Function

    Private Shared Sub DrawQrFinder(g As Graphics, pen As Pen, brush As SolidBrush, x As Integer, y As Integer)
        g.DrawRectangle(pen, x, y, 5, 5)              ' 6x6 ring
        g.FillRectangle(brush, x + 2, y + 2, 2, 2)   ' centre dot
    End Sub

    Private Sub EnableExport(on_ As Boolean)
        btnSave.Enabled = on_
        btnEmail.Enabled = on_
    End Sub

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub HandleFormClosed(sender As Object, e As FormClosedEventArgs)
        Dim old As Image = picQr.Image
        picQr.Image = Nothing
        If old IsNot Nothing Then old.Dispose()
        Try : _rebuildTimer.Stop() : _rebuildTimer.Dispose() : Catch : End Try
        Try : toolTip.Dispose() : Catch : End Try
        Try
            If _qrGlyph IsNot Nothing Then _qrGlyph.Dispose()
        Catch
        End Try
        ShareIcons.FreeIcon(Me.Icon, _iconHandle)
    End Sub

End Class
