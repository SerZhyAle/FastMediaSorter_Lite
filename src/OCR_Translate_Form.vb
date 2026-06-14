Option Strict On

Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

''' <summary>
''' "OCR и перевод" / "OCR &amp; Translation" settings window. Hosts every OCR
''' parameter plus the source (recognition) and target (translation) language
''' pickers, each a flag-illustrated dropdown built from
''' <see cref="OcrLanguageCatalog"/>. Source defaults to "Auto-detect".
'''
''' Also provides model-install buttons: download a Tesseract language pack for
''' the chosen recognition language, and pull an Ollama model into the server.
''' Writes back into the supplied <see cref="OcrTranslateSettings"/> on OK.
''' </summary>
Public Class OCR_Translate_Form
    Inherits Form

    Private ReadOnly _settings As OcrTranslateSettings
    Private ReadOnly _rus As Boolean

    Private chkEnabled As CheckBox
    Private chkAuto As CheckBox
    Private cmbProvider As ComboBox
    Private txtEndpoint As TextBox
    Private btnInstallOllama As Button
    Private btnStartOllama As Button
    Private cmbModel As ComboBox
    Private btnPullModel As Button
    Private txtApi As TextBox
    Private cmbSource As ComboBox
    Private cmbOcrModel As ComboBox
    Private cmbOcrMode As ComboBox
    Private cmbTarget As ComboBox
    Private btnDownloadOcr As Button
    Private trkOpacity As TrackBar
    Private lblOpacityVal As Label
    Private chkDisk As CheckBox
    Private lblStatus As Label
    Private btnOk As Button

    Private busy As Boolean
    Private _restoreOwnerTopMost As Boolean

    Public Sub New(settings As OcrTranslateSettings)
        _settings = settings
        _rus = Is_Russian_Language
        BuildUi()
        LoadFromSettings()
    End Sub

    ''' <summary>
    ''' Drops the owner window's always-on-top so an external window we launch
    ''' (the Ollama installer or the download page) is not hidden behind it.
    ''' Restored when this settings dialog closes.
    ''' </summary>
    Private Sub SuspendAlwaysOnTopForExternal()
        Me.TopMost = False
        Dim owner As Form = TryCast(Me.Owner, Form)
        If owner IsNot Nothing AndAlso owner.TopMost Then
            owner.TopMost = False
            _restoreOwnerTopMost = True
        End If
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        If _restoreOwnerTopMost Then
            Dim owner As Form = TryCast(Me.Owner, Form)
            If owner IsNot Nothing Then owner.TopMost = True
            _restoreOwnerTopMost = False
        End If
        MyBase.OnFormClosed(e)
    End Sub

    Private Sub BuildUi()
        Me.Text = If(_rus, "OCR и перевод", "OCR & Translation")
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.StartPosition = FormStartPosition.CenterParent
        Me.MinimizeBox = False
        Me.MaximizeBox = False
        Me.ShowInTaskbar = False
        Me.ClientSize = New Size(470, 664)
        Me.Font = New Font("Segoe UI", 9.0F)

        Dim labelW As Integer = 150
        Dim ctlX As Integer = 172
        Dim ctlW As Integer = 280
        Dim y As Integer = 12

        chkEnabled = New CheckBox With {.Left = 12, .Top = y, .Width = 440, .Text = If(_rus, "Включить OCR и перевод", "Enable OCR & translation")}
        y += 28
        chkAuto = New CheckBox With {.Left = 12, .Top = y, .Width = 440, .Text = If(_rus, "Авто-режим (после показа изображения)", "Auto mode (after each image settles)")}
        y += 34

        AddSection(If(_rus, "Перевод", "Translation"), y) : y += 26

        AddLabel(If(_rus, "Переводчик:", "Translator:"), 12, y, labelW)
        cmbProvider = New ComboBox With {.Left = ctlX, .Top = y, .Width = ctlW, .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbProvider.Items.AddRange(New Object() {"ollama", "libretranslate"})
        AddHandler cmbProvider.SelectedIndexChanged, AddressOf OnProviderChanged
        Me.Controls.Add(cmbProvider)
        y += 30

        AddLabel(If(_rus, "Адрес (endpoint):", "Endpoint URL:"), 12, y, labelW)
        txtEndpoint = New TextBox With {.Left = ctlX, .Top = y, .Width = ctlW}
        Me.Controls.Add(txtEndpoint)
        y += 30

        AddLabel(If(_rus, "Сервер Ollama:", "Ollama server:"), 12, y, labelW)
        btnInstallOllama = New Button With {.Left = ctlX, .Top = y - 1, .Width = 137, .Height = 25, .Text = If(_rus, "Установить Ollama", "Install Ollama")}
        btnStartOllama = New Button With {.Left = ctlX + 143, .Top = y - 1, .Width = 137, .Height = 25, .Text = If(_rus, "Запустить Ollama", "Start Ollama")}
        AddHandler btnInstallOllama.Click, AddressOf OnInstallOllama
        AddHandler btnStartOllama.Click, AddressOf OnStartOllama
        Me.Controls.Add(btnInstallOllama)
        Me.Controls.Add(btnStartOllama)
        y += 32

        AddLabel(If(_rus, "Модель Ollama:", "Ollama model:"), 12, y, labelW)
        cmbModel = New ComboBox With {.Left = ctlX, .Top = y, .Width = 196, .DropDownStyle = ComboBoxStyle.DropDown}
        btnPullModel = New Button With {.Left = ctlX + 200, .Top = y - 1, .Width = 80, .Height = 25, .Text = If(_rus, "Загрузить", "Pull")}
        AddHandler btnPullModel.Click, AddressOf OnPullModel
        Me.Controls.Add(cmbModel)
        Me.Controls.Add(btnPullModel)
        y += 32

        AddLabel(If(_rus, "API-ключ:", "API key:"), 12, y, labelW)
        txtApi = New TextBox With {.Left = ctlX, .Top = y, .Width = ctlW, .UseSystemPasswordChar = True}
        Me.Controls.Add(txtApi)
        y += 36

        AddSection(If(_rus, "Распознавание (OCR)", "Recognition (OCR)"), y) : y += 26

        AddLabel(If(_rus, "Язык распознавания:", "Recognition (source):"), 12, y, labelW)
        cmbSource = BuildLanguageCombo(ctlX, y, ctlW, OcrLanguageCatalog.SourceLanguages())
        y += 34

        AddLabel(If(_rus, "Модель OCR:", "OCR model:"), 12, y, labelW)
        cmbOcrModel = New ComboBox With {.Left = ctlX, .Top = y, .Width = ctlW, .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbOcrModel.Items.AddRange(New Object() {
            If(_rus, "Быстрая (fast)", "Fast"),
            If(_rus, "Лучшая (best, медленнее)", "Best (more accurate, slower)")})
        Me.Controls.Add(cmbOcrModel)
        y += 30

        AddLabel(If(_rus, "Режим OCR:", "OCR mode:"), 12, y, labelW)
        cmbOcrMode = New ComboBox With {.Left = ctlX, .Top = y, .Width = ctlW, .DropDownStyle = ComboBoxStyle.DropDownList}
        cmbOcrMode.Items.AddRange(New Object() {
            If(_rus, "Авто (рекомендуется)", "Auto (recommended)"),
            If(_rus, "Один блок", "Single block"),
            If(_rus, "Разреженный текст", "Sparse text"),
            If(_rus, "Одна строка", "Single line"),
            If(_rus, "Вертикальный текст", "Vertical text")})
        Me.Controls.Add(cmbOcrMode)
        y += 32

        btnDownloadOcr = New Button With {.Left = ctlX, .Top = y, .Width = ctlW, .Height = 27,
            .Text = If(_rus, "Скачать пакет распознавания", "Download recognition language pack")}
        AddHandler btnDownloadOcr.Click, AddressOf OnDownloadOcr
        Me.Controls.Add(btnDownloadOcr)
        y += 38

        AddSection(If(_rus, "Язык перевода", "Translation language"), y) : y += 26

        AddLabel(If(_rus, "Язык перевода:", "Translate to (target):"), 12, y, labelW)
        cmbTarget = BuildLanguageCombo(ctlX, y, ctlW, OcrLanguageCatalog.TargetLanguages())
        y += 40

        AddSection(If(_rus, "Наложение", "Overlay"), y) : y += 26

        AddLabel(If(_rus, "Прозрачность:", "Opacity:"), 12, y, labelW)
        trkOpacity = New TrackBar With {.Left = ctlX, .Top = y, .Width = ctlW - 44, .Minimum = 40, .Maximum = 255, .TickFrequency = 32}
        lblOpacityVal = New Label With {.Left = ctlX + ctlW - 38, .Top = y + 4, .Width = 38, .Text = "210"}
        AddHandler trkOpacity.ValueChanged, Sub() lblOpacityVal.Text = trkOpacity.Value.ToString()
        Me.Controls.Add(trkOpacity)
        Me.Controls.Add(lblOpacityVal)
        y += 50

        chkDisk = New CheckBox With {.Left = 12, .Top = y, .Width = 440, .Text = If(_rus, "Дисковый кэш результатов", "Cache results on disk")}
        Me.Controls.Add(chkDisk)
        y += 34

        lblStatus = New Label With {.Left = 12, .Top = y, .Width = 446, .Height = 20, .ForeColor = Color.DimGray, .AutoEllipsis = True}
        Me.Controls.Add(lblStatus)
        y += 26

        btnOk = New Button With {.Text = "OK", .Left = 282, .Top = y, .Width = 80, .DialogResult = DialogResult.OK}
        Dim btnCancel As New Button With {.Text = If(_rus, "Отмена", "Cancel"), .Left = 372, .Top = y, .Width = 80, .DialogResult = DialogResult.Cancel}
        AddHandler btnOk.Click, AddressOf OnOk
        Me.Controls.Add(btnOk)
        Me.Controls.Add(btnCancel)
        Me.AcceptButton = btnOk
        Me.CancelButton = btnCancel

        Me.Controls.Add(chkEnabled)
        Me.Controls.Add(chkAuto)
    End Sub

    Private Sub AddSection(text As String, top As Integer)
        Dim l As New Label With {.Left = 12, .Top = top, .Width = 440, .Text = text, .Font = New Font(Me.Font, FontStyle.Bold), .ForeColor = Color.FromArgb(33, 102, 172)}
        Me.Controls.Add(l)
    End Sub

    Private Sub AddLabel(text As String, left As Integer, top As Integer, width As Integer)
        Dim l As New Label With {.Left = left, .Top = top + 4, .Width = width, .Text = text}
        Me.Controls.Add(l)
    End Sub

    Private Function BuildLanguageCombo(left As Integer, top As Integer, width As Integer, entries As LanguageEntry()) As ComboBox
        Dim combo As New ComboBox With {
            .Left = left, .Top = top, .Width = width,
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .DrawMode = DrawMode.OwnerDrawFixed,
            .ItemHeight = 24,
            .MaxDropDownItems = 16
        }
        For Each e As LanguageEntry In entries
            combo.Items.Add(e)
        Next
        AddHandler combo.DrawItem, AddressOf LangCombo_DrawItem
        Me.Controls.Add(combo)
        Return combo
    End Function

    Private Sub LangCombo_DrawItem(sender As Object, e As DrawItemEventArgs)
        e.DrawBackground()
        If e.Index < 0 Then Return

        Dim combo As ComboBox = CType(sender, ComboBox)
        Dim entry As LanguageEntry = TryCast(combo.Items(e.Index), LanguageEntry)
        If entry Is Nothing Then Return

        Dim fw As Integer = 22
        Dim fh As Integer = 16
        Dim iy As Integer = e.Bounds.Top + (e.Bounds.Height - fh) \ 2
        Try
            Dim img As Image = FlagImages.Get(entry.Code)
            If img IsNot Nothing Then e.Graphics.DrawImage(img, e.Bounds.Left + 4, iy, fw, fh)
        Catch
        End Try

        Using b As New SolidBrush(e.ForeColor)
            Using sf As New StringFormat()
                sf.LineAlignment = StringAlignment.Center
                sf.Trimming = StringTrimming.EllipsisCharacter
                sf.FormatFlags = StringFormatFlags.NoWrap
                Dim tr As New Rectangle(e.Bounds.Left + 4 + fw + 6, e.Bounds.Top, e.Bounds.Width - (fw + 16), e.Bounds.Height)
                e.Graphics.DrawString(entry.DisplayName(_rus), combo.Font, b, tr, sf)
            End Using
        End Using

        e.DrawFocusRectangle()
    End Sub

    Private Sub LoadFromSettings()
        chkEnabled.Checked = _settings.Enabled
        chkAuto.Checked = _settings.AutoMode
        cmbProvider.SelectedItem = If(_settings.Provider.ToLowerInvariant() = "libretranslate", "libretranslate", "ollama")
        txtEndpoint.Text = _settings.Endpoint
        cmbModel.Text = _settings.OllamaModel
        txtApi.Text = _settings.ApiKey
        SelectLanguage(cmbSource, _settings.SourceLang, "auto")
        SelectLanguage(cmbTarget, _settings.TargetLang, "en")
        cmbOcrModel.SelectedIndex = If(String.Equals(_settings.OcrModelQuality, "best", StringComparison.OrdinalIgnoreCase), 1, 0)
        Dim modeIndex As Integer = Array.IndexOf(OcrModeCodes, If(_settings.OcrPageMode, "auto").Trim().ToLowerInvariant())
        cmbOcrMode.SelectedIndex = If(modeIndex >= 0, modeIndex, 0)
        trkOpacity.Value = OcrTranslateSettings.ClampOpacity(_settings.OverlayOpacity)
        lblOpacityVal.Text = trkOpacity.Value.ToString()
        chkDisk.Checked = _settings.DiskCache
        UpdateProviderControls()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        _settings.Enabled = chkEnabled.Checked
        _settings.AutoMode = chkAuto.Checked
        _settings.Provider = Convert.ToString(cmbProvider.SelectedItem)
        _settings.Endpoint = txtEndpoint.Text.Trim()
        _settings.OllamaModel = cmbModel.Text.Trim()
        _settings.ApiKey = txtApi.Text
        _settings.SourceLang = SelectedCode(cmbSource, "auto")
        _settings.TargetLang = SelectedCode(cmbTarget, "en")
        _settings.OcrModelQuality = If(cmbOcrModel.SelectedIndex = 1, "best", "fast")
        Dim modeIdx As Integer = cmbOcrMode.SelectedIndex
        _settings.OcrPageMode = If(modeIdx >= 0 AndAlso modeIdx < OcrModeCodes.Length, OcrModeCodes(modeIdx), "auto")
        _settings.OverlayOpacity = trkOpacity.Value
        _settings.DiskCache = chkDisk.Checked
    End Sub

    ' --- model install handlers ----------------------------------------------

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        UpdateOllamaStateHint()
        RefreshInstalledModels()
    End Sub

    Private Sub OnProviderChanged(sender As Object, e As EventArgs)
        UpdateProviderControls()
        RefreshInstalledModels()
    End Sub

    Private Sub UpdateProviderControls()
        Dim ollama As Boolean = IsOllama()
        cmbModel.Enabled = ollama
        btnPullModel.Enabled = ollama AndAlso Not busy
        btnInstallOllama.Enabled = ollama AndAlso Not busy
        btnStartOllama.Enabled = ollama AndAlso Not busy
    End Sub

    Private Function IsOllama() As Boolean
        Return String.Equals(Convert.ToString(cmbProvider.SelectedItem), "ollama", StringComparison.OrdinalIgnoreCase)
    End Function

    ' Popular, translation-capable models offered for download when the user has
    ' none installed yet. Fast EN->RU picks are listed first. The dropdown is
    ' editable, so any other name works too.
    Private Shared ReadOnly RecommendedModels As String() = {
        "qwen2.5:3b", "gemma2:2b", "qwen2.5:1.5b", "llama3.2:3b",
        "qwen2.5", "qwen2.5:7b", "llama3.2", "gemma2", "aya", "mistral", "phi3.5"
    }

    ' OCR page-mode codes, aligned with the cmbOcrMode item order.
    Private Shared ReadOnly OcrModeCodes As String() = {"auto", "block", "sparse", "line", "vertical"}

    ''' <summary>
    ''' Fills the model dropdown: installed models first, then recommended models
    ''' to download. Stays populated even when Ollama is unreachable so the user
    ''' can pick one and press "Загрузить" / Pull.
    ''' </summary>
    Private Async Sub RefreshInstalledModels()
        If Not IsOllama() Then Return

        Dim installed As New List(Of String)
        Try
            Dim tr As New OllamaTranslator(txtEndpoint.Text.Trim(), "")
            installed = Await tr.ListModelsAsync(CancellationToken.None)
        Catch
        End Try

        Dim merged As New List(Of String)
        For Each m As String In installed
            merged.Add(m)
        Next
        For Each m As String In RecommendedModels
            If Not merged.Any(Function(x) x.Equals(m, StringComparison.OrdinalIgnoreCase)) Then merged.Add(m)
        Next

        Dim current As String = cmbModel.Text
        cmbModel.BeginUpdate()
        cmbModel.Items.Clear()
        For Each m As String In merged
            cmbModel.Items.Add(m)
        Next
        cmbModel.EndUpdate()
        cmbModel.Text = current
    End Sub

    ''' <summary>Shows a hint when Ollama is missing or not running.</summary>
    Private Sub UpdateOllamaStateHint()
        If Not IsOllama() Then Return
        If Not OllamaManager.IsInstalled() Then
            lblStatus.Text = If(_rus, "Ollama не установлен - нажмите «Установить Ollama».", "Ollama not installed - press Install Ollama.")
        ElseIf Not OllamaManager.IsProcessRunning() Then
            lblStatus.Text = If(_rus, "Ollama не запущен - нажмите «Запустить Ollama».", "Ollama not running - press Start Ollama.")
        End If
    End Sub

    Private Async Sub OnInstallOllama(sender As Object, e As EventArgs)
        If busy Then Return
        If OllamaManager.IsInstalled() Then
            lblStatus.Text = If(_rus, "Ollama уже установлен. Нажмите «Запустить Ollama».", "Ollama is already installed. Press Start Ollama.")
            Return
        End If

        Dim confirm As String = If(_rus,
            "Скачать и установить Ollama? Это несколько сотен МБ, загрузка может занять время.",
            "Download and install Ollama? This is several hundred MB and may take a while.")
        If MessageBox.Show(Me, confirm, Me.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return

        SetBusy(True)
        lblStatus.Text = If(_rus, "Скачивание Ollama..", "Downloading Ollama..")
        Dim installerPath As String = ""
        Try
            Dim prog As New Progress(Of String)(Sub(s) lblStatus.Text = (If(_rus, "Скачивание Ollama: ", "Downloading Ollama: ")) & s)
            installerPath = Await OllamaManager.DownloadInstallerAsync(prog, CancellationToken.None)
        Catch ex As Exception
            lblStatus.Text = ex.Message
        End Try
        SetBusy(False)

        ' Make sure the installer / browser is not hidden behind an always-on-top window.
        SuspendAlwaysOnTopForExternal()

        If installerPath.Length > 0 AndAlso IO.File.Exists(installerPath) Then
            lblStatus.Text = If(_rus, "Запуск установщика Ollama..", "Launching Ollama installer..")
            OllamaManager.RunInstaller(installerPath)
        Else
            lblStatus.Text = If(_rus, "Не удалось скачать. Открываю сайт Ollama..", "Download failed. Opening Ollama website..")
            OllamaManager.OpenWebPage()
        End If
    End Sub

    Private Async Sub OnStartOllama(sender As Object, e As EventArgs)
        If busy Then Return
        If Not OllamaManager.IsInstalled() Then
            lblStatus.Text = If(_rus, "Ollama не установлен - нажмите «Установить Ollama».", "Ollama not installed - press Install Ollama.")
            Return
        End If

        SetBusy(True)
        lblStatus.Text = If(_rus, "Запуск Ollama..", "Starting Ollama..")
        OllamaManager.StartServer()

        Dim up As Boolean = False
        Dim tr As New OllamaTranslator(txtEndpoint.Text.Trim(), "")
        For i As Integer = 0 To 20
            If Await tr.ProbeAsync(CancellationToken.None) Then
                up = True
                Exit For
            End If
            Await Task.Delay(500)
        Next
        SetBusy(False)

        If up Then
            lblStatus.Text = If(_rus, "Ollama запущен.", "Ollama is running.")
            RefreshInstalledModels()
        Else
            lblStatus.Text = If(_rus, "Не удалось запустить Ollama.", "Could not start Ollama.")
        End If
    End Sub

    Private Async Sub OnPullModel(sender As Object, e As EventArgs)
        If busy Then Return
        Dim modelName As String = cmbModel.Text.Trim()
        If modelName.Length = 0 Then
            lblStatus.Text = If(_rus, "Укажите имя модели (например, llama3.2)", "Enter a model name (e.g. llama3.2)")
            Return
        End If

        SetBusy(True)
        Dim endpoint As String = txtEndpoint.Text.Trim()
        Dim ok As Boolean = False
        Try
            Dim tr As New OllamaTranslator(endpoint, modelName)
            Dim prog As New Progress(Of String)(Sub(s) lblStatus.Text = (If(_rus, "Загрузка: ", "Pulling: ")) & s)
            ok = Await tr.PullModelAsync(modelName, prog, CancellationToken.None)
        Catch ex As Exception
            lblStatus.Text = ex.Message
        End Try
        SetBusy(False)

        lblStatus.Text = If(ok,
            If(_rus, "Модель установлена: ", "Model installed: ") & modelName,
            If(_rus, "Не удалось загрузить модель (Ollama запущен?)", "Pull failed (is Ollama running?)"))
        If ok Then RefreshInstalledModels()
    End Sub

    Private Async Sub OnDownloadOcr(sender As Object, e As EventArgs)
        If busy Then Return
        Dim srcCode As String = SelectedCode(cmbSource, "auto")
        Dim tessLangs As String = OcrTranslateSettings.TessLanguages(srcCode)

        Dim preferBest As Boolean = (cmbOcrModel.SelectedIndex = 1)
        If Not Await OptionalRuntimeManager.EnsureOcrRuntimeInteractiveAsync(Me, _rus) Then
            lblStatus.Text = If(_rus, "OCR-движок не установлен.", "OCR runtime is not installed.")
            Return
        End If

        SetBusy(True)
        lblStatus.Text = If(_rus, "Скачивание языкового пакета: ", "Downloading language pack: ") & tessLangs
        Dim ok As Boolean = False
        Try
            ok = Await Task.Run(Function() TesseractOcrEngine.EnsureLanguagesPublic(tessLangs, preferBest))
        Catch ex As Exception
            lblStatus.Text = ex.Message
        End Try
        SetBusy(False)

        lblStatus.Text = If(ok,
            If(_rus, "Готово: пакет ", "Ready: pack ") & tessLangs,
            If(_rus, "Не удалось скачать (нет сети?)", "Download failed (no network?)"))
    End Sub

    Private Sub SetBusy(value As Boolean)
        busy = value
        btnDownloadOcr.Enabled = Not value
        btnPullModel.Enabled = Not value AndAlso IsOllama()
        btnInstallOllama.Enabled = Not value AndAlso IsOllama()
        btnStartOllama.Enabled = Not value AndAlso IsOllama()
        btnOk.Enabled = Not value
        Me.UseWaitCursor = value
        If value Then Application.DoEvents()
    End Sub

    ' --- helpers --------------------------------------------------------------

    Private Shared Sub SelectLanguage(combo As ComboBox, code As String, fallback As String)
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
        combo.SelectedIndex = fallbackIndex
    End Sub

    Private Shared Function SelectedCode(combo As ComboBox, fallback As String) As String
        Dim entry As LanguageEntry = TryCast(combo.SelectedItem, LanguageEntry)
        If entry IsNot Nothing Then Return entry.Code
        Return fallback
    End Function

End Class
