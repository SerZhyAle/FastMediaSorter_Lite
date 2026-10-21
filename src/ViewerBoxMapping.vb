#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Windows.Forms

' The pure half of "a viewer question is a product-owned window" (ticket 243 T-12a, BUTTONS 2.0 section 1: a
' system dialog must not be used solely to evade a required product-owned component). A system message box keeps
' the system's button order and rendering; a Yes/No/OK-Cancel QUESTION of the viewer is therefore asked through
' Ask_Dialog, and this file is the whole translation between the two vocabularies:
' MessageBoxButtons + MessageBoxIcon + MessageBoxDefaultButton in, an AskSpec out; the index of the pressed answer
' in, the DialogResult the caller already tests for out. ViewerBox.vb does the calling, Ask_Dialog.vb the drawing.
'
' Only the three question sets are routed. OK alone, RetryCancel and AbortRetryIgnore stay the system box (a one-line
' notice, an OS-level failure with its own verbs): no AskSpec is built for them.
'
' Component policy (BUTTONS 2.0 section 2 rule 1, recorded here as the one place): the plain answers OK, Yes and No
' are TEXT-ONLY and untinted - simple obvious answers need no decorative glyph - while the dismissing answer
' (Cancel, and No) keeps the neutral dismissing role, which is what Ask_Dialog draws with the cross at the reading
' start like every other cancel answer of the dialog layer (no tint since T-1).
'
' Whole-file "#If Not NETFRAMEWORK": the x86 fallback keeps its stock system boxes (maintenance policy).

''' <summary>MessageBox vocabulary to AskSpec and back. Pure: nothing here shows a window.</summary>
Friend NotInheritable Class ViewerBoxMapping

    Private Sub New()
    End Sub

    ''' <summary>True for the three button sets that are questions and go to the dialog layer.</summary>
    Friend Shared Function IsQuestion(buttons As MessageBoxButtons) As Boolean
        Return buttons = MessageBoxButtons.OKCancel OrElse buttons = MessageBoxButtons.YesNo OrElse buttons = MessageBoxButtons.YesNoCancel
    End Function

    ''' <summary>The answers of a question set in SPEC order - the affirmative one first, exactly as every spec of
    ''' AskSpecs.vb lists them (the window shows them the other way round, cancel first). Empty for a set that is not
    ''' routed.</summary>
    Friend Shared Function AnswersOf(buttons As MessageBoxButtons) As DialogResult()
        Select Case buttons
            Case MessageBoxButtons.OKCancel : Return New DialogResult() {DialogResult.OK, DialogResult.Cancel}
            Case MessageBoxButtons.YesNo : Return New DialogResult() {DialogResult.Yes, DialogResult.No}
            Case MessageBoxButtons.YesNoCancel : Return New DialogResult() {DialogResult.Yes, DialogResult.No, DialogResult.Cancel}
            Case Else : Return New DialogResult() {}
        End Select
    End Function

    ''' <summary>The index Escape and the close box answer with: Cancel of OK/Cancel and Yes/No/Cancel, No of
    ''' Yes/No. -1 for a set that is not routed.</summary>
    Friend Shared Function CancelIndexOf(buttons As MessageBoxButtons) As Integer
        Return AnswersOf(buttons).Length - 1
    End Function

    ''' <summary>The spec index of the default answer. <c>Button1/2/3</c> name the buttons in the WINDOWS order of the
    ''' set (OK, Cancel / Yes, No / Yes, No, Cancel), which is the spec order, so the index is the same number; a slot
    ''' the set does not have (Button3 of Yes/No, the help slot Button4) falls back to the first button, as the system
    ''' box does. The call sites' defaults are kept as they are - which answer is the default is not decided here.</summary>
    Friend Shared Function DefaultIndexOf(buttons As MessageBoxButtons, defaultButton As MessageBoxDefaultButton) As Integer
        Dim index As Integer
        Select Case defaultButton
            Case MessageBoxDefaultButton.Button2 : index = 1
            Case MessageBoxDefaultButton.Button3 : index = 2
            Case Else : index = 0
        End Select
        If index >= AnswersOf(buttons).Length Then index = 0
        Return index
    End Function

    ''' <summary>The localized caption of an answer. The keys are the viewer's own registered ones.</summary>
    Friend Shared Function CaptionOf(answer As DialogResult) As String
        Select Case answer
            Case DialogResult.Yes : Return Localization.T("Да")
            Case DialogResult.No : Return Localization.T("Нет")
            Case DialogResult.OK : Return Localization.T("ОК")
            Case Else : Return Localization.T("Отмена")
        End Select
    End Function

    ''' <summary>The picture of the question. The system's aliases share values (Exclamation = Warning, Stop = Hand =
    ''' Error, Asterisk = Information), so each picture is named once.</summary>
    Friend Shared Function IconOf(icon As MessageBoxIcon) As AskIcon
        Select Case icon
            Case MessageBoxIcon.Question : Return AskIcon.Question
            Case MessageBoxIcon.Warning : Return AskIcon.Warning
            Case MessageBoxIcon.Error : Return AskIcon.Error
            Case MessageBoxIcon.Information : Return AskIcon.Info
            Case Else : Return AskIcon.None
        End Select
    End Function

    ''' <summary>The question as the dialog layer asks it, or Nothing for a button set that stays a system box. The
    ''' caption is the title (an empty one is the product name, as the system box shows the exe's name); the text is
    ''' the body. Yes, OK and the other plain answers are text-only; Cancel and No are the dismissing role.</summary>
    Friend Shared Function SpecFor(text As String, caption As String, buttons As MessageBoxButtons,
                                   icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As AskSpec
        If Not IsQuestion(buttons) Then Return Nothing
        Dim answers As DialogResult() = AnswersOf(buttons)
        Dim list As New List(Of AskButton)()
        For Each answer As DialogResult In answers
            If answer = DialogResult.No OrElse answer = DialogResult.Cancel Then
                list.Add(New AskButton(CaptionOf(answer), role:=ButtonGlyphRole.Dismissing))
            Else
                list.Add(New AskButton(CaptionOf(answer)))
            End If
        Next
        Dim title As String = If(String.IsNullOrEmpty(caption), ProductNames.Full, caption)
        Return New AskSpec(title, text, IconOf(icon), list, DefaultIndexOf(buttons, defaultButton), CancelIndexOf(buttons))
    End Function

    ''' <summary>The DialogResult the caller tests for, from the index the question window answered. An index outside
    ''' the set (never expected: the window answers the cancel index for Escape and the close box) is the cancel answer,
    ''' the one that does nothing.</summary>
    Friend Shared Function AnswerOf(buttons As MessageBoxButtons, index As Integer) As DialogResult
        Dim answers As DialogResult() = AnswersOf(buttons)
        If answers.Length = 0 Then Return DialogResult.None
        If index < 0 OrElse index >= answers.Length Then Return answers(answers.Length - 1)
        Return answers(index)
    End Function

    ''' <summary>The owner a question is shown over, or Nothing when the given one cannot be read - a window that is
    ''' already disposed, or one whose handle cannot be asked for - so the dialog layer falls back to the viewer
    ''' instead of throwing in the middle of a person's action.</summary>
    Friend Shared Function UsableOwner(owner As IWin32Window) As IWin32Window
        If owner Is Nothing Then Return Nothing
        Try
            Dim window As Control = TryCast(owner, Control)
            If window IsNot Nothing Then
                If window.IsDisposed OrElse window.Disposing Then Return Nothing
                Return owner
            End If
            If owner.Handle = IntPtr.Zero Then Return Nothing
            Return owner
        Catch
            Return Nothing
        End Try
    End Function

End Class
#End If
