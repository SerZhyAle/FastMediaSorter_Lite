#If Not NETFRAMEWORK Then
Option Strict On

Imports Microsoft.Win32

' Ticket 180 section 3. The account entry of this Windows user: which server, which login, the pin, the device id and
' name. None of it is secret (the token is, and it lives in ExchangeCredentialStore under DPAPI).
'
' The values are kept beside the product's other settings (HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter,
' the hive the VB SaveSetting/GetSetting calls of both programs use) under names of their own. They are NEVER added to
' the log package's allow-list (LogPackage.SafeSettingNames): a new setting is withheld from a report until someone adds
' it on purpose, and these are the kind that should stay withheld.
'
' Dormant rule (177 section 2): reading opens a key read-only and creates nothing; the device id is created when the
' first account is configured (EnsureDeviceId from the enrollment), not at start.

''' <summary>A string store, injectable. Reading never creates a key or a value.</summary>
Friend Interface IExchangeSettingsPort
    ''' <summary>The value, or "" when it is absent.</summary>
    Function GetValue(name As String) As String
    Sub SetValue(name As String, value As String)
    Sub DeleteValue(name As String)
End Interface

Friend NotInheritable Class ExchangeRegistrySettings
    Implements IExchangeSettingsPort

    Private Const KeyPath As String = "Software\VB and VBA Program Settings\SZA\FastMediaSorter"

    Public Function GetValue(name As String) As String Implements IExchangeSettingsPort.GetValue
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(KeyPath, False)
                If key Is Nothing Then Return ""
                Dim v As Object = key.GetValue(name, "")
                Return If(TryCast(v, String), "")
            End Using
        Catch ex As Exception When TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is IO.IOException
            Return ""
        End Try
    End Function

    Public Sub SetValue(name As String, value As String) Implements IExchangeSettingsPort.SetValue
        Using key As RegistryKey = Registry.CurrentUser.CreateSubKey(KeyPath)
            key.SetValue(name, value, RegistryValueKind.String)
        End Using
    End Sub

    Public Sub DeleteValue(name As String) Implements IExchangeSettingsPort.DeleteValue
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(KeyPath, True)
                If key IsNot Nothing Then key.DeleteValue(name, False)
            End Using
        Catch ex As Exception When TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is IO.IOException
        End Try
    End Sub
End Class

''' <summary>What the account entry holds about the certificate (item D of section 14.3): a leaf the platform validates is
''' recorded as such and not pinned; one it does not is pinned after the user accepted its fingerprint.</summary>
Friend Enum ExchangeCertificateKind
    ''' <summary>Nothing recorded yet.</summary>
    None = 0
    ''' <summary>Validated by the platform trust store: no pin (a renewal must not lock the device out).</summary>
    CaSigned = 1
    ''' <summary>Self-signed or a private CA: pinned.</summary>
    Pinned = 2
End Enum

''' <summary>The account entry. Immutable.</summary>
Friend NotInheritable Class ExchangeAccount

    Public ReadOnly Server As String
    Public ReadOnly Login As String
    Public ReadOnly Pin As String
    Public ReadOnly Certificate As ExchangeCertificateKind
    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String

    Public Sub New(server As String, login As String, pin As String, certificate As ExchangeCertificateKind, deviceId As String, deviceName As String)
        Me.Server = If(server, "")
        Me.Login = If(login, "")
        Me.Pin = If(pin, "")
        Me.Certificate = certificate
        Me.DeviceId = If(deviceId, "")
        Me.DeviceName = If(deviceName, "")
    End Sub

    ''' <summary>The address, or False when the stored text does not parse (a hand-edited value reaches nothing).</summary>
    Public Function TryGetAddress(ByRef address As ExchangeServerAddress) As Boolean
        Return ExchangeServerAddress.TryParse(Server, address)
    End Function

    Public Function WithPin(newPin As String, kind As ExchangeCertificateKind) As ExchangeAccount
        Return New ExchangeAccount(Server, Login, newPin, kind, DeviceId, DeviceName)
    End Function

    Public Function WithDeviceName(name As String) As ExchangeAccount
        Return New ExchangeAccount(Server, Login, Pin, Certificate, DeviceId, name)
    End Function

    ''' <summary>The login and the server are not secrets, but the text still names no member.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeAccount"
    End Function

End Class

Friend NotInheritable Class ExchangeAccountStore

    Friend Const DeviceIdName As String = "Exchange_DeviceId"
    Friend Const DeviceNameName As String = "Exchange_DeviceName"
    Friend Const ServerName As String = "Exchange_AccountServer"
    Friend Const LoginName As String = "Exchange_AccountLogin"
    Friend Const PinName As String = "Exchange_AccountPin"
    Friend Const CertificateName As String = "Exchange_AccountCertificate"

    ''' <summary>Every value this store owns, so a test can assert that a dormant run wrote none of them.</summary>
    Friend Shared ReadOnly AllNames As String() = {DeviceIdName, DeviceNameName, ServerName, LoginName, PinName, CertificateName}

    Private ReadOnly _port As IExchangeSettingsPort

    Public Sub New(port As IExchangeSettingsPort)
        _port = If(port, New ExchangeRegistrySettings())
    End Sub

    Public Shared Function CreateDefault() As ExchangeAccountStore
        Return New ExchangeAccountStore(New ExchangeRegistrySettings())
    End Function

    ''' <summary>The account entry, or Nothing when none is configured. A pure read.</summary>
    Public Function Load() As ExchangeAccount
        Dim server As String = _port.GetValue(ServerName)
        Dim login As String = _port.GetValue(LoginName)
        Dim deviceId As String = _port.GetValue(DeviceIdName)
        If server.Length = 0 OrElse login.Length = 0 OrElse Not ExchangeIds.IsValidId(deviceId) Then Return Nothing
        Dim kind As ExchangeCertificateKind = ExchangeCertificateKind.None
        Select Case _port.GetValue(CertificateName)
            Case "ca-signed" : kind = ExchangeCertificateKind.CaSigned
            Case "pinned" : kind = ExchangeCertificateKind.Pinned
        End Select
        Dim pin As String = _port.GetValue(PinName)
        If Not ExchangeTrust.IsFingerprint(pin) Then pin = ""
        Return New ExchangeAccount(server, login, pin, kind, deviceId, _port.GetValue(DeviceNameName))
    End Function

    Public Function IsConfigured() As Boolean
        Return Load() IsNot Nothing
    End Function

    ''' <summary>The stored device id, or a fresh one that is NOT stored: an enrollment that fails leaves no value behind, and a
    ''' successful one saves the id with the account entry (<see cref="Save"/>).</summary>
    Public Function ProposeDeviceId() As String
        Dim id As String = _port.GetValue(DeviceIdName)
        Return If(ExchangeIds.IsValidId(id), id, ExchangeIds.NewId())
    End Function

    ''' <summary>The device id of this Windows user, created once on first use (a Windows user is a device).</summary>
    Public Function EnsureDeviceId() As String
        Dim id As String = _port.GetValue(DeviceIdName)
        If ExchangeIds.IsValidId(id) Then Return id
        id = ExchangeIds.NewId()
        _port.SetValue(DeviceIdName, id)
        Return id
    End Function

    Public Sub Save(account As ExchangeAccount)
        _port.SetValue(DeviceIdName, account.DeviceId)
        _port.SetValue(DeviceNameName, account.DeviceName)
        _port.SetValue(ServerName, account.Server)
        _port.SetValue(LoginName, account.Login)
        _port.SetValue(PinName, account.Pin)
        _port.SetValue(CertificateName, If(account.Certificate = ExchangeCertificateKind.CaSigned, "ca-signed", If(account.Certificate = ExchangeCertificateKind.Pinned, "pinned", "")))
    End Sub

    ''' <summary>"Disconnect": the entry is forgotten. The device id stays - it identifies this Windows user, not the account, and a
    ''' later enrollment with the same id rotates the token instead of adding a second device.</summary>
    Public Sub Forget()
        For Each name As String In {DeviceNameName, ServerName, LoginName, PinName, CertificateName}
            _port.DeleteValue(name)
        Next
    End Sub

    ''' <summary>Replaces only the pin: the one explicit act that lets a changed certificate in (item D).</summary>
    Public Sub ReplacePin(pin As String, kind As ExchangeCertificateKind)
        _port.SetValue(PinName, pin)
        _port.SetValue(CertificateName, If(kind = ExchangeCertificateKind.CaSigned, "ca-signed", If(kind = ExchangeCertificateKind.Pinned, "pinned", "")))
    End Sub

End Class

''' <summary>What this device says about itself in `enroll` (6.1). Built by the agent, held verbatim by the wire.</summary>
Friend NotInheritable Class ExchangeDeviceDescription

    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    Public ReadOnly Product As String
    Public ReadOnly ProductVersion As String
    Public ReadOnly Platform As String
    Public ReadOnly Roles As IReadOnlyList(Of String)
    ''' <summary>The `receiver` block as raw JSON, or "" when the device does not declare the role.</summary>
    Public ReadOnly ReceiverJson As String

    Public Sub New(deviceId As String, deviceName As String, product As String, productVersion As String, platform As String,
                   roles As IEnumerable(Of String), receiverJson As String)
        Me.DeviceId = deviceId
        Me.DeviceName = deviceName
        Me.Product = product
        Me.ProductVersion = productVersion
        Me.Platform = platform
        Me.Roles = New List(Of String)(If(roles, Array.Empty(Of String)()))
        Me.ReceiverJson = If(receiverJson, "")
    End Sub

    ''' <summary>The server bounds a name at 64 characters and a platform at 32; a longer one would close the stream, so it is cut here.</summary>
    Public Shared Function Clip(text As String, max As Integer) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Dim t As String = text.Trim()
        Dim sb As New System.Text.StringBuilder()
        For Each c As Char In t
            ' control characters are refused by the server, so they never leave here
            If Not Char.IsControl(c) Then sb.Append(c)
        Next
        Dim r As String = sb.ToString()
        If r.Length <= max Then Return r
        r = r.Substring(0, max)
        ' do not cut a surrogate pair in half
        If r.Length > 0 AndAlso Char.IsHighSurrogate(r(r.Length - 1)) Then r = r.Substring(0, r.Length - 1)
        Return r
    End Function

    ''' <summary>"windows 10.0.26200": the platform names the Windows version (a short, fixed form).</summary>
    Public Shared Function PlatformText() As String
        Dim v As Version = Environment.OSVersion.Version
        Return Clip("windows " & v.Major.ToString(Globalization.CultureInfo.InvariantCulture) & "." &
                    v.Minor.ToString(Globalization.CultureInfo.InvariantCulture) & "." &
                    v.Build.ToString(Globalization.CultureInfo.InvariantCulture), 32)
    End Function

End Class

''' <summary>Which roles this device declares: computed from what is installed and switched on, and a role not declared is
''' never asked of the device (6.1). The facts are injected; the production facts answer False for every role whose ticket
''' (170 producer, 171 consumer, 172-173 broadcaster, 174 receiver) is not built, so a role is never declared ahead of its code.</summary>
Friend Interface IExchangeRoleFacts
    Function SharesPublished() As Boolean
    Function ViewerAttaches() As Boolean
    Function CanReceive() As Boolean
    Function LiveBroadcastEnabled() As Boolean
End Interface

Friend NotInheritable Class ExchangeRoles

    Private Sub New()
    End Sub

    Public Shared Function Compute(facts As IExchangeRoleFacts) As List(Of String)
        Dim roles As New List(Of String)
        If facts Is Nothing Then Return roles
        If facts.SharesPublished() Then roles.Add(ExchangeWire.RoleResourceProducer)
        If facts.ViewerAttaches() Then roles.Add(ExchangeWire.RoleResourceConsumer)
        If facts.CanReceive() Then roles.Add(ExchangeWire.RoleReceiver)
        If facts.LiveBroadcastEnabled() Then roles.Add(ExchangeWire.RoleBroadcaster)
        Return roles
    End Function

End Class
#End If
