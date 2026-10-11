#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports Xunit

''' <summary>
''' Ticket 177 section 3 item 3, the LIVE-BROADCAST half of the gate guard (the exchange half lives in
''' <c>ExchangeClientGuardTests</c>, and the source-wide scan of every "Offered" constant in
''' <c>DualModeGateGuardTests</c>). The gate is 172 section 6 rule 6: <c>LiveBroadcastFeatures.Offered</c>,
''' false until the owner opens it, SEPARATE from the exchange's gate (Q172-5) - the [CONTRACT] audio shape
''' may be opened before the exchange, the [PROPOSED] video shape binds nobody - and 179 section 2 puts it in
''' the table of gates the release preflight reads (<c>tools\Test-FeatureGates.ps1</c>).
'''
''' What is NOT here, and why: the cases "no surface is reachable while the gate is false" need a producer
''' surface, and 172 has none built yet. Each case lands with the step of 172-176 that creates the surface
''' (the WANT 0b remainder of 177); until then the scan of DualModeGateGuardTests is the interim form and
''' already fails if this constant is switched on.
''' </summary>
Public Class LiveBroadcastGateGuardTests

    Private Shared Function RepoRoot() As String
        Dim dir As New DirectoryInfo(AppContext.BaseDirectory)
        While dir IsNot Nothing
            If File.Exists(Path.Combine(dir.FullName, "FastMediaSorter.sln")) Then Return dir.FullName
            dir = dir.Parent
        End While
        Throw New InvalidOperationException("repository root not found above " & AppContext.BaseDirectory)
    End Function

    ''' <summary>The shape of 159 section 3.4: the constant flips only together with the evidence that names the rung.</summary>
    <Fact>
    Public Sub The_gate_is_shut_unless_an_evidence_file_names_the_rung_or_phase_it_was_opened_at()
        Dim source As String = File.ReadAllText(Path.Combine(RepoRoot(), "src", "LiveBroadcast", "LiveBroadcastFeatures.vb"))
        Dim m As Match = Regex.Match(source, "Public Const Offered As Boolean = (True|False)")
        Assert.True(m.Success, "LiveBroadcastFeatures.Offered must stay a compile-time constant (Public Const Offered As Boolean = ..)")
        Dim offered As Boolean = m.Groups(1).Value = "True"
        Assert.Equal(offered, LiveBroadcastFeatures.Offered)
        If Not offered Then Return

        Dim evidence As String = Path.Combine(RepoRoot(), "docs", "specifications", "live-broadcast-gate-evidence.md")
        Assert.True(File.Exists(evidence),
                    "LiveBroadcastFeatures.Offered is True but docs\specifications\live-broadcast-gate-evidence.md does not exist. Opening the gate " &
                    "needs the owner's word and an evidence file naming the rung or phase it was opened at (177 section 3 item 3; 172 section 6 keeps " &
                    "the [PROPOSED] video shape closed until the owner flips it).")
        Dim text As String = File.ReadAllText(evidence)
        Assert.Matches("(?i)(rung\s*[2-5]|phase\s*B[0-3])", text)
        Assert.Matches("20\d\d-\d\d-\d\d", text)
    End Sub

    <Fact>
    Public Sub The_default_stays_False_no_build_offers_a_live_broadcast_producer()
        ' The test above allows True WITH evidence. This one states the present decision: the owner has not
        ' opened the producer, and the [PROPOSED] video shape is not his word until he gives it (179 section 2).
        Assert.False(LiveBroadcastFeatures.Offered, "the owner has not opened the live-broadcast producer; flip the constant only on his word, with the evidence file")
    End Sub

    Private Shared Function Reader(process As String, user As String, machine As String) As Func(Of String, EnvironmentVariableTarget, String)
        Return Function(name, target)
                   Assert.Equal("FMS_LIVE_BROADCAST", name)
                   Select Case target
                       Case EnvironmentVariableTarget.Process : Return process
                       Case EnvironmentVariableTarget.User : Return user
                       Case Else : Return machine
                   End Select
               End Function
    End Function

    <Fact>
    Public Sub The_developer_switch_is_the_value_1_in_the_process_user_or_machine_environment_and_nothing_else()
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, Nothing)))
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(Reader("", "", "")))
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(Reader("0", "no", "true")))
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(Reader("11", "01", "1 1")))
        Assert.True(LiveBroadcastFeatures.DeveloperSwitchOn(Reader("1", Nothing, Nothing)))
        Assert.True(LiveBroadcastFeatures.DeveloperSwitchOn(Reader(Nothing, "1", Nothing)))
        Assert.True(LiveBroadcastFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, "1")))
        Assert.True(LiveBroadcastFeatures.DeveloperSwitchOn(Reader(Nothing, Nothing, " 1 ")))
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(Nothing))
    End Sub

    <Fact>
    Public Sub With_the_gate_shut_the_features_exist_only_through_the_own_switch()
        ' Q172-5: the exchange's developer switch (FMS_EXCHANGE_CLIENT, the one the first check of 182 sets
        ' on the test host) must not light the producer up - the two gates are separate.
        Assert.False(LiveBroadcastFeatures.Offered)
        Assert.False(LiveBroadcastFeatures.IsEnabled(Reader(Nothing, Nothing, Nothing)))
        Assert.True(LiveBroadcastFeatures.IsEnabled(Reader(Nothing, Nothing, "1")))
        Dim exchangeSwitchOnly As Func(Of String, EnvironmentVariableTarget, String) =
            Function(name, target)
                If name = "FMS_LIVE_BROADCAST" AndAlso target = EnvironmentVariableTarget.Machine Then Return "1"
                Return Nothing
            End Function
        Assert.True(LiveBroadcastFeatures.IsEnabled(exchangeSwitchOnly))
        Dim exchangeOnly As Func(Of String, EnvironmentVariableTarget, String) =
            Function(name, target)
                Return If(name = "FMS_EXCHANGE_CLIENT", "1", Nothing)
            End Function
        Assert.False(LiveBroadcastFeatures.IsEnabled(exchangeOnly))
    End Sub

    <Fact>
    Public Sub A_reader_that_throws_is_the_switch_off_not_an_error()
        Dim throwing As Func(Of String, EnvironmentVariableTarget, String) =
            Function(n, t)
                Throw New ArgumentException("no machine scope here")
            End Function
        Assert.False(LiveBroadcastFeatures.DeveloperSwitchOn(throwing))
    End Sub

End Class
#End If
