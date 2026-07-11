Option Strict On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' "QR-код крупно" - click on any QR PictureBox (Share tab LAN/Internet pages,
''' share wizard) opens the code in a separate window at 4x the source box size
''' (clamped to the screen working area), so a phone camera grabs it from
''' further away. The image is CLONED: async status polls keep pumping under the
''' modal loop and may rebuild/dispose the original while this window is open.
''' Dismiss with a click on the image, Esc/Enter or the close box.
''' </summary>
Public Class Qr_Zoom_Form
    Inherits Form

    Private ReadOnly _pic As PictureBox

    Private Sub New(img As Image, clientSide As Integer)
        Me.Text = If(Is_Russian_Language, "QR-код", "QR code")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.BackColor = Color.White
        Me.ClientSize = New Size(clientSide, clientSide)
        Me.KeyPreview = True

        _pic = New PictureBox With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom,
            .Image = img, .BackColor = Color.White, .Cursor = Cursors.Hand}
        AddHandler _pic.Click, Sub() Me.Close()
        Controls.Add(_pic)

        AddHandler Me.KeyDown, Sub(sender As Object, e As KeyEventArgs)
                                   If e.KeyCode = Keys.Escape OrElse e.KeyCode = Keys.Enter Then Me.Close()
                               End Sub
        AddHandler Me.FormClosed, Sub(sender As Object, e As FormClosedEventArgs)
                                      Dim old As Image = _pic.Image
                                      _pic.Image = Nothing
                                      If old IsNot Nothing Then old.Dispose()
                                  End Sub
    End Sub

    ''' <summary>Opens the QR shown in <paramref name="source"/> in a modal window
    ''' at 4x the box size, clamped to the screen. No-op when the box is empty.</summary>
    Public Shared Sub ShowZoomed(owner As Form, source As PictureBox)
        If source Is Nothing OrElse source.Image Is Nothing Then Return
        Dim img As Image
        Try
            img = New Bitmap(source.Image)
        Catch
            Return ' the image was disposed under us mid-click - just skip
        End Try

        Dim side As Integer = Math.Max(source.ClientSize.Width, source.ClientSize.Height) * 4
        Try
            Dim wa As Rectangle = Screen.FromControl(source).WorkingArea
            side = Math.Min(side, Math.Min(wa.Width, wa.Height) - 80)
        Catch
        End Try
        If side < 120 Then side = 120

        Using dlg As New Qr_Zoom_Form(img, side)
            dlg.ShowDialog(owner)
        End Using
    End Sub

End Class
