Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The right-click menu on a FileDO disk row of the share list: open, close, force-close, autostart,
''' remove - the verbs of the ".fdd disks.." window, one click from the row they act on. A plain folder
''' row has no menu; its verbs are the buttons above the list.
'''
''' The menu is ONE ContextMenuStrip owned by the list for the window's life, its items rebuilt for the
''' row in <see cref="OnDiskMenuOpening"/>: a menu built per click and disposed from its own Closed event
''' is what crashed the language menu with ObjectDisposedException (companion.log, 2026-10-03). It also
''' answers the Menu key and Shift+F10 for free.
''' </summary>
Partial Public NotInheritable Class MainWindow
    Private _diskMenu As ContextMenuStrip

    Private Sub BuildDiskMenu()
        _diskMenu = New ContextMenuStrip()
        AddHandler _diskMenu.Opening, AddressOf OnDiskMenuOpening
        lvFolders.ContextMenuStrip = _diskMenu
        AddHandler lvFolders.Disposed, Sub()
                                           Try : _diskMenu.Dispose() : Catch : End Try
                                       End Sub
    End Sub

    Private Function SelectedDiskRoot() As ShareFolder
        If lvFolders Is Nothing OrElse lvFolders.SelectedItems.Count = 0 Then Return Nothing
        Dim root As ShareFolder = TryCast(lvFolders.SelectedItems(0).Tag, ShareFolder)
        Return If(root IsNot Nothing AndAlso root.owner = "filedo", root, Nothing)
    End Function

    Private Sub OnDiskMenuOpening(sender As Object, e As System.ComponentModel.CancelEventArgs)
        Dim root As ShareFolder = SelectedDiskRoot()
        If root Is Nothing OrElse _busy Then
            e.Cancel = True
            Return
        End If
        For i As Integer = _diskMenu.Items.Count - 1 To 0 Step -1
            Dim old As ToolStripItem = _diskMenu.Items(i)
            _diskMenu.Items.RemoveAt(i)
            old.Dispose()
        Next
        Dim disk As SharedDisk = _diskSnapshot.FirstOrDefault(Function(d) d.RootName = root.name)
        If disk IsNot Nothing Then
            AddDiskMenuItem(Localization.T("Открыть.."), DiskOperations.CanOpen(disk), False, Sub() OnDiskMenuOpen(disk))
            AddDiskMenuItem(Localization.T("Закрыть диск"), DiskOperations.CanClose(disk), False, Sub() OnDiskMenuClose(disk, False))
            AddDiskMenuItem(Localization.T("Принудительно закрыть.."), DiskOperations.CanClose(disk), False, Sub() OnDiskMenuClose(disk, True))
            _diskMenu.Items.Add(New ToolStripSeparator())
            AddDiskMenuItem(Localization.T("Автозапуск.."), True, disk.Autostart, Sub() OnDiskMenuAutostart(disk))
            AddDiskMenuItem(Localization.T("Убрать"), True, False, Sub() OnDiskMenuRemove(disk))
            _diskMenu.Items.Add(New ToolStripSeparator())
        End If
        AddDiskMenuItem(Localization.T("Диски .fdd.."), True, False, Sub() OnDiskMenuWindow())
    End Sub

    Private Sub AddDiskMenuItem(caption As String, enabled As Boolean, checked As Boolean, action As Action)
        Dim item As New ToolStripMenuItem(caption) With {.Enabled = enabled, .Checked = checked}
        AddHandler item.Click, Sub(s, ev) action()
        _diskMenu.Items.Add(item)
    End Sub

    Private Async Sub OnDiskMenuWindow()
        If Not _busy Then Await ShowDisksAsync()
    End Sub

    Private Async Sub OnDiskMenuOpen(disk As SharedDisk)
        If _busy Then Return
        Dim password As String = Nothing
        ' Only an encrypted disk asks anything; a plain one opens at once.
        If disk.Encrypted Then
            Using ask As New DiskOptionsForm(False, True, False)
                If ask.ShowDialog(Me) <> DialogResult.OK Then Return
                password = ask.Password.Text
                ask.Password.Clear()
            End Using
        End If
        Await RunDiskActionAsync(Function() DiskOperations.OpenAsync(disk, password, ServerIsRunning()))
    End Sub

    Private Async Sub OnDiskMenuClose(disk As SharedDisk, force As Boolean)
        If _busy Then Return
        Dim question As String = If(force,
            Localization.T("Принудительно закрыть диск? Открытые файлы будут отключены, несохранённые данные могут быть потеряны."),
            Localization.T("Закрыть диск? Клиенты временно потеряют доступ к его содержимому. Открытые файлы будут ожидаться до 30 секунд."))
        If Not ConfirmDiskAction(question) Then Return
        Await RunDiskActionAsync(Function() DiskOperations.CloseAsync(disk, force))
    End Sub

    Private Async Sub OnDiskMenuRemove(disk As SharedDisk)
        If _busy Then Return
        If Not ConfirmDiskAction(Localization.T("Убрать диск из раздачи? Диск будет закрыт, автозапуск и сохранённый ключ будут удалены. Файл .fdd сохранится.")) Then Return
        Await RunDiskActionAsync(Function() DiskOperations.UnshareAsync(disk))
    End Sub

    Private Async Sub OnDiskMenuAutostart(disk As SharedDisk)
        If _busy Then Return
        If disk.Autostart Then
            If Not ConfirmDiskAction(Localization.T("Отключить автозапуск диска и удалить сохранённый ключ?")) Then Return
            Await RunDiskActionAsync(Function() DiskOperations.DisableAutostartAsync(disk))
            Return
        End If
        If Not ConfirmDiskAction(Localization.T("Включить автозапуск диска вместе с сервером? Для зашифрованного диска пароль будет сохранён в защищённом хранилище Windows на этом ПК.")) Then Return
        Dim password As String = Nothing
        If disk.Encrypted Then
            Using ask As New DiskOptionsForm(False, True, False)
                If ask.ShowDialog(Me) <> DialogResult.OK Then Return
                password = ask.Password.Text
                ask.Password.Clear()
            End Using
        End If
        Await RunDiskActionAsync(Function() DiskOperations.EnableAutostartAsync(disk, password))
    End Sub

    ''' <summary>OK-or-Cancel with Cancel the default (APP-BEHAVIOUR rule 5): every disk verb that
    ''' cuts clients off or deletes a stored key asks first.</summary>
    Private Function ConfirmDiskAction(question As String) As Boolean
        Return MessageBox.Show(Me, question, Me.Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) = DialogResult.OK
    End Function

    ''' <summary>Runs one disk operation under the window's busy state, then ALWAYS re-reads the live
    ''' state - a mutation may have half-succeeded or timed out, and the row must show what the worker
    ''' says, not what was asked. A refusal is said in a dialog, not only in the hint line: the hint is
    ''' overwritten by the next status tick.</summary>
    Private Async Function RunDiskActionAsync(operation As Func(Of Task)) As Task
        If _busy Then Return
        SetBusy(True, Localization.T("Минутку.."))
        Dim failure As String = ""
        Try
            Await operation()
        Catch ex As Exception
            failure = DiskSharing.FailureText(ex)
            AppFileLogger.LogException("MainWindow.RunDiskActionAsync", ex)
        End Try
        Try
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            If IsDisposed Then Return
            If st IsNot Nothing Then _status = st
            Await RefreshDiskSnapshotAsync()
            If Not IsDisposed Then ApplyStatusToUi()
        Catch ex As Exception
            AppFileLogger.LogException("MainWindow.RunDiskActionAsync refresh", ex)
        Finally
            If Not IsDisposed Then SetBusy(False)
        End Try
        If IsDisposed Then Return
        If failure.Trim().Length > 0 Then
            Dim text As String = Localization.TF("Операция не завершена: {0}", failure.Trim())
            SetHint(text)
            MessageBox.Show(Me, text, Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Else
            SetHint("")
        End If
    End Function
End Class
