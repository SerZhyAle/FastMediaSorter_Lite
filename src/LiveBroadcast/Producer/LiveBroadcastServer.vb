#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Sockets
Imports System.Threading

' Ticket 199 sections 3.1, 3.2, 3.4 and 8: the LAN listener. Bound to the ONE chosen address, never to
' every interface; the port is the user's setting with no fallback, so a taken port refuses the start;
' the audio path answers the 2.1 shape byte for byte and the video path the item C shape; the video
' listener exists only while the user ticked "Allow viewers on my network" (off by default, Q199-3) and
' is served under the opaque token segment of item H once it is on.
'
' Threads: one accept thread, one task per connection. Stop() ends every listener cleanly (a final
' chunk where the socket still allows it, a reset where it does not), closes the listener socket and
' leaves no port behind (acceptance 3).

''' <summary>What the server serves and how it answers.</summary>
Friend Class LiveServerConfig
    ''' <summary>The icy-name value: the broadcast title, or the device name when untitled.</summary>
    Public Property IcyName As String = ""
    Public Property IcyGenre As String = "live"
    Public Property AudioBitrateKbps As Integer = LiveProducerContract.DefaultAudioBitrateKbps

    ''' <summary>The audio hub the /live-audio.aac path streams from.</summary>
    Public Property AudioHub As LiveBroadcastHub

    ''' <summary>Section 3.4: a LAN listener for video is off by default. When False the video path
    ''' answers 404 exactly as if it did not exist.</summary>
    Public Property VideoAllowed As Boolean = False

    ''' <summary>The video hub the /live.ts path streams from when video is allowed.</summary>
    Public Property VideoHub As LiveBroadcastHub

    ''' <summary>Item H's opaque path segment in front of /live.ts: "http://&lt;ip&gt;:&lt;port&gt;/&lt;token&gt;/live.ts".
    ''' Empty means the bare path (the microphone's own secret-link-by-obscurity is the port itself, as
    ''' on the phone).</summary>
    Public Property VideoToken As String = ""

    ''' <summary>Optional password protecting the broadcast (spec 199 section 3.4).</summary>
    Public Property Password As String = ""

    ''' <summary>Ticket 263 T-3: the credential pairs of the broadcast (item BF) - any pair of the set opens the endpoint, bound to its
    ''' user. When set it WINS over <see cref="Password"/>: the same object is the source of the relay's verifiers, so a pair revoked
    ''' there is refused here at the next request. A set that was protected stays protected when its pairs are revoked.</summary>
    Public Property Credentials As LiveCredentialPairs
End Class

Friend Enum LiveServerStartResult
    Ok = 0
    ''' <summary>The user's port is taken: the broadcast does not start, no fallback port is tried.</summary>
    PortTaken = 1
    ''' <summary>The bind itself failed (the address disappeared between the pick and the bind).</summary>
    BindFailed = 2
End Enum

''' <summary>The LAN listener of a live broadcast: one server, the audio path always on, the video path
''' behind the allow-viewers setting and the token.</summary>
Friend Class LiveBroadcastServer
    Implements IDisposable

    Private ReadOnly _bindAddress As IPAddress
    Private ReadOnly _port As Integer
    Private ReadOnly _config As LiveServerConfig
    Private _listener As TcpListener
    Private _acceptThread As Thread
    Private ReadOnly _cursors As New Dictionary(Of TcpClient, LiveHubCursor)()
    Private ReadOnly _clients As New HashSet(Of TcpClient)()
    Private ReadOnly _lock As New Object()
    Private _stopping As Boolean
    Private _disposed As Boolean
    Private ReadOnly _digest As New LiveProducerDigest()

    Public Sub New(bindAddress As IPAddress, port As Integer, config As LiveServerConfig)
        _bindAddress = bindAddress
        _port = port
        _config = If(config, New LiveServerConfig())
    End Sub

    ''' <summary>Raised on the accept thread whenever a listener attaches or detaches - the window's
    ''' "how many listeners" line and the tray state read it.</summary>
    Public Event ListenersChanged As EventHandler

    ''' <summary>Starts the listener. Binds the ONE chosen address - never a wildcard - and refuses the
    ''' start when the port is taken: no fallback, ever (2.1: "if it is taken the broadcast does not
    ''' start").</summary>
    Public Function Start() As LiveServerStartResult
        If _bindAddress Is Nothing Then Return LiveServerStartResult.BindFailed
        If _port < LiveProducerSettings.MinPort OrElse _port > LiveProducerSettings.MaxPort Then Return LiveServerStartResult.PortTaken
        Dim listener As TcpListener
        Try
            listener = New TcpListener(New IPEndPoint(_bindAddress, _port))
            listener.Start()
        Catch ex As SocketException
            ' WSAEADDRINUSE (10048) is the taken port of the no-fallback rule; anything else (10049,
            ' the address vanished between the pick and the bind) is a bind failure. Both refuse the
            ' start; neither ever tries another port.
            If ex.ErrorCode = 10048 Then Return LiveServerStartResult.PortTaken
            Return LiveServerStartResult.BindFailed
        Catch
            Return LiveServerStartResult.BindFailed
        End Try
        _listener = listener

        _stopping = False
        _acceptThread = New Thread(AddressOf AcceptLoop) With {.IsBackground = True, .Name = "LiveBroadcastServer.accept"}
        _acceptThread.Start()
        Return LiveServerStartResult.Ok
    End Function

    ''' <summary>The bound end point, for the window's address line.</summary>
    Public ReadOnly Property LocalEndPoint As IPEndPoint
        Get
            If _listener Is Nothing Then Return Nothing
            Return TryCast(_listener.LocalEndpoint, IPEndPoint)
        End Get
    End Property

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _listener IsNot Nothing AndAlso Not _stopping
        End Get
    End Property

    ''' <summary>The number of clients currently streaming (any path).</summary>
    Public ReadOnly Property ListenerCount As Integer
        Get
            SyncLock _lock
                Return _cursors.Count
            End SyncLock
        End Get
    End Property

    Private Sub AcceptLoop()
        Try
            While Not _stopping
                Dim client As TcpClient
                Try
                    client = _listener.AcceptTcpClient()
                Catch ex As Exception
                    Exit While
                End Try
                SyncLock _lock
                    If _stopping Then
                        client.Dispose()
                        Exit While
                    End If
                    _clients.Add(client)
                End SyncLock
                Dim t As New Task(Sub() ServeClient(client), TaskCreationOptions.LongRunning)
                t.Start()
            End While
        Catch
        End Try
    End Sub

    ''' <summary>True when a listener must answer the Digest challenge: a protected set of pairs (even one whose pairs were all
    ''' revoked), or the single legacy <see cref="LiveServerConfig.Password"/>.</summary>
    Private Function IsProtected() As Boolean
        Dim pairs As LiveCredentialPairs = _config.Credentials
        If pairs IsNot Nothing Then Return pairs.IsProtected
        Return Not String.IsNullOrEmpty(_config.Password)
    End Function

    ''' <summary>The password of the user a listener named; Nothing when that user holds no pair.</summary>
    Private Function PasswordFor(user As String) As String
        Dim pairs As LiveCredentialPairs = _config.Credentials
        If pairs IsNot Nothing Then Return pairs.PasswordFor(user)
        Return If(String.Equals(user, LiveCredentialPairs.LinkUser, StringComparison.Ordinal), _config.Password, Nothing)
    End Function

    Private Sub ServeClient(client As TcpClient)
        Dim cursor As LiveHubCursor = Nothing
        Try
            client.ReceiveTimeout = 5000
            client.SendTimeout = LiveProducerContract.ListenerWriteDeadlineMs
            Dim request As LiveProducerWire.LiveWireRequest = LiveProducerWire.ReadRequest(client, 8192, 5000)
            If request Is Nothing Then
                CloseClient(client)
                Return
            End If

            If IsProtected() Then
                Dim source As String = DirectCast(client.Client.RemoteEndPoint, IPEndPoint).Address.ToString()
                If Not _digest.Verify(request, AddressOf PasswordFor, source) Then
                    WriteAll(client, _digest.Challenge())
                    CloseClient(client)
                    Return
                End If
            End If

            If IsAudioPath(request.Path) AndAlso _config.AudioHub IsNot Nothing Then
                cursor = StreamTo(client, request, _config.AudioHub,
                                  LiveProducerWire.AudioResponseHead(_config.IcyName, _config.IcyGenre, _config.AudioBitrateKbps))
            ElseIf _config.VideoAllowed AndAlso _config.VideoHub IsNot Nothing AndAlso IsVideoPath(request.Path) Then
                cursor = StreamTo(client, request, _config.VideoHub, LiveProducerWire.VideoResponseHead())
            Else
                ' A path this server does not serve - including the video path while the LAN listener
                ' is off - answers 404 exactly as if it did not exist (3.4).
                WriteAll(client, LiveProducerWire.SimpleResponse(LiveProducerWire.NotFoundStatusLine))
                CloseClient(client)
                Return
            End If
        Catch
        Finally
            If cursor IsNot Nothing Then cursor.Detach()
            CloseClient(client)
            RaiseListenersChanged()
        End Try
    End Sub

    Private Function StreamTo(client As TcpClient,
                              request As LiveProducerWire.LiveWireRequest,
                              hub As LiveBroadcastHub,
                              head As Byte()) As LiveHubCursor
        ' Attach BEFORE the head is written: the cursor's join point search starts at the hub's
        ' current end, and anything the encoder pushes between the head and the attach would be
        ' invisible to exactly this listener - a gap the next frame would hide in production but a
        ' permanent hole for a listener that joined in that window. Attaching first closes it.
        Dim cursor As LiveHubCursor = hub.Attach()
        SyncLock _lock
            If _stopping Then
                cursor.Detach()
                Return Nothing
            End If
            _cursors(client) = cursor
        End SyncLock
        RaiseListenersChanged()

        ' The response head is the same whether or not the client asked for icy metadata: the shape
        ' never offers it, and a client that asks gets none - expected, not an error (2.1).
        WriteAll(client, head)

        Dim stream As NetworkStream = client.GetStream()
        While Not _stopping AndAlso Not cursor.Detached
            Dim data As Byte() = cursor.Pull()
            If data Is Nothing Then
                Thread.Sleep(10)
                Continue While
            End If
            If cursor.SkippedForward Then
                ' The listener fell behind and the hub skipped it to a joinable cut: the chunked body
                ' simply goes on - a broadcast has no content worth catching up on (contract 5.2).
            End If
            Try
                WriteAll(client, LiveProducerWire.Chunk(data))
            Catch ex As Exception
                Exit While
            End Try
        End While

        Try
            WriteAll(client, LiveProducerWire.FinalChunk())
        Catch
        End Try
        Return cursor
    End Function

    Private Function IsAudioPath(path As String) As Boolean
        Return String.Equals(path, LiveProducerContract.AudioPath, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(path, LiveProducerContract.AudioPathAlias, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function IsVideoPath(path As String) As Boolean
        If String.IsNullOrEmpty(path) Then Return False
        If _config.VideoToken.Length = 0 Then
            Return String.Equals(path, LiveProducerContract.VideoPath, StringComparison.OrdinalIgnoreCase)
        End If
        Return String.Equals(path, "/" & _config.VideoToken & LiveProducerContract.VideoPath, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub WriteAll(client As TcpClient, data As Byte())
        If data Is Nothing OrElse data.Length = 0 Then Return
        client.GetStream().Write(data, 0, data.Length)
    End Sub

    Private Sub RaiseListenersChanged()
        Try
            RaiseEvent ListenersChanged(Me, EventArgs.Empty)
        Catch
        End Try
    End Sub

    ''' <summary>Ends every listener - the final chunk where the socket still allows it, a reset where it
    ''' does not - and closes the listener. Acceptance 3: no port remains.</summary>
    Public Sub [Stop]()
        SyncLock _lock
            _stopping = True
        End SyncLock

        Try
            _listener?.Stop()
        Catch
        End Try

        SyncLock _lock
            Dim snapshot As List(Of TcpClient) = New List(Of TcpClient)(_clients)
            For Each cursor As LiveHubCursor In _cursors.Values
                cursor.Detach()
            Next
            _cursors.Clear()
            _clients.Clear()
            ' Include clients still reading a request or answering authentication.
            For Each c As TcpClient In snapshot
                Try
                    c.Close()
                Catch
                End Try
            Next
        End SyncLock

        If _acceptThread IsNot Nothing Then
            Try
                _acceptThread.Join(1000)
            Catch
            End Try
            _acceptThread = Nothing
        End If
    End Sub

    Private Sub CloseClient(client As TcpClient)
        SyncLock _lock
            Dim cursor As LiveHubCursor = Nothing
            If _cursors.TryGetValue(client, cursor) Then cursor.Detach()
            _cursors.Remove(client)
            _clients.Remove(client)
        End SyncLock
        Try
            client.Close()
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        [Stop]()
    End Sub

End Class
#End If
