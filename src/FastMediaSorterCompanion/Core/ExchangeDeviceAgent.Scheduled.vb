#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Partial Friend NotInheritable Class ExchangeDeviceAgent

    Private ReadOnly _scheduleRing As New ScheduledRemoteRing()
    Private _scheduleDispatcher As ScheduledRemoteDispatcher

    Private Async Function ReceiveScheduleRequestAsync(session As ExchangeControlSession, message As ExchangeEnvelope) As Task
        Dim fromDeviceId As String = message.Text("fromDeviceId")
        Dim requestId As String = message.Text("requestId")
        Dim boxJson As String = message.Record("box")
        If String.IsNullOrEmpty(fromDeviceId) OrElse String.IsNullOrEmpty(requestId) OrElse String.IsNullOrEmpty(boxJson) Then
            Return
        End If

        ' Idempotency ring check (item FT): repeated request with same requestId returns cached answer box
        Dim cachedResultBox As String = Nothing
        If _scheduleRing.TryGet(fromDeviceId, requestId, cachedResultBox) Then
            Dim cachedEnv As New ExchangeEnvelope(ExchangeEnvelopeTypes.ScheduleResult)
            cachedEnv.SetText("toDeviceId", fromDeviceId).SetText("requestId", requestId).SetRecord("box", cachedResultBox)
            Await session.SendAsync(cachedEnv, CancellationToken.None).ConfigureAwait(False)
            Return
        End If

        Dim box As ExchangeSealedBox = Nothing
        If Not ExchangeSealedBox.TryParse(boxJson, box) Then
            Return
        End If

        Dim ownDeviceId As String = ""
        If session IsNot Nothing AndAlso session.Sealed IsNot Nothing Then
            ownDeviceId = session.Sealed.OwnDeviceId
        End If
        If String.IsNullOrEmpty(ownDeviceId) Then Return

        Using ownKey As ExchangeDeviceKey = KeyStore().TryLoad(ownDeviceId)
            If ownKey Is Nothing Then Return

            Dim trustedKeyForFrom As Func(Of String, String) = Function(devId As String) As String
                SyncLock _gate
                    If session IsNot Nothing AndAlso session.Sealed IsNot Nothing Then
                        Return session.Sealed.TrustedKeyOf(devId)
                    End If
                End SyncLock
                Return ""
            End Function

            Dim openResult As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, ownDeviceId, ownKey,
                                                                              ExchangeSealedContext.Schedule(requestId),
                                                                              trustedKeyForFrom)
            If Not openResult.Succeeded OrElse openResult.Payload Is Nothing Then
                _ports.Log.Invoke("Exchange agent: remote schedule box dropped (sealed-failed)")
                Return
            End If

            Dim clearRequestJson As String = Encoding.UTF8.GetString(openResult.Payload)
            openResult.Wipe()

            If _scheduleDispatcher Is Nothing Then
                _scheduleDispatcher = New ScheduledRemoteDispatcher(ScheduledHost.Engine, ownDeviceId)
            End If

            Dim clearAnswerJson As String = _scheduleDispatcher.Execute(box.FromDeviceId, requestId, clearRequestJson)
            Dim answerBytes As Byte() = Encoding.UTF8.GetBytes(clearAnswerJson)

            ' Seal answer box with context schedule-result:<requestId> addressed to box.From
            Dim recipient As New ExchangeSealedRecipient(box.FromDeviceId, box.FromKey)
            Dim sealResult As ExchangeSealResult = ExchangeSealedBoxes.Seal(answerBytes,
                                                                        ExchangeSealedContext.ScheduleResult(requestId),
                                                                        ownDeviceId,
                                                                        ownKey,
                                                                        New ExchangeSealedRecipient() {recipient})
            If Not sealResult.Succeeded OrElse sealResult.Box Is Nothing Then
                Return
            End If

            Dim resultBoxJson As String = sealResult.Box.ToJson()
            _scheduleRing.Record(fromDeviceId, requestId, resultBoxJson)

            Dim resultEnv As New ExchangeEnvelope(ExchangeEnvelopeTypes.ScheduleResult)
            resultEnv.SetText("toDeviceId", fromDeviceId).SetText("requestId", requestId).SetRecord("box", resultBoxJson)
            Await session.SendAsync(resultEnv, CancellationToken.None).ConfigureAwait(False)
        End Using
    End Function

End Class
#End If
