#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' The remote cache on disk: one directory per open resource, the three lines of cleanup,
''' and the budget (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-3, PV-6).
'''
''' The rule that makes an aggressive cleanup safe: <b>a cache file is never the only copy
''' of anything</b>. Nothing the user creates lives here, so deleting the whole tree at any
''' moment loses nothing - which is why a directory that merely MIGHT be an orphan goes.
'''
''' Shaped on ArchiveTempStore, and sharing its pure verdict and its liveness question
''' rather than restating them: the two caches are the same kind of directory.
''' </summary>
Friend Module RemoteCacheStore

    ''' <summary>Default budget of one session, in megabytes (preference RemoteCacheMaxMb).</summary>
    Friend Const Default_Max_Mb As Integer = 2048

    ''' <summary>Creates a fresh session directory under the real root.</summary>
    Friend Function CreateSession() As String
        Return CreateSessionUnder(RemoteCachePaths.CacheRoot())
    End Function

    ''' <summary>Creates a session directory under <paramref name="root"/> - separate so a
    ''' test can drive the real directory handling against a scratch root.</summary>
    Friend Function CreateSessionUnder(root As String) As String
        Dim token As String = Guid.NewGuid().ToString("N").Substring(0, 8)
        Dim sessionDir As String = Path.Combine(
            root, RemoteCachePaths.SessionDirName(Diagnostics.Process.GetCurrentProcess().Id, token))
        Directory.CreateDirectory(sessionDir)
        Return sessionDir
    End Function

    ''' <summary>Removes a session directory. False when something survived - a file a
    ''' player or an antivirus still holds - which the next sweep takes; never an error
    ''' the user sees.</summary>
    Friend Function DeleteSession(sessionDir As String) As Boolean
        If String.IsNullOrEmpty(sessionDir) Then Return True
        Try
            If Not Directory.Exists(sessionDir) Then Return True
            Directory.Delete(sessionDir, recursive:=True)
            Return True
        Catch ex As Exception
            AppFileLogger.WriteLine("Remote cache: session directory not removed: " &
                                    sessionDir & " - " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Sweeps the real root. Called when a resource is opened, not at startup:
    ''' someone who never opens one should not pay for a directory walk.</summary>
    Friend Sub SweepOrphans()
        SweepRoot(RemoteCachePaths.CacheRoot(), Date.UtcNow, AddressOf ArchiveTempStore.IsLiveViewerProcess)
    End Sub

    ''' <summary>
    ''' The sweep against an explicit root, clock and liveness question, so a test can stand
    ''' in for "a live sibling viewer" without starting one. Each directory is judged inside
    ''' its own Try: an orphan that refuses to go must not stop the resource being opened.
    ''' </summary>
    Friend Sub SweepRoot(root As String, nowUtc As Date, isLiveViewer As Func(Of Integer, Boolean))
        If String.IsNullOrEmpty(root) Then Return
        Dim candidates As String()
        Try
            If Not Directory.Exists(root) Then Return
            candidates = Directory.GetDirectories(root)
        Catch ex As Exception
            AppFileLogger.WriteLine("Remote cache: sweep could not list sessions: " & ex.Message)
            Return
        End Try

        For Each candidate As String In candidates
            Try
                Dim name As String = Path.GetFileName(candidate)
                Dim ageHours As Double = (nowUtc - Directory.GetLastWriteTimeUtc(candidate)).TotalHours
                Dim owner As Integer
                Dim live As Boolean = RemoteCachePaths.TryParseSessionPid(name, owner) AndAlso isLiveViewer(owner)
                ' The archive cache's verdict, verbatim: a name that is not ours is never
                ' touched, anything past a day goes (pids are reused), otherwise the owner decides.
                If ArchiveTempStore.ShouldRemoveSession(name, ageHours, live) Then
                    Directory.Delete(candidate, recursive:=True)
                End If
            Catch ex As Exception
                AppFileLogger.WriteLine("Remote cache: orphan not removed: " & candidate & " - " & ex.Message)
            End Try
        Next
    End Sub

    ''' <summary>
    ''' Which materialised entries to delete to get back under <paramref name="budgetBytes"/>:
    ''' least recently used first, never one that <paramref name="isProtected"/> names - the
    ''' file on screen and its neighbours, which the viewer or the prefetch is about to want.
    ''' When only protected entries are left the answer stops short of the budget rather
    ''' than delete what is being looked at. Pure.
    ''' </summary>
    ''' <param name="recencyOldestFirst">Entry indexes, least recently used first.</param>
    Friend Function ChooseEvictions(recencyOldestFirst As IReadOnlyList(Of Integer),
                                    sizes As IReadOnlyDictionary(Of Integer, Long),
                                    totalBytes As Long,
                                    budgetBytes As Long,
                                    isProtected As Func(Of Integer, Boolean)) As List(Of Integer)
        Dim victims As New List(Of Integer)()
        If budgetBytes <= 0 Then Return victims
        Dim remaining As Long = totalBytes
        For Each candidate As Integer In recencyOldestFirst
            If remaining <= budgetBytes Then Exit For
            If isProtected IsNot Nothing AndAlso isProtected(candidate) Then Continue For
            Dim size As Long
            If Not sizes.TryGetValue(candidate, size) Then Continue For
            victims.Add(candidate)
            remaining -= size
        Next
        Return victims
    End Function

    ''' <summary>
    ''' How many files ahead the prefetch materialises (PV-6). None when nothing has to be
    ''' fetched; three on a fast, unmetered link; otherwise one. Never unbounded: a prefetch
    ''' that runs ahead of a user about to leave the folder is pure cost on a metered link.
    ''' </summary>
    Friend Function PrefetchDepth(capabilities As ProviderCapabilities) As Integer
        If capabilities Is Nothing OrElse Not capabilities.NeedsMaterialisation Then Return 0
        If capabilities.CostHint = ProviderCostHint.Fast AndAlso Not capabilities.IsMetered Then Return 3
        Return 1
    End Function

End Module
#End If
