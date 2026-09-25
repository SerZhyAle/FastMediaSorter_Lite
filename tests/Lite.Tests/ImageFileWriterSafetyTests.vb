#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' The editor's save, where it could destroy a photo (audit 046):
'''  * G-01 - a swap that failed AFTER the original had left its name, followed by an
'''    unconditional delete of the temporary file: the only copy of the photo, gone. The
'''    swap's file-system calls are scripted here so each can fail at its worst moment.
'''  * G-02 - "Save" over a multi-page TIFF or an animated PNG/WebP kept frame one only.
'''  * G-03 - an archive entry's temporary copy looked like an ordinary writable file.
''' Modern-only, like the editor.
''' </summary>
Public Class ImageFileWriterSafetyTests
    Implements IDisposable

    Private ReadOnly workDir As String
    Private ReadOnly target As String
    Private ReadOnly temporary As String

    Private Shared ReadOnly OriginalBytes As Byte() = {1, 1, 1, 1}
    Private Shared ReadOnly EditBytes As Byte() = {2, 2, 2, 2, 2, 2}

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-writer-safety-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(workDir)
        target = Path.Combine(workDir, "photo.png")
        temporary = ImageFileWriter.TempPathFor(target)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    ''' <summary>A swap file system whose Replace and chosen Moves fail on cue.</summary>
    Private NotInheritable Class ScriptedFileSystem
        Inherits ImageFileWriter.SwapFileSystem

        ''' <summary>"real", "throw" (nothing changed), or "rename-then-throw" - Win32's
        ''' ERROR_UNABLE_TO_MOVE_REPLACEMENT_2: the original is already at the backup name.</summary>
        Public ReplaceBehaviour As String = "real"
        Public ReadOnly FailingMoveSources As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Friend Overrides Sub Replace(sourcePath As String, destinationPath As String, backupPath As String)
            Select Case ReplaceBehaviour
                Case "throw"
                    Throw New IOException("simulated: this file system cannot replace")
                Case "rename-then-throw"
                    File.Move(destinationPath, backupPath)
                    Throw New IOException("simulated: ERROR_UNABLE_TO_MOVE_REPLACEMENT_2")
                Case Else
                    MyBase.Replace(sourcePath, destinationPath, backupPath)
            End Select
        End Sub

        Friend Overrides Sub Move(sourcePath As String, destinationPath As String)
            If FailingMoveSources.Contains(sourcePath) Then Throw New IOException("simulated: move refused")
            MyBase.Move(sourcePath, destinationPath)
        End Sub
    End Class

    Private Sub LayOut()
        File.WriteAllBytes(target, OriginalBytes)
        File.WriteAllBytes(temporary, EditBytes)
    End Sub

    Private Function Leftovers() As String()
        Return Directory.GetFiles(workDir).Select(Function(f) Path.GetFileName(f)).
                         Where(Function(n) Not String.Equals(n, "photo.png", StringComparison.OrdinalIgnoreCase)).ToArray()
    End Function

    ' --------------------------------------------------------------- G-01 ----

    <Fact>
    Public Sub A_working_replace_puts_the_edit_in_place_and_leaves_nothing_behind()
        LayOut()
        ImageFileWriter.SwapInto(temporary, target, New ScriptedFileSystem())

        Assert.Equal(EditBytes, File.ReadAllBytes(target))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub Without_replace_the_rename_fallback_still_finishes_the_job()
        LayOut()
        ImageFileWriter.SwapInto(temporary, target, New ScriptedFileSystem With {.ReplaceBehaviour = "throw"})

        Assert.Equal(EditBytes, File.ReadAllBytes(target))
        Assert.Empty(Leftovers())
    End Sub

    ''' <summary>The route that lost the photo: the original has already left its name
    ''' when the edit cannot take it. The original must come back.</summary>
    <Fact>
    Public Sub A_move_that_fails_after_the_original_left_puts_the_original_back()
        LayOut()
        Dim fs As New ScriptedFileSystem With {.ReplaceBehaviour = "throw"}
        fs.FailingMoveSources.Add(temporary)

        Assert.ThrowsAny(Of IOException)(Sub() ImageFileWriter.SwapInto(temporary, target, fs))

        Assert.Equal(OriginalBytes, File.ReadAllBytes(target))
        Assert.Empty(Leftovers())
    End Sub

    ''' <summary>And when even the way back is refused, nothing is deleted: both files stay,
    ''' and the exception names where they are.</summary>
    <Fact>
    Public Sub When_the_original_cannot_go_back_both_files_survive_and_are_named()
        LayOut()
        Dim fs As New ScriptedFileSystem With {.ReplaceBehaviour = "throw"}
        fs.FailingMoveSources.Add(temporary)
        Dim backup As String = ImageFileWriter.BackupPathFor(target, fs)
        fs.FailingMoveSources.Add(backup)

        Dim failure = Assert.Throws(Of ImageFileWriter.ImageSaveIncompleteException)(
            Sub() ImageFileWriter.SwapInto(temporary, target, fs))

        Assert.Equal(OriginalBytes, File.ReadAllBytes(failure.OriginalNowAt))
        Assert.Equal(EditBytes, File.ReadAllBytes(failure.EditAt))
    End Sub

    ''' <summary>ReplaceFile's own half-way state: the original already renamed to the
    ''' backup, the target name empty. The fallback picks it up from there.</summary>
    <Fact>
    Public Sub A_replace_that_stopped_half_way_is_completed()
        LayOut()
        ImageFileWriter.SwapInto(temporary, target, New ScriptedFileSystem With {.ReplaceBehaviour = "rename-then-throw"})

        Assert.Equal(EditBytes, File.ReadAllBytes(target))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_replace_that_stopped_half_way_and_cannot_finish_restores_the_original()
        LayOut()
        Dim fs As New ScriptedFileSystem With {.ReplaceBehaviour = "rename-then-throw"}
        fs.FailingMoveSources.Add(temporary)

        Assert.ThrowsAny(Of IOException)(Sub() ImageFileWriter.SwapInto(temporary, target, fs))

        Assert.Equal(OriginalBytes, File.ReadAllBytes(target))
        Assert.Empty(Leftovers())
    End Sub

    <Fact>
    Public Sub A_leftover_backup_from_an_earlier_crash_is_never_overwritten()
        LayOut()
        Dim fs As New ScriptedFileSystem()
        Dim first As String = ImageFileWriter.BackupPathFor(target, fs)
        File.WriteAllBytes(first, New Byte() {7, 7, 7})

        Dim second As String = ImageFileWriter.BackupPathFor(target, fs)

        Assert.NotEqual(first, second)
        ImageFileWriter.SwapInto(temporary, target, New ScriptedFileSystem With {.ReplaceBehaviour = "throw"})
        Assert.Equal(New Byte() {7, 7, 7}, File.ReadAllBytes(first))
        Assert.Equal(EditBytes, File.ReadAllBytes(target))
    End Sub

    ' --------------------------------------------------------------- G-02 ----

    <Fact>
    Public Sub A_two_page_tiff_is_refused()
        Dim tiff As String = Path.Combine(workDir, "scan.tiff")
        Dim codec As ImageCodecInfo = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Tiff.Guid)
        Using page1 As New Bitmap(8, 8), page2 As New Bitmap(8, 8)
            Using parameters As New EncoderParameters(1)
                parameters.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.MultiFrame))
                page1.Save(tiff, codec, parameters)
                parameters.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.FrameDimensionPage))
                page1.SaveAdd(page2, parameters)
                parameters.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.Flush))
                page1.SaveAdd(parameters)
            End Using
        End Using

        Assert.Equal(ImageFileWriter.ReplaceRefusal.MultiFrame, ImageFileWriter.CanReplaceOriginal(tiff))
    End Sub

    <Fact>
    Public Sub A_single_page_tiff_is_still_writable()
        Dim tiff As String = Path.Combine(workDir, "one.tiff")
        Using page As New Bitmap(8, 8)
            page.Save(tiff, ImageFormat.Tiff)
        End Using

        Assert.Equal(ImageFileWriter.ReplaceRefusal.None, ImageFileWriter.CanReplaceOriginal(tiff))
    End Sub

    <Fact>
    Public Sub A_two_frame_apng_is_refused()
        Dim apng As String = Path.Combine(workDir, "moving.png")
        Using image As New SixLabors.ImageSharp.Image(Of SixLabors.ImageSharp.PixelFormats.Rgba32)(8, 8)
            image.Frames.AddFrame(image.Frames.RootFrame)
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, apng)
        End Using
        Using stream As New FileStream(apng, FileMode.Open, FileAccess.Read)
            Assert.True(ImageFileWriter.PngIsAnimated(stream), "The fixture is not an APNG - the test would prove nothing.")
        End Using

        Assert.Equal(ImageFileWriter.ReplaceRefusal.MultiFrame, ImageFileWriter.CanReplaceOriginal(apng))
    End Sub

    <Fact>
    Public Sub An_animated_webp_header_is_recognised_and_a_still_one_is_not()
        Dim animated As Byte() = WebpHeader("VP8X", &H2)
        Dim extendedStill As Byte() = WebpHeader("VP8X", &H10)   ' alpha flag only
        Dim simple As Byte() = WebpHeader("VP8 ", 0)

        Assert.True(ImageFileWriter.WebpIsAnimated(New MemoryStream(animated)))
        Assert.False(ImageFileWriter.WebpIsAnimated(New MemoryStream(extendedStill)))
        Assert.False(ImageFileWriter.WebpIsAnimated(New MemoryStream(simple)))
        Assert.False(ImageFileWriter.WebpIsAnimated(New MemoryStream(New Byte() {1, 2, 3})))
    End Sub

    Private Shared Function WebpHeader(firstChunk As String, flags As Byte) As Byte()
        Dim bytes(29) As Byte
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0)
        System.Text.Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8)
        System.Text.Encoding.ASCII.GetBytes(firstChunk).CopyTo(bytes, 12)
        bytes(20) = flags
        Return bytes
    End Function

    ' --------------------------------------------------------------- G-03 ----

    <Fact>
    Public Sub An_archive_entrys_temporary_copy_is_refused_before_anything_is_read()
        ' Nothing is created under the real cache: the refusal is decided by the path.
        Dim entryCopy As String = Path.Combine(ArchivePaths.CacheRoot(), "1234-abcdef01", "00042_page12.png")

        Assert.Equal(ImageFileWriter.ReplaceRefusal.InsideArchive, ImageFileWriter.CanReplaceOriginal(entryCopy))
    End Sub

    <Theory>
    <InlineData("C:\cache\archive-cache", "C:\cache\archive-cache\1-a\x.jpg", True)>
    <InlineData("C:\cache\archive-cache", "C:\CACHE\Archive-Cache\1-a\x.jpg", True)>
    <InlineData("C:\cache\archive-cache", "C:\cache\archive-cache", True)>
    <InlineData("C:\cache\archive-cache\", "C:\cache\archive-cache\x.jpg", True)>
    <InlineData("C:\cache\archive-cache", "C:\cache\archive-cache-old\x.jpg", False)>
    <InlineData("C:\cache\archive-cache", "C:\cache\x.jpg", False)>
    <InlineData("C:\cache\archive-cache", "C:\cache\archive-cache\..\x.jpg", False)>
    <InlineData("C:\cache\archive-cache", "", False)>
    Public Sub IsUnderArchiveCache_is_a_prefix_test_on_whole_folders(root As String, candidate As String, expected As Boolean)
        Assert.Equal(expected, ArchivePaths.IsUnderArchiveCache(candidate, root))
    End Sub

End Class
#End If
