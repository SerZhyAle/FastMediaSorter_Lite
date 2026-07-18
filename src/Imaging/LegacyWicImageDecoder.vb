#If NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

''' <summary>
''' net48 decoder: WIC (WPF BitmapDecoder) first, ImageSharp 2.x as fallback for
''' WEBP variants WIC can't handle. Code moved verbatim from FileManager/Utils
''' behind the IImageDecoder seam (SPECIFICATION_DOTNET10_MODERN_BUILD.md Ф0).
''' This whole file compiles only in the .NET Framework build - the modern build
''' has no WPF reference.
''' </summary>
Friend NotInheritable Class LegacyWicImageDecoder
    Implements IImageDecoder

    Public Function DecodeToImage(stream As MemoryStream) As Image Implements IImageDecoder.DecodeToImage
        Try
            Return LoadBitmapViaWic(stream)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0038: WEBP via WIC failed, fallback to ImageSharp: " & ex.Message)
            AppFileLogger.LogException("WEBP via WIC failed; trying ImageSharp fallback", ex)
            Return LoadBitmapViaImageSharp(stream)
        End Try
    End Function

    Public Function TryGetPixelSize(stream As Stream) As Size Implements IImageDecoder.TryGetPixelSize
        Dim decoder As BitmapDecoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
        If decoder.Frames.Count = 0 Then Return Size.Empty

        Dim frame = decoder.Frames(0)
        If frame Is Nothing OrElse frame.PixelWidth <= 0 OrElse frame.PixelHeight <= 0 Then Return Size.Empty

        Return New Size(frame.PixelWidth, frame.PixelHeight)
    End Function

    Private Function LoadBitmapViaWic(stream As MemoryStream) As Bitmap
        stream.Position = 0

        Dim decoder As BitmapDecoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
        If decoder.Frames.Count = 0 Then Return Nothing

        Dim frame As BitmapSource = decoder.Frames(0)
        Dim bitmapSource As BitmapSource = frame
        If bitmapSource.Format <> PixelFormats.Bgra32 Then
            bitmapSource = New FormatConvertedBitmap(frame, PixelFormats.Bgra32, Nothing, 0)
        End If

        Dim width As Integer = bitmapSource.PixelWidth
        Dim height As Integer = bitmapSource.PixelHeight
        If width <= 0 OrElse height <= 0 Then Return Nothing

        Dim stride As Integer = width * 4
        Dim pixels(stride * height - 1) As Byte
        bitmapSource.CopyPixels(pixels, stride, 0)

        Dim bitmap As New Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
        Dim dpiX As Single = If(CSng(bitmapSource.DpiX) > 0, CSng(bitmapSource.DpiX), 96.0F)
        Dim dpiY As Single = If(CSng(bitmapSource.DpiY) > 0, CSng(bitmapSource.DpiY), 96.0F)
        bitmap.SetResolution(dpiX, dpiY)

        Dim rect As New Rectangle(0, 0, width, height)
        Dim data As BitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
        Try
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length)
        Finally
            bitmap.UnlockBits(data)
        End Try

        Return bitmap
    End Function

    Private Function LoadBitmapViaImageSharp(stream As MemoryStream) As Bitmap
        stream.Position = 0

        Using imageSharpBitmap As SixLabors.ImageSharp.Image(Of SixLabors.ImageSharp.PixelFormats.Bgra32) =
            SixLabors.ImageSharp.Image.Load(Of SixLabors.ImageSharp.PixelFormats.Bgra32)(stream)

            Using pngStream As New MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(imageSharpBitmap, pngStream)
                pngStream.Position = 0

                Using pngImage As Image = Image.FromStream(pngStream)
                    Return New Bitmap(pngImage)
                End Using
            End Using
        End Using
    End Function

End Class
#End If
