#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO

''' <summary>
''' The layout constants of FDSEC-FORMAT sections 3 to 9. Every one of them is a fact of
''' the format, not a choice of this reader: a container made by any implementation has
''' exactly these widths at exactly these offsets.
''' </summary>
Friend Module FdSecFormat

    Friend Const FormatVersion As Byte = 1
    Friend Const SuiteId As Byte = 1
    Friend Const ConformanceFlags As Integer = &H1

    Friend Const SaltSize As Integer = 16
    Friend Const HeaderSize As Integer = 28          ' salt + the masked parameter block
    Friend Const MaskOffset As Integer = SaltSize    ' the mask starts right after the salt
    Friend Const SlotCount As Integer = 8
    Friend Const SlotSize As Integer = 80
    Friend Const SlotsSize As Integer = SlotCount * SlotSize     ' 640
    Friend Const HeadSize As Integer = HeaderSize + SlotsSize    ' 668
    Friend Const MaskLength As Integer = HeadSize - MaskOffset   ' 652

    Friend Const MetadataPlainSize As Integer = 4096
    Friend Const TagSize As Integer = 16
    Friend Const MetadataSize As Integer = MetadataPlainSize + TagSize   ' 4112

    Friend Const FileKeySize As Integer = 32
    Friend Const NonceSize As Integer = 24
    Friend Const DigestSize As Integer = 32
    Friend Const WrappedSize As Integer = FileKeySize + TagSize          ' 48
    Friend Const SlotPayloadSize As Integer = SlotSize - 1               ' 79

    Friend Const SlotTypeEmpty As Byte = 0
    Friend Const SlotTypeCredentialWrap As Byte = 1

    Friend Const MaxNameBytes As Integer = 3000

    Friend Const MinAlignment As Integer = 512
    Friend Const MaxAlignment As Integer = 1 << 21

    ''' <summary>The largest chunk slot S this READER can hold: the largest multiple of the
    ''' minimum alignment below Array.MaxLength. Not a fact of the format - the format
    ''' allows any u32 - but a limit of the platform, refused as unsupported (G-18).</summary>
    Friend Const MaxReadableChunkSize As Long = &H7FFFFE00L

    Friend Const AdSlot0 As String = "FDSEC1/slot0"
    Friend Const AdMeta As String = "FDSEC1/meta"
    Friend Const AdChunk As String = "FDSEC1/chunk"
    Friend Const NoncePrefix As String = "FDSEC1/nonce/"

    ''' <summary>The extension a container wears. The only thing on disk that says
    ''' anything about it - and only because the user chose to keep it.</summary>
    Public Const Extension As String = ".fd-sec"

End Module

''' <summary>
''' The container head: the clear salt, the masked parameter block and the masked key
''' slots (FDSEC-FORMAT §§3-5), plus the derived header digest that is the associated
''' data of every AEAD call in the file.
''' </summary>
Friend NotInheritable Class FdSecHead

    Friend ReadOnly Property Salt As Byte()
    Friend ReadOnly Property Version As Byte
    Friend ReadOnly Property Suite As Byte
    Friend ReadOnly Property Flags As Integer
    Friend ReadOnly Property ChunkSize As Long
    Friend ReadOnly Property ClusterAlignment As Long
    ''' <summary>Derived, NEVER stored. A stored verifier would let an attacker confirm
    ''' a credential guess against a fixed string, and a container that stores nothing
    ''' verifiable cannot be recognised as a container at all - which is the entire point
    ''' of the head.</summary>
    Friend ReadOnly Property HeaderDigest As Byte()

    Private Sub New(plainHead As Byte())
        Salt = New Byte(FdSecFormat.SaltSize - 1) {}
        Array.Copy(plainHead, 0, Salt, 0, FdSecFormat.SaltSize)
        Version = plainHead(16)
        Suite = plainHead(17)
        Flags = FdSecPrimitives.ReadUInt16Le(plainHead, 18)
        ChunkSize = CLng(FdSecPrimitives.ReadUInt32Le(plainHead, 20))
        ClusterAlignment = CLng(FdSecPrimitives.ReadUInt32Le(plainHead, 24))

        Dim headerBytes(FdSecFormat.HeaderSize - 1) As Byte
        Array.Copy(plainHead, headerBytes, FdSecFormat.HeaderSize)
        HeaderDigest = FdSecPrimitives.Blake2b(256, headerBytes)
    End Sub

    ''' <summary>Where the payload starts: head plus sealed metadata, padded with random
    ''' bytes to a cluster boundary.</summary>
    Friend ReadOnly Property PreLength As Long
        Get
            Dim raw As Long = FdSecFormat.HeadSize + FdSecFormat.MetadataSize
            ' Parenthesised on purpose: VB binds * tighter than \, so "x \ a * a" would
            ' divide by a squared. The same expression is correct as written in the Go
            ' reference, where / and * share a precedence level and associate leftwards.
            Return ((raw + ClusterAlignment - 1) \ ClusterAlignment) * ClusterAlignment
        End Get
    End Property

    ''' <summary>Plaintext capacity of a full chunk slot: S minus the 16 tag bytes.</summary>
    Friend ReadOnly Property ChunkCapacity As Long
        Get
            Return ChunkSize - FdSecFormat.TagSize
        End Get
    End Property

    ''' <summary>An empty original has exactly one chunk, of length zero.</summary>
    Friend Function ChunkCount(size As Long) As Long
        ' Ceiling division written so it cannot overflow: "size + capacity - 1" does for a
        ' size near 2^63-1, and under checked arithmetic that is an OverflowException
        ' instead of an outcome class (FDSEC-FORMAT 1.1 §7).
        Dim k As Long = size \ ChunkCapacity
        If size Mod ChunkCapacity <> 0 Then k += 1
        Return If(k < 1, 1L, k)
    End Function

    Friend Function LastChunkLength(size As Long) As Long
        Return size - (ChunkCount(size) - 1) * ChunkCapacity
    End Function

    ''' <summary>
    ''' Reads the head, unmasks it under the credential and unwraps the file key from key
    ''' slot 0 - the reader algorithm of FDSEC-BEHAVIOUR §5.2, steps 1 to 5.
    '''
    ''' Until slot 0 authenticates NOT ONE BYTE of this head means anything: under a wrong
    ''' credential every unmasked byte is noise, so a wrong credential, a file that never
    ''' was a container and a damaged head all produce the same single outcome. Only after
    ''' the tag holds are the header and the slots interpreted, and only then can an
    ''' unknown version be refused by name and a broken structural rule be called damage.
    ''' </summary>
    Friend Shared Function Open(source As Stream, credentialUtf8 As Byte(), profile As FdSecProfile,
                                cache As FdSecKeyCache,
                                <System.Runtime.InteropServices.Out> ByRef fileKey As Byte()) As FdSecHead
        fileKey = Nothing

        Dim head(FdSecFormat.HeadSize - 1) As Byte
        If Not ReadExactly(source, head, FdSecFormat.HeadSize) Then
            Throw FdSecException.Damaged("file is too short to hold a container head")
        End If

        Dim salt(FdSecFormat.SaltSize - 1) As Byte
        Array.Copy(head, salt, FdSecFormat.SaltSize)

        Dim maskKey As Byte() = Nothing
        Dim kek As Byte() = Nothing
        FdSecKdf.DeriveKeys(credentialUtf8, salt, profile, maskKey, kek, cache)
        Try
            FdSecPrimitives.ChaCha20Xor(maskKey, New Byte(11) {}, head, FdSecFormat.MaskOffset, FdSecFormat.MaskLength)

            Dim parsed As New FdSecHead(head)
            Dim slots As FdSecSlot() = FdSecSlot.ParseAll(head)

            Dim unwrapped As Byte() = FdSecPrimitives.XChaChaOpen(
                kek, slots(0).WrapNonce, Slot0AssociatedData(parsed.HeaderDigest), slots(0).Wrapped)
            If unwrapped Is Nothing Then
                Throw FdSecException.CredentialOrTamper("key slot 0 does not authenticate")
            End If

            ' Authenticated. From here the bytes are ours and the outcome classes part
            ' company again (FDSEC-BEHAVIOUR §7).
            parsed.Validate()
            FdSecSlot.ValidateAll(slots)

            fileKey = unwrapped
            Return parsed
        Finally
            FdSecPrimitives.Wipe(maskKey)
            FdSecPrimitives.Wipe(kek)
            FdSecPrimitives.Wipe(head)
        End Try
    End Function

    ''' <summary>
    ''' The rules of FDSEC-BEHAVIOUR §5.2 step 5, applied ONLY after slot 0 authenticated.
    ''' By the time any of them can fail, the file is known to be a container opened with
    ''' the right credential - so an unknown version, suite or flag bit is a refusal by
    ''' name and a broken structural rule is damage, and neither can be a credential
    ''' problem any more.
    ''' </summary>
    Private Sub Validate()
        If Version <> FdSecFormat.FormatVersion Then
            Throw FdSecException.Unsupported("format version " & Version.ToString())
        End If
        If Suite <> FdSecFormat.SuiteId Then
            Throw FdSecException.Unsupported("suite id " & Suite.ToString())
        End If
        If Flags <> FdSecFormat.ConformanceFlags Then
            Throw FdSecException.Unsupported("flags 0x" & Flags.ToString("x4"))
        End If
        If ClusterAlignment <= 0 OrElse (ClusterAlignment And (ClusterAlignment - 1)) <> 0 OrElse
           ClusterAlignment < FdSecFormat.MinAlignment OrElse ClusterAlignment > FdSecFormat.MaxAlignment Then
            Throw FdSecException.Damaged("cluster alignment " & ClusterAlignment.ToString())
        End If
        If ChunkSize < ClusterAlignment OrElse ChunkSize Mod ClusterAlignment <> 0 Then
            Throw FdSecException.Damaged("chunk size " & ChunkSize.ToString() & " breaks the alignment invariant")
        End If
        ' The format allows any u32 multiple of A; one chunk slot is read into one array,
        ' and a .NET array cannot hold 2 GiB. That is an operation this platform has no
        ' equivalent for - FD-SEC-CONTRACT §7.1 row 6, refused by name as UNSUPPORTED -
        ' and never an OverflowException, which is no outcome class (audit 052 G-18).
        If ChunkSize > FdSecFormat.MaxReadableChunkSize Then
            Throw FdSecException.Unsupported("chunk size " & ChunkSize.ToString() & " is larger than this reader can hold in memory")
        End If
    End Sub

    Friend Shared Function Slot0AssociatedData(headerDigest As Byte()) As Byte()
        Return Concat(FdSecPrimitives.Ascii(FdSecFormat.AdSlot0), headerDigest)
    End Function

    Friend Shared Function MetadataAssociatedData(headerDigest As Byte()) As Byte()
        Return Concat(FdSecPrimitives.Ascii(FdSecFormat.AdMeta), headerDigest)
    End Function

    ''' <summary>
    ''' The associated data of one payload chunk (FDSEC-FORMAT §8). Each binding buys a
    ''' failure that must be impossible rather than unlikely: the index makes reordering
    ''' detectable, the header digest makes splicing between containers detectable, the
    ''' last flag and the length make truncation detectable.
    ''' </summary>
    Friend Shared Function ChunkAssociatedData(headerDigest As Byte(), index As Long, count As Long,
                                               isLast As Boolean, length As Long) As Byte()
        Dim prefix As Byte() = FdSecPrimitives.Ascii(FdSecFormat.AdChunk)
        Dim buffer(prefix.Length + headerDigest.Length + 8 + 8 + 1 + 8 - 1) As Byte
        Dim offset As Integer = 0
        Array.Copy(prefix, 0, buffer, offset, prefix.Length) : offset += prefix.Length
        Array.Copy(headerDigest, 0, buffer, offset, headerDigest.Length) : offset += headerDigest.Length
        Array.Copy(FdSecPrimitives.UInt64Le(CULng(index)), 0, buffer, offset, 8) : offset += 8
        Array.Copy(FdSecPrimitives.UInt64Le(CULng(count)), 0, buffer, offset, 8) : offset += 8
        buffer(offset) = If(isLast, CByte(1), CByte(0)) : offset += 1
        Array.Copy(FdSecPrimitives.UInt64Le(CULng(length)), 0, buffer, offset, 8)
        Return buffer
    End Function

    ''' <summary>
    ''' nonce(ctx) = BLAKE2b-256(key = file_key, msg = "FDSEC1/nonce/" || ctx)[0:24].
    ''' Derived, never stored - a stored nonce would either break the cluster alignment
    ''' or add a header field this format does not have.
    ''' </summary>
    Friend Shared Function MetadataNonce(fileKey As Byte()) As Byte()
        Return Nonce(fileKey, FdSecPrimitives.Ascii("meta"))
    End Function

    Friend Shared Function ChunkNonce(fileKey As Byte(), index As Long) As Byte()
        Return Nonce(fileKey, Concat(FdSecPrimitives.Ascii("chunk:"), FdSecPrimitives.UInt64Le(CULng(index))))
    End Function

    Private Shared Function Nonce(fileKey As Byte(), context As Byte()) As Byte()
        Dim wide As Byte() = FdSecPrimitives.Blake2bKeyed(fileKey, FdSecPrimitives.Ascii(FdSecFormat.NoncePrefix), context)
        Dim derived(FdSecFormat.NonceSize - 1) As Byte
        Array.Copy(wide, derived, FdSecFormat.NonceSize)
        Return derived
    End Function

    Friend Shared Function Concat(ParamArray parts As Byte()()) As Byte()
        Dim total As Integer = 0
        For Each part As Byte() In parts
            total += part.Length
        Next
        Dim buffer(total - 1) As Byte
        Dim offset As Integer = 0
        For Each part As Byte() In parts
            Array.Copy(part, 0, buffer, offset, part.Length)
            offset += part.Length
        Next
        Return buffer
    End Function

    ''' <summary>A short read from a network share is not the end of the stream; keep
    ''' asking until the buffer is full or the source really has no more.</summary>
    Friend Shared Function ReadExactly(source As Stream, buffer As Byte(), count As Integer) As Boolean
        Dim filled As Integer = 0
        While filled < count
            Dim read As Integer = source.Read(buffer, filled, count - filled)
            If read <= 0 Then Return False
            filled += read
        End While
        Return True
    End Function

End Class

''' <summary>
''' One 80-byte key-slot record (FDSEC-FORMAT §5). The whole region is masked with the
''' same keystream as the header fields, so the type bytes and the zero bytes of the
''' empty slots are indistinguishable from the rest of the file.
''' </summary>
Friend NotInheritable Class FdSecSlot

    Friend ReadOnly Property SlotType As Byte
    Private ReadOnly payload As Byte()

    Private Sub New(type As Byte, payloadBytes As Byte())
        SlotType = type
        payload = payloadBytes
    End Sub

    Friend ReadOnly Property WrapNonce As Byte()
        Get
            Dim nonce(FdSecFormat.NonceSize - 1) As Byte
            Array.Copy(payload, 0, nonce, 0, FdSecFormat.NonceSize)
            Return nonce
        End Get
    End Property

    Friend ReadOnly Property Wrapped As Byte()
        Get
            Dim bytes(FdSecFormat.WrappedSize - 1) As Byte
            Array.Copy(payload, FdSecFormat.NonceSize, bytes, 0, FdSecFormat.WrappedSize)
            Return bytes
        End Get
    End Property

    Friend Shared Function ParseAll(plainHead As Byte()) As FdSecSlot()
        Dim slots(FdSecFormat.SlotCount - 1) As FdSecSlot
        For i As Integer = 0 To FdSecFormat.SlotCount - 1
            Dim base As Integer = FdSecFormat.HeaderSize + i * FdSecFormat.SlotSize
            Dim payload(FdSecFormat.SlotPayloadSize - 1) As Byte
            Array.Copy(plainHead, base + 1, payload, 0, FdSecFormat.SlotPayloadSize)
            slots(i) = New FdSecSlot(plainHead(base), payload)
        Next
        Return slots
    End Function

    ''' <summary>
    ''' Runs after slot 0 authenticated, for the same reason the header validation does.
    '''
    ''' The type-1 payload is wrap_nonce (24) || wrapped (48) || SEVEN zero bytes: 1 + 24
    ''' + 48 = 73 of an 80-byte record, so seven remain. FDSEC-FORMAT §5 said six until
    ''' the erratum of 2026-09-22 corrected it; the committed vectors agree with the
    ''' arithmetic, and a reader validates all seven.
    ''' </summary>
    Friend Shared Sub ValidateAll(slots As FdSecSlot())
        If slots(0).SlotType <> FdSecFormat.SlotTypeCredentialWrap Then
            Throw FdSecException.Unsupported("key-slot type " & slots(0).SlotType.ToString() & " in slot 0")
        End If
        For i As Integer = 1 To FdSecFormat.SlotCount - 1
            If slots(i).SlotType <> FdSecFormat.SlotTypeEmpty Then
                Throw FdSecException.Unsupported("key-slot type " & slots(i).SlotType.ToString() & " in a reserved slot")
            End If
        Next

        If Nonzero(slots(0).payload, FdSecFormat.NonceSize + FdSecFormat.WrappedSize, FdSecFormat.SlotPayloadSize) Then
            Throw FdSecException.Damaged("nonzero reserved bytes in key slot 0")
        End If
        For i As Integer = 1 To FdSecFormat.SlotCount - 1
            If Nonzero(slots(i).payload, 0, FdSecFormat.SlotPayloadSize) Then
                Throw FdSecException.Damaged("nonzero payload in an empty key slot")
            End If
        Next
    End Sub

    Private Shared Function Nonzero(buffer As Byte(), start As Integer, [end] As Integer) As Boolean
        For i As Integer = start To [end] - 1
            If buffer(i) <> 0 Then Return True
        Next
        Return False
    End Function

End Class
#End If
