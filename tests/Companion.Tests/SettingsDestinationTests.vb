Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class SettingsDestinationTests
    Private Const Flags As BindingFlags = BindingFlags.NonPublic Or BindingFlags.Instance

    <Theory>
    <InlineData("network", SettingsDestination.Manager)>
    <InlineData("hosting", SettingsDestination.Manager)>
    <InlineData("exchange", SettingsDestination.ExchangeServer)>
    <InlineData("exchange-account", SettingsDestination.Manager)>
    <InlineData("scheduled", SettingsDestination.Manager)>
    <InlineData("", SettingsDestination.Manager)>
    Public Sub Legacy_routes_keep_client_and_schedule_independent(page As String, destination As SettingsDestination)
        Assert.Equal(destination, SettingsRoutes.Destination(page))
    End Sub

    <Fact>
    Public Sub Installed_components_are_independent_and_manager_is_always_available()
        Assert.True(SettingsRoutes.Available(SettingsDestination.Manager, False))
        Assert.True(SettingsRoutes.Available(SettingsDestination.Manager, True))
        Assert.False(SettingsRoutes.Available(SettingsDestination.ExchangeServer, False))
        Assert.True(SettingsRoutes.Available(SettingsDestination.ExchangeServer, True))
        ' Two windows own pages (253 R-1): two records, two sets of bounds.
        Assert.Equal(2, [Enum].GetValues(Of SettingsDestination)().Length)
        Assert.Equal(2, [Enum].GetValues(Of SettingsDestination)().Select(Function(d) SettingsRoutes.ContextKey(d)).Distinct().Count())
        Assert.Equal(2, [Enum].GetValues(Of SettingsDestination)().Select(Function(d) SettingsRoutes.BoundsKey(d)).Distinct().Count())
    End Sub

    ''' <summary>A page is available by the component that owns it, not by the window that holds it (253 R-4): the SFTP pages
    ''' need the worker payload, the exchange page the local exchange server, the exchange account neither.</summary>
    <Theory>
    <InlineData("network", True, False, True)>
    <InlineData("hosting", True, False, True)>
    <InlineData("network", False, True, False)>
    <InlineData("hosting", False, True, False)>
    <InlineData("exchange", True, False, False)>
    <InlineData("exchange", False, True, True)>
    <InlineData("exchange-account", False, False, True)>
    <InlineData("scheduled", False, False, True)>
    <InlineData("startup", False, False, True)>
    <InlineData("", False, False, True)>
    Public Sub A_page_is_available_by_the_component_that_owns_it(page As String, sftp As Boolean, exchange As Boolean, expected As Boolean)
        Assert.Equal(expected, SettingsRoutes.PageAvailable(page, sftp, exchange))
    End Sub

    <Fact>
    Public Sub Only_the_two_sftp_pages_are_sftp_pages()
        Assert.True(SettingsRoutes.IsSftpPage("network"))
        Assert.True(SettingsRoutes.IsSftpPage("hosting"))
        For Each page In {"startup", "exchange", "scheduled", "exchange-account", "help", ""}
            Assert.False(SettingsRoutes.IsSftpPage(page), page)
        Next
    End Sub

    <Fact>
    Public Sub Real_windows_own_their_pages_and_preserve_an_unapplied_sftp_port_when_another_opens()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(Sub()
                Using manager As New Share_Settings_Form(), exchange As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                    ' One window holds the common preferences and the SFTP sharing pages (253 R-2); the other the exchange server.
                    Assert.Equal({"startup", "network", "hosting", "scheduled", "exchange-account", "drive", "help"}, Pages(manager).Keys.ToArray())
                    Assert.Equal({"exchange"}, Pages(exchange).Keys.ToArray())
                    Dim port = DirectCast(GetType(Share_Settings_Form).GetField("numPort", Flags).GetValue(manager), NumericUpDown)
                    Dim before = port.Value
                    port.Value = If(before = 65535, before - 1, before + 1)
                    Dim draft = port.Value
                    manager.RefreshWorkerSnapshot(New WorkerStatus With {.Running = True, .ListenPort = 33222})
                    manager.Show()
                    Assert.Contains(ContextHelp.Inventory.surfaces, Function(row) row.type = manager.GetType().FullName)
                    DirectCast(manager, IThemeAware).ApplyTheme(ThemePalette.Light)
                    ContextHelpUi.Attach(manager)
                    Assert.NotNull(ContextHelpUi.AttachedButton(manager))
                    exchange.Show()
                    Application.DoEvents()
                    Assert.Equal(draft, port.Value)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("PortPending", Flags).Invoke(manager, Nothing)))
                    manager.ConfirmDraftClose = Function(isExchange) DialogResult.Cancel
                    Dim cancelClose As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.False(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(manager, {cancelClose})))
                    Assert.True(cancelClose.Cancel)
                    Assert.Equal(draft, port.Value)
                    manager.ConfirmDraftClose = Function(isExchange) DialogResult.No
                    Dim discardClose As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(manager, {discardClose})))
                    Assert.False(discardClose.Cancel)
                    Assert.Null(GetType(Share_Settings_Form).GetField("numPort", Flags).GetValue(exchange))
                    Assert.Null(GetType(Share_Settings_Form).GetField("_secAccount", Flags).GetValue(exchange))
                    manager.Hide()
                    exchange.Hide()
                End Using
            End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    ''' <summary>The port prompt is about the port draft and nothing else (253 R-5): closing the merged window with no pending
    ''' draft never asks, with or without the SFTP component.</summary>
    <Fact>
    Public Sub Closing_the_manager_window_never_asks_about_the_port_unless_a_draft_is_pending()
        ExchangeTestSupport.Reset()
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Try
            ExchangeTestSupport.OnUi(Sub()
                Using manager As New Share_Settings_Form()
                    Dim asked As Boolean
                    manager.ConfirmDraftClose = Function(isExchange)
                                                    asked = True
                                                    Return DialogResult.Cancel
                                                End Function
                    Assert.False(CBool(GetType(Share_Settings_Form).GetMethod("PortPending", Flags).Invoke(manager, Nothing)))
                    Dim close As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(manager, {close})))
                    Assert.False(asked, "A general window asked about the port with no draft pending.")
                    Assert.False(close.Cancel)
                End Using
            End Sub)
            ' Without the worker payload the SFTP pages do not exist at all (253 R-4): no empty group, no port field to prompt for.
            WorkerProcess.ExeDirectoryOverride = Path.Combine(worker, "absent")
            ExchangeTestSupport.OnUi(Sub()
                Using manager As New Share_Settings_Form()
                    Dim asked As Boolean
                    manager.ConfirmDraftClose = Function(isExchange)
                                                    asked = True
                                                    Return DialogResult.Cancel
                                                End Function
                    Assert.Equal({"startup", "scheduled", "exchange-account", "drive", "help"}, Pages(manager).Keys.ToArray())
                    Assert.Null(GetType(Share_Settings_Form).GetField("numPort", Flags).GetValue(manager))
                    Assert.Null(GetType(Share_Settings_Form).GetField("_navSftpHeading", Flags).GetValue(manager))
                    Dim close As New FormClosingEventArgs(CloseReason.UserClosing, False)
                    Assert.True(CBool(GetType(Share_Settings_Form).GetMethod("CanCloseDraft", Flags).Invoke(manager, {close})))
                    Assert.False(asked)
                End Using
            End Sub)
        Finally
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    ''' <summary>The scope is always visible (253 R-3): a heading names the component above Network and Hosting, and the pages
    ''' and the search hits carry it too.</summary>
    <Fact>
    Public Sub The_sftp_pages_name_their_component_in_the_rail_the_title_and_the_search()
        ExchangeTestSupport.Reset()
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousLanguage = Localization.CurrentCode
        Try
            For Each code In {"ru", "en", "de"}
                Localization.CurrentCode = code
                ExchangeTestSupport.OnUi(Sub()
                    Using manager As New Share_Settings_Form()
                        Dim heading = DirectCast(GetType(Share_Settings_Form).GetField("_navSftpHeading", Flags).GetValue(manager), Label)
                        Assert.NotNull(heading)
                        Assert.Equal(Localization.T("Общий доступ SFTP"), heading.Text)
                        Dim titles = DirectCast(GetType(Share_Settings_Form).GetField("_pageTitles", Flags).GetValue(manager),
                            Dictionary(Of String, Label))
                        For Each page In {"network", "hosting"}
                            Assert.Contains(heading.Text, titles(page).Text)
                        Next
                        For Each page In {"startup", "scheduled", "exchange-account", "help"}
                            Assert.DoesNotContain(heading.Text, titles(page).Text)
                        Next
                        ' The heading sits in the rail directly above the Network row and nowhere else.
                        Dim rail = DirectCast(GetType(Share_Settings_Form).GetField("_navigation", Flags).GetValue(manager), FlowLayoutPanel)
                        Dim network = DirectCast(GetType(Share_Settings_Form).GetField("_navButtons", Flags).GetValue(manager),
                            Dictionary(Of String, Button))("network")
                        Assert.Equal(rail.Controls.GetChildIndex(network) - 1, rail.Controls.GetChildIndex(heading))
                        Assert.False(heading.TabStop)
                        ' A live language switch re-translates the composed page title as well.
                        Localization.CurrentCode = If(code = "ru", "en", "ru")
                        Dim switched = DirectCast(GetType(Share_Settings_Form).GetMethod("RefreshSettingsLanguage", Flags), MethodInfo)
                        switched.Invoke(manager, {Nothing, EventArgs.Empty})
                        Assert.Equal(Localization.T("Общий доступ SFTP"), heading.Text)
                        Assert.Contains(heading.Text, titles("network").Text)
                        Localization.CurrentCode = code
                    End Using
                End Sub)
            Next
        Finally
            Localization.CurrentCode = previousLanguage
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(worker)
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
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsSplitTest_" & Guid.NewGuid().ToString("N")
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            ExchangeTestSupport.OnUi(Sub()
                ' Two editors of the one merged window are built (and so have read their preferences) before either saves anything:
                ' the common preferences and the SFTP ones are separate records of the same file.
                Using general As New Share_Settings_Form(), sftp As New Share_Settings_Form()
                    Dim openOnStart = DirectCast(GetType(Share_Settings_Form).GetField("chkOpenOnStart", Flags).GetValue(general), CheckBox)
                    Dim connections = DirectCast(GetType(Share_Settings_Form).GetField("numMaxConns", Flags).GetValue(sftp), NumericUpDown)
                    Dim lanOnly = DirectCast(GetType(Share_Settings_Form).GetField("chkLanOnly", Flags).GetValue(sftp), CheckBox)
                    openOnStart.Checked = True
                    connections.Value = 25
                    Dim fresh As New ShareSettings()
                    fresh.Load()
                    ' The SFTP editor saved its own field from a stale object without reverting the general one.
                    Assert.True(fresh.OpenWindowOnStartup, "The general preference was overwritten by a stale copy.")
                    Assert.Equal(25, fresh.MaxConnections)
                    ' And the other way round: the general editor saves again after the SFTP one changed things.
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
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    <Fact>
    Public Sub Legacy_combined_context_seeds_each_destination_once_and_is_never_rewritten()
        ExchangeTestSupport.Reset()
        Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsSplitTest_" & Guid.NewGuid().ToString("N")
        Const hive As String = "FastMediaSorter"
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            Dim legacyHosting = "{""SchemaVersion"":2,""LastPageId"":""hosting"",""Pages"":{}}"
            SaveSetting(scratch, hive, SettingsRoutes.LegacyContextKey, legacyHosting)
            ExchangeTestSupport.OnUi(Sub()
                ' The merged window holds the page the retired combined record remembered.
                Assert.Equal("hosting", OpenAndClose(SettingsDestination.Manager))
                Assert.Equal("exchange", OpenAndClose(SettingsDestination.ExchangeServer))
            End Sub)
            ' The retired record is a read-only seed; every destination now owns a record of its own.
            Assert.Equal(legacyHosting, GetSetting(scratch, hive, SettingsRoutes.LegacyContextKey, ""))
            For Each destination In [Enum].GetValues(Of SettingsDestination)()
                Assert.NotEqual(String.Empty, GetSetting(scratch, hive, SettingsRoutes.ContextKey(destination), ""))
                Assert.NotEqual(String.Empty, GetSetting(scratch, hive, SettingsRoutes.BoundsKey(destination), ""))
            Next
            ' Idempotent: once a destination has its own record, a later change of the legacy seed is ignored.
            SaveSetting(scratch, hive, SettingsRoutes.LegacyContextKey, "{""SchemaVersion"":2,""LastPageId"":""network"",""Pages"":{}}")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("hosting", OpenAndClose(SettingsDestination.Manager)))
            ' A malformed optional record falls back to safe defaults and leaves the legacy record alone.
            SaveSetting(scratch, hive, SettingsRoutes.ContextKey(SettingsDestination.Manager), "{not json")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("startup", OpenAndClose(SettingsDestination.Manager)))
            Assert.Contains("""network""", GetSetting(scratch, hive, SettingsRoutes.LegacyContextKey, ""))
        Finally
            CompanionGlobals.SettingsAppNameOverride = previousApp
            Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(component)
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    ''' <summary>The first open of the merged window reads the record of the retired SFTP window before the retired combined one
    ''' (253 R-7), once, lazily, and leaves both untouched.</summary>
    <Fact>
    Public Sub The_retired_sftp_record_seeds_the_manager_window_first_and_is_never_rewritten()
        ExchangeTestSupport.Reset()
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsMergeTest_" & Guid.NewGuid().ToString("N")
        Const hive As String = "FastMediaSorter"
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            Dim sftpRecord = "{""SchemaVersion"":2,""LastPageId"":""hosting"",""Pages"":{}}"
            Dim legacyRecord = "{""SchemaVersion"":2,""LastPageId"":""scheduled"",""Pages"":{}}"
            SaveSetting(scratch, hive, SettingsRoutes.SftpContextKey, sftpRecord)
            SaveSetting(scratch, hive, SettingsRoutes.LegacyContextKey, legacyRecord)
            Assert.Equal(String.Empty, GetSetting(scratch, hive, SettingsRoutes.ContextKey(SettingsDestination.Manager), ""))
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("hosting", OpenAndClose(SettingsDestination.Manager)))
            Assert.Equal(sftpRecord, GetSetting(scratch, hive, SettingsRoutes.SftpContextKey, ""))
            Assert.Equal(legacyRecord, GetSetting(scratch, hive, SettingsRoutes.LegacyContextKey, ""))
            Assert.NotEqual(String.Empty, GetSetting(scratch, hive, SettingsRoutes.ContextKey(SettingsDestination.Manager), ""))
            ' The seed is ignored from now on: the window's own record wins over a later change of the retired one.
            SaveSetting(scratch, hive, SettingsRoutes.SftpContextKey, "{""SchemaVersion"":2,""LastPageId"":""network"",""Pages"":{}}")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("hosting", OpenAndClose(SettingsDestination.Manager)))
        Finally
            CompanionGlobals.SettingsAppNameOverride = previousApp
            Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    ''' <summary>A remembered SFTP page means nothing when the SFTP component is not installed (253 R-4): the merged window opens
    ''' on its first page instead of on a page that does not exist.</summary>
    <Fact>
    Public Sub A_remembered_sftp_page_falls_back_to_the_first_page_without_the_component()
        ExchangeTestSupport.Reset()
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousApp = CompanionGlobals.SettingsAppNameOverride
        Dim scratch = "FmsSettingsMergeTest_" & Guid.NewGuid().ToString("N")
        Const hive As String = "FastMediaSorter"
        Try
            CompanionGlobals.SettingsAppNameOverride = scratch
            WorkerProcess.ExeDirectoryOverride = Path.Combine(worker, "absent")
            SaveSetting(scratch, hive, SettingsRoutes.SftpContextKey, "{""SchemaVersion"":2,""LastPageId"":""hosting"",""Pages"":{}}")
            ExchangeTestSupport.OnUi(Sub() Assert.Equal("startup", OpenAndClose(SettingsDestination.Manager)))
        Finally
            CompanionGlobals.SettingsAppNameOverride = previousApp
            Try : Microsoft.VisualBasic.Interaction.DeleteSetting(scratch) : Catch : End Try
            ExchangeTestSupport.Reset()
            ExchangeTestSupport.DeleteQuietly(worker)
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
        Dim worker = ExchangeTestSupport.MakeFakeWorker()
        Dim previousLanguage = Localization.CurrentCode
        Dim previousTheme = ThemeService.Mode
        ' One capture per help topic: the manager's own pages, its SFTP sharing pages, and the exchange server window.
        Dim shots = {(SettingsDestination.Manager, "startup", "share-settings"),
                     (SettingsDestination.Manager, "network", "sftp-settings"),
                     (SettingsDestination.ExchangeServer, "exchange", "exchange-server-settings")}
        Try
            ExchangeProcess.ExeDirectoryOverride = component
            ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
            For Each code In Localization.Codes
                Localization.CurrentCode = code
                For Each dark In {False, True}
                    ExchangeTestSupport.OnUi(Sub()
                        For Each shot In shots
                            Dim destination = shot.Item1
                            Using window As New Share_Settings_Form(destination:=destination)
                                window.SelectPage(shot.Item2)
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
                                Dim topic = shot.Item3
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
            ExchangeTestSupport.DeleteQuietly(worker)
        End Try
    End Sub

    ''' <summary>The tray has one entry for the manager settings and one for the exchange server (253 R-6); the SFTP window and its
    ''' route are gone from the code, not hidden.</summary>
    <Fact>
    Public Sub The_tray_menu_has_one_manager_settings_entry_and_the_sftp_destination_is_gone()
        Dim tray = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("TrayContext.vb"))
        Assert.DoesNotContain("SettingsDestination.Sftp", tray)
        Assert.DoesNotContain("Настройки общего доступа SFTP", tray)
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(tray, "ToolStripMenuItem\(Localization\.T\(""Настройки менеджера""\)\)").Cast(Of Object)())
        Assert.Contains("ShowSettingsWindow(SettingsDestination.ExchangeServer", tray)
        Assert.DoesNotContain("Sftp", [Enum].GetNames(GetType(SettingsDestination)))
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
