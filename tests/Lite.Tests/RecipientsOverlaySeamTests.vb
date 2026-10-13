Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The seams of the recipients overlay's free position
''' (219_SPECIFICATION_RECIPIENTS_OVERLAY_DRAG_BY_HEADER.md): the drag by the header, the
''' position model's persistence, the edit door's open-at seam, the reset row, the recovery
''' clamp and the net48 boundary are UI glue around rules the owner stated as a hard shape.
''' The geometry itself is pure and lives in RecipientsOverlayPlacementTests; what is proven
''' HERE is that the rules are wired where the specification says - by reading the sources,
''' the way the dialog-layer tests hold their hooks.
''' </summary>
Public Class RecipientsOverlaySeamTests

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        ' One line-ending convention for every assertion, whatever the file on disk uses.
        Return File.ReadAllText(fullPath).Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf)
    End Function

    ''' <summary>The body of the first member whose declaration contains <paramref name="declared"/>,
    ''' from the declaration to the line before its closing End Sub / End Function.</summary>
    Private Shared Function MemberBody(text As String, declared As String) As String
        Dim start As Integer = text.IndexOf(declared, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declared)
        Dim endSub As Integer = text.IndexOf(vbCrLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFunction As Integer = text.IndexOf(vbCrLf & "    End Function", start, StringComparison.Ordinal)
        Dim [end] As Integer = If(endSub >= 0 AndAlso (endFunction < 0 OrElse endSub < endFunction), endSub, endFunction)
        Assert.True([end] > start, "member end not found: " & declared)
        Return text.Substring(start, [end] - start)
    End Function

    ' ------------------------------------------------------------------ deliverable 2: the drag ----

    <Fact>
    Public Sub The_grip_is_the_header_strip_and_its_press_runs_the_native_move_loop()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        ' The strip is the grip between its two buttons, with the move cursor; a press on it
        ' drags the window through Windows' own loop, exactly like the window-commands grip.
        Assert.Contains(".Name = ""recipients_Overlay_Strip""", overlay)
        Assert.Contains(".Cursor = Cursors.SizeAll", overlay)
        Assert.Contains("If e.Button = MouseButtons.Left Then RecipientsOverlayBeginDrag()", overlay)
        Assert.Contains("SendMessage(recipients_Overlay.Handle, WmNcLButtonDown, New IntPtr(HtCaption), lParam)", overlay)
        ' The rows never drag: the wiring exists only on the strip, never on the rows host.
        Dim build As String = MemberBody(overlay, "Private Sub BuildRecipientsOverlay()")
        Assert.Contains("AddHandler strip.MouseDown", build)
        Assert.DoesNotContain("AddHandler rowsPanel.MouseDown", build)
        ' The buttons keep their own cursor and their own click - the press is not the grip's.
        Assert.Contains(".Cursor = Cursors.Default", build)
    End Sub

    <Fact>
    Public Sub A_drag_writes_the_custom_point_once_on_drop_and_never_per_pixel()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        ' Saved on drop, not per pixel - and a drag that ends where it began writes nothing.
        Dim drag As String = MemberBody(overlay, "Private Sub RecipientsOverlayBeginDrag()")
        Assert.Contains("If recipients_Overlay.Location <> before Then SaveRecipientsOverlayDraggedPosition()", drag)
        ' The point is stored through the logical-px seam (Q219-5), not as raw screen coordinates.
        Dim save As String = MemberBody(overlay, "Private Sub SaveRecipientsOverlayDraggedPosition()")
        Assert.Contains("RecipientsOverlayPlacement.ToLogicalPoint(", save)
        Assert.Contains("prefs.RecipientsOverlayHasCustomPosition = True", save)
        Assert.Contains("RecipientsOverlayPlacement.ToDeviceOffset(",
                        MemberBody(overlay, "Private Function RecipientsOverlayPlacedRect()"))
    End Sub

    <Fact>
    Public Sub The_position_rides_the_persistence_surfaces_of_its_block()
        ' Registry, export/import (the whole preferences object serializes), profile reset
        ' and the log package - the same four surfaces every preference of the block answers to.
        Dim prefs As String = Source("ModernViewerPreferences.vb")
        Assert.Contains("Public Property RecipientsOverlayHasCustomPosition As Boolean", prefs)
        Assert.Contains("Public Property RecipientsOverlayCustomX As Integer", prefs)
        Assert.Contains("Public Property RecipientsOverlayCustomY As Integer", prefs)
        Assert.Contains("WriteBool(""RecipientsOverlayHasCustomPosition"", RecipientsOverlayHasCustomPosition)", prefs)
        Assert.Contains("WriteString(""RecipientsOverlayCustomX""", prefs)
        ' The overlay's layout key - the change detector that rebuilds the panel - sees the
        ' custom point, so a corner pick, a reset or an import re-places it at once.
        Assert.Contains("RecipientsOverlayHasCustomPosition, RecipientsOverlayCustomX, RecipientsOverlayCustomY})", prefs)
        Assert.Contains("""RecipientsOverlayHasCustomPosition""", Source("ProfileReset\ProfileResetScope.vb"))
        Assert.Contains("""RecipientsOverlayCustomY""", Source("Support\LogPackage.vb"))
    End Sub

    ' ------------------------------------------------------- deliverable 4: the settings, no digits ----

    <Fact>
    Public Sub The_reset_row_is_the_only_new_row_of_the_group_and_carries_no_digits()
        Dim groups As String = Source("Table_Form.SettingsGroups.vb")
        ' Acceptance 3: the "Вид таблицы получателей" group carries the corner choice, the
        ' reset button and nothing new besides them.
        Assert.Contains("""recipients_position"", ""recipients_width"", ""recipients_font"", ""recipients_opacity"", ""recipients_rows""," & vbCrLf &
                        "                         ""recipients_position_reset"")", groups)
        Dim settings As String = Source("Table_Form.ExpandedSettings.vb")
        ' No position digits of any kind, ever (the hard shape of 219 §5).
        Assert.DoesNotContain("AddPreferenceNumber(destinations, ""recipients_custom", settings)
        Assert.DoesNotContain("AddPreferenceNumber(destinations, ""recipients_x", settings)
        Assert.DoesNotContain("AddPreferenceNumber(destinations, ""recipients_y", settings)
        Assert.DoesNotContain("AddPreferenceSlider(destinations, ""recipients_position", settings)
        ' One command, no confirmation dialog.
        Assert.Contains("AddHandler resetPosition.Click, Sub() Main_Form.ResetRecipientsOverlayPosition()", settings)
    End Sub

    <Fact>
    Public Sub The_reset_is_immediate_idempotent_and_says_what_it_did()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Dim reset As String = MemberBody(overlay, "Friend Sub ResetRecipientsOverlayPosition()")
        Assert.Contains("prefs.RecipientsOverlayHasCustomPosition = False", reset)
        Assert.Contains("ApplyRecipientsOverlay()", reset)
        Assert.Contains("NotifyLevel.Info", reset)
        ' The no-op is honest: both answers exist, each its own words.
        Assert.Contains("Позиция панели получателей сброшена.", reset)
        Assert.Contains("Панель получателей уже у своего угла.", reset)
    End Sub

    <Fact>
    Public Sub A_corner_pick_clears_the_custom_point_and_the_hint_names_the_drag()
        Dim settings As String = Source("Table_Form.ExpandedSettings.vb")
        Assert.Contains("Main_Form.GetModernPreferences().RecipientsOverlayHasCustomPosition = False", settings)
        Assert.Contains("""Угол области просмотра; панель можно перетащить за заголовок.""",
                        Source("Table_Form.ModernLayout.vb"))
    End Sub

    ' --------------------------------------------------------- deliverable 3: the edit door ----

    <Fact>
    Public Sub The_edit_button_is_a_door_not_a_move()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Assert.Contains(".Name = ""recipients_Overlay_Edit""", overlay)
        Assert.Contains("AddHandler edit.Click, Sub() RecipientsOverlayEditRequested()", overlay)
        ' The click opens the settings window and does nothing else: no pin, no position
        ' write, no visibility change (219 §4).
        Dim door As String = MemberBody(overlay, "Private Sub RecipientsOverlayEditRequested()")
        Assert.Contains("ShowSettingsWindowAtRecipientsGrid()", door)
        Assert.DoesNotContain("ChromeToggleRecipientsPin", door)
        Assert.DoesNotContain("SaveRecipientsOverlay", door)
        Assert.DoesNotContain("PositionRecipientsOverlay", door)
        Assert.Contains("Friend Sub ShowSettingsWindowAtRecipientsGrid()", Source("Main_Form.KeyboardInput.vb"))
    End Sub

    <Fact>
    Public Sub The_open_at_entry_rides_the_reveal_discipline_and_is_consumed_once()
        Dim groups As String = Source("Table_Form.SettingsGroups.vb")
        ' The open-at behaves exactly like RevealModernSetting: page, expanded group, anchor
        ' and focus state - overriding the remembered context for this one open.
        Dim reveal As String = MemberBody(groups, "Friend Sub RevealModernRecipientsGrid()")
        Assert.Contains("Tab_Control.SelectedTab = pageIds.First(Function(p) p.Value = ""recipients"").Key", reveal)
        Assert.Contains("settingsGroups(""recipients/basic"").Expanded = True", reveal)
        Assert.Contains("state.AnchorId = ""settingsRecipientsGrid""", reveal)
        Assert.Contains("state.FocusId = ""settingsRecipientsGrid""", reveal)
        Assert.Contains("contextFlush.MarkDirty()", reveal)
        ' The grid is an anchor of its page and takes real focus in the restore path.
        Assert.Contains("result.Add(""settingsRecipientsGrid"", Data_Grid_View)", groups)
        Assert.Contains("OrElse TypeOf focused Is DataGridView Then", groups)
        ' The pending request is consumed by the first Shown - a plain open never sees it.
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Assert.Contains("pendingOpenAtRecipientsGrid = False", layout)
        Assert.Contains("RevealModernRecipientsGrid()", layout)
    End Sub

    ' ------------------------------------------------ deliverables 5, 6: the recovery clamp ----

    <Fact>
    Public Sub Every_placement_validates_and_the_repaired_point_is_what_persists()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Dim placed As String = MemberBody(overlay, "Private Function RecipientsOverlayPlacedRect()")
        Assert.Contains("If Not RecipientsOverlayPlacement.HeaderInside(location, recipients_Overlay.Size,", placed)
        Assert.Contains("RecipientsOverlayPlacement.PullInside(location, recipients_Overlay.Size,", placed)
        ' The repair rewrites the saved custom point at the moment it fires, silently.
        Assert.Contains("PersistRecipientsOverlayRepairedPosition(onScreen)", overlay)
        Dim repair As String = MemberBody(overlay, "Private Sub PersistRecipientsOverlayRepairedPosition(applied As Point)")
        Assert.Contains("SaveRecipientsOverlayPreferences()", repair)
        Assert.DoesNotContain("ViewerBox", repair)
        ' Silent recovery: no dialog, no toast, nothing beyond the ordinary trace.
        Assert.DoesNotContain("Notify", repair)
    End Sub

    <Fact>
    Public Sub The_hot_zone_grows_from_the_panels_own_rect_not_the_configured_corner()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Dim zone As String = MemberBody(overlay, "Private Function RecipientsOverlayHotZone()")
        Assert.Contains("RecipientsOverlayPlacement.HotZone(placed,", zone)
        Assert.DoesNotContain("RecipientsOverlayPosition()", zone)
    End Sub

    ' --------------------------------------------------- deliverable 7: the net48 boundary ----

    <Fact>
    Public Sub The_net48_placement_keeps_the_fixed_corner_and_knows_nothing_of_the_free_position()
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        ' The placement function's net48 branch is the legacy Select Case alone: no custom
        ' point, no clamp, no repair - the x86 overlay stays always-on at a fixed corner.
        Dim placed As String = MemberBody(overlay, "Private Function RecipientsOverlayPlacedRect()")
        Dim seamStart As Integer = placed.IndexOf("#Else", StringComparison.Ordinal)
        Dim seamEnd As Integer = placed.IndexOf("#End If", seamStart, StringComparison.Ordinal)
        Assert.True(seamStart >= 0 AndAlso seamEnd > seamStart, "the placement seam was not found")
        Dim net48Branch As String = placed.Substring(seamStart, seamEnd - seamStart)
        Assert.Contains("Select Case RecipientsOverlayPosition()", net48Branch)
        Assert.DoesNotContain("RecipientsOverlayPlacement", net48Branch)
        Assert.DoesNotContain("RecipientsOverlayHasCustomPosition", net48Branch)
        ' And the fixed top-left overlay stays the x86 answer.
        Dim net48 As String = Source("Main_Form.ModernPreferences.vb")
        Assert.Contains("Private Function RecipientsOverlayPosition() As String" & vbCrLf &
                        "        Return ""topLeft""", net48)
    End Sub

End Class
