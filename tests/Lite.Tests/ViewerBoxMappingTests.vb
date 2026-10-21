#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' Ticket 243 T-12a: the viewer's Yes/No, OK/Cancel and Yes/No/Cancel QUESTIONS are asked through the product-owned
''' question window instead of a system message box (BUTTONS 2.0 section 1). This holds the pure mapping
''' (<see cref="ViewerBoxMapping"/>) button by button, the shape of the real window built from each mapped spec at 100 %
''' and 175 % in a left-to-right, a Cyrillic and a right-to-left language, and - by reading the source, like the other
''' scans of this suite - that ViewerBox routes every overload that takes buttons and that the x86 fallback keeps the
''' stock call.
''' </summary>
<Collection("LocalizationState")>
Public Class ViewerBoxMappingTests

    ''' <summary>The glyph catalog is an embedded payload of the exe, absent here: read the vendored copies instead, as
    ''' the other glyph tests do (the same directory, the same opener).</summary>
    Shared Sub New()
        ButtonGlyphs.AssetOpener = Function(logicalName As String) As Stream
                                       Dim glyphFile As String = Path.Combine(AppContext.BaseDirectory, "TestData", "glyphs", Path.GetFileName(logicalName))
                                       Return If(File.Exists(glyphFile), CType(File.OpenRead(glyphFile), Stream), Nothing)
                                   End Function
    End Sub

    ' --- the Windows reference, written down independently of the mapping ----------------------------------

    ''' <summary>The buttons of a set in the order the SYSTEM box lays them out (what Button1/2/3 name).</summary>
    Private Shared Function WindowsOrder(buttons As MessageBoxButtons) As DialogResult()
        Select Case buttons
            Case MessageBoxButtons.OKCancel : Return New DialogResult() {DialogResult.OK, DialogResult.Cancel}
            Case MessageBoxButtons.YesNo : Return New DialogResult() {DialogResult.Yes, DialogResult.No}
            Case MessageBoxButtons.YesNoCancel : Return New DialogResult() {DialogResult.Yes, DialogResult.No, DialogResult.Cancel}
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(buttons))
        End Select
    End Function

    ''' <summary>The answer the system box makes the default: the slot named, or the first button when the set has no such slot.</summary>
    Private Shared Function WindowsDefault(buttons As MessageBoxButtons, defaultButton As MessageBoxDefaultButton) As DialogResult
        Dim order As DialogResult() = WindowsOrder(buttons)
        Dim slot As Integer = CInt(defaultButton) \ 256
        If slot < 0 OrElse slot >= order.Length Then slot = 0
        Return order(slot)
    End Function

    Private Shared ReadOnly QuestionSets As MessageBoxButtons() = {MessageBoxButtons.OKCancel, MessageBoxButtons.YesNo, MessageBoxButtons.YesNoCancel}

    Private Shared Function AllDefaults() As MessageBoxDefaultButton()
        Return [Enum].GetValues(GetType(MessageBoxDefaultButton)).Cast(Of MessageBoxDefaultButton)().ToArray()
    End Function

    ' --- the pure mapping --------------------------------------------------------------------------------

    <Fact>
    Public Sub Only_the_three_question_sets_are_routed()
        Dim all As MessageBoxButtons() = [Enum].GetValues(GetType(MessageBoxButtons)).Cast(Of MessageBoxButtons)().ToArray()
        Assert.True(all.Length >= 6, "MessageBoxButtons lost a member?")
        For Each b As MessageBoxButtons In all
            Dim expected As Boolean = QuestionSets.Contains(b)
            Assert.Equal(expected, ViewerBoxMapping.IsQuestion(b))
            If expected Then Continue For
            ' OK alone, RetryCancel and AbortRetryIgnore stay the system box: nothing is built for them
            Assert.Null(ViewerBoxMapping.SpecFor("text", "caption", b, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1))
            Assert.Empty(ViewerBoxMapping.AnswersOf(b))
            Assert.Equal(-1, ViewerBoxMapping.CancelIndexOf(b))
        Next
        Assert.False(ViewerBoxMapping.IsQuestion(MessageBoxButtons.OK))
        Assert.False(ViewerBoxMapping.IsQuestion(MessageBoxButtons.RetryCancel))
        Assert.False(ViewerBoxMapping.IsQuestion(MessageBoxButtons.AbortRetryIgnore))
    End Sub

    <Theory>
    <InlineData(MessageBoxButtons.OKCancel, "OK,Cancel", 1)>
    <InlineData(MessageBoxButtons.YesNo, "Yes,No", 1)>
    <InlineData(MessageBoxButtons.YesNoCancel, "Yes,No,Cancel", 2)>
    Public Sub The_spec_lists_the_affirmative_answer_first_and_Escape_answers_the_cancel_answer(buttons As MessageBoxButtons, order As String, cancelIndex As Integer)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = "en"
            Dim spec As AskSpec = ViewerBoxMapping.SpecFor("Proceed?", "Caption", buttons, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
            Assert.NotNull(spec)
            Assert.Equal(order, String.Join(",", spec.Buttons.Select(Function(b) b.Caption)))
            Assert.Equal(cancelIndex, spec.CancelIndex)
            Assert.Equal(cancelIndex, ViewerBoxMapping.CancelIndexOf(buttons))
            Assert.Equal(cancelIndex, AskRules.ClosingAnswer(spec))

            ' the DialogResult of every index, in the same order the captions name
            Dim names As String() = order.Split(","c)
            For i As Integer = 0 To names.Length - 1
                Assert.Equal(names(i), ViewerBoxMapping.AnswerOf(buttons, i).ToString())
            Next
            ' Escape and the close box are the cancel answer; so is anything outside the set
            Dim cancelAnswer As DialogResult = ViewerBoxMapping.AnswerOf(buttons, cancelIndex)
            Assert.Equal(If(buttons = MessageBoxButtons.YesNo, DialogResult.No, DialogResult.Cancel), cancelAnswer)
            Assert.Equal(cancelAnswer, ViewerBoxMapping.AnswerOf(buttons, -1))
            Assert.Equal(cancelAnswer, ViewerBoxMapping.AnswerOf(buttons, 99))
            Assert.Empty(AskRules.Problems(spec))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Theory>
    <InlineData("en", "Yes", "No", "OK", "Cancel")>
    <InlineData("ru", "Да", "Нет", "ОК", "Отмена")>
    Public Sub The_answers_are_captioned_in_the_interface_language(code As String, yes As String, no As String, ok As String, cancel As String)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(yes, ViewerBoxMapping.CaptionOf(DialogResult.Yes))
            Assert.Equal(no, ViewerBoxMapping.CaptionOf(DialogResult.No))
            Assert.Equal(ok, ViewerBoxMapping.CaptionOf(DialogResult.OK))
            Assert.Equal(cancel, ViewerBoxMapping.CaptionOf(DialogResult.Cancel))
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    <Fact>
    Public Sub Every_language_has_a_caption_for_every_answer()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In Localization.Codes
                Localization.CurrentCode = code
                For Each answer As DialogResult In New DialogResult() {DialogResult.Yes, DialogResult.No, DialogResult.OK, DialogResult.Cancel}
                    Assert.False(String.IsNullOrWhiteSpace(ViewerBoxMapping.CaptionOf(answer)), code & ": no caption for " & answer.ToString())
                Next
            Next
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>The call sites' defaults are kept exactly: for every set and every default the system box accepts, the
    ''' answer that is the default in the question window is the one the system box would have made the default.</summary>
    <Fact>
    Public Sub The_default_is_never_a_different_answer_than_Windows_would_have_picked()
        For Each buttons As MessageBoxButtons In QuestionSets
            For Each defaultButton As MessageBoxDefaultButton In AllDefaults()
                Dim spec As AskSpec = ViewerBoxMapping.SpecFor("t", "c", buttons, MessageBoxIcon.None, defaultButton)
                Dim label As String = buttons.ToString() & "/" & defaultButton.ToString()
                Assert.True(spec.DefaultIndex >= 0 AndAlso spec.DefaultIndex < spec.Buttons.Count, label & ": default out of range")
                Assert.Equal(WindowsDefault(buttons, defaultButton), ViewerBoxMapping.AnswerOf(buttons, spec.DefaultIndex))
                Assert.Equal(spec.DefaultIndex, ViewerBoxMapping.DefaultIndexOf(buttons, defaultButton))
            Next
        Next
        ' the three the viewer actually names, spelled out
        Assert.Equal(0, ViewerBoxMapping.DefaultIndexOf(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button1))
        Assert.Equal(1, ViewerBoxMapping.DefaultIndexOf(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2))
        Assert.Equal(2, ViewerBoxMapping.DefaultIndexOf(MessageBoxButtons.YesNoCancel, MessageBoxDefaultButton.Button3))
        ' a slot the set does not have is the first button, as in the system box
        Assert.Equal(0, ViewerBoxMapping.DefaultIndexOf(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button3))
        Assert.Equal(0, ViewerBoxMapping.DefaultIndexOf(MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button4))
    End Sub

    <Fact>
    Public Sub Every_icon_of_the_system_maps_to_its_picture()
        For Each icon As MessageBoxIcon In [Enum].GetValues(GetType(MessageBoxIcon)).Cast(Of MessageBoxIcon)()
            Dim expected As AskIcon
            Select Case CInt(icon)
                Case 16 : expected = AskIcon.Error           ' Hand = Stop = Error
                Case 32 : expected = AskIcon.Question
                Case 48 : expected = AskIcon.Warning         ' Exclamation = Warning
                Case 64 : expected = AskIcon.Info            ' Asterisk = Information
                Case Else : expected = AskIcon.None
            End Select
            Assert.Equal(expected, ViewerBoxMapping.IconOf(icon))
            Assert.Equal(expected, ViewerBoxMapping.SpecFor("t", "c", MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button1).Icon)
        Next
        Assert.Equal(AskIcon.None, ViewerBoxMapping.IconOf(MessageBoxIcon.None))
    End Sub

    <Fact>
    Public Sub The_caption_is_the_title_and_an_empty_one_is_the_product_name()
        Dim named As AskSpec = ViewerBoxMapping.SpecFor("Delete the file?", "Confirm deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
        Assert.Equal("Confirm deletion", named.Title)
        Assert.Equal("Delete the file?", named.Body)
        Assert.Equal("", named.Detail)
        For Each caption As String In New String() {"", Nothing}
            Dim bare As AskSpec = ViewerBoxMapping.SpecFor("Delete the file?", caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            Assert.Equal(ProductNames.Full, bare.Title)
        Next
        ' a missing text is an empty body, not a failure
        Assert.Equal("", ViewerBoxMapping.SpecFor(Nothing, "c", MessageBoxButtons.OKCancel, MessageBoxIcon.None, MessageBoxDefaultButton.Button1).Body)
    End Sub

    ''' <summary>The component policy: OK and Yes are plain text answers (no glyph role, no tint, never marked
    ''' destructive), Cancel and No are the dismissing role - no spec built here can make a destructive answer the
    ''' default, because none is destructive.</summary>
    <Fact>
    Public Sub Plain_answers_are_text_only_and_the_dismissing_answers_are_dismissing()
        For Each buttons As MessageBoxButtons In QuestionSets
            For Each defaultButton As MessageBoxDefaultButton In AllDefaults()
                Dim spec As AskSpec = ViewerBoxMapping.SpecFor("t", "c", buttons, MessageBoxIcon.Question, defaultButton)
                Assert.True(AskRules.DefaultIsSafe(spec))
                For i As Integer = 0 To spec.Buttons.Count - 1
                    Dim answer As DialogResult = ViewerBoxMapping.AnswerOf(buttons, i)
                    Dim button As AskButton = spec.Buttons(i)
                    Assert.False(button.Destructive)
                    Assert.Equal("", button.GlyphId)
                    If answer = DialogResult.No OrElse answer = DialogResult.Cancel Then
                        Assert.Equal(ButtonGlyphRole.Dismissing, button.Role)
                    Else
                        Assert.Equal(ButtonGlyphRole.Neutral, button.Role)
                    End If
                Next
            Next
        Next
    End Sub

    ' --- the owner ----------------------------------------------------------------------------------------

    Private NotInheritable Class UnreadableOwner
        Implements IWin32Window

        Friend ReadOnly Zero As Boolean

        Friend Sub New(zero As Boolean)
            Me.Zero = zero
        End Sub

        Public ReadOnly Property Handle As IntPtr Implements IWin32Window.Handle
            Get
                If Zero Then Return IntPtr.Zero
                Throw New InvalidOperationException("the handle cannot be read")
            End Get
        End Property
    End Class

    Private NotInheritable Class PlainOwner
        Implements IWin32Window

        Public ReadOnly Property Handle As IntPtr Implements IWin32Window.Handle
            Get
                Return New IntPtr(1234)
            End Get
        End Property
    End Class

    <Fact>
    Public Sub An_unreadable_owner_falls_back_instead_of_throwing()
        OnSta(Sub()
                  Assert.Null(ViewerBoxMapping.UsableOwner(Nothing))
                  Assert.Null(ViewerBoxMapping.UsableOwner(New UnreadableOwner(zero:=True)))
                  Assert.Null(ViewerBoxMapping.UsableOwner(New UnreadableOwner(zero:=False)))
                  Dim gone As New Form()
                  gone.Dispose()
                  Assert.Null(ViewerBoxMapping.UsableOwner(gone))
                  Dim plain As New PlainOwner()
                  Assert.Same(plain, ViewerBoxMapping.UsableOwner(plain))
                  Using live As New Form()
                      Assert.Same(live, ViewerBoxMapping.UsableOwner(live))
                  End Using
              End Sub)
    End Sub

    ' --- the real window, built from each mapped spec ---------------------------------------------------------

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

    Private Const LongBody As String = "This question has a deliberately long text, so that it wraps over several lines at every scale and in " &
        "every language, and the answer row under it has to stay clear of it and inside the window whatever the width of the captions is."

    ''' <summary>The sentences that are wrong with the question window of a spec at a display scale (empty = sound).</summary>
    Private Shared Function WindowProblems(label As String, spec As AskSpec, buttons As MessageBoxButtons, factor As Single, rightToLeft As Boolean) As List(Of String)
        Dim found As New List(Of String)()
        Using dialog As New Ask_Dialog(spec)
            Dim auto As Single = dialog.DeviceDpi / 96.0F
            Dim extra As Single = factor / auto
            If Math.Abs(extra - 1.0F) > 0.01F Then
                dialog.Font = New Font(dialog.Font.FontFamily, dialog.Font.SizeInPoints * extra)
                dialog.Scale(New SizeF(extra, extra))
            End If
            dialog.StartPosition = FormStartPosition.Manual
            dialog.Location = New Point(-20000, -20000)
            dialog.ShowInTaskbar = False
            dialog.Show()
            Try
                Dim placed As List(Of Button) = AnswerButtons(dialog)
                If placed.Count <> spec.Buttons.Count Then
                    found.Add(label & ": " & placed.Count.ToString(CultureInfo.InvariantCulture) & " buttons for " & spec.Buttons.Count.ToString(CultureInfo.InvariantCulture) & " answers")
                    Return found
                End If
                Dim xs As List(Of Integer) = placed.Select(Function(b) b.PointToScreen(Point.Empty).X).ToList()

                ' reading order: the spec's LAST answer (Cancel / No) reads first, the affirmative one last, in both directions
                For i As Integer = 0 To placed.Count - 2
                    Dim ordered As Boolean = If(rightToLeft, xs(i + 1) > xs(i), xs(i + 1) < xs(i))
                    If Not ordered Then found.Add(label & ": answer " & (i + 1).ToString(CultureInfo.InvariantCulture) & " must read before answer " & i.ToString(CultureInfo.InvariantCulture))
                Next
                Dim cancelX As Integer = xs(spec.CancelIndex)
                Dim firstRead As Integer = If(rightToLeft, xs.Max(), xs.Min())
                If cancelX <> firstRead Then found.Add(label & ": the cancel answer is not the first one the eye meets")

                ' captions are whole, buttons do not overlap each other and stay inside the window
                Dim client As Rectangle = dialog.RectangleToScreen(dialog.ClientRectangle)
                Dim rects As List(Of Rectangle) = placed.Select(Function(b) New Rectangle(b.PointToScreen(Point.Empty), b.Size)).ToList()
                For i As Integer = 0 To placed.Count - 1
                    Dim need As Integer = TextRenderer.MeasureText(placed(i).Text.Replace("&", ""), placed(i).Font).Width + ButtonGlyphs.ExtraWidth(placed(i))
                    If placed(i).Width < need Then found.Add(label & ": '" & placed(i).Text & "' is " & placed(i).Width.ToString(CultureInfo.InvariantCulture) & " wide and needs " & need.ToString(CultureInfo.InvariantCulture))
                    If Not client.Contains(rects(i)) Then found.Add(label & ": '" & placed(i).Text & "' " & rects(i).ToString() & " leaves the window " & client.ToString())
                    For j As Integer = i + 1 To placed.Count - 1
                        Dim overlap As Rectangle = Rectangle.Intersect(rects(i), rects(j))
                        If overlap.Width > 0 AndAlso overlap.Height > 0 Then found.Add(label & ": '" & placed(i).Text & "' lies over '" & placed(j).Text & "'")
                    Next
                Next

                ' captions are the answers' words, the plain answers carry no glyph and the dismissing ones do
                For i As Integer = 0 To placed.Count - 1
                    Dim answer As DialogResult = ViewerBoxMapping.AnswerOf(buttons, i)
                    If placed(i).Text.Replace("&", "") <> spec.Buttons(i).Caption Then found.Add(label & ": the button reads '" & placed(i).Text & "', the answer is '" & spec.Buttons(i).Caption & "'")
                    Dim dismissing As Boolean = answer = DialogResult.No OrElse answer = DialogResult.Cancel
                    If ButtonGlyphs.CarriesGlyph(placed(i)) <> dismissing Then
                        found.Add(label & ": '" & placed(i).Text & "' " & If(dismissing, "has no cross", "carries a glyph, the plain answers are text-only"))
                    End If
                Next

                ' Enter takes the call site's default, Escape the cancel answer
                If Not Object.ReferenceEquals(dialog.AcceptButton, placed(spec.DefaultIndex)) Then found.Add(label & ": Enter is not on the default answer")
                If Not Object.ReferenceEquals(dialog.CancelButton, placed(spec.CancelIndex)) Then found.Add(label & ": Escape is not on the cancel answer")

                ' the window-wide audit: nothing leaves the client area, lies over a sibling or is clipped
                For Each p As String In DialogLayoutTests.Problems(dialog)
                    found.Add(label & ": " & p)
                Next
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
    Public Sub Each_mapped_question_reads_cancel_first_and_stays_clear_at_100_and_175_percent(code As String, rightToLeft As Boolean)
        Dim saved As String = Localization.CurrentCode
        Try
            Localization.CurrentCode = code
            Assert.Equal(rightToLeft, Localization.IsRightToLeft())
            OnSta(Sub()
                      Dim all As New List(Of String)()
                      For Each buttons As MessageBoxButtons In QuestionSets
                          For Each factor As Single In New Single() {1.0F, 1.75F}
                              For Each body As String In New String() {"Short?", LongBody}
                                  Dim label As String = buttons.ToString() & " " & code & " @" & CInt(factor * 100).ToString(CultureInfo.InvariantCulture) & "%" & If(body.Length > 20, " long", "")
                                  ' the default the viewer's destructive questions use (Button2) where the set has it
                                  Dim spec As AskSpec = ViewerBoxMapping.SpecFor(body, "Confirm", buttons, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                                  all.AddRange(WindowProblems(label, spec, buttons, factor, rightToLeft))
                              Next
                          Next
                      Next
                      Assert.True(all.Count = 0, String.Join(vbLf, all))
                  End Sub)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>Escape and the close box answer the cancel answer, a click answers its own button - checked on the
    ''' real window, through the same mapping ViewerBox uses.</summary>
    <Fact>
    Public Sub The_window_answers_the_cancel_answer_for_the_close_box_and_its_own_index_for_a_click()
        OnSta(Sub()
                  For Each buttons As MessageBoxButtons In QuestionSets
                      Dim spec As AskSpec = ViewerBoxMapping.SpecFor("Proceed?", "Confirm", buttons, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
                      Dim cancelAnswer As DialogResult = ViewerBoxMapping.AnswerOf(buttons, spec.CancelIndex)

                      Using closed As New Ask_Dialog(spec)
                          closed.StartPosition = FormStartPosition.Manual
                          closed.Location = New Point(-20000, -20000)
                          closed.ShowInTaskbar = False
                          closed.Show()
                          closed.Close()
                          Assert.Equal(spec.CancelIndex, closed.Answer)
                          Assert.Equal(cancelAnswer, ViewerBoxMapping.AnswerOf(buttons, closed.Answer))
                      End Using

                      For index As Integer = 0 To spec.Buttons.Count - 1
                          Using clicked As New Ask_Dialog(spec)
                              clicked.StartPosition = FormStartPosition.Manual
                              clicked.Location = New Point(-20000, -20000)
                              clicked.ShowInTaskbar = False
                              clicked.Show()
                              AnswerButtons(clicked)(index).PerformClick()
                              Assert.Equal(index, clicked.Answer)
                              Assert.Equal(ViewerBoxMapping.AnswerOf(buttons, index), ViewerBoxMapping.AnswerOf(buttons, clicked.Answer))
                          End Using
                      Next
                  Next
              End Sub)
    End Sub

    ' --- the source: ViewerBox routes every question overload and net48 keeps the stock call ---------------------

    Private Shared Function SourceText(fileName As String) As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src", fileName)
            If File.Exists(candidate) Then Return File.ReadAllText(candidate)
            dir = dir.Parent
        End While
        Throw New FileNotFoundException("src/" & fileName & " not found above " & AppContext.BaseDirectory)
    End Function

    Private Shared Function ViewerSourceFiles() As List(Of String)
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            Dim candidate As String = Path.Combine(dir.FullName, "src")
            If Directory.Exists(candidate) AndAlso File.Exists(Path.Combine(candidate, "Main_Form.vb")) Then
                Return Directory.GetFiles(candidate, "*.vb", SearchOption.AllDirectories).
                    Where(Function(p) p.IndexOf("\obj\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                      p.IndexOf("\bin\", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                                      p.IndexOf("\FastMediaSorterCompanion\", StringComparison.OrdinalIgnoreCase) < 0).ToList()
            End If
            dir = dir.Parent
        End While
        Throw New DirectoryNotFoundException("src/ not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>A new <c>Show</c> overload that takes <c>buttons</c> and forgets the routing would let a question slip
    ''' back to the system box: every one of them asks <c>IsQuestion(buttons)</c> first.</summary>
    <Fact>
    Public Sub Every_ViewerBox_overload_with_buttons_asks_whether_it_is_a_question()
        Dim text As String = SourceText("ViewerBox.vb")
        Dim withButtons As Integer = Regex.Matches(text, "Friend Shared Function Show\([^)]*buttons As MessageBoxButtons").Count
        Dim routed As Integer = Regex.Matches(text, "ViewerBoxMapping\.IsQuestion\(buttons\)").Count
        Assert.True(withButtons >= 6, "ViewerBox lost an overload? " & withButtons.ToString(CultureInfo.InvariantCulture))
        Assert.Equal(withButtons, routed)
    End Sub

    ''' <summary>The x86 fallback keeps the stock call byte for byte: ViewerBox names the dialog layer and the mapping
    ''' only between <c>#If Not NETFRAMEWORK</c> and its <c>#End If</c>.</summary>
    Private Shared Function OutsideTheModernBlock(text As String, words As String()) As List(Of String)
        Dim found As New List(Of String)()
        ' One entry per open "#If": True when it is the modern-only "Not NETFRAMEWORK" block.
        Dim blocks As New Stack(Of Boolean)()
        Dim lines As String() = text.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
        For i As Integer = 0 To lines.Length - 1
            Dim trimmed As String = lines(i).Trim()
            Dim number As String = (i + 1).ToString(CultureInfo.InvariantCulture)
            If trimmed.StartsWith("#If", StringComparison.Ordinal) Then
                blocks.Push(trimmed.StartsWith("#If Not NETFRAMEWORK", StringComparison.Ordinal))
            ElseIf trimmed.StartsWith("#End If", StringComparison.Ordinal) Then
                If blocks.Count > 0 Then blocks.Pop()
            ElseIf trimmed.StartsWith("#Else", StringComparison.Ordinal) Then
                ' ViewerBox has no #Else; one would make the branch of a block ambiguous for this scan
                found.Add("line " & number & ": unexpected " & trimmed)
            ElseIf Not trimmed.StartsWith("'", StringComparison.Ordinal) AndAlso Not blocks.Contains(True) Then
                For Each word As String In words
                    If trimmed.Contains(word) Then found.Add("line " & number & ": " & trimmed)
                Next
            End If
        Next
        Return found
    End Function

    <Fact>
    Public Sub The_net48_build_of_ViewerBox_never_names_the_question_window()
        Dim outside As List(Of String) = OutsideTheModernBlock(SourceText("ViewerBox.vb"), New String() {"ViewerBoxMapping", "AskDialogs", "AskSpec", "Ask_Dialog"})
        Assert.True(outside.Count = 0, "ViewerBox names the modern question window outside #If Not NETFRAMEWORK:" & vbLf & String.Join(vbLf, outside))
        Assert.StartsWith("#If Not NETFRAMEWORK Then", SourceText("ViewerBoxMapping.vb").TrimStart(), StringComparison.Ordinal)
    End Sub

    ''' <summary>The scanner catches what it is written to catch.</summary>
    <Fact>
    Public Sub The_net48_scanner_catches_a_call_outside_the_block()
        Dim sample As String = "Friend Shared Function A()" & vbLf & "    Return AskDialogs.Choose(Nothing, Nothing)" & vbLf & "End Function" & vbLf &
            "#If Not NETFRAMEWORK Then" & vbLf & "    Dim x = ViewerBoxMapping.IsQuestion(b)" & vbLf & "#End If" & vbLf
        Dim outside As List(Of String) = OutsideTheModernBlock(sample, New String() {"ViewerBoxMapping", "AskDialogs"})
        Assert.Single(outside)
        Assert.Contains("AskDialogs", outside(0))
    End Sub

    ''' <summary>Where a question deliberately stays a system box it says so in the source and is listed here, so the
    ''' list of exceptions is one a person can read: the close-time prompt ("No" closes the program).</summary>
    <Fact>
    Public Sub The_questions_that_stay_system_boxes_are_exactly_the_listed_ones()
        Dim callers As New List(Of String)()
        For Each path As String In ViewerSourceFiles()
            Dim name As String = System.IO.Path.GetFileName(path)
            If name = "ViewerBox.vb" Then Continue For
            Dim lines As String() = File.ReadAllLines(path)
            For i As Integer = 0 To lines.Length - 1
                If lines(i).TrimStart().StartsWith("'", StringComparison.Ordinal) Then Continue For
                If lines(i).Contains("ViewerBox.ShowSystem(") Then callers.Add(name)
            Next
        Next
        Assert.Equal(New String() {"Main_Form.Lifecycle.vb"}, callers.Distinct().OrderBy(Function(n) n, StringComparer.Ordinal).ToArray())
        Dim lifecycle As String = SourceText("Main_Form.Lifecycle.vb")
        Assert.Contains("stays the SYSTEM box on purpose", lifecycle)
    End Sub

End Class
#End If
