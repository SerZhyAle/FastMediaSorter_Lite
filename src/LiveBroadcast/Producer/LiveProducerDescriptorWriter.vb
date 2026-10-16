#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Json

' Ticket 199 section 5: the descriptor and the hand-off. Built to LIVE-BROADCAST 3.1 and 3.2:
' schemaVersion 1, url, mode, title, sourceId, isLive, targetLatencyMs and endpoints, with the LAN
' HTTP endpoint of this ticket (broadcasts and folder connections are 173's). Three channels carry the SAME
' description (producer rule 3): FMSBCAST1: barcode text (Base64 of the GZIP of the JSON), the
' broadcast_<n>.fmsbcast file, and the QR image - which renders the same barcode text, so the payload
' is built once here. The window's link is `fmsbcast://live?payload=` + the barcode text (the contract's
' target host is `import`: ticket 239 CR-16 keeps that gap on record).
'
' A descriptor handed outside the account that carries a RELAY or TUNNEL endpoint is always protected
' (LIVE-BROADCAST item M, ticket 239 CR-15): <see cref="BuildHandOutJson"/> is the ONE function the three
' hand-off channels build from, and it leaves RELAY and TUNNEL out of a descriptor that has no `auth` and is not sealed (item Q: a
' sealed endpoint is protected by its key or pin).
'
' Round-trip: this product's own reader (023, LiveBroadcastReader) must accept everything written
' here - LiveProducerDescriptorWriterTests holds it, byte for byte, against the real parser.

''' <summary>What the producer knows when it builds the descriptor (199 section 5): the address, the
''' mode, and the numbers of the endpoint entry.</summary>
Friend Class LiveDescriptorInput
    Public Property Url As String = ""
    Public Property Mode As String = ""
    Public Property Title As String = ""
    Public Property SourceId As String = ""
    Public Property TargetLatencyMs As Long = 1000
    Public Property VideoCodec As String
    Public Property AudioCodec As String
    Public Property SampleRateHz As Long?
    Public Property BitrateBps As Long?
    ''' <summary>Item H (0.16): an endpoints entry gains an optional member channels beside sampleRate.</summary>
    Public Property Channels As Integer?
    Public Property AllowLanViewers As Boolean = True
    Public Property RelayUrl As String = ""
    Public Property TunnelUrl As String = ""
    Public Property CertFingerprint As String = ""
    Public Property InnerUrl As String = ""
    Public Property AuthUser As String = ""
    ''' <summary>DEVICE-EXCHANGE item DK / LIVE-BROADCAST item Q: the RELAY and TUNNEL endpoints carry <c>seal</c> (<c>{v:1}</c>;
    ''' LAN endpoints never do, they do not pass through the server).</summary>
    Public Property Sealed As Boolean
    ''' <summary>True for the descriptor the person hands out (a link, a file, a barcode): the keys ride in its sealed endpoints and a
    ''' sealed endpoint with no key (RELAY) or no pin (TUNNEL) is left out, since nobody holding only that descriptor could play it.
    ''' False for the directory record: <c>seal</c> is <c>{v:1}</c> alone, the keys travel in the record's box.</summary>
    Public Property Outward As Boolean
    ''' <summary>The group key of THIS link (base64url, 32 bytes); written only into an outward descriptor.</summary>
    Public Property RelayKey As String = ""
    ''' <summary>The <c>SHA256:</c> pin of the tunnel's inner TLS leaf (item DM); written only into an outward descriptor.</summary>
    Public Property TunnelPin As String = ""

    ''' <summary>The same input as the directory record's descriptor: no key, no pin.</summary>
    Friend Function ForRecord() As LiveDescriptorInput
        Dim copy As LiveDescriptorInput = DirectCast(MemberwiseClone(), LiveDescriptorInput)
        copy.Outward = False
        copy.RelayKey = ""
        copy.TunnelPin = ""
        Return copy
    End Function

    ''' <summary>The same input with every endpoint that leaves the machine through the exchange taken out (RELAY and TUNNEL, with
    ''' their keys, pins and fingerprint): what is left is the LAN endpoint alone (ticket 239 CR-15).</summary>
    Friend Function ForLanOnly() As LiveDescriptorInput
        Dim copy As LiveDescriptorInput = DirectCast(MemberwiseClone(), LiveDescriptorInput)
        copy.RelayUrl = ""
        copy.TunnelUrl = ""
        copy.CertFingerprint = ""
        copy.InnerUrl = ""
        copy.Sealed = False
        copy.Outward = False
        copy.RelayKey = ""
        copy.TunnelPin = ""
        Return copy
    End Function

    ''' <summary>Never prints a key.</summary>
    Public Overrides Function ToString() As String
        Return "LiveDescriptorInput"
    End Function
End Class

Friend NotInheritable Class LiveProducerDescriptorWriter

    Private Sub New()
    End Sub

    ''' <summary>True when <see cref="BuildHandOutJson"/> leaves a RELAY or TUNNEL endpoint out because the descriptor is neither
    ''' protected by `auth` nor sealed.</summary>
    Public Shared Function WithholdsOutwardEndpoints(input As LiveDescriptorInput) As Boolean
        If input Is Nothing OrElse input.Sealed OrElse Not String.IsNullOrEmpty(input.AuthUser) Then Return False
        Return Not String.IsNullOrWhiteSpace(input.RelayUrl) OrElse Not String.IsNullOrWhiteSpace(input.TunnelUrl)
    End Function

    ''' <summary>The descriptor the person hands out (link, barcode, file): LIVE-BROADCAST item M says a descriptor handed outside
    ''' the account that carries a RELAY or TUNNEL endpoint is always protected, so one with no `auth` is built WITHOUT those two
    ''' endpoints (the LAN alone). When no LAN endpoint is reachable either there is nothing to hand out and the result is "" -
    ''' the window then says why and asks for a password instead of giving an empty hand-off. A protected input is built whole.
    ''' A SEALED input counts as protected (item Q, read with M): its RELAY and TUNNEL endpoints are listed only with the key or the
    ''' pin that opens them (<see cref="BuildJson"/> leaves out one that has none), the descriptor that carries those keys is a
    ''' secret handed by the person, and the relay is offered only to a broadcast with no password (the server has no listener
    ''' verifiers, 239 CR-14) - so without this a sealed outward link could never exist.
    ''' The directory record of the person's own account is a different document (<see cref="LiveDescriptorInput.ForRecord"/>)
    ''' and is not shortened by this.</summary>
    Public Shared Function BuildHandOutJson(input As LiveDescriptorInput) As String
        If input Is Nothing Then Return ""
        If String.IsNullOrEmpty(input.AuthUser) AndAlso Not input.Sealed Then
            Dim lan As LiveDescriptorInput = input.ForLanOnly()
            If Not lan.AllowLanViewers OrElse String.IsNullOrWhiteSpace(lan.Url) Then Return ""
            Return BuildJson(lan)
        End If
        Return BuildJson(input)
    End Function

    ''' <summary>The descriptor JSON of section 3, field by field: schemaVersion first, then the required
    ''' url and mode, then the optional members, then the endpoints array with the endpoints in order:
    ''' LAN (HTTP) -> RELAY -> TUNNEL. Member names and spellings are the contract's, verified by the
    ''' round-trip test.</summary>
    Public Shared Function BuildJson(input As LiveDescriptorInput) As String
        If input Is Nothing Then Return ""

        ' LIVE-BROADCAST item Q: an outward descriptor leaves a sealed endpoint out when its key / pin is missing.
        Dim showRelay As Boolean = Not String.IsNullOrWhiteSpace(input.RelayUrl) AndAlso
                                   Not (input.Sealed AndAlso input.Outward AndAlso String.IsNullOrEmpty(input.RelayKey))
        Dim showTunnel As Boolean = Not String.IsNullOrWhiteSpace(input.TunnelUrl) AndAlso
                                    Not (input.Sealed AndAlso input.Outward AndAlso String.IsNullOrEmpty(input.TunnelPin))

        Dim topUrl As String = ""
        If input.AllowLanViewers AndAlso Not String.IsNullOrWhiteSpace(input.Url) Then
            topUrl = input.Url
        ElseIf showRelay Then
            topUrl = input.RelayUrl
        ElseIf showTunnel Then
            topUrl = input.TunnelUrl
        ElseIf input.Sealed AndAlso input.Outward Then
            Return ""
        Else
            topUrl = input.Url
        End If
        If String.IsNullOrWhiteSpace(topUrl) OrElse String.IsNullOrWhiteSpace(input.Mode) Then Return ""

        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteNumber("schemaVersion", LiveProducerContract.DescriptorSchemaVersion)
                w.WriteString("url", topUrl)
                w.WriteString("mode", input.Mode)
                If Not String.IsNullOrWhiteSpace(input.Title) Then w.WriteString("title", input.Title.Trim())
                If Not String.IsNullOrEmpty(input.SourceId) Then w.WriteString("sourceId", input.SourceId)
                w.WriteBoolean("isLive", True)
                w.WriteNumber("targetLatencyMs", input.TargetLatencyMs)
                If Not String.IsNullOrEmpty(input.AuthUser) Then
                    w.WriteStartObject("auth")
                    w.WriteString("scheme", "password-v1")
                    w.WriteString("user", input.AuthUser)
                    w.WriteEndObject()
                End If

                w.WriteStartArray("endpoints")

                ' 1. LAN HTTP endpoint (when LAN listener is allowed and has a URL)
                If input.AllowLanViewers AndAlso Not String.IsNullOrWhiteSpace(input.Url) Then
                    w.WriteStartObject()
                    w.WriteString("url", input.Url)
                    w.WriteString("transport", "HTTP")
                    w.WriteString("mode", input.Mode)
                    If Not String.IsNullOrEmpty(input.VideoCodec) Then w.WriteString("videoCodec", input.VideoCodec)
                    If Not String.IsNullOrEmpty(input.AudioCodec) Then w.WriteString("audioCodec", input.AudioCodec)
                    If input.SampleRateHz.HasValue Then w.WriteNumber("sampleRate", input.SampleRateHz.Value)
                    If input.BitrateBps.HasValue Then w.WriteNumber("bitrate", input.BitrateBps.Value)
                    If input.Channels.HasValue Then w.WriteNumber("channels", input.Channels.Value)
                    w.WriteBoolean("isLive", True)
                    w.WriteNumber("targetLatencyMs", input.TargetLatencyMs)
                    w.WriteEndObject()
                End If

                ' 2. RELAY endpoint
                If showRelay Then
                    w.WriteStartObject()
                    w.WriteString("url", input.RelayUrl)
                    w.WriteString("transport", "RELAY")
                    w.WriteString("mode", input.Mode)
                    If Not String.IsNullOrEmpty(input.CertFingerprint) Then w.WriteString("certFingerprint", input.CertFingerprint)
                    If input.Sealed Then WriteSeal(w, If(input.Outward, input.RelayKey, ""), "")
                    If Not String.IsNullOrEmpty(input.VideoCodec) Then w.WriteString("videoCodec", input.VideoCodec)
                    If Not String.IsNullOrEmpty(input.AudioCodec) Then w.WriteString("audioCodec", input.AudioCodec)
                    If input.SampleRateHz.HasValue Then w.WriteNumber("sampleRate", input.SampleRateHz.Value)
                    If input.BitrateBps.HasValue Then w.WriteNumber("bitrate", input.BitrateBps.Value)
                    If input.Channels.HasValue Then w.WriteNumber("channels", input.Channels.Value)
                    w.WriteBoolean("isLive", True)
                    w.WriteNumber("targetLatencyMs", 2000L)
                    w.WriteEndObject()
                End If

                ' 3. TUNNEL endpoint
                If showTunnel Then
                    w.WriteStartObject()
                    w.WriteString("url", input.TunnelUrl)
                    w.WriteString("transport", "TUNNEL")
                    w.WriteString("mode", input.Mode)
                    If Not String.IsNullOrEmpty(input.InnerUrl) Then w.WriteString("inner", input.InnerUrl)
                    If Not String.IsNullOrEmpty(input.CertFingerprint) Then w.WriteString("certFingerprint", input.CertFingerprint)
                    If input.Sealed Then WriteSeal(w, "", If(input.Outward, input.TunnelPin, ""))
                    If Not String.IsNullOrEmpty(input.VideoCodec) Then w.WriteString("videoCodec", input.VideoCodec)
                    If Not String.IsNullOrEmpty(input.AudioCodec) Then w.WriteString("audioCodec", input.AudioCodec)
                    If input.SampleRateHz.HasValue Then w.WriteNumber("sampleRate", input.SampleRateHz.Value)
                    If input.BitrateBps.HasValue Then w.WriteNumber("bitrate", input.BitrateBps.Value)
                    If input.Channels.HasValue Then w.WriteNumber("channels", input.Channels.Value)
                    w.WriteBoolean("isLive", True)
                    w.WriteNumber("targetLatencyMs", 2000L)
                    w.WriteEndObject()
                End If

                w.WriteEndArray()

                w.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    ''' <summary>The <c>seal</c> member of a RELAY / TUNNEL endpoint: <c>{v:1}</c> in a directory record, plus <c>key</c> (RELAY) or
    ''' <c>pin</c> (TUNNEL) in a descriptor the person hands out. Keys are written to the document only, never to a log.</summary>
    Private Shared Sub WriteSeal(w As Utf8JsonWriter, key As String, pin As String)
        w.WriteStartObject("seal")
        w.WriteNumber("v", 1)
        If Not String.IsNullOrEmpty(key) Then w.WriteString("key", key)
        If Not String.IsNullOrEmpty(pin) Then w.WriteString("pin", pin)
        w.WriteEndObject()
    End Sub

    ''' <summary>Builds the broadcast record JSON for publication on the exchange (broadcast-start, broadcast-update).
    ''' <paramref name="sealedBoxJson"/> is the record's <c>sealed</c> box (item DK), written verbatim; Nothing or "" writes none.</summary>
    Public Shared Function BuildBroadcastRecord(broadcastId As String, deviceId As String, title As String, mode As String, descriptorJson As String, Optional sealedBoxJson As String = Nothing) As String
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteString("broadcastId", If(broadcastId, ""))
                If Not String.IsNullOrEmpty(deviceId) Then w.WriteString("deviceId", deviceId)
                If Not String.IsNullOrWhiteSpace(title) Then w.WriteString("title", title.Trim())
                w.WriteString("mode", If(mode, ""))
                w.WritePropertyName("descriptor")
                Using doc As JsonDocument = JsonDocument.Parse(If(descriptorJson, "{}"))
                    doc.RootElement.WriteTo(w)
                End Using
                If Not String.IsNullOrEmpty(sealedBoxJson) Then
                    w.WritePropertyName("sealed")
                    Using box As JsonDocument = JsonDocument.Parse(sealedBoxJson)
                        box.RootElement.WriteTo(w)
                    End Using
                End If
                w.WriteEndObject()
            End Using
            Return Encoding.UTF8.GetString(ms.ToArray())
        End Using
    End Function

    ''' <summary>The barcode payload of channel 4.1: "FMSBCAST1:" + standard-alphabet Base64 of the GZIP
    ''' of the JSON. ONE gzip member and nothing after it, because the reader accepts exactly one and
    ''' calls anything trailing invalid; GZipStream writes exactly one member with its trailer.</summary>
    Public Shared Function BarcodePayload(json As String) As String
        If String.IsNullOrEmpty(json) Then Return ""
        Dim raw As Byte() = Encoding.UTF8.GetBytes(json)
        Using compressed As New MemoryStream()
            Using gzip As New GZipStream(compressed, CompressionLevel.Optimal, leaveOpen:=True)
                gzip.Write(raw, 0, raw.Length)
            End Using
            Return LiveProducerContract.BarcodePrefix & Convert.ToBase64String(compressed.ToArray())
        End Using
    End Function

    ''' <summary>The import file's content of channel 4.2: the JSON, uncompressed.</summary>
    Public Shared Function FileContent(json As String) As Byte()
        Return Encoding.UTF8.GetBytes(If(json, ""))
    End Function

    ''' <summary>The import file's name: broadcast_&lt;n&gt;.fmsbcast (the contract's own shape).</summary>
    Public Shared Function FileName(sequence As Integer) As String
        Return "broadcast_" & sequence.ToString(Globalization.CultureInfo.InvariantCulture) & LiveProducerContract.FileExtension
    End Function

    ''' <summary>The text the QR image renders: the barcode payload, the same description as the other
    ''' two channels (producer rule 3).</summary>
    Public Shared Function QrPayload(json As String) As String
        Return BarcodePayload(json)
    End Function

End Class
#End If
