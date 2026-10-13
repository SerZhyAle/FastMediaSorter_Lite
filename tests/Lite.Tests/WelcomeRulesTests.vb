#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' Spec 184 (the welcome wizard), the pure half: when the wizard runs (T-1..T-5), which steps a build shows, and the
''' facts the trigger reads. The window and its registry writes are hand-accepted (spec 184 T-6); these are the
''' rules that protect a user's file and an upgrade, provable without a window.
''' </summary>
Public Class WelcomeRulesTests

    ' --- the trigger (spec 184 section 3) ----------------------------------------------------

    <Fact>
    Public Sub A_fresh_install_started_without_a_file_runs_the_wizard()
        ' T-1: the installer's "run after install" checkbox, or the Start menu, with no media argument.
        Assert.True(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=False, freshProfile:=True, hasFileArgument:=False))
    End Sub

    <Fact>
    Public Sub A_fresh_install_started_with_a_file_gets_no_wizard_on_that_launch()
        ' T-2: the picture is the user's intent. The launch shows the one-line invitation instead.
        Assert.False(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=False, freshProfile:=True, hasFileArgument:=True))
    End Sub

    <Fact>
    Public Sub A_fresh_install_started_with_a_file_leaves_a_note_for_the_next_launch()
        ' T-2 promises the wizard on the next launch without a file. The note is what keeps that promise, because the
        ' close of the file launch writes the hive and the next launch would no longer look fresh.
        Assert.True(WelcomeRules.DeferToNextLaunch(welcomeDone:=False, freshProfile:=True, hasFileArgument:=True))
        Assert.True(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=True, freshProfile:=False, hasFileArgument:=False))
    End Sub

    <Fact>
    Public Sub A_launch_with_a_file_never_runs_the_wizard_even_when_a_note_is_pending()
        Assert.False(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=True, freshProfile:=True, hasFileArgument:=True))
    End Sub

    <Fact>
    Public Sub An_upgrade_never_gets_the_wizard()
        ' T-3: the hive has values, the note is absent. An upgrade must not lecture a user of years.
        Assert.False(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=False, freshProfile:=False, hasFileArgument:=False))
        Assert.False(WelcomeRules.DeferToNextLaunch(welcomeDone:=False, freshProfile:=False, hasFileArgument:=True))
    End Sub

    <Fact>
    Public Sub A_second_windows_user_on_the_same_machine_gets_the_wizard()
        ' T-4: per-user HKCU, an empty hive - for that user it is a fresh install.
        Assert.True(WelcomeRules.ShouldRun(welcomeDone:=False, welcomePending:=False, freshProfile:=True, hasFileArgument:=False))
    End Sub

    <Fact>
    Public Sub Once_recorded_the_wizard_never_fires_again_by_accident()
        ' T-5: any later launch. WelcomeDone is written on every exit, so even a fresh-looking profile does not re-fire.
        Assert.False(WelcomeRules.ShouldRun(welcomeDone:=True, welcomePending:=True, freshProfile:=True, hasFileArgument:=False))
        Assert.False(WelcomeRules.DeferToNextLaunch(welcomeDone:=True, freshProfile:=True, hasFileArgument:=True))
    End Sub

    ' --- what counts as the viewer's own profile ------------------------------------------------

    <Fact>
    Public Sub The_viewers_own_values_are_the_ones_that_make_a_profile_used()
        Assert.True(WelcomeRules.IsViewerValue("FirstRun"))
        Assert.True(WelcomeRules.IsViewerValue("color_scheme"))
        Assert.True(WelcomeRules.IsViewerValue("UserAlreadyAskedForAssociations"))
    End Sub

    <Fact>
    Public Sub The_shared_and_the_wizards_own_values_do_not_make_a_profile_used()
        ' The Share Manager writes Share_* into the same hive, and the language is shared with it. The wizard's own
        ' markers are not evidence that the viewer has run before.
        Assert.False(WelcomeRules.IsViewerValue("Share_ListenPort"))
        Assert.False(WelcomeRules.IsViewerValue("UiLanguage"))
        Assert.False(WelcomeRules.IsViewerValue("UiTheme"))
        Assert.False(WelcomeRules.IsViewerValue("Is_Russian_Language"))
        Assert.False(WelcomeRules.IsViewerValue(WelcomeRules.DoneValue))
        Assert.False(WelcomeRules.IsViewerValue(WelcomeRules.PendingValue))
        Assert.False(WelcomeRules.IsViewerValue(""))
        Assert.False(WelcomeRules.IsViewerValue(Nothing))
    End Sub

    <Fact>
    Public Sub A_hive_with_only_shared_values_is_still_a_fresh_profile()
        Assert.False(WelcomeRules.HasViewerValues(New List(Of String) From {"Share_ListenPort", "UiLanguage", "UiTheme"}))
        Assert.False(WelcomeRules.HasViewerValues(New List(Of String)()))
        Assert.False(WelcomeRules.HasViewerValues(Nothing))
        Assert.True(WelcomeRules.HasViewerValues(New List(Of String) From {"Share_ListenPort", "RunsCount"}))
    End Sub

    ' --- the steps (spec 184 section 4) ------------------------------------------------------------

    <Fact>
    Public Sub The_steps_are_six_in_order_and_the_last_one_is_the_finish()
        Dim steps As IReadOnlyList(Of WelcomeStep) = WelcomeRules.StepsFor(isPackaged:=False)
        Assert.Equal(6, steps.Count)
        Assert.Equal(WelcomeStep.Language, steps(0))
        Assert.Equal(WelcomeStep.Appearance, steps(1))
        Assert.Equal(WelcomeStep.Sorting, steps(2))
        Assert.Equal(WelcomeStep.Delete, steps(3))
        Assert.Equal(WelcomeStep.Associations, steps(4))
        Assert.Equal(WelcomeStep.Finish, steps(5))
        Assert.Equal(5, WelcomeRules.FinishIndex(steps))
    End Sub

    <Fact>
    Public Sub The_packaged_build_has_no_association_step()
        ' The packaged (MSIX) build cannot offer it: the OS owns associations there (AssociationUx, spec 184 section 2).
        Dim steps As IReadOnlyList(Of WelcomeStep) = WelcomeRules.StepsFor(isPackaged:=True)
        Assert.Equal(5, steps.Count)
        Assert.DoesNotContain(WelcomeStep.Associations, steps)
        Assert.Equal(WelcomeStep.Finish, steps(steps.Count - 1))
        Assert.Equal(4, WelcomeRules.FinishIndex(steps))
    End Sub

    ' --- the code defaults (spec 184 rule 1: defaults are sacred) ----------------------------------

    <Fact>
    Public Sub The_wizard_preselects_the_code_defaults_not_the_prose_ones()
        ' Lifecycle reads "color_scheme" with the default "1" (Black). Spec 184 section 2 said "dynamic"; the code wins.
        Assert.Equal(1, WelcomeRules.DefaultColorScheme)
        ' Lifecycle reads "WheelZooms" with the default "0": the wheel flips files.
        Assert.False(WelcomeRules.DefaultWheelZooms)
    End Sub

    ' --- key 1 (spec 184 step W-3) -----------------------------------------------------------------

    <Fact>
    Public Sub Clearing_key_one_is_allowed_and_a_vanished_folder_is_refused()
        Assert.True(WelcomeRules.KeyOneMayBind("", folderExists:=False))
        Assert.True(WelcomeRules.KeyOneMayBind(Nothing, folderExists:=False))
        Assert.True(WelcomeRules.KeyOneMayBind("D:\Photos\Keep", folderExists:=True))
        Assert.False(WelcomeRules.KeyOneMayBind("\\nas\share\gone", folderExists:=False))
    End Sub

    ' --- the invitation button (spec 211 section 4.3) ----------------------------------------------

    <Fact>
    Public Sub The_invite_is_offered_exactly_while_the_profile_is_eligible()
        ' A fresh profile (T-1/T-2) and a deferred one (the pending note) are eligible; the invite exists
        ' exactly for the launch that carried a file, so there is no file-argument clause here.
        Assert.True(WelcomeRules.InviteOffered(welcomeDone:=False, welcomePending:=False, freshProfile:=True))
        Assert.True(WelcomeRules.InviteOffered(welcomeDone:=False, welcomePending:=True, freshProfile:=False))
        Assert.True(WelcomeRules.InviteOffered(welcomeDone:=False, welcomePending:=True, freshProfile:=True))
    End Sub

    <Fact>
    Public Sub The_invite_never_returns_on_an_ineligible_profile_or_after_a_wizard_exit()
        ' An upgrade (values, no note) is not eligible, and any exit of the wizard writes DoneValue - which
        ' is what takes the button away the moment the wizard is left.
        Assert.False(WelcomeRules.InviteOffered(welcomeDone:=False, welcomePending:=False, freshProfile:=False))
        Assert.False(WelcomeRules.InviteOffered(welcomeDone:=True, welcomePending:=False, freshProfile:=True))
        Assert.False(WelcomeRules.InviteOffered(welcomeDone:=True, welcomePending:=True, freshProfile:=True))
    End Sub

    ' --- the delete question (spec 211 section 4.2) --------------------------------------------------

    <Fact>
    Public Sub The_delete_question_opens_on_the_code_default_which_is_the_Bin()
        ' The checkbox preselects what the viewer holds; on a fresh profile that is the class default,
        ' and spec 211 rule 4 says walking through with Next must change nothing.
        Assert.True((New ModernViewerPreferences()).DeleteToRecycleBin)
    End Sub

    ' --- the installed-servers block (spec 215 sections 4-6) ---------------------------------------

    <Fact>
    Public Sub No_installed_server_component_draws_no_block_at_all()
        ' The fresh viewer-only install (and the packaged build, where both probes read False) ends the wizard
        ' exactly as 211 does: nothing to name, so nothing is added.
        Assert.Empty(WelcomeRules.ServerBlock(False, Nothing, False, Nothing))
        Assert.Empty(WelcomeRules.ServerBlock(False, True, False, True))
    End Sub

    <Fact>
    Public Sub One_installed_component_draws_only_its_own_row()
        Dim shareOnly As IReadOnlyList(Of WelcomeServerRow) = WelcomeRules.ServerBlock(True, True, False, Nothing)
        Assert.Single(shareOnly)
        Assert.Equal(WelcomeServer.ShareServer, shareOnly(0).Server)

        Dim exchangeOnly As IReadOnlyList(Of WelcomeServerRow) = WelcomeRules.ServerBlock(False, Nothing, True, False)
        Assert.Single(exchangeOnly)
        Assert.Equal(WelcomeServer.ExchangeServer, exchangeOnly(0).Server)
    End Sub

    <Fact>
    Public Sub Both_components_keep_the_fixed_order_share_first_exchange_second()
        ' The order is the table of spec 215 section 4, whatever the green facts are.
        For Each shareUp As Boolean In {True, False}
            For Each exchangeUp As Boolean In {True, False}
                Dim rows As IReadOnlyList(Of WelcomeServerRow) = WelcomeRules.ServerBlock(True, shareUp, True, exchangeUp)
                Assert.Equal(2, rows.Count)
                Assert.Equal(WelcomeServer.ShareServer, rows(0).Server)
                Assert.Equal(WelcomeServer.ExchangeServer, rows(1).Server)
            Next
        Next
    End Sub

    <Fact>
    Public Sub Green_needs_an_installed_component_AND_a_definite_running_process()
        Assert.Equal(ServerPill.Absent, WelcomeRules.StatusFor(False, True))
        Assert.Equal(ServerPill.Absent, WelcomeRules.StatusFor(False, False))
        Assert.Equal(ServerPill.Absent, WelcomeRules.StatusFor(False, Nothing))
        Assert.Equal(ServerPill.Green, WelcomeRules.StatusFor(True, True))
        Assert.Equal(ServerPill.Amber, WelcomeRules.StatusFor(True, False))
    End Sub

    <Fact>
    Public Sub An_unknown_probe_is_amber_and_never_green()
        ' Spec 215 rule 3: a lookup that could not tell never overclaims.
        Assert.Equal(ServerPill.Amber, WelcomeRules.StatusFor(True, Nothing))
        Dim rows As IReadOnlyList(Of WelcomeServerRow) = WelcomeRules.ServerBlock(True, Nothing, True, Nothing)
        Assert.All(rows, Sub(row) Assert.Equal(ServerPill.Amber, row.Pill))
    End Sub

    <Fact>
    Public Sub A_green_row_leads_on_to_adding_a_resource_and_any_other_opens_the_manager()
        Assert.Equal(ServerAct.AddResource, WelcomeRules.ActFor(ServerPill.Green))
        Assert.Equal(ServerAct.OpenManager, WelcomeRules.ActFor(ServerPill.Amber))
        Dim rows As IReadOnlyList(Of WelcomeServerRow) = WelcomeRules.ServerBlock(True, True, True, False)
        Assert.Equal(ServerAct.AddResource, rows(0).Act)
        Assert.Equal(ServerAct.OpenManager, rows(1).Act)
    End Sub

    <Fact>
    Public Sub The_probe_names_are_the_processes_of_the_two_servers()
        ' One name lookup each is the whole probe (spec 215 section 5); a rename of either exe is a decision here too.
        Assert.Equal("fms-share-worker", WelcomeRules.ShareWorkerProcessName)
        Assert.Equal("fms-exchange-server", WelcomeRules.ExchangeServerProcessName)
    End Sub

End Class

#End If
