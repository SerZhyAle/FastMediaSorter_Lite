#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' The recipients table's stored place on the shared model (L0281 T-6, owner answer Q281-3): the one new preference
''' <c>RecipientsOverlayPlace</c> beside the three 219 keys, through every surface the block rides - the tolerant reader
''' behind <c>ModernViewerPreferences.Normalize</c>, the import path, the layout key, the registry reader and writer, the
''' profile reset and the log package allow-list - and the places that clear it together with the old triple. The reader
''' RULE itself (new key wins, old triple read as <c>TL:x,y</c>, garbled = anchored) is pure and held by
''' <c>RecipientsOverlayPlacementTests</c>. No registry, no screen.
''' </summary>
Public Class RecipientsOverlayPlacePreferencesTests

    Private Const PlaceName As String = "RecipientsOverlayPlace"

    Private Shared Function Source(name As String) As String
        Dim root As New DirectoryInfo(AppContext.BaseDirectory)
        While root IsNot Nothing AndAlso Not File.Exists(Path.Combine(root.FullName, "src", "Main_Form.vb"))
            root = root.Parent
        End While
        Assert.NotNull(root)
        Dim fullPath As String = Path.Combine(root.FullName, "src", name)
        Assert.True(File.Exists(fullPath), "missing source: " & name)
        Return File.ReadAllText(fullPath)
    End Function

    <Fact>
    Public Sub A_fresh_profile_has_no_free_place_for_the_table()
        Assert.Equal(String.Empty, New ModernViewerPreferences().RecipientsOverlayPlace)
    End Sub

    <Fact>
    Public Sub Normalize_reads_a_garbled_or_future_table_place_as_none_and_leaves_the_old_triple_alone()
        Dim garbledValues As String() = {"XX:1,2", "TL:-1,2", Nothing, "TL:1,2,3", "", "TL:1", "TL:1,x", "tl:1,2",
                                         "TL:20001,0", "Z9:1,2;future"}
        For Each garbled As String In garbledValues
            Dim p As New ModernViewerPreferences() With {
                .RecipientsOverlayPlace = garbled, .RecipientsOverlayHasCustomPosition = True,
                .RecipientsOverlayCustomX = 40, .RecipientsOverlayCustomY = 50}
            p.Normalize()
            Assert.Equal(String.Empty, p.RecipientsOverlayPlace)
            ' the triple survives: it is what the reader falls back to
            Assert.True(p.RecipientsOverlayHasCustomPosition)
            Assert.Equal(40, p.RecipientsOverlayCustomX)
            Assert.Equal(50, p.RecipientsOverlayCustomY)
        Next
    End Sub

    <Fact>
    Public Sub Normalize_keeps_a_valid_table_place_in_the_canonical_form_and_leaves_the_panels_alone()
        Dim p As New ModernViewerPreferences() With {.RecipientsOverlayPlace = "BR:007,08", .ChromeRow1Place = "TL:1,2"}
        p.Normalize()
        Assert.Equal("BR:7,8", p.RecipientsOverlayPlace)
        Assert.Equal("TL:1,2", p.ChromeRow1Place)
        Assert.Equal(String.Empty, p.ChromeStatusPlace)
    End Sub

    <Fact>
    Public Sub An_imported_valid_table_place_is_kept_and_a_garbled_one_reads_as_none()
        Dim kept As ModernViewerPreferences = ModernViewerPreferences.FromJson(
            ImportedSettingsLimitsTests.JsonWithString(PlaceName, "BL:24,36"))
        Assert.Equal("BL:24,36", kept.RecipientsOverlayPlace)
        For Each garbled As String In New String() {"XX:1,2", "TL:-1,2", "TL:1,2,3", Nothing}
            Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(
                ImportedSettingsLimitsTests.JsonWithString(PlaceName, garbled))
            Assert.Equal(String.Empty, p.RecipientsOverlayPlace)
        Next
    End Sub

    <Fact>
    Public Sub A_profile_from_an_old_build_without_the_key_leaves_the_table_at_its_corner_or_its_old_point()
        ' No key at all: no place; the old triple (if the profile has one) is then read as TL:x,y by the reader.
        Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson("{""SchemaVersion"":1,""Preferences"":{}}")
        Assert.Equal(String.Empty, p.RecipientsOverlayPlace)
        Assert.False(p.RecipientsOverlayHasCustomPosition)
    End Sub

    <Fact>
    Public Sub Placing_the_table_changes_the_overlay_layout_key_and_leaves_the_chrome_layout_key_alone()
        Dim p As New ModernViewerPreferences()
        Dim overlayBefore As String = p.OverlayLayoutKey()
        Dim chromeBefore As String = p.ChromeLayoutKey()
        p.RecipientsOverlayPlace = "TL:0,0"
        Assert.NotEqual(overlayBefore, p.OverlayLayoutKey())
        Assert.Equal(chromeBefore, p.ChromeLayoutKey())
    End Sub

    <Fact>
    Public Sub The_registry_reader_and_writer_carry_the_table_place_key_through_the_tolerant_reader()
        Dim prefs As String = Source("ModernViewerPreferences.vb")
        Assert.Contains("ReadString(""" & PlaceName & """", prefs)
        Assert.Contains("WriteString(""" & PlaceName & """", prefs)
        Assert.Contains("p.RecipientsOverlayPlace = NormalizePlace(ReadString(""" & PlaceName & """", prefs)
        Assert.Contains("WriteString(""" & PlaceName & """, NormalizePlace(RecipientsOverlayPlace))", prefs)
        ' The three 219 keys are still read and written (an older build reads them; an older profile carries only them).
        For Each old As String In {"RecipientsOverlayHasCustomPosition", "RecipientsOverlayCustomX", "RecipientsOverlayCustomY"}
            Assert.Contains("""" & old & """", prefs)
        Next
    End Sub

    <Fact>
    Public Sub A_profile_reset_and_the_log_package_name_the_table_place()
        Assert.True(ProfileResetScope.IsResetName(PlaceName))
        Assert.Contains(PlaceName, ProfileResetScope.ResetNames)
        Assert.Contains(PlaceName, LogPackage.SafeSettingNames)
    End Sub

    <Fact>
    Public Sub The_corner_pick_and_the_reset_row_clear_the_place_with_the_triple()
        Dim settings As String = Source("Table_Form.ExpandedSettings.vb")
        Assert.Contains("Main_Form.GetModernPreferences().RecipientsOverlayPlace = """"", settings)
        Dim overlay As String = Source("Main_Form.RecipientsOverlay.vb")
        Assert.Contains("prefs.RecipientsOverlayPlace = """"", overlay)
    End Sub

End Class
#End If
