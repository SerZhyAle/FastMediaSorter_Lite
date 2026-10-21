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
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BUTTONS 2.0 sections 3 and 4 as the second batch of the Share Manager's dialogs holds them after they moved to the
''' shared <see cref="DialogButtonRow"/> (ticket 243, slice T-8b, Share Manager batch B): the account administration
''' dialog (add, reset), the password dialog (enter, generated, kept), the local account picker, the internet access
''' instructions, the camera scan, the broadcast creation, the broadcasts list and the exchange sign-in. Batch A is held
''' by <c>DialogButtonRowManagerBatchATests</c> in the same way; the row itself by the viewer's <c>DialogButtonRowTests</c>.
''' What is held here is that each window reads its answers Cancel first (the final affirmative last, a command before
''' both), binds Escape and Enter to the right buttons, never makes a command or a destructive button the default, keeps
''' the control names and its secret fields masked, and that the row neither lies over the content nor leaves the client
''' area - at 100 % and 175 %, in a left-to-right language (en, ru) and in a right-to-left one (ar). A scale is EMULATED
''' (the font grown by the factor, the fixed pixels scaled with Control.Scale), the way the batch A tests do it; how a
''' window LOOKS stays manual acceptance.
'''
''' NOT converted, with the reason (kept here so the next reader does not take them for forgotten):
'''   * <c>Live_Broadcast_Form</c> has no answer row at all - no OK, no Cancel, no Close; Start/Stop is a state pair and the
'''     five hand-off buttons are commands of the broadcast that is on air, not answers.
'''   * <c>Exchange_Server_Wizard_Form</c>: in the first-run mode (reached from the Welcome wizard) every button has the
'''     44 px target of specification 184 rule 6, which the row cannot express (Q-6 of the plan is open), and the window
'''     sizes itself from its single body (<c>ClientSize = Controls(0).PreferredSize</c>) inside a scrolling form.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class DialogButtonRowManagerBatchBTests

    ''' <summary>What a window must look like to its user: the buttons of its answer row in reading order (names), the
    ''' button Escape and Enter reach, how many masked (password) fields it holds and whether they hold a value on purpose.</summary>
    Private NotInheritable Class Spec
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly Reading As String()
        Friend ReadOnly CancelName As String
        Friend ReadOnly AcceptName As String
        Friend ReadOnly Masked As Integer
        Friend ReadOnly Filled As Boolean
        ''' <summary>False for the windows whose first Shown event reaches the outside (the camera, the worker, the status
        ''' poll): the window is shown, laid out and closed without the message loop delivering that event.</summary>
        Friend ReadOnly Pump As Boolean

        Friend Sub New(name As String, make As Func(Of Form), reading As String(), cancelName As String, acceptName As String, masked As Integer,
                       Optional filled As Boolean = False, Optional pump As Boolean = True)
            Me.Name = name
            Me.Make = make
            Me.Reading = reading
            Me.CancelName = cancelName
            Me.AcceptName = acceptName
            Me.Masked = masked
            Me.Filled = filled
            Me.Pump = pump
        End Sub
    End Class

    ''' <summary>The agent the sign-in window needs, with the folder it keeps: made once per test, removed after it.</summary>
    Private NotInheritable Class Session
        Implements IDisposable

        Private ReadOnly rig As New ExchangeDeviceAgentTests.Rig()
        Private ReadOnly agents As New List(Of ExchangeDeviceAgent)()

        Friend Function NewAgent() As ExchangeDeviceAgent
            Dim agent As ExchangeDeviceAgent = rig.NewAgent()
            agents.Add(agent)
            Return agent
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            For Each agent As ExchangeDeviceAgent In agents
                agent.Dispose()
            Next
            rig.Dispose()
        End Sub
    End Class

    Private Const KeptPassword As String = "Hq7Tm2Zk9Xv4Bn8Rc3Wd"

    Private Shared Function RegistrationString() As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build("exchange.example:443", "SHA256:" & New String("A"c, 43), "Test", "alice", ExchangeStubAccount.Invitation)
        Assert.True(built.Succeeded, built.Reason)
        Dim failure As ExchangeConnectionStringResult = Nothing
        Return ExchangeConnectionString.Format(built.Value, failure)
    End Function

    ''' <summary>The sign-in window with a pasted string that carries an invitation: the registering state - the longest the
    ''' window gets (certificate panel, repeat field, policy line, a longer main button).</summary>
    Private Shared Function RegisteringAccountForm(agent As ExchangeDeviceAgent) As Form
        Dim form As New Exchange_Account_Form(agent)
        Dim field As FieldInfo = GetType(Exchange_Account_Form).GetField("_txtString", BindingFlags.Instance Or BindingFlags.NonPublic)
        DirectCast(field.GetValue(form), TextBox).Text = RegistrationString()
        Return form
    End Function

    Private Shared Function Specs(session As Session) As List(Of Spec)
        Dim accounts As IReadOnlyList(Of String) = New List(Of String) From {"alice", "bob", "carol"}
        Dim folder As String = AppContext.BaseDirectory
        Return New List(Of Spec) From {
            New Spec("Exchange_Account_Admin_Form (add)", Function() New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add),
                     {"xaa_Cancel", "xaa_Ok"}, "xaa_Cancel", "xaa_Ok", 0),
            New Spec("Exchange_Account_Admin_Form (first account)", Function() New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Add, "", True),
                     {"xaa_Cancel", "xaa_Ok"}, "xaa_Cancel", "xaa_Ok", 0),
            New Spec("Exchange_Account_Admin_Form (reset)", Function() New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, "alice"),
                     {"xaa_Cancel", "xaa_Ok"}, "xaa_Cancel", "xaa_Ok", 0),
            New Spec("Exchange_Password_Form (enter)", Function() New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter),
                     {"xpw_Cancel", "xpw_Ok"}, "xpw_Cancel", "xpw_Ok", 1),
            New Spec("Exchange_Password_Form (generated)", Function() New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, KeptPassword, "alice", True),
                     {"xpw_Copy", "xpw_Own", "xpw_Close"}, "xpw_Close", "xpw_Close", 1, filled:=True),
            New Spec("Exchange_Password_Form (kept)", Function() New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Kept, KeptPassword, "alice", True),
                     {"xpw_Copy", "xpw_Close"}, "xpw_Close", "xpw_Close", 1, filled:=True),
            New Spec("Exchange_Local_Account_Picker_Form", Function() New Exchange_Local_Account_Picker_Form(accounts),
                     {"xlap_Cancel", "xlap_Ok"}, "xlap_Cancel", "xlap_Ok", 0),
            New Spec("InternetAccessForm", Function() New InternetAccessForm(New WorkerStatus()),
                     {"xia_Close"}, "xia_Close", "xia_Close", 0, pump:=False),
            New Spec("Camera_Scan_Form", Function() New Camera_Scan_Form(),
                     {"xcam_Cancel"}, "xcam_Cancel", "xcam_Cancel", 0, pump:=False),
            New Spec("Broadcast_Create_Form (file)", Function() New Broadcast_Create_Form("C:\Media\film.mp4", New ShareSettings()),
                     {"xbc_Cancel", "xbc_Ok"}, "xbc_Cancel", "xbc_Ok", 0),
            New Spec("Broadcast_Create_Form (folder)", Function() New Broadcast_Create_Form(folder, New ShareSettings()),
                     {"xbc_Cancel", "xbc_Ok"}, "xbc_Cancel", "xbc_Ok", 0),
            New Spec("Share_Broadcasts_Form", Function() New Share_Broadcasts_Form(),
                     {"xsb_Close"}, "xsb_Close", "xsb_Close", 0, pump:=False),
            New Spec("Exchange_Account_Form", Function() New Exchange_Account_Form(session.NewAgent()),
                     {"xacc_Diagnose", "xacc_Cancel", "xacc_SignIn"}, "xacc_Cancel", "", 2),
            New Spec("Exchange_Account_Form (registering)", Function() RegisteringAccountForm(session.NewAgent()),
                     {"xacc_Diagnose", "xacc_Cancel", "xacc_SignIn"}, "xacc_Cancel", "", 2)}
    End Function

    ' --- helpers -----------------------------------------------------------------------------

    Private Shared Function All(parent As Control) As List(Of Control)
        Dim found As New List(Of Control)()
        For Each c As Control In parent.Controls
            found.Add(c)
            found.AddRange(All(c))
        Next
        Return found
    End Function

    Private Shared Function RowsOf(parent As Control) As List(Of DialogButtonRow)
        Return All(parent).OfType(Of DialogButtonRow)().ToList()
    End Function

    ''' <summary>Builds a window, emulates a display scale and shows it (OnLoad runs its own fitting).</summary>
    Private Shared Function ShownAt(spec As Spec, factor As Single) As Form
        Dim form As Form = spec.Make()
        Dim auto As Single = form.DeviceDpi / 96.0F
        Dim extra As Single = factor / auto
        If Math.Abs(extra - 1.0F) > 0.01F Then
            form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
            form.Scale(New SizeF(extra, extra))
        End If
        form.StartPosition = FormStartPosition.Manual
        form.Location = New Point(20, 20)
        form.Show()
        If spec.Pump Then Application.DoEvents()
        Return form
    End Function

    Private Shared Sub Release(form As Form)
        Try
            form.Close()
        Finally
            form.Dispose()
        End Try
    End Sub

    Private Shared Function OnForm(form As Form, c As Control) As Rectangle
        Return form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))
    End Function

    ''' <summary>The visible part of a control on the window: its rectangle cut by every container between it and the window.</summary>
    Private Shared Function ClippedOnForm(form As Form, c As Control) As Rectangle
        Dim visible As Rectangle = OnForm(form, c)
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            visible = Rectangle.Intersect(visible, OnForm(form, p))
            p = p.Parent
        End While
        Return visible
    End Function

    ''' <summary>True when the window itself or a container above the control scrolls.</summary>
    Private Shared Function UnderScroller(form As Form, c As Control) As Boolean
        If form.AutoScroll Then Return True
        Dim p As Control = c.Parent
        While p IsNot Nothing AndAlso Not Object.ReferenceEquals(p, form)
            Dim sc As ScrollableControl = TryCast(p, ScrollableControl)
            If sc IsNot Nothing AndAlso sc.AutoScroll Then Return True
            p = p.Parent
        End While
        Return False
    End Function

    ''' <summary>True when <paramref name="a"/> reads before <paramref name="b"/>: on an earlier line, or further along the
    ''' reading direction of the same one.</summary>
    Private Shared Function ReadsBefore(form As Form, a As Control, b As Control, rightToLeft As Boolean) As Boolean
        Dim ra As Rectangle = OnForm(form, a)
        Dim rb As Rectangle = OnForm(form, b)
        If ra.Top <> rb.Top Then Return ra.Top < rb.Top
        Return If(rightToLeft, ra.Left > rb.Left, ra.Left < rb.Left)
    End Function

    ''' <summary>The sentences that are wrong with the answer row of a shown window (empty = sound).</summary>
    Private Shared Function RowProblems(label As String, form As Form) As List(Of String)
        Dim found As New List(Of String)()
        Dim rows As List(Of DialogButtonRow) = RowsOf(form)
        If rows.Count <> 1 Then
            found.Add(label & ": expected exactly one DialogButtonRow, found " & rows.Count.ToString(CultureInfo.InvariantCulture))
            Return found
        End If
        Dim row As DialogButtonRow = rows(0)
        Dim client As Rectangle = form.ClientRectangle

        ' every button of the row holds its caption and stays inside the client area
        For Each b As Button In row.ReadingButtons
            Dim need As Integer = TextRenderer.MeasureText(b.Text.Replace("&", ""), b.Font).Width + ButtonGlyphs.ExtraWidth(b)
            If b.Width < need Then found.Add(label & ": " & b.Name & " '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide and its caption needs " & need.ToString(CultureInfo.InvariantCulture))
            Dim at As Rectangle = OnForm(form, b)
            If at.Left < client.Left - 1 OrElse at.Right > client.Right + 1 OrElse at.Top < client.Top - 1 OrElse at.Bottom > client.Bottom + 1 Then
                found.Add(label & ": " & b.Name & " " & at.ToString() & " is clipped by the window " & client.Size.ToString())
            End If
        Next

        ' no two buttons of the row overlap
        Dim buttons As IReadOnlyList(Of Button) = row.ReadingButtons
        For i As Integer = 0 To buttons.Count - 1
            For j As Integer = i + 1 To buttons.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(OnForm(form, buttons(i)), OnForm(form, buttons(j)))
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(label & ": " & buttons(i).Name & " lies over " & buttons(j).Name)
            Next
        Next

        ' one height per line, never below the target
        For Each line In buttons.GroupBy(Function(b) b.Top)
            If line.Select(Function(b) b.Height).Distinct().Count() <> 1 Then
                found.Add(label & ": the buttons of one line differ in height: " & String.Join(",", line.Select(Function(b) b.Height.ToString(CultureInfo.InvariantCulture))))
            End If
            If line.Any(Function(b) b.Height < 32) Then found.Add(label & ": a button is below the 32 target")
        Next

        ' the row is inside the client area and lies over none of the content: not a leaf control of the window, and not a
        ' container whose own rectangle takes in the row (a flow or a table that ends above it)
        Dim rowBounds As Rectangle = OnForm(form, row)
        If rowBounds.Left < client.Left - 1 OrElse rowBounds.Right > client.Right + 1 OrElse rowBounds.Bottom > client.Bottom + 1 Then
            found.Add(label & ": the row " & rowBounds.ToString() & " leaves the client area " & client.Size.ToString())
        End If
        For Each c As Control In All(form)
            If Object.ReferenceEquals(c, row) OrElse row.Contains(c) OrElse Not c.Visible OrElse c.Controls.Count > 0 Then Continue For
            ' what a scrolling container clips away is not drawn over the row
            Dim at As Rectangle = ClippedOnForm(form, c)
            Dim overlap As Rectangle = Rectangle.Intersect(at, rowBounds)
            If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & at.ToString() & " lies over the answer row " & rowBounds.ToString())
            End If
        Next

        ' nothing visible of the window is cut by the client area on the way down (an auto-sized or fitted window); what sits
        ' in a scrolling container is reached by scrolling, so it may lie beyond the edge
        For Each c As Control In All(form)
            If Not c.Visible OrElse c.Controls.Count > 0 OrElse UnderScroller(form, c) Then Continue For
            Dim at As Rectangle = OnForm(form, c)
            If at.Bottom > client.Bottom + 1 OrElse at.Right > client.Right + 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & at.ToString() & " leaves the client area " & client.Size.ToString())
            End If
        Next
        Return found
    End Function

    ' --- the checks --------------------------------------------------------------------------

    <Theory>
    <InlineData("en")>
    <InlineData("ru")>
    <InlineData("ar")>
    Public Sub The_second_batch_keeps_the_row_clear_of_the_content_and_inside_the_window_at_100_and_175_percent(code As String)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    Dim problems As New List(Of String)()
                    Using session As New Session()
                        For Each spec As Spec In Specs(session)
                            For Each factor As Single In New Single() {1.0F, 1.75F}
                                Dim label As String = spec.Name & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                                Dim form As Form = ShownAt(spec, factor)
                                Try
                                    problems.AddRange(RowProblems(label, form))
                                    ' at 100 % no window needs a scroll bar: the scrolling bodies are a safety net for a small monitor, so a
                                    ' window that fills the whole working area of this (small) test screen may use it
                                    Dim wa As Rectangle = Screen.FromControl(form).WorkingArea
                                    If factor = 1.0F AndAlso form.Height < wa.Height AndAlso form.Width < wa.Width Then
                                        For Each sc As ScrollableControl In All(form).OfType(Of ScrollableControl)().Concat({CType(form, ScrollableControl)})
                                            If sc.AutoScroll AndAlso (sc.HorizontalScroll.Visible OrElse sc.VerticalScroll.Visible) Then
                                                problems.Add(label & ": " & If(String.IsNullOrEmpty(sc.Name), sc.GetType().Name, sc.Name) & " shows a scroll bar at 100 % (window " & form.Size.ToString() &
                                                             ", client " & form.ClientSize.ToString() & ", working area " & Screen.FromControl(form).WorkingArea.Size.ToString() &
                                                             ", min scroll " & sc.AutoScrollMinSize.ToString() & ", display " & sc.DisplayRectangle.Size.ToString() & ")")
                                            End If
                                        Next
                                    End If
                                    ' the window is within the working area of its monitor
                                    Dim area As Rectangle = Screen.FromControl(form).WorkingArea
                                    If form.Width > area.Width OrElse form.Height > area.Height Then
                                        problems.Add(label & ": the window " & form.Size.ToString() & " is larger than the working area " & area.Size.ToString())
                                    End If
                                Finally
                                    Release(form)
                                End Try
                            Next
                        Next
                    End Using
                    Assert.True(problems.Count = 0, String.Join(vbLf, problems))
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ru", False)>
    <InlineData("ar", True)>
    Public Sub The_second_batch_reads_cancel_first_and_binds_escape_and_enter(code As String, mirrored As Boolean)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    Assert.Equal(mirrored, Localization.IsRightToLeft())
                    Using session As New Session()
                        For Each spec As Spec In Specs(session)
                            Dim form As Form = ShownAt(spec, 1.0F)
                            Try
                                Assert.Equal(If(mirrored, RightToLeft.Yes, RightToLeft.No), form.RightToLeft)
                                Dim row As DialogButtonRow = RowsOf(form).Single()
                                Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                                Assert.Equal(spec.Reading, reading.Select(Function(b) b.Name).ToArray())

                                ' the reading order is what the eye sees: earlier lines first, mirrored under right-to-left
                                For i As Integer = 0 To reading.Count - 2
                                    Assert.True(ReadsBefore(form, reading(i), reading(i + 1), mirrored),
                                                spec.Name & " " & code & ": " & reading(i).Name & " must read before " & reading(i + 1).Name)
                                Next

                                ' Escape = the dismissing answer, bound by the row
                                Dim cancel As Button = TryCast(form.CancelButton, Button)
                                Assert.NotNull(cancel)
                                Assert.Equal(spec.CancelName, cancel.Name)
                                Assert.Same(row.CancelTarget, cancel)
                                Assert.Equal(ButtonGlyphRole.Dismissing, ButtonGlyphs.RoleOf(cancel))

                                ' Enter = the safe default of the row, or nothing for the window whose accept button belongs to its own
                                ' state (the sign-in has none until a certificate is trusted)
                                Dim accept As Button = TryCast(form.AcceptButton, Button)
                                If spec.AcceptName.Length = 0 Then
                                    Assert.Null(accept)
                                Else
                                    Assert.NotNull(accept)
                                    Assert.Equal(spec.AcceptName, accept.Name)
                                    Assert.Same(row.DefaultTarget, accept)
                                    Assert.Contains(accept, reading)
                                    ' never a command (they copy secrets or hand out access) and never the irreversible one
                                    Assert.NotEqual(ButtonGlyphRole.Irreversible, ButtonGlyphs.RoleOf(accept))
                                    Dim role As ButtonGlyphRole = ButtonGlyphs.RoleOf(accept)
                                    Assert.True(accept.Name = spec.CancelName OrElse role = ButtonGlyphRole.Affirmative OrElse role = ButtonGlyphRole.Caution,
                                                spec.Name & ": the default " & accept.Name & " is neither the dismissing nor the affirmative answer")
                                End If
                            Finally
                                Release(form)
                            End Try
                        Next
                    End Using
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_second_batch_keeps_its_answer_names_inside_the_row_and_numbers_the_tab_order_in_reading_order()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using session As New Session()
                    For Each spec As Spec In Specs(session)
                        Dim form As Form = ShownAt(spec, 1.0F)
                        Try
                            Dim row As DialogButtonRow = RowsOf(form).Single()
                            For Each name As String In spec.Reading
                                Dim found As Control() = form.Controls.Find(name, True)
                                Assert.True(found.Length = 1 AndAlso TypeOf found(0) Is Button, spec.Name & ": '" & name & "' is exactly one button")
                                Assert.Contains(DirectCast(found(0), Button), row.ReadingButtons)
                            Next
                            ' the fields keep their order and the answers come after all of them
                            Assert.Equal(row.Parent.Controls.Count - 1, row.Parent.Controls.GetChildIndex(row))
                            Dim tabs As Integer() = row.ReadingButtons.Select(Function(b) b.TabIndex).ToArray()
                            Assert.Equal(Enumerable.Range(0, tabs.Length).ToArray(), tabs)
                            ' every answer carries a glyph (the row applies it) and no button of the row keeps the old inner padding
                            For Each b As Button In row.ReadingButtons
                                Assert.True(ButtonGlyphs.CarriesGlyph(b), spec.Name & ": " & b.Name & " has no glyph")
                                Assert.Equal(Padding.Empty, b.Padding)
                            Next
                        Finally
                            Release(form)
                        End Try
                    Next
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_second_batch_keeps_its_secret_fields_masked_and_empty_unless_the_window_shows_a_value_once()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using session As New Session()
                    For Each spec As Spec In Specs(session)
                        Dim form As Form = ShownAt(spec, 1.0F)
                        Try
                            Dim masked As List(Of TextBox) = All(form).OfType(Of TextBox)().Where(Function(t) t.UseSystemPasswordChar).ToList()
                            Assert.True(masked.Count = spec.Masked, spec.Name & ": expected " & spec.Masked.ToString(CultureInfo.InvariantCulture) & " masked fields, found " & masked.Count.ToString(CultureInfo.InvariantCulture))
                            For Each t As TextBox In masked
                                ' the generated and the kept password are the one value the window shows: masked until revealed
                                If spec.Filled Then
                                    Assert.Equal(KeptPassword, t.Text)
                                    Assert.True(t.ReadOnly)
                                Else
                                    Assert.Equal("", t.Text)
                                End If
                            Next
                        Finally
                            Release(form)
                        End Try
                    Next
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub Cancel_of_the_password_dialogs_dismisses_without_handing_a_password_over()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using form As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Enter)
                    form.Show()
                    Application.DoEvents()
                    For Each t As TextBox In All(form).OfType(Of TextBox)()
                        t.Text = "Typed-Secret-1"
                    Next
                    DirectCast(form.CancelButton, Button).PerformClick()
                    Assert.Equal(DialogResult.Cancel, form.DialogResult)
                    Assert.Equal("", form.EnteredPassword)
                End Using
                Using form As New Exchange_Account_Admin_Form(Exchange_Account_Admin_Form.DialogMode.Reset, "alice", False, True)
                    form.Show()
                    Application.DoEvents()
                    DirectCast(form.CancelButton, Button).PerformClick()
                    Assert.Equal(DialogResult.Cancel, form.DialogResult)
                    Assert.Equal("", form.Password)
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub Enter_in_the_generated_password_dialog_closes_it_and_never_copies_the_secret()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using form As New Exchange_Password_Form(Exchange_Password_Form.DialogMode.Generated, KeptPassword, "alice", True)
                    form.Show()
                    Application.DoEvents()
                    ' the default is Close (it used to be Copy): Enter can no longer put the password on the clipboard by itself
                    Dim accept As Button = DirectCast(form.AcceptButton, Button)
                    Assert.Equal("xpw_Close", accept.Name)
                    Assert.False(form.WasCopied)
                    Dim own As Button = DirectCast(form.Controls.Find("xpw_Own", True).Single(), Button)
                    Assert.NotSame(accept, own)
                End Using
            End Sub)
    End Sub

    ''' <summary>The sign-in window has no accept button until a certificate has been shown and trusted: the row must not
    ''' hand it one, or Enter in the password field would send it to a server nobody vouched for.</summary>
    <Fact>
    Public Sub The_sign_in_window_has_no_accept_button_until_the_certificate_is_trusted()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using session As New Session()
                    Using form As Form = RegisteringAccountForm(session.NewAgent())
                        form.Show()
                        Application.DoEvents()
                        Assert.Null(form.AcceptButton)
                        Assert.Equal("xacc_Cancel", DirectCast(form.CancelButton, Button).Name)
                        Dim row As DialogButtonRow = RowsOf(form).Single()
                        Assert.Equal("xacc_SignIn", row.DefaultTarget.Name)
                        ' the registering state names its main button for what it does
                        Assert.Equal(Localization.T("Создать учётную запись"), form.Controls.Find("xacc_SignIn", True).Single().Text)
                    End Using
                End Using
            End Sub)
    End Sub

    ''' <summary>The windows are adopters, not look-alikes: their source builds the row, binds it, and no longer sets
    ''' AcceptButton / CancelButton or places an answer button by hand. The sign-in keeps its own accept button (it
    ''' belongs to the certificate state) and says so with bindAccept:=False.</summary>
    <Fact>
    Public Sub The_second_batch_sources_adopt_the_row()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim src As String = Nothing
        While dir IsNot Nothing AndAlso src Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "DialogButtonRow.vb")) Then src = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(src)
        For Each file As String In New String() {"Exchange_Account_Admin_Form", "Exchange_Password_Form", "Exchange_Local_Account_Picker_Form", "InternetAccessForm",
                                                 "Camera_Scan_Form", "Broadcast_Create_Form", "Share_Broadcasts_Form", "Exchange_Account_Form"}
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", file & ".vb"))
            Assert.Contains("New DialogButtonRow()", text)
            Assert.Contains(".Bind(Me", text)
            Assert.Contains("DpiLayout.AddAnswer(", text)
            Assert.DoesNotMatch("\bCancelButton\s*=", text)
            If file = "Exchange_Account_Form" Then
                Assert.Contains("bindAccept:=False", text)
            Else
                Assert.DoesNotMatch("\bAcceptButton\s*=", text)
            End If
        Next
    End Sub

End Class
