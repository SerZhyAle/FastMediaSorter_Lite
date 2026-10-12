#if Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199 sections 3, 4, 5, 6: the frozen numbers and names of the PC producer half of a live
' broadcast, in one place. LIVE-BROADCAST 0.14 sections 2.1 and 3 (the [CONTRACT] audio shape) and the
' [PROPOSED] amendment items C, H and I of 13_contract_amendment_2026-10-07.md (the MPEG-TS video shape,
' which binds nobody until the owner flips it and stays behind the gate of LiveBroadcastFeatures).
'
' Pure constants only - no state, so both programs and both test projects can compile this file.

Friend NotInheritable Class LiveProducerContract

    Private Sub New()
    End Sub

    ' --- the AUDIO_ONLY shape of section 2.1 [CONTRACT] ---

    ''' <summary>Default port of the audio listener, the user's setting, no fallback: if it is taken the
    ''' broadcast does not start (199 section 3.1).</summary>
    Public Const DefaultAudioPort As Integer = 8768

    ''' <summary>The one published path of 2.1; the extension-less form is accepted too.</summary>
    Public Const AudioPath As String = "/live-audio.aac"
    Public Const AudioPathAlias As String = "/live-audio"

    Public Const AudioContentType As String = "audio/aac"

    ''' <summary>ADTS AAC-LC, mono for the microphone by default (the shape's own words); system audio is
    ''' stereo and keeping it is the deviation proposal ask 5 asks the owner to bless - until then the
    ''' settings page marks stereo as "not in the written shape" and the gate is shut anyway.</summary>
    Public Const DefaultSampleRate As Integer = 44100
    Public Const DefaultAudioBitrateKbps As Integer = 128
    Public Const MonoChannels As Integer = 1
    Public Const StereoChannels As Integer = 2

    ' --- the VIDEO_AUDIO / VIDEO_ONLY shape of amendment item C, [PROPOSED] ---

    ''' <summary>The MPEG-TS path. Item H also allows an opaque token segment in front; the token is not a
    ''' constant, it is generated per broadcast and kept in the descriptor's URL only.</summary>
    Public Const VideoPath As String = "/live.ts"
    Public Const VideoContentType As String = "video/mp2t"

    ''' <summary>The targetLatencyMs of an HTTP video endpoint (item H): the RTSP shape's 200 is not
    ''' reachable over HTTP.</summary>
    Public Const VideoTargetLatencyMs As Integer = 1000

    ''' <summary>The producer's keyframe interval. The shape asks for at least one keyframe every 2 s; this
    ''' product uses 1 s so a listener joins faster (199 section 3.2).</summary>
    Public Const KeyframeIntervalMs As Integer = 1000

    ''' <summary>Item H: PAT and PMT repeat at least every 250 ms.</summary>
    Public Const PatPmtIntervalMs As Integer = 250

    ''' <summary>The cadence limit the tests flag: a stream with no IDR inside 2 s is a producer defect.</summary>
    Public Const KeyframeGapLimitMs As Integer = 2000

    ' --- the hub, section 3.5 ---

    ''' <summary>Safety rule 3 (no recording): nothing a broadcast carries is written to disk; the hub keeps
    ''' two seconds in memory.</summary>
    Public Const HubRetentionMs As Integer = 2000

    ''' <summary>The write deadline of the hub's listeners (3.5): a listener that cannot keep up is dropped
    ''' forward to the joinable live edge, never allowed to stall the others.</summary>
    Public Const ListenerWriteDeadlineMs As Integer = 2000

    ' --- the descriptor and the hand-off channels, sections 3.1, 3.2 and 5 ---

    Public Const DescriptorSchemaVersion As Integer = 1
    Public Const FileExtension As String = ".fmsbcast"
    Public Const FileMime As String = "application/vnd.fms.bcast+json"

    ''' <summary>Must match the consumer's prefix byte for byte (023 reader; LIVE-BROADCAST 4.1).</summary>
    Public Const BarcodePrefix As String = "FMSBCAST1:"

    ''' <summary>Where the random id of an unenrolled device is kept: the app's registry store
    ''' HKCU\Software\SZA\FastMediaSorter (199 section 5).</summary>
    Public Const SourceIdRegistryValue As String = "LiveBroadcast_SourceId"

    ' --- sourceId amendment item G (0.15): one broadcast at a time, so one id ---

    Public Const SourceSuffixMicrophone As String = "mic"
    Public Const SourceSuffixSystemAudio As String = "system-audio"
    Public Const SourceSuffixScreen As String = "screen"
    Public Const SourceSuffixCamera As String = "camera"

    ' --- the MPEG-TS transport stream numbers the chunker needs ---

    Public Const TsPacketSize As Integer = 188
    Public Const TsSyncByte As Byte = &H47
    Public Const PatPid As Integer = 0
    ''' <summary>Stream type of H.264 in a PMT (ITU-T H.222 table 2-33).</summary>
    Public Const StreamTypeH264 As Integer = &H1B
    ''' <summary>Stream type of AAC ADTS audio in a PMT.</summary>
    Public Const StreamTypeAacAdts As Integer = &HF

End Class
#End If
