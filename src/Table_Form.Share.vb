Option Strict On

Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

' "Поделиться" / "Share" tab of the Settings window - the Windows half of the
' Android Folder Share feature (SPECIFICATION_ANDROID_FOLDER_SHARE.md). Drives the
' bundled headless worker (companion\fms-share-worker.exe) via ShareController.
'
' LAYOUT: a common left column (shared folders + Start/Stop + autostart) and, on
' the right, an inner TabControl with two pages that each produce their OWN QR +
' .fmscfg:
'   * "Локальная сеть" (LAN)  -> ShareConfigBuilder.Build(status, includeExternal:=False)
'   * "Из интернета" (Internet) -> ShareConfigBuilder.Build(status, includeExternal:=True)
'     plus router / port-forward guidance. The internet config also advertises the
'     external access path (LAN + port-forward), so one scan works home or away.
' Both configs can be saved to file or attached to a new email (Simple MAPI).
'
' ACCESS MODEL (honest, given the frozen single-server worker - one SFTP port, one
' credential, all folders, binds 0.0.0.0): internal visibility is genuinely
' per-folder (the ListView checkboxes ARE the shared set); "external" is a whole-
' server property (a router port maps the whole port), so it is a mode (the
' Internet inner tab), not a per-folder switch. The worker always auto-attempts
' UPnP on start - the tabs decide only which addresses each exported config
' advertises. This TabPage is created fully in code (Tab_Page_6): every literal
' coordinate is a logical (96-dpi) pixel scaled through Table_Form.LU() so text
' never overflows its box at high DPI. The Settings window is fixed-size
' (FormBorderStyle.FixedDialog), so controls use plain absolute placement with NO
' Anchor - anchors previously drove the bottom-anchored left column off-screen at
' high DPI, blanking the tab. (Class-level XML doc lives on Table_Form.Ocr.vb.)
Partial Public Class Table_Form

    Private Tab_Page_6 As TabPage

    Private _shareBuilt As Boolean
    Private _shareLoading As Boolean
    Private _shareBusy As Boolean
    Private _shareTesting As Boolean
    Private _shareReachPollGen As Integer
    Private _shareSettings As ShareSettings
    Private _shareListPopulated As Boolean

    Private _shareStatus As WorkerStatus
    Private _cfgLan As ShareConfigResult
    Private _cfgNet As ShareConfigResult
    ''' <summary>The config the primary QR / Save / Email act on: the combined
    ''' [lan, portforward] build by default (S1006 - one scan works home and
    ''' away), or the LAN-only build when the user ticks "LAN only". Points at
    ''' _cfgNet or _cfgLan; never a third build.</summary>
    Private _cfgPrimary As ShareConfigResult
    Private _shareRouter As RouterIdentity
    ''' <summary>Set on the second MouseDown of a row double-click so ItemCheck can
    ''' cancel the checkbox toggle the native ListView performs on NM_DBLCLK -
    ''' otherwise double-click (advertised as "resource options") would silently
    ''' unshare the folder and the busy guard would eat the dialog.</summary>
    Private _shareSuppressCheck As Boolean

    ' Common (left column)
    Private lblShareIntro As Label
    Private lblShareFolders As Label
    Private lvShareFolders As ListView
    Private btnShareAddCurrent As Button
    Private btnShareAdd As Button
    Private btnShareRemove As Button
    Private btnShareParams As Button
    Private btnShareToggle As Button
    Private lblShareState As Label
    Private chkShareAutostart As CheckBox
    Private chkShareNoPassword As CheckBox
    Private lnkShareAndroid As LinkLabel

    ' Inner TabControl (right column). Reframed by ROLE (S1006), not by network:
    '   tab 1 (tpShareLan) = the ONE primary QR that works home + away
    '   tab 2 (tpShareNet) = internet-access setup guidance only (no duplicate QR)
    Private shareInnerTabs As TabControl
    Private tpShareLan As TabPage
    Private tpShareNet As TabPage

    ' Primary QR tab (tpShareLan)
    Private picShareQrLan As PictureBox
    Private lblShareLanAddr As Label
    Private btnShareTestLan As Button
    Private btnShareCopyLan As Button
    Private btnShareCopyCredentials As Button
    Private lblShareFinger As Label
    Private chkShareLanOnly As CheckBox
    Private btnShareSaveLan As Button
    Private btnShareEmailLan As Button
    Private lblShareLanHint As Label

    ' Internet-setup guidance tab (tpShareNet) - no QR / Save / Email of its own;
    ' the combined config is exported from the primary tab.
    Private lblShareNet As Label
    Private btnShareTestNet As Button
    Private btnShareOpenRouter As Button
    Private lblShareRouterUrl As Label
    Private lnkShareGuide As LinkLabel
    Private lnkShareRouterSearch As LinkLabel
    Private lnkShareWebGuide As LinkLabel
    Private txtShareForward As TextBox

    ' --- public hooks (called from Table_Form.PrepareForDisplay) ---------------

    Private Sub PrepareShareTabForDisplay()
        BuildShareTabIfNeeded()
        LocalizeShareTab()
        InitializeShareTooltips()
        LoadShareLocalState()
    End Sub

    Private Sub Tab_Control_SelectedIndexChanged_Share(sender As Object, e As EventArgs) Handles Tab_Control.SelectedIndexChanged
        If Tab_Page_6 IsNot Nothing AndAlso Tab_Control.SelectedTab Is Tab_Page_6 Then OnEnterShareTab()
    End Sub

    ''' <summary>Switches the Settings window to the Share tab. When
    ''' <paramref name="internet"/> is True, also selects the Internet inner tab.</summary>
    Friend Sub SelectShareTab(Optional internet As Boolean = False)
        BuildShareTabIfNeeded()
        If Tab_Page_6 Is Nothing Then Return
        If shareInnerTabs IsNot Nothing AndAlso tpShareNet IsNot Nothing Then
            shareInnerTabs.SelectedTab = If(internet, tpShareNet, tpShareLan)
        End If
        If Tab_Control.SelectedTab Is Tab_Page_6 Then
            OnEnterShareTab()
        Else
            Tab_Control.SelectedTab = Tab_Page_6 ' fires SelectedIndexChanged -> OnEnterShareTab
        End If
    End Sub

    Private Sub ShareTab_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Try
            If _shareSettings IsNot Nothing Then _shareSettings.Save()
        Catch
        End Try
    End Sub

    Private _shareDpiSized As Boolean

    ''' <summary>High-DPI safety net. The Share/OCR tabs position their code-created
    ''' controls with LU()-scaled (device-pixel) coordinates, but this FixedDialog
    ''' Settings form (AutoScaleMode.Font, PerMonitorV2 manifest on .NET Framework)
    ''' does not reliably grow to the full DPI-scaled size at fractional scaling
    ''' (e.g. 175%), so the inner-tab content - the QR box and the Test/Email buttons -
    ''' was clipped past the right edge (a user at 175% saw no QR and missing buttons).
    ''' Once the form is shown (after its auto-scale pass), grow it to the LU-scaled
    ''' design size so the scaled layout always has room. No-op at 100% DPI and
    ''' whenever the form already fits.</summary>
    Private Sub ShareTab_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _shareDpiSized Then Return
        _shareDpiSized = True
        Try
            Dim needW As Integer = LU(700)
            Dim needH As Integer = LU(466)
            If Me.ClientSize.Width < needW OrElse Me.ClientSize.Height < needH Then
                Me.ClientSize = New Size(Math.Max(Me.ClientSize.Width, needW), Math.Max(Me.ClientSize.Height, needH))
            End If
        Catch
        End Try
    End Sub

    ' --- build -----------------------------------------------------------------

    Private Sub BuildShareTabIfNeeded()
        If _shareBuilt Then Return
        If Tab_Control Is Nothing Then Return
        _shareBuilt = True

        Dim prev As Boolean = _shareLoading
        _shareLoading = True

        Tab_Page_6 = New TabPage With {.Padding = New Padding(4), .UseVisualStyleBackColor = True}
        Tab_Control.Controls.Add(Tab_Page_6)
        Tab_Page_6.SuspendLayout()

        ' Fixed-size Settings window (Table_Form = FormBorderStyle.FixedDialog): the
        ' Share tab uses plain absolute placement. Every literal is a logical (96-dpi)
        ' pixel scaled through LU() for high DPI, and NO Anchor is set (controls keep
        ' the default Top+Left). Anchors here previously pushed the bottom-anchored
        ' left column to a negative Y (off the top of the page) when the tab was laid
        ' out at the DPI-scaled size, so every element vanished; absolute coordinates
        ' keep each control exactly where it is placed.

        lblShareIntro = New Label With {.Left = LU(12), .Top = LU(6), .Width = LU(650), .Height = LU(30), .AutoSize = False}
        Tab_Page_6.Controls.Add(lblShareIntro)

        ' ---- left column: folders + server control (x=12, w=336) ----
        lblShareFolders = New Label With {.Left = LU(12), .Top = LU(40), .Width = LU(336), .Height = LU(16), .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
        Tab_Page_6.Controls.Add(lblShareFolders)

        lvShareFolders = New ListView With {.Left = LU(12), .Top = LU(58), .Width = LU(336), .Height = LU(150),
            .View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False, .CheckBoxes = True}
        lvShareFolders.Columns.Add("", LU(150))
        lvShareFolders.Columns.Add("", LU(178))
        AddHandler lvShareFolders.MouseDown, AddressOf OnShareListMouseDown
        AddHandler lvShareFolders.ItemCheck, AddressOf OnShareItemCheck
        AddHandler lvShareFolders.ItemChecked, AddressOf OnShareItemChecked
        AddHandler lvShareFolders.DoubleClick, AddressOf OnShareConfigureFolder
        Tab_Page_6.Controls.Add(lvShareFolders)

        btnShareAddCurrent = New Button With {.Left = LU(12), .Top = LU(212), .Width = LU(104), .Height = LU(27)}
        btnShareAdd = New Button With {.Left = LU(120), .Top = LU(212), .Width = LU(64), .Height = LU(27)}
        btnShareRemove = New Button With {.Left = LU(188), .Top = LU(212), .Width = LU(68), .Height = LU(27)}
        btnShareParams = New Button With {.Left = LU(260), .Top = LU(212), .Width = LU(88), .Height = LU(27)}
        AddHandler btnShareAddCurrent.Click, AddressOf OnShareAddCurrentFolder
        AddHandler btnShareAdd.Click, AddressOf OnShareAddFolder
        AddHandler btnShareRemove.Click, AddressOf OnShareRemoveFolder
        AddHandler btnShareParams.Click, AddressOf OnShareConfigureFolder
        Tab_Page_6.Controls.Add(btnShareAddCurrent)
        Tab_Page_6.Controls.Add(btnShareAdd)
        Tab_Page_6.Controls.Add(btnShareRemove)
        Tab_Page_6.Controls.Add(btnShareParams)

        btnShareToggle = New Button With {.Left = LU(12), .Top = LU(246), .Width = LU(336), .Height = LU(34), .Font = New Font(Me.Font, FontStyle.Bold)}
        AddHandler btnShareToggle.Click, AddressOf OnShareToggle
        Tab_Page_6.Controls.Add(btnShareToggle)

        lblShareState = New Label With {.Left = LU(12), .Top = LU(284), .Width = LU(336), .Height = LU(34), .AutoSize = False}
        Tab_Page_6.Controls.Add(lblShareState)

        chkShareAutostart = New CheckBox With {.Left = LU(12), .Top = LU(322), .Width = LU(336), .Height = LU(20), .AutoSize = False}
        AddHandler chkShareAutostart.CheckedChanged, AddressOf OnShareAutostartChanged
        Tab_Page_6.Controls.Add(chkShareAutostart)

        chkShareNoPassword = New CheckBox With {.Left = LU(12), .Top = LU(344), .Width = LU(336), .Height = LU(20), .AutoSize = False}
        AddHandler chkShareNoPassword.CheckedChanged, AddressOf OnShareNoPasswordChanged
        Tab_Page_6.Controls.Add(chkShareNoPassword)

        lnkShareAndroid = New LinkLabel With {.Left = LU(12), .Top = LU(368), .Width = LU(336), .Height = LU(30), .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 0.9F)}
        AddHandler lnkShareAndroid.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite(Is_Russian_Language))
        Tab_Page_6.Controls.Add(lnkShareAndroid)

        ' ---- right column: inner TabControl (x=356, w=312) ----
        shareInnerTabs = New TabControl With {.Left = LU(356), .Top = LU(34), .Width = LU(312), .Height = LU(356)}
        Tab_Page_6.Controls.Add(shareInnerTabs)

        tpShareLan = New TabPage With {.UseVisualStyleBackColor = True, .Padding = New Padding(6)}
        tpShareNet = New TabPage With {.UseVisualStyleBackColor = True, .Padding = New Padding(6)}
        shareInnerTabs.TabPages.Add(tpShareLan)
        shareInnerTabs.TabPages.Add(tpShareNet)

        ' Primary QR tab: the ONE combined config (LAN + internet) by default.
        ' Positions are packed to fit the ~332px inner-tab client height (the QR
        ' is a touch smaller than before to make room for the "LAN only" toggle;
        ' click-to-zoom covers the denser combined-config code).
        picShareQrLan = New PictureBox With {.Left = LU(74), .Top = LU(8), .Width = LU(148), .Height = LU(148),
            .BorderStyle = BorderStyle.FixedSingle, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}
        lblShareLanAddr = New Label With {.Left = LU(8), .Top = LU(160), .Width = LU(216), .Height = LU(18), .AutoEllipsis = True, .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 0.8F, FontStyle.Bold)}
        btnShareTestLan = New Button With {.Left = LU(228), .Top = LU(158), .Width = LU(64), .Height = LU(22), .Enabled = False}
        btnShareCopyLan = New Button With {.Left = LU(8), .Top = LU(182), .Width = LU(150), .Height = LU(24), .Enabled = False}
        btnShareCopyCredentials = New Button With {.Left = LU(162), .Top = LU(182), .Width = LU(130), .Height = LU(24), .Enabled = False}
        lblShareFinger = New Label With {.Left = LU(8), .Top = LU(210), .Width = LU(284), .Height = LU(28), .ForeColor = Color.DimGray, .AutoEllipsis = True}
        chkShareLanOnly = New CheckBox With {.Left = LU(8), .Top = LU(240), .Width = LU(284), .Height = LU(20), .AutoSize = False}
        btnShareSaveLan = New Button With {.Left = LU(8), .Top = LU(264), .Width = LU(140), .Height = LU(26), .Enabled = False}
        btnShareEmailLan = New Button With {.Left = LU(152), .Top = LU(264), .Width = LU(140), .Height = LU(26), .Enabled = False}
        lblShareLanHint = New Label With {.Left = LU(8), .Top = LU(294), .Width = LU(292), .Height = LU(36), .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .Font = New Font(Me.Font.FontFamily, Me.Font.Size * 0.78F)}
        AddHandler picShareQrLan.Click, Sub() Qr_Zoom_Form.ShowZoomed(Me, picShareQrLan)
        AddHandler btnShareTestLan.Click, AddressOf OnShareTestLan
        AddHandler btnShareCopyLan.Click, AddressOf OnShareCopyLan
        AddHandler btnShareCopyCredentials.Click, AddressOf OnShareCopyCredentials
        AddHandler chkShareLanOnly.CheckedChanged, AddressOf OnShareLanOnlyChanged
        AddHandler btnShareSaveLan.Click, Sub() SaveShareConfig(If(_cfgPrimary, Nothing))
        AddHandler btnShareEmailLan.Click, Sub() EmailShareConfig(If(_cfgPrimary, Nothing))
        tpShareLan.Controls.AddRange(New Control() {picShareQrLan, lblShareLanAddr, btnShareTestLan, btnShareCopyLan, btnShareCopyCredentials, lblShareFinger, chkShareLanOnly, btnShareSaveLan, btnShareEmailLan, lblShareLanHint})

        ' Internet-setup guidance tab: no QR / Save / Email (the primary tab owns
        ' the export). Just the reachability status, router links and the
        ' step-by-step port-forward instructions.
        lblShareNet = New Label With {.Left = LU(8), .Top = LU(6), .Width = LU(220), .Height = LU(32), .AutoSize = False, .AutoEllipsis = True}
        btnShareTestNet = New Button With {.Left = LU(230), .Top = LU(6), .Width = LU(64), .Height = LU(22), .Enabled = False}
        btnShareOpenRouter = New Button With {.Left = LU(8), .Top = LU(44), .Width = LU(158), .Height = LU(26), .Enabled = False}
        lblShareRouterUrl = New Label With {.Left = LU(172), .Top = LU(48), .Width = LU(122), .Height = LU(16), .ForeColor = Color.DimGray, .AutoEllipsis = True}
        lnkShareGuide = New LinkLabel With {.Left = LU(8), .Top = LU(76), .Width = LU(96), .Height = LU(16), .Enabled = False}
        lnkShareRouterSearch = New LinkLabel With {.Left = LU(108), .Top = LU(76), .Width = LU(108), .Height = LU(16), .Enabled = False}
        lnkShareWebGuide = New LinkLabel With {.Left = LU(220), .Top = LU(76), .Width = LU(74), .Height = LU(16)}
        txtShareForward = New TextBox With {.Left = LU(8), .Top = LU(100), .Width = LU(286), .Height = LU(206),
            .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.None,
            .BackColor = tpShareNet.BackColor, .TabStop = False}
        AddHandler btnShareTestNet.Click, AddressOf OnShareTestNet
        AddHandler btnShareOpenRouter.Click, AddressOf OnShareOpenRouter
        AddHandler lnkShareGuide.LinkClicked, AddressOf OnShareOpenGuide
        AddHandler lnkShareRouterSearch.LinkClicked, AddressOf OnShareOpenRouterSearch
        AddHandler lnkShareWebGuide.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        tpShareNet.Controls.AddRange(New Control() {lblShareNet, btnShareTestNet,
            btnShareOpenRouter, lblShareRouterUrl, lnkShareGuide, lnkShareRouterSearch, lnkShareWebGuide, txtShareForward})

        Tab_Page_6.ResumeLayout(False)
        Tab_Page_6.PerformLayout()
        _shareLoading = prev
    End Sub

    ' --- localization ----------------------------------------------------------

    Private Sub LocalizeShareTab()
        If Not _shareBuilt Then Return
        Dim rus As Boolean = Is_Russian_Language

        Tab_Page_6.Text = If(rus, "Поделиться", "Share")
        lblShareIntro.Text = If(rus,
            "Делитесь папками этого ПК с телефоном Android. Отметьте папки - общий доступ включится сразу, затем справа отсканируйте один QR-код: он работает и дома, и из интернета.",
            "Share this PC's folders with your Android phone. Tick the folders and sharing starts automatically, then on the right scan the single QR code - it works both at home and from the internet.")
        lblShareFolders.Text = If(rus, "Папки (галочка = видна в сети):", "Folders (checked = visible on the network):")
        lvShareFolders.Columns(0).Text = If(rus, "Папка", "Folder")
        lvShareFolders.Columns(1).Text = If(rus, "Путь", "Path")
        btnShareAddCurrent.Text = If(rus, "+ Текущая", "+ Current")
        btnShareAdd.Text = If(rus, "Ещё..", "More..")
        btnShareRemove.Text = If(rus, "Убрать", "Remove")
        btnShareParams.Text = If(rus, "Настроить..", "Options..")
        chkShareAutostart.Text = If(rus, "Запускать общий доступ при входе в систему", "Start sharing at logon")
        chkShareNoPassword.Text = ShareText.NoPasswordText(rus)
        lnkShareAndroid.Text = If(rus, "FastMediaSorter для Android ->", "FastMediaSorter for Android ->")

        tpShareLan.Text = If(rus, "Код для телефона", "QR for phone")
        tpShareNet.Text = If(rus, "Доступ из интернета", "Internet access")
        btnShareTestLan.Text = If(rus, "Тест", "Test")
        btnShareTestNet.Text = If(rus, "Тест", "Test")
        btnShareCopyLan.Text = If(rus, "Скопировать адрес", "Copy address")
        btnShareCopyCredentials.Text = If(rus, "Логин/пароль", "Login/pass")
        chkShareLanOnly.Text = If(rus, "Только локальная сеть", "LAN only")
        btnShareSaveLan.Text = If(rus, "Сохранить .fmscfg..", "Save .fmscfg..")
        btnShareEmailLan.Text = If(rus, "По почте..", "Email..")
        btnShareOpenRouter.Text = If(rus, "Открыть роутер", "Open router")
        lnkShareGuide.Text = If(rus, "Инструкция..", "Guide..")
        lnkShareRouterSearch.Text = If(rus, "Для роутера..", "My router..")
        lnkShareWebGuide.Text = If(rus, "На сайте..", "Website..")

        If btnShareToggle.Text.Length = 0 Then btnShareToggle.Text = If(rus, "Начать общий доступ", "Start sharing")
        If lblShareState.Text.Length = 0 Then lblShareState.Text = If(rus, "Остановлено", "Stopped")
        If lblShareLanAddr.Text.Length = 0 Then lblShareLanAddr.Text = If(rus, "Адрес: -", "Address: -")
        If lblShareFinger.Text.Length = 0 Then lblShareFinger.Text = If(rus, "Ключ узла: -", "Host key: -")
        If lblShareNet.Text.Length = 0 Then lblShareNet.Text = If(rus, "Внешний доступ: -", "Internet: -")
    End Sub

    Private Sub InitializeShareTooltips()
        If toolTip Is Nothing OrElse Not _shareBuilt Then Return
        Dim rus As Boolean = Is_Russian_Language
        toolTip.SetToolTip(lvShareFolders, If(rus, "Галочка = папка видна на телефоне. Двойной щелчок - параметры ресурса.", "Checked = folder visible on the phone. Double-click for the resource options."))
        toolTip.SetToolTip(btnShareAddCurrent, If(rus, "Добавить папку, открытую сейчас в программе.", "Share the folder currently open in the app."))
        toolTip.SetToolTip(btnShareParams, If(rus, "Название, тип (аудиотека, фото..), PIN и другие параметры выбранной папки на телефоне.", "Name, type (audio library, photos..), PIN and other options of the selected folder on the phone."))
        toolTip.SetToolTip(chkShareNoPassword, If(rus, "Файл/QR выйдет без пароля - на телефоне его придётся ввести вручную. Пароль показывается в строке состояния, пока галочка включена.", "The file/QR goes out without the password - the phone will ask for it at import. The password is shown in the status line while this is on."))
        Dim autostartTip As String = If(rus, "Запускать общий доступ автоматически при входе в Windows.", "Start folder sharing automatically at Windows logon.")
        If AutostartManager.IsPackaged() Then
            autostartTip &= If(rus, " Управляется Windows: Параметры > Приложения > Автозагрузка.", " Managed by Windows: Settings > Apps > Startup.")
        End If
        toolTip.SetToolTip(chkShareAutostart, autostartTip)
        toolTip.SetToolTip(btnShareToggle, If(rus, "Запустить или остановить SFTP-сервер для отмеченных папок.", "Start or stop the SFTP server for the ticked folders."))
        toolTip.SetToolTip(picShareQrLan, If(rus, "Один код для дома и интернета - телефон сам выберет доступный адрес. Клик - открыть крупно.", "One code for home and internet - the phone picks whichever address is reachable. Click to enlarge."))
        toolTip.SetToolTip(chkShareLanOnly, If(rus, "Не включать внешний (интернет) адрес в код и файл - только локальная сеть. Для тех, кто не открывает доступ из интернета.", "Keep the external (internet) address out of the code and file - LAN only. For users who never open internet access."))
        toolTip.SetToolTip(btnShareTestLan, If(rus, "Проверить, что SFTP-сервер отвечает по локальному адресу.", "Check that the SFTP server answers on the local address."))
        toolTip.SetToolTip(btnShareCopyCredentials, If(rus, "Скопировать логин и пароль SFTP в буфер обмена - чтобы передать их отдельно от файла/QR.", "Copy the SFTP login and password to the clipboard - to pass them on separately from the file/QR."))
        toolTip.SetToolTip(btnShareTestNet, If(rus, "Проверить внешний адрес с этого ПК. Точный тест - с телефона по мобильной сети.", "Check the internet address from this PC. The definitive test is from the phone on mobile data."))
        toolTip.SetToolTip(btnShareEmailLan, If(rus, "Прикрепить файл .fmscfg к новому письму (почтовый клиент по умолчанию).", "Attach the .fmscfg file to a new email (default mail client)."))
        toolTip.SetToolTip(btnShareOpenRouter, If(rus, "Открыть страницу настроек роутера в браузере.", "Open the router settings page in the browser."))
        toolTip.SetToolTip(lnkShareGuide, If(rus, "Пошаговая инструкция по пробросу порта (офлайн, в браузере).", "Step-by-step port-forward guide (offline, in the browser)."))
        toolTip.SetToolTip(lnkShareRouterSearch, If(rus, "Определить модель роутера и открыть поиск инструкции для неё.", "Detect the router model and search a how-to for it."))
        toolTip.SetToolTip(lnkShareWebGuide, If(rus, "Полная инструкция на сайте Fast Media Sorter.", "Full guide on the Fast Media Sorter website."))
    End Sub

    ' --- local state -----------------------------------------------------------

    Private Sub LoadShareLocalState()
        If Not _shareBuilt Then Return
        Dim prev As Boolean = _shareLoading
        _shareLoading = True
        If _shareSettings Is Nothing Then _shareSettings = New ShareSettings()
        _shareSettings.Load()
        Dim packaged As Boolean = AutostartManager.IsPackaged()
        chkShareAutostart.Checked = AutostartManager.IsEnabled()
        chkShareAutostart.Enabled = Not packaged
        chkShareNoPassword.Checked = _shareSettings.ExcludePasswordFromExport
        chkShareLanOnly.Checked = _shareSettings.LanOnlyExport
        _shareLoading = prev
    End Sub

    ' --- tab enter -------------------------------------------------------------

    Private Async Sub OnEnterShareTab()
        If Not _shareBuilt Then Return
        LoadShareLocalState()
        Dim rus As Boolean = Is_Russian_Language

        If Not WorkerProcess.IsAvailable() Then
            lblShareState.Text = If(rus, "Компаньон не найден", "Companion not found")
            SetShareHint(If(rus,
                "Файл companion\fms-share-worker.exe не найден рядом с программой - переустановите приложение.",
                "companion\fms-share-worker.exe was not found next to the app - reinstall the application."))
            SetShareServerControlsEnabled(False)
            Return
        End If

        SetShareBusy(True)
        SetShareHint(If(rus, "Запуск компаньона..", "Starting companion.."))
        Dim st As WorkerStatus = Await ShareController.EnsureRunningAsync()
        If st Is Nothing Then
            SetShareHint(If(rus, "Не удалось связаться с компаньоном.", "Could not reach the companion worker."))
            SetShareBusy(False)
            Return
        End If

        If Not _shareListPopulated Then
            PopulateShareFolders(st.Roots)
            _shareListPopulated = True
        End If

        _shareStatus = st
        ApplyStatusToUi()
        SetShareBusy(False)
        If st.Running Then
            SetShareHint("")
        Else
            SetShareHint(If(rus, "Отметьте папку и нажмите «Начать общий доступ».", "Tick a folder and press Start sharing."))
        End If

        ' The worker computes reachability (LAN IP + UPnP/external address) on a
        ' background probe, so the first status right after a (re)start still has
        ' Reachability = Nothing and every address reads "-". Keep polling until it
        ' lands, so the address/QR/Test controls fill in on their own instead of the
        ' user having to leave and re-open the tab.
        If st.Running AndAlso st.Reachability Is Nothing Then
            Await PollReachabilityAsync()
        End If
    End Sub

    ''' <summary>Polls worker status until the async reachability probe finishes (or a
    ''' bound elapses / a newer tab-enter or server op supersedes this poll), applying
    ''' each snapshot so the addresses appear as soon as they are known.</summary>
    Private Async Function PollReachabilityAsync() As Task
        _shareReachPollGen += 1
        Dim myGen As Integer = _shareReachPollGen
        For i As Integer = 1 To 20
            Await Task.Delay(1000)
            If myGen <> _shareReachPollGen OrElse Me.IsDisposed Then Return
            Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
            If myGen <> _shareReachPollGen OrElse Me.IsDisposed Then Return
            If st Is Nothing Then Continue For
            _shareStatus = st
            ApplyStatusToUi()
            If Not st.Running OrElse st.Reachability IsNot Nothing Then Return ' resolved (or server stopped)
        Next
    End Function

    ''' <summary>Supersedes any in-flight reachability poll so a start/stop/re-enter does
    ''' not get clobbered by a late poll snapshot.</summary>
    Private Sub CancelReachabilityPoll()
        _shareReachPollGen += 1
    End Sub

    ' --- folder add / remove / check -------------------------------------------

    Private Async Sub OnShareAddCurrentFolder(sender As Object, e As EventArgs)
        If _shareBusy Then Return
        Dim cur As String = ""
        Try
            cur = If(Main_Form.Current_Folder_Path, "").Trim()
        Catch
        End Try
        If String.IsNullOrEmpty(cur) OrElse Not Directory.Exists(cur) Then
            SetShareHint(If(Is_Russian_Language, "Нет открытой папки - откройте медиафайл или папку в программе.", "No folder is open - open a media file or folder in the app first."))
            Return
        End If
        If AddShareRow(cur) Then Await ApplySharedFoldersAsync()
    End Sub

    Private Async Sub OnShareAddFolder(sender As Object, e As EventArgs)
        If _shareBusy Then Return
        Dim picked As String = ""
        Using dlg As New FolderBrowserDialog()
            dlg.Description = If(Is_Russian_Language, "Выберите папку для общего доступа", "Choose a folder to share")
            dlg.ShowNewFolderButton = False
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            picked = dlg.SelectedPath
        End Using
        If AddShareRow(picked) Then Await ApplySharedFoldersAsync()
    End Sub

    Private Async Sub OnShareRemoveFolder(sender As Object, e As EventArgs)
        If _shareBusy Then Return
        If lvShareFolders.SelectedItems.Count = 0 Then Return
        Dim removed As ListViewItem = lvShareFolders.SelectedItems(0)
        ShareRootParamsStore.RemoveFor(Convert.ToString(removed.Tag))
        lvShareFolders.Items.Remove(removed)
        Await ApplySharedFoldersAsync()
    End Sub

    ''' <summary>"Настроить.." / double-click a folder row: edit the per-root
    ''' resource params the schema v2 export carries to the phone (S1002 §9).
    ''' The destination flag flips the share's writability, so it needs a
    ''' re-share; everything else only changes the exported config.</summary>
    Private Async Sub OnShareConfigureFolder(sender As Object, e As EventArgs)
        If _shareBusy Then Return
        If lvShareFolders.SelectedItems.Count = 0 Then
            SetShareHint(If(Is_Russian_Language, "Сначала выберите папку в списке.", "Select a folder in the list first."))
            Return
        End If
        Dim it As ListViewItem = lvShareFolders.SelectedItems(0)
        Dim hostPath As String = Convert.ToString(it.Tag)
        If String.IsNullOrEmpty(hostPath) Then Return
        Dim before As ShareRootParams = ShareRootParamsStore.GetFor(hostPath)
        Using dlg As New Share_Root_Params_Form(it.Text, before)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim after As ShareRootParams = dlg.Result
            ShareRootParamsStore.SetFor(hostPath, after)
            If after.IsDestination <> before.IsDestination AndAlso it.Checked Then
                ' Only a LIVE server needs the re-share (the flag is read fresh from
                ' the store at the next start anyway) - never cold-start the server
                ' from a settings dialog after the user explicitly stopped sharing.
                Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
                If st IsNot Nothing AndAlso st.Running Then
                    Await ApplySharedFoldersAsync()
                Else
                    ApplyStatusToUi()
                End If
            Else
                ApplyStatusToUi()
            End If
        End Using
    End Sub

    ''' <summary>The §6 "exclude password" export safeguard: rebuilds both configs
    ''' without (or again with) the embedded password and, while active, surfaces
    ''' the real password in the status line so the sender can pass it on.</summary>
    Private Sub OnShareNoPasswordChanged(sender As Object, e As EventArgs)
        If _shareLoading Then Return
        If _shareSettings IsNot Nothing Then
            _shareSettings.ExcludePasswordFromExport = chkShareNoPassword.Checked
            _shareSettings.Save()
        End If
        ApplyStatusToUi()
        If chkShareNoPassword.Checked Then
            Dim pw As String = If(_shareStatus IsNot Nothing, If(_shareStatus.Password, ""), "")
            SetShareHint(ShareText.NoPasswordHint(Is_Russian_Language, pw))
        Else
            SetShareHint("")
        End If
    End Sub

    ''' <summary>"Только локальная сеть" - the deliberate privacy opt-out (S1006):
    ''' rebuild the primary QR/.fmscfg from the LAN-only config so the WAN address
    ''' is never embedded. Off (default) = the combined config that works home and
    ''' away.</summary>
    Private Sub OnShareLanOnlyChanged(sender As Object, e As EventArgs)
        If _shareLoading Then Return
        If _shareSettings IsNot Nothing Then
            _shareSettings.LanOnlyExport = chkShareLanOnly.Checked
            _shareSettings.Save()
        End If
        ApplyStatusToUi()
    End Sub

    ''' <summary>MouseDown precedes the native NM_DBLCLK, so the second click of a
    ''' row double-click can be flagged here; ItemCheck then cancels the toggle the
    ''' ListView would otherwise perform. Double-click on the checkbox itself keeps
    ''' the default toggle behavior; a single click always resets the flag.</summary>
    Private Sub OnShareListMouseDown(sender As Object, e As MouseEventArgs)
        Dim onCheckbox As Boolean = False
        Try
            onCheckbox = lvShareFolders.HitTest(e.Location).Location = ListViewHitTestLocations.StateImage
        Catch
        End Try
        _shareSuppressCheck = e.Clicks = 2 AndAlso Not onCheckbox
    End Sub

    Private Sub OnShareItemCheck(sender As Object, e As ItemCheckEventArgs)
        If _shareSuppressCheck Then
            e.NewValue = e.CurrentValue
            _shareSuppressCheck = False
        End If
    End Sub

    Private Async Sub OnShareItemChecked(sender As Object, e As ItemCheckedEventArgs)
        If _shareLoading OrElse _shareBusy Then Return
        Await ApplySharedFoldersAsync()
    End Sub

    Private Function AddShareRow(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        For Each it As ListViewItem In lvShareFolders.Items
            If String.Equals(Convert.ToString(it.Tag), path, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Dim prev As Boolean = _shareLoading
        _shareLoading = True
        Dim item As New ListViewItem(ShareFolderDisplayName(path)) With {.Checked = True}
        item.SubItems.Add(path)
        item.Tag = path
        lvShareFolders.Items.Add(item)
        _shareLoading = prev
        Return True
    End Function

    Private Async Function ApplySharedFoldersAsync() As Task
        CancelReachabilityPoll()
        SetShareBusy(True)
        Dim folders As List(Of ShareFolder) = CurrentShareFolders()
        If folders.Count = 0 Then
            Await ShareController.StopServerAsync()
            _shareStatus = Await ShareController.GetStatusAsync()
            ApplyStatusToUi()
            SetShareBusy(False)
            SetShareHint(If(Is_Russian_Language, "Отметьте хотя бы одну папку - общий доступ включится сразу.", "Tick a folder to start sharing."))
            Return
        End If
        Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(folders)
        _shareStatus = r.Status
        ApplyStatusToUi()
        SetShareBusy(False)
    End Function

    ' --- start / stop ----------------------------------------------------------

    Private Async Sub OnShareToggle(sender As Object, e As EventArgs)
        If _shareBusy Then Return
        CancelReachabilityPoll()
        Dim rus As Boolean = Is_Russian_Language
        SetShareBusy(True)
        Dim st As WorkerStatus = Await ShareController.GetStatusAsync()
        If st IsNot Nothing AndAlso st.Running Then
            Await ShareController.StopServerAsync()
            _shareStatus = Await ShareController.GetStatusAsync()
            ApplyStatusToUi()
            SetShareBusy(False)
            SetShareHint(If(rus, "Общий доступ остановлен.", "Sharing stopped."))
            Return
        End If
        If CurrentShareFolders().Count = 0 Then
            SetShareBusy(False)
            SetShareHint(If(rus, "Сначала отметьте хотя бы одну папку.", "Tick at least one folder first."))
            Return
        End If
        SetShareHint(If(rus, "Запуск сервера..", "Starting server.."))
        Dim r As ShareController.ShareResult = Await ShareController.ShareFoldersAsync(CurrentShareFolders())
        _shareStatus = r.Status
        ApplyStatusToUi()
        SetShareBusy(False)
        SetShareHint(If(r.Served, If(rus, "Готово - отсканируйте QR-код или сохраните .fmscfg.", "Ready - scan the QR code or save the .fmscfg file."),
            If(rus, "Сервер запущен, но адрес ещё не определён. Проверьте брандмауэр.", "Server started, but no address yet. Check the firewall.")))
    End Sub

    ' --- save / email / copy / router / guide ----------------------------------

    Private Sub SaveShareConfig(cfg As ShareConfigResult)
        If cfg Is Nothing OrElse String.IsNullOrEmpty(cfg.ConfigJson) Then Return
        Using dlg As New SaveFileDialog()
            dlg.Filter = "FastMediaSorter config (*.fmscfg)|*.fmscfg|All files (*.*)|*.*"
            dlg.DefaultExt = "fmscfg"
            dlg.FileName = "FastMediaSorter.fmscfg"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                File.WriteAllText(dlg.FileName, cfg.ConfigJson, New UTF8Encoding(False))
                SetShareHint((If(Is_Russian_Language, "Сохранено: ", "Saved: ")) & dlg.FileName)
            Catch ex As Exception
                SetShareHint(ex.Message)
            End Try
        End Using
    End Sub

    Private Sub EmailShareConfig(cfg As ShareConfigResult)
        If cfg Is Nothing OrElse String.IsNullOrEmpty(cfg.ConfigJson) Then Return
        Dim rus As Boolean = Is_Russian_Language
        Try
            Dim dir As String = Path.Combine(Path.GetTempPath(), "FastMediaSorter")
            Directory.CreateDirectory(dir)
            Dim p As String = Path.Combine(dir, "FastMediaSorter.fmscfg")
            File.WriteAllText(p, cfg.ConfigJson, New UTF8Encoding(False))
            Dim subject As String = If(rus, "FastMediaSorter: доступ к папкам", "FastMediaSorter: folder access")
            Dim body As String = If(rus,
                "Откройте вложение .fmscfg в приложении FastMediaSorter на Android (Добавить ресурс -> Импорт из компаньона). Файл содержит пароль - не пересылайте посторонним.",
                "Open the attached .fmscfg in the FastMediaSorter Android app (Add resource -> Import from companion). The file contains a password - do not forward it to others.")
            If Not MailSender.SendFile(p, subject, body) Then
                SetShareHint(If(rus, "Не удалось открыть почтовый клиент.", "Could not open the mail client."))
            End If
        Catch ex As Exception
            SetShareHint(ex.Message)
        End Try
    End Sub

    Private Sub OnShareCopyLan(sender As Object, e As EventArgs)
        Dim addr As String = CurrentLanAddress()
        If addr.Length = 0 Then Return
        Try
            Clipboard.SetText(addr)
            SetShareHint((If(Is_Russian_Language, "Скопировано: ", "Copied: ")) & addr)
        Catch
        End Try
    End Sub

    ''' <summary>Always-available way to get the real SFTP login/password out of the
    ''' app - independent of the "exclude password from file/QR" checkbox above,
    ''' which only ever surfaced the password in the same status label that every
    ''' other status update immediately overwrites. Copies both fields so the
    ''' sender can pass them on out-of-band (chat, voice call, ..).</summary>
    Private Sub OnShareCopyCredentials(sender As Object, e As EventArgs)
        If _shareStatus Is Nothing OrElse String.IsNullOrEmpty(_shareStatus.Password) Then Return
        Dim rus As Boolean = Is_Russian_Language
        Dim login As String = If(String.IsNullOrEmpty(_shareStatus.Username), "fms", _shareStatus.Username)
        Dim text As String = (If(rus, "Логин: ", "Login: ")) & login & Environment.NewLine &
            (If(rus, "Пароль: ", "Password: ")) & _shareStatus.Password
        Try
            Clipboard.SetText(text)
            SetShareHint(If(rus, "Скопировано: логин и пароль", "Copied: login and password"))
        Catch
        End Try
    End Sub

    ' --- reachability test -----------------------------------------------------

    ''' <summary>"Тест" next to the LAN address: probe the SFTP server on the local
    ''' IP:port. A green result here is authoritative (same subnet, no NAT).</summary>
    Private Async Sub OnShareTestLan(sender As Object, e As EventArgs)
        Dim st As WorkerStatus = _shareStatus
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        Dim host As String = If(reach IsNot Nothing, If(reach.LanAddress, ""), "")
        Dim port As Integer = If(st IsNot Nothing, st.ListenPort, 0)
        Await RunShareProbe(host, port, internet:=False)
    End Sub

    ''' <summary>"Тест" next to the internet address: probe the external host:port from
    ''' this PC. A failure is reported as inconclusive (a router without NAT loopback
    ''' can refuse its own public address from inside) - the real test is the phone on
    ''' mobile data.</summary>
    Private Async Sub OnShareTestNet(sender As Object, e As EventArgs)
        Dim st As WorkerStatus = _shareStatus
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)
        Dim host As String = If(reach IsNot Nothing, If(reach.ExternalHost, ""), "")
        Dim port As Integer = 0
        If reach IsNot Nothing Then
            port = If(reach.ExternalPort > 0, reach.ExternalPort, If(st IsNot Nothing, st.ListenPort, 0))
        End If
        Await RunShareProbe(host, port, internet:=True)
    End Sub

    Private Async Function RunShareProbe(host As String, port As Integer, internet As Boolean) As Task
        If _shareTesting Then Return
        Dim rus As Boolean = Is_Russian_Language
        If String.IsNullOrEmpty(host) OrElse port <= 0 Then
            SetShareHint(If(rus, "Адрес ещё не определён.", "No address yet."))
            Return
        End If

        _shareTesting = True
        RefreshTestButtons() ' disable both "Тест" buttons while a probe is in flight
        SetShareHint((If(rus, "Проверка ", "Testing ")) & host & ":" & port.ToString() & " ..")
        Try
            Dim res As SftpProbe.ProbeResult = Await SftpProbe.ProbeAsync(host, port)
            SetShareHint(DescribeProbe(res, host, port, internet, rus))
        Catch
            SetShareHint(If(rus, "Не удалось выполнить проверку.", "Could not run the test."))
        Finally
            _shareTesting = False
            RefreshTestButtons()
        End Try
    End Function

    Private Shared Function DescribeProbe(res As SftpProbe.ProbeResult, host As String, port As Integer,
                                          internet As Boolean, rus As Boolean) As String
        Dim ep As String = host & ":" & port.ToString()
        Select Case res
            Case SftpProbe.ProbeResult.SshOk
                Return (If(rus, "✓ SFTP-сервер доступен: ", "✓ SFTP server reachable: ")) & ep
            Case SftpProbe.ProbeResult.PortOpen
                Return (If(rus, "Порт открыт, но SFTP не ответил: ", "Port open, but no SFTP reply: ")) & ep
            Case SftpProbe.ProbeResult.Timeout, SftpProbe.ProbeResult.Refused
                If internet Then
                    Return If(rus,
                        "✗ С этого ПК не отвечает (" & ep & "). Роутер может не пускать на свой внешний адрес изнутри - проверьте с телефона по мобильной сети.",
                        "✗ No answer from this PC (" & ep & "). Your router may block reaching its own external address from inside - test from the phone on mobile data.")
                End If
                Return (If(rus, "✗ Недоступно: ", "✗ Unreachable: ")) & ep &
                    If(rus, ". Проверьте, что общий доступ включён и брандмауэр разрешает порт.",
                            ". Check sharing is on and the firewall allows the port.")
            Case Else
                Return If(rus, "Адрес некорректен.", "Invalid address.")
        End Select
    End Function

    ''' <summary>Whether the internet "Тест" has a real external host:port to probe
    ''' (server running, not behind CGNAT, external host known).</summary>
    Private Function CanTestInternet() As Boolean
        Dim st As WorkerStatus = _shareStatus
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return False
        If st.Reachability.IsCgnat Then Return False
        Return Not String.IsNullOrEmpty(st.Reachability.ExternalHost)
    End Function

    ''' <summary>Re-evaluates the enabled state of both "Тест" buttons from the current
    ''' status (single source of truth, so ApplyStatusToUi / SetShareBusy / a finishing
    ''' probe all agree).</summary>
    Private Sub RefreshTestButtons()
        If Not _shareBuilt Then Return
        btnShareTestLan.Enabled = Not _shareBusy AndAlso Not _shareTesting AndAlso CurrentLanAddress().Length > 0
        btnShareTestNet.Enabled = Not _shareBusy AndAlso Not _shareTesting AndAlso CanTestInternet()
    End Sub

    Private Sub OnShareOpenRouter(sender As Object, e As EventArgs)
        Dim url As String = NetworkInfo.DefaultGatewayUrl()
        If url.Length = 0 Then
            SetShareHint(If(Is_Russian_Language, "Не удалось определить адрес роутера.", "Could not determine the router address."))
            Return
        End If
        NetworkInfo.OpenInBrowser(url)
    End Sub

    Private Async Sub OnShareOpenGuide(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim rus As Boolean = Is_Russian_Language
        Dim port As Integer = If(_shareStatus IsNot Nothing, _shareStatus.ListenPort, 0)
        Dim reach As WorkerReachability = If(_shareStatus IsNot Nothing, _shareStatus.Reachability, Nothing)
        Dim lanIp As String = If(reach IsNot Nothing AndAlso Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        SetShareHint(If(rus, "Определяем роутер..", "Detecting router.."))
        Dim rt As RouterIdentity = Await GetShareRouterAsync()
        Dim model As String = rt.DisplayName()
        If Not ShareGuide.OpenPortForwardGuide(NetworkInfo.DefaultGatewayUrl(), port, lanIp, port, rus, model, RouterInfo.SearchUrl(rt)) Then
            SetShareHint(If(rus, "Не удалось открыть инструкцию.", "Could not open the guide."))
        Else
            SetShareHint(If(model.Length > 0, (If(rus, "Роутер: ", "Router: ")) & model, ""))
        End If
    End Sub

    Private Async Sub OnShareOpenRouterSearch(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim rus As Boolean = Is_Russian_Language
        SetShareHint(If(rus, "Определяем роутер..", "Detecting router.."))
        Dim rt As RouterIdentity = Await GetShareRouterAsync()
        NetworkInfo.OpenInBrowser(RouterInfo.SearchUrl(rt))
        SetShareHint(If(rt.DisplayName().Length > 0, (If(rus, "Роутер: ", "Router: ")) & rt.DisplayName(),
            If(rus, "Модель не определена - открыт общий поиск.", "Model unknown - opened a general search.")))
    End Sub

    Private Async Function GetShareRouterAsync() As Task(Of RouterIdentity)
        If _shareRouter Is Nothing Then _shareRouter = Await Task.Run(Function() RouterInfo.Detect())
        Return _shareRouter
    End Function

    ' --- autostart -------------------------------------------------------------

    Private Sub OnShareAutostartChanged(sender As Object, e As EventArgs)
        If _shareLoading Then Return
        If AutostartManager.IsPackaged() Then Return
        Dim ok As Boolean = AutostartManager.SetEnabled(chkShareAutostart.Checked)
        If _shareSettings IsNot Nothing Then
            _shareSettings.AutostartEnabled = chkShareAutostart.Checked
            _shareSettings.Save()
        End If
        If Not ok Then
            SetShareHint(If(Is_Russian_Language, "Не удалось изменить автозапуск.", "Could not update autostart."))
            Dim prev As Boolean = _shareLoading
            _shareLoading = True
            chkShareAutostart.Checked = AutostartManager.IsEnabled()
            _shareLoading = prev
        End If
    End Sub

    ' --- UI update -------------------------------------------------------------

    Private Function CurrentShareFolders() As List(Of ShareFolder)
        Dim list As New List(Of ShareFolder)
        For Each it As ListViewItem In lvShareFolders.Items
            If Not it.Checked Then Continue For
            Dim hostPath As String = Convert.ToString(it.Tag)
            If Not String.IsNullOrEmpty(hostPath) Then
                ' A destination root (v2 isDestination, §5) must accept writes -
                ' serve it writable; everything else stays read-only.
                Dim writable As Boolean = ShareRootParamsStore.GetFor(hostPath).IsDestination
                list.Add(New ShareFolder With {.name = it.Text, .hostPath = hostPath, .readOnly = Not writable})
            End If
        Next
        Return list
    End Function

    Private Sub PopulateShareFolders(roots As List(Of ShareFolder))
        If roots Is Nothing Then Return
        Dim prev As Boolean = _shareLoading
        _shareLoading = True
        lvShareFolders.BeginUpdate()
        lvShareFolders.Items.Clear()
        For Each r As ShareFolder In roots
            Dim host As String = If(r.hostPath, "")
            Dim item As New ListViewItem(If(String.IsNullOrEmpty(r.name), ShareFolderDisplayName(host), r.name)) With {.Checked = True}
            item.SubItems.Add(host)
            item.Tag = host
            lvShareFolders.Items.Add(item)
        Next
        lvShareFolders.EndUpdate()
        _shareLoading = prev
    End Sub

    ''' <summary>The hint under the primary QR, in priority: too big for a QR ->
    ''' file; LAN address genuinely not found -> the honest VPN/retry note; the
    ''' config carries the WAN address too -> the combined "works home and away"
    ''' note (+ security nudge); otherwise the plain LAN-only hint.</summary>
    Private Shared Function PrimaryHintText(cfg As ShareConfigResult, rus As Boolean) As String
        If cfg Is Nothing Then Return ShareText.LanHintText(rus)
        If cfg.QrOverflow Then Return ShareText.QrOverflowText(rus)
        If cfg.LanDisplay.Length = 0 Then Return ShareText.LanUnknownText(rus)
        ' The worker's honest reachability line (dead forward / CGNAT / IPv6-only /
        ' confirmed / LAN-only) is the most actionable hint - prefer it once known.
        If Not String.IsNullOrEmpty(cfg.AccessNote) Then Return cfg.AccessNote
        If cfg.HasExternal Then Return ShareText.CombinedHintText(rus)
        Return ShareText.LanHintText(rus)
    End Function

    Private Sub ApplyStatusToUi()
        Dim rus As Boolean = Is_Russian_Language
        Dim st As WorkerStatus = _shareStatus
        Dim running As Boolean = st IsNot Nothing AndAlso st.Running

        If running Then
            btnShareToggle.Text = If(rus, "Остановить общий доступ", "Stop sharing")
        Else
            btnShareToggle.Text = If(rus, "Начать общий доступ", "Start sharing")
        End If

        ' Build both configs (LAN-only + combined LAN+internet) from the one
        ' status. The primary export is the combined build unless the user ticked
        ' "LAN only" (S1006 - the phone races the paths, so one scan works home
        ' and away; the WAN entry is only present when a usable path exists).
        Dim incPw As Boolean = _shareSettings Is Nothing OrElse Not _shareSettings.ExcludePasswordFromExport
        _cfgLan = If(running, ShareConfigBuilder.Build(st, False, incPw), Nothing)
        _cfgNet = If(running, ShareConfigBuilder.Build(st, True, incPw), Nothing)
        Dim lanOnly As Boolean = chkShareLanOnly IsNot Nothing AndAlso chkShareLanOnly.Checked
        _cfgPrimary = If(lanOnly, _cfgLan, _cfgNet)

        ' Primary QR tab
        Dim addr As String = CurrentLanAddress()
        lblShareLanAddr.Text = (If(rus, "Адрес: ", "Address: ")) & If(addr.Length > 0, addr, "-")
        btnShareCopyLan.Enabled = addr.Length > 0 AndAlso Not _shareBusy
        btnShareCopyCredentials.Enabled = running AndAlso st IsNot Nothing AndAlso Not String.IsNullOrEmpty(st.Password) AndAlso Not _shareBusy
        Dim fp As String = If(st IsNot Nothing, If(st.Fingerprint, ""), "")
        lblShareFinger.Text = (If(rus, "Ключ узла: ", "Host key: ")) & If(fp.Length > 0, fp, "-")
        ShowQr(picShareQrLan, _cfgPrimary)
        ' Stopped = no config/QR yet. Say "press Start" plainly instead of the
        ' "works on Wi-Fi, nothing to configure" hint, which reads as "ready"
        ' next to an empty QR box (the exact confusion a user reported).
        lblShareLanHint.Text = If(running, PrimaryHintText(_cfgPrimary, rus),
            If(rus, "Нажмите «Начать общий доступ», чтобы получить QR-код.", "Press 'Start sharing' to get the QR code."))
        ' The box is too small for the longer hints (e.g. CombinedHintText's
        ' security nudge) even at the smaller hint font - AutoEllipsis keeps the
        ' box tidy, but the full text stays one hover away.
        toolTip.SetToolTip(lblShareLanHint, lblShareLanHint.Text)
        btnShareSaveLan.Enabled = _cfgPrimary IsNot Nothing AndAlso Not _shareBusy
        btnShareEmailLan.Enabled = _cfgPrimary IsNot Nothing AndAlso Not _shareBusy

        ' Internet-setup guidance tab (owns no QR/Save/Email now)
        UpdateInternetUi()
        RefreshTestButtons()

        SetShareServerControlsEnabled(WorkerProcess.IsAvailable())

        ' Nudge the tray indicator to match the new server state at once (the poll
        ' would catch it within a few seconds anyway).
        Try : Main_Form.RefreshShareTray() : Catch : End Try
    End Sub

    Private Sub UpdateInternetUi()
        If Not _shareBuilt Then Return
        Dim rus As Boolean = Is_Russian_Language
        Dim st As WorkerStatus = _shareStatus
        Dim running As Boolean = st IsNot Nothing AndAlso st.Running
        Dim reach As WorkerReachability = If(st IsNot Nothing, st.Reachability, Nothing)

        Dim router As String = NetworkInfo.DefaultGatewayIp()
        lblShareRouterUrl.Text = If(router.Length > 0, "http://" & router, "-")
        btnShareOpenRouter.Enabled = running AndAlso router.Length > 0
        lnkShareGuide.Enabled = running
        lnkShareRouterSearch.Enabled = running

        If Not running Then
            lblShareNet.Text = If(rus, "Запустите общий доступ, чтобы настроить интернет.", "Start sharing to set up internet access.")
            txtShareForward.Text = ""
            Return
        End If
        If reach Is Nothing Then
            lblShareNet.Text = If(rus, "Определяем внешний адрес..", "Detecting the external address..")
            txtShareForward.Text = ""
            Return
        End If

        Dim port As Integer = st.ListenPort
        Dim lanIp As String = If(Not String.IsNullOrEmpty(reach.LanAddress), reach.LanAddress, NetworkInfo.LocalIPv4())
        Dim extHost As String = If(reach.ExternalHost, "")
        Dim extPort As Integer = reach.ExternalPort
        Dim isCgnat As Boolean = reach.IsCgnat
        Dim mapped As Boolean = Not String.IsNullOrEmpty(reach.PortMapMethod)
        Dim routerText As String = If(router.Length > 0, "http://" & router, If(rus, "адрес роутера", "the router address"))
        Dim lanText As String = If(lanIp.Length > 0, lanIp, If(rus, "IP этого ПК", "this PC's IP"))

        ' The QR-overflow / LAN-missing notes belong to the primary QR tab's hint
        ' (PrimaryHintText) now; this guidance tab leads with the security warning.
        Dim sb As New StringBuilder()
        sb.AppendLine(ShareText.SecurityText(rus)).AppendLine()
        If isCgnat Then
            lblShareNet.Text = If(rus, "За CGNAT - извне недоступно.", "Behind CGNAT - not reachable from outside.")
            sb.Append(ShareText.CgnatText(rus))
        ElseIf mapped Then
            lblShareNet.Text = (If(rus, "Доступно из интернета: ", "Reachable from internet: ")) & extHost & ":" & (If(extPort > 0, extPort, port)).ToString()
            If reach.ExternalVerified Then
                sb.Append(If(rus,
                    "Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg. Учтите: это не подтверждает работу извне - точный тест только с телефона по мобильной сети. При долгой работе общего доступа проверка может устареть; если телефон не подключается, отключите и снова включите общий доступ.",
                    "The port was opened automatically (UPnP) - no router setup needed. The address is already in the QR code and .fmscfg file. Note: this does not confirm it actually works from outside - the definitive test is from the phone on mobile data. Long-running sessions can go stale; if the phone can't connect, turn sharing off and back on."))
            Else
                sb.Append(ShareText.ExternalUnverifiedText(rus))
            End If
        ElseIf extHost.Length > 0 Then
            lblShareNet.Text = (If(rus, "Внешний адрес: ", "External address: ")) & extHost & ":" & port.ToString() & If(rus, " (нужен проброс порта)", " (needs port forwarding)")
            sb.Append(ShareText.PortForwardText(rus, routerText, port, lanText, port))
        Else
            lblShareNet.Text = If(rus, "Внешний адрес неизвестен - узнайте в роутере.", "External address unknown - check the router.")
            sb.Append(ShareText.PortForwardText(rus, routerText, port, lanText, port))
        End If
        txtShareForward.Text = sb.ToString()
    End Sub

    Private Function CurrentLanAddress() As String
        Dim st As WorkerStatus = _shareStatus
        If st Is Nothing OrElse Not st.Running OrElse st.Reachability Is Nothing Then Return ""
        Dim lan As String = If(st.Reachability.LanAddress, "")
        If lan.Length = 0 Then Return ""
        Return lan & ":" & st.ListenPort.ToString()
    End Function

    Private Sub ShowQr(box As PictureBox, cfg As ShareConfigResult)
        Dim newImg As Image = Nothing
        Try
            If cfg IsNot Nothing AndAlso cfg.QrPng IsNot Nothing AndAlso cfg.QrPng.Length > 0 Then
                Using ms As New MemoryStream(cfg.QrPng)
                    Using tmp As Image = Image.FromStream(ms)
                        newImg = New Bitmap(tmp)
                    End Using
                End Using
            End If
        Catch
            newImg = Nothing
        End Try
        Dim old As Image = box.Image
        box.Image = newImg
        If old IsNot Nothing Then old.Dispose()
        box.Cursor = If(newImg IsNot Nothing, Cursors.Hand, Cursors.Default) ' click = zoom window
    End Sub

    Private Sub SetShareHint(text As String)
        lblShareState.Text = text
    End Sub

    Private Sub SetShareBusy(value As Boolean)
        _shareBusy = value
        Dim avail As Boolean = WorkerProcess.IsAvailable()
        btnShareToggle.Enabled = Not value AndAlso avail
        btnShareAddCurrent.Enabled = Not value AndAlso avail
        btnShareAdd.Enabled = Not value AndAlso avail
        btnShareRemove.Enabled = Not value AndAlso avail
        btnShareParams.Enabled = Not value AndAlso avail
        lvShareFolders.Enabled = Not value AndAlso avail
        btnShareCopyLan.Enabled = Not value AndAlso CurrentLanAddress().Length > 0
        btnShareSaveLan.Enabled = Not value AndAlso _cfgPrimary IsNot Nothing
        btnShareEmailLan.Enabled = Not value AndAlso _cfgPrimary IsNot Nothing
        RefreshTestButtons()
        Me.UseWaitCursor = value
    End Sub

    Private Sub SetShareServerControlsEnabled(enabled As Boolean)
        btnShareToggle.Enabled = enabled AndAlso Not _shareBusy
        btnShareAddCurrent.Enabled = enabled AndAlso Not _shareBusy
        btnShareAdd.Enabled = enabled AndAlso Not _shareBusy
        btnShareRemove.Enabled = enabled AndAlso Not _shareBusy
        btnShareParams.Enabled = enabled AndAlso Not _shareBusy
        lvShareFolders.Enabled = enabled AndAlso Not _shareBusy
    End Sub

    Private Shared Function ShareFolderDisplayName(path As String) As String
        If String.IsNullOrEmpty(path) Then Return ""
        Try
            Dim name As String = New DirectoryInfo(path).Name
            If Not String.IsNullOrEmpty(name) Then Return name
        Catch
        End Try
        Return path
    End Function

End Class
