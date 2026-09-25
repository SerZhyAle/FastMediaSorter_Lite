Option Strict On

Imports Xunit

' Audit 051 E-05 and E-08: how big a picture reaches the recognizer, and which buffers are built.
Public Class OcrImageScaleTests

    ' --- E-05: a tall strip keeps readable lettering ------------------------------

    <Fact>
    Public Sub AWebtoonStrip_IsNotShrunkByItsLength()
        ' 800 x 20000 used to come out 128 x 3200 - 25 px lettering became 4 px.
        Assert.True(OcrImageScale.ComputeScale(800, 20000) >= 0.5)
    End Sub

    <Fact>
    Public Sub AWideStrip_IsNotShrunkByItsLengthEither()
        Assert.True(OcrImageScale.ComputeScale(20000, 800) >= 0.5)
    End Sub

    <Theory>
    <InlineData(4000, 3000)>
    <InlineData(6000, 4000)>
    <InlineData(10000, 8000)>
    <InlineData(3000, 4500)>
    Public Sub AnOrdinaryPhotoOrPage_IsScaledExactlyAsBefore(w As Integer, h As Integer)
        ' The old rule: the long edge to 3200. Unchanged wherever the short edge stays >= 1200.
        Dim expected As Double = 3200.0 / Math.Max(w, h)
        Assert.Equal(expected, OcrImageScale.ComputeScale(w, h), 9)
    End Sub

    <Fact>
    Public Sub ASmallPicture_IsStillEnlarged_UpToTheCeiling()
        Assert.Equal(1.75, OcrImageScale.ComputeScale(400, 300), 9)
        Assert.Equal(1200.0 / 1000.0, OcrImageScale.ComputeScale(1000, 700), 9)
    End Sub

    <Fact>
    Public Sub AMidSizedPicture_IsLeftAlone()
        Assert.Equal(1.0, OcrImageScale.ComputeScale(2400, 1800), 9)
    End Sub

    <Fact>
    Public Sub AnExtremeStrip_StaysInsideTheEngineAndMemoryLimits()
        Dim scale As Double = OcrImageScale.ComputeScale(1000, 60000)
        Assert.True(60000 * scale <= OcrImageScale.MaxOcrLongSide + 1)
        Assert.True(1000.0 * scale * 60000.0 * scale <= OcrImageScale.MaxOcrPixels + 1)
    End Sub

    <Fact>
    Public Sub NeverUpscalesALargePicture()
        Assert.True(OcrImageScale.ComputeScale(1100, 5000) <= 1.0)
    End Sub

    ' --- E-08: buffers are built on request, once ---------------------------------

    <Fact>
    Public Sub OnlyTheRequestedBuffer_IsBuilt()
        Dim builds As New List(Of String)
        Dim lazy As New LazyByKey(Of String, String)(Function(k)
                                                         builds.Add(k)
                                                         Return k.ToUpperInvariant()
                                                     End Function)
        ' A page won by its first, Normal attempt asks for nothing else.
        Assert.Equal("NORMAL", lazy.Get("normal"))
        Assert.Equal({"normal"}, builds.ToArray())
        Assert.Equal(1, lazy.BuiltKeys.Count)
    End Sub

    <Fact>
    Public Sub ABuffer_IsBuiltOnce_HoweverOftenItIsAskedFor()
        Dim builds As Integer = 0
        Dim lazy As New LazyByKey(Of Integer, Integer)(Function(k)
                                                           builds += 1
                                                           Return k * 2
                                                       End Function)
        For i As Integer = 1 To 5
            Assert.Equal(6, lazy.Get(3))
        Next
        Assert.Equal(1, builds)
    End Sub

End Class
