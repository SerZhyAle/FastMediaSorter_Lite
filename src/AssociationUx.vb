#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>What the two "default viewer / player" buttons actually do in this build.</summary>
Friend Enum AssociationMode
    ''' <summary>The packaged (MSIX) build declares its associations in its manifest; the one thing
    ''' left to the user is choosing the default, which only Windows Settings can do.</summary>
    OpenWindowsSettings = 0
    ''' <summary>The ordinary build writes the per-user HKCU registrations itself.</summary>
    RegisterForCurrentUser = 1
End Enum

''' <summary>
''' One chooser for the caption AND the action of the association buttons (057.S14-2), so the
''' wording cannot promise "Open Windows settings" while the click writes the registry, or the
''' other way round. Pure: the build tells it whether it is packaged.
''' </summary>
Friend Module AssociationUx

    Friend Function ModeFor(isPackaged As Boolean) As AssociationMode
        Return If(isPackaged, AssociationMode.OpenWindowsSettings, AssociationMode.RegisterForCurrentUser)
    End Function

    Friend Function Caption(mode As AssociationMode) As String
        Return If(mode = AssociationMode.OpenWindowsSettings,
                  Localization.T("Открыть параметры Windows"),
                  Localization.T("Зарегистрировать"))
    End Function

End Module
#End If
