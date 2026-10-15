#If Not NETFRAMEWORK Then
Option Strict On

' Split out of DeleteVolumeProbe.vb (ticket 222) so the probe can be linked into the Share Manager without
' the viewer's strings. 017_SPECIFICATION_RECYCLE_BIN_AND_UNDO_DOTNET10.md §3.7.

''' <summary>
''' What the user is told, built from the decision and nothing else - which is what
''' keeps the question and the status line from ever disagreeing (invariant 2), and
''' what makes "permanently" always come with a reason (invariant 3).
''' </summary>
Friend Module DeleteText

    ''' <summary>The question for one file.</summary>
    Friend Function ConfirmOne(decision As DeleteDecision, fileName As String) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("Удалить файл '{0}' в Корзину?", fileName)
        End If

        Select Case decision.Reason
            Case PermanentReason.NoBinOnNetwork
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: на сетевом диске Корзины нет.", fileName)
            Case PermanentReason.NoBinOnRemovable
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: на съёмном носителе Корзины нет.", fileName)
            Case PermanentReason.BinDisabledOnVolume
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: Корзина отключена для этого диска.", fileName)
            Case PermanentReason.FileExceedsBinQuota
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: он больше, чем вмещает Корзина.", fileName)
            Case PermanentReason.VolumeUnknown
                Return Localization.TF("Файл '{0}' будет удалён безвозвратно: это расположение не поддерживает Корзину.", fileName)
            Case Else
                ' UserAsked: they held Shift, or switched the bin off. No lecture about
                ' the volume - they know what they asked for.
                Return Localization.TF("Удалить файл '{0}' безвозвратно, минуя Корзину?", fileName)
        End Select
    End Function

    ''' <summary>
    ''' The question for a selection. One dialog, one outcome: the environmental reasons
    ''' collapse into a single sentence here, because a list of five different reasons
    ''' for one Yes/No is noise, not honesty.
    ''' </summary>
    Friend Function ConfirmMany(decision As DeleteDecision, count As Integer) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("Удалить {0} файл(ов) в Корзину?", count)
        End If

        If decision.Reason = PermanentReason.UserAsked Then
            Return Localization.TF("Удалить {0} файл(ов) безвозвратно, минуя Корзину?", count)
        End If

        Return Localization.TF("{0} файл(ов) будет удалено безвозвратно: Корзина здесь недоступна.", count)
    End Function

    ''' <summary>The status line, which has to agree with the question that preceded it.</summary>
    Friend Function StatusOne(decision As DeleteDecision, filePath As String) As String
        If decision Is Nothing OrElse Not decision.IsPermanent Then
            Return Localization.TF("удалён в Корзину: {0}", filePath)
        End If
        Return Localization.TF("удалён безвозвратно: {0}", filePath)
    End Function

End Module
#End If
