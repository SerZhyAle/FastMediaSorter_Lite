Option Strict On

Imports System.Collections.Generic

' <summary>
' Which half of the server welcome a step belongs to (spec 185 section 3.1): the folder share server (block A)
' or the Media Exchange Server (block B). Each half opens once per machine, on its own.
' </summary>
Friend Enum WelcomeHalf
    Share = 0
    Exchange = 1
End Enum

' <summary>
' The steps of spec 185 section 3.2 as the window knows them. A-1 and A-2 are the share half; B-1 and B-4 are the
' exchange half. B-4 runs the existing account-and-start wizard, which carries B-5 and B-6 inside it.
' </summary>
Friend Enum WelcomeStepKind
    ShareEnable = 0
    SharePort = 1
    ShareDone = 2
    ExchangeStatement = 3
    ExchangeAccount = 4
    ExchangeDone = 5
End Enum

''' <summary>
''' The pure rules of the server welcome (spec 185): when a half opens, which steps it has, what the port choice may
''' be and when Next may leave the mandatory step. No form, no registry, no process here - the window reads the facts
''' and passes them in, so each rule is provable in a test.
''' </summary>
Friend Module ServerWelcomeModel

    ''' <summary>Registry value "Later" writes for the share half (ShareSettings owns the hive write).</summary>
    Friend Const DismissShareValue As String = "Welcome_ShareDismissed"

    ''' <summary>Registry value "Later" writes for the exchange half.</summary>
    Friend Const DismissExchangeValue As String = "Welcome_ExchangeDismissed"

    ''' <summary>The port the welcome suggests for a fixed choice (spec 185 A-2, the same as ShareSettings.SuggestedFixedPort).</summary>
    Friend Const SuggestedFixedPort As Integer = 2222

    ''' <summary>0 means "the system chooses once and keeps it" (ShareSettings.UnsetPort, today's default).</summary>
    Friend Const SystemChoicePort As Integer = 0

    ''' <summary>The lowest port a fixed choice may take (ShareSettings.MinFixedPort).</summary>
    Friend Const MinFixedPort As Integer = 1024

    ''' <summary>The highest port a fixed choice may take (ShareSettings.MaxFixedPort).</summary>
    Friend Const MaxFixedPort As Integer = 65535

    ''' <summary>The default third-party port range the exchange half names (spec 185 B-2, the ExchangeSettings default).</summary>
    Friend Const DefaultThirdPartyRange As String = "40000-40100"

    ''' <summary>
    ''' Whether a half opens on this launch: its component is installed, it was never configured, and "Later" was not
    ''' recorded for it. The exchange half also needs the shipping gate open - until then it does not exist at all.
    ''' </summary>
    Friend Function ShouldOpen(half As WelcomeHalf, configured As Boolean, dismissed As Boolean,
                               componentPresent As Boolean, gateOpen As Boolean) As Boolean
        If Not componentPresent Then Return False
        If half = WelcomeHalf.Exchange AndAlso Not gateOpen Then Return False
        Return Not configured AndAlso Not dismissed
    End Function

    ''' <summary>The steps of a half, in the order the person meets them.</summary>
    Friend Function StepsFor(half As WelcomeHalf) As IReadOnlyList(Of WelcomeStepKind)
        If half = WelcomeHalf.Share Then
            Return New List(Of WelcomeStepKind) From {WelcomeStepKind.ShareEnable, WelcomeStepKind.SharePort, WelcomeStepKind.ShareDone}
        End If
        Return New List(Of WelcomeStepKind) From {WelcomeStepKind.ExchangeStatement, WelcomeStepKind.ExchangeAccount, WelcomeStepKind.ExchangeDone}
    End Function

    ''' <summary>Whether a number is a fixed port the welcome may store (never 0 here - 0 is the system choice).</summary>
    Friend Function IsFixedPortValid(port As Integer) As Boolean
        Return port >= MinFixedPort AndAlso port <= MaxFixedPort
    End Function

    ''' <summary>The port value a choice stores: 0 for "the system chooses", otherwise the typed fixed port.</summary>
    Friend Function PortToStore(systemChoice As Boolean, fixedPort As Integer) As Integer
        Return If(systemChoice, SystemChoicePort, fixedPort)
    End Function

    ''' <summary>
    ''' Whether Next may leave a step. Only the account step is mandatory (spec 185 rule 5): it waits for an account to
    ''' have been made. Every other step can be skipped, with its default named on the step.
    ''' </summary>
    Friend Function CanAdvance(kind As WelcomeStepKind, accountCreated As Boolean) As Boolean
        If kind = WelcomeStepKind.ExchangeAccount Then Return accountCreated
        Return True
    End Function

End Module
