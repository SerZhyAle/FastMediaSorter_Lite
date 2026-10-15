#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 7.
' Reconnect state machine and bounded backoff budget (0, 500, 1000, 2000, 4000 ms; max 12 s budget).
' Pure: it reads time through an injected clock, so the budget is testable without waiting.

Friend Enum LiveSessionState
    Idle = 0
    Connecting = 1
    Buffering = 2
    Playing = 3
    Reconnecting = 4
    Ended = 5
    Failed = 6
End Enum

Friend Enum LiveTerminationKind
    None = 0
    UserStopped = 1
    MaxListeners503 = 2
    AccessDenied = 3
    NotFoundOrGone = 4
    NotAudioStream = 5
    UnsupportedMode = 6
    UnsupportedVersion = 7
    LoopbackAddress = 8
    BudgetExhausted = 9
    UnknownError = 10
    ''' <summary>Any other non-2xx answer that is not worth retrying (a 3xx is never followed, a 4xx will not change).</summary>
    HttpStatus = 11
End Enum

Friend NotInheritable Class LiveReconnectPolicy

    Public Const MaxRetries As Integer = 5
    Public Const DefaultTotalBudgetMs As Long = 12000L
    Public Const StablePlayingResetThresholdMs As Long = 10000L

    Private Shared ReadOnly BackoffDelays As Integer() = {0, 500, 1000, 2000, 4000}

    Private ReadOnly clock As Func(Of Long)
    Private budgetStartMs As Long = -1L
    Private playingSinceMs As Long = -1L

    Public Property CurrentState As LiveSessionState = LiveSessionState.Idle
    Public Property TerminationReason As LiveTerminationKind = LiveTerminationKind.None

    ''' <summary>The HTTP status behind <see cref="LiveTerminationKind.HttpStatus"/>, else 0.</summary>
    Public Property HttpStatusCode As Integer

    Public Property RetryCount As Integer = 0
    Public Property HasEverPlayed As Boolean = False

    ''' <summary>The wall-clock cap of one outage (section 7). Settable only so a test can run it short.</summary>
    Public Property TotalBudgetMs As Long = DefaultTotalBudgetMs

    Public Sub New()
        Me.New(Nothing)
    End Sub

    ''' <summary>A clock in milliseconds that only moves forward; Nothing means the process's stopwatch.</summary>
    Public Sub New(clock As Func(Of Long))
        Me.clock = If(clock, Function() Stopwatch.GetTimestamp() * 1000L \ Stopwatch.Frequency)
    End Sub

    Private Function ClockMs() As Long
        Return clock()
    End Function

    Private Sub StartBudgetIfIdle()
        If budgetStartMs < 0L Then budgetStartMs = ClockMs()
    End Sub

    ''' <summary>
    ''' Time left of this outage's budget. The attempt in flight is cut at this, so "12 s" is a wall-clock
    ''' bound and not merely "no new attempt after 12 s".
    ''' </summary>
    Public ReadOnly Property RemainingBudgetMs As Long
        Get
            If budgetStartMs < 0L Then Return TotalBudgetMs
            Return Math.Max(0L, TotalBudgetMs - (ClockMs() - budgetStartMs))
        End Get
    End Property

    ''' <summary>Transitions to Connecting. The outage budget starts with the first connect.</summary>
    Public Sub NoteConnecting()
        CurrentState = LiveSessionState.Connecting
        playingSinceMs = -1L
        StartBudgetIfIdle()
    End Sub

    ''' <summary>Transitions to Playing. Marks HasEverPlayed.</summary>
    Public Sub NotePlaying()
        CurrentState = LiveSessionState.Playing
        HasEverPlayed = True
        playingSinceMs = ClockMs()
    End Sub

    ''' <summary>
    ''' If it has been playing continuously for 10 s the outage is over: the retry count and the budget
    ''' start from nothing again (section 7). Returns True when it did so.
    ''' </summary>
    Public Function CheckPlayingLiveness() As Boolean
        If CurrentState = LiveSessionState.Playing AndAlso playingSinceMs >= 0L AndAlso
           ClockMs() - playingSinceMs >= StablePlayingResetThresholdMs Then
            ResetRetries()
            Return True
        End If
        Return False
    End Function

    ''' <summary>Resets the retry counter and the budget.</summary>
    Public Sub ResetRetries()
        RetryCount = 0
        budgetStartMs = -1L
    End Sub

    ''' <summary>
    ''' Whether another reconnect fits in 5 retries and the budget. Returns the wait before it, in ms, or
    ''' Nothing when the budget is spent (the state then becomes Failed with BudgetExhausted).
    ''' A wait that would not end inside the budget is not started.
    ''' </summary>
    Public Function GetNextBackoffDelayMs() As Integer?
        StartBudgetIfIdle()

        If RetryCount >= MaxRetries Then Return Exhausted()

        Dim remaining As Long = RemainingBudgetMs
        If remaining <= 0L Then Return Exhausted()

        Dim delay As Integer = BackoffDelays(Math.Min(RetryCount, BackoffDelays.Length - 1))
        If delay > 0 AndAlso delay >= remaining Then Return Exhausted()

        RetryCount += 1
        CurrentState = LiveSessionState.Reconnecting
        Return delay
    End Function

    Private Function Exhausted() As Integer?
        CurrentState = LiveSessionState.Failed
        TerminationReason = LiveTerminationKind.BudgetExhausted
        Return Nothing
    End Function

    ''' <summary>
    ''' A drift rejoin is not a failure: it reconnects at once, spends no retry and starts no outage.
    ''' </summary>
    Public Sub NoteRejoin()
        CurrentState = LiveSessionState.Reconnecting
        playingSinceMs = -1L
    End Sub

    ''' <summary>Marks the session failed with a specific reason that no retry would change.</summary>
    Public Sub NoteFatalTermination(reason As LiveTerminationKind, Optional httpStatus As Integer = 0)
        CurrentState = LiveSessionState.Failed
        TerminationReason = reason
        HttpStatusCode = httpStatus
        budgetStartMs = -1L
        playingSinceMs = -1L
    End Sub

    ''' <summary>Marks the session stopped by the user (a clean end).</summary>
    Public Sub NoteUserStopped()
        CurrentState = LiveSessionState.Ended
        TerminationReason = LiveTerminationKind.UserStopped
        budgetStartMs = -1L
        playingSinceMs = -1L
    End Sub

    ''' <summary>Formats the user-facing status message upon termination.</summary>
    Public Function GetTerminalStatusText() As String
        Select Case TerminationReason
            Case LiveTerminationKind.MaxListeners503
                Return Localization.T("На часах уже максимальное число слушателей")
            Case LiveTerminationKind.AccessDenied
                Return Localization.T("Устройство отказало в доступе")
            Case LiveTerminationKind.NotFoundOrGone
                Return Localization.T("Трансляция больше недоступна")
            Case LiveTerminationKind.NotAudioStream
                Return Localization.T("Устройство вернуло не аудиопоток")
            Case LiveTerminationKind.UnsupportedMode
                Return Localization.T("Этот режим пока не поддерживается")
            Case LiveTerminationKind.UnsupportedVersion
                Return Localization.T("Требуется обновить приложение для этой трансляции")
            Case LiveTerminationKind.LoopbackAddress
                Return Localization.T("Устройство передало локальный адрес (loopback), недоступный с ПК")
            Case LiveTerminationKind.HttpStatus
                If HttpStatusCode = 429 Then Return Localization.T("Слишком много попыток. Подождите минуту и повторите.")
                Return Localization.TF("Устройство ответило: HTTP {0}", HttpStatusCode)
            Case LiveTerminationKind.BudgetExhausted
                If HasEverPlayed Then
                    Return Localization.T("Трансляция завершена или устройство недоступно")
                Else
                    Return Localization.T("Не удалось подключиться к трансляции")
                End If
            Case Else
                Return Localization.T("Трансляция завершена")
        End Select
    End Function

End Class
#End If
