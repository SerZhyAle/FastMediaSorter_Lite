#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Threading

''' <summary>
''' Local folders and UNC shares - the provider the viewer has always had, now behind the
''' seam (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-4).
'''
''' The rule for this class is that it contains NO new logic: ListEntries is the scan that
''' lived in Main_Form.EnumerateConfiguredFiles, moved, and the verbs are the System.IO
''' calls themselves. Nothing is ever materialised - a local file is already openable by
''' path - which is what keeps the local build exactly as fast as it was.
''' </summary>
Friend NotInheritable Class LocalProvider
    Implements IFileSystemProvider

    Private ReadOnly include_Subfolders As Func(Of Boolean)

    ''' <param name="includeSubfolders">Read on every listing, like the preference it
    ''' stands for: toggling it takes effect on the next scan without a new provider.</param>
    Friend Sub New(includeSubfolders As Func(Of Boolean))
        include_Subfolders = includeSubfolders
    End Sub

    Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
        Get
            Return ResourceKind.Local
        End Get
    End Property

    Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
        Get
            Return ProviderCapabilities.LocalDisk
        End Get
    End Property

    ''' <summary>
    ''' Recursive scans deliberately do not use AllDirectories: Windows lets a junction or
    ''' symlink point back to an ancestor (or outside the selected tree), and blindly
    ''' enumerating it can loop forever. Reparse directories are therefore leaves.
    ''' Inaccessible children are skipped just like Explorer's normal scan.
    ''' </summary>
    ''' <remarks>The scan itself is FolderScan.EnumerateFiles, shared with the x86 build. A
    ''' root that cannot be read THROWS (audit 048, A-07) - it used to come back as an empty
    ''' list, i.e. "the folder is empty", and a scan that died half-way as a truncated one.</remarks>
    Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
        Dim includeSubfolders As Boolean = include_Subfolders IsNot Nothing AndAlso include_Subfolders()
        Dim files As List(Of FileInfo) = FolderScan.EnumerateFiles(dirPath, includeSubfolders, ct)
        Dim result As New List(Of ResourceEntry)(files.Count)
        For Each f As FileInfo In files
            result.Add(ToEntry(f))
        Next
        Return result
    End Function

    ''' <summary>The enumeration already carries size and time (the find data), so this
    ''' costs no extra I/O per file.</summary>
    Private Shared Function ToEntry(f As FileInfo) As ResourceEntry
        Return New ResourceEntry With {
            .FullPath = f.FullName,
            .Name = f.Name,
            .Length = f.Length,
            .LastWriteUtc = f.LastWriteTimeUtc,
            .IsDirectory = False,
            .LocalPath = f.FullName
        }
    End Function

    Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
        entry = Nothing
        Try
            Dim f As New FileInfo(path)
            If Not f.Exists Then Return False
            entry = ToEntry(f)
            Return True
        Catch
            Return False
        End Try
    End Function

    ' The verbs below are never reached for a local folder today: FileManager keeps its own
    ' staged System.IO path for local-to-local, and nothing local is ever materialised.
    ' They exist so the provider is complete and a caller never has to special-case it.

    Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
        ct.ThrowIfCancellationRequested()
        File.Copy(remotePath, localPath, True)
        progress?.Report(New FileInfo(localPath).Length)
    End Sub

    Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
        ct.ThrowIfCancellationRequested()
        File.Copy(localPath, remotePath, True)
        progress?.Report(New FileInfo(remotePath).Length)
    End Sub

    Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        File.Delete(remotePath)
    End Sub

    Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        File.Move(remotePath, newRemotePath)
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        Directory.CreateDirectory(remotePath)
    End Sub

End Class
#End If
