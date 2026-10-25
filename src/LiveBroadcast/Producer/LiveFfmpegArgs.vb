#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Text

' Ticket 199 section 4, the engine of the working assumption (Q199-1, candidate a): FFmpeg as a child
' process fed raw audio (and video) over pipes and writing ADTS or MPEG-TS to its output. Pure
' argument building, so the suite holds the flags the specification names without a process:
'   - the native AAC encoder at the shape's rate and bitrate;
'   - x264 ultrafast, zerolatency, no B-frames (item C's latency shape);
'   - -flush_packets 1 and a short mux delay, PAT period 0.1 (item H's 250 ms with margin);
'   - the keyframe interval of 1 s (199 section 3.2: the shape asks 2 s, this product uses 1 s so a
'     listener joins faster).

Friend NotInheritable Class LiveFfmpegArgs

    Private Sub New()
    End Sub

    ''' <summary>The AUDIO_ONLY pipeline: raw s16le PCM on stdin, ADTS AAC-LC on stdout.</summary>
    Public Shared Function AudioToAdts(options As LiveEncoderOptions) As String
        Dim args As New StringBuilder()
        args.Append("-hide_banner -loglevel error -nostdin")
        args.Append(" -f s16le -ar ").Append(options.EffectiveInputRate.ToString(Globalization.CultureInfo.InvariantCulture))
        args.Append(" -ac ").Append(options.EffectiveInputChannels.ToString(Globalization.CultureInfo.InvariantCulture))
        args.Append(" -i pipe:0")
        ' The output carries the shape's own numbers (2.1): the engine resamples whatever the device
        ' gave (a 48 000 Hz stereo mix format) down to the shape's rate and channel count.
        args.Append(" -ar ").Append(options.SampleRate.ToString(Globalization.CultureInfo.InvariantCulture))
        args.Append(" -ac ").Append(options.Channels.ToString(Globalization.CultureInfo.InvariantCulture))
        args.Append(" -c:a aac -b:a ").Append(options.AudioBitrateKbps.ToString(Globalization.CultureInfo.InvariantCulture)).Append("k")
        args.Append(" -f adts -flush_packets 1 pipe:1")
        Return args.ToString()
    End Function

    ''' <summary>The VIDEO_AUDIO / VIDEO_ONLY pipeline: raw BGRA video frames on stdin, AAC audio on the
    ''' second input (a Windows named pipe the wrapper owns), MPEG-TS on stdout. Set options.WithAudio
    ''' False for VIDEO_ONLY - then the audio input is not opened at all. The INPUT size is what the
    ''' capture produces (a monitor's own size); when it differs from the shape's numbers the engine
    ''' scales with its own filter - section 4's "scaled to at most 1280x720" happens in the engine,
    ''' where scaling is native code, not on the capture thread.</summary>
    Public Shared Function VideoToMpegts(options As LiveEncoderOptions, audioPipePath As String) As String
        Dim args As New StringBuilder()
        args.Append("-hide_banner -loglevel error -nostdin")
        args.Append(" -f rawvideo -pix_fmt bgra")
        args.Append(" -s ").Append(options.EffectiveInputVideoWidth).Append("x").Append(options.EffectiveInputVideoHeight)
        args.Append(" -r ").Append(options.VideoFps.ToString(Globalization.CultureInfo.InvariantCulture))
        args.Append(" -i pipe:0")
        If options.WithAudio Then
            args.Append(" -f s16le -ar ").Append(options.EffectiveInputRate.ToString(Globalization.CultureInfo.InvariantCulture))
            args.Append(" -ac ").Append(options.EffectiveInputChannels.ToString(Globalization.CultureInfo.InvariantCulture))
            args.Append(" -i ").Append(audioPipePath)
        End If

        ' The scale filter exists only when the capture is not already the shape's size - a fake
        ' source producing 1280x720 must keep the exact argument string of the shape's own numbers.
        If options.EffectiveInputVideoWidth <> options.VideoWidth OrElse options.EffectiveInputVideoHeight <> options.VideoHeight Then
            args.Append(" -vf scale=").Append(options.VideoWidth).Append(":").Append(options.VideoHeight)
            ' Ticket 263 T-2: a window is any shape (a 4:3 tool, a tall chat); a plain scale to 16:9 would
            ' stretch it. An input of another aspect is scaled to fit and padded with black bars; a 16:9
            ' monitor (within one percent) keeps the plain scale and its exact argument string.
            If Not SameAspect(options) Then
                args.Append(":force_original_aspect_ratio=decrease,pad=").Append(options.VideoWidth).Append(":").Append(options.VideoHeight)
                args.Append(":(ow-iw)/2:(oh-ih)/2")
            End If
        End If
        args.Append(" -c:v libx264 -preset ultrafast -tune zerolatency -bf 0")
        args.Append(" -g ").Append(GopSize(options))
        args.Append(" -b:v ").Append(options.VideoBitrateKbps.ToString(Globalization.CultureInfo.InvariantCulture)).Append("k")
        If options.WithAudio Then
            args.Append(" -c:a aac -b:a ").Append(options.AudioBitrateKbps.ToString(Globalization.CultureInfo.InvariantCulture)).Append("k")
        End If

        ' The mux numbers of the shape: short mux delay, PAT/PMT period 0.1 s (item H asks at most
        ' 250 ms), packets flushed as they are produced.
        args.Append(" -f mpegts -muxdelay 0.1 -pat_period 0.1 -flush_packets 1 pipe:1")
        Return args.ToString()
    End Function

    ''' <summary>True when the capture's shape is the output shape's within one percent (a 1920x1080, 2560x1440 or
    ''' 1366x768 monitor against 1280x720): the plain scale distorts nothing visible. Cross-multiplied, no division.</summary>
    Public Shared Function SameAspect(options As LiveEncoderOptions) As Boolean
        Dim inputProduct As Long = CLng(options.EffectiveInputVideoWidth) * options.VideoHeight
        Dim outputProduct As Long = CLng(options.EffectiveInputVideoHeight) * options.VideoWidth
        Return Math.Abs(inputProduct - outputProduct) * 100L <= outputProduct
    End Function

    ''' <summary>The GOP size in frames: exactly the 1 s keyframe interval of section 3.2.</summary>
    Public Shared Function GopSize(options As LiveEncoderOptions) As Integer
        Return Math.Max(1, options.VideoFps * (LiveProducerContract.KeyframeIntervalMs \ 1000))
    End Function

End Class
#End If
