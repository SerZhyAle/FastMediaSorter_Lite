Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>The Status card (5.6.2): the counts the server's reply carries, a row only where the field is there - no version, no uptime, no
''' "administration is on" (the reply has none).</summary>
Friend NotInheritable Class RemoteAdminStatusCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _list As ListView

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        _list = NewList(Localization.T("Состояние"), False, {"", ""}, {300, 240})
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "status"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Состояние")
        End Get
    End Property

    Protected Overrides Sub ClearRows()
        _list.Items.Clear()
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        Dim result As RemoteAdminResult(Of RemoteAdminStatusView) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.StatusAsync(ct))
        If Not CanPaint(result, generation) Then Return
        Dim v As RemoteAdminStatusView = result.Value
        _list.BeginUpdate()
        _list.Items.Clear()
        If v.Accounts.HasValue Then
            AddRow(Localization.T("Учётные записи"), If(v.MaxAccounts.HasValue, Localization.TF("{0} из {1}", RemoteAdminCardText.Number(v.Accounts.Value), RemoteAdminCardText.Number(v.MaxAccounts.Value)), RemoteAdminCardText.Number(v.Accounts.Value)))
        End If
        If v.Devices.HasValue Then AddRow(Localization.T("Устройства"), RemoteAdminCardText.Number(v.Devices.Value))
        If v.Online.HasValue Then AddRow(Localization.T("Из них в сети"), RemoteAdminCardText.Number(v.Online.Value))
        If v.Resources.HasValue Then AddRow(Localization.T("Опубликованные ресурсы"), RemoteAdminCardText.Number(v.Resources.Value))
        If v.Broadcasts.HasValue Then AddRow(Localization.T("Трансляции"), RemoteAdminCardText.Number(v.Broadcasts.Value))
        If v.Clipboards.HasValue Then AddRow(Localization.T("Буферы обмена"), RemoteAdminCardText.Number(v.Clipboards.Value))
        If v.Sealed.HasValue Then AddRow(Localization.T("Защищённое хранилище"), If(v.Sealed.Value, Localization.T("Да"), Localization.T("Нет")))
        _list.EndUpdate()
        FitList(_list)
        NoteRows()
    End Function

    Private Sub AddRow(label As String, value As String)
        Dim item As New ListViewItem(label)
        item.SubItems.Add(value)
        _list.Items.Add(item)
    End Sub

End Class

''' <summary>The Invitations card (5.6.2): the server's invitations, a create and a confirmed withdraw. The plaintext code is never in the list.</summary>
Friend NotInheritable Class RemoteAdminInvitationsCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _list As ListView

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        _list = NewList(Localization.T("Приглашения"), True,
                        {Localization.T("Тип"), Localization.T("Для логина"), Localization.T("Создано"), Localization.T("Состояние")}, {90, 170, 170, 110})
        AddHandler _list.SelectedIndexChanged, Sub(sender As Object, e As EventArgs) UpdateActionStates()
        AddAction(Localization.T("Создать.."), "action.add", Nothing,
                  Sub() Fire(Function() RemoteAdminActions.CreateInvitationAsync(FindForm(), Me.Controller)))
        AddAction(Localization.T("Отозвать приглашение.."), "action.delete", AddressOf HasSelection,
                  Sub() Fire(Function() RemoteAdminActions.WithdrawInvitationAsync(FindForm(), Me.Controller, SelectedRow())))
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "invitations"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Приглашения")
        End Get
    End Property

    Private Function SelectedRow() As RemoteAdminInvitationRow
        If _list.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(_list.SelectedItems(0).Tag, RemoteAdminInvitationRow)
    End Function

    Private Function HasSelection() As Boolean
        Return SelectedRow() IsNot Nothing
    End Function

    Protected Overrides Sub ClearRows()
        _list.Items.Clear()
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        Dim result As RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminInvitationRow)) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.InvitationListAsync(ct))
        If Not CanPaint(result, generation) Then Return
        _list.BeginUpdate()
        _list.Items.Clear()
        For Each row As RemoteAdminInvitationRow In result.Value
            Dim item As New ListViewItem(RemoteAdminCardText.KindWord(row.Kind)) With {.Tag = row}
            item.SubItems.Add(row.ExpectedLogin)
            item.SubItems.Add(row.CreatedAt)
            item.SubItems.Add(If(row.Used, Localization.T("Использовано"), Localization.T("Ожидает")))
            _list.Items.Add(item)
        Next
        _list.EndUpdate()
        FitList(_list)
        NoteRows()
    End Function

End Class

''' <summary>The Shared invitation card (5.6.2): whether it is on, a Save that sets its password, and its OWN off button - an empty
''' password turns it off on the server, which is never left as a surprise of an empty field.</summary>
Friend NotInheritable Class RemoteAdminSharedInvitationCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _state As Label
    Private _status As RemoteAdminSharedStatus

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        _state = NewLabel("")
        AddContent(_state)
        AddAction(Localization.T("Задать пароль.."), "action.change-password", AddressOf HasStatus,
                  Sub() Fire(Function() RemoteAdminActions.SetSharedInvitationAsync(FindForm(), Me.Controller, _status)))
        AddAction(Localization.T("Выключить"), "action.delete", AddressOf IsOn,
                  Sub() Fire(Function() RemoteAdminActions.TurnSharedInvitationOffAsync(FindForm(), Me.Controller, _status)))
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "shared-invitation"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Общее приглашение")
        End Get
    End Property

    ''' <summary>The state sentence on screen, "" before a read.</summary>
    Friend ReadOnly Property StateText As String
        Get
            Return _state.Text
        End Get
    End Property

    Friend Overrides ReadOnly Property RowCount As Integer
        Get
            Return If(_status Is Nothing, 0, 1)
        End Get
    End Property

    Private Function HasStatus() As Boolean
        Return _status IsNot Nothing
    End Function

    Private Function IsOn() As Boolean
        Return _status IsNot Nothing AndAlso _status.Enabled
    End Function

    Protected Overrides Sub ClearRows()
        _status = Nothing
        _state.Text = ""
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        Dim result As RemoteAdminResult(Of RemoteAdminSharedStatus) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.SharedInvitationStatusAsync(ct))
        If Not CanPaint(result, generation) Then Return
        _status = result.Value
        If _status.Enabled Then
            _state.Text = If(_status.Kind.Length > 0, Localization.TF("Общее приглашение включено, тип: {0}", RemoteAdminCardText.KindWord(_status.Kind)),
                             Localization.T("Общее приглашение включено"))
        Else
            _state.Text = Localization.T("Общее приглашение выключено")
        End If
        NoteRows()
    End Function

End Class

''' <summary>The Limits and policy card (5.6.2): the account cap, how full it is and what a new account gets; Edit changes them. When the
''' server says a change holds only until it restarts, the line beside the buttons says so.</summary>
Friend NotInheritable Class RemoteAdminLimitsCard
    Inherits RemoteAdminCardBase

    Private ReadOnly _list As ListView
    Private _view As RemoteAdminPolicyView

    Public Sub New(controller As RemoteAdminController)
        MyBase.New(controller)
        _list = NewList(Localization.T("Ограничения и политика"), False, {"", ""}, {340, 200})
        AddAction(Localization.T("Изменить.."), "action.edit", AddressOf HasView,
                  Sub() Fire(Function() RemoteAdminActions.EditPolicyAsync(FindForm(), Me.Controller, _view)))
    End Sub

    Friend Overrides ReadOnly Property RailId As String
        Get
            Return "limits"
        End Get
    End Property

    Friend Overrides ReadOnly Property RailCaption As String
        Get
            Return Localization.T("Ограничения и политика")
        End Get
    End Property

    Private Function HasView() As Boolean
        Return _view IsNot Nothing
    End Function

    Protected Overrides Sub ClearRows()
        _view = Nothing
        _list.Items.Clear()
    End Sub

    Protected Overrides Async Function ReadAsync(generation As Long) As Task
        Dim result As RemoteAdminResult(Of RemoteAdminPolicyView) =
            Await Controller.CallAsync(Function(ops As RemoteAdminOps, ct As CancellationToken) ops.PolicyStatusAsync(ct))
        If Not CanPaint(result, generation) Then Return
        _view = result.Value
        _list.BeginUpdate()
        _list.Items.Clear()
        If _view.MaxAccounts.HasValue Then AddRow(Localization.T("Предел учётных записей"), RemoteAdminCardText.Number(_view.MaxAccounts.Value))
        If _view.Accounts.HasValue Then AddRow(Localization.T("Учётных записей на сервере"), RemoteAdminCardText.Number(_view.Accounts.Value))
        For Each pair As KeyValuePair(Of String, String) In RemoteAdminCardText.LimitRows(_view.Defaults)
            AddRow(Localization.TF("Для новых учётных записей: {0}", pair.Key), pair.Value)
        Next
        _list.EndUpdate()
        FitList(_list)
        NoteRows()
        If Not _view.Persisted Then SetNote(Localization.T("Изменение действует до перезапуска сервера."))
    End Function

    Private Sub AddRow(label As String, value As String)
        Dim item As New ListViewItem(label)
        item.SubItems.Add(value)
        _list.Items.Add(item)
    End Sub

End Class
