Option Strict On

Imports System.IO
Imports System.Threading

' Ticket 269 P4 (DEVICE-EXCHANGE item CP, spec 230 rule 6): the clipboard value of the person's own Google account, the second place
' the two clipboard actions keep a value in, beside the exchange servers.
'   - turned on by the Drive sign-in and by nothing else: without it this path does not exist and nothing reads the clipboard;
'   - not sealed in this version (item DO); DriveChannelText.PrivacyNote says so wherever the path is described;
'   - a receiver never deletes the value it took; the sender supersedes the older ones (ExchangeDriveQueue);
'   - a text or an image is kept in memory, never on the disk; a file, or a value over the memory bound, goes to the received-files
'     folder under a free name, with a file drop and the "downloaded" mark, like the exchange server's;
'   - the log carries the kind and a size class, never a value, a file name, a path or a device (spec 230 rule 10).

''' <summary>What the person's Google account holds: its clipboard value (Nothing when there is none) or why that is not known.</summary>
Friend NotInheritable Class ClipboardDriveAnswer

    Public ReadOnly Value As ExchangeDrivePacket
    Public ReadOnly Failure As ExchangeDriveFailure

    Public Sub New(value As ExchangeDrivePacket, failure As ExchangeDriveFailure)
        Me.Value = value
        Me.Failure = failure
    End Sub

End Class

Friend NotInheritable Class ClipboardDrivePath

    Friend Const SlotLabel As String = "Google Drive"
    Private Const ImageFileName As String = "clipboard.png"
    Private Const GigaByte As Long = 1024L * 1024L * 1024L

    Private ReadOnly _queue As ExchangeDriveQueue
    Private ReadOnly _client As ExchangeDriveClient

    Public Sub New(queue As ExchangeDriveQueue, client As ExchangeDriveClient)
        _queue = queue
        _client = client
    End Sub

    ''' <summary>True when the person turned the Drive channel on here: a sign-in is stored and this build has the OAuth client. A plain
    ''' existence check - nothing is created, no token is opened and no network is touched; this decides whether a button is drawn.</summary>
    Public Shared Function IsOffered() As Boolean
        If Not ExchangeFeatures.IsEnabled() Then Return False
        Return DriveChannelHosts.TokenPresent.Invoke() AndAlso ExchangeDriveClientConfig.Resolve().IsConfigured
    End Function

    ''' <summary>The path of this process, or Nothing when the channel is off. A sign-in that cannot be used gives a path that answers
    ''' "signed out", so the person who sees the button is told why it does nothing. No poller starts for it (a desktop shortcut's run).</summary>
    Public Shared Function ForThisProcess() As ClipboardDrivePath
        If Not IsOffered() Then Return Nothing
        Dim service As ExchangeDriveService = DriveChannelHosts.Current().ServiceForOneAction()
        If service Is Nothing Then Return New ClipboardDrivePath(Nothing, Nothing)
        Return New ClipboardDrivePath(service.Packets, service.Client)
    End Function

    ''' <summary>What the account holds, by a fresh listing and without a byte of the value. Never throws for Drive.</summary>
    Public Async Function StateAsync(ct As CancellationToken) As Task(Of ClipboardDriveAnswer)
        If _queue Is Nothing Then Return New ClipboardDriveAnswer(Nothing, ExchangeDriveFailure.SignedOut)
        Try
            Dim listing As ExchangeDriveListing = Await _client.ListAllAsync(ct).ConfigureAwait(False)
            Dim poll As ExchangeDriveQueuePoll = Await _queue.PollAsync(listing, ct).ConfigureAwait(False)
            Return New ClipboardDriveAnswer(poll.ClipboardValue, ExchangeDriveFailure.None)
        Catch ex As ExchangeDriveException
            Return New ClipboardDriveAnswer(Nothing, ex.Failure)
        Catch ex As OperationCanceledException When ct.IsCancellationRequested
            ' the caller looks at the token next
            Return New ClipboardDriveAnswer(Nothing, ExchangeDriveFailure.None)
        End Try
    End Function

    ''' <summary>Stores what was read as the account's value. One line, whatever happens.</summary>
    Public Async Function SendAsync(snapshot As ClipboardSnapshot, kind As String, name As String, mime As String,
                                    progress As Action(Of Long, Long), ct As CancellationToken) As Task(Of ClipboardLine)
        If _queue Is Nothing Then Return LineOfFailure(ExchangeDriveFailure.SignedOut)
        Try
            Dim source As ExchangeDriveSendSource
            Select Case snapshot.Kind
                Case ClipboardReadKind.OneFile
                    source = ExchangeDriveSendSource.FromFile(snapshot.Path, name, mime)
                Case ClipboardReadKind.Image
                    source = ExchangeDriveSendSource.FromBytes(ImageFileName, "image/png", snapshot.Content)
                Case Else
                    ' Already the wire form of item BX: UTF-8, LF.
                    source = ExchangeDriveSendSource.FromBytes(ExchangeDrivePackets.ClipboardTextFileName, "text/plain", snapshot.Content)
            End Select
            Dim result As ExchangeDriveSendResult = Await _queue.SendClipboardAsync(source, kind, ct, progress).ConfigureAwait(False)
            Select Case result.Outcome
                Case ExchangeDriveSendOutcome.Sent
                    Return New ClipboardLine(ClipboardLineCode.SentViaDrive)
                Case ExchangeDriveSendOutcome.DriveFull
                    Return New ClipboardLine(ClipboardLineCode.DriveFull, detail:=(Math.Max(0L, result.FreeBytes) \ (1024L * 1024L)).ToString(Globalization.CultureInfo.InvariantCulture))
                Case ExchangeDriveSendOutcome.TooLarge
                    Return OverLimitLine(_queue.PacketBytes)
                Case Else
                    Return New ClipboardLine(ClipboardLineCode.DriveBadName)
            End Select
        Catch ex As ExchangeDriveException
            Return LineOfFailure(ex.Failure)
        Catch ex As OperationCanceledException
            Return New ClipboardLine(ClipboardLineCode.Cancelled)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            ' the file of the file drop went away or cannot be read
            Return New ClipboardLine(ClipboardLineCode.ClipboardUnreadable)
        End Try
    End Function

    ''' <summary>The refusal of a value over the largest packet (item CS).</summary>
    Public Shared Function OverLimitLine(packetBytes As Long) As ClipboardLine
        Return New ClipboardLine(ClipboardLineCode.OverDriveLimit, detail:=(packetBytes \ GigaByte).ToString(Globalization.CultureInfo.InvariantCulture))
    End Function

    ''' <summary>Takes the value <see cref="StateAsync"/> found into the clipboard. One line, whatever happens.</summary>
    Public Async Function TakeAsync(value As ExchangeDrivePacket, ports As ClipboardExchangePorts,
                                    progress As Action(Of Long, Long), ct As CancellationToken) As Task(Of ClipboardLine)
        Dim manifest As ExchangeDrivePacketManifest = value.Manifest
        Dim device As String = manifest.SenderDeviceName
        Dim isImage As Boolean = String.Equals(manifest.ClipKind, ExchangeClipboardKinds.Image, StringComparison.Ordinal)
        Dim isText As Boolean = String.Equals(manifest.ClipKind, ExchangeClipboardKinds.Text, StringComparison.Ordinal)
        Dim kind As String = If(isImage, ExchangeClipboardKinds.Image, If(isText, ExchangeClipboardKinds.Text, ExchangeClipboardKinds.File))
        Dim memoryBound As Long = If(isText, ExchangeDriveQueue.MaxClipboardTextBytes, If(isImage, ClipboardExchange.MemoryCap, -1L))
        Dim inMemory As Boolean = memoryBound >= 0 AndAlso manifest.TotalSizeBytes <= memoryBound
        Dim line As ClipboardLine
        Try
            line = Await PlaceAsync(value, inMemory, isImage, memoryBound, device, ports, progress, ct).ConfigureAwait(False)
        Catch ex As ExchangeDriveException
            line = LineOfFailure(ex.Failure)
        Catch ex As OperationCanceledException
            line = New ClipboardLine(ClipboardLineCode.Cancelled)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            line = New ClipboardLine(ClipboardLineCode.SaveFailed)
        End Try
        ports.Log.Invoke("Clipboard get: via=drive kind=" & kind & " size=" & ClipboardExchange.SizeClass(manifest.TotalSizeBytes) & " outcome=" & line.Code.ToString())
        Return line
    End Function

    Private Async Function PlaceAsync(value As ExchangeDrivePacket, inMemory As Boolean, isImage As Boolean, memoryBound As Long, device As String,
                                      ports As ClipboardExchangePorts, progress As Action(Of Long, Long), ct As CancellationToken) As Task(Of ClipboardLine)
        If inMemory Then
            Dim read As ExchangeDriveReceiveResult = Await _queue.ReadClipboardAsync(value, CInt(memoryBound), ct, progress).ConfigureAwait(False)
            ' A value that is not whole, or not what its manifest said, is never placed.
            If Not read.Succeeded Then Return New ClipboardLine(ClipboardLineCode.Incomplete)
            Dim placedValue As ClipboardPlaceOutcome
            If isImage Then
                placedValue = Await ports.Port.PlaceImageAsync(read.Bytes, ct).ConfigureAwait(False)
            Else
                placedValue = Await ports.Port.PlaceTextAsync(read.Bytes, ct).ConfigureAwait(False)
            End If
            If placedValue <> ClipboardPlaceOutcome.Placed Then Return LineOfPlace(placedValue)
            Return New ClipboardLine(ClipboardLineCode.ReceivedValueViaDrive, "", "", device)
        End If

        Dim folder As String = ports.ReceivedFolder.Invoke()
        Dim received As ExchangeDriveReceiveResult = Await _queue.ReceiveClipboardAsync(value, folder, ct, progress).ConfigureAwait(False)
        If Not received.Succeeded Then Return New ClipboardLine(ClipboardLineCode.Incomplete)
        If received.SavedPaths.Count = 0 Then
            ' The manifest overstated a text the queue still read as a text: it is a value, not a file.
            Dim placedText As ClipboardPlaceOutcome = Await ports.Port.PlaceTextAsync(New System.Text.UTF8Encoding(False).GetBytes(received.Text), ct).ConfigureAwait(False)
            If placedText <> ClipboardPlaceOutcome.Placed Then Return LineOfPlace(placedText)
            Return New ClipboardLine(ClipboardLineCode.ReceivedValueViaDrive, "", "", device)
        End If
        Dim saved As String = received.SavedPaths(0)
        ClipboardExchange.MarkDownloaded(saved)
        Dim placedFile As ClipboardPlaceOutcome = Await ports.Port.PlaceFileAsync(saved, ct).ConfigureAwait(False)
        If placedFile <> ClipboardPlaceOutcome.Placed Then Return LineOfPlace(placedFile)
        Return New ClipboardLine(ClipboardLineCode.ReceivedFileViaDrive, "", "", device, Path.GetDirectoryName(saved))
    End Function

    Private Shared Function LineOfPlace(outcome As ClipboardPlaceOutcome) As ClipboardLine
        Return New ClipboardLine(If(outcome = ClipboardPlaceOutcome.Busy, ClipboardLineCode.ClipboardBusy, ClipboardLineCode.PlaceFailed))
    End Function

    ''' <summary>The line of a Drive failure. A value that vanished was replaced by a newer one (item CP): the next press takes that.</summary>
    Friend Shared Function LineOfFailure(failure As ExchangeDriveFailure) As ClipboardLine
        Select Case failure
            Case ExchangeDriveFailure.SignedOut
                Return New ClipboardLine(ClipboardLineCode.DriveSignedOut)
            Case ExchangeDriveFailure.Offline
                Return New ClipboardLine(ClipboardLineCode.DriveOffline)
            Case ExchangeDriveFailure.DailyLimit
                Return New ClipboardLine(ClipboardLineCode.DriveDailyLimit)
            Case ExchangeDriveFailure.DriveFull
                Return New ClipboardLine(ClipboardLineCode.DriveFull)
            Case ExchangeDriveFailure.NotFound
                Return New ClipboardLine(ClipboardLineCode.DriveValueReplaced)
            Case Else
                Return New ClipboardLine(ClipboardLineCode.DriveUnavailable)
        End Select
    End Function

End Class
