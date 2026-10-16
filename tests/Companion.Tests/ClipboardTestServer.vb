Option Strict On

Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion

' Spec 234 T-4: the exchange server of the clipboard tests, now the SEALED one. A conforming client sends and uses only sealed values
' (DEVICE-EXCHANGE 27 items DN and DP), so the server the tests talk to is a server that lists `sealed`, keeps the box and the sealed
' bytes as it is given them and never sees a clear byte. The test side keeps its old way of speaking about a value - Kind, Name, Mime,
' Stored are the CLEAR value - and the server models the OTHER device of the entry that reads what was put and sends what is read:
'   - a put: the box is opened with the key of `Peer` (a recipient the sender's directory lists), every segment is opened, and Stored
'     / Kind / Name / Mime become what that device would show; the wire members (LastWire*, LastPutSealed*) are what the server saw;
'   - a get: whatever a test set in Stored (+ Kind, Name, Mime) is sealed by `Peer` for `Own` (the device under test) the first time
'     it is asked for, once, so the state and the get describe the same bytes.
' Nothing here leaves the machine: the server is the kit's TLS stub on 127.0.0.1.

''' <summary>An exchange server that holds ONE clipboard value, answering `clipboard-put`, `-info` and `-get` over TLS as the
''' contract's section 22 and 27 say. The knobs make it refuse, lie about a hash, stop in the middle of a value or serve what no
''' conforming sender writes.</summary>
Friend NotInheritable Class ClipboardTestServer
    Implements IDisposable

    Private Shared ReadOnly Registry As New ConcurrentDictionary(Of Integer, ClipboardTestServer)()

    Private ReadOnly _server As ExchangeStubTlsServer
    Private ReadOnly _gate As New Object()
    Private _sealedFeature As Boolean = True
    Private _clear As Byte()
    Private _sealed As SealedTestValue

    ''' <summary>The device under test, as the entry knows it: the id the credentials carry, and a key of its own.</summary>
    Public ReadOnly Own As SealedTestDevice
    ''' <summary>The other device of the entry: it is a recipient of what the device under test sends, and the sender of what it gets.</summary>
    Public ReadOnly Peer As SealedTestDevice
    ''' <summary>The directory of the entry as the device under test sees it.</summary>
    Public ReadOnly Directory As SealedTestDirectory

    ' ---- the clear value a test arranges (a get) or reads back (a put)
    Public Kind As String = "text"
    Public Name As String = ""
    Public Mime As String = ""
    Public StoredAt As String = "2026-10-15T10:00:00Z"
    Public Device As String = "Phone"
    ''' <summary>A refusal name: a put is answered with it instead of `clipboard-ready`.</summary>
    Public RefusePutWith As String = ""
    ''' <summary>Hash the next value announces instead of the real one (the SEALED bytes' hash on a sealed server).</summary>
    Public ValueSha As String = Nothing
    ''' <summary>When 0 or more, a value is sent only this many bytes long and the connection ends (an interrupted transfer).</summary>
    Public CutValueAfter As Integer = -1
    ''' <summary>A put is opened and hashed while it streams and not kept (a value too large to hold).</summary>
    Public HashOnly As Boolean
    ''' <summary>The value in Stored goes out as it was once, in clear, with its kind, type and name (an old sender): never placed.</summary>
    Public ServeClear As Boolean
    ''' <summary>False seals the value for the peer only: the device under test joined after it was sent and is not in the box.</summary>
    Public SealToOwn As Boolean = True

    ' ---- what a put showed the server
    ''' <summary>Clear size and SHA-256 of the value, as the peer opened it (the sealed size and hash when it could not be opened).</summary>
    Public LastPutSize As Long
    Public LastPutSha As String = ""
    Public LastPutKind As String = ""
    Public LastPutName As String = ""
    Public LastPutMime As String = ""
    ''' <summary>The `kind` the put carried on the wire.</summary>
    Public LastWireKind As String = ""
    ''' <summary>The `size` the put declared: the sealed size.</summary>
    Public LastWireSize As Long
    Public LastPutSealedSize As Long
    Public LastPutSealedSha As String = ""
    Public LastWireHadMime As Boolean
    Public LastWireHadName As Boolean
    ''' <summary>The `sealed` record of the put (the box as sent), "" for a clear put.</summary>
    Public LastPutBox As String = ""
    ''' <summary>The put envelope as the server read it (JSON text).</summary>
    Public LastPutFrame As String = ""
    ''' <summary>The raw body of the last put (not kept when <see cref="HashOnly"/>).</summary>
    Public LastPutBody As Byte()
    ''' <summary>True when the peer opened every segment of the last sealed put and the box's clear size and hash held.</summary>
    Public LastPutOpened As Boolean

    Private _puts As Integer
    Private _gets As Integer
    Private _infos As Integer

    Public Sub New()
        Own = New SealedTestDevice(ExchangeWireTestKit.IdOf("D"c))
        Peer = New SealedTestDevice(ExchangeWireTestKit.IdOf("S"c))
        Directory = New SealedTestDirectory(Own)
        Directory.Add(Peer)
        _server = New ExchangeStubTlsServer(AddressOf HandleAsync)
        Registry(_server.Port) = Me
    End Sub

    ''' <summary>The server behind a target's address, or Nothing (an address nobody listens on).</summary>
    Public Shared Function ByPort(port As Integer) As ClipboardTestServer
        Dim found As ClipboardTestServer = Nothing
        If Registry.TryGetValue(port, found) Then Return found
        Return Nothing
    End Function

    Public ReadOnly Property Port As Integer
        Get
            Return _server.Port
        End Get
    End Property

    Public ReadOnly Property Fingerprint As String
        Get
            Return _server.Fingerprint
        End Get
    End Property

    ''' <summary>TLS connections that were accepted: a value that was never offered to the server leaves this at 0.</summary>
    Public ReadOnly Property Connections As Integer
        Get
            Return _server.Accepted
        End Get
    End Property

    Public ReadOnly Property Puts As Integer
        Get
            Return Volatile.Read(_puts)
        End Get
    End Property

    Public ReadOnly Property Gets As Integer
        Get
            Return Volatile.Read(_gets)
        End Get
    End Property

    Public ReadOnly Property Infos As Integer
        Get
            Return Volatile.Read(_infos)
        End Get
    End Property

    ''' <summary>True when the welcome lists `sealed` (and the directory of the entry says so too).</summary>
    Public Property Sealed As Boolean
        Get
            Return _sealedFeature
        End Get
        Set(value As Boolean)
            _sealedFeature = value
            Directory.ServerSupportsSealed = value
        End Set
    End Property

    ''' <summary>The CLEAR bytes of the value: what a test arranged for a get, or what the peer opened out of a put (Nothing when the
    ''' server holds nothing or the value could not be opened). Setting it replaces the value; it is sealed when first asked for.</summary>
    Public Property Stored As Byte()
        Get
            SyncLock _gate
                Return _clear
            End SyncLock
        End Get
        Set(value As Byte())
            SyncLock _gate
                _clear = value
                _sealed = Nothing
            End SyncLock
        End Set
    End Property

    ''' <summary>The server holds exactly this sealed value (a box and bytes a test made, however wrong): Stored is Nothing then.</summary>
    Public Sub HoldSealed(value As SealedTestValue)
        SyncLock _gate
            _clear = Nothing
            _sealed = value
        End SyncLock
    End Sub

    ''' <summary>The sealed bytes the server holds (Nothing when none, or when the value is served in clear).</summary>
    Public Function HeldSealed() As SealedTestValue
        SyncLock _gate
            Return _sealed
        End SyncLock
    End Function

    ''' <summary>Whether the server holds a value at all.</summary>
    Public ReadOnly Property HasValue As Boolean
        Get
            SyncLock _gate
                Return _clear IsNot Nothing OrElse _sealed IsNot Nothing
            End SyncLock
        End Get
    End Property

    Private NotInheritable Class WireValue
        Public Kind As String = ""
        Public Mime As String = ""
        Public Name As String = ""
        Public Bytes As Byte() = Array.Empty(Of Byte)()
        Public Sha As String = ""
        Public BoxJson As String = ""
    End Class

    ''' <summary>The value the way it goes out on the wire; Nothing when the server holds none.</summary>
    Private Function CurrentWire() As WireValue
        SyncLock _gate
            If ServeClear AndAlso _clear IsNot Nothing Then
                Return New WireValue With {.Kind = Kind, .Mime = Mime, .Name = Name, .Bytes = _clear, .Sha = SealedTestValue.Sha(_clear)}
            End If
            If _sealed Is Nothing AndAlso _clear IsNot Nothing Then
                Dim recipients As New List(Of SealedTestDevice) From {Peer}
                If SealToOwn Then recipients.Insert(0, Own)
                _sealed = SealedTestValue.MakeClear(_clear, Kind, Mime, Name, Peer, recipients)
            End If
            If _sealed Is Nothing Then Return Nothing
            Return New WireValue With {.Kind = ExchangeClipboardKinds.Sealed, .Bytes = _sealed.SealedBytes, .Sha = _sealed.SealedSha, .BoxJson = _sealed.BoxJson}
        End SyncLock
    End Function

    Private Async Function HandleAsync(c As ExchangeStubConnection) As Task
        Await c.ReadAsync()
        Dim features As String() = If(_sealedFeature, New String() {"directory", "tunnel", "clipboard", "sealed"}, New String() {"directory", "tunnel", "clipboard"})
        Await c.WriteAsync(c.Welcome(features:=features))
        c.Authenticated()
        Dim request As ExchangeEnvelope = Await c.ReadAsync()
        Select Case request.Type
            Case "clipboard-put"
                Interlocked.Increment(_puts)
                SyncLock _gate
                    LastPutFrame = Encoding.UTF8.GetString(ExchangeEnvelopeCodec.Encode(request))
                    LastWireKind = request.Text("kind")
                    LastWireSize = request.Number("size")
                    LastWireHadMime = request.Text("mimeType").Length > 0
                    LastWireHadName = request.Text("name").Length > 0
                    LastPutBox = request.Record("sealed")
                End SyncLock
                If RefusePutWith.Length > 0 Then
                    Await c.Refuse(RefusePutWith)
                    Return
                End If
                Await c.WriteAsync(New ExchangeEnvelope("clipboard-ready"))
                If String.Equals(request.Text("kind"), ExchangeClipboardKinds.Sealed, StringComparison.Ordinal) Then
                    Await TakeSealedPutAsync(c, request)
                Else
                    Await TakeClearPutAsync(c, request)
                End If
            Case "clipboard-info"
                Interlocked.Increment(_infos)
                Dim wire As WireValue = CurrentWire()
                If wire Is Nothing Then
                    Await c.WriteAsync(New ExchangeEnvelope("clipboard-state"))
                Else
                    Await c.WriteAsync(New ExchangeEnvelope("clipboard-state").SetRecord("value", ValueJson(wire)))
                End If
            Case "clipboard-get"
                Interlocked.Increment(_gets)
                Dim wire As WireValue = CurrentWire()
                If wire Is Nothing Then
                    Await c.WriteAsync(New ExchangeEnvelope("clipboard-empty"))
                    Return
                End If
                Dim value As ExchangeEnvelope = New ExchangeEnvelope("clipboard-value").SetText("clipId", ExchangeWireTestKit.IdOf("C"c)).
                    SetNumber("size", wire.Bytes.LongLength).SetText("sha256", If(ValueSha, wire.Sha)).
                    SetText("storedAt", StoredAt).SetText("sourceDeviceId", ExchangeWireTestKit.IdOf("S"c)).
                    SetText("sourceDeviceName", Device).SetText("kind", wire.Kind)
                If wire.Mime.Length > 0 Then value.SetText("mimeType", wire.Mime)
                If wire.Name.Length > 0 Then value.SetText("name", wire.Name)
                If wire.BoxJson.Length > 0 Then value.SetRecord("sealed", wire.BoxJson)
                Await c.WriteAsync(value)
                Dim count As Integer = If(CutValueAfter >= 0, Math.Min(CutValueAfter, wire.Bytes.Length), wire.Bytes.Length)
                Await c.Stream.WriteAsync(wire.Bytes, 0, count, c.Token)
                Await c.Stream.FlushAsync(c.Token)
        End Select
    End Function

    ''' <summary>A put of the old kinds (text, image, file): what a conforming client never sends, kept so that a test can show it is not.</summary>
    Private Async Function TakeClearPutAsync(c As ExchangeStubConnection, request As ExchangeEnvelope) As Task
        Dim size As Long = request.Number("size")
        Dim sha As String
        Dim held As Byte() = Nothing
        If HashOnly Then
            Dim hashed As Tuple(Of Long, String) = Await ClipboardTestKit.HashExactAsync(c.Stream, size, c.Token)
            LastPutSize = hashed.Item1
            sha = hashed.Item2
        Else
            held = Await ClipboardTestKit.ReadExactAsync(c.Stream, size, c.Token)
            LastPutSize = held.LongLength
            sha = ClipboardTestKit.Sha(held)
        End If
        SyncLock _gate
            If Not HashOnly Then
                _clear = held
                _sealed = Nothing
                LastPutBody = held
            End If
            Kind = request.Text("kind")
            Name = request.Text("name")
            Mime = request.Text("mimeType")
            LastPutSha = sha
            LastPutKind = Kind
            LastPutName = Name
            LastPutMime = Mime
        End SyncLock
        Await c.WriteAsync(New ExchangeEnvelope("clipboard-stored").SetText("clipId", ExchangeWireTestKit.IdOf("C"c)).
                           SetText("sha256", sha).SetText("storedAt", StoredAt))
    End Function

    ''' <summary>A sealed put: the sealed bytes are read, hashed and kept as the server keeps them; the peer then opens the box and
    ''' every segment to learn what the value is (the test's view of it), or streams through them when the value is too large to hold.</summary>
    Private Async Function TakeSealedPutAsync(c As ExchangeStubConnection, request As ExchangeEnvelope) As Task
        Dim size As Long = request.Number("size")
        Dim boxJson As String = request.Record("sealed")
        Dim payload As ExchangeClipboardSealedPayload = Nothing
        Dim valueKey As Byte() = SealedTestValue.OpenBox(boxJson, Peer, Own.Key.Kem, payload)
        Dim sealedSha As String
        Try
            If HashOnly Then
                Dim streamed As StreamedOpen = Await OpenStreamedAsync(c.Stream, size, valueKey, c.Token)
                sealedSha = streamed.SealedSha
                SyncLock _gate
                    LastPutOpened = streamed.Opened AndAlso payload IsNot Nothing AndAlso payload.Matches(streamed.ClearSize, streamed.ClearSha)
                    LastPutSize = streamed.ClearSize
                    LastPutSha = streamed.ClearSha
                    LastPutSealedSize = streamed.SealedSize
                    LastPutSealedSha = sealedSha
                    If payload IsNot Nothing Then
                        LastPutKind = payload.Kind
                        LastPutName = payload.Name
                        LastPutMime = payload.MimeType
                    End If
                End SyncLock
            Else
                Dim held As Byte() = Await ClipboardTestKit.ReadExactAsync(c.Stream, size, c.Token)
                sealedSha = ClipboardTestKit.Sha(held)
                Dim clear As Byte() = Nothing
                If valueKey IsNot Nothing AndAlso payload IsNot Nothing Then clear = ExchangeClipboardSeal.OpenBytes(held, valueKey, payload.Size, payload.Sha256)
                SyncLock _gate
                    LastPutBody = held
                    LastPutOpened = clear IsNot Nothing
                    LastPutSealedSize = held.LongLength
                    LastPutSealedSha = sealedSha
                    LastPutSize = If(clear IsNot Nothing, clear.LongLength, held.LongLength)
                    LastPutSha = If(clear IsNot Nothing, SealedTestValue.Sha(clear), sealedSha)
                    _sealed = SealedTestValue.Wrap(boxJson, held)
                    _clear = clear
                    If payload IsNot Nothing Then
                        Kind = payload.Kind
                        Name = payload.Name
                        Mime = payload.MimeType
                        LastPutKind = payload.Kind
                        LastPutName = payload.Name
                        LastPutMime = payload.MimeType
                    End If
                End SyncLock
            End If
        Finally
            If valueKey IsNot Nothing Then Array.Clear(valueKey, 0, valueKey.Length)
        End Try
        Await c.WriteAsync(New ExchangeEnvelope("clipboard-stored").SetText("clipId", ExchangeWireTestKit.IdOf("C"c)).
                           SetText("sha256", sealedSha).SetText("storedAt", StoredAt))
    End Function

    Private NotInheritable Class StreamedOpen
        Public Opened As Boolean
        Public ClearSize As Long
        Public ClearSha As String = ""
        Public SealedSize As Long
        Public SealedSha As String = ""
    End Class

    ''' <summary>Reads <paramref name="size"/> sealed bytes in segments, opens each one and hashes the sealed and the clear bytes
    ''' without keeping any of them: a value of gigabytes is held by nobody here either.</summary>
    Private Shared Async Function OpenStreamedAsync(stream As Stream, size As Long, valueKey As Byte(), ct As CancellationToken) As Task(Of StreamedOpen)
        Dim result As New StreamedOpen()
        Dim buffer(ExchangeClipboardSeal.SealedSegmentSize - 1) As Byte
        Dim clear(ExchangeClipboardSeal.SegmentSize - 1) As Byte
        Dim aes As AesGcm = Nothing
        If valueKey IsNot Nothing Then aes = New AesGcm(valueKey, ExchangeClipboardSeal.TagLength)
        Dim ok As Boolean = aes IsNot Nothing
        Try
            Using sealedHash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                Using clearHash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                    Dim left As Long = size
                    Dim index As Long = 0
                    While left > 0
                        Dim want As Integer = CInt(Math.Min(left, CLng(buffer.Length)))
                        Dim got As Integer = 0
                        While got < want
                            Dim n As Integer = Await stream.ReadAsync(buffer, got, want - got, ct)
                            If n = 0 Then Exit While
                            got += n
                        End While
                        If got = 0 Then Exit While
                        sealedHash.AppendData(buffer, 0, got)
                        result.SealedSize += got
                        left -= got
                        If ok Then
                            If got < want OrElse got <= ExchangeClipboardSeal.TagLength Then
                                ok = False
                            ElseIf ExchangeClipboardSeal.TryOpenSegment(aes, ExchangeClipboardSeal.SegmentNonce(index, left = 0), buffer, got, clear) Then
                                clearHash.AppendData(clear, 0, got - ExchangeClipboardSeal.TagLength)
                                result.ClearSize += got - ExchangeClipboardSeal.TagLength
                            Else
                                ok = False
                            End If
                        End If
                        index += 1
                        If got < want Then Exit While
                    End While
                    result.SealedSha = Convert.ToHexString(sealedHash.GetHashAndReset()).ToLowerInvariant()
                    result.ClearSha = Convert.ToHexString(clearHash.GetHashAndReset()).ToLowerInvariant()
                End Using
            End Using
        Finally
            If aes IsNot Nothing Then aes.Dispose()
        End Try
        result.Opened = ok AndAlso result.SealedSize = size
        Return result
    End Function

    ''' <summary>The value as the `clipboard-state` record carries it.</summary>
    Private Function ValueJson(wire As WireValue) As String
        Dim sb As New StringBuilder()
        sb.Append("{""clipId"":""").Append(ExchangeWireTestKit.IdOf("C"c)).Append(""",""size"":").Append(wire.Bytes.LongLength.ToString(Globalization.CultureInfo.InvariantCulture))
        sb.Append(",""sha256"":""").Append(wire.Sha).Append(""",""storedAt"":""").Append(StoredAt)
        sb.Append(""",""sourceDeviceId"":""").Append(ExchangeWireTestKit.IdOf("S"c)).Append(""",""sourceDeviceName"":").Append(JsonSerializer.Serialize(Device))
        sb.Append(",""kind"":").Append(JsonSerializer.Serialize(wire.Kind))
        If wire.Mime.Length > 0 Then sb.Append(",""mimeType"":").Append(JsonSerializer.Serialize(wire.Mime))
        If wire.Name.Length > 0 Then sb.Append(",""name"":").Append(JsonSerializer.Serialize(wire.Name))
        If wire.BoxJson.Length > 0 Then sb.Append(",""sealed"":").Append(wire.BoxJson)
        sb.Append("}")
        Return sb.ToString()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim removed As ClipboardTestServer = Nothing
        Registry.TryRemove(_server.Port, removed)
        _server.Dispose()
        Own.Dispose()
        Peer.Dispose()
    End Sub

End Class
