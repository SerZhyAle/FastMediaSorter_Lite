Option Strict On
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class ManagerWelcomeStatus
    Friend Property Text As String
    Friend Property Color As Color
    Friend Property Glyph As String
    Friend Property Green As Boolean
    Friend Property FolderCount As Integer
End Class

Partial Public NotInheritable Class MainWindow
    Private _managerWelcomeRequested As Boolean
    Private _managerWelcomeOpen As Boolean
    Private _serverWelcomeAllowed As Boolean
    Private _focusExchangeSettings As Boolean
    Friend Event WelcomeDeclined As EventHandler

    Friend Sub ShowExchangeSettings()
        If Not ExchangeFirstRunModel.GateOpen() Then Return
        _focusExchangeSettings = True
        OnSettingsClicked(Me, EventArgs.Empty)
    End Sub

    Friend Sub RequestManagerWelcome(Optional due As Boolean = True)
        _serverWelcomeAllowed = True
        _managerWelcomeRequested = due
        If due AndAlso wake_Ready.Task.IsCompleted Then
            BeginInvoke(New Action(Async Sub()
                Dim finished As Boolean = Await OpenManagerWelcomeAsync()
                If finished Then Await Server_Welcome_Form.OpenDueHalvesAsync(Me, includeShare:=False)
            End Sub))
        End If
    End Sub

    Private Async Function OpenManagerWelcomeAsync(Optional replay As Boolean = False) As Task(Of Boolean)
        If _managerWelcomeOpen OrElse IsDisposed Then Return False
        If Not replay AndAlso (Not _managerWelcomeRequested OrElse
                Not ManagerWelcomeModel.ShouldOpen(ManagerWelcomeStore.State(), False, False, False)) Then Return False
        _managerWelcomeRequested = False
        _managerWelcomeOpen = True
        Try
            If replay Then ManagerWelcomeStore.SetState(ManagerWelcomeModel.Pending)
            Using dialog As New Manager_Welcome_Form(Me)
                ManagerWelcomeStore.WelcomeWasOpened()
                dialog.ShowDialog(Me)
                If dialog.DialogResult = DialogResult.Abort Then
                    Hide()
                    RaiseEvent WelcomeDeclined(Me, EventArgs.Empty)
                    Return False
                End If
            End Using
            Await WelcomeRefreshAsync()
            Return ManagerWelcomeStore.State() = ManagerWelcomeModel.Done
        Finally
            _managerWelcomeOpen = False
        End Try
    End Function

    Friend Async Function ReplayManagerWelcomeAsync() As Task
        Dim finished As Boolean = Await OpenManagerWelcomeAsync(True)
        If finished Then Await Server_Welcome_Form.OpenDueHalvesAsync(Me, includeShare:=False)
    End Function

    Friend Async Function WelcomeRefreshAsync(Optional reloadFolders As Boolean = False) As Task
        _settings.Load()
        ApplyGate()
        If Not ServerFeatures.IsEnabled() Then
            _status = Nothing
            UpdateStateLine(False)
            Return
        End If
        If reloadFolders Then
            _entered = False
            _listPopulated = False
            Await EnterAsync()
        Else
            _status = Await ShareController.GetStatusAsync()
            If IsDisposed Then Return
            ApplyStatusToUi()
            Await RefreshFolderAccessAsync()
        End If
    End Function

    Friend Function WelcomeStatusSnapshot() As ManagerWelcomeStatus
        UpdateStateLine(_status IsNot Nothing AndAlso _status.Running)
        Dim count As Integer = If(_status Is Nothing OrElse _status.Roots Is Nothing, 0, _status.Roots.Count)
        Dim warning As String = ShareController.PortWarning(_status)
        If warning = "" Then warning = If(_status Is Nothing, ShareController.RefusalMessage(), "")
        If warning = "" Then warning = ShareController.PolicyWarning()
        Dim green As Boolean = ManagerWelcomeModel.IsGreen(_status IsNot Nothing AndAlso _status.Running,
            count, _rootsWithoutAccess, warning)
        Return New ManagerWelcomeStatus With {.Text = lblState.Text, .Color = lblState.ForeColor,
            .Glyph = If(TryCast(lblStateDot.Tag, String), "shareStatus"), .Green = green, .FolderCount = count}
    End Function

    Private Function WelcomeLiveFolderCount() As Integer
        Return If(_status Is Nothing OrElse _status.Roots Is Nothing, 0, _status.Roots.Count)
    End Function

    Friend ReadOnly Property WelcomeHint As String
        Get
            Return lblHint.Text
        End Get
    End Property

    Friend Sub WelcomePairPhone(owner As Form)
        If _status Is Nothing OrElse Not _status.Running Then Return
        Dim settings As New ShareSettings()
        settings.Load()
        Dim config As ShareConfigResult = ShareConfigBuilder.Build(_status, Not settings.LanOnlyExport,
            includePassword:=Not settings.ExcludePasswordFromExport)
        If config?.QrPng Is Nothing Then
            MessageBox.Show(owner, Localization.T("QR-код сейчас недоступен."), ProductNames.ShareManager,
                MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using stream As New IO.MemoryStream(config.QrPng), image As Image = Image.FromStream(stream)
            Qr_Zoom_Form.ShowImage(owner, image)
        End Using
    End Sub
End Class
