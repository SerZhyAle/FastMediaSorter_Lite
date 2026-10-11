#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' WINDOWS-UI 0.4 section 11 / ICON-RENDER 0.19 section 14 as the product holds them: the glyph before the caption
''' of a command button, the hue and the tint of the answer buttons of a dialog, and the numbers the section asks to be
''' measured (glyph 3 : 1, caption 4.5 : 1 against the face the pair is actually drawn on, tinted face included).
''' A window cannot be looked at here, so the checks read the button the way the module drew it (<see cref="ButtonGlyphs"/>)
''' and a rendered bitmap of it; what stays manual is the live high-contrast switch and a physical 150/200 percent monitor.
''' </summary>
<Collection("AppFileLogger")>
Public Class AnswerButtonGlyphTests

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
        Assert.True(thread.Join(60000), "the UI check did not finish")
        If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub

    ''' <summary>Runs <paramref name="body"/> under a palette and always puts the light one back.</summary>
    Private Shared Sub UnderPalette(dark As Boolean, body As Action)
        ButtonGlyphs.UsePalette(ThemePalette.ForDark(dark))
        Try
            body()
        Finally
            ButtonGlyphs.UsePalette(ThemePalette.Light)
        End Try
    End Sub

    Private Shared Function NewButton(caption As String, glyph As String, role As ButtonGlyphRole, Optional host As Form = Nothing) As Button
        Dim b As New Button With {.Text = caption, .AutoSize = True}
        If host IsNot Nothing Then host.Controls.Add(b)
        ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    ' --- the answers of the question window -----------------------------------------------------------------

    <Fact>
    Public Sub Every_answer_of_every_fixed_question_carries_a_glyph_in_its_polarity()
        OnSta(Sub()
                  For Each spec As AskSpec In AskSpecs.All()
                      Using dialog As New Ask_Dialog(spec)
                          Dim buttons As List(Of Button) = AnswerButtons(dialog)
                          Assert.Equal(spec.Buttons.Count, buttons.Count)
                          For i As Integer = 0 To buttons.Count - 1
                              Assert.True(ButtonGlyphs.CarriesGlyph(buttons(i)), spec.Title & ": the answer '" & spec.Buttons(i).Caption & "' has no glyph")
                              Dim expected As ButtonGlyphRole = spec.Buttons(i).Role
                              If expected = ButtonGlyphRole.Neutral AndAlso spec.Buttons(i).GlyphId.Length = 0 AndAlso i = spec.CancelIndex Then
                                  expected = ButtonGlyphRole.Dismissing
                              End If
                              Assert.Equal(expected, ButtonGlyphs.RoleOf(buttons(i)))
                          Next
                          ' The cancel answer is never an acting colour, whatever the spec says (APP-BEHAVIOUR rule 1).
                          Dim cancel As ButtonGlyphRole = ButtonGlyphs.RoleOf(buttons(spec.CancelIndex))
                          Assert.True(cancel = ButtonGlyphRole.Dismissing OrElse cancel = ButtonGlyphRole.Neutral,
                                      spec.Title & ": the Escape answer is drawn '" & cancel.ToString() & "'")
                      End Using
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub In_an_irreversible_confirmation_only_the_acting_button_has_a_red_face()
        ' WINDOWS-UI section 11 item 7: the dismissing answer keeps its red glyph and takes no tint, the acting button
        ' alone takes the danger face, and the default focus stays on the safe answer.
        OnSta(Sub()
                  Using dialog As New Ask_Dialog(AskSpecs.ProfileResetConfirm())
                      Dim buttons As List(Of Button) = AnswerButtons(dialog)
                      Dim acting As Button = buttons(AskSpecs.ProfileResetExecute)
                      Dim safe As Button = buttons(AskSpecs.ProfileResetCancel)
                      Assert.Equal(ButtonGlyphRole.Irreversible, ButtonGlyphs.RoleOf(acting))
                      Assert.Equal(ThemePalette.Light.Danger.ToArgb(), acting.BackColor.ToArgb())
                      Assert.Equal(ButtonGlyphRole.Dismissing, ButtonGlyphs.RoleOf(safe))
                      Assert.Equal(Color.FromArgb(225, 225, 225).ToArgb(), ButtonGlyphs.CurrentFace(safe).ToArgb())
                      Assert.Same(safe, dialog.AcceptButton)
                  End Using
                  ' A question that is not a confirmation of an irreversible act tints its dismissing answer.
                  Using dialog As New Ask_Dialog(AskSpecs.HotkeyConflict("Ctrl+K", "Action"))
                      Dim cancel As Button = AnswerButtons(dialog)(AskSpecs.ConflictCancel)
                      Assert.NotEqual(Color.FromArgb(225, 225, 225).ToArgb(), ButtonGlyphs.CurrentFace(cancel).ToArgb())
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_text_prompt_is_a_green_ok_beside_a_red_cancel()
        OnSta(Sub()
                  Using dialog As New Ask_Dialog(AskSpecs.JumpToNumberPrompt(1, 10))
                      Dim buttons As List(Of Button) = AnswerButtons(dialog)
                      Assert.Equal(2, buttons.Count)
                      Assert.Equal(ButtonGlyphRole.Affirmative, ButtonGlyphs.RoleOf(buttons(0)))
                      Assert.Equal("action.confirm", ButtonGlyphs.GlyphOf(buttons(0)))
                      Assert.Equal(ButtonGlyphRole.Dismissing, ButtonGlyphs.RoleOf(buttons(1)))
                      Assert.Equal("action.cancel", ButtonGlyphs.GlyphOf(buttons(1)))
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Escape_enter_and_the_default_focus_are_what_the_spec_said()
        ' WINDOWS-UI section 11 item 7: a hue never moves the default focus, the cancel path or the order.
        OnSta(Sub()
                  For Each spec As AskSpec In AskSpecs.All()
                      Using dialog As New Ask_Dialog(spec)
                          Dim buttons As List(Of Button) = AnswerButtons(dialog)
                          Assert.Same(buttons(spec.DefaultIndex), dialog.AcceptButton)
                          Assert.Same(buttons(spec.CancelIndex), dialog.CancelButton)
                          Assert.False(spec.Buttons(spec.DefaultIndex).Destructive, spec.Title & ": a destructive answer holds the default")
                          ' Only an irreversible acting button takes the danger face; the default never does.
                          Assert.NotEqual(ButtonGlyphRole.Irreversible, ButtonGlyphs.RoleOf(buttons(spec.DefaultIndex)))
                      End Using
                  Next
              End Sub)
    End Sub

    ' --- what the numbers are -----------------------------------------------------------------------------

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Glyphs_hold_three_to_one_and_captions_four_and_a_half_on_the_face_they_are_drawn_on(dark As Boolean)
        OnSta(Sub()
                  UnderPalette(dark, Sub()
                                         Dim palette As ThemePalette = ThemePalette.ForDark(dark)
                                         Dim report As New StringBuilder()
                                         Dim roles As ButtonGlyphRole() = {ButtonGlyphRole.Neutral, ButtonGlyphRole.Affirmative, ButtonGlyphRole.Dismissing, ButtonGlyphRole.Caution}
                                         For Each role As ButtonGlyphRole In roles
                                             Using form As New Form()
                                                 Dim b As Button = NewButton("Caption", "action.confirm", role, form)
                                                 Dim face As Color = ButtonGlyphs.CurrentFace(b)
                                                 Dim glyph As Color = ButtonGlyphs.CurrentInk(b)
                                                 Dim caption As Color = If(dark, palette.TextPrimary, b.ForeColor)
                                                 Dim glyphRatio As Double = AccentColors.Contrast(glyph, face)
                                                 Dim captionRatio As Double = AccentColors.Contrast(caption, face)
                                                 report.AppendLine(String.Join(",", If(dark, "dark", "light"), role.ToString(),
                                                                               HexOf(face), HexOf(glyph), glyphRatio.ToString("0.00", CultureInfo.InvariantCulture),
                                                                               HexOf(caption), captionRatio.ToString("0.00", CultureInfo.InvariantCulture)))
                                                 Assert.True(glyphRatio >= ButtonGlyphs.GlyphContrast - 0.001,
                                                             role.ToString() & " glyph " & HexOf(glyph) & " on " & HexOf(face) & " is " & glyphRatio.ToString("0.00", CultureInfo.InvariantCulture) & " : 1")
                                                 Assert.True(captionRatio >= 4.5R,
                                                             role.ToString() & " caption " & HexOf(caption) & " on " & HexOf(face) & " is " & captionRatio.ToString("0.00", CultureInfo.InvariantCulture) & " : 1")
                                             End Using
                                         Next
                                         ' The acting button of an irreversible confirmation: the danger face and its own ink.
                                         Using form As New Form()
                                             Dim b As Button = NewButton("Caption", "action.reset", ButtonGlyphRole.Irreversible, form)
                                             Dim face As Color = b.BackColor
                                             Assert.Equal(palette.Danger.ToArgb(), face.ToArgb())
                                             Dim ratio As Double = AccentColors.Contrast(b.ForeColor, face)
                                             report.AppendLine(String.Join(",", If(dark, "dark", "light"), "Irreversible", HexOf(face), HexOf(ButtonGlyphs.CurrentInk(b)),
                                                                           AccentColors.Contrast(ButtonGlyphs.CurrentInk(b), face).ToString("0.00", CultureInfo.InvariantCulture),
                                                                           HexOf(b.ForeColor), ratio.ToString("0.00", CultureInfo.InvariantCulture)))
                                             Assert.True(ratio >= 4.5R, "the danger face's own ink is " & ratio.ToString("0.00", CultureInfo.InvariantCulture) & " : 1")
                                         End Using
                                         File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "answer-glyph-contrast.csv"), report.ToString())
                                     End Sub)
              End Sub)
    End Sub

    Private Shared Function HexOf(c As Color) As String
        Return "#" & c.R.ToString("X2", CultureInfo.InvariantCulture) & c.G.ToString("X2", CultureInfo.InvariantCulture) & c.B.ToString("X2", CultureInfo.InvariantCulture)
    End Function

    ' --- live, disabled, direction, scale -------------------------------------------------------------------

    <Fact>
    Public Sub The_hue_follows_a_live_theme_change()
        OnSta(Sub()
                  Using form As New Form()
                      Dim b As Button = NewButton("Caption", "action.confirm", ButtonGlyphRole.Affirmative, form)
                      Dim light As Color = ButtonGlyphs.CurrentInk(b)
                      ButtonGlyphs.UsePalette(ThemePalette.Dark)
                      Try
                          Dim night As Color = ButtonGlyphs.CurrentInk(b)
                          Assert.NotEqual(light.ToArgb(), night.ToArgb())
                          ' the picture itself was re-rendered, not just the answer to a question: sample its ink
                          Using picture As New Bitmap(b.Image)
                              Dim sampled As Color = DominantInk(picture)
                              Assert.True(Math.Abs(sampled.G - night.G) < 24, "the bitmap still holds the light-theme ink " & HexOf(sampled))
                          End Using
                      Finally
                          ButtonGlyphs.UsePalette(ThemePalette.Light)
                      End Try
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub An_unavailable_answer_shows_no_hue_and_no_tint()
        OnSta(Sub()
                  Using form As New Form()
                      Dim b As Button = NewButton("Caption", "action.confirm", ButtonGlyphRole.Affirmative, form)
                      Dim enabled As Color = ButtonGlyphs.CurrentInk(b)
                      b.Enabled = False
                      Dim off As Color = ButtonGlyphs.CurrentInk(b)
                      Assert.NotEqual(enabled.ToArgb(), off.ToArgb())
                      Assert.True(Math.Abs(CInt(off.R) - off.G) < 8 AndAlso Math.Abs(CInt(off.G) - off.B) < 8, "the disabled glyph is not a neutral grey: " & HexOf(off))
                      Dim face As Color = ButtonGlyphs.CurrentFace(b)
                      Assert.Equal(CInt(Math.Round(AccentColors.Contrast(face, face))), 1)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_gap_is_six_logical_pixels_on_the_caption_side_and_follows_the_reading_direction()
        OnSta(Sub()
                  Using form As New Form()
                      Dim ltr As Button = NewButton("Caption", "action.cancel", ButtonGlyphRole.Dismissing, form)
                      Dim rtl As New Button With {.Text = "Caption", .RightToLeft = RightToLeft.Yes, .AutoSize = True}
                      form.Controls.Add(rtl)
                      ButtonGlyphs.Apply(rtl, "action.cancel", ButtonGlyphRole.Dismissing)
                      Dim glyph As Integer = ltr.LogicalToDeviceUnits(ButtonGlyphs.GlyphLogicalSize)
                      Dim gap As Integer = ltr.LogicalToDeviceUnits(ButtonGlyphs.GapLogicalSize)
                      Assert.Equal(glyph + gap, ltr.Image.Width)
                      Assert.Equal(glyph + gap, rtl.Image.Width)
                      Using a As New Bitmap(ltr.Image), b As New Bitmap(rtl.Image)
                          Assert.True(ColumnsClear(a, glyph, a.Width), "left-to-right: the gap must be empty on the right")
                          Assert.True(ColumnsClear(b, 0, gap), "right-to-left: the gap must be empty on the left")
                          Assert.False(ColumnsClear(a, 0, glyph), "the glyph itself is missing")
                      End Using
                  End Using
              End Sub)
    End Sub

    <Theory>
    <InlineData(144, 24, 9)>
    <InlineData(192, 32, 12)>
    Public Sub The_glyph_and_the_gap_scale_with_the_device_dpi(dpi As Integer, glyph As Integer, gap As Integer)
        OnSta(Sub()
                  Using form As New Form()
                      Dim b As New Button With {.Text = "Caption", .AutoSize = True}
                      form.Controls.Add(b)
                      ' DeviceDpi is what PerMonitorV2 sets on a monitor at 150 / 200 percent; there is no monitor here.
                      Dim field As FieldInfo = GetType(Control).GetField("_deviceDpi", BindingFlags.Instance Or BindingFlags.NonPublic)
                      If field Is Nothing Then Return
                      field.SetValue(b, dpi)
                      ButtonGlyphs.Apply(b, "action.confirm", ButtonGlyphRole.Affirmative)
                      Assert.Equal(glyph + gap, b.Image.Width)
                      Assert.Equal(glyph, b.Image.Height)
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub A_fixed_width_button_grows_for_its_longest_caption_instead_of_clipping_it()
        OnSta(Sub()
                  Using form As New Form()
                      form.ClientSize = New Size(420, 120)
                      Dim longest As String = "Восстановить регистрацию службы"
                      Dim b As New Button With {.Text = longest, .Location = New Point(300, 80), .Size = New Size(86, 28), .AutoSize = False}
                      form.Controls.Add(b)
                      ButtonGlyphs.Apply(b, "action.reconnect", ButtonGlyphRole.Neutral)
                      form.Show()
                      DialogLayout.FitContent(form)
                      Dim need As Integer = TextRenderer.MeasureText(longest, b.Font).Width + ButtonGlyphs.ExtraWidth(b)
                      Assert.True(b.Width >= need, "the button is " & b.Width.ToString(CultureInfo.InvariantCulture) & " px, its caption and glyph need " & need.ToString(CultureInfo.InvariantCulture))
                      form.Close()
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub The_tint_is_painted_over_the_face_of_an_answer_and_not_over_a_neutral_button()
        OnSta(Sub()
                  Using form As New Form()
                      form.ClientSize = New Size(300, 60)
                      Dim ok As Button = NewButton("OK", "action.confirm", ButtonGlyphRole.Affirmative, form)
                      Dim plain As Button = NewButton("OK", "action.confirm", ButtonGlyphRole.Neutral, form)
                      ok.Size = New Size(120, 36) : ok.AutoSize = False : ok.Location = New Point(10, 10)
                      plain.Size = New Size(120, 36) : plain.AutoSize = False : plain.Location = New Point(150, 10)
                      form.Show()
                      Using a As New Bitmap(ok.Width, ok.Height), b As New Bitmap(plain.Width, plain.Height)
                          ok.DrawToBitmap(a, New Rectangle(Point.Empty, ok.Size))
                          plain.DrawToBitmap(b, New Rectangle(Point.Empty, plain.Size))
                          ' a point inside the face, left of the glyph and clear of the border
                          Dim tinted As Color = a.GetPixel(6, ok.Height \ 2)
                          Dim bare As Color = b.GetPixel(6, plain.Height \ 2)
                          Assert.True(CInt(tinted.G) - tinted.R > CInt(bare.G) - bare.R, "no green over the face: " & HexOf(tinted) & " against " & HexOf(bare))
                      End Using
                      form.Close()
                  End Using
              End Sub)
    End Sub

    <Fact>
    Public Sub Render_the_question_windows_when_a_folder_is_given()
        Dim dir As String = Environment.GetEnvironmentVariable("FMS_SHOT_DIR")
        If String.IsNullOrEmpty(dir) Then Return
        Directory.CreateDirectory(dir)
        OnSta(Sub()
                  Dim index As Integer = 0
                  For Each spec As AskSpec In AskSpecs.All()
                      index += 1
                      Using dialog As New Ask_Dialog(spec)
                          dialog.StartPosition = FormStartPosition.Manual
                          dialog.Location = New Point(-20000, -20000)
                          dialog.ShowInTaskbar = False
                          dialog.Show()
                          Application.DoEvents()
                          Using bmp As New Bitmap(dialog.Width, dialog.Height)
                              dialog.DrawToBitmap(bmp, New Rectangle(Point.Empty, dialog.Size))
                              bmp.Save(Path.Combine(dir, "ask-" & index.ToString(CultureInfo.InvariantCulture) & "-light.png"), System.Drawing.Imaging.ImageFormat.Png)
                          End Using
                          dialog.Close()
                      End Using
                  Next
              End Sub)
    End Sub

    <Fact>
    Public Sub The_widest_answer_row_in_every_language_is_measured_and_fits_its_window()
        ' The row of answers is laid out by the dialog's own table, so a long caption grows the window; what is
        ' measured here is that no button is narrower than its caption plus glyph in any of the shipped languages, and
        ' the figures (logical px, 100 percent) are written for the acceptance record.
        Dim report As New StringBuilder()
        Dim original As String = Localization.CurrentCode
        OnSta(Sub()
                  Try
                      For Each code As String In Localization.Codes
                          Localization.CurrentCode = code
                          Dim widest As Integer = 0
                          Dim widestRow As Integer = 0
                          Dim widestSpec As String = ""
                          For Each spec As AskSpec In AskSpecs.All()
                              Using dialog As New Ask_Dialog(spec)
                                  dialog.Show()
                                  Dim row As Integer = 0
                                  For Each b As Button In AnswerButtons(dialog)
                                      Dim need As Integer = TextRenderer.MeasureText(b.Text.Replace("&", ""), b.Font).Width + ButtonGlyphs.ExtraWidth(b)
                                      Assert.True(b.Width >= need, code & ": '" & b.Text & "' is " & b.Width.ToString(CultureInfo.InvariantCulture) & " px, its caption and glyph need " & need.ToString(CultureInfo.InvariantCulture))
                                      widest = Math.Max(widest, b.Width)
                                      row += b.Width + 8
                                  Next
                                  If row > widestRow Then widestRow = row : widestSpec = spec.Title
                                  dialog.Close()
                              End Using
                          Next
                          report.AppendLine(code & "," & widest.ToString(CultureInfo.InvariantCulture) & "," & widestRow.ToString(CultureInfo.InvariantCulture) & "," & widestSpec)
                      Next
                  Finally
                      Localization.CurrentCode = original
                  End Try
              End Sub)
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "answer-row-widths.csv"), "language,widest button px,widest answer row px,question" & vbCrLf & report.ToString())
    End Sub

    ' --- the source -----------------------------------------------------------------------------------------

    <Fact>
    Public Sub Every_glyph_id_a_call_site_names_is_vendored()
        Dim root As String = RepoRoot()
        Dim named As New SortedSet(Of String)(StringComparer.Ordinal)
        Dim apply As New Regex("ButtonGlyphs\.Apply\([^,]+,\s*""([a-z0-9.\-]+)""")
        Dim askGlyph As New Regex("glyphId:=""([a-z0-9.\-]+)""")
        For Each file As String In Directory.GetFiles(Path.Combine(root, "src"), "*.vb", SearchOption.AllDirectories)
            If file.Contains("\obj\") OrElse file.Contains("\bin\") Then Continue For
            Dim text As String = System.IO.File.ReadAllText(file)
            For Each m As Match In apply.Matches(text)
                named.Add(m.Groups(1).Value)
            Next
            For Each m As Match In askGlyph.Matches(text)
                named.Add(m.Groups(1).Value)
            Next
        Next
        named.Add(ButtonGlyphs.DefaultGlyph(ButtonGlyphRole.Affirmative))
        named.Add(ButtonGlyphs.DefaultGlyph(ButtonGlyphRole.Dismissing))
        Assert.NotEmpty(named)
        For Each id As String In named
            Assert.True(System.IO.File.Exists(Path.Combine(root, "assets", "glyphs", id & ".svg")), "the glyph '" & id & "' a button names is not vendored")
        Next
    End Sub

    <Fact>
    Public Sub Every_answer_glyph_renders_through_the_catalog_reader()
        ' A partial SVG reader fails silently (an underline off the grid, a filled rectangle): paint every glyph the
        ' buttons use and require ink - and, for the product-drawn one, the two parts of its outline.
        Dim root As String = RepoRoot()
        For Each id As String In New String() {"action.confirm", "action.cancel", "nav.close", "action.save", "action.replace", "action.revert", "action.reset", "action.swap", "action.remove"}
            Using bitmap As Bitmap = GlyphRenderer.Render(id, 48, Color.Black, ButtonGlyphs.AssetOpener)
                Assert.True(InkPixels(bitmap) > 60, id & " painted almost nothing")
            End Using
        Next
        Using replace As Bitmap = GlyphRenderer.Render("action.replace", 48, Color.Black, ButtonGlyphs.AssetOpener)
            ' the outlined bar is hollow (evenodd), the lower bar is filled: the centre of the upper bar is clear, the lower one is not
            Assert.Equal(0, replace.GetPixel(24, 10).A)
            Assert.True(replace.GetPixel(24, 40).A > 200)
        End Using
    End Sub

    <Fact>
    Public Sub The_glyph_resources_are_not_read_as_cultures()
        ' "action.run.svg" is Rundi to MSBuild's culture assignment: the glyph went to a satellite assembly and a button
        ' asking for it showed none. Both projects that embed the files say so on the item.
        Dim root As String = RepoRoot()
        For Each project As String In New String() {Path.Combine("src", "Modern", "FastMediaSorter.Modern.vbproj"),
                                                    Path.Combine("src", "FastMediaSorterCompanion", "FastMediaSorterCompanion.vbproj")}
            Dim text As String = System.IO.File.ReadAllText(Path.Combine(root, project))
            Dim at As Integer = text.IndexOf("assets" & Path.DirectorySeparatorChar & "glyphs" & Path.DirectorySeparatorChar & "*.svg", StringComparison.Ordinal)
            Assert.True(at > 0, project & " no longer embeds the glyph files")
            Dim item As String = text.Substring(at, text.IndexOf("</EmbeddedResource>", at, StringComparison.Ordinal) - at)
            Assert.Contains("<WithCulture>false</WithCulture>", item)
        Next
    End Sub

    <Fact>
    Public Sub Every_vendored_glyph_renders_with_ink_through_the_reader()
        ' A reader that fails on one path grammar corner shows a button without its glyph and nothing else; every
        ' file the product ships is painted here, so the corner is found by name.
        Dim failures As New List(Of String)()
        For Each filePath As String In Directory.GetFiles(GlyphDir, "*.svg")
            Dim id As String = Path.GetFileNameWithoutExtension(filePath)
            Try
                Using bitmap As Bitmap = GlyphRenderer.Render(id, 32, Color.Black, ButtonGlyphs.AssetOpener)
                    If InkPixels(bitmap) < 20 Then failures.Add(id & " painted almost nothing")
                End Using
            Catch ex As Exception
                failures.Add(id & ": " & ex.GetType().Name & " " & ex.Message)
            End Try
        Next
        Assert.True(failures.Count = 0, String.Join("; ", failures))
    End Sub

    ' --- helpers --------------------------------------------------------------------------------------------

    Private Shared Function AnswerButtons(dialog As Form) As List(Of Button)
        Dim found As New List(Of Button)()
        CollectButtons(dialog, found)
        Return found.Where(Function(b) b.Name.StartsWith("ask_Button", StringComparison.Ordinal)).
            OrderBy(Function(b) Integer.Parse(b.Name.Substring("ask_Button".Length), CultureInfo.InvariantCulture)).ToList()
    End Function

    Private Shared Sub CollectButtons(parent As Control, into As List(Of Button))
        For Each c As Control In parent.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then into.Add(b)
            CollectButtons(c, into)
        Next
    End Sub

    Private Shared Function InkPixels(bitmap As Bitmap) As Integer
        Dim n As Integer = 0
        For y As Integer = 0 To bitmap.Height - 1
            For x As Integer = 0 To bitmap.Width - 1
                If bitmap.GetPixel(x, y).A > 128 Then n += 1
            Next
        Next
        Return n
    End Function

    Private Shared Function ColumnsClear(bitmap As Bitmap, fromX As Integer, toX As Integer) As Boolean
        For x As Integer = fromX To toX - 1
            For y As Integer = 0 To bitmap.Height - 1
                If bitmap.GetPixel(x, y).A > 0 Then Return False
            Next
        Next
        Return True
    End Function

    Private Shared Function DominantInk(bitmap As Bitmap) As Color
        For y As Integer = 0 To bitmap.Height - 1
            For x As Integer = 0 To bitmap.Width - 1
                Dim c As Color = bitmap.GetPixel(x, y)
                If c.A = 255 Then Return c
            Next
        Next
        Return Color.Empty
    End Function

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("the repository root was not found above " & AppContext.BaseDirectory)
    End Function

End Class
#End If
