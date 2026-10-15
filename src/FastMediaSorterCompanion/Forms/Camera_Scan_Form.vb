Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>
''' Camera QR code scanner window (spec 227 rule 2, T-3).
''' Captures live video from the PC's webcam via CameraCaptureSource,
''' decodes QR codes in real-time using QrCodeDecoder, and closes upon detecting a valid code.
''' </summary>
Friend NotInheritable Class Camera_Scan_Form
    Inherits Form
    Implements IThemeAware

    Private ReadOnly _previewBox As PictureBox
    Private ReadOnly _statusLabel As Label
    Private ReadOnly _cancelButton As Button
    Private _camera As CameraCaptureSource
    Private _lastScanTicks As Long
    Private _found As Boolean
    Private _stopping As Boolean
    Private _decodedText As String = ""

    Public ReadOnly Property DecodedText As String
        Get
            Return _decodedText
        End Get
    End Property

    Public Sub New()
        UiLanguage.ApplyTo(Me)
        Text = Localization.T("Сканирование QR-кода камерой")
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(540, 520)

        _previewBox = New PictureBox With {
            .Location = New Point(20, 20),
            .Size = New Size(500, 380),
            .SizeMode = PictureBoxSizeMode.Zoom,
            .BackColor = Color.Black,
            .BorderStyle = BorderStyle.FixedSingle
        }

        _statusLabel = New Label With {
            .Location = New Point(20, 415),
            .Size = New Size(500, 30),
            .Text = Localization.T("Наведите камеру на QR-код телефона"),
            .TextAlign = ContentAlignment.MiddleCenter,
            .Font = New Font(Font.FontFamily, 10.0F, FontStyle.Regular)
        }

        _cancelButton = New Button With {
            .Location = New Point(210, 460),
            .Size = New Size(120, 40),
            .Text = Localization.T("Отмена"),
            .DialogResult = DialogResult.Cancel
        }
        ButtonGlyphs.Apply(_cancelButton, "action.cancel")

        Controls.AddRange({_previewBox, _statusLabel, _cancelButton})
        CancelButton = _cancelButton

        DpiLayout.ApplyAutoScale(Me)
        ThemeService.Attach(Me)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        StartCamera()
    End Sub

    Private Sub StartCamera()
        Dim cameras = CameraCaptureSource.EnumerateCameras()
        If cameras Is Nothing OrElse cameras.Count = 0 Then
            _statusLabel.Text = Localization.T("Камера недоступна")
            Return
        End If

        _camera = New CameraCaptureSource(0, -1)
        Dim reason As String = ""
        If Not _camera.Start(AddressOf OnVideoFrame, reason) Then
            _statusLabel.Text = Localization.TF("{0}: {1}", Localization.T("Камера недоступна"), reason)
        End If
    End Sub

    Private Sub OnVideoFrame(frame As LiveVideoFrame)
        If _found OrElse _stopping Then Return
        If frame Is Nothing OrElse frame.Pixels Is Nothing OrElse frame.Width <= 0 OrElse frame.Height <= 0 Then Return

        Dim now As Long = Environment.TickCount64
        If now - _lastScanTicks < 150 Then Return
        _lastScanTicks = now

        Dim bmp As New Bitmap(frame.Width, frame.Height, Drawing.Imaging.PixelFormat.Format32bppRgb)
        Try
            Dim data = bmp.LockBits(New Rectangle(0, 0, frame.Width, frame.Height), Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppRgb)
            Marshal.Copy(frame.Pixels, 0, data.Scan0, Math.Min(frame.Pixels.Length, data.Stride * frame.Height))
            bmp.UnlockBits(data)

            ' Clone for UI display
            Dim displayBmp As Bitmap = DirectCast(bmp.Clone(), Bitmap)
            BeginInvoke(New Action(Sub()
                                       If IsDisposed OrElse _found Then
                                           displayBmp.Dispose()
                                           Return
                                       End If
                                       Dim old = _previewBox.Image
                                       _previewBox.Image = displayBmp
                                       old?.Dispose()
                                   End Sub))

            Dim qr As QrDecodeResult = QrCodeDecoder.DecodeBitmap(bmp)
            If qr.Succeeded Then
                _found = True
                _decodedText = qr.Text
                BeginInvoke(New Action(Sub()
                                           _statusLabel.Text = Localization.T("Код получен")
                                           DialogResult = DialogResult.OK
                                           Close()
                                       End Sub))
            End If
        Catch
        Finally
            bmp.Dispose()
        End Try
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        _stopping = True
        MyBase.OnFormClosed(e)
        If _camera IsNot Nothing Then
            Try
                _camera.Stop()
            Catch
            End Try
            _camera = Nothing
        End If
        If _previewBox.Image IsNot Nothing Then
            _previewBox.Image.Dispose()
            _previewBox.Image = Nothing
        End If
    End Sub

    Public ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return False
        End Get
    End Property

    Public Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        _statusLabel.ForeColor = palette.TextPrimary
    End Sub

End Class
