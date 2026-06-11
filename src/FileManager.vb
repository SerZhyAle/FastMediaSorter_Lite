Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

Public Module FileManager

    ''' <summary>
    ''' Loads an image from file and returns a Tuple with the Image and its MemoryStream.
    ''' </summary>
    Public Function LoadImageWithStream(filePath As String) As Tuple(Of Image, IO.MemoryStream)
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0036: LoadImage file not found")
            AppFileLogger.WriteLine("LoadImageWithStream: file not found: " & If(filePath, "(null)"))
            Return Nothing
        End If

        Try
            Dim extension As String = Path.GetExtension(filePath).ToLowerInvariant()
            AppFileLogger.WriteLine("LoadImageWithStream: file=" & filePath & " extension=" & extension)
            Dim imageBytes As Byte() = File.ReadAllBytes(filePath)
            Dim ms As New IO.MemoryStream(imageBytes)
            Dim nextImage As Image

            If extension = ".webp" Then
                nextImage = LoadBitmapPortable(ms)
            Else
                nextImage = Image.FromStream(ms)
            End If

            If nextImage Is Nothing Then
                ms.Dispose()
                Return Nothing
            End If

            If nextImage.RawFormat.Equals(ImageFormat.Gif) Then
                Try
                    Dim frameCount As Integer = nextImage.GetFrameCount(FrameDimension.Time)
                Catch ex As Exception
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0902: Error reading GIF: " & ex.Message)
                    nextImage?.Dispose()
                    ms?.Dispose()
                    'lbl_Status.Text = If(is_Russian_Language, "Ошибка чтения GIF: " & ex.Message, "Error reading GIF: " & ex.Message)
                    Return Nothing
                End Try
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0040: LoadImage end ")

            Return Tuple.Create(nextImage, ms)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " E041: ERR loading image " & ex.Message)
            AppFileLogger.LogException("LoadImageWithStream failed for: " & filePath, ex)
            Return Nothing
        End Try
    End Function

    Private Function LoadBitmapPortable(stream As IO.MemoryStream) As Bitmap
        Try
            Return LoadBitmapViaWic(stream)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0038: WEBP via WIC failed, fallback to ImageSharp: " & ex.Message)
            AppFileLogger.LogException("WEBP via WIC failed; trying ImageSharp fallback", ex)
            Return LoadBitmapViaImageSharp(stream)
        End Try
    End Function

    Private Function LoadBitmapViaWic(stream As IO.MemoryStream) As Bitmap
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

    Private Function LoadBitmapViaImageSharp(stream As IO.MemoryStream) As Bitmap
        stream.Position = 0

        Using imageSharpBitmap As SixLabors.ImageSharp.Image(Of SixLabors.ImageSharp.PixelFormats.Bgra32) =
            SixLabors.ImageSharp.Image.Load(Of SixLabors.ImageSharp.PixelFormats.Bgra32)(stream)

            Using pngStream As New IO.MemoryStream()
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(imageSharpBitmap, pngStream)
                pngStream.Position = 0

                Using pngImage As Image = Image.FromStream(pngStream)
                    Return New Bitmap(pngImage)
                End Using
            End Using
        End Using
    End Function

    ''' <summary>
    ''' Renames a file and returns the new full path.
    ''' </summary>
    Public Function RenameFile(currentFileName As String, newFileName As String) As String
        Dim directory As String = Path.GetDirectoryName(currentFileName)
        Dim fileExtension As String = Path.GetExtension(currentFileName)
        Dim newFullPath As String = Path.Combine(directory, newFileName & fileExtension)
        If newFullPath = currentFileName Then Return currentFileName
        File.Move(currentFileName, newFullPath)
        Return newFullPath
    End Function

    ''' <summary>
    ''' Deletes a file if it exists.
    ''' </summary>
    Public Sub DeleteFile(filePath As String)
        If File.Exists(filePath) Then
            File.Delete(filePath)
        End If
    End Sub

    ''' <summary>
    ''' Copies a file to a new location.
    ''' </summary>
    Public Sub CopyFile(sourceFile As String, destFile As String, Optional overwrite As Boolean = False)
        File.Copy(sourceFile, destFile, overwrite)
    End Sub

    ''' <summary>
    ''' Moves a file to a new location.
    ''' </summary>
    Public Sub MoveFile(sourceFile As String, destFile As String)
        File.Move(sourceFile, destFile)
    End Sub

End Module
