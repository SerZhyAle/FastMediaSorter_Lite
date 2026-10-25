Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Specification 203 section 5.10 (T-UI-1): the "Google Drive" page of Manager settings, built the way the program builds it
''' and shown in each state it can be in (no OAuth client, signed out with the longest sign-out sentence, waiting for the
''' browser, signed in with this PC's line, a failing round), at 100 % and 175 % in en, ru and de. Held for every visible control
''' of the page: none leaves its container or the page, none lies over a sibling, no label text is cut, no button is narrower
''' than its caption, every command button carries its glyph (WINDOWS-UI section 11), and no button's hit area reaches another
''' pointer control of the window. The scale is EMULATED the way the other settings tests do it; how the page LOOKS stays manual
''' acceptance. The page is in <see cref="ManagerSettingsPageOptionRowTests"/>'s window too (its pages are walked there), but it has
''' no check box, so nothing of it is audited there - this is its audit.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class DriveSettingsPageLayoutTests

    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic
    Private Const ClientId As String = "layout-test-client.apps.example"
    Private Const ClientSecret As String = "layout-test-secret-value"

    Private Shared ReadOnly Kinds As String() = {"not-configured", "signed-out", "signing-in", "signed-in", "unavailable"}

    ' --- harness (the same as the other settings-page tests: an STA thread, a scratch hive, a faked exchange component) ---

    Private Shared Sub OnSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        Assert.True(thread.Join(280000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Sub InLanguage(code As String, check As Action)
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Dim raised As New List(Of Exception)()
                  Dim handler As ThreadExceptionEventHandler = Sub(sender As Object, e As ThreadExceptionEventArgs) raised.Add(e.Exception)
                  Dim previousGate As String = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
                  ExchangeTestSupport.Reset()
                  Dim component As String = ExchangeTestSupport.MakeFakeComponent(True, False, False)
                  Dim worker As String = ExchangeTestSupport.MakeFakeWorker()
                  Try
                      Localization.CurrentCode = code
                      ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                      Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, "1")
                      ExchangeProcess.ExeDirectoryOverride = component
                      ExchangeProcess.RunOverride = Function(args, input) New ExchangeProcess.RunResult With {.Started = False}
                      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
                      AddHandler Application.ThreadException, handler
                      Using hive As New ScratchHive()
                          check()
                      End Using
                  Finally
                      RemoveHandler Application.ThreadException, handler
                      Environment.SetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName, previousGate)
                      ExchangeTestSupport.Reset()
                      ExchangeTestSupport.DeleteQuietly(component)
                      ExchangeTestSupport.DeleteQuietly(worker)
                      Localization.CurrentCode = saved
                  End Try
                  Assert.True(raised.Count = 0, "a control raised while painting or handling a message: " & String.Join(vbLf, raised.Select(Function(x) x.ToString())))
              End Sub)
    End Sub

    Private Shared Function Open(factor As Single) As Share_Settings_Form
        Dim window As New Share_Settings_Form(destination:=SettingsDestination.Manager)
        Try
            GetType(Share_Settings_Form).GetField("_exLoaded", Flags).SetValue(window, True)
            GetType(Share_Settings_Form).GetField("_hostingLoaded", Flags).SetValue(window, True)
            Dim extra As Single = factor / (window.DeviceDpi / 96.0F)
            If Math.Abs(extra - 1.0F) > 0.01F Then
                window.Font = New Font(window.Font.FontFamily, window.Font.SizeInPoints * extra)
                window.Scale(New SizeF(extra, extra))
            End If
            window.StartPosition = FormStartPosition.Manual
            window.Location = New Point(20, 20)
            window.Show()
            window.ClientSize = New Size(CInt(820 * factor), CInt(600 * factor))
            Application.DoEvents()
            Return window
        Catch
            window.Dispose()
            Throw
        End Try
    End Function

    Private Shared Function Pages(window As Share_Settings_Form) As Dictionary(Of String, Panel)
        Return DirectCast(GetType(Share_Settings_Form).GetField("_pages", Flags).GetValue(window), Dictionary(Of String, Panel))
    End Function

    Private Shared Sub ShowPage(window As Share_Settings_Form, pageId As String)
        GetType(Share_Settings_Form).GetMethod("SelectSettingsPage", Flags).Invoke(window, New Object() {pageId})
        Dim groups = DirectCast(GetType(Share_Settings_Form).GetField("_pageGroups", Flags).GetValue(window),
                                Dictionary(Of String, List(Of Share_Settings_Form.SettingsNavGroup)))
        If groups.ContainsKey(pageId) Then
            Dim setExpanded As MethodInfo = GetType(Share_Settings_Form).GetMethod("SetGroupExpanded", Flags)
            For Each grp In groups(pageId)
                setExpanded.Invoke(window, New Object() {pageId, grp, True})
            Next
        End If
        Application.DoEvents()
    End Sub

    Private Shared Function Member(Of T As Control)(window As Share_Settings_Form, name As String) As T
        Dim field As FieldInfo = GetType(Share_Settings_Form).GetField(name, Flags)
        Assert.True(field IsNot Nothing, "the window has no field " & name)
        Return TryCast(field.GetValue(window), T)
    End Function

    Private Shared Function AllOf(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each c As Control In parent.Controls
            found.Add(c)
            found.AddRange(AllOf(c))
        Next
        Return found
    End Function

    Private Shared Function OnForm(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    Private Shared Function ClippedOnForm(form As Form, c As Control) As Rectangle
        Dim visible As Rectangle = OnForm(form, c)
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            visible = Rectangle.Intersect(visible, OnForm(form, p))
            p = p.Parent
        End While
        Return visible
    End Function

    Private Shared Function TextFits(l As Label) As Boolean
        If l Is Nothing OrElse Not l.Visible OrElse l.Text.Length = 0 Then Return True
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(l.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim need As Integer = TextRenderer.MeasureText(l.Text, l.Font, New Size(Math.Max(1, l.Width - l.Padding.Horizontal), Integer.MaxValue), flags).Height
        Return need <= l.Height + 2
    End Function

    Private Shared Function Describe(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name & "(" & c.Text & ")", c.Name)
    End Function

    ' --- the PC in each state ---------------------------------------------------------------------------

    ''' <summary>A PC on the in-memory Drive in one of the states the page shows. Nothing here touches the real profile or the network.</summary>
    Private NotInheritable Class Scene
        Implements IDisposable

        Public ReadOnly Drive As New FakeDriveHandler()
        Public ReadOnly Http As HttpClient
        Public ReadOnly Folder As String
        Public ReadOnly Host As DriveChannelHost
        Private ReadOnly _store As ExchangeDriveTokenStore
        Private ReadOnly _config As ExchangeDriveClientConfig
        Private _signingIn As Task

        Public Sub New(kind As String)
            Folder = Directory.CreateTempSubdirectory("fmsdrvpage").FullName
            Http = New HttpClient(Drive, False)
            Dim exchange As String = Path.Combine(Folder, "exchange")
            _store = New ExchangeDriveTokenStore(exchange, Nothing)
            _config = If(kind = "not-configured", New ExchangeDriveClientConfig("", ""), New ExchangeDriveClientConfig(ClientId, ClientSecret))
            Dim ids As New DriveDeviceId(exchange, Function() "", Function() "BBBBBBBBBBBBBBBBBBBBBA")
            Dim factory As Func(Of String, String, ExchangeDriveService) = AddressOf BuildService
            If kind = "signed-out" Then
                factory = Function(id As String, name As String) As ExchangeDriveService
                              Throw New InvalidOperationException("no service in this scene")
                          End Function
            End If
            Host = New DriveChannelHost(Http, _store, _config, ids, "Office PC", Function(url As String) True, Function() "", factory)

            Select Case kind
                Case "signed-out"
                    ' signed in at an earlier start, then left with nothing confirmed: the longest sentence the page can carry
                    StoreSignIn()
                    Host.SignOutAsync().GetAwaiter().GetResult()
                Case "signing-in"
                    _signingIn = Host.SignInAsync()
                Case "signed-in"
                    StoreSignIn()
                    Host.StartIfSignedIn()
                Case "unavailable"
                    StoreSignIn()
                    Drive.FailWhen(Function(r) r.Method = "GET" AndAlso r.Path = "/drive/v3/files", 100000, 503)
                    Host.StartIfSignedIn()
                    Assert.True(SpinWait.SpinUntil(Function() Host.State = DriveChannelState.Unavailable, 20000), "the failing round never showed")
            End Select
        End Sub

        Private Sub StoreSignIn()
            Const seed As String = "seed-refresh-token-for-the-layout-tests"
            Drive.AcceptRefreshToken(seed)
            Assert.True(_store.Save(ClientId, seed))
        End Sub

        Private Function BuildService(id As String, name As String) As ExchangeDriveService
            Dim session As New ExchangeDriveSession(Http, _store, _config)
            Dim client As New ExchangeDriveClient(Http, session, Function(span As TimeSpan, token As CancellationToken) Task.CompletedTask, Function() 0.5)
            Return New ExchangeDriveService(session, client, New ExchangeDriveDirectory(client, id),
                                            New ExchangeDriveQueue(client, id, name, New ExchangeDriveMemoryHandledStore()),
                                            Function(span As TimeSpan, token As CancellationToken) Task.Delay(Timeout.Infinite, token), Function() 0.5)
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Host.CancelSignIn()
            Host.Dispose()
            Http.Dispose()
            Try
                Directory.Delete(Folder, True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            End Try
        End Sub
    End Class

    ' --- the audit ---------------------------------------------------------------------------------------

    ''' <summary>What the state must show: the commands that are on, the lines that are on. A page that audits the wrong thing passes silently.</summary>
    Private Shared Sub ExpectState(window As Share_Settings_Form, kind As String, lbl As String, found As List(Of String))
        Dim signIn As Button = Member(Of Button)(window, "_btnDrvSignIn")
        Dim cancel As Button = Member(Of Button)(window, "_btnDrvCancel")
        Dim signOut As Button = Member(Of Button)(window, "_btnDrvSignOut")
        Dim device As Label = Member(Of Label)(window, "_drvDevice")
        Dim result As Label = Member(Of Label)(window, "_drvResult")
        Dim wantSignIn As Boolean = kind = "not-configured" OrElse kind = "signed-out"
        Dim wantCancel As Boolean = kind = "signing-in"
        Dim wantSignOut As Boolean = kind = "signed-in" OrElse kind = "unavailable"
        If signIn.Visible <> wantSignIn Then found.Add(lbl & ": 'Sign in' is " & If(signIn.Visible, "shown", "hidden") & " in the state " & kind)
        If cancel.Visible <> wantCancel Then found.Add(lbl & ": 'Cancel' is " & If(cancel.Visible, "shown", "hidden") & " in the state " & kind)
        If signOut.Visible <> wantSignOut Then found.Add(lbl & ": 'Sign out' is " & If(signOut.Visible, "shown", "hidden") & " in the state " & kind)
        If kind = "not-configured" AndAlso signIn.Enabled Then found.Add(lbl & ": 'Sign in' is enabled with no OAuth client")
        If kind = "signed-out" AndAlso Not signIn.Enabled Then found.Add(lbl & ": 'Sign in' is disabled although a client exists")
        If kind = "signed-out" AndAlso Not result.Visible Then found.Add(lbl & ": the sign-out result is not shown")
        If kind = "signed-in" AndAlso Not device.Visible Then found.Add(lbl & ": this PC's line is not shown while signed in")
    End Sub

    Private Shared Function Audit(window As Share_Settings_Form, lbl As String, factor As Single, found As List(Of String)) As Integer
        Dim page As Panel = Pages(window)("drive")
        Dim pageBounds As Rectangle = OnForm(window, page)
        Dim everything As List(Of Control) = AllOf(page).Where(Function(c) c.Visible).ToList()
        Dim audited As Integer = 0

        For Each c As Control In everything
            Dim tag As String = lbl & " drive/" & Describe(c)
            Dim host As Control = c.Parent
            Dim client As Rectangle = host.ClientRectangle
            Dim scrolling As ScrollableControl = TryCast(host, ScrollableControl)
            If scrolling IsNot Nothing AndAlso scrolling.AutoScroll Then
                client = New Rectangle(0, 0, Math.Max(client.Width, scrolling.HorizontalScroll.Maximum + 1), Math.Max(client.Height, scrolling.VerticalScroll.Maximum + 1))
            End If
            If c.Left < -1 OrElse c.Top < -1 OrElse c.Right > client.Width + 1 OrElse c.Bottom > client.Height + 1 Then
                found.Add(tag & ": " & c.Bounds.ToString() & " leaves its container " & client.Size.ToString())
            End If
            If OnForm(window, c).Right > pageBounds.Right + 1 Then found.Add(tag & ": reaches past the right edge of the page")
            For Each s As Control In host.Controls
                If Object.ReferenceEquals(s, c) OrElse Not s.Visible Then Continue For
                Dim overlap As Rectangle = Rectangle.Intersect(c.Bounds, s.Bounds)
                If overlap.Width > 1 AndAlso overlap.Height > 1 Then found.Add(tag & ": " & c.Bounds.ToString() & " lies over " & Describe(s) & " " & s.Bounds.ToString())
            Next

            Dim text As Label = TryCast(c, Label)
            If text IsNot Nothing Then
                audited += 1
                If Not TextFits(text) Then found.Add(tag & ": the text '" & text.Text & "' is cut (" & text.Height.ToString(CultureInfo.InvariantCulture) & " px high, " & text.Width.ToString(CultureInfo.InvariantCulture) & " wide)")
            End If

            Dim button As Button = TryCast(c, Button)
            If button IsNot Nothing Then
                audited += 1
                Dim caption As Size = TextRenderer.MeasureText(button.Text, button.Font)
                If button.Width < caption.Width + button.Padding.Horizontal Then found.Add(tag & ": the button " & button.Width.ToString(CultureInfo.InvariantCulture) & " px wide is narrower than its caption " & caption.Width.ToString(CultureInfo.InvariantCulture))
                If button.Height < caption.Height Then found.Add(tag & ": the button is lower than its caption")
                If Not ButtonGlyphs.CarriesGlyph(button) Then found.Add(tag & ": the command button carries no glyph")
            End If
        Next

        ' Hit areas: each command button's reach against every other pointer control of the whole window.
        Dim pointer As List(Of Control) = AllOf(window).Where(Function(c) c.Visible AndAlso (TypeOf c Is ButtonBase OrElse TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown)).ToList()
        For Each button As Button In everything.OfType(Of Button)()
            Dim mine As Rectangle = ClippedOnForm(window, button)
            For Each other As Control In pointer
                If Object.ReferenceEquals(other, button) Then Continue For
                Dim reach As Rectangle = ClippedOnForm(window, other)
                Dim overlap As Rectangle = Rectangle.Intersect(mine, reach)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(lbl & " drive/" & Describe(button) & ": the hit area " & mine.ToString() & " reaches " & Describe(other) & " " & reach.ToString())
            Next
        Next
        Return audited
    End Function

    <Theory>
    <InlineData("en", 1.0F)>
    <InlineData("en", 1.75F)>
    <InlineData("ru", 1.0F)>
    <InlineData("ru", 1.75F)>
    <InlineData("de", 1.0F)>
    <InlineData("de", 1.75F)>
    Public Sub The_Google_Drive_page_fits_in_every_state_at_100_and_175_percent(code As String, factor As Single)
        InLanguage(code,
            Sub()
                Dim found As New List(Of String)()
                For Each kind As String In Kinds
                    Using scene As New Scene(kind)
                        DriveChannelHosts.Use(scene.Host)
                        Try
                            Using window As Share_Settings_Form = Open(factor)
                                Dim pageList As Dictionary(Of String, Panel) = Pages(window)
                                Assert.True(pageList.ContainsKey("drive"), "the Manager window has no 'drive' page")
                                ShowPage(window, "drive")
                                Dim lbl As String = code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "% " & kind
                                ExpectState(window, kind, lbl, found)
                                Dim audited As Integer = Audit(window, lbl, factor, found)
                                If audited < 4 Then found.Add(lbl & ": only " & audited.ToString(CultureInfo.InvariantCulture) & " texts and buttons were audited - the audit has stopped looking at the page")
                                window.Hide()
                            End Using
                        Finally
                            DriveChannelHosts.Release()
                        End Try
                    End Using
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found.Distinct()))
            End Sub)
    End Sub

End Class
