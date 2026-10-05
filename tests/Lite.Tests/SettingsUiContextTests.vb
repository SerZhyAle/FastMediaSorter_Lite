#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

Public Class SettingsUiContextTests
    <Fact>
    Public Sub Reordered_pages_and_independent_group_states_round_trip_without_captions()
        Dim state As New SettingsUiContext With {.LastPageId = "video"}
        state.Page("video").Groups("tracks") = True
        state.Page("video").Groups("advanced") = False
        state.Page("images").Groups("advanced") = True
        state.Page("video").AnchorId = "preferred_subtitle_language"
        state.Page("video").AnchorOffsetLogical = -12
        Dim invalid As Boolean
        Dim restored = SettingsUiContext.Read(state.ToJson(), invalid)
        Assert.False(invalid)
        Assert.Equal("video", restored.ResolvePage({"images", "general", "video"}))
        Assert.True(restored.Page("video").Expanded("tracks", False))
        Assert.False(restored.Page("video").Expanded("advanced", True))
        Assert.True(restored.Page("images").Expanded("advanced", False))
        Assert.Equal(-12, restored.Page("video").AnchorOffsetLogical)
        Assert.True(restored.Page("video").Expanded("newGroup", True))
    End Sub

    <Theory>
    <InlineData("{")>
    <InlineData("null")>
    <InlineData("{""SchemaVersion"":2}")>
    <InlineData("{""Pages"":null}")>
    Public Sub Invalid_or_future_state_resets_only_private_context(json As String)
        Dim invalid As Boolean
        Dim state = SettingsUiContext.Read(json, invalid)
        Assert.True(invalid)
        Assert.Equal("general", state.LastPageId)
        Assert.Empty(state.Pages)
    End Sub

    <Fact>
    Public Sub Removed_page_and_collapsed_row_fall_back_to_visible_targets()
        Dim state As New SettingsUiContext With {.LastPageId = "removed"}
        Assert.Equal("general", state.ResolvePage({"video", "general"}))
        Dim page As New SettingsPageContext With {.AnchorId = "subtitle"}
        Dim owners As New Dictionary(Of String, String) From {{"subtitle", "video/tracks"}}
        Assert.Equal("video/tracks", page.ResolveAnchor({"video/basic", "video/tracks"}, owners))
        page.AnchorId = "deletedRow"
        Assert.Equal("video/basic", page.ResolveAnchor({"video/basic", "video/tracks"}, owners))
    End Sub

    <Fact>
    Public Sub Null_page_entries_and_unbounded_offsets_are_tolerated()
        Dim invalid As Boolean
        Dim state = SettingsUiContext.Read("{""Pages"":{""deleted"":null,""video"":{""AnchorOffsetLogical"":2147483647}}}", invalid)
        Assert.False(invalid)
        Assert.False(state.Pages.ContainsKey("deleted"))
        Assert.Equal(4096, state.Page("video").AnchorOffsetLogical)
    End Sub

    <Fact>
    Public Sub Collapsing_a_focused_group_preserves_values_and_removes_its_body_from_focus()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Using form As New Form(), group As New SettingsCollapsibleGroup("audio/advanced", 1.0F, System.Drawing.SystemFonts.MessageBoxFont)
                        group.Width = 400
                        group.Caption = "Advanced settings"
                        Dim number As New NumericUpDown With {.Value = 17}
                        group.Body.Controls.Add(number)
                        form.Controls.Add(group)
                        form.Show()
                        number.Focus()
                        group.Expanded = False
                        Assert.Equal(17D, number.Value)
                        Assert.False(group.Body.Visible)
                        Assert.True(group.Header.Focused)
                        Assert.True((group.Header.AccessibilityObject.State And AccessibleStates.Collapsed) <> 0)
                        Using summaryFont As New System.Drawing.Font("Segoe UI", 8.0F)
                            group.SetSummary("A summary that wraps onto multiple lines at a narrow width")
                            group.SetSummaryStyle(summaryFont, System.Drawing.Color.Gray)
                            group.Width = 180
                            group.SetLayoutScale(1.75F)
                            Assert.Equal(System.Drawing.ContentAlignment.TopLeft, group.Header.TextAlign)
                            Dim captionHeight = TextRenderer.MeasureText(group.Caption, group.Header.Font,
                                New System.Drawing.Size(group.Header.Width - group.Header.Padding.Horizontal - 4, Integer.MaxValue), TextFormatFlags.WordBreak).Height
                            Dim summaryHeight = TextRenderer.MeasureText(group.Header.AccessibleDescription, summaryFont,
                                New System.Drawing.Size(group.Header.Width - group.Header.Padding.Horizontal - 4, Integer.MaxValue), TextFormatFlags.WordBreak).Height
                            Assert.True(group.Header.Height >= group.Header.Padding.Top + captionHeight + 2 + summaryHeight,
                                        "The collapsed summary must fit below the caption.")
                            group.Header.AccessibilityObject.DoDefaultAction()
                            Assert.Equal(System.Drawing.ContentAlignment.MiddleLeft, group.Header.TextAlign)
                        End Using
                        Assert.True(group.Expanded)
                        Assert.True(group.Body.Visible)
                        Assert.True(group.Height > group.Header.Height)
                        Assert.Equal("Advanced settings", group.Header.AccessibleName)
                        form.Hide()
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Group test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub
End Class
#End If
