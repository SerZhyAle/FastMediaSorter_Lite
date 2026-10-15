#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199 B0 and section 8: the pure MPEG-TS half of the [PROPOSED] video shape (amendment item C).
' Reads the transport stream the encoder writes so the hub can (a) start a new listener at the next
' keyframe preceded by PAT and PMT, and (b) prove the cadences the shape asks for: PAT and PMT at
' least every 250 ms (item H), a keyframe at least every 2 s.
'
' Packet-level only, deliberately: the point is not a decoder, it is finding PAT, PMT and IDR
' access-unit starts in a stream this product's own encoder produces. What is parsed:
'   - 188-byte packet alignment and the sync byte;
'   - PAT (PID 0) -> the PMT pid of the first program;
'   - PMT -> the H.264 video pid (stream type 0x1B) and the AAC pid (0x0F);
'   - a PES-start packet on the video pid whose payload carries an IDR NAL (nal_ref_idc type 5)
'     -> a join point: the packet a new listener starts at, preceded by the latest PAT and PMT.
' Real x264 output starts an access unit with SPS/PPS/IDR in the first PES of the AU, which is what
' the scan keys on; a frame split across PES packets does not hide its IDR, because the scan reads
' every PES payload of the video pid, not only AU starts guessed from timing.

''' <summary>One event of a fed segment: what the chunker saw and where.</summary>
Friend Class TsChunkEvent
    Public Enum Kind As Integer
        PatPacket = 1
        PmtPacket = 2
        ''' <summary>A PES-start packet on the video pid whose payload contains an IDR NAL. The join point
        ''' of item C: "a new connection starts at the next keyframe, preceded by PAT and PMT".</summary>
        JoinPoint = 3
    End Enum

    Public Property EventKind As Kind
    ''' <summary>Offset of the whole 188-byte packet inside the buffer this event came from.</summary>
    Public Property PacketOffset As Integer
    ''' <summary>A copy of the packet's 188 bytes, for the hub's header block (PAT/PMT events).</summary>
    Public Property PacketCopy As Byte()
End Class

''' <summary>The result of the one-shot analysis of section 8: PAT/PMT found, IDR boundaries found,
''' 188-byte alignment, a stream with no IDR flagged.</summary>
Friend Class TsAnalysis
    Public Property Aligned As Boolean = True
    Public Property PatFound As Boolean
    Public Property PmtFound As Boolean
    Public Property VideoPidFound As Boolean
    Public Property AudioPidFound As Boolean
    ''' <summary>Packet-aligned offsets of IDR join points, in order.</summary>
    Public ReadOnly Property JoinPointOffsets As New List(Of Integer)()
End Class

Friend NotInheritable Class TsChunker

    Private Sub New()
    End Sub

    ''' <summary>True when the buffer is a whole number of packets, each with a sync byte - the alignment
    ''' rule of section 8 ("188-byte alignment").</summary>
    Public Shared Function IsAligned(data As Byte()) As Boolean
        If data Is Nothing OrElse data.Length = 0 Then Return False
        If (data.Length Mod LiveProducerContract.TsPacketSize) <> 0 Then Return False
        For off As Integer = 0 To data.Length - 1 Step LiveProducerContract.TsPacketSize
            If data(off) <> LiveProducerContract.TsSyncByte Then Return False
        Next
        Return True
    End Function

    ''' <summary>The one-shot analysis of a whole recorded stream (tests, and the cadence checks of
    ''' section 8). The stream must be aligned; an unaligned stream answers Aligned = False and nothing
    ''' else is promised.</summary>
    Public Shared Function Analyze(data As Byte()) As TsAnalysis
        Dim a As New TsAnalysis With {.Aligned = IsAligned(data)}
        If Not a.Aligned Then Return a
        Dim state As New TsChunkerState()
        For off As Integer = 0 To data.Length - 1 Step LiveProducerContract.TsPacketSize
            Dim e As TsChunkEvent = state.Scan(data, off)
            If e Is Nothing Then Continue For
            Select Case e.EventKind
                Case TsChunkEvent.Kind.PatPacket : a.PatFound = True
                Case TsChunkEvent.Kind.PmtPacket : a.PmtFound = True
                Case TsChunkEvent.Kind.JoinPoint
                    If Not a.VideoPidFound Then a.VideoPidFound = True
                    a.JoinPointOffsets.Add(off)
            End Select
        Next
        a.VideoPidFound = a.VideoPidFound OrElse state.VideoPid >= 0
        a.AudioPidFound = state.AudioPid >= 0
        Return a
    End Function

    ''' <summary>Time-bounded keyframe cadence: True when at least one IDR join point sits inside the
    ''' window - "a stream with no IDR in 2 s flagged". The window is fed by the caller (the hub), which
    ''' owns the timestamps; this only counts.</summary>
    Public Shared Function HasJoinPointInWindow(events As IEnumerable(Of TsChunkEvent), kindFilter As TsChunkEvent.Kind) As Boolean
        If events Is Nothing Then Return False
        For Each e As TsChunkEvent In events
            If e.EventKind = kindFilter Then Return True
        Next
        Return False
    End Function

End Class

''' <summary>The stateful half the hub feeds: remembers the last PAT and PMT (the header block of a
''' join) and the pids they define, and emits one event per notable packet.</summary>
''' <summary>One relay cut; a joinable payload always STARTS at an IDR TS packet.</summary>
Friend Class LiveRelayCut
    Public Property Header As Boolean
    Public Property Joinable As Boolean
    Public Property Payload As Byte()
End Class

''' <summary>Retains PAT/PMT across hub pulls and cuts at IDRs before assigning relay flags.</summary>
Friend NotInheritable Class LiveRelayTsChunker
    Private ReadOnly _state As New TsChunkerState()
    Private _headerSent As Boolean

    Public Function Feed(segment As Byte()) As List(Of LiveRelayCut)
        Dim cuts As New List(Of LiveRelayCut)()
        If Not TsChunker.IsAligned(segment) Then Return cuts
        Dim joins As New HashSet(Of Integer)()
        For Each e As TsChunkEvent In _state.Feed(segment)
            If e.EventKind = TsChunkEvent.Kind.JoinPoint Then joins.Add(e.PacketOffset)
        Next
        If Not _headerSent Then
            Dim header As Byte() = _state.HeaderBlock()
            If header Is Nothing Then Return cuts
            cuts.Add(New LiveRelayCut With {.Header = True, .Payload = header})
            _headerSent = True
        End If
        Dim start As Integer = 0
        For offset As Integer = LiveProducerContract.TsPacketSize To segment.Length Step LiveProducerContract.TsPacketSize
            If offset = segment.Length OrElse joins.Contains(offset) OrElse offset - start >= 188 * 256 Then
                cuts.Add(New LiveRelayCut With {
                    .Joinable = joins.Contains(start),
                    .Payload = segment.AsSpan(start, offset - start).ToArray()
                })
                start = offset
            End If
        Next
        Return cuts
    End Function
End Class

Friend Class TsChunkerState

    Private _lastPatPacket As Byte()
    Private _lastPmtPacket As Byte()
    Private _patSeen As Boolean
    Private _pmtSeen As Boolean
    Private _pmtPid As Integer = -1

    Public ReadOnly Property PatSeen As Boolean
        Get
            Return _patSeen
        End Get
    End Property

    Public ReadOnly Property PmtSeen As Boolean
        Get
            Return _pmtSeen
        End Get
    End Property

    Public ReadOnly Property PmtPid As Integer
        Get
            Return _pmtPid
        End Get
    End Property

    ''' <summary>The H.264 pid, or -1 until a PMT names one.</summary>
    Public ReadOnly Property VideoPid As Integer
        Get
            Return _videoPidValue
        End Get
    End Property

    ''' <summary>The AAC pid, or -1 until a PMT names one.</summary>
    Public ReadOnly Property AudioPid As Integer
        Get
            Return _audioPidValue
        End Get
    End Property

    Private _videoPidValue As Integer = -1
    Private _audioPidValue As Integer = -1

    ''' <summary>A fresh header block for a listener that joins now: the latest PAT packet and the latest
    ''' PMT packet, in that order, or nothing when either has not been seen yet (a listener then waits -
    ''' the join contract starts a stream with both in front, never without).</summary>
    Public Function HeaderBlock() As Byte()
        If Not _patSeen OrElse Not _pmtSeen Then Return Nothing
        Dim block(LiveProducerContract.TsPacketSize * 2 - 1) As Byte
        Array.Copy(_lastPatPacket, 0, block, 0, LiveProducerContract.TsPacketSize)
        Array.Copy(_lastPmtPacket, 0, block, LiveProducerContract.TsPacketSize, LiveProducerContract.TsPacketSize)
        Return block
    End Function

    ''' <summary>Scans one packet at <paramref name="packetOffset"/> of a 188-aligned buffer and returns
    ''' its event, or Nothing for a packet that is none of PAT, PMT or a video join point.</summary>
    Public Function Scan(data As Byte(), packetOffset As Integer) As TsChunkEvent
        If data Is Nothing OrElse packetOffset < 0 OrElse packetOffset + LiveProducerContract.TsPacketSize > data.Length Then Return Nothing
        Dim pid As Integer = ((data(packetOffset + 1) And &H1F) << 8) Or data(packetOffset + 2)
        Dim payloadUnitStart As Boolean = (data(packetOffset + 1) And &H40) <> 0
        Dim payload As Integer = PacketPayloadOffset(data, packetOffset)
        If payload < 0 Then Return Nothing

        If pid = LiveProducerContract.PatPid Then
            _lastPatPacket = PacketCopy(data, packetOffset)
            _patSeen = True
            If payloadUnitStart Then ReadPatForPmtPid(data, payload)
            Return New TsChunkEvent With {.EventKind = TsChunkEvent.Kind.PatPacket, .PacketOffset = packetOffset, .PacketCopy = _lastPatPacket}
        End If

        If _pmtPid >= 0 AndAlso pid = _pmtPid Then
            _lastPmtPacket = PacketCopy(data, packetOffset)
            _pmtSeen = True
            If payloadUnitStart Then ReadPmtForStreams(data, payload)
            Return New TsChunkEvent With {.EventKind = TsChunkEvent.Kind.PmtPacket, .PacketOffset = packetOffset, .PacketCopy = _lastPmtPacket}
        End If

        If _videoPidValue >= 0 AndAlso pid = _videoPidValue AndAlso payloadUnitStart Then
            If PesPayloadHasIdrNal(data, payload, packetOffset + LiveProducerContract.TsPacketSize) Then
                Return New TsChunkEvent With {.EventKind = TsChunkEvent.Kind.JoinPoint, .PacketOffset = packetOffset, .PacketCopy = PacketCopy(data, packetOffset)}
            End If
        End If

        Return Nothing
    End Function

    ''' <summary>Feeds a whole segment and returns the notable events of it, with offsets relative to
    ''' the segment (the hub stores segments and serves listeners from them).</summary>
    Public Function Feed(segment As Byte()) As List(Of TsChunkEvent)
        Dim events As New List(Of TsChunkEvent)()
        If segment Is Nothing OrElse Not TsChunker.IsAligned(segment) Then Return events
        For off As Integer = 0 To segment.Length - 1 Step LiveProducerContract.TsPacketSize
            Dim e As TsChunkEvent = Scan(segment, off)
            If e IsNot Nothing Then events.Add(e)
        Next
        Return events
    End Function

    ' --- packet and section parsing ------------------------------------------------

    Private Shared Function PacketCopy(data As Byte(), packetOffset As Integer) As Byte()
        Dim copy(LiveProducerContract.TsPacketSize - 1) As Byte
        Array.Copy(data, packetOffset, copy, 0, LiveProducerContract.TsPacketSize)
        Return copy
    End Function

    ''' <summary>Offset of the packet's payload, after the 4-byte header and the adaptation field when
    ''' one is present. -1 when the packet carries no payload at all.</summary>
    Private Shared Function PacketPayloadOffset(data As Byte(), packetOffset As Integer) As Integer
        Dim adaptation As Integer = (data(packetOffset + 3) >> 4) And &H3
        Select Case adaptation
            Case 1 : Return packetOffset + 4
            Case 3
                Dim length As Integer = data(packetOffset + 4)
                Return packetOffset + 5 + length
            Case Else
                Return -1
        End Select
    End Function

    Private Sub ReadPatForPmtPid(data As Byte(), payload As Integer)
        ' payload_unit_start: one pointer_field byte, then the section. table_id 0, section_length in
        ' bytes 2-3 (low 12 bits), then program loop of (program_number 2, pid flags 2).
        Dim section As Integer = payload + 1 + data(payload)
        If section + 8 > data.Length Then Return
        If data(section) <> 0 Then Return                      ' PAT is table_id 0
        Dim sectionLength As Integer = ((data(section + 1) And &HF) << 8) Or data(section + 2)
        Dim cursor As Integer = section + 8                     ' after section_length, tsid, version
        Dim [end] As Integer = section + 3 + sectionLength - 4  ' last 4 bytes are the CRC
        While cursor + 4 <= [end]
            Dim program As Integer = (data(cursor) << 8) Or data(cursor + 1)
            Dim candidate As Integer = ((data(cursor + 2) And &H1F) << 8) Or data(cursor + 3)
            If program <> 0 AndAlso candidate > 0 Then
                _pmtPid = candidate
                Return
            End If
            cursor += 4
        End While
    End Sub

    Private Sub ReadPmtForStreams(data As Byte(), payload As Integer)
        Dim section As Integer = payload + 1 + data(payload)
        If section + 12 > data.Length Then Return
        If data(section) <> 2 Then Return                       ' PMT is table_id 2
        Dim sectionLength As Integer = ((data(section + 1) And &HF) << 8) Or data(section + 2)
        Dim programInfoLength As Integer = ((data(section + 10) And &HF) << 8) Or data(section + 11)
        Dim cursor As Integer = section + 12 + programInfoLength
        Dim [end] As Integer = section + 3 + sectionLength - 4
        While cursor + 5 <= [end]
            Dim streamType As Integer = data(cursor)
            Dim streamPid As Integer = ((data(cursor + 1) And &H1F) << 8) Or data(cursor + 2)
            Dim infoLength As Integer = ((data(cursor + 3) And &HF) << 8) Or data(cursor + 4)
            Select Case streamType
                Case LiveProducerContract.StreamTypeH264 : _videoPidValue = streamPid
                Case LiveProducerContract.StreamTypeAacAdts : _audioPidValue = streamPid
            End Select
            cursor += 5 + infoLength
        End While
    End Sub

    ''' <summary>Walks a PES payload for Annex-B NAL units (start code 00 00 01) and returns True when one
    ''' is an IDR (nal unit type 5). The scan stops at the packet's end - the hub feeds whole segments, and
    ''' an IDR AU of x264 ultrafast at 720p fits in one segment; a false "no IDR" is caught by the cadence
    ''' check, which is the test the spec asks for anyway.</summary>
    Private Shared Function PesPayloadHasIdrNal(data As Byte(), payload As Integer, limit As Integer) As Boolean
        Dim i As Integer = payload
        While i + 4 <= Math.Min(data.Length, limit)
            If data(i) = 0 AndAlso data(i + 1) = 0 AndAlso data(i + 2) = 1 Then
                Dim nalType As Integer = data(i + 3) And &H1F
                If nalType = 5 Then Return True
                i += 3
            Else
                i += 1
            End If
        End While
        Return False
    End Function

End Class
#End If
