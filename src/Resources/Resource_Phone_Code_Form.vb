#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Runtime.CompilerServices
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' "Show a code for the phone.." dialog (spec 227 rule 3, T-4, Q227-1).
''' Displays a QR code and URL for the phone to scan.
''' Runs a one-time LAN receive address with a single-use token while the window is open - never
''' before or after (Q227-1's recommendation); when the phone sends its code, the listener takes it,
''' closes the window, and the code goes through the same readers as typed text. The code is a
''' secret holder: it is never logged and never written anywhere.
'''
''' The receive loop is a shared function (<see cref="ReceiveAsync"/>) so a test drives it on a loopback
''' listener without a window; the window only owns the QR, the paste door and the way out.
''' </summary>
Friend NotInheritable Class Resource_Phone_Code_Form
    Inherits Form

    ''' <summary>The head of a request: 8 KiB is far more than a request line and a few headers.</summary>
    Friend Const MaxHeadBytes As Integer = 8 * 1024
    ''' <summary>The body of a request: an FMSSFTP code is a line, a .fmscfg a small document.</summary>
    Friend Const MaxBodyBytes As Integer = 64 * 1024
    ''' <summary>Clients served at once; one more is closed unanswered. A phone needs exactly one.</summary>
    Friend Const MaxConcurrentClients As Integer = 16
    ''' <summary>One deadline for the whole exchange with a client (read, answer): a client that connects and says
    ''' nothing - a browser's speculative second connection, a scanner - costs itself, never the window.</summary>
    Friend Shared ReadOnly ClientDeadline As TimeSpan = TimeSpan.FromSeconds(5)

    Private ReadOnly _qrBox As PictureBox
    Private ReadOnly _codeBox As TextBox
    Private ReadOnly _statusLabel As Label
    Private ReadOnly _copyButton As Button
    Private ReadOnly _pasteButton As Button
    Private ReadOnly _closeButton As Button
    Private ReadOnly _answerRow As DialogButtonRow
    Private _scriptFont As Font
    Private _titleFont As Font

    Private _listener As TcpListener
    Private _listenerCts As CancellationTokenSource
    Private _token As String
    Private _intakeUrl As String
    Private _receivedCode As String = ""
    ''' <summary>0 / 1, taken with Interlocked: the first of the phone and the paste button takes the window.</summary>
    Private _codeTaken As Integer
    ''' <summary>The landing page a browser shows when a person (not the phone) opens the address.</summary>
    Private _landingHtml As String = ""

    Public ReadOnly Property ReceivedCode As String
        Get
            Return _receivedCode
        End Get
    End Property

    Public Sub New()
        Text = Localization.T("Подключение телефона")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ' The authored content ends at 410 (the status line); the bottom row below it is 50 tall (8 + one 32 line + 10) at 96 dpi.
        ClientSize = New Size(420, 460)

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            _scriptFont = New Font(family, Font.Size)
            Font = _scriptFont
        End If
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        _titleFont = New Font(Font.FontFamily, 10.0F, FontStyle.Bold)
        Dim titleLabel As New Label With {
            .Text = Localization.T("Отсканируйте этот QR-код телефоном, чтобы передать настройки папки"),
            .AutoSize = False,
            .Location = New Point(20, 16),
            .Size = New Size(380, 40),
            .TextAlign = ContentAlignment.TopCenter,
            .Font = _titleFont
        }

        _qrBox = New PictureBox With {
            .Location = New Point(85, 65),
            .Size = New Size(250, 250),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BorderStyle = BorderStyle.FixedSingle
        }

        _codeBox = New TextBox With {
            .Location = New Point(20, 330),
            .Size = New Size(275, 26),
            .ReadOnly = True
        }

        _copyButton = New Button With {
            .Text = Localization.T("Копировать"),
            .Location = New Point(303, 328),
            .Size = New Size(97, 28)
        }
        AddHandler _copyButton.Click, AddressOf OnCopyClicked
        ButtonGlyphs.Apply(_copyButton, "action.copy")

        _statusLabel = New Label With {
            .Text = Localization.T("Ожидание подключения телефона.."),
            .AutoSize = False,
            .Location = New Point(20, 370),
            .Size = New Size(380, 40),
            .TextAlign = ContentAlignment.MiddleCenter,
            .ForeColor = SystemColors.GrayText
        }

        _pasteButton = New Button With {
            .Name = "phone_Paste",
            .Text = Localization.T("Вставить из буфера..")
        }
        AddHandler _pasteButton.Click, AddressOf OnPasteClicked

        _closeButton = New Button With {
            .Name = "phone_Close",
            .Text = Localization.T("Закрыть"),
            .DialogResult = DialogResult.Cancel
        }

        ' The way out rides the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4): the paste door is a
        ' command of the window and reads first, Close (the one dismissing answer) after it, widths from the caption and
        ' its glyph. Bind makes Close both Escape's target and, there being no affirmative answer here, the one Enter
        ' selects - it only ends the waiting, the phone's code is never taken by a key.
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "phone_Buttons", .Padding = New Padding(20, 8, 20, 10)}
        _answerRow.Add(_pasteButton, DialogButtonRole.Command, "action.paste")
        _answerRow.Add(_closeButton, DialogButtonRole.Dismissing, "action.cancel")

        Controls.AddRange({titleLabel, _qrBox, _codeBox, _copyButton, _statusLabel, _answerRow})
        _answerRow.Bind(Me)

        SetupIntakeListener()

        ' LAST line of the builder: scaling touches only the children that exist by now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DialogLayout.FitContent(Me)
        DialogLayout.KeepRoomForRow(Me, _answerRow)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then
            _scriptFont?.Dispose()
            _titleFont?.Dispose()
        End If
    End Sub

    Private Sub OnCopyClicked(sender As Object, e As EventArgs)
        If String.IsNullOrEmpty(_intakeUrl) Then Return
        ' CLIPBOARD-GUARD rule 6: the address carries the single-use token that lets whoever holds it hand a code to this window, so
        ' it takes the privacy markers (no Win+V history, no cloud sync) like every other credential copy.
        If Not SecretClipboard.SetSecret(_intakeUrl) Then
            _statusLabel.Text = Localization.T("Буфер обмена занят другой программой")
        End If
    End Sub

    ''' <summary>The same bounded clipboard door as every other intake (text, or a screenshot of the phone's QR),
    ''' checked by the one reader before the window closes - a clipboard that is not a code says so here and the
    ''' window stays open for the phone.</summary>
    Private Sub OnPasteClicked(sender As Object, e As EventArgs)
        Dim qr As QrDecodeResult = QrCodeDecoder.DecodeClipboard()
        If Not qr.Succeeded Then
            _statusLabel.Text = qr.FailureMessage
            Return
        End If
        Dim read As FmsConfigReadResult = PhoneConnectionIntake.ReadText(qr.Text)
        If Not read.Succeeded Then
            _statusLabel.Text = ResourceMessages.ImportFailureText(read)
            Return
        End If
        OnCodeReceived(qr.Text.Trim())
    End Sub

    ''' <summary>A code in hand - from the phone (a pool thread) or the paste button (the UI thread): the first one
    ''' takes the window, a second one is dropped.</summary>
    Private Sub OnCodeReceived(code As String)
        If Interlocked.CompareExchange(_codeTaken, 1, 0) <> 0 Then Return
        _receivedCode = code
        DeliverToUi()
    End Sub

    Private Sub SetupIntakeListener()
        Try
            _token = Guid.NewGuid().ToString("N")
            Dim localIp As String = GetLocalLanIp()
            ' Only the address the QR names is listened on, never every adapter of the machine (a VPN, a
            ' virtual switch); the phone has to reach that very address anyway.
            Dim bindAddress As IPAddress = IPAddress.Any
            Dim parsed As IPAddress = Nothing
            If IPAddress.TryParse(localIp, parsed) Then bindAddress = parsed
            _listener = New TcpListener(bindAddress, 0)
            _listener.Start()
            Dim port As Integer = DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
            _intakeUrl = "http://" & localIp & ":" & port.ToString(Globalization.CultureInfo.InvariantCulture) & "/fms-intake?token=" & _token

            _codeBox.Text = _intakeUrl
            Dim qrBmp As Bitmap = QrCodeRenderer.RenderBitmap(_intakeUrl, 6)
            If qrBmp IsNot Nothing Then
                _qrBox.Image = qrBmp
            End If

            _landingHtml = "<!DOCTYPE html><html><head><meta charset=""utf-8""><title>" &
                WebUtility.HtmlEncode(Localization.T("Подключение телефона")) & "</title></head><body>" &
                "<p>" & WebUtility.HtmlEncode(Localization.T("Эта страница - адрес однократного приёма кода.")) & "</p>" &
                "<p>" & WebUtility.HtmlEncode(Localization.T("Отсканируйте QR-код из приложения Fast Media Sorter на компьютере телефоном.")) & "</p>" &
                "</body></html>"

            _listenerCts = New CancellationTokenSource()
            Dim listener As TcpListener = _listener
            Dim token As String = _token
            Dim landing As String = _landingHtml
            Dim ct As CancellationToken = _listenerCts.Token
            Dim onCode As Action(Of String) = AddressOf OnCodeReceived
            ' The loop reports through onCode and ends with the window; nothing waits for it.
            System.Threading.Tasks.Task.Run(Function() ReceiveAsync(listener, token, landing, onCode, ct))
        Catch ex As Exception
            _statusLabel.Text = Localization.T("Не удалось запустить локальный приём")
        End Try
    End Sub

    ''' <summary>The receive loop: every client is served on its own task under its own deadline, so one that
    ''' stalls never keeps the phone waiting; the first request that carries the token and a code is answered
    ''' "OK" and handed to <paramref name="onCode"/>, then the loop ends. Everything else gets the landing page.
    ''' <paramref name="perClientLimit"/> (a test's shortened one) defaults to <see cref="ClientDeadline"/>.
    ''' (Named apart on purpose: VB is case-insensitive, so a parameter called clientDeadline hides the field.)</summary>
    Friend Shared Async Function ReceiveAsync(listener As TcpListener, token As String, landingHtml As String,
                                              onCode As Action(Of String), ct As CancellationToken,
                                              Optional perClientLimit As TimeSpan = Nothing) As System.Threading.Tasks.Task
        Dim deadline As TimeSpan = If(perClientLimit > TimeSpan.Zero, perClientLimit, ClientDeadline)
        Dim taken As New StrongBox(Of Integer)(0)
        Dim inFlight As New StrongBox(Of Integer)(0)
        While Not ct.IsCancellationRequested AndAlso Volatile.Read(taken.Value) = 0
            Dim client As TcpClient = Nothing
            Try
                client = Await listener.AcceptTcpClientAsync(ct).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Return
            Catch ex As ObjectDisposedException
                Return
            Catch ex As Exception
                ' A reset connection while being accepted is transient; a stopped listener ends the window anyway.
                If ct.IsCancellationRequested Then Return
            End Try
            If client Is Nothing Then
                Try
                    Await System.Threading.Tasks.Task.Delay(50, ct).ConfigureAwait(False)
                Catch ex2 As OperationCanceledException
                    Return
                End Try
                Continue While
            End If

            If Interlocked.Increment(inFlight.Value) > MaxConcurrentClients Then
                Interlocked.Decrement(inFlight.Value)
                client.Dispose()
                Continue While
            End If

            Dim accepted As TcpClient = client
            StartServing(accepted, token, landingHtml, onCode, taken, inFlight, deadline, ct)
        End While
    End Function

    ''' <summary>One client on its own task; the in-flight count is released when it ends, however it ends.</summary>
    Private Shared Sub StartServing(client As TcpClient, token As String, landingHtml As String,
                                    onCode As Action(Of String), taken As StrongBox(Of Integer),
                                    inFlight As StrongBox(Of Integer), deadline As TimeSpan, ct As CancellationToken)
        System.Threading.Tasks.Task.Run(
            Async Function() As System.Threading.Tasks.Task
                Try
                    Await ServeClientAsync(client, token, landingHtml, onCode, taken, deadline, ct).ConfigureAwait(False)
                Finally
                    Interlocked.Decrement(inFlight.Value)
                End Try
            End Function)
    End Sub

    Private Shared Async Function ServeClientAsync(client As TcpClient, token As String, landingHtml As String,
                                                   onCode As Action(Of String), taken As StrongBox(Of Integer),
                                                   deadline As TimeSpan, ct As CancellationToken) As System.Threading.Tasks.Task
        Using client
            Try
                Using limit As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                    limit.CancelAfter(deadline)
                    Using stream As NetworkStream = client.GetStream()
                        Dim request As String = Await ReadRequestAsync(stream, limit.Token).ConfigureAwait(False)
                        If request Is Nothing Then Return
                        Dim code As String = ExtractIntakePayload(request, token)
                        If code IsNot Nothing AndAlso Interlocked.CompareExchange(taken.Value, 1, 0) = 0 Then
                            Try
                                Await WriteResponseAsync(stream, "HTTP/1.1 200 OK", "text/plain", "OK", limit.Token).ConfigureAwait(False)
                            Catch ex As Exception
                                ' The phone may not have heard "OK"; the PC has the code, which is what the window waited for.
                            End Try
                            onCode(code)
                            Return
                        End If
                        ' No code in it (a person opened the address in a browser, or the token is spent):
                        ' a one-screen landing instead of a bare 400, with nothing of the token in it.
                        Await WriteResponseAsync(stream, "HTTP/1.1 200 OK", "text/html; charset=utf-8", landingHtml, limit.Token).ConfigureAwait(False)
                    End Using
                End Using
            Catch ex As Exception
                ' A dropped, stalled or malformed client (or a window that closed under it) costs only itself.
            End Try
        End Using
    End Function

    ''' <summary>The request head plus, when there is one, its whole body. Bounded twice (<see cref="MaxHeadBytes"/>,
    ''' <see cref="MaxBodyBytes"/>); the caller's token carries the deadline. The result is the head, a blank line
    ''' and the body - exactly what <see cref="ExtractIntakePayload"/> reads. The body is counted from the end of
    ''' the head: <c>Content-Length</c> is the length of the body alone, and the bytes of the body that arrived
    ''' with the head are kept.</summary>
    Friend Shared Async Function ReadRequestAsync(stream As Stream, ct As CancellationToken) As System.Threading.Tasks.Task(Of String)
        Dim head(MaxHeadBytes - 1) As Byte
        Dim have As Integer = 0
        Dim bodyStart As Integer = -1
        While have < head.Length
            Dim readCount As Integer = Await stream.ReadAsync(head, have, head.Length - have, ct).ConfigureAwait(False)
            If readCount = 0 Then Exit While
            have += readCount
            bodyStart = FindHeaderEnd(head, have)
            If bodyStart >= 0 Then Exit While
        End While
        If have = 0 Then Return Nothing
        If bodyStart < 0 Then Return Encoding.UTF8.GetString(head, 0, have)

        Dim contentLength As Integer = ParseContentLength(Encoding.UTF8.GetString(head, 0, bodyStart))
        If contentLength <= 0 Then Return Encoding.UTF8.GetString(head, 0, bodyStart)

        Dim whole(bodyStart + Math.Min(contentLength, MaxBodyBytes) - 1) As Byte
        Dim got As Integer = Math.Min(have, whole.Length)
        Array.Copy(head, whole, got)
        While got < whole.Length
            Dim readCount As Integer = Await stream.ReadAsync(whole, got, whole.Length - got, ct).ConfigureAwait(False)
            If readCount = 0 Then Exit While
            got += readCount
        End While
        Return Encoding.UTF8.GetString(whole, 0, got)
    End Function

    ''' <summary>The index just past the blank line that ends the head, or -1.</summary>
    Private Shared Function FindHeaderEnd(buffer As Byte(), length As Integer) As Integer
        For i As Integer = 0 To length - 4
            If buffer(i) = 13 AndAlso buffer(i + 1) = 10 AndAlso buffer(i + 2) = 13 AndAlso buffer(i + 3) = 10 Then Return i + 4
        Next
        Return -1
    End Function

    Private Shared Function ParseContentLength(requestHead As String) As Integer
        For Each line As String In requestHead.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            If line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) Then
                Dim value As String = line.Substring("Content-Length:".Length).Trim()
                Dim length As Integer
                If Integer.TryParse(value, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, length) Then Return length
            End If
        Next
        Return 0
    End Function

    Private Shared Async Function WriteResponseAsync(stream As Stream, status As String, contentType As String,
                                                     body As String, ct As CancellationToken) As System.Threading.Tasks.Task
        Dim bodyBytes As Byte() = Encoding.UTF8.GetBytes(If(body, ""))
        Dim head As String = status & vbCrLf &
            "Content-Type: " & contentType & vbCrLf &
            "Content-Length: " & bodyBytes.Length.ToString(Globalization.CultureInfo.InvariantCulture) & vbCrLf &
            "Connection: close" & vbCrLf & vbCrLf
        Dim headBytes As Byte() = Encoding.UTF8.GetBytes(head)
        Await stream.WriteAsync(headBytes, 0, headBytes.Length, ct).ConfigureAwait(False)
        If bodyBytes.Length > 0 Then
            Await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length, ct).ConfigureAwait(False)
        End If
    End Function

    ''' <summary>The code of a request that carries the expected token - in a POST body or a
    ''' <c>code=</c> query value (the last parameter: an FMSSFTP2 code has its own <c>&amp;</c>, so it is taken
    ''' to the end of the request target, then unescaped) - or Nothing. Nothing else of the request is read.</summary>
    Friend Shared Function ExtractIntakePayload(request As String, expectedToken As String) As String
        If String.IsNullOrEmpty(request) OrElse String.IsNullOrEmpty(expectedToken) Then Return Nothing
        Dim lines As String() = request.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        If lines.Length = 0 Then Return Nothing

        ' "METHOD target HTTP/1.1": the token is a parameter of the target's query, matched whole.
        Dim parts As String() = lines(0).Split(" "c)
        If parts.Length < 2 Then Return Nothing
        Dim target As String = parts(1)
        Dim queryAt As Integer = target.IndexOf("?"c)
        If queryAt < 0 Then Return Nothing
        Dim query As String = target.Substring(queryAt + 1)

        Dim codeAt As Integer = -1
        If query.StartsWith("code=", StringComparison.Ordinal) Then
            codeAt = 0
        Else
            Dim found As Integer = query.IndexOf("&code=", StringComparison.Ordinal)
            If found >= 0 Then codeAt = found + 1
        End If
        Dim beforeCode As String = If(codeAt >= 0, query.Substring(0, codeAt), query)

        Dim tokenSeen As Boolean = False
        For Each pair As String In beforeCode.Split("&"c)
            If pair.StartsWith("token=", StringComparison.Ordinal) AndAlso
               String.Equals(pair.Substring("token=".Length), expectedToken, StringComparison.Ordinal) Then
                tokenSeen = True
            End If
        Next
        If Not tokenSeen Then Return Nothing

        ' A POST body, after the blank line that ends the head.
        Dim emptyLineIndex As Integer = -1
        For i As Integer = 0 To lines.Length - 1
            If String.IsNullOrWhiteSpace(lines(i)) Then
                emptyLineIndex = i
                Exit For
            End If
        Next
        If emptyLineIndex >= 0 AndAlso emptyLineIndex < lines.Length - 1 Then
            Dim body As String = String.Join(vbLf, lines.Skip(emptyLineIndex + 1)).Trim()
            If body.Length > 0 Then Return body
        End If

        ' A GET: code=... in the query.
        If codeAt >= 0 Then
            Dim fromQuery As String = Uri.UnescapeDataString(query.Substring(codeAt + "code=".Length)).Trim()
            If fromQuery.Length > 0 Then Return fromQuery
        End If

        Return Nothing
    End Function

    Private Sub DeliverToUi()
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(New Action(Sub()
                                       If IsDisposed Then Return
                                       _statusLabel.Text = Localization.T("Код получен")
                                       DialogResult = DialogResult.OK
                                       Close()
                                   End Sub))
        Catch ex As Exception
            ' The window closed while the answer travelled; the code is already in ReceivedCode.
        End Try
    End Sub

    Private Shared Function GetLocalLanIp() As String
        Try
            Using socket As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP)
                socket.Connect("8.8.8.8", 65530)
                Dim endPoint As IPEndPoint = TryCast(socket.LocalEndPoint, IPEndPoint)
                If endPoint IsNot Nothing Then Return endPoint.Address.ToString()
            End Using
        Catch
        End Try
        Try
            Dim host As String = Dns.GetHostName()
            For Each ip In Dns.GetHostAddresses(host)
                If ip.AddressFamily = AddressFamily.InterNetwork AndAlso Not IPAddress.IsLoopback(ip) Then
                    Return ip.ToString()
                End If
            Next
        Catch
        End Try
        Return "127.0.0.1"
    End Function

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MyBase.OnFormClosed(e)
        Try
            _listenerCts?.Cancel()
            _listenerCts?.Dispose()
            _listener?.Stop()
        Catch
        End Try
        If _qrBox.Image IsNot Nothing Then
            _qrBox.Image.Dispose()
            _qrBox.Image = Nothing
        End If
    End Sub

End Class
#End If
