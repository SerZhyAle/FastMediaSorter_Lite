Option Strict On

Imports System.Collections.Generic
Imports System.Windows.Forms

''' <summary>
''' The pure halves of the viewer's input rules from ticket 047: which key presses the
''' historical key map may answer at all (C-10), which of them destroy or move a file and
''' so must not auto-repeat (C-20), how a high-resolution wheel turns into file flips
''' (C-21), and when a second click on the same recipients-overlay row is the same
''' gesture (C-20). Shared by both viewers and free of any window, so each rule is a test.
''' </summary>
Public Module KeyRouting

    ''' <summary>
    ''' May the historical key map answer this combination? Not when Ctrl or Alt is held:
    ''' that map was written for bare keys and Shift, and it never looked at the other two
    ''' modifiers - so Alt+F4 opened the file chooser (and, being handled, kept the window
    ''' open), Alt+Space flipped a file instead of opening the system menu, Ctrl+1 moved a
    ''' file and Alt+D deleted one (audit 047, C-10). Returning False leaves the key to
    ''' Windows and to the focused control, exactly as if the viewer had no key map. A
    ''' custom shortcut the user bound to such a combination is asked BEFORE this.
    ''' </summary>
    Public Function MainMapAccepts(keyData As Keys) As Boolean
        If (keyData And Keys.Control) = Keys.Control Then Return False
        If (keyData And Keys.Alt) = Keys.Alt Then Return False
        Return True
    End Function

    ''' <summary>
    ''' A key whose action moves, copies or deletes the file on screen: a recipient digit
    ''' (top row or NumPad, with or without Shift) or DEL / D. Holding one of these used to
    ''' repeat it - every auto-repeat KeyDown that arrived after the previous operation had
    ''' finished took the NEXT file, so one long press sorted five to fifteen files nobody
    ''' had looked at (C-20). Owner decision 2026-09-25 (045 §8): one press is
    ''' one operation for keys that move, copy or delete; navigation keeps its auto-repeat.
    ''' </summary>
    Public Function IsDestructiveKey(keyCode As Keys) As Boolean
        Select Case keyCode
            Case Keys.D0 To Keys.D9, Keys.NumPad0 To Keys.NumPad9, Keys.Delete, Keys.D
                Return True
        End Select
        Return False
    End Function

    ''' <summary>
    ''' Is this WM_KEYDOWN an auto-repeat? Bit 30 of lParam is the key's previous state:
    ''' set when the key was already down before this message, which is exactly the
    ''' repeat and never the first press.
    ''' </summary>
    Public Function IsAutoRepeat(lParam As Long) As Boolean
        Return ((lParam >> 30) And 1L) = 1L
    End Function

End Module

''' <summary>
''' Turns wheel messages into file flips one WHEEL_DELTA (120) at a time (audit 047, C-21).
''' A precision touchpad or a free-spinning wheel sends many messages with |delta| below
''' 120, and each of them used to flip a whole file - one swipe went past about 25 files a
''' second, files a sorter has to look at. A classic wheel still flips on every notch: its
''' single message is already 120.
''' </summary>
Public NotInheritable Class WheelAccumulator

    Public Const Wheel_Delta As Integer = 120

    ''' <summary>A pause this long starts a new gesture: half a swipe left over from a
    ''' minute ago must not add itself to the next one.</summary>
    Public Const Idle_Reset_Ms As Long = 400

    Private accumulated As Integer
    Private last_Ms As Long = Long.MinValue

    ''' <summary>
    ''' Adds one wheel message and says what to do: -1 = one flip towards negative delta
    ''' (the next file), +1 = one flip towards positive delta (the previous file), 0 =
    ''' nothing yet. A change of direction drops what was collected the other way. At most
    ''' one flip per message, as before - a single large message is still one flip.
    ''' </summary>
    Public Function Add(delta As Integer, nowMs As Long) As Integer
        If delta = 0 Then Return 0

        If last_Ms = Long.MinValue OrElse nowMs - last_Ms > Idle_Reset_Ms OrElse
           (accumulated <> 0 AndAlso Math.Sign(accumulated) <> Math.Sign(delta)) Then
            accumulated = 0
        End If
        last_Ms = nowMs

        accumulated += delta
        If Math.Abs(accumulated) < Wheel_Delta Then Return 0

        Dim direction As Integer = Math.Sign(accumulated)
        ' Only the notch that was spent is taken out; a part-notch carries into the next
        ' message. More than a whole notch left over means one large message, which has
        ' always been one flip - it must not bank a run of flips for the messages after it.
        accumulated -= direction * Wheel_Delta
        If Math.Abs(accumulated) >= Wheel_Delta Then accumulated = 0
        Return direction
    End Function

End Class

''' <summary>
''' Drops the second Click of a double-click on the same recipients-overlay row (audit
''' 047, C-20). The rows are Buttons, and a habitual double-click on a Button is two
''' Clicks - two files moved into the folder the user meant to send one to.
''' </summary>
Public NotInheritable Class RepeatClickGuard

    Public Const Window_Ms As Long = 300

    Private ReadOnly last_Click As New Dictionary(Of Integer, Long)()

    ''' <summary>True when this click on <paramref name="target"/> is the tail of the
    ''' previous one and must be ignored. Records the click either way.</summary>
    Public Function IsRepeat(target As Integer, nowMs As Long) As Boolean
        Dim previous As Long
        Dim seen As Boolean = last_Click.TryGetValue(target, previous)
        last_Click(target) = nowMs
        Return seen AndAlso nowMs - previous >= 0 AndAlso nowMs - previous < Window_Ms
    End Function

End Class
