#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading.Tasks

''' <summary>
''' Puts an encoded image on disk without ever being able to leave half a photo behind,
''' and answers - before a button is pressed - whether writing over the original is going
''' to be possible at all (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §9.3, §9.6).
'''
''' It is a module rather than code inside the editor window on purpose: this is the
''' dangerous half of the whole feature (invariant 2, "the original is never damaged"),
''' and it is the half a test can drive without a UI.
''' </summary>
Friend Module ImageFileWriter

    ''' <summary>
    ''' Marker in the name of the file the encoder actually writes. Distinctive so a
    ''' leftover after a power cut is recognisable next to the photo rather than looking
    ''' like a stray copy.
    ''' </summary>
    Friend Const TempMarker As String = ".fms-tmp"

    ' Unique neighbor, with the real extension last. CreateNew establishes ownership;
    ' generating a name alone does not reserve it.
    Friend Function TempPathFor(targetPath As String) As String
        Dim directory As String = Path.GetDirectoryName(targetPath)
        Dim name As String = Path.GetFileNameWithoutExtension(targetPath) & TempMarker & "-" & Guid.NewGuid().ToString("N") & Path.GetExtension(targetPath)
        Return If(String.IsNullOrEmpty(directory), name, Path.Combine(directory, name))
    End Function

    ''' <summary>Why "Save" is not available. One case per line so the UI can say which.</summary>
    Friend Enum ReplaceRefusal
        None
        ''' <summary>We can read this format but deliberately do not write it (§9.1).</summary>
        FormatNotWritable
        ''' <summary>The file carries the read-only attribute.</summary>
        ReadOnlyAttribute
        ''' <summary>ACL, read-only medium - the open-for-write probe was refused.</summary>
        AccessDenied
        ''' <summary>Another program holds it, or the share went away mid-probe.</summary>
        Locked
        ''' <summary>Nothing at that path.</summary>
        Missing
        ''' <summary>More than one page or frame - a multi-page TIFF, an animated PNG or
        ''' WebP. The editor works on the first one, so writing the result over the file
        ''' would silently throw every other page away (audit 046, G-02).</summary>
        MultiFrame
        ''' <summary>The path is an archive entry's temporary copy. Writing over it changes
        ''' nothing in the archive and is re-extracted over at once (audit 046, G-03).</summary>
        InsideArchive
    End Enum

    ''' <summary>
    ''' Can the result be laid over the original?
    '''
    ''' The application's habit everywhere else is "try it and show the exception", and for
    ''' a button that is not enough - it has to be disabled BEFORE the click, with a reason.
    ''' So this actually opens the file for writing, exclusively, and closes it again: that
    ''' single probe catches the permissions, the read-only medium and somebody else's lock,
    ''' none of which the attributes alone would show.
    '''
    ''' Called ONCE, when the editor opens. Re-probing on every repaint would be a round
    ''' trip to the network share per frame.
    '''
    ''' A network hiccup counts as "no": offering "Save as.." is better than promising a
    ''' replacement and failing it half way.
    ''' </summary>
    ' NOTE: no parameter here may be called "path" - VB is case-insensitive and the name
    ' would shadow System.IO.Path.
    Friend Function CanReplaceOriginal(filePath As String) As ReplaceRefusal
        If String.IsNullOrEmpty(filePath) Then Return ReplaceRefusal.Missing
        If ArchivePaths.IsUnderArchiveCache(filePath) Then Return ReplaceRefusal.InsideArchive
        If Not ImageEncoderProvider.IsWritableExtension(filePath) Then Return ReplaceRefusal.FormatNotWritable

        Try
            If Not File.Exists(filePath) Then Return ReplaceRefusal.Missing
            If (File.GetAttributes(filePath) And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                Return ReplaceRefusal.ReadOnlyAttribute
            End If
            If HasMoreThanOneFrame(filePath) Then Return ReplaceRefusal.MultiFrame
        Catch ex As UnauthorizedAccessException
            Return ReplaceRefusal.AccessDenied
        Catch ex As IOException
            Return ReplaceRefusal.Locked
        End Try

        Try
            Using probe As New FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            End Using
        Catch ex As UnauthorizedAccessException
            Return ReplaceRefusal.AccessDenied
        Catch ex As IOException
            Return ReplaceRefusal.Locked
        End Try

        Return ReplaceRefusal.None
    End Function

    ''' <summary>
    ''' Encodes and puts the result at <paramref name="targetPath"/>, off the UI thread.
    '''
    ''' The owner's destination is a second SMB share, so the bytes go over the wire and
    ''' the window must not sit at "not responding" while they do - the same reasoning
    ''' FileOpQueue was written for.
    ''' </summary>
    Friend Async Function SaveAsync(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                                    orientation As ExifOrientationPolicy) As Task
        Await Task.Run(Sub() Save(bitmap, targetPath, exifSource, orientation)).ConfigureAwait(True)
    End Function

    ''' <summary>
    ''' Write beside, then swap. The original is never opened for writing directly: an
    ''' interruption half way - the share blinks, the disk fills, the power goes - would
    ''' otherwise leave half a photo where the photo was.
    '''
    ''' A failed ENCODE takes the temporary file with it: the original was never touched.
    ''' A failed SWAP is judged by what is on disk afterwards - see <see cref="SwapInto"/>.
    ''' </summary>
    Friend Sub Save(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                    orientation As ExifOrientationPolicy)
        SaveWithEncoder(bitmap, targetPath, exifSource, orientation, ImageEncoderProvider.Current)
    End Sub

    Friend Sub SaveWithEncoder(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                               orientation As ExifOrientationPolicy, encoder As IImageEncoder)
        Dim temporary As String = TempPathFor(targetPath)
        ' CreateNew must succeed before cleanup is allowed. Keep this same handle through
        ' encoding and swapping: Delete sharing permits renames but denies read/write opens.
        Dim reservation As New FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete)
        Using reservation
            Try
                encoder.EncodeToStream(bitmap, reservation, Path.GetExtension(temporary), exifSource, orientation)
                reservation.Flush(flushToDisk:=True)
            Catch
                DeleteQuietly(temporary, Default_File_System)
                Throw
            End Try
            SwapInto(temporary, targetPath, Default_File_System)
        End Using
    End Sub

    ''' <summary>
    ''' The file-system calls the swap makes, one per method, so a test can make exactly one
    ''' of them fail at the worst moment - a failure AFTER the original has left its name,
    ''' which is the case audit 046 G-01 is about. Production uses this class as it is.
    ''' </summary>
    Friend Class SwapFileSystem
        Friend Overridable Function Exists(filePath As String) As Boolean
            Return File.Exists(filePath)
        End Function

        Friend Overridable Sub Move(sourcePath As String, destinationPath As String)
            File.Move(sourcePath, destinationPath)
        End Sub

        ''' <summary>Win32 ReplaceFile WITH a backup name: on failure it leaves both files
        ''' under their own names, or the original under the backup name - never nowhere,
        ''' which is what a call without a backup name may do (ERROR_UNABLE_TO_MOVE_REPLACEMENT:
        ''' "the replaced file no longer exists").</summary>
        Friend Overridable Sub Replace(sourcePath As String, destinationPath As String, backupPath As String)
            File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors:=True)
        End Sub

        Friend Overridable Sub Delete(filePath As String)
            File.Delete(filePath)
        End Sub
    End Class

    Private ReadOnly Default_File_System As New SwapFileSystem()

    ''' <summary>
    ''' The swap failed half way and the original could not be put back under its own name.
    ''' Nothing was deleted: the original is at <see cref="OriginalNowAt"/> and the edit at
    ''' <see cref="EditAt"/>, and the editor says so - "the original is untouched" would be
    ''' a false comfort, and "it is gone" would be false too.
    ''' </summary>
    Friend NotInheritable Class ImageSaveIncompleteException
        Inherits IOException

        Friend ReadOnly Property OriginalNowAt As String
        Friend ReadOnly Property EditAt As String

        Friend Sub New(originalNowAt As String, editAt As String, cause As Exception)
            MyBase.New("The edited image could not be put in place and the original could not be returned under its name.", cause)
            Me.OriginalNowAt = originalNowAt
            Me.EditAt = editAt
        End Sub
    End Class

    ''' <summary>
    ''' Puts the finished <paramref name="temporary"/> at <paramref name="targetPath"/>.
    '''
    ''' On failure the temporary file is deleted ONLY when the target still exists - then the
    ''' target holds the original and the edit is merely unsaved (it is still in the editor).
    ''' When the target is missing, every surviving byte is kept and reported: this used to be
    ''' an unconditional delete, and after a swap that failed past the point of removing the
    ''' original it destroyed the only remaining copy of the photo (audit 046, G-01).
    ''' </summary>
    Friend Sub SwapInto(temporary As String, targetPath As String, fs As SwapFileSystem)
        Try
            Swap(temporary, targetPath, fs)
        Catch ex As ImageSaveIncompleteException
            Throw
        Catch
            Dim targetStillThere As Boolean = False
            Try
                targetStillThere = fs.Exists(targetPath)
            Catch ex As Exception
                AppFileLogger.WriteLine("Image save: target could not be checked after failed swap - " & ex.Message)
            End Try
            If targetStillThere Then DeleteQuietly(temporary, fs)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' File.Replace keeps the destination's identity (ACL, creation time, streams) and is
    ''' the right call locally, but not every network file system implements it; the
    ''' fallback is rename-based and always has a way back. By this point the encoded bytes
    ''' are already complete on disk, which is the part that actually protects the photo.
    ''' </summary>
    Private Sub Swap(temporary As String, targetPath As String, fs As SwapFileSystem)
        If Not fs.Exists(targetPath) Then
            fs.Move(temporary, targetPath)
            Return
        End If

        Dim backup As String = BackupPathFor(targetPath, fs)
        Dim replaced As Boolean = False
        Try
            fs.Replace(temporary, targetPath, backup)
            replaced = True
        Catch ex As Exception
            ' Not an answer yet: where the failed Replace left the files is decided below.
            AppFileLogger.WriteLine(" w0042: File.Replace failed, falling back to renames: " & ex.Message)
        End Try

        If replaced Then
            ' The target is the edit and the backup the original, which is no longer needed.
            DeleteQuietly(backup, fs)
            Return
        End If

        ReplaceByRename(temporary, targetPath, backup, fs)
    End Sub

    ''' <summary>
    ''' The fallback: original to the backup name, edit to the original name, backup away.
    ''' Every step before the last is reversible, and is reversed on failure. The one state
    ''' a failed Replace itself can leave - the original already renamed to the backup and
    ''' the target name empty (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2) - is picked up where it is.
    ''' </summary>
    Private Sub ReplaceByRename(temporary As String, targetPath As String, backup As String, fs As SwapFileSystem)
        Dim created As DateTime? = Nothing
        Dim attributes As FileAttributes? = Nothing

        If fs.Exists(targetPath) Then
            Try
                created = File.GetCreationTimeUtc(targetPath)
                attributes = File.GetAttributes(targetPath)
            Catch ex As Exception
                AppFileLogger.WriteLine("Image save: original file attributes could not be read - " & ex.Message)
            End Try
            ' Throws with nothing changed yet; SwapInto then sees the target and cleans up.
            fs.Move(targetPath, backup)
        ElseIf Not fs.Exists(backup) Then
            ' The original is nowhere this code put it - the only thing left to protect is
            ' the edit, so it goes where the photo was.
            fs.Move(temporary, targetPath)
            Return
        End If

        Try
            fs.Move(temporary, targetPath)
        Catch moveFailure As Exception
            Try
                fs.Move(backup, targetPath)
            Catch
                Throw New ImageSaveIncompleteException(backup, temporary, moveFailure)
            End Try
            Throw
        End Try

        ' The rename gave the new file the folder's defaults; carry over what the user can
        ' see of the old one. Best effort - the pixels are already safe.
        Try
            If created.HasValue Then File.SetCreationTimeUtc(targetPath, created.Value)
            If attributes.HasValue Then File.SetAttributes(targetPath, attributes.Value And Not FileAttributes.ReadOnly)
        Catch ex As Exception
            AppFileLogger.WriteLine("Image save: original file attributes could not be restored - " & ex.Message)
        End Try
        DeleteQuietly(backup, fs)
    End Sub

    ''' <summary>Marker in the name of the original while it waits out a swap.</summary>
    Friend Const BackupMarker As String = ".fms-bak"

    ''' <summary>
    ''' Where the original waits during a swap: <c>photo.fms-bak.jpg</c>, or a free variant
    ''' of it when that exists - a leftover from a crash is somebody's original and is never
    ''' overwritten.
    ''' </summary>
    Friend Function BackupPathFor(targetPath As String, fs As SwapFileSystem) As String
        Dim directory As String = Path.GetDirectoryName(targetPath)
        Dim stem As String = Path.GetFileNameWithoutExtension(targetPath)
        Dim extension As String = Path.GetExtension(targetPath)
        For attempt As Integer = 1 To 99
            Dim marker As String = BackupMarker & If(attempt = 1, "", attempt.ToString(Globalization.CultureInfo.InvariantCulture))
            Dim candidate As String = stem & marker & extension
            candidate = If(String.IsNullOrEmpty(directory), candidate, Path.Combine(directory, candidate))
            If Not fs.Exists(candidate) Then Return candidate
        Next
        Dim unique As String = stem & BackupMarker & "-" & Guid.NewGuid().ToString("N") & extension
        Return If(String.IsNullOrEmpty(directory), unique, Path.Combine(directory, unique))
    End Function

    Private Sub DeleteQuietly(filePath As String, fs As SwapFileSystem)
        Try
            If fs.Exists(filePath) Then fs.Delete(filePath)
        Catch ex As Exception
            AppFileLogger.WriteLine(" w0041: leftover not removed: " & ex.Message)
        End Try
    End Sub

    ' ----------------------------------------------------------- frames (G-02) ----

    ''' <summary>
    ''' Does the file hold more than one page or frame? Only the writable formats that can:
    ''' TIFF (pages), PNG (APNG frames), WebP (animation). Header reads only - the chunks and
    ''' the IFD chain, never the pixels - because this runs once per editor opening on what
    ''' may be a network share.
    ''' </summary>
    Friend Function HasMoreThanOneFrame(filePath As String) As Boolean
        Dim extension As String = Path.GetExtension(filePath).ToLowerInvariant()
        Select Case extension
            Case ".png", ".webp", ".tif", ".tiff"
            Case Else
                Return False
        End Select

        Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            Select Case extension
                Case ".png"
                    Return PngIsAnimated(stream)
                Case ".webp"
                    Return WebpIsAnimated(stream)
                Case Else
                    Try
                        Using picture As Image = Image.FromStream(stream, useEmbeddedColorManagement:=False, validateImageData:=False)
                            Return picture.GetFrameCount(Drawing.Imaging.FrameDimension.Page) > 1
                        End Using
                    Catch ex As Exception When TypeOf ex Is ArgumentException OrElse
                                               TypeOf ex Is OutOfMemoryException OrElse
                                               TypeOf ex Is Runtime.InteropServices.ExternalException
                        ' Not a TIFF GDI+ can read (GDI+ reports that as any of these): the
                        ' editor could not have opened it either, so there is no page count
                        ' to protect.
                        Return False
                    End Try
            End Select
        End Using
    End Function

    ''' <summary>
    ''' An animated PNG announces itself with an <c>acTL</c> chunk, which the APNG
    ''' specification requires BEFORE the first <c>IDAT</c>. So this walks the chunk headers
    ''' up to the image data and stops - it never reads the pixels.
    ''' </summary>
    Friend Function PngIsAnimated(stream As Stream) As Boolean
        Dim signature(7) As Byte
        If ReadFully(stream, signature, 8) <> 8 Then Return False
        If signature(0) <> &H89 OrElse signature(1) <> &H50 OrElse signature(2) <> &H4E OrElse signature(3) <> &H47 Then Return False

        Dim header(7) As Byte
        ' A real PNG reaches IDAT within a handful of chunks; the cap only stops a
        ' malformed file from walking the stream forever.
        For chunk As Integer = 1 To 10000
            If ReadFully(stream, header, 8) <> 8 Then Return False
            Dim length As Long = (CLng(header(0)) << 24) Or (CLng(header(1)) << 16) Or (CLng(header(2)) << 8) Or CLng(header(3))
            Dim type As String = System.Text.Encoding.ASCII.GetString(header, 4, 4)
            If type = "acTL" Then Return True
            If type = "IDAT" OrElse type = "IEND" Then Return False
            If Not stream.CanSeek Then Return False
            stream.Seek(length + 4, SeekOrigin.Current)   ' data + CRC
        Next
        Return False
    End Function

    ''' <summary>
    ''' An animated WebP is the extended format (<c>VP8X</c> as the first chunk) with the
    ''' animation flag (bit 1 of the flags byte) set. A simple lossy or lossless WebP has
    ''' no VP8X chunk at all and is always one frame.
    ''' </summary>
    Friend Function WebpIsAnimated(stream As Stream) As Boolean
        Dim header(20) As Byte
        If ReadFully(stream, header, 21) <> 21 Then Return False
        If System.Text.Encoding.ASCII.GetString(header, 0, 4) <> "RIFF" Then Return False
        If System.Text.Encoding.ASCII.GetString(header, 8, 4) <> "WEBP" Then Return False
        If System.Text.Encoding.ASCII.GetString(header, 12, 4) <> "VP8X" Then Return False
        Return (header(20) And &H2) <> 0
    End Function

    Private Function ReadFully(stream As Stream, buffer As Byte(), count As Integer) As Integer
        Dim total As Integer = 0
        While total < count
            Dim read As Integer = stream.Read(buffer, total, count - total)
            If read <= 0 Then Exit While
            total += read
        End While
        Return total
    End Function

End Module
#End If
