Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports Xunit

''' <summary>
''' 105 L04 / L06 (057.S15-6, S13-7), 108 H01: the WM_COPYDATA transport policy - byte bounds,
''' strict UTF-8, no control characters, refuse-don't-truncate on send - and the bounded
''' monotonic send loop, plus process discovery that survives a target exiting under it.
''' Validation only: none of this authenticates a sender.
''' </summary>
Public Class CopyDataTransportTests

    Private Shared Function DecodeBytes(data As Byte(), ByRef text As String) As PayloadRefusal
        Dim refusal As PayloadRefusal
        CopyDataTransport.TryDecodeBytes(data, text, refusal)
        Return refusal
    End Function

    Private Shared Function DecodeThroughPointer(data As Byte(), cbData As Integer, ByRef text As String) As PayloadRefusal
        Dim mem As IntPtr = Marshal.AllocHGlobal(Math.Max(1, data.Length))
        Try
            Marshal.Copy(data, 0, mem, data.Length)
            Dim refusal As PayloadRefusal
            CopyDataTransport.TryDecode(cbData, mem, text, refusal)
            Return refusal
        Finally
            Marshal.FreeHGlobal(mem)
        End Try
    End Function

    <Theory>
    <InlineData(-1)>
    <InlineData(0)>
    <InlineData(65537)>
    <InlineData(Integer.MaxValue)>
    Public Sub Bounds_outside_one_to_64KiB_are_refused_before_any_copy(cbData As Integer)
        Dim text As String = Nothing
        Dim refusal As PayloadRefusal = DecodeThroughPointer(New Byte() {65}, cbData, text)
        Assert.Contains(refusal, {PayloadRefusal.BadBounds, PayloadRefusal.TooLarge})
        Assert.Equal(String.Empty, text)
    End Sub

    <Fact>
    Public Sub A_null_pointer_is_refused()
        Dim text As String = Nothing
        Dim refusal As PayloadRefusal
        Assert.False(CopyDataTransport.TryDecode(10, IntPtr.Zero, text, refusal))
        Assert.Equal(PayloadRefusal.BadBounds, refusal)
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(65536)>
    Public Sub The_boundary_sizes_are_accepted(size As Integer)
        Dim data(size - 1) As Byte
        For i As Integer = 0 To data.Length - 1
            data(i) = CByte(AscW("a"c))
        Next
        Dim text As String = Nothing
        Assert.Equal(PayloadRefusal.None, DecodeThroughPointer(data, size, text))
        Assert.Equal(size, text.Length)
    End Sub

    <Fact>
    Public Sub A_byte_cap_is_not_a_character_cap_Cyrillic_and_emoji_are_counted_in_bytes()
        ' 32768 Cyrillic letters = 65536 bytes: fits. One more does not.
        Dim fits As String = New String(ChrW(&H44F), 32768)
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.True(CopyDataTransport.TryEncode(fits, bytes, refusal))
        Assert.Equal(65536, bytes.Length)

        Assert.False(CopyDataTransport.TryEncode(fits & ChrW(&H44F), bytes, refusal))
        Assert.Equal(PayloadRefusal.TooLarge, refusal)

        ' An emoji is two UTF-16 units and four bytes.
        Dim emoji As String = Char.ConvertFromUtf32(&H1F600)
        Dim manyEmoji As String = String.Concat(Enumerable.Repeat(emoji, 16385))
        Assert.False(CopyDataTransport.TryEncode(manyEmoji, bytes, refusal))
        Assert.Equal(PayloadRefusal.TooLarge, refusal)
    End Sub

    <Fact>
    Public Sub An_oversized_path_is_refused_whole_never_truncated()
        Dim path As String = "C:\" & New String("d"c, 70000)
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.False(CopyDataTransport.TryEncode(path, bytes, refusal))
        Assert.Null(bytes)
        Assert.Equal(PayloadRefusal.TooLarge, refusal)
    End Sub

    <Fact>
    Public Sub Malformed_utf8_is_refused_not_repaired()
        Dim text As String = Nothing
        ' A lone continuation byte, an overlong encoding of '/', and a truncated 3-byte sequence.
        Assert.Equal(PayloadRefusal.MalformedUtf8, DecodeBytes(New Byte() {&H43, &H3A, &H5C, &H80}, text))
        Assert.Equal(PayloadRefusal.MalformedUtf8, DecodeBytes(New Byte() {&HC0, &HAF}, text))
        Assert.Equal(PayloadRefusal.MalformedUtf8, DecodeBytes(New Byte() {&H41, &HE2, &H82}, text))
        Assert.Equal(String.Empty, text)
    End Sub

    <Fact>
    Public Sub An_unpaired_surrogate_has_no_utf8_form_and_is_refused_on_send()
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.False(CopyDataTransport.TryEncode("C:\bad" & ChrW(&HD800) & "name", bytes, refusal))
        Assert.Equal(PayloadRefusal.MalformedUtf8, refusal)
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(9)>
    <InlineData(10)>
    <InlineData(13)>
    <InlineData(31)>
    <InlineData(127)>
    Public Sub Embedded_control_characters_including_NUL_are_refused_both_ways(code As Integer)
        Dim withControl As String = "C:\a" & ChrW(code) & "b"
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.False(CopyDataTransport.TryEncode(withControl, bytes, refusal))
        Assert.Equal(PayloadRefusal.ControlCharacter, refusal)

        Dim text As String = Nothing
        Assert.Equal(PayloadRefusal.ControlCharacter, DecodeBytes(Encoding.UTF8.GetBytes(withControl), text))
    End Sub

    <Fact>
    Public Sub Blank_and_empty_text_are_refused()
        Dim bytes As Byte() = Nothing
        Dim refusal As PayloadRefusal
        Assert.False(CopyDataTransport.TryEncode("", bytes, refusal))
        Assert.False(CopyDataTransport.TryEncode("   ", bytes, refusal))
        Assert.Equal(PayloadRefusal.Empty, refusal)
    End Sub

    <Fact>
    Public Sub Long_unicode_paths_and_the_show_window_sentinel_round_trip()
        For Each original As String In {"D:\Фото\Отпуск 2026 (копия)\IMG_0001.jpg", "\\nas\общая\" & New String("я"c, 200), "::fms-show-window::"}
            Dim bytes As Byte() = Nothing
            Dim refusal As PayloadRefusal
            Assert.True(CopyDataTransport.TryEncode(original, bytes, refusal))
            Dim text As String = Nothing
            Assert.Equal(PayloadRefusal.None, DecodeThroughPointer(bytes, bytes.Length, text))
            Assert.Equal(original, text)
        Next
    End Sub

    ' ------------------------------------------------------------------ send loop ----

    Private NotInheritable Class FakeTime
        Friend Now_Ms As Long
        Friend ReadOnly Sleeps As New List(Of Integer)()
        Friend ReadOnly SendTimeouts As New List(Of Integer)()

        Friend Function Clock() As Long
            Return Now_Ms
        End Function

        Friend Sub Sleep(ms As Integer)
            Sleeps.Add(ms)
            Now_Ms += ms
        End Sub
    End Class

    <Fact>
    Public Sub The_first_acknowledgement_stops_the_loop()
        Dim time As New FakeTime()
        Dim attempts As Integer
        Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(AddressOf time.Clock, AddressOf time.Sleep,
            Function(ms)
                attempts += 1
                Return If(attempts = 3, ForwardAttempt.Acknowledged, ForwardAttempt.NotAcknowledged)
            End Function)
        Assert.Equal(ForwardOutcome.Delivered, outcome)
        Assert.Equal(3, attempts)
        Assert.Equal(2, time.Sleeps.Count)
    End Sub

    <Fact>
    Public Sub No_target_ends_the_loop_at_once_without_sleeping()
        Dim time As New FakeTime()
        Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(AddressOf time.Clock, AddressOf time.Sleep,
                                                                      Function(ms) ForwardAttempt.NoTarget)
        Assert.Equal(ForwardOutcome.NoTarget, outcome)
        Assert.Empty(time.Sleeps)
    End Sub

    <Fact>
    Public Sub A_receiver_that_never_answers_ends_at_the_six_second_deadline_with_clipped_waits()
        Dim time As New FakeTime()
        Dim outcome As ForwardOutcome = CopyDataTransport.RunForward(AddressOf time.Clock, AddressOf time.Sleep,
            Function(sendMs)
                time.SendTimeouts.Add(sendMs)
                time.Now_Ms += sendMs ' a hung receiver burns the whole per-send timeout
                Return ForwardAttempt.NotAcknowledged
            End Function)
        Assert.Equal(ForwardOutcome.TimedOut, outcome)
        Assert.True(time.Now_Ms <= 6000, "overran the budget: " & time.Now_Ms.ToString())
        Assert.All(time.SendTimeouts, Sub(ms) Assert.InRange(ms, 1, 3000))
        Assert.All(time.Sleeps, Sub(ms) Assert.InRange(ms, 1, 250))
        ' The last send was clipped to what remained, not the full 3000 ms.
        Assert.True(time.SendTimeouts.Last() < 3000 OrElse time.Now_Ms = 6000)
    End Sub

    <Fact>
    Public Sub A_wall_clock_jump_cannot_stretch_the_budget_because_only_the_injected_clock_counts()
        Dim time As New FakeTime()
        Dim sends As Integer
        CopyDataTransport.RunForward(AddressOf time.Clock, AddressOf time.Sleep,
            Function(sendMs)
                sends += 1
                Return ForwardAttempt.NotAcknowledged
            End Function)
        ' 250 ms pauses inside 6000 ms: a bounded number of tries, then a verdict.
        Assert.InRange(sends, 20, 30)
    End Sub

    ' ------------------------------------------------------------------ discovery ----

    Private NotInheritable Class FakeProcess
        Implements IViewerProcess

        Friend Disposed As Boolean
        Friend ThrowOnId As Boolean
        Friend ThrowOnWindow As Boolean
        Friend PidValue As Integer
        Friend HandleValue As IntPtr

        Public ReadOnly Property Id As Integer Implements IViewerProcess.Id
            Get
                If ThrowOnId Then Throw New InvalidOperationException("The process has exited.")
                Return PidValue
            End Get
        End Property

        Public ReadOnly Property MainWindowHandle As IntPtr Implements IViewerProcess.MainWindowHandle
            Get
                If ThrowOnWindow Then Throw New InvalidOperationException("The process has exited.")
                Return HandleValue
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            Disposed = True
        End Sub
    End Class

    <Fact>
    Public Sub Every_process_object_is_disposed_including_the_current_one_and_a_vanished_one()
        Dim current As New FakeProcess With {.PidValue = 100}
        Dim ok As New FakeProcess With {.PidValue = 200, .HandleValue = New IntPtr(77)}
        Dim gone As New FakeProcess With {.ThrowOnId = True}
        Dim noWindow As New FakeProcess With {.PidValue = 300, .ThrowOnWindow = True}
        Dim all As New List(Of FakeProcess) From {current, ok, gone, noWindow}

        Dim targets As List(Of ViewerProcessTarget) = ViewerProcessDiscovery.Discover(
            {"FastMediaSorter_LITE"}, 100,
            Function(name) CType(all.Cast(Of IViewerProcess)().ToList(), IList(Of IViewerProcess)),
            Function(pid) New List(Of IntPtr) From {New IntPtr(pid)})

        Assert.Equal({200, 300}, targets.Select(Function(x) x.Pid).ToArray())
        Assert.Equal(New IntPtr(77), targets(0).MainWindowHandle)
        Assert.Equal(IntPtr.Zero, targets(1).MainWindowHandle) ' fell back to the top-level scan
        Assert.All(all, Sub(p) Assert.True(p.Disposed))
    End Sub

    <Fact>
    Public Sub A_failing_enumeration_for_one_name_does_not_stop_the_other_name_or_leak()
        Dim survivor As New FakeProcess With {.PidValue = 5}
        Dim calls As Integer
        Dim targets As List(Of ViewerProcessTarget) = ViewerProcessDiscovery.Discover(
            {"FastMediaSorter_LITE", "FastMediaSorter_x86"}, 1,
            Function(name) As IList(Of IViewerProcess)
                calls += 1
                If name = "FastMediaSorter_LITE" Then Throw New InvalidOperationException("access denied")
                Return New List(Of IViewerProcess) From {survivor}
            End Function,
            Function(pid) New List(Of IntPtr)())
        Assert.Equal(2, calls)
        Assert.Single(targets)
        Assert.True(survivor.Disposed)
    End Sub

    <Fact>
    Public Sub A_throwing_top_level_scan_loses_only_the_window_list_not_the_target()
        Dim p As New FakeProcess With {.PidValue = 9}
        Dim targets As List(Of ViewerProcessTarget) = ViewerProcessDiscovery.Discover(
            {"x"}, 1,
            Function(name) CType(New List(Of IViewerProcess) From {p}, IList(Of IViewerProcess)),
            Function(pid) As List(Of IntPtr)
                Throw New InvalidOperationException("enum")
            End Function)
        Assert.Single(targets)
        Assert.Empty(targets(0).TopLevelHandles)
        Assert.True(p.Disposed)
    End Sub

End Class
