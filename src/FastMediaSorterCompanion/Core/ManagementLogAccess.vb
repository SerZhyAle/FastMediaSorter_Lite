Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO

''' <summary>Ticket 246: resolve the live log on each gesture, including UTC rollover and service mode.
''' No clipboard contents, credentials or configuration payloads belong in these files.</summary>
Friend Module ManagementLogAccess
    Friend Function ServerLogPath(dataDir As String, utcNow As DateTimeOffset) As String
        If String.IsNullOrWhiteSpace(dataDir) Then Throw New IOException("The exchange data directory is unavailable.")
        Return Path.Combine(dataDir, "logs", "exchange-" & utcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & ".log")
    End Function

    Friend Sub OpenShareLog(Optional folder As Boolean = False)
        OpenPath(AppFileLogger.LogFilePath, folder)
    End Sub

    Friend Sub OpenServerLog(dataDir As String, Optional folder As Boolean = False)
        OpenPath(ServerLogPath(dataDir, DateTimeOffset.UtcNow), folder)
    End Sub

    Friend Function ServerAuditLogPath(dataDir As String, utcNow As DateTimeOffset) As String
        Return Path.Combine(Path.GetDirectoryName(ServerLogPath(dataDir, utcNow)), "audit-" & utcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & ".log")
    End Function

    Friend Sub OpenServerAuditLog(dataDir As String)
        OpenPath(ServerAuditLogPath(dataDir, DateTimeOffset.UtcNow), False)
    End Sub

    ''' <summary>FileMode.OpenOrCreate preserves a live writer's file, including an empty session log.</summary>
    Friend Sub EnsureLogFile(logPath As String)
        If String.IsNullOrWhiteSpace(logPath) Then Throw New IOException("The log location is unavailable.")
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)))
        Using stream As New FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite Or FileShare.Delete)
        End Using
    End Sub

    Private Sub OpenPath(logPath As String, folder As Boolean)
        EnsureLogFile(logPath)
        Dim target As String = If(folder, Path.GetDirectoryName(Path.GetFullPath(logPath)), logPath)
        Using launched As Process = Process.Start(New ProcessStartInfo(target) With {.UseShellExecute = True})
        End Using
    End Sub
End Module
