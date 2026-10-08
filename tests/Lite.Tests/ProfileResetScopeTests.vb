#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Specification 169 §4, §8: Tests for ProfileResetScope table and the §4.4 classification gate.
''' </summary>
Public Class ProfileResetScopeTests

    <Fact>
    Public Sub No_name_is_in_more_than_one_scope_list()
        Dim resetSet As New HashSet(Of String)(ProfileResetScope.ResetNames, StringComparer.OrdinalIgnoreCase)
        Dim keptSet As New HashSet(Of String)(ProfileResetScope.KeptNames, StringComparer.OrdinalIgnoreCase)
        Dim transientSet As New HashSet(Of String)(ProfileResetScope.TransientNames, StringComparer.OrdinalIgnoreCase)

        For Each name In resetSet
            Assert.False(keptSet.Contains(name), "Key '" & name & "' is in both Reset and Kept lists.")
            Assert.False(transientSet.Contains(name), "Key '" & name & "' is in both Reset and Transient lists.")
        Next

        For Each name In keptSet
            Assert.False(transientSet.Contains(name), "Key '" & name & "' is in both Kept and Transient lists.")
        Next
    End Sub

    <Fact>
    Public Sub No_reset_or_transient_name_has_share_prefix()
        For Each name In ProfileResetScope.ResetNames
            Assert.False(name.StartsWith("Share_", StringComparison.OrdinalIgnoreCase),
                         "Reset list contains a Share_ key: " & name)
        Next
        For Each name In ProfileResetScope.TransientNames
            Assert.False(name.StartsWith("Share_", StringComparison.OrdinalIgnoreCase),
                         "Transient list contains a Share_ key: " & name)
        Next
    End Sub

    <Fact>
    Public Sub Secrets_and_history_are_only_ever_kept()
        Dim requiredKept As String() = {
            "TranslateApiKey",
            "ResourceSecrets",
            "Resources",
            "Resources.bak",
            "ImageFolder",
            "LastCounter",
            "LastPlayedFile",
            "RecentFolders",
            "RecentMediaFiles",
            "FolderZoomHistory",
            "VideoPositionHistory",
            "DocumentPageHistory",
            "RunsCount",
            "mediaViewedCount"
        }

        For Each key In requiredKept
            Assert.True(ProfileResetScope.IsKeptName(key), "Key '" & key & "' must be classified as Kept.")
            Assert.False(ProfileResetScope.IsResetName(key), "Key '" & key & "' must not be in Reset scope.")
        Next
    End Sub

    <Fact>
    Public Sub ConfirmDelete_and_NoRequestBeforeFileOperation_are_together_in_reset_scope()
        Assert.True(ProfileResetScope.IsResetName("ConfirmDelete"), "ConfirmDelete must be in reset scope.")
        Assert.True(ProfileResetScope.IsResetName("NoRequestBeforeFileOperation"), "NoRequestBeforeFileOperation must be in reset scope.")
    End Sub

    ''' <summary>
    ''' Specification 169 §4.4 classification gate: scans the viewer source tree for every key read or written
    ''' through registry / preferences and asserts that every single key is classified in ProfileResetScope.
    ''' </summary>
    <Fact>
    Public Sub Gate_every_setting_read_or_written_by_viewer_is_classified()
        Dim srcDir As String = FindSourceDir()
        Assert.NotNull(srcDir)

        Dim keysFound As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ' 1. Scan ModernViewerPreferences.vb Read/Write calls
        Dim mvpPath As String = Path.Combine(srcDir, "ModernViewerPreferences.vb")
        If File.Exists(mvpPath) Then
            Dim text As String = File.ReadAllText(mvpPath)
            Dim mvpMatches = Regex.Matches(text, "(?:ReadString|ReadBool|ReadInt|ReadChoice|WriteString|WriteBool)\(\s*""([^""]+)""")
            For Each m As Match In mvpMatches
                keysFound.Add(m.Groups(1).Value)
            Next
        End If

        ' 2. Scan all viewer sources outside FastMediaSorterCompanion
        Dim files = Directory.GetFiles(srcDir, "*.vb", SearchOption.AllDirectories).
            Where(Function(p) p.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              p.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                              p.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0)

        Dim regCallPattern As New Regex("(?:GetSetting|SaveSetting|DeleteSetting)\s*\([^,]+,[^,]+,\s*(?:""([^""]+)""|([A-Za-z0-9_]+))")

        For Each f In files
            Dim content As String = File.ReadAllText(f)
            For Each m As Match In regCallPattern.Matches(content)
                If m.Groups(1).Success Then
                    Dim lit As String = m.Groups(1).Value
                    keysFound.Add(lit)
                Else
                    Dim ident As String = m.Groups(2).Value
                    Select Case ident
                        Case "Registry_Top" : keysFound.Add("EditorTop")
                        Case "Registry_Left" : keysFound.Add("EditorLeft")
                        Case "Registry_Width" : keysFound.Add("EditorWidth")
                        Case "Registry_Height" : keysFound.Add("EditorHeight")
                        Case "Registry_Dpi" : keysFound.Add("EditorDpi")
                        Case "UiLanguageValue" : keysFound.Add("UiLanguage")
                        Case "LegacyRussianValue" : keysFound.Add("Is_Russian_Language")
                        Case "ThemeModes" : keysFound.Add("UiTheme")
                        Case "Value_Name"
                            keysFound.Add("Resources")
                            keysFound.Add("Resources.bak")
                    End Select
                End If
            Next

            ' PersistSettings entries.Add / add("Key", ...)
            If Path.GetFileName(f).Equals("Main_Form.Lifecycle.vb", StringComparison.OrdinalIgnoreCase) Then
                For Each m As Match In Regex.Matches(content, "add\(\s*""([^""]+)""")
                    keysFound.Add(m.Groups(1).Value)
                Next
            End If
        Next

        ' Add MoveOn0..MoveOn9 explicitly if MoveOn was found
        If keysFound.Contains("MoveOn") Then
            For i As Integer = 0 To 9
                keysFound.Add("MoveOn" & i.ToString())
            Next
            keysFound.Remove("MoveOn")
        End If

        ' Verify gate non-triviality
        Assert.True(keysFound.Count >= 80, "Found only " & keysFound.Count & " keys - source scan failed to find keys.")

        Dim unclassified As New List(Of String)()
        For Each key In keysFound.OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase)
            If ProfileResetScope.Classify(key) = "Unclassified" Then
                unclassified.Add(key)
            End If
        Next

        Assert.True(unclassified.Count = 0,
                    "Unclassified settings found (" & unclassified.Count & "): " & String.Join(", ", unclassified))
    End Sub

    Private Shared Function FindSourceDir() As String
        Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Common_Module.vb")) Then
                Return candidate
            End If
            dir = dir.Parent
        End While
        Return Nothing
    End Function

End Class
#End If
