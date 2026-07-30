Option Strict On

Imports System.Diagnostics
Imports System.IO.Pipes
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The end-to-end timeout on the worker pipe (SPECIFICATION_LONG_RUN_STABILITY.md C-6).
'''
''' WorkerIpc.Send used to bound only the CONNECT. The write and the "read until the worker
''' closes its end" loop were unbounded blocking calls, so a worker that accepted the pipe and
''' then never answered - deadlocked, suspended by an AV, stuck on disk I/O - blocked the call
''' for ever. Every caller wraps Send in Task.Run, so each such call permanently pinned a
''' thread-pool thread plus a pipe handle, and the two status pollers added one every 10-30 s
''' for as long as Companion sat in the tray: thousands within days. Meanwhile every awaiting
''' UI flow never resumed, leaving the window stuck in its busy state.
'''
''' The parameter was even NAMED timeoutMs and passed through as the connect timeout, so the
''' code read as if it were bounded when it was not. These tests hold it to its name.
''' </summary>
Public Class WorkerIpcTimeoutTests

    ''' <summary>Generous enough that a slow CI machine does not fail it, tight enough that an
    ''' unbounded read would blow straight past it.</summary>
    Private Const Timeout_Ms As Integer = 1500

    <Fact>
    Public Async Function Send_gives_up_when_no_worker_is_listening() As Task
        ' Nothing on the pipe at all: the connect cannot succeed, and the call must come back
        ' rather than sit there.
        Dim watch As Stopwatch = Stopwatch.StartNew()
        Dim threw As Boolean = False
        Try
            Await Task.Run(Sub() WorkerIpc.Send(New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms))
        Catch
            threw = True
        End Try
        watch.Stop()

        Assert.True(threw, "Send must report a transport failure, not return a fabricated response")
        Assert.True(watch.ElapsedMilliseconds < Timeout_Ms * 6,
                    "Send took " & watch.ElapsedMilliseconds.ToString() & " ms with nothing listening")
    End Function

    <Fact>
    Public Async Function Send_gives_up_on_a_worker_that_accepts_and_then_says_nothing() As Task
        ' The case that used to hang for ever: the connection SUCCEEDS, so the old connect-only
        ' timeout was already satisfied, and then the server neither replies nor closes.
        Using accepted As New ManualResetEventSlim(False)
            Using release As New ManualResetEventSlim(False)
                Dim server As Task = Task.Run(Sub()
                                                  Try
                                                      Using pipe As New NamedPipeServerStream(
                                                          WorkerIpc.PipeName, PipeDirection.InOut, 1)
                                                          pipe.WaitForConnection()
                                                          accepted.Set()
                                                          ' Hold the connection open, in silence,
                                                          ' until the test lets go.
                                                          release.Wait(TimeSpan.FromSeconds(30))
                                                      End Using
                                                  Catch
                                                      accepted.Set()
                                                  End Try
                                              End Sub)

                Dim watch As Stopwatch = Stopwatch.StartNew()
                Dim threw As Boolean = False
                Try
                    Await Task.Run(Sub() WorkerIpc.Send(New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms))
                Catch
                    threw = True
                End Try
                watch.Stop()

                release.Set()
                Await server

                Assert.True(accepted.IsSet, "the stub server should have accepted the connection")
                Assert.True(threw, "a silent worker must surface as a transport failure")
                Assert.True(watch.ElapsedMilliseconds < Timeout_Ms * 6,
                            "Send waited " & watch.ElapsedMilliseconds.ToString() &
                            " ms on a silent worker - the timeout is not bounding the read")
            End Using
        End Using
    End Function

End Class
