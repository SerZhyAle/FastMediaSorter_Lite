Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

''' <summary>
''' The window that hands over an outsider link (ticket 170 rule 8): the FMSSFTP2 text, its QR code and a .fmscfg file of the one share.
''' Every one of them carries the credential of the server and the share id, so:
'''   * the code is shown nowhere on screen as text - it is copied only through the guarded clipboard door (CLIPBOARD-GUARD) and held in
'''     this window's memory only, dropped when it closes;
'''   * the .fmscfg goes through ShareConfigBuilder.WriteConfigFile (owner-only ACL, published by rename, verified);
'''   * the QR is shown by the existing zoom window, which says once that the picture carries access to the folders.
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line of the builder.
''' </summary>
Friend NotInheritable Class Exchange_Link_Form
    Inherits Form

    Private Const ContentWidth As Integer = 420

    Private ReadOnly _name As String
    Private _codeText As String
    Private _configJson As String
    Private _qrPng As Byte()
    Private _lblResult As Label
    Private _iconHandle As IntPtr

    Friend Sub New(name As String, codeText As String, configJson As String, qrPng As Byte())
        _name = name
        _codeText = If(codeText, "")
        _configJson = If(configJson, "")
        _qrPng = qrPng
        BuildUi()
    End Sub

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangePublishText.LinkWindowTitle(_name)
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                ' nothing secret outlives the window
                _codeText = ""
                _configJson = ""
                _qrPng = Nothing
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 8)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        body.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 0, 0, 6),
            .Text = ExchangePublishText.LinkWindowNote()})

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 2, 0, 2)}
        If _qrPng IsNot Nothing Then buttons.Controls.Add(NewButton(ExchangePublishText.ShowQrButton(), AddressOf OnShowQr, "action.show-code"))
        If _codeText.Length > 0 Then buttons.Controls.Add(NewButton(ExchangePublishText.CopyLinkTextButton(), AddressOf OnCopyText, "action.copy"))
        If _configJson.Length > 0 Then buttons.Controls.Add(NewButton(ExchangePublishText.SaveFileButton(), AddressOf OnSaveFile, "action.export"))
        body.Controls.Add(buttons)

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 4, 0, 4), .Visible = False}
        body.Controls.Add(_lblResult)

        Dim close As Button = NewButton(Localization.T("Закрыть"), Sub() Me.Close(), "nav.close", ButtonGlyphRole.Dismissing)
        close.DialogResult = DialogResult.OK
        Dim closeRow As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 6, 0, 2)}
        closeRow.Controls.Add(close)
        body.Controls.Add(closeRow)
        Me.AcceptButton = close
        Me.CancelButton = close

        Me.Controls.Add(body)
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
    End Sub

    Private Sub Say(text As String, ok As Boolean)
        _lblResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblResult.Text = text
        _lblResult.Visible = text.Length > 0
    End Sub

    Private Sub OnShowQr()
        If _qrPng Is Nothing Then
            Say(ExchangePublishText.NoQr(), False)
            Return
        End If
        Try
            Using ms As New MemoryStream(_qrPng)
                Using img As Image = Image.FromStream(ms)
                    Qr_Zoom_Form.ShowImage(Me, img, _name)
                End Using
            End Using
        Catch ex As ArgumentException
            Say(ExchangePublishText.NoQr(), False)
        End Try
    End Sub

    Private Sub OnCopyText()
        ' ONLY through the guarded door: the privacy markers keep it out of Win+V history and the cloud clipboard
        If SecureClipboard.SetSecret(_codeText) Then
            Say(ExchangePublishText.LinkTextCopied(), True)
        Else
            Say(Localization.T("Не удалось скопировать код."), False)
        End If
    End Sub

    Private Sub OnSaveFile()
        Using dlg As New SaveFileDialog With {.Filter = ExchangePublishText.SaveFilter(), .DefaultExt = "fmscfg", .AddExtension = True,
            .FileName = SafeFileName(_name), .OverwritePrompt = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                ShareConfigBuilder.WriteConfigFile(dlg.FileName, _configJson)
                Say(ExchangePublishText.FileSaved(dlg.FileName), True)
            Catch ex As Exception When TypeOf ex Is ConfigWriteRefusedException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                AppFileLogger.WriteLine("Exchange link: the .fmscfg could not be saved - " & ex.GetType().Name)
                Say(ExchangePublishText.FileNotSaved(), False)
            End Try
        End Using
    End Sub

    ''' <summary>A share name is a folder name, but a file name must not carry what Windows refuses.</summary>
    Private Shared Function SafeFileName(name As String) As String
        Dim sb As New System.Text.StringBuilder()
        For Each c As Char In If(name, "")
            If Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 Then sb.Append("_"c) Else sb.Append(c)
        Next
        Dim s As String = sb.ToString().Trim()
        Return If(s.Length = 0, "share", s)
    End Function

End Class
