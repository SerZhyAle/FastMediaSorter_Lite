Option Strict On

Imports System.Windows.Forms

' The "keep on top" z-order band, in one place.
'
' WHY THIS FILE EXISTS: Windows keeps an owned window above its owner only INSIDE
' one z-order band. "Поверх всех окон" moves the viewer into the topmost band,
' while a window created afterwards is born in the normal band - so Show(Me) /
' ShowDialog(Me) is not enough, and the settings window (or any other window the
' viewer opens) waits behind the pinned viewer where nobody can see it. On a modal
' one that looks exactly like the app froze. The same trap has now been walked into
' from three different call sites, so the rule lives here instead of being
' rediscovered per window:
'
'   * every window the viewer opens joins the viewer's band right before it is
'     shown (PinToViewerBand),
'   * toggling the checkbox re-applies the band to whatever is already open
'     (SyncPinnedWindowsWithViewer),
'   * and the whole set is dropped together while an external window - installer,
'     mail editor, browser - has to be visible (SuspendAlwaysOnTop / Resume).
'
' Native dialogs (MessageBox, the file/folder pickers) cannot carry the flag; they
' get an explicit owner instead, which is what puts them above their owner.
Partial Public Class Main_Form

    ''' <summary>Puts a window the viewer opens into the viewer's z-order band.
    ''' Call it right before Show/ShowDialog - a window shown first and pinned later
    ''' still flashes behind the viewer for a frame.</summary>
    Friend Sub PinToViewerBand(child As Form)
        If child Is Nothing OrElse child.IsDisposed Then Return
        Try
            If child.TopMost <> Me.TopMost Then child.TopMost = Me.TopMost
        Catch ex As Exception
            AppFileLogger.LogException("Main_Form pin child window", ex)
        End Try
    End Sub

    ''' <summary>Mirrors the viewer's band onto every window already open, so
    ''' switching "поверх всех окон" on or off also moves the open settings /
    ''' thumbnail window instead of leaving it in the wrong band.</summary>
    Friend Sub SyncPinnedWindowsWithViewer()
        Try
            ' Setting TopMost only calls SetWindowPos - it neither recreates the
            ' handle nor touches OpenForms, so iterating it directly is safe.
            For Each opened As Form In Application.OpenForms
                If opened IsNot Me Then PinToViewerBand(opened)
            Next
        Catch ex As Exception
            AppFileLogger.LogException("Main_Form sync pinned windows", ex)
        End Try
    End Sub

    ''' <summary>Drops the pinned band on the viewer AND on every window it opened,
    ''' so something we launched outside the app is not covered by it. Returns True
    ''' when there was anything to drop - feed that value back to
    ''' <see cref="ResumeAlwaysOnTop"/>. The checkbox itself is left alone: this is a
    ''' temporary suspension, not a change of the user's setting.</summary>
    Friend Function SuspendAlwaysOnTop() As Boolean
        If Not Me.TopMost Then Return False
        Me.TopMost = False
        SyncPinnedWindowsWithViewer()
        Return True
    End Function

    ''' <summary>Restores what <see cref="SuspendAlwaysOnTop"/> dropped. It restores
    ''' the checkbox, not the snapshot: the user may have switched "поверх всех окон"
    ''' off while the external window was up, and that choice outranks ours.</summary>
    Friend Sub ResumeAlwaysOnTop(wasPinned As Boolean)
        If Not wasPinned Then Return
        Me.TopMost = chkbox_Top_Most.Checked
        SyncPinnedWindowsWithViewer()
    End Sub

End Class
