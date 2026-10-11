#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.Json

' Ticket 187 T-3 and rule 6. The ENTRIES of the viewer: any number of (server, login) pairs, each with its own device id, its own
' pin and its own token, so "resources from different users" and two logins on one server work (Ask 3 of the proposal; the
' `id-collision` of item G never fires, because no two entries share a device id).
'
'   - Server, login, pin, device id and the names are NOT secrets and live in ONE registry value (a JSON array) beside the
'     product's other settings. They are never added to the log package's allow-list (LogPackage.SafeSettingNames), and
'     LogPackage / HostingReport report the NUMBER of entries and their states, never a server, a login or a fingerprint (rule 11).
'   - The device token is a secret and lives under DPAPI in a folder of its own per entry
'     (%LOCALAPPDATA%\SZA\FastMediaSorter\exchange\entries\<entry id>\device-token.dat): ExchangeCredentialStore, unchanged.
'   - The identity of an entry is its address and its login (case-insensitive): a second add of the same identity re-enrolls the
'     existing entry - same device id, a rotated token - instead of creating a twin (the proposal of FastMediaSorter Android,
'     ask 2). Removing an entry removes its token and pin and NEVER an attached resource (9.3): those say "sign in again".
'   - Dormant (177 section 2): reading never creates a key, a value or a folder; with no entry nothing exists.
'
' This store is the VIEWER's. The Share Manager keeps its own single producer entry (ExchangeAccountStore): the two programs are two
' devices of one account, which is what lets the viewer list this PC's shared folders next to the other machines'.

''' <summary>One server the viewer holds an account on. Immutable.</summary>
Friend NotInheritable Class ExchangeEntry

    Public ReadOnly Id As String
    ''' <summary>host:port as <see cref="ExchangeServerAddress"/> writes it (an IPv6 literal in brackets), the host lower-cased.</summary>
    Public ReadOnly Server As String
    Public ReadOnly Login As String
    ''' <summary>The pin the user accepted ("" when the platform validated the certificate).</summary>
    Public ReadOnly Pin As String
    Public ReadOnly Certificate As ExchangeCertificateKind
    Public ReadOnly DeviceId As String
    Public ReadOnly DeviceName As String
    ''' <summary>The server's display name (from the string's `n`, or the user's): a hint, never an identity. May be "".</summary>
    Public ReadOnly Name As String
    ''' <summary>"Attach new resources of this account automatically": off by default, per entry (Q187-4).</summary>
    Public ReadOnly AutoAttach As Boolean
    ''' <summary>The ids of the resources the viewer has already seen listed under this entry while "attach new ones automatically" was on:
    ''' what makes a resource NEW. Bounded; ids only, no name and no access.</summary>
    Public ReadOnly Seen As IReadOnlyList(Of String)

    Friend Const MaxSeen As Integer = 256

    Public Sub New(id As String, server As String, login As String, pin As String, certificate As ExchangeCertificateKind,
                   deviceId As String, deviceName As String, name As String, autoAttach As Boolean, Optional seen As IReadOnlyList(Of String) = Nothing)
        Me.Id = If(id, "")
        Me.Server = If(server, "")
        Me.Login = If(login, "")
        Me.Pin = If(pin, "")
        Me.Certificate = certificate
        Me.DeviceId = If(deviceId, "")
        Me.DeviceName = If(deviceName, "")
        Me.Name = If(name, "")
        Me.AutoAttach = autoAttach
        Me.Seen = If(seen, Array.Empty(Of String)())
    End Sub

    ''' <summary>The name to show: the server's own, else its address.</summary>
    Public ReadOnly Property DisplayName As String
        Get
            Return If(Name.Length > 0, Name, Server)
        End Get
    End Property

    ''' <summary>The identity key: address and login, folded.</summary>
    Public ReadOnly Property IdentityKey As String
        Get
            Return ExchangeEntryStore.IdentityOf(Server, Login)
        End Get
    End Property

    Public Function TryGetAddress(ByRef address As ExchangeServerAddress) As Boolean
        Return ExchangeServerAddress.TryParse(Server, address)
    End Function

    Public Function WithPin(newPin As String, kind As ExchangeCertificateKind) As ExchangeEntry
        Return New ExchangeEntry(Id, Server, Login, newPin, kind, DeviceId, DeviceName, Name, AutoAttach, Seen)
    End Function

    Public Function WithAutoAttach(value As Boolean) As ExchangeEntry
        Return New ExchangeEntry(Id, Server, Login, Pin, Certificate, DeviceId, DeviceName, Name, value, Seen)
    End Function

    ''' <summary>The seen set, bounded (the newest <see cref="MaxSeen"/> ids).</summary>
    Public Function WithSeen(ids As IEnumerable(Of String)) As ExchangeEntry
        Dim list As New List(Of String)(If(ids, Array.Empty(Of String)()))
        If list.Count > MaxSeen Then list = list.GetRange(list.Count - MaxSeen, MaxSeen)
        Return New ExchangeEntry(Id, Server, Login, Pin, Certificate, DeviceId, DeviceName, Name, AutoAttach, list)
    End Function

    Public Function WithName(value As String) As ExchangeEntry
        Return New ExchangeEntry(Id, Server, Login, Pin, Certificate, DeviceId, DeviceName, value, AutoAttach, Seen)
    End Function

    Public Function WithAddress(newServer As String) As ExchangeEntry
        Return New ExchangeEntry(Id, newServer, Login, Pin, Certificate, DeviceId, DeviceName, Name, AutoAttach, Seen)
    End Function

    ''' <summary>The server, the login and the fingerprint are not secrets, but the text still names no member.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeEntry"
    End Function

End Class

''' <summary>The entries of this Windows user, in one registry value. A pure read creates nothing.</summary>
Friend NotInheritable Class ExchangeEntryStore

    ''' <summary>The value this store owns, so a test can assert that a dormant run wrote none of it.</summary>
    Friend Const ValueName As String = "Exchange_ViewerEntries"
    Friend Shared ReadOnly AllNames As String() = {ValueName}

    ''' <summary>Few enough that a directory view of all of them stays an answer, not a flood.</summary>
    Friend Const MaxEntries As Integer = 16
    Private Const MaxJsonChars As Integer = 400000
    Private Const MaxNameChars As Integer = 64

    Private ReadOnly _port As IExchangeSettingsPort
    Private ReadOnly _tokenRoot As String
    Private ReadOnly _cipher As IExchangeSecretCipher

    Public Sub New(port As IExchangeSettingsPort, Optional tokenRoot As String = Nothing, Optional cipher As IExchangeSecretCipher = Nothing)
        _port = If(port, New ExchangeRegistrySettings())
        _tokenRoot = If(String.IsNullOrWhiteSpace(tokenRoot), Path.Combine(ExchangeCredentialStore.DefaultDirectory(), "entries"), tokenRoot)
        _cipher = If(cipher, ExchangeDpapiCipher.Instance)
    End Sub

    Public Shared Function CreateDefault() As ExchangeEntryStore
        Return New ExchangeEntryStore(New ExchangeRegistrySettings())
    End Function

    ''' <summary>The identity key of an address and a login: the host lower-cased, the login lower-cased.</summary>
    Public Shared Function IdentityOf(server As String, login As String) As String
        Dim address As ExchangeServerAddress = Nothing
        Dim s As String = If(server, "").Trim().ToLowerInvariant()
        If ExchangeServerAddress.TryParse(server, address) Then s = New ExchangeServerAddress(address.Host.ToLowerInvariant(), address.Port).ToString()
        Return s & "|" & If(login, "").Trim().ToLowerInvariant()
    End Function

    ''' <summary>The token store of one entry: a folder of its own, so removing the entry removes its token and nothing else.</summary>
    Public Function TokenStoreFor(entryId As String) As ExchangeCredentialStore
        If Not IsValidEntryId(entryId) Then Throw New ArgumentException("entry id", NameOf(entryId))
        Return New ExchangeCredentialStore(Path.Combine(_tokenRoot, entryId), _cipher)
    End Function

    Public Shared Function NewEntryId() As String
        Return Guid.NewGuid().ToString("N")
    End Function

    Public Shared Function IsValidEntryId(id As String) As Boolean
        If id Is Nothing OrElse id.Length <> 32 Then Return False
        For Each c As Char In id
            If Not ((c >= "0"c AndAlso c <= "9"c) OrElse (c >= "a"c AndAlso c <= "f"c)) Then Return False
        Next
        Return True
    End Function

    ' ---- reading --------------------------------------------------------------------------------------------

    ''' <summary>Every valid entry, in the order they were added. A damaged element is skipped, not an error; a value that does
    ''' not parse is "no entries". A pure read.</summary>
    Public Function Load() As List(Of ExchangeEntry)
        Dim result As New List(Of ExchangeEntry)
        Dim json As String = _port.GetValue(ValueName)
        If json.Length = 0 OrElse json.Length > MaxJsonChars Then Return result
        Try
            Using doc As JsonDocument = JsonDocument.Parse(json)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return result
                Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
                For Each item As JsonElement In doc.RootElement.EnumerateArray()
                    If result.Count >= MaxEntries Then Exit For
                    Dim e As ExchangeEntry = ReadEntry(item)
                    If e Is Nothing Then Continue For
                    If Not seen.Add(e.Id) OrElse Not seen.Add(e.DeviceId) Then Continue For
                    result.Add(e)
                Next
            End Using
        Catch ex As JsonException
            Return New List(Of ExchangeEntry)
        End Try
        Return result
    End Function

    Public Function Count() As Integer
        Return Load().Count
    End Function

    Public Function Find(id As String) As ExchangeEntry
        For Each e As ExchangeEntry In Load()
            If String.Equals(e.Id, id, StringComparison.Ordinal) Then Return e
        Next
        Return Nothing
    End Function

    Public Function FindByIdentity(server As String, login As String) As ExchangeEntry
        Dim key As String = IdentityOf(server, login)
        For Each e As ExchangeEntry In Load()
            If String.Equals(e.IdentityKey, key, StringComparison.Ordinal) Then Return e
        Next
        Return Nothing
    End Function

    Private Shared Function ReadEntry(item As JsonElement) As ExchangeEntry
        If item.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim id As String = ReadText(item, "id")
        Dim server As String = ReadText(item, "x")
        Dim login As String = ReadText(item, "login")
        Dim deviceId As String = ReadText(item, "deviceId")
        If Not IsValidEntryId(id) OrElse login.Length = 0 OrElse Not ExchangeIds.IsValidId(deviceId) Then Return Nothing
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(server, address) Then Return Nothing
        Dim pin As String = ReadText(item, "pin")
        If Not ExchangeTrust.IsFingerprint(pin) Then pin = ""
        Dim kind As ExchangeCertificateKind = ExchangeCertificateKind.None
        Select Case ReadText(item, "cert")
            Case "ca-signed" : kind = ExchangeCertificateKind.CaSigned
            Case "pinned" : kind = ExchangeCertificateKind.Pinned
        End Select
        Dim auto As Boolean = False
        Dim p As JsonElement
        If item.TryGetProperty("auto", p) AndAlso p.ValueKind = JsonValueKind.True Then auto = True
        Dim seen As New List(Of String)
        Dim sp As JsonElement
        If item.TryGetProperty("seen", sp) AndAlso sp.ValueKind = JsonValueKind.Array Then
            For Each s As JsonElement In sp.EnumerateArray()
                If s.ValueKind = JsonValueKind.String AndAlso seen.Count < ExchangeEntry.MaxSeen Then
                    Dim v As String = s.GetString()
                    If Not String.IsNullOrEmpty(v) AndAlso v.Length <= 64 Then seen.Add(v)
                End If
            Next
        End If
        Return New ExchangeEntry(id, address.ToString(), login, pin, kind, deviceId, ReadText(item, "deviceName"), ReadText(item, "name"), auto, seen)
    End Function

    Private Shared Function ReadText(item As JsonElement, name As String) As String
        Dim p As JsonElement
        If item.TryGetProperty(name, p) AndAlso p.ValueKind = JsonValueKind.String Then Return If(p.GetString(), "")
        Return ""
    End Function

    ' ---- writing --------------------------------------------------------------------------------------------

    ''' <summary>A fresh entry for an enrollment that has not happened yet: nothing is stored until <see cref="Upsert"/>. When
    ''' the identity is already held, that entry's id and device id are reused (a second add re-enrolls it).</summary>
    Public Function Propose(server As String, login As String, name As String, deviceName As String) As ExchangeEntry
        Dim existing As ExchangeEntry = FindByIdentity(server, login)
        Dim address As ExchangeServerAddress = Nothing
        Dim canonical As String = If(ExchangeServerAddress.TryParse(server, address), New ExchangeServerAddress(address.Host.ToLowerInvariant(), address.Port).ToString(), If(server, "").Trim())
        If existing IsNot Nothing Then
            Return New ExchangeEntry(existing.Id, canonical, existing.Login, existing.Pin, existing.Certificate, existing.DeviceId,
                                     If(If(deviceName, "").Length > 0, deviceName, existing.DeviceName), If(If(name, "").Length > 0, name, existing.Name), existing.AutoAttach, existing.Seen)
        End If
        Return New ExchangeEntry(NewEntryId(), canonical, If(login, "").Trim(), "", ExchangeCertificateKind.None, ExchangeIds.NewId(), deviceName, name, False)
    End Function

    ''' <summary>Adds the entry, or replaces the one with the same id. False when it would pass <see cref="MaxEntries"/> or its
    ''' identity belongs to ANOTHER entry (one entry per address and login).</summary>
    Public Function Upsert(entry As ExchangeEntry) As Boolean
        If entry Is Nothing OrElse Not IsValidEntryId(entry.Id) Then Return False
        Dim list As List(Of ExchangeEntry) = Load()
        For Each other As ExchangeEntry In list
            If Not String.Equals(other.Id, entry.Id, StringComparison.Ordinal) AndAlso
               (String.Equals(other.IdentityKey, entry.IdentityKey, StringComparison.Ordinal) OrElse String.Equals(other.DeviceId, entry.DeviceId, StringComparison.Ordinal)) Then
                Return False
            End If
        Next
        Dim at As Integer = list.FindIndex(Function(e) String.Equals(e.Id, entry.Id, StringComparison.Ordinal))
        If at >= 0 Then
            list(at) = entry
        Else
            If list.Count >= MaxEntries Then Return False
            list.Add(entry)
        End If
        Write(list)
        Return True
    End Function

    ''' <summary>Replaces only the pin: the one explicit act that lets a changed certificate in (item D).</summary>
    Public Function ReplacePin(id As String, pin As String, kind As ExchangeCertificateKind) As Boolean
        Dim e As ExchangeEntry = Find(id)
        If e Is Nothing Then Return False
        Return Upsert(e.WithPin(pin, kind))
    End Function

    ''' <summary>Forgets the entry and its token. An attached resource is NOT touched here (9.3): the caller leaves them in place.</summary>
    Public Function Remove(id As String) As Boolean
        Dim list As List(Of ExchangeEntry) = Load()
        Dim removed As Integer = list.RemoveAll(Function(e) String.Equals(e.Id, id, StringComparison.Ordinal))
        If removed = 0 Then Return False
        If list.Count = 0 Then
            _port.DeleteValue(ValueName)
        Else
            Write(list)
        End If
        Try
            TokenStoreFor(id).Delete()
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException
        End Try
        Return True
    End Function

    Private Sub Write(list As List(Of ExchangeEntry))
        Dim ms As New MemoryStream()
        Using w As New Utf8JsonWriter(ms)
            w.WriteStartArray()
            For Each e As ExchangeEntry In list
                w.WriteStartObject()
                w.WriteString("id", e.Id)
                w.WriteString("x", e.Server)
                w.WriteString("login", e.Login)
                w.WriteString("pin", e.Pin)
                w.WriteString("cert", If(e.Certificate = ExchangeCertificateKind.CaSigned, "ca-signed", If(e.Certificate = ExchangeCertificateKind.Pinned, "pinned", "")))
                w.WriteString("deviceId", e.DeviceId)
                w.WriteString("deviceName", ExchangeDeviceDescription.Clip(e.DeviceName, MaxNameChars))
                w.WriteString("name", ExchangeDeviceDescription.Clip(e.Name, MaxNameChars))
                w.WriteBoolean("auto", e.AutoAttach)
                If e.Seen.Count > 0 Then
                    w.WriteStartArray("seen")
                    For Each s As String In e.Seen
                        w.WriteStringValue(s)
                    Next
                    w.WriteEndArray()
                End If
                w.WriteEndObject()
            Next
            w.WriteEndArray()
        End Using
        _port.SetValue(ValueName, Encoding.UTF8.GetString(ms.ToArray()))
    End Sub

End Class

''' <summary>Ticket 187 remainder (the entries proposal's asks 2 and 7): what a client does when the server ANNOUNCES an address that
''' differs from the one the entry holds, and the string an entry can hand its server on with. Pure - nothing here touches the network, a
''' store or a log, and an address is only ever handed to the person who already holds this entry.
'''
'''   - Ask 2: a client keeps connecting to the address it holds; when `welcome.publicEndpoint` differs, it SHOWS the announced address and
'''     offers the in-place edit - it never switches silently. The edit keeps the device id, the token, the pin decision and the attachments
'''     (ExchangeEntry.WithAddress); a pin carries over only while the server presents the same leaf, and a different leaf is item D's
'''     explicit replacement (the `pin-mismatch` state, never a re-pin).
'''   - Ask 7 (optional): a client may hand a server on from an entry - `x` from the last announced endpoint (never a LAN address or a
'''     guess), `fp` exactly when the entry holds an item D pin, no login hint. It is not a secret and travels like the operator's.
''' </summary>
Friend NotInheritable Class ExchangeEntryMove

    Private Sub New()
    End Sub

    ''' <summary>The announced address of a welcome in canonical form (the host lower-cased): "" when the value is not an address, is
    ''' loopback or unspecified (a server misconfigured for its clients), or is the address the entry already holds.</summary>
    Public Shared Function Announced(entryServer As String, publicEndpoint As String) As String
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(If(publicEndpoint, ""), address) OrElse IsLoopbackOrUnspecified(address.Host) Then Return ""
        Dim held As ExchangeServerAddress = Nothing
        If ExchangeServerAddress.TryParse(If(entryServer, ""), held) AndAlso
           String.Equals(held.Host.ToLowerInvariant(), address.Host.ToLowerInvariant(), StringComparison.Ordinal) AndAlso held.Port = address.Port Then Return ""
        Return New ExchangeServerAddress(address.Host.ToLowerInvariant(), address.Port).ToString()
    End Function

    ''' <summary>Ask 7: the connection string this entry can hand its server on with. "" when the server has announced no usable address.</summary>
    Public Shared Function HandOn(entry As ExchangeEntry, publicEndpoint As String) As String
        If entry Is Nothing Then Return ""
        Dim address As ExchangeServerAddress = Nothing
        If Not ExchangeServerAddress.TryParse(If(publicEndpoint, ""), address) OrElse IsLoopbackOrUnspecified(address.Host) Then Return ""
        Dim pin As String = If(entry.Certificate = ExchangeCertificateKind.Pinned AndAlso ExchangeTrust.IsFingerprint(entry.Pin), entry.Pin, "")
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(New ExchangeServerAddress(address.Host.ToLowerInvariant(), address.Port).ToString(), pin, entry.Name, "")
        If Not built.Succeeded Then Return ""
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Dim text As String = ExchangeConnectionString.Format(built.Value, refusal)
        Return If(text, "")
    End Function

    Private Shared Function IsLoopbackOrUnspecified(host As String) As Boolean
        Dim h As String = If(host, "").Trim().Trim("["c, "]"c)
        If h.Length = 0 Then Return False
        If h.Equals("localhost", StringComparison.OrdinalIgnoreCase) OrElse h.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) Then Return True
        Dim ip As System.Net.IPAddress = Nothing
        If Not System.Net.IPAddress.TryParse(h, ip) Then Return False
        If ip.IsIPv4MappedToIPv6 Then ip = ip.MapToIPv4()
        Return System.Net.IPAddress.IsLoopback(ip) OrElse ip.Equals(System.Net.IPAddress.Any) OrElse ip.Equals(System.Net.IPAddress.IPv6Any)
    End Function

    ''' <summary>An address is not a secret, but the text still names no member.</summary>
    Public Overrides Function ToString() As String
        Return "ExchangeEntryMove"
    End Function

End Class
#End If
