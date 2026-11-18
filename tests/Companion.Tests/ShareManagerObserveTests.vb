Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports FastMediaSorterCompanion
Imports FastMediaSorterCompanion.AppCommands
Imports Xunit

''' <summary>
''' The three observe verbs of the Share Manager (ticket L0319, APP-COMMANDS 0.1 rules 1, 9, 12, 13): <c>share list</c>,
''' <c>schedule list</c> and <c>exchange status</c>. Each answer is built by a pure function, so these facts call the builder
''' directly with the program's own source objects - the worker's <c>WorkerStatus</c>/<c>ShareFolder</c>, a real
''' <c>ScheduledStore</c> over a temporary folder, a directly constructed <c>ExchangeDeviceStatus</c> and the folder queue -
''' and fix (1) the member names of every answer, (2) that nothing secret-shaped planted in the source reaches the text,
''' (3) the closed tokens and (4) the manifest the tokens are documented in. No window, no worker, no registry, no second
''' process: the round trips against a real worker, a real operation and an exchange account are a hand check of the release.
''' </summary>
Public Class ShareManagerObserveTests

    ' Values that look like what must never leave: planted in the source objects, searched for in the answer.
    Private Const Pin As String = "493817"
    Private Const Marker As String = "zz-secret-marker"

    Private Shared ReadOnly FixedZone As TimeZoneInfo = TimeZoneInfo.CreateCustomTimeZone("fms-observe-test", TimeSpan.FromHours(2), "fms-observe-test", "fms-observe-test")

    Private Shared Function Names(o As JsonObject) As String()
        Dim list As New List(Of String)()
        For Each pair As KeyValuePair(Of String, Object) In o
            list.Add(pair.Key)
        Next
        Return list.ToArray()
    End Function

    Private Shared Function EntriesOf(data As JsonObject, member As String) As List(Of JsonObject)
        Dim value As Object = Nothing
        Assert.True(data.TryGet(member, value), "member missing: " & member)
        Dim items As List(Of Object) = Assert.IsType(Of List(Of Object))(value)
        Dim result As New List(Of JsonObject)()
        For Each item As Object In items
            result.Add(Assert.IsType(Of JsonObject)(item))
        Next
        Return result
    End Function

    Private Shared Function TextOf(data As JsonObject) As String
        Return CommandJson.ToLine(data)
    End Function

    Private Shared Sub AssertNoneOf(text As String, ParamArray forbidden As String())
        For Each piece As String In forbidden
            Assert.DoesNotContain(piece, text, StringComparison.OrdinalIgnoreCase)
        Next
    End Sub

    Private Shared Function ReturnOf(verb As String, member As String) As CommandReturn
        For Each r As CommandReturn In ShareManagerCommandTable.Build().Find(verb).Returns
            If String.Equals(r.Name, member, StringComparison.Ordinal) Then Return r
        Next
        Return Nothing
    End Function

    ' --- share list ---------------------------------------------------------------------------------------

    <Fact>
    Public Sub Share_list_has_one_member_and_each_entry_has_exactly_folder_state_and_writable()
        Dim roots As New List(Of ShareFolder) From {
            New ShareFolder With {.name = "Photos-" & Marker, .hostPath = "D:\Photos", .accessPin = Pin, .pinProtected = True, .[readOnly] = False},
            New ShareFolder With {.name = "Docs", .hostPath = "D:\Docs", .[readOnly] = True}}
        Dim data As JsonObject = ShareManagerObserve.ShareListData(roots, True)

        Assert.Equal(New String() {"shares"}, Names(data))
        Dim rows As List(Of JsonObject) = EntriesOf(data, "shares")
        Assert.Equal(2, rows.Count)
        For Each row As JsonObject In rows
            Assert.Equal(New String() {"folder", "state", "writable"}, Names(row))
        Next
        Assert.Equal("D:\Photos", rows(0).GetString("folder"))
        Assert.True(rows(0).GetBool("writable", False))
        Assert.Equal("D:\Docs", rows(1).GetString("folder"))
        Assert.False(rows(1).GetBool("writable", True))
    End Sub

    <Fact>
    Public Sub Share_list_carries_no_pin_name_or_other_member_of_the_worker_record()
        Dim roots As New List(Of ShareFolder) From {
            New ShareFolder With {.name = "Photos-" & Marker, .hostPath = "D:\Photos", .accessPin = Pin, .pinProtected = True, .owner = "fms"}}
        Dim text As String = TextOf(ShareManagerObserve.ShareListData(roots, True))
        AssertNoneOf(text, Pin, Marker, "pin", "password", "token", "fingerprint", "owner", "name")
    End Sub

    <Fact>
    Public Sub Share_list_state_says_whether_the_server_serves_now()
        Dim roots As New List(Of ShareFolder) From {New ShareFolder With {.hostPath = "D:\Photos"}}
        Assert.Equal("serving", EntriesOf(ShareManagerObserve.ShareListData(roots, True), "shares")(0).GetString("state"))
        Assert.Equal("stopped", EntriesOf(ShareManagerObserve.ShareListData(roots, False), "shares")(0).GetString("state"))
        Assert.Equal(ShareManagerCommandTable.ShareServing, "serving")
        Assert.Equal(ShareManagerCommandTable.ShareStopped, "stopped")
    End Sub

    <Fact>
    Public Sub Share_list_keeps_only_the_folders_this_program_owns_and_that_have_a_path()
        Dim roots As New List(Of ShareFolder) From {
            New ShareFolder With {.hostPath = "D:\Mine", .owner = Nothing},
            New ShareFolder With {.hostPath = "D:\AlsoMine", .owner = "fms"},
            New ShareFolder With {.hostPath = "D:\Disk", .owner = "filedo"},
            New ShareFolder With {.hostPath = "", .owner = "fms"},
            Nothing}
        Dim rows As List(Of JsonObject) = EntriesOf(ShareManagerObserve.ShareListData(roots, True), "shares")
        Assert.Equal(2, rows.Count)
        Assert.Equal("D:\Mine", rows(0).GetString("folder"))
        Assert.Equal("D:\AlsoMine", rows(1).GetString("folder"))
    End Sub

    <Fact>
    Public Sub Share_list_of_no_roots_is_an_empty_array_not_a_missing_member()
        Dim data As JsonObject = ShareManagerObserve.ShareListData(Nothing, False)
        Assert.Equal(New String() {"shares"}, Names(data))
        Assert.Empty(EntriesOf(data, "shares"))
        Assert.Equal("{""shares"":[]}", TextOf(data))
    End Sub

    ' --- schedule list ------------------------------------------------------------------------------------

    Private Shared Function Op(id As String, name As String, action As ScheduledAction, enabled As Boolean, outcome As ScheduledRunOutcome) As ScheduledOperation
        Dim o As New ScheduledOperation With {
            .Id = id, .Name = name, .Action = action, .Enabled = enabled,
            .StartDate = New DateTime(2026, 11, 1), .AnchorMinutes = 9 * 60, .IntervalMinutes = 24 * 60,
            .HasExplicitRetryMissedRuns = True, .RetryMissedRunsAtStart = False}
        o.Source.Path = "D:\Private\Source-" & Marker
        o.Source.ResourceName = "Nas-" & Marker
        o.Destination.Path = "\\nas\secret-share-" & Marker
        o.Record.LastOutcome = outcome
        o.Record.LastError = "access denied for " & Marker
        Return o
    End Function

    <Fact>
    Public Sub Schedule_list_entry_has_exactly_the_six_members()
        Dim ops As New List(Of ScheduledOperation) From {
            Op(Guid.NewGuid().ToString("D"), "Nightly photos", ScheduledAction.Copy, True, ScheduledRunOutcome.Ok)}
        Dim data As JsonObject = ShareManagerObserve.ScheduleListData(ops, New DateTime(2026, 11, 14, 10, 0, 0), FixedZone)

        Assert.Equal(New String() {"operations"}, Names(data))
        Dim rows As List(Of JsonObject) = EntriesOf(data, "operations")
        Assert.Single(rows)
        Assert.Equal(New String() {"id", "name", "action", "enabled", "nextRun", "lastResult"}, Names(rows(0)))
        Assert.Equal(ops(0).Id, rows(0).GetString("id"))
        Assert.Equal("Nightly photos", rows(0).GetString("name"))
        Assert.Equal("copy", rows(0).GetString("action"))
        Assert.True(rows(0).GetBool("enabled", False))
        Assert.Equal("ok", rows(0).GetString("lastResult"))
    End Sub

    <Fact>
    Public Sub Schedule_list_carries_no_folder_resource_file_or_error_text()
        Dim ops As New List(Of ScheduledOperation) From {
            Op(Guid.NewGuid().ToString("D"), "  Nightly photos  ", ScheduledAction.Move, True, ScheduledRunOutcome.Failed)}
        Dim data As JsonObject = ShareManagerObserve.ScheduleListData(ops, New DateTime(2026, 11, 14, 10, 0, 0), FixedZone)
        Dim text As String = TextOf(data)
        AssertNoneOf(text, Marker, "Private", "Source-", "secret-share", "\\nas", "D:\", "denied", "password", "token", "resourceId")
        ' The name an owner gave the operation is the one text member; it is trimmed, nothing else of the record joins it.
        Assert.Equal("Nightly photos", EntriesOf(data, "operations")(0).GetString("name"))
    End Sub

    <Fact>
    Public Sub Schedule_next_run_is_iso_8601_with_the_offset_and_null_when_the_scheduler_does_not_arm_it()
        Dim now As New DateTime(2026, 11, 14, 10, 0, 0)
        Dim armed As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "a", ScheduledAction.Copy, True, ScheduledRunOutcome.None)
        Assert.Equal("2026-11-15T09:00:00+02:00", ShareManagerObserve.NextRunText(armed, now, FixedZone))

        Dim off As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "b", ScheduledAction.Copy, False, ScheduledRunOutcome.None)
        Assert.Null(ShareManagerObserve.NextRunText(off, now, FixedZone))

        Dim walled As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "c", ScheduledAction.Copy, True, ScheduledRunOutcome.Wall)
        walled.Halt = ScheduledHalt.Wall
        Assert.Equal("2026-11-15T09:00:00+02:00", ShareManagerObserve.NextRunText(walled, now, FixedZone))

        Dim parked As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "d", ScheduledAction.Copy, True, ScheduledRunOutcome.None)
        parked.Halt = ScheduledHalt.InvalidDefinition
        Assert.Null(ShareManagerObserve.NextRunText(parked, now, FixedZone))

        Dim data As JsonObject = ShareManagerObserve.ScheduleListData(New List(Of ScheduledOperation) From {off}, now, FixedZone)
        Dim value As Object = "unset"
        Assert.True(EntriesOf(data, "operations")(0).TryGet("nextRun", value), "nextRun is a member even when null")
        Assert.Null(value)
    End Sub

    <Fact>
    Public Sub Every_action_and_every_last_result_has_a_documented_token_that_is_not_the_unknown_one()
        Dim actionText As String = ReturnOf("schedule list", "operations").Description
        For Each v As Object In [Enum].GetValues(GetType(ScheduledAction))
            Dim token As String = ShareManagerObserve.ActionToken(DirectCast(v, ScheduledAction))
            Assert.NotEqual(ShareManagerCommandTable.ActionUnknown, token)
            Assert.Contains(token, actionText, StringComparison.Ordinal)
        Next
        For Each v As Object In [Enum].GetValues(GetType(ScheduledRunOutcome))
            Dim token As String = ShareManagerObserve.ResultToken(DirectCast(v, ScheduledRunOutcome))
            Assert.NotEqual(ShareManagerCommandTable.ResultUnknown, token)
            Assert.Contains(token, actionText, StringComparison.Ordinal)
        Next
        ' A value this build does not know is the closed "unknown", never a number or a name.
        Assert.Equal(ShareManagerCommandTable.ActionUnknown, ShareManagerObserve.ActionToken(CType(99, ScheduledAction)))
        Assert.Equal(ShareManagerCommandTable.ResultUnknown, ShareManagerObserve.ResultToken(CType(99, ScheduledRunOutcome)))
    End Sub

    <Fact>
    Public Sub Operations_read_back_from_a_real_store_list_in_order_without_running_anything()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-observe-" & Guid.NewGuid().ToString("N"))
        Try
            Dim store As New ScheduledStore(folder)
            Dim first As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "Nightly photos", ScheduledAction.Copy, True, ScheduledRunOutcome.Ok)
            first.CreatedAt = New DateTime(2026, 11, 1, 8, 0, 0)
            Dim second As ScheduledOperation = Op(Guid.NewGuid().ToString("D"), "Old downloads", ScheduledAction.Delete, False, ScheduledRunOutcome.Failed)
            second.CreatedAt = New DateTime(2026, 11, 2, 8, 0, 0)
            store.SaveImported(first)
            store.SaveImported(second)

            Dim data As JsonObject = ShareManagerObserve.ScheduleListData(store.LoadAll(), New DateTime(2026, 11, 14, 10, 0, 0), FixedZone)
            Dim rows As List(Of JsonObject) = EntriesOf(data, "operations")
            Assert.Equal(2, rows.Count)
            Assert.Equal("Nightly photos", rows(0).GetString("name"))
            Assert.Equal("copy", rows(0).GetString("action"))
            Assert.Equal("ok", rows(0).GetString("lastResult"))
            Assert.Equal("2026-11-15T09:00:00+02:00", rows(0).GetString("nextRun"))
            Assert.Equal("Old downloads", rows(1).GetString("name"))
            Assert.Equal("delete", rows(1).GetString("action"))
            Assert.False(rows(1).GetBool("enabled", True))
            Assert.Equal("failed", rows(1).GetString("lastResult"))
            Assert.Null(rows(1).GetString("nextRun"))
            AssertNoneOf(TextOf(data), Marker, "denied", "D:\", "\\nas")
        Finally
            If Directory.Exists(folder) Then Directory.Delete(folder, True)
        End Try
    End Sub

    ' --- exchange status ----------------------------------------------------------------------------------

    Private Shared Function Configured(state As ExchangeAgentState) As ExchangeDeviceStatus
        Return New ExchangeDeviceStatus(True, state, "bad-credentials-" & Marker, New String() {"e2e-v1"}, "device-" & Marker,
                                        "Name-" & Marker, "login-" & Marker, "server-" & Marker & ".example.org",
                                        ExchangeCertificateKind.None, "fp-" & Marker, "public-" & Marker)
    End Function

    <Fact>
    Public Sub Exchange_status_has_exactly_the_three_members_and_no_server_login_device_or_key()
        Dim data As JsonObject = ShareManagerObserve.ExchangeStatusData(Configured(ExchangeAgentState.Online), 3)
        Assert.Equal(New String() {"accountConfigured", "state", "pendingRequests"}, Names(data))
        Assert.True(data.GetBool("accountConfigured", False))
        Assert.Equal("online", data.GetString("state"))
        Dim pending As Object = Nothing
        Assert.True(data.TryGet("pendingRequests", pending))
        Assert.Equal(3, Assert.IsType(Of Integer)(pending))
        AssertNoneOf(TextOf(data), Marker, "server", "login", "device", "fingerprint", "key", "reason", "example.org")
    End Sub

    <Fact>
    Public Sub Exchange_status_with_no_account_says_not_configured_and_nothing_pending()
        Dim data As JsonObject = ShareManagerObserve.ExchangeStatusData(DirectCast(Nothing, ExchangeDeviceStatus), 0)
        Assert.Equal(New String() {"accountConfigured", "state", "pendingRequests"}, Names(data))
        Assert.False(data.GetBool("accountConfigured", True))
        Assert.Equal("not-configured", data.GetString("state"))
        Assert.Equal("{""accountConfigured"":false,""state"":""not-configured"",""pendingRequests"":0}", TextOf(data))
    End Sub

    <Fact>
    Public Sub Every_agent_state_has_a_documented_token_and_no_account_always_reads_not_configured()
        Dim stateText As String = ReturnOf("exchange status", "state").Description
        For Each v As Object In [Enum].GetValues(GetType(ExchangeAgentState))
            Dim state As ExchangeAgentState = DirectCast(v, ExchangeAgentState)
            Dim token As String = ShareManagerObserve.ExchangeStateToken(True, state)
            Assert.Contains(token, stateText, StringComparison.Ordinal)
            ' "not configured" is a fact about the account, so no state of the agent can say otherwise.
            Assert.Equal("not-configured", ShareManagerObserve.ExchangeStateToken(False, state))
        Next
        Assert.Equal("connecting", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.Connecting))
        Assert.Equal("reconnecting", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.Reconnecting))
        Assert.Equal("sign-in-again", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.SignInAgain))
        Assert.Equal("pin-mismatch", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.PinMismatch))
        Assert.Equal("update-needed", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.UpdateNeeded))
        Assert.Equal("defect", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.Defect))
        Assert.Equal("stopped", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.Stopped))
        ' An account that is on disk while no agent runs reads as stopped, never as "not configured".
        Assert.Equal("stopped", ShareManagerObserve.ExchangeStateToken(True, ExchangeAgentState.NotConfigured))
    End Sub

    <Fact>
    Public Sub Pending_requests_never_go_below_zero()
        Dim value As Object = Nothing
        Assert.True(ShareManagerObserve.ExchangeStatusData(True, ExchangeAgentState.Online, -5).TryGet("pendingRequests", value))
        Assert.Equal(0, Assert.IsType(Of Integer)(value))
    End Sub

    <Fact>
    Public Sub Outstanding_requests_count_the_queued_ones_and_the_one_on_the_screen()
        Dim queue As New FolderProposalQueue(Function() 0L)
        Assert.Equal(0, queue.OutstandingCount)
        queue.Offer("D:\A", Nothing)
        queue.Offer("D:\B", Nothing)
        Assert.Equal(2, queue.OutstandingCount)
        Assert.Equal("D:\A", queue.TakeNext())
        ' D:\A is on the screen now: no longer pending, still waiting for the person's answer.
        Assert.Equal(1, queue.PendingCount)
        Assert.Equal(2, queue.OutstandingCount)
        queue.Complete("D:\A")
        Assert.Equal(1, queue.OutstandingCount)
    End Sub

    ' --- the manifest documents what the builders write --------------------------------------------------

    <Fact>
    Public Sub The_manifest_returns_name_the_members_the_builders_write()
        Dim returns As New List(Of String)()
        For Each r As CommandReturn In ShareManagerCommandTable.Build().Find("share list").Returns
            returns.Add(r.Name)
        Next
        Assert.Equal(New String() {"shares"}, returns.ToArray())
        Dim entry As String = ReturnOf("share list", "shares").Description
        For Each member As String In New String() {"folder", "state", "writable"}
            Assert.Contains(member, entry, StringComparison.Ordinal)
        Next

        returns.Clear()
        For Each r As CommandReturn In ShareManagerCommandTable.Build().Find("schedule list").Returns
            returns.Add(r.Name)
        Next
        Assert.Equal(New String() {"operations"}, returns.ToArray())
        Dim operationText As String = ReturnOf("schedule list", "operations").Description
        For Each member As String In New String() {"id", "name", "action", "enabled", "nextRun", "lastResult"}
            Assert.Contains(member, operationText, StringComparison.Ordinal)
        Next

        returns.Clear()
        For Each r As CommandReturn In ShareManagerCommandTable.Build().Find("exchange status").Returns
            returns.Add(r.Name)
        Next
        Assert.Equal(New String() {"accountConfigured", "state", "pendingRequests"}, returns.ToArray())
    End Sub

    <Fact>
    Public Sub No_returned_member_is_named_like_a_secret()
        For Each verb As String In New String() {"share list", "schedule list", "exchange status"}
            For Each r As CommandReturn In ShareManagerCommandTable.Build().Find(verb).Returns
                For Each bad As String In New String() {"password", "pin", "token", "secret", "key", "address", "peer", "server", "user", "login", "fingerprint"}
                    Assert.DoesNotContain(bad, r.Name, StringComparison.OrdinalIgnoreCase)
                Next
            Next
        Next
    End Sub

    ' --- the sentences -----------------------------------------------------------------------------------

    <Fact>
    Public Sub The_sentences_carry_their_values_and_leave_no_placeholder()
        Dim shared2 As String = ShareManagerCommandText.SharedFolders(2)
        Assert.Contains("2", shared2, StringComparison.Ordinal)
        Dim scheduled As String = ShareManagerCommandText.ScheduledOperations(4)
        Assert.Contains("4", scheduled, StringComparison.Ordinal)
        Dim state As String = ShareManagerCommandText.ExchangeState("online", 3)
        Assert.Contains("online", state, StringComparison.Ordinal)
        Assert.Contains("3", state, StringComparison.Ordinal)
        For Each text As String In New String() {shared2, scheduled, state, ShareManagerCommandText.WorkerSilent(), ShareManagerCommandText.ExchangeNotSetUp(),
                                                 ShareManagerCommandText.Failed()}
            Assert.False(String.IsNullOrWhiteSpace(text))
            Assert.DoesNotContain("{", text, StringComparison.Ordinal)
        Next
    End Sub

End Class
