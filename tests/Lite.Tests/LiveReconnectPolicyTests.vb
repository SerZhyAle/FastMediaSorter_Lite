#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

Public Class LiveReconnectPolicyTests

    <Fact>
    Public Sub Backoff_Delays_Are_0_500_1000_2000_4000_Then_Exhausted()
        Dim policy As New LiveReconnectPolicy()
        policy.NoteConnecting()

        ' 1st retry: 0 ms
        Dim d1 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Equal(0, d1)
        Assert.Equal(LiveSessionState.Reconnecting, policy.CurrentState)

        ' 2nd retry: 500 ms
        Dim d2 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Equal(500, d2)

        ' 3rd retry: 1000 ms
        Dim d3 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Equal(1000, d3)

        ' 4th retry: 2000 ms
        Dim d4 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Equal(2000, d4)

        ' 5th retry: 4000 ms
        Dim d5 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Equal(4000, d5)

        ' 6th retry: budget exhausted -> returns Nothing
        Dim d6 As Integer? = policy.GetNextBackoffDelayMs()
        Assert.Null(d6)
        Assert.Equal(LiveSessionState.Failed, policy.CurrentState)
        Assert.Equal(LiveTerminationKind.BudgetExhausted, policy.TerminationReason)
    End Sub

    <Fact>
    Public Sub Terminal_Status_Text_Differentiates_Failed_Before_And_After_Sound()
        Dim policyBeforeSound As New LiveReconnectPolicy()
        policyBeforeSound.NoteConnecting()
        For i As Integer = 1 To 6
            policyBeforeSound.GetNextBackoffDelayMs()
        Next
        Dim msgBefore As String = policyBeforeSound.GetTerminalStatusText()
        AssertMatchesTranslation("Не удалось подключиться к трансляции", msgBefore)

        Dim policyAfterSound As New LiveReconnectPolicy()
        policyAfterSound.NoteConnecting()
        policyAfterSound.NotePlaying()
        For i As Integer = 1 To 6
            policyAfterSound.GetNextBackoffDelayMs()
        Next
        Dim msgAfter As String = policyAfterSound.GetTerminalStatusText()
        AssertMatchesTranslation("Трансляция завершена или устройство недоступно", msgAfter)
    End Sub

    <Fact>
    Public Sub Non_Retryable_Outcomes_Give_Exact_Messages()
        Dim policy As New LiveReconnectPolicy()

        policy.NoteFatalTermination(LiveTerminationKind.MaxListeners503)
        Assert.Equal(LiveTerminationKind.MaxListeners503, policy.TerminationReason)
        AssertMatchesTranslation("На часах уже максимальное число слушателей", policy.GetTerminalStatusText())

        policy.NoteFatalTermination(LiveTerminationKind.AccessDenied)
        Assert.Equal(LiveTerminationKind.AccessDenied, policy.TerminationReason)
        AssertMatchesTranslation("Устройство отказало в доступе", policy.GetTerminalStatusText())

        policy.NoteFatalTermination(LiveTerminationKind.NotFoundOrGone)
        Assert.Equal(LiveTerminationKind.NotFoundOrGone, policy.TerminationReason)
        AssertMatchesTranslation("Трансляция больше недоступна", policy.GetTerminalStatusText())

        policy.NoteFatalTermination(LiveTerminationKind.NotAudioStream)
        Assert.Equal(LiveTerminationKind.NotAudioStream, policy.TerminationReason)
        AssertMatchesTranslation("Устройство вернуло не аудиопоток", policy.GetTerminalStatusText())

        policy.NoteFatalTermination(LiveTerminationKind.UnsupportedMode)
        Assert.Equal(LiveTerminationKind.UnsupportedMode, policy.TerminationReason)
        AssertMatchesTranslation("Этот режим пока не поддерживается", policy.GetTerminalStatusText())

        ' no "unsupported version" terminal kind: a descriptor of a later schema is refused when it is read (LiveReadStatus), never in a session
        Assert.DoesNotContain("UnsupportedVersion", [Enum].GetNames(GetType(LiveTerminationKind)))

        policy.NoteFatalTermination(LiveTerminationKind.LoopbackAddress)
        Assert.Equal(LiveTerminationKind.LoopbackAddress, policy.TerminationReason)
        AssertMatchesTranslation("Устройство передало локальный адрес (loopback), недоступный с ПК", policy.GetTerminalStatusText())
    End Sub

    Private Sub AssertMatchesTranslation(key As String, actual As String)
        Dim isMatch As Boolean = String.Equals(actual, key, StringComparison.Ordinal) OrElse
                                 String.Equals(actual, Localization.T(key), StringComparison.Ordinal)
        If Not isMatch Then
            ' Also check against English fallback
            For Each kvp In Localization.All
                If String.Equals(kvp.Key, key, StringComparison.Ordinal) Then
                    If kvp.Value IsNot Nothing Then
                        For Each v In kvp.Value
                            If String.Equals(actual, v, StringComparison.Ordinal) Then
                                isMatch = True
                                Exit For
                            End If
                        Next
                    End If
                    Exit For
                End If
            Next
        End If
        Assert.True(isMatch, $"Expected translation for '{key}', got '{actual}'")
    End Sub

End Class
#End If
