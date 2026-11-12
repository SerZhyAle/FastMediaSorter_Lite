#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Ticket 269 P3 - the `request` member of a REQUEST packet and the `listing.json` of its REPLY (DEVICE-EXCHANGE section 24,
' item CO), as pure logic shared by the producer (the Share Manager answers) and the consumer (the viewer asks through the
' agent). Nothing here touches the network or the disk; the queue (ExchangeDriveQueue) carries the packets and the producer
' (DriveRequestProducer) reads the folder.
'
' Rules this file keeps (cited, not restated):
'   - `path` and each of `paths` are relative to the resource root, `/`-separated, with no `.` or `..` segment (CO). A path
'     that is not in that form is the producer's `not-permitted`; this file also refuses a segment Windows would read as
'     something else (`\`, `:`, a control character, a trailing space or dot), because such a segment can leave the root
'     or name a stream instead of a file.
'   - `fetch` carries at most 100 paths (CO).
'   - Times are RFC 3339 UTC on the producer's clock (CO); `modifiedAfter` is the previous reply's `asOf`, never the
'     consumer's clock.
'   - One reply carries at most `packetBytes`; a larger answer is split into several replies with `final: true` on the last
'     (CO, CS). The payload files of one packet carry unique base names (CN), so two files of one answer with the same base
'     name travel in different replies.

''' <summary>The `action` of a request (item CO).</summary>
Friend Enum ExchangeDriveRequestAction
    ''' <summary>An action this version does not know: the producer leaves the request alone (a newer consumer's).</summary>
    Unknown
    List
    Fetch
    Changes
End Enum

''' <summary>One request as the `request` member carries it.</summary>
Friend NotInheritable Class ExchangeDriveRequest

    Public Property Action As ExchangeDriveRequestAction = ExchangeDriveRequestAction.Unknown
    Public Property ResourceId As String = ""
    ''' <summary>The folder of `list` and `changes`; "" is the root.</summary>
    Public Property Path As String = ""
    ''' <summary>The files of `fetch`.</summary>
    Public Property Paths As IReadOnlyList(Of String) = Array.Empty(Of String)()
    ''' <summary>The `modifiedAfter` of `changes`; <see cref="HasModifiedAfter"/> False means "every file" (the first run).</summary>
    Public Property ModifiedAfter As DateTimeOffset
    Public Property HasModifiedAfter As Boolean

    Public Overrides Function ToString() As String
        Return "ExchangeDriveRequest(" & Action.ToString() & ")"
    End Function
End Class

''' <summary>One entry of `listing.json`.</summary>
Friend NotInheritable Class ExchangeDriveListingEntry

    Public ReadOnly Name As String
    Public ReadOnly IsDirectory As Boolean
    Public ReadOnly Size As Long
    Public ReadOnly ModifiedAt As DateTimeOffset

    Public Sub New(name As String, isDirectory As Boolean, size As Long, modifiedAt As DateTimeOffset)
        Me.Name = If(name, "")
        Me.IsDirectory = isDirectory
        Me.Size = If(isDirectory, 0, Math.Max(0, size))
        Me.ModifiedAt = modifiedAt
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeDriveListingEntry"
    End Function
End Class

''' <summary>A decoded `listing.json`.</summary>
Friend NotInheritable Class ExchangeDriveListingReply

    Public ReadOnly AsOf As String
    Public ReadOnly Entries As IReadOnlyList(Of ExchangeDriveListingEntry)

    Public Sub New(asOf As String, entries As IReadOnlyList(Of ExchangeDriveListingEntry))
        Me.AsOf = If(asOf, "")
        Me.Entries = entries
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeDriveListingReply"
    End Function
End Class

''' <summary>One file of a fetch or changes answer, as the producer planned it.</summary>
Friend NotInheritable Class ExchangeDriveAnswerFile

    ''' <summary>Relative to the resource root, `/`-separated.</summary>
    Public ReadOnly RelativePath As String
    Public ReadOnly Size As Long

    Public Sub New(relativePath As String, size As Long)
        Me.RelativePath = If(relativePath, "")
        Me.Size = Math.Max(0, size)
    End Sub

    ''' <summary>The payload name in the reply: the last segment (item CN).</summary>
    Public ReadOnly Property Name As String
        Get
            Dim cut As Integer = RelativePath.LastIndexOf("/"c)
            Return If(cut < 0, RelativePath, RelativePath.Substring(cut + 1))
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "ExchangeDriveAnswerFile"
    End Function
End Class

Friend NotInheritable Class ExchangeDriveRequests

    Private Sub New()
    End Sub

    Public Const ActionList As String = "list"
    Public Const ActionFetch As String = "fetch"
    Public Const ActionChanges As String = "changes"

    Public Const OutcomeOk As String = "ok"
    Public Const OutcomeRefused As String = "refused"

    ''' <summary>The resource is not published, or the path does not exist.</summary>
    Public Const ReasonUnavailable As String = "unavailable"
    ''' <summary>The path leaves the root.</summary>
    Public Const ReasonNotPermitted As String = "not-permitted"
    ''' <summary>The root carries `protection`; Drive packets carry no PIN proof in this version.</summary>
    Public Const ReasonPinRequired As String = "pin-required"
    ''' <summary>The answer would exceed `packetBytes` (a single file above it cannot be split).</summary>
    Public Const ReasonTooLarge As String = "too-large"
    ''' <summary>The producer's upload was refused for space.</summary>
    Public Const ReasonDriveFull As String = "drive-full"

    Public Const ListingFileName As String = "listing.json"
    Public Const ListingMimeType As String = "application/json"

    Public Const MaxFetchPaths As Integer = 100
    ''' <summary>The longest resource id read from a request (the id forms of item BA are far shorter).</summary>
    Public Const MaxResourceIdLength As Integer = 128
    ''' <summary>The longest relative path read from a request.</summary>
    Public Const MaxPathLength As Integer = 4096
    ''' <summary>The largest `listing.json` a consumer reads.</summary>
    Public Const MaxListingBytes As Integer = 16 * 1024 * 1024

    ' ------------------------------------------------------------------------------------------ the request member

    Public Shared Function ActionText(action As ExchangeDriveRequestAction) As String
        Select Case action
            Case ExchangeDriveRequestAction.List : Return ActionList
            Case ExchangeDriveRequestAction.Fetch : Return ActionFetch
            Case ExchangeDriveRequestAction.Changes : Return ActionChanges
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>The `request` member of a REQUEST, compact JSON; "" when the request is not one this version may send.</summary>
    Public Shared Function Build(request As ExchangeDriveRequest) As String
        If request Is Nothing OrElse request.Action = ExchangeDriveRequestAction.Unknown Then Return ""
        If request.ResourceId.Length = 0 OrElse request.ResourceId.Length > MaxResourceIdLength Then Return ""
        Dim o As New JsonObject()
        o("action") = ActionText(request.Action)
        o("resourceId") = request.ResourceId
        Select Case request.Action
            Case ExchangeDriveRequestAction.List
                If Not IsRelativePath(request.Path) Then Return ""
                o("path") = request.Path
            Case ExchangeDriveRequestAction.Fetch
                If request.Paths.Count = 0 OrElse request.Paths.Count > MaxFetchPaths Then Return ""
                Dim list As New JsonArray()
                For Each item As String In request.Paths
                    If Not IsRelativePath(item) OrElse item.Length = 0 Then Return ""
                    list.Add(item)
                Next
                o("paths") = list
            Case ExchangeDriveRequestAction.Changes
                If Not IsRelativePath(request.Path) Then Return ""
                o("path") = request.Path
                If request.HasModifiedAfter Then o("modifiedAfter") = FormatInstant(request.ModifiedAfter)
        End Select
        Return o.ToJsonString()
    End Function

    ''' <summary>Reads the `request` member. False when it is no JSON object or names no resource; an action this version does
    ''' not know comes back True with <see cref="ExchangeDriveRequestAction.Unknown"/>. A path is returned as written: the
    ''' producer judges it with <see cref="IsRelativePath"/> and answers `not-permitted`.</summary>
    Public Shared Function TryParse(json As String, ByRef request As ExchangeDriveRequest) As Boolean
        request = Nothing
        If String.IsNullOrEmpty(json) Then Return False
        Try
            Using document As JsonDocument = JsonDocument.Parse(json)
                Dim root As JsonElement = document.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return False
                Dim result As New ExchangeDriveRequest()
                Select Case ReadText(root, "action")
                    Case ActionList : result.Action = ExchangeDriveRequestAction.List
                    Case ActionFetch : result.Action = ExchangeDriveRequestAction.Fetch
                    Case ActionChanges : result.Action = ExchangeDriveRequestAction.Changes
                    Case Else : result.Action = ExchangeDriveRequestAction.Unknown
                End Select
                result.ResourceId = ReadText(root, "resourceId")
                If result.ResourceId.Length = 0 OrElse result.ResourceId.Length > MaxResourceIdLength Then Return False
                result.Path = ReadText(root, "path")
                Dim pathsEl As JsonElement
                If root.TryGetProperty("paths", pathsEl) AndAlso pathsEl.ValueKind = JsonValueKind.Array Then
                    Dim paths As New List(Of String)
                    For Each item As JsonElement In pathsEl.EnumerateArray()
                        ' a member that is no string is kept as a path no root holds, so the answer still names it refused
                        paths.Add(If(item.ValueKind = JsonValueKind.String, If(item.GetString(), ""), ChrW(0)))
                        If paths.Count > MaxFetchPaths Then Exit For
                    Next
                    result.Paths = paths
                End If
                Dim after As String = ReadText(root, "modifiedAfter")
                If after.Length > 0 Then
                    Dim instant As DateTimeOffset
                    If Not TryParseInstant(after, instant) Then Return False
                    result.ModifiedAfter = instant
                    result.HasModifiedAfter = True
                End If
                request = result
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    Private Shared Function ReadText(root As JsonElement, name As String) As String
        Dim el As JsonElement
        If root.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    ' ------------------------------------------------------------------------------------------ paths

    ''' <summary>True for "" (the root) and for `/`-separated segments none of which is empty, `.`, `..`, or a segment Windows
    ''' would read otherwise (a `\`, a `:`, a control character, a trailing space or dot). A leading or trailing `/` is refused
    ''' too: the form is relative.</summary>
    Public Shared Function IsRelativePath(path As String) As Boolean
        If path Is Nothing OrElse path.Length > MaxPathLength Then Return False
        If path.Length = 0 Then Return True
        For Each segment As String In path.Split("/"c)
            If segment.Length = 0 OrElse segment = "." OrElse segment = ".." Then Return False
            If segment.EndsWith(" ", StringComparison.Ordinal) OrElse segment.EndsWith(".", StringComparison.Ordinal) Then Return False
            For Each c As Char In segment
                If c = "\"c OrElse c = ":"c OrElse Char.IsControl(c) Then Return False
            Next
        Next
        Return True
    End Function

    ''' <summary>The segments of a path <see cref="IsRelativePath"/> accepted ("" has none).</summary>
    Public Shared Function Segments(path As String) As String()
        If String.IsNullOrEmpty(path) Then Return Array.Empty(Of String)()
        Return path.Split("/"c)
    End Function

    ''' <summary>A relative path below <paramref name="parent"/> ("" is the root).</summary>
    Public Shared Function Join(parent As String, name As String) As String
        Return If(String.IsNullOrEmpty(parent), name, parent & "/" & name)
    End Function

    ' ------------------------------------------------------------------------------------------ times

    ''' <summary>RFC 3339 UTC to the second, as the vectors write it. An instant is floored, never rounded up: an `asOf` that is
    ''' a little early only repeats a file in the next `changes`, one that is late would miss it.</summary>
    Public Shared Function FormatInstant(instant As DateTimeOffset) As String
        Dim utc As DateTime = instant.UtcDateTime
        Dim floored As New DateTime(utc.Ticks - (utc.Ticks Mod TimeSpan.TicksPerSecond), DateTimeKind.Utc)
        Return floored.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
    End Function

    Public Shared Function TryParseInstant(text As String, ByRef instant As DateTimeOffset) As Boolean
        Return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal Or DateTimeStyles.AdjustToUniversal, instant) AndAlso
               text.IndexOf("T"c) > 0
    End Function

    ' ------------------------------------------------------------------------------------------ listing.json

    Public Shared Function BuildListing(asOf As DateTimeOffset, entries As IEnumerable(Of ExchangeDriveListingEntry)) As Byte()
        Dim o As New JsonObject()
        o("asOf") = FormatInstant(asOf)
        Dim list As New JsonArray()
        For Each entry As ExchangeDriveListingEntry In If(entries, Array.Empty(Of ExchangeDriveListingEntry)())
            Dim item As New JsonObject()
            item("name") = entry.Name
            item("dir") = entry.IsDirectory
            item("size") = entry.Size
            item("modifiedAt") = FormatInstant(entry.ModifiedAt)
            list.Add(item)
        Next
        o("entries") = list
        Return New UTF8Encoding(False).GetBytes(o.ToJsonString())
    End Function

    ''' <summary>Reads a `listing.json`. An entry whose name is not a single safe segment is dropped (a producer never names a
    ''' file outside the folder); False when the body is no listing.</summary>
    Public Shared Function TryParseListing(body As Byte(), ByRef listing As ExchangeDriveListingReply) As Boolean
        listing = Nothing
        If body Is Nothing OrElse body.Length = 0 OrElse body.Length > MaxListingBytes Then Return False
        Try
            Using document As JsonDocument = JsonDocument.Parse(body)
                Dim root As JsonElement = document.RootElement
                If root.ValueKind <> JsonValueKind.Object Then Return False
                Dim entriesEl As JsonElement
                If Not root.TryGetProperty("entries", entriesEl) OrElse entriesEl.ValueKind <> JsonValueKind.Array Then Return False
                Dim entries As New List(Of ExchangeDriveListingEntry)
                For Each item As JsonElement In entriesEl.EnumerateArray()
                    If item.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim name As String = ReadText(item, "name")
                    If name.Length = 0 OrElse name.IndexOf("/"c) >= 0 OrElse Not IsRelativePath(name) Then Continue For
                    Dim dirEl As JsonElement
                    Dim isDir As Boolean = item.TryGetProperty("dir", dirEl) AndAlso dirEl.ValueKind = JsonValueKind.True
                    Dim size As Long = 0
                    Dim sizeEl As JsonElement
                    If item.TryGetProperty("size", sizeEl) AndAlso sizeEl.ValueKind = JsonValueKind.Number Then sizeEl.TryGetInt64(size)
                    Dim modified As DateTimeOffset = DateTimeOffset.MinValue
                    TryParseInstant(ReadText(item, "modifiedAt"), modified)
                    entries.Add(New ExchangeDriveListingEntry(name, isDir, size, modified))
                Next
                listing = New ExchangeDriveListingReply(ReadText(root, "asOf"), entries)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------ splitting an answer

    ''' <summary>Splits the files of a fetch or changes answer into replies: each at most <paramref name="packetBytes"/>, at most
    ''' <see cref="ExchangeDrivePackets.MaxFilesPerPacket"/> files, with unique base names (case-insensitive, as Windows saves
    ''' them). Files keep their order inside each reply. Nothing when a single file is above <paramref name="packetBytes"/> -
    ''' that answer is `too-large`. An empty answer is one empty reply (the final one says "nothing changed").</summary>
    Public Shared Function SplitReplies(files As IReadOnlyList(Of ExchangeDriveAnswerFile), packetBytes As Long) As List(Of List(Of ExchangeDriveAnswerFile))
        Dim limit As Long = If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes)
        Dim replies As New List(Of List(Of ExchangeDriveAnswerFile))
        Dim sizes As New List(Of Long)
        Dim names As New List(Of HashSet(Of String))
        For Each file As ExchangeDriveAnswerFile In If(files, Array.Empty(Of ExchangeDriveAnswerFile)())
            If file.Size > limit Then Return Nothing
            Dim placed As Boolean = False
            For index As Integer = 0 To replies.Count - 1
                If sizes(index) + file.Size > limit OrElse replies(index).Count >= ExchangeDrivePackets.MaxFilesPerPacket OrElse names(index).Contains(file.Name) Then Continue For
                replies(index).Add(file)
                sizes(index) += file.Size
                names(index).Add(file.Name)
                placed = True
                Exit For
            Next
            If placed Then Continue For
            replies.Add(New List(Of ExchangeDriveAnswerFile) From {file})
            sizes.Add(file.Size)
            names.Add(New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {file.Name})
        Next
        If replies.Count = 0 Then replies.Add(New List(Of ExchangeDriveAnswerFile))
        Return replies
    End Function

End Class
#End If
