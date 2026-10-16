Option Strict On

Imports System.IO
Imports System.Threading

' Spec 230 T-2: "send the clipboard to the server" and "get the clipboard from the server" for the Share Manager.
'
' The protocol (DEVICE-EXCHANGE section 22) is ExchangeClipboardClient's; the clipboard is IClipboardPort's. This file is the
' person's two actions made of them:
'   - SEND reads the clipboard ONCE (nothing is read when no server is configured), then sends the value to every server the
'     person holds an account on - the Share Manager's own account and the viewer's entries, one line per server (rule 4);
'   - GET asks every server what its clipboard holds, takes the NEWEST by the server's receive time and names the device it came
'     from (rule 5); text and an image go to the clipboard as such, a file is saved under its own name or the free name of
'     ticket 222 into the received-files folder and placed as a file drop. Nothing opens by itself;
'   - one operation at a time (item Q): a press while one runs gets one sentence;
'   - the log carries the kind and a size class and a count, never a value, a file name, a path, a server or a device (item R).
' Words: the result lines are CODES composed into the person's language at display time (ClipboardMessages), like every
' Share Manager operation's; a failure's reason is a row of the common connection-message table.

''' <summary>One server of the person's, as the clipboard functions address it.</summary>
Friend NotInheritable Class ClipboardTarget

    ''' <summary>What a result line names: the server's own name, else its address.</summary>
    Public ReadOnly Label As String
    ''' <summary>Address and login folded - the key that keeps one account from being counted twice.</summary>
    Public ReadOnly IdentityKey As String
    Public ReadOnly Address As ExchangeServerAddress
    Public ReadOnly Pin As String
    Public ReadOnly Credentials As ExchangeDeviceCredentials
    ''' <summary>The renewal-aware transport of the entry: a re-issued certificate updates only its own pin.</summary>
    Public ReadOnly Transport As IExchangeTransport
    ''' <summary>The folder beside the device token where this device's key and the kept keys of the entry live (spec 234 items DF,
    ''' DG); "" when there is none. A value is sealed with the key found there, and with no key nothing is sent or opened.</summary>
    Public ReadOnly KeyFolder As String

    Public Sub New(label As String, identityKey As String, address As ExchangeServerAddress, pin As String,
                   credentials As ExchangeDeviceCredentials, transport As IExchangeTransport, Optional keyFolder As String = "")
        Me.Label = If(label, "")
        Me.IdentityKey = If(identityKey, "")
        Me.Address = address
        Me.Pin = If(pin, "")
        Me.Credentials = credentials
        Me.Transport = transport
        Me.KeyFolder = If(keyFolder, "")
    End Sub

    ''' <summary>No member: a target is a server and a secret.</summary>
    Public Overrides Function ToString() As String
        Return "ClipboardTarget"
    End Function

End Class

''' <summary>Where the two functions find the person's servers.</summary>
Friend NotInheritable Class ClipboardTargets

    Private Sub New()
    End Sub

    ''' <summary>True when at least one account is configured. A pure read of two registry values: nothing is created, no
    ''' token is opened and no network is touched - this is what decides whether a button is drawn.</summary>
    Public Shared Function AnyConfigured(accounts As ExchangeAccountStore, entries As ExchangeEntryStore) As Boolean
        Try
            If accounts IsNot Nothing AndAlso accounts.IsConfigured() Then Return True
            If entries IsNot Nothing AndAlso entries.Count() > 0 Then Return True
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
            Return False
        End Try
        Return False
    End Function

    ''' <summary>
    ''' Every server the person can send to: the Share Manager's account first, then the viewer's entries that are not the same
    ''' account (address and login). An account whose token cannot be read, or whose address does not parse, is left out - a
    ''' value is never sent to a server with no credentials.
    ''' </summary>
    Public Shared Function Load(accounts As ExchangeAccountStore, credentials As ExchangeCredentialStore,
                                entries As ExchangeEntryStore, inner As IExchangeTransport) As List(Of ClipboardTarget)
        Dim result As New List(Of ClipboardTarget)()
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Try
            Dim account As ExchangeAccount = If(accounts IsNot Nothing, accounts.Load(), Nothing)
            If account IsNot Nothing Then
                Dim address As ExchangeServerAddress = Nothing
                Dim token As String = Nothing
                If account.TryGetAddress(address) AndAlso credentials IsNot Nothing AndAlso credentials.TryLoad(account.DeviceId, token) AndAlso Not String.IsNullOrEmpty(token) Then
                    Dim key As String = ExchangeEntryStore.IdentityOf(account.Server, account.Login)
                    If seen.Add(key) Then
                        result.Add(New ClipboardTarget(account.Server, key, address, account.Pin,
                                                       New ExchangeDeviceCredentials(account.DeviceId, token), accounts.TransportFor(account, inner),
                                                       credentials.FolderPath))
                    End If
                End If
            End If
            If entries IsNot Nothing Then
                For Each entry As ExchangeEntry In entries.Load()
                    Dim address As ExchangeServerAddress = Nothing
                    Dim token As String = Nothing
                    If Not entry.TryGetAddress(address) Then Continue For
                    Dim tokenStore As ExchangeCredentialStore = entries.TokenStoreFor(entry.Id)
                    If Not tokenStore.TryLoad(entry.DeviceId, token) OrElse String.IsNullOrEmpty(token) Then Continue For
                    If Not seen.Add(entry.IdentityKey) Then Continue For
                    result.Add(New ClipboardTarget(entry.DisplayName, entry.IdentityKey, address, entry.Pin,
                                                   New ExchangeDeviceCredentials(entry.DeviceId, token), entries.TransportFor(entry, inner),
                                                   tokenStore.FolderPath))
                Next
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Security.SecurityException
            ' A store that cannot be read gives the servers read so far - never a half-built credential.
        End Try
        Return result
    End Function

End Class

''' <summary>What one result line says. The words are composed at display time (<see cref="ClipboardMessages"/>).</summary>
Friend Enum ClipboardLineCode
    ''' <summary>Label: the value was stored on that server.</summary>
    Sent = 0
    NothingToSend = 1
    NotOneObject = 2
    ClipboardBusy = 3
    ClipboardUnreadable = 4
    ''' <summary>No account: nothing was read.</summary>
    NoServer = 5
    ''' <summary>Label + Reason (a code of the common message table).</summary>
    Failure = 6
    ''' <summary>No server holds a value (the common table's "clipboard-empty").</summary>
    ServerEmpty = 7
    ''' <summary>Device: a text or an image is in the clipboard.</summary>
    ReceivedValue = 8
    ''' <summary>Device + Detail (the folder): a file is saved and in the clipboard as a file drop.</summary>
    ReceivedFile = 9
    OperationUnderWay = 10
    PlaceFailed = 11
    SaveFailed = 12
    ''' <summary>The bytes that arrived are not the value the server named: nothing was placed.</summary>
    Incomplete = 13
    Cancelled = 14
    ''' <summary>Label: the server does not carry the clipboard for this account (no such feature, or a guest account).</summary>
    NotOffered = 15
    ''' <summary>A text or an image bigger than the program keeps in memory: nothing was sent.</summary>
    TooLarge = 16
    ' The four states of DEVICE-EXCHANGE 27 item DR and the one benign case of item DN. Their words are the common table's
    ' rows `server-no-e2e`, `e2e-missing`, `key-changed` and `sealed-failed` (ClipboardMessages); none of them places anything.
    ''' <summary>Label: the server lists no `sealed`, so nothing goes through it (a send) or the value on it is not used (a get).</summary>
    ServerNoE2e = 17
    ''' <summary>Label: no device of the account can read a sealed value (a send), or the value came from one that cannot seal (a get).</summary>
    E2eMissing = 18
    ''' <summary>Label: the key of the sending device is not the one kept for it: nothing was placed.</summary>
    KeyChanged = 19
    ''' <summary>Label: the value could not be opened (a failed tag, a missing last segment, a size or hash that differs): nothing was placed.</summary>
    SealedFailed = 20
    ''' <summary>Label: the value was sent before this device joined the account; it is not an error, the next value opens.</summary>
    JoinedLater = 21
End Enum

Friend NotInheritable Class ClipboardLine

    Public ReadOnly Code As ClipboardLineCode
    Public ReadOnly Label As String
    Public ReadOnly Reason As String
    Public ReadOnly Device As String
    Public ReadOnly Detail As String

    Public Sub New(code As ClipboardLineCode, Optional label As String = "", Optional reason As String = "",
                   Optional device As String = "", Optional detail As String = "")
        Me.Code = code
        Me.Label = If(label, "")
        Me.Reason = If(reason, "")
        Me.Device = If(device, "")
        Me.Detail = If(detail, "")
    End Sub

    ''' <summary>The code only.</summary>
    Public Overrides Function ToString() As String
        Return "ClipboardLine " & Code.ToString()
    End Function

End Class

Friend NotInheritable Class ClipboardRunResult

    Public ReadOnly Lines As IReadOnlyList(Of ClipboardLine)
    ''' <summary>At least one server stored the value (a send) or the value is in the clipboard (a get).</summary>
    Public ReadOnly Succeeded As Boolean

    Public Sub New(lines As IReadOnlyList(Of ClipboardLine), succeeded As Boolean)
        Me.Lines = lines
        Me.Succeeded = succeeded
    End Sub

End Class

''' <summary>Everything the two actions need from outside, so a test hands in fakes.</summary>
Friend NotInheritable Class ClipboardExchangePorts

    Public Property Targets As Func(Of List(Of ClipboardTarget)) =
        Function() ClipboardTargets.Load(ExchangeAccountStore.CreateDefault(), ExchangeCredentialStore.CreateDefault(),
                                         ExchangeEntryStore.CreateDefault(), ExchangeTlsTransport.Instance)
    Public Property Port As IClipboardPort = New ClipboardWin32()
    Public Property ProductVersion As String = ExchangeProductVersion.Current()
    ''' <summary>The folder a received file is saved into.</summary>
    Public Property ReceivedFolder As Func(Of String) = Function() ClipboardSettings.ReceivedFolder()
    Public Property Log As Action(Of String) = Sub(line) AppFileLogger.WriteLine(line)
    ''' <summary>The longest a server may be silent before the exchange ends (a test shortens it); Zero is the client's own bound.</summary>
    Public Property Idle As TimeSpan = TimeSpan.Zero
    ''' <summary>How long "what does your clipboard hold" waits for one server before another's answer is used instead.</summary>
    Public Property StateIdle As TimeSpan = TimeSpan.FromSeconds(20)
    ''' <summary>The key, the id and the directory a value to this server is sealed with and opened by (spec 234 T-4). The default asks
    ''' the Share Manager's agent when it runs this device, else opens a short session of its own: see <see cref="ClipboardSealingProvider"/>.
    ''' The caller disposes the result. A test hands in a directory it controls.</summary>
    Public Property Sealing As Func(Of ClipboardTarget, CancellationToken, Task(Of ExchangeClipboardSealing)) =
        Function(target As ClipboardTarget, ct As CancellationToken) ClipboardSealingProvider.ResolveAsync(target, ExchangeProductVersion.Current(), ct)

End Class

Friend NotInheritable Class ClipboardExchange

    Private ReadOnly _ports As ClipboardExchangePorts
    Private _running As Integer

    Public Sub New(Optional ports As ClipboardExchangePorts = Nothing)
        _ports = If(ports, New ClipboardExchangePorts())
    End Sub

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return Volatile.Read(_running) <> 0
        End Get
    End Property

    ''' <summary>Largest value kept in memory; a text or an image beyond it is saved as a file instead of lost.</summary>
    Friend Const MemoryCap As Long = ClipboardWin32.MaxInMemoryBytes

    ' --- SEND ---------------------------------------------------------------------------------------------

    ''' <param name="progress">Called with (bytes done, bytes total) for the server being sent to - from any thread.</param>
    Public Async Function SendAsync(progress As Action(Of Long, Long), ct As CancellationToken) As Task(Of ClipboardRunResult)
        If Interlocked.CompareExchange(_running, 1, 0) <> 0 Then Return One(ClipboardLineCode.OperationUnderWay)
        Try
            Dim targets As List(Of ClipboardTarget) = _ports.Targets.Invoke()
            ' With no account nothing reads the clipboard (rule 9).
            If targets.Count = 0 Then Return One(ClipboardLineCode.NoServer)

            Dim snapshot As ClipboardSnapshot = Await _ports.Port.ReadAsync(ct).ConfigureAwait(False)
            If ct.IsCancellationRequested Then Return One(ClipboardLineCode.Cancelled)
            Dim refusal As ClipboardLine = RefusalOf(snapshot)
            If refusal IsNot Nothing Then
                _ports.Log.Invoke("Clipboard send: not sent (" & snapshot.Kind.ToString() & ")")
                Return New ClipboardRunResult({refusal}, False)
            End If

            Dim kind As String
            Dim mime As String
            Dim name As String = ""
            Select Case snapshot.Kind
                Case ClipboardReadKind.Text
                    ' Item BW: a text carries no mimeType and no name (its form is always UTF-8, LF).
                    kind = ExchangeClipboardKinds.Text
                    mime = ""
                Case ClipboardReadKind.Image
                    kind = ExchangeClipboardKinds.Image
                    mime = "image/png"
                Case Else
                    kind = ExchangeClipboardKinds.File
                    name = ExchangeClipboardRules.NameForSend(snapshot.Path)
                    mime = MimeForFile(name)
            End Select
            If snapshot.Size < 1L Then Return One(ClipboardLineCode.NothingToSend)

            Dim lines As New List(Of ClipboardLine)()
            Dim sent As Integer = 0
            ' The clear SHA-256 is stated by the sealed box, so it is known before the first byte leaves: a value in memory is hashed
            ' here, a file by one reading pass of its own - and only once, and only when a server can take the value (below).
            Dim clearSha As String = Nothing
            For Each target As ClipboardTarget In targets
                If ct.IsCancellationRequested Then
                    lines.Add(New ClipboardLine(ClipboardLineCode.Cancelled))
                    Exit For
                End If
                Using sealing As ExchangeClipboardSealing = Await _ports.Sealing.Invoke(target, ct).ConfigureAwait(False)
                    ' What the device already knows about this server stops the send before a file is read for a hash nobody will use.
                    Dim blocked As ClipboardLine = BlockedBefore(sealing, target.Label)
                    If blocked IsNot Nothing Then
                        lines.Add(blocked)
                        Continue For
                    End If
                    If clearSha Is Nothing Then
                        clearSha = Await ClearHashAsync(snapshot, ct).ConfigureAwait(False)
                        If clearSha Is Nothing Then
                            lines.Add(New ClipboardLine(If(ct.IsCancellationRequested, ClipboardLineCode.Cancelled, ClipboardLineCode.ClipboardUnreadable)))
                            Exit For
                        End If
                    End If
                    Dim source As Stream = OpenSource(snapshot)
                    If source Is Nothing Then
                        lines.Add(New ClipboardLine(ClipboardLineCode.ClipboardUnreadable))
                        Exit For
                    End If
                    Dim result As ExchangeClipboardResult
                    Using source
                        Using counted As New ProgressStream(source, snapshot.Size, progress)
                            result = Await ExchangeClipboardClient.PutSealedAsync(target.Transport, target.Address, target.Pin, target.Credentials,
                                                                                  _ports.ProductVersion, kind, mime, name, snapshot.Size, clearSha,
                                                                                  counted, sealing, ct, _ports.Idle).ConfigureAwait(False)
                        End Using
                    End Using
                    Dim line As ClipboardLine = LineOf(result, target.Label, isPut:=True)
                    If result.IsDone Then sent += 1
                    lines.Add(line)
                    If result.Outcome = ExchangeClipboardOutcome.Transport AndAlso result.Open = ExchangeOpenOutcome.Cancelled Then Exit For
                End Using
            Next
            _ports.Log.Invoke("Clipboard send: kind=" & kind & " size=" & SizeClass(snapshot.Size) &
                       " servers=" & targets.Count.ToString() & " sent=" & sent.ToString())
            Return New ClipboardRunResult(lines, sent > 0)
        Finally
            Volatile.Write(_running, 0)
        End Try
    End Function

    ''' <summary>The line a snapshot that cannot be sent gets, or Nothing when it can be.</summary>
    Private Shared Function RefusalOf(snapshot As ClipboardSnapshot) As ClipboardLine
        Select Case snapshot.Kind
            Case ClipboardReadKind.Text, ClipboardReadKind.Image, ClipboardReadKind.OneFile
                Return Nothing
            Case ClipboardReadKind.Empty
                Return New ClipboardLine(ClipboardLineCode.NothingToSend)
            Case ClipboardReadKind.NotOneObject
                Return New ClipboardLine(ClipboardLineCode.NotOneObject)
            Case ClipboardReadKind.Busy
                Return New ClipboardLine(ClipboardLineCode.ClipboardBusy)
            Case ClipboardReadKind.TooLarge
                Return New ClipboardLine(ClipboardLineCode.TooLarge)
            Case Else
                Return New ClipboardLine(ClipboardLineCode.ClipboardUnreadable)
        End Select
    End Function

    ''' <summary>The line of a server a value cannot go to, from what the directory of the entry already says; Nothing when the exchange
    ''' itself must find out (a server not yet heard from reads as unknown, so a down server reads as a failure and not as a missing key).
    ''' The client states the same two refusals from its own `welcome`; this only keeps a large file from being read for nothing.</summary>
    Private Shared Function BlockedBefore(sealing As ExchangeClipboardSealing, label As String) As ClipboardLine
        If sealing Is Nothing OrElse sealing.Directory Is Nothing OrElse Not sealing.Directory.ServerKnown Then Return Nothing
        If Not sealing.Directory.ServerSealed Then Return New ClipboardLine(ClipboardLineCode.ServerNoE2e, label, ConnectionDiagnosisMessages.ServerNoE2e.Code)
        If sealing.OwnKey Is Nothing OrElse sealing.Directory.Recipients(True).Count = 0 Then
            Return New ClipboardLine(ClipboardLineCode.E2eMissing, label, ConnectionDiagnosisMessages.E2eMissing.Code)
        End If
        Return Nothing
    End Function

    ''' <summary>The SHA-256 of the clear value, lower-case hex; Nothing when the file cannot be read to its end.</summary>
    Private Shared Async Function ClearHashAsync(snapshot As ClipboardSnapshot, ct As CancellationToken) As Task(Of String)
        If snapshot.Kind <> ClipboardReadKind.OneFile Then
            Return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot.Content)).ToLowerInvariant()
        End If
        Using source As Stream = OpenSource(snapshot)
            If source Is Nothing Then Return Nothing
            Return Await ExchangeClipboardSealing.Sha256HexAsync(source, snapshot.Size, ct).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>A fresh read stream of what was read: the bytes held, or the file opened for reading (never loaded whole).</summary>
    Private Shared Function OpenSource(snapshot As ClipboardSnapshot) As Stream
        Try
            If snapshot.Kind = ClipboardReadKind.OneFile Then
                Return New FileStream(snapshot.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete, 81920, FileOptions.SequentialScan)
            End If
            Return New MemoryStream(snapshot.Content, False)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
            Return Nothing
        End Try
    End Function

    ' --- GET ----------------------------------------------------------------------------------------------

    Public Async Function GetAsync(progress As Action(Of Long, Long), ct As CancellationToken) As Task(Of ClipboardRunResult)
        If Interlocked.CompareExchange(_running, 1, 0) <> 0 Then Return One(ClipboardLineCode.OperationUnderWay)
        Try
            Dim targets As List(Of ClipboardTarget) = _ports.Targets.Invoke()
            If targets.Count = 0 Then Return One(ClipboardLineCode.NoServer)

            ' 1. What every server holds - without its bytes, all at once: a server that is down costs its own wait, not the sum.
            Dim asks As New List(Of Task(Of ExchangeClipboardResult))()
            For Each target As ClipboardTarget In targets
                asks.Add(ExchangeClipboardClient.StateAsync(target.Transport, target.Address, target.Pin, target.Credentials,
                                                            _ports.ProductVersion, ct, _ports.StateIdle))
            Next
            Dim answers As ExchangeClipboardResult() = Await Task.WhenAll(asks).ConfigureAwait(False)
            If ct.IsCancellationRequested Then Return One(ClipboardLineCode.Cancelled)

            Dim lines As New List(Of ClipboardLine)()
            Dim bestIndex As Integer = -1
            Dim empties As Integer = 0
            For i As Integer = 0 To targets.Count - 1
                Dim answer As ExchangeClipboardResult = answers(i)
                If answer.IsDone Then
                    ' The newest by the server's receive time; on a tie the first listed keeps it.
                    If bestIndex < 0 OrElse answer.Value.StoredAtUtc > answers(bestIndex).Value.StoredAtUtc Then bestIndex = i
                ElseIf answer.Outcome = ExchangeClipboardOutcome.Empty Then
                    empties += 1
                Else
                    lines.Add(LineOf(answer, targets(i).Label, isPut:=False))
                End If
            Next

            If bestIndex < 0 Then
                If empties > 0 Then lines.Insert(0, New ClipboardLine(ClipboardLineCode.ServerEmpty, "", "clipboard-empty"))
                _ports.Log.Invoke("Clipboard get: servers=" & targets.Count.ToString() & " value=none empty=" & empties.ToString())
                Return New ClipboardRunResult(lines, False)
            End If

            ' 2. The newest value, by its own id (a value never swapped for a newer one between the two steps).
            Dim chosen As ClipboardTarget = targets(bestIndex)
            Dim announced As ExchangeClipboardValue = answers(bestIndex).Value
            Dim saved As New SavedFile()
            Dim memory As MemoryStream = Nothing
            Dim folder As String = _ports.ReceivedFolder.Invoke()
            Dim result As ExchangeClipboardResult
            Using sealing As ExchangeClipboardSealing = Await _ports.Sealing.Invoke(chosen, ct).ConfigureAwait(False)
                ' The value is opened by the key of this device; what it is (kind, name, size) is known only after the box is open, so
                ' the sink is chosen from the OPENED members, and nothing is placed unless every segment and the hash agree.
                result = Await ExchangeClipboardClient.GetSealedAsync(
                    chosen.Transport, chosen.Address, chosen.Pin, chosen.Credentials, _ports.ProductVersion, announced.ClipId, sealing,
                    Function(payload As ExchangeClipboardSealedPayload, value As ExchangeClipboardValue) As Stream
                        If payload.Size > ExchangeClipboardRules.DefaultMaxBytes Then Return Nothing
                        Dim asFile As Boolean = String.Equals(payload.Kind, ExchangeClipboardKinds.File, StringComparison.Ordinal) OrElse payload.Size > MemoryCap
                        Dim sink As Stream
                        If asFile Then
                            sink = saved.Open(folder)
                        Else
                            memory = New MemoryStream(CInt(Math.Min(payload.Size, Integer.MaxValue)))
                            sink = memory
                        End If
                        If sink Is Nothing Then Return Nothing
                        Return New ProgressStream(sink, payload.Size, progress, ownsInner:=False)
                    End Function, ct, _ports.Idle).ConfigureAwait(False)
            End Using

            Dim opened As ExchangeClipboardSealedPayload = result.Opened
            Dim device As String = If(result.Value IsNot Nothing, result.Value.SourceDeviceName, announced.SourceDeviceName)
            ' The kind is known only for a value that opened; a log line never names what could not be opened.
            Dim kind As String = If(opened IsNot Nothing, opened.Kind, "sealed")
            Dim sizeText As String = SizeClass(If(opened IsNot Nothing, opened.Size, 0L))

            If Not result.IsDone Then
                saved.Discard()
                ' Whatever part of an unopened value reached memory is wiped, not just dropped.
                If memory IsNot Nothing Then Array.Clear(memory.GetBuffer(), 0, memory.GetBuffer().Length)
                memory = Nothing
                _ports.Log.Invoke("Clipboard get: kind=" & kind & " outcome=" & result.Outcome.ToString())
                lines.Add(GetFailureLine(result, chosen.Label))
                Return New ClipboardRunResult(lines, False)
            End If

            ' 3. Placed: the clipboard is opened once, here, when the value is complete and every check held.
            Dim placed As ClipboardPlaceOutcome
            Dim lineForValue As ClipboardLine
            If saved.IsOpen Then
                Dim finalPath As String = saved.Complete(folder, ExchangeClipboardRules.ReceivedFileName(opened.Name, announced.StoredAtUtc, opened.MimeType))
                If finalPath Is Nothing Then
                    _ports.Log.Invoke("Clipboard get: kind=" & kind & " size=" & sizeText & " outcome=SaveFailed")
                    lines.Add(New ClipboardLine(ClipboardLineCode.SaveFailed))
                    Return New ClipboardRunResult(lines, False)
                End If
                placed = Await _ports.Port.PlaceFileAsync(finalPath, ct).ConfigureAwait(False)
                lineForValue = New ClipboardLine(ClipboardLineCode.ReceivedFile, "", "", device, Path.GetDirectoryName(finalPath))
            ElseIf String.Equals(opened.Kind, ExchangeClipboardKinds.Image, StringComparison.Ordinal) Then
                placed = Await _ports.Port.PlaceImageAsync(memory.ToArray(), ct).ConfigureAwait(False)
                lineForValue = New ClipboardLine(ClipboardLineCode.ReceivedValue, "", "", device)
            Else
                placed = Await _ports.Port.PlaceTextAsync(memory.ToArray(), ct).ConfigureAwait(False)
                lineForValue = New ClipboardLine(ClipboardLineCode.ReceivedValue, "", "", device)
            End If

            _ports.Log.Invoke("Clipboard get: kind=" & kind & " size=" & sizeText & " outcome=" & placed.ToString())
            If placed <> ClipboardPlaceOutcome.Placed Then
                lines.Add(New ClipboardLine(If(placed = ClipboardPlaceOutcome.Busy, ClipboardLineCode.ClipboardBusy, ClipboardLineCode.PlaceFailed)))
                Return New ClipboardRunResult(lines, False)
            End If
            lines.Insert(0, lineForValue)
            Return New ClipboardRunResult(lines, True)
        Finally
            Volatile.Write(_running, 0)
        End Try
    End Function

    ''' <summary>The line of a get that did not end in a value. A failed save or a declined value are the device's own side.</summary>
    Private Shared Function GetFailureLine(result As ExchangeClipboardResult, label As String) As ClipboardLine
        Select Case result.Outcome
            Case ExchangeClipboardOutcome.LocalFailure, ExchangeClipboardOutcome.Declined
                Return New ClipboardLine(ClipboardLineCode.SaveFailed)
            Case ExchangeClipboardOutcome.Mismatch
                Return New ClipboardLine(ClipboardLineCode.Incomplete)
            Case Else
                Return LineOf(result, label, isPut:=False)
        End Select
    End Function

    ' --- result -> line -----------------------------------------------------------------------------------

    ''' <summary>The line of one exchange's result. A refusal and a transport failure name their reason from the common table.</summary>
    Friend Shared Function LineOf(result As ExchangeClipboardResult, label As String, isPut As Boolean) As ClipboardLine
        Select Case result.Outcome
            Case ExchangeClipboardOutcome.Done
                Return New ClipboardLine(ClipboardLineCode.Sent, label)
            Case ExchangeClipboardOutcome.Empty
                Return New ClipboardLine(ClipboardLineCode.ServerEmpty, label, "clipboard-empty")
            Case ExchangeClipboardOutcome.NotOffered
                Return New ClipboardLine(ClipboardLineCode.NotOffered, label)
            Case ExchangeClipboardOutcome.Refused
                Return New ClipboardLine(ClipboardLineCode.Failure, label,
                                         ConnectionDiagnosisMessages.OfWire(result.RefusalName, False, isPut).Code)
            Case ExchangeClipboardOutcome.Mismatch
                Return New ClipboardLine(ClipboardLineCode.Incomplete, label)
            Case ExchangeClipboardOutcome.ServerNoE2e
                Return New ClipboardLine(ClipboardLineCode.ServerNoE2e, label, ConnectionDiagnosisMessages.ServerNoE2e.Code)
            ' A clear value is refused for the same reason as one from a device without a key (item DP).
            Case ExchangeClipboardOutcome.E2eMissing, ExchangeClipboardOutcome.ClearRefused
                Return New ClipboardLine(ClipboardLineCode.E2eMissing, label, ConnectionDiagnosisMessages.E2eMissing.Code)
            Case ExchangeClipboardOutcome.KeyChanged
                Return New ClipboardLine(ClipboardLineCode.KeyChanged, label, ConnectionDiagnosisMessages.KeyChanged.Code)
            Case ExchangeClipboardOutcome.SealedFailed
                Return New ClipboardLine(ClipboardLineCode.SealedFailed, label, ConnectionDiagnosisMessages.SealedFailed.Code)
            Case ExchangeClipboardOutcome.JoinedLater
                Return New ClipboardLine(ClipboardLineCode.JoinedLater, label)
            Case ExchangeClipboardOutcome.LocalFailure
                Return New ClipboardLine(ClipboardLineCode.ClipboardUnreadable, label)
            Case ExchangeClipboardOutcome.Invalid, ExchangeClipboardOutcome.Declined
                Return New ClipboardLine(ClipboardLineCode.NothingToSend, label)
            Case Else
                Select Case result.Open
                    Case ExchangeOpenOutcome.Cancelled
                        Return New ClipboardLine(ClipboardLineCode.Cancelled, label)
                    Case ExchangeOpenOutcome.PinMismatch
                        Return New ClipboardLine(ClipboardLineCode.Failure, label, ConnectionDiagnosisMessages.CertificateChanged.Code)
                    Case ExchangeOpenOutcome.Untrusted, ExchangeOpenOutcome.TlsFailed
                        Return New ClipboardLine(ClipboardLineCode.Failure, label, ConnectionDiagnosisMessages.TlsFailed.Code)
                    Case ExchangeOpenOutcome.Network
                        Return New ClipboardLine(ClipboardLineCode.Failure, label, ConnectionDiagnosisMessages.UnreachableServer.Code)
                    Case Else
                        Return New ClipboardLine(ClipboardLineCode.Failure, label, ConnectionDiagnosisMessages.Unanswered.Code)
                End Select
        End Select
    End Function

    Private Shared Function One(code As ClipboardLineCode) As ClipboardRunResult
        Return New ClipboardRunResult({New ClipboardLine(code)}, False)
    End Function

    ''' <summary>A size class for a log line: enough to see what kind of value it was, never its exact length.</summary>
    Friend Shared Function SizeClass(size As Long) As String
        If size <= 4096L Then Return "xs"
        If size <= 1048576L Then Return "s"
        If size <= 67108864L Then Return "m"
        If size <= 1073741824L Then Return "l"
        Return "xl"
    End Function

    Private Shared Function MimeForFile(name As String) As String
        Dim ext As String = Path.GetExtension(If(name, "")).ToLowerInvariant()
        Select Case ext
            Case ".png" : Return "image/png"
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".gif" : Return "image/gif"
            Case ".webp" : Return "image/webp"
            Case ".bmp" : Return "image/bmp"
            Case ".pdf" : Return "application/pdf"
            Case ".txt" : Return "text/plain"
            Case ".zip" : Return "application/zip"
            Case ".mp4" : Return "video/mp4"
            Case ".mp3" : Return "audio/mpeg"
            Case Else : Return "application/octet-stream"
        End Select
    End Function

    ' --- the received file --------------------------------------------------------------------------------

    ''' <summary>A file being received: written under a temporary name, then given its own name or the first free one - a name that
    ''' is already there is never overwritten (rule 5), and an interrupted transfer never leaves a file with a final name.</summary>
    Private NotInheritable Class SavedFile

        Private _part As String
        Private _stream As FileStream

        Public ReadOnly Property IsOpen As Boolean
            Get
                Return _part IsNot Nothing
            End Get
        End Property

        Public Function Open(folder As String) As Stream
            Try
                Directory.CreateDirectory(folder)
                _part = Path.Combine(folder, "fms-" & Guid.NewGuid().ToString("N") & ".part")
                _stream = New FileStream(_part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.None)
                Return _stream
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
                _part = Nothing
                _stream = Nothing
                Return Nothing
            End Try
        End Function

        ''' <summary>Removes what was written (a transfer that did not complete).</summary>
        Public Sub Discard()
            Close()
            If _part Is Nothing Then Return
            Try
                File.Delete(_part)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
            _part = Nothing
        End Sub

        Private Sub Close()
            If _stream Is Nothing Then Return
            Try
                _stream.Dispose()
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is ObjectDisposedException
            End Try
            _stream = Nothing
        End Sub

        ''' <summary>Gives the written file its name: <paramref name="wanted"/> when it is free, else "name (2).ext" and on. The path of
        ''' the saved file, or Nothing when it could not be named.</summary>
        Public Function Complete(folder As String, wanted As String) As String
            Close()
            If _part Is Nothing Then Return Nothing
            Dim reserved As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim candidate As String = Path.Combine(folder, wanted)
            For attempt As Integer = 0 To 99
                If Not File.Exists(candidate) AndAlso Not Directory.Exists(candidate) Then
                    Try
                        File.Move(_part, candidate, overwrite:=False)
                        _part = Nothing
                        MarkDownloaded(candidate)
                        Return candidate
                    Catch ex As IOException
                        ' Taken between the check and the move: the next free name.
                    Catch ex As Exception When TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException OrElse TypeOf ex Is NotSupportedException
                        Exit For
                    End Try
                End If
                reserved.Add(candidate)
                candidate = FreeNameRule.Find(folder, wanted,
                                              Function(dir As String, leaf As String) Path.Combine(dir, leaf),
                                              Function(p As String) File.Exists(p) OrElse Directory.Exists(p), reserved)
                If candidate Is Nothing Then Exit For
            Next
            Discard()
            Return Nothing
        End Function

        ''' <summary>The Windows "downloaded from the internet" mark, kept as for any download (default 3 of the specification). A file
        ''' system without streams simply has none.</summary>
        Private Shared Sub MarkDownloaded(path As String)
            Try
                File.WriteAllText(path & ":Zone.Identifier", "[ZoneTransfer]" & vbCrLf & "ZoneId=3" & vbCrLf)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is NotSupportedException OrElse TypeOf ex Is ArgumentException
            End Try
        End Sub

    End Class

End Class

''' <summary>A stream that reports how far it has been read or written. It reports; it never changes a byte.</summary>
Friend NotInheritable Class ProgressStream
    Inherits Stream

    Private ReadOnly _inner As Stream
    Private ReadOnly _total As Long
    Private ReadOnly _report As Action(Of Long, Long)
    Private ReadOnly _ownsInner As Boolean
    Private _done As Long

    Public Sub New(inner As Stream, total As Long, report As Action(Of Long, Long), Optional ownsInner As Boolean = False)
        _inner = inner
        _total = total
        _report = report
        _ownsInner = ownsInner
    End Sub

    Public Overrides ReadOnly Property CanRead As Boolean
        Get
            Return _inner.CanRead
        End Get
    End Property

    Public Overrides ReadOnly Property CanSeek As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overrides ReadOnly Property CanWrite As Boolean
        Get
            Return _inner.CanWrite
        End Get
    End Property

    Public Overrides ReadOnly Property Length As Long
        Get
            Return _total
        End Get
    End Property

    Public Overrides Property Position As Long
        Get
            Return _done
        End Get
        Set(value As Long)
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
        Dim n As Integer = _inner.Read(buffer, offset, count)
        Advance(n)
        Return n
    End Function

    Public Overrides Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
        Return ReadCountedAsync(buffer, offset, count, cancellationToken)
    End Function

    Private Async Function ReadCountedAsync(buffer As Byte(), offset As Integer, count As Integer, ct As CancellationToken) As Task(Of Integer)
        Dim n As Integer = Await _inner.ReadAsync(buffer, offset, count, ct).ConfigureAwait(False)
        Advance(n)
        Return n
    End Function

    Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
        _inner.Write(buffer, offset, count)
        Advance(count)
    End Sub

    Public Overrides Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task
        Return WriteCountedAsync(buffer, offset, count, cancellationToken)
    End Function

    Private Async Function WriteCountedAsync(buffer As Byte(), offset As Integer, count As Integer, ct As CancellationToken) As Task
        Await _inner.WriteAsync(buffer, offset, count, ct).ConfigureAwait(False)
        Advance(count)
    End Function

    Private Sub Advance(n As Integer)
        If n <= 0 Then Return
        _done += n
        If _report IsNot Nothing Then _report(_done, _total)
    End Sub

    Public Overrides Sub Flush()
        _inner.Flush()
    End Sub

    Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
        Throw New NotSupportedException()
    End Function

    Public Overrides Sub SetLength(value As Long)
        Throw New NotSupportedException()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso _ownsInner Then _inner.Dispose()
        MyBase.Dispose(disposing)
    End Sub

End Class

''' <summary>The received-files folder: a setting, the person's Downloads by default (spec 230 rule 5, default 2).</summary>
Friend NotInheritable Class ClipboardSettings

    Private Sub New()
    End Sub

    Friend Const ValueName As String = "Share_ClipboardReceivedFolder"

    ''' <summary>The folder the person chose, else Downloads.</summary>
    Public Shared Function ReceivedFolder() As String
        Dim chosen As String = StoredFolder()
        ' Fully qualified: "\foo" and "C:foo" are rooted but resolve against the current directory or drive.
        If chosen.Length > 0 AndAlso Path.IsPathFullyQualified(chosen) Then Return chosen
        Return DefaultFolder()
    End Function

    ''' <summary>What the person set, "" when the default applies - the setting's own text, which the settings row shows.</summary>
    Public Shared Function StoredFolder() As String
        Try
            Return GetSetting(App_name, Second_App_Name, ValueName, "").Trim()
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is System.Security.SecurityException
            Return ""
        End Try
    End Function

    ''' <summary>"" means the default (Downloads).</summary>
    Public Shared Sub SaveReceivedFolder(folder As String)
        Try
            SaveSetting(App_name, Second_App_Name, ValueName, If(folder, "").Trim())
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is System.Security.SecurityException OrElse TypeOf ex Is IOException
        End Try
    End Sub

    ''' <summary>The user's Downloads folder as the shell knows it, else "Downloads" under the profile.</summary>
    Public Shared Function DefaultFolder() As String
        Try
            Dim known As String = Nothing
            Dim downloads As New Guid("374DE290-123F-4565-9164-39C4925E467B")
            Dim pathPtr As IntPtr = IntPtr.Zero
            If SHGetKnownFolderPath(downloads, 0UI, IntPtr.Zero, pathPtr) = 0 Then
                Try
                    known = System.Runtime.InteropServices.Marshal.PtrToStringUni(pathPtr)
                Finally
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pathPtr)
                End Try
            End If
            If Not String.IsNullOrWhiteSpace(known) Then Return known
        Catch ex As Exception When TypeOf ex Is System.Runtime.InteropServices.COMException OrElse TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
        End Try
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
    End Function

    <System.Runtime.InteropServices.DllImport("shell32.dll")>
    Private Shared Function SHGetKnownFolderPath(<System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)> rfid As Guid,
                                                 dwFlags As UInteger, hToken As IntPtr, ByRef ppszPath As IntPtr) As Integer
    End Function

End Class
