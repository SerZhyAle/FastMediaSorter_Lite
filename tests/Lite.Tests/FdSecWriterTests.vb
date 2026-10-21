#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Xunit

''' <summary>
''' The ephemeral FDSEC suite-1 writer (242_SPECIFICATION_ANIMATED_GRAPHICS_THROUGH_TEMPORARY_ENCRYPTED_VIDEO.md
''' rule 15, step T-2) against the catalog.
'''
''' What decides conformance is <see cref="Writer_reproduces_the_catalog_container_byte_for_byte"/>: the
''' catalog's three writer vectors (empty, one byte, and the three-chunk multi vector) are written again
''' from their published inputs - the original's bytes, the name, the credential, the four timestamps and
''' every random input fed in the contract's draw order - and must come out identical, byte for byte, under
''' the real version-1 profile (the slow Argon2id branch, three runs; a lowered profile would prove nothing
''' about conformance and is not used here). Everything else is behaviour the writer owes its callers: a round
''' trip through the reader, refusal of a damaged container, the 64-byte threshold, noise from byte 0, a
''' strictly sequential destination, and a container the owner's own filedo.exe opens.
''' </summary>
Public Class FdSecWriterTests

    ' The credential of the catalog's writer vectors: 28 UTF-8 bytes, so the slow Argon2id branch.
    Private Const SlowCredential As String = "correct horse battery staple"

    ' 92 UTF-8 bytes: the fast branch, which costs microseconds, so most tests use it.
    Private Const LongCredential As String =
        "a deliberately long passphrase of more than sixty-four UTF-8 bytes, carrying its own entropy"

    Private Shared ReadOnly VectorDirectory As String =
        Path.Combine(AppContext.BaseDirectory, "TestData", "fdsec-vectors")

    Private Shared ReadOnly Vectors As JsonElement = LoadVectors()

    Private Shared Function LoadVectors() As JsonElement
        Using document As JsonDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(VectorDirectory, "vectors.json")))
            Return document.RootElement.Clone()
        End Using
    End Function

    Private Shared Function VectorOf(name As String) As JsonElement
        Return Vectors.GetProperty("containers").GetProperty(name)
    End Function

    Private Shared Function HexOf(element As JsonElement, ParamArray path As String()) As Byte()
        Dim current As JsonElement = element
        For Each step_ As String In path
            current = current.GetProperty(step_)
        Next
        Dim text As String = current.GetString()
        If String.IsNullOrEmpty(text) Then Return Array.Empty(Of Byte)()
        Dim bytes(text.Length \ 2 - 1) As Byte
        For i As Integer = 0 To bytes.Length - 1
            bytes(i) = Convert.ToByte(text.Substring(i * 2, 2), 16)
        Next
        Return bytes
    End Function

    ''' <summary>The catalog's container, as the catalog published it: "empty" and "onebyte" ship as files,
    ''' "multi" only as its layout fields, assembled here in file order.</summary>
    Private Shared Function ReferenceBytes(name As String) As Byte()
        Dim vector As JsonElement = VectorOf(name)
        Dim fileProperty As JsonElement
        If vector.TryGetProperty("file", fileProperty) Then
            Return File.ReadAllBytes(Path.Combine(VectorDirectory, fileProperty.GetString()))
        End If
        Return {"header_on_disk", "slots_on_disk", "sealed_metadata", "align_pad", "payload", "tail_pad"}.
            SelectMany(Function(field) HexOf(vector, "layout", field)).ToArray()
    End Function

    ''' <summary>A published timestamp, read as the UTC instant it names.</summary>
    Private Shared Function Utc(vector As JsonElement, field As String) As Date
        Return Date.Parse(vector.GetProperty(field).GetString(), CultureInfo.InvariantCulture,
                          DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal)
    End Function

    Private Shared Function PatternBytes(count As Integer) As Byte()
        Dim bytes(count - 1) As Byte
        For i As Integer = 0 To count - 1
            bytes(i) = CByte((i * 31 + 7) And &HFF)
        Next
        Return bytes
    End Function

    Private Shared Function ToHex(bytes As Byte()) As String
        Return String.Concat(bytes.Select(Function(b) b.ToString("x2")))
    End Function

    ''' <summary>Fails naming the FIRST differing offset - a 12 KB array diff is no help to anyone.</summary>
    Private Shared Sub AssertSameBytes(expected As Byte(), actual As Byte())
        Assert.Equal(expected.Length, actual.Length)
        For i As Integer = 0 To expected.Length - 1
            If expected(i) <> actual(i) Then
                Assert.Fail("first difference at byte " & i.ToString() & " of " & expected.Length.ToString() &
                            ": expected " & expected(i).ToString("x2") & ", wrote " & actual(i).ToString("x2"))
            End If
        Next
    End Sub

    ''' <summary>The writer through its public form, into memory.</summary>
    Private Shared Function WriteToArray(plaintext As Byte(), name As String, credential As String) As Byte()
        Using output As New MemoryStream()
            Dim written As Long = FdSecWriter.Write(plaintext, name, credential, output)
            Assert.Equal(written, output.Length)
            Return output.ToArray()
        End Using
    End Function

    Private Shared Function OpenPacked(packed As Byte(), credential As String) As FdSecContainer
        Return FdSecContainer.Open(New MemoryStream(packed, writable:=False), credential, ownsSource:=True)
    End Function

    Private Shared Function OutcomeOf(packed As Byte(), credential As String) As FdSecOutcome
        Try
            Using container As FdSecContainer = OpenPacked(packed, credential)
                container.VerifyAll()
                Return FdSecOutcome.Ok
            End Using
        Catch ex As FdSecException
            Return ex.Outcome
        End Try
    End Function

    Private Shared Function ReadAll(container As FdSecContainer) As Byte()
        Using content As Stream = container.OpenContentStream()
            Using recovered As New MemoryStream()
                content.CopyTo(recovered)
                Return recovered.ToArray()
            End Using
        End Using
    End Function

    ' --------------------------------------------------------------- conformance ----

    ''' <summary>
    ''' The catalog's randomness, handed out in the contract's draw order, and strict about it: a draw of
    ''' the wrong length, an extra draw or a missing one fails the test by name, so the order and the sizes
    ''' of the writer's CSPRNG calls are pinned, not just its output.
    ''' </summary>
    Private NotInheritable Class ScriptedRandom
        Private ReadOnly queue As New Queue(Of KeyValuePair(Of String, Byte()))()

        Friend Sub New(vector As JsonElement)
            For Each name As String In {"salt", "file_key", "wrap_nonce", "meta_pad", "align_pad", "tail_pad"}
                queue.Enqueue(New KeyValuePair(Of String, Byte())(name, HexOf(vector, "random_inputs", name)))
            Next
        End Sub

        Friend Sub Fill(buffer As Byte())
            Assert.True(queue.Count > 0, "the writer asked for more randomness than the contract draws")
            Dim item As KeyValuePair(Of String, Byte()) = queue.Dequeue()
            Assert.True(item.Value.Length = buffer.Length,
                        "draw " & item.Key & ": the contract's order has " & item.Value.Length.ToString() &
                        " bytes here, the writer asked for " & buffer.Length.ToString())
            Array.Copy(item.Value, buffer, buffer.Length)
        End Sub

        Friend ReadOnly Property Remaining As Integer
            Get
                Return queue.Count
            End Get
        End Property
    End Class

    ''' <summary>
    ''' FDSEC-FORMAT section 16: a suite-1 implementation reproduces the published containers byte for byte.
    ''' The credential is below the threshold, so every row pays the real Argon2id derivation of the
    ''' version-1 profile once - the cost the reader's conformance tests accept too.
    ''' </summary>
    <Theory>
    <InlineData("empty")>
    <InlineData("onebyte")>
    <InlineData("multi")>
    Public Sub Writer_reproduces_the_catalog_container_byte_for_byte(name As String)
        Dim vector As JsonElement = VectorOf(name)
        Dim plaintext As Byte() = HexOf(vector, "content")
        Dim expected As Byte() = ReferenceBytes(name)

        ' The chunk size and alignment are header fields of the vector (the multi vector publishes a small
        ' pair so it stays small), read from its own unmasked header.
        Dim header As Byte() = HexOf(vector, "layout", "header_unmasked")
        Dim chunkSize As Integer = CInt(FdSecPrimitives.ReadUInt32Le(header, 20))
        Dim alignment As Integer = CInt(FdSecPrimitives.ReadUInt32Le(header, 24))

        Dim random As New ScriptedRandom(vector)
        Using output As New MemoryStream()
            Dim written As Long = FdSecWriter.WriteWithLayout(
                plaintext, vector.GetProperty("name").GetString(),
                FdSecKdf.NormalizeCredential(vector.GetProperty("credential").GetString()),
                output, AddressOf random.Fill, chunkSize, alignment,
                Utc(vector, "encoded_at"), Utc(vector, "created_at"), Utc(vector, "accessed_at"), Utc(vector, "modified_at"))

            Assert.Equal(0, random.Remaining)
            Assert.Equal(vector.GetProperty("layout").GetProperty("total_len").GetInt64(), written)
            AssertSameBytes(expected, output.ToArray())
        End Using
    End Sub

    ''' <summary>The credential of the vectors is the slow branch, and the test pins it - if the catalog ever
    ''' re-published them under a long credential, this file would silently stop covering Argon2id.</summary>
    <Fact>
    Public Sub The_vectors_exercise_the_slow_branch()
        Assert.True(Encoding.UTF8.GetByteCount(SlowCredential) < FdSecProfile.V1.Threshold)
        For Each name As String In {"empty", "onebyte", "multi"}
            Assert.Equal(SlowCredential, VectorOf(name).GetProperty("credential").GetString())
        Next
    End Sub

    ' ------------------------------------------------------------------ round trip ----

    <Fact>
    Public Sub A_small_payload_round_trips_through_the_reader()
        Dim plaintext As Byte() = PatternBytes(1000)
        Dim before As Date = Date.UtcNow.AddSeconds(-5)
        Dim packed As Byte() = WriteToArray(plaintext, "animation.mp4", LongCredential)
        Dim after As Date = Date.UtcNow.AddSeconds(5)

        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal("animation.mp4", container.Metadata.Name)
            Assert.Equal(1000L, container.Metadata.Size)
            Assert.Equal(1L, container.ChunkCount)
            Assert.Equal(FdSecPrimitives.Blake2b(256, plaintext), container.Metadata.Digest)

            ' No original file exists, so the file times are the moment of encoding: all four the same.
            Dim encoded As Date = container.Metadata.EncodedAt.Value
            Assert.InRange(encoded, before, after)
            Assert.Equal(encoded, container.Metadata.CreatedAt.Value)
            Assert.Equal(encoded, container.Metadata.AccessedAt.Value)
            Assert.Equal(encoded, container.Metadata.ModifiedAt.Value)

            Assert.Equal(plaintext, ReadAll(container))
            container.VerifyAll()
        End Using
    End Sub

    <Fact>
    Public Sub An_empty_payload_is_one_zero_length_chunk()
        Dim packed As Byte() = WriteToArray(Array.Empty(Of Byte)(), "empty.bin", LongCredential)
        Assert.Equal(12288, packed.Length)
        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal(0L, container.ContentLength)
            Assert.Equal(1L, container.ChunkCount)
            container.VerifyAll()
        End Using
    End Sub

    ''' <summary>
    ''' A 3 MiB original under the DEFAULT layout (1 MiB chunk slots, 4 KiB clusters): four chunks, the last
    ''' one short, and a container length that is a whole number of clusters.
    ''' </summary>
    <Fact>
    Public Sub A_multi_chunk_payload_round_trips_under_the_default_layout()
        Dim size As Integer = 3 * 1024 * 1024 + 123
        Dim plaintext As Byte() = PatternBytes(size)
        Dim packed As Byte() = WriteToArray(plaintext, "big.mp4", LongCredential)

        Dim capacity As Long = FdSecWriter.DefaultChunkSize - FdSecFormat.TagSize
        Dim chunks As Long = (size + capacity - 1) \ capacity
        Assert.Equal(4L, chunks)
        Dim minimum As Long = 8192 + (chunks - 1) * FdSecWriter.DefaultChunkSize + (size - (chunks - 1) * capacity) + FdSecFormat.TagSize
        Assert.True(packed.Length >= minimum AndAlso packed.Length < minimum + FdSecWriter.DefaultAlignment)
        Assert.Equal(0, packed.Length Mod FdSecWriter.DefaultAlignment)

        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal(chunks, container.ChunkCount)
            Assert.Equal(CLng(size), container.ContentLength)
            Assert.Equal(plaintext, ReadAll(container))
            container.VerifyAll()
        End Using

        ' One flipped bit inside the LAST chunk (it starts at 8192 + 3 * S) is found by the full
        ' verification, although the streaming reader would not touch that chunk until asked.
        Dim insideLastChunk As Integer = 8192 + 3 * FdSecWriter.DefaultChunkSize + 5
        packed(insideLastChunk) = CByte(packed(insideLastChunk) Xor &H1)
        Assert.Equal(FdSecOutcome.CredentialOrTamper, OutcomeOf(packed, LongCredential))
    End Sub

    ''' <summary>The plaintext is read where it lies: a slice of a larger array (a MemoryStream buffer is
    ''' exactly that) is sealed without being copied, and the caller's array is never written to.</summary>
    <Fact>
    Public Sub A_slice_of_a_larger_array_is_sealed_in_place_and_left_untouched()
        Dim backing As Byte() = PatternBytes(5000)
        Dim snapshot As Byte() = CType(backing.Clone(), Byte())
        Dim slice As New ReadOnlyMemory(Of Byte)(backing, 1000, 3200)

        Using output As New MemoryStream()
            FdSecWriter.Write(slice, "slice.mp4", LongCredential, output)
            Using container As FdSecContainer = OpenPacked(output.ToArray(), LongCredential)
                Assert.Equal(3200L, container.ContentLength)
                Assert.Equal(backing.Skip(1000).Take(3200).ToArray(), ReadAll(container))
                Assert.Equal(FdSecPrimitives.Blake2b(256, backing.Skip(1000).Take(3200).ToArray()), container.Metadata.Digest)
                container.VerifyAll()
            End Using
        End Using
        Assert.Equal(snapshot, backing)
    End Sub

    ''' <summary>The second form of the credential, for a per-run key that must never be an immutable
    ''' String: same container, the held bytes untouched, and a forgotten credential refused before any
    ''' byte is written.</summary>
    <Fact>
    Public Sub A_held_credential_writes_the_same_container_and_a_forgotten_one_is_refused()
        Dim plaintext As Byte() = PatternBytes(300)
        Dim held As FdSecCredential = FdSecCredential.FromText(LongCredential)
        Using output As New MemoryStream()
            FdSecWriter.Write(plaintext, "held.mp4", held, output)
            Using container As FdSecContainer = OpenPacked(output.ToArray(), LongCredential)
                Assert.Equal(plaintext, ReadAll(container))
            End Using
        End Using
        Assert.False(held.IsDisposed)
        Assert.True(held.SameAs(FdSecCredential.FromText(LongCredential)))

        held.Dispose()
        Using output As New MemoryStream()
            Assert.Throws(Of ObjectDisposedException)(Sub() FdSecWriter.Write(plaintext, "held.mp4", held, output))
            Assert.Equal(0L, output.Length)
        End Using
    End Sub

    ''' <summary>The credential is normalised to NFC and never trimmed (FDSEC-FORMAT section 6): a container
    ''' packed under the decomposed spelling opens under the composed one, and a trailing space is part of it.</summary>
    <Fact>
    Public Sub The_credential_is_normalised_to_nfc_and_never_trimmed()
        Dim decomposed As String = String.Concat(Enumerable.Repeat("e" & ChrW(&H301), 40))
        Dim composed As String = String.Concat(Enumerable.Repeat(ChrW(&HE9), 40))
        Assert.NotEqual(decomposed, composed)

        Dim packed As Byte() = WriteToArray(PatternBytes(64), "a.bin", decomposed)
        Assert.Equal(FdSecOutcome.Ok, OutcomeOf(packed, composed))
        Assert.Equal(FdSecOutcome.CredentialOrTamper, OutcomeOf(packed, composed & " "))
    End Sub

    ' ---------------------------------------------------------------- refusals ----

    ''' <summary>One flipped bit anywhere that is AUTHENTICATED is refused, and never as a wrong password
    ''' in disguise: the salt and the masked head change the derivation or the unwrap, the sealed metadata and
    ''' a chunk fail their own tags.</summary>
    <Theory>
    <InlineData(3)>
    <InlineData(40)>
    <InlineData(100)>
    <InlineData(700)>
    <InlineData(4000)>
    <InlineData(8192 + 5)>
    <InlineData(8192 + 2000)>
    Public Sub A_flipped_byte_is_refused(offset As Integer)
        Dim packed As Byte() = WriteToArray(PatternBytes(5000), "a.bin", LongCredential)
        packed(offset) = CByte(packed(offset) Xor &H1)
        Assert.Equal(FdSecOutcome.CredentialOrTamper, OutcomeOf(packed, LongCredential))
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(4096)>
    <InlineData(12288)>
    <InlineData(13312)>
    <InlineData(16384 - 512)>
    Public Sub A_truncated_container_is_refused_as_damaged(newLength As Integer)
        Dim packed As Byte() = WriteToArray(PatternBytes(5000), "a.bin", LongCredential)
        Assert.Equal(16384, packed.Length)
        Assert.Equal(FdSecOutcome.Damaged, OutcomeOf(packed.Take(newLength).ToArray(), LongCredential))
    End Sub

    <Fact>
    Public Sub A_wrong_credential_is_refused_and_never_called_damage()
        Dim packed As Byte() = WriteToArray(PatternBytes(300), "a.bin", LongCredential)
        Assert.Equal(FdSecOutcome.CredentialOrTamper, OutcomeOf(packed, LongCredential & "!"))
    End Sub

    ' ------------------------------------------------------------ the 64-byte rule ----

    ''' <summary>
    ''' Whether the keys of the FAST branch (<c>root = BLAKE2b-512(salt || credential)[0:32]</c>) unwrap key
    ''' slot 0 of this container. Derived here from the primitives, independently of <c>FdSecKdf</c>, so the
    ''' answer is a witness of which branch the WRITER took: a container written through Argon2id does not
    ''' unwrap under these keys, and one written through the fast branch does - without a single Argon2
    ''' run in this check and without timing anything.
    ''' </summary>
    Private Shared Function UnwrapsUnderFastKeys(packed As Byte(), credential As String) As Boolean
        Dim credentialBytes As Byte() = Encoding.UTF8.GetBytes(credential.Normalize(NormalizationForm.FormC))
        Dim salt As Byte() = packed.Take(FdSecFormat.SaltSize).ToArray()
        Dim wide As Byte() = FdSecPrimitives.Blake2b(512, salt, credentialBytes)
        Dim root As Byte() = wide.Take(32).ToArray()
        Dim maskKey As Byte() = FdSecPrimitives.Blake2bKeyed(root, FdSecPrimitives.Ascii("FDSEC1/mask"))
        Dim kek As Byte() = FdSecPrimitives.Blake2bKeyed(root, FdSecPrimitives.Ascii("FDSEC1/kek"))

        Dim head As Byte() = packed.Take(FdSecFormat.HeadSize).ToArray()
        FdSecPrimitives.ChaCha20Xor(maskKey, New Byte(11) {}, head, FdSecFormat.MaskOffset, FdSecFormat.MaskLength)
        Dim headerDigest As Byte() = FdSecPrimitives.Blake2b(256, head.Take(FdSecFormat.HeaderSize).ToArray())
        Dim slot As Byte() = head.Skip(FdSecFormat.HeaderSize).Take(FdSecFormat.SlotSize).ToArray()
        Dim opened As Byte() = FdSecPrimitives.XChaChaOpen(
            kek, slot.Skip(1).Take(FdSecFormat.NonceSize).ToArray(), FdSecHead.Slot0AssociatedData(headerDigest),
            slot.Skip(1 + FdSecFormat.NonceSize).Take(FdSecFormat.WrappedSize).ToArray())
        Return opened IsNot Nothing AndAlso opened.Length = FdSecFormat.FileKeySize
    End Function

    ''' <summary>The threshold is 64 UTF-8 BYTES, not characters: 32 two-byte letters take the fast branch.</summary>
    <Theory>
    <InlineData(64, False)>
    <InlineData(70, False)>
    <InlineData(32, True)>
    Public Sub A_credential_of_64_or_more_utf8_bytes_takes_the_fast_branch(length As Integer, twoByteLetters As Boolean)
        Dim credential As String = If(twoByteLetters,
                                      String.Concat(Enumerable.Repeat(ChrW(&HE9), length)),
                                      New String("k"c, length))
        Assert.True(Encoding.UTF8.GetByteCount(credential) >= FdSecProfile.V1.Threshold)

        Dim packed As Byte() = WriteToArray(PatternBytes(100), "a.bin", credential)
        Assert.True(UnwrapsUnderFastKeys(packed, credential), "a credential at the threshold must not pay for Argon2id")

        ' And the reader, which recomputes the branch from the length, opens it under a profile whose
        ' Argon2id could not even run: the branch never touches the memory or the passes.
        Dim unrunnable As New FdSecProfile(threshold:=64, memoryKib:=0, iterations:=0, lanes:=0)
        Using container As FdSecContainer = FdSecContainer.Open(New MemoryStream(packed, writable:=False), credential,
                                                                ownsSource:=True, profile:=unrunnable)
            container.VerifyAll()
        End Using
    End Sub

    ''' <summary>One byte short of the threshold is the slow branch (one real Argon2id run in the writer):
    ''' the fast keys do NOT unwrap it, so the check above is a real discriminator and not a tautology.</summary>
    <Fact>
    Public Sub A_credential_of_63_bytes_takes_the_slow_branch()
        Dim credential As String = New String("k"c, FdSecProfile.V1.Threshold - 1)
        Dim packed As Byte() = WriteToArray(PatternBytes(100), "a.bin", credential)
        Assert.False(UnwrapsUnderFastKeys(packed, credential))
    End Sub

    ' --------------------------------------------------------------------- shape ----

    Private Shared Function ChiSquare(bytes As Byte()) As Double
        Dim counts(255) As Long
        For Each b As Byte In bytes
            counts(b) += 1
        Next
        Dim expected As Double = bytes.Length / 256.0
        Dim sum As Double = 0
        For i As Integer = 0 To 255
            Dim delta As Double = counts(i) - expected
            sum += delta * delta / expected
        Next
        Return sum
    End Function

    Private Shared Function LongestRun(bytes As Byte()) As Integer
        Dim best As Integer = 0
        Dim run As Integer = 0
        For i As Integer = 0 To bytes.Length - 1
            run = If(i > 0 AndAlso bytes(i) = bytes(i - 1), run + 1, 1)
            If run > best Then best = run
        Next
        Return best
    End Function

    ''' <summary>
    ''' FDSEC-FORMAT section 14: nothing in the clear but noise, from byte 0. The worst case for a writer
    ''' is an all-zero original; the container must still show no zero run, a flat byte histogram, and no
    ''' byte that two independent writes of the same input agree on beyond chance. The length is a whole
    ''' number of clusters - the one thing the format admits.
    ''' </summary>
    <Fact>
    Public Sub A_container_is_noise_from_the_first_byte()
        Dim zeros(200 * 1024 - 1) As Byte
        Dim first As Byte() = WriteToArray(zeros, "zeros.mp4", LongCredential)
        Dim second As Byte() = WriteToArray(zeros, "zeros.mp4", LongCredential)

        Assert.Equal(0, first.Length Mod 512)
        Assert.Equal(0, first.Length Mod FdSecWriter.DefaultAlignment)
        Assert.Equal(first.Length, second.Length)

        For Each packed As Byte() In {first, second}
            Assert.True(packed.Take(4096).Distinct().Count() >= 200, "the first cluster is not uniform noise")
            Assert.True(LongestRun(packed) < 16, "a run of identical bytes: something was written in the clear")
            Assert.True(ChiSquare(packed) < 400.0, "the byte histogram is not flat (chi-square " & ChiSquare(packed).ToString("F0") & ")")
        Next

        ' Fresh salt and file key per container: two writes agree only by chance (1 in 256), nowhere more.
        Dim equalBytes As Integer = Enumerable.Range(0, first.Length).Count(Function(i) first(i) = second(i))
        Assert.True(equalBytes < first.Length \ 50, "two writes share " & equalBytes.ToString() & " of " & first.Length.ToString() & " bytes")
    End Sub

    ''' <summary>Same randomness and same layout, same bytes: nothing else (a clock, a counter) leaks into the
    ''' output, which is what makes the vectors a test at all.</summary>
    <Fact>
    Public Sub The_output_depends_only_on_the_inputs_and_the_randomness()
        Dim stamp As New Date(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc)
        Dim plaintext As Byte() = PatternBytes(3000)

        Dim first As Byte() = WriteWithCounterRandom(plaintext, stamp)
        Dim second As Byte() = WriteWithCounterRandom(plaintext, stamp)
        AssertSameBytes(first, second)
    End Sub

    ''' <summary>A deterministic stand-in for the CSPRNG: the same call sequence gives the same bytes.</summary>
    Private NotInheritable Class CounterRandom
        Private counter As Integer

        Friend Sub Fill(buffer As Byte())
            For i As Integer = 0 To buffer.Length - 1
                counter += 1
                buffer(i) = CByte((counter * 131 + 17) Mod 256)
            Next
        End Sub
    End Class

    Private Shared Function WriteWithCounterRandom(plaintext As Byte(), stamp As Date) As Byte()
        Dim random As New CounterRandom()
        Using output As New MemoryStream()
            FdSecWriter.WriteWithLayout(plaintext, "a.bin", Encoding.UTF8.GetBytes(LongCredential), output,
                                        AddressOf random.Fill, FdSecWriter.DefaultChunkSize, FdSecWriter.DefaultAlignment,
                                        stamp, stamp, stamp, stamp)
            Return output.ToArray()
        End Using
    End Function

    ' --------------------------------------------------------------------- names ----

    <Fact>
    Public Sub The_true_name_is_sealed_in_nfc()
        Dim decomposed As String = "cafe" & ChrW(&H301) & ".mp4"
        Dim packed As Byte() = WriteToArray(PatternBytes(10), decomposed, LongCredential)
        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal("caf" & ChrW(&HE9) & ".mp4", container.Metadata.Name)
        End Using
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData(".")>
    <InlineData("..")>
    <InlineData("a/b.mp4")>
    <InlineData("a\b.mp4")>
    <InlineData("c:evil.mp4")>
    <InlineData("what?.mp4")>
    <InlineData("star*.mp4")>
    <InlineData("nul.mp4")>
    <InlineData("trailing.")>
    <InlineData("trailing ")>
    Public Sub A_name_the_reader_would_refuse_is_refused_before_a_byte_is_written(name As String)
        Using output As New MemoryStream()
            Assert.Throws(Of ArgumentException)(Sub() FdSecWriter.Write(PatternBytes(10), name, LongCredential, output))
            Assert.Equal(0L, output.Length)
        End Using
    End Sub

    ''' <summary>FDSEC-FORMAT section 7: a name over 3000 UTF-8 bytes is refused, never truncated; 3000 itself
    ''' is the largest that fits and reads back whole.</summary>
    <Fact>
    Public Sub The_name_limit_is_3000_utf8_bytes()
        Using output As New MemoryStream()
            Assert.Throws(Of ArgumentException)(Sub() FdSecWriter.Write(PatternBytes(10), New String("n"c, 3001), LongCredential, output))
            Assert.Equal(0L, output.Length)
            Assert.Throws(Of ArgumentNullException)(Sub() FdSecWriter.Write(PatternBytes(10), Nothing, LongCredential, output))
        End Using

        Dim longest As String = New String("n"c, 3000)
        Dim packed As Byte() = WriteToArray(PatternBytes(10), longest, LongCredential)
        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal(longest, container.Metadata.Name)
        End Using
    End Sub

    ' ------------------------------------------------------------ the destination ----

    ''' <summary>A destination that can only be written to, front to back.</summary>
    Private NotInheritable Class ForwardOnlyStream
        Inherits Stream

        Private ReadOnly inner As New MemoryStream()
        Friend FlushCount As Integer

        Public Overrides ReadOnly Property CanRead As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanSeek As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property CanWrite As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides ReadOnly Property Length As Long
            Get
                Throw New NotSupportedException()
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Throw New NotSupportedException()
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Sub Flush()
            FlushCount += 1
        End Sub

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            inner.Write(buffer, offset, count)
        End Sub

        Friend Function Bytes() As Byte()
            Return inner.ToArray()
        End Function
    End Class

    ''' <summary>The writer is strictly sequential: it needs Write and Flush and nothing else - no Seek, no
    ''' Position, no Length, no Read - so a pipe or a forward-only wrapper works, and the caller reads the
    ''' container back through a second handle afterwards.</summary>
    <Fact>
    Public Sub The_destination_needs_only_sequential_write_and_flush()
        Dim plaintext As Byte() = PatternBytes(2 * 1024 * 1024 + 5)
        Dim forward As New ForwardOnlyStream()
        Dim written As Long = FdSecWriter.Write(plaintext, "pipe.mp4", LongCredential, forward)

        Assert.True(forward.FlushCount >= 1, "the OS must see every byte before a second handle reads the file")
        Dim packed As Byte() = forward.Bytes()
        Assert.Equal(written, CLng(packed.Length))
        Using container As FdSecContainer = OpenPacked(packed, LongCredential)
            Assert.Equal(plaintext, ReadAll(container))
            container.VerifyAll()
        End Using
    End Sub

    ''' <summary>
    ''' The shape the temporary store will use (242 rule 18): a file whose writer handle is opened
    ''' DeleteOnClose with delete sharing and is KEPT open. A second handle - the reader - opens it and
    ''' verifies it while the writer handle is held, and the operating system removes the file when the last
    ''' handle goes, with no code of ours deleting anything.
    ''' </summary>
    <Fact>
    Public Sub A_delete_on_close_file_is_readable_while_held_and_gone_when_released()
        Dim target As String = Path.Combine(Path.GetTempPath(), "fdsecw-" & Guid.NewGuid().ToString("N"))
        Dim plaintext As Byte() = PatternBytes(1500000)
        Try
            Using writer As New FileStream(target, FileMode.CreateNew, FileAccess.Write,
                                           FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.DeleteOnClose)
                Dim written As Long = FdSecWriter.Write(plaintext, "animation.mp4", LongCredential, writer)

                Using container As FdSecContainer = FdSecContainer.OpenFile(target, LongCredential)
                    Assert.True(written > plaintext.Length, "the container is the plaintext plus its head, tags and padding")
                    Assert.Equal(CLng(plaintext.Length), container.ContentLength)
                    container.VerifyAll()
                    Assert.Equal(plaintext, ReadAll(container))
                End Using
                Assert.True(File.Exists(target))
            End Using
            Assert.False(File.Exists(target), "the operating system must delete the container when its last handle closes")
        Finally
            If File.Exists(target) Then File.Delete(target)
        End Try
    End Sub

    ' ---------------------------------------------------------------- misuse ----

    <Fact>
    Public Sub A_destination_that_cannot_be_written_is_refused()
        Using readOnlyStream As New MemoryStream(New Byte(9) {}, writable:=False)
            Assert.Throws(Of ArgumentException)(Sub() FdSecWriter.Write(PatternBytes(10), "a.bin", LongCredential, readOnlyStream))
        End Using
        Assert.Throws(Of ArgumentNullException)(Sub() FdSecWriter.Write(PatternBytes(10), "a.bin", LongCredential, Nothing))
    End Sub

    <Theory>
    <InlineData(300, 1024)>
    <InlineData(256, 1024)>
    <InlineData(512, 1000)>
    <InlineData(512, 256)>
    <InlineData(4194304, 4194304)>
    Public Sub An_illegal_layout_is_refused(alignment As Integer, chunkSize As Integer)
        Dim stamp As Date = Date.UtcNow
        Using output As New MemoryStream()
            Assert.Throws(Of ArgumentOutOfRangeException)(
                Sub() FdSecWriter.WriteWithLayout(PatternBytes(10), "a.bin", Encoding.UTF8.GetBytes(LongCredential), output, Nothing,
                                                  chunkSize, alignment, stamp, stamp, stamp, stamp))
            Assert.Equal(0L, output.Length)
        End Using
    End Sub

    <Fact>
    Public Sub A_cancelled_write_stops_between_chunks_and_a_cancelled_token_writes_nothing()
        Dim plaintext As Byte() = PatternBytes(3 * 1024 * 1024)

        Using output As New MemoryStream()
            Using source As New CancellationTokenSource()
                source.Cancel()
                Assert.Throws(Of OperationCanceledException)(
                    Sub() FdSecWriter.Write(plaintext, "a.mp4", LongCredential, output, source.Token))
            End Using
            Assert.Equal(0L, output.Length)
        End Using

        ' Cancelled while the alignment pad is drawn: the head is already out, no chunk is.
        Using output As New MemoryStream()
            Using source As New CancellationTokenSource()
                Dim draws As Integer = 0
                Dim cancelling As Action(Of Byte()) =
                    Sub(buffer As Byte())
                        System.Security.Cryptography.RandomNumberGenerator.Fill(buffer)
                        draws += 1
                        If draws = 5 Then source.Cancel() ' salt, key, nonce, metadata pad, THEN the alignment pad
                    End Sub
                Assert.Throws(Of OperationCanceledException)(
                    Sub() FdSecWriter.WriteSeeded(plaintext, "a.mp4", LongCredential, output, cancelling, source.Token))
            End Using
            Assert.True(output.Length < 8192 + 1024 * 1024, "no chunk may follow a cancellation")
        End Using
    End Sub

    ' ------------------------------------------------- what the writer is NOT ----

    ''' <summary>
    ''' Q242-1 narrows the standing "no writer" decision to exactly one thing, and the narrowing is
    ''' structural: the writer has no notion of an original file. No parameter names a path, a file or a
    ''' source; no member deletes, wipes, moves or disposes; the only instance-free surface is Write.
    ''' </summary>
    <Fact>
    Public Sub The_writer_has_no_notion_of_an_original_file()
        Dim flags As BindingFlags = BindingFlags.Public Or BindingFlags.NonPublic Or BindingFlags.Static Or BindingFlags.Instance Or BindingFlags.DeclaredOnly
        Dim forbiddenName As String() = {"path", "file", "source", "original", "disposition", "delete", "wipe", "remove", "move", "rename", "dispose", "temp"}

        Assert.True(GetType(FdSecWriter).IsSealed)
        Assert.Empty(GetType(FdSecWriter).GetFields(flags).Where(Function(f) Not f.IsLiteral))
        Assert.Empty(GetType(FdSecWriter).GetProperties(flags))
        Assert.Empty(GetType(FdSecWriter).GetMethods(flags).Where(Function(m) Not m.IsStatic))

        For Each method As MethodInfo In GetType(FdSecWriter).GetMethods(flags)
            For Each word As String In forbiddenName
                Assert.DoesNotContain(word, method.Name, StringComparison.OrdinalIgnoreCase)
            Next
            For Each parameter As ParameterInfo In method.GetParameters()
                For Each word As String In forbiddenName
                    Assert.DoesNotContain(word, parameter.Name, StringComparison.OrdinalIgnoreCase)
                Next
                Assert.NotEqual(GetType(FileInfo), parameter.ParameterType)
                Assert.NotEqual(GetType(FileStream), parameter.ParameterType)
            Next
        Next
    End Sub

    ''' <summary>src/Secure has no product dependencies and the writer touches no file system of its own:
    ''' read the source and hold its imports to a short list and its code to no file API.</summary>
    <Fact>
    Public Sub The_writer_source_has_no_product_dependencies_and_no_file_api()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim text As String = Nothing
        While dir IsNot Nothing AndAlso text Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Secure", "FdSecWriter.vb")
            If File.Exists(candidate) Then text = File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Assert.False(text Is Nothing, "src\Secure\FdSecWriter.vb not found above " & AppContext.BaseDirectory)

        Dim allowed As String() = {"System.IO", "System.Runtime.InteropServices", "System.Security.Cryptography", "System.Text", "System.Threading"}
        For Each line As String In text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            If line.StartsWith("Imports ", StringComparison.Ordinal) Then
                Assert.Contains(line.Substring("Imports ".Length).Trim(), allowed)
            End If
        Next

        Dim code As String = String.Join(vbLf, text.Split({vbCrLf, vbLf}, StringSplitOptions.None).
            Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal)))
        For Each forbidden As String In {"FileStream", "File.", "Directory.", "Path.", "Registry", "Environment.", "Process.", "Log"}
            Assert.DoesNotContain(forbidden, code)
        Next
    End Sub

    ' ----------------------------------------------------------- interoperability ----

    ''' <summary>The owner's reference implementation, when this checkout carries it.</summary>
    Friend NotInheritable Class FiledoExe
        Private Sub New()
        End Sub

        Friend Shared Function Find() As String
            Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
            While dir IsNot Nothing
                Dim candidate As String = Path.Combine(dir.FullName, "payload", "filedo", "filedo.exe")
                If File.Exists(candidate) Then Return candidate
                dir = dir.Parent
            End While
            Return Nothing
        End Function
    End Class

    ''' <summary>Skipped, loudly and by name, while payload\filedo\filedo.exe is not in the checkout - never
    ''' a failure and never a pass.</summary>
    Public NotInheritable Class TheoryWhenFiledoAttribute
        Inherits TheoryAttribute

        Public Sub New()
            If FiledoExe.Find() Is Nothing Then
                Skip = "payload\filedo\filedo.exe is not present: the interoperability check needs the reference implementation"
            End If
        End Sub
    End Class

    Private Shared Function RunFiledo(arguments As String(), ByRef output As String) As Integer
        Dim info As New ProcessStartInfo(FiledoExe.Find()) With {
            .UseShellExecute = False,
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True
        }
        For Each argument As String In arguments
            info.ArgumentList.Add(argument)
        Next
        Using child As Process = Process.Start(info)
            child.StandardInput.Close()
            Dim stdout As System.Threading.Tasks.Task(Of String) = child.StandardOutput.ReadToEndAsync()
            Dim stderr As System.Threading.Tasks.Task(Of String) = child.StandardError.ReadToEndAsync()
            If Not child.WaitForExit(90000) Then
                child.Kill(True)
                Throw New TimeoutException("filedo.exe did not finish within 90 s")
            End If
            output = stdout.Result & stderr.Result
            Return child.ExitCode
        End Using
    End Function

    ''' <summary>
    ''' A container the writer made opens with the owner's own implementation: <c>filedo.exe &lt;container&gt;
    ''' unsecure to &lt;folder&gt; p:&lt;password&gt; -y</c> restores the sealed true name with the original bytes.
    ''' The rows cover the fast branch with a multi-chunk original (the temp store's own shape: a 64+ byte
    ''' credential, an extension-less file) and the slow Argon2id branch with a short one, with and without
    ''' the container extension. Skipped when filedo.exe is absent from the checkout.
    ''' </summary>
    <TheoryWhenFiledo>
    <InlineData("", 3000000, "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789-abcdef")>
    <InlineData(".fd-sec", 3000000, "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789-abcdef")>
    <InlineData(".fd-sec", 777, "hunter2")>
    Public Sub A_container_written_here_opens_with_filedo_unsecure(extension As String, size As Integer, password As String)
        Dim work As String = Path.Combine(Path.GetTempPath(), "fdsecw-interop-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(work)
        Try
            Dim plaintext As Byte() = PatternBytes(size)
            Dim container As String = Path.Combine(work, "c0ffee" & Guid.NewGuid().ToString("N") & extension)
            Using writer As New FileStream(container, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                FdSecWriter.Write(plaintext, "animation.mp4", password, writer)
            End Using

            Dim restored As String = Path.Combine(work, "restored")
            Directory.CreateDirectory(restored)
            Dim output As String = Nothing
            Dim exitCode As Integer = RunFiledo({container, "unsecure", "to", restored, "p:" & password, "-y"}, output)

            Assert.True(exitCode = 0, "filedo.exe unsecure exited " & exitCode.ToString() & ": " & output)
            Dim files As String() = Directory.GetFiles(restored)
            Assert.True(files.Length = 1, "filedo.exe restored " & files.Length.ToString() & " files: " & output)
            Assert.Equal("animation.mp4", Path.GetFileName(files(0)))
            Assert.Equal(plaintext, File.ReadAllBytes(files(0)))
        Finally
            Directory.Delete(work, recursive:=True)
        End Try
    End Sub

End Class
#End If
