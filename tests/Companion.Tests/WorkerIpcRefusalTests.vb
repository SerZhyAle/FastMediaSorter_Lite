Option Strict On

Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' WORKER-IPC section 6 rule 3 on the host, end to end - ticket 035 phase 1.
'''
''' The refusal existed before this file (ticket 025 section 9) and never reached anybody: it was
''' thrown at the transport and every caller swallowed it as "the worker is unreachable". So a
''' newer worker read as "could not reach the companion", and EnsureRunning then went on to spawn a
''' second worker that could only lose the frozen pipe to the one that had just answered. These
''' tests drive a private pipe answering with a higher <c>schemaVersion</c> and follow the answer
''' up to the sentence the user reads.
'''
''' In the language collection: the refusal is a localized sentence, and the process-global UI
''' language must not move under the comparison. The same collection also serializes the tests
''' that read <c>WorkerProcess.LastRefusal</c>, which is process-global too.
''' </summary>
<Collection(UiLanguageCollection.Name)>
Public Class WorkerIpcRefusalTests

    Private Const Newer As String = "{""schemaVersion"":2,""ok"":true}"

    <Fact>
    Public Sub EnsureUnderstood_RefusesUpwardsOnly()
        ' Newer: refused, carrying both numbers.
        Dim ex As WorkerVersionRefusedException = Assert.Throws(Of WorkerVersionRefusedException)(
            Sub() WorkerIpc.EnsureUnderstood(New WorkerResponse With {.SchemaVersion = WorkerIpc.SchemaVersion + 1}))
        Assert.Equal(WorkerIpc.SchemaVersion + 1, ex.WorkerVersion)
        Assert.Equal(WorkerIpc.SchemaVersion, ex.ClientVersion)

        ' Equal, older, and absent (0 = a worker older than the field): all read.
        WorkerIpc.EnsureUnderstood(New WorkerResponse With {.SchemaVersion = WorkerIpc.SchemaVersion})
        WorkerIpc.EnsureUnderstood(New WorkerResponse With {.SchemaVersion = 0})
        WorkerIpc.EnsureUnderstood(Nothing)
    End Sub

    ''' <summary>A refusal is its own type - never one of the transport failures a caller is
    ''' entitled to read as "nobody answered".</summary>
    <Fact>
    Public Async Function Send_ThrowsTheTypedRefusal_NotATransportFailure() As Task
        Using stub As New StubWorker(Function(t) Newer)
            Dim caught As Exception = Nothing
            Try
                Await Task.Run(Function() WorkerIpc.Send(New WorkerRequest With {.type = "GetStatus"}, 3000, stub.PipeName))
            Catch ex As Exception
                caught = ex
            End Try
            Dim refusal As WorkerVersionRefusedException = Assert.IsType(Of WorkerVersionRefusedException)(caught)
            Assert.Equal(2, refusal.WorkerVersion)
        End Using
    End Function

    ''' <summary>The product's exchange turns the refusal into a typed RESULT: a response that
    ''' says "a worker answered, and nothing it said was read" - no status, no ok.</summary>
    <Fact>
    Public Async Function Exchange_ReturnsARefusedResponse_AndRemembersIt() As Task
        Using stub As New StubWorker(Function(t) Newer)
            Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(3000, stub.PipeName))
            Assert.NotNull(resp)
            Assert.True(resp.IsRefused)
            Assert.False(resp.Ok)
            Assert.Null(resp.Status)
            Assert.NotNull(WorkerProcess.LastRefusal)
            Assert.Equal(2, WorkerProcess.LastRefusal.WorkerVersion)
        End Using

        ' An understood answer afterwards clears it: the refusal is a fact about THE worker now.
        Using current As New StubWorker(Function(t) "{""schemaVersion"":1,""ok"":true,""status"":{""running"":false}}")
            Dim resp As WorkerResponse = Await Task.Run(Function() WorkerProcess.TryGetStatus(3000, current.PipeName))
            Assert.False(resp.IsRefused)
            Assert.Null(WorkerProcess.LastRefusal)
        End Using
    End Function

    ''' <summary>The acceptance line of phase 1: a worker that answered - even with a refusal -
    ''' is running, and nothing is launched after it.</summary>
    <Fact>
    Public Async Function EnsureRunning_ReturnsTheRefusal_AndStartsNoProcess() As Task
        Using stub As New StubWorker(Function(t) Newer)
            Dim launched As Boolean = False
            Dim resp As WorkerResponse = Await Task.Run(
                Function() WorkerProcess.EnsureRunning(3000, stub.PipeName,
                                                       Function()
                                                           launched = True
                                                           Return True
                                                       End Function))
            Assert.NotNull(resp)
            Assert.True(resp.IsRefused)
            Assert.False(launched, "EnsureRunning launched a worker after one had answered")
            Assert.Equal(New String() {"GetStatus"}, stub.Seen().ToArray())
        End Using
    End Function

    ''' <summary>What the user reads is the registered sentence - in the active language, naming
    ''' both versions - and in Server mode it names the service.</summary>
    <Fact>
    Public Sub UiText_IsTheRegisteredMessage()
        Dim saved As String = Localization.CurrentCode
        Try
            For Each code As String In New String() {"en", "ru", "de"}
                Localization.CurrentCode = code
                Dim refusal As New WorkerVersionRefusedException(2, 1)
                Dim expected As String = Localization.TF(
                    "Служба общего доступа отвечает по версии протокола {0}, а это приложение понимает {1}. Обновите приложение.", 2, 1)
                Assert.Equal(expected, refusal.Message)
                Assert.Equal(expected, ShareController.RefusalMessage(refusal, serviceHost:=False))

                Dim service As String = ShareController.RefusalMessage(refusal, serviceHost:=True)
                Assert.Contains(ServiceControl.ServiceName, service)
                Assert.Contains("2", service)
                Assert.NotEqual(expected, service)
            Next
            Localization.CurrentCode = "en"
            Assert.Contains("Update the app", New WorkerVersionRefusedException(2, 1).Message)
        Finally
            Localization.CurrentCode = saved
        End Try
    End Sub

    ''' <summary>A share started against a newer worker stops at the refusal: the folder list
    ''' and the start are never sent, and the result carries the sentence.</summary>
    <Fact>
    Public Sub RefusedText_OfARefusal_IsTheRefusalMessage()
        Dim refusal As New WorkerVersionRefusedException(2, 1)
        Dim text As String = ShareController.RefusedText(WorkerResponse.Refused(refusal))
        Assert.Contains(refusal.WorkerVersion.ToString(), text)
    End Sub

End Class
