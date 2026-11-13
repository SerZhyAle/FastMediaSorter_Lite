#If Not NETFRAMEWORK Then
Option Strict On
Option Explicit On

Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.Globalization
Imports System.Threading

''' <summary>
''' The opt-in cost measurement of the panels' colour step (L0303 measurement M-4 (b)): how long the viewer spends
''' recolouring its panels when the picture's colour changes, and whether that step re-laid-out a panel. It is OFF
''' unless the process environment has <c>FMS_UI_TIMING=1</c> (the switch of <see cref="UiTiming"/>); switched off,
''' a colour step costs one read of a static field and the layout hook one more.
'''
''' Switched on, each measured step yields one line <c>Panel colour timing: &lt;ms&gt; ms | layout=&lt;true|false&gt;</c>
''' (invariant decimal) in the program's own <c>current.log</c>, the format read by
''' <c>tests/Integration/PanelColourTimingStats.ps1</c>. The line is not written on the UI thread: the step only puts
''' it on a bounded queue and a pool thread hands it to <see cref="AppFileLogger"/>, whose flush would otherwise be
''' charged to the next picture. Past <see cref="QueueLimit"/> waiting lines a sample is dropped, never blocked on.
''' </summary>
Friend NotInheritable Class PanelColourTiming

    Private Sub New()
    End Sub

    ''' <summary>The most lines that may wait for the writer; a burst beyond it drops samples.</summary>
    Friend Const QueueLimit As Integer = 1024

    Private Shared ReadOnly pending As New ConcurrentQueue(Of String)()
    Private Shared pendingCount As Integer
    Private Shared draining As Integer
    Private Shared layoutCount As Integer

    ''' <summary>A chrome surface laid its items out (called from <c>ChromeSurface.LayoutItems</c>).</summary>
    Friend Shared Sub NoteLayout()
        If UiTiming.Enabled Then Interlocked.Increment(layoutCount)
    End Sub

    ''' <summary>Starts one colour step: the timestamp, or 0 when the measurement is off.</summary>
    Friend Shared Function Begin(ByRef layoutsAtStart As Integer) As Long
        If Not UiTiming.Enabled Then Return 0
        layoutsAtStart = Volatile.Read(layoutCount)
        Return Stopwatch.GetTimestamp()
    End Function

    ''' <summary>Ends the step begun at <paramref name="startTicks"/>; a start of 0 reports nothing.</summary>
    Friend Shared Sub Complete(startTicks As Long, layoutsAtStart As Integer)
        If startTicks = 0 Then Return
        Dim elapsedMs As Double = (Stopwatch.GetTimestamp() - startTicks) * 1000.0R / Stopwatch.Frequency
        Dim laidOut As Boolean = Volatile.Read(layoutCount) <> layoutsAtStart
        Enqueue(FormatLine(elapsedMs, laidOut))
    End Sub

    ''' <summary>The line of one step - the contract of the statistics reader.</summary>
    Friend Shared Function FormatLine(elapsedMs As Double, laidOut As Boolean) As String
        Dim value As Double = If(Double.IsNaN(elapsedMs) OrElse elapsedMs < 0, 0.0R, elapsedMs)
        Return "Panel colour timing: " & value.ToString("0.000", CultureInfo.InvariantCulture) &
               " ms | layout=" & If(laidOut, "true", "false")
    End Function

    Private Shared Sub Enqueue(line As String)
        If Interlocked.Increment(pendingCount) > QueueLimit Then
            Interlocked.Decrement(pendingCount)
            Return
        End If
        pending.Enqueue(line)
        If Interlocked.CompareExchange(draining, 1, 0) = 0 Then
            ThreadPool.QueueUserWorkItem(AddressOf Drain)
        End If
    End Sub

    Private Shared Sub Drain(state As Object)
        Do
            Dim line As String = Nothing
            While pending.TryDequeue(line)
                Interlocked.Decrement(pendingCount)
                Try
                    AppFileLogger.WriteLine(line)
                Catch
                    ' A measurement line must never cost the person a picture.
                End Try
            End While
            Volatile.Write(draining, 0)
            ' A line queued after the loop emptied but before the flag dropped would wait for the next step: take it now.
            If pending.IsEmpty OrElse Interlocked.CompareExchange(draining, 1, 0) <> 0 Then Exit Do
        Loop
    End Sub

End Class
#End If
