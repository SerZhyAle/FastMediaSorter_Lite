#If Not NETFRAMEWORK Then
Option Strict On
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The pure half of the painted popup menus and the panels (ticket 125): what a key means to an open menu
''' (UX-302, UX-322, UX-323), the hint of a row (UX-325), where a submenu opens (UX-322), how rows stack with
''' separators (UX-320), the scroll cue (UX-329), the sort words (UX-304), the touch rule of the long press
''' (UX-307), the reveal rules (UX-308, UX-309) and the contrast floors (UX-313h, UX-314). Plus a few checks
''' of the surface and the menu that need a window handle but no screen.
''' </summary>
' Popup dismissal observes the desktop foreground window. Other UI tests must not
' activate windows while this collection drives the real popup message filter.
<Collection("ChromeMenuDesktop")>
Public Class ChromeMenuRulesTests

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(15000), "Chrome menu check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ' --- key routing ---------------------------------------------------------------------------

    Private Shared Function Menu(Optional focused As Integer = -1, Optional submenu As Boolean = False,
                                 Optional rtl As Boolean = False, Optional opensSubmenu As Boolean = False) As MenuKeyContext
        Return New MenuKeyContext With {
            .IsSubmenu = submenu, .RightToLeft = rtl, .HasFocusedRow = focused >= 0, .FocusedIndex = focused,
            .FocusedRowOpensSubmenu = opensSubmenu,
            .Accelerators = New Char() {ChrW(0), "1"c, "2"c, "0"c},
            .Initials = New Char() {"A"c, "B"c, "B"c, "C"c},
            .Enabled = New Boolean() {True, True, True, True}}
    End Function

    Private Shared Function Suggestions(Optional focused As Integer = -1) As MenuKeyContext
        Dim context As MenuKeyContext = Menu(focused)
        context.IsSuggestionList = True
        Return context
    End Function

    Private Shared Function Resolve(key As Keys, context As MenuKeyContext) As MenuKeyAction
        Return ChromeMenuRules.ResolveKey(key, context).Action
    End Function

    <Fact>
    Public Sub Suggestion_list_leaves_every_editing_key_to_the_text_box_until_a_row_has_the_ring()
        Dim context As MenuKeyContext = Suggestions()
        For Each key As Keys In New Keys() {Keys.A, Keys.Z, Keys.D5, Keys.NumPad3, Keys.Space, Keys.Back, Keys.Delete,
                                            Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.Enter, Keys.OemPeriod,
                                            Keys.OemMinus, Keys.ShiftKey, Keys.Oemcomma, Keys.Oem5}
            Assert.Equal(MenuKeyAction.PassThrough, Resolve(key, context))
        Next
        ' Shift+letter is an upper-case letter: still typing.
        Assert.Equal(MenuKeyAction.PassThrough, Resolve(Keys.A Or Keys.Shift, context))
    End Sub

    <Fact>
    Public Sub Suggestion_list_arrows_put_a_row_in_focus_and_then_enter_and_space_belong_to_the_row()
        Assert.Equal(MenuKeyAction.FocusFirst, Resolve(Keys.Down, Suggestions()))
        Assert.Equal(MenuKeyAction.FocusLast, Resolve(Keys.Up, Suggestions()))
        Assert.Equal(MenuKeyAction.FocusFirst, Resolve(Keys.Tab, Suggestions()))
        Dim focused As MenuKeyContext = Suggestions(1)
        Assert.Equal(MenuKeyAction.Activate, Resolve(Keys.Enter, focused))
        Assert.Equal(MenuKeyAction.Activate, Resolve(Keys.Space, focused))
        Assert.Equal(MenuKeyAction.FocusNext, Resolve(Keys.Down, focused))
        Assert.Equal(MenuKeyAction.FocusPrevious, Resolve(Keys.Up, focused))
        ' Typing goes on to the box even with a row focused (the glue then drops the ring).
        Assert.Equal(MenuKeyAction.PassThrough, Resolve(Keys.B, focused))
        Assert.Equal(MenuKeyAction.PassThrough, Resolve(Keys.Back, focused))
        Assert.Equal(MenuKeyAction.Close, Resolve(Keys.Escape, focused))
    End Sub

    <Fact>
    Public Sub Ctrl_and_alt_chords_are_never_the_menus()
        Assert.Equal(MenuKeyAction.PassThrough, Resolve(Keys.C Or Keys.Control, Menu()))
        Assert.Equal(MenuKeyAction.PassThrough, Resolve(Keys.F4 Or Keys.Alt, Menu()))
    End Sub

    <Fact>
    Public Sub Menu_keys_navigate_close_and_swallow_the_rest()
        Assert.Equal(MenuKeyAction.FocusFirst, Resolve(Keys.Down, Menu()))
        Assert.Equal(MenuKeyAction.FocusLast, Resolve(Keys.Up, Menu()))
        Assert.Equal(MenuKeyAction.FocusNext, Resolve(Keys.Down, Menu(0)))
        Assert.Equal(MenuKeyAction.FocusPrevious, Resolve(Keys.Up, Menu(2)))
        Assert.Equal(MenuKeyAction.FocusPrevious, Resolve(Keys.Tab Or Keys.Shift, Menu(2)))
        Assert.Equal(MenuKeyAction.FocusFirst, Resolve(Keys.Home, Menu(2)))
        Assert.Equal(MenuKeyAction.FocusLast, Resolve(Keys.End, Menu(2)))
        Assert.Equal(MenuKeyAction.Close, Resolve(Keys.Escape, Menu()))
        Assert.Equal(MenuKeyAction.CloseAndEatF10, Resolve(Keys.F10, Menu()))
        ' A viewer key must not run while a menu is being browsed.
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Delete, Menu()))
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.ShiftKey, Menu()))
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.PageDown, Menu(1)))
        ' Enter or Space with no row focused does nothing (and is not the viewer's).
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Enter, Menu()))
        Assert.Equal(MenuKeyAction.Activate, Resolve(Keys.Enter, Menu(1)))
        Assert.Equal(MenuKeyAction.Activate, Resolve(Keys.Space, Menu(1)))
    End Sub

    <Fact>
    Public Sub Submenus_open_with_right_or_enter_and_return_one_level_with_left_or_escape()
        Dim parentRow As MenuKeyContext = Menu(1, opensSubmenu:=True)
        Assert.Equal(MenuKeyAction.OpenSubmenu, Resolve(Keys.Right, parentRow))
        Assert.Equal(MenuKeyAction.OpenSubmenu, Resolve(Keys.Enter, parentRow))
        Assert.Equal(MenuKeyAction.OpenSubmenu, Resolve(Keys.Space, parentRow))
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Right, Menu(1)))
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Left, Menu(1)))

        Dim child As MenuKeyContext = Menu(0, submenu:=True)
        Assert.Equal(MenuKeyAction.BackOneLevel, Resolve(Keys.Left, child))
        Assert.Equal(MenuKeyAction.BackOneLevel, Resolve(Keys.Escape, child))
    End Sub

    <Fact>
    Public Sub Right_to_left_menus_swap_left_and_right()
        Assert.Equal(MenuKeyAction.OpenSubmenu, Resolve(Keys.Left, Menu(1, rtl:=True, opensSubmenu:=True)))
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Right, Menu(1, rtl:=True, opensSubmenu:=True)))
        Assert.Equal(MenuKeyAction.BackOneLevel, Resolve(Keys.Right, Menu(0, submenu:=True, rtl:=True)))
        ' In a right-to-left submenu Left is the opening key, and on a command row it opens nothing.
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Left, Menu(0, submenu:=True, rtl:=True)))
    End Sub

    <Fact>
    Public Sub A_digit_runs_the_row_that_answers_to_it_and_nothing_else_does()
        Dim result As MenuKeyResult = ChromeMenuRules.ResolveKey(Keys.D2, Menu())
        Assert.Equal(MenuKeyAction.ByAccelerator, result.Action)
        Assert.Equal(2, result.Index)
        ' The NumPad digit is the same slot; "0" is a row of its own (slot 10).
        Assert.Equal(1, ChromeMenuRules.ResolveKey(Keys.NumPad1, Menu()).Index)
        Assert.Equal(3, ChromeMenuRules.ResolveKey(Keys.D0, Menu()).Index)
        ' A digit no row answers to is swallowed: a number key must not move a file under an open menu.
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.D7, Menu()))
    End Sub

    <Fact>
    Public Sub A_disabled_row_does_not_answer_to_its_digit()
        Dim context As MenuKeyContext = Menu()
        context.Enabled = New Boolean() {True, False, True, True}
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.D1, context))
        Assert.Equal(2, ChromeMenuRules.ResolveKey(Keys.D2, context).Index)
    End Sub

    <Fact>
    Public Sub A_letter_jumps_to_the_next_row_with_that_initial_and_wraps()
        Dim first As MenuKeyResult = ChromeMenuRules.ResolveKey(Keys.B, Menu())
        Assert.Equal(MenuKeyAction.TypeAhead, first.Action)
        Assert.Equal(1, first.Index)
        ' Again from row 1: the next B, then back round.
        Assert.Equal(2, ChromeMenuRules.ResolveKey(Keys.B, Menu(1)).Index)
        Assert.Equal(1, ChromeMenuRules.ResolveKey(Keys.B, Menu(2)).Index)
        Assert.Equal(0, ChromeMenuRules.ResolveKey(Keys.A, Menu(3)).Index)
        Assert.Equal(MenuKeyAction.Swallow, Resolve(Keys.Q, Menu()))
        Assert.Equal(-1, ChromeMenuRules.NextWithInitial(Nothing, Nothing, "A"c, 0))
        Assert.Equal("Р"c, ChromeMenuRules.InitialOf("  ..рассылка"))
        Assert.Equal(ChrW(0), ChromeMenuRules.InitialOf("..."))
    End Sub

    <Fact>
    Public Sub Type_ahead_follows_the_letter_the_layout_types_and_falls_back_to_the_latin_key()
        Dim context As MenuKeyContext = Menu()
        context.Initials = New Char() {"П"c, "Р"c, "A"c}
        context.Accelerators = New Char() {ChrW(0), ChrW(0), ChrW(0)}
        context.Enabled = New Boolean() {True, True, True}
        ' A Russian layout: the H key types "Р" - the row that starts with it, not a Latin H that no row has.
        context.TypedChar = "Р"c
        Dim cyrillic As MenuKeyResult = ChromeMenuRules.ResolveKey(Keys.H, context)
        Assert.Equal(MenuKeyAction.TypeAhead, cyrillic.Action)
        Assert.Equal(1, cyrillic.Index)
        ' The same layout, the A key types "Ф": no such row, so the Latin A still finds "A...".
        context.TypedChar = "Ф"c
        Assert.Equal(2, ChromeMenuRules.ResolveKey(Keys.A, context).Index)
        ' A layout we know nothing about: the Latin key alone, as before.
        context.TypedChar = ChrW(0)
        Assert.Equal(2, ChromeMenuRules.ResolveKey(Keys.A, context).Index)
        Assert.Equal(MenuKeyAction.Swallow, ChromeMenuRules.ResolveKey(Keys.H, context).Action)
    End Sub

    ' --- hints ---------------------------------------------------------------------------------

    <Theory>
    <InlineData("Удалить (Del)", "Удалить", "Del")>
    <InlineData("Rotate clockwise (R)", "Rotate clockwise", "R")>
    <InlineData("Вписать в окно (серый /)", "Вписать в окно", "серый /")>
    <InlineData("适应窗口（灰色 /）", "适应窗口", "灰色 /")>
    <InlineData("Переименовать.. (F6)", "Переименовать..", "F6")>
    <InlineData("Plain caption", "Plain caption", "")>
    <InlineData("(Del)", "(Del)", "")>
    <InlineData("Открыть защищённый файл.. (Enter)", "Открыть защищённый файл..", "Enter")>
    Public Sub A_bracketed_key_at_the_end_of_a_caption_is_split_off(caption As String, words As String, hint As String)
        Dim found As String = Nothing
        Assert.Equal(words, ChromeMenuRules.SplitHintSuffix(caption, found))
        Assert.Equal(hint, found)
    End Sub

    <Fact>
    Public Sub A_long_bracket_is_a_sentence_not_a_key()
        Dim found As String = Nothing
        Dim caption As String = "Open the file (and then think about it carefully, twice)"
        Assert.Equal(caption, ChromeMenuRules.SplitHintSuffix(caption, found))
        Assert.Equal("", found)
    End Sub

    <Fact>
    Public Sub A_hint_follows_the_live_key_map_and_not_the_literal_in_the_caption()
        Assert.Equal("Delete", ChromeMenuRules.HintFor(Nothing, "deleteFile", "Del"))
        Dim map As New Dictionary(Of String, String) From {{"deleteFile", "Ctrl+Delete"}}
        Assert.Equal("Ctrl+Delete", ChromeMenuRules.HintFor(map, "deleteFile", "Del"))
        ' An action the user left with no shortcut shows none - not the retired key.
        map("deleteFile") = CustomHotkeys.NoShortcut
        Assert.Equal("", ChromeMenuRules.HintFor(map, "deleteFile", "Del"))
        ' A key outside the catalogue (F11) is the caller's fallback.
        Assert.Equal("F11", ChromeMenuRules.HintFor(Nothing, "", "F11"))
        Assert.Equal("F11", ChromeMenuRules.HintFor(Nothing, "noSuchAction", "F11"))
    End Sub

    <Fact>
    Public Sub Every_action_the_menus_name_exists_in_the_catalogue()
        For Each id As String In New String() {"rotateCw", "rotateCcw", "zoomFit", "zoomActual", "ocrTranslate", "ocrAuto", "fullScreen",
                                              "nextFile", "prevFile", "renameFile", "deleteFile", "undo", "playPause", "mute", "frameStep", "abLoop"}
            Assert.NotNull(CustomHotkeys.Find(id))
        Next
    End Sub

    <Fact>
    Public Sub Symbol_keys_are_shown_as_their_symbol()
        Assert.Equal(".", ChromeMenuRules.PrettyCombo("OemPeriod"))
        Assert.Equal("[", ChromeMenuRules.PrettyCombo("OemOpenBrackets"))
        Assert.Equal("Shift+L", ChromeMenuRules.PrettyCombo("Shift+L"))
        Assert.Equal("Ctrl+Shift+]", ChromeMenuRules.PrettyCombo("Ctrl+Shift+OemCloseBrackets"))
        Assert.Equal("Num +", ChromeMenuRules.PrettyCombo("Num +"))
        Assert.Equal("", ChromeMenuRules.PrettyCombo(""))
    End Sub

    ' --- structure and geometry ----------------------------------------------------------------

    <Fact>
    Public Sub Separators_that_divide_nothing_are_dropped()
        Dim rows As New List(Of String) From {"-", "a", "-", "-", "b", "-", "c", "-", "-"}
        Dim tidy As List(Of String) = ChromeMenuRules.TidySeparators(rows, Function(row) row = "-")
        Assert.Equal(New String() {"a", "-", "b", "-", "c"}, tidy.ToArray())
        Assert.Empty(ChromeMenuRules.TidySeparators(New List(Of String) From {"-", "-"}, Function(row) row = "-"))
        Assert.Empty(ChromeMenuRules.TidySeparators(New List(Of String)(), Function(row) row = "-"))
    End Sub

    <Fact>
    Public Sub Rows_stack_top_to_bottom_with_padding_and_a_slim_separator()
        Dim total As Integer = 0
        Dim sep As Integer = ChromeMenuRules.SeparatorHeight(4)
        Assert.True(sep < 32 \ 2)
        Dim tops As Integer() = ChromeMenuRules.StackRows(New Integer() {32, sep, 32}, 6, total)
        Assert.Equal(New Integer() {6, 38, 38 + sep}, tops)
        Assert.Equal(38 + sep + 32 + 6, total)
    End Sub

    <Fact>
    Public Sub A_submenu_opens_beside_its_row_and_flips_at_the_screen_edge()
        Dim work As New Rectangle(0, 0, 1920, 1040)
        Dim parent As New Rectangle(100, 100, 220, 400)
        Dim child As New Size(180, 300)
        Dim origin As Point = ChromeMenuRules.SubmenuOrigin(parent, 150, child, work, False, 4)
        Assert.Equal(New Point(316, 150), origin)
        ' No room on the right: it opens on the parent's left, overlapping by the same margin.
        Dim nearEdge As New Rectangle(1800, 100, 110, 400)
        Assert.Equal(1800 - 180 + 4, ChromeMenuRules.SubmenuOrigin(nearEdge, 150, child, work, False, 4).X)
        ' Right to left opens to the left first, and flips to the right when that leaves the screen.
        Assert.Equal(100 - 180 + 4 + 100, ChromeMenuRules.SubmenuOrigin(New Rectangle(200, 100, 220, 400), 150, child, work, True, 4).X)
        Assert.Equal(parent.Right - 4, ChromeMenuRules.SubmenuOrigin(parent, 150, child, work, True, 4).X)
        ' Never below the work area: the row's level gives way to the screen.
        Assert.Equal(1040 - 300, ChromeMenuRules.SubmenuOrigin(parent, 900, child, work, False, 4).Y)
        Assert.Equal(0, ChromeMenuRules.SubmenuOrigin(parent, -50, child, work, False, 4).Y)
    End Sub

    <Fact>
    Public Sub Scroll_cues_show_only_the_edge_that_has_more_behind_it()
        Dim above, below As Boolean
        ChromeMenuRules.ScrollCues(0, 500, 500, above, below)
        Assert.False(above) : Assert.False(below)
        ChromeMenuRules.ScrollCues(0, 1000, 400, above, below)
        Assert.False(above) : Assert.True(below)
        ChromeMenuRules.ScrollCues(300, 1000, 400, above, below)
        Assert.True(above) : Assert.True(below)
        ChromeMenuRules.ScrollCues(600, 1000, 400, above, below)
        Assert.True(above) : Assert.False(below)
    End Sub

    ' --- small rules ---------------------------------------------------------------------------

    <Fact>
    Public Sub The_overflow_menu_has_no_row_for_a_counter_or_a_zoom_label()
        Assert.False(ChromeMenuRules.OverflowEntryIsActionable(ChromeItemKind.Text))
        Assert.True(ChromeMenuRules.OverflowEntryIsActionable(ChromeItemKind.Button))
        Assert.True(ChromeMenuRules.OverflowEntryIsActionable(ChromeItemKind.Toggle))
        Assert.True(ChromeMenuRules.OverflowEntryIsActionable(ChromeItemKind.MenuButton))
    End Sub

    <Fact>
    Public Sub Only_a_touch_or_pen_press_may_become_a_long_press()
        ' MI_WP_SIGNATURE 0xFF515700 with the touch bit (0x80) or without: a pen or touch screen.
        Assert.True(ChromeMenuRules.IsTouchOrPenExtraInfo(&HFF515700L))
        Assert.True(ChromeMenuRules.IsTouchOrPenExtraInfo(&HFF515780L))
        Assert.True(ChromeMenuRules.IsTouchOrPenExtraInfo(&HFF51577FL))
        ' A mouse carries nothing (or an application's own number): it has the right button.
        Assert.False(ChromeMenuRules.IsTouchOrPenExtraInfo(0L))
        Assert.False(ChromeMenuRules.IsTouchOrPenExtraInfo(&H12345L))
        Assert.False(ChromeMenuRules.IsTouchOrPenExtraInfo(&HFF515600L))
    End Sub

    <Fact>
    Public Sub Only_the_status_panel_reacts_to_a_counter_zoom_or_status_change()
        Assert.True(ChromeMenuRules.PanelRevealsOnTextChange("chrome_Status"))
        Assert.False(ChromeMenuRules.PanelRevealsOnTextChange("chrome_Row1"))
        Assert.False(ChromeMenuRules.PanelRevealsOnTextChange("chrome_Row2"))
        Assert.False(ChromeMenuRules.PanelRevealsOnTextChange(""))
        Assert.False(ChromeMenuRules.PanelRevealsOnTextChange(Nothing))
    End Sub

    <Fact>
    Public Sub Both_top_rows_share_one_reveal_zone_measured_from_the_lowest()
        ' Row 1 ends at 38, row 2 (under it) at 76, the zone margin is 48.
        Assert.Equal(76 + 48, ChromeAutoHide.TopHotZoneBottom(New Integer() {38, 76}, 48))
        Assert.Equal(76 + 48, ChromeAutoHide.TopHotZoneBottom(New Integer() {76, 38}, 48))
        Assert.Equal(48, ChromeAutoHide.TopHotZoneBottom(New Integer() {}, 48))
        ' A pointer at 100 px is inside the zone for BOTH rows: row 2 can no longer appear alone.
        Assert.True(100 < ChromeAutoHide.TopHotZoneBottom(New Integer() {38, 76}, 48))
    End Sub

    <Fact>
    Public Sub Sort_orders_have_words_and_a_direction_and_the_codes_stay_untouched()
        Dim arrow As Char
        Dim codes As String() = {"abc", "xyz", "rnd", ">size", "<size", ">time", "<time", "<0123", ">3210"}
        For index As Integer = 0 To codes.Length - 1
            Dim key As String = ChromeMenuRules.SortOrderLabel(index, arrow)
            Assert.False(String.IsNullOrEmpty(key), codes(index) & " has no words")
            If codes(index) = "rnd" Then
                Assert.Equal(ChrW(0), arrow)
            Else
                Assert.True(arrow = ChrW(&H2191) OrElse arrow = ChrW(&H2193), codes(index) & " has no arrow")
            End If
        Next
        ' Ascending names and descending names point opposite ways; newest first (">time") points down.
        Dim up, down As Char
        Assert.Equal(ChromeMenuRules.SortOrderLabel(0, up), ChromeMenuRules.SortOrderLabel(1, down))
        Assert.NotEqual(up, down)
        ChromeMenuRules.SortOrderLabel(5, down)
        ChromeMenuRules.SortOrderLabel(6, up)
        Assert.Equal(ChrW(&H2193), down)
        Assert.Equal(ChrW(&H2191), up)
        Assert.Equal("", ChromeMenuRules.SortOrderLabel(99, arrow))
        Assert.Equal("", ChromeMenuRules.SortOrderLabel(-1, arrow))
    End Sub

    <Fact>
    Public Sub Every_sort_word_is_a_registered_localization_key()
        Dim arrow As Char
        For index As Integer = 0 To 8
            Dim key As String = ChromeMenuRules.SortOrderLabel(index, arrow)
            ' The key reaches Localization.T as a variable, so the call-site scan of the coverage test cannot see it:
            ' this is the check that it is registered, with all twelve other languages.
            Dim registered As Boolean = Localization.All.Any(Function(pair) pair.Key = key)
            Assert.True(registered, key & " is not registered")
        Next
    End Sub

    ' --- contrast ------------------------------------------------------------------------------

    <Theory>
    <InlineData(0, 0, 0, 255, 255, 255)>
    <InlineData(255, 255, 255, 0, 0, 0)>
    <InlineData(32, 32, 32, 220, 220, 220)>
    <InlineData(245, 245, 245, 30, 30, 30)>
    Public Sub Disabled_ink_and_the_corner_pin_read_at_about_three_to_one_on_light_and_dark(br As Integer, bg As Integer, bb As Integer,
                                                                                         fr As Integer, fg As Integer, fb As Integer)
        Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.FromArgb(br, bg, bb), Color.FromArgb(fr, fg, fb), False)
        Assert.True(ChromeTheme.ContrastRatio(theme.DisabledForeground, theme.PanelFill) >= 2.95R,
                    "disabled ink " & ChromeTheme.ContrastRatio(theme.DisabledForeground, theme.PanelFill).ToString("0.00"))
        Assert.True(ChromeTheme.ContrastRatio(theme.PinRestInk(False), theme.PanelFill) >= 2.95R,
                    "free pin " & ChromeTheme.ContrastRatio(theme.PinRestInk(False), theme.PanelFill).ToString("0.00"))
        Assert.True(ChromeTheme.ContrastRatio(theme.PinRestInk(True), theme.PanelFill) >= ChromeTheme.ContrastRatio(theme.PinRestInk(False), theme.PanelFill))
    End Sub

    <Fact>
    Public Sub Contrast_ratio_is_the_wcag_one()
        Assert.Equal(21.0R, ChromeTheme.ContrastRatio(Color.Black, Color.White), 2)
        Assert.Equal(1.0R, ChromeTheme.ContrastRatio(Color.Gray, Color.Gray), 3)
    End Sub

    ' --- surface and menu (window handle, no screen) ---------------------------------------------

    <Fact>
    Public Sub A_vertical_surface_stacks_slim_separators_and_skips_them_for_hit_test_and_focus()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      surface.VerticalLayout = True
                      surface.Size = New Size(260, 400)
                      Dim first As New ChromeItem("first", ChromeItemKind.Button) With {.Caption = "First"}
                      Dim line As New ChromeItem("line", ChromeItemKind.Button) With {.IsSeparator = True}
                      Dim second As New ChromeItem("second", ChromeItemKind.Button) With {.Caption = "Second"}
                      surface.SetItems({first, line, second})
                      Assert.True(line.Bounds.Height < first.Bounds.Height \ 2)
                      Assert.Equal(first.Bounds.Bottom, line.Bounds.Top)
                      Assert.Equal(line.Bounds.Bottom, second.Bounds.Top)
                      Assert.Null(surface.HitTest(New Point(10, line.Bounds.Top + 1)))
                      Assert.Same(second, surface.HitTest(New Point(10, second.Bounds.Top + 1)))
                      ' The ring walks past the separator.
                      Assert.True(surface.EnterKeyboardMode())
                      Assert.Same(first, surface.FocusedRow)
                      Assert.True(surface.MoveFocus(True))
                      Assert.Same(second, surface.FocusedRow)
                      Assert.False(surface.MoveFocus(True))
                      Dim size As Size = surface.PreferredPanelSize()
                      Assert.Equal(first.Bounds.Height + line.Bounds.Height + second.Bounds.Height + 2 * surface.PanelPadding(), size.Height)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_menu_row_with_a_hint_or_a_submenu_arrow_is_wider_than_the_same_row_without()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      surface.VerticalLayout = True
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      Dim plain As New ChromeItem("a", ChromeItemKind.Button) With {.Caption = "Rename.."}
                      Dim hinted As New ChromeItem("b", ChromeItemKind.Button) With {.Caption = "Rename..", .Hint = "F6"}
                      Dim submenu As New ChromeItem("c", ChromeItemKind.Button) With {.Caption = "Rename..", .Chevron = True}
                      Assert.True(surface.MeasureItemWidth(hinted) > surface.MeasureItemWidth(plain))
                      Assert.True(surface.MeasureItemWidth(submenu) > surface.MeasureItemWidth(plain))
                      Dim line As New ChromeItem("d", ChromeItemKind.Button) With {.IsSeparator = True}
                      Assert.Equal(0, surface.MeasureItemWidth(line))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_menu_button_keeps_room_for_its_chevron_and_a_plain_button_does_not()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      Dim menuButton As New ChromeItem("btn_Language", ChromeItemKind.MenuButton) With {.Caption = "Language"}
                      Dim button As New ChromeItem("btn_X", ChromeItemKind.Button) With {.Caption = "Language"}
                      Assert.Equal(surface.MeasureItemWidth(button) + UiScale.Px(ChromeMetrics.ChevronWidth, 1.0F), surface.MeasureItemWidth(menuButton))
                      ' The "more" button itself is not a menu button in this sense.
                      Dim more As New ChromeItem(surface.OverflowId, ChromeItemKind.MenuButton) With {.Caption = ChrW(&HBB)}
                      Assert.Equal(surface.MeasureItemWidth(New ChromeItem("btn_Y", ChromeItemKind.Button) With {.Caption = ChrW(&HBB)}), surface.MeasureItemWidth(more))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_reserved_text_keeps_the_slot_of_a_text_that_changes_length()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      Dim counter As New ChromeItem("lbl_File_Number", ChromeItemKind.Text) With {.Caption = "1 of 2", .ReservedText = "9999 of 9999"}
                      Dim shortWidth As Integer = surface.MeasureItemWidth(counter)
                      counter.Caption = "1234 of 5678"
                      Assert.Equal(shortWidth, surface.MeasureItemWidth(counter))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_zoom_value_that_is_away_still_measures_its_reserved_text_and_not_a_bare_square()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      Dim zoom As New ChromeItem("lbl_Zoom", ChromeItemKind.Text) With {.Caption = "", .ReservedText = "1600 %", .Visible = False}
                      Dim bare As New ChromeItem("x", ChromeItemKind.Text) With {.Caption = ""}
                      Assert.True(surface.MeasureItemWidth(zoom) > surface.MeasureItemWidth(bare),
                                  "an empty caption with a reserved text must reserve the text's width")
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_conditional_button_that_is_away_keeps_its_place_so_the_next_one_does_not_move()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      surface.Size = New Size(900, 44)
                      Dim first As New ChromeItem("first", ChromeItemKind.Button) With {.Caption = "First"}
                      Dim edit As New ChromeItem("btn_Edit", ChromeItemKind.Button) With {.Caption = "Edit", .Visible = False, .KeepsSlotWhenHidden = True}
                      Dim menu As New ChromeItem("menu", ChromeItemKind.Button) With {.Caption = "Menu"}
                      surface.SetItems({first, edit, menu})
                      Dim whileAway As Integer = menu.Bounds.X
                      ' Away: nothing is placed, painted or hit in its place - it is air.
                      Assert.True(edit.Bounds.IsEmpty)
                      Assert.Null(surface.HitTest(New Point(edit.Bounds.X + 1, 10)))
                      Assert.DoesNotContain(edit, surface.OverflowItems)
                      ' It comes back: the next one is exactly where it was.
                      edit.Visible = True
                      surface.LayoutItems()
                      Assert.False(edit.Bounds.IsEmpty)
                      Assert.Equal(whileAway, menu.Bounds.X)
                      ' An item that does NOT keep its place lets the next one close up - the old behaviour.
                      edit.Visible = False
                      edit.KeepsSlotWhenHidden = False
                      surface.LayoutItems()
                      Assert.True(menu.Bounds.X < whileAway)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_kept_place_with_nothing_visible_after_it_holds_nothing_and_adds_no_width()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font)
                      surface.Size = New Size(900, 44)
                      Dim first As New ChromeItem("first", ChromeItemKind.Button) With {.Caption = "First"}
                      Dim menu As New ChromeItem("menu", ChromeItemKind.Button) With {.Caption = "Menu"}
                      surface.SetItems({first, menu})
                      Dim plain As Size = surface.PreferredPanelSize()
                      Dim tracks As New ChromeItem("btn_Tracks", ChromeItemKind.Button) With {.Caption = "Tracks", .Visible = False, .KeepsSlotWhenHidden = True}
                      Dim toVideo As New ChromeItem("btn_ToVideo", ChromeItemKind.Button) With {.Caption = "To video", .Visible = False, .KeepsSlotWhenHidden = True}
                      surface.SetItems({first, menu, tracks, toVideo})
                      Assert.Equal(plain.Width, surface.PreferredPanelSize().Width)
                      ' With something visible after them they are kept, and the panel is as wide as the row it holds.
                      Dim last As New ChromeItem("last", ChromeItemKind.Button) With {.Caption = "Last"}
                      surface.SetItems({first, tracks, toVideo, last})
                      Assert.True(surface.PreferredPanelSize().Width > plain.Width + surface.MeasureItemWidth(tracks))
                  End Using
              End Sub)
    End Sub

    <Theory>
    <InlineData("lbl_Zoom", False, True)>
    <InlineData("lbl_Slideshow_Time", False, True)>
    <InlineData("btn_Translate", False, True)>
    <InlineData("btn_Edit", False, True)>
    <InlineData("btn_Tracks", False, True)>
    <InlineData("btn_ToVideo", False, True)>
    <InlineData("btn_Image_Menu", False, True)>
    <InlineData("btn_TranslateBrowser", False, False)>
    <InlineData("btn_TranslateBrowser", True, True)>
    <InlineData("btn_Resource", True, False)>
    <InlineData("btn_Rename", True, False)>
    <InlineData("bt_Delete", True, False)>
    <InlineData("cmbox_Media_Folder", True, False)>
    Public Sub Only_what_comes_and_goes_with_the_kind_of_the_file_keeps_its_place(name As String, companion As Boolean, expected As Boolean)
        Assert.Equal(expected, ChromeMenuRules.KeepsSlotWhenHidden(name, companion))
    End Sub

    <Fact>
    Public Sub The_small_size_never_goes_under_the_28_pixel_floor_and_the_touch_size_reaches_44()
        OnSta(Sub()
                  Using surface As New ChromeSurface()
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), UiScale.SizeMultiplier("small", 0.0F), False, font, font)
                      Assert.True(surface.ItemHeight() >= ChromeMetrics.MinTarget, "small: " & surface.ItemHeight().ToString())
                      Dim wide As New ChromeItem("w", ChromeItemKind.Button) With {.Caption = "x"}
                      Assert.True(surface.MeasureItemWidth(New ChromeItem("g", ChromeItemKind.Button) With {.GlyphId = "media.next"}) >= ChromeMetrics.MinTarget)
                      surface.Configure(ChromeTheme.FromScheme(Color.Black, Color.White, False), UiScale.SizeMultiplier("touch", 0.0F), False, font, font)
                      Assert.True(surface.ItemHeight() >= 44, "touch: " & surface.ItemHeight().ToString())
                      Assert.True(surface.MeasureItemWidth(New ChromeItem("g", ChromeItemKind.Button) With {.GlyphId = "media.next"}) >= 44)
                  End Using
              End Sub)
        Assert.Equal(1.0F, UiScale.SizeMultiplier("medium", 0.0F))
        Assert.True(UiScale.Px(ChromeMetrics.MinTarget, UiScale.TouchPreset) >= 44)
    End Sub

    <Fact>
    Public Sub A_menu_builds_its_rows_from_the_model_with_separators_tidied_and_the_reason_as_the_tooltip()
        OnSta(Sub()
                  Using menu As New ChromeMenu()
                      Dim theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.White, False)
                      Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
                      menu.Configure(theme, 1.0F, False, font, font)
                      Dim child As New List(Of ChromeMenuItem) From {New ChromeMenuItem("c1") With {.Caption = "1  A", .Accelerator = "1"c}}
                      menu.SetItems(New List(Of ChromeMenuItem) From {
                          ChromeMenuItem.Separator(),
                          New ChromeMenuItem("undo") With {.Caption = "Undo", .Hint = "U", .Enabled = False, .DisabledReason = "Nothing to undo"},
                          ChromeMenuItem.Separator(),
                          ChromeMenuItem.Separator(),
                          New ChromeMenuItem("move") With {.Caption = "Move to", .Submenu = child},
                          New ChromeMenuItem("hidden") With {.Caption = "Hidden", .Visible = False},
                          New ChromeMenuItem("del") With {.Caption = "Delete", .Hint = "Del", .Destructive = True},
                          ChromeMenuItem.Separator()})
                      Dim rows As IReadOnlyList(Of ChromeItem) = menu.Surface.Items
                      Assert.Equal(New String() {"undo", "separator", "move", "del"}, rows.Select(Function(row) row.Id).ToArray())
                      Assert.False(rows(0).Enabled)
                      Assert.Equal("Nothing to undo", rows(0).Tooltip)
                      Assert.Equal("U", rows(0).Hint)
                      Assert.True(rows(1).IsSeparator)
                      Assert.True(rows(2).Chevron)
                      Assert.False(rows(3).Chevron)
                      Assert.True(rows(3).Destructive)
                  End Using
              End Sub)
    End Sub

    ' --- the open chain (real popup windows, driven by keys; no message loop, no screen needed) ---------

    <Fact>
    Public Sub Escape_hold_is_consumed_after_the_popup_filter_is_removed()
        OnSta(Sub()
                  Dim items As New List(Of ChromeMenuItem) From {New ChromeMenuItem("one") With {.Caption = "One"}}
                  WithPopup(items, True, False,
                            Sub()
                                Dim down As Message = Message.Create(IntPtr.Zero, &H100, New IntPtr(CInt(Keys.Escape)), New IntPtr(1))
                                Assert.True(Application.FilterMessage(down))
                                Assert.False(ChromeMenu.PopupVisible)
                                Dim repeat As Message = Message.Create(IntPtr.Zero, &H100, New IntPtr(CInt(Keys.Escape)), New IntPtr(&H40000001))
                                Assert.True(Application.FilterMessage(repeat))
                                Dim up As Message = Message.Create(IntPtr.Zero, &H101, New IntPtr(CInt(Keys.Escape)), IntPtr.Zero)
                                Assert.True(Application.FilterMessage(up))
                                Assert.False(Application.FilterMessage(down))
                            End Sub)
              End Sub)
    End Sub

    Private Shared Sub WithPopup(items As List(Of ChromeMenuItem), focusFirst As Boolean, suggestions As Boolean, body As Action)
        Using owner As New Form()
            Dim font As New Font("Segoe UI", 12.0F, FontStyle.Regular, GraphicsUnit.Pixel)
            Try
                ChromeMenu.ShowPopup(owner, New Point(100, 100), items, ChromeTheme.FromScheme(Color.Black, Color.White, False), 1.0F, False, font, font,
                                     focusFirstRow:=focusFirst, isSuggestionList:=suggestions)
                body()
            Finally
                ChromeMenu.DismissPopup()
                Application.DoEvents()
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub A_submenu_opens_beside_its_row_keeps_the_parent_and_a_digit_runs_its_slot()
        OnSta(Sub()
                  Dim ran As String = ""
                  Dim slots As New List(Of ChromeMenuItem) From {
                      New ChromeMenuItem("c1") With {.Caption = "1  one", .Accelerator = "1"c, .Action = Sub() ran = "c1"},
                      New ChromeMenuItem("c2") With {.Caption = "2  two", .Accelerator = "2"c, .Action = Sub() ran = "c2"}}
                  Dim items As New List(Of ChromeMenuItem) From {
                      New ChromeMenuItem("a") With {.Caption = "Alpha", .Action = Sub() ran = "a"},
                      New ChromeMenuItem("move") With {.Caption = "Move to", .Submenu = slots},
                      New ChromeMenuItem("c") With {.Caption = "Charlie", .Action = Sub() ran = "c"}}
                  WithPopup(items, True, False,
                            Sub()
                                Assert.Equal(1, ChromeMenu.OpenLevels)
                                Assert.Equal("a", ChromeMenu.FocusedRowOfLeaf.Id)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.Equal("move", ChromeMenu.FocusedRowOfLeaf.Id)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Right))
                                Assert.Equal(2, ChromeMenu.OpenLevels)
                                Dim levels As List(Of Rectangle) = ChromeMenu.OpenBounds()
                                ' Beside the parent: it starts at the parent's right edge less the overlap and does not cover it.
                                Assert.Equal(levels(0).Right - UiScale.Px(ChromeMetrics.SubmenuOverlap, 1.0F), levels(1).Left)
                                Assert.True(levels(1).Top >= levels(0).Top)
                                ' Left returns one level and the parent is still there.
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Left))
                                Assert.Equal(1, ChromeMenu.OpenLevels)
                                Assert.Equal("", ran)
                                ' Open again; a digit runs the slot and closes the whole chain.
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Enter))
                                Assert.Equal(2, ChromeMenu.OpenLevels)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.D2))
                                Assert.Equal("c2", ran)
                                Assert.Equal(0, ChromeMenu.OpenLevels)
                                Assert.False(ChromeMenu.PopupVisible)
                            End Sub)
              End Sub)
    End Sub

    <Fact>
    Public Sub Escape_goes_back_one_level_then_closes_and_a_digit_without_a_row_moves_nothing()
        OnSta(Sub()
                  Dim ran As String = ""
                  Dim items As New List(Of ChromeMenuItem) From {
                      New ChromeMenuItem("move") With {.Caption = "Move to", .Submenu = New List(Of ChromeMenuItem) From {
                          New ChromeMenuItem("c1") With {.Caption = "1  one", .Accelerator = "1"c, .Action = Sub() ran = "c1"}}},
                      New ChromeMenuItem("b") With {.Caption = "Bravo", .Action = Sub() ran = "b"}}
                  WithPopup(items, True, False,
                            Sub()
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Right))
                                Assert.Equal(2, ChromeMenu.OpenLevels)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Escape))
                                Assert.Equal(1, ChromeMenu.OpenLevels)
                                ' A digit no row of THIS level answers to is swallowed, never the viewer's.
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.D5))
                                Assert.Equal("", ran)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Escape))
                                Assert.False(ChromeMenu.PopupVisible)
                            End Sub)
              End Sub)
    End Sub

    <Fact>
    Public Sub A_menu_opened_by_the_mouse_shows_no_ring_until_an_arrow_key_asks_for_one()
        OnSta(Sub()
                  Dim items As New List(Of ChromeMenuItem) From {
                      New ChromeMenuItem("a") With {.Caption = "Alpha"}, ChromeMenuItem.Separator(), New ChromeMenuItem("b") With {.Caption = "Bravo"}}
                  WithPopup(items, False, False,
                            Sub()
                                Assert.Null(ChromeMenu.FocusedRowOfLeaf)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.Equal("a", ChromeMenu.FocusedRowOfLeaf.Id)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.Equal("b", ChromeMenu.FocusedRowOfLeaf.Id)
                                ' A letter jumps to the row with that initial.
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.A))
                                Assert.Equal("a", ChromeMenu.FocusedRowOfLeaf.Id)
                            End Sub)
              End Sub)
    End Sub

    <Fact>
    Public Sub The_suggestion_list_lets_typing_through_and_picks_a_row_only_after_an_arrow_key()
        OnSta(Sub()
                  Dim picked As String = ""
                  Dim items As New List(Of ChromeMenuItem) From {
                      New ChromeMenuItem("p1") With {.Caption = "C:\one", .Action = Sub() picked = "p1"},
                      New ChromeMenuItem("p2") With {.Caption = "C:\two", .Action = Sub() picked = "p2"}}
                  WithPopup(items, True, True,
                            Sub()
                                Assert.True(ChromeMenu.SuggestionListVisible)
                                ' Nothing has the ring: every key that edits text goes on to the text box.
                                Assert.Null(ChromeMenu.FocusedRowOfLeaf)
                                For Each key As Keys In New Keys() {Keys.A, Keys.Space, Keys.Back, Keys.D3, Keys.Left, Keys.Enter}
                                    Assert.False(ChromeMenu.HandlePopupKey(key), key.ToString() & " must reach the text box")
                                Next
                                Assert.Equal("", picked)
                                ' An arrow key puts a row in focus; then Enter picks it, and typing gives the ring back.
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.Equal("p1", ChromeMenu.FocusedRowOfLeaf.Id)
                                Assert.False(ChromeMenu.HandlePopupKey(Keys.B))
                                Assert.Null(ChromeMenu.FocusedRowOfLeaf)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Down))
                                Assert.Equal("p2", ChromeMenu.FocusedRowOfLeaf.Id)
                                Assert.True(ChromeMenu.HandlePopupKey(Keys.Enter))
                                Assert.Equal("p2", picked)
                            End Sub)
              End Sub)
    End Sub

    <Fact>
    Public Sub An_accessible_item_speaks_the_localized_default_action_and_the_tooltip_not_the_id()
        OnSta(Sub()
                  Using surface As New ChromeSurface With {.Size = New Size(300, 40)}
                      Dim toggle As New ChromeItem("chkbox_Top_Most", ChromeItemKind.Toggle) With {.AccessibleName = "On top", .Tooltip = "Keep above all windows"}
                      Dim press As New ChromeItem("btn_Prev_File", ChromeItemKind.Button) With {.AccessibleName = "Previous", .Tooltip = "Previous"}
                      surface.SetItems({toggle, press})
                      Assert.Equal("Keep above all windows", surface.AccessibleFor(toggle).Description)
                      Assert.Equal("", surface.AccessibleFor(press).Description)
                      Assert.DoesNotContain("chkbox", surface.AccessibleFor(toggle).Description)
                      Assert.Equal(Localization.T("Переключить"), surface.AccessibleFor(toggle).DefaultAction)
                      Assert.Equal(Localization.T("Нажать"), surface.AccessibleFor(press).DefaultAction)
                  End Using
              End Sub)
    End Sub

End Class
#End If
