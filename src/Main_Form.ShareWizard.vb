Option Strict On

Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Main-window entry points for the Android-share feature (the wizard side of
''' SPECIFICATION_ANDROID_FOLDER_SHARE.md). Three ways in, all landing on the
''' same one-window <see cref="Share_Wizard_Form"/> for the folder currently
''' being viewed:
'''   * a toolbar button (built in code, next to the Translate button),
'''   * the Shift+S hotkey (KeybUse, Main_Form.KeyboardInput.vb),
'''   * a "Share this folder.." item on the folder-box right-click menu
'''     (the media surfaces already own right-click for file navigation).
''' </summary>
Partial Public Class Main_Form

    Private WithEvents btn_Share As Button
    Private shareFolderMenu As ContextMenuStrip
    Private shareMenuItem As ToolStripMenuItem

    ''' <summary>Creates the toolbar "Share" button. Called from BuildModernLayout
    ''' right after the Translate button so it inherits the uniform chrome.</summary>
    Friend Sub BuildShareToolbarControls(host As Panel)
        If btn_Share IsNot Nothing OrElse host Is Nothing Then Return
        btn_Share = New Button With {
            .Name = "btn_Share",
            .Text = If(Is_Russian_Language, "Поделиться", "Share"),
            .AutoSize = True,
            .TabStop = False
        }
        host.Controls.Add(btn_Share)
    End Sub

    ''' <summary>Sets up the folder-box context menu and tooltips. Called from
    ''' Form1_Load after the toolbar is built.</summary>
    Friend Sub InitializeShareEntryPoints()
        If shareFolderMenu Is Nothing Then
            shareFolderMenu = New ContextMenuStrip()
            shareMenuItem = New ToolStripMenuItem()
            AddHandler shareMenuItem.Click, Sub() ActivateShareEntryPoint()
            shareFolderMenu.Items.Add(shareMenuItem)

            ' Right-click on the media surfaces is already file navigation; attach
            ' the "share this folder" menu to the folder box + its label instead.
            If cmbox_Media_Folder IsNot Nothing Then cmbox_Media_Folder.ContextMenuStrip = shareFolderMenu
            If lbl_Folder IsNot Nothing Then lbl_Folder.ContextMenuStrip = shareFolderMenu
        End If

        LocalizeShareEntryPoints()
    End Sub

    ''' <summary>Re-localizes the Share button + menu (called from LngCh). The button
    ''' stays visible whether or not server features are enabled (it is the "диалог
    ''' кнопки поделиться" opt-in entry point); its tooltip reflects the state.</summary>
    Friend Sub LocalizeShareEntryPoints()
        Dim rus As Boolean = Is_Russian_Language
        Dim enabled As Boolean = ServerFeatures.IsEnabled()
        If btn_Share IsNot Nothing Then btn_Share.Text = If(rus, "Поделиться", "Share")
        If shareMenuItem IsNot Nothing Then shareMenuItem.Text = If(rus, "Поделиться этой папкой с телефоном..", "Share this folder with phone..")
        If toolTip IsNot Nothing AndAlso btn_Share IsNot Nothing Then
            toolTip.SetToolTip(btn_Share, If(enabled,
                If(rus,
                    "Поделиться текущей папкой с телефоном Android по локальной сети (Shift+S). ПКМ по каталогу - тоже.",
                    "Share the current folder with an Android phone over the local network (Shift+S). Right-click the folder box too."),
                ShareText.ServerDisabledButtonHint(rus)))
        End If
        LocalizeShareTray()
    End Sub

    Private Sub btn_Share_Click(sender As Object, e As EventArgs) Handles btn_Share.Click
        ActivateShareEntryPoint()
    End Sub

    ''' <summary>The shared entry-point action for the toolbar button, the folder-box
    ''' right-click item and Shift+S. When server features are enabled it opens the
    ''' share wizard; otherwise it offers the one-time enable flow first, then opens
    ''' the wizard on success (SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md §3.3-3.4).</summary>
    Friend Sub ActivateShareEntryPoint()
        If ServerFeatures.IsEnabled() Then
            OpenShareWizard()
        Else
            PromptEnableServerFeatures(thenOpenWizard:=True)
        End If
    End Sub

    ''' <summary>Shows the deferred opt-in dialog. On a successful enable, reveals the
    ''' Share UI live (no restart) and optionally opens the wizard right away.</summary>
    Friend Sub PromptEnableServerFeatures(Optional thenOpenWizard As Boolean = False)
        If Not ServerFeatures.CanEnable() Then
            MessageBox.Show(Me, ShareText.ServerEnableUnavailable(Is_Russian_Language),
                            ShareText.ServerEnableTitle(Is_Russian_Language), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using f As New Share_Enable_Form(Is_Russian_Language)
            If f.ShowDialog(Me) = DialogResult.OK Then
                OnServerFeaturesEnabledLive()
                If thenOpenWizard Then OpenShareWizard()
            End If
        End Using
    End Sub

    ''' <summary>Called once after server features are enabled at runtime: refresh the
    ''' gate-dependent chrome (button tooltip, tray) so the feature appears without a
    ''' restart. The Settings window, if open, swaps its own Share tab body.</summary>
    Friend Sub OnServerFeaturesEnabledLive()
        ServerFeatures.Refresh()
        Try : LocalizeShareEntryPoints() : Catch : End Try
        Try : RefreshShareTray() : Catch : End Try
    End Sub

    ''' <summary>Opens the one-window share wizard for the folder being viewed.
    ''' Resolves the folder from Current_Folder_Path, falling back to the folder
    ''' of the current media file. Assumes the gate is already satisfied (callers go
    ''' through <see cref="ActivateShareEntryPoint"/>).</summary>
    Friend Sub OpenShareWizard()
        Dim folder As String = ResolveCurrentFolder()
        Using w As New Share_Wizard_Form(folder)
            w.ShowDialog(Me)
        End Using
    End Sub

    ''' <summary>Opens the unified Settings window on its Share tab (from the
    ''' wizard's "Open sharing settings" link).</summary>
    Friend Sub ShowShareSettings()
        If Table_Form Is Nothing OrElse Table_Form.IsDisposed Then Table_Form = New Table_Form()
        Table_Form.PrepareForDisplay()
        Table_Form.SelectShareTab()
        Table_Form.Show(Me)
        Table_Form.Activate()
    End Sub

    Private Function ResolveCurrentFolder() As String
        Try
            Dim f As String = If(Current_Folder_Path, "").Trim()
            If f.Length > 0 AndAlso Directory.Exists(f) Then Return f
            If Not String.IsNullOrEmpty(Current_Image_Path) Then
                Dim d As String = Path.GetDirectoryName(Current_Image_Path)
                If Not String.IsNullOrEmpty(d) AndAlso Directory.Exists(d) Then Return d
            End If
        Catch
        End Try
        Return ""
    End Function

End Class
