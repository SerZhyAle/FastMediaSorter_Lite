#If Not NETFRAMEWORK Then
Option Strict On

Imports System
Imports System.Drawing
Imports Xunit

''' <summary>The grid's bitmap set is bounded by BYTES and evicts least-recently-used first
''' (005 PG-3): a 256 px card is sixteen times the memory of a 64 px one.</summary>
Public Class ThumbnailMemoryCacheTests

    ' 100 x 100 x 4 bytes = 40 000 bytes per bitmap.
    Private Shared Function Card() As Bitmap
        Return New Bitmap(100, 100)
    End Function

    <Fact>
    Public Sub The_oldest_goes_first_when_the_budget_is_exceeded()
        Using cache As New ThumbnailMemoryCache(100000L)       ' room for two
            cache.Put("a", Card())
            cache.Put("b", Card())
            cache.Put("c", Card())
            Assert.False(cache.Contains("a"))
            Assert.True(cache.Contains("b"))
            Assert.True(cache.Contains("c"))
            Assert.Equal(2, cache.Count)
            Assert.Equal(80000L, cache.BytesUsed)
        End Using
    End Sub

    <Fact>
    Public Sub Reading_an_entry_protects_it()
        Using cache As New ThumbnailMemoryCache(100000L)
            cache.Put("a", Card())
            cache.Put("b", Card())
            Assert.NotNull(cache.TryGet("a"))                     ' a is now the most recent
            cache.Put("c", Card())
            Assert.True(cache.Contains("a"))
            Assert.False(cache.Contains("b"))
        End Using
    End Sub

    <Fact>
    Public Sub One_oversized_bitmap_is_still_kept_once()
        Using cache As New ThumbnailMemoryCache(1000L)
            cache.Put("big", Card())
            Assert.True(cache.Contains("big"))
            cache.Put("big2", Card())
            Assert.False(cache.Contains("big"))
            Assert.True(cache.Contains("big2"))
        End Using
    End Sub

    <Fact>
    Public Sub Replacing_disposes_the_old_bitmap_and_keeps_the_count_right()
        Using cache As New ThumbnailMemoryCache(1000000L)
            Dim first As Bitmap = Card()
            cache.Put("a", first)
            cache.Put("a", Card())
            Assert.Equal(1, cache.Count)
            Assert.Equal(40000L, cache.BytesUsed)
            Assert.Throws(Of ArgumentException)(Sub() first.GetPixel(0, 0))     ' disposed
        End Using
    End Sub

    <Fact>
    Public Sub Remove_and_Clear_release_everything()
        Dim cache As New ThumbnailMemoryCache(1000000L)
        cache.Put("a", Card())
        cache.Put("b", Card())
        cache.Remove("a")
        Assert.Equal(1, cache.Count)
        cache.Clear()
        Assert.Equal(0, cache.Count)
        Assert.Equal(0L, cache.BytesUsed)
        cache.Put(Nothing, Card())                                 ' nothing to key it by: dropped, not thrown
        Assert.Equal(0, cache.Count)
    End Sub

End Class
#End If
