#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

' DEVICE-EXCHANGE 6, ticket 205: replay the Go server's listed records with the independent VB reader.
' Refusal cases describe server admission, not reader validation; those are proved by the generator/probe.
Public Class ExchangeRecordReplayTests

    Private Shared Function CorpusDirectory() As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "exchange-vectors")
    End Function

    Private Shared Function Hash(bytes As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    End Function

    <Fact>
    Public Sub Vendored_files_match_the_generator_and_the_provenance_hashes()
        Dim root As String = ExchangeWireTestKit.RepoRoot()
        Assert.NotEmpty(root)
        Dim provenance As String = File.ReadAllText(Path.Combine(CorpusDirectory(), "PROVENANCE.txt"))
        Assert.Equal(File.ReadAllBytes(Path.Combine(root, "exchange-server", "testdata", "vectors", "PROVENANCE.txt")),
                     File.ReadAllBytes(Path.Combine(CorpusDirectory(), "PROVENANCE.txt")))
        For Each name As String In {"frames-v2.json", "records-v1.json", "connection-string-v1.json", "protection-v1.json"}
            Dim bytes As Byte() = File.ReadAllBytes(Path.Combine(CorpusDirectory(), name))
            Assert.Equal(File.ReadAllBytes(Path.Combine(root, "exchange-server", "testdata", "vectors", name)), bytes)
            Assert.Matches("(?m)^\s*" & System.Text.RegularExpressions.Regex.Escape(name) & "\s+" & Hash(bytes) & "\s*$", provenance)
            Dim changed As Byte() = CType(bytes.Clone(), Byte())
            changed(0) = changed(0) Xor CByte(1)
            Assert.DoesNotContain(Hash(changed), provenance)
        Next
    End Sub

    <Fact>
    Public Sub Protected_resource_vectors_preserve_the_lock_and_the_per_client_access_gate()
        Dim seen As Integer = 0
        Using corpus As JsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory(), "protection-v1.json")))
            For Each transcript As JsonElement In corpus.RootElement.GetProperty("transcripts").EnumerateArray()
                For Each stepValue As JsonElement In transcript.GetProperty("steps").EnumerateArray()
                    Dim jsonValue As JsonElement
                    If Not stepValue.TryGetProperty("json", jsonValue) Then Continue For
                    Using frame As JsonDocument = JsonDocument.Parse(jsonValue.GetString())
                        Dim root As JsonElement = frame.RootElement
                        If root.GetProperty("type").GetString() <> "directory" Then Continue For
                        For Each raw As JsonElement In root.GetProperty("resources").EnumerateArray()
                            Dim record As ExchangeResourceRecord = Nothing
                            Assert.True(ExchangeRecordReader.TryReadResource(raw.GetRawText(), record))
                            Assert.True(record.IsProtected)
                            Assert.Equal("pin-v1", record.ProtectionScheme)
                            Assert.Equal(stepValue.GetProperty("stream").GetString() = "capable", record.HasAccess)
                            seen += 1
                        Next
                    End Using
                Next
            Next
        End Using
        Assert.Equal(2, seen)
    End Sub

    Private Shared Function ReadRaw(kind As String, json As String) As String
        Select Case kind
            Case "device"
                Dim record As ExchangeDeviceRecord = Nothing
                If Not ExchangeRecordReader.TryReadDevice(json, record) Then Return Nothing
                Return record.Raw
            Case "resource"
                Dim record As ExchangeResourceRecord = Nothing
                If Not ExchangeRecordReader.TryReadResource(json, record) Then Return Nothing
                Return record.Raw
            Case "broadcast"
                Dim record As ExchangeBroadcastRecord = Nothing
                If Not ExchangeRecordReader.TryReadBroadcast(json, record) Then Return Nothing
                Return record.Raw
            Case Else
                Throw New InvalidDataException("Unknown record vector kind: " & kind)
        End Select
    End Function

    <Fact>
    Public Sub Every_accepted_record_keeps_all_members_through_reader_and_directory_wire_round_trip()
        Dim accepted As Integer = 0
        Dim oversized As Integer = 0
        Using corpus As JsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory(), "records-v1.json")))
            For Each item As JsonElement In corpus.RootElement.GetProperty("cases").EnumerateArray()
                Dim name As String = item.GetProperty("name").GetString()
                Dim kind As String = item.GetProperty("kind").GetString()
                Dim verdict As String = item.GetProperty("verdict").GetString()
                If verdict <> "accepted" Then
                    Assert.Contains(verdict, {"refused", "closed"})
                    Dim input As String = item.GetProperty("input").GetString()
                    If Encoding.UTF8.GetByteCount(input) > ExchangeWire.MaxRecordBytes Then
                        Assert.Null(ReadRaw(kind, input))
                        oversized += 1
                    End If
                    Continue For
                End If

                Dim listed As String = item.GetProperty("listed").GetString()
                ' Admission bounds the input. Server-owned fields can make the listed boundary case larger;
                ' the client must still enforce section 6 on the bytes it receives.
                Dim readable As Boolean = Encoding.UTF8.GetByteCount(listed) <= ExchangeWire.MaxRecordBytes
                Assert.Equal(If(readable, listed, Nothing), ReadRaw(kind, listed))
                Dim member As String = If(kind = "device", "devices", If(kind = "resource", "resources", "broadcasts"))
                Dim env As New ExchangeEnvelope("directory")
                env.SetNumber("revision", 1).SetRecords("devices", Array.Empty(Of String)()).
                    SetRecords("resources", Array.Empty(Of String)()).SetRecords("broadcasts", Array.Empty(Of String)()).
                    SetRecords(member, {listed})
                Dim decoded As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(ExchangeEnvelopeCodec.Encode(env))
                Assert.Equal(listed, Assert.Single(decoded.Records(member)))

                If name = "resource-unknown-kind" Then
                    Dim record As ExchangeResourceRecord = Nothing
                    Assert.True(ExchangeRecordReader.TryReadResource(listed, record))
                    Assert.False(record.IsSupportedKind)
                End If
                If name = "resource-16KiB-exact" Then
                    Assert.Equal(ExchangeWire.MaxRecordBytes, Encoding.UTF8.GetByteCount(item.GetProperty("input").GetString()))
                End If

                ' The Drive wrapper's record is read by the same reader. This proves record parity, not Drive sync.
                Using drive As JsonDocument = JsonDocument.Parse(item.GetProperty("drive").GetProperty("text").GetString())
                    Assert.Equal(1, drive.RootElement.GetProperty("schemaVersion").GetInt32())
                    Assert.Equal(kind, drive.RootElement.GetProperty("type").GetString())
                    Dim raw As String = drive.RootElement.GetProperty("record").GetRawText()
                    Assert.Equal(If(Encoding.UTF8.GetByteCount(raw) <= ExchangeWire.MaxRecordBytes, raw, Nothing), ReadRaw(kind, raw))
                    Dim idMember As String = kind & "Id"
                    Dim id As String = drive.RootElement.GetProperty("record").GetProperty(idMember).GetString()
                    Assert.Equal("fmsx-" & kind & "-" & id & ".json", item.GetProperty("drive").GetProperty("fileName").GetString())
                    If kind = "resource" Then
                        Dim access As JsonElement = Nothing
                        Assert.False(drive.RootElement.GetProperty("record").TryGetProperty("access", access))
                    End If
                End Using
                accepted += 1
            Next
        End Using
        Assert.Equal(16, accepted)
        Assert.Equal(2, oversized)
    End Sub

    <Fact>
    Public Sub Replay_detects_a_codec_that_changes_a_record_or_frame_byte()
        Dim item As ExchangeVectorCorpus.Vector = ExchangeVectorCorpus.Load().First()
        Dim env As ExchangeEnvelope = ExchangeEnvelopeCodec.Decode(Encoding.UTF8.GetBytes(item.Json))
        Dim encoded As Byte() = ExchangeFrameWriter.BuildFrame(env, ExchangeWire.MaxFrameAuthenticated)
        Assert.Equal(Convert.FromHexString(item.Hex), encoded)
        encoded(encoded.Length - 1) = encoded(encoded.Length - 1) Xor CByte(1)
        Assert.Throws(Of Xunit.Sdk.EqualException)(Sub() Assert.Equal(Convert.FromHexString(item.Hex), encoded))
    End Sub
End Class
#End If
