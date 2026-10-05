#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Windows.Forms

' What the two media context menus - the video's (Main_Form.VideoMenu.vb, right button)
' and the image's (Main_Form.ImageMenu.vb, middle button) - have in common. Modern build
' only, like both of its callers.
'
' It is here rather than duplicated in each because the pieces that are genuinely shared
' are the ones that would rot apart unnoticed: the destination folders (the same eleven
' slots the number keys and the recipients overlay use), the pair of "Move to" /
' "Copy to" submenus, which both menus now offer side by side, and (125 UX-330) ONE "File"
' block - the same rows, in the same order, in both menus.
'
' The rows are ToolStrip items used as command data: Main_Form.ChromeMenus.vb turns them into
' the painted popup. What the popup needs beyond words and a click rides on the item's Tag as
' a MenuRowInfo - the action whose LIVE key is the row's hint, "this destroys", the reason a
' disabled row is disabled, a digit that runs it.
Partial Public Class Main_Form

    ''' <summary>One menu row that runs <paramref name="action"/>.</summary>
    ''' <param name="actionId">The <see cref="CustomHotkeys"/> action whose live key the row shows as its hint
    ''' (125 UX-325); a bracketed key at the end of the caption is the fallback.</param>
    ''' <param name="destructive">The look of "this destroys" - only the look (125 Q125-2).</param>
    ''' <param name="disabledReason">Shown as the tooltip of a disabled row.</param>
    Private Shared Sub AddMenuItem(target As ToolStripItemCollection,
                                   text As String,
                                   action As Action,
                                   Optional checked As Boolean = False,
                                   Optional actionId As String = "",
                                   Optional fallbackHint As String = "",
                                   Optional destructive As Boolean = False,
                                   Optional enabled As Boolean = True,
                                   Optional disabledReason As String = "")
        Dim item As New ToolStripMenuItem(text) With {.Checked = checked, .Enabled = enabled}
        item.Tag = New MenuRowInfo With {.ActionId = actionId, .FallbackHint = fallbackHint, .Destructive = destructive, .DisabledReason = disabledReason}
        AddHandler item.Click, Sub() action()
        target.Add(item)
    End Sub

    ''' <summary>
    ''' "Move to.." / "Copy to.." - the same destination folders the number keys and the
    ''' recipients overlay offer, behind the same one implementation. Nothing when none are
    ''' configured yet: an empty submenu would only advertise a feature the user has not
    ''' set up.
    '''
    ''' Both submenus are built, always. The wording used to follow the global copy mode,
    ''' so the menu showed one of the two actions and the other one was reachable only by
    ''' going to the settings window and coming back
    ''' (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.4).
    '''
    ''' A digit runs its slot while the submenu is open (125 UX-323); a resource that does not allow the verb
    ''' draws the row disabled, with the reason, instead of letting the click fail.
    ''' </summary>
    Private Function CreateRecipientsSubmenu(action As RecipientActionKind) As ToolStripMenuItem
        Dim root As New ToolStripMenuItem(If(action = RecipientActionKind.Copy,
                                             Localization.T("Копировать в"),
                                             Localization.T("Переместить в")))
        Dim silent As Boolean
        Dim reason As String = ResourceRefusalReason(If(action = RecipientActionKind.Copy, ResourceVerb.Copy, ResourceVerb.Move), silent)
        root.Enabled = reason.Length = 0 AndAlso Not silent
        root.Tag = New MenuRowInfo With {.DisabledReason = reason}

        ' Runtime convention, the keyboard's and the overlay's: key "k" -> slot k,
        ' key "0" -> slot 10.
        For k As Integer = 1 To 10
            Dim folder_Path As String = Hardkeys_to_move_mediafile(k)
            If String.IsNullOrEmpty(folder_Path) Then Continue For

            Dim slot As Integer = k
            Dim digit As String = If(k = 10, "0", k.ToString())
            Dim item As New ToolStripMenuItem(digit & "  " & RecipientRowCaption(folder_Path))
            item.Tag = New MenuRowInfo With {.Accelerator = digit(0)}
            AddHandler item.Click, Sub() ExecuteRecipientAction(slot, action)
            root.DropDownItems.Add(item)
        Next

        If root.DropDownItems.Count = 0 Then Return Nothing
        Return root
    End Function

    ''' <summary>
    ''' The shared "File" block of both media menus (125 UX-330, UX-320, UX-324, UX-326): what is known about
    ''' the file, then the operations - undo, rename, move, copy - then delete alone at the end, marked as the
    ''' one that destroys. Every operation row asks the one refusal point and is drawn disabled, with its
    ''' reason, where it would refuse; none is hidden any more, an archive included (010 §7 hid them - a
    ''' disabled row with a sentence is the newer rule, 125 §5.1).
    ''' </summary>
    Private Sub AddFileBlock(target As ToolStripItemCollection)
        AddMenuItem(target, Localization.T("О файле"), Sub() ShowFileDetailsDialog())
        AddMenuItem(target, Localization.T("Копировать путь к файлу"), Sub() CopyFilePathToClipboard())
        Dim onThisComputer As Boolean = Not IsArchiveMode() AndAlso Not IsArchiveOpenPending() AndAlso Not IsResourceMode() AndAlso Not IsStreamOnScreen()
        AddMenuItem(target, Localization.T("Показать в проводнике"), Sub() ShowCurrentFileInExplorer(),
                    enabled:=onThisComputer, disabledReason:=Localization.T("Файл недоступен"))

        target.Add(New ToolStripSeparator())
        AddFileOperationItems(target)
    End Sub

    ''' <summary>Undo / rename / move / copy / delete - identical in both menus, and in every case
    ''' the very methods the U, F6, number and Del keys call.</summary>
    Private Sub AddFileOperationItems(target As ToolStripItemCollection)
        Dim silent As Boolean
        Dim moveReason As String = ResourceRefusalReason(ResourceVerb.Move, silent)
        Dim undoEnabled As Boolean = UndoAvailable() AndAlso moveReason.Length = 0 AndAlso Not silent
        AddMenuItem(target, Localization.T("Отменить действие"), Sub() Undo(), actionId:="undo",
                    enabled:=undoEnabled,
                    disabledReason:=If(moveReason.Length > 0, moveReason, Localization.T("Нечего отменять")))

        Dim renameReason As String = ResourceRefusalReason(ResourceVerb.Rename, silent)
        AddMenuItem(target, Localization.T("Переименовать.. (F6)"), Sub() RenameCurrentFile(), actionId:="renameFile",
                    enabled:=renameReason.Length = 0 AndAlso Not silent, disabledReason:=renameReason)

        Dim move_Item As ToolStripMenuItem = CreateRecipientsSubmenu(RecipientActionKind.Move)
        If move_Item IsNot Nothing Then target.Add(move_Item)
        Dim copy_Item As ToolStripMenuItem = CreateRecipientsSubmenu(RecipientActionKind.Copy)
        If copy_Item IsNot Nothing Then target.Add(copy_Item)

        target.Add(New ToolStripSeparator())
        Dim deleteReason As String = ResourceRefusalReason(ResourceVerb.Delete, silent)
        AddMenuItem(target, Localization.T("Удалить (Del)"), Sub() ReadShowMediaFile(Mode_Delete), actionId:="deleteFile",
                    destructive:=True, enabled:=deleteReason.Length = 0 AndAlso Not silent, disabledReason:=deleteReason)
        ' The permanent delete (Shift+Del) was described nowhere; its live key is the row's tooltip (125 UX-330).
        Dim permanentKey As String = ChromeMenuRules.HintFor(custom_Hotkeys, "deletePermanent", "")
        If permanentKey.Length > 0 Then
            target(target.Count - 1).ToolTipText = Localization.TF("В Корзину. {0} - безвозвратно, минуя Корзину", permanentKey)
        End If
    End Sub

    ''' <summary>"Show in Explorer": the file selected in a folder window. Only for a file that lives on
    ''' this computer; anything else was refused by the row being disabled.</summary>
    Private Sub ShowCurrentFileInExplorer()
        Dim path As String = Current_Image_Path
        If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
            lbl_Status.Text = Localization.T("Файл недоступен")
            Return
        End If
        Try
            Process.Start(New ProcessStartInfo("explorer.exe", "/select,""" & path & """") With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Show in Explorer", ex)
            lbl_Status.Text = Localization.T("Файл недоступен")
        End Try
    End Sub

    ''' <summary>Full screen - the same two flips F7 and F11 do.</summary>
    Private Sub AddViewItems(target As ToolStripItemCollection)
        AddMenuItem(target, Localization.T("Во весь экран (F7)"),
                    AddressOf ToggleFullScreenMode, checked:=is_Full_Screen_Mode, actionId:="fullScreen")
        AddMenuItem(target, Localization.T("Во весь экран без панелей (F11)"),
                    AddressOf ToggleSuperFullScreenMode, checked:=is_Super_Full_Screen_Mode, fallbackHint:="F11")
    End Sub

    ''' <summary>Next / previous file.</summary>
    Private Sub AddNavigationItems(target As ToolStripItemCollection)
        AddMenuItem(target, Localization.T("Следующий файл (Space)"), Sub() ReadShowMediaFile(Mode_Next), actionId:="nextFile")
        AddMenuItem(target, Localization.T("Предыдущий файл (B)"), Sub() ReadShowMediaFile(Mode_Prev), actionId:="prevFile")
    End Sub

End Class
#End If
