#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports Xunit

<Collection("ResourceRouting")>
Public Class ResourceFollowupTests
    Implements IDisposable

    Private ReadOnly root As String = Path.Combine(Path.GetTempPath(), "fms-resource-followup-" & Guid.NewGuid().ToString("N"))

    Private Function Provider() As ResourceTestProvider
        Dim result As New ResourceTestProvider(Path.Combine(root, "remote"), New ProviderCapabilities(supportsServerSideRename:=True))
        result.PutFile("/MOV/photo.jpg", New Byte() {1, 2, 3})
        result.PutFile("/MOV/next.jpg", New Byte() {4, 5, 6})
        Return result
    End Function

    Private Function Session(remote As ResourceTestProvider) As ResourceSession
        Dim result As New ResourceSession(New ResourceDescriptor With {.Id = "r", .Host = "h", .RootPath = "/MOV"},
                                          remote, RemoteCacheStore.CreateSessionUnder(Path.Combine(root, "cache")), 100)
        result.LoadListing("/MOV", CancellationToken.None)
        Return result
    End Function

    <Fact>
    Public Sub Navigation_and_state_changes_do_not_wait_for_a_blocked_prefetch()
        Dim remote As ResourceTestProvider = Provider()
        Using opened As ResourceSession = Session(remote), started As New ManualResetEventSlim(), release As New ManualResetEventSlim()
            Dim prefetched As ResourceEntry = opened.Entries.Single(Function(e) e.Name = "photo.jpg")
            Dim shown As ResourceEntry = opened.Entries.Single(Function(e) e.Name = "next.jpg")
            Dim cancelled As CancellationToken
            remote.BeforeDownload = Sub(path, token)
                                        If path <> prefetched.FullPath Then Return
                                        cancelled = token
                                        started.Set()
                                        release.Wait(TimeSpan.FromSeconds(10))
                                    End Sub
            Dim background As Task(Of EnsureLocalOutcome) = Task.Run(Function() opened.TryEnsureLocal(prefetched.LocalPath, prefetch:=True))
            Try
                Assert.True(started.Wait(TimeSpan.FromSeconds(5)))
                Dim foreground As Task(Of EnsureLocalOutcome) = Task.Run(Function() opened.TryEnsureLocal(shown.LocalPath))
                Assert.True(foreground.Wait(TimeSpan.FromSeconds(2)), "navigation waited for unrelated prefetch")
                Assert.Equal(EnsureLocalOutcome.Ready, foreground.Result)
                Assert.True(cancelled.IsCancellationRequested)
                Dim changes As Task = Task.Run(Sub()
                                                  Assert.Equal(3L, opened.MaterialisedBytes)
                                                  opened.Invalidate(shown.LocalPath)
                                                  opened.Rekey(shown.LocalPath, "/MOV/renamed.jpg", "renamed.jpg")
                                              End Sub)
                Assert.True(changes.Wait(TimeSpan.FromSeconds(2)), "cache state waited for a download")
            Finally
                release.Set()
                Assert.True(background.Wait(TimeSpan.FromSeconds(5)))
            End Try
            Assert.Equal(EnsureLocalOutcome.Cancelled, background.Result)
            Assert.False(File.Exists(prefetched.LocalPath))
        End Using
    End Sub

    <Fact>
    Public Sub Invalidation_rejects_an_inflight_download_even_if_the_provider_ignores_cancellation()
        Dim remote As ResourceTestProvider = Provider()
        Using opened As ResourceSession = Session(remote), started As New ManualResetEventSlim(), release As New ManualResetEventSlim()
            Dim entry As ResourceEntry = opened.Entries(0)
            remote.BeforeDownload = Sub(path, token)
                                        started.Set()
                                        release.Wait(TimeSpan.FromSeconds(10))
                                    End Sub
            Dim fetch As Task(Of EnsureLocalOutcome) = Task.Run(Function() opened.TryEnsureLocal(entry.LocalPath, prefetch:=True))
            Try
                Assert.True(started.Wait(TimeSpan.FromSeconds(5)))
                opened.Invalidate(entry.LocalPath)
            Finally
                release.Set()
                Assert.True(fetch.Wait(TimeSpan.FromSeconds(5)))
            End Try
            Assert.Equal(EnsureLocalOutcome.Cancelled, fetch.Result)
            Assert.False(File.Exists(entry.LocalPath))
            Assert.Equal(0L, opened.MaterialisedBytes)
        End Using
    End Sub

    <Fact>
    Public Sub A_locked_invalidated_copy_stays_in_the_budget_and_never_returns_stale_bytes()
        Dim remote As ResourceTestProvider = Provider()
        Using opened As ResourceSession = Session(remote)
            Dim entry As ResourceEntry = opened.Entries.Single(Function(e) e.Name = "photo.jpg")
            Assert.Equal(EnsureLocalOutcome.Ready, opened.TryEnsureLocal(entry.LocalPath))
            Using held As New FileStream(entry.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                opened.Invalidate(entry.LocalPath)
                Assert.Equal(3L, opened.MaterialisedBytes)
                remote.PutFile(entry.FullPath, New Byte() {7, 8, 9})
                Assert.Equal(EnsureLocalOutcome.Failed, opened.TryEnsureLocal(entry.LocalPath))
                Assert.Equal(3L, opened.MaterialisedBytes)
            End Using
            Assert.Equal(EnsureLocalOutcome.Ready, opened.TryEnsureLocal(entry.LocalPath))
            Assert.Equal(New Byte() {7, 8, 9}, File.ReadAllBytes(entry.LocalPath))
            Assert.Equal(3L, opened.MaterialisedBytes)
        End Using
    End Sub

    <Fact>
    Public Sub A_new_listing_cannot_reuse_a_locked_old_copy_with_the_same_name_and_length()
        Dim remote As ResourceTestProvider = Provider()
        Using opened As ResourceSession = Session(remote)
            Dim entry As ResourceEntry = opened.Entries(0)
            Assert.Equal(EnsureLocalOutcome.Ready, opened.TryEnsureLocal(entry.LocalPath))
            Using held As New FileStream(entry.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                remote.PutFile(entry.FullPath, New Byte() {9, 8, 7})
                opened.LoadListing("/MOV", CancellationToken.None)
                Assert.Equal(3L, opened.MaterialisedBytes)
                Assert.Equal(EnsureLocalOutcome.Failed, opened.TryEnsureLocal(entry.LocalPath))
            End Using
            Assert.Equal(EnsureLocalOutcome.Ready, opened.TryEnsureLocal(entry.LocalPath))
            Assert.Equal(New Byte() {9, 8, 7}, File.ReadAllBytes(entry.LocalPath))
            Assert.Equal(3L, opened.MaterialisedBytes)
        End Using
    End Sub

    <Fact>
    Public Sub Case_only_rename_works_through_routing_without_deleting_the_source()
        Dim remote As ResourceTestProvider = Provider()
        Dim opened As ResourceSession = Session(remote)
        Dim slot As New ResourceSessionSlot()
        slot.Enter(opened)
        Try
            Dim entry As ResourceEntry = opened.Entries.Single(Function(e) e.Name = "photo.jpg")
            Assert.Equal(EnsureLocalOutcome.Ready, opened.TryEnsureLocal(entry.LocalPath))
            Dim renamed As String = Nothing
            Assert.True(ResourceRouting.TryRename(entry.LocalPath, "Photo.jpg", renamed))
            Assert.Equal("/MOV/Photo.jpg", opened.EntryAt(renamed).FullPath)
            Assert.Equal(New Byte() {1, 2, 3}, File.ReadAllBytes(renamed))
            Assert.Contains("Photo.jpg", Directory.GetFiles(remote.BackingPath("/MOV")).Select(AddressOf Path.GetFileName))
            Assert.Equal(0, remote.DeleteCalls)
        Finally
            slot.Leave()
        End Try
    End Sub

    <Fact>
    Public Sub Case_only_collision_on_a_sensitive_server_and_replace_with_self_are_refused()
        Dim remote As ResourceTestProvider = Provider()
        remote.StatOverride = Function(path) New ResourceEntry With {.FullPath = path, .Length = 3}
        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveWithin(remote, "/MOV/photo.jpg", "/MOV/Photo.jpg", False, root))
        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveWithin(remote, "/MOV/photo.jpg", "/MOV/Photo.jpg", True, root))
        Assert.Equal(0, remote.DeleteCalls)
        Assert.Equal(New Byte() {1, 2, 3}, File.ReadAllBytes(remote.BackingPath("/MOV/photo.jpg")))
    End Sub

    <Fact>
    Public Sub Case_only_rename_without_server_side_rename_is_refused_and_keeps_the_only_copy()
        ' The staged download/upload pair deletes the destination and then the source; on a
        ' case-insensitive server those are one file, so the pair would leave nothing (057.S07-7).
        Dim remote As New ResourceTestProvider(Path.Combine(root, "remote-norename"), New ProviderCapabilities())
        remote.PutFile("/MOV/photo.jpg", New Byte() {1, 2, 3})
        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveWithin(remote, "/MOV/photo.jpg", "/MOV/Photo.jpg", False, root))
        Assert.Throws(Of IOException)(Sub() ResourceTransfer.MoveWithin(remote, "/MOV/photo.jpg", "/MOV/Photo.jpg", True, root))
        Assert.Equal(0, remote.DeleteCalls)
        Assert.Equal(New Byte() {1, 2, 3}, File.ReadAllBytes(remote.BackingPath("/MOV/photo.jpg")))
    End Sub

    <Theory>
    <InlineData("{damaged")>
    <InlineData("[{""Id"":""future"",""Kind"":""CloudDrive"",""Host"":""h""}]")>
    <InlineData("[{""Id"":""r"",""Kind"":""Sftp"",""Host"":""h""},{""Id"":""r"",""Kind"":""Sftp"",""Host"":""other""}]")>
    <InlineData("null")>
    Public Sub Saving_after_a_lossy_parse_preserves_the_original_value(raw As String)
        Dim values As New Dictionary(Of String, String) From {{"Resources", raw}}
        Dim parsed = ResourceRegistry.Parse(raw)
        parsed.Add(New ResourceDescriptor With {.Id = "new", .Host = "new-host"})
        ResourceRegistry.Save(parsed, Function(name) If(values.ContainsKey(name), values(name), ""),
                              Sub(name, value) values(name) = value)
        Assert.Equal(raw, values("Resources.bak"))
        Assert.Contains(ResourceRegistry.Parse(values("Resources")), Function(e) e.Id = "new")
    End Sub

    <Fact>
    Public Sub Unknown_fields_survive_an_edit_and_save()
        Dim raw As String = "[{""Id"":""r"",""Kind"":""Sftp"",""Host"":""h"",""Future"":{""nested"":[1,true,""value""]}}]"
        Dim parsed = ResourceRegistry.Parse(raw)
        parsed(0).DisplayName = "Edited"
        Dim values As New Dictionary(Of String, String) From {{"Resources", raw}}
        ResourceRegistry.Save(parsed, Function(name) If(values.ContainsKey(name), values(name), ""),
                              Sub(name, value) values(name) = value)
        Using doc As JsonDocument = JsonDocument.Parse(values("Resources"))
            Assert.Equal("[1,true,""value""]", doc.RootElement(0).GetProperty("Future").GetProperty("nested").GetRawText())
        End Using
        Assert.False(values.ContainsKey("Resources.bak"))
    End Sub

    <Fact>
    Public Sub Backup_failure_or_an_existing_different_backup_prevents_overwriting_resources()
        Dim writes As Integer
        Assert.Throws(Of IOException)(Sub() ResourceRegistry.Save(Array.Empty(Of ResourceDescriptor)(),
            Function(name) If(name = "Resources", "{broken", ""),
            Sub(name, value)
                writes += 1
                Throw New IOException("backup unavailable")
            End Sub))
        Assert.Equal(1, writes)
        writes = 0
        Assert.Throws(Of IOException)(Sub() ResourceRegistry.Save(Array.Empty(Of ResourceDescriptor)(),
            Function(name) If(name = "Resources", "{broken", "{older"), Sub(name, value) writes += 1))
        Assert.Equal(0, writes)
    End Sub

    <Theory>
    <InlineData("C:\photos", "C:\photos", "C:\photos")>
    <InlineData("sftp://h/MOV", "sftp://h/MOV", "")>
    Public Sub Failure_rollback_restores_a_plain_folder_or_clears_a_closed_locator(folder As String, box As String, expected As String)
        Dim currentFolder As String = "sftp://failed/MOV"
        Dim currentBox As String = currentFolder
        ResourceSessionSlot.RestoreFolderAfterFailure(folder, box,
            Sub(path, text)
                currentFolder = path
                currentBox = text
            End Sub)
        Assert.Equal(expected, currentFolder)
        Assert.Equal(expected, currentBox)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If Directory.Exists(root) Then Directory.Delete(root, recursive:=True)
    End Sub
End Class
#End If
