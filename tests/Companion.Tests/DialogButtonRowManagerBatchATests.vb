Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' BUTTONS 2.0 sections 3 and 4 as the Share Manager's simple modal dialogs hold them after they moved to the shared
''' <see cref="DialogButtonRow"/> (ticket 243, slice T-8b, Share Manager batch A): the password change, the outsider link,
''' the connection string, the two invitation windows and the shared-invitation one, the account limits, the audit
''' and browser-administration windows, the access test, the status window and the "outside access" window. The row
''' itself is held by the viewer's <c>DialogButtonRowTests</c>; what is held here is that each window reads its answers
''' Cancel first (the final affirmative last), binds Escape and Enter to the right buttons, never makes a command or a
''' destructive button the default, keeps the control names and its secret fields masked, and that the row neither lies
''' over the content nor leaves the client area - at 100 % and 175 %, in a left-to-right language (en, ru) and in a
''' right-to-left one (ar). A scale is EMULATED (the font grown by the factor, the fixed pixels scaled with
''' Control.Scale), the way the viewer's batch tests do it; how a window LOOKS stays manual acceptance.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class DialogButtonRowManagerBatchATests

    ''' <summary>What a window must look like to its user: the buttons of its answer row in reading order (names), the
    ''' button Escape and Enter reach, and how many masked (password) fields it holds.</summary>
    Private NotInheritable Class Spec
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly Reading As String()
        Friend ReadOnly CancelName As String
        Friend ReadOnly AcceptName As String
        Friend ReadOnly Masked As Integer

        Friend Sub New(name As String, make As Func(Of Form), reading As String(), cancelName As String, acceptName As String, masked As Integer)
            Me.Name = name
            Me.Make = make
            Me.Reading = reading
            Me.CancelName = cancelName
            Me.AcceptName = acceptName
            Me.Masked = masked
        End Sub
    End Class

    Private Shared Function Made(name As String, login As String) As ExchangeProcess.ConnectionStringResult
        Return New ExchangeProcess.ConnectionStringResult With {.Outcome = ExchangeProcess.ConnectionStringOutcome.Made, .Text = "FMSEXCH1:test"}
    End Function

    Private Shared Function Specs() As List(Of Spec)
        Dim qr As Byte() = ShareConfigBuilder.QrPngForText("FMSSFTP2:test")
        Dim fp As String = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12"
        Return New List(Of Spec) From {
            New Spec("Exchange_Password_Change_Form", Function() New Exchange_Password_Change_Form("user@exchange.example"),
                     {"xpc_Cancel", "xpc_Ok"}, "xpc_Cancel", "xpc_Ok", 3),
            New Spec("Exchange_Link_Form", Function() New Exchange_Link_Form("Holiday photos with a long name", "FMSSFTP2:test", "{}", qr),
                     {"xlink_Qr", "xlink_Copy", "xlink_Save", "xlink_Close"}, "xlink_Close", "xlink_Close", 0),
            New Spec("Exchange_Connection_String_Form", Function() New Exchange_Connection_String_Form(AddressOf Made),
                     {"xcs_Close"}, "xcs_Close", "xcs_Close", 0),
            New Spec("Exchange_Invitation_Create_Form", Function() New Exchange_Invitation_Create_Form(),
                     {"xinvc_Cancel", "xinvc_Ok"}, "xinvc_Cancel", "xinvc_Ok", 0),
            New Spec("Exchange_Invitation_Created_Form", Function() New Exchange_Invitation_Created_Form("INV-CODE-0001", "FMSEXCH1:test", "member"),
                     {"xinvd_Close"}, "xinvd_Close", "xinvd_Close", 0),
            New Spec("Exchange_Shared_Invitation_Form", Function() New Exchange_Shared_Invitation_Form("member"),
                     {"xshi_Cancel", "xshi_Ok"}, "xshi_Cancel", "xshi_Ok", 0),
            New Spec("Exchange_Account_Limits_Form", Function() New Exchange_Account_Limits_Form("user@exchange.example", Nothing),
                     {"xlim_Cancel", "xlim_Ok"}, "xlim_Cancel", "xlim_Ok", 0),
            New Spec("Exchange_Admin_Audit_Form", Function() New Exchange_Admin_Audit_Form(New ExchangeAdminAuditView()),
                     {"xaud_Refresh", "xaud_Close"}, "xaud_Close", "xaud_Close", 0),
            New Spec("Exchange_Admin_Browser_Form", Function() New Exchange_Admin_Browser_Form("https://exch.example.com:22022/#admin", fp),
                     {"xabr_Close"}, "xabr_Close", "xabr_Close", 0),
            New Spec("Share_Access_Test_Form", Function() New Share_Access_Test_Form("127.0.0.1", 1),
                     {"xat_Close"}, "xat_Close", "xat_Close", 0),
            New Spec("Share_Status_Form", Function() New Share_Status_Form(),
                     {"xst_Reset", "xst_Refresh", "xst_Close"}, "xst_Close", "xst_Close", 0),
            New Spec("Folder_Outside_Access_Form", Function() New Folder_Outside_Access_Form("Photos", "D:\Photos", Nothing),
                     {"xfo_Close"}, "xfo_Close", "", 1)}
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
    Private Shared Function ShownAt(factory As Func(Of Form), factor As Single) As Form
        Dim form As Form = factory()
        Dim auto As Single = form.DeviceDpi / 96.0F
        Dim extra As Single = factor / auto
        If Math.Abs(extra - 1.0F) > 0.01F Then
            form.Font = New Font(form.Font.FontFamily, form.Font.SizeInPoints * extra)
            form.Scale(New SizeF(extra, extra))
        End If
        form.StartPosition = FormStartPosition.Manual
        form.Location = New Point(20, 20)
        form.Show()
        Application.DoEvents()
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
    Public Sub The_twelve_windows_keep_the_row_clear_of_the_content_and_inside_the_window_at_100_and_175_percent(code As String)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    Dim problems As New List(Of String)()
                    For Each spec As Spec In Specs()
                        For Each factor As Single In New Single() {1.0F, 1.75F}
                            Dim label As String = spec.Name & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                            Dim form As Form = ShownAt(spec.Make, factor)
                            Try
                                problems.AddRange(RowProblems(label, form))
                                ' at 100 % no window needs a scroll bar: the scrolling bodies are a safety net for a small monitor
                                If factor = 1.0F Then
                                    For Each sc As ScrollableControl In All(form).OfType(Of ScrollableControl)().Concat({CType(form, ScrollableControl)})
                                        If sc.AutoScroll AndAlso (sc.HorizontalScroll.Visible OrElse sc.VerticalScroll.Visible) Then
                                            problems.Add(label & ": " & If(String.IsNullOrEmpty(sc.Name), sc.GetType().Name, sc.Name) & " shows a scroll bar at 100 %")
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
    Public Sub The_twelve_windows_read_cancel_first_and_bind_escape_and_enter(code As String, mirrored As Boolean)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    Assert.Equal(mirrored, Localization.IsRightToLeft())
                    For Each spec As Spec In Specs()
                        Dim form As Form = ShownAt(spec.Make, 1.0F)
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

                            ' Enter = the safe default of the row (or nothing, for the one window that deliberately has none)
                            Dim accept As Button = TryCast(form.AcceptButton, Button)
                            If spec.AcceptName.Length = 0 Then
                                Assert.Null(accept)
                            Else
                                Assert.NotNull(accept)
                                Assert.Equal(spec.AcceptName, accept.Name)
                                Assert.Same(row.DefaultTarget, accept)
                                Assert.Contains(accept, reading)
                                ' never a command (they hand out access or reset counters) and never the irreversible one
                                Assert.NotEqual(ButtonGlyphRole.Irreversible, ButtonGlyphs.RoleOf(accept))
                                Assert.True(accept.Name = spec.CancelName OrElse ButtonGlyphs.RoleOf(accept) = ButtonGlyphRole.Affirmative,
                                            spec.Name & ": the default " & accept.Name & " is neither the dismissing nor the affirmative answer")
                            End If
                        Finally
                            Release(form)
                        End Try
                    Next
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    <Fact>
    Public Sub The_twelve_windows_keep_their_answer_names_inside_the_row_and_number_the_tab_order_in_reading_order()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                For Each spec As Spec In Specs()
                    Dim form As Form = ShownAt(spec.Make, 1.0F)
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
            End Sub)
    End Sub

    <Fact>
    Public Sub The_twelve_windows_keep_their_secret_fields_masked_and_empty()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                For Each spec As Spec In Specs()
                    Dim form As Form = ShownAt(spec.Make, 1.0F)
                    Try
                        Dim masked As List(Of TextBox) = All(form).OfType(Of TextBox)().Where(Function(t) t.UseSystemPasswordChar).ToList()
                        Assert.True(masked.Count = spec.Masked, spec.Name & ": expected " & spec.Masked.ToString(CultureInfo.InvariantCulture) & " masked fields, found " & masked.Count.ToString(CultureInfo.InvariantCulture))
                        For Each t As TextBox In masked
                            Assert.Equal("", t.Text)
                        Next
                    Finally
                        Release(form)
                    End Try
                Next
            End Sub)
    End Sub

    <Fact>
    Public Sub Cancel_of_the_password_change_dismisses_without_handing_a_password_over()
        ExchangeTestSupport.OnUi(
            Sub()
                ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                Using form As New Exchange_Password_Change_Form("user@exchange.example")
                    form.Show()
                    Application.DoEvents()
                    For Each t As TextBox In All(form).OfType(Of TextBox)()
                        t.Text = "Typed-Secret-1"
                    Next
                    DirectCast(form.CancelButton, Button).PerformClick()
                    Assert.Equal(DialogResult.Cancel, form.DialogResult)
                    Assert.Equal("", form.NewPassword)
                    Assert.Equal("", form.OldPassword)
                End Using
            End Sub)
    End Sub

    ''' <summary>The windows are adopters, not look-alikes: their source builds the row, binds it, and no longer sets
    ''' AcceptButton / CancelButton or places an answer button by hand.</summary>
    <Fact>
    Public Sub The_twelve_sources_adopt_the_row_and_the_Companion_links_it_once()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim src As String = Nothing
        While dir IsNot Nothing AndAlso src Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "DialogButtonRow.vb")) Then src = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(src)
        For Each file As String In New String() {"Exchange_Password_Change_Form", "Exchange_Link_Form", "Exchange_Connection_String_Form", "Exchange_Invitation_Create_Form",
                                                 "Exchange_Invitation_Created_Form", "Exchange_Shared_Invitation_Form", "Exchange_Account_Limits_Form",
                                                 "Exchange_Admin_Audit_Form", "Exchange_Admin_Browser_Form", "Share_Access_Test_Form", "Share_Status_Form",
                                                 "Folder_Outside_Access_Form"}
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "Forms", file & ".vb"))
            Assert.Contains("New DialogButtonRow()", text)
            Assert.Contains(".Bind(Me", text)
            Assert.Contains("DpiLayout.AddAnswer(", text)
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "\b(AcceptButton|CancelButton)\s*="), file & " still sets AcceptButton / CancelButton by hand")
        Next
        Dim project As String = System.IO.File.ReadAllText(Path.Combine(src, "FastMediaSorterCompanion", "FastMediaSorterCompanion.vbproj"))
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(project, "Include=""\.\.\\DialogButtonRow\.vb""").Count)
    End Sub

End Class
