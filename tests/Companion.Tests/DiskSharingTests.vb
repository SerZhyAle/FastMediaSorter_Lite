Option Strict On

Imports System.Text
Imports System.Text.Json
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit
Imports System.IO
Imports Microsoft.Win32

<Collection(UiLanguageCollection.Name)>
Public Class DiskSharingTests
    <Theory>
    <InlineData("disk-share")>
    <InlineData("disk-open")>
    <InlineData("disk-close")>
    <InlineData("disk-autostart")>
    <InlineData("disk-status")>
    <InlineData("disk-list")>
    Public Sub DiskRequestsUseTheFlatSchemaTwoEnvelope(kind As String)
        Dim request As New WorkerRequest With {.type = kind, .owner = "filedo", .containerPath = "C:\sample.fdd",
            .rootName = "Sample", .action = "share", .readOnly = True, .enable = False, .drainTimeout = 30}
        Using doc As JsonDocument = JsonDocument.Parse(WorkerIpc.SerializeRequest(request))
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32())
            Assert.Equal("filedo", doc.RootElement.GetProperty("owner").GetString())
            Assert.Equal("C:\sample.fdd", doc.RootElement.GetProperty("containerPath").GetString())
            Assert.False(doc.RootElement.GetProperty("enable").GetBoolean())
            Assert.True(doc.RootElement.GetProperty("readOnly").GetBoolean())
            Dim unused As JsonElement
            Assert.False(doc.RootElement.TryGetProperty("request", unused))
            Assert.False(doc.RootElement.TryGetProperty("password", unused))
        End Using
    End Sub

    <Fact>
    Public Sub DiskVersionSupportDoesNotChangeLegacyVersionRefusal()
        Const response As String = "{""schemaVersion"":2,""ok"":true,""disks"":[]}"
        Assert.Throws(Of WorkerVersionRefusedException)(Sub() WorkerIpc.ParseResponse(response))
        Assert.True(WorkerIpc.ParseResponse(response, 2).Ok)
        Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true}", 2))
        Assert.False(WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":false,""error"":""unsupported""}", 2).Ok)
    End Sub

    <Fact>
    Public Sub LegacyStatusPreservesExternalOwnershipAndCapabilities()
        Dim response As WorkerResponse = WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true,""status"":{""capabilities"":{""disk-sharing"":true},""roots"":[{""name"":""Disk"",""owner"":""filedo"",""readOnly"":true},{""name"":""Folder""},{""name"":""Other"",""owner"":""other""}]}}")
        Assert.True(DiskSharing.Supported(response.Status))
        Dim own As List(Of ShareFolder) = ShareController.FmsFoldersOnly(response.Status.Roots)
        Assert.Single(own)
        Assert.Equal("Folder", own(0).name)
        Assert.True(response.Status.Roots(0).readOnly)
        Assert.False(response.Status.Roots(2).IsFmsOwned)
    End Sub

    Private Const HostingHint As String = "Для монтирования .fdd нужны права администратора. Служба LocalService подключение дисков не поддерживает. Уже смонтированный диск можно добавить отдельно."

    <Theory>
    <InlineData("service FileDO must be inside its Program Files installation")>
    <InlineData("bundled FileDO is missing")>
    <InlineData("bundled FileDO predates disk sharing; update FMS for Windows")>
    Public Sub UnavailableText_NamesTheWorkersOwnReason_AndNothingGeneric(reason As String)
        Dim status As New WorkerStatus With {.Mode = "service", .DiskSharingError = reason}
        Dim text As String = DiskSharing.UnavailableText(status)
        Assert.Contains(reason, text)
        ' The account hint is about ONE cause. Pinned under every reason it blamed LocalService
        ' for a placement or a missing bundle, which the owner could not act on.
        Assert.DoesNotContain(Localization.T(HostingHint), text)
    End Sub

    <Fact>
    Public Sub UnavailableText_KeepsTheAccountHintOnlyForTheAccountRefusal()
        Dim status As New WorkerStatus With {.Mode = "service",
            .DiskSharingError = "disk sharing requires a LocalSystem holder; configure the Server service account before enabling disk support"}
        Dim text As String = DiskSharing.UnavailableText(status)
        Assert.Contains("LocalSystem holder", text)
        Assert.Contains(Localization.T(HostingHint), text)
    End Sub

    <Fact>
    Public Sub UnavailableText_WithoutAStatus_SaysTheCompanionDidNotAnswer()
        Dim text As String = DiskSharing.UnavailableText(Nothing)
        Assert.Contains(Localization.T("Не удалось связаться с компаньоном."), text)
    End Sub

    <Fact>
    Public Sub UnavailableText_StripsControlCharactersFromTheReason()
        Dim text As String = DiskSharing.UnavailableText(New WorkerStatus With {.DiskSharingError = "one" & vbLf & "two"})
        Assert.Contains("one two", text)
    End Sub

    <Theory>
    <InlineData("filedo")>
    <InlineData("unknown")>
    Public Async Function RemovingTheLastFolderPreservesOtherOwners(owner As String) As Task
        Using stub As New StubWorker(Function(kind) "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""roots"":[{""owner"":""" & owner & """,""name"":""External"",""hostPath"":""C:\\mount""}]}}")
            Dim result As New ShareController.ShareResult()
            Await ShareController.UnshareAsync(result, stub.PipeName)
            Assert.DoesNotContain("StopServer", stub.Seen())
            Assert.True(result.Status.Running)
        End Using
    End Function

    <Fact>
    Public Async Function DiskRequestsReachTheWorkerWithOwnerAndClearTheCredential() As Task
        Using stub As New StubWorker(Function(kind) "{""schemaVersion"":2,""ok"":true,""disk"":{""containerId"":""sample"",""state"":""open"",""readOnly"":true}}")
            Dim request As New WorkerRequest With {.type = "disk-open", .containerPath = "C:\sample.fdd", .password = "synthetic-test-key"}
            Dim result As WorkerResponse = Await DiskSharing.SendAsync(request, stub.PipeName)
            Assert.True(result.Disk.ReadOnly)
            Assert.Equal("open", result.Disk.State)
            Assert.Null(request.password)
            Using doc As JsonDocument = JsonDocument.Parse(stub.SeenRequests()(0))
                Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32())
                Assert.Equal("filedo", doc.RootElement.GetProperty("owner").GetString())
            End Using
        End Using
    End Function

    <Fact>
    Public Sub DriveAliasTargetsPreserveThePrivateMountAndVolumeGuid()
        Assert.Equal("\??\C:\mounts\sample", DiskDriveLetters.TargetFor("C:\mounts\sample\"))
        Assert.Equal("\??\Volume{sample}", DiskDriveLetters.TargetFor("\\?\Volume{sample}\"))
        Assert.Throws(Of ArgumentException)(Sub() DiskDriveLetters.TargetFor("relative"))
    End Sub

    <Fact>
    Public Sub DiskErrorsAreBoundedAndCannotInsertLogLines()
        Assert.Equal("one two", DiskSharing.SafeError("one" & vbLf & "two"))
        Assert.Equal(1024, DiskSharing.SafeError(New String("x"c, 2000)).Length)
    End Sub

    <Fact>
    Public Sub OversizedDiskListsAreRefused()
        Dim records As String = String.Join(",", Enumerable.Repeat("{}", 1025))
        Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true,""disks"":[" & records & "]}", 2))
    End Sub

    ' --- an absent "disks" is an empty list (the worker omits an empty one: omitempty on its Response) -------

    ''' <summary>The Go worker's real answer for "no disk is shared": <c>disks</c> is OMITTED, not <c>[]</c>.
    ''' An ok:true schema-2 answer without the member is zero disks - not "Missing disk list.", which is what
    ''' the Disks window threw and what made the tray and the main window skip their reconcile.</summary>
    <Fact>
    Public Sub AbsentDisks_OnAnOkSchemaTwoAnswer_IsAnEmptyList()
        Dim parsed As WorkerResponse = WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true}", 2)
        Assert.True(parsed.Ok)
        Assert.Null(parsed.Disks)   ' the wire form: the member is not there at all
        Dim disks As List(Of SharedDisk) = DiskSharing.DisksOf(parsed)
        Assert.NotNull(disks)
        Assert.Empty(disks)
    End Sub

    <Fact>
    Public Sub AnExplicitEmptyList_AndARealList_AreReadAsTheyAre()
        Assert.Empty(DiskSharing.DisksOf(WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true,""disks"":[]}", 2)))
        Dim two As List(Of SharedDisk) = DiskSharing.DisksOf(WorkerIpc.ParseResponse(
            "{""schemaVersion"":2,""ok"":true,""disks"":[{""rootName"":""A"",""state"":""open""},{""rootName"":""B"",""state"":""closed""}]}", 2))
        Assert.Equal(2, two.Count)
        Assert.Equal("A", two(0).RootName)
    End Sub

    ''' <summary>The relaxation is for an ok schema-2 answer ONLY: a refusal has no list to show, and the
    ''' schema-2 requirement and the 1024-record bound of the parser still hold.</summary>
    <Fact>
    Public Sub AbsentDisks_IsNotAnEmptyList_WhenTheAnswerIsNotOkSchemaTwo()
        Assert.Null(DiskSharing.DisksOf(Nothing))
        Assert.Null(DiskSharing.DisksOf(WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":false,""error"":""unsupported""}", 2)))
        Assert.Null(DiskSharing.DisksOf(New WorkerResponse With {.SchemaVersion = 1, .Ok = True}))
        Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse("{""schemaVersion"":1,""ok"":true}", 2))
        Assert.Throws(Of WorkerVersionRefusedException)(Sub() WorkerIpc.ParseResponse("{""schemaVersion"":3,""ok"":true}", 2))
        Dim records As String = String.Join(",", Enumerable.Repeat("{}", 1025))
        Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true,""disks"":[" & records & "]}", 2))
    End Sub

    ''' <summary>What the tray, the Disks window and the main window do with it: the empty list reconciles, so
    ''' a drive alias whose disk is no longer shared is released instead of staying for the whole session.</summary>
    <Fact>
    Public Sub AbsentDisks_ReconcilesAnAliasAway()
        Dim directory As String = Path.Combine(Path.GetTempPath(), "fms-disk-alias-" & Guid.NewGuid().ToString("N"))
        Dim storePath As String = "Software\SZA\FastMediaSorter\Tests\" & Guid.NewGuid().ToString("N")
        Dim disk As New SharedDisk With {.ContainerId = Guid.NewGuid().ToString("N"), .State = "open", .MountPath = directory}
        Dim letter As String = DiskDriveLetters.FreeLetters().Last()
        IO.Directory.CreateDirectory(directory)
        Try
            DiskDriveLetters.Attach(disk, letter, storePath)
            Assert.NotEqual("", DiskDriveLetters.CurrentTarget(letter))
            Dim none As List(Of SharedDisk) = DiskSharing.DisksOf(WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true}", 2))
            DiskDriveLetters.Reconcile(none, storePath)
            Assert.Equal("", DiskDriveLetters.CurrentTarget(letter))
        Finally
            DiskDriveLetters.Reconcile(Array.Empty(Of SharedDisk)(), storePath)
            Registry.CurrentUser.DeleteSubKey(storePath, False)
            IO.Directory.Delete(directory)
        End Try
    End Sub

    <Fact>
    Public Sub AWindowsDriveAliasReadsTheMountAndIsRemovedOnlyAfterClose()
        Dim directory As String = Path.Combine(Path.GetTempPath(), "fms-disk-alias-" & Guid.NewGuid().ToString("N"))
        Dim storePath As String = "Software\SZA\FastMediaSorter\Tests\" & Guid.NewGuid().ToString("N")
        Dim disk As New SharedDisk With {.ContainerId = Guid.NewGuid().ToString("N"), .State = "open", .MountPath = directory}
        Dim letter As String = DiskDriveLetters.FreeLetters().Last()
        IO.Directory.CreateDirectory(directory)
        File.WriteAllText(Path.Combine(directory, "sample.txt"), "synthetic-data")
        Try
            DiskDriveLetters.Attach(disk, letter, storePath)
            Assert.Equal("synthetic-data", File.ReadAllText(letter & "\sample.txt"))
            Assert.Throws(Of IOException)(Sub() DiskDriveLetters.Attach(disk, letter, storePath))
            DiskDriveLetters.Reconcile({disk}, storePath)
            Assert.NotEqual("", DiskDriveLetters.CurrentTarget(letter))
            disk.State = "closed"
            DiskDriveLetters.Reconcile({disk}, storePath)
            Assert.Equal("", DiskDriveLetters.CurrentTarget(letter))
        Finally
            DiskDriveLetters.Reconcile(Array.Empty(Of SharedDisk)(), storePath)
            Registry.CurrentUser.DeleteSubKey(storePath, False)
            File.Delete(Path.Combine(directory, "sample.txt"))
            IO.Directory.Delete(directory)
        End Try
    End Sub

    <Fact>
    Public Sub FileDoLinksPointToCanonicalDestinations()
        Assert.Equal("https://serzhyale.github.io/FileDO/guides/virtual-disks.html", ShareLinks.FileDoDisksGuideUrl)
        Assert.Equal("https://serzhyale.github.io/FileDO/", ShareLinks.FileDoSiteUrl)
        Assert.Equal("https://serzhyale.github.io/FileDO/guides/fms-sharing.html", ShareLinks.FileDoSharingGuideUrl)
        Assert.Equal(ShareLinks.FileDoDisksGuideUrl, ShareGuide.FileDoDisksGuideUrl)
        Assert.Equal(ShareLinks.FileDoSiteUrl, ShareGuide.FileDoSiteUrl)
        Assert.Equal(ShareLinks.FileDoSharingGuideUrl, ShareGuide.FileDoSharingGuideUrl)
    End Sub

    <Fact>
    Public Sub DiskLinksAreLocalizedInAllSupportedLanguages()
        Dim dict As Dictionary(Of String, String()) = Localization.All.ToDictionary(Function(kv) kv.Key, Function(kv) kv.Value, StringComparer.Ordinal)
        Dim guideKey As String = "Инструкция: диски FileDO"
        Dim siteKey As String = "Сайт FileDO"
        Assert.True(dict.ContainsKey(guideKey))
        Assert.True(dict.ContainsKey(siteKey))
        Assert.Equal(12, dict(guideKey).Length)
        Assert.Equal(12, dict(siteKey).Length)
        For Each translation In dict(guideKey)
            Assert.False(String.IsNullOrWhiteSpace(translation))
        Next
        For Each translation In dict(siteKey)
            Assert.False(String.IsNullOrWhiteSpace(translation))
        Next
    End Sub
End Class
