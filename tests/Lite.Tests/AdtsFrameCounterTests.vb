#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

' The audio-arrived measure behind the steady-state drift guard (contract-sync work item 2).

Public Class AdtsFrameCounterTests

    Private Shared Function Frames(count As Integer, Optional length As Integer = 371) As Byte()
        Dim all As New List(Of Byte)()
        For i As Integer = 1 To count
            all.AddRange(LiveMockDevice.AdtsFrame(length))
        Next
        Return all.ToArray()
    End Function

    <Fact>
    Public Sub Counts_Frames_And_Sums_Their_Duration()
        Dim counter As New AdtsFrameCounter()
        Dim data As Byte() = Frames(43)
        counter.Feed(data, 0, data.Length)

        Assert.Equal(43L, counter.FrameCount)
        ' 43 frames x 1024 samples / 44100 Hz = 998.4 ms
        Assert.InRange(counter.DurationMs, 998.0, 999.0)
        Assert.Equal(0L, counter.SkippedBytes)
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(3)>
    <InlineData(7)>
    <InlineData(100)>
    <InlineData(371)>
    <InlineData(1000)>
    Public Sub A_Frame_May_Be_Split_At_Any_Boundary(chunk As Integer)
        Dim data As Byte() = Frames(10)
        Dim counter As New AdtsFrameCounter()
        Dim offset As Integer = 0
        While offset < data.Length
            Dim n As Integer = Math.Min(chunk, data.Length - offset)
            counter.Feed(data, offset, n)
            offset += n
        End While
        Assert.Equal(10L, counter.FrameCount)
        Assert.Equal(0L, counter.SkippedBytes)
    End Sub

    <Fact>
    Public Sub A_Late_Joiner_Skips_To_The_Next_Sync_Word()
        ' A broadcast joined mid-frame: the tail of a frame, then whole frames.
        Dim tail(199) As Byte
        For i As Integer = 0 To tail.Length - 1
            tail(i) = CByte(i Mod 200)       ' never 0xFF
        Next
        Dim data As Byte() = Frames(5)
        Dim counter As New AdtsFrameCounter()
        counter.Feed(tail, 0, tail.Length)
        counter.Feed(data, 0, data.Length)

        Assert.Equal(5L, counter.FrameCount)
        Assert.Equal(200L, counter.SkippedBytes)
    End Sub

    <Fact>
    Public Sub A_False_Sync_Word_Costs_Bytes_Not_The_Count()
        ' FF F1 followed by a header that cannot be real (frame length below 7), then a real stream.
        Dim junk As Byte() = {&HFF, &HF1, &H50, &H0, &H0, &H0, &HFC, &H1, &H2}
        Dim data As Byte() = Frames(4)
        Dim counter As New AdtsFrameCounter()
        counter.Feed(junk, 0, junk.Length)
        counter.Feed(data, 0, data.Length)

        Assert.Equal(4L, counter.FrameCount)
        Assert.True(counter.SkippedBytes > 0L)
    End Sub

    <Fact>
    Public Sub An_Impossible_Sampling_Frequency_Index_Is_Not_A_Frame()
        Dim bad As Byte() = LiveMockDevice.AdtsFrame()
        bad(2) = CByte((bad(2) And &HC3) Or (15 << 2))   ' index 15 does not exist
        Dim counter As New AdtsFrameCounter()
        counter.Feed(bad, 0, bad.Length)
        Assert.Equal(0L, counter.FrameCount)
    End Sub

    <Fact>
    Public Sub Two_Raw_Data_Blocks_Per_Frame_Count_Twice_The_Time()
        Dim frame As Byte() = LiveMockDevice.AdtsFrame()
        frame(6) = CByte((frame(6) And &HFC) Or 1)      ' number_of_raw_data_blocks_in_frame = 1 -> two blocks
        Dim counter As New AdtsFrameCounter()
        counter.Feed(frame, 0, frame.Length)
        Assert.Equal(1L, counter.FrameCount)
        Assert.InRange(counter.DurationMs, 46.0, 47.0)
    End Sub

    <Fact>
    Public Sub Another_Sample_Rate_Is_Read_From_The_Header()
        Dim frame As Byte() = LiveMockDevice.AdtsFrame()
        frame(2) = CByte((frame(2) And &HC3) Or (3 << 2))   ' index 3 = 48000 Hz
        Dim counter As New AdtsFrameCounter()
        counter.Feed(frame, 0, frame.Length)
        Assert.InRange(counter.DurationMs, 21.0, 21.5)       ' 1024 / 48000
    End Sub

    <Fact>
    Public Sub Empty_And_Null_Input_Change_Nothing()
        Dim counter As New AdtsFrameCounter()
        counter.Feed(Nothing, 0, 0)
        counter.Feed(New Byte() {}, 0, 0)
        Assert.Equal(0L, counter.FrameCount)
        Assert.Equal(0.0, counter.DurationMs)
    End Sub

End Class
#End If
