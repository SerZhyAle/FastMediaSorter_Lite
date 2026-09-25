#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text
Imports Org.BouncyCastle.Crypto
Imports Org.BouncyCastle.Crypto.Digests
Imports Org.BouncyCastle.Crypto.Engines
Imports Org.BouncyCastle.Crypto.Generators
Imports Org.BouncyCastle.Crypto.Modes
Imports Org.BouncyCastle.Crypto.Parameters

''' <summary>
''' The four primitives the FDSEC container format is built from, behind one small
''' surface (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §5.1). This is the
''' ONLY file in the repository that names a cryptography library.
'''
''' Why a library at all: the .NET base class library carries none of them. There is no
''' Argon2 of any kind and no BLAKE2b, and
''' <c>System.Security.Cryptography.ChaCha20Poly1305</c> is the 12-byte IETF variant
''' backed on Windows by a CNG algorithm our support floor (Windows 10 1607) predates -
''' so its <c>IsSupported</c> cannot be relied on and one library has to carry all four
''' anyway. BouncyCastle is fully managed, which keeps the single-file publish a single
''' file and lays nothing down per architecture next to the exe.
'''
''' The one construction written here rather than taken from the library is
''' <see cref="HChaCha20"/>, the sub-key derivation that turns the IETF AEAD into
''' XChaCha20-Poly1305. BouncyCastle 2.x exposes no XChaCha20-Poly1305; if a maintained
''' library ever does, use it and delete ours. The catalog's vectors prove this end to
''' end, so it is a checked forty lines rather than a hopeful one.
'''
''' Five of the seven porting mistakes of FDSEC-BEHAVIOUR §13.1 live in this file and
''' are each named at the line that avoids them.
''' </summary>
Friend Module FdSecPrimitives

    ''' <summary>
    ''' Argon2id, RFC 9106, version 0x13, with all four parameters settable - mistake 1:
    ''' libsodium's crypto_pwhash fixes parallelism at 1 and cannot produce this
    ''' format's P = 4 at all, so a libsodium binding is not an answer here.
    ''' <paramref name="memoryKib"/> is in KiB, which is what the contract states and
    ''' what several other APIs take in bytes.
    ''' </summary>
    Friend Function Argon2id(password As Byte(), salt As Byte(),
                             iterations As Integer, memoryKib As Integer, lanes As Integer,
                             outputLength As Integer) As Byte()
        Dim builder As New Argon2Parameters.Builder(Argon2Parameters.Argon2id)
        builder.WithVersion(Argon2Parameters.Version13)
        builder.WithIterations(iterations)
        builder.WithMemoryAsKB(memoryKib)
        builder.WithParallelism(lanes)
        builder.WithSalt(salt)

        Dim generator As New Argon2BytesGenerator()
        generator.Init(builder.Build())
        Dim output(outputLength - 1) As Byte
        ' The byte-array overload on purpose: the string overload would apply an
        ' encoding of the library's choosing, and the contract fixes UTF-8 after NFC.
        generator.GenerateBytes(password, output)
        Return output
    End Function

    ''' <summary>
    ''' Unkeyed BLAKE2b. <paramref name="outputBits"/> is in BITS - BouncyCastle's
    ''' one-argument constructor is a bit length while its four-argument keyed
    ''' constructor is a byte length, and mixing them up produces a digest of the right
    ''' shape and the wrong value.
    ''' </summary>
    Friend Function Blake2b(outputBits As Integer, ParamArray parts As Byte()()) As Byte()
        Dim digest As New Blake2bDigest(outputBits)
        Return Finish(digest, outputBits \ 8, parts)
    End Function

    ''' <summary>
    ''' Keyed BLAKE2b-256 - mistake 2: this is the primitive's OWN key input, not
    ''' HMAC-BLAKE2b. The .NET ecosystem makes that mistake easy to make (the Konscious
    ''' type that does exactly the wrong thing is called <c>HMACBlake2B</c>), and the
    ''' result is a sub-key that is plausible, stable and incompatible with every other
    ''' implementation of this format.
    ''' </summary>
    Friend Function Blake2bKeyed(key As Byte(), ParamArray parts As Byte()()) As Byte()
        Const outputBytes As Integer = 32
        Dim digest As New Blake2bDigest(key, outputBytes, Nothing, Nothing)
        Return Finish(digest, outputBytes, parts)
    End Function

    Private Function Finish(digest As IDigest, outputBytes As Integer, parts As Byte()()) As Byte()
        For Each part As Byte() In parts
            If part IsNot Nothing AndAlso part.Length > 0 Then digest.BlockUpdate(part, 0, part.Length)
        Next
        Dim output(outputBytes - 1) As Byte
        digest.DoFinal(output, 0)
        Return output
    End Function

    ''' <summary>
    ''' Raw, unauthenticated ChaCha20 keystream XORed over <paramref name="buffer"/>:
    ''' RFC 8439, a 12-byte nonce and the block counter starting at ZERO - mistake 3.
    ''' Some APIs start the counter at 1 because that is what the AEAD construction
    ''' needs; that produces a different keystream and a head that unmasks to noise
    ''' under the right credential.
    ''' </summary>
    Friend Sub ChaCha20Xor(key As Byte(), nonce12 As Byte(), buffer As Byte(), offset As Integer, length As Integer)
        Dim engine As New ChaCha7539Engine()
        engine.Init(True, New ParametersWithIV(New KeyParameter(key), nonce12))
        engine.ProcessBytes(buffer, offset, length, buffer, offset)
    End Sub

    ''' <summary>
    ''' XChaCha20-Poly1305 seal - mistake 4: the nonce is 24 bytes, not the IETF 12.
    ''' Returns ciphertext with the 16-byte tag appended.
    ''' </summary>
    Friend Function XChaChaSeal(key As Byte(), nonce24 As Byte(), associatedData As Byte(), plaintext As Byte()) As Byte()
        Dim aead As ChaCha20Poly1305 = NewIetfAead(key, nonce24, associatedData, forEncryption:=True)
        Dim output(aead.GetOutputSize(plaintext.Length) - 1) As Byte
        Dim written As Integer = aead.ProcessBytes(plaintext, 0, plaintext.Length, output, 0)
        written += aead.DoFinal(output, written)
        If written <> output.Length Then ReDim Preserve output(written - 1)
        Return output
    End Function

    ''' <summary>
    ''' XChaCha20-Poly1305 open. Returns Nothing when the tag does not hold - the caller
    ''' decides which outcome class that is, because the answer depends on WHERE in the
    ''' container it happened (key slot 0 versus a chunk of an already-authenticated
    ''' file), and this module has no business inventing a message.
    ''' </summary>
    Friend Function XChaChaOpen(key As Byte(), nonce24 As Byte(), associatedData As Byte(), ciphertext As Byte()) As Byte()
        Try
            Dim aead As ChaCha20Poly1305 = NewIetfAead(key, nonce24, associatedData, forEncryption:=False)
            Dim output(Math.Max(0, aead.GetOutputSize(ciphertext.Length)) - 1) As Byte
            Dim written As Integer = aead.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0)
            written += aead.DoFinal(output, written)
            If written <> output.Length Then ReDim Preserve output(written - 1)
            Return output
        Catch ex As InvalidCipherTextException
            Return Nothing
        Catch ex As ArgumentException
            ' A ciphertext shorter than the tag cannot be opened, and asking is not a crash.
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' The XChaCha construction: a sub-key from the first 16 nonce bytes, then the
    ''' IETF AEAD under a 12-byte nonce of four zero bytes and the remaining eight.
    ''' </summary>
    Private Function NewIetfAead(key As Byte(), nonce24 As Byte(), associatedData As Byte(),
                                 forEncryption As Boolean) As ChaCha20Poly1305
        If nonce24 Is Nothing OrElse nonce24.Length <> 24 Then
            Throw New ArgumentException("FDSEC nonces are 24 bytes", NameOf(nonce24))
        End If

        Dim subKey As Byte() = HChaCha20(key, nonce24, 0)
        Dim ietfNonce(11) As Byte
        Array.Copy(nonce24, 16, ietfNonce, 4, 8)

        Dim aead As New ChaCha20Poly1305()
        aead.Init(forEncryption, New AeadParameters(New KeyParameter(subKey), 128, ietfNonce, associatedData))
        Array.Clear(subKey, 0, subKey.Length)
        Return aead
    End Function

    ''' <summary>
    ''' HChaCha20 (draft-irtf-cfrg-xchacha section 2.2): twenty rounds over the ChaCha
    ''' state built from the key and sixteen nonce bytes, WITHOUT the final addition of
    ''' the input state, taking words 0..3 and 12..15 as the sub-key.
    '''
    ''' The missing final addition is the whole point of the construction and the
    ''' easiest thing to get wrong by copying a ChaCha20 block function - the vectors
    ''' catch it immediately, which is why this is written here rather than trusted.
    ''' </summary>
    Private Function HChaCha20(key32 As Byte(), nonce As Byte(), nonceOffset As Integer) As Byte()
        Dim state(15) As UInteger
        state(0) = &H61707865UI  ' "expa"
        state(1) = &H3320646EUI  ' "nd 3"
        state(2) = &H79622D32UI  ' "2-by"
        state(3) = &H6B206574UI  ' "te k"
        For i As Integer = 0 To 7
            state(4 + i) = ReadUInt32Le(key32, i * 4)
        Next
        For i As Integer = 0 To 3
            state(12 + i) = ReadUInt32Le(nonce, nonceOffset + i * 4)
        Next

        For round As Integer = 0 To 9
            QuarterRound(state, 0, 4, 8, 12)
            QuarterRound(state, 1, 5, 9, 13)
            QuarterRound(state, 2, 6, 10, 14)
            QuarterRound(state, 3, 7, 11, 15)
            QuarterRound(state, 0, 5, 10, 15)
            QuarterRound(state, 1, 6, 11, 12)
            QuarterRound(state, 2, 7, 8, 13)
            QuarterRound(state, 3, 4, 9, 14)
        Next

        Dim output(31) As Byte
        For i As Integer = 0 To 3
            WriteUInt32Le(output, i * 4, state(i))
            WriteUInt32Le(output, 16 + i * 4, state(12 + i))
        Next
        Return output
    End Function

    Private Sub QuarterRound(state As UInteger(), a As Integer, b As Integer, c As Integer, d As Integer)
        state(a) = Add32(state(a), state(b)) : state(d) = RotateLeft(state(d) Xor state(a), 16)
        state(c) = Add32(state(c), state(d)) : state(b) = RotateLeft(state(b) Xor state(c), 12)
        state(a) = Add32(state(a), state(b)) : state(d) = RotateLeft(state(d) Xor state(a), 8)
        state(c) = Add32(state(c), state(d)) : state(b) = RotateLeft(state(b) Xor state(c), 7)
    End Sub

    ''' <summary>ChaCha addition is modulo 2^32. VB checks integer overflow by default,
    ''' so the widening has to happen BEFORE the addition, not after it.</summary>
    Private Function Add32(left As UInteger, right As UInteger) As UInteger
        Return CUInt((CULng(left) + CULng(right)) And &HFFFFFFFFUL)
    End Function

    Private Function RotateLeft(value As UInteger, bits As Integer) As UInteger
        Return CUInt(((CULng(value) << bits) Or (CULng(value) >> (32 - bits))) And &HFFFFFFFFUL)
    End Function

    ''' <summary>
    ''' Mistake 6: little-endian EVERYWHERE, including inside nonce context strings and
    ''' associated data. These two helpers are what the whole module reads and writes
    ''' integers through, so there is one place for that to be true.
    ''' </summary>
    Friend Function ReadUInt32Le(buffer As Byte(), offset As Integer) As UInteger
        Return CUInt(buffer(offset)) Or
               (CUInt(buffer(offset + 1)) << 8) Or
               (CUInt(buffer(offset + 2)) << 16) Or
               (CUInt(buffer(offset + 3)) << 24)
    End Function

    Friend Sub WriteUInt32Le(buffer As Byte(), offset As Integer, value As UInteger)
        buffer(offset) = CByte(value And &HFFUI)
        buffer(offset + 1) = CByte((value >> 8) And &HFFUI)
        buffer(offset + 2) = CByte((value >> 16) And &HFFUI)
        buffer(offset + 3) = CByte((value >> 24) And &HFFUI)
    End Sub

    Friend Function ReadUInt16Le(buffer As Byte(), offset As Integer) As Integer
        Return CInt(buffer(offset)) Or (CInt(buffer(offset + 1)) << 8)
    End Function

    Friend Function ReadUInt64Le(buffer As Byte(), offset As Integer) As ULong
        Dim value As ULong = 0
        For i As Integer = 7 To 0 Step -1
            value = (value << 8) Or CULng(buffer(offset + i))
        Next
        Return value
    End Function

    Friend Function UInt64Le(value As ULong) As Byte()
        Dim buffer(7) As Byte
        Dim work As ULong = value
        For i As Integer = 0 To 7
            buffer(i) = CByte(work And &HFFUL)
            work >>= 8
        Next
        Return buffer
    End Function

    Friend Function Ascii(text As String) As Byte()
        ' Every label in this format is ASCII by the contract; Encoding.UTF8 gives the
        ' same bytes and one fewer encoder to reason about.
        Return Encoding.UTF8.GetBytes(text)
    End Function

    ''' <summary>
    ''' Overwrite key material after use, as far as a garbage-collected runtime allows.
    ''' FDSEC-BEHAVIOUR §8.2 requires this be described as BEST EFFORT and never as a
    ''' guarantee - the runtime may already have copied the array during a collection.
    ''' </summary>
    Friend Sub Wipe(buffer As Byte())
        If buffer IsNot Nothing Then Array.Clear(buffer, 0, buffer.Length)
    End Sub

End Module
#End If
