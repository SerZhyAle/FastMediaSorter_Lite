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

    ''' <summary>Kept so the OCR cache row can restate the size after a clear without
    ''' rebuilding the page.</summary>
    Private ocrCacheSizeLabel As Label

    ''' <summary>Same, for the decode cache row (§6.2).</summary>
    Private decodeCacheSizeLabel As Label

    ''' <summary>Only the newest cache measurement may update its label (057.S14-1).</summary>
    Private ReadOnly ocrSizeGate As New RefreshGate()
    Private ReadOnly decodeSizeGate As New RefreshGate()

    ''' <summary>The "include personal data" opt-in of §7.4. Deliberately a field rather
    ''' than a preference: it is a decision about one export, not a setting.</summary>
    Private exportPersonalData As Boolean

    ''' <summary>Actions that refresh choice combo items, button captions, external preference values,
    ''' and cache measurements on language change and display preparation (057.S14-1).</summary>
    Private ReadOnly expandedRefreshActions As New List(Of Action)()

    Friend Sub RefreshExpandedSettingsRows()
        For Each refreshAction As Action In expandedRefreshActions
            refreshAction()
        Next
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
                            Function() {Choice("small", "Маленький"), Choice("medium", "Средний"), Choice("large", "Большой"), Choice("adaptive", "Адаптивный")},
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

        AddPreferenceNumber(ocr, "ocr_cache_limit", 0, 1024,
                            Function() Main_Form.GetModernPreferences().OcrDiskCacheMaxMb,
                            Sub(v) Main_Form.GetModernPreferences().OcrDiskCacheMaxMb = v)
        AddOcrCacheRow(ocr)
        AddPreferenceNumber(ocr, "decode_cache_limit", 0, 8192,
                            Function() Main_Form.GetModernPreferences().DecodeCacheMaxMb,
                            Sub(v) Main_Form.GetModernPreferences().DecodeCacheMaxMb = v)
        AddDecodeCacheRow(ocr)
        AddPreferenceNumber(ocr, "thumb_cache_limit", 0, 4096,
                            Function() Main_Form.GetModernPreferences().PanelThumbCacheMb,
                            Sub(v) Main_Form.GetModernPreferences().PanelThumbCacheMb = v)
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
                    Dim currentValue As String = Main_Form.GetModernPreferences().InterfaceScalePercent.ToString(Globalization.CultureInfo.InvariantCulture)
                    combo.SelectedItem = If(choices.FirstOrDefault(Function(item) item.Value = currentValue), choices(0))
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
                Dim percent As Integer = Integer.Parse(choice.Value, Globalization.CultureInfo.InvariantCulture)
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                If percent = p.InterfaceScalePercent Then Return

                p.InterfaceScalePercent = percent
                ApplyExpandedPreferences()
                If MessageBox.Show(Me, Localization.T("Новый масштаб применится после перезапуска окна настроек. Перезапустить сейчас?"),
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
                    PinDialogToViewerBand(dialog)
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
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
                    Dim input As New TextBox With {.Text = String.Join(";", values.OrderBy(Function(ext) ext)), .Location = New Point(16, 24), .Width = 428}
                    Dim ok As New Button With {.Text = Localization.T("ОК"), .DialogResult = DialogResult.OK, .Location = New Point(260, 88)}
                    Dim cancel As New Button With {.Text = Localization.T("Отмена"), .DialogResult = DialogResult.Cancel, .Location = New Point(350, 88)}
                    dialog.Controls.AddRange(New Control() {input, ok, cancel})
                    dialog.AcceptButton = ok
                    dialog.CancelButton = cancel
                    PinDialogToViewerBand(dialog)
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
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
    Private Sub AddHotkeysRow(files As FlowLayoutPanel)
        Dim button As New Button With {.AutoSize = True}
        Dim refresh As Action = Sub() button.Text = Localization.T("Настроить..")
        refresh()
        AddHandler button.Click,
            Sub()
                Dim p As ModernViewerPreferences = Main_Form.GetModernPreferences()
                Using dialog As New Hotkeys_Form(CustomHotkeys.Load(p.CustomHotkeysJson))
                    PinDialogToViewerBand(dialog)
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
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
                    PinDialogToViewerBand(dialog)
                    dialog.ShowDialog(Me)
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
                    MessageBox.Show(Me, Localization.T("Не удалось изменить запись в реестре"),
                                    Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                ' Whatever the registry actually says now, not what was asked for.
                refresh()
            End Sub
        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "secure_open_with", button, 230, True)
    End Sub

    ''' <summary>§7.3. The number above sets the budget; this row says what the cache
    ''' costs right now and offers the one action that is not automatic.</summary>
    Private Sub AddOcrCacheRow(ocr As FlowLayoutPanel)
        Dim host As New FlowLayoutPanel With {.Width = 300, .Height = 30, .WrapContents = False,
                                              .FlowDirection = FlowDirection.LeftToRight,
                                              .Margin = Padding.Empty, .Padding = Padding.Empty}
        ocrCacheSizeLabel = New Label With {.AutoSize = False, .Width = 100, .Height = 26,
                                            .TextAlign = Drawing.ContentAlignment.MiddleLeft}
        Dim clearButton As New Button With {.AutoSize = True}
        Dim refresh As Action =
            Sub()
                clearButton.Text = Localization.T("Очистить кэш")
                RefreshOcrCacheSize()
            End Sub
        refresh()
        AddHandler clearButton.Click,
            Sub()
                If MessageBox.Show(Me, Localization.T("Удалить сохранённые результаты распознавания? Настройки OCR не изменятся."),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
                Main_Form.ClearOcrResultCache()
                RefreshOcrCacheSize()
            End Sub
        host.Controls.Add(ocrCacheSizeLabel)
        host.Controls.Add(clearButton)
        expandedRefreshActions.Add(refresh)
        AddSettingRow(ocr, "ocr_cache_clear", host, 300, True)
    End Sub

    Private Async Sub RefreshOcrCacheSize()
        If ocrCacheSizeLabel Is Nothing Then Return
        Dim cacheDir As String = OcrPaths.OcrCacheDir()
        Dim ticket As Integer = ocrSizeGate.Begin()
        Try
            Dim size As String = Await Task.Run(Function() MegabytesOnDisk(cacheDir, "*.json"))
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated OrElse ocrCacheSizeLabel Is Nothing Then Return
            If Not ocrSizeGate.IsLatest(ticket) Then Return
            ocrCacheSizeLabel.Text = Localization.TF("{0} МБ", size)
        Catch ex As Exception
            AppFileLogger.LogException("Settings: measuring OCR cache", ex)
        End Try
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
        Dim clearButton As New Button With {.AutoSize = True}
        Dim refresh As Action =
            Sub()
                clearButton.Text = Localization.T("Очистить кэш")
                RefreshDecodeCacheSize()
            End Sub
        refresh()
        AddHandler clearButton.Click,
            Sub()
                If MessageBox.Show(Me, Localization.T("Удалить сохранённые результаты декодирования? Сами изображения не изменятся."),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
                DecodeCacheStore.Clear(DecodeCacheStore.CacheDir())
                RefreshDecodeCacheSize()
            End Sub
        host.Controls.Add(decodeCacheSizeLabel)
        host.Controls.Add(clearButton)
        expandedRefreshActions.Add(refresh)
        AddSettingRow(ocr, "decode_cache_clear", host, 300, True)
    End Sub

    Private Async Sub RefreshDecodeCacheSize()
        If decodeCacheSizeLabel Is Nothing Then Return
        Dim cacheDir As String = DecodeCacheStore.CacheDir()
        Dim ticket As Integer = decodeSizeGate.Begin()
        Try
            Dim size As Long = Await Task.Run(Function() DecodeCacheStore.BytesOnDisk(cacheDir))
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated OrElse decodeCacheSizeLabel Is Nothing Then Return
            If Not decodeSizeGate.IsLatest(ticket) Then Return
            decodeCacheSizeLabel.Text = Localization.TF("{0} МБ", Megabytes(size))
        Catch ex As Exception
            AppFileLogger.LogException("Settings: measuring decode cache", ex)
        End Try
    End Sub

    Private Sub InvalidateCacheMeasurements(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ocrSizeGate.Begin()
        decodeSizeGate.Begin()
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

        Dim exportButton As New Button With {.AutoSize = True}
        Dim importButton As New Button With {.AutoSize = True}

        Dim refresh As Action =
            Sub()
                If toolTip IsNot Nothing Then
                    toolTip.SetToolTip(personalCheck, Localization.T("Включает в файл историю папок и позиции просмотра видео. API-ключи и пароли не экспортируются никогда."))
                End If
                exportButton.Text = Localization.T("Экспортировать настройки")
                importButton.Text = Localization.T("Импортировать настройки")
            End Sub
        refresh()

        AddHandler exportButton.Click,
            Sub()
                If exportPersonalData AndAlso
                   MessageBox.Show(Me, Localization.T("В файл попадут пути к вашим папкам и позиции просмотра. Продолжить?"),
                                   Localization.T("Настройки"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return

                Using dialog As New SaveFileDialog With {.Filter = ProductNames.Compact & " settings|*.fms-settings.json", .DefaultExt = "fms-settings.json", .AddExtension = True}
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    Try
                        File.WriteAllText(dialog.FileName, Main_Form.GetModernPreferences().ExportJson(exportPersonalData))
                        MessageBox.Show(Me, Localization.T("Настройки экспортированы."), Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        AppFileLogger.LogException("Settings export", ex)
                        MessageBox.Show(Me, Localization.TF("Не удалось экспортировать настройки: {0}", FailureText.Describe(ex)), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub

        AddHandler importButton.Click, AddressOf ImportSettings

        expandedRefreshActions.Add(refresh)
        AddSettingRow(files, "settings_export_personal", personalCheck, 34, True)
        AddSettingRow(files, "settings_export", exportButton, 210, True)
        AddSettingRow(files, "settings_import", importButton, 210, True)
    End Sub

    ''' <summary>
    ''' §7.4 / 103 / 105 L12. The order is the contract: read and validate (FromJson already
    ''' normalizes) -> work out what Replace AND Merge would really change -> ask -> take the
    ''' app-private backup (no backup, no import) -> apply into the live object -> persist ->
    ''' rebuild the window. Cancel, a refused backup or an unreadable file leave the model, the
    ''' registry and the hotkeys exactly as they were.
    ''' </summary>
    Private Sub ImportSettings(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog With {.Filter = ProductNames.Compact & " settings|*.fms-settings.json"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

            Dim imported As ModernViewerPreferences
            Try
                imported = ModernViewerPreferences.FromJson(File.ReadAllText(dialog.FileName))
            Catch ex As Exception
                AppFileLogger.LogException("Settings import", ex)
                MessageBox.Show(Me, Localization.TF("Не удалось импортировать настройки: {0}", FailureText.Describe(ex)),
                                Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim current As ModernViewerPreferences = Main_Form.GetModernPreferences()
            Dim plan As SettingsImportPlan.Plan = SettingsImportPlan.Build(current, imported)
            If plan.ReplaceChanges.Count = 0 Then
                ' Nothing to import is reported, not confirmed (APP-BEHAVIOUR rule 5, ticket
                ' 028 Ф-5): no question, no backup, no write.
                MessageBox.Show(Me, Localization.T("Файл прочитан. Отличий от текущих настроек нет."),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim summary As String =
                Localization.TF("Файл прочитан. Изменится параметров: {0}.", plan.ReplaceChanges.Count) & vbCrLf &
                Localization.TF("При объединении изменится параметров: {0}.", plan.MergeChanges.Count) & vbCrLf & vbCrLf &
                String.Join(vbCrLf, plan.ReplaceChanges.Take(12))

            ' Yes = replace everything the document describes, No = merge only what differs
            ' from the shipped defaults, so a partial file cannot silently reset the rest.
            Dim answer As DialogResult = MessageBox.Show(Me,
                summary & vbCrLf & vbCrLf & Localization.T("Да - заменить настройки, Нет - объединить с текущими."),
                Localization.T("Настройки"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3)
            If answer = DialogResult.Cancel Then Return

            Dim final As ModernViewerPreferences = If(answer = DialogResult.No, plan.Merged, imported)
            If answer = DialogResult.No AndAlso plan.MergeChanges.Count = 0 Then
                MessageBox.Show(Me, Localization.T("Файл прочитан. Отличий от текущих настроек нет."),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim backup As String
            Try
                backup = SettingsImportBackupStore.Create(current, SettingsImportBackupStore.DefaultDirectory())
            Catch ex As Exception
                AppFileLogger.LogException("Settings import backup", ex)
                MessageBox.Show(Me, Localization.TF("Не удалось создать резервную копию: {0}", FailureText.Describe(ex)),
                                Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            Dim persisted As Boolean = Main_Form.ReplaceModernPreferences(final)
            Main_Form.ReloadCustomHotkeys()
            If persisted Then
                MessageBox.Show(Me, Localization.TF("Настройки импортированы. Резервная копия: {0}", backup) & vbCrLf &
                                    Localization.T("Масштаб интерфейса и режим запуска применятся после перезапуска."),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
            Else
                MessageBox.Show(Me, Localization.TF("Настройки применены, но записать их не удалось. Повторная попытка будет сделана автоматически. Резервная копия: {0}", backup),
                                Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            ' Every row of this window still shows the value it was built with. Rebuild the
            ' window, the way a scale change does, so what is on screen is what was imported.
            Main_Form.RequestSettingsWindowRestart()
            Close()
        End Using
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
        Dim host As New Panel With {.Height = 34}
        Dim valueLabel As New Label With {.Dock = DockStyle.Right, .Width = 48, .TextAlign = ContentAlignment.MiddleRight}
        Dim slider As New TrackBar With {.Dock = DockStyle.Fill, .Minimum = minimum, .Maximum = maximum, .TickFrequency = 10,
                                         .SmallChange = 1, .LargeChange = 10, .AutoSize = False}
        host.Controls.Add(slider)
        host.Controls.Add(valueLabel)
        Dim isUpdating As Boolean = False
        Dim pause As New System.Windows.Forms.Timer With {.Interval = 200}
        AddHandler host.Disposed, Sub() pause.Dispose()
        Dim refresh As Action =
            Sub()
                isUpdating = True
                Try
                    slider.Value = Math.Max(minimum, Math.Min(maximum, getter()))
                    valueLabel.Text = slider.Value.ToString(Globalization.CultureInfo.CurrentCulture) & " %"
                Finally
                    isUpdating = False
                End Try
            End Sub
        refresh()

        AddHandler slider.ValueChanged,
            Sub()
                valueLabel.Text = slider.Value.ToString(Globalization.CultureInfo.CurrentCulture) & " %"
                If isUpdating Then Return
                apply(slider.Value)
                pause.Stop()
                pause.Start()
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
