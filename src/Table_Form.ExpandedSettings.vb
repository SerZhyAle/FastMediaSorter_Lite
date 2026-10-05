#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Windows.Forms
Imports System.Linq
Imports System.IO
Imports System.Threading.Tasks

Partial Public Class Table_Form

    Private NotInheritable Class PreferenceChoice
        Public Sub New(value As String, text As String)
            Me.Value = value
            Me.Text = text
        End Sub
        Public ReadOnly Value As String
        Public ReadOnly Text As String
        Public Overrides Function ToString() As String
            Return Text
        End Function
    End Class

    ''' <summary>The five Storage usage rows' re-measure actions and their tickets
    ''' (130 §3.3): opening the Storage page re-runs the measures, closing the window
    ''' invalidates every outstanding one.</summary>
    Private ReadOnly cacheUsageRefresh As New List(Of Action)()
    Private ReadOnly cacheUsageGates As New List(Of RefreshGate)()

    ''' <summary>The "include personal data" opt-in of §7.4. Deliberately a field rather
    ''' than a preference: it is a decision about one export, not a setting.</summary>
    Private exportPersonalData As Boolean
    Private ocrBudgetRefresh As Action

    ''' <summary>Actions that refresh choice combo items, button captions, external preference values,
    ''' and cache measurements on language change and display preparation (057.S14-1).</summary>
    Private ReadOnly expandedRefreshActions As New List(Of Action)()

    Friend Sub RefreshExpandedSettingsRows()
        For Each refreshAction As Action In expandedRefreshActions
            refreshAction()
        Next
        ' Choice items and command captions change after the initial localization pass.
        ' Measure the final localized editors before assigning the shared page columns.
        LayoutModernSettingsPages()
    End Sub

    Private Sub AddExpandedSettingsRows(destinations As FlowLayoutPanel, viewing As FlowLayoutPanel,
                                        video As FlowLayoutPanel, audio As FlowLayoutPanel, files As FlowLayoutPanel,
                                        ocr As FlowLayoutPanel)
        Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()

        AddSectionHeader(destinations, "section_overlay_layout")
        AddPreferenceChoice(destinations, "recipients_position",
                            Function() {Choice("topLeft", "Вверху слева"), Choice("topRight", "Вверху справа"), Choice("bottomLeft", "Внизу слева"), Choice("bottomRight", "Внизу справа")},
                            Function() Main_Form.GetModernPreferences().RecipientsOverlayPosition,
                            Sub(v) Main_Form.GetModernPreferences().RecipientsOverlayPosition = v)
        AddPreferenceNumber(destinations, "recipients_width", 180, 520,
                            Function() Main_Form.GetModernPreferences().RecipientsOverlayWidth,
                            Sub(v) Main_Form.GetModernPreferences().RecipientsOverlayWidth = v)
        AddPreferenceNumber(destinations, "recipients_font", 9, 18,
                            Function() Main_Form.GetModernPreferences().RecipientsOverlayFontSize,
                            Sub(v) Main_Form.GetModernPreferences().RecipientsOverlayFontSize = v)
        AddPreferenceNumber(destinations, "recipients_opacity", 40, 100,
                            Function() Main_Form.GetModernPreferences().RecipientsOverlayOpacity,
                            Sub(v) Main_Form.GetModernPreferences().RecipientsOverlayOpacity = v)
        AddPreferenceNumber(destinations, "recipients_rows", 3, 11,
                            Function() Main_Form.GetModernPreferences().RecipientsOverlayVisibleRows,
                            Sub(v) Main_Form.GetModernPreferences().RecipientsOverlayVisibleRows = v)

        AddSectionHeader(viewing, "section_accessibility")
        AddInterfaceScaleRow(viewing)
        AddPreferenceChoice(viewing, "new_image_scale",
                            Function() {Choice("fit", "Вписать"), Choice("actual", "100 %"), Choice("perFolder", "Запоминать для папки")},
                            Function() Main_Form.GetModernPreferences().NewImageScaleMode,
                            Sub(v) Main_Form.GetModernPreferences().NewImageScaleMode = v)
        AddPreferenceCheck(viewing, "reduce_motion",
                           Function() Main_Form.GetModernPreferences().ReduceMotion,
                           Sub(v) Main_Form.GetModernPreferences().ReduceMotion = v)
        AddPreferenceCheck(viewing, "chrome_pinned",
                           Function() Main_Form.GetModernPreferences().ChromePinned,
                           Sub(v) Main_Form.GetModernPreferences().ChromePinned = v)
        AddPreferenceChoice(viewing, "chrome_size",
                            Function() {Choice("small", "Маленький"), Choice("medium", "Средний"), Choice("large", "Большой"), Choice("adaptive", "Адаптивный"), Choice("touch", "Крупные цели (касание)")},
                            Function() Main_Form.GetModernPreferences().ChromeSize,
                            Sub(v) Main_Form.GetModernPreferences().ChromeSize = v)
        AddPreferenceSlider(viewing, "chrome_opacity", 0, 100,
                            Function() Main_Form.GetModernPreferences().ChromeOpacityPercent,
                            Sub(v) Main_Form.GetModernPreferences().ChromeOpacityPercent = v)
        AddPreferenceNumber(viewing, "chrome_corners", 0, 16,
                            Function() Main_Form.GetModernPreferences().ChromeCornerRadius,
                            Sub(v) Main_Form.GetModernPreferences().ChromeCornerRadius = v)
        AddSectionHeader(viewing, "section_slideshow_behavior")
        AddPreferenceChoice(viewing, "slideshow_random_order",
                            Function() {Choice("natural", "Обычный порядок"), Choice("random", "Случайный"), Choice("shuffleCycle", "Без повторов до конца цикла")},
                            Function() Main_Form.GetModernPreferences().SlideshowRandomOrder,
                            Sub(v) Main_Form.GetModernPreferences().SlideshowRandomOrder = v)
        AddPreferenceCheck(viewing, "stop_slideshow_manual",
                           Function() Main_Form.GetModernPreferences().StopSlideshowOnManualNavigation,
                           Sub(v) Main_Form.GetModernPreferences().StopSlideshowOnManualNavigation = v)
        AddPreferenceChoice(viewing, "slideshow_ui",
                            Function() {Choice("none", "Не скрывать"), Choice("toolbar", "Скрывать панель инструментов"), Choice("toolbarAndStatus", "Скрывать панель и статус")},
                            Function() Main_Form.GetModernPreferences().SlideshowUiMode,
                            Sub(v) Main_Form.GetModernPreferences().SlideshowUiMode = v)
        ' 119 Ф-6, modern only: stillness before the pointer hides itself in a slideshow
        ' or full-screen viewing; 0 = never.
        AddPreferenceNumber(viewing, "video_hide_cursor", 0, 30,
                            Function() Main_Form.GetModernPreferences().HideCursorAfterSec,
                            Sub(v) Main_Form.GetModernPreferences().HideCursorAfterSec = v)

        AddSectionHeader(video, "section_video_behavior")
        AddNavigationKindCheck(video, "include_video_navigation",
                               Function() Main_Form.GetModernPreferences().IncludeVideoInNavigation,
                               Sub(v) Main_Form.GetModernPreferences().IncludeVideoInNavigation = v)
        AddPreferenceCheck(video, "video_autoplay",
                           Function() Main_Form.GetModernPreferences().VideoAutoplay,
                           Sub(v) Main_Form.GetModernPreferences().VideoAutoplay = v)
        AddPreferenceCheck(video, "video_remember_position",
                           Function() Main_Form.GetModernPreferences().RememberVideoPosition,
                           Sub(v) Main_Form.GetModernPreferences().RememberVideoPosition = v)
        AddPreferenceNumber(video, "video_controls_delay", 1, 15,
                            Function() Main_Form.GetModernPreferences().VideoControlsHideDelaySec,
                            Sub(v) Main_Form.GetModernPreferences().VideoControlsHideDelaySec = v)
        AddPreferenceCheck(video, "video_controls_paused",
                           Function() Main_Form.GetModernPreferences().ShowVideoControlsWhenPaused,
                           Sub(v) Main_Form.GetModernPreferences().ShowVideoControlsWhenPaused = v)
        AddPreferenceChoice(video, "video_click_action",
                            Function() {Choice("pauseResume", "Пауза / продолжить"), Choice("nextFile", "Следующий файл")},
                            Function() Main_Form.GetModernPreferences().VideoSingleClickAction,
                            Sub(v) Main_Form.GetModernPreferences().VideoSingleClickAction = v)
        AddPreferenceChoice(video, "video_end_action",
                            Function() {Choice("stay", "Оставить последний кадр"), Choice("nextFile", "Следующий файл"), Choice("repeat", "Повторить")},
                            Function() Main_Form.GetModernPreferences().VideoEndAction,
                            Sub(v) Main_Form.GetModernPreferences().VideoEndAction = v)
        ' The three keyboard seek steps of 119 Ф-2: Shift+arrows / Alt+arrows / Ctrl+arrows.
        AddPreferenceNumber(video, "video_seek_short", 1, 600,
                            Function() Main_Form.GetModernPreferences().SeekShortSec,
                            Sub(v) Main_Form.GetModernPreferences().SeekShortSec = v)
        AddPreferenceNumber(video, "video_seek_medium", 1, 600,
                            Function() Main_Form.GetModernPreferences().SeekMediumSec,
                            Sub(v) Main_Form.GetModernPreferences().SeekMediumSec = v)
        AddPreferenceNumber(video, "video_seek_long", 1, 600,
                            Function() Main_Form.GetModernPreferences().SeekLongSec,
                            Sub(v) Main_Form.GetModernPreferences().SeekLongSec = v)
        AddSectionHeader(audio, "section_audio_behavior")
        AddNavigationKindCheck(audio, "include_audio_navigation",
                               Function() Main_Form.GetModernPreferences().IncludeAudioInNavigation,
                               Sub(v) Main_Form.GetModernPreferences().IncludeAudioInNavigation = v)
        AddPreferenceChoice(audio, "audio_end_action",
                            Function() {Choice("next", "Следующий файл"), Choice("stay", "Остановиться"), Choice("repeat", "Повторить")},
                            Function() Main_Form.GetModernPreferences().AudioEndAction,
                            Sub(v) Main_Form.GetModernPreferences().AudioEndAction = v)
        AddPreferenceCheck(audio, "audio_controls_visible",
                           Function() Main_Form.GetModernPreferences().AudioControlsAlwaysVisible,
                           Sub(v) Main_Form.GetModernPreferences().AudioControlsAlwaysVisible = v)
        AddPreferenceCheck(audio, "audio_visualiser",
                           Function() Main_Form.GetModernPreferences().AudioVisualiser,
                           Sub(v) Main_Form.GetModernPreferences().AudioVisualiser = v)
        AddPreferenceNumber(audio, "audio_sleep_timer", 0, 180,
                            Function() Main_Form.GetModernPreferences().SleepTimerMinutes,
                            Sub(v) Main_Form.GetModernPreferences().SleepTimerMinutes = v)
        AddPreferenceChoice(audio, "preferred_audio_language",
                            Function() TrackLanguageChoices(subtitles:=False),
                            Function() Main_Form.GetModernPreferences().PreferredAudioLanguage,
                            Sub(v) Main_Form.GetModernPreferences().PreferredAudioLanguage = v)
        AddPreferenceChoice(audio, "preferred_subtitle_language",
                            Function() TrackLanguageChoices(subtitles:=True),
                            Function() Main_Form.GetModernPreferences().PreferredSubtitleLanguage,
                            Sub(v) Main_Form.GetModernPreferences().PreferredSubtitleLanguage = v)

        AddSectionHeader(files, "section_file_behavior")
        AddPreferenceChoice(files, "name_collision",
                            Function() {Choice("ask", "Спрашивать"), Choice("skip", "Пропустить"), Choice("rename", "Сохранить оба"), Choice("replace", "Заменить")},
                            Function() Main_Form.GetModernPreferences().NameCollisionPolicy,
                            Sub(v) Main_Form.GetModernPreferences().NameCollisionPolicy = v)
        AddPreferenceChoice(files, "after_file_operation",
                            Function() {Choice("next", "Следующий файл"), Choice("stay", "Остаться на текущем"), Choice("closeIfEmpty", "Закрыть, если файлов не осталось")},
                            Function() Main_Form.GetModernPreferences().AfterFileOperation,
                            Sub(v) Main_Form.GetModernPreferences().AfterFileOperation = v)
        AddPreferenceCheck(files, "create_missing_destination",
                           Function() Main_Form.GetModernPreferences().CreateMissingDestination,
                           Sub(v) Main_Form.GetModernPreferences().CreateMissingDestination = v)
        AddPreferenceCheck(files, "delete_to_recycle_bin",
                           Function() Main_Form.GetModernPreferences().DeleteToRecycleBin,
                           Sub(v) Main_Form.GetModernPreferences().DeleteToRecycleBin = v)
        AddPreferenceChoice(files, "confirm_delete",
                            Function() {Choice("always", "Всегда"), Choice("permanentOnly", "Только если файл не попадёт в Корзину"), Choice("never", "Никогда")},
                            Function() Main_Form.GetModernPreferences().ConfirmDelete,
                            Sub(v) Main_Form.GetModernPreferences().ConfirmDelete = v)
        AddPreferenceCheck(files, "include_subfolders",
                           Function() Main_Form.GetModernPreferences().IncludeSubfolders,
                           Sub(v) Main_Form.GetModernPreferences().IncludeSubfolders = v)
        AddFileTypesRow(files)
        AddNavigationKindCheck(files, "include_document_navigation",
                               Function() Main_Form.GetModernPreferences().IncludeDocumentInNavigation,
                               Sub(v) Main_Form.GetModernPreferences().IncludeDocumentInNavigation = v)
        AddContentExtensionsRow(files, "document_extensions", True)
        AddPreferenceNumber(files, "text_preview_max_kb", ModernViewerPreferences.TextPreviewMaxKbMin, ModernViewerPreferences.TextPreviewMaxKbMax,
                            Function() Main_Form.GetModernPreferences().TextPreviewMaxKb,
                            Sub(v) Main_Form.GetModernPreferences().TextPreviewMaxKb = v)
        AddNavigationKindCheck(files, "sort_other_files",
                               Function() Main_Form.GetModernPreferences().SortOtherFiles,
                               Sub(v) Main_Form.GetModernPreferences().SortOtherFiles = v)
        AddContentExtensionsRow(files, "other_extensions", False)
        AddHotkeysRow(files)
        AddPreferenceNumber(files, "recent_files_limit", 0, 200,
                            Function() Main_Form.GetModernPreferences().RecentFilesLimit,
                            Sub(v) Main_Form.GetModernPreferences().RecentFilesLimit = v)
        AddPreferenceNumber(files, "recent_folders_limit", 0, 200,
                            Function() Main_Form.GetModernPreferences().RecentFoldersLimit,
                            Sub(v) Main_Form.GetModernPreferences().RecentFoldersLimit = v)
        AddHistoryRow(files)
        AddPreferenceChoice(files, "startup_open",
                            Function() {Choice("home", "Стартовую страницу"), Choice("lastFolder", "Последнюю папку"), Choice("lastFile", "Последний файл")},
                            Function() Main_Form.GetModernPreferences().StartupOpenMode,
                            Sub(v) Main_Form.GetModernPreferences().StartupOpenMode = v)
        AddPreferenceCheck(files, "resume_last_playback",
                           Function() Main_Form.GetModernPreferences().ResumeLastPlayback,
                           Sub(v) Main_Form.GetModernPreferences().ResumeLastPlayback = v)
        AddSettingsTransferRows(files)
        AddAllowNewWindowsRow(files)

        ' ZIP/CBZ browsing (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §9, §12 Ф4).
        AddSectionHeader(files, "section_archives")
        ' The bounds are the shared constants, not fresh literals: the spinner, the registry
        ' loader and the import normalizer must not be able to drift apart (103).
        AddPreferenceNumber(files, "archive_cache_limit", ModernViewerPreferences.ArchiveCacheMaxMbMin, ModernViewerPreferences.ArchiveCacheMaxMbMax,
                            Function() Main_Form.GetModernPreferences().ArchiveCacheMaxMb,
                            Sub(v) Main_Form.GetModernPreferences().ArchiveCacheMaxMb = v)
        AddPreferenceNumber(files, "archive_entry_limit", ModernViewerPreferences.ArchiveMaxEntryMbMin, ModernViewerPreferences.ArchiveMaxEntryMbMax,
                            Function() Main_Form.GetModernPreferences().ArchiveMaxEntryMb,
                            Sub(v) Main_Form.GetModernPreferences().ArchiveMaxEntryMb = v)
        AddPreferenceNumber(files, "archive_entries_limit", ModernViewerPreferences.ArchiveMaxEntriesMin, ModernViewerPreferences.ArchiveMaxEntriesMax,
                            Function() Main_Form.GetModernPreferences().ArchiveMaxEntries,
                            Sub(v) Main_Form.GetModernPreferences().ArchiveMaxEntries = v)

        ' Secure containers (026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md §8, §9).
        AddSectionHeader(files, "section_secure")
        AddPreferenceCheck(files, "secure_in_navigation",
                           Function() Main_Form.GetModernPreferences().SecureIncludeInNavigation,
                           Sub(v) Main_Form.GetModernPreferences().SecureIncludeInNavigation = v)
        AddPreferenceNumber(files, "secure_remember_minutes", ModernViewerPreferences.SecureRememberMinutesMin, ModernViewerPreferences.SecureRememberMinutesMax,
                            Function() Main_Form.GetModernPreferences().SecureRememberMinutes,
                            Sub(v) Main_Form.GetModernPreferences().SecureRememberMinutes = v)
        AddPreferenceCheck(files, "secure_show_true_name",
                           Function() Main_Form.GetModernPreferences().SecureShowTrueName,
                           Sub(v) Main_Form.GetModernPreferences().SecureShowTrueName = v)
        AddSecureAssociationRow(files)

        AddPreferenceNumber(ocr, "ocr_cache_limit", -1, 1024,
                            Function() If(Not _ocrSettings.DiskCache, 0, If(Main_Form.GetModernPreferences().OcrDiskCacheMaxMb = 0, -1, Main_Form.GetModernPreferences().OcrDiskCacheMaxMb)),
                            Sub(v)
                                _ocrSettings.DiskCache = v <> 0
                                If v <> 0 Then Main_Form.GetModernPreferences().OcrDiskCacheMaxMb = Math.Max(0, v)
                                chkOcrDisk.Checked = _ocrSettings.DiskCache
                                Main_Form.OnOcrSettingsEditedFromSettingsWindow(False, False)
                            End Sub)
        AddPreferenceNumber(ocr, "decode_cache_limit", 0, 8192,
                            Function() Main_Form.GetModernPreferences().DecodeCacheMaxMb,
                            Sub(v) Main_Form.GetModernPreferences().DecodeCacheMaxMb = v)
        AddPreferenceNumber(ocr, "thumb_cache_limit", 0, 4096,
                            Function() Main_Form.GetModernPreferences().PanelThumbCacheMb,
                            Sub(v) Main_Form.GetModernPreferences().PanelThumbCacheMb = v)
        AddPreferenceNumber(ocr, "remote_cache_limit", ModernViewerPreferences.RemoteCacheMaxMbMin, ModernViewerPreferences.RemoteCacheMaxMbMax,
                            Function() Main_Form.GetModernPreferences().RemoteCacheMaxMb,
                            Sub(v) Main_Form.GetModernPreferences().RemoteCacheMaxMb = v)

        ' 130 §3.2/§3.3: one usage+clear row per cache. Where each lands next to its limit
        ' row is the group catalog's decision (AddSettingsGroup in SettingsGroups), never
        ' this construction order.
        AddCacheUsageRow(ocr, "decode_cache_clear",
                         Function() CacheUsageMeasure.FlatBytes(DecodeCacheStore.CacheDir(), DecodeCacheKey.File_Pattern),
                         "Удалить сохранённые результаты декодирования? Сами изображения не изменятся.",
                         Sub() CacheMaintenance.ClearFiles(DecodeCacheStore.CacheDir(), DecodeCacheKey.File_Pattern),
                         "Кэш декодирования очищен.")
        AddCacheUsageRow(ocr, "thumb_cache_clear",
                         Function() CacheUsageMeasure.FlatBytes(ThumbnailCacheStore.DefaultDirectory(), ThumbnailCacheKey.File_Pattern),
                         "Очистить кэш миниатюр?",
                         Sub() CacheMaintenance.ClearFiles(ThumbnailCacheStore.DefaultDirectory(), ThumbnailCacheKey.File_Pattern, "*.tmp"),
                         "Кэш миниатюр очищен.")
        AddCacheUsageRow(ocr, "ocr_cache_clear",
                         Function() CacheUsageMeasure.FlatBytes(OcrPaths.OcrCacheDir(), "*.json"),
                         "Удалить сохранённые результаты распознавания? Настройки OCR не изменятся.",
                         AddressOf Main_Form.ClearOcrResultCacheOnDisk,
                         "Сохранённые результаты распознавания удалены.",
                         Sub() Main_Form.ClearOcrResultCache(memoryOnly:=True))
        AddCacheUsageRow(ocr, "archive_cache_clear",
                         Function() CacheUsageMeasure.TreeBytes(ArchivePaths.CacheRoot()),
                         "Удалить временные копии записей архивов? Открытые архивы продолжат работать.",
                         Sub() CacheMaintenance.ClearSessions(ArchivePaths.CacheRoot(), Date.UtcNow,
                                                             Function(pid) ArchiveTempStore.IsLiveViewerProcess(pid, unknownIsLive:=True)),
                         "Временные копии архивов удалены.")
        AddCacheUsageRow(ocr, "remote_cache_clear",
                         Function() CacheUsageMeasure.TreeBytes(RemoteCachePaths.CacheRoot()),
                         "Удалить временные копии файлов сетевых ресурсов? Активные подключения не пострадают.",
                         Sub() CacheMaintenance.ClearSessions(RemoteCachePaths.CacheRoot(), Date.UtcNow,
                                                             Function(pid) ArchiveTempStore.IsLiveViewerProcess(pid, unknownIsLive:=True)),
                         "Временные копии файлов сетевых ресурсов удалены.")
    End Sub

    ''' <summary>
    ''' One usage+clear row of 130 §3.3 on the 127 anatomy: the cache's measured size, a
    ''' compact Clear beside it and a refresh affordance between them, 8 px apart, flush
    ''' right in the row's value column. <paramref name="measure"/> and
    ''' <paramref name="clearWork"/> are the cache's own facts; the row owns the rest - the
    ''' off-thread measure through a gate (only the newest answer lands; a failed one shows
    ''' "unknown" with the reason on the label's tooltip and Refresh as the retry), the
    ''' confirmed async clear (the safe default stays focused), the success via Notify,
    ''' errors via FailureText, and the re-measure afterwards.
    ''' </summary>
    Private Sub AddCacheUsageRow(flow As FlowLayoutPanel, key As String,
                                 measure As Func(Of Long),
                                 confirmText As String,
                                 clearWork As Action,
                                 successText As String,
                                 Optional afterClear As Action = Nothing)
        Dim usage As New Label With {.AutoSize = False, .Width = Sp(96),
                                     .Height = Sp(SettingsRowGridRules.ButtonHeight),
                                     .TextAlign = Drawing.ContentAlignment.MiddleLeft,
                                     .BackColor = SettingsSurface}
        Dim refreshButton As New Button With {.Text = Localization.T("Обновить")}
        Dim clearButton As New Button With {.Text = Localization.T("Очистить")}
        Dim gate As New RefreshGate()
        cacheUsageGates.Add(gate)
        Dim unknownText As String = Localization.T("Неизвестно")
        Dim clearing As Boolean = False

        Dim refresh As Action =
            Async Sub()
                If clearing OrElse Not CacheUsageRowAlive(usage) Then Return
                Dim ticket As Integer = gate.Begin()
                usage.Text = unknownText
                If toolTip IsNot Nothing Then toolTip.SetToolTip(usage, String.Empty)
                Try
                    Dim bytes As Long = Await Task.Run(measure)
                    If Not CacheUsageRowAlive(usage) OrElse Not gate.IsLatest(ticket) Then Return
                    usage.Text = Localization.TF("{0} МБ", Megabytes(bytes))
                    If toolTip IsNot Nothing Then toolTip.SetToolTip(usage, String.Empty)
                Catch ex As Exception
                    If Not CacheUsageRowAlive(usage) OrElse Not gate.IsLatest(ticket) Then Return
                    AppFileLogger.LogException("Settings: measuring " & key, ex)
                    ' 130 §3.3: a failed measurement is never a stale number presented as
                    ' fresh - the label says unknown, the tooltip names the reason and the
                    ' refresh button beside it is the retry.
                    usage.Text = unknownText
                    If toolTip IsNot Nothing Then
                        toolTip.SetToolTip(usage, Localization.TF("Не удалось измерить: {0}. Кнопка «Обновить» повторит попытку.", FailureText.Describe(ex)))
                    End If
                End Try
            End Sub
        Dim localize As Action =
            Sub()
                refreshButton.Text = Localization.T("Обновить")
                clearButton.Text = Localization.T("Очистить")
                unknownText = Localization.T("Неизвестно")
                refresh()
            End Sub
        localize()

        AddHandler refreshButton.Click, Sub() refresh()
        AddHandler clearButton.Click,
            Async Sub()
                If clearing OrElse Not CacheUsageRowAlive(usage) Then Return
                If ViewerBox.Show(Me, Localization.T(confirmText), Localization.T("Настройки"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
                clearing = True
                gate.Begin()
                usage.Text = unknownText
                clearButton.Enabled = False
                refreshButton.Enabled = False
                Try
                    Await Task.Run(clearWork)
                    If IsDisposed OrElse Disposing Then Return
                    If afterClear IsNot Nothing Then afterClear()
                    Main_Form.Notify(Localization.T(successText), NotifyLevel.Info)
                Catch ex As Exception
                    AppFileLogger.LogException("Settings: clearing " & key, ex)
                    If Not IsDisposed AndAlso Not Disposing Then
                        ViewerBox.Show(Me, FailureText.Describe(ex), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End If
                Finally
                    clearing = False
                    If Not clearButton.IsDisposed Then clearButton.Enabled = True
                    If Not refreshButton.IsDisposed Then refreshButton.Enabled = True
                End Try
                refresh()
            End Sub

        Dim strip As FlowLayoutPanel = MakeModernControlStrip({usage, refreshButton, clearButton}, 320)
        expandedRefreshActions.Add(localize)
        cacheUsageRefresh.Add(refresh)
        AddSettingRow(flow, key, strip, 320, True)
    End Sub

    ''' <summary>False once the window or the row's own label has gone: a late measurement
    ''' must not touch controls that no longer exist.</summary>
    Private Function CacheUsageRowAlive(usage As Label) As Boolean
        Return Not IsDisposed AndAlso Not Disposing AndAlso usage IsNot Nothing AndAlso Not usage.IsDisposed
    End Function

    ''' <summary>Re-measures every cache row - what opening the Storage page means
    ''' (130 §3.3). Wired to the tab's SelectedIndexChanged in SettingsDiscovery.</summary>
    Friend Sub RefreshCacheUsageMeasurements()
        For Each refreshUsage As Action In cacheUsageRefresh
            refreshUsage()
        Next
    End Sub

    ''' <summary>
    ''' §4.1. The scale changes the base font and the metrics of this window, and those
    ''' are read once while it is being built - so the honest way to apply it is to build
    ''' the window again, which is exactly what the specification asks to confirm first.
    ''' </summary>
    Private Sub AddInterfaceScaleRow(viewing As FlowLayoutPanel)
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    Dim choices As PreferenceChoice() = {Choice("0", "Системный"), Choice("90", "90 %"), Choice("100", "100 %"),
                                                         Choice("110", "110 %"), Choice("125", "125 %"), Choice("150", "150 %")}
                    combo.BeginUpdate()
                    combo.Items.Clear()
                    combo.Items.AddRange(choices)
                    Dim currentValue As String = Main_Form.GetModernPreferences().InterfaceScaleRelativePercent.ToString(Globalization.CultureInfo.InvariantCulture)
                    Dim absolute As Integer = Main_Form.GetModernPreferences().InterfaceScalePercent
                    If absolute > 0 AndAlso currentValue = "0" Then
                        Dim legacy As New PreferenceChoice("legacy", absolute.ToString(Globalization.CultureInfo.CurrentCulture) & " % (96 DPI)")
                        combo.Items.Add(legacy)
                        combo.SelectedItem = legacy
                    Else
                        combo.SelectedItem = If(choices.FirstOrDefault(Function(item) item.Value = currentValue), choices(0))
                    End If
                    combo.EndUpdate()
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler combo.SelectedIndexChanged,
            Sub()
                If isUpdating Then Return
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing OrElse choice.Value = "legacy" Then Return
                Dim percent As Integer = Integer.Parse(choice.Value, Globalization.CultureInfo.InvariantCulture)
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                If percent = p.InterfaceScaleRelativePercent AndAlso p.InterfaceScalePercent = 0 Then Return

                p.InterfaceScalePercent = 0
                p.InterfaceScaleRelativePercent = percent
                ApplyExpandedPreferences()
                If ViewerBox.Show(Me, Localization.T("Новый масштаб применится после перезапуска окна настроек. Перезапустить сейчас?"),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                Main_Form.RequestSettingsWindowRestart()
                Close()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(viewing, "interface_scale", combo, 230, True)
    End Sub

    ''' <summary>§3.4. The row is a button, not a list of extensions: the answer is
    ''' thirty-odd formats in four groups, and a semicolon-separated field could not be
    ''' checked by eye.</summary>
    Private Sub AddFileTypesRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action = Sub() button.Text = Localization.T("Настроить..")
        refresh()
        AddHandler button.Click,
            Sub()
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                Using dialog As New File_Types_Form(Main_Form.SupportedExtensionGroups(), CurrentExtensionFilter(p))
                    If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return
                    p.SetIncludedExtensionsFromEditing(String.Join(";", dialog.Selection))
                    Main_Form.ApplyMediaKindNavigationPreferenceChange()
                End Using
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "included_extensions", button, 230, True)
    End Sub

    Private Sub AddContentExtensionsRow(files As FlowLayoutPanel, key As String, documents As Boolean)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action = Sub() button.Text = Localization.T("Настроить..")
        refresh()
        AddHandler button.Click,
            Sub()
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                Using dialog As New Form With {.Text = Localization.T(If(documents, "Документы", "Другие")),
                    .ClientSize = New Size(460, 140), .StartPosition = FormStartPosition.CenterParent,
                    .FormBorderStyle = FormBorderStyle.FixedDialog, .MinimizeBox = False, .MaximizeBox = False}
                    Dim values = If(documents, p.DocumentExtensionSet(), p.OtherExtensionSet())
                    Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(12)}
                    layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                    layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                    layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                    Dim label As New Label With {.AutoSize = True, .MaximumSize = New Size(420, 0),
                        .Text = Localization.T("Расширения через пробел или точку с запятой, например .pdf;.txt. Без масок.")}
                    Dim input As New TextBox With {.Text = String.Join(";", values.OrderBy(Function(ext) ext)), .Dock = DockStyle.Top, .AccessibleName = label.Text}
                    Dim ok As New Button With {.Text = Localization.T("ОК"), .DialogResult = DialogResult.OK, .AutoSize = True}
                    Dim cancel As New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .AutoSize = True}
                    Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                    buttons.Controls.AddRange({cancel, ok})
                    layout.Controls.Add(label, 0, 0)
                    layout.Controls.Add(input, 0, 1)
                    layout.Controls.Add(buttons, 0, 2)
                    dialog.Controls.Add(layout)
                    AddHandler dialog.FormClosing,
                        Sub(sender, args)
                            If dialog.DialogResult <> DialogResult.OK OrElse SettingsSearchRules.IsExtensionList(input.Text) Then Return
                            args.Cancel = True
                            ViewerBox.Show(dialog, label.Text, Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            input.Focus()
                        End Sub
                    dialog.AcceptButton = ok
                    dialog.CancelButton = cancel
                    ' Literal 96-dpi pixels in a PerMonitorV2 process: scale them with the window (124 UX-202).
                    DialogLayout.ApplyAutoScale(dialog)
                    If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return
                    If documents Then
                        p.DocumentExtensions = input.Text
                    Else
                        p.OtherExtensions = input.Text
                    End If
                    Main_Form.ApplyMediaKindNavigationPreferenceChange()
                End Using
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, key, button, 230, True)
    End Sub

    Private Shared Function CurrentExtensionFilter(p As ModernViewerPreferences) As List(Of String)
        Return p.IncludedExtensionsForEditing().
            Split(";"c).
            Select(Function(item) item.Trim()).
            Where(Function(item) item.Length > 0).
            ToList()
    End Function

    ''' <summary>§3.5. Also its own dialog, and for the same reason.</summary>
    Private Sub AddHotkeysRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action = Sub() button.Text = Localization.T("Настроить..")
        refresh()
        AddHandler button.Click,
            Sub()
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                Using dialog As New Hotkeys_Form(CustomHotkeys.Load(p.CustomHotkeysJson))
                    If DialogLayer.ShowViewerDialog(dialog, Me) <> DialogResult.OK Then Return
                    p.CustomHotkeysJson = CustomHotkeys.Save(dialog.Result)
                    ApplyExpandedPreferences()
                    Main_Form.ReloadCustomHotkeys()
                End Using
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "custom_hotkeys", button, 230, True)
    End Sub

    ''' <summary>§7.2. Opening an entry closes this window: the user asked to look at a
    ''' picture, not to come back to a settings page.</summary>
    Private Sub AddHistoryRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action = Sub() button.Text = Localization.T("Открыть историю")
        refresh()
        AddHandler button.Click,
            Sub()
                Dim chosen As String = Nothing
                Using dialog As New History_Form(Main_Form.RecentFilesSnapshot(), Main_Form.RecentFoldersSnapshot())
                    AddHandler dialog.EntryChosen, Sub(entry) chosen = entry
                    DialogLayer.ShowViewerDialog(dialog, Me)
                    Main_Form.ReplaceRecentHistory(dialog.Files_Result, dialog.Folders_Result)
                End Using
                If String.IsNullOrEmpty(chosen) Then Return
                Close()
                Main_Form.OpenHistoryEntry(chosen)
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "recent_history", button, 230, True)
    End Sub

    ''' <summary>
    ''' The Explorer opt-in of §8, and both directions from the same place.
    '''
    ''' A button, not a checkbox (ticket 028 Ф-5, APP-BEHAVIOUR rule 12): pressing it
    ''' writes the registry there and then, and an operation does not ride on a checkbox in
    ''' a window of values. Its caption is the action it will take, derived from the
    ''' registry every time, so a user who removed the entry in Explorer sees "Add".
    ''' It adds this viewer to "Open with" and takes nothing away - .fd-sec belongs to
    ''' whatever already owns it, and FileDO owns it correctly.
    ''' </summary>
    Private Sub AddSecureAssociationRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action =
            Sub()
                button.Text = If(Main_Form.IsSecureContainerOpenWithRegistered(),
                                 Localization.T("Убрать из «Открыть с помощью»"),
                                 Localization.T("Добавить в «Открыть с помощью»"))
            End Sub
        refresh()
        AddHandler button.Click,
            Sub()
                Dim ok As Boolean = If(Main_Form.IsSecureContainerOpenWithRegistered(),
                                       Main_Form.UnregisterSecureContainerOpenWith(),
                                       Main_Form.RegisterSecureContainerOpenWith())
                If Not ok Then
                    ViewerBox.Show(Me, Localization.T("Не удалось изменить запись в реестре"),
                                    Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                ' Whatever the registry actually says now, not what was asked for.
                refresh()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "secure_open_with", button, 230, True)
    End Sub

    ''' <summary>The window is closing: every outstanding cache measurement is invalidated,
    ''' so a late answer cannot touch a label that no longer exists.</summary>
    Private Sub InvalidateCacheMeasurements(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        For Each gate As RefreshGate In cacheUsageGates
            gate.Begin()
        Next
    End Sub

    Private Shared Function Megabytes(bytes As Long) As String
        Return (bytes / 1048576.0R).ToString("0.0", Globalization.CultureInfo.CurrentCulture)
    End Function

    ''' <summary>
    ''' §6.3. "Do not choose" is the default, the system language sits next to it as a
    ''' real code (not a sentinel - the matcher compares codes), then the catalogue.
    ''' Subtitles get one extra entry that audio must not have: always off.
    ''' </summary>
    Private Shared Function TrackLanguageChoices(subtitles As Boolean) As PreferenceChoice()
        Dim choices As New List(Of PreferenceChoice) From {Choice("", "Не выбирать")}
        If subtitles Then choices.Add(Choice("::off::", "Всегда выключены"))

        Dim systemCode As String = Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
        For Each language As LanguageEntry In OcrLanguageCatalog.TargetLanguages()
            Dim caption As String = language.DisplayName()
            If String.Equals(language.Code, systemCode, StringComparison.OrdinalIgnoreCase) Then
                caption = Localization.TF("{0} - язык системы", caption)
            End If
            choices.Add(New PreferenceChoice(language.Code, caption))
        Next
        Return choices.ToArray()
    End Function

    Private Sub AddSettingsTransferRows(files As FlowLayoutPanel)
        Dim personalCheck As New CheckBox With {.Checked = exportPersonalData, .AutoSize = True}
        AddHandler personalCheck.CheckedChanged, Sub() exportPersonalData = personalCheck.Checked

        Dim exportButton As New Button With {.AutoSize = True}
        Dim importButton As New Button With {.AutoSize = True}

        Dim refresh As Action =
            Sub()
                If toolTip IsNot Nothing Then
                    toolTip.SetToolTip(personalCheck, Localization.T("Включает в файл историю папок и позиции просмотра видео. API-ключи и пароли не экспортируются никогда."))
                End If
                exportButton.Text = Localization.T("Экспорт дополнительных настроек")
                importButton.Text = Localization.T("Импорт дополнительных настроек")
            End Sub
        refresh()

        AddHandler exportButton.Click,
            Sub()
                If exportPersonalData AndAlso
                   ViewerBox.Show(Me, Localization.T("В файл попадут пути к вашим папкам и позиции просмотра. Продолжить?"),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return

                Using dialog As New SaveFileDialog With {.Filter = Localization.TF("{0} - настройки|*.fms-settings.json", ProductNames.Compact), .DefaultExt = "fms-settings.json", .AddExtension = True}
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    Try
                        File.WriteAllText(dialog.FileName, Main_Form.GetModernPreferences().ExportJson(exportPersonalData))
                        ViewerBox.Show(Me, Localization.T("Настройки экспортированы."), Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        AppFileLogger.LogException("Settings export", ex)
                        ViewerBox.Show(Me, Localization.TF("Не удалось экспортировать настройки: {0}", FailureText.Describe(ex)), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub

        AddHandler importButton.Click, AddressOf ImportSettings

        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "settings_export_personal", personalCheck, 34, True)
        AddSettingRow(files, "settings_export", exportButton, 210, True)
        AddSettingRow(files, "settings_import", importButton, 210, True)
        Dim reset As New Button With {.AutoSize = True}
        expandedRefreshActions.Add(Sub() reset.Text = Localization.T("Восстановить по умолчанию"))
        reset.Text = Localization.T("Восстановить по умолчанию")
        AddHandler reset.Click,
            Sub()
                Dim defaults As New ModernViewerPreferences()
                Dim current = Main_Form.GetModernPreferences()
                defaults.FolderZoomHistory = current.FolderZoomHistory
                defaults.VideoPositionHistory = current.VideoPositionHistory
                defaults.DocumentPageHistory = current.DocumentPageHistory
                If ViewerBox.Show(Me, Localization.T("Восстановить дополнительные настройки по умолчанию? Получатели, язык, тема, звук, OCR и история сохранятся."),
                    Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
                ApplyImportedPreferencesWithBackup(current, defaults)
            End Sub
        AddSettingRow(files, "settings_defaults", reset, 230, True)
    End Sub

    ''' <summary>
    ''' §7.4 / 103 / 105 L12. The order is the contract: read and validate (FromJson already
    ''' normalizes) -> work out what Replace AND Merge would really change -> ask -> take the
    ''' app-private backup (no backup, no import) -> apply into the live object -> persist ->
    ''' rebuild the window. Cancel, a refused backup or an unreadable file leave the model, the
    ''' registry and the hotkeys exactly as they were.
    ''' </summary>
    Private Sub ImportSettings(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {.Filter = Localization.TF("{0} - настройки|*.fms-settings.json", ProductNames.Compact)}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim imported As ModernViewerPreferences
            Try
                imported = ModernViewerPreferences.FromJson(File.ReadAllText(dialog.FileName))
            Catch ex As Exception
                AppFileLogger.LogException("Settings import", ex)
                ViewerBox.Show(Me, Localization.TF("Не удалось импортировать настройки: {0}", FailureText.Describe(ex)),
                                Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim current As ModernViewerPreferences = Main_Form.GetModernPreferences()
            Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)
            If plan.ReplaceChanges.Count = 0 Then
                ' Nothing to import is reported, not confirmed (APP-BEHAVIOUR rule 5, ticket
                ' 028 Ф-5): no question, no backup, no write.
                ViewerBox.Show(Me, Localization.T("Файл прочитан. Отличий от текущих настроек нет."),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim summary As String =
                Localization.TF("Файл прочитан. Изменится параметров: {0}.", plan.ReplaceChanges.Count) & vbCrLf &
                Localization.TF("При объединении изменится параметров: {0}.", plan.MergeChanges.Count) & vbCrLf & vbCrLf &
                String.Join(vbCrLf, plan.ReplaceChanges.Take(12))

            ' Replace = take everything the document describes, Merge = take only what differs from the
            ' shipped defaults, so a partial file cannot silently reset the rest. Named buttons, Cancel the
            ' default (124 UX-205) - the old Yes/No/Cancel box explained them in a sentence.
            Dim answer As Integer = AskDialogs.Choose(Me, AskSpecs.SettingsImport(summary))
            If answer = AskSpecs.ImportCancel Then Return

            Dim final As ModernViewerPreferences = If(answer = AskSpecs.ImportMerge, plan.Merged, imported)
            If answer = AskSpecs.ImportMerge AndAlso plan.MergeChanges.Count = 0 Then
                ViewerBox.Show(Me, Localization.T("Файл прочитан. Отличий от текущих настроек нет."),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ApplyImportedPreferencesWithBackup(current, final)
        End Using
    End Sub

    Private Sub ApplyImportedPreferencesWithBackup(current As ModernViewerPreferences, final As ModernViewerPreferences)
        Dim backup As String
        Try
            backup = SettingsImportBackupStore.Create(current, SettingsImportBackupStore.DefaultDirectory())
        Catch ex As Exception
            AppFileLogger.LogException("Settings import backup", ex)
            ViewerBox.Show(Me, Localization.TF("Не удалось создать резервную копию: {0}", FailureText.Describe(ex)),
                            Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim persisted As Boolean = Main_Form.ReplaceModernPreferences(final)
        Main_Form.ReloadCustomHotkeys()
        If persisted Then
            ViewerBox.Show(Me, Localization.TF("Настройки импортированы. Резервная копия: {0}", backup) & vbCrLf &
                                Localization.T("Масштаб интерфейса и режим запуска применятся после перезапуска."),
                            Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        Else
            ViewerBox.Show(Me, Localization.TF("Настройки применены, но записать их не удалось. Повторная попытка будет сделана автоматически. Резервная копия: {0}", backup),
                            Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
        ' Every row of this window still shows the value it was built with. Rebuild the
        ' window, the way a scale change does, so what is on screen is what was imported.
        Main_Form.RequestSettingsWindowRestart()
        Close()
    End Sub
    ''' <summary>This value is deliberately persisted right away, including from a
    ''' secondary process, so the next Explorer launch follows the new choice.</summary>
    Private Sub AddAllowNewWindowsRow(files As FlowLayoutPanel)
        Dim check As New CheckBox With {.AutoSize = True}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    check.Checked = Main_Form.GetModernPreferences().AllowNewWindows
                    If toolTip IsNot Nothing Then
                        toolTip.SetToolTip(check, Localization.T("Действует со следующего запуска. Настройки и позицию окна хранит первое окно. Помните: выделив в проводнике десять файлов, вы получите десять окон. По умолчанию выключено."))
                    End If
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler check.CheckedChanged,
            Sub()
                If isUpdating Then Return
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                p.AllowNewWindows = check.Checked
                Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, "AllowNewWindows", If(check.Checked, "1", "0"))
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "allow_new_windows", check, 34, True)
    End Sub

    ''' <summary>
    ''' The checkbox that replaced the global "copy mode" on the Files page: it does not
    ''' decide WHAT a recipient key does (that is now said at the moment of the action),
    ''' only what the view does after a copy. Built here, in code, because it is a
    ''' mainline-only setting - the Designer's control set is shared with net48, which
    ''' keeps its old copy-mode checkbox instead (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.1).
    ''' </summary>
    Private Sub AddAdvanceAfterCopyRow(files As FlowLayoutPanel)
        Dim check As New CheckBox With {.AutoSize = True}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    check.Checked = Main_Form.GetModernPreferences().AdvanceAfterCopy
                    If toolTip IsNot Nothing Then
                        toolTip.SetToolTip(check, Localization.T("Если отмечено, после копирования программа сразу показывает следующий файл. Если снять галочку, после копирования остаётся текущий файл. По умолчанию включено."))
                    End If
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler check.CheckedChanged,
            Sub()
                If isUpdating Then Return
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                p.AdvanceAfterCopy = check.Checked
                ApplyExpandedPreferences()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "advance_after_copy", check, 34, True)
    End Sub

    ''' <summary>
    ''' The interface language (APP-SETTINGS rule 5): on the first page, every shipped language
    ''' by its endonym and never by a flag (ICON-EXTERNAL rule 6), applied at once to every open
    ''' window without a restart. The toolbar menu writes the same setting through
    ''' Main_Form.SetUiLanguage, so the selected entry is re-read on every refresh.
    ''' </summary>
    Private Sub AddUiLanguageRow(flow As FlowLayoutPanel)
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Name = "cmbox_Ui_Language"}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    If combo.Items.Count = 0 Then
                        For index As Integer = 0 To Localization.Codes.Length - 1
                            combo.Items.Add(New PreferenceChoice(Localization.Codes(index), Localization.Names(index)))
                        Next
                    End If
                    combo.SelectedIndex = Math.Max(0, Localization.CurrentIndex)
                    combo.AccessibleName = Localization.T("Язык интерфейса")
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler combo.SelectedIndexChanged,
            Sub()
                If isUpdating Then Return
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing OrElse choice.Value = Localization.CurrentCode Then Return
                Main_Form.SetUiLanguage(choice.Value)
                LngCh()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, "interface_language", combo, 230, True)
    End Sub

    ''' <summary>
    ''' The application theme (060_SPECIFICATION_APP_THEME_SYSTEM_LIGHT_DARK.md §4). Not a
    ''' ModernViewerPreferences field: the value is shared with the Share Manager, so it lives in
    ''' the hive both programs read, and ThemeService owns it - storing, applying to every open
    ''' window at once, and telling the other program.
    ''' </summary>
    Private Sub AddUiThemeRow(flow As FlowLayoutPanel)
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    Dim choices As PreferenceChoice() = {Choice("system", "Как в системе"), Choice("light", "Светлая"), Choice("dark", "Тёмная")}
                    combo.BeginUpdate()
                    combo.Items.Clear()
                    combo.Items.AddRange(choices)
                    Dim current As String = ThemeModes.ToSetting(ThemeService.Mode)
                    combo.SelectedItem = If(choices.FirstOrDefault(Function(item) item.Value = current), choices(0))
                    combo.EndUpdate()
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler combo.SelectedIndexChanged,
            Sub()
                If isUpdating Then Return
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing Then Return
                Dim mode As ThemeMode = ThemeModes.Parse(choice.Value)
                If mode <> ThemeService.Mode Then ThemeService.SetMode(mode)
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, "ui_theme", combo, 230, True)
    End Sub

    ''' <summary>
    ''' One drop-down option. <paramref name="text"/> is the Russian source string, i.e.
    ''' the dictionary key - translation happens HERE rather than at each of the two
    ''' dozen call sites, which keeps them readable as plain text.
    '''
    ''' Until this went through the layer the options were not even bilingual: the
    ''' shipped build showed Russian to every user (see Localization.Choices.vb).
    ''' </summary>
    Private Shared Function Choice(value As String, text As String) As PreferenceChoice
        Return New PreferenceChoice(value, Localization.T(text))
    End Function

    Private Sub AddPreferenceCheck(flow As FlowLayoutPanel, key As String,
                                   getter As Func(Of Boolean),
                                   apply As Action(Of Boolean))
        Dim check As New CheckBox With {.AutoSize = True}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    check.Checked = getter()
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler check.CheckedChanged,
            Sub()
                If isUpdating Then Return
                apply(check.Checked)
                ApplyExpandedPreferences()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, key, check, 34, True)
    End Sub

    ''' <summary>A kind toggle needs more than applying preferences: it changes which
    ''' rows exist in the current folder, so refresh through Main_Form's single reload
    ''' path after persisting the new value.</summary>
    Private Sub AddNavigationKindCheck(flow As FlowLayoutPanel, key As String,
                                       getter As Func(Of Boolean),
                                       apply As Action(Of Boolean))
        Dim check As New CheckBox With {.AutoSize = True}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    check.Checked = getter()
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler check.CheckedChanged,
            Sub()
                If isUpdating Then Return
                apply(check.Checked)
                Main_Form.ApplyMediaKindNavigationPreferenceChange()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, key, check, 34, True)
    End Sub

    Private Sub AddPreferenceChoice(flow As FlowLayoutPanel, key As String,
                                    choiceFactory As Func(Of PreferenceChoice()),
                                    getter As Func(Of String),
                                    apply As Action(Of String))
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    Dim current As String = getter()
                    Dim choices As PreferenceChoice() = choiceFactory()
                    combo.BeginUpdate()
                    combo.Items.Clear()
                    combo.Items.AddRange(choices)
                    combo.SelectedItem = choices(SettingsRowRefresh.SelectedToken(choices.Select(Function(item) item.Value), current))
                    combo.EndUpdate()
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler combo.SelectedIndexChanged,
            Sub()
                If isUpdating Then Return
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing Then Return
                apply(choice.Value)
                ApplyExpandedPreferences()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, key, combo, 230, True)
    End Sub

    Private Sub AddPreferenceNumber(flow As FlowLayoutPanel, key As String, minimum As Integer, maximum As Integer,
                                    getter As Func(Of Integer),
                                    apply As Action(Of Integer))
        Dim number As New NumericUpDown With {.Minimum = minimum, .Maximum = maximum}
        Dim isUpdating As Boolean = False
        Dim refresh As Action =
            Sub()
                ' A field the user is typing into keeps its text until it is committed.
                If Not SettingsRowRefresh.ShouldReload(number.Focused, number.Text <> number.Value.ToString(Globalization.CultureInfo.CurrentCulture)) Then Return
                isUpdating = True
                Try
                    Dim val As Integer = getter()
                    number.Value = Math.Max(minimum, Math.Min(maximum, val))
                Finally
                    isUpdating = False
                End Try
            End Sub
        If key = "ocr_cache_limit" Then ocrBudgetRefresh = refresh
        refresh()

        AddHandler number.ValueChanged,
            Sub()
                If isUpdating Then Return
                apply(CInt(number.Value))
                ApplyExpandedPreferences()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, key, number, 100, True)
    End Sub

    ''' <summary>A percentage on a slider with its value beside it. Dragging fires a change per pixel and
    ''' applying a preference re-reads far more than one number, so the new value is applied once the
    ''' drag has paused rather than on every step.</summary>
    Private Sub AddPreferenceSlider(flow As FlowLayoutPanel, key As String, minimum As Integer, maximum As Integer,
                                    getter As Func(Of Integer),
                                    apply As Action(Of Integer))
        Dim host As New Panel With {.Height = Sp(34)}
        ' 127 §3.2: the slider is 180 logical px plus a 64 numeric companion.
        Dim exact As New NumericUpDown With {.Dock = DockStyle.Right, .Width = Sp(SettingsRowGridRules.SliderCompanionWidth), .Minimum = minimum, .Maximum = maximum}
        Dim slider As New TrackBar With {.Dock = DockStyle.Fill, .Minimum = minimum, .Maximum = maximum, .TickFrequency = 10,
                                         .SmallChange = 1, .LargeChange = 10, .AutoSize = False}
        host.Controls.Add(slider)
        host.Controls.Add(exact)
        Dim isUpdating As Boolean = False
        Dim pause As New System.Windows.Forms.Timer With {.Interval = 200}
        AddHandler host.Disposed, Sub() pause.Dispose()
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    slider.Value = Math.Max(minimum, Math.Min(maximum, getter()))
                    exact.Value = slider.Value
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler slider.ValueChanged,
            Sub()
                exact.Value = slider.Value
                If isUpdating Then Return
                apply(slider.Value)
                pause.Stop()
                pause.Start()
            End Sub
        AddHandler exact.ValueChanged,
            Sub()
                If isUpdating Then Return
                slider.Value = CInt(exact.Value)
            End Sub
        AddHandler pause.Tick,
            Sub()
                pause.Stop()
                ApplyExpandedPreferences()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(flow, key, host, 230, True)
    End Sub

    Private Sub ApplyExpandedPreferences()
        Main_Form.ApplyModernPreferencesFromSettings()
    End Sub
End Class
#End If
