#If Not NETFRAMEWORK Then
Option Strict On

''' <summary>The container a picture's first bytes name, for <see cref="ImageSignature.Sniff"/>.</summary>
Friend Enum ImageContainer
    Unknown
    Webp
    Png
    Jpeg
    Gif
    Bmp
    Tiff
    Pbm
    Qoi
    Tga
    ''' <summary>An ISO-BMFF "ftyp" box whose brand is an AVIF one.</summary>
    Avif
    ''' <summary>Any other ISO-BMFF "ftyp" box (HEIC/HEIF).</summary>
    Heif
    Jxl
End Enum

''' <summary>
''' Which reader a picture goes to, decided by its bytes and never by its name or by the backend
''' (specification 270 R1). The mainline's managed decoder used to auto-detect among exactly nine
''' formats - WEBP, PNG, JPEG, GIF, BMP, TIFF, PBM, QOI and TGA - and Magick.NET added AVIF/HEIF and
''' JPEG XL behind the same seam. ImageMagick reads far more than that, including formats that run
''' external programs or open other files; so every read names its coder explicitly from this
''' table, and anything the table does not know is refused before the backend sees a byte.
''' Pure; tolerates a short or missing head. Mainline only: the x86 fallback reads WEBP through WIC
''' and libwebp and needs none of it.
''' </summary>
Friend Module ImageSignature

    ''' <summary>How many leading bytes <see cref="Sniff"/> wants (it copes with fewer).</summary>
    Friend Const Head_Length As Integer = 32

    ''' <summary>The container the head names, or <see cref="ImageContainer.Unknown"/>.</summary>
    Friend Function Sniff(head As Byte()) As ImageContainer
        If head Is Nothing OrElse head.Length < 2 Then Return ImageContainer.Unknown

        If StartsWith(head, 0, &H52, &H49, &H46, &H46) AndAlso StartsWith(head, 8, &H57, &H45, &H42, &H50) Then Return ImageContainer.Webp
        If StartsWith(head, 0, &H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA) Then Return ImageContainer.Png
        If StartsWith(head, 0, &HFF, &HD8, &HFF) Then Return ImageContainer.Jpeg
        If StartsWith(head, 0, &H47, &H49, &H46, &H38) AndAlso head.Length >= 6 AndAlso
           (head(4) = &H37 OrElse head(4) = &H39) AndAlso head(5) = &H61 Then Return ImageContainer.Gif
        If StartsWith(head, 0, &H49, &H49, &H2A, 0) OrElse StartsWith(head, 0, &H4D, &H4D, 0, &H2A) OrElse
           StartsWith(head, 0, &H49, &H49, &H2B, 0) OrElse StartsWith(head, 0, &H4D, &H4D, 0, &H2B) Then Return ImageContainer.Tiff
        If LooksJxl(head) Then Return ImageContainer.Jxl
        If head.Length >= 12 AndAlso StartsWith(head, 4, &H66, &H74, &H79, &H70) Then
            Return If(IsAvifBrand(head), ImageContainer.Avif, ImageContainer.Heif)
        End If
        If StartsWith(head, 0, &H71, &H6F, &H69, &H66) Then Return ImageContainer.Qoi
        If LooksBmp(head) Then Return ImageContainer.Bmp
        If LooksPbm(head) Then Return ImageContainer.Pbm
        If LooksTga(head) Then Return ImageContainer.Tga
        Return ImageContainer.Unknown
    End Function

    ''' <summary>
    ''' True when the bytes start a JPEG XL file (ISO/IEC 18181, spec 236 section 2.3): either a
    ''' bare codestream (FF 0A) or the container's 12-byte signature box
    ''' (size 12, "JXL ", 0D 0A 87 0A). The container's own "ftyp" box comes AFTER that box, so
    ''' this never overlaps the ISO-BMFF test. A bare codestream is only two bytes of
    ''' evidence, which is enough here: the caller has either a decoder-backed extension or a
    ''' GDI+ refusal already, and FF 0A is neither a JPEG (FF D8) nor any other format this app
    ''' opens. Pure; tolerates a short or missing head.
    ''' </summary>
    Friend Function LooksJxl(head As Byte()) As Boolean
        If head Is Nothing OrElse head.Length < 2 Then Return False
        If head(0) = &HFF AndAlso head(1) = &HA Then Return True
        If head.Length < 12 Then Return False
        Return head(0) = 0 AndAlso head(1) = 0 AndAlso head(2) = 0 AndAlso head(3) = &HC AndAlso
               head(4) = &H4A AndAlso head(5) = &H58 AndAlso head(6) = &H4C AndAlso head(7) = &H20 AndAlso
               head(8) = &HD AndAlso head(9) = &HA AndAlso head(10) = &H87 AndAlso head(11) = &HA
    End Function

    ''' <summary>True for the containers that can hold more than one frame the viewer animates.</summary>
    Friend Function MayAnimate(container As ImageContainer) As Boolean
        Return container = ImageContainer.Webp OrElse container = ImageContainer.Png OrElse
               container = ImageContainer.Gif OrElse container = ImageContainer.Tiff OrElse
               container = ImageContainer.Jxl
    End Function

    Private Function StartsWith(head As Byte(), offset As Integer, ParamArray expected As Integer()) As Boolean
        If head.Length < offset + expected.Length Then Return False
        For i As Integer = 0 To expected.Length - 1
            If head(offset + i) <> expected(i) Then Return False
        Next
        Return True
    End Function

    ''' <summary>The major brand of the "ftyp" box (bytes 8..11) - "avif" for a still, "avis" for a
    ''' sequence. Every other brand (heic, heix, mif1, msf1, ..) is a HEIF image.</summary>
    Private Function IsAvifBrand(head As Byte()) As Boolean
        Return StartsWith(head, 8, &H61, &H76, &H69, &H66) OrElse StartsWith(head, 8, &H61, &H76, &H69, &H73)
    End Function

    ''' <summary>"BM" followed by a DIB header size the BMP family actually uses (12, 16, 40, 52, 56,
    ''' 64, 108 or 124 bytes) - two letters alone open far too many unrelated files.</summary>
    Private Function LooksBmp(head As Byte()) As Boolean
        If head.Length < 18 OrElse head(0) <> &H42 OrElse head(1) <> &H4D Then Return False
        Dim dibSize As Long = CLng(head(14)) Or (CLng(head(15)) << 8) Or (CLng(head(16)) << 16) Or (CLng(head(17)) << 24)
        Select Case dibSize
            Case 12, 16, 40, 52, 56, 64, 108, 124
                Return True
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>A Netpbm magic ("P1".."P6") followed by whitespace, the six variants the
    ''' managed decoder read.</summary>
    Private Function LooksPbm(head As Byte()) As Boolean
        If head.Length < 3 OrElse head(0) <> &H50 OrElse head(1) < &H31 OrElse head(1) > &H36 Then Return False
        Dim third As Byte = head(2)
        Return third = &H20 OrElse third = &H9 OrElse third = &HA OrElse third = &HD
    End Function

    ''' <summary>
    ''' TGA has no magic number, so this is the same structural test the managed decoder applied:
    ''' a colour-map type of 0 or 1, a known image type, an all-zero colour-map specification when
    ''' there is no colour map, and a non-zero width and height.
    ''' </summary>
    Private Function LooksTga(head As Byte()) As Boolean
        If head.Length < 18 Then Return False
        If head(1) <> 0 AndAlso head(1) <> 1 Then Return False
        Select Case head(2)
            Case 1, 2, 3, 9, 10, 11
            Case Else
                Return False
        End Select
        If head(1) = 0 Then
            For i As Integer = 3 To 7
                If head(i) <> 0 Then Return False
            Next
        End If
        If head(12) = 0 AndAlso head(13) = 0 Then Return False
        If head(14) = 0 AndAlso head(15) = 0 Then Return False
        Return True
    End Function

End Module
#End If
