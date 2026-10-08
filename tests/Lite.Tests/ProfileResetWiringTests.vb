#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Specification 169 §8: wiring and source-scan invariant tests for profile reset all.
''' </summary>
Public Class ProfileResetWiringTests

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    <Fact>
    Public Sub Application_events_calls_apply_pending_in_created_new_before_first_allow_new_windows()
        Dim path As String = System.IO.Path.Combine(RepoRoot(), "src", "Application_Events.vb")
        Dim content As String = File.ReadAllText(path)

        Dim applyPendingIdx As Integer = content.IndexOf("ProfileResetRunner.ApplyPending", StringComparison.Ordinal)
        Assert.True(applyPendingIdx >= 0, "Application_Events.vb must call ProfileResetRunner.ApplyPending")

        Dim createdNewIdx As Integer = content.IndexOf("If created_New Then", StringComparison.Ordinal)
        Assert.True(createdNewIdx >= 0 AndAlso createdNewIdx < applyPendingIdx,
                    "ApplyPending must be inside If created_New Then block")

        Dim markRoleIdx As Integer = content.IndexOf("MultiWindowPolicy.MarkInstanceRole", StringComparison.Ordinal)
        Assert.True(markRoleIdx >= 0 AndAlso markRoleIdx < applyPendingIdx,
                    "ApplyPending must be after MarkInstanceRole")

        Dim allowWindowsIdx As Integer = content.IndexOf("MultiWindowPolicy.AllowNewWindows()", StringComparison.Ordinal)
        Assert.True(allowWindowsIdx > applyPendingIdx,
                    "ApplyPending must be before first AllowNewWindows call")

        Dim themeInitIdx As Integer = content.IndexOf("ThemeService.Initialize()", StringComparison.Ordinal)
        Assert.True(themeInitIdx > applyPendingIdx,
                    "ApplyPending must be before ThemeService.Initialize call")
    End Sub

    <Fact>
    Public Sub Table_form_modern_layout_adds_reset_row_immediately_after_support_send_logs()
        Dim path As String = System.IO.Path.Combine(RepoRoot(), "src", "Table_Form.ModernLayout.vb")
        Dim lines As String() = File.ReadAllLines(path)

        Dim supportRowLine As Integer = -1
        Dim resetRowLine As Integer = -1

        For i As Integer = 0 To lines.Length - 1
            If lines(i).Contains("AddSettingRow(flow, ""support_send_logs""") Then
                supportRowLine = i
            ElseIf lines(i).Contains("AddSettingRow(flow, ""profile_reset_all""") Then
                resetRowLine = i
            End If
        Next

        Assert.True(supportRowLine >= 0, "support_send_logs row not found in Table_Form.ModernLayout.vb")
        Assert.True(resetRowLine >= 0, "profile_reset_all row not found in Table_Form.ModernLayout.vb")
        Assert.True(resetRowLine > supportRowLine, "profile_reset_all must be after support_send_logs")

        ' Ensure there are no other AddSettingRow calls between them
        For i As Integer = supportRowLine + 1 To resetRowLine - 1
            Assert.False(lines(i).Contains("AddSettingRow"),
                         "profile_reset_all row must be placed directly after support_send_logs without intervening rows")
        Next
    End Sub

    <Fact>
    Public Sub Legacy_vbproj_lists_all_new_vb_sources()
        Dim path As String = System.IO.Path.Combine(RepoRoot(), "src", "FastMediaSorter.vbproj")
        Dim text As String = File.ReadAllText(path)

        Dim expectedFiles As String() = {
            "ProfileReset\ProfileResetScope.vb",
            "ProfileReset\ProfileResetStore.vb",
            "ProfileReset\RegBackup.vb",
            "ProfileReset\ProfileResetRunner.vb",
            "Localization\Localization.ProfileReset.vb",
            "Table_Form.ProfileReset.vb",
            "Main_Form.ProfileReset.vb"
        }

        For Each expected As String In expectedFiles
            Dim pattern As String = "Include=""" & Regex.Escape(expected) & """"
            Assert.True(Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase),
                        "FastMediaSorter.vbproj is missing entry for: " & expected)
        Next
    End Sub

    <Fact>
    Public Sub Profile_reset_confirm_is_registered_in_ask_specs_all()
        Dim allSpecs As IReadOnlyList(Of AskSpec) = AskSpecs.All()
        Dim target As AskSpec = AskSpecs.ProfileResetConfirm()
        Dim found As AskSpec = allSpecs.FirstOrDefault(Function(s) s.Title = target.Title AndAlso s.Body = target.Body)
        Assert.NotNull(found)
        Assert.Equal(AskSpecs.ProfileResetCancel, found.DefaultIndex)
        Assert.Equal(AskSpecs.ProfileResetCancel, found.CancelIndex)
        Assert.True(found.Buttons(AskSpecs.ProfileResetExecute).Destructive)
        Assert.False(found.Buttons(AskSpecs.ProfileResetCancel).Destructive)
    End Sub

    <Fact>
    Public Sub Localization_profile_reset_has_no_em_dash_and_uses_guillemets_for_russian()
        Dim path As String = System.IO.Path.Combine(RepoRoot(), "src", "Localization", "Localization.ProfileReset.vb")
        Dim content As String = File.ReadAllText(path)

        Assert.DoesNotContain(ChrW(&H2014).ToString(), content) ' No em dash (—)
        Assert.DoesNotContain(ChrW(&H2013).ToString(), content) ' No en dash (–)
        Assert.Contains("«", content)
        Assert.Contains("»", content)
    End Sub

End Class
#End If
