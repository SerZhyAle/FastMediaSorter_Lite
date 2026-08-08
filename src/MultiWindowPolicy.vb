Option Strict On

Imports Microsoft.VisualBasic

''' <summary>
''' Decides the process-wide multi-window policy before any form exists. The value is
''' deliberately read once: changing the checkbox affects only processes started after
''' the change, while the mutex decides whether this process is primary or secondary.
''' </summary>
Friend Module MultiWindowPolicy

    Private allowNewWindowsValue As Boolean
    Private allowNewWindowsRead As Boolean
    Private secondaryInstanceValue As Boolean
    Private instanceRoleMarked As Boolean

    Friend Function AllowNewWindows() As Boolean
#If NETFRAMEWORK Then
        Return False
#Else
        If Not allowNewWindowsRead Then
            allowNewWindowsValue = Interaction.GetSetting(App_name, Second_App_Name, "AllowNewWindows", "0") = "1"
            allowNewWindowsRead = True
        End If
        Return allowNewWindowsValue
#End If
    End Function

    Friend Sub MarkInstanceRole(createdNew As Boolean)
#If Not NETFRAMEWORK Then
        secondaryInstanceValue = Not createdNew
        instanceRoleMarked = True
#End If
    End Sub

    Friend Function IsSecondaryInstance() As Boolean
#If NETFRAMEWORK Then
        Return False
#Else
        Return instanceRoleMarked AndAlso secondaryInstanceValue
#End If
    End Function

    Friend Function InstanceRoleName() As String
        Return If(IsSecondaryInstance(), "secondary", "primary")
    End Function
End Module
