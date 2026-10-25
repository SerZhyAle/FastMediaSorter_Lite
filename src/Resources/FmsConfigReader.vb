#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.Json

''' <summary>Why a configuration was not accepted. Never carries the payload: only a kind and the
''' PATH of the offending field ("roots[2].virtualPath"), so a diagnostic cannot leak a secret.</summary>
Friend Enum FmsConfigFailure
    None = 0
    Empty = 1
    TooLarge = 2
    BadEncoding = 3
    UnknownTransport = 4
    BadBase64 = 5
    CompressedTooLarge = 6
    InflatedTooLarge = 7
    BadGzip = 8
    InvalidJson = 9
    NotAnObject = 10
    DuplicateProperty = 11
    MissingVersion = 12
    InvalidVersion = 13
    ''' <summary>schemaVersion above 2: the user is told to update the app; nothing is read further.</summary>
    UpdateRequired = 14
    UnsupportedProtocol = 15
    BadAccessPaths = 16
    NoUsableAccessPath = 17
    BadUsername = 18
    BadPassword = 19
    BadFingerprint = 20
    BadRoots = 21
    TooManyRoots = 22
    BadRoot = 23
    InvalidVirtualPath = 24
    FieldTooLong = 25
    WrongFieldType = 26
    DuplicateRoot = 27
End Enum

''' <summary>The transport the text arrived in.</summary>
Friend Enum FmsTransport
    PlainJson = 0
    CompressedQr = 1
End Enum

Friend Enum RootSupport
    ''' <summary>Everything the root asks for can be honoured.</summary>
    Supported = 0
    ''' <summary>A non-empty accessPin: the resource would be locked on the phone. No PIN is implemented
    ''' here, so the root can neither be imported as unlocked nor opened (100 Q100-1/Q100-4).</summary>
    RefusedPin = 1
End Enum

''' <summary>One shared folder of the document, with every v2 parameter mapped or marked unsupported.</summary>
Friend NotInheritable Class FmsRootPlan
    Friend Property VirtualPath As String = "/"
    Friend Property Label As String = ""
    ''' <summary>The wire value; absent means True (the restrictive default of the 2.1 amendment).</summary>
    Friend Property ReadOnlyRaw As Boolean = True
    Friend Property IsDestination As Boolean
    ''' <summary>Writable iff <c>readOnly == false OR isDestination == true</c>. It permits an
    ''' ATTEMPT; the server's own permission decides.</summary>
    Friend ReadOnly Property EffectiveWritable As Boolean
        Get
            Return (Not ReadOnlyRaw) OrElse IsDestination
        End Get
    End Property
    Friend Property Profile As String = ""
    Friend Property ProfileRecognised As Boolean
    Friend Property MediaTypes As New List(Of String)()
    Friend Property ScanSubdirectories As Boolean = True
    Friend Property ShowSubfoldersAsItems As Boolean
    Friend Property ShowHiddenFiles As Boolean?
    Friend Property AllFiles As Boolean?
    Friend Property DestinationColor As Integer?
    Friend Property Comment As String = ""
    ''' <summary>Positive values only; Nothing when absent or not positive.</summary>
    Friend Property SlideshowIntervalSeconds As Integer?
    Friend Property HasPin As Boolean
    Friend Property Support As RootSupport = RootSupport.Supported
    ''' <summary>Presentation values this reader preserves but does not apply (shown as a notice).</summary>
    Friend ReadOnly Property Notices As New List(Of String)()
End Class

Friend NotInheritable Class FmsEndpoint
    Friend Property Kind As String = ""
    Friend Property Host As String = ""
    Friend Property Port As Integer
    ''' <summary>A head start the earlier candidates get (ticket 171 rule 6: the tunnel is the last resort, so it waits). The resolver does not
    ''' dial this candidate before that many milliseconds of the round have passed - or before every earlier candidate has already failed,
    ''' since waiting then helps nobody. 0 (the default) is no stagger: every candidate of a document read from a file is dialled as before.</summary>
    Friend Property StartAfterMs As Integer
    Friend Function Key() As String
        Return Host.ToLowerInvariant() & ":" & Port.ToString(CultureInfo.InvariantCulture)
    End Function
End Class

''' <summary>An import, as a preview: roots with their support, the secrets it needs, no connection made.
''' Immutable in spirit; <see cref="ToString"/> never prints a secret.</summary>
Friend NotInheritable Class FmsConfigPlan
    Friend Property SchemaVersion As Integer
    Friend Property ResourceName As String = ""
    Friend ReadOnly Property Endpoints As New List(Of FmsEndpoint)()
    Friend Property UserName As String = ""
    ''' <summary>The credential, held only until it is protected. Never logged, never in an exception.</summary>
    Friend Property Password As String = ""
    Friend ReadOnly Property NeedsPassword As Boolean
        Get
            Return String.IsNullOrEmpty(Password)
        End Get
    End Property
    ''' <summary>Canonical <c>SHA256:..</c>, or "" for an unpinned resource (first-use acceptance is then required).</summary>
    Friend Property PinnedFingerprint As String = ""
    Friend ReadOnly Property IsPinned As Boolean
        Get
            Return PinnedFingerprint.Length > 0
        End Get
    End Property
    Friend Property AccessNote As String = ""
    ''' <summary>The exchange tunnel of the document (ticket 171, phase C4): read only where the exchange client features are offered, kept
    ''' beside the endpoints and NEVER among them (it is not an SSH address). Nothing for a document without one - which is every document
    ''' while the features are not offered.</summary>
    Friend Property Tunnel As FmsTunnelPath
    Friend ReadOnly Property Roots As New List(Of FmsRootPlan)()
    Friend Property Transport As FmsTransport

    ''' <summary>Whole-file refusal is the interim policy for a mixed file (Q100-1): true when every
    ''' root can be honoured.</summary>
    Friend ReadOnly Property CanCommitAll As Boolean
        Get
            Return Roots.All(Function(r) r.Support = RootSupport.Supported)
        End Get
    End Property

    Friend ReadOnly Property RefusedRoots As IEnumerable(Of FmsRootPlan)
        Get
            Return Roots.Where(Function(r) r.Support <> RootSupport.Supported)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "FmsConfigPlan(v" & SchemaVersion.ToString(CultureInfo.InvariantCulture) & ", " &
               Endpoints.Count.ToString(CultureInfo.InvariantCulture) & " endpoint(s), " &
               Roots.Count.ToString(CultureInfo.InvariantCulture) & " root(s), secrets hidden)"
    End Function
End Class

Friend NotInheritable Class FmsConfigReadResult
    Friend Property Plan As FmsConfigPlan
    Friend Property Failure As FmsConfigFailure = FmsConfigFailure.None
    ''' <summary>The field the failure is about ("" for a whole-document failure). Never a value.</summary>
    Friend Property FieldPath As String = ""
    ''' <summary>The one already-localized sentence that says what failed, set when the failure came from
    ''' a step before this reader (spec 227: the QR decoder's own no-code / several-codes / oversized
    ''' sentences). Empty when the generic mapping of <see cref="Failure"/> is the right sentence.</summary>
    Friend Property Detail As String = ""
    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Failure = FmsConfigFailure.None AndAlso Plan IsNot Nothing
        End Get
    End Property
End Class

''' <summary>
''' The one reader of FMSCFG payloads (contract <c>FMSCFG</c> 2.1, schemas 1 and 2) for every entry
''' surface - file, clipboard, QR text - so a rule cannot hold on one and not another (100 §15.5).
'''
''' Order, and why: bytes counted before any decoding; strict UTF-8; transport chosen by the prefix;
''' base64 and gzip BOUNDED before a decompressor exists (256 KiB in, 1 MiB out - inflation is aborted
''' at the byte past the limit, never ReadToEnd); JSON depth-limited; duplicate properties refused so
''' a conflicting security value cannot depend on a parser's last-wins; the schema version is judged
''' BEFORE any other field is read, because a newer document must be refused whole, never half-imported;
''' then every known field is validated and a malformed one refuses the whole envelope (nothing is
''' coerced to a more permissive value). It makes no connection, writes nothing and logs nothing.
'''
''' The limits are Windows resource guardrails (100 §15.3), not wire limits and not Android's.
''' </summary>
Friend Module FmsConfigReader

    Friend Const MaxPlainBytes As Integer = 1024 * 1024
    Friend Const MaxCompressedBytes As Integer = 256 * 1024
    Friend Const MaxInflatedBytes As Integer = 1024 * 1024
    Friend Const MaxJsonDepth As Integer = 32
    Friend Const MaxRoots As Integer = 100
    Friend Const MaxAccessPaths As Integer = 16
    Friend Const MaxStringUnits As Integer = 4096
    Friend Const MaxMediaTypes As Integer = 32
    Friend Const SupportedSchema As Integer = 2
    Friend Const QrPrefix As String = "FMSCFG1:"

    Private ReadOnly StrictUtf8 As New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False, throwOnInvalidBytes:=True)

    ''' <summary>The recognised resource presets; anything else is ignored, never fatal.</summary>
    Private ReadOnly KnownProfiles As New HashSet(Of String)(StringComparer.Ordinal) From {
        "none", "audio_library", "video_library", "photo_storage", "documents", "all_files"}
    Private ReadOnly KnownMediaTypes As New HashSet(Of String)(StringComparer.Ordinal) From {
        "image", "video", "audio", "gif", "text", "pdf", "epub", "office"}
    Private ReadOnly KnownAccessKinds As New HashSet(Of String)(StringComparer.Ordinal) From {"lan", "ipv6", "portforward"}

    ' ------------------------------------------------------------------ entry points ----

    ''' <summary>A file's bytes.</summary>
    Friend Function ReadBytes(source As Byte()) As FmsConfigReadResult
        If source Is Nothing OrElse source.Length = 0 Then Return Fail(FmsConfigFailure.Empty)
        ' The limit is on what arrived, before a single character is decoded.
        If source.Length > MaxPlainBytes Then Return Fail(FmsConfigFailure.TooLarge)
        Dim text As String
        Try
            text = StrictUtf8.GetString(source)
        Catch ex As DecoderFallbackException
            Return Fail(FmsConfigFailure.BadEncoding)
        End Try
        Return ReadCore(text)
    End Function

    ''' <summary>Clipboard or QR text.</summary>
    Friend Function ReadText(source As String) As FmsConfigReadResult
        If String.IsNullOrWhiteSpace(source) Then Return Fail(FmsConfigFailure.Empty)
        ' UTF-8 BYTES, counted without building them when the text is obviously small.
        If source.Length > MaxPlainBytes OrElse StrictUtf8.GetByteCount(source) > MaxPlainBytes Then Return Fail(FmsConfigFailure.TooLarge)
        Return ReadCore(source)
    End Function

    ' --------------------------------------------------------------------- pipeline ----

    Private Function ReadCore(rawText As String) As FmsConfigReadResult
        Dim text As String = rawText
        If text.Length > 0 AndAlso AscW(text(0)) = 65279 Then text = text.Substring(1) ' U+FEFF: ' a BOM is tolerated, never produced
        text = text.Trim()
        If text.Length = 0 Then Return Fail(FmsConfigFailure.Empty)

        Dim jsonBytes As Byte() = Nothing
        Dim transport As FmsTransport
        If text(0) = "{"c Then
            transport = FmsTransport.PlainJson
            jsonBytes = StrictUtf8.GetBytes(text)
        ElseIf text.StartsWith(QrPrefix, StringComparison.Ordinal) Then
            transport = FmsTransport.CompressedQr
            Dim inflated As FmsConfigReadResult = Inflate(text.Substring(QrPrefix.Length), jsonBytes)
            If inflated IsNot Nothing Then Return inflated
        Else
            Return Fail(FmsConfigFailure.UnknownTransport)
        End If

        Dim document As JsonDocument
        Try
            document = JsonDocument.Parse(jsonBytes, New JsonDocumentOptions With {
                .MaxDepth = MaxJsonDepth, .AllowTrailingCommas = False, .CommentHandling = JsonCommentHandling.Disallow})
        Catch ex As JsonException
            Return Fail(FmsConfigFailure.InvalidJson)
        End Try

        Using document
            Dim plan As New FmsConfigPlan With {.Transport = transport}
            Dim failure As FmsConfigReadResult = Parse(document.RootElement, plan)
            If failure IsNot Nothing Then Return failure
            Return New FmsConfigReadResult With {.Plan = plan}
        End Using
    End Function

    ''' <summary>Decodes the QR body. Returns Nothing on success (jsonBytes set), else the failure.</summary>
    Private Function Inflate(body As String, ByRef jsonBytes As Byte()) As FmsConfigReadResult
        ' Scanned payloads wrap and indent: every whitespace character is dropped first.
        Dim compact As New StringBuilder(body.Length)
        For Each ch As Char In body
            If Not Char.IsWhiteSpace(ch) Then compact.Append(ch)
        Next
        If compact.Length = 0 Then Return Fail(FmsConfigFailure.Empty)
        ' The compressed size is bounded BEFORE decoding: base64 is 4 characters per 3 bytes.
        If compact.Length > (MaxCompressedBytes \ 3 + 1) * 4 Then Return Fail(FmsConfigFailure.CompressedTooLarge)
        Dim compressed As Byte()
        Try
            compressed = Convert.FromBase64String(compact.ToString())
        Catch ex As FormatException
            Return Fail(FmsConfigFailure.BadBase64)
        End Try
        If compressed.Length > MaxCompressedBytes Then Return Fail(FmsConfigFailure.CompressedTooLarge)

        Using input As New MemoryStream(compressed, writable:=False)
            Using gzip As New GZipStream(input, CompressionMode.Decompress)
                Using output As New MemoryStream()
                    Dim buffer(8191) As Byte
                    Try
                        Dim read As Integer = gzip.Read(buffer, 0, buffer.Length)
                        While read > 0
                            ' Aborted as soon as the limit is passed: nothing is "read to the end first".
                            If output.Length + read > MaxInflatedBytes Then Return Fail(FmsConfigFailure.InflatedTooLarge)
                            output.Write(buffer, 0, read)
                            read = gzip.Read(buffer, 0, buffer.Length)
                        End While
                    Catch ex As InvalidDataException
                        Return Fail(FmsConfigFailure.BadGzip)
                    End Try
                    If output.Length = 0 Then Return Fail(FmsConfigFailure.Empty)
                    jsonBytes = output.ToArray()
                End Using
            End Using
        End Using
        Return Nothing
    End Function

    Private Function Fail(kind As FmsConfigFailure, Optional fieldPath As String = "") As FmsConfigReadResult
        Return New FmsConfigReadResult With {.Failure = kind, .FieldPath = fieldPath}
    End Function

    ' ------------------------------------------------------------------- the envelope ----

    Private Function Parse(root As JsonElement, plan As FmsConfigPlan) As FmsConfigReadResult
        If root.ValueKind <> JsonValueKind.Object Then Return Fail(FmsConfigFailure.NotAnObject)
        Dim dup As String = FindDuplicate(root, "")
        If dup IsNot Nothing Then Return Fail(FmsConfigFailure.DuplicateProperty, dup)

        ' 1. The version first, and nothing else is read from a newer document.
        Dim version As JsonElement
        If Not root.TryGetProperty("schemaVersion", version) Then Return Fail(FmsConfigFailure.MissingVersion, "schemaVersion")
        Dim versionValue As Integer
        If version.ValueKind <> JsonValueKind.Number OrElse Not version.TryGetInt32(versionValue) Then Return Fail(FmsConfigFailure.InvalidVersion, "schemaVersion")
        If versionValue <= 0 Then Return Fail(FmsConfigFailure.InvalidVersion, "schemaVersion")
        If versionValue > SupportedSchema Then Return Fail(FmsConfigFailure.UpdateRequired, "schemaVersion")
        plan.SchemaVersion = versionValue

        ' 2. protocol: exactly "sftp".
        Dim protocol As String = Nothing
        Dim protocolFailure As FmsConfigReadResult = ReadString(root, "protocol", "", True, protocol)
        If protocolFailure IsNot Nothing Then Return protocolFailure
        If Not String.Equals(protocol, "sftp", StringComparison.Ordinal) Then Return Fail(FmsConfigFailure.UnsupportedProtocol, "protocol")

        ' 3. identity and credential.
        Dim resourceName As String = Nothing
        Dim f As FmsConfigReadResult = ReadString(root, "resourceName", "", False, resourceName)
        If f IsNot Nothing Then Return f
        plan.ResourceName = resourceName

        Dim user As String = Nothing
        f = ReadString(root, "username", "", True, user)
        If f IsNot Nothing Then Return f
        If String.IsNullOrWhiteSpace(user) Then Return Fail(FmsConfigFailure.BadUsername, "username")
        plan.UserName = user

        Dim password As String = Nothing
        f = ReadString(root, "password", "", False, password)
        If f IsNot Nothing Then Return f
        plan.Password = password

        Dim note As String = Nothing
        f = ReadString(root, "accessNote", "", False, note)
        If f IsNot Nothing Then Return f
        plan.AccessNote = note

        ' 4. the pin: absent/empty = unpinned (explicit first-use acceptance later); malformed = refused.
        Dim fingerprint As String = Nothing
        f = ReadString(root, "hostKeyFingerprintSha256", "", False, fingerprint)
        If f IsNot Nothing Then Return f
        If fingerprint.Length > 0 Then
            Dim digest As Byte() = Nothing
            If Not SshFingerprint.TryParse(fingerprint, digest) Then Return Fail(FmsConfigFailure.BadFingerprint, "hostKeyFingerprintSha256")
            plan.PinnedFingerprint = fingerprint
        End If

        ' 5. how to reach it.
        f = ReadAccessPaths(root, plan)
        If f IsNot Nothing Then Return f

        ' 6. what it holds.
        Return ReadRoots(root, plan)
    End Function

    ''' <summary>Required or optional string, never coerced, never longer than 4096 UTF-16 units, never truncated.</summary>
    Private Function ReadString(owner As JsonElement, name As String, path As String, required As Boolean, ByRef value As String) As FmsConfigReadResult
        value = ""
        Dim element As JsonElement
        Dim where_ As String = If(path.Length = 0, name, path & "." & name)
        If Not owner.TryGetProperty(name, element) Then
            Return If(required, Fail(FmsConfigFailure.WrongFieldType, where_), Nothing)
        End If
        If element.ValueKind = JsonValueKind.Null AndAlso Not required Then Return Nothing
        If element.ValueKind <> JsonValueKind.String Then Return Fail(FmsConfigFailure.WrongFieldType, where_)
        Dim text As String = element.GetString()
        If text Is Nothing Then Return Fail(FmsConfigFailure.WrongFieldType, where_)
        If text.Length > MaxStringUnits Then Return Fail(FmsConfigFailure.FieldTooLong, where_)
        value = text
        Return Nothing
    End Function

    Private Function ReadBool(owner As JsonElement, name As String, path As String, ByRef present As Boolean, ByRef value As Boolean) As FmsConfigReadResult
        present = False
        Dim element As JsonElement
        If Not owner.TryGetProperty(name, element) Then Return Nothing
        If element.ValueKind = JsonValueKind.Null Then Return Nothing
        If element.ValueKind <> JsonValueKind.True AndAlso element.ValueKind <> JsonValueKind.False Then
            Return Fail(FmsConfigFailure.WrongFieldType, path & "." & name)
        End If
        present = True
        value = element.GetBoolean()
        Return Nothing
    End Function

    Private Function ReadInt(owner As JsonElement, name As String, path As String, ByRef present As Boolean, ByRef value As Integer) As FmsConfigReadResult
        present = False
        Dim element As JsonElement
        If Not owner.TryGetProperty(name, element) Then Return Nothing
        If element.ValueKind = JsonValueKind.Null Then Return Nothing
        If element.ValueKind <> JsonValueKind.Number OrElse Not element.TryGetInt32(value) Then
            Return Fail(FmsConfigFailure.WrongFieldType, path & "." & name)
        End If
        present = True
        Return Nothing
    End Function

    Private Function ReadAccessPaths(root As JsonElement, plan As FmsConfigPlan) As FmsConfigReadResult
        Dim paths As JsonElement
        If Not root.TryGetProperty("accessPaths", paths) OrElse paths.ValueKind <> JsonValueKind.Array Then Return Fail(FmsConfigFailure.BadAccessPaths, "accessPaths")
        ' Counted BEFORE unknown kinds are dropped: an envelope of a thousand dead entries is refused.
        If paths.GetArrayLength() > MaxAccessPaths Then Return Fail(FmsConfigFailure.BadAccessPaths, "accessPaths")

        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim index As Integer = 0
        For Each entry As JsonElement In paths.EnumerateArray()
            Dim here As String = "accessPaths[" & index.ToString(CultureInfo.InvariantCulture) & "]"
            index += 1
            If entry.ValueKind <> JsonValueKind.Object Then Return Fail(FmsConfigFailure.BadAccessPaths, here)
            Dim kindElement As JsonElement
            Dim kind As String = ""
            If entry.TryGetProperty("kind", kindElement) AndAlso kindElement.ValueKind = JsonValueKind.String Then kind = kindElement.GetString()
            ' The exchange's own kind (ticket 171 C4) is taken - or skipped, never dialled - by the one module that knows it.
            If FmsTunnelPaths.TryTake(entry, kind, plan) Then Continue For
            ' An unknown (or missing) kind is skipped without being dialled or even validated.
            If Not KnownAccessKinds.Contains(kind) Then Continue For

            Dim host As String = Nothing
            Dim failure As FmsConfigReadResult = ReadString(entry, "host", here, True, host)
            If failure IsNot Nothing Then Return failure
            If String.IsNullOrWhiteSpace(host) OrElse host.Trim().Length <> host.Length Then Return Fail(FmsConfigFailure.BadAccessPaths, here & ".host")
            For Each ch As Char In host
                If AscW(ch) < &H21 OrElse AscW(ch) = &H7F OrElse ch = "/"c OrElse ch = "\"c OrElse ch = "@"c Then Return Fail(FmsConfigFailure.BadAccessPaths, here & ".host")
            Next
            Dim port As Integer
            Dim portPresent As Boolean
            failure = ReadInt(entry, "port", here, portPresent, port)
            If failure IsNot Nothing Then Return failure
            If Not portPresent OrElse port < 1 OrElse port > 65535 Then Return Fail(FmsConfigFailure.BadAccessPaths, here & ".port")

            Dim endpoint As New FmsEndpoint With {.Kind = kind, .Host = host, .Port = port}
            If seen.Add(endpoint.Key()) Then plan.Endpoints.Add(endpoint) ' producer order is preserved; duplicates collapse
        Next
        If plan.Endpoints.Count = 0 Then Return Fail(FmsConfigFailure.NoUsableAccessPath, "accessPaths")
        Return Nothing
    End Function

    Private Function ReadRoots(root As JsonElement, plan As FmsConfigPlan) As FmsConfigReadResult
        Dim roots As JsonElement
        If Not root.TryGetProperty("roots", roots) OrElse roots.ValueKind <> JsonValueKind.Array Then Return Fail(FmsConfigFailure.BadRoots, "roots")
        Dim count As Integer = roots.GetArrayLength()
        If count = 0 Then Return Fail(FmsConfigFailure.BadRoots, "roots")
        If count > MaxRoots Then Return Fail(FmsConfigFailure.TooManyRoots, "roots")

        Dim seenPaths As New HashSet(Of String)(StringComparer.Ordinal) ' remote paths are case-sensitive
        Dim index As Integer = 0
        For Each element As JsonElement In roots.EnumerateArray()
            Dim here As String = "roots[" & index.ToString(CultureInfo.InvariantCulture) & "]"
            index += 1
            If element.ValueKind <> JsonValueKind.Object Then Return Fail(FmsConfigFailure.BadRoot, here)

            Dim virtualPath As String = Nothing
            Dim failure As FmsConfigReadResult = ReadString(element, "virtualPath", here, True, virtualPath)
            If failure IsNot Nothing Then Return failure
            Dim canonical As String = CanonicalVirtualPath(virtualPath)
            If canonical Is Nothing Then Return Fail(FmsConfigFailure.InvalidVirtualPath, here & ".virtualPath")
            If Not seenPaths.Add(canonical) Then Return Fail(FmsConfigFailure.DuplicateRoot, here & ".virtualPath")

            Dim r As New FmsRootPlan With {.VirtualPath = canonical}
            Dim label As String = Nothing
            failure = ReadString(element, "label", here, False, label)
            If failure IsNot Nothing Then Return failure
            r.Label = label

            Dim present As Boolean
            Dim flag As Boolean
            failure = ReadBool(element, "readOnly", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.ReadOnlyRaw = If(present, flag, True) ' absent means read-only

            failure = ReadBool(element, "isDestination", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.IsDestination = present AndAlso flag

            Dim profile As String = Nothing
            failure = ReadString(element, "profile", here, False, profile)
            If failure IsNot Nothing Then Return failure
            r.Profile = profile
            r.ProfileRecognised = profile.Length = 0 OrElse KnownProfiles.Contains(profile)
            If Not r.ProfileRecognised Then r.Notices.Add("profile: unrecognised token ignored")

            failure = ReadMediaTypes(element, here, r)
            If failure IsNot Nothing Then Return failure

            failure = ReadBool(element, "scanSubdirectories", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.ScanSubdirectories = If(present, flag, True)
            failure = ReadBool(element, "showSubfoldersAsItems", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.ShowSubfoldersAsItems = present AndAlso flag
            If r.ShowSubfoldersAsItems Then r.Notices.Add("showSubfoldersAsItems: kept, not applied")
            failure = ReadBool(element, "showHiddenFiles", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.ShowHiddenFiles = If(present, CType(flag, Boolean?), Nothing)
            failure = ReadBool(element, "allFiles", here, present, flag)
            If failure IsNot Nothing Then Return failure
            r.AllFiles = If(present, CType(flag, Boolean?), Nothing)

            Dim number As Integer
            failure = ReadInt(element, "destinationColor", here, present, number)
            If failure IsNot Nothing Then Return failure
            If present Then r.DestinationColor = number
            failure = ReadInt(element, "slideshowInterval", here, present, number)
            If failure IsNot Nothing Then Return failure
            If present AndAlso number > 0 Then r.SlideshowIntervalSeconds = number

            Dim comment As String = Nothing
            failure = ReadString(element, "comment", here, False, comment)
            If failure IsNot Nothing Then Return failure
            r.Comment = comment

            Dim pin As String = Nothing
            failure = ReadString(element, "accessPin", here, False, pin)
            If failure IsNot Nothing Then Return failure
            r.HasPin = pin.Length > 0
            If r.HasPin Then r.Support = RootSupport.RefusedPin ' an empty PIN means no lock; a non-empty one is never ignored

            plan.Roots.Add(r)
        Next
        Return Nothing
    End Function

    Private Function ReadMediaTypes(element As JsonElement, here As String, r As FmsRootPlan) As FmsConfigReadResult
        Dim types As JsonElement
        If Not element.TryGetProperty("mediaTypes", types) OrElse types.ValueKind = JsonValueKind.Null Then Return Nothing
        If types.ValueKind <> JsonValueKind.Array Then Return Fail(FmsConfigFailure.WrongFieldType, here & ".mediaTypes")
        If types.GetArrayLength() > MaxMediaTypes Then Return Fail(FmsConfigFailure.FieldTooLong, here & ".mediaTypes")
        For Each item As JsonElement In types.EnumerateArray()
            If item.ValueKind <> JsonValueKind.String Then Return Fail(FmsConfigFailure.WrongFieldType, here & ".mediaTypes")
            Dim token As String = item.GetString()
            If token IsNot Nothing AndAlso token.Length <= MaxStringUnits AndAlso KnownMediaTypes.Contains(token) AndAlso Not r.MediaTypes.Contains(token) Then
                r.MediaTypes.Add(token)
            ElseIf token IsNot Nothing AndAlso Not KnownMediaTypes.Contains(token) Then
                r.Notices.Add("mediaTypes: unrecognised token ignored")
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>
    ''' A POSIX namespace path, not a Windows path: it must start with "/", contain no backslash, no NUL or
    ''' other control character, no ".." segment (decoded or not - this is a literal path, never
    ''' percent-decoded), at most 4096 units. "/" alone is valid. Case is kept: remote paths are case-
    ''' sensitive. A trailing slash is dropped (except for "/"); empty segments collapse nowhere - "//" is refused.
    ''' Returns Nothing when it is not acceptable.
    ''' </summary>
    Friend Function CanonicalVirtualPath(path As String) As String
        If path Is Nothing OrElse path.Length = 0 OrElse path.Length > MaxStringUnits Then Return Nothing
        If path(0) <> "/"c Then Return Nothing
        For Each ch As Char In path
            If ch = "\"c OrElse AscW(ch) < &H20 OrElse AscW(ch) = &H7F Then Return Nothing
        Next
        If path = "/" Then Return "/"
        Dim trimmed As String = If(path.EndsWith("/", StringComparison.Ordinal), path.Substring(0, path.Length - 1), path)
        Dim segments As String() = trimmed.Substring(1).Split("/"c)
        For Each segment As String In segments
            If segment.Length = 0 OrElse segment = ".." OrElse segment = "." Then Return Nothing
        Next
        Return trimmed
    End Function

    ''' <summary>The path of the first object that repeats a property name, or Nothing. Applied to every
    ''' object of the document, so a repeated "readOnly" or "accessPin" cannot depend on last-wins.</summary>
    Private Function FindDuplicate(element As JsonElement, path As String) As String
        Select Case element.ValueKind
            Case JsonValueKind.Object
                Dim names As New HashSet(Of String)(StringComparer.Ordinal)
                For Each prop As JsonProperty In element.EnumerateObject()
                    If Not names.Add(prop.Name) Then Return If(path.Length = 0, prop.Name, path & "." & prop.Name)
                    Dim inner As String = FindDuplicate(prop.Value, If(path.Length = 0, prop.Name, path & "." & prop.Name))
                    If inner IsNot Nothing Then Return inner
                Next
            Case JsonValueKind.Array
                Dim i As Integer = 0
                For Each item As JsonElement In element.EnumerateArray()
                    Dim inner As String = FindDuplicate(item, path & "[" & i.ToString(CultureInfo.InvariantCulture) & "]")
                    If inner IsNot Nothing Then Return inner
                    i += 1
                Next
        End Select
        Return Nothing
    End Function

End Module
#End If
