#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' BUTTONS 2.0 sections 3 and 4 as the six password / resource windows hold them after they moved to the shared
''' <see cref="DialogButtonRow"/> (ticket 243, slice T-8b batch 1): Resource_Credential_Form (and its PIN mode),
''' Resource_Export_Form, Resource_Edit_Form, Secure_Password_Form, Resource_Phone_Code_Form and the viewer's
''' Exchange_Password_Change_Form. The row itself is held by <see cref="DialogButtonRowTests"/>; what is held here is
''' that each window reads its answers Cancel first, binds Escape and Enter to the right buttons, keeps the control names
''' the handlers and the other tests find it by, leaves its secret fields masked, and that the docked row never lies over
''' the hand-positioned content above it or leaves the client area - at 100 % and 175 %, in a left-to-right language
''' (en, de: the longest captions) and in a right-to-left one (ar). A scale is EMULATED the way DialogLayoutTests does it
''' (font grown by the factor, fixed pixels scaled with Control.Scale); how a window LOOKS stays manual acceptance.
''' </summary>
<Collection("LocalizationState")>
Public Class DialogButtonRowBatch1Tests

    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")

    Shared Sub New()
        ButtonGlyphs.AssetOpener = Function(logicalName As String) As Stream
                                       Dim glyphFile As String = Path.Combine(GlyphDir, Path.GetFileName(logicalName))
                                       Return If(File.Exists(glyphFile), CType(File.OpenRead(glyphFile), Stream), Nothing)
                                   End Function
    End Sub

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

    ' --- the six windows ---------------------------------------------------------------------

    ''' <summary>What a window must look like to its user: the buttons of its answer row in reading order (names), the
    ''' button Escape and Enter reach, the secret fields that must stay masked and the buttons that deliberately sit
    ''' outside the row.</summary>
    Private NotInheritable Class Spec
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly Reading As String()
        Friend ReadOnly CancelName As String
        Friend ReadOnly AcceptName As String
        Friend ReadOnly Masked As String()
        Friend ReadOnly Outside As String()

        Friend Sub New(name As String, make As Func(Of Form), reading As String(), cancelName As String, acceptName As String,
                       masked As String(), outside As String())
            Me.Name = name
            Me.Make = make
            Me.Reading = reading
            Me.CancelName = cancelName
            Me.AcceptName = acceptName
            Me.Masked = masked
            Me.Outside = outside
        End Sub
    End Class

    Private NotInheritable Class NoSecrets
        Implements IResourceSecretStore

        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
        End Sub

        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Return Nothing
        End Function

        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
        End Sub

        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return New List(Of String)()
        End Function
    End Class

    Private Shared Function Specs() As List(Of Spec)
        Dim pinned As New ResourceDescriptor With {
            .Id = "r1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "me", .RootPath = "/photos",
            .PinnedFingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"}
        Return New List(Of Spec) From {
            New Spec("Resource_Credential_Form", Function() New Resource_Credential_Form("My server with a rather long display name", "somebody-with-a-long-user-name", retry:=True),
                     {"resource_Cancel", "resource_Ok"}, "resource_Cancel", "resource_Ok", {"resource_Password"}, {}),
            New Spec("Resource_Credential_Form(pin)", Function() New Resource_Credential_Form("Private photos", "", retry:=True, showRemember:=False, pinMode:=True),
                     {"resource_Cancel", "resource_Ok"}, "resource_Cancel", "resource_Ok", {"resource_Password"}, {}),
            New Spec("Resource_Export_Form", Function() New Resource_Export_Form(pinned, New NoSecrets()),
                     {"res_ExportSave", "res_ExportCopyText", "res_ExportCopyCode", "res_ExportClose"}, "res_ExportClose", "res_ExportClose", {}, {}),
            New Spec("Resource_Edit_Form(new)", Function() New Resource_Edit_Form(Nothing),
                     {"res_Cancel", "res_Ok"}, "res_Cancel", "res_Ok", {"res_Password"}, {"res_ForgetKey"}),
            New Spec("Resource_Edit_Form(edit)", Function() New Resource_Edit_Form(pinned),
                     {"res_Cancel", "res_Ok"}, "res_Cancel", "res_Ok", {"res_Password"}, {"res_ForgetKey"}),
            New Spec("Secure_Password_Form", Function() New Secure_Password_Form("A-container-with-a-rather-long-name-photo-archive-2026.fd-sec", retry:=True),
                     {"secure_Cancel", "secure_Ok"}, "secure_Cancel", "secure_Ok", {"secure_Password"}, {}),
            New Spec("Resource_Phone_Code_Form", Function() New Resource_Phone_Code_Form(),
                     {"phone_Paste", "phone_Close"}, "phone_Close", "phone_Close", {}, {""}),
            New Spec("Exchange_Password_Change_Form", Function() New Exchange_Password_Change_Form("user@exchange.example"),
                     {"xpc_Cancel", "xpc_Ok"}, "xpc_Cancel", "xpc_Ok", {"xpc_OldBox", "xpc_NewBox", "xpc_RepeatBox"}, {})}
    End Function

    ' --- helpers -----------------------------------------------------------------------------

    Private Shared Function AllButtons(parent As Control) As List(Of Button)
        Dim found As New List(Of Button)()
        For Each c As Control In parent.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then found.Add(b)
            found.AddRange(AllButtons(c))
        Next
        Return found
    End Function

    Private Shared Function RowsOf(parent As Control) As List(Of DialogButtonRow)
        Dim found As New List(Of DialogButtonRow)()
        For Each c As Control In parent.Controls
            Dim r As DialogButtonRow = TryCast(c, DialogButtonRow)
            If r IsNot Nothing Then found.Add(r)
            found.AddRange(RowsOf(c))
        Next
        Return found
    End Function

    ''' <summary>Builds a window, emulates a display scale and shows it (OnLoad runs the reflow and the room for the row).</summary>
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
        Return form
    End Function

    ''' <summary>Closes (the phone window stops its one-time listener on close) and disposes.</summary>
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

        ' every visible button holds its caption and stays inside the client area
        Dim shown As List(Of Button) = AllButtons(form).Where(Function(b) b.Visible).ToList()
        For Each b As Button In shown
            Dim need As Integer = TextRenderer.MeasureText(b.Text.Replace("&", ""), b.Font).Width + ButtonGlyphs.ExtraWidth(b)
            If b.Width < need Then found.Add(label & ": " & b.Name & " '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide and its caption needs " & need.ToString(CultureInfo.InvariantCulture))
            Dim at As Rectangle = OnForm(form, b)
            If at.Left < client.Left - 1 OrElse at.Right > client.Right + 1 OrElse at.Top < client.Top - 1 OrElse at.Bottom > client.Bottom + 1 Then
                found.Add(label & ": " & b.Name & " " & at.ToString() & " is clipped by the window " & client.Size.ToString())
            End If
        Next

        ' no two buttons overlap
        For i As Integer = 0 To shown.Count - 1
            For j As Integer = i + 1 To shown.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(OnForm(form, shown(i)), OnForm(form, shown(j)))
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then
                    found.Add(label & ": " & shown(i).Name & " lies over " & shown(j).Name)
                End If
            Next
        Next

        ' one height per line, never below the target
        For Each line In row.ReadingButtons.GroupBy(Function(b) b.Top)
            If line.Select(Function(b) b.Height).Distinct().Count() <> 1 Then
                found.Add(label & ": the buttons of one line differ in height: " & String.Join(",", line.Select(Function(b) b.Height.ToString(CultureInfo.InvariantCulture))))
            End If
            If line.Any(Function(b) b.Height < 32) Then found.Add(label & ": a button is below the 32 target")
        Next

        ' the docked row lies over none of the hand-positioned content above it
        Dim rowBounds As Rectangle = OnForm(form, row)
        If rowBounds.Left < client.Left - 1 OrElse rowBounds.Right > client.Right + 1 OrElse rowBounds.Bottom > client.Bottom + 1 Then
            found.Add(label & ": the row " & rowBounds.ToString() & " leaves the client area " & client.Size.ToString())
        End If
        For Each c As Control In form.Controls
            If Object.ReferenceEquals(c, row) OrElse Not c.Visible OrElse c.Dock <> DockStyle.None Then Continue For
            Dim overlap As Rectangle = Rectangle.Intersect(c.Bounds, rowBounds)
            If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & c.Bounds.ToString() & " lies over the answer row " & rowBounds.ToString())
            End If
        Next
        Return found
    End Function

    ' --- the checks --------------------------------------------------------------------------

    <Theory>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ar")>
    Public Sub The_six_windows_keep_the_row_clear_of_the_content_and_inside_the_window_at_100_and_175_percent(code As String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each spec As Spec In Specs()
                          For Each factor As Single In New Single() {1.0F, 1.75F}
                              Dim label As String = spec.Name & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                              Dim form As Form = ShownAt(spec.Make, factor)
                              Try
                                  all.AddRange(RowProblems(label, form))
                                  ' the window-wide audit of DialogLayoutTests (leaves the client area, lies over a sibling, clipped text)
                                  For Each p As String In DialogLayoutTests.Problems(form)
                                      all.Add(label & ": " & p)
                                  Next
                              Finally
                                  Release(form)
                              End Try
                          Next
                      Next
                      Assert.True(all.Count = 0, String.Join(vbLf, all))
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData("en", False)>
    <InlineData("ar", True)>
    Public Sub The_six_windows_read_cancel_first_and_bind_escape_and_enter(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(rightToLeft, Localization.IsRightToLeft())
            OnSta(Sub()
                      For Each spec As Spec In Specs()
                          Dim form As Form = ShownAt(spec.Make, 1.0F)
                          Try
                              Dim row As DialogButtonRow = RowsOf(form).Single()
                              Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                              Assert.Equal(spec.Reading, reading.Select(Function(b) b.Name).ToArray())

                              ' the reading order is what the eye sees: earlier lines first, mirrored under right-to-left
                              For i As Integer = 0 To reading.Count - 2
                                  Assert.True(ReadsBefore(form, reading(i), reading(i + 1), rightToLeft),
                                              spec.Name & " " & code & ": " & reading(i).Name & " must read before " & reading(i + 1).Name)
                              Next

                              ' Escape = the dismissing answer, which really dismisses
                              Dim cancel As Button = DirectCast(form.CancelButton, Button)
                              Assert.NotNull(cancel)
                              Assert.Equal(spec.CancelName, cancel.Name)
                              Assert.Equal(DialogResult.Cancel, cancel.DialogResult)
                              Assert.Same(row.CancelTarget, cancel)

                              ' Enter = the safe default of the row, never one of the commands (they hand out access)
                              Dim accept As Button = DirectCast(form.AcceptButton, Button)
                              Assert.NotNull(accept)
                              Assert.Equal(spec.AcceptName, accept.Name)
                              Assert.Same(row.DefaultTarget, accept)
                              Assert.Contains(accept, reading)
                          Finally
                              Release(form)
                          End Try
                      Next
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_six_windows_keep_their_control_names_and_hold_every_answer_in_the_row()
        OnSta(Sub()
                  For Each spec As Spec In Specs()
                      Dim form As Form = ShownAt(spec.Make, 1.0F)
                      Try
                          Dim row As DialogButtonRow = RowsOf(form).Single()
                          For Each name As String In spec.Reading
                              Dim found As Control() = form.Controls.Find(name, True)
                              Assert.True(found.Length = 1 AndAlso TypeOf found(0) Is Button, spec.Name & ": '" & name & "' is exactly one button")
                              Assert.Contains(DirectCast(found(0), Button), row.ReadingButtons)
                          Next
                          ' nothing else that looks like an answer is left outside the row
                          Dim loose As List(Of String) = AllButtons(form).Where(Function(b) Not row.ReadingButtons.Contains(b)).Select(Function(b) b.Name).ToList()
                          Assert.Equal(spec.Outside.OrderBy(Function(s) s).ToArray(), loose.OrderBy(Function(s) s).ToArray())
                      Finally
                          Release(form)
                      End Try
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub The_row_is_last_in_the_tab_order_and_numbers_its_buttons_in_reading_order()
        OnSta(Sub()
                  For Each spec As Spec In Specs()
                      Dim form As Form = ShownAt(spec.Make, 1.0F)
                      Try
                          Dim row As DialogButtonRow = RowsOf(form).Single()
                          ' the fields keep their order and the answers come after all of them
                          Assert.Equal(form.Controls.Count - 1, form.Controls.GetChildIndex(row))
                          Dim tabs As Integer() = row.ReadingButtons.Select(Function(b) b.TabIndex).ToArray()
                          Assert.Equal(Enumerable.Range(0, tabs.Length).ToArray(), tabs)
                      Finally
                          Release(form)
                      End Try
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub The_password_windows_keep_their_secret_fields_masked()
        OnSta(Sub()
                  For Each spec As Spec In Specs()
                      If spec.Masked.Length = 0 Then Continue For
                      Dim form As Form = ShownAt(spec.Make, 1.0F)
                      Try
                          For Each name As String In spec.Masked
                              Dim box As TextBox = TryCast(form.Controls.Find(name, True).Single(), TextBox)
                              Assert.NotNull(box)
                              Assert.True(box.UseSystemPasswordChar, spec.Name & ": " & name & " stays masked")
                              Assert.Equal("", box.Text)
                          Next
                      Finally
                          Release(form)
                      End Try
                  Next
              End Sub)
    End Sub

    ''' <summary>The export window had a label anchored to the bottom edge, which a docked row that wraps would have
    ''' ended up under; the room the row needs is made by growing the window, so a longer language never overlaps.</summary>
    <Fact>
    Public Sub The_export_window_does_not_anchor_its_status_line_to_the_edge_under_the_row()
        OnSta(Sub()
                  Dim pinned As New ResourceDescriptor With {.Id = "r1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp, .Host = "nas.local", .Port = 22, .UserName = "me", .RootPath = "/photos"}
                  Using form As New Resource_Export_Form(pinned, New NoSecrets())
                      Dim status As Control = form.Controls.Find("res_Export_Status", True).Single()
                      Assert.Equal(AnchorStyles.None, status.Anchor And AnchorStyles.Bottom)
                  End Using
              End Sub)
    End Sub

    ''' <summary>The six windows are adopters, not look-alikes: their source builds the row, binds it, and asks the layout
    ''' for the room it needs, and no longer places an answer button by hand.</summary>
    <Fact>
    Public Sub The_six_sources_adopt_the_row_and_no_longer_place_answer_buttons_by_hand()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim src As String = Nothing
        While dir IsNot Nothing AndAlso src Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then src = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(src)
        For Each file As String In New String() {"Resource_Credential_Form.vb", "Resource_Export_Form.vb", "Resource_Edit_Form.vb",
                                                 "Secure_Password_Form.vb", "Resources\Resource_Phone_Code_Form.vb", "Exchange_Password_Change_Form.vb"}
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(src, file))
            Assert.Contains("New DialogButtonRow()", text)
            Assert.Contains(".Bind(Me)", text)
            Assert.Contains("DialogLayout.KeepRoomForRow(Me,", text)
            ' the row applies the glyphs (Add) and Bind sets Accept / Cancel; the window does neither by hand any more
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "ButtonGlyphs\.Apply\((okButton|dismissButton|cancelButton_|_closeButton|_pasteButton|closeButton|saveButton|copyTextButton|copyCodeButton)\b"),
                         file & " still applies an answer glyph by hand")
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "\b(AcceptButton|CancelButton)\s*="), file & " still sets AcceptButton / CancelButton by hand")
        Next
    End Sub
End Class
#End If
