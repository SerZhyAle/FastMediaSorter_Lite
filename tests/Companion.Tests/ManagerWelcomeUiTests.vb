Option Strict On
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<CollectionDefinition("manager-welcome", DisableParallelization:=True)>
Public Class ManagerWelcomeCollection
End Class

<Collection("manager-welcome")>
Public Class ManagerWelcomeUiTests
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic

    Private Shared Function Field(Of T)(target As Object, name As String) As T
        Return DirectCast(target.GetType().GetField(name, Flags).GetValue(target), T)
    End Function

    Private Shared Function CallMethod(target As Object, name As String, ParamArray args As Object()) As Object
        Return target.GetType().GetMethod(name, Flags).Invoke(target, args)
    End Function

    Private Shared Sub Isolated(action As Action)
        ExchangeTestSupport.OnUi(Sub()
            Dim previous As String = CompanionGlobals.SettingsAppNameOverride
            Dim previousCode As String = Localization.CurrentCode
            Dim scratch As String = "FmsManagerWelcomeTest_" & Guid.NewGuid().ToString("N")
            CompanionGlobals.SettingsAppNameOverride = scratch
            Try
                Localization.CurrentCode = "en"
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                action()
            Finally
                CompanionGlobals.SettingsAppNameOverride = previous
                Localization.CurrentCode = previousCode
                Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
                GetType(ManagerWelcomeStore).GetField("capturedFresh", BindingFlags.NonPublic Or BindingFlags.Static).SetValue(Nothing, Nothing)
            End Try
        End Sub)
    End Sub

    <Fact>
    Public Sub Fresh_pending_survives_whole_settings_saves_and_an_explicit_decline_changes_no_preferences()
        Isolated(Sub()
            Assert.True(ManagerWelcomeStore.CaptureFreshProfile())
            Assert.Equal(ManagerWelcomeModel.Pending, ManagerWelcomeStore.State())
            Dim settings As New ShareSettings()
            settings.Load()
            settings.Save()
            Assert.Equal(ManagerWelcomeModel.Pending, ManagerWelcomeStore.State())
            ManagerWelcomeStore.SetState(ManagerWelcomeModel.Done)
            settings.Save()
            Assert.Equal(ManagerWelcomeModel.Done, ManagerWelcomeStore.State())
            Using host As New MainWindow(), wizard As New Manager_Welcome_Form(host)
                CallMethod(wizard, "ShowStep", ManagerWelcomeStep.Introduction)
                CallMethod(Field(Of Button)(wizard, "declineButton"), "OnClick", EventArgs.Empty)
                settings.Load()
                Assert.True(settings.WelcomeManagerDone)
                Assert.False(settings.OpenWindowOnStartup)
                Assert.False(settings.AutostartEnabled)
                Assert.False(settings.LanOnly)
                Assert.Equal(0, settings.ListenPort)
                Assert.False(settings.ServerFeaturesEnabled)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub A_regular_settings_save_does_not_invent_an_onboarding_marker_for_an_upgrade()
        Isolated(Sub()
            Dim settings As New ShareSettings()
            settings.Save()
            Assert.Equal("", ManagerWelcomeStore.State())
            Assert.False(ManagerWelcomeStore.CaptureFreshProfile())
            Assert.False(ManagerWelcomeModel.ShouldOpen(ManagerWelcomeStore.State(), False, False, False))
        End Sub)
    End Sub

    <Fact>
    Public Sub Folder_Next_is_blocked_and_Later_leaves_the_pending_marker_and_existing_choices()
        Isolated(Sub()
            ManagerWelcomeStore.SetState(ManagerWelcomeModel.Pending)
            Dim settings As New ShareSettings With {.ListenPort = 3456, .OpenWindowOnStartup = False}
            settings.Save()
            Using host As New MainWindow(), wizard As New Manager_Welcome_Form(host)
                CallMethod(wizard, "ShowStep", ManagerWelcomeStep.Folder)
                Assert.False(Field(Of Button)(wizard, "nextButton").Enabled)
                CallMethod(wizard, "OnNext", Nothing, EventArgs.Empty)
                Assert.Equal(ManagerWelcomeStep.Folder, Field(Of ManagerWelcomeStep)(wizard, "position"))
                CallMethod(Field(Of Button)(wizard, "laterButton"), "OnClick", EventArgs.Empty)
                Assert.Equal(ManagerWelcomeModel.Pending, ManagerWelcomeStore.State())
                settings.Load()
                Assert.Equal(3456, settings.ListenPort)
                Assert.False(settings.OpenWindowOnStartup)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Untouched_port_and_basic_questions_write_no_defaults_and_preserve_external_settings_changes()
        Isolated(Sub()
            Using host As New MainWindow(), wizard As New Manager_Welcome_Form(host)
                Dim other As New ShareSettings With {.MaxConnections = 42}
                other.Save()
                Dim before As String(,) = Microsoft.VisualBasic.Interaction.GetAllSettings(App_name, Second_App_Name)
                DirectCast(CallMethod(wizard, "CommitPortAsync"), Threading.Tasks.Task).GetAwaiter().GetResult()
                DirectCast(CallMethod(wizard, "CommitBasicsAsync"), Threading.Tasks.Task).GetAwaiter().GetResult()
                Dim after As String(,) = Microsoft.VisualBasic.Interaction.GetAllSettings(App_name, Second_App_Name)
                Assert.Equal(before.Cast(Of String)(), after.Cast(Of String)())
                other.Load()
                Assert.Equal(42, other.MaxConnections)
                Assert.False(other.OpenWindowOnStartup)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Finish_copies_the_main_windows_sentence_color_and_glyph_and_reopen_starts_at_finish_only_when_green()
        Isolated(Sub()
            Using host As New MainWindow()
                Dim status As New WorkerStatus With {.Running = True, .ListenPort = 2222, .PortSupported = True,
                    .Roots = New List(Of ShareFolder) From {New ShareFolder With {.hostPath = "C:\fixture"}}}
                GetType(MainWindow).GetField("_status", Flags).SetValue(host, status)
                Dim state As ManagerWelcomeStatus = host.WelcomeStatusSnapshot()
                Assert.True(state.Green)
                Assert.Equal(ThemePalette.ReadableText(Color.ForestGreen, ThemeService.Palette.SurfaceWindow), state.Color)
                Assert.Equal("shareStatusOk", state.Glyph)
                Using wizard As New Manager_Welcome_Form(host)
                    Assert.Equal(ManagerWelcomeStep.Finish, Field(Of ManagerWelcomeStep)(wizard, "position"))
                    CallMethod(wizard, "RenderFinish", state)
                    Assert.Equal(state.Text, Field(Of Label)(wizard, "stateLabel").Text)
                    Assert.Equal(state.Color, Field(Of Label)(wizard, "stateLabel").ForeColor)
                    Assert.True(Field(Of Button)(wizard, "pairButton").Enabled)
                    GetType(MainWindow).GetField("_rootsWithoutAccess", Flags).SetValue(host, 1)
                    state = host.WelcomeStatusSnapshot()
                    Assert.False(state.Green)
                    Assert.Equal("shareStatusError", state.Glyph)
                    CallMethod(wizard, "RenderFinish", state)
                    Assert.Equal(state.Color, Field(Of Label)(wizard, "stateLabel").ForeColor)
                    Assert.False(Field(Of Button)(wizard, "pairButton").Enabled)
                End Using
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Welcome_surfaces_never_send_preferences_paths_or_endpoints_to_logs()
        For Each name In {"Server_Welcome_Form.vb", "Share_Enable_Form.vb", "Manager_Welcome_Form.vb", "MainWindow.Welcome.vb"}
            Dim source As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(Path.Combine("Forms", name)))
            Assert.DoesNotContain("AppFileLogger", source, StringComparison.Ordinal)
            Assert.DoesNotContain("Console.Write", source, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub Automatic_handover_omits_the_old_share_half_and_is_suppressed_for_silent_or_folder_launches()
        Dim main As String = ExchangeTestSupport.ReadSource("Forms/MainWindow.vb")
        Assert.Contains("_serverWelcomeAllowed", main)
        Assert.Contains("_initialFolder.Length = 0", main)
        Assert.Contains("includeShare:=False", main)
        Dim tray As String = ExchangeTestSupport.ReadSource("TrayContext.vb")
        Assert.True(tray.IndexOf("CaptureFreshProfile", StringComparison.Ordinal) < tray.IndexOf("ResumeShareIfEnabled()", StringComparison.Ordinal))
        Assert.Contains("silentLaunch, Not String.IsNullOrEmpty(initialFolder)", tray)
        Assert.DoesNotContain("OpenWindowOnStartup = True", tray)
    End Sub
End Class
