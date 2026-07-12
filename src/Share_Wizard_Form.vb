Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "Поделиться этой папкой" / "Share this folder" - the one-window quick wizard
''' that shares the folder the user is currently viewing with their Android phone.
''' It is the fast path over the Settings > Share tab: opened from the toolbar
''' button, the Shift+S hotkey or the folder-box context menu, it prefills the
''' current folder, brings the bundled SFTP worker up and shows the LAN address +
''' a big QR + the saveable .fmscfg in one step, plus opt-in internet-access
''' guidance (router port forwarding). Built fully in code (no Designer artifact);
''' drives the worker through the shared <see cref="ShareController"/>, so it and
''' the Settings tab always act on the same singleton server.
''' </summary>
Public Class Share_Wizard_Form
    Inherits Form

    Private ReadOnly _initialFolder As String
    Private _busy As Boolean
    Private _lastConfigJson As String = ""
    Private _status As WorkerStatus
    Private _config As ShareConfigResult
    Private _settings As ShareSettings
    Private _router As RouterIdentity

    ' Left column: folders + server control + LAN address.
    Private lblIntro As Label
    Private lblFolders As Label
    Private lvFolders As ListView
    Private btnAdd As Button
    Private btnRemove As Button
    Private btnToggle As Button
    Private lblState As Label
    Private lblLan As Label
    Private btnCopyLan As Button
    Private btnCopyCredentials As Button
    Private lblFinger As Label

    ' Right column: QR + save.
    Private picQr As PictureBox
    Private btnShowQr As Button
    Private lblScan As Label
    Private btnSaveConfig As Button
    Private btnEmail As Button
    Private chkNoPassword As CheckBox

    ' Internet group (full width, bottom) + footer.
    Private grpNet As GroupBox
    Private chkExternal As CheckBox
    Private lblNet As Label
    Private btnOpenRouter As Button
    Private lblRouterUrl As Label
    Private lnkGuide As LinkLabel
    Private lnkRouterSearch As LinkLabel
    Private txtForward As TextBox
    Private lblPhoneHint As Label
    Private lnkSettings As LinkLabel
    Private lnkWebGuide As LinkLabel
    Private lnkAndroid As LinkLabel
    Private btnClose As Button

    ''' <summary>Scales a design-time (96-dpi) pixel value to the current display DPI.
    ''' This wizard is built entirely in code with literal pixel coordinates and no
    ''' Designer-recorded AutoScaleDimensions baseline, so nothing here auto-scales
    ''' on its own - point-based fonts still render larger at higher DPI (that part
    ''' *is* automatic), which overflowed the fixed-size, un-scaled boxes. Wrapping
    ''' every literal in LU() keeps the boxes matched to the fonts, same fix as
    ''' Table_Form's Share/OCR tabs (see its LU()). Identity at 96 DPI.</summary>
    Private Function LU(px As Integer) As Integer
        Return LogicalToDeviceUnits(px)
    End Function

    Public Sub New(initialFolder As String)
        _initialFolder = If(initialFolder, "").Trim()
        _settings = New ShareSettings()
        Try
            _settings.Load()
        Catch
        End Try
        BuildUi()
    End Sub

    ' --- UI construction -------------------------------------------------------

    Private Sub BuildUi()
        Dim rus As Boolean = Is_Russian_Language

        Me.Text = If(rus, "Поделиться этой папкой", "Share this folder")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.ShowInTaskbar = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Font = New Font("Segoe UI", 8.5F)
        Me.ClientSize = New Size(LU(640), LU(648))
        Dim smallFont As New Font(Me.Font.FontFamily, Me.Font.Size * 0.85F)
        Dim linkFont As New Font(Me.Font.FontFamily, Me.Font.Size * 0.9F)

        lblIntro = New Label With {.Left = LU(12), .Top = LU(10), .Width = LU(616), .Height = LU(34),
            .Text = If(rus,
                "Эта папка станет видна на телефоне Android по локальной сети. Отсканируйте QR-код в приложении FastMediaSorter или сохраните файл .fmscfg.",
                "This folder becomes visible on your Android phone over the local network. Scan the QR in the FastMediaSorter app or save the .fmscfg file.")}
        Controls.Add(lblIntro)

        ' ---- left column ----
        lblFolders = New Label With {.Left = LU(12), .Top = LU(50), .Width = LU(300), .Height = LU(18),
            .Text = If(rus, "Папки в общем доступе:", "Shared folders:")}
        Controls.Add(lblFolders)

        lvFolders = New ListView With {.Left = LU(12), .Top = LU(70), .Width = LU(300), .Height = LU(118),
            .View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False}
        lvFolders.Columns.Add(If(rus, "Имя", "Name"), LU(112))
        lvFolders.Columns.Add(If(rus, "Путь", "Path"), LU(168))
        AddHandler lvFolders.DoubleClick, AddressOf OnConfigureFolder
        Controls.Add(lvFolders)
        Dim tipFolders As New ToolTip()
        tipFolders.SetToolTip(lvFolders, If(rus,
            "Двойной клик по папке - параметры ресурса (тип, например видеотека, PIN, папка-получатель..)",
            "Double-click a folder - resource options (type, e.g. video library, PIN, destination..)"))

        btnAdd = New Button With {.Left = LU(12), .Top = LU(194), .Width = LU(145), .Height = LU(28), .Text = If(rus, "Добавить папку..", "Add folder..")}
        btnRemove = New Button With {.Left = LU(163), .Top = LU(194), .Width = LU(149), .Height = LU(28), .Text = If(rus, "Убрать", "Remove")}
        AddHandler btnAdd.Click, AddressOf OnAddFolder
        AddHandler btnRemove.Click, AddressOf OnRemoveFolder
        Controls.Add(btnAdd)
        Controls.Add(btnRemove)

        btnToggle = New Button With {.Left = LU(12), .Top = LU(228), .Width = LU(300), .Height = LU(34),
            .Font = New Font(Me.Font, FontStyle.Bold), .Text = If(rus, "Начать общий доступ", "Start sharing")}
        AddHandler btnToggle.Click, AddressOf OnToggle
        Controls.Add(btnToggle)

        lblState = New Label With {.Left = LU(12), .Top = LU(268), .Width = LU(300), .Height = LU(18),
            .Font = New Font(Me.Font, FontStyle.Bold), .AutoEllipsis = True, .Text = If(rus, "Остановлено", "Stopped")}
        lblLan = New Label With {.Left = LU(12), .Top = LU(290), .Width = LU(300), .Height = LU(18), .AutoEllipsis = True, .Text = If(rus, "Адрес: -", "Address: -")}
        btnCopyLan = New Button With {.Left = LU(12), .Top = LU(312), .Width = LU(150), .Height = LU(26), .Enabled = False, .Text = If(rus, "Скопировать адрес", "Copy address")}
        btnCopyCredentials = New Button With {.Left = LU(174), .Top = LU(312), .Width = LU(138), .Height = LU(26), .Enabled = False, .Text = If(rus, "Логин/пароль", "Login/pass")}
        lblFinger = New Label With {.Left = LU(12), .Top = LU(344), .Width = LU(300), .Height = LU(30), .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .Font = smallFont, .Text = If(rus, "Ключ узла: -", "Host key: -")}
        AddHandler btnCopyLan.Click, AddressOf OnCopyLan
        AddHandler btnCopyCredentials.Click, AddressOf OnCopyCredentials
        Dim tipCredentials As New ToolTip()
        tipCredentials.SetToolTip(btnCopyCredentials, If(rus, "Скопировать логин и пароль SFTP в буфер обмена - чтобы передать их отдельно от файла/QR.", "Copy the SFTP login and password to the clipboard - to pass them on separately from the file/QR."))
        Controls.Add(lblState)
        Controls.Add(lblLan)
        Controls.Add(btnCopyLan)
        Controls.Add(btnCopyCredentials)
        Controls.Add(lblFinger)

        ' ---- right column ----
        ' The QR is too dense/unreadable shrunk into a small on-dialog box (same
        ' problem as the Settings > Share tab) - picQr stays an invisible image
        ' holder (still sized so Qr_Zoom_Form.ShowZoomed's 4x-of-box sizing works)
        ' and a big button opens it full-size instead.
        picQr = New PictureBox With {.Left = LU(324), .Top = LU(70), .Width = LU(190), .Height = LU(190),
            .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White, .Visible = False}
        Controls.Add(picQr)

        btnShowQr = New Button With {.Left = LU(324), .Top = LU(70), .Width = LU(304), .Height = LU(90), .Enabled = False,
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 1.1F, FontStyle.Bold), .Text = If(rus, "Показать QR-код", "Show QR code")}
        AddHandler btnShowQr.Click, Sub() Qr_Zoom_Form.ShowZoomed(Me, picQr)
        Controls.Add(btnShowQr)

        ' Extra room now that the QR box is gone: this label also carries the
        ' QR-overflow message ("share the file instead"), which needs more than
        ' one short line.
        lblScan = New Label With {.Left = LU(324), .Top = LU(168), .Width = LU(304), .Height = LU(56), .ForeColor = Color.DimGray,
            .AutoEllipsis = True, .Font = smallFont,
            .Text = If(rus, "Отсканируйте код в приложении на телефоне.", "Scan the code in the phone app.")}
        Controls.Add(lblScan)

        btnSaveConfig = New Button With {.Left = LU(324), .Top = LU(230), .Width = LU(190), .Height = LU(28), .Enabled = False,
            .Text = If(rus, "Сохранить .fmscfg..", "Save .fmscfg..")}
        AddHandler btnSaveConfig.Click, AddressOf OnSaveConfig
        Controls.Add(btnSaveConfig)

        btnEmail = New Button With {.Left = LU(324), .Top = LU(262), .Width = LU(190), .Height = LU(28), .Enabled = False,
            .Text = If(rus, "Отправить по почте..", "Send by email..")}
        AddHandler btnEmail.Click, AddressOf OnEmail
        Controls.Add(btnEmail)

        chkNoPassword = New CheckBox With {.Left = LU(324), .Top = LU(296), .Width = LU(304), .Height = LU(28),
            .Font = smallFont, .Text = ShareText.NoPasswordText(rus), .Checked = _settings.ExcludePasswordFromExport}
        AddHandler chkNoPassword.CheckedChanged, AddressOf OnNoPasswordToggled
        Controls.Add(chkNoPassword)

        ' ---- internet group ----
        grpNet = New GroupBox With {.Left = LU(12), .Top = LU(384), .Width = LU(616), .Height = LU(184),
            .Text = If(rus, "Из интернета (для всех папок сразу)", "From the internet (all folders at once)")}
        Controls.Add(grpNet)

        chkExternal = New CheckBox With {.Left = LU(12), .Top = LU(20), .Width = LU(590), .Height = LU(20),
            .Text = If(rus, "Открыть доступ из интернета", "Open internet access"), .Checked = _settings.ExternalAccessIntent}
        lblNet = New Label With {.Left = LU(12), .Top = LU(44), .Width = LU(590), .Height = LU(18), .AutoEllipsis = True,
            .Font = smallFont, .Text = If(rus, "Внешний доступ: -", "Internet: -")}
        btnOpenRouter = New Button With {.Left = LU(12), .Top = LU(66), .Width = LU(150), .Height = LU(26), .Enabled = False, .Text = If(rus, "Открыть роутер", "Open router")}
        lblRouterUrl = New Label With {.Left = LU(170), .Top = LU(70), .Width = LU(430), .Height = LU(18), .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .Font = smallFont}
        lnkGuide = New LinkLabel With {.Left = LU(12), .Top = LU(98), .Width = LU(280), .Height = LU(18), .Enabled = False,
            .Font = smallFont, .Text = If(rus, "Инструкция по пробросу (офлайн)..", "Port-forward guide (offline)..")}
        lnkRouterSearch = New LinkLabel With {.Left = LU(300), .Top = LU(98), .Width = LU(300), .Height = LU(18), .Enabled = False,
            .Font = smallFont, .Text = If(rus, "Гайд для моего роутера (определить модель)..", "Guide for my router (detect model)..")}
        txtForward = New TextBox With {.Left = LU(12), .Top = LU(124), .Width = LU(592), .Height = LU(50),
            .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.None,
            .BackColor = grpNet.BackColor, .TabStop = False, .Font = smallFont}
        AddHandler chkExternal.CheckedChanged, AddressOf OnExternalToggled
        AddHandler btnOpenRouter.Click, AddressOf OnOpenRouter
        AddHandler lnkGuide.LinkClicked, AddressOf OnOpenGuide
        AddHandler lnkRouterSearch.LinkClicked, AddressOf OnOpenRouterSearch
        grpNet.Controls.Add(chkExternal)
        grpNet.Controls.Add(lblNet)
        grpNet.Controls.Add(btnOpenRouter)
        grpNet.Controls.Add(lblRouterUrl)
        grpNet.Controls.Add(lnkGuide)
        grpNet.Controls.Add(lnkRouterSearch)
        grpNet.Controls.Add(txtForward)

        ' ---- footer ----
        lblPhoneHint = New Label With {.Left = LU(12), .Top = LU(576), .Width = LU(616), .Height = LU(18), .ForeColor = Color.DimGray,
            .AutoEllipsis = True, .Font = smallFont, .Text = If(rus,
                "На телефоне: FastMediaSorter -> Добавить ресурс -> SFTP/FTP -> Импорт из компаньона.",
                "On the phone: FastMediaSorter -> Add resource -> SFTP/FTP -> Import from companion.")}
        Controls.Add(lblPhoneHint)

        lnkSettings = New LinkLabel With {.Left = LU(12), .Top = LU(596), .Width = LU(150), .Height = LU(20),
            .Font = linkFont, .Text = If(rus, "Настройки доступа", "Sharing settings")}
        AddHandler lnkSettings.LinkClicked, AddressOf OnOpenSettings
        Controls.Add(lnkSettings)

        lnkWebGuide = New LinkLabel With {.Left = LU(170), .Top = LU(596), .Width = LU(360), .Height = LU(20),
            .Font = linkFont, .Text = If(rus, "Как публиковать папки (на сайте)..", "How to publish folders (website)..")}
        AddHandler lnkWebGuide.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        Controls.Add(lnkWebGuide)

        lnkAndroid = New LinkLabel With {.Left = LU(12), .Top = LU(620), .Width = LU(400), .Height = LU(20),
            .Font = linkFont, .Text = If(rus, "Приложение FastMediaSorter для Android ->", "FastMediaSorter app for Android ->")}
        AddHandler lnkAndroid.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite(Is_Russian_Language))
        Controls.Add(lnkAndroid)

        btnClose = New Button With {.Left = LU(538), .Top = LU(618), .Width = LU(90), .Height = LU(28), .Text = If(rus, "Закрыть", "Close")}
        AddHandler btnClose.Click, Sub() Me.Close()
        Controls.Add(btnClose)
        Me.CancelButton = btnClose

        AddHandler Me.Shown, AddressOf OnShownFirst
        AddHandler Me.FormClosed, AddressOf OnWizardClosed
    End Sub

    ' --- lifecycle -------------------------------------------------------------

    Private Async Sub OnShownFirst(sender As Object, e As EventArgs)
        RemoveHandler Me.Shown, AddressOf OnShownFirst  ' run once

        If Not WorkerProcess.IsAvailable() Then
            lblState.Text = If(Is_Russian_Language, "Компаньон не найден", "Companion not found")
            lblPhoneHint.Text = If(Is_Russian_Language,
                "Файл companion\fms-share-worker.exe не найден рядом с программой - переустановите приложение.",
                "companion\fms-share-worker.exe was not found next to the app - reinstall the application.")
            SetControlsEnabled(False)
            Return
        End If

        If _initialFolder.Length > 0 AndAlso IsRealFolder(_initialFolder) Then
            AddFolderRow(_initialFolder)
            Await RunShareAsync()
        Else
            lblPhoneHint.Text = If(Is_Russian_Language,
                "Добавьте папку и нажмите «Начать общий доступ».", "Add a folder and press Start sharing.")
            UpdateInternetUi()
        End If
    End Sub

    Private Sub OnWizardClosed(sender As Object, e As FormClosedEventArgs)
        Try
            If _settings IsNot Nothing Then _settings.Save()
        Catch
        End Try
        Dim old As Image = picQr.Image
        picQr.Image = Nothing
        If old IsNot Nothing Then old.Dispose()
    End Sub

    ' --- actions ---------------------------------------------------------------

    Private Async Sub OnToggle(sender As Object, e As EventArgs)
        If _busy Then Return
        SetBusy(True)
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
            Render()
            lblPhoneHint.Text = If(Is_Russian_Language, "Общий доступ остановлен.", "Sharing stopped.")
            SetBusy(False)
            Return
        End If
        SetBusy(False)
        Await RunShareAsync()
    End Sub

    Private Async Sub OnAddFolder(sender As Object, e As EventArgs)
        If _busy Then Return
        Dim picked As String = ""
        Using dlg As New FolderBrowserDialog()
            dlg.Description = If(Is_Russian_Language, "Выберите папку для общего доступа", "Choose a folder to share")
            dlg.ShowNewFolderButton = False
            If Not String.IsNullOrEmpty(_initialFolder) Then dlg.SelectedPath = _initialFolder
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            picked = dlg.SelectedPath
        End Using
        If Not AddFolderRow(picked) Then Return
        Await RunShareAsync()
    End Sub

    Private Async Sub OnRemoveFolder(sender As Object, e As EventArgs)
        If _busy Then Return
        If lvFolders.SelectedItems.Count = 0 Then Return
        lvFolders.Items.Remove(lvFolders.SelectedItems(0))
        If CurrentFolders().Count = 0 Then
            SetBusy(True)
            Await ShareController.StopServerAsync()
            _status = Await ShareController.GetStatusAsync()
            Render()
            SetBusy(False)
            lblPhoneHint.Text = If(Is_Russian_Language, "Добавьте папку и нажмите «Начать».", "Add a folder and press Start.")
            Return
        End If
        Await RunShareAsync()
    End Sub

    ''' <summary>Double-click a folder row: edit the per-root resource params the
    ''' schema v2 export carries to the phone (S1002 §9) - same dialog and rules as
    ''' the Share tab's "Настроить..". The destination flag flips the share's
    ''' writability (worker-side), so only that change needs a re-share; everything
    ''' else (label/profile/media set/comment/PIN/slideshow) is LITE-side export
    ''' metadata that Render() picks up straight from the store.</summary>
    Private Async Sub OnConfigureFolder(sender As Object, e As EventArgs)
        If _busy Then Return
        If lvFolders.SelectedItems.Count = 0 Then Return
        Dim it As ListViewItem = lvFolders.SelectedItems(0)
        Dim hostPath As String = Convert.ToString(it.Tag)
        If String.IsNullOrEmpty(hostPath) Then Return
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(hostPath)
        Using dlg As New Share_Root_Params_Form(it.Text, before)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim after As ShareRootParams = dlg.Result
            ShareRootParamsStore.SetFor(hostPath, after)
            If after.IsDestination <> before.IsDestination AndAlso _status IsNot Nothing AndAlso _status.Running Then
                Await RunShareAsync()
            Else
                Render()
            End If
        End Using
    End Sub

    Private Sub OnSaveConfig(sender As Object, e As EventArgs)
        If String.IsNullOrEmpty(_lastConfigJson) Then Return
        Using dlg As New SaveFileDialog()
            dlg.Filter = "FastMediaSorter config (*.fmscfg)|*.fmscfg|All files (*.*)|*.*"
            dlg.DefaultExt = "fmscfg"
            dlg.FileName = "FastMediaSorter.fmscfg"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dlg.FileName, _lastConfigJson, New UTF8Encoding(False))
                lblPhoneHint.Text = If(Is_Russian_Language, "Сохранено: ", "Saved: ") & dlg.FileName
            Catch ex As Exception
                lblPhoneHint.Text = ex.Message
            End Try
        End Using
    End Sub

    Private Sub OnEmail(sender As Object, e As EventArgs)
        If String.IsNullOrEmpty(_lastConfigJson) Then Return
        Dim rus As Boolean = Is_Russian_Language
        Try
            Dim dir As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter")
            Directory.CreateDirectory(dir)
            Dim p As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(p, _lastConfigJson, New UTF8Encoding(False))
            Dim subject As String = If(rus, "FastMediaSorter: доступ к папкам", "FastMediaSorter: folder access")
            Dim body As String = If(rus,
                "Откройте вложение .fmscfg в приложении FastMediaSorter на Android (Добавить ресурс -> Импорт из компаньона). Файл содержит пароль - не пересылайте посторонним.",
                "Open the attached .fmscfg in the FastMediaSorter Android app (Add resource -> Import from companion). The file contains a password - do not forward it to others.")
            If Not MailSender.SendFile(p, subject, body) Then
                lblPhoneHint.Text = If(rus, "Не удалось открыть почтовый клиент.", "Could not open the mail client.")
            End If
        Catch ex As Exception
            lblPhoneHint.Text = ex.Message
        End Try
    End Sub

    Private Sub OnCopyLan(sender As Object, e As EventArgs)
        Dim addr As String = CurrentLanAddress()
        If addr.Length = 0 Then Return
        Try
            Clipboard.SetText(addr)
            lblPhoneHint.Text = (If(Is_Russian_Language, "Скопировано: ", "Copied: ")) & addr
        Catch
        End Try
    End Sub

    ''' <summary>Always-available way to get the real SFTP login/password out of the
    ''' app - independent of the "exclude password from file/QR" checkbox below,
    ''' which only ever surfaced the password in lblPhoneHint, the same label every
    ''' other status update immediately overwrites.</summary>
    Private Sub OnCopyCredentials(sender As Object, e As EventArgs)
        If _status Is Nothing OrElse String.IsNullOrEmpty(_status.Password) Then Return
        Dim rus As Boolean = Is_Russian_Language
        Dim login As String = If(String.IsNullOrEmpty(_status.Username), "fms", _status.Username)
        Dim text As String = (If(rus, "Логин: ", "Login: ")) & login & Environment.NewLine &
            (If(rus, "Пароль: ", "Password: ")) & _status.Password
        Try
            Clipboard.SetText(text)
            lblPhoneHint.Text = If(rus, "Скопировано: логин и пароль", "Copied: login and password")
        Catch
        End Try
    End Sub

    Private Sub OnOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            lblPhoneHint.Text = If(Is_Russian_Language, "Не удалось определить адрес роутера.", "Could not determine the router address.")
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnOpenGuide(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim rus As Boolean = Is_Russian_Language
        Dim port As Integer = If(_status IsNot Nothing, _status.ListenPort, 0)
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        Dim lanIp As String = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        lblPhoneHint.Text = If(rus, "Определяем роутер..", "Detecting router..")
        Dim r As RouterIdentity = Await GetRouterAsync()
        Dim model As String = r.DisplayName()
        If Not ShareGuide.OpenPortForwardGuide(NetworkInfo.DefaultGatewayUrl(), port, lanIp, port, rus, model, RouterInfo.SearchUrl(r)) Then
            lblPhoneHint.Text = If(rus, "Не удалось открыть инструкцию.", "Could not open the guide.")
        ElseIf model.Length > 0 Then
            lblPhoneHint.Text = (If(rus, "Роутер: ", "Router: ")) & model
        End If
    End Sub

    Private Async Sub OnOpenRouterSearch(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim rus As Boolean = Is_Russian_Language
        lblPhoneHint.Text = If(rus, "Определяем роутер..", "Detecting router..")
        Dim r As RouterIdentity = Await GetRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(r))
        lblPhoneHint.Text = If(r.DisplayName().Length > 0,
            (If(rus, "Роутер: ", "Router: ")) & r.DisplayName(),
            If(rus, "Модель не определена - открыт общий поиск.", "Model unknown - opened a general search."))
    End Sub

    Private Async Function GetRouterAsync() As Task(Of RouterIdentity)
        If _router Is Nothing Then _router = Await Task.Run(Function() RouterInfo.Detect())
        Return _router
    End Function

    Private Sub OnExternalToggled(sender As Object, e As EventArgs)
        If _settings IsNot Nothing Then _settings.ExternalAccessIntent = chkExternal.Checked
        Render()  ' rebuild config/QR (add or drop the internet path) + net UI
    End Sub

    ''' <summary>The §6 "exclude password" export safeguard (same setting as the
    ''' Share tab): rebuild the config/QR and, while on, show the real password so
    ''' the sender can pass it on out-of-band.</summary>
    Private Sub OnNoPasswordToggled(sender As Object, e As EventArgs)
        If _settings IsNot Nothing Then
            _settings.ExcludePasswordFromExport = chkNoPassword.Checked
            _settings.Save()
        End If
        Render()
        If chkNoPassword.Checked Then
            Dim pw As String = If(_status IsNot Nothing, If(_status.Password, ""), "")
            lblPhoneHint.Text = ShareText.NoPasswordHint(Is_Russian_Language, pw)
        End If
    End Sub

    Private Sub OnOpenSettings(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim mf As Main_Form = TryCast(Me.Owner, Main_Form)
        Me.Close()
        If mf IsNot Nothing Then mf.ShowShareSettings()
    End Sub

    ' --- worker flow -----------------------------------------------------------

    Private Async Function RunShareAsync() As Task
        If CurrentFolders().Count = 0 Then Return
        SetBusy(True)
        lblPhoneHint.Text = If(Is_Russian_Language, "Запуск сервера..", "Starting server..")

        Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(CurrentFolders())
        _status = r.Status

        If Not r.Available Then
            lblState.Text = If(Is_Russian_Language, "Компаньон не найден", "Companion not found")
            SetControlsEnabled(False)
            SetBusy(False)
            Return
        End If
        If Not r.Reachable Then
            lblPhoneHint.Text = If(Is_Russian_Language, "Не удалось связаться с компаньоном.", "Could not reach the companion worker.")
            Render()
            SetBusy(False)
            Return
        End If

        Render()
        If r.Served AndAlso _config IsNot Nothing Then
            lblPhoneHint.Text = If(Is_Russian_Language,
                "Готово - отсканируйте QR-код или сохраните .fmscfg.", "Ready - scan the QR code or save the .fmscfg file.")
        Else
            lblPhoneHint.Text = If(Is_Russian_Language,
                "Сервер запущен, но адрес ещё не определён. Проверьте брандмауэр и повторите.",
                "Server started, but no address yet. Check the firewall and try again.")
        End If
        SetBusy(False)
    End Function

    ' --- rendering -------------------------------------------------------------

    Private Sub Render()
        Dim rus As Boolean = Is_Russian_Language
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running

        If running Then
            lblState.Text = (If(rus, "Работает, порт ", "Running, port ")) & _status.ListenPort.ToString()
            btnToggle.Text = If(rus, "Остановить общий доступ", "Stop sharing")
        Else
            lblState.Text = If(rus, "Остановлено", "Stopped")
            btnToggle.Text = If(rus, "Начать общий доступ", "Start sharing")
        End If

        Dim addr As String = CurrentLanAddress()
        lblLan.Text = (If(rus, "Адрес: ", "Address: ")) & If(addr.Length > 0, addr, "-")
        btnCopyLan.Enabled = addr.Length > 0 AndAlso Not _busy
        btnCopyCredentials.Enabled = running AndAlso Not String.IsNullOrEmpty(_status.Password) AndAlso Not _busy

        Dim fp As String = If(_status IsNot Nothing, If(_status.Fingerprint, ""), "")
        lblFinger.Text = (If(rus, "Ключ узла: ", "Host key: ")) & If(fp.Length > 0, fp, "-")

        If _status IsNot Nothing AndAlso _status.Running Then
            Dim incPw As Boolean = _settings Is Nothing OrElse Not _settings.ExcludePasswordFromExport
            _config = ShareConfigBuilder.Build(_status, chkExternal.Checked, incPw)
        Else
            _config = Nothing
        End If
        ShowConfig(_config)
        UpdateInternetUi()

        ' Keep the tray indicator in step with the server state right away.
        Try : Main_Form.RefreshShareTray() : Catch : End Try
    End Sub

    Private Sub UpdateInternetUi()
        Dim rus As Boolean = Is_Russian_Language
        Dim running As Boolean = _status IsNot Nothing AndAlso _status.Running
        Dim reach As WorkerReachability = If(_status IsNot Nothing, _status.Reachability, Nothing)
        Dim wantExternal As Boolean = chkExternal.Checked

        Dim router As String = NetworkInfo.DefaultGatewayIp()
        lblRouterUrl.Text = If(router.Length > 0, "http://" & router, "-")
        btnOpenRouter.Enabled = wantExternal AndAlso router.Length > 0
        lnkGuide.Enabled = wantExternal
        lnkRouterSearch.Enabled = wantExternal

        If Not wantExternal Then
            lblNet.Text = If(rus, "Внешний доступ выключен (только локальная сеть).", "Internet access off (LAN only).")
            txtForward.Text = ""
            Return
        End If
        If Not running Then
            lblNet.Text = If(rus, "Запустите общий доступ, чтобы настроить интернет.", "Start sharing to set up internet access.")
            txtForward.Text = ""
            Return
        End If
        If reach Is Nothing Then
            lblNet.Text = If(rus, "Определяем внешний адрес..", "Detecting the external address..")
            txtForward.Text = ""
            Return
        End If

        Dim port As Integer = _status.ListenPort
        Dim lanIp As String = If(Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        Dim extHost As String = If(reach.ExternalHost, "")
        Dim extPort As Integer = reach.ExternalPort
        Dim isCgnat As Boolean = reach.IsCgnat
        Dim mapped As Boolean = Not String.IsNullOrEmpty(reach.PortMapMethod)
        Dim routerText As String = If(router.Length > 0, "http://" & router, If(rus, "адрес роутера", "the router address"))
        Dim lanText As String = If(lanIp.Length > 0, lanIp, If(rus, "IP этого ПК", "this PC's IP"))

        Dim sb As New StringBuilder()
        If _config IsNot Nothing AndAlso _config.LanDisplay.Length = 0 Then
            sb.AppendLine(ShareText.LanUnknownText(rus)).AppendLine()
        End If
        sb.AppendLine(ShareText.SecurityText(rus)).AppendLine()

        If isCgnat Then
            lblNet.Text = If(rus, "За CGNAT - извне недоступно.", "Behind CGNAT - not reachable from outside.")
            sb.Append(ShareText.CgnatText(rus))
        ElseIf mapped Then
            lblNet.Text = (If(rus, "Доступно из интернета: ", "Reachable from internet: ")) & extHost & ":" & (If(extPort > 0, extPort, port)).ToString()
            If reach.ExternalVerified Then
                sb.Append(If(rus,
                    "Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg. Учтите: это не подтверждает работу извне - точный тест только с телефона по мобильной сети. При долгой работе общего доступа проверка может устареть; если телефон не подключается, отключите и снова включите общий доступ.",
                    "The port was opened automatically (UPnP) - no router setup needed. The address is already in the QR code and .fmscfg file. Note: this does not confirm it actually works from outside - the definitive test is from the phone on mobile data. Long-running sessions can go stale; if the phone can't connect, turn sharing off and back on."))
            Else
                sb.Append(ShareText.ExternalUnverifiedText(rus))
            End If
        ElseIf extHost.Length > 0 Then
            lblNet.Text = (If(rus, "Внешний адрес: ", "External address: ")) & extHost & ":" & port.ToString() & If(rus, " (нужен проброс порта)", " (needs port forwarding)")
            sb.Append(ShareText.PortForwardText(rus, routerText, port, lanText, port))
        Else
            lblNet.Text = If(rus, "Внешний адрес неизвестен - узнайте в роутере.", "External address unknown - check the router.")
            sb.Append(ShareText.PortForwardText(rus, routerText, port, lanText, port))
        End If

        txtForward.Text = sb.ToString()
    End Sub

    ' --- helpers ---------------------------------------------------------------

    Private Function CurrentFolders() As List(Of ShareFolder)
        Dim list As New List(Of ShareFolder)
        For Each it As ListViewItem In lvFolders.Items
            Dim hostPath As String = Convert.ToString(it.Tag)
            If Not String.IsNullOrEmpty(hostPath) Then
                ' Same rule as the Share tab: a destination root (v2 isDestination,
                ' configured there) must accept writes; everything else read-only.
                Dim writable As Boolean = ShareRootParamsStore.GetFor(hostPath).IsDestination
                list.Add(New ShareFolder With {.name = it.Text, .hostPath = hostPath, .readOnly = Not writable})
            End If
        Next
        Return list
    End Function

    Private Function CurrentLanAddress() As String
        If _status Is Nothing OrElse Not _status.Running OrElse _status.Reachability Is Nothing Then Return ""
        Dim lan As String = If(_status.Reachability.LanAddress, "")
        If lan.Length = 0 Then Return ""
        Return lan & ":" & _status.ListenPort.ToString()
    End Function

    Private Function AddFolderRow(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        For Each it As ListViewItem In lvFolders.Items
            If String.Equals(Convert.ToString(it.Tag), path, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Dim item As New ListViewItem(FolderDisplayName(path))
        item.SubItems.Add(path)
        item.Tag = path
        lvFolders.Items.Add(item)
        Return True
    End Function

    Private Sub ShowConfig(cfg As ShareConfigResult)
        If cfg Is Nothing Then
            Dim old0 As Image = picQr.Image
            picQr.Image = Nothing
            If old0 IsNot Nothing Then old0.Dispose()
            picQr.Cursor = Cursors.Default
            btnShowQr.Enabled = False
            lblScan.Text = If(Is_Russian_Language, "Отсканируйте код в приложении на телефоне.", "Scan the code in the phone app.")
            _lastConfigJson = ""
            btnSaveConfig.Enabled = False
            btnEmail.Enabled = False
            Return
        End If

        ' Always replace the image: a config whose QR overflowed (QrPng = Nothing)
        ' must not keep showing the previous, now-stale code.
        Dim newImg As Image = Nothing
        Try
            If cfg.QrPng IsNot Nothing AndAlso cfg.QrPng.Length > 0 Then
                Using ms As New MemoryStream(cfg.QrPng)
                    Using tmp As Image = Image.FromStream(ms)
                        newImg = New Bitmap(tmp)
                    End Using
                End Using
            End If
        Catch
            newImg = Nothing
        End Try
        Dim old As Image = picQr.Image
        picQr.Image = newImg
        If old IsNot Nothing Then old.Dispose()
        picQr.Cursor = If(newImg IsNot Nothing, Cursors.Hand, Cursors.Default) ' click = zoom window
        btnShowQr.Enabled = newImg IsNot Nothing AndAlso Not _busy
        ' Prefer the worker's honest reachability line (dead forward / CGNAT /
        ' IPv6-only / confirmed / LAN-only) under the QR; fall back to the plain
        ' "scan the code" prompt.
        Dim scanText As String = If(Not String.IsNullOrEmpty(cfg.AccessNote), cfg.AccessNote,
            If(Is_Russian_Language, "Отсканируйте код в приложении на телефоне.", "Scan the code in the phone app."))
        lblScan.Text = If(cfg.QrOverflow, ShareText.QrOverflowText(Is_Russian_Language), scanText)
        _lastConfigJson = If(cfg.ConfigJson, "")
        btnSaveConfig.Enabled = _lastConfigJson.Length > 0 AndAlso Not _busy
        btnEmail.Enabled = _lastConfigJson.Length > 0 AndAlso Not _busy
    End Sub

    Private Sub SetBusy(value As Boolean)
        _busy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        btnToggle.Enabled = Not value AndAlso avail
        btnAdd.Enabled = Not value AndAlso avail
        btnRemove.Enabled = Not value AndAlso avail
        btnSaveConfig.Enabled = Not value AndAlso _lastConfigJson.Length > 0
        btnEmail.Enabled = Not value AndAlso _lastConfigJson.Length > 0
        btnCopyLan.Enabled = Not value AndAlso CurrentLanAddress().Length > 0
        btnCopyCredentials.Enabled = Not value AndAlso _status IsNot Nothing AndAlso Not String.IsNullOrEmpty(_status.Password)
        btnShowQr.Enabled = Not value AndAlso picQr.Image IsNot Nothing
        Me.UseWaitCursor = value
    End Sub

    Private Sub SetControlsEnabled(enabled As Boolean)
        btnToggle.Enabled = enabled AndAlso Not _busy
        btnAdd.Enabled = enabled AndAlso Not _busy
        btnRemove.Enabled = enabled AndAlso Not _busy
    End Sub

    Private Shared Function IsRealFolder(path As String) As Boolean
        Try
            Return Directory.Exists(path)
        Catch
            Return False
        End Try
    End Function

    Private Shared Function FolderDisplayName(path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Try
            Dim name As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(name) Then Return name
        Catch
        End Try
        Return path
    End Function

End Class
