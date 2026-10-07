#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 6.3.
' The numbers a live session runs with, in one testable place. Pure: no LibVLC, no network.

''' <summary>
''' Per-session knobs of a live broadcast. They are handed to ONE session only; the global LibVLC
''' defaults and every on-demand media keep theirs (spec 023 section 6.3).
''' </summary>
Friend NotInheritable Class LivePlaybackOptions

    ''' <summary>Used when neither the endpoint nor the descriptor names a target (section 6.3).</summary>
    Public Const DefaultTargetLatencyMs As Integer = 1000
    ''' <summary>A direct HTTP target is clamped to this range (section 6.3).</summary>
    Public Const MinTargetLatencyMs As Integer = 250
    Public Const MaxTargetLatencyMs As Integer = 2000

    ''' <summary>
    ''' The player-side cache. A START value to be measured, not an answer: 023 Phi-L1 records
    ''' the figure that meets "first sound within 1 s, no more than 2 s behind" on the real stack
    ''' (the measured table is in the specification, section 6.3).
    ''' </summary>
    Public Property CachingMs As Integer = 300

    ''' <summary>The headers deadline of the single GET (section 6.2 item 1).</summary>
    Public Property ConnectTimeoutMs As Integer = 2000

    ''' <summary>No bytes for this long closes the response and counts as a stall (section 6.2 item 5).</summary>
    Public Property StallTimeoutMs As Integer = 3000

    ''' <summary>How often the watchdog looks; it opens no connection (section 6.2 item 5).</summary>
    Public Property WatchdogPeriodMs As Integer = 250

    ''' <summary>
    ''' The effective latency target: the value the descriptor gave (endpoint first, then top level),
    ''' otherwise 1000 ms, clamped to 250..2000 ms for a direct HTTP endpoint.
    ''' </summary>
    Public Shared Function EffectiveTargetMs(targetLatencyMs As Long?) As Integer
        If Not targetLatencyMs.HasValue Then Return DefaultTargetLatencyMs
        Dim value As Long = targetLatencyMs.Value
        If value < MinTargetLatencyMs Then Return MinTargetLatencyMs
        If value > MaxTargetLatencyMs Then Return MaxTargetLatencyMs
        Return CInt(value)
    End Function

    ''' <summary>
    ''' The cache actually asked of the player: the start value, never more than the target
    ''' (a producer that asks for 250 ms must not get a 300 ms cache).
    ''' </summary>
    Public Function CachingFor(targetLatencyMs As Long?) As Integer
        Return Math.Min(CachingMs, EffectiveTargetMs(targetLatencyMs))
    End Function

    ''' <summary>
    ''' When set, replaces the computed list entirely. Only the measurement harness uses it, to try option variants against
    ''' the real LibVLC without recompiling; the viewer never sets it.
    ''' </summary>
    Public Property OverrideOptions As String()

    ''' <summary>
    ''' The per-media options. Seek, resume and input-repeat are never among them (section 6.3).
    ''' <para>
    ''' <paramref name="adtsAac"/> says the response declared an ADTS AAC body (<c>audio/aac</c>, section 2.1/2.2 of the
    ''' contract): LibVLC is then told which demuxer to use. Measured on LibVLC 3.0.21 (specification 023, section 6.3):
    ''' left to probe a custom input it tries 49 demuxers and lets libavformat analyse the stream, which at 8-16 KB/s of
    ''' live audio takes about 3.9 s - first sound at 4.3 s, 4.4 s behind the source. <c>:demux=aac</c> skips all of it:
    ''' first sound at about 1.0 s, about 0.75 s behind. A stream that is not AAC (a marked-live radio address, say) is
    ''' left to the probe: forcing the wrong demuxer would play nothing.
    ''' </para>
    ''' <para>
    ''' The cache options are the documented knobs, but on this input path they measurably do nothing (100 to 300 ms all
    ''' give the same figures; the input's own delay is a fixed 300 ms), and <c>clock-jitter=0</c>/<c>clock-synchro=0</c>,
    ''' which an earlier draft carried, made it about 300 ms worse - so they are not here.
    ''' </para>
    ''' </summary>
    Public Function ToMediaOptions(targetLatencyMs As Long?, Optional adtsAac As Boolean = False) As String()
        If OverrideOptions IsNot Nothing Then Return OverrideOptions
        Dim cache As Integer = CachingFor(targetLatencyMs)
        Dim list As New List(Of String)()
        If adtsAac Then list.Add(":demux=aac")
        list.Add(":network-caching=" & cache.ToString(Globalization.CultureInfo.InvariantCulture))
        list.Add(":live-caching=" & cache.ToString(Globalization.CultureInfo.InvariantCulture))
        Return list.ToArray()
    End Function

End Class
#End If
