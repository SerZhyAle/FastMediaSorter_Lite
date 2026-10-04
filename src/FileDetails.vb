Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text

''' <summary>
''' One "label: value" line of the "about file" window. The label is a localization
''' KEY (Russian, translated by the window through <see cref="Localization.T"/>); the
''' value is built language-neutral on purpose - units, numbers and dates are formatted
''' invariant ("12.35 MB", "1/250 s", "96 × 96 dpi"), so a value never needs a column
''' in the string tables and can never drift out of sync with them.
''' </summary>
Public NotInheritable Class FileDetailRow

    Public ReadOnly Property LabelKey As String
    Public ReadOnly Property Value As String

    Public Sub New(labelKey As String, value As String)
        Me.LabelKey = labelKey
        Me.Value = If(value, "")
    End Sub
End Class

''' <summary>A titled block of rows. An empty section is skipped by the window.</summary>
Public NotInheritable Class FileDetailSection

    Public ReadOnly Property TitleKey As String
    Public ReadOnly Property Rows As List(Of FileDetailRow)

    Public Sub New(titleKey As String)
        Me.TitleKey = titleKey
        Rows = New List(Of FileDetailRow)()
    End Sub

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return Rows.Count = 0
        End Get
    End Property

    Public Sub Add(labelKey As String, value As String)
        If Not String.IsNullOrEmpty(value) Then Rows.Add(New FileDetailRow(labelKey, value))
    End Sub
End Class

''' <summary>
''' The model of the "about file" window (the first panel's "i" item): everything useful
''' that can be read about the file on screen without launching anything. The old "i"
''' opened a mailto: to the author - the user's mail client springing open on an
''' information button - while the one thing a viewer's info button owes the user is the
''' file itself: its size and dates, an image's pixels and camera, an e-book's author,
''' a track's tags and length.
'''
''' This class is the PURE part, testable on both legs (net48 + net10): no WinForms, no
''' GDI+, no LibVLC. Image dimensions come from the header parser in <see cref="Utils"/>
''' (deliberately not Image.FromFile/FromStream - concurrent GDI+ decoding is what the
''' comment there warns about); EXIF is read straight from the JPEG APP1 / TIFF bytes.
''' Audio and video TRACK details need libvlc, live in Main_Form.FileDetails.vb and only
''' exist in the modern build; text documents use TextFileReader, e-books and PDF use
''' the document readers that the net48 fallback does not ship.
''' </summary>
Public NotInheritable Class FileDetails

    Private Sub New()
    End Sub

    ' --- classification ----------------------------------------------------------------------

    ' Kept local and smaller than MediaKindClassifier (which is modern-only) on purpose:
    ' this window answers "what can I read from THIS file", it does not navigate.
    Private Shared ReadOnly ImageExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif",
        ".avif", ".svg", ".ico", ".wmf", ".emf"}
    Private Shared ReadOnly VideoExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".mp4", ".mkv", ".mov", ".webm", ".3gp", ".flv", ".wmv", ".m4v", ".avi", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".vob", ".ogv", ".divx", ".m2v", ".mts", ".3g2", ".asf"}
    Private Shared ReadOnly AudioExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".mp3", ".m4a", ".flac", ".aac", ".ogg", ".wma", ".opus", ".amr", ".awb", ".ac3", ".ec3",
        ".adts", ".mka", ".oga", ".caf", ".alac", ".m4b", ".wav", ".wave", ".mid", ".midi"}

    ' The parameter is named filePath everywhere: a String called "path" shadows
    ' System.IO.Path under Option Strict and turns Path.GetFileName into a lookup
    ' on the string.
    Public Shared Function ExtensionOf(filePath As String) As String
        If String.IsNullOrEmpty(filePath) Then Return ""
        Dim name As String = Path.GetFileName(filePath)
        Dim dot As Integer = name.LastIndexOf("."c)
        If dot <= 0 OrElse dot = name.Length - 1 Then Return ""
        Return name.Substring(dot)
    End Function

    Public Shared Function IsImageFile(filePath As String) As Boolean
        Return ImageExtensions.Contains(ExtensionOf(filePath))
    End Function

    Public Shared Function IsVideoFile(filePath As String) As Boolean
        Return VideoExtensions.Contains(ExtensionOf(filePath))
    End Function

    Public Shared Function IsAudioFile(filePath As String) As Boolean
        Return AudioExtensions.Contains(ExtensionOf(filePath))
    End Function

    ' --- building ----------------------------------------------------------------------------

    ''' <summary>The sections readable without any media engine. Order is the display order:
    ''' the file itself first, then the image with its camera, then the document.</summary>
    Public Shared Function BuildSections(filePath As String) As List(Of FileDetailSection)
        Dim sections As New List(Of FileDetailSection)()
        Dim file As FileDetailSection = BuildFileSection(filePath)
        If file IsNot Nothing Then sections.Add(file)
        sections.AddRange(BuildImageSections(filePath))
        sections.AddRange(BuildDocumentSections(filePath))
        Return sections
    End Function

    ''' <summary>Name, location, size and dates - the rows every file has. Nothing here
    ''' throws on a file that vanished between display and click: the caller shows the
    ''' window for the file it can still see, and a missing file simply has no rows.</summary>
    Public Shared Function BuildFileSection(filePath As String) As FileDetailSection
        Dim section As New FileDetailSection("Файл")
        Dim info As FileInfo = Nothing
        Try
            info = New FileInfo(filePath)
        Catch
        End Try
        If info Is Nothing OrElse Not info.Exists Then Return section

        section.Add("Имя", info.Name)
        section.Add("Папка", If(String.IsNullOrEmpty(info.DirectoryName), "", info.DirectoryName))
        Dim extension As String = ExtensionOf(filePath)
        If extension.Length > 0 Then section.Add("Тип", extension.ToUpperInvariant())
        section.Add("Размер", FormatSize(info.Length))
        section.Add("Байт", info.Length.ToString("#,0", CultureInfo.InvariantCulture))
        section.Add("Создан", FormatTimestampSafe(info.CreationTime))
        section.Add("Изменён", FormatTimestampSafe(info.LastWriteTime))
        section.Add("Открыт", FormatTimestampSafe(info.LastAccessTime))
        Dim attributes As String = DescribeAttributes(info.Attributes)
        If attributes.Length > 0 Then section.Add("Атрибуты", attributes)
        Return section
    End Function

    ''' <summary>Attribute words as a comma-separated list, localized (they are plain
    ''' words, not values). Normal is the empty string - Windows Explorer does the same.
    ''' The ARCHIVE bit is deliberately not reported: Windows sets it on every ordinary
    ''' file, so the row would carry no information.</summary>
    Public Shared Function DescribeAttributes(attributes As IO.FileAttributes) As String
        Dim words As New List(Of String)()
        If (attributes And IO.FileAttributes.ReadOnly) = IO.FileAttributes.ReadOnly Then words.Add(Localization.T("только чтение"))
        If (attributes And IO.FileAttributes.Hidden) = IO.FileAttributes.Hidden Then words.Add(Localization.T("скрытый"))
        Return String.Join(", ", words)
    End Function

    ' --- image -------------------------------------------------------------------------------

    ''' <summary>The largest part of the header this window ever reads. EXIF lives in the
    ''' first segment of a JPEG and usually near the head of a TIFF; a file whose IFD
    ''' chain runs past this simply reports the rows that were reachable.</summary>
    Private Const Max_Header_Bytes As Integer = 512 * 1024

    Public Shared Function BuildImageSections(filePath As String) As List(Of FileDetailSection)
        Dim sections As New List(Of FileDetailSection)()
        If Not IsImageFile(filePath) Then Return sections

        Dim header As Byte() = ReadHeaderBytes(filePath)
        If header Is Nothing Then Return sections
        Dim exif As ExifReader = ExifReader.TryRead(header)

        Dim image As New FileDetailSection("Изображение")
        AddImageSizeRows(image, filePath, exif)
        AddImageDepthRows(image, header)
        AddImageDensityRows(image, header, exif)
        If Not image.IsEmpty Then sections.Add(image)

        Dim camera As FileDetailSection = BuildCameraSection(exif)
        If camera IsNot Nothing Then sections.Add(camera)
        Return sections
    End Function

    Private Shared Sub AddImageSizeRows(image As FileDetailSection, filePath As String, exif As ExifReader)
        Dim width As Integer = 0
        Dim height As Integer = 0
        ' The size AS DISPLAYED (audit 048, A-21): a portrait phone photo stored as
        ' landscape pixels plus an Orientation tag reads 3024 × 4032 here, not 4032 × 3024.
        Dim size As System.Drawing.Size = Utils.GetImageDimensions(filePath)
        If size.Width > 0 AndAlso size.Height > 0 Then
            width = size.Width
            height = size.Height
        ElseIf exif IsNot Nothing Then
            ' TIFF, which the header parser does not walk: IFD0 carries its own size.
            Dim w As Long? = exif.LongValue(ExifTag.ImageWidth)
            Dim h As Long? = exif.LongValue(ExifTag.ImageLength)
            If w.HasValue AndAlso h.HasValue AndAlso w.Value > 0 AndAlso w.Value <= Integer.MaxValue AndAlso
               h.Value > 0 AndAlso h.Value <= Integer.MaxValue Then
                width = CInt(w.Value)
                height = CInt(h.Value)
            End If
        End If
        If width <= 0 OrElse height <= 0 Then Return
        image.Add("Разрешение", width.ToString(CultureInfo.InvariantCulture) & " × " &
                                       height.ToString(CultureInfo.InvariantCulture) &
                                       " (" & (width * height / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture) & " MP)")
    End Sub

    Private Shared Sub AddImageDepthRows(image As FileDetailSection, header As Byte())
        Dim bits As Integer = PngBitDepth(header)
        If bits <= 0 Then bits = BmpBitDepth(header)
        If bits <= 0 Then bits = JpegBitDepth(header)
        If bits > 0 Then image.Add("Глубина цвета", bits.ToString(CultureInfo.InvariantCulture) & " bpp")
    End Sub

    Private Shared Sub AddImageDensityRows(image As FileDetailSection, header As Byte(), exif As ExifReader)
        Dim density As String = ""
        If exif IsNot Nothing Then
            ' EXIF resolution (also how a TIFF states it) beats the container's own field.
            Dim x As Double? = exif.RationalValue(ExifTag.XResolution)
            Dim y As Double? = exif.RationalValue(ExifTag.YResolution)
            Dim unit As Integer? = exif.ShortValue(ExifTag.ResolutionUnit)
            If x.HasValue AndAlso y.HasValue AndAlso x.Value > 0 AndAlso y.Value > 0 Then
                Dim factor As Double = If(unit.HasValue AndAlso unit.Value = 3, 2.54, 1.0)
                density = FormatDensity(x.Value * factor, y.Value * factor)
            End If
        End If
        If density.Length = 0 Then density = PngDensity(header)
        If density.Length = 0 Then density = JpegDensity(header)
        If density.Length = 0 Then density = BmpDensity(header)
        If density.Length > 0 Then image.Add("Плотность пикселей", density)
    End Sub

    Private Shared Function FormatDensity(xDpi As Double, yDpi As Double) As String
        Return Math.Round(xDpi).ToString(CultureInfo.InvariantCulture) & " × " &
               Math.Round(yDpi).ToString(CultureInfo.InvariantCulture) & " dpi"
    End Function

    ''' <summary>IHDR's bit depth and colour type, PNG's own header fields.</summary>
    Private Shared Sub AddPngIhdr(header As Byte(), ByRef bitDepth As Integer, ByRef colorType As Integer)
        bitDepth = 0 : colorType = -1
        If header.Length < 26 Then Return
        If header(0) <> &H89 OrElse header(1) <> &H50 OrElse header(2) <> &H4E OrElse header(3) <> &H47 Then Return
        bitDepth = header(24)
        colorType = header(25)
    End Sub

    Private Shared Function PngBitDepth(header As Byte()) As Integer
        Dim bitDepth As Integer, colorType As Integer
        AddPngIhdr(header, bitDepth, colorType)
        If bitDepth <= 0 OrElse colorType < 0 Then Return 0
        ' Channels per pixel by PNG colour type; the palette case is the palette's index
        ' depth, not the screen depth - better silence than a lie.
        Dim channels As Integer = 0
        Select Case colorType
            Case 0 : channels = 1
            Case 2 : channels = 3
            Case 4 : channels = 2
            Case 6 : channels = 4
        End Select
        If channels = 0 Then Return 0
        Return bitDepth * channels
    End Function

    Private Shared Function PngDensity(header As Byte()) As String
        If header.Length < 8 OrElse header(0) <> &H89 OrElse header(1) <> &H50 Then Return ""
        Dim position As Integer = 8
        While position + 8 <= header.Length
            Dim length As Integer = BigEndianInt32(header, position)
            Dim kind As String = Encoding.ASCII.GetString(header, position + 4, 4)
            If length < 0 OrElse position + 12 + length > header.Length Then Return ""
            If kind = "pHYs" AndAlso length >= 9 AndAlso header(position + 12 + 8) = 1 Then
                ' Pixels per METRE (unit = 1); an inch is 0.0254 of one.
                Dim perMetreX As Integer = BigEndianInt32(header, position + 12)
                Dim perMetreY As Integer = BigEndianInt32(header, position + 16)
                If perMetreX > 0 AndAlso perMetreY > 0 Then
                    Return FormatDensity(perMetreX * 0.0254, perMetreY * 0.0254)
                End If
                Return ""
            End If
            position += 12 + length
        End While
        Return ""
    End Function

    Private Shared Function JpegSofInfo(header As Byte(), ByRef height As Integer, ByRef bitDepth As Integer) As Boolean
        height = 0 : bitDepth = 0
        If header.Length < 4 OrElse header(0) <> &HFF OrElse header(1) <> &HD8 Then Return False
        Dim position As Integer = 2
        While position + 4 <= header.Length
            If header(position) <> &HFF Then Return False
            Dim marker As Integer = header(position + 1)
            If marker = &HD8 OrElse marker = &H01 OrElse (marker >= &HD0 AndAlso marker <= &HD7) Then
                position += 2
                Continue While
            End If
            If position + 4 > header.Length Then Return False
            Dim length As Integer = (CInt(header(position + 2)) << 8) Or header(position + 3)
            If length < 2 Then Return False
            ' SOF0..SOF15 except the JPG markers D4, D8, DC that carry no frame header.
            If marker >= &HC0 AndAlso marker <= &HCF AndAlso marker <> &HC4 AndAlso
               marker <> &HC8 AndAlso marker <> &HCC AndAlso position + 9 <= header.Length Then
                bitDepth = header(position + 4)
                height = (CInt(header(position + 5)) << 8) Or header(position + 6)
                Return True
            End If
            position += 2 + length
        End While
        Return False
    End Function

    Private Shared Function JpegBitDepth(header As Byte()) As Integer
        Dim height As Integer, bitDepth As Integer
        If Not JpegSofInfo(header, height, bitDepth) OrElse bitDepth <= 0 Then Return 0
        ' JPEG samples YCbCr: precision 8 is the 24 bpp the user expects.
        Return bitDepth * 3
    End Function

    Private Shared Function JpegDensity(header As Byte()) As String
        If header.Length < 13 OrElse header(0) <> &HFF OrElse header(1) <> &HD8 Then Return ""
        If header(2) <> &HFF OrElse header(3) <> &HE0 Then Return ""
        If Not Encoding.ASCII.GetString(header, 6, 5).StartsWith("JFIF", StringComparison.Ordinal) Then Return ""
        Dim units As Integer = header(13)
        Dim x As Integer = (CInt(header(14)) << 8) Or header(15)
        Dim y As Integer = (CInt(header(16)) << 8) Or header(17)
        If x <= 0 OrElse y <= 0 Then Return ""
        ' JFIF: 1 = dots per inch, 2 = dots per centimetre.
        If units = 1 Then Return FormatDensity(x, y)
        If units = 2 Then Return FormatDensity(x * 2.54, y * 2.54)
        Return ""
    End Function

    Private Shared Function BmpBitDepth(header As Byte()) As Integer
        If header.Length < 30 OrElse header(0) <> &H42 OrElse header(1) <> &H4D Then Return 0
        If header(28) > 0 Then Return header(28)
        Return 0
    End Function

    Private Shared Function BmpDensity(header As Byte()) As String
        If header.Length < 50 OrElse header(0) <> &H42 OrElse header(1) <> &H4D Then Return ""
        Dim ppmX As Integer = BitConverter.ToInt32(header, 38)
        Dim ppmY As Integer = BitConverter.ToInt32(header, 42)
        If ppmX <= 0 OrElse ppmY <= 0 Then Return ""
        Return FormatDensity(ppmX * 0.0254, ppmY * 0.0254)
    End Function

    Private Shared Function BigEndianInt32(bytes As Byte(), offset As Integer) As Integer
        Return (CInt(bytes(offset)) << 24) Or (CInt(bytes(offset + 1)) << 16) Or
               (CInt(bytes(offset + 2)) << 8) Or bytes(offset + 3)
    End Function

    Private Shared Function ReadHeaderBytes(filePath As String) As Byte()
        Try
            Using stream As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim length As Integer = CInt(Math.Min(stream.Length, CLng(Max_Header_Bytes)))
                Dim buffer(length - 1) As Byte
                Dim total As Integer = 0
                While total < length
                    Dim read As Integer = stream.Read(buffer, total, length - total)
                    If read <= 0 Then Exit While
                    total += read
                End While
                If total = 0 Then Return Nothing
                If total <> length Then Array.Resize(buffer, total)
                Return buffer
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ' --- the camera (EXIF) --------------------------------------------------------------------

    ''' <summary>Nothing when the file carries no camera data - a screenshot or a render
    ''' gets no empty "Camera" block.</summary>
    Private Shared Function BuildCameraSection(exif As ExifReader) As FileDetailSection
        If exif Is Nothing Then Return Nothing
        Dim section As New FileDetailSection("Камера")

        Dim make As String = exif.AsciiValue(ExifTag.Make)
        Dim model As String = exif.AsciiValue(ExifTag.Model)
        Dim camera As String = (make.Trim() & " " & model.Trim()).Trim()
        section.Add("Камера", camera)
        section.Add("Программа", exif.AsciiValue(ExifTag.Software))

        Dim taken As String = ExifReader.ExifDateText(exif.AsciiValue(ExifTag.DateTimeOriginal))
        If taken.Length = 0 Then taken = ExifReader.ExifDateText(exif.AsciiValue(ExifTag.DateTime))
        section.Add("Дата съёмки", taken)

        Dim exposure As Double? = exif.RationalValue(ExifTag.ExposureTime)
        section.Add("Выдержка", If(exposure.HasValue, ExposureText(exposure.Value), ""))
        Dim aperture As Double? = exif.RationalValue(ExifTag.FNumber)
        section.Add("Диафрагма", If(aperture.HasValue, ApertureText(aperture.Value), ""))
        Dim iso As Integer? = exif.ShortValue(ExifTag.IsoSpeedRatings)
        If iso.HasValue Then section.Add("ISO", iso.Value.ToString(CultureInfo.InvariantCulture))
        Dim focal As Double? = exif.RationalValue(ExifTag.FocalLength)
        section.Add("Фокусное расстояние", If(focal.HasValue, FocalText(focal.Value), ""))
        section.Add("Автор", exif.AsciiValue(ExifTag.Artist))

        Dim coordinates As String = exif.GpsCoordinateText()
        If coordinates.Length > 0 Then section.Add("Координаты", coordinates)

        If section.IsEmpty Then Return Nothing
        Return section
    End Function

    ''' <summary>"1/250 s" below a second, "2 s" at or above it - the two ways a person reads
    ''' an exposure. Rounding to the nearest thousandth keeps 1/8000 honest.</summary>
    Public Shared Function ExposureText(seconds As Double) As String
        If seconds <= 0 OrElse Double.IsNaN(seconds) Then Return ""
        If seconds >= 1.0 Then Return seconds.ToString("0.##", CultureInfo.InvariantCulture) & " s"
        Dim denominator As Double = Math.Round(1.0 / seconds)
        If denominator < 1.99 Then Return seconds.ToString("0.##", CultureInfo.InvariantCulture) & " s"
        Return "1/" & denominator.ToString("0", CultureInfo.InvariantCulture) & " s"
    End Function

    Public Shared Function ApertureText(fNumber As Double) As String
        If fNumber <= 0 OrElse Double.IsNaN(fNumber) Then Return ""
        Return "f/" & fNumber.ToString("0.##", CultureInfo.InvariantCulture)
    End Function

    Public Shared Function FocalText(millimetres As Double) As String
        If millimetres <= 0 OrElse Double.IsNaN(millimetres) Then Return ""
        Return Math.Round(millimetres).ToString(CultureInfo.InvariantCulture) & " mm"
    End Function

    ' --- documents ----------------------------------------------------------------------------

    Public Shared Function BuildDocumentSections(filePath As String) As List(Of FileDetailSection)
        Dim sections As New List(Of FileDetailSection)()
        Dim extension As String = ExtensionOf(filePath)
        Dim section As FileDetailSection = Nothing

        ' Each reader is fenced SEPARATELY: an If/ElseIf chain cut in half by a
        ' conditional-compilation seam does not compile on the leg where the chain's
        ' head is gone, while standalone "Is Nothing And" checks do. The text and
        ' epub readers ship with the modern viewer and both test legs; the PDF reader
        ' is modern-only (no DOCUMENT_PREVIEW_TESTS extension); the net48 fallback
        ' has no document preview at all, the same seams their own files carry.
#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
        If section Is Nothing AndAlso TextFileReader.IsTextExtension(extension) Then
            section = BuildTextSection(filePath)
        End If
        If section Is Nothing AndAlso String.Equals(extension, ".epub", StringComparison.OrdinalIgnoreCase) Then
            section = BuildEpubSection(filePath)
        End If
#End If
#If Not NETFRAMEWORK Then
        If section Is Nothing AndAlso String.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) Then
            section = BuildPdfSection(filePath)
        End If
#End If

        If section IsNot Nothing AndAlso Not section.IsEmpty Then sections.Add(section)
        Return sections
    End Function

#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
    Private Shared Function BuildTextSection(filePath As String) As FileDetailSection
        Dim section As New FileDetailSection("Документ")
        Dim result As TextReadResult = TextFileReader.Read(filePath)
        If Not result.Success Then Return section
        If result.Lines IsNot Nothing Then
            section.Add("Строк", result.Lines.Length.ToString(CultureInfo.InvariantCulture))
        End If
        If result.Text IsNot Nothing Then
            Dim words As Integer = 0
            For Each word As String In result.Text.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
                words += 1
            Next
            section.Add("Слов", words.ToString(CultureInfo.InvariantCulture))
            section.Add("Символов", result.Text.Length.ToString(CultureInfo.InvariantCulture))
        End If
        If result.DetectedEncoding IsNot Nothing Then
            section.Add("Кодировка", result.DetectedEncoding.WebName)
        End If
        ' An honest count of a truncated read, not a silent wrong one.
        If result.IsTruncated Then section.Add("Файл прочитан не полностью", "")
        Return section
    End Function
#End If

    ' The EPUB reader is granted to the net48 test leg (DOCUMENT_PREVIEW_TESTS), the
    ' same seam its own file carries.
#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
    Private Shared Function BuildEpubSection(filePath As String) As FileDetailSection
        Dim section As New FileDetailSection("Документ")
        Dim info As EpubInfo = EpubMetadata.Read(filePath)
        If Not info.Success Then Return section
        section.Add("Название", info.Title)
        section.Add("Автор", info.Author)
        Return section
    End Function
#End If

    ' The PDF reader ships only in the modern viewer (its file is #If Not NETFRAMEWORK
    ' without the DOCUMENT_PREVIEW_TESTS extension the epub reader grants the tests).
#If Not NETFRAMEWORK Then
    Private Shared Function BuildPdfSection(filePath As String) As FileDetailSection
        Dim section As New FileDetailSection("Документ")
        Try
            Using source As New PdfDocumentSource()
                source.Open(filePath)
                If source.IsOpen Then
                    section.Add("Страниц", source.PageCount.ToString(CultureInfo.InvariantCulture))
                End If
            End Using
        Catch
        End Try
        Return section
    End Function
#End If

    ' --- formatters ---------------------------------------------------------------------------

    ''' <summary>"12.35 MB" - invariant, two decimals, binary units, the same convention
    ''' as the audio plate (AudioMetadata.FormatSize) so two surfaces of one product never
    ''' print the same file two ways.</summary>
    Public Shared Function FormatSize(bytes As Long) As String
        If bytes < 0 Then Return ""
        Dim units() As String = {"B", "KB", "MB", "GB", "TB"}
        Dim size As Double = bytes
        Dim unit As Integer = 0
        While size >= 1024 AndAlso unit < units.Length - 1
            size /= 1024
            unit += 1
        End While
        If unit = 0 Then Return bytes.ToString(CultureInfo.InvariantCulture) & " B"
        Return size.ToString("0.00", CultureInfo.InvariantCulture) & " " & units(unit)
    End Function

    ''' <summary>"3:05" / "1:02:03" - the same shape as the audio plate (audit 057 S08-10:
    ''' hours are TOTAL hours, not the dropped Hours component).</summary>
    Public Shared Function FormatDuration(milliseconds As Long) As String
        If milliseconds <= 0 Then Return ""
        Dim value As TimeSpan = TimeSpan.FromMilliseconds(milliseconds)
        If value.TotalHours < 1 Then Return CInt(Math.Floor(value.TotalMinutes)).ToString(CultureInfo.InvariantCulture) & ":" &
                                             value.Seconds.ToString("00", CultureInfo.InvariantCulture)
        Return CInt(Math.Floor(value.TotalHours)).ToString(CultureInfo.InvariantCulture) & ":" &
               value.Minutes.ToString("00", CultureInfo.InvariantCulture) & ":" &
               value.Seconds.ToString("00", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>"128 kbps" below a megabit, "2.35 Mbps" above it.</summary>
    Public Shared Function FormatBitrate(bitsPerSecond As Long) As String
        If bitsPerSecond <= 0 Then Return ""
        If bitsPerSecond < 1000000 Then Return (bitsPerSecond \ 1000).ToString(CultureInfo.InvariantCulture) & " kbps"
        Return (bitsPerSecond / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) & " Mbps"
    End Function

    Public Shared Function FormatTimestamp(value As Date) As String
        Return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function FormatTimestampSafe(value As Date) As String
        Try
            Return FormatTimestamp(value)
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>A codec as a person reads it, from libvlc's FourCC - the video twin of
    ''' <see cref="AudioMetadata.CodecName"/>. Anything unmapped shows its four letters;
    ''' a value that is not four printable letters shows nothing rather than noise.</summary>
    Public Shared Function VideoCodecName(fourCc As UInteger) As String
        If fourCc = 0UI Then Return ""
        Dim chars(3) As Char
        For i As Integer = 0 To 3
            Dim b As Integer = CInt((fourCc >> (8 * i)) And &HFFUI)
            If b < 32 OrElse b > 126 Then Return ""
            chars(i) = ChrW(b)
        Next
        Dim text As String = New String(chars).Trim()
        If text.Length = 0 Then Return ""
        Select Case text.ToLowerInvariant()
            Case "h264", "x264", "avc1" : Return "H.264"
            Case "hevc", "h265", "x265" : Return "HEVC"
            Case "av01" : Return "AV1"
            Case "vp09" : Return "VP9"
            Case "vp08", "vp80" : Return "VP8"
            Case "mp4v", "fmp4", "xvid", "divx", "div3", "dx50" : Return "MPEG-4"
            Case "mpgv", "mp1v" : Return "MPEG-1"
            Case "mp2v", "mpgv2" : Return "MPEG-2"
            Case "mjpg", "mjpa" : Return "MJPEG"
            Case "theo" : Return "Theora"
            Case "wmv1", "wmv2", "wmv3", "wmvp" : Return "WMV"
            Case "png " : Return "PNG"
        End Select
        Return text.ToUpperInvariant()
    End Function

    ' --- the EXIF reader ---------------------------------------------------------------------

    ''' <summary>Tag ids this window reads. Names mirror the EXIF specification; the list
    ''' is deliberately short - a tag nobody can act on is noise, and this is a viewer,
    ''' not an editor.</summary>
    Public NotInheritable Class ExifTag
        Private Sub New()
        End Sub
        Public Const ImageWidth As Integer = &H100
        Public Const ImageLength As Integer = &H101
        Public Const Make As Integer = &H10F
        Public Const Model As Integer = &H110
        Public Const Orientation As Integer = &H112
        Public Const XResolution As Integer = &H11A
        Public Const YResolution As Integer = &H11B
        Public Const ResolutionUnit As Integer = &H128
        Public Const Software As Integer = &H131
        Public Const DateTime As Integer = &H132
        Public Const Artist As Integer = &H13B
        Public Const ExposureTime As Integer = &H829A
        Public Const FNumber As Integer = &H829D
        Public Const IsoSpeedRatings As Integer = &H8827
        Public Const DateTimeOriginal As Integer = &H9003
        Public Const FocalLength As Integer = &H920A
        Public Const GpsLatitudeRef As Integer = &H1
        Public Const GpsLatitude As Integer = &H2
        Public Const GpsLongitudeRef As Integer = &H3
        Public Const GpsLongitude As Integer = &H4
    End Class

    ''' <summary>
    ''' Reads the EXIF of a JPEG (APP1) or the IFDs of a TIFF straight from the bytes -
    ''' no GDI+ (Utils.GetImageDimensions documents why concurrent decoding is avoided).
    ''' Defensive by design: a damaged or truncated file yields Nothing, never a crash.
    ''' </summary>
    Public NotInheritable Class ExifReader

        Private ReadOnly data As Byte()
        Private ReadOnly tiffStart As Integer
        Private ReadOnly littleEndian As Boolean
        Private ReadOnly entryOffsets As New Dictionary(Of Integer, Integer)()

        Private Sub New(data As Byte(), tiffStart As Integer, littleEndian As Boolean)
            Me.data = data
            Me.tiffStart = tiffStart
            Me.littleEndian = littleEndian
        End Sub

        ''' <summary>A JPEG carrying an Exif APP1 segment, or a TIFF file itself. Nothing
        ''' for anything else (PNG carries text chunks, not EXIF).</summary>
        Public Shared Function TryRead(header As Byte()) As ExifReader
            If header Is Nothing OrElse header.Length < 12 Then Return Nothing
            If (header(0) = &H49 AndAlso header(1) = &H49) OrElse (header(0) = &H4D AndAlso header(1) = &H4D) Then
                Dim reader As New ExifReader(header, 0, header(0) = &H49)
                ' The TIFF header's own field: WHERE IFD0 sits (usually 8), not IFD0 itself.
                If Not reader.ReadIfd(CInt(reader.ReadUInt32(reader.data, 4))) Then Return Nothing
                Return reader
            End If
            If header(0) = &HFF AndAlso header(1) = &HD8 Then
                Dim position As Integer = 2
                While position + 4 <= header.Length
                    If header(position) <> &HFF Then Return Nothing
                    Dim marker As Integer = header(position + 1)
                    If marker = &HD8 OrElse marker = &H01 OrElse (marker >= &HD0 AndAlso marker <= &HD7) Then
                        position += 2
                        Continue While
                    End If
                    Dim length As Integer = (CInt(header(position + 2)) << 8) Or header(position + 3)
                    If length < 2 Then Return Nothing
                    If marker = &HE1 AndAlso position + 13 <= header.Length AndAlso
                       header(position + 4) = &H45 AndAlso header(position + 5) = &H78 AndAlso
                       header(position + 6) = &H69 AndAlso header(position + 7) = &H66 AndAlso
                       (header(position + 10) = &H49 AndAlso header(position + 11) = &H49 OrElse
                        header(position + 10) = &H4D AndAlso header(position + 11) = &H4D) Then
                        Dim reader As New ExifReader(header, position + 10, header(position + 10) = &H49)
                        ' The IFD0 offset field sits at the TIFF header's own +4, which
                        ' here is absolute position + 14 (the header begins at +10).
                        If Not reader.ReadIfd(CInt(reader.ReadUInt32(reader.data, position + 14))) Then Return Nothing
                        Return reader
                    End If
                    position += 2 + length
                End While
            End If
            Return Nothing
        End Function

        ''' <summary>Walks IFD0, then its Exif and GPS sub-tables. Returns False when the
        ''' first IFD itself is not readable - a file without IFDs has no EXIF.</summary>
        Private Function ReadIfd(offsetWithinTiff As Integer) As Boolean
            If Not ReadOneIfd(offsetWithinTiff, readSubTables:=True) Then Return False
            Dim subIfd As Integer = FindEntryOffset(ExifTagReservedExifIfd)
            If subIfd >= 0 Then ReadOneIfd(subIfd, readSubTables:=False)
            Dim gpsIfd As Integer = FindEntryOffset(ExifTagReservedGpsIfd)
            If gpsIfd >= 0 Then ReadOneIfd(gpsIfd, readSubTables:=False)
            Return True
        End Function

        ' The two pointer tags: ids this window never reads directly, only follows.
        Private Const ExifTagReservedExifIfd As Integer = &H8769
        Private Const ExifTagReservedGpsIfd As Integer = &H8825

        Private Function ReadOneIfd(offsetWithinTiff As Integer, readSubTables As Boolean) As Boolean
            If offsetWithinTiff < 2 OrElse offsetWithinTiff > data.Length - 2 Then Return False
            Dim ifdStart As Integer = tiffStart + offsetWithinTiff
            Dim count As Integer = ReadUInt16(data, ifdStart)
            If count = 0 OrElse count > 512 Then Return False
            If ifdStart + 2 + count * 12 + 4 > data.Length Then Return False
            For i As Integer = 0 To count - 1
                Dim entry As Integer = ifdStart + 2 + i * 12
                Dim tag As Integer = ReadUInt16(data, entry)
                entryOffsets(tag) = entry
            Next
            Return True
        End Function

        ''' <summary>The IFD-relative offset a pointer TAG points at, or -1. Only used for
        ''' the Exif and GPS sub-tables, whose value is a single LONG.</summary>
        Private Function FindEntryOffset(tag As Integer) As Integer
            Dim entry As Integer = 0
            If Not entryOffsets.TryGetValue(tag, entry) Then Return -1
            Return CInt(ReadUInt32(data, entry + 8))
        End Function

        ''' <summary>TIFF fields in the file's own byte order. The bytes widen to Long
        ''' BEFORE shifting: VB masks a shift count to the width of the operand, so a
        ''' Byte shifted by 8 is not shifted at all (audit of this reader's first run -
        ''' every tag read as the sum of its two bytes).</summary>
        Private Function ReadUInt16(bytes As Byte(), offset As Integer) As Integer
            If littleEndian Then Return CInt(CLng(bytes(offset)) Or (CLng(bytes(offset + 1)) << 8))
            Return CInt((CLng(bytes(offset)) << 8) Or CLng(bytes(offset + 1)))
        End Function

        Private Function ReadUInt32(bytes As Byte(), offset As Integer) As Long
            If littleEndian Then
                Return CLng(bytes(offset)) Or (CLng(bytes(offset + 1)) << 8) Or
                       (CLng(bytes(offset + 2)) << 16) Or (CLng(bytes(offset + 3)) << 24)
            End If
            Return (CLng(bytes(offset)) << 24) Or (CLng(bytes(offset + 1)) << 16) Or
                   (CLng(bytes(offset + 2)) << 8) Or CLng(bytes(offset + 3))
        End Function

        Private Function EntryValueOffset(entry As Integer) As Integer
            ' An entry's value is inline when it fits in four bytes, else a TIFF-relative
            ' offset to it. The byte count comes from the type and count fields - and a
            ' RATIONAL is EIGHT bytes, so a single one never fits inline (the first run of
            ' this reader got that wrong and read 1/250 s as 1/3128 s).
            Dim type As Integer = ReadUInt16(data, entry + 2)
            Dim count As Long = ReadUInt32(data, entry + 4)
            Dim typeSize As Integer
            Select Case type
                Case 3 : typeSize = 2      ' SHORT
                Case 4, 9 : typeSize = 4  ' LONG, SLONG
                Case 5, 10 : typeSize = 8  ' RATIONAL, SRATIONAL
                Case Else : typeSize = 1   ' BYTE, ASCII, UNDEFINED
            End Select
            If typeSize * count <= 4 Then Return entry + 8
            Return tiffStart + CInt(ReadUInt32(data, entry + 8))
        End Function

        ''' <summary>A positive, in-range value offset for the entry, or -1 (bounds are
        ''' re-checked per tag, so a damaged entry degrades to "no value", never to a
        ''' crash on a half-readable file).</summary>
        Private Function ValueOffset(tag As Integer) As Integer
            Dim entry As Integer = 0
            If Not entryOffsets.TryGetValue(tag, entry) Then Return -1
            Dim offset As Integer = EntryValueOffset(entry)
            If offset < 0 OrElse offset >= data.Length Then Return -1
            Return offset
        End Function

        Public Function AsciiValue(tag As Integer) As String
            Dim offset As Integer = ValueOffset(tag)
            If offset < 0 Then Return ""
            Dim length As Integer = 0
            While offset + length < data.Length AndAlso length < 1024 AndAlso data(offset + length) <> 0
                length += 1
            End While
            Dim text As String = Encoding.ASCII.GetString(data, offset, length)
            ' Some phones pad with spaces; the plate style here trims display text.
            Return text.Trim()
        End Function

        ''' <summary>A SHORT (or LONG, which some writers use for these tags) as the first
        ''' value, or Nothing. The tag ids here are read one value at a time - a pair such
        ''' as ISO in old Nikon bodies still reports its first member.</summary>
        Public Function ShortValue(tag As Integer) As Integer?
            Dim entry As Integer = 0
            If Not entryOffsets.TryGetValue(tag, entry) Then Return Nothing
            Dim offset As Integer = ValueOffset(tag)
            If offset < 0 OrElse offset + 2 > data.Length Then Return Nothing
            Dim type As Integer = ReadUInt16(data, entry + 2)
            If type = 4 Then Return CInt(ReadUInt32(data, offset))
            Return ReadUInt16(data, offset)
        End Function

        Public Function LongValue(tag As Integer) As Long?
            Dim offset As Integer = ValueOffset(tag)
            If offset < 0 OrElse offset + 4 > data.Length Then Return Nothing
            Return ReadUInt32(data, offset)
        End Function

        ''' <summary>The first unsigned rational (numerator/denominator) of a tag.</summary>
        Public Function RationalValue(tag As Integer) As Double?
            Dim values As Double() = RationalValues(tag)
            If values.Length = 0 Then Return Nothing
            Return values(0)
        End Function

        ''' <summary>Every rational of a tag (GPS coordinates are three: degrees, minutes,
        ''' seconds). A zero or absent denominator yields nothing rather than Infinity.</summary>
        Public Function RationalValues(tag As Integer) As Double()
            Dim offset As Integer = ValueOffset(tag)
            If offset < 0 Then Return New Double() {}
            Dim entry As Integer = 0
            If Not entryOffsets.TryGetValue(tag, entry) Then Return New Double() {}
            Dim count As Long = ReadUInt32(data, entry + 4)
            If count <= 0 OrElse count > 64 OrElse offset + count * 8 > data.Length Then Return New Double() {}
            Dim result(CInt(count) - 1) As Double
            For i As Integer = 0 To CInt(count) - 1
                Dim numerator As Long = ReadUInt32(data, offset + i * 8)
                Dim denominator As Long = ReadUInt32(data, offset + i * 8 + 4)
                If denominator = 0 Then Return New Double() {}
                result(i) = numerator / CDbl(denominator)
            Next
            Return result
        End Function

        ''' <summary>"54.6872° N, 25.2797° E" - decimal degrees with the hemisphere, the
        ''' form that can be pasted straight into a map. Empty when either half is
        ''' missing or the reference letter is not what the specification allows.</summary>
        Public Function GpsCoordinateText() As String
            Dim latitude As Double? = GpsPart(ExifTag.GpsLatitude, ExifTag.GpsLatitudeRef, "N"c, "S"c)
            Dim longitude As Double? = GpsPart(ExifTag.GpsLongitude, ExifTag.GpsLongitudeRef, "E"c, "W"c)
            If Not latitude.HasValue OrElse Not longitude.HasValue Then Return ""
            Return latitude.Value.ToString("0.####", CultureInfo.InvariantCulture) & "° " &
                   GpsReference(ExifTag.GpsLatitudeRef) & ", " &
                   longitude.Value.ToString("0.####", CultureInfo.InvariantCulture) & "° " &
                   GpsReference(ExifTag.GpsLongitudeRef)
        End Function

        Private Function GpsPart(valueTag As Integer, referenceTag As Integer,
                                 positive As Char, negative As Char) As Double?
            Dim parts As Double() = RationalValues(valueTag)
            If parts.Length < 3 Then Return Nothing
            Dim reference As String = GpsReference(referenceTag)
            If reference.Length = 0 Then Return Nothing
            Dim value As Double = parts(0) + parts(1) / 60.0 + parts(2) / 3600.0
            If reference(0) = negative Then Return -value
            If reference(0) = positive Then Return value
            Return Nothing
        End Function

        Private Function GpsReference(referenceTag As Integer) As String
            Dim reference As String = AsciiValue(referenceTag).ToUpperInvariant()
            If reference.Length = 0 Then Return ""
            If reference = "N" OrElse reference = "S" OrElse reference = "E" OrElse reference = "W" Then Return reference
            Return ""
        End Function

        ''' <summary>EXIF writes its own date shape ("yyyy:MM:dd HH:mm:ss"); the window
        ''' shows the one the rest of the product shows. A value that does not parse is
        ''' shown verbatim rather than dropped - a partially written tag is still the
        ''' user's own data.</summary>
        Public Shared Function ExifDateText(value As String) As String
            If String.IsNullOrEmpty(value) Then Return ""
            Dim parsed As Date
            If Date.TryParseExact(value.Trim(), "yyyy:MM:dd HH:mm:ss",
                                  CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
                Return FormatTimestamp(parsed)
            End If
            Return value.Trim()
        End Function
    End Class
End Class
