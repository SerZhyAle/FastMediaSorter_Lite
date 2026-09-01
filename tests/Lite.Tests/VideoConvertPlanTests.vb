#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

''' <summary>
''' What FFmpeg is asked to do, and where the result goes
''' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §9).
'''
''' Every flag in the argument list is here because leaving it out fails in a way nobody
''' sees until a user's file is gone: an odd-sized GIF simply refuses to encode without the
''' scale filter, a 10-bit profile produces a file that plays on the developer's machine and
''' on no phone, and the temp-name marker in the wrong place makes every single save fail.
''' </summary>
Public Class VideoConvertPlanTests

    ' --- frame rate ------------------------------------------------------------

    <Fact>
    Public Sub The_fps_is_the_sources_average_rate()
        ' 24 frames over 1 second.
        Assert.Equal(24, VideoConvertPlan.Fps(24, 1000))
        ' 10 frames over 2 seconds.
        Assert.Equal(5, VideoConvertPlan.Fps(10, 2000))
    End Sub

    <Fact>
    Public Sub The_fps_is_rounded_not_truncated()
        ' 7 frames over 1 second = 7; 15 frames over 2 seconds = 7.5 -> 8.
        Assert.Equal(8, VideoConvertPlan.Fps(15, 2000))
    End Sub

    <Fact>
    Public Sub The_fps_is_clamped_at_both_ends()
        ' A 2 ms frame delay would ask for 500 fps.
        Assert.Equal(VideoConvertPlan.Max_Fps, VideoConvertPlan.Fps(500, 1000))
        ' One frame every ten seconds rounds to 0, which no encoder accepts.
        Assert.Equal(VideoConvertPlan.Min_Fps, VideoConvertPlan.Fps(1, 10000))
    End Sub

    <Fact>
    Public Sub An_unknown_duration_falls_back_to_a_sane_rate()
        Assert.InRange(VideoConvertPlan.Fps(0, 0), VideoConvertPlan.Min_Fps, VideoConvertPlan.Max_Fps)
        Assert.InRange(VideoConvertPlan.Fps(10, 0), VideoConvertPlan.Min_Fps, VideoConvertPlan.Max_Fps)
    End Sub

    ' --- names -----------------------------------------------------------------

    <Fact>
    Public Sub The_target_is_the_source_name_with_an_mp4_extension()
        Assert.Equal("C:\pics\cat.mp4",
                     VideoConvertPlan.TargetPathFor("C:\pics\cat.webp", Function(candidate) False))
    End Sub

    ''' <summary>An existing file is NEVER overwritten - and NameCollisionPolicy is
    ''' deliberately not consulted, so its "replace" value cannot turn this button into
    ''' "overwrite somebody else's file".</summary>
    <Fact>
    Public Sub An_existing_target_is_never_overwritten()
        Dim taken As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"C:\pics\cat.mp4"}
        Assert.Equal("C:\pics\cat (2).mp4",
                     VideoConvertPlan.TargetPathFor("C:\pics\cat.webp", Function(candidate) taken.Contains(candidate)))

        taken.Add("C:\pics\cat (2).mp4")
        taken.Add("C:\pics\cat (3).mp4")
        Assert.Equal("C:\pics\cat (4).mp4",
                     VideoConvertPlan.TargetPathFor("C:\pics\cat.webp", Function(candidate) taken.Contains(candidate)))
    End Sub

    ''' <summary>
    ''' The marker goes BEFORE the extension - cat.fms-tmp.mp4, not cat.mp4.fms-tmp. FFmpeg
    ''' picks its container from the extension it is handed, so the second shape fails every
    ''' single time. Same rule, same constant, as ImageFileWriter.TempPathFor.
    ''' </summary>
    <Fact>
    Public Sub The_temp_name_keeps_the_extension_last()
        Dim temp As String = VideoConvertPlan.TempPathFor("C:\pics\cat.mp4")

        Assert.Equal("C:\pics\cat" & ImageFileWriter.TempMarker & ".mp4", temp)
        Assert.EndsWith(".mp4", temp)
    End Sub

    <Fact>
    Public Sub The_temp_file_sits_beside_the_target()
        Assert.StartsWith("C:\pics\", VideoConvertPlan.TempPathFor("C:\pics\cat (2).mp4"))
    End Sub

    ' --- the command line ------------------------------------------------------

    <Fact>
    Public Sub The_arguments_carry_both_quoted_paths_and_the_rate()
        Dim args As String = VideoConvertPlan.BuildArguments("C:\pics\my cat.webp", "C:\pics\my cat.fms-tmp.mp4", 12)

        ' Quoted, because a picture folder with a space in its name is the normal case.
        Assert.Contains("-i ""C:\pics\my cat.webp""", args)
        Assert.EndsWith("""C:\pics\my cat.fms-tmp.mp4""", args)
        Assert.Contains("-r 12", args)
    End Sub

    <Fact>
    Public Sub The_arguments_encode_what_every_player_understands()
        Dim args As String = VideoConvertPlan.BuildArguments("a.webp", "b.mp4", 10)

        Assert.Contains("-c:v libx264", args)
        Assert.Contains("-profile:v high", args)
        Assert.Contains("format=yuv420p", args)
        Assert.Contains("-movflags +faststart", args)
    End Sub

    ''' <summary>H.264 yuv420p needs even dimensions - without this an odd-sized GIF fails
    ''' outright, which is the single most likely source in this feature.</summary>
    <Fact>
    Public Sub The_arguments_force_even_dimensions()
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", VideoConvertPlan.BuildArguments("a.gif", "b.mp4", 10))
    End Sub

    <Fact>
    Public Sub The_arguments_never_wait_on_a_console_and_carry_no_audio()
        Dim args As String = VideoConvertPlan.BuildArguments("a.gif", "b.mp4", 10)

        ' The process runs with redirected pipes; a prompt would hang it for ever.
        Assert.Contains("-nostdin", args)
        ' The sources have no audio track.
        Assert.Contains("-an", args)
    End Sub

    <Fact>
    Public Sub The_arguments_ask_for_machine_readable_progress()
        Dim args As String = VideoConvertPlan.BuildArguments("a.gif", "b.mp4", 10)

        ' stdout, newline-terminated. The stderr status line is \r-terminated and arrives as
        ' one enormous line at the very end, which no progress dialog can use.
        Assert.Contains("-progress pipe:1", args)
        Assert.Contains("-nostats", args)
    End Sub

End Class
#End If
