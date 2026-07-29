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
        Public ErrorText As String
    End Structure

    ''' <summary>
    ''' Collects everything and writes the ZIP. Safe to call from a background thread;
    ''' it only reads files and the registry.
    ''' </summary>
    Public Function Build() As LogPackageResult
        Dim result As New LogPackageResult With {.Path = "", .Entries = New List(Of String)()}

        Try
            ' Both a marker in the log ("this is where the user asked") and a flush.
            AppFileLogger.WriteLine("User requested a log package")

            Dim zipPath As String = Path.Combine(Path.GetTempPath(),
                PackagePrefix & AppVersion() & "-" & DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & ".zip")

            Using zipStream As New FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archive As New ZipArchive(zipStream, ZipArchiveMode.Create)
                    WriteTextEntry(archive, "report.txt", BuildReport())
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
            result.ErrorText = ex.Message
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

    ''' <summary>Archive entry name -> source path, for the logs that exist.</summary>
    Private Function LogSources() As List(Of KeyValuePair(Of String, String))
        Dim sources As New List(Of KeyValuePair(Of String, String))()

        If Not String.IsNullOrEmpty(AppFileLogger.LogFilePath) Then
            sources.Add(New KeyValuePair(Of String, String)("current.log", AppFileLogger.LogFilePath))
        End If

        For Each candidate As String In CompanionLogCandidates()
            If File.Exists(candidate) Then
                sources.Add(New KeyValuePair(Of String, String)("companion.log", candidate))
                Exit For
            End If
        Next

        Return sources
    End Function

    Private Function CompanionLogCandidates() As String()
        Dim exeDir As String = AppContext.BaseDirectory
        Return {
            Path.Combine(If(exeDir, ""), "companion.log"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "FastMediaSorterCompanion", "companion.log")
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

                Using target As Stream = entry.Open()
                    If truncated Then
                        source.Seek(total - MaxEntryBytes, SeekOrigin.Begin)
                        Dim header As Byte() = Encoding.UTF8.GetBytes(
                            "[.. truncated: original " & total.ToString(CultureInfo.InvariantCulture) &
                            " bytes, keeping the last " & MaxEntryBytes.ToString(CultureInfo.InvariantCulture) & " ..]" &
                            Environment.NewLine)
                        target.Write(header, 0, header.Length)
                        SkipPartialLine(source)
                    End If
                    source.CopyTo(target)
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
    Private Function BuildReport() As String
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
        AppendSettings(sb)
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
    ''' Dumps the app's registry profile. The one secret in there - the translation API
    ''' key - is never written out, not even in its encrypted form.
    ''' </summary>
    Private Sub AppendSettings(sb As StringBuilder)
        Try
            Dim path As String = "Software\VB and VBA Program Settings\" & App_name & "\" & Second_App_Name
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(path, False)
                If key Is Nothing Then
                    sb.AppendLine("no saved profile")
                    Return
                End If
                For Each name As String In key.GetValueNames().OrderBy(Function(n) n, StringComparer.Ordinal)
                    If IsSecretSetting(name) Then
                        sb.AppendLine(name & "=<hidden>")
                    Else
                        sb.AppendLine(name & "=" & Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture))
                    End If
                Next
            End Using
        Catch ex As Exception
            sb.AppendLine("unreadable: " & ex.Message)
        End Try
    End Sub

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
