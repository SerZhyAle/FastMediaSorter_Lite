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
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " dpapi: protect failed: " & ex.Message)
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
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " dpapi: unprotect failed: " & ex.Message)
            Return ""
        End Try
    End Function

End Module
