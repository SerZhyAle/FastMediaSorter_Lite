Option Strict On

Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Ticket 106 (057.S13-5): which of several pending "open this path" probes may open its file.
'''
''' <see cref="OpenRequestSequence"/> is the whole policy; <see cref="OpenerModel"/> is the
''' shape of Main_Form's ProcessArgumentAsync / ResumeLastPlayedFileAsync with the filesystem
''' replaced by tasks the test completes in whatever order it likes - so "A then B, B finishes
''' first" is a deterministic case rather than a race.
''' </summary>
Public Class ArgumentProbeOrderingTests

    ''' <summary>The form's open path: take a ticket, probe, apply only while still current.
    ''' Displaying a file invalidates, exactly as UpdateCurrentFileAndDisplay does.</summary>
    Private NotInheritable Class OpenerModel
        Public ReadOnly Requests As New OpenRequestSequence()
        Public ReadOnly Opened As New List(Of String)()

        Public Async Function OpenAsync(path As String, probe As Task) As Task
            Dim ticket As Integer = Requests.Accept()
            Await probe
            If Not Requests.IsCurrent(ticket) Then Return
            Opened.Add(path)
            Requests.Invalidate() ' the display moved the world on
        End Function

        Public Sub Navigate()
            Requests.Invalidate()
        End Sub
    End Class

    <Fact>
    Public Sub Two_pending_requests_both_finishing_A_then_B_open_only_the_latest()
        Dim model As New OpenerModel()
        Dim probeA As New TaskCompletionSource(Of Boolean)()
        Dim probeB As New TaskCompletionSource(Of Boolean)()

        Dim a As Task = model.OpenAsync("A", probeA.Task)
        Dim b As Task = model.OpenAsync("B", probeB.Task)

        probeA.SetResult(True)
        probeB.SetResult(True)
        Task.WaitAll(a, b)

        ' The old generation compare opened A and dropped B - the file asked for last.
        Assert.Equal(New String() {"B"}, model.Opened.ToArray())
    End Sub

    <Fact>
    Public Sub Two_pending_requests_finishing_B_then_A_open_only_the_latest()
        Dim model As New OpenerModel()
        Dim probeA As New TaskCompletionSource(Of Boolean)()
        Dim probeB As New TaskCompletionSource(Of Boolean)()

        Dim a As Task = model.OpenAsync("A", probeA.Task)
        Dim b As Task = model.OpenAsync("B", probeB.Task)

        probeB.SetResult(True)
        probeA.SetResult(True)
        Task.WaitAll(a, b)

        Assert.Equal(New String() {"B"}, model.Opened.ToArray())
    End Sub

    <Fact>
    Public Sub A_lone_request_opens()
        Dim model As New OpenerModel()
        Dim probe As New TaskCompletionSource(Of Boolean)()

        Dim a As Task = model.OpenAsync("A", probe.Task)
        probe.SetResult(True)
        a.Wait()

        Assert.Equal(New String() {"A"}, model.Opened.ToArray())
    End Sub

    <Fact>
    Public Sub An_explicit_request_during_resume_wins_whichever_probe_returns_first()
        For Each resumeFirst As Boolean In New Boolean() {True, False}
            Dim model As New OpenerModel()
            Dim resumeProbe As New TaskCompletionSource(Of Boolean)()
            Dim explicitProbe As New TaskCompletionSource(Of Boolean)()

            Dim resumeOpen As Task = model.OpenAsync("resume", resumeProbe.Task)
            Dim explicitOpen As Task = model.OpenAsync("explicit", explicitProbe.Task)

            If resumeFirst Then
                resumeProbe.SetResult(True) : explicitProbe.SetResult(True)
            Else
                explicitProbe.SetResult(True) : resumeProbe.SetResult(True)
            End If
            Task.WaitAll(resumeOpen, explicitOpen)

            Assert.Equal(New String() {"explicit"}, model.Opened.ToArray())
        Next
    End Sub

    <Fact>
    Public Sub Navigation_during_a_probe_drops_the_probe()
        Dim model As New OpenerModel()
        Dim probe As New TaskCompletionSource(Of Boolean)()

        Dim a As Task = model.OpenAsync("A", probe.Task)
        model.Navigate()
        probe.SetResult(True)
        a.Wait()

        Assert.Empty(model.Opened)
    End Sub

    <Fact>
    Public Sub Closing_with_a_probe_in_flight_opens_nothing()
        Dim model As New OpenerModel()
        Dim probe As New TaskCompletionSource(Of Boolean)()

        Dim a As Task = model.OpenAsync("A", probe.Task)
        model.Requests.Invalidate() ' FormClosed
        probe.SetResult(True)
        a.Wait()

        Assert.Empty(model.Opened)
    End Sub

    <Fact>
    Public Sub A_request_after_a_navigation_is_current_again()
        Dim sequence As New OpenRequestSequence()
        sequence.Invalidate()
        Dim ticket As Integer = sequence.Accept()
        Assert.True(sequence.IsCurrent(ticket))
    End Sub

    <Fact>
    Public Sub Every_ticket_is_obsolete_the_moment_a_newer_one_is_accepted()
        Dim sequence As New OpenRequestSequence()
        Dim first As Integer = sequence.Accept()
        Dim second As Integer = sequence.Accept()

        Assert.False(sequence.IsCurrent(first))
        Assert.True(sequence.IsCurrent(second))
        Assert.NotEqual(first, second)
    End Sub

    <Fact>
    Public Sub An_obsolete_probe_is_visible_to_a_worker_thread()
        ' The probe loop polls this from a thread-pool thread to stop retrying.
        Dim sequence As New OpenRequestSequence()
        Dim ticket As Integer = sequence.Accept()
        Dim seenObsolete As Task(Of Boolean) = Task.Run(Function()
                                                            Dim spins As Integer = 0
                                                            Do While sequence.IsCurrent(ticket) AndAlso spins < 5000
                                                                Threading.Thread.Sleep(1)
                                                                spins += 1
                                                            Loop
                                                            Return Not sequence.IsCurrent(ticket)
                                                        End Function)
        sequence.Accept()
        Assert.True(seenObsolete.Result)
    End Sub

End Class
