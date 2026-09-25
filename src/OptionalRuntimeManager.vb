Option Strict On

Imports System.ComponentModel
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>Downloads and prepares the optional native runtimes. The OptionalRuntimeKind
''' enum and the disk-level install rules (staging, marker, containment, pinned digests)
''' live in RuntimeInstallPlan.vb, where they are unit-tested (audit 050).</summary>
Friend Module OptionalRuntimeManager

    Private Const OcrPackageVersion As String = "5.2.0"
    Private Const VlcPackageVersion As String = "3.0.21"

    Private Const OcrPackageUrl As String = "https://www.nuget.org/api/v2/package/Tesseract/" & OcrPackageVersion
    Private Const VlcPackageUrl As String = "https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/" & VlcPackageVersion

    ''' <summary>
    ''' Microsoft's own download of the VC++ runtime a native OCR/VLC library may need. The
    ''' viewer never runs it (ticket 037 Ф-3.3, owner decision 2026-09-25): the installer
    ''' needs administrator rights, and the viewer is the part of this product that never
    ''' asks for them. It only offers the link, matched to the process bitness - a 32-bit
    ''' viewer needs the x86 runtime even on 64-bit Windows.
    ''' </summary>
    Private Const VcRedistX86Url As String = "https://aka.ms/vc14/vc_redist.x86.exe"
    Private Const VcRedistX64Url As String = "https://aka.ms/vc14/vc_redist.x64.exe"

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' FFmpeg, pinned (§8.2). Three things about this constant set are deliberate:
    '''
    ''' * A VERSIONED tag, never "latest". The specification names BtbN/FFmpeg-Builds, and
    '''   that repository turned out not to be pinnable AT ALL: it keeps only about five
    '''   weeks of autobuild tags, so a tag pinned today 404s within a month and the feature
    '''   quietly stops working for everyone who had not downloaded yet. GyanD/codexffmpeg
    '''   publishes the same thing - a 64-bit STATIC GPLv3 Windows build with libx264 - under
    '''   permanent release tags, which is what "pinned" actually requires. Everything else
    '''   §8.2 asks for is unchanged.
    ''' * The SHA-256 is mandatory - and since audit 050 D-13 so are the NuGet packages'
    '''   (RuntimeInstallPlan). "It downloaded" is not "it is what we asked for". A mismatch
    '''   aborts before a single byte is extracted.
    ''' * The size is a constant rather than a number inside a sentence, so the download
    '''   prompt cannot go stale when the pinned build changes size.
    ''' </summary>
    Private Const FfmpegVersion As String = "9.0.1"
    Private Const FfmpegArchiveName As String = "ffmpeg-" & FfmpegVersion & "-essentials_build.zip"
    Private Const FfmpegUrl As String = "https://github.com/GyanD/codexffmpeg/releases/download/" & FfmpegVersion & "/" & FfmpegArchiveName
    Private Const FfmpegSha256 As String = "fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9"

    ''' <summary>Rounded down from the real 111 MB - the prompt says "about".</summary>
    Friend Const FfmpegDownloadMb As Integer = 110

    ' The only file extracted is RuntimeInstallPlan.FfmpegExeName: no ffplay, no ffprobe, no
    ' documentation. The gpl build is static, so there is nothing beside it to carry.
#End If

    ''' <summary>Trees already found whole in this process, so the 500-file VLC tree is
    ''' measured once per session rather than on every video (050 D-09).</summary>
    Private ReadOnly verifiedTrees As New System.Collections.Concurrent.ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

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
    ''' <param name="progress">Bytes against the announced size, for the download dialog
    ''' (ticket 028 Ф-6). Nothing on the x86 fallback, which has no dialog.</param>
    ''' <param name="cancel">The user's Cancel. Distinct from the stall watchdog: a cancel is
    ''' an outcome, a stall is a network failure.</param>
    Friend Async Function DownloadWithInactivityTimeoutAsync(url As String, destinationFile As String,
                                                             Optional progress As IProgress(Of DownloadReport) = Nothing,
                                                             Optional cancel As CancellationToken = Nothing) As Task
        Using watchdog As New CancellationTokenSource(Download_Inactivity_Timeout_Ms)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(watchdog.Token, cancel)
                Try
                    Using response As HttpResponseMessage = Await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(False)
                        ' Disposing the response is what actually aborts a stalled body read on
                        ' .NET Framework (audit 050 D-08): once the headers are in, netfx's
                        ' content stream checks the token only BEFORE a read starts, and
                        ' HttpClient has already dropped its own abort registration - so the
                        ' watchdog fired and the read waited for ever anyway (the C-4 hang,
                        ' still alive on the x86 build). Harmless on .NET 10, whose read
                        ' honours the token by itself.
                        Using abortOnCancel As CancellationTokenRegistration = linked.Token.Register(Sub() DisposeQuietly(response))
                            response.EnsureSuccessStatusCode()
                            Dim total As Long = If(response.Content.Headers.ContentLength, -1L)
                            Using source As Stream = Await response.Content.ReadAsStreamAsync().ConfigureAwait(False)
                                Using dest As New FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None)
                                    Dim buffer(81919) As Byte
                                    Dim done As Long = 0
                                    Do
                                        Dim read As Integer = Await source.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(False)
                                        If read <= 0 Then Exit Do
                                        Await dest.WriteAsync(buffer, 0, read, linked.Token).ConfigureAwait(False)
                                        ' Progress = another full window before we call it stalled.
                                        watchdog.CancelAfter(Download_Inactivity_Timeout_Ms)
                                        done += read
                                        If progress IsNot Nothing Then progress.Report(New DownloadReport With {.BytesDone = done, .BytesTotal = total})
                                    Loop
                                End Using
                            End Using
                        End Using
                    End Using
                Catch ex As Exception When watchdog.IsCancellationRequested AndAlso Not cancel.IsCancellationRequested
                    ' The watchdog, not the user: a stalled line is a network failure and must
                    ' read as one (APP-BEHAVIOUR rule 6), not as a cancellation nobody asked for.
                    ' Any exception type: after the abort above, netfx faults the read with an
                    ' ObjectDisposedException/IOException/WebException, not a cancellation.
                    Throw New TimeoutException("Download stalled: no data for " &
                                               (Download_Inactivity_Timeout_Ms \ 1000).ToString() & " s from " & url, ex)
                Catch ex As Exception When cancel.IsCancellationRequested AndAlso Not TypeOf ex Is OperationCanceledException
                    ' The user's Cancel, surfacing through the same abort: still a cancel.
                    Throw New OperationCanceledException("Download cancelled.", ex, cancel)
                End Try
            End Using
        End Using
    End Function

    ''' <summary>
    ''' The folder holding tesseract50.dll, or "". The exe-adjacent bundle wins: it came with
    ''' the installer, and a downloaded tree used to be checked FIRST, so a broken download
    ''' shadowed a good bundled copy (050 D-09). A downloaded tree counts only when it is
    ''' whole - see <see cref="IsDownloadedTreeWhole"/>.
    ''' </summary>
    Public Function GetOcrRuntimeDir() As String
        Dim local As String = Path.Combine(OcrPaths.ExeDir(), CurrentArchFolder())
        If File.Exists(Path.Combine(local, "tesseract50.dll")) Then Return local

        Dim installed As String = DownloadedRuntimeDir(OptionalRuntimeKind.Ocr)
        If File.Exists(Path.Combine(installed, "tesseract50.dll")) AndAlso
           IsDownloadedTreeWhole(OptionalRuntimeKind.Ocr) Then Return installed

        Return ""
    End Function

    ''' <summary>The folder holding libvlc.dll, or "". Same order and rule as
    ''' <see cref="GetOcrRuntimeDir"/>.</summary>
    Public Function GetVlcRuntimeDir() As String
        Dim local As String = Path.Combine(OcrPaths.ExeDir(), "libvlc", CurrentVlcArchFolder())
        If File.Exists(Path.Combine(local, "libvlc.dll")) Then Return local

        Dim installed As String = DownloadedRuntimeDir(OptionalRuntimeKind.Vlc)
        If File.Exists(Path.Combine(installed, "libvlc.dll")) AndAlso
           IsDownloadedTreeWhole(OptionalRuntimeKind.Vlc) Then Return installed

        Return ""
    End Function

    ''' <summary>
    ''' The folder a downloaded runtime of this kind lives in, for THIS process's
    ''' architecture. It is the unit the installer stages, marks and renames: the x86 and
    ''' x64 viewers share %LOCALAPPDATA%, so each architecture is its own runtime.
    ''' </summary>
    Private Function DownloadedRuntimeDir(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return Path.Combine(OcrRuntimeRoot(), CurrentArchFolder())
            Case OptionalRuntimeKind.Vlc
                Return Path.Combine(VlcRuntimeRoot(), "libvlc", CurrentVlcArchFolder())
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return FfmpegRuntimeRoot()
#End If
            Case Else
                Return Path.Combine(OcrPaths.AppDataRoot(), "optional-runtime", "unknown")
        End Select
    End Function

    ''' <summary>
    ''' True when the downloaded tree is a whole runtime: its ".complete" marker matches it,
    ''' or - for a tree installed before the marker existed - it holds exactly what the pinned
    ''' package holds, in which case the marker is written now. A half-extracted tree is never
    ''' loaded, never blamed on the VC++ runtime, and is replaced by the next download
    ''' (050 D-09).
    ''' </summary>
    Private Function IsDownloadedTreeWhole(kind As OptionalRuntimeKind) As Boolean
        Dim dir As String = DownloadedRuntimeDir(kind)
        If verifiedTrees.ContainsKey(dir) Then Return True
        Select Case RuntimeInstallPlan.ReadTreeState(dir, RuntimeInstallPlan.PinnedTotals(kind, Environment.Is64BitProcess))
            Case RuntimeTreeState.Complete
                verifiedTrees(dir) = True
                Return True
            Case RuntimeTreeState.CompleteUnmarked
                Try
                    RuntimeInstallPlan.WriteCompleteMarker(dir)
                Catch ex As Exception
                    AppFileLogger.WriteLine("Runtime " & kind.ToString() & ": could not mark the adopted tree: " & ex.Message)
                End Try
                verifiedTrees(dir) = True
                Return True
            Case RuntimeTreeState.Incomplete
                AppFileLogger.WriteLine("Runtime " & kind.ToString() & ": the downloaded tree is incomplete and will be downloaded again: " & dir)
                Return False
            Case Else
                Return False
        End Select
    End Function

    Private Sub DisposeQuietly(target As IDisposable)
        Try
            target.Dispose()
        Catch
        End Try
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' The ffmpeg.exe to run, or "".
    '''
    ''' Exe-adjacent wins if present, as for GetOcrRuntimeDir/GetVlcRuntimeDir - so a future
    ''' offline packaging option needs no code change here, only a folder in the package.
    ''' Today nothing bundles it: FFmpeg is GPL and ~110 MB, and shipping nothing is what
    ''' keeps invariant 12 (THIRD-PARTY-NOTICES inside every package that bundles third-party
    ''' binaries) inapplicable rather than merely satisfied. The downloaded copy counts only
    ''' when whole: a truncated ffmpeg.exe used to be run as-is (050 D-09).
    ''' </summary>
    Public Function GetFfmpegPath() As String
        Dim local As String = Path.Combine(OcrPaths.ExeDir(), "ffmpeg", RuntimeInstallPlan.FfmpegExeName)
        If File.Exists(local) Then Return local

        Dim installed As String = Path.Combine(FfmpegRuntimeRoot(), RuntimeInstallPlan.FfmpegExeName)
        If File.Exists(installed) AndAlso IsDownloadedTreeWhole(OptionalRuntimeKind.Ffmpeg) Then Return installed

        Return ""
    End Function

    Public Function HasFfmpegRuntime() As Boolean
        Return GetFfmpegPath().Length > 0
    End Function

    ''' <summary>Downloads FFmpeg after an explicit Yes, if it is not already there. A No
    ''' simply cancels the action - nothing is remembered as broken and the next click asks
    ''' again (§8.3).</summary>
    Public Async Function EnsureFfmpegRuntimeInteractiveAsync(owner As IWin32Window) As Task(Of Boolean)
        Return Await EnsureRuntimeInteractiveAsync(OptionalRuntimeKind.Ffmpeg, owner).ConfigureAwait(True)
    End Function

    Private Function FfmpegRuntimeRoot() As String
        Return Path.Combine(OcrPaths.AppDataRoot(), "ffmpeg", FfmpegVersion)
    End Function
#End If

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

        ' tesseract50.dll imports leptonica-<ver>.dll from its OWN folder. Load that one
        ' first, by full path, so the by-name import binds to an already-resident module -
        ' see the note in ProbeDll for why the folder itself is not searchable everywhere.
        Try
            For Each dependency As String In Directory.GetFiles(dir, "leptonica*.dll")
                Dim ignored As String = ""
                ProbeDll(dir, Path.GetFileName(dependency), ignored)
            Next
        Catch
        End Try

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

#If NETFRAMEWORK Then
            Dim installError As String = Await InstallRuntimePackageAsync(kind).ConfigureAwait(True)
            If installError.Length > 0 Then
                ShowRuntimeError(owner, GetRuntimeName(kind), installError)
                Return False
            End If
#Else
            ' Progress, Cancel as its own outcome, and a failure offered as Retry
            ' (APP-BEHAVIOUR rules 3 and 6, ticket 028 Ф-3/Ф-6).
            If Not Await DownloadWithDialogAsync(owner, GetRuntimeName(kind),
                                                 Function(p, t) InstallRuntimePackageAsync(kind, p, t)).ConfigureAwait(True) Then
                Return False
            End If
#End If
        End If

        reason = ""
        If IsRuntimeReady(kind, reason) Then Return True

        If LooksLikeVcRuntimeMissing(reason) Then
            ' Named, and handed to the user - never run from here (ticket 037 Ф-3.3). This
            ' used to download vc_redist and start it quietly WITHOUT elevation, which the
            ' installer refuses, so the offer failed every time; run elevated it would have
            ' been an administrator prompt the install-trust page promises never comes from
            ' the viewer. Microsoft's installer asks for its own rights when the user runs it.
            AppFileLogger.WriteLine("Runtime " & kind.ToString() & " needs the VC++ runtime: " & reason)
            Dim vcUrl As String = If(Environment.Is64BitProcess, VcRedistX64Url, VcRedistX86Url)
            If MessageBox.Show(owner, GetVcPrompt(kind), "Fast Media Sorter", MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                Try
                    Process.Start(New ProcessStartInfo(vcUrl) With {.UseShellExecute = True})
                Catch ex As Exception
                    AppFileLogger.WriteLine("Could not open " & vcUrl & ": " & ex.Message)
                End Try
            End If
            Return False
        End If

#If NETFRAMEWORK Then
        ShowRuntimeError(owner, GetRuntimeName(kind), reason)
#Else
        ' Installed and still not loading: the loader's own words go to the log, the user
        ' reads what it means and where the details are (APP-BEHAVIOUR rule 6).
        AppFileLogger.WriteLine("Runtime " & kind.ToString() & " not ready: " & reason)
        MessageBox.Show(owner,
                        Localization.TF("Компонент «{0}» установлен, но не загружается.", GetRuntimeName(kind)) &
                        Environment.NewLine & Environment.NewLine & FailureText.Sentence(FailureText.FailureCause.Unknown),
                        "Fast Media Sorter", MessageBoxButtons.OK, MessageBoxIcon.Warning)
#End If
        Return False
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' One download behind the progress dialog, retried for as long as the user asks.
    ''' True when it finished; False when it was cancelled (said so, as an outcome) or when
    ''' the user declined to retry a failure.
    ''' </summary>
    Friend Async Function DownloadWithDialogAsync(owner As IWin32Window, what As String,
                                                   work As Func(Of IProgress(Of DownloadReport), CancellationToken, Task)) As Task(Of Boolean)
        Do
            Dim run As DownloadRun(Of Boolean) = Await Download_Progress_Form.RunAsync(Of Boolean)(
                owner, what, Localization.TF("Загрузка: {0}", what),
                Async Function(p, t)
                    Await work(p, t).ConfigureAwait(True)
                    Return True
                End Function).ConfigureAwait(True)
            If run.Succeeded Then Return True
            If run.Cancelled Then
                DownloadMessages.ShowCancelled(owner, what)
                Return False
            End If
            If Not DownloadMessages.AskRetry(owner, what, run.Failure) Then Return False
        Loop
    End Function
#End If

    Private Function IsRuntimeInstalled(kind As OptionalRuntimeKind) As Boolean
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return HasOcrRuntime()
            Case OptionalRuntimeKind.Vlc
                Return HasVlcRuntime()
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return HasFfmpegRuntime()
#End If
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
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                ' An executable we spawn, not a library we load - there is nothing to
                ' probe with LoadLibraryEx and no PATH to prepend to. Being there IS being
                ' ready; a build that cannot run reports itself through its exit code, which
                ' is where the conversion already looks (VideoConvertPlan.DecideEncode).
                If HasFfmpegRuntime() Then
                    reason = ""
                    Return True
                End If
                reason = "FFmpeg is not installed."
                Return False
#End If
            Case Else
                reason = "Unknown runtime."
                Return False
        End Select
    End Function

#If NETFRAMEWORK Then
    ''' <summary>The x86 fallback's shape, unchanged: no dialog, the failure as its raw text.</summary>
    Private Async Function InstallRuntimePackageAsync(kind As OptionalRuntimeKind) As Task(Of String)
        Try
            Await InstallRuntimePackageAsync(kind, Nothing, CancellationToken.None).ConfigureAwait(False)
            Return ""
        Catch ex As Exception
            Return FailureText.Describe(ex)
        End Try
    End Function
#End If

    ''' <summary>
    ''' Downloads and extracts one runtime package. Throws on failure - the caller decides
    ''' whether that becomes a cause-and-Retry dialog (mainline) or the raw line the x86
    ''' fallback always showed - and throws OperationCanceledException on the user's Cancel.
    ''' </summary>
    Private Async Function InstallRuntimePackageAsync(kind As OptionalRuntimeKind, progress As IProgress(Of DownloadReport),
                                                      cancel As CancellationToken) As Task
        ' The downloaded runtime tree is shared by all viewer processes. A named
        ' Mutex keeps another window from observing or overwriting a half-extracted
        ' libvlc/Tesseract package. Run the blocking ownership wait on a pool thread:
        ' the UI remains responsive while another process finishes its download.
        Await Task.Run(Sub() InstallRuntimePackageUnderMutex(kind, progress, cancel)).ConfigureAwait(False)
    End Function

    ''' <summary>
    ''' The download mutex (audit 050 D-12). Three things the plain WaitOne() got wrong:
    '''   * the wait is a loop of short waits that checks the user's Cancel, so the dialog no
    '''     longer sits at "Cancelling.." until another process finishes;
    '''   * an AbandonedMutexException - the owner died mid-install - means THIS thread now
    '''     owns the mutex; the old code left it owned and never released it. What the dead
    '''     owner left is only a staging folder, which the install sweeps;
    '''   * after acquiring, "installed?" is asked again: the other process usually finished
    '''     the very download this one was waiting to start, and downloading 100 MB again
    '''     over files it had already loaded ended in a sharing violation and a spurious
    '''     "download failed, Retry?".
    ''' </summary>
    Private Sub InstallRuntimePackageUnderMutex(kind As OptionalRuntimeKind, progress As IProgress(Of DownloadReport),
                                                cancel As CancellationToken)
        Using runtimeMutex As New Mutex(False, "FastMediaSorterRuntimeDownloadMutex")
            Dim ownsMutex As Boolean = False
            Try
                Do Until ownsMutex
                    cancel.ThrowIfCancellationRequested()
                    Try
                        ownsMutex = runtimeMutex.WaitOne(500)
                    Catch ex As AbandonedMutexException
                        ownsMutex = True
                        AppFileLogger.WriteLine("Runtime " & kind.ToString() & ": the previous download owner ended without releasing the mutex; taking over.")
                    End Try
                Loop

                If IsRuntimeInstalled(kind) Then Return
                InstallRuntimePackageAsyncCore(kind, progress, cancel).GetAwaiter().GetResult()
            Finally
                If ownsMutex Then
                    Try
                        runtimeMutex.ReleaseMutex()
                    Catch
                    End Try
                End If
            End Try
        End Using
    End Sub

    ''' <summary>
    ''' Download, verify, stage, mark, rename (audit 050 D-09, D-13; 045 §3 theme 3: stage,
    ''' verify, rename, and only then delete). The runtime folder is replaced in one rename,
    ''' so no process ever sees a half-written runtime, and an interrupted install leaves
    ''' only a ".partial-" folder that the next one sweeps.
    ''' </summary>
    Private Async Function InstallRuntimePackageAsyncCore(kind As OptionalRuntimeKind, progress As IProgress(Of DownloadReport),
                                                          cancel As CancellationToken) As Task
        Dim runtimeDir As String = DownloadedRuntimeDir(kind)
        Dim tempFile As String = Path.Combine(Path.GetTempPath(),
                                              "FastMediaSorter-" & kind.ToString().ToLowerInvariant() & "-" & Guid.NewGuid().ToString("N") & ".zip")
        Dim staging As String = RuntimeInstallPlan.StagingDirFor(runtimeDir)

        Try
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeDir.TrimEnd(Path.DirectorySeparatorChar)))
            RuntimeInstallPlan.SweepStaging(runtimeDir)

            Await DownloadWithInactivityTimeoutAsync(GetPackageUrl(kind), tempFile, progress, cancel).ConfigureAwait(False)
            cancel.ThrowIfCancellationRequested()
            ' Past this point nothing is cancelled: the package is here, and finishing the
            ' install is cheaper than explaining a half-done one. The dialog greys its Cancel
            ' out (050 D-20), and a Cancel pressed a moment earlier no longer turns a finished
            ' install into "cancelled, nothing installed".
            If progress IsNot Nothing Then progress.Report(New DownloadReport With {.Committed = True})

            ' Mandatory for every package (050 D-13): verified BEFORE a single byte is
            ' extracted rather than trusted because the download finished.
            Dim expectedHash As String = GetExpectedSha256(kind)
            If expectedHash.Length = 0 Then Throw New InvalidDataException("No pinned checksum for " & kind.ToString() & ".")
            Dim actualHash As String = Sha256OfFile(tempFile)
            If Not String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException("Checksum mismatch: expected " & expectedHash & ", got " & actualHash & ".")
            End If

            Directory.CreateDirectory(staging)
            Dim extracted As Integer = 0
            Using archive As ZipArchive = ZipFile.OpenRead(tempFile)
                For Each entry As ZipArchiveEntry In archive.Entries
                    ' Only this process's architecture: half the bytes, half the time, and
                    ' half the ways a full disk could stop it.
                    Dim relative As String = RuntimeInstallPlan.MapEntry(kind, entry.FullName, Environment.Is64BitProcess)
                    If relative.Length = 0 Then Continue For

                    Dim destination As String = RuntimeInstallPlan.ContainedDestination(staging, relative)
                    If destination Is Nothing Then
                        Throw New InvalidDataException("The package contains an entry outside its folder: " & entry.FullName)
                    End If
                    Dim destinationDir As String = Path.GetDirectoryName(destination)
                    If Not String.IsNullOrEmpty(destinationDir) Then Directory.CreateDirectory(destinationDir)

                    Using src As Stream = entry.Open()
                        Using dst As New FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)
                            src.CopyTo(dst)
                        End Using
                    End Using
                    extracted += 1
                Next
            End Using
            If extracted = 0 Then Throw New InvalidDataException("The package holds nothing for this architecture.")

            RuntimeInstallPlan.WriteCompleteMarker(staging)
            verifiedTrees.TryRemove(runtimeDir, Nothing)
            ' A stale or half-extracted tree in the way: never loaded (it had no valid
            ' marker), so nothing of this process holds it open.
            If Directory.Exists(runtimeDir) Then Directory.Delete(runtimeDir, True)
            Directory.Move(staging, runtimeDir)
        Finally
            ' A cancelled or failed download leaves no partial file behind (APP-BEHAVIOUR rule 3).
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch
            End Try
            Try
                If Directory.Exists(staging) Then Directory.Delete(staging, True)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>The pinned archive hash of every runtime package. Never empty: a package
    ''' that is extracted and loaded into this process is verified first (050 D-13).</summary>
    Private Function GetExpectedSha256(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return RuntimeInstallPlan.OcrPackageSha256
            Case OptionalRuntimeKind.Vlc
                Return RuntimeInstallPlan.VlcPackageSha256
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return FfmpegSha256
#End If
            Case Else
                Return ""
        End Select
    End Function

    Private Function Sha256OfFile(filePath As String) As String
        Using hasher As Security.Cryptography.SHA256 = Security.Cryptography.SHA256.Create()
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim digest As Byte() = hasher.ComputeHash(stream)
                Dim builder As New Text.StringBuilder(digest.Length * 2)
                For Each b As Byte In digest
                    builder.Append(b.ToString("x2"))
                Next
                Return builder.ToString()
            End Using
        End Using
    End Function

    Private Function GetPackageUrl(kind As OptionalRuntimeKind) As String
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return OcrPackageUrl
            Case OptionalRuntimeKind.Vlc
                Return VlcPackageUrl
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return FfmpegUrl
#End If
            Case Else
                Return ""
        End Select
    End Function

    Private Function GetRuntimeName(kind As OptionalRuntimeKind) As String
#If Not NETFRAMEWORK Then
        ' Named in the user's language: these are quoted in the download dialog and in the
        ' cancelled / failed / not-loading sentences (ticket 028 Ф-3).
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return Localization.T("OCR-движок")
            Case OptionalRuntimeKind.Vlc
                Return Localization.T("Поддержка VLC")
            Case OptionalRuntimeKind.Ffmpeg
                Return "FFmpeg"
        End Select
#End If
        Select Case kind
            Case OptionalRuntimeKind.Ocr
                Return "OCR runtime"
            Case OptionalRuntimeKind.Vlc
                Return "VLC runtime"
#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return "FFmpeg"
#End If
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

#If Not NETFRAMEWORK Then
            Case OptionalRuntimeKind.Ffmpeg
                Return Localization.TF("Для создания видео нужен FFmpeg (около {0} МБ). Он будет загружен с сайта проекта и сохранён в папке программы. FFmpeg - свободная программа под лицензией GPL. Загрузить сейчас?", FfmpegDownloadMb.ToString())
#End If
        End Select

        Return ""
    End Function

    Private Function GetVcPrompt(kind As OptionalRuntimeKind) As String
        Dim feature As String = If(kind = OptionalRuntimeKind.Ocr, "OCR", "VLC")
        Dim arch As String = If(Environment.Is64BitProcess, "x64", "x86")
        Return Localization.TF("Для {0} нужен Microsoft Visual C++ Redistributable ({1}), а на этом компьютере его нет. Это установщик Microsoft, и права администратора он запрашивает сам, поэтому программа его не запускает. Скачать его с сайта Microsoft в браузере? Запустите скачанный файл, затем повторите попытку.", feature, arch)
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

        ' LOAD_WITH_ALTERED_SEARCH_PATH, not a plain LoadLibrary: it makes the loader resolve
        ' THIS dll's own imports out of its directory. Those imports are siblings sitting right
        ' next to it (libvlc.dll -> libvlccore.dll, tesseract50.dll -> leptonica-*.dll), and a
        ' plain LoadLibrary never searches the loaded dll's folder - it found them only because
        ' PrependToPath had put that folder on PATH. A PACKAGED process (MSIX / Microsoft Store)
        ' does not search PATH at all: its order is package graph -> exe folder -> System32. So
        ' in the Store build the probe failed with ERROR_MOD_NOT_FOUND (126) and the runtime was
        ' reported broken while both files sat in the same folder inside the package.
        Dim handle As IntPtr = LoadLibraryEx(fullPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH)
        If handle = IntPtr.Zero Then
            reason = BuildLoadLibraryMessage(fullPath, Marshal.GetLastWin32Error())
            Return False
        End If

        ' Deliberately NOT freed. Keeping the module resident is what lets a later load BY NAME
        ' bind to it (Tesseract's InteropDotNet loader, VLC's plugins) inside a packaged process,
        ' where neither PATH nor the sibling folder is on the search path.
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

    Private Const LOAD_WITH_ALTERED_SEARCH_PATH As UInteger = &H8UI

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function LoadLibraryEx(lpFileName As String, hFile As IntPtr, dwFlags As UInteger) As IntPtr
    End Function

End Module
