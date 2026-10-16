#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

Public Class ExchangeDormancyTests

    Private NotInheritable Class RejectTransport
        Implements IExchangeTransport

        Public Attempts As Integer

        Public Function ConnectAsync(host As String, port As Integer, pin As String, ct As CancellationToken) As Task(Of ExchangeTlsConnection) Implements IExchangeTransport.ConnectAsync
            Interlocked.Increment(Attempts)
            Throw New InvalidOperationException("An unconfigured surface attempted a connection.")
        End Function
    End Class

    <Theory>
    <InlineData("FMSEXCH1:x=127.0.0.1%3A1&i=AAAAAAAAAAAAAAAAAAAAAA", "", "password")>
    <InlineData("FMSEXCH1:x=127.0.0.1%3A1&i=AAAAAAAAAAAAAAAAAAAAAA", "alice", "")>
    <InlineData("FMSEXCH1:x=127.0.0.1%3A1", "alice", "password")>
    Public Async Function Incomplete_registration_never_probes_trust_connects_or_persists(input As String, login As String, password As String) As Task
        Dim settings As New ExchangeStoresTests.MemorySettings()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-dormant-register-" & Guid.NewGuid().ToString("N"))
        Dim transport As New RejectTransport()
        Dim ports As New ExchangeConsumerPorts With {
            .Entries = New ExchangeEntryStore(settings, folder), .Transport = transport}
        Dim trustQuestions As Integer = 0
        Dim confirm As Func(Of String, String, Boolean) =
            Function(server, fingerprint)
                trustQuestions += 1
                Return False
            End Function

        Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RegisterAsync(input, login, password, "", "PC", ports, confirm, CancellationToken.None)

        Assert.True(result.Input.IsUsable)
        Assert.Equal(If(result.Input.HasInvitation, ExchangeConnectResultKind.Failed, ExchangeConnectResultKind.InputRefused), result.Kind)
        Assert.Equal(ExchangeConsumerEnrollOutcome.BadInput, result.Enroll)
        Assert.Equal(0, transport.Attempts)
        Assert.Equal(0, trustQuestions)
        Assert.Equal(0, settings.Writes)
        Assert.Empty(settings.Values)
        Assert.False(Directory.Exists(folder))
    End Function

    <Fact>
    Public Sub Repeated_empty_device_refreshes_never_create_a_session_or_storage()
        Dim settings As New ExchangeStoresTests.MemorySettings()
        Dim folder As String = Path.Combine(Path.GetTempPath(), "fms-dormant-refresh-" & Guid.NewGuid().ToString("N"))
        Dim entries As New ExchangeEntryStore(settings, folder)
        Dim transport As New RejectTransport()
        Using hub As New ExchangeEntryHub(New ExchangeConsumerPorts With {.Entries = entries, .Transport = transport})
            For refresh As Integer = 1 To 20
                Assert.Empty(entries.Load())
                Assert.Equal(0, entries.Count())
                Assert.Null(hub.SessionFor(entries.Find("missing")))
                hub.Forget("missing")
            Next
        End Using
        Assert.Equal(0, transport.Attempts)
        Assert.Equal(0, settings.Writes)
        Assert.Empty(settings.Values)
        Assert.False(Directory.Exists(folder))
    End Sub

End Class
#End If
