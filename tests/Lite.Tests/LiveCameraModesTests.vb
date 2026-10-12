#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports Xunit

' Ticket 199 section 4 (phase B3) and section 8: the camera's mode picker is PURE - the judgement
' of which capture mode a broadcast uses lives in LiveCameraModes, so the suite drives it without
' a device. The numbers it guards are the item C shape's own: the encoder's 1280x720 is the cap the
' engine scales DOWN to, and the picker never chooses a mode above it when one fits.

Public Class LiveCameraModesTests

    Private Shared Function Mode(width As Integer, height As Integer, fps As Integer) As LiveCameraMode
        Return New LiveCameraMode With {.Width = width, .Height = height, .FramesPerSecond = fps}
    End Function

    <Fact>
    Public Sub Nothing_is_picked_from_an_empty_or_missing_list()
        Assert.Null(LiveCameraModes.PickBest(Nothing))
        Assert.Null(LiveCameraModes.PickBest(New List(Of LiveCameraMode)()))
    End Sub

    <Fact>
    Public Sub The_largest_mode_that_fits_the_shape_wins()
        ' A typical camera's list: VGA, 720p and 1080p. The 720p mode is the largest that fits the
        ' item C cap - the 1080p mode is NOT chosen even though it is larger.
        Dim modes As New List(Of LiveCameraMode) From {
            Mode(640, 480, 30), Mode(1280, 720, 30), Mode(1920, 1080, 30)
        }
        Dim picked As LiveCameraMode = LiveCameraModes.PickBest(modes)
        Assert.Equal(1280, picked.Width)
        Assert.Equal(720, picked.Height)
        Assert.Equal(30, picked.FramesPerSecond)
    End Sub

    <Fact>
    Public Sub Where_every_mode_is_above_the_cap_the_smallest_one_wins()
        ' The least scaling of a bad hand: 1080p beats 4K.
        Dim modes As New List(Of LiveCameraMode) From {
            Mode(3840, 2160, 30), Mode(1920, 1080, 25)
        }
        Dim picked As LiveCameraMode = LiveCameraModes.PickBest(modes)
        Assert.Equal(1920, picked.Width)
        Assert.Equal(1080, picked.Height)
    End Sub

    <Fact>
    Public Sub Equal_areas_tie_break_on_the_higher_frame_rate()
        Dim modes As New List(Of LiveCameraMode) From {
            Mode(1280, 720, 15), Mode(1280, 720, 30), Mode(1280, 720, 25)
        }
        Dim picked As LiveCameraMode = LiveCameraModes.PickBest(modes)
        Assert.Equal(30, picked.FramesPerSecond)
    End Sub

    <Fact>
    Public Sub A_fitting_mode_always_beats_a_larger_one_above_the_cap()
        Dim modes As New List(Of LiveCameraMode) From {
            Mode(2560, 1440, 60), Mode(320, 240, 30)
        }
        Dim picked As LiveCameraMode = LiveCameraModes.PickBest(modes)
        Assert.Equal(320, picked.Width)
        Assert.Equal(240, picked.Height)
    End Sub

    <Fact>
    Public Sub Impossible_rows_are_skipped_not_fatal()
        Dim modes As New List(Of LiveCameraMode) From {
            Nothing, Mode(0, 720, 30), Mode(640, -480, 30), Mode(640, 480, 0), Mode(320, 240, 15)
        }
        Dim picked As LiveCameraMode = LiveCameraModes.PickBest(modes)
        Assert.Equal(320, picked.Width)
        Assert.Equal(240, picked.Height)
    End Sub

    <Fact>
    Public Sub A_stored_row_is_honored_and_a_missing_or_bad_one_falls_back()
        Dim modes As New List(Of LiveCameraMode) From {
            Mode(640, 480, 30), Mode(1280, 720, 30)
        }
        ' Row 1 is the stored pick.
        Dim stored As LiveCameraMode = LiveCameraModes.PickAt(modes, 1)
        Assert.Equal(1280, stored.Width)

        ' -1 means the automatic best.
        Assert.Equal(1280, LiveCameraModes.PickAt(modes, -1).Width)
        ' A row the list no longer has, or an impossible one, hands the choice back (the source then
        ' falls back to PickBest).
        Assert.Null(LiveCameraModes.PickAt(modes, 9))
        Assert.Null(LiveCameraModes.PickAt(New List(Of LiveCameraMode) From {Mode(0, 0, 0)}, 0))
        Assert.Null(LiveCameraModes.PickAt(Nothing, 0))
    End Sub

End Class
#End If
