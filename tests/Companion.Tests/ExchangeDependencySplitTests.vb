Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 223 T-1 and T-2: the dependency split between the exchange CLIENT and the local server component. The client
''' path - the "Exchange account" page, the sign-in, the published shares - never launches or reads the local
''' fms-exchange-server binary; the component is a shipping-gate marker, not a functional dependency. The source scan that
''' holds the same truth file-by-file is <c>ExchangeClientGuardTests</c> in Lite.Tests; what is held here is the window:
'''
'''   * with the features on and NO component installed the account page exists and the administration page does not -
'''     the opened state of the gate must need no component (the flip of <c>ExchangeFeatures.Offered</c> is ticket 223 T-3);
'''   * with the component present both pages exist, in the one navigation list, the account page before Help;
'''   * with the features on and no account configured the host constructs no agent - the dormant rule (198 rule 2)
'''     holds in the opened state. The developer switch is the honest stand-in for the flip and lives only inside a test.
'''
''' This class shares the ui-language collection with <c>ExchangeGroupTests</c> (the other class that builds the settings
''' window over the exe-directory seam), so that seam never overlaps its runs. The gate's own inputs - the developer
''' switch and "the component is installed" - are NOT written process-wide: <c>ExchangeFeatures.ForFlow</c> sets them for
''' this test's flow of execution only. The first version wrote the environment variable, and a class in another
''' collection that reads the live gate (a check on the switch, then a form built a moment later) saw it come on between
''' the two and failed - a flake of the guard itself, reproduced in a filtered run.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ExchangeDependencySplitTests
    Implements IDisposable

    Private ReadOnly _dirs As New List(Of String)()

    Public Sub New()
        ExchangeTestSupport.Reset()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ExchangeTestSupport.Reset()
        For Each d As String In _dirs
            ExchangeTestSupport.DeleteQuietly(d)
        Next
    End Sub

    ''' <summary>A scratch folder standing in for the component's directory, with or without the binary.</summary>
    Private Function Component(withExe As Boolean) As String
        Dim d As String = ExchangeTestSupport.MakeFakeComponent(withExe, withExe, False)
        _dirs.Add(d)
        Return d
    End Function

    Private Shared Function Sections(form As Share_Settings_Form) As CollapsibleSection()
        Return DirectCast(GetType(Share_Settings_Form).GetMethod("AllSettingsSections", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(form, Nothing), CollapsibleSection())
    End Function

    Private Shared Function SectionField(form As Share_Settings_Form, name As String) As CollapsibleSection
        Return DirectCast(GetType(Share_Settings_Form).GetField(name, BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(form), CollapsibleSection)
    End Function

    ''' <summary>A settings port with no account in it that RECORDS every call: the dormant proof asserts what was asked of the
    ''' registry, not only that nothing was constructed. A read of the store's own values is all a dormant start may do.</summary>
    Private NotInheritable Class RecordingSettings
        Implements IExchangeSettingsPort

        Public ReadOnly Reads As New List(Of String)()
        Public ReadOnly Writes As New List(Of String)()

        Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
            Reads.Add(name)
            Return ""
        End Function

        Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
            Writes.Add("set " & name)
        End Sub

        Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
            Writes.Add("delete " & name)
        End Sub
    End Class

    <Fact>
    Public Sub With_the_features_on_and_no_component_the_account_page_exists_and_the_administration_page_does_not()
        ' the opened state of the gate (the switch stands in for the flip of ticket 223 T-3): the client page needs no
        ' component, and the administration page still never exists without the binary
        Using ExchangeFeatures.ForFlow(switchOn:=True, componentInstalled:=False)
            ExchangeProcess.ExeDirectoryOverride = Component(withExe:=False)
            ExchangeTestSupport.OnUi(Sub()
                                         Using form As New Share_Settings_Form()
                                             Assert.Null(SectionField(form, "_secExchange"))
                                             Assert.NotNull(SectionField(form, "_secAccount"))
                                             Dim keys As String() = Sections(form).Select(Function(s) s.Key).ToArray()
                                             Assert.Equal({"startup", "scheduled", "exchange-account", "help"}, keys)
                                             Assert.DoesNotContain("exchange", keys)
                                         End Using
                                     End Sub)
        End Using
    End Sub

    <Fact>
    Public Sub The_build_as_shipped_with_no_switch_and_no_component_offers_the_account_page_and_no_administration()
        ' ticket 223 T-3: the opened gate itself, with no stand-in - every Windows PC can join the ONE exchange server of its owner
        ' without a local server: no switch, no component, the account page is there and the administration page is not
        Using ExchangeFeatures.ForFlow(switchOn:=False, componentInstalled:=False)
            Assert.True(ExchangeFeatures.IsEnabled(), "the build offers the clients (ExchangeFeatures.Offered)")
            ExchangeProcess.ExeDirectoryOverride = Component(withExe:=False)
            ExchangeTestSupport.OnUi(Sub()
                                         Using form As New Share_Settings_Form()
                                             Assert.Null(SectionField(form, "_secExchange"))
                                             Assert.NotNull(SectionField(form, "_secAccount"))
                                             Dim keys As String() = Sections(form).Select(Function(s) s.Key).ToArray()
                                             Assert.Equal({"startup", "scheduled", "exchange-account", "help"}, keys)
                                         End Using
                                     End Sub)
        End Using
    End Sub

    <Fact>
    Public Sub With_the_component_present_client_preferences_still_have_their_own_window()
        ' the owner's rule of 2026-10-12 unchanged: where the component is installed WITH the program, the client side is on too
        Using ExchangeFeatures.ForFlow(switchOn:=False, componentInstalled:=True)
            ExchangeProcess.ExeDirectoryOverride = Component(withExe:=True)
            ExchangeTestSupport.OnUi(Sub()
                                         Using form As New Share_Settings_Form()
                                             Assert.Null(SectionField(form, "_secExchange"))
                                             Assert.NotNull(SectionField(form, "_secAccount"))
                                             Dim keys As String() = Sections(form).Select(Function(s) s.Key).ToArray()
                                             Assert.Equal({"startup", "scheduled", "exchange-account", "help"}, keys)
                                         End Using
                                     End Sub)
        End Using
    End Sub

    <Fact>
    Public Sub The_features_on_and_no_account_configured_construct_no_agent()
        ' the dormant rule (198 rule 2) in the opened state: the gate passes and the no-account answer still constructs
        ' nothing, and the port it asked records that the only thing asked of the registry was a read of the account
        ' store's own values - so "no registry value" is measured here, not only argued from "no agent"
        Using ExchangeFeatures.ForFlow(switchOn:=True)
            Assert.True(ExchangeFeatures.IsEnabled(), "the switch must open the features for this test to say anything")
            Dim settings As New RecordingSettings()
            Dim channels As Integer = 0
            Dim ports As New ExchangeAgentPorts With {
                .Accounts = New ExchangeAccountStore(settings),
                .StartLocalChannel = Function(h)
                                         channels += 1
                                         Return Nothing
                                     End Function}
            Dim built As Integer = 0
            Dim create As Func(Of ExchangeAgentPorts, ExchangeDeviceAgent) =
                Function(p)
                    built += 1
                    Return New ExchangeDeviceAgent(p)
                End Function
            ' not asserted: the process-wide ExchangeDeviceAgent.Constructed and ExchangeDeviceHost.Current - the classes of the
            ' "Exchange device host" collection build agents in parallel with this one. The factory below is this test's own count.
            Assert.Null(ExchangeDeviceHost.StartIfConfigured(ExchangeFeatures.IsEnabled(), ports, create))

            Assert.Equal(0, built)                                          ' no agent: no connection, no listener, no process
            Assert.Equal(0, channels)                                       ' no local channel: no pipe
            Assert.Empty(settings.Writes)                                   ' no value created, changed or deleted (the device id is made at the first enrollment)
            Assert.NotEmpty(settings.Reads)                                 ' the check really ran against this port
            For Each name As String In settings.Reads
                Assert.Contains(name, ExchangeAccountStore.AllNames)        ' and it read nothing but the account store's own values
            Next
        End Using
    End Sub

End Class
