#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' The row-grid arithmetic of SPECIFICATION 127 §3.1 and §3.2, straight from
''' <see cref="SettingsRowGridRules"/> - the helper the settings window's layout passes
''' run through. Every number is a logical pixel at the default scale, exactly the unit
''' the constants are written in.
''' </summary>
Public Class SettingsRowGridTests

    <Fact>
    Public Sub The_value_column_is_clamped_to_its_bounds()
        Assert.Equal(SettingsRowGridRules.MinValueColumnWidth, SettingsRowGridRules.PageValueColumn(0))
        Assert.Equal(SettingsRowGridRules.MinValueColumnWidth, SettingsRowGridRules.PageValueColumn(110))
        Assert.Equal(SettingsRowGridRules.MinValueColumnWidth, SettingsRowGridRules.PageValueColumn(SettingsRowGridRules.MinValueColumnWidth))
        Assert.Equal(260, SettingsRowGridRules.PageValueColumn(260))
        Assert.Equal(SettingsRowGridRules.MaxValueColumnWidth, SettingsRowGridRules.PageValueColumn(500))
        Assert.Equal(SettingsRowGridRules.MaxValueColumnWidth, SettingsRowGridRules.PageValueColumn(Integer.MaxValue))
    End Sub

    <Theory>
    <InlineData(880, 340, 340)>
    <InlineData(598, 340, 340)>
    <InlineData(560, 340, 302)>
    <InlineData(440, 340, 201)>
    <InlineData(320, 340, 141)>
    Public Sub Narrow_rows_keep_the_editor_beside_readable_labels(contentWidth As Integer, preferredWidth As Integer, expectedWidth As Integer)
        Dim column As Integer = SettingsRowGridRules.FitValueColumn(contentWidth, preferredWidth)
        Assert.Equal(expectedWidth, column)
        Dim labels As Integer = SettingsRowGridRules.LabelColumnWidth(contentWidth, column)
        Assert.True(labels >= Math.Min(SettingsRowGridRules.MinLabelColumnWidth,
                                      (contentWidth - SettingsRowGridRules.RowColumnInsets) \ 2))
        Assert.Equal(contentWidth, labels + column + SettingsRowGridRules.RowColumnInsets)
    End Sub

    <Fact>
    Public Sub A_narrow_preferred_column_does_not_expand_when_the_window_shrinks()
        Assert.Equal(200, SettingsRowGridRules.FitValueColumn(560, 200))
        Assert.Equal(110, SettingsRowGridRules.FitValueColumn(440, 110))
        Assert.Equal(1, SettingsRowGridRules.FitValueColumn(0, 340))
    End Sub

    <Fact>
    Public Sub The_label_column_is_what_the_value_column_leaves()
        Assert.Equal(322, SettingsRowGridRules.LabelColumnWidth(560, 200))
        Assert.Equal(182, SettingsRowGridRules.LabelColumnWidth(560, 340))
    End Sub

    <Fact>
    Public Sub Fill_editors_take_the_column_and_fixed_editors_keep_their_width()
        Assert.Equal(300, SettingsRowGridRules.EditorWidth(200, 300, True))
        Assert.Equal(340, SettingsRowGridRules.EditorWidth(400, 340, True))
        Assert.Equal(110, SettingsRowGridRules.EditorWidth(110, 300, False))
        Assert.Equal(280, SettingsRowGridRules.EditorWidth(280, 300, False))
        ' A fixed editor never overflows the column it belongs to.
        Assert.Equal(340, SettingsRowGridRules.EditorWidth(400, 340, False))
    End Sub

    <Fact>
    Public Sub Command_buttons_and_strips_are_text_fitted()
        Assert.Equal(200, SettingsRowGridRules.CommandButtonWidth(168))
        ' Two commands of 120 with one 8 px gap; three commands with two gaps.
        Assert.Equal(248, SettingsRowGridRules.StripWidth(New Integer() {120, 120}))
        Assert.Equal(376, SettingsRowGridRules.StripWidth(New Integer() {120, 120, 120}))
        Assert.Equal(120, SettingsRowGridRules.StripWidth(New Integer() {120}))
    End Sub

    <Fact>
    Public Sub The_row_grid_has_no_stored_state()
        ' The rules are shared functions on a NotInheritable class - two pages with
        ' different columns never interfere.
        Dim results As New List(Of Integer) From {
            SettingsRowGridRules.PageValueColumn(340),
            SettingsRowGridRules.PageValueColumn(110)}
        Assert.Equal(SettingsRowGridRules.MaxValueColumnWidth, results(0))
        Assert.Equal(SettingsRowGridRules.MinValueColumnWidth, results(1))
    End Sub
End Class
#End If
