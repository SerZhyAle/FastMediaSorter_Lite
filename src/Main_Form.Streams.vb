#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System
Imports System.Diagnostics
Imports System.IO
Imports System.Windows.Forms
Imports fmsl.Streams

Partial Public Class Main_Form

    ''' <summary>
    ''' Window action: builds a stream-catalog.zip from playlists in the current folder or a chosen folder.
    ''' </summary>
    Friend Sub BuildStreamCatalogViaDialog()
        Dim targetFolder As String = Current_Folder_Path
        If String.IsNullOrEmpty(targetFolder) OrElse Not Directory.Exists(targetFolder) Then
            Using fbd As New FolderBrowserDialog()
                fbd.Description = Localization.T("Выберите папку с плейлистами (.m3u, .m3u8)")
                If fbd.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrWhiteSpace(fbd.SelectedPath) Then
                    Return
                End If
                targetFolder = fbd.SelectedPath
            End Using
        End If

        Using sfd As New SaveFileDialog()
            sfd.Title = Localization.T("Сохранить каталог потоков")
            sfd.Filter = Localization.T("Каталог потоков (*.zip)|*.zip|Все файлы (*.*)|*.*")
            sfd.FileName = StreamCatalogSchema.ArchiveFileName
            sfd.InitialDirectory = targetFolder

            If sfd.ShowDialog(Me) <> DialogResult.OK OrElse String.IsNullOrWhiteSpace(sfd.FileName) Then
                Return
            End If

            Dim outputZipPath = sfd.FileName

            ' Churn confirmation callback
            Dim churnCallback As ChurnConfirmationCallback =
                Function(lostCount As Integer, previousCount As Integer, lossPercent As Double, mostAffected As String) As Boolean
                    Dim warningMsg = Localization.TF("Замена каталога приведёт к потере {0} из {1} адресов ({2:F1}%). Продолжить запись?", lostCount, previousCount, lossPercent)
                    Dim title = Localization.T("Предупреждение о потере записей")
                    ' The safe answer holds the default focus (APP-BEHAVIOUR 0.12 rule 5): Enter declines.
                    Dim dlgResult = ViewerBox.Show(Me, warningMsg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    Return dlgResult = DialogResult.Yes
                End Function

            Try
                Cursor.Current = Cursors.WaitCursor
                Dim buildResult = StreamCatalogProducer.Build(targetFolder, outputZipPath, forceChurn:=False, churnCallback:=churnCallback)

                If buildResult.Success Then
                    Dim msg = Localization.TF("Каталог потоков успешно создан: {0} записей.", buildResult.Report.ShippedRowCount) & vbCrLf &
                              Localization.TF("Отчёт сохранён: {0}", Path.GetFileName(buildResult.ReportFilePath))
                    Dim caption = Localization.T("Каталог потоков создан")
                    Dim choice = ViewerBox.Show(Me, msg & vbCrLf & vbCrLf & Localization.T("Открыть папку с каталогом?"),
                                                 caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    If choice = DialogResult.Yes Then
                        Try
                            Process.Start("explorer.exe", "/select,""" & outputZipPath & """")
                        Catch
                        End Try
                    End If
                ElseIf buildResult.ChurnRefused Then
                    ' Churn refused by user
                Else
                    Dim failMsg = Localization.TF("Не удалось создать каталог потоков: {0}", buildResult.ErrorDescription)
                    ViewerBox.Show(Me, failMsg, Localization.T("Ошибка создания каталога"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If
            Catch ex As Exception
                AppFileLogger.LogException("BuildStreamCatalogViaDialog", ex)
                ViewerBox.Show(Me, FailureText.Describe(ex), Localization.T("Ошибка создания каталога"), MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                Cursor.Current = Cursors.Default
            End Try
        End Using
    End Sub

End Class
#End If
