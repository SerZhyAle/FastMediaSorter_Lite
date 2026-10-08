#If Not NETFRAMEWORK Then
Option Strict On

' Ticket 180 T-1. The JSON the wire is written in, byte for byte as the Go module's encoder writes it
' (encoding/json with SetEscapeHTML(false)): the frames are held to generated vectors, so a writer that
' is merely valid JSON would fail them. System.Text.Json is used to READ only.
'
' The writer produces UTF-8 straight into a buffer it owns, so a frame that carries a password never
' exists as a string of its own and the buffer can be wiped after the send (ExchangeFrameWriter).

''' <summary>A forward-only UTF-8 JSON writer with the escaping of Go's encoder.</summary>
Friend NotInheritable Class ExchangeJsonWriter

    Private _buf As Byte() = New Byte(255) {}
    Private _len As Integer
    ' One entry per open object or array: whether it already holds an item (so the next needs a comma).
    Private ReadOnly _hasItem As New List(Of Boolean)
    Private _afterName As Boolean

    Private Const HexDigits As String = "0123456789abcdef"

    Public Sub BeginObject()
        BeforeValue()
        Append(CByte(AscW("{"c)))
        _hasItem.Add(False)
    End Sub

    Public Sub EndObject()
        _hasItem.RemoveAt(_hasItem.Count - 1)
        Append(CByte(AscW("}"c)))
    End Sub

    Public Sub BeginArray()
        BeforeValue()
        Append(CByte(AscW("["c)))
        _hasItem.Add(False)
    End Sub

    Public Sub EndArray()
        _hasItem.RemoveAt(_hasItem.Count - 1)
        Append(CByte(AscW("]"c)))
    End Sub

    ''' <summary>A member name; the next call writes its value.</summary>
    Public Sub Name(memberName As String)
        Comma()
        WriteString(memberName)
        Append(CByte(AscW(":"c)))
        _afterName = True
    End Sub

    Public Sub Text(value As String)
        BeforeValue()
        WriteString(value)
    End Sub

    Public Sub Number(value As Long)
        BeforeValue()
        AppendAscii(value.ToString(Globalization.CultureInfo.InvariantCulture))
    End Sub

    Public Sub Flag(value As Boolean)
        BeforeValue()
        AppendAscii(If(value, "true", "false"))
    End Sub

    ''' <summary>One JSON value written as it is (the caller vouches for it; a record is compacted first).</summary>
    Public Sub Raw(compactJson As String)
        BeforeValue()
        AppendUtf8(compactJson)
    End Sub

    Public Function ToArray() As Byte()
        Dim r(_len - 1) As Byte
        Buffer.BlockCopy(_buf, 0, r, 0, _len)
        Return r
    End Function

    Public ReadOnly Property Length As Integer
        Get
            Return _len
        End Get
    End Property

    ''' <summary>Overwrites the buffer. Called once the bytes have been sent (or refused).</summary>
    Public Sub Clear()
        Array.Clear(_buf, 0, _buf.Length)
        _len = 0
    End Sub

    ' --- internals ---

    Private Sub Comma()
        If _hasItem.Count = 0 Then Return
        Dim top As Integer = _hasItem.Count - 1
        If _hasItem(top) Then Append(CByte(AscW(","c)))
        _hasItem(top) = True
    End Sub

    Private Sub BeforeValue()
        If _afterName Then
            _afterName = False
            Return
        End If
        Comma()
    End Sub

    Private Sub Ensure(extra As Integer)
        If _len + extra <= _buf.Length Then Return
        Dim bigger(Math.Max(_buf.Length * 2, _len + extra) - 1) As Byte
        Buffer.BlockCopy(_buf, 0, bigger, 0, _len)
        Array.Clear(_buf, 0, _buf.Length)   ' the old copy may hold a secret
        _buf = bigger
    End Sub

    Private Sub Append(b As Byte)
        Ensure(1)
        _buf(_len) = b
        _len += 1
    End Sub

    Private Sub AppendAscii(s As String)
        Ensure(s.Length)
        For Each c As Char In s
            _buf(_len) = CByte(AscW(c))
            _len += 1
        Next
    End Sub

    Private Sub AppendUtf8(s As String)
        Dim n As Integer = System.Text.Encoding.UTF8.GetByteCount(s)
        Ensure(n)
        _len += System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _len)
    End Sub

    Private Sub AppendEscape(ch As Char)
        Append(CByte(AscW("\"c)))
        Append(CByte(AscW(ch)))
    End Sub

    Private Sub AppendU(code As Integer)
        AppendAscii("\u")
        Append(CByte(AscW(HexDigits((code >> 12) And &HF))))
        Append(CByte(AscW(HexDigits((code >> 8) And &HF))))
        Append(CByte(AscW(HexDigits((code >> 4) And &HF))))
        Append(CByte(AscW(HexDigits(code And &HF))))
    End Sub

    Private Sub WriteString(value As String)
        Append(CByte(AscW(""""c)))
        If value IsNot Nothing Then
            Dim i As Integer = 0
            While i < value.Length
                Dim c As Char = value(i)
                Dim code As Integer = AscW(c)
                If c = """"c OrElse c = "\"c Then
                    AppendEscape(c)
                ElseIf code < &H20 Then
                    Select Case code
                        Case 10 : AppendEscape("n"c)
                        Case 13 : AppendEscape("r"c)
                        Case 9 : AppendEscape("t"c)
                        Case 8 : AppendEscape("b"c)
                        Case 12 : AppendEscape("f"c)
                        Case Else : AppendU(code)
                    End Select
                ElseIf code < &H80 Then
                    Append(CByte(code))
                ElseIf code = &H2028 OrElse code = &H2029 Then
                    AppendU(code)
                ElseIf Char.IsHighSurrogate(c) AndAlso i + 1 < value.Length AndAlso Char.IsLowSurrogate(value(i + 1)) Then
                    Dim cp As Integer = Char.ConvertToUtf32(c, value(i + 1))
                    Ensure(4)
                    _buf(_len) = CByte(&HF0 Or (cp >> 18))
                    _buf(_len + 1) = CByte(&H80 Or ((cp >> 12) And &H3F))
                    _buf(_len + 2) = CByte(&H80 Or ((cp >> 6) And &H3F))
                    _buf(_len + 3) = CByte(&H80 Or (cp And &H3F))
                    _len += 4
                    i += 1
                ElseIf Char.IsSurrogate(c) Then
                    AppendU(&HFFFD)   ' a lone surrogate is invalid UTF-8, which Go writes as U+FFFD
                ElseIf code < &H800 Then
                    Ensure(2)
                    _buf(_len) = CByte(&HC0 Or (code >> 6))
                    _buf(_len + 1) = CByte(&H80 Or (code And &H3F))
                    _len += 2
                Else
                    Ensure(3)
                    _buf(_len) = CByte(&HE0 Or (code >> 12))
                    _buf(_len + 1) = CByte(&H80 Or ((code >> 6) And &H3F))
                    _buf(_len + 2) = CByte(&H80 Or (code And &H3F))
                    _len += 3
                End If
                i += 1
            End While
        End If
        Append(CByte(AscW(""""c)))
    End Sub

End Class

''' <summary>Small helpers over JSON text that is already valid.</summary>
Friend NotInheritable Class ExchangeJson

    Private Sub New()
    End Sub

    ''' <summary>The same JSON with every insignificant whitespace removed (what Go's encoder does to a
    ''' raw record it embeds). The text must be valid JSON; strings are left exactly as they are.</summary>
    Public Shared Function Compact(json As String) As String
        If json Is Nothing Then Return ""
        Dim sb As New System.Text.StringBuilder(json.Length)
        Dim inString As Boolean = False
        Dim escaped As Boolean = False
        For Each c As Char In json
            If inString Then
                sb.Append(c)
                If escaped Then
                    escaped = False
                ElseIf c = "\"c Then
                    escaped = True
                ElseIf c = """"c Then
                    inString = False
                End If
            ElseIf c = """"c Then
                inString = True
                sb.Append(c)
            ElseIf c = " "c OrElse c = vbTab OrElse c = vbLf OrElse c = vbCr Then
                ' dropped
            Else
                sb.Append(c)
            End If
        Next
        Return sb.ToString()
    End Function

    ''' <summary>True when the text is exactly one JSON object.</summary>
    Public Shared Function IsObject(json As String) As Boolean
        If String.IsNullOrWhiteSpace(json) Then Return False
        Try
            Using doc As System.Text.Json.JsonDocument = System.Text.Json.JsonDocument.Parse(json)
                Return doc.RootElement.ValueKind = System.Text.Json.JsonValueKind.Object
            End Using
        Catch ex As System.Text.Json.JsonException
            Return False
        End Try
    End Function

End Class
#End If
