Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices

Public Module FileManager

    ''' <summary>
    ''' Loads an image from file and returns a Tuple with the Image and its MemoryStream.
    ''' </summary>
    Public Function LoadImageWithStream(filePath As String) As Tuple(Of Image, IO.MemoryStream)
        Dim ignored As PathFailureKind
        Return LoadImageWithStream(filePath, ignored)
    End Function

    ''' <summary>
    ''' LoadImageWithStream that also says WHY it returned Nothing (audit 048, A-06).
    '''
    ''' The loader used to catch everything and return Nothing, so the display path could only
    ''' call every failure "Content" - and a photo still being copied into the folder (a
    ''' sharing violation) or an ACL-restricted file on a NAS was dropped from the list as
    ''' "not a picture". Now a failure to READ the bytes is classified by PathFailure (Locked,
    ''' Denied, Transport, Missing ..), while a failure to DECODE them stays Content - a
    ''' decoder that trips over a truncated file throws EndOfStreamException, an IOException,
    ''' and must not be mistaken for a dropped share.
    ''' </summary>
    ''' <param name="failure">None on success; otherwise the class of the failure.</param>
    Public Function LoadImageWithStream(filePath As String, ByRef failure As PathFailureKind) As Tuple(Of Image, IO.MemoryStream)
        Return LoadImageCore(filePath, failure, ImageLoadStage.Whole)
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' The same loader for a caller that does not need a whole long animation (spec 241): the viewer's
    ''' display and prefetch ask <c>Staged</c>, the thumbnails <c>FirstFrame</c>. A picture that comes
    ''' back as the first frame of an animation is marked - <see cref="AnimationStaging.InfoOf"/>.
    ''' Modern only: the net48 exe keeps <see cref="LoadImageWithStream"/> and decodes everything.
    ''' </summary>
    Friend Function LoadImageStaged(filePath As String, ByRef failure As PathFailureKind, stage As ImageLoadStage) As Tuple(Of Image, IO.MemoryStream)
        Return LoadImageCore(filePath, failure, stage)
    End Function
#End If

    Private Function LoadImageCore(filePath As String, ByRef failure As PathFailureKind, stage As ImageLoadStage) As Tuple(Of Image, IO.MemoryStream)
        failure = PathFailureKind.None
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0036: LoadImage file not found")
            AppFileLogger.WriteLine("LoadImageWithStream: file not found: " & If(filePath, "(null)"))
            failure = PathFailureKind.Missing
            Return Nothing
        End If

        ' Anything thrown past this point that is not classified below is about the bytes.
        failure = PathFailureKind.Content
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
            Dim ms As IO.MemoryStream = Nothing
            Dim nextImage As Image

            If ImageDecoderProvider.Decoder_Backed_Extensions.Contains(extension) Then
#If NETFRAMEWORK Then
                Dim source_Bytes As Byte() = ReadSourceBytes(filePath, failure)
                If source_Bytes Is Nothing Then Return Nothing
                ms = New IO.MemoryStream(source_Bytes)
                nextImage = ImageDecoderProvider.Current.DecodeToImage(ms)
                If ImageDecoderProvider.Current.ImageOwnsData AndAlso ms IsNot Nothing Then
                    ms.Dispose()
                    ms = Nothing
                End If
#Else
                ' The one modern-only branch of this function
                ' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §3.1).
                ' DecodeCache reads the file itself, because on a HIT it must not read it at
                ' all - the whole point is that the second view of an animated WEBP costs a
                ' cache read instead of a decode plus a GIF encode. The stream it returns is
                ' the PAYLOAD stream, which is strictly more correct than the source bytes
                ' returned here before: for an animation, the stream GDI+ actually reads was
                ' the invisible one, and the one the caller kept alive was dead weight.
                Dim viaCache As Tuple(Of Image, IO.MemoryStream) = DecodeCache.LoadImage(filePath, failure, stage)
                If viaCache Is Nothing Then Return Nothing
                nextImage = viaCache.Item1
                ms = viaCache.Item2
#End If
            Else
                Dim source_Bytes As Byte() = ReadSourceBytes(filePath, failure)
                If source_Bytes Is Nothing Then Return Nothing
                ms = New IO.MemoryStream(source_Bytes)
                Try
                    nextImage = Image.FromStream(ms)
                Catch ex As ArgumentException When ImageDecoderProvider.LooksDecoderBacked(source_Bytes)
                    ' A WEBP (or, on the mainline, an AVIF/HEIC) saved under a .jpg/.png name -
                    ' browsers do it all the time (audit 048, A-16). Routing is by extension,
                    ' GDI+ has no codec for those bytes on most editions, and the file was
                    ' dropped as undecodable while the managed decoder right behind the seam
                    ' would have opened it. The header, not the name, decides the retry.
                    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0037: GDI+ refused, header says decoder-backed: " & filePath)
#If NETFRAMEWORK Then
                    ms.Position = 0
                    nextImage = ImageDecoderProvider.Current.DecodeToImage(ms)
#Else
                    ms.Dispose()
                    ms = Nothing
                    Dim viaDecoder As Tuple(Of Image, IO.MemoryStream) = DecodeCache.LoadImage(filePath, failure, stage)
                    If viaDecoder Is Nothing Then Return Nothing
                    nextImage = viaDecoder.Item1
                    ms = viaDecoder.Item2
#End If
                End Try
            End If

            If nextImage Is Nothing Then
                ms?.Dispose()
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

            failure = PathFailureKind.None
            Return Tuple.Create(nextImage, ms)
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " E041: ERR loading image " & ex.Message)
            AppFileLogger.LogException("LoadImageWithStream failed for: " & filePath, ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' The file's bytes, or Nothing with <paramref name="failure"/> saying why the READ failed
    ''' (audit 048, A-06): a sharing violation is Locked, an ACL refusal Denied, a dropped
    ''' share Transport - all of them "keep the file" - and a file deleted in between Missing.
    ''' Only this read is classified; what the decoder later throws is about the content.
    ''' </summary>
    Friend Function ReadSourceBytes(filePath As String, ByRef failure As PathFailureKind) As Byte()
        Try
            Return File.ReadAllBytes(filePath)
        Catch ex As Exception
            failure = PathFailure.Classify(ex)
            ' An unclassifiable read failure is still a failure of the read, not a verdict on
            ' a picture nobody has seen - but Unknown keeps today's "drop it" on purpose (see
            ' PathFailureKind.Unknown), so it is passed on as it is.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " E042: could not read [" & failure.ToString() & "] " & filePath & ": " & ex.Message)
            AppFileLogger.LogException("Reading an image failed [" & failure.ToString() & "]: " & filePath, ex)
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
#If Not NETFRAMEWORK Then
        ' A file of an open remote resource is renamed THERE (006 PV-4); for anything else
        ' this returns False at once and the rename below is unchanged.
        Dim routed As String = Nothing
        If ResourceRouting.TryRename(currentFileName, newFileNameWithExtension, routed) Then Return routed
#End If
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
#If Not NETFRAMEWORK Then
        If ResourceRouting.TryDelete(filePath) Then Return
#End If
        If File.Exists(filePath) Then
            File.Delete(filePath)
        End If
    End Sub

    ''' <summary>
    ''' Undo of a copy: deletes the copy the application itself made. File.Copy carries the
    ''' source's attributes, so the copy of a camera's read-only ("protected") photo is
    ''' read-only too, and U answered "access denied" for a file the application had just
    ''' created (audit 057.S05-4). The attribute is dropped on THAT file only - owner
    ''' decision: a plain permanent delete of a protected file may keep refusing. The caller
    ''' has already asked <see cref="CopyUndoMayDelete(String, String)"/>.
    ''' </summary>
    Public Sub DeleteCopyForUndo(copyPath As String)
#If Not NETFRAMEWORK Then
        If ResourceRouting.TryDelete(copyPath) Then Return
#End If
        If Not File.Exists(copyPath) Then Return
        ClearReadOnly(copyPath)
        File.Delete(copyPath)
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' The worker's move: exactly <see cref="MoveFile"/>, plus - when a resource carried the
    ''' transfer - the typed outcome it ended with (100 §15.6), which is what the conditional
    ''' undo is built from. Nothing for a plain local transfer.
    ''' </summary>
    Friend Sub MoveFileTracked(sourceFile As String, destFile As String, overwrite As Boolean, ByRef outcome As TransferOutcome)
        If ResourceRouting.TryMove(sourceFile, destFile, overwrite, outcome) Then Return
        MoveFile(sourceFile, destFile, overwrite)
    End Sub

    ''' <summary>The tracked form of <see cref="CopyFile"/> - see <see cref="MoveFileTracked"/>.</summary>
    Friend Sub CopyFileTracked(sourceFile As String, destFile As String, overwrite As Boolean, ByRef outcome As TransferOutcome)
        If ResourceRouting.TryCopy(sourceFile, destFile, overwrite, outcome) Then Return
        CopyFile(sourceFile, destFile, overwrite)
    End Sub
#End If

    ''' <summary>
    ''' Copies a file to a new location - staged, exactly like a cross-volume move (audit
    ''' 047, C-08). A File.Copy straight onto the final name left a truncated file there
    ''' when the copy was interrupted - the app closed with "No" during a 200 MB copy to a
    ''' share, a crash, a kill - and it looked like the real thing, so the user could later
    ''' delete the original trusting it. With "replace" the old destination had already
    ''' been cut short too. Now the final name only ever holds a complete file.
    ''' </summary>
    Public Sub CopyFile(sourceFile As String, destFile As String, Optional overwrite As Boolean = False)
#If Not NETFRAMEWORK Then
        If ResourceRouting.TryCopy(sourceFile, destFile, overwrite) Then Return
#End If
        ' "Replace" must never be allowed to mean "replace the file with itself" (C-02).
        If overwrite Then ThrowIfSameFile(sourceFile, destFile)
        ' Refused before a byte is copied: without "replace" an existing name was always an
        ' error, and finding it only after a 200 MB copy would waste the copy.
        If Not overwrite AndAlso File.Exists(destFile) Then Throw DestinationExists()
        CopyThroughStaging(sourceFile, destFile, overwrite)
    End Sub

    ''' <summary>
    ''' THE copy routine of every transfer that is not a same-volume rename: copy to
    ''' "&lt;dest&gt;.fms-part" in the destination folder, then rename that into place, deleting
    ''' the old destination only when replacing and only once the new bytes are complete.
    ''' An interruption leaves the finished file, or an obvious "*.fms-part" leftover -
    ''' never a plausible half file under the real name. Both the move and the copy use it,
    ''' so there is one staging rule and not two that drift.
    ''' </summary>
    Private Sub CopyThroughStaging(sourceFile As String, destFile As String, overwrite As Boolean)
        Dim staging As String = destFile & Partial_Move_Suffix
        Try
            ' A leftover of an earlier attempt carries the source's attributes (File.Copy
            ' copies them), so a read-only camera file leaves a read-only leftover.
            If File.Exists(staging) Then
                ClearReadOnly(staging)
                File.Delete(staging)
            End If
            File.Copy(sourceFile, staging, True)
            ' Rename within one folder is atomic enough: no window in which the final name
            ' exists but holds an incomplete file.
            If File.Exists(destFile) Then
                If Not overwrite Then Throw DestinationExists()
                ThrowIfSameFile(sourceFile, destFile)
                File.Delete(destFile)
            End If
            File.Move(staging, destFile)
        Catch
            ' Leave the source alone and take the debris with us: a failed transfer must not
            ' cost the file.
            Try
                If File.Exists(staging) Then
                    ClearReadOnly(staging)
                    File.Delete(staging)
                End If
            Catch
            End Try
            Throw
        End Try
    End Sub

    ''' <summary>"The destination file already exists", carrying ERROR_FILE_EXISTS so the
    ''' failure classifier can say so instead of blaming the connection (C-17).</summary>
    Private Function DestinationExists() As IOException
        Return New IOException("The destination file already exists.", HResultFileExists)
    End Function

    Private Const HResultFileExists As Integer = &H80070050

    ''' <summary>Drops the read-only attribute, so the file can be deleted. Used only on a
    ''' file that is being replaced or moved away - never on one that stays.</summary>
    Private Sub ClearReadOnly(filePath As String)
        Dim attributes As FileAttributes = File.GetAttributes(filePath)
        If (attributes And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
            File.SetAttributes(filePath, attributes And Not FileAttributes.ReadOnly)
        End If
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
#If Not NETFRAMEWORK Then
        If ResourceRouting.TryMove(sourceFile, destFile, overwrite) Then Return
#End If
        ' The same file under two spellings (a mapped drive and its UNC path, a server's
        ' name and its IP address) is a different ROOT string, so the staged path below ran:
        ' "replace" deleted the destination - which was the source - and the final delete
        ' took the copy that had just been put back (audit 046, C-02). A name collision with
        ' ITSELF is refused before anything is deleted.
        If overwrite AndAlso File.Exists(destFile) Then ThrowIfSameFile(sourceFile, destFile)

        If IsSameVolume(sourceFile, destFile) Then
#If NETFRAMEWORK Then
            If overwrite AndAlso File.Exists(destFile) Then File.Delete(destFile)
            File.Move(sourceFile, destFile)
#Else
            File.Move(sourceFile, destFile, overwrite)
#End If
            Return
        End If

        CopyThroughStaging(sourceFile, destFile, overwrite)

        ' Defence in depth: the source is deleted only when it is provably NOT the file that
        ' now sits at the destination.
        If IsSameFile(sourceFile, destFile) Then
            Throw New IOException("The source and the destination are the same file; it was left in place.")
        End If
        ' A read-only source - a camera's "protected" photo on an SD card, and card to disk
        ' is always this staged path - made File.Delete throw AFTER the copy had landed: the
        ' move was reported failed and rolled back, the file sat in both places, and every
        ' retry bred "x (2).jpg" (audit 047, C-16). The file is being moved, and its copy
        ' already carries the attribute, so dropping it on the source loses nothing.
        Dim source_Attributes As FileAttributes? = Nothing
        Try
            source_Attributes = File.GetAttributes(sourceFile)
            ClearReadOnly(sourceFile)
            File.Delete(sourceFile)
        Catch
            ' Any other reason the source will not go - a reader holds it (an AV scanner, the
            ' indexer, a second viewer): the move is reported failed and rolled back, and the
            ' complete copy used to stay at the destination, so the retry met a taken name
            ' and bred "x (2).jpg" (audit 057.S05-3). Without "replace" the destination did
            ' not exist before this call (CopyThroughStaging refuses a taken name), so the
            ' copy is ours to take back - but only while the source is provably still there:
            ' otherwise the copy is the one instance left. With "replace" the old destination
            ' is already gone, and the copy is kept: it is now the only thing at that name.
            Try
                If File.Exists(sourceFile) Then
                    If Not overwrite AndAlso File.Exists(destFile) Then
                        ClearReadOnly(destFile)
                        File.Delete(destFile)
                    End If
                    ' The read-only mark dropped above goes back on the file that stays.
                    If source_Attributes.HasValue Then File.SetAttributes(sourceFile, source_Attributes.Value)
                End If
            Catch
                ' The rollback is best effort; the failure that matters is rethrown below.
            End Try
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Are these two paths the same file on disk, whatever they are called? Decided by the
    ''' file system's own identity - volume serial number plus file index, with size and
    ''' write time as a check against a server that reports a meaningless index - never by
    ''' comparing strings: <c>Z:\photos</c>, <c>\\nas\share\photos</c>,
    ''' <c>\\nas.local\share\photos</c> and <c>\\?\Z:\photos</c> are four spellings of one
    ''' folder.
    '''
    ''' False when either identity cannot be read (a missing file, a share that does not
    ''' answer, a server that reports index 0): callers use it to REFUSE, so "unknown" must
    ''' fall back to what they did before rather than invent a refusal.
    ''' </summary>
    Friend Function IsSameFile(pathA As String, pathB As String) As Boolean
        Dim a As FileIdentity = Nothing
        Dim b As FileIdentity = Nothing
        If Not TryGetFileIdentity(pathA, a) OrElse Not TryGetFileIdentity(pathB, b) Then Return False
        Return a.Equals(b)
    End Function

    ''' <summary>
    ''' May undoing a copy delete the copy? Undo deletes it permanently, so only while the
    ''' original it duplicates is still in place and still the same size - otherwise the
    ''' "copy" has become the only instance of the file (the original was deleted, which on
    ''' a share is permanent) and U would destroy the last one (audit 046, C-04). A copy
    ''' that is gone already has nothing to lose, and the old behaviour (a no-op delete)
    ''' stands. The pure rule is <see cref="CopyUndoMayDelete(Boolean, Long, Boolean, Long, Boolean)"/>.
    ''' </summary>
    Friend Function CopyUndoMayDelete(originalPath As String, copyPath As String) As Boolean
#If Not NETFRAMEWORK Then
        ' A copy in a server folder (a remote slot, 007 §4.2) cannot be judged through FileInfo -
        ' it would read as "not there" and so as safe to delete. Such a copy is taken back only
        ' through its transfer record (TransferUndo); without one it stays.
        If ResourceLocator.IsLocator(copyPath) Then Return False
#End If
        Try
            Dim copy As New FileInfo(copyPath)
            Dim original As New FileInfo(originalPath)
            Dim copyExists As Boolean = copy.Exists
            Dim originalExists As Boolean = original.Exists
            Return CopyUndoMayDelete(originalExists, If(originalExists, original.Length, -1L),
                                     copyExists, If(copyExists, copy.Length, -1L),
                                     copyExists AndAlso originalExists AndAlso IsSameFile(originalPath, copyPath))
        Catch
            ' Cannot tell: keep the file. A refusal costs one more key press; a wrong
            ' deletion costs the photo.
            Return False
        End Try
    End Function

    ''' <summary>The rule itself, with the file system already asked.</summary>
    Friend Function CopyUndoMayDelete(originalExists As Boolean, originalLength As Long,
                                      copyExists As Boolean, copyLength As Long,
                                      sameFile As Boolean) As Boolean
        If Not copyExists Then Return True
        If Not originalExists OrElse sameFile Then Return False
        Return originalLength = copyLength
    End Function

    Private Sub ThrowIfSameFile(sourceFile As String, destFile As String)
        If IsSameFile(sourceFile, destFile) Then
            Throw New IOException("The source and the destination are the same file; nothing was replaced.")
        End If
    End Sub

    ''' <summary>What makes a file itself, as far as <see cref="IsSameFile"/> is concerned.</summary>
    Friend Structure FileIdentity
        Friend VolumeSerial As UInteger
        Friend FileIndex As ULong
        Friend Length As ULong
        Friend LastWriteTicks As Long
    End Structure

    ''' <summary>
    ''' Reads a file's identity through a handle opened for NO data access and with every
    ''' sharing mode - so it succeeds on a file another program (or a playing video) holds
    ''' open, and it never takes a lock of its own.
    ''' </summary>
    Friend Function TryGetFileIdentity(filePath As String, ByRef identity As FileIdentity) As Boolean
        If String.IsNullOrEmpty(filePath) Then Return False
        Try
            Using handle As Microsoft.Win32.SafeHandles.SafeFileHandle =
                FileIdentityNative.CreateFileW(filePath, 0UI,
                                               FileIdentityNative.FILE_SHARE_READ Or FileIdentityNative.FILE_SHARE_WRITE Or FileIdentityNative.FILE_SHARE_DELETE,
                                               IntPtr.Zero, FileIdentityNative.OPEN_EXISTING,
                                               FileIdentityNative.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero)
                If handle.IsInvalid Then Return False
                Dim info As FileIdentityNative.BY_HANDLE_FILE_INFORMATION = Nothing
                If Not FileIdentityNative.GetFileInformationByHandle(handle, info) Then Return False
                Dim index As ULong = (CULng(info.nFileIndexHigh) << 32) Or CULng(info.nFileIndexLow)
                ' Some SMB servers report 0 for every file; that is "unknown", not an identity.
                If index = 0UL Then Return False
                identity = New FileIdentity With {
                    .VolumeSerial = info.dwVolumeSerialNumber,
                    .FileIndex = index,
                    .Length = (CULng(info.nFileSizeHigh) << 32) Or CULng(info.nFileSizeLow),
                    .LastWriteTicks = (CLng(info.ftLastWriteTimeHigh) << 32) Or CLng(info.ftLastWriteTimeLow)
                }
                Return True
            End Using
        Catch
            Return False
        End Try
    End Function

    Private NotInheritable Class FileIdentityNative
        Friend Const FILE_SHARE_READ As UInteger = &H1UI
        Friend Const FILE_SHARE_WRITE As UInteger = &H2UI
        Friend Const FILE_SHARE_DELETE As UInteger = &H4UI
        Friend Const OPEN_EXISTING As UInteger = 3UI
        ' Lets the same call open a directory, and costs nothing for a file.
        Friend Const FILE_FLAG_BACKUP_SEMANTICS As UInteger = &H2000000UI

        <StructLayout(LayoutKind.Sequential)>
        Friend Structure BY_HANDLE_FILE_INFORMATION
            Friend dwFileAttributes As UInteger
            Friend ftCreationTimeLow As UInteger
            Friend ftCreationTimeHigh As UInteger
            Friend ftLastAccessTimeLow As UInteger
            Friend ftLastAccessTimeHigh As UInteger
            Friend ftLastWriteTimeLow As UInteger
            Friend ftLastWriteTimeHigh As UInteger
            Friend dwVolumeSerialNumber As UInteger
            Friend nFileSizeHigh As UInteger
            Friend nFileSizeLow As UInteger
            Friend nNumberOfLinks As UInteger
            Friend nFileIndexHigh As UInteger
            Friend nFileIndexLow As UInteger
        End Structure

        <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True, EntryPoint:="CreateFileW")>
        Friend Shared Function CreateFileW(lpFileName As String, dwDesiredAccess As UInteger, dwShareMode As UInteger,
                                           lpSecurityAttributes As IntPtr, dwCreationDisposition As UInteger,
                                           dwFlagsAndAttributes As UInteger, hTemplateFile As IntPtr) As Microsoft.Win32.SafeHandles.SafeFileHandle
        End Function

        <DllImport("kernel32.dll", SetLastError:=True, EntryPoint:="GetFileInformationByHandle")>
        Friend Shared Function GetFileInformationByHandle(hFile As Microsoft.Win32.SafeHandles.SafeFileHandle,
                                                          ByRef lpFileInformation As BY_HANDLE_FILE_INFORMATION) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
    End Class

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
