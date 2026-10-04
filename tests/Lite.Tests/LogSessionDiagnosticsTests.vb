Option Strict On
Imports System.IO
Imports Xunit

<Collection("AppFileLogger")>
Public Class LogSessionDiagnosticsTests
    Private Const First As String = "Session started: 2026-10-04 13:03:08.1301; PID=37556; role=primary"
    Private Const Second As String = "Session started: 2026-10-04 13:44:51.0949; PID=23856; role=secondary"

    <Fact>
    Public Sub Empty_and_truncated_headers_are_not_sessions()
        Assert.Empty(LogSessionDiagnostics.UnfinishedSessions(""))
        Assert.Empty(LogSessionDiagnostics.UnfinishedSessions("Session started: 2026-10-04 13:44"))
    End Sub

    <Fact>
    Public Sub Finished_session_is_not_reported()
        Assert.Empty(LogSessionDiagnostics.UnfinishedSessions(First & vbLf & "Session finished: 2026-10-04 13:44:20.0000; PID=37556"))
    End Sub

    <Fact>
    Public Sub Finished_then_unfinished_session_is_reported_with_diagnostic_locations()
        Dim warnings = LogSessionDiagnostics.UnfinishedSessions(First & vbLf & "Session finished: 2026-10-04 13:44:20.0000; PID=37556" & vbLf & Second)
        Dim warning = Assert.Single(warnings)
        Assert.Contains("PID=23856", warning)
        Assert.Contains("did not finish (crash or kill)", warning)
        Assert.Contains("Windows Error Reporting", warning)
        Assert.Contains("%LOCALAPPDATA%\CrashDumps", warning)
    End Sub

    <Fact>
    Public Sub Interleaved_process_exit_only_finishes_its_own_session()
        Dim warnings = LogSessionDiagnostics.UnfinishedSessions(First & vbLf & Second & vbLf & "Session finished: 2026-10-04 13:45:00.0000; PID=23856")
        Assert.Contains("PID=37556", Assert.Single(warnings))
    End Sub

    <Fact>
    Public Sub Legacy_unambiguous_finish_is_supported_but_ambiguous_exit_clears_neither_process()
        Assert.Empty(LogSessionDiagnostics.UnfinishedSessions(First & vbLf & "Session finished: 2026-10-04 13:45:00.0000"))
        Assert.Equal(2, LogSessionDiagnostics.UnfinishedSessions(First & vbLf & Second & vbLf & "Session finished: 2026-10-04 13:45:00.0000").Count)
        Assert.Contains("PID=unknown", Assert.Single(LogSessionDiagnostics.UnfinishedSessions("Session started: 2026-10-04 13:44:51.0949")))
    End Sub

    <Fact>
    Public Sub Active_process_is_not_reported_as_unfinished()
        Dim filePath = Path.GetTempFileName()
        Try
            Using process = Diagnostics.Process.GetCurrentProcess()
                File.WriteAllText(filePath, "Session started: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", Globalization.CultureInfo.InvariantCulture) & "; PID=" & process.Id.ToString())
                Assert.Empty(LogSessionDiagnostics.ReadPreviousWarnings(filePath))
            End Using
        Finally
            File.Delete(filePath)
        End Try
    End Sub

    <Fact>
    Public Sub Empty_file_and_a_long_session_can_be_inspected()
        Dim filePath = Path.GetTempFileName()
        Try
            Assert.Empty(LogSessionDiagnostics.ReadPreviousWarnings(filePath))
            File.WriteAllText(filePath, "Session started: 2026-10-04 13:03:08.1301; PID=2147483647" & vbLf & New String("x"c, 300000))
            Assert.Contains("PID=2147483647", Assert.Single(LogSessionDiagnostics.ReadPreviousWarnings(filePath)))
        Finally
            File.Delete(filePath)
        End Try
    End Sub

    <Fact>
    Public Sub Logger_startup_appends_previous_session_evidence_and_preserves_existing_text()
        Dim directoryPath = Path.Combine(Path.GetTempPath(), "fms-session-start-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(directoryPath)
        Dim logPath = Path.Combine(directoryPath, "current.log")
        Dim original = "Session started: 2026-10-04 13:03:08.1301; PID=2147483647" & vbLf & "original evidence" & vbLf
        Try
            AppFileLogger.ResetForTesting()
            File.WriteAllText(logPath, original)
            AppFileLogger.LogDirectoryOverride = directoryPath
            AppFileLogger.Initialize()
            Assert.True(AppFileLogger.IsReady)
            AppFileLogger.ResetForTesting()
            Dim text = File.ReadAllText(logPath)
            Assert.StartsWith(original, text)
            Assert.Contains("Previous session PID=2147483647 started 2026-10-04 13:03:08.1301 did not finish", text)
            Assert.Contains("Windows Error Reporting", text)
        Finally
            AppFileLogger.ResetForTesting()
            AppFileLogger.LogDirectoryOverride = Nothing
            Directory.Delete(directoryPath, True)
        End Try
    End Sub
End Class
