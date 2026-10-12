#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199 section 4: the sources and the engine. Every capture (microphone, system audio, screen,
' camera) sits behind ILiveAudioSource and ILiveVideoSource so the suite drives fakes - no test needs
' a device - and the encoder sits behind ILiveEncoder for the same reason.
'
' The engine itself is chosen by the spike (Q199-1); the working assumption of the specification is
' candidate (a): FFmpeg as a child process fed raw audio and video over pipes and writing ADTS or
' MPEG-TS to its output - the pinned FFmpeg runtime the viewer already downloads on first use
' (OptionalRuntimeManager: versioned, SHA-256 pinned, about 110 MB, GPL, not bundled). The argument
' builders of LiveFfmpegArgs are pure and tested; the process wrapper (the Share Manager's
' LiveFfmpegEncoder) is the only piece that is not.

''' <summary>Raw PCM as WASAPI captured it: 16-bit little-endian interleaved, the number of frames
''' (samples per channel), the stream's rate and channel count fixed at start.</summary>
Friend Class LivePcmChunk
    Public Property Data As Byte()
    Public Property Frames As Integer
    ''' <summary>The peak absolute sample of this chunk (0..1), for the live level meter. The gain is
    ''' applied by the producer (consumer rule 6), so the meter shows the level AFTER the gain.</summary>
    Public Property Peak As Double
End Class

''' <summary>An audio capture (microphone or system-audio loopback). The implementation owns its own
''' capture thread and pushes chunks to the sink; Start returns False (with a reason) when Windows
''' does not grant the device - the privacy switches decide, and the product asks for nothing the OS
''' does not let it have (safety rule 4).</summary>
Friend Interface ILiveAudioSource
    ''' <summary>The human name of the picked device (the window's source line).</summary>
    ReadOnly Property DisplayName As String
    ReadOnly Property SampleRate As Integer
    ReadOnly Property Channels As Integer

    ''' <summary>Starts the capture. The sink receives one chunk per capture period.</summary>
    Function Start(sink As Action(Of LivePcmChunk), ByRef reason As String) As Boolean

    ''' <summary>Stops the capture and releases the device. Idempotent.</summary>
    Sub [Stop]()

    ''' <summary>Mute: the source keeps capturing (the meter keeps working) but pushes silence.</summary>
    Property Muted As Boolean

    ''' <summary>The producer's gain, 1.0 = the device's own level (section 4: a quiet microphone is a
    ''' producer defect and the consumer adds none).</summary>
    Property Gain As Double
End Interface

''' <summary>A video capture (screen now, camera in phase B3). Frames arrive as raw BGRA rows
''' bottom-up-or-top as the implementation declares, plus the frame interval the encoder is told.</summary>
Friend Class LiveVideoFrame
    Public Property Pixels As Byte()
    Public Property Width As Integer
    Public Property Height As Integer
End Class

Friend Interface ILiveVideoSource
    ReadOnly Property DisplayName As String
    ReadOnly Property Width As Integer
    ReadOnly Property Height As Integer
    ReadOnly Property FramesPerSecond As Integer

    Function Start(sink As Action(Of LiveVideoFrame), ByRef reason As String) As Boolean
    Sub [Stop]()
End Interface

''' <summary>What an encoder is asked to produce (section 3's numbers).</summary>
Friend Class LiveEncoderOptions
    ''' <summary>The SHAPE's numbers, what the descriptor and the icy headers state.</summary>
    Public Property SampleRate As Integer = LiveProducerContract.DefaultSampleRate
    Public Property Channels As Integer = LiveProducerContract.MonoChannels
    ''' <summary>The CAPTURE's numbers, what the source actually produces (WASAPI's mix format is
    ''' whatever the device answers - usually 48 000 Hz stereo). The engine resamples to the shape's
    ''' numbers; a fake source simply declares its own.</summary>
    Public Property InputSampleRate As Integer = 0
    Public Property InputChannels As Integer = 0
    Public Property AudioBitrateKbps As Integer = LiveProducerContract.DefaultAudioBitrateKbps
    ''' <summary>The MPEG-TS shape's numbers, used only by the video encoders (item C/H: PAT/PMT 250 ms,
    ''' this product's 1 s keyframe, x264 ultrafast zerolatency, no B-frames).</summary>
    Public Property VideoWidth As Integer = 1280
    Public Property VideoHeight As Integer = 720
    ''' <summary>The CAPTURE's video size, what the screen source actually produces (a monitor is
    ''' whatever it is - 1920x1080 more often than not). The engine scales to the shape's numbers;
    ''' a fake source simply declares nothing and produces exactly the shape's own size.</summary>
    Public Property InputVideoWidth As Integer = 0
    Public Property InputVideoHeight As Integer = 0
    Public Property VideoFps As Integer = 25
    Public Property VideoBitrateKbps As Integer = 2000
    Public Property WithVideo As Boolean = False
    Public Property WithAudio As Boolean = True

    ''' <summary>The input rate the encoder is told, falling back to the shape's own (a fake that
    ''' declares nothing produces exactly the shape's PCM).</summary>
    Public ReadOnly Property EffectiveInputRate As Integer
        Get
            Return If(InputSampleRate > 0, InputSampleRate, SampleRate)
        End Get
    End Property

    Public ReadOnly Property EffectiveInputChannels As Integer
        Get
            Return If(InputChannels > 0, InputChannels, Channels)
        End Get
    End Property

    Public ReadOnly Property EffectiveInputVideoWidth As Integer
        Get
            Return If(InputVideoWidth > 0, InputVideoWidth, VideoWidth)
        End Get
    End Property

    Public ReadOnly Property EffectiveInputVideoHeight As Integer
        Get
            Return If(InputVideoHeight > 0, InputVideoHeight, VideoHeight)
        End Get
    End Property
End Class

''' <summary>The encoder engine seam. The output sink receives whole ADTS frames (AUDIO_ONLY) or
''' MPEG-TS segments (the video modes) - whatever the hub's join policy of the mode expects.</summary>
Friend Interface ILiveEncoder
    ''' <summary>Starts the engine. False + reason when the runtime is missing (the pinned FFmpeg is
    ''' downloaded on demand and may not be on disk yet).</summary>
    Function Start(options As LiveEncoderOptions, output As Action(Of Byte()), ByRef reason As String) As Boolean

    ''' <summary>Raw PCM in, from the audio source.</summary>
    Sub WritePcm(chunk As LivePcmChunk)

    ''' <summary>A raw video frame in, from the video source.</summary>
    Sub WriteVideo(frame As LiveVideoFrame)

    ''' <summary>Ends the process and closes the pipes. Idempotent.</summary>
    Sub [Stop]()
End Interface
#End If
