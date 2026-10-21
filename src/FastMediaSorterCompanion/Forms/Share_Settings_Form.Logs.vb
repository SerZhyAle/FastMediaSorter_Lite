Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Windows.Forms

Partial Public NotInheritable Class Share_Settings_Form
    Private Sub AddShareLogActions(sec As CollapsibleSection)
        sec.AddBodyRow(MakeAction(Localization.T("Открыть журнал событий в редакторе"),
            Sub() OpenManagementLog(Me, False), "action.edit"))
        sec.AddBodyRow(MakeAction(Localization.T("Папка журнала событий.."),
            Sub() OpenManagementLog(Me, True), "content.folder"))
    End Sub

    Friend Shared Sub OpenManagementLog(owner As IWin32Window, folder As Boolean)
        Try
            ManagementLogAccess.OpenShareLog(folder)
        Catch ex As Exception
            ShowManagementLogError(owner, ex)
        End Try
    End Sub

    Private Shared Sub ShowManagementLogError(owner As IWin32Window, ex As Exception)
        AppFileLogger.LogException("Opening a management log", ex)
        MessageBox.Show(owner, Localization.T("Не удалось открыть журнал. Проверьте доступ к папке и приложение для текстовых файлов."),
            ProductNames.ShareManager, MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub
End Class
