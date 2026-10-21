Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml.Linq
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 243 T-12b: the Share Manager's Yes/No, OK/Cancel and Yes/No/Cancel QUESTIONS are asked through the
''' product-owned question window (<c>Ask_Dialog</c>, linked from the viewer) instead of a system message box
''' (BUTTONS 2.0 section 1). Held here:
'''   * by reading the sources, that no code the Share Manager compiles - its own and everything linked into it - passes a
'''     question set to the stock <c>MessageBox.Show</c>, so a new one cannot creep back (and the scanner finds a planted one);
'''   * the translation from a message box to a question (<c>ViewerBoxMapping</c>, the viewer's own file, held by the viewer's
'''     tests) as THIS program compiles it: the answers, the default, the cancel answer, a caption in all 13 languages;
'''   * the real question window of each set and default, at 100 % and 175 % (EMULATED, the way the batch C tests of the
'''     answer row do it), in a left-to-right, a Cyrillic and a right-to-left language: Cancel / No reads first, the
'''     affirmative answer last, Enter on the call site's default, Escape on the cancel answer, nothing clipped or overlapping;
'''   * <c>ManagerBox.Show</c> end to end: each answer comes back as the DialogResult the call site tests for, Escape and
'''     the close box answer the cancel answer, an owner pinned topmost keeps the question above it, a question with no
'''     owner is centred, topmost and in the taskbar, a caller on another thread gets the window made on its owner's thread.
''' The end-to-end checks drive the window through its own buttons (<c>PerformClick</c> from a timer inside the modal loop):
''' nothing here sends a mouse click to the desktop.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class ManagerBoxTests

    Private Shared ReadOnly QuestionSets As MessageBoxButtons() = {MessageBoxButtons.OKCancel, MessageBoxButtons.YesNo, MessageBoxButtons.YesNoCancel}

    Private Shared ReadOnly Defaults As MessageBoxDefaultButton() = {MessageBoxDefaultButton.Button1, MessageBoxDefaultButton.Button2, MessageBoxDefaultButton.Button3}

    Private Const LongBody As String = "This question has a deliberately long text, so that it wraps over several lines at every scale and in " &
        "every language, and the answer row under it has to stay clear of it and inside the window whatever the width of the captions is."

    ' --- the Windows reference, written down independently of the mapping ------------------------------------

    ''' <summary>The answers of a set in the order the SYSTEM box lays them out (what Button1/2/3 name).</summary>
    Private Shared Function WindowsOrder(buttons As MessageBoxButtons) As DialogResult()
        Select Case buttons
            Case MessageBoxButtons.OKCancel : Return New DialogResult() {DialogResult.OK, DialogResult.Cancel}
            Case MessageBoxButtons.YesNo : Return New DialogResult() {DialogResult.Yes, DialogResult.No}
            Case MessageBoxButtons.YesNoCancel : Return New DialogResult() {DialogResult.Yes, DialogResult.No, DialogResult.Cancel}
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(buttons))
        End Select
    End Function

    ''' <summary>The slot the system box makes the default: the one named, or the first when the set has no such slot.</summary>
    Private Shared Function WindowsDefaultIndex(buttons As MessageBoxButtons, defaultButton As MessageBoxDefaultButton) As Integer
        Dim slot As Integer = CInt(defaultButton) \ 256
        If slot < 0 OrElse slot >= WindowsOrder(buttons).Length Then slot = 0
        Return slot
    End Function

    ''' <summary>What Escape and the close box answer: the last answer of the set (Cancel, or No of Yes/No).</summary>
    Private Shared Function EscapeAnswer(buttons As MessageBoxButtons) As DialogResult
        Dim order As DialogResult() = WindowsOrder(buttons)
        Return order(order.Length - 1)
    End Function

    ' --- the source scan ------------------------------------------------------------------------------------

    ''' <summary>The calls of the stock <c>MessageBox.Show</c> in a source whose text names one of the three question sets,
    ''' each as its text. Whole-line comments are ignored; the parentheses of the call are matched, so a call over several
    ''' lines is read whole.</summary>
    Friend Shared Function QuestionCalls(source As String) As List(Of String)
        Dim found As New List(Of String)()
        Dim code As String = ExchangeTestSupport.CodeOnly(source)
        Dim scanFrom As Integer = 0
        Const needle As String = "MessageBox.Show("
        While True
            Dim at As Integer = code.IndexOf(needle, scanFrom, StringComparison.Ordinal)
            If at < 0 Then Exit While
            Dim depth As Integer = 0
            Dim inString As Boolean = False
            Dim close As Integer = -1
            For i As Integer = at + needle.Length - 1 To code.Length - 1
                Dim ch As Char = code(i)
                If ch = """"c Then
                    inString = Not inString
                ElseIf Not inString Then
                    If ch = "("c Then
                        depth += 1
                    ElseIf ch = ")"c Then
                        depth -= 1
                        If depth = 0 Then
                            close = i
                            Exit For
                        End If
                    End If
                End If
            Next
            If close < 0 Then close = code.Length - 1
            Dim call_ As String = code.Substring(at, close - at + 1)
            If call_.Contains("MessageBoxButtons.OKCancel") OrElse call_.Contains("MessageBoxButtons.YesNo") Then found.Add(call_)
            scanFrom = close + 1
        End While
        Return found
    End Function

    ''' <summary>Every source the Share Manager compiles: its own tree and everything its project links in (a wildcard
    ''' expanded), so a question in a library both programs share is found too.</summary>
    Private Shared Function CompiledSources() As List(Of String)
        Dim files As New HashSet(Of String)(ExchangeTestSupport.CompanionSources(), StringComparer.OrdinalIgnoreCase)
        Dim projectDir As String = ExchangeTestSupport.CompanionDir()
        Dim project As XDocument = XDocument.Load(Path.Combine(projectDir, "FastMediaSorterCompanion.vbproj"))
        For Each item As XElement In project.Descendants("Compile")
            Dim include As String = CStr(item.Attribute("Include"))
            If String.IsNullOrEmpty(include) OrElse Not include.StartsWith("..", StringComparison.Ordinal) Then Continue For
            Dim full As String = Path.GetFullPath(Path.Combine(projectDir, include))
            If include.Contains("*") Then
                Dim folder As String = Path.GetDirectoryName(full)
                If Directory.Exists(folder) Then
                    For Each match As String In Directory.GetFiles(folder, Path.GetFileName(full))
                        files.Add(match)
                    Next
                End If
            ElseIf File.Exists(full) Then
                files.Add(full)
            End If
        Next
        Return files.ToList()
    End Function

    <Fact>
    Public Sub No_source_of_the_Share_Manager_asks_a_question_through_the_system_message_box()
        Dim sources As List(Of String) = CompiledSources()
        Assert.True(sources.Count > 200, "the scan read only " & sources.Count.ToString(CultureInfo.InvariantCulture) & " sources - the project's links were not followed")
        Dim offenders As New List(Of String)()
        For Each source As String In sources
            ' the box itself passes the caller's set on to the stock call for the sets that are not questions
            If Path.GetFileName(source).Equals("ManagerBox.vb", StringComparison.OrdinalIgnoreCase) Then Continue For
            For Each found As String In QuestionCalls(File.ReadAllText(source))
                offenders.Add(Path.GetFileName(source) & ": " & found.Replace(vbCr, " ").Replace(vbLf, " ").Substring(0, Math.Min(110, found.Length)))
            Next
        Next
        Assert.True(offenders.Count = 0,
                    "A question (OKCancel / YesNo / YesNoCancel) goes to the system box - ask it through ManagerBox.Show (BUTTONS 2.0 section 1):" &
                    vbLf & String.Join(vbLf, offenders))
    End Sub

    <Fact>
    Public Sub The_scanner_finds_a_planted_question_and_leaves_a_notice_alone()
        Assert.Single(QuestionCalls("x = MessageBox.Show(Me, ""q"", ""c"", MessageBoxButtons.YesNo)"))
        Assert.Single(QuestionCalls("If MessageBox.Show(Me, Localization.T(""a (b)""), Me.Text," & vbCrLf & "    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) <> DialogResult.OK Then Return"))
        Assert.Single(QuestionCalls("MessageBox.Show(text, caption, MessageBoxButtons.YesNoCancel)"))
        Assert.Empty(QuestionCalls("MessageBox.Show(Me, ""q"", ""c"", MessageBoxButtons.OK, MessageBoxIcon.Information)"))
        Assert.Empty(QuestionCalls("MessageBox.Show(Me, ""q"", ""c"", MessageBoxButtons.RetryCancel)"))
        Assert.Empty(QuestionCalls("' MessageBox.Show(Me, ""q"", ""c"", MessageBoxButtons.YesNo)"))
        Assert.Empty(QuestionCalls("ManagerBox.Show(Me, ""q"", ""c"", MessageBoxButtons.YesNo)"))
        ' two calls in one source: only the question is reported
        Dim both As String = "MessageBox.Show(a, b, c, MessageBoxButtons.OK)" & vbLf & "MessageBox.Show(a, b, c, MessageBoxButtons.YesNo)"
        Assert.Single(QuestionCalls(both))
    End Sub

    <Fact>
    Public Sub The_box_routes_only_the_question_sets_and_themes_the_window_before_it_is_shown()
        Dim src As String = ExchangeTestSupport.CodeOnly(ExchangeTestSupport.ReadSource("Core\ManagerBox.vb"))
        Assert.Contains("If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(owner, text, caption, buttons, icon, defaultButton)", src, StringComparison.Ordinal)
        Dim prepare As Integer = src.IndexOf("Prepare(dialog, owner)", StringComparison.Ordinal)
        Dim attach As Integer = src.IndexOf("ThemeService.Attach(dialog)", StringComparison.Ordinal)
        Dim show As Integer = src.IndexOf("dialog.ShowDialog(owner)", StringComparison.Ordinal)
        Assert.True(prepare > 0 AndAlso attach > 0 AndAlso show > prepare, "the window is prepared before it is shown")
        ' the preparation is the method that themes the window
        Dim body As Integer = src.IndexOf("Private Shared Sub Prepare(", StringComparison.Ordinal)
        Assert.True(body > 0 AndAlso attach > body, "ThemeService.Attach belongs to Prepare")
    End Sub

    ' --- the mapping, as this program compiles it -------------------------------------------------------------

    <Fact>
    Public Sub Each_question_set_maps_to_a_well_formed_question_with_the_call_sites_default_and_the_cancel_answer()
        For Each buttons As MessageBoxButtons In QuestionSets
            Assert.True(ViewerBoxMapping.IsQuestion(buttons))
            Assert.Equal(WindowsOrder(buttons), ViewerBoxMapping.AnswersOf(buttons))
            For Each defaultButton As MessageBoxDefaultButton In Defaults
                Dim spec As AskSpec = ViewerBoxMapping.SpecFor("Proceed?", "Caption", buttons, MessageBoxIcon.Warning, defaultButton)
                Assert.Empty(AskRules.Problems(spec))
                Assert.True(AskRules.DefaultIsSafe(spec))
                Assert.Equal(WindowsDefaultIndex(buttons, defaultButton), spec.DefaultIndex)
                Assert.Equal(WindowsOrder(buttons).Length - 1, spec.CancelIndex)
                Assert.Equal(EscapeAnswer(buttons), ViewerBoxMapping.AnswerOf(buttons, spec.CancelIndex))
                ' an index the window never answers is the cancel answer, the one that does nothing
                Assert.Equal(EscapeAnswer(buttons), ViewerBoxMapping.AnswerOf(buttons, -1))
            Next
        Next
        For Each notQuestion As MessageBoxButtons In New MessageBoxButtons() {MessageBoxButtons.OK, MessageBoxButtons.RetryCancel, MessageBoxButtons.AbortRetryIgnore}
            Assert.False(ViewerBoxMapping.IsQuestion(notQuestion))
            Assert.Null(ViewerBoxMapping.SpecFor("t", "c", notQuestion, MessageBoxIcon.None, MessageBoxDefaultButton.Button1))
        Next
    End Sub

    <Fact>
    Public Sub The_four_answers_have_a_caption_in_all_thirteen_languages_in_the_Share_Managers_tables()
        Dim saved As String = Localization.CurrentCode
        Try
            Assert.Equal(13, Localization.Codes.Length)
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                For Each answer As DialogResult In New DialogResult() {DialogResult.Yes, DialogResult.No, DialogResult.OK, DialogResult.Cancel}
                    Assert.False(String.IsNullOrWhiteSpace(ViewerBoxMapping.CaptionOf(answer)), code & ": no caption for " & answer.ToString())
                Next
            Next
            Localization.CurrentCode = "en"
            Assert.Equal("Yes", ViewerBoxMapping.CaptionOf(DialogResult.Yes))
            Assert.Equal("No", ViewerBoxMapping.CaptionOf(DialogResult.No))
            Assert.Equal("OK", ViewerBoxMapping.CaptionOf(DialogResult.OK))
            Assert.Equal("Cancel", ViewerBoxMapping.CaptionOf(DialogResult.Cancel))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ' --- the real window ---------------------------------------------------------------------------------------

    Private Shared Sub InLanguage(code As String, check As Action)
        ExchangeTestSupport.OnUi(
            Sub()
                Dim saved As String = Localization.CurrentCode
                Try
                    Localization.CurrentCode = code
                    ButtonGlyphs.AssetOpener = AddressOf CompanionAssets.OpenBundledAsset
                    check()
                Finally
                    Localization.CurrentCode = saved
                End Try
            End Sub)
    End Sub

    Private Shared Sub CollectButtons(parent As Control, into As List(Of Button))
        For Each c As Control In parent.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then into.Add(b)
            CollectButtons(c, into)
        Next
    End Sub

    ''' <summary>The answer buttons in SPEC order.</summary>
    Private Shared Function AnswerButtons(dialog As Form) As List(Of Button)
        Dim found As New List(Of Button)()
        CollectButtons(dialog, found)
        Return found.Where(Function(b) b.Name.StartsWith("ask_Button", StringComparison.Ordinal)).
            OrderBy(Function(b) Integer.Parse(b.Name.Substring("ask_Button".Length), CultureInfo.InvariantCulture)).ToList()
    End Function

    Private Shared Sub CollectLeaves(parent As Control, into As List(Of Control))
        For Each c As Control In parent.Controls
            If c.Controls.Count = 0 Then
                If c.Visible AndAlso c.Name <> "contextHelp" Then into.Add(c)
            Else
                CollectLeaves(c, into)
            End If
        Next
    End Sub

    ''' <summary>The sentences that are wrong with the question window of a set at a display scale (empty = sound).</summary>
    Private Shared Function WindowProblems(label As String, buttons As MessageBoxButtons, defaultButton As MessageBoxDefaultButton,
                                           factor As Single, mirrored As Boolean) As List(Of String)
        Dim found As New List(Of String)()
        Dim spec As AskSpec = ViewerBoxMapping.SpecFor(LongBody, "Caption", buttons, MessageBoxIcon.Warning, defaultButton)
        Using dialog As New Ask_Dialog(spec)
            Dim auto As Single = dialog.DeviceDpi / 96.0F
            Dim extra As Single = factor / auto
            If Math.Abs(extra - 1.0F) > 0.01F Then
                dialog.Font = New Font(dialog.Font.FontFamily, dialog.Font.SizeInPoints * extra)
                dialog.Scale(New SizeF(extra, extra))
            End If
            ThemeService.Attach(dialog)
            dialog.StartPosition = FormStartPosition.Manual
            dialog.Location = New Point(20, 20)
            dialog.ShowInTaskbar = False
            dialog.Show()
            Try
                Application.DoEvents()
                If mirrored AndAlso dialog.RightToLeft <> RightToLeft.Yes Then found.Add(label & ": a right-to-left language does not mirror the window")
                If Not mirrored AndAlso dialog.RightToLeft = RightToLeft.Yes Then found.Add(label & ": a left-to-right language mirrors the window")

                Dim placed As List(Of Button) = AnswerButtons(dialog)
                If placed.Count <> spec.Buttons.Count Then
                    found.Add(label & ": " & placed.Count.ToString(CultureInfo.InvariantCulture) & " buttons for " & spec.Buttons.Count.ToString(CultureInfo.InvariantCulture) & " answers")
                    Return found
                End If
                Dim xs As List(Of Integer) = placed.Select(Function(b) b.PointToScreen(Point.Empty).X).ToList()

                ' reading order: the spec's LAST answer (Cancel / No) reads first, the affirmative one last, in both directions
                For i As Integer = 0 To placed.Count - 2
                    Dim ordered As Boolean = If(mirrored, xs(i + 1) > xs(i), xs(i + 1) < xs(i))
                    If Not ordered Then found.Add(label & ": answer " & (i + 1).ToString(CultureInfo.InvariantCulture) & " must read before answer " & i.ToString(CultureInfo.InvariantCulture))
                Next
                Dim firstRead As Integer = If(mirrored, xs.Max(), xs.Min())
                If xs(spec.CancelIndex) <> firstRead Then found.Add(label & ": the cancel answer is not the first one the eye meets")

                ' captions are whole, buttons stay inside the window and do not overlap each other
                Dim client As Rectangle = dialog.RectangleToScreen(dialog.ClientRectangle)
                Dim rects As List(Of Rectangle) = placed.Select(Function(b) New Rectangle(b.PointToScreen(Point.Empty), b.Size)).ToList()
                For i As Integer = 0 To placed.Count - 1
                    Dim need As Integer = TextRenderer.MeasureText(placed(i).Text.Replace("&", ""), placed(i).Font).Width + ButtonGlyphs.ExtraWidth(placed(i))
                    If placed(i).Width < need Then found.Add(label & ": '" & placed(i).Text & "' is " & placed(i).Width.ToString(CultureInfo.InvariantCulture) & " wide and needs " & need.ToString(CultureInfo.InvariantCulture))
                    If Not client.Contains(rects(i)) Then found.Add(label & ": '" & placed(i).Text & "' " & rects(i).ToString() & " leaves the window " & client.ToString())
                Next

                ' captions are the answers' words; the plain answers carry no glyph, the dismissing ones the cross
                Dim order As DialogResult() = WindowsOrder(buttons)
                For i As Integer = 0 To placed.Count - 1
                    If placed(i).Text.Replace("&", "") <> spec.Buttons(i).Caption Then found.Add(label & ": the button reads '" & placed(i).Text & "', the answer is '" & spec.Buttons(i).Caption & "'")
                    Dim dismissing As Boolean = order(i) = DialogResult.No OrElse order(i) = DialogResult.Cancel
                    If ButtonGlyphs.CarriesGlyph(placed(i)) <> dismissing Then
                        found.Add(label & ": '" & placed(i).Text & "' " & If(dismissing, "has no cross", "carries a glyph, the plain answers are text-only"))
                    End If
                Next

                ' Enter takes the call site's default, Escape the cancel answer
                If Not Object.ReferenceEquals(dialog.AcceptButton, placed(WindowsDefaultIndex(buttons, defaultButton))) Then found.Add(label & ": Enter is not on the default answer")
                If Not Object.ReferenceEquals(dialog.CancelButton, placed(placed.Count - 1)) Then found.Add(label & ": Escape is not on the cancel answer")

                ' nothing visible leaves the client area or lies over another control
                Dim leaves As New List(Of Control)()
                CollectLeaves(dialog, leaves)
                For i As Integer = 0 To leaves.Count - 1
                    Dim a As Rectangle = New Rectangle(leaves(i).PointToScreen(Point.Empty), leaves(i).Size)
                    If Not client.Contains(a) Then found.Add(label & ": " & If(leaves(i).Name.Length = 0, leaves(i).GetType().Name, leaves(i).Name) & " " & a.ToString() & " leaves the window " & client.ToString())
                    For j As Integer = i + 1 To leaves.Count - 1
                        Dim overlap As Rectangle = Rectangle.Intersect(a, New Rectangle(leaves(j).PointToScreen(Point.Empty), leaves(j).Size))
                        If overlap.Width > 0 AndAlso overlap.Height > 0 Then
                            found.Add(label & ": " & If(leaves(i).Name.Length = 0, leaves(i).GetType().Name, leaves(i).Name) & " lies over " & If(leaves(j).Name.Length = 0, leaves(j).GetType().Name, leaves(j).Name))
                        End If
                    Next
                Next
                Dim area As Rectangle = Screen.FromControl(dialog).WorkingArea
                If dialog.Width > area.Width OrElse dialog.Height > area.Height Then found.Add(label & ": the window " & dialog.Size.ToString() & " is larger than the working area " & area.Size.ToString())
            Finally
                dialog.Close()
            End Try
        End Using
        Return found
    End Function

    <Theory>
    <InlineData("en", False)>
    <InlineData("ru", False)>
    <InlineData("ar", True)>
    Public Sub Each_question_window_reads_cancel_first_and_stays_clear_at_100_and_175_percent(code As String, mirrored As Boolean)
        InLanguage(code,
            Sub()
                Assert.Equal(mirrored, Localization.IsRightToLeft())
                Dim problems As New List(Of String)()
                For Each buttons As MessageBoxButtons In QuestionSets
                    For Each defaultButton As MessageBoxDefaultButton In Defaults
                        For Each factor As Single In New Single() {1.0F, 1.75F}
                            Dim label As String = buttons.ToString() & "/" & defaultButton.ToString() & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%"
                            problems.AddRange(WindowProblems(label, buttons, defaultButton, factor, mirrored))
                        Next
                    Next
                Next
                Assert.True(problems.Count = 0, String.Join(vbLf, problems))
            End Sub)
    End Sub

    ' --- ManagerBox.Show end to end -----------------------------------------------------------------------------

    ''' <summary>What the window looked like the moment it was up, read by the timer before it acts.</summary>
    Private NotInheritable Class Seen
        Friend Found As Boolean
        Friend TopMost As Boolean
        Friend ShowInTaskbar As Boolean
        Friend StartPosition As FormStartPosition
        Friend OwnerIsExpected As Boolean
        Friend HasOwner As Boolean
        Friend AcceptName As String = ""
        Friend CancelName As String = ""
        Friend RightToLeft As RightToLeft
        Friend Title As String = ""
        Friend Body As String = ""
    End Class

    ''' <summary>Starts a timer on the calling (UI) thread that waits for the question window, reads it, then lets
    ''' <paramref name="act"/> answer it through its own buttons - and closes it if the action did not. The caller then
    ''' makes the call that shows the window.</summary>
    Private NotInheritable Class Driver
        Implements IDisposable

        Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 20}
        Private ReadOnly started As DateTime = DateTime.UtcNow
        Friend ReadOnly Result As New Seen()

        Friend Sub New(expectedOwner As Form, act As Action(Of Ask_Dialog))
            AddHandler timer.Tick,
                Sub()
                    Dim dialog As Ask_Dialog = Application.OpenForms.OfType(Of Ask_Dialog)().FirstOrDefault()
                    If dialog Is Nothing Then
                        If (DateTime.UtcNow - started).TotalSeconds > 15 Then timer.Stop()
                        Return
                    End If
                    timer.Stop()
                    Result.Found = True
                    Try
                        Result.TopMost = dialog.TopMost
                        Result.ShowInTaskbar = dialog.ShowInTaskbar
                        Result.StartPosition = dialog.StartPosition
                        Result.HasOwner = dialog.Owner IsNot Nothing
                        Result.OwnerIsExpected = expectedOwner IsNot Nothing AndAlso Object.ReferenceEquals(dialog.Owner, expectedOwner)
                        Result.AcceptName = If(TryCast(dialog.AcceptButton, Button)?.Name, "")
                        Result.CancelName = If(TryCast(dialog.CancelButton, Button)?.Name, "")
                        Result.RightToLeft = dialog.RightToLeft
                        Result.Title = dialog.Text
                        Dim message As Control = dialog.Controls.Find("ask_Message", True).FirstOrDefault()
                        Result.Body = If(message Is Nothing, "", message.Text)
                        act(dialog)
                    Finally
                        If Not dialog.IsDisposed AndAlso dialog.Visible Then dialog.Close()
                    End Try
                End Sub
            timer.Start()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            timer.Stop()
            timer.Dispose()
        End Sub
    End Class

    Private Shared Sub PressAnswer(dialog As Ask_Dialog, index As Integer)
        AnswerButtons(dialog)(index).PerformClick()
    End Sub

    ''' <summary>A small owner window for the checks that need one, away from the middle of the screen.</summary>
    Private Shared Function NewOwner() As Form
        Dim owner As New Form With {.StartPosition = FormStartPosition.Manual, .Location = New Point(40, 40), .Size = New Size(320, 200), .ShowInTaskbar = False}
        owner.Show()
        Application.DoEvents()
        Return owner
    End Function

    <Fact>
    Public Sub Each_answer_comes_back_as_the_DialogResult_the_call_site_tests_for()
        InLanguage("en",
            Sub()
                Using owner As Form = NewOwner()
                    For Each buttons As MessageBoxButtons In QuestionSets
                        Dim expected As DialogResult() = WindowsOrder(buttons)
                        For i As Integer = 0 To expected.Length - 1
                            Dim index As Integer = i
                            Dim result As DialogResult
                            Using driver As New Driver(owner, Sub(d) PressAnswer(d, index))
                                result = ManagerBox.Show(owner, "Proceed?", "Caption", buttons, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                                Assert.True(driver.Result.Found, buttons.ToString() & ": the question window never appeared")
                            End Using
                            Assert.Equal(expected(i), result)
                        Next
                    Next
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub Escape_and_the_close_box_answer_the_cancel_answer_of_every_set()
        InLanguage("en",
            Sub()
                Using owner As Form = NewOwner()
                    For Each buttons As MessageBoxButtons In QuestionSets
                        ' Escape is the cancel button the window binds
                        Dim escape As DialogResult
                        Using driver As New Driver(owner, Sub(d) DirectCast(d.CancelButton, Button).PerformClick())
                            escape = ManagerBox.Show(owner, "Proceed?", "Caption", buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1)
                            Assert.True(driver.Result.Found)
                        End Using
                        Assert.Equal(EscapeAnswer(buttons), escape)
                        ' the close box answers the same, even for Yes/No, whose system box had no close box at all
                        Dim closed As DialogResult
                        Using driver As New Driver(owner, Sub(d) d.Close())
                            closed = ManagerBox.Show(owner, "Proceed?", "Caption", buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1)
                            Assert.True(driver.Result.Found)
                        End Using
                        Assert.Equal(EscapeAnswer(buttons), closed)
                    Next
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_default_button_is_the_one_the_call_site_named_and_the_text_and_caption_are_the_callers()
        InLanguage("en",
            Sub()
                Using owner As Form = NewOwner()
                    For Each buttons As MessageBoxButtons In QuestionSets
                        For Each defaultButton As MessageBoxDefaultButton In Defaults
                            Using driver As New Driver(owner, Sub(d) DirectCast(d.CancelButton, Button).PerformClick())
                                ManagerBox.Show(owner, "Proceed with this?", "Confirm it", buttons, MessageBoxIcon.Warning, defaultButton)
                                Assert.True(driver.Result.Found)
                                Dim label As String = buttons.ToString() & "/" & defaultButton.ToString()
                                Assert.Equal("ask_Button" & WindowsDefaultIndex(buttons, defaultButton).ToString(CultureInfo.InvariantCulture), driver.Result.AcceptName)
                                Assert.Equal("ask_Button" & (WindowsOrder(buttons).Length - 1).ToString(CultureInfo.InvariantCulture), driver.Result.CancelName)
                                Assert.True("Confirm it" = driver.Result.Title, label & ": the caption is the window's title")
                                Assert.True("Proceed with this?" = driver.Result.Body, label & ": the text is the window's message")
                            End Using
                        Next
                    Next
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub An_owner_pinned_topmost_keeps_the_question_above_it_and_a_plain_owner_does_not_pin_it()
        InLanguage("en",
            Sub()
                Using owner As Form = NewOwner()
                    Using driver As New Driver(owner, Sub(d) PressAnswer(d, 0))
                        ManagerBox.Show(owner, "Proceed?", "Caption", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                        Assert.True(driver.Result.Found)
                        Assert.False(driver.Result.TopMost, "a window in the normal band is asked over in the normal band")
                        Assert.True(driver.Result.OwnerIsExpected)
                        Assert.False(driver.Result.ShowInTaskbar)
                    End Using
                    owner.TopMost = True
                    Try
                        Using driver As New Driver(owner, Sub(d) PressAnswer(d, 0))
                            ManagerBox.Show(owner, "Proceed?", "Caption", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                            Assert.True(driver.Result.Found)
                            Assert.True(driver.Result.TopMost, "a question over a window pinned on top is created in the topmost band too")
                            Assert.True(driver.Result.OwnerIsExpected)
                        End Using
                        ' a window OWNED by the pinned one is over the pinned band as well
                        Using child As New Form With {.Owner = owner, .StartPosition = FormStartPosition.Manual, .Location = New Point(60, 60), .Size = New Size(200, 120), .ShowInTaskbar = False}
                            child.Show()
                            Application.DoEvents()
                            Assert.True(ManagerBox.InTopmostBand(child))
                            Using driver As New Driver(child, Sub(d) PressAnswer(d, 0))
                                ManagerBox.Show(child, "Proceed?", "Caption", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                                Assert.True(driver.Result.Found)
                                Assert.True(driver.Result.TopMost)
                            End Using
                        End Using
                    Finally
                        owner.TopMost = False
                    End Try
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub A_question_with_no_owner_is_centred_on_the_screen_topmost_and_in_the_taskbar()
        InLanguage("en",
            Sub()
                Using driver As New Driver(Nothing, Sub(d) DirectCast(d.CancelButton, Button).PerformClick())
                    Dim answer As DialogResult = ManagerBox.Show("Another program asks.", "Share Manager", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                    Assert.True(driver.Result.Found)
                    Assert.Equal(DialogResult.Cancel, answer)
                    Assert.False(driver.Result.HasOwner)
                    Assert.True(driver.Result.TopMost)
                    Assert.True(driver.Result.ShowInTaskbar)
                    Assert.Equal(FormStartPosition.CenterScreen, driver.Result.StartPosition)
                    Assert.Equal("ask_Button1", driver.Result.AcceptName)
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub An_owner_that_is_gone_falls_back_to_a_question_with_no_owner_instead_of_throwing()
        InLanguage("en",
            Sub()
                Dim gone As New Form()
                gone.Dispose()
                Using driver As New Driver(Nothing, Sub(d) PressAnswer(d, 0))
                    Dim answer As DialogResult = ManagerBox.Show(gone, "Proceed?", "Caption", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                    Assert.True(driver.Result.Found)
                    Assert.Equal(DialogResult.Yes, answer)
                    Assert.False(driver.Result.HasOwner)
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub A_caller_on_another_thread_gets_the_window_made_on_its_owners_thread()
        InLanguage("en",
            Sub()
                Using owner As Form = NewOwner()
                    Using driver As New Driver(owner, Sub(d) PressAnswer(d, 1))
                        Dim worker As Task(Of DialogResult) = Task.Run(
                            Function() ManagerBox.Show(owner, "Proceed?", "Caption", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1))
                        Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(20)
                        While Not worker.IsCompleted AndAlso DateTime.UtcNow < deadline
                            Application.DoEvents()
                            Thread.Sleep(5)
                        End While
                        Assert.True(worker.IsCompleted, "the question asked from another thread never returned")
                        Assert.True(driver.Result.Found)
                        Assert.True(driver.Result.OwnerIsExpected)
                        Assert.Equal(DialogResult.No, worker.GetAwaiter().GetResult())
                    End Using
                End Using
            End Sub)
    End Sub

    <Fact>
    Public Sub The_question_is_mirrored_and_answers_in_arabic()
        InLanguage("ar",
            Sub()
                Using owner As Form = NewOwner()
                    Using driver As New Driver(owner, Sub(d) PressAnswer(d, 0))
                        Dim answer As DialogResult = ManagerBox.Show(owner, "Proceed?", "Caption", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3)
                        Assert.True(driver.Result.Found)
                        Assert.Equal(RightToLeft.Yes, driver.Result.RightToLeft)
                        Assert.Equal(DialogResult.Yes, answer)
                        Assert.Equal("ask_Button2", driver.Result.AcceptName)
                    End Using
                End Using
            End Sub)
    End Sub

End Class
