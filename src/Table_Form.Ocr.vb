Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "OCR и перевод" / "OCR &amp; translation" tab of the Settings window. This
''' replaces the former standalone OCR_Translate_Form: every OCR/translation
''' parameter now lives on the fifth tab (<see cref="Tab_Page_5"/>) of
''' <see cref="Table_Form"/>. The bulk of the parameters are split across an inner
''' two-page TabControl. "Распознавание (OCR)" (recognition) precedes
''' "Перевод" (translation), matching the processing order. The global toggles,
''' overlay opacity,
''' disk-cache and status line sit outside the inner tabs.
'''
''' The controls edit the live <see cref="OcrTranslateSettings"/> object owned by
''' Main_Form (obtained via Main_Form.EnsureOcrSettings): each change is applied
''' immediately, mirroring how the rest of this Settings window works.
''' </summary>
Partial Public Class Table_Form

    ' OCR page-mode codes, aligned with the cmbOcrMode item order.
    Private Shared ReadOnly OcrModeCodes As String() = {"auto", "block", "sparse", "line", "vertical"}

    ' Popular, translation-capable Ollama models offered when none are installed.
    Private Shared ReadOnly OcrRecommendedModels As String() = {
        "qwen2.5:3b", "gemma2:2b", "qwen2.5:1.5b", "llama3.2:3b",
        "qwen2.5", "qwen2.5:7b", "llama3.2", "gemma2", "aya", "mistral", "phi3.5"
    }

    Private _ocrBuilt As Boolean
    Private _ocrLoading As Boolean
    Private _ocrBusy As Boolean
    Private _ocrDroppedMainTopMost As Boolean
    Private _ocrSettings As OcrTranslateSettings

    Private ocrInner As TabControl
    Private ocrTabTranslate As TabPage
    Private ocrTabRecognition As TabPage

    Private chkOcrEnabled As CheckBox
    Private chkOcrAuto As CheckBox

    ' Translation page
    Private lblOcrTranslator As Label
    Private cmbOcrProvider As ComboBox
    Private lblOcrEndpoint As Label
    Private txtOcrEndpoint As TextBox
    Private lblOcrServer As Label
    Private btnOcrInstallOllama As Button
    Private btnOcrStartOllama As Button
    Private lblOcrModel As Label
    Private cmbOcrModelName As ComboBox
    Private btnOcrPullModel As Button
    Private lblOcrApi As Label
    Private txtOcrApi As TextBox
    Private lblOcrTarget As Label
    Private cmbOcrTarget As ComboBox

    ' Recognition page
    Private lblOcrSource As Label
    Private cmbOcrSource As ComboBox
    Private lblOcrQuality As Label
    Private cmbOcrQuality As ComboBox
    Private lblOcrMode As Label
    Private cmbOcrMode As ComboBox
    Private btnOcrDownload As Button

    ' Footer (shared)
    Private lblOcrOpacity As Label
    Private trkOcrOpacity As TrackBar
    Private lblOcrOpacityVal As Label
    Private chkOcrOverlayVisible As CheckBox
    Private chkOcrDisk As CheckBox
    Private lblOcrStatus As Label

    ' --- public hooks called by Table_Form / Main_Form ------------------------

    ''' <summary>Builds (once), re-localizes and reloads the OCR tab from the live
    ''' settings. Called from PrepareForDisplay.</summary>
    Private Sub PrepareOcrTabForDisplay()
        BuildOcrTabIfNeeded()
        LocalizeOcrTab()
        LoadOcrFromSettings()
    End Sub

    ''' <summary>Switches the Settings window to the OCR tab (used by the toolbar
    ''' Translate button's right-click).</summary>
    Friend Sub SelectOcrTab()
        BuildOcrTabIfNeeded()
        If Tab_Page_5 Is Nothing Then Return
        If Tab_Control.SelectedTab Is Tab_Page_5 Then
            OnEnterOcrTab()
        Else
            Tab_Control.SelectedTab = Tab_Page_5 ' fires SelectedIndexChanged -> OnEnterOcrTab
        End If
    End Sub

    Private Sub Tab_Control_SelectedIndexChanged(sender As Object, e As EventArgs) Handles Tab_Control.SelectedIndexChanged
        If Tab_Control.SelectedTab Is Tab_Page_5 Then OnEnterOcrTab()
    End Sub

    ''' <summary>Runs whenever the OCR tab becomes active: drops stale cached
    ''' results (so edits take effect on the next run), refreshes the control
    ''' values and probes Ollama for installed models / state.</summary>
    Private Sub OnEnterOcrTab()
        If Not _ocrBuilt Then Return
        Main_Form.ClearOcrResultCache()
        LoadOcrFromSettings()
        UpdateOllamaStateHint()
        RefreshInstalledOcrModels()
    End Sub

    Private Sub OcrTab_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Persist the OCR settings as soon as the Settings window closes (the rest
        ' are also saved when the app exits).
        Main_Form.SaveOcrSettings()
    End Sub

    ''' <summary>Restores the always-on-top state we dropped to let an external
    ''' window (Ollama installer / download page) show in front of us.</summary>
    Private Sub OcrTab_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        If _ocrDroppedMainTopMost Then
            _ocrDroppedMainTopMost = False
            Main_Form.ResumeAlwaysOnTop(True)
        End If
    End Sub

    ' --- build ----------------------------------------------------------------

    Private Sub BuildOcrTabIfNeeded()
        If _ocrBuilt Then Return
        If Tab_Page_5 Is Nothing Then Return
        _ocrBuilt = True

        Dim prev As Boolean = _ocrLoading
        _ocrLoading = True
        Tab_Page_5.SuspendLayout()

        Const labelW As Integer = 160
        Const ctlX As Integer = 180
        Const ctlW As Integer = 420

        ' Global toggles (above the inner tabs).
        chkOcrEnabled = New CheckBox With {.Left = LU(14), .Top = LU(10), .Width = LU(640), .AutoSize = True}
        chkOcrAuto = New CheckBox With {.Left = LU(14), .Top = LU(36), .Width = LU(640), .AutoSize = True}
        Tab_Page_5.Controls.Add(chkOcrEnabled)
        Tab_Page_5.Controls.Add(chkOcrAuto)
        AddHandler chkOcrEnabled.CheckedChanged, AddressOf OcrEnabled_CheckedChanged
        AddHandler chkOcrAuto.CheckedChanged, AddressOf OcrAuto_CheckedChanged

        ' Inner two-page tab control.
        ' Fixed height, anchored Top|Left|Right (NOT Bottom): the inner pages must
        ' never shrink below their content (no scrollbar), so they keep their size
        ' when the window is resized; the bottom-anchored footer still pins below.
        ocrInner = New TabControl With {.Left = LU(8), .Top = LU(64), .Width = LU(652), .Height = LU(236),
            .Anchor = CType(AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)}
        ocrTabTranslate = New TabPage With {.Padding = New Padding(4), .UseVisualStyleBackColor = True}
        ocrTabRecognition = New TabPage With {.Padding = New Padding(4), .UseVisualStyleBackColor = True}
        ' OCR precedes translation in the pipeline.
        ocrInner.Controls.Add(ocrTabRecognition)
        ocrInner.Controls.Add(ocrTabTranslate)
        Tab_Page_5.Controls.Add(ocrInner)

        ' ---- Translation page ----
        Dim y As Integer = 10
        lblOcrTranslator = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrTranslator)
        cmbOcrProvider = New ComboBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW), .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbOcrProvider.Items.AddRange(New Object() {"ollama", "libretranslate"})
        AddHandler cmbOcrProvider.SelectedIndexChanged, AddressOf OcrProvider_Changed
        ocrTabTranslate.Controls.Add(cmbOcrProvider)
        y += 32

        lblOcrEndpoint = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrEndpoint)
        txtOcrEndpoint = New TextBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW)}
        AddHandler txtOcrEndpoint.TextChanged, AddressOf OcrEndpoint_TextChanged
        ocrTabTranslate.Controls.Add(txtOcrEndpoint)
        y += 32

        lblOcrServer = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrServer)
        btnOcrInstallOllama = New Button With {.Left = LU(ctlX), .Top = LU(y - 1), .Width = LU(205), .Height = LU(25)}
        btnOcrStartOllama = New Button With {.Left = LU(ctlX + 211), .Top = LU(y - 1), .Width = LU(205), .Height = LU(25)}
        AddHandler btnOcrInstallOllama.Click, AddressOf OnInstallOllama
        AddHandler btnOcrStartOllama.Click, AddressOf OnStartOllama
        ocrTabTranslate.Controls.Add(btnOcrInstallOllama)
        ocrTabTranslate.Controls.Add(btnOcrStartOllama)
        y += 32

        lblOcrModel = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrModel)
        cmbOcrModelName = New ComboBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(330), .DropDownStyle = ComboBoxStyle.DropDown}
        btnOcrPullModel = New Button With {.Left = LU(ctlX + ctlW - 84), .Top = LU(y - 1), .Width = LU(84), .Height = LU(25)}
        AddHandler cmbOcrModelName.TextChanged, AddressOf OcrModelName_TextChanged
        AddHandler btnOcrPullModel.Click, AddressOf OnPullModel
        ocrTabTranslate.Controls.Add(cmbOcrModelName)
        ocrTabTranslate.Controls.Add(btnOcrPullModel)
        y += 32

        lblOcrApi = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrApi)
        txtOcrApi = New TextBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW), .UseSystemPasswordChar = True}
        AddHandler txtOcrApi.TextChanged, AddressOf OcrApi_TextChanged
        ocrTabTranslate.Controls.Add(txtOcrApi)
        y += 32

        lblOcrTarget = MakeOcrLabel(14, y, labelW)
        ocrTabTranslate.Controls.Add(lblOcrTarget)
        cmbOcrTarget = BuildOcrLanguageCombo(ocrTabTranslate, ctlX, y, ctlW, OcrLanguageCatalog.TargetLanguages())
        AddHandler cmbOcrTarget.SelectedIndexChanged, AddressOf OcrTarget_Changed

        ' ---- Recognition page ----
        y = 10
        lblOcrSource = MakeOcrLabel(14, y, labelW)
        ocrTabRecognition.Controls.Add(lblOcrSource)
        cmbOcrSource = BuildOcrLanguageCombo(ocrTabRecognition, ctlX, y, ctlW, OcrLanguageCatalog.SourceLanguages())
        AddHandler cmbOcrSource.SelectedIndexChanged, AddressOf OcrSource_Changed
        y += 34

        lblOcrQuality = MakeOcrLabel(14, y, labelW)
        ocrTabRecognition.Controls.Add(lblOcrQuality)
        cmbOcrQuality = New ComboBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW), .DropDownStyle = ComboBoxStyle.DropDownList}
        AddHandler cmbOcrQuality.SelectedIndexChanged, AddressOf OcrQuality_Changed
        ocrTabRecognition.Controls.Add(cmbOcrQuality)
        y += 32

        lblOcrMode = MakeOcrLabel(14, y, labelW)
        ocrTabRecognition.Controls.Add(lblOcrMode)
        cmbOcrMode = New ComboBox With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW), .DropDownStyle = ComboBoxStyle.DropDownList}
        AddHandler cmbOcrMode.SelectedIndexChanged, AddressOf OcrMode_Changed
        ocrTabRecognition.Controls.Add(cmbOcrMode)
        y += 36

        btnOcrDownload = New Button With {.Left = LU(ctlX), .Top = LU(y), .Width = LU(ctlW), .Height = LU(27)}
        AddHandler btnOcrDownload.Click, AddressOf OnDownloadOcr
        ocrTabRecognition.Controls.Add(btnOcrDownload)

        ' ---- Footer (shared, below the inner tabs) ----
        Dim botAnchor As AnchorStyles = CType(AnchorStyles.Bottom Or AnchorStyles.Left, AnchorStyles)
        lblOcrOpacity = New Label With {.Left = LU(14), .Top = LU(306), .Width = LU(labelW), .Height = LU(20),
            .TextAlign = ContentAlignment.MiddleLeft, .Anchor = botAnchor}
        ' The persisted setting remains an alpha byte (0..255) for compatibility,
        ' but people reason about opacity as a percentage. The Settings UI exposes
        ' the useful 30..100 % range and converts at the boundary.
        trkOcrOpacity = New TrackBar With {.Left = LU(ctlX), .Top = LU(302), .Width = LU(380), .Minimum = 30, .Maximum = 100, .TickFrequency = 10,
            .Anchor = CType(AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)}
        lblOcrOpacityVal = New Label With {.Left = LU(ctlX + 386), .Top = LU(310), .Width = LU(60), .Height = LU(20), .Text = "82 %",
            .Anchor = CType(AnchorStyles.Bottom Or AnchorStyles.Right, AnchorStyles)}
        AddHandler trkOcrOpacity.ValueChanged, AddressOf OcrOpacity_Changed
        Tab_Page_5.Controls.Add(lblOcrOpacity)
        Tab_Page_5.Controls.Add(trkOcrOpacity)
        Tab_Page_5.Controls.Add(lblOcrOpacityVal)

        chkOcrOverlayVisible = New CheckBox With {.Left = LU(14), .Top = LU(350), .Width = LU(400), .AutoSize = True, .Anchor = botAnchor}
        AddHandler chkOcrOverlayVisible.CheckedChanged, AddressOf OcrOverlayVisible_CheckedChanged
        Tab_Page_5.Controls.Add(chkOcrOverlayVisible)

        chkOcrDisk = New CheckBox With {.Left = LU(14), .Top = LU(374), .Width = LU(400), .AutoSize = True, .Anchor = botAnchor}
        AddHandler chkOcrDisk.CheckedChanged, AddressOf OcrDisk_CheckedChanged
        Tab_Page_5.Controls.Add(chkOcrDisk)

        lblOcrStatus = New Label With {.Left = LU(14), .Top = LU(398), .Width = LU(640), .Height = LU(18), .ForeColor = Color.DimGray, .AutoEllipsis = True,
            .Anchor = CType(AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)}
        Tab_Page_5.Controls.Add(lblOcrStatus)

        Tab_Page_5.ResumeLayout(False)
        Tab_Page_5.PerformLayout()
        _ocrLoading = prev
    End Sub

    Private Function MakeOcrLabel(left As Integer, top As Integer, width As Integer) As Label
        Return New Label With {.Left = LU(left), .Top = LU(top + 4), .Width = LU(width), .Height = LU(20),
            .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
    End Function

    Private Function BuildOcrLanguageCombo(parent As Control, left As Integer, top As Integer, width As Integer, entries As LanguageEntry()) As ComboBox
        Dim combo As New ComboBox With {
            .Left = LU(left), .Top = LU(top), .Width = LU(width),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .DrawMode = DrawMode.OwnerDrawFixed,
            .ItemHeight = LU(24),
            .MaxDropDownItems = 16}
        For Each entry As LanguageEntry In entries
            combo.Items.Add(entry)
        Next
        AddHandler combo.DrawItem, AddressOf OcrLangCombo_DrawItem
        parent.Controls.Add(combo)
        Return combo
    End Function

    Private Sub OcrLangCombo_DrawItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index < 0 Then Return

        Dim combo As ComboBox = CType(sender, ComboBox)
        Dim entry As LanguageEntry = TryCast(combo.Items(e.Index), LanguageEntry)
        If entry Is Nothing Then Return

        Dim fw As Integer = LU(22)
        Dim fh As Integer = LU(16)
        Dim iy As Integer = e.Bounds.Top + (e.Bounds.Height - fh) \ 2
        Try
            Dim img As Image = FlagImages.Get(entry.Code)
            If img IsNot Nothing Then e.Graphics.DrawImage(img, e.Bounds.Left + LU(4), iy, fw, fh)
        Catch
        End Try

        Using b As New SolidBrush(e.ForeColor)
            Using sf As New StringFormat()
                sf.LineAlignment = StringAlignment.Center
                sf.Trimming = StringTrimming.EllipsisCharacter
                sf.FormatFlags = StringFormatFlags.NoWrap
                Dim tr As New Rectangle(e.Bounds.Left + LU(4) + fw + LU(6), e.Bounds.Top, e.Bounds.Width - (fw + LU(16)), e.Bounds.Height)
                e.Graphics.DrawString(entry.DisplayName(), combo.Font, b, tr, sf)
            End Using
        End Using

        e.DrawFocusRectangle()
    End Sub

    ' --- localization ---------------------------------------------------------

    Private Sub LocalizeOcrTab()
        If Not _ocrBuilt Then Return
        Dim rus As Boolean = Is_Russian_Language
        Dim prev As Boolean = _ocrLoading
        _ocrLoading = True

        ocrTabTranslate.Text = Localization.T("Перевод")
        ocrTabRecognition.Text = Localization.T("Распознавание (OCR)")

        chkOcrEnabled.Text = Localization.T("Включить OCR и перевод")
        chkOcrAuto.Text = Localization.T("Авто-режим (после показа изображения)")

        lblOcrTranslator.Text = Localization.T("Переводчик:")
        lblOcrEndpoint.Text = Localization.T("Адрес (endpoint):")
        lblOcrServer.Text = Localization.T("Сервер Ollama:")
        btnOcrInstallOllama.Text = Localization.T("Установить Ollama")
        btnOcrStartOllama.Text = Localization.T("Запустить Ollama")
        lblOcrModel.Text = Localization.T("Модель Ollama:")
        btnOcrPullModel.Text = Localization.T("Загрузить")
        lblOcrApi.Text = Localization.T("API-ключ:")
        lblOcrTarget.Text = Localization.T("Язык перевода:")

        lblOcrSource.Text = Localization.T("Язык распознавания:")
        lblOcrQuality.Text = Localization.T("Модель OCR:")
        lblOcrMode.Text = Localization.T("Режим OCR:")
        btnOcrDownload.Text = Localization.T("Скачать пакет распознавания")

        lblOcrOpacity.Text = Localization.T("Непрозрачность:")
        chkOcrOverlayVisible.Text = Localization.T("Показывать панель перевода")
        chkOcrDisk.Text = Localization.T("Дисковый кэш результатов")

        PopulateOcrQualityItems(rus)
        PopulateOcrModeItems(rus)

        ' Owner-drawn language combos redraw with the active language.
        cmbOcrSource.Invalidate()
        cmbOcrTarget.Invalidate()

        _ocrLoading = prev
    End Sub

    Private Sub PopulateOcrQualityItems(rus As Boolean)
        Dim idx As Integer = cmbOcrQuality.SelectedIndex
        cmbOcrQuality.Items.Clear()
        cmbOcrQuality.Items.AddRange(New Object() {
            Localization.T("Быстрая (fast)"),
            Localization.T("Лучшая (best, медленнее)")})
        If idx >= 0 AndAlso idx < cmbOcrQuality.Items.Count Then cmbOcrQuality.SelectedIndex = idx
    End Sub

    Private Sub PopulateOcrModeItems(rus As Boolean)
        Dim idx As Integer = cmbOcrMode.SelectedIndex
        cmbOcrMode.Items.Clear()
        cmbOcrMode.Items.AddRange(New Object() {
            Localization.T("Авто (рекомендуется)"),
            Localization.T("Один блок"),
            Localization.T("Разреженный текст"),
            Localization.T("Одна строка"),
            Localization.T("Вертикальный текст")})
        If idx >= 0 AndAlso idx < cmbOcrMode.Items.Count Then cmbOcrMode.SelectedIndex = idx
    End Sub

    Private Sub InitializeOcrTooltips()
        BuildOcrTabIfNeeded()
        If toolTip Is Nothing OrElse Not _ocrBuilt Then Return
        Dim rus As Boolean = Is_Russian_Language
        toolTip.SetToolTip(chkOcrEnabled, Localization.T("Включить распознавание текста и перевод на изображениях - для картинок, что упорно говорят на чужом языке."))
        toolTip.SetToolTip(chkOcrAuto, Localization.T("Распознавать и переводить автоматически после каждого изображения - вы только смотрите, программа отдувается."))
        toolTip.SetToolTip(cmbOcrProvider, Localization.T("Сервис перевода: локальный Ollama или LibreTranslate."))
        toolTip.SetToolTip(txtOcrEndpoint, Localization.T("Адрес сервера перевода (оставьте пустым для значения по умолчанию)."))
        toolTip.SetToolTip(btnOcrInstallOllama, Localization.T("Скачать и установить Ollama - местного полиглота, который поселится на вашем компьютере."))
        toolTip.SetToolTip(btnOcrStartOllama, Localization.T("Запустить локальный сервер Ollama."))
        toolTip.SetToolTip(cmbOcrModelName, Localization.T("Имя модели Ollama для перевода."))
        toolTip.SetToolTip(btnOcrPullModel, Localization.T("Загрузить выбранную модель в Ollama."))
        toolTip.SetToolTip(txtOcrApi, Localization.T("Ключ API (для облачных переводчиков). Храним зашифрованным, честное слово."))
        toolTip.SetToolTip(cmbOcrTarget, Localization.T("Язык, на который переводить текст."))
        toolTip.SetToolTip(cmbOcrSource, Localization.T("Язык исходного текста на изображении (или автоопределение)."))
        toolTip.SetToolTip(cmbOcrQuality, Localization.T("Качество распознавания: быстрое или лучшее (то есть медленнее). Вечный выбор между скоростью и совестью."))
        toolTip.SetToolTip(cmbOcrMode, Localization.T("Режим разбора страницы Tesseract."))
        toolTip.SetToolTip(btnOcrDownload, Localization.T("Скачать языковой пакет распознавания для выбранного языка."))
        toolTip.SetToolTip(trkOcrOpacity, Localization.T("Непрозрачность наложения перевода - от еле заметного до полностью закрывающего оригинал."))
        toolTip.SetToolTip(chkOcrOverlayVisible, Localization.T("Показывать или скрывать уже распознанный перевод поверх изображения."))
        toolTip.SetToolTip(chkOcrDisk, Localization.T("Кэшировать результаты на диск, чтобы не распознавать одно и то же дважды."))
    End Sub

    ' --- load / apply ---------------------------------------------------------

    Private Sub LoadOcrFromSettings()
        If Not _ocrBuilt Then Return
        Dim prev As Boolean = _ocrLoading
        _ocrLoading = True

        _ocrSettings = Main_Form.EnsureOcrSettings()
        Dim s As OcrTranslateSettings = _ocrSettings

        chkOcrEnabled.Checked = s.Enabled
        chkOcrAuto.Checked = s.AutoMode
        chkOcrOverlayVisible.Checked = s.OverlayVisible
        cmbOcrProvider.SelectedIndex = If(String.Equals(s.Provider, "libretranslate", StringComparison.OrdinalIgnoreCase), 1, 0)
        txtOcrEndpoint.Text = s.Endpoint
        cmbOcrModelName.Text = s.OllamaModel
        txtOcrApi.Text = s.ApiKey
        SelectLanguageInCombo(cmbOcrSource, s.SourceLang, "auto")
        SelectLanguageInCombo(cmbOcrTarget, s.TargetLang, "en")
        cmbOcrQuality.SelectedIndex = If(String.Equals(s.OcrModelQuality, "best", StringComparison.OrdinalIgnoreCase), 1, 0)
        Dim modeIndex As Integer = Array.IndexOf(OcrModeCodes, If(s.OcrPageMode, "auto").Trim().ToLowerInvariant())
        cmbOcrMode.SelectedIndex = If(modeIndex >= 0, modeIndex, 0)
        trkOcrOpacity.Value = AlphaToOpacityPercent(s.OverlayOpacity)
        lblOcrOpacityVal.Text = trkOcrOpacity.Value.ToString() & " %"
        chkOcrDisk.Checked = s.DiskCache

        UpdateOcrProviderControls()
        _ocrLoading = prev
    End Sub

    ' --- control event handlers (write straight to the live settings) ---------

    Private Sub OcrEnabled_CheckedChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.Enabled = chkOcrEnabled.Checked
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, True)
    End Sub

    Private Sub OcrAuto_CheckedChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.AutoMode = chkOcrAuto.Checked
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, True)
    End Sub

    Private Sub OcrOverlayVisible_CheckedChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        Main_Form.SetOcrOverlayVisibility(chkOcrOverlayVisible.Checked)
    End Sub

    Private Sub OcrProvider_Changed(sender As Object, e As EventArgs)
        UpdateOcrProviderControls()
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.Provider = Convert.ToString(cmbOcrProvider.SelectedItem)
        RefreshInstalledOcrModels()
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, True)
    End Sub

    Private Sub OcrEndpoint_TextChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        Dim endpoint As String = txtOcrEndpoint.Text.Trim()
        If String.Equals(_ocrSettings.Endpoint, endpoint, StringComparison.Ordinal) Then Return
        _ocrSettings.Endpoint = endpoint
        Main_Form.ClearOcrResultCache()
    End Sub

    Private Sub OcrModelName_TextChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.OllamaModel = cmbOcrModelName.Text.Trim()
    End Sub

    Private Sub OcrApi_TextChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        If String.Equals(_ocrSettings.ApiKey, txtOcrApi.Text, StringComparison.Ordinal) Then Return
        _ocrSettings.ApiKey = txtOcrApi.Text
        Main_Form.ClearOcrResultCache()
    End Sub

    Private Sub OcrSource_Changed(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.SourceLang = SelectedLangCode(cmbOcrSource, "auto")
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, True)
    End Sub

    Private Sub OcrQuality_Changed(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.OcrModelQuality = If(cmbOcrQuality.SelectedIndex = 1, "best", "fast")
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(True, True)
    End Sub

    Private Sub OcrMode_Changed(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        Dim idx As Integer = cmbOcrMode.SelectedIndex
        _ocrSettings.OcrPageMode = If(idx >= 0 AndAlso idx < OcrModeCodes.Length, OcrModeCodes(idx), "auto")
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(True, True)
    End Sub

    Private Sub OcrTarget_Changed(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.TargetLang = SelectedLangCode(cmbOcrTarget, "en")
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, True)
    End Sub

    Private Sub OcrOpacity_Changed(sender As Object, e As EventArgs)
        lblOcrOpacityVal.Text = trkOcrOpacity.Value.ToString() & " %"
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.OverlayOpacity = OpacityPercentToAlpha(trkOcrOpacity.Value)
        Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, False)
    End Sub

    Private Shared Function AlphaToOpacityPercent(alpha As Integer) As Integer
        Dim percent As Integer = CInt(Math.Round(OcrTranslateSettings.ClampOpacity(alpha) * 100.0 / 255.0))
        Return Math.Max(30, Math.Min(100, percent))
    End Function

    Private Shared Function OpacityPercentToAlpha(percent As Integer) As Integer
        Dim clamped As Integer = Math.Max(30, Math.Min(100, percent))
        Return OcrTranslateSettings.ClampOpacity(CInt(Math.Round(clamped * 255.0 / 100.0)))
    End Function

    Private Sub OcrDisk_CheckedChanged(sender As Object, e As EventArgs)
        If _ocrLoading OrElse _ocrSettings Is Nothing Then Return
        _ocrSettings.DiskCache = chkOcrDisk.Checked
    End Sub

    ' --- provider / model state ----------------------------------------------

    Private Function IsOllamaProvider() As Boolean
        Return String.Equals(Convert.ToString(cmbOcrProvider.SelectedItem), "ollama", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub UpdateOcrProviderControls()
        Dim ollama As Boolean = IsOllamaProvider()
        cmbOcrModelName.Enabled = ollama
        btnOcrPullModel.Enabled = ollama AndAlso Not _ocrBusy
        btnOcrInstallOllama.Enabled = ollama AndAlso Not _ocrBusy
        btnOcrStartOllama.Enabled = ollama AndAlso Not _ocrBusy
    End Sub

    Private Sub UpdateOllamaStateHint()
        If Not IsOllamaProvider() Then Return
        If Not OllamaManager.IsInstalled() Then
            lblOcrStatus.Text = Localization.T("Ollama не установлен - нажмите «Установить Ollama».")
        ElseIf Not OllamaManager.IsProcessRunning() Then
            lblOcrStatus.Text = Localization.T("Ollama не запущен - нажмите «Запустить Ollama».")
        End If
    End Sub

    ''' <summary>Fills the model dropdown: installed models first, then recommended
    ''' downloads. Stays populated even when Ollama is unreachable.</summary>
    Private Async Sub RefreshInstalledOcrModels()
        If Not IsOllamaProvider() Then Return

        Dim installed As New List(Of String)
        Try
            Dim tr As New OllamaTranslator(txtOcrEndpoint.Text.Trim(), "")
            installed = Await tr.ListModelsAsync(CancellationToken.None)
        Catch
        End Try

        Dim merged As New List(Of String)
        For Each m As String In installed
            merged.Add(m)
        Next
        For Each m As String In OcrRecommendedModels
            If Not merged.Exists(Function(x) x.Equals(m, StringComparison.OrdinalIgnoreCase)) Then merged.Add(m)
        Next

        If cmbOcrModelName Is Nothing OrElse cmbOcrModelName.IsDisposed Then Return
        Dim prev As Boolean = _ocrLoading
        _ocrLoading = True
        Dim current As String = cmbOcrModelName.Text
        cmbOcrModelName.BeginUpdate()
        cmbOcrModelName.Items.Clear()
        For Each m As String In merged
            cmbOcrModelName.Items.Add(m)
        Next
        cmbOcrModelName.EndUpdate()
        cmbOcrModelName.Text = current
        _ocrLoading = prev
    End Sub

    Private Sub SetOcrBusy(value As Boolean)
        _ocrBusy = value
        btnOcrDownload.Enabled = Not value
        btnOcrPullModel.Enabled = Not value AndAlso IsOllamaProvider()
        btnOcrInstallOllama.Enabled = Not value AndAlso IsOllamaProvider()
        btnOcrStartOllama.Enabled = Not value AndAlso IsOllamaProvider()
        Me.UseWaitCursor = value
        If value Then Application.DoEvents()
    End Sub

    ''' <summary>Drops the main window's always-on-top so an external window
    ''' (installer / browser) is not hidden behind it. Restored on re-activation.</summary>
    Private Sub DropTopMostForExternal()
        ' The main window can be pinned always-on-top (persisted "keep on top"), and
        ' this window now rides the same band - so both have to step aside, not just
        ' the viewer, or the installer would simply hide behind the settings window.
        _ocrDroppedMainTopMost = Main_Form.SuspendAlwaysOnTop()
    End Sub

    ' --- install / pull / download handlers -----------------------------------

    Private Async Sub OnInstallOllama(sender As Object, e As EventArgs)
        If _ocrBusy Then Return
        If OllamaManager.IsInstalled() Then
            lblOcrStatus.Text = Localization.T("Ollama уже установлен. Нажмите «Запустить Ollama».")
            Return
        End If

        Dim confirm As String = Localization.T("Скачать и установить Ollama? Это несколько сотен МБ, загрузка может занять время.")
        If MessageBox.Show(Me, confirm, Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        SetOcrBusy(True)
        lblOcrStatus.Text = Localization.T("Скачивание Ollama..")
        Dim installerPath As String = ""
        Try
            Dim prog As New Progress(Of String)(Sub(s) lblOcrStatus.Text = (Localization.T("Скачивание Ollama: ")) & s)
            installerPath = Await OllamaManager.DownloadInstallerAsync(prog, CancellationToken.None)
        Catch ex As Exception
            lblOcrStatus.Text = ex.Message
        End Try
        SetOcrBusy(False)

        DropTopMostForExternal()

        If installerPath.Length > 0 AndAlso IO.File.Exists(installerPath) Then
            lblOcrStatus.Text = Localization.T("Запуск установщика Ollama..")
            OllamaManager.RunInstaller(installerPath)
        Else
            lblOcrStatus.Text = Localization.T("Не удалось скачать. Открываю сайт Ollama..")
            OllamaManager.OpenWebPage()
        End If
    End Sub

    Private Async Sub OnStartOllama(sender As Object, e As EventArgs)
        If _ocrBusy Then Return
        If Not OllamaManager.IsInstalled() Then
            lblOcrStatus.Text = Localization.T("Ollama не установлен - нажмите «Установить Ollama».")
            Return
        End If

        SetOcrBusy(True)
        lblOcrStatus.Text = Localization.T("Запуск Ollama..")
        OllamaManager.StartServer()

        Dim up As Boolean = False
        Dim tr As New OllamaTranslator(txtOcrEndpoint.Text.Trim(), "")
        For i As Integer = 0 To 20
            If Await tr.ProbeAsync(CancellationToken.None) Then
                up = True
                Exit For
            End If
            Await Task.Delay(500)
        Next
        SetOcrBusy(False)

        If up Then
            lblOcrStatus.Text = Localization.T("Ollama запущен.")
            RefreshInstalledOcrModels()
        Else
            lblOcrStatus.Text = Localization.T("Не удалось запустить Ollama.")
        End If
    End Sub

    Private Async Sub OnPullModel(sender As Object, e As EventArgs)
        If _ocrBusy Then Return
        Dim modelName As String = cmbOcrModelName.Text.Trim()
        If modelName.Length = 0 Then
            lblOcrStatus.Text = Localization.T("Укажите имя модели (например, llama3.2)")
            Return
        End If

        SetOcrBusy(True)
        Dim endpoint As String = txtOcrEndpoint.Text.Trim()
        Dim ok As Boolean = False
        Try
            Dim tr As New OllamaTranslator(endpoint, modelName)
            Dim prog As New Progress(Of String)(Sub(s) lblOcrStatus.Text = Localization.TC("ollama", "Загрузка: ") & s)
            ok = Await tr.PullModelAsync(modelName, prog, CancellationToken.None)
        Catch ex As Exception
            lblOcrStatus.Text = ex.Message
        End Try
        SetOcrBusy(False)

        lblOcrStatus.Text = If(ok,
            Localization.T("Модель установлена: ") & modelName,
            Localization.T("Не удалось загрузить модель (Ollama запущен?)"))
        If ok Then RefreshInstalledOcrModels()
    End Sub

    Private Async Sub OnDownloadOcr(sender As Object, e As EventArgs)
        If _ocrBusy Then Return
        Dim srcCode As String = SelectedLangCode(cmbOcrSource, "auto")
        Dim tessLangs As String = OcrTranslateSettings.TessLanguages(srcCode)

        Dim preferBest As Boolean = (cmbOcrQuality.SelectedIndex = 1)
        If Not Await OptionalRuntimeManager.EnsureOcrRuntimeInteractiveAsync(Me) Then
            lblOcrStatus.Text = Localization.T("OCR-движок не установлен.")
            Return
        End If

        SetOcrBusy(True)
        lblOcrStatus.Text = Localization.T("Скачивание языкового пакета: ") & tessLangs
        Dim ok As Boolean = False
        Try
            ok = Await Task.Run(Function() TesseractOcrEngine.EnsureLanguagesPublic(tessLangs, preferBest))
        Catch ex As Exception
            lblOcrStatus.Text = ex.Message
        End Try
        SetOcrBusy(False)

        lblOcrStatus.Text = If(ok,
            Localization.T("Готово: пакет ") & tessLangs,
            Localization.T("Не удалось скачать (нет сети?)"))
    End Sub

    ' --- language combo helpers ----------------------------------------------

    Private Shared Sub SelectLanguageInCombo(combo As ComboBox, code As String, fallback As String)
        Dim c As String = If(code, "").Trim()
        Dim fallbackIndex As Integer = 0
        For i As Integer = 0 To combo.Items.Count - 1
            Dim entry As LanguageEntry = CType(combo.Items(i), LanguageEntry)
            If String.Equals(entry.Code, c, StringComparison.OrdinalIgnoreCase) Then
                combo.SelectedIndex = i
                Return
            End If
            If String.Equals(entry.Code, fallback, StringComparison.OrdinalIgnoreCase) Then fallbackIndex = i
        Next
        If combo.Items.Count > 0 Then combo.SelectedIndex = fallbackIndex
    End Sub

    Private Shared Function SelectedLangCode(combo As ComboBox, fallback As String) As String
        Dim entry As LanguageEntry = TryCast(combo.SelectedItem, LanguageEntry)
        If entry IsNot Nothing Then Return entry.Code
        Return fallback
    End Function

End Class
