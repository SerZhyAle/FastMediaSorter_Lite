Option Strict On

Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' What one share contributes to an export when the share service is registered on an exchange server
''' (ticket 148 T-5): the client-facing endpoint, the share id and the third-party relay port.
'''
''' DORMANT by construction: <see cref="ShareConfigBuilder.Build"/> takes this as an optional argument that
''' no product path passes until contract rung 2 freezes the field set (133 section 8), so an export is
''' byte-identical to today's whenever it is <c>Nothing</c> (contract section 8.5, held by a golden test).
''' </summary>
Public NotInheritable Class ExchangeExport
    ''' <summary>The endpoint other devices dial. Never loopback (the builder re-checks).</summary>
    Public Property ClientHost As String = ""
    Public Property ClientPort As Integer
    ''' <summary>The share id (contract 6.2): 22 base64url characters. A secret link - never logged.</summary>
    Public Property ShareId As String = ""
    ''' <summary>The share's third-party per-session TCP port on the client-facing host, or 0 when the
    ''' server did not allocate one. Published as one more direct endpoint (Q148-1).</summary>
    Public Property RelayPort As Integer
End Class

''' <summary>
''' The wire names this dormant export uses, in ONE place because the contract freezes none of them before
''' rung 2 ("field names, the access-path kind name ... are frozen only at the freeze ladder", ANYWHERE-ACCESS
''' sections 4.1, 4.2 and the closing list). A name that changes at the freeze changes here and nowhere else.
''' </summary>
Public NotInheritable Class ExchangeWireNames
    Private Sub New()
    End Sub

    ''' <summary>The tunnel path's kind (ANYWHERE-ACCESS 4.2 names it; the spelling freezes at rung 2).</summary>
    Public Const TunnelKind As String = "rendezvousTunnel"
    ''' <summary>The member of the tunnel path that carries the share id (FMSCFG 2.4: the optional member
    ''' <c>shareId</c>, 22 base64url characters).</summary>
    Public Const TunnelShareIdMember As String = "shareId"
    ''' <summary>The kind of the third-party relay port. Q148-1: an existing direct kind, because it behaves
    ''' identically and an unchanged client then reaches the share through the relay with no update
    ''' (contract section 8.6). The catalog owner confirms at rung 1.</summary>
    Public Const RelayPortKind As String = "portforward"
End Class

Public NotInheritable Class ExchangeShareIds

    Private Sub New()
    End Sub

    ''' <summary>Contract 6.2: 16 random bytes, base64url without padding = exactly 22 characters of
    ''' <c>A-Z a-z 0-9 - _</c>, and the last character carries only 4 payload bits, so it is one of 16.</summary>
    Public Shared Function IsWellFormed(sid As String) As Boolean
        If sid Is Nothing OrElse sid.Length <> 22 Then Return False
        For Each c As Char In sid
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
            If Not ok Then Return False
        Next
        Return LastCharCarriesOnlyFourBits(sid(21))
    End Function

    ' 22 chars * 6 bits = 132 bits; 128 are payload, so the last character carries 2 payload bits and 4 zero
    ' bits (the Go side decodes with base64.RawURLEncoding.Strict()): its 6-bit value is a multiple of 16,
    ' i.e. A (0), Q (16), g (32) or w (48).
    Private Shared Function LastCharCarriesOnlyFourBits(c As Char) As Boolean
        Return c = "A"c OrElse c = "Q"c OrElse c = "g"c OrElse c = "w"c
    End Function

    ''' <summary>How a share is named in a log (133 R-6): the first 8 hex characters of SHA-256 of its id.
    ''' The id itself never reaches a log line.</summary>
    Public Shared Function LogName(sid As String) As String
        Dim hash As Byte() = SHA256.HashData(Encoding.UTF8.GetBytes(If(sid, "")))
        Return Convert.ToHexString(hash, 0, 4).ToLowerInvariant()
    End Function
End Class
