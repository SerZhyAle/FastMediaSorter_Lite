Option Strict On

Imports System.IO
Imports System.Threading

' Spec 234 T-4: where the sealed clipboard gets the three things it needs for ONE server - this device's id, its key and the
' directory of the entry (who a value is sealed for, which sender key a box is held to). Nothing here seals or opens a byte:
' that is ExchangeClipboardClient's. The rules this file keeps:
'   - the key is the one beside the device token (ExchangeDeviceKeyStore over ClipboardTarget.KeyFolder); no key, no directory is
'     fetched at all - such a device seals and opens nothing and says `e2e-missing`;
'   - when the Share Manager's own agent runs THIS device's session, its directory and kept keys are used as they are: one
'     control stream per device, one owner of the trust file;
'   - otherwise (the one-shot launch from a shortcut, the manager not running, the exchange gate shut) a short control session of
'     this action's own mirrors the directory and is closed with the exchange. It uses the same trust file, so a key kept by the
'     manager is the key it holds the sender to, and a first sighting it records is the manager's after it;
'   - a directory that does not arrive (server down, wrong login) is waited for a bounded time and then simply absent: the exchange
'     that follows reports the real reason (a transport failure, a refusal), never a made-up "no recipient".

Friend NotInheritable Class ClipboardSealingProvider

    Private Sub New()
    End Sub

    ''' <summary>The longest a directory that has not arrived yet is waited for; a test shortens it.</summary>
    Friend Shared DirectoryWait As TimeSpan = TimeSpan.FromSeconds(10)

    Private Const PollMs As Integer = 25

    ''' <summary>The sealing context of one target. The caller disposes it (the key, and the session that kept the directory).</summary>
    Public Shared Async Function ResolveAsync(target As ClipboardTarget, productVersion As String, ct As CancellationToken) As Task(Of ExchangeClipboardSealing)
        Dim deviceId As String = If(target.Credentials Is Nothing, "", target.Credentials.DeviceId)
        Dim key As ExchangeDeviceKey = LoadKey(target.KeyFolder, deviceId)
        If key Is Nothing Then Return New ExchangeClipboardSealing(deviceId, Nothing, Nothing)

        Dim handedOver As Boolean = False
        Try
            Dim sealing As ExchangeClipboardSealing

            Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.Current
            If agent IsNot Nothing AndAlso String.Equals(agent.GetStatus().DeviceId, deviceId, StringComparison.Ordinal) Then
                ' The manager runs this device: its session is the only one, and it holds the keys.
                Dim view As ExchangeSealedRecipients = agent.GetSealedRecipients()
                If view IsNot Nothing Then
                    Await WaitAsync(Function() view.ServerKnown AndAlso view.Devices().Count > 0,
                                    Function() AgentGaveUp(agent), ct).ConfigureAwait(False)
                End If
                sealing = New ExchangeClipboardSealing(deviceId, key, view, ownsKey:=True)
            Else
                sealing = Await OpenOwnSessionAsync(target, deviceId, key, productVersion, ct).ConfigureAwait(False)
            End If
            handedOver = True
            Return sealing
        Finally
            If Not handedOver Then key.Dispose()
        End Try
    End Function

    Private Shared Async Function OpenOwnSessionAsync(target As ClipboardTarget, deviceId As String, key As ExchangeDeviceKey,
                                                      productVersion As String, ct As CancellationToken) As Task(Of ExchangeClipboardSealing)
        Dim folder As String = target.KeyFolder
        Dim options As New ExchangeControlOptions With {
            .Server = target.Address, .Pin = target.Pin, .Credentials = target.Credentials, .ProductVersion = productVersion,
            .Transport = target.Transport, .OwnKem = key.Kem, .SealedTrust = New ExchangeSealedTrustStore(folder),
            .OwnKey = Function() LoadKey(folder, deviceId)}
        Dim session As New ExchangeControlSession(options, Nothing)
        Dim cts As New CancellationTokenSource()
        Dim run As Task = Task.Run(Function() session.RunAsync(cts.Token))
        Try
            Await WaitAsync(Function() session.Model.HasSnapshot AndAlso session.Sealed.ServerKnown AndAlso session.Sealed.Devices().Count > 0,
                            Function() session.State <> ExchangeAgentState.Connecting AndAlso session.State <> ExchangeAgentState.Online,
                            ct).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            Release(session, cts, run)
            Throw
        End Try
        Return New ExchangeClipboardSealing(deviceId, key, session.Sealed, ownsKey:=True, release:=Sub() Release(session, cts, run))
    End Function

    ''' <summary>Says `bye`, stops the session and drops it, off the caller's thread: the exchange is already over.</summary>
    Private Shared Sub Release(session As ExchangeControlSession, cts As CancellationTokenSource, run As Task)
        Task.Run(Async Function()
                     Try
                         Await session.SayByeAsync().ConfigureAwait(False)
                         cts.Cancel()
                         Await run.ConfigureAwait(False)
                     Catch ex As Exception When TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
                     Finally
                         cts.Dispose()
                     End Try
                 End Function)
    End Sub

    ''' <summary>True once the agent will not come online by itself (sign in again, pin mismatch, update needed, a defect, stopped).</summary>
    Private Shared Function AgentGaveUp(agent As ExchangeDeviceAgent) As Boolean
        Select Case agent.GetStatus().State
            Case ExchangeAgentState.NotConfigured, ExchangeAgentState.SignInAgain, ExchangeAgentState.PinMismatch,
                 ExchangeAgentState.UpdateNeeded, ExchangeAgentState.Defect, ExchangeAgentState.Stopped
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Async Function WaitAsync(ready As Func(Of Boolean), gaveUp As Func(Of Boolean), ct As CancellationToken) As Task
        Dim deadline As Long = Environment.TickCount64 + CLng(DirectoryWait.TotalMilliseconds)
        While Not ready.Invoke() AndAlso Not gaveUp.Invoke() AndAlso Environment.TickCount64 < deadline
            Await Task.Delay(PollMs, ct).ConfigureAwait(False)
        End While
    End Function

    ''' <summary>This device's key from the folder beside its token, or Nothing (no folder, not a wire id, no file, a file that is not this id's).</summary>
    Friend Shared Function LoadKey(folder As String, deviceId As String) As ExchangeDeviceKey
        If String.IsNullOrWhiteSpace(folder) OrElse Not ExchangeIds.IsValidId(deviceId) Then Return Nothing
        Try
            Return New ExchangeDeviceKeyStore(folder, Nothing).TryLoad(deviceId)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            Return Nothing
        End Try
    End Function

End Class
