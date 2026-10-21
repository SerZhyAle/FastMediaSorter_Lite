#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' What the modern decoder actually produces, before GDI+ is asked to make an
''' <see cref="System.Drawing.Image"/> out of it
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §3.1).
'''
''' The decoder has always built these bytes - a transcoded GIF for an animation, a
''' re-encoded PNG for a still - and has always thrown them away with the Image. Naming
''' the intermediate is what lets the cache keep it: the payload IS the cache entry, so
''' the hit path and the miss path end in the very same call,
''' <c>Image.FromStream(New MemoryStream(bytes))</c>, and cannot drift apart.
'''
''' Deliberately NOT a member of <see cref="IImageDecoder"/>: that interface is shared by
''' both builds, and the legacy WIC decoder would have to implement a method it has no use
''' for. A second, optional interface implemented by the modern decoder only says exactly
''' as much as is true.
'''
''' Whole-file modern-only, like everything else the cache is made of.
''' </summary>
Friend Enum DecodedPayloadKind
    ''' <summary>A still frame, re-encoded to PNG (ImageSharp or Magick.NET).</summary>
    Png
    ''' <summary>A multi-frame animation transcoded to GIF, frame delays carried across.</summary>
    Gif
End Enum

Friend NotInheritable Class DecodedPayload

    ''' <summary>The encoded bytes themselves - a whole PNG or a whole GIF file.</summary>
    Public Property Bytes As Byte()

    Public Property Kind As DecodedPayloadKind

    ''' <summary>True for a multi-frame source. Rides in the payload rather than being
    ''' re-derived from <see cref="Kind"/> because it is what the storage policy asks
    ''' about (§6.1: an animation is always worth keeping, a still only when it was
    ''' slow), and a policy that had to sniff the bytes to answer would be a second
    ''' decode.</summary>
    Public Property IsAnimation As Boolean

    ''' <summary>How long producing these bytes took. The only input the policy has for a
    ''' still image - the whole question there is "was this slow enough to be worth a
    ''' file".</summary>
    Public Property DecodeMs As Long

    ''' <summary>The source canvas of a multi-frame decode and its frame count, 0 when the
    ''' decoder did not say (a still, a cache hit). Diagnostics only - the "Animation
    ''' prepared" log line (spec 241 rule 6) - and never part of what the cache stores.</summary>
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Frames As Integer

    ''' <summary>Milliseconds spent in the GIF encode alone, 0 when there was none. The rest of
    ''' <see cref="DecodeMs"/> is the decode (spec 241 rule 6).</summary>
    Public Property EncodeMs As Long

    ''' <summary>True when the GIF encode dithered (spec 241 rule 3).</summary>
    Public Property Dithered As Boolean

    ''' <summary>True for the first frame of an animation that is still being prepared (spec
    ''' 241 rule 10): a plain picture standing in for the animation until the whole GIF
    ''' lands. Never stored in the decode cache.</summary>
    Public Property IsPreview As Boolean

    ''' <summary>The animation's length by the delays in its header, in ms; 0 when the header does not
    ''' say. Set on a first-frame payload so "Replace with video" can plan its frame rate for an
    ''' animation that is not played (spec 241 rule 18, Q241-5).</summary>
    Public Property DurationMs As Long

    ''' <summary>True when the animation carries transparency, by its header (spec 241 rule 18).</summary>
    Public Property HasAlpha As Boolean

    ''' <summary>True when the animation is over the decode budget: the payload is its first frame and
    ''' there will be no whole-animation stage (spec 241 rule 18).</summary>
    Public Property IsTooLong As Boolean

End Class

''' <summary>
''' Implemented by the modern decoder only. A decoder that does not implement it simply
''' never gets a cache - the caller falls back to <see cref="IImageDecoder.DecodeToImage"/>,
''' which is exactly today's behaviour.
''' </summary>
Friend Interface IImageDecoderPayload

    ''' <summary>
    ''' Decodes into the encoded payload, without building a GDI+ image. The input stream
    ''' stays owned by the caller. Returns Nothing when the data cannot be decoded.
    ''' <paramref name="sourceLabel"/> is the file's path when the caller has one; it only
    ''' names the file in the log lines the decoder writes (spec 241 rule 7).
    ''' </summary>
    Function DecodeToPayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload

    ''' <summary>
    ''' Stage 1 of a long animation (spec 241 rules 10 and 18): for a multi-frame file read by ImageSharp,
    ''' only its FIRST frame, as a PNG flagged <see cref="DecodedPayload.IsPreview"/>, with the canvas
    ''' size, the frame count and the header's duration filled in. An animation over the budget gets
    ''' the same (<see cref="DecodedPayload.IsTooLong"/>); a frame over the budget gets Nothing.
    ''' A still, and every Magick route, is answered exactly as <see cref="DecodeToPayload"/> would.
    ''' </summary>
    Function DecodeFirstFramePayload(stream As MemoryStream, Optional sourceLabel As String = "") As DecodedPayload

End Interface
#End If
