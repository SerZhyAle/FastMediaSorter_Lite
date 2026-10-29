#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Reflection
Imports Xunit

''' <summary>
''' The five stored places of the movable chrome panels (L0281 §5, T-2): the tolerant reader behind
''' <c>ModernViewerPreferences.Normalize</c> (garbled, future and null values read as anchored, an accepted value is
''' re-encoded canonically), the import path, the layout key, and the two lists a new setting must be named on -
''' the profile reset and the log package allow-list. No registry, no screen.
''' </summary>
Public Class FloatingPanelPlacePreferencesTests

    Private Shared ReadOnly PlaceNames As String() = {
        "ChromeRow1Place", "ChromeRow2Place", "ChromeStatusPlace", "ChromeWindowControlsPlace", "ChromeTransportPlace"}

    Private Shared Function Prop(name As String) As PropertyInfo
        Dim found As PropertyInfo = GetType(ModernViewerPreferences).GetProperty(name)
        Assert.NotNull(found)
        Return found
    End Function

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
    Public Sub A_fresh_profile_has_every_panel_anchored()
        Dim p As New ModernViewerPreferences()
        For Each name As String In PlaceNames
            Assert.Equal(String.Empty, DirectCast(Prop(name).GetValue(p), String))
        Next
    End Sub

    <Fact>
    Public Sub Normalize_reads_a_garbled_or_future_place_as_anchored_for_every_panel()
        ' Nothing is a null read from a hand-edited export; the rest are shapes the encoder never writes.
        Dim garbledValues As String() = {"XX:1,2", "TL:-1,2", Nothing, "TL:1,2,3", "", "TL:1", "TL:1,x", "tl:1,2",
                                         "TL:20001,0", "Z9:1,2;future"}
        For Each name As String In PlaceNames
            For Each garbled As String In garbledValues
                Dim p As New ModernViewerPreferences()
                Prop(name).SetValue(p, garbled)
                p.Normalize()
                Assert.Equal(String.Empty, DirectCast(Prop(name).GetValue(p), String))
            Next
        Next
    End Sub

    <Fact>
    Public Sub Normalize_keeps_a_valid_place_and_leaves_the_other_panels_alone()
        For Each name As String In PlaceNames
            Dim p As New ModernViewerPreferences()
            Prop(name).SetValue(p, "BR:24,36")
            p.Normalize()
            For Each other As String In PlaceNames
                Dim expected As String = If(other = name, "BR:24,36", String.Empty)
                Assert.Equal(expected, DirectCast(Prop(other).GetValue(p), String))
            Next
        Next
    End Sub

    <Fact>
    Public Sub Normalize_writes_an_accepted_place_back_in_the_canonical_form()
        Dim p As New ModernViewerPreferences() With {.ChromeRow1Place = "TL:007,08", .ChromeStatusPlace = "TR:7,300"}
        p.Normalize()
        Assert.Equal("TL:7,8", p.ChromeRow1Place)
        Assert.Equal("TR:7,300", p.ChromeStatusPlace)
    End Sub

    <Fact>
    Public Sub An_imported_garbled_place_reads_as_anchored()
        For Each name As String In PlaceNames
            For Each garbled As String In New String() {"XX:1,2", "TL:-1,2", "TL:1,2,3", Nothing}
                Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(
                    ImportedSettingsLimitsTests.JsonWithString(name, garbled))
                Assert.Equal(String.Empty, DirectCast(Prop(name).GetValue(p), String))
            Next
        Next
    End Sub

    <Fact>
    Public Sub An_imported_valid_place_is_kept()
        For Each name As String In PlaceNames
            Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson(
                ImportedSettingsLimitsTests.JsonWithString(name, "BL:24,36"))
            Assert.Equal("BL:24,36", DirectCast(Prop(name).GetValue(p), String))
        Next
    End Sub

    <Fact>
    Public Sub A_profile_from_an_old_build_without_the_keys_leaves_every_panel_anchored()
        Dim p As ModernViewerPreferences = ModernViewerPreferences.FromJson("{""SchemaVersion"":1,""Preferences"":{}}")
        For Each name As String In PlaceNames
            Assert.Equal(String.Empty, DirectCast(Prop(name).GetValue(p), String))
        Next
    End Sub

    <Fact>
    Public Sub Moving_a_panel_changes_the_chrome_layout_key()
        For Each name As String In PlaceNames
            Dim p As New ModernViewerPreferences()
            Dim before As String = p.ChromeLayoutKey()
            Prop(name).SetValue(p, "TL:0,0")
            Assert.NotEqual(before, p.ChromeLayoutKey())
        Next
    End Sub

    <Fact>
    Public Sub The_registry_reader_and_writer_carry_every_place_key()
        Dim prefs As String = Source("ModernViewerPreferences.vb")
        For Each name As String In PlaceNames
            Assert.Contains("ReadString(""" & name & """", prefs)
            Assert.Contains("WriteString(""" & name & """", prefs)
        Next
    End Sub

    <Fact>
    Public Sub A_profile_reset_returns_every_panel()
        For Each name As String In PlaceNames
            Assert.True(ProfileResetScope.IsResetName(name), name)
            Assert.Contains(name, ProfileResetScope.ResetNames)
        Next
    End Sub

    <Fact>
    Public Sub The_log_package_allow_list_names_every_place_key()
        For Each name As String In PlaceNames
            Assert.Contains(name, LogPackage.SafeSettingNames)
        Next
    End Sub

End Class
#End If
