Option Strict On

Imports System.Drawing
Imports System.IO
Imports Xunit
Imports fmsl

''' <summary>
''' Pins the IImageDecoder seam (src/Imaging/) - the one place where the two shipped
''' viewers deliberately decode differently. This class runs under BOTH target
''' frameworks of this project, which is the whole point: net48 exercises the WIC +
''' ImageSharp 2 path of FastMediaSorter_x86.exe, net10 exercises the ImageSharp 3
''' path of FastMediaSorter_LITE.exe. See CLAUDE.md "Two builds, one source tree".
''' </summary>
Public Class ImageDecoderSeamTests

    ''' <summary>
    ''' A 120x90 static WEBP (lossy VP8), as bytes - no fixture file to lose.
    ''' </summary>
    Private Shared ReadOnly StaticWebp As Byte() = {
        &H52, &H49, &H46, &H46, &H5E, &H0, &H0, &H0, &H57, &H45, &H42, &H50,
        &H56, &H50, &H38, &H20, &H52, &H0, &H0, &H0, &H10, &H6, &H0, &H9D,
        &H1, &H2A, &H78, &H0, &H5A, &H0, &H3E, &H91, &H48, &HA1, &H4C, &HA5,
        &HA4, &H23, &H22, &H20, &HA8, &H0, &HB0, &H12, &H9, &H69, &HA, &H16,
        &HB6, &HC0, &H0, &HD7, &HF4, &H27, &HC0, &HDB, &HEA, &H61, &H33, &HE1,
        &H8, &HA8, &H7D, &H7C, &H44, &H1F, &H4D, &H7C, &H44, &H1F, &H4D, &H7C,
        &H44, &H1F, &H4D, &H79, &H80, &H0, &HFE, &HFD, &H41, &HBF, &HFE, &HBB,
        &HC5, &HA5, &H1A, &HFF, &HFF, &H99, &HA3, &HF4, &H9B, &HE9, &H37, &H76,
        &H1E, &H63, &HA6, &H84, &H0, &H0}

    ''' <summary>
    ''' The same 120x90 image as an ANIMATED WEBP (3 frames, VP8X/ANIM/ANMF). This is
    ''' the format that triggered the whole .NET 10 migration: it fails to decode on
    ''' Windows editions without the "WebP Image Extensions" OS codec (Server 2025).
    ''' </summary>
    Private Shared ReadOnly AnimatedWebp As Byte() = {
        &H52, &H49, &H46, &H46, &H94, &H1, &H0, &H0, &H57, &H45, &H42, &H50,
        &H56, &H50, &H38, &H58, &HA, &H0, &H0, &H0, &H2, &H0, &H0, &H0,
        &H77, &H0, &H0, &H59, &H0, &H0, &H41, &H4E, &H49, &H4D, &H6, &H0,
        &H0, &H0, &H0, &H0, &H0, &H0, &H1, &H0, &H41, &H4E, &H4D, &H46,
        &H74, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H77, &H0,
        &H0, &H59, &H0, &H0, &H90, &H1, &H0, &H0, &H56, &H50, &H38, &H20,
        &H5C, &H0, &H0, &H0, &H30, &H6, &H0, &H9D, &H1, &H2A, &H78, &H0,
        &H5A, &H0, &H3E, &H91, &H48, &HA1, &H4C, &HA5, &HA4, &H23, &H22, &H20,
        &HA8, &H0, &HB0, &H12, &H9, &H69, &HA, &H16, &HB6, &HC0, &H0, &HE4,
        &HB4, &H47, &H1B, &HB1, &H74, &HFB, &H46, &H6D, &HA6, &H45, &H64, &H24,
        &HE9, &H30, &H62, &H40, &H48, &H9, &H1, &H20, &H24, &H4, &H80, &H90,
        &H11, &H80, &H0, &HFE, &HFE, &H1A, &HA5, &HFF, &HFE, &HC5, &H9C, &HB6,
        &H5, &HE3, &HFF, &HFF, &HB9, &HC0, &HFF, &HB9, &HC0, &HFF, &HB9, &HC0,
        &HFE, &H36, &HB2, &H6E, &HF6, &HDB, &HF7, &H22, &HA0, &H40, &H0, &H0,
        &H41, &H4E, &H4D, &H46, &H76, &H0, &H0, &H0, &H0, &H0, &H0, &H0,
        &H0, &H0, &H77, &H0, &H0, &H59, &H0, &H0, &H90, &H1, &H0, &H0,
        &H56, &H50, &H38, &H20, &H5E, &H0, &H0, &H0, &HD0, &H6, &H0, &H9D,
        &H1, &H2A, &H78, &H0, &H5A, &H0, &H3E, &H91, &H48, &HA1, &H4C, &HA5,
        &HA4, &H23, &H22, &H20, &HA8, &H0, &HB0, &H12, &H9, &H69, &HA, &H16,
        &HB6, &HC0, &H3F, &H0, &H3F, &H0, &H0, &HF8, &HC8, &H8A, &H51, &HB6,
        &HAF, &H28, &H23, &HDF, &H28, &HA5, &H5F, &H5A, &H5A, &H25, &H3C, &H9D,
        &HF2, &H89, &HE9, &HE5, &HA5, &HA2, &H5B, &HDF, &H57, &H38, &H80, &H0,
        &HFE, &HFC, &H65, &H17, &HFF, &HFD, &H18, &H2E, &H3, &H81, &HEF, &HA5,
        &HFF, &HFF, &HB1, &H98, &HFD, &HA3, &H3F, &HC7, &H8E, &H36, &H7D, &H7B,
        &H12, &H35, &HB3, &HF6, &H10, &H0, &H41, &H4E, &H4D, &H46, &H6E, &H0,
        &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H0, &H77, &H0, &H0, &H59,
        &H0, &H0, &H90, &H1, &H0, &H0, &H56, &H50, &H38, &H20, &H56, &H0,
        &H0, &H0, &HF0, &H5, &H0, &H9D, &H1, &H2A, &H78, &H0, &H5A, &H0,
        &H3E, &H91, &H48, &HA1, &H4C, &HA5, &HA4, &H23, &H22, &H20, &HA8, &H0,
        &HB0, &H12, &H9, &H69, &HA, &H16, &HA0, &H47, &H55, &H12, &H41, &H5A,
        &HB7, &H5C, &HE4, &H54, &HF3, &H97, &H17, &H99, &HF6, &HE6, &H7D, &HB9,
        &H9F, &H6E, &H67, &HDB, &H99, &HF6, &HE6, &H7D, &HB3, &H80, &H0, &HFE,
        &HFE, &HD5, &H8D, &HFF, &HFF, &HB9, &HC0, &HFF, &HFD, &H9C, &HF, &HFF,
        &HD9, &HC0, &HFE, &H3A, &HFF, &HF5, &HEA, &H96, &HB9, &H3A, &H3F, &HEB,
        &H54, &H8A, &H0, &H0}

    Private Shared Function WriteTemp(bytes As Byte(), ext As String) As String
        Dim tmpPath As String = Path.Combine(Path.GetTempPath(), "fms_seamtest_" & Guid.NewGuid().ToString("N") & ext)
        File.WriteAllBytes(tmpPath, bytes)
        Return tmpPath
    End Function

    ''' <summary>
    ''' THE regression test for the seam's wiring. `#If NETFRAMEWORK` is defined by
    ''' hand in the net48 projects (the SDK does not do it), so a deleted or typo'd
    ''' DefineConstants silently swaps the decoder - no compile error, just the wrong
    ''' engine inside the wrong exe. This fails loudly instead.
    ''' </summary>
    <Fact>
    Public Sub Provider_ResolvesThePlatformDecoder()
        Dim decoderName As String = ImageDecoderProvider.Current.GetType().Name
#If NETFRAMEWORK Then
        Assert.Equal("LegacyWicImageDecoder", decoderName)
#Else
        Assert.Equal("ModernImageSharpDecoder", decoderName)
#End If
    End Sub

    <Fact>
    Public Sub Provider_IsStable()
        Assert.Same(ImageDecoderProvider.Current, ImageDecoderProvider.Current)
    End Sub

#If Not NETFRAMEWORK Then

    ''' <summary>
    ''' The migration's whole reason to exist: the modern decoder is fully managed, so
    ''' an ANIMATED WEBP decodes with no OS codec present. On net48 this same file goes
    ''' through WIC and depends on the machine having "WebP Image Extensions" - which is
    ''' exactly why this assertion is modern-only.
    ''' </summary>
    <Fact>
    Public Sub ModernDecoder_DecodesAnimatedWebp_WithoutAnOsCodec()
        Using ms As New MemoryStream(AnimatedWebp)
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(120, img.Width)
                Assert.Equal(90, img.Height)
                ' Animation is handed to GDI+ as a GIF transcode, so the existing GIF
                ' playback path animates it unchanged (see ModernImageSharpDecoder).
                Assert.Equal(System.Drawing.Imaging.ImageFormat.Gif, img.RawFormat)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub ModernDecoder_DecodesStaticWebp()
        Using ms As New MemoryStream(StaticWebp)
            Using img As Image = ImageDecoderProvider.Current.DecodeToImage(ms)
                Assert.NotNull(img)
                Assert.Equal(120, img.Width)
                Assert.Equal(90, img.Height)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Utils.GetImageDimensions routes .webp through the seam (every other format is
    ''' parsed from the header). The background worker relies on this never touching
    ''' GDI+, so the answer must come back from the decoder, not a decode.
    ''' </summary>
    <Fact>
    Public Sub GetImageDimensions_Webp_UsesTheSeam()
        Dim p As String = WriteTemp(AnimatedWebp, ".webp")
        Try
            Assert.Equal(New Size(120, 90), Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

#End If

#If NETFRAMEWORK Then
    ''' <summary>
    ''' The x86 viewer's WEBP fallback library, exercised head-on.
    '''
    ''' WHY IT CALLS ImageSharp DIRECTLY INSTEAD OF GOING THROUGH THE SEAM: the seam
    ''' only reaches ImageSharp when WIC THROWS (LegacyWicImageDecoder.DecodeToImage).
    ''' On a dev box with the WebP Image Extensions installed WIC succeeds, so a
    ''' seam-level test would pass no matter how broken the bundled ImageSharp is and
    ''' would prove nothing about the version we actually ship. On the Windows 7/8.1
    ''' machines the x86 exe exists for, WIC has NO WebP codec at all and this library
    ''' is the ONLY thing that decodes WEBP - it deserves a test of its own.
    '''
    ''' Note the placement: this block must stay OUTSIDE the "#If Not NETFRAMEWORK"
    ''' region above. Nested inside it the condition is unsatisfiable, and VB compiles
    ''' the tests to nothing while the run stays green - the exact silent-seam trap
    ''' CLAUDE.md warns about.
    ''' </summary>
    <Fact>
    Public Sub BundledImageSharp_DecodesWebp_TheOnlyWebpPathOnLegacyWindows()
        Using source As New MemoryStream(StaticWebp)
            Using decoded = SixLabors.ImageSharp.Image.Load(Of SixLabors.ImageSharp.PixelFormats.Bgra32)(source)
                Assert.Equal(120, decoded.Width)
                Assert.Equal(90, decoded.Height)
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' Pins a REAL, PRE-EXISTING GAP rather than a wish: ImageSharp 2.x cannot decode
    ''' animated WEBP at all ("Animated webp are not yet supported" - the feature only
    ''' arrived in 3.x, which is exactly why the modern viewer runs ImageSharp 3 and has
    ''' ModernDecoder_DecodesAnimatedWebp_WithoutAnOsCodec passing).
    '''
    ''' Consequence, stated plainly: on Windows 7/8.1 - where WIC has no WebP codec at
    ''' all - the x86 viewer CANNOT open an animated WEBP, and never could. On Windows
    ''' 10/11 it still opens, because WIC handles it before the fallback is reached.
    ''' This is not a regression from the 2.1.8 -> 2.1.13 security bump: 2.1.x never had
    ''' the feature. Asserting the throw keeps the limitation visible; if the x86 leg
    ''' ever moves to a library that supports animation, this test fails and says so.
    ''' </summary>
    <Fact>
    Public Sub BundledImageSharp_CannotDecodeAnimatedWebp_KnownLegacyGap()
        Using source As New MemoryStream(AnimatedWebp)
            Assert.Throws(Of NotSupportedException)(
                Function() SixLabors.ImageSharp.Image.Load(Of SixLabors.ImageSharp.PixelFormats.Bgra32)(source))
        End Using
    End Sub
#End If

    ''' <summary>
    ''' Garbage must degrade to Size.Empty on BOTH runtimes rather than throw out of
    ''' GetImageDimensions - the background worker calls this on whatever is in the
    ''' folder.
    ''' </summary>
    <Fact>
    Public Sub GetImageDimensions_CorruptWebp_ReturnsEmpty()
        Dim p As String = WriteTemp(New Byte() {&H52, &H49, &H46, &H46, 1, 2, 3, 4}, ".webp")
        Try
            Assert.Equal(Size.Empty, Utils.GetImageDimensions(p))
        Finally
            File.Delete(p)
        End Try
    End Sub

End Class
