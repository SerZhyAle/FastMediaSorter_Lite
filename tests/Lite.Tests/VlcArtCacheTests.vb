#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports Xunit

''' <summary>
''' VlcArtCache - what the secure playback may delete from LibVLC's art cache (audit 046,
''' G-04). LibVLC writes a container's embedded cover there as the input opens, named by
''' the sealed artist and album; the viewer removes it on sight. Because this deletes files,
''' the plan has to be narrow: only an art file LibVLC wrote, only inside the cache, and
''' only the empty folders between it and the category folder.
''' </summary>
Public Class VlcArtCacheTests
    Implements IDisposable

    Private ReadOnly _root As String

    Public Sub New()
        _root = Path.Combine(Path.GetTempPath(), "fms_vlcart_tests_" & Guid.NewGuid().ToString("N"), "art")
        Directory.CreateDirectory(_root)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(Path.GetDirectoryName(_root), True)
        Catch
        End Try
    End Sub

    Private Function WriteArt(ParamArray segments As String()) As String
        Dim p As String = Path.Combine(_root, Path.Combine(segments))
        Directory.CreateDirectory(Path.GetDirectoryName(p))
        File.WriteAllBytes(p, New Byte() {&HFF, &HD8, &HFF})
        Return p
    End Function

    Private Shared Function UrlOf(localPath As String) As String
        Return New Uri(localPath).AbsoluteUri
    End Function

    <Fact>
    Public Sub An_artist_album_entry_plans_the_file_and_both_sealed_folders()
        Dim art As String = WriteArt("artistalbum", "Sealed Artist", "Sealed Album", "art.jpg")

        Dim plan As VlcArtCache.ArtScrubPlan = VlcArtCache.PlanScrub(UrlOf(art), {_root})

        Assert.NotNull(plan)
        Assert.Equal(art, plan.FilePath, ignoreCase:=True)
        Assert.Equal(2, plan.Directories.Count)
        Assert.Equal(Path.Combine(_root, "artistalbum", "Sealed Artist", "Sealed Album"), plan.Directories(0), ignoreCase:=True)
        Assert.Equal(Path.Combine(_root, "artistalbum", "Sealed Artist"), plan.Directories(1), ignoreCase:=True)
    End Sub

    <Fact>
    Public Sub A_non_ascii_name_survives_the_file_url_round_trip()
        ' LibVLC percent-encodes UTF-8 in the URL it reports; the plan must land on the
        ' real folder, or the Cyrillic artist of a sealed track would stay on disk.
        Dim art As String = WriteArt("artistalbum", "Влади", "Длится Февраль", "art.jpg")

        Dim plan As VlcArtCache.ArtScrubPlan = VlcArtCache.PlanScrub(UrlOf(art), {_root})

        Assert.NotNull(plan)
        Assert.Equal(art, plan.FilePath, ignoreCase:=True)
    End Sub

    <Fact>
    Public Sub Nothing_outside_the_cache_or_not_named_art_is_ever_planned()
        Dim outside As String = Path.Combine(Path.GetDirectoryName(_root), "art.jpg")
        File.WriteAllBytes(outside, New Byte() {1})
        Dim wrongName As String = WriteArt("arturl", "0123abcd", "photo.jpg")

        Assert.Null(VlcArtCache.PlanScrub(UrlOf(outside), {_root}))
        Assert.Null(VlcArtCache.PlanScrub(UrlOf(wrongName), {_root}))
        ' Straight inside the category folder is not a layout LibVLC writes.
        Assert.Null(VlcArtCache.PlanScrub(UrlOf(Path.Combine(_root, "arturl", "art.jpg")), {_root}))
        ' Not extracted yet, or not a file at all.
        Assert.Null(VlcArtCache.PlanScrub("attachment://cover.jpg", {_root}))
        Assert.Null(VlcArtCache.PlanScrub("", {_root}))
        Assert.Null(VlcArtCache.PlanScrub(Nothing, {_root}))
        ' A sibling folder that merely starts with the root's name is not the root.
        Dim sibling As String = _root & "-other"
        Directory.CreateDirectory(Path.Combine(sibling, "arturl", "x"))
        Assert.Null(VlcArtCache.PlanScrub(UrlOf(Path.Combine(sibling, "arturl", "x", "art.jpg")), {_root}))
    End Sub

    <Fact>
    Public Sub Scrub_removes_the_file_and_the_emptied_folders_but_keeps_a_shared_one()
        Dim sealedArt As String = WriteArt("artistalbum", "Shared Artist", "Sealed Album", "art.jpg")
        ' The same artist's other album, cached earlier from an ordinary file: its folder
        ' is not ours and must survive.
        Dim otherArt As String = WriteArt("artistalbum", "Shared Artist", "Public Album", "art.jpg")

        Assert.True(VlcArtCache.Scrub(UrlOf(sealedArt), {_root}))

        Assert.False(File.Exists(sealedArt))
        Assert.False(Directory.Exists(Path.GetDirectoryName(sealedArt)))
        Assert.True(File.Exists(otherArt))
        Assert.True(Directory.Exists(Path.Combine(_root, "artistalbum", "Shared Artist")))
        Assert.True(Directory.Exists(Path.Combine(_root, "artistalbum")))
    End Sub

    <Fact>
    Public Sub A_hash_entry_plans_its_one_folder()
        Dim art As String = WriteArt("arturl", "903806a29ce85071c02809ee886073c3", "art")

        Dim plan As VlcArtCache.ArtScrubPlan = VlcArtCache.PlanScrub(UrlOf(art), {"C:\no\such\root", _root})

        Assert.NotNull(plan)
        Assert.Single(plan.Directories)
        Assert.Equal(Path.Combine(_root, "arturl", "903806a29ce85071c02809ee886073c3"), plan.Directories(0), ignoreCase:=True)
    End Sub

End Class
#End If
