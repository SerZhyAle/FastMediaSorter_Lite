#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets
Imports System.Text

' Ticket 199 section 3 and section 8: the LAN responses, byte for byte.
'
' The AUDIO_ONLY shape of LIVE-BROADCAST 2.1 [CONTRACT]: HTTP/1.1 200 OK, Content-Type audio/aac,
' Transfer-Encoding chunked, Accept-Ranges none, icy-name, icy-genre, icy-pub, icy-br - and NEVER
' icy-metaint: in-stream metadata is not offered, and a client that asks for it (Icy-MetaData: 1) gets
' the same response with none, which is expected, not an error.
'
' The MPEG-TS shape of amendment item C [PROPOSED]: video/mp2t, chunked, Cache-Control no-cache, no
' icy-metaint.
'
' Hand-built, not HttpListener: the response is a fixed byte sequence the tests compare against a
' raw TcpListener client in the style of 023's mock device, and the listener is bound to the one
' chosen address - HttpListener's prefix ACL and header set stand in the way of both.

Friend NotInheritable Class LiveProducerWire

    Private Sub New()
    End Sub

    Public Const CrLf As String = ControlChars.CrLf
    Public Const OkStatusLine As String = "HTTP/1.1 200 OK"
    Public Const NotFoundStatusLine As String = "HTTP/1.1 404 Not Found"
    Public Const UnavailableStatusLine As String = "HTTP/1.1 503 Service Unavailable"

    ''' <summary>The 2.1 response head, exactly: status line, the five headers of the shape in the
    ''' contract's order, never icy-metaint. The icy values come from the broadcast's own settings.</summary>
    Public Shared Function AudioResponseHead(name As String, genre As String, bitrateKbps As Integer) As Byte()
        Dim sb As New StringBuilder()
        sb.Append(OkStatusLine).Append(CrLf)
        sb.Append("Content-Type: ").Append(LiveProducerContract.AudioContentType).Append(CrLf)
        sb.Append("Transfer-Encoding: chunked").Append(CrLf)
        sb.Append("Accept-Ranges: none").Append(CrLf)
        sb.Append("icy-name: ").Append(SanitizeHeaderValue(name)).Append(CrLf)
        sb.Append("icy-genre: ").Append(SanitizeHeaderValue(genre)).Append(CrLf)
        sb.Append("icy-pub: 1").Append(CrLf)
        sb.Append("icy-br: ").Append(bitrateKbps.ToString(Globalization.CultureInfo.InvariantCulture)).Append(CrLf)
        sb.Append(CrLf)
        Return Encoding.ASCII.GetBytes(sb.ToString())
    End Function

    ''' <summary>The item C video response head: video/mp2t, chunked, no-cache, no icy-metaint, no
    ''' icy-* at all - the shape carries no audio-radio headers.</summary>
    Public Shared Function VideoResponseHead() As Byte()
        Dim sb As New StringBuilder()
        sb.Append(OkStatusLine).Append(CrLf)
        sb.Append("Content-Type: ").Append(LiveProducerContract.VideoContentType).Append(CrLf)
        sb.Append("Transfer-Encoding: chunked").Append(CrLf)
        sb.Append("Cache-Control: no-cache").Append(CrLf)
        sb.Append(CrLf)
        Return Encoding.ASCII.GetBytes(sb.ToString())
    End Function

    ''' <summary>The plain refusal answers, with Content-Length 0 and a body that carries no information.</summary>
    Public Shared Function SimpleResponse(statusLine As String) As Byte()
        Dim body As String = ""
        Dim sb As New StringBuilder()
        sb.Append(statusLine).Append(CrLf)
        sb.Append("Content-Length: 0").Append(CrLf)
        sb.Append("Connection: close").Append(CrLf)
        sb.Append(CrLf)
        Return Encoding.ASCII.GetBytes(sb.ToString())
    End Function

    ''' <summary>One chunked-encoding chunk of the body.</summary>
    Public Shared Function Chunk(data As Byte()) As Byte()
        If data Is Nothing OrElse data.Length = 0 Then Return Encoding.ASCII.GetBytes("0" & CrLf & CrLf)
        Dim head As String = data.Length.ToString("X", Globalization.CultureInfo.InvariantCulture) & CrLf
        Dim headBytes As Byte() = Encoding.ASCII.GetBytes(head)
        Dim out(headBytes.Length + data.Length + 2 - 1) As Byte
        Array.Copy(headBytes, 0, out, 0, headBytes.Length)
        Array.Copy(data, 0, out, headBytes.Length, data.Length)
        out(out.Length - 2) = 13
        out(out.Length - 1) = 10
        Return out
    End Function

    ''' <summary>The terminating chunk of a body that ends cleanly ("the chunked body ends cleanly").</summary>
    Public Shared Function FinalChunk() As Byte()
        Return Encoding.ASCII.GetBytes("0" & CrLf & CrLf)
    End Function

    ''' <summary>A header value may not carry CR or LF - a title is user text. A line break collapses
    ''' to one space (CR, LF and CRLF all count as one break, not two).</summary>
    Public Shared Function SanitizeHeaderValue(value As String) As String
        Dim clean As String = If(value, "").Replace(ControlChars.CrLf, " ").Replace(ControlChars.Cr, " ").Replace(ControlChars.Lf, " ").Trim()
        Return clean
    End Function

    ' --- the request, as far as the server needs it --------------------------------

    ''' <summary>One parsed request head. The server reads the request line and the headers and nothing
    ''' more - there is no body to read on a stream request.</summary>
    Friend Class LiveWireRequest
        Public Property Method As String = ""
        Public Property Path As String = ""
        Public ReadOnly Property Headers As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>The client asked for in-stream metadata (Icy-MetaData: 1). The shape never offers
        ''' it: the response is the same with none (2.1).</summary>
        Public ReadOnly Property AsksForIcyMetadata As Boolean
            Get
                Dim v As String = Nothing
                Return Headers.TryGetValue("Icy-MetaData", v) AndAlso v.Trim() = "1"
            End Get
        End Property
    End Class

    ''' <summary>Reads the request head from the socket, bounded: at most 8 KiB and at most 5 seconds -
    ''' a client that opens a connection and says nothing is dropped, not waited on.</summary>
    Public Shared Function ReadRequest(client As TcpClient, maxBytes As Integer, timeoutMs As Integer) As LiveWireRequest
        Dim stream As NetworkStream = client.GetStream()
        stream.ReadTimeout = timeoutMs
        Dim buffer(maxBytes - 1) As Byte
        Dim filled As Integer = 0
        Dim headEnd As Integer = -1
        While filled < maxBytes
            Dim read As Integer = stream.Read(buffer, filled, Math.Min(4096, maxBytes - filled))
            If read <= 0 Then Exit While
            filled += read
            headEnd = FindHeaderEnd(buffer, filled)
            If headEnd >= 0 Then Exit While
        End While
        If headEnd < 0 Then Return Nothing

        Dim headText As String = Encoding.ASCII.GetString(buffer, 0, headEnd)
        Dim request As New LiveWireRequest()
        Dim lines As String() = headText.Split({ControlChars.CrLf}, StringSplitOptions.None)
        If lines.Length = 0 Then Return Nothing

        Dim parts As String() = lines(0).Split(" "c)
        If parts.Length < 2 Then Return Nothing
        request.Method = parts(0).Trim().ToUpperInvariant()
        Dim path As String = parts(1).Trim()
        Dim queryAt As Integer = path.IndexOf("?"c)
        If queryAt >= 0 Then path = path.Substring(0, queryAt)
        request.Path = path

        For i As Integer = 1 To lines.Length - 1
            Dim line As String = lines(i)
            If line.Length = 0 Then Continue For
            Dim colonAt As Integer = line.IndexOf(":"c)
            If colonAt <= 0 Then Continue For
            request.Headers(line.Substring(0, colonAt).Trim()) = line.Substring(colonAt + 1).Trim()
        Next
        Return request
    End Function

    Private Shared Function FindHeaderEnd(buffer As Byte(), filled As Integer) As Integer
        For i As Integer = 0 To filled - 4
            If buffer(i) = 13 AndAlso buffer(i + 1) = 10 AndAlso buffer(i + 2) = 13 AndAlso buffer(i + 3) = 10 Then
                Return i
            End If
        Next
        Return -1
    End Function

End Class
#End If
