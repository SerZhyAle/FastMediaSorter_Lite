Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

' The single remaining Share entry point inside the Settings window after the
' Stage 3 migration to the standalone Fast Media Sorter: Share Manager app (§4.2/
' O-3 of the migration spec): the "Поделиться" tab is gone - instead, one big
' button sits in the "Files and system" tab's existing "Associations and
' integration" group, next to the default-viewer/player buttons. Clicking it calls
' Main_Form.ActivateShareEntryPoint() (Main_Form.ShareLauncher.vb, VB
' default-instance syntax), which finds/wakes Share Manager. Built at runtime, not
' in the Designer file (CLAUDE.md: .Designer.vb is auto-generated, never
' hand-edited) - grows the existing grp_Integration box to make room, the same
' pattern already used for the OCR tab body.
Partial Public Class Table_Form

    Private btn_Share_Manager As Button
    Private _shareLauncherBuilt As Boolean

    ''' <summary>Adds the button once. Called from PrepareForDisplay.</summary>
    Private Sub BuildShareLauncherButtonIfNeeded()
        If _shareLauncherBuilt Then Return
        If grp_Integration Is Nothing Then Return
        _shareLauncherBuilt = True

        grp_Integration.Height = 150

        btn_Share_Manager = New Button With {
            .Location = New Point(16, 100),
            .Size = New Size(624, 34),
            .UseVisualStyleBackColor = True
        }
        AddHandler btn_Share_Manager.Click, AddressOf OnShareManagerClick
        grp_Integration.Controls.Add(btn_Share_Manager)
    End Sub

    Private Sub OnShareManagerClick(sender As Object, e As EventArgs)
        Try
            ' Management entry point - open the Share Manager's own window, not the
            ' share-this-folder wizard (see OpenShareManagerWindow).
            Main_Form.OpenShareManagerWindow()
        Catch ex As Exception
            AppFileLogger.LogException("Table_Form share-manager click", ex)
        End Try
    End Sub

    ''' <summary>Re-localizes the button text. Called from PrepareForDisplay (which
    ''' LngCh already re-runs on every language switch).</summary>
    Private Sub LocalizeShareLauncherButton()
        If btn_Share_Manager Is Nothing Then Return
        btn_Share_Manager.Text = If(Is_Russian_Language,
            "Управление SFTP-сервером для андроид-клиента..",
            "Manage the SFTP server for the Android client..")
    End Sub

End Class
