#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DL. The sealed relay: every chunk payload of a broadcast sent through the
' exchange server is a sequence of RECORDS, `len` (4 bytes, unsigned big-endian, counting the type byte and the body), `type`
' (1 byte), the body. Two types are defined: the key ring 0x4B (which opens an epoch) and the media record 0x4D (media
' sealed under the epoch key EK). A record of another type is skipped by a reader, never played.
'
'   - Group keys, 32 random bytes each, belong to the broadcaster: one for the account's own devices (`relayKey`), one per
'     guest grant, one per outward link. EK is 32 random bytes per epoch. The ring carries EK once per group key, sealed under
'     that group key, so a listener holding one group key opens the ring and learns EK; the server sees none of them.
'   - A listener keeps the EK of the ring it opened. A media record of an epoch it does not hold is the outcome NeedReconnect
'     (it reads the current header chunk once); a record whose tag fails is SealedFailed and is never played.
'
' This file is the cryptography and the framing, not the transport: the HTTP, the chunk flags on the upload and the
' reconnect are the relay client's. Nothing here logs; no key, nonce or media byte reaches a message.

''' <summary>The constants and the small functions of item DL.</summary>
Friend NotInheritable Class ExchangeRelayRecords

    Private Sub New()
    End Sub

    ''' <summary>The `contentType` of a sealed upload; the true media type travels inside the key ring.</summary>
    Public Const ContentType As String = "application/x-fmsx-sealed"
    Public Const TypeKeyRing As Byte = &H4B
    Public Const TypeMedia As Byte = &H4D
    Public Const KeyLength As Integer = 32
    Public Const NonceLength As Integer = ExchangeAead.NonceLength
    Public Const KeyIdLength As Integer = 8
    ''' <summary>EK sealed under a group key: 32 bytes and the tag.</summary>
    Public Const SealedKeyLength As Integer = KeyLength + ExchangeAead.TagLength
    Public Const RingEntryLength As Integer = KeyIdLength + NonceLength + SealedKeyLength
    Public Const MaxRingEntries As Integer = 64
    ''' <summary>The value of the `len` of one record: the chunk bound of item U, counted on the payload (1 MiB less the flag byte).</summary>
    Public Const MaxRecordLength As Integer = ExchangeWire.MaxChunkLengthField - 1
    ''' <summary>One EK seals at most 2^32 records (item DL).</summary>
    Public Const MaxRecordsPerKey As Long = 4294967296L

    Private Shared ReadOnly KeyIdPrefix As Byte() = Encoding.ASCII.GetBytes("fmsx-relay-v1 key-id")

    ''' <summary>A new group key or epoch key: 32 random bytes.</summary>
    Public Shared Function NewKey() As Byte()
        Return RandomNumberGenerator.GetBytes(KeyLength)
    End Function

    ''' <summary>The first 8 bytes of SHA-256 over the ASCII bytes `fmsx-relay-v1 key-id` and the group key.</summary>
    Public Shared Function KeyId(groupKey As Byte()) As Byte()
        Dim input(KeyIdPrefix.Length + groupKey.Length - 1) As Byte
        Buffer.BlockCopy(KeyIdPrefix, 0, input, 0, KeyIdPrefix.Length)
        Buffer.BlockCopy(groupKey, 0, input, KeyIdPrefix.Length, groupKey.Length)
        Dim hash As Byte() = SHA256.HashData(input)
        Dim r(KeyIdLength - 1) As Byte
        Buffer.BlockCopy(hash, 0, r, 0, KeyIdLength)
        Array.Clear(input, 0, input.Length)
        Return r
    End Function

    ''' <summary>The UTF-8 bytes of `relay-ring:&lt;broadcastId&gt;:&lt;epoch in decimal&gt;`.</summary>
    Public Shared Function RingAad(broadcastId As String, epoch As UInteger) As Byte()
        Return Encoding.UTF8.GetBytes("relay-ring:" & broadcastId & ":" & epoch.ToString(CultureInfo.InvariantCulture))
    End Function

    ''' <summary>The UTF-8 bytes of `relay-media:&lt;broadcastId&gt;:&lt;epoch&gt;:&lt;flag&gt;`, the flag the decimal value of the
    ''' flag byte of the chunk the record rides in.</summary>
    Public Shared Function MediaAad(broadcastId As String, epoch As UInteger, flag As Integer) As Byte()
        Return Encoding.UTF8.GetBytes("relay-media:" & broadcastId & ":" & epoch.ToString(CultureInfo.InvariantCulture) & ":" & flag.ToString(CultureInfo.InvariantCulture))
    End Function

    ''' <summary>One record on the wire: `len` (type byte and body), `type`, the body.</summary>
    Public Shared Function Frame(type As Byte, body As Byte()) As Byte()
        If body Is Nothing Then Throw New ArgumentNullException(NameOf(body))
        Dim len As Long = 1L + body.Length
        If len > MaxRecordLength Then Throw New ArgumentException("a record above the chunk bound", NameOf(body))
        Dim r(CInt(4 + len) - 1) As Byte
        r(0) = CByte((len >> 24) And &HFF)
        r(1) = CByte((len >> 16) And &HFF)
        r(2) = CByte((len >> 8) And &HFF)
        r(3) = CByte(len And &HFF)
        r(4) = type
        Buffer.BlockCopy(body, 0, r, 5, body.Length)
        Return r
    End Function

    Friend Shared Sub AppendUInt32(target As List(Of Byte), value As UInteger)
        target.Add(CByte((value >> 24) And &HFFUI))
        target.Add(CByte((value >> 16) And &HFFUI))
        target.Add(CByte((value >> 8) And &HFFUI))
        target.Add(CByte(value And &HFFUI))
    End Sub

    Friend Shared Function ReadUInt32(buffer As Byte(), offset As Integer) As UInteger
        Return (CUInt(buffer(offset)) << 24) Or (CUInt(buffer(offset + 1)) << 16) Or (CUInt(buffer(offset + 2)) << 8) Or CUInt(buffer(offset + 3))
    End Function

End Class

''' <summary>One entry of a key ring: the id of a group key, the nonce and EK sealed under that group key.</summary>
Friend NotInheritable Class ExchangeRelayRingEntry
    Public ReadOnly KeyId As Byte()
    Public ReadOnly Nonce As Byte()
    Public ReadOnly SealedKey As Byte()

    Public Sub New(keyId As Byte(), nonce As Byte(), sealedKey As Byte())
        Me.KeyId = keyId
        Me.Nonce = nonce
        Me.SealedKey = sealedKey
    End Sub
End Class

''' <summary>Why a ring did not give its epoch key.</summary>
Friend Enum ExchangeRelayRingOpen
    Opened = 0
    ''' <summary>No entry carries the id of the listener's group key: it was dropped from the broadcast, or it never held one.</summary>
    NoEntry = 1
    TagFailed = 2
End Enum

''' <summary>The body of a key ring record (type 0x4B).</summary>
Friend NotInheritable Class ExchangeRelayRing

    Public ReadOnly Epoch As UInteger
    Public ReadOnly MediaType As String
    Public ReadOnly Entries As IReadOnlyList(Of ExchangeRelayRingEntry)

    Private Sub New(epoch As UInteger, mediaType As String, entries As IReadOnlyList(Of ExchangeRelayRingEntry))
        Me.Epoch = epoch
        Me.MediaType = mediaType
        Me.Entries = entries
    End Sub

    ''' <summary>The body of a ring: `epoch`, the media type, `count` and the entries, EK sealed once per group key. A group key
    ''' listed twice is written once. The arguments are the broadcaster's own, so a violation throws (without a key in the message).</summary>
    Public Shared Function BuildBody(broadcastId As String, epoch As UInteger, mediaType As String, epochKey As Byte(), groupKeys As IReadOnlyList(Of Byte())) As Byte()
        Return BuildBody(broadcastId, epoch, mediaType, epochKey, groupKeys, Nothing)
    End Function

    ''' <summary>The same with the entry nonces given (one per group key, in order). TEST HOOK to reproduce a vector.</summary>
    Friend Shared Function BuildBody(broadcastId As String, epoch As UInteger, mediaType As String, epochKey As Byte(), groupKeys As IReadOnlyList(Of Byte()), nonces As IReadOnlyList(Of Byte())) As Byte()
        If String.IsNullOrEmpty(broadcastId) Then Throw New ArgumentException("broadcastId", NameOf(broadcastId))
        If epochKey Is Nothing OrElse epochKey.Length <> ExchangeRelayRecords.KeyLength Then Throw New ArgumentException("epochKey", NameOf(epochKey))
        If groupKeys Is Nothing OrElse groupKeys.Count = 0 OrElse groupKeys.Count > ExchangeRelayRecords.MaxRingEntries Then Throw New ArgumentException("groupKeys", NameOf(groupKeys))
        If nonces IsNot Nothing AndAlso nonces.Count <> groupKeys.Count Then Throw New ArgumentException("nonces", NameOf(nonces))
        Dim mt As Byte() = MediaTypeBytes(mediaType)
        Dim aad As Byte() = ExchangeRelayRecords.RingAad(broadcastId, epoch)
        Dim body As New List(Of Byte)
        ExchangeRelayRecords.AppendUInt32(body, epoch)
        body.Add(CByte(mt.Length))
        body.AddRange(mt)
        Dim countAt As Integer = body.Count
        body.Add(0)
        Dim written As New List(Of String)
        For i As Integer = 0 To groupKeys.Count - 1
            Dim gk As Byte() = groupKeys(i)
            If gk Is Nothing OrElse gk.Length <> ExchangeRelayRecords.KeyLength Then Throw New ArgumentException("groupKeys", NameOf(groupKeys))
            Dim id As Byte() = ExchangeRelayRecords.KeyId(gk)
            Dim idText As String = Convert.ToHexString(id)
            If written.Contains(idText) Then Continue For
            written.Add(idText)
            Dim nonce As Byte() = If(nonces Is Nothing, RandomNumberGenerator.GetBytes(ExchangeRelayRecords.NonceLength), nonces(i))
            body.AddRange(id)
            body.AddRange(nonce)
            body.AddRange(ExchangeAead.Seal(gk, nonce, aad, epochKey))
        Next
        body(countAt) = CByte(written.Count)
        Return body.ToArray()
    End Function

    ''' <summary>False for a body that is not exactly a ring: a short body, a media type longer than the body, `count` of 0 or
    ''' above 64, or bytes left over.</summary>
    Public Shared Function TryParse(body As Byte(), ByRef ring As ExchangeRelayRing) As Boolean
        ring = Nothing
        If body Is Nothing OrElse body.Length < 4 + 1 + 1 Then Return False
        Dim epoch As UInteger = ExchangeRelayRecords.ReadUInt32(body, 0)
        Dim mtLen As Integer = body(4)
        Dim countAt As Integer = 5 + mtLen
        If mtLen = 0 OrElse countAt >= body.Length Then Return False
        For i As Integer = 5 To countAt - 1
            If body(i) < &H20 OrElse body(i) > &H7E Then Return False
        Next
        Dim count As Integer = body(countAt)
        If count < 1 OrElse count > ExchangeRelayRecords.MaxRingEntries Then Return False
        If body.Length <> countAt + 1 + count * ExchangeRelayRecords.RingEntryLength Then Return False
        Dim entries As New List(Of ExchangeRelayRingEntry)(count)
        Dim at As Integer = countAt + 1
        For i As Integer = 0 To count - 1
            Dim id(ExchangeRelayRecords.KeyIdLength - 1) As Byte
            Dim nonce(ExchangeRelayRecords.NonceLength - 1) As Byte
            Dim sealedKey(ExchangeRelayRecords.SealedKeyLength - 1) As Byte
            Buffer.BlockCopy(body, at, id, 0, id.Length)
            Buffer.BlockCopy(body, at + id.Length, nonce, 0, nonce.Length)
            Buffer.BlockCopy(body, at + id.Length + nonce.Length, sealedKey, 0, sealedKey.Length)
            entries.Add(New ExchangeRelayRingEntry(id, nonce, sealedKey))
            at += ExchangeRelayRecords.RingEntryLength
        Next
        ring = New ExchangeRelayRing(epoch, Encoding.ASCII.GetString(body, 5, mtLen), entries)
        Return True
    End Function

    ''' <summary>Opens the epoch key with one group key: the entry of its id, AES-256-GCM under the group key with the ring's
    ''' associated data. The caller wipes the key.</summary>
    Public Function TryOpen(broadcastId As String, groupKey As Byte(), ByRef epochKey As Byte()) As ExchangeRelayRingOpen
        epochKey = Nothing
        Dim id As Byte() = ExchangeRelayRecords.KeyId(groupKey)
        Dim aad As Byte() = ExchangeRelayRecords.RingAad(broadcastId, Epoch)
        Dim found As Boolean = False
        For Each e As ExchangeRelayRingEntry In Entries
            If Not CryptographicOperations.FixedTimeEquals(e.KeyId, id) Then Continue For
            found = True
            Dim ek As Byte() = Nothing
            If ExchangeAead.TryOpen(groupKey, e.Nonce, aad, e.SealedKey, ek) AndAlso ek.Length = ExchangeRelayRecords.KeyLength Then
                epochKey = ek
                Return ExchangeRelayRingOpen.Opened
            End If
        Next
        Return If(found, ExchangeRelayRingOpen.TagFailed, ExchangeRelayRingOpen.NoEntry)
    End Function

    Private Shared Function MediaTypeBytes(mediaType As String) As Byte()
        If String.IsNullOrEmpty(mediaType) OrElse mediaType.Length > 255 Then Throw New ArgumentException("mediaType", NameOf(mediaType))
        For Each c As Char In mediaType
            If c < " "c OrElse c > "~"c Then Throw New ArgumentException("mediaType", NameOf(mediaType))
        Next
        Return Encoding.ASCII.GetBytes(mediaType)
    End Function

End Class

''' <summary>The broadcaster's side of one epoch: the ring it sends first and the media records sealed under EK. A
''' replacement upload, and every dropped group key, is a NEW epoch - a new sender with a new EK (item DL).</summary>
Friend NotInheritable Class ExchangeRelaySender
    Implements IDisposable

    Private ReadOnly _broadcastId As String
    Private ReadOnly _epoch As UInteger
    Private ReadOnly _epochKey As Byte()
    Private ReadOnly _ringRecord As Byte()
    Private _sealed As Long

    ''' <param name="epochKey">EK, 32 random bytes of this epoch (this object keeps its own copy and wipes it on Dispose).</param>
    ''' <param name="groupKeys">Every group key that may read this epoch; the ring is built here and the keys are not kept.</param>
    Public Sub New(broadcastId As String, mediaType As String, epoch As UInteger, epochKey As Byte(), groupKeys As IReadOnlyList(Of Byte()))
        Me.New(broadcastId, mediaType, epoch, epochKey, groupKeys, Nothing)
    End Sub

    Friend Sub New(broadcastId As String, mediaType As String, epoch As UInteger, epochKey As Byte(), groupKeys As IReadOnlyList(Of Byte()), ringNonces As IReadOnlyList(Of Byte()))
        Dim body As Byte() = ExchangeRelayRing.BuildBody(broadcastId, epoch, mediaType, epochKey, groupKeys, ringNonces)
        _broadcastId = broadcastId
        _epoch = epoch
        _epochKey = CType(epochKey.Clone(), Byte())
        _ringRecord = ExchangeRelayRecords.Frame(ExchangeRelayRecords.TypeKeyRing, body)
    End Sub

    Public ReadOnly Property Epoch As UInteger
        Get
            Return _epoch
        End Get
    End Property

    ''' <summary>The media records sealed under this EK so far.</summary>
    Public ReadOnly Property RecordsSealed As Long
        Get
            Return _sealed
        End Get
    End Property

    ''' <summary>True once EK has sealed the 2^32 records it may; the caller opens a new epoch before sealing another.</summary>
    Public ReadOnly Property IsExhausted As Boolean
        Get
            Return _sealed >= ExchangeRelayRecords.MaxRecordsPerKey
        End Get
    End Property

    ''' <summary>The payload of the header chunk (flag 0x02): the key ring first, then the media record of the format header
    ''' (PAT and PMT for MPEG-TS; none for ADTS, pass nothing or an empty array).</summary>
    Public Function BuildHeaderChunk(headerMedia As Byte()) As Byte()
        Return BuildHeaderChunk(headerMedia, Nothing)
    End Function

    Friend Function BuildHeaderChunk(headerMedia As Byte(), mediaNonce As Byte()) As Byte()
        If headerMedia Is Nothing OrElse headerMedia.Length = 0 Then Return CType(_ringRecord.Clone(), Byte())
        Dim media As Byte() = SealMediaRecord(headerMedia, 2, mediaNonce)
        Dim r(_ringRecord.Length + media.Length - 1) As Byte
        Buffer.BlockCopy(_ringRecord, 0, r, 0, _ringRecord.Length)
        Buffer.BlockCopy(media, 0, r, _ringRecord.Length, media.Length)
        If r.Length > ExchangeRelayRecords.MaxRecordLength Then Throw New ArgumentException("a header chunk above the chunk bound", NameOf(headerMedia))
        Return r
    End Function

    ''' <summary>One media record (type 0x4D) framed for a chunk whose flag byte is <paramref name="flag"/> (0, 1 or 2): `epoch`,
    ''' a random nonce, the media sealed under EK with the associated data of item DL.</summary>
    Public Function SealMedia(media As Byte(), flag As Byte) As Byte()
        Return SealMediaRecord(media, flag, Nothing)
    End Function

    ''' <summary>The same with the nonce given. TEST HOOK to reproduce a vector.</summary>
    Friend Function SealMediaWithNonce(media As Byte(), flag As Byte, nonce As Byte()) As Byte()
        Return SealMediaRecord(media, flag, nonce)
    End Function

    Private Function SealMediaRecord(media As Byte(), flag As Byte, fixedNonce As Byte()) As Byte()
        If media Is Nothing Then Throw New ArgumentNullException(NameOf(media))
        If flag > 2 Then Throw New ArgumentException("flag", NameOf(flag))
        If IsExhausted Then Throw New InvalidOperationException("the epoch key has sealed all the records it may; open a new epoch")
        Dim nonce As Byte() = If(fixedNonce, RandomNumberGenerator.GetBytes(ExchangeRelayRecords.NonceLength))
        Dim sealedMedia As Byte() = ExchangeAead.Seal(_epochKey, nonce, ExchangeRelayRecords.MediaAad(_broadcastId, _epoch, flag), media)
        Dim body As New List(Of Byte)(4 + nonce.Length + sealedMedia.Length)
        ExchangeRelayRecords.AppendUInt32(body, _epoch)
        body.AddRange(nonce)
        body.AddRange(sealedMedia)
        Dim framed As Byte() = ExchangeRelayRecords.Frame(ExchangeRelayRecords.TypeMedia, body.ToArray())
        _sealed += 1        ' counted only when the record fits the chunk bound and so can be sent
        Return framed
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Array.Clear(_epochKey, 0, _epochKey.Length)
    End Sub

End Class

Friend Enum ExchangeRelayReadStatus
    ''' <summary>A whole record was read.</summary>
    Record = 0
    ''' <summary>The stream ended at a record boundary.</summary>
    EndOfStream = 1
    ''' <summary>The stream ended inside a record.</summary>
    Truncated = 2
    ''' <summary>A `len` of 0 or above the chunk bound: the bytes are no longer records.</summary>
    Corrupt = 3
End Enum

Friend NotInheritable Class ExchangeRelayRecord
    Public ReadOnly Type As Byte
    Public ReadOnly Body As Byte()

    Public Sub New(type As Byte, body As Byte())
        Me.Type = type
        Me.Body = body
    End Sub
End Class

Friend NotInheritable Class ExchangeRelayReadResult
    Public ReadOnly Status As ExchangeRelayReadStatus
    Public ReadOnly Record As ExchangeRelayRecord

    Public Sub New(status As ExchangeRelayReadStatus, record As ExchangeRelayRecord)
        Me.Status = status
        Me.Record = record
    End Sub
End Class

''' <summary>Reads records by `len` alone, however the bytes arrive: fed piece by piece (Append, then TryRead until it says no)
''' or pulled from a stream (ReadNext, ReadNextAsync). A record of any type is returned whole; deciding to skip an unknown one
''' is the listener's.</summary>
Friend NotInheritable Class ExchangeRelayRecordReader

    Private _buf As Byte() = New Byte(4095) {}
    Private _start As Integer
    Private _end As Integer
    Private _corrupt As Boolean

    ''' <summary>The bytes held that are not yet a whole record.</summary>
    Public ReadOnly Property Buffered As Integer
        Get
            Return _end - _start
        End Get
    End Property

    Public ReadOnly Property IsCorrupt As Boolean
        Get
            Return _corrupt
        End Get
    End Property

    Public Sub Append(data As Byte(), offset As Integer, count As Integer)
        If data Is Nothing Then Throw New ArgumentNullException(NameOf(data))
        If offset < 0 OrElse count < 0 OrElse offset + count > data.Length Then Throw New ArgumentOutOfRangeException(NameOf(count))
        Reserve(count)
        Buffer.BlockCopy(data, offset, _buf, _end, count)
        _end += count
    End Sub

    Public Sub Append(data As Byte())
        Append(data, 0, data.Length)
    End Sub

    ''' <summary>The next whole record, or False when more bytes are needed - or when the framing is corrupt (IsCorrupt).</summary>
    Public Function TryRead(ByRef record As ExchangeRelayRecord) As Boolean
        record = Nothing
        If _corrupt Then Return False
        Dim have As Integer = _end - _start
        If have < 4 Then Return False
        Dim len As UInteger = ExchangeRelayRecords.ReadUInt32(_buf, _start)
        If len = 0UI OrElse len > CUInt(ExchangeRelayRecords.MaxRecordLength) Then
            _corrupt = True
            Return False
        End If
        If have < 4 + CInt(len) Then Return False
        Dim body(CInt(len) - 2) As Byte
        Buffer.BlockCopy(_buf, _start + 5, body, 0, body.Length)
        record = New ExchangeRelayRecord(_buf(_start + 4), body)
        _start += 4 + CInt(len)
        If _start = _end Then
            _start = 0
            _end = 0
        End If
        Return True
    End Function

    ''' <summary>The next record from the stream, reading only as much as it needs.</summary>
    Public Function ReadNext(source As Stream) As ExchangeRelayReadResult
        Do
            Dim record As ExchangeRelayRecord = Nothing
            If TryRead(record) Then Return New ExchangeRelayReadResult(ExchangeRelayReadStatus.Record, record)
            If _corrupt Then Return New ExchangeRelayReadResult(ExchangeRelayReadStatus.Corrupt, Nothing)
            Reserve(NeededMore())
            Dim n As Integer = source.Read(_buf, _end, _buf.Length - _end)
            If n <= 0 Then Return Ended()
            _end += n
        Loop
    End Function

    Public Async Function ReadNextAsync(source As Stream, cancellationToken As CancellationToken) As Task(Of ExchangeRelayReadResult)
        Do
            Dim record As ExchangeRelayRecord = Nothing
            If TryRead(record) Then Return New ExchangeRelayReadResult(ExchangeRelayReadStatus.Record, record)
            If _corrupt Then Return New ExchangeRelayReadResult(ExchangeRelayReadStatus.Corrupt, Nothing)
            Reserve(NeededMore())
            Dim n As Integer = Await source.ReadAsync(_buf, _end, _buf.Length - _end, cancellationToken).ConfigureAwait(False)
            If n <= 0 Then Return Ended()
            _end += n
        Loop
    End Function

    Private Function Ended() As ExchangeRelayReadResult
        Return New ExchangeRelayReadResult(If(Buffered = 0, ExchangeRelayReadStatus.EndOfStream, ExchangeRelayReadStatus.Truncated), Nothing)
    End Function

    ' How many more bytes the record in progress still needs (at least a few, so a read is never zero-length).
    Private Function NeededMore() As Integer
        Dim have As Integer = _end - _start
        If have < 4 Then Return 4 - have
        Dim len As UInteger = ExchangeRelayRecords.ReadUInt32(_buf, _start)
        Return Math.Max(1, 4 + CInt(Math.Min(len, CUInt(ExchangeRelayRecords.MaxRecordLength))) - have)
    End Function

    Private Sub Reserve(extra As Integer)
        If _end + extra <= _buf.Length Then Return
        Dim live As Integer = _end - _start
        Dim size As Integer = _buf.Length
        While live + extra > size
            size *= 2
        End While
        If size = _buf.Length Then
            Buffer.BlockCopy(_buf, _start, _buf, 0, live)
        Else
            Dim bigger(size - 1) As Byte
            Buffer.BlockCopy(_buf, _start, bigger, 0, live)
            Array.Clear(_buf, 0, _buf.Length)
            _buf = bigger
        End If
        _start = 0
        _end = live
    End Sub

End Class

Friend Enum ExchangeRelayOutcome
    ''' <summary>A media record opened; its media bytes are returned.</summary>
    Media = 0
    ''' <summary>A key ring opened: the listener now holds that epoch's key.</summary>
    KeyRing = 1
    ''' <summary>A record of an unknown type.</summary>
    Skipped = 2
    ''' <summary>A media record of an epoch this listener does not hold: reconnect once and read the current header chunk.</summary>
    NeedReconnect = 3
    ''' <summary>A tag that failed, a malformed record, or a ring with no entry for this listener's group key. Nothing is played.</summary>
    SealedFailed = 4
End Enum

Friend Enum ExchangeRelayFailure
    None = 0
    MalformedRecord = 1
    ''' <summary>The ring has no entry for this listener's group key.</summary>
    NoRingEntry = 2
    RingTagFailed = 3
    MediaTagFailed = 4
End Enum

''' <summary>The listener's side: it holds one group key and, once a ring opens, the EK of that ring's epoch.</summary>
Friend NotInheritable Class ExchangeRelayListener
    Implements IDisposable

    Private ReadOnly _broadcastId As String
    Private ReadOnly _groupKey As Byte()
    Private _epochKey As Byte()
    Private _epoch As UInteger
    Private _mediaType As String = ""
    Private _lastFailure As ExchangeRelayFailure

    Public Sub New(broadcastId As String, groupKey As Byte())
        If String.IsNullOrEmpty(broadcastId) Then Throw New ArgumentException("broadcastId", NameOf(broadcastId))
        If groupKey Is Nothing OrElse groupKey.Length <> ExchangeRelayRecords.KeyLength Then Throw New ArgumentException("groupKey", NameOf(groupKey))
        _broadcastId = broadcastId
        _groupKey = CType(groupKey.Clone(), Byte())
    End Sub

    ''' <summary>True once a ring has opened.</summary>
    Public ReadOnly Property HoldsEpoch As Boolean
        Get
            Return _epochKey IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property Epoch As UInteger
        Get
            Return _epoch
        End Get
    End Property

    ''' <summary>The true media type the ring named ("" before a ring).</summary>
    Public ReadOnly Property MediaType As String
        Get
            Return _mediaType
        End Get
    End Property

    ''' <summary>Why the last record gave SealedFailed (None otherwise). A reason, never a value.</summary>
    Public Property LastFailure As ExchangeRelayFailure
        Get
            Return _lastFailure
        End Get
        Private Set(value As ExchangeRelayFailure)
            _lastFailure = value
        End Set
    End Property

    ''' <summary>One record. <paramref name="flag"/> is the flag byte of the chunk it rode in (0, 1 or 2), or -1 when the reader
    ''' cannot see it (the listen body carries payloads only): the three values are tried, and only the one the sender used
    ''' verifies. <paramref name="media"/> is set only for Media; on any other outcome it is Nothing - nothing partial.</summary>
    Public Function Process(record As ExchangeRelayRecord, flag As Integer, ByRef media As Byte()) As ExchangeRelayOutcome
        media = Nothing
        LastFailure = ExchangeRelayFailure.None
        If record Is Nothing Then Throw New ArgumentNullException(NameOf(record))
        Select Case record.Type
            Case ExchangeRelayRecords.TypeKeyRing
                Return OpenRing(record.Body)
            Case ExchangeRelayRecords.TypeMedia
                Return OpenMedia(record.Body, flag, media)
            Case Else
                Return ExchangeRelayOutcome.Skipped
        End Select
    End Function

    ''' <summary>A whole chunk payload (a sequence of records). The media of every media record is returned joined, in order;
    ''' a ring in the chunk is opened first. The first NeedReconnect or SealedFailed ends it with no media at all. A payload
    ''' that ends inside a record is SealedFailed.</summary>
    Public Function ProcessChunk(payload As Byte(), flag As Integer, ByRef media As Byte()) As ExchangeRelayOutcome
        media = Nothing
        LastFailure = ExchangeRelayFailure.None
        Dim reader As New ExchangeRelayRecordReader()
        reader.Append(payload)
        Dim joined As New MemoryStream()
        Dim sawMedia As Boolean = False
        Dim sawRing As Boolean = False
        Dim record As ExchangeRelayRecord = Nothing
        While reader.TryRead(record)
            Dim one As Byte() = Nothing
            Dim outcome As ExchangeRelayOutcome = Process(record, flag, one)
            Select Case outcome
                Case ExchangeRelayOutcome.NeedReconnect, ExchangeRelayOutcome.SealedFailed
                    Return outcome
                Case ExchangeRelayOutcome.Media
                    sawMedia = True
                    joined.Write(one, 0, one.Length)
                Case ExchangeRelayOutcome.KeyRing
                    sawRing = True
            End Select
        End While
        If reader.IsCorrupt OrElse reader.Buffered <> 0 Then
            LastFailure = ExchangeRelayFailure.MalformedRecord
            Return ExchangeRelayOutcome.SealedFailed
        End If
        If sawMedia Then
            media = joined.ToArray()
            Return ExchangeRelayOutcome.Media
        End If
        Return If(sawRing, ExchangeRelayOutcome.KeyRing, ExchangeRelayOutcome.Skipped)
    End Function

    Private Function OpenRing(body As Byte()) As ExchangeRelayOutcome
        Dim ring As ExchangeRelayRing = Nothing
        If Not ExchangeRelayRing.TryParse(body, ring) Then
            LastFailure = ExchangeRelayFailure.MalformedRecord
            Return ExchangeRelayOutcome.SealedFailed
        End If
        Dim ek As Byte() = Nothing
        Select Case ring.TryOpen(_broadcastId, _groupKey, ek)
            Case ExchangeRelayRingOpen.Opened
                If _epochKey IsNot Nothing Then Array.Clear(_epochKey, 0, _epochKey.Length)
                _epochKey = ek
                _epoch = ring.Epoch
                _mediaType = ring.MediaType
                Return ExchangeRelayOutcome.KeyRing
            Case ExchangeRelayRingOpen.NoEntry
                LastFailure = ExchangeRelayFailure.NoRingEntry
            Case Else
                LastFailure = ExchangeRelayFailure.RingTagFailed
        End Select
        Return ExchangeRelayOutcome.SealedFailed
    End Function

    Private Function OpenMedia(body As Byte(), flag As Integer, ByRef media As Byte()) As ExchangeRelayOutcome
        ' epoch (4), nonce (12), then at least the tag.
        If body.Length < 4 + ExchangeRelayRecords.NonceLength + ExchangeAead.TagLength Then
            LastFailure = ExchangeRelayFailure.MalformedRecord
            Return ExchangeRelayOutcome.SealedFailed
        End If
        Dim epoch As UInteger = ExchangeRelayRecords.ReadUInt32(body, 0)
        If _epochKey Is Nothing OrElse epoch <> _epoch Then Return ExchangeRelayOutcome.NeedReconnect
        Dim nonce(ExchangeRelayRecords.NonceLength - 1) As Byte
        Buffer.BlockCopy(body, 4, nonce, 0, nonce.Length)
        Dim sealedMedia(body.Length - 4 - nonce.Length - 1) As Byte
        Buffer.BlockCopy(body, 4 + nonce.Length, sealedMedia, 0, sealedMedia.Length)
        Dim flags As Integer() = If(flag >= 0, New Integer() {flag}, New Integer() {2, 1, 0})
        For Each f As Integer In flags
            Dim clear As Byte() = Nothing
            If ExchangeAead.TryOpen(_epochKey, nonce, ExchangeRelayRecords.MediaAad(_broadcastId, epoch, f), sealedMedia, clear) Then
                media = clear
                Return ExchangeRelayOutcome.Media
            End If
        Next
        LastFailure = ExchangeRelayFailure.MediaTagFailed
        Return ExchangeRelayOutcome.SealedFailed
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Array.Clear(_groupKey, 0, _groupKey.Length)
        If _epochKey IsNot Nothing Then Array.Clear(_epochKey, 0, _epochKey.Length)
        _epochKey = Nothing
    End Sub

End Class
#End If
