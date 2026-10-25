#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports ZXing
Imports ZXing.Windows.Compatibility

''' <summary>
''' Outcome of attempting to find and decode a QR code from an image or clipboard.
''' Spec 227 (PHONE_CONNECTION_CODE_AND_QR_INTAKE) rules 1, 6, 8.
''' </summary>
Friend Enum QrDecodeOutcome
    Success = 0
    NoQr = 1
    MultipleQrs = 2
    TooLarge = 3
    Failed = 4
End Enum

''' <summary>
''' Immutable result of QR code decoding.
''' The text of a decoded code is never logged (spec 227 rule 6: secret holder).
''' </summary>
Friend NotInheritable Class QrDecodeResult
    Public ReadOnly Property Outcome As QrDecodeOutcome
    Public ReadOnly Property Text As String
    Public ReadOnly Property FailureMessage As String

    Friend Sub New(outcome As QrDecodeOutcome, text As String, failureMessage As String)
        Me.Outcome = outcome
        Me.Text = If(text, "")
        Me.FailureMessage = If(failureMessage, "")
    End Sub

    Public ReadOnly Property Succeeded As Boolean
        Get
            Return Outcome = QrDecodeOutcome.Success AndAlso Not String.IsNullOrEmpty(Text)
        End Get
    End Property
End Class

''' <summary>
''' The QR code decoder seam for modern .NET 10 builds.
''' Spec 227 T-1: reads still images, screenshots, and clipboard images via ZXing.Net.
''' Bounded in image pixel dimension and memory size; offline; x86 gets nothing.
''' </summary>
Friend NotInheritable Class QrCodeDecoder

    Private Sub New()
    End Sub

    ''' <summary>Max pixel count (e.g. 4000x4000 = 16 MP) before refusing as TooLarge.</summary>
    Friend Const MaxPixelCount As Long = 16000000L

    ''' <summary>Max file size (10 MB) before refusing as TooLarge.</summary>
    Friend Const MaxFileBytes As Long = 10L * 1024L * 1024L

    ''' <summary>Decodes a QR code from a <see cref="Bitmap"/>. Handles single QR, multiple QRs, and rotation.</summary>
    Friend Shared Function DecodeBitmap(bitmap As Bitmap, Optional maxPixels As Long = MaxPixelCount) As QrDecodeResult
        If bitmap Is Nothing Then
            Return New QrDecodeResult(QrDecodeOutcome.NoQr, "", Localization.T("В изображении не найден QR-код"))
        End If

        Dim pixelCount As Long = CLng(bitmap.Width) * CLng(bitmap.Height)
        If pixelCount > maxPixels Then
            Return New QrDecodeResult(QrDecodeOutcome.TooLarge, "", Localization.T("Изображение слишком велико для поиска QR-кода"))
        End If

        Try
            Dim reader As New BarcodeReader() With {
                .AutoRotate = True
            }
            reader.Options.PossibleFormats = New List(Of BarcodeFormat) From {BarcodeFormat.QR_CODE}
            reader.Options.TryHarder = True
            reader.Options.TryInverted = True

            Dim results As Result() = reader.DecodeMultiple(bitmap)
            If results IsNot Nothing AndAlso results.Length > 1 Then
                Return New QrDecodeResult(QrDecodeOutcome.MultipleQrs, "", Localization.T("В изображении найдено несколько QR-кодов"))
            End If

            If results IsNot Nothing AndAlso results.Length = 1 AndAlso Not String.IsNullOrEmpty(results(0).Text) Then
                Return New QrDecodeResult(QrDecodeOutcome.Success, results(0).Text, "")
            End If

            ' Fallback to single decode in case DecodeMultiple missed an edge case
            Dim singleResult As Result = reader.Decode(bitmap)
            If singleResult IsNot Nothing AndAlso Not String.IsNullOrEmpty(singleResult.Text) Then
                Return New QrDecodeResult(QrDecodeOutcome.Success, singleResult.Text, "")
            End If

            Return New QrDecodeResult(QrDecodeOutcome.NoQr, "", Localization.T("В изображении не найден QR-код"))
        Catch ex As Exception
            Return New QrDecodeResult(QrDecodeOutcome.Failed, "", Localization.T("Не удалось распознать QR-код"))
        End Try
    End Function

    ''' <summary>Decodes a QR code from in-memory image bytes (PNG, JPEG, BMP, WEBP, etc.).</summary>
    Friend Shared Function DecodeBytes(bytes As Byte()) As QrDecodeResult
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Return New QrDecodeResult(QrDecodeOutcome.NoQr, "", Localization.T("В изображении не найден QR-код"))
        End If

        If bytes.Length > MaxFileBytes Then
            Return New QrDecodeResult(QrDecodeOutcome.TooLarge, "", Localization.T("Изображение слишком велико для поиска QR-кода"))
        End If

        Try
            Using ms As New MemoryStream(bytes)
                Using bmp As New Bitmap(ms)
                    Return DecodeBitmap(bmp)
                End Using
            End Using
        Catch ex As Exception
            ' GDI+ has no WEBP codec of its own (a phone screenshot is often WEBP): the media
            ' pipeline's decoder (Magick.NET, through the same read gate) re-encodes to PNG in
            ' memory and the decode is tried again - the image never touches the disk (rule 6).
            ' The header is read first: a small file can declare a gigantic canvas, and the
            ' decoder would allocate all of it before the pixel bound of DecodeBitmap ever saw a
            ' bitmap. A container the read gate does not know is not decoded at all.
            If ExceedsPixelBound(bytes) Then
                Return New QrDecodeResult(QrDecodeOutcome.TooLarge, "", Localization.T("Изображение слишком велико для поиска QR-кода"))
            End If
            Dim bmp As Bitmap = DecodeViaMagick(bytes)
            If bmp IsNot Nothing Then
                Try
                    Return DecodeBitmap(bmp)
                Finally
                    bmp.Dispose()
                End Try
            End If
            Return New QrDecodeResult(QrDecodeOutcome.Failed, "", Localization.T("Не удалось прочитать файл изображения"))
        End Try
    End Function

    ''' <summary>True when the declared canvas of the image (read from its header only, nothing allocated)
    ''' is over <see cref="MaxPixelCount"/>. An unreadable header answers False: the decode then fails by itself.</summary>
    Friend Shared Function ExceedsPixelBound(bytes As Byte()) As Boolean
        Try
            Using ms As New MemoryStream(bytes, writable:=False)
                Dim container As ImageContainer = MagickReadGate.Sniff(ms)
                If Not IsFallbackContainer(container) Then Return False
                Dim headers As List(Of ImageMagick.IMagickImageInfo) = MagickReadGate.PingAll(ms, container)
                Return headers.Count > 0 AndAlso CLng(headers(0).Width) * CLng(headers(0).Height) > MaxPixelCount
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' The containers the fallback reads: the nine the managed decoder it replaced recognised
    ''' (specification 270 R1). AVIF/HEIF and JPEG XL were never read here and are not now -
    ''' a change of format reach is a decision, not a side effect of a backend swap.
    ''' </summary>
    Private Shared Function IsFallbackContainer(container As ImageContainer) As Boolean
        Select Case container
            Case ImageContainer.Unknown, ImageContainer.Avif, ImageContainer.Heif, ImageContainer.Jxl
                Return False
            Case Else
                Return True
        End Select
    End Function

    ''' <summary>Any-format image bytes to a Bitmap through Magick.NET and a memory PNG, or Nothing.
    ''' Only the fallback for what GDI+ cannot read; never a second decoder for what it can. The
    ''' first frame of an animation is all a QR search needs.</summary>
    Private Shared Function DecodeViaMagick(bytes As Byte()) As Bitmap
        Try
            Using ms As New MemoryStream(bytes, writable:=False)
                Dim container As ImageContainer = MagickReadGate.Sniff(ms)
                If Not IsFallbackContainer(container) Then Return Nothing
                Using image As ImageMagick.MagickImage = MagickReadGate.ReadFirstFrame(ms, container)
                    Using png As New MemoryStream(MagickReadGate.ToPngBytes(image))
                        Using decoded As New Bitmap(png)
                            Return New Bitmap(decoded)
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>Decodes a QR code from an image file path.</summary>
    Friend Shared Function DecodeFile(path As String) As QrDecodeResult
        Try
            Dim info As New FileInfo(path)
            If Not info.Exists Then
                Return New QrDecodeResult(QrDecodeOutcome.Failed, "", Localization.T("Файл не найден"))
            End If
            If info.Length > MaxFileBytes Then
                Return New QrDecodeResult(QrDecodeOutcome.TooLarge, "", Localization.T("Изображение слишком велико для поиска QR-кода"))
            End If
            Dim bytes As Byte() = File.ReadAllBytes(path)
            Return DecodeBytes(bytes)
        Catch ex As Exception
            Return New QrDecodeResult(QrDecodeOutcome.Failed, "", Localization.T("Не удалось прочитать файл изображения"))
        End Try
    End Function

    ''' <summary>
    ''' Reads clipboard content according to CLIPBOARD-GUARD (bounded retries, UI thread).
    ''' If clipboard contains an image (e.g. Win+Shift+S screenshot), decodes QR code from it.
    ''' If clipboard contains plain text, returns text directly as Success.
    ''' </summary>
    Friend Shared Function DecodeClipboard() As QrDecodeResult
        Dim containsImage As Boolean = False
        Dim containsText As Boolean = False
        Dim imageObj As Image = Nothing
        Dim textContent As String = Nothing

        Dim success As Boolean = False
        For attempt As Integer = 1 To 3
            Try
                containsImage = Clipboard.ContainsImage()
                containsText = Clipboard.ContainsText()
                If containsImage Then
                    imageObj = Clipboard.GetImage()
                ElseIf containsText Then
                    textContent = Clipboard.GetText()
                End If
                success = True
                Exit For
            Catch ex As ExternalException
                If attempt < 3 Then Thread.Sleep(120)
            Catch ex As Exception
                Exit For
            End Try
        Next

        If Not success Then
            Return New QrDecodeResult(QrDecodeOutcome.Failed, "", Localization.T("Буфер обмена занят другой программой"))
        End If

        If containsImage AndAlso imageObj IsNot Nothing Then
            Try
                Using bmp As New Bitmap(imageObj)
                    Return DecodeBitmap(bmp)
                End Using
            Finally
                imageObj.Dispose()
            End Try
        End If

        If containsText AndAlso Not String.IsNullOrWhiteSpace(textContent) Then
            Return New QrDecodeResult(QrDecodeOutcome.Success, textContent.Trim(), "")
        End If

        Return New QrDecodeResult(QrDecodeOutcome.NoQr, "", Localization.T("В буфере обмена нет текста или изображения"))
    End Function

End Class
#End If
