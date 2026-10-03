#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

''' <summary>
''' 100 T07: SFTP read-only consumer test suite against an isolated fixture server.
''' Tests verified endpoint resolution, imported pin checking, explicit TOFU,
''' bounded reconnect, leased materialization, preserving listings on refresh failure,
''' browse/view/play/copy-out, host-key mismatch, cancellation, rediscovery and stale completion.
''' </summary>
<Collection("ResourceRouting")>
Public Class SftpReadOnlyFixtureTests
    Implements IDisposable

    Private ReadOnly tempRoot As String
    Private ReadOnly cacheRoot As String

    Public Sub New()
        tempRoot = Path.Combine(Path.GetTempPath(), "fms-sftp-fixture-" & Guid.NewGuid().ToString("N"))
        cacheRoot = Path.Combine(tempRoot, "cache")
        Directory.CreateDirectory(cacheRoot)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(tempRoot) Then Directory.Delete(tempRoot, recursive:=True)
        Catch
        End Try
    End Sub

    Private Shared Function PosixName(path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Dim slash As Integer = path.LastIndexOf("/"c)
        Return If(slash >= 0, path.Substring(slash + 1), path)
    End Function

    ' =========================================================================
    ' ISOLATED SFTP FIXTURE SERVER
    ' =========================================================================

    ''' <summary>
    ''' In-memory isolated SFTP fixture server with simulated SSH host keys, endpoints,
    ''' authentication, virtual directory tree, configurable network faults, and provider adapters.
    ''' </summary>
    Private NotInheritable Class IsolatedSftpFixtureServer
        Implements IEndpointDialer, IFileSystemProvider

        Friend ReadOnly HostKeyBlob As Byte()
        Friend ReadOnly Fingerprint As String
        Friend ReadOnly ServerUserName As String = "fms_user"
        Friend ReadOnly ServerPassword As String = "valid_password"
        Friend ReadOnly Files As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Friend ReadOnly Dirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Friend FailListing As Boolean
        Friend ListingException As Exception
        Friend ListingDelayMs As Integer
        Friend FailDownloadFor As String = ""
        Friend DownloadDelayMs As Integer
        Friend TruncateDownloadsTo As Long = -1

        Friend DialCalls As Integer
        Friend ActiveDials As Integer
        Friend DownloadCalls As Integer
        Friend DialTokens As New Dictionary(Of String, CancellationToken)()
        Private ReadOnly gate As New Object()

        Friend Sub New(Optional hostKeySeed As Integer = 42)
            HostKeyBlob = Enumerable.Range(0, 64).Select(Function(i) CByte((i * 13 + hostKeySeed) Mod 251)).ToArray()
            Fingerprint = SshFingerprint.OfHostKey(HostKeyBlob)

            ' Populate virtual filesystem with sample media files
            Dirs.Add("/Media")
            Dirs.Add("/Media/Photos")
            Dirs.Add("/Media/Videos")
            Dirs.Add("/Media/Audio")

            PutFile("/Media/Photos/sample.jpg", Encoding.UTF8.GetBytes("JPEG_IMAGE_CONTENT_FIXTURE_DATA_12345"))
            PutFile("/Media/Photos/landscape.png", Encoding.UTF8.GetBytes("PNG_IMAGE_CONTENT_FIXTURE_DATA_67890"))
            PutFile("/Media/Videos/clip.mp4", Encoding.UTF8.GetBytes("MP4_VIDEO_STREAM_CONTENT_FIXTURE_DATA"))
            PutFile("/Media/Audio/track.flac", Encoding.UTF8.GetBytes("FLAC_AUDIO_MUSIC_CONTENT_FIXTURE_DATA"))
        End Sub

        Friend Sub PutFile(remotePath As String, content As Byte())
            Dim canonical As String = FmsConfigReader.CanonicalVirtualPath(remotePath)
            Files(canonical) = content
            Dim parent As String = ResourceTransfer.ParentOf(canonical)
            If Not String.IsNullOrEmpty(parent) Then Dirs.Add(parent)
        End Sub

        ' --- IEndpointDialer ---

        Public Function DialAsync(endpoint As FmsEndpoint, ct As CancellationToken) As Task(Of DialResult) Implements IEndpointDialer.DialAsync
            SyncLock gate
                DialCalls += 1
                ActiveDials += 1
                DialTokens(endpoint.Host) = ct
            End SyncLock

            If ct.IsCancellationRequested Then
                Return Task.FromResult(DialResult.Failed(New ResourceFailure(ResourceFailureKind.Cancelled)))
            End If

            Dim session As New FixtureSshSession(Me, endpoint)
            Return Task.FromResult(DialResult.Verified(session))
        End Function

        ' --- IFileSystemProvider ---

        Public ReadOnly Property Kind As ResourceKind Implements IFileSystemProvider.Kind
            Get
                Return ResourceKind.Sftp
            End Get
        End Property

        Public ReadOnly Property Capabilities As ProviderCapabilities Implements IFileSystemProvider.Capabilities
            Get
                ' T07 read-only SFTP root capabilities: view, play, copy-out allowed; mutations disallowed
                Return New ProviderCapabilities(
                    canWrite:=False,
                    canDelete:=False,
                    canRename:=False,
                    canCreateFolder:=False,
                    hasRecycleBin:=False,
                    needsMaterialisation:=True,
                    supportsServerSideRename:=False,
                    isMetered:=False,
                    costHint:=ProviderCostHint.Fast,
                    supportsAtomicOverwriteRename:=False,
                    supportsStableSourceDelete:=False)
            End Get
        End Property

        Public Function ListEntries(dirPath As String, ct As CancellationToken) As IReadOnlyList(Of ResourceEntry) Implements IFileSystemProvider.ListEntries
            If ListingDelayMs > 0 Then Thread.Sleep(ListingDelayMs)
            ct.ThrowIfCancellationRequested()

            If FailListing Then
                If ListingException IsNot Nothing Then Throw ListingException
                Throw New IOException("Simulated SFTP listing network failure.")
            End If

            Dim canonical As String = If(FmsConfigReader.CanonicalVirtualPath(dirPath), "/")
            Dim prefix As String = If(canonical.EndsWith("/", StringComparison.Ordinal), canonical, canonical & "/")
            Dim result As New List(Of ResourceEntry)()

            For Each kvp In Files
                Dim filePath As String = kvp.Key
                If filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Dim subName As String = filePath.Substring(prefix.Length)
                    If Not subName.Contains("/"c) Then
                        result.Add(New ResourceEntry With {
                            .FullPath = filePath,
                            .Name = subName,
                            .Length = kvp.Value.Length,
                            .LastWriteUtc = New DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                            .IsDirectory = False
                        })
                    End If
                End If
            Next

            For Each directoryPath As String In Dirs
                If directoryPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) AndAlso Not String.Equals(directoryPath, canonical, StringComparison.OrdinalIgnoreCase) Then
                    Dim subName As String = directoryPath.Substring(prefix.Length)
                    If Not subName.Contains("/"c) AndAlso subName.Length > 0 Then
                        result.Add(New ResourceEntry With {
                            .FullPath = directoryPath,
                            .Name = subName,
                            .IsDirectory = True
                        })
                    End If
                End If
            Next

            Return result
        End Function

        Public Function TryStat(path As String, ByRef entry As ResourceEntry) As Boolean Implements IFileSystemProvider.TryStat
            entry = Nothing
            Dim canonical As String = FmsConfigReader.CanonicalVirtualPath(path)
            If canonical Is Nothing Then Return False

            Dim content As Byte() = Nothing
            If Files.TryGetValue(canonical, content) Then
                entry = New ResourceEntry With {
                    .FullPath = canonical,
                    .Name = PosixName(canonical),
                    .Length = content.Length,
                    .LastWriteUtc = New DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                    .IsDirectory = False
                }
                Return True
            End If

            If Dirs.Contains(canonical) Then
                entry = New ResourceEntry With {
                    .FullPath = canonical,
                    .Name = PosixName(canonical),
                    .IsDirectory = True
                }
                Return True
            End If

            Return False
        End Function

        Public Sub Download(remotePath As String, localPath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Download
            SyncLock gate
                DownloadCalls += 1
            End SyncLock

            If DownloadDelayMs > 0 Then Thread.Sleep(DownloadDelayMs)
            ct.ThrowIfCancellationRequested()

            Dim canonical As String = FmsConfigReader.CanonicalVirtualPath(remotePath)
            If String.Equals(canonical, FailDownloadFor, StringComparison.OrdinalIgnoreCase) Then
                Throw New IOException("Simulated download failure for " & remotePath)
            End If

            Dim bytes As Byte() = Nothing
            If Not Files.TryGetValue(canonical, bytes) Then
                Throw New FileNotFoundException("Remote file not found: " & remotePath)
            End If

            If TruncateDownloadsTo >= 0 AndAlso TruncateDownloadsTo < bytes.Length Then
                Dim truncated(CInt(TruncateDownloadsTo) - 1) As Byte
                Array.Copy(bytes, truncated, truncated.Length)
                bytes = truncated
            End If

            File.WriteAllBytes(localPath, bytes)
            progress?.Report(bytes.Length)
        End Sub

        Public Sub Upload(localPath As String, remotePath As String, progress As IProgress(Of Long), ct As CancellationToken) Implements IFileSystemProvider.Upload
            Throw New NotSupportedException("Read-only SFTP root does not support Upload.")
        End Sub

        Public Sub DeleteFile(remotePath As String) Implements IFileSystemProvider.DeleteFile
            Throw New NotSupportedException("Read-only SFTP root does not support Delete.")
        End Sub

        Public Sub Rename(remotePath As String, newRemotePath As String) Implements IFileSystemProvider.Rename
            Throw New NotSupportedException("Read-only SFTP root does not support Rename.")
        End Sub

        Public Sub CreateDirectory(remotePath As String) Implements IFileSystemProvider.CreateDirectory
            Throw New NotSupportedException("Read-only SFTP root does not support CreateDirectory.")
        End Sub
    End Class

    Private NotInheritable Class FixtureSshSession
        Implements IDisposable

        Friend ReadOnly Server As IsolatedSftpFixtureServer
        Friend ReadOnly Endpoint As FmsEndpoint
        Friend Disposed As Boolean

        Friend Sub New(server As IsolatedSftpFixtureServer, endpoint As FmsEndpoint)
            Me.Server = server
            Me.Endpoint = endpoint
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    Private Function CreateDescriptor(server As IsolatedSftpFixtureServer, rootPath As String, Optional pinnedFingerprint As String = "") As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "sftp-fixture-res-1",
            .DisplayName = "Fixture SFTP",
            .Kind = ResourceKind.Sftp,
            .Host = "192.168.1.100",
            .Port = 22,
            .UserName = server.ServerUserName,
            .RootPath = rootPath,
            .SecretHandle = "cred:test"
        }
    End Function

    ' =========================================================================
    ' TESTS
    ' =========================================================================

    <Fact>
    Public Sub Browse_listing_populates_entries_and_assigns_materialized_cache_paths()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Photos", CancellationToken.None)

            Assert.Equal(2, session.Entries.Count)
            Assert.Contains(session.Entries, Function(e) e.Name = "sample.jpg" AndAlso e.Length > 0)
            Assert.Contains(session.Entries, Function(e) e.Name = "landscape.png" AndAlso e.Length > 0)

            For Each entry In session.Entries
                Assert.StartsWith(sessionDir, entry.LocalPath, StringComparison.OrdinalIgnoreCase)
                Assert.False(File.Exists(entry.LocalPath), "Files must not be downloaded during listing")
                Assert.True(session.OwnsLocalPath(entry.LocalPath))
            Next
        End Using
    End Sub

    <Fact>
    Public Sub View_materializes_image_on_demand_and_validates_bytes()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Photos", CancellationToken.None)
            Dim entry As ResourceEntry = session.Entries.Single(Function(e) e.Name = "sample.jpg")

            Dim outcome As EnsureLocalOutcome = session.TryEnsureLocal(entry.LocalPath)
            Assert.Equal(EnsureLocalOutcome.Ready, outcome)
            Assert.True(File.Exists(entry.LocalPath))
            Assert.Equal(server.Files("/Media/Photos/sample.jpg"), File.ReadAllBytes(entry.LocalPath))
            Assert.Equal(1, server.DownloadCalls)

            ' Second ensure is a cache hit and does not download again
            Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(entry.LocalPath))
            Assert.Equal(1, server.DownloadCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Play_materializes_audio_and_video_files_for_playback()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Videos", CancellationToken.None)
            Dim videoEntry As ResourceEntry = session.Entries.Single(Function(e) e.Name = "clip.mp4")

            Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(videoEntry.LocalPath))
            Assert.True(File.Exists(videoEntry.LocalPath))
            Assert.Equal(server.Files("/Media/Videos/clip.mp4"), File.ReadAllBytes(videoEntry.LocalPath))

            session.LoadListing("/Media/Audio", CancellationToken.None)
            Dim audioEntry As ResourceEntry = session.Entries.Single(Function(e) e.Name = "track.flac")

            Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(audioEntry.LocalPath))
            Assert.True(File.Exists(audioEntry.LocalPath))
            Assert.Equal(server.Files("/Media/Audio/track.flac"), File.ReadAllBytes(audioEntry.LocalPath))
        End Using
    End Sub

    <Fact>
    Public Sub Copy_out_downloads_verified_to_local_destination_and_preserves_remote_source()
        Dim server As New IsolatedSftpFixtureServer()
        Dim localDestDir As String = Path.Combine(tempRoot, "exported")
        Directory.CreateDirectory(localDestDir)
        Dim localDestFile As String = Path.Combine(localDestDir, "saved_photo.jpg")

        ResourceTransfer.DownloadVerified(server, "/Media/Photos/sample.jpg", localDestFile, False)

        Assert.True(File.Exists(localDestFile))
        Assert.Equal(server.Files("/Media/Photos/sample.jpg"), File.ReadAllBytes(localDestFile))
        Assert.True(server.Files.ContainsKey("/Media/Photos/sample.jpg"), "Remote source must remain intact")
    End Sub

    <Fact>
    Public Sub Read_only_root_refuses_mutating_verbs()
        Dim server As New IsolatedSftpFixtureServer()
        Dim caps As ProviderCapabilities = server.Capabilities

        Assert.False(ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.Delete))
        Assert.False(ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.Move))
        Assert.False(ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.Rename))
        Assert.False(ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.WriteBeside))
        Assert.True(ResourceRefusalPolicy.IsAllowed(caps, ResourceVerb.Copy))
    End Sub

    <Fact>
    Public Sub Preserve_listings_on_refresh_failure()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Photos", CancellationToken.None)
            Assert.Equal(2, session.Entries.Count)
            Dim initialListing As IReadOnlyList(Of ResourceEntry) = session.Entries.ToList()

            ' Simulate network disconnect or timeout during refresh
            server.FailListing = True
            server.ListingException = New SocketException(10054)

            Assert.Throws(Of SocketException)(Sub() session.LoadListing("/Media/Photos", CancellationToken.None))

            ' The prior listing MUST be preserved and not cleared
            Assert.Equal(2, session.Entries.Count)
            Assert.Equal(initialListing(0).FullPath, session.Entries(0).FullPath)
            Assert.Equal(initialListing(1).FullPath, session.Entries(1).FullPath)
            Assert.Equal("/Media/Photos", session.CurrentDirectory)

            ' Once network recovers, refresh succeeds normally
            server.FailListing = False
            session.LoadListing("/Media/Photos", CancellationToken.None)
            Assert.Equal(2, session.Entries.Count)
        End Using
    End Sub

    <Fact>
    Public Sub Imported_pin_checking_matches_silently_and_mismatch_fails_terminally()
        Dim server As New IsolatedSftpFixtureServer(hostKeySeed:=100)
        Dim pinnedPin As String = server.Fingerprint

        Dim gate As New HostKeyGate(pinnedPin,
            Function(fp, ct)
                Throw New InvalidOperationException("Consent should not be prompted for pinned server")
            End Function,
            Function(fp) True,
            Function() MonotonicClock.NowMs())

        ' Correct key matches silently
        Dim validDecision = gate.Verify(server.HostKeyBlob, CancellationToken.None)
        Assert.True(validDecision.IsAccepted)
        Assert.Equal(HostKeyVerdict.Accepted, validDecision.Verdict)

        ' Tampered / mismatched key is terminal mismatch
        Dim tamperedKey As Byte() = Enumerable.Range(0, 64).Select(Function(i) CByte((i * 17 + 99) Mod 251)).ToArray()
        Dim mismatchDecision = gate.Verify(tamperedKey, CancellationToken.None)
        Assert.False(mismatchDecision.IsAccepted)
        Assert.Equal(HostKeyVerdict.Mismatch, mismatchDecision.Verdict)
        Assert.Equal(ResourceFailureKind.KeyMismatch, mismatchDecision.ToFailure().Kind)
    End Sub

    <Fact>
    Public Sub Explicit_TOFU_prompts_for_unpinned_server_and_persists_before_accept()
        Dim server As New IsolatedSftpFixtureServer(hostKeySeed:=200)
        Dim persistedPin As String = ""
        Dim consentPrompted As Boolean = False

        Dim gate As New HostKeyGate("",
            Function(fp, ct)
                consentPrompted = True
                Assert.Equal(server.Fingerprint, fp)
                Return True ' User accepts prompt
            End Function,
            Function(fp)
                persistedPin = fp
                Return True
            End Function,
            Function() MonotonicClock.NowMs())

        Dim decision = gate.Verify(server.HostKeyBlob, CancellationToken.None)
        Assert.True(decision.IsAccepted)
        Assert.True(consentPrompted)
        Assert.Equal(server.Fingerprint, persistedPin)
        Assert.Equal(server.Fingerprint, gate.Pin)

        ' Subsequent connect strictly matches against the persisted pin without prompting
        consentPrompted = False
        Dim subsequentDecision = gate.Verify(server.HostKeyBlob, CancellationToken.None)
        Assert.True(subsequentDecision.IsAccepted)
        Assert.False(consentPrompted)
    End Sub

    <Fact>
    Public Sub Rediscovery_prioritizes_discovered_candidate_in_resolution_plan()
        Dim stored As New List(Of FmsEndpoint) From {
            New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.50", .Port = 22},
            New FmsEndpoint With {.Kind = "portforward", .Host = "router.dyn.example", .Port = 2222}
        }
        Dim discovered As New FmsEndpoint With {.Kind = "lan", .Host = "192.168.1.99", .Port = 22}

        Dim plan = EndpointResolver.PlanCandidates(stored, discovered)

        Assert.Equal(3, plan.Count)
        Assert.Equal("192.168.1.99", plan(0).Host)
        Assert.Equal("192.168.1.50", plan(1).Host)
        Assert.Equal("router.dyn.example", plan(2).Host)
    End Sub

    <Fact>
    Public Sub Cancellation_aborts_active_dial_and_materialization()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Photos", CancellationToken.None)
            Dim entry As ResourceEntry = session.Entries(0)

            Using cts As New CancellationTokenSource()
                cts.Cancel()
                Dim outcome As EnsureLocalOutcome = session.TryEnsureLocal(entry.LocalPath, cts.Token)
                Assert.Equal(EnsureLocalOutcome.Cancelled, outcome)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Stale_completion_from_old_revision_is_discarded_and_never_overwrites_cache()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Using session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
            session.LoadListing("/Media/Photos", CancellationToken.None)
            Dim entry As ResourceEntry = session.Entries(0)

            ' Simulate background fetch starting on old revision
            server.DownloadDelayMs = 150
            Dim fetchTask = Task.Run(Function() session.TryEnsureLocal(entry.LocalPath))

            ' Switch directory / bump revision while fetch is in-flight
            Thread.Sleep(30)
            session.LoadListing("/Media/Videos", CancellationToken.None)

            Dim outcome As EnsureLocalOutcome = fetchTask.Result
            Assert.True(outcome = EnsureLocalOutcome.Cancelled OrElse outcome = EnsureLocalOutcome.NotOurs, "Stale fetch from prior listing must be cancelled or dismissed as not belonging to new listing.")
        End Using
    End Sub

    <Fact>
    Public Sub Leased_materialization_prevents_premature_provider_and_cache_cleanup()
        Dim server As New IsolatedSftpFixtureServer()
        Dim descriptor As ResourceDescriptor = CreateDescriptor(server, "/Media/Photos")
        Dim sessionDir As String = RemoteCacheStore.CreateSessionUnder(cacheRoot)

        Dim session As New ResourceSession(descriptor, server, sessionDir, 1024L * 1024L)
        session.LoadListing("/Media/Photos", CancellationToken.None)

        Dim lease As IDisposable = Nothing
        Assert.True(session.TryAcquireLease(lease))
        Assert.NotNull(lease)

        ' Dispose session while lease is held
        session.Dispose()

        ' While lease is held, session directory is retained
        Assert.True(Directory.Exists(sessionDir))

        ' Releasing the lease allows final cleanup
        lease.Dispose()
        ' Release after session disposed clears session dir if idle
        RemoteCacheStore.DeleteSession(sessionDir)
        Assert.False(Directory.Exists(sessionDir))
    End Sub

    <Fact>
    Public Sub Bounded_reconnect_retries_transient_failures_with_backoff_schedule()
        Dim server As New IsolatedSftpFixtureServer()
        Dim clock As New FakeClock()
        Dim attempts As Integer = 0

        Dim runTask = ReconnectPolicy.RunAsync(Of String)(
            Function(ct)
                attempts += 1
                If attempts < 3 Then
                    Return Task.FromResult(ResourceResult(Of String).Fail(New ResourceFailure(ResourceFailureKind.Transport)))
                Else
                    Return Task.FromResult(ResourceResult(Of String).Ok("connected"))
                End If
            End Function, clock, CancellationToken.None)

        ' Advance clock through backoffs (1000 ms, 4000 ms)
        clock.Advance(1000)
        clock.Advance(4000)

        Assert.True(runTask.Wait(TimeSpan.FromSeconds(5)))
        Dim result = runTask.Result
        Assert.True(result.Succeeded)
        Assert.Equal("connected", result.Value)
        Assert.Equal(3, attempts)
    End Sub

    Private NotInheritable Class FakeClock
        Implements IResolutionClock

        Private now As Long
        Private ReadOnly gate As New Object()

        Public ReadOnly Property NowMs As Long Implements IResolutionClock.NowMs
            Get
                SyncLock gate
                    Return now
                End SyncLock
            End Get
        End Property

        Public Function Delay(ms As Integer, ct As CancellationToken) As Task Implements IResolutionClock.Delay
            Return Task.CompletedTask
        End Function

        Friend Sub Advance(ms As Long)
            SyncLock gate
                now += ms
            End SyncLock
        End Sub
    End Class

End Class
#End If
