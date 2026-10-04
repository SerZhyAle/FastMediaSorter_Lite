#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Windows.Forms

' Modern chrome keeps the Designer controls only as event/state carriers. They never dock
' into the media area; both rows use the same ordered inventory as the legacy toolbar.
Partial Public Class Main_Form

    Private NotInheritable Class ChromePanel
        Public ReadOnly Name As String
        Public ReadOnly Host As New ChromeHost()
        Public ReadOnly Legacy As New List(Of KeyValuePair(Of ChromeItem, Control))()
        Public TargetAlpha As Double
        Public Alpha As Double
        Public Wanted As Boolean
        Public LastActivity As Long
        Public AppliedOpacity As Double = -1.0R
        ''' <summary>The pin button in the panel's corner (see ChromeSurface.PinItem).</summary>
        Public ReadOnly Pin As New ChromeItem("chrome_pin_" & name, ChromeItemKind.Toggle) With {.IsPin = True}

        Public Sub New(name As String)
            Me.Name = name
            Host.Name = name
        End Sub
    End Class

    Private ReadOnly chrome_Panels As New List(Of ChromePanel)()
    Private ReadOnly chrome_Timer As New System.Windows.Forms.Timer() With {.Interval = ChromeMetrics.IdleTickMilliseconds}
    Private ReadOnly chrome_Clock As Stopwatch = Stopwatch.StartNew()
    Private chrome_Path As ChromePathBox
    Private chrome_Last_Tick As Long
    Private chrome_Last_Pointer As Point
    Private chrome_Applied_Scale As Single
    Private chrome_Theme As ChromeTheme = ChromeTheme.FromScheme(Color.Black, Color.Gainsboro, False)
    Private chrome_Scheme_Back As Color = Color.Black
    Private chrome_Scheme_Fore As Color = Color.Gainsboro
    Private chrome_High_Contrast As Boolean
    Private chrome_Text_Font As Font
    Private chrome_Symbol_Font As Font
    Private chrome_Tracking As Boolean
    Private chrome_Keyboard_Mode As Boolean
    Private chrome_Focus_Panel As Integer
    Private chrome_F10_Pending As Boolean

    Friend Function ModernChromeOn() As Boolean
        Return True
    End Function

    ''' <summary>"Keep floating panels visible" in the settings: every panel pinned at once.</summary>
    Private Function ChromePinnedOn() As Boolean
        Return modern_Preferences IsNot Nothing AndAlso modern_Preferences.ChromePinned
    End Function

    ''' <summary>The panel's own pin - the button in its corner.</summary>
    Private Function ChromeOwnPin(p As ChromePanel) As Boolean
        If modern_Preferences Is Nothing Then Return False
        Select Case p.Name
            Case "chrome_Row1" : Return modern_Preferences.ChromeRow1Pinned
            Case "chrome_Row2" : Return modern_Preferences.ChromeRow2Pinned
            Case Else : Return modern_Preferences.ChromeStatusPinned
        End Select
    End Function

    Private Sub ChromeSetOwnPin(p As ChromePanel, value As Boolean)
        If modern_Preferences Is Nothing Then Return
        Select Case p.Name
            Case "chrome_Row1" : modern_Preferences.ChromeRow1Pinned = value
            Case "chrome_Row2" : modern_Preferences.ChromeRow2Pinned = value
            Case Else : modern_Preferences.ChromeStatusPinned = value
        End Select
    End Sub

    ''' <summary>Whether this panel stays up: pinned for all, or pinned on its own.</summary>
    Private Function ChromePanelPinned(p As ChromePanel) As Boolean
        Return ChromePinnedOn() OrElse ChromeOwnPin(p)
    End Function

    ''' <summary>The corner button's click. While "keep visible" is on for every panel, unpinning one
    ''' turns that all-switch into the three individual pins minus this one - otherwise the click would
    ''' do nothing visible.</summary>
    Private Sub ChromeTogglePin(p As ChromePanel)
        If modern_Preferences Is Nothing Then Return
        If ChromePanelPinned(p) Then
            If modern_Preferences.ChromePinned Then
                modern_Preferences.ChromePinned = False
                For Each other As ChromePanel In chrome_Panels
                    ChromeSetOwnPin(other, True)
                Next
            End If
            ChromeSetOwnPin(p, False)
        Else
            ChromeSetOwnPin(p, True)
        End If
        For Each other As ChromePanel In chrome_Panels
            SyncChromePin(other)
            other.Host.Surface.Invalidate()
        Next
        p.Wanted = True
        p.LastActivity = chrome_Clock.ElapsedMilliseconds
        Try
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: persisting a panel pin", ex)
            MarkSettingsDirty()
        End Try
    End Sub

    ''' <summary>The button's state and words follow the preference (a language switch re-reads them).</summary>
    Private Sub SyncChromePin(p As ChromePanel)
        Dim pinned As Boolean = ChromePanelPinned(p)
        p.Pin.Checked = pinned
        p.Pin.AccessibleName = If(pinned, Localization.T("Открепить панель"), Localization.T("Закрепить панель"))
        p.Pin.Tooltip = If(pinned, Localization.T("Панель закреплена и не скрывается. Нажмите, чтобы открепить."),
                           Localization.T("Закрепить панель: она перестанет скрываться."))
    End Sub

    Friend Sub ApplyModernChrome()
        If panel_Media Is Nothing Then Return
        flow_Toolbar.Visible = False
        panel_Status.Visible = False
        If Not IsHandleCreated OrElse Not Visible Then Return
        If chrome_Panels.Count = 0 Then BuildChromePanels()
        ChromeConfigure()
        chrome_Last_Tick = chrome_Clock.ElapsedMilliseconds
        chrome_Timer.Start()
    End Sub

    Private Sub BuildChromePanels()
        AddHandler chrome_Timer.Tick, AddressOf Chrome_Timer_Tick
        AddHandler ChromeMenu.FocusKeyConsumed, AddressOf ChromePopupFocusKeyConsumed
        BuildChromeRow("chrome_Row1", New Control() {
            chkbox_Top_Most, cmbox_Sort, cmbox_Media_Folder, btn_choose_file, btn_Select_Folder,
            btn_Review, btn_Panel, btn_Full_Screen, lbl_Slideshow_Time, btn_Language, lbl_Info})
        Dim row2 As New List(Of Control)(toolbar_Row2_Items)
        If btn_ToVideo IsNot Nothing AndAlso Not row2.Contains(btn_ToVideo) Then row2.Add(btn_ToVideo)
        BuildChromeRow("chrome_Row2", row2)
        BuildChromeRow("chrome_Status", New Control() {lbl_Current_File, lbl_Status})
        TrackChromeGeometry()
    End Sub

    Private Sub BuildChromeRow(name As String, controls As IEnumerable(Of Control))
        Dim panel As New ChromePanel(name)
        chrome_Panels.Add(panel)
        panel.Host.Owner = Me
        Dim items As New List(Of ChromeItem)()
        For Each carrier As Control In controls
            If carrier Is Nothing Then Continue For
            Dim kind As ChromeItemKind = ChromeItemKind.Button
            If TypeOf carrier Is Label AndAlso carrier IsNot lbl_Info Then kind = ChromeItemKind.Text
            If carrier Is chkbox_Top_Most OrElse carrier Is btn_Slideshow Then kind = ChromeItemKind.Toggle
            If carrier Is cmbox_Sort OrElse carrier Is btn_Language OrElse carrier Is btn_RecentFiles Then kind = ChromeItemKind.MenuButton
            Dim item As New ChromeItem(carrier.Name, kind) With {
                .Destructive = carrier Is bt_Delete,
                .ShowCaptionWithGlyph = carrier Is btn_Prev_File OrElse carrier Is btn_Next_File OrElse carrier Is bt_Delete,
                .MirrorInRtl = carrier Is btn_Prev_File OrElse carrier Is btn_Next_File}
            Dim meaning As IconMeaning = IconMap.GetMeaning(carrier.Name)
            If meaning IsNot Nothing Then item.GlyphId = meaning.GlyphId
            If carrier Is btn_Move_Table Then item.GlyphId = "app.settings"
            If carrier Is lbl_Info Then item.Caption = "i" : item.CaptionIsSymbol = True
            Dim source As Control = carrier
            item.Action = Sub() InvokeOnClick(source, EventArgs.Empty)
            If carrier Is chkbox_Top_Most Then
                item.Action = Sub()
                                  chkbox_Top_Most.Checked = Not chkbox_Top_Most.Checked
                                  Me.Activate()
                                  Me.Focus()
                              End Sub
            End If
            If carrier Is lbl_Info Then item.Action = Sub() lbl_Info_LinkClicked(lbl_Info, Nothing)
            If carrier Is lbl_Current_File OrElse carrier Is lbl_Status Then
                item.PreferredWidth = ChromeMetrics.PathWidth
                item.MinimumWidth = ChromeMetrics.PathMinWidth
                item.Kind = ChromeItemKind.Button
                item.Action = Sub() CopyFilePathToClipboard()
                If carrier Is lbl_Current_File Then item.AlignLeft = True
            End If
            If carrier Is cmbox_Sort Then item.Action = Sub() ChromeShowSort(item)
            If carrier Is btn_Language Then item.Action = Sub() ChromeShowLanguages(item)
            If carrier Is btn_RecentFiles Then item.Action = Sub() ChromeShowRecentFiles(item)
            If carrier Is btn_choose_file Then item.SecondaryAction = Sub() ShowOpenUrlPrompt()
            If carrier Is btn_Translate Then item.SecondaryAction = Sub() btn_Translate_MouseUp(btn_Translate, New MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0))
            If carrier Is cmbox_Media_Folder Then
                item.PreferredWidth = ChromeMetrics.PathWidth
                item.MinimumWidth = ChromeMetrics.PathMinWidth
                item.Action = Sub() chrome_Path.BeginEditing()
                item.SecondaryAction = Sub() ChromeShowFolderMenu(item)
                chrome_Path = New ChromePathBox() With {.Name = carrier.Name, .Dock = DockStyle.None}
                panel.Host.Surface.Controls.Add(chrome_Path)
                AddHandler chrome_Path.Accepted, Sub() ChromeAcceptPath()
                AddHandler chrome_Path.ContextRequested, Sub() ChromeShowFolderMenu(item)
                AddHandler chrome_Path.Editor.DragEnter, AddressOf Form1_DragEnter
                AddHandler chrome_Path.Editor.DragDrop, AddressOf Form1_DragDrop
                AddHandler chrome_Path.EditingStarted,
                    Sub()
                        ChromeLeaveKeyboardMode()
                        panel.Host.AllowActivation = True
                        panel.Host.Activate()
                    End Sub
                AddHandler chrome_Path.EditingEnded,
                    Sub()
                        panel.Host.AllowActivation = False
                        ' Windows redirects activation of an owner to its last active popup.
                        ' Remove the editing host from that choice before returning the keyboard.
                        panel.Host.Hide()
                        Me.Activate()
                        If panel.Alpha > 0 Then panel.Host.Show()
                    End Sub
            End If
            panel.Legacy.Add(New KeyValuePair(Of ChromeItem, Control)(item, carrier))
            items.Add(item)
            SyncChromeItem(item, carrier)
        Next
        panel.Host.Surface.OverflowId = If(name = "chrome_Row1", "btn_Overflow", "btn_Overflow_2")
        panel.Pin.Action = Sub() ChromeTogglePin(panel)
        SyncChromePin(panel)
        panel.Host.Surface.PinItem = panel.Pin
        panel.Host.Surface.StatusLayout = name = "chrome_Status"
        panel.Host.Surface.SetItems(items)
        AddHandler panel.Host.Surface.ItemInvoked, AddressOf Chrome_ItemInvoked
        AddHandler panel.Host.Surface.LayoutChanged, Sub() ChromePlacePath()
        AddHandler panel.Host.Surface.OverflowRequested, Sub(sender, args) ChromeShowOverflow(panel)
    End Sub

    Private Sub ChromePlacePath()
        If chrome_Path Is Nothing OrElse chrome_Panels.Count = 0 Then Return
        Dim panel As ChromePanel = chrome_Panels(0)
        For Each pair In panel.Legacy
            If pair.Value Is cmbox_Media_Folder Then
                chrome_Path.Bounds = pair.Key.Bounds
                chrome_Path.Visible = Not pair.Key.Bounds.IsEmpty
                Exit For
            End If
        Next
    End Sub

    Private Sub ChromeAcceptPath()
        Dim path As String = chrome_Path.Text.Trim()
        chrome_Path.EndEditing()
        If path.Length = 0 Then Return
        SlideShowStop()
        is_Slide_Show_Random_Mode = False
        LeaveArchiveUnlessShowing(path)
        LeaveResource()
        If TryOpenAsResourceLocator(path) Then Return
        Dim editing As Boolean = is_TextBox_Editing
        Try
            is_TextBox_Editing = True
            cmbox_Media_Folder.Text = path
        Finally
            is_TextBox_Editing = editing
        End Try
        Current_Folder_Path = path
        ReadShowMediaFile(Mode_FolderAndFile)
    End Sub

    Private Sub ChromeShowMenu(item As ChromeItem, entries As IEnumerable(Of ChromeMenuItem))
        Dim location As Point = Cursor.Position
        For Each panel In chrome_Panels
            If panel.Host.Surface.Items.Contains(item) Then
                location = panel.Host.Surface.PointToScreen(New Point(item.Bounds.Left, item.Bounds.Bottom))
                Exit For
            End If
        Next
        ChromeMenu.ShowPopup(Me, location, entries, chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font)
    End Sub

    Private Sub ChromeShowSort(item As ChromeItem)
        Dim entries As New List(Of ChromeMenuItem)()
        For index As Integer = 0 To cmbox_Sort.Items.Count - 1
            Dim selected As Integer = index
            entries.Add(New ChromeMenuItem("sort_" & index.ToString()) With {
                .Caption = cmbox_Sort.Items(index).ToString(), .Checked = index = cmbox_Sort.SelectedIndex,
                .Action = Sub() ChromeApplySort(selected)})
        Next
        ChromeShowMenu(item, entries)
    End Sub

    Private Sub ChromeApplySort(index As Integer)
        ' This is an explicit command, including choosing the current order again.
        ' Suppress the carrier event and perform one reload after updating its state.
        If chrome_Path IsNot Nothing Then chrome_Path.EndEditing(True)
        Dim editing As Boolean = is_TextBox_Editing
        Try
            is_TextBox_Editing = True
            cmbox_Sort.SelectedIndex = index
        Finally
            is_TextBox_Editing = editing
        End Try
        If Not String.IsNullOrEmpty(Current_Folder_Path) Then ReadShowMediaFile(Mode_FolderAndFile)
    End Sub

    Private Sub ChromeShowLanguages(item As ChromeItem)
        Dim entries As New List(Of ChromeMenuItem)()
        For index As Integer = 0 To Localization.Codes.Length - 1
            Dim code As String = Localization.Codes(index)
            entries.Add(New ChromeMenuItem("language_" & code) With {
                .Caption = Localization.Names(index),
                .Checked = code = Localization.CurrentCode, .Action = Sub() SetUiLanguage(code)})
        Next
        ChromeShowMenu(item, entries)
    End Sub

    Private Sub ChromeShowRecentFiles(item As ChromeItem)
        Dim entries As New List(Of ChromeMenuItem)()
        If recent_Media_File_List IsNot Nothing Then
            For Each path As String In recent_Media_File_List.AsEnumerable().Reverse()
                Dim selected As String = path
                entries.Add(New ChromeMenuItem(path) With {.Caption = IO.Path.GetFileName(path), .Tooltip = path,
                    .Action = Sub() ProcessArgument(selected)})
            Next
        End If
        ChromeShowMenu(item, entries)
    End Sub

    Private Sub ChromeShowFolderMenu(item As ChromeItem)
        Dim entries As New List(Of ChromeMenuItem) From {
            New ChromeMenuItem("folder_select") With {.Caption = folder_Menu_Select_Item.Text, .Action = Sub() SelectFolderViaDialog()},
            New ChromeMenuItem("file_select") With {.Caption = folder_Menu_File_Item.Text, .Action = Sub() Choose_file()},
            New ChromeMenuItem("archive_open") With {.Caption = folder_Menu_Open_Archive_Item.Text, .Action = Sub() OpenArchiveViaDialog()}}
        If IsArchiveMode() Then entries.Add(New ChromeMenuItem("archive_close") With {.Caption = folder_Menu_Close_Archive_Item.Text, .Action = Sub() CloseArchiveAndReturn()})
        If Not IsArchiveMode() AndAlso Not IsArchiveOpenPending() Then
            For Each r As ResourceDescriptor In RecentResources()
                Dim chosen As ResourceDescriptor = r
                entries.Add(New ChromeMenuItem("resource_" & r.Id) With {.Caption = r.DisplayName, .Tooltip = ResourceMessages.DisplayAddress(r), .Action = Sub() ConnectAndEnterResource(chosen, "")})
            Next
            entries.Add(New ChromeMenuItem("resource_manage") With {.Caption = Localization.T("Управление ресурсами.."), .Action = Sub() ShowResourceManager()})
        End If
        If Not IsArchiveMode() AndAlso Not IsArchiveOpenPending() Then entries.Add(New ChromeMenuItem("folder_share") With {.Caption = folder_Menu_Share_Item.Text, .Action = Sub() ActivateShareEntryPoint()})
        ChromeShowMenu(item, entries)
    End Sub

    Private Sub ChromeShowOverflow(panel As ChromePanel)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each item In panel.Host.Surface.OverflowItems
            Dim source As ChromeItem = item
            entries.Add(New ChromeMenuItem(item.Id) With {.Caption = If(item.AccessibleName.Length > 0, item.AccessibleName, item.Caption),
                .Enabled = item.Enabled, .Checked = item.Checked, .Destructive = item.Destructive,
                .Action = Sub() source.Action?.Invoke()})
        Next
        ChromeMenu.ShowPopup(Me, panel.Host.PointToScreen(New Point(0, panel.Host.Height)), entries,
                             chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font)
    End Sub

    Private Sub TrackChromeGeometry()
        If chrome_Tracking Then Return
        chrome_Tracking = True
        AddHandler Me.LocationChanged, Sub() PlaceChromePanels()
        AddHandler Me.SizeChanged, Sub() PlaceChromePanels()
        AddHandler Me.Deactivate, Sub() ChromeLeaveKeyboardMode()
        AddHandler Me.DpiChanged, Sub() ChromeHandleDpiChanged()
        AddHandler panel_Media.SizeChanged, Sub()
                                                ChromeFollowAdaptiveScale()
                                                PlaceChromePanels()
                                            End Sub
    End Sub

    Private Sub TearDownChrome()
        chrome_Timer.Stop()
        ChromeLeaveKeyboardMode()
        For Each p As ChromePanel In chrome_Panels
            Try
                p.Host.Close()
                p.Host.Dispose()
            Catch
            End Try
        Next
        chrome_Panels.Clear()
        chrome_Path = Nothing
        RemoveHandler chrome_Timer.Tick, AddressOf Chrome_Timer_Tick
        RemoveHandler ChromeMenu.FocusKeyConsumed, AddressOf ChromePopupFocusKeyConsumed
        ChromeMenu.DismissPopup()
        DisposeChromeFonts()
    End Sub

    ''' <summary>Final teardown, called from Form1_FormClosing next to the recipients overlay's.</summary>
    Private Sub DisposeChromeResources()
        Try
            TearDownChrome()
            chrome_Timer.Dispose()
            If video_Chrome_Host IsNot Nothing Then video_Chrome_Host.Dispose() : video_Chrome_Host = Nothing
        Catch
        End Try
    End Sub

    Private Sub DisposeChromeFonts()
        If chrome_Text_Font IsNot Nothing Then chrome_Text_Font.Dispose() : chrome_Text_Font = Nothing
        If chrome_Symbol_Font IsNot Nothing Then chrome_Symbol_Font.Dispose() : chrome_Symbol_Font = Nothing
    End Sub

    ' --- theme, scale, fonts ---------------------------------------------------------------------

    Private Function ChromeCorner() As Integer
        Return If(modern_Preferences Is Nothing, ChromeMetrics.PanelCorner, modern_Preferences.ChromeCornerRadius)
    End Function

    ''' <summary>The monitor scale (or the explicit interface percent), times the panel-size choice:
    ''' small / medium / large are fixed multipliers, adaptive follows the media width in logical px.</summary>
    Private Function ChromeScale() As Single
        Dim percent As Integer = If(modern_Preferences Is Nothing, 0, modern_Preferences.InterfaceScalePercent)
        Dim factor As Single = UiScale.Factor(percent, Me.DeviceDpi)
        If modern_Preferences Is Nothing Then Return factor
        Dim logicalWidth As Single = 0.0F
        If panel_Media IsNot Nothing Then logicalWidth = panel_Media.ClientSize.Width / (Math.Max(1, Me.DeviceDpi) / CSng(UiScale.BaselineDpi))
        Return factor * UiScale.SizeMultiplier(modern_Preferences.ChromeSize, logicalWidth)
    End Function

    ''' <summary>Adaptive size follows the window: re-measure the panels when the media width has moved
    ''' the scale a step (the step keeps a drag of the window edge from re-measuring on every pixel).</summary>
    Private Sub ChromeFollowAdaptiveScale()
        If chrome_Panels.Count = 0 OrElse modern_Preferences Is Nothing Then Return
        If modern_Preferences.ChromeSize <> "adaptive" Then Return
        If ChromeScale() <> chrome_Applied_Scale Then ChromeConfigure()
    End Sub

    ''' <summary>
    ''' Re-measures chrome and reanchors media hosts after a monitor DPI change.
    ''' </summary>
    Private Sub ChromeHandleDpiChanged()
        ' Reconfigure all chrome panels with the new scale
        If chrome_Panels.Count > 0 Then ChromeConfigure()
        ApplyRecipientsOverlay()
        ConfigureFloatingMediaChrome()
        SyncMediaSurface()
        PlaceChromePanels()
    End Sub

    ''' <summary>Hands every panel the current theme, scale, direction and fonts. They only ever change
    ''' together (a language, a colour scheme, a scale preference, high contrast), so there is one call.</summary>
    Private Sub ChromeConfigure()
        If chrome_Panels.Count = 0 Then Return

        chrome_High_Contrast = SystemInformation.HighContrast
        chrome_Theme = ChromeTheme.FromScheme(chrome_Scheme_Back, chrome_Scheme_Fore, chrome_High_Contrast)

        If Not chrome_High_Contrast AndAlso modern_Preferences IsNot Nothing Then chrome_Theme.PanelOpacity = modern_Preferences.ChromeOpacityPercent / 100.0R
        chrome_Theme.PanelCornerRadius = ChromeCorner()
        Dim factor As Single = ChromeScale()
        chrome_Applied_Scale = factor
        Dim previousScale As Single = chrome_Panels(0).Host.Surface.ScaleFactor
        Dim previousFamily As String = If(chrome_Text_Font Is Nothing, "", chrome_Text_Font.FontFamily.Name)

        Dim family As String = Localization.FontFamily()
        If String.IsNullOrEmpty(family) Then family = "Segoe UI"
        Dim targetTextSize As Integer = UiScale.Px(ChromeMetrics.TextSize, factor)
        Dim targetSymbolSize As Integer = UiScale.Px(ChromeMetrics.SymbolSize, factor)

        Dim textFontChanged As Boolean = (chrome_Text_Font Is Nothing) OrElse
            (chrome_Text_Font.FontFamily.Name <> family) OrElse
            (CInt(Math.Round(chrome_Text_Font.Size)) <> targetTextSize)

        Dim symbolFontChanged As Boolean = (chrome_Symbol_Font Is Nothing) OrElse
            (CInt(Math.Round(chrome_Symbol_Font.Size)) <> targetSymbolSize)

        Dim previousTextFont As Font = Nothing
        Dim previousSymbolFont As Font = Nothing

        If textFontChanged Then
            previousTextFont = chrome_Text_Font
            chrome_Text_Font = New Font(family, targetTextSize, FontStyle.Regular, GraphicsUnit.Pixel)
        End If

        If symbolFontChanged Then
            previousSymbolFont = chrome_Symbol_Font
            chrome_Symbol_Font = New Font("Segoe UI Symbol", targetSymbolSize, FontStyle.Regular, GraphicsUnit.Pixel)
        End If

        If chrome_Path IsNot Nothing Then chrome_Path.Configure(chrome_Theme, factor, Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font)

        For Each p As ChromePanel In chrome_Panels
            p.Host.BackColor = chrome_Theme.PanelFill
            ' The panel's fill is as translucent as the user chose; its glyphs and captions are not.
            p.Host.UseInkLayer = Not chrome_High_Contrast AndAlso chrome_Theme.PanelOpacity < 0.999R
            p.Host.SetCornerRadius(UiScale.Px(ChromeCorner(), factor))
            p.Host.Surface.Configure(chrome_Theme, factor, Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font)
            p.Host.Surface.SetAnimationEnabled(Not ReduceMotionActive())
            p.AppliedOpacity = -1.0R
            SyncChromePin(p)
            For Each pair In p.Legacy
                SyncChromeItem(pair.Key, pair.Value)
            Next
            p.Host.Surface.LayoutItems()
        Next
        ConfigureFloatingMediaChrome()
        If recipients_Overlay IsNot Nothing AndAlso (previousScale <> factor OrElse previousFamily <> chrome_Text_Font.FontFamily.Name) Then ApplyRecipientsOverlay()
        PlaceChromePanels()
        If previousTextFont IsNot Nothing Then previousTextFont.Dispose()
        If previousSymbolFont IsNot Nothing Then previousSymbolFont.Dispose()
    End Sub

    ''' <summary>The colour scheme changed (RecolorChrome calls this): the panels follow the picture the
    ''' way the docked toolbar does.</summary>
    Private Sub ChromeApplyScheme(back_Color As Color, opposite_Color As Color)
        chrome_Scheme_Back = back_Color
        chrome_Scheme_Fore = opposite_Color
        ' Called from RecolorChrome on every image: a failure here must not stop the picture appearing.
        Try
            If chrome_Panels.Count > 0 Then ChromeConfigure()
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: applying the colour scheme", ex)
        End Try
    End Sub

    ''' <summary>A language was switched (LngCh): captions, tooltips, accessible names, the script font
    ''' and the direction are all re-read.</summary>
    Private Function CreateRecipientChromeFont() As Font
        Dim family As String = Localization.FontFamily()
        If String.IsNullOrEmpty(family) Then family = "Segoe UI"
        Dim logical As Integer = CInt(Math.Round(RecipientsOverlayFontSize() * 96.0R / 72.0R))
        Return New Font(family, UiScale.Px(logical, ChromeScale()), FontStyle.Regular, GraphicsUnit.Pixel)
    End Function

    Private Sub ConfigureFloatingMediaChrome()
        If video_Chrome_Host IsNot Nothing Then
            Dim factor As Single = ChromeScale()
            video_Controls.Height = UiScale.Px(ChromeMetrics.TransportHeight, factor)
            btn_Video_Play.Size = New Size(UiScale.Px(ChromeMetrics.TransportButtonWidth, factor), video_Controls.Height)
            btn_Video_Mute.Size = btn_Video_Play.Size
            video_Seek.Height = UiScale.Px(ChromeMetrics.TransportSliderHeight, factor)
            video_Volume.Size = New Size(UiScale.Px(ChromeMetrics.TransportVolumeWidth, factor), video_Seek.Height)
            lbl_Video_Time.Size = New Size(UiScale.Px(ChromeMetrics.TransportTimeWidth, factor), video_Controls.Height)
            If Not Object.ReferenceEquals(lbl_Video_Time.Font, chrome_Text_Font) Then
                lbl_Video_Time.Font = Nothing
                lbl_Video_Time.Font = chrome_Text_Font
            End If
            video_Seek.FillColor = chrome_Theme.Accent
            video_Volume.FillColor = chrome_Theme.Accent
            video_Chrome_Host.BackColor = chrome_Theme.PanelFill
            ' The transport is made of real controls, which have no ink layer to keep them legible: its
            ' window never goes below the old 40 % floor, even though the other panels may reach 0.
            video_Chrome_Host.Opacity = Math.Max(0.4R, chrome_Theme.PanelOpacity)
            video_Chrome_Host.SetCornerRadius(UiScale.Px(ChromeCorner(), ChromeScale()))
            For Each control As Control In video_Controls.Controls
                control.BackColor = chrome_Theme.PanelFill
                control.ForeColor = chrome_Theme.Foreground
                Dim button As Button = TryCast(control, Button)
                If button IsNot Nothing Then
                    button.FlatAppearance.MouseOverBackColor = chrome_Theme.ItemHover
                    button.FlatAppearance.MouseDownBackColor = chrome_Theme.ItemPressed
                    Dim glyph As String = TryCast(button.Tag, String)
                    If Not String.IsNullOrEmpty(glyph) Then
                        button.Tag = Nothing
                        SetVideoGlyph(button, glyph)
                    End If
                End If
            Next
            video_Controls.BackColor = chrome_Theme.PanelFill
            PositionVideoControls()
        End If
        If recipients_Overlay IsNot Nothing Then
            recipients_Overlay.BackColor = chrome_Theme.PanelFill
            recipients_Overlay.SetCornerRadius(UiScale.Px(ChromeCorner(), ChromeScale()))
            If chrome_High_Contrast Then
                recipients_Overlay.Opacity = 1.0R
            Else
                recipients_Overlay.Opacity = Math.Max(0.15R, Math.Min(1.0R, RecipientsOverlayAlpha() / 255.0R))
            End If
            ApplyRecipientsOverlayHealth()
        End If
    End Sub

    Private Sub ChromeRefreshText()
        If chrome_Panels.Count > 0 Then ChromeConfigure()
    End Sub

    ' --- state of the items ----------------------------------------------------------------------

    ''' <summary>Copies what the Designer control currently says into its item. True when anything changed
    ''' (so the tick repaints only then).</summary>
    Private Function SyncChromeItem(item As ChromeItem, carrier As Control) As Boolean
        If carrier Is Nothing Then Return False
        Dim previousGlyph As String = item.GlyphId
        If carrier Is btn_Full_Screen Then item.GlyphId = If(is_Full_Screen_Mode, "media.exit-fullscreen", "media.fullscreen")
        Dim caption As String = item.Caption
        Dim visible As Boolean = True
        If TypeOf carrier Is Label Then
            caption = If(carrier Is lbl_Info, "i", carrier.Text)
            If carrier Is lbl_Zoom Then visible = caption.Length > 0
            If carrier Is lbl_Slideshow_Time Then visible = Is_slide_show_mode AndAlso caption.Length > 0
        ElseIf carrier Is cmbox_Sort Then
            caption = cmbox_Sort.Text
            item.GlyphId = ""
        ElseIf carrier Is btn_choose_file Then
            caption = Localization.T("Выбрать файл..")
        ElseIf carrier Is btn_Select_Folder Then
            caption = Localization.T("Выбрать папку..")
        ElseIf carrier Is btn_Prev_File OrElse carrier Is btn_Next_File OrElse carrier Is bt_Delete Then
            caption = Localization.T(IconMap.GetMeaning(carrier.Name).LocalizationKey)
        ElseIf TypeOf carrier Is Button AndAlso item.GlyphId.Length = 0 AndAlso carrier IsNot btn_Random_Slideshow Then
            caption = carrier.Text
        ElseIf carrier Is btn_Random_Slideshow Then
            caption = "⤮▶"
            item.CaptionIsSymbol = True
        End If
        If carrier Is btn_Translate OrElse carrier Is btn_TranslateBrowser OrElse carrier Is btn_Edit OrElse
           carrier Is btn_Tracks OrElse carrier Is btn_ToVideo Then visible = Not toolbar_Hidden_Items.Contains(carrier)
        Dim checked As Boolean = If(carrier Is chkbox_Top_Most, chkbox_Top_Most.Checked,
                                    carrier Is btn_Slideshow AndAlso Is_slide_show_mode)
        Dim tip As String = If(toolTip Is Nothing, "", toolTip.GetToolTip(carrier))
        Dim name As String = If(TypeOf carrier Is Button, OverflowText(DirectCast(carrier, Button)), tip)
        If name.Length = 0 Then name = carrier.Text
        Dim meaning As IconMeaning = IconMap.GetMeaning(carrier.Name)
        If name.Length = 0 AndAlso meaning IsNot Nothing Then name = Localization.T(meaning.LocalizationKey)
        tip = If(tip.Length = 0, If(name.Length > 0, name, caption), tip)
        If carrier Is lbl_Status Then tip = carrier.Text
        If carrier Is lbl_Info Then tip = Localization.T("О программе")
        Dim changed As Boolean = previousGlyph <> item.GlyphId OrElse item.Caption <> caption OrElse item.Visible <> visible OrElse
            item.Checked <> checked OrElse item.Enabled <> carrier.Enabled OrElse item.Tooltip <> tip OrElse item.AccessibleName <> name
        item.Caption = caption
        item.Visible = visible
        item.Enabled = carrier.Enabled
        item.Checked = checked
        item.Tooltip = If(tip.Length = 0, caption, tip)
        item.AccessibleName = name
        If TypeOf carrier Is Button Then
            Dim button As Button = DirectCast(carrier, Button)
            If item.GlyphId.Length = 0 Then item.Image = button.Image
            If IconMap.TextCaptionToolbarControls.Contains(carrier.Name) Then
                item.GlyphId = ""
                item.Caption = carrier.Text
            End If
        End If
        If carrier Is cmbox_Media_Folder AndAlso chrome_Path IsNot Nothing Then
            chrome_Path.AccessibleName = tip
            chrome_Path.SetTooltip(tip)
        End If
        If changed AndAlso (carrier Is lbl_File_Number OrElse carrier Is lbl_Zoom OrElse carrier Is lbl_Status) Then
            For Each panel In chrome_Panels
                panel.Wanted = True
                panel.LastActivity = chrome_Clock.ElapsedMilliseconds
            Next
        End If
        Return changed
    End Function

    Private Sub Chrome_ItemInvoked(sender As Object, item As ChromeItem)
        If item.Id <> "cmbox_Media_Folder" AndAlso chrome_Path IsNot Nothing AndAlso chrome_Path.ContainsFocus Then chrome_Path.EndEditing(True)
        For Each panel In chrome_Panels
            For Each pair In panel.Legacy
                SyncChromeItem(pair.Key, pair.Value)
            Next
            panel.Host.Surface.LayoutItems()
            panel.LastActivity = chrome_Clock.ElapsedMilliseconds
        Next
        KeepRecipientsOverlayOnTop()
    End Sub

    ''' <summary>Where a panel goes, in screen coordinates. Different panels have different placement.</summary>
    Private Function ChromePanelRect(p As ChromePanel) As Rectangle
        Dim factor As Single = p.Host.Surface.ScaleFactor
        Dim media As Rectangle = panel_Media.RectangleToScreen(panel_Media.ClientRectangle)
        media = Rectangle.Intersect(media, Me.RectangleToScreen(Me.ClientRectangle))
        Dim margin As Integer = UiScale.Px(ChromeMetrics.ScreenMargin, factor)
        Dim inset As Integer = UiScale.Px(ChromeMetrics.PanelEdgeInset, factor)

        Dim size As Size = p.Host.Surface.PreferredPanelSize()
        Dim maxWidth As Integer = Math.Max(UiScale.Px(ChromeMetrics.MinTarget, factor), media.Width - 2 * inset)
        If size.Width > maxWidth Then size.Width = maxWidth
        If p.Name = "chrome_Status" Then size.Width = maxWidth

        ' Flush against the start edge of the window and stacked from its top, like the docked toolbar
        ' they replace: row 1, row 2 under it, the status line at the bottom. A right-to-left language
        ' starts at the right.
        Dim top As Integer = media.Top + inset
        If p.Name = "chrome_Status" Then
            If video_Controls IsNot Nothing AndAlso video_Controls.Visible Then top = media.Bottom - video_Controls.Height - size.Height - margin * 2 Else top = media.Bottom - size.Height - inset
        ElseIf p.Name = "chrome_Row2" AndAlso chrome_Panels.Count > 0 Then
            top += chrome_Panels(0).Host.Surface.PreferredPanelSize().Height + UiScale.Px(ChromeMetrics.Gap, factor)
        End If
        Dim left As Integer = If(Localization.IsRightToLeft(), media.Right - inset - size.Width, media.Left + inset)
        Return New Rectangle(left, top, size.Width, size.Height)
    End Function

    Private Sub PlaceChromePanels()
        If panel_Media Is Nothing OrElse Not Me.IsHandleCreated Then Return
        PositionVideoControls()
        For Each p As ChromePanel In chrome_Panels
            If Not p.Host.Visible Then Continue For
            Dim r As Rectangle = ChromePanelRect(p)
            If p.Host.Bounds <> r Then p.Host.Bounds = r
        Next
    End Sub

    ''' <summary>The pointer reveals a panel from a band across the appropriate edge.</summary>
    Private Function ChromeHotZone(p As ChromePanel) As Rectangle
        Dim factor As Single = p.Host.Surface.ScaleFactor
        Dim media As Rectangle = panel_Media.RectangleToScreen(panel_Media.ClientRectangle)
        Dim r As Rectangle = ChromePanelRect(p)
        Dim zoneHeight As Integer = UiScale.Px(ChromeMetrics.HotZoneMargin, factor)

        Select Case p.Name
            Case "chrome_Status"
                ' Status bar revealed from bottom
                Dim top As Integer = r.Top - zoneHeight
                Return New Rectangle(media.Left, Math.Max(media.Top, top), media.Width, Math.Max(0, r.Bottom - top))
            Case "chrome_Window", "chrome_Path", "chrome_Nav"
                ' Top panels revealed from top
                Dim bottom As Integer = r.Bottom + zoneHeight
                Return New Rectangle(media.Left, media.Top, media.Width, Math.Max(0, bottom - media.Top))
            Case Else
                ' Default: top zone
                Dim bottom As Integer = r.Bottom + zoneHeight
                Return New Rectangle(media.Left, media.Top, media.Width, Math.Max(0, bottom - media.Top))
        End Select
    End Function

    ''' <summary>False while the media surface is not on screen or the user's own setting hides the
    ''' chrome: minimized, hidden, super-full-screen (no chrome at all), or a slideshow that hides it.</summary>
    Private Function ChromeUsable() As Boolean
        If Not Me.Visible OrElse Me.WindowState = FormWindowState.Minimized Then Return False
        If panel_Media Is Nothing OrElse Not panel_Media.Visible OrElse panel_Media.ClientSize.Width <= 0 Then Return False
        If is_Super_Full_Screen_Mode Then Return False
        Return True
    End Function

    ' --- the shared tick: summon, fade, hide -------------------------------------------------------

    Private Sub Chrome_Timer_Tick(sender As Object, e As EventArgs) 
        If chrome_Panels.Count = 0 Then
            chrome_Timer.Stop()
            Return
        End If

        Try
            Dim now As Long = chrome_Clock.ElapsedMilliseconds
            Dim elapsed As Integer = CInt(Math.Min(250L, Math.Max(0L, now - chrome_Last_Tick)))
            chrome_Last_Tick = now

            ' High contrast can be switched while the app runs: re-derive the theme when it flips.
            If SystemInformation.HighContrast <> chrome_High_Contrast Then ChromeConfigure()

            Dim pointer As Point = Cursor.Position
            Dim moved As Boolean = pointer <> chrome_Last_Pointer
            chrome_Last_Pointer = pointer
            Dim usable As Boolean = ChromeUsable()
            Dim delayMs As Long = If(modern_Preferences Is Nothing, 3000L, modern_Preferences.VideoControlsHideDelaySec * 1000L)
            Dim active As Boolean = Me.ContainsFocus OrElse (Form.ActiveForm Is Me) OrElse (chrome_Path IsNot Nothing AndAlso chrome_Path.ContainsFocus)
            Dim animating As Boolean = False

            For Each p As ChromePanel In chrome_Panels
                For Each pair In p.Legacy
                    If SyncChromeItem(pair.Key, pair.Value) Then p.Host.Surface.LayoutItems()
                Next

                If Not usable OrElse (p.Name = "chrome_Status" AndAlso SlideshowHidesStatus()) OrElse
                   (p.Name <> "chrome_Status" AndAlso SlideshowHidesToolbar()) Then
                    p.Wanted = False
                    p.Alpha = 0.0R
                    p.TargetAlpha = 0.0R
                Else
                    Dim rect As Rectangle = If(p.Host.Visible, p.Host.Bounds, ChromePanelRect(p))
                    Dim over As Boolean = rect.Contains(pointer)

                    If ChromeMenu.PopupVisible OrElse (chrome_Path IsNot Nothing AndAlso chrome_Path.ContainsFocus) OrElse chrome_Keyboard_Mode OrElse over OrElse (ChromePanelPinned(p) AndAlso Me.Visible) Then
                        ' Never while the pointer is over it, never in focus mode, never when pinned: a panel
                        ' sliding out from under the reaching hand is worse than one that never appeared.
                        p.Wanted = True
                        p.LastActivity = now
                    ElseIf active AndAlso moved AndAlso Control.MouseButtons = MouseButtons.None AndAlso
                           ChromeHotZone(p).Contains(pointer) Then
                        p.Wanted = True
                        p.LastActivity = now
                    ElseIf p.Wanted AndAlso (now - p.LastActivity) >= delayMs Then
                        p.Wanted = False
                    End If
                    ' Unpinned and the window not in use: let it go.
                    If Not active AndAlso Not ChromePanelPinned(p) AndAlso Not over Then p.Wanted = False

                    Dim target As Double = If(p.Wanted, 1.0R, 0.0R)
                    Dim fadeElapsed As Integer = elapsed
                    If target <> p.TargetAlpha Then
                        p.TargetAlpha = target
                        fadeElapsed = 0 ' The idle poll precedes this transition; it is not part of the fade.
                    End If
                    If Not ChromeFadeAllowed() Then
                        p.Alpha = target
                    ElseIf p.Alpha < target Then
                        p.Alpha = Math.Min(target, p.Alpha + fadeElapsed / CDbl(ChromeMetrics.FadeMilliseconds))
                    ElseIf p.Alpha > target Then
                        p.Alpha = Math.Max(target, p.Alpha - fadeElapsed / CDbl(ChromeMetrics.FadeMilliseconds))
                    End If
                    If p.Alpha <> target Then animating = True
                End If

                ApplyChromeVisibility(p)

                If p.Host.Visible AndAlso p.Host.Surface.NeedsAnimation Then
                    p.Host.Surface.Animate(elapsed)
                    animating = True
                End If
            Next

            If chrome_Path IsNot Nothing AndAlso Not chrome_Path.ContainsFocus Then
                chrome_Path.Text = cmbox_Media_Folder.Text
                chrome_Path.RecentFolders = cmbox_Media_Folder.Items.Cast(Of Object)().Select(Function(entry) entry.ToString()).ToArray()
            End If
            Dim interval As Integer = If(animating, ChromeMetrics.AnimationTickMilliseconds, ChromeMetrics.IdleTickMilliseconds)
            If chrome_Timer.Interval <> interval Then chrome_Timer.Interval = interval
        Catch ex As Exception
            ' A tick must never raise: this runs ten times a second for the life of the window.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2601: chrome tick failed: " & ex.Message)
        End Try
    End Sub

    ''' <summary>The fade runs only when neither the user (Reduce motion, or Windows' animation switch)
    ''' nor high contrast has asked for stillness; the states still change, just without the in-between.</summary>
    Private Function ChromeFadeAllowed() As Boolean
        Return chrome_Theme.AllowsAnimation AndAlso Not ReduceMotionActive()
    End Function

    ''' <summary>Shows or hides a panel's window for its current fade. At zero it is hidden outright: a
    ''' layered window at alpha 0 still takes the clicks, so it must not merely be transparent.</summary>
    Private Sub ApplyChromeVisibility(p As ChromePanel)
        If p.Alpha <= 0.0R Then
            If p.Host.Visible Then p.Host.Visible = False
            p.AppliedOpacity = -1.0R
            Return
        End If

        If Not p.Host.Visible Then
            Dim r As Rectangle = ChromePanelRect(p)
            p.Host.Bounds = r
            PinToViewerBand(p.Host)
            p.Host.Opacity = 0.0R
            p.Host.InkOpacity = 0.0R
            p.Host.Visible = True
            p.AppliedOpacity = 0.0R
        Else
            Dim r As Rectangle = ChromePanelRect(p)
            If p.Host.Bounds <> r Then p.Host.Bounds = r
        End If

        Dim opacity As Double = Math.Max(0.02R, Math.Min(1.0R, p.Alpha * chrome_Theme.PanelOpacity))
        If Math.Abs(opacity - p.AppliedOpacity) > 0.004R Then
            p.Host.Opacity = opacity
            ' The ink layer follows only the panel's own fade (show / hide), never its translucency.
            p.Host.InkOpacity = Math.Max(0.02R, Math.Min(1.0R, p.Alpha))
            p.AppliedOpacity = opacity
        End If
    End Sub

    ' --- the keyboard: F10 enters, Tab/arrows walk, Enter/Space press, Esc leaves --------------------------

    ''' <summary>Called first by ProcessCmdKey. True = the key was the chrome's and goes no further.</summary>
    Private Function ChromeHandleKey(keyData As Keys) As Boolean
        If ChromeMenu.HandlePopupKey(keyData) Then Return True
        If chrome_Panels.Count = 0 OrElse Not ChromeUsable() OrElse SlideshowHidesToolbar() Then Return False

        Dim code As Keys = keyData And Keys.KeyCode
        Dim modifiers As Keys = keyData And Keys.Modifiers

        If Not chrome_Keyboard_Mode Then
            If keyData = Keys.F10 Then
                chrome_F10_Pending = True
                ChromeEnterKeyboardMode()
                Return True
            End If
            Return False
        End If

        ' A chord with Ctrl or Alt is Windows' or the viewer's, never the chrome's (Alt+F4 must still close).
        If (modifiers And (Keys.Control Or Keys.Alt)) <> Keys.None Then Return False

        Dim rtl As Boolean = Localization.IsRightToLeft()
        If keyData = (Keys.Shift Or Keys.F10) OrElse code = Keys.Apps Then
            chrome_Panels(chrome_Focus_Panel).Host.Surface.ActivateFocusedSecondary()
            Return True
        End If
        Select Case code
            Case Keys.Escape, Keys.F10
                If code = Keys.F10 Then chrome_F10_Pending = True
                ChromeLeaveKeyboardMode()
            Case Keys.Tab
                ChromeStepFocus(forward:=((modifiers And Keys.Shift) = Keys.None))
            Case Keys.Right, Keys.Down
                ChromeStepFocus(forward:=Not (rtl AndAlso code = Keys.Right))
            Case Keys.Left, Keys.Up
                ChromeStepFocus(forward:=(rtl AndAlso code = Keys.Left))
            Case Keys.Home
                ChromeJumpFocus(last:=False)
            Case Keys.End
                ChromeJumpFocus(last:=True)
            Case Keys.Enter, Keys.Space
                If chrome_Focus_Panel >= 0 AndAlso chrome_Focus_Panel < chrome_Panels.Count Then
                    chrome_Panels(chrome_Focus_Panel).Host.Surface.ActivateFocused()
                End If
            Case Keys.ShiftKey, Keys.ControlKey, Keys.Menu
                Return False
            Case Else
                ' While the ring is up the viewer is inert: "3" must not move a file the user is only
                ' trying to reach a button past. Esc hands every key back.
        End Select
        Return True
    End Function

    ''' <summary>The key-up of an F10 the chrome took (see chrome_F10_Pending). True = swallow it.</summary>
    Private Sub ChromePopupFocusKeyConsumed()
        chrome_F10_Pending = True
    End Sub

    Private Function ChromeSwallowKeyUp(keyCode As Keys) As Boolean
        If keyCode = Keys.F10 AndAlso chrome_F10_Pending Then
            chrome_F10_Pending = False
            Return True
        End If
        Return False
    End Function

    Private Sub ChromeEnterKeyboardMode()
        If chrome_Panels.Count = 0 Then Return
        chrome_Keyboard_Mode = True
        chrome_Focus_Panel = 0
        If Not chrome_Panels(0).Host.Surface.EnterKeyboardMode() Then
            ChromeLeaveKeyboardMode()
            Return
        End If
        Dim now As Long = chrome_Clock.ElapsedMilliseconds
        For Each p As ChromePanel In chrome_Panels
            p.Wanted = True
            p.LastActivity = now
        Next
        ' Up at once, not after the next idle tick.
        chrome_Timer.Interval = ChromeMetrics.AnimationTickMilliseconds
    End Sub

    Private Sub ChromeLeaveKeyboardMode()
        If Not chrome_Keyboard_Mode Then Return
        chrome_Keyboard_Mode = False
        For Each p As ChromePanel In chrome_Panels
            p.Host.Surface.LeaveKeyboardMode()
            ' Leaving starts the ordinary countdown from now.
            p.LastActivity = chrome_Clock.ElapsedMilliseconds
        Next
    End Sub

    Private Sub ChromeStepFocus(forward As Boolean)
        If chrome_Panels.Count = 0 Then Return
        Dim current As ChromePanel = chrome_Panels(chrome_Focus_Panel)
        If current.Host.Surface.MoveFocus(forward) Then Return

        ' Off the end of this panel: on to the next one (wrapping), starting at its near end.
        current.Host.Surface.LeaveKeyboardMode()
        chrome_Focus_Panel = (chrome_Focus_Panel + If(forward, 1, -1) + chrome_Panels.Count) Mod chrome_Panels.Count
        chrome_Panels(chrome_Focus_Panel).Host.Surface.EnterKeyboardMode(last:=Not forward)
    End Sub

    Private Sub ChromeJumpFocus(last As Boolean)
        If chrome_Panels.Count = 0 Then Return
        chrome_Panels(chrome_Focus_Panel).Host.Surface.LeaveKeyboardMode()
        chrome_Focus_Panel = If(last, chrome_Panels.Count - 1, 0)
        chrome_Panels(chrome_Focus_Panel).Host.Surface.EnterKeyboardMode(last)
    End Sub

End Class
#End If
