#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

''' <summary>
''' A minimal suite-1 container WRITER for EPHEMERAL containers only
''' (242_SPECIFICATION_ANIMATED_GRAPHICS_THROUGH_TEMPORARY_ENCRYPTED_VIDEO.md rule 15,
''' decision Q242-1), written against FDSEC-FORMAT section 10.
'''
''' What it is: a pure function from a FINISHED plaintext byte array, a true name and a
''' credential to the bytes of a conformant container on a destination stream. The sealed
''' metadata carries the real size and the digest of the whole plaintext, and every chunk's
''' associated data carries the chunk count, so a container cannot be sealed while the
''' bytes are still being produced (242 finding F1) - the caller captures the plaintext in
''' memory first.
'''
''' What it deliberately is NOT - the structural promise that keeps the "no writer" owner
''' decision narrowed to exactly this case:
''' - it has NO notion of an original file: no source path, no delete, no wipe, no
'''   disposition (FDSEC-BEHAVIOUR section 6 does not apply, because nothing is packed FROM
'''   a user's file). The timestamps inside the metadata are the moment of encoding, not
'''   anyone's file times;
''' - it does not read the container back and does not rename or clean up anything: the
'''   caller owns the destination, reads it back (FdSecContainer.OpenFile + VerifyAll) and
'''   discards it on any failure. A throw leaves a PARTIAL container on the destination;
'''   never keep one;
''' - it has no user-facing "pack this file" surface, which stays a ticket of its own.
'''
''' Conformance is decided by the catalog's vectors (FdSecWriterTests reproduces the
''' published containers byte for byte with the vectors' randomness fed in the draw order
''' below), never by reading this code. The primitives, constants, nonce and associated-data
''' derivations are the READER's own (FdSecPrimitives, FdSecFormat, FdSecHead,
''' FdSecKdf), reused, not re-derived.
'''
''' Like the rest of src/Secure it has no product dependencies: it never logs, never shows
''' a dialog and never reads a setting.
''' </summary>
Public NotInheritable Class FdSecWriter

    Private Sub New()
    End Sub

    ''' <summary>The format's default chunk slot S (FDSEC-FORMAT section 3): 1 MiB.</summary>
    Friend Const DefaultChunkSize As Integer = 1048576

    ''' <summary>The format's default cluster alignment A (FDSEC-FORMAT section 3): 4096.
    ''' The finished file is rounded up to this, hiding the exact size.</summary>
    Friend Const DefaultAlignment As Integer = 4096

    ''' <summary>
    ''' Seals <paramref name="plaintext"/> into a suite-1 container written to
    ''' <paramref name="destination"/> and returns the container's length in bytes.
    '''
    ''' <paramref name="destination"/> needs ONLY <c>Write</c> (and <c>Flush</c>, which is
    ''' called once at the end): the writer is strictly sequential - head, sealed metadata,
    ''' alignment pad, chunks 0..k-1, tail pad - and never seeks, never reads and never asks
    ''' for <c>Length</c> or <c>Position</c>, so a pipe or a forward-only stream is fine. It
    ''' is not disposed here; its owner decides when the file goes away.
    '''
    ''' <paramref name="credential"/> is used as given after Unicode NFC (no trimming, FDSEC-FORMAT
    ''' section 6): at least 64 UTF-8 bytes takes the cheap salted-BLAKE2b branch, a shorter one
    ''' pays the Argon2id derivation of the version-1 profile. The empty credential is accepted
    ''' by the format and is obfuscation with no secrecy; a caller that wants secrecy never
    ''' passes one.
    '''
    ''' The randomness is always the operating system CSPRNG: the injectable source the
    ''' conformance tests feed in the contract's draw order is reachable only through the
    ''' <c>Friend</c> entries below, never through this public surface (a source that is not a
    ''' CSPRNG makes a container as weak as the source).
    '''
    ''' Memory: the plaintext is read in place (never copied whole); what the writer holds beyond
    ''' it is two chunk buffers (plain and sealed, about 1 MiB each at the default chunk size).
    ''' </summary>
    ''' <param name="trueName">The name sealed inside, without any path. Refused (never
    ''' sanitised) when the reader would refuse it, or when it exceeds 3000 UTF-8 bytes.</param>
    ''' <exception cref="ArgumentException">The name, or a destination that cannot be written.</exception>
    ''' <exception cref="OperationCanceledException">Cancelled between chunks.</exception>
    Public Shared Function Write(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credential As String,
                                 destination As Stream,
                                 Optional cancellationToken As CancellationToken = Nothing) As Long
        Return WriteSeeded(plaintext, trueName, credential, destination, Nothing, cancellationToken)
    End Function

    ''' <summary>The same write for a credential held as bytes (<see cref="FdSecCredential"/>):
    ''' a copy is derived from and wiped, the held credential is untouched. The write itself adds
    ''' no immutable String copy of the credential; what text the caller built the credential
    ''' from is the caller's to keep (a per-run credential is held in memory only, never
    ''' persisted or logged - FDSEC-BEHAVIOUR section 8.2 is best effort on a managed runtime).</summary>
    Public Shared Function Write(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credential As FdSecCredential,
                                 destination As Stream,
                                 Optional cancellationToken As CancellationToken = Nothing) As Long
        Return WriteSeeded(plaintext, trueName, credential, destination, Nothing, cancellationToken)
    End Function

    ''' <summary>
    ''' TEST-ONLY entry with the injectable randomness source: <paramref name="fillRandom"/> is handed
    ''' an array and must fill ALL of it, and is called in the contract's draw order - salt (16), file
    ''' key (32), wrap nonce (24), metadata padding, alignment padding, tail padding; a pad of zero
    ''' bytes is not requested. Nothing means the operating system CSPRNG. No shipped path calls it.
    ''' </summary>
    Friend Shared Function WriteSeeded(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credential As String,
                                       destination As Stream, fillRandom As Action(Of Byte()),
                                       Optional cancellationToken As CancellationToken = Nothing) As Long
        Dim encodedAt As Date = Date.UtcNow
        Return WriteCore(plaintext, trueName, FdSecKdf.NormalizeCredential(credential), destination, fillRandom,
                         DefaultChunkSize, DefaultAlignment, encodedAt, encodedAt, encodedAt, encodedAt,
                         cancellationToken)
    End Function

    ''' <summary>TEST-ONLY, as the String overload above, for a credential held as bytes.</summary>
    Friend Shared Function WriteSeeded(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credential As FdSecCredential,
                                       destination As Stream, fillRandom As Action(Of Byte()),
                                       Optional cancellationToken As CancellationToken = Nothing) As Long
        If credential Is Nothing Then Throw New ArgumentNullException(NameOf(credential))
        Dim encodedAt As Date = Date.UtcNow
        Return WriteCore(plaintext, trueName, credential.CopyBytes(), destination, fillRandom,
                         DefaultChunkSize, DefaultAlignment, encodedAt, encodedAt, encodedAt, encodedAt,
                         cancellationToken)
    End Function

    ''' <summary>
    ''' The write with the per-file layout and the four timestamps spelled out - the seam the
    ''' conformance test needs, because the catalog's multi-chunk vector uses a small chunk size
    ''' and alignment and its own timestamps. No shipped path calls it: S and A are legitimate
    ''' header values of the format, but the product always writes the defaults. (The KDF profile
    ''' is NOT a parameter here at all: it belongs to the format version and is not ours to tune.)
    ''' Takes ownership of <paramref name="credentialUtf8"/> and wipes it.
    ''' </summary>
    Friend Shared Function WriteWithLayout(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credentialUtf8 As Byte(),
                                           destination As Stream, fillRandom As Action(Of Byte()),
                                           chunkSize As Integer, alignment As Integer,
                                           encodedAt As Date, createdAt As Date, accessedAt As Date, modifiedAt As Date,
                                           Optional cancellationToken As CancellationToken = Nothing) As Long
        Return WriteCore(plaintext, trueName, credentialUtf8, destination, fillRandom,
                         chunkSize, alignment, encodedAt, createdAt, accessedAt, modifiedAt, cancellationToken)
    End Function

    Private Shared Function WriteCore(plaintext As ReadOnlyMemory(Of Byte), trueName As String, credentialUtf8 As Byte(),
                                      destination As Stream, fillRandom As Action(Of Byte()),
                                      chunkSize As Integer, alignment As Integer,
                                      encodedAt As Date, createdAt As Date, accessedAt As Date, modifiedAt As Date,
                                      cancellationToken As CancellationToken) As Long
        Dim fileKey As Byte() = Nothing
        Dim maskKey As Byte() = Nothing
        Dim kek As Byte() = Nothing
        Dim head As Byte() = Nothing
        Dim metaBlock As Byte() = Nothing
        Dim fullChunk As Byte() = Nothing
        ' The last chunk is a fresh, shorter array of its own; held here (not inside the loop) so
        ' the finally wipes it on EVERY exit, including a failed seal or a failed write.
        Dim lastChunk As Byte() = Nothing
        Try
            If destination Is Nothing Then Throw New ArgumentNullException(NameOf(destination))
            If Not destination.CanWrite Then Throw New ArgumentException("the destination must be writable", NameOf(destination))
            ValidateLayout(chunkSize, alignment)
            Dim nameBytes As Byte() = SealedNameBytes(trueName)
            cancellationToken.ThrowIfCancellationRequested()

            ' 1. Normalised credential (done by the caller), then the three CSPRNG draws, in
            '    the contract's order: salt, file key, wrap nonce.
            Dim salt(FdSecFormat.SaltSize - 1) As Byte
            Draw(fillRandom, salt)
            fileKey = New Byte(FdSecFormat.FileKeySize - 1) {}
            Draw(fillRandom, fileKey)
            Dim wrapNonce(FdSecFormat.NonceSize - 1) As Byte
            Draw(fillRandom, wrapNonce)

            ' 2. D = BLAKE2b-256 of the whole plaintext, over the caller's own memory.
            Dim size As Long = plaintext.Length
            Dim digest As Byte() = DigestOf(plaintext)

            ' 3. The 28 header bytes and the digest that binds every AEAD call in the file.
            Dim header(FdSecFormat.HeaderSize - 1) As Byte
            Array.Copy(salt, header, FdSecFormat.SaltSize)
            header(16) = FdSecFormat.FormatVersion
            header(17) = FdSecFormat.SuiteId
            header(18) = CByte(FdSecFormat.ConformanceFlags And &HFF)
            header(19) = CByte((FdSecFormat.ConformanceFlags >> 8) And &HFF)
            FdSecPrimitives.WriteUInt32Le(header, 20, CUInt(chunkSize))
            FdSecPrimitives.WriteUInt32Le(header, 24, CUInt(alignment))
            Dim headerDigest As Byte() = FdSecPrimitives.Blake2b(256, header)

            ' The version-1 profile (Nothing): the threshold of 64 UTF-8 bytes picks the branch.
            FdSecKdf.DeriveKeys(credentialUtf8, salt, Nothing, maskKey, kek)

            ' Key slot 0 holds the file key WRAPPED under the credential-derived key, never the
            ' payload encrypted under the credential itself. Slots 1..7 are typed empty: all zero.
            Dim wrapped As Byte() = FdSecPrimitives.XChaChaSeal(kek, wrapNonce,
                                                                FdSecHead.Slot0AssociatedData(headerDigest), fileKey)
            Dim slots(FdSecFormat.SlotsSize - 1) As Byte
            slots(0) = FdSecFormat.SlotTypeCredentialWrap
            Array.Copy(wrapNonce, 0, slots, 1, FdSecFormat.NonceSize)
            Array.Copy(wrapped, 0, slots, 1 + FdSecFormat.NonceSize, wrapped.Length)

            ' The sealed metadata: always 4096 plaintext bytes, so the name length is hidden
            ' completely; the padding after the digest is the fourth CSPRNG draw.
            metaBlock = New Byte(FdSecFormat.MetadataPlainSize - 1) {}
            metaBlock(0) = CByte(nameBytes.Length And &HFF)
            metaBlock(1) = CByte((nameBytes.Length >> 8) And &HFF)
            Array.Copy(nameBytes, 0, metaBlock, 2, nameBytes.Length)
            Dim cursor As Integer = 2 + nameBytes.Length
            Array.Copy(FdSecPrimitives.UInt64Le(CULng(size)), 0, metaBlock, cursor, 8)
            Array.Copy(FdSecPrimitives.UInt64Le(WindowsTimeOf(encodedAt)), 0, metaBlock, cursor + 8, 8)
            Array.Copy(FdSecPrimitives.UInt64Le(WindowsTimeOf(createdAt)), 0, metaBlock, cursor + 16, 8)
            Array.Copy(FdSecPrimitives.UInt64Le(WindowsTimeOf(accessedAt)), 0, metaBlock, cursor + 24, 8)
            Array.Copy(FdSecPrimitives.UInt64Le(WindowsTimeOf(modifiedAt)), 0, metaBlock, cursor + 32, 8)
            Array.Copy(digest, 0, metaBlock, cursor + 40, FdSecFormat.DigestSize)
            Dim padStart As Integer = cursor + 40 + FdSecFormat.DigestSize
            Dim metaPad(FdSecFormat.MetadataPlainSize - padStart - 1) As Byte
            Draw(fillRandom, metaPad)
            Array.Copy(metaPad, 0, metaBlock, padStart, metaPad.Length)

            Dim sealedMeta As Byte() = FdSecPrimitives.XChaChaSeal(
                fileKey,
                FdSecHead.MetadataNonce(fileKey),
                FdSecHead.MetadataAssociatedData(headerDigest),
                metaBlock)

            ' The head goes to disk masked from the end of the salt onwards (keystream counter
            ' zero, a 12-byte zero nonce).
            head = New Byte(FdSecFormat.HeadSize - 1) {}
            Array.Copy(header, head, FdSecFormat.HeaderSize)
            Array.Copy(slots, 0, head, FdSecFormat.HeaderSize, FdSecFormat.SlotsSize)
            FdSecPrimitives.ChaCha20Xor(maskKey, New Byte(11) {}, head, FdSecFormat.MaskOffset, FdSecFormat.MaskLength)

            ' 4. Layout arithmetic (FDSEC-FORMAT sections 3, 8, 9). Parenthesised on purpose: VB
            '    binds * tighter than \, so "x \ a * a" would divide by a squared.
            Dim capacity As Long = chunkSize - FdSecFormat.TagSize
            Dim count As Long = Math.Max(1L, (size + capacity - 1) \ capacity)
            Dim lastLength As Long = size - (count - 1) * capacity
            Dim preLength As Long = ((CLng(FdSecFormat.HeadSize) + FdSecFormat.MetadataSize + alignment - 1) \ alignment) * alignment
            Dim payloadEnd As Long = preLength + (count - 1) * chunkSize + lastLength + FdSecFormat.TagSize
            Dim tailLength As Long = (alignment - payloadEnd Mod alignment) Mod alignment

            destination.Write(head, 0, head.Length)
            destination.Write(sealedMeta, 0, sealedMeta.Length)
            Dim alignPad(CInt(preLength - FdSecFormat.HeadSize - FdSecFormat.MetadataSize) - 1) As Byte
            Draw(fillRandom, alignPad)
            If alignPad.Length > 0 Then destination.Write(alignPad, 0, alignPad.Length)

            ' Each chunk is sealed on its own, bound to its index, the chunk count, the header,
            ' its length and its last-or-not position. A full chunk fills its slot exactly
            ' (P + 16 = S), so only the last one is short and nothing is padded between them.
            For index As Long = 0 To count - 1
                cancellationToken.ThrowIfCancellationRequested()
                Dim isLast As Boolean = (index = count - 1)
                Dim start As Long = index * capacity
                Dim length As Long = If(isLast, lastLength, capacity)

                Dim chunkPlain As Byte()
                If isLast Then
                    lastChunk = New Byte(CInt(length) - 1) {}
                    chunkPlain = lastChunk
                Else
                    ' One buffer serves every full chunk; it is wiped once, at the end.
                    If fullChunk Is Nothing Then fullChunk = New Byte(CInt(capacity) - 1) {}
                    chunkPlain = fullChunk
                End If
                ' Memory, not Span: VB cannot name a ref struct, and this copies the same bytes.
                If length > 0 Then plaintext.Slice(CInt(start), CInt(length)).CopyTo(New Memory(Of Byte)(chunkPlain))

                Dim sealedChunk As Byte() = FdSecPrimitives.XChaChaSeal(
                    fileKey,
                    FdSecHead.ChunkNonce(fileKey, index),
                    FdSecHead.ChunkAssociatedData(headerDigest, index, count, isLast, length),
                    chunkPlain)
                destination.Write(sealedChunk, 0, sealedChunk.Length)
            Next

            Dim tailPad(CInt(tailLength) - 1) As Byte
            Draw(fillRandom, tailPad)
            If tailPad.Length > 0 Then destination.Write(tailPad, 0, tailPad.Length)

            ' The OS must see every byte before the caller reopens the file to read it back.
            destination.Flush()
            Return payloadEnd + tailLength
        Finally
            ' Every exit wipes what it holds, as far as a garbage-collected runtime allows
            ' (best effort, FDSEC-BEHAVIOUR section 8.2): the credential, the keys, the
            ' plaintext metadata block and both plaintext chunk buffers (the shared full-chunk
            ' one and the last chunk's own).
            FdSecPrimitives.Wipe(credentialUtf8)
            FdSecPrimitives.Wipe(fileKey)
            FdSecPrimitives.Wipe(maskKey)
            FdSecPrimitives.Wipe(kek)
            FdSecPrimitives.Wipe(head)
            FdSecPrimitives.Wipe(metaBlock)
            FdSecPrimitives.Wipe(fullChunk)
            FdSecPrimitives.Wipe(lastChunk)
        End Try
    End Function

    ''' <summary>
    ''' D = BLAKE2b-256 of the plaintext, hashed where it lies. An array-backed memory (a
    ''' byte array, or a slice of one such as a MemoryStream buffer) costs no copy at all; a
    ''' memory that is not backed by an array (native memory) costs exactly one.
    ''' </summary>
    Private Shared Function DigestOf(plaintext As ReadOnlyMemory(Of Byte)) As Byte()
        Dim segment As ArraySegment(Of Byte) = Nothing
        If MemoryMarshal.TryGetArray(plaintext, segment) Then
            Return FdSecPrimitives.Blake2b256(segment.Array, segment.Offset, segment.Count)
        End If
        Dim copy As Byte() = plaintext.ToArray()
        Try
            Return FdSecPrimitives.Blake2b256(copy, 0, copy.Length)
        Finally
            FdSecPrimitives.Wipe(copy)
        End Try
    End Function

    ''' <summary>One CSPRNG draw. A pad that turns out to be zero bytes long is not requested,
    ''' so a scripted source sees exactly the contract's draws and nothing else.</summary>
    Private Shared Sub Draw(fillRandom As Action(Of Byte()), buffer As Byte())
        If buffer.Length = 0 Then Return
        If fillRandom Is Nothing Then
            RandomNumberGenerator.Fill(buffer)
        Else
            fillRandom(buffer)
        End If
    End Sub

    ''' <summary>FDSEC-FORMAT section 9: S is a multiple of A and at least A, A is a power of two
    ''' in [512, 2 MiB]. Also the reader's own memory limit for one chunk slot, because a
    ''' container this reader cannot hold is not worth writing.</summary>
    Private Shared Sub ValidateLayout(chunkSize As Integer, alignment As Integer)
        If alignment < FdSecFormat.MinAlignment OrElse alignment > FdSecFormat.MaxAlignment OrElse
           (alignment And (alignment - 1)) <> 0 Then
            Throw New ArgumentOutOfRangeException(NameOf(alignment), "the cluster alignment must be a power of two in [512, 2 MiB]")
        End If
        If chunkSize < alignment OrElse chunkSize Mod alignment <> 0 OrElse chunkSize > FdSecFormat.MaxReadableChunkSize Then
            Throw New ArgumentOutOfRangeException(NameOf(chunkSize), "the chunk size must be a multiple of the alignment")
        End If
    End Sub

    ''' <summary>
    ''' The sealed name: NFC, UTF-8, 1..3000 bytes (FDSEC-FORMAT section 7: a writer refuses a
    ''' longer name rather than truncating it), and acceptable to the READER's own name rule -
    ''' a writer that produced a container its own reader calls damaged would be a bug. The
    ''' name is never echoed into the message.
    ''' </summary>
    Private Shared Function SealedNameBytes(trueName As String) As Byte()
        If trueName Is Nothing Then Throw New ArgumentNullException(NameOf(trueName))
        Dim normalized As String = trueName.Normalize(NormalizationForm.FormC)
        Dim bytes As Byte() = Encoding.UTF8.GetBytes(normalized)
        If bytes.Length < 1 OrElse bytes.Length > FdSecFormat.MaxNameBytes Then
            Throw New ArgumentException("the true name must be 1 to " & FdSecFormat.MaxNameBytes.ToString() & " UTF-8 bytes", NameOf(trueName))
        End If
        If Not FdSecMetadata.IsAcceptableName(normalized) Then
            Throw New ArgumentException("the true name is not a plain file name", NameOf(trueName))
        End If
        Return bytes
    End Function

    ''' <summary>The Windows FILETIME of a UTC instant; a value before 1601 is refused by the
    ''' framework with an ArgumentOutOfRangeException, which is the right answer for it.</summary>
    Private Shared Function WindowsTimeOf(value As Date) As ULong
        Return CULng(value.ToFileTimeUtc())
    End Function

End Class
#End If
