Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports FastMediaSorterCompanion
Imports Xunit

Public Class ManagementLogTests
    <Fact>
    Public Sub ServerDayUsesUtcRatherThanTheOperatorsLocalDate()
        Dim instant As New DateTimeOffset(2026, 10, 19, 0, 30, 0, TimeSpan.FromHours(2))
        Assert.Equal(Path.Combine("C:\service-state", "logs", "exchange-2026-10-18.log"),
            ManagementLogAccess.ServerLogPath("C:\service-state", instant))
        Assert.Throws(Of IOException)(Sub() ManagementLogAccess.ServerLogPath("", instant))
        Assert.Equal(Path.Combine("C:\service-state", "logs", "audit-2026-10-18.log"),
            ManagementLogAccess.ServerAuditLogPath("C:\service-state", instant))
    End Sub

    <Fact>
    Public Sub OpeningAnActiveLogPreservesItsBytesAndAllowsTheWriterToContinue()
        Dim tempDir = Path.Combine(Path.GetTempPath(), "fms-log-test-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir)
        Dim logPath = Path.Combine(tempDir, "companion.log")
        Try
            ManagementLogAccess.EnsureLogFile(logPath)
            Assert.True(File.Exists(logPath))
            File.WriteAllText(logPath, "Existing session" & vbCrLf)
            Using writer As New FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite Or FileShare.Delete)
                ManagementLogAccess.EnsureLogFile(logPath)
                Dim tail = System.Text.Encoding.UTF8.GetBytes("Next event")
                writer.Write(tail, 0, tail.Length)
            End Using
            Assert.Equal("Existing session" & vbCrLf & "Next event", File.ReadAllText(logPath))
        Finally
            If File.Exists(logPath) Then File.Delete(logPath)
            Directory.Delete(tempDir)
        End Try
    End Sub

    <Fact>
    Public Sub ObservationsAreDeduplicatedAndExcludeCredentialAndErrorPayloads()
        Dim lines As New List(Of String)()
        Dim log As New ShareStateLog(Sub(line) lines.Add(line))
        Dim status As New WorkerStatus With {
            .Running = True, .ListenPort = 40222, .Password = "NEVER-password",
            .Fingerprint = "NEVER-key", .Username = "NEVER-user", .LastError = "NEVER-error",
            .Reachability = New WorkerReachability With {.LanAddress = "192.0.2.8"},
            .Stats = New WorkerStats With {.TotalConnections = 1, .LastConnectionAddress = "NEVER-address"}}
        log.Observe(status)
        Assert.Equal(3, lines.Count)
        log.Observe(status)
        Assert.Equal(3, lines.Count)
        status.Running = False
        log.Observe(status)
        Assert.Equal(4, lines.Count)
        status.Stats.TotalConnections = 2
        log.Observe(status)
        Assert.Equal(5, lines.Count)
        Assert.All(lines, Sub(line) Assert.Contains("[SHARE-STATE]", line))
        Assert.DoesNotContain("NEVER-", String.Join(vbLf, lines))
    End Sub

    <Fact>
    Public Sub CommandAuditIncludesFailuresAndDropsUnknownRequestTypes()
        Dim lines As New List(Of String)()
        Dim log As New ShareStateLog(Sub(line) lines.Add(line))
        log.CommandCompleted("SetSharedFolders", True)
        log.CommandCompleted("StartServer", False)
        log.CommandCompleted("secret" & vbCrLf & "injected", True)
        Assert.Equal(2, lines.Count)
        Assert.EndsWith("SetSharedFolders OK", lines(0))
        Assert.EndsWith("StartServer Failed", lines(1))
    End Sub
End Class
