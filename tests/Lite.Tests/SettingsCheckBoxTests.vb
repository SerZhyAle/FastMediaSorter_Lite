#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' The drawn checkbox of SPECIFICATION 127 §3.3, painted for real on an STA thread:
''' the checked and unchecked states must differ by FILL (the acceptance bar - a state
''' only a tooltip can read is a defect), the palette arrives only through ApplyPalette,
''' and a palette change (the theme switch) reaches the next repaint.
''' </summary>
Public Class SettingsCheckBoxTests

    Private Shared Sub WithShownCheckBox(test As Action(Of CheckBox))
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    Using form As New Form()
                        form.StartPosition = FormStartPosition.Manual
                        form.Location = New Point(-20000, -20000)
                        Dim check As New CheckBox()
                        form.Controls.Add(check)
                        form.Show()
                        SettingsCheckBox.Attach(check, 1.0F)
                        SettingsCheckBox.ApplyPalette(check,
                            Color.FromArgb(240, 240, 240), ' row surface
                            Color.FromArgb(255, 255, 255), ' unchecked fill (editor back)
                            Color.FromArgb(120, 120, 120), ' border
                            Color.FromArgb(20, 20, 20),    ' hover ink
                            Color.FromArgb(0, 90, 200),    ' checked fill (accent)
                            Color.FromArgb(255, 255, 255)) ' tick
                        Application.DoEvents()
                        test(check)
                        form.Hide()
                    End Using
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Checkbox test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    Private Shared Function CenterPixel(check As CheckBox) As Color
        Using bitmap As New Bitmap(check.Width, check.Height)
            check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
            ' The 20 px box is centred in the 32 px hit target, so the middle is fill.
            Return bitmap.GetPixel(check.Width \ 2, check.Height \ 2)
        End Using
    End Function

    <Fact>
    Public Sub Unchecked_and_checked_fills_are_their_palette_roles()
        WithShownCheckBox(
            Sub(check)
                check.Checked = False
                Application.DoEvents()
                Dim uncheckedFill As Color = CenterPixel(check)
                check.Checked = True
                Application.DoEvents()
                Dim checkedFill As Color = CenterPixel(check)

                Assert.Equal(Color.FromArgb(255, 255, 255), uncheckedFill)
                Assert.Equal(Color.FromArgb(0, 90, 200), checkedFill)
                ' Distinguishability: the two states differ by fill, not by a hair.
                Assert.True(Math.Abs(CInt(checkedFill.R) - uncheckedFill.R) +
                            Math.Abs(CInt(checkedFill.G) - uncheckedFill.G) +
                            Math.Abs(CInt(checkedFill.B) - uncheckedFill.B) > 200,
                            "Checked and unchecked are too close to tell apart.")
            End Sub)
    End Sub

    <Fact>
    Public Sub A_theme_change_reaches_the_next_repaint()
        WithShownCheckBox(
            Sub(check)
                check.Checked = True
                Application.DoEvents()
                Assert.Equal(Color.FromArgb(0, 90, 200), CenterPixel(check))

                ' The palette walker on a theme switch hands over the new roles.
                SettingsCheckBox.ApplyPalette(check,
                    Color.FromArgb(39, 41, 45), Color.FromArgb(50, 52, 57), Color.FromArgb(177, 181, 189),
                    Color.FromArgb(244, 245, 247), Color.FromArgb(200, 60, 60), Color.FromArgb(250, 250, 250))
                Application.DoEvents()
                Assert.Equal(Color.FromArgb(200, 60, 60), CenterPixel(check))
            End Sub)
    End Sub

    <Fact>
    Public Sub Attach_resizes_to_the_hit_target_around_the_box()
        WithShownCheckBox(
            Sub(check)
                ' BOOLEAN-OPTION 2.0 section 2 rule 4 / UI-SIZING section 2 rule 4: 32 by 32 around a 20 px box.
                Assert.Equal(32, check.Width)
                Assert.Equal(32, check.Height)
                Assert.True(SettingsCheckBox.IsAttached(check))
                Assert.Equal(String.Empty, check.Text)
                ' The primitive keeps its own toggle behavior.
                Dim changed As Boolean = False
                AddHandler check.CheckedChanged, Sub() changed = True
                check.Checked = Not check.Checked
                Assert.True(changed)
            End Sub)
    End Sub

    <Fact>
    Public Sub A_scale_change_resizes_an_attached_checkbox_and_keeps_its_state()
        WithShownCheckBox(
            Sub(check)
                check.Checked = True
                SettingsCheckBox.Attach(check, 1.75F)
                Assert.Equal(56, check.Width)
                Assert.Equal(56, check.Height)
                Assert.True(check.Checked)
                Assert.Equal(Color.FromArgb(0, 90, 200), CenterPixel(check))
                SettingsCheckBox.Attach(check, 1.0F)
                Assert.Equal(32, check.Width)
                Assert.True(check.Checked)
            End Sub)
    End Sub
End Class
#End If
