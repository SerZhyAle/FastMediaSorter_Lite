#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

' Spec 234 T-1 slice A: the streaming halves of DEVICE-EXCHANGE section 27 - the clipboard segments (item DN) and the sealed
' relay records (item DL) - against the vendored vectors in e2e-v1.json (see ExchangeSealedBoxTests for the file's provenance).
'
' What the vectors pin, and so what is reproduced byte for byte here: the clipboard sealed size, SHA-256 and first segment tag
' (the clear value is `byte i = i mod 251`), and every relay chunk (the ring nonces and media nonces are read out of the vector's
' own bytes and fed to the sender's test hooks, so the sender must produce those exact bytes). The segments themselves are not
' listed in the file (only their count), so the failing cases are derived from the sealed bytes the same way the vectors state
' them: the sealed bytes without their last segment, and the first two segments swapped.

''' <summary>A read-only stream over bytes that returns only a few bytes per read, in a repeating pattern, and counts how much of
''' the source has been taken.</summary>
Friend NotInheritable Class SealedTestChunkyStream
    Inherits Stream

    Private ReadOnly _data As Byte()
    Private ReadOnly _sizes As Integer()
    Private _pos As Integer
    Private _call As Integer
    Private _disposed As Boolean

    Public Sub New(data As Byte(), sizes As Integer())
        _data = data
        _sizes = sizes
    End Sub

    ''' <summary>How many bytes of the source have been handed out.</summary>
    Public ReadOnly Property Consumed As Integer
        Get
            Return _pos
        End Get
    End Property

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Return _disposed
        End Get
    End Property

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If count = 0 OrElse _pos >= _data.Length Then Return 0
        Dim n As Integer = Math.Min(Math.Min(count, _sizes(_call Mod _sizes.Length)), _data.Length - _pos)
        _call += 1
        System.Buffer.BlockCopy(_data, _pos, buffer, offset, n)
        _pos += n
        Return n
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        _disposed = True
        MyBase.Dispose(disposing)
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return False
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
    End Sub

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        Throw New NotSupportedException()
    End Sub
End Class

Public Class ExchangeSealedStreamTests

    ' ---------------------------------------------------------------- helpers

    Private Shared Function ClearValue(size As Integer) As Byte()
        Dim b(size - 1) As Byte
        For i As Integer = 0 To size - 1
            b(i) = CByte(i Mod 251)
        Next
        Return b
    End Function

    Private Shared Function Sha(data As Byte()) As String
        Return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
    End Function

    Private Shared Function SameBytes(a As Byte(), b As Byte()) As Boolean
        If a Is Nothing OrElse b Is Nothing Then Return a Is b
        Return a.Length = b.Length AndAlso a.SequenceEqual(b)
    End Function

    Private Shared Function Concat(ParamArray parts As Byte()()) As Byte()
        Using ms As New MemoryStream()
            For Each p As Byte() In parts
                ms.Write(p, 0, p.Length)
            Next
            Return ms.ToArray()
        End Using
    End Function

    Private Shared Function Slice(data As Byte(), start As Integer, count As Integer) As Byte()
        Dim r(count - 1) As Byte
        System.Buffer.BlockCopy(data, start, r, 0, count)
        Return r
    End Function

    ' Reads a stream to its end with read sizes that cycle through the given pattern.
    Private Shared Function ReadAll(s As Stream, sizes As Integer()) As Byte()
        Using ms As New MemoryStream()
            Dim i As Integer = 0
            While True
                Dim buf(sizes(i Mod sizes.Length) - 1) As Byte
                Dim n As Integer = s.Read(buf, 0, buf.Length)
                If n <= 0 Then Exit While
                ms.Write(buf, 0, n)
                i += 1
            End While
            Return ms.ToArray()
        End Using
    End Function

    Private Shared ReadOnly OddBufferSizes As Integer()() = {
        New Integer() {1},
        New Integer() {13, 4096, 70000, 65535, 2},
        New Integer() {65536},
        New Integer() {131072}}

    Private Shared ReadOnly OddSourceChunks As Integer()() = {
        New Integer() {1},
        New Integer() {7, 65535, 3},
        New Integer() {65536},
        New Integer() {65537, 2},
        New Integer() {100000, 1, 99999},
        New Integer() {4096}}

    Private Shared Function TestValueKey(Optional seed As Byte = 7) As Byte()
        Dim k(31) As Byte
        For i As Integer = 0 To 31
            k(i) = CByte((i * 5 + seed) And &HFF)
        Next
        Return k
    End Function

    Private Shared Function OpenAll(sealedBytes As Byte(), vk As Byte(), clearSize As Long, clearSha As String, ByRef opened As Byte()) As ExchangeClipboardOpenOutcome
        Using source As New MemoryStream(sealedBytes, False)
            Using target As New MemoryStream()
                Dim outcome As ExchangeClipboardOpenOutcome = ExchangeClipboardSeal.Open(source, target, vk, clearSize, clearSha)
                opened = target.ToArray()
                Return outcome
            End Using
        End Using
    End Function

    ' ---------------------------------------------------------------- clipboard: the vectors

    <Fact>
    Public Sub Every_clipboard_vector_gives_its_value_key_sizes_hashes_and_first_segment_tag()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim seen As Integer = 0
            For Each c As JsonElement In doc.RootElement.GetProperty("clipboard").EnumerateArray()
                Dim name As String = c.GetProperty("name").GetString()
                Dim contentKey As Byte() = E2eVectors.Hex(c.GetProperty("contentKey").GetString())
                Dim vk As Byte() = ExchangeClipboardSeal.ValueKey(contentKey)
                Assert.Equal(c.GetProperty("valueKey").GetString(), E2eVectors.HexOf(vk))

                Dim size As Integer = c.GetProperty("clearSize").GetInt32()
                Dim clear As Byte() = ClearValue(size)
                Assert.Equal(c.GetProperty("clearSha256").GetString(), Sha(clear))
                Assert.Equal(c.GetProperty("segments").GetInt64(), ExchangeClipboardSeal.SegmentCount(size))
                Assert.Equal(c.GetProperty("sealedSize").GetInt64(), ExchangeClipboardSeal.SealedSize(size))

                Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
                Assert.Equal(c.GetProperty("sealedSize").GetInt64(), CLng(sealedBytes.Length))
                Assert.Equal(c.GetProperty("sealedSha256").GetString(), Sha(sealedBytes))
                Dim firstSegment As Integer = Math.Min(size, ExchangeClipboardSeal.SegmentSize)
                Assert.True(String.Equals(c.GetProperty("firstSegmentTag").GetString(), E2eVectors.HexOf(Slice(sealedBytes, firstSegment, ExchangeClipboardSeal.TagLength)), StringComparison.Ordinal), name & ": the first segment tag")

                ' the streaming form reports the same sizes and hashes once it has been read to the end
                Using src As New MemoryStream(clear, False)
                    Using s As New ExchangeClipboardSealingStream(src, vk)
                        Assert.False(s.IsComplete)
                        Assert.Null(s.ClearSha256Hex)
                        Assert.True(SameBytes(sealedBytes, ReadAll(s, New Integer() {4096})))
                        Assert.True(s.IsComplete)
                        Assert.Equal(CLng(size), s.ClearSize)
                        Assert.Equal(CLng(sealedBytes.Length), s.SealedSize)
                        Assert.Equal(c.GetProperty("clearSha256").GetString(), s.ClearSha256Hex)
                        Assert.Equal(c.GetProperty("sealedSha256").GetString(), s.SealedSha256Hex)
                    End Using
                End Using
                seen += 1
            Next
            Assert.Equal(3, seen)
        End Using
    End Sub

    <Fact>
    Public Sub Every_clipboard_vector_value_opens_back_to_the_clear_bytes_through_the_one_shot_and_the_stream()
        Using doc As JsonDocument = E2eVectors.Load()
            For Each c As JsonElement In doc.RootElement.GetProperty("clipboard").EnumerateArray()
                Dim vk As Byte() = E2eVectors.Hex(c.GetProperty("valueKey").GetString())
                Dim size As Integer = c.GetProperty("clearSize").GetInt32()
                Dim clear As Byte() = ClearValue(size)
                Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
                Dim opened As Byte() = Nothing
                Assert.Equal(ExchangeClipboardOpenOutcome.Opened, OpenAll(sealedBytes, vk, size, c.GetProperty("clearSha256").GetString(), opened))
                Assert.True(SameBytes(clear, opened))
                Assert.True(SameBytes(clear, ExchangeClipboardSeal.OpenBytes(sealedBytes, vk, size, c.GetProperty("clearSha256").GetString())))
                Using src As New MemoryStream(sealedBytes, False)
                    Using o As New ExchangeClipboardOpeningStream(src, vk)
                        Assert.False(o.IsComplete)
                        Assert.True(SameBytes(clear, ReadAll(o, New Integer() {1000})))
                        Assert.True(o.IsComplete)
                        Assert.Equal(CLng(size), o.ClearSize)
                        Assert.Equal(c.GetProperty("clearSha256").GetString(), o.ClearSha256Hex)
                    End Using
                End Using
            Next
        End Using
    End Sub

    <Fact>
    Public Sub The_failing_clipboard_vectors_are_refused_and_hand_on_nothing()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim checkedCases As Integer = 0
            For Each c As JsonElement In doc.RootElement.GetProperty("clipboard").EnumerateArray()
                Dim name As String = c.GetProperty("name").GetString()
                Dim vk As Byte() = E2eVectors.Hex(c.GetProperty("valueKey").GetString())
                Dim size As Integer = c.GetProperty("clearSize").GetInt32()
                Dim sha As String = c.GetProperty("clearSha256").GetString()
                Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(ClearValue(size), vk)
                Dim failing As String = c.GetProperty("failing").GetRawText()

                If failing.Contains("truncated") Then
                    ' the sealed bytes without their last segment
                    Dim lastStart As Integer = ((sealedBytes.Length - 1) \ ExchangeClipboardSeal.SealedSegmentSize) * ExchangeClipboardSeal.SealedSegmentSize
                    Dim cut As Byte() = Slice(sealedBytes, 0, lastStart)
                    Dim opened As Byte() = Nothing
                    Assert.Equal(ExchangeClipboardOpenOutcome.MissingLastSegment, OpenAll(cut, vk, size, sha, opened))
                    Assert.Empty(opened)
                    Assert.Null(ExchangeClipboardSeal.OpenBytes(cut, vk, size, sha))
                    checkedCases += 1
                End If
                If failing.Contains("reordered") Then
                    ' the first two sealed segments swapped
                    Dim seg0 As Byte() = Slice(sealedBytes, 0, ExchangeClipboardSeal.SealedSegmentSize)
                    Dim seg1Length As Integer = Math.Min(sealedBytes.Length - ExchangeClipboardSeal.SealedSegmentSize, ExchangeClipboardSeal.SealedSegmentSize)
                    Dim seg1 As Byte() = Slice(sealedBytes, ExchangeClipboardSeal.SealedSegmentSize, seg1Length)
                    Dim restStart As Integer = ExchangeClipboardSeal.SealedSegmentSize + seg1Length
                    Dim swapped As Byte() = Concat(seg1, seg0, Slice(sealedBytes, restStart, sealedBytes.Length - restStart))
                    Dim opened As Byte() = Nothing
                    Assert.Equal(ExchangeClipboardOpenOutcome.TagFailed, OpenAll(swapped, vk, size, sha, opened))
                    Assert.Empty(opened)
                    Assert.Null(ExchangeClipboardSeal.OpenBytes(swapped, vk, size, sha))
                    checkedCases += 1
                End If
            Next
            Assert.Equal(5, checkedCases)      ' truncated x3, reordered x2
        End Using
    End Sub

    ' ---------------------------------------------------------------- clipboard: the primitives

    <Fact>
    Public Sub Segment_nonces_are_eleven_bytes_of_the_index_then_the_last_flag()
        Assert.Equal("000000000000000000000000", E2eVectors.HexOf(ExchangeClipboardSeal.SegmentNonce(0, False)))
        Assert.Equal("000000000000000000000001", E2eVectors.HexOf(ExchangeClipboardSeal.SegmentNonce(0, True)))
        Assert.Equal("000000000000000000010201", E2eVectors.HexOf(ExchangeClipboardSeal.SegmentNonce(258, True)))
        Assert.Equal("000000000000000000010200", E2eVectors.HexOf(ExchangeClipboardSeal.SegmentNonce(258, False)))
        Assert.Throws(Of ArgumentOutOfRangeException)(Sub() ExchangeClipboardSeal.SegmentNonce(-1, False))
    End Sub

    <Fact>
    Public Sub Segment_counts_and_sealed_sizes_follow_the_clear_size()
        Assert.Equal(0L, ExchangeClipboardSeal.SegmentCount(0))
        Assert.Equal(1L, ExchangeClipboardSeal.SegmentCount(1))
        Assert.Equal(1L, ExchangeClipboardSeal.SegmentCount(65536))
        Assert.Equal(2L, ExchangeClipboardSeal.SegmentCount(65537))
        Assert.Equal(2L, ExchangeClipboardSeal.SegmentCount(131072))
        Assert.Equal(0L, ExchangeClipboardSeal.SealedSize(0))
        Assert.Equal(17L, ExchangeClipboardSeal.SealedSize(1))
        Assert.Equal(65552L, ExchangeClipboardSeal.SealedSize(65536))
        Assert.Equal(65537L + 32L, ExchangeClipboardSeal.SealedSize(65537))
        Assert.Equal(65536, ExchangeClipboardSeal.SegmentSize)
        Assert.Equal(65552, ExchangeClipboardSeal.SealedSegmentSize)
    End Sub

    <Fact>
    Public Sub Sizes_at_every_segment_boundary_round_trip_and_match_the_computed_sealed_size()
        Dim vk As Byte() = TestValueKey()
        For Each size As Integer In {1, 15, 16, 17, 65535, 65536, 65537, 131071, 131072, 131073, 200000}
            Dim clear As Byte() = ClearValue(size)
            Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
            Assert.Equal(ExchangeClipboardSeal.SealedSize(size), CLng(sealedBytes.Length))
            Dim opened As Byte() = Nothing
            Assert.Equal(ExchangeClipboardOpenOutcome.Opened, OpenAll(sealedBytes, vk, size, Sha(clear), opened))
            Assert.True(SameBytes(clear, opened), "size " & size.ToString())
        Next
    End Sub

    <Fact>
    Public Sub An_empty_value_seals_to_nothing_and_nothing_opens_as_a_value()
        Dim vk As Byte() = TestValueKey()
        Assert.Empty(ExchangeClipboardSeal.SealBytes(New Byte() {}, vk))
        Dim opened As Byte() = Nothing
        Assert.Equal(ExchangeClipboardOpenOutcome.MissingLastSegment, OpenAll(New Byte() {}, vk, 0, Sha(New Byte() {}), opened))
        Assert.Null(ExchangeClipboardSeal.OpenBytes(New Byte() {}, vk, 0, Sha(New Byte() {})))
        Using empty As New MemoryStream()
            Using target As New MemoryStream()
                Dim result As ExchangeClipboardSealResult = ExchangeClipboardSeal.Seal(empty, target, vk)
                Assert.Equal(0L, result.ClearSize)
                Assert.Equal(0L, result.SealedSize)
                Assert.Equal(0L, target.Length)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Seal_reports_the_clear_and_sealed_sizes_and_hashes()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(70000)
        Using src As New MemoryStream(clear, False)
            Using target As New MemoryStream()
                Dim r As ExchangeClipboardSealResult = ExchangeClipboardSeal.Seal(src, target, vk)
                Assert.Equal(70000L, r.ClearSize)
                Assert.Equal(Sha(clear), r.ClearSha256Hex)
                Assert.Equal(target.Length, r.SealedSize)
                Assert.Equal(Sha(target.ToArray()), r.SealedSha256Hex)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Sealing_the_same_value_twice_gives_the_same_bytes_and_another_key_gives_other_bytes()
        Dim clear As Byte() = ClearValue(5000)
        Dim a As Byte() = ExchangeClipboardSeal.SealBytes(clear, TestValueKey(1))
        Dim b As Byte() = ExchangeClipboardSeal.SealBytes(clear, TestValueKey(1))
        Dim c As Byte() = ExchangeClipboardSeal.SealBytes(clear, TestValueKey(2))
        Assert.True(SameBytes(a, b))           ' deterministic per value key: the nonce is the segment index
        Assert.False(SameBytes(a, c))
    End Sub

    ' ---------------------------------------------------------------- clipboard: streaming

    <Fact>
    Public Sub Sealing_over_a_source_that_returns_odd_chunks_equals_the_one_shot_seal_for_any_read_size()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(200000)
        Dim reference As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        For Each chunks As Integer() In OddSourceChunks
            Using src As New SealedTestChunkyStream(clear, chunks)
                Using s As New ExchangeClipboardSealingStream(src, vk)
                    Assert.True(SameBytes(reference, ReadAll(s, OddBufferSizes(1))), "source chunks " & String.Join(",", chunks))
                End Using
            End Using
        Next
        For Each sizes As Integer() In OddBufferSizes
            Using src As New MemoryStream(clear, False)
                Using s As New ExchangeClipboardSealingStream(src, vk)
                    Assert.True(SameBytes(reference, ReadAll(s, sizes)), "read sizes " & String.Join(",", sizes))
                End Using
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Opening_over_a_sealed_source_that_returns_odd_chunks_equals_the_clear_value_for_any_read_size()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(200000)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        For Each chunks As Integer() In OddSourceChunks
            Using src As New SealedTestChunkyStream(sealedBytes, chunks)
                Using o As New ExchangeClipboardOpeningStream(src, vk)
                    Assert.True(SameBytes(clear, ReadAll(o, OddBufferSizes(1))), "source chunks " & String.Join(",", chunks))
                    Assert.True(o.IsComplete)
                    Assert.Equal(Sha(clear), o.ClearSha256Hex)
                End Using
            End Using
            ' and through the checked form, which also states the size and the hash
            Using src As New SealedTestChunkyStream(sealedBytes, chunks)
                Using target As New MemoryStream()
                    Assert.Equal(ExchangeClipboardOpenOutcome.Opened, ExchangeClipboardSeal.Open(src, target, vk, clear.Length, Sha(clear).ToUpperInvariant()))
                    Assert.True(SameBytes(clear, target.ToArray()))
                End Using
            End Using
        Next
    End Sub

    <Fact>
    Public Sub Sealing_reads_at_most_two_segments_ahead_and_opening_at_most_two_sealed_segments()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(4 * 65536 + 5)
        Using src As New SealedTestChunkyStream(clear, New Integer() {70000})
            Using s As New ExchangeClipboardSealingStream(src, vk)
                Dim one(0) As Byte
                Assert.Equal(1, s.Read(one, 0, 1))
                Assert.InRange(src.Consumed, ExchangeClipboardSeal.SegmentSize, 2 * ExchangeClipboardSeal.SegmentSize)
                Assert.False(s.IsComplete)
            End Using
        End Using
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        Using src As New SealedTestChunkyStream(sealedBytes, New Integer() {70000})
            Using o As New ExchangeClipboardOpeningStream(src, vk)
                Dim one(0) As Byte
                Assert.Equal(1, o.Read(one, 0, 1))
                Assert.InRange(src.Consumed, ExchangeClipboardSeal.SealedSegmentSize, 2 * ExchangeClipboardSeal.SealedSegmentSize)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Streams_close_their_source_unless_told_to_leave_it_open()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(100)
        Dim closing As New SealedTestChunkyStream(clear, New Integer() {10})
        Using s As New ExchangeClipboardSealingStream(closing, vk)
            ReadAll(s, New Integer() {50})
        End Using
        Assert.True(closing.IsDisposed)
        Dim kept As New SealedTestChunkyStream(clear, New Integer() {10})
        Using s As New ExchangeClipboardSealingStream(kept, vk, True)
            ReadAll(s, New Integer() {50})
        End Using
        Assert.False(kept.IsDisposed)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        Dim keptSealed As New SealedTestChunkyStream(sealedBytes, New Integer() {10})
        Using o As New ExchangeClipboardOpeningStream(keptSealed, vk, True)
            ReadAll(o, New Integer() {50})
        End Using
        Assert.False(keptSealed.IsDisposed)
    End Sub

    ' ---------------------------------------------------------------- clipboard: damage

    ' Reads until the stream throws its typed failure; the number of clear bytes handed on before that.
    Private Shared Function ReadUntilFailure(s As Stream, ByRef delivered As Long) As ExchangeSealedFailedException
        delivered = 0
        Dim buf(9999) As Byte
        Try
            While True
                Dim n As Integer = s.Read(buf, 0, buf.Length)
                If n <= 0 Then Exit While
                delivered += n
            End While
        Catch ex As ExchangeSealedFailedException
            Return ex
        End Try
        Return Nothing
    End Function

    <Fact>
    Public Sub A_segment_is_handed_on_only_after_its_tag_held_and_the_failure_is_typed_and_sticky()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(4 * 65536 + 5)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)

        ' a bit flipped inside the second segment: the first segment (65536 bytes) was good, nothing after it is handed on
        Dim tampered As Byte() = CType(sealedBytes.Clone(), Byte())
        tampered(ExchangeClipboardSeal.SealedSegmentSize + 100) = CByte(tampered(ExchangeClipboardSeal.SealedSegmentSize + 100) Xor 1)
        Using src As New MemoryStream(tampered, False)
            Using o As New ExchangeClipboardOpeningStream(src, vk)
                Dim delivered As Long
                Dim ex As ExchangeSealedFailedException = ReadUntilFailure(o, delivered)
                Assert.NotNull(ex)
                Assert.Equal(ExchangeSealedFailure.TagFailed, ex.Failure)
                Assert.Equal(65536L, delivered)
                Assert.False(o.IsComplete)
                Assert.Null(o.ClearSha256Hex)
                Dim again As ExchangeSealedFailedException = Assert.Throws(Of ExchangeSealedFailedException)(Sub() o.Read(New Byte(9) {}, 0, 10))
                Assert.Equal(ExchangeSealedFailure.TagFailed, again.Failure)
                Assert.DoesNotContain(E2eVectors.HexOf(vk), ex.ToString())
            End Using
        End Using

        ' a flipped bit in the very first segment: not one byte is handed on
        Dim first As Byte() = CType(sealedBytes.Clone(), Byte())
        first(0) = CByte(first(0) Xor 1)
        Using src As New MemoryStream(first, False)
            Using o As New ExchangeClipboardOpeningStream(src, vk)
                Dim delivered As Long
                Assert.NotNull(ReadUntilFailure(o, delivered))
                Assert.Equal(0L, delivered)
            End Using
        End Using

        ' the middle two segments swapped: the position is part of the nonce
        Dim swapped As Byte() = CType(sealedBytes.Clone(), Byte())
        Dim s1 As Integer = ExchangeClipboardSeal.SealedSegmentSize
        Dim s2 As Integer = 2 * ExchangeClipboardSeal.SealedSegmentSize
        System.Buffer.BlockCopy(sealedBytes, s2, swapped, s1, ExchangeClipboardSeal.SealedSegmentSize)
        System.Buffer.BlockCopy(sealedBytes, s1, swapped, s2, ExchangeClipboardSeal.SealedSegmentSize)
        Using src As New MemoryStream(swapped, False)
            Using o As New ExchangeClipboardOpeningStream(src, vk)
                Dim delivered As Long
                Dim ex As ExchangeSealedFailedException = ReadUntilFailure(o, delivered)
                Assert.NotNull(ex)
                Assert.Equal(65536L, delivered)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_stream_cut_at_a_segment_boundary_is_a_missing_last_segment_and_never_a_short_value()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(3 * 65536 + 10)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        For Each keep As Integer In {1, 2, 3}
            Dim cut As Byte() = Slice(sealedBytes, 0, keep * ExchangeClipboardSeal.SealedSegmentSize)
            Using src As New MemoryStream(cut, False)
                Using o As New ExchangeClipboardOpeningStream(src, vk)
                    Dim delivered As Long
                    Dim ex As ExchangeSealedFailedException = ReadUntilFailure(o, delivered)
                    Assert.NotNull(ex)
                    Assert.Equal(ExchangeSealedFailure.MissingLastSegment, ex.Failure)
                    ' the segments before the cut one were good and handed on; the cut one never is
                    Assert.Equal((keep - 1) * 65536L, delivered)
                    Assert.False(o.IsComplete)
                End Using
            End Using
            Dim opened As Byte() = Nothing
            Assert.Equal(ExchangeClipboardOpenOutcome.MissingLastSegment, OpenAll(cut, vk, clear.Length, Sha(clear), opened))
            Assert.Null(ExchangeClipboardSeal.OpenBytes(cut, vk, clear.Length, Sha(clear)))
        Next
    End Sub

    <Fact>
    Public Sub A_stream_cut_inside_a_segment_or_with_bytes_added_never_opens()
        Dim vk As Byte() = TestValueKey()
        Dim clear As Byte() = ClearValue(1000)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        Dim opened As Byte() = Nothing
        Assert.Equal(ExchangeClipboardOpenOutcome.TagFailed, OpenAll(Slice(sealedBytes, 0, sealedBytes.Length - 1), vk, 1000, Sha(clear), opened))
        Assert.Empty(opened)
        Assert.Equal(ExchangeClipboardOpenOutcome.TagFailed, OpenAll(Concat(sealedBytes, New Byte() {0}), vk, 1000, Sha(clear), opened))
        Assert.Empty(opened)
        ' a whole second segment appended: the first one was sealed as the last
        Assert.NotEqual(ExchangeClipboardOpenOutcome.Opened, OpenAll(Concat(sealedBytes, sealedBytes), vk, 1000, Sha(clear), opened))
        ' a "segment" that cannot hold one clear byte and a tag
        Assert.Equal(ExchangeClipboardOpenOutcome.Malformed, OpenAll(New Byte(15) {}, vk, 1, Sha(New Byte() {0}), opened))
        Assert.Equal(ExchangeClipboardOpenOutcome.Malformed, OpenAll(New Byte(9) {}, vk, 1, Sha(New Byte() {0}), opened))
        Assert.Empty(opened)
    End Sub

    <Fact>
    Public Sub Another_key_a_wrong_size_or_a_wrong_hash_is_refused_with_its_own_outcome()
        Dim vk As Byte() = TestValueKey(1)
        Dim clear As Byte() = ClearValue(70000)
        Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(clear, vk)
        Dim opened As Byte() = Nothing
        Assert.Equal(ExchangeClipboardOpenOutcome.TagFailed, OpenAll(sealedBytes, TestValueKey(2), clear.Length, Sha(clear), opened))
        Assert.Empty(opened)
        Assert.Equal(ExchangeClipboardOpenOutcome.SizeMismatch, OpenAll(sealedBytes, vk, clear.Length - 1, Sha(clear), opened))
        Assert.Equal(ExchangeClipboardOpenOutcome.HashMismatch, OpenAll(sealedBytes, vk, clear.Length, Sha(ClearValue(70001)), opened))
        Assert.Equal(ExchangeClipboardOpenOutcome.HashMismatch, OpenAll(sealedBytes, vk, clear.Length, Nothing, opened))
        ' OpenBytes holds back the clear bytes when the size or the hash of the box is not met
        Assert.Null(ExchangeClipboardSeal.OpenBytes(sealedBytes, vk, clear.Length - 1, Sha(clear)))
        Assert.Null(ExchangeClipboardSeal.OpenBytes(sealedBytes, vk, clear.Length, Sha(ClearValue(70001))))
        Assert.NotNull(ExchangeClipboardSeal.OpenBytes(sealedBytes, vk, clear.Length, Sha(clear)))
    End Sub

    <Fact>
    Public Sub Bad_arguments_are_programmer_errors_with_no_key_in_the_message()
        Assert.Throws(Of ArgumentException)(Sub() ExchangeClipboardSeal.ValueKey(Nothing))
        Assert.Throws(Of ArgumentException)(Sub() ExchangeClipboardSeal.ValueKey(New Byte() {}))
        Dim shortKey As Byte() = New Byte(15) {}
        Using src As New MemoryStream()
            Dim ex As ArgumentException = Assert.Throws(Of ArgumentException)(Sub() MakeSealingStream(src, shortKey))
            Assert.DoesNotContain(E2eVectors.HexOf(shortKey), ex.Message)
            Assert.Throws(Of ArgumentException)(Sub() MakeOpeningStream(src, shortKey))
            Assert.Throws(Of ArgumentNullException)(Sub() MakeSealingStream(Nothing, TestValueKey()))
            Assert.Throws(Of ArgumentNullException)(Sub() MakeOpeningStream(Nothing, TestValueKey()))
        End Using
        Using s As New ExchangeClipboardSealingStream(New MemoryStream(New Byte() {1}), TestValueKey())
            Assert.False(s.CanSeek)
            Assert.False(s.CanWrite)
            Assert.Throws(Of NotSupportedException)(Sub() s.Write(New Byte(0) {}, 0, 1))
            Assert.Throws(Of NotSupportedException)(Sub() s.Length.ToString())
        End Using
    End Sub

    Private Shared Sub MakeSealingStream(source As Stream, key As Byte())
        Using s As New ExchangeClipboardSealingStream(source, key)
        End Using
    End Sub

    Private Shared Sub MakeOpeningStream(source As Stream, key As Byte())
        Using s As New ExchangeClipboardOpeningStream(source, key)
        End Using
    End Sub

    ' ---------------------------------------------------------------- clipboard: the payload members

    <Fact>
    Public Sub The_payload_is_written_in_the_contract_order_and_read_back()
        Dim p As ExchangeClipboardSealedPayload = Nothing
        Dim sha As String = New String("a"c, 64)
        Assert.True(ExchangeClipboardSealedPayload.TryCreate("text", "text/plain", "", 12, sha, p))
        Assert.Equal("{""kind"":""text"",""mimeType"":""text/plain"",""size"":12,""sha256"":""" & sha & """}", Encoding.UTF8.GetString(p.ToJsonBytes()))
        Assert.True(ExchangeClipboardSealedPayload.TryCreate("file", "application/zip", "a b.zip", 9, sha.ToUpperInvariant(), p))
        Assert.Equal("{""kind"":""file"",""mimeType"":""application/zip"",""name"":""a b.zip"",""size"":9,""sha256"":""" & sha & """}", Encoding.UTF8.GetString(p.ToJsonBytes()))
        Dim back As ExchangeClipboardSealedPayload = Nothing
        Assert.True(ExchangeClipboardSealedPayload.TryParse(p.ToJsonBytes(), back))
        Assert.Equal("file", back.Kind)
        Assert.Equal("application/zip", back.MimeType)
        Assert.Equal("a b.zip", back.Name)
        Assert.Equal(9L, back.Size)
        Assert.Equal(sha, back.Sha256)
        Assert.Equal("ExchangeClipboardSealedPayload", back.ToString())
        Assert.True(back.Matches(9, sha.ToUpperInvariant()))
        Assert.False(back.Matches(10, sha))
        Assert.False(back.Matches(9, New String("b"c, 64)))
        Assert.False(back.Matches(9, Nothing))
    End Sub

    <Fact>
    Public Sub The_payload_refuses_members_that_break_the_rules()
        Dim sha As String = New String("a"c, 64)
        Dim p As ExchangeClipboardSealedPayload = Nothing
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("video", "", "", 1, sha, p))
        Assert.Null(p)
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("text", "", "", 0, sha, p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("text", "", "", 1, sha.Substring(1), p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("text", "", "", 1, New String("g"c, 64), p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("text", "", "", 1, Nothing, p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("text", "plain", "", 1, sha, p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("file", "a/b", "..", 1, sha, p))
        Assert.False(ExchangeClipboardSealedPayload.TryCreate("file", "a/b", "x/y", 1, sha, p))
        For Each text As String In {"", "not json", "[]", "{}", "{""kind"":""text""}", "{""kind"":""text"",""size"":""1"",""sha256"":""" & sha & """}",
                                    "{""kind"":1,""size"":1,""sha256"":""" & sha & """}", "{""kind"":""text"",""size"":1,""sha256"":""" & sha & """,""mimeType"":5}",
                                    "{""kind"":""text"",""size"":1.5,""sha256"":""" & sha & """}"}
            Assert.False(ExchangeClipboardSealedPayload.TryParse(Encoding.UTF8.GetBytes(text), p), text)
        Next
        Assert.False(ExchangeClipboardSealedPayload.TryParse(Nothing, p))
        ' a member the reader does not know is ignored
        Assert.True(ExchangeClipboardSealedPayload.TryParse(Encoding.UTF8.GetBytes("{""kind"":""text"",""size"":1,""sha256"":""" & sha & """,""later"":[1]}"), p))
    End Sub

    <Fact>
    Public Sub Every_clipboard_vector_runs_from_the_box_to_the_opened_value()
        ' box -> payload (kind, size, sha256) and content key -> value key -> the sealed value, as a receiver does it
        Using doc As JsonDocument = E2eVectors.Load()
            Dim devices As Dictionary(Of String, E2eVectors.VectorDevice) = E2eVectors.Devices(doc.RootElement)
            Dim phone As E2eVectors.VectorDevice = devices("phone")
            Dim tablet As E2eVectors.VectorDevice = devices("tablet")
            Using tabletKey As ExchangeDeviceKey = E2eVectors.Key(tablet)
                For Each c As JsonElement In doc.RootElement.GetProperty("clipboard").EnumerateArray()
                    Dim box As ExchangeSealedBox = Nothing
                    Assert.True(ExchangeSealedBox.TryRead(c.GetProperty("box"), box))
                    Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, tablet.DeviceId, tabletKey, ExchangeSealedContext.Clipboard, Function(f As String) phone.Kem, True)
                    Assert.True(opened.Succeeded, c.GetProperty("name").GetString())
                    Dim payload As ExchangeClipboardSealedPayload = Nothing
                    Assert.True(ExchangeClipboardSealedPayload.TryParse(opened.Payload, payload))
                    Assert.True(payload.Matches(c.GetProperty("clearSize").GetInt64(), c.GetProperty("clearSha256").GetString()), "the payload states the vector's size and hash")
                    Dim vk As Byte() = ExchangeClipboardSeal.ValueKey(opened.ContentKey)
                    Assert.Equal(c.GetProperty("valueKey").GetString(), E2eVectors.HexOf(vk))
                    Dim sealedBytes As Byte() = ExchangeClipboardSeal.SealBytes(ClearValue(CInt(payload.Size)), vk)
                    Assert.Equal(c.GetProperty("sealedSha256").GetString(), Sha(sealedBytes))
                    Dim clear As Byte() = ExchangeClipboardSeal.OpenBytes(sealedBytes, vk, payload.Size, payload.Sha256)
                    Assert.NotNull(clear)
                    Assert.Equal(c.GetProperty("clearSha256").GetString(), Sha(clear))
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_value_travels_through_a_fresh_box_and_sealed_segments_and_a_substituted_value_is_caught()
        Using sender As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
            Using receiver As ExchangeDeviceKey = ExchangeDeviceKey.Generate()
                Dim clear As Byte() = ClearValue(150000)
                Dim payload As ExchangeClipboardSealedPayload = Nothing
                Assert.True(ExchangeClipboardSealedPayload.TryCreate("file", "application/octet-stream", "data.bin", clear.Length, Sha(clear), payload))
                Dim json As Byte() = payload.ToJsonBytes()
                Dim recipients As New List(Of ExchangeSealedRecipient) From {New ExchangeSealedRecipient("dev-r", receiver.Kem)}
                Dim seal As ExchangeSealResult = ExchangeSealedBoxes.Seal(json, ExchangeSealedContext.Clipboard, "dev-s", sender, recipients, True)
                Assert.True(seal.Succeeded)
                Dim sealedValue As Byte() = ExchangeClipboardSeal.SealBytes(clear, ExchangeClipboardSeal.ValueKey(seal.ContentKey))

                Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(seal.Box, "dev-r", receiver, ExchangeSealedContext.Clipboard, Function(f As String) sender.Kem, True)
                Assert.True(opened.Succeeded)
                Dim got As ExchangeClipboardSealedPayload = Nothing
                Assert.True(ExchangeClipboardSealedPayload.TryParse(opened.Payload, got))
                Dim vk As Byte() = ExchangeClipboardSeal.ValueKey(opened.ContentKey)
                Assert.True(SameBytes(clear, ExchangeClipboardSeal.OpenBytes(sealedValue, vk, got.Size, got.Sha256)))

                ' a value sealed under another box's key does not open under this one
                Dim other As ExchangeSealResult = ExchangeSealedBoxes.Seal(json, ExchangeSealedContext.Clipboard, "dev-s", sender, recipients, True)
                Dim foreign As Byte() = ExchangeClipboardSeal.SealBytes(clear, ExchangeClipboardSeal.ValueKey(other.ContentKey))
                Assert.Null(ExchangeClipboardSeal.OpenBytes(foreign, vk, got.Size, got.Sha256))
                ' the right key but a different value than the box states: the hash stops it
                Dim swappedValue As Byte() = ExchangeClipboardSeal.SealBytes(ClearValue(150000).Select(Function(b) CByte(b Xor 1)).ToArray(), vk)
                Assert.Null(ExchangeClipboardSeal.OpenBytes(swappedValue, vk, got.Size, got.Sha256))
            End Using
        End Using
    End Sub

    ' ---------------------------------------------------------------- relay: constants and small functions

    <Fact>
    Public Sub The_relay_constants_aad_and_key_id_follow_the_contract()
        Assert.Equal("application/x-fmsx-sealed", ExchangeRelayRecords.ContentType)
        Assert.Equal(CByte(&H4B), ExchangeRelayRecords.TypeKeyRing)
        Assert.Equal(CByte(&H4D), ExchangeRelayRecords.TypeMedia)
        Assert.Equal(68, ExchangeRelayRecords.RingEntryLength)
        Assert.Equal(64, ExchangeRelayRecords.MaxRingEntries)
        Assert.Equal(1048575, ExchangeRelayRecords.MaxRecordLength)
        Assert.Equal(4294967296L, ExchangeRelayRecords.MaxRecordsPerKey)
        Assert.Equal("relay-ring:b-1:5", Encoding.UTF8.GetString(ExchangeRelayRecords.RingAad("b-1", 5UI)))
        Assert.Equal("relay-media:b-1:4294967295:2", Encoding.UTF8.GetString(ExchangeRelayRecords.MediaAad("b-1", UInteger.MaxValue, 2)))

        Dim key As Byte() = ClearValue(32)
        Dim expected As Byte() = SHA256.HashData(Concat(Encoding.ASCII.GetBytes("fmsx-relay-v1 key-id"), key))
        Assert.Equal(E2eVectors.HexOf(Slice(expected, 0, 8)), E2eVectors.HexOf(ExchangeRelayRecords.KeyId(key)))
        Dim a As Byte() = ExchangeRelayRecords.NewKey()
        Assert.Equal(32, a.Length)
        Assert.False(SameBytes(a, ExchangeRelayRecords.NewKey()))
    End Sub

    <Fact>
    Public Sub A_record_is_framed_as_length_type_body_and_the_bound_is_held()
        Assert.Equal("000000034b0102", E2eVectors.HexOf(ExchangeRelayRecords.Frame(&H4B, New Byte() {1, 2})))
        Assert.Equal("000000014d", E2eVectors.HexOf(ExchangeRelayRecords.Frame(&H4D, New Byte() {})))
        Assert.Equal(4 + 1048575, ExchangeRelayRecords.Frame(&H4D, New Byte(1048573) {}).Length)
        Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRecords.Frame(&H4D, New Byte(1048574) {}))
    End Sub

    ' ---------------------------------------------------------------- relay: the vectors

    Private NotInheritable Class RelayVector
        Public BroadcastId As String = ""
        Public MediaType As String = ""
        Public GroupKeys As New Dictionary(Of String, Byte())(StringComparer.Ordinal)
        Public EpochKeys As New Dictionary(Of Integer, Byte())
        Public Chunks As New List(Of RelayChunk)
    End Class

    Private NotInheritable Class RelayChunk
        Public Flag As Integer
        Public Epoch As UInteger
        Public Payload As Byte() = Array.Empty(Of Byte)()
        Public ClearMedia As Byte() = Array.Empty(Of Byte)()
    End Class

    Private Shared Function LoadRelay(doc As JsonDocument) As RelayVector
        Dim relay As JsonElement = doc.RootElement.GetProperty("relay")
        Dim v As New RelayVector()
        v.BroadcastId = relay.GetProperty("broadcastId").GetString()
        v.MediaType = relay.GetProperty("mediaType").GetString()
        Assert.Equal(ExchangeRelayRecords.ContentType, relay.GetProperty("contentType").GetString())
        For Each p As JsonProperty In relay.GetProperty("groupKeys").EnumerateObject()
            v.GroupKeys.Add(p.Name, E2eVectors.Hex(p.Value.GetString()))
        Next
        For Each p As JsonProperty In relay.GetProperty("epochKeys").EnumerateObject()
            v.EpochKeys.Add(Integer.Parse(p.Name), E2eVectors.Hex(p.Value.GetString()))
        Next
        For Each c As JsonElement In relay.GetProperty("chunks").EnumerateArray()
            Dim chunk As New RelayChunk()
            chunk.Flag = c.GetProperty("flag").GetInt32()
            chunk.Epoch = c.GetProperty("epoch").GetUInt32()
            Dim payload As Byte() = Nothing
            Assert.True(ExchangeBase64Url.TryDecode(c.GetProperty("payload").GetString(), payload))
            chunk.Payload = payload
            Dim media As New List(Of Byte)
            For Each m As JsonElement In c.GetProperty("clearMedia").EnumerateArray()
                media.AddRange(E2eVectors.Hex(m.GetString()))
            Next
            chunk.ClearMedia = media.ToArray()
            v.Chunks.Add(chunk)
        Next
        Return v
    End Function

    ' All the records of a payload.
    Private Shared Function RecordsOf(payload As Byte()) As List(Of ExchangeRelayRecord)
        Dim reader As New ExchangeRelayRecordReader()
        reader.Append(payload)
        Dim list As New List(Of ExchangeRelayRecord)
        Dim r As ExchangeRelayRecord = Nothing
        While reader.TryRead(r)
            list.Add(r)
        End While
        Assert.False(reader.IsCorrupt)
        Assert.Equal(0, reader.Buffered)
        Return list
    End Function

    <Fact>
    Public Sub The_relay_vector_has_the_shape_the_tests_rely_on()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Assert.Equal("video/mp2t", v.MediaType)
            Assert.Equal(4, v.GroupKeys.Count)
            Assert.Equal(2, v.EpochKeys.Count)
            Assert.Equal(6, v.Chunks.Count)
            Dim headerEpochs As New List(Of ExchangeRelayRing)
            For Each chunk As RelayChunk In v.Chunks
                Dim records As List(Of ExchangeRelayRecord) = RecordsOf(chunk.Payload)
                If chunk.Flag = 2 Then
                    Assert.Equal(2, records.Count)
                    Assert.Equal(ExchangeRelayRecords.TypeKeyRing, records(0).Type)
                    Assert.Equal(ExchangeRelayRecords.TypeMedia, records(1).Type)
                    Dim ring As ExchangeRelayRing = Nothing
                    Assert.True(ExchangeRelayRing.TryParse(records(0).Body, ring))
                    Assert.Equal(chunk.Epoch, ring.Epoch)
                    Assert.Equal(v.MediaType, ring.MediaType)
                    headerEpochs.Add(ring)
                Else
                    Assert.Single(records)
                    Assert.Equal(ExchangeRelayRecords.TypeMedia, records(0).Type)
                End If
            Next
            ' epoch 1: own-1, guest-1, link-1; epoch 2 drops the guest key and replaces the own one
            Assert.Equal(New String() {"own-1", "guest-1", "link-1"}, KeyNamesOf(v, headerEpochs(0)))
            Assert.Equal(New String() {"own-2", "link-1"}, KeyNamesOf(v, headerEpochs(1)))
        End Using
    End Sub

    Private Shared Function KeyNamesOf(v As RelayVector, ring As ExchangeRelayRing) As String()
        Dim names As New List(Of String)
        For Each e As ExchangeRelayRingEntry In ring.Entries
            Dim found As String = ""
            For Each kv As KeyValuePair(Of String, Byte()) In v.GroupKeys
                If SameBytes(ExchangeRelayRecords.KeyId(kv.Value), e.KeyId) Then found = kv.Key
            Next
            names.Add(found)
        Next
        Return names.ToArray()
    End Function

    <Fact>
    Public Sub The_sender_reproduces_every_vector_chunk_byte_for_byte_from_the_nonces_in_the_vector()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Dim sender As ExchangeRelaySender = Nothing
            Try
                For i As Integer = 0 To v.Chunks.Count - 1
                    Dim chunk As RelayChunk = v.Chunks(i)
                    Dim records As List(Of ExchangeRelayRecord) = RecordsOf(chunk.Payload)
                    If chunk.Flag = 2 Then
                        If sender IsNot Nothing Then sender.Dispose()
                        Dim ring As ExchangeRelayRing = Nothing
                        Assert.True(ExchangeRelayRing.TryParse(records(0).Body, ring))
                        Dim keys As New List(Of Byte())
                        Dim nonces As New List(Of Byte())
                        For Each e As ExchangeRelayRingEntry In ring.Entries
                            Dim name As String = KeyNamesOf(v, ring)(keys.Count)
                            keys.Add(v.GroupKeys(name))
                            nonces.Add(e.Nonce)
                        Next
                        sender = New ExchangeRelaySender(v.BroadcastId, v.MediaType, chunk.Epoch, v.EpochKeys(CInt(chunk.Epoch)), keys, nonces)
                        Dim mediaNonce As Byte() = Slice(records(1).Body, 4, 12)
                        Dim built As Byte() = sender.BuildHeaderChunk(chunk.ClearMedia, mediaNonce)
                        Assert.Equal(E2eVectors.HexOf(chunk.Payload), E2eVectors.HexOf(built))
                    Else
                        Dim mediaNonce As Byte() = Slice(records(0).Body, 4, 12)
                        Dim built As Byte() = sender.SealMediaWithNonce(chunk.ClearMedia, CByte(chunk.Flag), mediaNonce)
                        Assert.Equal(E2eVectors.HexOf(chunk.Payload), E2eVectors.HexOf(built))
                    End If
                Next
                Assert.Equal(3L, sender.RecordsSealed)      ' header media + two more of epoch 2
                Assert.False(sender.IsExhausted)
            Finally
                If sender IsNot Nothing Then sender.Dispose()
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub Each_group_key_reads_exactly_the_epochs_the_vector_gives_it_and_nothing_else_plays()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Dim readsEpoch1 As String() = {"own-1", "guest-1", "link-1"}
            Dim readsEpoch2 As String() = {"own-2", "link-1"}
            For Each useFlag As Boolean In {True, False}
                For Each name As String In v.GroupKeys.Keys
                    Using listener As New ExchangeRelayListener(v.BroadcastId, v.GroupKeys(name))
                        Assert.False(listener.HoldsEpoch)
                        For Each chunk As RelayChunk In v.Chunks
                            Dim media As Byte() = Nothing
                            Dim outcome As ExchangeRelayOutcome = listener.ProcessChunk(chunk.Payload, If(useFlag, chunk.Flag, -1), media)
                            Dim reads As Boolean = If(chunk.Epoch = 1UI, readsEpoch1.Contains(name), readsEpoch2.Contains(name))
                            Dim label As String = name & " epoch " & chunk.Epoch.ToString() & " flag " & chunk.Flag.ToString()
                            If reads Then
                                Assert.True(outcome = ExchangeRelayOutcome.Media, label & " gave " & outcome.ToString())
                                Assert.Equal(E2eVectors.HexOf(chunk.ClearMedia), E2eVectors.HexOf(media))
                                Assert.Equal(chunk.Epoch, listener.Epoch)
                                Assert.Equal("video/mp2t", listener.MediaType)
                                Assert.Equal(ExchangeRelayFailure.None, listener.LastFailure)
                            ElseIf chunk.Flag = 2 Then
                                ' the ring of an epoch this key was dropped from (or never in): nothing plays
                                Assert.True(outcome = ExchangeRelayOutcome.SealedFailed, label & " gave " & outcome.ToString())
                                Assert.Equal(ExchangeRelayFailure.NoRingEntry, listener.LastFailure)
                                Assert.Null(media)
                            Else
                                Assert.True(outcome = ExchangeRelayOutcome.NeedReconnect, label & " gave " & outcome.ToString())
                                Assert.Null(media)
                            End If
                        Next
                    End Using
                Next
            Next
        End Using
    End Sub

    <Fact>
    Public Sub A_listener_that_joins_mid_stream_reconnects_once_and_a_wrong_flag_fails_the_tag()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Using listener As New ExchangeRelayListener(v.BroadcastId, v.GroupKeys("own-1"))
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.NeedReconnect, listener.ProcessChunk(v.Chunks(1).Payload, 1, media))
                Assert.Null(media)
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(v.Chunks(0).Payload, 2, media))
                ' the media of a chunk is bound to the flag of that chunk
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(v.Chunks(1).Payload, 0, media))
                Assert.Equal(ExchangeRelayFailure.MediaTagFailed, listener.LastFailure)
                Assert.Null(media)
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(v.Chunks(2).Payload, 1, media))
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(v.Chunks(1).Payload, 1, media))
                Assert.Equal(E2eVectors.HexOf(v.Chunks(1).ClearMedia), E2eVectors.HexOf(media))
            End Using
            ' another broadcast id: the ring's associated data differs, so the entry is found and its tag fails
            Using listener As New ExchangeRelayListener("another-broadcast", v.GroupKeys("own-1"))
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(v.Chunks(0).Payload, 2, media))
                Assert.Equal(ExchangeRelayFailure.RingTagFailed, listener.LastFailure)
                Assert.False(listener.HoldsEpoch)
            End Using
        End Using
    End Sub

    ' ---------------------------------------------------------------- relay: behaviour

    <Fact>
    Public Sub A_fresh_sender_and_listeners_round_trip_and_a_new_epoch_replaces_the_old_one()
        Dim gk1 As Byte() = ExchangeRelayRecords.NewKey()
        Dim gk2 As Byte() = ExchangeRelayRecords.NewKey()
        Dim outsider As Byte() = ExchangeRelayRecords.NewKey()
        Dim media1 As Byte() = ClearValue(1880)
        Dim media2 As Byte() = ClearValue(777)

        Dim e7 As Byte() = ExchangeRelayRecords.NewKey()
        Using sender As New ExchangeRelaySender("bc-1", "audio/aac", 7UI, e7, New List(Of Byte()) From {gk1, gk2})
            Assert.Equal(7UI, sender.Epoch)
            Dim ringOnly As Byte() = sender.BuildHeaderChunk(Nothing)          ' ADTS: no format header
            Dim chunkA As Byte() = sender.SealMedia(media1, 1)
            Dim chunkB As Byte() = sender.SealMedia(media2, 0)
            Assert.Equal(2L, sender.RecordsSealed)

            Using listener As New ExchangeRelayListener("bc-1", gk1)
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.KeyRing, listener.ProcessChunk(ringOnly, 2, media))
                Assert.Null(media)
                Assert.True(listener.HoldsEpoch)
                Assert.Equal(7UI, listener.Epoch)
                Assert.Equal("audio/aac", listener.MediaType)
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(chunkA, 1, media))
                Assert.True(SameBytes(media1, media))
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(chunkB, -1, media))
                Assert.True(SameBytes(media2, media))
            End Using
            Using other As New ExchangeRelayListener("bc-1", gk2)
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.KeyRing, other.ProcessChunk(ringOnly, 2, media))
            End Using
            Using stranger As New ExchangeRelayListener("bc-1", outsider)
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, stranger.ProcessChunk(ringOnly, 2, media))
                Assert.Equal(ExchangeRelayFailure.NoRingEntry, stranger.LastFailure)
                Assert.Equal(ExchangeRelayOutcome.NeedReconnect, stranger.ProcessChunk(chunkA, 1, media))
            End Using

            ' epoch 8 drops gk2 and carries a new EK; a listener that opened epoch 7 then holds epoch 8 only
            Using nextEpoch As New ExchangeRelaySender("bc-1", "audio/aac", 8UI, ExchangeRelayRecords.NewKey(), New List(Of Byte()) From {gk1})
                Dim ring8 As Byte() = nextEpoch.BuildHeaderChunk(Nothing)
                Dim chunk8 As Byte() = nextEpoch.SealMedia(media1, 1)
                Using listener As New ExchangeRelayListener("bc-1", gk1)
                    Dim media As Byte() = Nothing
                    listener.ProcessChunk(ringOnly, 2, media)
                    Assert.Equal(ExchangeRelayOutcome.KeyRing, listener.ProcessChunk(ring8, 2, media))
                    Assert.Equal(8UI, listener.Epoch)
                    Assert.Equal(ExchangeRelayOutcome.NeedReconnect, listener.ProcessChunk(chunkA, 1, media))
                    Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(chunk8, 1, media))
                End Using
                Using dropped As New ExchangeRelayListener("bc-1", gk2)
                    Dim media As Byte() = Nothing
                    dropped.ProcessChunk(ringOnly, 2, media)
                    Assert.Equal(ExchangeRelayOutcome.SealedFailed, dropped.ProcessChunk(ring8, 2, media))
                    Assert.Equal(ExchangeRelayFailure.NoRingEntry, dropped.LastFailure)
                    Assert.Equal(7UI, dropped.Epoch)        ' still the old epoch: the dropped key learns nothing of the new one
                    Assert.Equal(ExchangeRelayOutcome.NeedReconnect, dropped.ProcessChunk(chunk8, 1, media))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_damaged_record_never_plays_and_unknown_records_are_skipped()
        Dim gk As Byte() = ExchangeRelayRecords.NewKey()
        Dim media1 As Byte() = ClearValue(500)
        Using sender As New ExchangeRelaySender("bc-2", "video/mp2t", 1UI, ExchangeRelayRecords.NewKey(), New List(Of Byte()) From {gk})
            Dim header As Byte() = sender.BuildHeaderChunk(ClearValue(188))
            Dim chunk As Byte() = sender.SealMedia(media1, 1)
            Using listener As New ExchangeRelayListener("bc-2", gk)
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(header, 2, media))
                Assert.True(SameBytes(ClearValue(188), media))

                ' a bit flipped in the sealed media
                Dim flipped As Byte() = CType(chunk.Clone(), Byte())
                flipped(flipped.Length - 1) = CByte(flipped(flipped.Length - 1) Xor 1)
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(flipped, 1, media))
                Assert.Equal(ExchangeRelayFailure.MediaTagFailed, listener.LastFailure)
                Assert.Null(media)
                ' another epoch number in the record
                Dim epoch As Byte() = CType(chunk.Clone(), Byte())
                epoch(8) = CByte(epoch(8) Xor 1)
                Assert.Equal(ExchangeRelayOutcome.NeedReconnect, listener.ProcessChunk(epoch, 1, media))
                ' a payload that ends inside a record
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(Slice(chunk, 0, chunk.Length - 1), 1, media))
                Assert.Equal(ExchangeRelayFailure.MalformedRecord, listener.LastFailure)
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(Slice(chunk, 0, 3), 1, media))
                ' a media record too short to hold an epoch, a nonce and a tag
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(ExchangeRelayRecords.Frame(ExchangeRelayRecords.TypeMedia, New Byte(20) {}), 1, media))
                Assert.Equal(ExchangeRelayFailure.MalformedRecord, listener.LastFailure)
                ' a ring that is no ring
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, listener.ProcessChunk(ExchangeRelayRecords.Frame(ExchangeRelayRecords.TypeKeyRing, New Byte(5) {}), 2, media))
                Assert.Equal(ExchangeRelayFailure.MalformedRecord, listener.LastFailure)

                ' a record of a type nobody defined is skipped, alone or beside a media record
                Dim unknown As Byte() = ExchangeRelayRecords.Frame(&H99, New Byte() {1, 2, 3})
                Assert.Equal(ExchangeRelayOutcome.Skipped, listener.ProcessChunk(unknown, 1, media))
                Assert.Null(media)
                Assert.Equal(ExchangeRelayOutcome.Media, listener.ProcessChunk(Concat(unknown, chunk, unknown), 1, media))
                Assert.True(SameBytes(media1, media))
                Dim unknownRecord As ExchangeRelayRecord = RecordsOf(unknown)(0)
                Assert.Equal(ExchangeRelayOutcome.Skipped, listener.Process(unknownRecord, 1, media))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_media_record_is_bound_to_its_broadcast_and_epoch_and_the_chunk_bound_is_held()
        Dim gk As Byte() = ExchangeRelayRecords.NewKey()
        Using sender As New ExchangeRelaySender("bc-3", "audio/aac", 1UI, ExchangeRelayRecords.NewKey(), New List(Of Byte()) From {gk})
            Dim header As Byte() = sender.BuildHeaderChunk(Nothing)
            ' the largest media a record can hold under the 1 MiB chunk bound: len = 1 + 4 + 12 + media + 16
            Dim biggest As Byte() = sender.SealMedia(New Byte(1048575 - 33 - 1) {}, 0)
            Assert.Equal(4 + 1048575, biggest.Length)
            Assert.Throws(Of ArgumentException)(Sub() sender.SealMedia(New Byte(1048575 - 33) {}, 0))
            Assert.Throws(Of ArgumentException)(Sub() sender.SealMedia(New Byte() {1}, CByte(3)))
            Assert.Throws(Of ArgumentException)(Sub() sender.BuildHeaderChunk(New Byte(1048575) {}))

            Using right As New ExchangeRelayListener("bc-3", gk)
                Dim media As Byte() = Nothing
                right.ProcessChunk(header, 2, media)
                Assert.Equal(ExchangeRelayOutcome.Media, right.ProcessChunk(biggest, 0, media))
                Assert.Equal(1048575 - 33, media.Length)
            End Using
            ' the same bytes replayed into another broadcast
            Using wrong As New ExchangeRelayListener("bc-4", gk)
                Dim media As Byte() = Nothing
                Assert.Equal(ExchangeRelayOutcome.SealedFailed, wrong.ProcessChunk(header, 2, media))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_key_ring_carries_one_entry_per_group_key_at_most_sixty_four_and_parses_only_whole()
        Dim ek As Byte() = ExchangeRelayRecords.NewKey()
        Dim keys As New List(Of Byte())
        For i As Integer = 0 To 63
            keys.Add(ExchangeRelayRecords.NewKey())
        Next
        Dim body As Byte() = ExchangeRelayRing.BuildBody("bc-5", 3UI, "audio/aac", ek, keys)
        Dim ring As ExchangeRelayRing = Nothing
        Assert.True(ExchangeRelayRing.TryParse(body, ring))
        Assert.Equal(64, ring.Entries.Count)
        Assert.Equal(3UI, ring.Epoch)
        Dim opened As Byte() = Nothing
        Assert.Equal(ExchangeRelayRingOpen.Opened, ring.TryOpen("bc-5", keys(63), opened))
        Assert.True(SameBytes(ek, opened))
        Assert.Equal(ExchangeRelayRingOpen.NoEntry, ring.TryOpen("bc-5", ExchangeRelayRecords.NewKey(), opened))
        Assert.Null(opened)
        Assert.Equal(ExchangeRelayRingOpen.TagFailed, ring.TryOpen("bc-6", keys(0), opened))

        keys.Add(ExchangeRelayRecords.NewKey())
        Dim tooMany As ArgumentException = Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRing.BuildBody("bc-5", 3UI, "audio/aac", ek, keys))
        Assert.DoesNotContain(E2eVectors.HexOf(ek), tooMany.Message)
        Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRing.BuildBody("bc-5", 3UI, "audio/aac", New Byte(30) {}, New List(Of Byte()) From {keys(0)}))
        Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRing.BuildBody("bc-5", 3UI, "audio/aac", ek, New List(Of Byte())))
        Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRing.BuildBody("bc-5", 3UI, "", ek, New List(Of Byte()) From {keys(0)}))
        Assert.Throws(Of ArgumentException)(Sub() ExchangeRelayRing.BuildBody("", 3UI, "audio/aac", ek, New List(Of Byte()) From {keys(0)}))

        ' a group key listed twice is written once
        Dim twice As Byte() = ExchangeRelayRing.BuildBody("bc-5", 1UI, "audio/aac", ek, New List(Of Byte()) From {keys(0), keys(0), keys(1)})
        Dim dedup As ExchangeRelayRing = Nothing
        Assert.True(ExchangeRelayRing.TryParse(twice, dedup))
        Assert.Equal(2, dedup.Entries.Count)

        ' the parser takes a whole ring and nothing else
        Dim good As Byte() = ExchangeRelayRing.BuildBody("bc-5", 1UI, "audio/aac", ek, New List(Of Byte()) From {keys(0)})
        Dim parsed As ExchangeRelayRing = Nothing
        Assert.True(ExchangeRelayRing.TryParse(good, parsed))
        Assert.False(ExchangeRelayRing.TryParse(Concat(good, New Byte() {0}), parsed))
        Assert.False(ExchangeRelayRing.TryParse(Slice(good, 0, good.Length - 1), parsed))
        Assert.False(ExchangeRelayRing.TryParse(Nothing, parsed))
        Assert.False(ExchangeRelayRing.TryParse(New Byte(4) {}, parsed))
        Dim countAt As Integer = 5 + "audio/aac".Length
        Dim zero As Byte() = CType(good.Clone(), Byte())
        zero(countAt) = 0
        Assert.False(ExchangeRelayRing.TryParse(zero, parsed))
        Dim sixtyFive As Byte() = CType(good.Clone(), Byte())
        sixtyFive(countAt) = 65
        Assert.False(ExchangeRelayRing.TryParse(sixtyFive, parsed))
        Dim noType As Byte() = CType(good.Clone(), Byte())
        noType(4) = 0
        Assert.False(ExchangeRelayRing.TryParse(noType, parsed))
        Dim control As Byte() = CType(good.Clone(), Byte())
        control(5) = 7
        Assert.False(ExchangeRelayRing.TryParse(control, parsed))
    End Sub

    <Fact>
    Public Sub A_sender_gives_a_fresh_random_nonce_to_every_record_and_wipes_its_key_on_dispose()
        Dim gk As Byte() = ExchangeRelayRecords.NewKey()
        Dim sender As New ExchangeRelaySender("bc-7", "audio/aac", 1UI, ExchangeRelayRecords.NewKey(), New List(Of Byte()) From {gk})
        Dim a As Byte() = sender.SealMedia(New Byte() {1, 2, 3}, 0)
        Dim b As Byte() = sender.SealMedia(New Byte() {1, 2, 3}, 0)
        Assert.False(SameBytes(a, b))
        Assert.False(SameBytes(Slice(a, 9, 12), Slice(b, 9, 12)))      ' len(4) type(1) epoch(4), then the nonce
        sender.Dispose()
    End Sub

    ' ---------------------------------------------------------------- relay: the streaming reader

    Private Shared Function AllPayloads(v As RelayVector) As Byte()
        Return Concat(v.Chunks.Select(Function(c) c.Payload).ToArray())
    End Function

    <Fact>
    Public Sub The_reader_gives_the_same_records_whether_the_bytes_come_whole_one_by_one_or_in_odd_pieces()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Dim all As Byte() = AllPayloads(v)
            Dim expectedTypes As Byte() = {&H4B, &H4D, &H4D, &H4D, &H4B, &H4D, &H4D, &H4D}
            Dim whole As List(Of ExchangeRelayRecord) = RecordsOf(all)
            Assert.Equal(expectedTypes, whole.Select(Function(r) r.Type).ToArray())

            ' byte by byte
            Dim reader As New ExchangeRelayRecordReader()
            Dim oneByOne As New List(Of ExchangeRelayRecord)
            For Each b As Byte In all
                reader.Append(New Byte() {b})
                Dim r As ExchangeRelayRecord = Nothing
                While reader.TryRead(r)
                    oneByOne.Add(r)
                End While
            Next
            Assert.Equal(whole.Count, oneByOne.Count)
            For i As Integer = 0 To whole.Count - 1
                Assert.Equal(whole(i).Type, oneByOne(i).Type)
                Assert.Equal(E2eVectors.HexOf(whole(i).Body), E2eVectors.HexOf(oneByOne(i).Body))
            Next
            Assert.Equal(0, reader.Buffered)

            ' pulled from a stream that returns a few bytes at a time
            For Each chunks As Integer() In OddSourceChunks
                Using src As New SealedTestChunkyStream(all, chunks)
                    Dim pull As New ExchangeRelayRecordReader()
                    Dim count As Integer = 0
                    While True
                        Dim res As ExchangeRelayReadResult = pull.ReadNext(src)
                        If res.Status <> ExchangeRelayReadStatus.Record Then
                            Assert.Equal(ExchangeRelayReadStatus.EndOfStream, res.Status)
                            Exit While
                        End If
                        Assert.Equal(expectedTypes(count), res.Record.Type)
                        count += 1
                    End While
                    Assert.Equal(8, count)
                End Using
            Next
        End Using
    End Sub

    <Fact>
    Public Sub The_reader_reads_asynchronously_and_stops_at_the_cancellation()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Dim all As Byte() = AllPayloads(v)
            Using src As New SealedTestChunkyStream(all, New Integer() {5, 1, 4000})
                Dim pull As New ExchangeRelayRecordReader()
                Dim count As Integer = 0
                While True
                    Dim res As ExchangeRelayReadResult = pull.ReadNextAsync(src, CancellationToken.None).GetAwaiter().GetResult()
                    If res.Status <> ExchangeRelayReadStatus.Record Then
                        Assert.Equal(ExchangeRelayReadStatus.EndOfStream, res.Status)
                        Exit While
                    End If
                    count += 1
                End While
                Assert.Equal(8, count)
            End Using
            Using src As New SealedTestChunkyStream(all, New Integer() {5})
                Dim cancelled As New CancellationTokenSource()
                cancelled.Cancel()
                Dim cancelReader As New ExchangeRelayRecordReader()
                Assert.ThrowsAny(Of OperationCanceledException)(Sub() cancelReader.ReadNextAsync(src, cancelled.Token).GetAwaiter().GetResult())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_stream_that_ends_inside_a_record_is_truncated_and_a_bad_length_is_corrupt()
        Using doc As JsonDocument = E2eVectors.Load()
            Dim v As RelayVector = LoadRelay(doc)
            Dim all As Byte() = AllPayloads(v)
            For Each cutBy As Integer In {1, 3, 40}
                Using src As New MemoryStream(Slice(all, 0, all.Length - cutBy), False)
                    Dim pull As New ExchangeRelayRecordReader()
                    Dim last As ExchangeRelayReadResult = Nothing
                    Dim count As Integer = 0
                    While True
                        last = pull.ReadNext(src)
                        If last.Status <> ExchangeRelayReadStatus.Record Then Exit While
                        count += 1
                    End While
                    Assert.Equal(ExchangeRelayReadStatus.Truncated, last.Status)
                    Assert.Equal(7, count)
                End Using
            Next
            ' the stream ends inside the length prefix
            Using src As New MemoryStream(New Byte() {0, 0}, False)
                Assert.Equal(ExchangeRelayReadStatus.Truncated, New ExchangeRelayRecordReader().ReadNext(src).Status)
            End Using
            Using src As New MemoryStream(New Byte() {}, False)
                Assert.Equal(ExchangeRelayReadStatus.EndOfStream, New ExchangeRelayRecordReader().ReadNext(src).Status)
            End Using
        End Using

        ' len 0 and len above the bound are no records; the bound itself is one that only needs more bytes
        Dim zero As New ExchangeRelayRecordReader()
        zero.Append(New Byte() {0, 0, 0, 0, 1, 2, 3})
        Dim r As ExchangeRelayRecord = Nothing
        Assert.False(zero.TryRead(r))
        Assert.True(zero.IsCorrupt)
        Dim over As New ExchangeRelayRecordReader()
        over.Append(New Byte() {0, &H10, 0, 0, 1})
        Assert.False(over.TryRead(r))
        Assert.True(over.IsCorrupt)
        Using src As New MemoryStream(New Byte() {0, &HFF, &HFF, &HFF, &H4D}, False)
            Assert.Equal(ExchangeRelayReadStatus.Corrupt, New ExchangeRelayRecordReader().ReadNext(src).Status)
        End Using
        Dim atBound As New ExchangeRelayRecordReader()
        atBound.Append(New Byte() {0, &HF, &HFF, &HFF, &H4D})
        Assert.False(atBound.TryRead(r))
        Assert.False(atBound.IsCorrupt)
    End Sub

End Class
#End If
