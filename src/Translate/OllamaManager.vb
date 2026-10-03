Option Strict On

Imports System.IO
Imports System.Net.Http
Imports System.Runtime.InteropServices
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

    ''' <summary>Ollama for Windows is a 64-bit program for Windows 10 and newer (audit
    ''' 057.S10-4). Pure, so the rule is held by a test: the decision is about the OPERATING
    ''' SYSTEM, not the process - the x86 viewer on 64-bit Windows 10 can still install it.</summary>
    Friend Function IsSupportedOs(majorVersion As Integer, is64BitOs As Boolean) As Boolean
        Return is64BitOs AndAlso majorVersion >= 10
    End Function

    ''' <summary>True when this machine can run the Ollama installer. Unknown version
    ''' (the query failed) is treated as supported: refusing on a guess would take the
    ''' offer away from a machine that could use it.</summary>
    Public Function IsSupportedOs() As Boolean
        Dim major As Integer = WindowsMajorVersion()
        Return IsSupportedOs(If(major > 0, major, 10), Environment.Is64BitOperatingSystem)
    End Function

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure OsVersionInfo
        Public Size As Integer
        Public Major As Integer
        Public Minor As Integer
        Public Build As Integer
        Public PlatformId As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)> Public CsdVersion As String
    End Structure

    <DllImport("ntdll.dll")>
    Private Function RtlGetVersion(ByRef info As OsVersionInfo) As Integer
    End Function

    ''' <summary>The real Windows major version, 0 when it cannot be read. Not
    ''' Environment.OSVersion: the net48 viewer ships without a manifest, and an
    ''' unmanifested process is told it runs on Windows 8 (6.2) even on Windows 10.</summary>
    Private Function WindowsMajorVersion() As Integer
        Try
            Dim info As New OsVersionInfo()
            info.Size = Marshal.SizeOf(GetType(OsVersionInfo))
            If RtlGetVersion(info) = 0 Then Return info.Major
        Catch
        End Try
        Return 0
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
            AppFileLogger.LogException("Ollama: start server", ex)
            Return False
        End Try
    End Function

    ''' <summary>The common name on Ollama's code-signing certificate. The installer is run
    ''' only when its Authenticode signature is valid AND carries this name.</summary>
    Public Const ExpectedSigner As String = "Ollama Inc."

    ''' <summary>
    ''' Downloads OllamaSetup.exe, reporting percent. Returns path or "".
    '''
    ''' Into a fresh, randomly named folder under the app's own data directory - it used to be
    ''' a fixed %TEMP%\OllamaSetup.exe, overwritten in place and run, so a stale or planted
    ''' file at that predictable name was what got started (audit 051 E-14). The name of the
    ''' file itself stays OllamaSetup.exe: installers look at it.
    ''' </summary>
    Public Async Function DownloadInstallerAsync(progress As IProgress(Of String), ct As CancellationToken) As Task(Of String)
        Dim downloads As String = Path.Combine(AppPaths.LocalAppDataRoot(), "downloads")
        SweepOldDownloads(downloads)
        Dim folder As String = Path.Combine(downloads, Path.GetRandomFileName())
        Directory.CreateDirectory(folder)
        Dim dest As String = Path.Combine(folder, "OllamaSetup.exe")
        Try
            Using client As New HttpClient()
                client.Timeout = Timeout.InfiniteTimeSpan
                Using resp As HttpResponseMessage = Await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(False)
                    If Not resp.IsSuccessStatusCode Then
                        ' This function never throws but for Cancel, so the caller's
                        ' Failure log never fires: the reason is written here (057.S12-7).
                        AppFileLogger.WriteLine("Ollama installer download: HTTP " & CInt(resp.StatusCode).ToString(Globalization.CultureInfo.InvariantCulture))
                        Return ""
                    End If
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
            ' A cancelled download leaves no partial installer behind (APP-BEHAVIOUR rule 3).
            TryDeletePartial(dest)
            Throw
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: installer download failed: " & ex.Message)
            AppFileLogger.LogException("Ollama installer download", ex)
            TryDeletePartial(dest)
            Return ""
        End Try
    End Function

    ''' <summary>Removes installer folders left by earlier downloads (a started installer
    ''' cannot delete itself). A day old, so one still running is never touched.</summary>
    Private Sub SweepOldDownloads(downloads As String)
        Try
            If Not Directory.Exists(downloads) Then Return
            For Each dir As String In Directory.GetDirectories(downloads)
                Try
                    If Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-1) Then Directory.Delete(dir, True)
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    Private Sub TryDeletePartial(path As String)
        Try
            If File.Exists(path) Then File.Delete(path)
            Dim folder As String = IO.Path.GetDirectoryName(path)
            If Directory.Exists(folder) AndAlso Not Directory.EnumerateFileSystemEntries(folder).Any() Then Directory.Delete(folder)
        Catch
        End Try
    End Sub

    ''' <summary>True when the downloaded installer is signed by Ollama (valid Authenticode
    ''' signature, signer <see cref="ExpectedSigner"/>).</summary>
    Public Function IsGenuineInstaller(installerPath As String) As Boolean
        Return AuthenticodeCheck.IsSignedBy(installerPath, ExpectedSigner)
    End Function

    ''' <summary>Removes a downloaded installer that failed the signature check.</summary>
    Public Sub DiscardInstaller(installerPath As String)
        TryDeletePartial(installerPath)
    End Sub

    ''' <summary>
    ''' Starts the installer - but only after checking, at the moment of starting, that it is
    ''' signed by Ollama; the check is here rather than only in the caller so no path can run
    ''' an unchecked file. False = not started.
    ''' </summary>
    Public Function RunInstaller(installerPath As String) As Boolean
        If Not IsGenuineInstaller(installerPath) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: installer signature rejected")
            AppFileLogger.WriteLine("Ollama installer: signature check failed, not started")
            Return False
        End If
        Try
            Process.Start(New ProcessStartInfo(installerPath) With {.UseShellExecute = True})
            Return True
        Catch ex As Exception
            ' A declined UAC prompt or an antivirus block lands here (057.S12-7).
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " ollama: run installer failed: " & ex.Message)
            AppFileLogger.LogException("Ollama installer: start", ex)
            Return False
        End Try
    End Function

    Public Sub OpenWebPage()
        Try
            Process.Start(New ProcessStartInfo(WebPage) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Ollama: open download page", ex)
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
