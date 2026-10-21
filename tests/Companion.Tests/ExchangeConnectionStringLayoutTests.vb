Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(UiLanguageCollection.Name)>
Public Class ExchangeConnectionStringLayoutTests
    <Theory>
    <InlineData("ru")>
    <InlineData("de")>
    <InlineData("ar")>
    Public Sub Window_fits_content_and_rows_reflow_when_resized(language As String)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim previous = Localization.CurrentCode
                Try
                    Localization.CurrentCode = language
                    Using form As New Exchange_Connection_String_Form(
                        Function(name, login) New ExchangeProcess.ConnectionStringResult With {
                            .Outcome = ExchangeProcess.ConnectionStringOutcome.Made, .Text = "FMSEXCH1:test"})
                        form.Show()
                        Application.DoEvents()
                        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle)
                        Dim body = form.Controls.OfType(Of FlowLayoutPanel)().Single()
                        Dim closeRow = body.Controls.OfType(Of DialogButtonRow)().Single()
                        Dim bottom = body.Top + closeRow.Bottom + closeRow.Margin.Bottom + body.Padding.Bottom
                        Assert.InRange(form.ClientSize.Height - bottom, 0, form.LogicalToDeviceUnits(40))
                        Capture(form, language, "initial")

                        For Each width In {340, 720, 400}
                            form.ClientSize = New Size(form.LogicalToDeviceUnits(width), form.ClientSize.Height)
                            Application.DoEvents()
                            For Each row As Control In body.Controls
                                Assert.True(row.Right + row.Margin.Right <= body.ClientSize.Width - body.Padding.Right + 1,
                                            $"{row.GetType().Name} exceeds the content width at {width}.")
                            Next
                            Dim fields = body.Controls.OfType(Of TextBox)().ToArray()
                            Assert.Equal(fields(0).Width, fields(2).Width)
                            Assert.True(fields(2).Height >= form.LogicalToDeviceUnits(76), $"String field height: {fields(2).Height}.")
                            Dim commands = body.Controls.OfType(Of FlowLayoutPanel)().First()
                            Assert.True(commands.Top >= fields(2).Bottom, "The commands overlap the connection string.")
                            Dim lastButton = commands.Controls.OfType(Of Button)().Last()
                            Assert.True(commands.Height >= lastButton.Bottom + lastButton.Margin.Bottom, "The command row clips its last button.")
                        Next
                        form.ClientSize = New Size(form.ClientSize.Width, form.LogicalToDeviceUnits(300))
                        Application.DoEvents()
                        Assert.True(form.VerticalScroll.Visible, "A short window must allow scrolling to Close.")
                        Capture(form, language, "narrow")
                        form.Close()
                    End Using
                Finally
                    Localization.CurrentCode = previous
                End Try
            End Sub)
    End Sub

    Private Shared Sub Capture(form As Form, language As String, suffix As String)
        Dim directory = Environment.GetEnvironmentVariable("FMS_SHOT_DIR")
        If String.IsNullOrEmpty(directory) Then Return
        IO.Directory.CreateDirectory(directory)
        Using bitmap As New Bitmap(form.Width, form.Height)
            form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, form.Size))
            bitmap.Save(Path.Combine(directory, "exchange-string-" & language & "-" & suffix & ".png"))
        End Using
    End Sub
End Class
