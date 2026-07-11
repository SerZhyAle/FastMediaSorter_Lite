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
' advertises. Controls use Top|Left anchoring (absolute) so nothing drifts when
' the sizable Settings window is widened. This TabPage is created fully in code
' (Tab_Page_6). (Class-level XML doc lives on Table_Form.Ocr.vb.)
Partial Public Class Table_Form

    Private Tab_Page_6 As TabPage

    Private _shareBuilt As Boolean
    Private _shareLoading As Boolean
    Private _shareBusy As Boolean
    Private _shareSettings As ShareSettings
    Private _shareListPopulated As Boolean

    Private _shareStatus As WorkerStatus
    Private _cfgLan As ShareConfigResult
    Private _cfgNet As ShareConfigResult
    Private _shareRouter As RouterIdentity

    ' Common (left column)
    Private lblShareIntro As Label
    Private lblShareFolders As Label
    Private lvShareFolders As ListView
    Private btnShareAddCurrent As Button
    Private btnShareAdd As Button
    Private btnShareRemove As Button
    Private btnShareToggle As Button
    Private lblShareState As Label
    Private chkShareAutostart As CheckBox
    Private lblShareAutostartNote As Label
    Private lnkShareAndroid As LinkLabel

    ' Inner TabControl (right column)
    Private shareInnerTabs As TabControl
    Private tpShareLan As TabPage
    Private tpShareNet As TabPage

    ' LAN inner tab
    Private picShareQrLan As PictureBox
    Private lblShareLanAddr As Label
    Private btnShareCopyLan As Button
    Private lblShareFinger As Label
    Private btnShareSaveLan As Button
    Private btnShareEmailLan As Button
    Private lblShareLanHint As Label

    ' Internet inner tab
    Private lblShareNet As Label
    Private picShareQrNet As PictureBox
    Private btnShareSaveNet As Button
    Private btnShareEmailNet As Button
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

        lblShareIntro = New Label With {.Left = 12, .Top = 6, .Width = 650, .Height = 30, .AutoSize = False}
        Tab_Page_6.Controls.Add(lblShareIntro)

        ' ---- left column: folders + server control (x=12, w=336) ----
        lblShareFolders = New Label With {.Left = 12, .Top = 40, .Width = 336, .Height = 16, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
        Tab_Page_6.Controls.Add(lblShareFolders)

        lvShareFolders = New ListView With {.Left = 12, .Top = 58, .Width = 336, .Height = 150,
            .View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False, .CheckBoxes = True}
        lvShareFolders.Columns.Add("", 150)
        lvShareFolders.Columns.Add("", 178)
        AddHandler lvShareFolders.ItemChecked, AddressOf OnShareItemChecked
        Tab_Page_6.Controls.Add(lvShareFolders)

        btnShareAddCurrent = New Button With {.Left = 12, .Top = 212, .Width = 150, .Height = 27}
        btnShareAdd = New Button With {.Left = 166, .Top = 212, .Width = 78, .Height = 27}
        btnShareRemove = New Button With {.Left = 248, .Top = 212, .Width = 100, .Height = 27}
        AddHandler btnShareAddCurrent.Click, AddressOf OnShareAddCurrentFolder
        AddHandler btnShareAdd.Click, AddressOf OnShareAddFolder
        AddHandler btnShareRemove.Click, AddressOf OnShareRemoveFolder
        Tab_Page_6.Controls.Add(btnShareAddCurrent)
        Tab_Page_6.Controls.Add(btnShareAdd)
        Tab_Page_6.Controls.Add(btnShareRemove)

        btnShareToggle = New Button With {.Left = 12, .Top = 246, .Width = 336, .Height = 34, .Font = New Font(Me.Font, FontStyle.Bold)}
        AddHandler btnShareToggle.Click, AddressOf OnShareToggle
        Tab_Page_6.Controls.Add(btnShareToggle)

        lblShareState = New Label With {.Left = 12, .Top = 284, .Width = 336, .Height = 34, .AutoSize = False}
        Tab_Page_6.Controls.Add(lblShareState)

        chkShareAutostart = New CheckBox With {.Left = 12, .Top = 322, .Width = 336, .Height = 20, .AutoSize = False}
        AddHandler chkShareAutostart.CheckedChanged, AddressOf OnShareAutostartChanged
        Tab_Page_6.Controls.Add(chkShareAutostart)

        lblShareAutostartNote = New Label With {.Left = 28, .Top = 344, .Width = 320, .Height = 16, .ForeColor = Color.DimGray, .AutoEllipsis = True, .Visible = False}
        Tab_Page_6.Controls.Add(lblShareAutostartNote)

        lnkShareAndroid = New LinkLabel With {.Left = 12, .Top = 368, .Width = 336, .Height = 18}
        AddHandler lnkShareAndroid.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.AndroidSite(Is_Russian_Language))
        Tab_Page_6.Controls.Add(lnkShareAndroid)

        ' ---- right column: inner TabControl (x=356, w=312) ----
        shareInnerTabs = New TabControl With {.Left = 356, .Top = 34, .Width = 312, .Height = 356}
        Tab_Page_6.Controls.Add(shareInnerTabs)

        tpShareLan = New TabPage With {.UseVisualStyleBackColor = True, .Padding = New Padding(6)}
        tpShareNet = New TabPage With {.UseVisualStyleBackColor = True, .Padding = New Padding(6)}
        shareInnerTabs.TabPages.Add(tpShareLan)
        shareInnerTabs.TabPages.Add(tpShareNet)

        ' LAN inner tab
        picShareQrLan = New PictureBox With {.Left = 66, .Top = 8, .Width = 164, .Height = 164,
            .BorderStyle = BorderStyle.FixedSingle, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}
        lblShareLanAddr = New Label With {.Left = 8, .Top = 178, .Width = 284, .Height = 18, .AutoEllipsis = True, .Font = New Font(Me.Font, FontStyle.Bold)}
        btnShareCopyLan = New Button With {.Left = 8, .Top = 200, .Width = 150, .Height = 26, .Enabled = False}
        lblShareFinger = New Label With {.Left = 8, .Top = 230, .Width = 284, .Height = 30, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        btnShareSaveLan = New Button With {.Left = 8, .Top = 264, .Width = 140, .Height = 28, .Enabled = False}
        btnShareEmailLan = New Button With {.Left = 152, .Top = 264, .Width = 140, .Height = 28, .Enabled = False}
        lblShareLanHint = New Label With {.Left = 8, .Top = 296, .Width = 284, .Height = 30, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        AddHandler btnShareCopyLan.Click, AddressOf OnShareCopyLan
        AddHandler btnShareSaveLan.Click, Sub() SaveShareConfig(If(_cfgLan, Nothing))
        AddHandler btnShareEmailLan.Click, Sub() EmailShareConfig(If(_cfgLan, Nothing))
        tpShareLan.Controls.AddRange(New Control() {picShareQrLan, lblShareLanAddr, btnShareCopyLan, lblShareFinger, btnShareSaveLan, btnShareEmailLan, lblShareLanHint})

        ' Internet inner tab
        lblShareNet = New Label With {.Left = 8, .Top = 6, .Width = 288, .Height = 32, .AutoSize = False, .AutoEllipsis = True}
        picShareQrNet = New PictureBox With {.Left = 8, .Top = 42, .Width = 120, .Height = 120,
            .BorderStyle = BorderStyle.FixedSingle, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.White}
        btnShareSaveNet = New Button With {.Left = 136, .Top = 42, .Width = 158, .Height = 26, .Enabled = False}
        btnShareEmailNet = New Button With {.Left = 136, .Top = 72, .Width = 158, .Height = 26, .Enabled = False}
        btnShareOpenRouter = New Button With {.Left = 136, .Top = 102, .Width = 158, .Height = 26, .Enabled = False}
        lblShareRouterUrl = New Label With {.Left = 136, .Top = 132, .Width = 158, .Height = 16, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        lnkShareGuide = New LinkLabel With {.Left = 8, .Top = 168, .Width = 96, .Height = 16, .Enabled = False}
        lnkShareRouterSearch = New LinkLabel With {.Left = 108, .Top = 168, .Width = 108, .Height = 16, .Enabled = False}
        lnkShareWebGuide = New LinkLabel With {.Left = 220, .Top = 168, .Width = 74, .Height = 16}
        txtShareForward = New TextBox With {.Left = 8, .Top = 188, .Width = 286, .Height = 118,
            .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.None,
            .BackColor = tpShareNet.BackColor, .TabStop = False}
        AddHandler btnShareSaveNet.Click, Sub() SaveShareConfig(If(_cfgNet, Nothing))
        AddHandler btnShareEmailNet.Click, Sub() EmailShareConfig(If(_cfgNet, Nothing))
        AddHandler btnShareOpenRouter.Click, AddressOf OnShareOpenRouter
        AddHandler lnkShareGuide.LinkClicked, AddressOf OnShareOpenGuide
        AddHandler lnkShareRouterSearch.LinkClicked, AddressOf OnShareOpenRouterSearch
        AddHandler lnkShareWebGuide.LinkClicked, Sub() NetworkInfo.OpenInBrowser(ShareGuide.SiteGuideUrl)
        tpShareNet.Controls.AddRange(New Control() {lblShareNet, picShareQrNet, btnShareSaveNet, btnShareEmailNet,
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
            "Делитесь папками этого ПК с телефоном Android. Отметьте папки - общий доступ включится сразу, затем на вкладке справа отсканируйте QR-код или сохраните файл .fmscfg.",
            "Share this PC's folders with your Android phone. Tick the folders and sharing starts automatically, then on the right tab scan the QR code or save the .fmscfg file.")
        lblShareFolders.Text = If(rus, "Папки (галочка = видна в сети):", "Folders (checked = visible on the network):")
        lvShareFolders.Columns(0).Text = If(rus, "Папка", "Folder")
        lvShareFolders.Columns(1).Text = If(rus, "Путь", "Path")
        btnShareAddCurrent.Text = If(rus, "+ Текущая папка", "+ Current folder")
        btnShareAdd.Text = If(rus, "Другую..", "Other..")
        btnShareRemove.Text = If(rus, "Убрать", "Remove")
        chkShareAutostart.Text = If(rus, "Запускать общий доступ при входе в систему", "Start sharing at logon")
        lblShareAutostartNote.Text = If(rus, "Управляется Windows: Параметры > Приложения > Автозагрузка", "Managed by Windows: Settings > Apps > Startup")
        lnkShareAndroid.Text = If(rus, "Приложение FastMediaSorter для Android ->", "FastMediaSorter app for Android ->")

        tpShareLan.Text = If(rus, "Локальная сеть", "Local network")
        tpShareNet.Text = If(rus, "Из интернета", "Internet")
        btnShareCopyLan.Text = If(rus, "Скопировать адрес", "Copy address")
        btnShareSaveLan.Text = If(rus, "Сохранить .fmscfg..", "Save .fmscfg..")
        btnShareEmailLan.Text = If(rus, "По почте..", "Email..")
        lblShareLanHint.Text = If(rus, "Работает на телефоне в той же сети Wi-Fi. Ничего настраивать не нужно.", "Works on a phone on the same Wi-Fi. Nothing to configure.")
        btnShareSaveNet.Text = If(rus, "Сохранить .fmscfg..", "Save .fmscfg..")
        btnShareEmailNet.Text = If(rus, "По почте..", "Email..")
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
        toolTip.SetToolTip(lvShareFolders, If(rus, "Галочка = папка видна на телефоне (только чтение). Снимите галочку, чтобы скрыть.", "Checked = folder visible on the phone (read-only). Uncheck to hide it."))
        toolTip.SetToolTip(btnShareAddCurrent, If(rus, "Добавить папку, открытую сейчас в программе.", "Share the folder currently open in the app."))
        toolTip.SetToolTip(btnShareToggle, If(rus, "Запустить или остановить SFTP-сервер для отмеченных папок.", "Start or stop the SFTP server for the ticked folders."))
        toolTip.SetToolTip(picShareQrLan, If(rus, "Отсканируйте в приложении на телефоне (доступ по локальной сети).", "Scan in the phone app (local-network access)."))
        toolTip.SetToolTip(picShareQrNet, If(rus, "Отсканируйте в приложении на телефоне (локальный + внешний адрес).", "Scan in the phone app (local + internet address)."))
        toolTip.SetToolTip(btnShareEmailLan, If(rus, "Прикрепить файл .fmscfg к новому письму (почтовый клиент по умолчанию).", "Attach the .fmscfg file to a new email (default mail client)."))
        toolTip.SetToolTip(btnShareEmailNet, If(rus, "Прикрепить файл .fmscfg к новому письму (почтовый клиент по умолчанию).", "Attach the .fmscfg file to a new email (default mail client)."))
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
        lblShareAutostartNote.Visible = packaged
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
        lvShareFolders.Items.Remove(lvShareFolders.SelectedItems(0))
        Await ApplySharedFoldersAsync()
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
                list.Add(New ShareFolder With {.name = it.Text, .hostPath = hostPath, .readOnly = True})
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

    Private Sub ApplyStatusToUi()
        Dim rus As Boolean = Is_Russian_Language
        Dim st As WorkerStatus = _shareStatus
        Dim running As Boolean = st IsNot Nothing AndAlso st.Running

        If running Then
            btnShareToggle.Text = If(rus, "Остановить общий доступ", "Stop sharing")
        Else
            btnShareToggle.Text = If(rus, "Начать общий доступ", "Start sharing")
        End If

        ' Build both configs (LAN-only + LAN+internet) from the one status.
        _cfgLan = If(running, ShareConfigBuilder.Build(st, False), Nothing)
        _cfgNet = If(running, ShareConfigBuilder.Build(st, True), Nothing)

        ' LAN tab
        Dim addr As String = CurrentLanAddress()
        lblShareLanAddr.Text = (If(rus, "Адрес: ", "Address: ")) & If(addr.Length > 0, addr, "-")
        btnShareCopyLan.Enabled = addr.Length > 0 AndAlso Not _shareBusy
        Dim fp As String = If(st IsNot Nothing, If(st.Fingerprint, ""), "")
        lblShareFinger.Text = (If(rus, "Ключ узла: ", "Host key: ")) & If(fp.Length > 0, fp, "-")
        ShowQr(picShareQrLan, _cfgLan)
        btnShareSaveLan.Enabled = _cfgLan IsNot Nothing AndAlso Not _shareBusy
        btnShareEmailLan.Enabled = _cfgLan IsNot Nothing AndAlso Not _shareBusy

        ' Internet tab
        ShowQr(picShareQrNet, _cfgNet)
        Dim hasNet As Boolean = _cfgNet IsNot Nothing AndAlso _cfgNet.HasExternal
        btnShareSaveNet.Enabled = hasNet AndAlso Not _shareBusy
        btnShareEmailNet.Enabled = hasNet AndAlso Not _shareBusy
        UpdateInternetUi()

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

        Dim sb As New StringBuilder()
        sb.AppendLine(ShareText.SecurityText(rus)).AppendLine()
        If isCgnat Then
            lblShareNet.Text = If(rus, "За CGNAT - извне недоступно.", "Behind CGNAT - not reachable from outside.")
            sb.Append(ShareText.CgnatText(rus))
        ElseIf mapped Then
            lblShareNet.Text = (If(rus, "Доступно из интернета: ", "Reachable from internet: ")) & extHost & ":" & (If(extPort > 0, extPort, port)).ToString()
            sb.Append(If(rus,
                "Порт открыт автоматически (UPnP) - настраивать роутер не нужно. Адрес уже в QR-коде и файле .fmscfg.",
                "The port was opened automatically (UPnP) - no router setup needed. The address is already in the QR code and .fmscfg file."))
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
        lvShareFolders.Enabled = Not value AndAlso avail
        btnShareCopyLan.Enabled = Not value AndAlso CurrentLanAddress().Length > 0
        btnShareSaveLan.Enabled = Not value AndAlso _cfgLan IsNot Nothing
        btnShareEmailLan.Enabled = Not value AndAlso _cfgLan IsNot Nothing
        btnShareSaveNet.Enabled = Not value AndAlso _cfgNet IsNot Nothing AndAlso _cfgNet.HasExternal
        btnShareEmailNet.Enabled = Not value AndAlso _cfgNet IsNot Nothing AndAlso _cfgNet.HasExternal
        Me.UseWaitCursor = value
    End Sub

    Private Sub SetShareServerControlsEnabled(enabled As Boolean)
        btnShareToggle.Enabled = enabled AndAlso Not _shareBusy
        btnShareAddCurrent.Enabled = enabled AndAlso Not _shareBusy
        btnShareAdd.Enabled = enabled AndAlso Not _shareBusy
        btnShareRemove.Enabled = enabled AndAlso Not _shareBusy
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
