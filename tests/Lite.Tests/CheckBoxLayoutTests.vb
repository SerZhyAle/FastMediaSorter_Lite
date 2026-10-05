#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

Public Class CheckBoxLayoutTests
    Private Shared Sub OnSta(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(
            Sub()
                Try
                    test()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Checkbox layout test did not finish.")
        If failure IsNot Nothing Then Throw failure
    End Sub

    <Fact>
    Public Sub Shared_preparation_preserves_the_checkbox_value_caption_and_toggle_handler()
        OnSta(Sub()
                  Using root As New Panel(), nested As New Panel(), check As New CheckBox With {
                      .Text = "Keep floating panels visible", .Checked = True, .RightToLeft = RightToLeft.Yes,
                      .CheckAlign = ContentAlignment.MiddleRight, .TextAlign = ContentAlignment.MiddleRight}
                      root.Controls.Add(nested)
                      nested.Controls.Add(check)
                      Dim changes As Integer = 0
                      AddHandler check.CheckedChanged, Sub() changes += 1
                      CheckBoxLayout.ApplyTree(root)
                      CheckBoxLayout.ApplyTree(root)
                      Assert.True(check.Checked)
                      Assert.Equal("Keep floating panels visible", check.Text)
                      Assert.Equal(0, changes)
                      Assert.Equal(ContentAlignment.MiddleLeft, check.CheckAlign)
                      Assert.Equal(ContentAlignment.MiddleLeft, check.TextAlign)
                      Assert.Equal(RightToLeft.Yes, check.RightToLeft)
                      check.Checked = False
                      Assert.Equal(1, changes)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_flow_hint_aligns_under_the_caption_without_widening_its_text_budget()
        OnSta(Sub()
                  Using flow As New FlowLayoutPanel With {.Width = 300, .FlowDirection = FlowDirection.TopDown,
                      .WrapContents = False}, check As New CheckBox With {.Text = "Reduce motion", .AutoSize = True,
                      .Margin = New Padding(5, 0, 0, 0)}, hint As New Label With {.AutoSize = True,
                      .MaximumSize = New Size(270, 0), .Margin = New Padding(5, 0, 0, 0),
                      .Text = "Disables decorative transitions and the moving audio backdrop."}
                      flow.Controls.AddRange(New Control() {check, hint})
                      CheckBoxLayout.BindHint(check, hint)
                      flow.PerformLayout()
                      Dim inset As Integer = CheckBoxLayout.CaptionInset(check)
                      Assert.Equal(check.Left + inset, hint.Left)
                      Assert.True(hint.Top >= check.Bottom)
                      Assert.Equal(275, hint.MaximumSize.Width + hint.Margin.Left)
                      Dim bounds As Rectangle = hint.Bounds
                      CheckBoxLayout.BindHint(check, hint)
                      check.PerformLayout()
                      flow.PerformLayout()
                      Assert.Equal(bounds, hint.Bounds)
                      Assert.Equal(275, hint.MaximumSize.Width + hint.Margin.Left)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_coordinate_based_hint_preserves_its_right_edge_and_the_following_row()
        OnSta(Sub()
                  Using form As New Form(), check As New CheckBox With {.Left = 14, .Top = 20,
                      .Text = "This is a live broadcast", .AutoSize = True}, hint As New Label With {
                      .Left = 14, .Top = 50, .Width = 300, .Height = 28}
                      form.Controls.AddRange(New Control() {check, hint})
                      Dim originalRight As Integer = hint.Right
                      CheckBoxLayout.BindHint(check, hint)
                      Assert.Equal(check.Left + CheckBoxLayout.CaptionInset(check), hint.Left)
                      Assert.Equal(originalRight, hint.Right)
                      Assert.Equal(50, hint.Top)
                      Assert.Equal(28, hint.Height)
                      check.PerformLayout()
                      Assert.Equal(originalRight, hint.Right)
                  End Using
              End Sub)
    End Sub
End Class
#End If
