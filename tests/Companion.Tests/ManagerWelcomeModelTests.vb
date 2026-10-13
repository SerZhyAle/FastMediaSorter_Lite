Option Strict On
Imports FastMediaSorterCompanion
Imports Xunit

Public Class ManagerWelcomeModelTests
    <Theory>
    <InlineData("", True, False, False, True)>
    <InlineData("", False, False, False, False)>
    <InlineData("pending", False, False, False, True)>
    <InlineData("pending", True, True, False, False)>
    <InlineData("pending", True, False, True, False)>
    <InlineData("1", True, False, False, False)>
    Public Sub Launch_rules_preserve_upgrades_and_silent_or_folder_requests(state As String, fresh As Boolean,
            silent As Boolean, folder As Boolean, expected As Boolean)
        Assert.Equal(expected, ManagerWelcomeModel.ShouldOpen(state, fresh, silent, folder))
    End Sub

    <Fact>
    Public Sub Installer_consent_and_shared_viewer_values_do_not_make_a_manager_profile_used()
        Assert.False(ManagerWelcomeModel.HasManagerValues({"UiLanguage", "UiTheme", "color_scheme", ServerFeatures.EnabledRegValue}))
        Assert.True(ManagerWelcomeModel.HasManagerValues({"Share_OpenWindowOnStartup"}))
        Assert.True(ManagerWelcomeModel.HasManagerValues({ServerWelcomeModel.DismissShareValue}))
    End Sub

    <Theory>
    <InlineData(False, 1, 0, "", False)>
    <InlineData(True, 0, 0, "", False)>
    <InlineData(True, 1, 1, "", False)>
    <InlineData(True, 1, 0, "refused", False)>
    <InlineData(True, 1, 0, "", True)>
    Public Sub Green_requires_a_live_folder_with_access_and_no_worker_warning(running As Boolean, count As Integer,
            missingAccess As Integer, warning As String, expected As Boolean)
        Assert.Equal(expected, ManagerWelcomeModel.IsGreen(running, count, missingAccess, warning))
    End Sub

    <Fact>
    Public Sub Enable_and_folder_are_mandatory_and_Later_only_records_an_explicit_initial_decline()
        Assert.False(ManagerWelcomeModel.CanAdvance(ManagerWelcomeStep.Enable, False, 0))
        Assert.False(ManagerWelcomeModel.CanAdvance(ManagerWelcomeStep.Folder, True, 0))
        Assert.True(ManagerWelcomeModel.CanAdvance(ManagerWelcomeStep.Folder, True, 1))
        Assert.False(ManagerWelcomeModel.RecordDone(ManagerWelcomeStep.Folder, False))
        Assert.False(ManagerWelcomeModel.RecordDone(ManagerWelcomeStep.Introduction, False))
        Assert.True(ManagerWelcomeModel.RecordDone(ManagerWelcomeStep.Introduction, True))
        Assert.True(ManagerWelcomeModel.RecordDone(ManagerWelcomeStep.Finish, False))
        Assert.Equal(ManagerWelcomeStep.Folder, ManagerWelcomeModel.StartStep(True, False))
        Assert.Equal(ManagerWelcomeStep.Finish, ManagerWelcomeModel.StartStep(True, True))
    End Sub

    <Fact>
    Public Sub Defaults_do_not_enable_startup_or_change_the_network_posture()
        Dim settings As New ShareSettings()
        Assert.False(settings.OpenWindowOnStartup)
        Assert.False(settings.AutostartEnabled)
        Assert.False(settings.LanOnly)
        Assert.True(settings.ExternalAccessIntent)
        Assert.Equal(0, settings.ListenPort)
        Assert.False(settings.WelcomeManagerDone)
    End Sub

    <Fact>
    Public Sub The_manager_steps_are_six_in_the_explain_enable_folder_port_basics_finish_order()
        Assert.Equal({ManagerWelcomeStep.Introduction, ManagerWelcomeStep.Enable, ManagerWelcomeStep.Folder,
            ManagerWelcomeStep.Port, ManagerWelcomeStep.Basics, ManagerWelcomeStep.Finish}, ManagerWelcomeModel.StepsFor())
    End Sub
End Class
