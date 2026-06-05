Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Locates, starts and installs the local Ollama runtime. The translator only
''' talks to the HTTP API; this module manages the process/binary around it.
''' </summary>
Public Module OllamaManager

    Public Const DownloadUrl As String = "https://ollama.com/download/OllamaSetup.exe"
    Public Const WebPage As String = "https://ollama.com/download"

    ''' <summary>Path to ollama.exe if found (common install dirs + PATH), else "".</summary>
    Public Function FindExe() As String
        Dim candidates As New List(Of String)
        candidates.Add(CombineSafe(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\Ollama\ollama.exe"))
        candidates.Add(CombineSafe(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Ollama\ollama.exe"))
        candidates.Add(CombineSafe(Environment.GetEnvironmentVariable("ProgramFiles"), "Ollama\ollama.exe"))
        candidates.Add(CombineSafe(Environment.GetEnvironmentVariable("ProgramW6432"), "Ollama\ollama.exe"))

        For Each c As String In candidates
            If c.Length > 0 AndAlso File.Exists(c) Then Return c
        Next

        ' Search PATH.
        Dim pathVar As String = Environment.GetEnvironmentVariable("PATH")
        If Not String.IsNullOrEmpty(pathVar) Then
            For Each dir As String In pathVar.Split(";"c)
                Dim d As String = dir.Trim()
                If d.Length = 0 Then Continue For
                Dim candidate As String = CombineSafe(d, "ollama.exe")
                If candidate.Length > 0 AndAlso File.Exists(candidate) Then Return candidate
            Next
        End If

        Return ""
    End Function

    Public Function IsInstalled() As Boolean
        Return FindExe().Length > 0
    End Function

    Public Function IsProcessRunning() As Boolean
        Try
            If Process.GetProcessesByName("ollama").Length > 0 Then Return True
            If Process.GetProcessesByName("ollama app").Length > 0 Then Return True
        Catch
        End Try
        Return False
    End Function

    ''' <summary>Starts the Ollama server (tray app if present, else "ollama serve").</summary>
    Public Function StartServer() As Boolean
        Dim exe As String = FindExe()
        If exe.Length = 0 Then Return False
        Try
            Dim dir As String = Path.GetDirectoryName(exe)
            Dim appExe As String = Path.Combine(dir, "ollama app.exe")
            If File.Exists(appExe) Then
                Process.Start(New ProcessStartInfo(appExe) With {.UseShellExecute = True})
            Else
                Process.Start(New ProcessStartInfo(exe, "serve") With {
                    .UseShellExecute = False, .CreateNoWindow = True, .WindowStyle = ProcessWindowStyle.Hidden})
            End If
            Return True
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: start failed: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Downloads OllamaSetup.exe to %TEMP%, reporting percent. Returns path or "".</summary>
    Public Async Function DownloadInstallerAsync(progress As IProgress(Of String), ct As CancellationToken) As Task(Of String)
        Dim dest As String = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe")
        Try
            Using client As New HttpClient()
                client.Timeout = Timeout.InfiniteTimeSpan
                Using resp As HttpResponseMessage = Await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(False)
                    If Not resp.IsSuccessStatusCode Then Return ""
                    Dim total As Long = If(resp.Content.Headers.ContentLength.HasValue, resp.Content.Headers.ContentLength.Value, -1L)
                    Using src As Stream = Await resp.Content.ReadAsStreamAsync().ConfigureAwait(False)
                        Using fs As New FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)
                            Dim buffer(81919) As Byte
                            Dim totalRead As Long = 0
                            Dim lastPct As Integer = -1
                            Do
                                Dim n As Integer = Await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(False)
                                If n <= 0 Then Exit Do
                                Await fs.WriteAsync(buffer, 0, n, ct).ConfigureAwait(False)
                                totalRead += n
                                If progress IsNot Nothing Then
                                    If total > 0 Then
                                        Dim pct As Integer = CInt(totalRead * 100L \ total)
                                        If pct <> lastPct Then
                                            lastPct = pct
                                            progress.Report(pct.ToString() & "% (" & MB(totalRead) & "/" & MB(total) & " MB)")
                                        End If
                                    Else
                                        progress.Report(MB(totalRead) & " MB")
                                    End If
                                End If
                            Loop
                        End Using
                    End Using
                End Using
            End Using
            Return dest
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: installer download failed: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub RunInstaller(installerPath As String)
        Try
            Process.Start(New ProcessStartInfo(installerPath) With {.UseShellExecute = True})
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: run installer failed: " & ex.Message)
        End Try
    End Sub

    Public Sub OpenWebPage()
        Try
            Process.Start(New ProcessStartInfo(WebPage) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

    Private Function MB(bytes As Long) As String
        Return (bytes \ (1024L * 1024L)).ToString()
    End Function

    Private Function CombineSafe(root As String, tail As String) As String
        If String.IsNullOrEmpty(root) Then Return ""
        Try
            Return Path.Combine(root, tail)
        Catch
            Return ""
        End Try
    End Function

End Module
