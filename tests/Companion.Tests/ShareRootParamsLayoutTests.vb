Option Strict On

Imports System.Drawing
Imports System.Reflection
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>The geometry of the "Resource settings" window, on the real controls with the theme applied. The theme raises the
''' drop-down list's ItemHeight, so the list is taller than a stock ComboBox tells its row: the note under it was half covered.</summary>
<Collection("manager-welcome")>
Public Class ShareRootParamsLayoutTests
    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic

    Private Shared Function Field(Of T)(target As Object, name As String) As T
        Return DirectCast(target.GetType().GetField(name, Flags).GetValue(target), T)
    End Function

    Private Shared Sub OnShownForm(check As Action(Of Share_Root_Params_Form))
        ExchangeTestSupport.OnUi(Sub()
            ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
            Using form As New Share_Root_Params_Form("folder", New ShareRootParams())
                form.StartPosition = FormStartPosition.Manual
                form.Location = New Point(-20000, -20000)
                form.Show()
                Application.DoEvents()
                check(form)
            End Using
        End Sub)
    End Sub

    <Fact>
    Public Sub The_type_list_does_not_cover_the_note_under_it_and_the_window_is_measured_for_its_height()
        OnShownForm(Sub(form)
            Dim content = Field(Of TableLayoutPanel)(form, "_content")
            Dim combo = Field(Of ComboBox)(form, "cmbProfile")
            ' The window was measured in OnLoad; the list must have its themed height by then.
            Assert.Equal(DrawMode.OwnerDrawFixed, combo.DrawMode)
            Dim note = content.GetControlFromPosition(0, content.GetRow(combo) + 1)
            Assert.True(combo.Bottom <= note.Top, $"The list ends at {combo.Bottom}, the note starts at {note.Top}.")
            ' A window capped by the working area scrolls on purpose.
            If form.Height < Screen.FromControl(form).WorkingArea.Height Then
                Assert.False(Field(Of Panel)(form, "_scrollHost").VerticalScroll.Visible)
            End If
        End Sub)
    End Sub

    <Fact>
    Public Sub The_scan_caption_stands_at_the_first_option_and_not_in_the_middle_of_the_column()
        OnShownForm(Sub(form)
            Dim content = Field(Of TableLayoutPanel)(form, "_content")
            Dim column As Control = Field(Of CheckBox)(form, "chkScanSub").Parent.Parent
            Dim caption = content.GetControlFromPosition(0, content.GetRow(column))
            Dim firstOptionBottom As Integer = column.Top + column.Controls(0).Bottom
            Assert.True(caption.Bottom <= firstOptionBottom, $"The caption ends at {caption.Bottom}, the first option at {firstOptionBottom}.")
        End Sub)
    End Sub
End Class
