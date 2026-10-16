#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

' Spec 234 T-1, DEVICE-EXCHANGE section 27 item DN. The sealed clipboard value: the box (context `clipboard`) carries the
' content key CK and the payload members of the clear value; the BYTES are cut into segments of 65536 and each segment is
' sealed with AES-256-GCM under the value key (HKDF-SHA256 of CK, empty salt, info `fmsx-e2e-v1 clipboard`, 32 bytes). The
' nonce of a segment is 11 bytes of its index i (unsigned big-endian) and then 0x01 for the last segment, 0x00 otherwise; there
' is no associated data; the sealed size is the clear size plus 16 bytes per segment. A value can be gigabytes, so both
' directions are STREAMS that hold two segments at most (one to look ahead: only the end of the source says which segment is
' the last).
'
' The receiver opens every segment (a tag is checked BEFORE a byte of the segment is handed on), then compares the clear size
' and SHA-256 with the box. A failed tag, a missing last segment or a difference means "discard everything": the caller wrote
' the clear bytes to a place it can drop (a temp file, a memory stream) and drops it, and places nothing. Nothing here logs;
' no key or byte of a value reaches a message.

Friend Enum ExchangeSealedFailure
    None = 0
    ''' <summary>A segment whose tag failed: an altered, reordered or foreign segment, or a wrong key.</summary>
    TagFailed = 1
    ''' <summary>The sealed bytes end before the segment that is marked last: nothing at all, or the tail cut off.</summary>
    MissingLastSegment = 2
    ''' <summary>A final segment too short to hold a tag and one byte.</summary>
    Malformed = 3
End Enum

''' <summary>What a read of a sealed stream throws. The message is fixed: it never carries a key, a byte or a name.</summary>
Friend NotInheritable Class ExchangeSealedFailedException
    Inherits Exception

    Public ReadOnly Property Failure As ExchangeSealedFailure

    Public Sub New(failure As ExchangeSealedFailure)
        MyBase.New("sealed content could not be opened")
        Me.Failure = failure
    End Sub

End Class

Friend Enum ExchangeClipboardOpenOutcome
    Opened = 0
    TagFailed = 1
    MissingLastSegment = 2
    Malformed = 3
    ''' <summary>Every tag held but the clear size is not the one the box states.</summary>
    SizeMismatch = 4
    ''' <summary>Every tag held but the clear SHA-256 is not the one the box states.</summary>
    HashMismatch = 5
End Enum

Friend NotInheritable Class ExchangeClipboardSealResult
    ''' <summary>0 for an empty source: nothing was sealed, and the sender refuses a value of no bytes before it gets here.</summary>
    Public ReadOnly ClearSize As Long
    Public ReadOnly ClearSha256Hex As String
    Public ReadOnly SealedSize As Long
    Public ReadOnly SealedSha256Hex As String

    Public Sub New(clearSize As Long, clearSha256Hex As String, sealedSize As Long, sealedSha256Hex As String)
        Me.ClearSize = clearSize
        Me.ClearSha256Hex = clearSha256Hex
        Me.SealedSize = sealedSize
        Me.SealedSha256Hex = sealedSha256Hex
    End Sub
End Class

''' <summary>The constants and the one-shot forms of item DN.</summary>
Friend NotInheritable Class ExchangeClipboardSeal

    Private Sub New()
    End Sub

    Public Const SegmentSize As Integer = 65536
    Public Const TagLength As Integer = ExchangeAead.TagLength
    Public Const SealedSegmentSize As Integer = SegmentSize + TagLength
    Public Const ValueKeyLength As Integer = 32

    Private Shared ReadOnly ValueKeyInfo As Byte() = Encoding.ASCII.GetBytes("fmsx-e2e-v1 clipboard")

    ''' <summary>The value key: HKDF-SHA256 with CK as the input key, an empty salt, `fmsx-e2e-v1 clipboard`, 32 bytes.</summary>
    Public Shared Function ValueKey(contentKey As Byte()) As Byte()
        If contentKey Is Nothing OrElse contentKey.Length = 0 Then Throw New ArgumentException("contentKey", NameOf(contentKey))
        Return HKDF.DeriveKey(HashAlgorithmName.SHA256, contentKey, ValueKeyLength, Array.Empty(Of Byte)(), ValueKeyInfo)
    End Function

    ''' <summary>The 12-byte nonce of segment <paramref name="index"/>: 11 bytes of the index, unsigned big-endian, then 0x01
    ''' for the last segment and 0x00 otherwise.</summary>
    Public Shared Function SegmentNonce(index As Long, last As Boolean) As Byte()
        If index < 0 Then Throw New ArgumentOutOfRangeException(NameOf(index))
        Dim n(ExchangeAead.NonceLength - 1) As Byte
        For i As Integer = 10 To 3 Step -1
            n(i) = CByte(index And &HFFL)
            index >>= 8
        Next
        n(11) = If(last, CByte(1), CByte(0))
        Return n
    End Function

    ''' <summary>The number of segments of a clear value (0 for 0 bytes).</summary>
    Public Shared Function SegmentCount(clearSize As Long) As Long
        If clearSize <= 0 Then Return 0
        Return (clearSize + SegmentSize - 1) \ SegmentSize
    End Function

    ''' <summary>The sealed size of a clear value: the clear size plus 16 bytes per segment (0 for 0 bytes).</summary>
    Public Shared Function SealedSize(clearSize As Long) As Long
        If clearSize <= 0 Then Return 0
        Return clearSize + TagLength * SegmentCount(clearSize)
    End Function

    ''' <summary>Seals a source to a destination, segment by segment, and reports the sizes and hashes.</summary>
    Public Shared Function Seal(clear As Stream, destination As Stream, valueKey As Byte()) As ExchangeClipboardSealResult
        If destination Is Nothing Then Throw New ArgumentNullException(NameOf(destination))
        Using s As New ExchangeClipboardSealingStream(clear, valueKey, True)
            s.CopyTo(destination)
            Return New ExchangeClipboardSealResult(s.ClearSize, s.ClearSha256Hex, s.SealedSize, s.SealedSha256Hex)
        End Using
    End Function

    ''' <summary>Opens a sealed source into a destination and checks the clear size and SHA-256 the box states. On anything but
    ''' Opened the destination may hold the segments that opened before the failure: the caller drops it (a temp file) and places
    ''' nothing.</summary>
    Public Shared Function Open(sealedSource As Stream, destination As Stream, valueKey As Byte(), expectedClearSize As Long, expectedClearSha256Hex As String) As ExchangeClipboardOpenOutcome
        If destination Is Nothing Then Throw New ArgumentNullException(NameOf(destination))
        Using o As New ExchangeClipboardOpeningStream(sealedSource, valueKey, True)
            Try
                o.CopyTo(destination)
            Catch ex As ExchangeSealedFailedException
                Select Case ex.Failure
                    Case ExchangeSealedFailure.MissingLastSegment
                        Return ExchangeClipboardOpenOutcome.MissingLastSegment
                    Case ExchangeSealedFailure.Malformed
                        Return ExchangeClipboardOpenOutcome.Malformed
                    Case Else
                        Return ExchangeClipboardOpenOutcome.TagFailed
                End Select
            End Try
            If o.ClearSize <> expectedClearSize Then Return ExchangeClipboardOpenOutcome.SizeMismatch
            If Not String.Equals(o.ClearSha256Hex, If(expectedClearSha256Hex, "").ToLowerInvariant(), StringComparison.Ordinal) Then Return ExchangeClipboardOpenOutcome.HashMismatch
            Return ExchangeClipboardOpenOutcome.Opened
        End Using
    End Function

    ''' <summary>A small value (clipboard text) sealed in memory.</summary>
    Public Shared Function SealBytes(clear As Byte(), valueKey As Byte()) As Byte()
        Using source As New MemoryStream(clear, False)
            Using target As New MemoryStream()
                Seal(source, target, valueKey)
                Return target.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>A small value opened in memory; Nothing on any failure, never the segments that opened before it.</summary>
    Public Shared Function OpenBytes(sealedBytes As Byte(), valueKey As Byte(), expectedClearSize As Long, expectedClearSha256Hex As String) As Byte()
        Using source As New MemoryStream(sealedBytes, False)
            Using target As New MemoryStream()
                If Open(source, target, valueKey, expectedClearSize, expectedClearSha256Hex) <> ExchangeClipboardOpenOutcome.Opened Then
                    Dim leftover As Byte() = target.GetBuffer()
                    Array.Clear(leftover, 0, leftover.Length)
                    Return Nothing
                End If
                Return target.ToArray()
            End Using
        End Using
    End Function

    Private Shared ReadOnly NoAad As Byte() = Array.Empty(Of Byte)()

    ''' <summary>Seals <paramref name="count"/> bytes of <paramref name="source"/> into <paramref name="target"/> as ciphertext and
    ''' tag (count + 16 bytes), no associated data.</summary>
    Friend Shared Sub SealSegment(aes As AesGcm, nonce As Byte(), source As Byte(), count As Integer, target As Byte())
        Dim plain As Byte() = source
        If count <> source.Length Then
            plain = New Byte(count - 1) {}
            Buffer.BlockCopy(source, 0, plain, 0, count)
        End If
        Dim ct(count - 1) As Byte
        Dim tag(TagLength - 1) As Byte
        aes.Encrypt(nonce, plain, ct, tag, NoAad)
        Buffer.BlockCopy(ct, 0, target, 0, count)
        Buffer.BlockCopy(tag, 0, target, count, TagLength)
        Array.Clear(ct, 0, ct.Length)
        If Not Object.ReferenceEquals(plain, source) Then Array.Clear(plain, 0, plain.Length)
    End Sub

    ''' <summary>Opens <paramref name="sealedCount"/> bytes (ciphertext and tag) of <paramref name="sealedSegment"/> into the
    ''' start of <paramref name="target"/>; False for a failed tag, and then nothing of the segment is left in the target.</summary>
    Friend Shared Function TryOpenSegment(aes As AesGcm, nonce As Byte(), sealedSegment As Byte(), sealedCount As Integer, target As Byte()) As Boolean
        Dim n As Integer = sealedCount - TagLength
        Dim ct(n - 1) As Byte
        Dim tag(TagLength - 1) As Byte
        Dim pt(n - 1) As Byte
        Buffer.BlockCopy(sealedSegment, 0, ct, 0, n)
        Buffer.BlockCopy(sealedSegment, n, tag, 0, TagLength)
        Try
            aes.Decrypt(nonce, ct, tag, pt, NoAad)
        Catch ex As CryptographicException
            Array.Clear(pt, 0, pt.Length)
            Return False
        End Try
        Buffer.BlockCopy(pt, 0, target, 0, n)
        Array.Clear(pt, 0, pt.Length)
        Return True
    End Function

    Friend Shared Function Hex(hash As Byte()) As String
        Return Convert.ToHexString(hash).ToLowerInvariant()
    End Function

    ''' <summary>Fills a buffer from a stream until it is full or the stream ends; the number of bytes.</summary>
    Friend Shared Function Fill(source As Stream, buffer As Byte(), size As Integer) As Integer
        Dim got As Integer = 0
        While got < size
            Dim n As Integer = source.Read(buffer, got, size - got)
            If n <= 0 Then Exit While
            got += n
        End While
        Return got
    End Function

End Class

''' <summary>A read-only stream that yields the SEALED bytes of a clear source. Sizes and hashes are known when it has been read
''' to its end. A source of no bytes yields nothing.</summary>
Friend NotInheritable Class ExchangeClipboardSealingStream
    Inherits Stream

    Private ReadOnly _source As Stream
    Private ReadOnly _leaveOpen As Boolean
    Private ReadOnly _aes As AesGcm
    Private ReadOnly _clearHash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    Private ReadOnly _sealedHash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    Private _a As Byte() = New Byte(ExchangeClipboardSeal.SegmentSize - 1) {}
    Private _b As Byte() = New Byte(ExchangeClipboardSeal.SegmentSize - 1) {}
    Private ReadOnly _out As Byte() = New Byte(ExchangeClipboardSeal.SealedSegmentSize - 1) {}
    Private _lenA As Integer
    Private _primed As Boolean
    Private _done As Boolean
    Private _disposed As Boolean
    Private _index As Long
    Private _outPos As Integer
    Private _outLen As Integer
    Private _clearSize As Long
    Private _sealedSize As Long
    Private _clearHex As String
    Private _sealedHex As String

    Public Sub New(clear As Stream, valueKey As Byte(), Optional leaveOpen As Boolean = False)
        If clear Is Nothing Then Throw New ArgumentNullException(NameOf(clear))
        If valueKey Is Nothing OrElse valueKey.Length <> ExchangeClipboardSeal.ValueKeyLength Then Throw New ArgumentException("valueKey", NameOf(valueKey))
        _source = clear
        _leaveOpen = leaveOpen
        _aes = New AesGcm(valueKey, ExchangeClipboardSeal.TagLength)
    End Sub

    ''' <summary>Clear bytes consumed so far.</summary>
    Public ReadOnly Property ClearSize As Long
        Get
            Return _clearSize
        End Get
    End Property

    ''' <summary>Sealed bytes produced so far.</summary>
    Public ReadOnly Property SealedSize As Long
        Get
            Return _sealedSize
        End Get
    End Property

    ''' <summary>True once the source was read to its end AND every sealed byte was handed on.</summary>
    Public ReadOnly Property IsComplete As Boolean
        Get
            Return _done AndAlso _outPos >= _outLen
        End Get
    End Property

    ''' <summary>The SHA-256 of the clear bytes (lowercase hex), or Nothing before IsComplete.</summary>
    Public ReadOnly Property ClearSha256Hex As String
        Get
            Return If(IsComplete, _clearHex, Nothing)
        End Get
    End Property

    ''' <summary>The SHA-256 of the sealed bytes (lowercase hex), or Nothing before IsComplete.</summary>
    Public ReadOnly Property SealedSha256Hex As String
        Get
            Return If(IsComplete, _sealedHex, Nothing)
        End Get
    End Property

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If offset < 0 OrElse count < 0 OrElse offset + count > buffer.Length Then Throw New ArgumentOutOfRangeException(NameOf(count))
        If _disposed Then Throw New ObjectDisposedException(NameOf(ExchangeClipboardSealingStream))
        If count = 0 Then Return 0
        While _outPos >= _outLen
            If _done Then Return 0
            NextSegment()
        End While
        Dim n As Integer = Math.Min(count, _outLen - _outPos)
        System.Buffer.BlockCopy(_out, _outPos, buffer, offset, n)
        _outPos += n
        Return n
    End Function

    Private Sub NextSegment()
        If Not _primed Then
            _lenA = ExchangeClipboardSeal.Fill(_source, _a, ExchangeClipboardSeal.SegmentSize)
            _primed = True
        End If
        _outPos = 0
        _outLen = 0
        If _lenA = 0 Then
            Finish()
            Return
        End If
        Dim lenB As Integer = 0
        Dim last As Boolean
        If _lenA < ExchangeClipboardSeal.SegmentSize Then
            last = True
        Else
            lenB = ExchangeClipboardSeal.Fill(_source, _b, ExchangeClipboardSeal.SegmentSize)
            last = (lenB = 0)
        End If
        Dim nonce As Byte() = ExchangeClipboardSeal.SegmentNonce(_index, last)
        ExchangeClipboardSeal.SealSegment(_aes, nonce, _a, _lenA, _out)
        _outLen = _lenA + ExchangeClipboardSeal.TagLength
        _clearHash.AppendData(_a, 0, _lenA)
        _sealedHash.AppendData(_out, 0, _outLen)
        _clearSize += _lenA
        _sealedSize += _outLen
        _index += 1
        If last Then
            Finish()
        Else
            Dim t As Byte() = _a
            _a = _b
            _b = t
            _lenA = lenB
        End If
    End Sub

    Private Sub Finish()
        _done = True
        _clearHex = ExchangeClipboardSeal.Hex(_clearHash.GetHashAndReset())
        _sealedHex = ExchangeClipboardSeal.Hex(_sealedHash.GetHashAndReset())
        Array.Clear(_a, 0, _a.Length)
        Array.Clear(_b, 0, _b.Length)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            _aes.Dispose()
            _clearHash.Dispose()
            _sealedHash.Dispose()
            Array.Clear(_a, 0, _a.Length)
            Array.Clear(_b, 0, _b.Length)
            If Not _leaveOpen Then _source.Dispose()
        End If
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

''' <summary>A read-only stream that yields the CLEAR bytes of a sealed source. A segment is handed on only after its tag held;
''' a failure throws <see cref="ExchangeSealedFailedException"/> from the read that met it and every later read. The clear size and
''' SHA-256 are known when it has been read to its end.</summary>
Friend NotInheritable Class ExchangeClipboardOpeningStream
    Inherits Stream

    Private ReadOnly _source As Stream
    Private ReadOnly _leaveOpen As Boolean
    Private ReadOnly _aes As AesGcm
    Private ReadOnly _clearHash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
    Private _a As Byte() = New Byte(ExchangeClipboardSeal.SealedSegmentSize - 1) {}
    Private _b As Byte() = New Byte(ExchangeClipboardSeal.SealedSegmentSize - 1) {}
    Private ReadOnly _plain As Byte() = New Byte(ExchangeClipboardSeal.SegmentSize - 1) {}
    Private _lenA As Integer
    Private _primed As Boolean
    Private _done As Boolean
    Private _disposed As Boolean
    Private _failure As ExchangeSealedFailure
    Private _index As Long
    Private _plainPos As Integer
    Private _plainLen As Integer
    Private _clearSize As Long
    Private _clearHex As String

    Public Sub New(sealedSource As Stream, valueKey As Byte(), Optional leaveOpen As Boolean = False)
        If sealedSource Is Nothing Then Throw New ArgumentNullException(NameOf(sealedSource))
        If valueKey Is Nothing OrElse valueKey.Length <> ExchangeClipboardSeal.ValueKeyLength Then Throw New ArgumentException("valueKey", NameOf(valueKey))
        _source = sealedSource
        _leaveOpen = leaveOpen
        _aes = New AesGcm(valueKey, ExchangeClipboardSeal.TagLength)
    End Sub

    ''' <summary>Clear bytes opened so far.</summary>
    Public ReadOnly Property ClearSize As Long
        Get
            Return _clearSize
        End Get
    End Property

    ''' <summary>True once the last segment opened AND every clear byte was handed on.</summary>
    Public ReadOnly Property IsComplete As Boolean
        Get
            Return _done AndAlso _failure = ExchangeSealedFailure.None AndAlso _plainPos >= _plainLen
        End Get
    End Property

    ''' <summary>The SHA-256 of the clear bytes (lowercase hex), or Nothing before IsComplete.</summary>
    Public ReadOnly Property ClearSha256Hex As String
        Get
            Return If(IsComplete, _clearHex, Nothing)
        End Get
    End Property

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        If buffer Is Nothing Then Throw New ArgumentNullException(NameOf(buffer))
        If offset < 0 OrElse count < 0 OrElse offset + count > buffer.Length Then Throw New ArgumentOutOfRangeException(NameOf(count))
        If _disposed Then Throw New ObjectDisposedException(NameOf(ExchangeClipboardOpeningStream))
        If _failure <> ExchangeSealedFailure.None Then Throw New ExchangeSealedFailedException(_failure)
        If count = 0 Then Return 0
        While _plainPos >= _plainLen
            If _done Then Return 0
            NextSegment()
        End While
        Dim n As Integer = Math.Min(count, _plainLen - _plainPos)
        System.Buffer.BlockCopy(_plain, _plainPos, buffer, offset, n)
        _plainPos += n
        Return n
    End Function

    Private Sub NextSegment()
        If Not _primed Then
            _lenA = ExchangeClipboardSeal.Fill(_source, _a, ExchangeClipboardSeal.SealedSegmentSize)
            _primed = True
        End If
        _plainPos = 0
        _plainLen = 0
        If _lenA = 0 Then Fail(ExchangeSealedFailure.MissingLastSegment)
        Dim lenB As Integer = 0
        Dim last As Boolean
        If _lenA < ExchangeClipboardSeal.SealedSegmentSize Then
            last = True
        Else
            lenB = ExchangeClipboardSeal.Fill(_source, _b, ExchangeClipboardSeal.SealedSegmentSize)
            last = (lenB = 0)
        End If
        ' A segment holds at least one clear byte, so at least a tag and one more.
        If _lenA <= ExchangeClipboardSeal.TagLength Then Fail(ExchangeSealedFailure.Malformed)
        Dim clearLen As Integer = _lenA - ExchangeClipboardSeal.TagLength
        If Not TryOpen(ExchangeClipboardSeal.SegmentNonce(_index, last), clearLen) Then
            ' A full-size segment that opens as "not last" says the sealed bytes were cut short; the bytes are discarded either way.
            Dim cutShort As Boolean = last AndAlso _lenA = ExchangeClipboardSeal.SealedSegmentSize AndAlso TryOpen(ExchangeClipboardSeal.SegmentNonce(_index, False), clearLen)
            Array.Clear(_plain, 0, _plain.Length)
            Fail(If(cutShort, ExchangeSealedFailure.MissingLastSegment, ExchangeSealedFailure.TagFailed))
        End If
        _plainLen = clearLen
        _clearHash.AppendData(_plain, 0, clearLen)
        _clearSize += clearLen
        _index += 1
        If last Then
            _done = True
            _clearHex = ExchangeClipboardSeal.Hex(_clearHash.GetHashAndReset())
            Array.Clear(_a, 0, _a.Length)
            Array.Clear(_b, 0, _b.Length)
        Else
            Dim t As Byte() = _a
            _a = _b
            _b = t
            _lenA = lenB
        End If
    End Sub

    Private Function TryOpen(nonce As Byte(), clearLen As Integer) As Boolean
        Return ExchangeClipboardSeal.TryOpenSegment(_aes, nonce, _a, clearLen + ExchangeClipboardSeal.TagLength, _plain)
    End Function

    Private Sub Fail(failure As ExchangeSealedFailure)
        _failure = failure
        _done = True
        Array.Clear(_a, 0, _a.Length)
        Array.Clear(_b, 0, _b.Length)
        Throw New ExchangeSealedFailedException(failure)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not _disposed Then
            _disposed = True
            _aes.Dispose()
            _clearHash.Dispose()
            Array.Clear(_a, 0, _a.Length)
            Array.Clear(_b, 0, _b.Length)
            Array.Clear(_plain, 0, _plain.Length)
            If Not _leaveOpen Then _source.Dispose()
        End If
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

''' <summary>The members of the box's payload for a clipboard value (item DN): the kind, the type and the name of the CLEAR value
''' and its size and SHA-256 - so the server, which sees only the sealed bytes, learns none of them.</summary>
Friend NotInheritable Class ExchangeClipboardSealedPayload

    Public ReadOnly Kind As String
    ''' <summary>"" when the value has none (an image without one, a text the sender did not type).</summary>
    Public ReadOnly MimeType As String
    Public ReadOnly Name As String
    Public ReadOnly Size As Long
    ''' <summary>64 lowercase hex characters.</summary>
    Public ReadOnly Sha256 As String

    Private Sub New(kind As String, mimeType As String, name As String, size As Long, sha256 As String)
        Me.Kind = kind
        Me.MimeType = mimeType
        Me.Name = name
        Me.Size = size
        Me.Sha256 = sha256
    End Sub

    ''' <summary>The payload of a clear value; False when a member breaks the rules of item BW (a known kind, a size of at least 1,
    ''' a `type/subtype` and a name when present, a SHA-256 of 64 hex characters).</summary>
    Public Shared Function TryCreate(kind As String, mimeType As String, name As String, size As Long, sha256Hex As String, ByRef payload As ExchangeClipboardSealedPayload) As Boolean
        payload = Nothing
        If Not ExchangeClipboardKinds.IsKnown(kind) OrElse size < 1 Then Return False
        If Not String.IsNullOrEmpty(mimeType) AndAlso Not ExchangeClipboardRules.IsValidMime(mimeType) Then Return False
        If Not String.IsNullOrEmpty(name) AndAlso Not ExchangeClipboardRules.IsValidName(name) Then Return False
        If sha256Hex Is Nothing OrElse sha256Hex.Length <> 64 Then Return False
        Dim hex As String = sha256Hex.ToLowerInvariant()
        For Each c As Char In hex
            If Not ((c >= "0"c AndAlso c <= "9"c) OrElse (c >= "a"c AndAlso c <= "f"c)) Then Return False
        Next
        payload = New ExchangeClipboardSealedPayload(kind, If(mimeType, ""), If(name, ""), size, hex)
        Return True
    End Function

    ''' <summary>The UTF-8 bytes of the JSON object, members in the order of the contract: kind, mimeType, name, size, sha256 (the
    ''' two optional ones only when present). The caller wipes the array after sealing it.</summary>
    Public Function ToJsonBytes() As Byte()
        Dim w As New ExchangeJsonWriter()
        Try
            w.BeginObject()
            w.Name("kind")
            w.Text(Kind)
            If MimeType.Length > 0 Then
                w.Name("mimeType")
                w.Text(MimeType)
            End If
            If Name.Length > 0 Then
                w.Name("name")
                w.Text(Name)
            End If
            w.Name("size")
            w.Number(Size)
            w.Name("sha256")
            w.Text(Sha256)
            w.EndObject()
            Return w.ToArray()
        Finally
            w.Clear()
        End Try
    End Function

    ''' <summary>False when the bytes are not one JSON object with a valid `kind`, `size` and `sha256` (unknown members are ignored).</summary>
    Public Shared Function TryParse(json As Byte(), ByRef payload As ExchangeClipboardSealedPayload) As Boolean
        payload = Nothing
        If json Is Nothing OrElse json.Length = 0 Then Return False
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return False
                Dim kind As String = Nothing
                Dim mime As String = Nothing
                Dim name As String = Nothing
                Dim sha As String = Nothing
                Dim size As Long
                Dim e As JsonElement
                If Not OptionalString(root, "kind", kind) OrElse kind Is Nothing Then Return False
                If Not OptionalString(root, "mimeType", mime) OrElse Not OptionalString(root, "name", name) Then Return False
                If Not OptionalString(root, "sha256", sha) OrElse sha Is Nothing Then Return False
                If Not root.TryGetProperty("size", e) OrElse e.ValueKind <> JsonValueKind.Number OrElse Not e.TryGetInt64(size) Then Return False
                Dim safeName As String = If(name IsNot Nothing, ExchangeClipboardRules.ReceivedFileName(name, DateTime.UtcNow, mime), Nothing)
                Return TryCreate(kind, mime, safeName, size, sha, payload)
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    ''' <summary>The receiver's check against what it opened: the clear size and SHA-256 of the box.</summary>
    Public Function Matches(clearSize As Long, clearSha256Hex As String) As Boolean
        Return clearSize = Size AndAlso String.Equals(If(clearSha256Hex, "").ToLowerInvariant(), Sha256, StringComparison.Ordinal)
    End Function

    ''' <summary>The type name only: a name or a hash of a value is never written to a log (item CC).</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeClipboardSealedPayload"
    End Function

    ' A member that is absent is Nothing and fine; one that is present must be a string.
    Private Shared Function OptionalString(root As JsonElement, memberName As String, ByRef value As String) As Boolean
        value = Nothing
        Dim e As JsonElement
        If Not root.TryGetProperty(memberName, e) Then Return True
        If e.ValueKind <> JsonValueKind.String Then Return False
        value = e.GetString()
        Return True
    End Function

End Class
#End If
