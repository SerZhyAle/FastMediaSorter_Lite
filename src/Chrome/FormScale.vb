#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>The unit a font size is assigned in (specification L0361 rule S3).</summary>
Friend Enum FormScaleFontKind
    ''' <summary>A pixel-unit font (<c>GraphicsUnit.Pixel</c>): no point-to-pixel conversion.</summary>
    Pixel = 0
    ''' <summary>A point font: the platform turns it into pixels through the SYSTEM dpi (measured fact E4).</summary>
    Point = 1
End Enum

''' <summary>
''' The one resolver of the form scale model (specification L0361 section 5, rules S1-S4): pure, no UI, no
''' registry, no static state. Inputs: a size in logical pixels at 96 dpi, the design multiplier
''' <c>M = U x T</c> (interface scale times the Windows Text size factor), the dpi a control reads its font at
''' (<c>c</c>: the system dpi <c>S</c> for a control not yet in its window, the window dpi <c>D</c> once it is in),
''' the system dpi and the window dpi. Replayed against <c>form-scale-vectors.json</c> by <c>FormScaleTests</c>.
''' </summary>
Friend NotInheritable Class FormScale

    ''' <summary>The dpi a logical pixel is defined at.</summary>
    Friend Const LogicalDpi As Integer = 96

    Private Sub New()
    End Sub

    ''' <summary>Rule S1: <c>K = max(1, dpi / 96)</c>. A dpi below 96 or missing is read as 96.</summary>
    Friend Shared Function LayoutFactor(dpi As Integer) As Double
        If dpi <= LogicalDpi Then Return 1.0
        Return dpi / CDbl(LogicalDpi)
    End Function

    ''' <summary>Rule S2: device pixels of a logical size, <c>floor(l x K x M + 0.5)</c> (half up).</summary>
    Friend Shared Function Px(logical As Double, windowDpi As Integer, multiplier As Double) As Integer
        Return CInt(Math.Floor(logical * LayoutFactor(windowDpi) * SafeMultiplier(multiplier) + 0.5))
    End Function

    ''' <summary>Rule S4, pixel form: the size in pixels to assign, <c>l x M x max(1, c / 96)</c>.</summary>
    Friend Shared Function PixelFontSize(logicalPixels As Double, multiplier As Double, basisDpi As Integer) As Double
        Return logicalPixels * SafeMultiplier(multiplier) * LayoutFactor(basisDpi)
    End Function

    ''' <summary>Rule S4, point form: the point size to assign, <c>p x M x c / S</c> with <c>p = 0.75 x l</c>.</summary>
    Friend Shared Function PointFontSize(logicalPixels As Double, multiplier As Double, basisDpi As Integer,
                                         systemDpi As Integer) As Double
        Return RebasePoints(0.75 * logicalPixels * SafeMultiplier(multiplier), basisDpi, systemDpi)
    End Function

    ''' <summary>Rule S4 for a point size already at the design scale (<c>p x M</c>): multiplied by <c>c / S</c>.</summary>
    Friend Shared Function RebasePoints(points As Double, basisDpi As Integer, systemDpi As Integer) As Double
        Dim c As Integer = SafeDpi(basisDpi)
        Dim s As Integer = SafeDpi(systemDpi)
        If c = s Then Return points
        Return points * c / s
    End Function

    ''' <summary>The size to assign for <paramref name="kind"/> (pixels or points).</summary>
    Friend Shared Function AssignedFontSize(kind As FormScaleFontKind, logicalPixels As Double, multiplier As Double,
                                            basisDpi As Integer, systemDpi As Integer) As Double
        If kind = FormScaleFontKind.Point Then Return PointFontSize(logicalPixels, multiplier, basisDpi, systemDpi)
        Return PixelFontSize(logicalPixels, multiplier, basisDpi)
    End Function

    ''' <summary>The model of what WinForms shows (measured facts E2-E4): a font assigned at basis <c>c</c> is
    ''' rescaled by <c>D / c</c> when the control is in a window at <c>D</c>; a point font becomes pixels through
    ''' <c>S</c> (<c>pt x S / 72</c>). Returns the em height in device pixels.</summary>
    Friend Shared Function PlatformOnScreenPixels(kind As FormScaleFontKind, assigned As Double, basisDpi As Integer,
                                                  systemDpi As Integer, windowDpi As Integer) As Double
        Dim c As Integer = SafeDpi(basisDpi)
        Dim d As Integer = SafeDpi(windowDpi)
        Dim pixels As Double = If(kind = FormScaleFontKind.Point, assigned * SafeDpi(systemDpi) / 72.0, assigned)
        Return pixels * d / c
    End Function

    ''' <summary>Rule S6 (1): the share of the work area a declared minimum may take at most.</summary>
    Friend Const MinimumShareOfWorkArea As Double = 0.9

    ''' <summary>Rule S6 (1): the part of a work-area length <paramref name="areaPx"/> a minimum may take,
    ''' <c>floor(area x share)</c>. An unknown area (0 or less) and a share outside (0, 1] give 0 and the area
    ''' itself respectively: 0 is "no bound" for <c>WindowPlacement.BoundMinimum</c>.</summary>
    Friend Shared Function ShareOfLength(areaPx As Integer, share As Double) As Integer
        If areaPx <= 0 Then Return 0
        If Double.IsNaN(share) OrElse share <= 0 OrElse share > 1 Then Return areaPx
        Return CInt(Math.Floor(areaPx * share))
    End Function

    Private Shared Function SafeDpi(dpi As Integer) As Integer
        Return If(dpi <= 0, LogicalDpi, dpi)
    End Function

    Private Shared Function SafeMultiplier(multiplier As Double) As Double
        If Double.IsNaN(multiplier) OrElse Double.IsInfinity(multiplier) OrElse multiplier <= 0 Then Return 1.0
        Return multiplier
    End Function

End Class
#End If
