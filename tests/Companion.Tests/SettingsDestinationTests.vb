Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class SettingsDestinationTests
    Private Const Flags As BindingFlags = BindingFlags.NonPublic Or BindingFlags.Instance

    <Theory>
    <InlineData("network", SettingsDestination.Sftp)>
    <InlineData("hosting", SettingsDestination.Sftp)>
    <InlineData("exchange", SettingsDestination.ExchangeServer)>
    <InlineData("exchange-account", SettingsDestination.Manager)>
    <InlineData("scheduled", SettingsDestination.Manager)>
    <InlineData("", SettingsDestination.Manager)>
    Public Sub Legacy_routes_keep_client_and_schedule_independent(page As String, destination As SettingsDestination)
        Assert.Equal(destination, SettingsRoutes.Destination(page))
    End Sub

    <Fact>
    Public Sub Installed_components_are_independent_and_manager_is_always_available()
        Assert.True(SettingsRoutes.Available(SettingsDestination.Manager, False, False))
        Assert.True(SettingsRoutes.Available(SettingsDestination.Sftp, True, False))
        Assert.False(SettingsRoutes.Available(SettingsDestination.ExchangeServer, True, False))
        Assert.True(SettingsRoutes.Available(SettingsDestination.ExchangeServer, False, True))
        Assert.False(SettingsRoutes.Available(SettingsDestination.Sftp, False, True))
        Assert.Equal(3, [Enum].GetValues(Of SettingsDestination)().Select(Function(d) SettingsRoutes.ContextKey(d)).Distinct().Count())
        Assert.Equal(3, [Enum].GetValues(Of SettingsDestination)().Select(Function(d) SettingsRoutes.BoundsKey(d)).Distinct().Count())
    End Sub

    <Fact>
    Public Sub Real_windows_own_disjoint_pages_and_preserve_an_unapplied_sftp_port_when_another_opens()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(Sub()
                Using manager As New Share_Settings_Form(), sftp As New Share_Settings_Form(destination:=SettingsDestination.Sftp),
                    exchange As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    Assert.Equal({"startup", "scheduled", "exchange-account", "help"}, Pages(manager).Keys.ToArray())
                    Assert.Equal({"network", "hosting"}, Pages(sftp).Keys.ToArray())
                    Assert.Equal({"exchange"}, Pages(exchange).Keys.ToArray())
                    Dim port = DirectCast(GetType(Share_Settings_Form).GetField("numPort", Flags).GetValue(sftp), NumericUpDown)
                    Dim before = port.Value
                    port.Value = If(before = 65535, before - 1, before + 1)
                    Dim draft = port.Value
                    sftp.RefreshWorkerSnapshot(New WorkerStatus With {.Running = True, .ListenPort = 33222})
                    manager.Show()
                    Assert.Contains(ContextHelp.Inventory.surfaces, Function(row) row.type = manager.GetType().FullName)
                    DirectCast(manager, IThemeAware).ApplyTheme(ThemePalette.Light)
                    ContextHelpUi.Attach(manager)
                    Assert.NotNull(ContextHelpUi.AttachedButton(manager))
                    exchange.Show()
                    Application.DoEvents()
                    Assert.Equal(draft, port.Value)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("PortPending", Flags).Invoke(sftp, Nothing)))
                    sftp.ConfirmDraftClose = Function(isExchange) DialogResult.Cancel
                    Dim cancelClose As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.False(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(sftp, {cancelClose})))
                    Assert.True(cancelClose.Cancel)
                    Assert.Equal(draft, port.Value)
                    sftp.ConfirmDraftClose = Function(isExchange) DialogResult.No
                    Dim discardClose As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(sftp, {discardClose})))
                    Assert.False(discardClose.Cancel)
                    Assert.Null(GetType(Share_Settings_Form).GetField("numPort", Flags).GetValue(manager))
                    Assert.Null(GetType(Share_Settings_Form).GetField("_secAccount", Flags).GetValue(exchange))
                    manager.Hide()
                    exchange.Hide()
                End Using
            End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    <Fact>
    Public Sub Repeated_targeted_requests_activate_one_modeless_window_and_select_the_requested_page()
        ExchangeTestSupport.OnUi(Sub()
            Using manager As New MainWindow(Nothing)
                manager.OpenSettings(SettingsDestination.Manager, "startup")
                Dim first = DirectCast(GetType(MainWindow).GetField("_settingsWindow", Flags).GetValue(manager), Share_Settings_Form)
                manager.OpenSettings(SettingsDestination.Manager, "scheduled")
                Dim second = DirectCast(GetType(MainWindow).GetField("_settingsWindow", Flags).GetValue(manager), Share_Settings_Form)
                Assert.Same(first, second)
                Assert.False(second.Modal)
                Assert.Equal("scheduled", second.ActiveHelpPageId)
                Assert.True(second.Visible)
                second.Hide()
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub Reciprocal_link_opens_the_other_window_beside_the_first_without_owning_or_blocking_it()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(Sub()
                Using main As New MainWindow(Nothing)
                    main.OpenSettings(SettingsDestination.Manager, "startup")
                    Dim windows = DirectCast(GetType(MainWindow).GetField("_settingsWindows", Flags).GetValue(main),
                        Dictionary(Of SettingsDestination, Share_Settings_Form))
                    Dim manager = windows(SettingsDestination.Manager)
                    ' The manager window's own reciprocal link raises this typed request; the coordinator answers it.
                    Dim raise = DirectCast(GetType(Share_Settings_Form).GetField("NavigateRequestedEvent", Flags).GetValue(manager),
                        MulticastDelegate)
                    raise.DynamicInvoke(SettingsDestination.ExchangeServer)
                    Dim exchange = windows(SettingsDestination.ExchangeServer)
                    Assert.True(manager.Visible AndAlso exchange.Visible)
                    Assert.False(manager.Modal OrElse exchange.Modal)
                    Assert.NotSame(manager.Owner, exchange)
                    Assert.NotSame(exchange.Owner, manager)
                    raise.DynamicInvoke(SettingsDestination.ExchangeServer)
                    Assert.Same(exchange, windows(SettingsDestination.ExchangeServer))
                    Assert.Equal(2, windows.Count)
                    ' Closing one editor closes only that editor.
                    exchange.Close()
                    Assert.False(windows.ContainsKey(SettingsDestination.ExchangeServer))
                    Assert.True(manager.Visible AndAlso Not manager.IsDisposed)
                    manager.Hide()
                End Using
            End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    <Fact>
    Public Sub A_window_built_earlier_never_overwrites_a_preference_another_window_changed_since()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsSplitTest_" & Guid.NewGuid().ToString("N")
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(Sub()
                ' Both windows are built (and so have read their preferences) before either one saves anything.
                Using manager As New Share_Settings_Form(), sftp As New Share_Settings_Form(destination:=SettingsDestination.Sftp)
                    Dim openOnStart = DirectCast(GetType(Share_Settings_Form).GetField("chkOpenOnStart", Flags).GetValue(manager), CheckBox)
                    Dim connections = DirectCast(GetType(Share_Settings_Form).GetField("numMaxConns", Flags).GetValue(sftp), NumericUpDown)
                    Dim lanOnly = DirectCast(GetType(Share_Settings_Form).GetField("chkLanOnly", Flags).GetValue(sftp), CheckBox)
                    openOnStart.Checked = True
                    connections.Value = 25
                    Dim fresh As New ShareSettings()
                    fresh.Load()
                    ' The SFTP window saved its own field from a stale object without reverting the manager's.
                    Assert.True(fresh.OpenWindowOnStartup, "The manager's preference was overwritten by a stale copy.")
                    Assert.Equal(25, fresh.MaxConnections)
                    ' And the other way round: the manager saves again after the SFTP window changed things.
                    openOnStart.Checked = False
                    fresh.Load()
                    Assert.False(fresh.OpenWindowOnStartup)
                    Assert.Equal(25, fresh.MaxConnections)
                    Assert.False(lanOnly.Checked)
                End Using
            End Sub)
        Finally
            CompanionGlobals.SettingsAppNameOverride = previousApp
            Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    <Fact>
    Public Sub Legacy_combined_context_seeds_each_destination_once_and_is_never_rewritten()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsSplitTest_" & Guid.NewGuid().ToString("N")
        Const hive As String = "FastMediaSorter"
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            Dim legacyHosting = "{""SchemaVersion"":2,""LastPageId"":""hosting"",""Pages"":{}}"
            SaveSetting(scratch, hive, "Share_SettingsContext", legacyHosting)
            ExchangeTestSupport.OnUi(Sub()
                Assert.Equal("hosting", OpenAndClose(SettingsDestination.Sftp))
                Assert.Equal("exchange", OpenAndClose(SettingsDestination.ExchangeServer))
                ' A remembered SFTP page means nothing to the manager: it opens on its own first page.
                Assert.Equal("startup", OpenAndClose(SettingsDestination.Manager))
            End Sub)
            ' The retired record is a read-only seed; every destination now owns a record of its own.
            Assert.Equal(legacyHosting, GetSetting(scratch, hive, "Share_SettingsContext", ""))
            For Each destination In [Enum].GetValues(Of SettingsDestination)()
                Assert.NotEqual(String.Empty, GetSetting(scratch, hive, SettingsRoutes.ContextKey(destination), ""))
                Assert.NotEqual(String.Empty, GetSetting(scratch, hive, SettingsRoutes.BoundsKey(destination), ""))
            Next
            ' Idempotent: once a destination has its own record, a later change of the legacy seed is ignored.
            SaveSetting(scratch, hive, "Share_SettingsContext", "{""SchemaVersion"":2,""LastPageId"":""network"",""Pages"":{}}")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("hosting", OpenAndClose(SettingsDestination.Sftp)))
            ' A malformed optional record falls back to safe defaults and leaves the legacy record alone.
            SaveSetting(scratch, hive, SettingsRoutes.ContextKey(SettingsDestination.Sftp), "{not json")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("network", OpenAndClose(SettingsDestination.Sftp)))
            Assert.Contains("""network""", GetSetting(scratch, hive, "Share_SettingsContext", ""))
        Finally
            CompanionGlobals.SettingsAppNameOverride = previousApp
            Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    ''' <summary>Opens a real destination window, reports the page it restored and closes it, which
    ''' writes that destination's own context and bounds.</summary>
    Private Shared Function OpenAndClose(destination As SettingsDestination) As String
        ' A throw inside a window handler would otherwise raise WinForms' modal exception dialog and hang the run.
        Dim errors As New List(Of Exception)()
        Dim catcher As Threading.ThreadExceptionEventHandler = Sub(sender, e) errors.Add(e.Exception)
        AddHandler Application.ThreadException, catcher
        Try
            Dim page = OpenAndCloseCore(destination)
            Assert.True(errors.Count = 0, destination.ToString() & ": " & String.Join(" | ", errors.Select(Function(item) item.ToString())))
            Return page
        Finally
            RemoveHandler Application.ThreadException, catcher
        End Try
    End Function

    Private Shared Function OpenAndCloseCore(destination As SettingsDestination) As String
        Using window As New Share_Settings_Form(destination:=destination)
            ' A field the status snapshot has not filled yet must never raise the modal draft prompt here.
            window.ConfirmDraftClose = Function(isExchange) DialogResult.No
            ' Lazy page loaders reach for the service manager and the network; context restore needs neither.
            GetType(Share_Settings_Form).GetField("_exLoaded", Flags).SetValue(window, True)
            GetType(Share_Settings_Form).GetField("_hostingLoaded", Flags).SetValue(window, True)
            window.Show()
            Application.DoEvents()
            Dim page = window.ActiveHelpPageId
            window.Close()
            Return page
        End Using
    End Function

    <SettingsCaptureFact>
    Public Sub Help_captures_show_real_component_windows_in_each_declared_locale_and_theme()
        Dim captures = Environment.GetEnvironmentVariable("FMS_SETTINGS_HELP_CAPTURE")
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim previousLanguage = Localization.CurrentCode
        Dim previousTheme = ThemeService.Mode
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            For Each code In Localization.Codes
                Localization.CurrentCode = code
                For Each dark In {False, True}
                    ExchangeTestSupport.OnUi(Sub()
                        For Each destination In {SettingsDestination.Manager, SettingsDestination.Sftp, SettingsDestination.ExchangeServer}
                            Using window As New Share_Settings_Form(destination:=destination)
                                window.SelectPage(If(destination = SettingsDestination.Sftp, "network", If(destination = SettingsDestination.ExchangeServer, "exchange", "startup")))
                                GetType(Share_Settings_Form).GetField("_exLoaded", Flags).SetValue(window, True)
                                GetType(Share_Settings_Form).GetField("_hostingLoaded", Flags).SetValue(window, True)
                                window.Show()
                                If destination = SettingsDestination.ExchangeServer Then
                                    ' The help picture shows an installed, configured, stopped server - not the probe-free
                                    ' "state could not be read" a window shows before its first read. Synthetic data only.
                                    Dim report As New ExchangeProcess.InspectReport With {.Answered = True, .ConfigPresent = True,
                                        .Accounts = 2, .Devices = 3, .CertPresent = True, .Listen = ":22022",
                                        .Fingerprint = "00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF",
                                        .PublicEndpoint = "exchange.example.test:22022", .DataDir = "C:\Example\ExchangeServer"}
                                    GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", Flags).Invoke(window,
                                        {New ExchangeSnapshot With {.Report = report}})
                                End If
                                ThemeService.SetMode(If(dark, ThemeMode.Dark, ThemeMode.Light))
                                ThemeService.Attach(window)
                                ContextHelpUi.Attach(window)
                                Assert.NotNull(ContextHelpUi.AttachedButton(window))
                                Assert.True(ContextHelpUi.AttachedButton(window).Visible)
                                window.ClientSize = New Size(1000, 620)
                                ThemeApplier.ApplyTree(window, If(dark, ThemePalette.Dark, ThemePalette.Light))
                                Application.DoEvents()
                                Assert.NotEmpty(Pages(window))
                                Dim topic = If(destination = SettingsDestination.Sftp, "sftp-settings", If(destination = SettingsDestination.ExchangeServer, "exchange-server-settings", "share-settings"))
                                IO.Directory.CreateDirectory(IO.Path.Combine(captures, code))
                                Using bitmap As New Bitmap(window.Width, window.Height)
                                    window.DrawToBitmap(bitmap, New Rectangle(Point.Empty, bitmap.Size))
                                    bitmap.Save(IO.Path.Combine(captures, code, topic & If(dark, "-dark.png", "-light.png")))
                                End Using
                                window.Hide()
                            End Using
                        Next
                    End Sub)
                Next
            Next
        Finally
            Localization.CurrentCode = previousLanguage
            ThemeService.SetMode(previousTheme)
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
        End Try
    End Sub

    Private Shared Function Pages(form As Share_Settings_Form) As Dictionary(Of String, Panel)
        Return DirectCast(GetType(Share_Settings_Form).GetField("_pages", Flags).GetValue(form), Dictionary(Of String, Panel))
    End Function
End Class

Public NotInheritable Class SettingsCaptureFactAttribute
    Inherits FactAttribute

    Public Sub New()
        If String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FMS_SETTINGS_HELP_CAPTURE")) Then
            Skip = "Set FMS_SETTINGS_HELP_CAPTURE to explicitly capture the real settings windows."
        End If
    End Sub
End Class
