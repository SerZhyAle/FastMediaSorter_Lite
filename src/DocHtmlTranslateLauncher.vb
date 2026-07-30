Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.Win32

''' <summary>
''' Locates and launches the SZA companion "doc-html-translate.exe" (from the
''' separate EPUB_2_HTML product) to open the current image in the browser with a
''' free Google page-translation - an alternative, "out to the browser" route next
''' to our own in-app OCR overlay (see SPECIFICATION_DOC_HTML_TRANSLATE_INTEGRATION.md).
'''
''' Modelled on <see cref="OllamaManager"/>: find the exe, check availability, run
''' it. The launch contract (positional image path, -ocr-lang / -folder flags, exit
''' codes) is FROZEN by the companion and lives in
''' P:\WINDOWS\EPUB_2_HTML\docs\integration-image-translate.md - never change it here.
''' </summary>
Public Module DocHtmlTranslate

    Public Const ExeFileName As String = "doc-html-translate.exe"
    Public Const WingetId As String = "SerZhyAle.DocHtmlTranslate"
    Public Const StorePage As String = "https://apps.microsoft.com/detail/9PMHSWQPR6V1"

    ' Cached availability for the UI thread (recomputed off-thread) - FindExe()
    ' touches the filesystem/registry and must not run on every navigation.
    Private ReadOnly sync As New Object()
    Private _available As Boolean = False
    Private _cachedExe As String = ""

    ''' <summary>Full path to doc-html-translate.exe, or "" if not found. May touch
    ''' the filesystem/registry - call off the UI thread or via the cache.</summary>
    Public Function FindExe() As String
        ' 1. Our own bundle next to the app (absolute path, most robust). Reserved:
        '    we don't bundle it today, but honour it if a copy is ever placed there.
        Dim appDir As String = AppDirectory()
        If appDir.Length > 0 Then
            Dim bundled As String = CombineSafe(appDir, "doc-html-translate\" & ExeFileName)
            If bundled.Length > 0 AndAlso File.Exists(bundled) Then Return bundled
        End If

        ' 2. winget/portable alias dir (also on PATH, but the absolute path here is
        '    seen even by a process started before the install finished).
        Dim links As String = CombineSafe(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Microsoft\WinGet\Links\" & ExeFileName)
        If links.Length > 0 AndAlso File.Exists(links) Then Return links

        ' 3. Scan PATH (equivalent of "where doc-html-translate").
        Dim pathVar As String = Environment.GetEnvironmentVariable("PATH")
        If Not String.IsNullOrEmpty(pathVar) Then
            For Each dir As String In pathVar.Split(";"c)
                Dim d As String = dir.Trim()
                If d.Length = 0 Then Continue For
                Dim candidate As String = CombineSafe(d, ExeFileName)
                If candidate.Length > 0 AndAlso File.Exists(candidate) Then Return candidate
            Next
        End If

        ' 4. HKCU Applications command (written once the GUI ran) - extra hook, since
        '    doc-html-translate has no App Paths / uninstall key.
        Dim fromReg As String = FindExeInRegistry()
        If fromReg.Length > 0 AndAlso File.Exists(fromReg) Then Return fromReg

        Return ""
    End Function

    Private Function FindExeInRegistry() As String
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(
                "Software\Classes\Applications\" & ExeFileName & "\shell\open\command")
                If key Is Nothing Then Return ""
                Dim raw As String = TryCast(key.GetValue(Nothing), String)
                Return ExtractExePath(raw)
            End Using
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Pulls the exe path out of a shell "open" command string, e.g.
    ''' "C:\..\doc-html-translate.exe" "%1" -&gt; C:\..\doc-html-translate.exe.</summary>
    Private Function ExtractExePath(command As String) As String
        If String.IsNullOrWhiteSpace(command) Then Return ""
        Dim s As String = command.Trim()
        If s.StartsWith("""") Then
            Dim close As Integer = s.IndexOf(""""c, 1)
            If close > 1 Then Return s.Substring(1, close - 1)
            Return ""
        End If
        Dim sp As Integer = s.IndexOf(" "c)
        Return If(sp > 0, s.Substring(0, sp), s)
    End Function

    Public Function IsInstalled() As Boolean
        Return FindExe().Length > 0
    End Function

    ''' <summary>Cheap cached flag for the UI thread (see RefreshAvailabilityAsync).</summary>
    Public Function IsAvailableCached() As Boolean
        SyncLock sync
            Return _available
        End SyncLock
    End Function

    ''' <summary>Recomputes availability off the UI thread and updates the cache.
    ''' Optionally invokes <paramref name="onDone"/> (raw = whether it is available)
    ''' back on a thread-pool thread when finished.</summary>
    Public Sub RefreshAvailabilityAsync(Optional onDone As Action(Of Boolean) = Nothing)
        Task.Run(Sub()
                     Dim exe As String = ""
                     Try
                         exe = FindExe()
                     Catch
                     End Try
                     SyncLock sync
                         _cachedExe = exe
                         _available = exe.Length > 0
                     End SyncLock
                     If onDone IsNot Nothing Then
                         Try : onDone(exe.Length > 0) : Catch : End Try
                     End If
                 End Sub)
    End Sub

    ''' <summary>
    ''' Launches doc-html-translate.exe on <paramref name="imagePath"/> non-blocking.
    ''' Returns True once the process is started (immediate launch failures set
    ''' <paramref name="errMsg"/> and return False). A non-zero exit is reported
    ''' asynchronously via <paramref name="onExitError"/> (raised on a background
    ''' thread - the caller must marshal to the UI).
    ''' </summary>
    Public Function TranslateImage(imagePath As String, ocrLang As String, force As Boolean,
                                   ByRef errMsg As String, Optional onExitError As Action(Of Integer) = Nothing) As Boolean
        errMsg = ""
        If String.IsNullOrWhiteSpace(imagePath) OrElse Not File.Exists(imagePath) Then
            errMsg = "no input file"
            Return False
        End If

        Dim exe As String
        SyncLock sync
            exe = _cachedExe
        End SyncLock
        If exe.Length = 0 OrElse Not File.Exists(exe) Then exe = FindExe()
        If exe.Length = 0 Then
            errMsg = "doc-html-translate not found"
            Return False
        End If

        Dim cacheDir As String
        Try
            cacheDir = OutputCacheDir(imagePath)
            Directory.CreateDirectory(cacheDir)
        Catch ex As Exception
            errMsg = ex.Message
            Return False
        End Try

        Dim args As New StringBuilder()
        Dim lang As String = If(ocrLang, "").Trim()
        If lang.Length > 0 Then args.Append("-ocr-lang ").Append(lang).Append(" ")
        If force Then args.Append("-force ")
        args.Append("-folder ").Append(Quote(cacheDir)).Append(" ")
        args.Append(Quote(imagePath))

        Try
            Dim psi As New ProcessStartInfo(exe, args.ToString()) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            }
            Dim proc As New Process With {.StartInfo = psi, .EnableRaisingEvents = True}
            AddHandler proc.Exited, Sub()
                                        Dim code As Integer = 0
                                        Try : code = proc.ExitCode : Catch : End Try
                                        Try : proc.Dispose() : Catch : End Try
                                        If code <> 0 AndAlso onExitError IsNot Nothing Then
                                            Try : onExitError(code) : Catch : End Try
                                        End If
                                    End Sub
            proc.Start()
            Return True
        Catch ex As Exception
            errMsg = ex.Message
            Return False
        End Try
    End Function

    ''' <summary>Runs the winget install for the companion (non-blocking). Callers
    ''' should refresh availability afterwards - but note an already-running process
    ''' won't see the new PATH; FindExe re-checks the WinGet\Links absolute path.</summary>
    Public Sub StartWingetInstall()
        Try
            Dim args As String = "install --id " & WingetId &
                " -e --source winget --accept-package-agreements --accept-source-agreements"
            Process.Start(New ProcessStartInfo("winget", args) With {.UseShellExecute = True})
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " doc-html-translate: winget install failed: " & ex.Message)
        End Try
    End Sub

    Public Sub OpenStorePage()
        Try
            Process.Start(New ProcessStartInfo(StorePage) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

    ''' <summary>Size budget for the browser-translate cache. Each entry is a whole folder
    ''' (an OCR'd HTML page plus its assets), and because the folder name folds in the source
    ''' file's write time, re-translating an edited image orphans the previous folder outright.
    ''' Nothing ever pruned this root - unlike LogPackage, which cleans up after itself.</summary>
    Private Const Browser_Cache_Budget_Mb As Integer = 500

    ''' <summary>The cache root - the folder whose subdirectories are the entries.</summary>
    Private Function OutputCacheRoot() As String
        Return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            App_name, Second_App_Name, "browser-translate-cache")
    End Function

    ''' <summary>%LOCALAPPDATA%\SZA\FastMediaSorter\browser-translate-cache\&lt;hash&gt;.
    ''' The hash folds in the file's last-write time, so an edited file re-OCRs into
    ''' a fresh folder instead of reusing a stale page.</summary>
    Private Function OutputCacheDir(imagePath As String) As String
        Dim ticks As Long = 0
        Try : ticks = File.GetLastWriteTimeUtc(imagePath).Ticks : Catch : End Try
        Dim hash As String = StableHash(imagePath.ToLowerInvariant() & "|" & ticks.ToString())
        Dim root As String = OutputCacheRoot()

        ' Trim BEFORE handing out a new entry: the oldest folders go, and the one we are about
        ' to create is not a candidate because it does not exist yet.
        Dim removed As Integer = DiskCacheTrim.TrimToBudget(root, Browser_Cache_Budget_Mb,
                                                           entries_Are_Folders:=True)
        If removed > 0 Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") &
                            " doc-html-translate: trimmed " & removed.ToString() & " old cache folders")
        End If

        Return Path.Combine(root, hash)
    End Function

    Private Function StableHash(text As String) As String
        Using sha As SHA1 = SHA1.Create()
            Dim bytes() As Byte = sha.ComputeHash(Encoding.UTF8.GetBytes(text))
            Dim sb As New StringBuilder(bytes.Length * 2)
            For Each b As Byte In bytes
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString().Substring(0, 20)
        End Using
    End Function

    Private Function AppDirectory() As String
        Try
            ' AppContext.BaseDirectory, not Assembly.Location: identical on net48
            ' and still correct in the .NET 10 single-file publish.
            Return AppContext.BaseDirectory
        Catch
            Return ""
        End Try
    End Function

    Private Function Quote(value As String) As String
        Return """" & If(value, "") & """"
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
