Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

''' <summary>
''' Shared pixel and edge sampler for the dynamic background colour and the perspective bars
''' (018_SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md, U-15). Works for both .NET Framework
''' 4.8 and .NET 10.
'''
''' It is one place for the sampling rules (clamping, the opaque alpha, the rounding of the
''' averages, the one-pixel inset of the displayed edges) so the two callers cannot drift apart.
'''
''' It deliberately does NOT copy the raster. The specification asked for one LockBits and a
''' Marshal.Copy buffer, and that was built first and measured (2026-10-07): both callers read
''' a few dozen pixels, not many, and a copy of the whole bitmap costs thousands of times more
''' than the reads it would save - 14-38 ms and up to ~96 MB for a 4K to 24 MP photo against
''' 0.001-0.011 ms for the three reads of the automatic scheme or the ~40 reads of a side/top/
''' bottom scheme, and still 0.7 ms slower than the per-row reads of a 1080p perspective edge.
''' So each read is one Bitmap.GetPixel; a GDI+ failure surfaces from the read, inside the
''' caller's Try/Catch, exactly as it did before this helper existed.
''' </summary>
Public NotInheritable Class EdgeSampler
    Implements IDisposable

    Private ReadOnly _source As Bitmap
    Private ReadOnly _width As Integer
    Private ReadOnly _height As Integer
    Private _disposed As Boolean

    Public ReadOnly Property Width As Integer
        Get
            Return _width
        End Get
    End Property

    Public ReadOnly Property Height As Integer
        Get
            Return _height
        End Get
    End Property

    Private Sub New(source As Bitmap, width As Integer, height As Integer)
        _source = source
        _width = width
        _height = height
    End Sub

    ''' <summary>
    ''' Wraps <paramref name="bmp"/> for sampling. Does not copy, lock or own it: the bitmap must
    ''' outlive the sampler, and a pixel changed in it afterwards is what the next read returns.
    ''' Returns Nothing if the bitmap is null, 0-sized or unusable (already disposed).
    ''' </summary>
    Public Shared Function TryCreate(bmp As Bitmap) As EdgeSampler
        If bmp Is Nothing Then Return Nothing
        Try
            Dim w As Integer = bmp.Width
            Dim h As Integer = bmp.Height
            If w <= 0 OrElse h <= 0 Then Return Nothing
            Return New EdgeSampler(bmp, w, h)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " EdgeSampler: bitmap unusable: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Reads pixel at (x, y) as fully opaque ARGB Color (Alpha = 255). Clamps coordinates to valid bounds.
    ''' </summary>
    Public Function GetPixel(x As Integer, y As Integer) As Color
        If _disposed Then Return Color.Black
        Dim clampedX As Integer = Math.Max(0, Math.Min(x, _width - 1))
        Dim clampedY As Integer = Math.Max(0, Math.Min(y, _height - 1))
        Dim px As Color = _source.GetPixel(clampedX, clampedY)
        Return Color.FromArgb(255, px.R, px.G, px.B)
    End Function

    ''' <summary>
    ''' Dynamic background color calculation for Form_Color_Scheme (0, 3, 4, 5).
    ''' The by-side/top/bottom averages round (<c>CInt(sum / count)</c>), exactly as the GetPixel
    ''' code this replaced did - integer division would truncate and shift the tint by a level.
    ''' </summary>
    Public Function CalculateBackgroundColor(scheme As Integer,
                                             firstColorX As Integer, firstColorY As Integer,
                                             secondColorX As Integer, secondColorY As Integer,
                                             colorDeviation As Long, cornerDivPoint As Double,
                                             stepSize As Integer, defaultColor As Color) As Color
        If _width <= 1 OrElse _height <= 1 Then Return defaultColor

        Select Case scheme
            Case 0 ' Auto / default
                If _width > secondColorX AndAlso _height > secondColorY Then
                    Dim firstPixel = GetPixel(firstColorX, firstColorY)
                    Dim secondPixel = GetPixel(secondColorX, secondColorY)
                    Dim dif As Long = CLng(Math.Abs(CInt(secondPixel.R) - CInt(firstPixel.R))) +
                                      CLng(Math.Abs(CInt(secondPixel.G) - CInt(firstPixel.G))) +
                                      CLng(Math.Abs(CInt(secondPixel.B) - CInt(firstPixel.B)))
                    If dif < colorDeviation Then
                        Return firstPixel
                    Else
                        Dim cornerX As Integer = If(cornerDivPoint > 0, CInt(_width / cornerDivPoint), 0)
                        Dim cornerY As Integer = If(cornerDivPoint > 0, CInt(_height / cornerDivPoint), 0)
                        Return GetPixel(cornerX, cornerY)
                    End If
                Else
                    Dim cornerX As Integer = If(cornerDivPoint > 0, CInt(_width / cornerDivPoint), 0)
                    Dim cornerY As Integer = If(cornerDivPoint > 0, CInt(_height / cornerDivPoint), 0)
                    Return GetPixel(cornerX, cornerY)
                End If

            Case 3 ' By side
                Dim difR As Long = 0, difG As Long = 0, difB As Long = 0
                Dim count As Integer = 0
                Dim stepS As Integer = Math.Max(1, stepSize)
                For z As Integer = 0 To _height - 1 Step stepS
                    Dim px = GetPixel(1, z)
                    difR += px.R
                    difG += px.G
                    difB += px.B
                    count += 1
                Next
                If count = 0 Then Return defaultColor
                Return Color.FromArgb(255, CInt(difR / count), CInt(difG / count), CInt(difB / count))

            Case 4 ' By top
                Dim difR As Long = 0, difG As Long = 0, difB As Long = 0
                Dim count As Integer = 0
                Dim stepS As Integer = Math.Max(1, stepSize)
                For z As Integer = 0 To _width - 1 Step stepS
                    Dim px = GetPixel(z, 1)
                    difR += px.R
                    difG += px.G
                    difB += px.B
                    count += 1
                Next
                If count = 0 Then Return defaultColor
                Return Color.FromArgb(255, CInt(difR / count), CInt(difG / count), CInt(difB / count))

            Case 5 ' By bottom
                Dim difR As Long = 0, difG As Long = 0, difB As Long = 0
                Dim count As Integer = 0
                Dim stepS As Integer = Math.Max(1, stepSize)
                For z As Integer = 0 To _width - 1 Step stepS
                    Dim px = GetPixel(z, _height - 1)
                    difR += px.R
                    difG += px.G
                    difB += px.B
                    count += 1
                Next
                If count = 0 Then Return defaultColor
                Return Color.FromArgb(255, CInt(difR / count), CInt(difG / count), CInt(difB / count))

            Case Else
                Return defaultColor
        End Select
    End Function

    ''' <summary>
    ''' Samples vertical edge color for perspective background with given depth averaging.
    ''' </summary>
    Public Function SampleVerticalEdgeColor(isLeftEdge As Boolean, sourceY As Integer, sampleDepth As Integer) As Color
        Dim clampedY As Integer = Math.Max(0, Math.Min(sourceY, _height - 1))
        Dim effectiveDepth As Integer = Math.Max(1, Math.Min(sampleDepth, _width))
        Dim startX As Integer = If(isLeftEdge, 0, _width - effectiveDepth)
        Dim sumR As Integer = 0
        Dim sumG As Integer = 0
        Dim sumB As Integer = 0

        For x As Integer = startX To startX + effectiveDepth - 1
            Dim px = GetPixel(x, clampedY)
            sumR += CInt(px.R)
            sumG += CInt(px.G)
            sumB += CInt(px.B)
        Next

        Return Color.FromArgb(255, sumR \ effectiveDepth, sumG \ effectiveDepth, sumB \ effectiveDepth)
    End Function

    ''' <summary>
    ''' Samples horizontal edge color for perspective background with given depth averaging.
    ''' </summary>
    Public Function SampleHorizontalEdgeColor(isTopEdge As Boolean, sourceX As Integer, sampleDepth As Integer) As Color
        Dim clampedX As Integer = Math.Max(0, Math.Min(sourceX, _width - 1))
        Dim effectiveDepth As Integer = Math.Max(1, Math.Min(sampleDepth, _height))
        Dim startY As Integer = If(isTopEdge, 0, _height - effectiveDepth)
        Dim sumR As Integer = 0
        Dim sumG As Integer = 0
        Dim sumB As Integer = 0

        For y As Integer = startY To startY + effectiveDepth - 1
            Dim px = GetPixel(clampedX, y)
            sumR += CInt(px.R)
            sumG += CInt(px.G)
            sumB += CInt(px.B)
        Next

        Return Color.FromArgb(255, sumR \ effectiveDepth, sumG \ effectiveDepth, sumB \ effectiveDepth)
    End Function

    ''' <summary>
    ''' Gets sampled vertical edge colors for displayed bitmap in perspective background.
    ''' </summary>
    Public Function GetDisplayedVerticalEdgeColors(targetHeight As Integer, imageRect As Rectangle, isLeftEdge As Boolean) As List(Of Color)
        Dim edgeColors As New List(Of Color)(targetHeight)
        ' LETTERBOX-BARS rule 3 (LetterboxFillMath): one pixel IN from the edge, and the surface
        ' row's source row clamped to the displayed image.
        Dim sourceX As Integer = LetterboxFillMath.EdgeIndex(_width, Not isLeftEdge)

        For targetY As Integer = 0 To targetHeight - 1
            Dim sourceY As Integer = LetterboxFillMath.EdgeSourceIndex(targetY, imageRect.Top, _height)
            Dim px As Color = GetPixel(sourceX, sourceY)
            edgeColors.Add(Color.FromArgb(255, px.R, px.G, px.B))
        Next

        Return edgeColors
    End Function

    ''' <summary>
    ''' Gets sampled horizontal edge colors for displayed bitmap in perspective background.
    ''' </summary>
    Public Function GetDisplayedHorizontalEdgeColors(targetWidth As Integer, imageRect As Rectangle, isTopEdge As Boolean) As List(Of Color)
        Dim edgeColors As New List(Of Color)(targetWidth)
        ' LETTERBOX-BARS rule 3, transposed.
        Dim sourceY As Integer = LetterboxFillMath.EdgeIndex(_height, Not isTopEdge)

        For targetX As Integer = 0 To targetWidth - 1
            Dim sourceX As Integer = LetterboxFillMath.EdgeSourceIndex(targetX, imageRect.Left, _width)
            Dim px As Color = GetPixel(sourceX, sourceY)
            edgeColors.Add(Color.FromArgb(255, px.R, px.G, px.B))
        Next

        Return edgeColors
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _disposed = True
    End Sub

End Class
