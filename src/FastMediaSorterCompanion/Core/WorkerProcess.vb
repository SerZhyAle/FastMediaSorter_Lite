Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Locates, spawns and stops the bundled Android-share sidecar. The worker exe
''' is a sibling payload resolved relative to the running app exe:
''' &lt;app dir&gt;\companion\fms-share-worker.exe in every distribution channel.
''' Never touches the worker's data dir (host key is TOFU-pinned by phones).
''' Spawn pattern mirrors Translate\OllamaManager.vb (hidden, no window).
''' </summary>
Public Module WorkerProcess

    ''' <summary>Payload exe file name.</summary>
    Public Const ExeName As String = "fms-share-worker.exe"

    ''' <summary>Process name for Process.GetProcessesByName (no extension).</summary>
    Public Const ProcessName As String = "fms-share-worker"

    ''' <summary>
    ''' Absolute path of the sibling worker exe, or "" if it cannot be resolved.
    ''' Resolved from Application.ExecutablePath only (Invariant 2).
    ''' </summary>
    Public Function WorkerExePath() As String
        Try
            Dim baseDir As String = Path.GetDirectoryName(Application.ExecutablePath)
            If String.IsNullOrEmpty(baseDir) Then Return ""
            Return Path.Combine(baseDir, "companion", ExeName)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>True when the worker exe is present next to the app.</summary>
    Public Function IsAvailable() As Boolean
        Dim p As String = WorkerExePath()
        Return p.Length > 0 AndAlso File.Exists(p)
    End Function

    ''' <summary>
    ''' Ensures the worker is listening: connects if it already runs, otherwise
    ''' spawns it hidden and polls the pipe up to totalWaitMs. Returns the worker
    ''' status (from GetStatus), or Nothing if it could not be reached.
    ''' </summary>
    ''' <summary>
    ''' How long after a failed bring-up a repeat call is answered from memory instead of
    ''' launching another process. Deliberately SHORT: every current caller is an explicit
    ''' user action (open the window, share a folder, resume from the tray), and making a
    ''' user wait out a long backoff after they read the error and clicked again would be
    ''' worse than the waste it prevents. This only swallows the burst - a double click, a
    ''' window reopened at once, or a future caller that ends up on a timer.
    ''' </summary>
    Private Const Spawn_Backoff_Ms As Integer = 5000

    Private spawn_Failed_At As DateTime = DateTime.MinValue

    Public Function EnsureRunning(Optional totalWaitMs As Integer = 5000) As WorkerResponse
        ' Fast path: a worker is already listening.
        Dim status As WorkerResponse = TryGetStatus(400)
        If status IsNot Nothing Then Return status

        If Not IsAvailable() Then Return Nothing

        If spawn_Failed_At <> DateTime.MinValue AndAlso
           (DateTime.UtcNow - spawn_Failed_At).TotalMilliseconds < Spawn_Backoff_Ms Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion: spawn backoff - not retrying yet")
            Return Nothing
        End If

        Try
            ' Using: we never wait on the object itself (readiness is decided by the pipe
            ' poll below), and a discarded Process holds its handle until finalization.
            Using spawned As Process = Process.Start(New ProcessStartInfo(WorkerExePath()) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden})
            End Using
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion: spawn failed: " & ex.Message)
            spawn_Failed_At = DateTime.UtcNow
            Return Nothing
        End Try

        Dim sw As Stopwatch = Stopwatch.StartNew()
        Do While sw.ElapsedMilliseconds < totalWaitMs
            Thread.Sleep(200)
            status = TryGetStatus(400)
            If status IsNot Nothing Then
                spawn_Failed_At = DateTime.MinValue
                Return status
            End If
        Loop

        ' It started but never answered the pipe - just as bad as a failed launch, and the
        ' same backoff applies.
        spawn_Failed_At = DateTime.UtcNow
        Return Nothing
    End Function

    ''' <summary>GetStatus with transport failures swallowed (returns Nothing).</summary>
    Public Function TryGetStatus(Optional connectTimeoutMs As Integer = 1000) As WorkerResponse
        Try
            Return WorkerIpc.Send(New WorkerRequest With {.type = "GetStatus"}, connectTimeoutMs)
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Hard stop: asks the worker to stop serving, then kills the worker process.
    ''' NOT called on app close - closing LITE must leave the worker running so
    ''' shares survive (acceptance checklist). Use for uninstall / explicit quit.
    ''' </summary>
    Public Sub StopWorker()
        Try
            WorkerIpc.Send(New WorkerRequest With {.type = "StopServer"}, 1000)
        Catch
        End Try
        Try
            For Each p As Process In Process.GetProcessesByName(ProcessName)
                Try
                    p.Kill()
                    p.WaitForExit(2000)
                Catch
                Finally
                    p.Dispose()
                End Try
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " companion: stop failed: " & ex.Message)
        End Try
    End Sub

End Module
