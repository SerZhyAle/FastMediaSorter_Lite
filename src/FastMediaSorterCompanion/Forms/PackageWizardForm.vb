Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Companion package wizard - LEVEL 2 of the two-wizard model (§4.5): a ONE-SHOT act
''' of giving a specific recipient access. The user picks a SUBSET of the served shares
''' and sets the PER-RECIPIENT parameters for THIS access code (PIN, slideshow interval,
''' publish-as-read-only), optionally restricts to LAN / drops the password, and gets a
''' QR + savable/mailable .fmscfg. Nothing here is persisted (§4.5.3); the overrides ride
''' only in this export, never touching the share's own defaults. Built with layout
''' panels (AutoSize) so it scales at any display scaling.
''' </summary>
Public NotInheritable Class PackageWizardForm
    Inherits Form

    Private ReadOnly _preselect As List(Of String)
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _config As ShareConfigResult
    Private _loading As Boolean

    Private clbShares As CheckedListBox
    Private chkLanOnly As CheckBox
    Private chkNoPassword As CheckBox
    Private chkPin As CheckBox
    Private txtPin As TextBox
    Private chkSlide As CheckBox
    Private numSlide As NumericUpDown
    Private chkSoftRo As CheckBox
    Private picQr As PictureBox
    Private btnShowQr As Button
    Private lblAddr As Label
    Private lblFinger As Label
    Private btnCopyLogin As Button
    Private btnSave As Button
    Private btnEmail As Button
    Private btnClose As Button
    Private lblHint As Label
    Private toolTip As ToolTip

    Public Sub New(preselect As List(Of String))
        _preselect = If(preselect, New List(Of String)())
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    Private Shared ReadOnly Property Rus As Boolean
        Get
            Return Is_Russian_Language
        End Get
    End Property

    Private Sub BuildUi()
        Me.Text = If(Rus, "Поделиться - код доступа", "Share - access code")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.AutoScaleMode = AutoScaleMode.Font
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        toolTip = New ToolTip()

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .ColumnCount = 2, .Padding = New Padding(16, 14, 16, 12)}
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        ' --- left column: which shares + per-recipient parameters --------------
        Dim left As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0, 0, 24, 0)}
        left.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 0, 0, 4), .Text = If(Rus, "Папки в этом коде доступа:", "Folders in this access code:")})
        clbShares = New CheckedListBox With {.Width = 340, .Height = 170, .CheckOnClick = True, .IntegralHeight = False, .Margin = New Padding(0, 0, 0, 12)}
        AddHandler clbShares.ItemCheck, AddressOf OnSharesItemCheck
        left.Controls.Add(clbShares)

        left.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 4), .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Параметры этого кода доступа:", "Parameters for this access code:")})

        chkLanOnly = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Только локальная сеть (без адреса из интернета)", "LAN only (no internet address)")}
        chkNoPassword = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Не включать пароль в файл/QR", "Do not include the password in the file/QR")}
        AddHandler chkLanOnly.CheckedChanged, AddressOf OnRebuildToggle
        AddHandler chkNoPassword.CheckedChanged, AddressOf OnRebuildToggle
        toolTip.SetToolTip(chkNoPassword, If(Rus, "Пароль не попадёт в файл/QR - телефон запросит его при импорте; передайте пароль отдельно.",
                                                 "The password stays out of the file/QR - the phone asks for it at import; pass it separately."))
        left.Controls.AddRange(New Control() {chkLanOnly, chkNoPassword})

        ' Per-recipient overrides (leave off = each share's own defaults).
        Dim pinRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 4, 0, 0)}
        chkPin = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 4, 6, 0), .Text = If(Rus, "Задать PIN для этого доступа:", "Set a PIN for this access:")}
        txtPin = New TextBox With {.Width = 120, .Enabled = False}
        AddHandler chkPin.CheckedChanged, AddressOf OnPinToggle
        AddHandler txtPin.TextChanged, AddressOf OnRebuildToggle
        pinRow.Controls.Add(chkPin) : pinRow.Controls.Add(txtPin)

        Dim slideRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 2, 0, 0)}
        chkSlide = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 4, 6, 0), .Text = If(Rus, "Свой интервал слайд-шоу:", "Custom slideshow interval:")}
        numSlide = New NumericUpDown With {.Width = 70, .Minimum = 1, .Maximum = 3600, .Value = 10, .Enabled = False}
        AddHandler chkSlide.CheckedChanged, AddressOf OnSlideToggle
        AddHandler numSlide.ValueChanged, AddressOf OnRebuildToggle
        slideRow.Controls.Add(chkSlide) : slideRow.Controls.Add(numSlide)
        slideRow.Controls.Add(New Label With {.AutoSize = True, .Margin = New Padding(6, 5, 0, 0), .ForeColor = Color.DimGray, .Text = If(Rus, "сек", "sec")})

        chkSoftRo = New CheckBox With {.AutoSize = True, .Margin = New Padding(0, 4, 0, 0), .Text = If(Rus, "Опубликовать как «только чтение» (для этого получателя)", "Publish as read-only (for this recipient)")}
        AddHandler chkSoftRo.CheckedChanged, AddressOf OnRebuildToggle
        toolTip.SetToolTip(chkSoftRo, If(Rus, "Телефону этого получателя ресурсы покажутся только для чтения (подсказка приложению). Настоящий запрет задаётся у самой папки.",
                                              "This recipient's phone shows the resources read-only (a hint). A real lock is set on the folder itself."))
        left.Controls.AddRange(New Control() {pinRow, slideRow, chkSoftRo})

        ' --- right column: QR + export -----------------------------------------
        Dim right As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = New Padding(0)}
        btnShowQr = New Button With {.Width = 250, .Height = 210, .Font = New Font(Me.Font, FontStyle.Bold), .Margin = New Padding(0, 0, 0, 6),
            .Text = If(Rus, "Показать QR-код", "Show QR code"), .Enabled = False}
        AddHandler btnShowQr.Click, Sub() Qr_Zoom_Form.ShowZoomed(Me, picQr)
        right.Controls.Add(btnShowQr)
        lblAddr = New Label With {.AutoSize = True, .MaximumSize = New Size(250, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 0, 0, 2)}
        lblFinger = New Label With {.AutoSize = True, .MaximumSize = New Size(250, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 0, 0, 8)}
        right.Controls.AddRange(New Control() {lblAddr, lblFinger})
        btnCopyLogin = New Button With {.Width = 250, .Height = 30, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Скопировать логин/пароль", "Copy login/password"), .Enabled = False}
        btnSave = New Button With {.Width = 250, .Height = 30, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Сохранить файл .fmscfg..", "Save .fmscfg file.."), .Enabled = False}
        btnEmail = New Button With {.Width = 250, .Height = 30, .Margin = New Padding(0, 2, 0, 2), .Text = If(Rus, "Отправить по почте..", "Send by email.."), .Enabled = False}
        AddHandler btnCopyLogin.Click, AddressOf OnCopyLogin
        AddHandler btnSave.Click, AddressOf OnSaveConfig
        AddHandler btnEmail.Click, AddressOf OnEmail
        right.Controls.AddRange(New Control() {btnCopyLogin, btnSave, btnEmail})

        ' Hidden holder for the QR image (source for the zoom window).
        picQr = New PictureBox With {.Width = 250, .Height = 210, .Visible = False, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}

        root.Controls.Add(left, 0, 0)
        root.Controls.Add(right, 1, 0)

        lblHint = New Label With {.AutoSize = True, .MaximumSize = New Size(600, 0), .ForeColor = Color.DimGray, .Margin = New Padding(0, 10, 0, 0)}
        root.Controls.Add(lblHint, 0, 1)
        root.SetColumnSpan(lblHint, 2)

        Dim btnRow As New FlowLayoutPanel With {.AutoSize = True, .Anchor = AnchorStyles.Right, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False, .Margin = New Padding(0, 8, 0, 0)}
        btnClose = New Button With {.Width = 96, .Height = 30, .Text = If(Rus, "Закрыть", "Close"), .DialogResult = DialogResult.Cancel}
        btnRow.Controls.Add(btnClose)
        root.Controls.Add(btnRow, 0, 2)
        root.SetColumnSpan(btnRow, 2)

        Me.Controls.Add(root)
        Me.Controls.Add(picQr)
        Me.CancelButton = btnClose

        _loading = True
        chkLanOnly.Checked = _settings.LanOnlyExport
        chkNoPassword.Checked = _settings.ExcludePasswordFromExport
        _loading = False

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosed, AddressOf HandleFormClosed
    End Sub

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        SetHint(If(Rus, "Получение состояния..", "Fetching state.."))
        _status = Await ShareController.GetStatusAsync()
        If _status Is Nothing OrElse Not _status.Running Then
            SetHint(If(Rus, "Сервер не запущен.", "The server is not running."))
            Return
        End If
        PopulateShares()
        Rebuild()
    End Sub

    Private Sub PopulateShares()
        _loading = True
        clbShares.Items.Clear()
        Try
            If _status.Roots IsNot Nothing Then
                For Each r As ShareFolder In _status.Roots
                    Dim host As String = If(r.hostPath, "")
                    If host.Length = 0 Then Continue For
                    Dim idx As Integer = clbShares.Items.Add(New ShareItem(host, If(String.IsNullOrEmpty(r.name), host, r.name)))
                    Dim on_ As Boolean = _preselect.Count = 0 OrElse _preselect.Exists(Function(p) String.Equals(p, host, StringComparison.OrdinalIgnoreCase))
                    clbShares.SetItemChecked(idx, on_)
                Next
            End If
        Finally
            _loading = False
        End Try
    End Sub

    Private Function BuildOverrides() As ShareExportOverrides
        Dim o As New ShareExportOverrides()
        If chkPin.Checked Then
            o.HasPin = True
            o.Pin = txtPin.Text.Trim()
        End If
        If chkSlide.Checked Then
            o.HasSlideshow = True
            o.SlideshowInterval = CInt(numSlide.Value)
        End If
        o.ForceSoftReadOnly = chkSoftRo.Checked
        Return o
    End Function

    ''' <summary>Rebuilds the .fmscfg + QR for the CHECKED subset, with this recipient's overrides.</summary>
    Private Sub Rebuild()
        If _status Is Nothing Then Return
        Dim selected As New List(Of ShareFolder)()
        If _status.Roots IsNot Nothing Then
            For i As Integer = 0 To clbShares.Items.Count - 1
                If Not clbShares.GetItemChecked(i) Then Continue For
                Dim si As ShareItem = TryCast(clbShares.Items(i), ShareItem)
                If si Is Nothing Then Continue For
                For Each r As ShareFolder In _status.Roots
                    If String.Equals(If(r.hostPath, ""), si.HostPath, StringComparison.OrdinalIgnoreCase) Then
                        selected.Add(r)
                        Exit For
                    End If
                Next
            Next
        End If

        If selected.Count = 0 Then
            _config = Nothing
            ShowQr(Nothing)
            EnableExport(False)
            SetHint(If(Rus, "Отметьте хотя бы одну папку.", "Check at least one folder."))
            lblAddr.Text = "" : lblFinger.Text = ""
            Return
        End If

        Dim snapshot As New WorkerStatus With {
            .Running = _status.Running, .ListenPort = _status.ListenPort,
            .Username = _status.Username, .Password = _status.Password,
            .Fingerprint = _status.Fingerprint, .Reachability = _status.Reachability,
            .Roots = selected}

        _config = ShareConfigBuilder.Build(snapshot, includeExternal:=Not chkLanOnly.Checked,
                                           includePassword:=Not chkNoPassword.Checked, exportOverrides:=BuildOverrides())

        If _config Is Nothing Then
            ShowQr(Nothing)
            EnableExport(False)
            SetHint(If(Rus, "Нет доступного адреса для раздачи.", "No usable address to share."))
            Return
        End If

        ShowQr(_config)
        EnableExport(True)
        lblAddr.Text = If(_config.LanDisplay.Length > 0, (If(Rus, "Адрес: ", "Address: ") & _config.LanDisplay), "")
        lblFinger.Text = If(String.IsNullOrEmpty(_status.Fingerprint), "", (If(Rus, "Ключ узла: ", "Host key: ") & _status.Fingerprint))
        btnCopyLogin.Enabled = Not String.IsNullOrEmpty(_status.Password)

        If _config.QrOverflow Then
            SetHint(If(Rus, "Код слишком большой для QR - сохраните файл .fmscfg и передайте его.", "Too large for a QR - save the .fmscfg file and share that instead."))
        ElseIf chkNoPassword.Checked AndAlso Not String.IsNullOrEmpty(_status.Password) Then
            SetHint((If(Rus, "Пароль (передайте отдельно): ", "Password (pass separately): ")) & _status.Password)
        Else
            SetHint("")
        End If
    End Sub

    Private Sub OnSharesItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _loading Then Return
        BeginInvoke(New MethodInvoker(AddressOf Rebuild))
    End Sub

    Private Sub OnPinToggle(sender As Object, e As EventArgs)
        txtPin.Enabled = chkPin.Checked
        OnRebuildToggle(sender, e)
    End Sub

    Private Sub OnSlideToggle(sender As Object, e As EventArgs)
        numSlide.Enabled = chkSlide.Checked
        OnRebuildToggle(sender, e)
    End Sub

    Private Sub OnRebuildToggle(sender As Object, e As EventArgs)
        If _loading Then Return
        ' Persist the two saved prefs; the per-recipient overrides are one-shot (not saved).
        _settings.LanOnlyExport = chkLanOnly.Checked
        _settings.ExcludePasswordFromExport = chkNoPassword.Checked
        Try : _settings.Save() : Catch : End Try
        Rebuild()
    End Sub

    Private Sub OnCopyLogin(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse String.IsNullOrEmpty(_status.Password) Then Return
        Try
            Dim user As String = If(String.IsNullOrEmpty(_status.Username), "fms", _status.Username)
            Clipboard.SetText((If(Rus, "Логин: ", "Login: ") & user & Environment.NewLine) & (If(Rus, "Пароль: ", "Password: ") & _status.Password))
            SetHint(If(Rus, "Логин и пароль скопированы.", "Login and password copied."))
        Catch
        End Try
    End Sub

    Private Sub OnSaveConfig(sender As Object, e As EventArgs)
        If _config Is Nothing OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Using dlg As New SaveFileDialog() With {.Filter = "FMS config (*.fmscfg)|*.fmscfg", .FileName = "FastMediaSorter.fmscfg"}
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dlg.FileName, _config.ConfigJson, New UTF8Encoding(False))
                SetHint(If(Rus, "Файл сохранён.", "File saved."))
            Catch
                SetHint(If(Rus, "Не удалось сохранить файл.", "Could not save the file."))
            End Try
        End Using
    End Sub

    Private Sub OnEmail(sender As Object, e As EventArgs)
        If _config Is Nothing OrElse String.IsNullOrEmpty(_config.ConfigJson) Then Return
        Try
            Dim dir As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter")
            Directory.CreateDirectory(dir)
            Dim cfgFile As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(cfgFile, _config.ConfigJson, New UTF8Encoding(False))
            Dim subject As String = If(Rus, "Доступ к папкам Fast Media Sorter", "Fast Media Sorter folder access")
            Dim body As String = If(Rus, "Импортируйте вложенный файл .fmscfg в приложении FastMediaSorter на Android.",
                                         "Import the attached .fmscfg file in the FastMediaSorter Android app.")
            If Not MailSender.SendFile(cfgFile, subject, body) Then
                SetHint(If(Rus, "Не удалось открыть почтовый клиент.", "Could not open the mail client."))
            End If
        Catch
            SetHint(If(Rus, "Не удалось отправить письмо.", "Could not send the email."))
        End Try
    End Sub

    Private Sub ShowQr(cfg As ShareConfigResult)
        Dim old As Image = picQr.Image
        Dim newImg As Image = Nothing
        Try
            If cfg IsNot Nothing AndAlso cfg.QrPng IsNot Nothing Then
                Using ms As New MemoryStream(cfg.QrPng)
                    Using tmp As Image = Image.FromStream(ms)
                        newImg = New Bitmap(tmp)
                    End Using
                End Using
            End If
        Catch
            newImg = Nothing
        End Try
        picQr.Image = newImg
        If old IsNot Nothing Then old.Dispose()
        btnShowQr.Enabled = newImg IsNot Nothing
    End Sub

    Private Sub EnableExport(on_ As Boolean)
        btnSave.Enabled = on_
        btnEmail.Enabled = on_
    End Sub

    Private Sub SetHint(text As String)
        lblHint.Text = If(text, "")
    End Sub

    Private Sub HandleFormClosed(sender As Object, e As FormClosedEventArgs)
        Dim old As Image = picQr.Image
        picQr.Image = Nothing
        If old IsNot Nothing Then old.Dispose()
    End Sub

    Private NotInheritable Class ShareItem
        Public ReadOnly HostPath As String
        Private ReadOnly _display As String
        Public Sub New(hostPath As String, display As String)
            Me.HostPath = hostPath
            _display = display
        End Sub
        Public Overrides Function ToString() As String
            Return _display
        End Function
    End Class

End Class
