#If Not NETFRAMEWORK Then
Option Strict On

Imports Xunit

''' <summary>
''' What the decode cache is willing to keep
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §6.1, §3.2).
'''
''' Every rule here trades megabytes for milliseconds, and getting one wrong fails
''' quietly - a cache that fills up with entries nobody would ever have waited for looks
''' exactly like a cache that is working.
''' </summary>
Public Class DecodeCachePolicyTests

    Private Shared Function Payload(sizeBytes As Integer, animation As Boolean, decodeMs As Long) As DecodedPayload
        Return New DecodedPayload With {
            .Bytes = New Byte(sizeBytes - 1) {},
            .Kind = If(animation, DecodedPayloadKind.Gif, DecodedPayloadKind.Png),
            .IsAnimation = animation,
            .DecodeMs = decodeMs
        }
    End Function

    ''' <summary>The case the whole feature exists for: seconds of ImageSharp decode plus a
    ''' full GIF encode, paid again on every single view.</summary>
    <Fact>
    Public Sub An_animation_is_stored_however_fast_it_was()
        Assert.True(DecodeCachePolicy.ShouldStore(Payload(1024, animation:=True, decodeMs:=0), 512))
    End Sub

    <Fact>
    Public Sub A_slow_still_is_stored()
        Assert.True(DecodeCachePolicy.ShouldStore(Payload(1024, animation:=False, decodeMs:=DecodeCachePolicy.Decode_Cache_Min_Ms), 512))
    End Sub

    ''' <summary>A WEBP that decodes in 30 ms must never consume an entry - the user was
    ''' never even shown the loading badge for it.</summary>
    <Fact>
    Public Sub A_fast_still_is_not_stored()
        Assert.False(DecodeCachePolicy.ShouldStore(Payload(1024, animation:=False, decodeMs:=DecodeCachePolicy.Decode_Cache_Min_Ms - 1), 512))
    End Sub

    <Fact>
    Public Sub Nothing_is_stored_when_the_budget_is_zero()
        Assert.False(DecodeCachePolicy.IsEnabled(0))
        Assert.False(DecodeCachePolicy.ShouldStore(Payload(1024, animation:=True, decodeMs:=9999), 0))
        Assert.False(DecodeCachePolicy.ShouldStore(Payload(1024, animation:=False, decodeMs:=9999), 0))
    End Sub

    <Fact>
    Public Sub A_negative_budget_is_read_as_off()
        Assert.False(DecodeCachePolicy.IsEnabled(-1))
        Assert.Equal(0L, DecodeCachePolicy.MaxEntryBytes(-1))
    End Sub

    <Fact>
    Public Sub Nothing_larger_than_a_quarter_of_a_small_budget_is_stored()
        ' 8 MB budget -> 2 MB ceiling.
        Assert.Equal(2L * 1024L * 1024L, DecodeCachePolicy.MaxEntryBytes(8))
        Assert.True(DecodeCachePolicy.ShouldStore(Payload(2 * 1024 * 1024, animation:=True, decodeMs:=0), 8))
        Assert.False(DecodeCachePolicy.ShouldStore(Payload(2 * 1024 * 1024 + 1, animation:=True, decodeMs:=0), 8))
    End Sub

    ''' <summary>A generous budget still does not let one 300 MB animation in: the absolute
    ''' ceiling wins over the quarter rule.</summary>
    <Fact>
    Public Sub The_absolute_ceiling_caps_a_large_budget()
        Assert.Equal(DecodeCachePolicy.Absolute_Max_Entry_Bytes, DecodeCachePolicy.MaxEntryBytes(8192))
    End Sub

    <Fact>
    Public Sub A_failed_decode_is_never_stored()
        Assert.False(DecodeCachePolicy.ShouldStore(Nothing, 512))
        Assert.False(DecodeCachePolicy.ShouldStore(New DecodedPayload With {.IsAnimation = True}, 512))
        Assert.False(DecodeCachePolicy.ShouldStore(New DecodedPayload With {.Bytes = New Byte() {}, .IsAnimation = True}, 512))
    End Sub

End Class
#End If
