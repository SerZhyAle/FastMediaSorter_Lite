#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' Where a remote resource's materialised files live, and what they are called
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-3).
'''
''' The naming rules are the archive cache's, deliberately and by delegation rather than by
''' copy: the destination name is BUILT from the entry's last segment only, prefixed by its
''' index (<c>00042_clip.mp4</c>), so a hostile remote path cannot address anything outside
''' the session directory, a reserved device name cannot come out, and two <c>cover.jpg</c>
''' stay apart. One implementation of that guarantee is one to keep right; ArchivePaths
''' already has its tests, and RemoteCachePathsTests hold it to the remote cases as well.
'''
''' Pure - strings in, strings out.
''' </summary>
Friend Module RemoteCachePaths

    ''' <summary>Name of the cache root under the app's own %LOCALAPPDATA% folder.
    ''' A sibling of archive-cache, never inside it: the two sweeps must not judge each
    ''' other's directories.</summary>
    Private Const Cache_Folder As String = "remote-cache"

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\remote-cache</summary>
    Friend Function CacheRoot() As String
        Return Path.Combine(AppPaths.LocalAppDataRoot(), Cache_Folder)
    End Function

    ''' <summary><c>&lt;pid&gt;-&lt;token&gt;</c> - the pid is what the orphan sweep reads back.</summary>
    Friend Function SessionDirName(processId As Integer, token As String) As String
        Return ArchivePaths.SessionDirName(processId, token)
    End Function

    Friend Function TryParseSessionPid(dirName As String, ByRef processId As Integer) As Boolean
        Return ArchivePaths.TryParseSessionPid(dirName, processId)
    End Function

    ''' <summary>
    ''' The file name a remote entry gets on disk. The remote name is parsed by hand, never
    ''' with Path.GetExtension - that reads a colon as a volume separator and eats
    ''' <c>a:b.jpg</c> - and both separators count, since a remote path uses "/".
    ''' </summary>
    Friend Function EntryFileName(index As Integer, remoteName As String) As String
        Return ArchivePaths.EntryFileName(index, remoteName)
    End Function

    ''' <summary>Is <paramref name="candidatePath"/> ours - inside the remote cache? Such a
    ''' path is never a place the user means to keep anything.</summary>
    Friend Function IsUnderRemoteCache(candidatePath As String, cacheRootPath As String) As Boolean
        Return ArchivePaths.IsUnderArchiveCache(candidatePath, cacheRootPath)
    End Function

    Friend Function IsUnderRemoteCache(candidatePath As String) As Boolean
        Return IsUnderRemoteCache(candidatePath, CacheRoot())
    End Function

    ''' <summary>
    ''' Is <paramref name="candidatePath"/> a session's temporary copy - an archive entry's
    ''' or a remote resource's materialised file? Such a path is gone once its session
    ''' closes, so it must never be persisted: not as a recent file, not as the "resume last
    ''' file" target (audit 057, S02-8 - the archive half was 052 A-05, this adds resources
    ''' before 007 ships the first provider).
    ''' </summary>
    Friend Function IsSessionCopy(candidatePath As String, archiveCacheRoot As String, remoteCacheRoot As String) As Boolean
        Return ArchivePaths.IsUnderArchiveCache(candidatePath, archiveCacheRoot) OrElse
               IsUnderRemoteCache(candidatePath, remoteCacheRoot)
    End Function

    ''' <summary><see cref="IsSessionCopy(String, String, String)"/> against the real roots.</summary>
    Friend Function IsSessionCopy(candidatePath As String) As Boolean
        Return IsSessionCopy(candidatePath, ArchivePaths.CacheRoot(), CacheRoot())
    End Function

End Module
#End If
