#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml.Linq

''' <summary>Renders catalog SVG path data into the one-colour WinForms bitmaps used
''' by controls. The source SVG remains the evidence; no font fallback is involved.</summary>
Public Module GlyphRenderer
    Private ReadOnly cache As New Dictionary(Of String, Bitmap)(StringComparer.Ordinal)
    Private ReadOnly cacheLock As New Object()
    Private ReadOnly transformPattern As New Regex("(matrix|translate|scale|rotate)\s*\(([^)]*)\)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
    Private ReadOnly numberPattern As New Regex("[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)

    ''' <summary>One drawable path of a glyph, already in 24-unit grid coordinates (every
    ''' enclosing group transform applied), with the paint the catalog gave it.</summary>
    Public NotInheritable Class GlyphShape
        Implements IDisposable

        Friend Sub New(path As GraphicsPath, fills As Boolean, strokeWidth As Single, opacity As Single)
            Me.Path = path
            Me.Fills = fills
            Me.StrokeWidth = strokeWidth
            Me.Opacity = opacity
        End Sub

        Public ReadOnly Property Path As GraphicsPath
        Public ReadOnly Property Fills As Boolean
        ''' <summary>0 when the path is not stroked.</summary>
        Public ReadOnly Property StrokeWidth As Single
        Public ReadOnly Property Opacity As Single

        ''' <summary>The inked area in grid units: the path's bounds, widened by half the
        ''' stroke for a stroked outline.</summary>
        Public Function InkBounds() As RectangleF
            Dim b As RectangleF = Path.GetBounds()
            If StrokeWidth > 0 Then b.Inflate(StrokeWidth / 2.0F, StrokeWidth / 2.0F)
            Return b
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Path.Dispose()
        End Sub
    End Class

    Public Function Render(glyphId As String, size As Integer, colour As Color, openAsset As Func(Of String, Stream)) As Bitmap
        If String.IsNullOrWhiteSpace(glyphId) Then Throw New ArgumentException("A glyph id is required.", NameOf(glyphId))
        If size <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(size))
        If openAsset Is Nothing Then Throw New ArgumentNullException(NameOf(openAsset))
        Dim key As String = glyphId & "|" & size.ToString(CultureInfo.InvariantCulture) & "|" & colour.ToArgb().ToString(CultureInfo.InvariantCulture)
        SyncLock cacheLock
            Dim cached As Bitmap = Nothing
            If cache.TryGetValue(key, cached) Then Return DirectCast(cached.Clone(), Bitmap)
            Using stream As Stream = openAsset(If(glyphId = "exchange-mark", "exchange-mark/svg/exchange-mark-mono.svg", "glyphs/" & glyphId & ".svg"))
                If stream Is Nothing Then Throw New FileNotFoundException("Bundled icon glyph was not found.", glyphId)
                Using reader As New StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks:=True)
                    cached = RenderSvg(reader.ReadToEnd(), size, colour)
                End Using
            End Using
            cache.Add(key, cached)
            Return DirectCast(cached.Clone(), Bitmap)
        End SyncLock
    End Function

    Public Function RenderSvg(svg As String, size As Integer, colour As Color) As Bitmap
        Dim bitmap As New Bitmap(size, size)
        Dim shapes As List(Of GlyphShape) = Nothing
        Try
            bitmap.SetResolution(96.0F, 96.0F)
            shapes = ReadShapes(svg)
            If shapes.Count = 0 Then Throw New FormatException("The catalog SVG contains no path data.")
            Using graphics As Graphics = Graphics.FromImage(bitmap)
                graphics.SmoothingMode = SmoothingMode.AntiAlias
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality
                graphics.Clear(Color.Transparent)
                graphics.ScaleTransform(size / 24.0F, size / 24.0F)
                For Each shape As GlyphShape In shapes
                    Dim ink As Color = Color.FromArgb(CInt(Math.Round(colour.A * shape.Opacity)), colour)
                    If shape.Fills Then
                        Using brush As New SolidBrush(ink)
                            graphics.FillPath(brush, shape.Path)
                        End Using
                    End If
                    If shape.StrokeWidth > 0 Then
                        Using pen As New Pen(ink, shape.StrokeWidth)
                            graphics.DrawPath(pen, shape.Path)
                        End Using
                    End If
                Next
            End Using
        Catch
            ' A malformed or empty SVG: nobody receives the bitmap, so it is ours to free
            ' (audit 057.S04-2). The exception itself goes on to the caller unchanged.
            bitmap.Dispose()
            Throw
        Finally
            If shapes IsNot Nothing Then
                For Each shape As GlyphShape In shapes
                    shape.Dispose()
                Next
            End If
        End Try
        Return bitmap
    End Function

    ''' <summary>
    ''' Reads every path of a catalog SVG into grid coordinates. The catalog uses a small
    ''' subset and this reads exactly that subset: nested &lt;g transform&gt; (translate,
    ''' scale, rotate, matrix), fill (currentColor or none), fill-rule, stroke with
    ''' stroke-width, and opacity / fill-opacity. Anything painted is painted in the ONE
    ''' colour of the control (ICON-RENDER rule 2) - a mono look has no second colour.
    ''' </summary>
    Public Function ReadShapes(svg As String) As List(Of GlyphShape)
        Dim result As New List(Of GlyphShape)()
        Dim doc As XDocument = XDocument.Parse(svg)
        Try
            Using identity As New Matrix()
                Walk(doc.Root, identity, 1.0F, result)
            End Using
        Catch
            ' A path that fails to parse part-way: the shapes read before it are never
            ' returned, so their GraphicsPaths are freed here (audit 057.S04-2).
            For Each shape As GlyphShape In result
                shape.Dispose()
            Next
            Throw
        End Try
        Return result
    End Function

    Private Sub Walk(element As XElement, parent As Matrix, parentOpacity As Single, result As List(Of GlyphShape))
        Using m As Matrix = parent.Clone()
            Dim transform As String = Attr(element, "transform")
            If transform.Length > 0 Then ApplyTransform(m, transform)
            Dim opacity As Single = parentOpacity * ReadFraction(Attr(element, "opacity"))

            If element.Name.LocalName = "path" Then
                Dim d As String = Attr(element, "d")
                If d.Length > 0 Then
                    Dim fill As String = Inherited(element, "fill")
                    Dim stroke As String = Inherited(element, "stroke")
                    Dim fills As Boolean = Not String.Equals(fill, "none", StringComparison.OrdinalIgnoreCase)
                    Dim strokeWidth As Single = 0
                    If stroke.Length > 0 AndAlso Not String.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase) Then
                        Dim w As String = Inherited(element, "stroke-width")
                        strokeWidth = If(w.Length > 0, ParseNumber(w), 1.0F)
                    End If
                    Dim fillOpacity As Single = ReadFraction(Attr(element, "fill-opacity"))
                    Dim path As GraphicsPath = SvgPathData.Parse(d)
                    path.FillMode = If(String.Equals(Inherited(element, "fill-rule"), "evenodd", StringComparison.OrdinalIgnoreCase),
                                       FillMode.Alternate, FillMode.Winding)
                    path.Transform(m)
                    ' A transform that scales also scales the stroke.
                    Dim scale As Single = CSng(Math.Sqrt(Math.Abs(m.Elements(0) * m.Elements(3) - m.Elements(1) * m.Elements(2))))
                    result.Add(New GlyphShape(path, fills, strokeWidth * scale, opacity * If(fills, fillOpacity, 1.0F)))
                End If
            End If

            For Each child As XElement In element.Elements()
                Walk(child, m, opacity, result)
            Next
        End Using
    End Sub

    Private Sub ApplyTransform(m As Matrix, transform As String)
        ' SVG applies the list right to left to a point, which is left to right prepended.
        For Each op As Match In transformPattern.Matches(transform)
            Dim args As New List(Of Single)()
            For Each n As Match In numberPattern.Matches(op.Groups(2).Value)
                args.Add(ParseNumber(n.Value))
            Next
            Select Case op.Groups(1).Value.ToLowerInvariant()
                Case "translate"
                    If args.Count >= 1 Then m.Translate(args(0), If(args.Count >= 2, args(1), 0.0F), MatrixOrder.Prepend)
                Case "scale"
                    If args.Count >= 1 Then m.Scale(args(0), If(args.Count >= 2, args(1), args(0)), MatrixOrder.Prepend)
                Case "rotate"
                    If args.Count >= 3 Then
                        m.RotateAt(args(0), New PointF(args(1), args(2)), MatrixOrder.Prepend)
                    ElseIf args.Count >= 1 Then
                        m.Rotate(args(0), MatrixOrder.Prepend)
                    End If
                Case "matrix"
                    If args.Count >= 6 Then
                        Using op6 As New Matrix(args(0), args(1), args(2), args(3), args(4), args(5))
                            m.Multiply(op6, MatrixOrder.Prepend)
                        End Using
                    End If
            End Select
        Next
    End Sub

    ''' <summary>A presentation attribute, looked up the tree as SVG inherits it.</summary>
    Private Function Inherited(element As XElement, name As String) As String
        Dim e As XElement = element
        While e IsNot Nothing
            Dim v As String = Attr(e, name)
            If v.Length > 0 Then Return v
            e = e.Parent
        End While
        Return ""
    End Function

    Private Function Attr(element As XElement, name As String) As String
        Dim a As XAttribute = element.Attribute(name)
        Return If(a Is Nothing, "", a.Value.Trim())
    End Function

    Private Function ReadFraction(value As String) As Single
        If value.Length = 0 Then Return 1.0F
        Return Math.Max(0.0F, Math.Min(1.0F, ParseNumber(value)))
    End Function

    Private Function ParseNumber(value As String) As Single
        Return Single.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
    End Function
End Module
#End If
