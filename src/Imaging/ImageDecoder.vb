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

''' <summary>Picks the platform decoder once; both builds share every call site.</summary>
Public Module ImageDecoderProvider

    Private ReadOnly platform_Decoder As IImageDecoder = CreateDecoder()

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
