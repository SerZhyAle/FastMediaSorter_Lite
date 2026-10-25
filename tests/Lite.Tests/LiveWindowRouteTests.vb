#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Threading
Imports Xunit

' Ticket 263 T-2: the window route of the screen source. Everything here runs without a desktop or a window:
' the picker's rules (LiveWindowCatalog), the letterboxed fold and the pool-size maths (LiveVideoCopy), the
' engine's aspect-keeping scale (LiveFfmpegArgs), the remembered window (LiveProducerSettings) and the
' controller's reaction to a source that ends by itself. What needs a real window (the WGC item, Closed,
' minimise, resize) is on the hand-run list of the ticket, not here.

Public Class LiveWindowCatalogTests

    Private Shared Function Window(title As String, processName As String, Optional processId As Integer = 100,
                                   Optional visible As Boolean = True, Optional cloaked As Boolean = False,
                                   Optional toolWindow As Boolean = False, Optional minimized As Boolean = False,
                                   Optional className As String = "Chrome_WidgetWin_1") As LiveWindowCandidate
        Return New LiveWindowCandidate With {
            .Handle = New IntPtr(processId * 10), .Title = title, .ProcessName = processName, .ProcessId = processId,
            .IsVisible = visible, .IsCloaked = cloaked, .IsToolWindow = toolWindow, .IsMinimized = minimized, .ClassName = className
        }
    End Function

    <Fact>
    Public Sub A_visible_titled_window_is_offered_with_title_and_process_name()
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({Window("Quarterly report", "winword")}, 999)
        Assert.Single(rows)
        Assert.Equal("Quarterly report - winword", rows(0).Label)
        Assert.Equal("Quarterly report", rows(0).Title)
        Assert.Equal("winword", rows(0).ProcessName)
    End Sub

    <Fact>
    Public Sub Hidden_cloaked_own_process_and_untitled_windows_are_not_offered()
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({
            Window("Hidden", "a", 1, visible:=False),
            Window("Cloaked on another desktop", "b", 2, cloaked:=True),
            Window("Share Manager window", "FastMediaSorterCompanion", 777),
            Window("", "c", 3),
            Window("   ", "d", 4),
            Window(Nothing, "e", 5),
            Window("Palette", "f", 6, toolWindow:=True, className:="Tool"),
            Window("Program Manager", "explorer", 7, className:="Progman"),
            Window("The one that stays", "g", 8)
        }, 777)
        ' The tool window HAS a title, so it stays (a floating palette of a real application); the rule drops
        ' only a tool window without one, and every untitled window.
        Assert.Equal({"Palette", "The one that stays"}, rows.Select(Function(r) r.Title).ToArray())
    End Sub

    <Fact>
    Public Sub A_tool_window_without_a_title_is_skipped()
        Assert.Empty(LiveWindowCatalog.Pick({Window("", "tip", 1, toolWindow:=True)}, 999))
    End Sub

    <Fact>
    Public Sub Minimised_windows_stay_in_the_list_and_say_so()
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({Window("Parked", "app", 1, minimized:=True)}, 999)
        Assert.Single(rows)
        Assert.True(rows(0).IsMinimized)
    End Sub

    <Fact>
    Public Sub Rows_are_ordered_by_title_and_equal_identities_collapse_to_the_topmost()
        Dim first As LiveWindowCandidate = Window("Notes", "notepad", 1)
        Dim second As LiveWindowCandidate = Window("Notes", "NOTEPAD", 2)
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({Window("zeta", "z", 3), first, Window("Alpha", "a", 4), second}, 999)
        Assert.Equal({"Alpha", "Notes", "zeta"}, rows.Select(Function(r) r.Title).ToArray())
        ' Title and process (case-insensitive) are the identity the lookup uses, so the second "Notes" would be
        ' unreachable; the first in the enumeration order (the topmost window) is the one kept.
        Assert.Equal(first.Handle, rows(1).Handle)
    End Sub

    <Fact>
    Public Sub A_remembered_window_is_found_by_exact_title_and_process_name_ignoring_case()
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({Window("Inbox - Mail", "OUTLOOK", 1), Window("Inbox", "OUTLOOK", 2)}, 999)
        Dim found As LiveWindowChoice = LiveWindowCatalog.Find(rows, "Inbox - Mail", "outlook")
        Assert.NotNull(found)
        Assert.Equal("Inbox - Mail", found.Title)
        Assert.Null(LiveWindowCatalog.Find(rows, "inbox - mail", "outlook"))          ' the title is exact
        Assert.Null(LiveWindowCatalog.Find(rows, "Inbox - Mail", "thunderbird"))      ' another program, same title
        Assert.Null(LiveWindowCatalog.Find(rows, "", "outlook"))
        Assert.Null(LiveWindowCatalog.Find(Nothing, "Inbox", "outlook"))
    End Sub

    <Fact>
    Public Sub A_window_that_is_gone_is_not_found_and_nothing_else_stands_in_for_it()
        Dim rows As List(Of LiveWindowChoice) = LiveWindowCatalog.Pick({Window("Other", "x", 1)}, 999)
        Assert.Null(LiveWindowCatalog.Find(rows, "Closed meeting", "teams"))
    End Sub

    <Fact>
    Public Sub The_label_keeps_a_title_alone_when_the_process_is_unknown()
        Assert.Equal("Locked", LiveWindowCatalog.LabelOf("Locked", ""))
        Assert.Equal("Locked - svc", LiveWindowCatalog.LabelOf("Locked", "svc"))
    End Sub

End Class

Public Class LiveVideoCopyLetterboxTests

    Private Shared Function Bytes(ParamArray pixels As Integer()) As Byte()
        Dim result(pixels.Length * 4 - 1) As Byte
        Buffer.BlockCopy(pixels, 0, result, 0, result.Length)
        Return result
    End Function

    Private Shared Function Pixels(data As Byte()) As Integer()
        Dim result(data.Length \ 4 - 1) As Integer
        Buffer.BlockCopy(data, 0, result, 0, data.Length)
        Return result
    End Function

    Private Shared Function Filled(count As Integer, value As Integer) As Byte()
        Dim values(count - 1) As Integer
        For i As Integer = 0 To count - 1
            values(i) = value
        Next
        Return Bytes(values)
    End Function

    <Fact>
    Public Sub The_same_size_is_a_plain_copy()
        Dim dst As Byte() = Filled(4, 9)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 2, 2, dst, 2, 2)
        Assert.Equal({1, 2, 3, 4}, Pixels(dst))
    End Sub

    <Fact>
    Public Sub A_wider_picture_gets_bars_above_and_below_and_is_not_stretched()
        ' 4x1 into 4x3: the picture keeps its 4:1 shape in the middle row, the rows above and below are black
        ' (whatever the buffer held before is cleared, a resized window never leaves its old picture behind).
        Dim dst As Byte() = Filled(12, 9)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 4, 1, dst, 4, 3)
        Assert.Equal({0, 0, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0}, Pixels(dst))
    End Sub

    <Fact>
    Public Sub A_taller_picture_gets_bars_left_and_right()
        Dim dst As Byte() = Filled(12, 9)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 1, 4, dst, 3, 4)
        Assert.Equal({0, 1, 0, 0, 2, 0, 0, 3, 0, 0, 4, 0}, Pixels(dst))
    End Sub

    <Fact>
    Public Sub A_larger_picture_of_the_same_shape_is_sampled_down_without_bars()
        Dim src As Byte() = Bytes(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)
        Dim dst As Byte() = Filled(4, 9)
        LiveVideoCopy.FoldFrameLetterboxed(src, 4, 4, dst, 2, 2)
        ' Centred sampling: the pixel at the middle of each 2x2 block's lower-right quarter.
        Assert.Equal({5, 7, 13, 15}, Pixels(dst))
    End Sub

    <Fact>
    Public Sub A_smaller_picture_of_the_same_shape_is_scaled_up_to_fill_the_buffer()
        Dim dst As Byte() = Filled(16, 9)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 2, 2, dst, 4, 4)
        Assert.Equal({1, 1, 2, 2, 1, 1, 2, 2, 3, 3, 4, 4, 3, 3, 4, 4}, Pixels(dst))
    End Sub

    <Theory>
    <InlineData(1920, 1080, 1280, 720, 1280, 720, 0, 0)>
    <InlineData(800, 600, 1280, 720, 960, 720, 160, 0)>
    <InlineData(600, 800, 1280, 720, 540, 720, 370, 0)>
    <InlineData(1280, 400, 1280, 720, 1280, 400, 0, 160)>
    <InlineData(5000, 1, 1280, 720, 1280, 1, 0, 359)>
    Public Sub The_picture_rectangle_keeps_the_aspect_and_is_centred(srcW As Integer, srcH As Integer, dstW As Integer, dstH As Integer,
                                                                      expectedW As Integer, expectedH As Integer, expectedX As Integer, expectedY As Integer)
        Dim w As Integer, h As Integer, x As Integer, y As Integer
        LiveVideoCopy.LetterboxRect(srcW, srcH, dstW, dstH, w, h, x, y)
        Assert.Equal(expectedW, w)
        Assert.Equal(expectedH, h)
        Assert.Equal(expectedX, x)
        Assert.Equal(expectedY, y)
        Assert.True(w <= dstW AndAlso h <= dstH)
        Assert.True(x >= 0 AndAlso y >= 0 AndAlso x + w <= dstW AndAlso y + h <= dstH)
    End Sub

    <Fact>
    Public Sub Wrong_sizes_and_short_arrays_change_nothing()
        Dim dst As Byte() = Filled(4, 9)
        LiveVideoCopy.FoldFrameLetterboxed(Nothing, 2, 2, dst, 2, 2)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2), 2, 2, dst, 2, 2)                    ' source shorter than 2x2
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 0, 2, dst, 2, 2)
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 2, 2, Filled(2, 9), 2, 2)     ' destination shorter than 2x2
        LiveVideoCopy.FoldFrameLetterboxed(Bytes(1, 2, 3, 4), 2, 2, dst, -1, 2)
        Assert.Equal({9, 9, 9, 9}, Pixels(dst))
    End Sub

    <Theory>
    <InlineData(1280, 720, 1280, 720, False)>
    <InlineData(1280, 720, 1000, 700, True)>
    <InlineData(1280, 720, 1280, 500, True)>
    <InlineData(1280, 720, 0, 0, False)>
    <InlineData(1280, 720, -3, 700, False)>
    <InlineData(1280, 720, 700, 0, False)>
    Public Sub The_pool_is_recreated_only_for_a_new_real_size(poolW As Integer, poolH As Integer, contentW As Integer, contentH As Integer, expected As Boolean)
        Assert.Equal(expected, LiveVideoCopy.NeedsPoolRecreate(poolW, poolH, contentW, contentH))
    End Sub

    <Theory>
    <InlineData(1280, 720, 1000, 500, 1000, 500)>
    <InlineData(1280, 720, 2000, 2000, 1280, 720)>
    <InlineData(1280, 720, 0, 0, 1280, 720)>
    <InlineData(1280, 720, 1280, 720, 1280, 720)>
    Public Sub Only_the_content_region_of_a_pooled_surface_is_read(surfaceW As Integer, surfaceH As Integer, contentW As Integer, contentH As Integer,
                                                                    expectedW As Integer, expectedH As Integer)
        Dim w As Integer, h As Integer
        LiveVideoCopy.ContentRegion(surfaceW, surfaceH, contentW, contentH, w, h)
        Assert.Equal(expectedW, w)
        Assert.Equal(expectedH, h)
    End Sub

End Class

Public Class LiveWindowRouteEngineAndSettingsTests

    <Fact>
    Public Sub A_sixteen_by_nine_monitor_keeps_the_plain_scale_and_its_exact_argument_string()
        Dim options As New LiveEncoderOptions With {.WithVideo = True, .WithAudio = False, .VideoFps = 25, .InputVideoWidth = 1920, .InputVideoHeight = 1080}
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\x")
        Assert.Contains("-vf scale=1280:720 -c:v", args)
        Assert.DoesNotContain("pad=", args)
        options.InputVideoWidth = 1366
        options.InputVideoHeight = 768
        Assert.DoesNotContain("pad=", LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\x"))     ' within one percent of 16:9
    End Sub

    <Fact>
    Public Sub A_window_of_another_shape_is_fitted_and_padded_never_stretched()
        Dim options As New LiveEncoderOptions With {.WithVideo = True, .WithAudio = False, .VideoFps = 25, .InputVideoWidth = 800, .InputVideoHeight = 600}
        Dim args As String = LiveFfmpegArgs.VideoToMpegts(options, "\\.\pipe\x")
        Assert.Contains("-vf scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2 -c:v", args)
        Assert.False(LiveFfmpegArgs.SameAspect(options))
    End Sub

    <Fact>
    Public Sub The_remembered_window_round_trips_beside_the_monitor_index()
        Dim store As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim saved As New LiveProducerSettings With {.ScreenMonitorIndex = 1, .ScreenWindowTitle = "Plan - Notes", .ScreenWindowProcess = "notepad"}
        saved.Save(Sub(key, value) store(key) = value)

        Assert.Equal("Plan - Notes", store(LiveProducerSettings.KeyScreenWindowTitle))
        Assert.Equal("notepad", store(LiveProducerSettings.KeyScreenWindowProcess))
        Dim loaded As LiveProducerSettings = LiveProducerSettings.Load(Function(key) If(store.ContainsKey(key), store(key), CStr(Nothing)))
        Assert.Equal(1, loaded.ScreenMonitorIndex)
        Assert.Equal("Plan - Notes", loaded.ScreenWindowTitle)
        Assert.Equal("notepad", loaded.ScreenWindowProcess)
        Assert.True(loaded.HasScreenWindow)
    End Sub

    <Fact>
    Public Sub No_remembered_window_means_the_monitor()
        Dim loaded As LiveProducerSettings = LiveProducerSettings.Load(Function(key) CStr(Nothing))
        Assert.Equal("", loaded.ScreenWindowTitle)
        Assert.Equal("", loaded.ScreenWindowProcess)
        Assert.False(loaded.HasScreenWindow)
    End Sub

End Class

''' <summary>A video source that can end on its own, the way the window route does when the window is closed.</summary>
Friend Class EndingVideoSource
    Implements ILiveVideoSource
    Implements ILiveSourceEnding

    Public Property Stopped As Boolean
    Public Event Ended As Action(Of String) Implements ILiveSourceEnding.Ended

    Public ReadOnly Property DisplayName As String = "window fake (1280x720)" Implements ILiveVideoSource.DisplayName
    Public ReadOnly Property Width As Integer = 1280 Implements ILiveVideoSource.Width
    Public ReadOnly Property Height As Integer = 720 Implements ILiveVideoSource.Height
    Public ReadOnly Property FramesPerSecond As Integer = 25 Implements ILiveVideoSource.FramesPerSecond

    Public Function Start(sink As Action(Of LiveVideoFrame), ByRef reason As String) As Boolean Implements ILiveVideoSource.Start
        Return True
    End Function

    Public Sub [Stop]() Implements ILiveVideoSource.Stop
        Stopped = True
    End Sub

    Public Sub EndNow(reason As String)
        RaiseEvent Ended(reason)
    End Sub

End Class

Public Class LiveWindowRouteControllerTests

    Private NotInheritable Class Rig
        Public ReadOnly Encoder As New FakeEncoder()
        Public ReadOnly Sources As New Queue(Of EndingVideoSource)()
        Public ReadOnly StoppedIds As New List(Of String)()
        Public ReadOnly Store As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Public ReadOnly Port As Integer = LiveTestPorts.FreePort()

        Public Function Controller() As LiveBroadcastController
            Dim rig As Rig = Me
            Return New LiveBroadcastController(New LiveProducerPorts With {
                .GateEnabled = Function() True,
                .PickAddress = Function() "192.168.1.50",
                .LoadSettings = Function() New LiveProducerSettings With {.AudioPort = rig.Port},
                .EnrolledDeviceId = Function() "",
                .ReadStored = Function(key)
                                  Dim value As String = Nothing
                                  If rig.Store.TryGetValue(key, value) Then Return value
                                  Return Nothing
                              End Function,
                .WriteStored = Sub(key, value) rig.Store(key) = value,
                .CreateEncoder = Function() rig.Encoder,
                .CreateAudioSource = Function(kind) CType(Nothing, ILiveAudioSource),
                .CreateVideoSource = Function(kind) CType(rig.Sources.Dequeue(), ILiveVideoSource),
                .ClockMs = Function() 0L,
                .NewToken = Function() "aabbccdd",
                .ExchangeEndpointInfo = Function() Tuple.Create("relay.example:443", "sha256:" & New String("a"c, 64)),
                .BroadcastStopped = Sub(id) rig.StoppedIds.Add(id)
            })
        End Function

        Public Shared ReadOnly Request As New LiveStartRequest With {.Kind = LiveProducerSourceKind.Screen, .Mode = "VIDEO_ONLY"}
    End Class

    <Fact>
    Public Sub A_source_that_ends_stops_the_broadcast_once_and_the_window_is_told_why()
        Dim rig As New Rig()
        Dim source As New EndingVideoSource()
        rig.Sources.Enqueue(source)
        Dim controller As LiveBroadcastController = rig.Controller()
        Dim reasons As New List(Of String)()
        Dim ended As New ManualResetEventSlim(False)
        AddHandler controller.SourceEnded, Sub(reason)
                                               SyncLock reasons
                                                   reasons.Add(reason)
                                               End SyncLock
                                               ended.Set()
                                           End Sub
        Try
            Assert.True(controller.Start(Rig.Request).Ok)
            Assert.True(controller.State.Running)

            source.EndNow(LiveWindowCatalog.ReasonClosed)
            source.EndNow(LiveWindowCatalog.ReasonClosed)     ' a second close notice must not stop or announce twice

            Assert.True(ended.Wait(10000), "the controller did not react to the source ending")
            Thread.Sleep(150)
            Assert.False(controller.State.Running)
            Assert.True(source.Stopped)
            Assert.True(rig.Encoder.Stopped)
            SyncLock reasons
                Assert.Equal({LiveWindowCatalog.ReasonClosed}, reasons.ToArray())
            End SyncLock
            ' The exchange agent is told through the ordinary stop port, exactly as a manual stop does.
            Assert.Single(rig.StoppedIds)
        Finally
            controller.Stop()
        End Try
    End Sub

    <Fact>
    Public Sub A_late_end_notice_of_an_earlier_broadcast_does_not_stop_the_next_one()
        Dim rig As New Rig()
        Dim first As New EndingVideoSource()
        Dim second As New EndingVideoSource()
        rig.Sources.Enqueue(first)
        rig.Sources.Enqueue(second)
        Dim controller As LiveBroadcastController = rig.Controller()
        Dim ended As New ManualResetEventSlim(False)
        AddHandler controller.SourceEnded, Sub(reason) ended.Set()
        Try
            Assert.True(controller.Start(Rig.Request).Ok)
            controller.Stop()
            Assert.True(controller.Start(Rig.Request).Ok)

            first.EndNow(LiveWindowCatalog.ReasonClosed)      ' the old source's notice arrives after the restart

            Assert.False(ended.Wait(400), "an old source's notice stopped a newer broadcast")
            Assert.True(controller.State.Running)
            Assert.False(second.Stopped)
        Finally
            controller.Stop()
        End Try
    End Sub

End Class
#End If
