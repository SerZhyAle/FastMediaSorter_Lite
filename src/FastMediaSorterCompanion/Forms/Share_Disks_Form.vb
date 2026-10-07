Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>File-backed DISK-SHARE management. No guessed installation paths, no
''' automatic takeover, no credential persistence in the host.</summary>
Public NotInheritable Class Share_Disks_Form
    Inherits Form

    Private ReadOnly _list As New ListView With {.Dock = DockStyle.Fill, .View = View.Details,
        .FullRowSelect = True, .MultiSelect = False, .HideSelection = False}
    Private ReadOnly _note As New Label With {.AutoSize = True, .MaximumSize = New Size(680, 0)}
    Private ReadOnly _actions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
    Private ReadOnly _timer As New Timer With {.Interval = 10000}
    Private _busy As Boolean
    Private _status As WorkerStatus
    Private _mountedFolder As String = ""
    Private _add As Button
    Private _mounted As Button
    Private _refresh As Button
    Private _manager As Button
    Private ReadOnly _managerHint As New LinkLabel With {.AutoSize = True, .Visible = False, .Margin = New Padding(0, 4, 0, 0)}
    Private _open As Button
    Private _close As Button
    Private _force As Button
    Private _remove As Button
    Private _auto As Button
    Private _exit As Button
    Private ReadOnly _layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5, .Padding = New Padding(12)}
    Public ReadOnly Property MountedFolder As String
        Get
            Return _mountedFolder
        End Get
    End Property

    Public Sub New()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Диски .fdd..")
        StartPosition = FormStartPosition.CenterParent
        MinimumSize = New Size(560, 380)
        ClientSize = New Size(740, 480)
        MinimizeBox = False
        Dim layout As TableLayoutPanel = _layout
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim top As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        _add = AddButton(top, Localization.T("Подключить .fdd.."), AddressOf OnAdd)
        _mounted = AddButton(top, Localization.T("Смонтированный диск.."), AddressOf OnMounted)
        _refresh = AddButton(top, Localization.T("Обновить"), AddressOf OnRefresh)
        ' Always offered, also while disk sharing is unavailable: that is when a disk held by FileDO
        ' has to be unmounted there before anything here can work.
        _manager = AddButton(top, Localization.T("Открыть менеджер дисков FileDO"), AddressOf OnOpenManager)
        _managerHint.Text = Localization.T("Открыть менеджер дисков FileDO")
        AddHandler _managerHint.LinkClicked, AddressOf OnOpenManager
        _list.Columns.Add(Localization.T("Название"), 170)
        _list.Columns.Add(Localization.T("Состояние"), 100)
        _list.Columns.Add(Localization.T("Владелец"), 110)
        _list.Columns.Add(Localization.T("Файл"), 280)
        AddHandler _list.SelectedIndexChanged, Sub()
                                                  UpdateActions()
                                                  UpdateDiskDetails()
                                              End Sub
        _open = AddButton(_actions, Localization.T("Открыть.."), AddressOf OnOpen)
        _close = AddButton(_actions, Localization.T("Закрыть диск"), AddressOf OnCloseDisk)
        _force = AddButton(_actions, Localization.T("Принудительно закрыть.."), AddressOf OnForceClose)
        _remove = AddButton(_actions, Localization.T("Убрать"), AddressOf OnRemove)
        _auto = AddButton(_actions, Localization.T("Автозапуск.."), AddressOf OnAutostart)
        Dim linksFlow As New FlowLayoutPanel With {
            .AutoSize = True,
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.LeftToRight,
            .Anchor = AnchorStyles.Left,
            .Margin = New Padding(0)
        }
        Dim lnkGuide As New LinkLabel With {
            .AutoSize = True,
            .Margin = New Padding(0, 0, 16, 0),
            .Text = Localization.T("Инструкция: диски FileDO")
        }
        AddHandler lnkGuide.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareLinks.FileDoLocalized(ShareLinks.FileDoDisksGuideUrl))
        Dim lnkFileDo As New LinkLabel With {
            .AutoSize = True,
            .Margin = New Padding(0),
            .Text = Localization.T("Сайт FileDO")
        }
        AddHandler lnkFileDo.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareLinks.FileDoLocalized(ShareLinks.FileDoSiteUrl))
        linksFlow.Controls.Add(lnkGuide)
        linksFlow.Controls.Add(lnkFileDo)
        ' APP-BEHAVIOUR rule 1: one no-action exit - the close box, Escape and this button are the
        ' same path. Busy work cannot be cancelled here, so FormClosing below refuses all three alike.
        _exit = New Button With {.AutoSize = True, .Text = Localization.T("Закрыть"), .Padding = New Padding(6),
            .DialogResult = DialogResult.Cancel, .Anchor = AnchorStyles.Right}
        CancelButton = _exit
        Dim footer As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .AutoSize = True,
            .Margin = New Padding(0, 6, 0, 0)}
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        footer.Controls.Add(linksFlow, 0, 0)
        footer.Controls.Add(_exit, 1, 0)
        layout.Controls.Add(top, 0, 0)
        layout.Controls.Add(_list, 0, 1)
        layout.Controls.Add(_actions, 0, 2)
        Dim noteBox As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .Margin = New Padding(0)}
        _note.Margin = New Padding(0)
        noteBox.Controls.Add(_note)
        noteBox.Controls.Add(_managerHint)
        layout.Controls.Add(noteBox, 0, 3)
        layout.Controls.Add(footer, 0, 4)
        Controls.Add(layout)
        DpiLayout.ApplyAutoScale(Me)
        AddHandler Shown, AddressOf OnRefresh
        AddHandler _timer.Tick, AddressOf OnRefresh
        AddHandler FormClosing, Sub(s, e)
                                    If _busy Then e.Cancel = True
                                End Sub
        UpdateActions()
        _timer.Start()
    End Sub

    Private Shared Function AddButton(panel As FlowLayoutPanel, caption As String, handler As EventHandler) As Button
        Dim button As New Button With {.AutoSize = True, .Text = caption, .Padding = New Padding(6)}
        AddHandler button.Click, handler
        panel.Controls.Add(button)
        Return button
    End Function

    Private Function SelectedDisk() As SharedDisk
        Return If(_list.SelectedItems.Count = 0, Nothing, TryCast(_list.SelectedItems(0).Tag, SharedDisk))
    End Function

    Private Sub UpdateActions()
        Dim disk As SharedDisk = SelectedDisk()
        Dim available As Boolean = DiskSharing.Supported(_status)
        Dim supported As Boolean = Not _busy AndAlso available
        ' APP-BEHAVIOUR rule 11: what cannot work here is hidden, not left as a row of dead buttons.
        ' The cause is the note and the way forward is Refresh (rule 6) - both stay.
        _add.Visible = available
        _actions.Visible = available
        _list.Visible = available
        _layout.RowStyles(1) = If(available, New RowStyle(SizeType.Percent, 100.0F), New RowStyle(SizeType.AutoSize))
        _add.Enabled = supported
        _mounted.Enabled = Not _busy
        _refresh.Enabled = Not _busy
        _open.Enabled = supported AndAlso DiskOperations.CanOpen(disk)
        _close.Enabled = supported AndAlso DiskOperations.CanClose(disk)
        _force.Enabled = _close.Enabled
        _remove.Enabled = supported AndAlso disk IsNot Nothing
        _auto.Enabled = supported AndAlso disk IsNot Nothing AndAlso DiskSharing.Supported(_status, "disk-autostart")
    End Sub

    Private Async Function RefreshDisksAsync() As Task
        _status = Await ShareController.GetStatusAsync()
        If IsDisposed Then Return
        If Not DiskSharing.Supported(_status) Then
            _list.Items.Clear()
            _note.Text = DiskSharing.UnavailableText(_status)
            _managerHint.Visible = DiskSharing.IsMountedByFileDo(_status?.DiskSharingError)
            Return
        End If
        _managerHint.Visible = False
        Dim response As WorkerResponse = Await DiskSharing.SendAsync(New WorkerRequest With {.type = "disk-list"})
        DiskSharing.RequireSuccess(response)
        If response.Disks Is Nothing Then Throw New IOException("Missing disk list.")
        Dim selected As String = If(SelectedDisk()?.ContainerId, "")
        _list.BeginUpdate()
        Try
            _list.Items.Clear()
            For Each disk As SharedDisk In response.Disks
                Dim item As New ListViewItem(disk.RootName) With {.Tag = disk}
                item.SubItems.Add(StateText(disk.State))
                item.SubItems.Add(HolderText(disk.Holder))
                item.SubItems.Add(disk.ContainerPath)
                _list.Items.Add(item)
                If disk.ContainerId = selected Then item.Selected = True
            Next
        Finally
            _list.EndUpdate()
        End Try
        If _list.SelectedItems.Count = 0 AndAlso _list.Items.Count > 0 Then _list.Items(0).Selected = True
        DiskDriveLetters.Reconcile(response.Disks)
        UpdateDiskDetails(True)
    End Function

    Private Sub UpdateDiskDetails(Optional refreshing As Boolean = False)
        If _busy AndAlso Not refreshing Then Return
        _note.Text = Localization.T("Диски доступны по SFTP. Буква диска действует только в текущем сеансе Windows. Автозапуск настраивается отдельно.")
        If SelectedDisk() IsNot Nothing Then
            Dim disk As SharedDisk = SelectedDisk()
            _note.Text &= vbCrLf & Localization.TF("Доступ: {0}; открытых файлов: {1}; автозапуск: {2}. {3}",
                If(disk.ReadOnly, Localization.T("Только чтение"), Localization.T("Чтение и запись")), disk.OpenHandles,
                If(disk.Autostart, Localization.T("Да"), Localization.T("Нет")), DiskSharing.SafeError(disk.ErrorMessage))
        End If
    End Sub

    ''' <summary>The state column. The six states of DISK-SHARE section 16 are named; a value the host does not
    ''' know (or no value) is "Unknown" - WORKER-IPC 0.14 item D - and is never worded as an error of the disk,
    ''' which is what the catch-all branch used to say.</summary>
    Friend Shared Function StateText(state As String) As String
        Select Case state
            Case "open" : Return Localization.T("Открыт")
            Case "opening" : Return Localization.T("Открывается")
            Case "closing" : Return Localization.T("Закрывается")
            Case "closed" : Return Localization.T("Закрыт")
            Case "locked" : Return Localization.T("Заблокирован")
            Case "failed" : Return Localization.T("Ошибка")
            Case Else : Return Localization.T("Неизвестно")
        End Select
    End Function

    ''' <summary>The holder column: none, the Windows service or this session. A holder the host does not know,
    ''' or none at all in the record, is "Unknown" - NEVER "No holder": item D says unknown is never none, and a
    ''' list that shows "No" beside a disk somebody may be holding invites the mount it must not authorize.</summary>
    Friend Shared Function HolderText(holder As String) As String
        Select Case holder
            Case "service" : Return Localization.T("Служба Windows")
            Case "session" : Return Localization.T("Текущий сеанс")
            Case "none" : Return Localization.T("Нет")
            Case Else : Return Localization.T("Неизвестно")
        End Select
    End Function

    Private Async Function RunAsync(operation As Func(Of Task)) As Task
        If _busy Then Return
        _busy = True
        _note.Text = Localization.T("Минутку..")
        UseWaitCursor = True
        UpdateActions()
        Dim errorText As String = ""
        Dim heldByFileDo As Boolean
        Try
            Await operation()
        Catch ex As Exception
            errorText = DiskSharing.FailureText(ex)
            heldByFileDo = DiskSharing.IsMountedByFileDo(TryCast(ex, DiskOperationRefusedException)?.Reason)
        End Try
        ' Mutations may have partially succeeded or timed out. Always reconcile live state.
        Try
            Await RefreshDisksAsync()
        Catch ex As Exception
            errorText &= " " & DiskSharing.FailureText(ex)
        Finally
            _busy = False
            UseWaitCursor = False
            UpdateActions()
        End Try
        If errorText.Trim().Length > 0 Then _note.Text = Localization.TF("Операция не завершена: {0}", errorText.Trim())
        If heldByFileDo Then _managerHint.Visible = True
    End Function

    Private Sub OnOpenManager(sender As Object, e As EventArgs)
        Dim result As FileDoLaunchResult = FileDoDiskManager.Launch()
        If result = FileDoLaunchResult.Started Then Return
        MessageBox.Show(Me, If(result = FileDoLaunchResult.Failed, Localization.T("Не удалось запустить менеджер дисков FileDO."),
            Localization.T("Менеджер дисков FileDO не найден на этом ПК. Установите FileDO с его сайта и повторите.")),
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Async Sub OnRefresh(sender As Object, e As EventArgs)
        Await RunAsync(Function() Task.CompletedTask)
    End Sub

    Private Async Sub OnAdd(sender As Object, e As EventArgs)
        If _busy Then Return
        Using picker As New OpenFileDialog With {.Filter = "FileDO (*.fdd)|*.fdd", .CheckFileExists = True}
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            Using options As New DiskOptionsForm(True, True)
                options.RootName.Text = Path.GetFileNameWithoutExtension(picker.FileName)
                If options.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim openFailure As String = ""
                Await RunAsync(Async Function()
                    Dim response As WorkerResponse = Await DiskSharing.SendAsync(New WorkerRequest With {
                        .type = "disk-share", .action = "share", .containerPath = picker.FileName,
                        .rootName = options.RootName.Text.Trim(), .readOnly = options.ReadOnlyAccess.Checked})
                    DiskSharing.RequireSuccess(response)
                    If response.Disk Is Nothing Then Throw New IOException("Missing registered disk record.")
                    If options.OpenNow.Checked Then
                        ' The disk is registered by now. A failed open must not read as a failed
                        ' registration - and must not hide in the small note either: a registered
                        ' but closed disk answers every phone with "no such file".
                        Try
                            Await OpenDiskAsync(response.Disk, options)
                        Catch ex As Exception
                            openFailure = DiskSharing.FailureText(ex)
                        End Try
                    End If
                End Function)
                If openFailure.Trim().Length > 0 AndAlso Not IsDisposed Then
                    MessageBox.Show(Me, Localization.TF("Диск добавлен в раздачу, но не открылся: {0}", openFailure.Trim()),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            End Using
        End Using
    End Sub

    Private Async Sub OnOpen(sender As Object, e As EventArgs)
        Dim disk As SharedDisk = SelectedDisk()
        If _busy OrElse disk Is Nothing Then Return
        Using options As New DiskOptionsForm(False, disk.Encrypted)
            If options.ShowDialog(Me) <> DialogResult.OK Then Return
            Await RunAsync(Function() OpenDiskAsync(disk, options))
        End Using
    End Sub

    Private Async Function OpenDiskAsync(disk As SharedDisk, options As DiskOptionsForm) As Task
        Dim opened As SharedDisk
        Try
            opened = Await DiskOperations.OpenAsync(disk, options.Password.Text, _status.Running)
        Finally
            options.Password.Clear()
        End Try
        If options.Letter.SelectedIndex > 0 Then DiskDriveLetters.Attach(opened, CStr(options.Letter.SelectedItem))
    End Function

    Private Async Sub OnCloseDisk(sender As Object, e As EventArgs)
        Await CloseDiskAsync(False)
    End Sub
    Private Async Sub OnForceClose(sender As Object, e As EventArgs)
        Await CloseDiskAsync(True)
    End Sub
    Private Async Function CloseDiskAsync(force As Boolean) As Task
        Dim disk As SharedDisk = SelectedDisk()
        If _busy OrElse disk Is Nothing Then Return
        Dim question As String = If(force,
            Localization.T("Принудительно закрыть диск? Открытые файлы будут отключены, несохранённые данные могут быть потеряны."),
            Localization.T("Закрыть диск? Клиенты временно потеряют доступ к его содержимому. Открытые файлы будут ожидаться до 30 секунд."))
        If Not Confirm(question) Then Return
        Await RunAsync(Function() DiskOperations.CloseAsync(disk, force))
    End Function

    Private Async Sub OnRemove(sender As Object, e As EventArgs)
        Dim disk As SharedDisk = SelectedDisk()
        If _busy OrElse disk Is Nothing OrElse Not Confirm(Localization.T("Убрать диск из раздачи? Диск будет закрыт, автозапуск и сохранённый ключ будут удалены. Файл .fdd сохранится.")) Then Return
        Await RunAsync(Function() DiskOperations.UnshareAsync(disk))
    End Sub

    Private Async Sub OnAutostart(sender As Object, e As EventArgs)
        Dim disk As SharedDisk = SelectedDisk()
        If _busy OrElse disk Is Nothing Then Return
        If disk.Autostart Then
            If Not Confirm(Localization.T("Отключить автозапуск диска и удалить сохранённый ключ?")) Then Return
            Await RunAsync(Function() DiskOperations.DisableAutostartAsync(disk))
            Return
        End If
        If Not Confirm(Localization.T("Включить автозапуск диска вместе с сервером? Для зашифрованного диска пароль будет сохранён в защищённом хранилище Windows на этом ПК.")) Then Return
        Using options As New DiskOptionsForm(False, disk.Encrypted, False)
            If disk.Encrypted AndAlso options.ShowDialog(Me) <> DialogResult.OK Then Return
            Await RunAsync(Async Function()
                Try
                    Await DiskOperations.EnableAutostartAsync(disk, options.Password.Text)
                Finally
                    options.Password.Clear()
                End Try
            End Function)
        End Using
    End Sub

    Private Sub OnMounted(sender As Object, e As EventArgs)
        If _busy Then Return
        Using picker As New Form With {.Text = Localization.T("Смонтированный диск.."), .StartPosition = FormStartPosition.CenterParent,
            .ClientSize = New Size(440, 130), .FormBorderStyle = FormBorderStyle.FixedDialog, .MaximizeBox = False, .MinimizeBox = False}
            Dim drives As New ComboBox With {.Dock = DockStyle.Top, .DropDownStyle = ComboBoxStyle.DropDownList}
            For Each drive As DriveInfo In DriveInfo.GetDrives()
                If drive.IsReady Then drives.Items.Add(drive.Name)
            Next
            If drives.Items.Count > 0 Then drives.SelectedIndex = 0
            ' APP-BEHAVIOUR rule 1: Enter takes the recommended answer, Escape / the close box / Cancel leave.
            Dim ok As New Button With {.AutoSize = True, .Text = Localization.T("ОК"), .Padding = New Padding(6), .DialogResult = DialogResult.OK}
            Dim cancel As New Button With {.AutoSize = True, .Text = Localization.T("Отмена"), .Padding = New Padding(6), .DialogResult = DialogResult.Cancel}
            Dim row As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .FlowDirection = FlowDirection.LeftToRight}
            row.Controls.AddRange(New Control() {ok, cancel})
            picker.Controls.Add(drives)
            picker.Controls.Add(row)
            picker.AcceptButton = ok
            picker.CancelButton = cancel
            UiLanguage.ApplyTo(picker)
            DpiLayout.ApplyAutoScale(picker)
            If picker.ShowDialog(Me) <> DialogResult.OK OrElse drives.SelectedItem Is Nothing Then Return
            _mountedFolder = CStr(drives.SelectedItem)
            Close()
        End Using
    End Sub

    Private Function Confirm(question As String) As Boolean
        Return MessageBox.Show(Me, question, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) = DialogResult.OK
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        ScaleColumns()
        ' A child's Visible = False set while the window was never shown is not kept (the getter already
        ' answers False), so the first state - nothing known yet, nothing offered - is applied here.
        UpdateActions()
    End Sub

    Protected Overrides Sub OnDpiChanged(e As DpiChangedEventArgs)
        MyBase.OnDpiChanged(e)
        ScaleColumns()
    End Sub

    Private Sub ScaleColumns()
        Dim widths As Integer() = {170, 100, 110, 280}
        For i As Integer = 0 To widths.Length - 1
            _list.Columns(i).Width = CInt(widths(i) * DeviceDpi / 96.0)
        Next
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _timer.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

Friend NotInheritable Class DiskOptionsForm
    Inherits Form
    Public ReadOnly RootName As New TextBox With {.Width = 330}
    Public ReadOnly Password As New TextBox With {.Width = 330, .UseSystemPasswordChar = True}
    Public ReadOnly ReadOnlyAccess As New CheckBox With {.AutoSize = True, .Text = Localization.T("Только чтение")}
    Public ReadOnly OpenNow As New CheckBox With {.AutoSize = True, .Text = Localization.T("Открыть после регистрации"), .Checked = True}
    Public ReadOnly Letter As New ComboBox With {.Width = 330, .DropDownStyle = ComboBoxStyle.DropDownList}

    Public Sub New(register As Boolean, encrypted As Boolean, Optional showLetter As Boolean = True)
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Подключить .fdd..")
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Dim layout As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .AutoSize = True, .Padding = New Padding(12)}
        If register Then
            layout.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Название")})
            layout.Controls.Add(RootName)
            layout.Controls.Add(ReadOnlyAccess)
            layout.Controls.Add(OpenNow)
            AddHandler OpenNow.CheckedChanged, Sub()
                                                  Password.Enabled = OpenNow.Checked
                                                  Letter.Enabled = OpenNow.Checked
                                              End Sub
        End If
        If encrypted Then
            layout.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Пароль")})
            layout.Controls.Add(Password)
        End If
        Letter.Items.Add(Localization.T("Без буквы - только шаринг"))
        For Each drive As String In DiskDriveLetters.FreeLetters()
            Letter.Items.Add(drive)
        Next
        Letter.SelectedIndex = 0
        If showLetter Then
            layout.Controls.Add(New Label With {.AutoSize = True, .Text = Localization.T("Буква в текущем сеансе Windows")})
            layout.Controls.Add(Letter)
        End If
        layout.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(390, 0),
            .Text = Localization.T("Если диск уже открыт в FileDO, сохраните изменения и закройте его там перед подключением.")})
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        Dim ok As New Button With {.AutoSize = True, .Text = Localization.T("ОК"), .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.AutoSize = True, .Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel}
        buttons.Controls.AddRange(New Control() {ok, cancel})
        layout.Controls.Add(buttons)
        Controls.Add(layout)
        AcceptButton = ok
        CancelButton = cancel
        DpiLayout.ApplyAutoScale(Me)
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then Password.Clear()
        MyBase.Dispose(disposing)
    End Sub
End Class
