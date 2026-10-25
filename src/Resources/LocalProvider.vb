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
    Implements ITypedStatProvider
    Implements IManagerDirectoryProvider

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

    ''' <summary>The typed answer: GetAttributes tells "not there" (FileNotFound/DirectoryNotFound)
    ''' from "not allowed" (UnauthorizedAccess) from a share that did not answer, which
    ''' <c>FileInfo.Exists</c> folds into one False.</summary>
    Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
        If String.IsNullOrEmpty(path) Then Return StatResult.Missing()
        Try
            Dim attributes As FileAttributes = File.GetAttributes(path)
            If (attributes And FileAttributes.Directory) <> 0 Then Return StatResult.Missing()
            Return StatResult.Found(ToEntry(New FileInfo(path)))
        Catch ex As FileNotFoundException
            Return StatResult.Missing()
        Catch ex As DirectoryNotFoundException
            Return StatResult.Missing()
        Catch ex As UnauthorizedAccessException
            Return StatResult.Denied(New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "access denied"))
        Catch ex As Exception
            Return StatResult.Failed(ResourceFailure.FromException(ex))
        End Try
    End Function

    ' ---------------------------------------------------------------- the file manager (257, N1) ----

    ''' <summary>The immediate children of a folder, folders included, never recursive: the viewer's "include
    ''' subfolders" preference is not consulted. A reparse point is listed as a link and is not followed. The three
    ''' ways a listing fails stay apart - nothing there, not allowed, the share did not answer.</summary>
    Public Function ListChildren(dirPath As String, ct As CancellationToken) As DirectoryListing Implements IManagerDirectoryProvider.ListChildren
        Dim full As String = ManagerPathRules.NormalizeLocal(dirPath)
        If full Is Nothing Then Return DirectoryListing.Missing()
        Try
            ct.ThrowIfCancellationRequested()
            If (File.GetAttributes(full) And FileAttributes.Directory) = 0 Then Return DirectoryListing.NotADirectory()
            Dim result As New List(Of ResourceEntry)()
            For Each info As FileSystemInfo In New DirectoryInfo(full).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
                ct.ThrowIfCancellationRequested()
                result.Add(ToChild(info))
            Next
            Return DirectoryListing.Listed(result)
        Catch ex As Exception
            Return ListingFailure(ex)
        End Try
    End Function

    ''' <summary>The typed stat that also answers for a directory.</summary>
    Public Function StatEntry(path As String) As StatResult Implements IManagerDirectoryProvider.StatEntry
        Dim full As String = ManagerPathRules.NormalizeLocal(path)
        If full Is Nothing Then Return StatResult.Missing()
        Try
            Dim isDirectory As Boolean = (File.GetAttributes(full) And FileAttributes.Directory) <> 0
            Return StatResult.Found(ToChild(If(isDirectory, DirectCast(New DirectoryInfo(full), FileSystemInfo), New FileInfo(full))))
        Catch ex As Exception
            Dim failure As ListingKind = ClassifyLocal(ex)
            Select Case failure
                Case ListingKind.Missing : Return StatResult.Missing()
                Case ListingKind.Denied : Return StatResult.Denied(New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "access denied"))
                Case Else : Return StatResult.Failed(LocalFailure(ex))
            End Select
        End Try
    End Function

    Public Function IsWithinRoot(rootPath As String, path As String) As Boolean Implements IManagerDirectoryProvider.IsWithinRoot
        Return ManagerPathRules.Contains(ResourceKind.Local, rootPath, path)
    End Function

    Public Function PathsEqual(a As String, b As String) As Boolean Implements IManagerDirectoryProvider.PathsEqual
        Return ManagerPathRules.AreEqual(ResourceKind.Local, a, b)
    End Function

    Public Function ParentOf(path As String) As String Implements IManagerDirectoryProvider.ParentOf
        Return ManagerPathRules.ParentOf(ResourceKind.Local, path)
    End Function

    Public Function RemoveEmptyDirectory(rootPath As String, path As String) As DirectoryRemoval Implements IManagerDirectoryProvider.RemoveEmptyDirectory
        If Not ManagerPathRules.Contains(ResourceKind.Local, rootPath, path) OrElse
           ManagerPathRules.AreEqual(ResourceKind.Local, rootPath, path) Then Return DirectoryRemoval.Refused
        Dim full As String = ManagerPathRules.NormalizeLocal(path)
        If full Is Nothing Then Return DirectoryRemoval.Refused
        Try
            Dim attributes As FileAttributes = File.GetAttributes(full)
            If (attributes And FileAttributes.Directory) = 0 OrElse (attributes And FileAttributes.ReparsePoint) <> 0 Then Return DirectoryRemoval.Refused
            Directory.Delete(full, False)
            Return DirectoryRemoval.Removed
        Catch ex As Exception
            If (ex.HResult And &HFFFF) = ErrorDirNotEmpty Then Return DirectoryRemoval.NotEmpty
            Select Case ClassifyLocal(ex)
                Case ListingKind.Missing : Return DirectoryRemoval.Missing
                Case ListingKind.Denied : Return DirectoryRemoval.Denied
                Case Else : Return DirectoryRemoval.Failed
            End Select
        End Try
    End Function

    Private Const ErrorDirNotEmpty As Integer = 145

    Private Enum ListingKind
        Missing
        Denied
        Cancelled
        Transport
        Other
    End Enum

    ''' <summary>What an exception of a local or UNC call says about the directory. The Windows network error codes
    ''' come first: a share that does not answer is reported by some runtimes as "not found", and "the folder is gone"
    ''' would be a lie about a NAS that is only asleep.</summary>
    Private Shared Function ClassifyLocal(ex As Exception) As ListingKind
        If ex Is Nothing Then Return ListingKind.Other
        If TypeOf ex Is OperationCanceledException Then Return ListingKind.Cancelled
        Select Case ex.HResult And &HFFFF
            Case 53, 64, 65, 67, 121, 1203, 1204, 1222, 1231, 1232
                Return ListingKind.Transport
        End Select
        If TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException Then Return ListingKind.Denied
        If TypeOf ex Is FileNotFoundException OrElse TypeOf ex Is DirectoryNotFoundException Then Return ListingKind.Missing
        If TypeOf ex Is IOException AndAlso PathFailure.Classify(ex) = PathFailureKind.Transport Then Return ListingKind.Transport
        Return ListingKind.Other
    End Function

    Private Shared Function LocalFailure(ex As Exception) As ResourceFailure
        If ClassifyLocal(ex) = ListingKind.Transport Then Return New ResourceFailure(ResourceFailureKind.Transport, ex.HResult And &HFFFF, "share did not answer")
        Return ResourceFailure.FromException(ex)
    End Function

    Private Shared Function ListingFailure(ex As Exception) As DirectoryListing
        Select Case ClassifyLocal(ex)
            Case ListingKind.Cancelled : Return DirectoryListing.Cancelled()
            Case ListingKind.Missing : Return DirectoryListing.Missing()
            Case ListingKind.Denied : Return DirectoryListing.Denied(New ResourceFailure(ResourceFailureKind.LocalIO, Nothing, "access denied"))
            Case Else : Return DirectoryListing.Failed(LocalFailure(ex))
        End Select
    End Function

    ''' <summary>One enumerated child. The find data is already in hand, so this costs no extra I/O per entry.</summary>
    Private Shared Function ToChild(info As FileSystemInfo) As ResourceEntry
        Dim attributes As FileAttributes = info.Attributes
        Dim isDirectory As Boolean = (attributes And FileAttributes.Directory) <> 0
        Dim length As Long = 0
        If Not isDirectory Then
            Dim file As FileInfo = TryCast(info, FileInfo)
            If file IsNot Nothing Then
                Try
                    length = file.Length
                Catch ex As IOException
                    length = 0
                End Try
            End If
        End If
        Return New ResourceEntry With {
            .FullPath = info.FullName,
            .Name = info.Name,
            .Length = length,
            .LastWriteUtc = info.LastWriteTimeUtc,
            .IsDirectory = isDirectory,
            .LocalPath = info.FullName,
            .IsLink = (attributes And FileAttributes.ReparsePoint) <> 0,
            .IsHidden = (attributes And FileAttributes.Hidden) <> 0,
            .IsSystem = (attributes And FileAttributes.System) <> 0
        }
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
        If Directory.Exists(remotePath) Then
            Directory.Move(remotePath, newRemotePath)
        Else
            File.Move(remotePath, newRemotePath)
        End If
    End Sub

    Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        Directory.CreateDirectory(remotePath)
    End Sub

End Class
#End If
