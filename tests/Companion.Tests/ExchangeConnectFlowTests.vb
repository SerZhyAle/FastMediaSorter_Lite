Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 187 T-4: the order of the quick join as the dialog drives it - the string's pin is the anchor (no prompt), no pin means ONE
''' question with the fingerprint and the default No, a wrong leaf is refused with both fingerprints and no password sent, a typed address
''' joins the same way. Against ExchangeStubAccount; no window, no Share Manager, no agent.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeConnectFlowTests
    Implements IDisposable

    Private ReadOnly _tokenRoot As String = Path.Combine(Path.GetTempPath(), "fms-flow-" & Guid.NewGuid().ToString("N"))
    Private ReadOnly _port As New ExchangeDeviceAgentTests.MemoryPort()

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(_tokenRoot) Then Directory.Delete(_tokenRoot, True)
        Catch ex As IOException
        End Try
    End Sub

    Private Function MakePorts() As ExchangeConsumerPorts
        Return New ExchangeConsumerPorts With {
            .Entries = New ExchangeEntryStore(_port, _tokenRoot, New ExchangeDeviceAgentTests.XorCipher()),
            .MachineName = Function() "OFFICE-PC", .ProductVersion = "26.10.11.1"}
    End Function

    Private Shared Function StringOf(acc As ExchangeStubAccount, Optional pin As Boolean = True, Optional fp As String = Nothing, Optional name As String = "") As String
        Dim built As ExchangeConnectionStringResult = ExchangeConnectionString.Build(acc.Address, If(pin, If(fp, acc.Fingerprint), ""), name, "")
        Assert.True(built.Succeeded, built.Reason)
        Dim refusal As ExchangeConnectionStringResult = Nothing
        Return ExchangeConnectionString.Format(built.Value, refusal)
    End Function

    <Fact>
    Public Async Function A_string_with_the_pin_joins_with_no_question_and_keeps_the_name_as_a_hint() As Task
        Using acc As New ExchangeStubAccount()
            Dim asked As Integer = 0
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(StringOf(acc, name:="Home NAS"), "alice", ExchangeStubAccount.Password, "", "",
                MakePorts(), Function(host, fp)
                                 asked += 1
                                 Return True
                             End Function, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, result.Kind)
            Assert.Equal(0, asked)                                              ' the pin is the anchor: nothing is asked
            Assert.Equal("Home NAS", result.Entry.Name)
            Assert.Equal(acc.Fingerprint, result.Entry.Pin)
            ' no probe: the first envelope on the first connection was `enroll`
            Assert.Equal("enroll", acc.ReceivedOn(1).First())
        End Using
    End Function

    <Fact>
    Public Async Function A_wrong_leaf_is_refused_with_both_fingerprints_and_no_password_leaves_the_machine() As Task
        Using acc As New ExchangeStubAccount()
            Dim wrong As String = "SHA256:" & New String("A"c, 43)
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(StringOf(acc, fp:=wrong), "alice", ExchangeStubAccount.Password, "", "",
                ports, Function(host, fp) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.PinMismatch, result.Kind)
            Assert.Equal(wrong, result.Expected)
            Assert.Equal(acc.Fingerprint, result.Seen)
            Assert.Empty(acc.EnrollPasswordsSeen())
            Assert.Empty(acc.Received())
            Assert.Equal(0, ports.Entries.Count())
        End Using
    End Function

    <Fact>
    Public Async Function Without_a_pin_the_person_is_asked_once_with_the_fingerprint_and_a_no_sends_nothing() As Task
        Using acc As New ExchangeStubAccount()
            Dim shown As New List(Of String)
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, "", "",
                ports, Function(host, fp)
                           shown.Add(host & "|" & fp)
                           Return False
                       End Function, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.TrustDeclined, result.Kind)
            Assert.Equal({acc.Address & "|" & acc.Fingerprint}, shown.ToArray())
            Assert.Empty(acc.EnrollPasswordsSeen())
            Assert.Equal(0, ports.Entries.Count())
        End Using
    End Function

    <Fact>
    Public Async Function A_typed_address_and_an_accepted_fingerprint_join_and_pin_it() As Task
        Using acc As New ExchangeStubAccount()
            Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, "", "",
                MakePorts(), Function(host, fp) fp = acc.Fingerprint, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, result.Kind)
            Assert.Equal(acc.Fingerprint, result.Entry.Pin)
            Assert.Equal(ExchangeCertificateKind.Pinned, result.Entry.Certificate)
        End Using
    End Function

    <Fact>
    Public Async Function Signing_in_again_uses_the_pin_the_entry_holds_and_a_changed_leaf_stops() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim first As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(StringOf(acc), "alice", ExchangeStubAccount.Password, "", "", ports,
                                                                                   Function(h, f) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, first.Kind)
            Dim noQuestion As Func(Of String, String, Boolean) = Function(h, f) As Boolean
                                                                     Throw New InvalidOperationException("no question expected")
                                                                 End Function
            ' again, with the entry's own pin: no question
            Dim again As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, first.Entry.Pin, "", ports,
                                                                                   noQuestion, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Added, again.Kind)
            Assert.Equal(first.Entry.Id, again.Entry.Id)
            ' the entry's pin is another leaf than the server presents: a named stop, nothing replaced
            Dim other As String = "SHA256:" & New String("Q"c, 43)
            Dim changed As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(acc.Address, "alice", ExchangeStubAccount.Password, other, "", ports,
                                                                                     noQuestion, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.PinMismatch, changed.Kind)
            Assert.Equal(acc.Fingerprint, ports.Entries.Find(first.Entry.Id).Pin)
        End Using
    End Function

    <Fact>
    Public Async Function A_refused_input_or_a_missing_password_sends_nothing() As Task
        Using acc As New ExchangeStubAccount()
            Dim ports As ExchangeConsumerPorts = MakePorts()
            Dim share As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync("FMSSFTP2:AbCdEf", "alice", "x", "", "", ports, Function(h, f) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.InputRefused, share.Kind)
            Assert.Equal(ExchangeConnectionStringReason.LooksShareCode, share.Input.Looks)
            Dim noPassword As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync(StringOf(acc), "alice", "", "", "", ports, Function(h, f) True, CancellationToken.None)
            Assert.Equal(ExchangeConnectResultKind.Failed, noPassword.Kind)
            Assert.Equal(ExchangeConsumerEnrollOutcome.BadInput, noPassword.Enroll)
            Assert.Empty(acc.Received())
        End Using
    End Function

    <Fact>
    Public Async Function A_server_that_is_not_there_is_a_named_network_failure() As Task
        Dim ports As ExchangeConsumerPorts = MakePorts()
        Dim result As ExchangeConnectResult = Await ExchangeConnectFlow.RunAsync("127.0.0.1:1", "alice", "x", "", "", ports, Function(h, f) True, CancellationToken.None)
        Assert.Equal(ExchangeConnectResultKind.Failed, result.Kind)
        Assert.Equal(ExchangeConsumerEnrollOutcome.Network, result.Enroll)
        Assert.Equal(0, ports.Entries.Count())
    End Function

End Class
