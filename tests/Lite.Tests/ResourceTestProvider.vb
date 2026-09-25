#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading

''' <summary>
''' A stand-in for a remote provider (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md
''' §11): "/dir/name" paths backed by a scratch directory, so the provider layer can be
''' driven end to end - fetch, upload, rename, delete - without a network. It counts what
''' it is asked to do, and can be told to lie about an upload the way a dropped link does.
''' </summary>
Friend NotInheritable Class ResourceTestProvider
    Implements IFileSystemProvider

    Private ReadOnly backing_Root As String
    Private ReadOnly caps As ProviderCapabilities

    Friend Sub New(backingRoot As String, Optional capabilities As ProviderCapabilities = Nothing)
        backing_Root = backingRoot
        caps = If(capabilities, New ProviderCapabilities())
        Directory.CreateDirectory(backing_Root)
    End Sub

    Friend Property DownloadCalls As Integer
    Friend Property UploadCalls As Integer
    Friend Property DeleteCalls As Integer
    ''' <summary>When set, an upload writes only this many bytes and reports success - the
    ''' truncated file a verification exists to catch.</summary>
    Friend Property TruncateUploadsTo As Long = -1
    ''' <summary>When set, a download writes only this many bytes.</summary>
    Friend Property TruncateDownloadsTo As Long = -1

    ''' <summary>The scratch file behind a remote path.</summary>
    Friend Function BackingPath(remotePath As String) As String
        Dim relative As String = If(remotePath, "").TrimStart("/"c).Replace("/"c, Path.DirectorySeparatorChar)
        Return Path.Combine(backing_Root, relative)
    End Function

    Friend Sub PutFile(remotePath As String, bytes As Byte())
        Dim target As String = BackingPath(remotePath)
        Directory.CreateDirectory(Path.GetDirectoryName(target))
        File.WriteAllBytes(target, bytes)
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Sftp
        End Get
    End Property

    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return caps
        End Get
    End Property

    Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        Dim result As New List(Of ResourceEntry)()
        Dim dir As String = BackingPath(dirPath)
        If Not Directory.Exists(dir) Then Return result
        Dim prefix As String = If(dirPath.EndsWith("/", StringComparison.Ordinal), dirPath, dirPath & "/")
        For Each f As String In Directory.GetFiles(dir)
            Dim info As New FileInfo(f)
            result.Add(New ResourceEntry With {
                .FullPath = prefix & info.Name, .Name = info.Name, .Length = info.Length,
                .LastWriteUtc = info.LastWriteTimeUtc})
        Next
        For Each d As String In Directory.GetDirectories(dir)
            result.Add(New ResourceEntry With {
                .FullPath = prefix & Path.GetFileName(d), .Name = Path.GetFileName(d), .IsDirectory = True})
        Next
        Return result
    End Function

    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        entry = Nothing
        Dim f As New FileInfo(BackingPath(path))
        If Not f.Exists Then Return False
        entry = New ResourceEntry With {.FullPath = path, .Name = f.Name, .Length = f.Length, .LastWriteUtc = f.LastWriteTimeUtc}
        Return True
    End Function

    Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        DownloadCalls += 1
        ct.ThrowIfCancellationRequested()
        Dim bytes As Byte() = File.ReadAllBytes(BackingPath(remotePath))
        If TruncateDownloadsTo >= 0 AndAlso TruncateDownloadsTo < bytes.Length Then Array.Resize(bytes, CInt(TruncateDownloadsTo))
        File.WriteAllBytes(localPath, bytes)
        progress?.Report(bytes.Length)
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        UploadCalls += 1
        ct.ThrowIfCancellationRequested()
        Dim bytes As Byte() = File.ReadAllBytes(localPath)
        If TruncateUploadsTo >= 0 AndAlso TruncateUploadsTo < bytes.Length Then Array.Resize(bytes, CInt(TruncateUploadsTo))
        PutFile(remotePath, bytes)
        progress?.Report(bytes.Length)
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        DeleteCalls += 1
        File.Delete(BackingPath(remotePath))
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        File.Move(BackingPath(remotePath), BackingPath(newRemotePath))
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        Directory.CreateDirectory(BackingPath(remotePath))
    End Sub

End Class
#End If
