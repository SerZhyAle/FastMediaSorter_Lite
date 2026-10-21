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
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BOOLEAN-OPTION 2.0 on the PAGES of the Share Manager's settings windows (ticket 243, slice T-11c-b2b): the eleven check
''' boxes of the manager, SFTP and exchange-server destinations stand in <see cref="OptionRowHost"/> rows made by
''' <see cref="OptionRows"/> - the check box stays a real child of its row (name, handlers, Checked, Tab order and persistence are
''' the page's own), the explanation the page used to hang under the box is the row's explanation, and the row is what the
''' page's layout, Visible and Enabled see. Held here: the source (no page note helper and no hint binding of the old kind is
''' left), every box of every destination in a row, and the real windows at 100 % and 175 % in en, ru and ar with every page
''' selected and every group open (no row leaves its container, none lies over a neighbour, no hit target reaches another
''' control, no text is cut).
''' The scale is EMULATED the way the dialog tests do it (a larger font plus <c>Control.Scale</c>); how a page LOOKS under a real
''' pointer on a 150-200 % monitor, in High Contrast or under Narrator stays manual acceptance. The windows are shown without
''' the worker: the exchange component is a fake and the pages that would talk to it are marked as loaded.
''' </summary>
<Collection(ScheduledCollection.Name)>
Public Class ManagerSettingsPageOptionRowTests

    Private Const Flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic

    ''' <summary>The check boxes of the settings pages by field name (the pages build them for their destination only).</summary>
    Private Shared ReadOnly BoxFields As String() = {
        "chkAutostart", "chkOpenOnStart", "chkLanOnly", "_schedFeature", "_chkXRemoteAdminOn", "_chkPxPublish", "_chkPxThird",
        "_chkXAwake", "_chkXKeepPasswords", "_chkXSharedInv", "_chkXKeep"}

    Private Shared ReadOnly Destinations As SettingsDestination() = {SettingsDestination.Manager, SettingsDestination.Sftp, SettingsDestination.ExchangeServer}

    ' --- harness ---------------------------------------------------------------------------------------

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

    ''' <summary>Runs a check on an STA thread with the UI language set, a scratch settings hive, the exchange component faked and
    ''' everything put back afterwards. A control that throws while it paints fails the test instead of opening a modal box.</summary>
    Private Shared Sub InLanguage(code As String, check As Action)
        OnSta(Sub()
                  Dim saved As String = Localization.CurrentCode
                  Dim raised As New List(Of Exception)()
                  Dim handler As ThreadExceptionEventHandler = Sub(sender As Object, e As ThreadExceptionEventArgs) raised.Add(e.Exception)
                  Dim previousGate As String = Environment.GetEnvironmentVariable(ExchangeFeatures.DeveloperSwitchName)
                  ExchangeTestSupport.Reset()
                  Dim component As String = ExchangeTestSupport.MakeFakeComponent(True, False, False)
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
                      Localization.CurrentCode = saved
                  End Try
                  Assert.True(raised.Count = 0, "a control raised while painting or handling a message: " & String.Join(vbLf, raised.Select(Function(x) x.ToString())))
              End Sub)
    End Sub

    ''' <summary>A window of a destination with its lazy pages marked as loaded (so nothing asks the worker or the component), at an
    ''' emulated display scale, shown so that OnLoad arranges every page's rows.</summary>
    Private Shared Function Open(destination As SettingsDestination, factor As Single) As Share_Settings_Form
        Dim window As New Share_Settings_Form(destination:=destination)
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

    Private Shared Function Fld(window As Share_Settings_Form, name As String) As CheckBox
        Dim field As FieldInfo = GetType(Share_Settings_Form).GetField(name, Flags)
        Assert.True(field IsNot Nothing, "the window has no field " & name)
        Return TryCast(field.GetValue(window), CheckBox)
    End Function

    ''' <summary>Opens every group of the page (nothing folds any more, but a page may still hold a collapsed one) and selects it.</summary>
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

    ''' <summary>The containers a live state shows only for a configured exchange device (publication) or a signed-in server
    ''' administrator (invitations). The audit is about geometry, so it shows them without faking the service behind them.</summary>
    Private Shared ReadOnly ConditionalContainers As String() = {"_pxGroup", "_pxThirdBlock", "_pxEndpointRow", "_pnlXInvitations"}

    Private Shared Sub RevealConditionalGroups(window As Share_Settings_Form)
        For Each fieldName In ConditionalContainers
            Dim field As FieldInfo = GetType(Share_Settings_Form).GetField(fieldName, Flags)
            Dim target As Control = If(field Is Nothing, Nothing, TryCast(field.GetValue(window), Control))
            If target IsNot Nothing Then target.Visible = True
        Next
        ' The invitations rows follow the account view of a server that answered: hand the window one, so the rows show the way
        ' they do for a signed-in administrator.
        Dim viewField As FieldInfo = GetType(Share_Settings_Form).GetField("_exAccView", Flags)
        If viewField IsNot Nothing Then
            viewField.SetValue(window, New ExchangeAccountsView With {.Reached = True})
            Dim apply As MethodInfo = GetType(Share_Settings_Form).GetMethod("ApplyAccountsView", Flags)
            If apply IsNot Nothing Then apply.Invoke(window, New Object() {False})
        End If
        ' No message pumping from here to the audit: a refresh that was started by the state would answer "not reached" in the
        ' test and hide the rows again.
        For Each page In Pages(window).Values
            OptionRows.ArrangeAll(page)
        Next
    End Sub

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

    Private Shared Function TextFits(l As Label) As Boolean
        If l Is Nothing OrElse Not l.Visible OrElse l.Text.Length = 0 Then Return True
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(l.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim need As Integer = TextRenderer.MeasureText(l.Text, l.Font, New Size(Math.Max(1, l.Width - l.Padding.Horizontal), Integer.MaxValue), flags).Height
        Return need <= l.Height + 2
    End Function

    Private Shared Function Describe(c As Control) As String
        Return If(String.IsNullOrEmpty(c.Name), c.GetType().Name & "(" & c.Text & ")", c.Name)
    End Function

    ''' <summary>The sentences that are wrong with the visible option rows of the selected page (nothing added = sound).</summary>
    Private Shared Function AuditPage(window As Share_Settings_Form, pageId As String, lbl As String, factor As Single, found As List(Of String), seen As HashSet(Of String)) As Integer
        Dim page As Panel = Pages(window)(pageId)
        Dim rows As List(Of OptionRowHost) = AllOf(page).OfType(Of OptionRowHost)().Where(Function(r) r.Visible).ToList()
        For Each row As OptionRowHost In rows
            seen.Add(row.Check.Name)
        Next
        For Each row As OptionRowHost In rows
            Dim tag As String = lbl & " " & pageId & "/" & Describe(row.Check)

            ' The row inside the container that holds it, and over none of its siblings.
            Dim host As Control = row.Parent
            Dim client As Rectangle = host.ClientRectangle
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
            Dim inner As New List(Of Control) From {row.Check, row.Caption}
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
            If row.Check.Width < SettingsCheckBox.Px(UiSizing.PointerTarget, factor) - 1 Then found.Add(tag & ": the hit target is narrower than 32 px at this scale")
        Next

        ' Hit areas: every check box's target against every other control that takes a pointer, on the whole window.
        Dim targets As New List(Of KeyValuePair(Of OptionRowHost, Rectangle))()
        For Each row As OptionRowHost In rows
            targets.Add(New KeyValuePair(Of OptionRowHost, Rectangle)(row, ClippedOnForm(window, row.Check)))
        Next
        For i As Integer = 0 To targets.Count - 1
            Dim name As String = lbl & " " & pageId & "/" & Describe(targets(i).Key.Check)
            For j As Integer = i + 1 To targets.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, targets(j).Value)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(name & ": the hit target " & targets(i).Value.ToString() & " overlaps " & Describe(targets(j).Key.Check))
            Next
            For Each c As Control In AllOf(page)
                If Not c.Visible OrElse TypeOf c.Parent Is OptionRowHost Then Continue For
                If Not (TypeOf c Is ButtonBase OrElse TypeOf c Is TextBox OrElse TypeOf c Is ComboBox OrElse TypeOf c Is NumericUpDown OrElse TypeOf c Is DateTimePicker) Then Continue For
                Dim reach As Rectangle = ClippedOnForm(window, c)
                Dim overlap As Rectangle = Rectangle.Intersect(targets(i).Value, reach)
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(name & ": the hit target " & targets(i).Value.ToString() & " overlaps " & Describe(c) & " " & reach.ToString())
            Next
        Next
        Return rows.Count
    End Function

    ' --- the check boxes are in rows -------------------------------------------------------------------

    <Fact>
    Public Sub Every_check_box_of_the_pages_stands_in_an_option_row_and_keeps_its_name()
        InLanguage("en",
            Sub()
                Dim built As New HashSet(Of String)()
                Dim problems As New List(Of String)()
                For Each destination In Destinations
                    Using window As Share_Settings_Form = Open(destination, 1.0F)
                        For Each fieldName In BoxFields
                            Dim check As CheckBox = Fld(window, fieldName)
                            If check Is Nothing Then Continue For
                            built.Add(fieldName)
                            Dim row As OptionRowHost = TryCast(check.Parent, OptionRowHost)
                            If row Is Nothing Then
                                problems.Add(destination.ToString() & "/" & fieldName & ": not in an option row")
                                Continue For
                            End If
                            If check.Name.Length = 0 Then problems.Add(destination.ToString() & "/" & fieldName & ": has no Name")
                            If Not Object.ReferenceEquals(row.Check, check) Then problems.Add(destination.ToString() & "/" & fieldName & ": the row holds another check box")
                            If row.Explanation IsNot Nothing AndAlso row.Explanation.Name <> check.Name & "Hint" Then
                                problems.Add(destination.ToString() & "/" & fieldName & ": the explanation is named " & row.Explanation.Name & ", not " & check.Name & "Hint")
                            End If
                        Next
                        window.Hide()
                    End Using
                Next
                Assert.True(problems.Count = 0, String.Join(vbLf, problems))
                Dim missing As String = String.Join(", ", BoxFields.Where(Function(f) Not built.Contains(f)))
                Assert.True(missing.Length = 0, "no destination built these check boxes (the field list is stale): " & missing)
            End Sub)
    End Sub

    ' --- the source ------------------------------------------------------------------------------------

    <Fact>
    Public Sub No_page_still_hangs_a_hint_under_a_check_box_the_old_way()
        Dim problems As New List(Of String)()
        For Each sourcePath As String In ExchangeTestSupport.CompanionSources()
            Dim name As String = Path.GetFileNameWithoutExtension(sourcePath)
            If Not name.StartsWith("Share_Settings_Form", StringComparison.Ordinal) Then Continue For
            Dim code As String = ExchangeTestSupport.CodeOnly(File.ReadAllText(sourcePath))
            If Regex.IsMatch(code, "\bSetBooleanHint\b") Then problems.Add(name & ": SetBooleanHint is back")
            If Regex.IsMatch(code, "\bCheckBoxLayout\.BindHint\b") Then problems.Add(name & ": a hint is bound to a native check box")
            For Each fieldName In BoxFields
                ' A check box is never added to a table or a body directly: the row is what is placed.
                If Regex.IsMatch(code, "\b(?:AddBodyRow|Controls\.Add|SetRow|SetColumn)\(\s*" & Regex.Escape(fieldName) & "\b") Then
                    problems.Add(name & ": " & fieldName & " is placed directly")
                End If
            Next
        Next
        Assert.True(problems.Count = 0, String.Join(vbLf, problems))
    End Sub

    ' --- the real pages at 100 and 175 percent, left to right and right to left -------------------------

    <Theory>
    <InlineData("en", 1.0F)>
    <InlineData("en", 1.75F)>
    <InlineData("ru", 1.0F)>
    <InlineData("ru", 1.75F)>
    <InlineData("ar", 1.0F)>
    <InlineData("ar", 1.75F)>
    Public Sub Every_option_row_of_every_page_sits_inside_its_page_and_over_nothing(code As String, factor As Single)
        InLanguage(code,
            Sub()
                Dim found As New List(Of String)()
                Dim seen As New HashSet(Of String)()
                Dim expected As New HashSet(Of String)()
                Dim whyHidden As New Dictionary(Of String, String)()
                For Each destination In Destinations
                    Using window As Share_Settings_Form = Open(destination, factor)
                        For Each fieldName In BoxFields
                            Dim box As CheckBox = Fld(window, fieldName)
                            If box IsNot Nothing Then expected.Add(box.Name)
                        Next
                        Dim states As Integer() = If(destination = SettingsDestination.ExchangeServer, New Integer() {2, 0}, New Integer() {-1})
                        For Each state As Integer In states
                            If state >= 0 Then
                                Dim snapshot As New ExchangeSnapshot With {.ServiceExists = state = 2,
                                    .ServiceState = If(state = 2, ServiceControl.ServiceState.Stopped, ServiceControl.ServiceState.NotInstalled),
                                    .CanChangeFirewall = True, .CanRegisterService = state <> 2, .CanRemoveService = state = 2,
                                    .Report = New ExchangeProcess.InspectReport With {
                                        .Answered = True, .ConfigPresent = state <> 0, .Accounts = If(state = 0, 0, 2),
                                        .CertPresent = state <> 0, .Fingerprint = If(state = 0, "", "SHA256:abc"),
                                        .Listen = ":22022", .PortRange = "40000-40100", .PublicEndpoint = "203.0.113.7:22022"}}
                                GetType(Share_Settings_Form).GetMethod("ApplyExchangeState", Flags).Invoke(window, New Object() {snapshot})
                            End If
                            For Each pageId As String In Pages(window).Keys.ToList()
                                ShowPage(window, pageId)
                                RevealConditionalGroups(window)
                                Dim lbl As String = code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "% " & destination.ToString()
                                AuditPage(window, pageId, lbl, factor, found, seen)
                            Next
                        Next
                        ' Where a box that stayed out of the audit is cut off from the screen (the message names the first hidden container).
                        For Each fieldName In BoxFields
                            Dim box As CheckBox = Fld(window, fieldName)
                            If box Is Nothing OrElse seen.Contains(box.Name) Then Continue For
                            Dim chain As Control = box
                            Dim cause As String = destination.ToString() & ": " & Describe(box) & " is in no visible row"
                            While chain IsNot Nothing
                                If chain.Parent IsNot Nothing AndAlso chain.Parent.Visible AndAlso Not chain.Visible Then cause = destination.ToString() & ": " & Describe(box) & " is hidden by " & Describe(chain)
                                chain = chain.Parent
                            End While
                            whyHidden(box.Name) = cause
                        Next
                        window.Hide()
                    End Using
                Next
                ' The eleven boxes are not all visible at once (a destination builds its own, the exchange ones follow the server
                ' state), but across the destinations and states each one has to be seen and audited at least once, or the audit
                ' has stopped looking at the pages.
                Dim unaudited As String = String.Join(", ", expected.Where(Function(n) Not seen.Contains(n)))
                Assert.True(unaudited.Length = 0, "these check boxes were never a visible row on any page: " & unaudited & vbLf &
                            String.Join(vbLf, whyHidden.Where(Function(p) Not seen.Contains(p.Key)).Select(Function(p) p.Value)))
                Assert.True(found.Count = 0, String.Join(vbLf, found.Distinct()))
            End Sub)
    End Sub

    ' --- hiding and disabling go through the row --------------------------------------------------------

    <Fact>
    Public Sub A_hidden_exchange_option_hides_its_whole_row_and_a_disabled_one_dims_its_caption()
        InLanguage("en",
            Sub()
                Using window As Share_Settings_Form = Open(SettingsDestination.ExchangeServer, 1.0F)
                    Dim keep As CheckBox = Fld(window, "_chkXKeep")
                    Assert.NotNull(keep)
                    Dim row As OptionRowHost = DirectCast(keep.Parent, OptionRowHost)
                    ShowPage(window, "exchange")
                    Dim setVisible As MethodInfo = GetType(Share_Settings_Form).GetMethod("SetBooleanVisible", BindingFlags.Static Or BindingFlags.NonPublic)
                    Dim setEnabled As MethodInfo = GetType(Share_Settings_Form).GetMethod("SetBooleanEnabled", BindingFlags.Static Or BindingFlags.NonPublic)
                    setVisible.Invoke(Nothing, New Object() {keep, False})
                    Application.DoEvents()
                    Assert.False(row.Visible, "the row stays when its option is hidden")
                    If row.Explanation IsNot Nothing Then Assert.False(row.Explanation.Visible, "the explanation stays when its option is hidden")
                    setVisible.Invoke(Nothing, New Object() {keep, True})
                    setEnabled.Invoke(Nothing, New Object() {keep, False})
                    Application.DoEvents()
                    Assert.True(row.Visible)
                    Assert.False(row.Enabled, "the row is not dimmed with its option")
                    Assert.False(keep.Enabled)
                    setEnabled.Invoke(Nothing, New Object() {keep, True})
                    Assert.True(row.Enabled)
                    window.Hide()
                End Using
            End Sub)
    End Sub

End Class
