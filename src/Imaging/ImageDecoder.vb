Option Strict On

Imports System.Drawing
Imports System.IO

''' <summary>
''' Seam between the shared media pipeline and the platform image decoder
''' (SPECIFICATION_DOTNET10_MODERN_BUILD.md §6.1). The net48 build decodes via
''' WIC with an ImageSharp 2 fallback; the .NET 10 build decodes with
''' ImageSharp 3 (no OS codec involved). Shared code (FileManager, Utils)
''' only ever sees this interface.
''' </summary>
Public Interface IImageDecoder

    ''' <summary>
    ''' Decodes a format GDI+ cannot open natively (WEBP today) into a GDI+
    ''' image. The input stream stays owned by the caller. Returns Nothing when
    ''' the data cannot be decoded (caller treats it as a load failure).
    ''' </summary>
    Function DecodeToImage(stream As MemoryStream) As Image

    ''' <summary>
    ''' Reads pixel dimensions without a full GDI+ decode, for formats the
    ''' manual header parsers in <see cref="Utils.GetImageDimensions"/> do not
    ''' cover (WEBP today). Returns Size.Empty when unknown. Must not touch
    ''' GDI+ - the background worker calls this while the UI thread may be
    ''' decoding the same image.
    ''' </summary>
    Function TryGetPixelSize(stream As Stream) As Size

End Interface

''' <summary>
''' How much a single managed decode may ask for, decided BEFORE it starts (audit 048,
''' A-17). A highly compressible animated WEBP of 10000x10000 x 100 frames is a few KB on
''' disk and ~40 GB decoded - and the prefetch decodes the NEXT file unasked, so merely
''' standing next to such a file started it; the 20 s display deadline abandons the wait, not
''' the decode. The header says how big the canvas is and how many frames there are, so the
''' decoder asks it first and refuses above the budget: the file then fails as Content, like
''' any other picture that cannot be shown.
'''
''' The numbers are chosen here, not derived: 400 megapixels is about 1.6 GB of RGBA and
''' covers any real photograph (a 200 MP phone shot, a stitched panorama) with room to spare,
''' and the total across frames is held to the same ceiling because the GIF transcode keeps
''' every frame in memory at once. Pure.
''' </summary>
Public Module DecodeBudget

    ''' <summary>Largest single frame, in pixels.</summary>
    Public Const Max_Frame_Pixels As Long = 400L * 1000L * 1000L

    ''' <summary>Largest total over all frames, in pixels.</summary>
    Public Const Max_Total_Pixels As Long = 400L * 1000L * 1000L

    ''' <summary>
    ''' True when a picture of <paramref name="width"/> x <paramref name="height"/> with
    ''' <paramref name="frames"/> frames may be decoded. Non-positive sizes are refused (a
    ''' header that says nothing sane); a frame count below 1 counts as one frame.
    ''' </summary>
    Public Function IsWithinBudget(width As Long, height As Long, frames As Long) As Boolean
        If width <= 0 OrElse height <= 0 Then Return False
        Dim frame_Pixels As Long = width * height
        If frame_Pixels > Max_Frame_Pixels Then Return False
        Dim frame_Count As Long = Math.Max(1L, frames)
        ' Division instead of frame_Pixels * frame_Count, which could overflow for a header
        ' that claims a huge frame count.
        Return frame_Count <= Max_Total_Pixels \ frame_Pixels
    End Function

End Module

''' <summary>Picks the platform decoder once; both builds share every call site.</summary>
Public Module ImageDecoderProvider

    Private ReadOnly platform_Decoder As IImageDecoder = CreateDecoder()

    ''' <summary>
    ''' Extensions GDI+ cannot open natively, routed through the platform decoder
    ''' instead of Image.FromStream. The modern build adds the Magick.NET-backed
    ''' formats (epic O-3); the x86 build keeps WEBP only - AVIF/HEIC stay
    ''' "unsupported" there (Win 7/8.1 WIC has no HEIF codec).
    ''' </summary>
#If NETFRAMEWORK Then
    Public ReadOnly Decoder_Backed_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".webp"}
#Else
    Public ReadOnly Decoder_Backed_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".webp", ".avif", ".heic", ".heif"}
#End If

    ''' <summary>
    ''' True when the bytes are a format the platform decoder reads and GDI+ usually does not,
    ''' whatever the file is called (audit 048, A-16): RIFF....WEBP on both builds, plus an
    ''' ISO-BMFF "ftyp" box (AVIF/HEIC/HEIF) on the mainline, the only build that decodes
    ''' those. Only the first twelve bytes are looked at. Pure.
    ''' </summary>
    Public Function LooksDecoderBacked(head As Byte()) As Boolean
        If head Is Nothing OrElse head.Length < 12 Then Return False
        If head(0) = &H52 AndAlso head(1) = &H49 AndAlso head(2) = &H46 AndAlso head(3) = &H46 AndAlso
           head(8) = &H57 AndAlso head(9) = &H45 AndAlso head(10) = &H42 AndAlso head(11) = &H50 Then Return True
#If Not NETFRAMEWORK Then
        If head(4) = &H66 AndAlso head(5) = &H74 AndAlso head(6) = &H79 AndAlso head(7) = &H70 Then Return True
#End If
        Return False
    End Function

    Public ReadOnly Property Current As IImageDecoder
        Get
            Return platform_Decoder
        End Get
    End Property

    Private Function CreateDecoder() As IImageDecoder
#If NETFRAMEWORK Then
        Return New LegacyWicImageDecoder()
#Else
        Return New ModernImageSharpDecoder()
#End If
    End Function

End Module
