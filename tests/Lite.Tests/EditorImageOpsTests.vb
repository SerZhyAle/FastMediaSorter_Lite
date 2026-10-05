#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports Xunit

''' <summary>
''' The crop itself - the editor's one edit that changes the size of the picture
''' (SPECIFICATION_IMAGE_EDITOR_DOTNET10.md §6.1).
'''
''' Worth a test rather than an eye: a crop that is off by one pixel, or that has been
''' quietly resampled through the default interpolation, looks exactly right on a canvas
''' scaled to a fifth of the picture. It is found later, in the saved file.
'''
''' Modern-only, like the editor: on the net48 leg this and the module compile to nothing.
''' </summary>
Public Class EditorImageOpsTests

    ''' <summary>A picture whose every pixel names its own coordinates, so a shifted crop
    ''' cannot pass by accident.</summary>
    Private Shared Function Coordinates(width As Integer, height As Integer) As Bitmap
        Dim map As New Bitmap(width, height, PixelFormat.Format32bppArgb)
        For y As Integer = 0 To height - 1
            For x As Integer = 0 To width - 1
                map.SetPixel(x, y, Color.FromArgb(255, x * 10, y * 10, 7))
            Next
        Next
        Return map
    End Function

    <Fact>
    Public Sub The_crop_keeps_exactly_the_pixels_under_the_frame()
        Using source As Bitmap = Coordinates(20, 16)
            Using cropped As Bitmap = EditorImageOps.CropTo(source, New Rectangle(4, 3, 6, 5))
                Assert.Equal(6, cropped.Width)
                Assert.Equal(5, cropped.Height)

                For y As Integer = 0 To cropped.Height - 1
                    For x As Integer = 0 To cropped.Width - 1
                        Assert.Equal(source.GetPixel(4 + x, 3 + y), cropped.GetPixel(x, y))
                    Next
                Next
            End Using
        End Using
    End Sub

    ''' <summary>The one that catches a blend instead of a copy: a semi-transparent pixel
    ''' composited onto the new bitmap's transparent surface comes back darker.</summary>
    <Fact>
    Public Sub Transparency_survives_the_crop_unblended()
        Using source As New Bitmap(4, 4, PixelFormat.Format32bppArgb)
            source.SetPixel(1, 1, Color.FromArgb(128, 255, 0, 0))
            Using cropped As Bitmap = EditorImageOps.CropTo(source, New Rectangle(1, 1, 2, 2))
                Assert.Equal(Color.FromArgb(128, 255, 0, 0), cropped.GetPixel(0, 0))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_frame_over_the_edge_crops_what_is_really_there()
        Using source As Bitmap = Coordinates(10, 10)
            Using cropped As Bitmap = EditorImageOps.CropTo(source, New Rectangle(6, 6, 40, 40))
                ' Clamped, not filled with transparent nothing along two sides.
                Assert.Equal(4, cropped.Width)
                Assert.Equal(4, cropped.Height)
                Assert.Equal(source.GetPixel(6, 6), cropped.GetPixel(0, 0))
                Assert.Equal(source.GetPixel(9, 9), cropped.GetPixel(3, 3))
            End Using
        End Using
    End Sub

    ''' <summary>An indexed source is the case that would otherwise open fine and throw on
    ''' the first stroke afterwards: Graphics.FromImage refuses an indexed bitmap.</summary>
    <Fact>
    Public Sub The_result_is_always_a_drawable_surface()
        Using source As New Bitmap(8, 8, PixelFormat.Format8bppIndexed)
            Using cropped As Bitmap = EditorImageOps.CropTo(source, New Rectangle(2, 2, 4, 4))
                Assert.Equal(PixelFormat.Format32bppArgb, cropped.PixelFormat)
                Using g As Graphics = Graphics.FromImage(cropped)
                    Assert.NotNull(g)
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_frame_of_no_size_still_produces_a_picture()
        Using source As Bitmap = Coordinates(8, 8)
            Using cropped As Bitmap = EditorImageOps.CropTo(source, New Rectangle(3, 3, 0, 0))
                ' One pixel rather than New Bitmap(0, 0), which is a GDI+ exception.
                Assert.Equal(1, cropped.Width)
                Assert.Equal(1, cropped.Height)
                Assert.Equal(source.GetPixel(3, 3), cropped.GetPixel(0, 0))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Nothing_in_gives_nothing_out()
        Assert.Null(EditorImageOps.CropTo(Nothing, New Rectangle(0, 0, 4, 4)))
    End Sub

    ' ------------------------------------------------- rotate and flip (126 Ф-3) ----
    '
    ' The rotate and flip are worth the same kind of test as the crop: a right angle
    ' cannot be resampled, but a wrong direction or a mirror of the wrong axis is exactly
    ' the kind of mistake that looks fine on screen and is found later, in the file.

    ''' <summary>One pixel of every colour in a 2x2: R G over B W. A quarter turn maps
    ''' source (x, y) onto (H-1-y, x), so clockwise gives B R over W G.</summary>
    Private Shared Function TwoByTwo() As Bitmap
        Dim map As New Bitmap(2, 2, PixelFormat.Format32bppArgb)
        map.SetPixel(0, 0, Color.Red)
        map.SetPixel(1, 0, Color.Lime)
        map.SetPixel(0, 1, Color.Blue)
        map.SetPixel(1, 1, Color.White)
        Return map
    End Function

    <Fact>
    Public Sub A_clockwise_turn_swaps_the_sides_and_moves_the_pixels_exactly()
        Using source As Bitmap = TwoByTwo()
            Using turned As Bitmap = EditorImageOps.Rotate90(source, clockwise:=True)
                Assert.Equal(2, turned.Width)
                Assert.Equal(2, turned.Height)
                Assert.Equal(Color.Blue.ToArgb(), turned.GetPixel(0, 0).ToArgb())
                Assert.Equal(Color.Red.ToArgb(), turned.GetPixel(1, 0).ToArgb())
                Assert.Equal(Color.White.ToArgb(), turned.GetPixel(0, 1).ToArgb())
                Assert.Equal(Color.Lime.ToArgb(), turned.GetPixel(1, 1).ToArgb())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_counter_clockwise_turn_is_the_other_direction()
        Using source As Bitmap = TwoByTwo()
            Using turned As Bitmap = EditorImageOps.Rotate90(source, clockwise:=False)
                ' The mirror mapping of the clockwise one: G W over R B.
                Assert.Equal(Color.Lime.ToArgb(), turned.GetPixel(0, 0).ToArgb())
                Assert.Equal(Color.White.ToArgb(), turned.GetPixel(1, 0).ToArgb())
                Assert.Equal(Color.Red.ToArgb(), turned.GetPixel(0, 1).ToArgb())
                Assert.Equal(Color.Blue.ToArgb(), turned.GetPixel(1, 1).ToArgb())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Four_quarters_come_back_to_the_start()
        Using source As Bitmap = TwoByTwo()
            Using once As Bitmap = EditorImageOps.Rotate90(source, True)
                Using twice As Bitmap = EditorImageOps.Rotate90(once, True)
                    Using thrice As Bitmap = EditorImageOps.Rotate90(twice, True)
                        Using four As Bitmap = EditorImageOps.Rotate90(thrice, True)
                            For y As Integer = 0 To 1
                                For x As Integer = 0 To 1
                                    Assert.Equal(source.GetPixel(x, y), four.GetPixel(x, y))
                                Next
                            Next
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_horizontal_flip_swaps_left_and_right_only()
        Using source As Bitmap = TwoByTwo()
            Using mirrored As Bitmap = EditorImageOps.Mirror(source, horizontal:=True)
                Assert.Equal(Color.Lime.ToArgb(), mirrored.GetPixel(0, 0).ToArgb())
                Assert.Equal(Color.Red.ToArgb(), mirrored.GetPixel(1, 0).ToArgb())
                Assert.Equal(Color.White.ToArgb(), mirrored.GetPixel(0, 1).ToArgb())
                Assert.Equal(Color.Blue.ToArgb(), mirrored.GetPixel(1, 1).ToArgb())
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub A_vertical_flip_swaps_top_and_bottom_only()
        Using source As Bitmap = TwoByTwo()
            Using mirrored As Bitmap = EditorImageOps.Mirror(source, horizontal:=False)
                Assert.Equal(Color.Blue.ToArgb(), mirrored.GetPixel(0, 0).ToArgb())
                Assert.Equal(Color.White.ToArgb(), mirrored.GetPixel(1, 0).ToArgb())
                Assert.Equal(Color.Red.ToArgb(), mirrored.GetPixel(0, 1).ToArgb())
                Assert.Equal(Color.Lime.ToArgb(), mirrored.GetPixel(1, 1).ToArgb())
            End Using
        End Using
    End Sub

    ''' <summary>The undo step is the untouched original: the operations work on a copy,
    ''' so the caller's bitmap keeps its pixels after a turn.</summary>
    <Fact>
    Public Sub The_source_is_never_touched()
        Using source As Bitmap = TwoByTwo()
            Using EditorImageOps.Rotate90(source, True)
            End Using
            Using EditorImageOps.Mirror(source, True)
            End Using
            Assert.Equal(Color.Red.ToArgb(), source.GetPixel(0, 0).ToArgb())
            Assert.Equal(Color.Lime.ToArgb(), source.GetPixel(1, 0).ToArgb())
            Assert.Equal(Color.Blue.ToArgb(), source.GetPixel(0, 1).ToArgb())
            Assert.Equal(Color.White.ToArgb(), source.GetPixel(1, 1).ToArgb())
        End Using
    End Sub

    <Fact>
    Public Sub A_rectangle_turns_into_a_rectangle_of_swapped_sides()
        Using source As Bitmap = Coordinates(24, 12)
            Using turned As Bitmap = EditorImageOps.Rotate90(source, clockwise:=True)
                Assert.Equal(12, turned.Width)
                Assert.Equal(24, turned.Height)
                ' Corners travel with the picture: source (0,0) lands at (11,0).
                Assert.Equal(source.GetPixel(0, 0), turned.GetPixel(11, 0))
                Assert.Equal(source.GetPixel(23, 11), turned.GetPixel(0, 23))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Nothing_in_gives_nothing_out_for_the_turns_too()
        Assert.Null(EditorImageOps.Rotate90(Nothing, True))
        Assert.Null(EditorImageOps.Mirror(Nothing, True))
    End Sub

End Class
#End If
