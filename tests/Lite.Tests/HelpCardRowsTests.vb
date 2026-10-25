#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports Xunit

''' <summary>
''' The row list of the F1 reference card (UX-115 and UX-116 of ticket 123): every shortcut action is
''' documented, the keys come from the LIVE map so a rebinding shows, the wrong "P/N (PgDn/PgUp)" reading
''' is gone, and every word the card shows is a registered localization key.
''' </summary>
Public Class HelpCardRowsTests

    Private Shared Function RowFor(groups As List(Of HelpCardGroup), actionId As String) As HelpCardRow
        For Each group As HelpCardGroup In groups
            For Each row As HelpCardRow In group.Rows
                If String.Equals(row.ActionId, actionId, StringComparison.Ordinal) Then Return row
            Next
        Next
        Return Nothing
    End Function

    Private Shared Function RowByDescription(groups As List(Of HelpCardGroup), description As String) As HelpCardRow
        For Each group As HelpCardGroup In groups
            For Each row As HelpCardRow In group.Rows
                If String.Equals(row.Description, description, StringComparison.Ordinal) Then Return row
            Next
        Next
        Return Nothing
    End Function

    ' --- coverage ------------------------------------------------------------------------

    ''' <summary>The test that stops a key from shipping undocumented: every catalogue action except the
    ''' ten move slots and the ten copy slots (two rows between them) is on the card exactly once.</summary>
    <Fact>
    Public Sub Every_catalogue_action_is_on_the_card_exactly_once()
        Dim onCard As List(Of String) = HelpCardRows.ActionIdsOnTheCard(Nothing)
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            If CustomHotkeys.RecipientSlotOf(action.Id) > 0 OrElse CustomHotkeys.CopySlotOf(action.Id) > 0 Then Continue For
            Assert.True(onCard.Where(Function(id) id = action.Id).Count() = 1, action.Id & " must appear on the card exactly once")
        Next
        ' ... and the card names nothing the catalogue does not know.
        For Each id As String In onCard
            Assert.NotNull(CustomHotkeys.Find(id))
        Next
    End Sub

    <Fact>
    Public Sub The_groups_come_in_the_documented_order()
        Dim titles As List(Of String) = HelpCardRows.Build(Nothing).Select(Function(g) g.Title).ToList()
        Assert.Equal(New List(Of String) From {"Навигация", "Файлы", "Вид", "Видео и аудио", "Мышь", "Панели и окна"}, titles)
    End Sub

    <Fact>
    Public Sub Every_row_has_a_description_and_the_mouse_chords_are_there()
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(Nothing)
        For Each group As HelpCardGroup In groups
            Assert.NotEmpty(group.Rows)
            For Each row As HelpCardRow In group.Rows
                Assert.False(String.IsNullOrWhiteSpace(row.Description), group.Title & ": a row without a description")
            Next
        Next
        Dim mouse As HelpCardGroup = groups.Single(Function(g) g.Title = "Мышь")
        Assert.True(mouse.Rows.Count >= 10)
        Assert.All(mouse.Rows, Sub(row) Assert.True(row.KeysAreWords))
    End Sub

    ' --- the corrected readings ----------------------------------------------------------

    <Fact>
    Public Sub Forward_is_Space_and_PgDn_back_is_P_and_PgUp_and_N_is_go_to_number()
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(Nothing)
        Dim forward As String = RowFor(groups, "nextFile").Keys
        Dim back As String = RowFor(groups, "prevFile").Keys
        Assert.Contains("Space", forward)
        Assert.Contains("PgDn", forward)
        ' P was never "forward" (the old line read "P/N (PgDn/PgUp)"): it is on the BACK row only.
        Assert.DoesNotContain("P", forward.Split({", "}, StringSplitOptions.None))
        Assert.Contains("P", back.Split({", "}, StringSplitOptions.None))
        Assert.Contains("PgUp", back)
        Assert.Equal("N", RowFor(groups, "jumpToNumber").Keys)
    End Sub

    <Fact>
    Public Sub Shift_D_is_documented_beside_Shift_Delete()
        Dim keys As String = RowFor(HelpCardRows.Build(Nothing), "deletePermanent").Keys
        Assert.Contains("Shift+Delete", keys)
        Assert.Contains("Shift+D", keys)
    End Sub

    <Fact>
    Public Sub The_system_media_keys_and_browser_keys_are_not_listed()
        For Each group As HelpCardGroup In HelpCardRows.Build(Nothing)
            For Each row As HelpCardRow In group.Rows
                Assert.DoesNotContain("Browser", row.Keys)
                Assert.DoesNotContain("MediaPlayPause", row.Keys)
                Assert.DoesNotContain("Oem", row.Keys)
            Next
        Next
    End Sub

    <Theory>
    <InlineData("speedSlower", "[")>
    <InlineData("speedFaster", "]")>
    <InlineData("speedReset", "\")>
    <InlineData("frameStep", ".")>
    <InlineData("frameBack", ",")>
    <InlineData("playPause", "K")>
    <InlineData("volumeUp", "Ctrl+Up")>
    Public Sub Punctuation_keys_are_spelled_as_printed_on_the_keycap(actionId As String, expected As String)
        Assert.Equal(expected, RowFor(HelpCardRows.Build(Nothing), actionId).Keys)
    End Sub

    ' --- the live map ----------------------------------------------------------------------

    <Fact>
    Public Sub A_rebinding_shows_on_the_card_and_the_old_keys_do_not()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""nextFile"":""F8""}")
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(map)
        Assert.Equal("F8", RowFor(groups, "nextFile").Keys)
        ' The sibling action is untouched.
        Assert.Contains("PgUp", RowFor(groups, "prevFile").Keys)
    End Sub

    <Fact>
    Public Sub A_cleared_shortcut_shows_an_empty_key_column()
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""undo"":""-""}")
        Assert.Equal(String.Empty, RowFor(HelpCardRows.Build(map), "undo").Keys)
    End Sub

    <Fact>
    Public Sub A_factory_key_another_action_was_moved_onto_leaves_the_old_row()
        ' "help" now answers to D: the delete row must not still claim it.
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""help"":""D""}")
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(map)
        Assert.Equal("D", RowFor(groups, "help").Keys)
        Assert.Equal("Delete", RowFor(groups, "deleteFile").Keys)
    End Sub

    <Fact>
    Public Sub The_slot_rows_say_one_to_zero_until_a_slot_is_rebound()
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(Nothing)
        Assert.Equal("1 - 0", RowByDescription(groups, "Переместить в папку-получатель").Keys)
        Assert.Equal("Shift+1 - Shift+0", RowByDescription(groups, "Копировать в папку-получатель").Keys)

        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""recipient5"":""F5"",""copySlot2"":""F6""}")
        Dim rebound As List(Of HelpCardGroup) = HelpCardRows.Build(map)
        Dim move As String = RowByDescription(rebound, "Переместить в папку-получатель").Keys
        Dim copy As String = RowByDescription(rebound, "Копировать в папку-получатель").Keys
        Assert.Contains("F5", move)
        Assert.DoesNotContain(" - ", move)
        Assert.Contains("F6", copy)
        Assert.Contains("Shift+3", copy)
    End Sub

    <Fact>
    Public Sub The_media_menu_row_always_lists_the_two_standard_keys_and_adds_the_users_own()
        Assert.Equal("Menu, Shift+F10", RowFor(HelpCardRows.Build(Nothing), "mediaMenu").Keys)
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""mediaMenu"":""Ctrl+M""}")
        Assert.Equal("Menu, Shift+F10, Ctrl+M", RowFor(HelpCardRows.Build(map), "mediaMenu").Keys)
    End Sub

    ' --- UX-113: the main row, UX-112: Enter ----------------------------------------------------

    <Fact>
    Public Sub Zoom_lists_the_main_row_as_printed_on_the_keycaps()
        Dim groups As List(Of HelpCardGroup) = HelpCardRows.Build(Nothing)
        Assert.Equal("Num +, =, +", RowFor(groups, "zoomIn").Keys)
        Assert.Equal("Num -, -", RowFor(groups, "zoomOut").Keys)
        Assert.Equal("Num /", RowFor(groups, "zoomFit").Keys)
        Assert.Equal("+", HelpCardRows.DisplayKey("Shift+Oemplus"))
        Assert.Equal("=", HelpCardRows.DisplayKey("Oemplus"))
        Assert.Equal("Ctrl+-", HelpCardRows.DisplayKey("Ctrl+OemMinus"))
    End Sub

    <Fact>
    Public Sub Enter_is_on_the_card_as_the_open_action_and_follows_a_rebinding()
        Assert.Equal("Enter", RowFor(HelpCardRows.Build(Nothing), "openContent").Keys)
        Dim map As Dictionary(Of String, String) = CustomHotkeys.Load("{""openContent"":""F8""}")
        Assert.Equal("F8", RowFor(HelpCardRows.Build(map), "openContent").Keys)
    End Sub

    ' --- UX-114: what a key works on ---------------------------------------------------------

    <Theory>
    <InlineData("playPause", "VideoAudio")>
    <InlineData("volumeUp", "VideoAudio")>
    <InlineData("seekFwdLong", "VideoAudio")>
    <InlineData("audioTrack", "VideoAudio")>
    <InlineData("frameStep", "Video")>
    <InlineData("frameBack", "Video")>
    <InlineData("subtitleTrack", "Video")>
    <InlineData("rotateCw", "Picture")>
    <InlineData("ocrTranslate", "Picture")>
    <InlineData("zoomActual", "Picture")>
    <InlineData("zoomIn", "PictureAndText")>
    <InlineData("zoomFit", "PictureAndText")>
    <InlineData("nextFile", "Everywhere")>
    <InlineData("deleteFile", "Everywhere")>
    <InlineData("openContent", "Everywhere")>
    Public Sub A_row_carries_the_scope_of_its_action(actionId As String, scopeName As String)
        ' By name: the scope enum is internal, and a public test signature may not expose it.
        Dim expected As HelpKeyScope = DirectCast([Enum].Parse(GetType(HelpKeyScope), scopeName), HelpKeyScope)
        Assert.Equal(expected, HelpCardRows.ScopeOf(actionId))
        Assert.Equal(expected, RowFor(HelpCardRows.Build(Nothing), actionId).Scope)
    End Sub

    ''' <summary>A playback or zoom key added to the catalogue later cannot ship unmarked: the card would go
    ''' back to saying nothing about which media the key works on.</summary>
    <Fact>
    Public Sub Every_playback_and_zoom_action_is_marked()
        For Each action As HotkeyAction In CustomHotkeys.Catalog
            If CustomHotkeys.IsPlaybackAction(action.Id) OrElse CustomHotkeys.IsZoomAction(action.Id) _
               OrElse action.Id = "audioTrack" OrElse action.Id = "subtitleTrack" OrElse action.Id = "frameStep" Then
                Assert.NotEqual(HelpKeyScope.Everywhere, HelpCardRows.ScopeOf(action.Id))
            End If
        Next
    End Sub

    <Fact>
    Public Sub Only_the_unmarked_scope_has_no_mark_text()
        Assert.Equal(String.Empty, HelpCardRows.ScopeMark(HelpKeyScope.Everywhere))
        For Each scope As HelpKeyScope In {HelpKeyScope.VideoAudio, HelpKeyScope.Video, HelpKeyScope.Picture, HelpKeyScope.PictureAndText}
            Assert.False(String.IsNullOrWhiteSpace(HelpCardRows.ScopeMark(scope)))
        Next
    End Sub

    ' --- text -------------------------------------------------------------------------------

    <Theory>
    <InlineData("help", "5", "Переместить в папку-получатель", "5")>
    <InlineData("help", "Shift+5", "Копировать в папку-получатель", "Shift+5")>
    Public Sub A_digit_taken_by_another_action_is_not_advertised_as_a_slot(actionId As String, combo As String, description As String, retired As String)
        Dim map As New Dictionary(Of String, String) From {{actionId, combo}}
        Dim keys As String = RowByDescription(HelpCardRows.Build(map), description).Keys
        Assert.DoesNotContain(" - ", keys)
        Assert.DoesNotContain(retired, keys.Split({", "}, StringSplitOptions.None))
    End Sub

    <Theory>
    <InlineData("OemOpenBrackets", "[")>
    <InlineData("Shift+OemCloseBrackets", "Shift+]")>
    <InlineData("PageDown", "PgDn")>
    <InlineData("Shift+PageUp", "Shift+PgUp")>
    <InlineData("Ctrl+Home", "Ctrl+Home")>
    <InlineData("", "")>
    Public Sub Key_names_are_spelled_for_reading(combo As String, expected As String)
        Assert.Equal(expected, HelpCardRows.DisplayKey(combo))
    End Sub

    ''' <summary>Everything the card says goes through the localization layer: a Russian key with no entry
    ''' would reach all twelve other languages untranslated.</summary>
    <Fact>
    Public Sub Every_word_on_the_card_is_a_registered_localization_key()
        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) kv.Key), StringComparer.Ordinal)
        Dim missing As New List(Of String)()
        Dim keys As New List(Of String) From {"Клавиши и мышь", "Esc или щелчок - закрыть. Клавиши можно изменить в настройках.", "Не назначено"}
        For Each group As HelpCardGroup In HelpCardRows.Build(Nothing)
            keys.Add(group.Title)
            For Each row As HelpCardRow In group.Rows
                keys.Add(row.Description)
                If row.KeysAreWords Then keys.Add(row.Keys)
                If row.Scope <> HelpKeyScope.Everywhere Then keys.Add(HelpCardRows.ScopeMark(row.Scope))
            Next
        Next
        For Each key As String In keys.Distinct()
            If Not known.Contains(key) Then missing.Add(key)
        Next
        Assert.True(missing.Count = 0, "Not registered: " & String.Join(" | ", missing))
    End Sub

    ''' <summary>The first-run line that said "P/N (PgDn/PgUp)" is gone from the sources (UX-115).</summary>
    <Fact>
    Public Sub The_misleading_P_N_line_is_gone_from_the_first_run_text()
        Dim known As New HashSet(Of String)(Localization.All.Select(Function(kv) kv.Key), StringComparer.Ordinal)
        Assert.DoesNotContain(known, Function(key) key.Contains("P/N (PgDn/PgUp)"))
    End Sub

End Class
#End If
