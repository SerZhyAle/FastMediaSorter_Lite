#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' The pure half of the question surface (ticket 124 section 3.2, UX-204/UX-205): what a question
' says, which answers it offers by NAME, and which one is the default. The window that draws it is
' Ask_Dialog.vb; the call sites' own questions are in AskSpecs.vb. Neither knows the other's business.
'
' Whole-file "#If Not NETFRAMEWORK" - the net48 fallback keeps its system boxes.

''' <summary>The picture beside a question's text. The system icon sets are not used: the dialog is drawn in
''' the application theme, and a Windows 11 sound-and-icon pair would be the one unthemed thing on it.</summary>
Friend Enum AskIcon
    None = 0
    Question = 1
    Warning = 2
    [Error] = 3
    Info = 4
End Enum

''' <summary>One named answer. <see cref="Destructive"/> marks an answer that overwrites or removes
''' something - such an answer is never the default (<see cref="AskRules.DefaultIsSafe"/>).</summary>
Friend NotInheritable Class AskButton
    Friend ReadOnly Caption As String
    Friend ReadOnly Destructive As Boolean
    ''' <summary>The polarity of the answer, which decides the hue of its glyph (WINDOWS-UI section 11 item 4).</summary>
    Friend ReadOnly Role As ButtonGlyphRole
    ''' <summary>The vocabulary id of the verb's own meaning; empty takes the bare answer's glyph for its role.</summary>
    Friend ReadOnly GlyphId As String

    Friend Sub New(caption As String, Optional destructive As Boolean = False,
                   Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral, Optional glyphId As String = "")
        Me.Caption = If(caption, String.Empty)
        Me.Destructive = destructive
        Me.Role = role
        Me.GlyphId = If(glyphId, String.Empty)
    End Sub
End Class

''' <summary>A question with named buttons. Index based: the caller receives the INDEX of the pressed button,
''' or <see cref="CancelIndex"/> for Escape and the close box - never a system DialogResult whose "No" can be
''' mistaken for "Cancel".</summary>
Friend NotInheritable Class AskSpec
    Friend ReadOnly Title As String
    Friend ReadOnly Body As String
    ''' <summary>An optional block of detail under the message (the two files of a name collision); may hold
    ''' several lines.</summary>
    Friend ReadOnly Detail As String
    Friend ReadOnly Icon As AskIcon
    Friend ReadOnly Buttons As IReadOnlyList(Of AskButton)
    Friend ReadOnly DefaultIndex As Integer
    ''' <summary>The answer Escape and the close box give. -1 only for a single-button notice.</summary>
    Friend ReadOnly CancelIndex As Integer

    Friend Sub New(title As String, message As String, icon As AskIcon, buttons As IReadOnlyList(Of AskButton),
                   defaultIndex As Integer, cancelIndex As Integer, Optional detail As String = "")
        Me.Title = If(title, String.Empty)
        Me.Body = If(message, String.Empty)
        Me.Icon = icon
        Me.Buttons = buttons
        Me.DefaultIndex = defaultIndex
        Me.CancelIndex = cancelIndex
        Me.Detail = If(detail, String.Empty)
    End Sub
End Class

''' <summary>A one-line text question (rename, a file number, an address): the typed text is checked while the
''' dialog is still up, so a refused answer keeps what was typed and says why beside the field.</summary>
Friend NotInheritable Class PromptSpec
    Friend Property IsPassword As Boolean
    Friend ReadOnly Title As String
    Friend ReadOnly Body As String
    Friend ReadOnly InitialText As String
    ''' <summary>Examples or a format hint shown under the field, in a dimmer ink; may be empty.</summary>
    Friend ReadOnly Hint As String
    Friend ReadOnly OkCaption As String
    ''' <summary>Returns the reason a text is refused, or Nothing / empty when it is acceptable.</summary>
    Friend ReadOnly Validator As Func(Of String, String)
    ''' <summary>True for a name: the part before the extension is selected, not the whole text.</summary>
    Friend ReadOnly SelectAll As Boolean

    Friend Sub New(title As String, message As String, initialText As String, okCaption As String,
                   validator As Func(Of String, String), Optional hint As String = "", Optional selectAll As Boolean = True)
        Me.Title = If(title, String.Empty)
        Me.Body = If(message, String.Empty)
        Me.InitialText = If(initialText, String.Empty)
        Me.OkCaption = If(okCaption, String.Empty)
        Me.Validator = validator
        Me.Hint = If(hint, String.Empty)
        Me.SelectAll = selectAll
    End Sub
End Class

''' <summary>What a prompt does with a typed text.</summary>
Friend Structure PromptOutcome
    ''' <summary>True when the text is acceptable and the dialog closes with it.</summary>
    Friend ReadOnly Accepted As Boolean
    ''' <summary>The reason shown beside the field when it is not (empty when accepted).</summary>
    Friend ReadOnly Problem As String

    Friend Sub New(accepted As Boolean, problem As String)
        Me.Accepted = accepted
        Me.Problem = If(problem, String.Empty)
    End Sub
End Structure

Friend NotInheritable Class AskRules

    Private Sub New()
    End Sub

    ''' <summary>Everything wrong with a spec as a list of sentences (empty = well formed): a default or a cancel
    ''' answer that does not exist, no buttons, an empty caption. A test walks every spec the product builds.</summary>
    Friend Shared Function Problems(spec As AskSpec) As IReadOnlyList(Of String)
        Dim found As New List(Of String)()
        If spec Is Nothing Then
            found.Add("no spec")
            Return found
        End If
        If spec.Buttons Is Nothing OrElse spec.Buttons.Count = 0 Then
            found.Add("no buttons")
            Return found
        End If
        If spec.DefaultIndex < 0 OrElse spec.DefaultIndex >= spec.Buttons.Count Then found.Add("default button out of range")
        If spec.Buttons.Count > 1 AndAlso (spec.CancelIndex < 0 OrElse spec.CancelIndex >= spec.Buttons.Count) Then
            found.Add("a question needs a cancel answer")
        End If
        For Each b As AskButton In spec.Buttons
            If b Is Nothing OrElse b.Caption.Length = 0 Then found.Add("a button without a caption")
        Next
        Return found
    End Function

    ''' <summary>The default button is never one that overwrites or removes something: sorting is a run of key
    ''' presses, and the Space or Enter already typed for the next picture lands on this window (audit 046 C-01).</summary>
    Friend Shared Function DefaultIsSafe(spec As AskSpec) As Boolean
        If spec Is Nothing OrElse spec.Buttons Is Nothing OrElse spec.DefaultIndex < 0 OrElse spec.DefaultIndex >= spec.Buttons.Count Then Return False
        Return Not spec.Buttons(spec.DefaultIndex).Destructive
    End Function

    ''' <summary>The captions with an "&" put before the first letter or digit no earlier caption took, so every
    ''' answer has an Alt accelerator (the system box offered one; replacing a file takes a click or Alt+letter,
    ''' never the Enter that was typed for the next picture). A caption with nothing usable is left as it is.</summary>
    Friend Shared Function WithMnemonics(captions As IReadOnlyList(Of String)) As String()
        Dim used As New HashSet(Of Char)()
        Dim result(captions.Count - 1) As String
        For i As Integer = 0 To captions.Count - 1
            Dim caption As String = If(captions(i), String.Empty)
            result(i) = caption.Replace("&", "&&")
            For k As Integer = 0 To caption.Length - 1
                Dim ch As Char = caption(k)
                If Not Char.IsLetterOrDigit(ch) Then Continue For
                If used.Add(Char.ToUpperInvariant(ch)) Then
                    result(i) = caption.Substring(0, k).Replace("&", "&&") & "&" & caption.Substring(k).Replace("&", "&&")
                    Exit For
                End If
            Next
        Next
        Return result
    End Function

    ''' <summary>The index a close request answers with: the cancel answer, or the only button of a notice.</summary>
    Friend Shared Function ClosingAnswer(spec As AskSpec) As Integer
        If spec.CancelIndex >= 0 Then Return spec.CancelIndex
        Return If(spec.Buttons.Count = 1, 0, -1)
    End Function

    ''' <summary>Runs a prompt's validator on a typed text. No validator accepts everything. A validator that throws
    ''' refuses the text with a generic reason: a question must never silently pass a text its own check could
    ''' not judge.</summary>
    Friend Shared Function Evaluate(spec As PromptSpec, text As String) As PromptOutcome
        If spec.Validator Is Nothing Then Return New PromptOutcome(True, String.Empty)
        Dim reason As String
        Try
            reason = spec.Validator(If(text, String.Empty))
        Catch
            reason = Localization.T("Не удалось проверить введённый текст")
        End Try
        If String.IsNullOrEmpty(reason) Then Return New PromptOutcome(True, String.Empty)
        Return New PromptOutcome(False, reason)
    End Function

End Class
#End If
