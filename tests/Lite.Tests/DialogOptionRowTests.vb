#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 243 T-11b: the twelve check boxes of eight viewer windows stand on the Settings option row (BOOLEAN-OPTION 2.0
''' sections 2 and 3). Every test drives the REAL window on an STA thread (the one exception, said in the report: the Space key
''' is not synthesised, because a real key needs the foreground; the key route is the check box's own Click, which
''' <c>PerformClick</c> runs): the drawn box and its hit target at 100 % and 175 %, the layout of the longest English, German, Russian
''' and Arabic captions, the caption that toggles once and the explanation that never does, the names, defaults and tab order the
''' windows had, a disabled row, the mirror under right-to-left, the accessible name and description, the theme walk, and a source
''' scan that keeps a native check box from being added to one of the windows directly again.
''' </summary>
<Collection("LocalizationState")>
Public Class DialogOptionRowTests

    ' --- the windows ----------------------------------------------------------------------------------

    ''' <summary>One check box of one window: how to build the window, what the check box is called, whether it starts ticked, the
    ''' explanation label it owns, the tab order the window had (the named controls it must keep in this order) and the password
    ''' field its handler masks.</summary>
    Private NotInheritable Class Subject
        Friend ReadOnly Window As String
        Friend ReadOnly CheckName As String
        Friend ReadOnly DefaultChecked As Boolean
        Friend ReadOnly ExplanationName As String
        Friend ReadOnly Tab As String
        Friend ReadOnly PasswordField As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly ExchangeOff As Boolean
        Friend ReadOnly Setup As Action(Of Form)

        Friend Sub New(window As String, checkName As String, defaultChecked As Boolean, explanationName As String, tab As String,
                       passwordField As String, make As Func(Of Form), Optional exchangeOff As Boolean = False,
                       Optional setup As Action(Of Form) = Nothing)
            Me.Window = window
            Me.CheckName = checkName
            Me.DefaultChecked = defaultChecked
            Me.ExplanationName = explanationName
            Me.Tab = tab
            Me.PasswordField = passwordField
            Me.Make = make
            Me.ExchangeOff = exchangeOff
            Me.Setup = setup
        End Sub

        Public Overrides Function ToString() As String
            Return Window & "/" & CheckName
        End Function
    End Class

    Private Shared Function InvitationText() As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build("exchange.example:443", "", "", "", "abcdefghijklmnopqrstuA")
        Dim refused As ExchangeConnectionStringResult = Nothing
        Return ExchangeConnectionString.Format(built.Value, refused)
    End Function

    Private Const LongFile As String = "A-photo-with-a-rather-long-file-name-that-goes-on.webp"

    ''' <summary>The 12 check boxes (14 subjects: the connect window also in its registration mode, the broadcast window also with the
    ''' exchange off, where the LAN hint is the explanation of "this is live").</summary>
    Private Shared Function Subjects() As List(Of Subject)
        Dim credentialTab As String = "resource_Password,resource_Reveal,resource_Remember"
        Dim editTab As String = "res_Kind,res_Name,res_Host,res_Port,res_User,res_Password,res_Reveal,res_Folder,res_ReadOnly"
        Dim secureTab As String = "secure_Password,secure_Reveal,secure_Remember"
        Dim connectTab As String = "xc_Input,xc_Paste,xc_OpenFile,xc_Login,xc_Password,xc_Reveal"
        Dim liveOnTab As String = "TextBox,live_IsLive,ListView,live_AutoCast"
        Dim liveOffTab As String = "TextBox,live_IsLive,ListView"
        Dim devicesTab As String = "res_DevicesList,res_DevicesAuto,res_DevicesSignOut,res_DevicesAdd"
        Return New List(Of Subject) From {
            New Subject("Resource_Credential_Form", "resource_Reveal", False, "", credentialTab, "resource_Password",
                        Function() New Resource_Credential_Form("My server with a rather long display name", "alice", False)),
            New Subject("Resource_Credential_Form", "resource_Remember", True, "", credentialTab, "",
                        Function() New Resource_Credential_Form("My server with a rather long display name", "alice", False)),
            New Subject("Resource_Edit_Form", "res_Reveal", False, "", editTab, "res_Password",
                        Function() New Resource_Edit_Form(Nothing)),
            New Subject("Resource_Edit_Form", "res_ReadOnly", False, "", editTab, "",
                        Function() New Resource_Edit_Form(Nothing)),
            New Subject("Secure_Password_Form", "secure_Reveal", False, "", secureTab, "secure_Password",
                        Function() New Secure_Password_Form("photos.fd-sec", False)),
            New Subject("Secure_Password_Form", "secure_Remember", True, "", secureTab, "",
                        Function() New Secure_Password_Form("photos.fd-sec", False)),
            New Subject("Exchange_Password_Change_Form", "xpc_Reveal", False, "", "xpc_OldBox,xpc_NewBox,xpc_RepeatBox,xpc_Reveal", "xpc_OldBox",
                        Function() New Exchange_Password_Change_Form("alice")),
            New Subject("Exchange_Connect_Form", "xc_Reveal", False, "", connectTab, "xc_Password",
                        Function() New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts())),
            New Subject("Exchange_Connect_Form(register)", "xc_Reveal", False, "", "", "xc_Password",
                        Function() New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts()),
                        setup:=Sub(f) DirectCast(f.Controls("xc_Input"), TextBox).Text = InvitationText()),
            New Subject("LiveBroadcast_Form(exchange off)", "live_IsLive", False, "live_LanHint", liveOffTab, "",
                        Function() New LiveBroadcast_Form(), exchangeOff:=True),
            New Subject("LiveBroadcast_Form", "live_IsLive", False, "", liveOnTab, "",
                        Function() New LiveBroadcast_Form()),
            New Subject("LiveBroadcast_Form", "live_AutoCast", True, "", liveOnTab, "",
                        Function() New LiveBroadcast_Form(True)),
            New Subject("Video_Replace_Confirm_Form", "replace_Suppress", False, "", "replace_Suppress", "",
                        Function() New Video_Replace_Confirm_Form(LongFile, LongFile.Replace(".webp", ".mp4"), True)),
            New Subject("Exchange_Devices_Panel", "res_DevicesAuto", False, "res_DevicesAutoNote", devicesTab, "",
                        Function() New DevicesForm())
        }
    End Function

    ''' <summary>The Exchange devices section is a panel of the Settings window, so it is hosted in a plain window here. It is
    ''' built the way ExchangeDevicesPanelTests builds it, with one stored server, and marked as running before it is shown so
    ''' that showing it opens no socket.</summary>
    Private NotInheritable Class DevicesForm
        Inherits Form

        Friend ReadOnly Devices As Exchange_Devices_Panel
        Private ReadOnly hub As ExchangeEntryHub
        Private ReadOnly folder As String
        Private ReadOnly previousProbe As Func(Of Boolean)

        Friend Sub New()
            previousProbe = ExchangeFeatures.ComponentProbeOverride
            ExchangeFeatures.ComponentProbeOverride = Function() True
            folder = Path.Combine(Path.GetTempPath(), "fms-optionrow-" & Guid.NewGuid().ToString("N"))
            Dim store As New ExchangeEntryStore(New ExchangeStoresTests.MemorySettings(), folder)
            Dim entry As New ExchangeEntry(ExchangeEntryStore.NewEntryId(), "127.0.0.1:22022", "alice", "",
                ExchangeCertificateKind.CaSigned, New String("D"c, 21) & "A", "Test viewer", "Test server", False)
            Assert.True(store.Upsert(entry))
            hub = New ExchangeEntryHub(New ExchangeConsumerPorts With {.Entries = store})
            Devices = New Exchange_Devices_Panel(hub, store, Function() New List(Of ResourceDescriptor)(), Function(r) False, Sub(r) Return)
            GetType(Exchange_Devices_Panel).GetField("_running", BindingFlags.Instance Or BindingFlags.NonPublic).SetValue(Devices, True)
            FormBorderStyle = FormBorderStyle.FixedDialog
            ShowInTaskbar = False
            ClientSize = New Size(560, 340)
            Devices.Dock = DockStyle.Fill
            Devices.Visible = True
            Controls.Add(Devices)
            Devices.Localize()
        End Sub

        ''' <summary>The host's scale: the section takes its pixel metrics from its host, and the window font grows with it.</summary>
        Friend Sub Prepare(factor As Single)
            Dim extra As Single = factor / (DeviceDpi / 96.0F)
            If Math.Abs(extra - 1.0F) > 0.01F Then Font = New Font(Font.FontFamily, Font.SizeInPoints * extra)
            ClientSize = New Size(CInt(Math.Round(560 * factor)), CInt(Math.Round(340 * factor)))
            Devices.SetMetrics(Function(n) CInt(Math.Round(n * factor)))
        End Sub

        ''' <summary>Renders the section once it is shown: the option row appears with the selected server.</summary>
        Friend Sub Reveal()
            GetType(Exchange_Devices_Panel).GetMethod("Render", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(Devices, Array.Empty(Of Object)())
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            MyBase.Dispose(disposing)
            If disposing Then
                hub.Dispose()
                ExchangeFeatures.ComponentProbeOverride = previousProbe
                Try
                    If Directory.Exists(folder) Then Directory.Delete(folder, True)
                Catch ex As IOException
                    ' a temp folder that stays behind is harmless
                End Try
            End If
        End Sub
    End Class

    ' --- helpers ----------------------------------------------------------------------------------------

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
        Assert.True(thread.Join(240000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    Private Shared Sub InLanguage(code As String, body As Action)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            ' A control that throws while it paints or handles a message must fail the test - never open the modal
            ' "unhandled exception" dialog of WinForms, which would hold the suite until its timeout.
            OnSta(Sub()
                      Dim raised As New List(Of Exception)()
                      Dim handler As ThreadExceptionEventHandler = Sub(sender As Object, e As ThreadExceptionEventArgs) raised.Add(e.Exception)
                      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
                      AddHandler Application.ThreadException, handler
                      Try
                          body()
                      Finally
                          RemoveHandler Application.ThreadException, handler
                      End Try
                      Assert.True(raised.Count = 0, "a control raised while painting or handling a message: " & String.Join(vbLf, raised.Select(Function(x) x.ToString())))
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>A window shown at a scale, with whatever scope its subject needs, released together.</summary>
    Private NotInheritable Class Shown
        Implements IDisposable

        Friend ReadOnly Form As Form
        Private ReadOnly scope As IDisposable

        Friend Sub New(form As Form, scope As IDisposable)
            Me.Form = form
            Me.scope = scope
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Form.Close()
            Finally
                Form.Dispose()
                If scope IsNot Nothing Then scope.Dispose()
            End Try
        End Sub
    End Class

    ''' <summary>Builds the subject's window in the current language, emulates a display scale (a larger font and the framework's
    ''' own Scale, the way the layout tests do), shows it - OnLoad runs the reflow - and applies the subject's set-up.</summary>
    Private Shared Function Open(subject As Subject, factor As Single) As Shown
        Dim scope As IDisposable = Nothing
        Dim form As Form = Nothing
        Try
            If subject.ExchangeOff Then scope = ExchangeFeatures.ForFlow(switchOn:=False, componentInstalled:=False, offered:=False)
            form = subject.Make()
            Dim devices As DevicesForm = TryCast(form, DevicesForm)
            Dim extra As Single = factor / (form.DeviceDpi / 96.0F)
            If devices IsNot Nothing Then
                devices.Prepare(factor)
            ElseIf Math.Abs(extra - 1.0F) > 0.01F Then
                form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                form.Scale(New SizeF(extra, extra))
            End If
            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(20, 20)
            form.Show()
            If devices IsNot Nothing Then devices.Reveal()
            Application.DoEvents()
            If subject.Setup IsNot Nothing Then
                subject.Setup(form)
                Application.DoEvents()
            End If
            Return New Shown(form, scope)
        Catch
            If form IsNot Nothing Then form.Dispose()
            If scope IsNot Nothing Then scope.Dispose()
            Throw
        End Try
    End Function

    Private Shared Function Mark(subject As Subject, code As String, factor As Single) As String
        Return subject.ToString() & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
    End Function

    Private Shared Function FindCheck(form As Form, name As String) As CheckBox
        Dim checks As CheckBox() = form.Controls.Find(name, True).OfType(Of CheckBox)().ToArray()
        Assert.True(checks.Length = 1, "expected exactly one check box named " & name & " but found " & checks.Length.ToString(CultureInfo.InvariantCulture))
        Return checks(0)
    End Function

    Private Shared Function RowOf(check As CheckBox) As OptionRowHost
        Dim row As OptionRowHost = TryCast(check.Parent, OptionRowHost)
        Assert.True(row IsNot Nothing, check.Name & " does not stand in an option row")
        Return row
    End Function

    ''' <summary>Raises Click the way a mouse up over the control does (Control.OnClick is protected).</summary>
    Private Shared Sub ClickOn(c As Control)
        GetType(Control).GetMethod("OnClick", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(c, New Object() {EventArgs.Empty})
    End Sub

    Private Shared Function OnForm(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    ''' <summary>The bounding box of what the check box really paints on its surface: the box with its ring, measured on the pixels.</summary>
    Private Shared Function PaintedBox(row As OptionRowHost) As Rectangle
        Dim check As CheckBox = row.Check
        Dim surface As Color = Color.FromArgb(240, 240, 240)
        row.ApplyColors(surface, Color.White, Color.FromArgb(110, 110, 110), Color.FromArgb(20, 20, 20), Color.FromArgb(0, 90, 200), Color.White)
        Application.DoEvents()
        Using bitmap As New Bitmap(check.Width, check.Height)
            check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
            Dim minX As Integer = Integer.MaxValue
            Dim minY As Integer = Integer.MaxValue
            Dim maxX As Integer = -1
            Dim maxY As Integer = -1
            For y As Integer = 0 To bitmap.Height - 1
                For x As Integer = 0 To bitmap.Width - 1
                    Dim p As Color = bitmap.GetPixel(x, y)
                    If Math.Abs(CInt(p.R) - surface.R) + Math.Abs(CInt(p.G) - surface.G) + Math.Abs(CInt(p.B) - surface.B) > 24 Then
                        minX = Math.Min(minX, x)
                        minY = Math.Min(minY, y)
                        maxX = Math.Max(maxX, x)
                        maxY = Math.Max(maxY, y)
                    End If
                Next
            Next
            If maxX < 0 Then Return Rectangle.Empty
            Return New Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)
        End Using
    End Function

    Private Shared Function CenterPixel(check As CheckBox) As Color
        Using bitmap As New Bitmap(check.Width, check.Height)
            check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
            Return bitmap.GetPixel(check.Width \ 2, check.Height \ 2)
        End Using
    End Function

    Private Shared Function CornerPixel(check As CheckBox) As Color
        Using bitmap As New Bitmap(check.Width, check.Height)
            check.DrawToBitmap(bitmap, New Rectangle(0, 0, check.Width, check.Height))
            Return bitmap.GetPixel(1, 1)
        End Using
    End Function

    ''' <summary>True when a label's wrapped text fits the height the label has (the same measure the layout audit uses).</summary>
    Private Shared Function TextFits(l As Label) As Boolean
        If l Is Nothing OrElse Not l.Visible OrElse l.Text.Length = 0 Then Return True
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(l.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim need As Integer = TextRenderer.MeasureText(l.Text, l.Font, New Size(Math.Max(1, l.Width - l.Padding.Horizontal), Integer.MaxValue), flags).Height
        Return need <= l.Height + 2
    End Function

    ' --- tests ------------------------------------------------------------------------------------------

    <Fact>
    Public Sub The_subjects_are_the_twelve_check_boxes_of_the_eight_windows()
        Dim all As List(Of Subject) = Subjects()
        Dim distinct As List(Of String) = all.Select(Function(s) s.Window.Split("("c)(0) & "/" & s.CheckName).Distinct().ToList()
        Assert.Equal(12, distinct.Count)
        Assert.Equal(8, all.Select(Function(s) s.Window.Split("("c)(0)).Distinct().Count())
    End Sub

    <Fact>
    Public Sub The_box_is_at_least_20_px_and_its_hit_target_at_least_32_px_at_100_and_175_percent()
        InLanguage("en",
            Sub()
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, factor)
                            Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = Mark(subject, "en", factor)
                            Dim seen As Single = check.Width / CSng(UiSizing.PointerTarget)
                            If Math.Abs(seen - factor) > 0.05F Then found.Add(lbl & ": the row is laid out at " & seen.ToString("0.00", CultureInfo.InvariantCulture) & ", not at the emulated scale")
                            Dim target As Integer = SettingsCheckBox.Px(UiSizing.PointerTarget, factor)
                            If check.Width < target - 1 OrElse check.Height < target - 1 Then found.Add(lbl & ": the hit target is " & check.Size.ToString() & ", needs " & target.ToString(CultureInfo.InvariantCulture))
                            Dim box As Rectangle = PaintedBox(row)
                            Dim side As Integer = SettingsCheckBox.Px(20, factor)
                            If box.Width < side OrElse box.Height < side Then found.Add(lbl & ": the painted box is " & box.Size.ToString() & ", needs " & side.ToString(CultureInfo.InvariantCulture))
                            If SettingsCheckBox.BoxSide(factor) < side Then found.Add(lbl & ": BoxSide is below 20 logical px")
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    <Theory>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ru")>
    <InlineData("ar")>
    Public Sub The_row_never_clips_or_overlaps_in_the_longest_captions_at_100_and_175_percent(code As String)
        InLanguage(code,
            Sub()
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, factor)
                            AuditRow(shown.Form, subject, Mark(subject, code, factor), found)
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    Private Shared Sub AuditRow(form As Form, subject As Subject, lbl As String, found As List(Of String))
        Dim check As CheckBox = FindCheck(form, subject.CheckName)
        Dim row As OptionRowHost = RowOf(check)
        If Not row.Visible Then
            found.Add(lbl & ": the row is not visible")
            Return
        End If
        Dim host As Control = row.Parent
        Dim client As Rectangle = host.ClientRectangle

        ' The row, and everything the window lays out beside it, inside the window; the row never over a sibling - the controls
        ' under it, the docked answer row among them.
        For Each s As Control In host.Controls
            If Not s.Visible Then Continue For
            If s.Dock = DockStyle.None AndAlso (s.Left < -1 OrElse s.Top < -1 OrElse s.Right > client.Width + 1 OrElse s.Bottom > client.Height + 1) Then
                found.Add(lbl & ": " & DescribeControl(s) & " " & s.Bounds.ToString() & " leaves the window " & client.Size.ToString())
            End If
            If Object.ReferenceEquals(s, row) Then Continue For
            Dim overlap As Rectangle = Rectangle.Intersect(row.Bounds, s.Bounds)
            If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                found.Add(lbl & ": the row " & row.Bounds.ToString() & " lies over " & DescribeControl(s) & " " & s.Bounds.ToString())
            End If
            If s.Top >= row.Top AndAlso Not Object.ReferenceEquals(s, row) AndAlso s.Left < row.Right AndAlso s.Right > row.Left AndAlso s.Top < row.Bottom Then
                found.Add(lbl & ": " & DescribeControl(s) & " starts at " & s.Top.ToString(CultureInfo.InvariantCulture) & " inside the row that ends at " & row.Bottom.ToString(CultureInfo.InvariantCulture))
            End If
        Next

        ' Inside the row: the box, the caption and the explanation inside it, none on another, no text cut.
        Dim inner As New List(Of Control) From {check, row.Caption}
        If row.Explanation IsNot Nothing AndAlso row.Explanation.Visible Then inner.Add(row.Explanation)
        Dim rowClient As Rectangle = row.ClientRectangle
        For i As Integer = 0 To inner.Count - 1
            Dim c As Control = inner(i)
            If c.Left < -1 OrElse c.Top < -1 OrElse c.Right > rowClient.Width + 1 OrElse c.Bottom > rowClient.Height + 1 Then
                found.Add(lbl & ": " & DescribeControl(c) & " " & c.Bounds.ToString() & " leaves its row " & rowClient.Size.ToString())
            End If
            For j As Integer = i + 1 To inner.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(c.Bounds, inner(j).Bounds)
                If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                    found.Add(lbl & ": " & DescribeControl(c) & " " & c.Bounds.ToString() & " lies over " & DescribeControl(inner(j)) & " " & inner(j).Bounds.ToString())
                End If
            Next
        Next
        If Not TextFits(row.Caption) Then found.Add(lbl & ": the caption is cut (" & row.Caption.Height.ToString(CultureInfo.InvariantCulture) & " px high)")
        If Not TextFits(row.Explanation) Then found.Add(lbl & ": the explanation is cut (" & row.Explanation.Height.ToString(CultureInfo.InvariantCulture) & " px high)")
        If row.Caption.Bottom > row.Height OrElse (row.Explanation IsNot Nothing AndAlso row.Explanation.Visible AndAlso row.Explanation.Bottom > row.Height) Then
            found.Add(lbl & ": the row is " & row.Height.ToString(CultureInfo.InvariantCulture) & " px high and its text reaches below it")
        End If
        ' The hit target is its own square: no neighbour of the row shares it (the row itself is the neighbour test above).
        If check.Width < SettingsCheckBox.Px(UiSizing.PointerTarget, 1.0F) Then found.Add(lbl & ": the hit target is narrower than 32 px")

        ' Whatever the layout audit of the whole window says about this option is a defect of this slice too.
        For Each problem As String In DialogLayoutTests.Problems(form)
            If problem.Contains(subject.CheckName, StringComparison.Ordinal) Then found.Add(lbl & ": " & problem)
        Next
    End Sub

    Private Shared Function DescribeControl(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name)
    End Function

    <Fact>
    Public Sub A_click_on_the_caption_toggles_once_a_click_on_the_explanation_never_does_and_the_key_route_toggles()
        InLanguage("en",
            Sub()
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim form As Form = shown.Form
                        Dim check As CheckBox = FindCheck(form, subject.CheckName)
                        Dim row As OptionRowHost = RowOf(check)
                        Dim lbl As String = subject.ToString()
                        Dim flips As Integer = 0
                        AddHandler check.CheckedChanged, Sub() flips += 1
                        Dim start As Boolean = check.Checked
                        Dim field As TextBox = If(subject.PasswordField.Length = 0, Nothing, DirectCast(form.Controls.Find(subject.PasswordField, True).Single(), TextBox))

                        Assert.True(Object.ReferenceEquals(Cursors.Hand, row.Caption.Cursor), lbl & ": the caption has the hand cursor")
                        ClickOn(row.Caption)
                        Assert.True(flips = 1, lbl & ": one click on the caption flips the box exactly once, not " & flips.ToString(CultureInfo.InvariantCulture))
                        Assert.Equal(Not start, check.Checked)
                        If field IsNot Nothing Then Assert.Equal(Not check.Checked, field.UseSystemPasswordChar)
                        ClickOn(row.Caption)
                        Assert.True(flips = 2, lbl & ": a second click flips it back")
                        Assert.Equal(start, check.Checked)
                        If field IsNot Nothing Then Assert.Equal(Not check.Checked, field.UseSystemPasswordChar)

                        If subject.ExplanationName.Length > 0 Then
                            Dim explanation As Label = row.Explanation
                            Assert.True(explanation IsNot Nothing AndAlso explanation.Visible AndAlso explanation.Name = subject.ExplanationName, lbl & ": the explanation " & subject.ExplanationName & " stands in the row")
                            Assert.False(Object.ReferenceEquals(Cursors.Hand, explanation.Cursor), lbl & ": the explanation is not a hand")
                            ClickOn(explanation)
                            ClickOn(explanation)
                            Assert.True(flips = 2 AndAlso check.Checked = start, lbl & ": a click on the explanation never toggles")
                        End If
                        ClickOn(row)
                        Assert.True(flips = 2, lbl & ": a click on the row's own surface never toggles")

                        ' The keyboard route: Space on the focused native check box is its Click (the real key needs the foreground, so
                        ' the Click itself is raised).
                        Assert.True(check.TabStop AndAlso check.GetType() Is GetType(CheckBox), lbl & ": the box stays a focusable native check box")
                        ClickOn(check)
                        Assert.True(flips = 3 AndAlso check.Checked = Not start, lbl & ": the key route toggles once")
                        If field IsNot Nothing Then Assert.Equal(Not check.Checked, field.UseSystemPasswordChar)
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_check_boxes_keep_their_names_defaults_and_stand_in_one_row_without_a_help_button()
        InLanguage("en",
            Sub()
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                        Dim row As OptionRowHost = RowOf(check)
                        Dim lbl As String = subject.ToString()
                        Assert.True(check.Checked = subject.DefaultChecked, lbl & ": Checked default")
                        Assert.Equal(subject.CheckName & "_Row", row.Name)
                        Assert.Equal(subject.CheckName & "_Caption", row.Caption.Name)
                        Assert.True(SettingsCheckBox.IsAttached(check), lbl & ": the box is the drawn Settings box")
                        Assert.Equal(String.Empty, check.Text)
                        Assert.NotEqual(String.Empty, row.Caption.Text)
                        Assert.Equal(If(subject.ExplanationName.Length > 0, 3, 2), row.Controls.Count)
                        Assert.Null(OptionHeading.Find(check))
                        If subject.ExplanationName.Length > 0 Then Assert.Equal(subject.ExplanationName, row.Explanation.Name)
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_disabled_row_never_toggles_and_toggles_again_when_enabled()
        InLanguage("en",
            Sub()
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                        Dim row As OptionRowHost = RowOf(check)
                        Dim lbl As String = subject.ToString()
                        Dim flips As Integer = 0
                        AddHandler check.CheckedChanged, Sub() flips += 1
                        Dim start As Boolean = check.Checked
                        check.Enabled = False
                        Application.DoEvents()
                        Assert.False(row.Caption.Enabled, lbl & ": the caption is disabled with the box")
                        ClickOn(row.Caption)
                        Assert.False(check.Enabled)
                        Assert.True(flips = 0 AndAlso check.Checked = start, lbl & ": a disabled row does not toggle")
                        check.Enabled = True
                        Application.DoEvents()
                        Assert.True(row.Caption.Enabled, lbl & ": the caption is enabled again")
                        ClickOn(row.Caption)
                        Assert.True(flips = 1 AndAlso check.Checked = Not start, lbl & ": it toggles again once enabled")
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_disabled_box_paints_dimmed_in_a_light_and_a_dark_palette_without_an_error()
        InLanguage("en",
            Sub()
                Dim subject As Subject = Subjects().First()
                Using shown As Shown = Open(subject, 1.0F)
                    Dim row As OptionRowHost = RowOf(FindCheck(shown.Form, subject.CheckName))
                    Dim check As CheckBox = row.Check
                    ' Light: the editor fill (white) is LIGHTER than the grey surface, the case Byte arithmetic used to overflow on.
                    row.ApplyColors(Color.FromArgb(240, 240, 240), Color.White, Color.FromArgb(120, 120, 120), Color.FromArgb(20, 20, 20), Color.FromArgb(0, 90, 200), Color.White)
                    Application.DoEvents()
                    Dim enabledFill As Color = CenterPixel(check)
                    check.Enabled = False
                    Application.DoEvents()
                    Dim lightDisabled As Color = CenterPixel(check)
                    Assert.True(lightDisabled.R > 240 AndAlso lightDisabled.R < enabledFill.R, "the disabled fill is dimmed toward the surface (" & lightDisabled.ToString() & " vs " & enabledFill.ToString() & ")")
                    check.Checked = True
                    Application.DoEvents()
                    Dim checkedDisabled As Color = CenterPixel(check)
                    Assert.NotEqual(Color.FromArgb(0, 90, 200).ToArgb(), checkedDisabled.ToArgb())
                    ' Dark: the fill is DARKER than the surface.
                    row.ApplyColors(Color.FromArgb(39, 41, 45), Color.FromArgb(20, 20, 24), Color.FromArgb(177, 181, 189), Color.FromArgb(244, 245, 247), Color.FromArgb(200, 60, 60), Color.FromArgb(250, 250, 250))
                    check.Checked = False
                    Application.DoEvents()
                    Dim darkDisabled As Color = CenterPixel(check)
                    Assert.True(darkDisabled.R > 20 AndAlso darkDisabled.R < 39, "dimmed toward the dark surface (" & darkDisabled.ToString() & ")")
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_tab_order_of_each_window_is_the_one_it_had()
        InLanguage("en",
            Sub()
                Dim found As New List(Of String)()
                For Each subject As Subject In Subjects()
                    If subject.Tab.Length = 0 Then Continue For
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim watched As HashSet(Of String) = New HashSet(Of String)(subject.Tab.Split(","c), StringComparer.Ordinal)
                        Dim all As List(Of String) = TabTokens(shown.Form)
                        Dim actual As String = String.Join(",", all.Where(Function(t) watched.Contains(t)))
                        If Not String.Equals(actual, subject.Tab, StringComparison.Ordinal) Then
                            found.Add(subject.ToString() & ": expected " & subject.Tab & " but the window tabs " & actual & " (all: " & String.Join(",", all) & ")")
                        End If
                    End Using
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    ''' <summary>The controls Tab visits, in order: the window's own walk (TabIndex, nested), keeping those that take the focus.</summary>
    Private Shared Function TabTokens(form As Form) As List(Of String)
        Dim tokens As New List(Of String)()
        ' The devices section is a container control of its own: Tab walks its children from inside it.
        Dim hosted As DevicesForm = TryCast(form, DevicesForm)
        Dim root As Control = If(hosted Is Nothing, DirectCast(form, Control), hosted.Devices)
        Dim current As Control = root
        For guard As Integer = 0 To 400
            current = root.GetNextControl(current, True)
            If current Is Nothing Then Exit For
            If current.TabStop AndAlso current.Visible AndAlso current.Enabled Then
                tokens.Add(DescribeControl(current))
            End If
        Next
        Return tokens
    End Function

    <Theory>
    <InlineData("en", False)>
    <InlineData("ar", True)>
    Public Sub The_box_leads_at_the_reading_start_and_mirrors_under_right_to_left(code As String, isRtl As Boolean)
        InLanguage(code,
            Sub()
                Assert.Equal(isRtl, Localization.IsRightToLeft())
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                        Dim row As OptionRowHost = RowOf(check)
                        Dim lbl As String = Mark(subject, code, 1.0F)
                        Assert.Equal(isRtl, row.RightToLeft = RightToLeft.Yes)
                        If isRtl Then
                            Assert.True(check.Left > row.ClientSize.Width \ 2, lbl & ": the box stands at the right")
                            Assert.True(row.Caption.Right <= check.Left + 1, lbl & ": the caption reads to the left of the box")
                            Assert.Equal(RightToLeft.Yes, row.Caption.RightToLeft)
                            If row.Explanation IsNot Nothing Then Assert.True(row.Explanation.Right <= check.Left + 1, lbl & ": the explanation is beside the caption")
                        Else
                            Assert.True(check.Left < row.ClientSize.Width \ 2, lbl & ": the box stands at the left")
                            Assert.True(check.Right <= row.Caption.Left + 1, lbl & ": the caption reads to the right of the box")
                            If row.Explanation IsNot Nothing Then Assert.True(check.Right <= row.Explanation.Left + 1, lbl & ": the explanation is beside the caption")
                        End If
                        ' The visible box stands 8 px from its text (BOOLEAN-OPTION 2.0 section 2 rule 2), counted from the painted box, not from
                        ' the larger hit target around it.
                        Dim inset As Integer = (check.Width - SettingsCheckBox.BoxSide(1.0F)) \ 2
                        Dim boxLeft As Integer = check.Left + inset
                        Dim gap As Integer = If(isRtl, boxLeft - row.Caption.Right, row.Caption.Left - (boxLeft + SettingsCheckBox.BoxSide(1.0F)))
                        Assert.True(Math.Abs(gap - 8) <= 1, lbl & ": the visible box stands " & gap.ToString(CultureInfo.InvariantCulture) & " px from its text, not 8")
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_accessible_name_is_the_caption_and_the_description_the_explanation()
        InLanguage("en",
            Sub()
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, 1.0F)
                        Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                        Dim row As OptionRowHost = RowOf(check)
                        Dim lbl As String = subject.ToString()
                        Dim expectedDescription As String = If(row.Explanation Is Nothing, "", row.Explanation.Text)
                        Assert.Equal(row.Caption.Text, check.AccessibleName)
                        Assert.Equal(row.Caption.Text, check.AccessibilityObject.Name)
                        Assert.Equal(expectedDescription, If(check.AccessibleDescription, ""))
                        If subject.ExplanationName.Length > 0 Then Assert.True(expectedDescription.Length > 0, lbl & ": the explanation has text")
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub A_theme_change_repaints_the_box_with_the_palette_roles_the_theme_walk_hands_it()
        InLanguage("en",
            Sub()
                If SystemInformation.HighContrast Then Return ' the system colours rule there; the palette is not read
                Dim subject As Subject = Subjects().First(Function(s) s.CheckName = "resource_Reveal")
                Using shown As Shown = Open(subject, 1.0F)
                    Dim check As CheckBox = FindCheck(shown.Form, subject.CheckName)
                    Dim row As OptionRowHost = RowOf(check)

                    ' ThemeApplier.ApplyOne gives the row its surface and then calls DialogOptionRow.ApplyPalette (held by the source
                    ' scan below); the walk itself needs the application's theme service, which this suite does not link.
                    Dim darkSurface As Color = Color.FromArgb(32, 33, 36)
                    row.BackColor = darkSurface
                    DialogOptionRow.ApplyPalette(row, ThemePalette.Dark)
                    Application.DoEvents()
                    Assert.Equal(darkSurface.ToArgb(), CornerPixel(check).ToArgb())
                    Assert.Equal(ThemePalette.Dark.SurfaceRaised.ToArgb(), CenterPixel(check).ToArgb())
                    check.Checked = True
                    Application.DoEvents()
                    Dim darkChecked As Integer = CenterPixel(check).ToArgb()
                    Assert.True(darkChecked = ThemePalette.Dark.Accent.ToArgb() OrElse darkChecked = Color.FromArgb(0, 120, 215).ToArgb(), "the checked fill is the accent (or the 3:1 fallback blue)")
                    check.Checked = False

                    Dim lightSurface As Color = Color.FromArgb(243, 243, 243)
                    row.BackColor = lightSurface
                    DialogOptionRow.ApplyPalette(row, ThemePalette.Light)
                    Application.DoEvents()
                    Assert.Equal(lightSurface.ToArgb(), CornerPixel(check).ToArgb())
                    Assert.Equal(ThemePalette.Light.SurfaceRaised.ToArgb(), CenterPixel(check).ToArgb())
                    Assert.NotEqual(darkSurface.ToArgb(), CornerPixel(check).ToArgb())
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub A_hosted_row_that_owns_its_palette_paints_the_hosts_colours()
        InLanguage("en",
            Sub()
                If SystemInformation.HighContrast Then Return
                Dim subject As Subject = Subjects().First(Function(s) s.CheckName = "res_DevicesAuto")
                Using shown As Shown = Open(subject, 1.0F)
                    Dim row As OptionRowHost = RowOf(FindCheck(shown.Form, subject.CheckName))
                    Assert.True(row.OwnsPalette, "the panel's row is coloured by the panel, so the window's theme walk leaves it alone")
                    row.ApplyOwnPalette(Color.FromArgb(32, 33, 36), Color.White, Color.Silver, Color.FromArgb(48, 49, 52), Color.FromArgb(0, 120, 215), Color.White)
                    Application.DoEvents()
                    Assert.Equal(Color.FromArgb(32, 33, 36).ToArgb(), row.BackColor.ToArgb())
                    Assert.Equal(Color.FromArgb(32, 33, 36).ToArgb(), CornerPixel(row.Check).ToArgb())
                    Assert.Equal(Color.FromArgb(48, 49, 52).ToArgb(), CenterPixel(row.Check).ToArgb())
                    Assert.Equal(Color.White.ToArgb(), row.Caption.ForeColor.ToArgb())
                End Using
            End Sub)
    End Sub

    ' --- the source scan --------------------------------------------------------------------------------

    Private Shared Function FindSourceDirectory() As String
        Dim dir As DirectoryInfo = New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If File.Exists(Path.Combine(candidate, "Main_Form.Lifecycle.vb")) Then Return candidate
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src was not found above the test output")
    End Function

    Private Shared Function CodeOnly(path As String) As String
        Dim lines As IEnumerable(Of String) = File.ReadAllLines(path).Where(Function(l) Not l.TrimStart().StartsWith("'", StringComparison.Ordinal))
        Return String.Join(vbLf, lines)
    End Function

    ''' <summary>The text between the parentheses of every <c>Controls.Add(..)</c> / <c>Controls.AddRange(..)</c> call.</summary>
    Private Shared Function ControlsArguments(code As String) As List(Of String)
        Dim result As New List(Of String)()
        For Each m As Match In Regex.Matches(code, "\bControls\.(?:Add|AddRange)\(")
            Dim depth As Integer = 1
            Dim i As Integer = m.Index + m.Length
            Dim start As Integer = i
            While i < code.Length AndAlso depth > 0
                If code(i) = "("c Then depth += 1
                If code(i) = ")"c Then depth -= 1
                i += 1
            End While
            result.Add(code.Substring(start, Math.Max(0, i - start - 1)))
        Next
        Return result
    End Function

    <Fact>
    Public Sub No_window_adds_a_native_check_box_to_itself_every_one_goes_through_the_option_row()
        Dim src As String = FindSourceDirectory()
        Dim expected As New Dictionary(Of String, Integer)(StringComparer.Ordinal) From {
            {"Resource_Credential_Form.vb", 2}, {"Resource_Edit_Form.vb", 2}, {"Secure_Password_Form.vb", 2},
            {"Exchange_Password_Change_Form.vb", 1}, {"Exchange_Connect_Form.vb", 1}, {"LiveBroadcast_Form.vb", 2},
            {"Video\Video_Convert_Form.vb", 1}, {"Exchange_Devices_Panel.vb", 1}}
        Dim problems As New List(Of String)()
        Dim total As Integer = 0
        For Each pair As KeyValuePair(Of String, Integer) In expected
            Dim code As String = CodeOnly(Path.Combine(src, pair.Key))
            Dim made As Integer = Regex.Matches(code, "\bNew CheckBox\b").Count
            Dim rows As Integer = Regex.Matches(code, "\bDialogOptionRow\.Create(?:At)?\(").Count
            total += made
            If made <> pair.Value Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " native check boxes, expected " & pair.Value.ToString(CultureInfo.InvariantCulture))
            If rows <> made Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " check boxes but " & rows.ToString(CultureInfo.InvariantCulture) & " option rows")
            Dim names As New List(Of String)()
            For Each m As Match In Regex.Matches(code, "\b(?:Private|Dim|Friend|Public)\s+(?:ReadOnly\s+)?(\w+)\s+As\s+(?:New\s+)?CheckBox\b")
                names.Add(m.Groups(1).Value)
            Next
            For Each argument As String In ControlsArguments(code)
                For Each name As String In names
                    If Regex.IsMatch(argument, "\b" & Regex.Escape(name) & "\b") Then problems.Add(pair.Key & ": the check box " & name & " is added to a Controls collection directly")
                Next
            Next
            If code.IndexOf("#If", StringComparison.Ordinal) >= 0 AndAlso Not code.StartsWith("#If Not NETFRAMEWORK", StringComparison.Ordinal) Then
                ' the file is whole-file modern-only; a seam inside it would be a second runtime path nobody asked for
                problems.Add(pair.Key & ": carries a conditional-compilation seam of its own")
            End If
        Next
        Assert.Equal(12, total)
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub The_theme_walk_hands_a_dialog_row_to_the_option_row_palette()
        Dim source As String = File.ReadAllText(Path.Combine(FindSourceDirectory(), "Theming", "ThemeApplier.vb"))
        ' The walk knows only the seam (IThemedOptionRow), so it also compiles in the Share Manager; the row implements it.
        Assert.Contains("TryCast(c.Parent, IThemedOptionRow)", source, StringComparison.Ordinal)
        Assert.Contains("optionRow.ApplyThemePalette(palette)", source, StringComparison.Ordinal)
        Assert.Contains("Not optionRow.OwnsPalette", source, StringComparison.Ordinal)
        Dim row As String = File.ReadAllText(Path.Combine(FindSourceDirectory(), "DialogOptionRow.vb"))
        Assert.Contains("Implements IThemedOptionRow", row, StringComparison.Ordinal)
        Assert.Contains("DialogOptionRow.ApplyPalette(Me, palette)", row, StringComparison.Ordinal)
        Dim layout As String = File.ReadAllText(Path.Combine(FindSourceDirectory(), "DialogLayout.vb"))
        Assert.Contains("DialogOptionRow.IsRow(c)", layout, StringComparison.Ordinal)
    End Sub
End Class
#End If
