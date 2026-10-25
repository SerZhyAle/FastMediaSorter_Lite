#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Text.Json
Imports Xunit

Public Class ManagerSettingsTests

    <Fact>
    Public Sub Preferences_DefaultValues_MatchSpecification()
        Dim prefs As New ModernViewerPreferences()
        Assert.Equal("grid", prefs.FileManagerViewMode)
        Assert.Equal("media", prefs.FileManagerContentMode)
        Assert.False(prefs.FileManagerShowHiddenFiles)
        Assert.Equal("default", prefs.FileManagerNonMediaOpenAction)
        Assert.True(prefs.FileManagerShowLocalPlaces)
        Assert.True(prefs.FileManagerShowResourcePlaces)
        Assert.True(prefs.FileManagerShowDevicePlaces)
        Assert.True(prefs.FileManagerRememberSort)
        Assert.Equal("{}", prefs.FileManagerPlaceSorts)

        Dim col As PanelSortColumn = PanelSortColumn.None
        Dim desc As Boolean = True
        Assert.False(prefs.GetPlaceSort("drive:C:\", col, desc))
    End Sub

    <Fact>
    Public Sub Preferences_Normalization_RejectsInvalidValues()
        Dim prefs As New ModernViewerPreferences()
        prefs.FileManagerViewMode = "invalid_mode"
        prefs.FileManagerContentMode = "invalid_content"
        prefs.FileManagerNonMediaOpenAction = "invalid_action"
        prefs.Normalize()

        Assert.Equal("grid", prefs.FileManagerViewMode)
        Assert.Equal("media", prefs.FileManagerContentMode)
        Assert.Equal("default", prefs.FileManagerNonMediaOpenAction)
    End Sub

    <Fact>
    Public Sub Preferences_PlaceSort_RoundTrip_And_LruLimit()
        Dim prefs As New ModernViewerPreferences()
        prefs.SetPlaceSort("place:1", PanelSortColumn.Size, True)
        prefs.SetPlaceSort("place:2", PanelSortColumn.Modified, False)

        Dim col As PanelSortColumn
        Dim desc As Boolean
        Assert.True(prefs.GetPlaceSort("place:1", col, desc))
        Assert.Equal(PanelSortColumn.Size, col)
        Assert.True(desc)

        Assert.True(prefs.GetPlaceSort("place:2", col, desc))
        Assert.Equal(PanelSortColumn.Modified, col)
        Assert.False(desc)

        ' Unknown or malformed place sorts rejected
        prefs.FileManagerPlaceSorts = "{""bad:place"":{""Column"":""NotAColumn"",""Descending"":true}}"
        Assert.False(prefs.GetPlaceSort("bad:place", col, desc))

        ' LRU eviction at 100 entries
        For i As Integer = 1 To 110
            prefs.SetPlaceSort("entry:" & i.ToString(), PanelSortColumn.Name, False)
        Next
        ' The earliest entries (1..10) must be evicted
        Assert.False(prefs.GetPlaceSort("entry:1", col, desc))
        Assert.False(prefs.GetPlaceSort("entry:5", col, desc))
        Assert.False(prefs.GetPlaceSort("entry:10", col, desc))
        ' Entry 110 must exist
        Assert.True(prefs.GetPlaceSort("entry:110", col, desc))
    End Sub

    <Fact>
    Public Sub Preferences_ExportJson_PrivacyEnforcement()
        Dim prefs As New ModernViewerPreferences()
        prefs.FileManagerViewMode = "list"
        prefs.FileManagerContentMode = "documents"
        prefs.FileManagerShowHiddenFiles = True
        prefs.SetPlaceSort("private:server/folder", PanelSortColumn.Size, True)

        Dim publicExport As String = prefs.ExportJson(includePersonalData:=False)
        Assert.Contains("""FileManagerViewMode"": ""list""", publicExport)
        Assert.Contains("""FileManagerContentMode"": ""documents""", publicExport)
        Assert.Contains("""FileManagerShowHiddenFiles"": true", publicExport)
        ' Place sorts and private path must be omitted when includePersonalData:=False
        Assert.DoesNotContain("private:server/folder", publicExport)
        Assert.Contains("""FileManagerPlaceSorts"": ""{}""", publicExport)

        Dim personalExport As String = prefs.ExportJson(includePersonalData:=True)
        Assert.Contains("private:server/folder", personalExport)
    End Sub

    <Fact>
    Public Sub Diagnostics_AllowList_ContainsOnlySafeScalarPreferences()
        Dim allowed As HashSet(Of String) = LogPackage.SafeSettingNames
        Assert.Contains("FileManagerViewMode", allowed)
        Assert.Contains("FileManagerContentMode", allowed)
        Assert.Contains("FileManagerShowHiddenFiles", allowed)
        Assert.Contains("FileManagerNonMediaOpenAction", allowed)
        Assert.Contains("FileManagerShowLocalPlaces", allowed)
        Assert.Contains("FileManagerShowResourcePlaces", allowed)
        Assert.Contains("FileManagerShowDevicePlaces", allowed)
        Assert.Contains("FileManagerRememberSort", allowed)
        ' Sensitive per-place dictionary is omitted
        Assert.DoesNotContain("FileManagerPlaceSorts", allowed)
    End Sub

    <Fact>
    Public Sub ProfileResetScope_IncludesAllFileManagerPreferences()
        For Each key As String In {
            "FileManagerViewMode", "FileManagerContentMode", "FileManagerShowHiddenFiles",
            "FileManagerNonMediaOpenAction", "FileManagerShowLocalPlaces", "FileManagerShowResourcePlaces",
            "FileManagerShowDevicePlaces", "FileManagerRememberSort", "FileManagerPlaceSorts"}
            Assert.True(ProfileResetScope.IsResetName(key), "Key '" & key & "' must be in ProfileResetScope.ResetNames")
        Next
    End Sub

    <Fact>
    Public Sub ResourceDescriptor_PresentationFields_DefaultsAndCarryOver()
        Dim res As New ResourceDescriptor()
        Assert.Null(res.AllFiles)
        Assert.Null(res.ShowHiddenFiles)

        res.AllFiles = True
        res.ShowHiddenFiles = False

        Dim target As New ResourceDescriptor()
        ResourceOrigin.CarryOver(res, target)

        Assert.True(target.AllFiles.HasValue AndAlso target.AllFiles.Value)
        Assert.True(target.ShowHiddenFiles.HasValue AndAlso Not target.ShowHiddenFiles.Value)
    End Sub

End Class
#End If
