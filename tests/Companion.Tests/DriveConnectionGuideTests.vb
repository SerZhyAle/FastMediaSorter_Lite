Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification L0308 section 3: the Google Drive page leads every person to a working connection - a help entry beside
''' "Sign in.." that opens the six-step guide at the page's own anchor in the interface language, and a recovery sentence
''' for each state that names its own next action instead of one generic "something went wrong".
''' </summary>
Public Class DriveConnectionGuideTests

    Private Shared ReadOnly Locales As String() = {"en", "ru", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    <Fact>
    Public Sub The_help_entry_opens_the_Manager_settings_page_at_its_Google_Drive_section_in_every_language()
        Assert.Equal("share-settings.drive", Share_Settings_Form.DriveHelpId)
        For Each code As String In Locales
            Assert.Equal(code & "/share-settings.html#drive", ContextHelp.Resolve(Share_Settings_Form.DriveHelpId, code))
        Next
    End Sub

    <Fact>
    Public Sub Every_language_page_holds_the_six_step_guide_opened_at_the_anchor()
        Dim root As String = ExchangeTestSupport.RepoRoot()
        For Each code As String In Locales
            Dim html As String = File.ReadAllText(Path.Combine(root, "docs", "help", code, "share-settings.html"))
            Dim section As Match = Regex.Match(html, "<details id=""drive"" open><summary>[^<]*</summary><ol class=""order"">(?<steps>.*?)</ol>", RegexOptions.Singleline)
            Assert.True(section.Success, code & ": the Google Drive section is not opened with its steps first")
            Assert.Equal(6, Regex.Matches(section.Groups("steps").Value, "<li>").Count)
        Next
    End Sub

    <Fact>
    Public Sub Each_recovery_names_its_own_next_action()
        Dim generic As String = DriveChannelText.FailureDetail(ExchangeDriveFailure.Unavailable)
        Dim offline As String = DriveChannelText.FailureDetail(ExchangeDriveFailure.Offline)
        Dim rejected As String = DriveChannelText.FailureDetail(ExchangeDriveFailure.ClientRejected)
        Assert.NotEqual(generic, rejected)
        Assert.NotEqual(offline, rejected)
        Assert.NotEqual(DriveChannelText.FailureDetail(ExchangeDriveFailure.DriveFull), generic)
        Assert.NotEqual(DriveChannelText.FailureDetail(ExchangeDriveFailure.DailyLimit), generic)

        Dim denied As String = DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.ConsentDenied, False)
        Dim restricted As String = DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.AccountRestricted, False)
        Assert.NotEqual(denied, restricted)
        Assert.NotEqual(DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.TokenRefused, False), restricted)
        Assert.NotEqual(DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.NoBrowser, False), DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.Offline, False))

        ' revoked or expired is its own sentence, not the first-time invitation
        Assert.NotEqual(DriveChannelText.StateDetail(DriveChannelState.SignedOut, ExchangeDriveFailure.None),
                        DriveChannelText.StateDetail(DriveChannelState.SignedOut, ExchangeDriveFailure.SignedOut))
        ' signed in with no answered round is not "linked"
        Assert.NotEqual(DriveChannelText.StateDetail(DriveChannelState.SignedIn, ExchangeDriveFailure.None, reached:=False),
                        DriveChannelText.StateDetail(DriveChannelState.SignedIn, ExchangeDriveFailure.None, reached:=True))
        ' not configured names the remedy, and a sign-in attempt in that state says the same
        Assert.Equal(DriveChannelText.StateDetail(DriveChannelState.NotConfigured, ExchangeDriveFailure.None),
                     DriveChannelText.SignInOutcome(ExchangeDriveSignInOutcome.NotConfigured, False))
    End Sub

    <Fact>
    Public Sub A_client_Google_refuses_leaves_the_page_unavailable_with_sign_out_offered_not_signed_out()
        ' ClientRejected is not a lost sign-in: the state line must not invite a new sign-in that cannot work
        Dim detail As String = DriveChannelText.StateDetail(DriveChannelState.Unavailable, ExchangeDriveFailure.ClientRejected)
        Assert.Equal(DriveChannelText.FailureDetail(ExchangeDriveFailure.ClientRejected), detail)
        Assert.NotEqual(DriveChannelText.StateDetail(DriveChannelState.SignedOut, ExchangeDriveFailure.SignedOut), detail)
    End Sub

End Class
