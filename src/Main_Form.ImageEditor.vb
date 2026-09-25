#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

' The viewer's side of the image editor (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §2, §9.7):
' a toolbar button, a menu entry, and what the list does once a file has been written.
'
' Everything else lives in Image_Editor_Form. This partial deliberately knows nothing
' about encoders, EXIF or temporary files - it hands over a path and gets one back
' (invariant 6), which is what keeps the editor out of the navigation code and the file
' list out of the editor.
'
' Modern-only, like the editor itself: the x86 viewer gets neither button nor menu entry.
Partial Public Class Main_Form

    Private WithEvents btn_Edit As Button

    ''' <summary>Creates the "Edit" toolbar button. Called from BuildModernLayout right
    ''' after the translate buttons, so it inherits the uniform chrome and joins the
    ''' overflow set.
    '''
    ''' The button's picture and accessible name both come from IconMap.</summary>
    Friend Sub BuildImageEditorToolbarControls(host As Panel)
        If btn_Edit IsNot Nothing OrElse host Is Nothing Then Return
        btn_Edit = New Button With {
            .Name = "btn_Edit",
            .Text = "",
            .AutoSize = True,
            .TabStop = False,
            .Visible = False,
            .AccessibleName = Localization.T("Редактировать")
        }
        host.Controls.Add(btn_Edit)
        ApplyCatalogGlyph(btn_Edit, 20)
        ' Collapsed until something editable is actually on screen. Visible = False on
        ' its own would not survive the first LayoutToolbar (PlaceControl force-shows
        ' whatever it lays out), so the button has to start in the hidden set.
        SetToolbarItemHidden(btn_Edit, True)
    End Sub

    ''' <summary>Re-applies the button's language-dependent text. Called from
    ''' InitializeTooltips and LngCh.</summary>
    Friend Sub LocalizeImageEditor()
        If btn_Edit Is Nothing Then Return
        btn_Edit.AccessibleName = Localization.T("Редактировать")
        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(btn_Edit, Localization.T("Открыть изображение в редакторе"))
        End If
    End Sub

    ''' <summary>
    ''' Opens the editor on the current file. Modal (ShowDialog): while an edit is in
    ''' progress the slideshow or a stray key must not move the file out from under it.
    ''' </summary>
    Friend Sub ShowImageEditor()
        If Not IsCurrentStillImage() Then Return
        ' The editor button is hidden for a container, but the middle-click menu and a
        ' hotkey reach this directly. Saving means re-packing, which is the deferred
        ' writer (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §7).
        If SecureBlocksWritingAction() Then Return
        Dim filePath As String = Current_File_Name
        If String.IsNullOrEmpty(filePath) Then Return

        SlideShowStop()

        Dim savedPath As String = ""
        Dim savedOverOriginal As Boolean = False

        Using editor As New Image_Editor_Form(filePath, Is_Exif_AutoRotate)
            ' Inside an archive the file on screen is a temporary copy under the archive
            ' cache: "Save" is refused by the writer's own probe (InsideArchive), and "Save
            ' as.." must start beside the ARCHIVE under the entry's own name - not in a
            ' directory deleted on leaving (audit 046, G-03).
            If IsArchiveMode() Then editor.SetSaveAsDefaults(archive_Return_Folder, ArchiveDisplayName(filePath))
            If Not editor.TryLoadOriginal() Then
                lbl_Status.Text = Localization.T("Не удалось открыть изображение для правки")
                Return
            End If

            ' An owned window still has to be put into the viewer's z-order band by hand
            ' when "always on top" is on - see Main_Form.WindowPinning.vb.
            PinToViewerBand(editor)
            PositionChildOnViewerMonitor(editor)
            If editor.ShowDialog(Me) <> DialogResult.OK Then Return

            savedPath = editor.SavedPath
            savedOverOriginal = editor.SavedOverOriginal
        End Using

        If String.IsNullOrEmpty(savedPath) Then Return
        AfterEditorSaved(savedPath, savedOverOriginal)
    End Sub

    ''' <summary>
    ''' What the viewer does with a file the editor just wrote (§9.7).
    '''
    ''' Three cases, and they differ only in what the file list already knows:
    '''   * over the original - the list is right, the pixels on screen are stale;
    '''   * a new file in the same folder - the list has never heard of it;
    '''   * a new file elsewhere - not this folder's business, just say where it went.
    ''' </summary>
    Private Sub AfterEditorSaved(savedPath As String, savedOverOriginal As Boolean)
        If savedOverOriginal Then
            ' Release the image AND its stream before re-reading (ReleaseActiveMedia also
            ' clears current_Loaded_File_Name, without which the dispatcher would decide
            ' the file is already loaded and skip the redraw).
            ReleaseActiveMedia()
            ' The pixel grid may have changed; cached OCR boxes no longer line up. Same
            ' reset the rotate hotkey does.
            current_Overlay_Document = Nothing
            ReadShowMediaFile(Mode_SetFile)
            lbl_Status.Text = Localization.TF("Сохранено: {0}", Path.GetFileName(savedPath))
            Return
        End If

        Dim savedFolder As String = Path.GetDirectoryName(savedPath)
        If String.Equals(savedFolder, Current_Folder_Path, StringComparison.OrdinalIgnoreCase) Then
            ' The folder gained a file the list does not have. Re-read it and land on
            ' what was just made - showing anything else would be the surprising answer.
            ProcessArgument(savedPath)
            lbl_Status.Text = Localization.TF("Сохранено: {0}", Path.GetFileName(savedPath))
            Return
        End If

        lbl_Status.Text = Localization.TF("Сохранено в: {0}", savedPath)
    End Sub

    Private Sub btn_Edit_Click(sender As Object, e As EventArgs) Handles btn_Edit.Click
        ShowImageEditor()
    End Sub

End Class
#End If
