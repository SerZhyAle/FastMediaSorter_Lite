#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' The archive cache on disk: one directory per session, and the three lines of defence
''' that make sure the disk goes back to the state it was in
''' (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §4, invariant 4).
'''
''' The point of the third line - the orphan sweep - is that the first two can be skipped:
''' a killed process, a power cut and a crash all leave a session directory behind, and
''' none of them run a Finally. So the sweep does not trust anything written at shutdown;
''' it reads the owner's pid out of the directory name and asks the operating system
''' whether that process is still one of ours.
'''
''' Modern-only, like the whole feature.
''' </summary>
Friend Module ArchiveTempStore

    ''' <summary>
    ''' Beyond this age a session directory is removed even if a live process claims its
    ''' pid. Windows reuses pids, so on a machine that has been up for weeks a stale
    ''' directory can be "owned" by an unrelated program that happens to share the number
    ''' - and a cache that is never cleaned is the failure this feature must not have.
    ''' Nothing legitimate needs a session older than a day: it would mean the viewer has
    ''' had one archive open for 24 hours, and even then only the cache is lost, not the
    ''' archive.
    ''' </summary>
    Friend Const Stale_After_Hours As Double = 24.0

    ''' <summary>
    ''' Creates a fresh session directory and returns its full path. The name carries our
    ''' pid plus a short random token, so two viewers (x64 and x86 side by side, or two
    ''' Windows sessions) never share one.
    ''' </summary>
    Friend Function CreateSession() As String
        Dim token As String = Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim path_Of_Session As String = Path.Combine(
            ArchivePaths.CacheRoot(),
            ArchivePaths.SessionDirName(Diagnostics.Process.GetCurrentProcess().Id, token))
        Directory.CreateDirectory(path_Of_Session)
        Return path_Of_Session
    End Function

    ''' <summary>
    ''' Removes a session directory and everything in it. Returns False when something
    ''' survived - a file an antivirus or a lagging handle still holds - which is not an
    ''' error the user should ever see: the sweep will take it next time.
    ''' </summary>
    Friend Function DeleteSession(sessionDir As String) As Boolean
        If String.IsNullOrEmpty(sessionDir) Then Return True
        Try
            If Not Directory.Exists(sessionDir) Then Return True
            Directory.Delete(sessionDir, recursive:=True)
            Return True
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive cache: session directory not removed: " &
                                    sessionDir & " - " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Sweeps the real cache root. Called on the FIRST archive open rather than at
    ''' startup: someone who never opens an archive should not pay for a directory walk.
    ''' </summary>
    Friend Sub SweepOrphans()
        SweepRoot(ArchivePaths.CacheRoot(), Date.UtcNow)
    End Sub

    ''' <summary>
    ''' The sweep itself, against an explicit root and clock so it can be driven by a test
    ''' with real directories and no real crash.
    '''
    ''' Every directory is judged on its own inside its own Try: an orphan that refuses to
    ''' go must not stop the archive the user is trying to open right now.
    ''' </summary>
    Friend Sub SweepRoot(root As String, nowUtc As Date)
        If String.IsNullOrEmpty(root) Then Return
        Try
            If Not Directory.Exists(root) Then Return
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive cache: sweep could not read the root: " & ex.Message)
            Return
        End Try

        Dim candidates As String()
        Try
            candidates = Directory.GetDirectories(root)
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive cache: sweep could not list sessions: " & ex.Message)
            Return
        End Try

        For Each candidate As String In candidates
            Try
                Dim ageHours As Double = (nowUtc - Directory.GetLastWriteTimeUtc(candidate)).TotalHours
                Dim owner As Integer
                Dim live As Boolean = ArchivePaths.TryParseSessionPid(Path.GetFileName(candidate), owner) AndAlso
                                      IsLiveViewerProcess(owner)
                If ShouldRemoveSession(Path.GetFileName(candidate), ageHours, live) Then
                    Directory.Delete(candidate, recursive:=True)
                End If
            Catch ex As Exception
                AppFileLogger.WriteLine("Archive cache: orphan not removed: " & candidate & " - " & ex.Message)
            End Try
        Next
    End Sub

    ''' <summary>
    ''' The whole decision, as a pure function - which is the only part of the sweep worth
    ''' a test, and the part where a mistake is expensive in both directions: delete a
    ''' live session and the viewer loses the archive it is showing; keep an orphan and
    ''' the cache grows for ever.
    '''
    ''' A name that is not one of ours is never touched. The cache root is ours alone, but
    ''' "delete what you do not recognise" is not a rule to write into a directory sweep.
    ''' </summary>
    Friend Function ShouldRemoveSession(dirName As String, ageHours As Double, ownerIsLive As Boolean) As Boolean
        Dim owner As Integer
        If Not ArchivePaths.TryParseSessionPid(dirName, owner) Then Return False
        If ageHours >= Stale_After_Hours Then Return True
        Return Not ownerIsLive
    End Function

    ''' <summary>
    ''' Is that pid still a running viewer? Our own pid counts without asking. Anything we
    ''' cannot answer counts as "not alive" - a directory whose owner cannot be identified
    ''' is exactly what an orphan looks like.
    ''' </summary>
    Private Function IsLiveViewerProcess(processId As Integer) As Boolean
        Try
            If processId = Diagnostics.Process.GetCurrentProcess().Id Then Return True

            Using owner As Diagnostics.Process = Diagnostics.Process.GetProcessById(processId)
                Dim name As String = owner.ProcessName
                For Each viewer As String In Viewer_Process_Names
                    If String.Equals(name, viewer, StringComparison.OrdinalIgnoreCase) Then Return True
                Next
            End Using
        Catch ex As ArgumentException
            ' No process with that id - the ordinary orphan case, not worth a log line.
        Catch ex As Exception
            AppFileLogger.WriteLine("Archive cache: could not identify session owner " &
                                    processId.ToString() & " - " & ex.Message)
        End Try
        Return False
    End Function

End Module
#End If
