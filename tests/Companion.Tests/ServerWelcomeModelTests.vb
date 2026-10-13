Option Strict On

Imports System.Collections.Generic
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Spec 185 (the server welcome), the pure half: when a half opens (section 3.1), which steps it has (3.2), what a
''' port choice may store (A-2) and when Next may leave the mandatory account step (rule 5). The window is not
''' tested here; these rules are what keeps an upgrade quiet and the exchange half hidden behind its gate.
''' </summary>
Public Class ServerWelcomeModelTests

    ' --- when a half opens (section 3.1) ----------------------------------------------------------------

    <Fact>
    Public Sub A_fresh_share_server_opens_its_welcome()
        Assert.True(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Share, configured:=False, dismissed:=False,
                                                  componentPresent:=True, gateOpen:=False))
    End Sub

    <Fact>
    Public Sub A_configured_share_server_never_opens_its_welcome()
        ' An upgrade over a configured machine never shows the welcome (spec 185 section 7, acceptance 3).
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Share, configured:=True, dismissed:=False,
                                                   componentPresent:=True, gateOpen:=False))
    End Sub

    <Fact>
    Public Sub A_recorded_Later_keeps_its_half_closed()
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Share, configured:=False, dismissed:=True,
                                                   componentPresent:=True, gateOpen:=False))
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Exchange, configured:=False, dismissed:=True,
                                                   componentPresent:=True, gateOpen:=True))
    End Sub

    <Fact>
    Public Sub A_missing_component_never_opens_its_half()
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Share, configured:=False, dismissed:=False,
                                                   componentPresent:=False, gateOpen:=True))
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Exchange, configured:=False, dismissed:=False,
                                                   componentPresent:=False, gateOpen:=True))
    End Sub

    <Fact>
    Public Sub The_exchange_half_does_not_exist_while_its_gate_is_closed()
        ' The shipping gate (ExchangeFeatures.Offered) is False today: no step may mention the exchange server.
        Assert.False(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Exchange, configured:=False, dismissed:=False,
                                                   componentPresent:=True, gateOpen:=False))
    End Sub

    <Fact>
    Public Sub An_open_gate_with_an_unconfigured_exchange_half_opens_it()
        Assert.True(ServerWelcomeModel.ShouldOpen(WelcomeHalf.Exchange, configured:=False, dismissed:=False,
                                                  componentPresent:=True, gateOpen:=True))
    End Sub

    ' --- the steps (section 3.2) ----------------------------------------------------------------------

    <Fact>
    Public Sub The_share_half_has_its_enable_port_and_done_steps_in_that_order()
        Dim steps As IReadOnlyList(Of WelcomeStepKind) = ServerWelcomeModel.StepsFor(WelcomeHalf.Share)
        Assert.Equal(New List(Of WelcomeStepKind) From {WelcomeStepKind.ShareEnable, WelcomeStepKind.SharePort, WelcomeStepKind.ShareDone},
                     New List(Of WelcomeStepKind)(steps))
    End Sub

    <Fact>
    Public Sub The_exchange_half_has_six_steps_without_service()
        Dim steps As IReadOnlyList(Of WelcomeStepKind) = ServerWelcomeModel.StepsFor(WelcomeHalf.Exchange)
        Assert.Equal(New List(Of WelcomeStepKind) From {WelcomeStepKind.ExchangeStatement, WelcomeStepKind.ExchangeDoors, WelcomeStepKind.ExchangeAddress, WelcomeStepKind.ExchangeAccount, WelcomeStepKind.ExchangeCertificate, WelcomeStepKind.ExchangeDone},
                     New List(Of WelcomeStepKind)(steps))
    End Sub

    ' --- the mandatory step (rule 5) -------------------------------------------------------------------

    <Fact>
    Public Sub Only_the_account_step_waits_for_an_account()
        Assert.False(ServerWelcomeModel.CanAdvance(WelcomeStepKind.ExchangeAccount, accountCreated:=False))
        Assert.True(ServerWelcomeModel.CanAdvance(WelcomeStepKind.ExchangeAccount, accountCreated:=True))
        Assert.True(ServerWelcomeModel.CanAdvance(WelcomeStepKind.ShareEnable, accountCreated:=False))
        Assert.True(ServerWelcomeModel.CanAdvance(WelcomeStepKind.SharePort, accountCreated:=False))
        Assert.True(ServerWelcomeModel.CanAdvance(WelcomeStepKind.ExchangeStatement, accountCreated:=False))
    End Sub

    ' --- the port (A-2) ---------------------------------------------------------------------------------

    <Theory>
    <InlineData(1023, False)>
    <InlineData(1024, True)>
    <InlineData(2222, True)>
    <InlineData(65535, True)>
    <InlineData(65536, False)>
    <InlineData(0, False)>
    Public Sub A_fixed_port_is_valid_only_inside_the_user_range(port As Integer, expected As Boolean)
        Assert.Equal(expected, ServerWelcomeModel.IsFixedPortValid(port))
    End Sub

    <Fact>
    Public Sub Letting_the_system_choose_stores_zero_and_a_fixed_choice_stores_its_number()
        Assert.Equal(0, ServerWelcomeModel.PortToStore(systemChoice:=True, fixedPort:=2222))
        Assert.Equal(2222, ServerWelcomeModel.PortToStore(systemChoice:=False, fixedPort:=2222))
    End Sub

    ' --- defaults and owners (section 6: the model's defaults equal the owning groups') ------------------

    <Fact>
    Public Sub The_welcome_suggests_the_same_numbers_the_share_settings_own()
        Assert.Equal(ShareSettings.SuggestedFixedPort, ServerWelcomeModel.SuggestedFixedPort)
        Assert.Equal(ShareSettings.UnsetPort, ServerWelcomeModel.SystemChoicePort)
        Assert.Equal(ShareSettings.MinFixedPort, ServerWelcomeModel.MinFixedPort)
        Assert.Equal(ShareSettings.MaxFixedPort, ServerWelcomeModel.MaxFixedPort)
    End Sub

    <Fact>
    Public Sub The_suggested_third_party_range_is_the_documented_default()
        Assert.Equal("40000-40100", ServerWelcomeModel.DefaultThirdPartyRange)
    End Sub

    <Fact>
    Public Sub The_dismissal_values_are_the_names_the_settings_hive_owns()
        Assert.Equal("Welcome_ShareDismissed", ServerWelcomeModel.DismissShareValue)
        Assert.Equal("Welcome_ExchangeDismissed", ServerWelcomeModel.DismissExchangeValue)
    End Sub

End Class
