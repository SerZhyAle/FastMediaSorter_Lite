#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

' The one setting of APP-COMMANDS rule 13: "let other programs ask this app to work with my files,
' screen, clipboard or the network". Off until the person turns it on in the Settings window; no
' command reads it as anything but a gate and no command changes it. Its own registry value, not a
' ModernViewerPreferences member: it is never exported into a settings file (.fmscfg), so a profile
' carried to another machine cannot arrive with it switched on.
Namespace AppCommands

    Friend Module AppCommandsSetting

        ' The registry value is spelled as a literal at both calls so the reset-scope gate sees it
        ' (ProfileResetScopeTests): a profile reset turns it back off.
        Friend Const RegistryValue As String = "AllowAppCommands"

        Friend Function IsAllowed() As Boolean
            Try
                Return Interaction.GetSetting(App_name, Second_App_Name, "AllowAppCommands", "0") = "1"
            Catch ex As Exception
                Return False
            End Try
        End Function

        Friend Sub SetAllowed(value As Boolean)
            Interaction.SaveSetting(App_name, Second_App_Name, "AllowAppCommands", If(value, "1", "0"))
        End Sub

    End Module

End Namespace
#End If
