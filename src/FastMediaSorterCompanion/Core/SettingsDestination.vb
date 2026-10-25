Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

''' <summary>UI ownership only. Routing never enables or starts a component. Two windows own pages (specification 253): the
''' Manager window, which also holds the SFTP sharing pages, and the local Media Exchange Server window.</summary>
Public Enum SettingsDestination
    Manager
    ExchangeServer
End Enum

Public NotInheritable Class SettingsRoutes
    Private Sub New()
    End Sub

    ''' <summary>The record of the retired SFTP sharing window: read once as a seed for the Manager window, never rewritten
    ''' and never deleted (253 R-7).</summary>
    Public Const SftpContextKey As String = "Share_SftpSettingsContext"

    ''' <summary>The record of the retired single combined window: a read-only seed.</summary>
    Public Const LegacyContextKey As String = "Share_SettingsContext"

    ''' <summary>Its window bounds, the read-only seed of a window with no bounds of its own.</summary>
    Public Const LegacyBoundsKey As String = "Share_SettingsBounds"

    Public Shared Function Destination(page As String) As SettingsDestination
        Select Case page
            Case "exchange" : Return SettingsDestination.ExchangeServer
            Case Else : Return SettingsDestination.Manager
        End Select
    End Function

    Public Shared Function Owns(destination As SettingsDestination, page As String) As Boolean
        Return SettingsRoutes.Destination(page) = destination
    End Function

    ''' <summary>True for the pages of the SFTP sharing component; they live in the Manager window but exist only while the
    ''' component is installed (253 R-4).</summary>
    Public Shared Function IsSftpPage(page As String) As Boolean
        Return page = "network" OrElse page = "hosting"
    End Function

    ''' <summary>Whether a window can open at all. The Manager window is always available.</summary>
    Public Shared Function Available(destination As SettingsDestination, exchangeInstalled As Boolean) As Boolean
        Select Case destination
            Case SettingsDestination.ExchangeServer : Return exchangeInstalled
            Case Else : Return True
        End Select
    End Function

    ''' <summary>Whether a page can be shown: availability is decided by the component that owns the page, not by the window
    ''' that holds it (253 R-4). An empty page asks for the window itself.</summary>
    Public Shared Function PageAvailable(page As String, sftpInstalled As Boolean, exchangeInstalled As Boolean) As Boolean
        If IsSftpPage(page) Then Return sftpInstalled
        Return Available(Destination(page), exchangeInstalled)
    End Function

    Public Shared Function ContextKey(destination As SettingsDestination) As String
        Select Case destination
            Case SettingsDestination.ExchangeServer : Return "Share_ExchangeSettingsContext"
            Case Else : Return "Share_ManagerSettingsContext"
        End Select
    End Function

    Public Shared Function BoundsKey(destination As SettingsDestination) As String
        Return ContextKey(destination).Replace("Context", "Bounds")
    End Function
End Class
