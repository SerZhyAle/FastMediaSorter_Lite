#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

' Specification 277 section 5.1: virtual read-only paging of a text file too large to hold.
' The data after the byte-order mark is cut into windows of a fixed nominal size; each cut is
' moved back to the start of a line within SnapMarginBytes, else to a character boundary of the
' encoding, so no character and no CRLF pair is ever split between two windows. A cut depends
' only on its nominal offset and the bytes around it, so adjacent windows share their cut
' exactly: no gap, no overlap. The file is opened per read and never held, so a move or delete
' of the open document is not blocked. Not thread-safe: one pager per reader.
Public NotInheritable Class TextFilePager

    Public Const SnapMarginBytes As Integer = 4096
    Public Const MinWindowMb As Integer = 1
    Public Const MaxWindowMb As Integer = 16
    ' Smallest window the pager accepts: above the margin, so cuts stay strictly increasing.
    Public Const MinWindowBytes As Integer = 4 * SnapMarginBytes
    ' A run of double-byte text with no single-byte character this long keeps the nominal cut.
    Private Const DoubleByteSearchBytes As Integer = 65536

    Private ReadOnly _filePath As String
    Private ReadOnly _encoding As Encoding
    Private ReadOnly _unitBytes As Integer
    Private ReadOnly _bigEndian As Boolean
    Private ReadOnly _dataStart As Long
    Private ReadOnly _fileSize As Long
    Private ReadOnly _windowSize As Integer
    Private ReadOnly _totalWindows As Integer
    Private ReadOnly _cuts As New Dictionary(Of Integer, Long)()
    Private _currentWindowIndex As Integer

    ' encoding: the detected or chosen encoding; bomLength: bytes of its byte-order mark at the
    ' start of the file, skipped. windowBytes is clamped up to MinWindowBytes.
    Public Sub New(filePath As String, encoding As Encoding, bomLength As Integer, windowBytes As Integer)
        If String.IsNullOrEmpty(filePath) Then Throw New ArgumentNullException("filePath")
        If encoding Is Nothing Then Throw New ArgumentNullException("encoding")
        _filePath = filePath
        _encoding = If(TextEncodingDetector.CreateEncoding(encoding.CodePage, False), encoding)
        Select Case encoding.CodePage
            Case 1200
                _unitBytes = 2
            Case 1201
                _unitBytes = 2
                _bigEndian = True
            Case 12000
                _unitBytes = 4
            Case 12001
                _unitBytes = 4
                _bigEndian = True
            Case Else
                _unitBytes = 1
        End Select
        _fileSize = New FileInfo(filePath).Length
        _dataStart = Math.Min(Math.Max(0, bomLength), _fileSize)
        ' A multiple of 4 keeps every nominal cut on a UTF-16 and UTF-32 code unit.
        _windowSize = Math.Max(MinWindowBytes, windowBytes) And Not 3
        Dim dataLength As Long = _fileSize - _dataStart
        _totalWindows = CInt(Math.Max(1L, (dataLength + _windowSize - 1) \ _windowSize))
    End Sub

    ' The TextReadWindowMb preference in bytes, clamped to its bounds.
    Public Shared Function WindowBytesFromMb(windowMb As Integer) As Integer
        Return Math.Min(MaxWindowMb, Math.Max(MinWindowMb, windowMb)) * 1048576
    End Function

    Public ReadOnly Property FileSize As Long
        Get
            Return _fileSize
        End Get
    End Property

    Public ReadOnly Property WindowSize As Integer
        Get
            Return _windowSize
        End Get
    End Property

    Public ReadOnly Property TotalWindows As Integer
        Get
            Return _totalWindows
        End Get
    End Property

    ' The window ReadWindow returned last.
    Public ReadOnly Property CurrentWindowIndex As Integer
        Get
            Return _currentWindowIndex
        End Get
    End Property

    ' Byte range [startOffset, endOffset) of a window in the file, for the status line.
    Public Sub GetWindowBounds(windowIndex As Integer, ByRef startOffset As Long, ByRef endOffset As Long)
        Dim index As Integer = ClampIndex(windowIndex)
        startOffset = CutAt(index)
        endOffset = CutAt(index + 1)
    End Sub

    ' Decoded text of a window; malformed bytes read as the replacement character.
    Public Function ReadWindow(windowIndex As Integer) As String
        Dim index As Integer = ClampIndex(windowIndex)
        Dim startOffset As Long = CutAt(index)
        Dim endOffset As Long = CutAt(index + 1)
        Dim bytes As Byte() = ReadRange(startOffset, CInt(endOffset - startOffset))
        _currentWindowIndex = index
        Return _encoding.GetString(bytes)
    End Function

    ' The window holding a byte offset of the file; an offset outside the file gives the first or
    ' last window. Reads nothing beyond the one cut it has to compare.
    Public Function SeekToOffset(byteOffset As Long) As Integer
        If byteOffset <= _dataStart Then Return 0
        Dim index As Integer = ClampIndex(CInt(Math.Min(Integer.MaxValue, (byteOffset - _dataStart) \ _windowSize)))
        If index + 1 < _totalWindows AndAlso byteOffset >= CutAt(index + 1) Then index += 1
        Return index
    End Function

    Private Function ClampIndex(windowIndex As Integer) As Integer
        Return Math.Min(_totalWindows - 1, Math.Max(0, windowIndex))
    End Function

    Private Function CutAt(index As Integer) As Long
        If index <= 0 Then Return _dataStart
        If index >= _totalWindows Then Return _fileSize
        Dim cut As Long
        If _cuts.TryGetValue(index, cut) Then Return cut
        cut = SnapCut(_dataStart + CLng(index) * _windowSize)
        _cuts(index) = cut
        Return cut
    End Function

    Private Function SnapCut(nominal As Long) As Long
        Dim searchBytes As Integer = If(_unitBytes = 1 AndAlso Not _encoding.IsSingleByte AndAlso _encoding.CodePage <> 65001,
                                        DoubleByteSearchBytes, SnapMarginBytes)
        Dim from As Long = Math.Max(_dataStart, nominal - searchBytes)
        ' One code unit of lookahead decides whether a CR at the cut is the first half of CRLF.
        Dim span As Byte() = ReadRange(from, CInt(nominal - from) + _unitBytes)
        Dim cutInSpan As Integer = CInt(nominal - from)
        Dim lineFloor As Integer = Math.Max(0, cutInSpan - SnapMarginBytes)

        Dim lineStart As Integer = FindLineStart(span, cutInSpan, lineFloor)
        If lineStart > 0 Then Return from + lineStart
        Return from + FindCharacterBoundary(span, cutInSpan)
    End Function

    ' The last position p in (floor, cut] that starts a line: the code unit before p is LF, or
    ' is CR not followed by LF. 0 when there is none.
    Private Function FindLineStart(span As Byte(), cut As Integer, floor As Integer) As Integer
        For p As Integer = cut To Math.Max(_unitBytes, floor) Step -_unitBytes
            Dim previous As Integer = UnitAt(span, p - _unitBytes)
            If previous = 10 Then Return p
            If previous = 13 AndAlso UnitAt(span, p) <> 10 Then Return p
        Next
        Return 0
    End Function

    Private Function FindCharacterBoundary(span As Byte(), cut As Integer) As Integer
        If _unitBytes = 2 Then
            ' A low surrogate at the cut belongs with the high surrogate before it.
            Dim unit As Integer = UnitAt(span, cut)
            If unit >= &HDC00 AndAlso unit <= &HDFFF AndAlso cut >= 2 Then Return cut - 2
            Return cut
        End If
        If _unitBytes = 4 OrElse _encoding.IsSingleByte Then Return cut
        If _encoding.CodePage = 65001 Then
            Dim p As Integer = cut
            While p > 0 AndAlso cut - p < 3 AndAlso p < span.Length AndAlso (span(p) And &HC0) = &H80
                p -= 1
            End While
            Return p
        End If
        ' Double-byte code pages: a byte below 0x40 is never a lead or trail byte, so the
        ' position after it is a character boundary. GB18030 uses digits as its second byte.
        Dim singleByteCeiling As Byte = If(_encoding.CodePage = 54936, CByte(&H30), CByte(&H40))
        For p As Integer = cut To 1 Step -1
            If span(p - 1) < singleByteCeiling Then Return p
        Next
        Return cut
    End Function

    ' Code unit value at a byte position of the span; -1 past its end.
    Private Function UnitAt(span As Byte(), position As Integer) As Integer
        If position < 0 OrElse position + _unitBytes > span.Length Then Return -1
        Select Case _unitBytes
            Case 2
                If _bigEndian Then Return (CInt(span(position)) << 8) Or span(position + 1)
                Return (CInt(span(position + 1)) << 8) Or span(position)
            Case 4
                If _bigEndian Then
                    Return (CInt(span(position)) << 24) Or (CInt(span(position + 1)) << 16) Or (CInt(span(position + 2)) << 8) Or span(position + 3)
                End If
                Return (CInt(span(position + 3)) << 24) Or (CInt(span(position + 2)) << 16) Or (CInt(span(position + 1)) << 8) Or span(position)
            Case Else
                Return span(position)
        End Select
    End Function

    ' Up to count bytes from offset; fewer when the file has since become shorter.
    Private Function ReadRange(offset As Long, count As Integer) As Byte()
        Dim available As Long = Math.Max(0L, Math.Min(CLng(count), _fileSize - offset))
        Dim buffer(CInt(available) - 1) As Byte
        If available = 0 Then Return buffer
        Dim total As Integer = 0
        Using stream As New FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 4096, FileOptions.RandomAccess)
            stream.Seek(offset, SeekOrigin.Begin)
            While total < buffer.Length
                Dim read As Integer = stream.Read(buffer, total, buffer.Length - total)
                If read <= 0 Then Exit While
                total += read
            End While
        End Using
        If total < buffer.Length Then Array.Resize(buffer, total)
        Return buffer
    End Function
End Class
#End If
