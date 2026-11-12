Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-9 (specification 7.5 and 7.7): the REAL <c>fms-exchange-server.exe</c> driven through the REAL remote administration
''' session. No stub, no scripted transport: a scratch data directory (<c>FMS_EXCHANGE_DATA_DIR</c>, a fresh certificate on the first
''' start), the binary's own CLI for <c>admin on</c>, <c>account add</c> and <c>set-listen</c>, <c>serve</c> on a free LOOPBACK port, and
''' <see cref="ExchangeRemoteAdminSession"/> over real TLS with the builders of <see cref="ExchangeAdminWire"/>. The way the Go side
''' does it in <c>stop_windows_test.go</c>, from the client's end.
'''
''' The binary: <see cref="RealBinaryFactAttribute"/> finds it (an explicit <c>FMS_EXCHANGE_TEST_EXE</c>, else
''' <c>bin\Release\exchange</c>, else <c>exchange-server\bin</c>) and reports a SKIP when there is none; <c>FMS_REQUIRE_BINARY=1</c>
''' turns that into a failure raised here, in <see cref="ServerRig.Create"/>. A run that went green and wrote no
''' <c>binary-used ..</c> line did not drive a binary.
'''
''' What is judged here and nowhere else: the trust path of 5.4 against a self-signed leaf (first contact, then the pins), the 18
''' operations against the server's own answers, the end of the session, and that no password and no token reach a file or a log. What
''' is not: the lapse needs 61 idle seconds and is marked release-only; the platform-valid certificate path needs a certificate the
''' machine trusts; the live-host test reads two environment variables, is skipped without them and says nothing about their values.
'''
''' Rules the helpers keep: the server is only ever told to listen on 127.0.0.1 (the test fails and kills it if it printed another
''' address); no public endpoint is set (the binary refuses a loopback one), so <c>connectionString</c> is the empty member of
''' specification section 6; the process is killed and the scratch folder deleted in <c>Dispose</c> whatever happened.
''' </summary>
Public Class RemoteAdminBinaryIntegrationTests

    Private Const LoopbackHost As String = "127.0.0.1"
    Private Const DataDirVariable As String = "FMS_EXCHANGE_DATA_DIR"
    Private Const ProofFileName As String = "fms-remote-admin-binary.proof.txt"

    ' ================================================================================================ the skip logic
    ' No binary needed: these hold the claim that a missing binary is a SKIP, not a pass (specification F-3), by calling the
    ' attribute's own decision over a path that does not exist - no real file is renamed.

    <Fact>
    Public Sub A_missing_binary_is_reported_as_a_skip_and_the_require_switch_turns_it_into_a_failure()
        Dim missing As String = Path.Combine(Path.GetTempPath(), "fms-no-such-folder-" & Guid.NewGuid().ToString("N"), ExchangeProcess.ExeName)
        Assert.False(File.Exists(missing))

        ' nothing found over a missing path, with or without a repository root
        Assert.Null(RealBinaryFactAttribute.LocateIn({missing}))
        Assert.Null(RealBinaryFactAttribute.LocateIn(RealBinaryFactAttribute.Candidates(Nothing, missing)))
        Assert.Null(RealBinaryFactAttribute.LocateIn(RealBinaryFactAttribute.Candidates(Path.GetDirectoryName(missing), Nothing)))

        ' the order the specification and the neighbouring tests give: explicit, staged distribution, build output
        Dim root As String = Path.Combine("R", "root")
        Dim order As List(Of String) = RealBinaryFactAttribute.Candidates(root, "explicit.exe")
        Assert.Equal(3, order.Count)
        Assert.Equal("explicit.exe", order(0))
        Assert.Equal(Path.Combine(root, "bin", "Release", "exchange", ExchangeProcess.ExeName), order(1))
        Assert.Equal(Path.Combine(root, "exchange-server", "bin", ExchangeProcess.ExeName), order(2))

        ' no binary: a skip with the text the specification names; only the value 1 of the switch changes that
        Assert.Equal("the server binary is not built", RealBinaryFactAttribute.NotBuilt)
        Assert.Equal(RealBinaryFactAttribute.NotBuilt, RealBinaryFactAttribute.SkipReason(Nothing, Nothing))
        Assert.Equal(RealBinaryFactAttribute.NotBuilt, RealBinaryFactAttribute.SkipReason("", ""))
        Assert.Equal(RealBinaryFactAttribute.NotBuilt, RealBinaryFactAttribute.SkipReason(Nothing, "0"))
        Assert.Equal(RealBinaryFactAttribute.NotBuilt, RealBinaryFactAttribute.SkipReason(Nothing, "true"))
        ' required: no skip - the test body runs and fails there (ServerRig.Create)
        Assert.Null(RealBinaryFactAttribute.SkipReason(Nothing, "1"))
        ' present: runs, with or without the switch
        Assert.Null(RealBinaryFactAttribute.SkipReason("C:\somewhere\" & ExchangeProcess.ExeName, Nothing))
        Assert.Null(RealBinaryFactAttribute.SkipReason("C:\somewhere\" & ExchangeProcess.ExeName, "1"))

        ' the attribute itself applies exactly that decision on this machine
        Dim attribute As New RealBinaryFactAttribute()
        Dim expected As String = RealBinaryFactAttribute.SkipReason(RealBinaryFactAttribute.Locate(), Environment.GetEnvironmentVariable(RealBinaryFactAttribute.RequireVariable))
        Assert.Equal(expected, attribute.Skip)
    End Sub

    <Fact>
    Public Sub The_live_host_attribute_skips_without_both_values_and_names_neither_variable()
        Assert.Equal(RemoteAdminLiveHostFactAttribute.NotConfigured, RemoteAdminLiveHostFactAttribute.SkipReason(Nothing, Nothing))
        Assert.Equal(RemoteAdminLiveHostFactAttribute.NotConfigured, RemoteAdminLiveHostFactAttribute.SkipReason("a-host", Nothing))
        Assert.Equal(RemoteAdminLiveHostFactAttribute.NotConfigured, RemoteAdminLiveHostFactAttribute.SkipReason("a-host", ""))
        Assert.Equal(RemoteAdminLiveHostFactAttribute.NotConfigured, RemoteAdminLiveHostFactAttribute.SkipReason(" ", "a-password"))
        Assert.Null(RemoteAdminLiveHostFactAttribute.SkipReason("a-host", "a-password"))
        Assert.DoesNotContain(RemoteAdminLiveHostFactAttribute.HostVariable, RemoteAdminLiveHostFactAttribute.NotConfigured)
        Assert.DoesNotContain(RemoteAdminLiveHostFactAttribute.PasswordVariable, RemoteAdminLiveHostFactAttribute.NotConfigured)
    End Sub

    ' ================================================================================================ the real trust path and the 18 operations

    <RealBinaryFact>
    Public Async Function The_real_trust_path_and_the_18_operations_run_against_the_binary_and_leave_no_secret_behind() As Task
        Dim adminPassword As String = "Adm9" & Guid.NewGuid().ToString("N")
        Dim unique As String = Guid.NewGuid().ToString("N").Substring(0, 12)
        Dim bobFirst As String = "Bob9first" & unique
        Dim bobSecond As String = "Bob9second" & unique
        Dim sharedPassword As String = "Shared9pw" & unique

        Using rig As ServerRig = ServerRig.Create()
            rig.Configure(adminPassword, 0)
            Await rig.StartAsync()
            Assert.StartsWith("fms-exchange-server ", rig.VersionLine)

            Dim handshake As New RecordingHandshake()
            Dim lines As New ConcurrentQueue(Of String)()
            Dim ran As New HashSet(Of String)(StringComparer.Ordinal)
            Dim session As ExchangeRemoteAdminSession = NewSession(handshake, lines)
            Dim inviteCode As String = ""
            Dim connectionString As String = ""
            Dim pairingCode As String = ""
            Dim presentedFingerprint As String = ""
            Try
                ' 1. first contact: the leaf is self-signed, no pin is held, so a person decides and NO password is read or sent
                Dim first As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, "", "", NeverAsked(), CancellationToken.None)
                Assert.True(first.Kind = ExchangeRemoteAdminSignInKind.NeedsFirstContact, "the first sign-in against a self-signed leaf should ask first contact: " & first.ToString())
                presentedFingerprint = first.Fingerprint
                Assert.StartsWith("SHA256:", presentedFingerprint)
                Assert.Equal(rig.Fingerprint, presentedFingerprint)   ' the pin the client shows is the one the server prints
                Assert.False(session.IsLive)

                ' 2. the same sign-in with the pins the person accepted: the real trust path, then the password
                Dim second As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, first.Fingerprint, first.KeyPin, PasswordOf(adminPassword), CancellationToken.None)
                Assert.True(second.Kind = ExchangeRemoteAdminSignInKind.SignedIn, "the sign-in with the accepted pins should succeed: " & second.ToString() & " (" & second.Detail & ")")
                Assert.True(session.IsLive)
                Assert.Same(session, second.Session)

                ' 3. the 18 operations, in an order that makes each valid (the admin-ops vector stream is the reference)
                Dim o As ExchangeRemoteAdminOutcome

                o = Await CallOp(session, ran, ExchangeAdminWire.OpStatus, Function(t, i) ExchangeAdminWire.Status(t, i))
                AssertDone(ExchangeAdminWire.OpStatus, o)
                Assert.Equal(JsonValueKind.Object, o.Data.ValueKind)
                Assert.Equal(1L, Num(o.Data, "accounts"))                    ' alice, added through the CLI before the start

                o = Await CallOp(session, ran, ExchangeAdminWire.OpPolicyStatus, Function(t, i) ExchangeAdminWire.PolicyStatus(t, i))
                AssertDone(ExchangeAdminWire.OpPolicyStatus, o)
                Assert.True(Num(o.Data, "maxAccounts") >= 1)
                Assert.True(HasMember(o.Data, "defaults"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpPolicySet, Function(t, i) ExchangeAdminWire.PolicySet(t, i, 8, Nothing))
                AssertDone(ExchangeAdminWire.OpPolicySet, o)
                Assert.Equal(8L, Num(o.Data, "maxAccounts"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountList, Function(t, i) ExchangeAdminWire.AccountList(t, i))
                AssertDone(ExchangeAdminWire.OpAccountList, o)
                Dim item As JsonElement
                Assert.Equal(JsonValueKind.Array, o.Data.ValueKind)
                Assert.True(FindBy(o.Data, "login", "alice", item))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountAdd, Function(t, i) ExchangeAdminWire.AccountAdd(t, i, "bob", bobFirst, "member"))
                AssertDone(ExchangeAdminWire.OpAccountAdd, o)
                Assert.Equal("bob", Txt(o.Data, "login"))                    ' the reply carries the normalised login
                Assert.True(HasMember(o.Data, "overCap"))
                Assert.False(Truth(o.Data, "overCap"))

                Dim limits As New ExchangeAdminWireLimits With {.DevicesPerAccount = 4}
                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountLimits, Function(t, i) ExchangeAdminWire.AccountLimits(t, i, "bob", limits))
                AssertDone(ExchangeAdminWire.OpAccountLimits, o)

                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountPasswd, Function(t, i) ExchangeAdminWire.AccountPasswd(t, i, "bob", bobSecond))
                AssertDone(ExchangeAdminWire.OpAccountPasswd, o)
                Assert.Equal(0L, Num(o.Data, "signedOut"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountKind, Function(t, i) ExchangeAdminWire.AccountKind(t, i, "bob", "guest"))
                AssertDone(ExchangeAdminWire.OpAccountKind, o)
                Assert.True(Truth(o.Data, "changed"))

                ' read back what the three mutations did (the card re-reads its own list after a change)
                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountList, Function(t, i) ExchangeAdminWire.AccountList(t, i))
                AssertDone(ExchangeAdminWire.OpAccountList, o)
                Assert.True(FindBy(o.Data, "login", "bob", item))
                Assert.Equal("guest", Txt(item, "kind"))
                Dim ownLimits As JsonElement
                Assert.True(item.TryGetProperty("limits", ownLimits))
                Assert.Equal(4L, Num(ownLimits, "devicesPerAccount"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpInvitationCreate, Function(t, i) ExchangeAdminWire.InvitationCreate(t, i, "member"))
                AssertDone(ExchangeAdminWire.OpInvitationCreate, o)
                inviteCode = Txt(o.Data, "code")
                Assert.NotEqual("", inviteCode)
                Dim invitation As JsonElement
                Assert.True(o.Data.TryGetProperty("invitation", invitation))
                Dim codeHash As String = Txt(invitation, "codeHash")
                Assert.NotEqual("", codeHash)
                Assert.True(HasMember(o.Data, "connectionString"))           ' present, and empty because the server has no public endpoint (section 6)
                connectionString = Txt(o.Data, "connectionString")
                Assert.Equal("", connectionString)

                o = Await CallOp(session, ran, ExchangeAdminWire.OpInvitationList, Function(t, i) ExchangeAdminWire.InvitationList(t, i))
                AssertDone(ExchangeAdminWire.OpInvitationList, o)
                Assert.Equal(JsonValueKind.Array, o.Data.ValueKind)
                Assert.True(FindBy(o.Data, "codeHash", codeHash, item))
                Assert.False(HasMember(item, "code"))                        ' the plaintext code is returned once, never listed
                Assert.DoesNotContain(inviteCode, o.Data.GetRawText())

                o = Await CallOp(session, ran, ExchangeAdminWire.OpInvitationWithdraw, Function(t, i) ExchangeAdminWire.InvitationWithdraw(t, i, codeHash))
                AssertDone(ExchangeAdminWire.OpInvitationWithdraw, o)

                o = Await CallOp(session, ran, ExchangeAdminWire.OpSharedInvitationStatus, Function(t, i) ExchangeAdminWire.SharedInvitationStatus(t, i))
                AssertDone(ExchangeAdminWire.OpSharedInvitationStatus, o)
                Assert.True(HasMember(o.Data, "enabled"))
                Assert.False(Truth(o.Data, "enabled"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpSharedInvitationSet, Function(t, i) ExchangeAdminWire.SharedInvitationSet(t, i, sharedPassword, "guest"))
                AssertDone(ExchangeAdminWire.OpSharedInvitationSet, o)
                o = Await CallOp(session, ran, ExchangeAdminWire.OpSharedInvitationStatus, Function(t, i) ExchangeAdminWire.SharedInvitationStatus(t, i))
                AssertDone(ExchangeAdminWire.OpSharedInvitationStatus, o)
                Assert.True(Truth(o.Data, "enabled"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpDeviceList, Function(t, i) ExchangeAdminWire.DeviceList(t, i, "alice"))
                AssertDone(ExchangeAdminWire.OpDeviceList, o)
                Assert.True(o.HasData)
                Assert.Equal(JsonValueKind.Array, o.Data.ValueKind)          ' never null: an account with no device lists as []
                Assert.Equal(0, o.Data.GetArrayLength())

                o = Await CallOp(session, ran, ExchangeAdminWire.OpDevicePairingCode, Function(t, i) ExchangeAdminWire.DevicePairingCode(t, i, "alice"))
                AssertDone(ExchangeAdminWire.OpDevicePairingCode, o)
                pairingCode = Txt(o.Data, "code")
                Assert.NotEqual("", pairingCode)
                Assert.NotEqual("", Txt(o.Data, "expiresAt"))

                o = Await CallOp(session, ran, ExchangeAdminWire.OpDeviceRevoke, Function(t, i) ExchangeAdminWire.DeviceRevoke(t, i, "alice", "non-existent-device-id"))
                Assert.True(o.Kind = ExchangeRemoteAdminOutcomeKind.Failed, "device.revoke of an unknown device should be Failed, not " & o.ToString())
                Assert.Equal("not-found", o.Code)

                o = Await CallOp(session, ran, ExchangeAdminWire.OpGrantList, Function(t, i) ExchangeAdminWire.GrantList(t, i, "alice", Nothing))
                AssertDone(ExchangeAdminWire.OpGrantList, o)
                Assert.True(o.HasData)
                Assert.Equal(JsonValueKind.Array, o.Data.ValueKind)
                Assert.Equal(0, o.Data.GetArrayLength())

                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountRemove, Function(t, i) ExchangeAdminWire.AccountRemove(t, i, "bob"))
                AssertDone(ExchangeAdminWire.OpAccountRemove, o)
                o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountList, Function(t, i) ExchangeAdminWire.AccountList(t, i))
                AssertDone(ExchangeAdminWire.OpAccountList, o)
                Assert.False(FindBy(o.Data, "login", "bob", item))

                Dim missed As List(Of String) = ExchangeAdminWire.CarriedOps.Where(Function(op) Not ran.Contains(op)).ToList()
                Assert.True(missed.Count = 0, "operations never exercised: " & String.Join(", ", missed))
                Assert.Equal(18, ExchangeAdminWire.CarriedOps.Count)

                ' 4. the end: bye, the server closes, the session is over and writes nothing more
                Await session.EndAsync()
                Assert.False(session.IsLive)
                o = Await CallOp(session, ran, ExchangeAdminWire.OpStatus, Function(t, i) ExchangeAdminWire.Status(t, i))
                Assert.True(o.Kind = ExchangeRemoteAdminOutcomeKind.Lapsed, "a call after EndAsync should come back Lapsed, not " & o.ToString())
            Finally
                session.Dispose()
            End Try

            rig.StopAndAssert()
            Dim token As String = handshake.SeenToken()
            Assert.True(token.Length >= 16, "the token of the admin-auth-ok frame was not seen on the wire, so the secrets scan below would prove nothing")

            ' 5. the secrets scan: nothing a person typed, and nothing the server minted for this session, is in the data directory,
            ' in what the server printed, or in the session's own log
            Dim secrets As New SortedDictionary(Of String, String)(StringComparer.Ordinal) From {
                {"administration password", adminPassword},
                {"session token", token},
                {"token prefix", token.Substring(0, 12)},
                {"account password (add)", bobFirst},
                {"account password (change)", bobSecond},
                {"shared invitation password", sharedPassword},
                {"invitation code", inviteCode},
                {"pairing code", pairingCode}}
            For Each pair As KeyValuePair(Of String, String) In secrets
                Assert.False(String.IsNullOrEmpty(pair.Value), pair.Key & " is empty: the scan would be vacuous")
            Next
            Dim scannedFiles As Integer = 0
            Dim hits As List(Of String) = rig.FindSecrets(secrets, scannedFiles)
            Assert.True(scannedFiles >= 1, "the data directory held no file to scan")
            Assert.True(hits.Count = 0, "secrets found: " & String.Join("; ", hits))

            ' a positive control, so a scan that reads nothing cannot pass: a login the CLI stored IS in a data-directory file
            Dim control As New SortedDictionary(Of String, String)(StringComparer.Ordinal) From {{"control", "alice"}}
            Dim controlHits As List(Of String) = rig.FindSecrets(control, scannedFiles)
            Assert.True(controlHits.Any(Function(h As String) h.StartsWith("control in ", StringComparison.Ordinal) AndAlso Not h.Contains("printed", StringComparison.Ordinal)),
                        "the scan did not find a login that is on disk, so an empty result above proves nothing")

            Dim logText As String = String.Join(vbLf, lines)
            Assert.True(lines.Count >= 20, "the session log has fewer lines than the connect, the sign-in, the 18 operations and the end")
            For Each line As String In lines
                Assert.StartsWith("remote-admin: ", line)
            Next
            For Each pair As KeyValuePair(Of String, String) In secrets
                Assert.False(logText.Contains(pair.Value, StringComparison.Ordinal), pair.Key & " reached the session log")
            Next
            Assert.False(logText.Contains(LoopbackHost, StringComparison.Ordinal), "the host reached the session log")
            Assert.False(logText.Contains(rig.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "the port reached the session log")
            Assert.False(logText.Contains(presentedFingerprint, StringComparison.Ordinal), "the fingerprint reached the session log")
            Assert.False(logText.Contains("SHA256:", StringComparison.Ordinal), "a fingerprint reached the session log")

            ' 6. the proof that a binary ran: one line, written only by a test that got here
            WriteProofLine(rig.ProofLine())
        End Using
    End Function

    ' ================================================================================================ refusals

    <RealBinaryFact>
    Public Async Function A_wrong_password_is_refused_with_bad_credentials_and_the_session_can_sign_in_again() As Task
        Dim adminPassword As String = "Adm9" & Guid.NewGuid().ToString("N")
        Using rig As ServerRig = ServerRig.Create()
            rig.Configure(adminPassword, 0)
            Await rig.StartAsync()

            Dim lines As New ConcurrentQueue(Of String)()
            Using session As ExchangeRemoteAdminSession = NewSession(ExchangeAdminHandshake.Instance, lines)
                Dim first As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, "", "", NeverAsked(), CancellationToken.None)
                Assert.True(first.Kind = ExchangeRemoteAdminSignInKind.NeedsFirstContact, first.ToString())

                Dim wrong As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, first.Fingerprint, first.KeyPin, PasswordOf(adminPassword & "x"), CancellationToken.None)
                Assert.True(wrong.Kind = ExchangeRemoteAdminSignInKind.Refused, "a wrong password should be Refused, not " & wrong.ToString())
                Assert.Equal(ExchangeRefusal.BadCredentials, wrong.Refusal)
                Assert.Null(wrong.Session)
                Assert.False(session.IsLive)

                ' the server allows one password check a second per address: wait it out (a bounded wait, not a poll), then the
                ' same object signs in with the right password - a refusal leaves nothing behind
                Await Task.Delay(1300)
                Dim right As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, first.Fingerprint, first.KeyPin, PasswordOf(adminPassword), CancellationToken.None)
                Assert.True(right.Kind = ExchangeRemoteAdminSignInKind.SignedIn, "the right password after a refusal should sign in: " & right.ToString() & " (" & right.Detail & ")")
                Await session.EndAsync()
            End Using

            Dim text As String = String.Join(vbLf, lines)
            Assert.False(text.Contains(adminPassword, StringComparison.Ordinal), "the password reached the session log")
            Assert.False(text.Contains(LoopbackHost, StringComparison.Ordinal), "the host reached the session log")
        End Using
    End Function

    <RealBinaryFact>
    Public Async Function Administration_off_answers_disabled_after_a_restart_with_the_same_certificate() As Task
        Dim adminPassword As String = "Adm9" & Guid.NewGuid().ToString("N")
        Using rig As ServerRig = ServerRig.Create()
            rig.Configure(adminPassword, 0)
            Await rig.StartAsync()

            Dim lines As New ConcurrentQueue(Of String)()
            Dim pinLeaf As String = ""
            Dim pinKey As String = ""
            Using session As ExchangeRemoteAdminSession = NewSession(ExchangeAdminHandshake.Instance, lines)
                Dim first As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, "", "", NeverAsked(), CancellationToken.None)
                Assert.True(first.Kind = ExchangeRemoteAdminSignInKind.NeedsFirstContact, first.ToString())
                pinLeaf = first.Fingerprint
                pinKey = first.KeyPin
                Dim on1 As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, pinLeaf, pinKey, PasswordOf(adminPassword), CancellationToken.None)
                Assert.True(on1.Kind = ExchangeRemoteAdminSignInKind.SignedIn, "administration is on: " & on1.ToString())
                Await session.EndAsync()
            End Using

            ' the CLI change takes effect only on the next start: stop, turn it off, start again
            rig.StopAndAssert()
            Assert.Equal(0, rig.RunCli({"admin", "off"}, Nothing))
            Await rig.StartAsync()
            Assert.Equal(pinLeaf, rig.Fingerprint)                          ' the certificate lives in the data directory and survives the restart

            Using session As ExchangeRemoteAdminSession = NewSession(ExchangeAdminHandshake.Instance, lines)
                Dim off As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, pinLeaf, pinKey, PasswordOf(adminPassword), CancellationToken.None)
                Assert.True(off.Kind = ExchangeRemoteAdminSignInKind.Disabled, "administration off should be Disabled, not " & off.ToString() & " (" & off.Detail & ")")
                Assert.Equal(ExchangeRefusal.AdminDisabled, off.Refusal)
                Assert.False(session.IsLive)
            End Using
        End Using
    End Function

    ' ================================================================================================ release only: the lapse

    ''' <summary>
    ''' RELEASE ONLY (specification 7.5, <c>TESTING_AND_QA</c> section 2): the shortest real lapse is the 1-minute lifetime plus a
    ''' margin, 61 s of wall clock. It is marked with the trait so any other run leaves it out (<c>--filter Category!=ReleaseOnly</c>), is
    ''' never the only evidence of the lapse (the scripted session tests cover it without waiting) and is the loopback answer to the open
    ''' question of evidence item 4: whether a request written into a stream the server closed for idleness comes back as a clean end
    ''' of stream (<c>Lapsed</c>) or as a reset (<c>Unreachable</c>).
    ''' </summary>
    <RealBinaryFact>
    <Trait("Category", "ReleaseOnly")>
    <Trait("Duration", "61s")>
    Public Async Function Release_only_lapse_after_61_idle_seconds_at_the_one_minute_lifetime() As Task
        Dim adminPassword As String = "Adm9" & Guid.NewGuid().ToString("N")
        Using rig As ServerRig = ServerRig.Create()
            rig.Configure(adminPassword, 1)
            Await rig.StartAsync()

            Dim lines As New ConcurrentQueue(Of String)()
            Using session As ExchangeRemoteAdminSession = NewSession(ExchangeAdminHandshake.Instance, lines)
                Dim first As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, "", "", NeverAsked(), CancellationToken.None)
                Assert.True(first.Kind = ExchangeRemoteAdminSignInKind.NeedsFirstContact, first.ToString())
                Dim signedIn As ExchangeRemoteAdminSignIn = Await session.SignInAsync(LoopbackHost, rig.Port, first.Fingerprint, first.KeyPin, PasswordOf(adminPassword), CancellationToken.None)
                Assert.True(signedIn.Kind = ExchangeRemoteAdminSignInKind.SignedIn, signedIn.ToString())
                Assert.True(lines.Any(Function(l As String) l.Contains("window-60s", StringComparison.Ordinal)), "the server did not grant the 1-minute lifetime")

                Await Task.Delay(TimeSpan.FromSeconds(61))

                Dim ran As New HashSet(Of String)(StringComparer.Ordinal)
                Dim o As ExchangeRemoteAdminOutcome = Await CallOp(session, ran, ExchangeAdminWire.OpStatus, Function(t, i) ExchangeAdminWire.Status(t, i))
                Assert.True(o.Kind = ExchangeRemoteAdminOutcomeKind.Lapsed,
                            "an idle stream the server closed should read as Lapsed, but it came back " & o.ToString() & " (" & o.Detail & ") - the FIN versus RST question of evidence item 4")
                Assert.False(session.IsLive)
                ' a lapsed session writes nothing more and keeps answering the same kind
                Dim again As ExchangeRemoteAdminOutcome = Await CallOp(session, ran, ExchangeAdminWire.OpStatus, Function(t, i) ExchangeAdminWire.Status(t, i))
                Assert.True(again.Kind = ExchangeRemoteAdminOutcomeKind.Lapsed, again.ToString())
            End Using
        End Using
    End Function

    ' ================================================================================================ opt-in: a live host

    ''' <summary>
    ''' Specification 7.7: an opt-in run against an owner-run server. READ-ONLY operations only (status, policy.status, account.list): it
    ''' must never change a real server. Nothing about the host, the password or the certificate is printed, logged or put into an
    ''' assertion message; the first contact is accepted the way the owner's accepted-pin button does it, once, for this run.
    ''' </summary>
    <RemoteAdminLiveHostFact>
    Public Async Function The_opt_in_live_host_signs_in_and_reads_without_changing_anything() As Task
        Dim hostText As String = Environment.GetEnvironmentVariable(RemoteAdminLiveHostFactAttribute.HostVariable)
        Dim secretText As String = Environment.GetEnvironmentVariable(RemoteAdminLiveHostFactAttribute.PasswordVariable)
        Dim host As String = Nothing
        Dim port As Integer = 0
        Assert.True(ParseHostAndPort(hostText, host, port), "the configured live host is not a host or host:port")

        Dim lines As New ConcurrentQueue(Of String)()
        Using session As ExchangeRemoteAdminSession = NewSession(ExchangeAdminHandshake.Instance, lines)
            Dim attempt As ExchangeRemoteAdminSignIn = Await session.SignInAsync(host, port, "", "", PasswordOf(secretText), CancellationToken.None)
            If attempt.Kind = ExchangeRemoteAdminSignInKind.NeedsFirstContact Then
                attempt = Await session.SignInAsync(host, port, attempt.Fingerprint, attempt.KeyPin, PasswordOf(secretText), CancellationToken.None)
            End If
            Assert.True(attempt.Kind = ExchangeRemoteAdminSignInKind.SignedIn, "the live host did not sign in: " & attempt.ToString())

            Dim ran As New HashSet(Of String)(StringComparer.Ordinal)
            Dim o As ExchangeRemoteAdminOutcome = Await CallOp(session, ran, ExchangeAdminWire.OpStatus, Function(t, i) ExchangeAdminWire.Status(t, i))
            AssertDone(ExchangeAdminWire.OpStatus, o)
            Assert.Equal(JsonValueKind.Object, o.Data.ValueKind)
            o = Await CallOp(session, ran, ExchangeAdminWire.OpPolicyStatus, Function(t, i) ExchangeAdminWire.PolicyStatus(t, i))
            AssertDone(ExchangeAdminWire.OpPolicyStatus, o)
            o = Await CallOp(session, ran, ExchangeAdminWire.OpAccountList, Function(t, i) ExchangeAdminWire.AccountList(t, i))
            AssertDone(ExchangeAdminWire.OpAccountList, o)
            Assert.Equal(JsonValueKind.Array, o.Data.ValueKind)
            Await session.EndAsync()
        End Using

        Dim text As String = String.Join(vbLf, lines)
        Assert.False(text.Contains(secretText, StringComparison.Ordinal), "the password reached the session log")
        Assert.False(text.Contains(host, StringComparison.OrdinalIgnoreCase), "the host reached the session log")
    End Function

    ' ================================================================================================ helpers

    Private Shared Function NewSession(handshake As IExchangeAdminHandshake, lines As ConcurrentQueue(Of String)) As ExchangeRemoteAdminSession
        Return New ExchangeRemoteAdminSession(handshake,
                                              Function() Environment.TickCount64,
                                              Function(span As TimeSpan, token As CancellationToken) Task.Delay(span, token),
                                              Sub(line As String) lines.Enqueue(line))
    End Function

    ''' <summary>The password source of the step that must NOT read it: first contact is decided before any password is asked.</summary>
    Private Shared Function NeverAsked() As Func(Of Char())
        Return Function() As Char()
                   Throw New InvalidOperationException("the password was asked before the trust decision accepted")
               End Function
    End Function

    ''' <summary>A fresh array on every call: the session clears the one it is handed.</summary>
    Private Shared Function PasswordOf(text As String) As Func(Of Char())
        Return Function() text.ToCharArray()
    End Function

    Private Shared Async Function CallOp(session As ExchangeRemoteAdminSession, ran As HashSet(Of String), op As String,
                                         build As Func(Of String, Long, ExchangeEnvelope)) As Task(Of ExchangeRemoteAdminOutcome)
        Dim outcome As ExchangeRemoteAdminOutcome = Await session.CallAsync(op, build, CancellationToken.None)
        ran.Add(op)
        Return outcome
    End Function

    Private Shared Sub AssertDone(op As String, outcome As ExchangeRemoteAdminOutcome)
        Assert.True(outcome.Kind = ExchangeRemoteAdminOutcomeKind.Done, op & " should be Done but came to " & outcome.ToString() & " (" & outcome.Detail & ")")
    End Sub

    Private Shared Function HasMember(el As JsonElement, name As String) As Boolean
        Dim v As JsonElement
        Return el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, v)
    End Function

    Private Shared Function Txt(el As JsonElement, name As String) As String
        Dim v As JsonElement
        If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.String Then Return If(v.GetString(), "")
        Return ""
    End Function

    ''' <summary>The integer member, or -1 when it is absent or not an integer.</summary>
    Private Shared Function Num(el As JsonElement, name As String) As Long
        Dim v As JsonElement
        Dim n As Long
        If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.Number AndAlso v.TryGetInt64(n) Then Return n
        Return -1
    End Function

    ''' <summary>True only for a member that is the JSON value true.</summary>
    Private Shared Function Truth(el As JsonElement, name As String) As Boolean
        Dim v As JsonElement
        Return el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, v) AndAlso v.ValueKind = JsonValueKind.True
    End Function

    ''' <summary>The first element of an array whose string member <paramref name="key"/> equals <paramref name="value"/>.</summary>
    Private Shared Function FindBy(list As JsonElement, key As String, value As String, ByRef found As JsonElement) As Boolean
        If list.ValueKind <> JsonValueKind.Array Then Return False
        For Each entry As JsonElement In list.EnumerateArray()
            If String.Equals(Txt(entry, key), value, StringComparison.Ordinal) Then
                found = entry
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>"host", "host:port" or "[v6]:port"; the port defaults to the server's own default.</summary>
    Private Shared Function ParseHostAndPort(text As String, ByRef host As String, ByRef port As Integer) As Boolean
        host = Nothing
        port = 22022
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim t As String = text.Trim()
        Dim colon As Integer = t.LastIndexOf(":"c)
        If t.StartsWith("[", StringComparison.Ordinal) Then
            Dim close As Integer = t.IndexOf("]"c)
            If close < 0 Then Return False
            host = t.Substring(1, close - 1)
            If close + 1 < t.Length Then
                If t(close + 1) <> ":"c Then Return False
                If Not Integer.TryParse(t.Substring(close + 2), NumberStyles.None, CultureInfo.InvariantCulture, port) Then Return False
            End If
        ElseIf colon > 0 AndAlso t.IndexOf(":"c) = colon Then
            host = t.Substring(0, colon)
            If Not Integer.TryParse(t.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, port) Then Return False
        Else
            host = t
        End If
        Return host.Length > 0 AndAlso port >= 1 AndAlso port <= 65535
    End Function

    ''' <summary>The proof line goes to stdout and to a file that no test-log pruning touches (the runner keeps the newest 25 .log files).</summary>
    Private Shared Sub WriteProofLine(line As String)
        Console.WriteLine(line)
        Try
            Dim dir As String = Path.Combine(Path.GetTempPath(), "fms-tests")
            Directory.CreateDirectory(dir)
            File.AppendAllText(Path.Combine(dir, ProofFileName), DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z' ", CultureInfo.InvariantCulture) & line & Environment.NewLine)
        Catch ex As IOException
            ' the console line stands; a locked proof file is not a failure of the test
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub

    ' ================================================================================================ the server rig

    ''' <summary>One scratch data directory, one copy of the binary and at most one running <c>serve</c> process. Disposing kills the
    ''' process and deletes the folder.</summary>
    Private NotInheritable Class ServerRig
        Implements IDisposable

        Private Const CliTimeoutMs As Integer = 20000
        Private Const StartTimeoutMs As Integer = 15000
        Private Const StopTimeoutMs As Integer = 15000

        Private ReadOnly _root As String
        Private ReadOnly _dataDir As String
        Private ReadOnly _exePath As String
        Private ReadOnly _serverLines As New ConcurrentQueue(Of String)()
        Private ReadOnly _cliLines As New ConcurrentQueue(Of String)()
        Private _server As Process
        Private _fingerprint As String = ""
        Private _versionLine As String = ""

        ''' <summary>The loopback port the server is configured to listen on.</summary>
        Public ReadOnly Port As Integer

        Private Sub New(source As String)
            _root = Path.Combine(Path.GetTempPath(), "fms-remote-admin-it-" & Guid.NewGuid().ToString("N"))
            _dataDir = Path.Combine(_root, "data")
            Directory.CreateDirectory(_dataDir)
            _exePath = Path.Combine(_root, ExchangeProcess.ExeName)
            File.Copy(source, _exePath)    ' a copy, so nothing locks the build output
            Port = FreeLoopbackPort()
        End Sub

        ''' <summary>The rig for the binary found on this machine. With none it FAILS (a skipped test never gets here; a run that
        ''' required a binary has no Skip, so this is where it is told).</summary>
        Public Shared Function Create() As ServerRig
            Dim source As String = RealBinaryFactAttribute.Locate()
            Assert.True(source IsNot Nothing,
                        RealBinaryFactAttribute.RequireVariable & "=1 or an unskipped run, but there is no " & ExchangeProcess.ExeName &
                        " to drive: build exchange-server (exchange-server\build.ps1), stage bin\Release\exchange, or name an exe in " & RealBinaryFactAttribute.OverrideVariable)
            Return New ServerRig(source)
        End Function

        Public ReadOnly Property Fingerprint As String
            Get
                Return _fingerprint
            End Get
        End Property

        ''' <summary>The first line the server printed on stdout (<c>fms-exchange-server &lt;version&gt;</c>).</summary>
        Public ReadOnly Property VersionLine As String
            Get
                Return _versionLine
            End Get
        End Property

        Private Shared Function FreeLoopbackPort() As Integer
            Dim l As New TcpListener(IPAddress.Loopback, 0)
            l.Start()
            Try
                Return CType(l.LocalEndpoint, IPEndPoint).Port
            Finally
                l.Stop()
            End Try
        End Function

        ''' <summary>The proof line of specification 7.5.</summary>
        Public Function ProofLine() As String
            Dim hash As String
            Using fs As New FileStream(_exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant()
            End Using
            Return "binary-used sha256=" & hash & " bytes=" & New FileInfo(_exePath).Length.ToString(CultureInfo.InvariantCulture) & " version-line=" & _versionLine
        End Function

        ''' <summary>The state the specification's test sets up: an account (alice, with a generated password nobody reads), the
        ''' operator's administration on with a known password (read from stdin, never an argument), the optional lifetime, and the
        ''' listen address on LOOPBACK only. Nothing is started here.</summary>
        Public Sub Configure(adminPassword As String, lifetimeMinutes As Integer)
            Assert.Equal(0, RunCli({"account", "add", "alice", "--generate"}, Nothing))
            Dim adminOn As New List(Of String) From {"admin", "on"}
            If lifetimeMinutes > 0 Then
                adminOn.Add("--lifetime")
                adminOn.Add(lifetimeMinutes.ToString(CultureInfo.InvariantCulture))
            End If
            Assert.Equal(0, RunCli(adminOn.ToArray(), adminPassword))
            Assert.Equal(0, RunCli({"set-listen", LoopbackHost & ":" & Port.ToString(CultureInfo.InvariantCulture)}, Nothing))
        End Sub

        ''' <summary>One CLI run of the binary against the scratch data directory; returns the exit code (-1 on a timeout).</summary>
        Public Function RunCli(args As String(), stdin As String) As Integer
            Dim psi As ProcessStartInfo = NewStartInfo()
            For Each a As String In args
                psi.ArgumentList.Add(a)
            Next
            Using p As Process = Process.Start(psi)
                Dim outTask As Task(Of String) = p.StandardOutput.ReadToEndAsync()
                Dim errTask As Task(Of String) = p.StandardError.ReadToEndAsync()
                Try
                    If stdin IsNot Nothing Then p.StandardInput.Write(stdin & vbLf)
                    p.StandardInput.Close()
                Catch ex As IOException
                    ' the child decided from its arguments alone and left
                End Try
                If Not p.WaitForExit(CliTimeoutMs) Then
                    Try : p.Kill(True) : Catch : End Try
                    Return -1
                End If
                p.WaitForExit()
                For Each text As String In {outTask.Result, errTask.Result}
                    For Each line As String In text.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        _cliLines.Enqueue(line)
                    Next
                Next
                Return p.ExitCode
            End Using
        End Function

        Private Function NewStartInfo() As ProcessStartInfo
            Dim psi As New ProcessStartInfo(_exePath) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .RedirectStandardInput = True,
                .StandardOutputEncoding = Encoding.UTF8,
                .StandardErrorEncoding = Encoding.UTF8,
                .StandardInputEncoding = New UTF8Encoding(False),
                .WorkingDirectory = _root}
            psi.Environment(DataDirVariable) = _dataDir
            Return psi
        End Function

        ''' <summary>Starts <c>serve</c> and waits (at most 15 s) for its <c>tls fingerprint</c> line, the last it prints on starting.
        ''' Kills it and fails when it listens anywhere but loopback.</summary>
        Public Async Function StartAsync() As Task
            Assert.True(_server Is Nothing OrElse _server.HasExited, "a server is running already")
            Dim psi As ProcessStartInfo = NewStartInfo()
            psi.ArgumentList.Add("serve")
            _fingerprint = ""
            _versionLine = ""
            Dim ready As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim p As Process = Process.Start(psi)
            _server = p
            AddHandler p.OutputDataReceived, Sub(sender As Object, e As DataReceivedEventArgs) OnServerLine(e.Data, True, ready)
            AddHandler p.ErrorDataReceived, Sub(sender As Object, e As DataReceivedEventArgs) OnServerLine(e.Data, False, ready)
            p.EnableRaisingEvents = True
            AddHandler p.Exited, Sub(sender As Object, e As EventArgs) ready.TrySetResult(False)
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            Try
                p.StandardInput.Close()
            Catch ex As IOException
            End Try

            Dim finished As Task = Await Task.WhenAny(ready.Task, Task.Delay(StartTimeoutMs))
            Dim up As Boolean = finished Is ready.Task AndAlso ready.Task.Result
            Assert.True(up, "the server did not print its fingerprint within " & (StartTimeoutMs \ 1000).ToString(CultureInfo.InvariantCulture) & " s; it printed: " & ServerText())
            Dim expected As String = "listening on " & LoopbackHost & ":" & Port.ToString(CultureInfo.InvariantCulture)
            If Not ServerText().Contains(expected, StringComparison.Ordinal) Then
                Try : p.Kill(True) : Catch : End Try
                Assert.Fail("the server is not listening on loopback only (expected the line '" & expected & "'); it printed: " & ServerText())
            End If
        End Function

        Private Sub OnServerLine(data As String, isStdout As Boolean, ready As TaskCompletionSource(Of Boolean))
            If data Is Nothing Then Return
            _serverLines.Enqueue(data)
            If Not isStdout Then Return
            If _versionLine.Length = 0 Then _versionLine = data
            If data.StartsWith("tls fingerprint ", StringComparison.Ordinal) Then
                _fingerprint = data.Substring("tls fingerprint ".Length).Trim()
                ready.TrySetResult(True)
            End If
        End Sub

        Private Function ServerText() As String
            Return String.Join(vbLf, _serverLines)
        End Function

        ''' <summary>The graceful stop the Share Manager uses (<c>stop</c>): exit 0, then the serve process exits 0 by itself.</summary>
        Public Sub StopAndAssert()
            If _server Is Nothing OrElse _server.HasExited Then Return
            Dim stopCode As Integer = RunCli({"stop"}, Nothing)
            Assert.Equal(0, stopCode)
            Assert.True(_server.WaitForExit(StopTimeoutMs), "the server did not exit within " & (StopTimeoutMs \ 1000).ToString(CultureInfo.InvariantCulture) & " s of stop")
            _server.WaitForExit()
            Assert.Equal(0, _server.ExitCode)
        End Sub

        ''' <summary>Every secret that appears in a data-directory file or in anything the processes printed, as "label in place" (never
        ''' the value). Files are read as bytes (Latin-1), so a secret inside a binary file is found as well.</summary>
        Public Function FindSecrets(secrets As IDictionary(Of String, String), ByRef scannedFiles As Integer) As List(Of String)
            Dim hits As New List(Of String)()
            Dim printed As String = ServerText() & vbLf & String.Join(vbLf, _cliLines)
            For Each pair As KeyValuePair(Of String, String) In secrets
                If printed.Contains(pair.Value, StringComparison.Ordinal) Then hits.Add(pair.Key & " in what the server and its CLI printed")
            Next
            scannedFiles = 0
            For Each f As String In Directory.GetFiles(_dataDir, "*", SearchOption.AllDirectories)
                Dim bytes As Byte()
                Try
                    Using fs As New FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                        Using ms As New MemoryStream()
                            fs.CopyTo(ms)
                            bytes = ms.ToArray()
                        End Using
                    End Using
                Catch ex As IOException
                    Continue For    ' a lock file of a running server
                End Try
                scannedFiles += 1
                Dim text As String = Encoding.Latin1.GetString(bytes)
                For Each pair As KeyValuePair(Of String, String) In secrets
                    If text.Contains(pair.Value, StringComparison.Ordinal) Then hits.Add(pair.Key & " in " & Path.GetRelativePath(_dataDir, f))
                Next
            Next
            Return hits
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                If _server IsNot Nothing AndAlso Not _server.HasExited Then
                    _server.Kill(True)
                    _server.WaitForExit(5000)
                End If
            Catch ex As Exception
                ' already gone
            End Try
            Try
                If _server IsNot Nothing Then _server.Dispose()
            Catch ex As Exception
            End Try
            ExchangeTestSupport.DeleteQuietly(_root)
            If Directory.Exists(_root) Then
                Thread.Sleep(300)    ' one bounded retry: the OS may still hold the exe or the lock file an instant after the exit
                ExchangeTestSupport.DeleteQuietly(_root)
            End If
        End Sub
    End Class

    ' ================================================================================================ the recording handshake

    ''' <summary>The real handshake (<see cref="ExchangeAdminHandshake.Instance"/>, real TLS) with one addition: every byte the server
    ''' sends on a pinned connection is also kept, so the test knows the session token - which only the server and the session ever
    ''' see - and can look for it in files and logs.</summary>
    Private NotInheritable Class RecordingHandshake
        Implements IExchangeAdminHandshake

        Private ReadOnly _received As New MemoryStream()
        Private Shared ReadOnly TokenPattern As New Regex("""token""\s*:\s*""([A-Za-z0-9_\-]+)""", RegexOptions.CultureInvariant)

        Public Async Function ConnectPinnedAsync(host As String, port As Integer, leafPin As String, keyPin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeAdminHandshake.ConnectPinnedAsync
            Dim conn As ExchangeTlsConnection = Await ExchangeAdminHandshake.Instance.ConnectPinnedAsync(host, port, leafPin, keyPin, ct)
            Return New ExchangeTlsConnection(New TeeStream(conn.Stream, _received), conn.Fingerprint, conn.Verdict, conn.KeyPin)
        End Function

        Public Function ProbeAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of ExchangeCertificateInfo) Implements IExchangeAdminHandshake.ProbeAsync
            Return ExchangeAdminHandshake.Instance.ProbeAsync(host, port, ct)
        End Function

        ''' <summary>The token of the first <c>admin-auth-ok</c> seen, or "".</summary>
        Public Function SeenToken() As String
            Dim bytes As Byte()
            SyncLock _received
                bytes = _received.ToArray()
            End SyncLock
            Dim m As Match = TokenPattern.Match(Encoding.UTF8.GetString(bytes))
            Return If(m.Success, m.Groups(1).Value, "")
        End Function
    End Class

    ''' <summary>A pass-through stream that copies what is read from the inner stream into a shared buffer.</summary>
    Private NotInheritable Class TeeStream
        Inherits Stream

        Private ReadOnly _inner As Stream
        Private ReadOnly _received As MemoryStream

        Public Sub New(inner As Stream, received As MemoryStream)
            _inner = inner
            _received = received
        End Sub

        Private Sub Keep(buffer As Byte(), offset As Integer, count As Integer)
            If count <= 0 Then Return
            SyncLock _received
                _received.Write(buffer, offset, count)
            End SyncLock
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return _inner.CanRead
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return _inner.CanWrite
            End Get
        End Property

        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim n As Integer = _inner.Read(buffer, offset, count)
            Keep(buffer, offset, n)
            Return n
        End Function

        Public Overrides Async Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
            Dim n As Integer = Await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(False)
            Keep(buffer, offset, n)
            Return n
        End Function

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            _inner.Write(buffer, offset, count)
        End Sub

        Public Overrides Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
            Return _inner.WriteAsync(buffer, offset, count, cancellationToken)
        End Function

        Public Overrides Sub Flush()
            _inner.Flush()
        End Sub

        Public Overrides Function FlushAsync(cancellationToken As CancellationToken) As Task
            Return _inner.FlushAsync(cancellationToken)
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _inner.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

End Class
