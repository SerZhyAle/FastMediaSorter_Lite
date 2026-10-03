#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md §7.
' Reconnect state machine and bounded backoff budget (0, 500, 1000, 2000, 4000 ms; max 12s budget).

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
End Enum

Friend NotInheritable Class LiveReconnectPolicy

    Public Const MaxRetries As Integer = 5
    Public Const TotalBudgetMs As Long = 12000L
    Public Const StablePlayingResetThresholdMs As Long = 10000L

    Private Shared ReadOnly BackoffDelays As Integer() = {0, 500, 1000, 2000, 4000}

    Public Property CurrentState As LiveSessionState = LiveSessionState.Idle
    Public Property TerminationReason As LiveTerminationKind = LiveTerminationKind.None
    Public Property CustomErrorMessage As String = Nothing

    Public Property RetryCount As Integer = 0
    Public Property HasEverPlayed As Boolean = False

    Private ReadOnly budgetStopwatch As New Stopwatch()
    Private ReadOnly playingStopwatch As New Stopwatch()

    Public Sub New()
    End Sub

    ''' <summary>
    ''' Transitions to Connecting state.
    ''' </summary>
    Public Sub NoteConnecting()
        CurrentState = LiveSessionState.Connecting
        playingStopwatch.Reset()
    End Sub

    ''' <summary>
    ''' Transitions to Playing state. Marks HasEverPlayed.
    ''' </summary>
    Public Sub NotePlaying()
        CurrentState = LiveSessionState.Playing
        HasEverPlayed = True
        playingStopwatch.Restart()
    End Sub

    ''' <summary>
    ''' Updates active playing time. If playing continuously for 10 s, resets the retry count and budget.
    ''' </summary>
    Public Sub CheckPlayingLiveness()
        If CurrentState = LiveSessionState.Playing AndAlso playingStopwatch.IsRunning Then
            If playingStopwatch.ElapsedMilliseconds >= StablePlayingResetThresholdMs Then
                ResetRetries()
            End If
        End If
    End Sub

    ''' <summary>
    ''' Resets retry counters and budget stopwatch.
    ''' </summary>
    Public Sub ResetRetries()
        RetryCount = 0
        budgetStopwatch.Reset()
    End Sub

    ''' <summary>
    ''' Checks if another reconnect attempt is allowed within the 5-retry limit and 12s budget.
    ''' Returns the delay in milliseconds for this attempt, or Nothing if the budget is exhausted.
    ''' </summary>
    Public Function GetNextBackoffDelayMs() As Integer?
        If Not budgetStopwatch.IsRunning Then
            budgetStopwatch.Start()
        End If

        If RetryCount >= MaxRetries OrElse budgetStopwatch.ElapsedMilliseconds >= TotalBudgetMs Then
            CurrentState = LiveSessionState.Failed
            TerminationReason = LiveTerminationKind.BudgetExhausted
            Return Nothing
        End If

        Dim delay As Integer = BackoffDelays(Math.Min(RetryCount, BackoffDelays.Length - 1))
        RetryCount += 1
        CurrentState = LiveSessionState.Reconnecting
        Return delay
    End Function

    ''' <summary>
    ''' Marks session failed with a specific non-retryable termination reason.
    ''' </summary>
    Public Sub NoteFatalTermination(reason As LiveTerminationKind, Optional customMsg As String = Nothing)
        CurrentState = LiveSessionState.Failed
        TerminationReason = reason
        CustomErrorMessage = customMsg
        budgetStopwatch.Reset()
        playingStopwatch.Reset()
    End Sub

    ''' <summary>
    ''' Marks session stopped by user (clean end).
    ''' </summary>
    Public Sub NoteUserStopped()
        CurrentState = LiveSessionState.Ended
        TerminationReason = LiveTerminationKind.UserStopped
        budgetStopwatch.Reset()
        playingStopwatch.Reset()
    End Sub

    ''' <summary>
    ''' Formats the user-facing status message upon termination.
    ''' </summary>
    Public Function GetTerminalStatusText() As String
        If Not String.IsNullOrEmpty(CustomErrorMessage) Then
            Return CustomErrorMessage
        End If

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
