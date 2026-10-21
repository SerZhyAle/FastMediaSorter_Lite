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
''' BUTTONS 2.0 sections 3 and 4 as the one dialog button row holds them (ticket 243, slice T-8a): the pure wrap and
''' order model, the control in a left-to-right and a right-to-left window, and the four windows that adopted it
''' (built offscreen at 100 / 150 / 175 percent in German, French and Portuguese, where the captions are longest).
''' A window cannot be looked at here; a scale is emulated the way DialogLayoutTests does (font grown by the factor,
''' fixed pixels scaled with Control.Scale).
''' </summary>
<Collection("LocalizationState")>
Public Class DialogButtonRowTests

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

    ' --- the pure model ----------------------------------------------------------------------

    Private Shared Function Items(ParamArray contentWidths As Integer()) As List(Of DialogButtonRowItem)
        Return contentWidths.Select(Function(w) New DialogButtonRowItem(w, True, 20)).ToList()
    End Function

    Private Shared Function Plan(list As List(Of DialogButtonRowItem), available As Double,
                                 Optional wrapped As Func(Of Integer, Integer, Integer) = Nothing) As DialogButtonRowLayout
        Return DialogButtonRowModel.Layout(list, available, 12, 8, 16, 32, wrapped)
    End Function

    <Fact>
    Public Sub A_row_that_fits_is_one_line_with_content_widths()
        Dim result As DialogButtonRowLayout = Plan(Items(100, 80), 1000)
        Assert.Single(result.Lines)
        Assert.Equal(New Integer() {124, 104}, result.Widths.ToArray())
        Assert.Equal(New Integer() {0, 8}, result.GapBefore.ToArray())
        Assert.Equal(124 + 8 + 104, result.LineWidths(0))
        Assert.Equal(32, result.TotalHeight)
    End Sub

    <Fact>
    Public Sub A_button_that_does_not_fit_moves_whole_to_the_next_line_and_no_caption_wraps()
        ' 124 + 8 + 104 = 236 fits in 250, the third (114 wide) would make 358
        Dim result As DialogButtonRowLayout = Plan(Items(100, 80, 90), 250)
        Assert.Equal(2, result.Lines.Count)
        Assert.Equal(New Integer() {0, 1}, result.Lines(0).ToArray())
        Assert.Equal(New Integer() {2}, result.Lines(1).ToArray())
        Assert.Equal(New Integer() {124, 104, 114}, result.Widths.ToArray())
        Assert.Equal(0, result.GapBefore(2))
        Assert.Equal(32 + 8 + 32, result.TotalHeight)
    End Sub

    <Fact>
    Public Sub Every_line_has_one_height_the_tallest_member_and_never_below_the_target()
        Dim list As New List(Of DialogButtonRowItem) From {
            New DialogButtonRowItem(100, True, 20),
            New DialogButtonRowItem(100, True, 45),
            New DialogButtonRowItem(100, True, 10)}
        ' Each button is 124 wide: 124 + 8 + 124 = 256 fits in 260, the third one moves to a second line.
        Dim result As DialogButtonRowLayout = Plan(list, 260)
        Assert.Equal(2, result.Lines.Count)
        Assert.Equal(45, result.Heights(0))
        Assert.Equal(45, result.Heights(1))
        Assert.Equal(32, result.Heights(2))
        Assert.Equal(New Integer() {45, 32}, result.LineHeights.ToArray())
        Assert.Equal(45 + 8 + 32, result.TotalHeight)
        Assert.All(result.Heights, Sub(h) Assert.True(h >= 32))
    End Sub

    <Fact>
    Public Sub Only_a_glyph_only_button_has_a_width_floor()
        Dim glyphOnly As New DialogButtonRowItem(4, False, 20)
        Dim captioned As New DialogButtonRowItem(4, True, 20)
        Dim result As DialogButtonRowLayout = Plan(New List(Of DialogButtonRowItem) From {glyphOnly, captioned}, 500)
        Assert.Equal(32, result.Widths(0))
        Assert.Equal(4 + 24, result.Widths(1))
        Assert.Equal(32, DialogButtonRowModel.ButtonWidth(0, False, 12, 32))
        Assert.Equal(40, DialogButtonRowModel.ButtonWidth(16, False, 12, 32))
    End Sub

    <Theory>
    <InlineData(0.0R)>
    <InlineData(-5.0R)>
    <InlineData(Double.NaN)>
    <InlineData(Double.NegativeInfinity)>
    <InlineData(Double.PositiveInfinity)>
    <InlineData(0.4R)>
    Public Sub An_unknown_width_is_one_natural_line_and_never_throws(available As Double)
        Dim result As DialogButtonRowLayout = Plan(Items(100, 80, 90), available)
        Assert.Single(result.Lines)
        Assert.Equal(New Integer() {124, 104, 114}, result.Widths.ToArray())
        Assert.Equal(32, result.TotalHeight)
    End Sub

    <Fact>
    Public Sub An_empty_row_and_negative_metrics_are_safe()
        Dim empty As DialogButtonRowLayout = DialogButtonRowModel.Layout(New List(Of DialogButtonRowItem)(), 300, 12, 8, 16, 32, Nothing)
        Assert.Empty(empty.Lines)
        Assert.Equal(0, empty.TotalHeight)
        Assert.Equal(0, empty.MaxLineWidth)
        Dim odd As DialogButtonRowLayout = DialogButtonRowModel.Layout(Items(-50, 0), 300, -3, -4, -5, -6, Nothing)
        Assert.Single(odd.Lines)
        Assert.All(odd.Widths, Sub(w) Assert.True(w >= 0))
        Assert.Equal(0, DialogButtonRowModel.Layout(Nothing, 300, 12, 8, 16, 32, Nothing).TotalHeight)
    End Sub

    <Fact>
    Public Sub Buttons_are_never_stretched_and_never_made_equal()
        Dim result As DialogButtonRowLayout = Plan(Items(30, 100, 220), 5000)
        Assert.Equal(New Integer() {54, 124, 244}, result.Widths.ToArray())
        Assert.True(result.LineWidths(0) < 5000 - 1000, "spare width stays empty")
        Assert.Equal(3, result.Widths.Distinct().Count())
    End Sub

    <Fact>
    Public Sub A_button_wider_than_the_line_takes_the_line_and_its_caption_wraps_and_grows_it()
        Dim asked As New List(Of KeyValuePair(Of Integer, Integer))()
        Dim result As DialogButtonRowLayout = Plan(Items(100, 600), 300,
            Function(index, inner)
                asked.Add(New KeyValuePair(Of Integer, Integer)(index, inner))
                Return 70
            End Function)
        Assert.Equal(2, result.Lines.Count)
        Assert.Equal(300, result.Widths(1))
        Assert.Equal(300 - 24, asked.Single().Value)
        Assert.Equal(1, asked.Single().Key)
        Assert.Equal(70, result.Heights(1))
        Assert.Equal(32, result.Heights(0))
        Assert.Equal(32 + 8 + 70, result.TotalHeight)
    End Sub

    <Fact>
    Public Sub A_group_moves_together_when_it_fits_a_line_of_its_own()
        Dim list As New List(Of DialogButtonRowItem) From {
            New DialogButtonRowItem(100, True, 20),
            New DialogButtonRowItem(100, True, 20, startsGroup:=True),
            New DialogButtonRowItem(100, True, 20)}
        ' first alone 124; the group 124 + 8 + 124 = 256 would need 124 + 16 + 256 = 396
        Dim together As DialogButtonRowLayout = Plan(list, 300)
        Assert.Equal(2, together.Lines.Count)
        Assert.Equal(New Integer() {0}, together.Lines(0).ToArray())
        Assert.Equal(New Integer() {1, 2}, together.Lines(1).ToArray())
        Assert.Equal(16, Plan(list, 5000).GapBefore(1))
        Assert.Equal(8, Plan(list, 5000).GapBefore(2))
        ' a group wider than the line wraps inside
        Dim inner As DialogButtonRowLayout = Plan(list, 200)
        Assert.Equal(3, inner.Lines.Count)
    End Sub

    <Fact>
    Public Sub The_order_function_reads_commands_cancel_apply_and_the_final_affirmative_last()
        Dim D As DialogButtonRole = DialogButtonRole.Dismissing
        Dim N As DialogButtonRole = DialogButtonRole.Neutral
        Dim A As DialogButtonRole = DialogButtonRole.Affirmative
        Dim X As DialogButtonRole = DialogButtonRole.Destructive
        Dim C As DialogButtonRole = DialogButtonRole.Command
        ' the call-site order is the ACTING answer first, the cancel answer last
        Assert.Equal(New Integer() {1, 0}, DialogButtonRowModel.ReadingOrder({A, D}))
        Assert.Equal(New Integer() {2, 1, 0}, DialogButtonRowModel.ReadingOrder({A, N, D}))
        Assert.Equal(New Integer() {3, 1, 2, 0}, DialogButtonRowModel.ReadingOrder({A, N, N, D}))
        Assert.Equal(New Integer() {1, 0}, DialogButtonRowModel.ReadingOrder({X, D}))
        Assert.Equal(New Integer() {2, 1, 0}, DialogButtonRowModel.ReadingOrder({X, A, D}))
        Assert.Equal(New Integer() {1, 2, 0}, DialogButtonRowModel.ReadingOrder({A, C, D}))
        Assert.Equal(New Integer() {0}, DialogButtonRowModel.ReadingOrder({A}))
        Assert.Empty(DialogButtonRowModel.ReadingOrder(New DialogButtonRole() {}))
        Assert.Empty(DialogButtonRowModel.ReadingOrder(Nothing))
    End Sub

    <Fact>
    Public Sub The_order_function_is_stable_inside_a_role()
        Dim roles As DialogButtonRole() = {DialogButtonRole.Command, DialogButtonRole.Affirmative, DialogButtonRole.Command, DialogButtonRole.Dismissing, DialogButtonRole.Neutral, DialogButtonRole.Neutral}
        Dim order As Integer() = DialogButtonRowModel.ReadingOrder(roles)
        Assert.Equal(New Integer() {0, 2, 3, 4, 5, 1}, order)
    End Sub

    <Fact>
    Public Sub A_destructive_answer_takes_the_affirmative_place_and_is_never_the_default()
        Dim roles As DialogButtonRole() = {DialogButtonRole.Destructive, DialogButtonRole.Dismissing}
        Assert.Equal(New Integer() {1, 0}, DialogButtonRowModel.ReadingOrder(roles))
        Assert.Equal(1, DialogButtonRowModel.SafeDefault(roles))
        Assert.Equal(1, DialogButtonRowModel.CancelIndex(roles))
        Assert.Equal(-1, DialogButtonRowModel.SafeDefault({DialogButtonRole.Destructive}))
        Assert.Equal(-1, DialogButtonRowModel.SafeDefault({DialogButtonRole.Command, DialogButtonRole.Destructive}))
        Assert.Equal(0, DialogButtonRowModel.SafeDefault({DialogButtonRole.Affirmative, DialogButtonRole.Destructive}))
    End Sub

    <Fact>
    Public Sub The_default_is_the_affirmative_then_the_dismissing_then_the_apply_answer_and_never_a_command()
        Assert.Equal(2, DialogButtonRowModel.SafeDefault({DialogButtonRole.Neutral, DialogButtonRole.Dismissing, DialogButtonRole.Affirmative}))
        Assert.Equal(1, DialogButtonRowModel.SafeDefault({DialogButtonRole.Neutral, DialogButtonRole.Dismissing}))
        Assert.Equal(0, DialogButtonRowModel.SafeDefault({DialogButtonRole.Neutral, DialogButtonRole.Command}))
        Assert.Equal(-1, DialogButtonRowModel.SafeDefault({DialogButtonRole.Command}))
        Assert.Equal(-1, DialogButtonRowModel.SafeDefault(Nothing))
        Assert.Equal(-1, DialogButtonRowModel.CancelIndex({DialogButtonRole.Affirmative}))
    End Sub

    ' --- the control -------------------------------------------------------------------------

    Private Shared Function NewButton(name As String, caption As String) As Button
        Return New Button With {.Name = name, .Text = caption}
    End Function

    ''' <summary>A window with one row at the bottom holding Save changes / Apply / Cancel, added in the call-site
    ''' order (the acting answer first).</summary>
    Private Shared Function HostWithRow(mirrored As Boolean, clientWidth As Integer, ByRef row As DialogButtonRow) As Form
        Dim host As New Form With {.ShowInTaskbar = False, .StartPosition = FormStartPosition.Manual, .Location = New Point(-20000, -20000)}
        If mirrored Then host.RightToLeft = RightToLeft.Yes
        host.ClientSize = New Size(clientWidth, 200)
        row = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "row"}
        row.Add(NewButton("ok", "Save changes"), DialogButtonRole.Affirmative, "action.confirm")
        row.Add(NewButton("apply", "Apply"), DialogButtonRole.Neutral)
        row.Add(NewButton("cancel", "Cancel"), DialogButtonRole.Dismissing, "action.cancel")
        host.Controls.Add(row)
        row.Bind(host)
        host.Show()
        Return host
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub The_row_reads_cancel_apply_save_from_the_reading_start_mirrored_in_a_right_to_left_window(rightToLeft As Boolean)
        OnSta(Sub()
                  Dim row As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(rightToLeft, 700, row)
                      Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                      Assert.Equal(New String() {"cancel", "apply", "ok"}, reading.Select(Function(b) b.Name).ToArray())
                      Dim xs As Integer() = reading.Select(Function(b) b.PointToScreen(Point.Empty).X).ToArray()
                      For i As Integer = 0 To xs.Length - 2
                          If rightToLeft Then
                              Assert.True(xs(i) > xs(i + 1), "answer " & i.ToString(CultureInfo.InvariantCulture) & " reads before answer " & (i + 1).ToString(CultureInfo.InvariantCulture))
                          Else
                              Assert.True(xs(i) < xs(i + 1), "answer " & i.ToString(CultureInfo.InvariantCulture) & " reads before answer " & (i + 1).ToString(CultureInfo.InvariantCulture))
                          End If
                      Next
                      ' Tab follows the reading order
                      Assert.Equal(New Integer() {0, 1, 2}, reading.Select(Function(b) b.TabIndex).ToArray())
                      ' the answers sit against the reading END: the right edge, or the left one in a mirrored window
                      Dim last As Button = reading(reading.Count - 1)
                      If rightToLeft Then
                          Assert.True(last.Left <= 1, "the final answer is at the far left of a right-to-left window: " & last.Left.ToString(CultureInfo.InvariantCulture))
                      Else
                          Assert.True(row.ClientSize.Width - last.Right <= 1, "the final answer is at the far right")
                      End If
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_row_binds_escape_to_cancel_and_enter_to_the_safe_default()
        OnSta(Sub()
                  Dim row As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(False, 700, row)
                      Assert.Same(host.Controls.Find("cancel", True).Single(), host.CancelButton)
                      Assert.Same(host.Controls.Find("ok", True).Single(), host.AcceptButton)
                  End Using
                  Using host As New Form()
                      Dim only As New DialogButtonRow()
                      only.Add(NewButton("c", "Close"), DialogButtonRole.Dismissing)
                      only.Add(NewButton("x", "Delete"), DialogButtonRole.Destructive)
                      host.Controls.Add(only)
                      only.Bind(host)
                      Assert.Same(only.CancelTarget, host.CancelButton)
                      Assert.Equal("c", DirectCast(host.AcceptButton, Button).Name)
                      host.AcceptButton = Nothing
                      only.Bind(host, bindAccept:=False)
                      Assert.Null(host.AcceptButton)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Widths_follow_the_caption_are_unequal_and_every_button_in_a_line_has_one_height()
        OnSta(Sub()
                  Dim row As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(False, 900, row)
                      Dim buttons As IReadOnlyList(Of Button) = row.ReadingButtons
                      For Each b As Button In buttons
                          Dim need As Integer = TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonGlyphs.ExtraWidth(b) + 24
                          Assert.True(b.Width >= need, b.Text & " is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide, needs " & need.ToString(CultureInfo.InvariantCulture))
                          Assert.True(b.Width <= need + 2, b.Text & " is stretched: " & b.Width.ToString(CultureInfo.InvariantCulture) & " for " & need.ToString(CultureInfo.InvariantCulture))
                          Assert.True(b.Height >= 32)
                      Next
                      Assert.Equal(buttons.Count, buttons.Select(Function(b) b.Width).Distinct().Count())
                      Assert.Single(buttons.Select(Function(b) b.Height).Distinct())
                      Assert.Single(buttons.Select(Function(b) b.Top).Distinct())
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_narrow_window_wraps_whole_buttons_keeps_their_widths_and_one_height_per_line()
        OnSta(Sub()
                  Dim wide As DialogButtonRow = Nothing
                  Dim naturalWidths As Integer()
                  Using host As Form = HostWithRow(False, 900, wide)
                      naturalWidths = wide.ReadingButtons.Select(Function(b) b.Width).ToArray()
                  End Using
                  Dim narrow As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(False, naturalWidths(0) + naturalWidths(1) + 12, narrow)
                      Dim buttons As IReadOnlyList(Of Button) = narrow.ReadingButtons
                      Assert.Equal(naturalWidths, buttons.Select(Function(b) b.Width).ToArray())
                      Assert.Equal(2, buttons.Select(Function(b) b.Top).Distinct().Count())
                      Assert.Equal(buttons(0).Top, buttons(1).Top)
                      Assert.True(buttons(2).Top > buttons(0).Top)
                      Assert.True(narrow.Height >= buttons(2).Bottom, "the row grew to hold the second line")
                      For Each b As Button In buttons
                          Assert.True(b.Right <= narrow.ClientSize.Width + 1 AndAlso b.Left >= -1, b.Text & " stays inside the row")
                      Next
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_repeat_of_the_same_width_does_not_measure_or_place_again_and_a_resize_does_not_measure()
        OnSta(Sub()
                  Dim row As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(False, 700, row)
                      Dim measured As Integer = row.MeasureCount
                      Dim placed As Integer = row.ArrangeCount
                      Assert.True(measured >= 1 AndAlso placed >= 1)
                      For i As Integer = 0 To 4
                          row.PerformLayout()
                      Next
                      Assert.Equal(measured, row.MeasureCount)
                      Assert.Equal(placed, row.ArrangeCount)
                      host.ClientSize = New Size(640, 200)
                      Assert.Equal(measured, row.MeasureCount)
                      Assert.True(row.ArrangeCount > placed, "a new width places the buttons once more")
                      Dim again As Integer = row.ArrangeCount
                      host.ClientSize = New Size(640, 200)
                      row.PerformLayout()
                      Assert.Equal(again, row.ArrangeCount)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_new_caption_or_a_new_font_measures_again_and_widens_the_button()
        OnSta(Sub()
                  Dim row As DialogButtonRow = Nothing
                  Using host As Form = HostWithRow(False, 900, row)
                      Dim cancel As Button = row.CancelTarget
                      Dim before As Integer = cancel.Width
                      Dim measured As Integer = row.MeasureCount
                      cancel.Text = "Abbrechen und alles verwerfen"
                      Assert.True(cancel.Width > before, "a longer caption (a language switch) widens the button")
                      Assert.True(row.MeasureCount > measured)
                      Dim widthAtNine As Integer = cancel.Width
                      row.Font = New Font(row.Font.FontFamily, row.Font.SizeInPoints * 1.5F)
                      Assert.True(cancel.Width > widthAtNine, "a larger font widens the button")
                      Assert.True(cancel.Height >= 32)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_row_in_a_self_sizing_container_reports_its_natural_size()
        OnSta(Sub()
                  Using host As New Form()
                      Dim row As New DialogButtonRow() With {.AutoSize = True}
                      row.Add(NewButton("a", "Save changes"), DialogButtonRole.Affirmative)
                      row.Add(NewButton("b", "Cancel"), DialogButtonRole.Dismissing)
                      host.Controls.Add(row)
                      host.Show()
                      Dim natural As Size = row.GetPreferredSize(Size.Empty)
                      Dim sum As Integer = row.ReadingButtons.Sum(Function(b) b.Width) + 8
                      Assert.Equal(sum, natural.Width)
                      Assert.True(natural.Height >= 32)
                      Dim squeezed As Size = row.GetPreferredSize(New Size(row.ReadingButtons(0).Width + 4, 0))
                      Assert.True(squeezed.Height > natural.Height, "a squeezed row asks for a second line")
                  End Using
              End Sub)
    End Sub

    ' --- the four windows that adopted it ----------------------------------------------------

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

    ''' <summary>The sentences that are wrong with the row of a shown window (empty = sound).</summary>
    Private Shared Function RowProblems(name As String, form As Form) As List(Of String)
        Dim found As New List(Of String)()
        Dim rows As List(Of DialogButtonRow) = RowsOf(form)
        If rows.Count <> 1 Then
            found.Add(name & ": expected exactly one DialogButtonRow, found " & rows.Count.ToString(CultureInfo.InvariantCulture))
            Return found
        End If
        Dim client As Rectangle = form.ClientRectangle
        For Each b As Button In AllButtons(form)
            If Not b.Visible Then Continue For
            ' The resource list panel's own overflow button (a 32 px glyph button of Resource_List_Panel, not a dialog answer)
            ' is outside this slice; the rows of the four windows are what is held here.
            If b.Name = "res_More" Then Continue For
            Dim need As Integer = TextRenderer.MeasureText(b.Text, b.Font).Width + ButtonGlyphs.ExtraWidth(b)
            If b.Width < need Then found.Add(name & ": " & b.Name & " '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " wide and its caption needs " & need.ToString(CultureInfo.InvariantCulture))
            Dim onForm As Rectangle = form.RectangleToClient(b.RectangleToScreen(b.ClientRectangle))
            If onForm.Left < client.Left - 1 OrElse onForm.Right > client.Right + 1 OrElse onForm.Top < client.Top - 1 OrElse onForm.Bottom > client.Bottom + 1 Then
                found.Add(name & ": " & b.Name & " " & onForm.ToString() & " is clipped by the window " & client.Size.ToString())
            End If
        Next
        For Each line In rows(0).ReadingButtons.GroupBy(Function(b) b.Top)
            If line.Select(Function(b) b.Height).Distinct().Count() <> 1 Then
                found.Add(name & ": the buttons of one line differ in height: " & String.Join(",", line.Select(Function(b) b.Height.ToString(CultureInfo.InvariantCulture))))
            End If
            If line.Any(Function(b) b.Height < 32) Then found.Add(name & ": a button is below the 32 target")
        Next
        Return found
    End Function

    Private Shared Function Windows() As Dictionary(Of String, Func(Of Form))
        Dim report As New BatchReport()
        report.Results.Add(New BatchItemResult With {.Path = "C:\a\b.jpg", .State = BatchItemState.Done})
        Return New Dictionary(Of String, Func(Of Form)) From {
            {"Batch_Report_Form", Function() New Batch_Report_Form(report)},
            {"Resource_Manager_Form", Function() New Resource_Manager_Form()},
            {"Hotkeys_Form", Function() New Hotkeys_Form(New Dictionary(Of String, String)())},
            {"Download_Progress_Form", Function() DirectCast(Activator.CreateInstance(GetType(Download_Progress_Form),
                BindingFlags.Instance Or BindingFlags.NonPublic, Nothing, New Object() {"Download", "Downloading the language data for the text recognition"}, Nothing), Form)}}
    End Function

    <Theory>
    <InlineData("de")>
    <InlineData("fr")>
    <InlineData("pt")>
    <InlineData("en")>
    <InlineData("ar")>
    Public Sub The_adopting_windows_hold_every_caption_unclipped_and_row_equal_at_every_scale(code As String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each pair As KeyValuePair(Of String, Func(Of Form)) In Windows()
                          For Each factor As Single In New Single() {1.0F, 1.5F, 1.75F}
                              Using form As Form = ShownAt(pair.Value, factor)
                                  For Each p As String In RowProblems(pair.Key & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%", form)
                                      all.Add(p)
                                  Next
                              End Using
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
    Public Sub The_adopting_windows_read_their_answers_cancel_first_and_bind_escape_and_enter(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(rightToLeft, Localization.IsRightToLeft())
            OnSta(Sub()
                      For Each pair As KeyValuePair(Of String, Func(Of Form)) In Windows()
                          Using form As Form = ShownAt(pair.Value, 1.0F)
                              Dim row As DialogButtonRow = RowsOf(form).Single()
                              Dim reading As IReadOnlyList(Of Button) = row.ReadingButtons
                              Dim xs As Integer() = reading.Select(Function(b) b.PointToScreen(Point.Empty).X).ToArray()
                              For i As Integer = 0 To xs.Length - 2
                                  If rightToLeft Then
                                      Assert.True(xs(i) > xs(i + 1), pair.Key & ": button " & i.ToString(CultureInfo.InvariantCulture) & " must read before " & (i + 1).ToString(CultureInfo.InvariantCulture))
                                  Else
                                      Assert.True(xs(i) < xs(i + 1), pair.Key & ": button " & i.ToString(CultureInfo.InvariantCulture) & " must read before " & (i + 1).ToString(CultureInfo.InvariantCulture))
                                  End If
                              Next
                              Assert.NotNull(form.CancelButton)
                              Assert.True(reading.Contains(DirectCast(form.CancelButton, Button)), pair.Key & ": Escape reaches a button of the row")
                              Select Case pair.Key
                                  Case "Hotkeys_Form"
                                      Assert.Equal(3, reading.Count)
                                      Assert.Same(reading(reading.Count - 1), form.AcceptButton)
                                      Assert.Same(reading(reading.Count - 2), form.CancelButton)
                                  Case "Resource_Manager_Form"
                                      Assert.Equal("res_Close", reading(0).Name)
                                      Assert.Equal("res_Open", reading(1).Name)
                                      Assert.Null(form.AcceptButton)
                                  Case "Batch_Report_Form"
                                      Assert.Same(form.AcceptButton, form.CancelButton)
                                  Case "Download_Progress_Form"
                                      Assert.Same(form.AcceptButton, form.CancelButton)
                              End Select
                          End Using
                      Next
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub The_adopting_windows_keep_the_names_the_other_tests_and_handlers_find_them_by()
        OnSta(Sub()
                  Dim report As New BatchReport()
                  Using manager As New Resource_Manager_Form()
                      Assert.NotNull(manager.Controls.Find("res_Open", True).Single())
                      Assert.NotNull(manager.Controls.Find("res_Close", True).Single())
                  End Using
                  Using batch As New Batch_Report_Form(report)
                      Assert.Equal(3, RowsOf(batch).Single().ReadingButtons.Count)
                  End Using
                  Using hotkeys As New Hotkeys_Form(New Dictionary(Of String, String)())
                      Assert.Equal(5, AllButtons(hotkeys).Count)
                  End Using
              End Sub)
    End Sub
End Class
#End If
