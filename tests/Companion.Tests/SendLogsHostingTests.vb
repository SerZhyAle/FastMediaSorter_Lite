Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The Share Manager's side of ticket 182 (L-4, L-5, L-6, L-8): what only it supplies to the shared hosting
''' report, the four entry points of "Send the log to the author", the sentences of the flow in all 13
''' languages, and the one state line it writes at start. The report, the sources and the mail-client
''' decision themselves are tested once in <c>LogPackageSourcesTests</c> / <c>HostingReportTests</c>, which
''' are compiled into this project too.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class SendLogsHostingTests

    Private Shared ReadOnly Codes As String() = {"ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh"}

    ' ------------------------------------------------------------------------- the three names ------

    <Fact>
    Public Sub The_report_asks_the_scm_about_exactly_the_two_frozen_service_names()
        Assert.Equal(ServiceControl.ServiceName, HostingReport.ShareServiceName)
        Assert.Equal(ExchangeService.ServiceName, HostingReport.ExchangeServiceName)
        Assert.Equal("FastMediaSorterCompanionSFTP", HostingReport.ShareServiceName)
        Assert.Equal("FastMediaSorterExchangeServer", HostingReport.ExchangeServiceName)
    End Sub

    ' ------------------------------------------------------------------ what only this program knows ------

    <Fact>
    Public Sub Registering_hands_the_host_mode_and_the_reachability_to_the_shared_report()
        HostingContext.Register()
        Assert.NotNull(HostingReport.HostingProbes.HostModeProvider)
        Assert.NotNull(HostingReport.HostingProbes.ReachabilityProvider)
        Assert.Contains(HostingReport.HostingProbes.HostModeProvider(), {"None", "UserSession", "SystemService"})
        ' The live report then carries the Share Manager's own reading instead of "not determined".
        Dim text As String = HostingReport.Build(HostingProbesLive.Live())
        Assert.DoesNotContain("not determined by this program", text)
    End Sub

    <Fact>
    Public Sub The_reachability_line_says_whether_but_never_where()
        Dim status As New WorkerStatus With {
            .Running = True, .ListenPort = 22023, .Fingerprint = "SHA256:FINGERPRINT-NEEDLE", .Username = "alice-login", .Password = "pw-needle",
            .Reachability = New WorkerReachability With {
                .LanAddress = "192.168.1.5", .ExternalHost = "193.178.50.43", .ExternalPort = 22023,
                .ExternalPortChecked = True, .ExternalPortOpen = False, .IsCgnat = False, .PortMapMethod = "upnp", .Ipv6Address = "2001:db8::1"}}
        Dim line As String = HostingContext.ReachabilityLine(status)
        Assert.Equal("server running; external port checked yes; external port open no; CGNAT no; port mapping upnp", line)
        For Each forbidden As String In {"193.178.50.43", "192.168.1.5", "2001:db8", "22023", "FINGERPRINT", "alice-login", "pw-needle"}
            Assert.DoesNotContain(forbidden, line)
        Next
        Assert.Equal("", HostingContext.ReachabilityLine(Nothing))
        Assert.Contains("no reachability facts", HostingContext.ReachabilityLine(New WorkerStatus With {.Running = False}))
        ' An unanswered external check is "no", and its result is not guessed.
        Dim unchecked As String = HostingContext.ReachabilityLine(New WorkerStatus With {
            .Running = True, .Reachability = New WorkerReachability With {.ExternalPortChecked = False, .ExternalPortOpen = True}})
        Assert.Contains("external port checked no", unchecked)
        Assert.DoesNotContain("external port open", unchecked)
    End Sub

    ' ------------------------------------------------------------------ one flow, four entries -----------

    <Fact>
    Public Sub Every_entry_point_opens_the_same_flow()
        Dim forms As String = Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "FastMediaSorterCompanion", "Forms")
        Dim entries As String() = {
            Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "FastMediaSorterCompanion", "TrayContext.vb"),
            Path.Combine(forms, "Share_Settings_Form.vb"),
            Path.Combine(forms, "Share_Settings_Form.Exchange.vb"),
            Path.Combine(forms, "Share_Settings_Form.Hosting.vb")}
        For Each entryFile As String In entries
            Dim text As String = File.ReadAllText(entryFile)
            Assert.True(text.Contains("SendLogsFlow.RunAsync("), Path.GetFileName(entryFile) & " does not open SendLogsFlow")
        Next
        ' The two groups carry the same link text as the footer (one key, 13 languages).
        For Each group As String In {"Share_Settings_Form.Exchange.vb", "Share_Settings_Form.Hosting.vb"}
            Assert.Contains("Localization.T(""Отправить журнал автору..""", File.ReadAllText(Path.Combine(forms, group)))
        Next
    End Sub

    ' --------------------------------------------------------------------------- the sentences -----------

    <Fact>
    Public Sub An_unreadable_log_line_names_the_entry_and_a_reason_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Codes
                Localization.CurrentCode = code
                For Each failure As LogPackage.LogReadFailure In {LogPackage.LogReadFailure.AccessDenied, LogPackage.LogReadFailure.InUse, LogPackage.LogReadFailure.Other}
                    Dim line As String = LogPackage.UnreadableLine("exchange/exchange.log", failure)
                    Assert.Contains("exchange/exchange.log", line)
                    Assert.DoesNotContain("{0}", line)
                    Assert.DoesNotContain("{1}", line)
                    ' The reason really was translated: it is not the Russian key in a non-Russian language.
                    If code <> "ru" AndAlso code <> "uk" Then Assert.DoesNotContain(LogPackage.ReasonKey(failure), line)
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_no_mail_client_message_carries_the_full_path_in_every_language()
        Dim saved As String = Localization.CurrentCode
        Dim zipPath As String = "C:\Users\someone\AppData\Local\Temp\fms-logs-26.10.7.1200-20261007-120000-abcd1234.zip"
        Try
            For Each code As String In Codes
                Localization.CurrentCode = code
                Dim text As String = Localization.TF("Почтовая программа не найдена. Архив скопирован в буфер обмена и показан в папке. Полный путь к архиву: {0}", zipPath)
                Assert.Contains(zipPath, text)
                Assert.DoesNotContain("{0}", text)
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_share_managers_manual_route_checks_for_a_handler_before_it_starts_a_mailto_and_prints_the_path()
        Dim src As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "FastMediaSorterCompanion", "Forms", "SendLogsFlow.vb"))
        Dim clipboard As Integer = src.IndexOf("Clipboard.SetFileDropList", StringComparison.Ordinal)
        Dim reveal As Integer = src.IndexOf("RevealPackage(zipPath)", clipboard, StringComparison.Ordinal)
        Dim decision As Integer = src.IndexOf("MailSender.ManualRouteFor(MailSender.HasMailHandler())", StringComparison.Ordinal)
        Dim mailto As Integer = src.IndexOf("""mailto:""", decision, StringComparison.Ordinal)
        Assert.True(clipboard > 0 AndAlso reveal > clipboard, "the clipboard drop and the folder reveal stay")
        Assert.True(decision > reveal AndAlso decision < mailto, "the handler check must sit between the reveal and the blind mailto")
        Assert.Contains("Полный путь к архиву: {0}", src.Substring(decision, mailto - decision))
        Assert.Contains("zipPath)", src.Substring(decision, mailto - decision))
    End Sub
    ' ------------------------------------------------------------------ the helper's log, in the texts ----

    <Fact>
    Public Sub The_helper_exit_texts_point_at_the_logs_folder_not_the_state_directory()
        Dim untrusted As String = ExchangeService.HelperExitMeaning(11)
        Assert.Contains("%TEMP%", untrusted)
        Assert.Contains("install-exchange-service.log", untrusted)
        Dim unexpected As String = ExchangeService.HelperExitMeaning(99)
        Assert.Contains("install-exchange-service.log", unexpected)
        Assert.Contains("logs", unexpected)
    End Sub

    ' ---------------------------------------------------------------------- the state line (L-8) --------

    <Fact>
    Public Sub The_share_manager_writes_its_state_line_once_at_start_after_the_language_is_known()
        Dim program As String = File.ReadAllText(Path.Combine(ExchangeTestSupport.RepoRoot(), "src", "FastMediaSorterCompanion", "Program.vb"))
        Assert.Contains("HostingContext.Register()", program)
        Assert.Contains("HostingContext.LogStartState()", program)
        ' Registered before anything can ask, written only on the primary-instance path (a forwarding launch
        ' has already returned) - so one line per session, not one per double-click.
        Dim register As Integer = program.IndexOf("HostingContext.Register()", StringComparison.Ordinal)
        Dim log As Integer = program.IndexOf("HostingContext.LogStartState()", StringComparison.Ordinal)
        Dim forward As Integer = program.IndexOf("ForwardToRunningInstance(payload)", StringComparison.Ordinal)
        Assert.True(register < forward AndAlso forward < log, "the state line must come after the single-instance decision")
        Assert.Equal(1, program.Split({"HostingContext.LogStartState()"}, StringSplitOptions.None).Length - 1)
    End Sub

    <Fact>
    Public Sub The_state_line_of_this_machine_is_one_line_without_a_secret()
        Dim line As String = HostingReport.BuildLiveStateLine("Share Manager", "26.10.7.1200")
        Assert.StartsWith("State: Share Manager 26.10.7.1200; os=", line)
        Assert.DoesNotContain(vbLf, line)
        Assert.Contains("shareService=", line)
        Assert.Contains("exchangeService=", line)
    End Sub

End Class
