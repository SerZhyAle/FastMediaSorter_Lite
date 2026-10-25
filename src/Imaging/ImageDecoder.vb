Option Strict On

Imports System.Drawing
Imports System.IO

''' <summary>
''' Seam between the shared media pipeline and the platform image decoder
''' (SPECIFICATION_DOTNET10_MODERN_BUILD.md §6.1). The net48 build decodes via
''' WIC with a libwebp fallback for static WEBP; the .NET 10 build decodes with
''' Magick.NET (no OS codec involved; specification 270). Shared code (FileManager,
''' Utils) only ever sees this interface.
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

    ''' <summary>
    ''' True when the decoded Image owns all its pixel data in memory and does not hold onto
    ''' the input MemoryStream (e.g. ModernMagickDecoder). When True, callers may safely
    ''' dispose the source MemoryStream after decoding.
    ''' </summary>
    ReadOnly Property ImageOwnsData As Boolean

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
        Return Classify(width, height, frames) = BudgetVerdict.Within
    End Function

    ''' <summary>
    ''' Why a picture is or is not within the budget (spec 241 rule 8). A non-positive size
    ''' and a frame over <see cref="Max_Frame_Pixels"/> are <see cref="BudgetVerdict.FrameTooLarge"/>
    ''' - the file cannot be shown at all. A frame that fits but too many of them make
    ''' <see cref="BudgetVerdict.AnimationTooLong"/> - the first frame can still be shown.
    ''' </summary>
    Public Function Classify(width As Long, height As Long, frames As Long) As BudgetVerdict
        If width <= 0 OrElse height <= 0 Then Return BudgetVerdict.FrameTooLarge
        ' A side beyond the frame ceiling cannot be within it, and checking it first keeps
        ' the product below from overflowing on a crafted header.
        If width > Max_Frame_Pixels OrElse height > Max_Frame_Pixels Then Return BudgetVerdict.FrameTooLarge
        Dim frame_Pixels As Long = width * height
        If frame_Pixels > Max_Frame_Pixels Then Return BudgetVerdict.FrameTooLarge
        Dim frame_Count As Long = Math.Max(1L, frames)
        ' Division instead of frame_Pixels * frame_Count, which could overflow for a header
        ' that claims a huge frame count.
        Return If(frame_Count <= Max_Total_Pixels \ frame_Pixels, BudgetVerdict.Within, BudgetVerdict.AnimationTooLong)
    End Function

End Module

''' <summary>The answer of <see cref="DecodeBudget.Classify"/> (spec 241 rule 8).</summary>
Public Enum BudgetVerdict
    ''' <summary>Decode it.</summary>
    Within
    ''' <summary>One frame fits, the total over all frames does not.</summary>
    AnimationTooLong
    ''' <summary>A frame is over the ceiling, or the size is not a size.</summary>
    FrameTooLarge
End Enum

''' <summary>
''' How much of a picture a caller of <c>FileManager</c> wants decoded (spec 241). Declared in the
''' shared file so the one loader keeps one signature; only the modern build reads it - the
''' net48 loader decodes everything, as it always did.
''' </summary>
Friend Enum ImageLoadStage
    ''' <summary>The whole picture, every frame of an animation (today's behaviour).</summary>
    Whole
    ''' <summary>For the viewer's display and its prefetch: a long animation arrives as its first frame
    ''' (a preview), everything else whole.</summary>
    Staged
    ''' <summary>For thumbnails: the first frame of any animation, never the transcode.</summary>
    FirstFrame
End Enum

''' <summary>Picks the platform decoder once; both builds share every call site.</summary>
Public Module ImageDecoderProvider

    Private ReadOnly platform_Decoder As IImageDecoder = CreateDecoder()

    ''' <summary>
    ''' Extensions GDI+ cannot open natively, routed through the platform decoder
    ''' instead of Image.FromStream. The modern build adds the Magick.NET-backed
    ''' formats (epic O-3, and JPEG XL, spec 236); the x86 build keeps WEBP only -
    ''' AVIF/HEIC/JXL stay "unsupported" there (Win 7/8.1 WIC has no HEIF or JXL codec).
    ''' </summary>
#If NETFRAMEWORK Then
    Public ReadOnly Decoder_Backed_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".webp"}
#Else
    Public ReadOnly Decoder_Backed_Extensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".webp", ".avif", ".heic", ".heif", ".jxl"}
#End If

    ''' <summary>
    ''' True when the bytes are a format the platform decoder reads and GDI+ usually does not,
    ''' whatever the file is called (audit 048, A-16): RIFF....WEBP on both builds, plus an
    ''' ISO-BMFF "ftyp" box (AVIF/HEIC/HEIF) and JPEG XL (<c>LooksJxl</c>) on the
    ''' mainline, the only build that decodes those. Only the first twelve bytes are looked
    ''' at. Pure.
    ''' </summary>
    Public Function LooksDecoderBacked(head As Byte()) As Boolean
        If head Is Nothing OrElse head.Length < 12 Then Return False
        If head(0) = &H52 AndAlso head(1) = &H49 AndAlso head(2) = &H46 AndAlso head(3) = &H46 AndAlso
           head(8) = &H57 AndAlso head(9) = &H45 AndAlso head(10) = &H42 AndAlso head(11) = &H50 Then Return True
#If Not NETFRAMEWORK Then
        If head(4) = &H66 AndAlso head(5) = &H74 AndAlso head(6) = &H79 AndAlso head(7) = &H70 Then Return True
        If LooksJxl(head) Then Return True
#End If
        Return False
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>True when the bytes start a JPEG XL file - see <see cref="ImageSignature.LooksJxl"/>,
    ''' which holds the test so the Share Manager's linked QR reader shares it.</summary>
    Public Function LooksJxl(head As Byte()) As Boolean
        Return ImageSignature.LooksJxl(head)
    End Function
#End If

    Public ReadOnly Property Current As IImageDecoder
        Get
            Return platform_Decoder
        End Get
    End Property

    Private Function CreateDecoder() As IImageDecoder
#If NETFRAMEWORK Then
        Return New LegacyWicImageDecoder()
#Else
        Return New ModernMagickDecoder()
#End If
    End Function

End Module
