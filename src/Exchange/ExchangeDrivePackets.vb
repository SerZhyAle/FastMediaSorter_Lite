#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes

' Specification 203 section 3.3 - the packet queue of DEVICE-EXCHANGE items CN to CS, as pure logic: the manifest
' (version 1 as the shipped phone writes it, plus the members item CN adds), the rules that decide whether a packet
' is for this device, expired, or an unfinished upload, the quota arithmetic of item CS and the polling cadence and
' back-off of item CQ. Nothing here touches the network or the disk; ExchangeDriveClient and ExchangeDriveQueue
' carry it out.
'
' The folder name `cross_device_transfer`, the sub-folder per packetId, the file name `manifest.json` and the order
' (payload files first, the manifest LAST as the commit marker) are frozen by the shipped phone (item CN) and are
' constants of this file.

''' <summary>The `payloadKind` of a manifest. The first two are the shipped phone's; the rest are item CN's additions.</summary>
Friend Enum ExchangeDrivePayloadKind
    Settings
    MediaFiles
    Files
    Clipboard
    Request
    Reply
    ''' <summary>A kind this program does not know: skipped, never applied to the wrong subsystem (item CN).</summary>
    Unknown
End Enum

Friend Enum ExchangeDrivePacketStatus
    Pending
    Claimed
    Expired
End Enum

''' <summary>Why a manifest was not used. A reader skips the packet and goes on; the packet stays for another attempt.</summary>
Friend Enum ExchangeDriveManifestRefusal
    None
    Malformed
    ''' <summary>A `packetId` or a file name outside the forms of item CN.</summary>
    BadName
End Enum

''' <summary>Who a packet is addressed to, from this device's point of view.</summary>
Friend Enum ExchangeDriveAddress
    ''' <summary>No target: offered to every own device.</summary>
    Anyone
    ''' <summary>Targeted at this device.</summary>
    ThisDevice
    ''' <summary>Targeted at another own device: not ours to take or to delete.</summary>
    OtherDevice
End Enum

''' <summary>One payload file of a packet: `name`, `size`, `sha256` (lower-case hex) and `mimeType`.</summary>
Friend NotInheritable Class ExchangeDrivePacketItem

    Public ReadOnly Name As String
    Public ReadOnly Size As Long
    Public ReadOnly Sha256 As String
    Public ReadOnly MimeType As String

    Public Sub New(name As String, size As Long, sha256 As String, mimeType As String)
        Me.Name = name
        Me.Size = size
        Me.Sha256 = If(sha256, "")
        Me.MimeType = If(String.IsNullOrEmpty(mimeType), ExchangeDrivePackets.DefaultMimeType, mimeType)
    End Sub

    Public Overrides Function ToString() As String
        Return "ExchangeDrivePacketItem"
    End Function
End Class

''' <summary>The manifest of one packet. A bag of members, deliberately mutable: the sender fills it, the reader's
''' decoder fills it, and a member this program does not know is ignored on read and never invented on write.</summary>
Friend NotInheritable Class ExchangeDrivePacketManifest

    Public Property PacketId As String = ""
    ''' <summary>1 when the member is absent (the shipped phone's manifests).</summary>
    Public Property SchemaVersion As Integer = 1
    ''' <summary>The writer's clock: informational, item CR decides expiry on Drive's clock.</summary>
    Public Property CreatedAtEpochMs As Long
    Public Property SenderDeviceName As String = ""
    ''' <summary>"" means `null` on the wire: any own device.</summary>
    Public Property TargetDeviceName As String = ""
    Public Property SenderDeviceId As String = ""
    Public Property TargetDeviceId As String = ""
    ''' <summary>The `payloadKind` text as written, kept so a kind this program does not know can be named in a log.</summary>
    Public Property PayloadKindText As String = ""
    Public Property PayloadKind As ExchangeDrivePayloadKind = ExchangeDrivePayloadKind.Unknown
    Public Property FileNames As IReadOnlyList(Of String) = Array.Empty(Of String)()
    ''' <summary>The `files` objects, in the order of FileNames; empty for a version-1 manifest.</summary>
    Public Property Files As IReadOnlyList(Of ExchangeDrivePacketItem) = Array.Empty(Of ExchangeDrivePacketItem)()
    Public Property TotalSizeBytes As Long
    ''' <summary>0 when absent.</summary>
    Public Property TtlDays As Integer
    ''' <summary>0 when absent; when present it replaces TtlDays (item CN).</summary>
    Public Property TtlSeconds As Integer
    Public Property Status As ExchangeDrivePacketStatus = ExchangeDrivePacketStatus.Pending
    Public Property InReplyTo As String = ""
    ''' <summary>`clipKind` of a CLIPBOARD packet: text, image or file.</summary>
    Public Property ClipKind As String = ""
    ''' <summary>The `request` member of a REQUEST packet, raw JSON.</summary>
    Public Property RequestJson As String = ""
    ''' <summary>`outcome` of a REPLY: "ok" or "refused".</summary>
    Public Property Outcome As String = ""
    ''' <summary>`reason` of a refused REPLY.</summary>
    Public Property Reason As String = ""
    ''' <summary>`asOf` of a fetch/changes REPLY, on the producer's clock.</summary>
    Public Property AsOf As String = ""
    ''' <summary>`final` of a REPLY: the last of several replies to one request.</summary>
    Public Property Final As Boolean

    Public Overrides Function ToString() As String
        Return "ExchangeDrivePacketManifest"
    End Function
End Class

''' <summary>The manifest codec, the rules of items CN, CR and CS, and the polling policy of item CQ.</summary>
Friend NotInheritable Class ExchangeDrivePackets

    Private Sub New()
    End Sub

    ''' <summary>The folder at the root of appDataFolder that holds the queue (frozen by the shipped phone).</summary>
    Public Const QueueFolderName As String = "cross_device_transfer"
    ''' <summary>The commit marker, written last (frozen by the shipped phone).</summary>
    Public Const ManifestFileName As String = "manifest.json"
    Public Const FolderMimeType As String = "application/vnd.google-apps.folder"
    Public Const DefaultMimeType As String = "application/octet-stream"
    ''' <summary>The one payload file of a text CLIPBOARD packet (item CP).</summary>
    Public Const ClipboardTextFileName As String = "clipboard.txt"

    Public Const MaxPacketIdLength As Integer = 64
    Public Const MaxManifestBytes As Integer = 1024 * 1024
    Public Const MaxFilesPerPacket As Integer = 1000

    ''' <summary>Item CS: the largest packet a device writes, 5 GiB by default (a product setting).</summary>
    Public Const DefaultPacketBytes As Long = 5L * 1024 * 1024 * 1024

    ''' <summary>Item CN: a payload file above this size goes by Drive's resumable upload, from a stream.</summary>
    Public Const ResumableThresholdBytes As Long = 5L * 1024 * 1024

    Public Const DefaultTtlDays As Integer = 7
    Public Const DefaultRequestTtlSeconds As Integer = 900
    Public Const DefaultReplyTtlSeconds As Integer = 24 * 60 * 60
    ''' <summary>Item CR: a packet folder with no manifest this old is an upload that never finished.</summary>
    Public Const OrphanFolderDays As Integer = 7
    ''' <summary>Item CP: a CLIPBOARD packet that is not the value is deleted only when this much older than the value.</summary>
    Public Const ClipboardSupersedeMinutes As Integer = 10

    ' ------------------------------------------------------------------------------------------ names

    ''' <summary>At most 64 characters of [A-Za-z0-9_-], opaque to a reader (item CN).</summary>
    Public Shared Function IsValidPacketId(id As String) As Boolean
        If String.IsNullOrEmpty(id) OrElse id.Length > MaxPacketIdLength Then Return False
        For Each c As Char In id
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "-"c OrElse c = "_"c
            If Not ok Then Return False
        Next
        Return True
    End Function

    ''' <summary>A base name with no `/`, `\` or control character, not `.`, `..` or `manifest.json` (item CN).</summary>
    Public Shared Function IsValidPayloadName(name As String) As Boolean
        If String.IsNullOrEmpty(name) OrElse name.Length > 255 Then Return False
        If name = "." OrElse name = ".." OrElse String.Equals(name, ManifestFileName, StringComparison.Ordinal) Then Return False
        For Each c As Char In name
            If c = "/"c OrElse c = "\"c OrElse Char.IsControl(c) Then Return False
        Next
        Return True
    End Function

    ''' <summary>A new packet id in the share id form of ANYWHERE-ACCESS 6.2 (the same 22 characters as every id).</summary>
    Public Shared Function NewPacketId() As String
        Return ExchangeIds.NewId()
    End Function

    ''' <summary>The name a received file is saved under (item CN): the base name only, a character the file system
    ''' refuses replaced, never empty and never a reserved device name. Overwriting is the caller's to avoid - it adds a
    ''' suffix, see <see cref="WithSuffix"/>.</summary>
    Public Shared Function SafeFileName(name As String) As String
        Dim baseName As String = If(name, "")
        Dim cut As Integer = Math.Max(baseName.LastIndexOf("/"c), baseName.LastIndexOf("\"c))
        If cut >= 0 Then baseName = baseName.Substring(cut + 1)
        Dim invalid As Char() = Path.GetInvalidFileNameChars()
        Dim builder As New StringBuilder(baseName.Length)
        For Each c As Char In baseName
            builder.Append(If(Array.IndexOf(invalid, c) >= 0 OrElse Char.IsControl(c), "_"c, c))
        Next
        Dim cleaned As String = builder.ToString().Trim().TrimEnd("."c, " "c)
        If cleaned.Length = 0 Then cleaned = "file"
        Dim stem As String = Path.GetFileNameWithoutExtension(cleaned)
        Select Case stem.ToUpperInvariant()
            Case "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                 "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
                cleaned = "_" & cleaned
        End Select
        Return cleaned
    End Function

    ''' <summary>"name (2).ext" - the suffix a received file takes instead of overwriting (item CB).</summary>
    Public Shared Function WithSuffix(fileName As String, number As Integer) As String
        Dim extension As String = Path.GetExtension(fileName)
        Dim stem As String = fileName.Substring(0, fileName.Length - extension.Length)
        Return stem & " (" & number.ToString(CultureInfo.InvariantCulture) & ")" & extension
    End Function

    ' ------------------------------------------------------------------------------------------ the codec

    Public Shared Function KindText(kind As ExchangeDrivePayloadKind) As String
        Select Case kind
            Case ExchangeDrivePayloadKind.Settings : Return "SETTINGS"
            Case ExchangeDrivePayloadKind.MediaFiles : Return "MEDIA_FILES"
            Case ExchangeDrivePayloadKind.Files : Return "FILES"
            Case ExchangeDrivePayloadKind.Clipboard : Return "CLIPBOARD"
            Case ExchangeDrivePayloadKind.Request : Return "REQUEST"
            Case ExchangeDrivePayloadKind.Reply : Return "REPLY"
            Case Else : Return ""
        End Select
    End Function

    Public Shared Function ParseKind(text As String) As ExchangeDrivePayloadKind
        Select Case text
            Case "SETTINGS" : Return ExchangeDrivePayloadKind.Settings
            Case "MEDIA_FILES" : Return ExchangeDrivePayloadKind.MediaFiles
            Case "FILES" : Return ExchangeDrivePayloadKind.Files
            Case "CLIPBOARD" : Return ExchangeDrivePayloadKind.Clipboard
            Case "REQUEST" : Return ExchangeDrivePayloadKind.Request
            Case "REPLY" : Return ExchangeDrivePayloadKind.Reply
            Case Else : Return ExchangeDrivePayloadKind.Unknown
        End Select
    End Function

    ''' <summary>The manifest bytes of a packet this program writes: every member of version 1 (the shipped phone's reader
    ''' filters on them) and every member item CN adds, with `schemaVersion` 2 because a kind written here is never a
    ''' version-1 kind. Nothing when the manifest breaks the forms of item CN.</summary>
    Public Shared Function Encode(manifest As ExchangeDrivePacketManifest) As Byte()
        If manifest Is Nothing OrElse Not IsValidPacketId(manifest.PacketId) Then Return Nothing
        If manifest.PayloadKind = ExchangeDrivePayloadKind.Unknown Then Return Nothing
        For Each name As String In manifest.FileNames
            If Not IsValidPayloadName(name) Then Return Nothing
        Next
        If manifest.Files.Count > 0 AndAlso manifest.Files.Count <> manifest.FileNames.Count Then Return Nothing
        For index As Integer = 0 To manifest.Files.Count - 1
            If Not String.Equals(manifest.Files(index).Name, manifest.FileNames(index), StringComparison.Ordinal) Then Return Nothing
        Next

        Dim root As New JsonObject()
        root("packetId") = manifest.PacketId
        root("schemaVersion") = 2
        root("createdAtEpochMs") = manifest.CreatedAtEpochMs
        root("senderDeviceName") = manifest.SenderDeviceName
        root("targetDeviceName") = If(manifest.TargetDeviceName.Length = 0, Nothing, JsonValue.Create(manifest.TargetDeviceName))
        If manifest.SenderDeviceId.Length > 0 Then root("senderDeviceId") = manifest.SenderDeviceId
        If manifest.TargetDeviceId.Length > 0 Then root("targetDeviceId") = manifest.TargetDeviceId
        root("payloadKind") = KindText(manifest.PayloadKind)
        Dim names As New JsonArray()
        For Each name As String In manifest.FileNames
            names.Add(JsonValue.Create(name))
        Next
        root("fileNames") = names
        If manifest.Files.Count > 0 Then
            Dim files As New JsonArray()
            For Each item As ExchangeDrivePacketItem In manifest.Files
                Dim entry As New JsonObject()
                entry("name") = item.Name
                entry("size") = item.Size
                entry("sha256") = item.Sha256
                entry("mimeType") = item.MimeType
                files.Add(entry)
            Next
            root("files") = files
        End If
        root("totalSizeBytes") = manifest.TotalSizeBytes
        ' ttlDays is written beside ttlSeconds so the shipped reader, which knows only days, still has an expiry.
        root("ttlDays") = If(manifest.TtlDays > 0, manifest.TtlDays, DefaultTtlDays)
        If manifest.TtlSeconds > 0 Then root("ttlSeconds") = manifest.TtlSeconds
        root("status") = "PENDING"
        If manifest.InReplyTo.Length > 0 Then root("inReplyTo") = manifest.InReplyTo
        If manifest.ClipKind.Length > 0 Then root("clipKind") = manifest.ClipKind
        If manifest.RequestJson.Length > 0 Then
            Dim request As JsonNode = Nothing
            Try
                request = JsonNode.Parse(manifest.RequestJson)
            Catch ex As JsonException
                Return Nothing
            End Try
            root("request") = request
        End If
        If manifest.Outcome.Length > 0 Then root("outcome") = manifest.Outcome
        If manifest.Reason.Length > 0 Then root("reason") = manifest.Reason
        If manifest.AsOf.Length > 0 Then root("asOf") = manifest.AsOf
        If manifest.Final Then root("final") = True
        Return Encoding.UTF8.GetBytes(root.ToJsonString())
    End Function

    ''' <summary>Reads a manifest (version 1 or 2). A member this program does not know is ignored; a `payloadKind` it does
    ''' not know decodes as <see cref="ExchangeDrivePayloadKind.Unknown"/> - the caller skips such a packet. Never throws
    ''' on content.</summary>
    Public Shared Function TryDecode(body As Byte(), ByRef manifest As ExchangeDrivePacketManifest, ByRef refusal As ExchangeDriveManifestRefusal) As Boolean
        manifest = Nothing
        refusal = ExchangeDriveManifestRefusal.None
        If body Is Nothing OrElse body.Length = 0 OrElse body.Length > MaxManifestBytes Then
            refusal = ExchangeDriveManifestRefusal.Malformed
            Return False
        End If
        Try
            Using document As JsonDocument = JsonDocument.Parse(body)
                Dim root As JsonElement = document.RootElement
                If root.ValueKind <> JsonValueKind.Object Then
                    refusal = ExchangeDriveManifestRefusal.Malformed
                    Return False
                End If

                Dim result As New ExchangeDrivePacketManifest()
                result.PacketId = ReadText(root, "packetId")
                If Not IsValidPacketId(result.PacketId) Then
                    refusal = ExchangeDriveManifestRefusal.BadName
                    Return False
                End If
                result.SchemaVersion = CInt(Math.Max(1L, ReadNumber(root, "schemaVersion", 1)))
                result.CreatedAtEpochMs = ReadNumber(root, "createdAtEpochMs", 0)
                result.SenderDeviceName = ReadText(root, "senderDeviceName")
                result.TargetDeviceName = ReadText(root, "targetDeviceName")
                result.SenderDeviceId = ReadText(root, "senderDeviceId")
                result.TargetDeviceId = ReadText(root, "targetDeviceId")
                result.PayloadKindText = ReadText(root, "payloadKind")
                result.PayloadKind = ParseKind(result.PayloadKindText)
                result.TotalSizeBytes = ReadNumber(root, "totalSizeBytes", 0)
                result.TtlDays = CInt(Math.Max(0L, Math.Min(ReadNumber(root, "ttlDays", 0), 36500L)))
                result.TtlSeconds = CInt(Math.Max(0L, Math.Min(ReadNumber(root, "ttlSeconds", 0), Integer.MaxValue)))
                result.InReplyTo = ReadText(root, "inReplyTo")
                result.ClipKind = ReadText(root, "clipKind")
                result.Outcome = ReadText(root, "outcome")
                result.Reason = ReadText(root, "reason")
                result.AsOf = ReadText(root, "asOf")
                Dim finalEl As JsonElement = Nothing
                result.Final = root.TryGetProperty("final", finalEl) AndAlso finalEl.ValueKind = JsonValueKind.True
                Dim requestEl As JsonElement = Nothing
                If root.TryGetProperty("request", requestEl) AndAlso requestEl.ValueKind = JsonValueKind.Object Then result.RequestJson = requestEl.GetRawText()

                Select Case ReadText(root, "status")
                    Case "CLAIMED" : result.Status = ExchangeDrivePacketStatus.Claimed
                    Case "EXPIRED" : result.Status = ExchangeDrivePacketStatus.Expired
                    Case Else : result.Status = ExchangeDrivePacketStatus.Pending
                End Select

                Dim names As New List(Of String)
                Dim namesEl As JsonElement = Nothing
                If root.TryGetProperty("fileNames", namesEl) AndAlso namesEl.ValueKind = JsonValueKind.Array Then
                    If namesEl.GetArrayLength() > MaxFilesPerPacket Then
                        refusal = ExchangeDriveManifestRefusal.Malformed
                        Return False
                    End If
                    For Each item As JsonElement In namesEl.EnumerateArray()
                        If item.ValueKind <> JsonValueKind.String OrElse Not IsValidPayloadName(item.GetString()) Then
                            refusal = ExchangeDriveManifestRefusal.BadName
                            Return False
                        End If
                        names.Add(item.GetString())
                    Next
                End If
                result.FileNames = names

                Dim items As New List(Of ExchangeDrivePacketItem)
                Dim filesEl As JsonElement = Nothing
                If root.TryGetProperty("files", filesEl) AndAlso filesEl.ValueKind = JsonValueKind.Array Then
                    If filesEl.GetArrayLength() > MaxFilesPerPacket Then
                        refusal = ExchangeDriveManifestRefusal.Malformed
                        Return False
                    End If
                    For Each entry As JsonElement In filesEl.EnumerateArray()
                        If entry.ValueKind <> JsonValueKind.Object Then
                            refusal = ExchangeDriveManifestRefusal.Malformed
                            Return False
                        End If
                        Dim name As String = ReadText(entry, "name")
                        If Not IsValidPayloadName(name) Then
                            refusal = ExchangeDriveManifestRefusal.BadName
                            Return False
                        End If
                        Dim size As Long = ReadNumber(entry, "size", -1)
                        If size < 0 Then
                            refusal = ExchangeDriveManifestRefusal.Malformed
                            Return False
                        End If
                        items.Add(New ExchangeDrivePacketItem(name, size, ReadText(entry, "sha256").ToLowerInvariant(), ReadText(entry, "mimeType")))
                    Next
                End If
                result.Files = items

                manifest = result
                Return True
            End Using
        Catch ex As JsonException
            refusal = ExchangeDriveManifestRefusal.Malformed
            Return False
        End Try
    End Function

    Private Shared Function ReadText(element As JsonElement, name As String) As String
        Dim el As JsonElement = Nothing
        If element.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
        Return ""
    End Function

    Private Shared Function ReadNumber(element As JsonElement, name As String, fallback As Long) As Long
        Dim el As JsonElement = Nothing
        Dim value As Long = 0
        If element.TryGetProperty(name, el) AndAlso el.ValueKind = JsonValueKind.Number AndAlso el.TryGetInt64(value) Then Return value
        Return fallback
    End Function

    ' ------------------------------------------------------------------------------------------ item CR / CN rules

    ''' <summary>The lifetime in seconds item CR gives a packet, or -1 for one that never expires (CLIPBOARD, item CP):
    ''' `ttlSeconds` when present, else `ttlDays`, else the default of the kind - 7 days for files, 900 seconds for a
    ''' REQUEST, 1 day for a REPLY.</summary>
    Public Shared Function LifetimeSeconds(manifest As ExchangeDrivePacketManifest) As Long
        If manifest.PayloadKind = ExchangeDrivePayloadKind.Clipboard Then Return -1
        If manifest.TtlSeconds > 0 Then Return manifest.TtlSeconds
        If manifest.TtlDays > 0 Then Return CLng(manifest.TtlDays) * 86400L
        Select Case manifest.PayloadKind
            Case ExchangeDrivePayloadKind.Request : Return DefaultRequestTtlSeconds
            Case ExchangeDrivePayloadKind.Reply : Return DefaultReplyTtlSeconds
            Case Else : Return CLng(DefaultTtlDays) * 86400L
        End Select
    End Function

    ''' <summary>Expiry on Drive's clock (item CR, as item C does for records): the packet FOLDER's Drive `createdTime`
    ''' against the `Date` header of the response, plus the lifetime. The manifest's own `createdAtEpochMs` is never
    ''' consulted. An unparsable folder time reads as Live - a packet is never deleted on a guess.</summary>
    Public Shared Function Judge(folderCreatedTime As String, driveNow As DateTimeOffset, manifest As ExchangeDrivePacketManifest) As ExchangeDriveRecordState
        Dim lifetime As Long = LifetimeSeconds(manifest)
        If lifetime < 0 Then Return ExchangeDriveRecordState.Live
        Dim created As DateTimeOffset
        If Not ExchangeDriveRecords.TryParseDriveTime(folderCreatedTime, created) Then Return ExchangeDriveRecordState.Live
        Return If(driveNow - created >= TimeSpan.FromSeconds(lifetime), ExchangeDriveRecordState.Expired, ExchangeDriveRecordState.Live)
    End Function

    ''' <summary>True for a packet folder that has no manifest and is older than <see cref="OrphanFolderDays"/> on Drive's
    ''' clock: an upload that never finished, which any device with the channel on deletes (item CR).</summary>
    Public Shared Function IsOrphanExpired(folderCreatedTime As String, driveNow As DateTimeOffset) As Boolean
        Dim created As DateTimeOffset
        If Not ExchangeDriveRecords.TryParseDriveTime(folderCreatedTime, created) Then Return False
        Return driveNow - created >= TimeSpan.FromDays(OrphanFolderDays)
    End Function

    ''' <summary>Item CR: `targetDeviceId` decides when present, else `targetDeviceName` (compared as the display name it
    ''' is, ignoring case), else the packet is for any own device.</summary>
    Public Shared Function AddresseeOf(manifest As ExchangeDrivePacketManifest, ownDeviceId As String, ownDeviceName As String) As ExchangeDriveAddress
        If manifest.TargetDeviceId.Length > 0 Then
            Return If(String.Equals(manifest.TargetDeviceId, ownDeviceId, StringComparison.Ordinal), ExchangeDriveAddress.ThisDevice, ExchangeDriveAddress.OtherDevice)
        End If
        If manifest.TargetDeviceName.Length > 0 Then
            Return If(String.Equals(manifest.TargetDeviceName.Trim(), If(ownDeviceName, "").Trim(), StringComparison.OrdinalIgnoreCase), ExchangeDriveAddress.ThisDevice, ExchangeDriveAddress.OtherDevice)
        End If
        Return ExchangeDriveAddress.Anyone
    End Function

    ''' <summary>True when this device wrote the packet itself. An untargeted packet is offered to every own device, but a
    ''' sender is not its own recipient. `senderDeviceId` decides when present; the shipped phone writes names only.</summary>
    Public Shared Function IsOwnPacket(manifest As ExchangeDrivePacketManifest, ownDeviceId As String, ownDeviceName As String) As Boolean
        If manifest.SenderDeviceId.Length > 0 Then Return String.Equals(manifest.SenderDeviceId, ownDeviceId, StringComparison.Ordinal)
        Return manifest.SenderDeviceName.Length > 0 AndAlso String.Equals(manifest.SenderDeviceName.Trim(), If(ownDeviceName, "").Trim(), StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Whether this device applies a packet of this kind. A kind it does not know, and a kind belonging to a
    ''' subsystem not built here (SETTINGS is the phone's own export), is skipped and left in the queue.</summary>
    Public Shared Function IsReceivableKind(kind As ExchangeDrivePayloadKind) As Boolean
        Select Case kind
            Case ExchangeDrivePayloadKind.Files, ExchangeDrivePayloadKind.MediaFiles, ExchangeDrivePayloadKind.Clipboard,
                 ExchangeDrivePayloadKind.Request, ExchangeDrivePayloadKind.Reply
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>The names a receiver expects in the packet folder: `files` when present (it carries the checks), else the
    ''' version-1 `fileNames` with no size or hash to check beyond the manifest's total.</summary>
    Public Shared Function ExpectedItems(manifest As ExchangeDrivePacketManifest) As IReadOnlyList(Of ExchangeDrivePacketItem)
        If manifest.Files.Count > 0 Then Return manifest.Files
        Dim items As New List(Of ExchangeDrivePacketItem)
        For Each name As String In manifest.FileNames
            items.Add(New ExchangeDrivePacketItem(name, -1, "", DefaultMimeType))
        Next
        Return items
    End Function

    ' ------------------------------------------------------------------------------------------ item CS

    ''' <summary>Item CS: before any byte is uploaded a sender refuses a packet that would not fit with 1% of the limit to
    ''' spare. <paramref name="limitBytes"/> 0 (Drive reports no limit for an unlimited plan) always fits.
    ''' <paramref name="freeBytes"/> is what is left after the 1% reserve, for the message "not enough space on your
    ''' Google Drive" - never negative.</summary>
    Public Shared Function FitsQuota(limitBytes As Long, usageBytes As Long, packetBytes As Long, ByRef freeBytes As Long) As Boolean
        If limitBytes <= 0 Then
            freeBytes = Long.MaxValue
            Return True
        End If
        Dim reserve As Long = limitBytes \ 100
        freeBytes = Math.Max(0L, limitBytes - usageBytes - reserve)
        Return packetBytes <= freeBytes
    End Function

End Class

''' <summary>Item CQ: Drive pushes nothing to an installed program, so every device polls - at these intervals, backing
''' off on a rate limit or a server error.</summary>
Friend NotInheritable Class ExchangeDrivePolling

    Private Sub New()
    End Sub

    ''' <summary>While a screen waits for a reply, a send is in progress or the queue screen is open.</summary>
    Public Const ActiveSeconds As Integer = 10
    ''' <summary>While the program runs with none of those.</summary>
    Public Const IdleSeconds As Integer = 60
    ''' <summary>A consumer waiting for a REPLY gives up after this long and deletes its own REQUEST.</summary>
    Public Const ReplyWaitSeconds As Integer = 120
    Public Const BackoffStartSeconds As Integer = 1
    Public Const BackoffMaxSeconds As Integer = 300

    Public Shared Function IntervalSeconds(active As Boolean) As Integer
        Return If(active, ActiveSeconds, IdleSeconds)
    End Function

    ''' <summary>Exponential back-off from 1 second, doubling to at most 5 minutes, with random jitter; a success resets it
    ''' (the caller then resumes its interval).</summary>
    Public NotInheritable Class Backoff

        Private _failures As Integer

        Public ReadOnly Property Failures As Integer
            Get
                Return _failures
            End Get
        End Property

        Public Sub Reset()
            _failures = 0
        End Sub

        ''' <summary>The wait after one more failure. <paramref name="jitter"/> is a value in [0, 1); the delay is the
        ''' doubled base scaled into [0.5, 1.0] of itself, so it never exceeds the cap.</summary>
        Public Function NextDelay(jitter As Double) As TimeSpan
            _failures += 1
            Dim shift As Integer = Math.Min(_failures - 1, 20)
            Dim baseSeconds As Double = Math.Min(CDbl(BackoffMaxSeconds), BackoffStartSeconds * Math.Pow(2.0, shift))
            Dim clamped As Double = Math.Max(0.0, Math.Min(0.999999, jitter))
            Return TimeSpan.FromSeconds(baseSeconds * (0.5 + 0.5 * clamped))
        End Function
    End Class

End Class
#End If
