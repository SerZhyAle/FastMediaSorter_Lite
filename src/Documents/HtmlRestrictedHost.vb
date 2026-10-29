#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

' Local document host shared with 275. No authored script/native bridge, file URLs,
' folder mapping, HTTP listener, automatic installation or browser navigation.
Friend NotInheritable Class HtmlRestrictedHost
    Inherits UserControl
    Private ReadOnly view As New WebView2 With {.Dock = DockStyle.Fill, .AllowExternalDrop = False}
    Private ReadOnly lifetime As New CancellationTokenSource()
    Private ReadOnly origin As New Uri("https://s" & Guid.NewGuid().ToString("N") & ".fms-doc.invalid/")
    Private ReadOnly read As Func(Of String, CancellationToken, Task(Of HtmlReaderResource))
    Private expected As String = ""
    Private activeKey As String = ""
    Private ready As Boolean
    Private pendingRequests As Integer
    Private ReadOnly resourceGate As New SemaphoreSlim(1, 1)
    Private hostEnvironment As CoreWebView2Environment
    Public Event ExitRequested()
    Public Event LinkRequested(key As String, anchor As String)
    Public Event ExternalLinkRequested(uri As Uri)
    Public Event ContentReady()
    Public Event HostFailed()

    Public Sub New(reader As Func(Of String, CancellationToken, Task(Of HtmlReaderResource)))
        read = reader
        Controls.Add(view)
        Dock = DockStyle.Fill
        TabStop = True
        AccessibleName = Localization.T("Полное чтение EPUB")
    End Sub

    Public Async Function InitializeAsync(token As CancellationToken) As Task
        ' The loader folder can be set once per process, so the EPUB reader and the HTML viewer share
        ' HtmlRuntimeService's loader and its owned local profile (specification 275 section 9.3).
        Dim prepared = HtmlRuntimeService.PrepareLoader()
        If prepared <> HtmlOutcome.Ok Then Throw New FileNotFoundException(HtmlViewerText.Outcome(prepared))
        Dim acquired = Await HtmlRuntimeService.AcquireEnvironmentAsync(HtmlRuntimeProfile.Local, token)
        If IsDisposed Then
            ' Disposed while the environment was being created: Dispose had nothing to release yet.
            HtmlRuntimeService.ReleaseController(HtmlRuntimeProfile.Local)
            Throw New OperationCanceledException(token)
        End If
        hostEnvironment = acquired
        token.ThrowIfCancellationRequested()
        view.CreationProperties = New CoreWebView2CreationProperties With {.IsInPrivateModeEnabled = True}
        Await view.EnsureCoreWebView2Async(hostEnvironment).WaitAsync(token)
        token.ThrowIfCancellationRequested()
        Dim core = view.CoreWebView2
        AddHandler view.KeyDown, Sub(sender, e)
            If e.KeyCode <> Keys.Escape Then Return
            e.Handled = True
            e.SuppressKeyPress = True
            BeginInvoke(New Action(Sub()
                If Not IsDisposed Then RaiseEvent ExitRequested()
            End Sub))
        End Sub
        With core.Settings
            .IsScriptEnabled = False
            .AreHostObjectsAllowed = False
            .IsWebMessageEnabled = False
            .AreDefaultContextMenusEnabled = False
            .AreDevToolsEnabled = False
            .IsStatusBarEnabled = False
            .AreDefaultScriptDialogsEnabled = False
            .IsPasswordAutosaveEnabled = False
            .IsGeneralAutofillEnabled = False
            .AreBrowserAcceleratorKeysEnabled = False
            .IsReputationCheckingRequired = False
        End With
        AddHandler core.PermissionRequested, Sub(sender, e) e.State = CoreWebView2PermissionState.Deny
        AddHandler core.NewWindowRequested, Sub(sender, e)
            e.Handled = True
            QueueLink(e.Uri)
        End Sub
        AddHandler core.DownloadStarting, Sub(sender, e) e.Cancel = True
        AddHandler core.BasicAuthenticationRequested, Sub(sender, e) e.Cancel = True
        AddHandler core.ServerCertificateErrorDetected, Sub(sender, e) e.Action = CoreWebView2ServerCertificateErrorAction.Cancel
        AddHandler core.LaunchingExternalUriScheme, Sub(sender, e) e.Cancel = True
        AddHandler core.FrameNavigationStarting, Sub(sender, e) e.Cancel = True
        AddHandler core.NavigationStarting, Sub(sender, e)
            If e.Uri = expected Then
                expected = ""
                Return
            End If
            e.Cancel = True
            QueueLink(e.Uri)
        End Sub
        AddHandler core.ProcessFailed, Sub(sender, e)
            ready = False
            RaiseEvent HostFailed()
        End Sub
        AddHandler core.NavigationCompleted, Sub(sender, e)
            If IsDisposed OrElse lifetime.IsCancellationRequested Then Return
            If e.IsSuccess Then
                RaiseEvent ContentReady()
            ElseIf e.WebErrorStatus <> CoreWebView2WebErrorStatus.OperationCanceled Then
                RaiseEvent HostFailed()
            End If
        End Sub
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All)
        AddHandler core.WebResourceRequested, AddressOf Serve
        ' Required stable Find API: unsupported installed runtimes fail before content loads.
        GC.KeepAlive(core.Find)
        ready = True
    End Function

    Private Sub QueueLink(address As String)
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(address, UriKind.Absolute, uri) Then Return
        Dim key = HtmlReaderContent.ResourceKey(uri, origin)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        BeginInvoke(New Action(Sub()
            If IsDisposed OrElse lifetime.IsCancellationRequested Then Return
            If key IsNot Nothing Then
                RaiseEvent LinkRequested(key, Uri.UnescapeDataString(uri.Fragment.TrimStart("#"c)))
            ElseIf uri.Scheme = "https" OrElse uri.Scheme = "http" Then
                RaiseEvent ExternalLinkRequested(uri)
            End If
        End Sub))
    End Sub

    Private Async Sub Serve(sender As Object, e As CoreWebView2WebResourceRequestedEventArgs)
        Dim deferral = e.GetDeferral()
        Dim token = lifetime.Token
        pendingRequests += 1
        Try
            Dim uri As Uri = Nothing
            Dim key = If(Uri.TryCreate(e.Request.Uri, UriKind.Absolute, uri), HtmlReaderContent.ResourceKey(uri, origin), Nothing)
            Dim resource As HtmlReaderResource = Nothing
            If key IsNot Nothing AndAlso e.Request.Method = "GET" AndAlso pendingRequests <= 128 Then
                Await resourceGate.WaitAsync(token)
                Try
                    resource = Await read(key, token)
                Finally
                    resourceGate.Release()
                End Try
            End If
            If IsDisposed OrElse token.IsCancellationRequested Then Return
            Dim status = If(resource Is Nothing, 403, 200)
            Dim bytes = If(resource?.Bytes, Array.Empty(Of Byte)())
            If bytes.Length > EpubPackage.MaxResourceBytes Then
                bytes = Array.Empty(Of Byte)()
                status = 413
            End If
            Dim stream As New MemoryStream(bytes, writable:=False)
            ' WebView2 owns the response's COM stream reference until it consumes the
            ' response. Do not dispose it here or retain every chapter in a host-side list.
            Dim csp = HtmlReaderContent.Policy
            Dim headers = "Content-Type: " & If(resource?.MimeType, "text/plain") & vbCrLf & "Content-Security-Policy: " & csp & vbCrLf &
                "X-Content-Type-Options: nosniff" & vbCrLf & "Cache-Control: no-store" & vbCrLf & "Referrer-Policy: no-referrer"
            e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(stream, status, If(status = 200, "OK", "Blocked"), headers)
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not IsDisposed AndAlso view.CoreWebView2 IsNot Nothing Then e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(Nothing, 403, "Blocked", "Cache-Control: no-store")
        Finally
            pendingRequests -= 1
            deferral.Complete()
        End Try
    End Sub

    Public Sub Navigate(key As String, Optional anchor As String = "")
        If Not ready Then Return
        Dim url = New Uri(origin, String.Join("/", key.Split("/"c).Select(Function(part) Uri.EscapeDataString(part)))).AbsoluteUri
        If anchor.Length > 0 Then url &= "#" & Uri.EscapeDataString(anchor)
        expected = url
        activeKey = key
        view.CoreWebView2.Navigate(url)
    End Sub

    Public Async Function ReadPositionAsync() As Task(Of DocumentReadingPosition)
        Dim json = Await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({viewport:window.scrollY/Math.max(1,document.documentElement.scrollHeight-innerHeight),horizontal:window.scrollX/Math.max(1,document.documentElement.scrollWidth-innerWidth)})")
        Dim raw = JsonSerializer.Deserialize(Of String)(json)
        Using document = JsonDocument.Parse(raw)
            Return New DocumentReadingPosition With {.Format = "epub", .SpineKey = activeKey, .HasViewport = True,
                .Viewport = document.RootElement.GetProperty("viewport").GetDouble(), .HorizontalViewport = document.RootElement.GetProperty("horizontal").GetDouble()}
        End Using
    End Function

    Public Async Function RestoreAsync(position As DocumentReadingPosition) As Task
        Dim x = position.HorizontalViewport.ToString(Globalization.CultureInfo.InvariantCulture)
        Dim y = position.Viewport.ToString(Globalization.CultureInfo.InvariantCulture)
        Await view.CoreWebView2.ExecuteScriptAsync("window.scrollTo(" & x & "*Math.max(0,document.documentElement.scrollWidth-innerWidth)," & y & "*Math.max(0,document.documentElement.scrollHeight-innerHeight))")
    End Function

    Public Async Function ReadTextAsync(selection As Boolean) As Task(Of String)
        Dim script = If(selection, "String(window.getSelection()).slice(0,250000)", "document.body.innerText.slice(0,250000)")
        Return JsonSerializer.Deserialize(Of String)(Await view.CoreWebView2.ExecuteScriptAsync(script))
    End Function

    Public Async Function FindAsync(query As String) As Task
        Dim options = view.CoreWebView2.Environment.CreateFindOptions()
        options.FindTerm = query
        options.SuppressDefaultFindDialog = True
        options.ShouldHighlightAllMatches = True
        Await view.CoreWebView2.Find.StartAsync(options)
    End Function

    Public Sub FindNext(backwards As Boolean)
        If Not ready Then Return
        If backwards Then view.CoreWebView2.Find.FindPrevious() Else view.CoreWebView2.Find.FindNext()
    End Sub

    Public Async Function ScrollAsync(forward As Boolean) As Task
        Await view.CoreWebView2.ExecuteScriptAsync("window.scrollBy({left:" & If(forward, "1", "-1") & "*innerWidth,top:" & If(forward, "1", "-1") & "*innerHeight*0.85,behavior:'instant'})")
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            lifetime.Cancel()
            ready = False
            view.Dispose()
            ' The shared environment retires its owned folder once no controller of the profile is left.
            If hostEnvironment IsNot Nothing Then
                hostEnvironment = Nothing
                HtmlRuntimeService.ReleaseController(HtmlRuntimeProfile.Local)
            End If
            lifetime.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
#End If
