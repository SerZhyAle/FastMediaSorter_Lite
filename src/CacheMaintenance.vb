#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO

''' <summary>User-requested Storage cleanup (130 §3.3). Background stores intentionally
''' suppress failures; an explicit operation must report them before announcing success.</summary>
Friend Module CacheMaintenance
    Friend Sub ClearFiles(root As String, ParamArray patterns As String())
        For Each pattern As String In patterns
            Dim entries As String()
            Try
                entries = Directory.GetFiles(root, pattern)
            Catch missing As DirectoryNotFoundException
                Return
            End Try
            For Each entry As String In entries
                Try
                    File.Delete(entry)
                Catch missing As DirectoryNotFoundException
                    ' Another viewer removed the cache directory meanwhile.
                End Try
            Next
        Next
    End Sub

    ''' <summary>Keep live sessions, recent sessions and unrecognised directories.
    ''' A PID that cannot be inspected is conservatively treated as live.</summary>
    Friend Sub ClearSessions(root As String, nowUtc As Date, isLiveViewer As Func(Of Integer, Boolean))
        Dim rootPrefix As String = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) & Path.DirectorySeparatorChar
        Dim sessions As String()
        Try
            sessions = Directory.GetDirectories(root)
        Catch missing As DirectoryNotFoundException
            Return
        End Try
        For Each session As String In sessions
            If Not Path.GetFullPath(session).StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) Then
                Throw New IOException("Cache session is outside the cache root.")
            End If
            Dim owner As Integer
            If Not ArchivePaths.TryParseSessionPid(Path.GetFileName(session), owner) Then Continue For
            If isLiveViewer(owner) Then Continue For
            If (nowUtc - Directory.GetLastWriteTimeUtc(session)).TotalHours < ArchiveTempStore.Stale_After_Hours Then Continue For
            If (File.GetAttributes(session) And FileAttributes.ReparsePoint) <> 0 Then
                Throw New IOException("Cache session is a reparse point.")
            End If
            Try
                Directory.Delete(session, recursive:=True)
            Catch missing As DirectoryNotFoundException
                ' Another viewer's orphan sweep already removed this session.
            End Try
        Next
    End Sub
End Module
#End If
