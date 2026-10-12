#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Text.Json

' Ticket 199 section 5: the descriptor and the hand-off. Built to LIVE-BROADCAST 3.1 and 3.2:
' schemaVersion 1, url, mode, title, sourceId, isLive, targetLatencyMs and endpoints, with the LAN
' HTTP endpoint of this ticket (RELAY and TUNNEL are 173's). Three channels carry the SAME
' description (producer rule 3): FMSBCAST1: barcode text (Base64 of the GZIP of the JSON), the
' broadcast_<n>.fmsbcast file, and the QR image - which renders the same barcode text, so the payload
' is built once here. A clickable link is not offered: the contract has not decided a desktop scheme.
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
End Class

Friend NotInheritable Class LiveProducerDescriptorWriter

    Private Sub New()
    End Sub

    ''' <summary>The descriptor JSON of section 3, field by field: schemaVersion first, then the required
    ''' url and mode, then the optional members, then the endpoints array with the one LAN HTTP endpoint.
    ''' Member names and spellings are the contract's, verified by the round-trip test.</summary>
    Public Shared Function BuildJson(input As LiveDescriptorInput) As String
        If input Is Nothing Then Return ""
        If String.IsNullOrWhiteSpace(input.Url) OrElse String.IsNullOrWhiteSpace(input.Mode) Then Return ""

        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms)
                w.WriteStartObject()
                w.WriteNumber("schemaVersion", LiveProducerContract.DescriptorSchemaVersion)
                w.WriteString("url", input.Url)
                w.WriteString("mode", input.Mode)
                If Not String.IsNullOrWhiteSpace(input.Title) Then w.WriteString("title", input.Title.Trim())
                If Not String.IsNullOrEmpty(input.SourceId) Then w.WriteString("sourceId", input.SourceId)
                w.WriteBoolean("isLive", True)
                w.WriteNumber("targetLatencyMs", input.TargetLatencyMs)

                w.WriteStartArray("endpoints")
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
                w.WriteEndArray()

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
