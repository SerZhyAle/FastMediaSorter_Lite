#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The custom-shortcut rules of SPECIFICATION_SETTINGS_EXPANSION §3.5.
'''
''' What is worth proving here is not "does Ctrl+F5 round-trip" but the two rules that
''' decide whether a remapping is real: a moved action answers to its new combination,
''' and the combination it was moved OFF stops doing the old job. Get the second one
''' wrong and every rebinding silently leaves the factory key working too - which looks
''' like nothing is broken until the user wonders why both keys delete a file.
'''
''' Modern-only, exactly like the feature: CustomHotkeys.vb is whole-file
''' "#If Not NETFRAMEWORK", so on the net48 leg this class compiles to nothing.
''' </summary>
Public Class CustomHotkeysTests

    <Fact>
    Public Sub A_combination_survives_the_round_trip()
        Dim combo As String = CustomHotkeys.Format(Keys.Control Or Keys.Shift Or Keys.F5)
        Assert.Equal("Ctrl+Shift+F5", combo)
        Assert.Equal(Keys.Control Or Keys.Shift Or Keys.F5, CustomHotkeys.Parse(combo))
    End Sub

    <Theory>
    <InlineData("Ctrl+Shift+F5")>
    <InlineData("Num +")>
    <InlineData("Shift+PageDown")>
    <InlineData("Alt+5")>
    <InlineData("Backspace")>
    Public Sub Canonical_is_a_fixed_point(combo As String)
        Assert.Equal(combo, CustomHotkeys.Canonical(combo))
    End Sub

    ''' <summary>Case and spacing are how a hand-edited profile arrives.</summary>
    <Theory>
    <InlineData("ctrl + shift + f5", "Ctrl+Shift+F5")>
    <InlineData("SHIFT+delete", "Shift+Delete")>
    <InlineData("num /", "Num /")>
    Public Sub A_sloppy_spelling_is_still_understood(written As String, expected As String)
        Assert.Equal(expected, CustomHotkeys.Canonical(written))
    End Sub

    <Theory>
    <InlineData("F11")>
    <InlineData("Escape")>
    <InlineData("Alt+F4")>
    <InlineData("Alt+Tab")>
    <InlineData("Ctrl+Alt+Delete")>
    Public Sub A_reserved_combination_cannot_be_assigned(combo As String)
        Assert.True(CustomHotkeys.IsReserved(CustomHotkeys.Parse(combo) Or ParseForReserved(combo)))
        Assert.Equal(Keys.None, CustomHotkeys.Parse(combo))
    End Sub

    ''' <summary>Parse refuses a reserved combination outright, so the reserved check has
    ''' to be fed the raw keys - which is what the capture field does.</summary>
    Private Shared Function ParseForReserved(combo As String) As Keys
        Select Case combo
            Case "F11" : Return Keys.F11
            Case "Escape" : Return Keys.Escape
            Case "Alt+F4" : Return Keys.Alt Or Keys.F4
            Case "Alt+Tab" : Return Keys.Alt Or Keys.Tab
            Case "Ctrl+Alt+Delete" : Return Keys.Control Or Keys.Alt Or Keys.Delete
            Case Else : Return Keys.None
        End Select
    End Function

    <Theory>
    <InlineData("")>
    <InlineData("Ctrl")>
    <InlineData("Ctrl+Shift")>
    <InlineData("Nonsense")>
    <InlineData("Ctrl+A+B")>
    Public Sub Nothing_usable_comes_back_empty(text As String)
        Assert.Equal(String.Empty, CustomHotkeys.Canonical(text))
    End Sub

    <Fact>
    Public Sub An_untouched_profile_produces_an_empty_map()
        Assert.Empty(CustomHotkeys.Load("{}"))
        Assert.Empty(CustomHotkeys.Load(""))
        Assert.Empty(CustomHotkeys.Load("this is not json"))
    End Sub

    ''' <summary>An entry that only restates the default is not an override - keeping it
    ''' would make the viewer swallow a key that never moved.</summary>
    <Fact>
    Public Sub An_override_that_equals_the_default_is_dropped()
        Assert.Empty(CustomHotkeys.Load("{""nextFile"":""Right""}"))
    End Sub

    <Fact>
    Public Sub An_unknown_id_is_dropped_and_an_unusable_combination_stays_disabled()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load(
            "{""noSuchAction"":""Ctrl+Q"",""undo"":""F11"",""help"":""Ctrl+H""}")
        Assert.Equal(2, map.Count)
        Assert.Equal(CustomHotkeys.NoShortcut, map("undo"))
        Assert.Equal("Ctrl+H", map("help"))
    End Sub

    ''' <summary>Two actions cannot hold one combination, and which of them wins must not
    ''' depend on the order a text editor left the file in - so catalogue order decides.</summary>
    <Fact>
    Public Sub A_duplicate_is_resolved_in_catalogue_order()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load(
            "{""undo"":""Ctrl+Q"",""nextFile"":""Ctrl+Q""}")
        Assert.Equal(2, map.Count)
        Assert.True(map.ContainsKey("nextFile"))
        Assert.Equal(CustomHotkeys.NoShortcut, map("undo"))
    End Sub

    <Fact>
    Public Sub Saving_and_loading_preserves_the_overrides()
        Dim map As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"undo", "Ctrl+Z"}, {"help", CustomHotkeys.NoShortcut}}
        Dim reloaded As Dictionary(Of String, String) = CustomHotkeys.Load(CustomHotkeys.Save(map))
        Assert.Equal("Ctrl+Z", reloaded("undo"))
        Assert.Equal(CustomHotkeys.NoShortcut, reloaded("help"))
    End Sub

    <Theory>
    <InlineData("Ctrl+Shift+Hyper+F12")>
    <InlineData("Ctrl+A+B")>
    <InlineData("Win+Space")>
    Public Sub An_unusable_stored_chord_disables_the_action_and_retires_its_default(written As String)
        Dim map = CustomHotkeys.Load(System.Text.Json.JsonSerializer.Serialize(
            New Dictionary(Of String, String) From {{"undo", written}}))
        Assert.Equal(CustomHotkeys.NoShortcut, map("undo"))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "U"))
        Assert.Equal(String.Empty, CustomHotkeys.EffectiveCombo(map, CustomHotkeys.Find("undo")))
        Assert.Equal(CustomHotkeys.NoShortcut, CustomHotkeys.Load(CustomHotkeys.Save(map))("undo"))
    End Sub

    <Fact>
    Public Sub A_legacy_modifier_order_and_numpad_binding_survive_the_next_save()
        Dim map = CustomHotkeys.Load("{""undo"":""Ctrl+Alt+Shift+N"",""help"":""Ctrl+Num +""}")
        Assert.Equal("Ctrl+Shift+Alt+N", map("undo"))
        Assert.Equal("Ctrl+Num +", map("help"))
        Assert.Equal(Keys.Control Or Keys.Shift Or Keys.Alt Or Keys.N, CustomHotkeys.Parse(map("undo")))
        Assert.Equal(Keys.Control Or Keys.Add, CustomHotkeys.Parse(map("help")))
        Dim saved = CustomHotkeys.Load(CustomHotkeys.Save(map))
        Assert.Equal(map("undo"), saved("undo"))
        Assert.Equal(map("help"), saved("help"))
    End Sub

    <Fact>
    Public Sub A_moved_action_answers_to_its_new_combination()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""undo"":""Ctrl+Z""}")
        Assert.Equal("undo", CustomHotkeys.OwnerOfOverride(map, "Ctrl+Z"))
        Assert.Equal("undo", CustomHotkeys.OwnerOfCombo(map, "Ctrl+Z"))
    End Sub

    ''' <summary>The rule the whole feature rests on: the key an action was moved off must
    ''' stop doing it, or the action ends up with two shortcuts.</summary>
    <Fact>
    Public Sub The_abandoned_default_stops_working()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""undo"":""Ctrl+Z""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "U"))
        Assert.Equal(String.Empty, CustomHotkeys.OwnerOfCombo(map, "U"))
    End Sub

    ''' <summary>..but only while it is genuinely free. Once something else moves in, the
    ''' key is that action's and must not be swallowed.</summary>
    <Fact>
    Public Sub A_default_somebody_else_took_over_is_not_retired()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load(
            "{""undo"":""Ctrl+Z"",""help"":""U""}")
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "U"))
        Assert.Equal("help", CustomHotkeys.OwnerOfOverride(map, "U"))
    End Sub

    <Fact>
    Public Sub An_untouched_profile_retires_nothing()
        Dim empty As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Assert.False(CustomHotkeys.IsRetiredDefault(empty, "U"))
        Assert.Equal(String.Empty, CustomHotkeys.OwnerOfOverride(empty, "U"))
    End Sub

    ''' <summary>"No shortcut" is a third state: the action answers to nothing, and its
    ''' factory key is retired rather than left working.</summary>
    <Fact>
    Public Sub An_action_can_be_left_without_a_shortcut()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""undo"":""-""}")
        Assert.Equal(String.Empty, CustomHotkeys.EffectiveCombo(map, CustomHotkeys.Find("undo")))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "U"))
        Assert.Equal(String.Empty, CustomHotkeys.OwnerOfOverride(map, "U"))
    End Sub

    <Fact>
    Public Sub Every_catalogue_entry_has_a_usable_unique_default()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            ' The one action with no default of its own (123 Q123-6): its keys, Menu and Shift+F10, are
            ' reserved OS-standard keys that always work, so the catalogue offers only an EXTRA key for it.
            If action.DefaultCombo.Length = 0 Then
                Assert.Equal("mediaMenu", action.Id)
                Continue For
            End If
            Dim combo As String = CustomHotkeys.Canonical(action.DefaultCombo)
            Assert.False(combo.Length = 0, "Unusable default for " & action.Id & ": " & action.DefaultCombo)
            Assert.True(seen.Add(combo), "Two actions ship with " & combo)
        Next
    End Sub

    <Fact>
    Public Sub The_ten_recipient_slots_keep_their_frozen_digits()
        For slot As Integer = 1 To 10
            Dim action As HotkeyAction = CustomHotkeys.Find(CustomHotkeys.RecipientActionId(slot))
            Assert.NotNull(action)
            Assert.Equal(slot, CustomHotkeys.RecipientSlotOf(action.Id))
            Assert.Equal(If(slot = 10, "0", slot.ToString()), action.DefaultCombo)
        Next
        Assert.Equal(0, CustomHotkeys.RecipientSlotOf("undo"))
        Assert.Equal(0, CustomHotkeys.RecipientSlotOf("recipient11"))
    End Sub

    ' ----------------------------- audit 047, C-19: every factory key retires ----

    ''' <summary>Moving "delete" off Delete retired only Delete: D went on deleting while the
    ''' dialog showed the action rebound.</summary>
    <Fact>
    Public Sub Moving_delete_retires_D_as_well()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""deleteFile"":""Ctrl+Shift+X""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Delete"))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "D"))
        ' Its permanent sibling is untouched.
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Shift+Delete"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Shift+D"))
    End Sub

    <Fact>
    Public Sub Moving_the_permanent_delete_retires_Shift_D_as_well()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""deletePermanent"":""-""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Shift+Delete"))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Shift+D"))
    End Sub

    <Fact>
    Public Sub Moving_a_recipient_slot_retires_its_NumPad_digit()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""recipient5"":""Ctrl+5""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "5"))
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Num 5"))
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "Num 6"))
    End Sub

    <Fact>
    Public Sub Moving_next_file_retires_every_key_that_flipped_forward()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""nextFile"":""Ctrl+N""}")
        For Each combo As String In {"Right", "Space", "PageDown", "BrowserForward"}
            Assert.True(CustomHotkeys.IsRetiredDefault(map, combo), combo)
        Next
    End Sub

    ''' <summary>A key another action was moved ONTO is that action's, never retired.</summary>
    <Fact>
    Public Sub A_factory_extra_taken_over_by_another_action_is_not_retired()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""deleteFile"":""Ctrl+Shift+X"",""help"":""D""}")
        Assert.False(CustomHotkeys.IsRetiredDefault(map, "D"))
        Assert.Equal("help", CustomHotkeys.OwnerOfOverride(map, "D"))
    End Sub

    <Fact>
    Public Sub Every_factory_extra_is_usable_and_belongs_to_one_action()
        Dim seen As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            For Each extra As String In action.ExtraDefaults
                Assert.False(CustomHotkeys.Canonical(extra).Length = 0, "Unusable extra for " & action.Id & ": " & extra)
            Next
            For Each combo As String In CustomHotkeys.FactoryCombos(action)
                Dim owner As String = Nothing
                Assert.False(seen.TryGetValue(combo, owner), combo & " belongs to both " & owner & " and " & action.Id)
                seen(combo) = action.Id
            Next
        Next
    End Sub

    ' ------------------------------------- 057.S05-5: repeat guard by action ----

    ''' <summary>A custom key bound to a destructive action acts once per press, whatever
    ''' key it is - the guard asked about the key code and let F8 = "delete" repeat.</summary>
    <Theory>
    <InlineData("deleteFile")>
    <InlineData("deletePermanent")>
    <InlineData("recipient3")>
    Public Sub A_custom_key_bound_to_a_destructive_action_does_not_repeat(actionId As String)
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""" & actionId & """:""F8""}")
        Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.F8))
        Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.Q))
    End Sub

    ''' <summary>The reverse: "next file" moved onto a digit keeps its auto-repeat.</summary>
    <Fact>
    Public Sub Next_file_rebound_to_a_digit_keeps_repeating()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""nextFile"":""5""}")
        Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.D5))
        ' The factory homes of "next file" are retired and do nothing, so their repeat is harmless.
        Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.Right))
    End Sub

    ''' <summary>Without a custom map the historical key list answers, unchanged.</summary>
    <Fact>
    Public Sub Without_a_custom_map_the_historical_keys_decide()
        For Each map As Dictionary(Of String, String) In {Nothing, CustomHotkeys.Load("{}")}
            Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.D5))
            Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.NumPad0))
            Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.Delete))
            Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.Shift Or Keys.D))
            Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.Right))
            Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.F8))
        Next
    End Sub

    ''' <summary>A destructive action moved away from its factory key: that key is retired
    ''' and does nothing, and the new home is the one that must not repeat.</summary>
    <Fact>
    Public Sub Delete_moved_off_its_factory_key()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""deleteFile"":""Ctrl+Shift+X""}")
        Assert.True(CustomHotkeys.IsDestructivePress(map, Keys.Control Or Keys.Shift Or Keys.X))
        Assert.False(CustomHotkeys.IsDestructivePress(map, Keys.Delete))
    End Sub

    ' --------------------------- ticket 119: the playback actions ----

    <Fact>
    Public Sub The_eleven_playback_actions_exist_and_nothing_else_is_one()
        Dim playback As String() = {
            "speedSlower", "speedFaster", "speedReset",
            "seekBackShort", "seekFwdShort", "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong",
            "frameStep", "abLoop"}
        For Each id As String In playback
            Assert.True(CustomHotkeys.IsPlaybackAction(id), id)
            Assert.NotNull(CustomHotkeys.Find(id))
        Next
        Assert.False(CustomHotkeys.IsPlaybackAction("nextFile"))
        Assert.False(CustomHotkeys.IsPlaybackAction("undo"))
        Assert.False(CustomHotkeys.IsPlaybackAction(""))
        Assert.False(CustomHotkeys.IsPlaybackAction(Nothing))
    End Sub

    <Fact>
    Public Sub Every_playback_factory_key_resolves_to_its_action()
        Assert.Equal("speedSlower", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.OemOpenBrackets))
        Assert.Equal("speedFaster", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.OemCloseBrackets))
        Assert.Equal("speedReset", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.OemPipe))
        Assert.Equal("seekBackShort", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Shift Or Keys.Left))
        Assert.Equal("seekFwdShort", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Shift Or Keys.Right))
        Assert.Equal("seekBackMedium", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Alt Or Keys.Left))
        Assert.Equal("seekFwdMedium", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Alt Or Keys.Right))
        Assert.Equal("seekBackLong", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Control Or Keys.Left))
        Assert.Equal("seekFwdLong", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Control Or Keys.Right))
        Assert.Equal("frameStep", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.OemPeriod))
        Assert.Equal("abLoop", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Shift Or Keys.L))
    End Sub

    <Fact>
    Public Sub A_taken_key_never_resolves_to_a_playback_action()
        ' The three the research found already owned (119 §0 #5) and the plain
        ' navigation keys stay navigation.
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.E))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.L))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Right))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Shift Or Keys.Delete))
        Assert.Equal(String.Empty, CustomHotkeys.PlaybackActionOfFactoryKey(Keys.None))
    End Sub

    <Fact>
    Public Sub Moving_a_playback_action_retires_its_factory_key()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""seekFwdLong"":""F9""}")
        Assert.True(CustomHotkeys.IsRetiredDefault(map, "Ctrl+Right"))
        Assert.Equal("seekFwdLong", CustomHotkeys.OwnerOfOverride(map, "F9"))
        ' The factory key of an UNTOUCHED playback action still resolves - that is the
        ' factory path's job, and it must not be swallowed by somebody else's move.
        Assert.Equal("seekBackLong", CustomHotkeys.PlaybackActionOfFactoryKey(Keys.Control Or Keys.Left))
    End Sub

    <Fact>
    Public Sub A_playback_default_collides_with_no_other_action()
        ' The test that would have caught E/L: every factory combo (defaults and extras,
        ' old and new) belongs to exactly one action - the uniqueness test above covers
        ' the shown defaults; this covers the playback actions' Oem* spellings being
        ' fixed points of Canonical rather than drifting to an alias.
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each id As String In {"speedSlower", "speedFaster", "speedReset", "seekBackShort", "seekFwdShort",
                                  "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong", "frameStep", "abLoop"}
            Dim combo As String = CustomHotkeys.Canonical(CustomHotkeys.Find(id).DefaultCombo)
            Assert.True(combo.Length > 0, id & " has an unusable default")
            Assert.True(seen.Add(combo), id & " ships with " & combo)
        Next
    End Sub

End Class
#End If
