#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports Xunit
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions

Public Class SettingsRevisionAuditTests
    <Fact>
    Public Sub Ordinary_groups_do_not_hide_a_single_row()
        Dim root As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Table_Form.SettingsGroups.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim source = File.ReadAllText(Path.Combine(root.FullName, "src", "Table_Form.SettingsGroups.vb"))
        Dim groups = Regex.Matches(source, "AddSettingsGroup\(""([^""]+)"",\s*""([^""]+)"",\s*""[^""]+"",\s*(?:True|False),([^)]*)\)")
        Assert.True(groups.Count >= 20)
        For Each group As Match In groups
            Dim count = Regex.Matches(group.Groups(3).Value, """[^""]+""").Count
            ' The recipients grid (127) and the resources list panel (131) are surfaces, not
            ' preference rows: their groups wrap one body each, so there is no single row to
            ' hide - the surface must simply be there.
            Dim page = group.Groups(1).Value
            Dim id = group.Groups(2).Value
            If page = "recipients" AndAlso id = "basic" Then
                Assert.Contains("settingsRecipientsGrid", source)
            ElseIf page = "resources" AndAlso id = "basic" Then
                Assert.Contains("settingsResourcesPanel", source)
            Else
                Assert.True(count >= 2, page & "/" & id)
            End If
        Next
    End Sub
    <Fact>
    Public Sub Appearance_edit_preserves_folder_enumeration_and_shuffle_order()
        Dim before As New ModernViewerPreferences With {.IncludeSubfolders = True, .SlideshowRandomOrder = "shuffleCycle"}
        Dim after = before.Snapshot()
        after.ChromeOpacityPercent = 40
        after.NameCollisionPolicy = "rename"
        after.DecodeCacheMaxMb = 100
        Assert.Equal(before.FolderEnumerationKey(), after.FolderEnumerationKey())
        Assert.Equal(before.SlideshowRandomOrder, after.SlideshowRandomOrder)
        Assert.NotEqual(before.ChromeLayoutKey(), after.ChromeLayoutKey())
        after.IncludeSubfolders = False
        Assert.NotEqual(before.FolderEnumerationKey(), after.FolderEnumerationKey())
    End Sub

    <Fact>
    Public Sub Relative_scale_follows_monitor_and_old_absolute_profiles_stay_readable()
        Dim current As New ModernViewerPreferences With {.InterfaceScaleRelativePercent = 125}
        Assert.Equal(1.25F, current.InterfaceFactor(96), 3)
        Assert.Equal(2.1875F, current.InterfaceFactor(168), 3)
        Assert.Equal(2.5F, current.InterfaceFactor(192), 3)
        Dim old = ModernViewerPreferences.FromJson("{""SchemaVersion"":1,""Preferences"":{""InterfaceScalePercent"":125}}")
        Assert.Equal(1.25F, old.InterfaceFactor(168), 3)
        Dim roundTrip = ModernViewerPreferences.FromJson(current.ExportJson())
        Assert.Equal(current.InterfaceFactor(168), roundTrip.InterfaceFactor(168))
        current.InterfaceScaleRelativePercent = 1
        current.Normalize()
        Assert.Equal(90, current.InterfaceScaleRelativePercent)
    End Sub

    <Theory>
    <InlineData(".pdf;.txt", True)>
    <InlineData(".TXT .my-format", True)>
    <InlineData("", True)>
    <InlineData("*.pdf", False)>
    <InlineData("pdf", False)>
    <InlineData(".pdf;C:\media", False)>
    <InlineData(".pdf;.t/t", False)>
    Public Sub Extension_editor_refuses_paths_and_masks(value As String, valid As Boolean)
        Assert.Equal(valid, SettingsSearchRules.IsExtensionList(value))
    End Sub
End Class
#End If
