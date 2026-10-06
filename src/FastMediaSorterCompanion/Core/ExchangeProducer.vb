Option Strict On

Imports System.Threading.Tasks

''' <summary>The named state of one share's registration on the exchange server, as the worker reports it
''' (<c>status.exchange.shares[].state</c>, catalog proposal PROPOSAL-2026-10-06-exchange-registration).</summary>
Public Enum ExchangeShareState
    ''' <summary>Off, or a name this host does not know: a generic "not registered" (WORKER-IPC section 6 rule 2).</summary>
    NotRegistered = 0
    Connecting
    Registered
    ''' <summary>Never retried by the worker: stays until a new <c>SetExchange</c> carries a different password.</summary>
    RefusedPassword
    ''' <summary>Never retried: the server is full.</summary>
    RefusedCapacity
    RefusedPort
    ''' <summary>Never retried and never re-pinned silently (like a host-key mismatch).</summary>
    CertificateChanged
    Unreachable
    RateLimited
End Enum

Public NotInheritable Class ExchangeShareStates

    Private Sub New()
    End Sub

    ''' <summary>The closed set of names the proposal fixes. Anything else is <see cref="ExchangeShareState.NotRegistered"/>.</summary>
    Public Shared Function Parse(name As String) As ExchangeShareState
        Select Case If(name, "").Trim().ToLowerInvariant()
            Case "connecting" : Return ExchangeShareState.Connecting
            Case "registered" : Return ExchangeShareState.Registered
            Case "refused-password" : Return ExchangeShareState.RefusedPassword
            Case "refused-capacity" : Return ExchangeShareState.RefusedCapacity
            Case "refused-port" : Return ExchangeShareState.RefusedPort
            Case "certificate-changed" : Return ExchangeShareState.CertificateChanged
            Case "unreachable" : Return ExchangeShareState.Unreachable
            Case "rate-limited" : Return ExchangeShareState.RateLimited
            Case Else : Return ExchangeShareState.NotRegistered
        End Select
    End Function

    ''' <summary>The states the worker does not retry: the user has to act (acceptance 7 - a named state, not a
    ''' retry storm).</summary>
    Public Shared Function NeedsUser(state As ExchangeShareState) As Boolean
        Return state = ExchangeShareState.RefusedPassword OrElse state = ExchangeShareState.CertificateChanged OrElse
               state = ExchangeShareState.RefusedCapacity
    End Function
End Class

''' <summary>The verdict of <see cref="ExchangeProducerRules.Validate"/>: what is wrong with what the user typed,
''' before anything is saved or sent.</summary>
Public Enum ExchangeProducerProblem
    None
    ''' <summary>External mode with no usable <c>host:port</c> to register on.</summary>
    RegistrationEndpoint
    ClientEndpointMalformed
    ClientEndpointLoopback
    ''' <summary>No password typed and the worker has none for this server yet.</summary>
    PasswordMissing
    ''' <summary>A typed password below the contract floor (section 5.2).</summary>
    PasswordBelowFloor
End Enum

''' <summary>Whether the running worker can take a <c>SetExchange</c> at all.</summary>
Public Enum ExchangeWorkerReadiness
    ''' <summary>Nobody answered (or the answer is not a status): nothing is sent.</summary>
    NoWorker
    ''' <summary>The worker answered and does not report <c>exchangeSupported</c>: it is older than the feature, and
    ''' nothing - above all the password - is sent to it (proposal rule 5).</summary>
    Stale
    Ready
End Enum

''' <summary>
''' Pure rules of the producer half (ticket 148): what is validated, what request is built, what the export
''' takes from the worker's status. No registry, no pipe, no UI.
''' </summary>
Public NotInheritable Class ExchangeProducerRules

    Private Sub New()
    End Sub

    Public Shared Function Readiness(status As WorkerStatus) As ExchangeWorkerReadiness
        If status Is Nothing Then Return ExchangeWorkerReadiness.NoWorker
        Return If(status.ExchangeSupported, ExchangeWorkerReadiness.Ready, ExchangeWorkerReadiness.Stale)
    End Function

    ''' <summary>The endpoint the WORKER dials, or Nothing when there is none (mode off, an external address that
    ''' does not parse). This PC's own server is loopback on its own port - <paramref name="localPort"/> is read
    ''' from the local server by the caller, never typed.</summary>
    Public Shared Function RegistrationEndpoint(settings As ExchangeProducerSettings, localPort As Integer) As ExchangeEndpoint?
        If settings Is Nothing Then Return Nothing
        Select Case settings.Mode
            Case ExchangeMode.ThisPc
                Return ExchangeEndpointRules.ThisPcRegistrationEndpoint(localPort)
            Case ExchangeMode.External
                Dim ep As ExchangeEndpoint = Nothing
                If ExchangeEndpointRules.TryParse(settings.RegistrationEndpoint, ep) Then Return ep
                Return Nothing
            Case Else
                Return Nothing
        End Select
    End Function

    ''' <summary>The first thing wrong with the form, in the order the user meets the fields. Mode off is always
    ''' fine. <paramref name="passwordRequired"/> is true when the worker has no password for this server yet;
    ''' after that an empty field means "keep the current one".</summary>
    Public Shared Function Validate(settings As ExchangeProducerSettings, password As String, passwordRequired As Boolean) As ExchangeProducerProblem
        If settings Is Nothing OrElse settings.Mode = ExchangeMode.Off Then Return ExchangeProducerProblem.None
        If settings.Mode = ExchangeMode.External AndAlso ExchangeEndpointRules.CheckRegistrationEndpoint(settings.RegistrationEndpoint) <> ExchangeEndpointVerdict.Valid Then
            Return ExchangeProducerProblem.RegistrationEndpoint
        End If
        Select Case ExchangeEndpointRules.CheckClientEndpoint(settings.ClientEndpoint)
            Case ExchangeEndpointVerdict.Malformed : Return ExchangeProducerProblem.ClientEndpointMalformed
            Case ExchangeEndpointVerdict.Loopback : Return ExchangeProducerProblem.ClientEndpointLoopback
        End Select
        If String.IsNullOrEmpty(password) Then
            Return If(passwordRequired, ExchangeProducerProblem.PasswordMissing, ExchangeProducerProblem.None)
        End If
        If Not ExchangeEndpointRules.CheckServerPassword(password) = ExchangePasswordPolicy.Verdict.Accepted Then Return ExchangeProducerProblem.PasswordBelowFloor
        Return ExchangeProducerProblem.None
    End Function

    ''' <summary>
    ''' The one <c>SetExchange</c> request for the typed settings. Mode off sends <c>enabled=false</c> and nothing
    ''' else (the worker unregisters and KEEPS the share ids, Q148-2). Otherwise it carries the endpoint the worker
    ''' dials, and the password ONLY when one was typed (<paramref name="password"/> empty = absent = unchanged),
    ''' the pin only when one is known, and the share names to re-issue only when there are some.
    ''' </summary>
    Public Shared Function BuildSetExchange(settings As ExchangeProducerSettings, localPort As Integer, password As String,
                                            pinFingerprint As String, reissue As IEnumerable(Of String)) As WorkerRequest
        Dim req As New WorkerRequest With {.type = "SetExchange"}
        Dim ep As ExchangeEndpoint? = RegistrationEndpoint(settings, localPort)
        If settings Is Nothing OrElse settings.Mode = ExchangeMode.Off OrElse Not ep.HasValue Then
            req.enabled = False
            Return req
        End If
        req.enabled = True
        req.registerHost = ep.Value.Host
        req.registerPort = ep.Value.Port
        If Not String.IsNullOrEmpty(password) Then req.password = password
        If Not String.IsNullOrWhiteSpace(pinFingerprint) Then req.certFingerprint = pinFingerprint.Trim()
        If reissue IsNot Nothing Then
            Dim names As New List(Of String)()
            For Each n As String In reissue
                If Not String.IsNullOrEmpty(n) AndAlso Not names.Contains(n) Then names.Add(n)
            Next
            If names.Count > 0 Then req.reissue = names
        End If
        Return req
    End Function

    ''' <summary>
    ''' What the "address for clients" field is prefilled with (never an external lookup - Q142-3): for an external
    ''' server, the address the worker registers on (the client-facing name equals it unless the deployment gave
    ''' another); for this PC's server, the public address the worker already knows, else its LAN address, on the
    ''' local server's port. "" when nothing usable is known. Always a value <see cref="ExchangeEndpointRules.CheckClientEndpoint"/>
    ''' accepts - loopback is never suggested.
    ''' </summary>
    Public Shared Function SuggestClientEndpoint(mode As ExchangeMode, registrationEndpoint As String, reach As WorkerReachability,
                                                 lanFallback As String, localPort As Integer) As String
        Dim text As String = ""
        Select Case mode
            Case ExchangeMode.External
                Dim ep As ExchangeEndpoint = Nothing
                If ExchangeEndpointRules.TryParse(registrationEndpoint, ep) Then text = ep.ToString()
            Case ExchangeMode.ThisPc
                Dim host As String = ""
                If reach IsNot Nothing Then
                    If Not reach.IsCgnat AndAlso Not String.IsNullOrEmpty(reach.ExternalHost) Then host = reach.ExternalHost
                    If host.Length = 0 AndAlso Not String.IsNullOrEmpty(reach.LanAddress) Then host = reach.LanAddress
                End If
                If host.Length = 0 Then host = If(lanFallback, "")
                If host.Length > 0 Then text = New ExchangeEndpoint(host, ExchangeEndpointRules.ThisPcRegistrationEndpoint(localPort).Port).ToString()
        End Select
        Return If(ExchangeEndpointRules.CheckClientEndpoint(text) = ExchangeEndpointVerdict.Valid, text, "")
    End Function

    ''' <summary>The worker's entry for a share by name, or Nothing.</summary>
    Public Shared Function ShareEntry(status As WorkerStatus, name As String) As WorkerExchangeShare
        If status?.Exchange?.Shares Is Nothing OrElse String.IsNullOrEmpty(name) Then Return Nothing
        For Each s As WorkerExchangeShare In status.Exchange.Shares
            If s IsNot Nothing AndAlso String.Equals(s.Name, name, StringComparison.Ordinal) Then Return s
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' What the export gets from the registration: the first share of the exported set (roots order) that is
    ''' registered and has a well-formed id. Nothing when the mode is off, no publishable client endpoint exists,
    ''' the worker reports nothing or no share is registered - so with the mode off (or a stale worker) the export
    ''' is exactly what it was (contract section 8.5).
    '''
    ''' One <c>.fmscfg</c> carries one tunnel path while the worker keeps one id per share, and every
    ''' registration splices to the same SFTP listener - so the id of the first registered share is the path the
    ''' file publishes. Recorded as a finding in ticket 148 section 9.
    ''' </summary>
    Public Shared Function ExportFor(status As WorkerStatus, settings As ExchangeProducerSettings) As ExchangeExport
        If status?.Exchange Is Nothing OrElse Not status.Exchange.Enabled OrElse settings Is Nothing Then Return Nothing
        Dim client As ExchangeEndpoint? = settings.PublishableClientEndpoint()
        If Not client.HasValue OrElse status.Roots Is Nothing Then Return Nothing
        For Each root As ShareFolder In status.Roots
            Dim entry As WorkerExchangeShare = ShareEntry(status, root?.name)
            If entry Is Nothing Then Continue For
            If ExchangeShareStates.Parse(entry.State) <> ExchangeShareState.Registered Then Continue For
            If Not ExchangeShareIds.IsWellFormed(entry.ShareId) Then Continue For
            Return New ExchangeExport With {.ClientHost = client.Value.Host, .ClientPort = client.Value.Port,
                                            .ShareId = entry.ShareId, .RelayPort = Math.Max(0, entry.RelayPort)}
        Next
        Return Nothing
    End Function

    ''' <summary>Shares of the current list that already hold an exchange id - the ones "new link" can rotate.</summary>
    Public Shared Function ReissuableShares(status As WorkerStatus) As List(Of String)
        Dim names As New List(Of String)()
        If status?.Exchange?.Shares Is Nothing Then Return names
        For Each s As WorkerExchangeShare In status.Exchange.Shares
            If s IsNot Nothing AndAlso Not String.IsNullOrEmpty(s.Name) AndAlso ExchangeShareIds.IsWellFormed(s.ShareId) Then names.Add(s.Name)
        Next
        Return names
    End Function
End Class

''' <summary>The outcome of one apply, with the sentence the user reads.</summary>
Public Enum ExchangeApplyOutcome
    Applied
    ''' <summary>Nobody answered the control pipe: nothing was sent.</summary>
    NoWorker
    ''' <summary>The worker is older than the feature: nothing was sent, the password least of all.</summary>
    StaleWorker
    ''' <summary>A worker answered, and it was a version refusal or a pipe problem.</summary>
    NotUnderstood
    ''' <summary>The worker answered <c>ok=false</c>.</summary>
    Refused
End Enum

Public NotInheritable Class ExchangeApplyResult
    Public Property Outcome As ExchangeApplyOutcome
    Public Property Text As String = ""
    ''' <summary>The status read after a successful apply (Nothing otherwise).</summary>
    Public Property Status As WorkerStatus
End Class

''' <summary>
''' The host side of <c>SetExchange</c> (ticket 148 T-2/T-3/T-6): sends the typed settings to the worker through
''' the one pipe, enforcing the secret-ordering rule of the proposal. It stores nothing: the password lives in
''' the request for the length of the call and is cleared from the object afterwards, the pin and the share ids
''' stay the worker's.
''' </summary>
Public Module ExchangeProducerController

    ''' <summary>Applies the settings. <paramref name="statusBefore"/> is the status the caller just read (it
    ''' decides readiness - the password is NEVER sent without <c>exchangeSupported</c>). Never throws.</summary>
    Public Async Function ApplyAsync(settings As ExchangeProducerSettings, localPort As Integer, password As String,
                                     pinFingerprint As String, reissue As IEnumerable(Of String),
                                     statusBefore As WorkerStatus) As Task(Of ExchangeApplyResult)
        Return Await ApplyAsync(settings, localPort, password, pinFingerprint, reissue, statusBefore, WorkerIpc.PipeName)
    End Function

    Friend Async Function ApplyAsync(settings As ExchangeProducerSettings, localPort As Integer, password As String,
                                     pinFingerprint As String, reissue As IEnumerable(Of String),
                                     statusBefore As WorkerStatus, pipeName As String) As Task(Of ExchangeApplyResult)
        Dim result As New ExchangeApplyResult()
        ' Off on a worker that was never configured for an exchange: there is nothing to unregister, and a
        ' stale worker must not be told about a feature the user just declined.
        If settings IsNot Nothing AndAlso settings.Mode = ExchangeMode.Off AndAlso statusBefore IsNot Nothing AndAlso statusBefore.Exchange Is Nothing Then
            result.Outcome = ExchangeApplyOutcome.Applied
            result.Status = statusBefore
            Return result
        End If
        Select Case ExchangeProducerRules.Readiness(statusBefore)
            Case ExchangeWorkerReadiness.NoWorker
                result.Outcome = ExchangeApplyOutcome.NoWorker
                result.Text = ExchangeProducerText.WorkerSilent()
                Return result
            Case ExchangeWorkerReadiness.Stale
                result.Outcome = ExchangeApplyOutcome.StaleWorker
                result.Text = ExchangeProducerText.WorkerStale()
                Return result
        End Select

        Dim req As WorkerRequest = ExchangeProducerRules.BuildSetExchange(settings, localPort, password, pinFingerprint, reissue)
        Dim resp As WorkerResponse
        Try
            resp = Await Task.Run(Function() WorkerProcess.Exchange(req, 5000, pipeName))
        Finally
            req.password = Nothing   ' the secret does not outlive the call on this side
        End Try

        If resp Is Nothing Then
            result.Outcome = ExchangeApplyOutcome.NoWorker
            result.Text = ExchangeProducerText.WorkerSilent()
        ElseIf resp.IsRefused OrElse resp.IsProblem Then
            result.Outcome = ExchangeApplyOutcome.NotUnderstood
            result.Text = If(resp.IsRefused, ShareController.RefusalMessage(resp.Refusal), ShareController.ProblemMessage(resp.Problem))
        ElseIf Not resp.Ok Then
            result.Outcome = ExchangeApplyOutcome.Refused
            result.Text = ShareController.RefusedText(resp)
        Else
            result.Outcome = ExchangeApplyOutcome.Applied
            result.Status = resp.Status
            If result.Status Is Nothing Then result.Status = Await ShareController.GetStatusAsync()
        End If
        Return result
    End Function
End Module
