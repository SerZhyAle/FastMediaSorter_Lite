#If Not NETFRAMEWORK Or DOCUMENT_PREVIEW_TESTS Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text

Public Structure TextReadResult
        Public Success As Boolean
        Public IsBinary As Boolean
        Public Text As String
        Public Lines As String()
        Public TotalBytes As Long
        Public ReadBytes As Long
        Public IsTruncated As Boolean
        Public DetectedEncoding As Encoding
        Public IsMonospace As Boolean
        Public ErrorMessage As String
    End Structure

    Public Module TextFileReader

        Private ReadOnly MonospaceExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".log", ".csv", ".json", ".xml", ".tsv", ".yaml", ".yml", ".toml", ".ini", ".conf", ".sql", ".sh", ".bat", ".cmd", ".ps1",
            ".cs", ".vb", ".cpp", ".h", ".c", ".py", ".js", ".css"
        }

        ' Source files are shown as text and never executed; .ts stays a video (MEDIA-CLASSIFICATION rule 1).
        Private ReadOnly TextExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".txt", ".md", ".log", ".csv", ".json", ".xml", ".tsv", ".yaml", ".yml", ".toml", ".ini", ".conf", ".html", ".htm", ".sql", ".sh", ".bat", ".cmd", ".ps1",
            ".cs", ".vb", ".cpp", ".h", ".c", ".py", ".js", ".css"
        }

        Private Function ExtensionOf(value As String) As String
            If String.IsNullOrEmpty(value) Then Return ""
            If value.StartsWith(".", StringComparison.Ordinal) AndAlso value.IndexOfAny(New Char() {"/"c, "\"c}) < 0 Then Return value
            Return Path.GetExtension(value)
        End Function

        Public Function IsMonospace(extensionOrPath As String) As Boolean
            Dim ext As String = ExtensionOf(extensionOrPath)
            Return MonospaceExtensions.Contains(ext)
        End Function

        Public Function IsTextExtension(extensionOrPath As String) As Boolean
            Dim ext As String = ExtensionOf(extensionOrPath)
            Return TextExtensions.Contains(ext)
        End Function

        Public Function Read(filePath As String, Optional maxKb As Integer = 2048) As TextReadResult
            Dim result As New TextReadResult()
            If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then
                result.Success = False
                result.ErrorMessage = Localization.T("Файл не найден")
                Return result
            End If

            Try
                Dim fileInfo As New FileInfo(filePath)
                Dim totalBytes As Long = fileInfo.Length
                Dim maxBytes As Long = Math.Min(65536L, Math.Max(1L, CLng(maxKb))) * 1024L
                Dim bytesToRead As Integer = CInt(Math.Min(totalBytes, maxBytes))

                Dim buffer(bytesToRead - 1) As Byte
                Using fs As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Dim totalRead As Integer = 0
                    While totalRead < bytesToRead
                        Dim readCount As Integer = fs.Read(buffer, totalRead, bytesToRead - totalRead)
                        If readCount <= 0 Then Exit While
                        totalRead += readCount
                    End While
                    If totalRead < bytesToRead Then
                        Array.Resize(buffer, totalRead)
                    End If
                End Using

                Return ReadBytes(buffer, totalBytes, Path.GetExtension(filePath), maxKb)
            Catch ex As Exception
                result.Success = False
                result.ErrorMessage = FailureText.Describe(ex)
                Return result
            End Try
        End Function

        Public Function Read(stream As Stream, totalBytes As Long, extension As String, Optional maxKb As Integer = 2048) As TextReadResult
            Dim maxBytes As Long = Math.Min(65536L, Math.Max(1L, CLng(maxKb))) * 1024L
            Dim bytesToRead As Integer = CInt(Math.Min(totalBytes, maxBytes))
            Dim buffer(bytesToRead - 1) As Byte
            Dim totalRead As Integer = 0
            While totalRead < bytesToRead
                Dim readCount As Integer = stream.Read(buffer, totalRead, bytesToRead - totalRead)
                If readCount <= 0 Then Exit While
                totalRead += readCount
            End While
            If totalRead < bytesToRead Then
                Array.Resize(buffer, totalRead)
            End If
            Return ReadBytes(buffer, totalBytes, extension, maxKb)
        End Function

        Public Function ReadBytes(bytes As Byte(), totalBytes As Long, extension As String, Optional maxKb As Integer = 2048,
                                  Optional ansiCodePage As Integer = 0) As TextReadResult
            Dim ceiling As Integer = CInt(Math.Min(65536L, Math.Max(1L, CLng(maxKb))) * 1024L)
            If bytes IsNot Nothing AndAlso bytes.Length > ceiling Then
                Array.Resize(bytes, ceiling)
            End If
            Dim result As New TextReadResult With {
                .TotalBytes = totalBytes,
                .ReadBytes = If(bytes IsNot Nothing, bytes.Length, 0),
                .IsTruncated = totalBytes > If(bytes IsNot Nothing, bytes.Length, 0),
                .IsMonospace = IsMonospace(extension)
            }

            If bytes Is Nothing OrElse bytes.Length = 0 Then
                result.Success = True
                result.IsBinary = False
                result.Text = String.Empty
                result.Lines = Array.Empty(Of String)()
                result.DetectedEncoding = Encoding.UTF8
                Return result
            End If

            Dim isBinary As Boolean = False
            Dim enc As Encoding = DetectEncoding(bytes, bytes.Length, isBinary, result.IsTruncated, ansiCodePage)

            If isBinary Then
                result.Success = False
                result.IsBinary = True
                result.ErrorMessage = "Binary file"
                Return result
            End If

            result.DetectedEncoding = enc
            result.IsBinary = False

            ' Decode string
            Dim text As String
            Try
                ' Strip BOM preamble if present in bytes
                Dim preamble As Byte() = enc.GetPreamble()
                Dim startOffset As Integer = 0
                If preamble IsNot Nothing AndAlso preamble.Length > 0 AndAlso bytes.Length >= preamble.Length Then
                    Dim matches As Boolean = True
                    For i As Integer = 0 To preamble.Length - 1
                        If bytes(i) <> preamble(i) Then
                            matches = False
                            Exit For
                        End If
                    Next
                    If matches Then startOffset = preamble.Length
                End If

                Dim decoder As Decoder = enc.GetDecoder()
                Dim chars(enc.GetMaxCharCount(bytes.Length - startOffset) - 1) As Char
                Dim count As Integer = decoder.GetChars(bytes, startOffset, bytes.Length - startOffset, chars, 0, Not result.IsTruncated)
                text = New String(chars, 0, count)
            Catch ex As Exception
                result.Success = False
                result.IsBinary = True
                result.ErrorMessage = FailureText.Describe(ex)
                Return result
            End Try

            ' Expand tabs to 4 spaces as specified in §2.1
            text = text.Replace(vbTab, "    ")
            result.Text = text

            ' Line splitting
            result.Lines = text.Split(New String() {vbCrLf, vbLf, vbCr}, StringSplitOptions.None)
            result.Success = True
            Return result
        End Function

        ' Specification 277 section 6.1: the tiers live in TextEncodingDetector; this signature stays for
        ' the preview, the file details panel and the tests that call it.
        Public Function DetectEncoding(bytes As Byte(), readLength As Integer, ByRef isBinary As Boolean, Optional isTruncated As Boolean = False, Optional ansiCodePage As Integer = 0) As Encoding
            Dim detection As EncodingDetectionResult = TextEncodingDetector.Detect(bytes, readLength, isTruncated, ansiCodePage)
            isBinary = detection.IsBinary
            Return detection.Encoding
        End Function

    End Module
#End If
