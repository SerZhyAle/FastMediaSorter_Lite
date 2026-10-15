Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic

''' <summary>The gated, explain-first exchange flow. No IO or secret values enter these decisions.</summary>
Friend Module ExchangeFirstRunModel
    Friend Function GateOpen() As Boolean
        Return ExchangeFeatures.BuildOffers() OrElse ExchangeFeatures.DeveloperSwitchOn()
    End Function

    Friend Function ShouldOpen(component As Boolean, gate As Boolean, answered As Boolean, accounts As Integer,
            dismissed As Boolean, silent As Boolean, folderRequest As Boolean) As Boolean
        Return component AndAlso gate AndAlso answered AndAlso accounts = 0 AndAlso Not dismissed AndAlso
            Not silent AndAlso Not folderRequest
    End Function

    Friend Function StepsFor(serverEdition As Boolean, serviceExists As Boolean) As IReadOnlyList(Of WelcomeStepKind)
        Dim steps As New List(Of WelcomeStepKind) From {WelcomeStepKind.ExchangeStatement, WelcomeStepKind.ExchangeDoors}
        If serverEdition AndAlso Not serviceExists Then steps.Add(WelcomeStepKind.ExchangeService)
        steps.AddRange({WelcomeStepKind.ExchangeAddress, WelcomeStepKind.ExchangeAccount,
                       WelcomeStepKind.ExchangeCertificate, WelcomeStepKind.ExchangeDone})
        Return steps
    End Function

    Friend Function Completed(outcome As ExchangeWizardOutcome) As Boolean
        Return outcome = ExchangeWizardOutcome.Done OrElse outcome = ExchangeWizardOutcome.DoneNoAgent
    End Function

    Friend Function CanAdvance(kind As WelcomeStepKind, outcome As ExchangeWizardOutcome) As Boolean
        Return kind <> WelcomeStepKind.ExchangeAccount OrElse Completed(outcome)
    End Function

    ''' <summary>Stable group row identities, shared with the replay/hand-over placement gate.</summary>
    Friend Function HandoverRows() As String()
        Return {"_pnlXAccounts", "_lvXDevices", "_txtXEndpoint", "numXRangeLow", "_txtXFp",
                "_btnXStart", "_btnXStop", "_btnXSvcStart", "_btnXSvcStop", "_btnXRegister"}
    End Function
End Module
