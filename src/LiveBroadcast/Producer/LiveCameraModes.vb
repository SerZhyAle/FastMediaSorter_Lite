#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic

' Ticket 199 section 4, phase B3: the camera's mode picker - the PURE half, so the suite drives it
' without a device (section 8). A camera answers a list of capture modes (Media Foundation through
' the WinRT projections in the Share Manager's source); which of them a broadcast uses is one
' judgement, and it lives here, not in the device code:
'   - the LARGEST mode that fits the MPEG-TS shape's 1280x720 wins, because the engine scales down
'     natively (LiveFfmpegArgs' -vf scale) and a mode above the cap buys bandwidth the viewer cannot
'     see;
'   - where every mode is above the cap, the SMALLEST one wins - the least scaling;
'   - equal areas tie-break on the higher frame rate (a static camera frame rate costs nothing, a
'     smoother one is a better live picture);
'   - impossible rows (zero or negative pixels or rate) are skipped, never crash the pick.

''' <summary>One capture mode the camera offers: the pixels and the frame rate. Index is the row of
''' the source's own list, so the stored pick stays stable across enumerations.</summary>
Friend Class LiveCameraMode
    Public Property Index As Integer
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property FramesPerSecond As Integer
End Class

Friend NotInheritable Class LiveCameraModes

    Private Sub New()
    End Sub

    ''' <summary>The cap of the video shape (item C): the encoder's own numbers, 1280x720.</summary>
    Public Const CapWidth As Integer = 1280
    Public Const CapHeight As Integer = 720

    ''' <summary>The best mode of the list, or Nothing when the list carries nothing usable.</summary>
    Public Shared Function PickBest(modes As IEnumerable(Of LiveCameraMode)) As LiveCameraMode
        If modes Is Nothing Then Return Nothing
        Dim usable As New List(Of LiveCameraMode)()
        For Each m As LiveCameraMode In modes
            If IsUsable(m) Then usable.Add(m)
        Next
        If usable.Count = 0 Then Return Nothing

        Dim best As LiveCameraMode = usable(0)
        For i As Integer = 1 To usable.Count - 1
            If BetterThan(usable(i), best) Then best = usable(i)
        Next
        Return best
    End Function

    ''' <summary>A stored pick that no longer exists (a mode list that changed) falls back to the
    ''' automatic best - the same clamp the screen source applies to a remembered monitor.</summary>
    Public Shared Function PickAt(modes As IEnumerable(Of LiveCameraMode), index As Integer) As LiveCameraMode
        If modes Is Nothing Then Return Nothing
        If index < 0 Then Return PickBest(modes)
        Dim i As Integer = -1
        For Each m As LiveCameraMode In modes
            i += 1
            If i = index Then Return If(IsUsable(m), m, Nothing)
        Next
        Return Nothing
    End Function

    Private Shared Function IsUsable(m As LiveCameraMode) As Boolean
        Return m IsNot Nothing AndAlso m.Width > 0 AndAlso m.Height > 0 AndAlso m.FramesPerSecond > 0
    End Function

    Private Shared Function BetterThan(candidate As LiveCameraMode, current As LiveCameraMode) As Boolean
        Dim candidateArea As Long = CLng(candidate.Width) * CLng(candidate.Height)
        Dim currentArea As Long = CLng(current.Width) * CLng(current.Height)
        Dim capArea As Long = CLng(CapWidth) * CLng(CapHeight)

        Dim candidateFits As Boolean = candidateArea <= capArea
        Dim currentFits As Boolean = currentArea <= capArea
        If candidateFits <> currentFits Then Return candidateFits

        If candidateArea <> currentArea Then
            ' Both fit: the larger picture wins. Both over: the smaller one scales the least.
            If candidateFits Then Return candidateArea > currentArea
            Return candidateArea < currentArea
        End If
        Return candidate.FramesPerSecond > current.FramesPerSecond
    End Function

End Class
#End If
