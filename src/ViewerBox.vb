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
''' Compiled by both builds and identical on net48: the fallback has no right-to-left language and gets no
''' implicit owner, so every call there is the stock call, byte for byte (the maintenance policy of CLAUDE.md).
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

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon, defaultButton As MessageBoxDefaultButton) As DialogResult
        Return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons,
                                icon As MessageBoxIcon) As DialogResult
        Return MessageBox.Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(owner As IWin32Window, text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
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
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, icon, defaultButton, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons, icon As MessageBoxIcon) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, icon, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String, buttons As MessageBoxButtons) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String, caption As String) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

    Friend Shared Function Show(text As String) As DialogResult
        Return MessageBox.Show(DefaultOwner(), text, String.Empty, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, Options())
    End Function

End Class
