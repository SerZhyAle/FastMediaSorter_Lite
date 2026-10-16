Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
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

    Private ReadOnly Property _resourceContextMenu As ContextMenuStrip
        Get
            Return _diskMenu
        End Get
    End Property

    Private Function SelectedDiskRoot() As ShareFolder
        If lvFolders Is Nothing OrElse lvFolders.SelectedItems.Count = 0 Then Return Nothing
        Dim root As ShareFolder = TryCast(lvFolders.SelectedItems(0).Tag, ShareFolder)
        Return If(root IsNot Nothing AndAlso root.owner = "filedo", root, Nothing)
    End Function

    Private Sub OnDiskMenuOpening(sender As Object, e As System.ComponentModel.CancelEventArgs)
        If lvFolders Is Nothing OrElse lvFolders.SelectedItems.Count = 0 OrElse _busy Then
            e.Cancel = True
            Return
        End If
        For i As Integer = _diskMenu.Items.Count - 1 To 0 Step -1
            Dim old As ToolStripItem = _diskMenu.Items(i)
            _diskMenu.Items.RemoveAt(i)
            old.Image?.Dispose()
            old.Dispose()
        Next

        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim sf As ShareFolder = TryCast(it.Tag, ShareFolder)

        If sf IsNot Nothing AndAlso sf.owner = "filedo" Then
            Dim disk As SharedDisk = _diskSnapshot.FirstOrDefault(Function(d) d.RootName = sf.name)
            If disk IsNot Nothing Then
                Dim canOpen As Boolean = DiskOperations.CanOpen(disk)
                Dim canClose As Boolean = DiskOperations.CanClose(disk)
                If canOpen Then
                    AddDiskMenuItem(Localization.T("Открыть.."), True, False, Sub() OnDiskMenuOpen(disk), "action.mount-disk", isDefault:=True)
                End If
                If canClose Then
                    AddDiskMenuItem(Localization.T("Закрыть диск"), True, False, Sub() OnDiskMenuClose(disk, False), "action.unmount-disk", isDefault:=Not canOpen)
                    AddDiskMenuItem(Localization.T("Принудительно закрыть.."), True, False, Sub() OnDiskMenuClose(disk, True), "action.unmount-disk")
                End If
                If canOpen OrElse canClose Then
                    _diskMenu.Items.Add(New ToolStripSeparator())
                End If
                AddDiskMenuItem(Localization.T("Автозапуск.."), True, disk.Autostart, Sub() OnDiskMenuAutostart(disk), "action.auto-mount")
                Dim hostDir As String = disk.MountPath
                If Not String.IsNullOrEmpty(hostDir) AndAlso System.IO.Directory.Exists(hostDir) Then
                    AddDiskMenuItem(Localization.T("Показать в Проводнике"), True, False, Sub() OpenInExplorer(hostDir), "content.folder")
                End If
                _diskMenu.Items.Add(New ToolStripSeparator())
                AddDiskMenuItem(Localization.T("Убрать"), True, False, Sub() OnDiskMenuRemove(disk), "action.remove")
                _diskMenu.Items.Add(New ToolStripSeparator())
            End If
            AddDiskMenuItem(Localization.T("Диски .fdd.."), True, False, Sub() OnDiskMenuWindow(), "content.disk-container")
        ElseIf sf IsNot Nothing AndAlso sf.owner = "broadcast" Then
            AddDiskMenuItem(Localization.T("Трансляции.."), True, False, Sub() OnBroadcastsClicked(Nothing, EventArgs.Empty), "media.cast", isDefault:=True)
            _diskMenu.Items.Add(New ToolStripSeparator())
            AddDiskMenuItem(Localization.T("Убрать"), True, False, Sub() OnRemoveFolder(Nothing, EventArgs.Empty), "action.remove")
        Else
            Dim host As String = Convert.ToString(it.Tag)
            AddDiskMenuItem(Localization.T("Редактировать"), True, False, Sub() OnConfigureFolder(Nothing, EventArgs.Empty), "action.edit", isDefault:=True)
            AddDiskMenuItem(Localization.T("Дать доступ наружу.."), True, False, Sub() OnOutsideAccessClicked(Nothing, EventArgs.Empty), "action.share")
            AddDiskMenuItem(Localization.T("Показать в Проводнике"), Not String.IsNullOrEmpty(host) AndAlso System.IO.Directory.Exists(host), False, Sub() OpenInExplorer(host), "content.folder")
            _diskMenu.Items.Add(New ToolStripSeparator())
            AddDiskMenuItem(Localization.T("Убрать"), True, False, Sub() OnRemoveFolder(Nothing, EventArgs.Empty), "action.remove")
        End If
    End Sub

    Private Shared Sub OpenInExplorer(host As String)
        Try
            If System.IO.Directory.Exists(host) Then
                Process.Start(New ProcessStartInfo("explorer.exe", $"""{host}""") With {.UseShellExecute = True})
            End If
        Catch ex As Exception
            AppFileLogger.LogException("MainWindow.OpenInExplorer", ex)
        End Try
    End Sub

    Private Function AddDiskMenuItem(caption As String, enabled As Boolean, checked As Boolean, action As Action, Optional glyphId As String = Nothing, Optional isDefault As Boolean = False) As ToolStripMenuItem
        Dim item As New ToolStripMenuItem(caption) With {.Enabled = enabled, .Checked = checked}
        If isDefault Then item.Font = New Font(item.Font, FontStyle.Bold)
        If Not String.IsNullOrEmpty(glyphId) Then
            Dim iconSize As Integer = LogicalToDeviceUnits(16)
            item.Image = GlyphRenderer.Render(glyphId, iconSize, item.ForeColor, AddressOf CompanionAssets.OpenBundledAsset)
        End If
        AddHandler item.Click, Sub(s, ev) action()
        _diskMenu.Items.Add(item)
        Return item
    End Function

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
