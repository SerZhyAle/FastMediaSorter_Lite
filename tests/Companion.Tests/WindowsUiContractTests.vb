Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class WindowsUiContractTests
    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(handle As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    Private Shared Sub OnUi(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         test()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI contract check timed out.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    <Fact>
    Public Sub Collapsing_a_focused_group_returns_focus_and_exposes_its_real_state()
        OnUi(Sub()
                 Using host As New Form With {.ShowInTaskbar = False, .StartPosition = FormStartPosition.Manual,
                     .Location = New Point(-5000, -5000)}, section As New CollapsibleSection("test", "Test"),
                     edit As New TextBox()
                     host.Controls.Add(section)
                     section.AddBodyRow(edit)
                     section.Expanded = True
                     host.Show()
                     edit.Focus()
                     Assert.True(edit.Focused)
                     Dim header = section.Controls.Cast(Of Control)().Single(Function(c) c.AccessibleRole = AccessibleRole.ButtonDropDown)
                     Assert.True((header.AccessibilityObject.State And AccessibleStates.Expanded) <> 0)
                     section.Expanded = False
                     Assert.True(header.Focused)
                     Assert.False(edit.Visible)
                     Assert.True((header.AccessibilityObject.State And AccessibleStates.Collapsed) <> 0)
                     header.AccessibilityObject.DoDefaultAction()
                     Assert.True(section.Expanded)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Revealing_the_menu_items_preserves_the_user_window_size()
        OnUi(Sub()
                 Using window As New Share_Settings_Form()
                     If Environment.GetEnvironmentVariable("FMS_SETTINGS_CAPTURE") IsNot Nothing Then
                         IO.File.WriteAllLines(IO.Path.Combine(IO.Path.GetTempPath(), "fms-settings-timing.txt"),
                             window.SettingsBuildTimings.Select(Function(pair) pair.Key & "=" & pair.Value.ToString()))
                     End If
                     window.Size = New Size(750, 550)
                     Dim before = window.Size
                     Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                     Dim expand = GetType(Share_Settings_Form).GetMethod("SetSettingsNavExpanded", flags)
                     expand.Invoke(window, New Object() {"startup", True})
                     Assert.Equal(before, window.Size)
                     expand.Invoke(window, New Object() {"startup", False})
                     Assert.Equal(before, window.Size)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub The_left_menu_tree_toggles_items_selects_pages_and_reveals_blocks()
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     Using window As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         ' A fresh context, not whatever this machine last saved: the tree starts
                         ' from its defaults and the test owns every toggle below.
                         GetType(Share_Settings_Form).GetField("managerSettingsContext", flags).SetValue(window, New SettingsUiContext())
                         GetType(Share_Settings_Form).GetMethod("ApplySettingsNavExpansion", flags).Invoke(window, Nothing)
                         window.Show()
                         window.ClientSize = New Size(820, 600)
                         Application.DoEvents()
                     Dim buttons = DirectCast(GetType(Share_Settings_Form).GetField("_navButtons", flags).GetValue(window), Dictionary(Of String, Button))
                     Dim items = DirectCast(GetType(Share_Settings_Form).GetField("_navItems", flags).GetValue(window), Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavItemButton)))
                     Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", flags).GetValue(window), Dictionary(Of String, Panel))
                     Dim context = DirectCast(GetType(Share_Settings_Form).GetField("managerSettingsContext", flags).GetValue(window), SettingsUiContext)
                     ' A page with groups announces itself as an expandable PageTab and starts with its items hidden.
                     Assert.True(items("exchange").Count > 0)
                     Assert.All(items("exchange"), Sub(item) Assert.False(item.Visible))
                     Assert.True((buttons("exchange").AccessibilityObject.State And AccessibleStates.Collapsed) <> 0)
                     Assert.True(items("exchange").Count = 7, "the exchange page carries its seven blocks")
                     ' Two targets per page row (SETTINGS-NAVIGATION 2.0 section 2 rule 4, ticket 243 T-7b - this pin used to
                     ' say "the whole row is the toggle", Q210-3, which the contract retired): a click on the row selects the
                     ' page and leaves its items as they were; the chevron's own accessible action expands them.
                     buttons("exchange").PerformClick()
                     Assert.All(items("exchange"), Sub(item) Assert.False(item.Visible))
                     Assert.Equal("exchange", context.LastPageId)
                     Assert.True(pages("exchange").Visible)
                     buttons("exchange").AccessibilityObject.GetChild(0).DoDefaultAction()
                     Assert.True(items("exchange")(0).Visible)
                     Assert.True((buttons("exchange").AccessibilityObject.State And AccessibleStates.Expanded) <> 0)
                     Assert.Equal("exchange", context.LastPageId)
                     ' The chevron again folds the items and keeps the page selected.
                     buttons("exchange").AccessibilityObject.GetChild(0).DoDefaultAction()
                     Assert.All(items("exchange"), Sub(item) Assert.False(item.Visible))
                     Assert.Equal("exchange", context.LastPageId)
                     Assert.True((buttons("exchange").AccessibilityObject.State And AccessibleStates.Collapsed) <> 0)
                     ' The page's blocks are collapsible cards (238), each with its own fold
                     ' state; a fresh context leaves only the first one open. Folded by hand
                     ' here, so the reveal below owns every state regardless of what this
                     ' machine had saved when the window was constructed.
                     Dim groups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", flags).GetValue(window),
                                             Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavGroup)))
                     Dim setExpanded = GetType(Share_Settings_Form).GetMethod("SetGroupExpanded", flags)
                     For Each grp In groups("exchange")
                         setExpanded.Invoke(window, New Object() {"exchange", grp, False})
                     Next
                     Application.DoEvents()
                     ' No card repeats its page title (238 sections 2.2 and 5.1): the page
                     ' heading and any card header of the same page never read the same.
                     For Each pair In groups
                         For Each grp In pair.Value
                             Assert.True(TypeOf grp.Card Is SettingsGroupCard, pair.Key & "/" & grp.Id)
                             Assert.Equal(AccessibleRole.Grouping, grp.Card.AccessibleRole)
                             Assert.NotEqual(buttons(pair.Key).Text, grp.Card.Caption)
                         Next
                     Next
                     buttons("exchange").AccessibilityObject.GetChild(0).DoDefaultAction()
                     ' A subordinate item expands its card, scrolls it into view and focuses
                     ' its first editor - and the sibling cards stay on the page (238 section
                     ' 2.1), collapsible without navigating away.
                     items("exchange")(items("exchange").Count - 1).PerformClick()
                     Application.DoEvents()
                     Assert.Equal("exchange", context.LastPageId)
                     Assert.True(pages("exchange").Visible)
                     Dim chosenCards = pages("exchange").Controls.Find("managerSettingsCard_exchange_service", True)
                     Assert.True(chosenCards.Length = 1, "the service card exists")
                     Dim chosen = DirectCast(chosenCards(0), SettingsGroupCard)
                     Assert.True(chosen.Visible AndAlso chosen.Expanded, "the service card shows expanded")
                     Assert.True(context.Page("exchange").Groups("service"), "the fold state is remembered")
                     Dim headerTop = chosen.Header.PointToScreen(Point.Empty).Y - pages("exchange").PointToScreen(Point.Empty).Y
                     Assert.True(headerTop >= 0 AndAlso headerTop < pages("exchange").ClientSize.Height,
                                 "the card scrolled into view, top " & headerTop.ToString())
                     Assert.All(pages("exchange").Controls.Find("managerSettingsCard_exchange_server", True),
                                Sub(card) Assert.True(card.Visible, "the sibling card stays on the page"))
                     Assert.All(pages("exchange").Controls.Find("managerSettingsCard_exchange_server", True),
                                Sub(card) Assert.False(DirectCast(card, SettingsGroupCard).Expanded, "the sibling keeps its fold"))
                     ' The block's first editor takes the focus - when the model gives this
                     ' state any editor to focus at all (a bare not-configured server has
                     ' none, and hidden rows inside a visible command flow do not count).
                     Dim firstEditor = GetType(Share_Settings_Form).GetMethod("FirstSettingsBlockEditor",
                         BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Static).Invoke(
                         window, New Object() {groups("exchange").Last()})
                     If firstEditor IsNot Nothing Then
                         Assert.True(chosen.Contains(window.ActiveControl), "an editor of the block holds the focus")
                     End If
                     ' The page row means the whole page: every card of it in its remembered state.
                     buttons("exchange").PerformClick()
                     Application.DoEvents()
                     Assert.All(groups("exchange"), Sub(grp) Assert.True(grp.Card.Visible, grp.Id & " visible"))
                     Assert.True(groups("exchange").Last().Card.Expanded, "remembered fold survives the page click")
                     ' Left/Right fold and unfold the selected page's items.
                     Dim keyDown = GetType(Share_Settings_Form).GetMethod("OnSettingsNavigationKeyDown", flags)
                     keyDown.Invoke(window, New Object() {buttons("exchange"), New KeyEventArgs(Keys.Left)})
                     Assert.All(items("exchange"), Sub(item) Assert.False(item.Visible))
                     keyDown.Invoke(window, New Object() {buttons("exchange"), New KeyEventArgs(Keys.Right)})
                     Assert.True(items("exchange")(0).Visible)
                     window.Hide()
                 End Using
                 Finally
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                 End Try
             End Sub)
    End Sub

    <Fact>
    Public Sub Manager_pages_keep_window_geometry_and_restore_each_viewport()
        OnUi(Sub()
                 Dim clock = Diagnostics.Stopwatch.StartNew()
                 Dim timings As New List(Of String)()
                 Using window As New Share_Settings_Form(destination:=SettingsDestination.Sftp)
                     Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                     Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                     selectPage.Invoke(window, New Object() {"hosting"})
                     window.Show()
                     Application.DoEvents()
                     timings.Add("dialog=" & clock.ElapsedMilliseconds.ToString())
                     Try
                         window.ClientSize = New Size(820, 300)
                         Application.DoEvents()
                         Dim before = window.Bounds
                         Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", flags).GetValue(window), Dictionary(Of String, Panel))
                         clock.Restart()
                         selectPage.Invoke(window, New Object() {"network"})
                         Application.DoEvents()
                         timings.Add("network-first=" & clock.ElapsedMilliseconds.ToString())
                         Assert.Single(pages.Values.Where(Function(page) page.Visible))
                         Assert.True(pages("network").Visible, "Network page is hidden")
                         Assert.False(pages("hosting").Visible)
                         Assert.True(pages("network").DisplayRectangle.Height > pages("network").ClientSize.Height, "Viewport " & pages("network").ClientSize.ToString() & " content " & pages("network").DisplayRectangle.ToString())
                         pages("network").AutoScrollPosition = New Point(0, 120)
                         Dim scroll = pages("network").AutoScrollPosition.Y
                         Assert.True(scroll < 0, "Scroll position is " & scroll.ToString())
                         selectPage.Invoke(window, New Object() {"hosting"})
                         Application.DoEvents()
                         Assert.Equal(before, window.Bounds)
                         clock.Restart()
                         selectPage.Invoke(window, New Object() {"network"})
                         Application.DoEvents()
                         timings.Add("network-repeat=" & clock.ElapsedMilliseconds.ToString())
                         Assert.InRange(pages("network").AutoScrollPosition.Y, scroll - 2, scroll + 2)
                         Dim context = DirectCast(GetType(Share_Settings_Form).GetField("managerSettingsContext", flags).GetValue(window), SettingsUiContext)
                         Assert.Equal("network", context.LastPageId)
                         Assert.StartsWith("network/", context.Page("network").AnchorId)
                         Dim capture = Environment.GetEnvironmentVariable("FMS_SETTINGS_CAPTURE")
                         If Not String.IsNullOrEmpty(capture) Then
                             IO.Directory.CreateDirectory(capture)
                             IO.File.WriteAllLines(IO.Path.Combine(capture, "timings.txt"), timings)
                             window.ClientSize = New Size(820, 600)
                             For Each id In pages.Keys
                                 selectPage.Invoke(window, New Object() {id})
                                 Application.DoEvents()
                                 Using bitmap As New Bitmap(window.Width, window.Height)
                                     window.DrawToBitmap(bitmap, New Rectangle(Point.Empty, bitmap.Size))
                                     bitmap.Save(IO.Path.Combine(capture, id & ".png"))
                                 End Using
                             Next
                         End If
                     Finally
                         ' Dispose rather than Close: do not save test geometry or context.
                         window.Hide()
                     End Try
                 End Using
             End Sub)
    End Sub


    <Fact>
    Public Sub Manager_contract_anatomy_keeps_hints_visible_and_about_last()
        OnUi(Sub()
                 Using window As New Share_Settings_Form()
                     Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                     Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", flags).GetValue(window), Dictionary(Of String, Panel))
                     Dim buttons = DirectCast(GetType(Share_Settings_Form).GetField("_navButtons", flags).GetValue(window), Dictionary(Of String, Button))
                     Assert.Equal("startup", pages.Keys.First())
                     Assert.Equal("help", pages.Keys.Last())
                     window.Show()
                     window.ClientSize = New Size(820, 600)
                     GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags).Invoke(window, New Object() {"startup"})
                     Application.DoEvents()
                     For Each row In ControlsBelow(window).OfType(Of ManagerSettingRow)()
                         Assert.False(String.IsNullOrWhiteSpace(row.Hint.Text), row.Caption.Text)
                         Assert.True(Not row.Visible OrElse row.Hint.Height >= row.Hint.GetPreferredSize(New Size(row.Hint.Width, 0)).Height,
                             row.Caption.Text & ": hint " & row.Hint.Size.ToString())
                     Next
                     Assert.True(pages("startup").Parent.Top >= window.LogicalToDeviceUnits(52))
                     Dim inventory = DirectCast(GetType(Share_Settings_Form).GetMethod("SettingsSearchEntries", flags).Invoke(window, Nothing), System.Collections.ICollection)
                     Assert.True(inventory.Count >= 6)
                     Assert.Contains(ControlsBelow(window), Function(c) c.Name = "managerLicenseText" AndAlso c.Text.Contains("MIT License"))
                     Dim license = ControlsBelow(window).Single(Function(c) c.Name = "managerLicenseText")
                     Assert.Contains(Environment.NewLine, license.Text)
                     Dim support = Assert.Single(ControlsBelow(window).OfType(Of Button)().Where(Function(c) c.Name = "managerSupportBundle"))
                     Assert.True(pages("help").Contains(support))
                     Assert.Single(ControlsBelow(window).Where(Function(c) c.Text = Localization.T("Отправить журнал автору..")))
                     Assert.DoesNotContain(ControlsBelow(window).OfType(Of Button)(), Function(c) c.Text = Localization.T("Закрыть"))
                     Assert.Null(window.AcceptButton)
                     Assert.Null(window.CancelButton)
                     Assert.True((buttons("startup").AccessibilityObject.State And AccessibleStates.Selected) <> 0)
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    Private Shared Iterator Function ControlsBelow(parent As Control) As IEnumerable(Of Control)
        For Each child As Control In parent.Controls
            Yield child
            For Each descendant In ControlsBelow(child)
                Yield descendant
            Next
        Next
    End Function

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.75F)>
    <InlineData(2.0F)>
    Public Sub Menu_tree_toggles_retain_bounds_and_complete_within_the_repeat_budget(scale As Single)
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     Using window As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         GetType(Share_Settings_Form).GetField("_exLoaded", flags).SetValue(window, True)
                         GetType(Share_Settings_Form).GetField("_hostingLoaded", flags).SetValue(window, True)
                         Dim snapshot As New ExchangeSnapshot With {.Report = New ExchangeProcess.InspectReport With {
                             .Answered = True, .ConfigPresent = True, .Accounts = 2, .Devices = 3,
                             .CertPresent = True, .Fingerprint = "SHA256:abc", .Listen = ":22022", .PortRange = "40000-40100"}}
                         GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", flags).Invoke(window, New Object() {snapshot})
                         window.Scale(New SizeF(scale, scale))
                         window.Show()
                         ThemeService.Attach(window)
                         Application.DoEvents()
                         Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", flags).GetValue(window), Dictionary(Of String, Panel))
                         Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                         Dim setExpanded = GetType(Share_Settings_Form).GetMethod("SetSettingsNavExpanded", flags)
                         Dim before = window.Bounds
                         For Each pageId In pages.Keys
                             selectPage.Invoke(window, New Object() {pageId})
                             Application.DoEvents()
                             ' Warm both states before timing repeats.
                             setExpanded.Invoke(window, New Object() {pageId, True})
                             setExpanded.Invoke(window, New Object() {pageId, False})
                             Application.DoEvents()
                             For Each expanded In {True, False, True, False}
                                 Dim clock = Diagnostics.Stopwatch.StartNew()
                                 setExpanded.Invoke(window, New Object() {pageId, expanded})
                                 Application.DoEvents()
                                 Assert.True(clock.ElapsedMilliseconds < 150,
                                     pageId & " at scale " & scale.ToString() & ": " & clock.ElapsedMilliseconds.ToString() & " ms")
                                 Assert.Equal(before, window.Bounds)
                             Next
                         Next
                         window.Hide()
                     End Using
                 Finally
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                 End Try
             End Sub)
    End Sub

    <Fact>
    Public Sub Manager_icons_use_identity_color_with_contrast_on_every_navigation_state()
        For Each palette In {ThemePalette.Light, ThemePalette.Dark}
            For Each id In {"startup", "network", "hosting", "exchange", "exchange-account", "help"}
                Dim fill As Func(Of Double, Color) = Function(amount) Color.FromArgb(
                    CInt(palette.SurfaceWindow.R * (1 - amount) + palette.Accent.R * amount),
                    CInt(palette.SurfaceWindow.G * (1 - amount) + palette.Accent.G * amount),
                    CInt(palette.SurfaceWindow.B * (1 - amount) + palette.Accent.B * amount))
                Dim surfaces = {palette.SurfaceWindow, fill(0.22), fill(0.18), fill(0.3), fill(0.28), fill(0.4)}
                Dim ink = SettingsNavIconPalette.ResolveInk("managerSettingsNav_" & id, palette.IsDark, surfaces, palette.TextPrimary, False)
                Assert.True(Math.Max(ink.R, Math.Max(ink.G, ink.B)) - Math.Min(ink.R, Math.Min(ink.G, ink.B)) > 10, id)
                For Each surface In surfaces
                    Assert.True(AccentColors.Contrast(ink, surface) >= 3.0R, id)
                Next
                Assert.Equal(palette.TextPrimary, SettingsNavIconPalette.ResolveInk("managerSettingsNav_" & id,
                    palette.IsDark, surfaces, palette.TextPrimary, True))
            Next
        Next
    End Sub

    <Fact>
    Public Sub Async_server_refresh_without_a_synchronization_context_creates_controls_on_the_window_thread()
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input)
                                                   If args(0) = "inspect" Then Thread.Sleep(30)
                                                   Return New ExchangeProcess.RunResult With {.Started = True,
                                                       .StdOut = If(args(0) = "inspect", "{""configPresent"":true,""accounts"":0,""devices"":0,""certPresent"":true,""listen"":"":22022""}", "[]")}
                                               End Function
                 ExchangeFirewall.ShowRuleOverride = Function() Tuple.Create(0, "No rules match")
                 ExchangeService.ImagePathOverride = Function() ""
                 ExchangeService.QueryStateOverride = Function() ServiceControl.ServiceState.NotInstalled
                 Dim previousChecks = Control.CheckForIllegalCrossThreadCalls
                 Try
                     Control.CheckForIllegalCrossThreadCalls = True
                     Using window As New Share_Settings_Form(destination:=SettingsDestination.ExchangeServer)
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         GetType(Share_Settings_Form).GetField("_exLoaded", flags).SetValue(window, True)
                         GetType(Share_Settings_Form).GetField("_hostingLoaded", flags).SetValue(window, True)
                         window.Show()
                         GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags).Invoke(window, New Object() {"exchange"})
                         Dim previousContext = SynchronizationContext.Current
                         Dim refresh As Threading.Tasks.Task
                         Try
                             SynchronizationContext.SetSynchronizationContext(Nothing)
                             refresh = DirectCast(GetType(Share_Settings_Form).GetMethod("RefreshExchangeAsync", flags).Invoke(window, Nothing), Threading.Tasks.Task)
                         Finally
                             SynchronizationContext.SetSynchronizationContext(previousContext)
                         End Try
                         Dim clock = Diagnostics.Stopwatch.StartNew()
                         While Not refresh.IsCompleted AndAlso clock.ElapsedMilliseconds < 5000
                             Application.DoEvents()
                             Thread.Sleep(5)
                         End While
                         Assert.True(refresh.IsCompleted, "Refresh failed to return to the UI thread")
                         refresh.GetAwaiter().GetResult()
                         Dim processId As UInteger
                         Dim ownerThread = GetWindowThreadProcessId(window.Handle, processId)
                         Dim wizard = DirectCast(GetType(Share_Settings_Form).GetField("_btnXWizard", flags).GetValue(window), Button)
                         Assert.True(wizard.Visible)
                         Assert.True(wizard.IsHandleCreated)
                         For Each control In ControlsBelow(window).Where(Function(c) c.IsHandleCreated)
                             Assert.Equal(ownerThread, GetWindowThreadProcessId(control.Handle, processId))
                         Next
                         window.Hide()
                     End Using
                 Finally
                     Control.CheckForIllegalCrossThreadCalls = previousChecks
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                 End Try
             End Sub)
    End Sub

    <Theory>
    <InlineData(Share_Settings_Form.MinimumWidthLogical)>
    <InlineData(820)>
    Public Sub Every_page_contains_its_editors_after_resize_and_server_state_changes(width As Integer)
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 Dim previousGate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
                 Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     For Each destination In {SettingsDestination.Manager, SettingsDestination.Sftp, SettingsDestination.ExchangeServer}
                     Using window As New Share_Settings_Form(destination:=destination)
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         GetType(Share_Settings_Form).GetField("_exLoaded", flags).SetValue(window, True)
                         GetType(Share_Settings_Form).GetField("_hostingLoaded", flags).SetValue(window, True)
                         GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags).Invoke(window, New Object() {"startup"})
                         window.Show()
                         window.ClientSize = New Size(window.LogicalToDeviceUnits(width), window.LogicalToDeviceUnits(480))
                         Dim pages = DirectCast(GetType(Share_Settings_Form).GetField("_pages", flags).GetValue(window), Dictionary(Of String, Panel))
                         Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                         For Each state As Integer In {2, 0, 1, 3}
                             Dim snapshot As New ExchangeSnapshot With {.ServiceExists = state = 2,
                                 .ServiceState = If(state = 2, ServiceControl.ServiceState.Stopped, ServiceControl.ServiceState.NotInstalled),
                                 .ConsolePid = If(state = 3, 42, 0), .CanChangeFirewall = True, .CanRegisterService = state <> 2,
                                 .CanRemoveService = state = 2, .Report = New ExchangeProcess.InspectReport With {
                                     .Answered = True, .ConfigPresent = state <> 0, .Accounts = If(state = 0, 0, 2),
                                     .CertPresent = state <> 0, .Fingerprint = If(state = 0, "", "SHA256:abc"),
                                     .Listen = ":22022", .PortRange = "40000-40100", .PublicEndpoint = "203.0.113.7:22022"}}
                             If destination = SettingsDestination.ExchangeServer Then GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", flags).Invoke(window, New Object() {snapshot})
                             For Each pageId In pages.Keys
                                 selectPage.Invoke(window, New Object() {pageId})
                                 Dim pageGroups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", flags).GetValue(window),
                                                             Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavGroup)))
                                 Dim setExpanded = GetType(Share_Settings_Form).GetMethod("SetGroupExpanded", flags)
                                 If pageGroups.ContainsKey(pageId) Then
                                     For Each grp In pageGroups(pageId)
                                         setExpanded.Invoke(window, New Object() {pageId, grp, True})
                                     Next
                                 End If
                                 If pageId = "exchange" AndAlso state = 1 Then
                                     DirectCast(GetType(Share_Settings_Form).GetField("_txtXEndpoint", flags).GetValue(window), TextBox).Text = "203.0.113.9:22023"
                                     DirectCast(GetType(Share_Settings_Form).GetField("numXRangeHigh", flags).GetValue(window), NumericUpDown).Value = 40101
                                     DirectCast(GetType(Share_Settings_Form).GetField("numXPort", flags).GetValue(window), NumericUpDown).Value = 22023
                                 End If
                                 Application.DoEvents()
                                 If pageId = "exchange" AndAlso state = 1 Then
                                     For Each name In {"_btnXApplyEndpoint", "_btnXApplyRange", "_btnXApplyPort"}
                                         Assert.True(DirectCast(GetType(Share_Settings_Form).GetField(name, flags).GetValue(window), Button).Visible, name)
                                     Next
                                 End If
                                 ' Nothing folds any more (210): every editor of the page is on
                                 ' show and must sit inside its parent's layout box.
                                 For Each control In ControlsBelow(pages(pageId)).Where(Function(c) c.Visible AndAlso
                                     (TypeOf c Is ManagerSettingRow OrElse TypeOf c Is FlowLayoutPanel OrElse
                                      TypeOf c Is Button OrElse TypeOf c Is TextBox OrElse TypeOf c Is NumericUpDown OrElse TypeOf c Is ListView))
                                     Assert.True(control.Left >= 0 AndAlso control.Right <= control.Parent.ClientSize.Width + 1,
                                         pageId & "/" & control.Name & ": horizontal clipping " & control.Bounds.ToString() & " in " & control.Parent.ClientSize.ToString())
                                     Assert.True(control.Top >= 0 AndAlso control.Bottom <= control.Parent.ClientSize.Height + 1,
                                         pageId & "/" & control.Name & ": vertical clipping " & control.Bounds.ToString() & " in " & control.Parent.ClientSize.ToString())
                                 Next
                                 For Each row In ControlsBelow(window).OfType(Of ManagerSettingRow)().Where(Function(c) c.Visible)
                                     Assert.True(row.Hint.Height >= row.Hint.GetPreferredSize(New Size(row.Hint.Width, 0)).Height, row.Caption.Text)
                                     Assert.False(row.Caption.Bounds.IntersectsWith(row.Editor.Bounds), row.Caption.Text & ": editor overlaps caption")
                                     Assert.False(row.Hint.Bounds.IntersectsWith(row.Editor.Bounds), row.Caption.Text & ": editor overlaps hint")
                                 Next
                                 Dim rowEnds = ControlsBelow(window).OfType(Of ManagerSettingRow)().Where(Function(c) c.Visible).
                                     Select(Function(row) row.PointToScreen(New Point(row.ClientSize.Width, 0)).X).ToArray()
                                 If rowEnds.Length > 1 Then Assert.InRange(rowEnds.Max() - rowEnds.Min(), 0, 1)
                             Next
                             If destination = SettingsDestination.ExchangeServer Then
                             Dim keep = DirectCast(GetType(Share_Settings_Form).GetField("_chkXKeep", flags).GetValue(window), CheckBox)
                             selectPage.Invoke(window, New Object() {"exchange"})
                             Dim hint = ControlsBelow(window).Single(Function(c) c.Name = keep.Name & "Hint")
                             Assert.Equal(keep.Visible, hint.Visible)
                             End If
                         Next
                         window.Hide()
                     End Using
                     Next
                 Finally
                     Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, previousGate)
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                 End Try
             End Sub)
    End Sub

    <Theory>
    <InlineData("ru")>
    <InlineData("de")>
    <InlineData("ar")>
    <InlineData("hi")>
    <InlineData("zh")>
    Public Sub Manager_navigation_and_hints_fit_the_drawn_script(code As String)
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 Try
                     Localization.CurrentCode = code
                     Using window As New Share_Settings_Form()
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         window.Show()
                         window.ClientSize = New Size(820, 600)
                         GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags).Invoke(window, New Object() {"startup"})
                         Application.DoEvents()
                         Dim buttons = DirectCast(GetType(Share_Settings_Form).GetField("_navButtons", flags).GetValue(window), Dictionary(Of String, Button))
                         Dim items = DirectCast(GetType(Share_Settings_Form).GetField("_navItems", flags).GetValue(window), Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavItemButton)))
                         ' A caption wraps and its row grows, never clips (SETTINGS-NAVIGATION 2.0 rule 2). The room is measured from
                         ' the row's REAL padding (the reading-start inset, the chevron zone, the child indent), the page rows'
                         ' glyph and gap and the renderer's slack, and the height is the rule's own: the larger of the 32 logical
                         ' px target and the wrapped caption plus its vertical slack. (Ticket 243 T-7b replaced the former proxy of
                         ' width - 52 and height >= text + 10, which described the 44 px rows of the old rail.)
                         Dim factor = window.DeviceDpi / 96.0F
                         Dim glyphRoom = SettingsRailRules.GlyphPx(factor) + SettingsRailRules.GlyphGapPx(factor)
                         Dim slack = window.LogicalToDeviceUnits(Share_Settings_Form.NavRenderSlackLogical)
                         For Each button In buttons.Values
                             Dim room = Math.Max(1, button.Width - button.Padding.Horizontal - glyphRoom - slack)
                             Dim needed = TextRenderer.MeasureText(button.Text, button.Font,
                                 New Size(room, Integer.MaxValue), TextFormatFlags.WordBreak).Height
                             Assert.True(button.Height >= SettingsRailRules.RowHeightPx(factor, needed), code & ": " & button.Text)
                         Next
                         For Each row In items.Values.SelectMany(Function(list) list)
                             ' An item with a catalogued glyph draws it in the column an item without one leaves empty.
                             Dim room = Math.Max(1, row.Width - row.Padding.Horizontal - slack - If(row.Image IsNot Nothing, glyphRoom, 0))
                             Dim needed = TextRenderer.MeasureText(row.Text, row.Font,
                                 New Size(room, Integer.MaxValue), TextFormatFlags.WordBreak).Height
                             Assert.True(row.Height >= SettingsRailRules.RowHeightPx(factor, needed), code & ": " & row.Text)
                         Next
                         For Each row In ControlsBelow(window).OfType(Of ManagerSettingRow)().Where(Function(item) item.Visible)
                             Assert.True(row.Hint.Height >= row.Hint.GetPreferredSize(New Size(row.Hint.Width, 0)).Height, code & ": " & row.Caption.Text)
                         Next
                         window.Hide()
                     End Using
                 Finally
                     Localization.CurrentCode = previous
                 End Try
             End Sub)
    End Sub

    <Fact>
    Public Sub Live_language_refresh_preserves_window_and_editor_values_and_resets_direction()
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 Try
                     Localization.CurrentCode = "en"
                     Using window As New Share_Settings_Form(destination:=SettingsDestination.Sftp)
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         Dim number = ControlsBelow(window).OfType(Of NumericUpDown)().First()
                         Dim value = number.Value
                         Dim capture As New LiveControlLocalization(window)
                         Localization.CurrentCode = "ar"
                         ' Every container is held while the captions, font and direction change (HoldLayout): measured 2026-10-16 the
                         ' switch took 6 s for this window without it and 0.2 s with it. The bound only catches that coming back.
                         Dim clock = Diagnostics.Stopwatch.StartNew()
                         capture.Apply()
                         GetType(Share_Settings_Form).GetMethod("RefreshSettingsLanguage", flags).Invoke(window, New Object() {Nothing, EventArgs.Empty})
                         Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), "Live language switch took " & clock.Elapsed.TotalSeconds.ToString("0.0") & " s.")
                         Assert.False(window.IsDisposed)
                         Assert.Equal(DialogResult.None, window.DialogResult)
                         Assert.Equal(value, number.Value)
                         Assert.Equal(RightToLeft.Yes, window.RightToLeft)
                         capture = New LiveControlLocalization(window)
                         Localization.CurrentCode = "en"
                         capture.Apply()
                         GetType(Share_Settings_Form).GetMethod("RefreshSettingsLanguage", flags).Invoke(window, New Object() {Nothing, EventArgs.Empty})
                         Assert.Equal(RightToLeft.No, window.RightToLeft)
                         Assert.Equal(value, number.Value)
                     End Using
                 Finally
                     Localization.CurrentCode = previous
                 End Try
             End Sub)
    End Sub
    <Fact>
    Public Sub Live_language_refresh_of_the_main_window_takes_a_moment_not_seconds()
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 Try
                     Localization.CurrentCode = "en"
                     Using window As New MainWindow()
                         Dim capture As New LiveControlLocalization(window)
                         Localization.CurrentCode = "ar"
                         Dim clock = Diagnostics.Stopwatch.StartNew()
                         capture.Apply()
                         Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), "Live language switch took " & clock.Elapsed.TotalSeconds.ToString("0.0") & " s.")
                         Assert.Equal(RightToLeft.Yes, window.RightToLeft)
                     End Using
                 Finally
                     Localization.CurrentCode = previous
                 End Try
             End Sub)
    End Sub

    <Fact>
    Public Sub Secondary_window_numeric_buttons_keep_their_width_and_native_value_semantics()
        OnUi(Sub()
                 Using window As New Form(), number As New NumericUpDown With {.Width = 140, .Value = 5,
                     .Minimum = 0, .Maximum = 10, .Increment = 0.25D, .DecimalPlaces = 2}
                     window.Controls.Add(number)
                     For Each palette In {ThemePalette.Dark, ThemePalette.Light, ThemePalette.Dark}
                         ThemeApplier.ApplyTree(window, palette)
                         Dim buttons = number.Controls.Cast(Of Control)().Single(Function(c) Not TypeOf c Is TextBox)
                         Assert.Equal(CInt(Math.Round(28 * number.DeviceDpi / 96.0R)), buttons.Width)
                         Assert.Equal(5D, number.Value)
                     Next
                     number.UpButton()
                     Assert.Equal(5.25D, number.Value)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Dropdown_field_and_arrow_are_painted_in_the_current_palette()
        OnUi(Sub()
                 Using window As New Form(), combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 200}
                     window.Controls.Add(combo)
                     combo.Items.Add("Test")
                     combo.SelectedIndex = 0
                     For Each palette In {ThemePalette.Dark, ThemePalette.Light, ThemePalette.Dark}
                         ThemeApplier.ApplyTree(window, palette)
                         Using bitmap As New Bitmap(combo.Width, combo.Height)
                             combo.DrawToBitmap(bitmap, combo.ClientRectangle)
                             Assert.Equal(palette.SurfaceRaised.ToArgb(), bitmap.GetPixel(4, 4).ToArgb())
                             Assert.Equal(palette.ColorOf(ThemeRole.Control).ToArgb(), bitmap.GetPixel(combo.Width - 5, 4).ToArgb())
                         End Using
                         Assert.Equal(0, combo.SelectedIndex)
                     Next
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Embedded_dropdowns_in_toolbars_follow_both_palettes()
        OnUi(Sub()
                 Using strip As New ToolStrip(), combo As New ToolStripComboBox()
                     strip.Items.Add(combo)
                     For Each palette In {ThemePalette.Dark, ThemePalette.Light, ThemePalette.Dark}
                         ThemeApplier.ApplyTree(strip, palette)
                         Assert.Equal(palette.SurfaceRaised, combo.ComboBox.BackColor)
                         Assert.Equal(palette.TextPrimary, combo.ComboBox.ForeColor)
                     Next
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Native_checkboxes_keep_the_language_direction_and_hint_indent()
        OnUi(Sub()
                 Using window As New Form With {.RightToLeft = RightToLeft.Yes},
                       flow As New FlowLayoutPanel(), check As New CheckBox With {.Text = "Test", .AutoSize = True},
                       hint As New Label With {.Text = "Hint", .AutoSize = True}
                     window.Controls.Add(flow)
                     flow.Controls.AddRange({check, hint})
                     CheckBoxLayout.BindHint(check, hint)
                     ThemeApplier.ApplyTree(window, ThemePalette.Dark)
                     Assert.Equal(RightToLeft.Yes, check.RightToLeft)
                     Assert.Equal(check.Margin.Right + CheckBoxLayout.CaptionInset(check), hint.Margin.Right)
                     window.RightToLeft = RightToLeft.No
                     Assert.Equal(check.Margin.Left + CheckBoxLayout.CaptionInset(check), hint.Margin.Left)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Main_window_toolbar_and_context_menus_streamline_per_spec_237()
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 Try
                     Localization.CurrentCode = "ru"
                     MainWindow.ShowStatsDialogOverride = Sub(w, st)
                                                              ' Test seam: avoid blocking modal dialog
                                                          End Sub
                     Using window As New MainWindow()
                         window.Show()
                         window.ClientSize = New Size(760, 560)
                         Application.DoEvents()

                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         Dim pnlButtons = DirectCast(GetType(MainWindow).GetField("_pnlListButtons", flags).GetValue(window), FlowLayoutPanel)
                         Dim addMenu = DirectCast(GetType(MainWindow).GetField("_addMenu", flags).GetValue(window), ContextMenuStrip)
                         Dim lvFolders = DirectCast(GetType(MainWindow).GetField("lvFolders", flags).GetValue(window), ListView)
                         Dim diskMenu = DirectCast(GetType(MainWindow).GetField("_diskMenu", flags).GetValue(window), ContextMenuStrip)
                         Dim secStats = DirectCast(GetType(MainWindow).GetField("_secStats", flags).GetValue(window), CollapsibleSection)
                         Dim clipBtn = pnlButtons.Controls.OfType(Of Button)().FirstOrDefault(Function(b) b.Name = "btnClipboardMenu")

                         ' 1. Toolbar single-row layout without wrapping
                         Assert.False(pnlButtons.WrapContents)
                         Dim buttonNames = pnlButtons.Controls.Cast(Of Control)().Select(Function(c) c.Name).ToArray()
                         Assert.Equal(New String() {"btnAdd", "btnAddDrop", "btnClipboardMenu", "btnParams", "btnRemove", "btnPhoneCode"}, buttonNames)

                         ' Verify buttons are on a single horizontal row at 760 and 560 px
                         Dim tops760 = pnlButtons.Controls.Cast(Of Control)().Select(Function(c) c.Top).Distinct().Count()
                         Assert.Equal(1, tops760)

                         window.ClientSize = New Size(560, 420)
                         Application.DoEvents()
                         Dim tops560 = pnlButtons.Controls.Cast(Of Control)().Select(Function(c) c.Top).Distinct().Count()
                         Assert.Equal(1, tops560)

                         ' 2. Resource creation dropdown menu
                         Assert.NotNull(addMenu)
                         Dim openAddMenu = GetType(ToolStripDropDown).GetMethod("OnOpening", flags)
                         openAddMenu.Invoke(addMenu, New Object() {New System.ComponentModel.CancelEventArgs()})
                         Assert.True(addMenu.Items.Count >= 5)
                         Assert.Equal("Папку..", addMenu.Items(0).Text)
                         Assert.NotNull(addMenu.Items(0).Image)

                         ' 3. Clipboard consolidation dropdown
                         Assert.NotNull(clipBtn)
                         Dim clipMenu = clipBtn.ContextMenuStrip
                         Assert.NotNull(clipMenu)
                         Dim openClipMenu = GetType(ToolStripDropDown).GetMethod("OnOpening", flags)
                         openClipMenu.Invoke(clipMenu, New Object() {New System.ComponentModel.CancelEventArgs()})
                         Assert.Equal(3, clipMenu.Items.Count)
                         Assert.Equal("Отправить буфер на сервер", clipMenu.Items(0).Text)
                         Assert.NotNull(clipMenu.Items(0).Image)
                         Assert.Equal("Получить буфер с сервера", clipMenu.Items(1).Text)
                         Assert.NotNull(clipMenu.Items(1).Image)
                         Assert.Equal("Отмена", clipMenu.Items(2).Text)

                         ' 4. Unified context menu on lvFolders
                         Assert.NotNull(diskMenu)
                         Assert.Same(diskMenu, lvFolders.ContextMenuStrip)
                         Dim folderItem As New ListViewItem("SharedDocs") With {.Tag = "C:\NonExistentTestFolder"}
                         lvFolders.Items.Add(folderItem)
                         folderItem.Selected = True
                         Dim openDiskMenu = GetType(ToolStripDropDown).GetMethod("OnOpening", flags)
                         openDiskMenu.Invoke(diskMenu, New Object() {New System.ComponentModel.CancelEventArgs()})
                         Assert.True(diskMenu.Items.Count >= 4)
                         Dim editItem = DirectCast(diskMenu.Items(0), ToolStripMenuItem)
                         Assert.Equal("Редактировать", editItem.Text)
                         Assert.True(editItem.Font.Bold, "Edit item is bold default action")
                         Assert.NotNull(editItem.Image)
                         Assert.Contains(diskMenu.Items.OfType(Of ToolStripMenuItem)(), Function(it) it.Text = "Дать доступ наружу..")
                         Assert.Contains(diskMenu.Items.OfType(Of ToolStripMenuItem)(), Function(it) it.Text = "Убрать")

                         ' 5. Stats fold simplification: expanding immediately collapses and opens details
                         Assert.NotNull(secStats)
                         secStats.Expanded = True
                         Assert.False(secStats.Expanded, "Stats section collapses immediately to act as a direct door")
                     End Using
                 Finally
                     MainWindow.ShowStatsDialogOverride = Nothing
                     Localization.CurrentCode = previous
                 End Try
             End Sub)
    End Sub

    <Fact>
    Public Sub Main_window_resources_fold_and_content_sized_list_per_spec_237_zone_d()
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 Try
                     Localization.CurrentCode = "ru"
                     Using window As New MainWindow()
                         window.Show()
                         ' Size in LOGICAL px times the live scale, so the height assertions
                         ' below hold on any DPI the test host runs at.
                         Dim scale As Double = window.DeviceDpi / 96.0R
                         window.ClientSize = New Size(CInt(760 * scale), CInt(560 * scale))
                         Application.DoEvents()

                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         Dim type = GetType(MainWindow)
                         Dim secResources = DirectCast(type.GetField("_secResources", flags).GetValue(window), CollapsibleSection)
                         Dim pnlList = DirectCast(type.GetField("_pnlList", flags).GetValue(window), Panel)
                         Dim lvFolders = DirectCast(type.GetField("lvFolders", flags).GetValue(window), ListView)
                         Dim root = DirectCast(type.GetField("_root", flags).GetValue(window), TableLayoutPanel)
                         Dim relayout = type.GetMethod("OnResourceRowsChanged", flags)
                         Dim desiredHeight = type.GetMethod("DesiredListHeight", flags)
                         Dim minHeight = type.GetMethod("MinimumListHeight", flags)
                         Dim csvOf = type.GetMethod("ExpandedSectionsCsv", flags)

                         ' S-0: the fold exists, wraps the list block, and defaults to expanded.
                         Assert.NotNull(secResources)
                         Assert.Equal("resources", secResources.Key)
                         Assert.Equal("Общие ресурсы", secResources.Title)
                         Assert.True(secResources.Expanded, "The resources fold defaults to expanded - the list is the window's object (G4).")
                         Assert.Same(secResources.Body, pnlList.Parent)

                         ' L-1: the list row is content-sized, never stretched to the window.
                         For i As Integer = 1 To 3
                             lvFolders.Items.Add(New ListViewItem("Folder" & i) With {.Tag = "C:\NonExistent\F" & i})
                         Next
                         relayout.Invoke(window, New Object() {})
                         Dim h3 As Integer = pnlList.Height
                         Dim desired3 As Integer = CInt(desiredHeight.Invoke(window, New Object() {}))
                         Dim floor3 As Integer = CInt(minHeight.Invoke(window, New Object() {}))
                         Assert.True(h3 >= floor3, "List height " & h3 & " fell below the G4 floor " & floor3 & ".")
                         Assert.True(h3 <= desired3 + 1,
                                     "List height " & h3 & " exceeds its content size " & desired3 & " - rule L-1 is not applied.")

                         ' The eight-row ceiling: 9 and 12 items size the list identically.
                         For i As Integer = 4 To 9
                             lvFolders.Items.Add(New ListViewItem("Folder" & i) With {.Tag = "C:\NonExistent\F" & i})
                         Next
                         relayout.Invoke(window, New Object() {})
                         Dim h9 As Integer = pnlList.Height
                         For i As Integer = 10 To 12
                             lvFolders.Items.Add(New ListViewItem("Folder" & i) With {.Tag = "C:\NonExistent\F" & i})
                         Next
                         relayout.Invoke(window, New Object() {})
                         Dim h12 As Integer = pnlList.Height
                         Assert.True(h3 < h9, "More rows must grow the list (" & h3 & " -> " & h9 & ").")
                         Assert.Equal(h9, h12)

                         ' The root row is AutoSize - the fold sizes itself from the list panel.
                         Assert.Equal(SizeType.AutoSize, root.RowStyles(1).SizeType)

                         ' S-0: the live summary counts the rows the fold folds away.
                         Assert.Equal("12 папок", secResources.Summary)

                         ' S-0: collapsing compacts the WINDOW and records the fold state.
                         Dim fullHeight As Integer = window.Height
                         secResources.Expanded = False
                         Assert.False(pnlList.Visible)
                         Assert.True(window.Height < fullHeight - CInt(30 * scale),
                                     "Collapsing the fold must compact the window (" & fullHeight & " -> " & window.Height & ").")
                         Assert.Contains("resources", CStr(csvOf.Invoke(window, New Object() {})))

                         ' Expanding gives the remembered window back and clears the record.
                         secResources.Expanded = True
                         Assert.True(pnlList.Visible)
                         Assert.Equal(fullHeight, window.Height)
                         Assert.DoesNotContain("resources", CStr(csvOf.Invoke(window, New Object() {})))
                     End Using
                 Finally
                     Localization.CurrentCode = previous
                 End Try
             End Sub)
    End Sub
End Class
