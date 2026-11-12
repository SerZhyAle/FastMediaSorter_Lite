Option Strict On

Imports System.Drawing
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>The theme raises a drop-down list's ItemHeight, and the panel around a stock ComboBox goes on sizing it from the
''' height it was created with: the list covered what stood under it, and a ManagerSettingRow cut its bottom off.</summary>
Public Class RowFittingComboBoxTests

    ''' <summary>The order of a real window: built, shown, themed on the first paint.</summary>
    Private Shared Sub ThemedOnShownForm(content As Control, combo As ComboBox, check As Action)
        Using form As New Form With {.StartPosition = FormStartPosition.Manual, .Location = New Point(-20000, -20000),
                                     .ShowInTaskbar = False, .ClientSize = New Size(900, 300)}
            form.Controls.Add(content)
            form.Show()
            Application.DoEvents()
            ThemedComboBox.Apply(combo, ThemePalette.Light)
            Application.DoEvents()
            check()
        End Using
    End Sub

    Private Shared Function NewList() As ComboBox
        Dim combo As New RowFittingComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180}
        combo.Items.Add("Test")
        combo.SelectedIndex = 0
        Return combo
    End Function

    <Fact>
    Public Sub A_setting_row_holds_the_whole_themed_list()
        ExchangeTestSupport.OnUi(Sub()
            Dim combo As ComboBox = NewList()
            Dim row As New ManagerSettingRow(New Label With {.AutoSize = True, .Text = "Theme"}, combo, New Padding(0, 6, 0, 2))
            Dim table As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 1, .Width = 700, .Dock = DockStyle.Top}
            table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            table.Controls.Add(row, 0, 0)
            ThemedOnShownForm(table, combo, Sub() Assert.True(combo.Bottom <= row.ClientSize.Height,
                $"The list ends at {combo.Bottom}, the row at {row.ClientSize.Height}."))
        End Sub)
    End Sub

    <Fact>
    Public Sub A_label_in_a_flow_stays_clear_of_the_themed_list()
        ExchangeTestSupport.OnUi(Sub()
            Dim combo As ComboBox = NewList()
            Dim note As New Label With {.AutoSize = True, .Text = "Under the list"}
            Dim flow As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True}
            flow.Controls.Add(combo)
            flow.Controls.Add(note)
            ThemedOnShownForm(flow, combo, Sub() Assert.True(combo.Bottom <= note.Top,
                $"The list ends at {combo.Bottom}, the label starts at {note.Top}."))
        End Sub)
    End Sub

    ''' <summary>These windows fit themselves to their content on load, and the first paint comes after that: a list that gets its
    ''' height there outgrows the window by the difference.</summary>
    <Theory>
    <InlineData("Forms\Share_Root_Params_Form.vb")>
    <InlineData("Forms\Live_Broadcast_Form.vb")>
    <InlineData("Forms\Scheduled_Operation_Form.vb")>
    Public Sub A_window_that_measures_itself_on_load_takes_the_theme_first(source As String)
        Dim body As String = ExchangeTestSupport.MethodBody(ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(source)),
                                                            "Protected Overrides Sub OnLoad(e As EventArgs)")
        Dim theme As Integer = body.IndexOf("ThemeService.Attach(Me)", StringComparison.Ordinal)
        Dim measure As Integer = body.IndexOf("OptionRows.ArrangeAll(Me)", StringComparison.Ordinal)
        Assert.True(theme >= 0 AndAlso measure > theme, $"{source}: theme at {theme}, the first measure at {measure}.")
    End Sub
End Class
