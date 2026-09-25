Option Strict On

Imports System.IO
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Who is on the other end of the control pipe, and what an unusable answer means
''' (tickets 054 H-10, H-22, H-26).
'''
''' H-10: the pipe name is machine-wide, and before the worker creates it ANY local account
''' can - with a DACL of its choosing. The client must refuse the exchange BEFORE a byte of
''' the request is written when the server process is not one it trusts.
'''
''' H-22: a worker that answered in a shape this client cannot read is still a RUNNING
''' worker - reading it as "nobody answered" sent EnsureRunning off to spawn a second
''' worker that could only lose the pipe.
'''
''' H-26: an access denial on connect is the pipe DACL of a worker that belongs to another
''' account - not "not running".
'''
''' All tests drive a pipe of their own (see WorkerIpcTimeoutTests for why the frozen
''' machine-wide pipe can never be the one under test).
''' </summary>
Public Class WorkerIpcIdentityTests

    Private Const Timeout_Ms As Integer = 2000

    Private Shared Function OkStatusAnswer() As String
        Return "{""schemaVersion"":1,""ok"":true,""status"":{""running"":true,""listenPort"":2222,""roots"":[]}}"
    End Function

    <Fact>
    Public Sub UntrustedServer_IsRefusedBeforeAnythingIsSent()
        Using stub As New StubWorker(Function(type) OkStatusAnswer())
            ' A per-pipe verifier that says No regardless of who the server is - the seam
            ' the product's own check is reached through.
            WorkerIpc.ServerVerifierOverrides(stub.PipeName) = Function(pid) False
            Try
                Dim resp As WorkerResponse = WorkerProcess.Exchange(
                    New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms, stub.PipeName)
                Assert.NotNull(resp)
                Assert.False(resp.Ok)
                Assert.True(resp.IsProblem)
                Assert.Equal(WorkerExchangeProblem.NotOurWorker, resp.Problem)
                Assert.Null(resp.Status)
                ' Nothing was written to the squatter - the folder list never left.
                Assert.Empty(stub.Seen())
            Finally
                WorkerIpc.ServerVerifierOverrides.TryRemove(stub.PipeName, Nothing)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub MalformedAnswer_IsAWorkerRunning_NotSilence()
        ' "ok" as a string does not bind to Boolean: a JsonException in ParseResponse.
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":""yes""}")
            Dim resp As WorkerResponse = WorkerProcess.Exchange(
                New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms, stub.PipeName)
            Assert.NotNull(resp)
            Assert.False(resp.Ok)
            Assert.True(resp.IsProblem)
            Assert.Equal(WorkerExchangeProblem.Malformed, resp.Problem)
        End Using
    End Sub

    <Fact>
    Public Sub NullBodyAnswer_IsMalformedToo()
        Using stub As New StubWorker(Function(type) "null")
            Dim resp As WorkerResponse = WorkerProcess.Exchange(
                New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms, stub.PipeName)
            Assert.NotNull(resp)
            Assert.True(resp.IsProblem)
            Assert.Equal(WorkerExchangeProblem.Malformed, resp.Problem)
        End Using
    End Sub

    ''' <summary>The exact misdiagnosis H-22 fixed: whatever holds the pipe, a second
    ''' worker would only lose the race for it.</summary>
    <Fact>
    Public Sub EnsureRunning_DoesNotSpawnOnAMalformedAnswer()
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":1,""ok"":""yes""}")
            Dim launches As Integer = 0
            Dim resp As WorkerResponse = WorkerProcess.EnsureRunning(
                Timeout_Ms, stub.PipeName, Function()
                                              launches += 1
                                              Return True
                                          End Function)
            Assert.Equal(0, launches)
            Assert.NotNull(resp)
            Assert.True(resp.IsProblem)
            Assert.Equal(WorkerExchangeProblem.Malformed, resp.Problem)
        End Using
    End Sub

    <Fact>
    Public Sub MalformedBody_FromANewerWorkerIsStillRefusedByName()
        ' The version is read on its own before giving up, so a newer worker is a version
        ' refusal (WORKER-IPC section 6 rule 3), never "an answer we could not read".
        Using stub As New StubWorker(Function(type) "{""schemaVersion"":2,""ok"":""yes""}")
            Dim resp As WorkerResponse = WorkerProcess.Exchange(
                New WorkerRequest With {.type = "GetStatus"}, Timeout_Ms, stub.PipeName)
            Assert.NotNull(resp)
            Assert.True(resp.IsRefused)
            Assert.Equal(2, resp.Refusal.WorkerVersion)
        End Using
    End Sub

    ' --- the exception classification (H-26) -----------------------------------

    <Fact>
    Public Sub ClassifyFailure_SeparatesAccessDeniedFromSilence()
        Assert.Equal(WorkerExchangeProblem.NotAuthorized,
                     WorkerProcess.ClassifyFailure(New UnauthorizedAccessException("denied")))
        Assert.Equal(WorkerExchangeProblem.Malformed,
                     New WorkerExchangeException(WorkerExchangeProblem.Malformed, "x").Problem)
        Assert.Equal(WorkerExchangeProblem.None,
                     WorkerProcess.ClassifyFailure(New IOException("timeout")))
    End Sub

End Class
