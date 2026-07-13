Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' Companion package wizard - LEVEL 2 of the two-wizard model (§4.5): a ONE-SHOT
''' act of giving a specific recipient access. The user picks a SUBSET of the
''' already-served shares, optionally restricts to LAN / drops the password, and
''' gets a QR + savable/mailable .fmscfg for exactly those roots. Nothing here is
''' persisted (§4.5.3). Share-level params (name/type/hard-RO/PIN/slideshow) are
''' inherited from each share via ShareRootParamsStore inside ShareConfigBuilder.
''' The .fmscfg for a subset is produced by handing ShareConfigBuilder a status
''' snapshot whose Roots are filtered to the checked shares - no contract change.
''' </summary>
Public NotInheritable Class PackageWizardForm
    Inherits Form

    Private ReadOnly _preselect As List(Of String)
    Private ReadOnly _settings As New ShareSettings()
    Private _status As WorkerStatus
    Private _config As ShareConfigResult
    Private _busy As Boolean
    Private _loading As Boolean

    Private clbShares As CheckedListBox
    Private chkLanOnly As CheckBox
    Private chkNoPassword As CheckBox
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
        Me.ClientSize = New Size(560, 452)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.AutoScaleMode = AutoScaleMode.Font
        toolTip = New ToolTip()

        ' Left: which shares go into this access code.
        Controls.Add(New Label With {.Left = 12, .Top = 12, .Width = 300, .Height = 18,
            .Text = If(Rus, "Папки в этом коде доступа:", "Folders in this access code:")})
        clbShares = New CheckedListBox With {.Left = 12, .Top = 34, .Width = 300, .Height = 300, .CheckOnClick = True, .IntegralHeight = False}
        AddHandler clbShares.ItemCheck, AddressOf OnSharesItemCheck
        Controls.Add(clbShares)

        chkLanOnly = New CheckBox With {.Left = 12, .Top = 342, .Width = 300, .Height = 20,
            .Text = If(Rus, "Только локальная сеть (без адреса из интернета)", "LAN only (no internet address)")}
        chkNoPassword = New CheckBox With {.Left = 12, .Top = 366, .Width = 300, .Height = 20,
            .Text = If(Rus, "Не включать пароль в файл/QR", "Do not include the password in the file/QR")}
        AddHandler chkLanOnly.CheckedChanged, AddressOf OnLanOnlyChanged
        AddHandler chkNoPassword.CheckedChanged, AddressOf OnNoPasswordChanged
        Controls.AddRange(New Control() {chkLanOnly, chkNoPassword})
        toolTip.SetToolTip(chkNoPassword, If(Rus, "Пароль не попадёт в файл/QR - телефон запросит его при импорте; передайте пароль отдельно (он показан ниже).",
                                                 "The password stays out of the file/QR - the phone asks for it at import; pass it separately (shown below)."))

        ' Right: QR + export actions.
        btnShowQr = New Button With {.Left = 324, .Top = 34, .Width = 224, .Height = 200, .Font = New Font(Me.Font, FontStyle.Bold),
            .Text = If(Rus, "Показать QR-код", "Show QR code"), .Enabled = False}
        AddHandler btnShowQr.Click, Sub() Qr_Zoom_Form.ShowZoomed(Me, picQr)
        picQr = New PictureBox With {.Left = 324, .Top = 34, .Width = 224, .Height = 200, .Visible = False, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}
        Controls.Add(btnShowQr)
        Controls.Add(picQr)

        lblAddr = New Label With {.Left = 324, .Top = 242, .Width = 224, .Height = 20, .AutoEllipsis = True, .ForeColor = Color.DimGray}
        lblFinger = New Label With {.Left = 324, .Top = 262, .Width = 224, .Height = 34, .AutoEllipsis = True, .ForeColor = Color.DimGray, .Font = New Font(Me.Font.FontFamily, Me.Font.Size - 0.5F)}
        Controls.AddRange(New Control() {lblAddr, lblFinger})

        btnCopyLogin = New Button With {.Left = 324, .Top = 300, .Width = 224, .Height = 28, .Text = If(Rus, "Скопировать логин/пароль", "Copy login/password"), .Enabled = False}
        btnSave = New Button With {.Left = 324, .Top = 332, .Width = 224, .Height = 28, .Text = If(Rus, "Сохранить файл .fmscfg..", "Save .fmscfg file.."), .Enabled = False}
        btnEmail = New Button With {.Left = 324, .Top = 364, .Width = 224, .Height = 28, .Text = If(Rus, "Отправить по почте..", "Send by email.."), .Enabled = False}
        AddHandler btnCopyLogin.Click, AddressOf OnCopyLogin
        AddHandler btnSave.Click, AddressOf OnSaveConfig
        AddHandler btnEmail.Click, AddressOf OnEmail
        Controls.AddRange(New Control() {btnCopyLogin, btnSave, btnEmail})

        lblHint = New Label With {.Left = 12, .Top = 396, .Width = 440, .Height = 48, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        btnClose = New Button With {.Left = 466, .Top = 414, .Width = 82, .Height = 28, .Text = If(Rus, "Закрыть", "Close"), .DialogResult = DialogResult.Cancel}
        Controls.Add(lblHint)
        Controls.Add(btnClose)
        Me.CancelButton = btnClose

        ' Reflect saved prefs into the toggles (without firing a rebuild).
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

    ''' <summary>Rebuilds the .fmscfg + QR for the CHECKED subset of shares.</summary>
    Private Sub Rebuild()
        If _busy OrElse _status Is Nothing Then Return
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
            lblAddr.Text = ""
            lblFinger.Text = ""
            Return
        End If

        ' Hand the builder a status snapshot filtered to the selected roots.
        Dim snapshot As New WorkerStatus With {
            .Running = _status.Running, .ListenPort = _status.ListenPort,
            .Username = _status.Username, .Password = _status.Password,
            .Fingerprint = _status.Fingerprint, .Reachability = _status.Reachability,
            .Roots = selected}

        Dim includeExternal As Boolean = Not chkLanOnly.Checked
        Dim includePassword As Boolean = Not chkNoPassword.Checked
        _config = ShareConfigBuilder.Build(snapshot, includeExternal, includePassword)

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
            SetHint(If(Rus, "Код слишком большой для QR - сохраните файл .fmscfg и передайте его.",
                            "Too large for a QR - save the .fmscfg file and share that instead."))
        ElseIf chkNoPassword.Checked AndAlso Not String.IsNullOrEmpty(_status.Password) Then
            SetHint((If(Rus, "Пароль (передайте отдельно): ", "Password (pass separately): ")) & _status.Password)
        Else
            SetHint("")
        End If
    End Sub

    Private Sub OnSharesItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _loading Then Return
        ' ItemCheck fires BEFORE the state flips; defer the rebuild so GetItemChecked is current.
        BeginInvoke(New MethodInvoker(AddressOf Rebuild))
    End Sub

    Private Sub OnLanOnlyChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        _settings.LanOnlyExport = chkLanOnly.Checked
        Try : _settings.Save() : Catch : End Try
        Rebuild()
    End Sub

    Private Sub OnNoPasswordChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        _settings.ExcludePasswordFromExport = chkNoPassword.Checked
        Try : _settings.Save() : Catch : End Try
        Rebuild()
    End Sub

    Private Sub OnCopyLogin(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse String.IsNullOrEmpty(_status.Password) Then Return
        Try
            Dim user As String = If(String.IsNullOrEmpty(_status.Username), "fms", _status.Username)
            Clipboard.SetText((If(Rus, "Логин: ", "Login: ") & user & Environment.NewLine) &
                              (If(Rus, "Пароль: ", "Password: ") & _status.Password))
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

    ''' <summary>CheckedListBox item wrapping a share (host path + display name).</summary>
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
