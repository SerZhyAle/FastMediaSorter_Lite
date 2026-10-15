#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.Windows.Forms

''' <summary>
''' The opt-in, local measurement behind WINDOWS-UI section 9 (amended 0.2): "time every settings page and every
''' secondary window from the user's action to the first complete frame". It is OFF unless the environment of the
''' process has <c>FMS_UI_TIMING=1</c>; switched on, it appends one line per shown page or window to the program's own
''' <c>current.log</c> (<see cref="UiTimingBudget.FormatLine"/>) and nothing else - no network, no file of its own,
''' no value or caption in the line. Switched off, the cost of a surface is one read of a static field.
'''
''' What "the first complete frame" is, here: the first <c>Application.Idle</c> after the surface has been shown
''' (for a window, after its <c>Shown</c>; for a settings page, after the tab changed - which posts the restore of
''' the page's remembered position before it). Idle is raised when the thread's message queue is empty, and a
''' window's paint is delivered before an empty queue is reported, so by then everything that was laid out has been
''' drawn and the surface is waiting for the person. The clock starts at the person's action: the call that opens
''' the window (<see cref="DialogLayer"/>, the settings window's entry) or the tab's <c>Selecting</c>. A surface that
''' keeps the queue busy - a timer storm, a long handler - is therefore charged for it, which is the point.
''' </summary>
Friend NotInheritable Class UiTiming

    Private Sub New()
    End Sub

    ''' <summary>The environment variable that switches the measurement on.</summary>
    Friend Const EnvironmentVariable As String = "FMS_UI_TIMING"

    ''' <summary>Read once, when the type is first touched.</summary>
    Friend Shared ReadOnly Enabled As Boolean = ReadSwitch()

    Private Shared Function ReadSwitch() As Boolean
        Try
            Return String.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>The moment of the person's action, or 0 when the measurement is off (callers test for 0).</summary>
    Friend Shared Function Begin() As Long
        If Not Enabled Then Return 0
        Return Stopwatch.GetTimestamp()
    End Function

    ''' <summary>Reports <paramref name="name"/> at the next idle moment, the time since <paramref name="startTicks"/>.
    ''' A start of 0 (the measurement was off when the action began) reports nothing.</summary>
    Friend Shared Sub CompleteAtIdle(name As String, kind As UiTimingKind, startTicks As Long)
        If Not Enabled OrElse startTicks = 0 Then Return
        ArmIdle(name, kind, startTicks)
    End Sub

    ''' <summary>Times a window from <paramref name="startTicks"/> to the first idle moment after it is shown.
    ''' The window is named by its type, which is the same in every language.</summary>
    Friend Shared Sub WatchWindow(window As Form, startTicks As Long)
        If Not Enabled OrElse startTicks = 0 OrElse window Is Nothing Then Return
        ArmShown(window, startTicks)
    End Sub

    ' The two methods below hold the closures: a lambda that captures a parameter allocates its closure when its
    ' method is entered, so they are only reached once the switch is known to be on.

    Private Shared Sub ArmIdle(name As String, kind As UiTimingKind, startTicks As Long)
        Dim handler As EventHandler = Nothing
        handler = Sub(sender As Object, e As EventArgs)
                      RemoveHandler Application.Idle, handler
                      Report(name, kind, ElapsedMs(startTicks))
                  End Sub
        AddHandler Application.Idle, handler
    End Sub

    Private Shared Sub ArmShown(window As Form, startTicks As Long)
        Dim name As String = window.GetType().Name
        Dim shown As EventHandler = Nothing
        shown = Sub(sender As Object, e As EventArgs)
                    RemoveHandler window.Shown, shown
                    ArmIdle(name, UiTimingKind.Dialog, startTicks)
                End Sub
        AddHandler window.Shown, shown
    End Sub

    Private Shared Function ElapsedMs(startTicks As Long) As Double
        Return (Stopwatch.GetTimestamp() - startTicks) * 1000.0R / Stopwatch.Frequency
    End Function

    Private Shared Sub Report(name As String, kind As UiTimingKind, elapsedMs As Double)
        Try
            AppFileLogger.WriteLine(UiTimingBudget.FormatLine(name, kind, elapsedMs))
        Catch
            ' A log line must never cost the person a window.
        End Try
    End Sub

End Class
#End If
