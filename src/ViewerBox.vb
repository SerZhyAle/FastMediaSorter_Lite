Option Strict On

Imports System.Windows.Forms

''' <summary>
''' The viewer's system message box (ticket 124, UX-205): <c>MessageBox.Show</c> with the two things the stock call
''' leaves out. In a right-to-left interface language the box is read and aligned right to left
''' (<c>RtlReading</c> + <c>RightAlign</c> - no call anywhere in the tree passed them, so Arabic and Urdu got a
''' left-to-right box), and a box asked for with no owner gets the viewer as its owner on the mainline, which is
''' what puts a native box above a viewer pinned "on top" (Main_Form.WindowPinning.vb: a native dialog cannot carry
''' the topmost flag, only an owner).
'''
''' It is a question surface for what must stay a SYSTEM box (an OS-level failure, a one-line OK). Questions that
''' name their answers go through <c>AskDialogs</c> instead, and news goes to <c>Notify</c>.
'''
''' Since ticket 243 T-12a the mainline also routes every QUESTION asked here - <c>OKCancel</c>, <c>YesNo</c>,
''' <c>YesNoCancel</c> - to the product-owned question window (BUTTONS 2.0 section 1: a system dialog must not be used
''' solely to evade a required product-owned component): the answers read cancel-first in the interface language's
''' direction, Escape and the close box answer the cancel answer, the call site's default answer is kept. The
''' translation is <see cref="ViewerBoxMapping"/>. <c>OK</c> alone, <c>RetryCancel</c> and <c>AbortRetryIgnore</c> stay
''' the system box, and so does a question that asks for it by <see cref="ShowSystem"/>.
'''
''' Compiled by both builds and identical on net48: the fallback has no right-to-left language, gets no implicit
''' owner and routes nothing (every new path is behind <c>#If Not NETFRAMEWORK</c>), so every call there is the stock
''' call, byte for byte (the maintenance policy of CLAUDE.md).
''' </summary>
Friend NotInheritable Class ViewerBox

    Private Sub New()
    End Sub

    ''' <summary>The extra options of the stock call: none, except right-to-left reading in an RTL language.</summary>
    Friend Shared Function Options() As MessageBoxOptions
        Return OptionsFor(Localization.IsRightToLeft())
    End Function

    ''' <summary>The pure rule: a right-to-left language reads and aligns the box right to left; any other adds nothing.</summary>
    Friend Shared Function OptionsFor(rightToLeft As Boolean) As MessageBoxOptions
        If rightToLeft Then Return MessageBoxOptions.RtlReading Or MessageBoxOptions.RightAlign
        Return CType(0, MessageBoxOptions)
    End Function

    ''' <summary>The owner a box asked for without one gets: the viewer on the mainline, nothing on net48.</summary>
    Private Shared Function DefaultOwner() As IWin32Window
#If Not NETFRAMEWORK Then
        Try
            ' The window of this application that is active - the F3 panel, the settings window, the editor - is
            ' what the stock call used to find by itself; naming it keeps a box over the window that asked.
            Dim active As Form = Form.ActiveForm
            If active IsNot Nothing AndAlso Not active.IsDisposed Then Return active
            ' Otherwise the viewer, through the dialog layer's own locator (installed by the viewer; absent in the
            ' unit-test assembly).
            Dim locate As Func(Of IWin32Window, IWin32Window) = DialogLayer.ViewerLocator
            If locate IsNot Nothing Then Return locate(Nothing)
        Catch
        End Try
#End If
        Return Nothing
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>A question of the three question sets, asked through the product-owned window and answered as the
    ''' DialogResult the caller already tests for. A caller on another thread than its owner's gets the window made on
    ''' the owner's thread (a window has one thread), and waits for the answer there - the same wait the system box
    ''' imposed.</summary>
    Private Shared Function Ask(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        Dim spec As AskSpec = ViewerBoxMapping.SpecFor(text, caption, buttons, icon, defaultButton)
        If spec Is Nothing Then Return ShowSystem(owner, text, caption, buttons, icon, defaultButton)
        Dim parent As IWin32Window = ViewerBoxMapping.UsableOwner(owner)
        Dim thread As Control = TryCast(parent, Control)
        If thread IsNot Nothing AndAlso thread.IsHandleCreated AndAlso thread.InvokeRequired Then
            Return CType(thread.Invoke(New Func(Of DialogResult)(
                             Function() ViewerBoxMapping.AnswerOf(buttons, AskDialogs.Choose(parent, spec)))), DialogResult)
        End If
        Return ViewerBoxMapping.AnswerOf(buttons, AskDialogs.Choose(parent, spec))
    End Function
#End If

    ''' <summary>The stock system box with this class's options (right-to-left reading), never the product-owned
    ''' question window: for a question whose Escape or close box must not answer the way the question window's would
    ''' (the close-time prompt, where "No" closes the program and unfinished file operations are dropped), and for the
    ''' sets that are not questions. A caller that uses it says why at the call.</summary>
    Friend Shared Function ShowSystem(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                      icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        Return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(owner, text, caption, buttons, icon, defaultButton)
#End If
        Return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1)
#End If
        Return MessageBox.Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1)
#End If
        Return MessageBox.Show(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String) As DialogResult
        Return MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String) As DialogResult
        Return MessageBox.Show(owner, text, String.Empty, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(DefaultOwner(), text, caption, buttons, icon, defaultButton)
#End If
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, icon, defaultButton, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(DefaultOwner(), text, caption, buttons, icon, MessageBoxDefaultButton.Button1)
#End If
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, icon, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
#If Not NETFRAMEWORK Then
        If ViewerBoxMapping.IsQuestion(buttons) Then Return Ask(DefaultOwner(), text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1)
#End If
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, String.Empty, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

End Class
