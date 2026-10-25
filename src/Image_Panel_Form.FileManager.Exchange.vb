#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Image_Panel_Form
    Private manager_Exchange As ManagerExchangePlaces
    Private manager_Exchange_Timer As System.Windows.Forms.Timer
    Private manager_Exchange_Busy As Boolean

    Private Sub StartManagerExchange()
        If manager_Exchange IsNot Nothing Then Return
        If Not ExchangeFeatures.IsEnabled() OrElse Not Main_Form.GetModernPreferences().FileManagerShowDevicePlaces Then Return
        manager_Exchange = ManagerExchangePlaces.CreateDefault()
        manager_Exchange_Timer = New System.Windows.Forms.Timer With {.Interval = 3000}
        AddHandler manager_Exchange_Timer.Tick, Sub() RefreshManagerExchange()
        manager_Exchange_Timer.Start()
        RefreshManagerExchange()
    End Sub

    Private Sub StopManagerExchange()
        manager_Exchange_Timer?.Stop()
        manager_Exchange_Timer?.Dispose()
        manager_Exchange_Timer = Nothing
        manager_Exchange?.Dispose()
        manager_Exchange = Nothing
    End Sub

    Private Async Sub RefreshManagerExchange()
        If manager_Exchange Is Nothing OrElse manager_Exchange_Busy Then Return
        If Not Main_Form.GetModernPreferences().FileManagerShowDevicePlaces Then
            StopManagerExchange()
            RefreshPlaces()
            UpdateManagerCommands()
            Return
        End If
        Dim adapter As ManagerExchangePlaces = manager_Exchange
        Dim epoch As Integer = manager_Epoch
        manager_Exchange_Busy = True
        Try
            Dim snapshot As ManagerExchangeSnapshot = Await Task.Run(Function() adapter.RefreshAsync(CancellationToken.None))
            If IsDisposed OrElse Not manager_Mode OrElse epoch <> manager_Epoch OrElse adapter IsNot manager_Exchange Then Return
            resource_List = LoadResources()
            RefreshPlaces()
            If nav?.Current IsNot Nothing AndAlso nav.Current.Place.ExchangeEntryId.Length > 0 Then
                Dim state As ManagerExchangeAvailability = adapter.StateOf(nav.Current.Place)
                If state <> ManagerExchangeAvailability.Available Then
                    browser.Cancel()
                    ShowStrip(ManagerExchangeStateText(state), False, False, True)
                End If
            End If
            UpdateManagerCommands()
        Catch ex As OperationCanceledException
        Catch ex As Exception
            AppFileLogger.LogException("Manager exchange directory", ex)
        Finally
            manager_Exchange_Busy = False
        End Try
    End Sub

    Private Sub AppendManagerExchangePlaces()
        If Not Main_Form.GetModernPreferences().FileManagerShowDevicePlaces Then Return
        If manager_Exchange Is Nothing OrElse manager_Exchange.Current.Nodes.Count = 0 Then Return
        lst_Places.Items.Add(ExchangeResourceText.SectionTitle())
        For Each node In manager_Exchange.Current.Nodes
            lst_Places.Items.Add(node)
            If node.Place IsNot Nothing AndAlso Not place_List.Any(Function(p) p.Id = node.Place.Id) Then place_List.Add(node.Place)
        Next
        If Not manager_Exchange.Current.Nodes.Any(Function(n) n.Kind = ManagerExchangeNodeKind.Root) Then
            lst_Places.Items.Add(ExchangeResourceText.NoFolders())
        End If
    End Sub

    Private Shared Function ManagerExchangeStateText(state As ManagerExchangeAvailability) As String
        Return New ManagerExchangeNode("", "", "", ManagerExchangeNodeKind.Root, state, "", "", "", Nothing).StateText
    End Function

    Private Async Sub OpenManagerExchangeNode(node As ManagerExchangeNode)
        If node.Kind <> ManagerExchangeNodeKind.Root Then
            If node.State = ManagerExchangeAvailability.SignInRequired OrElse node.State = ManagerExchangeAvailability.Revoked Then Main_Form.ShowSettingsWindowAtFileManager()
            Return
        End If
        If node.Place IsNot Nothing Then
            If node.State <> ManagerExchangeAvailability.Available Then
                ShowStrip(node.StateText, False, False, True)
                Return
            End If
            NavigateTo(ManagerIntent.Go, ManagerLocation.AtRoot(node.Place))
            Return
        End If
        If node.State <> ManagerExchangeAvailability.Available AndAlso node.State <> ManagerExchangeAvailability.Locked Then
            ShowStrip(node.StateText, False, False, True)
            Return
        End If
        If manager_Exchange_Busy OrElse manager_Command_Starting Then Return
        Dim answer As DialogResult = ViewerBox.Show(Me, Localization.TF("Подключить папку «{0}»?", node.Title),
            Localization.T("Ресурсы"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
        If answer <> DialogResult.Yes Then Return
        Dim pin As String = Nothing
        If node.State = ManagerExchangeAvailability.Locked Then
            pin = ResourcePinPromptUi.Ask(Me, node.Title)
            If pin Is Nothing Then Return
        End If
        Dim adapter As ManagerExchangePlaces = manager_Exchange
        Dim epoch As Integer = manager_Epoch
        manager_Command_Starting = True
        Try
            Dim outcome As ExchangeAttachOutcome = Await adapter.AttachAsync(node, pin, CancellationToken.None)
            If IsDisposed OrElse epoch <> manager_Epoch Then Return
            If outcome <> ExchangeAttachOutcome.Attached AndAlso outcome <> ExchangeAttachOutcome.AlreadyAttached Then
                ShowStrip(ExchangeResourceText.AttachFailure(outcome), False, False, True)
                Return
            End If
            resource_List = LoadResources()
            RefreshPlaces()
            Dim descriptor As ResourceDescriptor = resource_List.FirstOrDefault(Function(d) d.ExchangeEntryId = node.EntryId AndAlso
                d.ExchangeDeviceId = node.DeviceId AndAlso d.ExchangeResourceId = node.ResourceId)
            If descriptor IsNot Nothing Then NavigateTo(ManagerIntent.Go, ManagerLocation.AtRoot(ManagerPlace.ForResource(descriptor)))
            RefreshManagerExchange()
        Catch ex As Exception
            If Not IsDisposed Then ShowStrip(FailureText.Describe(ex), False, False, True)
        Finally
            manager_Command_Starting = False
            If Not IsDisposed Then UpdateManagerCommands()
        End Try
    End Sub
End Class
#End If
