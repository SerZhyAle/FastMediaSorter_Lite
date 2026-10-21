Option Strict On

Imports System.Windows.Forms

''' <summary>
''' The Share Manager's question box (ticket 243 T-12b, BUTTONS 2.0 section 1: a system dialog must not be used solely to
''' evade a required product-owned component). A Yes/No, OK/Cancel or Yes/No/Cancel QUESTION is asked through the
''' product-owned question window - the viewer's <c>Ask_Dialog</c>, linked into this program - instead of a system
''' <c>MessageBox</c>: the answers read cancel-first in the interface language's direction, the window follows the
''' application theme and the language's reading direction, the call site's default answer stays the default, and Escape
''' and the close box answer the cancel answer (Cancel of OK/Cancel and Yes/No/Cancel, No of Yes/No).
''' The translation between the two vocabularies is <see cref="ViewerBoxMapping"/> (the viewer's own, pure, held by its
''' tests); this class only shows the window. The call shapes are those of <c>MessageBox.Show</c>, so a call site changes
''' by its class name alone.
'''
''' What stays the system box, on purpose: <c>OK</c> alone (a one-line notice), <c>RetryCancel</c> and
''' <c>AbortRetryIgnore</c> (none is a question set; no call site of this program uses the last two). A source scan
''' (<c>ManagerBoxTests</c>) fails on a question set passed to the stock <c>MessageBox.Show</c> anywhere in this program,
''' so a new one cannot creep back.
'''
''' A window placed in the topmost band (the broadcast window) keeps its question above it: the question is made topmost
''' too, the rule the viewer's dialog layer applies to every window it opens. A question with no owner at all (the tray's
''' "another program asks to open a folder") is centred on the screen, topmost, and has a taskbar button, as the system
''' box without an owner had.
''' </summary>
Friend NotInheritable Class ManagerBox

    Private Sub New()
    End Sub

    ''' <summary>A question of the three question sets, or the stock system box for any other set.</summary>
    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(owner, text, caption, buttons, icon, defaultButton)
        Return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon) As DialogResult
        Return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1)
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
        Return Show(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1)
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        Return Show(Nothing, text, caption, buttons, icon, defaultButton)
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon) As DialogResult
        Return Show(Nothing, text, caption, buttons, icon, MessageBoxDefaultButton.Button1)
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
        Return Show(Nothing, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1)
    End Function

    ''' <summary>The question through the product-owned window, answered as the DialogResult the caller already tests
    ''' for. A caller on another thread than its owner's gets the window made on the owner's thread (a window has one
    ''' thread) and waits for the answer there - the same wait the system box imposed.</summary>
    Private Shared Function Ask(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        Dim spec As AskSpec = ViewerBoxMapping.SpecFor(text, caption, buttons, icon, defaultButton)
        If spec Is Nothing Then Return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
        Dim parent As IWin32Window = ViewerBoxMapping.UsableOwner(owner)
        Dim thread As Control = TryCast(parent, Control)
        If thread IsNot Nothing AndAlso thread.IsHandleCreated AndAlso thread.InvokeRequired Then
            Return CType(thread.Invoke(New Func(Of DialogResult)(
                             Function() ViewerBoxMapping.AnswerOf(buttons, Choose(parent, spec)))), DialogResult)
        End If
        Return ViewerBoxMapping.AnswerOf(buttons, Choose(parent, spec))
    End Function

    ''' <summary>Shows the question window modally and returns the index of the answer (Escape and the close box answer
    ''' with the spec's cancel index).</summary>
    Friend Shared Function Choose(owner As IWin32Window, spec As AskSpec) As Integer
        Using dialog As New Ask_Dialog(spec)
            Prepare(dialog, owner)
            If owner Is Nothing Then dialog.ShowDialog() Else dialog.ShowDialog(owner)
            Return If(dialog.Answer >= 0, dialog.Answer, AskRules.ClosingAnswer(spec))
        End Using
    End Function

    ''' <summary>The topmost band, the place and the look of the window right before it is shown.</summary>
    Private Shared Sub Prepare(dialog As Ask_Dialog, owner As IWin32Window)
        If owner Is Nothing Then
            ' No window to be over: the screen's middle, above the rest, reachable from the taskbar (the tray asks this way).
            dialog.StartPosition = FormStartPosition.CenterScreen
            dialog.ShowInTaskbar = True
            dialog.TopMost = True
        ElseIf InTopmostBand(owner) Then
            dialog.TopMost = True
        End If
        ThemeService.Attach(dialog)
    End Sub

    ''' <summary>True when the owner, or a window above it in the owner chain, is pinned topmost: a window created
    ''' afterwards is born in the normal band and would open BEHIND it.</summary>
    Friend Shared Function InTopmostBand(owner As IWin32Window) As Boolean
        Try
            Dim control As Control = TryCast(owner, Control)
            Dim f As Form = If(control Is Nothing, Nothing, If(TypeOf control Is Form, DirectCast(control, Form), control.FindForm()))
            Dim guard As Integer = 0
            While f IsNot Nothing AndAlso guard < 16
                If f.TopMost Then Return True
                f = f.Owner
                guard += 1
            End While
        Catch
        End Try
        Return False
    End Function

End Class
