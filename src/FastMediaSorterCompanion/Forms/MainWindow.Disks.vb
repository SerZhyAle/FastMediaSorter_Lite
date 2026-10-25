Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public NotInheritable Class MainWindow
    Private _diskSnapshot As New List(Of SharedDisk)()
    Private _diskDialogOpen As Boolean

    Private Async Function RefreshDiskSnapshotAsync() As Task
        If Not DiskSharing.Supported(_status) Then Return
        Try
            Dim response As WorkerResponse = Await DiskSharing.SendAsync(New WorkerRequest With {.type = "disk-list"})
            ' An absent "disks" on an ok answer is an empty list (zero disks shared), not a failure:
            ' keeping the previous snapshot then would show disks that are no longer shared.
            Dim disks As List(Of SharedDisk) = DiskSharing.DisksOf(response)
            If disks IsNot Nothing AndAlso Not IsDisposed Then
                _diskSnapshot = disks
                DiskDriveLetters.Reconcile(_diskSnapshot)
            End If
        Catch
            ' Keep the last snapshot and aliases on transport failure. Only live state
            ' can authorize removing a drive alias.
        End Try
    End Function
    Private Function HasExternalRoots() As Boolean
        Return _status?.Roots IsNot Nothing AndAlso _status.Roots.Any(Function(r) Not r.IsFmsOwned)
    End Function

    Private Sub RefreshExternalRoots()
        If lvFolders Is Nothing OrElse _status?.Roots Is Nothing Then Return
        Dim previous As Boolean = _loading
        Dim selected As ShareFolder = If(lvFolders.SelectedItems.Count > 0, TryCast(lvFolders.SelectedItems(0).Tag, ShareFolder), Nothing)
        _loading = True
        lvFolders.BeginUpdate()
        Try
            For i As Integer = lvFolders.Items.Count - 1 To 0 Step -1
                If TypeOf lvFolders.Items(i).Tag Is ShareFolder Then lvFolders.Items.RemoveAt(i)
            Next
            For Each root As ShareFolder In _status.Roots
                If root.IsFmsOwned Then Continue For
                Dim item As New ListViewItem(root.name) With {.Tag = root, .Checked = True}
                Dim disk As SharedDisk = _diskSnapshot.FirstOrDefault(Function(d) d.RootName = root.name)
                item.SubItems.Add(If(root.owner = "filedo", DiskOwnerText(disk, DiskSharing.Supported(_status, "disk-autostart")), root.owner))
                item.SubItems.Add(root.hostPath)
                item.SubItems.Add(If(root.readOnly, HardReadOnlyText(), ""))
                lvFolders.Items.Add(item)
                If selected IsNot Nothing AndAlso root.owner = selected.owner AndAlso root.name = selected.name Then item.Selected = True
            Next
            Dim ownerWidth As Integer = LogicalToDeviceUnits(130)
            For Each row As ListViewItem In lvFolders.Items
                If row.SubItems.Count > 1 Then ownerWidth = Math.Max(ownerWidth,
                    TextRenderer.MeasureText(row.SubItems(1).Text, lvFolders.Font).Width + LogicalToDeviceUnits(24))
            Next
            lvFolders.Columns(1).Width = ownerWidth
            RestripeList()
        Finally
            lvFolders.EndUpdate()
            _loading = previous
        End Try
        RefreshRowActionButtons()
        OnResourceRowsChanged()
    End Sub

    Friend Shared Function DiskOwnerText(disk As SharedDisk, autostartSupported As Boolean) As String
        If disk Is Nothing Then Return "FileDO"
        Dim state As String = Share_Disks_Form.StateText(disk.State)
        If autostartSupported AndAlso disk.Autostart Then Return Localization.TF("FileDO - {0} - автозапуск", state)
        Return Localization.TF("FileDO - {0}", state)
    End Function

    Private Async Sub OnDisksClicked(sender As Object, e As EventArgs)
        If Not _busy Then Await ShowDisksAsync()
    End Sub

    Private Async Function ShowDisksAsync() As Task
        _diskDialogOpen = True
        _statsTimer.Stop()
        Try
            Using dialog As New Share_Disks_Form()
                AddHandler dialog.SnapshotChanged, Sub(status, disks)
                                                      _status = status
                                                      _diskSnapshot = disks
                                                      If Not IsDisposed Then ApplyStatusToUi()
                                                  End Sub
                dialog.ShowDialog(Me)
                If dialog.MountedFolder.Length > 0 Then Await AddFolderInteractive(dialog.MountedFolder)
            End Using
            _status = Await ShareController.GetStatusAsync()
            Await RefreshDiskSnapshotAsync()
            If Not IsDisposed Then ApplyStatusToUi()
        Finally
            _diskDialogOpen = False
            If Not IsDisposed Then _statsTimer.Start()
        End Try
    End Function
End Class
