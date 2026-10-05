#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The one way a window the viewer owns is opened (ticket 124, UX-201). Windows keeps an owned window above
''' its owner only inside one z-order band, and "Поверх всех окон" moves the viewer into the topmost band while
''' a window created afterwards is born in the normal one - so a bare ShowDialog(Me) leaves the container
''' password window, the resource windows, "О файле" and the rest BEHIND the pinned viewer, where a modal window
''' looks exactly like a hung program. The rule used to be re-learnt per window (editor, ToVideo, the thumbnail
''' panel); it lives here now, and <c>DialogLayerTests</c> reads the source and fails on a bare modal show.
'''
''' What a window gets on the way in is <see cref="Prepare"/>: the viewer's band, the viewer as its owner and its
''' monitor (CenterParent), the interface language's reading direction when the window did not choose one, and
''' the application theme before its first paint. The preparation names Main_Form and the theme service, which
''' the unit-test assembly does not link; so it is installed as a hook by the viewer
''' (<c>ViewerDialogPrep.Install</c>, DialogLayer.Viewer.vb) and, absent it, a window is simply shown with its
''' owner - which is exactly what the tests that open a dialog without a viewer need.
''' Whole-file modern: the net48 fallback has none of it.
''' </summary>
Friend NotInheritable Class DialogLayer

    Private Sub New()
    End Sub

    ''' <summary>What the viewer does to a window before it is shown. Installed once, at the viewer's start.</summary>
    Friend Shared Property Preparation As Action(Of Form, IWin32Window)

    ''' <summary>The viewer window that has to stay in front of modal windows, found from an owner; Nothing in the
    ''' test assembly and before the viewer exists. Installed with <see cref="Preparation"/>.</summary>
    Friend Shared Property ViewerLocator As Func(Of IWin32Window, IWin32Window)

    ''' <summary>Joins a window to the viewer's band and look, right before it is shown.</summary>
    Friend Shared Sub Prepare(window As Form, owner As IWin32Window)
        If window Is Nothing OrElse window.IsDisposed Then Return
        Dim hook As Action(Of Form, IWin32Window) = Preparation
        If hook Is Nothing Then Return
        Try
            hook(window, owner)
        Catch ex As Exception
            ' Decoration and placement: a window that cannot be prepared is still shown.
            AppFileLogger.LogException("DialogLayer prepare", ex)
        End Try
    End Sub

    Private Shared Function OwnerOrViewer(owner As IWin32Window) As IWin32Window
        If owner IsNot Nothing Then Return owner
        Dim locate As Func(Of IWin32Window, IWin32Window) = ViewerLocator
        Return If(locate Is Nothing, Nothing, locate(Nothing))
    End Function

    ''' <summary>Shows a modal window through the viewer's layer.</summary>
    Friend Shared Function ShowViewerDialog(dialog As Form, owner As IWin32Window) As DialogResult
        Dim parent As IWin32Window = OwnerOrViewer(owner)
        Prepare(dialog, parent)
        If parent Is Nothing Then Return dialog.ShowDialog()
        Return dialog.ShowDialog(parent)
    End Function

    ''' <summary>Shows a modeless window through the viewer's layer (the file details).</summary>
    Friend Shared Sub ShowViewerWindow(window As Form, owner As IWin32Window)
        Dim parent As IWin32Window = OwnerOrViewer(owner)
        Prepare(window, parent)
        If parent Is Nothing Then window.Show() Else window.Show(parent)
    End Sub

End Class

''' <summary>The viewer's questions: named buttons and validated text, in the application theme, in the
''' viewer's band, in the interface language's reading direction (ticket 124 section 3.2).</summary>
Friend NotInheritable Class AskDialogs

    Private Sub New()
    End Sub

    ''' <summary>Asks a question with named buttons; returns the index of the answer (Escape and the close box
    ''' answer with the spec's cancel index).</summary>
    Friend Shared Function Choose(owner As IWin32Window, spec As AskSpec) As Integer
        Using dialog As New Ask_Dialog(spec)
            DialogLayer.ShowViewerDialog(dialog, owner)
            Return If(dialog.Answer >= 0, dialog.Answer, AskRules.ClosingAnswer(spec))
        End Using
    End Function

    ''' <summary>Asks for one line of text. Returns the text, or Nothing when the user cancelled - an empty
    ''' answer is not a cancel (the spec's validator decides whether an empty text is acceptable).</summary>
    Friend Shared Function Prompt(owner As IWin32Window, spec As PromptSpec) As String
        Using dialog As New Ask_Dialog(spec)
            DialogLayer.ShowViewerDialog(dialog, owner)
            Return If(dialog.Answer = 0, dialog.TypedText, Nothing)
        End Using
    End Function

    ''' <summary>The replacement for a bare "OK" system box: one button, Escape and Enter both close it. Only for
    ''' a notice the user has to acknowledge; news that needs no answer goes to the status line instead.</summary>
    Friend Shared Sub Tell(owner As IWin32Window, title As String, message As String, icon As AskIcon)
        Dim spec As New AskSpec(title, message, icon,
                                New System.Collections.Generic.List(Of AskButton) From {New AskButton(Localization.T("Закрыть"))},
                                defaultIndex:=0, cancelIndex:=-1)
        Choose(owner, spec)
    End Sub

End Class
#End If
