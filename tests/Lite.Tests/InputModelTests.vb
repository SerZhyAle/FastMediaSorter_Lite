#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Drawing
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 123 Ф-3 and Ф-4: the video basics as rebindable catalogue actions, the track and copy-slot
''' actions (UX-102, UX-112), the volume arithmetic, and the reserved-key list (UX-111). Pure rules
''' and the catalogue - no form is built.
''' </summary>
Public Class InputModelTests

    <Theory>
    <InlineData(&HE0000L, True)>
    <InlineData(&H800E0000L, True)>
    <InlineData(&H100E0008L, True)>
    <InlineData(&HD0000L, False)>
    <InlineData(&H90000L, False)>
    Public Sub Hardware_pause_command_ignores_device_and_modifier_bits(payload As Long, expected As Boolean)
        Assert.Equal(expected, PlaybackRules.IsMediaPlayPauseCommand(payload))
    End Sub

    <Fact>
    Public Sub Inapplicable_video_basics_are_consumed_before_the_slideshow_stop()
        For Each fileName As String In {"Main_Form.CustomHotkeys.vb", "Main_Form.PlaybackControls.vb"}
            Assert.Contains("If Not PlaybackActionApplicable(actionId) Then Return CustomHotkeys.IsVideoBasicsAction(actionId)", SourceOf(fileName))
        Next
    End Sub

    <Fact>
    Public Sub Help_scroll_keys_precede_rebindings_and_help_Escape_precedes_panel_mode()
        Dim source As String = SourceOf("Main_Form.KeyboardInput.vb")
        Dim commandAt As Integer = source.IndexOf("Protected Overrides Function ProcessCmdKey", StringComparison.Ordinal)
        Dim cardAt As Integer = source.IndexOf("help_Card.TryScrollKey(keyData)", commandAt, StringComparison.Ordinal)
        Dim chromeAt As Integer = source.IndexOf("ChromeHandleKey(keyData)", commandAt, StringComparison.Ordinal)
        Assert.True(cardAt > commandAt AndAlso chromeAt > cardAt)
        Dim body As String = KeybUseBody()
        Assert.True(body.IndexOf("help_Card.TryScrollKey(e.KeyCode)", StringComparison.Ordinal) <
                    body.IndexOf("TryHandleCustomHotkey(", StringComparison.Ordinal))
    End Sub

    ' --- the volume keys -----------------------------------------------------------------

    <Fact>
    Public Sub Volume_up_steps_by_five_percent_and_stops_at_full()
        Dim state As PlaybackRules.VolumeState = PlaybackRules.NextVolume(0.5R, False, True)
        Assert.Equal(0.55R, state.Level, 6)
        Assert.False(state.Muted)
        Assert.Equal(1.0R, PlaybackRules.NextVolume(1.0R, False, True).Level, 6)
        Assert.Equal(1.0R, PlaybackRules.NextVolume(0.98R, False, True).Level, 6)
    End Sub

    <Fact>
    Public Sub Volume_down_steps_by_five_percent_and_stops_at_silence()
        Assert.Equal(0.45R, PlaybackRules.NextVolume(0.5R, False, False).Level, 6)
        Assert.Equal(0.0R, PlaybackRules.NextVolume(0.0R, False, False).Level, 6)
        Assert.Equal(0.0R, PlaybackRules.NextVolume(0.03R, False, False).Level, 6)
    End Sub

    <Fact>
    Public Sub Volume_up_while_muted_unmutes_and_volume_down_never_touches_the_mute_flag()
        Assert.False(PlaybackRules.NextVolume(0.5R, True, True).Muted)
        Assert.True(PlaybackRules.NextVolume(0.5R, True, False).Muted)
        Assert.False(PlaybackRules.NextVolume(0.5R, False, False).Muted)
    End Sub

    <Fact>
    Public Sub Twenty_presses_walk_the_whole_range_without_drifting_off_the_step()
        Dim level As Double = 0.0R
        For press As Integer = 1 To 20
            level = PlaybackRules.NextVolume(level, False, True).Level
        Next
        Assert.Equal(1.0R, level, 9)
        For press As Integer = 1 To 20
            level = PlaybackRules.NextVolume(level, False, False).Level
        Next
        Assert.Equal(0.0R, level, 9)
    End Sub

    <Fact>
    Public Sub A_garbage_level_is_clamped_instead_of_thrown()
        Assert.InRange(PlaybackRules.NextVolume(Double.NaN, False, True).Level, 0.0R, 1.0R)
        Assert.InRange(PlaybackRules.NextVolume(7.0R, False, False).Level, 0.0R, 1.0R)
        Assert.InRange(PlaybackRules.NextVolume(-3.0R, False, True).Level, 0.0R, 1.0R)
    End Sub

    ' --- the video basics ----------------------------------------------------------------

    <Theory>
    <InlineData("playPause", "K")>
    <InlineData("mute", "M")>
    <InlineData("volumeUp", "Ctrl+Up")>
    <InlineData("volumeDown", "Ctrl+Down")>
    Public Sub The_video_basics_ship_with_the_defaults_of_Q123_3(id As String, combo As String)
        Dim action As HotkeyAction = CustomHotkeys.Find(id)
        Assert.NotNull(action)
        Assert.Equal(combo, CustomHotkeys.Canonical(action.DefaultCombo))
        Assert.True(CustomHotkeys.IsVideoBasicsAction(id))
        Assert.True(CustomHotkeys.IsPlaybackAction(id), "they ride the playback dispatch: no slideshow stop, same gate")
    End Sub

    <Fact>
    Public Sub The_basics_are_not_confused_with_anything_else()
        Assert.False(CustomHotkeys.IsVideoBasicsAction("nextFile"))
        Assert.False(CustomHotkeys.IsVideoBasicsAction("speedFaster"))
        Assert.False(CustomHotkeys.IsVideoBasicsAction(""))
        Assert.False(CustomHotkeys.IsVideoBasicsAction(Nothing))
    End Sub

    <Fact>
    Public Sub Every_basic_factory_key_resolves_to_its_action_and_the_system_media_key_too()
        Assert.Equal("playPause", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.K))
        Assert.Equal("mute", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.M))
        Assert.Equal("volumeUp", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Control Or Keys.Up))
        Assert.Equal("volumeDown", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Control Or Keys.Down))
        Assert.Equal("playPause", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.MediaPlayPause))
    End Sub

    <Fact>
    Public Sub The_bare_arrows_and_Space_are_never_taken_by_the_basics()
        ' Space is the sorter's "next file"; the plain arrows flip and jump files.
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Space))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Up))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Down))
    End Sub

    <Fact>
    Public Sub Moving_a_basic_retires_its_factory_key_including_the_media_key()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""playPause"":""F8""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "K"))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "MediaPlayPause"))
        Assert.Equal("playPause", CustomHotkeys.OwnerOfOverride(map, "F8"))
        ' The sibling basics are untouched.
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "M"))
    End Sub

    ''' <summary>Q123-3: every default must be FREE. The two letters are not claimed by KeybUse, and the
    ''' two chords are Ctrl chords the historical map throws away - this fails if somebody later gives
    ''' K or M a meaning in the main map without moving the default.</summary>
    <Fact>
    Public Sub The_default_letters_are_free_in_the_historical_map()
        Dim body As String = KeybUseBody()
        For Each letter As String In {"K", "M"}
            Assert.DoesNotContain("Keys." & letter & vbCr, body)
            Assert.DoesNotContain("Keys." & letter & vbLf, body)
            Assert.DoesNotContain("Keys." & letter & ",", body)
            Assert.DoesNotContain("Keys." & letter & " ", body)
        Next
        ' Ctrl+Up / Ctrl+Down: the main map rejects every Ctrl chord (MainMapAccepts) ...
        Assert.False(KeyRouting.MainMapAccepts(Keys.Control Or Keys.Up))
        Assert.False(KeyRouting.MainMapAccepts(Keys.Control Or Keys.Down))
        ' ... and neither is reserved, so the dialog accepts a rebinding onto them.
        Assert.False(CustomHotkeys.IsReserved(Keys.Control Or Keys.Up))
    End Sub

    <Fact>
    Public Sub Every_new_default_is_unique_across_the_whole_catalogue()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            For Each combo As String In CustomHotkeys.FactoryCombos(action)
                Assert.True(seen.Add(combo), combo & " is claimed twice (" & action.Id & ")")
            Next
        Next
    End Sub

    ' --- the track keys ------------------------------------------------------------------

    <Fact>
    Public Sub The_track_keys_are_catalogue_actions_but_not_playback_actions()
        Assert.Equal("A", CustomHotkeys.Canonical(CustomHotkeys.Find("audioTrack").DefaultCombo))
        Assert.Equal("V", CustomHotkeys.Canonical(CustomHotkeys.Find("subtitleTrack").DefaultCombo))
        ' They keep the legacy slideshow behaviour (the historical A / V stop it), so they are not extras.
        Assert.False(CustomHotkeys.IsPlaybackAction("audioTrack"))
        Assert.False(CustomHotkeys.IsPlaybackAction("subtitleTrack"))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.A))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.V))
    End Sub

    <Fact>
    Public Sub Moving_the_audio_track_action_retires_the_letter_A()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""audioTrack"":""F9""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "A"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "V"))
    End Sub

    ' --- copy to a slot ------------------------------------------------------------------

    <Fact>
    Public Sub Shift_plus_a_top_row_digit_is_a_catalogue_action_per_slot()
        For slot As Integer = 1 To 10
            Dim id As String = CustomHotkeys.CopyActionId(slot)
            Dim action As HotkeyAction = CustomHotkeys.Find(id)
            Assert.NotNull(action)
            Assert.Equal(slot, CustomHotkeys.CopySlotOf(id))
            Assert.Equal("Shift+" & CustomHotkeys.SlotDigit(slot), CustomHotkeys.Canonical(action.DefaultCombo))
            Assert.Equal(0, CustomHotkeys.RecipientSlotOf(id))
            Assert.True(CustomHotkeys.IsDestructiveAction(id), "a copy acts once per press, like a move")
        Next
        Assert.Equal(0, CustomHotkeys.CopySlotOf("copySlot0"))
        Assert.Equal(0, CustomHotkeys.CopySlotOf("copySlot11"))
        Assert.Equal(0, CustomHotkeys.CopySlotOf("copySlotX"))
        Assert.Equal(0, CustomHotkeys.CopySlotOf("recipient3"))
        Assert.Equal(0, CustomHotkeys.CopySlotOf(Nothing))
    End Sub

    <Fact>
    Public Sub Moving_a_copy_slot_retires_its_Shift_digit_and_leaves_the_move_alone()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""copySlot3"":""F8""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Shift+3"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "3"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Shift+4"))
        ' The repeat guard follows the action: the new home of a copy must not auto-repeat.
        Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.F8))
    End Sub

    <Fact>
    Public Sub Moving_a_recipient_slot_does_not_move_its_copy_key()
        ' The move and the copy are two actions now: the user decides about each (UX-112).
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""recipient3"":""F8""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "3"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Shift+3"))
    End Sub

    ' --- Ctrl+Home / Ctrl+End join the catalogue -----------------------------------------

    <Fact>
    Public Sub Ctrl_Home_and_Ctrl_End_are_factory_keys_of_first_and_last_file()
        Assert.Contains("Ctrl+Home", CustomHotkeys.FactoryCombos(CustomHotkeys.Find("firstFile")))
        Assert.Contains("Ctrl+End", CustomHotkeys.FactoryCombos(CustomHotkeys.Find("lastFile")))
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""firstFile"":""F8""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Ctrl+Home"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Ctrl+End"))
    End Sub

    ' --- UX-111: F10 is the chrome's ----------------------------------------------------

    <Fact>
    Public Sub F10_and_the_menu_keys_are_reserved_so_the_dialog_refuses_them()
        Assert.True(CustomHotkeys.IsReserved(Keys.F10))
        Assert.True(CustomHotkeys.IsReserved(Keys.Shift Or Keys.F10))
        Assert.True(CustomHotkeys.IsReserved(Keys.Apps))
        Assert.Equal(String.Empty, CustomHotkeys.Format(Keys.F10))
        Assert.Equal(Keys.None, CustomHotkeys.Parse("F10"))
        ' A hand-edited profile that binds F10 is cleaned up, not honoured as a dead binding.
        Assert.Empty(CustomHotkeys.Load("{""undo"":""F10""}"))
    End Sub

    ' --- UX-104: the keyboard door to the media menu -----------------------------------------

    <Theory>
    <InlineData(CInt(Keys.Apps), True)>
    <InlineData(CInt(Keys.Shift Or Keys.F10), True)>
    <InlineData(CInt(Keys.F10), False)>
    <InlineData(CInt(Keys.Control Or Keys.F10), False)>
    <InlineData(CInt(Keys.Shift Or Keys.Apps), False)>
    <InlineData(CInt(Keys.M), False)>
    Public Sub Only_the_Menu_key_and_Shift_F10_open_the_media_menu(keyData As Integer, expected As Boolean)
        Assert.Equal(expected, CustomHotkeys.IsMediaMenuKey(CType(keyData, Keys)))
    End Sub

    <Fact>
    Public Sub The_media_menu_action_adds_a_key_and_ships_unbound()
        Dim action As HotkeyAction = CustomHotkeys.Find("mediaMenu")
        Assert.NotNull(action)
        Assert.Equal(String.Empty, action.DefaultCombo)
        Assert.Empty(CustomHotkeys.FactoryCombos(action))
        ' The user can give it a key of their own ...
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""mediaMenu"":""Ctrl+M""}")
        Assert.Equal("mediaMenu", CustomHotkeys.OwnerOfOverride(map, "Ctrl+M"))
        ' ... and the two reserved keys can never be bound to anything else.
        Assert.Equal(Keys.None, CustomHotkeys.Parse("Apps"))
        Assert.Empty(CustomHotkeys.Load("{""mediaMenu"":""Apps""}"))
    End Sub

    ' --- UX-106: F7 then F11 ---------------------------------------------------------------

    Private Shared Function Modes(isFull As Boolean, isSuper As Boolean, fromFull As Boolean) As FullScreenRules.Modes
        Return New FullScreenRules.Modes With {.IsFull = isFull, .IsSuper = isSuper, .SuperEnteredFromFull = fromFull}
    End Function

    <Fact>
    Public Sub F11_from_the_window_enters_the_panel_less_screen_and_F11_again_leaves_it()
        Dim entered As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(Modes(False, False, False))
        Assert.True(entered.IsFull)
        Assert.True(entered.IsSuper)
        Assert.False(entered.SuperEnteredFromFull)
        Dim left As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(entered)
        Assert.False(left.IsFull)
        Assert.False(left.IsSuper)
    End Sub

    <Fact>
    Public Sub F7_then_F11_enters_the_panel_less_screen_instead_of_leaving_full_screen()
        ' The defect: both flags were inverted, giving (not full, super), which the layout then cleared.
        Dim entered As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(Modes(True, False, False))
        Assert.True(entered.IsFull)
        Assert.True(entered.IsSuper)
        Assert.True(entered.SuperEnteredFromFull)
    End Sub

    <Fact>
    Public Sub F11_again_after_F7_F11_returns_to_the_plain_full_screen()
        Dim entered As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(Modes(True, False, False))
        Dim back As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(entered)
        Assert.True(back.IsFull)
        Assert.False(back.IsSuper)
        Assert.False(back.SuperEnteredFromFull)
        ' And one more F11 starts the cycle again from the plain full screen.
        Assert.True(FullScreenRules.AfterSuperToggle(back).IsSuper)
    End Sub

    <Fact>
    Public Sub The_panel_less_screen_is_never_reported_without_full_screen()
        For Each isFull As Boolean In {False, True}
            For Each isSuper As Boolean In {False, True}
                For Each fromFull As Boolean In {False, True}
                    Dim after As FullScreenRules.Modes = FullScreenRules.AfterSuperToggle(Modes(isFull, isSuper, fromFull))
                    If after.IsSuper Then Assert.True(after.IsFull)
                Next
            Next
        Next
    End Sub

    ' --- UX-107 / UX-108: the cursor-hide timer and the pointer over chrome -------------------

    <Fact>
    Public Sub An_open_popup_or_any_window_under_the_pointer_keeps_the_cursor()
        Dim panels As Rectangle() = {New Rectangle(10, 10, 300, 40), New Rectangle(10, 500, 300, 30)}
        Assert.True(PlaybackRules.PointerIsOverChromeOrMenu(New Point(20, 20), False, panels))
        Assert.True(PlaybackRules.PointerIsOverChromeOrMenu(New Point(20, 510), False, panels))
        Assert.False(PlaybackRules.PointerIsOverChromeOrMenu(New Point(400, 300), False, panels))
        ' An open popup counts wherever the pointer is: the menu must not lose its pointer.
        Assert.True(PlaybackRules.PointerIsOverChromeOrMenu(New Point(400, 300), True, panels))
        Assert.False(PlaybackRules.PointerIsOverChromeOrMenu(New Point(0, 0), False, Nothing))
        ' An empty rectangle (a panel that was never placed) never "contains" the pointer.
        Assert.False(PlaybackRules.PointerIsOverChromeOrMenu(New Point(0, 0), False, {Rectangle.Empty}))
    End Sub

    <Fact>
    Public Sub Every_way_into_full_screen_starts_the_cursor_timer_through_SetViewSizes()
        Dim source As String = SourceOf("Main_Form.UILayout.vb")
        Dim start As Integer = source.IndexOf("Private Sub SetViewSizes()", StringComparison.Ordinal)
        Assert.True(start >= 0)
        Dim body As String = source.Substring(start, source.IndexOf("End Sub", start, StringComparison.Ordinal) - start)
        Assert.Contains("UpdateCursorHideTimer()", body)
        ' The two hotkeys no longer carry a call of their own: one place, no way to forget it.
        Dim keyboard As String = SourceOf("Main_Form.KeyboardInput.vb")
        Dim toggleStart As Integer = keyboard.IndexOf("Friend Sub ToggleFullScreenMode()", StringComparison.Ordinal)
        Dim toggle As String = keyboard.Substring(toggleStart, keyboard.IndexOf("End Sub", toggleStart, StringComparison.Ordinal) - toggleStart)
        Assert.DoesNotContain(vbLf & "        UpdateCursorHideTimer()", toggle)
    End Sub

    ''' <summary>UX-108: the chrome's own tick feeds the stillness clock, or a move over the toolbar never
    ''' reached the media surfaces that used to be its only listeners.</summary>
    <Fact>
    Public Sub The_chrome_tick_feeds_the_cursor_stillness_clock()
        Dim chrome As String = SourceOf("Main_Form.Chrome.vb")
        Assert.Contains("If moved Then NotePointerMovedOnScreen()", chrome)
        Dim playback As String = SourceOf("Main_Form.PlaybackControls.vb")
        Assert.Contains("ChromeMenu.PopupVisible", playback)
    End Sub

    ' --- helpers -------------------------------------------------------------------------

    Private Shared Function SourceOf(fileName As String) As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function KeybUseBody() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src", "Main_Form.KeyboardInput.vb")
            If File.Exists(candidate) Then
                Dim source As String = File.ReadAllText(candidate)
                Dim start As Integer = source.IndexOf("Public Sub KeybUse", StringComparison.Ordinal)
                Dim finish As Integer = source.IndexOf("End Sub", start, StringComparison.Ordinal)
                Return source.Substring(start, finish - start)
            End If
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/Main_Form.KeyboardInput.vb not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
