Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 226 T-1, the tests the specification's section 5 names: registration against a scripted server (the entry a
''' registration stores is identical in shape to a sign-in's; a used code, a wrong code and a full server each answer with
''' their own sentence of the common table; the login is prefilled from the string), the signed-out state after a password
''' change elsewhere (the agent makes no second attempt), the password change that keeps this device's session and stores
''' the new key, the sign-out that leaves the device record on the server, and the secrets that reach no store and no log.
''' Against ExchangeStubAccount; no window, no Share Manager.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeRegistrationTests
    Implements IDisposable

    Private Const NewPassword As String = "NewPassword123!"

    Private ReadOnly _tokenRoot As String = Path.Combine(Path.GetTempPath(), "fms-register-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly _port As New ExchangeDeviceAgentTests.MemoryPort()
    Private ReadOnly _logs As New List(Of String)

    Private Sub NoteLog(line As String)
        SyncLock _logs
            _logs.Add(line)
        End SyncLock
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(_tokenRoot) Then Directory.Delete(_tokenRoot, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function MakePorts() As ExchangeConsumerPorts
        Return New ExchangeConsumerPorts With {
            .Entries = New ExchangeEntryStore(_port, _tokenRoot, New ExchangeDeviceAgentTests.XorCipher()),
            .MachineName = Function() "OFFICE-PC", .ProductVersion = "26.10.11.1",
            .Log = AddressOf NoteLog}
    End Function

    ''' <summary>The invitation string of 224 rule 1: the connection string of 187 carrying the invitation code.</summary>
    Private Shared Function InvitationStringOf(acc As ExchangeStubAccount, Optional login As String = "", Optional name As String = "Home NAS") As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(acc.Address, acc.Fingerprint, name, login, ExchangeStubAccount.Invitation)
        Assert.True(built.Succeeded, built.Reason)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Dim text As String = ExchangeConnectionString.Format(built.Value, refusal)
        Assert.NotNull(text)
        Return text
    End Function

    ''' <summary>A plain sign-in string of the same server (the invitation's contrast: no `i`).</summary>
    Private Shared Function PlainStringOf(acc As ExchangeStubAccount, Optional login As String = "") As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(acc.Address, acc.Fingerprint, "Home NAS", login)
        Assert.True(built.Succeeded, built.Reason)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Dim text As String = ExchangeConnectionString.Format(built.Value, refusal)
        Assert.NotNull(text)
        Return text
    End Function

    Private Function AllStoredText() As String
        Dim sb As New System.Text.StringBuilder()
        SyncLock _port.Values
            For Each kv In _port.Values
                sb.Append(kv.Key).Append("="c).Append(kv.Value).Append(vbLf)
            Next
        End SyncLock
        If Directory.Exists(_tokenRoot) Then
            For Each f As String In Directory.GetFiles(_tokenRoot, "*", SearchOption.AllDirectories)
                sb.Append(Convert.ToBase64String(File.ReadAllBytes(f)))
            Next
        End If
        Return sb.ToString()
    End Function

    Private Function LogsText() As String
        SyncLock _logs
            Return String.Join(vbLf, _logs)
        End SyncLock
    End Function

    ' ---- registration ------------------------------------------------------------------------------------------

    <Fact>
    Public Async Function Registration_stores_an_entry_identical_in_shape_to_a_sign_ins() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "carol"), "carol", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, result.Kind)

            Dim entry As ExchangeEntry = result.Entry
            Assert.Equal(1, ports.Entries.Count())
            Assert.Equal(acc.Address, entry.Server)
            Assert.Equal("carol", entry.Login)
            Assert.Equal(acc.Fingerprint, entry.Pin)                                ' pinned on first contact, exactly as a sign-in
            Assert.Equal(ExchangeCertificateKind.Pinned, entry.Certificate)
            Assert.Equal("Home NAS", entry.Name)
            Assert.Equal("OFFICE-PC (viewer)", entry.DeviceName)
            Assert.False(entry.AutoAttach)

            Dim token As String = ""
            Assert.True(ports.Entries.TokenStoreFor(entry.Id).TryLoad(entry.DeviceId, token))
            Assert.Equal(acc.TokenOf(entry.DeviceId), token)

            ' the first envelope was `register`; the account's other members are a sign-in's own
            Assert.Equal("register", acc.ReceivedOn(1).First())
            Assert.Equal({"resource-consumer"}, acc.EnrollRolesSeen().ToArray())
            Assert.Equal(ExchangeStubAccount.Invitation, acc.RegisterCodesSeen().Single())
            Assert.Equal("carol", acc.RegisteredLogins().Single())
        End Using
    End Function

    <Fact>
    Public Async Function A_used_code_and_a_wrong_code_answer_the_same_and_store_nothing() As Task
        Using acc As New ExchangeStubAccount()
            acc.UseInvitation()                                                     ' the code was spent on another machine
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim used As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "carol"), "carol", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Failed, used.Kind)
            Assert.Equal(ExchangeConsumerEnrollOutcome.Refused, used.Enroll)
            Assert.Equal(ExchangeRefusal.BadCredentials, used.Refusal)

            Dim wrong As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "dana"), "dana", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Failed, wrong.Kind)
            Assert.Equal(ExchangeRefusal.BadCredentials, wrong.Refusal)             ' the SAME answer: no oracle (224 rule 6)

            Assert.Equal(0, ports.Entries.Count())
            Assert.False(Directory.Exists(_tokenRoot))
            Assert.Empty(acc.RegisteredLogins())
        End Using
    End Function

    <Fact>
    Public Async Function A_full_server_refuses_with_capacity_and_the_sentence_is_its_own() As Task
        Using acc As New ExchangeStubAccount()
            acc.MaxAccounts = 1                                                    ' the account that exists fills the limit
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "carol"), "carol", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Failed, result.Kind)
            Assert.Equal(ExchangeRefusal.Capacity, result.Refusal)
            Assert.Equal(0, ports.Entries.Count())
        End Using
    End Function

    <Fact>
    Public Async Function A_weak_password_is_refused_before_the_account_is_made() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "carol"), "carol", "short", "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Failed, result.Kind)
            Assert.Equal(ExchangeRefusal.BadCredentials, result.Refusal)
            Assert.Equal(0, ports.Entries.Count())
        End Using
    End Function

    <Fact>
    Public Async Function The_registration_is_offered_only_where_the_string_is_an_invitation() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            ' a plain connection string (no `i`) and no shared password: the flow refuses, the dialog keeps the sign-in
            Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(acc.Address, acc.Fingerprint, "", "")
            Dim refusal As ExchangeConnectionStringResult = Nothing
            Dim plain As String = ExchangeConnectionString.Format(built.Value, refusal)
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(plain, "carol", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.InputRefused, result.Kind)
            Assert.Empty(acc.Received())
        End Using
    End Function

    <Fact>
    Public Async Function The_shared_invitation_password_registers_when_the_operator_set_one() As Task
        Using acc As New ExchangeStubAccount()
            acc.SharedInvitationPassword = "Family-Invite-42"
            Dim ports As ExchangeConsumerPorts = MakePorts()
            ' the string carries no `i`; the person types the shared password the operator handed out
            Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(acc.Address, acc.Fingerprint, "Home NAS", "")
            Dim refusal As ExchangeConnectionStringResult = Nothing
            Dim text As String = ExchangeConnectionString.Format(built.Value, refusal)
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(text, "carol", NewPassword, "Family-Invite-42", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, result.Kind)
            Assert.Equal(1, ports.Entries.Count())
        End Using
    End Function

    <Fact>
    Public Sub The_login_hint_of_the_invitation_string_is_read_by_the_same_classify()
        Using acc As New ExchangeStubAccount()
            Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(InvitationStringOf(acc, "carol"))
            Assert.Equal(ExchangeConnectInputKind.ConnectionString, input.Kind)
            Assert.True(input.HasInvitation)
            Assert.Equal("carol", input.Value.Login)                                ' the dialog prefills it and never submits
            Assert.False(ExchangeConnectInput.Classify(PlainStringOf(acc)).HasInvitation)
        End Using
    End Sub

    ' ---- the signed-out state and the stop-reconnect rule (rule 3) --------------------------------------------

    <Fact>
    Public Async Function A_password_changed_elsewhere_turns_the_entry_into_signed_out_and_the_agent_never_retries() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim added As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(PlainStringOf(acc, "alice"), "alice", ExchangeStubAccount.Password, "", "", ports,
                                                                                   Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, added.Kind)

            acc.ChangePasswordFromElsewhere(NewPassword)                            ' 224 rules 8-9: every token dies

            Dim session As New ExchangeEntrySession(added.Entry, ports)
            Using lease As IDisposable = session.Lease()
                Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(10)
                While DateTime.UtcNow < deadline
                    Dim state As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                    If state.Info.State = ExchangeAgentState.SignInAgain Then Exit While
                    Await Task.Delay(20)
                End While
                Dim final As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                Assert.Equal(ExchangeAgentState.SignInAgain, final.Info.State)      ' "Signed out - sign in again"
                Assert.Equal("bad-credentials", final.Info.LastReason)
                Dim hellos As Integer = acc.CountOf("hello")
                Assert.Equal(1, hellos)                                            ' one attempt, refused

                Await Task.Delay(2000)
                Assert.Equal(hellos, acc.CountOf("hello"))                         ' no retry loop against a refusal (218)
            End Using
            session.Dispose()
        End Using
    End Function

    ' ---- changing the password from this device (rule 4) --------------------------------------------------------

    <Fact>
    Public Async Function A_password_change_keeps_this_device_and_stores_the_new_key_while_the_others_sign_in_again() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim added As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, "", "", ports,
                Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, added.Kind)
            Dim otherToken As String = acc.PreEnroll("PPPPPPPPPPPPPPPPPPPPPA")     ' the account's other device

            Dim session As New ExchangeEntrySession(added.Entry, ports)
            Using lease As IDisposable = session.Lease()
                Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(10)
                While DateTime.UtcNow < deadline
                    Dim state As ExchangeAgentStateResult = Await session.GetStateAsync(CancellationToken.None)
                    If state.Info.State = ExchangeAgentState.Online Then Exit While
                    Await Task.Delay(20)
                End While

                Dim result As ExchangePasswordChangeResult = Await session.ChangePasswordAsync(ExchangeStubAccount.Password, NewPassword, CancellationToken.None)
                Assert.Equal(ExchangePasswordChangeOutcome.Changed, result.Outcome)

                ' the fresh key of the answer is stored: this device keeps working
                Dim token As String = ""
                Assert.True(ports.Entries.TokenStoreFor(added.Entry.Id).TryLoad(added.Entry.DeviceId, token))
                Assert.Equal(result.FreshToken, token)
                Assert.Equal("", acc.TokenOf("PPPPPPPPPPPPPPPPPPPPPA"))            ' the OTHER devices are signed out

                ' a wrong old password is the common refusal and changes nothing
                Dim wrongOld As ExchangePasswordChangeResult = Await session.ChangePasswordAsync("not-the-password", "Another-Password-9", CancellationToken.None)
                Assert.Equal(ExchangePasswordChangeOutcome.Refused, wrongOld.Outcome)
                Assert.Equal(ExchangeRefusal.BadCredentials, wrongOld.Refusal)

                ' A dropped wire can reconnect before the window rebuilds its hub. It must use the fresh token immediately.
                Dim beforeReconnect As Integer = acc.CountOf("hello")
                acc.DropAll()
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.CountOf("hello") > beforeReconnect))
                Assert.True(Await ExchangeStubAccount.WaitUntilAsync(
                    Function() session.GetStateAsync(CancellationToken.None).GetAwaiter().GetResult().Info.State = ExchangeAgentState.Online))
            End Using
            session.Dispose()
        End Using
    End Function

    ' ---- signing out on this device (rule 5) --------------------------------------------------------------------

    <Fact>
    Public Async Function Signing_out_on_this_device_removes_the_entry_and_the_key_and_sends_nothing_that_deletes_the_device() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim added As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, "", "", ports,
                Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, added.Kind)
            Assert.Equal(1, ports.Entries.Count())

            Assert.True(ports.Entries.Remove(added.Entry.Id))                      ' the store's own sign-out: entry and key gone
            Assert.Equal(0, ports.Entries.Count())
            Dim gone As String = ""
            Assert.False(ports.Entries.TokenStoreFor(added.Entry.Id).TryLoad(added.Entry.DeviceId, gone))
            Assert.Contains(added.Entry.DeviceId, acc.Enrolled())                   ' the device record stays and shows offline
            Assert.Equal(0, acc.CountOf("revoke"))                                 ' nothing that deletes the device was sent
        End Using
    End Function

    ' ---- the secrets (rule 6) -----------------------------------------------------------------------------------

    <Fact>
    Public Async Function No_invitation_code_password_or_key_reaches_a_store_or_a_log_line() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(InvitationStringOf(acc, "carol"), "carol", NewPassword, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, result.Kind)

            Assert.Equal(NewPassword, acc.RegisterPasswordsSeen().Single())        ' it crossed the wire once, as the field of `register`
            Assert.DoesNotContain(ExchangeStubAccount.Invitation, AllStoredText())
            Assert.DoesNotContain(NewPassword, AllStoredText())
            Assert.DoesNotContain(ExchangeStubAccount.Invitation, LogsText())
            Assert.DoesNotContain(NewPassword, LogsText())
            Assert.DoesNotContain("register", LogsText())                          ' the flow itself logs nothing of the registration
        End Using
    End Function
    ' ---- QR code intake (rule 1, T-3) --------------------------------------------------------------------------

    <Fact>
    Public Sub Invitation_rendered_to_QR_code_decodes_back_via_ExchangeQrDecoder()
        Using acc As New ExchangeStubAccount()
            Dim invitationStr As String = InvitationStringOf(acc, "dave")
            Using gen As New QRCoder.QRCodeGenerator()
                Using data As QRCoder.QRCodeData = gen.CreateQrCode(invitationStr, QRCoder.QRCodeGenerator.ECCLevel.M)
                    Dim png As New QRCoder.PngByteQRCode(data)
                    Dim pngBytes As Byte() = png.GetGraphic(8)
                    Using ms As New MemoryStream(pngBytes)
                        Using bmp As New Bitmap(ms)
                            Dim decoded As QrDecodeResult = QrCodeDecoder.DecodeBitmap(bmp)
                            Assert.True(decoded.Succeeded)
                            Assert.Equal(invitationStr, decoded.Text)

                            Dim input As ExchangeConnectInput = ExchangeConnectInput.Classify(decoded.Text)
                            Assert.True(input.HasInvitation)
                            Assert.Equal("dave", input.Value.Login)
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Async Function Agent_disconnect_forgets_locally_and_sends_no_revoke() As Task
        Using acc As New ExchangeStubAccount()
            Using rig As New ExchangeDeviceAgentTests.Rig()
                Using agent As ExchangeDeviceAgent = rig.NewAgent()
                    Dim enrolled As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
                    Assert.Equal(ExchangeEnrollOutcome.Enrolled, enrolled.Outcome)
                    Assert.True(rig.Ports.Accounts.IsConfigured())
                    Assert.True(rig.Ports.Credentials.Exists())
                    Dim deviceId As String = agent.GetStatus().DeviceId
                    Assert.Contains(deviceId, acc.Enrolled())

                    Await agent.DisconnectAsync()

                    Assert.False(rig.Ports.Accounts.IsConfigured())
                    Assert.False(rig.Ports.Credentials.Exists())
                    Assert.Contains(deviceId, acc.Enrolled())
                    Assert.Equal(0, acc.CountOf("revoke"))
                End Using
            End Using
        End Using
    End Function

End Class

