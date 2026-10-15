#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Text.Json

' Ticket 180 T-1. The envelopes of DEVICE-EXCHANGE 7.2-7.8 as DATA: one table row per type, member names in
' the ORDER the encoder writes them (the table of ticket 161 section 4), their kind and which are required.
' Encode and decode both read the table, so a member added to the wire is one row, and the byte-stable
' member order is a property of the table, not of a hundred call sites.
'
' Records (a resource, a broadcast, a receiver block) travel as RAW JSON text: a newer client's extra members
' reach another newer client through this one untouched, at any depth (the server's rule 6 held on this side).
'
' Nothing here ever prints a member: an enroll holds a password, enrolled and hello a token, directory a
' resource's `access`. ToString names the type and nothing else.

Friend Enum ExchangeMemberKind
    ''' <summary>A JSON string.</summary>
    Text
    ''' <summary>A JSON integer.</summary>
    Number
    ''' <summary>A JSON boolean.</summary>
    Flag
    ''' <summary>An array of strings.</summary>
    TextList
    ''' <summary>One JSON object kept verbatim.</summary>
    Record
    ''' <summary>An array of JSON objects kept verbatim.</summary>
    RecordList
    ''' <summary>The `upserts` object of `changed`.</summary>
    Upserts
    ''' <summary>The `removals` object of `changed`.</summary>
    Removals
End Enum

''' <summary>When a member is written.</summary>
Friend Enum ExchangeOmit
    ''' <summary>Always written, an empty value included (a `[]`, a `0`).</summary>
    Never
    ''' <summary>Left out when empty or zero (Go's omitempty).</summary>
    WhenEmpty
    ''' <summary>Left out only when it was never set: present with 0 when set (a pointer in Go).</summary>
    WhenUnset
End Enum

Friend NotInheritable Class ExchangeMemberSpec
    Public ReadOnly Name As String
    Public ReadOnly Kind As ExchangeMemberKind
    Public ReadOnly Required As Boolean
    Public ReadOnly Omit As ExchangeOmit

    Public Sub New(name As String, kind As ExchangeMemberKind, required As Boolean, omit As ExchangeOmit)
        Me.Name = name
        Me.Kind = kind
        Me.Required = required
        Me.Omit = omit
    End Sub
End Class

''' <summary>The records of a `changed.upserts` (or a directory), each a raw JSON object.</summary>
Friend NotInheritable Class ExchangeRecordSets
    Public ReadOnly Property Devices As New List(Of String)
    Public ReadOnly Property Resources As New List(Of String)
    Public ReadOnly Property Broadcasts As New List(Of String)
End Class

''' <summary>The ids of a `changed.removals`.</summary>
Friend NotInheritable Class ExchangeIdSets
    Public ReadOnly Property DeviceIds As New List(Of String)
    Public ReadOnly Property ResourceIds As New List(Of String)
    Public ReadOnly Property BroadcastIds As New List(Of String)
End Class

''' <summary>What can be wrong with a frame or an envelope. Every one of them ends the stream; an unknown
''' TYPE and an unknown MEMBER are not errors (7.1).</summary>
Friend Enum ExchangeWireError
    ''' <summary>Not one JSON object, a member of the wrong shape, a required member missing.</summary>
    Malformed = 1
    ''' <summary>A `schemaVersion` other than 2 (the client has no reading of any other).</summary>
    Schema = 2
    ''' <summary>An object with no `type`.</summary>
    NoType = 3
    ''' <summary>A zero length, or one above the limit in force for the stream's state.</summary>
    FrameSize = 4
    ''' <summary>An envelope that cannot be encoded.</summary>
    BadMessage = 5
    ''' <summary>The stream ended inside a frame.</summary>
    Truncated = 6
End Enum

Friend NotInheritable Class ExchangeWireException
    Inherits Exception

    Public ReadOnly Property [Error] As ExchangeWireError

    Public Sub New(kind As ExchangeWireError, message As String)
        MyBase.New(message)
        [Error] = kind
    End Sub
End Class

''' <summary>One envelope: a type and the members its table row lists.</summary>
Friend NotInheritable Class ExchangeEnvelope

    Private ReadOnly _values As New Dictionary(Of String, Object)(StringComparer.Ordinal)

    Public Sub New(typeName As String)
        Type = typeName
    End Sub

    ''' <summary>The `type` as written or read.</summary>
    Public ReadOnly Property Type As String

    ''' <summary>False for a type this wire does not define: a reader ignores such an envelope (7.1).</summary>
    Public ReadOnly Property IsKnown As Boolean
        Get
            Return ExchangeEnvelopeTable.Find(Type) IsNot Nothing
        End Get
    End Property

    Public Function [Is](typeName As String) As Boolean
        Return String.Equals(Type, typeName, StringComparison.Ordinal)
    End Function

    ''' <summary>Never a member: see the class remark.</summary>
    Public Overrides Function ToString() As String
        Return "wire." & Type
    End Function

    Public Function Has(name As String) As Boolean
        Return _values.ContainsKey(name)
    End Function

    ' --- setters (chainable) ---

    Public Function SetText(name As String, value As String) As ExchangeEnvelope
        _values(name) = If(value, "")
        Return Me
    End Function

    Public Function SetNumber(name As String, value As Long) As ExchangeEnvelope
        _values(name) = value
        Return Me
    End Function

    Public Function SetFlag(name As String, value As Boolean) As ExchangeEnvelope
        _values(name) = value
        Return Me
    End Function

    Public Function SetTextList(name As String, values As IEnumerable(Of String)) As ExchangeEnvelope
        _values(name) = New List(Of String)(If(values, Array.Empty(Of String)()))
        Return Me
    End Function

    ''' <summary>A record as raw JSON text (one object).</summary>
    Public Function SetRecord(name As String, rawJson As String) As ExchangeEnvelope
        _values(name) = If(rawJson, "")
        Return Me
    End Function

    Public Function SetRecords(name As String, rawJson As IEnumerable(Of String)) As ExchangeEnvelope
        _values(name) = New List(Of String)(If(rawJson, Array.Empty(Of String)()))
        Return Me
    End Function

    Public Function SetUpserts(value As ExchangeRecordSets) As ExchangeEnvelope
        _values("upserts") = value
        Return Me
    End Function

    Public Function SetRemovals(value As ExchangeIdSets) As ExchangeEnvelope
        _values("removals") = value
        Return Me
    End Function

    ' --- getters ---

    Public Function Text(name As String) As String
        Dim v As Object = Nothing
        If _values.TryGetValue(name, v) Then
            Dim s As String = TryCast(v, String)
            If s IsNot Nothing Then Return s
        End If
        Return ""
    End Function

    Public Function Number(name As String) As Long
        Dim v As Object = Nothing
        If _values.TryGetValue(name, v) AndAlso TypeOf v Is Long Then Return DirectCast(v, Long)
        Return 0L
    End Function

    ''' <summary>The number, or Nothing when the member is absent.</summary>
    Public Function NumberOrNothing(name As String) As Long?
        Dim v As Object = Nothing
        If _values.TryGetValue(name, v) AndAlso TypeOf v Is Long Then Return DirectCast(v, Long)
        Return Nothing
    End Function

    Public Function Flag(name As String) As Boolean
        Dim v As Object = Nothing
        If _values.TryGetValue(name, v) AndAlso TypeOf v Is Boolean Then Return DirectCast(v, Boolean)
        Return False
    End Function

    Public Function TextList(name As String) As IReadOnlyList(Of String)
        Dim v As Object = Nothing
        If _values.TryGetValue(name, v) Then
            Dim l As List(Of String) = TryCast(v, List(Of String))
            If l IsNot Nothing Then Return l
        End If
        Return Array.Empty(Of String)()
    End Function

    ''' <summary>The raw JSON of a record member, or "" when absent.</summary>
    Public Function Record(name As String) As String
        Return Text(name)
    End Function

    Public Function Records(name As String) As IReadOnlyList(Of String)
        Return TextList(name)
    End Function

    Public Function Upserts() As ExchangeRecordSets
        Dim v As Object = Nothing
        If _values.TryGetValue("upserts", v) Then
            Dim s As ExchangeRecordSets = TryCast(v, ExchangeRecordSets)
            If s IsNot Nothing Then Return s
        End If
        Return New ExchangeRecordSets()
    End Function

    Public Function Removals() As ExchangeIdSets
        Dim v As Object = Nothing
        If _values.TryGetValue("removals", v) Then
            Dim s As ExchangeIdSets = TryCast(v, ExchangeIdSets)
            If s IsNot Nothing Then Return s
        End If
        Return New ExchangeIdSets()
    End Function

    ''' <summary>The `refused.reason` as an enum (Unknown for any value outside the nine).</summary>
    Public Function Refusal() As ExchangeRefusal
        Return ExchangeRefusals.Parse(Text("reason"))
    End Function

End Class

''' <summary>The table of ticket 161 section 4: every type, its members in wire order.</summary>
Friend NotInheritable Class ExchangeEnvelopeTable

    Private Sub New()
    End Sub

    Private Shared ReadOnly Rows As New Dictionary(Of String, ExchangeMemberSpec())(StringComparer.Ordinal)
    Private Shared ReadOnly Order As New List(Of String)

    Private Shared Function T(name As String, Optional required As Boolean = False, Optional omit As ExchangeOmit = ExchangeOmit.Never) As ExchangeMemberSpec
        Return New ExchangeMemberSpec(name, ExchangeMemberKind.Text, required, omit)
    End Function

    Private Shared Function N(name As String, Optional omit As ExchangeOmit = ExchangeOmit.Never) As ExchangeMemberSpec
        Return New ExchangeMemberSpec(name, ExchangeMemberKind.Number, False, omit)
    End Function

    Private Shared Function L(name As String, Optional omit As ExchangeOmit = ExchangeOmit.Never) As ExchangeMemberSpec
        Return New ExchangeMemberSpec(name, ExchangeMemberKind.TextList, False, omit)
    End Function

    Private Shared Function R(name As String, Optional required As Boolean = False, Optional omit As ExchangeOmit = ExchangeOmit.Never) As ExchangeMemberSpec
        Return New ExchangeMemberSpec(name, ExchangeMemberKind.Record, required, omit)
    End Function

    Private Shared Function RL(name As String) As ExchangeMemberSpec
        Return New ExchangeMemberSpec(name, ExchangeMemberKind.RecordList, False, ExchangeOmit.Never)
    End Function

    Private Shared Sub Row(typeName As String, ParamArray members As ExchangeMemberSpec())
        Rows(typeName) = members
        Order.Add(typeName)
    End Sub

    Shared Sub New()
        Const WhenEmpty As ExchangeOmit = ExchangeOmit.WhenEmpty
        ' --- opening a stream (7.2) ---
        Row("enroll", T("login", True), T("password", omit:=WhenEmpty), T("pairingCode", omit:=WhenEmpty),
            T("deviceId", True), T("deviceName", True), T("product", True), T("productVersion", omit:=WhenEmpty),
            T("platform", omit:=WhenEmpty), L("roles", WhenEmpty), L("capabilities", WhenEmpty), R("receiver", omit:=WhenEmpty),
            N("keepaliveSeconds", WhenEmpty))
        Row("enrolled", T("deviceToken", True), T("account"), T("accountKind", omit:=WhenEmpty), T("publicEndpoint"), N("keepaliveSeconds"),
            L("features"), T("serverVersion"))
        Row("hello", T("deviceId", True), T("deviceToken", True), T("productVersion", omit:=WhenEmpty),
            N("keepaliveSeconds", WhenEmpty))
        Row("welcome", T("account"), T("accountKind", omit:=WhenEmpty), T("publicEndpoint"), N("keepaliveSeconds"), L("features"), T("serverVersion"))
        Row("refused", T("reason", True))
        ' --- the control stream (7.3, 7.4) ---
        Row("keepalive")
        Row("bye")
        Row("list")
        Row("directory", N("revision"), RL("devices"), RL("resources"), RL("broadcasts"))
        Row("subscribe")
        Row("changed", N("revision"),
            New ExchangeMemberSpec("upserts", ExchangeMemberKind.Upserts, False, ExchangeOmit.Never),
            New ExchangeMemberSpec("removals", ExchangeMemberKind.Removals, False, ExchangeOmit.Never))
        Row("publish", R("resource", True))
        Row("published", T("resourceId", True), N("publicPort", ExchangeOmit.WhenUnset))
        Row("unpublish", T("resourceId", True))
        Row("broadcast-start", R("broadcast", True), N("maxListeners", ExchangeOmit.WhenUnset))
        Row("broadcast-started", T("broadcastId", True))
        Row("broadcast-update", R("broadcast", True), N("maxListeners", ExchangeOmit.WhenUnset))
        Row("broadcast-end", T("broadcastId", True))
        Row("pairing-request")
        Row("pairing-code", T("code", True), T("expiresAt"))
        Row("revoke", T("deviceId", True))
        ' --- tunnels (7.5) ---
        Row("connect", T("resourceId", omit:=WhenEmpty), T("shareId", omit:=WhenEmpty),
            T("broadcastId", omit:=WhenEmpty), T("scheme", omit:=WhenEmpty))
        Row("connected")
        Row("open", T("tunnelId", True), T("resourceId", omit:=WhenEmpty), T("shareId", omit:=WhenEmpty),
            T("broadcastId", omit:=WhenEmpty), T("scheme", omit:=WhenEmpty))
        Row("attach", T("tunnelId", True))
        ' --- the relay (7.6) ---
        Row("upload", T("broadcastId", True), T("contentType", True))
        Row("upload-accepted")
        ' --- cast (7.8) ---
        Row("cast", T("broadcastId", True), T("receiverDeviceId", True))
        Row("cast-offer", R("broadcast", True), T("fromDeviceId", True))
        Row("cast-answer", T("broadcastId", True),
            New ExchangeMemberSpec("accepted", ExchangeMemberKind.Flag, False, ExchangeOmit.Never),
            T("reason", omit:=WhenEmpty))
        Row("cast-result", T("broadcastId", True), T("receiverDeviceId", True),
            New ExchangeMemberSpec("accepted", ExchangeMemberKind.Flag, False, ExchangeOmit.Never),
            T("reason", omit:=WhenEmpty))
        Row("cast-stop", T("broadcastId", True), T("receiverDeviceId", True))
        ' --- ticket 224 account lifecycle: registration, passwords, deletion ---
        Row("register", T("invitationCode", omit:=WhenEmpty), T("sharedPassword", omit:=WhenEmpty),
            T("login", True), T("password", True), T("deviceId", True), T("deviceName", True),
            T("product", True), T("productVersion", omit:=WhenEmpty), T("platform", omit:=WhenEmpty),
            L("roles", WhenEmpty), L("capabilities", WhenEmpty), R("receiver", omit:=WhenEmpty),
            N("keepaliveSeconds", WhenEmpty))
        Row("password-change", T("oldPassword", True), T("newPassword", True))
        Row("password-changed", T("deviceToken", True))
        Row("account-delete", T("password", True))
        Row("account-deleted")
        ' --- spec 230 / DEVICE-EXCHANGE section 22 items BW-BY: the clipboard. The value itself is never an envelope
        ' member: after `clipboard-ready` (a put) or `clipboard-value` (a get) exactly `size` raw bytes follow. ---
        Row("clipboard-put", T("kind", True), N("size"), T("mimeType", omit:=WhenEmpty), T("name", omit:=WhenEmpty))
        Row("clipboard-ready")
        Row("clipboard-stored", T("clipId", True), T("sha256", True), T("storedAt"))
        Row("clipboard-info")
        Row("clipboard-state", R("value", omit:=WhenEmpty))
        Row("clipboard-get", T("clipId", omit:=WhenEmpty))
        Row("clipboard-value", T("clipId", True), N("size"), T("sha256", True), T("storedAt"),
            T("sourceDeviceId", omit:=WhenEmpty), T("sourceDeviceName", omit:=WhenEmpty), T("kind", True),
            T("mimeType", omit:=WhenEmpty), T("name", omit:=WhenEmpty))
        Row("clipboard-empty")
    End Sub

    ''' <summary>The members of a type in wire order, or Nothing for a type this wire does not define.</summary>
    Public Shared Function Find(typeName As String) As ExchangeMemberSpec()
        If typeName Is Nothing Then Return Nothing
        Dim m As ExchangeMemberSpec() = Nothing
        If Rows.TryGetValue(typeName, m) Then Return m
        Return Nothing
    End Function

    ''' <summary>Every type, in the order of the table of ticket 161 section 4.</summary>
    Public Shared Function Types() As IReadOnlyList(Of String)
        Return Order
    End Function

End Class

''' <summary>Encode and decode of one envelope body (the JSON of a frame, without its 4-byte length).</summary>
Friend NotInheritable Class ExchangeEnvelopeCodec

    Private Sub New()
    End Sub

    ''' <summary>The body of an envelope: members in table order, schema version and type first.</summary>
    Public Shared Function Encode(env As ExchangeEnvelope) As Byte()
        Dim w As ExchangeJsonWriter = EncodeToWriter(env)
        Try
            Return w.ToArray()
        Finally
            w.Clear()
        End Try
    End Function

    ''' <summary>Encodes into a writer the caller clears (a frame that carries a secret is wiped after the send).</summary>
    Public Shared Function EncodeToWriter(env As ExchangeEnvelope) As ExchangeJsonWriter
        If env Is Nothing Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "no envelope")
        Dim specs As ExchangeMemberSpec() = ExchangeEnvelopeTable.Find(env.Type)
        If specs Is Nothing Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "unknown type")
        Dim w As New ExchangeJsonWriter()
        Try
            w.BeginObject()
            w.Name("schemaVersion")
            w.Number(ExchangeWire.SchemaVersion)
            w.Name("type")
            w.Text(env.Type)
            For Each s As ExchangeMemberSpec In specs
                WriteMember(w, env, s)
            Next
            w.EndObject()
            Return w
        Catch
            w.Clear()
            Throw
        End Try
    End Function

    Private Shared Sub WriteMember(w As ExchangeJsonWriter, env As ExchangeEnvelope, s As ExchangeMemberSpec)
        Select Case s.Kind
            Case ExchangeMemberKind.Text
                Dim v As String = env.Text(s.Name)
                If s.Required AndAlso v.Length = 0 Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, s.Name & " is required")
                If s.Omit = ExchangeOmit.WhenEmpty AndAlso v.Length = 0 Then Return
                w.Name(s.Name)
                w.Text(v)
            Case ExchangeMemberKind.Number
                Dim v As Long? = env.NumberOrNothing(s.Name)
                If s.Omit = ExchangeOmit.WhenUnset AndAlso Not v.HasValue Then Return
                If s.Omit = ExchangeOmit.WhenEmpty AndAlso (Not v.HasValue OrElse v.Value = 0) Then Return
                w.Name(s.Name)
                w.Number(If(v.HasValue, v.Value, 0L))
            Case ExchangeMemberKind.Flag
                w.Name(s.Name)
                w.Flag(env.Flag(s.Name))
            Case ExchangeMemberKind.TextList
                Dim l As IReadOnlyList(Of String) = env.TextList(s.Name)
                If s.Omit = ExchangeOmit.WhenEmpty AndAlso l.Count = 0 Then Return
                w.Name(s.Name)
                WriteTextArray(w, l)
            Case ExchangeMemberKind.Record
                Dim raw As String = env.Record(s.Name)
                If raw.Length = 0 Then
                    If s.Required Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, s.Name & " is required")
                    Return
                End If
                If Not ExchangeJson.IsObject(raw) Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, s.Name & " is not an object")
                w.Name(s.Name)
                w.Raw(ExchangeJson.Compact(raw))
            Case ExchangeMemberKind.RecordList
                w.Name(s.Name)
                WriteRecordArray(w, env.Records(s.Name))
            Case ExchangeMemberKind.Upserts
                Dim u As ExchangeRecordSets = env.Upserts()
                w.Name(s.Name)
                w.BeginObject()
                w.Name("devices") : WriteRecordArray(w, u.Devices)
                w.Name("resources") : WriteRecordArray(w, u.Resources)
                w.Name("broadcasts") : WriteRecordArray(w, u.Broadcasts)
                w.EndObject()
            Case ExchangeMemberKind.Removals
                Dim r As ExchangeIdSets = env.Removals()
                w.Name(s.Name)
                w.BeginObject()
                w.Name("deviceIds") : WriteTextArray(w, r.DeviceIds)
                w.Name("resourceIds") : WriteTextArray(w, r.ResourceIds)
                w.Name("broadcastIds") : WriteTextArray(w, r.BroadcastIds)
                w.EndObject()
        End Select
    End Sub

    Private Shared Sub WriteTextArray(w As ExchangeJsonWriter, items As IEnumerable(Of String))
        w.BeginArray()
        For Each item As String In items
            w.Text(item)
        Next
        w.EndArray()
    End Sub

    Private Shared Sub WriteRecordArray(w As ExchangeJsonWriter, items As IEnumerable(Of String))
        w.BeginArray()
        For Each item As String In items
            If Not ExchangeJson.IsObject(item) Then Throw New ExchangeWireException(ExchangeWireError.BadMessage, "a record is not an object")
            w.Raw(ExchangeJson.Compact(item))
        Next
        w.EndArray()
    End Sub

    ''' <summary>Parses one body. An unknown type comes back as an envelope with <see cref="ExchangeEnvelope.IsKnown"/>
    ''' False for the caller to ignore; unknown members are ignored.</summary>
    Public Shared Function Decode(body As Byte()) As ExchangeEnvelope
        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(body)
        Catch ex As JsonException
            Throw New ExchangeWireException(ExchangeWireError.Malformed, "not JSON")
        End Try
        Using doc
            Dim root As JsonElement = doc.RootElement
            If root.ValueKind <> JsonValueKind.Object Then Throw New ExchangeWireException(ExchangeWireError.Malformed, "not an object")

            Dim version As JsonElement = Nothing
            Dim versionNumber As Integer
            If Not root.TryGetProperty("schemaVersion", version) OrElse version.ValueKind <> JsonValueKind.Number OrElse
               Not version.TryGetInt32(versionNumber) OrElse versionNumber <> ExchangeWire.SchemaVersion Then
                Throw New ExchangeWireException(ExchangeWireError.Schema, "unsupported schemaVersion")
            End If

            Dim typeElement As JsonElement = Nothing
            If Not root.TryGetProperty("type", typeElement) Then Throw New ExchangeWireException(ExchangeWireError.NoType, "no type")
            If typeElement.ValueKind <> JsonValueKind.String Then Throw New ExchangeWireException(ExchangeWireError.Malformed, "type is not a string")
            Dim typeName As String = typeElement.GetString()
            If String.IsNullOrEmpty(typeName) Then Throw New ExchangeWireException(ExchangeWireError.NoType, "no type")

            Dim env As New ExchangeEnvelope(typeName)
            Dim specs As ExchangeMemberSpec() = ExchangeEnvelopeTable.Find(typeName)
            If specs Is Nothing Then Return env

            For Each s As ExchangeMemberSpec In specs
                Dim el As JsonElement = Nothing
                If Not root.TryGetProperty(s.Name, el) Then
                    If s.Required Then Throw New ExchangeWireException(ExchangeWireError.Malformed, s.Name & " is required")
                    Continue For
                End If
                ReadMember(env, s, el)
            Next
            Return env
        End Using
    End Function

    Private Shared Sub ReadMember(env As ExchangeEnvelope, s As ExchangeMemberSpec, el As JsonElement)
        Select Case s.Kind
            Case ExchangeMemberKind.Text
                If el.ValueKind <> JsonValueKind.String Then Throw Bad(s)
                Dim v As String = el.GetString()
                If s.Required AndAlso String.IsNullOrEmpty(v) Then Throw Bad(s)
                env.SetText(s.Name, v)
            Case ExchangeMemberKind.Number
                Dim n As Long
                If el.ValueKind <> JsonValueKind.Number OrElse Not el.TryGetInt64(n) Then Throw Bad(s)
                env.SetNumber(s.Name, n)
            Case ExchangeMemberKind.Flag
                If el.ValueKind <> JsonValueKind.True AndAlso el.ValueKind <> JsonValueKind.False Then Throw Bad(s)
                env.SetFlag(s.Name, el.GetBoolean())
            Case ExchangeMemberKind.TextList
                env.SetTextList(s.Name, ReadTextArray(el, s))
            Case ExchangeMemberKind.Record
                If el.ValueKind <> JsonValueKind.Object Then Throw Bad(s)
                env.SetRecord(s.Name, ExchangeJson.Compact(el.GetRawText()))
            Case ExchangeMemberKind.RecordList
                env.SetRecords(s.Name, ReadRecordArray(el, s))
            Case ExchangeMemberKind.Upserts
                If el.ValueKind <> JsonValueKind.Object Then Throw Bad(s)
                Dim u As New ExchangeRecordSets()
                Dim part As JsonElement = Nothing
                If el.TryGetProperty("devices", part) Then u.Devices.AddRange(ReadRecordArray(part, s))
                If el.TryGetProperty("resources", part) Then u.Resources.AddRange(ReadRecordArray(part, s))
                If el.TryGetProperty("broadcasts", part) Then u.Broadcasts.AddRange(ReadRecordArray(part, s))
                env.SetUpserts(u)
            Case ExchangeMemberKind.Removals
                If el.ValueKind <> JsonValueKind.Object Then Throw Bad(s)
                Dim r As New ExchangeIdSets()
                Dim part As JsonElement = Nothing
                If el.TryGetProperty("deviceIds", part) Then r.DeviceIds.AddRange(ReadTextArray(part, s))
                If el.TryGetProperty("resourceIds", part) Then r.ResourceIds.AddRange(ReadTextArray(part, s))
                If el.TryGetProperty("broadcastIds", part) Then r.BroadcastIds.AddRange(ReadTextArray(part, s))
                env.SetRemovals(r)
        End Select
    End Sub

    Private Shared Function Bad(s As ExchangeMemberSpec) As ExchangeWireException
        Return New ExchangeWireException(ExchangeWireError.Malformed, s.Name & " has the wrong shape")
    End Function

    Private Shared Function ReadTextArray(el As JsonElement, s As ExchangeMemberSpec) As List(Of String)
        If el.ValueKind <> JsonValueKind.Array Then Throw Bad(s)
        Dim list As New List(Of String)
        For Each item As JsonElement In el.EnumerateArray()
            If item.ValueKind <> JsonValueKind.String Then Throw Bad(s)
            list.Add(item.GetString())
        Next
        Return list
    End Function

    Private Shared Function ReadRecordArray(el As JsonElement, s As ExchangeMemberSpec) As List(Of String)
        If el.ValueKind <> JsonValueKind.Array Then Throw Bad(s)
        Dim list As New List(Of String)
        For Each item As JsonElement In el.EnumerateArray()
            If item.ValueKind <> JsonValueKind.Object Then Throw Bad(s)
            list.Add(ExchangeJson.Compact(item.GetRawText()))
        Next
        Return list
    End Function

End Class
#End If
