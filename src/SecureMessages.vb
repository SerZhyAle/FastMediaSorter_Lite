#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The one place an FDSEC outcome class becomes words
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §3.4, §14 Ф-S2).
'''
''' Three classes, three messages, never merged. This is the part of the feature most
''' likely to be quietly "improved" into a single friendly sentence, and doing so is a
''' defect: a message that says "wrong password" for what might be damage is lying half
''' the time, and so is the reverse.
'''
''' It lives here rather than inside Main_Form so that "the mapping is exhaustive BY TYPE,
''' not by a chain of Ifs" is something a test can hold it to - and it lives outside
''' src/Secure because that module has no product dependencies at all, and a translated
''' string is one.
'''
''' Nothing here ever names a file. The sealed true name is the thing the format exists to
''' hide (FDSEC-BEHAVIOUR §6 rule 8), so a message about a failure says what happened and
''' not what it happened to.
''' </summary>
Friend Module SecureMessages

    ''' <summary>The user-visible sentence for one outcome class.</summary>
    Friend Function ForOutcome(outcome As FdSecOutcome) As String
        Select Case outcome
            Case FdSecOutcome.Ok
                Return Localization.T("Контейнер открыт")

            Case FdSecOutcome.CredentialOrTamper
                ' All three readings, choosing none. With no marker and no stored verifier,
                ' "this file is not one of ours" is indistinguishable from "your password is
                ' wrong", and that is a security property rather than a gap to paper over.
                Return Localization.T("Не открылось: пароль может быть неверным, файл может не быть контейнером, либо его изменили.")

            Case FdSecOutcome.Damaged
                ' And the password is NOT blamed here: the credential has already proven
                ' itself by the time anything can be called damage.
                Return Localization.T("Файл повреждён или неполон")

            Case FdSecOutcome.Unsupported
                ' Refused by name, never called damage - a container from a newer version of
                ' the format is not a broken one.
                Return Localization.T("Этот контейнер создан более новой версией формата")

            Case Else
                ' A class added to the enum and not to this map lands here, which says
                ' nothing it cannot support. The test walks the enum so it never gets to.
                Return Localization.T("Не удалось показать содержимое")
        End Select
    End Function

End Module
#End If
