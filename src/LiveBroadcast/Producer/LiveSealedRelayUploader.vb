#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks

' Spec 234 T-3, DEVICE-EXCHANGE section 27 item DL: the sealed relay upload of one broadcast. What was the clear upload loop of
' the agent is here, with the records of the contract in place of the bare chunks:
'   - every upload opens with a HEADER chunk (flag 0x02): the key ring, then the media record of the format header (PAT and PMT
'     for MPEG-TS, none for ADTS), so the ring always comes first;
'   - every other chunk is ONE media record sealed under the epoch key, the flag of the chunk part of its associated data;
'     a joinable chunk's media starts where 7.6 asks (an IDR packet / an ADTS frame), the cut is the chunker's and the hub's;
'   - every upload, first or replacement, is a NEW epoch (a new sender from the session);
'   - a replacement is released by the session (record updated first), and the new upload is opened BEFORE the old stream is
'     let go, since the server drops the first upload when a second arrives (item N);
'   - with no sealed server (<see cref="ExchangeSealedBroadcastSession.CanUpload"/> False) NOTHING is uploaded - no clear
'     fallback, the loop waits.
' The upload is opened by the caller's function, so a test drives this with a memory stream and no server. No key, record or media
' byte reaches a log or a message from here.

Friend NotInheritable Class LiveSealedRelayUploader

    Private Sub New()
    End Sub

    ''' <param name="open">Opens one upload with the content type of item DL; Nothing (or a result that is not connected) is "no
    ''' stream now" and the loop tries again after <paramref name="retryDelayMs"/>.</param>
    Public Shared Async Function RunAsync(session As ExchangeSealedBroadcastSession, hub As LiveBroadcastHub, isVideo As Boolean,
                                          open As Func(Of CancellationToken, Task(Of ExchangeOpenResult)),
                                          retryDelayMs As Integer, ct As CancellationToken) As Task
        If session Is Nothing OrElse hub Is Nothing OrElse open Is Nothing Then Return
        Dim previous As ExchangeOpenResult = Nothing
        Try
            While Not ct.IsCancellationRequested
                If Not session.CanUpload Then
                    ' server-no-e2e or not connected: nothing goes through the server, and nothing clear goes in its place
                    DisposeQuietly(previous)
                    previous = Nothing
                    If Not Await PauseAsync(retryDelayMs, ct).ConfigureAwait(False) Then Return
                    Continue While
                End If

                Dim seq As Long = session.ReplaceSeq
                Dim sender As ExchangeRelaySender = Nothing
                Dim current As ExchangeOpenResult = Nothing
                Dim cursor As LiveHubCursor = Nothing
                Dim retry As Boolean = False
                Dim replaced As Boolean = False
                Try
                    sender = session.NewSender()
                    current = Await open.Invoke(ct).ConfigureAwait(False)
                    ' The second upload is open and the server dropped the first (item N): our handle on it can go.
                    DisposeQuietly(previous)
                    previous = Nothing
                    If current Is Nothing OrElse Not current.IsConnected Then
                        retry = True
                    Else
                        cursor = hub.Attach()
                        replaced = Await StreamAsync(current.Stream, sender, session, seq, cursor, isVideo, ct).ConfigureAwait(False)
                    End If
                Catch ex As OperationCanceledException When ct.IsCancellationRequested
                    Return
                Catch ex As Exception
                    ' A lost stream, or a record that did not fit: reconnect within the server's grace, with a new epoch.
                    retry = True
                Finally
                    cursor?.Detach()
                    If sender IsNot Nothing Then sender.Dispose()
                    If replaced Then
                        previous = current
                    Else
                        DisposeQuietly(current)
                    End If
                End Try

                If retry Then
                    If Not Await PauseAsync(retryDelayMs, ct).ConfigureAwait(False) Then Return
                End If
            End While
        Finally
            DisposeQuietly(previous)
        End Try
    End Function

    ''' <summary>Writes one upload until the broadcast ends, the token is cancelled, a replacement is released or the sealed server
    ''' is gone. True when a replacement is wanted (the caller opens the next upload before it lets this one go).</summary>
    Private Shared Async Function StreamAsync(stream As Stream, sender As ExchangeRelaySender, session As ExchangeSealedBroadcastSession,
                                              seq As Long, cursor As LiveHubCursor, isVideo As Boolean, ct As CancellationToken) As Task(Of Boolean)
        Dim chunker As LiveRelayTsChunker = If(isVideo, New LiveRelayTsChunker(), Nothing)
        If Not isVideo Then
            ' ADTS has no format header: the header chunk is the key ring alone.
            Await WriteAsync(stream, ExchangeChunks.Build(ExchangeChunks.FlagHeader, sender.BuildHeaderChunk(Nothing)), ct).ConfigureAwait(False)
        End If

        While Not ct.IsCancellationRequested AndAlso Not cursor.Detached
            If session.ReplaceSeq <> seq OrElse sender.IsExhausted Then Return True
            If Not session.CanUpload Then Return False
            Dim chunk As Byte() = cursor.Pull()
            If chunk Is Nothing Then
                Await Task.Delay(10, ct).ConfigureAwait(False)
                Continue While
            End If

            If isVideo Then
                For Each cut As LiveRelayCut In chunker.Feed(chunk)
                    If cut.Header Then
                        ' PAT and PMT: the first chunk of an MPEG-TS upload, behind the ring.
                        Await WriteAsync(stream, ExchangeChunks.Build(ExchangeChunks.FlagHeader, sender.BuildHeaderChunk(cut.Payload)), ct).ConfigureAwait(False)
                    Else
                        Dim flag As Byte = If(cut.Joinable, ExchangeChunks.FlagJoinable, CByte(0))
                        Await WriteAsync(stream, ExchangeChunks.Build(flag, sender.SealMedia(cut.Payload, flag)), ct).ConfigureAwait(False)
                    End If
                Next
            Else
                Await WriteAsync(stream, ExchangeChunks.Build(ExchangeChunks.FlagJoinable, sender.SealMedia(chunk, ExchangeChunks.FlagJoinable)), ct).ConfigureAwait(False)
            End If
        End While

        ' The hub ended: the zero-length end marker of 7.6 (never on a cancelled token, which only drops the stream).
        If cursor.Detached AndAlso Not ct.IsCancellationRequested Then
            Try
                Await WriteAsync(stream, ExchangeChunks.EndMarker(), ct).ConfigureAwait(False)
            Catch
            End Try
        End If
        Return False
    End Function

    Private Shared Async Function WriteAsync(stream As Stream, bytes As Byte(), ct As CancellationToken) As Task
        Using deadline As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
            deadline.CancelAfter(TimeSpan.FromSeconds(5))
            Await stream.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(False)
        End Using
    End Function

    ''' <summary>False when the token ended the wait.</summary>
    Private Shared Async Function PauseAsync(milliseconds As Integer, ct As CancellationToken) As Task(Of Boolean)
        Try
            Await Task.Delay(Math.Max(1, milliseconds), ct).ConfigureAwait(False)
            Return True
        Catch ex As OperationCanceledException
            Return False
        End Try
    End Function

    Private Shared Sub DisposeQuietly(open As ExchangeOpenResult)
        If open Is Nothing Then Return
        Try
            open.Dispose()
        Catch
        End Try
    End Sub

End Class
#End If
