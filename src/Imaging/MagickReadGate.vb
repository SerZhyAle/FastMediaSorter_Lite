#If Not NETFRAMEWORK Then
Option Strict On

Imports System.IO
Imports ImageMagick

''' <summary>
''' The ONE way this program hands encoded picture bytes to ImageMagick (specification 270 R1
''' and its security section). Every read names its coder explicitly, taken from the
''' <see cref="ImageSignature"/> table, so ImageMagick never guesses a format from the content:
''' its own detection reaches coders that run external programs, open other files or read
''' scripts, none of which a picture viewer asks for. A container the table does not know is
''' refused before ImageMagick sees a byte. The process-wide resource limits are applied first
''' (<see cref="MagickResourceLimits"/>).
'''
''' Shared by the viewer's decoder and encoder and by the Share Manager's QR intake, which links
''' this file. Mainline only.
''' </summary>
Friend Module MagickReadGate

    ''' <summary>
    ''' PNG chunks the in-memory PNG that GDI+ reads back must not carry: gAMA/cHRM/sRGB make
    ''' GDI+ correct the colours a second time, bKGD and the oFFs/vpAg/caNv page chunks describe
    ''' nothing GDI+ uses, and the date/time text chunks would make two decodes of one file
    ''' differ byte for byte. pHYs, iCCP, eXIf and the text chunks of the source stay.
    ''' </summary>
    Friend Const Png_Excluded_Chunks As String = "bKGD,cHRM,gAMA,sRGB,date,tIME,oFFs,vpAg,caNv"

    ''' <summary>The zlib level of <see cref="ToPngBytes"/> - see there.</summary>
    Friend Const Png_Compression_Level As String = "1"

    ''' <summary>The coder for a container; <see cref="MagickFormat.Unknown"/> when it has none here.</summary>
    Friend Function FormatOf(container As ImageContainer) As MagickFormat
        Select Case container
            Case ImageContainer.Webp : Return MagickFormat.WebP
            Case ImageContainer.Png : Return MagickFormat.Png
            Case ImageContainer.Jpeg : Return MagickFormat.Jpeg
            Case ImageContainer.Gif : Return MagickFormat.Gif
            Case ImageContainer.Bmp : Return MagickFormat.Bmp
            Case ImageContainer.Tiff : Return MagickFormat.Tiff
            Case ImageContainer.Pbm : Return MagickFormat.Pnm
            Case ImageContainer.Qoi : Return MagickFormat.Qoi
            Case ImageContainer.Tga : Return MagickFormat.Tga
            Case ImageContainer.Avif : Return MagickFormat.Avif
            Case ImageContainer.Heif : Return MagickFormat.Heic
            Case ImageContainer.Jxl : Return MagickFormat.Jxl
            Case Else : Return MagickFormat.Unknown
        End Select
    End Function

    ''' <summary>The first <see cref="ImageSignature.Head_Length"/> bytes of a seekable stream; the
    ''' position is restored.</summary>
    Friend Function ReadHead(stream As Stream) As Byte()
        Dim start As Long = stream.Position
        Dim head(ImageSignature.Head_Length - 1) As Byte
        stream.Position = 0
        Dim total As Integer = 0
        Do While total < head.Length
            Dim read As Integer = stream.Read(head, total, head.Length - total)
            If read <= 0 Then Exit Do
            total += read
        Loop
        stream.Position = start
        If total = 0 Then Return Array.Empty(Of Byte)()
        If total < head.Length Then ReDim Preserve head(total - 1)
        Return head
    End Function

    ''' <summary>The container of a seekable stream, by its first bytes.</summary>
    Friend Function Sniff(stream As Stream) As ImageContainer
        Return ImageSignature.Sniff(ReadHead(stream))
    End Function

    ''' <summary>Read settings that pin the coder; the frame range is left to the caller.</summary>
    Friend Function SettingsFor(container As ImageContainer) As MagickReadSettings
        Dim format As MagickFormat = FormatOf(container)
        If format = MagickFormat.Unknown Then Throw New NotSupportedException("Unrecognised image container.")
        Return New MagickReadSettings With {.Format = format}
    End Function

    ''' <summary>The headers of every frame (no pixel is decoded), with the coder pinned.</summary>
    Friend Function PingAll(stream As Stream, container As ImageContainer) As List(Of IMagickImageInfo)
        MagickResourceLimits.EnsureApplied()
        stream.Position = 0
        Try
            Return MagickImageInfo.ReadCollection(stream, SettingsFor(container)).ToList()
        Finally
            stream.Position = 0
        End Try
    End Function

    ''' <summary>Every frame, with the coder pinned. The caller owns the collection.</summary>
    Friend Function ReadAll(stream As Stream, container As ImageContainer) As MagickImageCollection
        MagickResourceLimits.EnsureApplied()
        stream.Position = 0
        Dim settings As MagickReadSettings = SettingsFor(container)
        ' An APNG's frames are only read by the APNG coder; the PNG coder returns its default image.
        If container = ImageContainer.Png Then settings.Format = MagickFormat.APng
        Try
            Return New MagickImageCollection(stream, settings)
        Finally
            stream.Position = 0
        End Try
    End Function

    ''' <summary>
    ''' The first frame on the full canvas, decoding no more of an animation than it must
    ''' (specification 270 R4). A WEBP animation's first frame is cut out of the file
    ''' (<see cref="AnimationHeader.ExtractFirstWebpFrame"/>) and placed on a transparent canvas,
    ''' because ImageMagick's WEBP reader decodes the whole animation even for one frame; an APNG
    ''' whose default image is its first frame is read as a plain PNG; the other containers stop
    ''' after the first frame by themselves. The caller owns the image.
    ''' </summary>
    Friend Function ReadFirstFrame(stream As Stream, container As ImageContainer) As MagickImage
        MagickResourceLimits.EnsureApplied()
        stream.Position = 0
        Try
            Select Case container
                Case ImageContainer.Webp
                    Dim header As AnimationHeaderInfo = AnimationHeader.ReadWebp(stream)
                    If header IsNot Nothing AndAlso header.IsAnimated Then
                        Dim frame As WebpFirstFrame = AnimationHeader.ExtractFirstWebpFrame(stream, header)
                        If frame IsNot Nothing Then Return PlaceOnCanvas(frame, header)
                    End If
                Case ImageContainer.Png
                    Dim header As AnimationHeaderInfo = AnimationHeader.ReadPng(stream)
                    If header IsNot Nothing AndAlso header.IsAnimated AndAlso Not header.DefaultImageIsFirstFrame Then
                        ' The default image is not part of the animation: the first frame is the first
                        ' fdAT one, which only the APNG coder reaches. Rare, and read whole.
                        Using all As MagickImageCollection = ReadAll(stream, container)
                            all.Coalesce()
                            Return New MagickImage(all(0))
                        End Using
                    End If
                Case ImageContainer.Gif
                    ' The first frame may cover only part of the canvas: coalesce puts it on it.
                    Dim gifSettings As MagickReadSettings = SettingsFor(container)
                    gifSettings.FrameIndex = 0UI
                    gifSettings.FrameCount = 1UI
                    Using first As New MagickImageCollection(stream, gifSettings)
                        first.Coalesce()
                        Dim image As New MagickImage(first(0))
                        image.ResetPage()
                        Return image
                    End Using
            End Select

            Dim settings As MagickReadSettings = SettingsFor(container)
            settings.FrameIndex = 0UI
            settings.FrameCount = 1UI
            stream.Position = 0
            Return New MagickImage(stream, settings)
        Finally
            stream.Position = 0
        End Try
    End Function

    ''' <summary>
    ''' The picture as PNG bytes for GDI+ (see <see cref="Png_Excluded_Chunks"/>). These bytes are
    ''' an in-memory hand-over and a decode-cache entry, never a user's file, so zlib level 1:
    ''' ImageMagick's default level spent 19.7 s of a 24 Mpx WEBP open on deflate against 0.25 s
    ''' of decoding, level 1 takes 2.1 s and its output is still smaller than the payload the
    ''' replaced decoder produced (specification 270 section 6).
    ''' </summary>
    Friend Function ToPngBytes(image As IMagickImage(Of Byte)) As Byte()
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", Png_Excluded_Chunks)
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", Png_Compression_Level)
        Using png As New MemoryStream()
            image.Write(png, MagickFormat.Png)
            Return png.ToArray()
        End Using
    End Function

    Private Function PlaceOnCanvas(frame As WebpFirstFrame, header As AnimationHeaderInfo) As MagickImage
        Dim settings As MagickReadSettings = SettingsFor(ImageContainer.Webp)
        Dim picture As New MagickImage(frame.Bytes, settings)
        If frame.X = 0 AndAlso frame.Y = 0 AndAlso CInt(picture.Width) = header.Width AndAlso CInt(picture.Height) = header.Height Then
            picture.ResetPage()
            Return picture
        End If
        Using picture
            ' A frame smaller than the canvas starts on a transparent one, as the WEBP
            ' specification's reference decoder does.
            Dim canvas As New MagickImage(MagickColors.Transparent, CUInt(header.Width), CUInt(header.Height))
            canvas.Composite(picture, frame.X, frame.Y, CompositeOperator.Over)
            Return canvas
        End Using
    End Function

End Module
#End If
