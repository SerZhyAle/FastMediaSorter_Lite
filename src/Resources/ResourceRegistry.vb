#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>
''' A configured place the user picks - what the phone calls a resource
''' (006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md PV-2). Only things that had to be
''' configured are resources: a plain local folder stays a plain folder and is never listed.
'''
''' <see cref="SecretHandle"/> NAMES a secret kept elsewhere (PR §4); it is never one. There
''' is deliberately no property here that could hold a password or a key.
''' </summary>
Friend NotInheritable Class ResourceDescriptor
    Public Property Id As String = ""
    Public Property DisplayName As String = ""
    <JsonConverter(GetType(JsonStringEnumConverter))>
    Public Property Kind As ResourceKind = ResourceKind.Sftp
    Public Property Host As String = ""
    Public Property Port As Integer
    Public Property UserName As String = ""
    ''' <summary>The provider's own path of the resource's root ("/MOV", or a UNC path).</summary>
    Public Property RootPath As String = ""
    ''' <summary>What the user told us when adding it; the provider's own capabilities
    ''' still have the last word.</summary>
    Public Property ReadOnlyHint As Boolean
    Public Property SecretHandle As String = ""
    ''' <summary>The SSH host key this resource is pinned to, canonical <c>SHA256:..</c>, or ""
    ''' while the first connection has not been accepted yet (007 §3.3). A fingerprint is public
    ''' information, so unlike the credential it lives here, next to the resource it protects.</summary>
    Public Property PinnedFingerprint As String = ""
    ''' <summary>The other addresses the same server answers on, in the producer's order
    ''' (<c>lan</c>, <c>ipv6</c>, <c>portforward</c>) - raced, never indexed (007 §3.1).
    ''' Empty for a resource added by hand: <see cref="Host"/>/<see cref="Port"/> is then the only address.</summary>
    Public Property Endpoints As List(Of ResourceEndpoint)
    ''' <summary>Nothing = follow the viewer's own "include subfolders" setting; a value is the
    ''' root's own wish from an imported document (<c>scanSubdirectories</c>).</summary>
    Public Property IncludeSubfolders As Boolean?
    ''' <summary>When this resource was last opened, for the manager's list and its ordering.</summary>
    Public Property LastUsedUtc As DateTime?
    <JsonExtensionData>
    Public Property AdditionalFields As Dictionary(Of String, JsonElement)
End Class

''' <summary>One address of a resource: what kind of path it is, and where.</summary>
Friend NotInheritable Class ResourceEndpoint
    Public Property Kind As String = ""
    Public Property Host As String = ""
    Public Property Port As Integer
End Class

''' <summary>
''' The string that stands for "this folder of this resource" - what the folder box shows
''' and what PersistableFolderPath writes to the registry, so the next start can re-open it:
''' <c>sftp://fms@192.168.1.100:55259/MOV</c>. Never the cache directory. Pure.
''' </summary>
Friend Module ResourceLocator

    Friend Const Sftp_Scheme As String = "sftp://"

    ''' <summary>The locator of <paramref name="remoteDir"/> inside <paramref name="resource"/>.
    ''' An empty directory means the resource's root.</summary>
    Friend Function Build(resource As ResourceDescriptor, remoteDir As String) As String
        If resource Is Nothing Then Return ""
        Dim dir As String = If(String.IsNullOrEmpty(remoteDir), resource.RootPath, remoteDir)
        Select Case resource.Kind
            Case ResourceKind.Sftp
                Dim remote As String = If(dir, "").Replace("\"c, "/"c)
                If Not remote.StartsWith("/", StringComparison.Ordinal) Then remote = "/" & remote
                Dim user As String = If(String.IsNullOrEmpty(resource.UserName), "", EscapeComponent(resource.UserName, True) & "@")
                Dim port As String = If(resource.Port > 0, ":" & resource.Port.ToString(CultureInfo.InvariantCulture), "")
                ' An IPv6 literal carries colons of its own: bracketed, or the port would be ambiguous.
                Dim host As String = If(resource.Host.Contains(":"c) AndAlso Not resource.Host.StartsWith("[", StringComparison.Ordinal),
                                        "[" & resource.Host & "]", resource.Host)
                Return Sftp_Scheme & user & host & port & EscapePath(remote)
            Case Else
                ' A UNC resource is addressed by its own path - Windows already knows how.
                Return If(dir, "")
        End Select
    End Function

    ''' <summary>Is this string a resource locator rather than a folder path?</summary>
    Friend Function IsLocator(value As String) As Boolean
        Return Not String.IsNullOrEmpty(value) AndAlso
               value.StartsWith(Sftp_Scheme, StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>
    ''' Splits an sftp locator. False for anything else, and for one that half-parses: a bad
    ''' port, a password in the user-info (a locator never carries one - it is shown and
    ''' persisted), an unbracketed IPv6 literal, a query/fragment, a bad percent escape, or a
    ''' path segment that decodes to a separator, NUL/control character, "." or "..".
    ''' Each segment is percent-decoded exactly once and the remote case is preserved.
    ''' </summary>
    Friend Function TryParse(locator As String, ByRef userName As String, ByRef host As String,
                             ByRef port As Integer, ByRef remotePath As String) As Boolean
        userName = "" : host = "" : port = 0 : remotePath = "/"
        If Not IsLocator(locator) Then Return False

        Dim rest As String = locator.Substring(Sftp_Scheme.Length)
        If rest.IndexOf("?"c) >= 0 OrElse rest.IndexOf("#"c) >= 0 Then Return False
        Dim slash As Integer = rest.IndexOf("/"c)
        Dim authority As String = If(slash >= 0, rest.Substring(0, slash), rest)
        Dim rawPath As String = If(slash >= 0, rest.Substring(slash), "/")

        Dim at As Integer = authority.LastIndexOf("@"c)
        If at >= 0 Then
            Dim rawUser As String = authority.Substring(0, at)
            If rawUser.IndexOf(":"c) >= 0 Then Return False
            Dim decodedUser As String = Nothing
            If Not TryDecode(rawUser, decodedUser) Then Return False
            userName = decodedUser
            authority = authority.Substring(at + 1)
        End If

        Dim portText As String = Nothing
        If authority.StartsWith("[", StringComparison.Ordinal) Then
            Dim close As Integer = authority.IndexOf("]"c)
            If close < 2 Then Return False
            Dim tail As String = authority.Substring(close + 1)
            host = authority.Substring(1, close - 1)
            If Not System.Net.IPAddress.TryParse(host, Nothing) OrElse host.IndexOf(":"c) < 0 Then Return False
            If tail.Length > 0 Then
                If Not tail.StartsWith(":", StringComparison.Ordinal) Then Return False
                portText = tail.Substring(1)
            End If
        Else
            Dim colon As Integer = authority.IndexOf(":"c)
            If colon >= 0 Then
                If authority.IndexOf(":"c, colon + 1) >= 0 Then Return False ' unbracketed IPv6
                portText = authority.Substring(colon + 1)
                host = authority.Substring(0, colon)
            Else
                host = authority
            End If
        End If

        If portText IsNot Nothing Then
            Dim parsed As Integer
            If Not Integer.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, parsed) OrElse
               parsed < 1 OrElse parsed > 65535 Then Return False
            port = parsed
        End If

        If host.Length = 0 OrElse HasForbiddenHostChar(host) Then Return False
        Dim decodedPath As String = Nothing
        If Not TryDecodePath(rawPath, decodedPath) Then Return False
        remotePath = decodedPath
        Return True
    End Function

    ''' <summary>
    ''' The locator of a file named <paramref name="fileName"/> inside the folder
    ''' <paramref name="folderLocator"/> - what a recipient slot that names a server folder turns
    ''' into once a file is sent there (007 §4.2). The name is escaped exactly as
    ''' <see cref="Build"/> escapes a path segment, so <see cref="TryParse"/> gives it back
    ''' unchanged. Pure.
    ''' </summary>
    Friend Function AppendName(folderLocator As String, fileName As String) As String
        If String.IsNullOrEmpty(folderLocator) Then Return ""
        Dim folder As String = folderLocator
        ' Only the PATH's trailing slash: "sftp://" itself must never be shortened.
        While folder.Length > AuthorityEnd(folder) + 1 AndAlso folder.EndsWith("/", StringComparison.Ordinal)
            folder = folder.Substring(0, folder.Length - 1)
        End While
        If Not folder.EndsWith("/", StringComparison.Ordinal) Then folder &= "/"
        Return folder & EscapeComponent(If(fileName, ""), False)
    End Function

    ''' <summary>The folder locator a file locator sits in - its last path segment taken off. The
    ''' root folder of a server is its own parent. Pure.</summary>
    Friend Function ParentOf(fileLocator As String) As String
        If Not IsLocator(fileLocator) Then Return ""
        Dim authority As Integer = AuthorityEnd(fileLocator)
        If authority < 0 Then Return fileLocator & "/"
        Dim trimmed As String = fileLocator
        While trimmed.Length > authority + 1 AndAlso trimmed.EndsWith("/", StringComparison.Ordinal)
            trimmed = trimmed.Substring(0, trimmed.Length - 1)
        End While
        Dim cut As Integer = trimmed.LastIndexOf("/"c)
        If cut <= authority Then Return trimmed.Substring(0, authority + 1)
        Return trimmed.Substring(0, cut)
    End Function

    ''' <summary>The decoded last segment of a locator - a file's real name, as the server spells
    ''' it. Empty for a server's root, and for a locator that does not parse. Pure.</summary>
    Friend Function LeafName(locator As String) As String
        Dim user As String = "", host As String = "", port As Integer = 0, remotePath As String = "/"
        If Not TryParse(locator, user, host, port, remotePath) Then Return ""
        Dim cut As Integer = remotePath.LastIndexOf("/"c)
        Return If(cut < 0, remotePath, remotePath.Substring(cut + 1))
    End Function

    ''' <summary>
    ''' Do two locators name the same folder of the same server, as the same user? The host is
    ''' compared without case, the path and the user with it (a remote file system decides its
    ''' own case rules - assuming it folds would call two different folders one). A missing port
    ''' is 22. False when either does not parse. Pure.
    ''' </summary>
    Friend Function SameFolder(a As String, b As String) As Boolean
        Dim userA As String = "", hostA As String = "", portA As Integer = 0, pathA As String = "/"
        Dim userB As String = "", hostB As String = "", portB As Integer = 0, pathB As String = "/"
        If Not TryParse(a, userA, hostA, portA, pathA) OrElse Not TryParse(b, userB, hostB, portB, pathB) Then Return False
        Return String.Equals(hostA, hostB, StringComparison.OrdinalIgnoreCase) AndAlso
               If(portA > 0, portA, 22) = If(portB > 0, portB, 22) AndAlso
               String.Equals(userA, userB, StringComparison.Ordinal) AndAlso
               String.Equals(pathA, pathB, StringComparison.Ordinal)
    End Function

    ''' <summary>Index of the "/" that ends the authority (the first one after the scheme), or -1
    ''' when the locator has no path at all.</summary>
    Private Function AuthorityEnd(locator As String) As Integer
        If Not IsLocator(locator) Then Return -1
        Return locator.IndexOf("/"c, Sftp_Scheme.Length)
    End Function

    Private Function HasForbiddenHostChar(host As String) As Boolean
        For Each ch As Char In host
            If Char.IsWhiteSpace(ch) OrElse Char.IsControl(ch) OrElse "@/\%?#".IndexOf(ch) >= 0 Then Return True
        Next
        Return False
    End Function

    ''' <summary>Characters escaped in a locator component. Non-ASCII stays literal (the box
    ''' shows what the user typed); "%" is always escaped, which is what makes a single decode
    ''' exact. A user name also escapes "@", ":" and "/".</summary>
    Private Function EscapeComponent(value As String, isUserInfo As Boolean) As String
        Dim sb As New System.Text.StringBuilder(value.Length)
        For Each ch As Char In value
            Dim escape As Boolean = ch < " "c OrElse AscW(ch) = &H7F OrElse " ""%<>?#[]^`{|}\".IndexOf(ch) >= 0 OrElse
                                    (isUserInfo AndAlso "@:/".IndexOf(ch) >= 0)
            If escape Then
                ' Everything escaped is ASCII, so one byte per character.
                sb.Append("%"c).Append(AscW(ch).ToString("X2", CultureInfo.InvariantCulture))
            Else
                sb.Append(ch)
            End If
        Next
        Return sb.ToString()
    End Function

    Private Function EscapePath(remote As String) As String
        Dim segments As String() = remote.Split("/"c)
        For i As Integer = 0 To segments.Length - 1
            segments(i) = EscapeComponent(segments(i), False)
        Next
        Return String.Join("/", segments)
    End Function

    ''' <summary>One percent-decode, strict: a "%" must be followed by two hex digits, and
    ''' the bytes must be valid UTF-8 (a lone invalid byte is refused, not replaced).</summary>
    Private Function TryDecode(raw As String, ByRef decoded As String) As Boolean
        decoded = ""
        If raw.IndexOf("%"c) < 0 Then
            decoded = raw
            Return True
        End If
        Dim bytes As New List(Of Byte)(raw.Length)
        Dim i As Integer = 0
        While i < raw.Length
            Dim ch As Char = raw(i)
            If ch = "%"c Then
                Dim value As Integer
                If i + 2 > raw.Length - 1 Then Return False
                Dim hex As String = raw.Substring(i + 1, 2)
                If Not Uri.IsHexDigit(hex(0)) OrElse Not Uri.IsHexDigit(hex(1)) Then Return False
                value = Convert.ToInt32(hex, 16)
                bytes.Add(CByte(value))
                i += 3
            ElseIf Char.IsHighSurrogate(ch) AndAlso i + 1 < raw.Length AndAlso Char.IsLowSurrogate(raw(i + 1)) Then
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(raw.Substring(i, 2)))
                i += 2
            ElseIf Char.IsSurrogate(ch) Then
                Return False ' a lone surrogate has no UTF-8 form
            Else
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(ch.ToString()))
                i += 1
            End If
        End While
        Try
            decoded = New System.Text.UTF8Encoding(False, True).GetString(bytes.ToArray())
            Return True
        Catch ex As System.Text.DecoderFallbackException
            Return False
        End Try
    End Function

    Private Function TryDecodePath(rawPath As String, ByRef path As String) As Boolean
        path = "/"
        Dim parts As New List(Of String)()
        Dim segments As String() = rawPath.Split("/"c)
        For i As Integer = 1 To segments.Length - 1 ' [0] is the empty text before the leading slash
            Dim isLast As Boolean = i = segments.Length - 1
            If segments(i).Length = 0 Then
                If isLast Then Continue For ' a trailing slash
                Return False                 ' an interior "//" is ambiguous
            End If
            Dim decoded As String = Nothing
            If Not TryDecode(segments(i), decoded) Then Return False
            If decoded = "." OrElse decoded = ".." Then Return False
            For Each ch As Char In decoded
                If ch = "/"c OrElse ch = "\"c OrElse Char.IsControl(ch) Then Return False
            Next
            parts.Add(decoded)
        Next
        path = "/" & String.Join("/", parts)
        Return True
    End Function

End Module

''' <summary>
''' The persisted list of resources: JSON in the SZA\FastMediaSorter hive, next to the other
''' modern preferences (006 PV-2). The pure half - parsing and serialising - is what the
''' tests hold; the registry half is two calls.
''' </summary>
Friend Module ResourceRegistry

    ''' <summary>The hive value the list lives under. Friend so a host that takes an injected
    ''' hive reader (the resource panel's tests) can name the same value the real Load reads.</summary>
    Friend Const Value_Name As String = "Resources"

    Friend Function Load() As List(Of ResourceDescriptor)
        Dim raw As String = Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, Value_Name, "[]")
        Dim lossy As Boolean
        Dim result As List(Of ResourceDescriptor) = ParseStored(raw, lossy)
        
        ' If parsing was lossy, try to restore from backup (057.S07-9)
        If lossy Then
            Dim backup As String = Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, Value_Name & ".bak", "")
            If Not String.IsNullOrEmpty(backup) AndAlso Not String.Equals(backup, raw, StringComparison.Ordinal) Then
                ' Restore from backup and retry parsing
                lossy = False
                result = ParseStored(backup, lossy)
                If Not lossy Then
                    ' Backup was good, save it back to the main value for next time
                    Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, Value_Name, backup)
                End If
            End If
        End If
        
        Return result
    End Function

    Friend Sub Save(resources As IEnumerable(Of ResourceDescriptor))
        Save(resources, AddressOf HiveRead, AddressOf HiveWrite)
    End Sub

    ''' <summary>The real hive, as the delegate pair every persistence function of this layer takes -
    ''' so the viewer and the tests drive the same code and differ only in what they pass.</summary>
    Friend Function HiveRead(name As String) As String
        Return Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, name, "")
    End Function

    Friend Sub HiveWrite(name As String, value As String)
        Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, name, value)
    End Sub

    ' Read the current value even if Load was never called. Backup must succeed before
    ' replacement; an older recovery copy is never silently overwritten either.
    Friend Sub Save(resources As IEnumerable(Of ResourceDescriptor), readValue As Func(Of String, String),
                    writeValue As Action(Of String, String))
        Dim replacement As String = Serialize(resources)
        Dim raw As String = readValue(Value_Name)
        Dim lossy As Boolean
        ParseStored(raw, lossy)
        If lossy Then
            Dim backup As String = readValue(Value_Name & ".bak")
            If Not String.IsNullOrEmpty(backup) AndAlso Not String.Equals(backup, raw, StringComparison.Ordinal) Then
                Throw New IO.IOException("Resources could not be read; the existing recovery copy must be kept.")
            End If
            writeValue(Value_Name & ".bak", raw)
        End If
        writeValue(Value_Name, replacement)
    End Sub

    ''' <summary>Return usable entries without throwing. Save backs up any raw value
    ''' containing unrecognised or rejected entries; extension fields round-trip intact.</summary>
    Friend Function Parse(json As String) As List(Of ResourceDescriptor)
        Dim lossy As Boolean
        Return ParseStored(json, lossy)
    End Function

    Private Function ParseStored(json As String, ByRef lossy As Boolean) As List(Of ResourceDescriptor)
        Dim result As New List(Of ResourceDescriptor)()
        lossy = False
        If String.IsNullOrEmpty(json) Then Return result
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                If document.RootElement.ValueKind <> JsonValueKind.Array Then
                    lossy = True
                    Return result
                End If
                Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each element As JsonElement In document.RootElement.EnumerateArray()
                    Try
                        Dim item As ResourceDescriptor = element.Deserialize(Of ResourceDescriptor)()
                        If Not IsStorable(item) OrElse Not seen.Add(item.Id) Then
                            lossy = True
                            Continue For
                        End If
                        result.Add(item)
                    Catch ex As JsonException
                        lossy = True
                    End Try
                Next
            End Using
        Catch ex As JsonException
            lossy = True
        End Try
        Return result
    End Function

    Friend Function Serialize(resources As IEnumerable(Of ResourceDescriptor)) As String
        Dim storable As List(Of ResourceDescriptor) =
            If(resources, Enumerable.Empty(Of ResourceDescriptor)()).Where(AddressOf IsStorable).ToList()
        Return JsonSerializer.Serialize(storable)
    End Function

    ''' <summary>Is this a resource the registry may hold at all?</summary>
    Friend Function IsStorable(item As ResourceDescriptor) As Boolean
        If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Id) Then Return False
        Select Case item.Kind
            Case ResourceKind.Sftp
                Return Not String.IsNullOrWhiteSpace(item.Host) AndAlso item.Port >= 0 AndAlso item.Port <= 65535
            Case ResourceKind.Unc, ResourceKind.Smb
                Return Not String.IsNullOrWhiteSpace(item.RootPath)
            Case Else
                Return False
        End Select
    End Function

End Module
#End If
