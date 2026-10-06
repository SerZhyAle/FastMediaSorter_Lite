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
        Return Build(AddressOf ReadRegistrySettings, Nothing, Nothing, Nothing)
    End Function

    ''' <summary>
    ''' The same, with the settings source injected - the registry in the product, a
    ''' seeded list in the tests that prove a secret never reaches the archive.
    ''' </summary>
    Friend Function Build(settingsSource As Func(Of List(Of KeyValuePair(Of String, Object)))) As LogPackageResult
        Return Build(settingsSource, Nothing, Nothing, Nothing)
    End Function

    ''' <summary>
    ''' Full builder with injectable clock, destination directory, and report generator for deterministic tests.
    ''' Atomically reserves the unique package with FileMode.CreateNew and deletes on failure only the file
    ''' reserved by this invocation.
    ''' </summary>
    Friend Function Build(settingsSource As Func(Of List(Of KeyValuePair(Of String, Object))),
                          now As DateTime?,
                          destinationDirectory As String,
                          Optional reportGenerator As Func(Of Func(Of List(Of KeyValuePair(Of String, Object))), String) = Nothing) As LogPackageResult
        Dim result As New LogPackageResult With {.Path = "", .Entries = New List(Of String)()}
        Dim reservedPath As String = Nothing

        Try
            ' Both a marker in the log ("this is where the user asked") and a flush.
            AppFileLogger.WriteLine("User requested a log package")

            Dim destDir As String = If(String.IsNullOrEmpty(destinationDirectory), Path.GetTempPath(), destinationDirectory)
            If Not Directory.Exists(destDir) Then
                Directory.CreateDirectory(destDir)
            End If

            Dim currentTime As DateTime = If(now.HasValue, now.Value, DateTime.Now)
            Dim timeStampString As String = currentTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            Dim version As String = AppVersion()
            Dim effectiveSettingsSource As Func(Of List(Of KeyValuePair(Of String, Object))) = If(settingsSource, AddressOf ReadRegistrySettings)

            Dim zipStream As FileStream = Nothing
            For attempt As Integer = 1 To 100
                Dim token As String = Guid.NewGuid().ToString("N").Substring(0, 8)
                Dim candidate As String = Path.Combine(destDir, PackagePrefix & version & "-" & timeStampString & "-" & token & ".zip")
                Try
                    zipStream = New FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    reservedPath = candidate
                    Exit For
                Catch ex As IOException When File.Exists(candidate)
                    ' Collision with existing file - retry with another token
                End Try
            Next

            If zipStream Is Nothing Then
                Throw New IOException("Failed to reserve a unique log package file in " & destDir)
            End If

            Using zipStream
                Using archive As New ZipArchive(zipStream, ZipArchiveMode.Create)
                    Dim reportText As String = If(reportGenerator IsNot Nothing,
                                                  reportGenerator(effectiveSettingsSource),
                                                  BuildReport(effectiveSettingsSource))

                    WriteTextEntry(archive, "report.txt", RedactUserProfile(reportText))
                    result.Entries.Add("report.txt")

                    For Each source As KeyValuePair(Of String, String) In LogSources()
                        Dim line As String = CopyLogEntry(archive, source.Key, source.Value)
                        If line IsNot Nothing Then result.Entries.Add(line)
                    Next
                End Using
            End Using

            Dim info As New FileInfo(reservedPath)
            result.Path = reservedPath
            result.SizeBytes = info.Length
            result.OverSizeLimit = info.Length > MaxPackageBytes
        Catch ex As Exception
            AppFileLogger.LogException("LogPackage.Build", ex)
            If Not String.IsNullOrEmpty(reservedPath) Then
                Try
                    If File.Exists(reservedPath) Then File.Delete(reservedPath)
                Catch delEx As Exception
                    AppFileLogger.LogException("LogPackage.Build cleanup", delEx)
                End Try
            End If
            result.Path = ""
            result.ErrorText = ex.GetType().FullName
            result.Failure = ex
        End Try

        Return result
    End Function

    ''' <summary>Deletes leftovers of earlier runs. Quiet by design - a failure here
    ''' must never interrupt the user's actual request.</summary>
    Public Sub SweepOldPackages()
        SweepOldPackages(Nothing, Nothing)
    End Sub

    Friend Sub SweepOldPackages(targetDirectory As String, now As DateTime?)
        Try
            Dim dir As String = If(String.IsNullOrEmpty(targetDirectory), Path.GetTempPath(), targetDirectory)
            If Not Directory.Exists(dir) Then Return

            Dim currentTime As DateTime = If(now.HasValue, now.Value, DateTime.Now)
            Dim cutoff As DateTime = currentTime.AddDays(-7)

            For Each stale As String In Directory.GetFiles(dir, PackagePrefix & "*.zip")
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
    Friend Function SiblingLogCandidates() As List(Of KeyValuePair(Of String, String()))
        Dim exeDir As String = If(AppContext.BaseDirectory, "")
        Dim localAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Dim commonAppData As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        Return New List(Of KeyValuePair(Of String, String())) From {
            New KeyValuePair(Of String, String())("current.log", {
                Path.Combine(exeDir, "current.log"),
                Path.Combine(localAppData, App_name, Second_App_Name, "logs", "current.log")}),
            New KeyValuePair(Of String, String())("companion.log", {
                Path.Combine(exeDir, "companion.log"),
                Path.Combine(localAppData, "FastMediaSorterCompanion", "companion.log")}),
            New KeyValuePair(Of String, String())("exchange.log", ExchangeLogCandidates(commonAppData, localAppData))
        }
    End Function

    ''' <summary>
    ''' Where the exchange server writes exchange.log (ticket 141 T-8): the machine store the service uses
    ''' first - it wins whenever it exists, exactly as the server resolves its own data directory - then
    ''' the per-user store of a console run. The server's log names sessions by hash only, so it is
    ''' redacted by construction; the tail cap and the user-profile replacement apply on top, like for
    ''' every other log. A data-directory override (the server's own FMS_EXCHANGE_DATA_DIR) comes first.
    ''' </summary>
    Friend Function ExchangeLogCandidates(commonAppData As String, localAppData As String) As String()
        Dim list As New List(Of String)()
        Dim overrideDir As String = Environment.GetEnvironmentVariable("FMS_EXCHANGE_DATA_DIR")
        If Not String.IsNullOrWhiteSpace(overrideDir) Then list.Add(Path.Combine(overrideDir, "exchange.log"))
        If Not String.IsNullOrEmpty(commonAppData) Then list.Add(Path.Combine(commonAppData, "FastMediaSorterExchange", "exchange.log"))
        If Not String.IsNullOrEmpty(localAppData) Then list.Add(Path.Combine(localAppData, "FastMediaSorterExchange", "exchange.log"))
        Return list.ToArray()
    End Function

    ''' <summary>What a bounded read of a live log produced: the bytes themselves, so every
    ''' later step works on a snapshot that can no longer grow.</summary>
    Friend Structure TailSnapshot
        ''' <summary>The source length captured ONCE, at open.</summary>
        Public SourceLength As Long
        ''' <summary>The original bytes kept (never more than the cap), partial first record
        ''' and an incomplete trailing UTF-8 sequence already removed.</summary>
        Public Bytes As Byte()
        ''' <summary>True when the source was longer than the cap and only its tail is kept.</summary>
        Public Truncated As Boolean
    End Structure

    ''' <summary>
    ''' Reads at most <paramref name="cap"/> ORIGINAL bytes from the end of <paramref name="source"/>
    ''' (057.S15-4). The length is captured once; the read loop is bounded by it, so a file that is
    ''' appended to on every read, truncated or replaced underneath us still ends in a short read
    ''' rather than a long one. Bytes discarded to land on a record boundary were read, so they
    ''' count against the cap - the budget is the number of bytes that leave the file.
    ''' </summary>
    Friend Function ReadTail(source As Stream, cap As Long) As TailSnapshot
        Dim snapshot As New TailSnapshot With {.Bytes = New Byte() {}}
        Dim total As Long = source.Length
        snapshot.SourceLength = total
        snapshot.Truncated = total > cap
        Dim budget As Long = If(snapshot.Truncated, cap, total)
        If snapshot.Truncated Then source.Seek(total - cap, SeekOrigin.Begin)

        Dim collected As New MemoryStream(CInt(Math.Min(budget, 1024L * 1024L)))
        Using bounded As New BoundedReadStream(source, budget)
            Dim chunk(81919) As Byte
            Dim count As Integer = bounded.Read(chunk, 0, chunk.Length)
            While count > 0
                collected.Write(chunk, 0, count)
                count = bounded.Read(chunk, 0, chunk.Length)
            End While
        End Using

        Dim data As Byte() = collected.ToArray()
        Dim from As Integer = 0
        If snapshot.Truncated Then
            ' The tail-seek landed inside a record: start at the next line.
            Dim lf As Integer = Array.IndexOf(data, CByte(10))
            from = If(lf < 0, data.Length, lf + 1)
        End If
        Dim until As Integer = data.Length - IncompleteUtf8TailLength(data, from)
        If until < from Then until = from
        Dim kept(until - from - 1) As Byte
        If kept.Length > 0 Then Array.Copy(data, from, kept, 0, kept.Length)
        snapshot.Bytes = kept
        Return snapshot
    End Function

    ''' <summary>How many bytes at the end of <paramref name="data"/> are the start of a
    ''' multi-byte UTF-8 sequence that the cap cut short (0 when the end is clean).</summary>
    Friend Function IncompleteUtf8TailLength(data As Byte(), lowestIndex As Integer) As Integer
        Dim n As Integer = data.Length
        For back As Integer = 1 To Math.Min(3, n - lowestIndex)
            Dim b As Byte = data(n - back)
            If (b And &HC0) = &H80 Then Continue For ' a continuation byte: keep looking for its lead
            Dim needed As Integer = If((b And &HE0) = &HC0, 2, If((b And &HF0) = &HE0, 3, If((b And &HF8) = &HF0, 4, 1)))
            Return If(needed > back, back, 0)
        Next
        Return 0
    End Function

    ''' <summary>
    ''' Copies one log into the archive, keeping only the tail when it is long.
    '''
    ''' The share mode is the point: AppFileLogger holds current.log open for writing,
    ''' so ZipFileExtensions.CreateEntryFromFile - which opens with FileShare.Read -
    ''' throws IOException on the very file this feature exists for.
    ''' </summary>
    Friend Function CopyLogEntry(archive As ZipArchive, entryName As String, sourcePath As String,
                                 Optional openSource As Func(Of String, Stream) = Nothing) As String
        Try
            Dim opener As Func(Of String, Stream) = If(openSource, AddressOf OpenLiveLog)
            Dim snapshot As TailSnapshot
            Using source As Stream = opener(sourcePath)
                snapshot = ReadTail(source, MaxEntryBytes)
            End Using

            Dim entry As ZipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal)
            Using target As New StreamWriter(entry.Open(), New UTF8Encoding(False))
                If snapshot.Truncated Then
                    target.Write("[.. truncated: original " & snapshot.SourceLength.ToString(CultureInfo.InvariantCulture) &
                                 " bytes, keeping the last " & MaxEntryBytes.ToString(CultureInfo.InvariantCulture) & " ..]" &
                                 Environment.NewLine)
                End If
                ' Line by line, so the user-profile path is replaced before it is
                ' written (spec 039 phase 1). ReadLine drops the terminator; LF goes
                ' back, which every log reader accepts.
                Using reader As New StreamReader(New MemoryStream(snapshot.Bytes), New UTF8Encoding(False), False)
                    Dim line As String = reader.ReadLine()
                    While line IsNot Nothing
                        target.Write(RedactUserProfile(line))
                        target.Write(vbLf)
                        line = reader.ReadLine()
                    End While
                End Using
            End Using

            Return entryName & "  (" & DescribeSize(snapshot.Bytes.Length) & ")"
        Catch ex As Exception
            AppFileLogger.LogException("LogPackage.CopyLogEntry " & entryName, ex)
            Return Nothing
        End Try
    End Function

    Private Function OpenLiveLog(path As String) As Stream
        Return New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
    End Function
    ''' <summary>
    ''' A read-only stream wrapper that bounds reading to a fixed number of bytes from the current position,
    ''' preventing tail-readers from reading to a growing live EOF (057.S15-4).
    ''' </summary>
    Friend NotInheritable Class BoundedReadStream
        Inherits Stream

        Private ReadOnly inner As Stream
        Private remainingBytes As Long

        Public Sub New(inner As Stream, maxBytesToRead As Long)
            Me.inner = inner
            Me.remainingBytes = Math.Max(0L, maxBytesToRead)
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return inner.CanRead
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property Length As Long
            Get
                Return remainingBytes
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            If remainingBytes <= 0 Then Return 0
            Dim toRead As Integer = CInt(Math.Min(CLng(count), remainingBytes))
            Dim readCount As Integer = inner.Read(buffer, offset, toRead)
            If readCount > 0 Then
                remainingBytes -= readCount
            Else
                remainingBytes = 0
            End If
            Return readCount
        End Function

        Public Overrides Sub Flush()
        End Sub

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Throw New NotSupportedException()
        End Sub
    End Class

    Private Sub WriteTextEntry(archive As ZipArchive, entryName As String, text As String)
        Dim entry As ZipArchiveEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal)
        Using writer As New StreamWriter(entry.Open(), New UTF8Encoding(False))
            writer.Write(text)
        End Using
    End Sub

    ''' <summary>
    ''' The diagnostic sheet. English on purpose - the author reads it, not the user.
    ''' </summary>
    Friend Function BuildReport(settingsSource As Func(Of List(Of KeyValuePair(Of String, Object))), Optional dumpDirectory As String = Nothing) As String
        Dim sb As New StringBuilder()

        sb.AppendLine(ProductNames.Full & " - diagnostic report")
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

        sb.AppendLine("[crash dumps - names and sizes only; contents excluded]")
        sb.AppendLine(DescribeCrashDumps(If(dumpDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"))))
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

    Friend Function DescribeCrashDumps(dumpDirectory As String) As String
        Try
            If Not Directory.Exists(dumpDirectory) Then Return "none found"
            Dim lines As New List(Of String)
            For Each dump In New DirectoryInfo(dumpDirectory).EnumerateFiles("FastMediaSorter*.dmp").OrderBy(Function(item) item.Name, StringComparer.OrdinalIgnoreCase)
                ' Metadata only: never open a dump, copy it or add it as a ZIP entry.
                lines.Add(dump.Name & ": " & dump.Length.ToString(CultureInfo.InvariantCulture) & " bytes")
            Next
            Return If(lines.Count = 0, "none found", String.Join(Environment.NewLine, lines))
        Catch
            Return "could not list dump metadata"
        End Try
    End Function

    Friend Function ComponentPaths() As List(Of KeyValuePair(Of String, String))
        Dim baseDir As String = AppContext.BaseDirectory
        Return New List(Of KeyValuePair(Of String, String))(New KeyValuePair(Of String, String)() {
            New KeyValuePair(Of String, String)("libvlc x64", Path.Combine(baseDir, "libvlc", "win-x64", "libvlc.dll")),
            New KeyValuePair(Of String, String)("libvlc x86", Path.Combine(baseDir, "libvlc", "win-x86", "libvlc.dll")),
            New KeyValuePair(Of String, String)("tesseract x64", Path.Combine(baseDir, "x64", "tesseract50.dll")),
            New KeyValuePair(Of String, String)("share worker", Path.Combine(baseDir, "companion", "fms-share-worker.exe")),
            New KeyValuePair(Of String, String)("exchange server", Path.Combine(baseDir, "exchange", "fms-exchange-server.exe"))
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
    Friend ReadOnly SafeSettingNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
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
        "NewImageScaleMode", "ReduceMotion", "ModernChrome", "ChromePinned", "ChromeSize", "ChromeRow1Pinned", "ChromeRow2Pinned", "ChromeStatusPinned", "ChromeOpacityPercent", "ChromeCornerRadius", "RecipientsOverlayPosition", "RecipientsOverlayWidth",
        "RecipientsOverlayFontSize", "RecipientsOverlayOpacity", "RecipientsOverlayVisibleRows", "RecipientsOverlayPinned",
        "SlideshowRandomOrder", "StopSlideshowOnManualNavigation", "SlideshowUiMode", "VideoAutoplay",
        "IncludeVideoInNavigation", "RememberVideoPosition", "VideoControlsHideDelaySec",
        "SeekShortSec", "SeekMediumSec", "SeekLongSec", "HideCursorAfterSec",
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
        "Share_WindowX", "Share_WindowY", "Share_WorkerEverStarted", "AppWindowState",
        "Exchange_ListenPort", "Exchange_KeepRunningOnExit", "Exchange_EverStarted",
        "Share_ExchangeMode"
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
    Friend Function RecentErrorLines(Optional openSource As Func(Of String, Stream) = Nothing, Optional logPathOverride As String = Nothing) As List(Of String)
        Dim found As New List(Of String)()
        Try
            Dim logPath As String = If(logPathOverride, AppFileLogger.LogFilePath)
            If String.IsNullOrEmpty(logPath) OrElse Not File.Exists(logPath) Then Return found

            Dim snapshot As TailSnapshot
            Using stream As Stream = If(openSource, AddressOf OpenLiveLog)(logPath)
                snapshot = ReadTail(stream, MaxEntryBytes)
            End Using
            Using reader As New StreamReader(New MemoryStream(snapshot.Bytes), New UTF8Encoding(False), False)
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
            Using current As Process = Process.GetCurrentProcess()
                Return current.MainModule.FileName
            End Using
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
