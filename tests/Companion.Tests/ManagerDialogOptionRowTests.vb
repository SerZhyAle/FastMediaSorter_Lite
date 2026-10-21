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
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BOOLEAN-OPTION 2.0 in the Share Manager's DIALOG windows (ticket 243, slice T-11c-b2a): the 17 check boxes of the broadcast
''' creation, the account administration, the exchange sign-in, the two password windows, the guided add, the package window, the
''' live broadcast window, the disk options and the broadcasts page stand in <see cref="OptionRowHost"/> rows made by
''' <see cref="OptionRows"/> - a drawn box of at least 20 px inside a 32 px hit target at the reading start, a caption that
''' toggles once, an explanation that never toggles, and a row as tall as its content. Held here: the source (no native check
''' box is placed directly, every window arranges its rows once it has its scale), the real windows at 100 % and 175 % in
''' en, ru and ar (nothing clipped, nothing overlapped, no hit target reaching a neighbour), the click, and the windows' own
''' show/hide of a row (the whole row hides, nothing is left behind and no cell shifts).
''' The scale is EMULATED the way the other layout tests do it: a larger font plus <c>Control.Scale</c>; how a window LOOKS
''' under a real pointer on a 150-200 % monitor, in High Contrast or under Narrator stays manual acceptance. The collection is
''' the scheduled one (no parallelism): the windows read the settings hive, which a scratch hive stands in for.
''' The windows that talk to the outside (the broadcasts page, the package window) are shown WITHOUT the message loop
''' delivering their first Shown event, and the gateway switch is neutralised before a click, so no test reaches the worker.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ManagerDialogOptionRowTests

    ' --- the subjects ----------------------------------------------------------------------------------

    ''' <summary>One window state with the check boxes it holds (field name and Checked value in a fresh window).</summary>
    Private NotInheritable Class Subject
        Friend ReadOnly Window As String
        Friend ReadOnly Make As Func(Of Kit, Form)
        Friend ReadOnly Boxes As List(Of KeyValuePair(Of String, Boolean))
        Friend ReadOnly Setup As Action(Of Form)
        ''' <summary>False for a window whose first Shown event reaches the outside: it is shown and laid out without the message loop.</summary>
        Friend ReadOnly Pump As Boolean
        ''' <summary>Makes the window's own handlers harmless before a box is clicked (the gateway switch talks to the worker).</summary>
        Friend ReadOnly Quiet As Action(Of Form)

        Friend Sub New(window As String, make As Func(Of Kit, Form), boxes As List(Of KeyValuePair(Of String, Boolean)),
                       Optional setup As Action(Of Form) = Nothing, Optional pump As Boolean = True, Optional quiet As Action(Of Form) = Nothing)
            Me.Window = window
            Me.Make = make
            Me.Boxes = boxes
            Me.Setup = setup
            Me.Pump = pump
            Me.Quiet = quiet
        End Sub

        Public Overrides Function ToString() As String
            Return Window
        End Function
    End Class

    Private Shared Function Pair(key As String, checkedAtStart As Boolean) As KeyValuePair(Of String, Boolean)
        Return New KeyValuePair(Of String, Boolean)(key, checkedAtStart)
    End Function

    Private Shared Function Boxes(ParamArray pairs As KeyValuePair(Of String, Boolean)()) As List(Of KeyValuePair(Of String, Boolean))
        Return New List(Of KeyValuePair(Of String, Boolean))(pairs)
    End Function

    Private Const KeptFlags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public

    Private Shared Function Fld(Of T)(target As Object, name As String) As T
        Dim info As FieldInfo = target.GetType().GetField(name, KeptFlags)
        Assert.True(info IsNot Nothing, name & " not found on " & target.GetType().Name)
        Return DirectCast(info.GetValue(target), T)
    End Function

    Private Shared Function Call_(target As Object, name As String, ParamArray args As Object()) As Object
        Return target.GetType().GetMethod(name, BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(target, args)
    End Function

    ''' <summary>The guided add of the exchange entry, on its last step (the step that holds the publish row).</summary>
    Private Shared Function ExchangeFinishStep() As Form
        Dim form As New Resource_Add_Form(New ResourceAddTests.Actions(), ResourceAddEntry.Exchange, currentPublish:=True)
        Call_(form, "AcceptPick", "C:\ResourceScene")
        Call_(form, "ShowStep", ResourceAddStep.Questions)
        DirectCast(Call_(form, "AdvanceAsync"), Task).GetAwaiter().GetResult()
        Return form
    End Function

    Private Shared Function BroadcastFolder() As Subject
        Return New Subject("Broadcast_Create_Form (folder)",
                           Function(kit As Kit) New Broadcast_Create_Form(AppContext.BaseDirectory, New ShareSettings With {.LanOnlyExport = False}),
                           Boxes(Pair("chkRecursive", False), Pair("chkExternal", True)))
    End Function

    Private Shared Function AccountSignIn() As Subject
        Return New Subject("Exchange_Account_Form",
                           Function(kit As Kit) New Exchange_Account_Form(kit.NewAgent(), "", "", True),
                           Boxes(Pair("_chkShow", False), Pair("_chkPublish", True), Pair("_chkStart", False)))
    End Function

    Private Shared Function LiveWindow() As Subject
        Return New Subject("Live_Broadcast_Form (screen)",
                           Function(kit As Kit) New Live_Broadcast_Form(),
                           Boxes(Pair("_chkCursor", True), Pair("_chkNoSound", False), Pair("_chkAllowLanViewers", False)),
                           Sub(form As Form) Fld(Of RadioButton)(form, "_rbScreen").Checked = True)
    End Function

    Private Shared Function GatewayPage() As Subject
        Return New Subject("Share_Broadcasts_Form",
                           Function(kit As Kit) New Share_Broadcasts_Form(),
                           Boxes(Pair("chkEnableGateway", False)), Nothing, False,
                           Sub(form As Form) SetField(form, "_degraded", True))
    End Function

    Private Shared Sub SetField(target As Object, name As String, value As Object)
        target.GetType().GetField(name, KeptFlags).SetValue(target, value)
    End Sub

    Private Shared Function Subjects() As List(Of Subject)
        Return New List(Of Subject) From {
            BroadcastFolder(),
            New Subject("Broadcast_Create_Form (file)",
                        Function(kit As Kit) New Broadcast_Create_Form("C:\Media\film.mp4", New ShareSettings With {.LanOnlyExport = True}),
                        Boxes(Pair("chkExternal", False))),
            New Subject("Exchange_Account_Admin_Form (reset)",
                        Function(kit As Kit) New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, "alice"),
                        Boxes(Pair("_chkSignOut", True))),
            AccountSignIn(),
            New Subject("Exchange_Password_Change_Form",
                        Function(kit As Kit) New Exchange_Password_Change_Form("user@exchange.example"),
                        Boxes(Pair("_chkShow", False))),
            New Subject("Exchange_Password_Form (enter)",
                        Function(kit As Kit) New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter),
                        Boxes(Pair("_chkShow", False))),
            New Subject("Resource_Add_Form (exchange, last step)",
                        Function(kit As Kit) ExchangeFinishStep(),
                        Boxes(Pair("publish", True))),
            New Subject("PackageWizardForm",
                        Function(kit As Kit) New PackageWizardForm(New List(Of String)()),
                        Boxes(Pair("chkLanOnly", False), Pair("chkNoPassword", False)), Nothing, False),
            LiveWindow(),
            New Subject("DiskOptionsForm (register)",
                        Function(kit As Kit) New DiskOptionsForm(True, True),
                        Boxes(Pair("ReadOnlyAccess", False), Pair("OpenNow", True))),
            GatewayPage()}
    End Function

    ''' <summary>What the windows need: a scratch settings hive, an empty store of folder parameters, the exchange gate on (the
    ''' guided add of the exchange entry refuses to exist without it) and, on request, a sign-in agent over a scratch folder.</summary>
    Private NotInheritable Class Kit
        Implements IDisposable

        Private Shared ReadOnly MapField As FieldInfo = GetType(ShareRootParamsStore).GetField("_map", BindingFlags.NonPublic Or BindingFlags.Static)

        Friend ReadOnly Hive As New ScratchHive()
        Private ReadOnly oldMap As Object
        Private ReadOnly gate As IDisposable
        Private ReadOnly agents As New List(Of ExchangeDeviceAgent)()
        Private rig As ExchangeDeviceAgentTests.Rig

        Friend Sub New()
            oldMap = MapField.GetValue(Nothing)
            MapField.SetValue(Nothing, Nothing)
            gate = ExchangeFeatures.ForFlow(switchOn:=True)
        End Sub

        Friend Function NewAgent() As ExchangeDeviceAgent
            If rig Is Nothing Then rig = New ExchangeDeviceAgentTests.Rig()
            Dim agent As ExchangeDeviceAgent = rig.NewAgent()
            agents.Add(agent)
            Return agent
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each agent As ExchangeDeviceAgent In agents
                agent.Dispose()
            Next
            If rig IsNot Nothing Then rig.Dispose()
            gate.Dispose()
            MapField.SetValue(Nothing, oldMap)
            Hive.Dispose()
        End Sub
    End Class

    ' --- helpers ---------------------------------------------------------------------------------------

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

    ''' <summary>Runs a check on an STA thread with the UI language set and everything put back afterwards. A control that throws
    ''' while it paints or handles a message fails the test instead of opening WinForms' modal "unhandled exception" box.</summary>
    Private Shared Sub InLanguage(code As String, check As Action(Of Kit))
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Dim raised As New List(Of Exception)()
                  Dim handler As ThreadExceptionEventHandler = Sub(sender As Object, e As ThreadExceptionEventArgs) raised.Add(e.Exception)
                  Try
                      Localization.CurrentCode = code
                      ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
                      AddHandler Application.ThreadException, handler
                      Using kit As New Kit()
                          check(kit)
                      End Using
                  Finally
                      RemoveHandler Application.ThreadException, handler
                      Localization.CurrentCode = saved
                  End Try
                  Assert.True(raised.Count = 0, "a control raised while painting or handling a message: " & String.Join(vbLf, raised.Select(Function(x) x.ToString())))
              End Sub)
    End Sub

    Private Shared Function AllOf(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each c As Control In parent.Controls
            found.Add(c)
            found.AddRange(AllOf(c))
        Next
        Return found
    End Function

    ''' <summary>A window shown at a scale, released together with what it needed.</summary>
    Private NotInheritable Class Shown
        Implements IDisposable

        Friend ReadOnly Form As Form

        Friend Sub New(form As Form)
            Me.Form = form
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Form.Close()
            Finally
                Form.Dispose()
            End Try
        End Sub
    End Class

    ''' <summary>Builds the subject's window in the current language, emulates a display scale, shows it (OnLoad arranges the rows)
    ''' and applies the subject's set-up.</summary>
    Private Shared Function Open(subject As Subject, kit As Kit, factor As Single) As Shown
        Dim form As Form = subject.Make(kit)
        Try
            Dim extra As Single = factor / (form.DeviceDpi / 96.0F)
            If Math.Abs(extra - 1.0F) > 0.01F Then
                form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
                form.Scale(New SizeF(extra, extra))
            End If
            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(20, 20)
            form.Show()
            If subject.Pump Then Application.DoEvents()
            If subject.Setup IsNot Nothing Then
                subject.Setup(form)
                If subject.Pump Then Application.DoEvents()
            End If
            Return New Shown(form)
        Catch
            form.Dispose()
            Throw
        End Try
    End Function

    Private Shared Function Mark(subject As Subject, code As String, factor As Single) As String
        Return subject.Window & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
    End Function

    ''' <summary>The check box a subject keeps in a field.</summary>
    Private Shared Function CheckOf(form As Form, key As String) As CheckBox
        Return Fld(Of CheckBox)(form, key)
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

    ''' <summary>The part of a control that can be reached with a pointer: its rectangle cut by every container between it and the window.</summary>
    Private Shared Function ClippedOnForm(form As Form, c As Control) As Rectangle
        Dim visible As Rectangle = OnForm(form, c)
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            visible = Rectangle.Intersect(visible, OnForm(form, p))
            p = p.Parent
        End While
        Return visible
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

    ''' <summary>True when a label's wrapped text fits the height the label has.</summary>
    Private Shared Function TextFits(l As Label) As Boolean
        If l Is Nothing OrElse Not l.Visible OrElse l.Text.Length = 0 Then Return True
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(l.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim need As Integer = TextRenderer.MeasureText(l.Text, l.Font, New Size(Math.Max(1, l.Width - l.Padding.Horizontal), Integer.MaxValue), flags).Height
        Return need <= l.Height + 2
    End Function

    Private Shared Function Describe(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name & "(" & c.Text & ")", c.Name)
    End Function

    ' --- the audit of a shown window -------------------------------------------------------------------

    ''' <summary>The sentences that are wrong with the rows of a shown window (nothing added = sound).</summary>
    Private Shared Sub Audit(subject As Subject, form As Form, lbl As String, factor As Single, found As List(Of String))
        Dim rtl As Boolean = (form.RightToLeft = RightToLeft.Yes)
        Dim rows As New List(Of OptionRowHost)()
        Dim names As New Dictionary(Of OptionRowHost, String)()
        For Each box As KeyValuePair(Of String, Boolean) In subject.Boxes
            Dim row As OptionRowHost = TryCast(CheckOf(form, box.Key).Parent, OptionRowHost)
            If row Is Nothing Then
                found.Add(lbl & " " & box.Key & ": does not stand in an option row")
            Else
                rows.Add(row)
                names(row) = box.Key
            End If
        Next

        For Each row As OptionRowHost In rows
            Dim check As CheckBox = row.Check
            Dim tag As String = lbl & " " & names(row)
            If Not row.Visible Then
                found.Add(tag & ": the row is not visible")
                Continue For
            End If

            ' The row inside the container that holds it, and over none of its neighbours.
            Dim host As Control = row.Parent
            Dim client As Rectangle = host.ClientRectangle
            ' A body that scrolls (a window capped by a small working area) holds its rows in the scroll extent, not the viewport.
            Dim scrolling As ScrollableControl = TryCast(host, ScrollableControl)
            If scrolling IsNot Nothing AndAlso scrolling.AutoScroll Then
                client = New Rectangle(0, 0, Math.Max(client.Width, scrolling.HorizontalScroll.Maximum + 1), Math.Max(client.Height, scrolling.VerticalScroll.Maximum + 1))
            End If
            If row.Left < -1 OrElse row.Top < -1 OrElse row.Right > client.Width + 1 OrElse row.Bottom > client.Height + 1 Then
                found.Add(tag & ": the row " & row.Bounds.ToString() & " leaves its container " & client.Size.ToString())
            End If
            For Each s As Control In host.Controls
                If Object.ReferenceEquals(s, row) OrElse Not s.Visible Then Continue For
                Dim overlap As Rectangle = Rectangle.Intersect(row.Bounds, s.Bounds)
                If overlap.Width > 1 AndAlso overlap.Height > 1 Then found.Add(tag & ": the row " & row.Bounds.ToString() & " lies over " & Describe(s) & " " & s.Bounds.ToString())
            Next

            ' Inside the row: the box, the caption and the explanation stay in it, none on another, no text cut.
            Dim inner As New List(Of Control) From {check, row.Caption}
            If row.Explanation IsNot Nothing AndAlso row.Explanation.Visible Then inner.Add(row.Explanation)
            Dim rowClient As Rectangle = row.ClientRectangle
            For i As Integer = 0 To inner.Count - 1
                Dim c As Control = inner(i)
                If c.Left < -1 OrElse c.Top < -1 OrElse c.Right > rowClient.Width + 1 OrElse c.Bottom > rowClient.Height + 1 Then
                    found.Add(tag & ": " & Describe(c) & " " & c.Bounds.ToString() & " leaves its row " & rowClient.Size.ToString())
                End If
                For j As Integer = i + 1 To inner.Count - 1
                    Dim overlap As Rectangle = Rectangle.Intersect(c.Bounds, inner(j).Bounds)
                    If overlap.Width > 1 AndAlso overlap.Height > 1 Then found.Add(tag & ": " & Describe(c) & " " & c.Bounds.ToString() & " lies over " & Describe(inner(j)) & " " & inner(j).Bounds.ToString())
                Next
            Next
            If Not TextFits(row.Caption) Then found.Add(tag & ": the caption '" & row.Caption.Text & "' is cut (" & row.Caption.Height.ToString(CultureInfo.InvariantCulture) & " px high, " & row.Caption.Width.ToString(CultureInfo.InvariantCulture) & " wide)")
            If Not TextFits(row.Explanation) Then found.Add(tag & ": the explanation is cut")
            If row.Caption.Bottom > row.Height OrElse (row.Explanation IsNot Nothing AndAlso row.Explanation.Visible AndAlso row.Explanation.Bottom > row.Height) Then
                found.Add(tag & ": the row is " & row.Height.ToString(CultureInfo.InvariantCulture) & " px high and its text reaches below it")
            End If

            ' The row is as tall as its content needs: asking it again changes nothing.
            Dim before As Integer = row.Height
            DialogOptionRow.NeededHeight(row)
            If Math.Abs(row.Height - before) > 1 Then found.Add(tag & ": the row was " & before.ToString(CultureInfo.InvariantCulture) & " px high and needs " & row.Height.ToString(CultureInfo.InvariantCulture))
            If check.Width < SettingsCheckBox.Px(UiSizing.PointerTarget, factor) - 1 Then found.Add(tag & ": the hit target is narrower than 32 px at this scale")

            ' The box leads at the reading start, 8 px from its text.
            If rtl Then
                If row.Caption.Right > check.Left + 1 Then found.Add(tag & ": the caption does not read to the left of the box")
            Else
                If check.Right > row.Caption.Left + 1 Then found.Add(tag & ": the caption does not read to the right of the box")
            End If
            Dim inset As Integer = (check.Width - SettingsCheckBox.BoxSide(factor)) \ 2
            Dim boxLeft As Integer = check.Left + inset
            Dim gap As Integer = If(rtl, boxLeft - row.Caption.Right, row.Caption.Left - (boxLeft + SettingsCheckBox.BoxSide(factor)))
            If Math.Abs(gap - SettingsCheckBox.Px(8, factor)) > 2 Then found.Add(tag & ": the visible box stands " & gap.ToString(CultureInfo.InvariantCulture) & " px from its text, not 8 logical px")
        Next

        ' Hit areas: every check box's target against every other target and every other control that takes a pointer.
        Dim targets As New List(Of KeyValuePair(Of String, Rectangle))()
        For Each row As OptionRowHost In rows
            If row.Visible Then targets.Add(New KeyValuePair(Of String, Rectangle)(names(row), ClippedOnForm(form, row.Check)))
        Next
        For i As Integer = 0 To targets.Count - 1
            For j As Integer = i + 1 To targets.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, targets(j).Value)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(lbl & ": the hit target of " & targets(i).Key & " " & targets(i).Value.ToString() & " overlaps " & targets(j).Key & " " & targets(j).Value.ToString())
            Next
            For Each c As Control In AllOf(form)
                If Not c.Visible OrElse TypeOf c.Parent Is OptionRowHost Then Continue For
                If Not (TypeOf c Is ButtonBase OrElse TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown OrElse TypeOf c Is DateTimePicker) Then Continue For
                Dim reach As Rectangle = ClippedOnForm(form, c)
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, reach)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(lbl & ": the hit target of " & targets(i).Key & " " & targets(i).Value.ToString() & " overlaps " & Describe(c) & " " & reach.ToString())
            Next
        Next

        ' The window stays on its monitor (a window that is resizable and fixed in size is left to the monitor it is opened on),
        ' and a row is never beyond the window's width.
        Dim area As Rectangle = Screen.FromControl(form).WorkingArea
        If form.FormBorderStyle <> FormBorderStyle.Sizable Then
            If form.Width > area.Width OrElse form.Height > area.Height Then
                found.Add(lbl & ": the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
            End If
        End If
        For Each row As OptionRowHost In rows
            Dim at As Rectangle = OnForm(form, row)
            If at.Left < -1 OrElse at.Right > form.ClientSize.Width + 1 Then
                found.Add(lbl & ": " & names(row) & " " & at.ToString() & " is beyond the window width " & form.ClientSize.Width.ToString(CultureInfo.InvariantCulture))
            End If
        Next
    End Sub

    ' --- the subjects are what they were -------------------------------------------------------------

    <Fact>
    Public Sub The_seventeen_check_boxes_stand_in_option_rows_and_keep_their_defaults()
        InLanguage("en",
            Sub(kit As Kit)
                Dim total As Integer = 0
                Dim found As New List(Of String)()
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        For Each box As KeyValuePair(Of String, Boolean) In subject.Boxes
                            Dim check As CheckBox = CheckOf(shown.Form, box.Key)
                            Dim lbl As String = subject.Window & "/" & box.Key
                            Dim row As OptionRowHost = TryCast(check.Parent, OptionRowHost)
                            If row Is Nothing Then
                                found.Add(lbl & ": not in an option row")
                                Continue For
                            End If
                            If check.Checked <> box.Value Then found.Add(lbl & ": Checked is " & check.Checked.ToString() & ", the window's default is " & box.Value.ToString())
                            If Not check.Enabled Then found.Add(lbl & ": starts disabled")
                            If Not String.Equals(row.Caption.Text, row.Check.AccessibleName, StringComparison.Ordinal) Then found.Add(lbl & ": the accessible name is not the caption")
                            If row.Caption.Text.Length = 0 Then found.Add(lbl & ": the caption is empty")
                            If Not check.TabStop Then found.Add(lbl & ": the box is not a tab stop")
                        Next
                    End Using
                Next
                ' The subjects hold each of the 17 boxes of the slice once (the two Broadcast_Create_Form states share chkExternal).
                Dim distinct As New HashSet(Of String)(StringComparer.Ordinal)
                For Each subject As Subject In Subjects()
                    Dim windowType As String = subject.Window.Split(" "c)(0)
                    For Each box As KeyValuePair(Of String, Boolean) In subject.Boxes
                        distinct.Add(windowType & "/" & box.Key)
                    Next
                Next
                total = distinct.Count
                Assert.True(total = 17, "the subjects hold " & total.ToString(CultureInfo.InvariantCulture) & " distinct check boxes, expected 17")
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    <Fact>
    Public Sub The_box_is_at_least_20_px_and_its_hit_target_at_least_32_px_at_100_and_175_percent()
        InLanguage("en",
            Sub(kit As Kit)
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, kit, factor)
                            For Each box As KeyValuePair(Of String, Boolean) In subject.Boxes
                                Dim check As CheckBox = CheckOf(shown.Form, box.Key)
                                Dim row As OptionRowHost = RowOf(check)
                                Dim lbl As String = Mark(subject, "en", factor) & " " & box.Key
                                Dim seen As Single = check.Width / CSng(UiSizing.PointerTarget)
                                If Math.Abs(seen - factor) > 0.05F Then found.Add(lbl & ": the row is laid out at " & seen.ToString("0.00", CultureInfo.InvariantCulture) & ", not at the emulated scale")
                                Dim target As Integer = SettingsCheckBox.Px(UiSizing.PointerTarget, factor)
                                If check.Width < target - 1 OrElse check.Height < target - 1 Then found.Add(lbl & ": the hit target is " & check.Size.ToString() & ", needs " & target.ToString(CultureInfo.InvariantCulture))
                                Dim side As Integer = SettingsCheckBox.Px(20, factor)
                                Dim painted As Rectangle = PaintedBox(row)
                                If painted.Width < side OrElse painted.Height < side Then found.Add(lbl & ": the painted box is " & painted.Size.ToString() & ", needs " & side.ToString(CultureInfo.InvariantCulture))
                            Next
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    <Theory>
    <InlineData("en")>
    <InlineData("ru")>
    <InlineData("ar")>
    Public Sub No_row_clips_or_overlaps_and_no_hit_target_reaches_a_neighbour_at_100_and_175_percent(code As String)
        InLanguage(code,
            Sub(kit As Kit)
                Dim found As New List(Of String)()
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    For Each subject As Subject In Subjects()
                        Using shown As Shown = Open(subject, kit, factor)
                            Audit(subject, shown.Form, Mark(subject, code, factor), factor, found)
                        End Using
                    Next
                Next
                Assert.True(found.Count = 0, String.Join(vbLf, found))
            End Sub)
    End Sub

    ' --- what a click does ----------------------------------------------------------------------------

    <Fact>
    Public Sub A_click_on_the_caption_toggles_once_a_click_on_the_explanation_never_does_and_the_key_route_toggles()
        InLanguage("en",
            Sub(kit As Kit)
                For Each subject As Subject In Subjects()
                    Using shown As Shown = Open(subject, kit, 1.0F)
                        Dim form As Form = shown.Form
                        If subject.Quiet IsNot Nothing Then subject.Quiet(form)
                        For Each box As KeyValuePair(Of String, Boolean) In subject.Boxes
                            Dim check As CheckBox = CheckOf(form, box.Key)
                            Dim row As OptionRowHost = RowOf(check)
                            Dim lbl As String = subject.Window & "/" & box.Key
                            Dim flips As Integer = 0
                            Dim counter As EventHandler = Sub(sender As Object, e As EventArgs) flips += 1
                            AddHandler check.CheckedChanged, counter
                            Dim start As Boolean = check.Checked

                            Assert.True(Object.ReferenceEquals(Cursors.Hand, row.Caption.Cursor), lbl & ": the caption has the hand cursor")
                            ClickOn(row.Caption)
                            Assert.True(flips = 1, lbl & ": one click on the caption flips the box exactly once, not " & flips.ToString(CultureInfo.InvariantCulture))
                            Assert.Equal(Not start, check.Checked)
                            ClickOn(row.Caption)
                            Assert.True(flips = 2 AndAlso check.Checked = start, lbl & ": a second click flips it back")

                            If row.Explanation IsNot Nothing AndAlso row.Explanation.Text.Length > 0 Then
                                Assert.False(Object.ReferenceEquals(Cursors.Hand, row.Explanation.Cursor), lbl & ": the explanation is not a hand")
                                ClickOn(row.Explanation)
                                ClickOn(row.Explanation)
                                Assert.True(flips = 2 AndAlso check.Checked = start, lbl & ": a click on the explanation never toggles")
                            End If
                            ClickOn(row)
                            Assert.True(flips = 2, lbl & ": a click on the row's own surface never toggles")

                            ' The keyboard route: Space on the focused native check box is its Click; the box stays a focusable stock CheckBox.
                            Assert.True(check.TabStop AndAlso check.GetType() Is GetType(CheckBox), lbl & ": the box stays a focusable native check box")
                            ClickOn(check)
                            Assert.True(flips = 3 AndAlso check.Checked = Not start, lbl & ": the key route toggles once")

                            RemoveHandler check.CheckedChanged, counter
                            If check.Checked <> start Then check.Checked = start
                            Application.DoEvents()
                        Next
                    End Using
                Next
            End Sub)
    End Sub

    ' --- what the windows do with a row -----------------------------------------------------------------

    <Fact>
    Public Sub The_internet_warning_is_the_explanation_of_the_internet_row_and_shows_only_while_the_option_is_on()
        InLanguage("en",
            Sub(kit As Kit)
                For Each factor As Single In New Single() {1.0F, 1.75F}
                    Dim subject As Subject = BroadcastFolder()
                    Using shown As Shown = Open(subject, kit, factor)
                        Dim form As Form = shown.Form
                        Dim lbl As String = Mark(subject, "en", factor)
                        Dim check As CheckBox = CheckOf(form, "chkExternal")
                        Dim row As OptionRowHost = RowOf(check)
                        Dim found As New List(Of String)()
                        Assert.True(check.Checked, lbl & ": the internet option starts on")
                        Assert.True(Object.ReferenceEquals(Fld(Of Label)(form, "lblWarning"), row.Explanation), lbl & ": the warning is the row's explanation")
                        Assert.True(row.Explanation.Visible AndAlso row.Explanation.Text.Length > 0, lbl & ": the warning shows while the option is on")
                        Assert.True(TextFits(row.Explanation), lbl & ": the warning is cut")
                        Dim tall As Integer = row.Height
                        Audit(subject, form, lbl & " on", factor, found)

                        check.Checked = False
                        Application.DoEvents()
                        Assert.True(row.Explanation.Text.Length = 0 AndAlso Not row.Explanation.Visible, lbl & ": the warning goes with the option")
                        Assert.True(row.Height < tall, lbl & ": the row gives the warning's room back (" & row.Height.ToString(CultureInfo.InvariantCulture) & " vs " & tall.ToString(CultureInfo.InvariantCulture) & ")")
                        Audit(subject, form, lbl & " off", factor, found)

                        check.Checked = True
                        Application.DoEvents()
                        Assert.True(row.Explanation.Visible AndAlso row.Explanation.Text.Length > 0, lbl & ": the warning is back")
                        Assert.True(Math.Abs(row.Height - tall) <= 1, lbl & ": the row is as tall as before")
                        Audit(subject, form, lbl & " on again", factor, found)
                        Assert.True(found.Count = 0, String.Join(vbLf, found))
                    End Using
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub The_sign_in_hides_the_whole_show_password_row_in_the_code_mode_and_nothing_below_it_shifts_sideways()
        InLanguage("en",
            Sub(kit As Kit)
                Dim subject As Subject = AccountSignIn()
                Using shown As Shown = Open(subject, kit, 1.0F)
                    Dim form As Form = shown.Form
                    Dim show As OptionRowHost = RowOf(CheckOf(form, "_chkShow"))
                    Dim publish As OptionRowHost = RowOf(CheckOf(form, "_chkPublish"))
                    Assert.True(show.Visible, "the show-password row starts visible in the password mode")
                    Dim publishAt As Rectangle = OnForm(form, publish)

                    Fld(Of RadioButton)(form, "_rbCode").Checked = True
                    Application.DoEvents()
                    Assert.False(show.Visible, "the whole row hides in the code mode")
                    Assert.False(show.Caption.Visible, "no caption is left behind by the hidden box")
                    Dim moved As Rectangle = OnForm(form, publish)
                    Assert.True(moved.Top < publishAt.Top, "what stands under the hidden row moves up")
                    Assert.True(Math.Abs(moved.Left - publishAt.Left) <= 1, "and does not move sideways")

                    Fld(Of RadioButton)(form, "_rbPassword").Checked = True
                    Application.DoEvents()
                    Assert.True(show.Visible, "the row is back in the password mode")
                    Dim back As Rectangle = OnForm(form, publish)
                    Assert.True(Math.Abs(back.Top - publishAt.Top) <= 1 AndAlso Math.Abs(back.Left - publishAt.Left) <= 1, "and everything stands where it stood")
                    Dim found As New List(Of String)()
                    Audit(subject, form, subject.Window, 1.0F, found)
                    Assert.True(found.Count = 0, String.Join(vbLf, found))
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_live_window_shows_the_lan_row_for_the_video_sources_only_and_keeps_its_cell()
        InLanguage("en",
            Sub(kit As Kit)
                Dim subject As Subject = New Subject("Live_Broadcast_Form", Function(k As Kit) New Live_Broadcast_Form(), Boxes(Pair("_chkAllowLanViewers", False)))
                Using shown As Shown = Open(subject, kit, 1.0F)
                    Dim form As Form = shown.Form
                    Dim lan As OptionRowHost = RowOf(CheckOf(form, "_chkAllowLanViewers"))
                    Dim table As TableLayoutPanel = DirectCast(lan.Parent, TableLayoutPanel)
                    Dim column As Integer = table.GetColumn(lan)
                    Dim line As Integer = table.GetRow(lan)
                    Assert.True(column = 0 AndAlso line = 5, "the LAN row holds its own cell (0, 5), not " & column.ToString(CultureInfo.InvariantCulture) & ", " & line.ToString(CultureInfo.InvariantCulture))
                    Assert.True(Fld(Of RadioButton)(form, "_rbMicrophone").Checked, "the window starts on the microphone")
                    Assert.False(lan.Visible, "the LAN row hides for the microphone")

                    For Each source As String In {"_rbScreen", "_rbCamera"}
                        Fld(Of RadioButton)(form, source).Checked = True
                        Application.DoEvents()
                        Assert.True(lan.Visible AndAlso lan.Caption.Visible, "the LAN row shows for " & source)
                        Assert.True(table.GetColumn(lan) = column AndAlso table.GetRow(lan) = line, "the LAN row keeps its cell for " & source)
                        Dim found As New List(Of String)()
                        Audit(subject, form, subject.Window & " " & source, 1.0F, found)
                        Assert.True(found.Count = 0, String.Join(vbLf, found))
                    Next
                    For Each source As String In {"_rbSystemAudio", "_rbMicrophone"}
                        Fld(Of RadioButton)(form, source).Checked = True
                        Application.DoEvents()
                        Assert.False(lan.Visible, "the whole LAN row hides for " & source)
                        Assert.True(table.GetColumn(lan) = column AndAlso table.GetRow(lan) = line, "the LAN row keeps its cell for " & source)
                    Next
                End Using
            End Sub)
    End Sub

    <Theory>
    <InlineData("en")>
    <InlineData("ar")>
    Public Sub The_gateway_page_keeps_its_bold_caption_and_the_status_lines_start_where_the_caption_starts(code As String)
        InLanguage(code,
            Sub(kit As Kit)
                Dim subject As Subject = GatewayPage()
                Using shown As Shown = Open(subject, kit, 1.0F)
                    Dim form As Form = shown.Form
                    Dim check As CheckBox = CheckOf(form, "chkEnableGateway")
                    Dim row As OptionRowHost = RowOf(check)
                    Dim rtl As Boolean = Localization.IsRightToLeft()
                    Assert.True(row.Caption.Font.Bold AndAlso row.Caption.Font.SizeInPoints > row.Explanation.Font.SizeInPoints - 0.01F, "the caption keeps the page's bold, larger face")
                    Assert.True(Object.ReferenceEquals(Fld(Of Label)(form, "lblGatewayDesc"), row.Explanation), "the description is the row's explanation")
                    Assert.True(Object.ReferenceEquals(row, row.Explanation.Parent), "and stands inside the row")
                    Dim status As Label = Fld(Of Label)(form, "lblGatewayStatus")
                    Dim captionAt As Rectangle = OnForm(form, row.Caption)
                    Dim statusAt As Rectangle = OnForm(form, status)
                    If rtl Then
                        Assert.True(Math.Abs(statusAt.Right - captionAt.Right) <= 2, "the status line ends where the caption ends (" & statusAt.ToString() & " vs " & captionAt.ToString() & ")")
                    Else
                        Assert.True(Math.Abs(statusAt.Left - captionAt.Left) <= 2, "the status line starts where the caption starts (" & statusAt.ToString() & " vs " & captionAt.ToString() & ")")
                    End If
                    Assert.True(statusAt.Top >= OnForm(form, row).Bottom - 1, "and stands under the row")
                End Using
            End Sub)
    End Sub

    ' --- the source -------------------------------------------------------------------------------------

    Private Shared Function PlacingArguments(code As String) As List(Of String)
        Dim result As New List(Of String)()
        For Each m As Match In Regex.Matches(code, "\b(?:Controls\.(?:Add|AddRange)|AddRow|AddFullRow)\(")
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

    ''' <summary>The argument without the calls that wrap a check box into its row: a box handed to those is the right way in.</summary>
    Private Shared Function WithoutRowCalls(argument As String) As String
        Dim result As String = argument
        For Each marker As String In New String() {"OptionRows.InFlow(", "OptionRows.At(", "DialogOptionRow.Create(", "DialogOptionRow.CreateAt("}
            Dim at As Integer = result.IndexOf(marker, StringComparison.Ordinal)
            While at >= 0
                Dim depth As Integer = 1
                Dim i As Integer = at + marker.Length
                While i < result.Length AndAlso depth > 0
                    If result(i) = "("c Then depth += 1
                    If result(i) = ")"c Then depth -= 1
                    i += 1
                End While
                result = result.Remove(at, i - at)
                at = result.IndexOf(marker, StringComparison.Ordinal)
            End While
        Next
        Return result
    End Function

    <Fact>
    Public Sub No_dialog_adds_a_native_check_box_to_its_parent_every_one_goes_through_the_option_row()
        Dim problems As New List(Of String)()
        Dim expected As New Dictionary(Of String, Integer)(StringComparer.Ordinal) From {
            {Path.Combine("Forms", "Broadcast_Create_Form.vb"), 2}, {Path.Combine("Forms", "Exchange_Account_Admin_Form.vb"), 1},
            {Path.Combine("Forms", "Exchange_Account_Form.vb"), 3}, {Path.Combine("Forms", "Exchange_Password_Change_Form.vb"), 1},
            {Path.Combine("Forms", "Exchange_Password_Form.vb"), 1}, {Path.Combine("Forms", "Resource_Add_Form.vb"), 1},
            {Path.Combine("Forms", "PackageWizardForm.vb"), 2}, {Path.Combine("Forms", "Live_Broadcast_Form.vb"), 3},
            {Path.Combine("Forms", "Share_Disks_Form.vb"), 2}, {Path.Combine("Forms", "Share_Broadcasts_Form.vb"), 1}}
        Assert.Equal(17, expected.Values.Sum())
        For Each pair As KeyValuePair(Of String, Integer) In expected
            Dim code As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource(pair.Key))
            Dim made As Integer = Regex.Matches(code, "\bNew CheckBox\b").Count
            Dim rows As Integer = Regex.Matches(code, "\bOptionRows\.(?:InFlow|At)\(").Count
            If made <> pair.Value Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " 'New CheckBox', expected " & pair.Value.ToString(CultureInfo.InvariantCulture))
            If rows <> made Then problems.Add(pair.Key & ": " & made.ToString(CultureInfo.InvariantCulture) & " check boxes but " & rows.ToString(CultureInfo.InvariantCulture) & " option rows")
            If code.IndexOf("OptionRows.ArrangeAll(Me)", StringComparison.Ordinal) < 0 Then problems.Add(pair.Key & ": never arranges its rows once it has its scale (OptionRows.ArrangeAll)")
            ' A hint indenter measured on the native glyph has no place beside a row (the row's explanation or a plain margin does that).
            If code.IndexOf("CheckBoxLayout.BindHint(", StringComparison.Ordinal) >= 0 Then problems.Add(pair.Key & ": still binds a hint to a native glyph (CheckBoxLayout.BindHint)")
            Dim names As New List(Of String)()
            For Each m As Match In Regex.Matches(code, "\b(?:Private|Dim|Friend|Public)\s+(?:ReadOnly\s+)?(\w+)\s*(?:\([^)]*\))?\s+As\s+(?:New\s+)?CheckBox\b")
                names.Add(m.Groups(1).Value)
            Next
            If names.Count <> pair.Value Then problems.Add(pair.Key & ": the scan finds " & names.Count.ToString(CultureInfo.InvariantCulture) & " check box fields, expected " & pair.Value.ToString(CultureInfo.InvariantCulture))
            For Each argument As String In PlacingArguments(code)
                Dim bare As String = WithoutRowCalls(argument)
                For Each name As String In names
                    If Regex.IsMatch(bare, "\b" & Regex.Escape(name) & "\b") Then problems.Add(pair.Key & ": the check box " & name & " is placed directly (" & bare.Trim() & ")")
                Next
            Next
        Next
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    <Fact>
    Public Sub Every_window_of_the_Manager_outside_the_welcome_windows_makes_one_row_per_check_box()
        ' The settings pages were converted by T-11c-b2b. The welcome windows and the server wizard wait for the owner's answer
        ' to Q-6 (ticket 243); everything else the Manager compiles must hold the rule, so a new native check box cannot slip
        ' into a dialog or a settings page unnoticed.
        Dim pending As String() = {"Manager_Welcome_Form", "Server_Welcome_Form", "Exchange_Server_Wizard_Form"}
        Dim problems As New List(Of String)()
        Dim checked As Integer = 0
        For Each sourcePath As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileNameWithoutExtension(sourcePath)
            If pending.Any(Function(prefix) name.StartsWith(prefix, StringComparison.Ordinal)) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(sourcePath))
            Dim made As Integer = Regex.Matches(code, "\bNew CheckBox\b").Count
            Dim rows As Integer = Regex.Matches(code, "\bOptionRows\.(?:InFlow|At)\(|\bDialogOptionRow\.Create(?:At)?\(").Count
            If made > 0 Then checked += 1
            If rows < made Then problems.Add(Path.GetFileName(sourcePath) & ": " & made.ToString(CultureInfo.InvariantCulture) & " check boxes but " & rows.ToString(CultureInfo.InvariantCulture) & " option rows")
        Next
        Assert.True(checked >= 12, "the scan found only " & checked.ToString(CultureInfo.InvariantCulture) & " files with check boxes (the pattern is stale)")
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

End Class
