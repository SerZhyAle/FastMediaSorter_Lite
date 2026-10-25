#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.ExceptionServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading

' Specification 203 section 3.3 (acceptance criterion 3) - the packet queue `cross_device_transfer` of the Drive channel, over
' ExchangeDriveClient. A device SENDS a packet (payload files first, `manifest.json` last as the commit marker - item CN),
' POLLS the queue (one listing per interval, item CQ), RECEIVES what is addressed to it (size and SHA-256 checked, saved under
' the base name, never over an existing file, never partial - item CN), and DELETES what the contract has it delete (item CR).
' The consumers - resources (228), the clipboard (230), scheduled operations (231) - sit on top of this file and use only the
' primitives here; none of them talks to Drive itself.
'
' The shapes are the pure layer's (ExchangeDrivePackets); this file adds the I/O, the order of the calls and the clean-up.
'
' Rules this file keeps (DEVICE-EXCHANGE items CN-CS, cited, not restated):
'   - A packet whose upload failed is deleted by its sender at once (CR); a folder without a manifest older than 7 days on
'     Drive's clock is an orphan any device deletes; an expired packet is deleted by any device that sees it.
'   - A targeted packet is deleted by its target after EVERY file arrived whole; an untargeted one stays, and a local
'     "handled" set (IExchangeDriveHandledStore) stops this device saving it twice. A manifest is never rewritten.
'   - The clipboard value is the committed CLIPBOARD packet whose manifest has the latest Drive createdTime; the receiver never
'     deletes it; the sender deletes the older ones; anyone deletes one that is 10 minutes older than the value (CP).
'   - Before any byte is uploaded the sender reads the quota and refuses a packet that does not fit with 1% to spare (CS).

''' <summary>One payload file to send: a name, a media type and a way to open it for reading. The stream must be SEEKABLE for a
''' file above the resumable threshold (a file stream or a memory stream both are).</summary>
Friend NotInheritable Class ExchangeDriveSendSource

    Public ReadOnly Name As String
    Public ReadOnly MimeType As String
    Private ReadOnly _open As Func(Of Stream)

    Public Sub New(name As String, mimeType As String, open As Func(Of Stream))
        If open Is Nothing Then Throw New ArgumentNullException(NameOf(open))
        Me.Name = If(name, "")
        Me.MimeType = If(String.IsNullOrEmpty(mimeType), ExchangeDrivePackets.DefaultMimeType, mimeType)
        _open = open
    End Sub

    Public Function OpenRead() As Stream
        Return _open()
    End Function

    Public Shared Function FromBytes(name As String, mimeType As String, bytes As Byte()) As ExchangeDriveSendSource
        If bytes Is Nothing Then Throw New ArgumentNullException(NameOf(bytes))
        Return New ExchangeDriveSendSource(name, mimeType, Function() New MemoryStream(bytes, False))
    End Function

    ''' <summary>A file on disk, sent under its base name unless <paramref name="name"/> says otherwise.</summary>
    Public Shared Function FromFile(path As String, Optional name As String = Nothing, Optional mimeType As String = Nothing) As ExchangeDriveSendSource
        Dim sendName As String = If(String.IsNullOrEmpty(name), System.IO.Path.GetFileName(path), name)
        Return New ExchangeDriveSendSource(sendName, If(String.IsNullOrEmpty(mimeType), MimeTypeOf(path), mimeType),
                                           Function() New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, True))
    End Function

    ''' <summary>A media type from the extension for the formats the viewer shows; anything else is octet-stream.</summary>
    Public Shared Function MimeTypeOf(path As String) As String
        Select Case System.IO.Path.GetExtension(If(path, "")).ToLowerInvariant()
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".png" : Return "image/png"
            Case ".gif" : Return "image/gif"
            Case ".webp" : Return "image/webp"
            Case ".bmp" : Return "image/bmp"
            Case ".tif", ".tiff" : Return "image/tiff"
            Case ".heic" : Return "image/heic"
            Case ".avif" : Return "image/avif"
            Case ".jxl" : Return "image/jxl"
            Case ".mp4", ".m4v" : Return "video/mp4"
            Case ".mkv" : Return "video/x-matroska"
            Case ".webm" : Return "video/webm"
            Case ".mov" : Return "video/quicktime"
            Case ".avi" : Return "video/x-msvideo"
            Case ".mp3" : Return "audio/mpeg"
            Case ".txt" : Return "text/plain"
            Case ".json" : Return "application/json"
            Case ".pdf" : Return "application/pdf"
            Case ".zip" : Return "application/zip"
            Case Else : Return ExchangeDrivePackets.DefaultMimeType
        End Select
    End Function
End Class

''' <summary>Which packets this device has already taken - how an untargeted packet that stays in the queue is not saved twice.</summary>
Friend Interface IExchangeDriveHandledStore

    Function Contains(packetId As String) As Boolean
    Sub Add(packetId As String)
End Interface

''' <summary>The handled set in memory (a test, or a device that keeps nothing between runs).</summary>
Friend NotInheritable Class ExchangeDriveMemoryHandledStore
    Implements IExchangeDriveHandledStore

    Private ReadOnly _ids As New HashSet(Of String)(StringComparer.Ordinal)

    Public Function Contains(packetId As String) As Boolean Implements IExchangeDriveHandledStore.Contains
        SyncLock _ids
            Return _ids.Contains(packetId)
        End SyncLock
    End Function

    Public Sub Add(packetId As String) Implements IExchangeDriveHandledStore.Add
        SyncLock _ids
            _ids.Add(packetId)
        End SyncLock
    End Sub
End Class

''' <summary>The handled set in a small text file beside the Drive token, one packet id per line, the newest 2000 kept.
''' Nothing is created until the first packet is taken (the dormant rule of 204 section 2).</summary>
Friend NotInheritable Class ExchangeDriveFileHandledStore
    Implements IExchangeDriveHandledStore

    Private Const MaxEntries As Integer = 2000

    Private ReadOnly _path As String
    Private ReadOnly _lock As New Object()
    Private _loaded As Boolean
    Private ReadOnly _order As New List(Of String)
    Private ReadOnly _set As New HashSet(Of String)(StringComparer.Ordinal)

    Public Sub New(path As String)
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("path", NameOf(path))
        _path = path
    End Sub

    Public Shared Function CreateDefault() As ExchangeDriveFileHandledStore
        Return New ExchangeDriveFileHandledStore(System.IO.Path.Combine(ExchangeCredentialStore.DefaultDirectory(), "drive-handled.txt"))
    End Function

    Private Sub EnsureLoaded()
        If _loaded Then Return
        _loaded = True
        Try
            If File.Exists(_path) Then
                For Each line As String In File.ReadAllLines(_path, Encoding.UTF8)
                    If ExchangeDrivePackets.IsValidPacketId(line) AndAlso _set.Add(line) Then _order.Add(line)
                Next
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
        End Try
    End Sub

    Public Function Contains(packetId As String) As Boolean Implements IExchangeDriveHandledStore.Contains
        SyncLock _lock
            EnsureLoaded()
            Return _set.Contains(packetId)
        End SyncLock
    End Function

    Public Sub Add(packetId As String) Implements IExchangeDriveHandledStore.Add
        If Not ExchangeDrivePackets.IsValidPacketId(packetId) Then Return
        SyncLock _lock
            EnsureLoaded()
            If Not _set.Add(packetId) Then Return
            _order.Add(packetId)
            Try
                Dim dir As String = System.IO.Path.GetDirectoryName(_path)
                If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
                If _order.Count > MaxEntries Then
                    Dim dropped As Integer = _order.Count - MaxEntries
                    For index As Integer = 0 To dropped - 1
                        _set.Remove(_order(index))
                    Next
                    _order.RemoveRange(0, dropped)
                    File.WriteAllLines(_path, _order, Encoding.UTF8)
                Else
                    File.AppendAllText(_path, packetId & vbLf, Encoding.UTF8)
                End If
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                ' the set stays in memory for this run; a lost file costs at worst one repeated offer
            End Try
        End SyncLock
    End Sub
End Class

''' <summary>A packet as one poll saw it: its folder, its decoded manifest and the payload files beside the manifest.</summary>
Friend NotInheritable Class ExchangeDrivePacket

    Public ReadOnly FolderId As String
    Public ReadOnly FolderCreatedTime As String
    Public ReadOnly ManifestFileId As String
    ''' <summary>Drive's `createdTime` of the manifest - the clock item CP orders clipboard values by.</summary>
    Public ReadOnly ManifestCreatedTime As String
    Public ReadOnly ManifestCreatedAt As DateTimeOffset
    Public ReadOnly HasManifestCreatedAt As Boolean
    Public ReadOnly Manifest As ExchangeDrivePacketManifest
    Public ReadOnly PayloadFiles As IReadOnlyList(Of ExchangeDriveFile)
    Public ReadOnly Address As ExchangeDriveAddress
    Public ReadOnly IsOwn As Boolean

    Public Sub New(folder As ExchangeDriveFile, manifestFile As ExchangeDriveFile, manifest As ExchangeDrivePacketManifest, payloadFiles As IReadOnlyList(Of ExchangeDriveFile),
                   address As ExchangeDriveAddress, isOwn As Boolean)
        FolderId = folder.Id
        FolderCreatedTime = folder.CreatedTime
        ManifestFileId = manifestFile.Id
        ManifestCreatedTime = manifestFile.CreatedTime
        Dim parsed As DateTimeOffset
        HasManifestCreatedAt = ExchangeDriveRecords.TryParseDriveTime(manifestFile.CreatedTime, parsed)
        ManifestCreatedAt = parsed
        Me.Manifest = manifest
        Me.PayloadFiles = payloadFiles
        Me.Address = address
        Me.IsOwn = isOwn
    End Sub

    Public ReadOnly Property PacketId As String
        Get
            Return Manifest.PacketId
        End Get
    End Property

    ''' <summary>What the packet's files take of the person's Drive (the queue screen shows it, item CS).</summary>
    Public ReadOnly Property DriveBytes As Long
        Get
            Dim total As Long = 0
            For Each file As ExchangeDriveFile In PayloadFiles
                total += Math.Max(0L, file.Size)
            Next
            Return total
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return "ExchangeDrivePacket"
    End Function
End Class

''' <summary>What one poll of the queue found.</summary>
Friend NotInheritable Class ExchangeDriveQueuePoll

    ''' <summary>Every committed, unexpired packet whose manifest could be read, oldest first - what the queue screen lists.</summary>
    Public ReadOnly Packets As IReadOnlyList(Of ExchangeDrivePacket)
    ''' <summary>The account's clipboard value (item CP), or Nothing.</summary>
    Public ReadOnly ClipboardValue As ExchangeDrivePacket
    ''' <summary>How many expired packets, orphan folders and superseded clipboard packets this poll deleted.</summary>
    Public ReadOnly Cleaned As Integer
    ''' <summary>Packets skipped for a manifest this program refuses (a broken file, a name outside the forms of item CN).</summary>
    Public ReadOnly Skipped As Integer
    ''' <summary>Packet folders with no manifest yet - an upload in progress somewhere.</summary>
    Public ReadOnly InProgress As Integer

    Public Sub New(packets As IReadOnlyList(Of ExchangeDrivePacket), clipboardValue As ExchangeDrivePacket, cleaned As Integer, skipped As Integer, inProgress As Integer)
        Me.Packets = packets
        Me.ClipboardValue = clipboardValue
        Me.Cleaned = cleaned
        Me.Skipped = skipped
        Me.InProgress = inProgress
    End Sub
End Class

Friend Enum ExchangeDriveSendOutcome
    ''' <summary>The manifest landed: the packet is in the queue (not "delivered" - that is the target deleting it, item CT).</summary>
    Sent
    ''' <summary>A file name outside item CN's forms, a repeated name, too many files, or a request that is no JSON.</summary>
    BadPacket
    ''' <summary>More than `packetBytes` (item CS).</summary>
    TooLarge
    ''' <summary>Would not fit on the person's Drive with 1% to spare (item CS); <see cref="ExchangeDriveSendResult.FreeBytes"/> says how much room there is.</summary>
    DriveFull
End Enum

Friend NotInheritable Class ExchangeDriveSendResult

    Public ReadOnly Outcome As ExchangeDriveSendOutcome
    Public ReadOnly PacketId As String
    Public ReadOnly FreeBytes As Long

    Public Sub New(outcome As ExchangeDriveSendOutcome, packetId As String, freeBytes As Long)
        Me.Outcome = outcome
        Me.PacketId = If(packetId, "")
        Me.FreeBytes = freeBytes
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = ExchangeDriveSendOutcome.Sent
        End Get
    End Property
End Class

Friend Enum ExchangeDriveReceiveOutcome
    ''' <summary>Every file arrived whole and was saved.</summary>
    Received
    ''' <summary>A packet this device wrote, or one addressed to another device.</summary>
    NotForThisDevice
    ''' <summary>A kind this call does not apply (SETTINGS, REQUEST, REPLY, an unknown kind).</summary>
    NotReceivable
    ''' <summary>The manifest names a file the folder does not hold.</summary>
    MissingFile
    ''' <summary>A downloaded file is not the size or the SHA-256 the manifest said: dropped, nothing placed.</summary>
    Corrupt
    ''' <summary>The packet is not a clipboard value this call can read (more than a text bound, or no payload).</summary>
    TooLarge
End Enum

Friend NotInheritable Class ExchangeDriveReceiveResult

    Public ReadOnly Outcome As ExchangeDriveReceiveOutcome
    ''' <summary>The files saved, in the order of the manifest (only the ones that arrived whole).</summary>
    Public ReadOnly SavedPaths As IReadOnlyList(Of String)
    ''' <summary>The packet's name of the file that failed, for the message; "" otherwise.</summary>
    Public ReadOnly FailedName As String
    ''' <summary>True when the packet folder was deleted from Drive (a targeted packet taken in full).</summary>
    Public ReadOnly PacketDeleted As Boolean
    ''' <summary>A text clipboard value (<see cref="ExchangeDriveQueue.ReceiveClipboardAsync"/>); "" otherwise.</summary>
    Public ReadOnly Text As String

    Public Sub New(outcome As ExchangeDriveReceiveOutcome, savedPaths As IReadOnlyList(Of String), failedName As String, packetDeleted As Boolean, text As String)
        Me.Outcome = outcome
        Me.SavedPaths = savedPaths
        Me.FailedName = If(failedName, "")
        Me.PacketDeleted = packetDeleted
        Me.Text = If(text, "")
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = ExchangeDriveReceiveOutcome.Received
        End Get
    End Property
End Class

''' <summary>Sending, polling, receiving and deleting packets.</summary>
Friend NotInheritable Class ExchangeDriveQueue

    ''' <summary>The largest clipboard text read into memory (item BZ's bound is for files; a text is never that big in practice).</summary>
    Public Const MaxClipboardTextBytes As Integer = 64 * 1024 * 1024

    Private Const ManifestMimeType As String = "application/json"

    Private NotInheritable Class CachedManifest
        Public ModifiedTime As String = ""
        Public Manifest As ExchangeDrivePacketManifest
    End Class

    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _ownDeviceId As String
    Private ReadOnly _ownDeviceName As String
    Private ReadOnly _handled As IExchangeDriveHandledStore
    Private ReadOnly _utcNow As Func(Of DateTime)
    Private ReadOnly _packetBytes As Long
    Private ReadOnly _delay As Func(Of TimeSpan, CancellationToken, Task)
    Private ReadOnly _monotonicMs As Func(Of Long)
    Private ReadOnly _manifestCache As New Dictionary(Of String, CachedManifest)(StringComparer.Ordinal)

    Public Sub New(client As ExchangeDriveClient, ownDeviceId As String, ownDeviceName As String, handled As IExchangeDriveHandledStore,
                   Optional utcNow As Func(Of DateTime) = Nothing, Optional packetBytes As Long = ExchangeDrivePackets.DefaultPacketBytes,
                   Optional delay As Func(Of TimeSpan, CancellationToken, Task) = Nothing, Optional monotonicMs As Func(Of Long) = Nothing)
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))
        If Not ExchangeIds.IsValidId(ownDeviceId) Then Throw New ArgumentException("the device id is not in the id form", NameOf(ownDeviceId))
        _client = client
        _ownDeviceId = ownDeviceId
        _ownDeviceName = If(ownDeviceName, "")
        _handled = If(handled, New ExchangeDriveMemoryHandledStore())
        _utcNow = If(utcNow, Function() DateTime.UtcNow)
        _packetBytes = If(packetBytes > 0, packetBytes, ExchangeDrivePackets.DefaultPacketBytes)
        _delay = If(delay, Function(span As TimeSpan, token As CancellationToken) Task.Delay(span, token))
        _monotonicMs = If(monotonicMs, Function() CLng(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency))
    End Sub

    ' ------------------------------------------------------------------------------------------ sending

    ''' <summary>Sends files to one device (<paramref name="targetDeviceId"/>) or, with no target, offers them to every own device.</summary>
    Public Function SendFilesAsync(sources As IReadOnlyList(Of ExchangeDriveSendSource), targetDeviceId As String, targetDeviceName As String,
                                   cancellation As CancellationToken, Optional progress As Action(Of Long, Long) = Nothing, Optional ttlDays As Integer = 0) As Task(Of ExchangeDriveSendResult)
        Dim template As New ExchangeDrivePacketManifest() With {
            .PayloadKind = ExchangeDrivePayloadKind.Files,
            .TargetDeviceId = If(targetDeviceId, ""),
            .TargetDeviceName = If(targetDeviceName, ""),
            .TtlDays = ttlDays}
        Return SendCoreAsync(template, sources, cancellation, progress)
    End Function

    ''' <summary>Writes this device's clipboard value (item CP): untargeted, no expiry, one payload file; a text is
    ''' `clipboard.txt`. After the manifest landed the older clipboard packets this device can see are deleted.</summary>
    Public Async Function SendClipboardAsync(source As ExchangeDriveSendSource, clipKind As String, cancellation As CancellationToken) As Task(Of ExchangeDriveSendResult)
        Dim template As New ExchangeDrivePacketManifest() With {.PayloadKind = ExchangeDrivePayloadKind.Clipboard, .ClipKind = If(clipKind, "")}
        Dim result As ExchangeDriveSendResult = Await SendCoreAsync(template, New ExchangeDriveSendSource() {source}, cancellation, Nothing).ConfigureAwait(False)
        If result.Succeeded Then
            Try
                Await SupersedeClipboardAsync(result.PacketId, cancellation).ConfigureAwait(False)
            Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
                ' the older values are cleaned by the next poll of any device (item CP)
            End Try
        End If
        Return result
    End Function

    ''' <summary>A text clipboard value: `clipboard.txt`, UTF-8 with LF line ends (item CP).</summary>
    Public Function SendClipboardTextAsync(text As String, cancellation As CancellationToken) As Task(Of ExchangeDriveSendResult)
        Dim normalized As String = If(text, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        Dim source As ExchangeDriveSendSource = ExchangeDriveSendSource.FromBytes(ExchangeDrivePackets.ClipboardTextFileName, "text/plain", New UTF8Encoding(False).GetBytes(normalized))
        Return SendClipboardAsync(source, "text", cancellation)
    End Function

    ''' <summary>A REQUEST to one device (item CO). The caller then waits with <see cref="AwaitRepliesAsync"/>.</summary>
    Public Function SendRequestAsync(requestJson As String, targetDeviceId As String, targetDeviceName As String, cancellation As CancellationToken, Optional ttlSeconds As Integer = 0) As Task(Of ExchangeDriveSendResult)
        If Not ExchangeIds.IsValidId(targetDeviceId) Then Throw New ArgumentException("a request names its device by id", NameOf(targetDeviceId))
        Dim template As New ExchangeDrivePacketManifest() With {
            .PayloadKind = ExchangeDrivePayloadKind.Request,
            .RequestJson = If(requestJson, ""),
            .TargetDeviceId = targetDeviceId,
            .TargetDeviceName = If(targetDeviceName, ""),
            .TtlSeconds = If(ttlSeconds > 0, ttlSeconds, ExchangeDrivePackets.DefaultRequestTtlSeconds)}
        Return SendCoreAsync(template, Nothing, cancellation, Nothing)
    End Function

    ''' <summary>A REPLY to a request, targeted at its sender (item CO). <paramref name="sources"/> are the files it carries
    ''' (`listing.json`, the fetched files); <paramref name="final"/> marks the last of several.</summary>
    Public Function SendReplyAsync(request As ExchangeDrivePacket, outcome As String, reason As String, asOf As String, final As Boolean,
                                   sources As IReadOnlyList(Of ExchangeDriveSendSource), cancellation As CancellationToken, Optional progress As Action(Of Long, Long) = Nothing) As Task(Of ExchangeDriveSendResult)
        If request Is Nothing Then Throw New ArgumentNullException(NameOf(request))
        If Not ExchangeIds.IsValidId(request.Manifest.SenderDeviceId) Then Throw New ArgumentException("the request does not name its sender by id", NameOf(request))
        Dim template As New ExchangeDrivePacketManifest() With {
            .PayloadKind = ExchangeDrivePayloadKind.Reply,
            .InReplyTo = request.PacketId,
            .Outcome = If(outcome, ""),
            .Reason = If(reason, ""),
            .AsOf = If(asOf, ""),
            .Final = final,
            .TargetDeviceId = request.Manifest.SenderDeviceId,
            .TargetDeviceName = request.Manifest.SenderDeviceName,
            .TtlSeconds = ExchangeDrivePackets.DefaultReplyTtlSeconds}
        Return SendCoreAsync(template, sources, cancellation, progress)
    End Function

    Private Async Function SendCoreAsync(template As ExchangeDrivePacketManifest, sources As IReadOnlyList(Of ExchangeDriveSendSource),
                                         cancellation As CancellationToken, progress As Action(Of Long, Long)) As Task(Of ExchangeDriveSendResult)
        If sources Is Nothing Then sources = Array.Empty(Of ExchangeDriveSendSource)()
        If sources.Count > ExchangeDrivePackets.MaxFilesPerPacket Then Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.BadPacket, "", 0)

        ' 1. measure and hash every file - the manifest carries them, and the receiver checks them (item CN)
        Dim names As New List(Of String)
        Dim items As New List(Of ExchangeDrivePacketItem)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim total As Long = 0
        For Each source As ExchangeDriveSendSource In sources
            If Not ExchangeDrivePackets.IsValidPayloadName(source.Name) OrElse Not seen.Add(source.Name) Then
                Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.BadPacket, "", 0)
            End If
            Using stream As Stream = source.OpenRead()
                Dim size As Long = stream.Length
                Dim hash As String = Await HashAsync(stream, cancellation).ConfigureAwait(False)
                names.Add(source.Name)
                items.Add(New ExchangeDrivePacketItem(source.Name, size, hash, source.MimeType))
                total += size
            End Using
        Next
        If total > _packetBytes Then Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.TooLarge, "", 0)

        ' 2. the manifest, complete, BEFORE anything is uploaded: a request that is no JSON is refused here, not after the files
        Dim packetId As String = ExchangeDrivePackets.NewPacketId()
        template.PacketId = packetId
        template.SenderDeviceId = _ownDeviceId
        template.SenderDeviceName = _ownDeviceName
        template.CreatedAtEpochMs = New DateTimeOffset(DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc)).ToUnixTimeMilliseconds()
        template.FileNames = names
        template.Files = items
        template.TotalSizeBytes = total
        Dim manifestBody As Byte() = ExchangeDrivePackets.Encode(template)
        If manifestBody Is Nothing Then Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.BadPacket, "", 0)

        ' 3. the room on the person's Drive, before any byte goes up (item CS)
        If total > 0 Then
            Dim quota As ExchangeDriveQuota = Await _client.GetStorageQuotaAsync(cancellation).ConfigureAwait(False)
            Dim free As Long
            If Not ExchangeDrivePackets.FitsQuota(quota.LimitBytes, quota.UsageBytes, total, free) Then
                Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.DriveFull, "", free)
            End If
        End If

        ' 4. the packet folder, then the payload files, then the manifest last (the commit marker)
        Dim queueFolderId As String = Await EnsureQueueFolderAsync(cancellation).ConfigureAwait(False)
        Dim folder As ExchangeDriveFile = Await _client.CreateFolderAsync(queueFolderId, packetId, cancellation).ConfigureAwait(False)

        Dim failure As Exception = Nothing
        Try
            Dim sent As Long = 0
            For index As Integer = 0 To sources.Count - 1
                Dim item As ExchangeDrivePacketItem = items(index)
                If item.Size <= ExchangeDrivePackets.ResumableThresholdBytes Then
                    Dim bytes As Byte() = Await ReadAllAsync(sources(index), item.Size, cancellation).ConfigureAwait(False)
                    Await _client.CreateSmallFileAsync(folder.Id, item.Name, item.MimeType, bytes, Nothing, cancellation).ConfigureAwait(False)
                    sent += item.Size
                    If progress IsNot Nothing Then progress(sent, total)
                Else
                    Dim before As Long = sent
                    Using stream As Stream = sources(index).OpenRead()
                        Await _client.UploadResumableAsync(folder.Id, item.Name, item.MimeType, stream, cancellation,
                                                           Sub(done As Long)
                                                               If progress IsNot Nothing Then progress(before + done, total)
                                                           End Sub).ConfigureAwait(False)
                    End Using
                    sent += item.Size
                End If
            Next
            Await _client.CreateSmallFileAsync(folder.Id, ExchangeDrivePackets.ManifestFileName, ManifestMimeType, manifestBody, Nothing, cancellation).ConfigureAwait(False)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException
            failure = ex
        End Try

        If failure IsNot Nothing Then
            ' a sender whose upload failed deletes its own folder at once (item CR)
            Try
                Await _client.DeleteAsync(folder.Id, CancellationToken.None).ConfigureAwait(False)
            Catch ex As ExchangeDriveException
            End Try
            ExceptionDispatchInfo.Capture(failure).Throw()
        End If
        Return New ExchangeDriveSendResult(ExchangeDriveSendOutcome.Sent, packetId, 0)
    End Function

    ''' <summary>The id of the queue folder at the root, created when it is not there. When two devices created one at the same
    ''' moment the oldest is used; the poll reads every folder of that name.</summary>
    Private Async Function EnsureQueueFolderAsync(cancellation As CancellationToken) As Task(Of String)
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
        Dim best As ExchangeDriveFile = Nothing
        For Each id As String In QueueFolderIds(listing)
            For Each file As ExchangeDriveFile In listing.Files
                If String.Equals(file.Id, id, StringComparison.Ordinal) AndAlso (best Is Nothing OrElse String.CompareOrdinal(file.CreatedTime, best.CreatedTime) < 0) Then best = file
            Next
        Next
        If best IsNot Nothing Then Return best.Id
        Dim created As ExchangeDriveFile = Await _client.CreateFolderAsync(ExchangeDriveClient.AppDataFolder, ExchangeDrivePackets.QueueFolderName, cancellation).ConfigureAwait(False)
        Return created.Id
    End Function

    ''' <summary>The queue folders at the root of the listing (usually one).</summary>
    Friend Shared Function QueueFolderIds(listing As ExchangeDriveListing) As HashSet(Of String)
        Dim ids As New HashSet(Of String)(StringComparer.Ordinal)
        Dim folders As HashSet(Of String) = ExchangeDriveDirectory.FolderIdsOf(listing)
        For Each file As ExchangeDriveFile In listing.Files
            If file.IsFolder AndAlso String.Equals(file.Name, ExchangeDrivePackets.QueueFolderName, StringComparison.Ordinal) AndAlso ExchangeDriveDirectory.IsRootFile(file, folders) Then ids.Add(file.Id)
        Next
        Return ids
    End Function

    Private Shared Async Function HashAsync(stream As Stream, cancellation As CancellationToken) As Task(Of String)
        Using sha As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Dim buffer(81919) As Byte
            Do
                Dim got As Integer = Await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(False)
                If got = 0 Then Exit Do
                sha.AppendData(buffer, 0, got)
            Loop
            Return ToHex(sha.GetHashAndReset())
        End Using
    End Function

    Private Shared Function ToHex(bytes As Byte()) As String
        Dim builder As New StringBuilder(bytes.Length * 2)
        For Each b As Byte In bytes
            builder.Append(b.ToString("x2", Globalization.CultureInfo.InvariantCulture))
        Next
        Return builder.ToString()
    End Function

    Private Shared Async Function ReadAllAsync(source As ExchangeDriveSendSource, expected As Long, cancellation As CancellationToken) As Task(Of Byte())
        Using stream As Stream = source.OpenRead()
            Using memory As New MemoryStream(CInt(Math.Min(expected, Integer.MaxValue)))
                Await stream.CopyToAsync(memory, 81920, cancellation).ConfigureAwait(False)
                Return memory.ToArray()
            End Using
        End Using
    End Function

    ' ------------------------------------------------------------------------------------------ polling

    ''' <summary>Reads the queue out of a listing the caller already holds (one listing serves the directory and the queue):
    ''' decodes the manifests that changed, drops the expired packets and the orphan folders from Drive, and settles the
    ''' clipboard value. Skips what it cannot read; never deletes on a guess (a listing without Drive's `Date` deletes nothing).</summary>
    Public Async Function PollAsync(listing As ExchangeDriveListing, cancellation As CancellationToken) As Task(Of ExchangeDriveQueuePoll)
        Dim queueIds As HashSet(Of String) = QueueFolderIds(listing)
        Dim childrenOf As New Dictionary(Of String, List(Of ExchangeDriveFile))(StringComparer.Ordinal)
        For Each file As ExchangeDriveFile In listing.Files
            For Each parent As String In file.Parents
                Dim bucket As List(Of ExchangeDriveFile) = Nothing
                If Not childrenOf.TryGetValue(parent, bucket) Then
                    bucket = New List(Of ExchangeDriveFile)
                    childrenOf(parent) = bucket
                End If
                bucket.Add(file)
            Next
        Next

        Dim packets As New List(Of ExchangeDrivePacket)
        Dim seenManifests As New HashSet(Of String)(StringComparer.Ordinal)
        Dim cleaned As Integer = 0
        Dim skipped As Integer = 0
        Dim inProgress As Integer = 0

        For Each folder As ExchangeDriveFile In listing.Files
            If Not folder.IsFolder Then Continue For
            Dim inQueue As Boolean = False
            For Each parent As String In folder.Parents
                If queueIds.Contains(parent) Then inQueue = True
            Next
            If Not inQueue Then Continue For

            Dim children As List(Of ExchangeDriveFile) = Nothing
            If Not childrenOf.TryGetValue(folder.Id, children) Then children = New List(Of ExchangeDriveFile)
            Dim manifestFile As ExchangeDriveFile = Nothing
            For Each child As ExchangeDriveFile In children
                If Not child.IsFolder AndAlso String.Equals(child.Name, ExchangeDrivePackets.ManifestFileName, StringComparison.Ordinal) Then manifestFile = child
            Next

            If manifestFile Is Nothing Then
                If listing.HasServerDate AndAlso ExchangeDrivePackets.IsOrphanExpired(folder.CreatedTime, listing.ServerDate) Then
                    If Await TryDeleteAsync(folder.Id, cancellation).ConfigureAwait(False) Then cleaned += 1
                Else
                    inProgress += 1
                End If
                Continue For
            End If
            seenManifests.Add(manifestFile.Id)

            Dim manifest As ExchangeDrivePacketManifest = Await DecodeManifestCachedAsync(manifestFile, cancellation).ConfigureAwait(False)
            If manifest Is Nothing Then
                skipped += 1
                Continue For
            End If

            If listing.HasServerDate AndAlso ExchangeDrivePackets.Judge(folder.CreatedTime, listing.ServerDate, manifest) = ExchangeDriveRecordState.Expired Then
                If Await TryDeleteAsync(folder.Id, cancellation).ConfigureAwait(False) Then cleaned += 1
                Continue For
            End If

            Dim payload As New List(Of ExchangeDriveFile)
            For Each child As ExchangeDriveFile In children
                If Not child.IsFolder AndAlso Not String.Equals(child.Name, ExchangeDrivePackets.ManifestFileName, StringComparison.Ordinal) Then payload.Add(child)
            Next
            packets.Add(New ExchangeDrivePacket(folder, manifestFile, manifest, payload,
                                                ExchangeDrivePackets.AddresseeOf(manifest, _ownDeviceId, _ownDeviceName),
                                                ExchangeDrivePackets.IsOwnPacket(manifest, _ownDeviceId, _ownDeviceName)))
        Next

        SyncLock _manifestCache
            For Each key As String In _manifestCache.Keys.Where(Function(k) Not seenManifests.Contains(k)).ToList()
                _manifestCache.Remove(key)
            Next
        End SyncLock

        ' the clipboard (item CP): the latest manifest by Drive's createdTime is the value; one 10 minutes older goes
        Dim value As ExchangeDrivePacket = Nothing
        For Each packet As ExchangeDrivePacket In packets
            If packet.Manifest.PayloadKind <> ExchangeDrivePayloadKind.Clipboard Then Continue For
            If value Is Nothing OrElse IsLaterThan(packet, value) Then value = packet
        Next
        If value IsNot Nothing AndAlso value.HasManifestCreatedAt Then
            For Each packet As ExchangeDrivePacket In packets.ToList()
                If packet Is value OrElse packet.Manifest.PayloadKind <> ExchangeDrivePayloadKind.Clipboard OrElse Not packet.HasManifestCreatedAt Then Continue For
                If value.ManifestCreatedAt - packet.ManifestCreatedAt > TimeSpan.FromMinutes(ExchangeDrivePackets.ClipboardSupersedeMinutes) Then
                    If Await TryDeleteAsync(packet.FolderId, cancellation).ConfigureAwait(False) Then
                        cleaned += 1
                        packets.Remove(packet)
                    End If
                End If
            Next
        End If

        packets.Sort(Function(a As ExchangeDrivePacket, b As ExchangeDrivePacket) String.CompareOrdinal(a.ManifestCreatedTime, b.ManifestCreatedTime))
        Return New ExchangeDriveQueuePoll(packets, value, cleaned, skipped, inProgress)
    End Function

    Private Shared Function IsLaterThan(candidate As ExchangeDrivePacket, current As ExchangeDrivePacket) As Boolean
        If Not candidate.HasManifestCreatedAt Then Return False
        If Not current.HasManifestCreatedAt Then Return True
        Return candidate.ManifestCreatedAt > current.ManifestCreatedAt
    End Function

    Private Async Function DecodeManifestCachedAsync(manifestFile As ExchangeDriveFile, cancellation As CancellationToken) As Task(Of ExchangeDrivePacketManifest)
        Dim cached As CachedManifest = Nothing
        SyncLock _manifestCache
            If _manifestCache.TryGetValue(manifestFile.Id, cached) AndAlso String.Equals(cached.ModifiedTime, manifestFile.ModifiedTime, StringComparison.Ordinal) Then Return cached.Manifest
        End SyncLock

        Dim body As Byte()
        Try
            body = Await _client.DownloadBytesAsync(manifestFile.Id, ExchangeDrivePackets.MaxManifestBytes, cancellation).ConfigureAwait(False)
        Catch ex As ExchangeDriveException When ex.Failure = ExchangeDriveFailure.NotFound OrElse ex.Failure = ExchangeDriveFailure.Other
            Return Nothing
        End Try
        Dim manifest As ExchangeDrivePacketManifest = Nothing
        Dim refusal As ExchangeDriveManifestRefusal
        If Not ExchangeDrivePackets.TryDecode(body, manifest, refusal) Then manifest = Nothing
        SyncLock _manifestCache
            _manifestCache(manifestFile.Id) = New CachedManifest() With {.ModifiedTime = manifestFile.ModifiedTime, .Manifest = manifest}
        End SyncLock
        Return manifest
    End Function

    ''' <summary>A best-effort delete during a poll: a Drive hiccup leaves the file for the next poll, but a sign-out is not hidden.</summary>
    Private Async Function TryDeleteAsync(fileId As String, cancellation As CancellationToken) As Task(Of Boolean)
        Try
            Await _client.DeleteAsync(fileId, cancellation).ConfigureAwait(False)
            Return True
        Catch ex As ExchangeDriveException When ex.Failure <> ExchangeDriveFailure.SignedOut
            Return False
        End Try
    End Function

    ''' <summary>The FILES and MEDIA_FILES packets this device may take and has not taken: not its own, not addressed to another
    ''' device, of a kind this program applies, not in the handled set.</summary>
    Public Function IncomingFiles(poll As ExchangeDriveQueuePoll) As IReadOnlyList(Of ExchangeDrivePacket)
        Dim result As New List(Of ExchangeDrivePacket)
        For Each packet As ExchangeDrivePacket In poll.Packets
            Select Case packet.Manifest.PayloadKind
                Case ExchangeDrivePayloadKind.Files, ExchangeDrivePayloadKind.MediaFiles
                    If packet.IsOwn OrElse packet.Address = ExchangeDriveAddress.OtherDevice Then Continue For
                    If _handled.Contains(packet.PacketId) Then Continue For
                    result.Add(packet)
            End Select
        Next
        Return result
    End Function

    ''' <summary>The REQUESTs addressed to this device (item CO), oldest first. A request is always targeted by id.</summary>
    Public Function RequestsForThisDevice(poll As ExchangeDriveQueuePoll) As IReadOnlyList(Of ExchangeDrivePacket)
        Dim result As New List(Of ExchangeDrivePacket)
        For Each packet As ExchangeDrivePacket In poll.Packets
            If packet.Manifest.PayloadKind = ExchangeDrivePayloadKind.Request AndAlso Not packet.IsOwn AndAlso packet.Address = ExchangeDriveAddress.ThisDevice Then result.Add(packet)
        Next
        Return result
    End Function

    ''' <summary>The REPLYs addressed to this device for one request.</summary>
    Public Function RepliesTo(poll As ExchangeDriveQueuePoll, requestPacketId As String) As IReadOnlyList(Of ExchangeDrivePacket)
        Dim result As New List(Of ExchangeDrivePacket)
        For Each packet As ExchangeDrivePacket In poll.Packets
            If packet.Manifest.PayloadKind = ExchangeDrivePayloadKind.Reply AndAlso packet.Address = ExchangeDriveAddress.ThisDevice AndAlso
               String.Equals(packet.Manifest.InReplyTo, requestPacketId, StringComparison.Ordinal) Then result.Add(packet)
        Next
        Return result
    End Function

    ''' <summary>Waits for replies to one request that are not in <paramref name="alreadySeen"/>, polling at the active interval
    ''' (item CQ). Gives up after <paramref name="waitSeconds"/> (120 in the foreground), deletes its own REQUEST and returns an
    ''' empty list - the caller says the device has not answered via GD.</summary>
    Public Async Function AwaitRepliesAsync(requestPacketId As String, alreadySeen As ISet(Of String), cancellation As CancellationToken,
                                            Optional waitSeconds As Integer = ExchangeDrivePolling.ReplyWaitSeconds) As Task(Of IReadOnlyList(Of ExchangeDrivePacket))
        Dim startMs As Long = _monotonicMs()
        Dim request As ExchangeDrivePacket = Nothing
        Do
            Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
            Dim poll As ExchangeDriveQueuePoll = Await PollAsync(listing, cancellation).ConfigureAwait(False)
            Dim fresh As New List(Of ExchangeDrivePacket)
            For Each reply As ExchangeDrivePacket In RepliesTo(poll, requestPacketId)
                If alreadySeen Is Nothing OrElse Not alreadySeen.Contains(reply.PacketId) Then fresh.Add(reply)
            Next
            If fresh.Count > 0 Then Return fresh
            For Each packet As ExchangeDrivePacket In poll.Packets
                If packet.Manifest.PayloadKind = ExchangeDrivePayloadKind.Request AndAlso String.Equals(packet.PacketId, requestPacketId, StringComparison.Ordinal) Then request = packet
            Next
            If _monotonicMs() - startMs >= CLng(waitSeconds) * 1000L Then Exit Do
            Await _delay(TimeSpan.FromSeconds(ExchangeDrivePolling.IntervalSeconds(True)), cancellation).ConfigureAwait(False)
        Loop
        If request IsNot Nothing Then Await TryDeleteAsync(request.FolderId, CancellationToken.None).ConfigureAwait(False)
        Return Array.Empty(Of ExchangeDrivePacket)()
    End Function

    ' ------------------------------------------------------------------------------------------ receiving

    ''' <summary>Takes the files of a FILES or MEDIA_FILES packet addressed to this device into <paramref name="destinationDirectory"/>:
    ''' each is downloaded to a temporary file, checked against the manifest's size and SHA-256, and only then given its final
    ''' name - the base name, with a " (2)" suffix instead of overwriting (item CN). When every file arrived whole the packet is
    ''' marked handled and, if it was targeted, deleted from Drive (item CR). Nothing partial is ever placed.</summary>
    Public Async Function ReceiveFilesAsync(packet As ExchangeDrivePacket, destinationDirectory As String, cancellation As CancellationToken,
                                            Optional progress As Action(Of Long, Long) = Nothing) As Task(Of ExchangeDriveReceiveResult)
        If packet Is Nothing Then Throw New ArgumentNullException(NameOf(packet))
        Dim noFiles As IReadOnlyList(Of String) = Array.Empty(Of String)()
        If packet.IsOwn OrElse packet.Address = ExchangeDriveAddress.OtherDevice Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.NotForThisDevice, noFiles, "", False, "")
        Select Case packet.Manifest.PayloadKind
            Case ExchangeDrivePayloadKind.Files, ExchangeDrivePayloadKind.MediaFiles
            Case Else
                Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.NotReceivable, noFiles, "", False, "")
        End Select

        Dim items As IReadOnlyList(Of ExchangeDrivePacketItem) = ExchangeDrivePackets.ExpectedItems(packet.Manifest)
        Dim total As Long = 0
        For Each item As ExchangeDrivePacketItem In items
            Dim driveFile As ExchangeDriveFile = FindPayload(packet, item.Name)
            If driveFile Is Nothing Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.MissingFile, noFiles, item.Name, False, "")
            total += If(item.Size >= 0, item.Size, driveFile.Size)
        Next

        Directory.CreateDirectory(destinationDirectory)
        Dim saved As New List(Of String)
        Dim done As Long = 0
        For Each item As ExchangeDrivePacketItem In items
            Dim driveFile As ExchangeDriveFile = FindPayload(packet, item.Name)
            Dim before As Long = done
            Dim fetched As FetchResult = Await FetchAsync(driveFile, item, destinationDirectory, True,
                                                          Sub(copied As Long)
                                                              If progress IsNot Nothing Then progress(before + copied, total)
                                                          End Sub, cancellation).ConfigureAwait(False)
            If Not fetched.Ok Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Corrupt, saved, item.Name, False, "")
            saved.Add(fetched.Path)
            done += If(item.Size >= 0, item.Size, driveFile.Size)
        Next

        _handled.Add(packet.PacketId)
        Dim deleted As Boolean = False
        If packet.Address = ExchangeDriveAddress.ThisDevice Then deleted = Await TryDeleteAsync(packet.FolderId, cancellation).ConfigureAwait(False)
        Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Received, saved, "", deleted, "")
    End Function

    ''' <summary>Reads the account's clipboard value (item CP). A text comes back in <see cref="ExchangeDriveReceiveResult.Text"/>; an
    ''' image or a file is saved under <paramref name="destinationDirectory"/> like any received file. The value is NEVER deleted
    ''' by the receiver, and it is not recorded as handled (it is read again on the next "get").</summary>
    Public Async Function ReceiveClipboardAsync(packet As ExchangeDrivePacket, destinationDirectory As String, cancellation As CancellationToken) As Task(Of ExchangeDriveReceiveResult)
        If packet Is Nothing Then Throw New ArgumentNullException(NameOf(packet))
        Dim noFiles As IReadOnlyList(Of String) = Array.Empty(Of String)()
        If packet.Manifest.PayloadKind <> ExchangeDrivePayloadKind.Clipboard Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.NotReceivable, noFiles, "", False, "")
        Dim items As IReadOnlyList(Of ExchangeDrivePacketItem) = ExchangeDrivePackets.ExpectedItems(packet.Manifest)
        If items.Count <> 1 Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.NotReceivable, noFiles, "", False, "")
        Dim item As ExchangeDrivePacketItem = items(0)
        Dim driveFile As ExchangeDriveFile = FindPayload(packet, item.Name)
        If driveFile Is Nothing Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.MissingFile, noFiles, item.Name, False, "")

        Dim isText As Boolean = String.Equals(packet.Manifest.ClipKind, "text", StringComparison.Ordinal)
        If isText Then
            Dim expected As Long = If(item.Size >= 0, item.Size, driveFile.Size)
            If expected > MaxClipboardTextBytes Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.TooLarge, noFiles, item.Name, False, "")
            Directory.CreateDirectory(destinationDirectory)
            Dim fetched As FetchResult = Await FetchAsync(driveFile, item, destinationDirectory, False, Nothing, cancellation).ConfigureAwait(False)
            If Not fetched.Ok Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Corrupt, noFiles, item.Name, False, "")
            Dim text As String
            Try
                text = File.ReadAllText(fetched.Path, New UTF8Encoding(False))
            Finally
                Try
                    File.Delete(fetched.Path)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                End Try
            End Try
            Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Received, noFiles, "", False, text)
        End If

        Directory.CreateDirectory(destinationDirectory)
        Dim placed As FetchResult = Await FetchAsync(driveFile, item, destinationDirectory, True, Nothing, cancellation).ConfigureAwait(False)
        If Not placed.Ok Then Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Corrupt, noFiles, item.Name, False, "")
        Return New ExchangeDriveReceiveResult(ExchangeDriveReceiveOutcome.Received, New String() {placed.Path}, "", False, "")
    End Function

    ''' <summary>One small payload file of a packet, whole and checked (a reply's `listing.json`, say), or Nothing when it is
    ''' missing, over <paramref name="maxBytes"/> or not what the manifest said.</summary>
    Public Async Function ReadPayloadAsync(packet As ExchangeDrivePacket, name As String, maxBytes As Integer, cancellation As CancellationToken) As Task(Of Byte())
        If packet Is Nothing Then Throw New ArgumentNullException(NameOf(packet))
        Dim driveFile As ExchangeDriveFile = FindPayload(packet, name)
        If driveFile Is Nothing Then Return Nothing
        Dim body As Byte()
        Try
            body = Await _client.DownloadBytesAsync(driveFile.Id, maxBytes, cancellation).ConfigureAwait(False)
        Catch ex As ExchangeDriveException When ex.Failure = ExchangeDriveFailure.Other OrElse ex.Failure = ExchangeDriveFailure.NotFound
            Return Nothing
        End Try
        For Each item As ExchangeDrivePacketItem In ExchangeDrivePackets.ExpectedItems(packet.Manifest)
            If Not String.Equals(item.Name, name, StringComparison.Ordinal) Then Continue For
            If item.Size >= 0 AndAlso item.Size <> body.Length Then Return Nothing
            If item.Sha256.Length > 0 Then
                Using sha As SHA256 = SHA256.Create()
                    If Not String.Equals(ToHex(sha.ComputeHash(body)), item.Sha256, StringComparison.OrdinalIgnoreCase) Then Return Nothing
                End Using
            End If
        Next
        Return body
    End Function

    Private Shared Function FindPayload(packet As ExchangeDrivePacket, name As String) As ExchangeDriveFile
        For Each file As ExchangeDriveFile In packet.PayloadFiles
            If String.Equals(file.Name, name, StringComparison.Ordinal) Then Return file
        Next
        Return Nothing
    End Function

    Private NotInheritable Class FetchResult
        Public Ok As Boolean
        Public Path As String = ""
    End Class

    ''' <summary>Downloads one file to a temporary name in <paramref name="targetFolder"/>, checks size and SHA-256, and either places it
    ''' under a free final name (<paramref name="place"/>) or leaves the temporary file for the caller to read and delete.</summary>
    Private Async Function FetchAsync(driveFile As ExchangeDriveFile, item As ExchangeDrivePacketItem, targetFolder As String, place As Boolean,
                                      progress As Action(Of Long), cancellation As CancellationToken) As Task(Of FetchResult)
        Dim temp As String = System.IO.Path.Combine(targetFolder, ".fmsgd-" & Guid.NewGuid().ToString("N") & ".part")
        Dim keep As Boolean = False
        Try
            Dim hashHex As String
            Dim length As Long
            Using output As New FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, True)
                Using hashing As New HashingWriteStream(output)
                    Await _client.DownloadAsync(driveFile.Id, hashing, cancellation, progress).ConfigureAwait(False)
                    hashHex = hashing.FinishHex()
                    length = hashing.Length
                End Using
            End Using

            Dim expectedSize As Long = If(item.Size >= 0, item.Size, driveFile.Size)
            If length <> expectedSize Then Return New FetchResult()
            If item.Sha256.Length > 0 AndAlso Not String.Equals(hashHex, item.Sha256, StringComparison.OrdinalIgnoreCase) Then Return New FetchResult()

            If Not place Then
                keep = True
                Return New FetchResult() With {.Ok = True, .Path = temp}
            End If

            Dim baseName As String = ExchangeDrivePackets.SafeFileName(item.Name)
            For number As Integer = 1 To 10000
                Dim candidate As String = System.IO.Path.Combine(targetFolder, If(number = 1, baseName, ExchangeDrivePackets.WithSuffix(baseName, number)))
                If File.Exists(candidate) OrElse Directory.Exists(candidate) Then Continue For
                Try
                    File.Move(temp, candidate, False)
                    Return New FetchResult() With {.Ok = True, .Path = candidate}
                Catch ex As IOException When File.Exists(candidate)
                    ' taken between the check and the move: the next suffix
                End Try
            Next
            Return New FetchResult()
        Finally
            If Not keep Then
                Try
                    If File.Exists(temp) Then File.Delete(temp)
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                End Try
            End If
        End Try
    End Function

    ''' <summary>A write-through stream that hashes what passes and counts it.</summary>
    Private NotInheritable Class HashingWriteStream
        Inherits Stream

        Private ReadOnly _inner As Stream
        Private ReadOnly _hash As IncrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
        Private _count As Long

        Public Sub New(inner As Stream)
            _inner = inner
        End Sub

        Public Function FinishHex() As String
            Return ToHex(_hash.GetHashAndReset())
        End Function

        Public Overrides ReadOnly Property CanRead As Boolean = False
        Public Overrides ReadOnly Property CanSeek As Boolean = False
        Public Overrides ReadOnly Property CanWrite As Boolean = True

        Public Overrides ReadOnly Property Length As Long
            Get
                Return _count
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Return _count
            End Get
            Set(value As Long)
                Throw New NotSupportedException()
            End Set
        End Property

        Public Overrides Sub Write(buffer() As Byte, offset As Integer, count As Integer)
            _hash.AppendData(buffer, offset, count)
            _count += count
            _inner.Write(buffer, offset, count)
        End Sub

        Public Overrides Async Function WriteAsync(buffer() As Byte, offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
            _hash.AppendData(buffer, offset, count)
            _count += count
            Await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(False)
        End Function

        Public Overrides Sub Flush()
            _inner.Flush()
        End Sub

        Public Overrides Function Read(buffer() As Byte, offset As Integer, count As Integer) As Integer
            Throw New NotSupportedException()
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Throw New NotSupportedException()
        End Function

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _hash.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

    ' ------------------------------------------------------------------------------------------ deleting

    ''' <summary>Deletes a packet from Drive (one permanent delete of its folder). The person may delete any packet of the queue
    ''' (item CS); a producer deletes the REQUEST it has answered, a requester the REPLY it has read (item CR).</summary>
    Public Async Function DeletePacketAsync(packet As ExchangeDrivePacket, cancellation As CancellationToken) As Task
        If packet Is Nothing Then Throw New ArgumentNullException(NameOf(packet))
        Await _client.DeleteAsync(packet.FolderId, cancellation).ConfigureAwait(False)
    End Function

    ''' <summary>After this device's clipboard manifest landed: deletes the older CLIPBOARD packets it can see (item CP). Returns how many.</summary>
    Public Async Function SupersedeClipboardAsync(valuePacketId As String, cancellation As CancellationToken) As Task(Of Integer)
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
        Dim poll As ExchangeDriveQueuePoll = Await PollAsync(listing, cancellation).ConfigureAwait(False)
        Dim value As ExchangeDrivePacket = Nothing
        For Each packet As ExchangeDrivePacket In poll.Packets
            If String.Equals(packet.PacketId, valuePacketId, StringComparison.Ordinal) Then value = packet
        Next
        If value Is Nothing OrElse Not value.HasManifestCreatedAt Then Return 0
        Dim deleted As Integer = 0
        For Each packet As ExchangeDrivePacket In poll.Packets
            If packet Is value OrElse packet.Manifest.PayloadKind <> ExchangeDrivePayloadKind.Clipboard OrElse Not packet.HasManifestCreatedAt Then Continue For
            If packet.ManifestCreatedAt < value.ManifestCreatedAt Then
                If Await TryDeleteAsync(packet.FolderId, cancellation).ConfigureAwait(False) Then deleted += 1
            End If
        Next
        Return deleted
    End Function

End Class
#End If
