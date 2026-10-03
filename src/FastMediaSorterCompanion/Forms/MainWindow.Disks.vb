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
            If response.Ok AndAlso response.Disks IsNot Nothing AndAlso Not IsDisposed Then
                _diskSnapshot = response.Disks
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
                item.SubItems.Add(If(root.owner = "filedo", "FileDO" & If(disk Is Nothing, "", " - " & Share_Disks_Form.StateText(disk.State)), root.owner))
                item.SubItems.Add(root.hostPath)
                item.SubItems.Add(If(root.readOnly, HardReadOnlyText(), ""))
                lvFolders.Items.Add(item)
                If selected IsNot Nothing AndAlso root.owner = selected.owner AndAlso root.name = selected.name Then item.Selected = True
            Next
            RestripeList()
        Finally
            lvFolders.EndUpdate()
            _loading = previous
        End Try
        RefreshRowActionButtons()
    End Sub

    Private Async Sub OnDisksClicked(sender As Object, e As EventArgs)
        If Not _busy Then Await ShowDisksAsync()
    End Sub

    Private Async Function ShowDisksAsync() As Task
        _diskDialogOpen = True
        _statsTimer.Stop()
        Try
            Using dialog As New Share_Disks_Form()
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
