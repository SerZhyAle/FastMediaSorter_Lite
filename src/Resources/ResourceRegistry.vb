#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
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

    ''' <summary>Fields a newer build wrote that this one does not know, kept as they came:
    ''' a read-edit-save carries them through unchanged, so saving here never forgets what
    ''' another build added (057.S07-9, canon invariant 10).</summary>
    <JsonExtensionData>
    Public Property UnknownFields As New Dictionary(Of String, JsonElement)()
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
                Dim user As String = If(String.IsNullOrEmpty(resource.UserName), "", resource.UserName & "@")
                Dim port As String = If(resource.Port > 0, ":" & resource.Port.ToString(CultureInfo.InvariantCulture), "")
                Return Sftp_Scheme & user & resource.Host & port & remote
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

    ''' <summary>Splits an sftp locator. False for anything else, and for a locator whose
    ''' port is not a number in range - a string that half-parses is not one to connect to.</summary>
    Friend Function TryParse(locator As String, ByRef userName As String, ByRef host As String,
                             ByRef port As Integer, ByRef remotePath As String) As Boolean
        userName = "" : host = "" : port = 0 : remotePath = "/"
        If Not IsLocator(locator) Then Return False

        Dim rest As String = locator.Substring(Sftp_Scheme.Length)
        Dim slash As Integer = rest.IndexOf("/"c)
        Dim authority As String = If(slash >= 0, rest.Substring(0, slash), rest)
        remotePath = If(slash >= 0, rest.Substring(slash), "/")

        Dim at As Integer = authority.LastIndexOf("@"c)
        If at >= 0 Then
            userName = authority.Substring(0, at)
            authority = authority.Substring(at + 1)
        End If

        Dim colon As Integer = authority.LastIndexOf(":"c)
        If colon >= 0 Then
            Dim parsed As Integer
            If Not Integer.TryParse(authority.Substring(colon + 1), NumberStyles.None,
                                    CultureInfo.InvariantCulture, parsed) OrElse
               parsed < 1 OrElse parsed > 65535 Then Return False
            port = parsed
            authority = authority.Substring(0, colon)
        End If

        host = authority
        Return host.Length > 0
    End Function

End Module

''' <summary>
''' The persisted list of resources: JSON in the SZA\FastMediaSorter hive, next to the other
''' modern preferences (006 PV-2). The pure half - parsing and serialising - is what the
''' tests hold; the registry half is two calls.
'''
''' A stored value this build cannot fully read back is user-authored data (canon invariant
''' 10): <see cref="Save(IEnumerable(Of ResourceDescriptor), Func(Of String, String), Action(Of String, String))"/>
''' copies it aside before overwriting, and refuses to save over it at all when a different
''' copy is already aside (057.S07-9) - so the first resource a newer build's user adds never
''' erases a list an older build could not read.
''' </summary>
Friend Module ResourceRegistry

    Private Const Value_Name As String = "Resources"
    Private Const Backup_Name As String = "Resources.bak"

    Friend Function Load() As List(Of ResourceDescriptor)
        Return Parse(Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, Value_Name, "[]"))
    End Function

    Friend Sub Save(resources As IEnumerable(Of ResourceDescriptor))
        Save(resources,
             Function(name) CStr(Microsoft.VisualBasic.Interaction.GetSetting(App_name, Second_App_Name, name, "")),
             Sub(name, value) Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, name, value))
    End Sub

    ''' <summary>
    ''' Saves the list through the caller's registry seam. A stored value that would not
    ''' survive <see cref="Parse"/> - damaged, not the shape of a list, an entry the registry
    ''' must not hold, a duplicate id - is copied to <c>Resources.bak</c> FIRST (057.S07-9):
    ''' if a DIFFERENT copy is already there, the save is refused outright rather than
    ''' destroy the only other copy of the user's list.
    ''' </summary>
    Friend Sub Save(resources As IEnumerable(Of ResourceDescriptor),
                    readValue As Func(Of String, String), writeValue As Action(Of String, String))
        Dim raw As String = If(readValue(Value_Name), "")
        If Not String.IsNullOrWhiteSpace(raw) AndAlso Not SurvivesParse(raw) Then
            Dim backup As String = If(readValue(Backup_Name), "")
            If backup.Length > 0 AndAlso Not String.Equals(backup, raw, StringComparison.Ordinal) Then
                Throw New IOException("The stored resource list could not be read; it was left untouched.")
            End If
            writeValue(Backup_Name, raw)
        End If
        writeValue(Value_Name, Serialize(resources))
    End Sub

    ''' <summary>
    ''' The stored list, never throwing: a damaged value is an empty list, not a start-up
    ''' failure. Entries that could never be opened are dropped - no id, no host, and any
    ''' Local kind (a plain folder is never a resource) - as are duplicate ids.
    ''' </summary>
    Friend Function Parse(json As String) As List(Of ResourceDescriptor)
        Dim result As New List(Of ResourceDescriptor)()
        If String.IsNullOrWhiteSpace(json) Then Return result
        Dim parsed As List(Of ResourceDescriptor)
        Try
            parsed = JsonSerializer.Deserialize(Of List(Of ResourceDescriptor))(json)
        Catch
            Return result
        End Try
        If parsed Is Nothing Then Return result

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each item As ResourceDescriptor In parsed
            If Not IsStorable(item) Then Continue For
            If Not seen.Add(item.Id) Then Continue For
            result.Add(item)
        Next
        Return result
    End Function

    ''' <summary>Would <see cref="Parse"/> keep every entry of this stored value? Unknown
    ''' FIELDS are fine - they ride along on the descriptor - but a value that is not a
    ''' list, or holds an entry the registry would drop (not storable, or a duplicate id),
    ''' would come back shorter, and the next save would forget what was dropped.</summary>
    Private Function SurvivesParse(raw As String) As Boolean
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Using doc As JsonDocument = JsonDocument.Parse(raw)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return False
                For Each element As JsonElement In doc.RootElement.EnumerateArray()
                    Dim candidate As ResourceDescriptor
                    Try
                        candidate = JsonSerializer.Deserialize(Of ResourceDescriptor)(element)
                    Catch
                        Return False
                    End Try
                    If Not IsStorable(candidate) Then Return False
                    If Not seen.Add(candidate.Id) Then Return False
                Next
            End Using
        Catch
            Return False
        End Try
        Return True
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
            Case ResourceKind.Unc
                Return Not String.IsNullOrWhiteSpace(item.RootPath)
            Case Else
                Return False
        End Select
    End Function

End Module
#End If
