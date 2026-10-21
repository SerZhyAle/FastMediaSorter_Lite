#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Threading

' Spec 242, step T-6a: the decisions of the VIDEO SURFACE of the animated-graphics-through-video route,
' kept apart from the form so they are tested without one (the glue is Main_Form.AnimationVideo.vb).
' Nothing here touches LibVLC, WinForms, the registry or a log.

''' <summary>Where the video surface of the route is: nothing, opening (the first-frame picture is still
''' what the user sees), or shown (the video has started and is the surface on screen).</summary>
Friend Enum AnimationVideoPhase
    ''' <summary>No animation video is held.</summary>
    None
    ''' <summary>The media was handed to the player; the preview picture stays on screen until it plays (rule 21).</summary>
    Opening
    ''' <summary>The player reported <c>Playing</c>; the video is the surface on screen.</summary>
    Shown
End Enum

''' <summary>
''' The state of ONE animation video showing at a time (spec 242 rules 19, 21, 26-28), as plain data.
''' Every session gets a serial that never repeats and is never 0, so a late answer - a libvlc event posted
''' to the UI thread, a timeout, a stop that finishes after the user moved on - is recognised as another
''' session's and dropped, whatever the order it arrives in.
'''
''' Two readers exist: the UI thread, which owns every call but <see cref="EventSerial"/> and
''' <see cref="EventsDone"/>, and the libvlc event thread, which reads <see cref="EventSerial"/> only
''' (a volatile read of one Integer) to decide whether an event belongs to the animation branch.
''' </summary>
Friend NotInheritable Class AnimationVideoState

    Private phaseValue As AnimationVideoPhase = AnimationVideoPhase.None
    Private serialValue As Integer
    Private lastSerial As Integer
    Private generationValue As Integer = -1
    Private pathValue As String = ""
    Private eventSerialValue As Integer

    Friend ReadOnly Property Phase As AnimationVideoPhase
        Get
            Return phaseValue
        End Get
    End Property

    ''' <summary>The serial of the held session; 0 when none.</summary>
    Friend ReadOnly Property Serial As Integer
        Get
            Return serialValue
        End Get
    End Property

    ''' <summary>The <c>media_Generation</c> the session was started for; -1 when none.</summary>
    Friend ReadOnly Property Generation As Integer
        Get
            Return generationValue
        End Get
    End Property

    Friend ReadOnly Property FilePath As String
        Get
            Return pathValue
        End Get
    End Property

    ''' <summary>A session is held (opening or shown): the player or its media may be in use.</summary>
    Friend ReadOnly Property IsActive As Boolean
        Get
            Return phaseValue <> AnimationVideoPhase.None
        End Get
    End Property

    ''' <summary>The video is the surface on screen.</summary>
    Friend ReadOnly Property IsShown As Boolean
        Get
            Return phaseValue = AnimationVideoPhase.Shown
        End Get
    End Property

    ''' <summary>Starts a session for the showing of <paramref name="mediaGeneration"/> and returns its serial.
    ''' A session already held is replaced - the caller releases it first; its serial is no longer accepted.</summary>
    Friend Function Begin(mediaGeneration As Integer, path As String) As Integer
        lastSerial = If(lastSerial = Integer.MaxValue, 1, lastSerial + 1)
        serialValue = lastSerial
        generationValue = mediaGeneration
        pathValue = If(path, "")
        phaseValue = AnimationVideoPhase.Opening
        Return serialValue
    End Function

    ''' <summary>The answer of a session is acted on only while it is still the held one AND its showing is still
    ''' the one on screen.</summary>
    Friend Function Accepts(serial As Integer, mediaGeneration As Integer) As Boolean
        Return serial <> 0 AndAlso serial = serialValue AndAlso phaseValue <> AnimationVideoPhase.None AndAlso
               generationValue = mediaGeneration
    End Function

    ''' <summary>Opening to shown, once, for the held session of the showing on screen (rule 21).</summary>
    Friend Function TryReveal(serial As Integer, mediaGeneration As Integer) As Boolean
        If Not Accepts(serial, mediaGeneration) OrElse phaseValue <> AnimationVideoPhase.Opening Then Return False
        phaseValue = AnimationVideoPhase.Shown
        Return True
    End Function

    ''' <summary>Ends the held session when it is <paramref name="serial"/>; False (and nothing changes) for any other.</summary>
    Friend Function Finish(serial As Integer) As Boolean
        If serial = 0 OrElse serial <> serialValue Then Return False
        Clear()
        Return True
    End Function

    ''' <summary>Ends whatever is held and returns its serial (0 when nothing was).</summary>
    Friend Function FinishCurrent() As Integer
        Dim ended As Integer = serialValue
        Clear()
        Return ended
    End Function

    Private Sub Clear()
        phaseValue = AnimationVideoPhase.None
        serialValue = 0
        generationValue = -1
        pathValue = ""
    End Sub

    ' --- the libvlc event thread's half ------------------------------------------------------------------

    ''' <summary>The serial whose player events go to the animation branch, 0 when events are the ordinary
    ''' video's. Read on the libvlc thread.</summary>
    Friend ReadOnly Property EventSerial As Integer
        Get
            Return Volatile.Read(eventSerialValue)
        End Get
    End Property

    ''' <summary>Called right before the media is played: from here the player's events carry this serial. Only the held
    ''' session may arm; False for any other.</summary>
    Friend Function ArmEvents(serial As Integer) As Boolean
        If serial = 0 OrElse serial <> serialValue Then Return False
        Volatile.Write(eventSerialValue, serial)
        Return True
    End Function

    ''' <summary>Called once the native stop of <paramref name="serial"/>'s media has returned, so the player can
    ''' raise nothing more for it. Clears the routing only if it is still this serial's - a newer session that has
    ''' armed its own keeps it.</summary>
    Friend Sub EventsDone(serial As Integer)
        If serial = 0 Then Return
        Interlocked.CompareExchange(eventSerialValue, 0, serial)
    End Sub

End Class

''' <summary>The constants and yes/no questions of the video surface (spec 242 rules 21, 23, 26-28).</summary>
Friend Module AnimationVideoSurfaceRules

    ''' <summary>How long the first-frame preview waits for the video to report <c>Playing</c> before the route is
    ''' given up (rule 21).</summary>
    Friend Const Open_Timeout_Ms As Integer = 8000

    ''' <summary>The per-media options of the animation video (rule 26, E10): a native loop (the viewer never sees
    ''' an end), and no audio or subtitle output - an animation has neither. Everything else is an ordinary video's
    ''' (transport bar, pause, click), driven by the shared video path. <c>input-repeat=65535</c> is the same number
    ''' the viewer's own repeat uses.</summary>
    Friend ReadOnly Property PlayerOptions As String()
        Get
            Return New String() {":input-repeat=65535", ":no-audio", ":no-spu"}
        End Get
    End Property

    ' Why a session failed, as the log line of rule 23 (<c>Animation video failed: {reason}</c>) and the caller's
    ' fall-back say it. A reason is a code, never a message: it carries no path and no address.

    ''' <summary>LibVLC could not be started.</summary>
    Friend Const Reason_Vlc_Unavailable As String = "vlc-unavailable"

    ''' <summary>The player or the media could not be created or started.</summary>
    Friend Const Reason_Open As String = "vlc-open"

    ''' <summary>The player reported an error for this media.</summary>
    Friend Const Reason_Player_Error As String = "vlc"

    ''' <summary>No <c>Playing</c> within <see cref="Open_Timeout_Ms"/>.</summary>
    Friend Const Reason_Timeout As String = "timeout"

    ''' <summary>There was no picture on screen to hold while the video opens.</summary>
    Friend Const Reason_No_Preview As String = "no-preview"

    ''' <summary>The shared player is a real video's, a stream's or a live broadcast's: it is never taken over.</summary>
    Friend Const Reason_Busy As String = "vlc-busy"

    ''' <summary>
    ''' Is a call still wanted after it waited? It is while the form lives, the showing it was made for is still
    ''' the one on screen, and the picture it was to replace is still on that screen (the same three conditions as
    ''' spec 241's swap: generation, the preview's identity, a living form).
    ''' </summary>
    Friend Function IsStillWanted(callGeneration As Integer, currentGeneration As Integer,
                                  previewStillOnScreen As Boolean, formAlive As Boolean) As Boolean
        Return formAlive AndAlso previewStillOnScreen AndAlso callGeneration = currentGeneration
    End Function

End Module
#End If
