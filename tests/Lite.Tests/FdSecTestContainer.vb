#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text

''' <summary>
''' A container WRITER that exists only in this test project
''' (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §13, §14).
'''
''' It lives here rather than in src/Secure on purpose. Phase 1 of the ticket implements
''' <c>unsecure</c> only, and the real writer - temporary file, flush, close, reopen, read
''' back to the digest, then rename, and only then any disposition of the original - is a
''' deferred ticket of its own, because that discipline is what decides whether the
''' feature destroys data. Shipping an unused in-memory packer next to the reader would be
''' an invitation to call it.
'''
''' What it is for: the rows of §13 that need a container the test MADE - a multi-chunk
''' round trip under a lowered profile, a chunk spliced in from a second container built
''' with the same credential, a truncation with the length fixed up to look plausible. The
''' rows that decide CONFORMANCE are checked against the catalog's committed containers,
''' which this code had no hand in producing.
'''
''' Randomness is supplied by the caller and is deterministic, so a tamper test can name a
''' byte offset and mean it.
''' </summary>
Friend NotInheritable Class FdSecTestContainer

    Private Sub New()
    End Sub

    ''' <summary>A test profile: Argon2id at 64 MiB with three passes is designed to be
    ''' expensive, and a suite that packs a dozen containers under it is unbearable. A
    ''' container written under a lowered profile is readable only under that same
    ''' profile, which is exactly why no shipped path may touch this.</summary>
    Friend Shared ReadOnly FastProfile As New FdSecProfile(threshold:=64, memoryKib:=64, iterations:=1, lanes:=1)

    ''' <summary>
    ''' Packs <paramref name="content"/> into a complete suite-1 container, following the
    ''' writer algorithm of FDSEC-BEHAVIOUR §5.1 as far as the bytes go.
    ''' </summary>
    Friend Shared Function Pack(content As Byte(), name As String, credential As String,
                                profile As FdSecProfile,
                                Optional alignment As Integer = 512,
                                Optional chunkSize As Integer = 1024,
                                Optional seed As String = "fdsec-test",
                                Optional sealedSize As ULong? = Nothing,
                                Optional declaredChunkSize As UInteger? = Nothing) As Byte()
        ' declaredChunkSize writes an S into the head that the chunks were NOT laid out
        ' with - the only way to reach a head that authenticates and then declares a chunk
        ' size no in-memory reader can hold (audit 052 G-18). The head is refused before
        ' any chunk is read, so the mismatch below it is never looked at.
        Dim random As New DeterministicBytes(seed)

        Dim salt As Byte() = random.Next(FdSecFormat.SaltSize)
        Dim fileKey As Byte() = random.Next(FdSecFormat.FileKeySize)
        Dim wrapNonce As Byte() = random.Next(FdSecFormat.NonceSize)

        ' The 28 header bytes, then the digest that binds every AEAD call in the file.
        Dim header(FdSecFormat.HeaderSize - 1) As Byte
        Array.Copy(salt, header, FdSecFormat.SaltSize)
        header(16) = FdSecFormat.FormatVersion
        header(17) = FdSecFormat.SuiteId
        header(18) = CByte(FdSecFormat.ConformanceFlags And &HFF)
        header(19) = 0
        FdSecPrimitives.WriteUInt32Le(header, 20, If(declaredChunkSize, CUInt(chunkSize)))
        FdSecPrimitives.WriteUInt32Le(header, 24, CUInt(alignment))
        Dim headerDigest As Byte() = FdSecPrimitives.Blake2b(256, header)

        Dim maskKey As Byte() = Nothing
        Dim kek As Byte() = Nothing
        FdSecKdf.DeriveKeys(FdSecKdf.NormalizeCredential(credential), salt, profile, maskKey, kek)

        ' Key slot 0: the file key WRAPPED under the credential-derived key, never the
        ' payload encrypted under the credential directly.
        Dim wrapped As Byte() = FdSecPrimitives.XChaChaSeal(kek, wrapNonce,
                                                            FdSecHead.Slot0AssociatedData(headerDigest), fileKey)
        Dim slots(FdSecFormat.SlotsSize - 1) As Byte
        slots(0) = FdSecFormat.SlotTypeCredentialWrap
        Array.Copy(wrapNonce, 0, slots, 1, FdSecFormat.NonceSize)
        Array.Copy(wrapped, 0, slots, 1 + FdSecFormat.NonceSize, wrapped.Length)

        ' The sealed metadata block, fixed at 4096 plaintext bytes so the name length is
        ' hidden completely.
        Dim nameBytes As Byte() = Encoding.UTF8.GetBytes(name.Normalize(NormalizationForm.FormC))
        Dim contentDigest As Byte() = FdSecPrimitives.Blake2b(256, content)
        Dim metaBlock(FdSecFormat.MetadataPlainSize - 1) As Byte
        metaBlock(0) = CByte(nameBytes.Length And &HFF)
        metaBlock(1) = CByte((nameBytes.Length >> 8) And &HFF)
        Array.Copy(nameBytes, 0, metaBlock, 2, nameBytes.Length)
        Dim cursor As Integer = 2 + nameBytes.Length
        ' A sealedSize override writes a size the content does not have - the only way to
        ' reach the 1.1 impossible-size refusals, which no honest writer can produce.
        Array.Copy(FdSecPrimitives.UInt64Le(If(sealedSize, CULng(content.Length))), 0, metaBlock, cursor, 8)
        Array.Copy(FdSecPrimitives.UInt64Le(ToFileTime(New Date(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc))), 0, metaBlock, cursor + 8, 8)
        Array.Copy(FdSecPrimitives.UInt64Le(ToFileTime(New Date(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc))), 0, metaBlock, cursor + 16, 8)
        Array.Copy(FdSecPrimitives.UInt64Le(ToFileTime(New Date(2025, 6, 7, 8, 9, 10, DateTimeKind.Utc))), 0, metaBlock, cursor + 24, 8)
        Array.Copy(FdSecPrimitives.UInt64Le(ToFileTime(New Date(2026, 8, 19, 23, 59, 59, DateTimeKind.Utc))), 0, metaBlock, cursor + 32, 8)
        Array.Copy(contentDigest, 0, metaBlock, cursor + 40, FdSecFormat.DigestSize)
        Dim padStart As Integer = cursor + 40 + FdSecFormat.DigestSize
        Array.Copy(random.Next(FdSecFormat.MetadataPlainSize - padStart), 0, metaBlock, padStart,
                   FdSecFormat.MetadataPlainSize - padStart)

        Dim sealedMeta As Byte() = FdSecPrimitives.XChaChaSeal(
            fileKey,
            FdSecHead.MetadataNonce(fileKey),
            FdSecHead.MetadataAssociatedData(headerDigest),
            metaBlock)

        ' The head goes to disk masked from the end of the salt onwards.
        Dim head(FdSecFormat.HeadSize - 1) As Byte
        Array.Copy(header, head, FdSecFormat.HeaderSize)
        Array.Copy(slots, 0, head, FdSecFormat.HeaderSize, FdSecFormat.SlotsSize)
        FdSecPrimitives.ChaCha20Xor(maskKey, New Byte(11) {}, head, FdSecFormat.MaskOffset, FdSecFormat.MaskLength)

        Dim capacity As Integer = chunkSize - FdSecFormat.TagSize
        Dim count As Integer = Math.Max(1, (content.Length + capacity - 1) \ capacity)
        ' Parenthesised: VB binds * tighter than \, so "x \ a * a" divides by a squared.
        Dim preLen As Integer = ((FdSecFormat.HeadSize + FdSecFormat.MetadataSize + alignment - 1) \ alignment) * alignment

        Using buffer As New IO.MemoryStream()
            buffer.Write(head, 0, head.Length)
            buffer.Write(sealedMeta, 0, sealedMeta.Length)
            Dim alignPad As Byte() = random.Next(preLen - FdSecFormat.HeadSize - FdSecFormat.MetadataSize)
            buffer.Write(alignPad, 0, alignPad.Length)

            For index As Integer = 0 To count - 1
                Dim isLast As Boolean = (index = count - 1)
                Dim start As Integer = index * capacity
                Dim length As Integer = If(isLast, content.Length - start, capacity)
                Dim plain(Math.Max(0, length) - 1) As Byte
                If length > 0 Then Array.Copy(content, start, plain, 0, length)

                Dim sealedChunk As Byte() = FdSecPrimitives.XChaChaSeal(
                    fileKey,
                    FdSecHead.ChunkNonce(fileKey, index),
                    FdSecHead.ChunkAssociatedData(headerDigest, index, count, isLast, length),
                    plain)
                buffer.Write(sealedChunk, 0, sealedChunk.Length)
                ' A full chunk fills its slot exactly (P + 16 = S), so only the last one
                ' is short and nothing has to be padded between chunks.
            Next

            Dim tail As Integer = CInt((alignment - buffer.Length Mod alignment) Mod alignment)
            If tail > 0 Then
                Dim tailPad As Byte() = random.Next(tail)
                buffer.Write(tailPad, 0, tailPad.Length)
            End If
            Return buffer.ToArray()
        End Using
    End Function

    ''' <summary>Where chunk <paramref name="index"/> starts, for a test that wants to
    ''' flip a bit inside one or swap two of them.</summary>
    Friend Shared Function ChunkOffset(index As Integer, alignment As Integer, chunkSize As Integer) As Integer
        Dim preLen As Integer = ((FdSecFormat.HeadSize + FdSecFormat.MetadataSize + alignment - 1) \ alignment) * alignment
        Return preLen + index * chunkSize
    End Function

    Friend Shared Function ToFileTime(value As Date) As ULong
        Return CULng(value.ToFileTimeUtc())
    End Function

    ''' <summary>
    ''' A fixed keystream standing in for the CSPRNG. Deterministic on purpose: a tamper
    ''' test that names an offset needs the same container every run. The shipped format
    ''' takes its randomness from the operating system and nothing here is ever reachable
    ''' from a shipped path.
    ''' </summary>
    Private NotInheritable Class DeterministicBytes
        Private ReadOnly key As Byte()
        Private counterBlock As Long

        Friend Sub New(seed As String)
            key = FdSecPrimitives.Blake2b(256, Encoding.UTF8.GetBytes(seed))
        End Sub

        Friend Function [Next](count As Integer) As Byte()
            If count <= 0 Then Return Array.Empty(Of Byte)()
            Dim output(count - 1) As Byte
            Dim nonce(11) As Byte
            Array.Copy(FdSecPrimitives.UInt64Le(CULng(counterBlock)), 0, nonce, 0, 8)
            counterBlock += 1
            FdSecPrimitives.ChaCha20Xor(key, nonce, output, 0, output.Length)
            Return output
        End Function
    End Class

End Class
#End If
