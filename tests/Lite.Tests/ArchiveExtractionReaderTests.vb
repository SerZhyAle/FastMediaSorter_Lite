#If Not NETFRAMEWORK Then
Option Strict On
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Threading
Imports Xunit

Public Class ArchiveExtractionReaderTests
    Implements IDisposable
    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-extraction-" & Guid.NewGuid().ToString("N"))
    Public Sub New()
        Directory.CreateDirectory(root)
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Directory.Delete(root, True)
    End Sub

    Private Function ZipWith(ParamArray names As String()) As String
        Dim path As String = IO.Path.Combine(root, "input.zip")
        Using zip As ZipArchive = ZipFile.Open(path, ZipArchiveMode.Create)
            For Each name As String In names
                Dim entry As ZipArchiveEntry = zip.CreateEntry(name)
                If Not name.EndsWith("/", StringComparison.Ordinal) AndAlso Not name.EndsWith("zero.exe", StringComparison.Ordinal) Then
                    Using writer As New StreamWriter(entry.Open())
                        writer.Write("ordinary bytes")
                    End Using
                End If
            Next
        End Using
        Return path
    End Function

    <Fact>
    Public Sub Ordinary_files_empty_executable_and_empty_directory_extract_without_activation()
        Dim archive As String = ZipWith("folder/readme.txt", "folder/zero.exe", "folder/empty/")
        Dim before As Byte() = File.ReadAllBytes(archive)
        Dim stage As String = Path.Combine(root, "stage")
        Dim plan As ArchiveExtractionPlan = ArchiveExtractionReader.Extract(archive, stage, CancellationToken.None)
        Assert.Equal(3, plan.Items.Count)
        Assert.Equal("ordinary bytes", File.ReadAllText(Path.Combine(stage, "folder", "readme.txt")))
        Assert.Equal(0L, New FileInfo(Path.Combine(stage, "folder", "zero.exe")).Length)
        Assert.True(Directory.Exists(Path.Combine(stage, "folder", "empty")))
        Assert.Equal(before, File.ReadAllBytes(archive))
        ' Media-only browsing remains separate.
        Using session As New ArchiveSession(archive, Path.Combine(root, "browse"), Function(ext) ext = ".jpg", 0)
            Assert.Empty(session.Entries)
        End Using
    End Sub

    <Theory>
    <InlineData("../escape")> <InlineData("NUL.txt")> <InlineData("folder/../escape")>
    Public Sub A_late_unsafe_header_refuses_the_entire_plan_before_any_entry_write(name As String)
        Dim archive As String = ZipWith("safe.txt", name)
        Dim stage As String = Path.Combine(root, "stage")
        Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionReader.Extract(archive, stage, CancellationToken.None))
        Assert.False(Directory.Exists(stage))
        Assert.True(File.Exists(archive))
    End Sub

    <Theory>
    <InlineData("solid.7z")> <InlineData("nonsolid.7z")> <InlineData("solid-many.7z")>
    Public Sub Sequential_7z_uses_one_pass_and_keeps_nonmedia_bytes(name As String)
        Dim fixture As String = Path.Combine(AppContext.BaseDirectory, "TestData", "archives", name)
        Dim stage As String = Path.Combine(root, "stage")
        Dim plan As ArchiveExtractionPlan = ArchiveExtractionReader.Extract(fixture, stage, CancellationToken.None)
        For Each row As String In File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "archives", "manifest.txt"))
            Dim parts As String() = row.Split("|"c)
            If Not parts(0).Split(","c).Contains(name) Then Continue For
            Dim bytes As Byte() = File.ReadAllBytes(Path.Combine(stage, parts(1).Replace("/"c, "\"c)))
            Assert.Equal(Long.Parse(parts(2), Globalization.CultureInfo.InvariantCulture), bytes.LongLength)
            Assert.Equal(parts(3), Convert.ToHexString(Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant())
        Next
        Assert.True(plan.Items.Count >= 4)
    End Sub

    <Theory>
    <InlineData("encrypted-data.7z")> <InlineData("encrypted-headers.7z")>
    Public Sub Encrypted_7z_has_a_typed_refusal_and_no_outputs(name As String)
        Dim stage As String = Path.Combine(root, "stage")
        Assert.Equal(ArchiveFailure.Encrypted, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionReader.Extract(
            Path.Combine(AppContext.BaseDirectory, "TestData", "archives", name), stage, CancellationToken.None)).Reason)
        Assert.False(Directory.Exists(stage))
    End Sub

    <Fact>
    Public Sub Cancellation_after_one_entry_never_removes_the_archive()
        Dim archive As String = ZipWith("a.txt", "b.txt")
        Dim stage As String = Path.Combine(root, "stage")
        Using cts As New CancellationTokenSource()
            Assert.ThrowsAny(Of OperationCanceledException)(Function() ArchiveExtractionReader.Extract(archive, stage, cts.Token, Sub(count, bytes) cts.Cancel()))
        End Using
        Assert.Single(Directory.GetFiles(stage))
        Assert.True(File.Exists(archive))
    End Sub

    <Fact>
    Public Sub Unix_symlink_attributes_are_rejected_before_writes()
        Dim archive As String = ZipWith("link")
        Using zip As ZipArchive = ZipFile.Open(archive, ZipArchiveMode.Update)
            zip.Entries(0).ExternalAttributes = &HA1FF0000
        End Using
        Assert.Equal(ArchiveFailure.Link, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionReader.Extract(archive, Path.Combine(root, "stage"), CancellationToken.None)).Reason)
    End Sub

    <Theory>
    <InlineData(False)> <InlineData(True)>
    Public Sub Rar_unix_regular_files_extract_but_unshifted_symlink_attributes_are_refused(link As Boolean)
        Dim archive As String = Path.Combine(root, "unix.rar")
        Dim payload As Byte() = Text.Encoding.ASCII.GetBytes("target bytes")
        Dim name As Byte() = Text.Encoding.ASCII.GetBytes("entry.txt")
        Using file As New FileStream(archive, FileMode.CreateNew), writer As New BinaryWriter(file)
            writer.Write(New Byte() {&H52, &H61, &H72, &H21, &H1A, &H7, &H0})
            WriteRarHeader(writer, &H73, 0, Sub(body)
                                               body.Write(CUShort(0))
                                               body.Write(0UI)
                                           End Sub)
            WriteRarHeader(writer, &H74, &H8000US, Sub(body)
                                                     body.Write(CUInt(payload.Length))
                                                     body.Write(CUInt(payload.Length))
                                                     body.Write(CByte(3)) ' Unix host stores mode in the low word.
                                                     body.Write(Crc(payload))
                                                     body.Write(0UI)
                                                     body.Write(CByte(20))
                                                     body.Write(CByte(&H30)) ' Stored, not compressed.
                                                     body.Write(CUShort(name.Length))
                                                     body.Write(If(link, &HA1FFUI, &H81A4UI))
                                                     body.Write(name)
                                                 End Sub)
            writer.Write(payload)
            WriteRarHeader(writer, &H7B, 0, Sub(body)
                                          End Sub)
        End Using
        Dim stage As String = Path.Combine(root, "stage")
        If link Then
            Assert.Equal(ArchiveFailure.Link, Assert.Throws(Of ArchiveSafetyException)(Function() ArchiveExtractionReader.Extract(archive, stage, CancellationToken.None)).Reason)
            Assert.False(Directory.Exists(stage))
        Else
            ArchiveExtractionReader.Extract(archive, stage, CancellationToken.None)
            Assert.Equal(payload, File.ReadAllBytes(Path.Combine(stage, "entry.txt")))
        End If
    End Sub

    Private Shared Function Crc(bytes As Byte()) As UInteger
        Using source As New MemoryStream(bytes)
            Return VerifiedZipWriter.Digest(source, bytes.Length, CancellationToken.None).Crc32
        End Using
    End Function

    Private Shared Sub WriteRarHeader(writer As BinaryWriter, kind As Byte, flags As UShort, fields As Action(Of BinaryWriter))
        Using bytes As New MemoryStream()
            Using body As New BinaryWriter(bytes, Text.Encoding.UTF8, True)
                body.Write(kind)
                body.Write(flags)
                body.Write(CUShort(0))
                fields(body)
                bytes.Position = 3
                body.Write(CUShort(bytes.Length + 2))
            End Using
            Dim header As Byte() = bytes.ToArray()
            writer.Write(CUShort(Crc(header) And &HFFFFUI))
            writer.Write(header)
        End Using
    End Sub
End Class
#End If
