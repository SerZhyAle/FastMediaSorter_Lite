#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' The decode cache on disk
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §5, invariants 1 and 3).
'''
''' Driven against a temporary directory, never the user's real cache - which is exactly
''' why every operation takes the directory as a parameter.
'''
''' The two rules worth the trouble of a disk test: a DAMAGED entry must come back as a
''' plain miss and take itself with it (otherwise one bad write poisons a file for ever),
''' and the budget must actually evict (the OCR cache displayed a limit for two years that
''' nobody enforced - that is the defect DiskCacheTrim was written for).
''' </summary>
Public Class DecodeCacheStoreTests
    Implements IDisposable

    Private ReadOnly dir As String

    Public Sub New()
        dir = Path.Combine(Path.GetTempPath(), "fms-decode-cache-tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        Catch
        End Try
    End Sub

    Private Shared Function Payload(bytes As Byte(), kind As DecodedPayloadKind) As DecodedPayload
        Return New DecodedPayload With {
            .Bytes = bytes,
            .Kind = kind,
            .IsAnimation = (kind = DecodedPayloadKind.Gif),
            .DecodeMs = 9999
        }
    End Function

    Private Shared Function Bytes(fill As Byte, count As Integer) As Byte()
        Return Enumerable.Repeat(fill, count).ToArray()
    End Function

    <Fact>
    Public Sub A_stored_payload_comes_back_unchanged()
        Dim written As Byte() = Bytes(&HA1, 64)
        DecodeCacheStore.TryStore(dir, "k1", Payload(written, DecodedPayloadKind.Gif), 512)

        Dim read As DecodedPayload = DecodeCacheStore.TryLoad(dir, "k1")

        Assert.NotNull(read)
        Assert.Equal(written, read.Bytes)
        Assert.Equal(DecodedPayloadKind.Gif, read.Kind)
        ' The kind is recovered from the NAME, and an animation is what a GIF entry is.
        Assert.True(read.IsAnimation)
    End Sub

    <Fact>
    Public Sub A_still_payload_keeps_its_kind()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&HB2, 32), DecodedPayloadKind.Png), 512)

        Dim read As DecodedPayload = DecodeCacheStore.TryLoad(dir, "k1")

        Assert.NotNull(read)
        Assert.Equal(DecodedPayloadKind.Png, read.Kind)
        Assert.False(read.IsAnimation)
    End Sub

    <Fact>
    Public Sub An_unknown_key_is_a_miss()
        Assert.Null(DecodeCacheStore.TryLoad(dir, "never-written"))
    End Sub

    <Fact>
    Public Sub A_second_store_replaces_the_first()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H1, 16), DecodedPayloadKind.Gif), 512)
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H2, 24), DecodedPayloadKind.Gif), 512)

        Dim read As DecodedPayload = DecodeCacheStore.TryLoad(dir, "k1")

        Assert.Equal(Bytes(&H2, 24), read.Bytes)
        ' One entry, not two - and no leftover .tmp beside it.
        Assert.Single(Directory.GetFiles(dir, DecodeCacheKey.File_Pattern))
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"))
    End Sub

    ''' <summary>Invariant 1: a truncated entry is a MISS, and it does not survive to be a
    ''' miss a second time.</summary>
    <Fact>
    Public Sub A_truncated_entry_is_a_miss_and_is_deleted()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H7, 128), DecodedPayloadKind.Gif), 512)
        Dim entry As String = DecodeCacheStore.EntryPath(dir, "k1", DecodedPayloadKind.Gif)
        File.WriteAllBytes(entry, New Byte() {})

        Assert.Null(DecodeCacheStore.TryLoad(dir, "k1"))
        Assert.False(File.Exists(entry))
    End Sub

    <Fact>
    Public Sub Invalidate_removes_only_the_named_kind()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H7, 16), DecodedPayloadKind.Gif), 512)
        DecodeCacheStore.TryStore(dir, "k2", Payload(Bytes(&H8, 16), DecodedPayloadKind.Png), 512)

        DecodeCacheStore.Invalidate(dir, "k1", DecodedPayloadKind.Gif)

        Assert.Null(DecodeCacheStore.TryLoad(dir, "k1"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "k2"))
    End Sub

    <Fact>
    Public Sub A_missing_directory_is_a_miss_and_not_an_exception()
        Dim gone As String = Path.Combine(dir, "no-such-directory")
        Assert.Null(DecodeCacheStore.TryLoad(gone, "k1"))
        Assert.Equal(0L, DecodeCacheStore.BytesOnDisk(gone))
        Assert.Equal(0, DecodeCacheStore.Clear(gone))
    End Sub

    <Fact>
    Public Sub Nothing_is_written_when_the_budget_is_zero()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H7, 16), DecodedPayloadKind.Gif), 0)

        Assert.Empty(Directory.GetFiles(dir))
        Assert.Null(DecodeCacheStore.TryLoad(dir, "k1"))
    End Sub

    ''' <summary>
    ''' Invariant 3, and the reason DiskCacheTrim grew a file_pattern parameter.
    '''
    ''' It takes FIVE entries rather than the obvious two, and that is the policy talking:
    ''' no single entry may exceed a quarter of the budget (§3.2), so overflowing a budget
    ''' at all needs more than four of them. Entries of exactly a quarter of 1 MB, and the
    ''' fifth write pushes the total to 1.25 MB - the OLDEST is the one that goes, and
    ''' exactly one goes, because that is enough.
    ''' </summary>
    <Fact>
    Public Sub The_budget_evicts_the_oldest_entry_first()
        Const budgetMb As Integer = 1
        Const entryBytes As Integer = 256 * 1024

        Dim keys As String() = {"oldest", "older", "old", "recent"}
        For index As Integer = 0 To keys.Length - 1
            DecodeCacheStore.TryStore(dir, keys(index), Payload(Bytes(CByte(index + 1), entryBytes), DecodedPayloadKind.Gif), budgetMb)
            Age(DecodeCacheStore.EntryPath(dir, keys(index), DecodedPayloadKind.Gif), hours:=keys.Length - index)
        Next

        DecodeCacheStore.TryStore(dir, "newest", Payload(Bytes(&H9, entryBytes), DecodedPayloadKind.Gif), budgetMb)

        Assert.Null(DecodeCacheStore.TryLoad(dir, "oldest"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "older"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "old"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "recent"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "newest"))
        Assert.True(DecodeCacheStore.BytesOnDisk(dir) <= CLng(budgetMb) * 1024L * 1024L)
    End Sub

    ''' <summary>The other half of that rule, stated where it bites: an entry above the
    ''' per-entry ceiling is not written at all - it does not evict the whole cache in
    ''' order to store itself.</summary>
    <Fact>
    Public Sub An_oversized_entry_is_not_written_and_evicts_nothing()
        Const budgetMb As Integer = 1

        DecodeCacheStore.TryStore(dir, "kept", Payload(Bytes(&H1, 4096), DecodedPayloadKind.Gif), budgetMb)
        DecodeCacheStore.TryStore(dir, "huge", Payload(Bytes(&H2, 512 * 1024), DecodedPayloadKind.Gif), budgetMb)

        Assert.Null(DecodeCacheStore.TryLoad(dir, "huge"))
        Assert.NotNull(DecodeCacheStore.TryLoad(dir, "kept"))
    End Sub

    <Fact>
    Public Sub BytesOnDisk_counts_only_our_entries()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H7, 1000), DecodedPayloadKind.Gif), 512)
        File.WriteAllBytes(Path.Combine(dir, "someone-elses.txt"), Bytes(&H0, 5000))

        Assert.Equal(1000L, DecodeCacheStore.BytesOnDisk(dir))
    End Sub

    <Fact>
    Public Sub Clear_removes_the_entries_and_leaves_foreign_files_alone()
        DecodeCacheStore.TryStore(dir, "k1", Payload(Bytes(&H7, 16), DecodedPayloadKind.Gif), 512)
        DecodeCacheStore.TryStore(dir, "k2", Payload(Bytes(&H8, 16), DecodedPayloadKind.Png), 512)
        Dim foreign As String = Path.Combine(dir, "someone-elses.txt")
        File.WriteAllText(foreign, "keep me")

        Assert.Equal(2, DecodeCacheStore.Clear(dir))

        Assert.Empty(Directory.GetFiles(dir, DecodeCacheKey.File_Pattern))
        Assert.True(File.Exists(foreign))
    End Sub

    ''' <summary>DiskCacheTrim sorts by last-write time, so the test has to make the entries
    ''' genuinely different ages - three writes inside one millisecond would otherwise be
    ''' evicted in an order nobody promised.</summary>
    Private Shared Sub Age(entryPath As String, hours As Integer)
        File.SetLastWriteTimeUtc(entryPath, DateTime.UtcNow.AddHours(-hours))
    End Sub

End Class
#End If
