#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

Partial Public Class Main_Form
    ' Keep the existing menu builders and their command handlers. ToolStrip items are
    ' command data here; the popup itself uses the shared painted chrome host.
    Private Sub ChromeShowToolStripMenu(items As ToolStripItemCollection, atScreen As Point)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each source As ToolStripItem In items
            Dim command As ToolStripMenuItem = TryCast(source, ToolStripMenuItem)
            If command Is Nothing OrElse Not command.Available Then Continue For
            Dim item As New ChromeMenuItem(If(command.Name.Length > 0, command.Name, "menu_" & entries.Count.ToString())) With {
                .Caption = command.Text, .AccessibleName = command.Text, .Tooltip = command.ToolTipText,
                .Enabled = command.Enabled, .Checked = command.Checked, .Image = command.Image}
            If command.HasDropDownItems Then
                item.Action = Sub() ChromeShowToolStripMenu(command.DropDownItems, Cursor.Position)
            Else
                item.Action = Sub() command.PerformClick()
            End If
            entries.Add(item)
        Next
        ChromeMenu.ShowPopup(Me, atScreen, entries, chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font)
    End Sub

    Private Function ChromeControlMenuPoint(control As Control) As Point
        For Each panel In chrome_Panels
            For Each binding In panel.Legacy
                If binding.Value Is control AndAlso Not binding.Key.Bounds.IsEmpty Then
                    Return panel.Host.Surface.PointToScreen(New Point(binding.Key.Bounds.Left, binding.Key.Bounds.Bottom))
                End If
            Next
        Next
        Return Cursor.Position
    End Function
End Class
#End If
