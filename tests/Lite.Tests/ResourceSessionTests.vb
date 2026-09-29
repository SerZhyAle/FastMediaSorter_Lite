#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports Xunit

''' <summary>
''' Acceptance 2 and 5 of 006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md, plus the
''' budget (PV-3) and the prefetch depth (PV-6): the open resource's lifecycle, the orphan
''' sweep, and the fact that a file is fetched - once, complete - before anything can ask
''' whether it exists. In the "ResourceRouting" collection: entering a session attaches it
''' to FileManager's routing, which is one per process.
''' </summary>
<Collection("ResourceRouting")>
Public Class ResourceSessionTests
    Implements IDisposable

    Private ReadOnly workDir As String
    Private ReadOnly cacheRoot As String

    Public Sub New()
        workDir = Path.Combine(Path.GetTempPath(), "fms-resource-tests-" & Guid.NewGuid().ToString("N"))
        cacheRoot = Path.Combine(workDir, "remote-cache")
        Directory.CreateDirectory(cacheRoot)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(workDir) Then Directory.Delete(workDir, recursive:=True)
        Catch
        End Try
    End Sub

    Private Function NewProvider(Optional name As String = "remote",
                                 Optional capabilities As ProviderCapabilities = Nothing) As ResourceTestProvider
        Dim provider As New ResourceTestProvider(Path.Combine(workDir, name), capabilities)
        provider.PutFile("/MOV/a.jpg", New Byte(99) {})
        provider.PutFile("/MOV/b.jpg", New Byte(199) {})
        provider.PutFile("/MOV/c.jpg", New Byte(299) {})
        provider.CreateDirectory("/MOV/sub")
        Return provider
    End Function

    Private Function Descriptor() As ResourceDescriptor
        Return New ResourceDescriptor With {
            .Id = "r1", .DisplayName = "NAS", .Kind = ResourceKind.Sftp,
            .Host = "192.168.1.100", .Port = 55259, .UserName = "fms", .RootPath = "/MOV"}
    End Function

    Private Function OpenSession(provider As IFileSystemProvider, Optional maxBytes As Long = 1024L * 1024L) As ResourceSession
        Dim session As New ResourceSession(Descriptor(), provider, RemoteCacheStore.CreateSessionUnder(cacheRoot), maxBytes)
        session.LoadListing("/MOV", CancellationToken.None)
        Return session
    End Function

    ' ------------------------------------------------------------ acceptance 2 ----

    <Fact>
    Public Sub Entering_a_second_resource_disposes_the_first_and_deletes_its_directory()
        Dim slot As New ResourceSessionSlot()
        Dim first As ResourceSession = OpenSession(NewProvider("one"))
        slot.Enter(first)
        Assert.Equal(EnsureLocalOutcome.Ready, first.TryEnsureLocal(first.Entries(0).LocalPath))
        Assert.True(Directory.Exists(first.SessionDir))

        Dim released As Integer = 0
        Dim second As ResourceSession = OpenSession(NewProvider("two"))
        slot.Enter(second, Sub() released += 1)

        Assert.Equal(1, released)
        Assert.False(Directory.Exists(first.SessionDir))
        Assert.True(first.Cancellation.IsCancellationRequested)
        Assert.Same(second, slot.Current)
        ' A disposed session fetches nothing any more.
        Assert.Equal(EnsureLocalOutcome.Cancelled, first.TryEnsureLocal(first.Entries(1).LocalPath))
        slot.Leave()
    End Sub

    <Fact>
    Public Sub Leaving_deletes_the_session_directory_and_is_safe_twice()
        Dim slot As New ResourceSessionSlot()
        Dim session As ResourceSession = OpenSession(NewProvider())
        slot.Enter(session)
        session.TryEnsureLocal(session.Entries(0).LocalPath)

        slot.Leave()
        Assert.False(slot.IsOpen)
        Assert.False(Directory.Exists(session.SessionDir))
        slot.Leave()
    End Sub

    <Fact>
    Public Sub The_sweep_takes_a_dead_owner_and_keeps_a_live_sibling()
        Dim dead As String = Path.Combine(cacheRoot, RemoteCachePaths.SessionDirName(4242, "deadbeef"))
        Dim live As String = Path.Combine(cacheRoot, RemoteCachePaths.SessionDirName(5151, "cafebabe"))
        Dim stale As String = Path.Combine(cacheRoot, RemoteCachePaths.SessionDirName(5151, "0badf00d"))
        Dim foreign As String = Path.Combine(cacheRoot, "not-ours")
        For Each d As String In {dead, live, stale, foreign}
            Directory.CreateDirectory(d)
            File.WriteAllText(Path.Combine(d, "00001_x.jpg"), "x")
        Next
        Directory.SetLastWriteTimeUtc(stale, Date.UtcNow.AddHours(-30))

        ' 5151 plays the live sibling viewer (the x86 exe beside us); 4242 is gone.
        RemoteCacheStore.SweepRoot(cacheRoot, Date.UtcNow, Function(pid) pid = 5151)

        Assert.False(Directory.Exists(dead))
        Assert.True(Directory.Exists(live))
        Assert.False(Directory.Exists(stale))   ' past a day: pids are reused
        Assert.True(Directory.Exists(foreign))  ' never touch what is not ours
    End Sub

    ' ------------------------------------------------------------ acceptance 5 ----

    <Fact>
    Public Sub A_file_is_fetched_before_it_exists_and_only_once()
        Dim provider As ResourceTestProvider = NewProvider()
        Dim session As ResourceSession = OpenSession(provider)
        Dim entry As ResourceEntry = session.Entries.Single(Function(e) e.Name = "b.jpg")

        Assert.False(File.Exists(entry.LocalPath))
        Assert.Equal(0, provider.DownloadCalls)

        Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(entry.LocalPath))
        Assert.True(File.Exists(entry.LocalPath))
        Assert.Equal(200L, New FileInfo(entry.LocalPath).Length)
        Assert.Equal(1, provider.DownloadCalls)

        Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(entry.LocalPath))
        Assert.Equal(1, provider.DownloadCalls)
        session.Dispose()
    End Sub

    <Fact>
    Public Sub The_listing_holds_files_only_named_inside_the_session()
        Dim session As ResourceSession = OpenSession(NewProvider())
        Assert.Equal(3, session.Entries.Count)
        For Each entry As ResourceEntry In session.Entries
            Assert.Equal(session.SessionDir, Path.GetDirectoryName(entry.LocalPath))
            Assert.True(session.OwnsLocalPath(entry.LocalPath))
        Next
        Assert.Equal("sftp://fms@192.168.1.100:55259/MOV", session.Locator)
        session.Dispose()
    End Sub

    <Fact>
    Public Sub A_truncated_download_never_gets_the_final_name()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.TruncateDownloadsTo = 10
        Dim session As ResourceSession = OpenSession(provider)
        Dim entry As ResourceEntry = session.Entries(0)

        Assert.Equal(EnsureLocalOutcome.Failed, session.TryEnsureLocal(entry.LocalPath))
        Assert.False(File.Exists(entry.LocalPath))
        Assert.False(File.Exists(entry.LocalPath & FileManager.Partial_Move_Suffix))
        session.Dispose()
    End Sub

    <Fact>
    Public Sub A_path_that_is_not_ours_is_left_alone()
        Dim session As ResourceSession = OpenSession(NewProvider())
        Assert.Equal(EnsureLocalOutcome.NotOurs, session.TryEnsureLocal("C:\photos\a.jpg"))
        session.Dispose()
    End Sub

    <Fact>
    Public Sub A_local_provider_materialises_nothing()
        Dim folder As String = Path.Combine(workDir, "plain")
        Directory.CreateDirectory(folder)
        File.WriteAllBytes(Path.Combine(folder, "a.jpg"), New Byte(9) {})
        Dim session As New ResourceSession(Nothing, New LocalProvider(Function() False), "", 0)
        session.LoadListing(folder, CancellationToken.None)

        Assert.False(session.NeedsMaterialisation)
        Assert.Equal(Path.Combine(folder, "a.jpg"), session.Entries(0).LocalPath)
        Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(session.Entries(0).LocalPath))
        Assert.Equal(0L, session.MaterialisedBytes)
    End Sub

    ' ------------------------------------------------------------ budget + depth ----

    <Fact>
    Public Sub Over_budget_the_least_recently_used_goes_but_never_the_neighbours()
        Dim provider As ResourceTestProvider = NewProvider()
        provider.PutFile("/MOV/d.jpg", New Byte(399) {})
        provider.PutFile("/MOV/e.jpg", New Byte(499) {})
        ' 700 bytes: room for a couple of files, not five.
        Dim session As ResourceSession = OpenSession(provider, 700)
        Dim paths As List(Of String) = session.Entries.OrderBy(Function(e) e.Name).Select(Function(e) e.LocalPath).ToList()

        For Each p As String In paths
            Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(p))
        Next

        ' The last one touched and its neighbour survive; the oldest went.
        Assert.True(File.Exists(paths(4)))
        Assert.True(File.Exists(paths(3)))
        Assert.False(File.Exists(paths(0)))
        Assert.True(session.MaterialisedBytes <= 700 + 500)
        session.Dispose()
    End Sub

    ''' <summary>057.S07-2: the prefetch wraps like "next file" does, so after the last entry
    ''' it fetches the first - and the neighbour protection has to wrap with it, or the file
    ''' on screen is the least recently used one and goes.</summary>
    <Fact>
    Public Sub Prefetching_past_the_end_of_the_list_never_evicts_the_file_on_screen()
        Dim provider As ResourceTestProvider = NewProvider()
        Dim files As List(Of ResourceEntry) = provider.ListEntries("/MOV", CancellationToken.None).
            Where(Function(e) Not e.IsDirectory).ToList()
        Dim onScreen As Integer = files.Count - 1
        ' Room for one of the two files, not both.
        Dim session As ResourceSession = OpenSession(provider, files(onScreen).Length + files(0).Length - 1)

        Dim shown As String = session.Entries(onScreen).LocalPath
        Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(shown))
        Assert.Equal(EnsureLocalOutcome.Ready, session.TryEnsureLocal(session.Entries(0).LocalPath))

        Assert.True(File.Exists(shown))
        session.Dispose()
    End Sub

    <Fact>
    Public Sub Eviction_choice_is_lru_and_respects_protection()
        Dim sizes As New Dictionary(Of Integer, Long) From {{0, 100}, {1, 100}, {2, 100}, {3, 100}}
        Dim victims As List(Of Integer) = RemoteCacheStore.ChooseEvictions(
            New List(Of Integer) From {2, 0, 1, 3}, sizes, 400, 200, Function(i) i = 2)
        Assert.Equal(New Integer() {0, 1}, victims.ToArray())

        ' Nothing but protected entries: stop short rather than delete what is on screen.
        Assert.Empty(RemoteCacheStore.ChooseEvictions(New List(Of Integer) From {0}, sizes, 400, 100, Function(i) True))
        ' No budget means no limit.
        Assert.Empty(RemoteCacheStore.ChooseEvictions(New List(Of Integer) From {0}, sizes, 400, 0, Nothing))
    End Sub

    <Fact>
    Public Sub Prefetch_depth_follows_the_link()
        Assert.Equal(0, RemoteCacheStore.PrefetchDepth(ProviderCapabilities.LocalDisk))
        Assert.Equal(1, RemoteCacheStore.PrefetchDepth(New ProviderCapabilities()))
        Assert.Equal(3, RemoteCacheStore.PrefetchDepth(New ProviderCapabilities(costHint:=ProviderCostHint.Fast)))
        Assert.Equal(1, RemoteCacheStore.PrefetchDepth(New ProviderCapabilities(costHint:=ProviderCostHint.Fast, isMetered:=True)))
    End Sub

End Class
#End If
