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
    ' WINDOWS-UI sections 5 and 9: construct a page's editors on its first visit.
    ' Group metadata remains available for navigation and remembered context.
    Private ReadOnly pendingSettingsRows As New Dictionary(Of String, Action)(StringComparer.Ordinal)
    Private ReadOnly pendingSettingsPages As New Dictionary(Of String, List(Of Action))(StringComparer.Ordinal)
    Private ReadOnly builtSettingsPages As New HashSet(Of TabPage)()
    Private ReadOnly pendingSettingsGroups As New Dictionary(Of String, Action)(StringComparer.Ordinal)
    Private modernShellInitializing As Boolean
    Private ReadOnly parkedSettingsControls As New List(Of Control)()

    Private Sub PrepareModernSettingsForDisplay()
        CaptureSettingsPage(Tab_Control.SelectedTab)
        If Not modernSettingsBuilt Then ParkLegacySettingsControls()
        RightToLeft = If(Localization.IsRightToLeft(), RightToLeft.Yes, RightToLeft.No)
        InitializeTooltips()
        Text = Localization.T("Настройки")
        If modernSettingsBuilt Then
            For Each page In builtSettingsPages
                PopulateModernSettingsControls(pageIds(page))
            Next
        End If
        BuildModernSettingsLayout()
        LocalizeModernSettingsLayout()
        WireSettingsRows()
        RefreshExpandedSettingsRows()
        FitInitialSettingsWindowToContent()
        preparedBeforeLoad = True
    End Sub

    ''' <summary>Populate only the shared Designer controls owned by the requested page.
    ''' Opening General must not construct a destination grid or prepare image/audio editors.</summary>
    Private Sub PopulateModernSettingsControls(pageId As String)
        Select Case pageId
            Case "general"
                chk_Show_Info_Overlay.Checked = Is_Show_Info_Overlay
            Case "recipients"
                SetOnTop.Checked = Is_Show_Recipients_Overlay
                If Data_Grid_View.Columns.Count = 0 Then
                    Data_Grid_View.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "KeyColumn", .Width = 60, .ReadOnly = True})
                    Data_Grid_View.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "FolderColumn", .Width = 300, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
                End If
                Data_Grid_View.Columns(0).HeaderText = Localization.T("клавиша")
                Data_Grid_View.Columns(1).HeaderText = Localization.T("каталог-получатель")
                Data_Grid_View.Rows.Clear()
                Data_Grid_View.Rows.Add()
                Data_Grid_View.Item(0, 0).Value = Main_Form.DeleteKeyLabel()
                Data_Grid_View.Item(0, 0).ReadOnly = True
                Data_Grid_View.Item(1, 0).Value = Localization.T("Удаление файла")
                Data_Grid_View.Item(1, 0).ReadOnly = True
                For slot As Integer = 1 To 10
                    Data_Grid_View.Rows.Add()
                    Data_Grid_View.Item(0, slot).Value = Main_Form.SlotKeyLabel(slot)
                    Data_Grid_View.Item(1, slot).Value = If(Hardkeys_to_move_mediafile(slot), String.Empty)
                Next
                lbl_Grid_Hint.Text = Localization.T("Двойной клик по номеру клавиши - перенести файл, Shift + двойной клик - скопировать. Двойной клик по пути - выбрать папку.")
                ApplySlotHealthToGrid()
                If builtSettingsPages.Contains(Tab_Page_1) Then StyleModernRecipientGrid()
            Case "images"
                Dim colorScheme As Integer = Form_Color_Scheme
                cmbox_color_schema.Items.Clear()
                cmbox_color_schema.Items.AddRange({Localization.T("По углу"), Localization.T("Чёрный"), Localization.T("Белый"),
                                                   Localization.T("По краю"), Localization.T("По верху"), Localization.T("По низу")})
                cmbox_color_schema.SelectedIndex = If(colorScheme >= 0 AndAlso colorScheme < cmbox_color_schema.Items.Count, colorScheme, 1)
                cmb_Halo_Speed.Items.Clear()
                cmb_Halo_Speed.Items.AddRange({Localization.T("Медленная"), Localization.T("Средняя"), Localization.T("Быстрая")})
                cmb_Halo_Speed.SelectedIndex = HaloSpeedToIndex(Halo_Animation_Speed)
                chb_perspectiva.Checked = Is_Pespective
                chk_Dynamic_Perspective.Checked = Is_Dynamic_Perspective
                chk_Animated_Perspective.Checked = Is_Animated_Perspective
                SyncDynamicPerspectiveEnabled()
                chkb_show_pic_size.Checked = Main_Form.Is_to_show_picture_sizes
                chkb_is_to_show_file_datetime.Checked = Main_Form.Is_to_show_file_datetime
                chkb_show_file_size.Checked = Main_Form.Is_to_show_file_sizes
                chk_Exif_AutoRotate.Checked = Is_Exif_AutoRotate
                chk_Hq_Scaling.Checked = Is_HighQuality_Scaling
                chk_Wheel_Zooms.Checked = Zoom_Wheel_Zooms
                num_Slideshow_Interval.DecimalPlaces = 3
                num_Slideshow_Interval.Increment = 0.1D
                num_Slideshow_Interval.Value = Math.Max(num_Slideshow_Interval.Minimum,
                    Math.Min(num_Slideshow_Interval.Maximum, Slideshow_Base_Interval_Ms / 1000D))
                updatingThumbnailChoice = True
                Try
                    cmb_Picture_Size.Items.Clear()
                    For Each tier In ModernViewerPreferences.PanelThumbnailTiers
                        cmb_Picture_Size.Items.Add(tier.ToString() & " px")
                    Next
                    Dim size As Integer = Main_Form.GetModernPreferences().PanelThumbnailSize
                    If size = 0 Then size = ModernViewerPreferences.SnapPanelThumbnailSize(Math.Max(Picture_Box_Width_At_Panel, Picture_Box_Height_At_Panel))
                    cmb_Picture_Size.SelectedIndex = Array.IndexOf(ModernViewerPreferences.PanelThumbnailTiers, size)
                Finally
                    updatingThumbnailChoice = False
                End Try
            Case "audio"
                updatingAudioDefaults = True
                Try
                    chk_Video_Mute.Checked = Main_Form.CurrentVideoMuted
                    num_Video_Volume.Value = Math.Max(num_Video_Volume.Minimum, Math.Min(num_Video_Volume.Maximum, Main_Form.CurrentVideoVolumePercent))
                Finally
                    updatingAudioDefaults = False
                End Try
            Case "files"
                chkb_no_request_before_file_operation.Checked = Is_no_request_before_file_operation
            Case "resources"
                BuildShareLauncherButtonIfNeeded()
                LocalizeShareLauncherButton()
            Case "ocr", "translation", "storage"
                If _ocrBuilt Then PrepareOcrTabForDisplay()
        End Select
    End Sub

    Private Sub ParkLegacySettingsControls()
        If parkedSettingsControls.Count > 0 Then Return
        ' Keep Designer controls and their Handles bindings, without creating native
        ' windows or walking hidden legacy groups during shell theme/layout passes.
        For Each page As TabPage In Tab_Control.TabPages
            For Each control As Control In page.Controls.Cast(Of Control)().ToArray()
                page.Controls.Remove(control)
                parkedSettingsControls.Add(control)
            Next
        Next
        AddHandler Disposed,
            Sub()
                For Each control As Control In parkedSettingsControls
                    If Not control.IsDisposed Then control.Dispose()
                Next
                parkedSettingsControls.Clear()
            End Sub
    End Sub

    Private Sub RegisterSettingsRow(key As String, build As Action)
        pendingSettingsRows.Add(key, build)
    End Sub

    Private Sub RegisterSettingsPage(pageId As String, build As Action)
        If Not pendingSettingsPages.ContainsKey(pageId) Then pendingSettingsPages.Add(pageId, New List(Of Action)())
        pendingSettingsPages(pageId).Add(build)
    End Sub

    Private Sub EnsureModernSettingsPage(page As TabPage)
        If Not settingsContextReady OrElse page Is Nothing OrElse builtSettingsPages.Contains(page) Then Return
        Dim pageId As String = pageIds(page)
        builtSettingsPages.Add(page)
        Dim flow = PageFlow(page)
        flow.SuspendLayout()
        Dim heldGroups As New List(Of SettingsCollapsibleGroup)()
        Try
            PopulateModernSettingsControls(pageId)
            If pageId = "ocr" OrElse pageId = "translation" OrElse pageId = "storage" Then
                Dim firstOcrBuild As Boolean = Not _ocrBuilt
                PrepareOcrTabForDisplay()
                ocrInner.Visible = False
                If firstOcrBuild Then
                    For Each control As Control In Tab_Page_5.Controls.Cast(Of Control)().Where(Function(item) item IsNot PageFlow(Tab_Page_5)).ToArray()
                        Tab_Page_5.Controls.Remove(control)
                        parkedSettingsControls.Add(control)
                    Next
                End If
                For Each oldLabel As Control In {lblOcrTranslator, lblOcrEndpoint, lblOcrServer, lblOcrModel,
                                                  lblOcrApi, lblOcrTarget, lblOcrSource, lblOcrQuality, lblOcrMode, lblOcrOpacity}
                    oldLabel.Visible = False
                Next
            End If
            For Each key In pendingSettingsGroups.Keys.Where(Function(id) id.StartsWith(pageId & "/", StringComparison.Ordinal)).ToArray()
                pendingSettingsGroups(key)()
                pendingSettingsGroups.Remove(key)
            Next
            For Each group In settingsGroups.Values.Where(Function(item) item.GroupId.StartsWith(pageId & "/", StringComparison.Ordinal))
                group.Body.SuspendLayout()
                heldGroups.Add(group)
            Next
            Dim builders As List(Of Action) = Nothing
            If pendingSettingsPages.TryGetValue(pageId, builders) Then
                For Each buildPage As Action In builders
                    buildPage()
                Next
                pendingSettingsPages.Remove(pageId)
            End If
            For Each key In pendingSettingsRows.Keys.ToArray()
                Dim groupId As String = Nothing
                If Not rowGroups.TryGetValue(key, groupId) Then Throw New InvalidOperationException("Settings row has no group: " & key)
                If Not groupId.StartsWith(pageId & "/", StringComparison.Ordinal) Then Continue For
                pendingSettingsRows(key)()
                pendingSettingsRows.Remove(key)
            Next
            FinishModernSettingsPage(pageId)
            flow.BringToFront()
            If Not modernShellInitializing Then
                If pageId = "recipients" Then StyleModernRecipientGrid()
                StyleModernSettingsControlTree(page)
                ApplyModernPaletteToTree(page)
                ThemeApplier.ApplyNativeTree(page, settingsDarkMode)
                RewriteModernSettingRows()
                WireSettingsRows()
                LocalizeSettingsDiscovery()
                For Each item In settingsGroups.Where(Function(entry) entry.Key.StartsWith(pageId & "/", StringComparison.Ordinal))
                    item.Value.Caption = Localization.T(groupCaptionKeys(item.Key))
                    ApplyModernGroupPalette(item.Value)
                Next
            End If
        Catch
            builtSettingsPages.Remove(page)
            Throw
        Finally
            For Each group In heldGroups
                group.Body.ResumeLayout(True)
            Next
            flow.ResumeLayout(False)
        End Try
    End Sub
End Class
#End If
