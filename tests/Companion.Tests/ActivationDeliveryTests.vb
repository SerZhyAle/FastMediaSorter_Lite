Option Strict On

Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' 108 H01 (057.S22-2 / S22-3): who owns the instance, what the receiver accepts, and that a
''' folder request is only ever STAGED. The receiver's decision runs on real unmanaged buffers,
''' the way WndProc hands it one; the acknowledgement is asserted, and so is the absence of any
''' sharing side effect.
''' </summary>
Public Class ActivationDeliveryTests

    <Theory>
    <InlineData("C:\folder:stream")>
    <InlineData("\\server")>
    <InlineData("\\server\share:stream")>
    Public Sub Invalid_folder_grammar_is_refused_before_consent(payload As String)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(payload).Kind)
    End Sub

    <Fact>
    Public Sub A_new_Companion_never_sends_a_folder_to_an_old_or_unknown_receiver()
        Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload("D:\Photos", Nothing))
        Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload("D:\Photos", New Version(26, 10, 1, 0)))
        Assert.Equal("D:\Photos", Program.CompatiblePayload("D:\Photos", ShareActivationPolicy.MinConsentVersion))
        Assert.Equal(Program.ShowWindowCommand, Program.CompatiblePayload(Program.PlainStartCommand, Nothing))
    End Sub

    <Fact>
    Public Sub A_slow_pending_or_displayed_proposal_cannot_be_duplicated_after_ten_seconds()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        queue.Offer("D:\Photos", Nothing)
        clock.NowMs = 20000
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("D:\Photos", Nothing))
        queue.TakeNext()
        clock.NowMs = 40000
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("D:\Photos", Nothing))
        queue.Complete("D:\Photos")
        clock.NowMs += 9000
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("D:\Photos", Nothing))
        clock.NowMs += 2000
        Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\Photos", Nothing))
    End Sub

    ' ------------------------------------------------------------ instance ownership ----

    <Fact>
    Public Sub Acquiring_the_mutex_is_primary_and_finding_it_taken_is_existing()
        Assert.Equal(InstanceRole.Primary, InstanceOwnership.Decide(True, Nothing))
        Assert.Equal(InstanceRole.Existing, InstanceOwnership.Decide(False, Nothing))
    End Sub

    <Fact>
    Public Sub Access_denied_means_an_instance_exists_never_a_second_primary()
        Dim denied As New UnauthorizedAccessException("elevated owner")
        ' Whatever createdNew held before the call threw, the exception decides.
        Assert.Equal(InstanceRole.Existing, InstanceOwnership.Decide(True, denied))
        Assert.Equal(InstanceRole.Existing, InstanceOwnership.Decide(False, denied))
        Assert.False(InstanceOwnership.IsUnexpected(denied))
    End Sub

    <Fact>
    Public Sub An_unexpected_failure_is_neither_primary_nor_silent()
        For Each failure As Exception In New Exception() {New InvalidOperationException("x"), New System.IO.IOException("y"), New ArgumentException("name")}
            Assert.Equal(InstanceRole.Unavailable, InstanceOwnership.Decide(True, failure))
            Assert.True(InstanceOwnership.IsUnexpected(failure))
        Next
    End Sub

    ' ------------------------------------------------------------------- grammar ----

    <Fact>
    Public Sub The_two_sentinels_are_exact()
        Assert.Equal(CompanionCommandKind.ShowWindow, CompanionCommand.Parse(Program.ShowWindowCommand).Kind)
        Assert.Equal(CompanionCommandKind.PlainStart, CompanionCommand.Parse(Program.PlainStartCommand).Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse("::FMS-SHOW-WINDOW::").Kind)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(Program.ShowWindowCommand & " x").Kind)
    End Sub

    <Theory>
    <InlineData("D:\Photos")>
    <InlineData("D:\Photos\")>
    <InlineData("D:\")>
    <InlineData("\\nas\share\folder")>
    <InlineData("C:/Users/x/Pictures")>
    <InlineData("D:\Фото\Отпуск 2026")>
    Public Sub Fully_qualified_drive_and_unc_folders_are_accepted_in_canonical_form(raw As String)
        Dim command As CompanionCommand = CompanionCommand.Parse(raw)
        Assert.Equal(CompanionCommandKind.ShareFolder, command.Kind)
        Assert.DoesNotContain("/", command.Folder)
        If command.Folder.Length > 3 Then Assert.False(command.Folder.EndsWith("\", StringComparison.Ordinal))
    End Sub

    <Theory>
    <InlineData("relative\folder")>
    <InlineData("photos")>
    <InlineData("\Windows")>
    <InlineData("D:photos")>
    <InlineData("\\?\C:\x")>
    <InlineData("\\.\pipe\x")>
    <InlineData("D:\a\..\b")>
    <InlineData("D:\wild*card")>
    <InlineData("D:\what?")>
    <InlineData("D:\a|b")>
    <InlineData("\\")>
    <InlineData("")>
    <InlineData("   ")>
    Public Sub Anything_else_is_not_a_folder(raw As String)
        Assert.Equal(CompanionCommandKind.Invalid, CompanionCommand.Parse(raw).Kind)
    End Sub

    ' ------------------------------------------------------------------ receiver ----

    Private NotInheritable Class Stage
        Friend ReadOnly Staged As New List(Of String)()
        Friend AcceptStaging As Boolean = True

        Friend Function Accept(command As CompanionCommand) As Boolean
            Staged.Add(command.Folder)
            Return AcceptStaging
        End Function
    End Class

    Private Shared Function Deliver(tag As Integer, payload As Byte(), cbData As Integer, stage As Stage, ByRef command As CompanionCommand) As Integer
        Dim mem As IntPtr = Marshal.AllocHGlobal(Math.Max(1, payload.Length))
        Try
            Marshal.Copy(payload, 0, mem, payload.Length)
            Return ActivationReceiver.Evaluate(New IntPtr(tag), cbData, mem, AddressOf stage.Accept, command)
        Finally
            Marshal.FreeHGlobal(mem)
        End Try
    End Function

    Private Shared Function Utf8(text As String) As Byte()
        Return Encoding.UTF8.GetBytes(text)
    End Function

    <Fact>
    Public Sub A_tagged_folder_request_is_acknowledged_and_only_staged()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim bytes As Byte() = Utf8("D:\Photos")
        Dim ack As Integer = Deliver(CopyDataTransport.CompanionTag, bytes, bytes.Length, stage, command)
        Assert.Equal(1, ack)
        Assert.Equal(CompanionCommandKind.ShareFolder, command.Kind)
        Assert.Equal(New String() {"D:\Photos"}, stage.Staged.ToArray())
    End Sub

    <Fact>
    Public Sub A_legacy_untagged_folder_request_is_staged_for_consent_too_never_shared()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim bytes As Byte() = Utf8("D:\Photos")
        Assert.Equal(1, Deliver(0, bytes, bytes.Length, stage, command))
        Assert.Equal(CompanionCommandKind.ShareFolder, command.Kind)
        Assert.Single(stage.Staged) ' the ONLY thing done with it
    End Sub

    <Fact>
    Public Sub An_unknown_tag_is_refused_with_no_staging()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim bytes As Byte() = Utf8("D:\Photos")
        Assert.Equal(0, Deliver(&H12345678, bytes, bytes.Length, stage, command))
        Assert.Null(command)
        Assert.Empty(stage.Staged)
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(-5)>
    <InlineData(65537)>
    Public Sub Bad_lengths_are_refused_before_decode(cbData As Integer)
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Assert.Equal(0, Deliver(CopyDataTransport.CompanionTag, Utf8("D:\Photos"), cbData, stage, command))
        Assert.Empty(stage.Staged)
    End Sub

    <Fact>
    Public Sub Malformed_utf8_and_embedded_nul_are_refused_and_stage_nothing()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim broken As Byte() = {&H44, &H3A, &H5C, &H80, &H81}
        Assert.Equal(0, Deliver(CopyDataTransport.CompanionTag, broken, broken.Length, stage, command))
        Dim withNul As Byte() = Utf8("D:\Pho" & ChrW(0) & "tos")
        Assert.Equal(0, Deliver(CopyDataTransport.CompanionTag, withNul, withNul.Length, stage, command))
        Assert.Empty(stage.Staged)
    End Sub

    <Fact>
    Public Sub A_folder_that_is_not_a_folder_is_refused_and_a_full_queue_answers_zero()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim relative As Byte() = Utf8("..\..\secrets")
        Assert.Equal(0, Deliver(CopyDataTransport.CompanionTag, relative, relative.Length, stage, command))
        Assert.Empty(stage.Staged)

        stage.AcceptStaging = False
        Dim ok As Byte() = Utf8("D:\Photos")
        Assert.Equal(0, Deliver(CopyDataTransport.CompanionTag, ok, ok.Length, stage, command))
    End Sub

    <Fact>
    Public Sub Show_window_from_either_generation_is_acknowledged_without_staging()
        For Each tag As Integer In {CopyDataTransport.CompanionTag, 0}
            Dim stage As New Stage()
            Dim command As CompanionCommand = Nothing
            Dim bytes As Byte() = Utf8(Program.ShowWindowCommand)
            Assert.Equal(1, Deliver(tag, bytes, bytes.Length, stage, command))
            Assert.Equal(CompanionCommandKind.ShowWindow, command.Kind)
            Assert.Empty(stage.Staged)
        Next
    End Sub

    <Fact>
    Public Sub Cyrillic_and_emoji_folder_names_survive_the_receiver()
        Dim stage As New Stage()
        Dim command As CompanionCommand = Nothing
        Dim name As String = "D:\Фото\" & Char.ConvertFromUtf32(&H1F4F7) & " отпуск"
        Dim bytes As Byte() = Utf8(name)
        Assert.Equal(1, Deliver(CopyDataTransport.CompanionTag, bytes, bytes.Length, stage, command))
        Assert.Equal(name, command.Folder)
    End Sub

    ' ------------------------------------------------------------------ proposals ----

    Private NotInheritable Class FakeClock
        Friend NowMs As Long
        Friend Function Read() As Long
            Return NowMs
        End Function
    End Class

    <Fact>
    Public Sub The_same_folder_twice_inside_the_window_is_one_proposal()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\Photos", Nothing))
        clock.NowMs += 4000 ' the sender's send timed out and it retried
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("d:\photos", Nothing)) ' case-insensitive on Windows
        Assert.Equal(1, queue.PendingCount)
    End Sub

    <Fact>
    Public Sub A_cancelled_proposal_stays_deduplicated_through_the_window_and_may_return_after_it()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        queue.Offer("D:\Photos", Nothing)
        Assert.Equal("D:\Photos", queue.TakeNext()) ' the user said no
        queue.Complete("D:\Photos")
        clock.NowMs += 9000
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("D:\Photos", Nothing)) ' a retry cannot reopen the question
        Assert.Equal("", queue.TakeNext())

        clock.NowMs += 2000 ' now 11 s after the first offer, past the 10 s window
        Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\Photos", Nothing)) ' a deliberate later action asks again
    End Sub

    <Fact>
    Public Sub An_already_shared_folder_is_acknowledged_and_never_queued()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        Assert.Equal(ProposalVerdict.AlreadyShared, queue.Offer("D:\Photos", Function(f) True))
        Assert.Equal(0, queue.PendingCount)
    End Sub

    <Fact>
    Public Sub Distinct_folders_stay_distinct_and_the_queue_caps_at_thirty_two()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        For i As Integer = 1 To FolderProposalQueue.MaxPending
            Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\f" & i.ToString(), Nothing))
        Next
        Assert.Equal(ProposalVerdict.Full, queue.Offer("D:\one-more", Nothing))
        Assert.Equal(FolderProposalQueue.MaxPending, queue.PendingCount)
        Assert.Equal("D:\f1", queue.TakeNext()) ' reading order
        Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\one-more", Nothing)) ' room again
    End Sub

    <Fact>
    Public Sub The_dedup_window_follows_the_injected_monotonic_clock_not_the_wall_clock()
        Dim clock As New FakeClock()
        Dim queue As New FolderProposalQueue(AddressOf clock.Read)
        queue.Offer("D:\Photos", Nothing)
        ' Wall time is irrelevant: only the injected clock decides, and it has not moved.
        Assert.Equal(ProposalVerdict.Duplicate, queue.Offer("D:\Photos", Nothing))
        queue.TakeNext()
        queue.Complete("D:\Photos")
        clock.NowMs = FolderProposalQueue.DedupWindowMs + 1
        Assert.Equal(ProposalVerdict.Queued, queue.Offer("D:\Photos", Nothing))
    End Sub

End Class
