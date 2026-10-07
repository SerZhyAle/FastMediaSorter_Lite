Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 148, the dev-tree half: the producer's settings rules and the DORMANT relay export. What these
''' hold, in the order of the ticket's acceptance list:
'''   1. mode off (or no exchange argument) leaves an export byte-identical - contract section 8.5;
'''   2. a loopback address is never published, a malformed share id is never advertised;
'''   3. the relay paths come after every direct one and never into the "LAN only" export;
'''   4. no password has a place in the settings class, and a share id never survives the log redaction;
'''   5. nothing on a product path passes the exchange argument before rung 2.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeProducerTests

    Private Const GoodSid As String = "AAAAAAAAAAAAAAAAAAAAAA"

    Private Shared Function RunningStatus() As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2222,
            .Username = "fms",
            .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__148", .readOnly = False}
            },
            .Reachability = New WorkerReachability With {
                .LanAddress = "192.168.1.50", .ExternalHost = "203.0.113.9", .ExternalPort = 2222
            }
        }
    End Function

    Private Shared Function FixedStamp() As ShareConfigStamp
        Return New ShareConfigStamp With {.ResourceName = "Test PC", .CreatedAtUtc = New Date(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)}
    End Function

    Private Shared Function Export(Optional host As String = "relay.example.org", Optional port As Integer = 22022,
                                   Optional sid As String = GoodSid, Optional relayPort As Integer = 40007) As ExchangeExport
        Return New ExchangeExport With {.ClientHost = host, .ClientPort = port, .ShareId = sid, .RelayPort = relayPort}
    End Function

    Private Shared Function Baseline() As String
        Return ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp()).ConfigJson
    End Function

    ' ---- 1 + 2: dormant and safe -------------------------------------------------------------

    <Fact>
    Public Sub No_exchange_argument_leaves_the_export_untouched()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Nothing)
        Assert.Equal(Baseline(), res.ConfigJson)
        Assert.False(res.HasRelay)
    End Sub

    <Fact>
    Public Sub An_export_that_may_not_carry_the_relay_is_byte_identical_to_none()
        Dim expected As String = Baseline()
        Dim loopbacks As String() = {"127.0.0.1", "localhost", "::1", "0.0.0.0", "::", "[::1]", "127.9.9.9", "relay.localhost"}
        For Each h As String In loopbacks
            Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export(host:=h, relayPort:=0, sid:="bad"))
            Assert.True(expected = res.ConfigJson, "Loopback host '" & h & "' changed the export.")
            Assert.False(res.HasRelay)
            ' even WITH a valid id and port, a loopback host is never published
            Dim res2 = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export(host:=h))
            Assert.True(expected = res2.ConfigJson, "Loopback host '" & h & "' was published.")
            Assert.DoesNotContain("rendezvousTunnel", res2.ConfigJson)
        Next
    End Sub

    <Fact>
    Public Sub The_LAN_only_export_never_carries_the_relay()
        Dim lanOnly As String = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=False, stamp:=FixedStamp()).ConfigJson
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=False, stamp:=FixedStamp(), exchange:=Export())
        Assert.Equal(lanOnly, res.ConfigJson)
        Assert.False(res.HasRelay)
        Assert.DoesNotContain("relay.example.org", res.ConfigJson)
    End Sub

    <Fact>
    Public Sub A_malformed_share_id_publishes_no_tunnel_but_the_relay_port_still_goes()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export(sid:="not-a-share-id"))
        Assert.True(res.HasRelay)
        Assert.DoesNotContain("rendezvousTunnel", res.ConfigJson)
        Assert.DoesNotContain("not-a-share-id", res.ConfigJson)
    End Sub

    ' ---- 3: order and shape -------------------------------------------------------------------

    <Fact>
    Public Sub Relay_paths_come_after_every_direct_path_in_the_agreed_order()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export())
        Assert.True(res.HasRelay)
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim kinds = doc.RootElement.GetProperty("accessPaths").EnumerateArray().Select(Function(p) p.GetProperty("kind").GetString()).ToList()
            Assert.Equal(New List(Of String) From {"lan", "portforward", "portforward", "rendezvousTunnel"}, kinds)

            Dim paths = doc.RootElement.GetProperty("accessPaths")
            Assert.Equal("203.0.113.9", paths(1).GetProperty("host").GetString())     ' the direct forward first
            Assert.Equal("relay.example.org", paths(2).GetProperty("host").GetString())
            Assert.Equal(40007, paths(2).GetProperty("port").GetInt32())              ' the third-party relay port
            Dim tunnel = paths(3)
            Assert.Equal("relay.example.org", tunnel.GetProperty("host").GetString())
            Assert.Equal(22022, tunnel.GetProperty("port").GetInt32())
            Assert.Equal(GoodSid, tunnel.GetProperty("shareId").GetString())
            ' one identity on every transport: the fingerprint is the same single top-level one
            Assert.Equal("SHA256:abc123", doc.RootElement.GetProperty("hostKeyFingerprintSha256").GetString())
        End Using
    End Sub

    <Fact>
    Public Sub The_share_id_appears_once_and_only_in_the_tunnel_path()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export())
        Dim first As Integer = res.ConfigJson.IndexOf(GoodSid, StringComparison.Ordinal)
        Assert.True(first >= 0)
        Assert.Equal(-1, res.ConfigJson.IndexOf(GoodSid, first + 1, StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub No_relay_port_means_only_the_tunnel_path()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export(relayPort:=0))
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim kinds = doc.RootElement.GetProperty("accessPaths").EnumerateArray().Select(Function(p) p.GetProperty("kind").GetString()).ToList()
            Assert.Equal(New List(Of String) From {"lan", "portforward", "rendezvousTunnel"}, kinds)
        End Using
    End Sub

    <Fact>
    Public Sub An_IPv6_client_host_is_written_without_brackets()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export(host:="2001:db8::7"))
        Using doc = JsonDocument.Parse(res.ConfigJson)
            Dim last = doc.RootElement.GetProperty("accessPaths").EnumerateArray().Last()
            Assert.Equal("2001:db8::7", last.GetProperty("host").GetString())
        End Using
    End Sub

    ' ---- 4: secrets ---------------------------------------------------------------------------

    <Fact>
    Public Sub Log_redaction_blanks_the_share_id_like_the_password()
        Dim res = ShareConfigBuilder.Build(RunningStatus(), includeExternal:=True, stamp:=FixedStamp(), exchange:=Export())
        Dim redacted As String = ShareConfigBuilder.RedactSecrets(res.ConfigJson)
        Assert.DoesNotContain(GoodSid, redacted)
        Assert.DoesNotContain("secret", redacted)
        Assert.Contains("relay.example.org", redacted)   ' the address itself is not a secret
    End Sub

    <Fact>
    Public Sub Settings_have_no_place_to_keep_a_password()
        Dim members = GetType(ExchangeProducerSettings).GetMembers(BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Instance Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
        Dim offenders = members.Where(Function(m) m.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                                  m.Name.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                                  m.Name.IndexOf("shareid", StringComparison.OrdinalIgnoreCase) >= 0).Select(Function(m) m.Name).ToList()
        Assert.True(offenders.Count = 0, "The registry-backed settings class must not hold a secret: " & String.Join(", ", offenders))
    End Sub

    <Fact>
    Public Sub Only_the_mode_is_on_the_log_package_allow_list_never_an_endpoint()
        Dim lp As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "Support", "LogPackage.vb"))
        Assert.Contains("""Share_ExchangeMode""", lp)
        Assert.DoesNotContain("Share_ExchangeRegistrationEndpoint", lp)
        Assert.DoesNotContain("Share_ExchangeClientEndpoint", lp)
    End Sub

    <Theory>
    <InlineData("AAAAAAAAAAAAAAAAAAAAAA", True)>
    <InlineData("AAAAAAAAAAAAAAAAAAAAAQ", True)>
    <InlineData("AAAAAAAAAAAAAAAAAAAAAg", True)>
    <InlineData("AAAAAAAAAAAAAAAAAAAAAw", True)>
    <InlineData("abcDEF123-_abcDEF123-A", True)>
    <InlineData("AAAAAAAAAAAAAAAAAAAAAB", False)>      ' last character carries a non-zero padding bit: the Go side decodes Strict
    <InlineData("AAAAAAAAAAAAAAAAAAAAA", False)>       ' 21
    <InlineData("AAAAAAAAAAAAAAAAAAAAAAA", False)>     ' 23
    <InlineData("AAAAAAAAAAAAAAAAAAAA+A", False)>      ' not base64url
    <InlineData("AAAAAAAAAAAAAAAAAAAAA=", False)>      ' padding is not part of the form
    <InlineData("", False)>
    Public Sub Share_id_form_matches_the_server(sid As String, expected As Boolean)
        Assert.Equal(expected, ExchangeShareIds.IsWellFormed(sid))
    End Sub

    <Fact>
    Public Sub The_log_name_is_eight_hex_characters_and_never_the_id()
        Dim n As String = ExchangeShareIds.LogName(GoodSid)
        Assert.Equal(8, n.Length)
        Assert.Matches("^[0-9a-f]{8}$", n)
        Assert.NotEqual(ExchangeShareIds.LogName(GoodSid), ExchangeShareIds.LogName("AAAAAAAAAAAAAAAAAAAAAQ"))
        ' the rule of 133 R-6, computed here independently: first 8 hex characters of SHA-256 of the id
        Dim expected As String = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GoodSid))).Substring(0, 8).ToLowerInvariant()
        Assert.Equal(expected, n)
    End Sub

    ' ---- the endpoint rules --------------------------------------------------------------------

    <Theory>
    <InlineData("relay.example.org:22022", "relay.example.org", 22022)>
    <InlineData("203.0.113.9:443", "203.0.113.9", 443)>
    <InlineData("[2001:db8::7]:22022", "2001:db8::7", 22022)>
    <InlineData("  host_1.lan:1  ", "host_1.lan", 1)>
    <InlineData("example.org:65535", "example.org", 65535)>
    Public Sub Endpoints_that_are_host_colon_port_parse(text As String, host As String, port As Integer)
        Dim ep As ExchangeEndpoint = Nothing
        Assert.True(ExchangeEndpointRules.TryParse(text, ep))
        Assert.Equal(host, ep.Host)
        Assert.Equal(port, ep.Port)
        ' and survive a round trip through the stored text
        Dim again As ExchangeEndpoint = Nothing
        Assert.True(ExchangeEndpointRules.TryParse(ep.ToString(), again))
        Assert.Equal(ep.Host, again.Host)
        Assert.Equal(ep.Port, again.Port)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("relay.example.org")>
    <InlineData("relay.example.org:")>
    <InlineData(":22022")>
    <InlineData("relay.example.org:0")>
    <InlineData("relay.example.org:65536")>
    <InlineData("relay.example.org:-1")>
    <InlineData("relay.example.org:22 022")>
    <InlineData("https://relay.example.org:22022")>
    <InlineData("relay.example.org:22022/path")>
    <InlineData("user@relay.example.org:22022")>
    <InlineData("2001:db8::7:22022")>
    <InlineData("[2001:db8::7]")>
    <InlineData("[relay.example.org]:22022")>
    <InlineData("[203.0.113.9]:22022")>
    <InlineData("999.1.1.1:22022")>
    <InlineData("-bad.example.org:22022")>
    <InlineData("bad..example.org:22022")>
    <InlineData("bad host:22022")>
    Public Sub Everything_else_is_malformed(text As String)
        Dim ep As ExchangeEndpoint = Nothing
        Assert.False(ExchangeEndpointRules.TryParse(text, ep), "'" & text & "' parsed")
        Assert.Equal(If(String.IsNullOrWhiteSpace(text), ExchangeEndpointVerdict.Empty, ExchangeEndpointVerdict.Malformed),
                     ExchangeEndpointRules.CheckClientEndpoint(text))
    End Sub

    <Fact>
    Public Sub The_client_verdict_separates_empty_loopback_and_valid()
        Assert.Equal(ExchangeEndpointVerdict.Empty, ExchangeEndpointRules.CheckClientEndpoint("   "))
        Assert.Equal(ExchangeEndpointVerdict.Loopback, ExchangeEndpointRules.CheckClientEndpoint("127.0.0.1:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Loopback, ExchangeEndpointRules.CheckClientEndpoint("localhost:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Loopback, ExchangeEndpointRules.CheckClientEndpoint("[::1]:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Loopback, ExchangeEndpointRules.CheckClientEndpoint("0.0.0.0:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Valid, ExchangeEndpointRules.CheckClientEndpoint("relay.example.org:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Valid, ExchangeEndpointRules.CheckClientEndpoint("192.168.1.5:22022"))   ' a LAN server is a valid client endpoint
    End Sub

    <Fact>
    Public Sub The_registration_endpoint_may_be_loopback_but_not_empty()
        Assert.Equal(ExchangeEndpointVerdict.Valid, ExchangeEndpointRules.CheckRegistrationEndpoint("127.0.0.1:22022"))
        Assert.Equal(ExchangeEndpointVerdict.Malformed, ExchangeEndpointRules.CheckRegistrationEndpoint(""))
    End Sub

    <Fact>
    Public Sub This_PC_registers_on_loopback_at_the_local_servers_port()
        Dim ep = ExchangeEndpointRules.ThisPcRegistrationEndpoint(23000)
        Assert.Equal("127.0.0.1", ep.Host)
        Assert.Equal(23000, ep.Port)
        Assert.Equal(ExchangeSettings.DefaultPort, ExchangeEndpointRules.ThisPcRegistrationEndpoint(0).Port)
        Assert.Equal(ExchangeSettings.DefaultPort, ExchangeEndpointRules.ThisPcRegistrationEndpoint(70000).Port)
    End Sub

    <Fact>
    Public Sub An_external_servers_password_meets_the_same_floor_as_the_local_one()
        Assert.Equal(ExchangePasswordPolicy.Verdict.TooShort, ExchangeEndpointRules.CheckServerPassword("Abcdefgh1"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.NoUpper, ExchangeEndpointRules.CheckServerPassword("abcdefghij1"))
        Assert.Equal(ExchangePasswordPolicy.Verdict.Accepted, ExchangeEndpointRules.CheckServerPassword("Abcdefghij1"))
    End Sub

    ' ---- the settings object -------------------------------------------------------------------

    <Fact>
    Public Sub A_damaged_stored_mode_reads_as_off()
        Assert.Equal(ExchangeMode.Off, ExchangeProducerSettings.ClampMode(-1))
        Assert.Equal(ExchangeMode.Off, ExchangeProducerSettings.ClampMode(3))
        Assert.Equal(ExchangeMode.Off, ExchangeProducerSettings.ClampMode(99))
        Assert.Equal(ExchangeMode.ThisPc, ExchangeProducerSettings.ClampMode(1))
        Assert.Equal(ExchangeMode.External, ExchangeProducerSettings.ClampMode(2))
    End Sub

    <Fact>
    Public Sub A_stored_endpoint_that_no_longer_parses_reads_as_empty()
        Assert.Equal("", ExchangeProducerSettings.SanitizedEndpoint("garbage"))
        Assert.Equal("", ExchangeProducerSettings.SanitizedEndpoint("https://x.example:1"))
        Assert.Equal("relay.example.org:22022", ExchangeProducerSettings.SanitizedEndpoint("  relay.example.org:22022 "))
        Assert.Equal("[2001:db8::7]:22022", ExchangeProducerSettings.SanitizedEndpoint("[2001:db8::7]:22022"))
    End Sub

    <Fact>
    Public Sub Nothing_is_publishable_with_the_mode_off_or_a_loopback_or_an_empty_endpoint()
        Dim s As New ExchangeProducerSettings With {.Mode = ExchangeMode.Off, .ClientEndpoint = "relay.example.org:22022"}
        Assert.Null(s.PublishableClientEndpoint())
        s.Mode = ExchangeMode.External
        s.ClientEndpoint = ""
        Assert.Null(s.PublishableClientEndpoint())
        s.ClientEndpoint = "127.0.0.1:22022"
        Assert.Null(s.PublishableClientEndpoint())
        s.ClientEndpoint = "relay.example.org:22022"
        Dim ep = s.PublishableClientEndpoint()
        Assert.True(ep.HasValue)
        Assert.Equal("relay.example.org", ep.Value.Host)
        s.Mode = ExchangeMode.ThisPc
        Assert.True(s.PublishableClientEndpoint().HasValue)
    End Sub

    ' ---- 5: one door to the export ---------------------------------------------------------------
    ' Ticket 148 wired the export (it was dormant until the IPC existed). The rule that remains: exactly ONE product
    ' call passes the argument, and what it passes is built by ExchangeProducerRules.ExportFor - which returns
    ' Nothing for mode off, a stale worker or a worker that reports no registration, so the export is then
    ' byte-identical to today's (contract section 8.5). Nothing else may build an ExchangeExport by hand.

    <Fact>
    Public Sub One_product_call_passes_the_exchange_argument_and_it_goes_through_the_rules()
        Dim passers As New List(Of String)()
        Dim builders As New List(Of String)()
        For Each f As String In Directory.EnumerateFiles(ExchangeTestSupport.CompanionDir(), "*.vb", SearchOption.AllDirectories)
            If f.Contains(Path.DirectorySeparatorChar & "obj" & Path.DirectorySeparatorChar) Then Continue For
            Dim name As String = Path.GetFileName(f)
            If name.Equals("ShareConfigBuilder.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim text As String = File.ReadAllText(f)
            If text.Contains("exchange:=") Then passers.Add(name)
            If text.Contains("New ExchangeExport") Then builders.Add(name)
        Next
        Assert.Equal(New String() {"PackageWizardForm.vb"}, passers.ToArray())
        Assert.Equal(New String() {"ExchangeProducer.vb"}, builders.ToArray())
        Dim wizard As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.CompanionDir(), "Forms", "PackageWizardForm.vb"))
        Assert.Contains("ExchangeProducerRules.ExportFor(", wizard)
    End Sub
End Class
