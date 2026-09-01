#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Windows.Forms
Imports System.Linq
Imports System.IO

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

    ''' <summary>Kept so the OCR cache row can restate the size after a clear without
    ''' rebuilding the page.</summary>
    Private ocrCacheSizeLabel As Label

    ''' <summary>Same, for the decode cache row (§6.2).</summary>
    Private decodeCacheSizeLabel As Label

    ''' <summary>The "include personal data" opt-in of §7.4. Deliberately a field rather
    ''' than a preference: it is a decision about one export, not a setting.</summary>
    Private exportPersonalData As Boolean

    Private Sub AddExpandedSettingsRows(destinations As FlowLayoutPanel, viewing As FlowLayoutPanel,
                                        video As FlowLayoutPanel, audio As FlowLayoutPanel, files As FlowLayoutPanel,
                                        ocr As FlowLayoutPanel)
        Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()

        AddSectionHeader(destinations, "section_overlay_layout")
        AddPreferenceChoice(destinations, "recipients_position", p.RecipientsOverlayPosition,
                            {Choice("topLeft", "Вверху слева"), Choice("topRight", "Вверху справа"), Choice("bottomLeft", "Внизу слева"), Choice("bottomRight", "Внизу справа")},
                            Sub(v) p.RecipientsOverlayPosition = v)
        AddPreferenceNumber(destinations, "recipients_width", p.RecipientsOverlayWidth, 180, 520, Sub(v) p.RecipientsOverlayWidth = v)
        AddPreferenceNumber(destinations, "recipients_font", p.RecipientsOverlayFontSize, 9, 18, Sub(v) p.RecipientsOverlayFontSize = v)
        AddPreferenceNumber(destinations, "recipients_opacity", p.RecipientsOverlayOpacity, 40, 100, Sub(v) p.RecipientsOverlayOpacity = v)
        AddPreferenceNumber(destinations, "recipients_rows", p.RecipientsOverlayVisibleRows, 3, 11, Sub(v) p.RecipientsOverlayVisibleRows = v)

        AddSectionHeader(viewing, "section_accessibility")
        AddInterfaceScaleRow(viewing, p)
        AddPreferenceChoice(viewing, "new_image_scale", p.NewImageScaleMode,
                            {Choice("fit", "Вписать"), Choice("actual", "100 %"), Choice("perFolder", "Запоминать для папки")},
                            Sub(v) p.NewImageScaleMode = v)
        AddPreferenceCheck(viewing, "reduce_motion", p.ReduceMotion, Sub(v) p.ReduceMotion = v)
        AddSectionHeader(viewing, "section_slideshow_behavior")
        AddPreferenceChoice(viewing, "slideshow_random_order", p.SlideshowRandomOrder,
                            {Choice("natural", "Обычный порядок"), Choice("random", "Случайный"), Choice("shuffleCycle", "Без повторов до конца цикла")},
                            Sub(v) p.SlideshowRandomOrder = v)
        AddPreferenceCheck(viewing, "stop_slideshow_manual", p.StopSlideshowOnManualNavigation, Sub(v) p.StopSlideshowOnManualNavigation = v)
        AddPreferenceChoice(viewing, "slideshow_ui", p.SlideshowUiMode,
                            {Choice("none", "Не скрывать"), Choice("toolbar", "Скрывать панель инструментов"), Choice("toolbarAndStatus", "Скрывать панель и статус")},
                            Sub(v) p.SlideshowUiMode = v)

        AddSectionHeader(video, "section_video_behavior")
        AddNavigationKindCheck(video, "include_video_navigation", p.IncludeVideoInNavigation,
                               Sub(v) p.IncludeVideoInNavigation = v)
        AddPreferenceCheck(video, "video_autoplay", p.VideoAutoplay, Sub(v) p.VideoAutoplay = v)
        AddPreferenceCheck(video, "video_remember_position", p.RememberVideoPosition, Sub(v) p.RememberVideoPosition = v)
        AddPreferenceNumber(video, "video_controls_delay", p.VideoControlsHideDelaySec, 1, 15, Sub(v) p.VideoControlsHideDelaySec = v)
        AddPreferenceCheck(video, "video_controls_paused", p.ShowVideoControlsWhenPaused, Sub(v) p.ShowVideoControlsWhenPaused = v)
        AddPreferenceChoice(video, "video_click_action", p.VideoSingleClickAction,
                            {Choice("pauseResume", "Пауза / продолжить"), Choice("nextFile", "Следующий файл")}, Sub(v) p.VideoSingleClickAction = v)
        AddPreferenceChoice(video, "video_end_action", p.VideoEndAction,
                            {Choice("stay", "Оставить последний кадр"), Choice("nextFile", "Следующий файл"), Choice("repeat", "Повторить")}, Sub(v) p.VideoEndAction = v)
        AddSectionHeader(audio, "section_audio_behavior")
        AddNavigationKindCheck(audio, "include_audio_navigation", p.IncludeAudioInNavigation,
                               Sub(v) p.IncludeAudioInNavigation = v)
        AddPreferenceChoice(audio, "audio_end_action", p.AudioEndAction,
                            {Choice("next", "Следующий файл"), Choice("stay", Localization.T("Остановиться")), Choice("repeat", "Повторить")}, Sub(v) p.AudioEndAction = v)
        AddPreferenceCheck(audio, "audio_controls_visible", p.AudioControlsAlwaysVisible, Sub(v) p.AudioControlsAlwaysVisible = v)
        AddPreferenceCheck(audio, "audio_visualiser", p.AudioVisualiser, Sub(v) p.AudioVisualiser = v)
        AddPreferenceNumber(audio, "audio_sleep_timer", p.SleepTimerMinutes, 0, 180, Sub(v) p.SleepTimerMinutes = v)
        AddPreferenceChoice(audio, "preferred_audio_language", p.PreferredAudioLanguage,
                            TrackLanguageChoices(subtitles:=False), Sub(v) p.PreferredAudioLanguage = v)
        AddPreferenceChoice(audio, "preferred_subtitle_language", p.PreferredSubtitleLanguage,
                            TrackLanguageChoices(subtitles:=True), Sub(v) p.PreferredSubtitleLanguage = v)

        AddSectionHeader(files, "section_file_behavior")
        AddPreferenceChoice(files, "name_collision", p.NameCollisionPolicy,
                            {Choice("ask", "Спрашивать"), Choice("skip", "Пропустить"), Choice("rename", "Сохранить оба"), Choice("replace", "Заменить")}, Sub(v) p.NameCollisionPolicy = v)
        AddPreferenceChoice(files, "after_file_operation", p.AfterFileOperation,
                            {Choice("next", "Следующий файл"), Choice("stay", "Остаться на текущем"), Choice("closeIfEmpty", "Закрыть, если файлов не осталось")}, Sub(v) p.AfterFileOperation = v)
        ' The two deletion rows sit with the transfer policy they belong with, and in this
        ' order: whether the bin is used at all decides what the confirmation can even say.
        ' With the transfer policy, because it is one: it says what a recipient slot does
        ' when its folder is not there yet (SLOT_HEALTH §3.6). Off means "not found" and a
        ' refusal, which is the behaviour that existed before it.
        AddPreferenceCheck(files, "create_missing_destination", p.CreateMissingDestination,
                           Sub(v) p.CreateMissingDestination = v)
        AddPreferenceCheck(files, "delete_to_recycle_bin", p.DeleteToRecycleBin, Sub(v) p.DeleteToRecycleBin = v)
        AddPreferenceChoice(files, "confirm_delete", p.ConfirmDelete,
                            {Choice("always", "Всегда"), Choice("permanentOnly", "Только если файл не попадёт в Корзину"), Choice("never", "Никогда")}, Sub(v) p.ConfirmDelete = v)
        ' With the two deletion rows, because it guards a deletion: "Replace with video"
        ' removes the original permanently. The confirmation's own checkbox is what turns
        ' this off; this row is the only way back on
        ' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §10.1).
        AddPreferenceCheck(files, "to_video_confirm", p.ConfirmReplaceWithVideo, Sub(v) p.ConfirmReplaceWithVideo = v)
        AddPreferenceCheck(files, "include_subfolders", p.IncludeSubfolders, Sub(v) p.IncludeSubfolders = v)
        AddFileTypesRow(files, p)
        AddHotkeysRow(files, p)
        AddPreferenceNumber(files, "recent_files_limit", p.RecentFilesLimit, 0, 200, Sub(v) p.RecentFilesLimit = v)
        AddPreferenceNumber(files, "recent_folders_limit", p.RecentFoldersLimit, 0, 200, Sub(v) p.RecentFoldersLimit = v)
        AddHistoryRow(files)
        AddPreferenceChoice(files, "startup_open", p.StartupOpenMode,
                            {Choice("home", "Стартовую страницу"), Choice("lastFolder", "Последнюю папку"), Choice("lastFile", "Последний файл")}, Sub(v) p.StartupOpenMode = v)
        ' Directly under it, because the two are a pair: that row says WHAT to open, this
        ' one says "and carry on from the same frame" (RESUME_LAST_PLAYBACK §6).
        AddPreferenceCheck(files, "resume_last_playback", p.ResumeLastPlayback, Sub(v) p.ResumeLastPlayback = v)
        AddSettingsTransferRows(files)
        AddAllowNewWindowsRow(files, p)

        ' ZIP/CBZ browsing (010_SPECIFICATION_ARCHIVE_BROWSING_DOTNET10.md §9, §12 Ф4).
        AddSectionHeader(files, "section_archives")
        AddPreferenceNumber(files, "archive_cache_limit", p.ArchiveCacheMaxMb, 100, 16384, Sub(v) p.ArchiveCacheMaxMb = v)
        AddPreferenceNumber(files, "archive_entry_limit", p.ArchiveMaxEntryMb, 16, 4096, Sub(v) p.ArchiveMaxEntryMb = v)
        AddPreferenceNumber(files, "archive_entries_limit", p.ArchiveMaxEntries, 100, 100000, Sub(v) p.ArchiveMaxEntries = v)

        AddPreferenceNumber(ocr, "ocr_cache_limit", p.OcrDiskCacheMaxMb, 0, 1024, Sub(v) p.OcrDiskCacheMaxMb = v)
        AddOcrCacheRow(ocr)
        ' The decode cache sits beside the OCR one: they are the same kind of setting - a
        ' megabyte budget for work already paid for - and the second answer to "what does
        ' this app keep on disk" belongs next to the first, not on a page of its own
        ' (SPECIFICATION_DECODE_CACHE_AND_ANIMATION_TO_VIDEO_DOTNET10.md §6.2).
        AddPreferenceNumber(ocr, "decode_cache_limit", p.DecodeCacheMaxMb, 0, 8192, Sub(v) p.DecodeCacheMaxMb = v)
        AddDecodeCacheRow(ocr)
    End Sub

    ''' <summary>
    ''' §4.1. The scale changes the base font and the metrics of this window, and those
    ''' are read once while it is being built - so the honest way to apply it is to build
    ''' the window again, which is exactly what the specification asks to confirm first.
    ''' </summary>
    Private Sub AddInterfaceScaleRow(viewing As FlowLayoutPanel, p As ModernViewerPreferences)
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Dim choices As PreferenceChoice() = {Choice("0", "Системный"), Choice("90", "90 %"), Choice("100", "100 %"),
                                             Choice("110", "110 %"), Choice("125", "125 %"), Choice("150", "150 %")}
        combo.Items.AddRange(choices)
        Dim currentValue As String = p.InterfaceScalePercent.ToString(Globalization.CultureInfo.InvariantCulture)
        combo.SelectedItem = If(choices.FirstOrDefault(Function(item) item.Value = currentValue), choices(0))

        AddHandler combo.SelectedIndexChanged,
            Sub()
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing Then Return
                Dim percent As Integer = Integer.Parse(choice.Value, Globalization.CultureInfo.InvariantCulture)
                If percent = p.InterfaceScalePercent Then Return

                p.InterfaceScalePercent = percent
                ApplyExpandedPreferences()
                If MessageBox.Show(Me, Localization.T("Новый масштаб применится после перезапуска окна настроек. Перезапустить сейчас?"),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                Main_Form.RequestSettingsWindowRestart()
                Close()
            End Sub
        AddSettingRow(viewing, "interface_scale", combo, 230, True)
    End Sub

    ''' <summary>§3.4. The row is a button, not a list of extensions: the answer is
    ''' thirty-odd formats in four groups, and a semicolon-separated field could not be
    ''' checked by eye.</summary>
    Private Sub AddFileTypesRow(files As FlowLayoutPanel, p As ModernViewerPreferences)
        Dim button As New Button With {.AutoSize = True, .Text = Localization.T("Настроить..")}
        AddHandler button.Click,
            Sub()
                Using dialog As New File_Types_Form(Main_Form.SupportedExtensionGroups(), CurrentExtensionFilter(p))
                    PinDialogToViewerBand(dialog)
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    p.SetIncludedExtensionsFromEditing(String.Join(";", dialog.Selection))
                    ApplyExpandedPreferences()
                End Using
            End Sub
        AddSettingRow(files, "included_extensions", button, 230, True)
    End Sub

    ''' <summary>A dialog owned by this window still has to be put into the viewer's
    ''' z-order band by hand - being owned is not enough to leave the normal band, so
    ''' without this it opens BEHIND a viewer pinned «поверх всех окон»
    ''' (Main_Form.WindowPinning.vb; same line as Table_Form.SendLogs.vb).</summary>
    Private Sub PinDialogToViewerBand(dialog As Form)
        dialog.TopMost = Me.TopMost OrElse MainWindowIsTopMost()
    End Sub

    Private Shared Function CurrentExtensionFilter(p As ModernViewerPreferences) As List(Of String)
        Return p.IncludedExtensionsForEditing().
            Split(";"c).
            Select(Function(item) item.Trim()).
            Where(Function(item) item.Length > 0).
            ToList()
    End Function

    ''' <summary>§3.5. Also its own dialog, and for the same reason.</summary>
    Private Sub AddHotkeysRow(files As FlowLayoutPanel, p As ModernViewerPreferences)
        Dim button As New Button With {.AutoSize = True, .Text = Localization.T("Настроить..")}
        AddHandler button.Click,
            Sub()
                Using dialog As New Hotkeys_Form(CustomHotkeys.Load(p.CustomHotkeysJson))
                    PinDialogToViewerBand(dialog)
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    p.CustomHotkeysJson = CustomHotkeys.Save(dialog.Result)
                    ApplyExpandedPreferences()
                    Main_Form.ReloadCustomHotkeys()
                End Using
            End Sub
        AddSettingRow(files, "custom_hotkeys", button, 230, True)
    End Sub

    ''' <summary>§7.2. Opening an entry closes this window: the user asked to look at a
    ''' picture, not to come back to a settings page.</summary>
    Private Sub AddHistoryRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True, .Text = Localization.T("Открыть историю")}
        AddHandler button.Click,
            Sub()
                Dim chosen As String = Nothing
                Using dialog As New History_Form(Main_Form.RecentFilesSnapshot(), Main_Form.RecentFoldersSnapshot())
                    AddHandler dialog.EntryChosen, Sub(entry) chosen = entry
                    PinDialogToViewerBand(dialog)
                    dialog.ShowDialog(Me)
                    Main_Form.ReplaceRecentHistory(dialog.Files_Result, dialog.Folders_Result)
                End Using
                If String.IsNullOrEmpty(chosen) Then Return
                Close()
                Main_Form.OpenHistoryEntry(chosen)
            End Sub
        AddSettingRow(files, "recent_history", button, 230, True)
    End Sub

    ''' <summary>§7.3. The number above sets the budget; this row says what the cache
    ''' costs right now and offers the one action that is not automatic.</summary>
    Private Sub AddOcrCacheRow(ocr As FlowLayoutPanel)
        Dim host As New FlowLayoutPanel With {.Width = 300, .Height = 30, .WrapContents = False,
                                              .FlowDirection = FlowDirection.LeftToRight,
                                              .Margin = Padding.Empty, .Padding = Padding.Empty}
        ocrCacheSizeLabel = New Label With {.AutoSize = False, .Width = 100, .Height = 26,
                                            .TextAlign = Drawing.ContentAlignment.MiddleLeft}
        Dim clearButton As New Button With {.AutoSize = True, .Text = Localization.T("Очистить кэш")}
        AddHandler clearButton.Click,
            Sub()
                If MessageBox.Show(Me, Localization.T("Удалить сохранённые результаты распознавания? Настройки OCR не изменятся."),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                Main_Form.ClearOcrResultCache()
                RefreshOcrCacheSize()
            End Sub
        host.Controls.Add(ocrCacheSizeLabel)
        host.Controls.Add(clearButton)
        AddSettingRow(ocr, "ocr_cache_clear", host, 300, True)
        RefreshOcrCacheSize()
    End Sub

    Private Sub RefreshOcrCacheSize()
        If ocrCacheSizeLabel Is Nothing Then Return
        ocrCacheSizeLabel.Text = Localization.TF("{0} МБ", MegabytesOnDisk(OcrPaths.OcrCacheDir(), "*.json"))
    End Sub

    ''' <summary>The decode cache's own size-and-clear row, built exactly like the OCR one
    ''' above (§6.2). Its size comes from DecodeCacheStore rather than from a second walk
    ''' of the directory: the store owns the file pattern, and a copy of it here is a copy
    ''' that would be wrong the day the extension changes.</summary>
    Private Sub AddDecodeCacheRow(ocr As FlowLayoutPanel)
        Dim host As New FlowLayoutPanel With {.Width = 300, .Height = 30, .WrapContents = False,
                                              .FlowDirection = FlowDirection.LeftToRight,
                                              .Margin = Padding.Empty, .Padding = Padding.Empty}
        decodeCacheSizeLabel = New Label With {.AutoSize = False, .Width = 100, .Height = 26,
                                               .TextAlign = Drawing.ContentAlignment.MiddleLeft}
        Dim clearButton As New Button With {.AutoSize = True, .Text = Localization.T("Очистить кэш")}
        AddHandler clearButton.Click,
            Sub()
                If MessageBox.Show(Me, Localization.T("Удалить сохранённые результаты декодирования? Сами изображения не изменятся."),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                DecodeCacheStore.Clear(DecodeCacheStore.CacheDir())
                RefreshDecodeCacheSize()
            End Sub
        host.Controls.Add(decodeCacheSizeLabel)
        host.Controls.Add(clearButton)
        AddSettingRow(ocr, "decode_cache_clear", host, 300, True)
        RefreshDecodeCacheSize()
    End Sub

    Private Sub RefreshDecodeCacheSize()
        If decodeCacheSizeLabel Is Nothing Then Return
        decodeCacheSizeLabel.Text = Localization.TF("{0} МБ", Megabytes(DecodeCacheStore.BytesOnDisk(DecodeCacheStore.CacheDir())))
    End Sub

    ''' <summary>One decimal, and never an exception: a cache directory that cannot be
    ''' measured is a disk problem, not a reason to fail opening the settings.</summary>
    Private Shared Function MegabytesOnDisk(cacheDir As String, filePattern As String) As String
        Dim bytes As Long = 0
        Try
            If Not String.IsNullOrEmpty(cacheDir) AndAlso Directory.Exists(cacheDir) Then
                For Each entry As String In Directory.GetFiles(cacheDir, filePattern)
                    Try
                        bytes += New FileInfo(entry).Length
                    Catch
                    End Try
                Next
            End If
        Catch
        End Try
        Return Megabytes(bytes)
    End Function

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
        AddSettingRow(files, "settings_export_personal", personalCheck, 34, True)
        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(personalCheck, Localization.T("Включает в файл историю папок и позиции просмотра видео. API-ключи и пароли не экспортируются никогда."))
        End If

        Dim exportButton As New Button With {.AutoSize = True, .Text = Localization.T("Экспортировать настройки")}
        AddHandler exportButton.Click,
            Sub()
                If exportPersonalData AndAlso
                   MessageBox.Show(Me, Localization.T("В файл попадут пути к вашим папкам и позиции просмотра. Продолжить?"),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return

                Using dialog As New SaveFileDialog With {.Filter = "Fast Media Sorter settings|*.fms-settings.json", .DefaultExt = "fms-settings.json", .AddExtension = True}
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    Try
                        File.WriteAllText(dialog.FileName, Main_Form.GetModernPreferences().ExportJson(exportPersonalData))
                        MessageBox.Show(Me, Localization.T("Настройки экспортированы."), Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        MessageBox.Show(Me, Localization.TF("Не удалось экспортировать настройки: {0}", ex.Message), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub
        AddSettingRow(files, "settings_export", exportButton, 210, True)

        Dim importButton As New Button With {.AutoSize = True, .Text = Localization.T("Импортировать настройки")}
        AddHandler importButton.Click, AddressOf ImportSettings
        AddSettingRow(files, "settings_import", importButton, 210, True)
    End Sub

    ''' <summary>
    ''' §7.4. Validate, summarize, ask Replace or Merge, back the current profile up, then
    ''' write. Nothing before the last step touches the profile, so a document that turns
    ''' out to be unreadable - or an import the user backs out of - leaves it untouched.
    ''' </summary>
    Private Sub ImportSettings(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {.Filter = "Fast Media Sorter settings|*.fms-settings.json"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim imported As ModernViewerPreferences
            Try
                imported = ModernViewerPreferences.FromJson(File.ReadAllText(dialog.FileName))
            Catch ex As Exception
                MessageBox.Show(Me, Localization.TF("Не удалось импортировать настройки: {0}", ex.Message),
                                Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim current As ModernViewerPreferences = Main_Form.GetModernPreferences()
            Dim changes As List(Of String) = DescribeDifferences(current, imported)
            Dim summary As String = If(changes.Count = 0,
                Localization.T("Файл прочитан. Отличий от текущих настроек нет."),
                Localization.TF("Файл прочитан. Изменится параметров: {0}.", changes.Count) & vbCrLf & vbCrLf &
                String.Join(vbCrLf, changes.Take(12)))

            ' Yes = replace everything the document describes, No = merge only what differs
            ' from the shipped defaults, so a partial file cannot silently reset the rest.
            Dim answer As DialogResult = MessageBox.Show(Me,
                summary & vbCrLf & vbCrLf & Localization.T("Да - заменить настройки, Нет - объединить с текущими."),
                Localization.T("Настройки"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            If answer = DialogResult.Cancel Then Return

            Dim backup As String = dialog.FileName & ".backup"
            Try
                File.WriteAllText(backup, current.ExportJson(includePersonalData:=True))
            Catch ex As Exception
                MessageBox.Show(Me, Localization.TF("Не удалось создать резервную копию: {0}", ex.Message),
                                Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            If answer = DialogResult.No Then imported = MergeIntoCurrent(current, imported)
            Main_Form.ReplaceModernPreferences(imported)
            Main_Form.ReloadCustomHotkeys()
            MessageBox.Show(Me, Localization.TF("Настройки импортированы. Резервная копия: {0}", backup) & vbCrLf &
                                Localization.T("Масштаб интерфейса и режим запуска применятся после перезапуска."),
                            Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Using
    End Sub

    ''' <summary>
    ''' Merge: the document wins only where it says something other than the factory
    ''' default. That is what makes "объединить" mean "do not wipe what I did not send" -
    ''' a file exported from a fresh profile would otherwise reset every local choice to
    ''' the defaults it happens to carry.
    ''' </summary>
    Private Shared Function MergeIntoCurrent(current As ModernViewerPreferences, imported As ModernViewerPreferences) As ModernViewerPreferences
        Dim defaults As New ModernViewerPreferences()
        Dim merged As ModernViewerPreferences = Clone(current)
        For Each prop As Reflection.PropertyInfo In WritableProperties()
            Dim fromFile As Object = prop.GetValue(imported)
            If Equals(fromFile, prop.GetValue(defaults)) Then Continue For
            prop.SetValue(merged, fromFile)
        Next
        merged.Normalize()
        Return merged
    End Function

    Private Shared Function Clone(source As ModernViewerPreferences) As ModernViewerPreferences
        Dim copy As New ModernViewerPreferences()
        For Each prop As Reflection.PropertyInfo In WritableProperties()
            prop.SetValue(copy, prop.GetValue(source))
        Next
        Return copy
    End Function

    ''' <summary>The scalar preferences, which is every property the profile stores. The
    ''' nested entry classes are not properties of this type, so nothing else gets in.</summary>
    Private Shared Function WritableProperties() As IEnumerable(Of Reflection.PropertyInfo)
        Return GetType(ModernViewerPreferences).
            GetProperties(Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance).
            Where(Function(prop) prop.CanRead AndAlso prop.CanWrite)
    End Function

    Private Shared Function DescribeDifferences(current As ModernViewerPreferences, imported As ModernViewerPreferences) As List(Of String)
        Dim changes As New List(Of String)()
        For Each prop As Reflection.PropertyInfo In WritableProperties()
            Dim before As Object = prop.GetValue(current)
            Dim after As Object = prop.GetValue(imported)
            If Equals(before, after) Then Continue For
            changes.Add(prop.Name & ": " & Convert.ToString(before) & " -> " & Convert.ToString(after))
        Next
        Return changes
    End Function

    ''' <summary>This value is deliberately persisted right away, including from a
    ''' secondary process, so the next Explorer launch follows the new choice.</summary>
    Private Sub AddAllowNewWindowsRow(files As FlowLayoutPanel, p As ModernViewerPreferences)
        Dim check As New CheckBox With {.Checked = p.AllowNewWindows, .AutoSize = True}
        AddHandler check.CheckedChanged,
            Sub()
                p.AllowNewWindows = check.Checked
                Microsoft.VisualBasic.Interaction.SaveSetting(App_name, Second_App_Name, "AllowNewWindows", If(check.Checked, "1", "0"))
            End Sub
        AddSettingRow(files, "allow_new_windows", check, 34, True)
        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(check, Localization.T("Действует со следующего запуска. Настройки и позицию окна хранит первое окно. Помните: выделив в проводнике десять файлов, вы получите десять окон. По умолчанию выключено."))
        End If
    End Sub

    ''' <summary>
    ''' The checkbox that replaced the global "copy mode" on the Files page: it does not
    ''' decide WHAT a recipient key does (that is now said at the moment of the action),
    ''' only what the view does after a copy. Built here, in code, because it is a
    ''' mainline-only setting - the Designer's control set is shared with net48, which
    ''' keeps its old copy-mode checkbox instead (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.1).
    ''' </summary>
    Private Sub AddAdvanceAfterCopyRow(files As FlowLayoutPanel)
        Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
        Dim check As New CheckBox With {.Checked = p.AdvanceAfterCopy, .AutoSize = True}
        AddHandler check.CheckedChanged,
            Sub()
                p.AdvanceAfterCopy = check.Checked
                ApplyExpandedPreferences()
            End Sub
        AddSettingRow(files, "advance_after_copy", check, 34, True)
        If toolTip IsNot Nothing Then
            toolTip.SetToolTip(check, Localization.T("Если отмечено, после копирования программа сразу показывает следующий файл. Если снять галочку, после копирования остаётся текущий файл. По умолчанию включено."))
        End If
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

    Private Sub AddPreferenceCheck(flow As FlowLayoutPanel, key As String, value As Boolean, apply As Action(Of Boolean))
        Dim check As New CheckBox With {.Checked = value, .AutoSize = True}
        AddHandler check.CheckedChanged,
            Sub()
                apply(check.Checked)
                ApplyExpandedPreferences()
            End Sub
        AddSettingRow(flow, key, check, 34, True)
    End Sub

    ''' <summary>A kind toggle needs more than applying preferences: it changes which
    ''' rows exist in the current folder, so refresh through Main_Form's single reload
    ''' path after persisting the new value.</summary>
    Private Sub AddNavigationKindCheck(flow As FlowLayoutPanel, key As String, value As Boolean, apply As Action(Of Boolean))
        Dim check As New CheckBox With {.Checked = value, .AutoSize = True}
        AddHandler check.CheckedChanged,
            Sub()
                apply(check.Checked)
                Main_Form.ApplyMediaKindNavigationPreferenceChange()
            End Sub
        AddSettingRow(flow, key, check, 34, True)
    End Sub

    Private Sub AddPreferenceChoice(flow As FlowLayoutPanel, key As String, value As String,
                                    choices As PreferenceChoice(), apply As Action(Of String))
        Dim combo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        combo.Items.AddRange(choices)
        Dim selected As PreferenceChoice = choices.FirstOrDefault(Function(item) item.Value = value)
        combo.SelectedItem = If(selected, choices(0))
        AddHandler combo.SelectedIndexChanged,
            Sub()
                Dim choice As PreferenceChoice = TryCast(combo.SelectedItem, PreferenceChoice)
                If choice Is Nothing Then Return
                apply(choice.Value)
                ApplyExpandedPreferences()
            End Sub
        AddSettingRow(flow, key, combo, 230, True)
    End Sub

    Private Sub AddPreferenceNumber(flow As FlowLayoutPanel, key As String, value As Integer, minimum As Integer, maximum As Integer, apply As Action(Of Integer))
        Dim number As New NumericUpDown With {.Minimum = minimum, .Maximum = maximum, .Value = Math.Max(minimum, Math.Min(maximum, value))}
        AddHandler number.ValueChanged,
            Sub()
                apply(CInt(number.Value))
                ApplyExpandedPreferences()
            End Sub
        AddSettingRow(flow, key, number, 100, True)
    End Sub

    Private Sub ApplyExpandedPreferences()
        Main_Form.ApplyModernPreferencesFromSettings()
    End Sub
End Class
#End If
