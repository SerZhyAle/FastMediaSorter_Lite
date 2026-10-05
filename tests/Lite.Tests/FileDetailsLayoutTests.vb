#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The "about file" window had its buttons covered: the status line sat over the top of the button row (the
''' absolute layout of ticket 124 only scaled the old numbers), and a control added earlier is drawn over one
''' added later. This holds the rule that matters to the user - both buttons are fully inside the window, on
''' nothing, whatever the scale, the language, the file name or the path - at 100 %, 175 % and 250 % emulated
''' the way DialogLayoutTests does (font grown by the factor, fixed pixels scaled with Control.Scale).
''' </summary>
<Collection("AppFileLogger")>
Public Class FileDetailsLayoutTests

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(120000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>A rectangle of a control in the form's client coordinates.</summary>
    Private Shared Function InClient(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    Friend Shared Function Problems(form As Form) As List(Of String)
        Dim found As New List(Of String)()
        Dim client As Rectangle = form.ClientRectangle
        Dim area As Rectangle = Screen.FromControl(form).WorkingArea
        If form.Width > area.Width OrElse form.Height > area.Height Then
            found.Add("the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
        End If
        Dim box As Control = form.Controls.Find("details_Box", True).Single()
        Dim status As Control = form.Controls.Find("details_Status", True).Single()
        Dim buttons As New List(Of Control) From {form.Controls.Find("details_Copy", True).Single(), form.Controls.Find("details_Close", True).Single()}
        For Each b As Control In buttons
            Dim r As Rectangle = InClient(form, b)
            If Not client.Contains(r) Then found.Add(b.Name & " " & r.ToString() & " is not inside the client area " & client.Size.ToString())
            If Not b.Visible Then found.Add(b.Name & " is not visible")
            Dim need As Integer = TextRenderer.MeasureText(b.Text, b.Font).Width
            If need > b.Width Then found.Add(b.Name & " is " & b.Width.ToString() & " px wide, its caption needs " & need.ToString())
            If Rectangle.Intersect(r, InClient(form, box)).Width > 0 AndAlso Rectangle.Intersect(r, InClient(form, box)).Height > 0 Then
                found.Add(b.Name & " lies over the text box")
            End If
            Dim over As Rectangle = Rectangle.Intersect(r, InClient(form, status))
            If over.Width > 0 AndAlso over.Height > 0 Then found.Add(b.Name & " " & r.ToString() & " lies under/over the status line " & InClient(form, status).ToString())
        Next
        Dim a As Rectangle = InClient(form, buttons(0))
        Dim c As Rectangle = InClient(form, buttons(1))
        If Rectangle.Intersect(a, c).Width > 0 Then found.Add("the two buttons overlap")
        Dim s As Rectangle = InClient(form, status)
        If Not client.Contains(s) Then found.Add("the status line " & s.ToString() & " is not inside the client area")
        Return found
    End Function

    Private Shared Function WithLanguage(code As String, action As Func(Of List(Of String))) As List(Of String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Return action()
        Finally
            Localization.CurrentCode = saved
        End Try
    End Function

    Private Shared Function Measure(filePath As String, factor As Single) As List(Of String)
        Dim form As New File_Details_Form(filePath)
        Try
            Dim auto As Single = form.DeviceDpi / 96.0F
            Dim extra As Single = factor / auto
            If Math.Abs(extra - 1.0F) > 0.01F Then
                form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                form.Scale(New SizeF(extra, extra))
            End If
            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(20, 20)
            form.Show()
            ' The rows are read off the UI thread; wait until the box has them, as a user would.
            Dim box As RichTextBox = DirectCast(form.Controls.Find("details_Box", True).Single(), RichTextBox)
            Dim until As DateTime = DateTime.UtcNow.AddSeconds(20)
            While box.TextLength = 0 AndAlso DateTime.UtcNow < until
                Application.DoEvents()
                Thread.Sleep(10)
            End While
            Application.DoEvents()
            Return Problems(form)
        Finally
            form.Dispose()
        End Try
    End Function

    Private Const LongStem As String = "A-very-long-photo-archive-name-with-no-spaces-at-all-that-keeps-going-and-going-2026-01-02-holiday-in-the-mountains"

    <Theory>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ar")>
    <InlineData("hi")>
    Public Sub The_buttons_are_fully_inside_the_window_at_every_scale_with_a_long_name_and_a_long_path(code As String)
        Dim root As String = Path.Combine(Path.GetTempPath(), "fms-details-" & Guid.NewGuid().ToString("N"))
        Dim deep As String = Path.Combine(root, "a-rather-long-folder-name-number-one", "and-another-quite-long-folder-name-two", "yet-a-third-folder-name-with-many-words-three")
        Directory.CreateDirectory(deep)
        Dim path1 As String = Path.Combine(deep, LongStem & ".jpg")
        File.WriteAllBytes(path1, New Byte() {1, 2, 3})
        Try
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each factor As Single In New Single() {1.0F, 1.75F, 2.5F}
                          For Each p As String In WithLanguage(code, Function() Measure(path1, factor))
                              all.Add(code & " @" & CInt(factor * 100).ToString() & "%: " & p)
                          Next
                      Next
                      Assert.True(all.Count = 0, String.Join(vbLf, all))
                  End Sub)
        Finally
            Try
                Directory.Delete(root, True)
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>A short name and a path that fits: nothing may move out of place either.</summary>
    <Fact>
    Public Sub The_buttons_are_inside_the_window_for_a_short_name()
        ' Named png_Path rather than "file": a local of that name would shadow System.IO.File
        ' (VB ignores case) and the File calls below would compile against the String.
        Dim png_Path As String = Path.Combine(Path.GetTempPath(), "fms-details-short-" & Guid.NewGuid().ToString("N") & ".png")
        File.WriteAllBytes(png_Path, New Byte() {1})
        Try
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each factor As Single In New Single() {1.0F, 1.75F, 2.5F}
                          all.AddRange(Measure(png_Path, factor).Select(Function(p) "@" & CInt(factor * 100).ToString() & "%: " & p))
                      Next
                      Assert.True(all.Count = 0, String.Join(vbLf, all))
                  End Sub)
        Finally
            Try
                File.Delete(png_Path)
            Catch
            End Try
        End Try
    End Sub

End Class
#End If
