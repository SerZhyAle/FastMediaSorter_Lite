#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 0.27 section 25 (items CW, CX, CY, CZ, DD, DR):
''' Tests for connection diagnosis engine, probes, 13-language parity,
''' first-failure stop, redacted support bundle strings, and outside visibility.
''' </summary>
Public Class ConnectionDiagnosisTests

    Private Shared ReadOnly Locales13 As String() = {
        "en", "ru", "uk", "de", "es", "fr", "it", "ja", "ko", "pl", "pt", "tr", "zh"
    }

    <Fact>
    Public Sub All15StepsHaveNonEmptyTextsInAll13Languages()
        Dim allSteps As ConnectionDiagnosisStep() = CType([Enum].GetValues(GetType(ConnectionDiagnosisStep)), ConnectionDiagnosisStep())
        Assert.Equal(15, allSteps.Length)

        For Each stepVal In allSteps
            Assert.False(String.IsNullOrWhiteSpace(stepVal.IdOf()))
            For Each localeCode In Locales13
                Dim text As String = ConnectionDiagnosisLocalization.GetStepText(stepVal, localeCode)
                Assert.False(String.IsNullOrWhiteSpace(text), $"Step {stepVal.IdOf()} missing text for locale {localeCode}")
            Next
        Next
    End Sub

    <Fact>
    Public Sub CanonicalStepTextsMatchContractTableCY()
        ' Verbatim checks for EN, RU, UK from DEVICE-EXCHANGE 0.27 item CY
        Dim pcAsleepEn = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "en")
        Assert.Equal("Switch on and wake the device that runs the server or the sharing service, and keep it from going to sleep.", pcAsleepEn)

        Dim pcAsleepRu = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "ru")
        Assert.Equal("Включите и разбудите устройство, на котором работает сервер или сервис шаринга, и не давайте ему засыпать.", pcAsleepRu)

        Dim pcAsleepUk = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.PcAsleep, "uk")
        Assert.Equal("Увімкніть і розбудіть пристрій, на якому працює сервер або сервіс шарингу, і не давайте йому засинати.", pcAsleepUk)

        Dim pubNetEn = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "en")
        Assert.Equal("On the Windows computer, set this network to private: Windows keeps other devices out of a public network.", pubNetEn)

        Dim pubNetRu = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "ru")
        Assert.Equal("На компьютере с Windows переключите эту сеть на «Частная»: из общедоступной сети Windows не пускает другие устройства.", pubNetRu)

        Dim pubNetUk = ConnectionDiagnosisLocalization.GetStepText(ConnectionDiagnosisStep.WindowsPublicNetwork, "uk")
        Assert.Equal("На комп'ютері з Windows перемкніть цю мережу на «Приватна»: до загальнодоступної мережі Windows не пускає інші пристрої.", pubNetUk)
    End Sub

    <Fact>
    Public Sub CanonicalMessageTextsMatchContractTableCZ()
        ' Verbatim checks for EN, RU, UK from DEVICE-EXCHANGE 0.27 item CZ
        Dim badCredsEn = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "en")
        Assert.Equal("The login or the password is wrong.", badCredsEn)

        Dim badCredsRu = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "ru")
        Assert.Equal("Логин или пароль неверны.", badCredsRu)

        Dim badCredsUk = ConnectionDiagnosisLocalization.GetMessageText("bad-credentials", "uk")
        Assert.Equal("Логін або пароль неправильні.", badCredsUk)

        Dim unreachEn = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "en")
        Assert.Equal("Cannot reach the server.", unreachEn)

        Dim unreachRu = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "ru")
        Assert.Equal("Не удаётся связаться с сервером.", unreachRu)

        Dim unreachUk = ConnectionDiagnosisLocalization.GetMessageText("unreachable-server", "uk")
        Assert.Equal("Не вдається зв'язатися із сервером.", unreachUk)
    End Sub

    <Fact>
    Public Sub MessageCodesResolveToNonEmptyTextsInAll13Languages()
        Dim codes As String() = {
            "bad-credentials", "bad-credentials-code", "device-revoked", "signed-out", "version-unsupported",
            "tls-required", "id-collision", "capacity", "port-unavailable", "unavailable", "rate-limited",
            "protection-unsupported", "invitation-invalid", "login-taken", "password-policy", "limit-reached",
            "limit-reached-clipboard", "not-permitted", "declined", "timeout", "unsupported",
            "unreachable-server", "unreachable-device", "unanswered", "tls-failed", "certificate-changed",
            "empty-credential", "server-offline", "resource-locked", "clipboard-empty",
            "share-paused", "e2e-missing", "key-changed", "server-no-e2e", "sealed-failed", "refused"
        }

        For Each code In codes
            Dim resolvedMsg = ConnectionDiagnosisMessages.OfCode(code)
            Assert.NotNull(resolvedMsg)
            Assert.Equal(code, resolvedMsg.Code)

            For Each localeCode In Locales13
                Dim text As String = ConnectionDiagnosisLocalization.GetMessageText(code, localeCode)
                Assert.False(String.IsNullOrWhiteSpace(text), $"Message {code} missing text for locale {localeCode}")
            Next
        Next
    End Sub

    <Fact>
    Public Sub WindowsSettingsUriMappedCorrectly()
        Assert.Equal("ms-settings:network-status", ConnectionDiagnosisStep.WindowsPublicNetwork.WindowsSettingsUriOf())
        Assert.Equal("ms-settings:dateandtime", ConnectionDiagnosisStep.DeviceClock.WindowsSettingsUriOf())
        Assert.Null(ConnectionDiagnosisStep.PcAsleep.WindowsSettingsUriOf())
        Assert.Null(ConnectionDiagnosisStep.Operator.WindowsSettingsUriOf())
    End Sub

    <Fact>
    Public Sub RouteClassificationCategorizesRfc1918AndLanAsNetwork()
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("192.168.1.5"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("10.0.0.1"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("172.16.0.10"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("myhost.local"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("phone.lan"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("home.home"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("::1"))
        Assert.Equal(ConnectionDiagnosisApplies.Network, ConnectionDiagnosisEngine.RouteOf("fe80::1"))

        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("8.8.8.8"))
        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("example.com"))
        Assert.Equal(ConnectionDiagnosisApplies.Internet, ConnectionDiagnosisEngine.RouteOf("share.mydomain.org"))
    End Sub

    Private Class MockProbeProvider
        Implements IConnectionDiagnosisProbeProvider

        Public ReadOnly Outcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome)
        Public ReadOnly ExecutedSteps As New List(Of ConnectionDiagnosisStep)

        Public Sub New(configuredOutcomes As Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome))
            If configuredOutcomes IsNot Nothing Then
                For Each kvp In configuredOutcomes
                    Outcomes(kvp.Key) = kvp.Value
                Next
            End If
        End Sub

        Public Function ProbeAsync(stepVal As ConnectionDiagnosisStep, context As ConnectionDiagnosisContext, ct As CancellationToken) As Task(Of ProbeOutcome) Implements IConnectionDiagnosisProbeProvider.ProbeAsync
            ExecutedSteps.Add(stepVal)
            Dim outcome As ProbeOutcome = ProbeOutcome.Passed
            If Outcomes.TryGetValue(stepVal, outcome) Then
                Return Task.FromResult(outcome)
            End If
            Return Task.FromResult(ProbeOutcome.Passed)
        End Function
    End Class

    <Fact>
    Public Async Function LadderStopsAtFirstFailedStep() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.GuestNetwork, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.Vpn, ProbeOutcome.Failed},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, ProbeOutcome.Passed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim ctx As New ConnectionDiagnosisContext("192.168.1.50", 22, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Assert.Equal(ConnectionDiagnosisStep.Vpn, result.FirstFailedStep)
        Assert.Equal(4, mockProbes.ExecutedSteps.Count)
        Assert.Contains(ConnectionDiagnosisStep.Vpn, mockProbes.ExecutedSteps)
        Assert.DoesNotContain(ConnectionDiagnosisStep.WindowsPublicNetwork, mockProbes.ExecutedSteps)
    End Function

    <Fact>
    Public Async Function LadderContinuesWhenProbeReturnsCouldNotCheck() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.CouldNotCheck},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.GuestNetwork, ProbeOutcome.CouldNotCheck},
            {ConnectionDiagnosisStep.Vpn, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.WindowsPublicNetwork, ProbeOutcome.Failed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim ctx As New ConnectionDiagnosisContext("192.168.1.50", 22, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Assert.Equal(ConnectionDiagnosisStep.WindowsPublicNetwork, result.FirstFailedStep)
        Assert.Equal(5, mockProbes.ExecutedSteps.Count)
        Assert.Equal(ProbeOutcome.CouldNotCheck, result.StepOutcomes(ConnectionDiagnosisStep.PcAsleep))
        Assert.Equal(ProbeOutcome.CouldNotCheck, result.StepOutcomes(ConnectionDiagnosisStep.GuestNetwork))
        Assert.Equal(ProbeOutcome.Failed, result.StepOutcomes(ConnectionDiagnosisStep.WindowsPublicNetwork))
    End Function

    <Fact>
    Public Async Function RedactedDiagnosticStringContainsNoSensitiveData() As Task
        Dim mockOutcomes As New Dictionary(Of ConnectionDiagnosisStep, ProbeOutcome) From {
            {ConnectionDiagnosisStep.PcAsleep, ProbeOutcome.Passed},
            {ConnectionDiagnosisStep.AddressLan, ProbeOutcome.Failed}
        }
        Dim mockProbes As New MockProbeProvider(mockOutcomes)
        Dim engine As New ConnectionDiagnosisEngine(mockProbes)

        Dim secretHost As String = "192.168.1.188"
        Dim ctx As New ConnectionDiagnosisContext(secretHost, 8080, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisMessages.UnreachableServer)
        Dim result As ConnectionDiagnosisResult = Await engine.DiagnoseAsync(ctx, CancellationToken.None)

        Dim diagnosticText As String = result.ToRedactedDiagnosticString()

        Assert.DoesNotContain(secretHost, diagnosticText)
        Assert.DoesNotContain("8080", diagnosticText)
        Assert.Contains("walk=pc-asleep:passed,address-lan:failed", diagnosticText)
        Assert.Contains("fail=address-lan", diagnosticText)
        Assert.Contains("msg=unreachable-server", diagnosticText)
        Assert.Contains("route=network", diagnosticText)

        Dim report As String = LogPackage.BuildReport(Function() New List(Of KeyValuePair(Of String, Object))(), Path.GetTempPath(), Array.Empty(Of String)(), "none")
        Assert.Contains("[connection diagnosis]", report)
        Assert.Contains("walk=pc-asleep:passed,address-lan:failed", report)
        Assert.DoesNotContain(secretHost, report)
    End Function

    Private Class MockOutsideProbe
        Implements IOutsideVisibilityProbe

        Public CallCount As Integer = 0
        Public DesiredVerdict As OutsideVisibilityVerdict

        Public Sub New(verdict As OutsideVisibilityVerdict)
            DesiredVerdict = verdict
        End Sub

        Public Function CheckVisibilityAsync(host As String, port As Integer, ct As CancellationToken) As Task(Of OutsideVisibilityVerdict) Implements IOutsideVisibilityProbe.CheckVisibilityAsync
            CallCount += 1
            Return Task.FromResult(DesiredVerdict)
        End Function
    End Class

    <Fact>
    Public Async Function OutsideVisibilityCheckerCallsServiceOnlyOnRequest() As Task
        Dim probeReachable As New MockOutsideProbe(OutsideVisibilityVerdict.Reachable)
        Dim checker As New OutsideVisibilityChecker(probeReachable)

        Assert.Equal(0, probeReachable.CallCount)

        Dim res = Await checker.CheckAsync("203.0.113.10", 443, CancellationToken.None)
        Assert.Equal(1, probeReachable.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.Reachable, res.Verdict)

        Dim probeNotReachable As New MockOutsideProbe(OutsideVisibilityVerdict.NotReachable)
        Dim checkerNotReachable As New OutsideVisibilityChecker(probeNotReachable)
        Dim res2 = Await checkerNotReachable.CheckAsync("example.com:8443", CancellationToken.None)
        Assert.Equal(1, probeNotReachable.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.NotReachable, res2.Verdict)

        ' Invalid inputs should return CouldNotCheck without calling probe
        Dim probeNeverCalled As New MockOutsideProbe(OutsideVisibilityVerdict.Reachable)
        Dim checkerInvalid As New OutsideVisibilityChecker(probeNeverCalled)
        Dim resInvalid = Await checkerInvalid.CheckAsync("", 0, CancellationToken.None)
        Assert.Equal(0, probeNeverCalled.CallCount)
        Assert.Equal(OutsideVisibilityVerdict.CouldNotCheck, resInvalid.Verdict)
    End Function

End Class
#End If
