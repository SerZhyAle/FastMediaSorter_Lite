Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports Xunit

''' <summary>
''' APP-ACTIVATION 0.13 (the 2026-10-17 consolidation): "test a hung receiver". The real transport loop
''' (<see cref="CopyDataTransport.RunForward"/>) sends a real WM_COPYDATA with SMTO_ABORTIFHUNG, the flags both senders
''' (the viewer's TryForwardOnce, the Share Manager's SendOnce) use, to a real window whose thread pumps no messages.
''' Each send must cost its bound and no more, the forward must end at the budget, and the outcome must never be
''' "delivered" - so the caller opens its own argument and the target is not lost. A pumping receiver that answers 1 is
''' the positive control: without it a harness that never delivers would pass the first test for the wrong reason.
''' The senders' own process and window lookups are not exercised here (they need a running viewer or manager).
''' </summary>
Public Class HungReceiverForwardTests

    Private Const WM_COPYDATA As Integer = &H4A
    Private Const SMTO_ABORTIFHUNG As Integer = &H2

    <StructLayout(LayoutKind.Sequential)>
    Private Structure CopyDataBlock
        Public dwData As IntPtr
        Public cbData As Integer
        Public lpData As IntPtr
    End Structure

    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", SetLastError:=True)>
    Private Shared Function SendMessageTimeoutW(hWnd As IntPtr, msg As Integer, wParam As IntPtr, ByRef lParam As CopyDataBlock,
                                                flags As Integer, timeout As Integer, ByRef result As IntPtr) As IntPtr
    End Function

    ''' <summary>A window that answers a tagged WM_COPYDATA with the handled result, as the viewer's Main_Form does.</summary>
    Private NotInheritable Class AnsweringWindow
        Inherits NativeWindow

        Protected Overrides Sub WndProc(ByRef m As Message)
            If m.Msg = WM_COPYDATA Then
                m.Result = New IntPtr(SingleInstanceProtocol.Handled_Result)
                Return
            End If
            MyBase.WndProc(m)
        End Sub
    End Class

    ''' <summary>A receiver on a thread of its own. Hung: the thread creates the window and then blocks in a plain wait, so no
    ''' sent message is ever dispatched (an MTA thread - an STA wait would pump them). Answering: the thread pumps.</summary>
    Private NotInheritable Class Receiver
        Implements IDisposable

        Private ReadOnly ready As New ManualResetEventSlim(False)
        Private ReadOnly release As New ManualResetEventSlim(False)
        Private ReadOnly worker As Thread
        Public Handle As IntPtr

        Public Sub New(pumps As Boolean)
            worker = New Thread(Sub() Run(pumps)) With {.IsBackground = True, .Name = "L0334 receiver"}
            worker.Start()
            If Not ready.Wait(5000) Then Throw New TimeoutException("the receiver window was not created")
        End Sub

        Private Sub Run(pumps As Boolean)
            Dim window As New AnsweringWindow()
            window.CreateHandle(New CreateParams With {.Caption = "fms-L0334-receiver"})
            Handle = window.Handle
            ready.Set()
            If pumps Then
                Do Until release.IsSet
                    Application.DoEvents()
                    Thread.Sleep(2)
                Loop
            Else
                release.Wait()
            End If
            window.DestroyHandle()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            release.Set()
            worker.Join(5000)
            ready.Dispose()
            release.Dispose()
        End Sub
    End Class

    ''' <summary>One real send, the shape of the senders': the show-window sentinel in the tagged COPYDATASTRUCT, bounded by
    ''' <paramref name="timeoutMs"/> and SMTO_ABORTIFHUNG. Records how long the call took.</summary>
    Private Shared Function SendOnce(hwnd As IntPtr, timeoutMs As Integer, log As List(Of Tuple(Of Integer, Long))) As ForwardAttempt
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.True(CopyDataTransport.TryEncode("::fms-show-window::", bytes, refusal))
        Dim memory As IntPtr = Marshal.AllocHGlobal(bytes.Length + 1)
        Try
            Marshal.Copy(bytes, 0, memory, bytes.Length)
            Marshal.WriteByte(memory, bytes.Length, 0)
            Dim block As New CopyDataBlock With {.dwData = SingleInstanceProtocol.Tag_Pointer, .cbData = bytes.Length, .lpData = memory}
            Dim answer As IntPtr = IntPtr.Zero
            Dim watch As Stopwatch = Stopwatch.StartNew()
            Dim sent As Boolean = SendMessageTimeoutW(hwnd, WM_COPYDATA, IntPtr.Zero, block, SMTO_ABORTIFHUNG, timeoutMs, answer) <> IntPtr.Zero
            watch.Stop()
            log.Add(Tuple.Create(timeoutMs, watch.ElapsedMilliseconds))
            Return If(SingleInstanceProtocol.IsDelivered(sent, answer), ForwardAttempt.Acknowledged, ForwardAttempt.NotAcknowledged)
        Finally
            Marshal.FreeHGlobal(memory)
        End Try
    End Function

    <Fact>
    Public Sub A_receiver_that_pumps_no_messages_costs_each_send_its_bound_and_the_forward_ends_at_the_budget()
        Using receiver As New Receiver(pumps:=False)
            Dim log As New List(Of Tuple(Of Integer, Long))()
            Dim total As Stopwatch = Stopwatch.StartNew()
            Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(
                Function() total.ElapsedMilliseconds,
                Sub(pauseMs) Thread.Sleep(pauseMs),
                Function(timeoutMs) SendOnce(receiver.Handle, timeoutMs, log),
                totalMs:=900, perSendMs:=250, pauseMs:=50)
            total.Stop()

            ' Nobody took the argument: the caller sees "not delivered" and opens it itself - the target is not lost.
            Assert.Equal(ForwardOutcome.TimedOut, outcome)
            Assert.NotEqual(ForwardOutcome.Delivered, outcome)
            Assert.True(log.Count >= 2, "a silent receiver is retried inside the budget, got " & log.Count & " send(s)")
            For Each attempt As Tuple(Of Integer, Long) In log
                ' The send waited for its bound (it was not refused at once) and was not held past it.
                Assert.True(attempt.Item2 >= attempt.Item1 - 50, "send bound " & attempt.Item1 & " ms returned after " & attempt.Item2 & " ms: too early")
                Assert.True(attempt.Item2 <= attempt.Item1 + 250, "send bound " & attempt.Item1 & " ms returned after " & attempt.Item2 & " ms: unbounded")
            Next
            Assert.InRange(total.ElapsedMilliseconds, 850L, 1700L)
        End Using
    End Sub

    <Fact>
    Public Sub A_receiver_that_pumps_and_answers_is_delivered_on_the_first_send_well_inside_the_bound()
        Using receiver As New Receiver(pumps:=True)
            Dim log As New List(Of Tuple(Of Integer, Long))()
            Dim total As Stopwatch = Stopwatch.StartNew()
            Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(
                Function() total.ElapsedMilliseconds,
                Sub(pauseMs) Thread.Sleep(pauseMs),
                Function(timeoutMs) SendOnce(receiver.Handle, timeoutMs, log),
                totalMs:=900, perSendMs:=250, pauseMs:=50)

            Assert.Equal(ForwardOutcome.Delivered, outcome)
            Assert.Single(log)
            Assert.True(log(0).Item2 < 250, "an answering receiver took " & log(0).Item2 & " ms")
        End Using
    End Sub

End Class
