#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 0.27 section 25 item CY:
''' The fifteen check steps for a connection that does not work, in fixed order from machine outward to account.
''' </summary>
Public Enum ConnectionDiagnosisStep
    ''' <summary>1. pc-asleep (any): Switch on and wake the device that runs the server or the sharing service.</summary>
    PcAsleep = 1

    ''' <summary>2. address-lan (network): Check the address, and make sure both devices are on the same Wi-Fi or home network.</summary>
    AddressLan = 2

    ''' <summary>3. guest-network (network): Join the main Wi-Fi network, not the router's guest network.</summary>
    GuestNetwork = 3

    ''' <summary>4. vpn (any): Turn the VPN off on this device for a moment and try again.</summary>
    Vpn = 4

    ''' <summary>5. windows-public-network (network, windowsOnly): On the Windows computer, set this network to private.</summary>
    WindowsPublicNetwork = 5

    ''' <summary>6. ip-family (any): Try another network (IPv4 or IPv6 availability).</summary>
    IpFamily = 6

    ''' <summary>7. server-internet (internet): From another network, check that the server answers from the internet.</summary>
    ServerInternet = 7

    ''' <summary>8. port-forward (internet): In the router's settings, forward the server's port.</summary>
    PortForward = 8

    ''' <summary>9. device-clock (any): Set the right date and time on this device.</summary>
    DeviceClock = 9

    ''' <summary>10. certificate (any): Confirm server certificate fingerprint.</summary>
    Certificate = 10

    ''' <summary>11. credentials (any): Check the login and type the password again.</summary>
    Credentials = 11

    ''' <summary>12. sign-in-rate (any): Wait a few seconds and try again (rate limit).</summary>
    SignInRate = 12

    ''' <summary>13. invitation (any): Check the invitation or ask for a new one.</summary>
    Invitation = 13

    ''' <summary>14. device-removed (any): Sign in to the account again to add this device back.</summary>
    DeviceRemoved = 14

    ''' <summary>15. operator (any): Ask whoever runs the server.</summary>
    [Operator] = 15
End Enum

''' <summary>
''' Where a check step applies (item CY).
''' </summary>
Public Enum ConnectionDiagnosisApplies
    ''' <summary>The target is on the person's own network.</summary>
    Network = 1
    ''' <summary>The target is reached from outside over the internet.</summary>
    Internet = 2
    ''' <summary>Both local network and internet.</summary>
    Any = 3
End Enum

''' <summary>
''' Metadata and helpers for the fifteen check steps (item CY).
''' </summary>
Public Module ConnectionDiagnosisStepInfo

    Public Function IdOf(stepValue As ConnectionDiagnosisStep) As String
        Select Case stepValue
            Case ConnectionDiagnosisStep.PcAsleep : Return "pc-asleep"
            Case ConnectionDiagnosisStep.AddressLan : Return "address-lan"
            Case ConnectionDiagnosisStep.GuestNetwork : Return "guest-network"
            Case ConnectionDiagnosisStep.Vpn : Return "vpn"
            Case ConnectionDiagnosisStep.WindowsPublicNetwork : Return "windows-public-network"
            Case ConnectionDiagnosisStep.IpFamily : Return "ip-family"
            Case ConnectionDiagnosisStep.ServerInternet : Return "server-internet"
            Case ConnectionDiagnosisStep.PortForward : Return "port-forward"
            Case ConnectionDiagnosisStep.DeviceClock : Return "device-clock"
            Case ConnectionDiagnosisStep.Certificate : Return "certificate"
            Case ConnectionDiagnosisStep.Credentials : Return "credentials"
            Case ConnectionDiagnosisStep.SignInRate : Return "sign-in-rate"
            Case ConnectionDiagnosisStep.Invitation : Return "invitation"
            Case ConnectionDiagnosisStep.DeviceRemoved : Return "device-removed"
            Case ConnectionDiagnosisStep.[Operator] : Return "operator"
            Case Else : Return "operator"
        End Select
    End Function

    Public Function ParseId(id As String) As Nullable(Of ConnectionDiagnosisStep)
        If String.IsNullOrWhiteSpace(id) Then Return Nothing
        Select Case id.Trim().ToLowerInvariant()
            Case "pc-asleep" : Return ConnectionDiagnosisStep.PcAsleep
            Case "address-lan" : Return ConnectionDiagnosisStep.AddressLan
            Case "guest-network" : Return ConnectionDiagnosisStep.GuestNetwork
            Case "vpn" : Return ConnectionDiagnosisStep.Vpn
            Case "windows-public-network" : Return ConnectionDiagnosisStep.WindowsPublicNetwork
            Case "ip-family" : Return ConnectionDiagnosisStep.IpFamily
            Case "server-internet" : Return ConnectionDiagnosisStep.ServerInternet
            Case "port-forward" : Return ConnectionDiagnosisStep.PortForward
            Case "device-clock" : Return ConnectionDiagnosisStep.DeviceClock
            Case "certificate" : Return ConnectionDiagnosisStep.Certificate
            Case "credentials" : Return ConnectionDiagnosisStep.Credentials
            Case "sign-in-rate" : Return ConnectionDiagnosisStep.SignInRate
            Case "invitation" : Return ConnectionDiagnosisStep.Invitation
            Case "device-removed" : Return ConnectionDiagnosisStep.DeviceRemoved
            Case "operator" : Return ConnectionDiagnosisStep.[Operator]
            Case Else : Return Nothing
        End Select
    End Function

    Public Function AppliesOf(stepValue As ConnectionDiagnosisStep) As ConnectionDiagnosisApplies
        Select Case stepValue
            Case ConnectionDiagnosisStep.AddressLan,
                 ConnectionDiagnosisStep.GuestNetwork,
                 ConnectionDiagnosisStep.WindowsPublicNetwork
                Return ConnectionDiagnosisApplies.Network
            Case ConnectionDiagnosisStep.ServerInternet,
                 ConnectionDiagnosisStep.PortForward
                Return ConnectionDiagnosisApplies.Internet
            Case Else
                Return ConnectionDiagnosisApplies.Any
        End Select
    End Function

    Public Function IsWindowsOnly(stepValue As ConnectionDiagnosisStep) As Boolean
        Return stepValue = ConnectionDiagnosisStep.WindowsPublicNetwork
    End Function

    ''' <summary>
    ''' Spec 232 rule 4: where Windows has a settings page for the fix, returns the ms-settings: URI.
    ''' </summary>
    Public Function WindowsSettingsUriOf(stepValue As ConnectionDiagnosisStep) As String
        Select Case stepValue
            Case ConnectionDiagnosisStep.WindowsPublicNetwork
                Return "ms-settings:network-status"
            Case ConnectionDiagnosisStep.DeviceClock
                Return "ms-settings:dateandtime"
            Case Else
                Return String.Empty
        End Select
    End Function

End Module
#End If
