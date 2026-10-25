Option Strict On
Imports System.Security.Cryptography
Imports System.Text

' Specification 200 rule 9, amended by 234: consent is remembered for the server identity, never for a link or content key.
Friend NotInheritable Class ExchangeRelayConsent
    Private Sub New()
    End Sub

    Public Shared Function Accept(server As String, pin As String, read As Func(Of String, String), write As Action(Of String, String), confirm As Func(Of Boolean)) As Boolean
        If String.IsNullOrWhiteSpace(server) Then Return True
        Dim id = "Exchange_RelayConsent_" & Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.ToLowerInvariant() & "|" & pin)))
        If read.Invoke(id) = "1" Then Return True
        If Not confirm.Invoke() Then Return False
        write.Invoke(id, "1")
        Return True
    End Function
End Class
