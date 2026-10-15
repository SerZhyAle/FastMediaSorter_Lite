Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

Public Class BroadcastCreateFormTests
    <Theory>
    <InlineData(False, 3600L)>
    <InlineData(True, 86400L)>
    Public Sub Initial_internet_choice_selects_the_short_lifetime_and_accepts_the_file(lanOnly As Boolean, expectedTtl As Long)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         Using form As New Broadcast_Create_Form("C:\Media\film.mp4", New ShareSettings With {.LanOnlyExport = lanOnly})
                                             form.StartPosition = FormStartPosition.Manual
                                             form.Location = New Point(-20000, -20000)
                                             form.Show()
                                             Application.DoEvents()
                                             Dim shotDir = Environment.GetEnvironmentVariable("FMS_SHOT_DIR")
                                             If Not String.IsNullOrEmpty(shotDir) Then
                                                 Directory.CreateDirectory(shotDir)
                                                 Using bitmap As New Bitmap(form.Width, form.Height)
                                                     form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, form.Size))
                                                     bitmap.Save(Path.Combine(shotDir, If(lanOnly, "broadcast-lan.png", "broadcast-internet.png")))
                                                 End Using
                                             End If
                                             DirectCast(form.AcceptButton, Button).PerformClick()
                                             Assert.Equal(DialogResult.OK, form.DialogResult)
                                             Assert.Equal(expectedTtl, form.TtlSeconds)
                                             Assert.Equal(Not lanOnly, form.IncludeExternal)
                                             Assert.Equal("film", form.BroadcastTitle)
                                             Assert.False(form.IsFolderKind)
                                         End Using
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(10000), "The creation dialog did not finish.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
