#If Not NETFRAMEWORK Then
Option Strict On

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

    Private Sub AddExpandedSettingsRows(destinations As FlowLayoutPanel, viewing As FlowLayoutPanel,
                                        video As FlowLayoutPanel, files As FlowLayoutPanel,
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
        AddPreferenceCheck(video, "video_autoplay", p.VideoAutoplay, Sub(v) p.VideoAutoplay = v)
        AddPreferenceNumber(video, "video_controls_delay", p.VideoControlsHideDelaySec, 1, 15, Sub(v) p.VideoControlsHideDelaySec = v)
        AddPreferenceCheck(video, "video_controls_paused", p.ShowVideoControlsWhenPaused, Sub(v) p.ShowVideoControlsWhenPaused = v)
        AddPreferenceChoice(video, "video_click_action", p.VideoSingleClickAction,
                            {Choice("pauseResume", "Пауза / продолжить"), Choice("nextFile", "Следующий файл")}, Sub(v) p.VideoSingleClickAction = v)
        AddPreferenceChoice(video, "video_end_action", p.VideoEndAction,
                            {Choice("stay", "Оставить последний кадр"), Choice("nextFile", "Следующий файл"), Choice("repeat", "Повторить")}, Sub(v) p.VideoEndAction = v)
        AddPreferenceText(video, "preferred_audio_language", p.PreferredAudioLanguage, Sub(v) p.PreferredAudioLanguage = v)
        AddPreferenceText(video, "preferred_subtitle_language", p.PreferredSubtitleLanguage, Sub(v) p.PreferredSubtitleLanguage = v)

        AddSectionHeader(files, "section_file_behavior")
        AddPreferenceChoice(files, "name_collision", p.NameCollisionPolicy,
                            {Choice("ask", "Спрашивать"), Choice("skip", "Пропустить"), Choice("rename", "Сохранить оба"), Choice("replace", "Заменить")}, Sub(v) p.NameCollisionPolicy = v)
        AddPreferenceChoice(files, "after_file_operation", p.AfterFileOperation,
                            {Choice("next", "Следующий файл"), Choice("stay", "Остаться на текущем"), Choice("closeIfEmpty", "Закрыть, если файлов не осталось")}, Sub(v) p.AfterFileOperation = v)
        AddPreferenceCheck(files, "include_subfolders", p.IncludeSubfolders, Sub(v) p.IncludeSubfolders = v)
        AddPreferenceText(files, "included_extensions", p.IncludedExtensionsForEditing(), Sub(v) p.SetIncludedExtensionsFromEditing(v))
        AddPreferenceNumber(files, "recent_files_limit", p.RecentFilesLimit, 0, 200, Sub(v) p.RecentFilesLimit = v)
        AddPreferenceNumber(files, "recent_folders_limit", p.RecentFoldersLimit, 0, 200, Sub(v) p.RecentFoldersLimit = v)
        AddPreferenceChoice(files, "startup_open", p.StartupOpenMode,
                            {Choice("home", "Стартовую страницу"), Choice("lastFolder", "Последнюю папку"), Choice("lastFile", "Последний файл")}, Sub(v) p.StartupOpenMode = v)
        AddSettingsTransferRows(files)
        AddAllowNewWindowsRow(files, p)

        AddPreferenceNumber(ocr, "ocr_cache_limit", p.OcrDiskCacheMaxMb, 0, 1024, Sub(v) p.OcrDiskCacheMaxMb = v)
    End Sub

    Private Sub AddSettingsTransferRows(files As FlowLayoutPanel)
        Dim exportButton As New Button With {.AutoSize = True, .Text = Localization.T("Экспортировать настройки")}
        AddHandler exportButton.Click,
            Sub()
                Using dialog As New SaveFileDialog With {.Filter = "Fast Media Sorter settings|*.fms-settings.json", .DefaultExt = "fms-settings.json", .AddExtension = True}
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    Try
                        File.WriteAllText(dialog.FileName, Main_Form.GetModernPreferences().ExportJson())
                        MessageBox.Show(Me, Localization.T("Настройки экспортированы."), Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        MessageBox.Show(Me, Localization.TF("Не удалось экспортировать настройки: {0}", ex.Message), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub
        AddSettingRow(files, "settings_export", exportButton, 210, True)

        Dim importButton As New Button With {.AutoSize = True, .Text = Localization.T("Импортировать настройки")}
        AddHandler importButton.Click,
            Sub()
                Using dialog As New OpenFileDialog With {.Filter = "Fast Media Sorter settings|*.fms-settings.json"}
                    If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                    Try
                        Dim imported As ModernViewerPreferences = ModernViewerPreferences.FromJson(File.ReadAllText(dialog.FileName))
                        Dim backup As String = dialog.FileName & ".backup"
                        File.WriteAllText(backup, Main_Form.GetModernPreferences().ExportJson(includePersonalData:=True))
                        Main_Form.ReplaceModernPreferences(imported)
                        MessageBox.Show(Me, Localization.TF("Настройки импортированы. Резервная копия: {0}", backup), Localization.T("Настройки"), MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        ' No write happens before FromJson validated the complete document.
                        MessageBox.Show(Me, Localization.TF("Не удалось импортировать настройки: {0}", ex.Message), Localization.T("Ошибка"), MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End Using
            End Sub
        AddSettingRow(files, "settings_import", importButton, 210, True)
    End Sub

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

    Private Sub AddPreferenceText(flow As FlowLayoutPanel, key As String, value As String, apply As Action(Of String))
        Dim textBox As New TextBox With {.Text = value}
        AddHandler textBox.TextChanged,
            Sub()
                apply(textBox.Text.Trim())
                ApplyExpandedPreferences()
            End Sub
        AddSettingRow(flow, key, textBox, 260, True)
    End Sub

    Private Sub ApplyExpandedPreferences()
        Main_Form.ApplyModernPreferencesFromSettings()
    End Sub
End Class
#End If
