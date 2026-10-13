Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public NotInheritable Class MainWindow
    Private _emptyResourceHint As Button
    Private _resourceAddOpen As Boolean
    Private _focusHostingSettings As Boolean
    Private _focusPublishSettings As Boolean

    Friend Async Function OpenResourceAddAsync(owner As Form, entry As ResourceAddEntry, Optional path As String = Nothing) As Task
        If _busy OrElse _resourceAddOpen OrElse ResourceAddModel.Steps(entry, ExchangeFirstRunModel.GateOpen()).Length = 0 Then Return
        _resourceAddOpen = True
        Try
            Dim currentPublish As Boolean = entry = ResourceAddEntry.Exchange AndAlso
                If(ExchangeDeviceHost.Current?.Publisher?.Settings.PublishShares(), False)
            Using wizard As New Resource_Add_Form(New ResourceAddActions(Me, entry), entry, path, currentPublish)
                wizard.ShowDialog(owner)
            End Using
        Finally
            _resourceAddOpen = False
            RefreshEmptyResourceHint()
        End Try
        Await Task.CompletedTask
    End Function

    ''' <summary>
    ''' Spec 215: the viewer's welcome wizard asked for the guided add flow on a server that is up. It opens the very
    ''' flow the Add button opens (entry E-1: it starts at "what a resource is", which is right for someone arriving
    ''' from a first-run wizard), through the same gate the button answers to: the window has finished its first entry
    ''' (<c>wake_Ready</c>) and the Add button is enabled. When it is not - no worker, a busy window, the server surface
    ''' off - the plain window stays, which is what an older Share Manager does with the same wake.
    ''' </summary>
    Friend Async Function OpenResourceAddFromWakeAsync() As Task
        If Not Await wake_Ready.Task OrElse IsDisposed Then Return
        If Not ServerFeatures.IsEnabled() OrElse Not WorkerProcess.IsAvailable() OrElse Not btnAdd.Enabled Then Return
        Await OpenResourceAddAsync(Me, ResourceAddEntry.AddButton)
    End Function

    Private Sub RefreshEmptyResourceHint()
        If _emptyResourceHint Is Nothing OrElse lvFolders Is Nothing Then Return
        _emptyResourceHint.Visible = ResourceAddModel.EmptyHint(lvFolders.Items.Count)
        _emptyResourceHint.Enabled = Not _busy AndAlso WorkerProcess.IsAvailable()
    End Sub

    Friend NotInheritable Class ResourceAddActions
        Implements IResourceAddActions
        Private ReadOnly host As MainWindow
        Private ReadOnly kind As ResourceAddEntry
        Friend Sub New(owner As MainWindow, Optional entry As ResourceAddEntry = ResourceAddEntry.AddButton)
            host = owner
            kind = entry
        End Sub
        Public ReadOnly Property CanPickDisks As Boolean Implements IResourceAddActions.CanPickDisks
            Get
                Return DiskSharing.Supported(host._status) OrElse FileDoDiskManager.Locate().Length > 0
            End Get
        End Property
        Public Function Pick(owner As Form, disk As Boolean) As String Implements IResourceAddActions.Pick
            Using dialog As New FolderBrowserDialog With {.ShowNewFolderButton = False,
                .Description = Localization.T(If(disk, "Выберите папку на уже открытом диске FileDO", "Выберите папку, которую хотите открыть на телефоне"))}
                If disk Then
                    Dim mounted = host._diskSnapshot.FirstOrDefault(Function(d) Not String.IsNullOrEmpty(d.MountPath))
                    If mounted IsNot Nothing Then dialog.SelectedPath = mounted.MountPath
                End If
                Return If(dialog.ShowDialog(owner) = DialogResult.OK, dialog.SelectedPath, "")
            End Using
        End Function
        Public Function Duplicate(path As String) As Boolean Implements IResourceAddActions.Duplicate
            If host.HasShareRow(path) Then Return True
            Return host.lvFolders.Items.Cast(Of ListViewItem)().Any(Function(item)
                Dim root = TryCast(item.Tag, ShareFolder)
                Return root IsNot Nothing AndAlso String.Equals(root.hostPath, path, StringComparison.OrdinalIgnoreCase)
            End Function)
        End Function
        Public Sub WarnNetwork(owner As Form, path As String) Implements IResourceAddActions.WarnNetwork
            If FolderAccess.NetworkRootsInServerMode({New ShareFolder With {.hostPath = path}}).Count = 0 Then Return
            MessageBox.Show(owner, HostingText.FolderNoAccessTip(), ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Sub
        Public Function Parameters(path As String) As ShareRootParams Implements IResourceAddActions.Parameters
            Dim value = ShareRootParamsStore.GetFor(path)
            If value.IsDefault() Then value.Profile = "all_files"
            Return value
        End Function
        Public Function FolderName(path As String) As String Implements IResourceAddActions.FolderName
            Return ShareFolderDisplayName(path)
        End Function
        Public Async Function CommitAsync(owner As Form, path As String, parameters As ShareRootParams, exchange As Boolean, publish As Boolean) As Task(Of ResourceAddResult) Implements IResourceAddActions.CommitAsync
            If Duplicate(path) Then Return New ResourceAddResult()
            ' User-approved atomic boundary: no store, row or worker change before Finish.
            If Not ShareRootParamsStore.TrySetFor(path, parameters) Then Return New ResourceAddResult()
            If Not host.AddShareRow(path, paramsAreConfirmed:=True) Then Return New ResourceAddResult()
            Dim result As New ResourceAddResult With {.Added = True}
            Try
                ' Withdraw a changed publication choice before the worker can expose the new folder to the watch.
                If exchange AndAlso ExchangeFirstRunModel.GateOpen() AndAlso ExchangeDeviceHost.Current?.Publisher IsNot Nothing Then
                    ExchangeDeviceHost.Current.Publisher.Settings.SetPublishShares(publish)
                End If
                Await host.ApplySharedFoldersAsync(owner, startServing:=ServerFeatures.IsEnabled())
                Await host.RefreshFolderAccessAsync()
            Catch
                ' The local row exists. Retrying Finish would duplicate the act; repair through the owning settings.
                host.SetBusy(False)
                result.Delivery = ResourceAddDelivery.NotServing
                Return result
            End Try
            result.Delivery = ResourceAddModel.Delivery(ServerFeatures.IsEnabled(), host._status IsNot Nothing AndAlso host._status.Running AndAlso host._status.Roots IsNot Nothing AndAlso
                host._status.Roots.Any(Function(r) r.IsFmsOwned AndAlso String.Equals(r.hostPath, path, StringComparison.OrdinalIgnoreCase)),
                If(host._status IsNot Nothing AndAlso host._status.Stats IsNot Nothing AndAlso host._status.Stats.TotalConnections > 0, 1, 0), host._rootsWithoutAccess = 0)
            Try
                If exchange AndAlso ExchangeFirstRunModel.GateOpen() Then
                    Dim publisher As ExchangePublisher = ExchangeDeviceHost.Current?.Publisher
                    If publisher Is Nothing Then
                        result.Publication = ExchangePublishText.StateNotConnected()
                    Else
                            Await publisher.ApplyNowAsync(CancellationToken.None)
                        Dim snapshot = publisher.Snapshot
                        result.Publication = If(Not publish, Localization.T("Публикация выключена."),
                            If(snapshot.Block <> ExchangePublishBlock.None, ExchangePublishText.StateBlocked(snapshot.Block),
                            If(Not snapshot.Connected, ExchangePublishText.StateNotConnected(),
                                ExchangePublishText.StatePublished(snapshot.Rows.Where(Function(r) r.State = ExchangePublishState.Published).Count(), snapshot.Rows.Count))))
                    End If
                End If
            Catch
                result.Publication = ExchangePublishText.StateNotConnected()
            End Try
            Return result
        End Function
        Public Sub Pair(owner As Form) Implements IResourceAddActions.Pair
            If host._status Is Nothing OrElse Not host._status.Running Then Return
            Using dialog As New Share_Status_Form(host._status)
                dialog.ShowDialog(owner)
            End Using
        End Sub
        Public Sub Settings(owner As Form) Implements IResourceAddActions.Settings
            host._focusHostingSettings = kind <> ResourceAddEntry.Exchange
            host._focusPublishSettings = kind = ResourceAddEntry.Exchange
            owner.Close()
            host.BeginInvoke(New Action(Sub() host.OnSettingsClicked(host, EventArgs.Empty)))
        End Sub
    End Class
End Class
