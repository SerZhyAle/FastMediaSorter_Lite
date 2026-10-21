Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>UI ownership only. Routing never enables or starts a component.</summary>
Public Enum SettingsDestination
    Manager
    Sftp
    ExchangeServer
End Enum

Public NotInheritable Class SettingsRoutes
    Private Sub New()
    End Sub

    Public Shared Function Destination(page As String) As SettingsDestination
        Select Case page
            Case "network", "hosting" : Return SettingsDestination.Sftp
            Case "exchange" : Return SettingsDestination.ExchangeServer
            Case Else : Return SettingsDestination.Manager
        End Select
    End Function

    Public Shared Function Owns(destination As SettingsDestination, page As String) As Boolean
        Return SettingsRoutes.Destination(page) = destination
    End Function

    Public Shared Function Available(destination As SettingsDestination, sftpInstalled As Boolean, exchangeInstalled As Boolean) As Boolean
        Select Case destination
            Case SettingsDestination.Sftp : Return sftpInstalled
            Case SettingsDestination.ExchangeServer : Return exchangeInstalled
            Case Else : Return True
        End Select
    End Function

    Public Shared Function ContextKey(destination As SettingsDestination) As String
        Select Case destination
            Case SettingsDestination.Sftp : Return "Share_SftpSettingsContext"
            Case SettingsDestination.ExchangeServer : Return "Share_ExchangeSettingsContext"
            Case Else : Return "Share_ManagerSettingsContext"
        End Select
    End Function

    Public Shared Function BoundsKey(destination As SettingsDestination) As String
        Return ContextKey(destination).Replace("Context", "Bounds")
    End Function
End Class
