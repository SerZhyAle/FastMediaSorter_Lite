Option Strict On

''' <summary>
''' How large a picture is handed to the recognizer, and the lazy set of preprocessed
''' buffers built from it. Pure - no Tesseract, no GDI+ - so the two decisions audit 051
''' found wrong can be held by a test: a tall strip must not be shrunk until its lettering is
''' unreadable (E-05), and a buffer nobody asks for must not be built (E-08).
'''
''' Constant status (`OCR-OVERLAY` rule 13). None of these decides what is DRAWN - they size
''' the engine's input - which is why none carries a two-sided bracket; they are marked
''' plainly rather than given one retroactively. Ticket 033 owns measuring them.
''' | Constant | Status | Source |
''' | --- | --- | --- |
''' | MaxOcrDimension / MinOcrDimension | chosen here, NOT derived | carried over from the first engine integration. They were marked *inherited* from "Tesseract's own guidance", but that guidance is about glyph HEIGHT in pixels, not about an edge length, and no document states these two numbers - so no source can be named. |
''' | MaxUpscaleFactor | chosen here, NOT derived | a ceiling on how far a small picture may be enlarged before enlargement stops adding information and starts adding interpolation artifacts. |
''' | MaxOcrLongSide | chosen here, NOT derived | kept under the 32767-pixel ceiling of the engine's image structures, with a margin. |
''' | MaxOcrPixels | chosen here, NOT derived | 16 MP: an 800 x 20000 webtoon strip passes at full size, and a single buffer stays under ~48 MB as 24 bpp, which the 32-bit viewer can afford once buffers are built one at a time. |
''' </summary>
Friend Module OcrImageScale

    Friend Const MaxOcrDimension As Integer = 3200
    Friend Const MinOcrDimension As Integer = 1200
    Friend Const MaxUpscaleFactor As Double = 1.75
    Friend Const MaxOcrLongSide As Integer = 30000
    Friend Const MaxOcrPixels As Long = 16000000L

    ''' <summary>
    ''' The factor the picture is resampled by before recognition.
    '''
    ''' Small pictures are enlarged as they always were. A large one is brought down so its
    ''' LONG edge is <see cref="MaxOcrDimension"/> - but never so far that the SHORT edge,
    ''' which is the one the lettering scales with on a strip, drops below
    ''' <see cref="MinOcrDimension"/> (or below itself). That floor is what the old rule
    ''' lacked: an 800 x 20000 webtoon strip came out 128 x 3200, its 25 px lettering 4 px
    ''' high, and nothing was found (audit 051 E-05). The floor is bounded in turn by the long
    ''' side and the pixel budget, so an extreme strip is still reduced - just by its area, not
    ''' by its length. For every picture whose short side stays at or above 1200 after the old
    ''' rule - an ordinary photo or page - the result is exactly what it was.
    ''' </summary>
    Friend Function ComputeScale(width As Integer, height As Integer) As Double
        Dim longest As Integer = Math.Max(width, height)
        Dim shortest As Integer = Math.Min(width, height)
        If longest <= 0 OrElse shortest <= 0 Then Return 1.0

        If longest < MinOcrDimension Then
            Return Math.Min(MaxUpscaleFactor, MinOcrDimension / CDbl(longest))
        End If
        If longest <= MaxOcrDimension Then Return 1.0

        Dim scale As Double = MaxOcrDimension / CDbl(longest)
        Dim shortFloor As Double = Math.Min(shortest, MinOcrDimension) / CDbl(shortest)
        If scale < shortFloor Then scale = shortFloor

        scale = Math.Min(scale, MaxOcrLongSide / CDbl(longest))
        Dim pixels As Double = CDbl(width) * CDbl(height)
        scale = Math.Min(scale, Math.Sqrt(MaxOcrPixels / pixels))
        Return Math.Min(scale, 1.0)
    End Function

End Module

''' <summary>
''' Values built on first request and kept for the rest of the run - the preprocessed OCR
''' buffers, one per mode. They used to be built all at once, at full resolution, before the
''' first attempt ran, although most pages are won by that first attempt (audit 051 E-08).
''' </summary>
Friend NotInheritable Class LazyByKey(Of TKey, TValue)

    Private ReadOnly factory As Func(Of TKey, TValue)
    Private ReadOnly built As New Dictionary(Of TKey, TValue)()

    Friend Sub New(factory As Func(Of TKey, TValue))
        Me.factory = factory
    End Sub

    Friend Function [Get](key As TKey) As TValue
        Dim value As TValue = Nothing
        If built.TryGetValue(key, value) Then Return value
        value = factory(key)
        built(key) = value
        Return value
    End Function

    ''' <summary>The keys built so far, in no particular order.</summary>
    Friend ReadOnly Property BuiltKeys As ICollection(Of TKey)
        Get
            Return built.Keys
        End Get
    End Property

End Class
