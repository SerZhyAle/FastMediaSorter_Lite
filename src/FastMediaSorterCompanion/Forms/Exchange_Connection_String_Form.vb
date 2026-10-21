Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' The operator's "Get connection string" window (ticket 187 T-2, proposal Ask 6): the one line a client pastes into "Add
''' from an exchange server..". It is built by the server binary (<c>fms-exchange-server connection-string</c>) from the
''' configured address and the certificate the server really presents - this window never assembles it, so what it shows is
''' what the command prints, byte for byte.
'''
''' It is NOT a secret and carries none (no password, token or pairing code), so it is shown as text and copied through the plain
''' clipboard door - not the guarded one - and its QR is an ordinary picture. Rules kept here:
'''   * a refusal is a sentence, never a half string;
'''   * the optional name and login are the operator's words for this string only; nothing is stored;
'''   * nothing is written to the log but the verb and the exit code (ExchangeProcess).
''' Hand-built, so it follows the Companion recipe: language direction first, DPI scaling as the LAST line of the builder.
''' </summary>
Friend NotInheritable Class Exchange_Connection_String_Form
    Inherits Form

    Private Const ContentWidth As Integer = 460
    Private Const DebounceMs As Integer = 350
    Private Const MaxNameChars As Integer = 64

    Private ReadOnly _make As Func(Of String, String, ExchangeProcess.ConnectionStringResult)
    Private ReadOnly _debounce As New Timer() With {.Interval = DebounceMs}
    Private _txtName As TextBox
    Private _txtLogin As TextBox
    Private _txtString As TextBox
    Private _btnCopy As Button
    Private _btnSave As Button
    Private _btnQr As Button
    Private _btnLink As Button
    Private ReadOnly _signInLink As Func(Of String, String)
    Private _lblResult As Label
    Private _body As FlowLayoutPanel
    Private _answerRow As DialogButtonRow
    Private _fittingRows As Boolean
    Private _iconHandle As IntPtr
    Private _sequence As Integer
    Private _text As String = ""

    ''' <param name="make">The command, injectable: name and login in, the binary's answer out. Runs on a pool thread.</param>
    ''' <param name="signInLink">The connection string in, the browser sign-in link out ("" = this server serves no web client, so no
    ''' link is offered). Nothing = the window has no such action (WEB-BINDING page-served-by-the-server 3, item AE).</param>
    Friend Sub New(Optional make As Func(Of String, String, ExchangeProcess.ConnectionStringResult) = Nothing,
                   Optional signInLink As Func(Of String, String) = Nothing)
        _make = If(make, AddressOf ExchangeProcess.ConnectionString)
        _signInLink = signInLink
        BuildUi()
    End Sub

    ''' <summary>The sign-in link of the string currently shown, or "" when there is none (no string yet, no web client, or no such action).</summary>
    Friend ReadOnly Property CurrentSignInLink As String
        Get
            If _signInLink Is Nothing OrElse _text.Length = 0 Then Return ""
            Return If(_signInLink(_text), "")
        End Get
    End Property

    ''' <summary>The string currently shown ("" while there is none): what Copy, Save and the QR hand over.</summary>
    Friend ReadOnly Property CurrentText As String
        Get
            Return _text
        End Get
    End Property

    Private Function NewButton(text As String, onClick As Action, Optional glyph As String = "", Optional role As ButtonGlyphRole = ButtonGlyphRole.Neutral) As Button
        Dim b As New Button With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(12, 4, 12, 4),
            .Margin = New Padding(0, 4, 8, 4), .Text = text}
        AddHandler b.Click, Sub() onClick()
        If glyph.Length > 0 Then ButtonGlyphs.Apply(b, glyph, role)
        Return b
    End Function

    Private Function Note(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 2, 0, 4), .Text = text}
    End Function

    Private Function Caption(text As String) As Label
        Return New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 8, 0, 2), .Text = text}
    End Function

    Private Sub BuildUi()
        UiLanguage.ApplyTo(Me)
        Me.Text = ExchangeConnectionStringText.WindowTitle()
        Me.Icon = ShareIcons.CreateIcon(_iconHandle)
        AddHandler Me.FormClosed,
            Sub()
                _debounce.Stop()
                _debounce.Dispose()
                _sequence += 1
                ShareIcons.FreeIcon(Me.Icon, _iconHandle)
            End Sub
        Me.FormBorderStyle = FormBorderStyle.Sizable
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent

        Dim body As New FlowLayoutPanel With {.Dock = DockStyle.Top, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Padding = New Padding(14, 10, 14, 6)}
        _body = body
        body.Controls.Add(ExchangeMark.Heading(Text, 32, AddressOf CompanionAssets.OpenBundledAsset))
        body.Controls.Add(Note(ExchangeConnectionStringText.Intro()))

        body.Controls.Add(Caption(ExchangeConnectionStringText.NameLabel()))
        _txtName = New TextBox With {.Width = ContentWidth, .MaxLength = MaxNameChars, .Margin = New Padding(0, 0, 0, 2),
            .AccessibleName = ExchangeConnectionStringText.NameLabel()}
        AddHandler _txtName.TextChanged, AddressOf OnInputChanged
        body.Controls.Add(_txtName)

        body.Controls.Add(Caption(ExchangeConnectionStringText.LoginLabel()))
        _txtLogin = New TextBox With {.Width = ContentWidth, .MaxLength = 64, .Margin = New Padding(0, 0, 0, 2),
            .AccessibleName = ExchangeConnectionStringText.LoginLabel()}
        AddHandler _txtLogin.TextChanged, AddressOf OnInputChanged
        body.Controls.Add(_txtLogin)
        body.Controls.Add(Note(ExchangeConnectionStringText.LoginNote()))

        body.Controls.Add(Caption(ExchangeConnectionStringText.StringLabel()))
        _txtString = New TextBox With {.ReadOnly = True, .Multiline = True, .AutoSize = False, .WordWrap = True, .ScrollBars = ScrollBars.Vertical,
            .Width = ContentWidth, .Height = 76, .Margin = New Padding(0, 0, 0, 4), .AccessibleName = ExchangeConnectionStringText.StringLabel()}
        body.Controls.Add(_txtString)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = True,
            .MaximumSize = New Size(ContentWidth, 0), .Margin = New Padding(0, 2, 0, 2)}
        _btnCopy = NewButton(ExchangeConnectionStringText.CopyButton(), AddressOf OnCopy, "action.copy")
        _btnSave = NewButton(ExchangeConnectionStringText.SaveButton(), AddressOf OnSave, "action.export")
        _btnQr = NewButton(ExchangePublishText.ShowQrButton(), AddressOf OnShowQr, "action.show-code")
        buttons.Controls.Add(_btnCopy)
        buttons.Controls.Add(_btnSave)
        buttons.Controls.Add(_btnQr)
        ' the sign-in link of the browser client: only where the caller supplies the builder, and only while there is a link to hand over
        _btnLink = NewButton(ExchangeConnectionStringText.CopySignInLinkButton(), AddressOf OnCopyLink, "action.copy")
        _btnLink.Visible = False
        buttons.Controls.Add(_btnLink)
        body.Controls.Add(buttons)

        _lblResult = New Label With {.AutoSize = True, .MaximumSize = New Size(ContentWidth, 0), .ForeColor = ThemeService.Palette.TextMuted,
            .Margin = New Padding(0, 4, 0, 4), .Visible = False}
        body.Controls.Add(_lblResult)

        ' Close is the window's one answer and rides the shared answer row (ticket 243 T-8b, BUTTONS 2.0 sections 3 and 4);
        ' Enter and Escape both reach it, as before. The four buttons above stay where they are: they are the commands of
        ' the string field they sit under, enabled and shown with it, not answers of the window.
        Dim close As Button = NewButton(Localization.T("Закрыть"), Sub() Me.Close(), "nav.close", ButtonGlyphRole.Dismissing)
        close.Name = "xcs_Close"
        close.DialogResult = DialogResult.OK
        _answerRow = New DialogButtonRow() With {.Name = "xcs_Buttons", .Margin = New Padding(0, 6, 0, 2)}
        DpiLayout.AddAnswer(_answerRow, close, DialogButtonRole.Dismissing, "nav.close")
        body.Controls.Add(_answerRow)
        _answerRow.Bind(Me)

        Me.Controls.Add(body)
        Me.AutoScroll = True
        Me.ClientSize = body.PreferredSize
        AddHandler body.Layout, Sub(sender, e) FitRowsToWidth()
        AddHandler body.SizeChanged, Sub() Me.AutoScrollMinSize = New Size(0, body.Height)

        AddHandler _debounce.Tick, AddressOf OnDebounce
        SetActionsEnabled(False)

        DpiLayout.ApplyAutoScale(Me)   ' last, once every child exists - see DpiLayout
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Measure only after DPI scaling and the final row widths have settled.
        FitRowsToWidth()
        _body.PerformLayout()
        Me.ClientSize = New Size(Me.ClientSize.Width, _body.GetPreferredSize(New Size(_body.ClientSize.Width, 0)).Height)
        Me.MinimumSize = Me.SizeFromClientSize(New Size(Me.LogicalToDeviceUnits(280), Me.LogicalToDeviceUnits(200)))
        DpiLayout.ClampToWorkingArea(Me)
        DpiLayout.CenterOnOwner(Me)
        Dim t As Task = RegenerateAsync()
    End Sub

    Private Sub FitRowsToWidth()
        If _fittingRows Then Return
        _fittingRows = True
        _body.SuspendLayout()
        Try
            Dim width As Integer = Math.Max(1, _body.ClientSize.Width - _body.Padding.Horizontal)
            For Each row As Control In _body.Controls
                Dim available As Integer = Math.Max(1, width - row.Margin.Horizontal)
                Dim minimumHeight As Integer = If(row Is _txtString, Me.LogicalToDeviceUnits(76), 0)
                row.MaximumSize = New Size(available, minimumHeight)
                row.MinimumSize = New Size(available, minimumHeight)
                row.Width = available
                If row Is _txtString Then row.Height = minimumHeight
                ' The answer row sizes its own height from the width it is given (a longer caption wraps whole buttons).
                If row Is _answerRow Then row.Height = _answerRow.GetPreferredSize(New Size(available, 0)).Height
            Next
        Finally
            _body.ResumeLayout(True)
            _fittingRows = False
        End Try
    End Sub

    Private Sub SetActionsEnabled(enabled As Boolean)
        _btnCopy.Enabled = enabled
        _btnSave.Enabled = enabled
        _btnQr.Enabled = enabled
        _btnLink.Enabled = enabled
        _btnLink.Visible = enabled AndAlso CurrentSignInLink.Length > 0
    End Sub

    Private Sub OnInputChanged(sender As Object, e As EventArgs)
        _debounce.Stop()
        _debounce.Start()
    End Sub

    Private Sub OnDebounce(sender As Object, e As EventArgs)
        _debounce.Stop()
        Dim t As Task = RegenerateAsync()
    End Sub

    ''' <summary>Asks the binary for the string of the current name and login. A later request supersedes an earlier one: an
    ''' answer that arrives for an older input is dropped.</summary>
    Private Async Function RegenerateAsync() As Task
        _sequence += 1
        Dim mine As Integer = _sequence
        Dim name As String = _txtName.Text
        Dim login As String = _txtLogin.Text
        Dim r As ExchangeProcess.ConnectionStringResult
        Try
            r = Await Task.Run(Function() _make(name, login))
        Catch ex As Exception When TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException
            AppFileLogger.LogException("Exchange: the connection string", ex)
            r = New ExchangeProcess.ConnectionStringResult()
        End Try
        If IsDisposed OrElse Disposing OrElse mine <> _sequence Then Return
        If r.Outcome = ExchangeProcess.ConnectionStringOutcome.Made Then
            _text = r.Text
            _txtString.Text = r.Text
            SetActionsEnabled(True)
            Say("", True)
        Else
            _text = ""
            _txtString.Text = ""
            SetActionsEnabled(False)
            Say(ExchangeConnectionStringText.ForOutcome(r.Outcome), False)
        End If
    End Function

    Private Sub Say(text As String, ok As Boolean)
        _lblResult.ForeColor = If(ok, ThemeService.Palette.TextMuted, CollapsibleSection.AttentionColor)
        _lblResult.Text = text
        _lblResult.Visible = text.Length > 0
    End Sub

    Private Sub OnCopy()
        If _text.Length = 0 Then Return
        ' Not a secret: a plain copy through the one clipboard door (no privacy markers needed).
        Dim data As New DataObject()
        data.SetText(_text, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then
            Say(ExchangeConnectionStringText.Copied(), True)
        Else
            Say(ExchangeText.CopyFailed(), False)
        End If
    End Sub

    Private Sub OnCopyLink()
        Dim link As String = CurrentSignInLink
        If link.Length = 0 Then Return
        ' The string this window builds is secret-free (name, login, fingerprint; never a password or an invitation), and so is the link
        ' that carries it after the #: a plain copy through the one clipboard door, as OnCopy does.
        Dim data As New DataObject()
        data.SetText(link, TextDataFormat.UnicodeText)
        If SecureClipboard.TrySetDataObject(data) Then
            Say(Localization.T("Ссылка скопирована в буфер обмена."), True)
        Else
            Say(ExchangeText.CopyFailed(), False)
        End If
    End Sub

    Private Sub OnSave()
        If _text.Length = 0 Then Return
        Using dlg As New SaveFileDialog With {.Filter = ExchangeConnectionStringText.SaveFilter(), .DefaultExt = "fmsexch", .AddExtension = True,
            .FileName = "exchange-server.fmsexch", .OverwritePrompt = True}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                ' UTF-8 without a byte-order mark, the text and nothing else (the contract's file form)
                File.WriteAllBytes(dlg.FileName, New UTF8Encoding(False).GetBytes(_text))
                Say(ExchangePublishText.FileSaved(dlg.FileName), True)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                AppFileLogger.WriteLine("Exchange: the connection string file could not be saved - " & ex.GetType().Name)
                Say(ExchangePublishText.FileNotSaved(), False)
            End Try
        End Using
    End Sub

    Private Sub OnShowQr()
        If _text.Length = 0 Then Return
        Dim png As Byte() = ShareConfigBuilder.QrPngForText(_text)
        If png Is Nothing Then
            Say(ExchangePublishText.NoQr(), False)
            Return
        End If
        Try
            Using ms As New MemoryStream(png)
                Using img As Image = Image.FromStream(ms)
                    ' the same payload as the text, byte for byte; the picture names a server and grants nothing
                    Qr_Zoom_Form.ShowImage(Me, img, "exchange-server", carriesAccess:=False)
                End Using
            End Using
        Catch ex As ArgumentException
            Say(ExchangePublishText.NoQr(), False)
        End Try
    End Sub

End Class
