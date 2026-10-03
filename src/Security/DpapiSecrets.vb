Option Strict On

Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' Cloud API keys must never be stored as plaintext. These helpers encrypt with
''' DPAPI under the current-user scope and store a base64 blob in app settings.
''' Keys are never written to logs or status labels.
''' </summary>
Public Module DpapiSecrets

    ''' <summary>Encrypts plaintext to a base64 DPAPI blob ("" for empty input).</summary>
    Public Function Protect(plain As String) As String
        If String.IsNullOrEmpty(plain) Then Return ""
        Try
            Dim data As Byte() = Encoding.UTF8.GetBytes(plain)
            Dim blob As Byte() = ProtectedData.Protect(data, Nothing, DataProtectionScope.CurrentUser)
            Return Convert.ToBase64String(blob)
        Catch ex As Exception
            ' The failure, never the value: neither exception message carries the key.
            AppFileLogger.WriteLine("DPAPI: protect failed - " & ex.GetType().Name & ": " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Decrypts a base64 DPAPI blob back to plaintext ("" on failure).</summary>
    Public Function Unprotect(protectedBase64 As String) As String
        If String.IsNullOrEmpty(protectedBase64) Then Return ""
        Try
            Dim blob As Byte() = Convert.FromBase64String(protectedBase64)
            Dim data As Byte() = ProtectedData.Unprotect(blob, Nothing, DataProtectionScope.CurrentUser)
            Return Encoding.UTF8.GetString(data)
        Catch ex As Exception
            ' A profile restored or migrated to another machine lands here: the key field comes
            ' up empty, and this line is the only trace of why (audit 057.S10-6).
            AppFileLogger.WriteLine("DPAPI: the stored API key could not be decrypted - " & ex.GetType().Name & ": " & ex.Message)
            Return ""
        End Try
    End Function

End Module

''' <summary>What protecting or unprotecting a secret came to - three outcomes a caller
''' must not collapse: "nothing to protect" is not a failure, and "protection failed" must
''' never be saved as if an empty blob were the secret (100 §15.4 item 6).</summary>
Public Enum SecretProtectionStatus
    EmptyInput = 0
    Success = 1
    ProtectionFailed = 2
End Enum

Public NotInheritable Class SecretProtectionResult
    Friend Sub New(status As SecretProtectionStatus, value As String)
        Me.Status = status
        Me.Value = If(value, "")
    End Sub

    Public ReadOnly Property Status As SecretProtectionStatus
    ''' <summary>The base64 blob (protect) or the plaintext (unprotect); "" unless Success.</summary>
    Public ReadOnly Property Value As String

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Status = SecretProtectionStatus.Success
        End Get
    End Property

    ''' <summary>Never the value.</summary>
    Public Overrides Function ToString() As String
        Return Status.ToString()
    End Function
End Class

''' <summary>The result-returning form of <see cref="DpapiSecrets"/>. The existing
''' Protect/Unprotect keep their "empty string on failure" contract, which the cloud API
''' key code relies on; a caller that must tell failure from empty uses these. The cipher
''' is injectable so the failure paths are testable without a broken DPAPI.</summary>
Public Module DpapiSecretChecks

    Public Function ProtectChecked(plain As String, Optional cipher As Func(Of Byte(), Byte()) = Nothing) As SecretProtectionResult
        If String.IsNullOrEmpty(plain) Then Return New SecretProtectionResult(SecretProtectionStatus.EmptyInput, "")
        Try
            Dim encrypt As Func(Of Byte(), Byte()) = If(cipher, Function(data As Byte()) ProtectedData.Protect(data, Nothing, DataProtectionScope.CurrentUser))
            Dim blob As Byte() = encrypt(Encoding.UTF8.GetBytes(plain))
            ' A non-empty secret that produced no blob is a failure, whatever the API said.
            If blob Is Nothing OrElse blob.Length = 0 Then Return New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, "")
            Return New SecretProtectionResult(SecretProtectionStatus.Success, Convert.ToBase64String(blob))
        Catch ex As Exception
            AppFileLogger.WriteLine("DPAPI: protect failed - " & ex.GetType().Name)
            Return New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, "")
        End Try
    End Function

    Public Function UnprotectChecked(protectedBase64 As String, Optional cipher As Func(Of Byte(), Byte()) = Nothing) As SecretProtectionResult
        If String.IsNullOrEmpty(protectedBase64) Then Return New SecretProtectionResult(SecretProtectionStatus.EmptyInput, "")
        Try
            Dim decrypt As Func(Of Byte(), Byte()) = If(cipher, Function(data As Byte()) ProtectedData.Unprotect(data, Nothing, DataProtectionScope.CurrentUser))
            Dim plain As Byte() = decrypt(Convert.FromBase64String(protectedBase64))
            If plain Is Nothing OrElse plain.Length = 0 Then Return New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, "")
            Return New SecretProtectionResult(SecretProtectionStatus.Success, Encoding.UTF8.GetString(plain))
        Catch ex As Exception
            ' The caller re-prompts; the stored blob is left exactly as it was.
            AppFileLogger.WriteLine("DPAPI: a stored secret could not be decrypted - " & ex.GetType().Name)
            Return New SecretProtectionResult(SecretProtectionStatus.ProtectionFailed, "")
        End Try
    End Function

End Module
