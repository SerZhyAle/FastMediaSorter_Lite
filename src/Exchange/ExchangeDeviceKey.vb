#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Buffers.Binary
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text

' Spec 234 T-1, DEVICE-EXCHANGE section 27 items DF and DG. The device key of the sealed-content scheme: one P-256 key
' pair per deviceId, its public half written as the 65-byte uncompressed SEC1 point in base64url without padding (the
' `kem` of `e2eKey`), and the check code a person may compare between two screens.
'
' The private half lives in this object only. It never has a ToString, a log line or an exception message: the one way
' out is ExportPrivateScalar, Friend, for the key store (ExchangeDeviceKeyStore, DPAPI) and for tests. The ECDH goes
' through the platform (CNG), so the secret is not held in managed arithmetic of ours.

''' <summary>Base64url without padding, the form of every binary member of the sealed scheme. The reader is strict:
''' the alphabet only, no padding, and the canonical tail, so one byte string has exactly one spelling.</summary>
Friend NotInheritable Class ExchangeBase64Url

    Private Sub New()
    End Sub

    Public Shared Function Encode(bytes As Byte()) As String
        If bytes Is Nothing Then Return ""
        Return Convert.ToBase64String(bytes).TrimEnd("="c).Replace("+"c, "-"c).Replace("/"c, "_"c)
    End Function

    ''' <summary>False for an empty string, a character outside the alphabet, padding, an impossible length or a
    ''' non-canonical last character.</summary>
    Public Shared Function TryDecode(text As String, ByRef bytes As Byte()) As Boolean
        bytes = Nothing
        If String.IsNullOrEmpty(text) OrElse text.Length Mod 4 = 1 Then Return False
        For Each c As Char In text
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
            If Not ok Then Return False
        Next
        Dim padded As String = text.Replace("-"c, "+"c).Replace("_"c, "/"c) & New String("="c, (4 - text.Length Mod 4) Mod 4)
        Try
            Dim raw As Byte() = Convert.FromBase64String(padded)
            If Not String.Equals(Encode(raw), text, StringComparison.Ordinal) Then Return False
            bytes = raw
            Return True
        Catch ex As FormatException
            Return False
        End Try
    End Function

End Class

''' <summary>A device's P-256 key pair for the sealed scheme (item DF). Dispose it when done; it holds the private key.</summary>
Friend NotInheritable Class ExchangeDeviceKey
    Implements IDisposable

    ''' <summary>The `v` of `e2eKey`.</summary>
    Public Const E2eKeyVersion As Integer = 1
    ''' <summary>The uncompressed SEC1 point: 0x04, X (32 bytes), Y (32 bytes).</summary>
    Public Const PublicKeyLength As Integer = 65
    Public Const PrivateKeyLength As Integer = 32
    Private Const CoordinateLength As Integer = 32

    Private Shared ReadOnly CheckPrefix As Byte() = Encoding.ASCII.GetBytes("fmsx-e2e-v1 check")

    Private ReadOnly _dh As ECDiffieHellman
    Private ReadOnly _public As Byte()

    Private Sub New(dh As ECDiffieHellman)
        _dh = dh
        Dim p As ECParameters = dh.ExportParameters(False)
        _public = EncodePoint(p.Q.X, p.Q.Y)
    End Sub

    ''' <summary>A new random key pair. Nothing is stored: the caller (the key store) decides when it is kept.</summary>
    Public Shared Function Generate() As ExchangeDeviceKey
        Return New ExchangeDeviceKey(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
    End Function

    ''' <summary>The key pair of a private scalar (32 bytes, big-endian, 1 to n-1); Nothing when it is no valid scalar.
    ''' The public point is derived by the platform.</summary>
    Public Shared Function FromPrivateScalar(scalar As Byte()) As ExchangeDeviceKey
        If scalar Is Nothing OrElse scalar.Length <> PrivateKeyLength Then Return Nothing
        Dim p As New ECParameters()
        p.Curve = ECCurve.NamedCurves.nistP256
        p.D = CType(scalar.Clone(), Byte())
        Try
            Return New ExchangeDeviceKey(ECDiffieHellman.Create(p))
        Catch ex As Exception When IsCurveFailure(ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>What the platform throws for a scalar or a point that is not valid on P-256: Windows CNG reports a point off the
    ''' curve as a PlatformNotSupportedException that wraps the CryptographicException.</summary>
    Private Shared Function IsCurveFailure(ex As Exception) As Boolean
        Return TypeOf ex Is CryptographicException OrElse TypeOf ex Is PlatformNotSupportedException OrElse TypeOf ex Is ArgumentException
    End Function

    ''' <summary>The public key as the 65-byte point (a copy).</summary>
    Public ReadOnly Property PublicKey As Byte()
        Get
            Return CType(_public.Clone(), Byte())
        End Get
    End Property

    ''' <summary>The `kem` of `e2eKey`: the public point in base64url without padding.</summary>
    Public ReadOnly Property Kem As String
        Get
            Return ExchangeBase64Url.Encode(_public)
        End Get
    End Property

    ''' <summary>The check code of this key (item DG): twelve decimal digits in three groups of four.</summary>
    Public ReadOnly Property CheckCode As String
        Get
            Return CheckCodeOf(_public)
        End Get
    End Property

    ''' <summary>The private scalar, 32 bytes. For the DPAPI key store and for tests; the caller wipes the array. It is
    ''' never written to a log, a backup, an export or a hand-over (item DF).</summary>
    Friend Function ExportPrivateScalar() As Byte()
        Dim p As ECParameters = _dh.ExportParameters(True)
        Try
            Dim d As Byte() = p.D
            If d Is Nothing OrElse d.Length > PrivateKeyLength Then Return Nothing
            ' The platform may drop leading zero bytes; the stored form is always 32.
            Dim r(PrivateKeyLength - 1) As Byte
            Buffer.BlockCopy(d, 0, r, PrivateKeyLength - d.Length, d.Length)
            Return r
        Finally
            If p.D IsNot Nothing Then Array.Clear(p.D, 0, p.D.Length)
        End Try
    End Function

    ''' <summary>The ECDH of RFC 9180 section 7.1.1 for P-256: the X coordinate of the shared point, 32 bytes; Nothing when
    ''' the peer's 65 bytes are no point of the curve. The caller wipes the result.</summary>
    Friend Function DeriveRaw(peerPublicKey As Byte()) As Byte()
        Dim peerParameters As ECParameters = Nothing
        If Not TryPointParameters(peerPublicKey, peerParameters) Then Return Nothing
        Try
            Using peer As ECDiffieHellman = ECDiffieHellman.Create(peerParameters)
                Using peerKey As ECDiffieHellmanPublicKey = peer.PublicKey
                    Dim raw As Byte() = _dh.DeriveRawSecretAgreement(peerKey)
                    If raw Is Nothing OrElse raw.Length > CoordinateLength Then Return Nothing
                    If raw.Length = CoordinateLength Then Return raw
                    Dim padded(CoordinateLength - 1) As Byte
                    Buffer.BlockCopy(raw, 0, padded, CoordinateLength - raw.Length, raw.Length)
                    Array.Clear(raw, 0, raw.Length)
                    Return padded
                End Using
            End Using
        Catch ex As Exception When IsCurveFailure(ex)
            Return Nothing
        End Try
    End Function

    ' --- public values, no key needed ---

    ''' <summary>True for 65 bytes that are an uncompressed point of P-256 (the platform checks it is on the curve).</summary>
    Public Shared Function IsValidPublicKey(point As Byte()) As Boolean
        Dim p As ECParameters = Nothing
        If Not TryPointParameters(point, p) Then Return False
        Try
            Using k As ECDiffieHellman = ECDiffieHellman.Create(p)
                Return True
            End Using
        Catch ex As Exception When IsCurveFailure(ex)
            Return False
        End Try
    End Function

    ''' <summary>A `kem` (base64url) to its 65 bytes; False when it is not strictly that or not a point of the curve.</summary>
    Public Shared Function TryParseKem(kem As String, ByRef point As Byte()) As Boolean
        point = Nothing
        Dim raw As Byte() = Nothing
        If Not ExchangeBase64Url.TryDecode(kem, raw) Then Return False
        If Not IsValidPublicKey(raw) Then Return False
        point = raw
        Return True
    End Function

    ''' <summary>The check code of a public key (item DG): the first 8 bytes of SHA-256 over the ASCII bytes
    ''' `fmsx-e2e-v1 check` and the 65 bytes of `kem`, as an unsigned big-endian integer, modulo 10^12, as twelve digits
    ''' in three groups of four. "" for anything that is not 65 bytes.</summary>
    Public Shared Function CheckCodeOf(point As Byte()) As String
        If point Is Nothing OrElse point.Length <> PublicKeyLength Then Return ""
        Dim input(CheckPrefix.Length + PublicKeyLength - 1) As Byte
        Buffer.BlockCopy(CheckPrefix, 0, input, 0, CheckPrefix.Length)
        Buffer.BlockCopy(point, 0, input, CheckPrefix.Length, PublicKeyLength)
        Dim hash As Byte() = SHA256.HashData(input)
        Dim value As ULong = BinaryPrimitives.ReadUInt64BigEndian(hash) Mod 1000000000000UL
        Dim digits As String = value.ToString("D12", CultureInfo.InvariantCulture)
        Return digits.Substring(0, 4) & " " & digits.Substring(4, 4) & " " & digits.Substring(8, 4)
    End Function

    ''' <summary>The check code of a `kem` string; "" when it is not a valid key.</summary>
    Public Shared Function CheckCodeOfKem(kem As String) As String
        Dim point As Byte() = Nothing
        If Not TryParseKem(kem, point) Then Return ""
        Return CheckCodeOf(point)
    End Function

    ''' <summary>Never the key; the type name only.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeDeviceKey"
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _dh.Dispose()
    End Sub

    ' --- internals ---

    Private Shared Function EncodePoint(x As Byte(), y As Byte()) As Byte()
        Dim r(PublicKeyLength - 1) As Byte
        r(0) = 4
        Buffer.BlockCopy(x, 0, r, 1 + CoordinateLength - x.Length, x.Length)
        Buffer.BlockCopy(y, 0, r, 1 + CoordinateLength + CoordinateLength - y.Length, y.Length)
        Return r
    End Function

    Private Shared Function TryPointParameters(point As Byte(), ByRef parameters As ECParameters) As Boolean
        If point Is Nothing OrElse point.Length <> PublicKeyLength OrElse point(0) <> 4 Then Return False
        Dim x(CoordinateLength - 1) As Byte
        Dim y(CoordinateLength - 1) As Byte
        Buffer.BlockCopy(point, 1, x, 0, CoordinateLength)
        Buffer.BlockCopy(point, 1 + CoordinateLength, y, 0, CoordinateLength)
        Dim q As New ECPoint()
        q.X = x
        q.Y = y
        Dim p As New ECParameters()
        p.Curve = ECCurve.NamedCurves.nistP256
        p.Q = q
        parameters = p
        Return True
    End Function

End Class
#End If
