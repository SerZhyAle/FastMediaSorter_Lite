Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>The Accounts card (5.6.2): the list of the server's accounts with a row's actions, and the chosen account's grants, read-only
''' (a grant is added or removed nowhere in this window).</summary>
Friend NotInheritable Class RemoteAdminAccountsCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _accounts As ListView
    Private ReadOnly _grantsHeading As Label
    Private ReadOnly _grantsHint As Label
    Private ReadOnly _grants As ListView
    Private _painting As Boolean
    Private _grantsStamp As Integer

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        _accounts = NewList(Localization.T("Учётные записи"), True,
                            {Localization.T("Логин"), Localization.T("Тип"), Localization.T("Устройства"), Localization.T("Свои лимиты")}, {150, 80, 80, 250})
        AddHandler _accounts.SelectedIndexChanged, AddressOf OnAccountSelected
        _grantsHeading = NewLabel(Localization.T("Доступы выбранной учётной записи"))
        AddContent(_grantsHeading)
        _grantsHint = NewLabel(Localization.T("Выберите учётную запись, чтобы увидеть её доступы."))
        AddContent(_grantsHint, True)
        _grants = NewList(Localization.T("Доступы выбранной учётной записи"), True,
                          {Localization.T("Тип"), Localization.T("Объект доступа"), Localization.T("Выдал")}, {110, 280, 170})

        AddAction(Localization.T("Добавить.."), "action.add", Nothing,
                  Sub() Fire(Function() RemoteAdminActions.AddAccountAsync(FindForm(), Me.Controller)))
        AddAction(Localization.T("Сменить пароль.."), "action.change-password", AddressOf HasSelection,
                  Sub() Fire(Function() RemoteAdminActions.ChangePasswordAsync(FindForm(), Me.Controller, SelectedLogin())))
        AddAction(Localization.T("Сменить тип.."), "action.edit", AddressOf HasSelection,
                  Sub() Fire(Function() RemoteAdminActions.ChangeKindAsync(FindForm(), Me.Controller, SelectedRow())))
        AddAction(Localization.T("Лимиты.."), "action.edit", AddressOf HasSelection,
                  Sub() Fire(Function() RemoteAdminActions.EditAccountLimitsAsync(FindForm(), Me.Controller, SelectedRow())))
        AddAction(Localization.T("Удалить учётную запись.."), "action.delete", AddressOf HasSelection,
                  Sub() Fire(Function() RemoteAdminActions.RemoveAccountAsync(FindForm(), Me.Controller, SelectedRow())))
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "accounts"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Учётные записи")
        End Get
    End Property

    ''' <summary>The grants of the chosen account (a test reads the second list through it).</summary>
    Friend ReadOnly Property GrantCount As Integer
        Get
            Return _grants.Items.Count
        End Get
    End Property

    Private Function SelectedRow() As RemoteAdminAccountRow
        If _accounts.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(_accounts.SelectedItems(0).Tag, RemoteAdminAccountRow)
    End Function

    Private Function SelectedLogin() As String
        Dim row As RemoteAdminAccountRow = SelectedRow()
        Return If(row Is Nothing, "", row.Login)
    End Function

    Private Function HasSelection() As Boolean
        Return SelectedRow() IsNot Nothing
    End Function

    Protected Overrides Sub ClearRows()
        _painting = True
        Try
            _accounts.Items.Clear()
            _grants.Items.Clear()
        Finally
            _painting = False
        End Try
        _grantsStamp += 1
        _grantsHint.Visible = False
        FitList(_grants)
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountListAsync(ct))
        If Not CanPaint(result, generation) Then Return
        Dim keep As String = SelectedLogin()
        _painting = True
        Try
            _accounts.BeginUpdate()
            _accounts.Items.Clear()
            For Each row As RemoteAdminAccountRow In result.Value
                Dim item As New ListViewItem(row.Login) With {.Tag = row}
                item.SubItems.Add(RemoteAdminCardText.KindWord(row.Kind))
                item.SubItems.Add(RemoteAdminCardText.Number(row.Devices))
                item.SubItems.Add(RemoteAdminCardText.LimitsLine(row.Limits))
                _accounts.Items.Add(item)
                If keep.Length > 0 AndAlso String.Equals(row.Login, keep, StringComparison.Ordinal) Then item.Selected = True
            Next
            _accounts.EndUpdate()
        Finally
            _painting = False
        End Try
        FitList(_accounts)
        NoteRows()
        Await ReadGrantsAsync(generation, SelectedLogin())
    End Function

    Private Sub OnAccountSelected(sender As Object, e As EventArgs)
        If _painting Then Return
        UpdateActionStates()
        Dim generation As Long = Controller.State.Generation
        Dim login As String = SelectedLogin()
        FireRead(Function() ReadGrantsAsync(generation, login))
    End Sub

    Private Async Function ReadGrantsAsync(generation As Long, login As String) As Task
        _grantsStamp += 1
        Dim stamp As Integer = _grantsStamp
        _grants.Items.Clear()
        FitList(_grants)
        _grantsHint.Visible = ShowsContent AndAlso login.Length = 0
        If login.Length = 0 OrElse Not Controller.State.CardsLive Then Return
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.GrantListAsync(login, "", ct))
        If stamp <> _grantsStamp OrElse Not CanPaint(result, generation) Then Return
        For Each g As RemoteAdminGrantRow In result.Value
            Dim kind As String = If(g.Kind = "resource", Localization.T("Ресурс"), If(g.Kind = "broadcast", Localization.T("Трансляция"), g.Kind))
            Dim item As New ListViewItem(kind) With {.Tag = g}
            item.SubItems.Add(g.TargetId)
            item.SubItems.Add(g.GrantedBy)
            _grants.Items.Add(item)
        Next
        FitList(_grants)
    End Function

End Class

''' <summary>The Devices card (5.6.2): an account chosen from the server's own list, that account's devices, a revoke and a pairing code.</summary>
Friend NotInheritable Class RemoteAdminDevicesCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _chooserRow As FlowLayoutPanel
    Private ReadOnly _chooser As ComboBox
    Private ReadOnly _hint As Label
    Private ReadOnly _devices As ListView
    Private _painting As Boolean
    Private _devicesOnly As Boolean

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        Dim caption As Label = NewLabel(Localization.T("Учётная запись:"))
        caption.Margin = New Padding(0, 6, 8, 2)
        _chooser = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 260, .Margin = New Padding(0, 2, 0, 2),
            .AccessibleName = Localization.T("Учётная запись:")}
        AddHandler _chooser.SelectedIndexChanged, AddressOf OnChosen
        _chooserRow = New FlowLayoutPanel With {.FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 2, 0, 2)}
        _chooserRow.Controls.Add(caption)
        _chooserRow.Controls.Add(_chooser)
        AddContent(_chooserRow)
        _hint = NewLabel(Localization.T("Выберите учётную запись, чтобы увидеть её устройства."))
        AddContent(_hint, True)
        _devices = NewList(Localization.T("Устройства"), True,
                           {Localization.T("Устройство"), Localization.T("Платформа"), Localization.T("Последняя активность"), Localization.T("Состояние")}, {170, 110, 170, 110})

        AddAction(Localization.T("Отозвать устройство.."), "action.delete", AddressOf HasDeviceSelected,
                  Sub() Fire(Function() RemoteAdminActions.RevokeDeviceAsync(FindForm(), Me.Controller, ChosenLogin(), SelectedDevice())))
        AddAction(Localization.T("Код сопряжения.."), "action.secure", AddressOf HasLogin,
                  Sub() Fire(Function() RemoteAdminActions.ShowPairingCodeAsync(FindForm(), Me.Controller, ChosenLogin())))
        AddHandler _devices.SelectedIndexChanged, Sub(sender As Object, e As EventArgs) UpdateActionStates()
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "devices"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Устройства")
        End Get
    End Property

    ''' <summary>The logins in the chooser (a test reads what the account list filled).</summary>
    Friend ReadOnly Property ChooserCount As Integer
        Get
            Return _chooser.Items.Count
        End Get
    End Property

    Private Function ChosenLogin() As String
        Dim s As String = TryCast(_chooser.SelectedItem, String)
        Return If(s, "")
    End Function

    Private Function HasLogin() As Boolean
        Return ChosenLogin().Length > 0
    End Function

    Private Function SelectedDevice() As RemoteAdminDeviceRow
        If _devices.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(_devices.SelectedItems(0).Tag, RemoteAdminDeviceRow)
    End Function

    Private Function HasDeviceSelected() As Boolean
        Return SelectedDevice() IsNot Nothing
    End Function

    Protected Overrides Sub ClearRows()
        _painting = True
        Try
            _chooser.Items.Clear()
            _devices.Items.Clear()
        Finally
            _painting = False
        End Try
        _devicesOnly = False
        _hint.Visible = False
        FitList(_devices)
    End Sub

    Protected Overrides Sub OnRefreshing()
        _devicesOnly = False
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        If Not _devicesOnly Then
            Dim accounts As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)) =
                Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.AccountListAsync(ct))
            If Not CanPaint(accounts, generation) Then Return
            Dim keep As String = ChosenLogin()
            _painting = True
            Try
                _chooser.BeginUpdate()
                _chooser.Items.Clear()
                For Each row As RemoteAdminAccountRow In accounts.Value
                    _chooser.Items.Add(row.Login)
                Next
                Dim at As Integer = If(keep.Length > 0, _chooser.Items.IndexOf(keep), -1)
                If at < 0 AndAlso _chooser.Items.Count > 0 Then at = 0
                _chooser.SelectedIndex = at
                _chooser.EndUpdate()
            Finally
                _painting = False
            End Try
        End If
        _devicesOnly = False
        Dim login As String = ChosenLogin()
        _devices.Items.Clear()
        _hint.Visible = login.Length = 0
        If login.Length = 0 Then
            FitList(_devices)
            NoteRows()
            Return
        End If
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.DeviceListAsync(login, ct))
        If Not CanPaint(result, generation) Then Return
        _devices.BeginUpdate()
        For Each d As RemoteAdminDeviceRow In result.Value
            Dim item As New ListViewItem(If(d.DeviceName.Length > 0, d.DeviceName, d.Product)) With {.Tag = d}
            item.SubItems.Add(d.Platform)
            item.SubItems.Add(d.LastSeen)
            item.SubItems.Add(If(d.SignedOut, Localization.T("Вход сброшен"), If(d.Online, Localization.T("В сети"), Localization.T("Не в сети"))))
            _devices.Items.Add(item)
        Next
        _devices.EndUpdate()
        FitList(_devices)
        NoteRows()
    End Function

    Private Sub OnChosen(sender As Object, e As EventArgs)
        If _painting OrElse IsReading Then Return
        _devicesOnly = True
        UpdateActionStates()
        FireRead(Function() ReloadAsync())
    End Sub

End Class
