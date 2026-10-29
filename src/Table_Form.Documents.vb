#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

Partial Public Class Table_Form
    Private modernDocumentsPage As TabPage

    Private Shared ReadOnly documentRows As String()() = {
        New String() {"FullPdfReader", "Полное чтение PDF"},
        New String() {"FullEpubReader", "Полное чтение EPUB"},
        New String() {"RememberDocumentPosition", "Запоминать позицию чтения"},
        New String() {"RememberDocumentAppearance", "Запоминать оформление документа"},
        New String() {"DocumentBoundaryNavigation", "Переходить к следующему файлу на границе документа"},
        New String() {"DocumentTtsEnabled", "Чтение вслух"},
        New String() {"DocumentTtsVoice", "Голос"},
        New String() {"DocumentTtsRate", "Скорость чтения", "0.5", "2"},
        New String() {"DocumentFullscreenChrome", "Панель чтения в полноэкранном режиме", "auto", "Скрывать автоматически", "visible", "Всегда показывать"},
        New String() {"DocumentMemoryCacheMb", "Кэш документов в памяти", "64", "1024"},
        New String() {"PdfLayout", "Режим просмотра", "single", "Одна страница", "continuous", "Непрерывно", "spread", "Разворот"},
        New String() {"PdfFirstPageAlone", "Первая страница отдельно"},
        New String() {"PdfSpreadDirection", "Направление чтения", "document", "Как в документе", "ltr", "Слева направо", "rtl", "Справа налево"},
        New String() {"PdfZoom", "Масштаб", "fitPage", "Вписать", "fitWidth", "По ширине", "actual", "100 %", "manual", "Вручную"},
        New String() {"PdfZoomPercent", "Масштаб, %", "25", "800"},
        New String() {"PdfColorMode", "Цвет страницы", "original", "Оригинал", "night", "Ночь", "sepia", "Сепия"},
        New String() {"PdfThumbnailPanel", "Миниатюры страниц"},
        New String() {"PdfOutlinePanel", "Оглавление"},
        New String() {"PdfPanelWidth", "Ширина панели", "120", "640"},
        New String() {"PdfSearchScope", "Область поиска", "whole", "Весь документ", "page", "Текущая страница"},
        New String() {"PdfSearchCase", "Учитывать регистр"},
        New String() {"PdfRenderQuality", "Качество отображения", "automatic", "Автоматически", "fast", "Быстро", "high", "Высокое"},
        New String() {"PdfRenderCacheMb", "Кэш страниц в памяти", "32", "512"},
        New String() {"PdfPrefetchPages", "Страниц для предзагрузки", "0", "3"},
        New String() {"EpubLayout", "Режим просмотра", "continuous", "Непрерывно", "paginated", "По страницам"},
        New String() {"EpubTheme", "Тема документа", "system", "Как в системе", "light", "Светлая", "dark", "Тёмная", "sepia", "Сепия", "black", "Чёрная"},
        New String() {"EpubFontFamily", "Шрифт"},
        New String() {"EpubFontSize", "Размер текста", "10", "48"},
        New String() {"EpubLineHeight", "Межстрочный интервал", "1", "3"},
        New String() {"EpubParagraphSpacing", "Интервал между абзацами", "0", "2"},
        New String() {"EpubHorizontalMargin", "Поля по горизонтали", "0", "96"},
        New String() {"EpubColumnWidth", "Ширина текста", "0", "120"},
        New String() {"EpubAlignment", "Выравнивание текста", "book", "Как в документе", "left", "Слева", "justify", "По ширине"},
        New String() {"EpubHyphenation", "Перенос слов", "book", "Как в документе", "on", "Включено", "off", "Выключено"},
        New String() {"EpubUseBookStyles", "Использовать стили книги"},
        New String() {"EpubUseEmbeddedFonts", "Использовать шрифты книги"},
        New String() {"EpubTocDepth", "Глубина оглавления", "1", "32"},
        New String() {"EpubSearchScope", "Область поиска", "whole", "Весь документ", "chapter", "Текущая глава"}}

    Private Sub RegisterDocumentSettings()
        Dim sharedRows = documentRows.Where(Function(row) Not row(0).StartsWith("Pdf", StringComparison.Ordinal) AndAlso Not row(0).StartsWith("Epub", StringComparison.Ordinal) AndAlso Not row(0).StartsWith("Full", StringComparison.Ordinal)).ToArray()
        AddSettingsGroup("documents", "basic", "Документы", True, sharedRows.Select(Function(row) "doc_" & row(0)).Concat({"doc_clear_history"}).ToArray())
        AddSettingsGroup("documents", "pdf", "PDF", True, documentRows.Where(Function(row) row(0).StartsWith("Pdf", StringComparison.Ordinal) OrElse row(0) = "FullPdfReader").Select(Function(row) "doc_" & row(0)).Concat({"doc_reset_pdf"}).ToArray())
        AddSettingsGroup("documents", "epub", "EPUB", True, documentRows.Where(Function(row) row(0).StartsWith("Epub", StringComparison.Ordinal) OrElse row(0) = "FullEpubReader").Select(Function(row) "doc_" & row(0)).Concat({"doc_reset_epub"}).ToArray())
        Dim flow = PageFlow(modernDocumentsPage)
        For Each definition In documentRows
            Dim parts = definition
            Dim prop = GetType(ModernViewerPreferences).GetProperty(parts(0))
            Dim key = "doc_" & parts(0)
            RegisterSettingsRow(key, Sub()
                If prop.PropertyType Is GetType(Boolean) Then
                    AddPreferenceCheck(flow, key, Function() CBool(prop.GetValue(Main_Form.GetModernPreferences())),
                        Sub(value) prop.SetValue(Main_Form.GetModernPreferences(), value))
                    If prop.Name = "FullEpubReader" AndAlso Not Main_Form.EpubReaderHostAvailable Then
                        Dim row = modernSettingsRows.Last(Function(item) item.Key = key)
                        row.Editor.Enabled = False
                        row.Description.Text = Localization.T("Чтение EPUB недоступно")
                    End If
                ElseIf prop.Name = "EpubColumnWidth" Then
                    ' 0 is the whole window and 40..120 are characters per line; a number box would let 1..39 in and the
                    ' preference would quietly lift them to 40.
                    AddPreferenceChoice(flow, key, AddressOf EpubColumnWidthChoices,
                        Function() CInt(prop.GetValue(Main_Form.GetModernPreferences())).ToString(CultureInfo.InvariantCulture),
                        Sub(value) prop.SetValue(Main_Form.GetModernPreferences(), Integer.Parse(value, CultureInfo.InvariantCulture)))
                ElseIf prop.PropertyType Is GetType(Integer) Then
                    AddPreferenceNumber(flow, key, Integer.Parse(parts(2), CultureInfo.InvariantCulture), Integer.Parse(parts(3), CultureInfo.InvariantCulture),
                        Function() CInt(prop.GetValue(Main_Form.GetModernPreferences())), Sub(value) prop.SetValue(Main_Form.GetModernPreferences(), value))
                ElseIf prop.PropertyType Is GetType(Double) Then
                    Dim number As New NumericUpDown With {.DecimalPlaces = 1, .Increment = 0.1D, .Minimum = Decimal.Parse(parts(2), CultureInfo.InvariantCulture),
                        .Maximum = Decimal.Parse(parts(3), CultureInfo.InvariantCulture), .Width = Sp(100)}
                    Dim refreshing As Boolean
                    Dim refresh As Action = Sub()
                                                refreshing = True
                                                number.Value = CDec(prop.GetValue(Main_Form.GetModernPreferences()))
                                                refreshing = False
                                            End Sub
                    refresh()
                    expandedRefreshActions.Add(refresh)
                    AddHandler number.ValueChanged, Sub()
                                                        If refreshing Then Return
                                                        prop.SetValue(Main_Form.GetModernPreferences(), CDbl(number.Value))
                                                        ApplyExpandedPreferences()
                                                    End Sub
                    AddSettingRow(flow, key, number, 130, True)
                ElseIf prop.Name = "DocumentTtsVoice" Then
                    Dim voices = DocumentSpeech.InstalledVoices()
                    AddPreferenceChoice(flow, key, Function() New PreferenceChoice() {Choice("", "Как в системе")}.Concat(voices.Select(Function(name) New PreferenceChoice(name, name))).ToArray(),
                        Function() CStr(prop.GetValue(Main_Form.GetModernPreferences())), Sub(value) prop.SetValue(Main_Form.GetModernPreferences(), value))
                ElseIf parts.Length >= 3 Then
                    AddPreferenceChoice(flow, key, Function() Enumerable.Range(0, (parts.Length - 2) \ 2).Select(Function(index) Choice(parts(2 + index * 2), parts(3 + index * 2))).ToArray(),
                        Function() CStr(prop.GetValue(Main_Form.GetModernPreferences())), Sub(value) prop.SetValue(Main_Form.GetModernPreferences(), value))
                Else
                    Dim text As New TextBox With {.Text = CStr(prop.GetValue(Main_Form.GetModernPreferences())), .MaxLength = 128}
                    AddHandler text.Validated, Sub()
                                                  prop.SetValue(Main_Form.GetModernPreferences(), text.Text)
                                                  ApplyExpandedPreferences()
                                              End Sub
                    AddSettingRow(flow, key, text, 230, True)
                End If
            End Sub)
        Next
        For Each formatName As String In {"pdf", "epub"}
            Dim value = formatName
            RegisterSettingsRow("doc_reset_" & value, Sub()
                Dim reset As New Button With {.AutoSize = True, .Text = Localization.T("Сбросить")}
                AddHandler reset.Click, Sub()
                                            Main_Form.GetModernPreferences().ResetDocumentAppearance(value)
                                            ApplyExpandedPreferences()
                                            RefreshExpandedSettingsRows()
                                        End Sub
                AddSettingRow(flow, "doc_reset_" & value, reset, 150, True)
            End Sub)
        Next
        RegisterSettingsRow("doc_clear_history", Sub()
            Dim clear As New Button With {.AutoSize = True, .Text = Localization.T("Очистить")}
            AddHandler clear.Click, Sub()
                                        Main_Form.GetModernPreferences().DocumentPageHistory = "[]"
                                        ApplyExpandedPreferences()
                                    End Sub
            AddSettingRow(flow, "doc_clear_history", clear, 150, True)
        End Sub)
        RegisterHtmlSettings(flow)
    End Sub

    Private Function DocumentSettingTitle(key As String) As String
        If key = "doc_clear_history" Then Return Localization.T("Очистить историю чтения")
        If key.StartsWith("doc_reset_", StringComparison.Ordinal) Then Return Localization.T("Сбросить оформление")
        Dim definition = documentRows.FirstOrDefault(Function(row) "doc_" & row(0) = key)
        Return If(definition Is Nothing, key, Localization.T(definition(1)))
    End Function

    ' ---- HTML pages: specification 275 section 12 ------------------------------------------------------
    ' Building a row only creates editors over the stored preferences. Nothing below starts WebView2, reads the
    ' reading-state file or probes the runtime until the user presses the button that says so.

    ''' <summary>The one address the runtime row can open, and only from a click: the Microsoft page that offers the
    ''' Evergreen Runtime. The program downloads and installs nothing itself.</summary>
    Private Const HtmlRepairAddress As String = "https://developer.microsoft.com/microsoft-edge/webview2/"

    ''' <summary>The column-width stops: 0 is the whole window, the rest are characters per line.</summary>
    Private Shared ReadOnly htmlColumnWidths As Integer() = {0, 40, 50, 60, 70, 80, 90, 100, 110, 120}

    ''' <summary>What the last explicit check said; empty until the user asks. Kept so a language change or a rebuilt
    ''' row shows the answer again instead of the hint.</summary>
    Private htmlRuntimeStatusText As String = ""
    Private htmlRuntimeChecking As Boolean

    Private Sub RegisterHtmlSettings(flow As FlowLayoutPanel)
        AddSettingsGroup("documents", "html.basic", "HTML-страницы", True,
            "html_enabled", "html_presentation", "html_zoom", "html_runtime_status", "html_runtime_repair")
        AddSettingsGroup("documents", "html.appearance", "Оформление HTML-страниц", False,
            "html_theme", "html_font", "html_font_size", "html_line_height", "html_column_width", "html_margin",
            "html_wrap", "html_remember_appearance", "html_reset_appearance")
        AddSettingsGroup("documents", "html.navigation", "Ссылки, поиск и позиция в HTML-страницах", False,
            "html_external_links", "html_find_case", "html_find_word", "html_remember_position")
        AddSettingsGroup("documents", "html.online", "HTML-страницы из интернета", False,
            "html_online_enabled", "html_remote_note")
        AddSettingsGroup("documents", "html.storage", "Данные HTML-страниц", False,
            "html_history_limit", "html_snapshot_cache", "html_clear_history", "html_clear_runtime")

        RegisterHtmlCheck(flow, "html_enabled", Function(p) p.HtmlViewerEnabled, Sub(p, v) p.HtmlViewerEnabled = v)
        RegisterHtmlChoice(flow, "html_presentation",
            Function() {Choice("original", "Оригинал"), Choice("reader", "Режим чтения")},
            Function(p) p.HtmlPresentation, Sub(p, v) p.HtmlPresentation = v)
        RegisterHtmlNumber(flow, "html_zoom", ModernViewerPreferences.HtmlZoomMin, ModernViewerPreferences.HtmlZoomMax,
            Function(p) p.HtmlZoom, Sub(p, v) p.HtmlZoom = v)

        RegisterHtmlChoice(flow, "html_theme",
            Function() {Choice("system", "Как в системе"), Choice("light", "Светлая"), Choice("dark", "Тёмная"), Choice("sepia", "Сепия"), Choice("black", "Чёрная")},
            Function(p) p.HtmlTheme, Sub(p, v) p.HtmlTheme = v)
        RegisterHtmlChoice(flow, "html_font", AddressOf HtmlFontChoices,
            Function(p) p.HtmlFontFamily, Sub(p, v) p.HtmlFontFamily = v)
        RegisterHtmlNumber(flow, "html_font_size", ModernViewerPreferences.HtmlFontSizeMin, ModernViewerPreferences.HtmlFontSizeMax,
            Function(p) p.HtmlFontSize, Sub(p, v) p.HtmlFontSize = v)
        RegisterHtmlLineHeight(flow)
        RegisterHtmlChoice(flow, "html_column_width", AddressOf HtmlColumnWidthChoices,
            Function(p) p.HtmlColumnWidth.ToString(CultureInfo.InvariantCulture),
            Sub(p, v)
                Dim width As Integer
                If Integer.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, width) Then p.HtmlColumnWidth = width
            End Sub)
        RegisterHtmlNumber(flow, "html_margin", 0, ModernViewerPreferences.HtmlMarginMax,
            Function(p) p.HtmlMargin, Sub(p, v) p.HtmlMargin = v)
        RegisterHtmlCheck(flow, "html_wrap", Function(p) p.HtmlWrapWideContent, Sub(p, v) p.HtmlWrapWideContent = v)
        RegisterHtmlCheck(flow, "html_remember_appearance", Function(p) p.HtmlRememberAppearance, Sub(p, v) p.HtmlRememberAppearance = v)

        RegisterHtmlChoice(flow, "html_external_links",
            Function() {Choice("ask", "Спрашивать"), Choice("block", "Блокировать")},
            Function(p) p.HtmlExternalLinks, Sub(p, v) p.HtmlExternalLinks = v)
        RegisterHtmlCheck(flow, "html_find_case", Function(p) p.HtmlFindCase, Sub(p, v) p.HtmlFindCase = v)
        RegisterHtmlCheck(flow, "html_find_word", Function(p) p.HtmlFindWholeWord, Sub(p, v) p.HtmlFindWholeWord = v)
        RegisterHtmlCheck(flow, "html_remember_position", Function(p) p.HtmlRememberPosition, Sub(p, v) p.HtmlRememberPosition = v)

        RegisterHtmlCheck(flow, "html_online_enabled", Function(p) p.HtmlOnlinePagesEnabled, Sub(p, v) p.HtmlOnlinePagesEnabled = v)
        RegisterSettingsRow("html_remote_note", Sub() AddInformationBlock(flow, "html_remote_note"))

        RegisterHtmlNumber(flow, "html_history_limit", 0, ModernViewerPreferences.HtmlHistoryLimitMax,
            Function(p) p.HtmlHistoryLimit, Sub(p, v) p.HtmlHistoryLimit = v)
        RegisterHtmlNumber(flow, "html_snapshot_cache", ModernViewerPreferences.HtmlSnapshotCacheMbMin, ModernViewerPreferences.HtmlSnapshotCacheMbMax,
            Function(p) p.HtmlSnapshotCacheMb, Sub(p, v) p.HtmlSnapshotCacheMb = v)

        RegisterHtmlButton(flow, "html_reset_appearance", "Сбросить", AddressOf ResetHtmlAppearanceClicked)
        RegisterHtmlButton(flow, "html_clear_history", "Очистить", AddressOf ClearHtmlHistoryClicked)
        RegisterHtmlButton(flow, "html_clear_runtime", "Очистить", AddressOf ClearHtmlRuntimeClicked)
        RegisterHtmlButton(flow, "html_runtime_status", "Проверить", AddressOf CheckHtmlRuntimeClicked)
        RegisterHtmlButton(flow, "html_runtime_repair", "Открыть", AddressOf OpenHtmlRepairClicked)
    End Sub

    Private Sub RegisterHtmlCheck(flow As FlowLayoutPanel, key As String,
                                  read As Func(Of ModernViewerPreferences, Boolean),
                                  write As Action(Of ModernViewerPreferences, Boolean))
        RegisterSettingsRow(key, Sub() AddPreferenceCheck(flow, key,
            Function() read(Main_Form.GetModernPreferences()), Sub(value) write(Main_Form.GetModernPreferences(), value)))
    End Sub

    Private Sub RegisterHtmlNumber(flow As FlowLayoutPanel, key As String, minimum As Integer, maximum As Integer,
                                   read As Func(Of ModernViewerPreferences, Integer),
                                   write As Action(Of ModernViewerPreferences, Integer))
        RegisterSettingsRow(key, Sub() AddPreferenceNumber(flow, key, minimum, maximum,
            Function() read(Main_Form.GetModernPreferences()), Sub(value) write(Main_Form.GetModernPreferences(), value)))
    End Sub

    Private Sub RegisterHtmlChoice(flow As FlowLayoutPanel, key As String,
                                   choices As Func(Of PreferenceChoice()),
                                   read As Func(Of ModernViewerPreferences, String),
                                   write As Action(Of ModernViewerPreferences, String))
        RegisterSettingsRow(key, Sub() AddPreferenceChoice(flow, key, choices,
            Function() read(Main_Form.GetModernPreferences()), Sub(value) write(Main_Form.GetModernPreferences(), value)))
    End Sub

    ''' <summary>Line height is a decimal with one place; it travels as an invariant number and the control keeps
    ''' the 1.0-3.0 step of 0.1 whatever the user's decimal separator is.</summary>
    Private Sub RegisterHtmlLineHeight(flow As FlowLayoutPanel)
        Const key As String = "html_line_height"
        RegisterSettingsRow(key,
            Sub()
                Dim number As New NumericUpDown With {.DecimalPlaces = 1, .Increment = 0.1D, .Minimum = 1D, .Maximum = 3D, .Width = Sp(100)}
                Dim refreshing As Boolean
                Dim refresh As Action =
                    Sub()
                        If number.Focused Then Return
                        refreshing = True
                        Try
                            Dim stored As Decimal = CDec(Math.Round(Main_Form.GetModernPreferences().HtmlLineHeight, 1))
                            number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, stored))
                        Finally
                            refreshing = False
                        End Try
                    End Sub
                refresh()
                expandedRefreshActions.Add(refresh)
                AddHandler number.ValueChanged,
                    Sub()
                        If refreshing Then Return
                        Main_Form.GetModernPreferences().HtmlLineHeight = Math.Round(CDbl(number.Value), 1)
                        ApplyExpandedPreferences()
                    End Sub
                AddSettingRow(flow, key, number, 130, True)
            End Sub)
    End Sub

    ''' <summary>The three generic families, then the stored name when it is none of them (an imported or earlier
    ''' choice must stay visible, not silently turn into "As in document").</summary>
    Private Function HtmlFontChoices() As PreferenceChoice()
        Dim list As New List(Of PreferenceChoice) From {
            Choice("authored", "Как в документе"), Choice("serif", "С засечками"),
            Choice("sans-serif", "Без засечек"), Choice("monospace", "Моноширинный")}
        Dim current As String = Main_Form.GetModernPreferences().HtmlFontFamily
        If Not list.Any(Function(item) String.Equals(item.Value, current, StringComparison.Ordinal)) Then
            list.Add(New PreferenceChoice(current, current))
        End If
        Return list.ToArray()
    End Function

    Private Function HtmlColumnWidthChoices() As PreferenceChoice()
        Return ColumnWidthChoices(Main_Form.GetModernPreferences().HtmlColumnWidth)
    End Function

    Private Function EpubColumnWidthChoices() As PreferenceChoice()
        Return ColumnWidthChoices(Main_Form.GetModernPreferences().EpubColumnWidth)
    End Function

    Private Shared Function ColumnWidthChoices(current As Integer) As PreferenceChoice()
        Dim widths As New List(Of Integer)(htmlColumnWidths)
        If Not widths.Contains(current) Then
            widths.Add(current)
            widths.Sort()
        End If
        Return widths.Select(Function(width) New PreferenceChoice(width.ToString(CultureInfo.InvariantCulture),
            If(width = 0, Localization.T("На всю ширину"), Localization.TF("{0} симв.", width)))).ToArray()
    End Function

    ''' <summary>A command button that is a row's editor. Its caption is re-read with the other choices when the
    ''' language changes.</summary>
    Private Sub RegisterHtmlButton(flow As FlowLayoutPanel, key As String, captionKey As String, click As EventHandler)
        RegisterSettingsRow(key,
            Sub()
                Dim button As New Button With {.AutoSize = True, .Text = Localization.T(captionKey)}
                AddHandler button.Click, click
                expandedRefreshActions.Add(Sub() button.Text = Localization.T(captionKey))
                AddSettingRow(flow, key, button, 150, True)
            End Sub)
    End Sub

    ''' <summary>Returns the HTML appearance to its defaults and removes the per-document overrides; reading
    ''' positions stay. The state file is opened only now, on the user's click.</summary>
    Private Async Sub ResetHtmlAppearanceClicked(sender As Object, e As EventArgs)
        Main_Form.GetModernPreferences().ResetHtmlAppearance()
        ApplyExpandedPreferences()
        RefreshExpandedSettingsRows()
        Try
            Await Task.Run(Sub()
                               Dim state As HtmlReaderState = HtmlReaderState.Current
                               state.ResetAllAppearance()
                               state.Save()
                           End Sub).ConfigureAwait(True)
        Catch ex As Exception
            AppFileLogger.LogException("Settings: resetting the HTML document appearance", ex)
        End Try
    End Sub

    ''' <summary>The explicit clear: the scope is named first, the answer is Cancel by default, and the records
    ''' (positions and appearance overrides) are the only thing removed.</summary>
    Private Async Sub ClearHtmlHistoryClicked(sender As Object, e As EventArgs)
        Dim button As Button = TryCast(sender, Button)
        If AskDialogs.Choose(Me, AskSpecs.HtmlHistoryClear()) <> AskSpecs.HtmlHistoryClearExecute Then Return
        If button IsNot Nothing Then button.Enabled = False
        Dim failed As Boolean
        Try
            failed = Await Task.Run(Function()
                                        Dim state As HtmlReaderState = HtmlReaderState.Current
                                        state.Clear()
                                        Return state.Diagnostic.Length > 0
                                    End Function).ConfigureAwait(True)
        Catch ex As Exception
            AppFileLogger.LogException("Settings: clearing the HTML reading history", ex)
            failed = True
        End Try
        If IsDisposed OrElse Disposing Then Return
        If button IsNot Nothing AndAlso Not button.IsDisposed Then button.Enabled = True
        AskDialogs.Tell(Me, Localization.T("Очистить историю HTML-страниц"),
            If(failed, Localization.T("Историю не удалось очистить: файл записей недоступен для записи."), Localization.T("История HTML-страниц очищена.")),
            If(failed, AskIcon.Warning, AskIcon.Info))
    End Sub

    ''' <summary>Clears only the folders this program owns for its own runs, on the UI thread that owns the
    ''' environments. A page that is open keeps its folder and the answer says so.</summary>
    Private Sub ClearHtmlRuntimeClicked(sender As Object, e As EventArgs)
        Dim complete As Boolean
        Try
            complete = HtmlRuntimeService.ClearOwnedData()
        Catch ex As Exception
            AppFileLogger.LogException("Settings: clearing the HTML runtime data", ex)
        End Try
        AskDialogs.Tell(Me, Localization.T("Очистить временные данные просмотра"),
            If(complete, Localization.T("Временные данные просмотра HTML-страниц удалены."),
               Localization.T("Часть данных используется открытой страницей и останется. Закройте HTML-страницы и повторите очистку.")),
            If(complete, AskIcon.Info, AskIcon.Warning))
    End Sub

    ''' <summary>The explicit runtime check: detection only, off the UI thread, no download and no install. The
    ''' answer becomes the row's explanation.</summary>
    Private Async Sub CheckHtmlRuntimeClicked(sender As Object, e As EventArgs)
        If htmlRuntimeChecking Then Return
        Dim button As Button = TryCast(sender, Button)
        htmlRuntimeChecking = True
        If button IsNot Nothing Then button.Enabled = False
        Dim answer As String
        Try
            answer = Await Task.Run(Function() HtmlRuntimeService.DescribeAvailability()).ConfigureAwait(True)
        Catch ex As Exception
            AppFileLogger.LogException("Settings: checking the HTML runtime", ex)
            answer = Localization.T("Не удалось проверить компонент просмотра HTML-страниц.")
        End Try
        htmlRuntimeChecking = False
        If IsDisposed OrElse Disposing Then Return
        If button IsNot Nothing AndAlso Not button.IsDisposed Then button.Enabled = True
        htmlRuntimeStatusText = answer
        Dim statusRow As ModernSettingRow = modernSettingsRows.FirstOrDefault(Function(item) item.Key = "html_runtime_status")
        If statusRow Is Nothing OrElse statusRow.Description Is Nothing Then Return
        statusRow.Description.Text = answer
        If button IsNot Nothing Then button.AccessibleDescription = answer
        SizeModernSettingRow(statusRow)
    End Sub

    Private Sub OpenHtmlRepairClicked(sender As Object, e As EventArgs)
        Try
            AboutResources.Open(HtmlRepairAddress, Localization.CurrentCode)
        Catch ex As Exception
            AppFileLogger.LogException("Settings: opening the runtime page", ex)
            ViewerBox.Show(Me, Localization.TF("Не удалось открыть ссылку в браузере. Откройте её вручную: {0}", HtmlRepairAddress),
                           Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Function HtmlSettingTitle(key As String) As String
        Select Case key
            Case "html_enabled" : Return Localization.T("Просмотр HTML-страниц")
            Case "html_presentation" : Return Localization.T("Вид страницы")
            Case "html_zoom" : Return Localization.T("Масштаб, %")
            Case "html_runtime_status" : Return Localization.T("Компонент просмотра страниц")
            Case "html_runtime_repair" : Return Localization.T("Страница загрузки компонента")
            Case "html_theme" : Return Localization.T("Тема документа")
            Case "html_font" : Return Localization.T("Шрифт")
            Case "html_font_size" : Return Localization.T("Размер текста")
            Case "html_line_height" : Return Localization.T("Межстрочный интервал")
            Case "html_column_width" : Return Localization.T("Ширина текста")
            Case "html_margin" : Return Localization.T("Поля по горизонтали")
            Case "html_wrap" : Return Localization.T("Переносить широкое содержимое")
            Case "html_remember_appearance" : Return Localization.T("Запоминать оформление HTML-страниц")
            Case "html_reset_appearance" : Return Localization.T("Сбросить оформление HTML-страниц")
            Case "html_external_links" : Return Localization.T("Ссылки на другие сайты")
            Case "html_find_case" : Return Localization.T("Учитывать регистр")
            Case "html_find_word" : Return Localization.T("Только слова целиком")
            Case "html_remember_position" : Return Localization.T("Запоминать позицию в HTML-страницах")
            Case "html_online_enabled" : Return Localization.T("Страницы из интернета")
            Case "html_remote_note" : Return Localization.T("Внешние ресурсы локальных страниц")
            Case "html_history_limit" : Return Localization.T("Страниц в истории чтения")
            Case "html_snapshot_cache" : Return Localization.T("Кэш HTML-страниц")
            Case "html_clear_history" : Return Localization.T("Очистить историю HTML-страниц")
            Case "html_clear_runtime" : Return Localization.T("Очистить временные данные просмотра")
            Case Else : Return key
        End Select
    End Function

    Private Function HtmlSettingDescription(key As String) As String
        Select Case key
            Case "html_enabled" : Return Localization.T("Показывает HTML-файлы как страницы в защищённом окне: скрипты, формы и загрузки не выполняются, файл не изменяется. Пока параметр выключен, HTML открывается как исходный текст.")
            Case "html_presentation" : Return Localization.T("«Оригинал» показывает страницу так, как её оформил автор. «Режим чтения» применяет тему, шрифт, ширину и поля из этой группы. Файл не изменяется.")
            Case "html_zoom" : Return Localization.T("Начальный масштаб страницы. Для отдельной страницы его можно изменить при чтении.")
            Case "html_runtime_status" : Return If(htmlRuntimeStatusText.Length > 0, htmlRuntimeStatusText,
                Localization.T("Проверка выполняется только по кнопке: ничего не устанавливается и не скачивается."))
            Case "html_runtime_repair" : Return Localization.T("Открывает в браузере страницу Microsoft, где компонент можно установить или обновить. Страница открывается только по щелчку.")
            Case "html_theme" : Return Localization.T("Цвета страницы в режиме чтения. «Как в системе» следует теме Windows.")
            Case "html_font" : Return Localization.T("Шрифт страницы в режиме чтения. «Как в документе» оставляет шрифт автора.")
            Case "html_font_size" : Return Localization.T("Размер текста в режиме чтения в пикселях страницы; не зависит от масштаба экрана.")
            Case "html_line_height" : Return Localization.T("Расстояние между строками в режиме чтения.")
            Case "html_column_width" : Return Localization.T("Ширина колонки текста в режиме чтения в символах. «На всю ширину» не ограничивает колонку.")
            Case "html_margin" : Return Localization.T("Поля страницы в режиме чтения в пикселях страницы; размер окна не меняется.")
            Case "html_wrap" : Return Localization.T("Длинные строки таблиц и блоков кода переносятся, а не прокручиваются в стороны.")
            Case "html_remember_appearance" : Return Localization.T("Помнит тему, шрифт и масштаб, выбранные для отдельной страницы.")
            Case "html_reset_appearance" : Return Localization.T("Возвращает оформление HTML к значениям по умолчанию и убирает оформление отдельных страниц. Позиции чтения остаются.")
            Case "html_external_links" : Return Localization.T("Что делать со ссылкой на другой сайт: «Спрашивать» открывает браузер после подтверждения, «Блокировать» не открывает ничего.")
            Case "html_find_case" : Return Localization.T("Поиск по странице различает заглавные и строчные буквы; применяется к новому поиску.")
            Case "html_find_word" : Return Localization.T("Поиск находит только слова целиком; границы слов определяет компонент просмотра с учётом языка.")
            Case "html_remember_position" : Return Localization.T("Помнит место, где вы остановились. Выключение прекращает новые записи; уже сохранённое удаляет очистка истории.")
            Case "html_online_enabled" : Return Localization.T("Разрешает открывать HTML-страницу по адресу в интернете. Такие страницы открываются отдельно от локальных, без скриптов и входа в учётные записи.")
            Case "html_remote_note" : Return Localization.T("Загрузку картинок и стилей с других сайтов для локальной страницы вы разрешаете при её открытии, отдельно для каждого сайта и только на текущий сеанс. Здесь это не сохраняется.")
            Case "html_history_limit" : Return Localization.T("Сколько страниц помнит программа. Ноль отключает запись позиций и оформления.")
            Case "html_snapshot_cache" : Return Localization.T("Сколько памяти занимают копии открытых страниц и их ресурсов; входит в общий кэш документов.")
            Case "html_clear_history" : Return Localization.T("Удаляет позиции чтения и оформление отдельных HTML-страниц. Сами файлы и общие настройки не меняются.")
            Case "html_clear_runtime" : Return Localization.T("Удаляет временные данные, которые создаёт окно просмотра. Профили браузера не затрагиваются.")
            Case Else : Return key
        End Select
    End Function
End Class
#End If
