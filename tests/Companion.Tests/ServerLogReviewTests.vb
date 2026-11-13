Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

<Collection(ScheduledCollection.Name)>
Public Class ServerLogReviewTests
    Private Shared Sub OnSta(action As Action)
        Dim problem As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         problem = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(30000), "review thread did not finish")
        If problem IsNot Nothing Then Throw New Exception("Review assertion failed", problem)
    End Sub

    <Fact>
    Public Sub Revealing_then_closing_preserves_the_exact_reviewed_archive()
        OnSta(Sub()
                  Dim file = Path.Combine(Path.GetTempPath(), "fms-review-" & Guid.NewGuid().ToString("N") & ".zip")
                  System.IO.File.WriteAllText(file, "archive sentinel")
                  Dim state As New SendLogsFlow.ExportReviewState()
                  Dim seen As String = Nothing
                  Try
                      Dim package As New LogPackage.LogPackageResult With {.Path = file, .Entries = New List(Of String) From {"report.txt"}}
                      Using dialog = SendLogsFlow.BuildConsentDialog(Nothing, package, state,
                          Function(value)
                              seen = value
                              Return True
                          End Function)
                          dialog.Show()
                          DirectCast(dialog.Controls.Find("xsl_Reveal", True).Single(), Button).PerformClick()
                          dialog.Close()
                      End Using
                      SendLogsFlow.FinishReview(file, state)
                      Assert.Equal(file, seen)
                      Assert.Equal("archive sentinel", System.IO.File.ReadAllText(file))
                  Finally
                      System.IO.File.Delete(file)
                  End Try
              End Sub)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Cancel_only_deletes_an_archive_before_any_handoff_requested(requested As Boolean)
        Dim file = Path.Combine(Path.GetTempPath(), "fms-review-" & Guid.NewGuid().ToString("N") & ".zip")
        System.IO.File.WriteAllText(file, "reviewed archive")
        Try
            Dim state As New SendLogsFlow.ExportReviewState()
            If requested Then state.RequestHandoff()
            SendLogsFlow.FinishReview(file, state)
            Assert.Equal(requested, System.IO.File.Exists(file))
        Finally
            System.IO.File.Delete(file)
        End Try
    End Sub

    <Theory, Trait("Kind", "UiForm"), Trait("Form", "SendLogsFlow")>
    <InlineData(1.0F, "en", False)>
    <InlineData(1.0F, "en", True)>
    <InlineData(1.75F, "ru", False)>
    <InlineData(1.75F, "ru", True)>
    <InlineData(2.0F, "ar", False)>
    <InlineData(2.0F, "ar", True)>
    Public Sub Long_source_lists_scroll_and_preserve_actions_safe_defaults_and_a_selectable_path(scale As Single, language As String, dark As Boolean)
        OnSta(Sub()
                  Dim saved = Localization.CurrentCode
                  Dim palette = ThemeService.Palette
                  Dim paletteField = GetType(ThemeService).GetField("_palette", BindingFlags.Static Or BindingFlags.NonPublic)
                  Try
                      Localization.CurrentCode = language
                      paletteField.SetValue(Nothing, If(dark, ThemePalette.Dark, ThemePalette.Light))
                      Dim package As New LogPackage.LogPackageResult With {.Path = "C:\logs\fms-logs-review.zip", .OverSizeLimit = True,
                          .ServerSourcesFound = True, .SizeBytes = LogPackage.MaxPackageBytes + 1,
                          .IsPartial = True, .Entries = Enumerable.Range(1, 1000).Select(Function(index) "exchange/machine/exchange-2026-10-01.log." & index.ToString()).ToList()}
                      Using dialog = SendLogsFlow.BuildConsentDialog(Nothing, package)
                          Dim scaledFont As New Font(dialog.Font.FontFamily, dialog.Font.SizeInPoints * scale)
                          dialog.Font = scaledFont
                          AddHandler dialog.Disposed, Sub() scaledFont.Dispose()
                          dialog.Scale(New SizeF(scale, scale))
                          dialog.StartPosition = FormStartPosition.Manual
                          dialog.Location = New Point(-6000, -6000)
                          dialog.TopMost = False
                          dialog.ShowInTaskbar = False
                          dialog.Show()
                          dialog.PerformLayout()
                          Dim sources = DirectCast(dialog.Controls.Find("xsl_Sources", True).Single(), TextBox)
                          Dim archivePath = DirectCast(dialog.Controls.Find("xsl_Path", True).Single(), TextBox)
                          Dim send = DirectCast(dialog.Controls.Find("xsl_Send", True).Single(), Button)
                          Dim cancel = DirectCast(dialog.Controls.Find("xsl_Cancel", True).Single(), Button)
                          Assert.Equal(ScrollBars.Vertical, sources.ScrollBars)
                          Assert.True(sources.Height > 0)
                          Assert.True(archivePath.ReadOnly)
                          archivePath.SelectAll()
                          Assert.Equal(package.Path, archivePath.SelectedText)
                          Assert.False(send.Enabled)
                          Assert.Same(cancel, dialog.AcceptButton)
                          Assert.Same(cancel, dialog.CancelButton)
                          Dim bounds = dialog.RectangleToClient(send.RectangleToScreen(send.ClientRectangle))
                          Assert.True(dialog.ClientRectangle.Contains(bounds), "actions left the review window")
                          Assert.True(sources.RectangleToScreen(sources.ClientRectangle).Bottom <= send.RectangleToScreen(send.ClientRectangle).Top)
                          Assert.Equal(If(language = "ar", RightToLeft.Yes, RightToLeft.No), dialog.RightToLeft)
                          Assert.Equal(ThemeService.Palette.SurfaceWindow, dialog.BackColor)
                          Dim captureDir = Path.Combine(Path.GetTempPath(), "fms-tests", "272-review")
                          Directory.CreateDirectory(captureDir)
                          Using bitmap As New Bitmap(dialog.Width, dialog.Height)
                              dialog.DrawToBitmap(bitmap, New Rectangle(Point.Empty, dialog.Size))
                              bitmap.Save(Path.Combine(captureDir, language & "-" & CInt(scale * 100).ToString() & If(dark, "-dark.png", "-light.png")))
                          End Using
                          dialog.Close()
                      End Using
                  Finally
                      Localization.CurrentCode = saved
                      paletteField.SetValue(Nothing, palette)
                  End Try
              End Sub)
    End Sub
End Class
