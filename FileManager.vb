Imports System.Drawing.Imaging
Imports System.IO

Public Module FileManager

    ''' <summary>
    ''' Loads an image from file and returns a Tuple with the Image and its MemoryStream.
    ''' </summary>
    Public Function LoadImageWithStream(filePath As String) As Tuple(Of Image, IO.MemoryStream)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0035: LoadImage begin " & filePath)
        If Not My.Computer.FileSystem.FileExists(filePath) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0036: LoadImage file not found")
            Return Nothing
        End If
        Try
            Dim imageBytes As Byte() = File.ReadAllBytes(filePath)
            Dim ms As New IO.MemoryStream(imageBytes)
            Dim nextImage As Image = Image.FromStream(ms)

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
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0041: ERR loading image " & ex.Message)
            Return Nothing
        End Try
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