#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

Public Class SettingsNumericStepperTests
    <DllImport("user32.dll", EntryPoint:="SendMessageW")>
    Private Shared Function SendMessage(window As IntPtr, message As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Shared Sub Apply(number As NumericUpDown, scale As Single)
        SettingsNumericStepper.Apply(number, scale, Color.White, Color.Black, Color.Gray, Color.Blue)
    End Sub

    Private Shared Sub WithNumber(scale As Single, test As Action(Of NumericUpDown, Control))
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Using form As New Form With {.AutoScaleMode = AutoScaleMode.None, .ShowInTaskbar = False,
                        .StartPosition = FormStartPosition.Manual, .Location = New Point(-5000, -5000)},
                        font As New Font("Segoe UI", 16 * scale, FontStyle.Regular, GraphicsUnit.Pixel),
                        number As New NumericUpDown With {.Width = CInt(110 * scale), .Font = font,
                            .Minimum = -1, .Maximum = 10, .DecimalPlaces = 2, .Increment = 0.25D, .Value = 5}
                        form.Controls.Add(number)
                        form.Show()
                        Apply(number, scale)
                        Application.DoEvents()
                        Dim buttons As Control = number.Controls.Cast(Of Control)().Single(Function(c) Not TypeOf c Is TextBox)
                        test(number, buttons)
                        form.Hide()
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Numeric stepper test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    Private Shared Sub ClickHalf(buttons As Control, down As Boolean)
        Dim x As Integer = buttons.Width \ 2
        Dim y As Integer = If(down, buttons.Height * 3 \ 4, buttons.Height \ 4)
        Dim point As New IntPtr((y << 16) Or x)
        SendMessage(buttons.Handle, &H201, New IntPtr(1), point)
        SendMessage(buttons.Handle, &H202, IntPtr.Zero, point)
    End Sub

    <Theory>
    <InlineData(1.0F)>
    <InlineData(1.5F)>
    <InlineData(1.75F)>
    <InlineData(2.0F)>
    Public Sub Both_native_hit_targets_scale_and_leave_the_edit_outside_the_button_area(scale As Single)
        WithNumber(scale,
            Sub(number, buttons)
                Assert.Equal(CInt(Math.Round(28 * scale)), buttons.Width)
                Assert.True(buttons.Height >= number.ClientSize.Height - 4)
                Dim edit As Control = number.Controls.Cast(Of Control)().Single(Function(c) TypeOf c Is TextBox)
                Assert.True(edit.Right <= buttons.Left)
                Dim bounds As Rectangle = buttons.Bounds
                Apply(number, scale)
                number.PerformLayout()
                Assert.Equal(bounds, buttons.Bounds)
                Assert.Equal(5D, number.Value)
                ClickHalf(buttons, False)
                Assert.Equal(5.25D, number.Value)
                ClickHalf(buttons, True)
                Assert.Equal(5D, number.Value)
            End Sub)
    End Sub

    <Fact>
    Public Sub Enlarged_buttons_keep_native_limits_and_do_not_duplicate_value_events()
        WithNumber(1.75F,
            Sub(number, buttons)
                number.Value = number.Maximum
                Dim changes As Integer = 0
                AddHandler number.ValueChanged, Sub() changes += 1
                ClickHalf(buttons, False)
                Assert.Equal(number.Maximum, number.Value)
                Assert.Equal(0, changes)
                ClickHalf(buttons, True)
                Assert.Equal(number.Maximum - number.Increment, number.Value)
                Assert.Equal(1, changes)
                number.Value = number.Minimum
                changes = 0
                ClickHalf(buttons, True)
                Assert.Equal(number.Minimum, number.Value)
                Assert.Equal(0, changes)
            End Sub)
    End Sub

    <Fact>
    Public Sub Keyboard_arrows_and_typed_decimal_values_keep_native_validation()
        WithNumber(1.75F,
            Sub(number, buttons)
                Dim edit As Control = number.Controls.Cast(Of Control)().Single(Function(c) TypeOf c Is TextBox)
                Dim key As New KeyEventArgs(Keys.Up)
                GetType(Control).GetMethod("OnKeyDown", BindingFlags.Instance Or BindingFlags.NonPublic).
                    Invoke(edit, New Object() {key})
                Assert.True(key.Handled)
                Assert.Equal(5.25D, number.Value)
                number.Text = 4.75D.ToString(Globalization.CultureInfo.CurrentCulture)
                ClickHalf(buttons, False)
                Assert.Equal(5D, number.Value)
            End Sub)
    End Sub

    <Fact>
    Public Sub Right_to_left_keeps_the_native_button_and_text_order()
        WithNumber(1.75F,
            Sub(number, buttons)
                number.RightToLeft = RightToLeft.Yes
                Apply(number, 1.75F)
                Dim edit As Control = number.Controls.Cast(Of Control)().Single(Function(c) TypeOf c Is TextBox)
                Assert.True(buttons.Right <= edit.Left)
                ClickHalf(buttons, False)
                Assert.Equal(5.25D, number.Value)
            End Sub)
    End Sub

    <Fact>
    Public Sub Reapplying_the_scale_updates_the_existing_controls_and_palette()
        WithNumber(1.0F,
            Sub(number, buttons)
                Apply(number, 2.0F)
                Assert.Equal(56, buttons.Width)
                Apply(number, 1.0F)
                Assert.Equal(28, buttons.Width)
                Assert.Equal(5D, number.Value)
                SettingsNumericStepper.Apply(number, 1.0F, Color.Red, Color.Black, Color.Gray, Color.Blue)
                Using bitmap As New Bitmap(buttons.Width, buttons.Height)
                    buttons.DrawToBitmap(bitmap, buttons.ClientRectangle)
                    Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(3, 3).ToArgb())
                End Using
            End Sub)
    End Sub
End Class
#End If
