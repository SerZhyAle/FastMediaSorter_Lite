Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms

' Scheduled fill of free space (ticket L0299): FileDO's `fill <MB> del` driven unattended.
'
' OWNERSHIP IS BY CONSTRUCTION (Q299-4, section 4.0a of the specification). FileDO names its files with a run id it
' never publishes, so nothing it writes tells the caller which FILL_*.tmp files are this run's. Each run therefore makes
' its own work folder under the chosen place, writes an owner marker into it BEFORE FileDO starts, and points FileDO at
' that folder. Recovery looks only inside folders whose marker names this operation and removes only FileDO's exact file
' shapes there; anything else is left alone and stops the operation as a wall. FileDO's target-wide `clean` is never run.
'
' THE VERDICT is CLI-EVENT-STREAM 0.13's: the `result` event, cross-checked with the exit code (rules 10 and 11), the stop
' file this run created being authoritative unless the result says Failed (rule 15).

''' <summary>Finds FileDO's command-line program. The bundle next to this program first, then the developer override and
''' payload, then the folder FileDO's own installer registered, then PATH. Only folders come from outside; the file name is
''' ours, so no registry value or variable can make a run start some other program.</summary>
Friend Module FileDoLocator

    Friend Const CliExe As String = "filedo.exe"
    Friend Const OverrideVariable As String = "FMS_FILEDO_EXE"

    ''' <summary>The full path, or "" when no candidate exists. <paramref name="exists"/> is injected so the order is testable.</summary>
    Friend Function Locate(candidateDirs As IEnumerable(Of String), exists As Func(Of String, Boolean)) As String
        For Each dir As String In candidateDirs
            If String.IsNullOrWhiteSpace(dir) Then Continue For
            Dim path As String
            Try
                path = System.IO.Path.Combine(dir.Trim().Trim(""""c), CliExe)
            Catch ex As ArgumentException
                Continue For ' a malformed candidate is skipped; the next one still counts
            End Try
            If exists(path) Then Return path
        Next
        Return ""
    End Function

    Friend Function Find() As String
        Return Locate(CandidateDirectories(), AddressOf File.Exists)
    End Function

    Private Iterator Function CandidateDirectories() As IEnumerable(Of String)
        Dim appDir As String = ""
        Try
            appDir = If(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "")
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException
            appDir = "" ' no app folder: the other candidates still apply
        End Try
        If appDir.Length > 0 Then Yield System.IO.Path.Combine(appDir, FileDoDiskManager.BundleFolder)
        Dim overridePath As String = If(Environment.GetEnvironmentVariable(OverrideVariable), "").Trim().Trim(""""c)
        If overridePath.Length > 0 AndAlso String.Equals(System.IO.Path.GetFileName(overridePath), CliExe, StringComparison.OrdinalIgnoreCase) Then
            Yield System.IO.Path.GetDirectoryName(overridePath)
        End If
        ' A development tree: <repo>\payload\filedo next to some ancestor of the build output.
        Dim cursor As String = appDir
        For level As Integer = 0 To 6
            If String.IsNullOrEmpty(cursor) Then Exit For
            Yield System.IO.Path.Combine(cursor, "payload", "filedo")
            cursor = System.IO.Path.GetDirectoryName(cursor)
        Next
        For Each dir As String In FileDoDiskManager.RegisteredInstallDirectories()
            Yield dir
        Next
        For Each dir As String In If(Environment.GetEnvironmentVariable("PATH"), "").Split(";"c)
            If dir.Trim().Length > 0 Then Yield dir
        Next
    End Function

End Module

''' <summary>What the editor and the run need to know about the volume a place lies on.</summary>
Friend NotInheritable Class FillVolumeInfo
    Friend Property Known As Boolean
    Friend Property Root As String = ""
    Friend Property TotalBytes As Long
    Friend Property FreeBytes As Long
    Friend Property IsSystemVolume As Boolean
    Friend Property IsNetwork As Boolean

    Friend Shared Function Read(folder As String) As FillVolumeInfo
        Dim info As New FillVolumeInfo()
        Try
            Dim canonical As String = CompanionCommand.CanonicalFolder(folder)
            If canonical.Length = 0 OrElse canonical.StartsWith("\\", StringComparison.Ordinal) Then
                info.IsNetwork = canonical.StartsWith("\\", StringComparison.Ordinal)
                Return info
            End If
            info.Root = System.IO.Path.GetPathRoot(canonical)
            Dim drive As New DriveInfo(info.Root)
            info.IsNetwork = drive.DriveType = DriveType.Network
            ' A disconnected mapped drive can take seconds to answer IsReady, and the editor asks on every keystroke.
            If info.IsNetwork Then Return info
            Dim systemRoot As String = System.IO.Path.GetPathRoot(Environment.SystemDirectory)
            info.IsSystemVolume = String.Equals(info.Root, systemRoot, StringComparison.OrdinalIgnoreCase)
            If drive.IsReady Then
                info.TotalBytes = drive.TotalSize
                info.FreeBytes = drive.AvailableFreeSpace
                info.Known = True
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
        Return info
    End Function
End Class

''' <summary>One launch of FileDO, as the executor asks for it.</summary>
Friend NotInheritable Class FillLaunch
    Friend Property Exe As String = ""
    Friend Property Arguments As New List(Of String)()
    Friend Property StopFile As String = ""
    ''' <summary>How long a stop request may take before the process is ended (FileDO's own writer join budget).</summary>
    Friend Property StopGrace As TimeSpan = TimeSpan.FromSeconds(30)
    ''' <summary>Variables set in the child's environment only, never in this process's own (L0298: a sealed backup's
    ''' password, read by FileDO through `pe:&lt;VARIABLE&gt;`, CLI-EVENT-STREAM 0.17 item H). Never logged.</summary>
    Friend Property ChildEnvironment As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
End Class

''' <summary>How a launch ended.</summary>
Friend NotInheritable Class FillLaunchOutcome
    Friend Property Started As Boolean
    ''' <summary>Nothing when the process did not end by itself (it was ended after the grace period) or never started.</summary>
    Friend Property ExitCode As Integer?
    ''' <summary>The run created the stop file (it was cancelled).</summary>
    Friend Property StopRequested As Boolean
    Friend Property Killed As Boolean
End Class

''' <summary>What a fill's event file said, judged by CLI-EVENT-STREAM 0.13.</summary>
Friend Enum FillJudgement
    Passed = 0
    Failed = 1
    NotProven = 2
    Stopped = 3
End Enum

Friend NotInheritable Class FillEvents
    ''' <summary>The `result` event's verdict word, "" when the run wrote none.</summary>
    Friend Property Verdict As String = ""
    Friend Property HasResult As Boolean
    ''' <summary>A carrier this reader must refuse: a newer MAJOR or a non-integer schemaVersion (section 3).</summary>
    Friend Property Refused As Boolean
    ''' <summary>The last `progress` event's doneBytes / doneItems (rule 7; 0 when there was none).</summary>
    Friend Property DoneBytes As Long
    Friend Property DoneItems As Long
    ''' <summary>The `result` event's integer `numbers` (rule 16). An absent key is "not reported", never zero (L0298: a backup's
    ''' `generation` and `retiredGeneration`).</summary>
    Friend ReadOnly Property Numbers As New Dictionary(Of String, Long)(StringComparer.Ordinal)

    ''' <summary>A published number, or Nothing when the run did not report it.</summary>
    Friend Function Number(key As String) As Long?
        Dim value As Long
        If Numbers.TryGetValue(key, value) Then Return value
        Return Nothing
    End Function

    ''' <summary>Reads the whole file after the process ended. A final line without its newline is a write in progress and is
    ''' not read (rule 4); an unreadable line or a member this reader does not know is ignored (rules 6 and 8).</summary>
    Friend Shared Function Parse(text As String) As FillEvents
        Dim events As New FillEvents()
        If String.IsNullOrEmpty(text) Then Return events
        Dim complete As Integer = text.LastIndexOf(ChrW(10))
        If complete < 0 Then Return events
        Dim decided As Boolean = False
        For Each rawLine As String In text.Substring(0, complete).Split(ChrW(10))
            Dim line As String = rawLine.Trim()
            If line.Length = 0 Then Continue For
            Try
                Using doc As JsonDocument = JsonDocument.Parse(line)
                    Dim root As JsonElement = doc.RootElement
                    If root.ValueKind <> JsonValueKind.Object Then Continue For
                    If Not decided Then
                        decided = True
                        Dim carrier As JsonElement
                        If root.TryGetProperty("schemaVersion", carrier) Then
                            Dim version As Integer
                            If carrier.ValueKind <> JsonValueKind.Number OrElse Not carrier.TryGetInt32(version) OrElse version > 1 Then
                                events.Refused = True
                                Return events
                            End If
                        End If
                    End If
                    Dim kind As JsonElement
                    If Not root.TryGetProperty("kind", kind) OrElse kind.ValueKind <> JsonValueKind.String Then Continue For
                    Dim data As JsonElement
                    Dim hasData As Boolean = root.TryGetProperty("data", data) AndAlso data.ValueKind = JsonValueKind.Object
                    Select Case kind.GetString()
                        Case "progress"
                            If hasData Then
                                events.DoneBytes = Math.Max(events.DoneBytes, ReadLong(data, "doneBytes"))
                                events.DoneItems = Math.Max(events.DoneItems, ReadLong(data, "doneItems"))
                            End If
                        Case "result"
                            events.HasResult = True
                            Dim verdict As JsonElement
                            events.Verdict = If(hasData AndAlso data.TryGetProperty("verdict", verdict) AndAlso verdict.ValueKind = JsonValueKind.String,
                                                verdict.GetString(), "")
                            Dim numbers As JsonElement
                            If hasData AndAlso data.TryGetProperty("numbers", numbers) AndAlso numbers.ValueKind = JsonValueKind.Object Then
                                For Each member As JsonProperty In numbers.EnumerateObject()
                                    Dim number As Long
                                    If member.Value.ValueKind = JsonValueKind.Number AndAlso member.Value.TryGetInt64(number) Then
                                        events.Numbers(member.Name) = number
                                    End If
                                Next
                            End If
                    End Select
                End Using
            Catch ex As JsonException
            End Try
        Next
        Return events
    End Function

    Private Shared Function ReadLong(data As JsonElement, name As String) As Long
        Dim value As JsonElement
        Dim number As Long
        If data.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt64(number) Then Return Math.Max(0L, number)
        Return 0L
    End Function

    ''' <summary>
    ''' Rules 10, 11 and 15: no result, a refused carrier, an unknown verdict word or a verdict its exit code contradicts is
    ''' not proven; a stop this run requested is authoritative unless the result is Failed, which outranks a stop.
    ''' <paramref name="exitCode"/> is Nothing when the process was ended rather than exiting by itself.
    ''' </summary>
    Friend Function Judge(exitCode As Integer?, stopRequested As Boolean) As FillJudgement
        Dim agreed As Boolean
        Dim judged As FillJudgement = FillJudgement.NotProven
        If Not Refused AndAlso HasResult Then
            Select Case Verdict
                Case "Passed", "Done" : judged = FillJudgement.Passed : agreed = exitCode.HasValue AndAlso exitCode.Value = 0
                Case "Failed" : judged = FillJudgement.Failed : agreed = exitCode.HasValue AndAlso exitCode.Value = 1
                Case "Not proven" : judged = FillJudgement.NotProven : agreed = exitCode.HasValue AndAlso exitCode.Value = 2
                Case "Stopped" : judged = FillJudgement.Stopped : agreed = exitCode.HasValue AndAlso exitCode.Value = 0
            End Select
        End If
        If stopRequested Then
            If judged <> FillJudgement.Failed Then Return FillJudgement.Stopped
            Return If(agreed, FillJudgement.Failed, FillJudgement.NotProven)
        End If
        Return If(agreed, judged, FillJudgement.NotProven)
    End Function
End Class

''' <summary>Starts FileDO hidden, waits, and on cancellation asks it to stop through the stop file (rule 14) before it ends it.</summary>
Friend Module FillProcess

    Friend Function Run(launch As FillLaunch, ct As CancellationToken) As FillLaunchOutcome
        Dim outcome As New FillLaunchOutcome()
        Dim info As New ProcessStartInfo(launch.Exe) With {
            .UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardInput = True,
            .WorkingDirectory = If(System.IO.Path.GetDirectoryName(launch.Exe), "")}
        For Each argument As String In launch.Arguments
            info.ArgumentList.Add(argument)
        Next
        ' No prompt may halt an unattended run; the closed stdin answers any that is left with its default.
        info.Environment("FILEDO_AUTO_CONFIRM") = "1"
        info.Environment("FILEDO_NO_UI") = "1"
        For Each pair As KeyValuePair(Of String, String) In launch.ChildEnvironment
            info.Environment(pair.Key) = pair.Value
        Next
        Dim child As Process
        Try
            child = Process.Start(info)
        Catch ex As Exception When TypeOf ex Is System.ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            AppFileLogger.LogException("Scheduled operations: starting FileDO", ex)
            Return outcome
        End Try
        If child Is Nothing Then Return outcome
        outcome.Started = True
        Using child
            Try
                child.StandardInput.Close()
            Catch ex As IOException
            End Try
            Do Until child.WaitForExit(500)
                If ct.IsCancellationRequested Then
                    outcome.StopRequested = True
                    Try
                        File.WriteAllText(launch.StopFile, "stop")
                    Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                        AppFileLogger.LogException("Scheduled operations: FileDO stop file", ex)
                    End Try
                    If Not child.WaitForExit(CInt(launch.StopGrace.TotalMilliseconds)) Then
                        Try
                            child.Kill(entireProcessTree:=True)
                            outcome.Killed = True
                        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is System.ComponentModel.Win32Exception
                        End Try
                        child.WaitForExit(5000)
                    End If
                    Exit Do
                End If
            Loop
            If child.HasExited AndAlso Not outcome.Killed Then outcome.ExitCode = child.ExitCode
        End Using
        Return outcome
    End Function

End Module

''' <summary>The run-owned work folders of a fill operation and their recovery (Q299-4).</summary>
Friend Module FillWorkFolders

    Friend Const Prefix As String = "FMS-FillFreeSpace-"
    Friend Const MarkerName As String = "fms-fill-owner.json"
    ''' <summary>The run's event file and stop file live in its work folder too, so a run leaves nothing anywhere else.</summary>
    Friend Const EventsName As String = "fms-fill-events.jsonl"
    Friend Const StopName As String = "fms-fill-stop"
    Private Const StampFormat As String = "yyyyMMddHHmmss"

    ' FileDO's own file shapes (capacity_plan.go, CAP-14): fill files, current and legacy, and its writability probe.
    Private ReadOnly FillFile As New Regex("^FILL_\d{3,}_(\d{8}_[0-9a-f]{8}|\d{6,8})\.tmp$", RegexOptions.CultureInvariant)
    Private ReadOnly ProbeFile As New Regex("^__filedo_(access_)?test_\d+\.tmp$", RegexOptions.CultureInvariant)
    Friend Const FatSubfolder As String = "FileDO_Test"

    Friend Function OperationTag(operationId As String) As String
        Return New String(If(operationId, "").Where(Function(c) Char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant()
    End Function

    Friend Function FolderName(operationId As String, startedAt As DateTime) As String
        Return Prefix & OperationTag(operationId) & "-" & startedAt.ToString(StampFormat, CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Is <paramref name="name"/> one of this operation's work folders by its name?</summary>
    Friend Function IsOwnName(name As String, operationId As String) As Boolean
        Dim head As String = Prefix & OperationTag(operationId) & "-"
        If OperationTag(operationId).Length = 0 OrElse Not name.StartsWith(head, StringComparison.OrdinalIgnoreCase) Then Return False
        Dim stamp As String = name.Substring(head.Length)
        Return stamp.Length = StampFormat.Length AndAlso stamp.All(Function(c) Char.IsDigit(c))
    End Function

    ''' <summary>Creates the folder and its owner marker; the marker is written before FileDO ever sees the folder.</summary>
    Friend Function Create(place As String, operationId As String, startedAt As DateTime) As String
        Dim folder As String = Path.Combine(place, FolderName(operationId, startedAt))
        Directory.CreateDirectory(folder)
        Dim marker As String = JsonSerializer.Serialize(New Dictionary(Of String, String) From {
            {"operationId", operationId}, {"startedAt", startedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)},
            {"owner", "FastMediaSorterCompanion"}})
        File.WriteAllText(Path.Combine(folder, MarkerName), marker)
        Return folder
    End Function

    ''' <summary>Does the folder's marker name this operation?</summary>
    Friend Function MarkerNames(folder As String, operationId As String) As Boolean
        Try
            Using doc As JsonDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, MarkerName)))
                Dim id As JsonElement
                Return doc.RootElement.ValueKind = JsonValueKind.Object AndAlso doc.RootElement.TryGetProperty("operationId", id) AndAlso
                       id.ValueKind = JsonValueKind.String AndAlso String.Equals(id.GetString(), operationId, StringComparison.OrdinalIgnoreCase)
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
            Return False
        End Try
    End Function

    Private Function IsFileDoShape(name As String) As Boolean
        Return FillFile.IsMatch(name) OrElse ProbeFile.IsMatch(name)
    End Function

    Private Function IsRunFile(name As String) As Boolean
        Return String.Equals(name, EventsName, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(name, StopName, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' Can <see cref="Clear"/> remove this folder, by its contents alone? Read-only: the pre-flight of a walled operation
    ''' asks this, and only <see cref="Clear"/> touches the disk.
    ''' </summary>
    Friend Function IsClearable(folder As String, operationId As String) As Boolean
        Try
            If Not Directory.Exists(folder) Then Return True
            If Not MarkerNames(folder, operationId) Then Return Not Directory.EnumerateFileSystemEntries(folder).Any()
            For Each entry As String In Directory.EnumerateFiles(folder)
                Dim name As String = Path.GetFileName(entry)
                If Not (String.Equals(name, MarkerName, StringComparison.OrdinalIgnoreCase) OrElse IsRunFile(name) OrElse IsFileDoShape(name)) Then Return False
            Next
            For Each subfolder As String In Directory.EnumerateDirectories(folder)
                If Not String.Equals(Path.GetFileName(subfolder), FatSubfolder, StringComparison.OrdinalIgnoreCase) Then Return False
                If Directory.EnumerateDirectories(subfolder).Any() Then Return False
                If Directory.EnumerateFiles(subfolder).Any(Function(entry) Not IsFileDoShape(Path.GetFileName(entry))) Then Return False
            Next
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Empties and removes one work folder of this operation: FileDO's exact file shapes, its FAT subfolder, the run's own
    ''' event and stop files, then the marker and the folder. True when the folder is gone. A file of any other shape, a
    ''' folder whose marker does not name this operation, or a file that cannot be removed leaves the folder in place - its
    ''' ownership is not proven, or it is stuck - and the caller walls.
    ''' </summary>
    Friend Function Clear(folder As String, operationId As String) As Boolean
        If Not Directory.Exists(folder) Then Return True
        Try
            If Not MarkerNames(folder, operationId) Then
                ' A folder of our name without a readable marker: only an empty one (a run that died between the two writes) goes.
                If Directory.EnumerateFileSystemEntries(folder).Any() Then Return False
                Directory.Delete(folder)
                Return True
            End If
            Dim foreign As Boolean = False
            For Each entry As String In Directory.EnumerateFiles(folder)
                Dim name As String = Path.GetFileName(entry)
                If String.Equals(name, MarkerName, StringComparison.OrdinalIgnoreCase) Then Continue For
                If IsFileDoShape(name) OrElse IsRunFile(name) Then
                    File.Delete(entry)
                Else
                    foreign = True
                End If
            Next
            For Each subfolder As String In Directory.EnumerateDirectories(folder)
                If Not String.Equals(Path.GetFileName(subfolder), FatSubfolder, StringComparison.OrdinalIgnoreCase) Then
                    foreign = True
                    Continue For
                End If
                For Each entry As String In Directory.EnumerateFiles(subfolder)
                    If IsFileDoShape(Path.GetFileName(entry)) Then File.Delete(entry)
                Next
                If Directory.EnumerateFileSystemEntries(subfolder).Any() Then
                    foreign = True
                Else
                    Directory.Delete(subfolder)
                End If
            Next
            If foreign Then Return False
            File.Delete(Path.Combine(folder, MarkerName))
            Directory.Delete(folder)
            Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            AppFileLogger.LogException("Scheduled operations: clearing " & folder, ex)
            Return False
        End Try
    End Function

    ''' <summary>The work folders of this operation in <paramref name="place"/>, by name; none when the place cannot be listed.</summary>
    Friend Function OwnFolders(place As String, operationId As String) As List(Of String)
        Try
            Return Directory.GetDirectories(place, Prefix & "*").Where(Function(folder) IsOwnName(Path.GetFileName(folder), operationId)).ToList()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>Every earlier work folder of this operation in <paramref name="place"/> that could not be cleared.</summary>
    Friend Function RecoverEarlier(place As String, operationId As String) As List(Of String)
        Return OwnFolders(place, operationId).Where(Function(folder) Not Clear(folder, operationId)).ToList()
    End Function

End Module

''' <summary>The command line of one fill, CLI-EVENT-STREAM's two flags included.</summary>
Friend Module FillCommandLine

    Friend Function Arguments(workFolder As String, chunkSizeMb As Integer, eventsFile As String, stopFile As String) As List(Of String)
        Return New List(Of String) From {
            "folder", workFolder, "fill", chunkSizeMb.ToString(CultureInfo.InvariantCulture), "del",
            "--no-ui", "--events", eventsFile, "--stop-file", stopFile}
    End Function

    ''' <summary>"fill:free=..;bytes=..;exit=.." - the run's verdict text. Machine words, read back by <see cref="ScheduledText"/>.</summary>
    Friend Function Verdict(freeAtStart As Long, bytesWritten As Long, exitCode As Integer?) As String
        Return String.Format(CultureInfo.InvariantCulture, "fill:free={0};bytes={1};exit={2}", freeAtStart, bytesWritten,
                             If(exitCode.HasValue, exitCode.Value.ToString(CultureInfo.InvariantCulture), "none"))
    End Function

    ''' <summary>Reads <see cref="Verdict"/> back; False for any other text.</summary>
    Friend Function TryReadVerdict(text As String, ByRef freeAtStart As Long, ByRef bytesWritten As Long) As Boolean
        freeAtStart = 0
        bytesWritten = 0
        If text Is Nothing OrElse Not text.StartsWith("fill:", StringComparison.Ordinal) Then Return False
        For Each part As String In text.Substring("fill:".Length).Split(";"c)
            Dim eq As Integer = part.IndexOf("="c)
            If eq < 0 Then Continue For
            Dim value As Long
            If Not Long.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, value) Then Continue For
            Select Case part.Substring(0, eq)
                Case "free" : freeAtStart = value
                Case "bytes" : bytesWritten = value
            End Select
        Next
        Return True
    End Function

End Module
