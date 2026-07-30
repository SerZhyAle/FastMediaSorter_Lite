Option Strict On

Imports System.ComponentModel
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend Enum OptionalRuntimeKind
    Ocr
    Vlc
End Enum

Friend Module OptionalRuntimeManager

    Private Const OcrPackageVersion As String = "5.2.0"
    Private Const VlcPackageVersion As String = "3.0.21"

    Private Const OcrPackageUrl As String = "https://www.nuget.org/api/v2/package/Tesseract/" & OcrPackageVersion
    Private Const VlcPackageUrl As String = "https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/" & VlcPackageVersion

    Private Const VcRedistX86Url As String = "https://aka.ms/vc14/vc_redist.x86.exe"
    Private Const VcRedistX64Url As String = "https://aka.ms/vc14/vc_redist.x64.exe"

    ''' <summary>
    ''' Infinite HttpClient.Timeout is deliberate - it is a per-REQUEST clock, and these are
    ''' ~80 MB downloads that legitimately take minutes on a slow line. What bounds them
    ''' instead is <see cref="Download_Inactivity_Timeout_Ms"/>: a token that is cancelled when
    ''' no bytes arrive for that long. Before it existed, a stream that stalled with no FIN or
    ''' RST (a laptop changing Wi-Fi network, a dead NAT entry) left the await pending FOR EVER
    ''' - and since Main_Form caches that task as vlc_Init_Task, every later video in the
    ''' session awaited the same dead task: no VLC, and not even the external-player fallback,
    ''' which only runs once the await returns False.
    ''' </summary>
    Private ReadOnly httpClient As New HttpClient() With {.Timeout = Timeout.InfiniteTimeSpan}

    ''' <summary>How long a download may make no progress before it is given up on.</summary>
    Private Const Download_Inactivity_Timeout_Ms As Integer = 60000

    Private ReadOnly pathSync As New Object()

    ''' <summary>
    ''' Streams a URL to a file, cancelling if it stalls for Download_Inactivity_Timeout_Ms.
    ''' Copies block by block rather than using Stream.CopyToAsync so the watchdog is fed by
    ''' actual progress, not merely by the operation having been started.
    ''' </summary>
    Private Async Function DownloadWithInactivityTimeoutAsync(url As String, destinationFile As String) As Task
        Using watchdog As New CancellationTokenSource(Download_Inactivity_Timeout_Ms)
            Using response As HttpResponseMessage = Await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, watchdog.Token).ConfigureAwait(False)
                response.EnsureSuccessStatusCode()
                Using source As Stream = Await response.Content.ReadAsStreamAsync().ConfigureAwait(False)
                    Using dest As New FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None)
                        Dim buffer(81919) As Byte
                        Do
                            Dim read As Integer = Await source.ReadAsync(buffer, 0, buffer.Length, watchdog.Token).ConfigureAwait(False)
                            If read <= 0 Then Exit Do
                            Await dest.WriteAsync(buffer, 0, read, watchdog.Token).ConfigureAwait(False)
                            ' Progress = another full window before we call it stalled.
                            watchdog.CancelAfter(Download_Inactivity_Timeout_Ms)
                        Loop
                    End Using
                End Using
            End Using
        End Using
    End Function

    Public Function GetOcrRuntimeDir() As String
        Dim installed As String = Path.Combine(OcrRuntimeRoot(), CurrentArchFolder())
        If File.Exists(Path.Combine(installed, "tesseract50.dll")) Then Return installed

        Dim local As String = Path.Combine(OcrPaths.ExeDir(), CurrentArchFolder())
        If File.Exists(Path.Combine(local, "tesseract50.dll")) Then Return local

        Return ""
    End Function

    Public Function GetVlcRuntimeDir() As String
        Dim installed As String = Path.Combine(VlcRuntimeRoot(), "libvlc", CurrentVlcArchFolder())
        If File.Exists(Path.Combine(installed, "libvlc.dll")) Then Return installed

        Dim local As String = Path.Combine(OcrPaths.ExeDir(), "libvlc", CurrentVlcArchFolder())
        If File.Exists(Path.Combine(local, "libvlc.dll")) Then Return local

        Return ""
    End Function

    Public Function HasOcrRuntime() As Boolean
        Return GetOcrRuntimeDir().Length > 0
    End Function

    Public Function HasVlcRuntime() As Boolean
        Return GetVlcRuntimeDir().Length > 0
    End Function

    Public Function TryPrepareOcrRuntime(ByRef reason As String) As Boolean
        Dim dir As String = GetOcrRuntimeDir()
        If dir.Length = 0 Then
            reason = "OCR runtime is not installed."
            Return False
        End If

#If Not NETFRAMEWORK Then
        ' Single-file publish: Tesseract.dll is bundled into the apphost, so its
        ' InteropDotNet loader sees Assembly.Location = "" and dies computing its
        ' probe paths (ArgumentNullException) even with the natives on disk.
        ' CustomSearchPath is probed FIRST and expects the x64\ subfolder under it -
        ' point it at the parent of whichever runtime dir won (exe-adjacent bundle
        ' or the downloaded %LOCALAPPDATA% tree).
        Try
            InteropDotNet.LibraryLoader.Instance.CustomSearchPath = Path.GetDirectoryName(dir)
        Catch
        End Try
#End If

        PrependToPath(dir)
        Return ProbeDll(dir, "tesseract50.dll", reason)
    End Function

    Public Function TryPrepareVlcRuntime(ByRef reason As String) As Boolean
        Dim dir As String = GetVlcRuntimeDir()
        If dir.Length = 0 Then
            reason = "VLC runtime is not installed."
            Return False
        End If

        PrependToPath(dir)
        Dim pluginDir As String = Path.Combine(dir, "plugins")
        If Directory.Exists(pluginDir) Then
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginDir)
        End If

        Return ProbeDll(dir, "libvlc.dll", reason)
    End Function

    Public Async Function EnsureOcrRuntimeInteractiveAsync(owner As IWin32Window) As Task(Of Boolean)
        Return Await EnsureRuntimeInteractiveAsync(OptionalRuntimeKind.Ocr, owner).ConfigureAwait(True)
    End Function

    ''' <summary>Was a synchronous `.GetAwaiter().GetResult()` wrapper - deadlocked the
    ''' UI thread the moment a download actually happened, because the awaited chain
    ''' needs that same (blocked) thread to run its continuation on. Now a plain async
    ''' passthrough, mirroring <see cref="EnsureOcrRuntimeInteractiveAsync"/>; callers
    ''' must Await it instead of blocking on it.</summary>
    Public Async Function EnsureVlcRuntimeInteractiveAsync(owner As IWin32Window) As Task(Of Boolean)
        Return Await EnsureRuntimeInteractiveAsync(OptionalRuntimeKind.Vlc, owner).ConfigureAwait(True)
    End Function

    Public Function GetOcrUnavailableStatusText() As String
        Return Localization.T("OCR не установлен")
    End Function

    Public Function GetVlcUnavailableStatusText() As String
        Return Localization.T("VLC не установлен, открываю внешний плеер")
    End Function

    Private Async Function EnsureRuntimeInteractiveAsync(kind As OptionalRuntimeKind, owner As IWin32Window) As Task(Of Boolean)
        Dim reason As String = ""
        If IsRuntimeReady(kind, reason) Then Return True

        If Not IsRuntimeInstalled(kind) Then
            Dim confirmText As String = GetInstallPrompt(kind)
            If MessageBox.Show(owner, confirmText, "Fast Media Sorter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return False
            End If

            Dim installError As String = Await InstallRuntimePackageAsync(kind).ConfigureAwait(True)
            If installError.Length > 0 Then
                ShowRuntimeError(owner, GetRuntimeName(kind), installError)
                Return False
            End If
        End If

        reason = ""
        If IsRuntimeReady(kind, reason) Then Return True

        If LooksLikeVcRuntimeMissing(reason) Then
            Dim vcPrompt As String = GetVcPrompt(kind)
            If MessageBox.Show(owner, vcPrompt, "Fast Media Sorter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return False
            End If

            Dim vcError As String = Await InstallVcRedistAsync().ConfigureAwait(True)
            If vcError.Length > 0 Then
                ShowRuntimeError(owner, "Microsoft Visual C++ Redistributable", vcError)
                Return False
            End If

            reason = ""
            If IsRuntimeReady(kind, reason) Then Return True
        End If

        ShowRuntimeError(owner, GetRuntimeName(kind), reason)
        Return False
    End Function

    Private Function IsRuntimeInstalled(kind As OptionalRuntimeKind) As Boolean
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return HasOcrRuntime()
            Case OptionalRuntimeKind.Vlc
                Return HasVlcRuntime()
            Case Else
                Return False
        End Select
    End Function

    Private Function IsRuntimeReady(kind As OptionalRuntimeKind, ByRef reason As String) As Boolean
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return TryPrepareOcrRuntime(reason)
            Case OptionalRuntimeKind.Vlc
                Return TryPrepareVlcRuntime(reason)
            Case Else
                reason = "Unknown runtime."
                Return False
        End Select
    End Function

    Private Async Function InstallRuntimePackageAsync(kind As OptionalRuntimeKind) As Task(Of String)
        Dim tempFile As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter-" & kind.ToString().ToLowerInvariant() & ".nupkg")
        Dim targetRoot As String = GetTargetRoot(kind)

        Try
            Directory.CreateDirectory(targetRoot)

            Await DownloadWithInactivityTimeoutAsync(GetPackageUrl(kind), tempFile).ConfigureAwait(False)

            Using archive As ZipArchive = ZipFile.OpenRead(tempFile)
                For Each entry As ZipArchiveEntry In archive.Entries
                    Dim relative As String = MapEntry(kind, entry.FullName)
                    If relative.Length = 0 Then Continue For

                    Dim destination As String = Path.Combine(targetRoot, relative.Replace("/"c, Path.DirectorySeparatorChar))
                    Dim destinationDir As String = Path.GetDirectoryName(destination)
                    If Not String.IsNullOrEmpty(destinationDir) Then Directory.CreateDirectory(destinationDir)

                    Using src As Stream = entry.Open()
                        Using dst As New FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)
                            src.CopyTo(dst)
                        End Using
                    End Using
                Next
            End Using

            Return ""
        Catch ex As Exception
            Return ex.Message
        Finally
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch
            End Try
        End Try
    End Function

    Private Async Function InstallVcRedistAsync() As Task(Of String)
        Dim url As String = If(Environment.Is64BitProcess, VcRedistX64Url, VcRedistX86Url)
        Dim fileName As String = If(Environment.Is64BitProcess, "vc_redist.x64.exe", "vc_redist.x86.exe")
        Dim tempFile As String = Path.Combine(Path.GetTempPath(), fileName)

        Try
            Await DownloadWithInactivityTimeoutAsync(url, tempFile).ConfigureAwait(False)

            Using proc As Process = Process.Start(New ProcessStartInfo(tempFile, "/install /quiet /norestart") With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            })
                If proc Is Nothing Then Return "Could not start the VC++ installer."
                proc.WaitForExit()
                If proc.ExitCode <> 0 AndAlso proc.ExitCode <> 3010 Then
                    Return "VC++ installer exit code: " & proc.ExitCode.ToString()
                End If
            End Using

            Return ""
        Catch ex As Exception
            Return ex.Message
        Finally
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch
            End Try
        End Try
    End Function

    Private Function MapEntry(kind As OptionalRuntimeKind, fullName As String) As String
        Dim normalized As String = fullName.Replace("\"c, "/"c)
        If normalized.EndsWith("/", StringComparison.Ordinal) Then Return ""

        Select Case kind
            Case OptionalRuntimeKind.Ocr
                If normalized.StartsWith("x64/", StringComparison.OrdinalIgnoreCase) Then
                    Return normalized
                End If
                If normalized.StartsWith("x86/", StringComparison.OrdinalIgnoreCase) Then
                    Return normalized
                End If

            Case OptionalRuntimeKind.Vlc
                If normalized.StartsWith("build/x64/", StringComparison.OrdinalIgnoreCase) Then
                    Return "libvlc/win-x64/" & normalized.Substring("build/x64/".Length)
                End If
                If normalized.StartsWith("build/x86/", StringComparison.OrdinalIgnoreCase) Then
                    Return "libvlc/win-x86/" & normalized.Substring("build/x86/".Length)
                End If
        End Select

        Return ""
    End Function

    Private Function GetTargetRoot(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return OcrRuntimeRoot()
            Case OptionalRuntimeKind.Vlc
                Return VlcRuntimeRoot()
            Case Else
                Return OcrPaths.AppDataRoot()
        End Select
    End Function

    Private Function GetPackageUrl(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return OcrPackageUrl
            Case OptionalRuntimeKind.Vlc
                Return VlcPackageUrl
            Case Else
                Return ""
        End Select
    End Function

    Private Function GetRuntimeName(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return "OCR runtime"
            Case OptionalRuntimeKind.Vlc
                Return "VLC runtime"
            Case Else
                Return "runtime"
        End Select
    End Function

    Private Function GetInstallPrompt(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return Localization.T("OCR-движок ещё не установлен. Скачать и установить его сейчас?")

            Case OptionalRuntimeKind.Vlc
                Return Localization.T("Поддержка VLC ещё не установлена. Скачать и установить её сейчас?")
        End Select

        Return ""
    End Function

    Private Function GetVcPrompt(kind As OptionalRuntimeKind) As String
        Dim feature As String = If(kind = OptionalRuntimeKind.Ocr, "OCR", "VLC")
        Return Localization.TF("{0} требует Microsoft Visual C++ Redistributable. Скачать и тихо установить его сейчас?", feature)
    End Function

    Private Function OcrRuntimeRoot() As String
        Return Path.Combine(OcrPaths.AppDataRoot(), "optional-runtime", "ocr", OcrPackageVersion)
    End Function

    Private Function VlcRuntimeRoot() As String
        Return Path.Combine(OcrPaths.AppDataRoot(), "optional-runtime", "vlc", VlcPackageVersion)
    End Function

    Private Function CurrentArchFolder() As String
        Return If(Environment.Is64BitProcess, "x64", "x86")
    End Function

    Private Function CurrentVlcArchFolder() As String
        Return If(Environment.Is64BitProcess, "win-x64", "win-x86")
    End Function

    Private Sub PrependToPath(dir As String)
        If dir.Length = 0 Then Return

        SyncLock pathSync
            Dim current As String = Environment.GetEnvironmentVariable("PATH")
            Dim parts As New List(Of String)
            parts.Add(dir)
            If Not String.IsNullOrEmpty(current) Then parts.Add(current)
            Environment.SetEnvironmentVariable("PATH", String.Join(";", parts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
        End SyncLock
    End Sub

    Private Function ProbeDll(baseDir As String, fileName As String, ByRef reason As String) As Boolean
        Dim fullPath As String = Path.Combine(baseDir, fileName)
        If Not File.Exists(fullPath) Then
            reason = "Required file is missing: " & fullPath
            Return False
        End If

        Dim handle As IntPtr = LoadLibrary(fullPath)
        If handle = IntPtr.Zero Then
            reason = BuildLoadLibraryMessage(fullPath, Marshal.GetLastWin32Error())
            Return False
        End If

        FreeLibrary(handle)
        reason = ""
        Return True
    End Function

    Private Function BuildLoadLibraryMessage(fullPath As String, lastError As Integer) As String
        Dim detail As String
        Try
            detail = New Win32Exception(lastError).Message
        Catch
            detail = "Win32 error " & lastError.ToString()
        End Try

        If lastError = 126 Then
            Return "Could not load '" & fullPath & "'. A required native dependency is missing. " & detail
        End If

        If lastError = 193 Then
            Return "Could not load '" & fullPath & "' because of an architecture mismatch. " & detail
        End If

        Return "Could not load '" & fullPath & "'. " & detail
    End Function

    Private Function LooksLikeVcRuntimeMissing(reason As String) As Boolean
        If String.IsNullOrWhiteSpace(reason) Then Return False
        Return reason.IndexOf("native dependency is missing", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               reason.IndexOf("specified module could not be found", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Private Sub ShowRuntimeError(owner As IWin32Window, title As String, details As String)
        Dim prefix As String = Localization.TF("Не удалось подготовить {0}.", title)

        MessageBox.Show(owner,
                        prefix & Environment.NewLine & Environment.NewLine & details,
                        "Fast Media Sorter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning)
    End Sub

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function LoadLibrary(lpFileName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function FreeLibrary(hModule As IntPtr) As Boolean
    End Function

End Module
