Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' 105 L03 (057.S15-5): only "Unicode unsupported" reaches the ANSI call, cancellation stays
''' cancellation, every other failure - including one thrown outside the sender's own Try - is a
''' Failed outcome, and Failed always leads the callers to the manual route.
''' </summary>
Public Class MailSenderBehaviorTests

    Private NotInheritable Class Probe
        Friend UnicodeCalls As Integer
        Friend AnsiCalls As Integer
    End Class

    Private Shared Function Attachment() As String
        Dim p As String = Path.GetTempFileName()
        File.WriteAllText(p, "x")
        Return p
    End Function

    Private Shared Function Unicode(probe As Probe, status As MailSender.UnicodeMapiResult, rc As Integer) As MailSender.UnicodeMapiSendDelegate
        Return Function(path As String, subject As String, body As String, owner As IntPtr, ByRef code As Integer) As MailSender.UnicodeMapiResult
                   probe.UnicodeCalls += 1
                   code = rc
                   Return status
               End Function
    End Function

    Private Shared Function Ansi(probe As Probe, answered As Boolean, rc As Integer) As MailSender.AnsiMapiSendDelegate
        Return Function(path As String, subject As String, body As String, owner As IntPtr, ByRef code As Integer) As Boolean
                   probe.AnsiCalls += 1
                   code = rc
                   Return answered
               End Function
    End Function

    Private Shared Function Run(probe As Probe, unicodeSender As MailSender.UnicodeMapiSendDelegate, ansiSender As MailSender.AnsiMapiSendDelegate) As MailSender.MailSendOutcome
        Dim file As String = Attachment()
        Try
            Return MailSender.SendCore(file, "s", "b", IntPtr.Zero, unicodeSender, ansiSender)
        Finally
            IO.File.Delete(file)
        End Try
    End Function

    <Fact>
    Public Sub Unicode_success_never_touches_ANSI()
        Dim probe As New Probe()
        Dim outcome = Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.Answered, 0), Ansi(probe, True, 0))
        Assert.Equal(MailSender.MailSendOutcome.Sent, outcome)
        Assert.Equal(1, probe.UnicodeCalls)
        Assert.Equal(0, probe.AnsiCalls)
    End Sub

    <Fact>
    Public Sub Unicode_user_abort_is_cancellation_and_does_not_retry_in_ANSI()
        Dim probe As New Probe()
        Dim outcome = Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.Answered, 1), Ansi(probe, True, 0))
        Assert.Equal(MailSender.MailSendOutcome.Cancelled, outcome)
        Assert.Equal(0, probe.AnsiCalls)
    End Sub

    <Fact>
    Public Sub Unicode_failure_code_is_final_and_does_not_fall_back()
        Dim probe As New Probe()
        Dim outcome = Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.Answered, 2), Ansi(probe, True, 0))
        Assert.Equal(MailSender.MailSendOutcome.Failed, outcome)
        Assert.Equal(0, probe.AnsiCalls)
    End Sub

    <Fact>
    Public Sub Only_unsupported_falls_back_and_the_ANSI_answer_decides()
        For Each ansiRc As Integer In New Integer() {0, 1, 3}
            Dim probe As New Probe()
            Dim outcome = Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.NotSupported, 26), Ansi(probe, True, ansiRc))
            Assert.Equal(1, probe.AnsiCalls)
            Assert.Equal(If(ansiRc = 0, MailSender.MailSendOutcome.Sent, If(ansiRc = 1, MailSender.MailSendOutcome.Cancelled, MailSender.MailSendOutcome.Failed)), outcome)
        Next
    End Sub

    <Fact>
    Public Sub An_ANSI_call_that_could_not_be_made_is_Failed()
        Dim probe As New Probe()
        Assert.Equal(MailSender.MailSendOutcome.Failed,
                     Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.NotSupported, 26), Ansi(probe, False, 0)))
        Assert.Equal(MailSender.MailSendOutcome.Failed,
                     Run(New Probe(), Unicode(New Probe(), MailSender.UnicodeMapiResult.NotSupported, 26), Nothing))
    End Sub

    <Fact>
    Public Sub An_execution_failure_in_Unicode_is_Failed_without_ANSI()
        Dim probe As New Probe()
        Assert.Equal(MailSender.MailSendOutcome.Failed,
                     Run(probe, Unicode(probe, MailSender.UnicodeMapiResult.ExecutionFailed, 0), Ansi(probe, True, 0)))
        Assert.Equal(0, probe.AnsiCalls)
    End Sub

    <Fact>
    Public Sub A_sender_that_throws_outside_its_own_try_still_yields_Failed_not_an_exception()
        Dim thrower As MailSender.UnicodeMapiSendDelegate =
            Function(path As String, subject As String, body As String, owner As IntPtr, ByRef code As Integer) As MailSender.UnicodeMapiResult
                Throw New System.Runtime.InteropServices.COMException("MAPI client is broken")
            End Function
        Dim probe As New Probe()
        Dim outcome As MailSender.MailSendOutcome = Run(probe, thrower, Ansi(probe, True, 0))
        Assert.Equal(MailSender.MailSendOutcome.Failed, outcome)
        Assert.Equal(0, probe.AnsiCalls)

        Dim ansiThrower As MailSender.AnsiMapiSendDelegate =
            Function(path As String, subject As String, body As String, owner As IntPtr, ByRef code As Integer) As Boolean
                Throw New AccessViolationException("ansi")
            End Function
        Assert.Equal(MailSender.MailSendOutcome.Failed,
                     Run(New Probe(), Unicode(New Probe(), MailSender.UnicodeMapiResult.NotSupported, 26), ansiThrower))
    End Sub

    <Fact>
    Public Sub A_missing_attachment_is_Failed_and_calls_nothing()
        Dim probe As New Probe()
        Dim outcome = MailSender.SendCore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "s", "b", IntPtr.Zero,
                                          Unicode(probe, MailSender.UnicodeMapiResult.Answered, 0), Ansi(probe, True, 0))
        Assert.Equal(MailSender.MailSendOutcome.Failed, outcome)
        Assert.Equal(0, probe.UnicodeCalls)
    End Sub

    <Fact>
    Public Sub Both_callers_take_the_manual_route_on_Failed_and_discard_the_archive_only_on_cancel()
        ' Viewer (Table_Form.SendLogs) and Share Manager (SendLogsFlow) switch on this one decision.
        Assert.Equal(MailSender.MailFollowUp.Finished, MailSender.FollowUpFor(MailSender.MailSendOutcome.Sent))
        Assert.Equal(MailSender.MailFollowUp.DiscardArchive, MailSender.FollowUpFor(MailSender.MailSendOutcome.Cancelled))
        Assert.Equal(MailSender.MailFollowUp.ManualRoute, MailSender.FollowUpFor(MailSender.MailSendOutcome.Failed))
    End Sub

End Class
