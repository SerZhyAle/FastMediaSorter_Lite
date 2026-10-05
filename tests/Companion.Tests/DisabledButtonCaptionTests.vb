Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

Public Class DisabledButtonCaptionTests
    Private Shared Sub WithButton(test As Action(Of Form, Button))
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Using form As New Form With {.AutoScaleMode = AutoScaleMode.None, .ShowInTaskbar = False,
                        .StartPosition = FormStartPosition.Manual, .Location = New Point(-5000, -5000)},
                        button As New Button With {.Text = "Resources", .FlatStyle = FlatStyle.Flat,
                            .UseVisualStyleBackColor = False, .Size = New Size(160, 44),
                            .BackColor = Color.FromArgb(39, 41, 45), .ForeColor = Color.LightSkyBlue, .Enabled = False}
                        button.FlatAppearance.BorderColor = Color.Gray
                        form.Controls.Add(button)
                        form.Show()
                        test(form, button)
                        form.Hide()
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Disabled button test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    Private Shared Function CaptionPixels(button As Button, ink As Color) As Integer
        Using bitmap As New Bitmap(button.Width, button.Height)
            button.DrawToBitmap(bitmap, button.ClientRectangle)
            Dim count As Integer = 0
            For y As Integer = 5 To bitmap.Height - 6
                For x As Integer = 5 To bitmap.Width - 6
                    If bitmap.GetPixel(x, y).ToArgb() = ink.ToArgb() Then count += 1
                Next
            Next
            Return count
        End Using
    End Function

    <Fact>
    Public Sub Self_themed_flat_buttons_draw_disabled_text_in_the_readable_role()
        WithButton(Sub(form, button)
                       Dim face As Color = button.BackColor
                       Dim border As Color = button.FlatAppearance.BorderColor
                       ThemeApplier.ApplyButtonCaptions(form, ThemePalette.Dark)
                       Assert.True(CaptionPixels(button, ThemePalette.Dark.TextDisabled) > 10)
                       Assert.Equal(face, button.BackColor)
                       Assert.Equal(border, button.FlatAppearance.BorderColor)
                       Assert.False(button.Enabled)
                       button.Enabled = True
                       Assert.True(CaptionPixels(button, button.ForeColor) > 10)
                   End Sub)
    End Sub

    <Fact>
    Public Sub Buttons_added_after_the_theme_pass_receive_the_same_disabled_painter()
        WithButton(Sub(form, button)
                       ThemeApplier.ApplyButtonCaptions(form, ThemePalette.Dark)
                       Using panel As New Panel With {.Top = 60}, late As New Button With {
                           .Text = "Clear", .FlatStyle = FlatStyle.Flat, .Size = New Size(120, 44),
                           .BackColor = button.BackColor, .ForeColor = button.ForeColor, .Enabled = False}
                           form.Controls.Add(panel)
                           panel.Controls.Add(late)
                           Assert.True(CaptionPixels(late, ThemePalette.Dark.TextDisabled) > 10)
                       End Using
                   End Sub)
    End Sub

    <Fact>
    Public Sub Switching_to_light_stops_the_dark_disabled_paint_and_keeps_native_state()
        WithButton(Sub(form, button)
                       ThemeApplier.ApplyButtonCaptions(form, ThemePalette.Dark)
                       Assert.True(CaptionPixels(button, ThemePalette.Dark.TextDisabled) > 10)
                       button.BackColor = SystemColors.Control
                       ThemeApplier.ApplyButtonCaptions(form, ThemePalette.Light)
                       Assert.Equal(0, CaptionPixels(button, ThemePalette.Dark.TextDisabled))
                       Assert.False(button.Enabled)
                       button.BackColor = Color.FromArgb(39, 41, 45)
                       ThemeApplier.ApplyButtonCaptions(form, ThemePalette.Dark)
                       Assert.True(CaptionPixels(button, ThemePalette.Dark.TextDisabled) > 10)
                   End Sub)
    End Sub
End Class
