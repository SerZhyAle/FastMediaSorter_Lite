Option Strict On

Imports System.IO
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The remote half of a scheduled backup (ticket L0343): staging of a nested SFTP source, the checked publication of
''' a generation into the remote numbered set (the cooperative lock, the read-back, the provably-later timestamp, the
''' observable-change refusal, the one-removal retirement and the shapes that are never touched), and the FileDO
''' number rule transcribed from the producer - against an in-memory SFTP server, so no connection, no credential and
''' no real server is involved.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ScheduledBackupRemoteTests

    ''' <summary>
    ''' An SFTP-like server held in memory: "/" paths, folders, files and links, an upload time the test controls,
    ''' and a refusal hook on every mutating verb - everything the staging walk and the publication need.
    ''' </summary>
    Friend NotInheritable Class FakeRemoteServer
        Implements IFileSystemProvider, ITypedStatProvider, IManagerDirectoryProvider, IDisposable

        Friend ReadOnly Files As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
        Private ReadOnly Times As New Dictionary(Of String, Date)(StringComparer.Ordinal)
        Private ReadOnly Folders As New HashSet(Of String)(StringComparer.Ordinal) From {"/"}
        Private ReadOnly Links As New HashSet(Of String)(StringComparer.Ordinal)

        Friend ListingFails As Boolean
        ''' <summary>More children than this make the listing truncate (a prefix is never the folder).</summary>
        Friend TruncateAt As Integer = Integer.MaxValue
        Friend DownloadTransform As Func(Of Byte(), Byte())
        Friend UploadTransform As Func(Of Byte(), Byte())
        Friend FailUpload As Boolean
        ''' <summary>A path whose deletion the server refuses (the retired slot's removal fails).</summary>
        Friend DeleteRefusesPath As String = ""
        ''' <summary>A file whose rename into place the server refuses (the publish keeps the verified partial).</summary>
        Friend NameOfRefusedRename As String = ""
        ''' <summary>The fixed mtime every upload gets when set (the equal-timestamp refusal); otherwise each upload
        ''' is one strictly later second.</summary>
        Friend UploadTime As Date?
        ''' <summary>Called during every download; a test cancels its own token here.</summary>
        Friend CancelAction As Action
        ''' <summary>Invoked after the Nth listing of any folder is built, with its 1-based number - the hook a test
        ''' uses to change the set between the run's check and its mutation.</summary>
        Friend OnNthList As Action(Of Integer)
        Friend ListCount As Integer

        Private uploadClock As New Date(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc)

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property

        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Return New ProviderCapabilities(canWrite:=True, canDelete:=True, canRename:=True, canCreateFolder:=True,
                    hasRecycleBin:=False, needsMaterialisation:=True, supportsServerSideRename:=True,
                    costHint:=ProviderCostHint.Slow, supportsCheckedSourceDelete:=True)
            End Get
        End Property

        Friend Sub Put(path As String, content As String, Optional writtenUtc As Date = Nothing)
            Files(Canonical(path)) = System.Text.Encoding.UTF8.GetBytes(content)
            Times(Canonical(path)) = If(writtenUtc = Nothing, New Date(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), writtenUtc)
        End Sub

        Friend Sub PutFolder(path As String, Optional writtenUtc As Date = Nothing)
            Folders.Add(Canonical(path))
            Times(Canonical(path)) = If(writtenUtc = Nothing, New Date(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), writtenUtc)
        End Sub

        Friend Sub PutLink(path As String, Optional writtenUtc As Date = Nothing)
            Links.Add(Canonical(path))
            Times(Canonical(path)) = If(writtenUtc = Nothing, New Date(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), writtenUtc)
        End Sub

        Private Shared Function Canonical(path As String) As String
            Dim trimmed As String = If(path, "").TrimEnd("/"c)
            Return If(trimmed.Length = 0, "/", trimmed)
        End Function

        Private Function EntryOf(path As String) As ResourceEntry
            Dim isDir As Boolean = Folders.Contains(path) AndAlso Not Links.Contains(path)
            Return New ResourceEntry With {
                .FullPath = path,
                .Name = path.Substring(path.LastIndexOf("/"c) + 1),
                .Length = If(isDir, 0L, Files(path).Length),
                .LastWriteUtc = Times(path),
                .IsDirectory = isDir,
                .IsLink = Links.Contains(path),
                .IsHidden = path.Substring(path.LastIndexOf("/"c) + 1).StartsWith("."c)}
        End Function

        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Dim prefix As String = Canonical(dirPath) & "/"
            Return Files.Keys.Where(Function(p As String) p.StartsWith(prefix, StringComparison.Ordinal)).
                Select(Function(p As String) EntryOf(p)).ToList()
        End Function

        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            entry = Nothing
            Dim canonicalPath As String = Canonical(path)
            If Files.ContainsKey(canonicalPath) Then
                entry = EntryOf(canonicalPath)
                Return True
            End If
            If Links.Contains(canonicalPath) Then
                entry = New ResourceEntry With {.FullPath = canonicalPath, .Name = canonicalPath.Substring(canonicalPath.LastIndexOf("/"c) + 1),
                    .LastWriteUtc = Times(canonicalPath), .IsDirectory = False, .IsLink = True}
                Return True
            End If
            If Folders.Contains(canonicalPath) Then
                entry = New ResourceEntry With {.FullPath = canonicalPath, .Name = canonicalPath.Substring(canonicalPath.LastIndexOf("/"c) + 1),
                    .LastWriteUtc = Times(canonicalPath), .IsDirectory = True}
                Return True
            End If
            Return False
        End Function

        Public Function Stat(path As String) As StatResult Implements ITypedStatProvider.Stat
            Dim entry As ResourceEntry = Nothing
            If TryStat(path, entry) Then Return StatResult.Found(entry)
            Return StatResult.Missing()
        End Function

        Public Function StatEntry(path As String) As StatResult Implements IManagerDirectoryProvider.StatEntry
            Dim entry As ResourceEntry = Nothing
            If TryStat(path, entry) Then Return StatResult.Found(entry)
            Return StatResult.Missing()
        End Function

        Public Function ListChildren(dirPath As String, ct As CancellationToken) As DirectoryListing Implements IManagerDirectoryProvider.ListChildren
            ListCount += 1
            If ListingFails Then Return DirectoryListing.Failed(New ResourceFailure(ResourceFailureKind.Transport))
            Dim folder As String = Canonical(dirPath)
            If Not Folders.Contains(folder) Then Return DirectoryListing.Missing()
            Dim entries As New List(Of ResourceEntry)()
            For Each child As String In Files.Keys
                If ParentOfInternal(child) = folder Then entries.Add(EntryOf(child))
            Next
            For Each child As String In Links
                If Files.ContainsKey(child) Then Continue For
                If ParentOfInternal(child) = folder Then
                    entries.Add(New ResourceEntry With {.FullPath = child, .Name = child.Substring(child.LastIndexOf("/"c) + 1),
                        .Length = 4L, .LastWriteUtc = Times(child), .IsDirectory = False, .IsLink = True})
                End If
            Next
            For Each child As String In Folders
                If child = "/" OrElse child = folder Then Continue For
                If ParentOfInternal(child) = folder Then entries.Add(EntryOf(child))
            Next
            OnNthList?.Invoke(ListCount)
            If entries.Count > TruncateAt Then
                Return DirectoryListing.Listed(entries.Take(TruncateAt).ToList(), truncated:=True)
            End If
            Return DirectoryListing.Listed(entries, False)
        End Function

        Private Shared Function ParentOfInternal(path As String) As String
            Dim slash As Integer = path.LastIndexOf("/"c)
            Return If(slash <= 0, "/", path.Substring(0, slash))
        End Function

        Public Function IsWithinRoot(rootPath As String, path As String) As Boolean Implements IManagerDirectoryProvider.IsWithinRoot
            Return ManagerPathRules.Contains(ResourceKind.Sftp, rootPath, path)
        End Function

        Public Function PathsEqual(a As String, b As String) As Boolean Implements IManagerDirectoryProvider.PathsEqual
            Return ManagerPathRules.AreEqual(ResourceKind.Sftp, a, b)
        End Function

        Public Function ParentOf(path As String) As String Implements IManagerDirectoryProvider.ParentOf
            Return ManagerPathRules.ParentOf(ResourceKind.Sftp, path)
        End Function

        Public Function RemoveEmptyDirectory(rootPath As String, path As String) As DirectoryRemoval Implements IManagerDirectoryProvider.RemoveEmptyDirectory
            Return DirectoryRemoval.Refused
        End Function

        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            CancelAction?.Invoke()
            ct.ThrowIfCancellationRequested()
            Dim content As Byte() = Files(Canonical(remotePath))
            If DownloadTransform IsNot Nothing Then content = DownloadTransform.Invoke(content)
            File.WriteAllBytes(localPath, content)
            progress?.Report(content.Length)
        End Sub

        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            If FailUpload Then Throw New IOException("The server refused the upload.")
            Dim content As Byte() = File.ReadAllBytes(localPath)
            If UploadTransform IsNot Nothing Then content = UploadTransform.Invoke(content)
            Dim target As String = Canonical(remotePath)
            Files(target) = content
            ' A lock upload is valued by the real clock of this run (the staleness rule reads it); a generation
            ' by the controlled one, one strictly later second per upload unless the test fixes the time.
            uploadClock = uploadClock.AddSeconds(1)
            Times(target) = If(target.EndsWith(BackupRemoteSet.LockSuffix, StringComparison.Ordinal),
                              Date.UtcNow, If(UploadTime.HasValue, UploadTime.Value, uploadClock))
            progress?.Report(content.Length)
        End Sub

        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            If String.Equals(DeleteRefusesPath, Canonical(remotePath), StringComparison.Ordinal) Then
                Throw New UnauthorizedAccessException("The server refused the removal.")
            End If
            Files.Remove(Canonical(remotePath))
            Times.Remove(Canonical(remotePath))
            Links.Remove(Canonical(remotePath))
        End Sub

        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Dim source As String = Canonical(remotePath)
            Dim target As String = Canonical(newRemotePath)
            If NameOfRefusedRename.Length > 0 AndAlso
               String.Equals(target.Substring(target.LastIndexOf("/"c) + 1), NameOfRefusedRename, StringComparison.Ordinal) Then
                Throw New IOException("The server refused the rename.")
            End If
            If Files.ContainsKey(target) OrElse Folders.Contains(target) OrElse Links.Contains(target) Then
                Throw New IOException("The rename target exists.")
            End If
            If Files.ContainsKey(source) Then
                Files(target) = Files(source)
                Times(target) = Times(source)
                Files.Remove(source)
                Times.Remove(source)
            ElseIf Links.Contains(source) Then
                Links.Remove(source)
                Links.Add(target)
            Else
                Throw New IOException("The rename source does not exist.")
            End If
        End Sub

        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Folders.Add(Canonical(remotePath))
        End Sub

    End Class

    ''' <summary>One staged backup run against one or two in-memory servers: the FileDO launch is faked to write the
    ''' local generation and its result event exactly as FileDO would, and the staged tree is captured while the run
    ''' is in flight (the staging folder is the fake's only chance to see it).</summary>
    Private NotInheritable Class Kit
        Implements IDisposable

        Friend ReadOnly ServerA As New FakeRemoteServer()
        Friend ReadOnly ServerB As New FakeRemoteServer()
        Friend ReadOnly Seen As New List(Of FillLaunch)()
        Friend ReadOnly Items As New List(Of ItemJournalEntry)()
        Friend ReadOnly StagedWhileRunning As New List(Of String)()
        Private ReadOnly temp As String = Path.Combine(Path.GetTempPath(), "fms-l343-" & Guid.NewGuid().ToString("N"))
        Private ReadOnly runs_ As String
        Private ReadOnly destination_ As String
        Private launchCount As Integer
        Private disposedValue As Boolean

        Friend Sub New(Optional destinationFolder As String = "/sets")
            Directory.CreateDirectory(temp)
            runs_ = Path.Combine(temp, "runs")
            destination_ = Path.Combine(temp, "destination")
            Directory.CreateDirectory(destination_)
            Services = New ScheduledServices With {
                .Clock = Function() New Date(2026, 11, 18, 3, 0, 0, DateTimeKind.Unspecified),
                .LocateFileDo = Function() "C:\stub\filedo.exe",
                .FileDoHasBackupGenerations = Function(exe As String) True,
                .FileDoHasBackupDatedRetention = Function(exe As String) True,
                .FileDoHasBackupSkipLinks = Function(exe As String) True,
                .BackupPassword = Function(id As String) "",
                .BackupRunFolder = Function() runs_,
                .ResourceFacts = AddressOf FactsOf,
                .OpenSide = Function(location As ScheduledLocation) As ScheduledSideOpen
                                Dim server As FakeRemoteServer = If(String.Equals(location.ResourceId, "r2", StringComparison.Ordinal), ServerB, ServerA)
                                Return ScheduledSideOpen.Opened(New ProviderSide(server, location.ResourceId,
                                    If(location.Path.Length > 0, location.Path, "/srv"), "NAS",
                                    New RecursionFlag With {.Value = True}, Nothing))
                            End Function,
                .LaunchFill = Function(launch As FillLaunch, ct As CancellationToken) As FillLaunchOutcome
                                  LaunchAsFileDo(launch)
                                  Return New FillLaunchOutcome With {.Started = True, .ExitCode = 0}
                              End Function,
                .ItemJournal = Sub(entry As ItemJournalEntry) Items.Add(entry)}
        End Sub

        Friend ReadOnly Services As ScheduledServices
        Friend ReadOnly Property Destination As String
            Get
                Return destination_
            End Get
        End Property

        Private Shared Function FactsOf(id As String) As ScheduledResourceFacts
            Return New ScheduledResourceFacts With {.Exists = True, .DisplayName = "NAS", .RootPath = If(String.Equals(id, "r2", StringComparison.Ordinal), "/sets", "/srv")}
        End Function

        ''' <summary>The fake FileDO: the staged tree is captured now (it is gone when the run ends), the generation
        ''' is written into the destination folder it was given, and the result event names it. Every run numbers from
        ''' 001 - the staging folder is empty, as it is on a real run.</summary>
        Private Sub LaunchAsFileDo(launch As FillLaunch)
            Seen.Add(launch)
            launchCount += 1
            Dim source As String = launch.Arguments(1)
            Dim destinationArg As String = launch.Arguments(3)
            Dim stemIndex As Integer = launch.Arguments.IndexOf("stem")
            Dim stem As String = launch.Arguments(stemIndex + 1)

            Dim stageDirName As String = Path.GetFileName(Path.GetDirectoryName(source))
            If stageDirName IsNot Nothing AndAlso stageDirName.StartsWith(BackupStagingFolders.Prefix, StringComparison.OrdinalIgnoreCase) Then
                For Each folder As String In Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                    StagedWhileRunning.Add("folder " & folder.Substring(source.Length + 1))
                Next
                For Each file As String In Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                    StagedWhileRunning.Add("file " & file.Substring(source.Length + 1))
                Next
            End If

            Dim eventsFile As String = launch.Arguments(launch.Arguments.IndexOf("--events") + 1)
            If launch.Arguments.Contains("keep-days") Then
                Dim stamp As String = BackupCommandLine.DatedArchiveName(stem, Services.Clock.Invoke(), launch.Arguments.Contains("sec"))
                File.WriteAllText(Path.Combine(destinationArg, stamp), "dated " & launchCount.ToString(Globalization.CultureInfo.InvariantCulture))
                Dim daysArg As String = launch.Arguments(launch.Arguments.IndexOf("keep-days") + 1)
                Dim monthlyPart As String = If(launch.Arguments.Contains("keep-monthly"), ",""monthlyKept"":0", "")
                File.WriteAllText(eventsFile,
                    "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""Done"",""numbers"":{""files"":6,""bytes"":4200,""retiredCount"":0,""keptCount"":1,""keepDays"":" &
                    daysArg & monthlyPart & "}}}" & vbLf)
            Else
                File.WriteAllText(Path.Combine(destinationArg, stem & "_001.zip"), "generation " & launchCount.ToString(Globalization.CultureInfo.InvariantCulture))
                File.WriteAllText(eventsFile,
                    "{""schemaVersion"":1,""kind"":""result"",""data"":{""verdict"":""Done"",""numbers"":{""files"":6,""bytes"":4200,""generation"":1,""retiredGeneration"":0,""keepCount"":" &
                    launch.Arguments(launch.Arguments.IndexOf("gen") + 1) & "}}}" & vbLf)
            End If
        End Sub

        Friend Function Run(op As ScheduledOperation, Optional ct As CancellationToken = Nothing) As ScheduledRunResult
            Return New ScheduledExecutor(Services).Run(op, ct)
        End Function

        Private Sub Dispose(disposing As Boolean)
            If disposedValue Then Return
            disposedValue = True
            Try
                Directory.Delete(temp, True)
            Catch
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    Private Shared Function SftpLocation(Optional path As String = "/srv/photos") As ScheduledLocation
        Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r1", .ResourceName = "NAS", .Path = path}
    End Function

    Private Shared Function SftpDestination(Optional path As String = "/sets") As ScheduledLocation
        Return New ScheduledLocation With {.Kind = ScheduledLocationKind.Resource, .ResourceId = "r2", .ResourceName = "NAS", .Path = path}
    End Function

    Private Shared Function BackupOp(Optional id As String = "b343cafe0123456789abcdef000000001") As ScheduledOperation
        Dim op As New ScheduledOperation With {.Id = id, .Name = "backup", .Action = ScheduledAction.Backup, .BackupKeepCount = 3}
        Return op
    End Function

    Private Shared Function LocalSource(ParamArray files As String()) As String
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-l343-src-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(folder)
        For Each name As String In files
            File.WriteAllText(Path.Combine(folder, name), name & " content")
        Next
        Return folder
    End Function

    ' --- the set rule, transcribed from the producer (FileDO backupGen*) ---------------------------------

    <Fact>
    Public Sub A_slot_is_the_whole_name_within_keep_and_the_producer_folds_case()
        Assert.Equal(3, BackupRemoteSet.SlotNumberOf("Photos-tag_003.zip", "Photos-tag", ".zip", 7))
        Assert.Equal(1, BackupRemoteSet.SlotNumberOf("photos-TAG_001.ZIP", "Photos-tag", ".zip", 7))
        Assert.Equal(0, BackupRemoteSet.SlotNumberOf("Photos-tag_004.zip", "Photos-tag", ".zip", 3))
        Assert.Equal(0, BackupRemoteSet.SlotNumberOf("Photos-tag_0030.zip", "Photos-tag", ".zip", 7))
        Assert.Equal(0, BackupRemoteSet.SlotNumberOf("Photos-tag_003.fd-sec", "Photos-tag", ".zip", 7))
        Assert.Equal(0, BackupRemoteSet.SlotNumberOf("Other_003.zip", "Photos-tag", ".zip", 7))
        Assert.Equal("Photos-tag_001.fd-sec", BackupRemoteSet.GenerationName("Photos-tag", 1, ".fd-sec"))
    End Sub

    <Fact>
    Public Sub Next_number_follows_the_newest_generation_and_wraps_after_keep()
        Dim keep As Integer = 3
        Dim [set] As Dictionary(Of Integer, BackupRemoteGeneration) = BackupRemoteSet.Scan(Entries(
            ("photos-tag_001.zip", 100, New Date(2026, 1, 1)), ("photos-tag_002.zip", 100, New Date(2026, 1, 2))),
            "photos-tag", ".zip", keep)
        Assert.Equal(3, BackupRemoteSet.NextNumber([set], keep))

        ' Equal times: the higher number is the newest, so the run after 002 wraps to 003 - not to the missing 001.
        Dim tied As Dictionary(Of Integer, BackupRemoteGeneration) = BackupRemoteSet.Scan(Entries(
            ("photos-tag_001.zip", 100, New Date(2026, 1, 1)), ("photos-tag_002.zip", 100, New Date(2026, 1, 1))),
            "photos-tag", ".zip", keep)
        Assert.Equal(2, BackupRemoteSet.NewestOf(tied).Number)
        Assert.Equal(3, BackupRemoteSet.NextNumber(tied, keep))

        Dim full As Dictionary(Of Integer, BackupRemoteGeneration) = BackupRemoteSet.Scan(Entries(
            ("photos-tag_001.zip", 100, New Date(2026, 1, 1)),
            ("photos-tag_002.zip", 100, New Date(2026, 1, 2)),
            ("photos-tag_003.zip", 100, New Date(2026, 1, 3))),
            "photos-tag", ".zip", keep)
        Assert.Equal(1, BackupRemoteSet.NextNumber(full, keep))
        Assert.Equal(1, BackupRemoteSet.NextNumber(New Dictionary(Of Integer, BackupRemoteGeneration)(), keep))
    End Sub

    Private Shared Function Entries(ParamArray rows As (Name As String, Length As Long, Written As Date)()) As List(Of ResourceEntry)
        Dim result As New List(Of ResourceEntry)()
        For Each row As (Name As String, Length As Long, Written As Date) In rows
            result.Add(New ResourceEntry With {.Name = row.Name, .FullPath = "/sets/" & row.Name,
                .Length = row.Length, .LastWriteUtc = DateTime.SpecifyKind(row.Written, DateTimeKind.Utc), .IsDirectory = False})
        Next
        Return result
    End Function

    <Fact>
    Public Sub A_folder_or_link_under_a_slot_name_is_not_a_generation_and_is_never_taken()
        Dim entries As New List(Of ResourceEntry) From {
            New ResourceEntry With {.Name = "photos-tag_001.zip", .FullPath = "/sets/photos-tag_001.zip", .IsDirectory = True},
            New ResourceEntry With {.Name = "photos-tag_002.zip", .FullPath = "/sets/photos-tag_002.zip", .IsLink = True}
        }
        Assert.Empty(BackupRemoteSet.Scan(entries, "photos-tag", ".zip", 3))
        Assert.True(BackupRemoteSet.ForeignHolder(entries, "photos-tag_001.zip"))
        Assert.False(BackupRemoteSet.ForeignHolder(entries, "photos-tag_003.zip"))
        Assert.True(BackupRemoteSet.IsSameOrInside("/srv/photos", "/srv/photos"))
        Assert.True(BackupRemoteSet.IsSameOrInside("/srv/photos/sub", "/srv/photos"))
        Assert.False(BackupRemoteSet.IsSameOrInside("/srv/photo", "/srv/photos"))
    End Sub

    ' --- the staged source ---------------------------------------------------------------------------------

    <Fact>
    Public Sub An_sftp_source_is_staged_whole_with_empty_folders_and_the_run_speaks_the_remote_side()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.PutFolder("/srv/photos/one")
            kit.ServerA.Put("/srv/photos/one/a.txt", "a")
            kit.ServerA.PutFolder("/srv/photos/empty")
            kit.ServerA.PutFolder("/srv/photos/empty/deeper")
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op)

            Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
            Assert.Contains("file one\a.txt", kit.StagedWhileRunning)
            Assert.Contains("folder empty", kit.StagedWhileRunning)
            Assert.Contains("folder empty\deeper", kit.StagedWhileRunning)
            Assert.Single(kit.Seen)
            Dim sourceArg As String = kit.Seen(0).Arguments(1)
            Assert.StartsWith(BackupStagingFolders.Prefix, Path.GetFileName(Path.GetDirectoryName(sourceArg)), StringComparison.OrdinalIgnoreCase)
            ' The staging folder is this run's own and is gone when the run ends.
            Assert.Empty(BackupStagingFolders.OwnFolders(op.Id))
            Assert.Contains(kit.Items, Function(e) e.Verb = "backup" AndAlso e.Verdict = "done" AndAlso e.Source.Contains("NAS"))
            Assert.True(File.Exists(result.ArchivePath))
        End Using
    End Sub

    <Fact>
    Public Sub A_truncated_listing_of_the_source_refuses_the_run_whole()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "a")
            kit.ServerA.Put("/srv/photos/b.txt", "b")
            kit.ServerA.TruncateAt = 1
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-listing|", result.Detail)
            Assert.Empty(Directory.GetFiles(kit.Destination))
        End Using
    End Sub

    <Fact>
    Public Sub A_link_under_the_source_refuses_the_run_whole()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "a")
            kit.ServerA.PutLink("/srv/photos/elsewhere")
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-link|", result.Detail)
            Assert.Empty(Directory.GetFiles(kit.Destination))
        End Using
    End Sub

    <Fact>
    Public Sub A_name_windows_cannot_hold_refuses_the_run_whole()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/bad:name.txt", "no")
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-name|", result.Detail)
            Assert.Empty(Directory.GetFiles(kit.Destination))
        End Using
    End Sub

    <Fact>
    Public Sub A_source_that_changes_under_the_download_is_refused_not_backed_up()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "1234567890")
            kit.ServerA.DownloadTransform = Function(content As Byte()) content.Take(4).ToArray()
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-source-changed|", result.Detail)
            Assert.Empty(Directory.GetFiles(kit.Destination))
        End Using
    End Sub

    <Fact>
    Public Sub A_cancellation_ends_the_run_as_cancelled_with_nothing_published_and_nothing_left_behind()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "a")
            Dim cts As New CancellationTokenSource()
            kit.ServerA.CancelAction = Sub() cts.Cancel()
            Dim op As ScheduledOperation = BackupOp()
            op.Source = SftpLocation()
            op.Destination.Path = kit.Destination
            Dim result As ScheduledRunResult = kit.Run(op, cts.Token)
            Assert.Equal(ScheduledRunOutcome.Cancelled, result.Outcome)
            Assert.Empty(BackupStagingFolders.OwnFolders(op.Id))
            Assert.Empty(Directory.GetFiles(kit.Destination))
        End Using
    End Sub

    <Fact>
    Public Sub A_source_the_volume_cannot_hold_never_starts_the_download()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "12345678")
            Dim side As New ProviderSide(kit.ServerA, "r1", "/srv/photos", "NAS", New RecursionFlag With {.Value = True}, Nothing)
            Dim stage As New BackupRemoteStage With {
                .FreeBytesOf = Function(folder As String) New Long?(1024L)
            }
            Dim staged As BackupStageResult = stage.Stage(side, Path.Combine(kit.Destination, "stage"), CancellationToken.None)
            Assert.True(staged.Refused)
            Assert.Equal(BackupRemoteRefusal.Space, staged.Code)
            Assert.StartsWith("disk-full|", staged.Detail)
        End Using
    End Sub

    ' --- the remote set ------------------------------------------------------------------------------------

    Private Shared Function PublishOp(Optional keep As Integer = 3) As ScheduledOperation
        Dim op As ScheduledOperation = BackupOp()
        op.BackupKeepCount = keep
        op.Source.Path = LocalSource("a.txt")
        op.Destination = SftpDestination()
        Return op
    End Function

    Private Shared Function NamesOn(server As FakeRemoteServer) As List(Of String)
        Return server.Files.Keys.Select(Function(p As String) p.Substring(p.LastIndexOf("/"c) + 1)).OrderBy(Function(n As String) n).ToList()
    End Function

    <Fact>
    Public Sub A_local_generation_publishes_into_the_remote_set_and_wraps_by_the_published_rule()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            Dim stem As String = op.BackupStem
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
            Assert.EndsWith("_001.zip", result.ArchivePath)
            Assert.Contains("gen=1;", result.Verdict)
            ' The lock is gone, and only the generation holds the stem.
            Assert.Equal(New String() {stem & "_001.zip"}, NamesOn(kit.ServerB))

            ' Numbers follow the destination's own listing: 002, 003, then the wrap replaces the slot of the newest.
            kit.Run(op)
            kit.Run(op)
            Dim wrapped As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Ok, wrapped.Outcome)
            Assert.Equal(New String() {stem & "_001.zip", stem & "_002.zip", stem & "_003.zip"}, NamesOn(kit.ServerB))
            ' The wrap replaced the slot the rule names: the retirement is journaled as a replacement, one per run.
            Assert.Contains(kit.Items, Function(e) e.Verb = "backup-retire" AndAlso e.Verdict = "replaced")
            Assert.Single(kit.Items, Function(e) e.Verb = "backup-retire")
            Dim retiredOnce As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Ok, retiredOnce.Outcome)
            Assert.Equal(New String() {stem & "_001.zip", stem & "_002.zip", stem & "_003.zip"}, NamesOn(kit.ServerB))
            ' Only the set's own files are there after every run - the lock never stays behind.
            Assert.DoesNotContain(NamesOn(kit.ServerB), Function(n As String) n.EndsWith(BackupRemoteSet.LockSuffix, StringComparison.Ordinal))
        End Using
    End Sub

    <Fact>
    Public Sub The_wrap_never_touches_other_stems_other_extensions_or_out_of_range_slots()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            Dim stem As String = op.BackupStem
            kit.ServerB.Put("/sets/" & stem & "_001.zip", "old 001", New Date(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))
            kit.ServerB.Put("/sets/other-stem_001.zip", "another set", New Date(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc))
            kit.ServerB.Put("/sets/" & stem & "_004.zip", "out of range", New Date(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc))
            kit.ServerB.Put("/sets/" & stem & "_001.fd-sec", "sealed set", New Date(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc))

            For i As Integer = 1 To 3
                Assert.Equal(ScheduledRunOutcome.Ok, kit.Run(op).Outcome)
            Next
            ' The remote set filled 002 and 003 and wrapped into 001 by replacement; the neighbours stand untouched.
            Dim names As List(Of String) = NamesOn(kit.ServerB)
            Assert.Contains(stem & "_001.zip", names)
            Assert.Contains(stem & "_002.zip", names)
            Assert.Contains(stem & "_003.zip", names)
            Assert.Equal("another set", System.Text.Encoding.UTF8.GetString(kit.ServerB.Files("/sets/other-stem_001.zip")))
            Assert.Equal("out of range", System.Text.Encoding.UTF8.GetString(kit.ServerB.Files("/sets/" & stem & "_004.zip")))
            Assert.Equal("sealed set", System.Text.Encoding.UTF8.GetString(kit.ServerB.Files("/sets/" & stem & "_001.fd-sec")))
        End Using
    End Sub

    <Fact>
    Public Sub A_corrupted_upload_is_refused_by_the_read_back_and_the_set_stays_as_it_was()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            kit.ServerB.UploadTransform = Function(content As Byte()) content.Take(content.Length - 1).ToArray()
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-verify|", result.Detail)
            Assert.DoesNotContain(kit.ServerB.Files.Keys, Function(p As String) p.StartsWith("/sets/" & op.BackupStem & "_"))
        End Using
    End Sub

    <Fact>
    Public Sub An_observable_change_between_the_check_and_the_mutation_refuses_without_publication()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            ' The publication lists the set twice (its plan and its mutation check); a change in between refuses.
            kit.ServerB.OnNthList = Sub(n As Integer)
                                       If n = 1 Then kit.ServerB.Put("/sets/" & op.BackupStem & "_002.zip", "someone else")
                                   End Sub
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-changed|", result.Detail)
            ' Nothing of ours was published or removed; the other client's file stands.
            Assert.DoesNotContain("/sets/" & op.BackupStem & "_001.zip", kit.ServerB.Files.Keys)
            Assert.Contains("/sets/" & op.BackupStem & "_002.zip", kit.ServerB.Files.Keys)
        End Using
    End Sub

    <Fact>
    Public Sub Another_run_fresh_lock_refuses_this_one_and_a_stale_lock_is_recovered()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            kit.ServerB.Put("/sets/" & BackupRemoteSet.LockName(op.BackupStem), "held", Date.UtcNow)
            Dim refused As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, refused.Outcome)
            Assert.StartsWith("backup-remote-locked|", refused.Detail)
            Assert.DoesNotContain("/sets/" & op.BackupStem & "_001.zip", kit.ServerB.Files.Keys)

            ' A lock older than the stale window is a crashed run's leftover and goes.
            kit.ServerB.Put("/sets/" & BackupRemoteSet.LockName(op.BackupStem), "stale", Date.UtcNow.AddHours(-2))
            Dim recovered As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Ok, recovered.Outcome)
            Assert.Contains("/sets/" & op.BackupStem & "_001.zip", kit.ServerB.Files.Keys)
            Assert.DoesNotContain(NamesOn(kit.ServerB), Function(n As String) n.EndsWith(BackupRemoteSet.LockSuffix, StringComparison.Ordinal))
        End Using
    End Sub

    <Fact>
    Public Sub A_generation_not_provably_later_than_a_kept_one_stops_the_run_before_anything_moves()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            Dim existing As New Date(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc)
            kit.ServerB.Put("/sets/" & op.BackupStem & "_001.zip", "previous", existing)
            kit.ServerB.UploadTime = existing
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-time|", result.Detail)
            Assert.Equal("previous", System.Text.Encoding.UTF8.GetString(kit.ServerB.Files("/sets/" & op.BackupStem & "_001.zip")))
            Assert.Single(kit.ServerB.Files)
        End Using
    End Sub

    <Fact>
    Public Sub A_foreign_holder_of_the_slot_name_is_never_replaced_or_removed()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            kit.ServerB.PutFolder("/sets/" & op.BackupStem & "_001.zip")
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-place|", result.Detail)
            Assert.Empty(kit.ServerB.Files)
        End Using
    End Sub

    <Fact>
    Public Sub A_rename_failure_keeps_the_verified_partial_and_names_it()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            Dim stem As String = op.BackupStem
            kit.ServerB.Put("/sets/" & stem & "_001.zip", "previous", New Date(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ' The rename into place fails; the verified generation stays under its partial name.
            kit.ServerB.NameOfRefusedRename = stem & "_002.zip"
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-move|", result.Detail)
            Assert.Contains(stem & "_002.zip" & BackupRemoteSet.PartialSuffix, result.Detail)
            Assert.Contains("/sets/" & stem & "_002.zip.fms-part", kit.ServerB.Files.Keys)
            Assert.Contains("/sets/" & stem & "_001.zip", kit.ServerB.Files.Keys)
        End Using
    End Sub

    <Fact>
    Public Sub A_removal_the_server_refuses_lets_the_last_good_generation_stand()
        Using kit As New Kit()
            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = PublishOp()
            Dim stem As String = op.BackupStem
            kit.ServerB.Put("/sets/" & stem & "_001.zip", "previous", New Date(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            kit.ServerB.Put("/sets/" & stem & "_002.zip", "previous", New Date(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))
            kit.ServerB.Put("/sets/" & stem & "_003.zip", "previous", New Date(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc))
            ' The wrap retires the slot of the newest (001) by replacement; the server refuses that removal.
            kit.ServerB.DeleteRefusesPath = "/sets/" & stem & "_001.zip"
            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Wall, result.Outcome)
            Assert.StartsWith("backup-remote-transport|", result.Detail)
            Assert.Equal("previous", System.Text.Encoding.UTF8.GetString(kit.ServerB.Files("/sets/" & stem & "_001.zip")))
            Assert.Equal(3, kit.ServerB.Files.Count)
        End Using
    End Sub

    <Fact>
    Public Sub Dated_remote_selection_keeps_cutoff_and_earliest_per_month_and_skip_links_stages()
        Using kit As New Kit()
            kit.ServerA.PutFolder("/srv/photos")
            kit.ServerA.Put("/srv/photos/a.txt", "data")
            kit.ServerA.PutLink("/srv/photos/linkdir")
            Dim sourceSide As New ProviderSide(kit.ServerA, "r1", "/srv/photos", "NAS", New RecursionFlag With {.Value = True}, Nothing)
            Dim staged As BackupStageResult = New BackupRemoteStage().Stage(sourceSide, Path.Combine(kit.Destination, "stage"), CancellationToken.None, skipLinks:=True)
            Assert.False(staged.Refused)
            Assert.Single(staged.SkippedLinks)
            Assert.Equal("linkdir", staged.SkippedLinks(0))

            kit.ServerB.PutFolder("/sets")
            Dim op As ScheduledOperation = BackupOp()
            op.BackupRetention = BackupRetentionMode.Dated
            op.BackupKeepDays = 7
            op.BackupKeepMonthly = True
            op.BackupSkipLinks = True
            op.Source = SftpLocation()
            op.Destination = SftpDestination()
            Dim stem As String = op.BackupStem

            kit.ServerB.Put("/sets/" & stem & "-2610011300.zip", "oct-first")
            kit.ServerB.Put("/sets/" & stem & "-2610051300.zip", "oct-old")
            kit.ServerB.Put("/sets/" & stem & "-2611151300.zip", "recent")
            kit.ServerB.Put("/sets/" & stem & "-2602291300.zip", "invalid-leap-date")

            Dim result As ScheduledRunResult = kit.Run(op)
            Assert.Equal(ScheduledRunOutcome.Ok, result.Outcome)
            Assert.Equal(1, result.Skipped)
            Dim names As List(Of String) = NamesOn(kit.ServerB)
            Assert.Contains(stem & "-2610011300.zip", names)
            Assert.DoesNotContain(stem & "-2610051300.zip", names)
            Assert.Contains(stem & "-2611151300.zip", names)
            Assert.Contains(stem & "-2611180300.zip", names)
            Assert.Contains(stem & "-2602291300.zip", names)
            Assert.Contains(kit.Items, Function(e) e.Verb = "backup-retire" AndAlso e.Source = "/sets/" & stem & "-2610051300.zip" AndAlso e.Verdict = "retired")
            Assert.Contains(kit.Items, Function(e) e.Verb = "backup-skip-link" AndAlso e.Source = "linkdir" AndAlso e.Verdict = "skipped")
        End Using
    End Sub

End Class
