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
    ''' </summary>
    Function DecodeToPayload(stream As MemoryStream) As DecodedPayload

End Interface
#End If
