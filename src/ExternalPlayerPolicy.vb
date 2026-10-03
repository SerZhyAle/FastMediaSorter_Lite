Option Strict On

Imports System.Collections.Generic
Imports System.IO

''' <summary>
''' Whether a video that LibVLC could not open may be handed to the shell's default program
''' (ticket 090, audit finding 057.S08-1).
'''
''' The hand-over is `Process.Start` with `UseShellExecute`, i.e. "whatever Windows has
''' registered for this extension". When this viewer IS that program - Settings "Files and
''' system" registers it as the default video player, and "Open with - Always" does the
''' same - the shell starts this exe with the failed path, single-instance forwarding hands
''' it to the running window, VLC fails again, and the loop repeats: navigation is dragged
''' back to the broken file every round and the folder list is replaced by that one file.
'''
''' The rule is pure - the registered handler and the set of paths already handed over are
''' its inputs - so "the fallback cannot come back here" is a test, not a promise. The half
''' that asks Windows for the handler stays in Main_Form.VideoPlayer.vb. Shared by both
''' runtimes: the x86 fallback takes this as a bug fix, not a feature.
''' </summary>
Public NotInheritable Class ExternalPlayerPolicy

    Private Sub New()
    End Sub

    Public Enum Verdict
        ''' <summary>Nothing says the shell would come back here. Hand the file over.</summary>
        Allow
        ''' <summary>The extension's registered handler is one of our own exes.</summary>
        HandlerIsViewer
        ''' <summary>This path was already handed over automatically in this session and
        ''' the file is still failing - the guard for handlers the query cannot resolve,
        ''' such as a Store app alias.</summary>
        AlreadyTried
    End Enum

    ''' <summary>
    ''' True when <paramref name="handlerExecutable"/> names one of the viewer exes. Compared
    ''' by file name without extension, case-insensitively, against the process names -
    ''' the same list the single-instance forwarding searches - so a renamed exe is one edit.
    ''' A blank handler (nothing registered, or the query failed) is never "ours".
    ''' </summary>
    Public Shared Function IsViewerExecutable(handlerExecutable As String,
                                              viewerProcessNames As IEnumerable(Of String)) As Boolean
        If String.IsNullOrWhiteSpace(handlerExecutable) OrElse viewerProcessNames Is Nothing Then Return False

        Dim name As String
        Try
            name = Path.GetFileNameWithoutExtension(handlerExecutable.Trim().Trim(""""c))
        Catch ex As ArgumentException
            Return False
        End Try
        If name.Length = 0 Then Return False

        For Each viewer As String In viewerProcessNames
            If String.Equals(name, viewer, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' The decision. <paramref name="automatic"/> is False for the explicit "Open in
    ''' external player" menu item: the user asked, so the once-per-session guard does not
    ''' apply to it - but the handler check does, since a hand-over that only reopens this
    ''' viewer is no external player at all.
    ''' </summary>
    Public Shared Function Decide(path As String,
                                  handlerExecutable As String,
                                  viewerProcessNames As IEnumerable(Of String),
                                  alreadyHandedOver As ISet(Of String),
                                  automatic As Boolean) As Verdict
        If IsViewerExecutable(handlerExecutable, viewerProcessNames) Then Return Verdict.HandlerIsViewer
        If automatic AndAlso alreadyHandedOver IsNot Nothing AndAlso alreadyHandedOver.Contains(path) Then
            Return Verdict.AlreadyTried
        End If
        Return Verdict.Allow
    End Function

End Class
