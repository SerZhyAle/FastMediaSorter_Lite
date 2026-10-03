#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text

''' <summary>
''' The host-key fingerprint in the one form this product stores and compares: <c>SHA256:</c> +
''' standard base64 WITHOUT padding of the SHA-256 of the raw SSH host-key blob (the wire <c>K_S</c>),
''' exactly 32 bytes (OpenSSH's own form; FMSCFG <c>hostKeyFingerprintSha256</c>). 100 §4.4.
'''
''' Strict on purpose: Android's permissive normalizer (hex, padding, a lower-case prefix) is an
''' implementation detail, not wire authority, so a non-canonical value is REFUSED rather than
''' repaired - repairing would make two spellings of one key and two ways to disagree about a pin.
''' Comparison is over the decoded bytes in constant time.
''' </summary>
Friend Module SshFingerprint

    Friend Const Prefix As String = "SHA256:"
    Friend Const DigestBytes As Integer = 32
    ''' <summary>43 base64 characters encode 32 bytes without padding.</summary>
    Friend Const EncodedLength As Integer = 43

    ''' <summary>The canonical text of a host-key blob's fingerprint.</summary>
    Friend Function OfHostKey(hostKeyBlob As Byte()) As String
        If hostKeyBlob Is Nothing OrElse hostKeyBlob.Length = 0 Then Throw New ArgumentException("An empty host key has no fingerprint.", NameOf(hostKeyBlob))
        Using sha As SHA256 = SHA256.Create()
            Return Encode(sha.ComputeHash(hostKeyBlob))
        End Using
    End Function

    Friend Function Encode(digest As Byte()) As String
        If digest Is Nothing OrElse digest.Length <> DigestBytes Then Throw New ArgumentException("A SHA-256 digest is 32 bytes.", NameOf(digest))
        Return Prefix & Convert.ToBase64String(digest).TrimEnd("="c)
    End Function

    ''' <summary>True only for the canonical form. <paramref name="digest"/> is the 32 decoded bytes.</summary>
    Friend Function TryParse(text As String, ByRef digest As Byte()) As Boolean
        digest = Nothing
        If text Is Nothing OrElse text.Length <> Prefix.Length + EncodedLength Then Return False
        If Not text.StartsWith(Prefix, StringComparison.Ordinal) Then Return False
        Dim body As String = text.Substring(Prefix.Length)
        ' Standard alphabet only (no URL-safe '-' '_'), no padding, no stray characters.
        For Each ch As Char In body
            If Not ((ch >= "A"c AndAlso ch <= "Z"c) OrElse (ch >= "a"c AndAlso ch <= "z"c) OrElse
                    (ch >= "0"c AndAlso ch <= "9"c) OrElse ch = "+"c OrElse ch = "/"c) Then Return False
        Next
        Dim decoded As Byte()
        Try
            decoded = Convert.FromBase64String(body & "=")
        Catch ex As FormatException
            Return False
        End Try
        If decoded.Length <> DigestBytes Then Return False
        ' Non-zero trailing bits would decode to the same bytes under a different spelling.
        If Not String.Equals(Convert.ToBase64String(decoded).TrimEnd("="c), body, StringComparison.Ordinal) Then Return False
        digest = decoded
        Return True
    End Function

    ''' <summary>Constant-time equality of two canonical fingerprints (false if either is malformed).</summary>
    Friend Function AreEqual(a As String, b As String) As Boolean
        Dim da As Byte() = Nothing
        Dim db As Byte() = Nothing
        If Not TryParse(a, da) OrElse Not TryParse(b, db) Then Return False
        Return CryptographicOperations.FixedTimeEquals(da, db)
    End Function

    ''' <summary>Constant-time comparison of a pinned canonical value against raw host-key bytes.</summary>
    Friend Function MatchesHostKey(pinned As String, hostKeyBlob As Byte()) As Boolean
        Dim pinnedDigest As Byte() = Nothing
        If Not TryParse(pinned, pinnedDigest) OrElse hostKeyBlob Is Nothing OrElse hostKeyBlob.Length = 0 Then Return False
        Using sha As SHA256 = SHA256.Create()
            Return CryptographicOperations.FixedTimeEquals(pinnedDigest, sha.ComputeHash(hostKeyBlob))
        End Using
    End Function

End Module
#End If
