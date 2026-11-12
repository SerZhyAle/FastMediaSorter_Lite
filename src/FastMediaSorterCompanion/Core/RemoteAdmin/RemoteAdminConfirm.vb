Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' Ticket L0278 T-5 (specification section 5.6.3, owner decision Q278-6, 2026-10-29): the "do not ask again" opt-out of the three
' destructive confirmations of the remote administration window, one per KIND.
'
'   - Three kinds: account remove, device revoke, invitation withdraw. Each has its own value, so ticking the box for one never
'     skips another: RemoteAdmin_SkipConfirm_AccountRemove, RemoteAdmin_SkipConfirm_DeviceRevoke,
'     RemoteAdmin_SkipConfirm_InvitationWithdraw. The text "1" means skip; absent (or anything else) means ask.
'   - A per-user Share Manager setting in the product hive, through the IExchangeSettingsPort seam. It is not part of a target or a
'     session, names no server, login or password, and is NOT in LogPackage.SafeSettingNames (a test reads LogPackage.vb).
'   - Declining stores nothing: only a confirmed action with the box ticked writes (see RecordAnswer).
'   - ResetAll is the one way back ("Ask again before every removal"): it deletes the three values and nothing else. Owner rule
'     "no nanny confirmations" in its strong form: the opt-out exists, and nothing else removes it.
'   - Dormant: a read opens nothing for writing and creates no value.
'
' No user-visible text here. The words (the checkbox, the reset button, the consequence lines) are the Localization entries of the
' remote administration window.

''' <summary>The three actions whose confirmation can be switched off, each on its own.</summary>
Friend Enum RemoteAdminConfirmKind
    ''' <summary>account.remove</summary>
    AccountRemove = 1
    ''' <summary>device.revoke</summary>
    DeviceRevoke = 2
    ''' <summary>invitation.withdraw</summary>
    InvitationWithdraw = 3
End Enum

Friend NotInheritable Class RemoteAdminConfirm

    Friend Const AccountRemoveValue As String = "RemoteAdmin_SkipConfirm_AccountRemove"
    Friend Const DeviceRevokeValue As String = "RemoteAdmin_SkipConfirm_DeviceRevoke"
    Friend Const InvitationWithdrawValue As String = "RemoteAdmin_SkipConfirm_InvitationWithdraw"

    ''' <summary>Every value this class owns, so a test can assert that a dormant run wrote none of them.</summary>
    Friend Shared ReadOnly AllNames As String() = {AccountRemoveValue, DeviceRevokeValue, InvitationWithdrawValue}

    ''' <summary>Every kind, in one place for the reset path and the tests.</summary>
    Friend Shared ReadOnly AllKinds As RemoteAdminConfirmKind() = {RemoteAdminConfirmKind.AccountRemove, RemoteAdminConfirmKind.DeviceRevoke, RemoteAdminConfirmKind.InvitationWithdraw}

    ''' <summary>The stored text of a skipped confirmation.</summary>
    Friend Const SkipText As String = "1"

    Private ReadOnly _port As IExchangeSettingsPort

    Public Sub New(port As IExchangeSettingsPort)
        _port = If(port, New ExchangeRegistrySettings())
    End Sub

    Public Shared Function CreateDefault() As RemoteAdminConfirm
        Return New RemoteAdminConfirm(New ExchangeRegistrySettings())
    End Function

    ''' <summary>The value of one kind. A value that is not one of the three kinds is an error, not a fourth value.</summary>
    Public Shared Function ValueNameOf(kind As RemoteAdminConfirmKind) As String
        Select Case kind
            Case RemoteAdminConfirmKind.AccountRemove : Return AccountRemoveValue
            Case RemoteAdminConfirmKind.DeviceRevoke : Return DeviceRevokeValue
            Case RemoteAdminConfirmKind.InvitationWithdraw : Return InvitationWithdrawValue
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(kind))
        End Select
    End Function

    ''' <summary>True when this kind's confirmation was switched off. Only the stored text "1" counts; anything else asks. A pure read.</summary>
    Public Function IsSkipped(kind As RemoteAdminConfirmKind) As Boolean
        Return String.Equals(If(_port.GetValue(ValueNameOf(kind)), "").Trim(), SkipText, StringComparison.Ordinal)
    End Function

    ''' <summary>True while any of the three is switched off: the reset button is enabled exactly then.</summary>
    Public Function AnySkipped() As Boolean
        For Each kind As RemoteAdminConfirmKind In AllKinds
            If IsSkipped(kind) Then Return True
        Next
        Return False
    End Function

    ''' <summary>The one place an answer becomes a stored opt-out: it writes only when the person CONFIRMED the action and ticked
    ''' "do not ask again", and only for that kind. A declined confirmation stores nothing, whatever the box says.</summary>
    Public Sub RecordAnswer(kind As RemoteAdminConfirmKind, confirmed As Boolean, doNotAskAgain As Boolean)
        Dim name As String = ValueNameOf(kind)
        If Not (confirmed AndAlso doNotAskAgain) Then Return
        If IsSkipped(kind) Then Return
        _port.SetValue(name, SkipText)
    End Sub

    ''' <summary>"Ask again before every removal": deletes the three values, and only those.</summary>
    Public Sub ResetAll()
        For Each name As String In AllNames
            _port.DeleteValue(name)
        Next
    End Sub

End Class
