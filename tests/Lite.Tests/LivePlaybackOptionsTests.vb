#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Linq
Imports Xunit

' 023_SPECIFICATION_FMS_LIVE_BROADCAST_CONSUMER.md section 6.3: the per-media numbers of a live session.

Public Class LivePlaybackOptionsTests

    <Theory>
    <InlineData(Nothing, 1000)>
    <InlineData(0L, 250)>
    <InlineData(100L, 250)>
    <InlineData(250L, 250)>
    <InlineData(1000L, 1000)>
    <InlineData(2000L, 2000)>
    <InlineData(60000L, 2000)>
    Public Sub Effective_Target_Is_The_Descriptors_Value_Clamped_To_250_2000_Else_1000(value As Long?, expected As Integer)
        Assert.Equal(expected, LivePlaybackOptions.EffectiveTargetMs(value))
    End Sub

    <Fact>
    Public Sub The_Cache_Is_The_Start_Value_And_Never_More_Than_The_Target()
        Dim options As New LivePlaybackOptions()
        Assert.Equal(options.CachingMs, options.CachingFor(Nothing))
        Assert.Equal(250, options.CachingFor(250L))      ' a target below the start value wins
        Assert.Equal(options.CachingMs, options.CachingFor(2000L))
    End Sub

    <Fact>
    Public Sub Media_Options_Set_The_Cache_For_This_Media_And_Never_Add_Seek_Resume_Or_Repeat()
        Dim options As New LivePlaybackOptions()
        Dim list As String() = options.ToMediaOptions(1000L)

        Assert.Contains(list, Function(o) o.StartsWith(":network-caching=", StringComparison.Ordinal))
        Assert.Contains(list, Function(o) o.StartsWith(":live-caching=", StringComparison.Ordinal))
        For Each o As String In list
            Assert.StartsWith(":", o, StringComparison.Ordinal)        ' per-media, never a global instance option
            Assert.DoesNotContain("start-time", o, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain("input-repeat", o, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain("loop", o, StringComparison.OrdinalIgnoreCase)
        Next
    End Sub

    <Fact>
    Public Sub An_Adts_Aac_Body_Names_Its_Demuxer_And_Nothing_Else_Does()
        ' Measured on LibVLC 3.0.21 (spec 023 section 6.3): probing takes the first sound from ~1.0 s to ~4.3 s.
        Dim options As New LivePlaybackOptions()
        Assert.Equal(":demux=aac", options.ToMediaOptions(1000L, adtsAac:=True)(0))
        Assert.DoesNotContain(options.ToMediaOptions(1000L, adtsAac:=False), Function(o) o.StartsWith(":demux=", StringComparison.Ordinal))
        Assert.DoesNotContain(options.ToMediaOptions(1000L), Function(o) o.StartsWith(":demux=", StringComparison.Ordinal))
    End Sub

    <Fact>
    Public Sub The_Clock_Options_That_Made_The_Delay_Worse_Are_Not_There()
        Dim options As New LivePlaybackOptions()
        For Each o As String In options.ToMediaOptions(1000L, adtsAac:=True)
            Assert.DoesNotContain("clock-jitter", o, StringComparison.Ordinal)
            Assert.DoesNotContain("clock-synchro", o, StringComparison.Ordinal)
        Next
    End Sub

    <Fact>
    Public Sub An_Override_Replaces_The_List_For_The_Measurement_Harness_Only()
        Dim options As New LivePlaybackOptions With {.OverrideOptions = New String() {":demux=aac"}}
        Assert.Equal(New String() {":demux=aac"}, options.ToMediaOptions(1000L, adtsAac:=False))
        Assert.Null(New LivePlaybackOptions().OverrideOptions)
    End Sub

    <Fact>
    Public Sub The_Deadlines_Are_The_Ones_Of_Section_6_2()
        Dim options As New LivePlaybackOptions()
        Assert.Equal(1500, options.ConnectTimeoutMs)   ' ticket 201: fail over promptly to the next candidate
        Assert.Equal(3000, options.StallTimeoutMs)     ' bytes stop for 3 s
    End Sub

End Class
#End If
