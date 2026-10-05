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

<Collection("Theme service state")>
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
        If failure IsNot Nothing Then Throw failure
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
                     window.Size = New Size(750, 550)
                     Dim before = window.Size
                     Dim section = DirectCast(GetType(Share_Settings_Form).GetField("_secHelp",
                         BindingFlags.Instance Or BindingFlags.NonPublic).GetValue(window), CollapsibleSection)
                     section.Expanded = True
                     Assert.Equal(before, window.Size)
                     section.Expanded = False
                     Assert.Equal(before, window.Size)
                 End Using
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
