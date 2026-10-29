#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' L0281 T-4, the ways back for a moved floating panel (section 3.7): the handle's menu, the settings button and the
''' drop near the anchor all answer through the Notify line, and a reset clears all five stored places and saves once.
''' The pure half is <c>ModernViewerPreferences.ClearChromePlaces</c>; the rest is wiring in the viewer and the settings
''' window, proven the way <c>RecipientsOverlaySeamTests</c> proves the recipients table's reset - by reading the sources.
''' </summary>
Public Class ChromeReturnPanelsTests

    Private Shared ReadOnly PlaceNames As String() = {
        "ChromeRow1Place", "ChromeRow2Place", "ChromeStatusPlace", "ChromeWindowControlsPlace", "ChromeTransportPlace"}

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

    ''' <summary>The body of the first member whose declaration contains <paramref name="declared"/>.</summary>
    Private Shared Function MemberBody(text As String, declared As String) As String
        Dim start As Integer = text.IndexOf(declared, StringComparison.Ordinal)
        Assert.True(start >= 0, "declaration not found: " & declared)
        Dim endSub As Integer = text.IndexOf(vbCrLf & "    End Sub", start, StringComparison.Ordinal)
        Dim endFunction As Integer = text.IndexOf(vbCrLf & "    End Function", start, StringComparison.Ordinal)
        Dim [end] As Integer = If(endSub >= 0 AndAlso (endFunction < 0 OrElse endSub < endFunction), endSub, endFunction)
        Assert.True([end] > start, "member end not found: " & declared)
        Return text.Substring(start, [end] - start)
    End Function

    Private Shared Function Occurrences(text As String, part As String) As Integer
        Return text.Split(New String() {part}, StringSplitOptions.None).Length - 1
    End Function

    ' ------------------------------------------------------------------------------- the pure half ----

    <Fact>
    Public Sub Clearing_empties_all_five_places_and_reports_whether_any_was_set()
        Dim prefs As New ModernViewerPreferences()
        For Each name As String In PlaceNames
            GetType(ModernViewerPreferences).GetProperty(name).SetValue(prefs, "TL:5,6")
        Next
        Assert.True(prefs.ClearChromePlaces())
        For Each name As String In PlaceNames
            Assert.Equal(String.Empty, DirectCast(GetType(ModernViewerPreferences).GetProperty(name).GetValue(prefs), String))
        Next
        ' Idempotent: the second call finds nothing to clear.
        Assert.False(prefs.ClearChromePlaces())
    End Sub

    <Fact>
    Public Sub Any_single_stored_place_counts_as_something_to_return()
        ' Includes the keys of panels that are not movable yet: a reset is complete whatever a later slice adds.
        For Each name As String In PlaceNames
            Dim prefs As New ModernViewerPreferences()
            Assert.False(prefs.ClearChromePlaces())
            Dim prop As PropertyInfo = GetType(ModernViewerPreferences).GetProperty(name)
            prop.SetValue(prefs, "BR:10,20")
            Assert.True(prefs.ClearChromePlaces(), name)
            Assert.Equal(String.Empty, DirectCast(prop.GetValue(prefs), String))
        Next
    End Sub

    ' ----------------------------------------------------------------------------- the viewer half ----

    <Fact>
    Public Sub The_handle_click_opens_the_menu_and_forgets_the_press()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Assert.Contains("AddHandler panel.Host.Surface.PanelHandleClick, Sub(sender, args) ChromeMoveClick(panel)", chrome)
        Dim click As String = MemberBody(chrome, "Private Sub ChromeMoveClick(p As ChromePanel)")
        Assert.Contains("chrome_Move_Panel = Nothing", click)
        Assert.Contains("ChromeShowHandleMenu(p)", click)
    End Sub

    <Fact>
    Public Sub The_handle_menu_has_two_entries_through_the_chrome_popup_and_never_a_dialog()
        Dim menu As String = MemberBody(Source("Main_Form.Chrome.vb"), "Private Sub ChromeShowHandleMenu(p As ChromePanel)")
        Assert.Contains("Localization.T(""Вернуть панель на место"")", menu)
        Assert.Contains("Localization.T(""Вернуть все панели на места"")", menu)
        ' This panel's entry is unavailable (shown, not hidden - the convention of the other chrome menus) while it is anchored.
        Assert.Contains(".Enabled = ChromePanelIsFree(p)", menu)
        Assert.Contains(".Action = Sub() ChromeResetPlaces(p)", menu)
        Assert.Contains(".Action = Sub() ChromeResetPlaces(Nothing)", menu)
        ' The shared popup: it opens under the handle, above it for a panel that stands low, and is never activated.
        Assert.Contains("ChromeMenu.ShowPopup(Me, location, entries", menu)
        Assert.Contains("anchorTop:=anchorTop", menu)
        Assert.DoesNotContain("ShowDialog", menu)
        Assert.DoesNotContain("ViewerBox", menu)
        Assert.DoesNotContain(".Activate(", menu)
    End Sub

    <Fact>
    Public Sub The_reset_clears_every_key_saves_once_places_afresh_and_answers()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim reset As String = MemberBody(chrome, "Private Function ChromeResetPlaces(onlyPanel As ChromePanel) As Boolean")
        ' All five keys for "all"; the panel's own key for one panel.
        Assert.Contains("prefs.ClearChromePlaces()", reset)
        Assert.Contains("SetChromePanelPlaceText(onlyPanel, """")", reset)
        ' ONE save, whatever changed; the panels are placed afresh (which includes the transport bar) and it answers.
        Assert.Equal(1, Occurrences(reset, "ChromeSavePlaces()"))
        Assert.Contains("PlaceChromePanels()", reset)
        Assert.Contains("ChromeSayPlaces(changed)", reset)
        Assert.Contains("ChromeAbandonMove()", reset)
        Assert.DoesNotContain("ViewerBox", reset)
        ' Reachable from the settings window (the chrome panel type is private, so the public form takes no panel).
        Assert.Contains("Friend Function ResetChromePanelPlaces() As Boolean" & vbCrLf & "        Return ChromeResetPlaces(Nothing)", chrome)
        ' A drop's write and a reset's write are the same save: the pin toggle's discipline.
        Dim write As String = MemberBody(chrome, "Private Sub ChromeWritePlace(p As ChromePanel, text As String)")
        Assert.Contains("ChromeSavePlaces()", write)
        Dim save As String = MemberBody(chrome, "Private Sub ChromeSavePlaces()")
        Assert.Contains("modern_Preferences.Save()", save)
        Assert.Contains("MarkSettingsDirty()", save)
    End Sub

    <Fact>
    Public Sub A_running_move_gesture_ends_before_anything_is_cleared()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim abandon As String = MemberBody(chrome, "Private Sub ChromeAbandonMove()")
        Dim stateCleared As Integer = abandon.IndexOf("chrome_Move_Panel = Nothing", StringComparison.Ordinal)
        Dim modeLeft As Integer = abandon.IndexOf("LeavePanelMoveMode()", StringComparison.Ordinal)
        Assert.True(stateCleared >= 0 AndAlso modeLeft > stateCleared, "the gesture state is cleared first, then the surface leaves its mode")
        Assert.Contains("chrome_Move_Live = Rectangle.Empty", abandon)
        Assert.Contains("Friend Sub LeavePanelMoveMode()", Source("Chrome\ChromeSurface.vb"))
        ' The abandon runs before the places are touched.
        Dim reset As String = MemberBody(chrome, "Private Function ChromeResetPlaces(onlyPanel As ChromePanel) As Boolean")
        Assert.True(reset.IndexOf("ChromeAbandonMove()", StringComparison.Ordinal) < reset.IndexOf("ClearChromePlaces()", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub Every_way_back_answers_with_one_of_two_sentences_through_Notify()
        Dim chrome As String = Source("Main_Form.Chrome.vb")
        Dim say As String = MemberBody(chrome, "Private Sub ChromeSayPlaces(moved As Boolean)")
        Assert.Contains("Localization.T(""Панели возвращены на свои места."")", say)
        Assert.Contains("Localization.T(""Панели уже на своих местах."")", say)
        Assert.Contains("NotifyLevel.Info", say)
        ' The third way: a drop near the anchor answers too, moved or already there.
        Dim drop As String = MemberBody(chrome, "Private Sub ChromeStoreDrop(p As ChromePanel, start As Rectangle, dropped As Rectangle)")
        Assert.Contains("If snapped Then ChromeSayPlaces(False)", drop)
        Assert.Contains("If snapped Then ChromeSayPlaces(True)", drop)
    End Sub

    ' --------------------------------------------------------------------------- the settings half ----

    <Fact>
    Public Sub The_settings_row_sits_among_the_chrome_appearance_rows_and_calls_the_reset()
        Dim settings As String = Source("Table_Form.ExpandedSettings.vb")
        Dim corners As Integer = settings.IndexOf("RegisterSettingsRow(""chrome_corners"",", StringComparison.Ordinal)
        Dim reset As Integer = settings.IndexOf("RegisterSettingsRow(""chrome_reset_places"",", StringComparison.Ordinal)
        Assert.True(corners >= 0 AndAlso reset > corners, "the row follows the chrome appearance rows")
        Assert.Contains("AddHandler resetPlaces.Click, Sub() Main_Form.ResetChromePanelPlaces()", settings)
        Assert.Contains("AddSettingRow(viewing, ""chrome_reset_places"", resetPlaces, 230, True)", settings)
        Assert.Contains("resetPlaces.Text = Localization.T(""Вернуть панели на места"")", settings)
        ' One command and no copy of the preferences: the row writes through the live object the other rows write into.
        Dim row As String = settings.Substring(reset, settings.IndexOf("RegisterSettingsRow(""slideshow_random_order""", reset, StringComparison.Ordinal) - reset)
        Assert.DoesNotContain("Snapshot", row)
        Assert.DoesNotContain("CopyFrom", row)
        Assert.DoesNotContain("ViewerBox", row)
    End Sub

    <Fact>
    Public Sub The_settings_row_is_in_the_interface_group_with_its_words_and_glyph()
        Dim groups As String = Source("Table_Form.SettingsGroups.vb")
        Dim start As Integer = groups.IndexOf("AddSettingsGroup(""general"", ""interface""", StringComparison.Ordinal)
        Dim [end] As Integer = groups.IndexOf("AddSettingsGroup(""general"", ""startup""", start, StringComparison.Ordinal)
        Assert.True(start >= 0 AndAlso [end] > start)
        Dim interfaceGroup As String = groups.Substring(start, [end] - start)
        Assert.Contains("""chrome_opacity""", interfaceGroup)
        Assert.Contains("""chrome_reset_places""", interfaceGroup)
        Dim layout As String = Source("Table_Form.ModernLayout.vb")
        Assert.Contains("Case ""chrome_reset_places"" : Return Localization.T(""Вернуть панели на места"")", layout)
        Assert.Contains("""recipients_position_reset"", ""chrome_reset_places"", ""html_reset_appearance""", layout)
        Assert.Contains("Case ""chrome_reset_places"" : Return Localization.T(""Возвращает перемещённые панели", layout)
    End Sub

    ' -------------------------------------------------------------------------------- the strings ----

    <Fact>
    Public Sub Each_new_string_has_all_thirteen_columns()
        Dim table As String = Source("Localization\Localization.Chrome.vb")
        Dim keys As String() = {
            "Вернуть панель на место", "Вернуть все панели на места", "Вернуть панели на места",
            "Возвращает перемещённые панели на места по умолчанию", "Панели возвращены на свои места.", "Панели уже на своих местах."}
        For Each key As String In keys
            Dim line As String = table.Split({vbCrLf}, StringSplitOptions.None).
                Single(Function(candidate) candidate.TrimStart().StartsWith("Add(""" & key, StringComparison.Ordinal))
            Dim literals As MatchCollection = Regex.Matches(line, """((?:[^""]|"""")*)""")
            Assert.Equal(13, literals.Count)
            For Each literal As Match In literals
                Assert.False(String.IsNullOrWhiteSpace(literal.Groups(1).Value), key)
            Next
            ' No smart double quote in any column.
            Assert.DoesNotContain(ChrW(&H201C).ToString(), line)
            Assert.DoesNotContain(ChrW(&H201D).ToString(), line)
        Next
    End Sub

End Class
#End If
