#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.Text.RegularExpressions

''' <summary>Dependency-free reader for the SVG path subset used by the iconography
''' catalog. Coordinates stay on the catalog's 24 by 24 design grid.</summary>
Public Module SvgPathData
    Private ReadOnly tokenPattern As New Regex("[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled)

    Public Function Parse(pathData As String) As GraphicsPath
        Dim path As New GraphicsPath()
        If String.IsNullOrWhiteSpace(pathData) Then Return path

        Dim tokens As New List(Of String)()
        For Each match As Match In tokenPattern.Matches(pathData)
            tokens.Add(match.Value)
        Next

        Dim reader As New PathReader(tokens)
        Dim current As PointF = PointF.Empty
        Dim figureStart As PointF = PointF.Empty
        Dim lastCubicControl As PointF = PointF.Empty
        Dim lastQuadraticControl As PointF = PointF.Empty
        Dim previousCommand As Char = ChrW(0)
        Dim command As Char = ChrW(0)

        While reader.HasMore
            Dim nextCommand As Char
            If reader.TryReadCommand(nextCommand) Then
                command = nextCommand
            ElseIf command = ChrW(0) Then
                Throw New FormatException("SVG path starts without a command.")
            End If

            Dim relative As Boolean = Char.IsLower(command)
            Select Case Char.ToUpperInvariant(command)
                Case "M"c
                    Dim first As Boolean = True
                    Do While reader.HasNumber
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        If first Then
                            path.StartFigure()
                            current = point
                            figureStart = point
                            first = False
                        Else
                            path.AddLine(current, point)
                            current = point
                        End If
                    Loop
                Case "L"c
                    Do While reader.HasNumber
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        path.AddLine(current, point)
                        current = point
                    Loop
                Case "H"c
                    Do While reader.HasNumber
                        Dim x As Single = reader.ReadNumber()
                        If relative Then x += current.X
                        Dim point As New PointF(x, current.Y)
                        path.AddLine(current, point)
                        current = point
                    Loop
                Case "V"c
                    Do While reader.HasNumber
                        Dim y As Single = reader.ReadNumber()
                        If relative Then y += current.Y
                        Dim point As New PointF(current.X, y)
                        path.AddLine(current, point)
                        current = point
                    Loop
                Case "C"c
                    Do While reader.HasNumber
                        Dim c1 As PointF = ReadPoint(reader, relative, current)
                        Dim c2 As PointF = ReadPoint(reader, relative, current)
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        path.AddBezier(current, c1, c2, point)
                        current = point
                        lastCubicControl = c2
                    Loop
                Case "S"c
                    Do While reader.HasNumber
                        Dim c1 As PointF = If(Char.ToUpperInvariant(previousCommand) = "C"c OrElse Char.ToUpperInvariant(previousCommand) = "S"c,
                                             Reflect(lastCubicControl, current), current)
                        Dim c2 As PointF = ReadPoint(reader, relative, current)
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        path.AddBezier(current, c1, c2, point)
                        current = point
                        lastCubicControl = c2
                    Loop
                Case "Q"c
                    Do While reader.HasNumber
                        Dim control As PointF = ReadPoint(reader, relative, current)
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        AddQuadratic(path, current, control, point)
                        current = point
                        lastQuadraticControl = control
                    Loop
                Case "T"c
                    Do While reader.HasNumber
                        Dim control As PointF = If(Char.ToUpperInvariant(previousCommand) = "Q"c OrElse Char.ToUpperInvariant(previousCommand) = "T"c,
                                                    Reflect(lastQuadraticControl, current), current)
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        AddQuadratic(path, current, control, point)
                        current = point
                        lastQuadraticControl = control
                    Loop
                Case "A"c
                    Do While reader.HasNumber
                        Dim rx As Single = Math.Abs(reader.ReadNumber())
                        Dim ry As Single = Math.Abs(reader.ReadNumber())
                        Dim rotation As Single = reader.ReadNumber()
                        Dim largeArc As Boolean = reader.ReadNumber() <> 0.0F
                        Dim sweep As Boolean = reader.ReadNumber() <> 0.0F
                        Dim point As PointF = ReadPoint(reader, relative, current)
                        AddArc(path, current, rx, ry, rotation, largeArc, sweep, point)
                        current = point
                    Loop
                Case "Z"c
                    path.CloseFigure()
                    current = figureStart
                Case Else
                    Throw New FormatException("Unsupported SVG path command '" & command & "'.")
            End Select
            previousCommand = command
        End While
        Return path
    End Function

    Private Function ReadPoint(reader As PathReader, relative As Boolean, origin As PointF) As PointF
        Dim x As Single = reader.ReadNumber()
        Dim y As Single = reader.ReadNumber()
        If relative Then Return New PointF(origin.X + x, origin.Y + y)
        Return New PointF(x, y)
    End Function

    Private Function Reflect(control As PointF, around As PointF) As PointF
        Return New PointF(2.0F * around.X - control.X, 2.0F * around.Y - control.Y)
    End Function

    Private Sub AddQuadratic(path As GraphicsPath, startPoint As PointF, control As PointF, endPoint As PointF)
        Dim c1 As New PointF(startPoint.X + (2.0F / 3.0F) * (control.X - startPoint.X), startPoint.Y + (2.0F / 3.0F) * (control.Y - startPoint.Y))
        Dim c2 As New PointF(endPoint.X + (2.0F / 3.0F) * (control.X - endPoint.X), endPoint.Y + (2.0F / 3.0F) * (control.Y - endPoint.Y))
        path.AddBezier(startPoint, c1, c2, endPoint)
    End Sub

    ' SVG endpoint-arc conversion from the SVG 1.1 implementation notes. Cubic
    ' segments keep the result independent of any SVG runtime.
    Private Sub AddArc(path As GraphicsPath, startPoint As PointF, rx As Single, ry As Single,
                       rotationDegrees As Single, largeArc As Boolean, sweep As Boolean, endPoint As PointF)
        If startPoint = endPoint Then Return
        If rx = 0.0F OrElse ry = 0.0F Then
            path.AddLine(startPoint, endPoint)
            Return
        End If

        Dim phi As Double = rotationDegrees * Math.PI / 180.0
        Dim cosPhi As Double = Math.Cos(phi)
        Dim sinPhi As Double = Math.Sin(phi)
        Dim dx As Double = (startPoint.X - endPoint.X) / 2.0
        Dim dy As Double = (startPoint.Y - endPoint.Y) / 2.0
        Dim x1p As Double = cosPhi * dx + sinPhi * dy
        Dim y1p As Double = -sinPhi * dx + cosPhi * dy
        Dim rxd As Double = rx
        Dim ryd As Double = ry
        Dim lambda As Double = x1p * x1p / (rxd * rxd) + y1p * y1p / (ryd * ryd)
        If lambda > 1.0 Then
            Dim scale As Double = Math.Sqrt(lambda)
            rxd *= scale
            ryd *= scale
        End If

        Dim numerator As Double = rxd * rxd * ryd * ryd - rxd * rxd * y1p * y1p - ryd * ryd * x1p * x1p
        Dim denominator As Double = rxd * rxd * y1p * y1p + ryd * ryd * x1p * x1p
        Dim factor As Double = If(denominator = 0.0, 0.0, Math.Sqrt(Math.Max(0.0, numerator / denominator)))
        If largeArc = sweep Then factor = -factor
        Dim cxp As Double = factor * (rxd * y1p / ryd)
        Dim cyp As Double = factor * (-ryd * x1p / rxd)
        Dim cx As Double = cosPhi * cxp - sinPhi * cyp + (startPoint.X + endPoint.X) / 2.0
        Dim cy As Double = sinPhi * cxp + cosPhi * cyp + (startPoint.Y + endPoint.Y) / 2.0
        Dim theta1 As Double = VectorAngle(1.0, 0.0, (x1p - cxp) / rxd, (y1p - cyp) / ryd)
        Dim delta As Double = VectorAngle((x1p - cxp) / rxd, (y1p - cyp) / ryd, (-x1p - cxp) / rxd, (-y1p - cyp) / ryd)
        If Not sweep AndAlso delta > 0.0 Then delta -= 2.0 * Math.PI
        If sweep AndAlso delta < 0.0 Then delta += 2.0 * Math.PI
        Dim segments As Integer = Math.Max(1, CInt(Math.Ceiling(Math.Abs(delta) / (Math.PI / 2.0))))
        Dim stepSize As Double = delta / segments
        Dim fromAngle As Double = theta1
        For i As Integer = 0 To segments - 1
            Dim toAngle As Double = fromAngle + stepSize
            AddArcSegment(path, cx, cy, rxd, ryd, cosPhi, sinPhi, fromAngle, toAngle)
            fromAngle = toAngle
        Next
    End Sub

    Private Sub AddArcSegment(path As GraphicsPath, cx As Double, cy As Double, rx As Double, ry As Double,
                              cosPhi As Double, sinPhi As Double, fromAngle As Double, toAngle As Double)
        Dim alpha As Double = 4.0 / 3.0 * Math.Tan((toAngle - fromAngle) / 4.0)
        Dim p0 As PointF = ArcPoint(cx, cy, rx, ry, cosPhi, sinPhi, fromAngle)
        Dim p3 As PointF = ArcPoint(cx, cy, rx, ry, cosPhi, sinPhi, toAngle)
        Dim d0 As PointF = ArcDerivative(rx, ry, cosPhi, sinPhi, fromAngle)
        Dim d3 As PointF = ArcDerivative(rx, ry, cosPhi, sinPhi, toAngle)
        Dim p1 As New PointF(p0.X + CSng(alpha) * d0.X, p0.Y + CSng(alpha) * d0.Y)
        Dim p2 As New PointF(p3.X - CSng(alpha) * d3.X, p3.Y - CSng(alpha) * d3.Y)
        path.AddBezier(p0, p1, p2, p3)
    End Sub

    Private Function ArcPoint(cx As Double, cy As Double, rx As Double, ry As Double, cosPhi As Double, sinPhi As Double, angle As Double) As PointF
        Dim x As Double = rx * Math.Cos(angle)
        Dim y As Double = ry * Math.Sin(angle)
        Return New PointF(CSng(cx + cosPhi * x - sinPhi * y), CSng(cy + sinPhi * x + cosPhi * y))
    End Function

    Private Function ArcDerivative(rx As Double, ry As Double, cosPhi As Double, sinPhi As Double, angle As Double) As PointF
        Dim x As Double = -rx * Math.Sin(angle)
        Dim y As Double = ry * Math.Cos(angle)
        Return New PointF(CSng(cosPhi * x - sinPhi * y), CSng(sinPhi * x + cosPhi * y))
    End Function

    Private Function VectorAngle(ux As Double, uy As Double, vx As Double, vy As Double) As Double
        Return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy)
    End Function

    Private NotInheritable Class PathReader
        Private ReadOnly tokens As IList(Of String)
        Private index As Integer
        Public Sub New(values As IList(Of String))
            tokens = values
        End Sub
        Public ReadOnly Property HasMore As Boolean
            Get
                Return index < tokens.Count
            End Get
        End Property
        Public ReadOnly Property HasNumber As Boolean
            Get
                Return HasMore AndAlso Not IsCommand(tokens(index))
            End Get
        End Property
        Public Function TryReadCommand(ByRef value As Char) As Boolean
            If Not HasMore OrElse Not IsCommand(tokens(index)) Then Return False
            value = tokens(index)(0)
            index += 1
            Return True
        End Function
        Public Function ReadNumber() As Single
            If Not HasNumber Then Throw New FormatException("SVG path has too few parameters.")
            Dim token As String = tokens(index)
            index += 1
            Return Single.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture)
        End Function
        Private Shared Function IsCommand(value As String) As Boolean
            Return value.Length = 1 AndAlso Char.IsLetter(value(0))
        End Function
    End Class
End Module
#End If
