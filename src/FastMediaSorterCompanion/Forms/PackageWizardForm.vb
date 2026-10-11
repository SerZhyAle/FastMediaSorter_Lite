Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' Companion package wizard - LEVEL 2 of the two-wizard model (§4.5): a ONE-SHOT act of
''' giving a specific recipient access. The resources are shown as a real editable GRID
''' (DataGridView): one row per served folder, one column per resource parameter. Click any
''' cell to change it (checkbox / dropdown / media-set popup / text / number). Cells start
''' from each folder's stored defaults; edits are PER-RECIPIENT and one-shot (§4.5.3) - they
''' ride only in this export (perRootParams) and never touch the folder's own defaults. Below
''' the grid: common per-export settings, then a single action-button row led by "Show QR code"
''' (which opens the code full-size in its own window - there is no inline preview, it was too
''' small for a phone camera). AutoScaleMode.Font + app-wide default font so it scales at any DPI.
''' </summary>
Public NotInheritable Class PackageWizardForm
    Inherits Form
    Implements IThemeAware

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        If btnShowQr Is Nothing Then Return
        Dim previous As Image = _qrGlyph
        _qrGlyph = BuildQrGlyph(LogicalToDeviceUnits(16), palette.AccentInk)
        SetQrAvailable(btnShowQr.Enabled)
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

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
    Private btnShowQr As Button
    Private btnCopyLogin As Button
    Private btnSave As Button
    Private btnEmail As Button
    Private btnClose As Button
    Private lblHint As Label
    ''' <summary>Always-visible: the code and the file are a key (FMSCFG rule 6, ticket 034 phase 1).</summary>
    Private lblSecret As Label
    Private toolTip As ToolTip
    Private _qrGlyph As Image
    Private _qrImage As Image   ' the current QR bitmap - opened enlarged on demand (no inline preview)
    Private _fontBold As Font   ' one bold face shared by the two section headers and the QR button - owned here, disposed in HandleFormClosed
    ' Access preflight. A code is a promise that the folders behind it open, and until
    ' the 2026-09-01 incident nothing here checked: the share root was readable, so the
    ' wizard happily produced a code for a tree whose subfolders the serving account
    ' could not list, and the only symptom appeared on the phone. The scan runs off the
    ' UI thread and its result is cached per SELECTION, so toggling the export options
    ' does not re-walk the disk; _accessGen drops the answer of a scan whose selection
    ' has already moved on (the generation guard used elsewhere in the app).
    '
    ' WHO may be handed the code is PreflightTracker's decision (108 H02): a verdict belongs to
    ' the selection the scan looked at, a payload to a revision, and pending / timed-out /
    ' failed / cancelled are never permission. This form only carries it out.
    Private ReadOnly _preflight As New PreflightTracker()
    Private _accessScan As FolderAccess.SubtreeScan
    Private _exportServiceAccount As Boolean
    Private _scanTicket As Integer
    Private btnRetryCheck As Button
    ''' <summary>The first scan keeps the 2 s budget; an explicit "check again" gets room for a big tree.</summary>
    Private _retryBudget As Boolean

    Public Sub New(preselect As List(Of String))
        _preselect = If(preselect, New List(Of String)())
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

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
        Return New String() {
            Localization.T("Обычная папка"), Localization.T("Аудиотека"), Localization.T("Видеотека"),
            Localization.T("Фотохранилище"), Localization.T("Документы"), Localization.T("Все файлы")}
    End Function

    Private Shared Function MediaDisplays() As String()
        ' GIF/PDF/EPUB/Office are the same token everywhere - nothing to translate.
        Return New String() {
            Localization.T("Изображения"), Localization.T("Видео"), Localization.T("Аудио"), "GIF",
            Localization.T("Текст"), "PDF", "EPUB", "Office"}
    End Function

    Private Sub BuildUi()
        ' Script font + text direction for the active language, before any control
        ' exists - children inherit both (013_SPECIFICATION_THIRTEEN_UI_LANGUAGES.md block A').
        UiLanguage.ApplyTo(Me)
        _fontBold = New Font(Me.Font, FontStyle.Bold)
        Me.Text = Localization.T("Поделиться - код доступа")
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = True
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
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
        topBar.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0), .Font = _fontBold,
            .Text = Localization.T("Папки и параметры этого кода доступа")})
        topBar.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 0), .ForeColor = Color.DimGray,
            .Text = Localization.T("Нажмите на любую ячейку, чтобы изменить значение. Значения взяты из настроек папки; правки - только для этого кода.")})

        ' The resource grid (fill).
        BuildGrid()

        ' Below the grid: common per-export settings (full width), then ONE action-button row.
        Dim leftCol As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0)}
        leftCol.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4), .Font = _fontBold, .Text = Localization.T("Общие настройки передачи:")})
        ' Per-CODE only (ticket 054 H-06): this shapes what THIS export carries and nothing
        ' else. It used to be worded - and wired - as the server-wide "LAN only", while
        ' silently acting as one at the next resume; the enforcing switch is its own,
        ' pushed the moment it moves, in Settings - Сеть.
        chkLanOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = Localization.T("Не включать адрес из интернета в этот код")}
        chkNoPassword = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 6), .Text = Localization.T("Не включать пароль в файл/QR")}
        AddHandler chkLanOnly.CheckedChanged, AddressOf OnRebuildToggle
        AddHandler chkNoPassword.CheckedChanged, AddressOf OnRebuildToggle
        toolTip.SetToolTip(chkLanOnly, Localization.T("Относится только к этому коду: телефон будет подключаться по локальной сети. Сам сервер это не выключает - доступ из интернета для всей раздачи закрывается отдельно: Настройки менеджера - Сеть."))
        ' No promise about what the phone does with an empty password: the importer's
        ' answer is still open (ticket 034 phase 3, FMSCFG registry exception).
        toolTip.SetToolTip(chkNoPassword, Localization.T("Пароль не попадёт в файл/QR - передайте его отдельно. Примет ли телефон код без пароля, зависит от версии приложения."))
        ' FMSCFG rule 6: the payload is a secret. Said once, always visible, next to the
        ' toggles that decide what the secret contains (ticket 034 phase 1).
        lblSecret = New Label With {.AutoSize = True, .MaximumSize = New Size(520, 0), .ForeColor = Color.Firebrick, .Margin = New Padding(0, 2, 0, 2)}
        leftCol.Controls.AddRange(New Control() {chkLanOnly, chkNoPassword, lblSecret})
        CheckBoxLayout.BindHint(chkNoPassword, lblSecret)
        ' NB: the LAN address + host-key fingerprint are deliberately NOT shown here. The
        ' package embeds ALL access paths (LAN + IPv6 + internet), so showing only the LAN
        ' address misleads the user into thinking the code is local-only; those details already
        ' live (copyable) in the main window's server panel.

        lblHint = New Label With {.AutoSize = True, .MaximumSize = New Size(980, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 8, 0, 4)}

        ' Single action row: the bright "Show QR code" button leads (there is no inline preview -
        ' it was far too small for a phone camera; the code opens big in its own window on click),
        ' then the export/close actions - all on one line. It sits to the RIGHT of the common
        ' settings (see bottomBar), filling that otherwise-empty band. Anchored Left+Right so it
        ' spans the right column (and can wrap when the window is narrow) while staying vertically
        ' centred against the taller settings block.
        _qrGlyph = BuildQrGlyph(LogicalToDeviceUnits(16), ThemeService.Palette.AccentInk)
        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = True, .Margin = New Padding(0, 4, 0, 0)}
        btnShowQr = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Font = _fontBold, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 12, 5),
            .Text = Localization.T("Показать QR-код"), .MinimumSize = New Size(128, 0),
            .ImageAlign = ContentAlignment.MiddleLeft, .TextImageRelation = TextImageRelation.ImageBeforeText,
            .TextAlign = ContentAlignment.MiddleCenter, .FlatStyle = FlatStyle.Flat}
        btnShowQr.FlatAppearance.BorderSize = 1
        AddHandler btnShowQr.Click, AddressOf OnShowQr
        btnCopyLogin = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Скопировать логин/пароль"), .Enabled = False}
        btnSave = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Сохранить файл .fmscfg.."), .Enabled = False}
        btnEmail = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Отправить по почте.."), .Enabled = False}
        btnClose = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 0, 2), .Padding = New Padding(16, 5, 16, 5), .Text = Localization.T("Закрыть"), .DialogResult = DialogResult.Cancel}
        btnRetryCheck = New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 8, 2), .Padding = New Padding(10, 5, 10, 5), .Text = Localization.T("Проверить доступ снова"), .Visible = False}
        ButtonGlyphs.Apply(btnCopyLogin, "action.copy")
        ButtonGlyphs.Apply(btnSave, "action.export")
        ButtonGlyphs.Apply(btnEmail, "action.email")
        ButtonGlyphs.Apply(btnRetryCheck, "action.test-connection")
        ButtonGlyphs.Apply(btnClose, "nav.close", ButtonGlyphRole.Dismissing)
        AddHandler btnRetryCheck.Click, AddressOf OnRetryCheck
        AddHandler btnCopyLogin.Click, AddressOf OnCopyLogin
        AddHandler btnSave.Click, AddressOf OnSaveConfig
        AddHandler btnEmail.Click, AddressOf OnEmail
        btnRow.Controls.AddRange(New Control() {btnShowQr, btnRetryCheck, btnCopyLogin, btnSave, btnEmail, btnClose})
        SetQrAvailable(False)

        ' Bottom band: common per-export settings on the LEFT, the action-button row filling the
        ' empty space to their RIGHT (instead of a separate full-width row underneath - that saves
        ' vertical space and gives the grid more room). The status line spans below both columns.
        Dim bottomBar As New TableLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .ColumnCount = 2, .RowCount = 2, .Padding = New Padding(16, 6, 16, 12)}
        bottomBar.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))          ' left: common settings (sizes to content)
        bottomBar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))   ' right: action buttons fill the rest
        bottomBar.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        bottomBar.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        bottomBar.Controls.Add(leftCol, 0, 0)
        bottomBar.Controls.Add(btnRow, 1, 0)
        bottomBar.Controls.Add(lblHint, 0, 1)
        bottomBar.SetColumnSpan(lblHint, 2)

        ' Add Fill FIRST (docked last => fills the leftover), then the edges.
        Me.Controls.Add(dgv)
        Me.Controls.Add(topBar)
        Me.Controls.Add(bottomBar)
        Me.CancelButton = btnClose
        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout

        _loading = True
        chkLanOnly.Checked = _settings.LanOnlyExport
        chkNoPassword.Checked = _settings.ExcludePasswordFromExport
        _loading = False
        UpdateSecretLine()

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

        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "inc", .HeaderText = Localization.T("Вкл")})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "folder", .HeaderText = Localization.T("Папка"), .[ReadOnly] = True, .MinimumWidth = 120})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "label", .HeaderText = Localization.T("Имя на телефоне"), .MinimumWidth = 130})

        Dim colProfile As New DataGridViewComboBoxColumn With {.Name = "profile", .HeaderText = Localization.T("Тип"), .FlatStyle = FlatStyle.Flat, .MinimumWidth = 130}
        colProfile.Items.AddRange(DirectCast(ProfileDisplays(), Object()))
        dgv.Columns.Add(colProfile)

        dgv.Columns.Add(New DataGridViewButtonColumn With {.Name = "media", .HeaderText = Localization.T("Типы медиа"), .UseColumnTextForButtonValue = False, .FlatStyle = FlatStyle.Standard, .MinimumWidth = 120})

        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "scan", .HeaderText = Localization.T("Скан подпапок")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "subitems", .HeaderText = Localization.T("Подпапки как элементы")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "hidden", .HeaderText = Localization.T("Скрытые")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "allfiles", .HeaderText = Localization.T("Все файлы")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "ro", .HeaderText = Localization.T("Только чтение")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "softro", .HeaderText = Localization.T("RO-подсказка")})
        dgv.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = "dest", .HeaderText = Localization.T("Приёмник")})

        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "comment", .HeaderText = Localization.T("Комментарий"), .MinimumWidth = 140})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "pin", .HeaderText = "PIN", .MinimumWidth = 70})
        dgv.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "slide", .HeaderText = Localization.T("Слайд-шоу, сек"), .MinimumWidth = 80})

        ' Hard read-only and "destination" are what the SERVER enforces for the folder, set
        ' once per shared folder in the main window - the one shared login in a code cannot
        ' be restricted per code (spec §4.5.3). Shown here, never edited: ticking "read only"
        ' for one recipient used to change only what the code SAID, while that recipient could
        ' still write (ticket 054 H-07). A per-code restriction is the soft hint next to it.
        dgv.Columns("ro").[ReadOnly] = True
        dgv.Columns("dest").[ReadOnly] = True

        ' Header tooltips for the less-obvious columns.
        dgv.Columns("ro").ToolTipText = Localization.T("Сервер запрещает изменения (настоящий запрет).")
        dgv.Columns("softro").ToolTipText = Localization.T("Приложение спрячет кнопки изменения, но сервер запись не запрещает. На папку-получатель не действует.")
        dgv.Columns("dest").ToolTipText = Localization.T("Папка-получатель: в неё можно копировать/переносить с телефона (делает папку доступной на запись).")
        dgv.Columns("media").ToolTipText = Localization.T("Нажмите, чтобы выбрать точный набор типов. Пусто = решает тип.")

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
        SetHint(Localization.T("Получение состояния.."))
        _status = Await ShareController.GetStatusAsync()
        ' The user may have closed the modal wizard during the pipe round-trip - the form
        ' (and its grid/timer) is then disposed, so bail before touching any control.
        If IsDisposed OrElse Disposing Then Return
        If _status Is Nothing OrElse Not _status.Running Then
            ' A worker on a newer protocol answered - it may well be serving, and "not
            ' running" would be the wrong sentence (WORKER-IPC section 6 rule 3).
            Dim refusal As String = If(_status Is Nothing, ShareController.RefusalMessage(), "")
            SetHint(If(refusal <> "", refusal, Localization.T("Сервер не запущен.")))
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
                    SyncSoftReadOnlyCell(row, p.IsDestination)
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
        If tokens Is Nothing OrElse tokens.Count = 0 Then Return Localization.T("по типу")
        Dim disp As String() = MediaDisplays()
        Dim parts As New List(Of String)()
        For Each t As String In tokens
            Dim i As Integer = Array.IndexOf(MediaTokens, t)
            If i >= 0 Then parts.Add(disp(i))
        Next
        Return If(parts.Count > 0, String.Join(", ", parts), Localization.T("по типу"))
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
                SyncSoftReadOnlyCell(row, p.IsDestination)
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

    ''' <summary>A destination ignores the soft read-only hint (ShareRootParams.AdvertisedReadOnly,
    ''' ticket 034 phase 2), so its cell is greyed and locked while "destination" is set. The
    ''' value is kept, not cleared - the stored hint comes back if the folder stops being one.</summary>
    Private Shared Sub SyncSoftReadOnlyCell(row As DataGridViewRow, isDestination As Boolean)
        Dim cell As DataGridViewCell = row.Cells("softro")
        cell.ReadOnly = isDestination
        cell.Style.ForeColor = If(isDestination, ThemeService.Palette.TextMuted, Color.Empty)
        cell.Style.BackColor = If(isDestination, ThemeService.Palette.SurfaceWindow, Color.Empty)
        cell.ToolTipText = If(isDestination, Localization.T("На папку-получатель не действует: она всегда доступна на запись."), "")
    End Sub

    ' --- rebuild ----------------------------------------------------------------

    Private Sub OnRebuildTick(sender As Object, e As EventArgs)
        _rebuildTimer.Stop()
        Rebuild()
    End Sub

    Private Sub ScheduleRebuild()
        _preflight.InvalidatePayload()
        _preflight.Cancel()
        Threading.Volatile.Write(_scanTicket, 0)
        AccessProbes.CancelOwner(Me)
        ShowQr(Nothing)
        RefreshOfferState()
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

        ' Every edit is a new revision: the previous payload is void from this line on, whatever
        ' the check says about the folders (108 H02).
        _preflight.Edit(AccessKeyOf(selected) & System.Text.Json.JsonSerializer.Serialize(perRoot) &
                        "|lan=" & chkLanOnly.Checked.ToString() & "|password=" & chkNoPassword.Checked.ToString())
        _exportServiceAccount = ServerFeatures.HostMode() = ServerFeatures.ServerHostMode.SystemService

        If selected.Count = 0 Then
            _config = Nothing
            ShowQr(Nothing)
            RefreshOfferState()
            SetHint(Localization.T("Отметьте хотя бы одну папку."))
            Return
        End If

        Dim snapshot As New WorkerStatus With {
            .Running = _status.Running, .ListenPort = _status.ListenPort,
            .Username = _status.Username, .Password = _status.Password,
            .Fingerprint = _status.Fingerprint, .Reachability = _status.Reachability,
            .Roots = selected}

        ' Tickets 148 and 170: the relay port and the tunnel path of the first selected share that has a live outsider
        ' link in the exchange account. Nothing with no agent, no account or no link: the export is then
        ' byte-identical to what it was (ANYWHERE-ACCESS section 8.5).
        _config = ShareConfigBuilder.Build(snapshot, includeExternal:=Not chkLanOnly.Checked,
                                           includePassword:=Not chkNoPassword.Checked, perRootParams:=perRoot,
                                           exchange:=ProducerExport(snapshot))

        If _config Is Nothing Then
            ShowQr(Nothing)
            RefreshOfferState()
            SetHint(Localization.T("Нет доступного адреса для раздачи."))
            Return
        End If

        _preflight.PayloadBuilt()
        ShowQr(_config)
        ' The check is started (or joined, or its verdict reused) and every surface is gated by the
        ' ONE decision: nothing is offered while it is pending (spec 057.S22-4).
        BeginAccessCheck(selected)
        RefreshOfferState()
        btnCopyLogin.Enabled = Not String.IsNullOrEmpty(_status.Password)

        If PreflightHint().Length > 0 Then
            SetHint(PreflightHint())
        ElseIf _config.QrOverflow Then
            SetHint(Localization.T("Код слишком большой для QR - сохраните файл .fmscfg и передайте его."))
        ElseIf chkNoPassword.Checked AndAlso Not String.IsNullOrEmpty(_status.Password) Then
            SetHint(Localization.TF("Пароль (передайте отдельно): {0}", _status.Password))
        Else
            SetHint("")
        End If
    End Sub

    ' --- access preflight -------------------------------------------------------

    ''' <summary>
    ''' Starts - or joins, or reuses the verdict of - the access check for the current selection.
    ''' Handing out a code for a tree the serving account cannot read is the failure this exists to
    ''' remove: the recipient scans it, gets an empty folder, and nothing on this PC ever says why.
    ''' Run off the UI thread: the walk is bounded but far too slow for a 250 ms debounce, and a scan
    ''' for a selection that has since changed can never answer for the new one (PreflightTracker).
    ''' </summary>
    Private Sub BeginAccessCheck(selected As List(Of ShareFolder))
        _lastSelected = New List(Of ShareFolder)(selected)
        Dim id As Integer = _preflight.Begin()
        If id = 0 Then Return ' a verdict for this exact selection is in hand, or its scan is in flight
        StartScan(id, _lastSelected, If(_retryBudget, Retry_Check_Budget, Access_Check_Budget))
    End Sub

    Private _lastSelected As New List(Of ShareFolder)()

    Private Shared ReadOnly AccessProbes As New ProbeScheduler(Of FolderAccess.SubtreeScan)(
        New ProbePolicy With {.MaxActive = 2, .MaxPending = 8, .ReplacePendingOfSameOwner = True})
    Private Shared ReadOnly AccessRunner As New PreflightScanRunner(Of FolderAccess.SubtreeScan)(AccessProbes)

    Private Async Sub StartScan(id As Integer, selected As List(Of ShareFolder), budget As TimeSpan)
        Threading.Volatile.Write(_scanTicket, id)
        _accessScan = Nothing
        ' Held back, not shown-then-withdrawn: a code that appears and then turns out to be
        ' worthless may already have been scanned.
        SetHint(HostingText.CheckingAccessHint())
        Dim roots As List(Of ShareFolder) = selected.Select(Function(root) New ShareFolder With {
            .hostPath = root.hostPath, .readOnly = root.readOnly}).ToList()
        Dim serviceAccount As Boolean = _exportServiceAccount
        Try
            Dim result As ProbeResult(Of FolderAccess.SubtreeScan) = Await AccessRunner.Run(Me, id.ToString(),
                Function() FolderAccess.ScanRoots(roots, budget, forServiceAccount:=serviceAccount),
                budget, Function() Threading.Volatile.Read(_scanTicket) = id AndAlso Not IsDisposed AndAlso Not Disposing,
                Sub(ex) AppFileLogger.LogException("Package wizard: late access preflight", ex))
            If result.Failure IsNot Nothing Then AppFileLogger.LogException("Package wizard: access preflight", result.Failure)
            OnScanFinished(id, result.Value, result.Status = ProbeStatus.Faulted)
        Catch ex As Exception
            AppFileLogger.LogException("Package wizard: access preflight", ex)
            OnScanFinished(id, Nothing, True)
        End Try
    End Sub

    Private Sub OnScanFinished(id As Integer, scan As FolderAccess.SubtreeScan, faulted As Boolean)
        If Me.IsDisposed Then Return
        Dim outcome As PreflightOutcome = PreflightLogic.Judge(
            faulted, scan IsNot Nothing AndAlso scan.Completed,
            If(scan Is Nothing, 0, scan.Blocked.Count), scan IsNot Nothing AndAlso AllRootsBlocked(scan))
        If Not _preflight.Complete(id, outcome) Then Return ' stale: the selection moved on or the wizard closed
        _accessScan = scan
        RefreshOfferState()
    End Sub

    ''' <summary>The wizard gets a tighter budget than the default: it runs while someone is clicking
    ''' checkboxes. Running out of it is a TIMEOUT - not a pass; the user can ask again with room.</summary>
    Private Shared ReadOnly Access_Check_Budget As TimeSpan = TimeSpan.FromSeconds(2)
    Private Shared ReadOnly Retry_Check_Budget As TimeSpan = TimeSpan.FromSeconds(20)

    ''' <summary>The ONE question every surface asks: may the code, the file or the e-mail go out for
    ''' the current revision? Never "is the button enabled".</summary>
    Private Function CanOfferNow() As Boolean
        Return Not IsDisposed AndAlso Not Disposing AndAlso _config IsNot Nothing AndAlso _preflight.CanOffer() AndAlso
               _exportServiceAccount = (ServerFeatures.HostMode() = ServerFeatures.ServerHostMode.SystemService)
    End Function

    ''' <summary>Pure function: may the QR code be offered right now?</summary>
    Private Function MayOfferQr() As Boolean
        Return CanOfferNow() AndAlso _qrImage IsNot Nothing
    End Function

    ''' <summary>Identity of what the scan looks at: the roots, their read/write flags (a folder shared
    ''' writable needs more from the serving account than a read-only one) and the host mode - the
    ''' account that will actually serve them.</summary>
    Private Shared Function AccessKeyOf(selected As List(Of ShareFolder)) As String
        Dim sb As New StringBuilder()
        Try
            sb.Append("mode=").Append(ServerFeatures.HostMode().ToString()).Append(vbLf)
        Catch
            sb.Append("mode=?").Append(vbLf)
        End Try
        For Each r As ShareFolder In selected
            If r Is Nothing Then Continue For
            sb.Append(If(r.hostPath, "")).Append(If(r.readOnly, "|ro", "|rw")).Append(vbLf)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Applies the verdict to every surface at once: QR, save, e-mail, the retry button and
    ''' the explaining line. Nothing else may enable a handout.</summary>
    ''' <summary>The exchange part of an export (tickets 148 and 170), or Nothing: no agent, no account, publication off or no live
    ''' outsider link - then the export is byte-identical to the build before. Never throws - an export must not fail on this.</summary>
    Private Shared Function ProducerExport(status As WorkerStatus) As ExchangeExport
        Try
            Return ExchangeDeviceHost.Current?.Publisher?.ExportFor(status)
        Catch ex As Exception
            AppFileLogger.LogException("Package wizard: the exchange part of the export", ex)
            Return Nothing
        End Try
    End Function

    Private Sub RefreshOfferState()
        Dim can As Boolean = MayOfferQr()
        SetQrAvailable(can)
        EnableExport(CanOfferNow()) ' A QR overflow still permits a verified file export.
        If btnRetryCheck IsNot Nothing Then btnRetryCheck.Visible = _config IsNot Nothing AndAlso _preflight.CanRetry
        Dim hint As String = PreflightHint()
        If hint.Length > 0 Then SetHint(hint)
    End Sub

    ''' <summary>What the current state asks the user to know ("" = nothing to add).</summary>
    Private Function PreflightHint() As String
        Select Case _preflight.State
            Case PreflightState.Checking
                Return HostingText.CheckingAccessHint()
            Case PreflightState.Blocked
                Return HostingText.SubtreeNothingReadable()
            Case PreflightState.TimedOut, PreflightState.Failed, PreflightState.Cancelled
                Return Localization.T("Проверка доступа не завершена - код и файл не выдаются. Нажмите «Проверить доступ снова».")
            Case PreflightState.Allowed
                If _accessScan IsNot Nothing AndAlso _accessScan.Blocked.Count > 0 Then
                    ' Part of the tree is served: the code is real, shown with what it will NOT deliver.
                    Return HostingText.SubtreeBlockedWarning(_accessScan.Blocked.Count)
                End If
        End Select
        Return ""
    End Function

    Private Sub OnRetryCheck(sender As Object, e As EventArgs)
        If _config Is Nothing Then Return
        _retryBudget = True
        Dim id As Integer = _preflight.Retry()
        If id <> 0 Then StartScan(id, _lastSelected, Retry_Check_Budget)
        RefreshOfferState()
    End Sub
    ''' <summary>True when every selected folder is blocked at its own root - the case
    ''' where the code promises nothing at all.</summary>
    Private Function AllRootsBlocked(scan As FolderAccess.SubtreeScan) As Boolean
        If _status Is Nothing OrElse _status.Roots Is Nothing Then Return False
        Dim blocked As New HashSet(Of String)(scan.Blocked, StringComparer.OrdinalIgnoreCase)
        Dim any As Boolean = False
        For Each row As DataGridViewRow In dgv.Rows
            If Not CellBool(row, "inc") Then Continue For
            Dim st As RowState = TryCast(row.Tag, RowState)
            If st Is Nothing Then Continue For
            any = True
            If Not blocked.Contains(st.HostPath) Then Return False
        Next
        Return any
    End Function

    Private Sub OnRebuildToggle(sender As Object, e As EventArgs)
        If _loading Then Return
        _settings.LanOnlyExport = chkLanOnly.Checked
        _settings.ExcludePasswordFromExport = chkNoPassword.Checked
        Try : _settings.Save() : Catch : End Try
        UpdateSecretLine()
        ScheduleRebuild()
    End Sub

    ''' <summary>The rule-6 line: what the code carries depends on the password toggle, the fact
    ''' that it is a key does not.</summary>
    Private Sub UpdateSecretLine()
        If lblSecret Is Nothing Then Return
        lblSecret.Text = If(chkNoPassword.Checked,
            Localization.T("QR-код и файл .fmscfg открывают доступ к папкам (и содержат PIN, если он задан) - храните и передавайте их как ключ."),
            Localization.T("QR-код и файл .fmscfg содержат пароль (и PIN, если он задан) - храните и передавайте их как ключ."))
    End Sub

    Private Sub OnCopyLogin(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse String.IsNullOrEmpty(_status.Password) Then Return
        Dim user As String = If(String.IsNullOrEmpty(_status.Username), "fms", _status.Username)
        If SecureClipboard.SetSecret(Localization.TF("Логин: {0}", user) & Environment.NewLine & Localization.TF("Пароль: {0}", _status.Password)) Then
            SetHint(Localization.T("Логин и пароль скопированы."))
        Else
            SetHint(Localization.T("Не удалось скопировать в буфер обмена"))
        End If
    End Sub

    Private Sub OnShowQr(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        ' The flush may have started a NEW revision and a new check: ask the one decision again,
        ' for the current revision, rather than trusting what the button looked like a moment ago.
        If Not MayOfferQr() Then Return
        Qr_Zoom_Form.ShowImage(Me, _qrImage, QrFileBaseName())
    End Sub

    ''' <summary>A meaningful name part for the PNG the zoom window can save: the folder this
    ''' code is for, when it is for exactly one. With several folders there is no honest name,
    ''' so the plain timestamped form is used instead.</summary>
    Private Function QrFileBaseName() As String
        Dim only1 As String = Nothing
        For Each row As DataGridViewRow In dgv.Rows
            If Not CellBool(row, "inc") Then Continue For
            Dim st As RowState = TryCast(row.Tag, RowState)
            If st Is Nothing Then Continue For
            If only1 IsNot Nothing Then Return ""
            only1 = If(String.IsNullOrWhiteSpace(st.P.Label), st.FolderName, st.P.Label)
        Next
        Return If(only1, "")
    End Function

    Private Sub OnSaveConfig(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        If Not CanOfferNow() OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Dim revision As Integer = _preflight.Revision
        Dim json As String = _config.ConfigJson
        Using dlg As New SaveFileDialog() With {.Filter = "FMS config (*.fmscfg)|*.fmscfg", .FileName = "FastMediaSorter.fmscfg"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If Not CanOfferNow() OrElse revision <> _preflight.Revision Then Return
            Try
                ShareConfigBuilder.WriteConfigFile(dlg.FileName, json)
                SetHint(Localization.T("Файл сохранён."))
            Catch ex As ConfigWriteRefusedException
                ' Refused for a reason the user can act on - and never reported as saved.
                AppFileLogger.WriteLine("Package wizard: the .fmscfg was not written (" & ex.Outcome.ToString() & ")")
                Select Case ex.Outcome
                    Case ConfigWriteOutcome.RefusedNoAcls
                        SetHint(Localization.T("Эта файловая система не умеет ограничивать доступ к файлу, а в нём ключ доступа. Выберите диск с NTFS."))
                    Case ConfigWriteOutcome.RefusedUnsafeTarget
                        SetHint(Localization.T("В эту папку могут писать и удалять из неё другие пользователи, а в файле ключ доступа. Выберите папку, доступную только вам."))
                    Case Else
                        SetHint(Localization.T("Не удалось сохранить файл."))
                End Select
            Catch ex As Exception
                AppFileLogger.LogException("Package wizard: saving the .fmscfg failed", ex)
                SetHint(Localization.T("Не удалось сохранить файл."))
            End Try
        End Using
    End Sub

    ''' <summary>
    ''' Hands the code to the mail client. The attachment is a key (FMSCFG rule 6), so the body
    ''' says so, and the temporary copy lives only for the call: Simple MAPI has taken the
    ''' attachment by the time MAPISendMail returns, and a copy left in %TEMP% for good is a
    ''' plaintext credential nobody knows is there (ticket 034 phase 1). Each send gets its own
    ''' folder, so the recipient still sees the plain file name.
    ''' </summary>
    Private Sub OnEmail(sender As Object, e As EventArgs)
        FlushPendingRebuild()
        If Not CanOfferNow() OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Dim root As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastMediaSorterCompanion", "mail")
        Dim dir As String = Path.Combine(root, "mail-" & Guid.NewGuid().ToString("N"))
        Dim ownedDirectory As Boolean = False
        Try
            If Not ProtectedConfigWriter.CreatePrivateDirectory(root) Then Throw New ConfigWriteRefusedException(ConfigWriteOutcome.RefusedUnsafeTarget)
            ShareConfigBuilder.DeleteStaleConfigCopies(root)
            ' A folder only this user can touch: a shared TEMP is not a safe place for a key.
            If Not ProtectedConfigWriter.CreatePrivateDirectory(dir) Then Throw New ConfigWriteRefusedException(ConfigWriteOutcome.Failed)
            ownedDirectory = True
            Dim cfgFile As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            ShareConfigBuilder.WriteConfigFile(cfgFile, _config.ConfigJson)
            Dim subject As String = Localization.TF("Доступ к папкам {0}", ProductNames.Compact)
            Dim body As String = Localization.T("Импортируйте вложенный файл .fmscfg в приложении для Android.") &
                Environment.NewLine & Environment.NewLine &
                Localization.T("Этот файл открывает доступ к папкам: не пересылайте его и удалите после импорта.")
            If Not MailSender.SendFile(cfgFile, subject, body) Then
                SetHint(Localization.T("Не удалось открыть почтовый клиент."))
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Package wizard: preparing the e-mail failed", ex)
            SetHint(Localization.T("Не удалось отправить письмо."))
        Finally
            Try
                If ownedDirectory AndAlso Directory.Exists(dir) Then Directory.Delete(dir, recursive:=True)
            Catch
            End Try
        End Try
    End Sub

    Private Sub ShowQr(cfg As ShareConfigResult)
        Dim old As Image = _qrImage
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
        _qrImage = newImg
        If old IsNot Nothing Then old.Dispose()
        SetQrAvailable(MayOfferQr())
    End Sub

    ''' <summary>Switches the QR preview + button between the bright, clickable "ready" look and a
    ''' muted disabled look (no code built yet / no usable address).</summary>
    Private Sub SetQrAvailable(available As Boolean)
        btnShowQr.Enabled = available
        btnShowQr.Image = If(available, _qrGlyph, Nothing)
        If available Then
            btnShowQr.BackColor = ThemeService.Palette.Accent
            btnShowQr.ForeColor = ThemeService.Palette.AccentInk
            btnShowQr.FlatAppearance.BorderColor = ThemeService.Palette.Border
        Else
            btnShowQr.BackColor = SystemColors.Control
            btnShowQr.ForeColor = SystemColors.GrayText
            btnShowQr.FlatAppearance.BorderColor = ThemeService.Palette.Border
        End If
    End Sub

    ''' <summary>A tiny QR glyph (three finder squares + a few modules) for the Show-QR button,
    ''' drawn in the active accent ink - matches the app's other in-code glyphs. Drawn at
    ''' <paramref name="size"/> px (artwork authored for 16 px), since the form's auto-scaling
    ''' grows the button but never the image inside it.</summary>
    Private Shared Function BuildQrGlyph(size As Integer, ink As Color) As Bitmap
        Dim bmp As New Bitmap(size, size)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.SmoothingMode = Drawing2D.SmoothingMode.None
            g.Clear(Color.Transparent)
            g.ScaleTransform(size / 16.0F, size / 16.0F)
            Using pen As New Pen(ink, 1.0F), b As New SolidBrush(ink)
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
        ' A scan still running must never answer into a closed wizard.
        _preflight.Cancel()
        Threading.Volatile.Write(_scanTicket, 0)
        AccessProbes.CancelOwner(Me)
        Dim old As Image = _qrImage
        _qrImage = Nothing
        If old IsNot Nothing Then old.Dispose()
        Try : _rebuildTimer.Stop() : _rebuildTimer.Dispose() : Catch : End Try
        Try : toolTip.Dispose() : Catch : End Try
        Try
            If _qrGlyph IsNot Nothing Then _qrGlyph.Dispose()
        Catch
        End Try
        Try
            If _fontBold IsNot Nothing Then _fontBold.Dispose()
        Catch
        End Try
        ShareIcons.FreeIcon(Me.Icon, _iconHandle)
    End Sub

End Class
