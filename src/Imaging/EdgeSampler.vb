Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices

''' <summary>
''' High-performance pixel and edge sampler using LockBits and memory buffers instead of GetPixel
''' (018_SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md, U-15).
''' Works for both .NET Framework 4.8 and .NET 10.
''' Provides fast, thread-safe pixel lookups from an in-memory 32bpp buffer after a single lock/copy.
''' </summary>
Public NotInheritable Class EdgeSampler
    Implements IDisposable

    Private ReadOnly _width As Integer
    Private ReadOnly _height As Integer
    Private ReadOnly _stride As Integer
    Private ReadOnly _pixels As Byte()
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

    Private Sub New(width As Integer, height As Integer, stride As Integer, pixels As Byte())
        _width = width
        _height = height
        _stride = stride
        _pixels = pixels
    End Sub

    ''' <summary>
    ''' Creates an EdgeSampler by locking the bitmap once, copying its 32bpp ARGB raster to a byte buffer,
    ''' and unlocking immediately. Returns Nothing if bitmap is null, 0-sized, or LockBits fails.
    ''' </summary>
    Public Shared Function TryCreate(bmp As Bitmap) As EdgeSampler
        If bmp Is Nothing Then Return Nothing
        Try
            Dim w As Integer = bmp.Width
            Dim h As Integer = bmp.Height
            If w <= 0 OrElse h <= 0 Then Return Nothing

            Dim rect As New Rectangle(0, 0, w, h)
            Dim bmpData As BitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
            Try
                Dim stride As Integer = Math.Abs(bmpData.Stride)
                Dim bytesCount As Integer = stride * h
                Dim buffer(bytesCount - 1) As Byte
                Marshal.Copy(bmpData.Scan0, buffer, 0, bytesCount)
                Return New EdgeSampler(w, h, stride, buffer)
            Finally
                bmp.UnlockBits(bmpData)
            End Try
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " EdgeSampler: LockBits failed: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Reads pixel at (x, y) as fully opaque ARGB Color (Alpha = 255). Clamps coordinates to valid bounds.
    ''' </summary>
    Public Function GetPixel(x As Integer, y As Integer) As Color
        If _pixels Is Nothing OrElse _pixels.Length = 0 Then Return Color.Black
        Dim clampedX As Integer = Math.Max(0, Math.Min(x, _width - 1))
        Dim clampedY As Integer = Math.Max(0, Math.Min(y, _height - 1))
        Dim offset As Integer = (clampedY * _stride) + (clampedX * 4)
        If offset + 2 >= _pixels.Length Then Return Color.Black
        ' Format32bppArgb stores BGRA in memory (little-endian byte order: B=0, G=1, R=2, A=3)
        Dim b As Byte = _pixels(offset)
        Dim g As Byte = _pixels(offset + 1)
        Dim r As Byte = _pixels(offset + 2)
        Return Color.FromArgb(255, r, g, b)
    End Function

    ''' <summary>
    ''' Dynamic background color calculation for Form_Color_Scheme (0, 3, 4, 5).
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
                Return Color.FromArgb(255, CInt(difR \ count), CInt(difG \ count), CInt(difB \ count))

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
                Return Color.FromArgb(255, CInt(difR \ count), CInt(difG \ count), CInt(difB \ count))

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
                Return Color.FromArgb(255, CInt(difR \ count), CInt(difG \ count), CInt(difB \ count))

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
        Dim edgeInset As Integer = If(_width >= 3, 1, 0)
        Dim sourceX As Integer = If(isLeftEdge, edgeInset, _width - 1 - edgeInset)

        For targetY As Integer = 0 To targetHeight - 1
            Dim sourceY As Integer = Math.Max(0, Math.Min(targetY - imageRect.Top, _height - 1))
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
        Dim edgeInset As Integer = If(_height >= 3, 1, 0)
        Dim sourceY As Integer = If(isTopEdge, edgeInset, _height - 1 - edgeInset)

        For targetX As Integer = 0 To targetWidth - 1
            Dim sourceX As Integer = Math.Max(0, Math.Min(targetX - imageRect.Left, _width - 1))
            Dim px As Color = GetPixel(sourceX, sourceY)
            edgeColors.Add(Color.FromArgb(255, px.R, px.G, px.B))
        Next

        Return edgeColors
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _disposed = True
    End Sub

End Class
