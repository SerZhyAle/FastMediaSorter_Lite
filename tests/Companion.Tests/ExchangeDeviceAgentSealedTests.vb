Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 234 slice C4, DEVICE-EXCHANGE section 27 item DF on the Windows device agent, from the angles the key tests do not take: the key is
''' kept BEFORE the first byte goes out and is the one in the frame (`enroll` and `register`), one id has one key for as long as it lives
''' (never regenerated across failed and repeated attempts) while another id has another, no attempt that ends without an enrollment leaves a
''' key made for it behind (a refusal, a pin that does not match, a token that cannot be stored, an unexpected exception, a cancelled call),
''' and the private scalar is in no log line, no result sentence and no exception text of these flows.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceAgentSealedTests

    ''' <summary>A key store that sees every write: how often Save was asked, which ids got a NEW key and which were taken back, and the
    ''' private scalar of every key it was offered (copied at the moment of the call, because a refused key is disposed by the caller).</summary>
    Friend NotInheritable Class SpyKeyStore
        Implements IExchangeDeviceKeyStore

        Private ReadOnly _inner As New ExchangeMemoryDeviceKeyStore()
        Private ReadOnly _gate As New Object()
        Private ReadOnly _live As New List(Of String)

        Public ReadOnly Created As New List(Of String)
        Public ReadOnly Deleted As New List(Of String)
        Public ReadOnly Scalars As New List(Of Byte())
        Public SaveCalls As Integer
        Public RefuseSave As Boolean

        Public Function TryLoad(deviceId As String) As ExchangeDeviceKey Implements IExchangeDeviceKeyStore.TryLoad
            SyncLock _gate
                Return _inner.TryLoad(deviceId)
            End SyncLock
        End Function

        Public Function Save(deviceId As String, key As ExchangeDeviceKey) As Boolean Implements IExchangeDeviceKeyStore.Save
            SyncLock _gate
                SaveCalls += 1
                Dim scalar As Byte() = key.ExportPrivateScalar()
                If scalar IsNot Nothing Then Scalars.Add(CType(scalar.Clone(), Byte()))
                If RefuseSave Then Return False
                Dim isNew As Boolean = Not _inner.Exists(deviceId)
                Dim ok As Boolean = _inner.Save(deviceId, key)
                If ok AndAlso isNew Then
                    Created.Add(deviceId)
                    _live.Add(deviceId)
                End If
                Return ok
            End SyncLock
        End Function

        Public Function Exists(deviceId As String) As Boolean Implements IExchangeDeviceKeyStore.Exists
            SyncLock _gate
                Return _inner.Exists(deviceId)
            End SyncLock
        End Function

        Public Sub Delete(deviceId As String) Implements IExchangeDeviceKeyStore.Delete
            SyncLock _gate
                If _inner.Exists(deviceId) Then Deleted.Add(deviceId)
                _live.Remove(deviceId)
                _inner.Delete(deviceId)
            End SyncLock
        End Sub

        Public Function KemOf(deviceId As String) As String
            Using key As ExchangeDeviceKey = TryLoad(deviceId)
                Return If(key Is Nothing, "", key.Kem)
            End Using
        End Function

        ''' <summary>Id to public half of every key that is kept right now.</summary>
        Public Function Snapshot() As Dictionary(Of String, String)
            Dim all As New Dictionary(Of String, String)(StringComparer.Ordinal)
            SyncLock _gate
                For Each id As String In _live
                    all(id) = KemOf(id)
                Next
            End SyncLock
            Return all
        End Function
    End Class

    ''' <summary>The rig's transport behind a window: what the key store held at the moment of every connect, and optionally an exception
    ''' instead of a connection.</summary>
    Friend NotInheritable Class SpyTransport
        Implements IExchangeTransport

        Private ReadOnly _inner As IExchangeTransport
        Private ReadOnly _keys As SpyKeyStore

        Public ReadOnly KeysAtConnect As New List(Of Dictionary(Of String, String))
        Public ThrowOnConnect As Exception

        Public Sub New(inner As IExchangeTransport, keys As SpyKeyStore)
            _inner = inner
            _keys = keys
        End Sub

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            Dim held As Dictionary(Of String, String) = _keys.Snapshot()
            SyncLock KeysAtConnect
                KeysAtConnect.Add(held)
            End SyncLock
            If ThrowOnConnect IsNot Nothing Then Return Task.FromException(Of ExchangeTlsConnection)(ThrowOnConnect)
            Return _inner.ConnectAsync(host, port, pin, ct)
        End Function
    End Class

    Private Shared Function NewRig(keys As SpyKeyStore) As ExchangeDeviceAgentTests.Rig
        Dim rig As New ExchangeDeviceAgentTests.Rig()
        rig.Ports.DeviceKeys = keys
        rig.Ports.SealedTrust = New ExchangeMemorySealedTrustStore()
        Return rig
    End Function

    Private Shared Function RegisterRequest(acc As ExchangeStubAccount) As ExchangeRegisterRequest
        Return New ExchangeRegisterRequest With {
            .Server = acc.Address, .Login = "carol", .Password = ExchangeStubAccount.Password, .InvitationCode = ExchangeStubAccount.Invitation,
            .PlatformValid = False, .AcceptedFingerprint = acc.Fingerprint}
    End Function

    ''' <summary>The `kem` of one raw `e2eKey` member; the member must be there and carry `v` 1.</summary>
    Private Shared Function KemOfFrame(raw As String) As String
        Assert.False(String.IsNullOrEmpty(raw), "the frame carried no e2eKey")
        Using doc As JsonDocument = JsonDocument.Parse(raw)
            Dim root As JsonElement = doc.RootElement
            Assert.Equal(1, root.GetProperty("v").GetInt32())
            Return root.GetProperty("kem").GetString()
        End Using
    End Function

    Private Shared Function Leaks(text As String, scalar As Byte()) As Boolean
        Return text.Contains(Encoding.Latin1.GetString(scalar), StringComparison.Ordinal) OrElse
               text.Contains(Convert.ToHexString(scalar), StringComparison.OrdinalIgnoreCase) OrElse
               text.Contains(Convert.ToBase64String(scalar), StringComparison.Ordinal) OrElse
               text.Contains(ExchangeBase64Url.Encode(scalar), StringComparison.Ordinal)
    End Function

    ' ---------------------------------------------------------------- DF: the key is kept first and is the one in the frame

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function The_key_is_already_kept_when_the_connection_opens_and_is_the_one_in_the_frame(useRegister As Boolean) As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Dim spy As New SpyTransport(rig.Transport, keys)
                rig.Ports.Transport = spy
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim result As ExchangeEnrollResult
                    If useRegister Then
                        result = Await agent.RegisterAsync(RegisterRequest(acc), CancellationToken.None)
                    Else
                        result = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    End If
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Dim id As String = agent.GetStatus().DeviceId

                    ' at the one connect that carried the frame the store already held exactly this id's key..
                    Assert.Equal(1, spy.KeysAtConnect.Count)
                    Assert.Equal({id}, spy.KeysAtConnect(0).Keys.ToArray())
                    ' ..and the frame carried the public half of that very key, once
                    Assert.Equal(id, acc.EnrollDeviceIdsSeen().Single())
                    Dim kem As String = KemOfFrame(acc.EnrollE2eKeysSeen().Single())
                    Assert.Equal(spy.KeysAtConnect(0)(id), kem)
                    Assert.Equal(keys.KemOf(id), kem)
                    Assert.Contains(ExchangeSealedNames.CapabilityE2e, acc.EnrollCapabilitiesSeen().Single().Split(","c))
                    Assert.Equal(1, keys.SaveCalls)
                    Assert.Empty(keys.Deleted)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DF: a key lives exactly as long as its id

    <Fact>
    Public Async Function Another_id_gets_another_key_and_the_same_id_keeps_its_own_across_a_new_attempt() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rigA As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using rigB As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                    Using agentA As ExchangeDeviceAgent = rigA.NewAgent()
                        Using agentB As ExchangeDeviceAgent = rigB.NewAgent()
                            Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agentA.EnrollAsync(rigA.Request(acc), CancellationToken.None)).Outcome)
                            Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agentB.EnrollAsync(rigB.Request(acc), CancellationToken.None)).Outcome)
                            Dim idA As String = agentA.GetStatus().DeviceId
                            Dim idB As String = agentB.GetStatus().DeviceId
                            Dim kemA As String = keys.KemOf(idA)
                            Dim kemB As String = keys.KemOf(idB)

                            Assert.NotEqual(idA, idB)
                            Assert.NotEqual("", kemA)
                            Assert.NotEqual(kemA, kemB)                                    ' another id, another key
                            Assert.Equal({idA, idB}, keys.Created.ToArray())
                            Assert.Equal(2, keys.SaveCalls)
                            Assert.False(keys.Scalars(0).SequenceEqual(keys.Scalars(1)))   ' not the same pair under two ids

                            Dim ids As List(Of String) = acc.EnrollDeviceIdsSeen()
                            Dim sent As List(Of String) = acc.EnrollE2eKeysSeen()
                            Assert.Equal(2, sent.Count)
                            Assert.Equal(kemA, KemOfFrame(sent(ids.IndexOf(idA))))
                            Assert.Equal(kemB, KemOfFrame(sent(ids.IndexOf(idB))))

                            ' the same id enrolls again (a token rotation): the same key goes, none is made, none is taken back
                            Await agentA.DisconnectAsync()
                            Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agentA.EnrollAsync(rigA.Request(acc), CancellationToken.None)).Outcome)
                            Assert.Equal(idA, agentA.GetStatus().DeviceId)
                            Assert.Equal(2, keys.SaveCalls)                                 ' no write: the stored key was read, never regenerated
                            Assert.Equal(2, keys.Created.Count)
                            Assert.Empty(keys.Deleted)
                            Assert.Equal(kemA, keys.KemOf(idA))
                            Assert.Equal(kemB, keys.KemOf(idB))
                            Dim again As List(Of String) = acc.EnrollE2eKeysSeen()
                            Assert.Equal(3, again.Count)
                            Assert.Equal(kemA, KemOfFrame(again(2)))
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Failed_attempts_of_a_known_id_neither_regenerate_nor_drop_its_key() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim kem As String = keys.KemOf(id)
                    Await agent.DisconnectAsync()

                    ' a pin that is not the server's
                    Dim wrong As ExchangeEnrollRequest = rig.Request(acc)
                    wrong.AcceptedFingerprint = ExchangeTrust.FingerprintOf(New Byte() {7})
                    Assert.Equal(ExchangeEnrollOutcome.PinMismatch, (Await agent.EnrollAsync(wrong, CancellationToken.None)).Outcome)
                    ' a refused enrollment, a refused registration
                    acc.RefuseNextEnroll("bad-credentials")
                    Assert.Equal(ExchangeEnrollOutcome.Refused, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    acc.RefuseNextRegister("capacity")
                    Assert.Equal(ExchangeEnrollOutcome.Refused, (Await agent.RegisterAsync(RegisterRequest(acc), CancellationToken.None)).Outcome)

                    Assert.True(keys.Exists(id))
                    Assert.Equal(kem, keys.KemOf(id))
                    Assert.Empty(keys.Deleted)
                    Assert.Equal(1, keys.SaveCalls)                                         ' one key in all, written once

                    ' the attempt that gets through sends the same key as every attempt before it
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.Equal(id, agent.GetStatus().DeviceId)
                    Assert.Equal(1, keys.SaveCalls)
                    Dim sent As List(Of String) = acc.EnrollE2eKeysSeen()
                    Assert.Equal(4, sent.Count)                                             ' the first enrollment, two refusals, the last one
                    Assert.All(sent, Sub(raw) Assert.Equal(kem, KemOfFrame(raw)))
                    Assert.All(acc.EnrollDeviceIdsSeen(), Sub(seen) Assert.Equal(id, seen))
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DF: a failure leaves no key behind

    <Fact>
    Public Async Function A_token_that_cannot_be_stored_takes_back_the_key_the_enrollment_made_but_not_an_older_one() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    ' the server accepts, then the token cannot be stored under the cipher: nothing is configured, so the key goes with the attempt
                    rig.Cipher.Fail = True
                    Dim failed As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, failed.Outcome)
                    Assert.Equal(1, acc.CountOf("enroll"))
                    Dim madeFor As String = acc.EnrollDeviceIdsSeen().Single()
                    Assert.Equal({madeFor}, keys.Created.ToArray())
                    Assert.Equal({madeFor}, keys.Deleted.ToArray())
                    Assert.False(keys.Exists(madeFor))
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(0, rig.Port.Writes)
                    Assert.Contains(rig.Log, Function(l) l.Contains("token could not be stored", StringComparison.Ordinal))

                    ' a good enrollment, then the same failure for the same id: the key that was there before is the device's own and stays
                    rig.Cipher.Fail = False
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim kem As String = keys.KemOf(id)
                    Await agent.DisconnectAsync()
                    rig.Cipher.Fail = True
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.True(keys.Exists(id))
                    Assert.Equal(kem, keys.KemOf(id))
                    Assert.Equal({madeFor}, keys.Deleted.ToArray())                        ' still only the first attempt's key
                    rig.Cipher.Fail = False
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function An_unexpected_exception_and_a_cancelled_call_leave_no_key_and_no_secret_behind() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Dim spy As New SpyTransport(rig.Transport, keys)
                rig.Ports.Transport = spy
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    ' the transport throws something nobody expected: the exception reaches the caller, the secrets are wiped, the key goes
                    spy.ThrowOnConnect = New InvalidOperationException("transport blew up")
                    Dim enroll As ExchangeEnrollRequest = rig.Request(acc)
                    Dim caught As Exception = Nothing
                    Try
                        Await agent.EnrollAsync(enroll, CancellationToken.None)
                    Catch ex As InvalidOperationException
                        caught = ex
                    End Try
                    Assert.NotNull(caught)
                    Assert.Equal("", enroll.Password)
                    Assert.Equal(1, keys.Created.Count)
                    Assert.Equal(keys.Created.ToArray(), keys.Deleted.ToArray())
                    Assert.False(keys.Exists(keys.Created(0)))

                    Dim register As ExchangeRegisterRequest = RegisterRequest(acc)
                    caught = Nothing
                    Try
                        Await agent.RegisterAsync(register, CancellationToken.None)
                    Catch ex As InvalidOperationException
                        caught = ex
                    End Try
                    Assert.NotNull(caught)
                    Assert.Equal("", register.Password)
                    Assert.Equal("", register.InvitationCode)
                    Assert.Equal(2, keys.Created.Count)
                    Assert.Equal(keys.Created.ToArray(), keys.Deleted.ToArray())

                    ' a call that is cancelled before it starts: either the outcome or the cancellation itself, and no key either way
                    spy.ThrowOnConnect = Nothing
                    Using cts As New CancellationTokenSource()
                        cts.Cancel()
                        Try
                            Dim cancelled As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), cts.Token)
                            Assert.Equal(ExchangeEnrollOutcome.Cancelled, cancelled.Outcome)
                        Catch ex As OperationCanceledException
                            ' surfaced as the cancellation itself
                        End Try
                    End Using
                    Assert.Equal(3, keys.Created.Count)
                    Assert.Equal(keys.Created.ToArray(), keys.Deleted.ToArray())
                    Assert.Empty(keys.Snapshot())
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(0, rig.Port.Writes)
                    Assert.Equal(0, acc.CountOf("enroll"))
                End Using
            End Using
        End Using
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function A_session_that_cannot_start_after_the_account_is_saved_keeps_the_key(useRegister As Boolean) As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                ' the server accepts and the account is written; then the session cannot be set up and the exception reaches the caller
                rig.Ports.ConfigureSession = Sub(o) Throw New InvalidOperationException("session setup blew up")
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim caught As Exception = Nothing
                    Try
                        If useRegister Then
                            Await agent.RegisterAsync(RegisterRequest(acc), CancellationToken.None)
                        Else
                            Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                        End If
                    Catch ex As InvalidOperationException
                        caught = ex
                    End Try
                    Assert.NotNull(caught)

                    ' the device is configured, so the key made for it stays: without it the device could never open sealed content
                    Assert.True(rig.Ports.Accounts.IsConfigured())
                    Dim id As String = rig.Ports.Accounts.Load().DeviceId
                    Assert.Equal(acc.EnrollDeviceIdsSeen().Single(), id)
                    Assert.Equal({id}, keys.Created.ToArray())
                    Assert.Empty(keys.Deleted)
                    Assert.True(keys.Exists(id))
                    Assert.Equal(KemOfFrame(acc.EnrollE2eKeysSeen().Single()), keys.KemOf(id))
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- DF: the private half is in no log line, sentence or exception text

    <Fact>
    Public Async Function The_private_scalar_is_in_no_log_line_no_result_sentence_and_no_exception_text_of_a_failing_flow() As Task
        Using acc As New ExchangeStubAccount()
            Dim keys As New SpyKeyStore()
            Using rig As ExchangeDeviceAgentTests.Rig = NewRig(keys)
                Dim spy As New SpyTransport(rig.Transport, keys)
                rig.Ports.Transport = spy
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim texts As New List(Of String)

                    ' a key that cannot be kept: nothing is sent, and the scalar was offered to the store and nowhere else
                    keys.RefuseSave = True
                    Dim notKept As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, notKept.Outcome)
                    texts.Add(ExchangeDeviceText.EnrollFailure(notKept))
                    keys.RefuseSave = False

                    ' a refusal, an id collision, a token that cannot be stored
                    acc.RefuseNextEnroll("id-collision")
                    Dim collided As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeRefusal.IdCollision, collided.Refusal)
                    texts.Add(ExchangeDeviceText.EnrollFailure(collided))
                    rig.Cipher.Fail = True
                    texts.Add(ExchangeDeviceText.EnrollFailure(Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)))
                    rig.Cipher.Fail = False

                    ' an exception from the transport, whole, with its stack
                    spy.ThrowOnConnect = New InvalidOperationException("transport blew up")
                    Try
                        Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Catch ex As InvalidOperationException
                        texts.Add(ex.ToString())
                    End Try
                    spy.ThrowOnConnect = Nothing

                    ' and the one that works, so the success path's log and settings are in the sample too
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    texts.Add(rig.Everything())
                    texts.Add(String.Join(vbLf, rig.Log))

                    Assert.True(keys.Scalars.Count >= 5)
                    Dim all As String = String.Join(vbLf, texts)
                    Assert.True(Leaks(ExchangeBase64Url.Encode(keys.Scalars(0)), keys.Scalars(0)))   ' the detector does detect
                    For Each scalar As Byte() In keys.Scalars
                        Assert.False(Leaks(all, scalar))
                    Next
                    ' the public half did reach the server on the attempt that got through, so the flows above really ran with keys
                    Assert.NotEqual("", KemOfFrame(acc.EnrollE2eKeysSeen().Last()))
                End Using
            End Using
        End Using
    End Function

End Class
