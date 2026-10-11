#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public Class Table_Form
    Private modernGeneralPage As TabPage
    Private modernStoragePage As TabPage
    Private ReadOnly settingsGroups As New Dictionary(Of String, SettingsCollapsibleGroup)(StringComparer.Ordinal)
    Private ReadOnly pageIds As New Dictionary(Of TabPage, String)()
    Private ReadOnly rowGroups As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly groupCaptionKeys As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly groupRowOrder As New Dictionary(Of String, String())(StringComparer.Ordinal)
    Private settingsContext As New SettingsUiContext()
    Private settingsContextReady As Boolean
    Private restoringSettingsContext As Boolean
    Private settingsViewportInitialized As Boolean
    Private restoreGeneration As Integer
    Private contextTimer As Timer
    Private contextFlush As SettingsFlushScheduler
    Private contextSaveFailed As Boolean

    Private Sub BuildSettingsGroups()
        Dim invalid As Boolean
        Try
            settingsContext = SettingsUiContext.Read(GetSetting(App_name, Second_App_Name, "ModernSettingsContext", String.Empty), invalid)
            If invalid Then AppFileLogger.WriteLine("Settings UI: ignored malformed or unsupported context.")
        Catch ex As Exception
            AppFileLogger.LogException("Settings UI: reading context", ex)
        End Try
        pageIds.Add(modernGeneralPage, "general")
        pageIds.Add(Tab_Page_1, "recipients")
        pageIds.Add(Tab_Page_2, "images")
        pageIds.Add(Tab_Page_3, "video")
        pageIds.Add(modernAudioPage, "audio")
        pageIds.Add(Tab_Page_4, "files")
        pageIds.Add(Tab_Page_5, "ocr")
        pageIds.Add(modernTranslationPage, "translation")
        pageIds.Add(modernStoragePage, "storage")
        pageIds.Add(modernAboutPage, "about")
        pageIds.Add(modernResourcesPage, "resources")
        settingsContext.MergeSharingIntoResources()
        CreateModernPageFlow(modernGeneralPage)
        CreateModernPageFlow(modernStoragePage)
        For Each page As TabPage In pageIds.Keys
            Dim capturedPage As TabPage = page
            AddHandler PageFlow(page).Scroll,
                Sub()
                    If Not settingsContextReady OrElse restoringSettingsContext OrElse Not settingsViewportInitialized Then Return
                    CaptureSettingsPage(capturedPage)
                    contextFlush.MarkDirty()
                End Sub
        Next

        ' One placement owner for every existing setting. Values/defaults still belong to their services.
        AddSettingsGroup("general", "basic", "Основные настройки", True, "interface_language", "ui_theme")
        AddSettingsGroup("general", "interface", "Оформление", True,
                         "interface_scale", "reduce_motion", "chrome_pinned", "chrome_size", "chrome_opacity", "chrome_corners",
                         "show_info_overlay", "video_hide_cursor")
        AddSettingsGroup("general", "startup", "Запуск", True, "startup_open", "resume_last_playback", "allow_new_windows", "secondary_instance_warning", "welcome_replay")
        AddSettingsGroup("general", "advanced", "Дополнительные настройки", False, "settings_export_personal", "settings_export", "settings_import", "settings_defaults")
        AddSettingsGroup("recipients", "basic", "Основные настройки", True, "recipients_overlay")
        AddSettingsGroup("recipients", "overlay", "Вид таблицы получателей", False,
                         "recipients_position", "recipients_width", "recipients_font", "recipients_opacity", "recipients_rows")
        AddSettingsGroup("images", "basic", "Основные настройки", True, "new_image_scale", "exif_rotate", "wheel_zooms", "show_picture_size", "show_file_size", "show_file_date")
        AddSettingsGroup("images", "background", "Фон изображения", False, "background_color", "perspective", "dynamic_perspective", "animated_perspective", "halo_speed")
        AddSettingsGroup("images", "slideshow", "Поведение слайд-шоу", True, "slideshow_interval", "slideshow_random_order", "stop_slideshow_manual", "slideshow_ui")
        AddSettingsGroup("images", "advanced", "Дополнительные настройки", False, "hq_scaling", "thumbnail_size")
        AddSettingsGroup("video", "basic", "Основные настройки", True,
                         "include_video_navigation", "video_autoplay", "video_end_action", "video_remember_position", "shared_audio_settings")
        AddSettingsGroup("video", "tracks", "Дорожки и субтитры", False, "preferred_audio_language", "preferred_subtitle_language")
        AddSettingsGroup("video", "controls", "Управление воспроизведением", False, "video_click_action", "video_controls_delay", "video_controls_paused",
                         "video_seek_short", "video_seek_medium", "video_seek_long")
        AddSettingsGroup("audio", "basic", "Основные настройки", True, "include_audio_navigation", "audio_end_action", "video_volume", "video_mute")
        AddSettingsGroup("audio", "display", "Информация и управление", True, "audio_controls_visible", "audio_visualiser", "audio_sleep_timer")
        AddSettingsGroup("files", "basic", "Основные настройки", True,
                         "advance_after_copy", "name_collision", "after_file_operation", "create_missing_destination", "delete_to_recycle_bin", "confirm_delete", "no_confirmation")
        AddSettingsGroup("files", "content", "Типы файлов", True,
                         "include_subfolders", "included_extensions", "include_document_navigation", "document_extensions", "text_preview_max_kb", "sort_other_files", "other_extensions")
        AddSettingsGroup("files", "history", "Недавние файлы и папки", False, "recent_files_limit", "recent_folders_limit", "recent_history")
        AddSettingsGroup("files", "archives", "Архивы", False, "archive_entry_limit", "archive_entries_limit")
        AddSettingsGroup("files", "secure", "Защищённые файлы", False, "secure_in_navigation", "secure_remember_minutes", "secure_show_true_name", "secure_open_with")
        AddSettingsGroup("files", "advanced", "Клавиши и ассоциации", True, "custom_hotkeys", "image_associations", "video_associations", "live_open_with")
        AddSettingsGroup("ocr", "basic", "Основные настройки", True, "ocr_enabled", "ocr_auto")
        AddSettingsGroup("ocr", "recognition", "Распознавание текста", True, "ocr_source", "ocr_quality", "ocr_mode", "ocr_download")
        AddSettingsGroup("translation", "basic", "Основные настройки", True, "ocr_provider", "ocr_target")
        AddSettingsGroup("translation", "provider", "Сервис перевода", True, "ocr_endpoint", "ocr_server", "ocr_model", "ocr_api")
        AddSettingsGroup("translation", "overlay", "Перевод поверх изображения", False, "ocr_opacity", "ocr_overlay_visible")
        ' 130 §3.2: limits and clearing for the same cache belong together - per cache its
        ' limit row, then its usage+clear row. The OCR trio stays a group of its own (the
        ' disk toggle and the OCR limits); the collapsed "cache management" group is gone.
        AddSettingsGroup("storage", "basic", "Основные настройки", True,
                         "decode_cache_limit", "decode_cache_clear",
                         "thumb_cache_limit", "thumb_cache_clear",
                         "archive_cache_limit", "archive_cache_clear",
                         "remote_cache_limit", "remote_cache_clear")
        AddSettingsGroup("storage", "ocr", "Распознавание текста", True, "ocr_disk_cache", "ocr_cache_limit", "ocr_cache_clear")
        ' One page hosts the resource list and server controls in independent groups.
        ' The Open command remains in the list panel's own action row.
        AddSettingsGroup("resources", "basic", "Ресурсы", True, "resources_intro")
        AddSettingsGroup("resources", "server", "Android / SFTP", True, "sftp_intro", "sftp_manager", "sftp_guide", "android_app", "exchange_portal")
        Dim resourcesFlow As FlowLayoutPanel = PageFlow(modernResourcesPage)
        For Each child As Control In resourcesFlow.Controls.Cast(Of Control)().Where(Function(c) Not TypeOf c Is SettingsCollapsibleGroup).ToArray()
            If modernSettingsRows.Any(Function(row) row.Host Is child) Then Continue For
            child.Name = "settingsResourcesPanel"
            settingsGroups("resources/basic").Body.Controls.Add(child)
        Next
        Dim resourcesHost As Panel = DirectCast(resourcesPanel.Parent, Panel)
        AddHandler settingsGroups("resources/server").SizeChanged,
            Sub() SizeResourcesListHost(resourcesFlow, resourcesHost)

        For Each heading As ModernSettingRow In modernSettingsRows.Where(Function(r) r.Key.StartsWith("section_", StringComparison.Ordinal)).ToArray()
            modernSettingsRows.Remove(heading)
            heading.Host.Dispose()
        Next
        For Each row As ModernSettingRow In modernSettingsRows
            row.Host.Name = "settingsRow_" & row.Key
            Dim ownerId As String = Nothing
            If rowGroups.TryGetValue(row.Key, ownerId) Then
                settingsGroups(ownerId).Body.Controls.Add(row.Host)
            ElseIf row.Host.Parent IsNot Nothing AndAlso TypeOf row.Host.Parent.Parent Is TabPage AndAlso row.Host.Parent.Parent IsNot modernAboutPage Then
                Throw New InvalidOperationException("Settings row has no placement: " & row.Key)
            End If
        Next
        ' The recipient grid and operation status lines are not preference rows.
        Dim recipientFlow As FlowLayoutPanel = PageFlow(Tab_Page_1)
        For Each child As Control In recipientFlow.Controls.Cast(Of Control)().Where(Function(c) Not TypeOf c Is SettingsCollapsibleGroup).ToArray()
            child.Name = "settingsRecipientsGrid"
            settingsGroups("recipients/basic").Body.Controls.Add(child)
        Next
        If lblOcrRecognitionStatus IsNot Nothing Then settingsGroups("ocr/recognition").Body.Controls.Add(lblOcrRecognitionStatus)
        If lblOcrStatus IsNot Nothing Then settingsGroups("translation/provider").Body.Controls.Add(lblOcrStatus)
        ' The grid must remain keyboard reachable; focus restoration now handles its auto-scroll.
        Data_Grid_View.TabStop = True
        For Each group As SettingsCollapsibleGroup In settingsGroups.Values
            Dim order = groupRowOrder.First(Function(item) settingsGroups(item.Key) Is group).Value
            For index As Integer = 0 To order.Length - 1
                Dim rowId As String = order(index)
                Dim row = modernSettingsRows.FirstOrDefault(Function(item) item.Key = rowId)
                If row IsNot Nothing Then group.Body.Controls.SetChildIndex(row.Host, index)
            Next
            group.ArrangeContents()
        Next

        contextTimer = New Timer()
        contextFlush = New SettingsFlushScheduler(AddressOf WriteSettingsContext,
            Sub(delay)
                contextTimer.Interval = delay
                contextTimer.Start()
            End Sub,
            Sub() contextTimer.Stop(), Sub(message) AppFileLogger.WriteLine("Settings UI: " & message))
        AddHandler contextTimer.Tick, Sub()
                                         contextTimer.Stop()
                                         contextFlush.OnTimerTick()
                                     End Sub
        AddHandler FormClosing, AddressOf SaveSettingsContextOnClose
        AddHandler Application.ApplicationExit, AddressOf SaveSettingsContextOnExit
        AddHandler Disposed, Sub()
                                 RemoveHandler Application.ApplicationExit, AddressOf SaveSettingsContextOnExit
                                 contextTimer.Dispose()
                             End Sub
        settingsContextReady = True
        restoringSettingsContext = True
        Tab_Control.SelectedTab = pageIds.First(Function(p) p.Value = settingsContext.ResolvePage(pageIds.Values)).Key
        restoringSettingsContext = False
        LocalizeSettingsGroups()
    End Sub

    Private Function PageFlow(page As TabPage) As FlowLayoutPanel
        Return modernPageFlows.First(Function(flow) flow.Parent Is page)
    End Function

    Private Sub AddSettingsGroup(pageId As String, groupId As String, captionKey As String, defaultExpanded As Boolean, ParamArray rows As String())
        Dim id As String = pageId & "/" & groupId
        Dim page As TabPage = pageIds.First(Function(entry) entry.Value = pageId).Key
        ' The header caption is Semibold 10.5 - visibly larger than a row's 8.75, one half
        ' of how a header stands apart from the rows it governs (127 §3.4; the blended
        ' band fill is the other half and arrives with the palette pass).
        Dim group As New SettingsCollapsibleGroup(id, settingsScale, SettingsFont("Segoe UI Semibold", Sf(10.5F)),
            Function(expanded, ink) GlyphRenderer.Render(IconMap.GetMeaning(If(expanded, "collapsibleSectionOpen", "collapsibleSection")).GlyphId,
                Sp(20), ink, AddressOf RuntimeBootstrap.OpenBundledAsset))
        group.Expanded = settingsContext.Page(pageId).Expanded(groupId, defaultExpanded)
        groupCaptionKeys.Add(id, captionKey)
        groupRowOrder.Add(id, rows)
        settingsGroups.Add(id, group)
        PageFlow(page).Controls.Add(group)
        For Each row As String In rows
            rowGroups.Add(row, id)
        Next
        Dim headerY As Integer
        AddHandler group.ExpansionChanging,
            Sub()
                If Not settingsContextReady Then Return
                headerY = group.Header.PointToScreen(Point.Empty).Y - PageFlow(page).PointToScreen(Point.Empty).Y
            End Sub
        AddHandler group.ExpansionChanged,
            Sub()
                If Not settingsContextReady OrElse restoringSettingsContext Then Return
                settingsContext.Page(pageId).Groups(groupId) = group.Expanded
                LayoutModernSettingsPages()
                Dim flow As FlowLayoutPanel = PageFlow(page)
                Dim top As Integer = group.Header.PointToScreen(Point.Empty).Y - flow.PointToScreen(Point.Empty).Y - flow.AutoScrollPosition.Y
                flow.AutoScrollPosition = New Point(0, Math.Max(0, top - headerY))
                CaptureSettingsPage(page)
                contextFlush.MarkDirty()
            End Sub
    End Sub

    Private Sub LocalizeSettingsGroups()
        If Not settingsContextReady Then Return
        For Each item In settingsGroups
            item.Value.Caption = Localization.T(groupCaptionKeys(item.Key))
        Next
        LayoutModernSettingsPages()
    End Sub

    Private Function ContextAnchors(page As TabPage) As Dictionary(Of String, Control)
        Dim result As New Dictionary(Of String, Control)(StringComparer.Ordinal)
        For Each item In settingsGroups
            If item.Value.Parent Is PageFlow(page) Then
                result.Add(item.Key, item.Value.Header)
                If item.Value.Expanded Then
                    For Each row As ModernSettingRow In modernSettingsRows.Where(Function(r) r.Host.Parent Is item.Value.Body)
                        result.Add(row.Key, row.Host)
                    Next
                End If
            End If
        Next
        For Each row As ModernSettingRow In modernSettingsRows.Where(Function(r) r.Host.Parent Is PageFlow(page))
            result.Add(row.Key, row.Host)
        Next
        Return result
    End Function

    Private Sub CaptureSettingsPage(page As TabPage)
        If page Is Nothing OrElse Not settingsContextReady OrElse restoringSettingsContext OrElse Not settingsViewportInitialized OrElse Not IsHandleCreated Then Return
        Dim flow As FlowLayoutPanel = PageFlow(page)
        Dim anchors As Dictionary(Of String, Control) = ContextAnchors(page)
        If anchors.Count = 0 Then Return
        Dim origin As Integer = flow.PointToScreen(Point.Empty).Y
        Dim ordered = anchors.OrderBy(Function(a) a.Value.PointToScreen(Point.Empty).Y).ToArray()
        Dim anchor = ordered.FirstOrDefault(Function(a) a.Value.PointToScreen(Point.Empty).Y + a.Value.Height > origin)
        If anchor.Key Is Nothing Then anchor = ordered.Last()
        Dim state As SettingsPageContext = settingsContext.Page(pageIds(page))
        state.AnchorId = anchor.Key
        state.AnchorOffsetLogical = CInt((anchor.Value.PointToScreen(Point.Empty).Y - origin) / settingsScale)
        state.FocusId = String.Empty
        For Each item In anchors
            If item.Value.ContainsFocus Then
                state.FocusId = item.Key
                Exit For
            End If
        Next
    End Sub

    Private Sub ChangeSettingsContextPage()
        If Not settingsContextReady OrElse restoringSettingsContext Then Return
        ' Deselecting captured the departing page while it was still visible.
        settingsContext.LastPageId = pageIds(Tab_Control.SelectedTab)
        contextFlush.MarkDirty()
        QueueRestoreSettingsContext()
    End Sub

    ''' <summary>Explicit setting navigation overrides remembered context, without changing its value.</summary>
    Friend Sub RevealModernSetting(rowId As String)
        If Not settingsContextReady Then Return
        Dim ownerId As String = Nothing
        If Not rowGroups.TryGetValue(rowId, ownerId) Then Return
        Dim pageId As String = ownerId.Split("/"c)(0)
        Tab_Control.SelectedTab = pageIds.First(Function(p) p.Value = pageId).Key
        settingsGroups(ownerId).Expanded = True
        Dim state As SettingsPageContext = settingsContext.Page(pageId)
        state.AnchorId = rowId
        state.AnchorOffsetLogical = 0
        state.FocusId = rowId
        contextFlush.MarkDirty()
        QueueRestoreSettingsContext()
    End Sub

    Private Sub QueueRestoreSettingsContext()
        If Not settingsContextReady OrElse Not IsHandleCreated OrElse IsDisposed Then Return
        restoreGeneration += 1
        Dim generation As Integer = restoreGeneration
        Dim page As TabPage = Tab_Control.SelectedTab
        BeginInvoke(New MethodInvoker(
            Sub()
                If IsDisposed OrElse Not Visible OrElse generation <> restoreGeneration OrElse Tab_Control.SelectedTab IsNot page Then Return
                restoringSettingsContext = True
                Try
                    LayoutModernSettingsPages()
                    Dim anchors As Dictionary(Of String, Control) = ContextAnchors(page)
                    Dim state As SettingsPageContext = settingsContext.Page(pageIds(page))
                    Dim id As String = state.ResolveAnchor(anchors.Keys, rowGroups)
                    Dim flow As FlowLayoutPanel = PageFlow(page)
                    If id Is Nothing Then Return
                    Dim focused As Control = Nothing
                    If anchors.TryGetValue(state.FocusId, focused) Then
                        If TypeOf focused Is Button Then
                            focused.Focus()
                        Else
                            focused.SelectNextControl(Nothing, True, True, True, False)
                        End If
                    End If
                    Dim top As Integer = anchors(id).PointToScreen(Point.Empty).Y - flow.PointToScreen(Point.Empty).Y - flow.AutoScrollPosition.Y
                    flow.AutoScrollPosition = New Point(0, Math.Max(0, top - CInt(state.AnchorOffsetLogical * settingsScale)))
                Finally
                    restoringSettingsContext = False
                    settingsViewportInitialized = True
                End Try
            End Sub))
    End Sub

    Private Sub SaveSettingsContextOnClose(sender As Object, e As FormClosingEventArgs)
        OcrEndpoint_TextChanged(txtOcrEndpoint, EventArgs.Empty)
        OcrModelName_TextChanged(cmbOcrModelName, EventArgs.Empty)
        OcrApi_TextChanged(txtOcrApi, EventArgs.Empty)
        SaveSettingsContext()
    End Sub

    Private Sub SaveSettingsContextOnExit(sender As Object, e As EventArgs)
        If Not IsDisposed Then SaveSettingsContext()
    End Sub

    Private Sub SaveSettingsContext()
        If Not settingsContextReady Then Return
        CaptureSettingsPage(Tab_Control.SelectedTab)
        settingsContext.LastPageId = pageIds(Tab_Control.SelectedTab)
        For Each item In settingsGroups
            Dim ids As String() = item.Key.Split("/"c)
            settingsContext.Page(ids(0)).Groups(ids(1)) = item.Value.Expanded
        Next
        contextFlush.SaveNow()
    End Sub

    Private Function WriteSettingsContext() As Boolean
        If MultiWindowPolicy.IsSecondaryInstance() Then Return True
        Try
            SaveSetting(App_name, Second_App_Name, "ModernSettingsContext", settingsContext.ToJson())
            contextSaveFailed = False
            Return True
        Catch ex As Exception
            AppFileLogger.LogException("Settings UI: saving context", ex)
            If Not contextSaveFailed Then
                contextSaveFailed = True
                ViewerBox.Show(Me, Localization.T("Не удалось сохранить расположение настроек. Закройте и снова откройте настройки, чтобы повторить."),
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            Return False
        End Try
    End Function
End Class
#End If
