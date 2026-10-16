#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Security.Cryptography
Imports System.Text

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DH. HPKE (RFC 9180) in the Auth mode, written from the RFC because the
' .NET 10 class library has no HPKE type (checked against System.Security.Cryptography.dll, 10.0.13). One suite only:
' DHKEM(P-256, HKDF-SHA256) 0x0010, HKDF-SHA256 0x0001, AES-128-GCM 0x0001, mode_auth 0x02, single-shot (sequence
' number 0, so the nonce of the one message is base_nonce).
'
' The contract demands the RFC 9180 Appendix A.3 Auth vectors before this seals anything; ExchangeSealedTests replays
' them through the trace hook below (shared secret, key schedule, key, base nonce, the first encryption) and the
' DeriveKeyPair of 7.1.3 for the three ikm values.
'
' Nothing here logs or puts a key into a message. The DH outputs, the shared secret, the key schedule secret, the AEAD
' key and the base nonce are overwritten as soon as they are used (the trace hook keeps them for a test).

''' <summary>The encapsulated key and the ciphertext (with its 16-byte tag) of one single-shot HPKE message.</summary>
Friend NotInheritable Class ExchangeHpkeSealed

    Public ReadOnly Enc As Byte()
    Public ReadOnly Ciphertext As Byte()

    Friend Sub New(enc As Byte(), ciphertext As Byte())
        Me.Enc = enc
        Me.Ciphertext = ciphertext
    End Sub

End Class

''' <summary>Every intermediate value of one seal, as RFC 9180 Appendix A prints them. TEST HOOK: it holds secrets and is
''' made only by SealAuthTraced.</summary>
Friend NotInheritable Class ExchangeHpkeTrace
    Public Enc As Byte()
    Public SharedSecret As Byte()
    Public KeyScheduleContext As Byte()
    Public Secret As Byte()
    Public Key As Byte()
    Public BaseNonce As Byte()
    Public Ciphertext As Byte()
End Class

''' <summary>AES-GCM with a 16-byte tag, the one AEAD of the sealed scheme (AES-128 inside HPKE, AES-256 everywhere else).
''' The output is the ciphertext followed by its tag.</summary>
Friend NotInheritable Class ExchangeAead

    Public Const TagLength As Integer = 16
    Public Const NonceLength As Integer = 12

    Private Sub New()
    End Sub

    Public Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As Byte()
        Dim ct(plaintext.Length - 1) As Byte
        Dim tag(TagLength - 1) As Byte
        Using aes As New AesGcm(key, TagLength)
            aes.Encrypt(nonce, plaintext, ct, tag, aad)
        End Using
        Dim r(plaintext.Length + TagLength - 1) As Byte
        Buffer.BlockCopy(ct, 0, r, 0, ct.Length)
        Buffer.BlockCopy(tag, 0, r, ct.Length, TagLength)
        Return r
    End Function

    ''' <summary>False for a failed tag or input too short to hold one; nothing partial is ever returned.</summary>
    Public Shared Function TryOpen(key As Byte(), nonce As Byte(), aad As Byte(), sealed As Byte(), count As Integer, ByRef plaintext As Byte()) As Boolean
        plaintext = Nothing
        If sealed Is Nothing OrElse count < TagLength OrElse count > sealed.Length Then Return False
        ' A key or nonce of a size AES-GCM does not take is a failed open, not an exception: both can come from a peer's bytes.
        If key Is Nothing OrElse (key.Length <> 16 AndAlso key.Length <> 24 AndAlso key.Length <> 32) Then Return False
        If nonce Is Nothing OrElse nonce.Length <> NonceLength Then Return False
        Dim n As Integer = count - TagLength
        Dim ct(n - 1) As Byte
        Dim tag(TagLength - 1) As Byte
        Buffer.BlockCopy(sealed, 0, ct, 0, n)
        Buffer.BlockCopy(sealed, n, tag, 0, TagLength)
        Dim pt(n - 1) As Byte
        Try
            Using aes As New AesGcm(key, TagLength)
                aes.Decrypt(nonce, ct, tag, pt, aad)
            End Using
        Catch ex As CryptographicException
            Array.Clear(pt, 0, pt.Length)
            Return False
        End Try
        plaintext = pt
        Return True
    End Function

    Public Shared Function TryOpen(key As Byte(), nonce As Byte(), aad As Byte(), sealed As Byte(), ByRef plaintext As Byte()) As Boolean
        Return TryOpen(key, nonce, aad, sealed, If(sealed Is Nothing, 0, sealed.Length), plaintext)
    End Function

End Class

''' <summary>RFC 9180 in the Auth mode with the one suite of the sealed scheme.</summary>
Friend NotInheritable Class ExchangeHpke

    Public Const KemId As Integer = &H10
    Public Const KdfId As Integer = &H1
    Public Const AeadId As Integer = &H1
    Public Const EncLength As Integer = ExchangeDeviceKey.PublicKeyLength
    Public Const KeyLength As Integer = 16
    Public Const SecretLength As Integer = 32
    Private Const ModeAuth As Byte = 2

    Private Shared ReadOnly Version As Byte() = Encoding.ASCII.GetBytes("HPKE-v1")
    Private Shared ReadOnly SuiteIdKem As Byte() = Concat(Encoding.ASCII.GetBytes("KEM"), I2Osp2(KemId))
    Private Shared ReadOnly SuiteIdHpke As Byte() = Concat(Encoding.ASCII.GetBytes("HPKE"), I2Osp2(KemId), I2Osp2(KdfId), I2Osp2(AeadId))
    ' The order of the P-256 group, for DeriveKeyPair.
    Private Shared ReadOnly GroupOrder As Byte() = Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551")

    Private Sub New()
    End Sub

    ''' <summary>SealAuth of RFC 9180 section 6.1: a fresh ephemeral key, `sender` as the authenticating key. Nothing when the
    ''' recipient's 65 bytes are no point of the curve. The sender's private key never leaves its object.</summary>
    Public Shared Function SealAuth(recipientPublicKey As Byte(), sender As ExchangeDeviceKey, info As Byte(), aad As Byte(), plaintext As Byte()) As ExchangeHpkeSealed
        Using ephemeral As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Dim t As ExchangeHpkeTrace = Run(recipientPublicKey, sender, ephemeral, info, aad, plaintext, False)
            If t Is Nothing Then Return Nothing
            Return New ExchangeHpkeSealed(t.Enc, t.Ciphertext)
        End Using
    End Function

    ''' <summary>The same with a given ephemeral key, returning every intermediate value. TEST HOOK for the RFC vectors; a
    ''' fixed ephemeral key must never be used for a real message.</summary>
    Friend Shared Function SealAuthTraced(recipientPublicKey As Byte(), sender As ExchangeDeviceKey, ephemeral As ExchangeDeviceKey, info As Byte(), aad As Byte(), plaintext As Byte()) As ExchangeHpkeTrace
        Return Run(recipientPublicKey, sender, ephemeral, info, aad, plaintext, True)
    End Function

    ''' <summary>OpenAuth of RFC 9180 section 6.1 (AuthDecap, then the key schedule, then the AEAD): False for an `enc` or a
    ''' sender key that is no point, and for a failed tag - the caller cannot tell which, and nothing partial is returned.</summary>
    Public Shared Function TryOpenAuth(enc As Byte(), senderPublicKey As Byte(), recipient As ExchangeDeviceKey, info As Byte(), aad As Byte(), ciphertext As Byte(), ByRef plaintext As Byte()) As Boolean
        plaintext = Nothing
        If enc Is Nothing OrElse senderPublicKey Is Nothing OrElse recipient Is Nothing OrElse ciphertext Is Nothing Then Return False
        If enc.Length <> EncLength OrElse senderPublicKey.Length <> ExchangeDeviceKey.PublicKeyLength Then Return False
        Dim dhE As Byte() = recipient.DeriveRaw(enc)
        Dim dhS As Byte() = recipient.DeriveRaw(senderPublicKey)
        If dhE Is Nothing OrElse dhS Is Nothing Then
            Wipe(dhE)
            Wipe(dhS)
            Return False
        End If
        Dim dh As Byte() = Concat(dhE, dhS)
        Wipe(dhE)
        Wipe(dhS)
        Dim sharedSecret As Byte() = Nothing
        Dim ks As KeySchedule = Nothing
        Try
            sharedSecret = ExtractAndExpand(dh, Concat(enc, recipient.PublicKey, senderPublicKey))
            ks = New KeySchedule(sharedSecret, info)
            Return ExchangeAead.TryOpen(ks.Key, ks.BaseNonce, aad, ciphertext, plaintext)
        Finally
            Wipe(dh)
            Wipe(sharedSecret)
            If ks IsNot Nothing Then ks.Wipe()
        End Try
    End Function

    ''' <summary>DeriveKeyPair of RFC 9180 section 7.1.3 for P-256: the private scalar of an input keying material. TEST HOOK
    ''' (the RFC prints ikm and the resulting key).</summary>
    Friend Shared Function DeriveKeyPairScalar(ikm As Byte()) As Byte()
        Dim prk As Byte() = LabeledExtract(SuiteIdKem, Array.Empty(Of Byte)(), "dkp_prk", ikm)
        Try
            For counter As Integer = 0 To 255
                Dim candidate As Byte() = LabeledExpand(SuiteIdKem, prk, "candidate", New Byte() {CByte(counter)}, 32)
                If IsBelowOrder(candidate) Then Return candidate
                Wipe(candidate)
            Next
            Return Nothing
        Finally
            Wipe(prk)
        End Try
    End Function

    ' --- internals ---

    ''' <summary>The key schedule of section 5.1 for mode_auth with the default (empty) psk and psk_id.</summary>
    Private NotInheritable Class KeySchedule
        Public ReadOnly Context As Byte()
        Public ReadOnly Secret As Byte()
        Public ReadOnly Key As Byte()
        Public ReadOnly BaseNonce As Byte()

        Public Sub New(sharedSecret As Byte(), info As Byte())
            Dim empty As Byte() = Array.Empty(Of Byte)()
            Dim pskIdHash As Byte() = LabeledExtract(SuiteIdHpke, empty, "psk_id_hash", empty)
            Dim infoHash As Byte() = LabeledExtract(SuiteIdHpke, empty, "info_hash", If(info, empty))
            Context = Concat(New Byte() {ModeAuth}, pskIdHash, infoHash)
            Secret = LabeledExtract(SuiteIdHpke, sharedSecret, "secret", empty)
            Key = LabeledExpand(SuiteIdHpke, Secret, "key", Context, KeyLength)
            BaseNonce = LabeledExpand(SuiteIdHpke, Secret, "base_nonce", Context, ExchangeAead.NonceLength)
        End Sub

        Public Sub Wipe()
            ExchangeHpke.Wipe(Secret)
            ExchangeHpke.Wipe(Key)
            ExchangeHpke.Wipe(BaseNonce)
        End Sub
    End Class

    Private Shared Function Run(recipientPublicKey As Byte(), sender As ExchangeDeviceKey, ephemeral As ExchangeDeviceKey, info As Byte(), aad As Byte(), plaintext As Byte(), keepTrace As Boolean) As ExchangeHpkeTrace
        If sender Is Nothing Then Throw New ArgumentNullException(NameOf(sender))
        If ephemeral Is Nothing Then Throw New ArgumentNullException(NameOf(ephemeral))
        If plaintext Is Nothing Then Throw New ArgumentNullException(NameOf(plaintext))
        If recipientPublicKey Is Nothing Then Return Nothing
        Dim dhE As Byte() = ephemeral.DeriveRaw(recipientPublicKey)
        Dim dhS As Byte() = sender.DeriveRaw(recipientPublicKey)
        If dhE Is Nothing OrElse dhS Is Nothing Then
            Wipe(dhE)
            Wipe(dhS)
            Return Nothing
        End If
        Dim dh As Byte() = Concat(dhE, dhS)
        Wipe(dhE)
        Wipe(dhS)
        Dim enc As Byte() = ephemeral.PublicKey
        Dim sharedSecret As Byte() = ExtractAndExpand(dh, Concat(enc, recipientPublicKey, sender.PublicKey))
        Wipe(dh)
        Dim ks As New KeySchedule(sharedSecret, info)
        Dim ciphertext As Byte() = ExchangeAead.Seal(ks.Key, ks.BaseNonce, aad, plaintext)
        Dim trace As New ExchangeHpkeTrace()
        trace.Enc = enc
        trace.Ciphertext = ciphertext
        If keepTrace Then
            trace.SharedSecret = sharedSecret
            trace.KeyScheduleContext = ks.Context
            trace.Secret = ks.Secret
            trace.Key = ks.Key
            trace.BaseNonce = ks.BaseNonce
        Else
            Wipe(sharedSecret)
            ks.Wipe()
        End If
        Return trace
    End Function

    ''' <summary>ExtractAndExpand of section 4.1 (DHKEM): the shared secret from the DH output and the KEM context.</summary>
    Private Shared Function ExtractAndExpand(dh As Byte(), kemContext As Byte()) As Byte()
        Dim eaePrk As Byte() = LabeledExtract(SuiteIdKem, Array.Empty(Of Byte)(), "eae_prk", dh)
        Try
            Return LabeledExpand(SuiteIdKem, eaePrk, "shared_secret", kemContext, SecretLength)
        Finally
            Wipe(eaePrk)
        End Try
    End Function

    Private Shared Function LabeledExtract(suiteId As Byte(), salt As Byte(), label As String, ikm As Byte()) As Byte()
        Dim labeledIkm As Byte() = Concat(Version, suiteId, Encoding.ASCII.GetBytes(label), ikm)
        Try
            Return HKDF.Extract(HashAlgorithmName.SHA256, labeledIkm, salt)
        Finally
            Wipe(labeledIkm)
        End Try
    End Function

    Private Shared Function LabeledExpand(suiteId As Byte(), prk As Byte(), label As String, info As Byte(), length As Integer) As Byte()
        Dim labeledInfo As Byte() = Concat(I2Osp2(length), Version, suiteId, Encoding.ASCII.GetBytes(label), info)
        Return HKDF.Expand(HashAlgorithmName.SHA256, prk, length, labeledInfo)
    End Function

    Private Shared Function IsBelowOrder(scalar As Byte()) As Boolean
        Dim zero As Boolean = True
        For Each b As Byte In scalar
            If b <> 0 Then
                zero = False
                Exit For
            End If
        Next
        If zero Then Return False
        For i As Integer = 0 To scalar.Length - 1
            If scalar(i) < GroupOrder(i) Then Return True
            If scalar(i) > GroupOrder(i) Then Return False
        Next
        Return False
    End Function

    Private Shared Function I2Osp2(value As Integer) As Byte()
        Return New Byte() {CByte((value >> 8) And &HFF), CByte(value And &HFF)}
    End Function

    Friend Shared Function Concat(ParamArray parts As Byte()()) As Byte()
        Dim total As Integer = 0
        For Each p As Byte() In parts
            total += p.Length
        Next
        Dim r(total - 1) As Byte
        Dim at As Integer = 0
        For Each p As Byte() In parts
            Buffer.BlockCopy(p, 0, r, at, p.Length)
            at += p.Length
        Next
        Return r
    End Function

    Friend Shared Sub Wipe(bytes As Byte())
        If bytes IsNot Nothing Then Array.Clear(bytes, 0, bytes.Length)
    End Sub

End Class
#End If
