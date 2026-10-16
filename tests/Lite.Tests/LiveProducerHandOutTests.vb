#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Xunit

' Ticket 239 CR-15 (LIVE-BROADCAST item M): a descriptor handed OUT of the account - the link, the barcode text and QR, the
' .fmsbcast file - never carries a RELAY or a TUNNEL endpoint when it has no `auth` and is not sealed (item Q: a sealed endpoint is
' protected by its key or pin, and its key is in the descriptor the person hands out). BuildHandOutJson is the one function the three
' channels build from. The account's OWN directory record (ForRecord) is a different document and keeps every endpoint.
' Modern-only like the whole producer (src/LiveBroadcast/Producer is not in the net48 project).

Public Class LiveProducerHandOutTests

    Private Const RelayHost As String = "relay.example.net:443"
    Private Const RelayKey As String = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    Private Const TunnelPin As String = "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"

    ''' <summary>A broadcast that can be reached in all three ways: the LAN, the relay and the tunnel.</summary>
    Private Shared Function Reachable(isSealed As Boolean, authUser As String) As LiveDescriptorInput
        Return New LiveDescriptorInput With {
            .Url = "http://192.168.1.50:8768/live-audio.aac",
            .Mode = "AUDIO_ONLY",
            .Title = "Kitchen PC",
            .SourceId = "fmsw-0123456789ABCDEF",
            .TargetLatencyMs = 1000,
            .AudioCodec = "aac",
            .SampleRateHz = 44100,
            .BitrateBps = 128000,
            .Channels = 1,
            .AllowLanViewers = True,
            .RelayUrl = "https://" & RelayHost & "/v2/b/bc-1/stream",
            .TunnelUrl = "fmsx://" & RelayHost & "/b/bc-1/http",
            .CertFingerprint = "sha256:" & New String("a"c, 64),
            .InnerUrl = "http://192.168.1.50:8768/live-audio.aac",
            .AuthUser = authUser,
            .Sealed = isSealed,
            .Outward = True,
            .RelayKey = If(isSealed, RelayKey, ""),
            .TunnelPin = If(isSealed, TunnelPin, "")
        }
    End Function

    Private Shared Function Transports(json As String) As List(Of String)
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Return result.Descriptor.Endpoints.Select(Function(e) e.Transport).ToList()
    End Function

    ' ---------------------------------------------------------------- no password: the LAN alone

    <Fact>
    Public Sub Without_a_password_the_hand_out_has_no_RELAY_no_TUNNEL_and_still_the_LAN_url()
        Dim input As LiveDescriptorInput = Reachable(isSealed:=False, authUser:="")

        Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(input)

        Assert.NotEqual("", json)
        Assert.DoesNotContain("RELAY", json, StringComparison.Ordinal)
        Assert.DoesNotContain("TUNNEL", json, StringComparison.Ordinal)
        Assert.DoesNotContain(RelayHost, json, StringComparison.Ordinal)
        Assert.DoesNotContain(RelayKey, json, StringComparison.Ordinal)
        Assert.DoesNotContain(TunnelPin, json, StringComparison.Ordinal)
        Assert.DoesNotContain("certFingerprint", json, StringComparison.Ordinal)
        Assert.DoesNotContain("seal", json, StringComparison.Ordinal)
        Assert.Contains("http://192.168.1.50:8768/live-audio.aac", json, StringComparison.Ordinal)
        Assert.Equal(New List(Of String) From {"HTTP"}, Transports(json))
        ' this product's own reader takes it, and it is the LAN url that is the descriptor's address
        Assert.Equal("http://192.168.1.50:8768/live-audio.aac", LiveBroadcastReader.ReadText(json).Descriptor.Url)
    End Sub

    ''' <summary>Item Q read with item M: a sealed endpoint is protected by its key or pin, so a sealed broadcast with no password is
    ''' handed out whole - the sealed relay link is the one outward form the relay can have while it has no listener verifiers.</summary>
    <Fact>
    Public Sub A_sealed_broadcast_with_no_password_is_handed_out_whole_with_its_keys()
        Dim input As LiveDescriptorInput = Reachable(isSealed:=True, authUser:="")

        Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(input)

        Assert.Equal(LiveProducerDescriptorWriter.BuildJson(input), json)
        Assert.Equal(New List(Of String) From {"HTTP", "RELAY", "TUNNEL"}, Transports(json))
        Assert.Contains(RelayKey, json, StringComparison.Ordinal)
        Assert.Contains(TunnelPin, json, StringComparison.Ordinal)

        ' a sealed endpoint whose key or pin is missing is still left out by the writer, never listed in the clear
        Dim noKeys As LiveDescriptorInput = Reachable(isSealed:=True, authUser:="")
        noKeys.RelayKey = ""
        noKeys.TunnelPin = ""
        Assert.Equal(New List(Of String) From {"HTTP"}, Transports(LiveProducerDescriptorWriter.BuildHandOutJson(noKeys)))
    End Sub

    <Fact>
    Public Sub The_three_channels_carry_the_same_LAN_only_description()
        Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(Reachable(isSealed:=False, authUser:=""))

        ' channel 1: the barcode text (and the QR image, which renders the same text)
        Dim barcode As String = LiveProducerDescriptorWriter.BarcodePayload(json)
        Assert.Equal(New List(Of String) From {"HTTP"}, Transports(barcode))
        Assert.Equal(barcode, LiveProducerDescriptorWriter.QrPayload(json))
        ' channel 2: the link the window copies
        Dim link As String = "fmsbcast://live?payload=" & Uri.EscapeDataString(barcode)
        Dim fromLink As LiveBroadcastReadResult = LiveBroadcastLinkReader.ReadLink(link)
        Assert.Equal(LiveReadStatus.Ok, fromLink.Status)
        Assert.Single(fromLink.Descriptor.Endpoints)
        Assert.Equal("HTTP", fromLink.Descriptor.Endpoints(0).Transport)
        ' channel 3: the .fmsbcast file
        Dim fromFile As LiveBroadcastReadResult = LiveBroadcastReader.ReadFileBytes(LiveProducerDescriptorWriter.FileContent(json))
        Assert.Equal(LiveReadStatus.Ok, fromFile.Status)
        Assert.Single(fromFile.Descriptor.Endpoints)
        Assert.Equal("HTTP", fromFile.Descriptor.Endpoints(0).Transport)
        ' and no channel mentions the exchange
        Assert.DoesNotContain("RELAY", Encoding.UTF8.GetString(LiveProducerDescriptorWriter.FileContent(json)), StringComparison.Ordinal)
        Assert.DoesNotContain(RelayHost, link, StringComparison.Ordinal)
    End Sub

    <Fact>
    Public Sub A_broadcast_with_only_a_LAN_endpoint_is_handed_out_exactly_as_it_was_before()
        Dim input As LiveDescriptorInput = Reachable(isSealed:=False, authUser:="")
        input.RelayUrl = ""
        input.TunnelUrl = ""
        input.CertFingerprint = ""
        input.InnerUrl = ""

        Assert.Equal(LiveProducerDescriptorWriter.BuildJson(input), LiveProducerDescriptorWriter.BuildHandOutJson(input))
    End Sub

    ' ---------------------------------------------------------------- a password: the whole descriptor

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub With_a_password_the_hand_out_is_the_whole_descriptor_byte_for_byte(isSealed As Boolean)
        Dim input As LiveDescriptorInput = Reachable(isSealed, "listener")

        Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(input)

        Assert.Equal(LiveProducerDescriptorWriter.BuildJson(input), json)
        Assert.Contains("RELAY", json, StringComparison.Ordinal)
        Assert.Contains("TUNNEL", json, StringComparison.Ordinal)
        Assert.Equal(New List(Of String) From {"HTTP", "RELAY", "TUNNEL"}, Transports(json))
        Dim read As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(json)
        Assert.Equal("password-v1", read.Descriptor.AuthScheme)
        Assert.Equal("listener", read.Descriptor.AuthUser)
        ' the sealed endpoints carry their key and pin only in the outward descriptor, which is what is handed out
        If isSealed Then
            Assert.Contains(RelayKey, json, StringComparison.Ordinal)
            Assert.Contains(TunnelPin, json, StringComparison.Ordinal)
        End If
    End Sub

    ' ---------------------------------------------------------------- no password and no LAN: nothing to hand out

    <Fact>
    Public Sub Without_a_password_and_without_a_reachable_LAN_endpoint_there_is_no_hand_out()
        Dim noLanViewers As LiveDescriptorInput = Reachable(isSealed:=False, authUser:="")
        noLanViewers.AllowLanViewers = False
        Assert.Equal("", LiveProducerDescriptorWriter.BuildHandOutJson(noLanViewers))

        Dim noLanUrl As LiveDescriptorInput = Reachable(isSealed:=False, authUser:="")
        noLanUrl.Url = ""
        Assert.Equal("", LiveProducerDescriptorWriter.BuildHandOutJson(noLanUrl))

        ' sealed endpoints without their keys are not listed, so a sealed broadcast with no LAN and no keys has nothing to hand out
        Dim sealedNoKeysNoLan As LiveDescriptorInput = Reachable(isSealed:=True, authUser:="")
        sealedNoKeysNoLan.AllowLanViewers = False
        sealedNoKeysNoLan.RelayKey = ""
        sealedNoKeysNoLan.TunnelPin = ""
        Assert.Equal("", LiveProducerDescriptorWriter.BuildHandOutJson(sealedNoKeysNoLan))

        Assert.Equal("", LiveProducerDescriptorWriter.BuildHandOutJson(Nothing))
        ' and the barcode text of "nothing" is nothing, never a blank link payload
        Assert.Equal("", LiveProducerDescriptorWriter.BarcodePayload(""))
    End Sub

    <Fact>
    Public Sub With_a_password_and_no_LAN_the_exchange_endpoints_are_still_handed_out()
        Dim input As LiveDescriptorInput = Reachable(isSealed:=False, authUser:="listener")
        input.AllowLanViewers = False

        Dim json As String = LiveProducerDescriptorWriter.BuildHandOutJson(input)

        Assert.NotEqual("", json)
        Assert.Equal(New List(Of String) From {"RELAY", "TUNNEL"}, Transports(json))
    End Sub

    ' ---------------------------------------------------------------- the two helpers

    <Fact>
    Public Sub WithholdsOutwardEndpoints_is_true_only_for_an_unprotected_unsealed_descriptor_that_has_a_RELAY_or_a_TUNNEL()
        Assert.True(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(Reachable(False, "")))
        ' sealed: protected by its keys (item Q), nothing is withheld
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(Reachable(True, "")))

        Dim relayOnly As LiveDescriptorInput = Reachable(False, "")
        relayOnly.TunnelUrl = ""
        Assert.True(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(relayOnly))

        Dim tunnelOnly As LiveDescriptorInput = Reachable(False, "")
        tunnelOnly.RelayUrl = ""
        Assert.True(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(tunnelOnly))

        ' protected: nothing is withheld
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(Reachable(False, "listener")))
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(Reachable(True, "listener")))

        ' nothing to withhold: the LAN alone, or blank urls
        Dim lanOnly As LiveDescriptorInput = Reachable(False, "")
        lanOnly.RelayUrl = ""
        lanOnly.TunnelUrl = ""
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(lanOnly))
        Dim blank As LiveDescriptorInput = Reachable(False, "")
        blank.RelayUrl = "  "
        blank.TunnelUrl = " "
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(blank))
        Assert.False(LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(Nothing))
    End Sub

    <Fact>
    Public Sub ForLanOnly_drops_relay_tunnel_key_pin_and_fingerprint_and_leaves_the_original_alone()
        Dim original As LiveDescriptorInput = Reachable(isSealed:=True, authUser:="")

        Dim lan As LiveDescriptorInput = original.ForLanOnly()

        Assert.Equal("", lan.RelayUrl)
        Assert.Equal("", lan.TunnelUrl)
        Assert.Equal("", lan.CertFingerprint)
        Assert.Equal("", lan.InnerUrl)
        Assert.Equal("", lan.RelayKey)
        Assert.Equal("", lan.TunnelPin)
        Assert.False(lan.Sealed)
        Assert.False(lan.Outward)
        ' what describes the LAN endpoint is kept
        Assert.Equal(original.Url, lan.Url)
        Assert.Equal(original.Mode, lan.Mode)
        Assert.Equal(original.Title, lan.Title)
        Assert.Equal(original.SourceId, lan.SourceId)
        Assert.Equal(original.AudioCodec, lan.AudioCodec)
        Assert.Equal(original.SampleRateHz, lan.SampleRateHz)
        Assert.True(lan.AllowLanViewers)
        ' the original still has everything (the record is built from it)
        Assert.NotEqual("", original.RelayUrl)
        Assert.NotEqual("", original.TunnelUrl)
        Assert.Equal(RelayKey, original.RelayKey)
        Assert.Equal(TunnelPin, original.TunnelPin)
        Assert.True(original.Sealed)
        Assert.True(original.Outward)
    End Sub

    ' ---------------------------------------------------------------- the account's own record is not shortened

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_directory_record_built_from_the_unshortened_input_keeps_the_outward_endpoints(isSealed As Boolean)
        Dim input As LiveDescriptorInput = Reachable(isSealed, "")
        ' an unsealed hand-out is shortened; a sealed one is whole (its keys protect it) but still differs from the keyless record
        Assert.Equal(Not isSealed, LiveProducerDescriptorWriter.WithholdsOutwardEndpoints(input))

        Dim record As String = LiveProducerDescriptorWriter.BuildJson(input.ForRecord())

        Assert.Equal(New List(Of String) From {"HTTP", "RELAY", "TUNNEL"}, Transports(record))
        Assert.Contains(RelayHost, record, StringComparison.Ordinal)
        ' the record never carries a key or a pin: with a seal it is `{v:1}` alone
        Assert.DoesNotContain(RelayKey, record, StringComparison.Ordinal)
        Assert.DoesNotContain(TunnelPin, record, StringComparison.Ordinal)
        ' and the hand-out of the very same input is the shortened one
        Assert.NotEqual(record, LiveProducerDescriptorWriter.BuildHandOutJson(input))
    End Sub

End Class
#End If
