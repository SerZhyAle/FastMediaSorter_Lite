#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

<Collection("LocalizationState")>
Public Class ResourcePinDialogTests
    <Fact>
    Public Sub Original_pin_prompt_is_masked_cancellable_and_fits_all_authored_languages()
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    For Each code As String In Localization.Codes
                        Localization.CurrentCode = code
                        Using dialog As New Resource_Credential_Form("Private photos", "", True, showRemember:=False, pinMode:=True)
                            dialog.StartPosition = FormStartPosition.Manual
                            dialog.Location = New Point(20, 20)
                            dialog.Show()
                            Dim field As TextBox = DirectCast(dialog.Controls("resource_Password"), TextBox)
                            Assert.True(field.UseSystemPasswordChar)
                            Assert.Equal(128, field.MaxLength)
                            Assert.False(dialog.Controls.OfType(Of CheckBox)().Any(Function(c) c.Visible AndAlso c.Checked))
                            Assert.Same(field, dialog.ActiveControl)
                            Assert.Empty(DialogLayoutTests.Problems(dialog))
                            If code = "en" Then
                                Dim directory As String = Path.Combine(ExchangeWireTestKit.RepoRoot(), "temp", "216")
                                System.IO.Directory.CreateDirectory(directory)
                                Using bitmap As New Bitmap(dialog.Width, dialog.Height)
                                    dialog.DrawToBitmap(bitmap, New Rectangle(Point.Empty, dialog.Size))
                                    bitmap.Save(Path.Combine(directory, "resource-pin-en.png"), Imaging.ImageFormat.Png)
                                End Using
                            End If
                            DirectCast(dialog.CancelButton, Button).PerformClick()
                            Assert.Equal(DialogResult.Cancel, dialog.DialogResult)
                            Assert.Empty(dialog.Credential)
                        End Using
                    Next
                Catch ex As Exception
                    failure = ex
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(30000), "PIN dialog check timed out")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
#End If
