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
    ''' <summary>The kind of a sealed value (27 item DN): `size` counts the sealed bytes and `sealed` holds the box whose payload
    ''' carries the real kind, type and name. It is NOT one of the three kinds of <see cref="IsKnown"/>: a clear kind stays on the wire
    ''' only for a server or a sender without the capability, and a sealed value is placed by the sealed path alone.</summary>
    Public Const [Sealed] As String = "sealed"

    ''' <summary>The three clear kinds (item BU); false for <see cref="Sealed"/>.</summary>
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
    ''' <summary>The box of a sealed value (27 item DN) as one compact JSON object, "" for a clear value. It is the sender's
    ''' ciphertext and key ring, not a secret by itself, but it is never logged (item DQ).</summary>
    Public ReadOnly SealedJson As String

    Public Sub New(clipId As String, size As Long, sha256 As String, storedAt As String, sourceDeviceId As String,
                   sourceDeviceName As String, kind As String, mimeType As String, name As String, Optional sealedJson As String = "")
        Me.SealedJson = If(sealedJson, "")
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

    ''' <summary>True for a value of the kind `sealed` (27 item DN) - whatever else is wrong with it.</summary>
    Public ReadOnly Property IsSealedKind As Boolean
        Get
            Return String.Equals(Kind, ExchangeClipboardKinds.Sealed, StringComparison.Ordinal)
        End Get
    End Property

    ''' <summary>True when the members of a SEALED value are usable: the kind `sealed`, a box, a size of at least 1 (sealed bytes), a
    ''' SHA-256 in hex of those bytes and a clip id. Separate from <see cref="IsUsable"/>, which stays false for it: only the sealed
    ''' path may place such a value, and it opens the box first.</summary>
    Public ReadOnly Property IsUsableSealed As Boolean
        Get
            Return IsSealedKind AndAlso SealedJson.Length > 0 AndAlso Size >= 1 AndAlso Sha256.Length = 64 AndAlso ClipId.Length > 0
        End Get
    End Property

    Friend Shared Function FromEnvelope(env As ExchangeEnvelope) As ExchangeClipboardValue
        Return New ExchangeClipboardValue(env.Text("clipId"), env.Number("size"), env.Text("sha256"), env.Text("storedAt"),
                                          env.Text("sourceDeviceId"), env.Text("sourceDeviceName"), env.Text("kind"),
                                          env.Text("mimeType"), env.Text("name"), env.Record("sealed"))
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
                Dim box As String = ""
                Dim boxEl As JsonElement
                If root.TryGetProperty("sealed", boxEl) AndAlso boxEl.ValueKind = JsonValueKind.Object Then box = ExchangeJson.Compact(boxEl.GetRawText())
                Return New ExchangeClipboardValue(Str(root, "clipId"), size, Str(root, "sha256"), Str(root, "storedAt"),
                                                  Str(root, "sourceDeviceId"), Str(root, "sourceDeviceName"), Str(root, "kind"),
                                                  Str(root, "mimeType"), Str(root, "name"), box)
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
    ' The outcomes below belong to the sealed paths (27 items DN, DP, DR). They are the four states of item DR and the one benign
    ' case; none of them places anything.
    ''' <summary>A sealed put: the server does not list `sealed` (DR `server-no-e2e`). Nothing was sent - no clear fallback (item DP).
    ''' A get: the server holds a clear value and cannot hold a sealed one; it is not placed.</summary>
    ServerNoE2e = 9
    ''' <summary>A sealed put: no recipient holds an `e2eKey` (or this device holds none), DR `e2e-missing`. Nothing was sent.
    ''' A get: the value is a clear one, sent by a device without the capability; it is not placed (item DP).</summary>
    E2eMissing = 10
    ''' <summary>A sealed get: the sender's key is not the one this device kept for it (DR `key-changed`). Nothing was placed.</summary>
    KeyChanged = 11
    ''' <summary>A sealed get: a failed tag, a missing last segment, a size or SHA-256 that differs from the box, a box that does not
    ''' open for a reason other than the two above (DR `sealed-failed`). Everything was discarded, nothing was placed.</summary>
    SealedFailed = 12
    ''' <summary>A sealed get: this device is not in the box - it joined after the value was sent and reads the next one (item DN).
    ''' Not an error state.</summary>
    JoinedLater = 13
    ''' <summary>A CLEAR put or get against a server that lists `sealed` (item DP): nothing was sent and nothing was placed. The Share
    ''' Manager never asks for it; the guard is for a caller that forgets.</summary>
    ClearRefused = 14
End Enum

Friend NotInheritable Class ExchangeClipboardResult

    Public ReadOnly Outcome As ExchangeClipboardOutcome
    ''' <summary>The opening or stream outcome behind <see cref="ExchangeClipboardOutcome.Transport"/> and <see cref="ExchangeClipboardOutcome.Refused"/>.</summary>
    Public ReadOnly Open As ExchangeOpenOutcome
    Public ReadOnly Refusal As ExchangeRefusal
    ''' <summary>A put: `clipId`, `sha256` and `storedAt` of what was stored. A get or a state: the value. Else Nothing.</summary>
    Public ReadOnly Value As ExchangeClipboardValue
    ''' <summary>A sealed get that came to <see cref="ExchangeClipboardOutcome.Done"/> (and a JoinedLater/KeyChanged one never): the
    ''' kind, type, name, clear size and clear SHA-256 the box stated and the opened bytes matched. Nothing for any other result.</summary>
    Public ReadOnly Opened As ExchangeClipboardSealedPayload

    Public Sub New(outcome As ExchangeClipboardOutcome, open As ExchangeOpenOutcome, refusal As ExchangeRefusal, value As ExchangeClipboardValue,
                   Optional opened As ExchangeClipboardSealedPayload = Nothing)
        Me.Outcome = outcome
        Me.Open = open
        Me.Refusal = refusal
        Me.Value = value
        Me.Opened = opened
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

    Friend Shared Function Plain(outcome As ExchangeClipboardOutcome, Optional value As ExchangeClipboardValue = Nothing,
                                 Optional opened As ExchangeClipboardSealedPayload = Nothing) As ExchangeClipboardResult
        Return New ExchangeClipboardResult(outcome, ExchangeOpenOutcome.Connected, ExchangeRefusal.Unknown, value, opened)
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

''' <summary>What the sealed clipboard paths need from the device (27 items DG, DN, DP): its id, its key and the directory of the entry
''' the exchange goes to. The directory says WHO a value is sealed for (every Ready device of the entry, this one included, so the
''' sender reads back what it sent) and which sender key to hold a box to; the key opens the box addressed to this device.</summary>
Friend NotInheritable Class ExchangeClipboardSealing
    Implements IDisposable

    Public ReadOnly OwnDeviceId As String
    ''' <summary>This device's key; Nothing when it has none (nothing is sealed or opened then: `e2e-missing`).</summary>
    Public ReadOnly OwnKey As ExchangeDeviceKey
    ''' <summary>The recipients and kept keys of the entry; Nothing reads as an empty directory.</summary>
    Public ReadOnly Directory As IExchangeSealedDirectory
    Private ReadOnly _ownsKey As Boolean
    Private _release As Action

    ''' <param name="ownsKey">True when <see cref="Dispose"/> disposes the key (the caller loaded it for this one use).</param>
    ''' <param name="release">Runs once at <see cref="Dispose"/>: whatever kept the directory alive (a session opened for it).</param>
    Public Sub New(ownDeviceId As String, ownKey As ExchangeDeviceKey, directory As IExchangeSealedDirectory,
                   Optional ownsKey As Boolean = False, Optional release As Action = Nothing)
        Me.OwnDeviceId = If(ownDeviceId, "")
        Me.OwnKey = ownKey
        Me.Directory = directory
        _ownsKey = ownsKey
        _release = release
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dim release As Action = Interlocked.Exchange(_release, Nothing)
        If _ownsKey AndAlso OwnKey IsNot Nothing Then OwnKey.Dispose()
        If release IsNot Nothing Then release.Invoke()
    End Sub

    ''' <summary>The SHA-256 (lower-case hex) of the first <paramref name="size"/> bytes of <paramref name="content"/>, or Nothing when
    ''' the source ends early or cannot be read. A sealed put needs the clear SHA-256 BEFORE the box is built (the box states it), so a
    ''' value that is not already in memory is read twice: once here, once while it is sealed and sent. The caller rewinds or reopens it
    ''' between the two passes; a source that changed in between makes the send fail locally, never a wrong value on the server.</summary>
    Public Shared Async Function Sha256HexAsync(content As Stream, size As Long, ct As CancellationToken) As Task(Of String)
        If content Is Nothing OrElse size < 1 Then Return Nothing
        Dim buffer(ExchangeClipboardClient.HashBufferSize - 1) As Byte
        Dim remaining As Long = size
        Using hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            While remaining > 0
                Dim want As Integer = CInt(Math.Min(remaining, CLng(buffer.Length)))
                Dim n As Integer
                Try
                    n = Await content.ReadAsync(buffer, 0, want, ct).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                    Return Nothing
                End Try
                If n <= 0 Then Return Nothing
                hash.AppendData(buffer, 0, n)
                remaining -= n
            End While
            Return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
        End Using
    End Function

    ''' <summary>Never a member: no id, key or box reaches a log line (item CC).</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeClipboardSealing"
    End Function

End Class

''' <summary>Opens the three clipboard exchanges of section 22 on top of <see cref="ExchangeSessions"/>.</summary>
Friend NotInheritable Class ExchangeClipboardClient

    Private Sub New()
    End Sub

    ''' <summary>No byte for this long ends the exchange, the server's own bound (item BW) held on this side too.</summary>
    Public Shared ReadOnly DefaultIdle As TimeSpan = TimeSpan.FromSeconds(60)

    Private Const BufferSize As Integer = 81920
    ''' <summary>The piece a hashing pass reads (<see cref="ExchangeClipboardSealing.Sha256HexAsync"/>).</summary>
    Friend Const HashBufferSize As Integer = BufferSize

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

    ''' <summary>True when the welcome lists `sealed` (item DP): the server keeps and delivers the sealed clipboard kind.</summary>
    Friend Shared Function ListsSealed(welcome As ExchangeEnvelope) As Boolean
        If welcome Is Nothing Then Return False
        For Each feature As String In welcome.TextList("features")
            If String.Equals(feature, ExchangeSealedNames.FeatureSealed, StringComparison.Ordinal) Then Return True
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
            ' Item DP: no clear value through a server that can carry a sealed one. The Share Manager sends through PutSealedAsync only.
            If ListsSealed(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.ClearRefused)

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

    ''' <summary>
    ''' Sends one value SEALED (27 item DN): `hello`, `clipboard-put` of kind `sealed` (the sealed size, a box of the context `clipboard`
    ''' addressed to every Ready device of the entry and carrying the kind, type, name, clear size and clear SHA-256), `clipboard-ready`,
    ''' then the bytes of <paramref name="content"/> cut into 65536-byte segments and sealed AS THEY ARE READ - a large file is never
    ''' held whole - and `clipboard-stored`. Neither the kind nor the type nor the name is in clear on the wire.
    ''' The clear SHA-256 is stated by the box, so it is known before the first byte is sent: a value in memory is hashed by the caller,
    ''' a file by <see cref="ExchangeClipboardSealing.Sha256HexAsync"/> in a first pass (the caller then gives a source positioned at
    ''' its start). A source that differs in the second pass ends the send with <see cref="ExchangeClipboardOutcome.LocalFailure"/>; the
    ''' server then holds a value no receiver opens, and the next put replaces it.
    ''' Nothing is sent on <see cref="ExchangeClipboardOutcome.ServerNoE2e"/> (the welcome lists no `sealed`) or
    ''' <see cref="ExchangeClipboardOutcome.E2eMissing"/> (no recipient holds a key): there is no clear fallback (item DP).
    ''' </summary>
    Public Shared Async Function PutSealedAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                creds As ExchangeDeviceCredentials, productVersion As String,
                                                kind As String, mimeType As String, name As String, clearSize As Long, clearSha256Hex As String,
                                                content As Stream, sealing As ExchangeClipboardSealing, ct As CancellationToken,
                                                Optional idle As TimeSpan = Nothing) As Task(Of ExchangeClipboardResult)
        Dim payload As ExchangeClipboardSealedPayload = Nothing
        If content Is Nothing OrElse sealing Is Nothing OrElse Not ExchangeClipboardSealedPayload.TryCreate(kind, mimeType, name, clearSize, clearSha256Hex, payload) Then
            Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Invalid)
        End If
        If idle <= TimeSpan.Zero Then idle = DefaultIdle

        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return ExchangeClipboardResult.FromOpen(session)
        Dim valueKey As Byte() = Nothing
        Try
            If Not IsOffered(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.NotOffered)
            If Not ListsSealed(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.ServerNoE2e)

            Dim recipients As IReadOnlyList(Of ExchangeSealedRecipient) =
                If(sealing.Directory Is Nothing, Array.Empty(Of ExchangeSealedRecipient)(), sealing.Directory.Recipients(True))
            If sealing.OwnKey Is Nothing OrElse recipients.Count = 0 Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.E2eMissing)

            Dim payloadJson As Byte() = payload.ToJsonBytes()
            Dim sealResult As ExchangeSealResult = Nothing
            Try
                sealResult = ExchangeSealedBoxes.Seal(payloadJson, ExchangeSealedContext.Clipboard, sealing.OwnDeviceId, sealing.OwnKey, recipients, True)
            Finally
                Array.Clear(payloadJson, 0, payloadJson.Length)
            End Try
            Dim boxJson As String = ""
            Try
                If sealResult.Outcome = ExchangeSealOutcome.NoRecipients Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.E2eMissing)
                If Not sealResult.Succeeded Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.SealedFailed)
                valueKey = ExchangeClipboardSeal.ValueKey(sealResult.ContentKey)
                boxJson = sealResult.Box.ToJson()
            Finally
                sealResult.Wipe()
            End Try

            Dim sealedSize As Long = ExchangeClipboardSeal.SealedSize(clearSize)
            Await session.Writer.WriteAsync(ExchangeMessages.ClipboardPutSealed(sealedSize, boxJson), ct).ConfigureAwait(False)
            Dim ready As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If ready.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(ready.Refusal())
            If Not ready.Is(ExchangeEnvelopeTypes.ClipboardReady) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)

            Dim sentSha As String = ""
            Using sealingStream As New ExchangeClipboardSealingStream(content, valueKey, True)
                sentSha = Await SendBytesAsync(session.Stream, sealingStream, sealedSize, idle, ct).ConfigureAwait(False)
                ' The source must have ended exactly where the box says and hashed to what the box says.
                If Not sealingStream.IsComplete OrElse sealingStream.ClearSize <> clearSize OrElse
                   Not String.Equals(sealingStream.ClearSha256Hex, payload.Sha256, StringComparison.Ordinal) Then
                    Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.LocalFailure)
                End If
            End Using

            Dim stored As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If stored.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(stored.Refusal())
            If Not stored.Is(ExchangeEnvelopeTypes.ClipboardStored) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            ' The value as the sender knows it: the clear members for the line it shows, the SEALED hash the server computed.
            Dim value As ExchangeClipboardValue = New ExchangeClipboardValue(stored.Text("clipId"), clearSize, stored.Text("sha256"), stored.Text("storedAt"),
                                                                            "", "", kind, mimeType, name)
            If Not String.Equals(stored.Text("sha256"), sentSha, StringComparison.OrdinalIgnoreCase) Then
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
            If valueKey IsNot Nothing Then Array.Clear(valueKey, 0, valueKey.Length)
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
            ' A sealed value is described by its box and the SEALED size and hash; the state carries no clear member to show.
            If value Is Nothing OrElse Not (value.IsUsable OrElse value.IsUsableSealed) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            ' `bye` is courtesy: the stream is closed either way, and a failure to say it changes nothing.
            Try
                Await session.Writer.WriteAsync(ExchangeMessages.Simple(ExchangeEnvelopeTypes.Bye), ct).ConfigureAwait(False)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is ExchangeWireException OrElse TypeOf ex Is ObjectDisposedException
            End Try
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
            ' Item DP: a clear value is not placed when the server can carry a sealed one (the sender had no key, or is an old program).
            ' No byte is read and the stream is closed.
            If ListsSealed(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.ClearRefused, value)

            Dim sink As Stream = accept(value)
            If sink Is Nothing Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Declined, value)

            Dim recv = Await ReceiveBytesAsync(session.Stream, sink, value.Size, idle, ct).ConfigureAwait(False)
            Dim got As Long = recv.Got
            Dim sha As String = recv.Sha
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

    ''' <summary>
    ''' Takes the value and opens it (27 item DN): `hello`, `clipboard-get`, `clipboard-value`, then the sealed bytes. The box is opened
    ''' BEFORE a byte is read - it holds the key of the segments and states what the value is - and <paramref name="accept"/> sees the
    ''' opened members (kind, type, name, clear size: never bytes) and returns the sink the CLEAR bytes go to, or Nothing to decline
    ''' (the stream is closed before a byte is read). Every segment is opened as it arrives; a failed tag, a missing last segment, a
    ''' sealed size or SHA-256 that differs from the server's own statement (item BY) or a clear size or SHA-256 that differs from the
    ''' box discards everything: the sink holds nothing to keep and the outcome says why. The sink stays the caller's (flushed, not
    ''' closed); only <see cref="ExchangeClipboardOutcome.Done"/> means its content may be placed.
    ''' A box that does not list this device is <see cref="ExchangeClipboardOutcome.JoinedLater"/> (a device that joined after the value
    ''' was sent reads the next one): no error. A clear value is never placed (item DP): <see cref="ExchangeClipboardOutcome.ServerNoE2e"/>
    ''' on a server without `sealed`, <see cref="ExchangeClipboardOutcome.E2eMissing"/> on one that lists it.
    ''' </summary>
    Public Shared Async Function GetSealedAsync(transport As IExchangeTransport, server As ExchangeServerAddress, pin As String,
                                                creds As ExchangeDeviceCredentials, productVersion As String, clipId As String,
                                                sealing As ExchangeClipboardSealing,
                                                accept As Func(Of ExchangeClipboardSealedPayload, ExchangeClipboardValue, Stream),
                                                ct As CancellationToken, Optional idle As TimeSpan = Nothing) As Task(Of ExchangeClipboardResult)
        If sealing Is Nothing OrElse accept Is Nothing Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Invalid)
        If idle <= TimeSpan.Zero Then idle = DefaultIdle
        Dim session As ExchangeOpenResult = Await ExchangeSessions.HelloAsync(transport, server, pin, creds, productVersion, 0, ct).ConfigureAwait(False)
        If Not session.IsConnected Then Return ExchangeClipboardResult.FromOpen(session)
        Dim valueKey As Byte() = Nothing
        Try
            If Not IsOffered(session.Welcome) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.NotOffered)
            Await session.Writer.WriteAsync(ExchangeMessages.ClipboardGet(clipId), ct).ConfigureAwait(False)
            Dim reply As ExchangeEnvelope = Await ReadWithIdleAsync(session.Reader, idle, ct).ConfigureAwait(False)
            If reply.Is(ExchangeEnvelopeTypes.Refused) Then Return ExchangeClipboardResult.RefusedWith(reply.Refusal())
            If reply.Is(ExchangeEnvelopeTypes.ClipboardEmpty) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Empty)
            If Not reply.Is(ExchangeEnvelopeTypes.ClipboardValue) Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)
            Dim value As ExchangeClipboardValue = ExchangeClipboardValue.FromEnvelope(reply)
            If value.IsUsable Then
                ' A clear value (text, image, file): not placed, no byte read (item DP).
                Return ExchangeClipboardResult.Plain(If(ListsSealed(session.Welcome), ExchangeClipboardOutcome.E2eMissing, ExchangeClipboardOutcome.ServerNoE2e), value)
            End If
            If Not value.IsUsableSealed Then Return ExchangeClipboardResult.Transported(ExchangeOpenOutcome.Protocol)

            Dim box As ExchangeSealedBox = Nothing
            If Not ExchangeSealedBox.TryParse(value.SealedJson, box) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.SealedFailed, value)
            If sealing.OwnKey Is Nothing OrElse sealing.Directory Is Nothing Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.E2eMissing, value)

            Dim directory As IExchangeSealedDirectory = sealing.Directory
            Dim opened As ExchangeSealedOpenResult = ExchangeSealedBoxes.Open(box, sealing.OwnDeviceId, sealing.OwnKey, ExchangeSealedContext.Clipboard,
                                                                              Function(deviceId As String) directory.TrustedKeyOf(deviceId), True)
            Dim payload As ExchangeClipboardSealedPayload = Nothing
            Try
                Select Case opened.Outcome
                    Case ExchangeSealedOpenOutcome.Opened
                        ' on to the payload
                    Case ExchangeSealedOpenOutcome.NoEntry
                        Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.JoinedLater, value)
                    Case ExchangeSealedOpenOutcome.UntrustedFromKey
                        Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.KeyChanged, value)
                    Case Else
                        Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.SealedFailed, value)
                End Select
                If Not ExchangeClipboardSealedPayload.TryParse(opened.Payload, payload) Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.SealedFailed, value)
                valueKey = ExchangeClipboardSeal.ValueKey(opened.ContentKey)
            Finally
                opened.Wipe()
            End Try
            ' The sealed size is fixed by the clear size the box states; a server (or a forger) cannot make them disagree.
            If ExchangeClipboardSeal.SealedSize(payload.Size) <> value.Size Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.SealedFailed, value)

            Dim sink As Stream = accept(payload, value)
            If sink Is Nothing Then Return ExchangeClipboardResult.Plain(ExchangeClipboardOutcome.Declined, value)

            Dim outcome As ExchangeClipboardOutcome = Await ReceiveSealedAsync(session.Stream, sink, value, payload, valueKey, idle, ct).ConfigureAwait(False)
            Return ExchangeClipboardResult.Plain(outcome, value, If(outcome = ExchangeClipboardOutcome.Done, payload, Nothing))
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
            If valueKey IsNot Nothing Then Array.Clear(valueKey, 0, valueKey.Length)
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
                                                    ct As CancellationToken) As Task(Of (Sha As String, Got As Long))
        Dim got As Long = 0
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
            Return (ToHex(hash.GetHashAndReset()), got)
        End Using
    End Function

    ''' <summary>
    ''' Reads the sealed bytes of <paramref name="value"/> from <paramref name="stream"/>, opens them segment by segment and writes the
    ''' clear bytes to <paramref name="sink"/>. The opening stream reads its source synchronously and treats a short read as the end of the
    ''' value, so it is fed from a buffer that is topped up from the network (asynchronously) whenever it holds less than the two segments
    ''' one opening step can ask for, or the network has ended: it never waits on the network and never sees a false end.
    ''' </summary>
    Private Shared Async Function ReceiveSealedAsync(stream As Stream, sink As Stream, value As ExchangeClipboardValue,
                                                     payload As ExchangeClipboardSealedPayload, valueKey As Byte(), idle As TimeSpan,
                                                     ct As CancellationToken) As Task(Of ExchangeClipboardOutcome)
        Dim feed As New SealedFeed()
        Dim netBuffer(BufferSize - 1) As Byte
        Dim clearBuffer(BufferSize - 1) As Byte
        Dim received As Long = 0
        Dim netEnded As Boolean = False
        Using hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                Using opening As New ExchangeClipboardOpeningStream(feed, valueKey, True)
                    Try
                        While True
                            While Not netEnded AndAlso received < value.Size AndAlso feed.Available < 2 * ExchangeClipboardSeal.SealedSegmentSize
                                Dim want As Integer = CInt(Math.Min(Math.Min(value.Size - received, CLng(netBuffer.Length)), CLng(feed.Space)))
                                linked.CancelAfter(idle)
                                Dim n As Integer = Await stream.ReadAsync(netBuffer, 0, want, linked.Token).ConfigureAwait(False)
                                If n <= 0 Then
                                    netEnded = True
                                    Exit While
                                End If
                                hash.AppendData(netBuffer, 0, n)
                                feed.Append(netBuffer, n)
                                received += n
                            End While
                            Dim m As Integer = opening.Read(clearBuffer, 0, clearBuffer.Length)
                            If m <= 0 Then Exit While
                            Try
                                Await sink.WriteAsync(clearBuffer, 0, m, ct).ConfigureAwait(False)
                            Catch ex As OperationCanceledException
                                Throw
                            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                                Throw New SinkFailure()
                            End Try
                        End While
                    Catch ex As ExchangeSealedFailedException
                        ' The stream ended before the value did: a transfer that was cut, not a forged value. Otherwise the bytes are
                        ' not the ones the key made (a failed tag, a missing last segment). Either way everything is discarded.
                        Return If(netEnded, ExchangeClipboardOutcome.Mismatch, ExchangeClipboardOutcome.SealedFailed)
                    Finally
                        Array.Clear(clearBuffer, 0, clearBuffer.Length)
                    End Try

                    ' The opening is finished. Sealed bytes it did not need are not part of an honest value.
                    If received < value.Size Then Return If(netEnded, ExchangeClipboardOutcome.Mismatch, ExchangeClipboardOutcome.SealedFailed)
                    If feed.Available > 0 Then Return ExchangeClipboardOutcome.SealedFailed
                    ' Item BY: the sealed size and SHA-256 the server stated, then what the box states.
                    If Not String.Equals(ToHex(hash.GetHashAndReset()), value.Sha256, StringComparison.OrdinalIgnoreCase) Then Return ExchangeClipboardOutcome.Mismatch
                    If Not opening.IsComplete Then Return ExchangeClipboardOutcome.SealedFailed
                    If Not payload.Matches(opening.ClearSize, opening.ClearSha256Hex) Then Return ExchangeClipboardOutcome.SealedFailed
                End Using
            End Using
            Try
                Await sink.FlushAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ObjectDisposedException
                Throw New SinkFailure()
            End Try
            Return ExchangeClipboardOutcome.Done
        End Using
    End Function

    ''' <summary>The sealed bytes received but not yet opened: a bounded buffer the opening stream reads synchronously.</summary>
    Private NotInheritable Class SealedFeed
        Inherits Stream

        Private ReadOnly _buffer(2 * ExchangeClipboardSeal.SealedSegmentSize + BufferSize - 1) As Byte
        Private _start As Integer
        Private _end As Integer

        Public ReadOnly Property Available As Integer
            Get
                Return _end - _start
            End Get
        End Property

        ''' <summary>What <see cref="Append"/> can take now (after moving the unread bytes to the front).</summary>
        Public ReadOnly Property Space As Integer
            Get
                Return _buffer.Length - Available
            End Get
        End Property

        Public Sub Append(source As Byte(), count As Integer)
            If count > Space Then Throw New InvalidOperationException("sealed feed overflow")
            If _end + count > _buffer.Length Then
                System.Buffer.BlockCopy(_buffer, _start, _buffer, 0, Available)
                _end -= _start
                _start = 0
            End If
            System.Buffer.BlockCopy(source, 0, _buffer, _end, count)
            _end += count
        End Sub

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim n As Integer = Math.Min(count, Available)
            If n <= 0 Then Return 0
            System.Buffer.BlockCopy(_buffer, _start, buffer, offset, n)
            _start += n
            Return n
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then Array.Clear(_buffer, 0, _buffer.Length)
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

    Private Shared Function ToHex(bytes As Byte()) As String
        Return Convert.ToHexString(bytes).ToLowerInvariant()
    End Function

End Class
#End If
