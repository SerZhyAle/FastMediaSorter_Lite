#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Diagnostics
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.Threading

' Specification 203 section 3.2 (acceptance criteria 2, 4 and 5) - the directory half of the Drive channel, over
' ExchangeDriveClient: a device PUBLISHES its own records (device, resource, broadcast) as `fmsx-<kind>-<id>.json` files in the
' appDataFolder, keeps the ones that stand alive by rewriting them every ttlSeconds / 3 (item C), WITHDRAWS them when they
' stop standing, and READS everybody's into the same ExchangeDirectorySnapshot the exchange server's directory produces - so
' the resource list, the attach flow and the cast list need no second model.
'
' Rules this file keeps (DEVICE-EXCHANGE 8.1 and item C, cited, not restated):
'   - A device writes and deletes only its own files: a record is refused unless its `deviceId` is this device's, and every file
'     written carries the hidden marker `fmsxOwner = <deviceId>` so "mine" is known from a listing without reading a body.
'   - Expiry is Drive's clock: the file's modifiedTime against the Date header of the listing that reported it. A record stays
'     "online" while it is rewritten whatever its `writtenAt` says.
'   - A reader gives an expired device or resource as offline (presence "offline") and drops an expired broadcast (ended).
'   - A file that is not a record - a probe file, the packet queue, another product's name - is never touched.
'   - A request record (8.3) is not built here: its member set freezes at rung 3.

''' <summary>What one read of the directory produced.</summary>
Friend NotInheritable Class ExchangeDriveDirectoryResult

    ''' <summary>The directory without this device's own records (a consumer hides what it serves itself, 9 rule 1).</summary>
    Public ReadOnly Snapshot As ExchangeDirectorySnapshot
    ''' <summary>The listing it was made from, so the packet queue of the same poll needs no second call (item CQ).</summary>
    Public ReadOnly Listing As ExchangeDriveListing
    ''' <summary>Record files that were refused (a version this program does not know, a broken body): skipped, not errors.</summary>
    Public ReadOnly SkippedFiles As Integer

    Public Sub New(snapshot As ExchangeDirectorySnapshot, listing As ExchangeDriveListing, skippedFiles As Integer)
        Me.Snapshot = snapshot
        Me.Listing = listing
        Me.SkippedFiles = skippedFiles
    End Sub
End Class

''' <summary>Publishes, keeps alive and reads the Drive directory.</summary>
Friend NotInheritable Class ExchangeDriveDirectory

    ''' <summary>The hidden file property that marks a file as written by a device (its value is the deviceId).</summary>
    Public Const OwnerProperty As String = "fmsxOwner"

    Private Const RecordMimeType As String = "application/json"
    Private Const MaxRecordFileBytes As Integer = ExchangeWire.MaxRecordBytes + 2048

    Private NotInheritable Class Standing
        Public Kind As ExchangeDriveRecordKind
        Public Id As String = ""
        Public RecordJson As String = ""
        Public TtlSeconds As Integer
        Public LastWriteMs As Long
    End Class

    Private NotInheritable Class CachedRecord
        Public ModifiedTime As String = ""
        Public File As ExchangeDriveRecordFile
    End Class

    Private ReadOnly _client As ExchangeDriveClient
    Private ReadOnly _ownDeviceId As String
    Private ReadOnly _utcNow As Func(Of DateTime)
    Private ReadOnly _monotonicMs As Func(Of Long)

    Private ReadOnly _standing As New Dictionary(Of String, Standing)(StringComparer.Ordinal)
    Private ReadOnly _fileIds As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly _cache As New Dictionary(Of String, CachedRecord)(StringComparer.Ordinal)
    Private ReadOnly _gate As New SemaphoreSlim(1, 1)
    Private _revision As Long

    ''' <param name="utcNow">The clock `writtenAt` is taken from (informational only).</param>
    ''' <param name="monotonicMs">The clock the rewrite schedule runs on; a test moves it by hand.</param>
    Public Sub New(client As ExchangeDriveClient, ownDeviceId As String, Optional utcNow As Func(Of DateTime) = Nothing, Optional monotonicMs As Func(Of Long) = Nothing)
        If client Is Nothing Then Throw New ArgumentNullException(NameOf(client))
        If Not ExchangeIds.IsValidId(ownDeviceId) Then Throw New ArgumentException("the device id is not in the id form", NameOf(ownDeviceId))
        _client = client
        _ownDeviceId = ownDeviceId
        _utcNow = If(utcNow, Function() DateTime.UtcNow)
        _monotonicMs = If(monotonicMs, Function() CLng(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency))
    End Sub

    ''' <summary>How many records this device keeps alive.</summary>
    Public ReadOnly Property StandingCount As Integer
        Get
            Return _standing.Count
        End Get
    End Property

    Private Shared Function KeyOf(kind As ExchangeDriveRecordKind, id As String) As String
        Return ExchangeDriveRecords.KindName(kind) & ":" & id
    End Function

    ' ------------------------------------------------------------------------------------------ publishing

    ''' <summary>Writes (or rewrites) one of this device's records and keeps it alive until withdrawn. Refuses a record that
    ''' is not this device's own, a kind not built here, or a body that breaks the forms of 8.1.</summary>
    Public Async Function PublishAsync(kind As ExchangeDriveRecordKind, id As String, recordJson As String, ttlSeconds As Integer, cancellation As CancellationToken) As Task
        If kind = ExchangeDriveRecordKind.Request Then Throw New ArgumentException("a request record is not built before its members freeze (8.3)", NameOf(kind))
        If Not ExchangeIds.IsValidId(id) Then Throw New ArgumentException("the record id is not in the id form", NameOf(id))
        If Not IsOwnRecord(kind, id, recordJson) Then Throw New ArgumentException("a device writes only its own records (8.1)", NameOf(recordJson))
        Dim ttl As Integer = If(ttlSeconds > 0, ttlSeconds, ExchangeDriveRecords.DefaultTtlSeconds(kind))

        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            Await WriteAsync(kind, id, recordJson, ttl, cancellation).ConfigureAwait(False)
            Dim entry As New Standing() With {.Kind = kind, .Id = id, .RecordJson = recordJson, .TtlSeconds = ttl, .LastWriteMs = _monotonicMs()}
            _standing(KeyOf(kind, id)) = entry
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>A record is this device's own when its `deviceId` is this device's (a device record: its `deviceId` is also
    ''' the file's id).</summary>
    Private Function IsOwnRecord(kind As ExchangeDriveRecordKind, id As String, recordJson As String) As Boolean
        If String.IsNullOrEmpty(recordJson) Then Return False
        Try
            Using document As JsonDocument = JsonDocument.Parse(recordJson)
                If document.RootElement.ValueKind <> JsonValueKind.Object Then Return False
                Dim deviceEl As JsonElement = Nothing
                If Not document.RootElement.TryGetProperty("deviceId", deviceEl) OrElse deviceEl.ValueKind <> JsonValueKind.String Then Return False
                If Not String.Equals(deviceEl.GetString(), _ownDeviceId, StringComparison.Ordinal) Then Return False
                If kind = ExchangeDriveRecordKind.Device Then Return String.Equals(id, _ownDeviceId, StringComparison.Ordinal)
                Return True
            End Using
        Catch ex As JsonException
            Return False
        End Try
    End Function

    Private Async Function WriteAsync(kind As ExchangeDriveRecordKind, id As String, recordJson As String, ttl As Integer, cancellation As CancellationToken) As Task
        Dim body As Byte() = ExchangeDriveRecords.Encode(kind, recordJson, ttl, _utcNow())
        If body Is Nothing Then Throw New ArgumentException("the record breaks the forms of 8.1", NameOf(recordJson))
        Dim name As String = ExchangeDriveRecords.FileName(kind, id)
        Dim fileId As String = Await FindFileIdAsync(name, cancellation).ConfigureAwait(False)

        If fileId.Length > 0 Then
            Try
                Await _client.UpdateSmallFileAsync(fileId, RecordMimeType, body, cancellation).ConfigureAwait(False)
                Return
            Catch ex As ExchangeDriveException When ex.Failure = ExchangeDriveFailure.NotFound
                ' someone deleted it (the person, from another device): write it afresh below
                _fileIds.Remove(name)
            End Try
        End If
        Dim marker As New Dictionary(Of String, String)(StringComparer.Ordinal) From {{OwnerProperty, _ownDeviceId}}
        Dim created As ExchangeDriveFile = Await _client.CreateSmallFileAsync(ExchangeDriveClient.AppDataFolder, name, RecordMimeType, body, marker, cancellation).ConfigureAwait(False)
        _fileIds(name) = created.Id
    End Function

    ''' <summary>The file id of a record name: the cache, else one listing (which also fills the cache for every record file).</summary>
    Private Async Function FindFileIdAsync(name As String, cancellation As CancellationToken) As Task(Of String)
        Dim known As String = Nothing
        If _fileIds.TryGetValue(name, known) Then Return known
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
        For Each file As ExchangeDriveFile In listing.Files
            If Not file.IsFolder AndAlso String.Equals(file.Name, name, StringComparison.Ordinal) Then
                _fileIds(name) = file.Id
                Return file.Id
            End If
        Next
        Return ""
    End Function

    ''' <summary>Rewrites every standing record that has gone a third of its ttl without a write (item C). Returns how many were
    ''' rewritten. A failure on one record does not stop the others; the first failure is thrown after the pass so the host can back off.</summary>
    Public Async Function RefreshStandingAsync(cancellation As CancellationToken) As Task(Of Integer)
        Dim due As New List(Of Standing)
        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            Dim nowMs As Long = _monotonicMs()
            For Each entry As Standing In _standing.Values
                If nowMs - entry.LastWriteMs >= CLng(ExchangeDriveRecords.RewriteIntervalSeconds(entry.TtlSeconds)) * 1000L Then due.Add(entry)
            Next

            Dim rewritten As Integer = 0
            Dim firstFailure As ExchangeDriveException = Nothing
            For Each entry As Standing In due
                Try
                    Await WriteAsync(entry.Kind, entry.Id, entry.RecordJson, entry.TtlSeconds, cancellation).ConfigureAwait(False)
                    entry.LastWriteMs = _monotonicMs()
                    rewritten += 1
                Catch ex As ExchangeDriveException
                    If firstFailure Is Nothing Then firstFailure = ex
                End Try
            Next
            If firstFailure IsNot Nothing Then Throw firstFailure
            Return rewritten
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>Stops keeping a record alive and deletes its file (only this device's own can be here).</summary>
    Public Async Function WithdrawAsync(kind As ExchangeDriveRecordKind, id As String, cancellation As CancellationToken) As Task
        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            _standing.Remove(KeyOf(kind, id))
            Dim name As String = ExchangeDriveRecords.FileName(kind, id)
            If name.Length = 0 Then Return
            Dim fileId As String = Await FindFileIdAsync(name, cancellation).ConfigureAwait(False)
            If fileId.Length = 0 Then Return
            Await _client.DeleteAsync(fileId, cancellation).ConfigureAwait(False)
            _fileIds.Remove(name)
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>Deletes every file this device wrote (sign-out, acceptance criterion 4) and stops keeping anything alive.
    ''' Returns how many files were deleted. Other devices' records and the packet queue are not touched.</summary>
    Public Async Function DeleteAllOwnAsync(cancellation As CancellationToken) As Task(Of Integer)
        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            _standing.Clear()
            Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
            Dim deleted As Integer = 0
            For Each file As ExchangeDriveFile In OwnRecordFiles(listing)
                Await _client.DeleteAsync(file.Id, cancellation).ConfigureAwait(False)
                deleted += 1
            Next
            _fileIds.Clear()
            SyncLock _cache
                _cache.Clear()
            End SyncLock
            Return deleted
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>Removes this device's own records that are past a full ttl on Drive's clock - "on its next start" (8.1): a
    ''' crash leaves records behind, and the next start sweeps them. Returns how many were deleted.</summary>
    Public Async Function DeleteOwnExpiredAsync(cancellation As CancellationToken) As Task(Of Integer)
        Await _gate.WaitAsync(cancellation).ConfigureAwait(False)
        Try
            Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
            If Not listing.HasServerDate Then Return 0
            Dim deleted As Integer = 0
            For Each file As ExchangeDriveFile In OwnRecordFiles(listing)
                Dim kind As ExchangeDriveRecordKind
                Dim id As String = ""
                If Not ExchangeDriveRecords.TryParseFileName(file.Name, kind, id) Then Continue For
                ' Judged with the kind's ttl: this device wrote it, so the default it wrote is the ttl it stands for.
                If ExchangeDriveRecords.Judge(file.ModifiedTime, listing.ServerDate, ExchangeDriveRecords.DefaultTtlSeconds(kind)) = ExchangeDriveRecordState.Expired Then
                    If _standing.ContainsKey(KeyOf(kind, id)) Then Continue For
                    Await _client.DeleteAsync(file.Id, cancellation).ConfigureAwait(False)
                    _fileIds.Remove(file.Name)
                    deleted += 1
                End If
            Next
            Return deleted
        Finally
            _gate.Release()
        End Try
    End Function

    ''' <summary>The record files this device wrote: the owner marker names it, or - for a file written before the marker or by a
    ''' reinstalled copy - the device record that carries its own id in its name. Only root record files, never the queue.</summary>
    Private Function OwnRecordFiles(listing As ExchangeDriveListing) As List(Of ExchangeDriveFile)
        Dim result As New List(Of ExchangeDriveFile)
        Dim folderIds As HashSet(Of String) = FolderIdsOf(listing)
        For Each file As ExchangeDriveFile In listing.Files
            If file.IsFolder OrElse Not IsRootFile(file, folderIds) Then Continue For
            Dim kind As ExchangeDriveRecordKind
            Dim id As String = ""
            If Not ExchangeDriveRecords.TryParseFileName(file.Name, kind, id) Then Continue For
            Dim marked As Boolean = String.Equals(file.AppProperty(OwnerProperty), _ownDeviceId, StringComparison.Ordinal)
            Dim ownDeviceFile As Boolean = kind = ExchangeDriveRecordKind.Device AndAlso String.Equals(id, _ownDeviceId, StringComparison.Ordinal)
            If marked OrElse ownDeviceFile Then result.Add(file)
        Next
        Return result
    End Function

    Friend Shared Function FolderIdsOf(listing As ExchangeDriveListing) As HashSet(Of String)
        Dim ids As New HashSet(Of String)(StringComparer.Ordinal)
        For Each file As ExchangeDriveFile In listing.Files
            If file.IsFolder Then ids.Add(file.Id)
        Next
        Return ids
    End Function

    ''' <summary>A file is at the root of the appDataFolder when none of its parents is a folder of the listing - the root is
    ''' not a listed file, a packet folder is. This keeps a payload file that happens to be named like a record out of the directory.</summary>
    Friend Shared Function IsRootFile(file As ExchangeDriveFile, folderIds As HashSet(Of String)) As Boolean
        For Each parent As String In file.Parents
            If folderIds.Contains(parent) Then Return False
        Next
        Return True
    End Function

    ' ------------------------------------------------------------------------------------------ reading

    ''' <summary>Reads everybody's records (one listing, then one small download per record that changed since the last read) into
    ''' a snapshot without this device's own records.</summary>
    Public Async Function ReadAsync(cancellation As CancellationToken) As Task(Of ExchangeDriveDirectoryResult)
        Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(cancellation).ConfigureAwait(False)
        Return Await ReadFromAsync(listing, cancellation).ConfigureAwait(False)
    End Function

    ''' <summary>The same, from a listing the caller already holds (the queue's poll listed the folder once for both).</summary>
    Public Async Function ReadFromAsync(listing As ExchangeDriveListing, cancellation As CancellationToken) As Task(Of ExchangeDriveDirectoryResult)
        Dim devices As New List(Of ExchangeDeviceRecord)
        Dim resources As New List(Of ExchangeResourceRecord)
        Dim broadcasts As New List(Of ExchangeBroadcastRecord)
        Dim skipped As Integer = 0
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim folderIds As HashSet(Of String) = FolderIdsOf(listing)

        For Each file As ExchangeDriveFile In listing.Files
            If file.IsFolder OrElse Not IsRootFile(file, folderIds) Then Continue For
            Dim kind As ExchangeDriveRecordKind
            Dim id As String = ""
            If Not ExchangeDriveRecords.TryParseFileName(file.Name, kind, id) Then Continue For
            If kind = ExchangeDriveRecordKind.Request Then Continue For
            seen.Add(file.Id)

            Dim decoded As ExchangeDriveRecordFile = Await DecodeCachedAsync(file, kind, cancellation).ConfigureAwait(False)
            If decoded Is Nothing Then
                skipped += 1
                Continue For
            End If

            ' Item C: expired is judged on Drive's clock. Without the header nothing can be proven stale, so the verdict is the
            ' record's own word (a header-less listing does not happen in practice; this keeps the reader from inventing one).
            Dim expired As Boolean = listing.HasServerDate AndAlso ExchangeDriveRecords.Judge(file.ModifiedTime, listing.ServerDate, decoded.TtlSeconds) = ExchangeDriveRecordState.Expired
            Select Case kind
                Case ExchangeDriveRecordKind.Device
                    Dim record As ExchangeDeviceRecord = Nothing
                    If ExchangeRecordReader.TryReadDevice(Presented(decoded.RecordJson, expired, file.ModifiedTime), record) Then devices.Add(record) Else skipped += 1
                Case ExchangeDriveRecordKind.Resource
                    Dim record As ExchangeResourceRecord = Nothing
                    If ExchangeRecordReader.TryReadResource(Presented(decoded.RecordJson, expired, file.ModifiedTime), record) Then resources.Add(record) Else skipped += 1
                Case ExchangeDriveRecordKind.Broadcast
                    ' an expired broadcast has ended: it is not listed at all
                    If expired Then Continue For
                    Dim record As ExchangeBroadcastRecord = Nothing
                    If ExchangeRecordReader.TryReadBroadcast(decoded.RecordJson, record) Then broadcasts.Add(record) Else skipped += 1
            End Select
        Next

        ' forget cached bodies of files that are gone
        Dim revision As Long
        SyncLock _cache
            For Each key As String In _cache.Keys.Where(Function(k) Not seen.Contains(k)).ToList()
                _cache.Remove(key)
            Next
            _revision += 1
            revision = _revision
        End SyncLock
        Dim snapshot As New ExchangeDirectorySnapshot(revision, devices, resources, broadcasts)
        Return New ExchangeDriveDirectoryResult(snapshot.WithoutDevice(_ownDeviceId), listing, skipped)
    End Function

    Private Async Function DecodeCachedAsync(file As ExchangeDriveFile, kind As ExchangeDriveRecordKind, cancellation As CancellationToken) As Task(Of ExchangeDriveRecordFile)
        Dim cached As CachedRecord = Nothing
        SyncLock _cache
            If _cache.TryGetValue(file.Id, cached) AndAlso String.Equals(cached.ModifiedTime, file.ModifiedTime, StringComparison.Ordinal) Then Return cached.File
        End SyncLock

        Dim body As Byte()
        Try
            body = Await _client.DownloadBytesAsync(file.Id, MaxRecordFileBytes, cancellation).ConfigureAwait(False)
        Catch ex As ExchangeDriveException When ex.Failure = ExchangeDriveFailure.NotFound OrElse ex.Failure = ExchangeDriveFailure.Other
            ' gone between the listing and the download, or over the bound: not a record this poll
            Return Nothing
        End Try
        Dim decoded As ExchangeDriveRecordFile = Nothing
        Dim refusal As ExchangeDriveRecordRefusal
        If Not ExchangeDriveRecords.TryDecode(body, kind, decoded, refusal) Then decoded = Nothing
        SyncLock _cache
            _cache(file.Id) = New CachedRecord() With {.ModifiedTime = file.ModifiedTime, .File = decoded}
        End SyncLock
        Return decoded
    End Function

    ''' <summary>The record as a reader presents it: `lastSeen` is Drive's modifiedTime (the one clock all devices agree on),
    ''' `presence` is "offline" for an expired record and "online" for a live one that does not say otherwise.</summary>
    Friend Shared Function Presented(recordJson As String, expired As Boolean, modifiedTime As String) As String
        Try
            Dim node As JsonObject = TryCast(JsonNode.Parse(recordJson), JsonObject)
            If node Is Nothing Then Return recordJson
            If modifiedTime.Length > 0 Then node("lastSeen") = modifiedTime
            If expired Then
                node("presence") = ExchangeWire.PresenceOffline
            Else
                Dim presence As JsonNode = Nothing
                If Not node.TryGetPropertyValue("presence", presence) OrElse presence Is Nothing OrElse String.IsNullOrEmpty(presence.ToString()) Then node("presence") = ExchangeWire.PresenceOnline
            End If
            Return node.ToJsonString()
        Catch ex As JsonException
            Return recordJson
        End Try
    End Function

End Class
#End If
