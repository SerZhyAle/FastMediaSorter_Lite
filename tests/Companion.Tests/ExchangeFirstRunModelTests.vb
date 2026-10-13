Option Strict On
Imports FastMediaSorterCompanion
Imports Xunit

Public Class ExchangeFirstRunModelTests
    <Theory>
    <InlineData(True, True, True, 0, False, False, False, True)>
    <InlineData(False, True, True, 0, False, False, False, False)>
    <InlineData(True, False, True, 0, False, False, False, False)>
    <InlineData(True, True, False, 0, False, False, False, False)>
    <InlineData(True, True, True, 1, False, False, False, False)>
    <InlineData(True, True, True, 0, True, False, False, False)>
    <InlineData(True, True, True, 0, False, True, False, False)>
    <InlineData(True, True, True, 0, False, False, True, False)>
    Public Sub Only_a_known_unconfigured_undismissed_server_on_a_deliberate_launch_gets_the_flow(component As Boolean,
            gate As Boolean, answered As Boolean, accounts As Integer, dismissed As Boolean, silent As Boolean,
            folder As Boolean, expected As Boolean)
        Assert.Equal(expected, ExchangeFirstRunModel.ShouldOpen(component, gate, answered, accounts, dismissed, silent, folder))
    End Sub

    <Theory>
    <InlineData(False, False, 6)>
    <InlineData(True, False, 7)>
    <InlineData(False, True, 6)>
    <InlineData(True, True, 6)>
    Public Sub Service_is_offered_only_in_Server_edition_without_a_registered_service(server As Boolean, exists As Boolean, count As Integer)
        Dim steps = ExchangeFirstRunModel.StepsFor(server, exists)
        Assert.Equal(count, steps.Count)
        Assert.Equal(WelcomeStepKind.ExchangeStatement, steps(0))
        Assert.Equal(WelcomeStepKind.ExchangeDoors, steps(1))
        Assert.Equal(WelcomeStepKind.ExchangeDone, steps(steps.Count - 1))
        Assert.Equal(server AndAlso Not exists, steps.Contains(WelcomeStepKind.ExchangeService))
    End Sub

    <Fact>
    Public Sub Only_successful_engine_outcomes_unlock_the_mandatory_account_step()
        For Each outcome As ExchangeWizardOutcome In [Enum].GetValues(Of ExchangeWizardOutcome)()
            Assert.Equal(outcome = ExchangeWizardOutcome.Done OrElse outcome = ExchangeWizardOutcome.DoneNoAgent,
                ExchangeFirstRunModel.CanAdvance(WelcomeStepKind.ExchangeAccount, outcome))
            Assert.True(ExchangeFirstRunModel.CanAdvance(WelcomeStepKind.ExchangeAddress, outcome))
        Next
    End Sub

    <Fact>
    Public Sub Defaults_remain_owned_by_the_existing_services()
        Assert.Equal(22022, ExchangeSettings.DefaultPort)
        Assert.Equal("40000-40100", ServerWelcomeModel.DefaultThirdPartyRange)
        Assert.False((New ExchangeSettings()).KeepAccountPasswords)
        Assert.False(ExchangeFeatures.Offered)
    End Sub
End Class
