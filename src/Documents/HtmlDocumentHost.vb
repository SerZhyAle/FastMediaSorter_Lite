#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

' Host commands reached through the Runtime's accelerator path while the document has focus.
Friend Enum HtmlHostCommand
    FindStart
    FindNext
    FindPrevious
    LeaveDocument
    ChromeKeyboard
    Help
    Print
    ZoomIn
    ZoomOut
    ZoomReset
    Back
    Forward
    Reload
End Enum

' Reader appearance as validated values; passed to fixed host code as JSON, never as CSS text.
Friend NotInheritable Class HtmlAppearance
    Public Property Reader As Boolean
    Public Property Theme As String = "light"
    Public Property FontFamily As String = "authored"
    Public Property FontSize As Integer = 18
    Public Property LineHeight As Double = 1.6
    Public Property ColumnWidth As Integer = 80
    Public Property Margin As Integer = 16
    Public Property Wrap As Boolean
End Class

Friend NotInheritable Class HtmlPosition
    Public Property PageKey As String = ""
    Public Property Anchor As String = ""
    Public Property Ratio As Double
End Class

' Specification 275 sections 9.3, 10.2-10.4 and 11: the WinForms WebView2 surface. Every request
' is answered by the host (no listener, no folder mapping, no network fallthrough); page scripts,
' host objects, web messages, devtools and browser accelerators are off.
Friend NotInheritable Class HtmlDocumentHost
    Inherits UserControl

    Private Const BlockedStatus As Integer = 403
    Private ReadOnly view As New WebView2 With {.Dock = DockStyle.Fill, .AllowExternalDrop = False}
    Private ReadOnly lifetime As New CancellationTokenSource()
    Private ReadOnly backStack As New List(Of String)()
    Private ReadOnly forwardStack As New List(Of String)()
    Private ReadOnly approvedOrigins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly insecureOrigins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly blockedOrigins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private documentSource As IHtmlDocumentSource
    Private profile As HtmlRuntimeProfile
    Private environment As CoreWebView2Environment
    Private acquired As Boolean
    Private origin As Uri
    Private session As HtmlSessionToken
    ' Local: logical key; online: absolute URL without fragment.
    Private currentPageKey As String = ""
    Private expectedNavigation As String
    Private allowedRedirect As String
    Private pagePending As Byte()
    Private pendingLocation As String = ""
    Private pageResult As HtmlSanitizeResult
    Private navigationDone As TaskCompletionSource(Of HtmlOutcome)
    Private fetcher As HtmlNetworkFetcher
    Private fetchBudget As HtmlFetchBudget
    Private findActive As Boolean
    Private zoomGuard As Boolean
    Private blockedCount As Integer
    Private appearanceValue As New HtmlAppearance()
    Private disposedValue As Boolean

    Public Event ContentReady()
    Public Event Failed(outcome As HtmlOutcome)
    Public Event CommandRequested(command As HtmlHostCommand)
    Public Event ExternalLinkRequested(url As String, isMail As Boolean)
    Public Event TypedOpenRequested(localPath As String)
    Public Event RedirectApprovalRequested(target As Uri)
    Public Event FindChanged(activeIndex As Integer, count As Integer)
    Public Event ZoomChanged(percent As Integer)
    Public Event StateChanged()

    Public Sub New()
        Controls.Add(view)
        Dock = DockStyle.Fill
        TabStop = True
        AccessibleName = Localization.T("HTML-документ")
        AccessibleRole = AccessibleRole.Document
    End Sub

    Public ReadOnly Property Token As HtmlSessionToken
        Get
            Return session
        End Get
    End Property

    Public ReadOnly Property Title As String
        Get
            Dim live = If(view.CoreWebView2 IsNot Nothing, view.CoreWebView2.DocumentTitle, "")
            If Not String.IsNullOrWhiteSpace(pageResult?.Title) Then Return pageResult.Title
            Return If(live, "")
        End Get
    End Property

    Public ReadOnly Property Direction As String
        Get
            Return If(pageResult?.Direction, "")
        End Get
    End Property

    Public ReadOnly Property CurrentLocation As String
        Get
            Return currentPageKey
        End Get
    End Property

    Public ReadOnly Property BlockedResources As Integer
        Get
            Return blockedCount + If(pageResult Is Nothing, 0, pageResult.BlockedResources)
        End Get
    End Property

    ' Distinct origins a page tried to load resources from; shown before any approval.
    Public ReadOnly Property BlockedOriginList As IReadOnlyList(Of String)
        Get
            Dim all As New HashSet(Of String)(blockedOrigins, StringComparer.OrdinalIgnoreCase)
            If pageResult?.ExternalOrigins IsNot Nothing Then all.UnionWith(pageResult.ExternalOrigins)
            all.ExceptWith(approvedOrigins)
            Return all.OrderBy(Function(o) o, StringComparer.OrdinalIgnoreCase).ToList()
        End Get
    End Property

    Public ReadOnly Property CanGoBack As Boolean
        Get
            Return backStack.Count > 0
        End Get
    End Property

    Public ReadOnly Property CanGoForward As Boolean
        Get
            Return forwardStack.Count > 0
        End Get
    End Property

    Public ReadOnly Property IsOnline As Boolean
        Get
            Return profile = HtmlRuntimeProfile.Online
        End Get
    End Property

    Public ReadOnly Property Source As IHtmlDocumentSource
        Get
            Return documentSource
        End Get
    End Property

    Public ReadOnly Property PageOrigin As Uri
        Get
            Return origin
        End Get
    End Property

    Public ReadOnly Property ZoomPercent As Integer
        Get
            Return CInt(Math.Round(view.ZoomFactor * 100))
        End Get
    End Property

    ' The document (or a child HWND of the controller) holds the keyboard focus.
    Public ReadOnly Property DocumentHasFocus As Boolean
        Get
            Return Not disposedValue AndAlso IsHandleCreated AndAlso view.IsHandleCreated AndAlso view.ContainsFocus
        End Get
    End Property

    Private ReadOnly Property Alive As Boolean
        Get
            Return Not disposedValue AndAlso Not lifetime.IsCancellationRequested AndAlso Not IsDisposed
        End Get
    End Property

    Private Function Current(candidate As HtmlSessionToken) As Boolean
        Return Alive AndAlso session IsNot Nothing AndAlso session.Matches(candidate)
    End Function

    ' Local document: the source supplies bytes under its declared root.
    Public Function OpenLocalAsync(document As IHtmlDocumentSource, mediaGeneration As Integer, zoomPercent As Integer) As Task(Of HtmlOutcome)
        documentSource = document
        profile = HtmlRuntimeProfile.Local
        origin = HtmlDocumentPolicy.NewSessionOrigin()
        Return OpenCoreAsync(mediaGeneration, document.Request.EntryResourceKey, zoomPercent)
    End Function

    ' Explicit online page: the real origin stays in the Runtime, every request goes through the
    ' host fetcher. insecureAllowed carries the per-origin http acknowledgement.
    Public Function OpenOnlineAsync(document As IHtmlDocumentSource, page As Uri, insecureAllowed As Boolean, mediaGeneration As Integer, zoomPercent As Integer) As Task(Of HtmlOutcome)
        documentSource = document
        profile = HtmlRuntimeProfile.Online
        origin = New Uri(page.GetLeftPart(UriPartial.Authority) & "/")
        approvedOrigins.Add(HtmlAddressPolicy.OriginOf(page))
        If insecureAllowed Then insecureOrigins.Add(HtmlAddressPolicy.OriginOf(page))
        Return OpenCoreAsync(mediaGeneration, page.GetLeftPart(UriPartial.Query), zoomPercent)
    End Function

    Private Async Function OpenCoreAsync(mediaGeneration As Integer, entry As String, zoomPercent As Integer) As Task(Of HtmlOutcome)
        session = New HtmlSessionToken(mediaGeneration, Guid.NewGuid().ToString("N"), 0)
        Dim mine = session
        If documentSource.Request.IsSecure Then Return HtmlOutcome.SecureRefused
        Dim availability = HtmlRuntimeService.Detect()
        If availability.Status <> HtmlOutcome.Ok Then Return availability.Status
        Try
            environment = Await HtmlRuntimeService.AcquireEnvironmentAsync(profile, lifetime.Token)
            If disposedValue OrElse IsDisposed Then
                ' Disposed while the environment was being created: Dispose had nothing to release yet.
                HtmlRuntimeService.ReleaseController(profile)
                Return HtmlOutcome.Cancelled
            End If
            acquired = True
            If Not Current(mine) Then Return HtmlOutcome.Cancelled
            Dim controllerOptions = environment.CreateCoreWebView2ControllerOptions()
            controllerOptions.IsInPrivateModeEnabled = True
            Await view.EnsureCoreWebView2Async(environment, controllerOptions).WaitAsync(lifetime.Token)
            If Not Current(mine) Then Return HtmlOutcome.Cancelled
            Dim probe = ConfigureCore(view.CoreWebView2)
            If probe <> HtmlOutcome.Ok Then Return probe
            view.ZoomFactor = Math.Max(HtmlZoomMinFactor, Math.Min(HtmlZoomMaxFactor, zoomPercent / 100.0))
            Return Await NavigateCoreAsync(entry, "", False, mine, firstNavigation:=True)
        Catch ex As OperationCanceledException
            Return HtmlOutcome.Cancelled
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            Return HtmlRuntimeService.OutcomeOf(ex)
        End Try
    End Function

    Private Const HtmlZoomMinFactor As Double = 0.25
    Private Const HtmlZoomMaxFactor As Double = 5.0

    ' Section 9.3 step 5: settings, denials and request filters before the first navigation.
    Private Function ConfigureCore(core As CoreWebView2) As HtmlOutcome
        With core.Settings
            .IsScriptEnabled = False
            .AreHostObjectsAllowed = False
            .IsWebMessageEnabled = False
            .AreDefaultScriptDialogsEnabled = False
            .AreDevToolsEnabled = False
            ' The native menu stays on only to be reduced to copy/select in ContextMenuRequested.
            .AreDefaultContextMenusEnabled = True
            .IsStatusBarEnabled = False
            .IsPasswordAutosaveEnabled = False
            .IsGeneralAutofillEnabled = False
            .AreBrowserAcceleratorKeysEnabled = False
            .IsBuiltInErrorPageEnabled = False
            .IsSwipeNavigationEnabled = False
            .IsZoomControlEnabled = True
            ' SmartScreen is UDF-wide: off for synthetic local origins, on for real online pages.
            .IsReputationCheckingRequired = profile = HtmlRuntimeProfile.Online
        End With
        AddHandler core.PermissionRequested, Sub(sender, e) e.State = CoreWebView2PermissionState.Deny
        AddHandler core.NewWindowRequested, AddressOf OnNewWindowRequested
        AddHandler core.DownloadStarting, Sub(sender, e)
                                              e.Cancel = True
                                              e.Handled = True
                                          End Sub
        AddHandler core.BasicAuthenticationRequested, Sub(sender, e) e.Cancel = True
        AddHandler core.ClientCertificateRequested, Sub(sender, e)
                                                        e.Cancel = True
                                                        e.Handled = True
                                                    End Sub
        ' An invalid certificate is never overridden.
        AddHandler core.ServerCertificateErrorDetected, Sub(sender, e) e.Action = CoreWebView2ServerCertificateErrorAction.Cancel
        AddHandler core.LaunchingExternalUriScheme, Sub(sender, e) e.Cancel = True
        AddHandler core.FrameNavigationStarting, Sub(sender, e) e.Cancel = True
        AddHandler core.ScreenCaptureStarting, Sub(sender, e)
                                                   e.Cancel = True
                                                   e.Handled = True
                                               End Sub
        AddHandler core.SaveAsUIShowing, Sub(sender, e) e.Cancel = True
        AddHandler core.NotificationReceived, Sub(sender, e) e.Handled = True
        AddHandler core.ContextMenuRequested, AddressOf OnContextMenuRequested
        AddHandler core.NavigationStarting, AddressOf OnNavigationStarting
        AddHandler core.NavigationCompleted, AddressOf OnNavigationCompleted
        AddHandler core.DocumentTitleChanged, Sub(sender, e) Post(Sub() RaiseEvent StateChanged())
        AddHandler core.ProcessFailed, AddressOf OnProcessFailed
        AddHandler view.ZoomFactorChanged, AddressOf OnZoomFactorChanged
        AddHandler view.KeyDown, AddressOf OnAcceleratorKey
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All)
        AddHandler core.WebResourceRequested, AddressOf OnWebResourceRequested
        ' Required APIs: an installed Runtime without them fails before any content loads.
        Try
            AddHandler core.Find.MatchCountChanged, AddressOf OnFindCountChanged
            AddHandler core.Find.ActiveMatchIndexChanged, AddressOf OnFindCountChanged
            GC.KeepAlive(environment.CreateFindOptions())
        Catch ex As Exception When TypeOf ex Is InvalidCastException OrElse TypeOf ex Is NotImplementedException OrElse TypeOf ex Is Runtime.InteropServices.COMException
            Return HtmlOutcome.RequiredApiUnavailable
        End Try
        Return HtmlOutcome.Ok
    End Function

    Private Function UrlOf(target As String, fragment As String) As String
        Dim url = If(profile = HtmlRuntimeProfile.Online, target, HtmlDocumentPolicy.EntryUrl(origin, target))
        If Not String.IsNullOrEmpty(fragment) Then url &= "#" & fragment
        Return url
    End Function

    ' Reads and sanitizes the target page off the UI thread, then lets the Runtime request it.
    Private Async Function NavigateCoreAsync(target As String, fragment As String, pushHistory As Boolean, expectedToken As HtmlSessionToken, Optional firstNavigation As Boolean = False) As Task(Of HtmlOutcome)
        If Not Current(expectedToken) Then Return HtmlOutcome.Cancelled
        session = session.NextNavigation()
        Dim mine = session
        StopFind()
        Dim prepared = Await PreparePageAsync(target, mine)
        If Not Current(mine) Then Return HtmlOutcome.Cancelled
        If prepared.Status <> HtmlOutcome.Ok Then Return prepared.Status
        If pushHistory AndAlso currentPageKey.Length > 0 Then
            backStack.Add(currentPageKey)
            If backStack.Count > HtmlDocumentPolicy.MaxNavigationEntries Then backStack.RemoveAt(0)
            forwardStack.Clear()
        End If
        ' Online: a followed same-origin redirect lands on its final URL so relative assets resolve.
        Dim landed = If(String.IsNullOrEmpty(prepared.Location), target, prepared.Location)
        pageResult = prepared.Result
        pagePending = prepared.Result.Bytes
        pendingLocation = landed
        currentPageKey = landed
        blockedCount = 0
        Dim url = UrlOf(landed, fragment)
        expectedNavigation = url
        Dim waiter As New TaskCompletionSource(Of HtmlOutcome)(TaskCreationOptions.RunContinuationsAsynchronously)
        navigationDone = waiter
        view.CoreWebView2.Navigate(url)
        RaiseEvent StateChanged()
        If Not firstNavigation Then Return HtmlOutcome.Ok
        Dim finished = Await Task.WhenAny(waiter.Task, Task.Delay(HtmlDocumentPolicy.FirstNavigationDeadlineMs, lifetime.Token)).ConfigureAwait(True)
        If Not Current(mine) Then Return HtmlOutcome.Cancelled
        If finished IsNot waiter.Task Then Return HtmlOutcome.TimedOut
        Return waiter.Task.Result
    End Function

    Private NotInheritable Class PreparedPage
        Public Property Status As HtmlOutcome
        Public Property Result As HtmlSanitizeResult
        Public Property Location As String = ""
    End Class

    Private Async Function PreparePageAsync(target As String, mine As HtmlSessionToken) As Task(Of PreparedPage)
        Dim token = lifetime.Token
        Dim raw As Byte()
        Dim charset = ""
        Dim pageOrigin = origin
        Dim entryKey = target
        Dim landed = ""
        If profile = HtmlRuntimeProfile.Online Then
            Dim fetched = Await FetchAsync(New Uri(target), HtmlRequestContext.Document, token)
            If fetched.Status = HtmlOutcome.RedirectNeedsApproval AndAlso fetched.RedirectTarget IsNot Nothing Then
                Dim redirect = fetched.RedirectTarget
                Post(Sub() RaiseEvent RedirectApprovalRequested(redirect))
            End If
            If fetched.Status <> HtmlOutcome.Ok Then Return New PreparedPage With {.Status = fetched.Status}
            raw = fetched.Bytes
            charset = If(fetched.Charset, "")
            Dim final = If(fetched.FinalUrl, New Uri(target))
            pageOrigin = New Uri(final.GetLeftPart(UriPartial.Authority) & "/")
            entryKey = final.AbsolutePath.TrimStart("/"c)
            landed = final.GetLeftPart(UriPartial.Query)
        ElseIf String.Equals(target, documentSource.Request.EntryResourceKey, StringComparison.Ordinal) Then
            Dim entry = Await documentSource.ReadEntryAsync(token)
            If Not entry.IsOk Then Return New PreparedPage With {.Status = entry.Status}
            raw = entry.Bytes
        Else
            Dim resolved = documentSource.ResolveKey(target)
            If Not resolved.IsAllowed Then Return New PreparedPage With {.Status = resolved.Status}
            Dim page = Await documentSource.ReadResourceAsync(resolved.Key, token)
            If Not page.IsOk Then Return New PreparedPage With {.Status = page.Status}
            raw = page.Bytes
        End If
        Dim options As New HtmlSanitizeOptions With {.Origin = pageOrigin, .EntryKey = entryKey, .TransportCharset = charset,
            .Profile = If(profile = HtmlRuntimeProfile.Online, HtmlSourceKind.OnlinePage, documentSource.Request.SourceKind),
            .ApprovedOrigins = New HashSet(Of String)(approvedOrigins, StringComparer.OrdinalIgnoreCase)}
        Dim sanitized = Await Task.Run(Function() HtmlDocumentSanitizer.Sanitize(raw, options, token), token)
        Return New PreparedPage With {.Status = sanitized.Status, .Result = sanitized, .Location = landed}
    End Function

    Private Async Function FetchAsync(url As Uri, context As HtmlRequestContext, token As CancellationToken) As Task(Of HtmlFetchResult)
        If fetcher Is Nothing Then fetcher = New HtmlNetworkFetcher(New SystemHtmlDnsResolver())
        If fetchBudget Is Nothing OrElse context = HtmlRequestContext.Document Then fetchBudget = New HtmlFetchBudget()
        Dim request As New HtmlFetchRequest With {.Url = url, .Context = context, .Method = "GET",
            .ApprovedOrigins = New HashSet(Of String)(approvedOrigins, StringComparer.OrdinalIgnoreCase),
            .InsecureOrigins = New HashSet(Of String)(insecureOrigins, StringComparer.OrdinalIgnoreCase), .Budget = fetchBudget}
        Return Await fetcher.FetchAsync(request, token)
    End Function

    Private Shared Function ContextOf(context As CoreWebView2WebResourceContext) As HtmlRequestContext
        Select Case context
            Case CoreWebView2WebResourceContext.Document : Return HtmlRequestContext.Document
            Case CoreWebView2WebResourceContext.Stylesheet : Return HtmlRequestContext.Stylesheet
            Case CoreWebView2WebResourceContext.Image : Return HtmlRequestContext.Image
            Case CoreWebView2WebResourceContext.Font : Return HtmlRequestContext.Font
            Case Else : Return HtmlRequestContext.Other
        End Select
    End Function

    Private Shared Function KindFor(context As HtmlRequestContext) As HtmlResourceKind
        Select Case context
            Case HtmlRequestContext.Document : Return HtmlResourceKind.Document
            Case HtmlRequestContext.Stylesheet : Return HtmlResourceKind.Stylesheet
            Case HtmlRequestContext.Image : Return HtmlResourceKind.Image
            Case HtmlRequestContext.Font : Return HtmlResourceKind.Font
            Case Else : Return HtmlResourceKind.Other
        End Select
    End Function

    ' Section 10.3 steps 1-7: every request gets a host response, completed in Finally.
    Private Async Sub OnWebResourceRequested(sender As Object, e As CoreWebView2WebResourceRequestedEventArgs)
        Dim deferral = e.GetDeferral()
        Dim mine = session
        Dim core = view.CoreWebView2
        Dim response As CoreWebView2WebResourceResponse = Nothing
        Try
            Dim context = ContextOf(e.ResourceContext)
            Dim requestUri = e.Request.Uri
            Dim method = e.Request.Method
            Dim result As HtmlResourceResult
            If e.RequestedSourceKind <> CoreWebView2WebResourceRequestSourceKinds.Document Then
                result = HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
            ElseIf profile = HtmlRuntimeProfile.Online Then
                result = Await ServeOnlineAsync(requestUri, method, context)
            Else
                result = Await ServeLocalAsync(requestUri, method, context)
            End If
            If Not Current(mine) OrElse core Is Nothing Then
                response = Respond(core, Nothing, 410, "Gone", "Cache-Control: no-store")
            ElseIf result.IsOk AndAlso result.Bytes Is Nothing Then
                ' Explicit empty answer (favicon): nothing falls through to DNS or the network.
                response = Respond(core, Nothing, 204, "No Content", "Cache-Control: no-store")
            ElseIf result.IsOk Then
                Dim csp = If(result.Kind = HtmlResourceKind.Document, ContentPolicy(), Nothing)
                Dim headers = HtmlDocumentPolicy.ResponseHeaders(result.MimeType, result.Kind = HtmlResourceKind.Document, csp)
                Dim body = If(String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase), Array.Empty(Of Byte)(), result.Bytes)
                response = Respond(core, body, 200, "OK", headers)
            Else
                response = Respond(core, Nothing, BlockedStatus, "Blocked", "Cache-Control: no-store" & vbCrLf & "X-Content-Type-Options: nosniff")
            End If
        Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            Try
                If core IsNot Nothing AndAlso Not disposedValue Then response = Respond(core, Nothing, BlockedStatus, "Blocked", "Cache-Control: no-store")
            Catch inner As Exception When Not TypeOf inner Is OutOfMemoryException
            End Try
        Finally
            Try
                If response IsNot Nothing Then e.Response = response
            Catch ex As Exception When Not TypeOf ex Is OutOfMemoryException
            End Try
            deferral.Complete()
        End Try
    End Sub

    Private Shared Function Respond(core As CoreWebView2, body As Byte(), status As Integer, reason As String, headers As String) As CoreWebView2WebResourceResponse
        If core Is Nothing Then Return Nothing
        ' The Runtime owns the stream's COM reference until it consumes the response.
        Dim stream = If(body Is Nothing OrElse body.Length = 0, Nothing, New MemoryStream(body, writable:=False))
        Return core.Environment.CreateWebResourceResponse(stream, status, reason, headers)
    End Function

    Private Async Function ServeLocalAsync(requestUri As String, method As String, context As HtmlRequestContext) As Task(Of HtmlResourceResult)
        Dim parsed As Uri = Nothing
        If Uri.TryCreate(requestUri, UriKind.Absolute, parsed) AndAlso Not HtmlDocumentPolicy.IsSessionOrigin(parsed, origin) Then
            ' Another origin: only an approved remote resource of an allowed passive kind.
            Return Await ServeRemoteAsync(parsed, method, context)
        End If
        Dim key = HtmlDocumentPolicy.ParseRequestKey(requestUri, origin, method)
        If Not key.IsAllowed Then
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(key.Status)
        End If
        If context = HtmlRequestContext.Document Then
            If pagePending IsNot Nothing AndAlso String.Equals(key.Key, pendingLocation, StringComparison.Ordinal) Then
                Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Kind = HtmlResourceKind.Document, .MimeType = "text/html; charset=utf-8", .Bytes = pagePending}
            End If
            ' A document request the host did not prepare (an unowned navigation) is refused.
            Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
        End If
        Dim resolved = documentSource.ResolveKey(key.Key)
        If Not resolved.IsAllowed Then
            If IsFavicon(key.Key) Then Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Bytes = Nothing}
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(resolved.Status)
        End If
        Dim kind = documentSource.KindOf(resolved.Key)
        If Not HtmlDocumentPolicy.IsAllowedInContext(kind, context) Then
            If IsFavicon(key.Key) Then Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Bytes = Nothing}
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
        End If
        Dim read = Await documentSource.ReadResourceAsync(resolved.Key, lifetime.Token)
        If Not read.IsOk Then
            If IsFavicon(key.Key) Then Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Bytes = Nothing}
            CountBlocked(Nothing)
            Return read
        End If
        If read.Bytes.LongLength > HtmlDocumentPolicy.MaxBytesFor(kind) Then
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(HtmlOutcome.LimitExceeded)
        End If
        read.Kind = kind
        read.MimeType = ServedMime(kind, read.MimeType, resolved.Key)
        Return read
    End Function

    ' A package's declared media type is kept only when it fits the kind; otherwise the extension decides.
    Private Shared Function ServedMime(kind As HtmlResourceKind, declared As String, key As String) As String
        Dim mime = If(declared, "").ToLowerInvariant()
        Dim fits = (kind = HtmlResourceKind.Stylesheet AndAlso mime.StartsWith("text/css", StringComparison.Ordinal)) OrElse
            (kind = HtmlResourceKind.Image AndAlso mime.StartsWith("image/", StringComparison.Ordinal)) OrElse
            (kind = HtmlResourceKind.Font AndAlso (mime.StartsWith("font/", StringComparison.Ordinal) OrElse mime.StartsWith("application/font", StringComparison.Ordinal) OrElse
                mime.StartsWith("application/x-font", StringComparison.Ordinal) OrElse mime = "application/vnd.ms-opentype"))
        Return If(fits, declared, HtmlDocumentPolicy.MimeTypeOf(key))
    End Function

    Private Shared Function IsFavicon(key As String) As Boolean
        Return String.Equals(key, "favicon.ico", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Async Function ServeRemoteAsync(target As Uri, method As String, context As HtmlRequestContext) As Task(Of HtmlResourceResult)
        Dim targetOrigin = If(target.Scheme = Uri.UriSchemeHttp OrElse target.Scheme = Uri.UriSchemeHttps, HtmlAddressPolicy.OriginOf(target), "")
        If targetOrigin.Length = 0 OrElse Not approvedOrigins.Contains(targetOrigin) OrElse context = HtmlRequestContext.Document OrElse context = HtmlRequestContext.Other Then
            CountBlocked(targetOrigin)
            Return HtmlResourceResult.Refused(HtmlOutcome.NetworkBlocked)
        End If
        Dim fetched = Await FetchAsync(target, context, lifetime.Token)
        If fetched.Status <> HtmlOutcome.Ok Then
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(fetched.Status)
        End If
        Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Kind = KindFor(context), .MimeType = fetched.MimeType,
            .Bytes = If(String.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase), Array.Empty(Of Byte)(), fetched.Bytes)}
    End Function

    Private Async Function ServeOnlineAsync(requestUri As String, method As String, context As HtmlRequestContext) As Task(Of HtmlResourceResult)
        Dim target As Uri = Nothing
        If Not Uri.TryCreate(requestUri, UriKind.Absolute, target) OrElse (target.Scheme <> Uri.UriSchemeHttps AndAlso target.Scheme <> Uri.UriSchemeHttp) Then
            CountBlocked(Nothing)
            Return HtmlResourceResult.Refused(HtmlOutcome.NetworkBlocked)
        End If
        If context = HtmlRequestContext.Document Then
            Dim withoutFragment = target.GetLeftPart(UriPartial.Query)
            If pagePending IsNot Nothing AndAlso String.Equals(withoutFragment, pendingLocation, StringComparison.Ordinal) Then
                Return New HtmlResourceResult With {.Status = HtmlOutcome.Ok, .Kind = HtmlResourceKind.Document, .MimeType = "text/html; charset=utf-8", .Bytes = pagePending}
            End If
            Return HtmlResourceResult.Refused(HtmlOutcome.UnsupportedResource)
        End If
        Return Await ServeRemoteAsync(target, method, context)
    End Function

    Private Sub CountBlocked(blockedOrigin As String)
        If Not String.IsNullOrEmpty(blockedOrigin) Then blockedOrigins.Add(blockedOrigin)
        blockedCount += 1
        Post(Sub() RaiseEvent StateChanged())
    End Sub

    ' Section 10.4 host policy; approved origins only widen passive style/image/font sources.
    Private Function ContentPolicy() As String
        If approvedOrigins.Count = 0 Then Return HtmlDocumentPolicy.LocalContentSecurityPolicy()
        Dim extra = " " & String.Join(" ", approvedOrigins.Where(Function(o) o.IndexOfAny({" "c, ";"c, ","c, "'"c, ChrW(13), ChrW(10)}) < 0).OrderBy(Function(o) o, StringComparer.Ordinal))
        Return "default-src 'none'; script-src 'none'; connect-src 'none'; style-src 'self' 'unsafe-inline'" & extra &
            "; img-src 'self' data:" & extra & "; font-src 'self'" & extra &
            "; frame-src 'none'; child-src 'none'; worker-src 'none'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'; media-src 'none'"
    End Function

    Private Sub OnNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        If expectedNavigation IsNot Nothing AndAlso String.Equals(e.Uri, expectedNavigation, StringComparison.Ordinal) Then
            expectedNavigation = Nothing
            Return
        End If
        If allowedRedirect IsNot Nothing AndAlso e.IsRedirected AndAlso String.Equals(e.Uri, allowedRedirect, StringComparison.Ordinal) Then
            allowedRedirect = Nothing
            Return
        End If
        e.Cancel = True
        QueueNavigation(e.Uri, e.IsUserInitiated AndAlso Not e.IsRedirected)
    End Sub

    Private Sub OnNewWindowRequested(sender As Object, e As CoreWebView2NewWindowRequestedEventArgs)
        e.Handled = True
        QueueNavigation(e.Uri, e.IsUserInitiated)
    End Sub

    ' Cancel first, capture immutable arguments, decide outside the callback.
    Private Sub QueueNavigation(target As String, userInitiated As Boolean)
        If Not userInitiated OrElse Not Alive Then Return
        Dim mine = session
        Post(Sub() HandleNavigation(target, mine))
    End Sub

    Private Sub HandleNavigation(target As String, mine As HtmlSessionToken)
        If Not Alive OrElse session Is Nothing OrElse Not session.SameSession(mine) Then Return
        If profile = HtmlRuntimeProfile.Online Then
            HandleOnlineNavigation(target)
            Return
        End If
        Dim decision = HtmlDocumentPolicy.ClassifyNavigation(target, origin, currentPageKey)
        Select Case decision.Action
            Case HtmlNavigationAction.Internal
                If profile = HtmlRuntimeProfile.Local Then
                    Dim ignored = NavigateWithReportAsync(decision.Key, decision.Fragment, True)
                End If
            Case HtmlNavigationAction.Anchor
                If profile = HtmlRuntimeProfile.Local Then
                    expectedNavigation = UrlOf(currentPageKey, decision.Fragment)
                    view.CoreWebView2?.Navigate(expectedNavigation)
                End If
            Case HtmlNavigationAction.OpenTyped
                Dim local = TryCast(documentSource, LocalHtmlDocumentSource)
                Dim path = If(local IsNot Nothing, local.LocalPathOf(decision.Key), "")
                If path.Length > 0 Then RaiseEvent TypedOpenRequested(path)
            Case HtmlNavigationAction.External
                RaiseEvent ExternalLinkRequested(decision.ExternalUrl, False)
            Case HtmlNavigationAction.Mail
                RaiseEvent ExternalLinkRequested(decision.ExternalUrl, True)
        End Select
    End Sub

    ' Online: same-origin pages stay in the host; another origin goes to the system browser after
    ' the user's own click; mailto needs the explicit external action; everything else is dropped.
    Private Sub HandleOnlineNavigation(target As String)
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(target, UriKind.Absolute, uri) Then Return
        If uri.Scheme = Uri.UriSchemeMailto Then
            RaiseEvent ExternalLinkRequested(uri.AbsoluteUri, True)
            Return
        End If
        Dim external = HtmlDocumentPolicy.NormalizeExternalUrl(target)
        If external.Length = 0 Then Return
        If String.Equals(HtmlAddressPolicy.OriginOf(uri), HtmlAddressPolicy.OriginOf(origin), StringComparison.OrdinalIgnoreCase) Then
            Dim fragment = If(uri.Fragment.Length > 1, uri.Fragment.Substring(1), "")
            Dim ignored = NavigateWithReportAsync(uri.GetLeftPart(UriPartial.Query), fragment, True)
        Else
            RaiseEvent ExternalLinkRequested(external, False)
        End If
    End Sub

    Private Async Function NavigateWithReportAsync(target As String, fragment As String, pushHistory As Boolean) As Task
        Dim outcome = Await NavigateCoreAsync(target, fragment, pushHistory, session)
        If outcome <> HtmlOutcome.Ok AndAlso outcome <> HtmlOutcome.Cancelled AndAlso Alive Then RaiseEvent Failed(outcome)
    End Function

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        If Not Alive Then Return
        Dim waiter = navigationDone
        navigationDone = Nothing
        If e.IsSuccess Then
            waiter?.TrySetResult(HtmlOutcome.Ok)
            RaiseEvent ContentReady()
            RaiseEvent StateChanged()
        ElseIf e.WebErrorStatus <> CoreWebView2WebErrorStatus.OperationCanceled Then
            Dim outcome = If(e.WebErrorStatus = CoreWebView2WebErrorStatus.Timeout, HtmlOutcome.TimedOut, HtmlOutcome.UnsupportedResource)
            If waiter IsNot Nothing Then
                waiter.TrySetResult(outcome)
            Else
                RaiseEvent Failed(outcome)
            End If
        End If
    End Sub

    Private Sub OnProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        Dim outcome As HtmlOutcome
        Select Case e.ProcessFailedKind
            Case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive : outcome = HtmlOutcome.RendererUnresponsive
            Case CoreWebView2ProcessFailedKind.BrowserProcessExited, CoreWebView2ProcessFailedKind.RenderProcessExited : outcome = HtmlOutcome.RendererExited
            Case Else : Return
        End Select
        navigationDone?.TrySetResult(outcome)
        Post(Sub() RaiseEvent Failed(outcome))
    End Sub

    ' Reduced native menu: copy and select all only - no Save as, DevTools, print or link targets.
    Private Sub OnContextMenuRequested(sender As Object, e As CoreWebView2ContextMenuRequestedEventArgs)
        Dim keep = e.MenuItems.Where(Function(item) item.Kind = CoreWebView2ContextMenuItemKind.Command AndAlso
            (item.Name = "copy" OrElse item.Name = "selectAll" OrElse item.Name = "copyImage")).ToList()
        e.MenuItems.Clear()
        For Each item In keep
            e.MenuItems.Add(item)
        Next
        If keep.Count = 0 Then e.Handled = True
    End Sub

    Private Sub OnAcceleratorKey(sender As Object, e As KeyEventArgs)
        Dim command As HtmlHostCommand
        Select Case e.KeyData
            Case Keys.Escape : command = HtmlHostCommand.LeaveDocument
            Case Keys.Control Or Keys.F : command = HtmlHostCommand.FindStart
            Case Keys.F3 : command = HtmlHostCommand.FindNext
            Case Keys.Shift Or Keys.F3 : command = HtmlHostCommand.FindPrevious
            Case Keys.F10 : command = HtmlHostCommand.ChromeKeyboard
            Case Keys.F1 : command = HtmlHostCommand.Help
            Case Keys.Control Or Keys.P : command = HtmlHostCommand.Print
            Case Keys.Control Or Keys.Oemplus, Keys.Control Or Keys.Add : command = HtmlHostCommand.ZoomIn
            Case Keys.Control Or Keys.OemMinus, Keys.Control Or Keys.Subtract : command = HtmlHostCommand.ZoomOut
            Case Keys.Control Or Keys.D0, Keys.Control Or Keys.NumPad0 : command = HtmlHostCommand.ZoomReset
            Case Keys.Alt Or Keys.Left, Keys.BrowserBack : command = HtmlHostCommand.Back
            Case Keys.Alt Or Keys.Right, Keys.BrowserForward : command = HtmlHostCommand.Forward
            Case Keys.F5 : command = HtmlHostCommand.Reload
            Case Else : Return
        End Select
        e.Handled = True
        e.SuppressKeyPress = True
        Post(Sub() RaiseEvent CommandRequested(command))
    End Sub

    Private Sub OnZoomFactorChanged(sender As Object, e As EventArgs)
        If zoomGuard OrElse Not Alive Then Return
        Dim clamped = Math.Max(HtmlZoomMinFactor, Math.Min(HtmlZoomMaxFactor, view.ZoomFactor))
        If Math.Abs(clamped - view.ZoomFactor) > 0.0001 Then
            zoomGuard = True
            Try
                view.ZoomFactor = clamped
            Finally
                zoomGuard = False
            End Try
        End If
        RaiseEvent ZoomChanged(ZoomPercent)
    End Sub

    Public Sub SetZoom(percent As Integer)
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        view.ZoomFactor = Math.Max(HtmlZoomMinFactor, Math.Min(HtmlZoomMaxFactor, percent / 100.0))
    End Sub

    ' Fit width: scale so the widest content fits the viewport, within the zoom range.
    Public Async Function FitWidthAsync() As Task
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        Dim mine = session
        Dim json = Await view.CoreWebView2.ExecuteScriptAsync(FitWidthScript)
        If Not Current(mine) Then Return
        Dim ratio As Double
        If Not Double.TryParse(json, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, ratio) OrElse ratio <= 0 OrElse Double.IsInfinity(ratio) Then Return
        view.ZoomFactor = Math.Max(HtmlZoomMinFactor, Math.Min(HtmlZoomMaxFactor, view.ZoomFactor * ratio))
    End Function

    Private Const FitWidthScript As String = "(function(){var d=document.documentElement;var w=Math.max(d.scrollWidth,document.body?document.body.scrollWidth:0);return w>0?innerWidth/w:1;})()"

    Public Async Function BackAsync() As Task
        If backStack.Count = 0 OrElse Not Alive Then Return
        Dim target = backStack(backStack.Count - 1)
        backStack.RemoveAt(backStack.Count - 1)
        forwardStack.Add(currentPageKey)
        Await NavigateWithReportAsync(target, "", False)
    End Function

    Public Async Function ForwardAsync() As Task
        If forwardStack.Count = 0 OrElse Not Alive Then Return
        Dim target = forwardStack(forwardStack.Count - 1)
        forwardStack.RemoveAt(forwardStack.Count - 1)
        backStack.Add(currentPageKey)
        Await NavigateWithReportAsync(target, "", False)
    End Function

    ' Reload re-reads the source; a changed source yields a new version, never old bytes.
    Public Async Function ReloadAsync() As Task(Of HtmlOutcome)
        If Not Alive OrElse currentPageKey.Length = 0 Then Return HtmlOutcome.Cancelled
        Return Await NavigateCoreAsync(currentPageKey, "", False, session)
    End Function

    ' Session-only approval of remote resource origins (local HtmlRemoteResources or third-party
    ' assets of an online page); never persisted, at most ten origins.
    Public Function ApproveOrigins(origins As IEnumerable(Of String)) As Integer
        For Each item In origins
            If approvedOrigins.Count >= MaxApprovedOrigins Then Exit For
            If Not String.IsNullOrWhiteSpace(item) Then approvedOrigins.Add(item.Trim())
        Next
        blockedOrigins.ExceptWith(approvedOrigins)
        Return approvedOrigins.Count
    End Function

    Friend Const MaxApprovedOrigins As Integer = 10

    Public Sub ApproveInsecureOrigin(value As String)
        If Not String.IsNullOrWhiteSpace(value) Then insecureOrigins.Add(value.Trim())
    End Sub

    Public Async Function FindAsync(term As String, caseSensitive As Boolean, wholeWord As Boolean) As Task
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        Dim text = HtmlDocumentPolicy.NormalizeFindTerm(term)
        If text.Length = 0 Then
            StopFind()
            RaiseEvent FindChanged(-1, 0)
            Return
        End If
        Dim mine = session
        Dim options = environment.CreateFindOptions()
        options.FindTerm = text
        options.IsCaseSensitive = caseSensitive
        options.ShouldMatchWord = wholeWord
        options.SuppressDefaultFindDialog = True
        options.ShouldHighlightAllMatches = True
        findActive = True
        Await view.CoreWebView2.Find.StartAsync(options)
        If Not Current(mine) OrElse Not findActive Then Return
        RaiseEvent FindChanged(view.CoreWebView2.Find.ActiveMatchIndex, view.CoreWebView2.Find.MatchCount)
    End Function

    Public Sub FindStep(backwards As Boolean)
        If Not Alive OrElse Not findActive OrElse view.CoreWebView2 Is Nothing Then Return
        If backwards Then view.CoreWebView2.Find.FindPrevious() Else view.CoreWebView2.Find.FindNext()
    End Sub

    Public Sub StopFind()
        If Not findActive Then Return
        findActive = False
        Try
            view.CoreWebView2?.Find.Stop()
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is Runtime.InteropServices.COMException
        End Try
    End Sub

    ' A stale count event cannot revive a closed find panel.
    Private Sub OnFindCountChanged(sender As Object, e As Object)
        If Not findActive OrElse Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        RaiseEvent FindChanged(view.CoreWebView2.Find.ActiveMatchIndex, view.CoreWebView2.Find.MatchCount)
    End Sub

    Public Sub ShowPrint()
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        view.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser)
    End Sub

    Public Async Function ApplyAppearanceAsync(value As HtmlAppearance) As Task
        appearanceValue = If(value, New HtmlAppearance())
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        Dim data = JsonSerializer.Serialize(New With {
            .reader = appearanceValue.Reader, .theme = appearanceValue.Theme,
            .family = ModernViewerPreferences.NormalizeHtmlFontFamily(appearanceValue.FontFamily),
            .size = Math.Max(ModernViewerPreferences.HtmlFontSizeMin, Math.Min(ModernViewerPreferences.HtmlFontSizeMax, appearanceValue.FontSize)),
            .line = Math.Max(1.0, Math.Min(3.0, appearanceValue.LineHeight)),
            .column = If(appearanceValue.ColumnWidth = 0, 0, Math.Max(ModernViewerPreferences.HtmlColumnWidthMin, Math.Min(ModernViewerPreferences.HtmlColumnWidthMax, appearanceValue.ColumnWidth))),
            .margin = Math.Max(0, Math.Min(ModernViewerPreferences.HtmlMarginMax, appearanceValue.Margin)),
            .wrap = appearanceValue.Wrap})
        Await view.CoreWebView2.ExecuteScriptAsync("(" & AppearanceScript & ")(" & data & ")")
    End Function

    ' Fixed host code: values arrive as JSON data and go through CSSOM setProperty.
    Private Const AppearanceScript As String =
        "function(o){var id='fms-host-reader';var s=document.getElementById(id);" &
        "if(!o.reader){if(s)s.remove();return;}" &
        "if(!s){s=document.createElement('style');s.id=id;(document.head||document.documentElement).appendChild(s);}" &
        "var sh=s.sheet;while(sh.cssRules.length)sh.deleteRule(0);" &
        "var p={light:['#ffffff','#1b1b1b','#0b57d0'],dark:['#1e1f22','#e3e3e3','#8ab4f8'],sepia:['#f4ecd8','#4f3b2a','#7a4b00'],black:['#000000','#d6d6d6','#8ab4f8']}[o.theme]||['#ffffff','#1b1b1b','#0b57d0'];" &
        "function rule(sel){return sh.cssRules[sh.insertRule(sel+'{}',sh.cssRules.length)].style;}" &
        "var h=rule('html');h.setProperty('background-color',p[0],'important');h.setProperty('color',p[1],'important');" &
        "var b=rule('body');b.setProperty('background-color',p[0],'important');b.setProperty('color',p[1],'important');" &
        "b.setProperty('font-size',o.size+'px','important');b.setProperty('line-height',String(o.line),'important');" &
        "b.setProperty('padding',o.margin+'px','important');b.setProperty('margin','0 auto','important');" &
        "if(o.column>0){b.setProperty('max-width',o.column+'ch','important');}" &
        "if(o.family&&o.family!=='authored'){var f=(o.family==='serif'||o.family==='sans-serif'||o.family==='monospace')?o.family:JSON.stringify(o.family);b.setProperty('font-family',f,'important');}" &
        "var all=rule('body *:not(img):not(svg):not(svg *):not(video)');all.setProperty('background-color','transparent','important');all.setProperty('color','inherit','important');" &
        "if(o.family&&o.family!=='authored'){all.setProperty('font-family','inherit','important');}" &
        "var a=rule('a, a *');a.setProperty('color',p[2],'important');" &
        "var im=rule('img, svg');im.setProperty('max-width','100%','important');im.setProperty('height','auto','important');" &
        "var w=rule('table, pre');w.setProperty('display','block','important');w.setProperty('max-width','100%','important');w.setProperty('overflow-x','auto','important');" &
        "if(o.wrap){var pr=rule('pre, code');pr.setProperty('white-space','pre-wrap','important');pr.setProperty('overflow-wrap','anywhere','important');var td=rule('td, th');td.setProperty('overflow-wrap','anywhere','important');}" &
        "}"

    Public Async Function ReadPositionAsync() As Task(Of HtmlPosition)
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return Nothing
        Dim mine = session
        Dim json = Await view.CoreWebView2.ExecuteScriptAsync(PositionScript)
        If Not Current(mine) Then Return Nothing
        Try
            Dim raw = JsonSerializer.Deserialize(Of String)(json)
            If String.IsNullOrEmpty(raw) Then Return Nothing
            Using document = JsonDocument.Parse(raw)
                Dim anchor = document.RootElement.GetProperty("a").GetString()
                Dim ratio = document.RootElement.GetProperty("r").GetDouble()
                If Double.IsNaN(ratio) OrElse Double.IsInfinity(ratio) Then ratio = 0
                Return New HtmlPosition With {.PageKey = currentPageKey, .Anchor = If(anchor, "").Substring(0, Math.Min(If(anchor, "").Length, 512)), .Ratio = Math.Max(0, Math.Min(1, ratio))}
            End Using
        Catch ex As Exception When TypeOf ex Is JsonException OrElse TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is InvalidOperationException
            Return Nothing
        End Try
    End Function

    ' The last element with an id at or above the viewport top is the stable anchor.
    Private Const PositionScript As String =
        "(function(){var d=document.documentElement;var r=Math.min(1,Math.max(0,scrollY/Math.max(1,d.scrollHeight-innerHeight)));" &
        "var a='';var els=document.querySelectorAll('[id]');for(var i=0;i<els.length&&i<20000;i++){var t=els[i].getBoundingClientRect().top;if(t<=8){a=els[i].id;}else{break;}}" &
        "if(scrollY<8){a='';}return JSON.stringify({a:a,r:r});})()"

    Public Async Function RestorePositionAsync(position As HtmlPosition) As Task
        If position Is Nothing OrElse Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        Dim data = JsonSerializer.Serialize(New With {.a = If(position.Anchor, ""), .r = Math.Max(0, Math.Min(1, position.Ratio))})
        Await view.CoreWebView2.ExecuteScriptAsync("(function(o){var e=o.a?document.getElementById(o.a):null;if(e){e.scrollIntoView({block:'start'});return;}" &
            "window.scrollTo(0,o.r*Math.max(0,document.documentElement.scrollHeight-innerHeight));})(" & data & ")")
    End Function

    Public Async Function ScrollAsync(forward As Boolean) As Task
        If Not Alive OrElse view.CoreWebView2 Is Nothing Then Return
        Await view.CoreWebView2.ExecuteScriptAsync(If(forward, "window.scrollBy(0,innerHeight*0.85)", "window.scrollBy(0,-innerHeight*0.85)"))
    End Function

    Public Async Function ScrollByAsync(pixels As Integer) As Task
        If Not Alive OrElse view.CoreWebView2 Is Nothing OrElse pixels = 0 Then Return
        Await view.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0," & pixels.ToString(Globalization.CultureInfo.InvariantCulture) & ")")
    End Function

    Public Sub FocusDocument()
        If Alive AndAlso view.CoreWebView2 IsNot Nothing Then view.Focus()
    End Sub

    Private Sub Post(action As Action)
        If Not Alive OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(New Action(Sub()
                                       If Alive Then action()
                                   End Sub))
        Catch ex As InvalidOperationException
        End Try
    End Sub

    ' Section 10.2 leave: invalidate the token first, cancel work, stop Find and the page,
    ' detach and release the controller, then the source; repeated calls are harmless.
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not disposedValue Then
            disposedValue = True
            session = Nothing
            lifetime.Cancel()
            navigationDone?.TrySetResult(HtmlOutcome.Cancelled)
            Try
                If view.CoreWebView2 IsNot Nothing Then
                    If findActive Then view.CoreWebView2.Find.Stop()
                    view.CoreWebView2.Stop()
                    RemoveHandler view.CoreWebView2.WebResourceRequested, AddressOf OnWebResourceRequested
                    RemoveHandler view.CoreWebView2.NavigationStarting, AddressOf OnNavigationStarting
                    RemoveHandler view.CoreWebView2.NavigationCompleted, AddressOf OnNavigationCompleted
                    RemoveHandler view.CoreWebView2.NewWindowRequested, AddressOf OnNewWindowRequested
                    RemoveHandler view.CoreWebView2.ProcessFailed, AddressOf OnProcessFailed
                End If
            Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is Runtime.InteropServices.COMException
            End Try
            findActive = False
            pagePending = Nothing
            view.Dispose()
            If acquired Then
                acquired = False
                HtmlRuntimeService.ReleaseController(profile)
            End If
            fetcher?.Dispose()
            documentSource?.Dispose()
            lifetime.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

' Section 14: typed outcomes as localized, actionable sentences - never a path, URL query or stack.
Friend Module HtmlViewerText
    Public Function Outcome(status As HtmlOutcome) As String
        Select Case status
            Case HtmlOutcome.Ok : Return ""
            Case HtmlOutcome.Disabled : Return Localization.T("Просмотр HTML-страниц выключен. Включите его в настройках, раздел «Документы».")
            Case HtmlOutcome.SecureRefused : Return Localization.T("HTML из защищённого контейнера не открывается в просмотре страниц. Показан исходный текст.")
            Case HtmlOutcome.UnsupportedPlatform : Return Localization.T("Просмотр HTML-страниц недоступен в этой версии Windows. Откройте файл в браузере или посмотрите исходный текст.")
            Case HtmlOutcome.RuntimeMissing : Return Localization.T("Не найден компонент Microsoft Edge WebView2. Установите его с сайта Microsoft или откройте файл в браузере.")
            Case HtmlOutcome.LoaderMissing : Return Localization.T("Не найден загрузчик просмотра HTML. Переустановите программу.")
            Case HtmlOutcome.LoaderWrongArchitecture : Return Localization.T("Загрузчик просмотра HTML не подходит для этой системы. Переустановите программу.")
            Case HtmlOutcome.RequiredApiUnavailable : Return Localization.T("Установленный компонент Microsoft Edge WebView2 устарел. Обновите его через Центр обновления Windows или с сайта Microsoft.")
            Case HtmlOutcome.RuntimeBlocked : Return Localization.T("Компонент Microsoft Edge WebView2 не запустился. Возможно, его запуск запрещён политикой. Откройте файл в браузере.")
            Case HtmlOutcome.SourceMissing : Return Localization.T("Файл страницы не найден.")
            Case HtmlOutcome.AccessDenied : Return Localization.T("Нет доступа к файлу страницы.")
            Case HtmlOutcome.SourceChanged : Return Localization.T("Файл страницы изменился. Обновите просмотр.")
            Case HtmlOutcome.InvalidEncoding : Return Localization.T("Не удалось определить кодировку страницы. Показан исходный текст.")
            Case HtmlOutcome.InvalidMarkup : Return Localization.T("Разметку страницы не удалось разобрать. Показан исходный текст.")
            Case HtmlOutcome.RootEscape : Return Localization.T("Ссылка ведёт за пределы папки документа и заблокирована.")
            Case HtmlOutcome.UnsupportedResource : Return Localization.T("Этот тип содержимого не показывается в просмотре страниц.")
            Case HtmlOutcome.LimitExceeded : Return Localization.T("Страница слишком велика для просмотра. Откройте исходный текст или браузер.")
            Case HtmlOutcome.NetworkBlocked : Return Localization.T("Сетевой доступ для этой страницы заблокирован.")
            Case HtmlOutcome.RedirectNeedsApproval : Return Localization.T("Страница перенаправляет на другой сайт. Подтвердите переход.")
            Case HtmlOutcome.CertificateRejected : Return Localization.T("Сертификат сайта недействителен. Страница не открыта.")
            Case HtmlOutcome.TimedOut : Return Localization.T("Страница не загрузилась вовремя. Повторите попытку.")
            Case HtmlOutcome.Cancelled : Return Localization.T("Открытие страницы отменено.")
            Case HtmlOutcome.RendererExited : Return Localization.T("Процесс отображения страницы завершился. Повторите попытку.")
            Case HtmlOutcome.RendererUnresponsive : Return Localization.T("Процесс отображения страницы не отвечает. Повторите попытку.")
            Case Else : Return Localization.T("Страницу не удалось показать.")
        End Select
    End Function
End Module
#End If
