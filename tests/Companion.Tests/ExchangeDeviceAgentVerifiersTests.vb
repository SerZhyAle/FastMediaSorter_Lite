Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' Ticket 263 T-3 (CR-14), the BROADCASTER side of DEVICE-EXCHANGE items BF and DV as the device agent wires it: `verifiers` rides every
''' `broadcast-start` and `broadcast-update` (one entry per credential pair, `[]` when the link has no password), the entries are the
''' contract's PBKDF2 and carry no password, one pair can be revoked alone, and the account's own devices get the OWN pair in the sealed
''' box. The pair set itself (hashes, revocation, the Digest lookup) is held by LiveCredentialPairsTests in the viewer suite.
''' </summary>
<Collection("Exchange device host")>
Public Class ExchangeDeviceAgentVerifiersTests

    Private Const BroadcastId As String = "bcast-0123456789abcdeQ"
    Private Const LinkPassword As String = "link-secret-0042"
    Private Const OwnPassword As String = "own-secret-abcdefghijklmnop-0042"

    Private Shared Async Function OnlineAgentAsync(rig As ExchangeDeviceAgentTests.Rig, acc As ExchangeStubAccount) As Task(Of ExchangeDeviceAgent)
        acc.Features = {"directory", "tunnel", "pairing", ExchangeSealedNames.FeatureSealed}
        acc.ListEnrolledDevices = True
        Dim agent As ExchangeDeviceAgent = rig.NewAgent()
        Dim result As ExchangeEnrollResult = Await agent.EnrollAsync(rig.Request(acc), CancellationToken.None)
        Assert.Equal(ExchangeEnrollOutcome.Enrolled, result.Outcome)
        Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetStatus().State = ExchangeAgentState.Online))
        Return agent
    End Function

    ''' <summary>A sealed broadcast with a LAN, a RELAY and a TUNNEL endpoint, the way the controller hands it to the agent.</summary>
    Private Shared Function StateFor(linkKey As String, credentials As LiveCredentialPairs, linkPassword As String) As LiveProducerState
        Dim input As New LiveDescriptorInput With {
            .Url = "http://127.0.0.1:1/live.aac", .Mode = "AUDIO_ONLY", .Title = "t", .SourceId = "src", .AudioCodec = "aac",
            .SampleRateHz = 44100, .Channels = 1, .AllowLanViewers = True,
            .RelayUrl = "https://relay.example:443/v2/b/" & BroadcastId & "/stream",
            .TunnelUrl = "fmsx://relay.example:443/b/" & BroadcastId & "/http",
            .CertFingerprint = "sha256:" & New String("a"c, 64),
            .InnerUrl = "http://127.0.0.1:1/live.aac",
            .AuthUser = If(linkPassword.Length > 0, LiveCredentialPairs.LinkUser, ""),
            .Sealed = True, .Outward = True, .RelayKey = linkKey
        }
        Return New LiveProducerState With {
            .Running = True, .Mode = "AUDIO_ONLY", .Url = input.Url, .Port = 1, .ListenerAddress = "127.0.0.1", .SourceName = "src",
            .BroadcastId = BroadcastId, .Password = linkPassword, .AuthUser = input.AuthUser, .Credentials = credentials,
            .RelayUrl = input.RelayUrl, .TunnelUrl = input.TunnelUrl, .Sealed = True,
            .DescriptorJson = LiveProducerDescriptorWriter.BuildJson(input),
            .RecordDescriptorJson = LiveProducerDescriptorWriter.BuildJson(input.ForRecord())
        }
    End Function

    Private Shared Function NewHub() As LiveBroadcastHub
        Return New LiveBroadcastHub(New LiveAudioJoinPolicy(), Function() 0L)
    End Function

    ''' <summary>The entries of one logged `verifiers` line ("type|[..]"), or Nothing when the envelope carried no such member.</summary>
    Private Shared Function EntriesOf(line As String) As List(Of JsonElement)
        Dim body As String = line.Substring(line.IndexOf("|"c) + 1)
        If body = "absent" Then Return Nothing
        Dim doc As JsonDocument = JsonDocument.Parse(body)
        Return doc.RootElement.EnumerateArray().ToList()
    End Function

    Private Shared Function RecordsOf(acc As ExchangeStubAccount) As List(Of String)
        Return acc.BroadcastLog().Select(Function(line) line.Substring(line.IndexOf("|"c) + 1)).ToList()
    End Function

    Private Shared Function HasBox(record As String) As Boolean
        Using doc As JsonDocument = JsonDocument.Parse(record)
            Dim member As JsonElement = Nothing
            Return doc.RootElement.TryGetProperty("sealed", member)
        End Using
    End Function

    Private Shared Function Decode(text As String) As Byte()
        Dim s As String = text.Replace("-"c, "+"c).Replace("_"c, "/"c)
        Select Case s.Length Mod 4
            Case 2 : s &= "=="
            Case 3 : s &= "="
        End Select
        Return Convert.FromBase64String(s)
    End Function

    ''' <summary>The contract's check, written out here and not taken from the producer's own class: PBKDF2-HMAC-SHA256, 600000 rounds,
    ''' 32 bytes, over the UTF-8 bytes of the password with the entry's own salt.</summary>
    Private Shared Function EntryAccepts(entry As JsonElement, user As String, password As String) As Boolean
        If entry.GetProperty("user").GetString() <> user Then Return False
        Dim salt As Byte() = Decode(entry.GetProperty("salt").GetString())
        Dim hash As Byte() = Decode(entry.GetProperty("hash").GetString())
        If salt.Length <> 16 OrElse hash.Length <> 32 Then Return False
        Dim expected As Byte() = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 600000, HashAlgorithmName.SHA256, 32)
        Return CryptographicOperations.FixedTimeEquals(expected, hash)
    End Function

    <Fact>
    Public Async Function A_protected_broadcast_start_carries_one_verifier_per_pair_and_never_a_password() As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)
                Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, OwnPassword)    ' the contract's 600000 rounds
                Dim state As LiveProducerState = StateFor(linkKey, pairs, LinkPassword)
                Dim hub As LiveBroadcastHub = NewHub()
                agent.OnBroadcastStarted(state, hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.BroadcastVerifiersLog().Count >= 1, 20))
                    Dim first As String = acc.BroadcastVerifiersLog()(0)
                    Assert.StartsWith("broadcast-start|", first, StringComparison.Ordinal)
                    Dim entries As List(Of JsonElement) = EntriesOf(first)
                    Assert.NotNull(entries)
                    Assert.Equal({LiveCredentialPairs.LinkUser, LiveCredentialPairs.OwnUser}, entries.Select(Function(e) e.GetProperty("user").GetString()).ToArray())
                    Assert.True(EntryAccepts(entries(0), LiveCredentialPairs.LinkUser, LinkPassword))
                    Assert.True(EntryAccepts(entries(1), LiveCredentialPairs.OwnUser, OwnPassword))
                    Assert.False(EntryAccepts(entries(0), LiveCredentialPairs.LinkUser, OwnPassword))     ' user-bound, password-bound
                    ' nothing but user, salt and hash per entry; no password anywhere on the wire or in the logs
                    For Each entry As JsonElement In entries
                        Assert.Equal({"user", "salt", "hash"}, entry.EnumerateObject().Select(Function(p) p.Name).ToArray())
                    Next
                    For Each line As String In acc.BroadcastVerifiersLog().Concat(acc.BroadcastLog())
                        Assert.DoesNotContain(LinkPassword, line)
                        Assert.DoesNotContain(OwnPassword, line)
                    Next
                    Assert.DoesNotContain(LinkPassword, rig.Everything())
                    Assert.DoesNotContain(OwnPassword, rig.Everything())
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_broadcast_with_no_password_sends_verifiers_as_an_empty_array_on_start_and_on_update() As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)
                Dim hub As LiveBroadcastHub = NewHub()
                agent.OnBroadcastStarted(StateFor(linkKey, LiveCredentialPairs.None(), ""), hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.BroadcastVerifiersLog().Count >= 1))
                    ' item DV: the member is present, and empty - an absent member would read as "no change" on an update
                    Assert.Equal("broadcast-start|[]", acc.BroadcastVerifiersLog()(0))

                    ' a new outward link on the running broadcast makes the agent send a broadcast-update (item N / DK); it carries the member too
                    Assert.NotEqual("", agent.NewBroadcastLinkKey(BroadcastId))
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.BroadcastVerifiersLog().Any(Function(l) l.StartsWith("broadcast-update|", StringComparison.Ordinal))))
                    For Each line As String In acc.BroadcastVerifiersLog()
                        Assert.EndsWith("|[]", line, StringComparison.Ordinal)
                    Next
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function A_revoked_pair_leaves_the_producers_check_and_the_next_update_and_the_last_pair_cannot_be_revoked() As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                Assert.False(Await agent.RevokeBroadcastPairAsync(LiveCredentialPairs.LinkUser))        ' nothing is on air yet

                Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)
                Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, OwnPassword, iterations:=2)
                Dim hub As LiveBroadcastHub = NewHub()
                agent.OnBroadcastStarted(StateFor(linkKey, pairs, LinkPassword), hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() acc.BroadcastVerifiersLog().Count >= 1))
                    Dim startEntries As List(Of JsonElement) = EntriesOf(acc.BroadcastVerifiersLog()(0))
                    Assert.Equal(2, startEntries.Count)
                    Dim ownEntryAtStart As String = startEntries(1).GetRawText()

                    ' the link pair alone: gone from the producer's lookup at once, gone from the relay's list with the update
                    Assert.True(Await agent.RevokeBroadcastPairAsync(LiveCredentialPairs.LinkUser))
                    Assert.Null(pairs.PasswordFor(LiveCredentialPairs.LinkUser))
                    Assert.Equal(OwnPassword, pairs.PasswordFor(LiveCredentialPairs.OwnUser))
                    ' (the sealed publisher may send an update of its own before this one, with both pairs: look for the one with a single entry)
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(
                        Function() acc.BroadcastVerifiersLog().Any(Function(l) l.StartsWith("broadcast-update|", StringComparison.Ordinal) AndAlso EntriesOf(l) IsNot Nothing AndAlso EntriesOf(l).Count = 1)))
                    Dim update As String = acc.BroadcastVerifiersLog().First(Function(l) l.StartsWith("broadcast-update|", StringComparison.Ordinal) AndAlso EntriesOf(l) IsNot Nothing AndAlso EntriesOf(l).Count = 1)
                    Dim remaining As List(Of JsonElement) = EntriesOf(update)
                    Assert.Equal(LiveCredentialPairs.OwnUser, remaining(0).GetProperty("user").GetString())
                    Assert.Equal(ownEntryAtStart, remaining(0).GetRawText())                               ' the survivor is not re-hashed
                    Assert.True(pairs.IsProtected)

                    ' a second revoke of the same user, an unknown user, and the last pair: refused (an empty list would open the relay)
                    Assert.False(Await agent.RevokeBroadcastPairAsync(LiveCredentialPairs.LinkUser))
                    Assert.False(Await agent.RevokeBroadcastPairAsync("nobody"))
                    Assert.False(Await agent.RevokeBroadcastPairAsync(LiveCredentialPairs.OwnUser))
                    Assert.Equal(OwnPassword, pairs.PasswordFor(LiveCredentialPairs.OwnUser))
                    Assert.Equal(1, pairs.Count)
                    For Each line As String In acc.BroadcastVerifiersLog()
                        Assert.False(line.EndsWith("|[]", StringComparison.Ordinal), "no update ever tells the relay to check nobody")
                    Next
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
                Assert.False(Await agent.RevokeBroadcastPairAsync(LiveCredentialPairs.OwnUser))           ' the broadcast ended
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function The_sealed_box_hands_the_own_devices_the_own_pair_and_not_the_pair_of_the_link() As Task
        Using acc As New ExchangeStubAccount(), rig As New ExchangeDeviceAgentTests.Rig()
            Using agent As ExchangeDeviceAgent = Await OnlineAgentAsync(rig, acc)
                Dim linkKey As String = agent.NewBroadcastLinkKey(BroadcastId)
                Dim pairs As LiveCredentialPairs = LiveCredentialPairs.ForLink(LinkPassword, OwnPassword, iterations:=2)
                Dim hub As LiveBroadcastHub = NewHub()
                agent.OnBroadcastStarted(StateFor(linkKey, pairs, LinkPassword), hub, hub)
                Try
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() RecordsOf(acc).Any(Function(r) HasBox(r))))
                    acc.Broadcasts.Add(RecordsOf(acc).Last(Function(r) HasBox(r)))
                    Await acc.PushDirectoryAsync()
                    Assert.True(Await ExchangeStubAccount.WaitUntilAsync(Function() agent.GetFullDirectory().Broadcasts.Count = 1))
                    ' what an own device opens: the pair it answers the challenge with
                    Using req As JsonDocument = JsonDocument.Parse("{""broadcastId"":""" & BroadcastId & """}")
                        Dim opened = Await agent.HandleAsync(ExchangeAgentPipe.RequestGetBroadcastSeal, req.RootElement, CancellationToken.None)
                        Assert.True(opened.Ok, opened.Error)
                        Using payload As JsonDocument = JsonDocument.Parse(opened.DataJson)
                            Dim credentials As JsonElement = payload.RootElement.GetProperty("credentials")
                            Assert.Equal(LiveCredentialPairs.OwnUser, credentials.GetProperty("user").GetString())
                            Assert.Equal(OwnPassword, credentials.GetProperty("password").GetString())
                        End Using
                        Assert.DoesNotContain(LinkPassword, opened.DataJson)
                    End Using
                Finally
                    agent.OnBroadcastStopped(BroadcastId)
                End Try
            End Using
        End Using
    End Function

End Class
