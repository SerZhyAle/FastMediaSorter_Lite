Option Strict On

Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Text.RegularExpressions
Imports FastMediaSorterCompanion
Imports Xunit

''' <summary>
''' The picture the QR zoom window hands over (SPECIFICATION_QR_SAVE_AND_COPY.md §4).
''' What is worth a test here is exactly what no manual click can check reliably: the
''' saved code is the code's own pixels at a deterministic size, never smoothed, never
''' downscaled, never cropped into its quiet zone - the properties that decide whether
''' the pasted code still scans after a messenger has re-compressed it.
''' </summary>
Public Class QrImageOutputTests

    Private Const MinSavedSide As Integer = 512

    ''' <summary>A QR bitmap: white with a black square inset, so the quiet zone and the
    ''' hard module edges are both visible in the result.</summary>
    Private Shared Function FakeCode(side As Integer) As Bitmap
        Dim bmp As New Bitmap(side, side, PixelFormat.Format32bppArgb)
        Using g As Graphics = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            Dim inset As Integer = side \ 10
            g.FillRectangle(Brushes.Black, inset, inset, side - 2 * inset, side - 2 * inset)
        End Using
        Return bmp
    End Function

    <Theory>
    <InlineData(120, 600)>      ' x5
    <InlineData(240, 720)>      ' x3
    <InlineData(300, 600)>      ' x2
    <InlineData(511, 1022)>     ' x2 - one pixel short of the floor still doubles
    Public Sub A_code_below_the_floor_grows_by_a_whole_number_factor(srcSide As Integer, expected As Integer)
        Using src As Bitmap = FakeCode(srcSide)
            Using out As Bitmap = Qr_Zoom_Form.RenderForOutput(src)
                Assert.Equal(expected, out.Width)
                Assert.Equal(expected, out.Height)
                Assert.True(out.Width >= MinSavedSide, "Saved code is under the 512 px floor.")
                Assert.Equal(0, out.Width Mod srcSide)   ' whole-number factor, never 1.4x
            End Using
        End Using
    End Sub

    <Theory>
    <InlineData(512)>
    <InlineData(690)>
    <InlineData(1000)>
    Public Sub A_code_at_or_above_the_floor_is_kept_pixel_for_pixel(srcSide As Integer)
        Using src As Bitmap = FakeCode(srcSide)
            Using out As Bitmap = Qr_Zoom_Form.RenderForOutput(src)
                ' Never downscaled: shrinking a code is the one resize that destroys modules.
                Assert.Equal(srcSide, out.Width)
                Assert.Equal(srcSide, out.Height)
            End Using
        End Using
    End Sub

    ''' <summary>The DIB that goes on the clipboard must be a plain opaque bitmap on white:
    ''' an alpha channel is what turns a pasted code into a black rectangle in some receivers.</summary>
    <Fact>
    Public Sub The_output_is_opaque_and_white_backed()
        Using src As Bitmap = FakeCode(200)
            Using out As Bitmap = Qr_Zoom_Form.RenderForOutput(src)
                Assert.Equal(PixelFormat.Format24bppRgb, out.PixelFormat)
                ' The quiet zone survives: the corner is still white, edge to edge.
                Assert.Equal(Color.White.ToArgb(), out.GetPixel(0, 0).ToArgb())
                Assert.Equal(Color.White.ToArgb(), out.GetPixel(out.Width - 1, out.Height - 1).ToArgb())
            End Using
        End Using
    End Sub

    ''' <summary>Nearest-neighbour, not smoothing: every pixel of the result is still pure
    ''' black or pure white. A grey pixel anywhere means an interpolated edge.</summary>
    <Fact>
    Public Sub Modules_stay_hard_black_and_white()
        Using src As Bitmap = FakeCode(160)
            Using out As Bitmap = Qr_Zoom_Form.RenderForOutput(src)
                Dim grey As Integer = 0
                For y As Integer = 0 To out.Height - 1 Step 3
                    For x As Integer = 0 To out.Width - 1 Step 3
                        Dim c As Color = out.GetPixel(x, y)
                        If Not ((c.R = 0 AndAlso c.G = 0 AndAlso c.B = 0) OrElse
                                (c.R = 255 AndAlso c.G = 255 AndAlso c.B = 255)) Then grey += 1
                    Next
                Next
                Assert.True(grey = 0, grey & " interpolated pixels - the upscale is smoothing the modules.")
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub No_source_gives_no_picture()
        Assert.Null(Qr_Zoom_Form.RenderForOutput(Nothing))
    End Sub

    ' ---------------------------------------------------------------- file name ----

    <Fact>
    Public Sub The_plain_name_is_the_timestamped_form()
        Dim name As String = Qr_Zoom_Form.OutputFileName("", New DateTime(2026, 8, 8, 21, 53, 0))
        Assert.Equal("fms-qr-20260808-2153.png", name)
        Assert.Matches(New Regex("^fms-qr-\d{8}-\d{4}\.png$"), name)
    End Sub

    <Fact>
    Public Sub A_caller_supplied_name_rides_in_front_of_the_stamp()
        Assert.Equal("fms-qr-dune-20260808-2153.png",
                     Qr_Zoom_Form.OutputFileName("Dune", New DateTime(2026, 8, 8, 21, 53, 0)))
    End Sub

    ''' <summary>Two windows opened a minute apart never overwrite each other's file, while
    ''' one window (which latches the name once) always overwrites its own.</summary>
    <Fact>
    Public Sub A_later_window_gets_a_different_name()
        Dim a As String = Qr_Zoom_Form.OutputFileName("", New DateTime(2026, 8, 8, 21, 53, 0))
        Dim b As String = Qr_Zoom_Form.OutputFileName("", New DateTime(2026, 8, 8, 21, 54, 0))
        Assert.NotEqual(a, b)
    End Sub

    <Theory>
    <InlineData("Photos 2026", "photos-2026")>
    <InlineData("C:\Users\me\Pictures", "c-users-me-pictures")>
    <InlineData("  ..  ", "")>
    <InlineData("", "")>
    <InlineData(Nothing, "")>
    Public Sub A_base_name_is_reduced_to_a_safe_file_name_part(input As String, expected As String)
        Assert.Equal(expected, Qr_Zoom_Form.SanitizeBase(input))
    End Sub

    <Fact>
    Public Sub A_long_base_name_is_truncated()
        Dim part As String = Qr_Zoom_Form.SanitizeBase(New String("a"c, 200))
        Assert.True(part.Length <= 32, "Base name grew to " & part.Length & " characters.")
    End Sub

End Class
