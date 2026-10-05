#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>What a menu builder knows about a row beyond the words and the click, carried on the
''' <see cref="ToolStripMenuItem"/>'s Tag to the painted popup (125 section 5.1): the catalogue action whose
''' LIVE key is shown as the row's hint, the look of "this destroys", why a disabled row is disabled and the
''' digit that runs the row while the menu is open.</summary>
Friend NotInheritable Class MenuRowInfo
    ''' <summary>A <see cref="CustomHotkeys"/> action id; the hint is its effective combination right now.</summary>
    Public ActionId As String = ""
    ''' <summary>The key shown when the row has no catalogue action (F11), or when the caption's own bracket says it.</summary>
    Public FallbackHint As String = ""
    Public Destructive As Boolean
    Public DisabledReason As String = ""
    Public Accelerator As Char = ChrW(0)
End Class

Partial Public Class Main_Form
    ' Keep the existing menu builders and their command handlers. ToolStrip items are
    ' command data here; the popup itself uses the shared painted chrome host.

    ''' <param name="fromKeyboard">The keyboard opened the menu (Menu / Shift+F10): the first row has the ring at
    ''' once. A mouse-opened menu shows none until an arrow key asks (125 UX-313j).</param>
    ''' <param name="anchorTop">The top edge of the control the menu opens from, when that control sits low on the
    ''' screen: a menu that does not fit below it opens ABOVE it instead of sliding up over it (the transport bar).</param>
    Private Sub ChromeShowToolStripMenu(items As ToolStripItemCollection, atScreen As Point, Optional fromKeyboard As Boolean = False,
                                        Optional anchorTop As Integer = Integer.MinValue)
        Dim entries As List(Of ChromeMenuItem) = BuildChromeMenuEntries(items)
        ChromeMenu.ShowPopup(Me, atScreen, entries, chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font,
                             focusFirstRow:=fromKeyboard, anchorTop:=anchorTop)
    End Sub

    ''' <summary>The builders' ToolStrip rows as painted-menu rows: separators kept (125 UX-320), a submenu as
    ''' a row with a chevron that opens beside it (UX-322), the hotkey in the caption's brackets replaced by the
    ''' key the live map says (UX-325), and the destructive and disabled-with-a-reason looks.</summary>
    Private Function BuildChromeMenuEntries(items As ToolStripItemCollection) As List(Of ChromeMenuItem)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each source As ToolStripItem In items
            If TypeOf source Is ToolStripSeparator Then
                entries.Add(ChromeMenuItem.Separator())
                Continue For
            End If
            Dim command As ToolStripMenuItem = TryCast(source, ToolStripMenuItem)
            If command Is Nothing OrElse Not command.Available Then Continue For
            Dim info As MenuRowInfo = TryCast(command.Tag, MenuRowInfo)
            Dim caption As String = command.Text
            Dim hint As String = ""
            If info IsNot Nothing Then
                Dim literal As String = ""
                caption = ChromeMenuRules.SplitHintSuffix(command.Text, literal)
                hint = ChromeMenuRules.HintFor(custom_Hotkeys, info.ActionId, If(info.FallbackHint.Length > 0, info.FallbackHint, literal))
            End If
            Dim item As New ChromeMenuItem(If(command.Name.Length > 0, command.Name, "menu_" & entries.Count.ToString())) With {
                .Caption = caption, .AccessibleName = caption, .Tooltip = If(command.ToolTipText, ""), .Hint = hint,
                .Enabled = command.Enabled, .Checked = command.Checked, .Image = command.Image}
            If info IsNot Nothing Then
                item.Destructive = info.Destructive
                item.DisabledReason = info.DisabledReason
                item.Accelerator = info.Accelerator
            End If
            If command.HasDropDownItems Then
                item.Submenu = BuildChromeMenuEntries(command.DropDownItems)
            Else
                item.Action = Sub() command.PerformClick()
            End If
            entries.Add(item)
        Next
        Return entries
    End Function

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
