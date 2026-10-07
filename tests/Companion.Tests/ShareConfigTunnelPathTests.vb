Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' FMSCFG 2.4 rule 7 from the PRODUCER's side (CONFIG_FORMAT.md "Amendment 2026-10-07 (2.4)", B):
''' a <c>rendezvousTunnel</c> path always carries a valid <c>host</c> and <c>port</c> and is written
''' LAST, because a shipped reader (FastMediaSorter Android) validates every path whatever its kind
''' and refuses the whole file when one is invalid, then tries each path as an SSH endpoint.
'''
''' This product publishes a tunnel path only when the exchange producer is switched on (off by default) AND the
''' worker reports a registered share - and no worker reports one yet (tickets 148, 170, 171 own the publication).
''' <see cref="ShareConfigBuilder.Build"/> takes the exchange argument as an optional one, so what these tests hold is
''' the property that matters before that day:
''' whatever the input, the exporter either writes a well-formed, last tunnel path or writes none.
''' The owner of the exchange producer has its own tests of the dormant export
''' (<c>ExchangeProducerTests</c>); this file is the FMSCFG reading of the same code.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ShareConfigTunnelPathTests

    Private Const GoodSid As String = "AAAAAAAAAAAAAAAAAAAAAA"
    Private Const TunnelKind As String = "rendezvousTunnel"
    Private Shared ReadOnly DirectKinds As String() = {"lan", "ipv6", "portforward"}

    Private Shared Function Running() As WorkerStatus
        Return New WorkerStatus With {
            .Running = True,
            .ListenPort = 2222,
            .Username = "fms",
            .Password = "secret",
            .Fingerprint = "SHA256:abc123",
            .Roots = New List(Of ShareFolder) From {
                New ShareFolder With {.name = "Photos", .hostPath = "C:\__fms_test__2_4", .readOnly = False}
            },
            .Reachability = New WorkerReachability With {
                .LanAddress = "192.168.1.50", .Ipv6Address = "2001:db8::1", .ExternalHost = "203.0.113.9", .ExternalPort = 2222
            }
        }
    End Function

    Private Shared Function Stamp() As ShareConfigStamp
        Return New ShareConfigStamp With {.ResourceName = "Test PC", .CreatedAtUtc = New Date(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc)}
    End Function

    Private Shared Function Export(host As String, port As Integer, sid As String, relayPort As Integer) As ExchangeExport
        Return New ExchangeExport With {.ClientHost = host, .ClientPort = port, .ShareId = sid, .RelayPort = relayPort}
    End Function

    ''' <summary>The validation a shipped reader applies to EVERY path (the Android reader, and the
    ''' viewer's own once it knows a kind): a usable host token and a TCP port. Deliberately the strict
    ''' reading - no whitespace or control characters, no slash, backslash or at-sign in the host.</summary>
    Private Shared Function IsValidEndpoint(path As JsonElement) As Boolean
        Dim host As JsonElement
        Dim port As JsonElement
        If Not path.TryGetProperty("host", host) OrElse host.ValueKind <> JsonValueKind.String Then Return False
        If Not path.TryGetProperty("port", port) OrElse port.ValueKind <> JsonValueKind.Number Then Return False
        Dim text As String = host.GetString()
        If String.IsNullOrWhiteSpace(text) OrElse text.Trim().Length <> text.Length Then Return False
        For Each ch As Char In text
            If AscW(ch) < &H21 OrElse AscW(ch) = &H7F OrElse ch = "/"c OrElse ch = "\"c OrElse ch = "@"c Then Return False
        Next
        Dim number As Integer
        Return port.TryGetInt32(number) AndAlso number >= 1 AndAlso number <= 65535
    End Function

    Private Shared Function PathsOf(json As String) As List(Of JsonElement)
        Using doc As JsonDocument = JsonDocument.Parse(json)
            Return doc.RootElement.GetProperty("accessPaths").EnumerateArray().Select(Function(e) e.Clone()).ToList()
        End Using
    End Function

    Private Shared Function Kind(path As JsonElement) As String
        Return path.GetProperty("kind").GetString()
    End Function

    Public Shared Function Inputs() As IEnumerable(Of Object())
        ' host, port, share id, relay port, expected to publish a tunnel path
        Return New List(Of Object()) From {
            New Object() {"relay.example.org", 22022, GoodSid, 40007, True},
            New Object() {"relay.example.org", 22022, GoodSid, 0, True},
            New Object() {"198.51.100.20", 7443, GoodSid, 0, True},
            New Object() {"2001:db8::7", 7443, GoodSid, 0, True},
            New Object() {"[2001:db8::7]", 7443, GoodSid, 0, False},   ' a bracketed host is a caller error: never published
            New Object() {"relay.example.org", 1, GoodSid, 0, True},
            New Object() {"relay.example.org", 65535, GoodSid, 0, True},
            New Object() {"", 22022, GoodSid, 40007, False},
            New Object() {"   ", 22022, GoodSid, 40007, False},
            New Object() {"re lay.example.org", 22022, GoodSid, 40007, False},
            New Object() {"relay.example.org/path", 22022, GoodSid, 40007, False},
            New Object() {"user@relay.example.org", 22022, GoodSid, 40007, False},
            New Object() {"127.0.0.1", 22022, GoodSid, 40007, False},
            New Object() {"localhost", 22022, GoodSid, 40007, False},
            New Object() {"::1", 22022, GoodSid, 40007, False},
            New Object() {"0.0.0.0", 22022, GoodSid, 40007, False},
            New Object() {"relay.example.org", 0, GoodSid, 40007, False},
            New Object() {"relay.example.org", -1, GoodSid, 40007, False},
            New Object() {"relay.example.org", 65536, GoodSid, 40007, False},
            New Object() {"relay.example.org", 22022, "", 0, False},
            New Object() {"relay.example.org", 22022, Nothing, 0, False},
            New Object() {"relay.example.org", 22022, "short", 0, False},
            New Object() {"relay.example.org", 22022, "AAAAAAAAAAAAAAAAAAAAAAA", 0, False},
            New Object() {"relay.example.org", 22022, "AAAAAAAAAAAAAAAAAAAA+A", 0, False}
        }
    End Function

    ''' <summary>Rule 7, both duties, over every input: when a tunnel path is written it has a valid
    ''' endpoint and nothing follows it; when the input cannot make a valid one, none is written.</summary>
    <Theory>
    <MemberData(NameOf(Inputs))>
    Public Sub ATunnelPath_IsEitherWellFormedAndLast_OrAbsent(host As String, port As Integer, sid As String, relayPort As Integer, publishes As Boolean)
        Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(), includeExternal:=True, stamp:=Stamp(), exchange:=Export(host, port, sid, relayPort))
        Assert.NotNull(res)
        Dim paths As List(Of JsonElement) = PathsOf(res.ConfigJson)

        ' Whatever else changed, every path is a valid endpoint: a shipped reader refuses the whole
        ' file for one that is not, whatever its kind.
        For Each p As JsonElement In paths
            Assert.True(IsValidEndpoint(p), "an access path without a valid host and port was written: " & p.ToString())
        Next

        Dim tunnels As Integer = paths.Where(Function(p) Kind(p) = TunnelKind).Count()
        If Not publishes Then
            Assert.Equal(0, tunnels)
            Return
        End If
        Assert.Equal(1, tunnels)
        Dim last As JsonElement = paths(paths.Count - 1)
        Assert.Equal(TunnelKind, Kind(last))
        For Each p As JsonElement In paths.Take(paths.Count - 1)
            Assert.Contains(Kind(p), DirectKinds)   ' everything before the tunnel is a direct path
        Next
        ' The id is not the file-level pin, and the pin stays the share's own host key.
        Using doc As JsonDocument = JsonDocument.Parse(res.ConfigJson)
            Assert.Equal("SHA256:abc123", doc.RootElement.GetProperty("hostKeyFingerprintSha256").GetString())
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32())   ' the kind needs no schema 3
        End Using
    End Sub

    ''' <summary>The share id rides on the tunnel path as an ordinary access-path member (FMSCFG 2.4 A
    ''' registers it as <c>shareId</c>) and nowhere else in the file. The exporter's member is read from
    ''' the one constant the dormant export names it in, so this holds whichever spelling it has when
    ''' the export is switched on; which spelling that is, is the dated exception recorded against the
    ''' registered name (see the registry row of this product).</summary>
    <Fact>
    Public Sub TheShareId_IsAnAccessPathMember_AndAppearsOnce()
        Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(), includeExternal:=True, stamp:=Stamp(), exchange:=Export("relay.example.org", 22022, GoodSid, 0))
        Dim tunnel As JsonElement = PathsOf(res.ConfigJson).Last()
        Dim member As JsonElement
        Assert.True(tunnel.TryGetProperty(ExchangeWireNames.TunnelShareIdMember, member))
        Assert.Equal(GoodSid, member.GetString())
        Dim first As Integer = res.ConfigJson.IndexOf(GoodSid, StringComparison.Ordinal)
        Assert.Equal(-1, res.ConfigJson.IndexOf(GoodSid, first + 1, StringComparison.Ordinal))
    End Sub

    ''' <summary>The "LAN only" export describes the local path alone: no tunnel, whatever the exchange
    ''' argument (an exchange endpoint is an externally reachable address).</summary>
    <Fact>
    Public Sub TheLanOnlyExport_NeverCarriesATunnelPath()
        Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(), includeExternal:=False, stamp:=Stamp(), exchange:=Export("relay.example.org", 22022, GoodSid, 40007))
        Assert.DoesNotContain(TunnelKind, res.ConfigJson)
        Assert.Equal({"lan"}, PathsOf(res.ConfigJson).Select(Function(p) Kind(p)).ToArray())
    End Sub

    ''' <summary>No exchange argument - what the package wizard passes unless the producer is on and a share is
    ''' registered - leaves the export free of
    ''' the kind: the bytes of a file written before 2.4 are the bytes written now.</summary>
    <Fact>
    Public Sub WithoutAnExchange_NoTunnelPathIsEverWritten()
        Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(), includeExternal:=True, stamp:=Stamp())
        Assert.DoesNotContain(TunnelKind, res.ConfigJson)
        Assert.False(res.HasRelay)
    End Sub

    ''' <summary>Rule 6 meets rule 7: the diagnostic log line of an export that carries a tunnel path
    ''' still blanks the share id (a bearer link), under the exporter's current member name and under
    ''' the one FMSCFG 2.4 registers.</summary>
    <Fact>
    Public Sub TheLogRedaction_BlanksTheShareId_UnderBothSpellings()
        Dim res As ShareConfigResult = ShareConfigBuilder.Build(Running(), includeExternal:=True, stamp:=Stamp(), exchange:=Export("relay.example.org", 22022, GoodSid, 0))
        Dim logged As String = ShareConfigBuilder.RedactSecrets(res.ConfigJson)
        Assert.DoesNotContain(GoodSid, logged)
        Assert.DoesNotContain("secret", logged)

        Dim registered As String = "{""accessPaths"":[{""kind"":""rendezvousTunnel"",""host"":""h"",""port"":1,""shareId"":""" & GoodSid & """}]}"
        Dim provisional As String = registered.Replace("""shareId""", """sid""")
        Assert.DoesNotContain(GoodSid, ShareConfigBuilder.RedactSecrets(registered))
        Assert.DoesNotContain(GoodSid, ShareConfigBuilder.RedactSecrets(provisional))
        Assert.Contains("""host"":""h""", ShareConfigBuilder.RedactSecrets(registered))   ' a diagnostic that still says something
    End Sub

End Class
