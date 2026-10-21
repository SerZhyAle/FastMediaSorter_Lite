Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' Dialog for "Manage in the browser" (ticket 245, Rule 13, DEVICE-EXCHANGE 1.4):
''' Shows leaf certificate fingerprint in hex beside the canonical browser-warning of certificate-trust 4,
''' the URL https://<x>/#admin with copy and open-in-browser buttons, and QR code representation.
''' </summary>
Friend NotInheritable Class Exchange_Admin_Browser_Form
    Inherits Form

    Private Const ContentWidth As Integer = 460

    Private ReadOnly _url As String
    Private ReadOnly _fingerprintHex As String
    Private _iconHandle As IntPtr
    Private _qrPng As Byte()
    Private _lblResult As Label
    Private _answerRow As DialogButtonRow

    Friend Sub New(url As String, fingerprintHex As String)
        _url = If(url, "")
        _fingerprintHex = If(fingerprintHex, "")
        If _url.Length > 0 Then
            _qrPng = ShareConfigBuilder.QrPngForText(_url)
        End If
        BuildUi()
    End Sub

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4),
            .Text = text
        }
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = Localization.T("Управление в браузере")
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                _qrPng = Nothing
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub

        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(14, 10, 14, 8)
        }
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        ' Canonical certificate-trust 4 warning
        Dim lblWarning As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .Margin = New Padding(0, 0, 0, 8),
            .Text = ExchangeAdminText.BrowserTrustNotice(_fingerprintHex)
        }
        ExchangeMark.Apply(lblWarning, 32, AddressOf CompanionAssets.OpenBundledAsset)
        body.Controls.Add(lblWarning)

        ' Fingerprint header and textbox
        Dim lblFp As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .Margin = New Padding(0, 4, 0, 2),
            .Text = Localization.T("Отпечаток сертификата сервера (SHA-256):")
        }

        Dim txtFp As New TextBox With {
            .Width = ContentWidth,
            .ReadOnly = True,
            .Text = _fingerprintHex,
            .Font = New Font("Consolas", 8.5F, FontStyle.Regular),
            .Margin = New Padding(0, 0, 0, 8)
        }
        ' no fingerprint (a publicly trusted certificate, or none made yet): no empty box and no label for it, the sentence on top says why
        If _fingerprintHex.Length > 0 Then
            body.Controls.Add(lblFp)
            body.Controls.Add(txtFp)
        Else
            lblFp.Dispose()
            txtFp.Dispose()
        End If

        ' URL header and textbox
        Dim lblUrl As New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .Margin = New Padding(0, 4, 0, 2),
            .Text = Localization.T("Ссылка на страницу администрирования:")
        }
        body.Controls.Add(lblUrl)

        Dim txtUrl As New TextBox With {
            .Width = ContentWidth,
            .ReadOnly = True,
            .Text = _url,
            .Margin = New Padding(0, 0, 0, 8)
        }
        body.Controls.Add(txtUrl)

        ' QR code and action buttons layout
        Dim midPanel As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False,
            .Margin = New Padding(0, 4, 0, 6)
        }

        If _qrPng IsNot Nothing Then
            Dim picQr As New PictureBox With {
                .Size = New Size(96, 96),
                .SizeMode = PictureBoxSizeMode.Zoom,
                .Cursor = Cursors.Hand,
                .Margin = New Padding(0, 0, 12, 0)
            }
            Try
                Using ms As New MemoryStream(_qrPng)
                    picQr.Image = Image.FromStream(ms)
                End Using
                AddHandler picQr.Click, Sub(s, e) OnShowQr()
                midPanel.Controls.Add(picQr)
            Catch
            End Try
        End If

        Dim actionButtons As New FlowLayoutPanel With {
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .Margin = New Padding(0)
        }

        Dim btnOpen As Button = NewButton(Localization.T("Открыть в браузере"), AddressOf OnOpenInBrowser, "action.browse")
        actionButtons.Controls.Add(btnOpen)

        Dim btnCopy As Button = NewButton(Localization.T("Копировать ссылку"), AddressOf OnCopyLink, "action.copy")
        actionButtons.Controls.Add(btnCopy)

        If _qrPng IsNot Nothing Then
            Dim btnQr As Button = NewButton(Localization.T("Показать QR-код"), AddressOf OnShowQr, "action.show-code")
            actionButtons.Controls.Add(btnQr)
        End If

        midPanel.Controls.Add(actionButtons)
        body.Controls.Add(midPanel)

        _lblResult = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(ContentWidth, 0),
            .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 4, 0, 4),
            .Visible = False
        }
        body.Controls.Add(_lblResult)

        ' Close is the window's one answer and rides the shared bottom row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4);
        ' Enter and Escape both reach it, as before. The three buttons beside the QR picture stay where they are: they
        ' are the commands of the link and its picture, not answers of the window.
        Dim close As Button = NewButton(Localization.T("Закрыть"), Sub() Me.Close(), "nav.close", ButtonGlyphRole.Dismissing)
        close.Name = "xabr_Close"
        close.DialogResult = DialogResult.OK
        _answerRow = New DialogButtonRow() With {.Dock = DockStyle.Bottom, .Name = "xabr_Buttons", .Padding = New Padding(14, 4, 14, 10)}
        DpiLayout.AddAnswer(_answerRow, close, DialogButtonRole.Dismissing, "nav.close")

        Me.Controls.Add(body)
        ' Last in the collection: the docked row takes its edge first and reads after the content in the Tab order.
        Me.Controls.Add(_answerRow)
        _answerRow.Bind(Me)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        DpiLayout.ApplyAutoScale(Me)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    Private Sub OnOpenInBrowser()
        If String.IsNullOrEmpty(_url) Then Return
        Try
            Process.Start(New ProcessStartInfo(_url) With {.UseShellExecute = True})
        Catch ex As Exception
            AppFileLogger.LogException("Exchange admin browser: opening URL", ex)
        End Try
    End Sub

    Private Sub OnCopyLink()
        If String.IsNullOrEmpty(_url) Then Return
        Dim data As New DataObject()
        data.SetText(_url, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then
            _lblResult.ForeColor = ThemeService.Palette.TextMuted
            _lblResult.Text = Localization.T("Ссылка скопирована в буфер обмена.")
            _lblResult.Visible = True
        Else
            _lblResult.ForeColor = CollapsibleSection.AttentionColor
            _lblResult.Text = Localization.T("Не удалось скопировать.")
            _lblResult.Visible = True
        End If
    End Sub

    Private Sub OnShowQr()
        If _qrPng Is Nothing Then Return
        Try
            Using ms As New MemoryStream(_qrPng)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Me, img, "exchange-admin", carriesAccess:=False)
                End Using
            End Using
        Catch ex As ArgumentException
        End Try
    End Sub

End Class
