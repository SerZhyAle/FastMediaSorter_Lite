#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports System.Linq
Imports Xunit

''' <summary>
''' The key of the decode cache
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §4).
'''
''' This is the one part of the feature whose failure is SILENT and shows wrong pixels: a
''' key that does not move when the answer moves serves yesterday's decode for today's
''' file, and the result is a perfectly good picture - just not this one. So each of the
''' four inputs gets its own test, and so does the one thing that must NOT change the key.
'''
''' Modern-only, like the cache: on the net48 leg this file compiles to nothing.
''' </summary>
Public Class DecodeCacheKeyTests

    Private Const Path_A As String = "C:\photos\cat.webp"

    Private Shared Function Key(Optional filePath As String = Path_A,
                                Optional ticks As Long = 1000,
                                Optional length As Long = 2000,
                                Optional exif As Boolean = True,
                                Optional version As Integer = 1) As String
        Return DecodeCacheKey.Build(filePath, ticks, length, exif, version)
    End Function

    <Fact>
    Public Sub The_write_time_changes_the_key()
        Assert.NotEqual(Key(ticks:=1000), Key(ticks:=1001))
    End Sub

    <Fact>
    Public Sub The_length_changes_the_key()
        Assert.NotEqual(Key(length:=2000), Key(length:=2001))
    End Sub

    ''' <summary>The EXIF setting alters the PIXELS the Magick path writes (it applies
    ''' AutoOrient itself), so a payload produced under one setting must never be served
    ''' under the other.</summary>
    <Fact>
    Public Sub The_exif_auto_rotate_setting_changes_the_key()
        Assert.NotEqual(Key(exif:=True), Key(exif:=False))
    End Sub

    ''' <summary>The single discipline the whole feature rests on: bump the version and
    ''' every payload built by the previous algorithm stops being reachable.</summary>
    <Fact>
    Public Sub The_format_version_changes_the_key()
        Assert.NotEqual(Key(version:=1), Key(version:=2))
    End Sub

    <Fact>
    Public Sub The_path_changes_the_key()
        Assert.NotEqual(Key(filePath:="C:\photos\cat.webp"), Key(filePath:="C:\photos\dog.webp"))
    End Sub

    ''' <summary>..and nothing else does. Same five inputs, same key - otherwise the cache
    ''' would miss on every second open and quietly do nothing at all.</summary>
    <Fact>
    Public Sub The_same_inputs_give_the_same_key()
        Assert.Equal(Key(), Key())
    End Sub

    ''' <summary>Two spellings of one file cost two entries - documented in §4 as cheaper
    ''' than normalising a UNC path wrong. Pinned so nobody "fixes" it by accident.</summary>
    <Fact>
    Public Sub The_path_is_taken_exactly_as_given()
        Assert.NotEqual(Key(filePath:="C:\photos\cat.webp"), Key(filePath:="c:\PHOTOS\cat.webp"))
    End Sub

    <Fact>
    Public Sub The_file_name_carries_the_kind_and_the_extension()
        Dim gif As String = DecodeCacheKey.FileNameFor(Key(), DecodedPayloadKind.Gif)
        Dim png As String = DecodeCacheKey.FileNameFor(Key(), DecodedPayloadKind.Png)

        Assert.EndsWith("-gif" & DecodeCacheKey.File_Extension, gif)
        Assert.EndsWith("-png" & DecodeCacheKey.File_Extension, png)
        ' One key, two kinds, two names - a GIF payload can never be read back as a PNG one.
        Assert.NotEqual(gif, png)
    End Sub

    ''' <summary>
    ''' The name is a hash, not the path - so a picture called "a:b?.webp" on a share, or
    ''' one with 300 characters of Unicode in its name, still produces something the file
    ''' system will accept.
    ''' </summary>
    <Fact>
    Public Sub The_file_name_is_path_safe_whatever_the_source_was_called()
        Dim hostile As String = Key(filePath:="\\nas\share\<>:""|?*" & New String("щ"c, 300) & ".webp")
        Dim name As String = DecodeCacheKey.FileNameFor(hostile, DecodedPayloadKind.Gif)

        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()))
        ' 40 hex + "-gif" + ".fmsdec"
        Assert.Equal(40 + 4 + DecodeCacheKey.File_Extension.Length, name.Length)
    End Sub

    <Fact>
    Public Sub Different_keys_give_different_file_names()
        Assert.NotEqual(DecodeCacheKey.FileNameFor(Key(ticks:=1), DecodedPayloadKind.Png),
                        DecodeCacheKey.FileNameFor(Key(ticks:=2), DecodedPayloadKind.Png))
    End Sub

End Class
#End If
