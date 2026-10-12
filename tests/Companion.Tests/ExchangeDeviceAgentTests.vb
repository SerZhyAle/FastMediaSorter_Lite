Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 180 T-2 and acceptance 1-4: the device agent against ExchangeStubAccount - enrollment by password and by pairing code, the token
''' under a cipher and never the password anywhere, the control stream that stays open, a restart with no prompt, a revoked device that stops and
''' is not retried, a changed certificate that is a named stop, leaving the account, and the dormant state (no account: nothing happens).
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceAgentTests

    Friend NotInheritable Class MemoryPort
        Implements IExchangeSettingsPort

        Public ReadOnly Values As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public Writes As Integer

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            SyncLock Values
                Dim v As String = Nothing
                Return If(Values.TryGetValue(name, v), v, "")
            End SyncLock
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            SyncLock Values
                Writes += 1
                Values(name) = value
            End SyncLock
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            SyncLock Values
                Writes += 1
                Values.Remove(name)
            End SyncLock
        End Sub
    End Class

    Friend NotInheritable Class XorCipher
        Implements IExchangeSecretCipher

        Public Fail As Boolean

        Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
            If Fail Then Throw New CryptographicException("no key")
            Return plain.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function

        Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
            If Fail Then Throw New CryptographicException("no key")
            Return blob.Select(Function(b) CByte(b Xor &H5A)).ToArray()
        End Function
    End Class

    ''' <summary>A transport that counts the connection attempts and then delegates to the real TLS one.</summary>
    Friend NotInheritable Class CountingTransport
        Implements IExchangeTransport

        Public Attempts As Integer

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            Interlocked.Increment(Attempts)
            Return ExchangeTlsTransport.Instance.ConnectAsync(host, port, pin, ct)
        End Function
    End Class

    ''' <summary>Everything one agent test needs: fake settings, a token folder, a counting transport, a captured log.</summary>
    Friend NotInheritable Class Rig
        Implements IDisposable

        Public ReadOnly Port As New MemoryPort()
        Public ReadOnly Cipher As New XorCipher()
        Public ReadOnly Folder As String = Path.Combine(Path.GetTempPath(), "fms-agent-test-" & Guid.NewGuid().ToString("N"))
        Public ReadOnly Transport As New CountingTransport()
        Public ReadOnly Log As New ConcurrentQueue(Of String)
        Public ReadOnly Ports As ExchangeAgentPorts
        Public Channels As Integer
        Public ReadOnly Waits As New ConcurrentQueue(Of TimeSpan)

        Public Sub New()
            Ports = New ExchangeAgentPorts With {
                .Accounts = New ExchangeAccountStore(Port),
                .Credentials = New ExchangeCredentialStore(Folder, Cipher),
                .Transport = Transport,
                .ProductVersion = "26.10.7.1",
                .MachineName = Function() "TESTPC",
                .Log = Sub(line) Log.Enqueue(line),
                .StartLocalChannel = Function(h) CountChannel(),
                .ConfigureSession = AddressOf Configure}
        End Sub

        Private Function CountChannel() As IDisposable
            Interlocked.Increment(Channels)
            Return Nothing
        End Function

        Private Sub Configure(o As ExchangeControlOptions)
            o.KeepaliveUnitMs = 60
            o.Backoff = Function(attempt) TimeSpan.FromSeconds(ExchangeBackoff.BaseSeconds(attempt))
            o.Delay = Function(t, ct)
                          If t >= TimeSpan.FromSeconds(1) Then
                              Waits.Enqueue(t)
                              Return Task.Delay(5, ct)
                          End If
                          Return Task.Delay(t, ct)
                      End Function
        End Sub

        Public Function NewAgent() As ExchangeDeviceAgent
            Return New ExchangeDeviceAgent(Ports)
        End Function

        Public Function Request(acc As ExchangeStubAccount, Optional password As String = ExchangeStubAccount.Password, Optional code As String = "") As ExchangeEnrollRequest
            Return New ExchangeEnrollRequest With {
                .Server = acc.Address, .Login = ExchangeStubAccount.Login, .Password = password, .PairingCode = code,
                .PlatformValid = False, .AcceptedFingerprint = acc.Fingerprint}
        End Function

        ''' <summary>Everything this rig stored or logged, as one string, for "the secret is nowhere" assertions.</summary>
        Public Function Everything() As String
            Dim sb As New StringBuilder()
            SyncLock Port.Values
                For Each kv As KeyValuePair(Of String, String) In Port.Values
                    sb.Append(kv.Key).Append("="c).Append(kv.Value).Append(vbLf)
                Next
            End SyncLock
            For Each line As String In Log
                sb.Append(line).Append(vbLf)
            Next
            If Directory.Exists(Folder) Then
                For Each f As String In Directory.GetFiles(Folder)
                    sb.Append(Encoding.Latin1.GetString(File.ReadAllBytes(f))).Append(vbLf)
                Next
            End If
            Return sb.ToString()
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If Directory.Exists(Folder) Then Directory.Delete(Folder, True)
            Catch ex As IOException
            End Try
        End Sub
    End Class

    Private Shared Async Function WaitStateAsync(agent As ExchangeDeviceAgent, state As ExchangeAgentState, Optional seconds As Double = 10) As Task(Of Boolean)
        Return Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = state, seconds)
    End Function

    ' ---------------------------------------------------------------- enrollment

    <Fact>
    Public Async Function Enrolling_by_password_stores_the_token_and_the_account_and_keeps_the_stream_open() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim request As ExchangeEnrollRequest = rig.Request(acc)
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(request, CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Assert.Equal(acc.Fingerprint, result.Fingerprint)
                    Assert.Equal("", request.Password)   ' the field is cleared

                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim status As ExchangeDeviceStatus = agent.GetStatus()
                    Assert.True(status.Configured)
                    Assert.Equal("alice", status.Login)
                    Assert.Equal("TESTPC", status.DeviceName)
                    Assert.Equal(ExchangeCertificateKind.Pinned, status.Certificate)
                    Assert.Equal(acc.Fingerprint, status.Fingerprint)
                    Assert.True(status.HasFeature("pairing"))
                    Assert.Equal(acc.Address, status.Server)

                    ' the enroll frame carried the password; the device id and the token were stored
                    Assert.Equal({ExchangeStubAccount.Password}, acc.EnrollPasswordsSeen().ToArray())
                    Assert.Equal(acc.TokenOf(status.DeviceId), LoadToken(rig, status.DeviceId))
                    ' the control stream is the very stream that enrolled: one connection, then list and subscribe on it
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.ReceivedOn(1).Contains("subscribe")))
                    Assert.Equal({"enroll", "list", "subscribe"}, acc.ReceivedOn(1).Take(3).ToArray())
                    Assert.Equal(1, acc.Server.Accepted)
                    Assert.Equal(1, rig.Transport.Attempts)
                    Assert.Equal(1, rig.Channels)   ' the local channel started with the account

                    ' the password is nowhere: not in a setting, not in the token file, not in the log
                    Assert.DoesNotContain(ExchangeStubAccount.Password, rig.Everything())
                    Assert.DoesNotContain(acc.TokenOf(status.DeviceId), rig.Everything())
                End Using
            End Using
        End Using
    End Function

    Private Shared Function LoadToken(rig As Rig, deviceId As String) As String
        Dim t As String = ""
        rig.Ports.Credentials.TryLoad(deviceId, t)
        Return t
    End Function

    <Fact>
    Public Async Function Enrolling_by_a_pairing_code_sends_the_code_and_no_password() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc, "", ExchangeStubAccount.PairingCode.ToLowerInvariant()), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
                    Assert.Equal({""}, acc.EnrollPasswordsSeen().ToArray())
                    Assert.DoesNotContain(ExchangeStubAccount.PairingCode, rig.Everything())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_enrollment_is_a_named_outcome_and_leaves_nothing_behind() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc, "Wrong-Password-1"), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Refused, result.Outcome)
                    Assert.Equal(ExchangeRefusal.BadCredentials, result.Refusal)
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.False(rig.Ports.Credentials.Exists())
                    Assert.Equal(0, rig.Port.Writes)   ' not even the device id: a failed enrollment writes no value
                    Assert.Equal(0, rig.Channels)
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_certificate_the_user_has_not_accepted_never_reaches_the_server() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    ' no accepted fingerprint and the platform does not know the certificate: refused before a connection
                    Dim noAccept As ExchangeEnrollRequest = rig.Request(acc)
                    noAccept.AcceptedFingerprint = ""
                    Assert.Equal(ExchangeEnrollOutcome.Untrusted, (Await agent.EnrollAsync(noAccept, CancellationToken.None)).Outcome)
                    Assert.Equal(0, rig.Transport.Attempts)

                    ' an accepted fingerprint that is not the one the server shows
                    Dim wrong As ExchangeEnrollRequest = rig.Request(acc)
                    wrong.AcceptedFingerprint = ExchangeTrust.FingerprintOf(New Byte() {9})
                    Dim r As ExchangeEnrollResult = Await agent.EnrollAsync(wrong, CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.PinMismatch, r.Outcome)
                    Assert.Equal(acc.Fingerprint, r.Fingerprint)
                    Assert.Equal(0, acc.CountOf("enroll"))   ' the password never left
                    Assert.Equal(0, rig.Port.Writes)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Input_that_cannot_be_used_is_refused_before_anything_is_sent() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim both As ExchangeEnrollRequest = rig.Request(acc, ExchangeStubAccount.Password, "ABCD2345")
                    Dim neither As ExchangeEnrollRequest = rig.Request(acc, "", "")
                    Dim badAddress As ExchangeEnrollRequest = rig.Request(acc)
                    badAddress.Server = "not an address"
                    Dim noLogin As ExchangeEnrollRequest = rig.Request(acc)
                    noLogin.Login = "  "
                    For Each r As ExchangeEnrollRequest In {both, neither, badAddress, noLogin}
                        Assert.Equal(ExchangeEnrollOutcome.BadInput, (Await agent.EnrollAsync(r, CancellationToken.None)).Outcome)
                        Assert.Equal("", r.Password)   ' wiped whatever happens
                    Next
                    Assert.Equal(0, rig.Transport.Attempts)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_token_that_cannot_be_stored_leaves_the_device_unconfigured_and_says_so() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                rig.Cipher.Fail = True
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.StoreFailed, result.Outcome)
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)
                    Assert.Equal(0, rig.Channels)
                End Using
            End Using
        End Using
    End Function

    <Theory>
    <InlineData("rate-limited", "RateLimited")>
    <InlineData("capacity", "Capacity")>
    <InlineData("id-collision", "IdCollision")>
    <InlineData("version-unsupported", "VersionUnsupported")>
    <InlineData("tls-required", "TlsRequired")>
    <InlineData("a-reason-from-the-future", "Unknown")>
    Public Async Function Every_refusal_of_an_enroll_comes_back_as_its_reason(reason As String, expectedName As String) As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Dim refusing As New ExchangeStubTlsServer(Async Function(c)
                                                              Await c.ReadAsync()
                                                              Await c.Refuse(reason)
                                                          End Function, acc.Server.Certificate)
                Using refusing
                    Using agent As ExchangeDeviceAgent = rig.NewAgent()
                        Dim request As ExchangeEnrollRequest = rig.Request(acc)
                        request.Server = refusing.Address
                        Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(request, CancellationToken.None)
                        Assert.Equal(ExchangeEnrollOutcome.Refused, result.Outcome)
                        Assert.Equal(DirectCast([Enum].Parse(GetType(ExchangeRefusal), expectedName), ExchangeRefusal), result.Refusal)
                    End Using
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- a restart (acceptance 1)

    <Fact>
    Public Async Function After_a_restart_the_agent_is_online_with_the_stored_token_and_no_prompt() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Dim deviceId As String
                Using first As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, (Await first.EnrollAsync(rig.Request(acc), CancellationToken.None)).Outcome)
                    Assert.True(Await WaitStateAsync(first, ExchangeAgentState.Online))
                    deviceId = first.GetStatus().DeviceId
                    Await first.StopAsync()
                End Using
                Assert.Equal(1, acc.CountOf("bye"))   ' a clean exit says bye: presence goes offline at once

                ' a new process: a new agent over the same stores
                Using second As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.Equal(ExchangeAgentState.Stopped, second.GetStatus().State)   ' configured, not started yet
                    second.Start()
                    Assert.True(Await WaitStateAsync(second, ExchangeAgentState.Online))
                    Assert.Equal(deviceId, second.GetStatus().DeviceId)
                    Assert.Equal(1, acc.CountOf("hello"))   ' a hello with the token, not a second enroll
                    Assert.Equal(1, acc.CountOf("enroll"))
                    Assert.DoesNotContain(ExchangeStubAccount.Password, rig.Everything())
                    Assert.Equal({ExchangeStubAccount.Password}, acc.EnrollPasswordsSeen().ToArray())   ' only the one enroll ever saw it
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- revoked (acceptance 2)

    <Fact>
    Public Async Function A_device_revoked_elsewhere_stops_says_sign_in_again_and_is_not_retried() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Dim deviceId As String
                Using first As ExchangeDeviceAgent = rig.NewAgent()
                    Await first.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(first, ExchangeAgentState.Online))
                    deviceId = first.GetStatus().DeviceId
                    Await first.StopAsync()
                End Using
                ' the phone revokes this PC
                acc.RevokeFromServer(deviceId)

                Using second As ExchangeDeviceAgent = rig.NewAgent()
                    second.Start()
                    Assert.True(Await WaitStateAsync(second, ExchangeAgentState.SignInAgain))
                    Assert.Equal("device-revoked", second.GetStatus().LastReason)
                    Assert.False(rig.Ports.Credentials.Exists(), "the token is discarded on device-revoked")
                    Assert.True(second.GetStatus().Configured)   ' the account entry stays, so the window can say who to sign in to
                    Dim hellos As Integer = acc.CountOf("hello")
                    Await Task.Delay(500)
                    Assert.Equal(hellos, acc.CountOf("hello"))   ' no retry follows
                    Assert.Empty(rig.Waits)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_configured_account_with_no_token_says_sign_in_again_and_connects_to_nothing() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, "alice", acc.Fingerprint, ExchangeCertificateKind.Pinned, ExchangeWireTestKit.IdOf("D"c), "PC"))
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    agent.Start()
                    Assert.Equal(ExchangeAgentState.SignInAgain, agent.GetStatus().State)
                    Assert.Equal("no-token", agent.GetStatus().LastReason)
                    Assert.Equal(0, rig.Transport.Attempts)
                    Assert.Equal(0, rig.Channels)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the certificate (acceptance 3)

    <Fact>
    Public Async Function A_replaced_certificate_is_a_named_stop_and_the_agent_does_not_re_pin_by_itself() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim pinned As String = agent.GetStatus().Fingerprint
                    acc.Server.Certificate = ExchangeWireTestKit.NewSelfSignedCertificate()
                    acc.DropAll()
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.PinMismatch))
                    Assert.Equal("pin-mismatch", agent.GetStatus().LastReason)
                    Assert.Equal(pinned, agent.GetStatus().Fingerprint)   ' the stored pin is still the old one
                    Assert.Equal(pinned, rig.Port.GetValue(ExchangeAccountStore.PinName))
                    Dim attempts As Integer = rig.Transport.Attempts
                    Await Task.Delay(400)
                    Assert.Equal(attempts, rig.Transport.Attempts)   ' no retry

                    ' only the user's explicit act replaces the pin: then the stream restarts with the new one
                    agent.ReplacePinAndRestart(acc.Fingerprint, False)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Assert.Equal(acc.Fingerprint, agent.GetStatus().Fingerprint)
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- a welcome that names another address (187, the entries proposal's ask 2)

    <Fact>
    Public Async Function A_replaced_server_address_keeps_the_device_the_pin_and_the_token() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim before As ExchangeDeviceStatus = agent.GetStatus()
                    ' the stub's welcome announces a different address than the loopback one the entry holds: it is carried, shown
                    Assert.Equal("203.0.113.5:22022", before.PublicEndpoint)
                    Assert.Equal("203.0.113.5:22022", ExchangeEntryMove.Announced(before.Server, before.PublicEndpoint))
                    Dim tokenBefore As String = ""
                    Assert.True(rig.Ports.Credentials.TryLoad(before.DeviceId, tokenBefore))

                    agent.ReplaceServerAndRestart("203.0.113.5:22022")
                    ' only the address changed: the device id, the pin decision and the token are the entry's and stay
                    Assert.Equal("203.0.113.5:22022", rig.Port.GetValue(ExchangeAccountStore.ServerName))
                    Assert.Equal(before.DeviceId, rig.Port.GetValue(ExchangeAccountStore.DeviceIdName))
                    Assert.Equal(before.Fingerprint, rig.Port.GetValue(ExchangeAccountStore.PinName))
                    Dim tokenAfter As String = ""
                    Assert.True(rig.Ports.Credentials.TryLoad(before.DeviceId, tokenAfter))
                    Assert.Equal(tokenBefore, tokenAfter)
                    Dim after As ExchangeDeviceStatus = agent.GetStatus()
                    Assert.Equal("203.0.113.5:22022", after.Server)
                    Assert.Equal(before.DeviceId, after.DeviceId)
                    Assert.Equal(before.Login, after.Login)
                    Assert.Equal(before.Fingerprint, after.Fingerprint)

                    ' a value that is not an address changes nothing
                    agent.ReplaceServerAndRestart("not an address")
                    Assert.Equal("203.0.113.5:22022", rig.Port.GetValue(ExchangeAccountStore.ServerName))
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the account: pairing, revoke, leave, disconnect

    <Fact>
    Public Async Function The_agent_asks_for_a_pairing_code_and_revokes_another_device() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Assert.False((Await agent.RequestPairingCodeAsync(CancellationToken.None)).Answered)   ' not signed in yet
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim code As ExchangePairingResult = Await agent.RequestPairingCodeAsync(CancellationToken.None)
                    Assert.True(code.Succeeded)
                    Assert.Equal(ExchangeStubAccount.PairingCode, code.Code)
                    Dim other As String = ExchangeWireTestKit.IdOf("X"c)
                    Assert.True(Await agent.RevokeAsync(other, CancellationToken.None))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.RevokedDevices().Contains(other)))
                    Assert.DoesNotContain(ExchangeStubAccount.PairingCode, rig.Everything())
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Leaving_the_account_revokes_this_device_discards_the_token_and_stops() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim told As Boolean = Await agent.RevokeAsync(id, CancellationToken.None)   ' revoking THIS device is leaving the account
                    Assert.True(told)
                    Assert.Contains(id, acc.RevokedDevices())
                    Assert.False(rig.Ports.Credentials.Exists())
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(id, rig.Port.GetValue(ExchangeAccountStore.DeviceIdName))   ' the id names this Windows user and stays
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)
                    Dim attempts As Integer = rig.Transport.Attempts
                    Await Task.Delay(300)
                    Assert.Equal(attempts, rig.Transport.Attempts)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Disconnect_forgets_everything_and_sends_bye() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Await agent.DisconnectAsync()
                    Assert.Equal(1, acc.CountOf("bye"))
                    Assert.Equal(0, acc.CountOf("revoke"))   ' disconnecting is local: the account is not told to forget this device
                    Assert.False(rig.Ports.Credentials.Exists())
                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)
                    ' nothing but the device id is left in the settings, and the one note that the person left on purpose: the local join
                    ' (this PC enrolling itself in the server that runs on it) must not undo a disconnect at the next start
                    Assert.Equal({ExchangeAccountStore.DeviceIdName, ExchangeAccountStore.LocalJoinDeclinedName}.OrderBy(Function(n) n).ToArray(),
                                 rig.Port.Values.Keys.OrderBy(Function(n) n).ToArray())
                    Assert.Equal("1", rig.Port.Values(ExchangeAccountStore.LocalJoinDeclinedName))
                End Using
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- dormant (acceptance 4)

    <Fact>
    Public Sub With_no_account_the_agent_opens_no_connection_creates_no_channel_and_writes_nothing()
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    agent.Start()
                    Assert.Equal(ExchangeAgentState.NotConfigured, agent.GetStatus().State)
                    Assert.False(agent.GetStatus().Configured)
                    Assert.Equal(0, rig.Transport.Attempts)
                    Assert.Equal(0, rig.Channels)
                    Assert.Equal(0, rig.Port.Writes)
                    Assert.False(Directory.Exists(rig.Folder))
                    Assert.Equal(0, acc.Server.Accepted)
                    Assert.Empty(rig.Log)
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_host_constructs_no_agent_when_the_gate_is_shut_or_no_account_is_configured()
        Using rig As New Rig()
            Dim built As Integer = 0
            Dim create As Func(Of ExchangeAgentPorts, ExchangeDeviceAgent) = Function(p)
                                                                                 built += 1
                                                                                 Return New ExchangeDeviceAgent(p)
                                                                             End Function
            ' gate shut
            Assert.Null(ExchangeDeviceHost.StartIfConfigured(False, rig.Ports, create))
            ' gate open, nothing configured
            Assert.Null(ExchangeDeviceHost.StartIfConfigured(True, rig.Ports, create))
            Assert.Equal(0, built)
            Assert.Equal(0, rig.Transport.Attempts)
            Assert.Equal(0, rig.Port.Writes)
            Assert.Equal(0, rig.Channels)
            Assert.False(Directory.Exists(rig.Folder))
        End Using
    End Sub

    <Fact>
    Public Async Function The_host_starts_an_agent_only_for_a_configured_account_with_the_gate_open() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Dim deviceId As String = ExchangeWireTestKit.IdOf("D"c)
                Dim token As String = acc.PreEnroll(deviceId)
                rig.Ports.Accounts.Save(New ExchangeAccount(acc.Address, "alice", acc.Fingerprint, ExchangeCertificateKind.Pinned, deviceId, "PC"))
                Assert.True(rig.Ports.Credentials.Save(deviceId, token))
                Dim built As Integer = 0
                Dim agent As ExchangeDeviceAgent = ExchangeDeviceHost.StartIfConfigured(True, rig.Ports, Function(p)
                                                                                                          built += 1
                                                                                                          Return New ExchangeDeviceAgent(p)
                                                                                                      End Function)
                Assert.NotNull(agent)
                Assert.Equal(1, built)
                Using agent
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                End Using
                ExchangeDeviceHost.StopAtExit()
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- the log

    <Fact>
    Public Async Function Nothing_secret_reaches_the_log_through_a_whole_life_of_the_agent() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.True(Await WaitStateAsync(agent, ExchangeAgentState.Online))
                    Dim id As String = agent.GetStatus().DeviceId
                    Dim token As String = acc.TokenOf(id)
                    Await agent.RequestPairingCodeAsync(CancellationToken.None)
                    acc.DropAll()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.Received().Contains("2:hello")))
                    Await agent.DisconnectAsync()
                    Dim log As String = String.Join(vbLf, rig.Log)
                    Assert.NotEmpty(rig.Log)
                    Assert.DoesNotContain(ExchangeStubAccount.Password, log)
                    Assert.DoesNotContain(token, log)
                    Assert.DoesNotContain(ExchangeStubAccount.PairingCode, log)
                    Assert.DoesNotContain(id, log)                    ' a raw id: only its first 8 hex of the SHA-256 may appear
                    Assert.Contains(ExchangeIds.ForLog(id), log)
                End Using
            End Using
        End Using
    End Function

End Class
