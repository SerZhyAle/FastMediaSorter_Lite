Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Ticket L0278 T-5 (specification 5.3, 5.5, 5.6.2 and the operation map of section 6): the typed operations of the remote administration
' client. One method per carried operation, each over ExchangeRemoteAdminSession.CallAsync with the builder of ExchangeAdminWire, each
' answering with a RemoteAdminResult that holds a kind, a code and either a view model or nothing. The window (T-6) and the dialogs (T-7)
' are built on this API and on nothing below it.
'
' What this class keeps (each has a test):
'   - It holds no user word, no Localization call, no registry value and no file: the sentences are RemoteAdminWords' job, and the
'     result carries what that class needs (RemoteAdminResult.Outcome - the kind, the code and the reason, never a body).
'   - A result carries only values: the JsonElement of an outcome does not outlive the call, and the outcome is disposed here. The
'     view models are read from `data` with this class's own tolerant reader (an unknown member is ignored). The pure
'     ExchangeAdminParser is NOT used on purpose: none of its rows has every member the views need (maxAccounts, tombstones, effective,
'     expectedLogin, roles, signedOut, expiresAt, connectionString), and its zero-value reading would turn a wrong body into a plausible
'     empty view where the specification wants a Protocol result (5.5). A body that is not what the operation says - a member of the wrong
'     kind, a number out of range, a required member missing - is a Protocol result with a bounded Detail from a fixed vocabulary
'     (never a piece of the reply), and never an exception.
'   - A Protocol result ends the session (5.5: the request may have been carried out, the next sign-in re-reads what the person sees),
'     and so does an Unreachable result of a mutating operation. The session already ends itself on most of these; EndAsync is
'     idempotent (nothing is written to a session that has ended), so asking again never sends a second `bye`. A Lapsed result needs
'     nothing more, a Busy result is passed through untouched (another call is in flight and must not be cancelled by this one).
'   - A refusal "before anything is written" - maxAccounts below 1, a negative limit, an empty kind of account.kind, an empty password
'     for the "on" form of the shared invitation, a grant.list with both a login and a guest account - is a Failed result with the
'     matching code of the server's ten (bad-limit, bad-kind, weak-password, bad-request), and the session is not touched.
'   - No password generator of any kind (owner decision Q278-17). A typed password arrives as a String because that is what the wire
'     builders take (the envelope holds it as text until it is collected; the frame buffer is wiped by the writer after the send), so
'     there is no array here to clear.
'   - Nothing is logged here: the session logs its own events, and no host, name, login, password, code or reply body ever reaches a
'     line. ToString of every result and view names the type or the kind and nothing else.

''' <summary>Names the carried operation a method of <see cref="RemoteAdminOps"/> performs (the tests hold the set of these equal to
''' the 18 operations; a method with no attribute is not an operation).</summary>
<AttributeUsage(AttributeTargets.Method, AllowMultiple:=False)>
Friend NotInheritable Class RemoteAdminOpAttribute
    Inherits Attribute

    Public ReadOnly Property Op As String

    Public Sub New(op As String)
        Me.Op = op
    End Sub
End Class

''' <summary>What one call came to, for an operation that carries no data. <see cref="Outcome"/> is the session's outcome without its
''' body (a clone that holds nothing to dispose), so the words of <c>RemoteAdminWords</c> can be given what they need.</summary>
Friend Class RemoteAdminResult

    ''' <summary>Done, Failed, Lapsed, Unreachable, Protocol or Busy (5.3).</summary>
    Public ReadOnly Property Kind As ExchangeRemoteAdminOutcomeKind
    ''' <summary>The server's code of a Failed result (one of the ten, or a newer one); "" otherwise.</summary>
    Public ReadOnly Property Code As String
    ''' <summary>Why an Unreachable result is one; None for every other kind.</summary>
    Public ReadOnly Property Reason As ExchangeRemoteAdminUnreachableReason
    ''' <summary>A bounded internal reason for the log and the tests (a fixed vocabulary or an exception TYPE name); never shown.</summary>
    Public ReadOnly Property Detail As String
    ''' <summary>The outcome of the call with no body: its kind, code, reason and detail. Never disposed by anyone.</summary>
    Public ReadOnly Property Outcome As ExchangeRemoteAdminOutcome

    Friend Sub New(outcome As ExchangeRemoteAdminOutcome)
        Me.Outcome = outcome
        Kind = outcome.Kind
        Code = outcome.Code
        Reason = outcome.Reason
        Detail = outcome.Detail
    End Sub

    ''' <summary>True for a Done result.</summary>
    Public ReadOnly Property IsDone As Boolean
        Get
            Return Kind = ExchangeRemoteAdminOutcomeKind.Done
        End Get
    End Property

    Friend Shared Function Make(outcome As ExchangeRemoteAdminOutcome) As RemoteAdminResult
        Return New RemoteAdminResult(outcome)
    End Function

    ''' <summary>The kind and the code; never a value.</summary>
    Public Overrides Function ToString() As String
        Return "RemoteAdminResult " & Outcome.ToString()
    End Function
End Class

''' <summary>What one call came to, for an operation that answers with data. <see cref="Value"/> is set for a Done result and is
''' <c>Nothing</c> (the default of the type) for every other kind.</summary>
Friend NotInheritable Class RemoteAdminResult(Of T)
    Inherits RemoteAdminResult

    Public ReadOnly Property Value As T

    Private Sub New(outcome As ExchangeRemoteAdminOutcome, value As T)
        MyBase.New(outcome)
        Me.Value = value
    End Sub

    Friend Shared Function MakeOf(outcome As ExchangeRemoteAdminOutcome, value As T) As RemoteAdminResult(Of T)
        Return New RemoteAdminResult(Of T)(outcome, value)
    End Function

    ''' <summary>The kind and the code; never the value.</summary>
    Public Overrides Function ToString() As String
        Return "RemoteAdminResult(Of " & GetType(T).Name & ") " & Outcome.ToString()
    End Function
End Class

''' <summary>The five limits of the server (an account's own overrides, the defaults a new account gets, or what is in force). A member
''' that is not set is Nothing. Immutable. The same type is the input of account.limits and policy.set: the server REPLACES the whole
''' override object of an account, so the caller passes every member that is set after the edit.</summary>
Friend NotInheritable Class RemoteAdminLimits

    Public Shared ReadOnly Empty As New RemoteAdminLimits(Nothing, Nothing, Nothing, Nothing, Nothing)

    Public ReadOnly Property DevicesPerAccount As Integer?
    Public ReadOnly Property ResourcesPerAccount As Integer?
    Public ReadOnly Property BroadcastsPerAccount As Integer?
    Public ReadOnly Property BroadcastsPerDevice As Integer?
    Public ReadOnly Property ClipboardMaxBytes As Long?

    Public Sub New(devicesPerAccount As Integer?, resourcesPerAccount As Integer?, broadcastsPerAccount As Integer?, broadcastsPerDevice As Integer?, clipboardMaxBytes As Long?)
        Me.DevicesPerAccount = devicesPerAccount
        Me.ResourcesPerAccount = resourcesPerAccount
        Me.BroadcastsPerAccount = broadcastsPerAccount
        Me.BroadcastsPerDevice = broadcastsPerDevice
        Me.ClipboardMaxBytes = clipboardMaxBytes
    End Sub

    ''' <summary>True when no member is set.</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Not DevicesPerAccount.HasValue AndAlso Not ResourcesPerAccount.HasValue AndAlso Not BroadcastsPerAccount.HasValue AndAlso
                   Not BroadcastsPerDevice.HasValue AndAlso Not ClipboardMaxBytes.HasValue
        End Get
    End Property

    ''' <summary>True when a member is set below zero (the server's `bad-limit`; zero itself is a legal limit).</summary>
    Public ReadOnly Property HasNegative As Boolean
        Get
            Return (DevicesPerAccount.HasValue AndAlso DevicesPerAccount.Value < 0) OrElse
                   (ResourcesPerAccount.HasValue AndAlso ResourcesPerAccount.Value < 0) OrElse
                   (BroadcastsPerAccount.HasValue AndAlso BroadcastsPerAccount.Value < 0) OrElse
                   (BroadcastsPerDevice.HasValue AndAlso BroadcastsPerDevice.Value < 0) OrElse
                   (ClipboardMaxBytes.HasValue AndAlso ClipboardMaxBytes.Value < 0)
        End Get
    End Property

    Friend Function ToWire() As ExchangeAdminWireLimits
        Return New ExchangeAdminWireLimits With {
            .DevicesPerAccount = DevicesPerAccount, .ResourcesPerAccount = ResourcesPerAccount,
            .BroadcastsPerAccount = BroadcastsPerAccount, .BroadcastsPerDevice = BroadcastsPerDevice,
            .ClipboardMaxBytes = ClipboardMaxBytes}
    End Function

    Public Overrides Function ToString() As String
        Return "RemoteAdminLimits"
    End Function
End Class

''' <summary>The counts of `status`. A member is Nothing when the server did not send it (the card shows a field only where it is
''' there). There is no version, no uptime and no "administration is on": the reply has none (3.1).</summary>
Friend NotInheritable Class RemoteAdminStatusView
    Public ReadOnly Property Accounts As Integer?
    Public ReadOnly Property MaxAccounts As Integer?
    Public ReadOnly Property Devices As Integer?
    Public ReadOnly Property Online As Integer?
    Public ReadOnly Property Resources As Integer?
    Public ReadOnly Property Broadcasts As Integer?
    Public ReadOnly Property Tombstones As Integer?
    Public ReadOnly Property Clipboards As Integer?
    Public ReadOnly Property ClipboardBytes As Long?
    Public ReadOnly Property KeepAwakeActive As Boolean?
    Public ReadOnly Property KeepAwake As Boolean?
    Public ReadOnly Property LogRetentionDays As Integer?
    Public ReadOnly Property Sealed As Boolean?

    Friend Sub New(accounts As Integer?, maxAccounts As Integer?, devices As Integer?, online As Integer?, resources As Integer?,
                   broadcasts As Integer?, tombstones As Integer?, clipboards As Integer?, clipboardBytes As Long?,
                   keepAwakeActive As Boolean?, keepAwake As Boolean?, logRetentionDays As Integer?, sealed As Boolean?)
        Me.Accounts = accounts
        Me.MaxAccounts = maxAccounts
        Me.Devices = devices
        Me.Online = online
        Me.Resources = resources
        Me.Broadcasts = broadcasts
        Me.Tombstones = tombstones
        Me.Clipboards = clipboards
        Me.ClipboardBytes = clipboardBytes
        Me.KeepAwakeActive = keepAwakeActive
        Me.KeepAwake = keepAwake
        Me.LogRetentionDays = logRetentionDays
        Me.Sealed = sealed
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminStatusView"
    End Function
End Class

''' <summary>The policy of `policy.status` and `policy.set`: the cap on accounts, how full it is, what a NEW account gets, and whether
''' the change is written for good.</summary>
Friend NotInheritable Class RemoteAdminPolicyView
    Public ReadOnly Property MaxAccounts As Integer?
    Public ReadOnly Property Accounts As Integer?
    ''' <summary>What a new account starts with (never Nothing; <see cref="RemoteAdminLimits.IsEmpty"/> when the reply had none).</summary>
    Public ReadOnly Property Defaults As RemoteAdminLimits
    ''' <summary>False = the change holds only until the server restarts ("this run only"). A reply with no `persisted` member reads as
    ''' True: the card then shows no warning it has no word for.</summary>
    Public ReadOnly Property Persisted As Boolean

    Friend Sub New(maxAccounts As Integer?, accounts As Integer?, defaults As RemoteAdminLimits, persisted As Boolean)
        Me.MaxAccounts = maxAccounts
        Me.Accounts = accounts
        Me.Defaults = If(defaults, RemoteAdminLimits.Empty)
        Me.Persisted = persisted
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminPolicyView"
    End Function
End Class

''' <summary>One account of `account.list`.</summary>
Friend NotInheritable Class RemoteAdminAccountRow
    ''' <summary>As the server holds it (lower-case).</summary>
    Public ReadOnly Property Login As String
    ''' <summary>"member" or "guest" (a reply with no kind is a member, as the server omits the default).</summary>
    Public ReadOnly Property Kind As String
    Public ReadOnly Property Devices As Integer
    ''' <summary>The account's own overrides (never Nothing; <see cref="RemoteAdminLimits.IsEmpty"/> when it has none).</summary>
    Public ReadOnly Property Limits As RemoteAdminLimits
    ''' <summary>What is in force: the overrides over the defaults (never Nothing).</summary>
    Public ReadOnly Property Effective As RemoteAdminLimits
    ''' <summary>The server's timestamp text.</summary>
    Public ReadOnly Property CreatedAt As String

    Friend Sub New(login As String, kind As String, devices As Integer, limits As RemoteAdminLimits, effective As RemoteAdminLimits, createdAt As String)
        Me.Login = login
        Me.Kind = kind
        Me.Devices = devices
        Me.Limits = If(limits, RemoteAdminLimits.Empty)
        Me.Effective = If(effective, RemoteAdminLimits.Empty)
        Me.CreatedAt = createdAt
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminAccountRow"
    End Function
End Class

''' <summary>The answer of `account.add`: the login the server normalised (trim and lower-case) - show it, not what was typed - and
''' whether the operator took the account past the cap.</summary>
Friend NotInheritable Class RemoteAdminAccountAdded
    Public ReadOnly Property Login As String
    Public ReadOnly Property OverCap As Boolean

    Friend Sub New(login As String, overCap As Boolean)
        Me.Login = login
        Me.OverCap = overCap
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminAccountAdded"
    End Function
End Class

''' <summary>The answer of `account.passwd`: how many devices of the account were signed out.</summary>
Friend NotInheritable Class RemoteAdminPasswordChanged
    Public ReadOnly Property SignedOut As Integer

    Friend Sub New(signedOut As Integer)
        Me.SignedOut = signedOut
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminPasswordChanged"
    End Function
End Class

''' <summary>One device of `device.list` (sorted by id on the server).</summary>
Friend NotInheritable Class RemoteAdminDeviceRow
    Public ReadOnly Property DeviceId As String
    Public ReadOnly Property DeviceName As String
    Public ReadOnly Property Product As String
    ''' <summary>"" when the server sent none.</summary>
    Public ReadOnly Property ProductVersion As String
    ''' <summary>"" when the server sent none.</summary>
    Public ReadOnly Property Platform As String
    ''' <summary>The roles the device announced (empty when none).</summary>
    Public ReadOnly Property Roles As IReadOnlyList(Of String)
    Public ReadOnly Property EnrolledAt As String
    ''' <summary>"" when the device never connected.</summary>
    Public ReadOnly Property LastSeen As String
    Public ReadOnly Property Online As Boolean
    ''' <summary>True while the device's token is dead through a password change or a reset.</summary>
    Public ReadOnly Property SignedOut As Boolean

    Friend Sub New(deviceId As String, deviceName As String, product As String, productVersion As String, platform As String,
                   roles As IReadOnlyList(Of String), enrolledAt As String, lastSeen As String, online As Boolean, signedOut As Boolean)
        Me.DeviceId = deviceId
        Me.DeviceName = deviceName
        Me.Product = product
        Me.ProductVersion = productVersion
        Me.Platform = platform
        Me.Roles = roles
        Me.EnrolledAt = enrolledAt
        Me.LastSeen = lastSeen
        Me.Online = online
        Me.SignedOut = signedOut
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminDeviceRow"
    End Function
End Class

''' <summary>The answer of `device.pairing-code`: a single-use code of ten minutes. It is a secret for those ten minutes: shown once,
''' copied with the clipboard flags, never logged.</summary>
Friend NotInheritable Class RemoteAdminPairingCode
    Public ReadOnly Property Code As String
    ''' <summary>The server's timestamp text of the code's end; "" when the reply had none.</summary>
    Public ReadOnly Property ExpiresAt As String

    Friend Sub New(code As String, expiresAt As String)
        Me.Code = code
        Me.ExpiresAt = expiresAt
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminPairingCode"
    End Function
End Class

''' <summary>One invitation of `invitation.list` or the `invitation` member of `invitation.create`. It never carries the plaintext code.</summary>
Friend NotInheritable Class RemoteAdminInvitationRow
    ''' <summary>The full hash (what `invitation.withdraw` takes).</summary>
    Public ReadOnly Property CodeHash As String
    Public ReadOnly Property Kind As String
    Public ReadOnly Property CreatedAt As String
    ''' <summary>The login the invitation was written for (a hint, never enforced); "" when none.</summary>
    Public ReadOnly Property ExpectedLogin As String
    Public ReadOnly Property Used As Boolean
    Public ReadOnly Property UsedAt As String
    Public ReadOnly Property UsedBy As String

    Friend Sub New(codeHash As String, kind As String, createdAt As String, expectedLogin As String, used As Boolean, usedAt As String, usedBy As String)
        Me.CodeHash = codeHash
        Me.Kind = kind
        Me.CreatedAt = createdAt
        Me.ExpectedLogin = expectedLogin
        Me.Used = used
        Me.UsedAt = usedAt
        Me.UsedBy = usedBy
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminInvitationRow"
    End Function
End Class

''' <summary>The answer of `invitation.create`. The plaintext <see cref="Code"/> is returned ONCE and is never in the list: the window
''' shows it once and copies it with the clipboard flags. It is never logged.</summary>
Friend NotInheritable Class RemoteAdminInvitationCreated
    Public ReadOnly Property Code As String
    ''' <summary>The invitation as the server lists it; Nothing when the reply had no readable `invitation` (the code is still shown).</summary>
    Public ReadOnly Property Invitation As RemoteAdminInvitationRow
    ''' <summary>The FMSEXCH1 string of the server's own public address, or "" when the server has none (the member is present and empty):
    ''' the card says so in words and offers no empty Copy.</summary>
    Public ReadOnly Property ConnectionString As String

    Friend Sub New(code As String, invitation As RemoteAdminInvitationRow, connectionString As String)
        Me.Code = code
        Me.Invitation = invitation
        Me.ConnectionString = If(connectionString, "")
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminInvitationCreated"
    End Function
End Class

''' <summary>The answer of `shared_invitation.status`.</summary>
Friend NotInheritable Class RemoteAdminSharedStatus
    Public ReadOnly Property Enabled As Boolean
    ''' <summary>The kind it registers; "" when the server sent none.</summary>
    Public ReadOnly Property Kind As String

    Friend Sub New(enabled As Boolean, kind As String)
        Me.Enabled = enabled
        Me.Kind = kind
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminSharedStatus"
    End Function
End Class

''' <summary>One grant of `grant.list` (read-only: `grant.add` and `grant.remove` are not carried and have no control).</summary>
Friend NotInheritable Class RemoteAdminGrantRow
    ''' <summary>"" when the server omitted it.</summary>
    Public ReadOnly Property GrantId As String
    Public ReadOnly Property TargetId As String
    ''' <summary>"resource" or "broadcast".</summary>
    Public ReadOnly Property Kind As String
    Public ReadOnly Property Guest As String
    Public ReadOnly Property GrantedBy As String
    Public ReadOnly Property GrantedAt As String

    Friend Sub New(grantId As String, targetId As String, kind As String, guest As String, grantedBy As String, grantedAt As String)
        Me.GrantId = grantId
        Me.TargetId = targetId
        Me.Kind = kind
        Me.Guest = guest
        Me.GrantedBy = grantedBy
        Me.GrantedAt = grantedAt
    End Sub

    Public Overrides Function ToString() As String
        Return "RemoteAdminGrantRow"
    End Function
End Class

''' <summary>The 18 carried operations of the remote administration client over one signed-in <see cref="ExchangeRemoteAdminSession"/>,
''' typed. There is no method for an operation the server does not carry. Every method returns a result and none throws for what the
''' server or the network did; a call that is not allowed to start (a refusal before anything is written) is a Failed result with
''' the code of the server's ten that fits.</summary>
Friend NotInheritable Class RemoteAdminOps

    ''' <summary>The reader found a body that is not what the operation says. <see cref="Reason"/> is a fixed word, never a piece of the reply.</summary>
    Private NotInheritable Class RemoteAdminShapeException
        Inherits Exception

        Public ReadOnly Property Reason As String

        Public Sub New(reason As String)
            MyBase.New(reason)
            Me.Reason = reason
        End Sub
    End Class

    Private Shared ReadOnly _mutating As New HashSet(Of String)(StringComparer.Ordinal) From {
        ExchangeAdminWire.OpAccountAdd, ExchangeAdminWire.OpAccountPasswd, ExchangeAdminWire.OpAccountKind, ExchangeAdminWire.OpAccountRemove,
        ExchangeAdminWire.OpAccountLimits, ExchangeAdminWire.OpDeviceRevoke, ExchangeAdminWire.OpDevicePairingCode,
        ExchangeAdminWire.OpInvitationCreate, ExchangeAdminWire.OpInvitationWithdraw, ExchangeAdminWire.OpSharedInvitationSet,
        ExchangeAdminWire.OpPolicySet}

    Private ReadOnly _session As ExchangeRemoteAdminSession

    Public Sub New(session As ExchangeRemoteAdminSession)
        If session Is Nothing Then Throw New ArgumentNullException(NameOf(session))
        _session = session
    End Sub

    ''' <summary>True for the 11 operations the server audits as a change (section 6): after a successful one the card re-reads its own
    ''' list, and after an Unreachable or Protocol result of one the session ends. An ordinal comparison; anything else is False.</summary>
    Public Shared Function IsMutating(op As String) As Boolean
        If op Is Nothing Then Return False
        Return _mutating.Contains(op)
    End Function

    ' ----------------------------------------------------------------------------------------------------- the operations

    <RemoteAdminOp(ExchangeAdminWire.OpStatus)>
    Public Function StatusAsync(ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminStatusView))
        Return RunAsync(Of RemoteAdminStatusView)(ExchangeAdminWire.OpStatus, Function(t As String, i As Long) ExchangeAdminWire.Status(t, i), AddressOf ReadStatus, ct)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpPolicyStatus)>
    Public Function PolicyStatusAsync(ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminPolicyView))
        Return RunAsync(Of RemoteAdminPolicyView)(ExchangeAdminWire.OpPolicyStatus, Function(t As String, i As Long) ExchangeAdminWire.PolicyStatus(t, i), AddressOf ReadPolicy, ct)
    End Function

    ''' <summary>Changes the cap on accounts and/or the defaults a new account gets; what is left Nothing stays as it is on the server
    ''' (it merges). <paramref name="maxAccounts"/> Nothing is "not changed" - never a 0 - and below 1 is refused before anything is
    ''' written (`bad-limit`); so is a negative limit. The result's `Persisted` False means the change holds until the server restarts.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpPolicySet)>
    Public Async Function PolicySetAsync(maxAccounts As Integer?, limits As RemoteAdminLimits, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminPolicyView))
        If (maxAccounts.HasValue AndAlso maxAccounts.Value < 1) OrElse (limits IsNot Nothing AndAlso limits.HasNegative) Then
            Return RemoteAdminResult(Of RemoteAdminPolicyView).MakeOf(Refused(ExchangeAdminCodes.BadLimit), Nothing)
        End If
        Dim wire As ExchangeAdminWireLimits = If(limits Is Nothing, Nothing, limits.ToWire())
        Return Await RunAsync(Of RemoteAdminPolicyView)(ExchangeAdminWire.OpPolicySet, Function(t As String, i As Long) ExchangeAdminWire.PolicySet(t, i, maxAccounts, wire), AddressOf ReadPolicy, ct).ConfigureAwait(False)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpAccountList)>
    Public Function AccountListAsync(ct As CancellationToken) As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminAccountRow)))
        Return RunAsync(Of IReadOnlyList(Of RemoteAdminAccountRow))(ExchangeAdminWire.OpAccountList, Function(t As String, i As Long) ExchangeAdminWire.AccountList(t, i),
                                                                    Function(el As JsonElement) ReadList(Of RemoteAdminAccountRow)(el, AddressOf ReadAccount), ct)
    End Function

    ''' <summary>The server normalises the login (trim and lower-case): the result's `Login` is what to show. An empty kind means member.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpAccountAdd)>
    Public Function AccountAddAsync(login As String, password As String, kind As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminAccountAdded))
        Return RunAsync(Of RemoteAdminAccountAdded)(ExchangeAdminWire.OpAccountAdd, Function(t As String, i As Long) ExchangeAdminWire.AccountAdd(t, i, login, password, kind), AddressOf ReadAccountAdded, ct)
    End Function

    ''' <summary>Every device of the account is signed out by the server.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpAccountPasswd)>
    Public Function AccountPasswdAsync(login As String, password As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminPasswordChanged))
        Return RunAsync(Of RemoteAdminPasswordChanged)(ExchangeAdminWire.OpAccountPasswd, Function(t As String, i As Long) ExchangeAdminWire.AccountPasswd(t, i, login, password), AddressOf ReadPasswordChanged, ct)
    End Function

    ''' <summary>The server reads an empty kind as MEMBER, never as "unchanged", so an empty kind is refused here before anything is
    ''' written (`bad-kind`). The result's value is the server's `changed`.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpAccountKind)>
    Public Async Function AccountKindAsync(login As String, kind As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of Boolean))
        If String.IsNullOrEmpty(kind) Then Return RemoteAdminResult(Of Boolean).MakeOf(Refused(ExchangeAdminCodes.BadKind), False)
        Return Await RunAsync(Of Boolean)(ExchangeAdminWire.OpAccountKind, Function(t As String, i As Long) ExchangeAdminWire.AccountKind(t, i, login, kind), AddressOf ReadChanged, ct).ConfigureAwait(False)
    End Function

    ''' <summary>The server REPLACES the account's whole override object: the caller passes every member that is set after the edit, and
    ''' Nothing or an empty value clears every override. A negative member is refused before anything is written (`bad-limit`).</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpAccountLimits)>
    Public Async Function AccountLimitsAsync(login As String, limits As RemoteAdminLimits, ct As CancellationToken) As Task(Of RemoteAdminResult)
        If limits IsNot Nothing AndAlso limits.HasNegative Then Return RemoteAdminResult.Make(Refused(ExchangeAdminCodes.BadLimit))
        Dim wire As ExchangeAdminWireLimits = If(limits Is Nothing, Nothing, limits.ToWire())
        Return Await RunNoDataAsync(ExchangeAdminWire.OpAccountLimits, Function(t As String, i As Long) ExchangeAdminWire.AccountLimits(t, i, login, wire), ct).ConfigureAwait(False)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpAccountRemove)>
    Public Function AccountRemoveAsync(login As String, ct As CancellationToken) As Task(Of RemoteAdminResult)
        Return RunNoDataAsync(ExchangeAdminWire.OpAccountRemove, Function(t As String, i As Long) ExchangeAdminWire.AccountRemove(t, i, login), ct)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpDeviceList)>
    Public Function DeviceListAsync(login As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminDeviceRow)))
        Return RunAsync(Of IReadOnlyList(Of RemoteAdminDeviceRow))(ExchangeAdminWire.OpDeviceList, Function(t As String, i As Long) ExchangeAdminWire.DeviceList(t, i, login),
                                                                   Function(el As JsonElement) ReadList(Of RemoteAdminDeviceRow)(el, AddressOf ReadDevice), ct)
    End Function

    ''' <summary>`not-found` is the server's answer for a device that is gone already.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpDeviceRevoke)>
    Public Function DeviceRevokeAsync(login As String, deviceId As String, ct As CancellationToken) As Task(Of RemoteAdminResult)
        Return RunNoDataAsync(ExchangeAdminWire.OpDeviceRevoke, Function(t As String, i As Long) ExchangeAdminWire.DeviceRevoke(t, i, login, deviceId), ct)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpDevicePairingCode)>
    Public Function DevicePairingCodeAsync(login As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminPairingCode))
        Return RunAsync(Of RemoteAdminPairingCode)(ExchangeAdminWire.OpDevicePairingCode, Function(t As String, i As Long) ExchangeAdminWire.DevicePairingCode(t, i, login), AddressOf ReadPairingCode, ct)
    End Function

    ''' <summary><paramref name="expectedLogin"/> is optional (Nothing or "" sends no member).</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpInvitationCreate)>
    Public Function InvitationCreateAsync(kind As String, expectedLogin As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminInvitationCreated))
        Return RunAsync(Of RemoteAdminInvitationCreated)(ExchangeAdminWire.OpInvitationCreate, Function(t As String, i As Long) ExchangeAdminWire.InvitationCreate(t, i, kind, expectedLogin), AddressOf ReadInvitationCreated, ct)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpInvitationList)>
    Public Function InvitationListAsync(ct As CancellationToken) As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminInvitationRow)))
        Return RunAsync(Of IReadOnlyList(Of RemoteAdminInvitationRow))(ExchangeAdminWire.OpInvitationList, Function(t As String, i As Long) ExchangeAdminWire.InvitationList(t, i),
                                                                       Function(el As JsonElement) ReadList(Of RemoteAdminInvitationRow)(el, AddressOf ReadInvitation), ct)
    End Function

    ''' <summary><paramref name="codeHash"/> is the full hash the list returned.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpInvitationWithdraw)>
    Public Function InvitationWithdrawAsync(codeHash As String, ct As CancellationToken) As Task(Of RemoteAdminResult)
        Return RunNoDataAsync(ExchangeAdminWire.OpInvitationWithdraw, Function(t As String, i As Long) ExchangeAdminWire.InvitationWithdraw(t, i, codeHash), ct)
    End Function

    <RemoteAdminOp(ExchangeAdminWire.OpSharedInvitationStatus)>
    Public Function SharedInvitationStatusAsync(ct As CancellationToken) As Task(Of RemoteAdminResult(Of RemoteAdminSharedStatus))
        Return RunAsync(Of RemoteAdminSharedStatus)(ExchangeAdminWire.OpSharedInvitationStatus, Function(t As String, i As Long) ExchangeAdminWire.SharedInvitationStatus(t, i), AddressOf ReadSharedStatus, ct)
    End Function

    ''' <summary>Turns the shared invitation ON with this password and kind. An EMPTY password turns it off on the server, so the "on" form
    ''' refuses an empty one before anything is written (`weak-password`); the off form is <see cref="SharedInvitationOffAsync"/>.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpSharedInvitationSet)>
    Public Async Function SharedInvitationSetAsync(password As String, kind As String, ct As CancellationToken) As Task(Of RemoteAdminResult)
        If String.IsNullOrEmpty(password) Then Return RemoteAdminResult.Make(Refused(ExchangeAdminCodes.WeakPassword))
        Return Await RunNoDataAsync(ExchangeAdminWire.OpSharedInvitationSet, Function(t As String, i As Long) ExchangeAdminWire.SharedInvitationSet(t, i, password, kind), ct).ConfigureAwait(False)
    End Function

    ''' <summary>Turns the shared invitation off: the same operation with the empty password the server reads as "off" (the kind is still
    ''' validated by the server). Its own method, so "off" is never a surprise left to an empty field.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpSharedInvitationSet)>
    Public Function SharedInvitationOffAsync(kind As String, ct As CancellationToken) As Task(Of RemoteAdminResult)
        Return RunNoDataAsync(ExchangeAdminWire.OpSharedInvitationSet, Function(t As String, i As Long) ExchangeAdminWire.SharedInvitationSet(t, i, "", kind), ct)
    End Function

    ''' <summary>An owner's set (<paramref name="login"/>) or a guest's (<paramref name="guestAccount"/>); with neither the server answers an
    ''' empty list. Both given is refused before anything is written (`bad-request`): the server would silently take the guest.</summary>
    <RemoteAdminOp(ExchangeAdminWire.OpGrantList)>
    Public Async Function GrantListAsync(login As String, guestAccount As String, ct As CancellationToken) As Task(Of RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)))
        If Not String.IsNullOrEmpty(login) AndAlso Not String.IsNullOrEmpty(guestAccount) Then
            Return RemoteAdminResult(Of IReadOnlyList(Of RemoteAdminGrantRow)).MakeOf(Refused(ExchangeAdminCodes.BadRequest), Nothing)
        End If
        Return Await RunAsync(Of IReadOnlyList(Of RemoteAdminGrantRow))(ExchangeAdminWire.OpGrantList, Function(t As String, i As Long) ExchangeAdminWire.GrantList(t, i, login, guestAccount),
                                                                        Function(el As JsonElement) ReadList(Of RemoteAdminGrantRow)(el, AddressOf ReadGrant), ct).ConfigureAwait(False)
    End Function

    ' ----------------------------------------------------------------------------------------------------- the call

    Private Shared Function Refused(code As String) As ExchangeRemoteAdminOutcome
        Return ExchangeRemoteAdminOutcome.NewFailed(code)
    End Function

    ''' <summary>The outcome again with no body, so nothing that holds a JsonElement leaves this class.</summary>
    Private Shared Function Plain(outcome As ExchangeRemoteAdminOutcome) As ExchangeRemoteAdminOutcome
        Select Case outcome.Kind
            Case ExchangeRemoteAdminOutcomeKind.Done
                Dim none As JsonElement = Nothing
                Return ExchangeRemoteAdminOutcome.NewDone(False, none)
            Case ExchangeRemoteAdminOutcomeKind.Failed
                Return ExchangeRemoteAdminOutcome.NewFailed(outcome.Code)
            Case ExchangeRemoteAdminOutcomeKind.Lapsed
                Return ExchangeRemoteAdminOutcome.NewLapsed(outcome.Detail)
            Case ExchangeRemoteAdminOutcomeKind.Unreachable
                Return ExchangeRemoteAdminOutcome.NewUnreachable(outcome.Reason, outcome.Detail)
            Case ExchangeRemoteAdminOutcomeKind.Protocol
                Return ExchangeRemoteAdminOutcome.NewProtocol(outcome.Detail)
            Case Else
                Return ExchangeRemoteAdminOutcome.NewBusy()
        End Select
    End Function

    ''' <summary>One call and its body read into a value. A Done reply whose body does not read is a Protocol result.</summary>
    Private Async Function RunAsync(Of T)(op As String, members As Func(Of String, Long, ExchangeEnvelope), read As Func(Of JsonElement, T), ct As CancellationToken) As Task(Of RemoteAdminResult(Of T))
        Dim outcome As ExchangeRemoteAdminOutcome = Await _session.CallAsync(op, members, ct).ConfigureAwait(False)
        Dim result As RemoteAdminResult(Of T) = Nothing
        Try
            If outcome.Kind = ExchangeRemoteAdminOutcomeKind.Done Then
                Dim value As T = Nothing
                Dim problem As String = ""
                If Not outcome.HasData Then
                    problem = "no-data"
                Else
                    Try
                        value = read.Invoke(outcome.Data)
                    Catch ex As RemoteAdminShapeException
                        problem = ex.Reason
                    Catch ex As Exception When TypeOf ex Is FormatException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is OverflowException OrElse
                                               TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is ArgumentException
                        problem = "body-" & ex.GetType().Name
                    End Try
                End If
                If problem.Length = 0 Then
                    result = RemoteAdminResult(Of T).MakeOf(Plain(outcome), value)
                Else
                    result = RemoteAdminResult(Of T).MakeOf(ExchangeRemoteAdminOutcome.NewProtocol(problem), Nothing)
                End If
            Else
                result = RemoteAdminResult(Of T).MakeOf(Plain(outcome), Nothing)
            End If
        Finally
            outcome.Dispose()
        End Try
        Await FinishAsync(op, result.Kind).ConfigureAwait(False)
        Return result
    End Function

    ''' <summary>One call whose answer carries no data (a body that is present is tolerated and dropped).</summary>
    Private Async Function RunNoDataAsync(op As String, members As Func(Of String, Long, ExchangeEnvelope), ct As CancellationToken) As Task(Of RemoteAdminResult)
        Dim outcome As ExchangeRemoteAdminOutcome = Await _session.CallAsync(op, members, ct).ConfigureAwait(False)
        Dim result As RemoteAdminResult = Nothing
        Try
            result = RemoteAdminResult.Make(Plain(outcome))
        Finally
            outcome.Dispose()
        End Try
        Await FinishAsync(op, result.Kind).ConfigureAwait(False)
        Return result
    End Function

    ''' <summary>Ends the session after a Protocol result (5.5) and after an Unreachable result of a mutating operation (5.5, section 6).
    ''' EndAsync is idempotent: a session that ended itself on the same result is not written to again.</summary>
    Private Async Function FinishAsync(op As String, kind As ExchangeRemoteAdminOutcomeKind) As Task
        If kind = ExchangeRemoteAdminOutcomeKind.Protocol OrElse (kind = ExchangeRemoteAdminOutcomeKind.Unreachable AndAlso IsMutating(op)) Then
            Try
                Await _session.EndAsync().ConfigureAwait(False)
            Catch ex As Exception
                ' the end is best effort and the result is decided; the session logged what it saw
            End Try
        End If
    End Function

    ' ----------------------------------------------------------------------------------------------------- the readers

    Private Shared Function TryMember(el As JsonElement, name As String, ByRef value As JsonElement) As Boolean
        If el.ValueKind <> JsonValueKind.Object Then Throw New RemoteAdminShapeException("not-object")
        If Not el.TryGetProperty(name, value) Then Return False
        Return value.ValueKind <> JsonValueKind.Null
    End Function

    Private Shared Function OptInt(el As JsonElement, name As String) As Integer?
        Dim v As JsonElement = Nothing
        If Not TryMember(el, name, v) Then Return Nothing
        Dim n As Integer
        If v.ValueKind <> JsonValueKind.Number OrElse Not v.TryGetInt32(n) Then Throw New RemoteAdminShapeException("kind-" & name)
        Return n
    End Function

    Private Shared Function OptLong(el As JsonElement, name As String) As Long?
        Dim v As JsonElement = Nothing
        If Not TryMember(el, name, v) Then Return Nothing
        Dim n As Long
        If v.ValueKind <> JsonValueKind.Number OrElse Not v.TryGetInt64(n) Then Throw New RemoteAdminShapeException("kind-" & name)
        Return n
    End Function

    Private Shared Function OptBool(el As JsonElement, name As String) As Boolean?
        Dim v As JsonElement = Nothing
        If Not TryMember(el, name, v) Then Return Nothing
        If v.ValueKind = JsonValueKind.True Then Return True
        If v.ValueKind = JsonValueKind.False Then Return False
        Throw New RemoteAdminShapeException("kind-" & name)
    End Function

    ''' <summary>A text member; "" when it is absent. A member of another kind is a shape failure.</summary>
    Private Shared Function TextOf(el As JsonElement, name As String) As String
        Dim v As JsonElement = Nothing
        If Not TryMember(el, name, v) Then Return ""
        If v.ValueKind <> JsonValueKind.String Then Throw New RemoteAdminShapeException("kind-" & name)
        Return If(v.GetString(), "")
    End Function

    ''' <summary>A text member that must be there and not empty.</summary>
    Private Shared Function RequiredText(el As JsonElement, name As String) As String
        Dim s As String = TextOf(el, name)
        If s.Length = 0 Then Throw New RemoteAdminShapeException("missing-" & name)
        Return s
    End Function

    Private Shared Function Flag(el As JsonElement, name As String) As Boolean
        Dim b As Boolean? = OptBool(el, name)
        Return b.HasValue AndAlso b.Value
    End Function

    ''' <summary>The five limits of a member that is an object. <paramref name="forDefaults"/> picks the names `effective` and `defaults` use
    ''' (devices, resources, broadcasts, broadcastsPerDevice, clipboardBytes) over the names of an account's own `limits`.</summary>
    Private Shared Function ReadLimits(el As JsonElement, member As String, forDefaults As Boolean) As RemoteAdminLimits
        Dim v As JsonElement = Nothing
        If Not TryMember(el, member, v) Then Return RemoteAdminLimits.Empty
        If v.ValueKind <> JsonValueKind.Object Then Throw New RemoteAdminShapeException("kind-" & member)
        If forDefaults Then
            Return New RemoteAdminLimits(OptInt(v, "devices"), OptInt(v, "resources"), OptInt(v, "broadcasts"), OptInt(v, "broadcastsPerDevice"), OptLong(v, "clipboardBytes"))
        End If
        Return New RemoteAdminLimits(OptInt(v, "devicesPerAccount"), OptInt(v, "resourcesPerAccount"), OptInt(v, "broadcastsPerAccount"), OptInt(v, "broadcastsPerDevice"), OptLong(v, "clipboardMaxBytes"))
    End Function

    Private Shared Function ReadList(Of R)(el As JsonElement, row As Func(Of JsonElement, R)) As IReadOnlyList(Of R)
        If el.ValueKind <> JsonValueKind.Array Then Throw New RemoteAdminShapeException("not-array")
        Dim list As New List(Of R)()
        For Each item As JsonElement In el.EnumerateArray()
            list.Add(row.Invoke(item))
        Next
        Return list.AsReadOnly()
    End Function

    Private Shared Function ReadStatus(el As JsonElement) As RemoteAdminStatusView
        Return New RemoteAdminStatusView(OptInt(el, "accounts"), OptInt(el, "maxAccounts"), OptInt(el, "devices"), OptInt(el, "online"), OptInt(el, "resources"),
                                         OptInt(el, "broadcasts"), OptInt(el, "tombstones"), OptInt(el, "clipboards"), OptLong(el, "clipboardBytes"),
                                         OptBool(el, "keepAwakeActive"), OptBool(el, "keepAwake"), OptInt(el, "logRetentionDays"), OptBool(el, "sealed"))
    End Function

    Private Shared Function ReadPolicy(el As JsonElement) As RemoteAdminPolicyView
        Dim persisted As Boolean? = OptBool(el, "persisted")
        Return New RemoteAdminPolicyView(OptInt(el, "maxAccounts"), OptInt(el, "accounts"), ReadLimits(el, "defaults", True), If(persisted.HasValue, persisted.Value, True))
    End Function

    Private Shared Function ReadAccount(el As JsonElement) As RemoteAdminAccountRow
        Dim kind As String = TextOf(el, "kind")
        Dim devices As Integer? = OptInt(el, "devices")
        Return New RemoteAdminAccountRow(RequiredText(el, "login"), If(kind.Length = 0, "member", kind), If(devices.HasValue, devices.Value, 0),
                                         ReadLimits(el, "limits", False), ReadLimits(el, "effective", True), TextOf(el, "createdAt"))
    End Function

    Private Shared Function ReadAccountAdded(el As JsonElement) As RemoteAdminAccountAdded
        Return New RemoteAdminAccountAdded(RequiredText(el, "login"), Flag(el, "overCap"))
    End Function

    Private Shared Function ReadPasswordChanged(el As JsonElement) As RemoteAdminPasswordChanged
        Dim n As Integer? = OptInt(el, "signedOut")
        If Not n.HasValue Then Throw New RemoteAdminShapeException("missing-signedOut")
        Return New RemoteAdminPasswordChanged(n.Value)
    End Function

    Private Shared Function ReadChanged(el As JsonElement) As Boolean
        Dim b As Boolean? = OptBool(el, "changed")
        If Not b.HasValue Then Throw New RemoteAdminShapeException("missing-changed")
        Return b.Value
    End Function

    Private Shared Function ReadDevice(el As JsonElement) As RemoteAdminDeviceRow
        Dim roles As New List(Of String)()
        Dim rolesEl As JsonElement = Nothing
        If TryMember(el, "roles", rolesEl) AndAlso rolesEl.ValueKind = JsonValueKind.Array Then
            For Each r As JsonElement In rolesEl.EnumerateArray()
                If r.ValueKind = JsonValueKind.String Then roles.Add(If(r.GetString(), ""))
            Next
        End If
        Return New RemoteAdminDeviceRow(RequiredText(el, "deviceId"), TextOf(el, "deviceName"), TextOf(el, "product"), TextOf(el, "productVersion"), TextOf(el, "platform"),
                                        roles.AsReadOnly(), TextOf(el, "enrolledAt"), TextOf(el, "lastSeen"), Flag(el, "online"), Flag(el, "signedOut"))
    End Function

    Private Shared Function ReadPairingCode(el As JsonElement) As RemoteAdminPairingCode
        Return New RemoteAdminPairingCode(RequiredText(el, "code"), TextOf(el, "expiresAt"))
    End Function

    Private Shared Function ReadInvitation(el As JsonElement) As RemoteAdminInvitationRow
        Return New RemoteAdminInvitationRow(RequiredText(el, "codeHash"), TextOf(el, "kind"), TextOf(el, "createdAt"), TextOf(el, "expectedLogin"),
                                            Flag(el, "used"), TextOf(el, "usedAt"), TextOf(el, "usedBy"))
    End Function

    ''' <summary>The code is the point of the answer and is shown once, so everything around it is read leniently: an `invitation` that does
    ''' not read is Nothing and a `connectionString` that is not text is "", and the code is still handed over.</summary>
    Private Shared Function ReadInvitationCreated(el As JsonElement) As RemoteAdminInvitationCreated
        Dim code As String = RequiredText(el, "code")
        Dim invitation As RemoteAdminInvitationRow = Nothing
        Dim invEl As JsonElement = Nothing
        If TryMember(el, "invitation", invEl) AndAlso invEl.ValueKind = JsonValueKind.Object Then
            Try
                invitation = ReadInvitation(invEl)
            Catch ex As RemoteAdminShapeException
                invitation = Nothing
            End Try
        End If
        Dim connection As String = ""
        Dim csEl As JsonElement = Nothing
        If TryMember(el, "connectionString", csEl) AndAlso csEl.ValueKind = JsonValueKind.String Then connection = If(csEl.GetString(), "")
        Return New RemoteAdminInvitationCreated(code, invitation, connection)
    End Function

    Private Shared Function ReadSharedStatus(el As JsonElement) As RemoteAdminSharedStatus
        Dim enabled As Boolean? = OptBool(el, "enabled")
        If Not enabled.HasValue Then Throw New RemoteAdminShapeException("missing-enabled")
        Return New RemoteAdminSharedStatus(enabled.Value, TextOf(el, "kind"))
    End Function

    Private Shared Function ReadGrant(el As JsonElement) As RemoteAdminGrantRow
        Return New RemoteAdminGrantRow(TextOf(el, "grantId"), TextOf(el, "targetId"), TextOf(el, "kind"), TextOf(el, "guest"), TextOf(el, "grantedBy"), TextOf(el, "grantedAt"))
    End Function

End Class
