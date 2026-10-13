#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Spec 211 section 5 rule 2 and section 7: the wizard's where-text never lies. Every row the
''' "Где это менять" parts name must sit in the settings window's placement catalog exactly where the
''' wizard says, with the caption the window really renders. The catalog is parsed from the source of
''' <c>Table_Form.SettingsGroups.vb</c> - the same declarations the settings window builds from - and
''' the captions from <c>Table_Form.ModernLayout.vb</c>, so a rework that moves or renames a row the
''' wizard points at fails here before the text can mislead a user.
''' </summary>
Public Class WelcomeWhereGateTests

    ' --- the sources --------------------------------------------------------------------------------

    Private Shared Function SourceDir() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing AndAlso Not File.Exists(Path.Combine(dir.FullName, "src", "Table_Form.SettingsGroups.vb"))
            dir = dir.Parent
        End While
        Assert.NotNull(dir)
        Return Path.Combine(dir.FullName, "src")
    End Function

    ''' <summary>The placement catalog as declared: row id -> { page id, group id, group caption key }.</summary>
    Private Shared Function PlacementCatalog() As Dictionary(Of String, String())
        Dim text As String = LocalizationCoverageTests.BlankComments(
            File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.SettingsGroups.vb")))
        Dim catalog As New Dictionary(Of String, String())(StringComparer.Ordinal)
        For Each group As Match In Regex.Matches(text,
                "AddSettingsGroup\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(?:True|False)\s*,([^)]*)\)")
            Dim pageId As String = group.Groups(1).Value
            Dim groupId As String = group.Groups(2).Value
            Dim captionKey As String = group.Groups(3).Value
            For Each row As Match In Regex.Matches(group.Groups(4).Value, """([^""]+)""")
                catalog(row.Groups(1).Value) = New String() {pageId, groupId, captionKey}
            Next
        Next
        Assert.True(catalog.Count >= 40, "Only " & catalog.Count & " placed rows parsed - the catalog scan saw too little.")
        Return catalog
    End Function

    ''' <summary>The caption the settings window renders per row, from its own Select Case.</summary>
    Private Shared Function RowCaptions() As Dictionary(Of String, String)
        Dim text As String = LocalizationCoverageTests.BlankComments(
            File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        Dim body As Match = Regex.Match(text, "Function ModernSettingTitle\b.*?End Function", RegexOptions.Singleline)
        Assert.True(body.Success, "ModernSettingTitle not found in Table_Form.ModernLayout.vb.")
        Dim titles As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each m As Match In Regex.Matches(body.Value,
                "Case\s+""([^""]+)""\s*:\s*Return\s+Localization\.(?:T|TC\s*\(\s*""[^""]*""\s*,)\s*\(?\s*""([^""]*)""")
            titles(m.Groups(1).Value) = m.Groups(2).Value
        Next
        Return titles
    End Function

    ''' <summary>The page captions the settings window's left menu renders.</summary>
    Private Shared Function NavPageCaptions() As HashSet(Of String)
        Dim text As String = LocalizationCoverageTests.BlankComments(
            File.ReadAllText(Path.Combine(SourceDir(), "Table_Form.ModernLayout.vb")))
        ' Two declarations share the name: the designer-era one with literal captions and the localized
        ' one the menu really renders. The gate reads the localized array.
        Dim body As Match = Regex.Match(text, "Dim navLabels As String\(\) = \{(Localization\..*?)\}", RegexOptions.Singleline)
        Assert.True(body.Success, "The localized navLabels not found in Table_Form.ModernLayout.vb.")
        Dim labels As New HashSet(Of String)(StringComparer.Ordinal)
        For Each m As Match In Regex.Matches(body.Groups(1).Value, """([^""]+)""")
            labels.Add(m.Groups(1).Value)
        Next
        Assert.True(labels.Count >= 11, "Only " & labels.Count & " nav labels parsed - the scan saw too little.")
        Return labels
    End Function

    ' --- the gate -----------------------------------------------------------------------------------

    ''' <summary>Spec 211 section 4.1's table: the page each wizard row lives on. A row 210's rework moves
    ''' to another page is named here by this test, exactly as the ticket's ordering note promises.</summary>
    Private Shared Function ExpectedPageIds() As Dictionary(Of String, String)
        Dim pages As New Dictionary(Of String, String)(StringComparer.Ordinal)
        pages("interface_language") = "general"
        pages("background_color") = "images"
        pages("wheel_zooms") = "images"
        pages("advance_after_copy") = "files"
        pages("delete_to_recycle_bin") = "files"
        pages("confirm_delete") = "files"
        pages("image_associations") = "files"
        pages("video_associations") = "files"
        pages("welcome_replay") = "general"
        Return pages
    End Function

    <Fact>
    Public Sub Every_row_the_wizard_names_is_placed_where_the_wizard_says_it_is()
        Dim catalog As Dictionary(Of String, String()) = PlacementCatalog()
        Dim pages As Dictionary(Of String, String) = ExpectedPageIds()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

        For Each wizardStep As WelcomeStep In WelcomeRules.StepsFor(isPackaged:=False)
            For Each rowId As String In WelcomeRules.WhereRowIds(wizardStep)
                seen.Add(rowId)
                Dim placed As String() = Nothing
                Assert.True(catalog.TryGetValue(rowId, placed),
                            wizardStep.ToString() & " names row '" & rowId & "', which the placement catalog does not place.")
                Assert.Equal(pages(rowId), placed(0))
            Next
        Next
        Assert.Equal(pages.Count, seen.Count)

        ' The packaged build omits the associations step, but the rows it names must still exist.
        Assert.True(catalog.ContainsKey("image_associations") AndAlso catalog.ContainsKey("video_associations"))
    End Sub

    <Fact>
    Public Sub The_wizards_group_and_row_captions_are_the_ones_the_window_renders()
        Dim catalog As Dictionary(Of String, String()) = PlacementCatalog()
        Dim captions As Dictionary(Of String, String) = RowCaptions()

        For Each wizardStep As WelcomeStep In WelcomeRules.StepsFor(isPackaged:=False)
            For Each rowId As String In WelcomeRules.WhereRowIds(wizardStep)
                Dim keys As String() = WelcomeRules.WherePathKeys(rowId)
                Assert.Equal(3, keys.Length)
                Assert.True(captions.ContainsKey(rowId), "The settings window has no caption for '" & rowId & "'.")
                Assert.Equal(captions(rowId), keys(2))
                Assert.Equal(catalog(rowId)(2), keys(1))
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_wizards_page_captions_are_pages_the_settings_window_shows()
        Dim labels As HashSet(Of String) = NavPageCaptions()
        Dim catalog As Dictionary(Of String, String()) = PlacementCatalog()
        Dim pageCaptions As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
            {"general", "Общие"}, {"images", "Изображения"}, {"files", "Файлы"}}
        For Each wizardStep As WelcomeStep In WelcomeRules.StepsFor(isPackaged:=False)
            For Each rowId As String In WelcomeRules.WhereRowIds(wizardStep)
                Dim pageKey As String = WelcomeRules.WherePathKeys(rowId)(0)
                Assert.True(labels.Contains(pageKey), "'" & pageKey & "' is not a caption of the settings window's left menu.")
                Assert.Equal(pageCaptions(catalog(rowId)(0)), pageKey)
            Next
        Next
    End Sub

    <Fact>
    Public Sub The_gate_covers_the_row_paths_the_wizard_actually_renders()
        Dim text As String = LocalizationCoverageTests.BlankComments(
            File.ReadAllText(Path.Combine(SourceDir(), "Welcome", "Welcome_Form.vb")))
        Dim rendered As New HashSet(Of String)(StringComparer.Ordinal)
        For Each path As Match In Regex.Matches(text, "WherePath\(""([^""]+)""\)")
            rendered.Add(path.Groups(1).Value)
        Next
        Dim paired As New HashSet(Of String)(StringComparer.Ordinal)
        For Each wizardStep As WelcomeStep In WelcomeRules.StepsFor(isPackaged:=False)
            paired.UnionWith(WelcomeRules.WhereRowIds(wizardStep))
        Next
        Assert.True(rendered.SetEquals(paired), "The rendered where-paths and the gate's row inventory differ.")
    End Sub

End Class

#End If
