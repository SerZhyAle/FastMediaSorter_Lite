#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>
''' The kind of message in the common diagnosis table (item CZ).
''' </summary>
Public Enum ConnectionDiagnosisKind
    Wire = 1
    Cast = 2
    Local = 3
    State = 4
    Result = 5
End Enum

''' <summary>
''' Spec 232 / DEVICE-EXCHANGE 1.1 section 9 items CZ, DD, DR:
''' One row of the common connection message table.
''' </summary>
Public NotInheritable Class ConnectionDiagnosisMessage

    Public ReadOnly Property Code As String
    Public ReadOnly Property Kind As ConnectionDiagnosisKind
    Public ReadOnly Property StepValue As Nullable(Of ConnectionDiagnosisStep)
    Public ReadOnly Property IsWalked As Boolean

    Public Sub New(code As String, kind As ConnectionDiagnosisKind, stepValue As Nullable(Of ConnectionDiagnosisStep), isWalked As Boolean)
        Me.Code = If(code, String.Empty)
        Me.Kind = kind
        Me.StepValue = stepValue
        Me.IsWalked = isWalked
    End Sub

    Public Overrides Function ToString() As String
        Return Code
    End Function

End Class

''' <summary>
''' The canonical table of connection messages and mappings from wire/local failure codes.
''' </summary>
Public NotInheritable Class ConnectionDiagnosisMessages

    Private Sub New()
    End Sub

    Public Shared ReadOnly BadCredentials As New ConnectionDiagnosisMessage("bad-credentials", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Credentials, False)
    Public Shared ReadOnly BadCredentialsCode As New ConnectionDiagnosisMessage("bad-credentials-code", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Credentials, False)
    Public Shared ReadOnly DeviceRevoked As New ConnectionDiagnosisMessage("device-revoked", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.DeviceRemoved, False)
    Public Shared ReadOnly SignedOut As New ConnectionDiagnosisMessage("signed-out", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Credentials, False)
    Public Shared ReadOnly VersionUnsupported As New ConnectionDiagnosisMessage("version-unsupported", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly TlsRequired As New ConnectionDiagnosisMessage("tls-required", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly IdCollision As New ConnectionDiagnosisMessage("id-collision", ConnectionDiagnosisKind.Wire, Nothing, False)
    Public Shared ReadOnly Capacity As New ConnectionDiagnosisMessage("capacity", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly PortUnavailable As New ConnectionDiagnosisMessage("port-unavailable", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly Unavailable As New ConnectionDiagnosisMessage("unavailable", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.PcAsleep, False)
    Public Shared ReadOnly RateLimited As New ConnectionDiagnosisMessage("rate-limited", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.SignInRate, False)
    Public Shared ReadOnly ProtectionUnsupported As New ConnectionDiagnosisMessage("protection-unsupported", ConnectionDiagnosisKind.Wire, Nothing, False)
    Public Shared ReadOnly InvitationInvalid As New ConnectionDiagnosisMessage("invitation-invalid", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Invitation, False)
    Public Shared ReadOnly LoginTaken As New ConnectionDiagnosisMessage("login-taken", ConnectionDiagnosisKind.Wire, Nothing, False)
    Public Shared ReadOnly PasswordPolicy As New ConnectionDiagnosisMessage("password-policy", ConnectionDiagnosisKind.Wire, Nothing, False)
    Public Shared ReadOnly LimitReached As New ConnectionDiagnosisMessage("limit-reached", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly LimitReachedClipboard As New ConnectionDiagnosisMessage("limit-reached-clipboard", ConnectionDiagnosisKind.Wire, Nothing, False)
    Public Shared ReadOnly NotPermitted As New ConnectionDiagnosisMessage("not-permitted", ConnectionDiagnosisKind.Wire, Nothing, False)

    Public Shared ReadOnly Declined As New ConnectionDiagnosisMessage("declined", ConnectionDiagnosisKind.Cast, Nothing, False)
    Public Shared ReadOnly Timeout As New ConnectionDiagnosisMessage("timeout", ConnectionDiagnosisKind.Cast, Nothing, False)
    Public Shared ReadOnly Unsupported As New ConnectionDiagnosisMessage("unsupported", ConnectionDiagnosisKind.Cast, Nothing, False)

    Public Shared ReadOnly UnreachableServer As New ConnectionDiagnosisMessage("unreachable-server", ConnectionDiagnosisKind.Local, ConnectionDiagnosisStep.PcAsleep, True)
    Public Shared ReadOnly UnreachableDevice As New ConnectionDiagnosisMessage("unreachable-device", ConnectionDiagnosisKind.Local, ConnectionDiagnosisStep.PcAsleep, True)
    Public Shared ReadOnly Unanswered As New ConnectionDiagnosisMessage("unanswered", ConnectionDiagnosisKind.Local, ConnectionDiagnosisStep.Operator, True)
    Public Shared ReadOnly TlsFailed As New ConnectionDiagnosisMessage("tls-failed", ConnectionDiagnosisKind.Local, ConnectionDiagnosisStep.DeviceClock, True)
    Public Shared ReadOnly CertificateChanged As New ConnectionDiagnosisMessage("certificate-changed", ConnectionDiagnosisKind.Local, ConnectionDiagnosisStep.Certificate, False)
    Public Shared ReadOnly EmptyCredential As New ConnectionDiagnosisMessage("empty-credential", ConnectionDiagnosisKind.Local, Nothing, False)

    Public Shared ReadOnly ServerOffline As New ConnectionDiagnosisMessage("server-offline", ConnectionDiagnosisKind.State, ConnectionDiagnosisStep.PcAsleep, False)
    Public Shared ReadOnly ResourceLocked As New ConnectionDiagnosisMessage("resource-locked", ConnectionDiagnosisKind.State, Nothing, False)
    Public Shared ReadOnly ClipboardEmpty As New ConnectionDiagnosisMessage("clipboard-empty", ConnectionDiagnosisKind.Result, Nothing, False)

    ' Extended states (item DD & DR)
    Public Shared ReadOnly SharePaused As New ConnectionDiagnosisMessage("share-paused", ConnectionDiagnosisKind.State, Nothing, False)
    Public Shared ReadOnly E2eMissing As New ConnectionDiagnosisMessage("e2e-missing", ConnectionDiagnosisKind.State, Nothing, False)
    Public Shared ReadOnly KeyChanged As New ConnectionDiagnosisMessage("key-changed", ConnectionDiagnosisKind.State, Nothing, False)
    Public Shared ReadOnly ServerNoE2e As New ConnectionDiagnosisMessage("server-no-e2e", ConnectionDiagnosisKind.State, ConnectionDiagnosisStep.Operator, False)
    Public Shared ReadOnly SealedFailed As New ConnectionDiagnosisMessage("sealed-failed", ConnectionDiagnosisKind.Local, Nothing, False)

    ' Fallback generic refusal (DEVICE-EXCHANGE 7.10)
    Public Shared ReadOnly Refused As New ConnectionDiagnosisMessage("refused", ConnectionDiagnosisKind.Wire, ConnectionDiagnosisStep.Operator, False)

    Private Shared ReadOnly AllMessages As ConnectionDiagnosisMessage() = {
        BadCredentials, BadCredentialsCode, DeviceRevoked, SignedOut, VersionUnsupported,
        TlsRequired, IdCollision, Capacity, PortUnavailable, Unavailable, RateLimited,
        ProtectionUnsupported, InvitationInvalid, LoginTaken, PasswordPolicy, LimitReached,
        LimitReachedClipboard, NotPermitted, Declined, Timeout, Unsupported,
        UnreachableServer, UnreachableDevice, Unanswered, TlsFailed, CertificateChanged,
        EmptyCredential, ServerOffline, ResourceLocked, ClipboardEmpty,
        SharePaused, E2eMissing, KeyChanged, ServerNoE2e, SealedFailed, Refused
    }

    Public Shared Function OfCode(code As String) As ConnectionDiagnosisMessage
        If String.IsNullOrWhiteSpace(code) Then Return Refused
        Dim normalized As String = code.Trim().ToLowerInvariant()
        For Each msg As ConnectionDiagnosisMessage In AllMessages
            If String.Equals(msg.Code, normalized, StringComparison.OrdinalIgnoreCase) Then
                Return msg
            End If
        Next
        Return Refused
    End Function

    ''' <summary>
    ''' Resolves wire refusal reason to message, taking oneTimeCode and clipboardSend contexts into account.
    ''' </summary>
    Public Shared Function OfWire(reason As String, Optional oneTimeCode As Boolean = False, Optional clipboardSend As Boolean = False) As ConnectionDiagnosisMessage
        If String.IsNullOrWhiteSpace(reason) Then Return Refused
        Dim r As String = reason.Trim().ToLowerInvariant()
        Select Case r
            Case "bad-credentials"
                Return If(oneTimeCode, BadCredentialsCode, BadCredentials)
            Case "device-revoked", "not-enrolled"
                Return DeviceRevoked
            Case "signed-out"
                Return SignedOut
            Case "version-unsupported"
                Return VersionUnsupported
            Case "tls-required"
                Return TlsRequired
            Case "id-collision"
                Return IdCollision
            Case "capacity"
                Return Capacity
            Case "port-unavailable"
                Return PortUnavailable
            Case "unavailable"
                Return Unavailable
            Case "rate-limited"
                Return RateLimited
            Case "protection-unsupported"
                Return ProtectionUnsupported
            Case "invitation-invalid"
                Return InvitationInvalid
            Case "login-taken"
                Return LoginTaken
            Case "password-policy"
                Return PasswordPolicy
            Case "limit-reached"
                Return If(clipboardSend, LimitReachedClipboard, LimitReached)
            Case "not-permitted"
                Return NotPermitted
            Case Else
                Return Refused
        End Select
    End Function

    Public Shared Function OfCast(reason As String) As ConnectionDiagnosisMessage
        If String.IsNullOrWhiteSpace(reason) Then Return Refused
        Select Case reason.Trim().ToLowerInvariant()
            Case "unavailable" : Return Unavailable
            Case "declined" : Return Declined
            Case "timeout" : Return Timeout
            Case "unsupported" : Return Unsupported
            Case Else : Return Refused
        End Select
    End Function

End Class
#End If
