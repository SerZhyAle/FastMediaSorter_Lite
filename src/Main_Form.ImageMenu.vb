#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' The picture's context menu, on the MIDDLE mouse button - modern build only, and an
' improvement rather than a fix (the x86 viewer keeps the historical mechanics, exactly as
' it does for zoom and the video menu).
'
' WHY THE MIDDLE BUTTON, and why not the right one: on a picture the right button is
' "previous file" and has been for fourteen years - that muscle memory outranks any
' convention, so it stays. The middle button was a single hidden command (rename), which is
' the weakest possible use of a whole button: undiscoverable, and one action out of the
' dozen the app can perform on the picture in front of you. It now opens all of them, with
' rename among them - so nothing is lost and the rest stops being reachable only by a key
' you have to know about.
'
' The menu OWNS no behaviour: every item calls the very method its hotkey calls (rotate,
' zoom, OCR, full screen, the file operations), so the two cannot drift apart. It is built
' on each open because half of it depends on the picture in front of you and on settings.
Partial Public Class Main_Form

    Private image_Menu As ContextMenuStrip

    ''' <summary>Where the middle button was pressed, in panel_Media coordinates - "100 %"
    ''' zooms about the point the user aimed at, not about wherever the pointer has
    ''' wandered to by the time the item is picked.</summary>
    Private image_Menu_Anchor As Point

    ''' <summary>
    ''' Opens the picture menu, and says whether it did. False when there is no picture on
    ''' screen - the caller then falls back to the historical middle-click rename, so a
    ''' middle click over a playing video or an empty window behaves as it always has.
    ''' </summary>
    Private Function TryShowImageContextMenu() As Boolean
        If Not is_PictureBox1_Visible AndAlso Not is_PictureBox2_Visible Then Return False
        If String.IsNullOrEmpty(Current_File_Name) Then Return False

        If panel_Media IsNot Nothing Then
            image_Menu_Anchor = panel_Media.PointToClient(Cursor.Position)
        End If

        If image_Menu Is Nothing Then image_Menu = New ContextMenuStrip()
        BuildImageMenu()
        If image_Menu.Items.Count = 0 Then Return False

        ChromeShowToolStripMenu(image_Menu.Items, Cursor.Position)
        Return True
    End Function

    Private Sub BuildImageMenu()
        ' Dispose the previous generation, do not just unparent it: a middle click builds
        ' ~11 items, ~6 separators and the two recipients submenus (one item per configured
        ' slot), and Items.Clear disposes none of them.
        ClearAndDisposeItems(image_Menu.Items)

        Dim rus As Boolean = Is_Russian_Language

        ' --- the editor, first: it is the only entry that opens another window, and it
        ' is offered only for a still picture (an animation would have to lose either its
        ' frames or its animation - §10 of the editor specification).
        If IsCurrentStillImage() Then
            AddMenuItem(image_Menu.Items, Localization.T("Редактировать.."), Sub() ShowImageEditor())
            image_Menu.Items.Add(New ToolStripSeparator())
        End If

        ' --- and the animation's counterpart, in the same first position and under the
        ' opposite predicate: an animation cannot be edited, and a still cannot be turned
        ' into a video. There is no hotkey for it - the letters near this concept are taken,
        ' and a one-key path to a permanent delete is not something to add quietly (§11).
        If IsCurrentAnimation() Then
            AddMenuItem(image_Menu.Items, Localization.T("Заменить видео.."),
                        Sub() StartReplaceAnimationWithVideo())
            image_Menu.Items.Add(New ToolStripSeparator())
        End If

        ' --- secure containers (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §7,
        ' O-2). "Unlock" always asks, even for one declined a moment ago - declining meant
        ' "not now". "Verify" is the full guarantee the streaming reader gives up: it reads
        ' every block and checks the sealed digest, which on a large file is a full read,
        ' so it is an action rather than something paid for on every open. "Forget the
        ' password" is here whenever one is held, because a password the user cannot put
        ' down is one they have to close the program to be rid of.
        ' "Open" only for a LOCKED placeholder (audit 052 G-07): a damaged or unsupported
        ' container is not offered a password dialog it could never use.
        If IsSecurePlaceholderLocked() Then
            AddMenuItem(image_Menu.Items, Localization.T("Открыть защищённый файл.. (Enter)"),
                        Sub() SecureUnlockCurrent())
            image_Menu.Items.Add(New ToolStripSeparator())
        ElseIf IsCurrentFileSecureContainer() AndAlso IsSecureContentOnScreen() Then
            AddMenuItem(image_Menu.Items, Localization.T("Проверить контейнер целиком"),
                        Sub() SecureVerifyCurrent())
            image_Menu.Items.Add(New ToolStripSeparator())
        End If
        If SecureHasCredential() Then
            AddMenuItem(image_Menu.Items, Localization.T("Забыть пароль"), Sub() SecureForgetCredential())
            image_Menu.Items.Add(New ToolStripSeparator())
        End If

        ' --- the picture itself
        AddMenuItem(image_Menu.Items, Localization.T("Повернуть по часовой (R)"),
                    Sub() RotateActiveImage(True))
        AddMenuItem(image_Menu.Items, Localization.T("Повернуть против часовой (Shift+R)"),
                    Sub() RotateActiveImage(False))

        ' --- zoom (the grey NumPad block does the same)
        image_Menu.Items.Add(New ToolStripSeparator())
        AddMenuItem(image_Menu.Items, Localization.T("Вписать в окно (серый /)"),
                    Sub() ZoomToFit())
        AddMenuItem(image_Menu.Items, Localization.T("Реальный размер, 100 % (серый *)"),
                    Sub() ZoomToActualSize(image_Menu_Anchor))

        ' --- OCR / translation, only when the feature is on: with it off, T is plain
        ' rotate and there is nothing here to offer.
        If ocr_Settings IsNot Nothing AndAlso ocr_Settings.Enabled Then
            image_Menu.Items.Add(New ToolStripSeparator())
            AddMenuItem(image_Menu.Items, Localization.T("Перевести текст на картинке (T)"),
                        Sub() TriggerOcrHotkey())
            AddMenuItem(image_Menu.Items, Localization.T("Переводить автоматически (Shift+T)"),
                        Sub() ToggleOcrAutoMode(), checked:=ocr_Settings.AutoMode)
        End If

        ' --- view
        image_Menu.Items.Add(New ToolStripSeparator())
        AddViewItems(image_Menu.Items)

        ' --- navigation
        image_Menu.Items.Add(New ToolStripSeparator())
        AddNavigationItems(image_Menu.Items)

        ' --- the file
        image_Menu.Items.Add(New ToolStripSeparator())
        AddFileOperationItems(image_Menu.Items)

        ' --- elsewhere
        image_Menu.Items.Add(New ToolStripSeparator())
        AddMenuItem(image_Menu.Items, Localization.T("Копировать путь к файлу"),
                    Sub() CopyFilePathToClipboard())
    End Sub

End Class
#End If
