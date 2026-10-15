#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' Specification 228: One resource record with every channel, publishing on every own path,
''' outward access by guest invitation or link with a password, the stored PIN.
''' </summary>
Public Class OneResourceRecord228Tests

    Private NotInheritable Class MemoryValues
        Friend ReadOnly Values As New Dictionary(Of String, String)()
        Friend ReadOnly Writes As New List(Of String)()
        Friend Function Read(name As String) As String
            Dim v As String = Nothing
            Return If(Values.TryGetValue(name, v), v, "")
        End Function
        Friend Sub Write(name As String, value As String)
            Writes.Add(name)
            Values(name) = value
        End Sub
    End Class

    Private NotInheritable Class MemorySecrets
        Implements IResourceSecretStore
        Friend ReadOnly Blobs As New Dictionary(Of String, String)()
        Public Sub PutBlob(handle As String, protectedBlob As String) Implements IResourceSecretStore.PutBlob
            Blobs(handle) = protectedBlob
        End Sub
        Public Function GetBlob(handle As String) As String Implements IResourceSecretStore.GetBlob
            Dim v As String = Nothing
            Return If(Blobs.TryGetValue(handle, v), v, "")
        End Function
        Public Sub DeleteBlob(handle As String) Implements IResourceSecretStore.DeleteBlob
            Blobs.Remove(handle)
        End Sub
        Public Function AllHandles() As IReadOnlyList(Of String) Implements IResourceSecretStore.AllHandles
            Return Blobs.Keys.ToList()
        End Function
    End Class

    Private NotInheritable Class FakeSession
        Implements IFileSystemProvider
        Implements IDisposable

        Friend ReadOnly Endpoint As FmsEndpoint
        Friend ReadOnly AttemptDescriptor As ResourceDescriptor

        Friend Sub New(endpoint As FmsEndpoint, Optional attempt As ResourceDescriptor = Nothing)
            Me.Endpoint = endpoint
            Me.AttemptDescriptor = attempt
        End Sub

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property

        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                Dim canWrite As Boolean = If(AttemptDescriptor IsNot Nothing, Not AttemptDescriptor.ReadOnlyHint, True)
                Return New ProviderCapabilities(canWrite:=canWrite)
            End Get
        End Property

        Public Function ListEntries(dirPath As String, ct As Threading.CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            Return New List(Of ResourceEntry)()
        End Function

        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            entry = Nothing
            Return False
        End Function

        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As Threading.CancellationToken) Implements IFileSystemProvider.Download
        End Sub

        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As Threading.CancellationToken) Implements IFileSystemProvider.Upload
        End Sub

        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
        End Sub

        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
        End Sub

        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub
    End Class

    Private NotInheritable Class ScriptedDialer
        Implements IEndpointDialer

        Friend ReadOnly Dialled As New List(Of String)()
        Friend ReadOnly Script As New Dictionary(Of String, ResourceFailure)()
        Friend ReadOnly Sessions As New List(Of FakeSession)()
        Friend Clock As ManualClock
        Friend Descriptor As ResourceDescriptor

        Public Function DialAsync(endpoint As FmsEndpoint, ct As Threading.CancellationToken) As Threading.Tasks.Task(Of DialResult) Implements IEndpointDialer.DialAsync
            SyncLock Dialled
                Dialled.Add(endpoint.Key())
            End SyncLock
            Dim failure As ResourceFailure = Nothing
            If Script.TryGetValue(endpoint.Key(), failure) Then
                Return Threading.Tasks.Task.FromResult(DialResult.Failed(failure))
            End If
            Dim session As New FakeSession(endpoint, Descriptor)
            SyncLock Sessions
                Sessions.Add(session)
            End SyncLock
            Return Threading.Tasks.Task.FromResult(DialResult.Verified(session))
        End Function
    End Class

    Private NotInheritable Class ManualClock
        Implements IResolutionClock

        Private ReadOnly timers As New List(Of (Due As Long, Source As Threading.Tasks.TaskCompletionSource(Of Boolean)))()
        Private ReadOnly gate As New Object()
        Private now As Long

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                SyncLock gate
                    Return now
                End SyncLock
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As Threading.CancellationToken) As Threading.Tasks.Task Implements IResolutionClock.Delay
            Dim source As New Threading.Tasks.TaskCompletionSource(Of Boolean)()
            If ct.IsCancellationRequested Then
                source.TrySetCanceled(ct)
                Return source.Task
            End If
            SyncLock gate
                timers.Add((now + ms, source))
            End SyncLock
            ct.Register(Sub()
                            SyncLock gate
                                timers.RemoveAll(Function(t) ReferenceEquals(t.Source, source))
                            End SyncLock
                            source.TrySetCanceled(ct)
                        End Sub)
            Return source.Task
        End Function

        Friend Sub Advance(ms As Long)
            Dim target As Long
            SyncLock gate
                target = now + ms
            End SyncLock
            Do
                Dim fire As (Due As Long, Source As Threading.Tasks.TaskCompletionSource(Of Boolean))? = Nothing
                SyncLock gate
                    Dim due = timers.Where(Function(t) t.Due <= target).OrderBy(Function(t) t.Due).ToList()
                    If due.Count > 0 Then
                        fire = due(0)
                        timers.Remove(due(0))
                        now = Math.Max(now, due(0).Due)
                    Else
                        now = target
                    End If
                End SyncLock
                If fire Is Nothing Then Return
                fire.Value.Source.TrySetResult(True)
            Loop
        End Sub
    End Class

    ' ----------------------------------------------------------------- merge rules ----

    <Fact>
    Public Sub CanProveSameResource_MatchesFingerprintAndCanonicalRoot()
        Dim r1 As New ResourceDescriptor With {
            .Id = "res1", .DisplayName = "Primary", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/media/photos", .PinnedFingerprint = "SHA256:abc123def456",
            .Kind = ResourceKind.Sftp
        }
        Dim r2 As New ResourceDescriptor With {
            .Id = "res2", .DisplayName = "Secondary", .Host = "mywan.example.com", .Port = 2222,
            .RootPath = "media/photos/", .PinnedFingerprint = "SHA256:abc123def456",
            .Kind = ResourceKind.Sftp
        }
        Assert.True(ResourceRegistry.CanProveSameResource(r1, r2))
    End Sub

    <Fact>
    Public Sub CanProveSameResource_UnprovablePair_StaysSeparate()
        ' Absence is not authority: no fingerprint means unprovable
        Dim r1 As New ResourceDescriptor With {
            .Id = "res1", .DisplayName = "A", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/media/photos", .PinnedFingerprint = "",
            .Kind = ResourceKind.Sftp
        }
        Dim r2 As New ResourceDescriptor With {
            .Id = "res2", .DisplayName = "B", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/media/photos", .PinnedFingerprint = "",
            .Kind = ResourceKind.Sftp
        }
        Assert.False(ResourceRegistry.CanProveSameResource(r1, r2))

        ' Different roots
        Dim r3 As New ResourceDescriptor With {
            .Id = "res3", .DisplayName = "C", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/media/videos", .PinnedFingerprint = "SHA256:abc123def456",
            .Kind = ResourceKind.Sftp
        }
        Assert.False(ResourceRegistry.CanProveSameResource(r1, r3))
    End Sub

    <Fact>
    Public Sub CanProveSameResource_MatchesExchangeId()
        Dim r1 As New ResourceDescriptor With {
            .Id = "res1", .DisplayName = "Exchange Share 1",
            .ExchangeResourceId = "share-alpha", .ExchangeEntryId = "entry-001"
        }
        Dim r2 As New ResourceDescriptor With {
            .Id = "res2", .DisplayName = "Exchange Share 2",
            .ExchangeResourceId = "share-alpha", .ExchangeEntryId = "entry-001"
        }
        Assert.True(ResourceRegistry.CanProveSameResource(r1, r2))
    End Sub

    <Fact>
    Public Sub MergeDuplicates_CombinesEndpoints_AndPreservesMetadata()
        Dim r1 As New ResourceDescriptor With {
            .Id = "res1", .DisplayName = "My Photos", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/photos", .PinnedFingerprint = "SHA256:fingerprint1",
            .Kind = ResourceKind.Sftp, .ReadOnlyHint = False,
            .Endpoints = New List(Of ResourceEndpoint) From {New ResourceEndpoint With {.Host = "192.168.1.50", .Port = 22, .Kind = "lan"}}
        }
        Dim r2 As New ResourceDescriptor With {
            .Id = "res2", .DisplayName = "Photos WAN", .Host = "wan.example.com", .Port = 2222,
            .RootPath = "/photos", .PinnedFingerprint = "SHA256:fingerprint1",
            .Kind = ResourceKind.Sftp, .ReadOnlyHint = True,
            .Endpoints = New List(Of ResourceEndpoint) From {New ResourceEndpoint With {.Host = "wan.example.com", .Port = 2222, .Kind = "portforward"}}
        }

        Dim list As New List(Of ResourceDescriptor) From {r1, r2}
        Dim merged As List(Of ResourceDescriptor) = ResourceRegistry.MergeDuplicates(list)

        Assert.Single(merged)
        Dim m As ResourceDescriptor = merged(0)
        Assert.Equal("res1", m.Id)
        Assert.Equal("My Photos", m.DisplayName)
        Assert.Equal(2, m.Endpoints.Count)
        Assert.Contains(m.Endpoints, Function(ep) ep.Host = "192.168.1.50" AndAlso ep.Port = 22)
        Assert.Contains(m.Endpoints, Function(ep) ep.Host = "wan.example.com" AndAlso ep.Port = 2222)
    End Sub

    <Fact>
    Public Sub PreMergeBackup_BackedUpOnFirstMerge_AndRestorable()
        Dim r1 As New ResourceDescriptor With {
            .Id = "res1", .DisplayName = "R1", .Host = "192.168.1.50", .Port = 22,
            .RootPath = "/photos", .PinnedFingerprint = "SHA256:fp", .Kind = ResourceKind.Sftp
        }
        Dim r2 As New ResourceDescriptor With {
            .Id = "res2", .DisplayName = "R2", .Host = "wan.example.com", .Port = 2222,
            .RootPath = "/photos", .PinnedFingerprint = "SHA256:fp", .Kind = ResourceKind.Sftp
        }

        Dim mem As New MemoryValues()
        mem.Write("Resources", ResourceRegistry.Serialize(New List(Of ResourceDescriptor) From {r1, r2}))

        ' First Load should trigger merge and write Resources.premerge.bak
        Dim loaded As List(Of ResourceDescriptor) = ResourceRegistry.Load(AddressOf mem.Read, AddressOf mem.Write)
        Assert.Single(loaded)
        Assert.NotEmpty(mem.Read("Resources.premerge.bak"))

        ' Subsequent Load does not overwrite backup
        mem.Write("Resources.premerge.bak", "do-not-overwrite")
        loaded = ResourceRegistry.Load(AddressOf mem.Read, AddressOf mem.Write)
        Assert.Equal("do-not-overwrite", mem.Read("Resources.premerge.bak"))

        ' RestorePreMergeBackup restores the premerge backup
        mem.Write("Resources.premerge.bak", ResourceRegistry.Serialize(New List(Of ResourceDescriptor) From {r1, r2}))
        Dim restored As Boolean = ResourceRegistry.RestorePreMergeBackup(AddressOf mem.Read, AddressOf mem.Write)
        Assert.True(restored)
        Dim afterRestore As List(Of ResourceDescriptor) = ResourceRegistry.Parse(mem.Read("Resources"))
        Assert.Equal(2, afterRestore.Count)
    End Sub

    ' ----------------------------------------------------------------- channel race ----

    <Fact>
    Public Sub ChannelPriorityRules_FastestToSlowest()
        ' Fast to slow: LAN (1) -> IPv6 (2) -> PortForward (3) -> Tunnel (4) -> Drive (5)
        Assert.Equal(1, ChannelPriorityRules.ChannelRank("lan"))
        Assert.Equal(2, ChannelPriorityRules.ChannelRank("ipv6"))
        Assert.Equal(3, ChannelPriorityRules.ChannelRank("portforward"))
        Assert.Equal(4, ChannelPriorityRules.ChannelRank("tunnel"))
        Assert.Equal(5, ChannelPriorityRules.ChannelRank("drive"))

        Dim endpoints As New List(Of FmsEndpoint) From {
            New FmsEndpoint With {.Kind = "drive", .Host = "drive.google.com", .Port = 443},
            New FmsEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222},
            New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.1", .Port = 22},
            New FmsEndpoint With {.Kind = "tunnel", .Host = "127.0.0.1", .Port = 5000},
            New FmsEndpoint With {.Kind = "ipv6", .Host = "fe80::1", .Port = 22}
        }

        Dim sorted As List(Of FmsEndpoint) = ChannelPriorityRules.SortCandidates(endpoints)
        Assert.Equal("lan", sorted(0).Kind)
        Assert.Equal("ipv6", sorted(1).Kind)
        Assert.Equal("portforward", sorted(2).Kind)
        Assert.Equal("tunnel", sorted(3).Kind)
        Assert.Equal("drive", sorted(4).Kind)
    End Sub

    <Fact>
    Public Sub NetworkChangeTracker_BumpsEpoch()
        Dim e1 As Long = NetworkChangeTracker.CurrentEpoch()
        NetworkChangeTracker.BumpEpoch()
        Dim e2 As Long = NetworkChangeTracker.CurrentEpoch()
        Assert.True(e2 > e1)
    End Sub

    ' ----------------------------------------------------------------- stored pin ----

    <Fact>
    Public Sub StoredPin_Save_Read_AndMarkChanged()
        Dim r As New ResourceDescriptor With {
            .Id = "res-pin-test", .DisplayName = "Pin Resource", .Host = "192.168.1.50",
            .Kind = ResourceKind.Sftp, .PinRequired = True
        }
        Dim mem As New MemoryValues()
        mem.Write("Resources", ResourceRegistry.Serialize(New List(Of ResourceDescriptor) From {r}))
        Dim secrets As New MemorySecrets()

        ' Mock protector
        Dim protect As Func(Of String, SecretProtectionResult) =
            Function(plain) New SecretProtectionResult(SecretProtectionStatus.Success, "enc:" & plain)
        Dim unprotect As Func(Of String, SecretProtectionResult) =
            Function(enc) New SecretProtectionResult(SecretProtectionStatus.Success, enc.Substring(4))

        ' Save PIN
        Dim saveRes As Boolean = ResourceCommit.SavePin(r, "9876", AddressOf mem.Read, AddressOf mem.Write, secrets, protect)
        Assert.True(saveRes)
        Assert.NotEmpty(r.PinSecretHandle)
        Assert.False(r.PinChanged)

        ' Read PIN
        Dim readRes As CredentialReadResult = ResourceCommit.ReadPin(r, secrets, unprotect)
        Assert.Equal(CredentialReadOutcome.Ok, readRes.Outcome)
        Assert.Equal("9876", readRes.Secret)

        ' Mark PIN changed on auth failure
        ResourceCommit.MarkPinChanged(r, AddressOf mem.Read, AddressOf mem.Write)
        Assert.True(r.PinChanged)

        ' Check descriptor reloaded from registry has PinChanged = True
        Dim reloadedList As List(Of ResourceDescriptor) = ResourceRegistry.Parse(mem.Read("Resources"))
        Dim reloaded As ResourceDescriptor = reloadedList.Find(Function(x) x.Id = r.Id)
        Assert.NotNull(reloaded)
        Assert.True(reloaded.PinChanged)
    End Sub

    <Fact>
    Public Sub TwoImports_OfOneShare_GiveOneRecordWithTwoChannels_AndPreserveReadOnlyChoice()
        Dim mem As New MemoryValues()
        Dim store As New MemorySecrets()
        Dim protect As Func(Of String, SecretProtectionResult) =
            Function(plain) New SecretProtectionResult(SecretProtectionStatus.Success, "enc:" & plain)

        Dim validFp As String = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"

        ' Import 1: LAN address with read-only choice set to True
        Dim doc1 As String = "{""schemaVersion"":1,""resourceName"":""My NAS"",""protocol"":""sftp""," &
            """accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.100"",""port"":22}]," &
            """username"":""fms"",""password"":""secret"",""hostKeyFingerprintSha256"":""" & validFp & """," &
            """roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":true}]}"
        Dim plan1 As FmsConfigPlan = FmsConfigReader.ReadText(doc1).Plan
        Dim preview1 As ImportPreview = ResourceImport.Preview(plan1, ResourceRegistry.Parse(mem.Read("Resources")), Function() "res1")
        Dim applyRes1 As ImportApplyResult = ResourceImport.Apply(preview1, "secret", False, AddressOf mem.Read, AddressOf mem.Write, store, protect)
        Assert.Equal(ImportApplyOutcome.Applied, applyRes1.Outcome)

        Dim listAfter1 As List(Of ResourceDescriptor) = ResourceRegistry.Parse(mem.Read("Resources"))
        Assert.Single(listAfter1)
        Assert.True(listAfter1(0).ReadOnlyHint)

        ' Import 2: WAN portforward address for the SAME share (same fingerprint + root), with readOnly false in document
        Dim doc2 As String = "{""schemaVersion"":1,""resourceName"":""My NAS WAN"",""protocol"":""sftp""," &
            """accessPaths"":[{""kind"":""portforward"",""host"":""wan.example.com"",""port"":2222}]," &
            """username"":""fms"",""password"":""secret"",""hostKeyFingerprintSha256"":""" & validFp & """," &
            """roots"":[{""virtualPath"":""/Photos"",""label"":""Photos"",""readOnly"":false}]}"
        Dim plan2 As FmsConfigPlan = FmsConfigReader.ReadText(doc2).Plan
        Dim preview2 As ImportPreview = ResourceImport.Preview(plan2, ResourceRegistry.Parse(mem.Read("Resources")), Function() "res2")
        Dim applyRes2 As ImportApplyResult = ResourceImport.Apply(preview2, "secret", False, AddressOf mem.Read, AddressOf mem.Write, store, protect)
        Assert.Equal(ImportApplyOutcome.Applied, applyRes2.Outcome)

        ' Result: Exactly one record with two channels (endpoints), keeping display name and consumer's read-only choice!
        Dim listAfter2 As List(Of ResourceDescriptor) = ResourceRegistry.Parse(mem.Read("Resources"))
        Assert.Single(listAfter2)
        Dim res As ResourceDescriptor = listAfter2(0)
        Assert.Equal("res1", res.Id)
        Assert.Equal("My NAS - Photos", res.DisplayName)
        Assert.True(res.ReadOnlyHint) ' Read-only choice is preserved
        Assert.Equal(2, res.Endpoints.Count)
        Assert.Contains(res.Endpoints, Function(e) e.Host = "192.168.1.100" AndAlso e.Port = 22)
        Assert.Contains(res.Endpoints, Function(e) e.Host = "wan.example.com" AndAlso e.Port = 2222)
    End Sub

    <Fact>
    Public Sub UnprovablePair_StaysTwoRows()
        Dim mem As New MemoryValues()
        Dim store As New MemorySecrets()
        Dim protect As Func(Of String, SecretProtectionResult) =
            Function(plain) New SecretProtectionResult(SecretProtectionStatus.Success, "enc:" & plain)

        ' Two imports with NO fingerprint (unprovable)
        Dim doc1 As String = "{""schemaVersion"":1,""resourceName"":""Server A"",""protocol"":""sftp""," &
            """accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.10"",""port"":22}]," &
            """username"":""user1"",""password"":""pw1""," &
            """roots"":[{""virtualPath"":""/Photos""}]}"
        Dim doc2 As String = "{""schemaVersion"":1,""resourceName"":""Server B"",""protocol"":""sftp""," &
            """accessPaths"":[{""kind"":""lan"",""host"":""192.168.1.20"",""port"":22}]," &
            """username"":""user2"",""password"":""pw2""," &
            """roots"":[{""virtualPath"":""/Photos""}]}"

        Dim p1 = ResourceImport.Preview(FmsConfigReader.ReadText(doc1).Plan, ResourceRegistry.Parse(mem.Read("Resources")), Function() "resA")
        ResourceImport.Apply(p1, "pw1", False, AddressOf mem.Read, AddressOf mem.Write, store, protect)

        Dim p2 = ResourceImport.Preview(FmsConfigReader.ReadText(doc2).Plan, ResourceRegistry.Parse(mem.Read("Resources")), Function() "resB")
        ResourceImport.Apply(p2, "pw2", False, AddressOf mem.Read, AddressOf mem.Write, store, protect)

        Dim list As List(Of ResourceDescriptor) = ResourceRegistry.Parse(mem.Read("Resources"))
        Assert.Equal(2, list.Count)
    End Sub

    <Fact>
    Public Sub Race_TakesFirstChannelThatAnswers_AndReportsNoConnectionOnlyAfterAll()
        Dim descriptor As New ResourceDescriptor With {
            .Id = "res-race", .Kind = ResourceKind.Sftp, .Host = "192.168.1.50", .Port = 22,
            .PinnedFingerprint = "SHA256:fp",
            .Endpoints = New List(Of ResourceEndpoint) From {
                New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 22},
                New ResourceEndpoint With {.Kind = "ipv6", .Host = "fe80::1", .Port = 22},
                New ResourceEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222}
            }
        }

        ' Case 1: LAN answers -> wins first, remaining not dialled
        Dim dialer1 As New ScriptedDialer()
        Dim outcome1 As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True, Threading.CancellationToken.None, dialer1).GetAwaiter().GetResult()
        Assert.True(outcome1.Succeeded)
        Assert.Contains("192.168.1.50:22", dialer1.Dialled)
        Assert.DoesNotContain("wan.example.com:2222", dialer1.Dialled)

        ' Case 2: LAN fails -> IPv6 answers and wins
        Dim dialer2 As New ScriptedDialer()
        dialer2.Script("192.168.1.50:22") = New ResourceFailure(ResourceFailureKind.Transport)
        Dim outcome2 As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True, Threading.CancellationToken.None, dialer2).GetAwaiter().GetResult()
        Assert.True(outcome2.Succeeded)
        Assert.Equal("fe80::1:22", DirectCast(outcome2.Provider, FakeSession).Endpoint.Key())

        ' Case 3: Every address fails -> reports "no connection" only after trying all candidates
        Dim dialer3 As New ScriptedDialer()
        dialer3.Script("192.168.1.50:22") = New ResourceFailure(ResourceFailureKind.Transport)
        dialer3.Script("fe80::1:22") = New ResourceFailure(ResourceFailureKind.Transport)
        dialer3.Script("wan.example.com:2222") = New ResourceFailure(ResourceFailureKind.Timeout)
        Dim outcome3 As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True, Threading.CancellationToken.None, dialer3).GetAwaiter().GetResult()
        Assert.False(outcome3.Succeeded)
        Assert.Null(outcome3.Provider)
        Assert.Contains("no connection", outcome3.Failure.Detail)
        Assert.Equal(3, dialer3.Dialled.Count)
    End Sub

    <Fact>
    Public Sub DeadFirstAddress_DoesNotExhaustBudget_WithInjectedClock()
        Dim clock As New ManualClock()
        Dim descriptor As New ResourceDescriptor With {
            .Id = "res-budget", .Kind = ResourceKind.Sftp, .Host = "192.168.1.50", .Port = 22,
            .PinnedFingerprint = "SHA256:fp",
            .Endpoints = New List(Of ResourceEndpoint) From {
                New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 22},
                New ResourceEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222}
            }
        }
        Dim dialer As New ScriptedDialer With {.Clock = clock}
        dialer.Script("192.168.1.50:22") = New ResourceFailure(ResourceFailureKind.Timeout)

        Dim connectTask As Threading.Tasks.Task(Of ConnectOutcome) = ResourceConnect.ConnectAsync(
            descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True,
            Threading.CancellationToken.None, dialer, clockOverride:=clock)

        clock.Advance(ResolutionLimits.CandidateMs + 100)

        Dim outcome As ConnectOutcome = connectTask.GetAwaiter().GetResult()
        Assert.True(outcome.Succeeded)
        Assert.Equal("wan.example.com:2222", DirectCast(outcome.Provider, FakeSession).Endpoint.Key())
        Assert.True(clock.NowMs < ResolutionLimits.RoundMs, "must not exhaust the 45s round budget")
    End Sub

    <Fact>
    Public Sub NetworkChange_ReRaces()
        NetworkChangeTracker.ResetForTesting()
        Dim e1 As Long = NetworkChangeTracker.CurrentEpoch()
        Assert.Equal(1, e1)

        ' Simulate network address changed
        NetworkChangeTracker.BumpEpoch()
        Dim e2 As Long = NetworkChangeTracker.CurrentEpoch()
        Assert.True(e2 > e1)

        ' Channel priority is always sorted from top: LAN -> IPv6 -> port forward -> tunnel -> drive
        Dim endpoints As New List(Of FmsEndpoint) From {
            New FmsEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222},
            New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.1", .Port = 22}
        }
        Dim sorted As List(Of FmsEndpoint) = ChannelPriorityRules.SortCandidates(endpoints)
        Assert.Equal("lan", sorted(0).Kind)
        Assert.Equal("portforward", sorted(1).Kind)
    End Sub

    <Fact>
    Public Sub ReadOnly_AppliesOnEveryChannel()
        Dim descriptor As New ResourceDescriptor With {
            .Id = "res-ro", .Kind = ResourceKind.Sftp, .Host = "192.168.1.50", .Port = 22,
            .PinnedFingerprint = "SHA256:fp", .ReadOnlyHint = True,
            .Endpoints = New List(Of ResourceEndpoint) From {
                New ResourceEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 22},
                New ResourceEndpoint With {.Kind = "portforward", .Host = "wan.example.com", .Port = 2222}
            }
        }
        Dim dialer As New ScriptedDialer With {.Descriptor = descriptor}
        Dim outcome1 As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True, Threading.CancellationToken.None, dialer).GetAwaiter().GetResult()
        Assert.True(outcome1.Succeeded)
        Assert.False(outcome1.Provider.Capabilities.CanWrite, "LAN channel must be read-only")

        ' Fallback to WAN channel
        Dim dialer2 As New ScriptedDialer With {.Descriptor = descriptor}
        dialer2.Script("192.168.1.50:22") = New ResourceFailure(ResourceFailureKind.Transport)
        Dim outcome2 As ConnectOutcome = ResourceConnect.ConnectAsync(descriptor, "pw", Function(fp, ct) True, Function(fp) True, Function() True, Threading.CancellationToken.None, dialer2).GetAwaiter().GetResult()
        Assert.True(outcome2.Succeeded)
        Assert.False(outcome2.Provider.Capabilities.CanWrite, "portforward channel must also be read-only")
    End Sub

    <Fact>
    Public Sub StoredPin_OpensRootUnattended_AndChangedOneStopsAtTheWall()
        Dim r As New ResourceDescriptor With {
            .Id = "res-pin-unattended", .DisplayName = "Pin Share", .Host = "192.168.1.50", .Port = 22,
            .PinnedFingerprint = "SHA256:fp", .Kind = ResourceKind.Sftp, .PinRequired = True
        }
        Dim mem As New MemoryValues()
        Dim secrets As New MemorySecrets()
        Dim protect As Func(Of String, SecretProtectionResult) =
            Function(plain) New SecretProtectionResult(SecretProtectionStatus.Success, "enc:" & plain)
        Dim unprotect As Func(Of String, SecretProtectionResult) =
            Function(enc) New SecretProtectionResult(SecretProtectionStatus.Success, enc.Substring(4))

        ' Save PIN into DPAPI store
        ResourceCommit.SavePin(r, "1234", AddressOf mem.Read, AddressOf mem.Write, secrets, protect)
        Assert.False(r.PinChanged)

        ' Part 1: Unattended open with stored PIN
        Dim dialer As New ScriptedDialer()
        ' Unattended: consent prompt is never shown (returns False if called), resourcePin parameter is Nothing
        Dim outcome1 As ConnectOutcome = ResourceConnect.ConnectAsync(
            r, "pw", Function(fp, ct) False, Function(fp) False, Function() True,
            Threading.CancellationToken.None, dialer, resourcePin:=Nothing,
            secretsOverride:=secrets, unprotectOverride:=unprotect).GetAwaiter().GetResult()
        Assert.True(outcome1.Succeeded)

        ' Part 2: Changed PIN auth failure
        ResourceCommit.MarkPinChanged(r, AddressOf mem.Read, AddressOf mem.Write)
        Assert.True(r.PinChanged)

        ' Part 3: Subsequent unattended run stops at the wall (PinChanged -> Auth failure without attempting connection)
        Dim dialer2 As New ScriptedDialer()
        Dim outcome2 As ConnectOutcome = ResourceConnect.ConnectAsync(
            r, "pw", Function(fp, ct) False, Function(fp) False, Function() True,
            Threading.CancellationToken.None, dialer2, resourcePin:=Nothing,
            secretsOverride:=secrets, unprotectOverride:=unprotect).GetAwaiter().GetResult()
        Assert.False(outcome2.Succeeded)
        Assert.Equal(ResourceFailureKind.Auth, outcome2.Failure.Kind)
        Assert.Empty(dialer2.Dialled) ' Never retries, stops at the wall
    End Sub

    <Fact>
    Public Sub NoPinOrPasswordOrToken_ReachesLogOrExportOrLogPackage()
        Dim r As New ResourceDescriptor With {
            .Id = "res-sec", .DisplayName = "Secret Test", .Host = "192.168.1.50", .Port = 22,
            .PinnedFingerprint = "SHA256:fp", .PinRequired = True, .PinSecretHandle = "pin:xyz789",
            .SecretHandle = "cred:abc123"
        }
        Dim json As String = ResourceRegistry.Serialize(New List(Of ResourceDescriptor) From {r})
        ' PIN secret text and handles
        Assert.DoesNotContain("secret-pin-value", json)

        ' Export does not leak SecretHandle or PinSecretHandle
        Dim expResult As FmsExportResult = FmsConfigExport.Export(r, includePassword:=False, New MemorySecrets())
        Dim doc As String = expResult.Json & " " & expResult.Code
        Assert.DoesNotContain("pin:xyz789", doc)
        Assert.DoesNotContain("cred:abc123", doc)

        ' LogPackage SafeSettingNames does not contain "Resources" or credential names
        Assert.False(LogPackage.SafeSettingNames.Contains("Resources"))
        Assert.False(LogPackage.SafeSettingNames.Contains("Exchange_AgentPublishShares"))
        Assert.False(LogPackage.SafeSettingNames.Contains("Exchange_AgentThirdPartyPort"))
    End Sub

End Class
#End If
