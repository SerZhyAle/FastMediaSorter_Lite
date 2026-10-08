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
    Public Sub Settings_group_changes_preserve_the_user_window_size()
        OnUi(Sub()
                 Using window As New Share_Settings_Form()
                     If Environment.GetEnvironmentVariable("FMS_SETTINGS_CAPTURE") IsNot Nothing Then
                         IO.File.WriteAllLines(IO.Path.Combine(IO.Path.GetTempPath(), "fms-settings-timing.txt"),
                             window.SettingsBuildTimings.Select(Function(pair) pair.Key & "=" & pair.Value.ToString()))
                     End If
                     window.Size = New Size(750, 550)
                     Dim before = window.Size
                     Dim groups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups",
                         BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(window), Dictionary(Of String, List(Of CollapsibleSection)))
                     Dim section = groups("startup")(0)
                     section.Expanded = True
                     Assert.Equal(before, window.Size)
                     section.Expanded = False
                     Assert.Equal(before, window.Size)
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Manager_pages_keep_window_geometry_and_restore_each_viewport()
        OnUi(Sub()
                 Dim clock = Diagnostics.Stopwatch.StartNew()
                 Dim timings As New List(Of String)()
                 Using window As New Share_Settings_Form()
                     Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                     Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                     selectPage.Invoke(window, New Object() {"startup"})
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
                         Assert.False(pages("startup").Visible)
                         Assert.True(pages("network").DisplayRectangle.Height > pages("network").ClientSize.Height, "Viewport " & pages("network").ClientSize.ToString() & " content " & pages("network").DisplayRectangle.ToString())
                         pages("network").AutoScrollPosition = New Point(0, 120)
                         Dim scroll = pages("network").AutoScrollPosition.Y
                         Assert.True(scroll < 0, "Scroll position is " & scroll.ToString())
                         selectPage.Invoke(window, New Object() {"help"})
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
    Public Sub Settings_group_changes_retain_bounds_and_complete_within_the_repeat_budget(scale As Single)
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     Using window As New Share_Settings_Form()
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
                         Dim groups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", flags).GetValue(window), Dictionary(Of String, List(Of CollapsibleSection)))
                         Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                         Dim before = window.Bounds
                         For Each page In groups
                             selectPage.Invoke(window, New Object() {page.Key})
                             Application.DoEvents()
                             For Each group In page.Value
                                 ' Warm both states before timing repeats, including the expanded server configuration.
                                 group.Expanded = True
                                 group.Expanded = False
                                 Application.DoEvents()
                                 For Each expanded In {True, False, True, False}
                                     Dim clock = Diagnostics.Stopwatch.StartNew()
                                     group.Expanded = expanded
                                     Application.DoEvents()
                                     Assert.True(clock.ElapsedMilliseconds < 150,
                                         group.Key & " at scale " & scale.ToString() & ": " & clock.ElapsedMilliseconds.ToString() & " ms")
                                     Assert.Equal(before, window.Bounds)
                                 Next
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

    <Theory>
    <InlineData(640)>
    <InlineData(820)>
    Public Sub Every_expanded_group_contains_its_editors_after_resize_and_server_state_changes(width As Integer)
        OnUi(Sub()
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 Dim previousGate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
                 Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     Using window As New Share_Settings_Form()
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         GetType(Share_Settings_Form).GetField("_exLoaded", flags).SetValue(window, True)
                         GetType(Share_Settings_Form).GetField("_hostingLoaded", flags).SetValue(window, True)
                         window.Show()
                         window.ClientSize = New Size(window.LogicalToDeviceUnits(width), window.LogicalToDeviceUnits(480))
                         Dim groups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", flags).GetValue(window), Dictionary(Of String, List(Of CollapsibleSection)))
                         Dim selectPage = GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", flags)
                         For Each state As Integer In {0, 1, 2, 3}
                             Dim snapshot As New ExchangeSnapshot With {.ServiceExists = state = 2,
                                 .ServiceState = If(state = 2, ServiceControl.ServiceState.Stopped, ServiceControl.ServiceState.NotInstalled),
                                 .ConsolePid = If(state = 3, 42, 0), .CanChangeFirewall = True, .CanRegisterService = state <> 2,
                                 .CanRemoveService = state = 2, .Report = New ExchangeProcess.InspectReport With {
                                     .Answered = True, .ConfigPresent = state <> 0, .Accounts = If(state = 0, 0, 2),
                                     .CertPresent = state <> 0, .Fingerprint = If(state = 0, "", "SHA256:abc"),
                                     .Listen = ":22022", .PortRange = "40000-40100", .PublicEndpoint = "203.0.113.7:22022"}}
                             GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", flags).Invoke(window, New Object() {snapshot})
                             For Each page In groups
                                 selectPage.Invoke(window, New Object() {page.Key})
                                 If page.Key = "exchange" AndAlso state = 1 Then
                                     DirectCast(GetType(Share_Settings_Form).GetField("_txtXEndpoint", flags).GetValue(window), TextBox).Text = "203.0.113.9:22023"
                                     DirectCast(GetType(Share_Settings_Form).GetField("numXRangeHigh", flags).GetValue(window), NumericUpDown).Value = 40101
                                     DirectCast(GetType(Share_Settings_Form).GetField("numXPort", flags).GetValue(window), NumericUpDown).Value = 22023
                                 End If
                                 For Each group In page.Value
                                     group.Expanded = False
                                     group.Expanded = True
                                 Next
                                 Application.DoEvents()
                                 If page.Key = "exchange" AndAlso state = 1 Then
                                     For Each name In {"_btnXApplyEndpoint", "_btnXApplyRange", "_btnXApplyPort"}
                                         Assert.True(DirectCast(GetType(Share_Settings_Form).GetField(name, flags).GetValue(window), Button).Visible, name)
                                     Next
                                 End If
                                 For Each group In page.Value
                                     If Not group.Visible Then Continue For
                                     Assert.True(group.Height >= group.PreferredSize.Height, group.Key & ": stale expanded height")
                                     For Each control In ControlsBelow(group).Where(Function(c) c.Visible AndAlso
                                         (TypeOf c Is ManagerSettingRow OrElse TypeOf c Is FlowLayoutPanel OrElse
                                          TypeOf c Is Button OrElse TypeOf c Is TextBox OrElse TypeOf c Is NumericUpDown OrElse TypeOf c Is ListView))
                                         Assert.True(control.Left >= 0 AndAlso control.Right <= control.Parent.ClientSize.Width + 1,
                                             page.Key & "/" & control.Name & ": horizontal clipping " & control.Bounds.ToString() & " in " & control.Parent.ClientSize.ToString())
                                         Assert.True(control.Top >= 0 AndAlso control.Bottom <= control.Parent.ClientSize.Height + 1,
                                             page.Key & "/" & control.Name & ": vertical clipping " & control.Bounds.ToString() & " in " & control.Parent.ClientSize.ToString())
                                     Next
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
                             Dim keep = DirectCast(GetType(Share_Settings_Form).GetField("_chkXKeep", flags).GetValue(window), CheckBox)
                             selectPage.Invoke(window, New Object() {"exchange"})
                             Dim hint = ControlsBelow(window).Single(Function(c) c.Name = keep.Name & "Hint")
                             Assert.Equal(keep.Visible, hint.Visible)
                         Next
                         window.Hide()
                     End Using
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
                         For Each button In buttons.Values
                             Dim needed = TextRenderer.MeasureText(button.Text, button.Font,
                                 New Size(button.Width - window.LogicalToDeviceUnits(52), Integer.MaxValue), TextFormatFlags.WordBreak).Height
                             Assert.True(button.Height >= needed + window.LogicalToDeviceUnits(16), code & ": " & button.Text)
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
                     Using window As New Share_Settings_Form()
                         Dim flags = BindingFlags.Instance Or BindingFlags.NonPublic
                         Dim number = ControlsBelow(window).OfType(Of NumericUpDown)().First()
                         Dim value = number.Value
                         Dim capture As New LiveControlLocalization(window)
                         Localization.CurrentCode = "ar"
                         capture.Apply()
                         GetType(Share_Settings_Form).GetMethod("RefreshSettingsLanguage", flags).Invoke(window, New Object() {Nothing, EventArgs.Empty})
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
End Class
