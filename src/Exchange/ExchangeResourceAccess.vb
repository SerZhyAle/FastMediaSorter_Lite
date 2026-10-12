#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Ticket 170 rule 3 (the producer writes) and ticket 171 rule 3 (the consumer reads): the `access` member and the whole record of an
' `sftp-share` resource (DEVICE-EXCHANGE 6.2). One module for both, so the two ends cannot drift.
'
'   access = {"descriptor": "<FMSSFTP1|2 code>", "root": "/<share name>"}
'
' `root` is the member of item O (14.5): the worker serves several named roots under one SFTP server and one login, and the code
' carries a host, a port and credentials but no root. A reader that finds none falls back to "/" + the record's `name` (ask 10).
'
' An `access` holds a password: nothing here prints a member, and the library holds no user-facing string.

Friend NotInheritable Class ExchangeAccessInfo

    Public ReadOnly Code As FmsSftpCode
    ''' <summary>A path from the server's root, canonical form "/name" (never "" and never a relative path).</summary>
    Public ReadOnly Root As String

    Public Sub New(code As FmsSftpCode, root As String)
        Me.Code = code
        Me.Root = root
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeAccessInfo"
    End Function

End Class

Friend NotInheritable Class ExchangeResourceAccess

    Private Sub New()
    End Sub

    Public Const DescriptorMember As String = "descriptor"
    Public Const RootMember As String = "root"

    ''' <summary>The root the consumer assumes when the record names none: "/" + the resource's name (ticket 170 rule 3).</summary>
    Public Shared Function FallbackRoot(name As String) As String
        Dim n As String = If(name, "").Trim().Replace("\"c, "/"c).Trim("/"c)
        Return "/" & n
    End Function

    ''' <summary>True for a root a consumer may use: absolute, no `.` or `..` segment, no control character, at most 1024 characters.</summary>
    Public Shared Function IsUsableRoot(root As String) As Boolean
        If String.IsNullOrEmpty(root) OrElse root.Length > 1024 OrElse root(0) <> "/"c Then Return False
        For Each segment As String In root.Split("/"c)
            If segment = "." OrElse segment = ".." Then Return False
            For Each c As Char In segment
                If Char.IsControl(c) OrElse c = "\"c Then Return False
            Next
        Next
        Return True
    End Function

    ''' <summary>The `access` JSON of a share: the code and its root. False when the code cannot be written or the root is unusable.</summary>
    Public Shared Function TryBuild(code As FmsSftpCode, root As String, ByRef accessJson As String) As Boolean
        accessJson = ""
        Dim text As String = Nothing
        If code Is Nothing OrElse Not code.TryBuild(text) OrElse Not IsUsableRoot(root) Then Return False
        Dim o As New JsonObject()
        o(DescriptorMember) = text
        o(RootMember) = root
        accessJson = o.ToJsonString()
        Return True
    End Function

    ''' <summary>Reads an `access` member. False for anything that is not an object with a descriptor this reader accepts whole; a
    ''' root that is absent falls back to "/" + <paramref name="fallbackName"/>, one that is present and unusable is refused.</summary>
    Public Shared Function TryRead(accessJson As String, fallbackName As String, ByRef info As ExchangeAccessInfo) As Boolean
        info = Nothing
        If String.IsNullOrEmpty(accessJson) Then Return False
        Try
            Using doc As JsonDocument = JsonDocument.Parse(accessJson)
                Dim r As JsonElement = doc.RootElement
                If r.ValueKind <> JsonValueKind.Object Then Return False
                Dim d As JsonElement = Nothing
                If Not r.TryGetProperty(DescriptorMember, d) OrElse d.ValueKind <> JsonValueKind.String Then Return False
                Dim code As FmsSftpCode = Nothing
                If Not FmsSftpCode.TryParse(d.GetString(), code) Then Return False
                Dim root As String
                Dim rootEl As JsonElement = Nothing
                If r.TryGetProperty(RootMember, rootEl) AndAlso rootEl.ValueKind = JsonValueKind.String Then
                    root = rootEl.GetString()
                    If Not IsUsableRoot(root) Then Return False
                ElseIf r.TryGetProperty(RootMember, rootEl) Then
                    Return False
                Else
                    root = FallbackRoot(fallbackName)
                    If Not IsUsableRoot(root) Then Return False
                End If
                info = New ExchangeAccessInfo(code, root)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    ''' <summary>Whether two `access` members say the same thing (what "a changed access" means for a consumer): the code text and the
    ''' root, compared as written, never the member order or white space.</summary>
    Public Shared Function SameAccess(a As String, b As String, fallbackName As String) As Boolean
        Dim x As ExchangeAccessInfo = Nothing
        Dim y As ExchangeAccessInfo = Nothing
        If Not TryRead(a, fallbackName, x) OrElse Not TryRead(b, fallbackName, y) Then Return String.Equals(a, b, StringComparison.Ordinal)
        Dim tx As String = Nothing
        Dim ty As String = Nothing
        x.Code.TryBuild(tx)
        y.Code.TryBuild(ty)
        Return String.Equals(tx, ty, StringComparison.Ordinal) AndAlso String.Equals(x.Root, y.Root, StringComparison.Ordinal)
    End Function

End Class

''' <summary>The `sftp-share` record the producer publishes (DEVICE-EXCHANGE 6.2).</summary>
Friend NotInheritable Class ExchangeResourceRecordBuilder

    Private Sub New()
    End Sub

    ''' <summary>The record as compact JSON, members in the order of the catalog's vectors: resourceId, deviceId, kind, name, updatedAt,
    ''' access, shareId, publicPort. `presence` is the server's to stamp and is never sent. <paramref name="publicPortRequest"/> is
    ''' the third-party port REQUEST (0 = any, N = that one, Nothing = none; ANYWHERE-ACCESS 5.5). Nothing is returned when the
    ''' record would break a rule of section 6 (an id out of its form, an empty name, a record above 16 KiB).</summary>
    Public Shared Function TryBuild(resourceId As String, deviceId As String, name As String, accessJson As String, shareId As String,
                                    publicPortRequest As Integer?, updatedAtUtc As DateTime, ByRef json As String, Optional pinProtected As Boolean = False) As Boolean
        json = ""
        If Not ExchangeIds.IsValidId(resourceId) OrElse Not ExchangeIds.IsValidId(deviceId) Then Return False
        If String.IsNullOrWhiteSpace(name) OrElse name.Length > 256 Then Return False
        If Not String.IsNullOrEmpty(shareId) AndAlso Not ExchangeIds.IsValidId(shareId) Then Return False
        If publicPortRequest.HasValue AndAlso (publicPortRequest.Value < 0 OrElse publicPortRequest.Value > 65535) Then Return False
        Dim o As New JsonObject()
        o("resourceId") = resourceId
        o("deviceId") = deviceId
        o("kind") = ExchangeWire.KindSftpShare
        o("name") = name
        o("updatedAt") = updatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        If pinProtected Then o("protection") = New JsonObject From {{"scheme", JsonValue.Create("pin-v1")}}
        If Not String.IsNullOrEmpty(accessJson) Then
            Dim access As JsonNode = Nothing
            Try
                access = JsonNode.Parse(accessJson)
            Catch ex As JsonException
                Return False
            End Try
            If TypeOf access IsNot JsonObject Then Return False
            o("access") = access
        End If
        If Not String.IsNullOrEmpty(shareId) Then o("shareId") = shareId
        If publicPortRequest.HasValue Then o("publicPort") = publicPortRequest.Value
        Dim text As String = o.ToJsonString(New JsonSerializerOptions With {.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping})
        If Encoding.UTF8.GetByteCount(text) > ExchangeWire.MaxRecordBytes Then Return False
        json = text
        Return True
    End Function

End Class
#End If
