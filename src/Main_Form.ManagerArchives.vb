#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Partial Public Class Main_Form
    Private manager_Archive_Preparing As ManagerCommandRequest
    Friend Event ManagerArchiveProgress As EventHandler(Of BatchProgressEventArgs)

    Private Sub ShowManagerArchiveProgress(count As Integer, bytes As Long)
        If manager_Archive_Preparing Is Nothing OrElse IsDisposed Then Return
        SayProgress(Localization.TF("Архив: записей {0}; байт {1}", count, bytes))
        RaiseEvent ManagerArchiveProgress(Me, New BatchProgressEventArgs(count, 0, "", bytes, 0))
    End Sub

    Private Sub FinishManagerArchiveProgress()
        RaiseEvent ManagerArchiveProgress(Me, Nothing)
    End Sub
End Class
#End If
