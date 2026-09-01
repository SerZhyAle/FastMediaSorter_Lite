#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' The folder box's right-click menu - modern build only, like the Share entry point it
' carries (the x86 viewer has no Share at all: see Main_Form.ShareLauncher.vb).
'
' WHY THIS FILE EXISTS AT ALL: the menu was attached with
' cmbox_Media_Folder.ContextMenuStrip = ... and simply never appeared. An editable
' ComboBox is really TWO windows - the combo, and a native EDIT sitting inside it - and
' the right-click lands on the EDIT, which answers it itself with Windows' own
' Cut/Copy/Paste. So the one place a person would ever right-click, the folder path,
' showed the wrong menu, and "Share this folder.." was reachable only from the tiny
' "Каталог:" label next to it. It read as "the command does not work" - and effectively
' it didn't.
'
' The fix is to hook the inner EDIT and answer WM_CONTEXTMENU ourselves.
Partial Public Class Main_Form

    Private folder_Context_Menu As ContextMenuStrip
    Private folder_Menu_Select_Item As ToolStripMenuItem
    Private folder_Menu_File_Item As ToolStripMenuItem
    Private folder_Menu_Open_Archive_Item As ToolStripMenuItem
    Private folder_Menu_Close_Archive_Item As ToolStripMenuItem
    Private folder_Menu_Share_Item As ToolStripMenuItem
    Private folder_Combo_Edit_Hook As ComboEditContextMenuHook

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function FindWindowEx(hwndParent As IntPtr, hwndChildAfter As IntPtr,
                                         lpszClass As String, lpszWindow As String) As IntPtr
    End Function

    ''' <summary>Builds the folder-box context menu and makes sure it actually shows up.
    ''' Called once from Form1_Load.</summary>
    Friend Sub InitializeFolderMenu()
        If folder_Context_Menu Is Nothing Then
            ' The tooltips are load-bearing here, not decoration - see the item below.
            folder_Context_Menu = New ContextMenuStrip() With {.ShowItemToolTips = True}

            folder_Menu_Select_Item = New ToolStripMenuItem()
            AddHandler folder_Menu_Select_Item.Click, Sub() SelectFolderViaDialog()
            folder_Context_Menu.Items.Add(folder_Menu_Select_Item)

            folder_Menu_File_Item = New ToolStripMenuItem()
            AddHandler folder_Menu_File_Item.Click, Sub() Choose_file()
            folder_Context_Menu.Items.Add(folder_Menu_File_Item)

            ' Next to Select folder../Select file.. (010_SPECIFICATION_ARCHIVE_BROWSING
            ' §2.1 point 4, §2.3, §12 Ф4) - "Close archive" only shows while an archive is
            ' actually open, decided fresh on every Opening so the state can never go stale.
            folder_Menu_Open_Archive_Item = New ToolStripMenuItem()
            AddHandler folder_Menu_Open_Archive_Item.Click, Sub() OpenArchiveViaDialog()
            folder_Context_Menu.Items.Add(folder_Menu_Open_Archive_Item)

            folder_Menu_Close_Archive_Item = New ToolStripMenuItem()
            AddHandler folder_Menu_Close_Archive_Item.Click, Sub() CloseArchiveAndReturn()
            folder_Context_Menu.Items.Add(folder_Menu_Close_Archive_Item)

            folder_Context_Menu.Items.Add(New ToolStripSeparator())

            folder_Menu_Share_Item = New ToolStripMenuItem()
            AddHandler folder_Menu_Share_Item.Click, Sub() ActivateShareEntryPoint()
            folder_Context_Menu.Items.Add(folder_Menu_Share_Item)

            AddHandler folder_Context_Menu.Opening, Sub() folder_Menu_Close_Archive_Item.Visible = IsArchiveMode()

            ' The label answers a right-click by itself, so the plain assignment works
            ' there. The combo needs the hook below.
            If lbl_Folder IsNot Nothing Then lbl_Folder.ContextMenuStrip = folder_Context_Menu
            If cmbox_Media_Folder IsNot Nothing Then
                cmbox_Media_Folder.ContextMenuStrip = folder_Context_Menu
                HookFolderComboEdit()
                ' Reparenting or a style change recreates the inner EDIT and silently
                ' drops the hook with it - take it again when that happens.
                AddHandler cmbox_Media_Folder.HandleCreated, Sub() HookFolderComboEdit()
            End If
        End If
        LocalizeFolderMenu()
    End Sub

    ''' <summary>Subclasses the ComboBox's inner EDIT so the right-click on the folder
    ''' path opens our menu instead of Windows' Cut/Copy/Paste.</summary>
    Private Sub HookFolderComboEdit()
        Try
            If cmbox_Media_Folder Is Nothing OrElse Not cmbox_Media_Folder.IsHandleCreated Then Return

            Dim edit_Handle As IntPtr = FindWindowEx(cmbox_Media_Folder.Handle, IntPtr.Zero, "EDIT", Nothing)
            If edit_Handle = IntPtr.Zero Then Return   ' a non-editable combo has none

            If folder_Combo_Edit_Hook IsNot Nothing Then folder_Combo_Edit_Hook.Detach()
            folder_Combo_Edit_Hook = New ComboEditContextMenuHook(edit_Handle, cmbox_Media_Folder, folder_Context_Menu)
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2520: folder combo edit hooked for the context menu")
        Catch ex As Exception
            ' Worst case the old behaviour returns (the native edit menu) - never a crash.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2521: folder combo hook failed: " & ex.Message)
        End Try
    End Sub

    Friend Sub LocalizeFolderMenu()
        If folder_Menu_Select_Item IsNot Nothing Then
            folder_Menu_Select_Item.Text = Localization.T("Выбрать папку..")
            folder_Menu_Select_Item.ToolTipText = Localization.T("Открыть другую папку с медиафайлами")
        End If

        If folder_Menu_File_Item IsNot Nothing Then
            ' "Мамка" is the house joke: папка (folder) has a mate. The pun only exists in
            ' Russian, and in either language the caption alone does not say what the item
            ' does - hence the tooltip, which is the part that has to be plain. Same action
            ' as the toolbar's "file" button and the F / F4 keys.
            folder_Menu_File_Item.Text = Localization.T("Выбрать мамку..")
            folder_Menu_File_Item.ToolTipText = Localization.T("Выбрать медиафайл (F, F4)")
        End If

        If folder_Menu_Open_Archive_Item IsNot Nothing Then
            folder_Menu_Open_Archive_Item.Text = Localization.T("Открыть архив..")
            folder_Menu_Open_Archive_Item.ToolTipText = Localization.T("Открыть ZIP или CBZ как папку")
        End If

        If folder_Menu_Close_Archive_Item IsNot Nothing Then
            folder_Menu_Close_Archive_Item.Text = Localization.T("Закрыть архив")
            folder_Menu_Close_Archive_Item.ToolTipText = Localization.T("Вернуться в папку, где лежит архив")
        End If

        If folder_Menu_Share_Item IsNot Nothing Then
            ' "Android", not "phone": the feature IS Android Folder Share - the app on the
            ' other end is the Android one, and a tablet is not a phone.
            folder_Menu_Share_Item.Text = Localization.T("Поделиться этой папкой с Android..")
            folder_Menu_Share_Item.ToolTipText = Localization.T("Открыть Share Manager и раздать эту папку на Android")
        End If
    End Sub

    ''' <summary>
    ''' Answers the right-click for a ComboBox's inner EDIT.
    '''
    ''' NativeWindow rather than a ComboBox subclass on purpose: the EDIT is created by
    ''' the ComboBox itself, is not a WinForms control, and never routes its
    ''' WM_CONTEXTMENU to the parent - it just runs its own menu and stops.
    ''' </summary>
    Private NotInheritable Class ComboEditContextMenuHook
        Inherits NativeWindow

        Private Const WM_CONTEXTMENU As Integer = &H7B

        Private ReadOnly _menu As ContextMenuStrip
        Private ReadOnly _owner As Control

        Public Sub New(edit_Handle As IntPtr, owner As Control, menu As ContextMenuStrip)
            _owner = owner
            _menu = menu
            AssignHandle(edit_Handle)
        End Sub

        Public Sub Detach()
            Try
                ReleaseHandle()
            Catch
            End Try
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_CONTEXTMENU AndAlso _menu IsNot Nothing Then
                _menu.Show(ScreenPointFor(m.LParam))
                Return   ' swallowed: no native Cut/Copy/Paste behind ours
            End If
            MyBase.WndProc(m)
        End Sub

        ''' <summary>Where to put the menu. lParam carries the screen position, except
        ''' for the keyboard's own menu key, which sends (-1,-1) - then drop it under the
        ''' control, where the mouse would have opened it.</summary>
        Private Function ScreenPointFor(lParam As IntPtr) As Point
            Dim raw As Long = lParam.ToInt64()
            Dim x As Integer = CInt(CShort(raw And &HFFFFL))
            Dim y As Integer = CInt(CShort((raw >> 16) And &HFFFFL))
            If x = -1 AndAlso y = -1 Then
                Return _owner.PointToScreen(New Point(0, _owner.Height))
            End If
            Return New Point(x, y)
        End Function

    End Class

End Class
#End If
