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
        Assert.Equal(String.Empty, CustomHotkeys.Canonical(combo))
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
    Public Sub An_unknown_id_and_an_unusable_combination_are_dropped()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load(
            "{""noSuchAction"":""Ctrl+Q"",""undo"":""F11"",""help"":""Ctrl+H""}")
        Assert.Equal(1, map.Count)
        Assert.Equal("Ctrl+H", map("help"))
    End Sub

    ''' <summary>Two actions cannot hold one combination, and which of them wins must not
    ''' depend on the order a text editor left the file in - so catalogue order decides.</summary>
    <Fact>
    Public Sub A_duplicate_is_resolved_in_catalogue_order()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load(
            "{""undo"":""Ctrl+Q"",""nextFile"":""Ctrl+Q""}")
        Assert.Equal(1, map.Count)
        Assert.True(map.ContainsKey("nextFile"))
    End Sub

    <Fact>
    Public Sub Saving_and_loading_preserves_the_overrides()
        Dim map As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"undo", "Ctrl+Z"}, {"help", CustomHotkeys.NoShortcut}}
        Dim reloaded As Dictionary(Of String, String) = CustomHotkeys.Load(CustomHotkeys.Save(map))
        Assert.Equal("Ctrl+Z", reloaded("undo"))
        Assert.Equal(CustomHotkeys.NoShortcut, reloaded("help"))
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

End Class
#End If
