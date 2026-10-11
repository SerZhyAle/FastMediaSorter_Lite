#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The question window of the dialog layer (ticket 124 section 3.2): a message, an optional block of detail,
''' an optional one-line text field with its hint and its refusal, and NAMED buttons from the localization
''' layer. It replaces <c>Interaction.InputBox</c> and the Yes/No/Cancel system boxes, which carry the
''' system's button language, ignore the interface language's reading direction and are not themed.
'''
''' It knows nothing about the viewer: <see cref="AskDialogs"/> (DialogLayer.vb) opens it through the viewer's
''' band. The decisions - which button is the default, what Escape answers, whether a typed text is acceptable -
''' are the pure ones of AskModel.vb.
'''
''' Laid out with tables so a long caption wraps and the window grows, at any DPI and in every language;
''' reading direction is the form's RightToLeft only (never RightToLeftLayout - the localization rules), which
''' mirrors the table and the button row by itself.
''' </summary>
Friend NotInheritable Class Ask_Dialog
    Inherits Form

    ''' <summary>The width of the text column, in 96-dpi pixels; every label wraps inside it.</summary>
    Private Const ContentWidth As Integer = 400

    Private ReadOnly spec As AskSpec
    Private ReadOnly prompt As PromptSpec
    Private ReadOnly answerButtons As New List(Of Button)()
    Private ReadOnly field As TextBox
    Private problemLabel As Label
    Private script_Font As Font

    ''' <summary>The index of the button that was pressed, or the cancel answer; -1 while the window is open.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Answer As Integer = -1

    ''' <summary>For a prompt: the text typed. Only meaningful when <see cref="Answer"/> is 0.</summary>
    Friend ReadOnly Property TypedText As String
        Get
            Return If(field Is Nothing, String.Empty, field.Text)
        End Get
    End Property

    ''' <summary>The ink of the refusal line. The caller sets it from the application theme before the window is
    ''' shown; the default is readable on a light surface.</summary>
    <System.ComponentModel.Browsable(False), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property ProblemInk As Color = Color.Firebrick

    ''' <summary>A question with named buttons.</summary>
    Friend Sub New(question As AskSpec)
        spec = question
        Build(question.Title, question.Body, question.Detail, String.Empty, question.Icon, question.Buttons, question.DefaultIndex, question.CancelIndex, Nothing)
    End Sub

    ''' <summary>A one-line text question.</summary>
    Friend Sub New(question As PromptSpec)
        prompt = question
        field = New TextBox()
        Dim buttons As New List(Of AskButton) From {
            New AskButton(question.OkCaption, role:=ButtonGlyphRole.Affirmative),
            New AskButton(Localization.T("Отмена"), role:=ButtonGlyphRole.Dismissing)}
        Build(question.Title, question.Body, String.Empty, question.Hint, AskIcon.None, buttons, 0, 1, field)
        field.Text = question.InitialText
    End Sub

    Private Sub Build(title As String, message As String, detail As String, hint As String, icon As AskIcon,
                      buttons As IReadOnlyList(Of AskButton), defaultIndex As Integer, cancelIndex As Integer, input As TextBox)
        Text = title
        AccessibleName = title
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        KeyPreview = True

        Dim family As String = Localization.FontFamily()
        If family IsNot Nothing Then
            script_Font = New Font(family, Me.Font.Size)
            Font = script_Font
        End If
        If Localization.IsRightToLeft() Then RightToLeft = RightToLeft.Yes

        Dim root As New TableLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2,
            .Padding = New Padding(14),
            .Location = Point.Empty}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim row As Integer = 0
        Dim hasIcon As Boolean = icon <> AskIcon.None
        If hasIcon Then
            Dim pic As New PictureBox With {.SizeMode = PictureBoxSizeMode.Zoom, .Size = New Size(32, 32),
                                            .Margin = New Padding(0, 0, 12, 0), .TabStop = False, .AccessibleRole = AccessibleRole.Graphic,
                                            .Image = IconImage(icon)}
            root.Controls.Add(pic, 0, 0)
        End If

        Dim messageLabel As Label = WrappedLabel(message, New Padding(0, 0, 0, 10))
        messageLabel.Name = "ask_Message"
        AddRow(root, messageLabel, row)
        row += 1

        If detail.Length > 0 Then
            Dim detailLabel As Label = WrappedLabel(detail, New Padding(0, 0, 0, 10))
            detailLabel.Name = "ask_Detail"
            detailLabel.ForeColor = SystemColors.GrayText
            AddRow(root, detailLabel, row)
            row += 1
        End If

        If input IsNot Nothing Then
            input.Name = "ask_Field"
            input.Width = ContentWidth
            input.Margin = New Padding(0, 0, 0, 4)
            input.AccessibleName = message
            AddHandler input.TextChanged, Sub() If problemLabel IsNot Nothing Then problemLabel.Text = String.Empty
            AddRow(root, input, row)
            row += 1
        End If

        If hint.Length > 0 Then
            Dim hintLabel As Label = WrappedLabel(hint, New Padding(0, 0, 0, 6))
            hintLabel.Name = "ask_Hint"
            hintLabel.ForeColor = SystemColors.GrayText
            AddRow(root, hintLabel, row)
            row += 1
        End If

        If input IsNot Nothing Then
            problemLabel = WrappedLabel(String.Empty, New Padding(0, 0, 0, 6))
            problemLabel.Name = "ask_Problem"
            problemLabel.ForeColor = ProblemInk
            AddRow(root, problemLabel, row)
            row += 1
        End If

        Dim captions As New List(Of String)()
        For Each b As AskButton In buttons
            captions.Add(b.Caption)
        Next
        Dim marked As String() = AskRules.WithMnemonics(captions)
        Dim strip As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = False,
            .FlowDirection = FlowDirection.RightToLeft,
            .Anchor = AnchorStyles.Right,
            .Margin = New Padding(0, 6, 0, 0)}
        For i As Integer = 0 To buttons.Count - 1
            Dim index As Integer = i
            Dim b As New Button With {
                .Name = "ask_Button" & i.ToString(Globalization.CultureInfo.InvariantCulture),
                .Text = marked(i),
                .UseMnemonic = True,
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .MinimumSize = New Size(86, 28),
                .Margin = New Padding(8, 0, 0, 0),
                .Padding = New Padding(6, 0, 6, 0)}
            AddHandler b.Click, Sub() OnAnswer(index)
            answerButtons.Add(b)

            ' The glyph before the caption (WINDOWS-UI 0.4 section 11): the answer's own verb, in the hue of
            ' its polarity. The cancel answer of a question that has other answers is the dismissing one even
            ' when its spec did not say so. Escape, Enter and the default focus are decided below, untouched.
            Dim answer As AskButton = buttons(i)
            Dim role As ButtonGlyphRole = answer.Role
            Dim glyph As String = answer.GlyphId
            If role = ButtonGlyphRole.Neutral AndAlso glyph.Length = 0 AndAlso i = cancelIndex AndAlso buttons.Count > 1 Then
                role = ButtonGlyphRole.Dismissing
            End If
            If glyph.Length = 0 Then glyph = ButtonGlyphs.DefaultGlyph(role)
            If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Next
        ' FlowDirection.RightToLeft places the first control added at the far right: add them last-to-first so
        ' the first answer reads first, and the cancel answer sits at the end.
        For i As Integer = answerButtons.Count - 1 To 0 Step -1
            strip.Controls.Add(answerButtons(i))
        Next
        root.Controls.Add(strip, 0, row)
        root.SetColumnSpan(strip, 2)

        Controls.Add(root)

        If defaultIndex >= 0 AndAlso defaultIndex < answerButtons.Count Then AcceptButton = answerButtons(defaultIndex)
        Dim closing As Integer = If(cancelIndex >= 0, cancelIndex, If(buttons.Count = 1, 0, -1))
        If closing >= 0 AndAlso closing < answerButtons.Count Then CancelButton = answerButtons(closing)
        closingAnswer = closing
        initialIndex = defaultIndex

        ' LAST line of the builder: scaling touches only the children present now.
        DialogLayout.ApplyAutoScale(Me)
    End Sub

    Private closingAnswer As Integer
    Private initialIndex As Integer

    ''' <summary>Everything but the icon and the button row lives in the second column; the icon (if any) sits in
    ''' the first column of the first row.</summary>
    Private Shared Sub AddRow(root As TableLayoutPanel, c As Control, row As Integer)
        root.Controls.Add(c, 1, row)
    End Sub

    Private Shared Function WrappedLabel(text As String, margin As Padding) As Label
        Return New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .UseMnemonic = False,
            .Text = text,
            .Margin = margin}
    End Function

    Private Shared Function IconImage(icon As AskIcon) As Image
        Try
            Select Case icon
                Case AskIcon.Warning : Return SystemIcons.Warning.ToBitmap()
                Case AskIcon.Error : Return SystemIcons.Error.ToBitmap()
                Case AskIcon.Info : Return SystemIcons.Information.ToBitmap()
                Case AskIcon.Question : Return SystemIcons.Question.ToBitmap()
            End Select
        Catch
        End Try
        Return Nothing
    End Function

    Private Sub OnAnswer(index As Integer)
        If prompt IsNot Nothing AndAlso index = 0 Then
            Dim outcome As PromptOutcome = AskRules.Evaluate(prompt, field.Text)
            If Not outcome.Accepted Then
                problemLabel.ForeColor = ProblemInk
                problemLabel.Text = outcome.Problem
                field.Focus()
                field.SelectAll()
                Return
            End If
        End If
        Answer = index
        DialogResult = If(index = closingAnswer, DialogResult.Cancel, DialogResult.OK)
        Close()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' A long caption in a long language: the labels already wrap (MaximumSize), the form grows with the
        ' table. Only the cap to the monitor is left to do.
        DialogLayout.ClampToWorkingArea(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        If field IsNot Nothing Then
            field.Focus()
            If prompt.SelectAll Then field.SelectAll() Else field.SelectionStart = field.TextLength
        ElseIf initialIndex >= 0 AndAlso initialIndex < answerButtons.Count Then
            answerButtons(initialIndex).Focus()
        End If
    End Sub

    ''' <summary>The close box answers like Escape: the cancel answer (or the only button).</summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If Answer < 0 Then
            Answer = closingAnswer
            If DialogResult = DialogResult.None Then DialogResult = DialogResult.Cancel
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        MyBase.Dispose(disposing)
        If disposing Then script_Font?.Dispose()
    End Sub

End Class
#End If
