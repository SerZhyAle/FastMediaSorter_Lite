#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.Encodings.Web
Imports System.Text.Json

''' <summary>Why a resource could not be handed over as a <c>.fmscfg</c> document (ticket 118 Ф-3,
''' the owner scope that closes Q100-2). Each kind has its own sentence in the export dialog and
''' is never folded into another: the reason is the whole point of refusing.</summary>
Friend Enum FmsExportRefusal
    None = 0
    ''' <summary>The document describes SFTP only; a Windows network folder cannot travel in it.</summary>
    Smb = 1
    ''' <summary>No pinned fingerprint: the pin is the property the receiver inherits, and without
    ''' it the import downgrades to first-use trust (Q118-6).</summary>
    NotPinned = 2
    ''' <summary>Every address was dropped by the reader's own rules; nothing usable remains.</summary>
    NoAddress = 3
    ''' <summary>The root path is not a virtual path the reader accepts.</summary>
    BadRoot = 4
End Enum

''' <summary>The outcome of one export. The JSON and the compressed code are held only here, in
''' memory, and are dropped with the object: they never reach a control's Text/Tag or a log.</summary>
Friend NotInheritable Class FmsExportResult
    Friend Sub New(refusal As FmsExportRefusal)
        Me.Refusal = refusal
    End Sub

    Friend Sub New(json As String, code As String)
        Me.Refusal = FmsExportRefusal.None
        Me.Json = json
        Me.Code = code
    End Sub

    Friend ReadOnly Property Refusal As FmsExportRefusal
    ''' <summary>Set only when the document itself failed its reader self-check (E14); the failure
    ''' NAME may be logged, never a value.</summary>
    Friend Property SelfCheckFailure As FmsConfigFailure = FmsConfigFailure.None
    ''' <summary>Plain JSON. "" unless the export succeeded.</summary>
    Friend ReadOnly Property Json As String = ""
    ''' <summary><c>FMSCFG1:</c> + base64(gzip(json)) - the transport for the clipboard's «Код».</summary>
    Friend ReadOnly Property Code As String = ""

    Friend ReadOnly Property Succeeded As Boolean
        Get
            Return Refusal = FmsExportRefusal.None AndAlso SelfCheckFailure = FmsConfigFailure.None AndAlso Json.Length > 0
        End Get
    End Property
End Class

''' <summary>
''' The viewer's <c>.fmscfg</c> producer (ticket 118 Ф-3): one registered SFTP resource to the
''' FMSCFG 2.2 document the Share Manager already ships, so a resource can be handed to another
''' computer or a phone. Pure and offline: no network call, no registry write, no log line - the
''' outcome is returned, and the caller decides what is said and where.
'''
''' The contract is consumed, never changed. Field order and spelling follow
''' <see cref="ShareConfigBuilder"/>-shaped producers: schemaVersion, resourceName, protocol,
''' accessPaths, username, password, hostKeyFingerprintSha256, roots, createdAt. The reader is the
''' judge: the finished document is validated through <see cref="FmsConfigReader.ReadText"/>
''' before it is offered, so an export this program itself would refuse is caught at the source.
''' Nothing that would carry a secret reaches the returned failure information.
''' </summary>
Friend Module FmsConfigExport

    Private ReadOnly JsonStringOpts As New JsonSerializerOptions With {.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping}

    ''' <summary>
    ''' Builds the document for one resource. <paramref name="includePassword"/> off (the default
    ''' button) writes <c>"password":""</c> - *not carried - the importer asks the user*; the same
    ''' empty password is written when the stored credential is missing or undecryptable, which is
    ''' never an error. <paramref name="createdAtUtc"/> exists for the byte-stable test; Date.MinValue
    ''' means the real clock.
    ''' </summary>
    Friend Function Export(descriptor As ResourceDescriptor, includePassword As Boolean,
                           secrets As IResourceSecretStore,
                           Optional createdAtUtc As Date = Nothing,
                           Optional unprotect As Func(Of String, SecretProtectionResult) = Nothing) As FmsExportResult
        If descriptor Is Nothing Then Return New FmsExportResult(FmsExportRefusal.BadRoot)
        ' The wire carries protocol "sftp" only; an SMB selection is refused before anything is built.
        If descriptor.Kind <> ResourceKind.Sftp Then Return New FmsExportResult(FmsExportRefusal.Smb)
        ' An exported document without a fingerprint would downgrade the receiver to first-use
        ' trust - the pin is what makes the document better than typing the address in.
        If String.IsNullOrEmpty(descriptor.PinnedFingerprint) Then Return New FmsExportResult(FmsExportRefusal.NotPinned)

        Dim paths As List(Of FmsEndpoint) = AccessPathsOf(descriptor)
        If paths.Count = 0 Then Return New FmsExportResult(FmsExportRefusal.NoAddress)

        Dim rootPath As String = FmsConfigReader.CanonicalVirtualPath(If(descriptor.RootPath, ""))
        If rootPath Is Nothing Then Return New FmsExportResult(FmsExportRefusal.BadRoot)

        Dim password As String = ""
        If includePassword Then
            Dim read As CredentialReadResult = ResourceCommit.ReadCredential(descriptor, secrets, unprotect)
            If read.Outcome = CredentialReadOutcome.Ok Then password = read.Secret
        End If

        ' Adaptive version rule: 2 only when the root carries a v2 field. The viewer emits at most
        ' one - scanSubdirectories:false; the import default (True/absent) stays out of the document.
        Dim scanSubdirectories As Boolean? = descriptor.IncludeSubfolders
        Dim anyV2 As Boolean = scanSubdirectories.HasValue AndAlso Not scanSubdirectories.Value

        Dim created As Date = If(createdAtUtc = Date.MinValue, DateTime.UtcNow, createdAtUtc)

        Dim sb As New StringBuilder()
        sb.Append("{""schemaVersion"":").Append(If(anyV2, "2", "1"))
        sb.Append(",""resourceName"":").Append(J(If(descriptor.DisplayName, "")))
        sb.Append(",""protocol"":""sftp""")
        sb.Append(",""accessPaths"":[").Append(String.Join(",", paths.Select(AddressOf EndpointJson)))
        sb.Append("]")
        sb.Append(",""username"":").Append(J(If(descriptor.UserName, "")))
        sb.Append(",""password"":").Append(J(password))
        sb.Append(",""hostKeyFingerprintSha256"":").Append(J(descriptor.PinnedFingerprint))
        sb.Append(",""roots"":[{""virtualPath"":").Append(J(rootPath))
        sb.Append(",""readOnly"":").Append(If(descriptor.ReadOnlyHint, "true", "false"))
        If anyV2 Then sb.Append(",""scanSubdirectories"":false")
        sb.Append("}]")
        sb.Append(",""createdAt"":").Append(J(created.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)))
        sb.Append("}"c)
        Dim json As String = sb.ToString()

        ' The reader is the judge: an export the viewer itself would refuse (a malformed stored
        ' fingerprint, a user name that is whitespace, a name past the reader's limits) is caught
        ' here and never offered. The failure NAME is all that travels out.
        Dim selfCheck As FmsConfigReadResult = FmsConfigReader.ReadText(json)
        If Not selfCheck.Succeeded Then
            Return New FmsExportResult(FmsExportRefusal.None) With {.SelfCheckFailure = selfCheck.Failure}
        End If
        Return New FmsExportResult(json, CompressedCode(json))
    End Function

    ''' <summary>The addresses in the producer's order <c>lan</c> -> <c>ipv6</c> -> <c>portforward</c>,
    ''' duplicates collapsed, every entry the reader would refuse dropped - an absent entry is honest,
    ''' a dead entry is a bug. A hand-added resource has no stored list: its single Host/Port becomes
    ''' its one address, of the Q118-10 kind, with port 0 spoken of as 22.</summary>
    Friend Function AccessPathsOf(descriptor As ResourceDescriptor) As List(Of FmsEndpoint)
        Dim result As New List(Of FmsEndpoint)()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        If descriptor.Endpoints IsNot Nothing AndAlso descriptor.Endpoints.Count > 0 Then
            For Each stored As ResourceEndpoint In descriptor.Endpoints.
                    Where(Function(e) EndpointRank(If(e.Kind, "")) >= 0).
                    OrderBy(Function(e) EndpointRank(If(e.Kind, ""))) ' stable: ties keep the stored order
                Dim host As String = BracketlessHost(stored.Host)
                Dim endpoint As New FmsEndpoint With {.Kind = stored.Kind, .Host = host, .Port = stored.Port}
                If Not ReaderWouldAccept(endpoint) Then Continue For
                If seen.Add(endpoint.Key()) Then result.Add(endpoint)
            Next
            Return result
        End If
        ' Hand-added: one address, the reader's own host rules applied after the bracket strip.
        Dim hand As New FmsEndpoint With {.Kind = EndpointKindForHost(descriptor.Host),
                                          .Host = BracketlessHost(descriptor.Host),
                                          .Port = If(descriptor.Port > 0, descriptor.Port, 22)}
        If ReaderWouldAccept(hand) Then result.Add(hand)
        Return result
    End Function

    ''' <summary>The Q118-10 kind of a hand-added address: a private/loopback/link-local IPv4 literal,
    ''' a single-label name or <c>*.local</c> is <c>lan</c>; an IPv6 literal is <c>ipv6</c>; anything
    ''' else can only be reached from outside, so it is <c>portforward</c>.</summary>
    Friend Function EndpointKindForHost(host As String) As String
        Dim value As String = BracketlessHost(If(host, ""))
        Dim bare As String = value
        Dim zone As Integer = value.IndexOf("%"c)
        If zone >= 0 Then bare = value.Substring(0, zone)
        If bare.IndexOf(":"c) >= 0 Then Return "ipv6"
        If IsLanLikeHost(bare) Then Return "lan"
        Return "portforward"
    End Function

    Private Function IsLanLikeHost(host As String) As Boolean
        If host.Length = 0 Then Return True
        If host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) Then Return True
        If host.IndexOf("."c) < 0 Then Return True ' a single-label name lives on the local network
        Dim address As IPAddress = Nothing
        If IPAddress.TryParse(host, address) AndAlso address.AddressFamily = AddressFamily.InterNetwork Then
            Dim bytes As Byte() = address.GetAddressBytes()
            If bytes.Length = 4 Then
                If bytes(0) = 10 OrElse bytes(0) = 127 Then Return True
                If bytes(0) = 172 AndAlso bytes(1) >= 16 AndAlso bytes(1) <= 31 Then Return True
                If bytes(0) = 192 AndAlso bytes(1) = 168 Then Return True
                If bytes(0) = 169 AndAlso bytes(1) = 254 Then Return True
            End If
        End If
        Return False
    End Function

    ''' <summary>An IPv6 literal may be stored bracketed: the brackets are a locator spelling, not part
    ''' of the host the reader validates.</summary>
    Private Function BracketlessHost(host As String) As String
        Dim value As String = If(host, "")
        If value.Length >= 2 AndAlso value.StartsWith("[", StringComparison.Ordinal) AndAlso value.EndsWith("]", StringComparison.Ordinal) Then
            Return value.Substring(1, value.Length - 2)
        End If
        Return value
    End Function

    Private Function EndpointRank(kind As String) As Integer
        Select Case kind
            Case "lan" : Return 0
            Case "ipv6" : Return 1
            Case "portforward" : Return 2
            Case Else : Return -1 ' an unknown kind is dropped, never guessed at
        End Select
    End Function

    ''' <summary>The reader's own rules for one accessPaths entry (FmsConfigReader.ReadAccessPaths),
    ''' applied to what is stored: an entry that would be refused there is dropped here.</summary>
    Private Function ReaderWouldAccept(endpoint As FmsEndpoint) As Boolean
        If endpoint Is Nothing Then Return False
        If String.IsNullOrWhiteSpace(endpoint.Host) OrElse endpoint.Host.Trim().Length <> endpoint.Host.Length Then Return False
        For Each ch As Char In endpoint.Host
            If AscW(ch) < &H21 OrElse AscW(ch) = &H7F OrElse ch = "/"c OrElse ch = "\"c OrElse ch = "@"c Then Return False
        Next
        Return endpoint.Port >= 1 AndAlso endpoint.Port <= 65535
    End Function

    Private Function EndpointJson(endpoint As FmsEndpoint) As String
        Return "{""kind"":" & J(endpoint.Kind) & ",""host"":" & J(endpoint.Host) &
               ",""port"":" & endpoint.Port.ToString(CultureInfo.InvariantCulture) & "}"
    End Function

    Private Function J(s As String) As String
        Return JsonSerializer.Serialize(If(s, ""), JsonStringOpts)
    End Function

    ''' <summary>The clipboard's «Код» transport: <c>FMSCFG1:</c> + base64(gzip(json)). The bytes of
    ''' the compression are a runtime detail - what the receiver reads is the JSON inside.</summary>
    Private Function CompressedCode(json As String) As String
        Dim raw As Byte() = Encoding.UTF8.GetBytes(json)
        Using output As New MemoryStream()
            Using gzip As New GZipStream(output, CompressionLevel.Optimal, leaveOpen:=True)
                gzip.Write(raw, 0, raw.Length)
            End Using
            Return FmsConfigReader.QrPrefix & Convert.ToBase64String(output.ToArray())
        End Using
    End Function

End Module
#End If
