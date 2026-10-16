Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>How one attempt of the local join ended.</summary>
Friend Enum ExchangeLocalJoinOutcome
    ''' <summary>This PC is a device of the local server's account now, and its shared folders are published into it.</summary>
    Joined
    ''' <summary>Nothing to do and nothing done: the client features are off, an account is configured already, the person
    ''' disconnected on purpose, or the server component is not installed. Final.</summary>
    NotNeeded
    ''' <summary>No running server answered (its admin channel or its port): nothing was sent anywhere. Worth trying again.</summary>
    ServerNotRunning
    ''' <summary>The server holds no account or more than one: which one is this person's is not for the program to guess. Final.</summary>
    NotOneAccount
    ''' <summary>Something answers on the port with a certificate that is not the server's: no code was requested, nothing was sent.</summary>
    CertificateMismatch
    ''' <summary>The server would not issue a code (its limit of outstanding codes, or it went away).</summary>
    CodeRefused
    ''' <summary>The code was issued and the enrolment was refused or could not connect.</summary>
    EnrollFailed
End Enum

''' <summary>Everything the local join touches, so a test drives it with stand-ins and counts what it did.</summary>
Friend Interface IExchangeLocalJoinPorts
    ''' <summary>The client features exist in this run (<see cref="ExchangeFeatures.IsEnabled()"/>).</summary>
    Function Offered() As Boolean
    ''' <summary>An account entry is configured for this Windows user already.</summary>
    Function AlreadyConfigured() As Boolean
    ''' <summary>The person disconnected on purpose.</summary>
    Function Declined() As Boolean
    ''' <summary>The server's binary is installed with this program.</summary>
    Function ComponentPresent() As Boolean
    ''' <summary>What the binary reports for the data directory of the server that runs HERE (the registered service's, else the
    ''' per-user one). Only the address and the fingerprint of the certificate are used.</summary>
    Function Inspect() As ExchangeProcess.InspectReport
    Function ListAccountsAsync(ct As CancellationToken) As Task(Of ExchangeAdminReply)
    Function PairingCodeAsync(login As String, ct As CancellationToken) As Task(Of ExchangeAdminReply)
    ''' <summary>First contact: the certificate, no byte of envelope. Throws <see cref="ExchangeConnectException"/>.</summary>
    Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
    Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult)
    ''' <summary>Turns "publish my shared folders" on for the account that was just made and asks for a publish pass.</summary>
    Sub EnablePublishing()
    Function MachineName() As String
    Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task
End Interface

''' <summary>
''' The local join: the Share Manager of a PC that runs the Media Exchange Server becomes a device of that server by itself and publishes
''' its shared folders into the account (owner, 2026-10-12: "local hosting must become part of the exchange server on its own"; this
''' replaces the "publication is a choice, off by default" of 170 rule 1 for exactly this case - a server on THIS PC and one account).
'''
''' Rules held here, each with its test:
'''   * nothing happens unless the client features are on, no account is configured, the person has not disconnected on purpose and
'''     the component is installed; the server is NEVER started from here (133 section 3, no background resident);
'''   * it needs the RUNNING server's admin channel (the pipe): the account list must come from it, not from the offline verbs;
'''   * exactly one account: with none there is nothing to join, with several the program does not guess whose they are;
'''   * the certificate that answers on the port must be the one the binary reports for that directory before a code is even
'''     requested - no secret is sent to anything else;
'''   * what is enrolled with is a single-use pairing code of ten minutes from the admin channel, never a password, and it is wiped;
'''   * publishing is turned on only after the enrolment succeeded, so a failed join leaves no setting behind.
''' </summary>
Friend NotInheritable Class ExchangeLocalJoin
    Private Sub New()
    End Sub

    ''' <summary>A server that has just been started needs a moment to accept: attempts and pause of the certificate probe.</summary>
    Friend Const ProbeAttempts As Integer = 4
    Friend Const ProbePauseMs As Integer = 500

    ''' <summary>When attempts are made after the Share Manager starts, in seconds: the server's service may still be starting, a
    ''' sign-in may be earlier than the service. The last one ends the effort until the next start.</summary>
    Friend Shared ReadOnly Schedule As Integer() = {3, 20, 90, 300, 900}

    ''' <summary>The entry's address is this PC's own (loopback): the account of a server on this very PC. The address the server
    ''' announces for OTHER devices is not an update for it - the agent here reaches its own server by loopback, which never
    ''' depends on a router - so the "the server announced a new address" offer is not made.</summary>
    Friend Shared Function IsLocalServer(server As String) As Boolean
        Dim ep As ExchangeEndpoint = Nothing
        Return ExchangeEndpointRules.TryParse(server, ep) AndAlso ExchangeEndpointRules.IsLoopbackOrUnspecified(ep.Host)
    End Function

    ''' <summary>An outcome after which another attempt cannot help.</summary>
    Friend Shared Function IsFinal(outcome As ExchangeLocalJoinOutcome) As Boolean
        Return outcome = ExchangeLocalJoinOutcome.Joined OrElse outcome = ExchangeLocalJoinOutcome.NotNeeded OrElse
               outcome = ExchangeLocalJoinOutcome.NotOneAccount
    End Function

    Friend Shared Async Function TryJoinAsync(ports As IExchangeLocalJoinPorts, ct As CancellationToken) As Task(Of ExchangeLocalJoinOutcome)
        If Not ports.Offered() OrElse ports.Declined() OrElse ports.AlreadyConfigured() OrElse Not ports.ComponentPresent() Then
            Return ExchangeLocalJoinOutcome.NotNeeded
        End If

        ' the running server's own channel names the account(s); the offline verbs would answer for a server that is not up
        Dim listed As ExchangeAdminReply = Await ports.ListAccountsAsync(ct).ConfigureAwait(False)
        If Not listed.Ok OrElse listed.Route <> ExchangeAdminRoute.Pipe Then Return ExchangeLocalJoinOutcome.ServerNotRunning
        Dim accountList As List(Of ExchangeAdminAccount) = ExchangeAdminParser.Accounts(listed.DataJson)
        If accountList.Count <> 1 OrElse accountList(0).Login.Length = 0 Then Return ExchangeLocalJoinOutcome.NotOneAccount
        Dim login As String = accountList(0).Login

        ' what answers on the port must be this server, before anything secret exists
        ct.ThrowIfCancellationRequested()
        Dim report As ExchangeProcess.InspectReport = ports.Inspect()
        If Not report.Answered Then Return ExchangeLocalJoinOutcome.ServerNotRunning
        Dim address As String = ExchangeServerWizard.LocalAddressOf(report.Listen)
        Dim probe As ExchangeCertificateInfo = Await ProbeWithRetriesAsync(ports, address, ct).ConfigureAwait(False)
        If probe Is Nothing Then Return ExchangeLocalJoinOutcome.ServerNotRunning
        If report.Fingerprint.Length = 0 OrElse Not String.Equals(probe.Fingerprint, report.Fingerprint, StringComparison.Ordinal) Then
            Return ExchangeLocalJoinOutcome.CertificateMismatch
        End If

        ct.ThrowIfCancellationRequested()
        Dim issued As ExchangeAdminReply = Await ports.PairingCodeAsync(login, ct).ConfigureAwait(False)
        Dim code As String = If(issued.Ok AndAlso issued.Route = ExchangeAdminRoute.Pipe, ExchangeAdminParser.PairingCode(issued.DataJson), "")
        If code.Length = 0 Then Return ExchangeLocalJoinOutcome.CodeRefused

        Dim request As New ExchangeEnrollRequest With {.Server = address, .Login = login, .PairingCode = code,
            .PlatformValid = probe.PlatformValid, .AcceptedFingerprint = probe.Fingerprint, .DeviceName = ports.MachineName()}
        code = Nothing
        Dim result As ExchangeEnrollResult
        Try
            result = Await ports.EnrollAsync(request, ct).ConfigureAwait(False)
        Finally
            request.Wipe()
        End Try
        If result Is Nothing OrElse result.Outcome <> ExchangeEnrollOutcome.Enrolled Then Return ExchangeLocalJoinOutcome.EnrollFailed

        ports.EnablePublishing()
        Return ExchangeLocalJoinOutcome.Joined
    End Function

    Private Shared Async Function ProbeWithRetriesAsync(ports As IExchangeLocalJoinPorts, address As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo)
        For attempt As Integer = 1 To ProbeAttempts
            ct.ThrowIfCancellationRequested()
            Try
                Return Await ports.ProbeAsync(address, ct).ConfigureAwait(False)
            Catch ex As ExchangeConnectException
                ' not answering (refused, reset): ask again a bounded number of times
            End Try
            If attempt < ProbeAttempts Then Await ports.DelayAsync(ProbePauseMs, ct).ConfigureAwait(False)
        Next
        Return Nothing
    End Function

    ''' <summary>The attempts of one run, one after the other on <see cref="Schedule"/>, ending at the first final outcome. The ports'
    ''' delay is what waits, so a test passes none. Returns the last outcome.</summary>
    Friend Shared Async Function RunScheduleAsync(ports As IExchangeLocalJoinPorts, schedule As IReadOnlyList(Of Integer), ct As CancellationToken) As Task(Of ExchangeLocalJoinOutcome)
        Dim outcome As ExchangeLocalJoinOutcome = ExchangeLocalJoinOutcome.ServerNotRunning
        Dim elapsed As Integer = 0
        For Each at As Integer In schedule
            Dim wait As Integer = Math.Max(0, at - elapsed)
            elapsed = at
            If wait > 0 Then Await ports.DelayAsync(wait * 1000, ct).ConfigureAwait(False)
            ct.ThrowIfCancellationRequested()
            outcome = Await TryJoinAsync(ports, ct).ConfigureAwait(False)
            AppFileLogger.WriteLine("Exchange local join: " & outcome.ToString())
            If IsFinal(outcome) Then Exit For
        Next
        Return outcome
    End Function
End Class

''' <summary>The production ports: this process's own agent, the real binary and the real admin channel.</summary>
Friend NotInheritable Class ExchangeLocalJoinPorts
    Implements IExchangeLocalJoinPorts

    Private ReadOnly _accounts As ExchangeAccountStore = ExchangeAccountStore.CreateDefault()

    Public Function Offered() As Boolean Implements IExchangeLocalJoinPorts.Offered
        Return ExchangeFeatures.IsEnabled()
    End Function

    Public Function AlreadyConfigured() As Boolean Implements IExchangeLocalJoinPorts.AlreadyConfigured
        Return _accounts.IsConfigured()
    End Function

    Public Function Declined() As Boolean Implements IExchangeLocalJoinPorts.Declined
        Return _accounts.LocalJoinDeclined()
    End Function

    Public Function ComponentPresent() As Boolean Implements IExchangeLocalJoinPorts.ComponentPresent
        Return ExchangeProcess.IsAvailable()
    End Function

    Public Function Inspect() As ExchangeProcess.InspectReport Implements IExchangeLocalJoinPorts.Inspect
        ' a registered service has a directory of its own, which this user cannot open as a whole: the binary is asked about
        ' THAT directory (its config and certificate files are readable), not about the per-user fallback it would pick unasked
        Dim serviceDir As String = ExchangeAdmin.ServiceDataDir()
        Return If(serviceDir.Length > 0, ExchangeProcess.InspectIn(serviceDir), ExchangeProcess.Inspect())
    End Function

    Public Function ListAccountsAsync(ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeLocalJoinPorts.ListAccountsAsync
        Return ExchangeAdmin.ListAccountsAsync(ct)
    End Function

    Public Function PairingCodeAsync(login As String, ct As CancellationToken) As Task(Of ExchangeAdminReply) Implements IExchangeLocalJoinPorts.PairingCodeAsync
        Return ExchangeAdmin.PairingCodeAsync(login, ct)
    End Function

    Public Function ProbeAsync(server As String, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeLocalJoinPorts.ProbeAsync
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Throw New ExchangeConnectException(ExchangeConnectFailure.Network, "", "no agent")
        Return agent.ProbeCertificateAsync(server, ct)
    End Function

    Public Function EnrollAsync(request As ExchangeEnrollRequest, ct As CancellationToken) As Task(Of ExchangeEnrollResult) Implements IExchangeLocalJoinPorts.EnrollAsync
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.GetOrCreate()
        If agent Is Nothing Then Return Task.FromResult(New ExchangeEnrollResult(ExchangeEnrollOutcome.BadInput, ExchangeRefusal.Unknown, ""))
        Return agent.EnrollAsync(request, ct)
    End Function

    Public Sub EnablePublishing() Implements IExchangeLocalJoinPorts.EnablePublishing
        Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
        If agent Is Nothing OrElse agent.Publisher Is Nothing Then Return
        agent.Publisher.Settings.SetPublishShares(True)
        agent.Publisher.Notify()
    End Sub

    Public Function MachineName() As String Implements IExchangeLocalJoinPorts.MachineName
        Return Environment.MachineName
    End Function

    Public Function DelayAsync(milliseconds As Integer, ct As CancellationToken) As Task Implements IExchangeLocalJoinPorts.DelayAsync
        Return Task.Delay(milliseconds, ct)
    End Function
End Class

''' <summary>The Share Manager's start of the local join: one background effort per run, silent, bounded and cancelled at exit.</summary>
Friend NotInheritable Class ExchangeLocalJoinHost
    Private Sub New()
    End Sub

    Private Shared _cts As CancellationTokenSource

    Friend Shared Sub Start()
        If Not ExchangeFeatures.IsEnabled() Then Return
        If _cts IsNot Nothing Then Return
        _cts = New CancellationTokenSource()
        Dim token As CancellationToken = _cts.Token
        Task.Run(
            Async Function() As Task
                Try
                    Await ExchangeLocalJoin.RunScheduleAsync(New ExchangeLocalJoinPorts(), ExchangeLocalJoin.Schedule, token).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    ' the program is closing
                Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException OrElse
                                           TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ExchangeConnectException
                    AppFileLogger.WriteLine("Exchange local join: stopped - " & ex.GetType().Name)
                End Try
            End Function)
    End Sub

    Friend Shared Sub [Stop]()
        Dim c As CancellationTokenSource = _cts
        _cts = Nothing
        If c Is Nothing Then Return
        Try : c.Cancel() : Catch : End Try
    End Sub
End Class

''' <summary>The one line the main window shows under the hosting line: what the exchange account holds of this PC's folders right now.
''' "" while the feature is off, no account is configured, publishing is off or nothing was decided yet - a line that promised
''' something it cannot prove would be worse than none. The sentences are the producer rows' own (already translated).</summary>
Friend NotInheritable Class ExchangeProducerSummary
    Private Sub New()
    End Sub

    Friend Shared Function IsReadOnly() As Boolean
        Try
            If Not ExchangeFeatures.IsEnabled() Then Return False
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            If agent Is Nothing Then Return False
            Dim publisher As ExchangePublisher = agent.Publisher
            If publisher Is Nothing Then Return False
            Dim snapshot As ExchangePublishSnapshot = publisher.Snapshot
            Return snapshot.Block = ExchangePublishBlock.ServerNoE2e
        Catch
            Return False
        End Try
    End Function

    Friend Shared Function Line() As String
        Try
            If Not ExchangeFeatures.IsEnabled() Then Return ""
            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            If agent Is Nothing Then Return ""
            Dim status As ExchangeDeviceStatus = agent.GetStatus()
            Dim publisher As ExchangePublisher = agent.Publisher
            If status Is Nothing OrElse Not status.Configured OrElse publisher Is Nothing OrElse Not publisher.Settings.PublishShares() Then Return ""
            Dim snapshot As ExchangePublishSnapshot = publisher.Snapshot
            If snapshot.Block = ExchangePublishBlock.None AndAlso snapshot.Rows.Count = 0 Then Return ""
            Return ExchangePublishModel.Build(True, status.State = ExchangeAgentState.Online, True, False, snapshot).StateLine
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IO.IOException
            Return ""
        End Try
    End Function
End Class
