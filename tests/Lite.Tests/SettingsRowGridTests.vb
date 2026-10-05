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

    <Fact>
    Public Sub Two_columns_apply_at_and_above_the_stacked_threshold()
        Dim column As Integer = SettingsRowGridRules.PageValueColumn(340)
        Assert.False(SettingsRowGridRules.IsTwoColumn(SettingsRowGridRules.StackedBelowWidth - 1, column))
        Assert.False(SettingsRowGridRules.IsTwoColumn(SettingsRowGridRules.StackedBelowWidth, column))
        Assert.True(SettingsRowGridRules.IsTwoColumn(SettingsRowGridRules.StackedBelowWidth, SettingsRowGridRules.MinValueColumnWidth))
        Assert.True(SettingsRowGridRules.IsTwoColumn(SettingsRowGridRules.StackedBelowWidth + 400, column))
    End Sub

    <Fact>
    Public Sub A_page_column_that_would_starve_the_labels_stacks()
        ' Insets count against the label floor as well as the shared column.
        Dim column As Integer = SettingsRowGridRules.MaxValueColumnWidth
        Dim minimumHost As Integer = column + SettingsRowGridRules.MinLabelColumnWidth + SettingsRowGridRules.RowColumnInsets
        Assert.True(SettingsRowGridRules.IsTwoColumn(minimumHost, column))
        ' One logical px more column and the labels are under their floor.
        Assert.False(SettingsRowGridRules.IsTwoColumn(minimumHost - 1, column))
        ' A narrower column on the same content width keeps two columns.
        Assert.True(SettingsRowGridRules.IsTwoColumn(SettingsRowGridRules.StackedBelowWidth, SettingsRowGridRules.MinValueColumnWidth))
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
    Public Sub The_stacked_fallback_stretches_only_fill_editors()
        ' A text editor fills the stacked row's content width.
        Assert.Equal(400, SettingsRowGridRules.StackedEditorWidth(200, 400, True))
        ' A button, a checkbox and a spinner keep their text-fitted or declared width.
        Assert.Equal(120, SettingsRowGridRules.StackedEditorWidth(120, 400, False))
        ' And nothing is wider than the row actually is.
        Assert.Equal(150, SettingsRowGridRules.StackedEditorWidth(280, 150, False))
        Assert.Equal(150, SettingsRowGridRules.StackedEditorWidth(280, 150, True))
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
