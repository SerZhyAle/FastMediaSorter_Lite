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

    ''' <summary>
    ''' Where the encoder writes before the swap: <c>photo.fms-tmp.jpg</c>, NOT
    ''' <c>photo.jpg.fms-tmp</c>.
    '''
    ''' The marker goes BEFORE the extension, and that is not cosmetic. The encoder picks
    ''' the format from the extension of the path it is handed, so a name ending in
    ''' ".fms-tmp" is a format it does not know and every save fails - which is exactly what
    ''' happened, because the sketch in §9.6 of the specification appends the suffix. The
    ''' real extension has to stay last.
    ''' </summary>
    Friend Function TempPathFor(targetPath As String) As String
        Dim directory As String = Path.GetDirectoryName(targetPath)
        Dim name As String = Path.GetFileNameWithoutExtension(targetPath) & TempMarker & Path.GetExtension(targetPath)
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
        If Not ImageEncoderProvider.IsWritableExtension(filePath) Then Return ReplaceRefusal.FormatNotWritable

        Try
            If Not File.Exists(filePath) Then Return ReplaceRefusal.Missing
            If (File.GetAttributes(filePath) And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                Return ReplaceRefusal.ReadOnlyAttribute
            End If
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
    ''' Both failure paths are covered by the Finally: a throw from the encoder and a throw
    ''' from the swap leave the original untouched and take the temporary file with them.
    ''' </summary>
    Friend Sub Save(bitmap As Bitmap, targetPath As String, exifSource As Byte(),
                    orientation As ExifOrientationPolicy)
        Dim temporary As String = TempPathFor(targetPath)
        Try
            ImageEncoderProvider.Current.EncodeToFile(bitmap, temporary, exifSource, orientation)
            Swap(temporary, targetPath)
        Finally
            ' Only ever reached with the temporary still in place when something threw -
            ' a successful swap has already consumed it.
            Try
                If File.Exists(temporary) Then File.Delete(temporary)
            Catch ex As Exception
                Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0041: leftover temp not removed: " & ex.Message)
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' File.Replace keeps the destination's identity and is the right call locally, but it
    ''' is not implemented on every network file system. The fallback loses the atomicity
    ''' the API gave us, so it is second, not first - and by then the encoded bytes are
    ''' already complete on disk, which is the part that actually protects the photo.
    ''' </summary>
    Private Sub Swap(temporary As String, targetPath As String)
        If Not File.Exists(targetPath) Then
            File.Move(temporary, targetPath)
            Return
        End If

        Try
            File.Replace(temporary, targetPath, Nothing, ignoreMetadataErrors:=True)
        Catch ex As PlatformNotSupportedException
            ReplaceByMove(temporary, targetPath)
        Catch ex As IOException
            ReplaceByMove(temporary, targetPath)
        End Try
    End Sub

    Private Sub ReplaceByMove(temporary As String, targetPath As String)
        File.Delete(targetPath)
        File.Move(temporary, targetPath)
    End Sub

End Module
#End If
