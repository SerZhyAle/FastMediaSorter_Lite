#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.IO
Imports Xunit

''' <summary>
''' Acceptance 1 of 006_SPECIFICATION_FILESYSTEM_PROVIDER_LAYER_DOTNET10.md: a name that a
''' remote server hands us cannot address anything outside the cache session directory.
''' That is a structural guarantee - the name is BUILT from the last segment, prefixed by the
''' index - so the test states it as the names that actually come out.
''' </summary>
Public Class RemoteCachePathsTests

    Private Const Session As String = "C:\cache\remote-cache\1234-abcd1234"

    Private Shared ReadOnly Hostile As String() = {
        "../../etc/passwd",
        "..\..\Windows\System32\x.dll",
        "a:b.jpg",
        "CON",
        "",
        New String("x"c, 300) & ".jpg",
        "....",
        "/MOV/cover.jpg",
        "/MOV/sub/cover.jpg"
    }

    <Fact>
    Public Sub Every_hostile_name_stays_inside_the_session_directory()
        For index As Integer = 0 To Hostile.Length - 1
            Dim name As String = RemoteCachePaths.EntryFileName(index, Hostile(index))
            Dim full As String = Path.GetFullPath(Path.Combine(Session, name))
            Assert.Equal(Session, Path.GetDirectoryName(full))
            Assert.True(name.Length <= 255, "name too long: " & name.Length.ToString())
            Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()))
        Next
    End Sub

    <Fact>
    Public Sub Every_name_is_distinct_even_for_identical_remote_names()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For index As Integer = 0 To Hostile.Length - 1
            Assert.True(seen.Add(RemoteCachePaths.EntryFileName(index, Hostile(index))))
        Next
        ' The two cover.jpg from different remote folders came out different.
        Assert.NotEqual(RemoteCachePaths.EntryFileName(7, "/MOV/cover.jpg"),
                        RemoteCachePaths.EntryFileName(8, "/MOV/sub/cover.jpg"))
    End Sub

    <Fact>
    Public Sub The_names_read_as_expected()
        Assert.Equal("00000_passwd", RemoteCachePaths.EntryFileName(0, "../../etc/passwd"))
        ' The part before the colon survives - Path.GetExtension would have eaten it.
        Assert.Equal("00002_a_b.jpg", RemoteCachePaths.EntryFileName(2, "a:b.jpg"))
        ' Never a reserved device name: it always starts with digits.
        Assert.Equal("00003_CON", RemoteCachePaths.EntryFileName(3, "CON"))
        Assert.Equal("00004_entry", RemoteCachePaths.EntryFileName(4, ""))
        Assert.Equal("00006_entry", RemoteCachePaths.EntryFileName(6, "...."))
        Assert.Equal("00042_clip.mp4", RemoteCachePaths.EntryFileName(42, "/MOV/clip.mp4"))
    End Sub

    <Fact>
    Public Sub The_remote_cache_is_its_own_root_beside_the_archive_cache()
        Dim root As String = RemoteCachePaths.CacheRoot()
        Assert.EndsWith("remote-cache", root)
        Assert.False(RemoteCachePaths.IsUnderRemoteCache(ArchivePaths.CacheRoot(), root))
        Assert.True(RemoteCachePaths.IsUnderRemoteCache(Path.Combine(root, "1-a", "00001_x.jpg"), root))
        Assert.False(RemoteCachePaths.IsUnderRemoteCache(root & "-old\x.jpg", root))
    End Sub

End Class
#End If
