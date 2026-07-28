#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Windows.Forms

' What the two media context menus - the video's (Main_Form.VideoMenu.vb, right button)
' and the image's (Main_Form.ImageMenu.vb, middle button) - have in common. Modern build
' only, like both of its callers.
'
' It is here rather than duplicated in each because the pieces that are genuinely shared
' are the ones that would rot apart unnoticed: the destination folders (the same eleven
' slots the number keys and the recipients overlay use) and the "Copy to" vs "Move to"
' wording, which follows a setting either menu can be opened under.
Partial Public Class Main_Form

    ''' <summary>One menu row that runs <paramref name="action"/>.</summary>
    Private Shared Sub AddMenuItem(target As ToolStripItemCollection,
                                   text As String,
                                   action As Action,
                                   Optional checked As Boolean = False)
        Dim item As New ToolStripMenuItem(text) With {.Checked = checked}
        AddHandler item.Click, Sub() action()
        target.Add(item)
    End Sub

    ''' <summary>
    ''' "Move to.." - the same destination folders the number keys and the recipients
    ''' overlay offer, behind the same PoMove. Nothing when none are configured yet: an
    ''' empty submenu would only advertise a feature the user has not set up.
    ''' </summary>
    Private Function CreateRecipientsSubmenu() As ToolStripMenuItem
        Dim rus As Boolean = Is_Russian_Language
        Dim root As New ToolStripMenuItem(If(Is_Copying_not_Moving,
                                             Localization.T("Копировать в"),
                                             Localization.T("Переместить в")))

        ' Runtime convention, the keyboard's and the overlay's: key "k" -> slot k,
        ' key "0" -> slot 10.
        For k As Integer = 1 To 10
            Dim folder_Path As String = Hardkeys_to_move_mediafile(k)
            If String.IsNullOrEmpty(folder_Path) Then Continue For

            Dim slot As Integer = k
            Dim item As New ToolStripMenuItem(If(k = 10, "0", k.ToString()) & "  " & folder_Path)
            AddHandler item.Click, Sub() PoMove(slot)
            root.DropDownItems.Add(item)
        Next

        If root.DropDownItems.Count = 0 Then Return Nothing
        Return root
    End Function

    ''' <summary>Rename / move / delete - identical in both menus, and in both cases the
    ''' very methods the F6, number and Del keys call.</summary>
    Private Sub AddFileOperationItems(target As ToolStripItemCollection)
        Dim rus As Boolean = Is_Russian_Language

        AddMenuItem(target, Localization.T("Переименовать.. (F6)"), Sub() RenameCurrentFile())

        Dim recipients_Item As ToolStripMenuItem = CreateRecipientsSubmenu()
        If recipients_Item IsNot Nothing Then target.Add(recipients_Item)

        AddMenuItem(target, Localization.T("Удалить (Del)"), Sub() ReadShowMediaFile(Mode_Delete))
    End Sub

    ''' <summary>Full screen - the same two flips F7 and F11 do.</summary>
    Private Sub AddViewItems(target As ToolStripItemCollection)
        Dim rus As Boolean = Is_Russian_Language
        AddMenuItem(target, Localization.T("Полный экран (F7)"),
                    AddressOf ToggleFullScreenMode, checked:=is_Full_Screen_Mode)
        AddMenuItem(target, Localization.T("Полный экран без панелей (F11)"),
                    AddressOf ToggleSuperFullScreenMode, checked:=is_Super_Full_Screen_Mode)
    End Sub

    ''' <summary>Next / previous file.</summary>
    Private Sub AddNavigationItems(target As ToolStripItemCollection)
        Dim rus As Boolean = Is_Russian_Language
        AddMenuItem(target, Localization.T("Следующий файл (Space)"), Sub() ReadShowMediaFile(Mode_Next))
        AddMenuItem(target, Localization.T("Предыдущий файл (B)"), Sub() ReadShowMediaFile(Mode_Prev))
    End Sub

End Class
#End If
