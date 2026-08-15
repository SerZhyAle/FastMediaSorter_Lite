Imports System.Drawing.Imaging
Imports System.IO

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
            ' Debug, not AppFileLogger: this fires on EVERY image the app decodes - the one
            ' on screen, the prefetch of the next one, and every thumbnail the panel builds -
            ' so as a file-log line it was ~1-2.5 MB/day of unbounded growth plus a
            ' synchronous flush on the UI thread per file flip. Debug.WriteLine still reaches
            ' current.log in a Debug build (AppFileLogger hooks the trace listener) and
            ' compiles away in Release, which is exactly the intended reach. Failures below
            ' are still logged for real.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " LoadImageWithStream: file=" & filePath & " extension=" & extension)
            Dim imageBytes As Byte() = File.ReadAllBytes(filePath)
            Dim ms As New IO.MemoryStream(imageBytes)
            Dim nextImage As Image

            If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(extension) Then
                nextImage = ImageDecoderProvider.Current.DecodeToImage(ms)
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
            ElseIf Is_Exif_AutoRotate Then
                ' Orient JPEG/TIFF photos by their EXIF Orientation tag (phones and
                ' cameras store landscape pixels + a rotation flag). Skipped for GIF
                ' (animation) and harmless when the tag is absent.
                ApplyExifOrientation(nextImage)
            End If

            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0040: LoadImage end ")

            Return Tuple.Create(nextImage, ms)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " E041: ERR loading image " & ex.Message)
            AppFileLogger.LogException("LoadImageWithStream failed for: " & filePath, ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Rotates/flips the image in place to match its EXIF Orientation tag (0x0112)
    ''' and then strips the tag, so the displayed pixels are upright. No-op when the
    ''' tag is missing or already normal. Failures are swallowed - a bad tag must
    ''' never stop the image from loading.
    '''
    ''' Friend rather than Private because the image editor decodes its own copy from
    ''' the file's bytes (it must not take pixels from Picture_Box.Image) and has to
    ''' orient them by exactly this rule - two implementations of "upright" would be two
    ''' answers to what the saved Orientation tag should say.
    ''' </summary>
    Friend Sub ApplyExifOrientation(img As Image)
        Const OrientationId As Integer = &H112
        Try
            If img Is Nothing Then Return
            If Array.IndexOf(img.PropertyIdList, OrientationId) < 0 Then Return

            Dim orient As Integer = CInt(img.GetPropertyItem(OrientationId).Value(0))
            Dim rot As RotateFlipType
            Select Case orient
                Case 2 : rot = RotateFlipType.RotateNoneFlipX
                Case 3 : rot = RotateFlipType.Rotate180FlipNone
                Case 4 : rot = RotateFlipType.Rotate180FlipX
                Case 5 : rot = RotateFlipType.Rotate90FlipX
                Case 6 : rot = RotateFlipType.Rotate90FlipNone
                Case 7 : rot = RotateFlipType.Rotate270FlipX
                Case 8 : rot = RotateFlipType.Rotate270FlipNone
                Case Else : rot = RotateFlipType.RotateNoneFlipNone ' 1 or unknown
            End Select

            If rot <> RotateFlipType.RotateNoneFlipNone Then
                img.RotateFlip(rot)
                img.RemovePropertyItem(OrientationId)
            End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0903: EXIF orient skipped: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Renames a file and returns the path it actually created.
    ''' newFileNameWithExtension is the FINISHED file name, extension included -
    ''' nothing is appended to it. It used to append the source extension a second
    ''' time (the caller already passes one), so "photo2.jpg" landed on disk as
    ''' "photo2.jpg.jpg" while the caller recorded "photo2.jpg" and promptly lost the
    ''' file from the list.
    ''' </summary>
    Public Function RenameFile(currentFileName As String, newFileNameWithExtension As String) As String
        Dim directory As String = Path.GetDirectoryName(currentFileName)
        Dim newFullPath As String = Path.Combine(directory, newFileNameWithExtension)
        ' Ordinal on purpose: a case-only rename (photo -> Photo) is a real rename.
        If String.Equals(newFullPath, currentFileName, StringComparison.Ordinal) Then Return currentFileName
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
    '''
    ''' Across two shares Windows cannot rename, so this is a byte copy over the wire - the
    ''' owner's working setup (\\p7\_i\output to \\p7\down, 1-212 MB videos) is exactly that.
    ''' A File.Move killed part-way through therefore leaves a TRUNCATED file sitting under
    ''' the final name, indistinguishable from the real thing: the app cannot rule that out
    ''' (the process can be terminated at any instant - the shutdown drain gives up after
    ''' 15 s and closes anyway), so the move is staged instead.
    '''
    ''' Copy to a temporary name in the destination folder, rename into place, then delete
    ''' the source. Now an interruption leaves either the finished file, or the original plus
    ''' an obvious "*.fms-part" leftover - never a plausible-looking half file. Same-volume
    ''' moves keep the cheap path: there is no copy to interrupt.
    ''' </summary>
    Public Sub MoveFile(sourceFile As String, destFile As String, Optional overwrite As Boolean = False)
        If IsSameVolume(sourceFile, destFile) Then
#If NETFRAMEWORK Then
            If overwrite AndAlso File.Exists(destFile) Then File.Delete(destFile)
            File.Move(sourceFile, destFile)
#Else
            File.Move(sourceFile, destFile, overwrite)
#End If
            Return
        End If

        Dim staging As String = destFile & Partial_Move_Suffix
        Try
            If File.Exists(staging) Then File.Delete(staging)
            File.Copy(sourceFile, staging, True)
            ' Rename within one folder is atomic enough: no window in which the final name
            ' exists but holds an incomplete file.
            If File.Exists(destFile) Then
                If Not overwrite Then Throw New IOException("The destination file already exists.")
                File.Delete(destFile)
            End If
            File.Move(staging, destFile)
        Catch
            ' Leave the source alone and take the debris with us: a failed move must not cost
            ' the file.
            Try
                If File.Exists(staging) Then File.Delete(staging)
            Catch
            End Try
            Throw
        End Try

        File.Delete(sourceFile)
    End Sub

    ''' <summary>Extension given to the in-flight copy of a staged move.</summary>
    Public Const Partial_Move_Suffix As String = ".fms-part"

    ''' <summary>
    ''' Are both paths on the same volume, so a move is a rename rather than a copy? Compared
    ''' by path root, which is what decides it for both drive letters and UNC shares
    ''' (\\server\share). Unknown answers say False - staging a move that did not need it
    ''' costs a rename, while NOT staging one that did is the truncation this guards against.
    ''' </summary>
    Private Function IsSameVolume(pathA As String, pathB As String) As Boolean
        Try
            Dim rootA As String = Path.GetPathRoot(Path.GetFullPath(pathA))
            Dim rootB As String = Path.GetPathRoot(Path.GetFullPath(pathB))
            If String.IsNullOrEmpty(rootA) OrElse String.IsNullOrEmpty(rootB) Then Return False
            Return String.Equals(rootA.TrimEnd(Path.DirectorySeparatorChar),
                                 rootB.TrimEnd(Path.DirectorySeparatorChar),
                                 StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

End Module
