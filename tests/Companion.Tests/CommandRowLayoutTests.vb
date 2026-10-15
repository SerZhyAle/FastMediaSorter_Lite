Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class CommandRowLayoutTests
    <Fact>
    Public Sub Section_commands_fit_content_wrap_and_keep_notes_in_order()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Using form As New Form With {.ClientSize = New Size(1000, 600), .ShowInTaskbar = False,
                        .StartPosition = FormStartPosition.Manual, .Location = New Point(-5000, -5000)}
                        Dim section As New CollapsibleSection("commands", "Commands") With {.Dock = DockStyle.Top}
                        section.Expanded = True
                        form.Controls.Add(section)
                        Dim first As New Button With {.Text = "Restart the service", .Dock = DockStyle.Fill}
                        Dim second As New Button With {.Text = "Repair the service registration", .Dock = DockStyle.Fill}
                        ButtonGlyphs.Apply(first, "action.refresh")
                        ButtonGlyphs.Apply(second, "action.reconnect")
                        section.AddBodyRow(first)
                        section.AddBodyRow(second)
                        Dim note As New Label With {.Text = "Service account access", .AutoSize = True}
                        section.AddBodyRow(note)
                        Dim remove As New Button With {.Text = "Remove the server role"}
                        section.AddBodyRow(remove)
                        form.Show()
                        Application.DoEvents()

                        Dim row = Assert.IsType(Of FlowLayoutPanel)(first.Parent)
                        Assert.Same(row, second.Parent)
                        Assert.Equal(first.Top, second.Top)
                        Assert.True(first.Width < row.ClientSize.Width / 2)
                        Assert.True(second.Width < row.ClientSize.Width / 2)
                        Assert.True(first.Width >= first.GetPreferredSize(Size.Empty).Width)
                        Assert.True(second.Width >= second.GetPreferredSize(Size.Empty).Width)
                        Assert.NotSame(row, remove.Parent)
                        Dim body = DirectCast(section.Body, TableLayoutPanel)
                        Assert.True(body.GetRow(note) > body.GetRow(row))
                        Assert.True(body.GetRow(remove.Parent) > body.GetRow(note))

                        form.ClientSize = New Size(Math.Max(first.Width, second.Width) + 40, 600)
                        Application.DoEvents()
                        Assert.True(second.Top >= first.Bottom)
                        Assert.True(row.Height >= second.Bottom + second.Margin.Bottom)

                        first.Visible = False
                        Application.DoEvents()
                        Assert.Equal(second.Margin.Top, second.Top)
                        form.ClientSize = New Size(1000, 600)
                        first.Visible = True
                        Application.DoEvents()
                        Assert.Equal(first.Top, second.Top)
                        form.Close()
                    End Using
                    VerifySecondaryDialogCommands()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Command row layout test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    Private Shared Sub VerifySecondaryDialogCommands()
        Using form As New Form With {.ClientSize = New Size(900, 300), .ShowInTaskbar = False,
            .StartPosition = FormStartPosition.Manual, .Location = New Point(-5000, -5000)}
            Dim table As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
            table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            Dim commands As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .WrapContents = False, .Anchor = AnchorStyles.Right}
            Dim first As New Button With {.Text = "Copy the connection string", .Width = 400}
            Dim second As New Button With {.Text = "Close this dialog", .Width = 400}
            commands.Controls.AddRange({first, second})
            table.Controls.Add(commands, 1, 0)
            form.Controls.Add(table)
            DpiLayout.ApplyAutoScale(form)
            form.Show()
            Application.DoEvents()
            Assert.True(commands.WrapContents)
            Assert.True(first.Width < 400)
            Assert.Equal(first.Top, second.Top)
            Assert.True(commands.Width <= table.GetColumnWidths()(1))
            form.ClientSize = New Size(500, 300)
            Application.DoEvents()
            Assert.True(second.Top >= first.Bottom)
            Assert.True(commands.Width <= table.GetColumnWidths()(1))
            form.Close()
        End Using
    End Sub
End Class
