#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

Public Class CacheMaintenanceTests
    Implements IDisposable

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-cache-clear-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Directory.Delete(root, recursive:=True)
    End Sub

    <Fact>
    Public Sub Explicit_clear_deletes_only_cache_patterns_and_reports_a_locked_entry()
        Dim entry As String = Path.Combine(root, "entry.cache")
        File.WriteAllText(entry, "cached")
        File.WriteAllText(Path.Combine(root, "source.jpg"), "source")
        Using held As New FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read)
            Assert.Throws(Of IOException)(Sub() CacheMaintenance.ClearFiles(root, "*.cache"))
        End Using
        CacheMaintenance.ClearFiles(root, "*.cache")
        Assert.False(File.Exists(entry))
        Assert.True(File.Exists(Path.Combine(root, "source.jpg")))
        CacheMaintenance.ClearFiles(Path.Combine(root, "missing"), "*.cache")
    End Sub

    <Fact>
    Public Sub Explicit_clear_reports_an_unlistable_root()
        Dim blocker As String = Path.Combine(root, "blocked")
        File.WriteAllText(blocker, "not a directory")
        Assert.Throws(Of IOException)(Sub() CacheMaintenance.ClearFiles(blocker, "*.cache"))
        Assert.Throws(Of IOException)(Sub() CacheMaintenance.ClearSessions(blocker, Date.UtcNow, Function(pid) False))
    End Sub

    <Fact>
    Public Sub Explicit_session_clear_keeps_live_old_sessions_recent_orphans_and_foreign_folders()
        Dim nowUtc As Date = Date.UtcNow
        For Each name As String In {"1-live", "2-recent", "3-stale", "foreign"}
            Dim session As String = Path.Combine(root, name)
            Directory.CreateDirectory(session)
            File.WriteAllText(Path.Combine(session, "cached.bin"), "cached")
            Directory.SetLastWriteTimeUtc(session, nowUtc.AddHours(If(name = "2-recent", -1, -48)))
        Next
        CacheMaintenance.ClearSessions(root, nowUtc, Function(pid) pid = 1)
        Assert.True(Directory.Exists(Path.Combine(root, "1-live")))
        Assert.True(Directory.Exists(Path.Combine(root, "2-recent")))
        Assert.True(Directory.Exists(Path.Combine(root, "foreign")))
        Assert.False(Directory.Exists(Path.Combine(root, "3-stale")))
    End Sub

    <Fact>
    Public Sub Explicit_session_clear_reports_failed_deletion()
        Dim session As String = Path.Combine(root, "3-stale")
        Directory.CreateDirectory(session)
        Dim entry As String = Path.Combine(session, "cached.bin")
        File.WriteAllText(entry, "cached")
        Dim nowUtc As Date = Date.UtcNow
        Directory.SetLastWriteTimeUtc(session, nowUtc.AddHours(-48))
        Using held As New FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read)
            Assert.Throws(Of IOException)(Sub() CacheMaintenance.ClearSessions(root, nowUtc, Function(pid) False))
        End Using
        Assert.True(File.Exists(entry))
    End Sub
End Class
#End If
