#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms
Imports Microsoft.Win32

''' <summary>
''' Builds the ZIP a user sends to the author from Settings -> About
''' (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md). Collects the viewer log, the Share
''' Manager log when it exists, and a generated report.txt with version/OS/settings
''' facts, then packs them into %TEMP%.
'''
''' Modern build only: the About tab exists only in the .NET 10 settings shell, and
''' the x86 fallback takes no new features (CLAUDE.md maintenance policy).
'''
''' Nothing here touches the network. The caller hands the finished file to the
''' user's own mail client - see Table_Form.SendLogs.vb.
''' </summary>
Public Module LogPackage

    ''' <summary>Per-entry cap on the ORIGINAL text; a longer log contributes its tail.</summary>
    Private Const MaxEntryBytes As Long = 2L * 1024L * 1024L

    ''' <summary>Above this the mail will not go through, so the UI offers "open" only.</summary>
    Public Const MaxPackageBytes As Long = 20L * 1024L * 1024L

    Private Const PackagePrefix As String = "fms-logs-"

    Public Structure LogPackageResult
        ''' <summary>Full path of the ZIP, or "" when building failed.</summary>
        Public Path As String
        Public SizeBytes As Long
        ''' <summary>One human-readable line per archive entry, for the consent dialog.</summary>
        Public Entries As List(Of String)
        Public OverSizeLimit As Boolean
        ''' <summary>The failure's type, for a test's message - never shown to the user.</summary>
        Public ErrorText As String
        ''' <summary>What ended the build, for the caller to describe in its own words
        ''' (APP-BEHAVIOUR rule 6) - or Nothing.</summary>
        Public Failure As Exception
    End Structure

    ''' <summary>
    ''' Collects everything and writes the ZIP. Safe to call from a background thread;
    ''' it only reads files and the registry.
    ''' </summary>
    Public Function Build() As LogPackageResult
        Return Build(AddressOf ReadRegistrySettings)
    End Function

    ''' <summary>
    ''' The same, with the settings source injected - the registry in the product, a
    ''' seeded list in the tests that prove a secret never reaches the archive.
    ''' </summary>
    Friend Function Build(settingsSource As Func(Of List(Of KeyValuePair(Of String, Object)))) As LogPackageResult
        Dim result As New LogPackageResult With {.Path = "", .Entries = New List(Of String)()}

        Try
            ' Both a marker in the log ("this is where the user asked") and a flush.
            AppFileLogger.WriteLine("User requested a log package")

            Dim zipPath As String = Path.Combine(Path.GetTempPath(),
                PackagePrefix & AppVersion() & "-" & DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & ".zip")

            Using zipStream As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zipStream, ZipArchiveMode.Create)
                    WriteTextEntry(archive, "report.txt", RedactUserProfile(BuildReport(settingsSource)))
                    result.Entries.Add("report.txt")

                    For Each source As KeyValuePair(Of String, String) In LogSources()
                        Dim line As String = CopyLogEntry(archive, source.Key, source.Value)
                        If line IsNot Nothing Then result.Entries.Add(line)
                    Next
                End Using
            End Using

            Dim info As New FileInfo(zipPath)
            result.Path = zipPath
            result.SizeBytes = info.Length
            result.OverSizeLimit = info.Length > MaxPackageBytes
        Catch ex As Exception
            AppFileLogger.LogException("LogPackage.Build", ex)
            result.Path = ""
            result.ErrorText = ex.GetType().FullName
            result.Failure = ex
        End Try

        Return result
    End Function

    ''' <summary>Deletes leftovers of earlier runs. Quiet by design - a failure here
    ''' must never interrupt the user's actual request.</summary>
    Public Sub SweepOldPackages()
        Try
            Dim cutoff As DateTime = DateTime.Now.AddDays(-7)
            For Each stale As String In Directory.GetFiles(Path.GetTempPath(), PackagePrefix & "*.zip")
                Try
                    If File.GetLastWriteTime(stale) < cutoff Then File.Delete(stale)
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Archive entry name -> source path, for the logs that exist. The running program's own
    ''' log goes under its own file name, then the other program's: this module is linked
    ''' into the Share Manager too (ticket 028 Ф-3), where AppFileLogger is companion.log and
    ''' the viewer's current.log is the sibling to look for.
    ''' </summary>
    Private Function LogSources() As List(Of KeyValuePair(Of String, String))
        Dim sources As New List(Of KeyValuePair(Of String, String))()
        Dim own As String = AppFileLogger.LogFilePath
        If Not String.IsNullOrEmpty(own) Then
            sources.Add(New KeyValuePair(Of String, String)(Path.GetFileName(own), own))
        End If

        For Each sibling As KeyValuePair(Of String, String()) In SiblingLogCandidates()
            If sources.Any(Function(s) String.Equals(s.Key, sibling.Key, StringComparison.OrdinalIgnoreCase)) Then Continue For
            For Each candidate As String In sibling.Value
                If File.Exists(candidate) Then
                    sources.Add(New KeyValuePair(Of String, String)(sibling.Key, candidate))
                    Exit For
                End If
            Next
        Next

        Return sources
    End Function

    ''' <summary>Where each program writes its log: next to the exe, or the per-user fallback.</summary>
    Private Function SiblingLogCandidates() As List(Of KeyValuePair(Of String, String()))
        Dim exeDir As String = If(AppContext.BaseDirectory, "")
        Dim localAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Return New List(Of KeyValuePair(Of String, String())) From {
            New KeyValuePair(Of String, String())("current.log", {
                Path.Combine(exeDir, "current.log"),
                Path.Combine(localAppData, App_name, Second_App_Name, "logs", "current.log")}),
            New KeyValuePair(Of String, String())("companion.log", {
                Path.Combine(exeDir, "companion.log"),
                Path.Combine(localAppData, "FastMediaSorterCompanion", "companion.log")})
        }
    End Function

    ''' <summary>
    ''' Copies one log into the archive, keeping only the tail when it is long.
    '''
    ''' The share mode is the point: AppFileLogger holds current.log open for writing,
    ''' so ZipFileExtensions.CreateEntryFromFile - which opens with FileShare.Read -
    ''' throws IOException on the very file this feature exists for.
    ''' </summary>
    Friend Function CopyLogEntry(archive As ZipArchive, entryName As String, sourcePath As String) As String
        Try
            Using source As New FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite Or FileShare.Delete)
                Dim total As Long = source.Length
                Dim truncated As Boolean = total > MaxEntryBytes
                Dim entry As ZipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal)

                Using target As New StreamWriter(entry.Open(), New UTF8Encoding(False))
                    If truncated Then
                        source.Seek(total - MaxEntryBytes, SeekOrigin.Begin)
                        target.Write("[.. truncated: original " & total.ToString(CultureInfo.InvariantCulture) &
                                     " bytes, keeping the last " & MaxEntryBytes.ToString(CultureInfo.InvariantCulture) & " ..]" &
                                     Environment.NewLine)
                        SkipPartialLine(source)
                    End If
                    ' Line by line, so the user-profile path is replaced before it is
                    ' written (spec 039 phase 1). ReadLine drops the terminator; LF goes
                    ' back, which every log reader accepts.
                    Using reader As New StreamReader(source, New UTF8Encoding(False), False, 81920, True)
                        Dim line As String = reader.ReadLine()
                        While line IsNot Nothing
                            target.Write(RedactUserProfile(line))
                            target.Write(vbLf)
                            line = reader.ReadLine()
                        End While
                    End Using
                End Using

                Return entryName & "  (" & DescribeSize(If(truncated, MaxEntryBytes, total)) & ")"
            End Using
        Catch ex As Exception
            AppFileLogger.LogException("LogPackage.CopyLogEntry " & entryName, ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>Reads to the end of the line the tail-seek landed inside, so the
    ''' archive never starts with half a record.</summary>
    Private Sub SkipPartialLine(source As FileStream)
        Dim value As Integer = source.ReadByte()
        While value >= 0 AndAlso value <> 10
            value = source.ReadByte()
        End While
    End Sub

    Private Sub WriteTextEntry(archive As ZipArchive, entryName As String, text As String)
        Dim entry As ZipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal)
        Using writer As New StreamWriter(entry.Open(), New UTF8Encoding(False))
            writer.Write(text)
        End Using
    End Sub

    ''' <summary>
    ''' The diagnostic sheet. English on purpose - the author reads it, not the user.
    ''' </summary>
    Private Function BuildReport(settingsSource As Func(Of List(Of KeyValuePair(Of String, Object)))) As String
        Dim sb As New StringBuilder()

        sb.AppendLine("Fast Media Sorter for Windows - diagnostic report")
        sb.AppendLine("Generated: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
        sb.AppendLine()

        sb.AppendLine("[application]")
        sb.AppendLine("Running exe: " & CurrentExePath())
        sb.AppendLine("Version: " & AppVersion())
        For Each sibling As String In {"FastMediaSorter_LITE.exe", "FastMediaSorter_x86.exe", "FastMediaSorterCompanion.exe"}
            ' VB is case-insensitive, so a local named "path" would shadow System.IO.Path.
            Dim siblingPath As String = Path.Combine(AppContext.BaseDirectory, sibling)
            sb.AppendLine(sibling & ": " & If(File.Exists(siblingPath), FileVersionOf(siblingPath), "not present"))
        Next
        sb.AppendLine("Packaged (MSIX): " & IsPackaged().ToString())
        sb.AppendLine()

        sb.AppendLine("[system]")
        sb.AppendLine("OS: " & Environment.OSVersion.ToString())
        sb.AppendLine("64-bit OS: " & Environment.Is64BitOperatingSystem.ToString() &
                      "; 64-bit process: " & Environment.Is64BitProcess.ToString())
        sb.AppendLine("Processors: " & Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture))
        sb.AppendLine("Culture: " & CultureInfo.CurrentCulture.Name &
                      "; UI culture: " & CultureInfo.CurrentUICulture.Name)
        sb.AppendLine("UI language: " & Localization.CurrentCode)
        sb.AppendLine("Screen: " & DescribeScreens())
        sb.AppendLine()

        sb.AppendLine("[components]")
        For Each component As KeyValuePair(Of String, String) In ComponentPaths()
            Dim componentPath As String = component.Value
            sb.AppendLine(component.Key & ": " &
                          If(File.Exists(componentPath),
                             "present (" & DescribeSize(New FileInfo(componentPath).Length) & ")",
                             "missing"))
        Next
        sb.AppendLine("tessdata: " & DescribeTessdata())
        sb.AppendLine()

        sb.AppendLine("[settings]")
        Try
            For Each line As String In DescribeSettings(settingsSource())
                sb.AppendLine(line)
            Next
        Catch ex As Exception
            sb.AppendLine("unreadable: " & ex.Message)
        End Try
        sb.AppendLine()

        sb.AppendLine("[recent errors]")
        Dim errors As List(Of String) = RecentErrorLines()
        If errors.Count = 0 Then
            sb.AppendLine("none found in the log")
        Else
            For Each line As String In errors
                sb.AppendLine(line)
            Next
        End If

        Return sb.ToString()
    End Function

    Private Function ComponentPaths() As List(Of KeyValuePair(Of String, String))
        Dim baseDir As String = AppContext.BaseDirectory
        Return New List(Of KeyValuePair(Of String, String))(New KeyValuePair(Of String, String)() {
            New KeyValuePair(Of String, String)("libvlc x64", Path.Combine(baseDir, "libvlc", "win-x64", "libvlc.dll")),
            New KeyValuePair(Of String, String)("libvlc x86", Path.Combine(baseDir, "libvlc", "win-x86", "libvlc.dll")),
            New KeyValuePair(Of String, String)("tesseract x64", Path.Combine(baseDir, "x64", "tesseract50.dll")),
            New KeyValuePair(Of String, String)("share worker", Path.Combine(baseDir, "companion", "fms-share-worker.exe"))
        })
    End Function

    Private Function DescribeTessdata() As String
        Try
            Dim dir As String = Path.Combine(AppContext.BaseDirectory, "tessdata")
            If Not Directory.Exists(dir) Then Return "no bundled folder"
            Dim names As String() = Directory.GetFiles(dir, "*.traineddata").
                Select(Function(f) Path.GetFileNameWithoutExtension(f)).OrderBy(Function(n) n, StringComparer.Ordinal).ToArray()
            If names.Length = 0 Then Return "folder is empty"
            Return String.Join(", ", names)
        Catch ex As Exception
            Return "unreadable: " & ex.Message
        End Try
    End Function

    ''' <summary>
    ''' Settings whose VALUES are safe to send: switches, numbers, modes, window sizes,
    ''' language codes. Nothing on this list can hold a path, a name the user typed, an
    ''' address or a credential. A value that is not here never leaves the machine
    ''' (spec 039 phase 1): the hive also holds user paths and the Share Manager's
    ''' per-share JSON with its PINs, which the name-based secret filter had missed.
    ''' A setting added to the product later is therefore counted, not sent, until it
    ''' is added here on purpose.
    ''' </summary>
    Private ReadOnly SafeSettingNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "AllowNewWindows", "AnimatedPerspective", "AnimatedPerspectiveSpeed", "AppHeight", "AppLeft", "AppTop",
        "AppWidth", "CopyMode", "DynamicPerspective", "ExifAutoRotate", "FirstRun", "HighQualityScaling",
        "IsVideoLoop", "LastCounter", "NoRequestBeforeFileOperation", "PicturePanelHeight", "PicturePanelLeft",
        "PicturePanelTop", "PicturePanelWidth", "Picture_Box_Height_At_Panel", "Picture_Box_Width_At_Panel",
        "RunsCount", "SettingsWindowHeight", "SettingsWindowWidth", "ShowFileDates", "ShowFileSizes",
        "ShowInfoOverlay", "ShowPictureSizes", "ShowRecipientsOverlay", "SlideshowIntervalSec", "SortDir",
        "TableOpened", "UseIndependentThreadForOperationsWithFiles", "UserAlreadyAskedForAssociations",
        "VideoMuted", "VideoVolume", "WheelZooms", "chkTopMost", "color_scheme", "isPerspective",
        "mediaViewedCount", "EditorHeight", "EditorLeft", "EditorTop", "EditorWidth",
        "UiLanguage", "Is_Russian_Language",
        "NameCollisionPolicy", "AfterFileOperation", "AdvanceAfterCopy", "DeleteToRecycleBin", "ConfirmDelete",
        "CreateMissingDestination", "IncludeSubfolders", "IncludedExtensions", "InterfaceScalePercent",
        "NewImageScaleMode", "ReduceMotion", "RecipientsOverlayPosition", "RecipientsOverlayWidth",
        "RecipientsOverlayFontSize", "RecipientsOverlayOpacity", "RecipientsOverlayVisibleRows",
        "SlideshowRandomOrder", "StopSlideshowOnManualNavigation", "SlideshowUiMode", "VideoAutoplay",
        "IncludeVideoInNavigation", "RememberVideoPosition", "VideoControlsHideDelaySec",
        "ShowVideoControlsWhenPaused", "VideoSingleClickAction", "VideoEndAction", "PreferredAudioLanguage",
        "PreferredSubtitleLanguage", "AudioEndAction", "IncludeAudioInNavigation", "IncludeDocumentInNavigation",
        "AudioControlsAlwaysVisible", "AudioVisualiser", "SleepTimerMinutes", "StartupOpenMode",
        "ResumeLastPlayback", "RecentFilesLimit", "RecentFoldersLimit", "OcrDiskCacheMaxMb", "DecodeCacheMaxMb",
        "ConfirmReplaceWithVideo", "ArchiveCacheMaxMb", "ArchiveMaxEntryMb", "ArchiveMaxEntries",
        "SecureIncludeInNavigation", "SecureRememberMinutes", "SecureShowTrueName", "CustomHotkeys",
        "OcrEnabled", "OcrAutoMode", "OcrEngine", "OcrSourceLang", "TranslateProvider", "TranslateTargetLang",
        "OverlayVisible", "OverlayOpacity", "OcrDiskCache", "OllamaModel", "OcrModelQuality", "OcrPageMode",
        "Share_AutostartEnabled", "Share_AutostartOffered", "Share_ExpandedSections", "Share_ExternalAccessIntent",
        "Share_LanOnlyExport", "Share_ListenPort", "Share_MaxConnections", "Share_OpenWindowOnStartup",
        "Share_ServerFeaturesEnabled", "Share_WindowHeight", "Share_WindowMaximized", "Share_WindowWidth",
        "Share_WindowX", "Share_WindowY", "Share_WorkerEverStarted", "AppWindowState"
    }

    ''' <summary>"|"-joined path lists: the author learns how many, never which.</summary>
    Private ReadOnly CountedListSettingNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "RecentFolders", "RecentMediaFiles"
    }

    ''' <summary>Emitted in no form at all - not the value, not "hidden", not the name.
    ''' The Share Manager's per-share JSON carries the PINs and the shared host paths.</summary>
    Private ReadOnly NeverEmittedSettingNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "Share_RootParams"
    }

    Private Function ReadRegistrySettings() As List(Of KeyValuePair(Of String, Object))
        Dim values As New List(Of KeyValuePair(Of String, Object))()
        Dim keyPath As String = "Software\VB and VBA Program Settings\" & App_name & "\" & Second_App_Name
        Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(keyPath, False)
            If key Is Nothing Then Return values
            For Each name As String In key.GetValueNames()
                values.Add(New KeyValuePair(Of String, Object)(name, key.GetValue(name)))
            Next
        End Using
        Return values
    End Function

    ''' <summary>
    ''' The [settings] lines of the report. Allow-listed values are written as they are;
    ''' a secret shows only its name; path lists become a count; a recipient slot says
    ''' only whether it is set; everything else is summed into one "not sent" line.
    ''' Pure, so the tests can seed it.
    ''' </summary>
    Friend Function DescribeSettings(values As IEnumerable(Of KeyValuePair(Of String, Object))) As List(Of String)
        Dim lines As New List(Of String)()
        Dim withheld As Integer = 0
        Dim any As Boolean = False

        If values IsNot Nothing Then
            For Each pair As KeyValuePair(Of String, Object) In values.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
                any = True
                Dim name As String = If(pair.Key, "")
                If NeverEmittedSettingNames.Contains(name) Then Continue For

                Dim text As String = If(Convert.ToString(pair.Value, CultureInfo.InvariantCulture), "")
                If IsSecretSetting(name) Then
                    lines.Add(name & "=<hidden>")
                ElseIf SafeSettingNames.Contains(name) Then
                    lines.Add(name & "=" & text)
                ElseIf CountedListSettingNames.Contains(name) Then
                    Dim count As Integer = text.Split({"|"c}, StringSplitOptions.RemoveEmptyEntries).Length
                    lines.Add(name & "=<" & count.ToString(CultureInfo.InvariantCulture) & " entries, not sent>")
                ElseIf IsRecipientSlot(name) Then
                    lines.Add(name & "=" & If(text.Length = 0, "<empty>", "<set, path not sent>"))
                Else
                    withheld += 1
                End If
            Next
        End If

        If Not any Then
            lines.Add("no saved profile")
        ElseIf withheld > 0 Then
            lines.Add("(" & withheld.ToString(CultureInfo.InvariantCulture) & " other values not sent)")
        End If
        Return lines
    End Function

    ''' <summary>MoveOn, MoveOn0 .. MoveOn10: the recipient folders of the sort hotkeys.</summary>
    Private Function IsRecipientSlot(name As String) As Boolean
        If Not name.StartsWith("MoveOn", StringComparison.OrdinalIgnoreCase) Then Return False
        Return name.Substring(6).All(Function(c) Char.IsDigit(c))
    End Function

    ''' <summary>
    ''' Replaces the user-profile folder with &lt;USER&gt;, so the account name does not
    ''' leave the machine with the report and the log tails. Case-insensitive, like the
    ''' file system; both slash directions.
    ''' </summary>
    Friend Function RedactUserProfile(text As String) As String
        Return RedactUserProfile(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    End Function

    Friend Function RedactUserProfile(text As String, profilePath As String) As String
        If String.IsNullOrEmpty(text) OrElse String.IsNullOrEmpty(profilePath) Then Return text
        Dim profile As String = profilePath.TrimEnd("\"c, "/"c)
        ' A bare drive root would redact half the text and hide nothing.
        If profile.Length < 4 Then Return text
        Dim redacted As String = ReplaceIgnoreCase(text, profile, "<USER>")
        Return ReplaceIgnoreCase(redacted, profile.Replace("\"c, "/"c), "<USER>")
    End Function

    Private Function ReplaceIgnoreCase(text As String, find As String, replacement As String) As String
        Dim index As Integer = text.IndexOf(find, StringComparison.OrdinalIgnoreCase)
        If index < 0 Then Return text
        Dim sb As New StringBuilder(text.Length)
        Dim start As Integer = 0
        While index >= 0
            sb.Append(text, start, index - start)
            sb.Append(replacement)
            start = index + find.Length
            index = text.IndexOf(find, start, StringComparison.OrdinalIgnoreCase)
        End While
        sb.Append(text, start, text.Length - start)
        Return sb.ToString()
    End Function

    Friend Function IsSecretSetting(name As String) As Boolean
        If name Is Nothing Then Return False
        Return name.IndexOf("ApiKey", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               name.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               name.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>The last few exception lines of the log - a shortcut so the author does
    ''' not have to scan a two-megabyte tail to see what broke.</summary>
    Private Function RecentErrorLines() As List(Of String)
        Dim found As New List(Of String)()
        Try
            Dim logPath As String = AppFileLogger.LogFilePath
            If String.IsNullOrEmpty(logPath) OrElse Not File.Exists(logPath) Then Return found

            Using stream As New FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                If stream.Length > MaxEntryBytes Then stream.Seek(stream.Length - MaxEntryBytes, SeekOrigin.Begin)
                Using reader As New StreamReader(stream)
                    Dim line As String = reader.ReadLine()
                    While line IsNot Nothing
                        If line.IndexOf("xception", StringComparison.Ordinal) >= 0 OrElse
                           line.IndexOf("Unhandled", StringComparison.Ordinal) >= 0 Then
                            found.Add(line)
                            If found.Count > 200 Then found.RemoveAt(0)
                        End If
                        line = reader.ReadLine()
                    End While
                End Using
            End Using
        Catch ex As Exception
            found.Add("unreadable: " & ex.Message)
        End Try

        If found.Count > 20 Then found.RemoveRange(0, found.Count - 20)
        Return found
    End Function

    Private Function DescribeScreens() As String
        Try
            Dim parts As New List(Of String)()
            For Each screen As Screen In Screen.AllScreens
                parts.Add(screen.Bounds.Width.ToString(CultureInfo.InvariantCulture) & "x" &
                          screen.Bounds.Height.ToString(CultureInfo.InvariantCulture) &
                          If(screen.Primary, " (primary)", ""))
            Next
            Return String.Join("; ", parts)
        Catch
            Return "unknown"
        End Try
    End Function

    Private Function CurrentExePath() As String
        Try
            ' Not Assembly.Location: it is "" in a single-file publish.
            Return Process.GetCurrentProcess().MainModule.FileName
        Catch
            Return AppContext.BaseDirectory
        End Try
    End Function

    Private Function AppVersion() As String
        Try
            Return Application.ProductVersion
        Catch
            Return "unknown"
        End Try
    End Function

    Private Function FileVersionOf(filePath As String) As String
        Try
            Return FileVersionInfo.GetVersionInfo(filePath).FileVersion
        Catch
            Return "present, version unreadable"
        End Try
    End Function

    Private Function IsPackaged() As Boolean
        Try
            ' An MSIX install lives under WindowsApps; good enough as a fact for a
            ' report, and it needs no packaging API reference.
            Return AppContext.BaseDirectory.IndexOf("\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Human-readable size for the consent dialog and the report.</summary>
    Public Function DescribeSize(bytes As Long) As String
        If bytes < 1024L Then Return bytes.ToString(CultureInfo.InvariantCulture) & " B"
        If bytes < 1024L * 1024L Then Return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) & " KB"
        Return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) & " MB"
    End Function

End Module
#End If
