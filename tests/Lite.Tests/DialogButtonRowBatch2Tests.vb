#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' BUTTONS 2.0 sections 3 and 4 as the second batch of viewer windows holds them after they moved to the shared
''' <see cref="DialogButtonRow"/> (ticket 243, slice T-8b batch 2): Exchange_Connect_Form (add mode, registration mode and
''' a switch between them), LiveBroadcast_Form, the two windows of Video_Convert_Form.vb (the progress window and the
''' "Replace with video" confirmation), File_Details_Form and Connection_Diagnosis_Dialog. The row itself is held by
''' <see cref="DialogButtonRowTests"/>, the first batch by <see cref="DialogButtonRowBatch1Tests"/>; what is held here is that each
''' window reads its answers Cancel first (the acting answer of the irreversible confirmation last, and never the
''' default), binds Escape and Enter to the right buttons, keeps the control names the handlers and the other tests find
''' it by, leaves its secret fields masked, and that the docked row never lies over the content above it or leaves the
''' client area - at 100 % and 175 %, in a left-to-right language (en, de: the longest captions) and in a right-to-left
''' one (ar). A scale is EMULATED the way DialogLayoutTests does it (font grown by the factor, fixed pixels scaled with
''' Control.Scale); how a window LOOKS stays manual acceptance. The welcome wizard is deliberately NOT here: its
''' footer keeps the 44 px pointer target of spec 184 rule 6, which the row (a 32 px target, no per-window override)
''' cannot express.
''' </summary>
<Collection("LocalizationState")>
Public Class DialogButtonRowBatch2Tests

    Private Shared ReadOnly GlyphDir As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs")
    Private Shared ReadOnly DetailsFile As String = Path.Combine(Path.GetTempPath(), "fms-batch2-details-" & Guid.NewGuid().ToString("N") & ".png")

    Shared Sub New()
        ButtonGlyphs.AssetOpener = Function(logicalName As String) As Stream
                                       Dim glyphFile As String = Path.Combine(GlyphDir, Path.GetFileName(logicalName))
                                       Return If(File.Exists(glyphFile), CType(File.OpenRead(glyphFile), Stream), Nothing)
                                   End Function
        Try
            File.WriteAllBytes(DetailsFile, New Byte() {1, 2, 3})
        Catch ex As IOException
        End Try
        AddHandler AppDomain.CurrentDomain.ProcessExit, Sub()
                                                            Try
                                                                File.Delete(DetailsFile)
                                                            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                                                            End Try
                                                        End Sub
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

    ' --- the windows -------------------------------------------------------------------------

    ''' <summary>What a window must look like to its user. A button is named by its control name; a window that gave its answer
    ''' buttons no name is read by the private field that holds it, or - for the two unnamed locals of the confirmation - by
    ''' "dr:" and its DialogResult.</summary>
    Private NotInheritable Class Spec
        Friend ReadOnly Name As String
        Friend ReadOnly Make As Func(Of Form)
        Friend ReadOnly Reading As String()
        Friend ReadOnly CancelId As String
        Friend ReadOnly AcceptId As String
        Friend ReadOnly CancelResult As DialogResult
        Friend ReadOnly Masked As String()
        Friend ReadOnly Outside As String()
        Friend ReadOnly Prepare As Action(Of Form)
        ''' <summary>True for a window whose texts are never right-to-left: the diagnosis dialog has its own table of the
        ''' contract's thirteen languages, Arabic is not among them (the English text is shown), so the window stays
        ''' left-to-right under "ar" and its row reads left to right with it.</summary>
        Friend ReadOnly LeftToRight As Boolean

        Friend Sub New(name As String, make As Func(Of Form), reading As String(), cancelId As String, acceptId As String,
                       cancelResult As DialogResult, masked As String(), outside As String(), Optional prepare As Action(Of Form) = Nothing,
                       Optional leftToRight As Boolean = False)
            Me.LeftToRight = leftToRight
            Me.Name = name
            Me.Make = make
            Me.Reading = reading
            Me.CancelId = cancelId
            Me.AcceptId = acceptId
            Me.CancelResult = cancelResult
            Me.Masked = masked
            Me.Outside = outside
            Me.Prepare = prepare
        End Sub
    End Class

    Private Shared Function InvitationText() As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build("exchange.example:443", "", "", "", "abcdefghijklmnopqrstuA")
        Dim refused As ExchangeConnectionStringResult = Nothing
        Return ExchangeConnectionString.Format(built.Value, refused)
    End Function

    Private Shared Function Diagnosis(withSteps As Boolean) As Form
        Dim result As New ConnectionDiagnosisResult(ConnectionDiagnosisMessages.UnreachableServer, ConnectionDiagnosisTarget.Server, ConnectionDiagnosisApplies.Network)
        If withSteps Then
            result.WalkedSteps.Add(ConnectionDiagnosisStep.PcAsleep)
            result.WalkedSteps.Add(ConnectionDiagnosisStep.AddressLan)
            result.WalkedSteps.Add(ConnectionDiagnosisStep.Vpn)
            result.FirstFailedStep = ConnectionDiagnosisStep.PcAsleep
        End If
        Return New Connection_Diagnosis_Dialog(result)
    End Function

    Private Shared Sub Press(form As Form, fieldName As String)
        FieldButton(form, fieldName).PerformClick()
    End Sub

    Private Shared Function Specs() As List(Of Spec)
        Dim connectNames As String() = {"xc_Cancel", "xc_Connect"}
        Dim secretFields As String() = {"xc_Password", "xc_Password2"}
        Dim looseConnect As String() = {"xc_Paste", "xc_OpenFile"}
        ' Beside the input and not answers: the Browse button (named by its field), Paste and - behind the exchange gate - the
        ' "My devices" list switch (both unnamed locals).
        Dim looseLive As String() = If(ExchangeFeatures.IsEnabled(), New String() {"", "", "btnBrowseFile"}, New String() {"", "btnBrowseFile"})
        Dim diagnosisNames As String() = {"_btnNextStep", "_btnDetails", "_btnCopy", "_btnClose"}
        Return New List(Of Spec) From {
            New Spec("Exchange_Connect_Form", Function() New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts()),
                     connectNames, "xc_Cancel", "xc_Connect", DialogResult.Cancel, secretFields, looseConnect),
            New Spec("Exchange_Connect_Form(register)", Function() New Exchange_Connect_Form(InvitationText(), Nothing, New ExchangeConsumerPorts()),
                     connectNames, "xc_Cancel", "xc_Connect", DialogResult.Cancel, secretFields, looseConnect),
            New Spec("Exchange_Connect_Form(switched to register)", Function() New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts()),
                     connectNames, "xc_Cancel", "xc_Connect", DialogResult.Cancel, secretFields, looseConnect,
                     Sub(f) DirectCast(f.Controls("xc_Input"), TextBox).Text = InvitationText()),
            New Spec("LiveBroadcast_Form", Function() New LiveBroadcast_Form(),
                     {"btnCancel", "btnConnect"}, "btnCancel", "btnConnect", DialogResult.Cancel, {}, looseLive),
            New Spec("Video_Convert_Form", Function() New Video_Convert_Form(determinate:=True),
                     {"cancelButton"}, "cancelButton", "cancelButton", DialogResult.Cancel, {}, {}),
            New Spec("Video_Replace_Confirm_Form", Function() New Video_Replace_Confirm_Form("A-photo-with-a-rather-long-file-name.webp", "A-photo-with-a-rather-long-file-name.mp4", warnAboutAlpha:=False),
                     {"dr:Cancel", "dr:OK"}, "dr:Cancel", "dr:Cancel", DialogResult.Cancel, {}, {}),
            New Spec("Video_Replace_Confirm_Form(alpha)", Function() New Video_Replace_Confirm_Form("A-photo-with-a-rather-long-file-name.webp", "A-photo-with-a-rather-long-file-name.mp4", warnAboutAlpha:=True),
                     {"dr:Cancel", "dr:OK"}, "dr:Cancel", "dr:Cancel", DialogResult.Cancel, {}, {}),
            New Spec("File_Details_Form", Function() New File_Details_Form(DetailsFile),
                     {"details_Copy", "details_Close"}, "details_Close", Nothing, DialogResult.Cancel, {}, {}),
            New Spec("Connection_Diagnosis_Dialog(steps)", Function() Diagnosis(True),
                     diagnosisNames, "_btnClose", "_btnClose", DialogResult.Cancel, {}, {"_btnOpenSettings"}, leftToRight:=True),
            New Spec("Connection_Diagnosis_Dialog(details shown)", Function() Diagnosis(True),
                     diagnosisNames, "_btnClose", "_btnClose", DialogResult.Cancel, {}, {"_btnOpenSettings"},
                     Sub(f) Press(f, "_btnDetails"), leftToRight:=True),
            New Spec("Connection_Diagnosis_Dialog(no steps)", Function() Diagnosis(False),
                     diagnosisNames, "_btnClose", "_btnClose", DialogResult.Cancel, {}, {"_btnOpenSettings"}, leftToRight:=True)}
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

    Private Shared Function FieldButton(form As Form, fieldName As String) As Button
        Dim field As FieldInfo = form.GetType().GetField(fieldName, BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public)
        Assert.True(field IsNot Nothing, form.GetType().Name & " has no field '" & fieldName & "'")
        Return DirectCast(field.GetValue(form), Button)
    End Function

    ''' <summary>How a spec names a button: its control name, else the private field of the window that holds it, else (the
    ''' two locals of the confirmation) "dr:" and its DialogResult.</summary>
    Private Shared Function IdOf(form As Form, button As Button) As String
        If Not String.IsNullOrEmpty(button.Name) Then Return button.Name
        For Each field As FieldInfo In form.GetType().GetFields(BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public)
            If field.FieldType = GetType(Button) AndAlso Object.ReferenceEquals(field.GetValue(form), button) Then Return field.Name
        Next
        If button.DialogResult <> DialogResult.None Then Return "dr:" & button.DialogResult.ToString()
        Return ""
    End Function

    ''' <summary>Builds a window, emulates a display scale, shows it (OnLoad runs the reflow and the room for the row) and lets
    ''' the spec put it in the state under test.</summary>
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
        If spec.Prepare IsNot Nothing Then
            spec.Prepare(form)
            form.PerformLayout()
        End If
        Return form
    End Function

    ''' <summary>Closes and disposes. The progress window refuses a close before its work is finished (it means Cancel), so it is
    ''' finished first; the others close at once.</summary>
    Private Shared Sub Release(form As Form)
        Try
            Dim progress As Video_Convert_Form = TryCast(form, Video_Convert_Form)
            If progress IsNot Nothing Then progress.Finish()
            If Not form.IsDisposed Then form.Close()
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

    ''' <summary>The sentences that are wrong with the answer row of a shown window (empty = sound). A hidden button keeps its
    ''' slot in the row but takes no part in the checks: the user does not see it.</summary>
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
            If b.Width < need Then found.Add(label & ": " & IdOf(form, b) & " '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide and its caption needs " & need.ToString(CultureInfo.InvariantCulture))
            Dim at As Rectangle = OnForm(form, b)
            If at.Left < client.Left - 1 OrElse at.Right > client.Right + 1 OrElse at.Top < client.Top - 1 OrElse at.Bottom > client.Bottom + 1 Then
                found.Add(label & ": " & IdOf(form, b) & " " & at.ToString() & " is clipped by the window " & client.Size.ToString())
            End If
        Next

        ' no two buttons overlap
        For i As Integer = 0 To shown.Count - 1
            For j As Integer = i + 1 To shown.Count - 1
                Dim overlap As Rectangle = Rectangle.Intersect(OnForm(form, shown(i)), OnForm(form, shown(j)))
                If overlap.Width > 0 AndAlso overlap.Height > 0 Then
                    found.Add(label & ": " & IdOf(form, shown(i)) & " lies over " & IdOf(form, shown(j)))
                End If
            Next
        Next

        ' one height per line, never below the target
        For Each line In row.ReadingButtons.Where(Function(b) b.Visible).GroupBy(Function(b) b.Top)
            If line.Select(Function(b) b.Height).Distinct().Count() <> 1 Then
                found.Add(label & ": the buttons of one line differ in height: " & String.Join(",", line.Select(Function(b) b.Height.ToString(CultureInfo.InvariantCulture))))
            End If
            If line.Any(Function(b) b.Height < 32) Then found.Add(label & ": a button is below the 32 target")
        Next

        ' the docked row lies over none of the content above it, hand-positioned or docked
        Dim rowBounds As Rectangle = OnForm(form, row)
        If rowBounds.Left < client.Left - 1 OrElse rowBounds.Right > client.Right + 1 OrElse rowBounds.Bottom > client.Bottom + 1 Then
            found.Add(label & ": the row " & rowBounds.ToString() & " leaves the client area " & client.Size.ToString())
        End If
        For Each c As Control In form.Controls
            If Object.ReferenceEquals(c, row) OrElse Not c.Visible Then Continue For
            Dim overlap As Rectangle = Rectangle.Intersect(OnForm(form, c), rowBounds)
            If overlap.Width > 1 AndAlso overlap.Height > 1 Then
                found.Add(label & ": " & If(String.IsNullOrEmpty(c.Name), c.GetType().Name, c.Name) & " " & OnForm(form, c).ToString() & " lies over the answer row " & rowBounds.ToString())
            End If
        Next
        Return found
    End Function

    ' --- the checks --------------------------------------------------------------------------

    <Theory>
    <InlineData("en")>
    <InlineData("de")>
    <InlineData("ar")>
    Public Sub The_batch2_windows_keep_the_row_clear_of_the_content_and_inside_the_window_at_100_and_175_percent(code As String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each spec As Spec In Specs()
                          For Each factor As Single In New Single() {1.0F, 1.75F}
                              Dim label As String = spec.Name & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                              Dim form As Form = ShownAt(spec, factor)
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
    Public Sub The_batch2_windows_read_cancel_first_and_bind_escape_and_enter(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(rightToLeft, Localization.IsRightToLeft())
            OnSta(Sub()
                      For Each spec As Spec In Specs()
                          Dim form As Form = ShownAt(spec, 1.0F)
                          Try
                              Dim row As DialogButtonRow = RowsOf(form).Single()
                              Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                              Assert.Equal(spec.Reading, reading.Select(Function(b) IdOf(form, b)).ToArray())

                              ' the reading order is what the eye sees: earlier lines first, mirrored under right-to-left;
                              ' a button the window hid for now (Next step with no next step) is not looked at
                              Dim seen As List(Of Button) = reading.Where(Function(b) b.Visible).ToList()
                              For i As Integer = 0 To seen.Count - 2
                                  Assert.True(ReadsBefore(form, seen(i), seen(i + 1), rightToLeft AndAlso Not spec.LeftToRight),
                                              spec.Name & " " & code & ": " & IdOf(form, seen(i)) & " must read before " & IdOf(form, seen(i + 1)) &
                                              " (" & String.Join(" ", seen.Select(Function(b) IdOf(form, b) & OnForm(form, b).ToString())) & " in " & form.ClientSize.ToString() & ")")
                              Next

                              ' Escape = the dismissing answer, which really dismisses
                              Dim cancel As Button = DirectCast(form.CancelButton, Button)
                              Assert.NotNull(cancel)
                              Assert.Equal(spec.CancelId, IdOf(form, cancel))
                              Assert.Equal(spec.CancelResult, cancel.DialogResult)
                              Assert.Same(row.CancelTarget, cancel)

                              ' Enter = the safe default of the row (never a command, never the acting answer of an irreversible
                              ' confirmation); the information window has none on purpose
                              If spec.AcceptId Is Nothing Then
                                  Assert.Null(form.AcceptButton)
                              Else
                                  Dim accept As Button = DirectCast(form.AcceptButton, Button)
                                  Assert.NotNull(accept)
                                  Assert.Equal(spec.AcceptId, IdOf(form, accept))
                                  Assert.Same(row.DefaultTarget, accept)
                                  Assert.Contains(accept, reading)
                              End If
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
    Public Sub The_batch2_windows_keep_their_control_names_and_hold_every_answer_in_the_row()
        OnSta(Sub()
                  For Each spec As Spec In Specs()
                      Dim form As Form = ShownAt(spec, 1.0F)
                      Try
                          Dim row As DialogButtonRow = RowsOf(form).Single()
                          For Each id As String In spec.Reading
                              Dim hit As Button = row.ReadingButtons.Where(Function(b) IdOf(form, b) = id).SingleOrDefault()
                              Assert.True(hit IsNot Nothing, spec.Name & ": '" & id & "' is exactly one button of the row")
                              ' a named button is also found by name through the tree, the way the handlers and other tests find it
                              If Not id.StartsWith("dr:", StringComparison.Ordinal) AndAlso hit.Name.Length > 0 Then
                                  Dim found As Control() = form.Controls.Find(id, True)
                                  Assert.True(found.Length = 1 AndAlso Object.ReferenceEquals(found(0), hit), spec.Name & ": '" & id & "' is found once by name")
                              End If
                          Next
                          ' nothing else that looks like an answer is left outside the row
                          Dim loose As List(Of String) = AllButtons(form).Where(Function(b) Not row.ReadingButtons.Contains(b)).Select(Function(b) IdOf(form, b)).ToList()
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
                      Dim form As Form = ShownAt(spec, 1.0F)
                      Try
                          Dim row As DialogButtonRow = RowsOf(form).Single()
                          ' the fields keep their order and the answers come after all of them
                          Assert.True(form.Controls.GetChildIndex(row) = form.Controls.Count - 1, spec.Name & ": the row is the last child of the window")
                          Dim tabs As Integer() = row.ReadingButtons.Select(Function(b) b.TabIndex).ToArray()
                          Assert.Equal(Enumerable.Range(0, tabs.Length).ToArray(), tabs)
                      Finally
                          Release(form)
                      End Try
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub The_connect_windows_keep_their_secret_fields_masked_and_empty()
        OnSta(Sub()
                  For Each spec As Spec In Specs()
                      If spec.Masked.Length = 0 Then Continue For
                      Dim form As Form = ShownAt(spec, 1.0F)
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

    ''' <summary>The confirmation deletes the original past the Recycle Bin: its acting answer takes the affirmative place (last),
    ''' Cancel reads first and is the default, so one Enter never converts.</summary>
    <Fact>
    Public Sub The_replace_confirmation_puts_the_destructive_answer_last_and_never_makes_it_the_default()
        OnSta(Sub()
                  Using form As New Video_Replace_Confirm_Form("a.webp", "a.mp4", warnAboutAlpha:=False)
                      form.StartPosition = FormStartPosition.Manual
                      form.Location = New Point(20, 20)
                      form.Show()
                      Dim row As DialogButtonRow = RowsOf(form).Single()
                      Dim acting As Button = row.ReadingButtons.Last()
                      Assert.Equal(DialogResult.OK, acting.DialogResult)
                      Assert.NotSame(acting, form.AcceptButton)
                      Assert.NotSame(acting, row.DefaultTarget)
                      Assert.Equal(DialogResult.Cancel, DirectCast(form.AcceptButton, Button).DialogResult)
                      Assert.Equal(DialogResult.Cancel, DirectCast(form.CancelButton, Button).DialogResult)
                  End Using
              End Sub)
    End Sub

    ''' <summary>The registration mode renames the Connect button; the row re-measures it and the window keeps the room.</summary>
    <Fact>
    Public Sub The_connect_window_row_follows_the_caption_of_the_mode()
        OnSta(Sub()
                  Using form As New Exchange_Connect_Form("exchange.example:443", Nothing, New ExchangeConsumerPorts())
                      form.StartPosition = FormStartPosition.Manual
                      form.Location = New Point(20, 20)
                      form.Show()
                      Dim connect As Button = DirectCast(form.Controls.Find("xc_Connect", True).Single(), Button)
                      Dim addCaption As String = connect.Text
                      Dim addWidth As Integer = connect.Width
                      DirectCast(form.Controls("xc_Input"), TextBox).Text = InvitationText()
                      Assert.NotEqual(addCaption, connect.Text)
                      Assert.True(connect.Width >= TextRenderer.MeasureText(connect.Text, connect.Font).Width + ButtonGlyphs.ExtraWidth(connect), "the registration caption is not clipped")
                      DirectCast(form.Controls("xc_Input"), TextBox).Text = "exchange.example:443"
                      Assert.Equal(addCaption, connect.Text)
                      Assert.Equal(addWidth, connect.Width)
                  End Using
              End Sub)
    End Sub

    ''' <summary>The windows are adopters, not look-alikes: their source builds the row, binds it, and no longer places an
    ''' answer button by hand. The hand-positioned ones also ask the layout for the room the row needs; the docked ones cannot
    ''' lie under it.</summary>
    <Fact>
    Public Sub The_batch2_sources_adopt_the_row_and_no_longer_place_answer_buttons_by_hand()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim src As String = Nothing
        While dir IsNot Nothing AndAlso src Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then src = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(src)
        Dim handPositioned As String() = {"Exchange_Connect_Form.vb", "LiveBroadcast_Form.vb", "Video\Video_Convert_Form.vb"}
        Dim docked As String() = {"File_Details_Form.vb", "Exchange\Connection_Diagnosis_Dialog.vb"}
        For Each file As String In handPositioned.Concat(docked)
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(src, file))
            Assert.Contains("New DialogButtonRow()", text)
            Assert.Contains(".Bind(Me", text)
            If handPositioned.Contains(file) Then Assert.Contains("DialogLayout.KeepRoomForRow(Me,", text)
            ' the row applies the glyphs (Add) and Bind sets Accept / Cancel; the window does neither by hand any more
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "ButtonGlyphs\.Apply\((connectButton|cancelButton_|btnConnect|btnCancel|cancelButton|accept|decline|btnCopy|btnClose|_btnCopy|_btnClose|_btnNextStep|_btnDetails)\b"),
                         file & " still applies an answer glyph by hand")
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "\b(AcceptButton|CancelButton)\s*="), file & " still sets AcceptButton / CancelButton by hand")
        Next
    End Sub

    ''' <summary>The welcome wizard keeps its own footer on purpose (spec 184 rule 6: a 44 px pointer target for every button):
    ''' the shared row has a 32 px target and no per-window override, so the wizard is not an adopter. This holds the reason
    ''' in the source, so a later conversion is a decision and not an accident.</summary>
    <Fact>
    Public Sub The_welcome_wizard_footer_keeps_its_44_px_pointer_target_and_is_not_an_adopter()
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        Dim path1 As String = Nothing
        While dir IsNot Nothing AndAlso path1 Is Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", "Welcome", "Welcome_Form.vb")
            If File.Exists(candidate) Then path1 = candidate
            dir = dir.Parent
        End While
        Assert.NotNull(path1)
        Dim text As String = File.ReadAllText(path1)
        Assert.Contains("Private Const TargetHeight As Integer = 44", text)
        Assert.DoesNotContain("New DialogButtonRow()", text)
    End Sub
End Class
#End If
