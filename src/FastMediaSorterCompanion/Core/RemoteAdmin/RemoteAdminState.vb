Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

' Ticket L0278 T-5 (specification sections 5.3, 5.6 and 5.6.1): the ONE state object of the remote administration window, the one
' transition function that moves it, and the banner as a pure function of it. No UI and no I/O here: the window reads everything it
' shows or enables from this object, and nothing else reads the target (5.3: "no control reads the target from anywhere else").
'
'   - RemoteAdminTransitions.Next is one pure, TOTAL function: an event that does not apply to a phase returns the phase unchanged
'     (the window is never thrown at). The table is the one of 5.6; TryNext tells whether the event applied at all.
'   - RemoteAdminState holds Target, Phase, Session, Generation and ReasonLine. A target switch builds a NEW state object with
'     Generation + 1 (ForTarget); a reply that was asked for under an older generation is dropped (Accepts, and StaleGuard for the
'     window's own loads). The state does not end a session: the window ends and disposes it BEFORE it switches, signs out or lets it
'     lapse; the state only forgets the reference once the phase leaves SignedIn, so no call can go through a dead session.
'   - Which control is enabled in which phase is a pure function of the phase here too (5.6, "Controls" column).
'   - The banner (5.6.1): the ink by phase (Warning while signed in, Info otherwise; never a danger ink) and the text from the target
'     name, host:port and the state words, plus the reason line when there is one. The state is carried by the WORDS; the ink only
'     supplements them. The words are the Companion's own table (Localization.RemoteAdmin.vb), the key is the Russian source.
'   - Single-threaded by design: the window's UI thread owns the state. A worker's reply is marshalled to it and checked with Accepts.

''' <summary>Where the remote administration window stands (5.6).</summary>
Friend Enum RemoteAdminPhase
    ''' <summary>The store is empty or the person has not chosen: the target list and "Add a server" only.</summary>
    NoTarget = 0
    ''' <summary>A target is chosen; also after Sign out, a lapse, a lock, an error.</summary>
    SignedOut = 1
    ''' <summary>Sign in pressed; the pinned connect or the probe runs (5.4).</summary>
    CheckingServer = 2
    ''' <summary>The trust decision is a first contact: the inline trust panel waits for the person.</summary>
    AskingTrust = 3
    ''' <summary>The decision is Accept; the password is on the wire.</summary>
    SigningIn = 4
    ''' <summary>admin-auth-ok: the cards are live.</summary>
    SignedIn = 5
    ''' <summary>refused{admin-disabled}: administration is off on that server.</summary>
    Disabled = 6
End Enum

''' <summary>What happened to the window. The comment on each is the transition it causes (5.6); any other phase leaves it unchanged.</summary>
Friend Enum RemoteAdminEvent
    ''' <summary>NoTarget, SignedOut, Disabled -> SignedOut.</summary>
    TargetChosen = 0
    ''' <summary>any -> NoTarget.</summary>
    TargetCleared = 1
    ''' <summary>SignedOut, Disabled -> CheckingServer ("Try again" of the Disabled card is the same click).</summary>
    SignInPressed = 2
    ''' <summary>CheckingServer -> AskingTrust (the decision is a first contact).</summary>
    TrustAsked = 3
    ''' <summary>AskingTrust -> CheckingServer (the pins are written, a new sign-in follows).</summary>
    TrustAccepted = 4
    ''' <summary>AskingTrust -> SignedOut.</summary>
    TrustDeclined = 5
    ''' <summary>CheckingServer -> SigningIn (signalled by the session asking for the password).</summary>
    DecisionAccepted = 6
    ''' <summary>SigningIn -> SignedIn.</summary>
    SignInOk = 7
    ''' <summary>CheckingServer, SigningIn -> Disabled.</summary>
    SignInDisabled = 8
    ''' <summary>CheckingServer, SigningIn -> SignedOut (refused, unreachable, certificate changed).</summary>
    SignInFailed = 9
    ''' <summary>CheckingServer, SigningIn -> SignedOut with no reason line (cancellation is an outcome, not an error).</summary>
    Cancelled = 10
    ''' <summary>SignedIn -> SignedOut (Sign out, a lapse, unreachable, protocol, lock, sleep).</summary>
    SessionEnded = 11
End Enum

''' <summary>The banner's ink, by registered APP-STYLE role. "danger" marks an acting button or a refusal and is never the banner's.</summary>
Friend Enum RemoteAdminInk
    ''' <summary>The role info: every phase but SignedIn.</summary>
    Info = 0
    ''' <summary>The role warning: SignedIn, the state in which a click changes a machine somewhere else.</summary>
    Warning = 1
End Enum

''' <summary>The one transition function of the window (5.6). Pure and total.</summary>
Friend NotInheritable Class RemoteAdminTransitions

    Private Structure Rule
        Friend ReadOnly OnEvent As RemoteAdminEvent
        Friend ReadOnly FromPhase As RemoteAdminPhase
        Friend ReadOnly ToPhase As RemoteAdminPhase

        Friend Sub New(onEvent As RemoteAdminEvent, fromPhase As RemoteAdminPhase, toPhase As RemoteAdminPhase)
            Me.OnEvent = onEvent
            Me.FromPhase = fromPhase
            Me.ToPhase = toPhase
        End Sub
    End Structure

    ' The table of 5.6, one row per (event, phase it applies to). TargetCleared is "any phase" and is handled before the table.
    Private Shared ReadOnly Rules As Rule() = {
        New Rule(RemoteAdminEvent.TargetChosen, RemoteAdminPhase.NoTarget, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.TargetChosen, RemoteAdminPhase.SignedOut, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.TargetChosen, RemoteAdminPhase.Disabled, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.SignInPressed, RemoteAdminPhase.SignedOut, RemoteAdminPhase.CheckingServer),
        New Rule(RemoteAdminEvent.SignInPressed, RemoteAdminPhase.Disabled, RemoteAdminPhase.CheckingServer),
        New Rule(RemoteAdminEvent.TrustAsked, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.AskingTrust),
        New Rule(RemoteAdminEvent.TrustAccepted, RemoteAdminPhase.AskingTrust, RemoteAdminPhase.CheckingServer),
        New Rule(RemoteAdminEvent.TrustDeclined, RemoteAdminPhase.AskingTrust, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.DecisionAccepted, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SigningIn),
        New Rule(RemoteAdminEvent.SignInOk, RemoteAdminPhase.SigningIn, RemoteAdminPhase.SignedIn),
        New Rule(RemoteAdminEvent.SignInDisabled, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.Disabled),
        New Rule(RemoteAdminEvent.SignInDisabled, RemoteAdminPhase.SigningIn, RemoteAdminPhase.Disabled),
        New Rule(RemoteAdminEvent.SignInFailed, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.SignInFailed, RemoteAdminPhase.SigningIn, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.Cancelled, RemoteAdminPhase.CheckingServer, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.Cancelled, RemoteAdminPhase.SigningIn, RemoteAdminPhase.SignedOut),
        New Rule(RemoteAdminEvent.SessionEnded, RemoteAdminPhase.SignedIn, RemoteAdminPhase.SignedOut)}

    Private Sub New()
    End Sub

    ''' <summary>True when the event applies to the phase, with the phase it leads to; otherwise False and <paramref name="result"/> is the
    ''' phase itself. A value that is not a member of either enumeration applies to nothing (but TargetCleared applies to any phase).</summary>
    Friend Shared Function TryNext(phase As RemoteAdminPhase, ev As RemoteAdminEvent, ByRef result As RemoteAdminPhase) As Boolean
        result = phase
        If ev = RemoteAdminEvent.TargetCleared Then
            result = RemoteAdminPhase.NoTarget
            Return True
        End If
        For Each rule As Rule In Rules
            If rule.OnEvent = ev AndAlso rule.FromPhase = phase Then
                result = rule.ToPhase
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>The phase after the event; the phase unchanged when the event does not apply to it.</summary>
    Friend Shared Function [Next](phase As RemoteAdminPhase, ev As RemoteAdminEvent) As RemoteAdminPhase
        Dim result As RemoteAdminPhase
        TryNext(phase, ev, result)
        Return result
    End Function

End Class

''' <summary>The state of one remote administration window. A target switch is a NEW object (<see cref="ForTarget"/>).</summary>
Friend NotInheritable Class RemoteAdminState

    Private _target As ExchangeAdminTarget
    Private _phase As RemoteAdminPhase
    Private _session As ExchangeRemoteAdminSession
    Private _generation As Long
    Private _reasonLine As String = ""

    Private Sub New(target As ExchangeAdminTarget, phase As RemoteAdminPhase, generation As Long)
        _target = target
        _phase = phase
        _generation = generation
    End Sub

    ''' <summary>The nothing-chosen state: NoTarget, generation 0.</summary>
    Friend Shared Function Initial() As RemoteAdminState
        Return New RemoteAdminState(Nothing, RemoteAdminPhase.NoTarget, 0L)
    End Function

    ''' <summary>The state of a target switch: a NEW object, SignedOut (NoTarget when <paramref name="target"/> is Nothing), generation of
    ''' <paramref name="previous"/> plus one, no session and no reason line. The caller ends the previous session first.</summary>
    Friend Shared Function ForTarget(previous As RemoteAdminState, target As ExchangeAdminTarget) As RemoteAdminState
        Dim generation As Long = If(previous Is Nothing, 0L, previous.Generation) + 1L
        Return New RemoteAdminState(target, If(target Is Nothing, RemoteAdminPhase.NoTarget, RemoteAdminPhase.SignedOut), generation)
    End Function

    ''' <summary>The target administered now; Nothing in NoTarget.</summary>
    Friend ReadOnly Property Target As ExchangeAdminTarget
        Get
            Return _target
        End Get
    End Property

    Friend ReadOnly Property Phase As RemoteAdminPhase
        Get
            Return _phase
        End Get
    End Property

    ''' <summary>The live session of SignedIn (and, between <see cref="SetSession"/> and the event, of SigningIn); Nothing otherwise.</summary>
    Friend ReadOnly Property Session As ExchangeRemoteAdminSession
        Get
            Return _session
        End Get
    End Property

    ''' <summary>0 for <see cref="Initial"/>, then one more for every <see cref="ForTarget"/> and every TargetCleared.</summary>
    Friend ReadOnly Property Generation As Long
        Get
            Return _generation
        End Get
    End Property

    ''' <summary>The words of why the window is signed out, "" when there is none. Set by the event that carried it; a sign-in or a
    ''' cancel clears it.</summary>
    Friend ReadOnly Property ReasonLine As String
        Get
            Return _reasonLine
        End Get
    End Property

    ''' <summary>The target's name, "" in NoTarget.</summary>
    Friend ReadOnly Property TargetName As String
        Get
            Return If(_target Is Nothing, "", _target.Name)
        End Get
    End Property

    ''' <summary>The target's host:port as the address class writes it, "" in NoTarget.</summary>
    Friend ReadOnly Property TargetAddress As String
        Get
            Return If(_target Is Nothing, "", _target.Address)
        End Get
    End Property

    ''' <summary>Moves the state by one event. An event that does not apply changes nothing, the reason line included; a state without a
    ''' target takes no event. <paramref name="reason"/> becomes the reason line of the phase the event leads to (Cancelled never has
    ''' one). TargetCleared forgets the target, bumps the generation and is the state's own NoTarget; the usual switch is
    ''' <see cref="ForTarget"/>. Leaving SignedIn forgets the session reference (the window has ended it).</summary>
    Friend Sub Apply(ev As RemoteAdminEvent, Optional reason As String = "")
        If _target Is Nothing Then Return
        Dim nextPhase As RemoteAdminPhase
        If Not RemoteAdminTransitions.TryNext(_phase, ev, nextPhase) Then Return
        If ev = RemoteAdminEvent.TargetCleared Then
            _target = Nothing
            _generation += 1L
        End If
        _phase = nextPhase
        _reasonLine = If(ev = RemoteAdminEvent.Cancelled, "", If(reason, ""))
        If _phase <> RemoteAdminPhase.SignedIn Then _session = Nothing
    End Sub

    ''' <summary>Keeps the session a sign-in produced, in SigningIn (before SignInOk) or SignedIn. False in any other phase: the session is
    ''' then not kept and the caller still owns it and must end it.</summary>
    Friend Function SetSession(session As ExchangeRemoteAdminSession) As Boolean
        If _phase <> RemoteAdminPhase.SigningIn AndAlso _phase <> RemoteAdminPhase.SignedIn Then Return False
        _session = session
        Return True
    End Function

    ''' <summary>The same target read again from the store (the pins written after a first contact, a saved edit, a forgotten certificate):
    ''' the generation does not move, because nothing asked for under it has become stale. Another id is a switch, <see cref="ForTarget"/>,
    ''' and is refused here with False, as is a state with no target.</summary>
    Friend Function ReplaceTarget(updated As ExchangeAdminTarget) As Boolean
        If updated Is Nothing OrElse _target Is Nothing Then Return False
        If Not String.Equals(updated.Id, _target.Id, StringComparison.Ordinal) Then Return False
        _target = updated
        Return True
    End Function

    ''' <summary>True when a reply asked for under <paramref name="replyGeneration"/> still belongs to this state; False means the target was
    ''' switched (or cleared) since, and the reply is dropped.</summary>
    Friend Function Accepts(replyGeneration As Long) As Boolean
        Return replyGeneration = _generation
    End Function

    ' --- which control is live in which phase (5.6, "Controls") -------------------------------------------------

    ''' <summary>The target list and "Add a server": every phase but the three where something waits on the wire or on the person. SignedIn
    ''' is one of them: a change of target ends the session first (5.3, 5.8), so the list stays live and the switch is a plain click.</summary>
    Friend ReadOnly Property CanChooseTarget As Boolean
        Get
            Return _phase = RemoteAdminPhase.NoTarget OrElse _phase = RemoteAdminPhase.SignedOut OrElse
                   _phase = RemoteAdminPhase.SignedIn OrElse _phase = RemoteAdminPhase.Disabled
        End Get
    End Property

    ''' <summary>The password field and Sign in (the Disabled card's "Try again" is the same click): a target is chosen and the phase is
    ''' SignedOut or Disabled.</summary>
    Friend ReadOnly Property CanSignIn As Boolean
        Get
            Return _target IsNot Nothing AndAlso (_phase = RemoteAdminPhase.SignedOut OrElse _phase = RemoteAdminPhase.Disabled)
        End Get
    End Property

    ''' <summary>Sign out: SignedIn only.</summary>
    Friend ReadOnly Property CanSignOut As Boolean
        Get
            Return _phase = RemoteAdminPhase.SignedIn
        End Get
    End Property

    ''' <summary>Cancel: the connect, the probe and the sign-in on the wire.</summary>
    Friend ReadOnly Property CanCancel As Boolean
        Get
            Return _phase = RemoteAdminPhase.CheckingServer OrElse _phase = RemoteAdminPhase.SigningIn
        End Get
    End Property

    ''' <summary>Edit target: a target is chosen and the phase is SignedOut (5.6 lists it there and nowhere else).</summary>
    Friend ReadOnly Property CanEditTarget As Boolean
        Get
            Return _target IsNot Nothing AndAlso _phase = RemoteAdminPhase.SignedOut
        End Get
    End Property

    ''' <summary>The six cards show their content and their actions are enabled: SignedIn only. In every other phase each card shows its
    ''' empty-state line and its actions are off.</summary>
    Friend ReadOnly Property CardsLive As Boolean
        Get
            Return _phase = RemoteAdminPhase.SignedIn
        End Get
    End Property

    ''' <summary>The inline trust panel: AskingTrust only.</summary>
    Friend ReadOnly Property ShowsTrustPanel As Boolean
        Get
            Return _phase = RemoteAdminPhase.AskingTrust
        End Get
    End Property

    ''' <summary>The dedicated card with the refusal sentence and "Try again": Disabled only.</summary>
    Friend ReadOnly Property ShowsDisabledCard As Boolean
        Get
            Return _phase = RemoteAdminPhase.Disabled
        End Get
    End Property

    ''' <summary>The type only: the target's name and the address do not belong in a log line.</summary>
    Public Overrides Function ToString() As String
        Return "RemoteAdminState"
    End Function

End Class

''' <summary>The banner band (5.6.1) as pure functions: the ink by phase, the state words by phase, the text by state. Nothing here reads
''' a session, a clock or a setting.</summary>
Friend NotInheritable Class RemoteAdminBanner

    Private Sub New()
    End Sub

    ''' <summary>Warning for SignedIn, Info for every other phase; the banner never uses the danger role.</summary>
    Friend Shared Function Ink(phase As RemoteAdminPhase) As RemoteAdminInk
        Return If(phase = RemoteAdminPhase.SignedIn, RemoteAdminInk.Warning, RemoteAdminInk.Info)
    End Function

    ''' <summary>The state words of 5.6 for one phase, in the language of the window.</summary>
    Friend Shared Function StateWords(phase As RemoteAdminPhase) As String
        Select Case phase
            Case RemoteAdminPhase.SignedOut
                Return Localization.T("Вход не выполнен")
            Case RemoteAdminPhase.CheckingServer
                Return Localization.T("Проверка сертификата сервера")
            Case RemoteAdminPhase.AskingTrust
                Return Localization.T("Ожидается ваше решение о сертификате")
            Case RemoteAdminPhase.SigningIn
                Return Localization.T("Выполняется вход")
            Case RemoteAdminPhase.SignedIn
                Return Localization.T("Вход выполнен")
            Case RemoteAdminPhase.Disabled
                Return Localization.T("На этом сервере администрирование выключено")
            Case Else
                Return Localization.T("Сервер не выбран")
        End Select
    End Function

    ''' <summary>The banner text: the target name, host:port and the state words (the exchange mark is the window's), then the reason
    ''' line on its own line when there is one. NoTarget has no name and no address. The address is wrapped for a right-to-left window
    ''' (<c>Localization.LtrValue</c>, a no-op elsewhere): a value that always reads left to right.</summary>
    Friend Shared Function Text(state As RemoteAdminState) As String
        If state Is Nothing Then Return StateWords(RemoteAdminPhase.NoTarget)
        Dim line As String
        If state.Target Is Nothing OrElse state.Phase = RemoteAdminPhase.NoTarget Then
            line = StateWords(RemoteAdminPhase.NoTarget)
        Else
            line = Localization.TF("Сервер «{0}» ({1}): {2}", state.TargetName, Localization.LtrValue(state.TargetAddress), StateWords(state.Phase))
        End If
        If state.ReasonLine.Length = 0 Then Return line
        Return String.Join(vbCrLf, {line, state.ReasonLine})
    End Function

End Class
