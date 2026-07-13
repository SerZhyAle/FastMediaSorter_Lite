Option Strict On

Imports System.Drawing
Imports System.IO
Imports Xunit
Imports fmsl

''' <summary>
''' Tests for LITE's Utils module. GetImageDimensions is the important one: the
''' background worker reads pixel size from the file HEADER (never GDI+ decode) to
''' avoid corrupting shared GDI+ state while the UI thread runs GetPixel. These pin
''' the PNG/GIF/BMP/JPEG header parsing with crafted byte headers.
''' </summary>
Public Class UtilsTests

    ' --- array helpers ---

    <Fact>
    Public Sub AddAt_InsertsAtIndex()
        Dim r As Integer() = Utils.AddAt(New Integer() {1, 2, 4}, 3, 2)
        Assert.Equal(New Integer() {1, 2, 3, 4}, r)
    End Sub

    <Fact>
    Public Sub AddAt_AtStartAndEnd()
        Assert.Equal(New Integer() {9, 1, 2}, Utils.AddAt(New Integer() {1, 2}, 9, 0))
        Assert.Equal(New Integer() {1, 2, 9}, Utils.AddAt(New Integer() {1, 2}, 9, 2))
    End Sub

    <Fact>
    Public Sub RemoveAt_RemovesElement()
        Assert.Equal(New Integer() {1, 3}, Utils.RemoveAt(New Integer() {1, 2, 3}, 1))
        Assert.Equal(New Integer() {2, 3}, Utils.RemoveAt(New Integer() {1, 2, 3}, 0))
    End Sub

    ' --- contrast colour ---

    <Theory>
    <InlineData(0, 0, 0)>       ' black bg -> white text
    <InlineData(20, 20, 20)>    ' dark bg -> white
    Public Sub GetOppositeColor_DarkBackgrounds_White(r As Integer, g As Integer, b As Integer)
        Assert.Equal(Color.White.ToArgb(), Utils.GetOppositeColor(Color.FromArgb(r, g, b)).ToArgb())
    End Sub

    <Theory>
    <InlineData(255, 255, 255)> ' white bg -> black text
    <InlineData(200, 200, 200)> ' light bg -> black
    Public Sub GetOppositeColor_LightBackgrounds_Black(r As Integer, g As Integer, b As Integer)
        Assert.Equal(Color.Black.ToArgb(), Utils.GetOppositeColor(Color.FromArgb(r, g, b)).ToArgb())
    End Sub

    ' --- GetImageDimensions (header-only) ---

    Private Shared Function WriteTemp(bytes As Byte(), ext As String) As String
        Dim tmpPath As String = Path.Combine(Path.GetTempPath(), "fms_utiltest_" & Guid.NewGuid().ToString("N") & ext)
        File.WriteAllBytes(tmpPath, bytes)
        Return tmpPath
    End Function

    <Fact>
    Public Sub GetImageDimensions_Png()
        ' 8-byte PNG signature + IHDR (len, "IHDR", width BE, height BE = 800x600).
        Dim b As Byte() = {
            &H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA,
            0, 0, 0, &HD, &H49, &H48, &H44, &H52,
            0, 0, 3, &H20, 0, 0, 2, &H58}
        Dim p As String = WriteTemp(b, ".png")
        Try
            Assert.Equal(New Size(800, 600), Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

    <Fact>
    Public Sub GetImageDimensions_Gif()
        ' "GIF89a" + logical screen width/height little-endian (640x480).
        Dim b As Byte() = {&H47, &H49, &H46, &H38, &H39, &H61, &H80, 2, &HE0, 1}
        Dim p As String = WriteTemp(b, ".gif")
        Try
            Assert.Equal(New Size(640, 480), Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

    <Fact>
    Public Sub GetImageDimensions_Bmp()
        ' "BM" + header; width @18 (4 LE), height @22 (4 LE) = 1024x768.
        Dim b(25) As Byte
        b(0) = &H42 : b(1) = &H4D
        b(18) = 0 : b(19) = 4 : b(20) = 0 : b(21) = 0    ' 0x00000400 = 1024
        b(22) = 0 : b(23) = 3 : b(24) = 0 : b(25) = 0    ' 0x00000300 = 768
        Dim p As String = WriteTemp(b, ".bmp")
        Try
            Assert.Equal(New Size(1024, 768), Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

    <Fact>
    Public Sub GetImageDimensions_Jpeg()
        ' SOI + SOF0 (len=17, precision=8, height BE=600, width BE=800).
        Dim b As Byte() = {
            &HFF, &HD8, &HFF, &HC0, 0, &H11, 8, 2, &H58, 3, &H20,
            1, &H22, 0, 0, 0, 0, 0, 0}
        Dim p As String = WriteTemp(b, ".jpg")
        Try
            Assert.Equal(New Size(800, 600), Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

    <Fact>
    Public Sub GetImageDimensions_UnknownOrMissing_ReturnsEmpty()
        Assert.Equal(Size.Empty, Utils.GetImageDimensions("C:\__fms_no_such_file__.png"))
        Dim p As String = WriteTemp(New Byte() {1, 2, 3, 4}, ".dat")
        Try
            Assert.Equal(Size.Empty, Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

End Class
