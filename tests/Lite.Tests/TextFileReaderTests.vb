#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.IO
Imports System.Text
Imports Xunit

Public Class TextFileReaderTests

        <Fact>
        Public Sub Empty_file_decodes_cleanly()
            Dim bytes As Byte() = Array.Empty(Of Byte)()
            Dim res As TextReadResult = TextFileReader.ReadBytes(bytes, 0, ".txt")
            Assert.True(res.Success)
            Assert.False(res.IsBinary)
            Assert.Equal(String.Empty, res.Text)
            Assert.Empty(res.Lines)
            Assert.Equal(0L, res.TotalBytes)
            Assert.Equal(0L, res.ReadBytes)
            Assert.False(res.IsTruncated)
        End Sub

        <Fact>
        Public Sub Utf8_without_bom_is_detected_and_decoded()
            Dim input As String = "Hello world!" & vbCrLf & "Второй ряд текста"
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(input)
            Dim res As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt")
            Assert.True(res.Success)
            Assert.False(res.IsBinary)
            Assert.Equal(input, res.Text)
            Assert.Equal(2, res.Lines.Length)
            Assert.Equal("Hello world!", res.Lines(0))
            Assert.Equal("Второй ряд текста", res.Lines(1))
        End Sub

        <Fact>
        Public Sub Utf8_with_bom_is_detected_and_bom_is_stripped()
            Dim input As String = "BOM Header Text"
            Dim bodyBytes As Byte() = Encoding.UTF8.GetBytes(input)
            Dim bomBytes As Byte() = Encoding.UTF8.GetPreamble()
            Dim fullBytes(bomBytes.Length + bodyBytes.Length - 1) As Byte
            Array.Copy(bomBytes, 0, fullBytes, 0, bomBytes.Length)
            Array.Copy(bodyBytes, 0, fullBytes, bomBytes.Length, bodyBytes.Length)

            Dim res As TextReadResult = TextFileReader.ReadBytes(fullBytes, fullBytes.Length, ".txt")
            Assert.True(res.Success)
            Assert.False(res.IsBinary)
            Assert.Equal(input, res.Text)
        End Sub

        <Fact>
        Public Sub Utf16_le_and_be_are_detected_and_decoded()
            Dim input As String = "UTF-16 text: привет мир"
            
            ' LE with BOM
            Dim bytesLE As Byte() = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(input)).ToArray()
            Dim resLE As TextReadResult = TextFileReader.ReadBytes(bytesLE, bytesLE.Length, ".txt")
            Assert.True(resLE.Success)
            Assert.False(resLE.IsBinary)
            Assert.Equal(input, resLE.Text)

            ' BE with BOM
            Dim bytesBE As Byte() = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(input)).ToArray()
            Dim resBE As TextReadResult = TextFileReader.ReadBytes(bytesBE, bytesBE.Length, ".txt")
            Assert.True(resBE.Success)
            Assert.False(resBE.IsBinary)
            Assert.Equal(input, resBE.Text)
        End Sub

        <Fact>
        Public Sub Ansi_cp1251_text_is_decoded_when_not_utf8()
#If Not NETFRAMEWORK Then
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
            Dim cp1251 As Encoding = Encoding.GetEncoding(1251)
            Dim input As String = "Текст в кодировке Windows-1251"
            Dim bytes As Byte() = cp1251.GetBytes(input)

            Dim res As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt", ansiCodePage:=1251)
            Assert.True(res.Success)
            Assert.False(res.IsBinary)
            Assert.Equal(input, res.Text)
            Assert.Equal(1251, res.DetectedEncoding.CodePage)
        End Sub

        <Fact>
        Public Sub Random_binary_bytes_are_declined_as_binary()
            Dim rnd As New Random(42)
            Dim bytes(1024) As Byte
            rnd.NextBytes(bytes)
            ' Insert several NUL and control bytes
            bytes(10) = 0
            bytes(20) = 1
            bytes(30) = 2

            Dim res As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".bin")
            Assert.False(res.Success)
            Assert.True(res.IsBinary)
        End Sub

        <Fact>
        Public Sub Size_ceiling_applies_to_10mb_line_without_hanging()
            ' Create a 10 MB payload
            Dim lineLength As Integer = 10 * 1024 * 1024
            Dim bytes(lineLength - 1) As Byte
            For i As Integer = 0 To bytes.Length - 1
                bytes(i) = CByte(AscW("A"c) + (i Mod 26))
            Next

            ' Read with maxKb = 100 (100 KB limit)
            Using ms As New MemoryStream(bytes)
                Dim res As TextReadResult = TextFileReader.Read(ms, bytes.Length, ".log", maxKb:=100)
                Assert.True(res.Success)
                Assert.False(res.IsBinary)
                Assert.True(res.IsTruncated)
                Assert.Equal(100 * 1024L, res.ReadBytes)
                Assert.Equal(bytes.Length, res.TotalBytes)
                Assert.Equal(100 * 1024, res.Text.Length)
            End Using
        End Sub

        <Fact>
        Public Sub Monospace_extension_classification_matches_spec()
            Assert.True(TextFileReader.IsMonospace(".log"))
            Assert.True(TextFileReader.IsMonospace(".csv"))
            Assert.True(TextFileReader.IsMonospace(".json"))
            Assert.True(TextFileReader.IsMonospace(".xml"))
            Assert.True(TextFileReader.IsMonospace("C:\path\server.log"))

            Assert.False(TextFileReader.IsMonospace(".txt"))
            Assert.False(TextFileReader.IsMonospace(".md"))
            Assert.False(TextFileReader.IsMonospace("README.md"))
        End Sub

        <Fact>
        Public Sub Tabs_are_expanded_to_4_spaces()
            Dim input As String = "col1" & vbTab & "col2" & vbTab & "col3"
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(input)
            Dim res As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt")
            Assert.True(res.Success)
            Assert.Equal("col1    col2    col3", res.Text)
        End Sub

        <Fact>
        Public Sub Utf32_both_endiannesses_strip_bom()
            For Each encoding As Encoding In {New UTF32Encoding(False, True), New UTF32Encoding(True, True)}
                Dim bytes As Byte() = encoding.GetPreamble().Concat(encoding.GetBytes("Text Ж 😀")).ToArray()
                Dim result As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt")
                Assert.True(result.Success)
                Assert.Equal("Text Ж 😀", result.Text)
            Next
        End Sub

        <Fact>
        Public Sub Utf8_probe_and_preview_boundaries_do_not_split_characters()
            Dim input As String = New String("A"c, 65535) & "€" & " tail"
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(input)
            Dim full As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt")
            Assert.Equal(input, full.Text)
            Dim prefix As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt", 64)
            Assert.True(prefix.Success)
            Assert.True(prefix.IsTruncated)
            Assert.Equal(65536, prefix.ReadBytes)
            Assert.Equal(New String("A"c, 65535), prefix.Text)
        End Sub

        <Fact>
        Public Sub Invalid_bom_encoded_text_is_declined()
            Dim invalid As Byte() = {&HEF, &HBB, &HBF, &HFF}
            Dim result As TextReadResult = TextFileReader.ReadBytes(invalid, invalid.Length, ".txt")
            Assert.False(result.Success)
            Assert.True(result.IsBinary)
        End Sub

        <Fact>
        Public Sub Default_fallback_uses_system_ansi_codepage()
#If Not NETFRAMEWORK Then
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
#End If
            Dim ansi As Encoding = Encoding.GetEncoding(0)
            Dim input As String = "ANSI sample Текст é 日本語"
            Dim bytes As Byte() = ansi.GetBytes(input)
            Dim result As TextReadResult = TextFileReader.ReadBytes(bytes, bytes.Length, ".txt")
            Assert.True(result.Success)
            Assert.Equal(ansi.GetString(bytes), result.Text)
            Assert.Equal(ansi.CodePage, result.DetectedEncoding.CodePage)
        End Sub

    End Class
#End If
