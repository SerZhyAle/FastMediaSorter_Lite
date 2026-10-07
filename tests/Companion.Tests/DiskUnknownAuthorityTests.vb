Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-IPC section 10 item D (0.14), the consumer's side: "an unknown or absent disk state, mode, holder or
''' record is unknown and never authorizes a mount or dropping a holder". Unknown is never "none" and never
''' "closed": the Share Manager's disk surfaces (the ".fdd disks.." window, the list's right-click menu, the
''' drive-letter alias) read the six states, the three holders and the two modes of DISK-SHARE section 16 by
''' name, and anything else - another spelling, another case, an absent member, an absent record - is a reason
''' to wait for the next honest answer, never a default that opens something.
'''
''' The pure helpers are asserted directly; the verbs that send a request are asserted to refuse BEFORE a byte is
''' written (an unknown state must not reach the pipe at all). The worker stays the authority and refuses on its
''' own as well - this is the host not asking on the strength of a guess.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class DiskUnknownAuthorityTests

    Private Shared Function Disk(state As String, Optional holder As String = "none", Optional encrypted As Boolean = False) As SharedDisk
        Return New SharedDisk With {.ContainerId = "c1", .ContainerPath = "C:\sample.fdd", .RootName = "Sample", .State = state, .Holder = holder, .Encrypted = encrypted}
    End Function

    ' --- state --------------------------------------------------------------------------------

    <Theory>
    <InlineData("closed")>
    <InlineData("failed")>
    <InlineData("locked")>
    Public Sub AKnownNotOpenState_CanBeOpened(state As String)
        Assert.True(DiskOperations.CanOpen(Disk(state)))
    End Sub

    <Theory>
    <InlineData("open")>
    <InlineData("opening")>
    <InlineData("closing")>
    Public Sub AnOpenOrInFlightState_CannotBeOpened(state As String)
        Assert.False(DiskOperations.CanOpen(Disk(state)))
    End Sub

    ''' <summary>The one that used to pass: "not one of the three states I recognise" enabled Open.</summary>
    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData(" ")>
    <InlineData("mounted")>
    <InlineData("frozen")>
    <InlineData("OPEN")>
    <InlineData("Closed")>
    <InlineData("closed ")>
    Public Sub AnUnknownOrAbsentState_NeverEnablesOpen_AndNeverClose(state As String)
        Assert.False(DiskOperations.CanOpen(Disk(state)))
        Assert.False(DiskOperations.CanClose(Disk(state)))
        Assert.False(DiskOperations.IsKnownState(state))
    End Sub

    <Fact>
    Public Sub AnAbsentRecord_EnablesNothing()
        Assert.False(DiskOperations.CanOpen(Nothing))
        Assert.False(DiskOperations.CanClose(Nothing))
    End Sub

    <Fact>
    Public Sub OnlyAnOpenDisk_CanBeClosed()
        Assert.True(DiskOperations.CanClose(Disk("open")))
        For Each state As String In {"closed", "opening", "closing", "failed", "locked"}
            Assert.False(DiskOperations.CanClose(Disk(state)), state)
        Next
    End Sub

    ''' <summary>The state column: the six names are named, and anything else says "Unknown" - not "Error",
    ''' which is what the catch-all used to answer and which is a claim about the disk.</summary>
    <Fact>
    Public Sub TheStateColumn_SaysUnknownForWhatItDoesNotKnow()
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Assert.Equal("Error", Share_Disks_Form.StateText("failed"))
            For Each unknown As String In {Nothing, "", "mounted", "FAILED", "frozen"}
                Assert.Equal("Unknown", Share_Disks_Form.StateText(unknown))
            Next
            Assert.Equal("Open", Share_Disks_Form.StateText("open"))
            Assert.Equal("Closed", Share_Disks_Form.StateText("closed"))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- holder -------------------------------------------------------------------------------

    ''' <summary>Unknown is never none: a holder the host does not know, or none in the record, is shown as
    ''' Unknown - the list must not say "No" beside a disk somebody may be holding.</summary>
    <Fact>
    Public Sub AnUnknownOrAbsentHolder_IsNeverShownAsNoHolder()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                Dim none As String = Share_Disks_Form.HolderText("none")
                Dim unknown As String = Localization.T("Неизвестно")
                Assert.NotEqual(none, unknown)
                For Each holder As String In {Nothing, "", "ghost", "NONE", "Service", "filedo", "fms"}
                    Assert.Equal(unknown, Share_Disks_Form.HolderText(holder))
                Next
                Assert.Equal(Localization.T("Служба Windows"), Share_Disks_Form.HolderText("service"))
                Assert.Equal(Localization.T("Текущий сеанс"), Share_Disks_Form.HolderText("session"))
            Next
            Localization.CurrentCode = "en"
            Assert.Equal("No", Share_Disks_Form.HolderText("none"))
            Assert.Equal("Unknown", Share_Disks_Form.HolderText("ghost"))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the verbs refuse BEFORE a byte reaches the worker -----------------------------------------

    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("mounted")>
    <InlineData("OPEN")>
    Public Async Function OpeningAnUnknownState_IsRefusedWithoutAskingTheWorker(state As String) As Task
        Dim refused As DiskOperationRefusedException = Await Assert.ThrowsAsync(Of DiskOperationRefusedException)(
            Function() DiskOperations.OpenAsync(Disk(state), "pw", serverRunning:=True))
        Assert.Equal(Localization.T("Состояние диска неизвестно - обновите список и повторите."), refused.Reason)
    End Function

    <Fact>
    Public Async Function OpeningAnAbsentRecord_IsRefused() As Task
        Await Assert.ThrowsAsync(Of DiskOperationRefusedException)(Function() DiskOperations.OpenAsync(Nothing, "pw", serverRunning:=True))
    End Function

    ''' <summary>Turning autostart on for an encrypted disk OPENS it once to prove the password: that open is a
    ''' mount, so an unknown state refuses the whole verb before the proof is attempted.</summary>
    <Theory>
    <InlineData(Nothing)>
    <InlineData("mounted")>
    Public Async Function EnablingAutostartForAnEncryptedDiskOfUnknownState_IsRefused(state As String) As Task
        Await Assert.ThrowsAsync(Of DiskOperationRefusedException)(
            Function() DiskOperations.EnableAutostartAsync(Disk(state, encrypted:=True), "pw"))
    End Function

    ''' <summary>The drive-letter alias is made from an open disk's mount and from nothing else.</summary>
    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("mounted")>
    <InlineData("closed")>
    <InlineData("opening")>
    Public Sub ADriveLetterIsNeverAttachedToAnUnknownOrNotOpenDisk(state As String)
        Dim disk As SharedDisk = DiskWithMount(state)
        Assert.Throws(Of InvalidOperationException)(Sub() DiskDriveLetters.Attach(disk, "Z:", "Software\SZA\FastMediaSorter\Tests\unused"))
        Assert.Throws(Of InvalidOperationException)(Sub() DiskDriveLetters.Attach(Nothing, "Z:", "Software\SZA\FastMediaSorter\Tests\unused"))
    End Sub

    Private Shared Function DiskWithMount(state As String) As SharedDisk
        Dim d As SharedDisk = Disk(state)
        d.MountPath = "C:\mount"
        Return d
    End Function

    ' --- reading what the worker sends -------------------------------------------------------------

    ''' <summary>A record with a state and a holder from the future binds without failing (the field is text),
    ''' and what the host then does with it is the above: nothing is offered.</summary>
    <Fact>
    Public Sub ARecordFromANewerWorker_BindsAndOffersNothing()
        Dim wire As String = "{""schemaVersion"":2,""ok"":true,""disks"":[{""containerId"":""c1"",""containerPath"":""C:\\a.fdd""," &
                             """rootName"":""A"",""state"":""suspended"",""holder"":""cluster"",""readOnly"":false}]}"
        Dim disk As SharedDisk = WorkerIpc.ParseResponse(wire, 2).Disks.Single()
        Assert.Equal("suspended", disk.State)
        Assert.False(DiskOperations.CanOpen(disk))
        Assert.False(DiskOperations.CanClose(disk))
        Assert.Equal(Localization.T("Неизвестно"), Share_Disks_Form.HolderText(disk.Holder))
    End Sub

    ''' <summary>A response without its record leaves the record absent - Nothing, not an empty "closed" one.</summary>
    <Fact>
    Public Sub AResponseWithoutARecord_LeavesItAbsent()
        Dim response As WorkerResponse = WorkerIpc.ParseResponse("{""schemaVersion"":2,""ok"":true}", 2)
        Assert.Null(response.Disk)
        Assert.Null(response.Disks)
    End Sub

    ' --- capabilities and mode -----------------------------------------------------------------------

    ''' <summary>Capabilities are named flags: absent, false, a name the host has never heard of, or a status
    ''' without the map at all each leave disk sharing UNAVAILABLE - never available by default.</summary>
    <Fact>
    Public Sub DiskSharingIsOfferedOnlyWhenTheWorkerSaysTrue()
        Assert.False(DiskSharing.Supported(Nothing))
        Assert.False(DiskSharing.Supported(New WorkerStatus()))
        Assert.False(DiskSharing.Supported(New WorkerStatus With {.Capabilities = New Dictionary(Of String, Boolean)()}))
        Assert.False(DiskSharing.Supported(New WorkerStatus With {.Capabilities = New Dictionary(Of String, Boolean) From {{"disk-sharing", False}}}))
        Assert.False(DiskSharing.Supported(New WorkerStatus With {.Capabilities = New Dictionary(Of String, Boolean) From {{"disk-sharing-v3", True}, {"DISK-SHARING", True}}}))
        Assert.True(DiskSharing.Supported(New WorkerStatus With {.Capabilities = New Dictionary(Of String, Boolean) From {{"disk-sharing", True}, {"future", True}}}))
    End Sub

    ''' <summary>A capability that is not a boolean is an answer this host cannot read - the whole response
    ''' is refused as unreadable, and no capability is inferred from it.</summary>
    <Fact>
    Public Sub ACapabilityThatIsNotABoolean_IsAnUnreadableAnswer_NotASupport()
        Dim wire As String = "{""schemaVersion"":1,""ok"":true,""status"":{""capabilities"":{""disk-sharing"":""yes""},""roots"":[]}}"
        Assert.Throws(Of WorkerExchangeException)(Sub() WorkerIpc.ParseResponse(wire))
    End Sub

    ''' <summary>The status <c>mode</c> only ever words a hint (the administrator advice that describes the
    ''' Windows service). A mode the host does not know - or none - selects no mode-specific advice and decides
    ''' nothing else.</summary>
    <Theory>
    <InlineData(Nothing)>
    <InlineData("")>
    <InlineData("hybrid")>
    <InlineData("SERVICE")>
    <InlineData("session")>
    Public Sub AnUnknownOrNotServiceMode_DoesNotSelectTheServiceAdvice(mode As String)
        Dim status As New WorkerStatus With {.Mode = mode, .DiskSharingError = "disk sharing requires a LocalSystem holder"}
        Dim text As String = DiskSharing.UnavailableText(status)
        Assert.Contains("LocalSystem holder", text)
        Assert.DoesNotContain(Localization.T("Для монтирования .fdd нужны права администратора. Служба LocalService подключение дисков не поддерживает. Уже смонтированный диск можно добавить отдельно."), text)
    End Sub

    ''' <summary>A root whose owner the host does not know is never replaced or dropped by the host's own
    ''' folder list: only the host's own roots are sent (the existing owner-retention test covers the
    ''' stop path; this is the list itself).</summary>
    <Fact>
    Public Sub OnlyTheHostsOwnRootsAreEverSent_AnUnknownOwnerIsLeftAlone()
        Dim roots As New List(Of ShareFolder) From {
            New ShareFolder With {.name = "Mine", .owner = Nothing},
            New ShareFolder With {.name = "Mine2", .owner = "fms"},
            New ShareFolder With {.name = "Disk", .owner = "filedo"},
            New ShareFolder With {.name = "Odd", .owner = "someone-else"}
        }
        Assert.Equal({"Mine", "Mine2"}, ShareController.FmsFoldersOnly(roots).Select(Function(r) r.name).ToArray())
    End Sub

End Class
