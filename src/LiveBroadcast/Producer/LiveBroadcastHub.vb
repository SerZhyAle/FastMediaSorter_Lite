#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199 section 3.5: one fan-out inside the program. The encoder's output goes to a small
' LiveBroadcastHub - a managed twin of the server's ring (165): header bytes for a new listener,
' joinable starts, a per-listener cursor, a write deadline, drop-never-stall. LAN listeners and (173)
' the relay uploader are its consumers.
'
' The hub knows nothing about sockets or encoding formats: a join policy classifies each segment
' (where a listener may start, what header precedes the start) and the hub keeps segments for two
' seconds in memory (safety rule 3: no recording, nothing a broadcast carries is written to disk).
' The clock is injected, so the slow-listener test drives time by hand.
'
' Cursors address segments by the hub's append sequence, not by list position, so pruning (which
' happens on the producer's thread) can never make a cursor read the wrong segment: a cursor whose
' segment aged out is skipped to the live edge instead - drop-never-stall.

''' <summary>What a join policy saw in one segment.</summary>
Friend Class LiveJoinInfo
    ''' <summary>Offsets (relative to the segment) where a listener may start. Empty when the segment
    ''' holds no joinable cut - a video listener then waits for the next keyframe, an audio listener
    ''' for the next frame.</summary>
    Public ReadOnly Property JoinOffsets As New List(Of Integer)()

    ''' <summary>The header bytes that precede a start inside this segment: for MPEG-TS the latest PAT
    ''' and PMT packets (item C: "a new connection starts at the next keyframe, preceded by PAT and
    ''' PMT"); Nothing when the format needs none (ADTS: every frame carries its own header).</summary>
    Public Property HeaderBytes As Byte()
End Class

''' <summary>Where a new listener may start in a segment, and what precedes the start. Implementations:
''' <see cref="LiveAudioJoinPolicy"/> (every ADTS frame) and <see cref="LiveVideoJoinPolicy"/> (IDR
''' access-unit starts, PAT and PMT in front).</summary>
Friend Interface ILiveJoinPolicy
    Function Classify(segment As Byte()) As LiveJoinInfo
End Interface

''' <summary>The AUDIO_ONLY policy of 2.1: every frame carries its own ADTS header, so every frame
''' boundary is a joinable start and a late joiner decodes from the next frame. No header bytes.</summary>
Friend Class LiveAudioJoinPolicy
    Implements ILiveJoinPolicy

    Public Function Classify(segment As Byte()) As LiveJoinInfo Implements ILiveJoinPolicy.Classify
        Dim info As New LiveJoinInfo With {.HeaderBytes = Nothing}
        If segment Is Nothing OrElse segment.Length = 0 Then Return info
        Dim offset As Integer = LiveAdtsFrames.FirstFrameAt(segment, 0)
        While offset >= 0
            info.JoinOffsets.Add(offset)
            Dim h As LiveAdtsFrames.AdtsHeader = LiveAdtsFrames.ReadHeader(segment, offset)
            offset = LiveAdtsFrames.FirstFrameAt(segment, offset + h.FrameLengthBytes)
        End While
        Return info
    End Function

End Class

''' <summary>The MPEG-TS policy of item C: a listener starts at an IDR access-unit start, preceded by
''' the latest PAT and PMT packets the state has seen.</summary>
Friend Class LiveVideoJoinPolicy
    Implements ILiveJoinPolicy

    Private ReadOnly _state As New TsChunkerState()

    ''' <summary>The chunker state, for the cadence checks of section 8 (PAT/PMT seen, pids).</summary>
    Public ReadOnly Property State As TsChunkerState
        Get
            Return _state
        End Get
    End Property

    Public Function Classify(segment As Byte()) As LiveJoinInfo Implements ILiveJoinPolicy.Classify
        ' Feed scans in packet order, so by the time a join point is seen the PAT/PMT packets in front
        ' of it (the muxer writes them ahead of the keyframe it belongs to) are already in the state.
        Dim events As List(Of TsChunkEvent) = _state.Feed(segment)
        Dim info As New LiveJoinInfo()
        For Each e As TsChunkEvent In events
            If e.EventKind = TsChunkEvent.Kind.JoinPoint Then info.JoinOffsets.Add(e.PacketOffset)
        Next
        info.HeaderBytes = _state.HeaderBlock()
        Return info
    End Function

End Class

''' <summary>One stored segment of the ring.</summary>
Friend Class LiveHubSegment
    Public Property Seq As Long
    Public Property Bytes As Byte()
    Public Property TimeMs As Long
    Public Property Info As LiveJoinInfo
End Class

''' <summary>A listener's cursor into the hub. Created by <see cref="LiveBroadcastHub.Attach"/>; pulled
''' by the listener's own thread. A cursor never stalls the producer: when the data it still owes has
''' aged out of the ring it skips forward to a joinable cut and says so through
''' <see cref="SkippedForward"/>.</summary>
Friend Class LiveHubCursor

    Private ReadOnly _hub As LiveBroadcastHub
    Private _pendingStart As Boolean = True
    Private _atSeq As Long
    Private _atOffset As Integer
    Private _afterSeq As Long
    Private _detached As Boolean
    Private _skippedForward As Boolean

    Friend Sub New(hub As LiveBroadcastHub, afterSeq As Long)
        _hub = hub
        _afterSeq = afterSeq
        _atSeq = afterSeq
    End Sub

    ''' <summary>True when the last pull skipped data this listener could not keep up with. The server
    ''' resets the consumer cleanly at the next joinable cut rather than replaying a gap.</summary>
    Public ReadOnly Property SkippedForward As Boolean
        Get
            Return _skippedForward
        End Get
    End Property

    Public ReadOnly Property Detached As Boolean
        Get
            Return _detached
        End Get
    End Property

    Public Sub Detach()
        _detached = True
        _hub.RemoveCursor(Me)
    End Sub

    Friend Sub MarkDetached()
        _detached = True
    End Sub

    ''' <summary>The next bytes for this listener, or Nothing when it is caught up (or, at the start,
    ''' waiting for the next joinable cut). Never throws; never blocks the producer.</summary>
    Public Function Pull() As Byte()
        If _detached Then Return Nothing
        SyncLock _hub.Lock
            If _detached Then Return Nothing

            ' A fresh listener waits for the next joinable cut that arrived after it attached - the
            ' live edge, never a replay (contract 5.2).
            If _pendingStart Then
                Dim segment As LiveHubSegment = _hub.FirstJoinableAfter(_afterSeq)
                If segment Is Nothing Then Return Nothing
                _pendingStart = False
                _atSeq = segment.Seq + 1
                _atOffset = 0
                Return HeaderPlus(segment, segment.Info.JoinOffsets(0))
            End If

            Dim current As LiveHubSegment = _hub.SegmentBySeq(_atSeq)
            If current Is Nothing Then
                ' The segment aged out while this listener was slow: skip to the oldest joinable cut
                ' still in the ring (or wait for the next one), never stall the producer or replay.
                _skippedForward = True
                Dim fresh As LiveHubSegment = _hub.FirstJoinableAfter(_atSeq)
                If fresh Is Nothing Then
                    _pendingStart = True
                    _afterSeq = _hub.LastSeq
                    Return Nothing
                End If
                _atSeq = fresh.Seq + 1
                _atOffset = 0
                Return HeaderPlus(fresh, fresh.Info.JoinOffsets(0))
            End If

            Dim data As Byte() = Tail(current, _atOffset)
            _atSeq += 1
            _atOffset = 0
            Return data
        End SyncLock
    End Function

    Private Function HeaderPlus(segment As LiveHubSegment, fromOffset As Integer) As Byte()
        Dim header As Byte() = segment.Info.HeaderBytes
        Dim body As Byte() = Tail(segment, fromOffset)
        If header Is Nothing OrElse header.Length = 0 Then Return body
        If body Is Nothing Then Return header
        Dim combined(header.Length + body.Length - 1) As Byte
        Array.Copy(header, 0, combined, 0, header.Length)
        Array.Copy(body, 0, combined, header.Length, body.Length)
        Return combined
    End Function

    Private Function Tail(segment As LiveHubSegment, fromOffset As Integer) As Byte()
        If segment Is Nothing OrElse segment.Bytes Is Nothing Then Return Nothing
        If fromOffset >= segment.Bytes.Length Then Return Nothing
        Dim length As Integer = segment.Bytes.Length - fromOffset
        Dim data(length - 1) As Byte
        Array.Copy(segment.Bytes, fromOffset, data, 0, length)
        Return data
    End Function

End Class

''' <summary>The fan-out hub: the encoder appends, the listeners pull. Drop-never-stall: appending is
''' O(1) for the producer whatever the listeners do; retention is two seconds of memory.</summary>
Friend Class LiveBroadcastHub

    Private ReadOnly _policy As ILiveJoinPolicy
    Private ReadOnly _clockMs As Func(Of Long)
    Private ReadOnly _retentionMs As Integer
    Private ReadOnly _segments As New List(Of LiveHubSegment)()
    Private ReadOnly _bySeq As New Dictionary(Of Long, LiveHubSegment)()
    Private ReadOnly _cursors As New List(Of LiveHubCursor)()
    Private ReadOnly _lock As New Object()
    Private _lastSeq As Long

    ''' <summary>The one lock: the producer appends under it, the listeners pull under it. Nothing in
    ''' here blocks on I/O, so the lock is held for array copies only.</summary>
    Public ReadOnly Property Lock As Object
        Get
            Return _lock
        End Get
    End Property

    Public ReadOnly Property LastSeq As Long
        Get
            Return _lastSeq
        End Get
    End Property

    ''' <summary>How many segments the ring holds right now - the test's proof that retention is the
    ''' two seconds of safety rule 3 and not the whole broadcast.</summary>
    Public ReadOnly Property RetainedSegments As Integer
        Get
            SyncLock _lock
                Return _segments.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>The number of listeners attached right now (the window's "how many listeners" line).</summary>
    Public ReadOnly Property ListenerCount As Integer
        Get
            SyncLock _lock
                Return _cursors.Count
            End SyncLock
        End Get
    End Property

    Public Sub New(policy As ILiveJoinPolicy, clockMs As Func(Of Long), Optional retentionMs As Integer = LiveProducerContract.HubRetentionMs)
        _policy = policy
        _clockMs = clockMs
        _retentionMs = retentionMs
    End Sub

    ''' <summary>The encoder's output. Classifies the segment, stores it under the injected clock's now,
    ''' prunes what fell out of the retention window (never the newest segment), and returns. The
    ''' producer thread is never made to wait on a listener.</summary>
    Public Sub Append(data As Byte())
        If data Is Nothing OrElse data.Length = 0 Then Return
        Dim nowMs As Long = _clockMs()
        SyncLock _lock
            _lastSeq += 1
            Dim seq As Long = _lastSeq
            Dim segment As New LiveHubSegment With {
                .Seq = seq,
                .Bytes = data,
                .TimeMs = nowMs,
                .Info = _policy.Classify(data)
            }
            _segments.Add(segment)
            _bySeq(seq) = segment

            Dim keepFromMs As Long = nowMs - _retentionMs
            While _segments.Count > 1 AndAlso _segments(0).TimeMs < keepFromMs
                Dim dropped As LiveHubSegment = _segments(0)
                _segments.RemoveAt(0)
                _bySeq.Remove(dropped.Seq)
            End While
        End SyncLock
    End Sub

    ''' <summary>A new listener. It receives the header bytes and starts at the NEXT joinable cut
    ''' (contract 5.2: start at the live edge, never replay) - an audio listener at the next ADTS
    ''' frame, a video listener at the next keyframe preceded by PAT and PMT.</summary>
    Public Function Attach() As LiveHubCursor
        ' Item I's SHOULD (request an IDR on attach) is not done on purpose: the encoder child reads raw frames from a pipe with a
        ' fixed 1 s GOP and FFmpeg takes no on-demand keyframe command, so the join waits for the next IDR (worst case about 1 s,
        ' inside the budget keyframeIntervalMs + 1000) and a restart of the child to force one would cost the other listeners more.
        SyncLock _lock
            Dim cursor As New LiveHubCursor(Me, _lastSeq)
            _cursors.Add(cursor)
            Return cursor
        End SyncLock
    End Function

    Friend Sub RemoveCursor(cursor As LiveHubCursor)
        SyncLock _lock
            _cursors.Remove(cursor)
        End SyncLock
    End Sub

    Friend Function SegmentBySeq(seq As Long) As LiveHubSegment
        Dim s As LiveHubSegment = Nothing
        If _bySeq.TryGetValue(seq, s) Then Return s
        Return Nothing
    End Function

    ''' <summary>The oldest segment after <paramref name="afterSeq"/> that holds a joinable cut.</summary>
    Friend Function FirstJoinableAfter(afterSeq As Long) As LiveHubSegment
        For Each s As LiveHubSegment In _segments
            If s.Seq > afterSeq AndAlso s.Info.JoinOffsets.Count > 0 Then Return s
        Next
        Return Nothing
    End Function

    ''' <summary>The broadcast ended: every listener is detached, the ring is emptied. "Nothing a
    ''' broadcast carries is written to disk" also means nothing outlives the broadcast in memory.</summary>
    Public Sub Clear()
        SyncLock _lock
            For Each c As LiveHubCursor In _cursors.ToArray()
                c.MarkDetached()
            Next
            _cursors.Clear()
            _segments.Clear()
            _bySeq.Clear()
            _lastSeq = 0
        End SyncLock
    End Sub

End Class
#End If
