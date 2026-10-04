Option Strict On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports Xunit

''' <summary>
''' The pure model of the "about file" window (the first panel's "i" item):
''' the formatters the window prints values with, and the EXIF reader the camera
''' block is built from - on BOTH legs, because the window ships in both viewers.
''' The EXIF fixtures are built in memory: a real photo per test would make the
''' suite depend on somebody's holiday pictures.
''' </summary>
Public Class FileDetailsTests

    ' --- formatters -----------------------------------------------------------------------

    <Theory>
    <InlineData(512, "512 B")>
    <InlineData(1024, "1.00 KB")>
    <InlineData(1536, "1.50 KB")>
    <InlineData(1048576, "1.00 MB")>
    <InlineData(12345678, "11.77 MB")>
    <InlineData(-1, "")>
    Public Sub Size_is_printed_in_binary_units(value As Long, expected As String)
        Assert.Equal(expected, FileDetails.FormatSize(value))
    End Sub

    <Theory>
    <InlineData(59000, "0:59")>
    <InlineData(185000, "3:05")>
    <InlineData(3723000, "1:02:03")>
    <InlineData(0, "")>
    Public Sub Duration_shows_minutes_then_total_hours(milliseconds As Long, expected As String)
        Assert.Equal(expected, FileDetails.FormatDuration(milliseconds))
    End Sub

    <Theory>
    <InlineData(128000, "128 kbps")>
    <InlineData(999999, "999 kbps")>
    <InlineData(2350000, "2.35 Mbps")>
    <InlineData(0, "")>
    Public Sub Bitrate_switches_units_at_a_megabit(bitsPerSecond As Long, expected As String)
        Assert.Equal(expected, FileDetails.FormatBitrate(bitsPerSecond))
    End Sub

    <Fact>
    Public Sub Timestamp_is_invariant_so_two_surfaces_never_disagree()
        Assert.Equal("2020-01-02 13:45:12",
                     FileDetails.FormatTimestamp(New Date(2020, 1, 2, 13, 45, 12, DateTimeKind.Local)))
    End Sub

    <Theory>
    <InlineData(0.004, "1/250 s")>
    <InlineData(0.5, "1/2 s")>
    <InlineData(2.5, "2.5 s")>
    <InlineData(0.0001, "1/10000 s")>
    <InlineData(0, "")>
    Public Sub Exposure_reads_like_a_camera_dial(seconds As Double, expected As String)
        Assert.Equal(expected, FileDetails.ExposureText(seconds))
    End Sub

    <Theory>
    <InlineData(2.8, "f/2.8")>
    <InlineData(1.8, "f/1.8")>
    <InlineData(11.0, "f/11")>
    Public Sub Aperture_shows_one_decimal_at_most(fNumber As Double, expected As String)
        Assert.Equal(expected, FileDetails.ApertureText(fNumber))
    End Sub

    <Theory>
    <InlineData(50.2, "50 mm")>
    <InlineData(24.0, "24 mm")>
    Public Sub Focal_length_is_rounded_to_whole_millimetres(millimetres As Double, expected As String)
        Assert.Equal(expected, FileDetails.FocalText(millimetres))
    End Sub

    <Theory>
    <InlineData("2020:01:02 13:45:12", "2020-01-02 13:45:12")>
    <InlineData("2020:01:02", "2020:01:02")>
    Public Sub An_exif_date_is_reshaped_and_a_partial_one_is_kept_verbatim(value As String, expected As String)
        Assert.Equal(expected, FileDetails.ExifReader.ExifDateText(value))
    End Sub

    <Fact>
    Public Sub Video_fourcc_reads_as_a_person_reads_a_codec()
        Assert.Equal("H.264", FileDetails.VideoCodecName(FourCc("h264")))
        Assert.Equal("HEVC", FileDetails.VideoCodecName(FourCc("hevc")))
        Assert.Equal("AV1", FileDetails.VideoCodecName(FourCc("av01")))
        Assert.Equal("VP9", FileDetails.VideoCodecName(FourCc("vp09")))
        Assert.Equal("WMV", FileDetails.VideoCodecName(FourCc("wmv3")))
        Assert.Equal("ZZZ1", FileDetails.VideoCodecName(FourCc("zzz1")))
        Assert.Equal("", FileDetails.VideoCodecName(0UI))
        Assert.Equal("", FileDetails.VideoCodecName(&HFF00FF00UI))
    End Sub

    Private Shared Function FourCc(text As String) As UInteger
        Dim value As UInteger = 0
        For i As Integer = 0 To 3
            value = value Or CUInt(Convert.ToInt32(Convert.ToByte(text(i))) << (8 * i))
        Next
        Return value
    End Function

    ' --- attributes ------------------------------------------------------------------------

    <Fact>
    Public Sub Attributes_are_words_and_normal_is_nothing()
        Assert.Equal("", FileDetails.DescribeAttributes(IO.FileAttributes.Normal))
        Assert.Equal(Localization.T("только чтение"), FileDetails.DescribeAttributes(IO.FileAttributes.ReadOnly))
        Dim described As String = FileDetails.DescribeAttributes(IO.FileAttributes.ReadOnly Or IO.FileAttributes.Hidden)
        Assert.Equal(Localization.T("только чтение") & ", " & Localization.T("скрытый"), described)
    End Sub

    ' --- the EXIF reader -------------------------------------------------------------------

    <Fact>
    Public Sub A_jpeg_app1_segment_yields_its_camera_rows()
        Dim probe As String = Nothing
        Try
            probe = TempFileBytes(ExifJpegBytes(), ".jpg")
            Dim camera As FileDetailSection = CameraOf(probe)
            Assert.NotNull(camera)
            Assert.Equal("Test", ValueOf(camera, "Камера"))
            Assert.Equal("1/250 s", ValueOf(camera, "Выдержка"))
            Assert.Equal("200", ValueOf(camera, "ISO"))
        Finally
            DeleteQuietly(probe)
        End Try
    End Sub

    <Fact>
    Public Sub A_file_without_exif_gets_no_camera_block()
        Dim probe As String = Nothing
        Try
            probe = TempFileBytes(New Byte() {&HFF, &HD8, &HFF, &HD9}, ".jpg")
            Assert.Null(CameraOf(probe))
        Finally
            DeleteQuietly(probe)
        End Try
    End Sub

    <Fact>
    Public Sub Gps_rationals_become_pasteable_decimal_degrees()
        Dim reader As FileDetails.ExifReader = FileDetails.ExifReader.TryRead(GpsTiffBytes())
        Assert.NotNull(reader)
        Assert.Equal("54.6847° N, 25.2781° E", reader.GpsCoordinateText())
    End Sub

    <Fact>
    Public Sub A_truncated_tiff_degrades_to_no_exif_rather_than_an_error()
        Dim truncated As Byte() = New Byte(11) {}
        Array.Copy(GpsTiffBytes(), truncated, 12)
        Assert.Null(FileDetails.ExifReader.TryRead(truncated))
    End Sub

    ' --- the image and document sections ---------------------------------------------------

    <Fact>
    Public Sub A_png_header_gives_size_and_depth_without_decoding()
        Dim probe As String = Nothing
        Try
            probe = TempFileBytes(PngBytes(4, 4, 8, 6), ".png")
            Dim sections As List(Of FileDetailSection) = FileDetails.BuildImageSections(probe)
            Dim image As FileDetailSection = SectionTitled(sections, "Изображение")
            Assert.NotNull(image)
            Assert.Equal("4 × 4 (0.0 MP)", ValueOf(image, "Разрешение"))
            Assert.Equal("32 bpp", ValueOf(image, "Глубина цвета"))
        Finally
            DeleteQuietly(probe)
        End Try
    End Sub

    <Fact>
    Public Sub A_text_file_reports_its_shape()
        Dim probe As String = Nothing
        Try
            probe = Path.Combine(Path.GetTempPath(), "fmsl_file_details_" & Guid.NewGuid().ToString("N") & ".txt")
            File.WriteAllText(probe, "one two" & vbCrLf & "three", New UTF8Encoding(False))
            Dim sections As List(Of FileDetailSection) = FileDetails.BuildDocumentSections(probe)
            Dim document As FileDetailSection = SectionTitled(sections, "Документ")
            Assert.NotNull(document)
            Assert.Equal("2", ValueOf(document, "Строк"))
            Assert.Equal("3", ValueOf(document, "Слов"))
        Finally
            DeleteQuietly(probe)
        End Try
    End Sub

    <Fact>
    Public Sub The_file_section_names_every_file_and_skips_absent_ones()
        Dim probe As String = Nothing
        Try
            probe = Path.Combine(Path.GetTempPath(), "fmsl_file_details_" & Guid.NewGuid().ToString("N") & ".bin")
            File.WriteAllBytes(probe, New Byte() {1, 2, 3, 4, 5, 6, 7, 8, 9})
            Dim section As FileDetailSection = FileDetails.BuildFileSection(probe)
            Assert.False(section.IsEmpty)
            Assert.Equal("9 B", ValueOf(section, "Размер"))
            Assert.Equal("9", ValueOf(section, "Байт"))
            Assert.Equal(".bin".ToUpperInvariant(), ValueOf(section, "Тип"))
            ' Normal files carry no attribute words - the row is absent, not blank.
            Assert.Null(RowOf(section, "Атрибуты"))

            Dim missing As FileDetailSection = FileDetails.BuildFileSection(
                Path.Combine(Path.GetTempPath(), "fmsl_file_details_no_such_dir\missing.bin"))
            Assert.True(missing.IsEmpty)
        Finally
            DeleteQuietly(probe)
        End Try
    End Sub

    ' --- fixtures --------------------------------------------------------------------------

    ''' <summary>A minimal JPEG: SOI, one APP1 Exif segment carrying a TIFF block, EOI.
    ''' No pixels - the window reads the header, and the test only needs the tags.</summary>
    Private Shared Function ExifJpegBytes() As Byte()
        Dim tiff As Byte() = ExifTiffBytes()
        Dim payloadLength As Integer = 6 + tiff.Length
        Dim stream As New MemoryStream()
        stream.WriteByte(&HFF) : stream.WriteByte(&HD8)
        stream.WriteByte(&HFF) : stream.WriteByte(&HE1)
        stream.WriteByte(CByte((payloadLength + 2) >> 8)) : stream.WriteByte(CByte((payloadLength + 2) And &HFF))
        stream.Write(New Byte() {&H45, &H78, &H69, &H66, 0, 0}, 0, 6)
        stream.Write(tiff, 0, tiff.Length)
        stream.WriteByte(&HFF) : stream.WriteByte(&HD9)
        Return stream.ToArray()
    End Function

    ''' <summary>The TIFF block inside that segment: IFD0 pointing at one Exif sub-IFD.
    ''' IFD0: Model ("Test") + the SubIFD pointer. SubIFD: ExposureTime (1/250) + ISO (200).</summary>
    Private Shared Function ExifTiffBytes() As Byte()
        Dim data As Byte() = New Byte(81) {}
        data(0) = &H49 : data(1) = &H49 : data(2) = &H2A : data(3) = 0
        SetU32(data, 4, 8)                 ' IFD0 at 8

        SetU16(data, 8, 2)                 ' two entries
        SetU16(data, 10, &H110) : SetU16(data, 12, 2) : SetU32(data, 14, 5) : SetU32(data, 18, 68)  ' Model
        SetU16(data, 22, &H8769) : SetU16(data, 24, 4) : SetU32(data, 26, 1) : SetU32(data, 30, 38) ' Exif IFD at 38
        SetU32(data, 34, 0)                ' no further IFD

        SetU16(data, 38, 2)                ' two entries
        SetU16(data, 40, &H829A) : SetU16(data, 42, 5) : SetU32(data, 44, 1) : SetU32(data, 48, 74) ' ExposureTime at 74
        SetU16(data, 52, &H8827) : SetU16(data, 54, 3) : SetU32(data, 56, 1) : SetU16(data, 60, 200) ' ISO inline
        SetU32(data, 64, 0)

        SetAscii(data, 68, "Test")         ' 5 bytes with the NUL
        SetU32(data, 74, 1) : SetU32(data, 78, 250)  ' the 1/250 rational
        Return data
    End Function

    ''' <summary>A TIFF carrying only a GPS IFD: 54°41'5" N, 25°16'41" E.</summary>
    Private Shared Function GpsTiffBytes() As Byte()
        Dim data As Byte() = New Byte(127) {}
        data(0) = &H49 : data(1) = &H49 : data(2) = &H2A : data(3) = 0
        SetU32(data, 4, 8)                 ' IFD0 at 8

        SetU16(data, 8, 1)                 ' one entry
        SetU16(data, 10, &H8825) : SetU16(data, 12, 4) : SetU32(data, 14, 1) : SetU32(data, 18, 26) ' GPS IFD at 26
        SetU32(data, 22, 0)

        SetU16(data, 26, 4)                ' four entries
        SetU16(data, 28, 1) : SetU16(data, 30, 2) : SetU32(data, 32, 2)                         ' LatitudeRef inline
        data(36) = Convert.ToByte("N"c)
        SetU16(data, 40, 2) : SetU16(data, 42, 5) : SetU32(data, 44, 3) : SetU32(data, 48, 80)   ' Latitude at 80
        SetU16(data, 52, 3) : SetU16(data, 54, 2) : SetU32(data, 56, 2)                         ' LongitudeRef inline
        data(60) = Convert.ToByte("E"c)
        SetU16(data, 64, 4) : SetU16(data, 66, 5) : SetU32(data, 68, 3) : SetU32(data, 72, 104)  ' Longitude at 104
        SetU32(data, 76, 0)

        SetU32(data, 80, 54) : SetU32(data, 84, 1) : SetU32(data, 88, 41) : SetU32(data, 92, 1) : SetU32(data, 96, 5) : SetU32(data, 100, 1)
        SetU32(data, 104, 25) : SetU32(data, 108, 1) : SetU32(data, 112, 16) : SetU32(data, 116, 1) : SetU32(data, 120, 41) : SetU32(data, 124, 1)
        Return data
    End Function

    ''' <summary>A minimal PNG whose IHDR says width x height, bitDepth, colourType.
    ''' The CRC is zeroed: the window's parser reads the fields, it does not verify the file.</summary>
    Private Shared Function PngBytes(width As Integer, height As Integer, bitDepth As Integer, colorType As Integer) As Byte()
        Dim data As Byte() = New Byte(32) {}
        Dim signature As Byte() = {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA}
        Array.Copy(signature, data, 8)
        SetU32(data, 8, 13)                ' IHDR length
        data(12) = &H49 : data(13) = &H48 : data(14) = &H44 : data(15) = &H52  ' "IHDR"
        ' IHDR's width and height are BIG-endian, unlike everything in the fixtures
        ' above: the header parser reads them the PNG way.
        SetU32BE(data, 16, width)
        SetU32BE(data, 20, height)
        data(24) = CByte(bitDepth)
        data(25) = CByte(colorType)
        ' 26..27 compression/filter, 28 interlace, 29..32 CRC: already zero.
        Return data
    End Function

    Private Shared Sub SetU16(data As Byte(), offset As Integer, value As Integer)
        data(offset) = CByte(value And &HFF)
        data(offset + 1) = CByte((value >> 8) And &HFF)
    End Sub

    Private Shared Sub SetU32(data As Byte(), offset As Integer, value As Long)
        data(offset) = CByte(value And &HFF)
        data(offset + 1) = CByte((value >> 8) And &HFF)
        data(offset + 2) = CByte((value >> 16) And &HFF)
        data(offset + 3) = CByte((value >> 24) And &HFF)
    End Sub

    ''' <summary>Big-endian, the PNG and EXIF way of counting.</summary>
    Private Shared Sub SetU32BE(data As Byte(), offset As Integer, value As Long)
        data(offset) = CByte((value >> 24) And &HFF)
        data(offset + 1) = CByte((value >> 16) And &HFF)
        data(offset + 2) = CByte((value >> 8) And &HFF)
        data(offset + 3) = CByte(value And &HFF)
    End Sub

    Private Shared Sub SetAscii(data As Byte(), offset As Integer, text As String)
        For i As Integer = 0 To text.Length - 1
            data(offset + i) = Convert.ToByte(text(i))
        Next
    End Sub

    Private Shared Function TempFileBytes(bytes As Byte(), extension As String) As String
        Dim fullPath As String = Path.Combine(Path.GetTempPath(), "fmsl_file_details_" & Guid.NewGuid().ToString("N") & extension)
        File.WriteAllBytes(fullPath, bytes)
        Return fullPath
    End Function

    Private Shared Sub DeleteQuietly(path As String)
        If path IsNot Nothing Then
            Try : File.Delete(path) : Catch : End Try
        End If
    End Sub

    Private Shared Function CameraOf(probedPath As String) As FileDetailSection
        Return SectionTitled(FileDetails.BuildImageSections(probedPath), "Камера")
    End Function

    Private Shared Function SectionTitled(sections As List(Of FileDetailSection), titleKey As String) As FileDetailSection
        For Each section As FileDetailSection In sections
            If String.Equals(section.TitleKey, titleKey, StringComparison.Ordinal) Then Return section
        Next
        Return Nothing
    End Function

    Private Shared Function RowOf(section As FileDetailSection, labelKey As String) As FileDetailRow
        For Each row As FileDetailRow In section.Rows
            If String.Equals(row.LabelKey, labelKey, StringComparison.Ordinal) Then Return row
        Next
        Return Nothing
    End Function

    Private Shared Function ValueOf(section As FileDetailSection, labelKey As String) As String
        Dim row As FileDetailRow = RowOf(section, labelKey)
        If row Is Nothing Then Return Nothing
        Return row.Value
    End Function
End Class
