#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Windows.Forms

Partial Public Class Main_Form

    Private _resetCloseRequested As Boolean

    ''' <summary>Called by Table_Form when the user confirms a profile reset.</summary>
    Friend Sub RequestResetClose()
        _resetCloseRequested = True
    End Sub

    ''' <summary>Executed in SettingsWindowClosed when Table_Form finishes closing.</summary>
    Friend Sub HandleResetCloseIfRequested()
        If Not _resetCloseRequested Then Return
        _resetCloseRequested = False
        If IsDisposed Then Return

        BeginInvoke(New MethodInvoker(
            Sub()
                Close()
                If Not IsDisposed Then
                    Notify(Localization.T("Программа ещё не закрыта. Настройки будут сброшены при её следующем запуске."), NotifyLevel.Info)
                End If
            End Sub))
    End Sub

    ''' <summary>
    ''' Displays the result of a pending profile reset that was applied at startup.
    ''' Called once from Main_Form_Shown.
    ''' </summary>
    Friend Sub ShowProfileResetOutcome()
        Dim outcome As ProfileResetOutcome = ProfileResetRunner.ConsumeOutcome()
        If outcome Is Nothing OrElse outcome.Status = ProfileResetStatus.None Then Return

        Select Case outcome.Status
            Case ProfileResetStatus.Applied
                ViewerBox.Show(Me,
                    Localization.TF("Настройки сброшены к значениям по умолчанию. Прежние значения сохранены в файле: {0}. Чтобы вернуть их, закройте программу и откройте этот файл двойным щелчком.", outcome.BackupPath),
                    Localization.T("Сброс настроек"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information)

            Case ProfileResetStatus.Skipped
                ViewerBox.Show(Me,
                    Localization.TF("Настройки не сброшены: не удалось сохранить прежние значения ({0}). Всё осталось как было.", outcome.FailureCause),
                    Localization.T("Сброс настроек"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning)

            Case ProfileResetStatus.PartialSuccess
                ViewerBox.Show(Me,
                    Localization.TF("Настройки сброшены не полностью: не удалось сбросить {0} из {1} значений. Прежние значения сохранены в файле: {2}. Отправьте журнал автору: «Настройки» - «О программе» - «Собрать и отправить».", outcome.FailedCount, outcome.TotalCount, outcome.BackupPath),
                    Localization.T("Сброс настроек"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning)

            Case ProfileResetStatus.Expired
                ViewerBox.Show(Me,
                    Localization.T("Запрос на сброс настроек устарел и не выполнен. Если он нужен, повторите его: «Настройки» - «О программе»."),
                    Localization.T("Сброс настроек"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information)
        End Select
    End Sub

End Class
#End If
