#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports ZXing
Imports ZXing.Windows.Compatibility

' Ticket 226 T-3 / 227 Q227-2: offline QR code decoding using ZXing.Net.
' Decodes images (screenshots from the clipboard, opened image files) in memory.
' Never writes images to disk and never logs decoded text (secret holder).
Friend NotInheritable Class ExchangeQrDecoder

    Private Sub New()
    End Sub

    ''' <summary>At most 4096 pixels on each side: an oversized image is refused before decoding.</summary>
    Public Const MaxDimension As Integer = 4096

    ''' <summary>True for known image extensions.</summary>
    Public Shared Function IsImageFile(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        Dim ext As String = IO.Path.GetExtension(path).ToLowerInvariant()
        Return ext = ".png" OrElse ext = ".jpg" OrElse ext = ".jpeg" OrElse ext = ".bmp" OrElse ext = ".webp" OrElse ext = ".gif"
    End Function

    ''' <summary>Decodes a QR code from a Bitmap. Returns True and the decoded text if found, False otherwise.</summary>
    Public Shared Function TryDecode(bitmap As Bitmap, ByRef text As String) As Boolean
        text = ""
        If bitmap Is Nothing Then Return False
        Try
            If bitmap.Width <= 0 OrElse bitmap.Height <= 0 OrElse bitmap.Width > MaxDimension OrElse bitmap.Height > MaxDimension Then
                Return False
            End If
            Dim reader As New ZXing.Windows.Compatibility.BarcodeReader() With {
                .AutoRotate = True
            }
            reader.Options.PossibleFormats = New List(Of BarcodeFormat) From {BarcodeFormat.QR_CODE}
            reader.Options.TryHarder = True
            Dim result As Result = reader.Decode(bitmap)
            If result IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(result.Text) Then
                text = result.Text.Trim()
                Return True
            End If
        Catch
            ' Decoded text or failure details are never logged (secret holder)
        End Try
        Return False
    End Function

    ''' <summary>Decodes a QR code from an image file path.</summary>
    Public Shared Function TryDecodeFile(path As String, ByRef text As String) As Boolean
        text = ""
        If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then Return False
        Try
            Using bmp As New Bitmap(path)
                Return TryDecode(bmp, text)
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Decodes a QR code from in-memory image bytes.</summary>
    Public Shared Function TryDecodeBytes(bytes As Byte(), ByRef text As String) As Boolean
        text = ""
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return False
        Try
            Using ms As New MemoryStream(bytes)
                Using bmp As New Bitmap(ms)
                    Return TryDecode(bmp, text)
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

End Class
#End If
