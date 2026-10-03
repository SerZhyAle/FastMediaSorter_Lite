Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports FastMediaSorterCompanion
Imports Xunit

Public Class ActivationWindowDispatchTests

    <DllImport("user32.dll", EntryPoint:="SendMessageTimeoutW", ExactSpelling:=True)>
    Private Shared Function SendMessageTimeout(hwnd As IntPtr, message As Integer, sender As IntPtr,
                                               ByRef data As Program.COPYDATASTRUCT, flags As UInteger,
                                               timeout As UInteger, ByRef result As IntPtr) As IntPtr
    End Function

    Private Shared Function Send(hwnd As IntPtr, tag As IntPtr, payload As Byte(), Optional byteCount As Integer = -1) As Integer
        Dim pointer As IntPtr = Marshal.AllocHGlobal(payload.Length)
        Try
            Marshal.Copy(payload, 0, pointer, payload.Length)
            Dim data As New Program.COPYDATASTRUCT With {.dwData = tag, .cbData = If(byteCount < 0, payload.Length, byteCount), .lpData = pointer}
            Dim result As IntPtr
            Dim transport As IntPtr = SendMessageTimeout(hwnd, Program.WM_COPYDATA, IntPtr.Zero, data, 2, 3000, result)
            Return If(transport = IntPtr.Zero, 0, result.ToInt32())
        Finally
            Marshal.FreeHGlobal(pointer)
        End Try
    End Function

    <Fact>
    Public Sub The_actual_window_acknowledges_owned_proposals_and_refuses_invalid_full_or_destroyed_delivery()
        Dim failure As Exception = Nothing
        Dim worker As New Thread(
            Sub()
                Dim window As TrayContext.MessageWindow = Nothing
                Try
                    window = New TrayContext.MessageWindow()
                    Dim queue As New FolderProposalQueue(Function() 0L)
                    window.StageFolder = Function(command) queue.Offer(command.Folder, Nothing) <> ProposalVerdict.Full
                    Dim dispatched As Integer
                    AddHandler window.CommandAccepted, Sub(command) dispatched += 1
                    Dim tag As New IntPtr(CopyDataTransport.CompanionTag)
                    Dim folder As Byte() = Encoding.UTF8.GetBytes("D:\fixture")
                    Assert.Equal(1, Send(window.Handle, tag, folder))
                    Assert.Equal(1, Send(window.Handle, IntPtr.Zero, folder)) ' Legacy goes to the same proposal queue.
                    Assert.Equal(1, queue.PendingCount)
                    Assert.Equal(2, dispatched)
                    Assert.Equal(0, Send(window.Handle, New IntPtr(123), folder))
                    Assert.Equal(0, Send(window.Handle, tag, New Byte() {&HC0, &HAF}))
                    Assert.Equal(0, Send(window.Handle, tag, folder, 65537))
                    Assert.Equal(2, dispatched)
                    For i As Integer = 1 To 31
                        queue.Offer("D:\fixture" & i.ToString(), Nothing)
                    Next
                    Assert.Equal(0, Send(window.Handle, tag, Encoding.UTF8.GetBytes("D:\overflow")))
                    Assert.Equal(2, dispatched)
                    Dim oldHandle As IntPtr = window.Handle
                    window.DestroyHandle()
                    Assert.Equal(0, Send(oldHandle, tag, folder))
                    Assert.Equal(2, dispatched)
                Catch ex As Exception
                    failure = ex
                Finally
                    If window IsNot Nothing AndAlso window.Handle <> IntPtr.Zero Then window.DestroyHandle()
                End Try
            End Sub)
        worker.SetApartmentState(ApartmentState.STA)
        worker.IsBackground = True
        worker.Start()
        Assert.True(worker.Join(15000), "The window dispatch test did not terminate.")
        If failure IsNot Nothing Then Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
