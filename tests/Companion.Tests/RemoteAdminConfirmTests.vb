Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket L0278 T-5, specification section 7.3 (row <c>RemoteAdminConfirmTests</c>) and 5.6.3, owner decision Q278-6: each of the
''' three destructive confirmations (account remove, device revoke, invitation withdraw) reads and writes its own
''' <c>RemoteAdmin_SkipConfirm_*</c> value; a stored opt-out for one kind does not skip another; declining stores nothing; the reset
''' deletes all three and only those; no value name contains a server, login or password; none is in the log package's allow-list.
''' No registry: the settings seam is the in-memory <c>MemoryPort</c>.
''' </summary>
Public Class RemoteAdminConfirmTests

    Private Shared ReadOnly Expected As New Dictionary(Of RemoteAdminConfirmKind, String) From {
        {RemoteAdminConfirmKind.AccountRemove, "RemoteAdmin_SkipConfirm_AccountRemove"},
        {RemoteAdminConfirmKind.DeviceRevoke, "RemoteAdmin_SkipConfirm_DeviceRevoke"},
        {RemoteAdminConfirmKind.InvitationWithdraw, "RemoteAdmin_SkipConfirm_InvitationWithdraw"}}

    Private Shared Function NewPort() As ExchangeDeviceAgentTests.MemoryPort
        Return New ExchangeDeviceAgentTests.MemoryPort()
    End Function

    <Fact>
    Public Sub Each_kind_has_its_own_value_name()
        Assert.Equal(3, RemoteAdminConfirm.AllKinds.Length)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Assert.Equal(Expected(kind), RemoteAdminConfirm.ValueNameOf(kind))
        Next
        Assert.Equal(3, RemoteAdminConfirm.AllNames.Distinct(StringComparer.Ordinal).Count())
        Assert.Equal(String.Join("|", Expected.Values.OrderBy(Function(s) s, StringComparer.Ordinal)),
                     String.Join("|", RemoteAdminConfirm.AllNames.OrderBy(Function(s) s, StringComparer.Ordinal)))
        ' A fourth kind is an error, never a fourth value.
        Assert.Throws(Of ArgumentOutOfRangeException)(Sub() RemoteAdminConfirm.ValueNameOf(CType(0, RemoteAdminConfirmKind)))
        Assert.Throws(Of ArgumentOutOfRangeException)(Sub() RemoteAdminConfirm.ValueNameOf(CType(4, RemoteAdminConfirmKind)))
    End Sub

    <Fact>
    Public Sub Nothing_stored_means_ask_and_a_read_creates_nothing()
        Dim port = NewPort()
        Dim confirm As New RemoteAdminConfirm(port)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Assert.False(confirm.IsSkipped(kind))
        Next
        Assert.False(confirm.AnySkipped())
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
    End Sub

    <Fact>
    Public Sub A_stored_opt_out_for_one_kind_does_not_skip_another()
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Dim port = NewPort()
            Dim confirm As New RemoteAdminConfirm(port)
            confirm.RecordAnswer(kind, confirmed:=True, doNotAskAgain:=True)
            For Each other As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
                Assert.Equal(other = kind, confirm.IsSkipped(other))
            Next
            Assert.True(confirm.AnySkipped())
            ' Exactly that kind's value, holding "1", and nothing else.
            Assert.Equal(1, port.Values.Count)
            Assert.Equal("1", port.Values(Expected(kind)))
            ' A second program instance over the same settings sees the same answers.
            Dim again As New RemoteAdminConfirm(port)
            For Each other As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
                Assert.Equal(other = kind, again.IsSkipped(other))
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_three_opt_outs_are_independent_of_each_other_in_both_directions()
        Dim port = NewPort()
        Dim confirm As New RemoteAdminConfirm(port)
        confirm.RecordAnswer(RemoteAdminConfirmKind.AccountRemove, True, True)
        confirm.RecordAnswer(RemoteAdminConfirmKind.InvitationWithdraw, True, True)
        Assert.True(confirm.IsSkipped(RemoteAdminConfirmKind.AccountRemove))
        Assert.False(confirm.IsSkipped(RemoteAdminConfirmKind.DeviceRevoke))
        Assert.True(confirm.IsSkipped(RemoteAdminConfirmKind.InvitationWithdraw))
        confirm.RecordAnswer(RemoteAdminConfirmKind.DeviceRevoke, True, True)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Assert.True(confirm.IsSkipped(kind))
        Next
        Assert.Equal(3, port.Values.Count)
    End Sub

    <Theory>
    <InlineData(False, False)>
    <InlineData(False, True)>
    <InlineData(True, False)>
    Public Sub Declining_or_not_ticking_the_box_stores_nothing(confirmed As Boolean, doNotAskAgain As Boolean)
        Dim port = NewPort()
        Dim confirm As New RemoteAdminConfirm(port)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            confirm.RecordAnswer(kind, confirmed, doNotAskAgain)
            Assert.False(confirm.IsSkipped(kind))
        Next
        Assert.Equal(0, port.Writes)
        Assert.Empty(port.Values)
    End Sub

    <Fact>
    Public Sub Recording_an_opt_out_that_is_already_stored_writes_nothing_again()
        Dim port = NewPort()
        Dim confirm As New RemoteAdminConfirm(port)
        confirm.RecordAnswer(RemoteAdminConfirmKind.DeviceRevoke, True, True)
        Assert.Equal(1, port.Writes)
        confirm.RecordAnswer(RemoteAdminConfirmKind.DeviceRevoke, True, True)
        Assert.Equal(1, port.Writes)
    End Sub

    <Theory>
    <InlineData("0")>
    <InlineData("")>
    <InlineData("2")>
    <InlineData("true")>
    <InlineData("yes")>
    <InlineData("11")>
    Public Sub Only_the_stored_text_one_switches_a_confirmation_off(stored As String)
        Dim port = NewPort()
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            port.Values(Expected(kind)) = stored
        Next
        Dim confirm As New RemoteAdminConfirm(port)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Assert.False(confirm.IsSkipped(kind), "'" & stored & "' must mean ask")
        Next
        Assert.False(confirm.AnySkipped())
        Assert.Equal(0, port.Writes)
    End Sub

    <Fact>
    Public Sub The_reset_deletes_the_three_values_and_only_those()
        Dim port = NewPort()
        port.Values("RemoteAdmin_Targets") = "[]"
        port.Values("Exchange_ViewerEntries") = "[]"
        port.Values("ConfirmDelete") = "1"
        Dim confirm As New RemoteAdminConfirm(port)
        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            confirm.RecordAnswer(kind, True, True)
        Next
        Assert.True(confirm.AnySkipped())
        Assert.Equal(6, port.Values.Count)

        confirm.ResetAll()

        For Each kind As RemoteAdminConfirmKind In RemoteAdminConfirm.AllKinds
            Assert.False(confirm.IsSkipped(kind))
            Assert.False(port.Values.ContainsKey(Expected(kind)))
        Next
        Assert.False(confirm.AnySkipped())
        Assert.Equal("[]|[]|1", String.Join("|", {port.Values("RemoteAdmin_Targets"), port.Values("Exchange_ViewerEntries"), port.Values("ConfirmDelete")}))
        Assert.Equal(3, port.Values.Count)
        ' Asking again works after the reset: the box can be ticked once more.
        confirm.RecordAnswer(RemoteAdminConfirmKind.AccountRemove, True, True)
        Assert.True(confirm.IsSkipped(RemoteAdminConfirmKind.AccountRemove))
    End Sub

    <Fact>
    Public Sub The_reset_on_an_empty_store_leaves_it_empty()
        Dim port = NewPort()
        Dim confirm As New RemoteAdminConfirm(port)
        confirm.ResetAll()
        Assert.Empty(port.Values)
        Assert.False(confirm.AnySkipped())
    End Sub

    <Fact>
    Public Sub No_value_name_names_a_server_a_login_or_a_password()
        Dim words As New Regex("server|login|password|passwd|host|address|secret|token", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
        Dim kinds As New HashSet(Of String)(StringComparer.Ordinal) From {"AccountRemove", "DeviceRevoke", "InvitationWithdraw"}
        For Each name As String In RemoteAdminConfirm.AllNames
            Assert.StartsWith("RemoteAdmin_SkipConfirm_", name, StringComparison.Ordinal)
            ' After the prefix comes the action and nothing else: no server, login, account name or secret can be part of the name.
            Assert.Contains(name.Substring("RemoteAdmin_SkipConfirm_".Length), kinds)
            Assert.False(words.IsMatch(name), name & " names something a value name must not")
        Next
        ' The stores of one window never share a value.
        Assert.DoesNotContain(ExchangeAdminTargets.ValueName, RemoteAdminConfirm.AllNames)
    End Sub

    <Fact>
    Public Sub No_value_is_in_the_log_package_allow_list_and_the_log_package_never_mentions_the_prefix()
        Dim source As String = ExchangeAdminTargetsTests.LogPackageSource()
        Dim block As String = ExchangeAdminTargetsTests.SafeSettingNamesBlock(source)
        Assert.True(block.Length > 200, "the SafeSettingNames block looks empty; the guard would be vacuous")
        For Each name As String In RemoteAdminConfirm.AllNames
            Assert.DoesNotContain(name, block, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain(name, source, StringComparison.OrdinalIgnoreCase)
        Next
        Assert.DoesNotContain("SkipConfirm", source, StringComparison.OrdinalIgnoreCase)
    End Sub

End Class
