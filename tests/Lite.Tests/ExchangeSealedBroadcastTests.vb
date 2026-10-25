#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Spec 234 T-3, DEVICE-EXCHANGE section 27 items DK and DL: the BROADCASTER side of the sealed relay. Held here, with fakes and
' no server:
'   - the upload (LiveSealedRelayUploader): a header chunk (flag 0x02) with the key ring and the media record of the format
'     header first, one sealed media record per other chunk, the flag in the frame and bound into the record, read back by a
'     ExchangeRelayListener that holds only a group key; nothing recognisable in the captured bytes;
'   - the record (ExchangeSealedBroadcastPublisher + ExchangeSealedBroadcastSession): RELAY / TUNNEL endpoints carry `seal` and no
'     key, the box opens on recipients only and holds the keys and the credentials, the order "record first, replacement upload
'     after" on an own device's removal, the debounce, the update before a cast;
'   - the descriptor the person hands out (link key in `seal.key`), and the controller that decides what is offered at all.
' The Companion tests do not drive this: the stub account has no broadcast-start / broadcast-update / upload, so what the agent
' does around these classes is held by the compile and by the seams the classes expose to the agent.

Public Class ExchangeSealedBroadcastTests

    Private Const BroadcastId As String = "bcast-0123456789abcdef"
    Private Const OwnId As String = "dev-own"

    ' ------------------------------------------------------------------ fakes

    ''' <summary>A device of the account with a real key pair.</summary>
    Private NotInheritable Class TestDevice
        Implements IDisposable

        Public ReadOnly Id As String
        Public ReadOnly Key As ExchangeDeviceKey

        Public Sub New(id As String)
            Me.Id = id
            Key = ExchangeDeviceKey.Generate()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Key.Dispose()
        End Sub
    End Class

    ''' <summary>The sealed directory of the entry: the devices a test lists, a switch for the server's `sealed`, a raisable Changed.</summary>
    Private NotInheritable Class FakeDirectory
        Implements IExchangeSealedDirectory

        Private ReadOnly _gate As New Object()
        Private ReadOnly _devices As New List(Of TestDevice)()
        Private _revision As Long

        Public ReadOnly Own As TestDevice
        Public Property ServerSupportsSealed As Boolean = True
        ''' <summary>This program has no usable key of its own (item DP `e2e-missing`): nothing may be sealed through the server.</summary>
        Public Property OwnKeyMissing As Boolean
        ''' <summary>No device of the entry is Ready (none has an `e2eKey` this program trusts): the recipient list is empty.</summary>
        Public Property NoRecipientReady As Boolean

        Public Sub New(own As TestDevice)
            Me.Own = own
            _devices.Add(own)
        End Sub

        Public Sub Add(device As TestDevice)
            SyncLock _gate
                _devices.Add(device)
                _revision += 1
            End SyncLock
        End Sub

        Public Sub Remove(deviceId As String)
            SyncLock _gate
                _devices.RemoveAll(Function(d) String.Equals(d.Id, deviceId, StringComparison.Ordinal))
                _revision += 1
            End SyncLock
        End Sub

        Public Sub Raise()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub

        Public ReadOnly Property OwnDeviceId As String Implements IExchangeSealedDirectory.OwnDeviceId
            Get
                Return Own.Id
            End Get
        End Property

        Public ReadOnly Property HasOwnKey As Boolean Implements IExchangeSealedDirectory.HasOwnKey
            Get
                Return Not OwnKeyMissing
            End Get
        End Property

        Public ReadOnly Property ServerKnown As Boolean Implements IExchangeSealedDirectory.ServerKnown
            Get
                Return True
            End Get
        End Property

        Public ReadOnly Property ServerSealed As Boolean Implements IExchangeSealedDirectory.ServerSealed
            Get
                Return ServerSupportsSealed
            End Get
        End Property

        Public ReadOnly Property CanSealThroughServer As Boolean Implements IExchangeSealedDirectory.CanSealThroughServer
            Get
                Return ServerSupportsSealed AndAlso Not OwnKeyMissing
            End Get
        End Property

        Public ReadOnly Property ServerMessage As ConnectionDiagnosisMessage Implements IExchangeSealedDirectory.ServerMessage
            Get
                Return If(ServerSupportsSealed, Nothing, ConnectionDiagnosisMessages.ServerNoE2e)
            End Get
        End Property

        Public ReadOnly Property Revision As Long Implements IExchangeSealedDirectory.Revision
            Get
                SyncLock _gate
                    Return _revision
                End SyncLock
            End Get
        End Property

        Public Function Devices() As IReadOnlyList(Of ExchangeSealedDevice) Implements IExchangeSealedDirectory.Devices
            SyncLock _gate
                Return _devices.Select(Function(d) New ExchangeSealedDevice(d.Id, d.Id, d.Key.Kem, "", True, d Is Own, ExchangeSealState.Ready)).ToList()
            End SyncLock
        End Function

        Public Function Recipients(Optional includeSelf As Boolean = True) As IReadOnlyList(Of ExchangeSealedRecipient) Implements IExchangeSealedDirectory.Recipients
            SyncLock _gate
                If NoRecipientReady Then Return New List(Of ExchangeSealedRecipient)()
                Return _devices.Where(Function(d) includeSelf OrElse d IsNot Own).
                    Select(Function(d) New ExchangeSealedRecipient(d.Id, d.Key.Kem)).ToList()
            End SyncLock
        End Function

        Public Function StateOf(deviceId As String) As Nullable(Of ExchangeSealState) Implements IExchangeSealedDirectory.StateOf
            SyncLock _gate
                If _devices.Any(Function(d) String.Equals(d.Id, deviceId, StringComparison.Ordinal)) Then Return ExchangeSealState.Ready
                Return Nothing
            End SyncLock
        End Function

        Public Function TrustedKeyOf(deviceId As String) As String Implements IExchangeSealedDirectory.TrustedKeyOf
            SyncLock _gate
                For Each d As TestDevice In _devices
                    If String.Equals(d.Id, deviceId, StringComparison.Ordinal) Then Return d.Key.Kem
                Next
                Return ""
            End SyncLock
        End Function

        Public Event Changed As EventHandler Implements IExchangeSealedDirectory.Changed

    End Class

    ''' <summary>A stream that remembers every write as one piece; the uploader writes exactly one chunk frame per call.</summary>
    Private NotInheritable Class CaptureStream
        Inherits Stream

        Private ReadOnly _gate As New Object()
        Private ReadOnly _writes As New List(Of Byte())()

        Public Function Writes() As List(Of Byte())
            SyncLock _gate
                Return New List(Of Byte())(_writes)
            End SyncLock
        End Function

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return True
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

        Public Overrides Sub Flush()
        End Sub

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Dim copy(count - 1) As Byte
            System.Buffer.BlockCopy(buffer, offset, copy, 0, count)
            SyncLock _gate
                _writes.Add(copy)
            End SyncLock
        End Sub

        Public Overrides Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
            Write(buffer, offset, count)
            Return Task.CompletedTask
        End Function
    End Class

    ''' <summary>One chunk frame as the server sees it: the flag byte in the clear and the payload.</summary>
    Private NotInheritable Class Frame
        Public ReadOnly Flags As Byte
        Public ReadOnly Payload As Byte()
        Public ReadOnly IsEnd As Boolean

        Public Sub New(flags As Byte, payload As Byte(), isEnd As Boolean)
            Me.Flags = flags
            Me.Payload = payload
            Me.IsEnd = isEnd
        End Sub
    End Class

    Private Shared Function ParseFrame(write As Byte()) As Frame
        Dim n As Integer = (CInt(write(0)) << 24) Or (CInt(write(1)) << 16) Or (CInt(write(2)) << 8) Or CInt(write(3))
        If n = 0 Then Return New Frame(CByte(0), Array.Empty(Of Byte)(), True)
        Assert.Equal(n, write.Length - 4)
        Dim payload(n - 2) As Byte
        System.Buffer.BlockCopy(write, 5, payload, 0, payload.Length)
        Return New Frame(write(4), payload, False)
    End Function

    ''' <summary>The record layer of a session and its publisher, wired to a fake transport. The event log is shared with the
    ''' upload rig so that a test can read the order "update, then upload".</summary>
    Private NotInheritable Class PublishRig
        Implements IDisposable

        Public ReadOnly Dir As FakeDirectory
        Public ReadOnly Session As ExchangeSealedBroadcastSession
        Public ReadOnly Publisher As ExchangeSealedBroadcastPublisher
        Public ReadOnly Events As New ConcurrentQueue(Of String)()
        Public ReadOnly Records As New List(Of String)()
        Public Property SendResult As Boolean = True
        Public Property SendDelayMs As Integer
        Public Property ThrowOnSend As Boolean
        Public Property Remembered As String
        ''' <summary>The own key cannot be loaded for sealing (a box cannot be built).</summary>
        Public Property KeyLoadFails As Boolean

        Public Sub New(dir As FakeDirectory, mediaType As String, hasRelay As Boolean, tunnelPin As String, password As String, debounceMs As Integer)
            Me.Dir = dir
            Dim scalar As Byte() = dir.Own.Key.ExportPrivateScalar()
            Dim directory As IExchangeSealedDirectory = dir
            Session = New ExchangeSealedBroadcastSession(
                BroadcastId, mediaType, New ExchangeBroadcastKeys(),
                Function() directory, Function() If(KeyLoadFails, Nothing, ExchangeDeviceKey.FromPrivateScalar(scalar)),
                hasRelay, tunnelPin, If(password.Length > 0, "listener", ""), password)
            Dim descriptor As String = LiveProducerDescriptorWriter.BuildJson(SampleInput(True, False, "", ""))
            Dim ownId As String = dir.OwnDeviceId
            Publisher = New ExchangeSealedBroadcastPublisher(
                Session,
                Function(box) LiveProducerDescriptorWriter.BuildBroadcastRecord(BroadcastId, ownId, "title", "AUDIO_ONLY", descriptor, Date.UtcNow, Date.UtcNow, box),
                Sub(record) Remembered = record,
                Function(record) SendAsync(record),
                debounceMs)
        End Sub

        Private Async Function SendAsync(record As String) As Task(Of Boolean)
            Events.Enqueue("update-begin")
            If SendDelayMs > 0 Then Await Task.Delay(SendDelayMs)
            If ThrowOnSend Then Throw New IOException("link down")
            If Not SendResult Then
                Events.Enqueue("update-failed")
                Return False
            End If
            SyncLock Records
                Records.Add(record)
            End SyncLock
            Events.Enqueue("update-sent")
            Return True
        End Function

        Public Function LastRecord() As String
            SyncLock Records
                Return Records(Records.Count - 1)
            End SyncLock
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Publisher.Dispose()
            Session.Dispose()
        End Sub
    End Class

    ''' <summary>The sealed upload loop on a hub, writing to memory streams. Each opened upload is one CaptureStream.</summary>
    Private NotInheritable Class UploadRig
        Implements IDisposable

        Private ReadOnly _session As ExchangeSealedBroadcastSession
        Private ReadOnly _isVideo As Boolean
        Private ReadOnly _events As ConcurrentQueue(Of String)
        Private ReadOnly _streams As New List(Of CaptureStream)()
        Private ReadOnly _cts As New CancellationTokenSource()
        Private _task As Task

        Public ReadOnly Hub As LiveBroadcastHub

        Public Sub New(session As ExchangeSealedBroadcastSession, isVideo As Boolean, events As ConcurrentQueue(Of String))
            _session = session
            _isVideo = isVideo
            _events = events
            Dim policy As ILiveJoinPolicy = If(isVideo, CType(New LiveVideoJoinPolicy(), ILiveJoinPolicy), New LiveAudioJoinPolicy())
            Hub = New LiveBroadcastHub(policy, Function() 0L)
        End Sub

        Public Sub Start()
            _task = Task.Run(Function() LiveSealedRelayUploader.RunAsync(_session, Hub, _isVideo, AddressOf OpenAsync, 10, _cts.Token))
        End Sub

        Private Function OpenAsync(token As CancellationToken) As Task(Of ExchangeOpenResult)
            Dim stream As New CaptureStream()
            SyncLock _streams
                _streams.Add(stream)
            End SyncLock
            _events.Enqueue("upload")
            Return Task.FromResult(New ExchangeOpenResult(ExchangeOpenOutcome.Connected, stream, Nothing, Nothing, Nothing, ExchangeRefusal.Unknown, ""))
        End Function

        Public Function OpenCount() As Integer
            SyncLock _streams
                Return _streams.Count
            End SyncLock
        End Function

        Public Function FrameCount(index As Integer) As Integer
            SyncLock _streams
                If index >= _streams.Count Then Return 0
                Return _streams(index).Writes().Count
            End SyncLock
        End Function

        Public Function Frames(index As Integer) As List(Of Frame)
            SyncLock _streams
                Return _streams(index).Writes().Select(Function(w) ParseFrame(w)).ToList()
            End SyncLock
        End Function

        Public Function AllBytes() As Byte()
            Dim all As New List(Of Byte)()
            SyncLock _streams
                For Each s As CaptureStream In _streams
                    For Each w As Byte() In s.Writes()
                        all.AddRange(w)
                    Next
                Next
            End SyncLock
            Return all.ToArray()
        End Function

        ''' <summary>Appends a segment every 15 ms until the condition holds; False after the timeout.</summary>
        Public Async Function PumpUntilAsync(done As Func(Of Boolean), timeoutMs As Integer) As Task(Of Boolean)
            Dim deadline As Long = Environment.TickCount64 + timeoutMs
            While Environment.TickCount64 < deadline
                If done() Then Return True
                Hub.Append(If(_isVideo, VideoSegment(), AudioSegment()))
                Await Task.Delay(15)
            End While
            Return done()
        End Function

        Public Async Function StopAsync() As Task
            _cts.Cancel()
            If _task IsNot Nothing Then Await Task.WhenAny(_task, Task.Delay(3000))
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                _cts.Cancel()
            Catch
            End Try
            _cts.Dispose()
        End Sub
    End Class

    Private NotInheritable Class CaptureResult
        Public Frames As List(Of Frame)
        Public Raw As Byte()
        Public RelayKey As Byte()
        Public LinkKey As Byte()
    End Class

    ' ------------------------------------------------------------------ helpers

    Private Shared Function VideoSegment() As Byte()
        Return LiveTsBuilder.Segment(LiveTsBuilder.PatPacket(), LiveTsBuilder.PmtPacket(), LiveTsBuilder.VideoPesPacket(True))
    End Function

    Private Shared Function AudioSegment() As Byte()
        Return LiveAdtsBuilder.AdtsStream(44100, 1, 2, 20)
    End Function

    Private Shared Function ContainsBytes(haystack As Byte(), needle As Byte()) As Boolean
        If needle.Length = 0 OrElse haystack.Length < needle.Length Then Return False
        For i As Integer = 0 To haystack.Length - needle.Length
            If haystack(i) <> needle(0) Then Continue For
            Dim same As Boolean = True
            For j As Integer = 1 To needle.Length - 1
                If haystack(i + j) <> needle(j) Then
                    same = False
                    Exit For
                End If
            Next
            If same Then Return True
        Next
        Return False
    End Function

    Private Shared Function Decode(keyText As String) As Byte()
        Dim raw As Byte() = Nothing
        Assert.True(ExchangeBase64Url.TryDecode(keyText, raw))
        Assert.Equal(32, raw.Length)
        Return raw
    End Function

    Private Shared Function RecordsOf(payload As Byte()) As List(Of ExchangeRelayRecord)
        Dim reader As New ExchangeRelayRecordReader()
        reader.Append(payload)
        Dim list As New List(Of ExchangeRelayRecord)()
        Dim rec As ExchangeRelayRecord = Nothing
        While reader.TryRead(rec)
            list.Add(rec)
        End While
        Assert.False(reader.IsCorrupt)
        Assert.Equal(0, reader.Buffered)
        Return list
    End Function

    Private Shared Async Function WaitForAsync(condition As Func(Of Boolean), Optional timeoutMs As Integer = 8000) As Task(Of Boolean)
        Dim deadline As Long = Environment.TickCount64 + timeoutMs
        While Environment.TickCount64 < deadline
            If condition() Then Return True
            Await Task.Delay(10)
        End While
        Return condition()
    End Function

    ''' <summary>The descriptor input of a RELAY + TUNNEL + LAN broadcast.</summary>
    Private Shared Function SampleInput(isSealed As Boolean, outward As Boolean, relayKey As String, tunnelPin As String) As LiveDescriptorInput
        Return New LiveDescriptorInput With {
            .Url = "http://192.168.1.50:8080/live.aac",
            .Mode = "AUDIO_ONLY",
            .Title = "t",
            .SourceId = "src",
            .AudioCodec = "aac",
            .SampleRateHz = 44100,
            .Channels = 1,
            .AllowLanViewers = True,
            .RelayUrl = "https://relay.example:443/v2/b/" & BroadcastId & "/stream",
            .TunnelUrl = "fmsx://relay.example:443/b/" & BroadcastId & "/http",
            .CertFingerprint = "sha256:" & New String("a"c, 64),
            .InnerUrl = "http://192.168.1.50:8080/live.aac",
            .Sealed = isSealed,
            .Outward = outward,
            .RelayKey = relayKey,
            .TunnelPin = tunnelPin
        }
    End Function

    Private NotInheritable Class EndpointSeal
        Public HasSeal As Boolean
        Public V As Integer
        Public Key As String = ""
        Public Pin As String = ""
    End Class

    ''' <summary>The `seal` member of every endpoint of a descriptor, by transport.</summary>
    Private Shared Function SealsOf(descriptorJson As String) As Dictionary(Of String, EndpointSeal)
        Dim result As New Dictionary(Of String, EndpointSeal)(StringComparer.Ordinal)
        Using doc As JsonDocument = JsonDocument.Parse(descriptorJson)
            For Each ep As JsonElement In doc.RootElement.GetProperty("endpoints").EnumerateArray()
                Dim info As New EndpointSeal()
                Dim seal As JsonElement = Nothing
                If ep.TryGetProperty("seal", seal) Then
                    info.HasSeal = True
                    Dim member As JsonElement = Nothing
                    If seal.TryGetProperty("v", member) Then info.V = member.GetInt32()
                    If seal.TryGetProperty("key", member) Then info.Key = member.GetString()
                    If seal.TryGetProperty("pin", member) Then info.Pin = member.GetString()
                End If
                result(ep.GetProperty("transport").GetString()) = info
            Next
        End Using
        Return result
    End Function

    Private Shared Function DescriptorOf(record As String) As String
        Using doc As JsonDocument = JsonDocument.Parse(record)
            Return doc.RootElement.GetProperty("descriptor").GetRawText()
        End Using
    End Function

    ''' <summary>Opens the `sealed` box of a record as one device.</summary>
    Private Shared Function OpenBox(record As String, device As TestDevice, dir As FakeDirectory) As ExchangeSealedOpenResult
        Using doc As JsonDocument = JsonDocument.Parse(record)
            Dim box As ExchangeSealedBox = Nothing
            Assert.True(ExchangeSealedBox.TryParse(doc.RootElement.GetProperty("sealed").GetRawText(), box))
            Return ExchangeSealedBoxes.Open(box, device.Id, device.Key, ExchangeSealedContext.Broadcast(BroadcastId), Function(id) dir.TrustedKeyOf(id))
        End Using
    End Function

    Private Shared Function PayloadOf(opened As ExchangeSealedOpenResult) As JsonDocument
        Assert.True(opened.Succeeded)
        Dim doc As JsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(opened.Payload))
        opened.Wipe()
        Return doc
    End Function

    ''' <summary>True when the JSON object carries the member (a null value counts as present).</summary>
    Private Shared Function HasMember(root As JsonElement, name As String) As Boolean
        If root.ValueKind <> JsonValueKind.Object Then Return False
        Dim member As JsonElement = Nothing
        Return root.TryGetProperty(name, member)
    End Function

    Private Shared Async Function CaptureAsync(isVideo As Boolean, minFrames As Integer, withLink As Boolean) As Task(Of CaptureResult)
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, If(isVideo, "video/mp2t", "audio/aac"), True, "", "", 0)
                Dim linkText As String = If(withLink, rig.Session.AddLink(), "")
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Using upload As New UploadRig(rig.Session, isVideo, rig.Events)
                    upload.Start()
                    Dim ok As Boolean = Await upload.PumpUntilAsync(Function() upload.FrameCount(0) >= minFrames, 8000)
                    Await upload.StopAsync()
                    Assert.True(ok, "the upload did not produce " & minFrames & " chunks")
                    Return New CaptureResult With {
                        .Frames = upload.Frames(0),
                        .Raw = upload.AllBytes(),
                        .RelayKey = Decode(rig.Session.Keys.RelayKeyText()),
                        .LinkKey = If(withLink, Decode(linkText), Nothing)
                    }
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ item DL: the upload

    <Fact>
    Public Async Function Video_upload_opens_with_a_header_chunk_holding_the_ring_and_the_PAT_PMT_media_record() As Task
        Dim capture As CaptureResult = Await CaptureAsync(True, 5, False)
        Dim head As Frame = capture.Frames(0)
        Assert.Equal(ExchangeChunks.FlagHeader, head.Flags)
        Dim headRecords As List(Of ExchangeRelayRecord) = RecordsOf(head.Payload)
        Assert.Equal(2, headRecords.Count)
        Assert.Equal(ExchangeRelayRecords.TypeKeyRing, headRecords(0).Type)
        Assert.Equal(ExchangeRelayRecords.TypeMedia, headRecords(1).Type)

        Using listener As New ExchangeRelayListener(BroadcastId, capture.RelayKey)
            Dim media As Byte() = Nothing
            Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(head.Payload, head.Flags, media))
            Assert.Equal(LiveTsBuilder.PatPacket().Concat(LiveTsBuilder.PmtPacket()).ToArray(), media)
            Assert.Equal("video/mp2t", listener.MediaType)
            Assert.Equal(1UI, listener.Epoch)

            Dim clear As New List(Of Byte)()
            Dim sawJoinable As Boolean = False
            For i As Integer = 1 To capture.Frames.Count - 1
                Dim f As Frame = capture.Frames(i)
                Assert.False(f.IsEnd)
                Assert.True(f.Flags = 0 OrElse f.Flags = ExchangeChunks.FlagJoinable, "only the first chunk is a header")
                Assert.Single(RecordsOf(f.Payload))
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(f.Payload, f.Flags, media))
                Assert.Equal(0, media.Length Mod 188)
                Assert.Equal(CByte(&H47), media(0))
                If f.Flags = ExchangeChunks.FlagJoinable Then
                    sawJoinable = True
                    ' 7.6: a joinable chunk opens where a listener may start - the access unit with the IDR slice
                    Assert.True(ContainsBytes(media, LiveTsBuilder.VideoPesPacket(True)))
                End If
                clear.AddRange(media)
            Next
            Assert.True(sawJoinable)
            Assert.True(ContainsBytes(clear.ToArray(), LiveTsBuilder.VideoPesPacket(True)))
        End Using
    End Function

    <Fact>
    Public Async Function Audio_upload_header_chunk_is_the_ring_alone_and_every_other_chunk_one_joinable_record() As Task
        Dim capture As CaptureResult = Await CaptureAsync(False, 4, False)
        Dim head As Frame = capture.Frames(0)
        Assert.Equal(ExchangeChunks.FlagHeader, head.Flags)
        Dim headRecords As List(Of ExchangeRelayRecord) = RecordsOf(head.Payload)
        Assert.Single(headRecords)                                  ' ADTS has no format header: the ring alone
        Assert.Equal(ExchangeRelayRecords.TypeKeyRing, headRecords(0).Type)

        Using listener As New ExchangeRelayListener(BroadcastId, capture.RelayKey)
            Dim media As Byte() = Nothing
            Assert.Equal(ExchangeRelayOutcome.KeyRing, listener.ProcessChunk(head.Payload, head.Flags, media))
            Assert.Null(media)
            Assert.Equal("audio/aac", listener.MediaType)
            For i As Integer = 1 To capture.Frames.Count - 1
                Dim f As Frame = capture.Frames(i)
                Assert.Equal(ExchangeChunks.FlagJoinable, f.Flags)
                Assert.Single(RecordsOf(f.Payload))
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(f.Payload, f.Flags, media))
                Assert.Equal(AudioSegment(), media)                 ' every chunk starts at an ADTS frame, nothing is cut or lost
            Next
        End Using
    End Function

    <Fact>
    Public Async Function A_listener_that_cannot_see_the_flag_still_reads_the_upload_by_trying_all_three() As Task
        Dim capture As CaptureResult = Await CaptureAsync(True, 4, False)
        Using listener As New ExchangeRelayListener(BroadcastId, capture.RelayKey)
            Dim media As Byte() = Nothing
            ' -1 is "the reader cannot see the frame flag" (the listen body carries payloads only)
            For Each f As Frame In capture.Frames
                Dim outcome As ExchangeRelayOutcome = listener.ProcessChunk(f.Payload, -1, media)
                Assert.True(outcome = ExchangeRelayOutcome.Media OrElse outcome = ExchangeRelayOutcome.KeyRing)
            Next
        End Using
    End Function

    <Fact>
    Public Async Function The_flag_is_bound_into_the_record_so_a_wrong_one_is_refused() As Task
        Dim capture As CaptureResult = Await CaptureAsync(True, 4, False)
        Using listener As New ExchangeRelayListener(BroadcastId, capture.RelayKey)
            Dim media As Byte() = Nothing
            Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(capture.Frames(0).Payload, capture.Frames(0).Flags, media))
            Dim f As Frame = capture.Frames(1)
            Dim wrong As Integer = If(f.Flags = ExchangeChunks.FlagJoinable, 0, 1)
            Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(f.Payload, wrong, media))
            Assert.Equal(ExchangeRelayFailure.MediaTagFailed, listener.LastFailure)
            Assert.Null(media)
            ' the right one still opens: a refusal changes nothing
            Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(f.Payload, f.Flags, media))
        End Using
    End Function

    <Fact>
    Public Async Function The_flags_are_readable_by_the_server_in_the_chunk_frame() As Task
        Dim capture As CaptureResult = Await CaptureAsync(True, 5, False)
        ' the server sees the header flag and the joinable flag without any key: it finds them in the frame byte
        Assert.Equal(ExchangeChunks.FlagHeader, capture.Frames(0).Flags)
        Assert.Contains(capture.Frames, Function(f) f.Flags = ExchangeChunks.FlagJoinable)
        Assert.DoesNotContain(capture.Frames.Skip(1), Function(f) f.Flags = ExchangeChunks.FlagHeader)
    End Function

    <Fact>
    Public Async Function The_captured_video_upload_holds_no_recognisable_plaintext_and_no_key() As Task
        Dim capture As CaptureResult = Await CaptureAsync(True, 5, True)
        Assert.False(ContainsBytes(capture.Raw, LiveTsBuilder.PatPacket()))
        Assert.False(ContainsBytes(capture.Raw, LiveTsBuilder.PmtPacket()))
        Assert.False(ContainsBytes(capture.Raw, LiveTsBuilder.VideoPesPacket(True)))
        Assert.False(ContainsBytes(capture.Raw, capture.RelayKey))
        Assert.False(ContainsBytes(capture.Raw, capture.LinkKey))
    End Function

    <Fact>
    Public Async Function The_captured_audio_upload_holds_no_recognisable_plaintext_and_no_key() As Task
        Dim capture As CaptureResult = Await CaptureAsync(False, 4, True)
        Dim probe As Byte() = LiveAdtsBuilder.AdtsFrame(44100, 1, 20).Take(16).ToArray()
        Assert.False(ContainsBytes(capture.Raw, probe))
        Assert.False(ContainsBytes(capture.Raw, capture.RelayKey))
        Assert.False(ContainsBytes(capture.Raw, capture.LinkKey))
    End Function

    <Fact>
    Public Async Function An_outward_link_key_opens_the_ring_and_a_stranger_key_does_not() As Task
        Dim capture As CaptureResult = Await CaptureAsync(False, 3, True)
        Dim head As Frame = capture.Frames(0)
        Dim media As Byte() = Nothing
        Using viaLink As New ExchangeRelayListener(BroadcastId, capture.LinkKey)
            Assert.Equal(ExchangeRelayOutcome.KeyRing, viaLink.ProcessChunk(head.Payload, head.Flags, media))
            Assert.Equal(ExchangeRelayOutcome.Media, viaLink.ProcessChunk(capture.Frames(1).Payload, capture.Frames(1).Flags, media))
            Assert.Equal(AudioSegment(), media)
        End Using
        Using stranger As New ExchangeRelayListener(BroadcastId, ExchangeRelayRecords.NewKey())
            Assert.Equal(ExchangeRelayOutcome.SealedFailed, stranger.ProcessChunk(head.Payload, head.Flags, media))
            Assert.Equal(ExchangeRelayFailure.NoRingEntry, stranger.LastFailure)
            Assert.False(stranger.HoldsEpoch)
        End Using
        ' the key ring names the broadcast: a listener holding the right key for another broadcast opens nothing
        Using elsewhere As New ExchangeRelayListener("bcast-another", capture.RelayKey)
            Assert.Equal(ExchangeRelayOutcome.SealedFailed, elsewhere.ProcessChunk(head.Payload, head.Flags, media))
        End Using
    End Function

    ' ------------------------------------------------------------------ item DP: no clear fallback

    <Fact>
    Public Async Function A_server_without_sealed_gets_no_upload_at_all_until_it_has_one() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own) With {.ServerSupportsSealed = False}
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                Assert.NotNull(rig.Publisher.PrepareRecord())
                Assert.Null(rig.Session.BoxJson)                    ' nothing to seal to through this server
                Assert.False(rig.Session.CanUpload)
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.False(Await upload.PumpUntilAsync(Function() upload.OpenCount() > 0, 500))
                    Assert.Equal(0, upload.OpenCount())             ' not a single clear byte, not even an empty stream
                    Assert.Empty(upload.AllBytes())

                    dir.ServerSupportsSealed = True
                    ' what the agent does when the directory changes: the record gets its box first, then the upload may follow
                    Assert.True(Await rig.Publisher.PublishAsync())
                    Assert.True(Await upload.PumpUntilAsync(Function() upload.FrameCount(0) >= 2, 8000))
                    Assert.Equal(1, upload.OpenCount())
                    Await upload.StopAsync()
                    Assert.Equal(ExchangeChunks.FlagHeader, upload.Frames(0)(0).Flags)
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_sealed_server_that_goes_away_mid_stream_stops_the_writes() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.True(Await upload.PumpUntilAsync(Function() upload.FrameCount(0) >= 3, 8000))
                    dir.ServerSupportsSealed = False
                    Await Task.Delay(100)
                    Dim before As Integer = upload.AllBytes().Length
                    Assert.False(Await upload.PumpUntilAsync(Function() upload.AllBytes().Length > before, 400))
                    Await upload.StopAsync()
                End Using
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ item N: record first, replacement upload after

    <Fact>
    Public Async Function Removing_an_own_device_makes_a_new_key_and_epoch_and_the_update_goes_before_the_replacement_upload() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), tablet As New TestDevice("dev-tablet")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            dir.Add(tablet)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.SendDelayMs = 150                              ' a slow send: an upload that did not wait would show in the log
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim firstKey As String = rig.Session.Keys.RelayKeyText()
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.True(Await WaitForAsync(Function() upload.FrameCount(0) >= 1))
                    Assert.Equal(1UI, rig.Session.Epoch)

                    dir.Remove("dev-tablet")
                    Assert.True(Await rig.Publisher.PublishAsync())
                    Assert.True(Await WaitForAsync(Function() upload.OpenCount() >= 2 AndAlso upload.FrameCount(1) >= 1))
                    Await upload.StopAsync()

                    ' the record (with the new box) is updated FIRST, the replacement upload is opened after it went out
                    Assert.Equal(New String() {"upload", "update-begin", "update-sent", "upload"}, rig.Events.ToArray())
                    Assert.Equal(2UI, rig.Session.Epoch)
                    Assert.NotEqual(firstKey, rig.Session.Keys.RelayKeyText())

                    ' the old own key opens nothing of the new epoch; the new one does
                    Dim head2 As Frame = upload.Frames(1)(0)
                    Dim media As Byte() = Nothing
                    Using old As New ExchangeRelayListener(BroadcastId, Decode(firstKey))
                        Assert.Equal(ExchangeRelayOutcome.SealedFailed, old.ProcessChunk(head2.Payload, head2.Flags, media))
                        Assert.Equal(ExchangeRelayFailure.NoRingEntry, old.LastFailure)
                    End Using
                    Using current As New ExchangeRelayListener(BroadcastId, Decode(rig.Session.Keys.RelayKeyText()))
                        Assert.Equal(ExchangeRelayOutcome.KeyRing, current.ProcessChunk(head2.Payload, head2.Flags, media))
                        Assert.Equal(2UI, current.Epoch)
                    End Using

                    ' the record that went out: the device that stayed learns the new key, the removed one is not addressed
                    Dim record As String = rig.LastRecord()
                    Using payload As JsonDocument = PayloadOf(OpenBox(record, phone, dir))
                        Assert.Equal(rig.Session.Keys.RelayKeyText(), payload.RootElement.GetProperty("relayKey").GetString())
                    End Using
                    Assert.Equal(ExchangeSealedOpenOutcome.NoEntry, OpenBox(record, tablet, dir).Outcome)
                    Assert.False(rig.Session.Addresses("dev-tablet"))
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_withdrawn_link_key_cannot_open_the_next_epoch() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.SendDelayMs = 100
                Dim link As String = rig.Session.AddLink()
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.True(Await WaitForAsync(Function() upload.FrameCount(0) >= 1))
                    Dim media As Byte() = Nothing
                    Using viaLink As New ExchangeRelayListener(BroadcastId, Decode(link))
                        Assert.Equal(ExchangeRelayOutcome.KeyRing, viaLink.ProcessChunk(upload.Frames(0)(0).Payload, ExchangeChunks.FlagHeader, media))
                    End Using

                    Assert.True(rig.Session.WithdrawLink(link))
                    Assert.True(rig.Session.ReplacementPending)
                    Assert.True(Await rig.Publisher.PublishAsync())
                    Assert.True(Await WaitForAsync(Function() upload.OpenCount() >= 2 AndAlso upload.FrameCount(1) >= 1))
                    Await upload.StopAsync()

                    Assert.Equal(New String() {"upload", "update-begin", "update-sent", "upload"}, rig.Events.ToArray())
                    Dim head2 As Frame = upload.Frames(1)(0)
                    Using viaLink As New ExchangeRelayListener(BroadcastId, Decode(link))
                        Assert.Equal(ExchangeRelayOutcome.SealedFailed, viaLink.ProcessChunk(head2.Payload, head2.Flags, media))
                        Assert.Equal(ExchangeRelayFailure.NoRingEntry, viaLink.LastFailure)
                    End Using
                    Using own2 As New ExchangeRelayListener(BroadcastId, Decode(rig.Session.Keys.RelayKeyText()))
                        Assert.Equal(ExchangeRelayOutcome.KeyRing, own2.ProcessChunk(head2.Payload, head2.Flags, media))
                        Assert.Equal(2UI, own2.Epoch)
                    End Using
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_failed_send_keeps_the_replacement_waiting_until_the_record_went_out() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), tablet As New TestDevice("dev-tablet")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            dir.Add(tablet)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                dir.Remove("dev-tablet")

                rig.SendResult = False
                Assert.False(Await rig.Publisher.PublishAsync())
                Assert.True(rig.Session.ReplacementPending)
                Assert.Equal(0L, rig.Session.ReplaceSeq)            ' the uploader is not released

                rig.SendResult = True
                Assert.True(Await rig.Publisher.PublishAsync())
                Assert.False(rig.Session.ReplacementPending)
                Assert.Equal(1L, rig.Session.ReplaceSeq)

                ' a connection that throws is the next change's problem, not an exception for the caller
                rig.ThrowOnSend = True
                dir.Add(tablet)
                Await rig.Publisher.NotifyRecipientsChangedAsync()
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ item DK: the record and its box

    <Fact>
    Public Sub RELAY_and_TUNNEL_endpoints_carry_seal_and_no_key_and_the_box_opens_only_on_recipients()
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), stranger As New TestDevice("dev-stranger")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                Dim record As String = rig.Publisher.PrepareRecord()
                Dim relayKey As String = rig.Session.Keys.RelayKeyText()

                Dim seals As Dictionary(Of String, EndpointSeal) = SealsOf(DescriptorOf(record))
                Assert.False(seals("HTTP").HasSeal)                 ' a LAN endpoint does not pass through the server
                For Each transport As String In New String() {"RELAY", "TUNNEL"}
                    Assert.True(seals(transport).HasSeal)
                    Assert.Equal(1, seals(transport).V)
                    Assert.Equal("", seals(transport).Key)          ' the key rides in the box, never in the endpoint
                    Assert.Equal("", seals(transport).Pin)
                Next
                Assert.DoesNotContain(relayKey, record)
                Assert.DoesNotContain("credentials", record)
                Assert.Equal(record, rig.Remembered)                 ' what the agent repeats after a reconnect

                For Each device As TestDevice In New TestDevice() {own, phone}
                    Using payload As JsonDocument = PayloadOf(OpenBox(record, device, dir))
                        Assert.Equal(relayKey, payload.RootElement.GetProperty("relayKey").GetString())
                        Assert.False(HasMember(payload.RootElement, "tunnelPin"))   ' no pin until the tunnel has TLS (item DM)
                        Assert.False(HasMember(payload.RootElement, "credentials"))
                    End Using
                Next
                Assert.Equal(ExchangeSealedOpenOutcome.NoEntry, OpenBox(record, stranger, dir).Outcome)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_tunnel_pin_is_carried_in_the_box_when_there_is_one()
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", False, "SHA256:abcdef", "", 0)
                Dim record As String = rig.Publisher.PrepareRecord()
                Assert.DoesNotContain("SHA256:abcdef", record)
                Using payload As JsonDocument = PayloadOf(OpenBox(record, phone, dir))
                    Assert.Equal("SHA256:abcdef", payload.RootElement.GetProperty("tunnelPin").GetString())
                    Assert.False(HasMember(payload.RootElement, "relayKey"))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Credentials_of_a_protected_broadcast_are_only_ever_inside_the_box()
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", False, "", "s3cret-pass-0042", 0)
                Dim record As String = rig.Publisher.PrepareRecord()
                Assert.DoesNotContain("s3cret-pass-0042", record)
                Assert.DoesNotContain("s3cret-pass-0042", rig.Session.BoxJson)
                Assert.DoesNotContain("credentials", record)
                Using payload As JsonDocument = PayloadOf(OpenBox(record, phone, dir))
                    Dim credentials As JsonElement = payload.RootElement.GetProperty("credentials")
                    Assert.Equal("listener", credentials.GetProperty("user").GetString())
                    Assert.Equal("s3cret-pass-0042", credentials.GetProperty("password").GetString())
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Async Function Credential_revocation_rebuilds_the_box_for_unchanged_recipients() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "revoked-password", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim key As String = rig.Session.Keys.RelayKeyText()
                rig.Session.SetCredentials("", "")
                Assert.Null(rig.Session.BoxJson)
                Assert.True(Await rig.Publisher.PublishAsync())
                Using payload As JsonDocument = PayloadOf(OpenBox(rig.LastRecord(), phone, dir))
                    Assert.False(HasMember(payload.RootElement, "credentials"))
                    Assert.Equal(key, payload.RootElement.GetProperty("relayKey").GetString())
                End Using
                Assert.False(Await rig.Publisher.PublishAsync())
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function Endpoint_only_updates_publish_even_when_the_box_has_not_changed() As Task
        Using own As New TestDevice(OwnId)
            Dim dir As New FakeDirectory(own)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim box As String = rig.Session.BoxJson
                Assert.False(Await rig.Publisher.PublishAsync())
                Assert.True(Await rig.Publisher.PublishAsync(force:=True))
                Assert.Equal(box, rig.Session.BoxJson)
                Assert.False(Await rig.Publisher.PublishAsync())
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_recipient_that_joins_updates_the_record_without_a_new_epoch_or_key() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), tablet As New TestDevice("dev-tablet")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim keyBefore As String = rig.Session.Keys.RelayKeyText()
                Assert.False(Await rig.Publisher.PublishAsync())     ' nothing changed: nothing is sent

                dir.Add(tablet)
                Assert.True(Await rig.Publisher.PublishAsync())
                Assert.Equal(keyBefore, rig.Session.Keys.RelayKeyText())
                Assert.False(rig.Session.ReplacementPending)
                Assert.Equal(0L, rig.Session.ReplaceSeq)
                Assert.True(rig.Session.Addresses("dev-tablet"))
                Using payload As JsonDocument = PayloadOf(OpenBox(rig.LastRecord(), tablet, dir))
                    Assert.Equal(keyBefore, payload.RootElement.GetProperty("relayKey").GetString())
                End Using
                Assert.False(Await rig.Publisher.PublishAsync())
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_burst_of_directory_changes_is_debounced_into_one_update() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), a As New TestDevice("dev-a"), b As New TestDevice("dev-b")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 80)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim t1 As Task = rig.Publisher.NotifyRecipientsChangedAsync()
                dir.Add(a)
                Dim t2 As Task = rig.Publisher.NotifyRecipientsChangedAsync()
                dir.Add(b)
                Dim t3 As Task = rig.Publisher.NotifyRecipientsChangedAsync()
                Await Task.WhenAll(t1, t2, t3)
                Assert.Equal(1, rig.Records.Count)
                Assert.True(rig.Session.Addresses("dev-a"))
                Assert.True(rig.Session.Addresses("dev-b"))
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_cast_to_a_device_the_box_does_not_address_updates_the_record_first() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), tablet As New TestDevice("dev-tablet")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()

                Await rig.Publisher.BeforeCastAsync("dev-phone")     ' addressed already: nothing to send
                Assert.Empty(rig.Records)

                dir.Add(tablet)                                      ' joined, the debounce has not fired yet
                Assert.False(rig.Session.Addresses("dev-tablet"))
                Await rig.Publisher.BeforeCastAsync("dev-tablet")
                Assert.Single(rig.Records)
                Assert.True(rig.Session.Addresses("dev-tablet"))
                Using payload As JsonDocument = PayloadOf(OpenBox(rig.LastRecord(), tablet, dir))
                    Assert.True(HasMember(payload.RootElement, "relayKey"))
                End Using

                Await rig.Publisher.BeforeCastAsync("dev-tablet")
                Await rig.Publisher.BeforeCastAsync("")
                Assert.Single(rig.Records)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_concurrent_own_device_update_cannot_release_the_ring_before_guest_boxes_are_sent() As Task
        Using own As New TestDevice(OwnId)
            Dim dir As New FakeDirectory(own)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                rig.Publisher.PrepareRecord()
                rig.Session.MarkPublished()
                Dim entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                Dim guests = rig.Publisher.PublishAsync(Async Function()
                    rig.Session.RequestReplacement()
                    entered.TrySetResult(True)
                    Await release.Task
                End Function)
                Await entered.Task
                Dim ownUpdate = rig.Publisher.PublishAsync()
                Try
                    Assert.True(rig.Session.ReplacementPending)
                    Assert.Empty(rig.Records)
                    Assert.False(ownUpdate.IsCompleted)
                Finally
                    release.TrySetResult(True)
                End Try
                Await Task.WhenAll(guests, ownUpdate)
                Assert.Single(rig.Records)
                Assert.False(rig.Session.ReplacementPending)
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------ item DK: the descriptor the person hands out, and old ones

    <Fact>
    Public Sub The_outward_descriptor_carries_the_link_key_and_the_record_descriptor_never_does()
        Dim key As String = ExchangeBase64Url.Encode(ExchangeRelayRecords.NewKey())

        Dim outward As Dictionary(Of String, EndpointSeal) = SealsOf(LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, key, "")))
        Assert.False(outward("HTTP").HasSeal)
        Assert.Equal(1, outward("RELAY").V)
        Assert.Equal(key, outward("RELAY").Key)
        Assert.False(outward.ContainsKey("TUNNEL"))                  ' no pin yet (item DM): nobody could play it, so it is left out

        Dim withPin As Dictionary(Of String, EndpointSeal) = SealsOf(LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, key, "SHA256:pin")))
        Assert.Equal(1, withPin("TUNNEL").V)
        Assert.Equal("SHA256:pin", withPin("TUNNEL").Pin)
        Assert.Equal("", withPin("TUNNEL").Key)

        Dim noKey As Dictionary(Of String, EndpointSeal) = SealsOf(LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, "", "")))
        Assert.False(noKey.ContainsKey("RELAY"))

        Dim recordJson As String = LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, key, "SHA256:pin").ForRecord())
        Assert.DoesNotContain(key, recordJson)
        Assert.DoesNotContain("SHA256:pin", recordJson)
        Assert.Equal(1, SealsOf(recordJson)("RELAY").V)
        Assert.Equal(1, SealsOf(recordJson)("TUNNEL").V)
    End Sub

    <Fact>
    Public Sub The_reader_reads_the_link_key_and_an_old_descriptor_without_seal_keeps_parsing()
        Dim key As String = ExchangeBase64Url.Encode(ExchangeRelayRecords.NewKey())
        Dim sealedResult As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, key, "")))
        Assert.Equal(LiveReadStatus.Ok, sealedResult.Status)
        Dim relay As LiveBroadcastEndpoint = sealedResult.Descriptor.Endpoints.Single(Function(e) e.Transport = "RELAY")
        Assert.True(relay.IsSealed)
        Assert.Equal(1, relay.SealVersion)
        Assert.Equal(key, relay.SealKey)

        ' a descriptor of the time before `seal`: no member, still read, nothing sealed
        Dim oldResult As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(LiveProducerDescriptorWriter.BuildJson(SampleInput(False, False, "", "")))
        Assert.Equal(LiveReadStatus.Ok, oldResult.Status)
        Assert.All(oldResult.Descriptor.Endpoints, Sub(e) Assert.False(e.IsSealed))
        Assert.Equal("RELAY", oldResult.Descriptor.Endpoints.Single(Function(e) e.Transport = "RELAY").Transport)
    End Sub

    <Fact>
    Public Sub An_endpoint_with_an_unknown_seal_version_stays_skippable_and_the_rest_still_plays()
        Dim key As String = ExchangeBase64Url.Encode(ExchangeRelayRecords.NewKey())
        Dim json As String = LiveProducerDescriptorWriter.BuildJson(SampleInput(True, True, key, ""))
        Dim future As String = json.Replace("""v"":1", """v"":7")
        Assert.NotEqual(json, future)
        Dim result As LiveBroadcastReadResult = LiveBroadcastReader.ReadText(future)
        Assert.Equal(LiveReadStatus.Ok, result.Status)
        Dim relay As LiveBroadcastEndpoint = result.Descriptor.Endpoints.Single(Function(e) e.Transport = "RELAY")
        Assert.True(relay.IsSealed)
        Assert.Equal(7, relay.SealVersion)
        Assert.Equal("HTTP", LiveEndpointSelector.SelectEndpoint(result.Descriptor).Transport)
    End Sub

    ' ------------------------------------------------------------------ the controller: what is offered at all

    Private NotInheritable Class FakeSealing
        Implements ILiveBroadcastSealing

        Public Property CanSeal As Boolean = True
        Public KeysMade As Integer
        Public LastKey As String = ""
        ''' <summary>Ticket 234 E1b: how many tunnel pins were asked for, and the last one; "" in <see cref="NoTunnelPin"/> mode.</summary>
        Public PinsMade As Integer
        Public LastPin As String = ""
        Public NoTunnelPin As Boolean

        Public ReadOnly Property CanSealThroughServer As Boolean Implements ILiveBroadcastSealing.CanSealThroughServer
            Get
                Return CanSeal
            End Get
        End Property

        Public Function NewLinkKey(broadcastId As String) As String Implements ILiveBroadcastSealing.NewLinkKey
            KeysMade += 1
            LastKey = ExchangeBase64Url.Encode(ExchangeRelayRecords.NewKey())
            Return LastKey
        End Function

        Public Function NewTunnelPin(broadcastId As String) As String Implements ILiveBroadcastSealing.NewTunnelPin
            PinsMade += 1
            If NoTunnelPin Then Return ""
            LastPin = ExchangeSealedTunnel.PinOf(Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            Return LastPin
        End Function
    End Class

    Private NotInheritable Class ControllerRig
        Public ReadOnly Encoder As New FakeEncoder()
        Public ReadOnly Source As New FakeAudioSource()
        Public ReadOnly VideoSource As New FakeVideoSource()
        Public ReadOnly Store As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public ReadOnly Port As Integer = LiveTestPorts.FreePort()
        Public Address As String = "192.168.1.50"

        Public Function Ports(sealing As ILiveBroadcastSealing) As LiveProducerPorts
            Dim rig As ControllerRig = Me
            Return New LiveProducerPorts With {
                .GateEnabled = Function() True,
                .PickAddress = Function() rig.Address,
                .LoadSettings = Function() New LiveProducerSettings With {.AudioPort = rig.Port, .AllowLanVideoViewers = False},
                .EnrolledDeviceId = Function() "",
                .ReadStored = Function(key) If(rig.Store.ContainsKey(key), rig.Store(key), Nothing),
                .WriteStored = Sub(key, value) rig.Store(key) = value,
                .CreateEncoder = Function() rig.Encoder,
                .CreateAudioSource = Function(kind) rig.Source,
                .CreateVideoSource = Function(kind) rig.VideoSource,
                .ClockMs = Function() 0L,
                .NewToken = Function() "aabbccdd",
                .ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64)),
                .Sealing = sealing
            }
        End Function
    End Class

    <Fact>
    Public Sub A_sealed_start_lists_RELAY_and_TUNNEL_with_seal_and_keeps_the_link_key_out_of_the_record_descriptor()
        Dim rig As New ControllerRig()
        Dim sealing As New FakeSealing()
        Dim controller As New LiveBroadcastController(rig.Ports(sealing))
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.True(controller.State.Sealed)
            Assert.Equal(1, sealing.KeysMade)

            ' what the person hands out: the RELAY endpoint with the link key and the TUNNEL endpoint with the pin of its inner TLS (item DM)
            Dim outward As Dictionary(Of String, EndpointSeal) = SealsOf(controller.State.DescriptorJson)
            Assert.Equal(1, outward("RELAY").V)
            Assert.Equal(sealing.LastKey, outward("RELAY").Key)
            Assert.Equal(1, sealing.PinsMade)
            Assert.Equal(1, outward("TUNNEL").V)
            Assert.Equal(sealing.LastPin, outward("TUNNEL").Pin)
            Assert.Equal("", outward("TUNNEL").Key)
            Assert.Contains(sealing.LastKey, controller.State.DescriptorJson)

            ' what the directory record carries: `seal: {v}` on both, no key, no pin
            Dim recordSeals As Dictionary(Of String, EndpointSeal) = SealsOf(controller.State.RecordDescriptorJson)
            For Each transport As String In New String() {"RELAY", "TUNNEL"}
                Assert.True(recordSeals(transport).HasSeal)
                Assert.Equal(1, recordSeals(transport).V)
                Assert.Equal("", recordSeals(transport).Key)
                Assert.Equal("", recordSeals(transport).Pin)
            Next
            Assert.DoesNotContain(sealing.LastKey, controller.State.RecordDescriptorJson)
            Assert.DoesNotContain(sealing.LastPin, controller.State.RecordDescriptorJson)
        Finally
            controller.Stop()
        End Try
    End Sub

    ''' <summary>Ticket 263 T-3 (CR-14): a protected broadcast lists its RELAY only once the box is wired, because the relay then carries
    ''' ciphertext and `verifiers` are a second factor; the set holds the link pair and the own pair, whose password only the box carries.</summary>
    <Fact>
    Public Sub A_protected_sealed_start_lists_the_RELAY_and_holds_the_link_pair_and_the_own_pair()
        Dim rig As New ControllerRig()
        Dim sealing As New FakeSealing()
        Dim controller As New LiveBroadcastController(rig.Ports(sealing))
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY", .Password = "separate-secret"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.True(controller.State.Sealed)
            Dim outward As Dictionary(Of String, EndpointSeal) = SealsOf(controller.State.DescriptorJson)
            Assert.True(outward.ContainsKey("RELAY"), "sealed and protected: the RELAY is listed")
            Assert.True(outward.ContainsKey("TUNNEL"))
            Assert.StartsWith("https://relay.example:443/v2/b/", controller.State.RelayUrl, StringComparison.Ordinal)
            Assert.Equal("listener", controller.State.AuthUser)

            Dim pairs As LiveCredentialPairs = controller.State.Credentials
            Assert.True(pairs.IsProtected)
            Assert.Equal(New String() {"listener", "own"}, pairs.Users().ToArray())
            Assert.Equal("separate-secret", pairs.PasswordFor("listener"))
            Dim own As String = pairs.PasswordFor("own")
            Assert.True(own.Length >= 24)
            Assert.NotEqual("separate-secret", own)
            ' neither password is in anything the person hands out or the directory holds
            For Each text As String In New String() {controller.State.DescriptorJson, controller.State.RecordDescriptorJson, controller.State.BarcodeText}
                Assert.DoesNotContain("separate-secret", text)
                Assert.DoesNotContain(own, text)
            Next
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub Without_a_password_a_sealed_start_holds_no_pair_so_the_verifiers_are_empty()
        Dim rig As New ControllerRig()
        Dim controller As New LiveBroadcastController(rig.Ports(New FakeSealing()))
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.False(controller.State.Credentials.IsProtected)
            Assert.Equal(0, controller.State.Credentials.Count)
            Assert.Empty(controller.State.Credentials.VerifierRecords())
            Assert.Equal("", controller.State.AuthUser)
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub A_tunnel_that_cannot_make_its_identity_is_not_listed_at_all_and_the_relay_still_is()
        Dim rig As New ControllerRig()
        Dim sealing As New FakeSealing() With {.NoTunnelPin = True}
        Dim controller As New LiveBroadcastController(rig.Ports(sealing))
        Try
            Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY"})
            Assert.True(result.Ok, result.SeamReason)
            Assert.Equal(1, sealing.PinsMade)
            For Each json As String In New String() {controller.State.DescriptorJson, controller.State.RecordDescriptorJson}
                Dim seals As Dictionary(Of String, EndpointSeal) = SealsOf(json)
                Assert.True(seals.ContainsKey("RELAY"))
                Assert.False(seals.ContainsKey("TUNNEL"))   ' no clear TUNNEL either: no pin, no endpoint
            Next
            Assert.Equal("", controller.State.TunnelUrl)
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub Without_a_sealed_server_neither_RELAY_nor_TUNNEL_is_offered_and_no_key_is_made()
        Dim rig As New ControllerRig() With {.Address = ""}
        Dim sealing As New FakeSealing() With {.CanSeal = False}
        Dim controller As New LiveBroadcastController(rig.Ports(sealing))
        Dim result As LiveStartResult = controller.Start(New LiveStartRequest With {.Kind = LiveProducerSourceKind.Camera, .Mode = "VIDEO_ONLY"})
        Assert.False(result.Ok)
        Assert.Equal(LiveStartRefusal.NoLanAddress, result.Refusal)   ' exchange only, and the exchange cannot seal: nothing is left to offer
        Assert.False(controller.State.Running)
        Assert.Equal("", controller.State.DescriptorJson)
        Assert.Equal(0, sealing.KeysMade)
        Assert.False(rig.Encoder.Started)
    End Sub

    ' ------------------------------------------------------------------ items DH and DP: who is sealed to, and when nothing goes

    Private Shared Function RecordHasBox(record As String) As Boolean
        Using doc As JsonDocument = JsonDocument.Parse(record)
            Return HasMember(doc.RootElement, "sealed")
        End Using
    End Function

    <Fact>
    Public Async Function Without_a_key_of_its_own_a_device_makes_no_box_and_uploads_nothing() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own) With {.OwnKeyMissing = True}
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                Dim record As String = rig.Publisher.PrepareRecord()
                Assert.False(RecordHasBox(record))
                Assert.Null(rig.Session.BoxJson)
                Assert.False(rig.Session.CanUpload)
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.False(Await upload.PumpUntilAsync(Function() upload.OpenCount() > 0, 400))
                    Assert.Empty(upload.AllBytes())             ' no clear byte in place of the sealed ones
                    Await upload.StopAsync()
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function With_no_recipient_ready_nothing_goes_up_until_a_link_or_a_recipient_can_open_it() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own) With {.NoRecipientReady = True}
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                Dim record As String = rig.Publisher.PrepareRecord()
                Assert.False(RecordHasBox(record))              ' no device with a key: no box
                Assert.Null(rig.Session.BoxJson)
                Assert.False(rig.Session.CanUpload)
                Using upload As New UploadRig(rig.Session, False, rig.Events)
                    upload.Start()
                    Assert.False(Await upload.PumpUntilAsync(Function() upload.OpenCount() > 0, 400))
                    Assert.Empty(upload.AllBytes())

                    ' an outward link is somebody who can open the stream: its key is in the ring
                    Dim link As String = rig.Session.AddLink()
                    Assert.True(rig.Session.CanUpload)
                    Assert.True(Await upload.PumpUntilAsync(Function() upload.FrameCount(0) >= 2, 8000))
                    Dim media As Byte() = Nothing
                    Using viaLink As New ExchangeRelayListener(BroadcastId, Decode(link))
                        Assert.Equal(ExchangeRelayOutcome.KeyRing, viaLink.ProcessChunk(upload.Frames(0)(0).Payload, ExchangeChunks.FlagHeader, media))
                    End Using

                    ' a device that becomes ready is sealed to by the next record, never the other way round
                    dir.NoRecipientReady = False
                    Assert.True(Await rig.Publisher.PublishAsync())
                    Assert.True(rig.Session.Addresses("dev-phone"))
                    Await upload.StopAsync()
                End Using
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_box_that_cannot_be_rebuilt_after_a_removal_leaves_no_stale_box_in_the_record() As Task
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone"), tablet As New TestDevice("dev-tablet")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            dir.Add(tablet)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "", 0)
                Assert.True(RecordHasBox(rig.Publisher.PrepareRecord()))
                rig.Session.MarkPublished()
                Dim firstKey As String = rig.Session.Keys.RelayKeyText()

                rig.KeyLoadFails = True
                dir.Remove("dev-tablet")
                Assert.True(Await rig.Publisher.PublishAsync())
                Assert.NotEqual(firstKey, rig.Session.Keys.RelayKeyText())   ' what the removed device knew opens nothing new
                Assert.Null(rig.Session.BoxJson)
                Assert.False(RecordHasBox(rig.LastRecord()))                 ' the box with the old key does not stay on the server
                Assert.False(rig.Session.Addresses("dev-tablet"))
                Assert.False(rig.Session.Addresses("dev-phone"))
                Assert.Equal(1L, rig.Session.ReplaceSeq)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub A_box_addresses_at_most_40_recipients_and_the_record_stays_within_the_wire_bound()
        Dim devices As New List(Of TestDevice)()
        Using own As New TestDevice(OwnId)
            Try
                Dim dir As New FakeDirectory(own)
                For i As Integer = 1 To 45
                    Dim d As New TestDevice("dev-" & i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) & New String("x"c, 16))   ' 22 characters, like a real id
                    devices.Add(d)
                    dir.Add(d)
                Next
                Using rig As New PublishRig(dir, "video/mp2t", True, "SHA256:" & New String("A"c, 43), "s3cret-pass-0042", 0)
                    Dim record As String = rig.Publisher.PrepareRecord()
                    Assert.NotNull(rig.Session.BoxJson)             ' 46 devices listed: the box is built for 40, not refused
                    Assert.True(Encoding.UTF8.GetByteCount(record) <= ExchangeWire.MaxRecordBytes, "the largest record (relayKey, pin, credentials, 40 keys) fits the wire bound")
                    Using doc As JsonDocument = JsonDocument.Parse(record)
                        Assert.Equal(40, doc.RootElement.GetProperty("sealed").GetProperty("keys").GetArrayLength())
                    End Using
                    Assert.True(rig.Session.Addresses(OwnId))
                    Assert.True(rig.Session.Addresses(devices(38).Id))
                    Assert.False(rig.Session.Addresses(devices(39).Id))
                    Assert.False(rig.Session.Addresses(devices(44).Id))
                    Using payload As JsonDocument = PayloadOf(OpenBox(record, devices(38), dir))
                        Assert.Equal(rig.Session.Keys.RelayKeyText(), payload.RootElement.GetProperty("relayKey").GetString())
                    End Using
                    Assert.Equal(ExchangeSealedOpenOutcome.NoEntry, OpenBox(record, devices(44), dir).Outcome)
                End Using
            Finally
                For Each d As TestDevice In devices
                    d.Dispose()
                Next
            End Try
        End Using
    End Sub

    ' ------------------------------------------------------------------ spec 198 rule 4: nothing secret in a string

    <Fact>
    Public Sub No_key_box_or_credential_reaches_a_ToString()
        Using own As New TestDevice(OwnId), phone As New TestDevice("dev-phone")
            Dim dir As New FakeDirectory(own)
            dir.Add(phone)
            Using rig As New PublishRig(dir, "audio/aac", True, "", "s3cret-pass-0042", 0)
                rig.Publisher.PrepareRecord()
                Dim link As String = rig.Session.AddLink()
                Dim relayKey As String = rig.Session.Keys.RelayKeyText()
                Dim texts As String() = {
                    rig.Session.ToString(), rig.Session.Keys.ToString(), rig.Publisher.ToString(),
                    SampleInput(True, True, link, "SHA256:pin").ToString()
                }
                For Each text As String In texts
                    Assert.DoesNotContain(link, text)
                    Assert.DoesNotContain(relayKey, text)
                    Assert.DoesNotContain("s3cret-pass-0042", text)
                    Assert.DoesNotContain("SHA256:pin", text)
                Next
                Using sender As ExchangeRelaySender = rig.Session.NewSender()
                    Assert.DoesNotContain(relayKey, sender.ToString())
                End Using
            End Using
        End Using
    End Sub

End Class
#End If
