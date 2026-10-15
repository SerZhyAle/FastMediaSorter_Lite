#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.IO

''' <summary>
''' QR code renderer helper for modern builds (spec 227 rule 3, T-4).
''' </summary>
Friend NotInheritable Class QrCodeRenderer

    Private Sub New()
    End Sub

    ''' <summary>Renders payload text to a PNG byte array.</summary>
    Friend Shared Function RenderPngBytes(payload As String, Optional pixelsPerModule As Integer = 8) As Byte()
        If String.IsNullOrEmpty(payload) Then Return Array.Empty(Of Byte)()
        Using gen As New QRCoder.QRCodeGenerator()
            Using data As QRCoder.QRCodeData = gen.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M)
                Dim png As New QRCoder.PngByteQRCode(data)
                Return png.GetGraphic(pixelsPerModule)
            End Using
        End Using
    End Function

    ''' <summary>Renders payload text directly to a Bitmap.</summary>
    Friend Shared Function RenderBitmap(payload As String, Optional pixelsPerModule As Integer = 8) As Bitmap
        Dim bytes As Byte() = RenderPngBytes(payload, pixelsPerModule)
        If bytes.Length = 0 Then Return Nothing
        Using ms As New MemoryStream(bytes)
            Return New Bitmap(ms)
        End Using
    End Function

End Class
#End If
