Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit
Imports Xunit.Abstractions

''' <summary>The Share Manager settings window's page rail against SETTINGS-NAVIGATION 2.0 section 2 (rules 1-7), ticket 243
''' slice T-7b - the sibling of the viewer's <c>SettingsRailTests</c> (T-7a). Real windows, real mouse messages, real
''' accessible objects: roles and states (rule 7), one roving tab stop and arrow movement over the visible rows, the
''' select target versus the expand target (rule 4), the 32 logical px rows at 100 and 150 percent (rule 5), the mirror under
''' <c>RightToLeft = Yes</c> (rule 1) and the narrow window (rule 6).</summary>
<Collection(UiLanguageCollection.Name)>
Public Class SettingsRailTests
    Private Const MinContentLogical As Integer = 480
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
    Private Const WM_LBUTTONDOWN As UInteger = &H201UI
    Private Const WM_LBUTTONUP As UInteger = &H202UI
    Private Const MK_LBUTTON As Integer = 1

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(handle As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private ReadOnly output As ITestOutputHelper

    Public Sub New(output As ITestOutputHelper)
        Me.output = output
    End Sub

    ' --- harness ------------------------------------------------------------------------------------

    Private Shared Sub OnUi(test As Action, Optional seconds As Integer = 40)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(seconds)), "Rail check timed out.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>A shown manager-settings window on a fresh context: every page starts with its items folded.</summary>
    Private Shared Function OpenWindow() As Share_Settings_Form
        Dim window As New Share_Settings_Form()
        GetType(Share_Settings_Form).GetField("managerSettingsContext", Flags).SetValue(window, New SettingsUiContext())
        GetType(Share_Settings_Form).GetMethod("ApplySettingsNavExpansion", Flags).Invoke(window, Nothing)
        window.Show()
        window.ClientSize = New Size(820, 600)
        ' A real click on a button only counts when WindowFromPoint names the button's own window, so the window must be the
        ' top one at its place: a test window shown behind the desk's foreground window (Windows keeps a window of a process
        ' that is not in the foreground behind it) would otherwise swallow every click. Top-most, on the primary screen.
        window.StartPosition = FormStartPosition.Manual
        window.Location = New Point(20, 20)
        window.TopMost = True
        Application.DoEvents()
        Return window
    End Function

    Private Shared Function Field(Of T)(window As Share_Settings_Form, name As String) As T
        Return DirectCast(GetType(Share_Settings_Form).GetField(name, Flags).GetValue(window), T)
    End Function

    Private Shared Sub Run(window As Share_Settings_Form, name As String, ParamArray args As Object())
        GetType(Share_Settings_Form).GetMethod(name, Flags).Invoke(window, args)
    End Sub

    Private Shared Function Buttons(window As Share_Settings_Form) As Dictionary(Of String, Button)
        Return Field(Of Dictionary(Of String, Button))(window, "_navButtons")
    End Function

    Private Shared Function Items(window As Share_Settings_Form) As Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavItemButton))
        Return Field(Of Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavItemButton)))(window, "_navItems")
    End Function

    ''' <summary>Every row of the rail in display order, hidden ones too.</summary>
    Private Shared Function Rows(window As Share_Settings_Form) As List(Of Button)
        Return Field(Of FlowLayoutPanel)(window, "_navigation").Controls.OfType(Of Button)().ToList()
    End Function

    Private Shared Function SelectedPage(window As Share_Settings_Form) As String
        Return Field(Of String)(window, "_selectedPage")
    End Function

    Private Shared Function TreePage(window As Share_Settings_Form) As String
        Dim found = Items(window).Where(Function(pair) pair.Value.Count > 0).Select(Function(pair) pair.Key).FirstOrDefault()
        Assert.False(String.IsNullOrEmpty(found), "the window has no page with items to test the tree on")
        Return found
    End Function

    ''' <summary>A page row that has NO items. Every page of the Manager's windows carries at least one (each former fold
    ''' group of a page is one item), so a page without items is made from data, exactly as the window holds a page that
    ''' has no groups: its item rows leave the rail, its list is empty and its row is told it has none. The row, the
    ''' arrangement and the accessible object under test are the product's own; only the number of groups differs.</summary>
    Private Shared Function PlainPage(window As Share_Settings_Form, Optional except As String = "") As String
        Dim found = Buttons(window).Keys.FirstOrDefault(Function(id) id <> except AndAlso Items(window)(id).Count = 0)
        If String.IsNullOrEmpty(found) Then
            found = Buttons(window).Keys.LastOrDefault(Function(id) id <> except)
            Assert.False(String.IsNullOrEmpty(found), "the window has no second page to test selection on")
            Dim rail = Field(Of FlowLayoutPanel)(window, "_navigation")
            Dim list = Items(window)(found)
            For Each item In list.ToList()
                rail.Controls.Remove(item)
                item.Dispose()
            Next
            list.Clear()
            Dim row = DirectCast(Buttons(window)(found), Share_Settings_Form.SettingsNavRowButton)
            row.HasItems = False
            row.Expanded = False
            window.SetRailFactor(0.0F)
            Application.DoEvents()
        End If
        Return found
    End Function

    Private Shared Sub Press(window As Share_Settings_Form, row As Control, key As Keys)
        Run(window, "OnSettingsNavigationKeyDown", row, New KeyEventArgs(key))
    End Sub

    Private Shared Function Zone(row As Control) As Rectangle
        Return DirectCast(row.GetType().GetMethod("DisclosureRect", Flags).Invoke(row, Nothing), Rectangle)
    End Function

    Private Shared Function HasState(row As Control, state As AccessibleStates) As Boolean
        Return (row.AccessibilityObject.State And state) <> 0
    End Function

    ''' <summary>A real left click: the same two window messages the mouse produces, so the order of MouseDown, Click and
    ''' MouseUp is the framework's and not the test's.</summary>
    Private Shared Sub ClickAt(row As Control, x As Integer, y As Integer)
        Dim lParam As New IntPtr((y << 16) Or (x And &HFFFF))
        SendMessage(row.Handle, WM_LBUTTONDOWN, New IntPtr(MK_LBUTTON), lParam)
        SendMessage(row.Handle, WM_LBUTTONUP, IntPtr.Zero, lParam)
        Application.DoEvents()
    End Sub

    Private Shared Sub ClickChevron(row As Control)
        Dim z = Zone(row)
        Assert.False(z.IsEmpty, "the row has no chevron zone")
        ClickAt(row, z.Left + z.Width \ 2, z.Top + z.Height \ 2)
    End Sub

    ''' <summary>A click on the part of the row that is not the chevron (the glyph's side of the caption).</summary>
    Private Shared Sub ClickCaption(row As Control)
        Dim z = Zone(row)
        Dim x = If(z.IsEmpty, row.Width \ 2, If(z.Left = 0, z.Right + (row.Width - z.Right) \ 2, z.Left \ 2))
        ClickAt(row, x, row.Height \ 2)
    End Sub

    ' --- rule 7: semantics --------------------------------------------------------------------------

    <Fact>
    Public Sub Rows_are_outline_items_with_the_name_selection_and_expansion_a_tree_needs()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim nav = Field(Of FlowLayoutPanel)(window, "_navigation")
                     Assert.Equal(AccessibleRole.Outline, nav.AccessibleRole)
                     Dim tree = TreePage(window)
                     Dim plain = PlainPage(window)
                     Dim page = Buttons(window)(tree)
                     window.SelectPage(plain)
                     For Each row In Rows(window)
                         Dim acc = row.AccessibilityObject
                         Assert.Equal(AccessibleRole.OutlineItem, acc.Role)
                         Assert.Equal(row.Text, acc.Name)
                         Assert.True(HasState(row, AccessibleStates.Selectable) AndAlso HasState(row, AccessibleStates.Focusable), row.Text)
                         ' Expanded / Collapsed belong to a page that has items - and to nothing else.
                         Dim expandable = Buttons(window).Values.Contains(row) AndAlso DirectCast(row, Share_Settings_Form.SettingsNavRowButton).HasItems
                         Assert.Equal(expandable, HasState(row, AccessibleStates.Collapsed) OrElse HasState(row, AccessibleStates.Expanded))
                     Next
                     ' Exactly one row is selected, and it is the shown page.
                     Assert.Equal(1, Rows(window).Where(Function(row) HasState(row, AccessibleStates.Selected)).Count())
                     Assert.True(HasState(Buttons(window)(plain), AccessibleStates.Selected))
                     ' The folded page says Collapsed; its chevron is the second accessible action and unfolds it.
                     Assert.True(HasState(page, AccessibleStates.Collapsed))
                     Assert.False(HasState(page, AccessibleStates.Expanded))
                     Assert.Equal(1, page.AccessibilityObject.GetChildCount())
                     Assert.Equal(0, Buttons(window)(plain).AccessibilityObject.GetChildCount())
                     Assert.Equal(Localization.T("Выбрать"), page.AccessibilityObject.DefaultAction)
                     Dim chevron = page.AccessibilityObject.GetChild(0)
                     Assert.Equal(AccessibleRole.PushButton, chevron.Role)
                     Assert.Equal(Localization.T("Развернуть"), chevron.Name)
                     Assert.Equal(Localization.T("Развернуть"), chevron.DefaultAction)
                     chevron.DoDefaultAction()
                     Assert.True(HasState(page, AccessibleStates.Expanded))
                     Assert.False(HasState(page, AccessibleStates.Collapsed))
                     Assert.Equal(Localization.T("Свернуть"), page.AccessibilityObject.GetChild(0).Name)
                     Assert.Equal(plain, SelectedPage(window))
                     For Each item In Items(window)(tree)
                         Assert.Equal(0, item.AccessibilityObject.GetChildCount())
                     Next
                     ' A chosen item is the one selected row, not its page (the page only stands for the whole).
                     Dim group = Items(window)(tree)(0).Group
                     Run(window, "RevealSettingsBlock", tree, group, Nothing)
                     Assert.Equal(1, Rows(window).Where(Function(row) HasState(row, AccessibleStates.Selected)).Count())
                     Assert.True(HasState(Items(window)(tree)(0), AccessibleStates.Selected))
                     Assert.False(HasState(page, AccessibleStates.Selected))
                     ' The default action of any row chooses it.
                     Buttons(window)(plain).AccessibilityObject.DoDefaultAction()
                     Assert.Equal(plain, SelectedPage(window))
                     Assert.True(HasState(Buttons(window)(plain), AccessibleStates.Selected))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Choosing_a_child_reveals_its_parent_page_row()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     Run(window, "RevealSettingsBlock", tree, Items(window)(tree)(0).Group, Nothing)
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     Assert.True(HasState(Buttons(window)(tree), AccessibleStates.Expanded))
                     Assert.Equal(tree, SelectedPage(window))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub The_rail_has_one_roving_tab_stop_that_returns_to_the_selected_row()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Dim plain = PlainPage(window)
                     Run(window, "SetSettingsNavExpanded", tree, True)
                     window.SelectPage(plain)
                     Run(window, "ResetNavTabStop")
                     Assert.Equal(1, window.NavTabStopCount())
                     Assert.Same(Buttons(window)(plain), window.NavTabStopRow())
                     Assert.Equal(1, Rows(window).Where(Function(row) row.TabStop).Count())
                     ' An arrow key rolls the stop with the focus ring and leaves the selection where it was.
                     Dim visibleRows = Rows(window).Where(Function(row) row.Visible).ToList()
                     Press(window, window.NavTabStopRow(), If(visibleRows.Last() Is Buttons(window)(plain), Keys.Up, Keys.Down))
                     Assert.Equal(1, window.NavTabStopCount())
                     Assert.NotSame(Buttons(window)(plain), window.NavTabStopRow())
                     Assert.Equal(plain, SelectedPage(window))
                     ' When the focus leaves the rail the stop is back on the selected row (Tab re-enters where the user is).
                     GetType(Control).GetMethod("OnLeave", Flags).Invoke(Field(Of FlowLayoutPanel)(window, "_navigation"), New Object() {EventArgs.Empty})
                     Assert.Equal(1, window.NavTabStopCount())
                     Assert.Same(Buttons(window)(plain), window.NavTabStopRow())
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Arrow_keys_move_the_focus_over_the_visible_rows_and_skip_folded_children()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Dim selected = SelectedPage(window)
                     Dim walk = Sub()
                                    Dim visible = Rows(window).Where(Function(row) row.Visible).ToList()
                                    Press(window, window.NavTabStopRow(), Keys.Home)
                                    Assert.Same(visible.First(), window.NavTabStopRow())
                                    Dim seen As New List(Of Button)() From {window.NavTabStopRow()}
                                    For hop = 1 To visible.Count + 3
                                        Press(window, window.NavTabStopRow(), Keys.Down)
                                        If Not Object.ReferenceEquals(seen.Last(), window.NavTabStopRow()) Then seen.Add(window.NavTabStopRow())
                                    Next
                                    ' Every visible row once, in display order, none twice, none hidden, and the end does not wrap.
                                    Assert.Equal(visible.Count, seen.Count)
                                    For i = 0 To visible.Count - 1
                                        Assert.Same(visible(i), seen(i))
                                    Next
                                    Assert.Same(visible.Last(), window.NavTabStopRow())
                                    Press(window, window.NavTabStopRow(), Keys.Home)
                                    Press(window, window.NavTabStopRow(), Keys.Up)
                                    Assert.Same(visible.First(), window.NavTabStopRow())
                                    Press(window, window.NavTabStopRow(), Keys.End)
                                    Assert.Same(visible.Last(), window.NavTabStopRow())
                                End Sub
                     ' Folded: the page's children are not on the way.
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     walk()
                     ' Unfolded: they are, once each.
                     Run(window, "SetSettingsNavExpanded", tree, True)
                     Application.DoEvents()
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     walk()
                     ' No arrow key ever switched the page.
                     Assert.Equal(selected, SelectedPage(window))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    ' --- rule 4: select and expand are two targets --------------------------------------------------

    <Fact>
    Public Sub The_chevron_toggles_without_selecting_and_the_row_selects_without_toggling()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Dim plain = PlainPage(window)
                     Dim page = Buttons(window)(tree)
                     window.SelectPage(plain)
                     Application.DoEvents()
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))

                     ' The chevron zone: the items unfold, the selection stays on the other page.
                     ClickChevron(page)
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     Assert.Equal(plain, SelectedPage(window))
                     Assert.True(HasState(page, AccessibleStates.Expanded))
                     ClickChevron(page)
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     Assert.Equal(plain, SelectedPage(window))
                     Assert.True(HasState(page, AccessibleStates.Collapsed))

                     ' The rest of the row: the page is chosen, the folded items stay folded.
                     ClickCaption(page)
                     Assert.Equal(tree, SelectedPage(window))
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     ' And an unfolded list stays unfolded when its page row is chosen again.
                     ClickChevron(page)
                     window.SelectPage(plain)
                     ClickCaption(page)
                     Assert.Equal(tree, SelectedPage(window))
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))

                     ' A row without items has no chevron zone: every point of it chooses.
                     Dim other = Buttons(window)(plain)
                     Assert.True(Zone(other).IsEmpty)
                     ClickAt(other, other.Width - 4, other.Height \ 2)
                     Assert.Equal(plain, SelectedPage(window))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    <Fact>
    Public Sub Left_Right_fold_and_unfold_and_Enter_and_Space_choose()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Dim plain = PlainPage(window)
                     Dim page = Buttons(window)(tree)
                     window.SelectPage(plain)
                     ' Right unfolds, Left folds, and neither chooses the page (left-to-right reading).
                     Assert.Equal(RightToLeft.No, window.RightToLeft)
                     Press(window, page, Keys.Right)
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     Assert.Equal(plain, SelectedPage(window))
                     Press(window, page, Keys.Left)
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     Assert.Equal(plain, SelectedPage(window))
                     ' Enter and Space choose the focused row and never fold or unfold it.
                     Press(window, page, Keys.Return)
                     Assert.Equal(tree, SelectedPage(window))
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     window.SelectPage(plain)
                     Press(window, page, Keys.Space)
                     Assert.Equal(tree, SelectedPage(window))
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     ' Left on a child folds its page and the one tab stop goes to the page row.
                     Press(window, page, Keys.Right)
                     Dim child = Items(window)(tree)(0)
                     Run(window, "NoteNavFocus", child)
                     Assert.Same(child, window.NavTabStopRow())
                     Press(window, child, Keys.Left)
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     Assert.Equal(1, window.NavTabStopCount())
                     Assert.Same(page, window.NavTabStopRow())
                     ' Enter on a child chooses its block and keeps its page unfolded.
                     Press(window, page, Keys.Right)
                     Press(window, child, Keys.Return)
                     Application.DoEvents()
                     Assert.Equal(tree, SelectedPage(window))
                     Assert.True(HasState(child, AccessibleStates.Selected))
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    ' --- rule 5: sizes ------------------------------------------------------------------------------

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    Public Sub Rows_are_at_least_32_logical_px_with_a_16_glyph_an_8_gap_and_a_measured_rail(factor As Single)
        OnUi(Sub()
                 Using window = OpenWindow()
                     window.ClientSize = New Size(CInt(Math.Round(1000 * factor)), 600)
                     window.SetRailFactor(factor)
                     Application.DoEvents()
                     Dim target = SettingsRailRules.RowTargetPx(factor)
                     Dim glyph = SettingsRailRules.GlyphPx(factor)
                     Dim gap = SettingsRailRules.GlyphGapPx(factor)
                     Assert.Equal(CInt(Math.Round(32 * factor)), target)
                     Assert.Equal(CInt(Math.Round(16 * factor)), glyph)
                     Assert.Equal(CInt(Math.Round(8 * factor)), gap)
                     ' Hidden children are rows too: they must be right before they are shown.
                     For Each row In Rows(window)
                         Assert.True(row.Height >= target, row.Text & ": " & row.Height.ToString() & " < " & target.ToString())
                     Next
                     For Each pair In Buttons(window)
                         Dim image = pair.Value.Image
                         Assert.NotNull(image)
                         Assert.Equal(glyph, image.Height)
                         Assert.Equal(glyph + gap, image.Width)
                         Dim z = Zone(pair.Value)
                         If Items(window)(pair.Key).Count > 0 Then
                             Assert.True(z.Width >= target AndAlso z.Height >= target, pair.Key & " chevron zone " & z.ToString())
                         Else
                             Assert.True(z.IsEmpty)
                         End If
                     Next
                     ' The rail is the measured column of the shared rules, not a fixed number.
                     Dim column = CInt(Field(Of ColumnStyle)(window, "_railColumn").Width)
                     Dim pages = Buttons(window).Values.Select(Function(b) b.Text).ToList()
                     Dim children = Items(window).Values.SelectMany(Function(list) list).Select(Function(i) i.Text).ToList()
                     Dim expected = Share_Settings_Form.RailColumnPx(pages, children, True, Buttons(window).Values.First().Font, factor,
                                                                      window.ClientSize.Width, SystemInformation.VerticalScrollBarWidth)
                     Assert.Equal(expected, column)
                     Assert.True(column <= Math.Floor(window.ClientSize.Width * SettingsRailRules.MaxShareOfClient), "the rail stays within 30 percent of the client")
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    ' --- rule 1: the mirror --------------------------------------------------------------------------

    <Fact>
    Public Sub Under_RightToLeft_the_rail_its_chevron_its_indent_and_its_glyph_side_mirror()
        OnUi(Sub()
                 Using window = OpenWindow()
                     Dim tree = TreePage(window)
                     Dim plain = PlainPage(window)
                     Dim page = Buttons(window)(tree)
                     Dim nav = Field(Of FlowLayoutPanel)(window, "_navigation")
                     Dim host = Field(Of Panel)(window, "_pageHost")
                     Dim root = Field(Of TableLayoutPanel)(window, "_root")
                     Dim child = Items(window)(tree)(0)
                     window.SelectPage(tree)

                     ' Left-to-right: the rail left of the page, the chevron at the right end, the child indented from the left.
                     Assert.True(nav.Left < host.Left, "LTR: the rail is on the left")
                     Assert.Equal(page.Width, Zone(page).Right)
                     Dim ltrIndent = child.Padding
                     Dim ltrPage = page.Padding
                     Assert.True(ltrIndent.Left > ltrIndent.Right, "LTR: the child indent is on the left")
                     Assert.True(ltrPage.Right > ltrPage.Left, "LTR: the chevron zone's room is on the right")
                     Assert.True(AlphaIn(page, True) > 0 AndAlso AlphaIn(page, False) = 0,
                                 "LTR: the glyph is at the start of its picture, the gap after it")
                     Dim ltrInk = DrawnInk(page)

                     window.RightToLeft = RightToLeft.Yes
                     Application.DoEvents()
                     root.PerformLayout()
                     Application.DoEvents()
                     Assert.True(nav.Left > host.Left, "RTL: the rail is on the right")
                     Assert.Equal(0, Zone(page).Left)
                     Assert.True(child.Padding.Right > child.Padding.Left, "RTL: the child indent is on the right")
                     Assert.True(page.Padding.Left > page.Padding.Right, "RTL: the chevron zone's room is on the left")
                     Assert.Equal(ltrPage.Right, page.Padding.Left)
                     Assert.Equal(ltrPage.Left, page.Padding.Right)
                     Assert.Equal(ltrIndent.Left, child.Padding.Right)
                     Assert.Equal(ltrIndent.Right, child.Padding.Left)
                     ' The picture is padded on the side that faces the caption, which is the left now.
                     Assert.True(AlphaIn(page, False) = 0 AndAlso AlphaIn(page, True) > 0 AndAlso
                                 PaddedOnTheLeft(page), "RTL: the gap is on the picture's left")
                     ' Painted: the stripe of the selected row is at the right edge, the chevron in the left zone, the glyph at the right.
                     Dim rtlInk = DrawnInk(page)
                     Assert.True(ltrInk.ChevronZone > 0 AndAlso rtlInk.ChevronZone > 0, "the chevron is drawn in its zone in both directions")
                     Assert.True(rtlInk.Stripe > 0, "RTL: the selection stripe is drawn at the reading start (right) edge")
                     Assert.True(ltrInk.GlyphSlot > 0 AndAlso rtlInk.GlyphSlot > 0, "the glyph is drawn at the reading start in both directions")

                     ' The chevron zone is the target at the LEFT now; a click on the right chooses.
                     window.SelectPage(plain)
                     ClickChevron(page)
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     Assert.Equal(plain, SelectedPage(window))
                     ClickCaption(page)
                     Assert.Equal(tree, SelectedPage(window))
                     ' Left/Right swap: Left unfolds, Right folds.
                     Press(window, page, Keys.Right)
                     Assert.All(Items(window)(tree), Sub(item) Assert.False(item.Visible))
                     Press(window, page, Keys.Left)
                     Assert.All(Items(window)(tree), Sub(item) Assert.True(item.Visible))
                     window.Hide()
                 End Using
             End Sub)
    End Sub

    Private Structure Ink
        Public ChevronZone As Integer
        Public Stripe As Integer
        Public GlyphSlot As Integer
    End Structure

    ''' <summary>Counts the pixels that differ from the row's background in the three places the rule names: the chevron
    ''' zone, the 3 px selection stripe at the reading start, and the glyph's 16 px slot.</summary>
    Private Shared Function DrawnInk(row As Button) As Ink
        Dim rtl = row.RightToLeft = RightToLeft.Yes
        Dim factor = row.DeviceDpi / 96.0F
        Dim glyph = SettingsRailRules.GlyphPx(factor)
        Dim startInset = row.Padding.Left
        If rtl Then startInset = row.Padding.Right
        Using bitmap As New Bitmap(row.Width, row.Height)
            row.DrawToBitmap(bitmap, New Rectangle(0, 0, row.Width, row.Height))
            Dim count = Function(area As Rectangle) As Integer
                            Dim n = 0
                            area.Intersect(New Rectangle(0, 0, bitmap.Width, bitmap.Height))
                            For y = area.Top To area.Bottom - 1
                                For x = area.Left To area.Right - 1
                                    Dim c = bitmap.GetPixel(x, y)
                                    If Math.Abs(CInt(c.R) - row.BackColor.R) + Math.Abs(CInt(c.G) - row.BackColor.G) + Math.Abs(CInt(c.B) - row.BackColor.B) > 60 Then n += 1
                                Next
                            Next
                            Return n
                        End Function
            Dim chevronZone = Zone(row)
            Dim stripeWidth = row.LogicalToDeviceUnits(3)
            Dim stripe = If(rtl, New Rectangle(row.Width - stripeWidth, 0, stripeWidth, row.Height), New Rectangle(0, 0, stripeWidth, row.Height))
            ' The slot starts after the stripe and the start padding, inside the row's own inset.
            Dim slotLeft = If(rtl, row.Width - startInset - glyph, startInset)
            Dim slot = New Rectangle(slotLeft, (row.Height - glyph) \ 2, glyph, glyph)
            Return New Ink With {.ChevronZone = count(chevronZone), .Stripe = count(stripe), .GlyphSlot = count(slot)}
        End Using
    End Function

    ''' <summary>The summed alpha in the glyph columns (the first <c>glyph</c> columns in left-to-right, the last in
    ''' right-to-left) or in the gap columns of the row's padded picture.</summary>
    Private Shared Function AlphaIn(row As Button, glyphColumns As Boolean) As Long
        Dim image = DirectCast(row.Image, Bitmap)
        Dim factor = row.DeviceDpi / 96.0F
        Dim glyph = SettingsRailRules.GlyphPx(factor)
        Dim rtl = row.RightToLeft = RightToLeft.Yes
        Dim first As Integer
        Dim last As Integer
        If glyphColumns Then
            first = If(rtl, image.Width - glyph, 0)
            last = first + glyph - 1
        Else
            first = If(rtl, 0, glyph)
            last = first + (image.Width - glyph) - 1
        End If
        Dim sum As Long = 0
        For x = first To last
            For y = 0 To image.Height - 1
                sum += image.GetPixel(x, y).A
            Next
        Next
        Return sum
    End Function

    Private Shared Function PaddedOnTheLeft(row As Button) As Boolean
        Dim image = DirectCast(row.Image, Bitmap)
        Dim factor = row.DeviceDpi / 96.0F
        Dim gap = SettingsRailRules.GlyphGapPx(factor)
        For x = 0 To gap - 1
            For y = 0 To image.Height - 1
                If image.GetPixel(x, y).A <> 0 Then Return False
            Next
        Next
        Return True
    End Function

    ' --- rules 2 and 4: the items' glyphs ----------------------------------------------------------

    ''' <summary>An item draws a glyph only where the vocabulary already has its meaning (no glyph file is invented for
    ''' this); every other item is a missing meaning for the catalog's proposal procedure and keeps its caption where its
    ''' siblings' captions are. The set of missing meanings is pinned by name so an item joins or leaves it on purpose.</summary>
    <Fact>
    Public Sub Items_draw_a_catalogued_glyph_or_are_reported_as_missing_meanings()
        Dim missing As New List(Of String)()
        Dim drawn As New List(Of String)()
        OnUi(Sub()
                 Dim previousGate = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
                 Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 Dim worker = ExchangeTestSupport.MakeFakeWorker()
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     For Each destination In {SettingsDestination.Manager, SettingsDestination.ExchangeServer}
                         Using window As New Share_Settings_Form(destination:=destination)
                             Dim factor = window.DeviceDpi / 96.0F
                             Dim glyph = SettingsRailRules.GlyphPx(factor)
                             Dim gap = SettingsRailRules.GlyphGapPx(factor)
                             Dim captionStarts As New HashSet(Of Integer)()
                             For Each pair In Items(window)
                                 For Each item In pair.Value
                                     Dim id = pair.Key & "/" & item.Group.Id
                                     Dim catalogued = Share_Settings_Form.SettingsItemGlyph(pair.Key, item.Group.Id)
                                     If catalogued.Length = 0 Then
                                         Assert.Null(item.Image)
                                         missing.Add(id)
                                         captionStarts.Add(item.Padding.Left)
                                     Else
                                         Assert.True(IconMap.GetMeaning("managerSettingsNavItem_" & id.Replace("/", "_")).IsVocabulary, id)
                                         Dim image = DirectCast(item.Image, Bitmap)
                                         Assert.NotNull(image)
                                         Assert.Equal(glyph, image.Height)
                                         Assert.Equal(glyph + gap, image.Width)
                                         ' Ink in the glyph's columns, nothing in the gap after it (left-to-right).
                                         Assert.True(AlphaIn(item, True) > 0 AndAlso AlphaIn(item, False) = 0, id)
                                         drawn.Add(id)
                                         captionStarts.Add(item.Padding.Left + image.Width)
                                     End If
                                 Next
                             Next
                             ' Captions of one window's items start at the same place, with or without a glyph.
                             Assert.True(captionStarts.Count <= 1, destination.ToString() & ": item captions start at " & String.Join(", ", captionStarts))
                             window.Hide()
                         End Using
                     Next
                 Finally
                     Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, previousGate)
                     ExchangeProcess.RunOverride = Nothing
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                     ExchangeTestSupport.DeleteQuietly(worker)
                 End Try
             End Sub, 90)
        output.WriteLine("Items with a catalogued glyph: " & String.Join(", ", drawn))
        output.WriteLine("Missing meanings: " & String.Join(", ", missing))
        Assert.Equal(New String() {
            "exchange-account/account", "exchange/invitations", "exchange/server", "help/about", "help/documentation", "startup/appearance"},
            drawn.OrderBy(Function(id) id, StringComparer.Ordinal).ToArray())
        ' Reported to the catalog's proposal procedure (ICON-SET): none of these has a meaning in the vocabulary today.
        Assert.Equal(New String() {
            "drive/account", "drive/device", "exchange-account/clipboard", "exchange-account/publishing", "exchange/accounts", "exchange/configuration",
            "exchange/devices", "exchange/remote-admin", "exchange/service", "hosting/service", "hosting/status",
            "network/access", "network/connections", "scheduled/machine", "scheduled/operations", "startup/startup"},
            missing.OrderBy(Function(id) id, StringComparer.Ordinal).ToArray())
    End Sub

    ' --- rule 6: the narrow window ------------------------------------------------------------------

    Private NotInheritable Class Captions
        Public Code As String = ""
        Public Destination As SettingsDestination
        Public Pages As New List(Of String)()
        Public Children As New List(Of String)()
        Public FamilyName As String = ""
        Public Points As Single
        Public Style As FontStyle
        Public WindowFactor As Single
        Public RootPaddingLogical As Single
    End Class

    <Fact>
    Public Sub At_the_minimum_window_width_the_page_column_keeps_480_logical_px_in_every_language()
        Dim captured As New List(Of Captions)()
        Dim frameLogical As Single = 0
        OnUi(Sub()
                 Dim previous = Localization.CurrentCode
                 ExchangeTestSupport.Reset()
                 Dim component = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                 Dim worker = ExchangeTestSupport.MakeFakeWorker()
                 ExchangeProcess.ExeDirectoryOverride = component
                 ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                 Try
                     Using probe As New Form()
                         probe.FormBorderStyle = FormBorderStyle.Sizable
                         GC.KeepAlive(probe.Handle)
                         frameLogical = (probe.Width - probe.ClientSize.Width) / (probe.DeviceDpi / 96.0F)
                     End Using
                     For Each code In Localization.Codes
                         Localization.CurrentCode = code
                         For Each destination In {SettingsDestination.Manager, SettingsDestination.ExchangeServer}
                             Using window As New Share_Settings_Form(destination:=destination)
                                 Dim entry As New Captions With {.Code = code, .Destination = destination}
                                 Dim windowFactor = window.DeviceDpi / 96.0F
                                 Dim font = Buttons(window).Values.First().Font
                                 entry.FamilyName = font.FontFamily.Name
                                 entry.Points = font.SizeInPoints
                                 entry.Style = font.Style
                                 entry.WindowFactor = windowFactor
                                 entry.RootPaddingLogical = Field(Of TableLayoutPanel)(window, "_root").Padding.Horizontal / windowFactor
                                 entry.Pages.AddRange(Buttons(window).Values.Select(Function(b) b.Text))
                                 entry.Children.AddRange(Items(window).Values.SelectMany(Function(list) list).Select(Function(i) i.Text))
                                 captured.Add(entry)
                             End Using
                         Next
                     Next
                 Finally
                     Localization.CurrentCode = previous
                     ExchangeTestSupport.Reset()
                     ExchangeTestSupport.DeleteQuietly(component)
                     ExchangeTestSupport.DeleteQuietly(worker)
                 End Try
             End Sub, 150)

        Assert.Equal(Localization.Codes.Length * 2, captured.Count)
        Dim worstOverall As Double = Double.MaxValue
        Dim worstLabel As String = ""
        output.WriteLine(String.Format(CultureInfo.InvariantCulture, "Minimum window {0} x {1} logical px; frame {2:0.0} logical px.",
                                       Share_Settings_Form.MinimumWidthLogical, Share_Settings_Form.MinimumHeightLogical, frameLogical))
        For Each factor In {1.0F, 1.5F}
            Dim clientPx = CInt(Math.Round(Share_Settings_Form.MinimumWidthLogical * factor)) - CInt(Math.Round(frameLogical * factor))
            output.WriteLine(String.Format(CultureInfo.InvariantCulture, "Interface factor {0}: client {1} device px ({2:0.0} logical).", factor, clientPx, clientPx / factor))
            output.WriteLine("lang  destination      longest-page  longest-item  rail  page-column(logical)")
            Dim worst As Double = Double.MaxValue
            Dim worstEntry As String = ""
            For Each entry In captured
                Using font As New Font(entry.FamilyName, entry.Points * factor / entry.WindowFactor, entry.Style)
                    Dim scroll = CInt(Math.Round(SystemInformation.VerticalScrollBarWidth / entry.WindowFactor * factor))
                    Dim column = Share_Settings_Form.RailColumnPx(entry.Pages, entry.Children, entry.Children.Count > 0, font, factor, clientPx, scroll)
                    Dim content = (clientPx - UiSizing.DevicePx(CInt(Math.Round(entry.RootPaddingLogical)), factor) - column) / CDbl(factor)
                    output.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0,-4}  {1,-15}  {2,12}  {3,12}  {4,4}  {5,10:0.0}",
                        entry.Code, entry.Destination, Share_Settings_Form.LongestCaptionPx(entry.Pages, font),
                        Share_Settings_Form.LongestCaptionPx(entry.Children, font), column, content))
                    If content < worst Then
                        worst = content
                        worstEntry = entry.Code & "/" & entry.Destination.ToString() & " x" & factor.ToString(CultureInfo.InvariantCulture)
                    End If
                End Using
            Next
            output.WriteLine(String.Format(CultureInfo.InvariantCulture, "WORST x{0}: {1:0.0} logical px ({2}); shortfall to {3}: {4:0.0}.",
                                           factor, worst, worstEntry, MinContentLogical, Math.Max(0, MinContentLogical - worst)))
            If worst < worstOverall Then
                worstOverall = worst
                worstLabel = worstEntry
            End If
        Next
        Assert.True(worstOverall >= MinContentLogical,
                    String.Format(CultureInfo.InvariantCulture, "At the minimum width ({0} logical px) the page column is {1:0.0} logical px ({2}), below {3}.",
                                  Share_Settings_Form.MinimumWidthLogical, worstOverall, worstLabel, MinContentLogical))
        ' The figure is a window minimum a person can feel (never above 760), and the window really uses it.
        Assert.InRange(Share_Settings_Form.MinimumWidthLogical, 640, 760)
        OnUi(Sub()
                 Using window = OpenWindow()
                     Assert.Equal(window.LogicalToDeviceUnits(Share_Settings_Form.MinimumWidthLogical), window.MinimumSize.Width)
                     Assert.Equal(window.LogicalToDeviceUnits(Share_Settings_Form.MinimumHeightLogical), window.MinimumSize.Height)
                     window.Hide()
                 End Using
             End Sub)
    End Sub
End Class
