#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

' Spec 230 T-2. The clipboard section of DEVICE-EXCHANGE 1.1 (section 22, items BU-BZ, CA, CB) as a client: send one
' value (`clipboard-put`, then exactly `size` raw bytes), ask what the server holds (`clipboard-info` ->
' `clipboard-state`) and take it (`clipboard-get` -> `clipboard-value`, then exactly `size` raw bytes).
'
' The content never rides an envelope (item BW): the frame bounds of 7.1 do not limit a value, only the account's
' `clipboardBytes` does, and it is the SERVER that answers `limit-reached` on the declared size. Nothing here reads
' or writes the Windows clipboard (ExchangeClipboardAccess in the Share Manager does, off the UI thread) and nothing
' here logs: a result never carries content, a name or a hash into a message (item CC).
'
' Both directions are checked end to end: a send compares the server's `sha256` with its own, a get compares the
' byte count and the SHA-256 with `size` and `sha256` and never reports a partial value as received (item BY).

''' <summary>The three kinds of a value (item BU).</summary>
Friend NotInheritable Class ExchangeClipboardKinds

    Private Sub New()
    End Sub

    Public Const Text As String = "text"
    Public Const Image As String = "image"
    Public Const File As String = "file"

    Public Shared Function IsKnown(kind As String) As Boolean
        Return String.Equals(kind, Text, StringComparison.Ordinal) OrElse
               String.Equals(kind, Image, StringComparison.Ordinal) OrElse
               String.Equals(kind, File, StringComparison.Ordinal)
    End Function

End Class

''' <summary>The rules of item BW for the members of a value, and the saving of a received file (item CB).</summary>
Friend NotInheritable Class ExchangeClipboardRules

    Private Sub New()
    End Sub

    ''' <summary>A `name` is at most 255 bytes of UTF-8 (item BW).</summary>
    Public Const MaxNameBytes As Integer = 255
    ''' <summary>A `mimeType` is at most 127 bytes in the `type/subtype` form (item BW).</summary>
    Public Const MaxMimeBytes As Integer = 127
    ''' <summary>The default `clipboardBytes` of an account, 5 GiB (item BZ). The server decides; the sender only
    ''' uses it to refuse a hopeless send before it opens a stream, when the server's limit is not known to it.</summary>
    Public Const DefaultMaxBytes As Long = 5368709120L

    ''' <summary>The name rules of item BW: not empty, at most 255 UTF-8 bytes, no `/`, `\` or control character, not `.`
    ''' or `..`.</summary>
    Public Shared Function IsValidName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        If String.Equals(name, ".", StringComparison.Ordinal) OrElse String.Equals(name, "..", StringComparison.Ordinal) Then Return False
        If Encoding.UTF8.GetByteCount(name) > MaxNameBytes Then Return False
        For Each c As Char In name
            If c = "/"c OrElse c = "\"c OrElse Char.IsControl(c) Then Return False
        Next
        Return True
    End Function

    ''' <summary>`type/subtype`, at most 127 bytes, printable ASCII without blanks (item BW).</summary>
    Public Shared Function IsValidMime(mimeType As String) As Boolean
        If String.IsNullOrEmpty(mimeType) OrElse mimeType.Length > MaxMimeBytes Then Return False
        Dim slash As Integer = mimeType.IndexOf("/"c)
        If slash <= 0 OrElse slash = mimeType.Length - 1 OrElse slash <> mimeType.LastIndexOf("/"c) Then Return False
        For Each c As Char In mimeType
            If c <= " "c OrElse c > "~"c Then Return False
        Next
        Return True
    End Function

    ''' <summary>The members of a `clipboard-put` against item BW: `mimeType` required for `image` and `file` and absent
    ''' for `text`; `name` required for `file`, optional for `image`, absent for `text`; `size` at least 1.</summary>
    Public Shared Function IsValidPut(kind As String, size As Long, mimeType As String, name As String) As Boolean
        If size < 1 Then Return False
        Select Case kind
            Case ExchangeClipboardKinds.Text
                Return String.IsNullOrEmpty(mimeType) AndAlso String.IsNullOrEmpty(name)
            Case ExchangeClipboardKinds.Image
                Return IsValidMime(mimeType) AndAlso (String.IsNullOrEmpty(name) OrElse IsValidName(name))
            Case ExchangeClipboardKinds.File
                Return IsValidMime(mimeType) AndAlso IsValidName(name)
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>The name a SENDER puts on a file: the base name of what the person's file is called, cut to the byte
    ''' bound at a character boundary, or "" when nothing valid is left (the send is then refused on the device).</summary>
    Public Shared Function NameForSend(pathOrName As String) As String
        If String.IsNullOrEmpty(pathOrName) Then Return ""
        Dim name As String = pathOrName
        Dim cut As Integer = Math.Max(name.LastIndexOf("\"c), name.LastIndexOf("/"c))
        If cut >= 0 Then name = name.Substring(cut + 1)
        Dim sb As New StringBuilder(name.Length)
        For Each c As Char In name
            If Not Char.IsControl(c) Then sb.Append(c)
        Next
        name = sb.ToString()
        While name.Length > 0 AndAlso Encoding.UTF8.GetByteCount(name) > MaxNameBytes
            name = name.Substring(0, name.Length - 1)
            If name.Length > 0 AndAlso Char.IsHighSurrogate(name(name.Length - 1)) Then name = name.Substring(0, name.Length - 1)
        End While
        Return If(IsValidName(name), name, "")
    End Function

    Private Shared ReadOnly Reserved As String() = {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"}

    ''' <summary>The file name a RECEIVER saves under (item CB): the base name of `name` only, a character the file
    ''' system refuses replaced, a reserved device name and a trailing dot or blank made safe, and - when there is no
    ''' name (an `image` without one) - one made from `storedAt` and the MIME type. Never empty, never a path. The
    ''' caller still has to make it FREE (never overwrite an existing file): that is the file system's question.</summary>
    Public Shared Function ReceivedFileName(name As String, storedAtUtc As DateTime, mimeType As String) As String
        Dim baseName As String = If(name, "")
        Dim cut As Integer = Math.Max(baseName.LastIndexOf("\"c), baseName.LastIndexOf("/"c))
        If cut >= 0 Then baseName = baseName.Substring(cut + 1)
        Dim invalid As Char() = Path.GetInvalidFileNameChars()
        Dim sb As New StringBuilder(baseName.Length)
        For Each c As Char In baseName
            If Char.IsControl(c) OrElse Array.IndexOf(invalid, c) >= 0 Then
                sb.Append("_"c)
            Else
                sb.Append(c)
            End If
        Next
        baseName = sb.ToString().TrimEnd("."c, " "c).TrimStart(" "c)
        If baseName.Length = 0 OrElse baseName = "." OrElse baseName = ".." Then
            Dim stamp As DateTime = If(storedAtUtc = DateTime.MinValue, DateTime.UtcNow, storedAtUtc)
            baseName = "clipboard-" & stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) & ExtensionForMime(mimeType)
        End If
        Dim stem As String = Path.GetFileNameWithoutExtension(baseName)
        If Array.IndexOf(Reserved, stem.ToUpperInvariant()) >= 0 Then baseName = "_" & baseName
        ' Room for the " (2)" of a free name and for a long folder in front of it.
        If baseName.Length > 150 Then
            Dim ext As String = Path.GetExtension(baseName)
            If ext.Length > 20 Then ext = ""
            baseName = baseName.Substring(0, 150 - ext.Length) & ext
        End If
        Return baseName
    End Function

    ''' <summary>The extension of the pictures a clipboard holds, for a name made from a MIME type; ".bin" otherwise.</summary>
    Public Shared Function ExtensionForMime(mimeType As String) As String
        Select Case If(mimeType, "").Trim().ToLowerInvariant()
            Case "image/png" : Return ".png"
            Case "image/jpeg", "image/jpg" : Return ".jpg"
            Case "image/gif" : Return ".gif"
            Case "image/webp" : Return ".webp"
            Case "image/bmp" : Return ".bmp"
            Case "image/tiff" : Return ".tif"
            Case "image/heic" : Return ".heic"
            Case "text/plain" : Return ".txt"
            Case "application/pdf" : Return ".pdf"
            Case Else : Return ".bin"
        End Select
    End Function

End Class

''' <summary>One value of a clipboard as the server describes it (item BU): never content.</summary>
Friend NotInheritable Class ExchangeClipboardValue

    Public ReadOnly ClipId As String
    Public ReadOnly Size As Long
    Public ReadOnly Sha256 As String
    ''' <summary>The server's time as it sent it (UTC ISO 8601).</summary>
    Public ReadOnly StoredAt As String
    Public ReadOnly SourceDeviceId As String
    Public ReadOnly SourceDeviceName As String
    Public ReadOnly Kind As String
    Public ReadOnly MimeType As String
    Public ReadOnly Name As String

    Public Sub New(clipId As String, size As Long, sha256 As String, storedAt As String, sourceDeviceId As String,
                   sourceDeviceName As String, kind As String, mimeType As String, name As String)
        Me.ClipId = If(clipId, "")
        Me.Size = size
        Me.Sha256 = If(sha256, "")
        Me.StoredAt = If(storedAt, "")
        Me.SourceDeviceId = If(sourceDeviceId, "")
        Me.SourceDeviceName = If(sourceDeviceName, "")
        Me.Kind = If(kind, "")
        Me.MimeType = If(mimeType, "")
        Me.Name = If(name, "")
    End Sub

    ''' <summary>`StoredAt` as an instant, or <see cref="DateTime.MinValue"/> when the server sent none or one that does
    ''' not parse (such a value loses every comparison and is still readable).</summary>
    Public ReadOnly Property StoredAtUtc As DateTime
        Get
            Dim parsed As DateTimeOffset
            If DateTimeOffset.TryParse(StoredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal, parsed) Then
                Return parsed.UtcDateTime
            End If
            Return DateTime.MinValue
        End Get
    End Property

    ''' <summary>True when the members are usable: a known kind, a size of at least 1, a SHA-256 in hex.</summary>
    Public ReadOnly Property IsUsable As Boolean
        Get
            Return ExchangeClipboardKinds.IsKnown(Kind) AndAlso Size >= 1 AndAlso Sha256.Length = 64 AndAlso ClipId.Length > 0
        End Get
    End Property

    Friend Shared Function FromEnvelope(env As ExchangeEnvelope) As ExchangeClipboardValue
        Return New ExchangeClipboardValue(env.Text("clipId"), env.Number("size"), env.Text("sha256"), env.Text("storedAt"),
                                          env.Text("sourceDeviceId"), env.Text("sourceDeviceName"), env.Text("kind"),
                                          env.Text("mimeType"), env.Text("name"))
    End Function

    ''' <summary>The `value` record of a `clipboard-state`; Nothing when it is not an object of the shape of item BU.</summary>
    Friend Shared Function FromJson(rawJson As String) As ExchangeClipboardValue
        If String.IsNullOrEmpty(rawJson) Then Return Nothing
        Try
            Using doc As JsonDocument = JsonDocument.Parse(rawJson)
                Dim root As JsonElement = doc.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return Nothing
                Dim size As Long = 0
                Dim el As JsonElement
                If root.TryGetProperty("size", el) AndAlso el.ValueKind = JsonValueKind.Number Then
                    If Not el.TryGetInt64(size) Then size = 0
                End If
                Return New ExchangeClipboardValue(Str(root, "clipId"), size, Str(root, "sha256"), Str(root, "storedAt"),
                                                  Str(root, "sourceDeviceId"), Str(root, "sourceDeviceName"), Str(root, "kind"),
                                                  Str(root, "mimeType"), Str(root, "name"))
            End Using
        Catch ex As JsonException
            Return Nothing
        End Try
    End Function

    Private Shared Function Str(root As JsonElement, name As String) As String
        Dim el As JsonElement
        If root.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    ''' <summary>Never a member: the kind and the size class are what a log may carry (item CC).</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeClipboardValue " & Kind
    End Function

End Class

''' <summary>What one clipboard exchange with one server came to.</summary>
Friend Enum ExchangeClipboardOutcome
    ''' <summary>A put: `clipboard-stored` was read (and its hash is ours). A get: every byte arrived and the count and the
    ''' SHA-256 agree.</summary>
    Done = 0
    ''' <summary>The clipboard on the server holds nothing (`clipboard-empty`, or a `clipboard-state` without a value).</summary>
    Empty = 1
    ''' <summary>The server answered `refused`; <see cref="ExchangeClipboardResult.Refusal"/> says why.</summary>
    Refused = 2
    ''' <summary>The server does not list `clipboard`, or the account is a `guest`: nothing was sent to it.</summary>
    NotOffered = 3
    ''' <summary>A get whose `accept` declined the value (too large for the place it would go, a kind the caller cannot place).</summary>
    Declined = 4
    ''' <summary>A get: the byte count or the SHA-256 differs from what `clipboard-value` said. A put: the server's hash
    ''' is not the one of the bytes sent. Never placed, never reported as sent.</summary>
    Mismatch = 5
    ''' <summary>The device's own side failed: the source of a put ended early or could not be read, or the sink of a get
    ''' could not take the bytes (a full disk).</summary>
    LocalFailure = 6
    ''' <summary>The members of a put break item BW (nothing was opened).</summary>
    Invalid = 7
    ''' <summary>No stream, or it ended: <see cref="ExchangeClipboardResult.Open"/> says how.</summary>
    Transport = 8
End Enum

Friend NotInheritable Class ExchangeClipboardResult

    Public ReadOnly Outcome As ExchangeClipboardOutcome
    ''' <summary>The opening or stream outcome behind <see cref="ExchangeClipboardOutcome.Transport"/> and <see cref="ExchangeClipboardOutcome.Refused"/>.</summary>
    Public ReadOnly Open As ExchangeOpenOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>A put: `clipId`, `sha256` and `storedAt` of what was stored. A get or a state: the value. Else Nothing.</summary>
    Public ReadOnly Value As ExchangeClipboardValue

    Public Sub New(outcome As ExchangeClipboardOutcome, open As ExchangeOpenOutcome, refusal As ExchangeRefusal, value As ExchangeClipboardValue)
        Me.Outcome = outcome
        Me.Open = open
        Me.Refusal = refusal
        Me.Value = value
    End Sub

    Public ReadOnly Property IsDone As Boolean
        Get
            Return Outcome = ExchangeClipboardOutcome.Done
        End Get
    End Property

    ''' <summary>The wire reason of a refusal ("" when there is none or it is not one this client names).</summary>
    Public ReadOnly Property RefusalName As String
        Get
            Return ExchangeRefusals.WireName(Refusal)
        End Get
    End Property

    Friend Shared Function Plain(outcome As ExchangeClipboardOutcome, Optional value As ExchangeClipboardValue = Nothing) As ExchangeClipboardResult
        Return New ExchangeClipboardResult(outcome, ExchangeOpenOutcome.Connected, ExchangeRefusal.Unknown, value)
    End Function

    Friend Shared Function RefusedWith(refusal As ExchangeRefusal) As ExchangeClipboardResult
        Return New ExchangeClipboardResult(ExchangeClipboardOutcome.Refused, ExchangeOpenOutcome.Refused, refusal, Nothing)
    End Function

    Friend Shared Function FromOpen(open As ExchangeOpenResult) As ExchangeClipboardResult
        If open.Outcome = ExchangeOpenOutcome.Refused Then Return RefusedWith(open.Refusal)
        Return New ExchangeClipboardResult(ExchangeClipboardOutcome.Transport, open.Outcome, ExchangeRefusal.Unknown, Nothing)
    End Function

    Friend Shared Function Transported(open As ExchangeOpenOutcome) As ExchangeClipboardResult
        Return New ExchangeClipboardResult(ExchangeClipboardOutcome.Transport, open, ExchangeRefusal.Unknown, Nothing)
    End Function

    ''' <summary>The outcome only - a value's members are not a log line.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeClipboardResult " & Outcome.ToString()
    End Function

End Class

''' <summary>Opens the three clipboard exchanges of section 22 on top of <see cref="ExchangeSessions"/>.</summary>
Friend NotInheritable Class ExchangeClipboardClient

    Private Sub New()
    End Sub

    ''' <summary>No byte for this long ends the exchange, the server's own bound (item BW) held on this side too.</summary>
    Public Shared ReadOnly DefaultIdle As TimeSpan = TimeSpan.FromSeconds(60)

    Private Const BufferSize As Integer = 81920

    ''' <summary>Raised inside a put when the SOURCE failed, so it is not taken for a network failure.</summary>
    Private NotInheritable Class SourceFailure
        Inherits Exception
    End Class

    Private NotInheritable Class SinkFailure
        Inherits Exception
    End Class

    ''' <summary>False when the welcome does not carry the `clipboard` feature or names a `guest` account (item BV, CA).</summary>
    Friend Shared Function IsOffered(welcome As ExchangeEnvelope) As Boolean
        If welcome Is Nothing Then Return False
        If String.Equals(welcome.Text("accountKind"), "guest", StringComparison.Ordinal) Then Return False
        For Each feature As String In welcome.TextList("features")
            If String.Equals(feature, ExchangeWire.FeatureClipboard, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' Sends one value: `hello`, `clipboard-put`, `clipboard-ready`, exactly <paramref name="size"/> raw bytes read from
    ''' <paramref name="content"/>, `clipboard-stored`. The send is reported done only on that last frame (item BW). The
    ''' bytes are read once, in pieces; <paramref name="content"/> is not disposed here.
    ''' </summary>
    Public Shared Async Function PutAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                          creds As ExchangeDeviceCredentials, productVersion As String,
                                          kind As String, size As Long, mimeType As String, name As String,
                                          content As Stream, ct As CancellationToken,
                                          Optional idle As TimeSpan = Nothing) As Task(Of ExchangeClipboardResult)
        If Not ExchangeClipboardRules.IsValidPut(kind, size, mimeType, name) OrElse content Is Nothing Then
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Invalid)
        End If
        If idle <= TimeSpan.Zero Then idle = DefaultIdle

        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return ExchangeClipboardResult.FromOpen(session)
        Try
            If Not IsOffered(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.NotOffered)

            Await session.Writer.WriteAsync(ExchangeMessages.ClipboardPut(kind, size, mimeType, name), ct).ConfigureAwait(False)
            Dim ready As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If ready.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(ready.Refusal())
            If Not ready.Is(ExchangeEnvelopeTypes.ClipboardReady) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)

            Dim sha As String = Await SendBytesAsync(session.Stream, content, size, idle, ct).ConfigureAwait(False)

            Dim stored As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If stored.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(stored.Refusal())
            If Not stored.Is(ExchangeEnvelopeTypes.ClipboardStored) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            Dim value As ExchangeClipboardValue = New ExchangeClipboardValue(stored.Text("clipId"), size, stored.Text("sha256"), stored.Text("storedAt"),
                                                                            "", "", kind, mimeType, name)
            If Not String.Equals(stored.Text("sha256"), sha, StringComparison.OrdinalIgnoreCase) Then
                Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Mismatch, value)
            End If
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Done, value)
        Catch ex As SourceFailure
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.LocalFailure)
        Catch ex As OperationCanceledException
            Return ExchangeClipboardResult.Transported(If(ct.IsCancellationRequested, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.TimedOut))
        Catch ex As EndOfStreamException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Catch ex As ExchangeWireException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Finally
            session.Dispose()
        End Try
    End Function

    ''' <summary>What the server holds, without its bytes: `hello`, `clipboard-info`, `clipboard-state`. A result of
    ''' <see cref="ExchangeClipboardOutcome.Done"/> carries the value; an empty clipboard is
    ''' <see cref="ExchangeClipboardOutcome.Empty"/>. A guest account gets an empty state from the server (item BY).</summary>
    Public Shared Async Function StateAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                            creds As ExchangeDeviceCredentials, productVersion As String,
                                            ct As CancellationToken, Optional idle As TimeSpan = Nothing) As Task(Of ExchangeClipboardResult)
        If idle <= TimeSpan.Zero Then idle = DefaultIdle
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return ExchangeClipboardResult.FromOpen(session)
        Try
            If Not IsOffered(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.NotOffered)
            Await session.Writer.WriteAsync(ExchangeMessages.ClipboardInfo(), ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(reply.Refusal())
            If Not reply.Is(ExchangeEnvelopeTypes.ClipboardState) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            Dim raw As String = reply.Record("value")
            If raw.Length = 0 Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Empty)
            Dim value As ExchangeClipboardValue = ExchangeClipboardValue.FromJson(raw)
            If value Is Nothing OrElse Not value.IsUsable Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Done, value)
        Catch ex As OperationCanceledException
            Return ExchangeClipboardResult.Transported(If(ct.IsCancellationRequested, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.TimedOut))
        Catch ex As EndOfStreamException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Catch ex As ExchangeWireException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Finally
            ' `bye` is courtesy: the stream is closed either way, and a failure to say it changes nothing.
            Try
                Await session.Writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Bye), CancellationToken.None).ConfigureAwait(False)
            Catch
            End Try
            session.Dispose()
        End Try
    End Function

    ''' <summary>
    ''' Takes the value: `hello`, `clipboard-get` (with <paramref name="clipId"/> when one is wanted, so a value the person
    ''' was told about is never swapped for a newer one), `clipboard-value`, exactly `size` raw bytes. <paramref name="accept"/>
    ''' sees the members (never bytes) and returns the stream the bytes go to - or Nothing to decline, which closes the
    ''' stream before a byte is read. The sink stays the caller's: it is flushed, not closed, and on any outcome other
    ''' than <see cref="ExchangeClipboardOutcome.Done"/> what it holds is to be discarded.
    ''' </summary>
    Public Shared Async Function GetAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                          creds As ExchangeDeviceCredentials, productVersion As String, clipId As String,
                                          accept As Func(Of ExchangeClipboardValue, Stream), ct As CancellationToken,
                                          Optional idle As TimeSpan = Nothing) As Task(Of ExchangeClipboardResult)
        If idle <= TimeSpan.Zero Then idle = DefaultIdle
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return ExchangeClipboardResult.FromOpen(session)
        Try
            If Not IsOffered(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.NotOffered)
            Await session.Writer.WriteAsync(ExchangeMessages.ClipboardGet(clipId), ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(reply.Refusal())
            If reply.Is(ExchangeEnvelopeTypes.ClipboardEmpty) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Empty)
            If Not reply.Is(ExchangeEnvelopeTypes.ClipboardValue) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            Dim value As ExchangeClipboardValue = ExchangeClipboardValue.FromEnvelope(reply)
            If Not value.IsUsable Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)

            Dim sink As Stream = accept(value)
            If sink Is Nothing Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Declined, value)

            Dim got As Long = 0
            Dim sha As String = Await ReceiveBytesAsync(session.Stream, sink, value.Size, idle, ct, got).ConfigureAwait(False)
            If got < value.Size Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Mismatch, value)
            If Not String.Equals(sha, value.Sha256, StringComparison.OrdinalIgnoreCase) Then
                Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Mismatch, value)
            End If
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Done, value)
        Catch ex As SinkFailure
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.LocalFailure)
        Catch ex As OperationCanceledException
            Return ExchangeClipboardResult.Transported(If(ct.IsCancellationRequested, ExchangeOpenOutcome.Cancelled, ExchangeOpenOutcome.TimedOut))
        Catch ex As EndOfStreamException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Catch ex As ExchangeWireException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Closed)
        Finally
            session.Dispose()
        End Try
    End Function

    ''' <summary>The next known envelope, bounded by the idle time: a server that goes silent ends the exchange, it never
    ''' holds the person's action for good.</summary>
    Private Shared Async Function ReadWithIdleAsync(reader As ExchangeFrameReader, idle As TimeSpan, ct As CancellationToken) As Task(Of ExchangeEnvelope)
        Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            linked.CancelAfter(idle)
            Return Await ExchangeSessions.ReadKnownAsync(reader, linked.Token).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>Writes exactly <paramref name="size"/> bytes of <paramref name="content"/> and returns the SHA-256 (lower-case
    ''' hex) of what was written. A source that ends early or fails raises <see cref="SourceFailure"/>.</summary>
    Private Shared Async Function SendBytesAsync(stream As Stream, content As Stream, size As Long, idle As TimeSpan, ct As CancellationToken) As Task(Of String)
        Dim buffer(BufferSize - 1) As Byte
        Dim remaining As Long = size
        Using hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                While remaining > 0
                    Dim want As Integer = CInt(Math.Min(remaining, CLng(buffer.Length)))
                    Dim n As Integer
                    Try
                        n = Await content.ReadAsync(buffer, 0, want, ct).ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                        Throw New SourceFailure()
                    End Try
                    If n <= 0 Then Throw New SourceFailure()
                    hash.AppendData(buffer, 0, n)
                    linked.CancelAfter(idle)
                    Await stream.WriteAsync(buffer, 0, n, linked.Token).ConfigureAwait(False)
                    remaining -= n
                End While
                linked.CancelAfter(idle)
                Await stream.FlushAsync(linked.Token).ConfigureAwait(False)
            End Using
            Return ToHex(hash.GetHashAndReset())
        End Using
    End Function

    ''' <summary>Reads up to <paramref name="size"/> bytes into <paramref name="sink"/>. Returns the SHA-256 of what arrived;
    ''' <paramref name="got"/> is the count (smaller than size when the stream ended early). A sink that fails raises
    ''' <see cref="SinkFailure"/>.</summary>
    Private Shared Async Function ReceiveBytesAsync(stream As Stream, sink As Stream, size As Long, idle As TimeSpan,
                                                    ct As CancellationToken, ByRef got As Long) As Task(Of String)
        Dim buffer(BufferSize - 1) As Byte
        Using hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                While got < size
                    Dim want As Integer = CInt(Math.Min(size - got, CLng(buffer.Length)))
                    linked.CancelAfter(idle)
                    Dim n As Integer = Await stream.ReadAsync(buffer, 0, want, linked.Token).ConfigureAwait(False)
                    If n <= 0 Then Exit While
                    hash.AppendData(buffer, 0, n)
                    Try
                        Await sink.WriteAsync(buffer, 0, n, ct).ConfigureAwait(False)
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                        Throw New SinkFailure()
                    End Try
                    got += n
                End While
            End Using
            Try
                Await sink.FlushAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                Throw New SinkFailure()
            End Try
            Return ToHex(hash.GetHashAndReset())
        End Using
    End Function

    Private Shared Function ToHex(bytes As Byte()) As String
        Return Convert.ToHexString(bytes).ToLowerInvariant()
    End Function

End Class
#End If
