#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports Xunit
#If COMPANION_TESTS Then
Imports FastMediaSorterCompanion
#End If

Public Class VerifiedZipWriterTests
    Implements IDisposable
    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-verified-" & Guid.NewGuid().ToString("N"))
    Public Sub New()
        Directory.CreateDirectory(root)
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Directory.Delete(root, True)
    End Sub

    Private Shared Function Input(name As String, text As String) As ArchiveWriteInput
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(text)
        Return New ArchiveWriteInput With {.Name = name, .Length = bytes.Length, .ModifiedUtc = DateTime.UtcNow,
            .OpenRead = Function(ct) New MemoryStream(bytes, False)}
    End Function

    <Fact>
    Public Sub Verified_artifact_preserves_names_empty_entries_and_source_digests()
        Dim inputs As ArchiveWriteInput() = {Input("folder/ordinary.txt", "exact bytes"), Input("folder/zero.bin", ""),
            New ArchiveWriteInput With {.Name = "folder/empty/", .IsDirectory = True}}
        Dim artifact As VerifiedZipArtifact = VerifiedZipWriter.Write(Path.Combine(root, "a.part"), inputs, CancellationToken.None)
        Assert.Equal(3, artifact.Records.Count)
        Assert.Equal(11L, artifact.Bytes)
        Assert.Equal(Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("exact bytes")), artifact.Records(0).Sha256)
        VerifiedZipWriter.Verify(artifact.Path, artifact.Records, CancellationToken.None)
        Using zip As ZipArchive = ZipFile.OpenRead(artifact.Path)
            Assert.Equal(inputs.Select(Function(e) e.Name), zip.Entries.Select(Function(e) e.FullName))
        End Using
    End Sub

    <Fact>
    Public Sub Same_length_corruption_and_duplicate_readback_names_cannot_verify()
        Dim artifact As VerifiedZipArtifact = VerifiedZipWriter.Write(Path.Combine(root, "a.part"), {Input("a", "good")}, CancellationToken.None)
        Using zip As ZipArchive = ZipFile.Open(artifact.Path, ZipArchiveMode.Update)
            zip.Entries(0).Delete()
            Using writer As New StreamWriter(zip.CreateEntry("a").Open())
                writer.Write("evil")
            End Using
        End Using
        Assert.Throws(Of ArchiveSafetyException)(Sub() VerifiedZipWriter.Verify(artifact.Path, artifact.Records, CancellationToken.None))
        Using zip As ZipArchive = ZipFile.Open(artifact.Path, ZipArchiveMode.Update)
            zip.CreateEntry("a")
        End Using
        Assert.Throws(Of ArchiveSafetyException)(Sub() VerifiedZipWriter.Verify(artifact.Path, artifact.Records, CancellationToken.None))
    End Sub

    <Fact>
    Public Sub A_source_changed_before_or_after_reading_fails_the_whole_artifact()
        For Each changedAt As Integer In {1, 2}
            Dim calls As Integer
            Dim entry As ArchiveWriteInput = Input("a", "bytes")
            entry.ValidateVersion = Sub()
                                        calls += 1
                                        If calls = changedAt Then Throw New IOException("changed")
                                    End Sub
            Dim stage As String = Path.Combine(root, changedAt.ToString() & ".part")
            Assert.Throws(Of IOException)(Sub() VerifiedZipWriter.Write(stage, {entry}, CancellationToken.None))
            Assert.False(File.Exists(stage))
        Next
    End Sub

    <Fact>
    Public Sub Unreadable_growing_and_short_inputs_leave_no_archive()
        For Each length As Long In {0L, 99L}
            Dim entry As ArchiveWriteInput = Input("a", "bytes")
            entry.Length = length
            Dim stage As String = Path.Combine(root, length.ToString() & ".part")
            Assert.Throws(Of ArchiveSafetyException)(Sub() VerifiedZipWriter.Write(stage, {entry}, CancellationToken.None))
            Assert.False(File.Exists(stage))
        Next
        Dim unreadable As ArchiveWriteInput = Input("a", "bytes")
        unreadable.OpenRead = Function(ct) ThrowUnreadable()
        Assert.Throws(Of IOException)(Sub() VerifiedZipWriter.Write(Path.Combine(root, "fail.part"), {unreadable}, CancellationToken.None))
    End Sub
    Private Shared Function ThrowUnreadable() As Stream
        Throw New IOException("unreadable")
    End Function

    <Theory>
    <InlineData("../a")> <InlineData("/a")> <InlineData("a//b")> <InlineData("C:/a")>
    <InlineData("a:stream")> <InlineData("NUL.txt")> <InlineData("COM¹.txt")> <InlineData("a.")>
    Public Sub Unsafe_names_fail_before_opening_sources(name As String)
        Dim entry As ArchiveWriteInput = Input(name, "bytes")
        entry.OpenRead = Function(ct) ThrowUnreadable()
        Assert.Throws(Of ArchiveSafetyException)(Sub() VerifiedZipWriter.Write(Path.Combine(root, "a.part"), {entry}, CancellationToken.None))
        Assert.Empty(Directory.GetFiles(root))
    End Sub

    <Theory>
    <InlineData("A", "a")> <InlineData("a", "a/b")>
    Public Sub Ambiguous_names_are_rejected_before_writing(first As String, second As String)
        Assert.Throws(Of ArchiveSafetyException)(Sub() VerifiedZipWriter.Write(Path.Combine(root, "a.part"), {Input(first, ""), Input(second, "")}, CancellationToken.None))
        Assert.Empty(Directory.GetFiles(root))
    End Sub

    <Fact>
    Public Sub Cancellation_and_an_existing_stage_preserve_unowned_data()
        Dim stage As String = Path.Combine(root, "a.part")
        File.WriteAllText(stage, "unowned")
        Assert.Throws(Of IOException)(Sub() VerifiedZipWriter.Write(stage, {Input("a", "a")}, CancellationToken.None))
        Assert.Equal("unowned", File.ReadAllText(stage))
        File.Delete(stage)
        Using cts As New CancellationTokenSource()
            Dim entry As ArchiveWriteInput = Input("a", "a")
            entry.ValidateVersion = Sub() cts.Cancel()
            Assert.ThrowsAny(Of OperationCanceledException)(Sub() VerifiedZipWriter.Write(stage, {entry}, cts.Token))
            Assert.False(File.Exists(stage))
        End Using
    End Sub
End Class
#End If
