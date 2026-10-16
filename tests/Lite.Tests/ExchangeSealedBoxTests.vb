#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports Xunit

' Spec 234 T-1 slice A: the sealed-content cryptography of DEVICE-EXCHANGE section 27, items DF, DG, DH. The vendored
' vectors (TestData\exchange-vectors\e2e-v1.json, copied byte for byte from the catalog; their hash is checked first) are
' replayed whole: the check codes, every positive box opened by every device it is addressed to, every failing case with
' its expected outcome. HPKE is held to RFC 9180 Appendix A.3, the Auth-mode block, before it is believed anywhere.
'
' What the vectors do NOT pin: the ephemeral HPKE keys, so the `enc` and `wk` of a box cannot be reproduced - a sealed box
' is proved by opening it and by round trips. The content key and nonce of the clipboard boxes ARE pinned (`contentKey`, `n`),
' so their `ct` is reproduced byte for byte.

''' <summary>The vendored vector file and the devices in it.</summary>
Friend NotInheritable Class E2eVectors

    Private Sub New()
    End Sub

    Friend NotInheritable Class VectorDevice
        Public Name As String = ""
        Public DeviceId As String = ""
        Public PrivateKey As Byte() = Array.Empty(Of Byte)()
        Public Kem As String = ""
        Public CheckCode As String = ""
    End Class

    Friend Shared Function FilePath(name As String) As String
        Return Path.Combine(AppContext.BaseDirectory, "TestData", "exchange-vectors", name)
    End Function

    ''' <summary>The hash of e2e-v1.json against its provenance file, checked before a vector is trusted.</summary>
    Friend Shared Sub VerifyProvenance()
        Dim provenance As String = File.ReadAllText(FilePath("e2e-v1.PROVENANCE.txt"), New UTF8Encoding(False))
        Dim actual As String = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FilePath("e2e-v1.json")))).ToLowerInvariant()
        Assert.True(provenance.Contains("e2e-v1.json " & actual), "e2e-v1.json no longer matches e2e-v1.PROVENANCE.txt (" & actual & ")")
    End Sub

    Friend Shared Function Load() As JsonDocument
        VerifyProvenance()
        Return JsonDocument.Parse(File.ReadAllText(FilePath("e2e-v1.json"), New UTF8Encoding(False)))
    End Function

    Friend Shared Function Devices(root As JsonElement) As Dictionary(Of String, VectorDevice)
        Dim r As New Dictionary(Of String, VectorDevice)(StringComparer.Ordinal)
        For Each d As JsonElement In root.GetProperty("devices").EnumerateArray()
            Dim v As New VectorDevice()
            v.Name = d.GetProperty("name").GetString()
            v.DeviceId = d.GetProperty("deviceId").GetString()
            v.PrivateKey = Convert.FromHexString(d.GetProperty("privateKey").GetString())
            v.Kem = d.GetProperty("e2eKey").GetProperty("kem").GetString()
            v.CheckCode = d.GetProperty("checkCode").GetString()
            r.Add(v.Name, v)
        Next
        Return r
    End Function

    Friend Shared Function FindBox(root As JsonElement, name As String) As JsonElement
        For Each b As JsonElement In root.GetProperty("boxes").EnumerateArray()
            If b.GetProperty("name").GetString() = name Then Return b
        Next
        Throw New InvalidOperationException("no box " & name)
    End Function

    ''' <summary>Structural equality of two JSON values (member order is not significant).</summary>
    Friend Shared Function JsonEqual(a As JsonElement, b As JsonElement) As Boolean
        If a.ValueKind <> b.ValueKind Then Return False
        Select Case a.ValueKind
            Case JsonValueKind.Object
                Dim ma As List(Of JsonProperty) = a.EnumerateObject().ToList()
                Dim mb As List(Of JsonProperty) = b.EnumerateObject().ToList()
                If ma.Count <> mb.Count Then Return False
                For Each p As JsonProperty In ma
                    Dim other As JsonElement
                    If Not b.TryGetProperty(p.Name, other) OrElse Not JsonEqual(p.Value, other) Then Return False
                Next
                Return True
            Case JsonValueKind.Array
                Dim ia As List(Of JsonElement) = a.EnumerateArray().ToList()
                Dim ib As List(Of JsonElement) = b.EnumerateArray().ToList()
                If ia.Count <> ib.Count Then Return False
                For i As Integer = 0 To ia.Count - 1
                    If Not JsonEqual(ia(i), ib(i)) Then Return False
                Next
                Return True
            Case JsonValueKind.String
                Return a.GetString() = b.GetString()
            Case JsonValueKind.Number
                Return a.GetRawText() = b.GetRawText()
            Case Else
                Return True
        End Select
    End Function

    Friend Shared Function Key(device As VectorDevice) As ExchangeDeviceKey
        Dim k As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(device.PrivateKey)
        Assert.NotNull(k)
        Return k
    End Function

    Friend Shared Function Hex(text As String) As Byte()
        Return Convert.FromHexString(text)
    End Function

    Friend Shared Function HexOf(bytes As Byte()) As String
        Return Convert.ToHexString(bytes).ToLowerInvariant()
    End Function

End Class

''' <summary>A cipher double for the key store: reversible, and able to fail on demand.</summary>
Friend NotInheritable Class ReversibleTestCipher
    Implements IExchangeSecretCipher

    Public FailUnprotect As Boolean

    Public Function Protect(plain As Byte()) As Byte() Implements IExchangeSecretCipher.Protect
        Dim r(plain.Length - 1) As Byte
        For i As Integer = 0 To plain.Length - 1
            r(i) = CByte(plain(i) Xor &H5A)
        Next
        Return r
    End Function

    Public Function Unprotect(blob As Byte()) As Byte() Implements IExchangeSecretCipher.Unprotect
        If FailUnprotect Then Throw New CryptographicException("test")
        Return Protect(blob)
    End Function
End Class

Public Class ExchangeSealedBoxTests

    ' ---------------------------------------------------------------- the vendored file

    <Fact>
    Public Sub The_vendored_vector_file_is_the_one_the_provenance_names()
        E2eVectors.VerifyProvenance()
    End Sub

    ' ---------------------------------------------------------------- RFC 9180 Appendix A.3, Auth mode

    Private Const RfcInfo As String = "4f6465206f6e2061204772656369616e2055726e"
    Private Const RfcIkmE As String = "798d82a8d9ea19dbc7f2c6dfa54e8a6706f7cdc119db0813dacf8440ab37c857"
    Private Const RfcPkEm As String = "042224f3ea800f7ec55c03f29fc9865f6ee27004f818fcbdc6dc68932c1e52e15b79e264a98f2c535ef06745f3d308624414153b22c7332bc1e691cb4af4d53454"
    Private Const RfcSkEm As String = "6b8de0873aed0c1b2d09b8c7ed54cbf24fdf1dfc7a47fa501f918810642d7b91"
    Private Const RfcIkmR As String = "7bc93bde8890d1fb55220e7f3b0c107ae7e6eda35ca4040bb6651284bf0747ee"
    Private Const RfcPkRm As String = "04423e363e1cd54ce7b7573110ac121399acbc9ed815fae03b72ffbd4c18b01836835c5a09513f28fc971b7266cfde2e96afe84bb0f266920e82c4f53b36e1a78d"
    Private Const RfcSkRm As String = "d929ab4be2e59f6954d6bedd93e638f02d4046cef21115b00cdda2acb2a4440e"
    Private Const RfcIkmS As String = "874baa0dcf93595a24a45a7f042e0d22d368747daaa7e19f80a802af19204ba8"
    Private Const RfcPkSm As String = "04a817a0902bf28e036d66add5d544cc3a0457eab150f104285df1e293b5c10eef8651213e43d9cd9086c80b309df22cf37609f58c1127f7607e85f210b2804f73"
    Private Const RfcSkSm As String = "1120ac99fb1fccc1e8230502d245719d1b217fe20505c7648795139d177f0de9"
    Private Const RfcSharedSecret As String = "d4aea336439aadf68f9348880aa358086f1480e7c167b6ef15453ba69b94b44f"
    Private Const RfcKeyScheduleContext As String = "02b88d4e6d91759e65e87c470e8b9141113e9ad5f0c8ceefc1e088c82e6980500798e486f9c9c09c9b5c753ac72d6005de254c607d1b534ed11d493ae1c1d9ac85"
    Private Const RfcSecret As String = "fd0a93c7c6f6b1b0dd6a822d7b16f6c61c83d98ad88426df4613c3581a2319f1"
    Private Const RfcKey As String = "19aa8472b3fdc530392b0e54ca17c0f5"
    Private Const RfcBaseNonce As String = "b390052d26b67a5b8a8fcaa4"
    Private Const RfcPt As String = "4265617574792069732074727574682c20747275746820626561757479"
    Private Const RfcAad As String = "436f756e742d30"
    Private Const RfcCt As String = "82ffc8c44760db691a07c5627e5fc2c08e7a86979ee79b494a17cc3405446ac2bdb8f265db4a099ed3289ffe19"

    <Fact>
    Public Sub Rfc9180_A3_auth_DeriveKeyPair_gives_the_published_private_and_public_keys()
        Dim cases As (ikm As String, sk As String, pk As String)() = {
            (RfcIkmE, RfcSkEm, RfcPkEm), (RfcIkmR, RfcSkRm, RfcPkRm), (RfcIkmS, RfcSkSm, RfcPkSm)}
        For Each c As (ikm As String, sk As String, pk As String) In cases
            Dim sk As Byte() = ExchangeHpke.DeriveKeyPairScalar(E2eVectors.Hex(c.ikm))
            Assert.Equal(c.sk, E2eVectors.HexOf(sk))
            Using key As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(sk)
                Assert.Equal(c.pk, E2eVectors.HexOf(key.PublicKey))
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Rfc9180_A3_auth_key_schedule_and_first_encryption_are_reproduced()
        Using skE As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(E2eVectors.Hex(RfcSkEm))
            Using skS As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(E2eVectors.Hex(RfcSkSm))
                Dim t As ExchangeHpkeTrace = ExchangeHpke.SealAuthTraced(E2eVectors.Hex(RfcPkRm), skS, skE, E2eVectors.Hex(RfcInfo), E2eVectors.Hex(RfcAad), E2eVectors.Hex(RfcPt))
                Assert.NotNull(t)
                Assert.Equal(RfcPkEm, E2eVectors.HexOf(t.Enc))
                Assert.Equal(RfcSharedSecret, E2eVectors.HexOf(t.SharedSecret))
                Assert.Equal(RfcKeyScheduleContext, E2eVectors.HexOf(t.KeyScheduleContext))
                Assert.Equal(RfcSecret, E2eVectors.HexOf(t.Secret))
                Assert.Equal(RfcKey, E2eVectors.HexOf(t.Key))
                Assert.Equal(RfcBaseNonce, E2eVectors.HexOf(t.BaseNonce))
                Assert.Equal(RfcCt, E2eVectors.HexOf(t.Ciphertext))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Rfc9180_A3_auth_message_opens_with_the_published_recipient_key()
        Using skR As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(E2eVectors.Hex(RfcSkRm))
            Dim pt As Byte() = Nothing
            Assert.True(ExchangeHpke.TryOpenAuth(E2eVectors.Hex(RfcPkEm), E2eVectors.Hex(RfcPkSm), skR, E2eVectors.Hex(RfcInfo), E2eVectors.Hex(RfcAad), E2eVectors.Hex(RfcCt), pt))
            Assert.Equal(RfcPt, E2eVectors.HexOf(pt))
        End Using
    End Sub

    <Fact>
    Public Sub Hpke_open_fails_for_another_sender_aad_info_or_a_flipped_bit_and_returns_nothing()
        Using skR As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(E2eVectors.Hex(RfcSkRm))
            Dim enc As Byte() = E2eVectors.Hex(RfcPkEm)
            Dim pkS As Byte() = E2eVectors.Hex(RfcPkSm)
            Dim info As Byte() = E2eVectors.Hex(RfcInfo)
            Dim aad As Byte() = E2eVectors.Hex(RfcAad)
            Dim ct As Byte() = E2eVectors.Hex(RfcCt)
            Dim pt As Byte() = Nothing
            ' another sender's key (Auth mode binds the sender)
            Assert.False(ExchangeHpke.TryOpenAuth(enc, E2eVectors.Hex(RfcPkRm), skR, info, aad, ct, pt))
            Assert.Null(pt)
            Assert.False(ExchangeHpke.TryOpenAuth(enc, pkS, skR, info, E2eVectors.Hex("436f756e742d31"), ct, pt))
            Assert.Null(pt)
            Assert.False(ExchangeHpke.TryOpenAuth(enc, pkS, skR, Encoding.ASCII.GetBytes("other"), aad, ct, pt))
            Dim flipped As Byte() = CType(ct.Clone(), Byte())
            flipped(3) = CByte(flipped(3) Xor 1)
            Assert.False(ExchangeHpke.TryOpenAuth(enc, pkS, skR, info, aad, flipped, pt))
            Assert.Null(pt)
            ' an enc that is no point of the curve
            Dim bad As Byte() = CType(enc.Clone(), Byte())
            bad(40) = CByte(bad(40) Xor 1)
            Assert.False(ExchangeHpke.TryOpenAuth(bad, pkS, skR, info, aad, ct, pt))
            Assert.False(ExchangeHpke.TryOpenAuth(New Byte(63) {}, pkS, skR, info, aad, ct, pt))
        End Using
    End Sub

    <Fact>
    Public Sub Hpke_seal_to_something_that_is_no_point_gives_nothing_and_a_fresh_seal_round_trips()
        Using sender As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Using recipient As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim notAPoint(64) As Byte
                notAPoint(0) = 4
                notAPoint(1) = 1
                Assert.Null(ExchangeHpke.SealAuth(notAPoint, sender, Encoding.ASCII.GetBytes("i"), Encoding.ASCII.GetBytes("a"), New Byte() {1, 2, 3}))
                Assert.Null(ExchangeHpke.SealAuth(New Byte(10) {}, sender, Encoding.ASCII.GetBytes("i"), Encoding.ASCII.GetBytes("a"), New Byte() {1, 2, 3}))

                Dim info As Byte() = Encoding.ASCII.GetBytes("i")
                Dim aad As Byte() = Encoding.ASCII.GetBytes("a")
                Dim sealed1 As ExchangeHpkeSealed = ExchangeHpke.SealAuth(recipient.PublicKey, sender, info, aad, New Byte() {1, 2, 3})
                Dim sealed2 As ExchangeHpkeSealed = ExchangeHpke.SealAuth(recipient.PublicKey, sender, info, aad, New Byte() {1, 2, 3})
                Assert.Equal(65, sealed1.Enc.Length)
                Assert.Equal(3 + 16, sealed1.Ciphertext.Length)
                Assert.NotEqual(E2eVectors.HexOf(sealed1.Enc), E2eVectors.HexOf(sealed2.Enc))   ' a fresh ephemeral key each time
                Dim pt As Byte() = Nothing
                Assert.True(ExchangeHpke.TryOpenAuth(sealed1.Enc, sender.PublicKey, recipient, info, aad, sealed1.Ciphertext, pt))
                Assert.Equal(New Byte() {1, 2, 3}, pt)
            End Using
        End Using
    End Sub

    ' ---------------------------------------------------------------- the device key (items DF, DG)

    <Fact>
    Public Sub The_vector_devices_have_their_public_keys_and_check_codes()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim devices As Dictionary(Of String, E2eVectors.VectorDevice) = E2eVectors.Devices(doc.RootElement)
            Assert.Equal(5, devices.Count)
            For Each d As E2eVectors.VectorDevice In devices.Values
                Using key As ExchangeDeviceKey = E2eVectors.Key(d)
                    Assert.Equal(d.Kem, key.Kem)
                    Assert.Equal(d.CheckCode, key.CheckCode)
                    Assert.Equal(d.CheckCode, ExchangeDeviceKey.CheckCodeOfKem(d.Kem))
                    Dim point As Byte() = Nothing
                    Assert.True(ExchangeDeviceKey.TryParseKem(d.Kem, point))
                    Assert.Equal(65, point.Length)
                    Assert.Equal(4, CInt(point(0)))
                End Using
            Next
        End Using
    End Sub

    <Fact>
    Public Sub A_check_code_is_twelve_digits_in_three_groups_of_four()
        Using key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Assert.Matches("^[0-9]{4} [0-9]{4} [0-9]{4}$", key.CheckCode)
        End Using
        Assert.Equal("", ExchangeDeviceKey.CheckCodeOf(New Byte(64) {}.Take(10).ToArray()))
        Assert.Equal("", ExchangeDeviceKey.CheckCodeOfKem("not a key"))
    End Sub

    <Fact>
    Public Sub A_generated_key_exports_and_imports_its_scalar_and_never_prints_itself()
        Using key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Dim scalar As Byte() = key.ExportPrivateScalar()
            Assert.Equal(32, scalar.Length)
            Using again As ExchangeDeviceKey = ExchangeDeviceKey.FromPrivateScalar(scalar)
                Assert.Equal(key.Kem, again.Kem)
            End Using
            Assert.DoesNotContain(E2eVectors.HexOf(scalar), key.ToString())
            Assert.DoesNotContain(ExchangeBase64Url.Encode(scalar), key.ToString())
        End Using
        Assert.Null(ExchangeDeviceKey.FromPrivateScalar(New Byte(31) {}))       ' zero is no private key
        Assert.Null(ExchangeDeviceKey.FromPrivateScalar(New Byte(30) {}))
        Assert.Null(ExchangeDeviceKey.FromPrivateScalar(Nothing))
    End Sub

    <Fact>
    Public Sub Kem_parsing_is_strict()
        Using key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Dim kem As String = key.Kem
            Assert.Equal(87, kem.Length)                          ' 65 bytes, base64url, no padding
            Dim point As Byte() = Nothing
            Assert.True(ExchangeDeviceKey.TryParseKem(kem, point))
            Assert.False(ExchangeDeviceKey.TryParseKem(kem & "=", point))
            Assert.False(ExchangeDeviceKey.TryParseKem(kem.Replace("-"c, "+"c).Replace("_"c, "/"c) & "", point) AndAlso (kem.Contains("-") OrElse kem.Contains("_")))
            Assert.False(ExchangeDeviceKey.TryParseKem(kem.Substring(0, 86), point))
            Assert.False(ExchangeDeviceKey.TryParseKem("", point))
            Assert.False(ExchangeDeviceKey.TryParseKem(Nothing, point))
            ' a point that is not on the curve
            Dim raw As Byte() = key.PublicKey
            raw(64) = CByte(raw(64) Xor 1)
            Assert.False(ExchangeDeviceKey.TryParseKem(ExchangeBase64Url.Encode(raw), point))
            ' compressed form is no kem
            raw = key.PublicKey
            raw(0) = 2
            Assert.False(ExchangeDeviceKey.TryParseKem(ExchangeBase64Url.Encode(raw), point))
        End Using
    End Sub

    <Fact>
    Public Sub Base64url_has_exactly_one_spelling_per_byte_string()
        Dim b As Byte() = Nothing
        Assert.True(ExchangeBase64Url.TryDecode("AQID", b))
        Assert.Equal(New Byte() {1, 2, 3}, b)
        Assert.True(ExchangeBase64Url.TryDecode("AQI", b))
        Assert.False(ExchangeBase64Url.TryDecode("AQJ", b))        ' non-canonical last character
        Assert.False(ExchangeBase64Url.TryDecode("AQI=", b))
        Assert.False(ExchangeBase64Url.TryDecode("AQ+/", b))
        Assert.False(ExchangeBase64Url.TryDecode("A", b))
        Assert.False(ExchangeBase64Url.TryDecode(" AQID", b))
        Assert.Equal("-_8", ExchangeBase64Url.Encode(New Byte() {&HFB, &HFF}))
    End Sub

    ' ---------------------------------------------------------------- the key store

    Private Shared Function TempDirectory() As String
        Return Path.Combine(Path.GetTempPath(), "fms-e2e-keys-" & Guid.NewGuid().ToString("N"))
    End Function

    <Fact>
    Public Sub The_memory_store_keeps_one_key_per_id_and_never_replaces_it()
        Dim store As IExchangeDeviceKeyStore = New ExchangeMemoryDeviceKeyStore()
        Assert.False(store.Exists("dev-a"))
        Assert.Null(store.TryLoad("dev-a"))
        Using first As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Using other As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Assert.True(store.Save("dev-a", first))
                Assert.True(store.Save("dev-a", first))        ' the very same key again is fine
                Assert.False(store.Save("dev-a", other))       ' another key under the same id never is
                Using loaded As ExchangeDeviceKey = store.TryLoad("dev-a")
                    Assert.Equal(first.Kem, loaded.Kem)
                End Using
                store.Delete("dev-a")
                Assert.False(store.Exists("dev-a"))
                Assert.True(store.Save("dev-a", other))        ' only after the id was dropped
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_file_store_binds_a_key_to_its_id_protects_it_and_leaves_no_plain_scalar_on_disk()
        Dim dir As String = TempDirectory()
        Try
            Dim cipher As New ReversibleTestCipher()
            Dim store As New ExchangeDeviceKeyStore(dir, cipher)
            Dim id As String = ExchangeIds.NewId()
            Dim other As String = ExchangeIds.NewId()
            Assert.False(Directory.Exists(dir))                 ' a read creates nothing
            Assert.Null(store.TryLoad(id))
            Assert.False(store.Exists(id))
            Assert.False(Directory.Exists(dir))

            Using key As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Assert.True(store.Save(id, key))
                Dim file As String = store.FilePathFor(id)
                Assert.True(System.IO.File.Exists(file))
                Dim onDisk As String = Encoding.Latin1.GetString(System.IO.File.ReadAllBytes(file))
                Dim scalar As Byte() = key.ExportPrivateScalar()
                Assert.DoesNotContain(ExchangeBase64Url.Encode(scalar), onDisk)   ' the cipher ran
                Using loaded As ExchangeDeviceKey = store.TryLoad(id)
                    Assert.Equal(key.Kem, loaded.Kem)
                End Using
                ' a file copied beside another id is no key
                System.IO.File.Copy(file, store.FilePathFor(other))
                Assert.Null(store.TryLoad(other))
                ' a damaged or foreign blob is no key, and is not replaced silently
                cipher.FailUnprotect = True
                Assert.Null(store.TryLoad(id))
                Assert.True(store.Exists(id))
                Using fresh As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                    Assert.False(store.Save(id, fresh))
                End Using
                Assert.Null(ExchangeDeviceKeys.LoadOrCreate(store, id))
                cipher.FailUnprotect = False
                store.Delete(id)
                Assert.False(store.Exists(id))
            End Using
            ' no path from anything but a wire id
            Assert.Equal("", store.FilePathFor("..\..\x"))
            Assert.Null(store.TryLoad("dev-phone"))
            Using k As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Assert.False(store.Save("dev-phone", k))
            End Using
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub LoadOrCreate_makes_a_key_once_and_returns_the_same_one_afterwards()
        Dim store As New ExchangeMemoryDeviceKeyStore()
        Using first As ExchangeDeviceKey = ExchangeDeviceKeys.LoadOrCreate(store, "dev-a")
            Using second As ExchangeDeviceKey = ExchangeDeviceKeys.LoadOrCreate(store, "dev-a")
                Assert.Equal(first.Kem, second.Kem)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub The_dpapi_store_round_trips_a_key_for_the_current_user()
        Dim dir As String = TempDirectory()
        Try
            Dim store As New ExchangeDeviceKeyStore(dir, Nothing)       ' the real DPAPI cipher
            Dim id As String = ExchangeIds.NewId()
            Using key As ExchangeDeviceKey = ExchangeDeviceKeys.LoadOrCreate(store, id)
                Assert.NotNull(key)
                Using again As ExchangeDeviceKey = store.TryLoad(id)
                    Assert.Equal(key.Kem, again.Kem)
                End Using
                Dim onDisk As String = Encoding.Latin1.GetString(System.IO.File.ReadAllBytes(store.FilePathFor(id)))
                Assert.DoesNotContain(ExchangeBase64Url.Encode(key.ExportPrivateScalar()), onDisk)
            End Using
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    ' ---------------------------------------------------------------- the sealed box: vectors

    Private Shared Function Open(box As ExchangeSealedBox, deviceId As String, key As ExchangeDeviceKey, context As String, trustedKem As String, Optional keepKey As Boolean = False) As ExchangeSealedOpenResult
        Return ExchangeSealedBoxes.Open(box, deviceId, key, context, Function(fromId As String) trustedKem, keepKey)
    End Function

    <Fact>
    Public Sub Every_positive_vector_box_opens_with_every_device_it_is_addressed_to_and_gives_the_payload()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim devices As Dictionary(Of String, E2eVectors.VectorDevice) = E2eVectors.Devices(doc.RootElement)
            Dim opened As Integer = 0
            For Each entry As JsonElement In doc.RootElement.GetProperty("boxes").EnumerateArray()
                Dim name As String = entry.GetProperty("name").GetString()
                Dim box As ExchangeSealedBox = Nothing
                Assert.True(ExchangeSealedBox.TryRead(entry.GetProperty("box"), box), name)
                Dim from As E2eVectors.VectorDevice = devices(entry.GetProperty("from").GetString())
                Assert.Equal(from.DeviceId, box.FromDeviceId)
                Assert.Equal(from.Kem, box.FromKey)
                For Each toName As JsonElement In entry.GetProperty("to").EnumerateArray()
                    Dim d As E2eVectors.VectorDevice = devices(toName.GetString())
                    Using key As ExchangeDeviceKey = E2eVectors.Key(d)
                        Dim r As ExchangeSealedOpenResult = Open(box, d.DeviceId, key, entry.GetProperty("context").GetString(), from.Kem)
                        Assert.True(r.Succeeded, name & " for " & d.Name & ": " & r.Outcome.ToString())
                        Using payload As JsonDocument = JsonDocument.Parse(r.Payload)
                            Assert.True(E2eVectors.JsonEqual(payload.RootElement, entry.GetProperty("payload")), name & " for " & d.Name & ": the payload differs")
                        End Using
                        opened += 1
                    End Using
                Next
            Next
            Assert.Equal(7, opened)     ' access x2, grant x1, broadcast x2, clipboard x2
        End Using
    End Sub

    <Fact>
    Public Sub Every_failing_vector_fails_with_the_expected_outcome_and_opens_nothing()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim devices As Dictionary(Of String, E2eVectors.VectorDevice) = E2eVectors.Devices(doc.RootElement)
            Dim seen As Integer = 0
            For Each f As JsonElement In doc.RootElement.GetProperty("failing").EnumerateArray()
                Dim name As String = f.GetProperty("name").GetString()
                Dim source As JsonElement = E2eVectors.FindBox(doc.RootElement, f.GetProperty("box").GetString())
                Dim boxElement As JsonElement = source.GetProperty("box")
                Dim replaced As JsonElement
                If f.TryGetProperty("replacedBox", replaced) Then boxElement = replaced
                Dim box As ExchangeSealedBox = Nothing
                Assert.True(ExchangeSealedBox.TryRead(boxElement, box), name)
                Dim reader As E2eVectors.VectorDevice = devices(f.GetProperty("openAs").GetString())
                Dim trusted As E2eVectors.VectorDevice = devices(f.GetProperty("readerTrustsForPhone").GetString())
                Dim expected As ExchangeSealedOpenOutcome
                Select Case f.GetProperty("expected").GetString()
                    Case "NO_ENTRY" : expected = ExchangeSealedOpenOutcome.NoEntry
                    Case "UNTRUSTED_SENDER" : expected = ExchangeSealedOpenOutcome.UntrustedFromKey
                    Case "BAD_TAG" : expected = ExchangeSealedOpenOutcome.TagFailed
                    Case Else : Throw New InvalidOperationException("an outcome this test does not know: " & f.GetProperty("expected").GetString())
                End Select
                Using key As ExchangeDeviceKey = E2eVectors.Key(reader)
                    Dim r As ExchangeSealedOpenResult = Open(box, reader.DeviceId, key, f.GetProperty("context").GetString(), trusted.Kem, True)
                    Assert.Equal(expected, r.Outcome)
                    Assert.False(r.Succeeded, name)
                    Assert.Null(r.Payload)
                    Assert.Null(r.ContentKey)
                End Using
                seen += 1
            Next
            Assert.Equal(4, seen)
        End Using
    End Sub

    <Fact>
    Public Sub A_box_written_back_is_the_same_JSON_the_vector_has()
        Using doc As JsonDocument = E2eVectors.Load()
            For Each entry As JsonElement In doc.RootElement.GetProperty("boxes").EnumerateArray()
                Dim raw As String = entry.GetProperty("box").GetRawText()
                Dim box As ExchangeSealedBox = Nothing
                Assert.True(ExchangeSealedBox.TryParse(raw, box))
                Assert.Equal(ExchangeJson.Compact(raw), box.ToJson())
            Next
        End Using
    End Sub

    <Fact>
    Public Sub The_clipboard_vector_ct_is_reproduced_from_the_pinned_content_key_and_nonce()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim devices As Dictionary(Of String, E2eVectors.VectorDevice) = E2eVectors.Devices(doc.RootElement)
            Dim phone As E2eVectors.VectorDevice = devices("phone")
            Dim tablet As E2eVectors.VectorDevice = devices("tablet")
            For Each c As JsonElement In doc.RootElement.GetProperty("clipboard").EnumerateArray()
                Dim box As ExchangeSealedBox = Nothing
                Assert.True(ExchangeSealedBox.TryRead(c.GetProperty("box"), box))
                Using tabletKey As ExchangeDeviceKey = E2eVectors.Key(tablet)
                    Dim r As ExchangeSealedOpenResult = Open(box, tablet.DeviceId, tabletKey, ExchangeSealedContext.Clipboard, phone.Kem, True)
                    Assert.True(r.Succeeded, c.GetProperty("name").GetString())
                    Assert.Equal(c.GetProperty("contentKey").GetString(), E2eVectors.HexOf(r.ContentKey))
                    ' seal the same payload with the pinned inputs: the ct is deterministic and must be the vector's
                    Dim nonce As Byte() = Nothing
                    Assert.True(ExchangeBase64Url.TryDecode(box.Nonce, nonce))
                    Using phoneKey As ExchangeDeviceKey = E2eVectors.Key(phone)
                        Dim recipients As New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient(tablet.DeviceId, tablet.Kem)}
                        Dim sealedAgain As ExchangeSealResult = ExchangeSealedBoxes.SealWithFixedRandom(r.Payload, ExchangeSealedContext.Clipboard, phone.DeviceId, phoneKey, recipients, r.ContentKey, nonce)
                        Assert.True(sealedAgain.Succeeded)
                        Assert.Equal(box.Ciphertext, sealedAgain.Box.Ciphertext)
                        Assert.Equal(box.Nonce, sealedAgain.Box.Nonce)
                        Assert.Equal(box.FromKey, sealedAgain.Box.FromKey)
                        ' the fresh box is a new box (fresh HPKE ephemeral keys) and opens like the old one
                        Assert.NotEqual(box.Keys(0).Enc, sealedAgain.Box.Keys(0).Enc)
                        Dim reopened As ExchangeSealedOpenResult = Open(sealedAgain.Box, tablet.DeviceId, tabletKey, ExchangeSealedContext.Clipboard, phone.Kem)
                        Assert.True(reopened.Succeeded)
                        Assert.Equal(E2eVectors.HexOf(r.Payload), E2eVectors.HexOf(reopened.Payload))
                    End Using
                End Using
            Next
        End Using
    End Sub

    ' ---------------------------------------------------------------- the sealed box: behaviour

    Private NotInheritable Class Party
        Public ReadOnly Id As String
        Public ReadOnly Key As ExchangeDeviceKey

        Public Sub New(id As String)
            Me.Id = id
            Key = ExchangeDeviceKey.Generate()
        End Sub

        Public Function AsRecipient() As ExchangeSealedRecipient
            Return New ExchangeSealedRecipient(Id, Key.Kem)
        End Function
    End Class

    Private Shared ReadOnly Payload As Byte() = Encoding.UTF8.GetBytes("{""access"":{""type"":""sftp"",""username"":""u"",""password"":""not-in-any-message""}}")

    <Fact>
    Public Sub A_box_round_trips_for_each_recipient_and_for_nobody_else()
        Dim sender As New Party("dev-sender")
        Dim a As New Party("dev-a")
        Dim b As New Party("dev-b")
        Dim outsider As New Party("dev-out")
        Dim context As String = ExchangeSealedContext.Access("res-1")
        Dim result As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, context, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient(), b.AsRecipient()})
        Assert.True(result.Succeeded)
        Assert.Null(result.ContentKey)
        Assert.Equal(1, result.Box.Version)
        Assert.Equal(sender.Id, result.Box.FromDeviceId)
        Assert.Equal(sender.Key.Kem, result.Box.FromKey)
        Assert.Equal(2, result.Box.Keys.Count)
        Dim nonceBytes As Byte() = Nothing
        Assert.True(ExchangeBase64Url.TryDecode(result.Box.Nonce, nonceBytes))
        Assert.Equal(12, nonceBytes.Length)

        For Each p As Party In {a, b}
            Dim r As ExchangeSealedOpenResult = Open(result.Box, p.Id, p.Key, context, sender.Key.Kem)
            Assert.True(r.Succeeded)
            Assert.Equal(Encoding.UTF8.GetString(Payload), Encoding.UTF8.GetString(r.Payload))
            Assert.Null(r.ContentKey)
        Next
        Assert.Equal(ExchangeSealedOpenOutcome.NoEntry, Open(result.Box, outsider.Id, outsider.Key, context, sender.Key.Kem).Outcome)
        ' the entry of another device, opened with this device's key, is a failed tag - never a payload
        Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, Open(result.Box, a.Id, outsider.Key, context, sender.Key.Kem).Outcome)
    End Sub

    <Fact>
    Public Sub A_box_does_not_open_in_another_context_with_another_trusted_key_or_another_version()
        Dim sender As New Party("dev-sender")
        Dim impostor As New Party("dev-sender")        ' same id, another key: the trusted one is the first
        Dim a As New Party("dev-a")
        Dim result As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Grant("g-1"), sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient()})
        Assert.True(Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), sender.Key.Kem).Succeeded)
        Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-2"), sender.Key.Kem).Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Access("g-1"), sender.Key.Kem).Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Clipboard, sender.Key.Kem).Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.UntrustedFromKey, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), impostor.Key.Kem).Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.UntrustedFromKey, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), Nothing).Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.UntrustedFromKey, Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), "garbage").Outcome)
        Assert.Equal(ExchangeSealedOpenOutcome.UntrustedFromKey, ExchangeSealedBoxes.Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), Nothing).Outcome)

        Dim v2 As New ExchangeSealedBox(2, result.Box.FromDeviceId, result.Box.FromKey, result.Box.Nonce, result.Box.Ciphertext, result.Box.Keys)
        Assert.Equal(ExchangeSealedOpenOutcome.UnknownVersion, Open(v2, a.Id, a.Key, ExchangeSealedContext.Grant("g-1"), sender.Key.Kem).Outcome)
    End Sub

    <Fact>
    Public Sub A_damaged_box_never_opens_whichever_member_is_touched()
        Dim sender As New Party("dev-sender")
        Dim a As New Party("dev-a")
        Dim context As String = ExchangeSealedContext.Broadcast("b-1")
        Dim box As ExchangeSealedBox = ExchangeSealedBoxes.Seal(Payload, context, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient()}).Box
        Dim k As ExchangeSealedBoxKey = box.Keys(0)

        Dim variants As New List(Of ExchangeSealedBox) From {
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, Flip(box.Nonce), box.Ciphertext, box.Keys),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, Flip(box.Ciphertext), box.Keys),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, box.Ciphertext.Substring(0, 10), box.Keys),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce.Substring(0, 8), box.Ciphertext, box.Keys),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, box.Ciphertext, New List(Of ExchangeSealedBoxKey) From {New ExchangeSealedBoxKey(k.ToDeviceId, Flip(k.Enc), k.Wk)}),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, box.Ciphertext, New List(Of ExchangeSealedBoxKey) From {New ExchangeSealedBoxKey(k.ToDeviceId, k.Enc, Flip(k.Wk))}),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, box.Ciphertext, New List(Of ExchangeSealedBoxKey) From {New ExchangeSealedBoxKey(k.ToDeviceId, k.Enc, k.Wk.Substring(0, 20))}),
            New ExchangeSealedBox(1, box.FromDeviceId, box.FromKey, box.Nonce, box.Ciphertext, New List(Of ExchangeSealedBoxKey) From {New ExchangeSealedBoxKey(k.ToDeviceId, "", "")})}
        For i As Integer = 0 To variants.Count - 1
            Dim r As ExchangeSealedOpenResult = Open(variants(i), a.Id, a.Key, context, sender.Key.Kem, True)
            Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, r.Outcome)
            Assert.Null(r.Payload)
            Assert.Null(r.ContentKey)
        Next
        Assert.Equal(ExchangeSealedOpenOutcome.TagFailed, ExchangeSealedBoxes.Open(Nothing, a.Id, a.Key, context, Function(s As String) sender.Key.Kem).Outcome)
    End Sub

    ' Flips the first character of a base64url string to another that stays valid.
    Private Shared Function Flip(text As String) As String
        Dim first As Char = If(text(0) = "A"c, "B"c, "A"c)
        Return first & text.Substring(1)
    End Function

    <Fact>
    Public Sub Forty_recipients_are_sealed_forty_one_are_refused_and_none_is_refused()
        Dim sender As New Party("dev-sender")
        Dim many As New List(Of Party)
        For i As Integer = 0 To 40
            many.Add(New Party("dev-" & i.ToString()))
        Next
        Dim forty As List(Of ExchangeSealedRecipient) = many.Take(40).Select(Function(p) p.AsRecipient()).ToList()
        Dim ok As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, forty)
        Assert.True(ok.Succeeded)
        Assert.Equal(40, ok.Box.Keys.Count)
        Assert.True(Open(ok.Box, many(39).Id, many(39).Key, ExchangeSealedContext.Clipboard, sender.Key.Kem).Succeeded)

        Dim fortyOne As List(Of ExchangeSealedRecipient) = many.Select(Function(p) p.AsRecipient()).ToList()
        Dim refused As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, fortyOne)
        Assert.Equal(ExchangeSealOutcome.TooManyRecipients, refused.Outcome)
        Assert.Null(refused.Box)

        Assert.Equal(ExchangeSealOutcome.NoRecipients, ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient)).Outcome)
        Assert.Equal(ExchangeSealOutcome.NoRecipients, ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, Nothing).Outcome)
    End Sub

    <Fact>
    Public Sub Recipients_are_checked_and_one_id_is_sealed_once()
        Dim sender As New Party("dev-sender")
        Dim a As New Party("dev-a")
        Dim b As New Party("dev-b")
        ' the same id twice with the same key: one entry; 41 listings of 40 distinct devices are still 40
        Dim twice As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient(), a.AsRecipient()})
        Assert.True(twice.Succeeded)
        Assert.Single(twice.Box.Keys)
        ' the same id with two keys is a caller's bug
        Dim conflict As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient(), New ExchangeSealedRecipient(a.Id, b.Key.Kem)})
        Assert.Equal(ExchangeSealOutcome.InvalidRecipient, conflict.Outcome)
        ' a kem that is no key
        Assert.Equal(ExchangeSealOutcome.InvalidRecipient, ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient("dev-x", "AAAA")}).Outcome)
        Assert.Equal(ExchangeSealOutcome.InvalidRecipient, ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient("", a.Key.Kem)}).Outcome)
        ' the programmer's errors throw, with no payload in the message
        Dim ex As ArgumentException = Assert.Throws(Of ArgumentException)(Sub() ExchangeSealedBoxes.Seal(Payload, "", sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient()}))
        Assert.DoesNotContain("not-in-any-message", ex.Message)
        Assert.Throws(Of ArgumentException)(Sub() ExchangeSealedContext.Access(""))
    End Sub

    <Fact>
    Public Sub The_contexts_have_the_forms_of_the_contract()
        Assert.Equal("access:r1", ExchangeSealedContext.Access("r1"))
        Assert.Equal("grant:g1", ExchangeSealedContext.Grant("g1"))
        Assert.Equal("broadcast:b1", ExchangeSealedContext.Broadcast("b1"))
        Assert.Equal("clipboard", ExchangeSealedContext.Clipboard)
    End Sub

    <Fact>
    Public Sub Two_seals_of_one_payload_differ_everywhere_and_the_content_key_is_given_only_when_asked()
        Dim sender As New Party("dev-sender")
        Dim a As New Party("dev-a")
        Dim recipients As New List(Of ExchangeSealedRecipient) From {a.AsRecipient()}
        Dim one As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, recipients, True)
        Dim two As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, recipients, True)
        Assert.NotEqual(one.Box.Nonce, two.Box.Nonce)
        Assert.NotEqual(one.Box.Ciphertext, two.Box.Ciphertext)
        Assert.NotEqual(E2eVectors.HexOf(one.ContentKey), E2eVectors.HexOf(two.ContentKey))
        Assert.Equal(32, one.ContentKey.Length)
        Dim opened As ExchangeSealedOpenResult = Open(one.Box, a.Id, a.Key, ExchangeSealedContext.Clipboard, sender.Key.Kem, True)
        Assert.Equal(E2eVectors.HexOf(one.ContentKey), E2eVectors.HexOf(opened.ContentKey))
        one.Wipe()
        Assert.Null(one.ContentKey)
        opened.Wipe()
        Assert.Null(opened.Payload)
    End Sub

    <Fact>
    Public Sub A_box_parses_only_when_it_has_every_member_and_nothing_prints_a_payload_or_a_key()
        Dim box As ExchangeSealedBox = Nothing
        Assert.False(ExchangeSealedBox.TryParse("", box))
        Assert.False(ExchangeSealedBox.TryParse("not json", box))
        Assert.False(ExchangeSealedBox.TryParse("[]", box))
        Assert.False(ExchangeSealedBox.TryParse("{""v"":1}", box))
        Assert.False(ExchangeSealedBox.TryParse("{""v"":""1"",""from"":""a"",""fromKey"":""k"",""n"":""n"",""ct"":""c"",""keys"":[]}", box))
        Assert.False(ExchangeSealedBox.TryParse("{""v"":1,""from"":""a"",""fromKey"":""k"",""n"":""n"",""ct"":""c"",""keys"":[{""to"":""x""}]}", box))
        Assert.True(ExchangeSealedBox.TryParse("{""v"":7,""from"":""a"",""fromKey"":""k"",""n"":""n"",""ct"":""c"",""keys"":[],""later"":true}", box))
        Assert.Equal(7, box.Version)

        Dim sender As New Party("dev-sender")
        Dim a As New Party("dev-a")
        Dim result As ExchangeSealResult = ExchangeSealedBoxes.Seal(Payload, ExchangeSealedContext.Clipboard, sender.Id, sender.Key, New List(Of ExchangeSealedRecipient) From {a.AsRecipient()}, True)
        Assert.Equal("Sealed", result.ToString())
        Assert.Equal("ExchangeSealedBox", result.Box.ToString())
        Dim opened As ExchangeSealedOpenResult = Open(result.Box, a.Id, a.Key, ExchangeSealedContext.Clipboard, sender.Key.Kem)
        Assert.Equal("Opened", opened.ToString())
        Assert.DoesNotContain("not-in-any-message", opened.ToString())
        Dim text As String = result.Box.ToJson()
        Assert.DoesNotContain("not-in-any-message", text)
    End Sub

End Class
#End If
