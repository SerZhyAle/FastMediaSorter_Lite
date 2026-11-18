Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text.RegularExpressions

' Backup of a folder (tickets L0298, L0346): FileDO's `backup` verb with numbered generation sets (`CLI-EVENT-STREAM` 0.17)
' or dated retention + monthly-first preservation (`CLI-EVENT-STREAM` 0.18, `keep-days` / `keep-monthly` / `skip-links`).

''' <summary>How one backup run ended, read from the `result` event, the exit class and the reason tokens (0.17 item F).</summary>
Friend Enum BackupEnding
    Done = 0
    ''' <summary>The run stopped because this run asked it to; nothing was retired (item D3).</summary>
    Stopped = 1
    ''' <summary>Class 10: the definition is refused however often it is tried. The operation is parked until edited.</summary>
    DefinitionRefused = 2
    ''' <summary>Class 11, 13, 15, or 2 with `file-in-use`: an obstacle that may pass. The operation waits at a wall.</summary>
    Obstacle = 3
    ''' <summary>`Failed`, exit 1: a defect was proven.</summary>
    Failed = 4
    ''' <summary>Anything else: no result, a refused carrier, a verdict its code contradicts, or a class of neither kind.</summary>
    NotProven = 5
End Enum

Friend Module BackupCommandLine

    ''' <summary>The child-only variable that carries a sealed backup's password (`pe:`); FileDO removes it on first read.</summary>
    Friend Const PasswordVariable As String = "FMS_SCHEDULED_BACKUP_SECRET"
    Friend Const EventsSuffix As String = ".backup-events.jsonl"
    Friend Const StopSuffix As String = ".backup-stop"

    ''' <summary>
    ''' `backup &lt;source&gt; to &lt;destination&gt; gen &lt;keep&gt; stem &lt;stem&gt; [skip-links] [sec pe:&lt;VAR&gt;] y --no-ui --events .. --stop-file ..`.
    ''' </summary>
    Friend Function Arguments(source As String, destination As String, keepCount As Integer, stem As String, sealed As Boolean,
                              eventsFile As String, stopFile As String) As List(Of String)
        Return Arguments(source, destination, BackupRetentionMode.Generations, keepCount,
                         ScheduledOperation.DefaultBackupKeepDays, False, False, stem, sealed, eventsFile, stopFile)
    End Function

    ''' <summary>
    ''' Composes the FileDO `backup` argument list for numbered generations (`gen &lt;N&gt; stem &lt;stem&gt;`) or dated retention
    ''' (`keep-days &lt;N&gt; [keep-monthly] stem &lt;stem&gt;`), with optional `skip-links` and `sec pe:&lt;VAR&gt;`.
    ''' The password itself never stands here: only the variable name in the child's environment.
    ''' </summary>
    Friend Function Arguments(source As String, destination As String, retention As BackupRetentionMode,
                              keepCount As Integer, keepDays As Integer, keepMonthly As Boolean, skipLinks As Boolean,
                              stem As String, sealed As Boolean, eventsFile As String, stopFile As String) As List(Of String)
        Dim args As New List(Of String) From {"backup", source, "to", destination}
        If retention = BackupRetentionMode.Dated Then
            args.Add("keep-days")
            args.Add(keepDays.ToString(CultureInfo.InvariantCulture))
            If keepMonthly Then args.Add("keep-monthly")
        Else
            args.Add("gen")
            args.Add(keepCount.ToString(CultureInfo.InvariantCulture))
        End If
        args.Add("stem")
        args.Add(stem)
        If skipLinks Then args.Add("skip-links")
        If sealed Then
            args.Add("sec")
            args.Add("pe:" & PasswordVariable)
        End If
        args.AddRange({"y", "--no-ui", "--events", eventsFile, "--stop-file", stopFile})
        Return args
    End Function

    ''' <summary>`&lt;stem&gt;_&lt;NNN&gt;.zip`, or `.fd-sec` when sealed (item D1).</summary>
    Friend Function GenerationName(stem As String, number As Long, sealed As Boolean) As String
        Return stem & "_" & number.ToString("000", CultureInfo.InvariantCulture) & If(sealed, ".fd-sec", ".zip")
    End Function

    ''' <summary>`&lt;stem&gt;-&lt;yyMMddHHmm&gt;.zip`, or `.fd-sec` when sealed (CLI-EVENT-STREAM 0.18 item I1).</summary>
    Friend Function DatedArchiveName(stem As String, stamp As DateTime, sealed As Boolean) As String
        Return stem & "-" & stamp.ToString("yyMMddHHmm", CultureInfo.InvariantCulture) & If(sealed, ".fd-sec", ".zip")
    End Function

    ''' <summary>Parses `&lt;stem&gt;-&lt;yyMMddHHmm&gt;&lt;ext&gt;` strictly in local wall-clock time; invalid calendar dates return False.</summary>
    Friend Function TryParseDatedArchiveStamp(fileName As String, stem As String, extension As String, ByRef stamp As DateTime) As Boolean
        stamp = DateTime.MinValue
        If String.IsNullOrEmpty(fileName) OrElse String.IsNullOrEmpty(stem) OrElse String.IsNullOrEmpty(extension) Then Return False
        If fileName.Length < extension.Length + 12 Then Return False
        If Not String.Equals(fileName.Substring(fileName.Length - extension.Length), extension, StringComparison.OrdinalIgnoreCase) Then Return False
        Dim body As String = fileName.Substring(0, fileName.Length - extension.Length)
        If body.Length < 12 Then Return False
        Dim digits As String = body.Substring(body.Length - 10)
        For i As Integer = 0 To digits.Length - 1
            If digits(i) < "0"c OrElse digits(i) > "9"c Then Return False
        Next
        Dim head As String = body.Substring(0, body.Length - 10)
        If head.Length = 0 OrElse head(head.Length - 1) <> "-"c Then Return False
        If Not String.Equals(head.Substring(0, head.Length - 1), stem, StringComparison.OrdinalIgnoreCase) Then Return False
        Dim parsed As DateTime
        If Not DateTime.TryParseExact(digits, "yyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return False
        stamp = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
        Return True
    End Function

    ''' <summary>Regular files in <paramref name="destinationFolder"/> belonging to the dated set `&lt;stem&gt;-&lt;yyMMddHHmm&gt;&lt;ext&gt;`.</summary>
    Friend Function ScanDatedSet(destinationFolder As String, stem As String, sealed As Boolean) As Dictionary(Of String, String)
        Dim found As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrEmpty(destinationFolder) OrElse Not Directory.Exists(destinationFolder) Then Return found
        Dim ext As String = If(sealed, ".fd-sec", ".zip")
        Try
            Dim dir As New DirectoryInfo(destinationFolder)
            For Each f As FileInfo In dir.EnumerateFiles()
                If (f.Attributes And FileAttributes.ReparsePoint) <> 0 Then Continue For
                Dim stamp As DateTime
                If TryParseDatedArchiveStamp(f.Name, stem, ext, stamp) AndAlso Not found.ContainsKey(f.Name) Then
                    found(f.Name) = f.Name
                End If
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
            AppFileLogger.LogException("Scheduled operations: scanning dated backup set " & destinationFolder, ex)
        End Try
        Return found
    End Function

    ''' <summary>Item F, cross-checked with rule 11 and the exit table of the amendment of 2026-11-10.</summary>
    Friend Function Classify(events As FillEvents, exitCode As Integer?, stopRequested As Boolean) As BackupEnding
        If events Is Nothing OrElse events.Refused OrElse Not events.HasResult Then
            Return If(stopRequested, BackupEnding.Stopped, BackupEnding.NotProven)
        End If
        Dim code As Integer = If(exitCode.HasValue, exitCode.Value, -1)
        Select Case events.Verdict
            Case "Failed"
                ' A proven defect outranks a stop (rule 15).
                Return If(code = 1, BackupEnding.Failed, BackupEnding.NotProven)
            Case "Done", "Passed"
                If stopRequested Then Return BackupEnding.Stopped
                Return If(code = 0, BackupEnding.Done, BackupEnding.NotProven)
            Case "Stopped"
                Return If(stopRequested AndAlso (code = 16 OrElse code = 0), BackupEnding.Stopped, BackupEnding.NotProven)
            Case "Not proven"
                If stopRequested Then Return BackupEnding.Stopped
                Select Case code
                    Case 10 : Return BackupEnding.DefinitionRefused
                    Case 11, 13, 15 : Return BackupEnding.Obstacle
                    Case 2 : Return If(events.Reasons.Contains("file-in-use"), BackupEnding.Obstacle, BackupEnding.NotProven)
                    Case Else : Return BackupEnding.NotProven
                End Select
        End Select
        Return If(stopRequested, BackupEnding.Stopped, BackupEnding.NotProven)
    End Function

    ''' <summary>The wall detail of a refused definition or a passing obstacle, as an existing code plus the place.</summary>
    Friend Function WallDetail(ending As BackupEnding, events As FillEvents, exitCode As Integer?, source As String, destination As String,
                               sourceExists As Boolean) As String
        If ending = BackupEnding.DefinitionRefused Then
            If events.Reasons.Contains("backup-destination-refused") Then Return "backup-destination-refused|" & destination
            ' A build without the tokens is read by its class (item G); the source is the definition's first word.
            Return "backup-source-refused|" & source
        End If
        Select Case If(exitCode.HasValue, exitCode.Value, -1)
            Case 13 : Return "disk-full|" & destination
            Case 15 : Return "transport|" & If(sourceExists, destination, source)
            Case 2 : Return "locked|" & source
            Case Else : Return If(sourceExists, "destination-unreachable|" & destination, "source-unreachable|" & source)
        End Select
    End Function

    ''' <summary>"backup:gen=..;retired=..;keep=..;files=..;bytes=..;exit=.." or dated retention verdict; an absent number is "none".</summary>
    Friend Function Verdict(events As FillEvents, exitCode As Integer?) As String
        Return Verdict(events, exitCode, BackupRetentionMode.Generations, 0)
    End Function

    Friend Function Verdict(events As FillEvents, exitCode As Integer?, retention As BackupRetentionMode,
                            skippedLinksCount As Integer) As String
        Dim exitText As String = If(exitCode.HasValue, exitCode.Value.ToString(CultureInfo.InvariantCulture), "none")
        If retention = BackupRetentionMode.Dated OrElse (events IsNot Nothing AndAlso events.Number("keepDays").HasValue) Then
            Dim baseText As String = String.Format(CultureInfo.InvariantCulture,
                "backup:mode=dated;retiredCount={0};keptCount={1};keepDays={2};monthlyKept={3};files={4};bytes={5};exit={6}",
                NumberText(If(events IsNot Nothing, events.Number("retiredCount"), Nothing)),
                NumberText(If(events IsNot Nothing, events.Number("keptCount"), Nothing)),
                NumberText(If(events IsNot Nothing, events.Number("keepDays"), Nothing)),
                NumberText(If(events IsNot Nothing, events.Number("monthlyKept"), Nothing)),
                NumberText(If(events IsNot Nothing, events.Number("files"), Nothing)),
                NumberText(If(events IsNot Nothing, events.Number("bytes"), Nothing)),
                exitText)
            If skippedLinksCount > 0 Then
                baseText &= ";skippedLinks=" & skippedLinksCount.ToString(CultureInfo.InvariantCulture)
            End If
            Return baseText
        End If
        Dim genText As String = String.Format(CultureInfo.InvariantCulture, "backup:gen={0};retired={1};keep={2};files={3};bytes={4};exit={5}",
                             NumberText(If(events IsNot Nothing, events.Number("generation"), Nothing)),
                             NumberText(If(events IsNot Nothing, events.Number("retiredGeneration"), Nothing)),
                             NumberText(If(events IsNot Nothing, events.Number("keepCount"), Nothing)),
                             NumberText(If(events IsNot Nothing, events.Number("files"), Nothing)),
                             NumberText(If(events IsNot Nothing, events.Number("bytes"), Nothing)),
                             exitText)
        If skippedLinksCount > 0 Then
            genText &= ";skippedLinks=" & skippedLinksCount.ToString(CultureInfo.InvariantCulture)
        End If
        Return genText
    End Function

    Private Function NumberText(value As Long?) As String
        Return If(value.HasValue, value.Value.ToString(CultureInfo.InvariantCulture), "none")
    End Function

    ''' <summary>Parses a machine verdict written by <see cref="Verdict"/> into its optional integer numbers.</summary>
    Friend Sub TryParseVerdict(verdict As String, ByRef isDated As Boolean, ByRef generation As Long?,
                               ByRef retiredGeneration As Long?, ByRef retiredCount As Long?, ByRef keptCount As Long?,
                               ByRef keepDays As Long?, ByRef monthlyKept As Long?, ByRef skippedLinks As Long?)
        isDated = False
        generation = Nothing
        retiredGeneration = Nothing
        retiredCount = Nothing
        keptCount = Nothing
        keepDays = Nothing
        monthlyKept = Nothing
        skippedLinks = Nothing
        If String.IsNullOrEmpty(verdict) OrElse Not verdict.StartsWith("backup:", StringComparison.Ordinal) Then Return
        Dim body As String = verdict.Substring("backup:".Length)
        For Each part As String In body.Split(";"c)
            Dim eq As Integer = part.IndexOf("="c)
            If eq <= 0 Then Continue For
            Dim key As String = part.Substring(0, eq)
            Dim raw As String = part.Substring(eq + 1)
            If String.Equals(key, "mode", StringComparison.Ordinal) AndAlso String.Equals(raw, "dated", StringComparison.Ordinal) Then
                isDated = True
                Continue For
            End If
            Dim value As Long
            If Not Long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, value) Then Continue For
            Select Case key
                Case "gen"
                    If value > 0 Then generation = value
                Case "retired"
                    If value > 0 Then retiredGeneration = value
                Case "retiredCount"
                    If value >= 0 Then retiredCount = value
                Case "keptCount"
                    If value >= 0 Then keptCount = value
                Case "keepDays"
                    If value > 0 Then
                        keepDays = value
                        isDated = True
                    End If
                Case "monthlyKept"
                    If value >= 0 Then monthlyKept = value
                Case "skippedLinks"
                    If value >= 0 Then skippedLinks = value
            End Select
        Next
    End Sub

End Module

''' <summary>
''' Whether a FileDO build has numbered generations (`gen` + `stem`), dated retention (`keep-days` + `keep-monthly` + `stem`),
''' or `skip-links`. Asked once per exe file (path, size, time); a build that does not answer within the limit answers False.
''' </summary>
Friend Module FileDoBackupProbe

    Private ReadOnly HelpCache As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly GenWord As New Regex("\bgen\s+(\d|<)", RegexOptions.CultureInvariant)
    Private ReadOnly StemWord As New Regex("\bstem\s+\S", RegexOptions.CultureInvariant)
    Private ReadOnly KeepDaysWord As New Regex("\bkeep-days\s+(\d|<)", RegexOptions.CultureInvariant)
    Private ReadOnly KeepMonthlyWord As New Regex("\bkeep-monthly\b", RegexOptions.CultureInvariant)
    Private ReadOnly SkipLinksWord As New Regex("\bskip-links\b", RegexOptions.CultureInvariant)

    Friend Function HelpNamesGenerations(helpText As String) As Boolean
        If String.IsNullOrEmpty(helpText) Then Return False
        Return GenWord.IsMatch(helpText) AndAlso StemWord.IsMatch(helpText)
    End Function

    Friend Function HelpNamesDatedRetention(helpText As String) As Boolean
        If String.IsNullOrEmpty(helpText) Then Return False
        Return KeepDaysWord.IsMatch(helpText) AndAlso KeepMonthlyWord.IsMatch(helpText) AndAlso StemWord.IsMatch(helpText)
    End Function

    Friend Function HelpNamesSkipLinks(helpText As String) As Boolean
        If String.IsNullOrEmpty(helpText) Then Return False
        Return SkipLinksWord.IsMatch(helpText)
    End Function

    Friend Function HasGenerations(exe As String) As Boolean
        Return HelpNamesGenerations(ReadHelpCached(exe))
    End Function

    Friend Function HasDatedRetention(exe As String) As Boolean
        Return HelpNamesDatedRetention(ReadHelpCached(exe))
    End Function

    Friend Function HasSkipLinks(exe As String) As Boolean
        Return HelpNamesSkipLinks(ReadHelpCached(exe))
    End Function

    Private Function ReadHelpCached(exe As String) As String
        If String.IsNullOrEmpty(exe) Then Return ""
        Dim key As String
        Try
            Dim info As New FileInfo(exe)
            If Not info.Exists Then Return ""
            key = exe & "|" & info.Length.ToString(CultureInfo.InvariantCulture) & "|" &
                  info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            Return ""
        End Try
        Return HelpCache.GetOrAdd(key, Function(k As String) ReadHelp(exe))
    End Function

    Private Function ReadHelp(exe As String) As String
        Dim info As New ProcessStartInfo(exe) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardInput = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True,
            .WorkingDirectory = If(Path.GetDirectoryName(exe), "")}
        info.ArgumentList.Add("help")
        info.Environment("FILEDO_AUTO_CONFIRM") = "1"
        info.Environment("FILEDO_NO_UI") = "1"
        Try
            Using child As Process = Process.Start(info)
                If child Is Nothing Then Return ""
                child.StandardInput.Close()
                Dim errorText = child.StandardError.ReadToEndAsync()
                Dim outputText = child.StandardOutput.ReadToEndAsync()
                If Not child.WaitForExit(10000) Then
                    Try
                        child.Kill(entireProcessTree:=True)
                    Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is System.ComponentModel.Win32Exception
                    End Try
                    Return ""
                End If
                Return outputText.Result & errorText.Result
            End Using
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            AppFileLogger.LogException("Scheduled operations: asking FileDO for its help", ex)
            Return ""
        End Try
    End Function

End Module
