#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' How the animation -> GIF transcode is encoded (spec 241 rules 3 and 4).
'''
''' The default <c>GifEncoder</c> quantizes every frame with Floyd-Steinberg dithering, about
''' 46 to 53 ns per pixel of the whole animation; without dither it is about 17 ns. On a
''' 368 Mpx animation that is 17.1 s against 6.3 s, which is the difference between a file
''' that opens and one the 20 s display deadline abandons. Dithering buys smoother gradients
''' in a 256-colour frame, so it is kept wherever it is cheap (up to
''' <see cref="Dither_Max_Total_Pixels"/>, where the payload is byte-identical to the one
''' produced before this policy existed) and dropped above it.
'''
''' Pure, so the boundary is unit-testable without encoding anything.
''' </summary>
Friend Module GifTranscodePolicy

    ''' <summary>Total pixels over all frames up to which the transcode dithers. At the
    ''' threshold the worst case is about 100 Mpx x 53 ns = 5 s.</summary>
    Friend Const Dither_Max_Total_Pixels As Long = 100L * 1000L * 1000L

    ''' <summary>
    ''' True when an animation of <paramref name="width"/> x <paramref name="height"/> with
    ''' <paramref name="frames"/> frames is encoded with dither. A size that says nothing
    ''' sane counts as no pixels, so it keeps today's encoder. Never overflows, whatever the
    ''' header claims.
    ''' </summary>
    Friend Function UseDither(width As Long, height As Long, frames As Long) As Boolean
        If width <= 0L OrElse height <= 0L OrElse frames <= 0L Then Return True
        ' Each side at most the threshold, so the product below stays far inside a Long.
        If width > Dither_Max_Total_Pixels OrElse height > Dither_Max_Total_Pixels Then Return False
        Dim framePixels As Long = width * height
        If framePixels > Dither_Max_Total_Pixels Then Return False
        ' Division instead of framePixels * frames, as DecodeBudget does.
        Return frames <= Dither_Max_Total_Pixels \ framePixels
    End Function

End Module
#End If
