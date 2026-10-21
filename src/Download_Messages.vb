#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Windows.Forms

''' <summary>The words around a download, shared by every caller of <see cref="Download_Progress_Form"/>. They
''' live in their own file (ticket 243 T-8a) because they name the viewer window, which the test project cannot
''' link, while the progress window itself is built there to check its button row.</summary>
Friend Module DownloadMessages

    ''' <summary>Cancelling is an outcome, not a failure: its own sentence, informational.</summary>
    Friend Sub ShowCancelled(owner As IWin32Window, what As String)
        Dim words As String = Localization.TF("Загрузка «{0}» отменена. Ничего не установлено; её можно запустить снова.", what)
        ' The user's own Cancel is answered on the status line, not with a modal box (124 UX-208): a box for news.
        Dim viewer As Main_Form = ViewerDialogPrep.FindViewer(owner)
        If viewer IsNot Nothing Then
            viewer.Notify(words, NotifyLevel.Info)
        Else
            ViewerBox.Show(owner, words, ProductNames.Full, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End If
    End Sub

    ''' <summary>A failed download: the cause and the actions, Retry first. True = retry.</summary>
    Friend Function AskRetry(owner As IWin32Window, what As String, failure As Exception) As Boolean
        AppFileLogger.LogException("Download failed: " & what, failure)
        Return ViewerBox.Show(owner,
                               Localization.TF("Не удалось загрузить «{0}».", what) & Environment.NewLine & Environment.NewLine &
                               FailureText.Describe(failure),
                               ProductNames.Full, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) = DialogResult.Retry
    End Function

End Module
#End If
