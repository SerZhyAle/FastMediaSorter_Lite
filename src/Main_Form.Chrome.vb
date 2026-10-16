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
    ''' <summary>The palette BEFORE the 220 tone flip. The panels' <see cref="chrome_Theme"/> is its
    ''' flipped view below half opacity; a surface with a transparency knob of its own (the
    ''' recipients overlay) reads its base ink here and decides its own flip.</summary>
    Private chrome_Base_Theme As ChromeTheme = chrome_Theme
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

    ''' <summary>The floating panel that carries the window commands the removed native caption
    ''' used to hold (188). Its window name is an explicit case in the pin algebra, never the fallback.</summary>
    Private Const Window_Controls_Panel_Name As String = "chrome_WindowControls"

    ''' <summary>The persisted pin switches as one value (see <see cref="ChromeAutoHide"/>). Without
    ''' a preferences object the window-controls panel is still pinned: it replaces the native
    ''' caption, and its default pin is True (188 §4).</summary>
    Private Function ChromePinSnapshot() As ChromeAutoHide.ChromePinState
        If modern_Preferences Is Nothing Then Return New ChromeAutoHide.ChromePinState With {.WindowControlsPinned = True}
        Return New ChromeAutoHide.ChromePinState With {
            .GlobalPinned = modern_Preferences.ChromePinned,
            .Row1Pinned = modern_Preferences.ChromeRow1Pinned,
            .Row2Pinned = modern_Preferences.ChromeRow2Pinned,
            .StatusPinned = modern_Preferences.ChromeStatusPinned,
            .OverlayPinned = modern_Preferences.RecipientsOverlayPinned,
            .WindowControlsPinned = modern_Preferences.ChromeWindowControlsPinned}
    End Function

    ''' <summary>Whether this panel stays up: pinned for all, or pinned on its own.</summary>
    Private Function ChromePanelPinned(p As ChromePanel) As Boolean
        Return ChromeAutoHide.PanelPinned(ChromePinSnapshot(), p.Name)
    End Function

    ''' <summary>The corner button's click. While "keep visible" is on for every panel, unpinning one
    ''' turns that all-switch into the three individual pins minus this one - otherwise the click would
    ''' do nothing visible.</summary>
    Private Sub ChromeTogglePin(p As ChromePanel)
        If modern_Preferences Is Nothing Then Return
        WriteChromePinState(ChromeAutoHide.TogglePin(ChromePinSnapshot(), p.Name))
        For Each other As ChromePanel In chrome_Panels
            SyncChromePin(other)
            other.Host.Surface.Invalidate()
        Next
        p.Wanted = True
        p.LastActivity = chrome_Clock.ElapsedMilliseconds
        SyncRecipientsOverlayPin()
        ' The global switch may have flipped (the split) - the overlay's own lifecycle follows it,
        ' so re-evaluate it now, not on the next tick.
        ChromeSyncRecipientsOverlayLifecycle()
        Try
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: persisting a panel pin", ex)
            MarkSettingsDirty()
        End Try
    End Sub

    ''' <summary>Writes the pin switches back to the preferences. Callers persist and re-sync.</summary>
    Private Sub WriteChromePinState(updated As ChromeAutoHide.ChromePinState)
        modern_Preferences.ChromePinned = updated.GlobalPinned
        modern_Preferences.ChromeRow1Pinned = updated.Row1Pinned
        modern_Preferences.ChromeRow2Pinned = updated.Row2Pinned
        modern_Preferences.ChromeStatusPinned = updated.StatusPinned
        modern_Preferences.RecipientsOverlayPinned = updated.OverlayPinned
        modern_Preferences.ChromeWindowControlsPinned = updated.WindowControlsPinned
    End Sub

    ''' <summary>The recipients overlay's own pin button (2026-10-04 revision of 117 Ф-2). The same
    ''' rule as a panel's: while the global switch is on, unpinning converts it into the panels'
    ''' three pins and the overlay's pin goes off, so the click does something visible.</summary>
    Private Sub ChromeToggleRecipientsPin()
        If modern_Preferences Is Nothing Then Return
        WriteChromePinState(ChromeAutoHide.TogglePin(ChromePinSnapshot(), Recipients_Overlay_Panel_Name))
        For Each other As ChromePanel In chrome_Panels
            SyncChromePin(other)
            other.Host.Surface.Invalidate()
        Next
        SyncRecipientsOverlayPin()
        ' Land in the phase the cycle is in right now: pinning shows the overlay at once, unpinning
        ' keeps it up while the pointer is over it and hands it to the countdown after that.
        ChromeSyncRecipientsOverlayLifecycle()
        Try
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: persisting the recipients pin", ex)
            MarkSettingsDirty()
        End Try
    End Sub

    ''' <summary>The button's state and words follow the preference (a language switch re-reads them).
    ''' Since the 2026-10-04 revision the overlay has a pin of its own, so the panel tooltips no
    ''' longer promise that the destination table follows the panels.</summary>
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
        ' The carrier must exist before the rows are built: a Nothing carrier is skipped by
        ' BuildChromeRow, which would drop the button for the whole session (ticket 118 13.3).
        EnsureResourceButton()
        EnsureImageMenuButton()
        If chrome_Panels.Count = 0 Then BuildChromePanels()
        ChromeConfigure()
        chrome_Last_Tick = chrome_Clock.ElapsedMilliseconds
        chrome_Timer.Start()
        RefreshResourceButton()
    End Sub

    Private Sub BuildChromePanels()
        AddHandler chrome_Timer.Tick, AddressOf Chrome_Timer_Tick
        AddHandler ChromeMenu.FocusKeyConsumed, AddressOf ChromePopupFocusKeyConsumed
        AddHandler ThemeService.Changed, AddressOf ChromeThemeServiceChanged
        ' The language button is not on the panel: the settings window is the one surface that
        ' offers the 13 languages on this build (120, AddUiLanguageRow) and the row needs the room.
        BuildChromeRow("chrome_Row1", New Control() {
            chkbox_Top_Most, cmbox_Sort, cmbox_Media_Folder, btn_choose_file, btn_Select_Folder, btn_Resource,
            btn_Review, btn_Panel, btn_Full_Screen, lbl_Slideshow_Time, lbl_Info, btn_Move_Table})
        ' The settings button lives on row 1, after "i": the row reads folder first, file
        ' second, window-level commands last. toolbar_Row2_Items itself stays as the shared
        ' net48 toolbar order, so only this build's copy drops the button.
        Dim row2 As New List(Of Control)(toolbar_Row2_Items)
        row2.RemoveAll(Function(carrier) carrier Is btn_Move_Table)
        If btn_ToVideo IsNot Nothing AndAlso Not row2.Contains(btn_ToVideo) Then row2.Add(btn_ToVideo)
        row2.Add(btn_Image_Menu)
        BuildChromeRow("chrome_Row2", row2)
        BuildChromeRow("chrome_Status", New Control() {lbl_Current_File, lbl_Status})
        BuildWindowControlsPanel()
        TrackChromeGeometry()
    End Sub

    ''' <summary>The floating panel of the window commands the removed native caption used to hold
    ''' (188): pin, Minimize, Maximize / Restore and Close, in physical order with Close rightmost.
    ''' It rides the shared chrome cycle like the other panels - its own persisted pin, its own local
    ''' reveal zone, the F10 ring reaches it after the status strip - but carries no legacy carrier:
    ''' the actions drive the window directly, and its drag grip runs the native move loop.</summary>
    Private Sub BuildWindowControlsPanel()
        Dim panel As New ChromePanel(Window_Controls_Panel_Name)
        chrome_Panels.Add(panel)
        panel.Host.Owner = Me
        AdmitLowerIntegrityMessages(panel.Host)

        Dim items As New List(Of ChromeItem)()
        ' The drag grip (2026-10-12 owner revision): one button-sized EMPTY slot right after the
        ' pin - no hover fill, no tooltip, no action; a press on it runs the native move loop,
        ' a double click toggles maximize. The three window commands keep the corner.
        Dim grip As New ChromeItem("win_Grip", ChromeItemKind.Text) With {
            .IsMoveGrip = True,
            .AccessibleName = Localization.T("Перетащить окно")}
        items.Add(grip)

        Dim minimize As New ChromeItem("win_Minimize", ChromeItemKind.Button) With {
            .CustomGlyph = ChromeCustomGlyph.WindowMinimize,
            .AccessibleName = Localization.T("Свернуть окно"),
            .Tooltip = Localization.T("Свернуть окно"),
            .Action = AddressOf ChromeMinimizeViewer}
        items.Add(minimize)

        Dim maximize As New ChromeItem("win_Maximize", ChromeItemKind.Button) With {
            .CustomGlyph = ChromeCustomGlyph.WindowMaximize,
            .Action = AddressOf ChromeToggleMaximized}
        items.Add(maximize)

        Dim closeItem As New ChromeItem("win_Close", ChromeItemKind.Button) With {
            .CustomGlyph = ChromeCustomGlyph.WindowClose,
            .AccessibleName = Localization.T("Закрыть окно"),
            .Action = AddressOf ChromeCloseViewer}
        items.Add(closeItem)

        panel.Host.Surface.PinAtStart = True
        panel.Host.Surface.ExtraPadding = ChromeMetrics.WindowControlsExtraPadding
        panel.Pin.Action = Sub() ChromeTogglePin(panel)
        SyncChromePin(panel)
        panel.Host.Surface.PinItem = panel.Pin
        panel.Host.Surface.SetItems(items)
        AddHandler panel.Host.Surface.ItemInvoked, AddressOf Chrome_ItemInvoked
        AddHandler panel.Host.Surface.MouseWheel, Sub(sender, args) MouseUse(args)
        ' Subscribed before the drag: the drag is a modal move loop and returns only when it ends.
        AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeTakeKeyboardForViewer()
        AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeBeginWindowDrag()
        AddHandler panel.Host.Surface.MoveGripDoubleClick, Sub(sender, args) ChromeToggleMaximized()
        ChromeSyncWindowControlsItems(panel)
        ' For the file-drop surfaces the panel floats over the picture like any other (124 UX-212).
        For Each target As Control In New Control() {panel.Host, panel.Host.Surface}
            target.AllowDrop = True
            AddHandler target.DragEnter, AddressOf Form1_DragEnter
            AddHandler target.DragDrop, AddressOf Form1_DragDrop
        Next
    End Sub

    ''' <summary>The Maximize / Restore button's state follows the window, not the button: glyph,
    ''' words, tooltip and the full-screen disable (188 §6) are re-read on every tick and after
    ''' every command, so a snap shortcut or the system menu update it within one idle poll.</summary>
    Private Sub ChromeSyncWindowControlsItems(p As ChromePanel)
        Dim maximized As Boolean = WindowState = FormWindowState.Maximized
        Dim fullscreen As Boolean = is_Full_Screen_Mode
        Dim maximizeItem As ChromeItem = Nothing
        For Each candidate As ChromeItem In p.Host.Surface.Items
            If candidate.Id = "win_Maximize" Then maximizeItem = candidate
        Next
        If maximizeItem Is Nothing Then Return
        Dim caption As String = If(maximized, Localization.T("Восстановить окно"), Localization.T("Развернуть окно"))
        Dim tooltip As String = If(fullscreen, Localization.T("Окно раскрыто на весь экран. Выйдите клавишей F7 или Esc."), caption)
        Dim glyph As ChromeCustomGlyph = If(maximized, ChromeCustomGlyph.WindowRestore, ChromeCustomGlyph.WindowMaximize)
        If maximizeItem.Enabled = Not fullscreen AndAlso maximizeItem.AccessibleName = caption AndAlso
           maximizeItem.Tooltip = tooltip AndAlso maximizeItem.CustomGlyph = glyph Then Return
        maximizeItem.Enabled = Not fullscreen
        maximizeItem.AccessibleName = caption
        maximizeItem.Tooltip = tooltip
        maximizeItem.CustomGlyph = glyph
        p.Host.Surface.LayoutItems()
        p.Host.Surface.Invalidate()
    End Sub

    Private Function ChromeCloseTooltipText() As String
        Return Localization.TF("Закрыть окно: {0}", ProductNames.WindowTitle)
    End Function

    Private Sub ChromeMinimizeViewer()
        Me.WindowState = FormWindowState.Minimized
    End Sub

    ''' <summary>The existing close lifecycle exactly once (188 §5): Close() runs FormClosing,
    ''' with its cancellation, persistence and cleanup - no direct termination, no new question.</summary>
    Private Sub ChromeCloseViewer()
        Me.Close()
    End Sub

    Private Sub BuildChromeRow(name As String, controls As IEnumerable(Of Control))
        Dim panel As New ChromePanel(name)
        chrome_Panels.Add(panel)
        panel.Host.Owner = Me
        ' A panel is a window of its own and a drop target only where it registers as one: a file dropped over
        ' a panel showed the no-drop cursor while the picture under it took it (124 UX-212). The host and the
        ' surface that fills it both take the same handlers as the picture.
        AdmitLowerIntegrityMessages(panel.Host)
        For Each target As Control In New Control() {panel.Host, panel.Host.Surface}
            target.AllowDrop = True
            AddHandler target.DragEnter, AddressOf Form1_DragEnter
            AddHandler target.DragDrop, AddressOf Form1_DragDrop
        Next
        Dim items As New List(Of ChromeItem)()
        For Each carrier As Control In controls
            If carrier Is Nothing Then Continue For
            Dim kind As ChromeItemKind = ChromeItemKind.Button
            If TypeOf carrier Is Label AndAlso carrier IsNot lbl_Info Then kind = ChromeItemKind.Text
            If carrier Is chkbox_Top_Most OrElse carrier Is btn_Slideshow Then kind = ChromeItemKind.Toggle
            If carrier Is cmbox_Sort OrElse carrier Is btn_RecentFiles OrElse carrier Is btn_Resource Then kind = ChromeItemKind.MenuButton
            Dim item As New ChromeItem(carrier.Name, kind) With {
                .Destructive = carrier Is bt_Delete OrElse carrier Is btn_ToVideo,
                .ShowCaptionWithGlyph = carrier Is btn_Prev_File OrElse carrier Is btn_Next_File OrElse carrier Is bt_Delete OrElse carrier Is btn_Tracks,
                .MirrorInRtl = carrier Is btn_Prev_File OrElse carrier Is btn_Next_File}
            Dim meaning As IconMeaning = IconMap.GetMeaning(carrier.Name)
            If meaning IsNot Nothing Then item.GlyphId = meaning.GlyphId
            If carrier Is btn_Move_Table Then item.GlyphId = "app.settings"
            ' The tracks command and the file-information command each had only a word or a bare letter on the panel; both
            ' now carry the catalogue's own picture (WINDOWS-UI 0.4 section 11 item 1).
            If carrier Is btn_Tracks Then item.GlyphId = "media.audio-track"
            If carrier Is lbl_Info Then item.GlyphId = "app.info"
            ' "On top" was a bare check box that meant something only in its tooltip; the catalogue has no glyph for
            ' it yet, so the surface paints one from lines (125 UX-311).
            If carrier Is chkbox_Top_Most Then item.CustomGlyph = ChromeCustomGlyph.OnTop
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
                If carrier Is lbl_Current_File Then
                    item.Kind = ChromeItemKind.Button
                    item.Action = Sub() CopyFilePathToClipboard()
                    item.AlignLeft = True
                Else
                    ' The status line is a readout. A click on it used to copy the FILE's path - a command its
                    ' tooltip never announced and its words had nothing to do with (125 UX-313c).
                    item.Kind = ChromeItemKind.Text
                End If
            End If
            If carrier Is cmbox_Sort Then item.Action = Sub() ChromeShowSort(item)
            If carrier Is btn_RecentFiles Then item.Action = Sub() ChromeShowRecentFiles(item)
            If carrier Is btn_Resource Then item.Action = Sub() ChromeShowResourceMenu(item)
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
                        ChromeReturnKeyboardToViewer(panel)
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
        ' A panel floats over the picture: the wheel over it still pages the files (or zooms), as it does over the
        ' picture beside it, instead of going nowhere (125 UX-313a). The middle click is NOT forwarded: it is a
        ' gesture of the media surface, and on a button it has no meaning to borrow.
        AddHandler panel.Host.Surface.MouseWheel, Sub(sender, args) MouseUse(args)
        AddHandler panel.Host.Surface.LayoutChanged, Sub() ChromePlacePath()
        AddHandler panel.Host.Surface.OverflowRequested, Sub(sender, args) ChromeShowOverflow(panel)
        ' Row 1 carries the window-level commands, so a press that misses every control drags the
        ' viewer, like the caption it stands in for (188 §5, 2026-10-12 owner revision). A double
        ' click on it toggles nothing: the row's own commands are the point here.
        ' Every row hands the keyboard to the viewer on a press that misses its controls; row 1 then also drags.
        AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeTakeKeyboardForViewer()
        If name = "chrome_Row1" Then
            AddHandler panel.Host.Surface.MoveGripPress, Sub(sender, args) ChromeBeginWindowDrag()
        End If
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

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function SetActiveWindow(hWnd As IntPtr) As IntPtr
    End Function

    <System.Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetActiveWindow() As IntPtr
    End Function

    ''' <summary>Gives the keyboard back to the viewer after the path box let go of it (125 UX-313i). The panel's
    ''' window was activated for the typing, and Windows hands an owner's activation to the owner's LAST ACTIVE
    ''' POPUP - the panel again - so the old code took the panel off the screen, activated the viewer and put the
    ''' panel back: the path panel blinked at every end of an edit. The viewer is activated directly instead
    ''' (<c>SetActiveWindow</c> makes the owner its own last active window and does not redirect), and the panel
    ''' never leaves the screen. Only when that did not land - the viewer is not the active window of this thread
    ''' afterwards - the earlier hide, activate, show sequence runs, which is exactly the old behaviour.</summary>
    Private Sub ChromeReturnKeyboardToViewer(panel As ChromePanel)
        Try
            If IsHandleCreated Then
                SetActiveWindow(Me.Handle)
                If GetActiveWindow() = Me.Handle Then Return
            End If
        Catch ex As Exception
            AppFileLogger.LogException("Chrome: returning the keyboard to the viewer", ex)
        End Try
        ' Windows redirects activation of an owner to its last active popup. Remove the editing host from
        ' that choice before returning the keyboard.
        panel.Host.Hide()
        Me.Activate()
        If panel.Alpha > 0 Then panel.Host.Show()
    End Sub

    ''' <summary>A press on the free background of a floating panel hands the keyboard to the viewer and does
    ''' nothing else: the panels never activate themselves (invariant 2), so after working in another window this
    ''' is the way to bring the viewer back WITHOUT a click on the picture, which would flip the file. The next
    ''' digit then moves the file that is on screen, whether or not the recipients list is visible.</summary>
    Private Sub ChromeTakeKeyboardForViewer()
        ChromeLeaveKeyboardMode()
        If Not IsHandleCreated OrElse Not Me.Enabled OrElse WindowState = FormWindowState.Minimized Then Return
        If Form.ActiveForm IsNot Me Then Me.Activate()
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
        ' The ring on the first row only when the keyboard (the F10 ring) opened it (125 UX-313j).
        ChromeMenu.ShowPopup(Me, location, entries, chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font,
                             focusFirstRow:=chrome_Keyboard_Mode)
    End Sub

    Private Sub ChromeShowSort(item As ChromeItem)
        Dim entries As New List(Of ChromeMenuItem)()
        For index As Integer = 0 To cmbox_Sort.Items.Count - 1
            Dim selected As Integer = index
            entries.Add(New ChromeMenuItem("sort_" & index.ToString()) With {
                .Caption = SortDisplayText(index), .Checked = index = cmbox_Sort.SelectedIndex,
                .Action = Sub() ChromeApplySort(selected)})
        Next
        ChromeShowMenu(item, entries)
    End Sub

    ''' <summary>The sort order in words with its direction - "Имя ↑", "Размер ↓", "Случайно" - in the UI language
    ''' (125 UX-304). The combo keeps its codes ("abc", ">size"): the stored index and the sort itself read them.</summary>
    Private Function SortDisplayText(index As Integer) As String
        Dim arrow As Char
        Dim key As String = ChromeMenuRules.SortOrderLabel(index, arrow)
        If key.Length = 0 Then Return If(index >= 0 AndAlso index < cmbox_Sort.Items.Count, cmbox_Sort.Items(index).ToString(), "")
        Dim words As String = Localization.T(key)
        Return If(arrow = ChrW(0), words, words & " " & arrow)
    End Function

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

    ''' <summary>The folder box's right-click menu. ONE list (125 UX-327): the same rows the native menu of
    ''' <c>Main_Form.FolderMenu.vb</c> holds - words, tooltips, which are visible - so the painted menu can no longer
    ''' lose "Транслировать эту папку..", the stream catalog or the tooltip of the file row. Only the resources row
    ''' is swapped for the shared builder's entries (ticket 118 Q118-7: the same ones the «Ресурс» button shows,
    ''' minus the never-opened resources the folder menu's recent rule keeps out, Q118-14).</summary>
    Private Sub ChromeShowFolderMenu(item As ChromeItem)
        ApplyFolderMenuVisibility()
        Dim entries As List(Of ChromeMenuItem) = BuildChromeMenuEntries(folder_Context_Menu.Items)
        Dim resourcesAt As Integer = entries.FindIndex(Function(entry) entry.Id = "folder_resources")
        If resourcesAt >= 0 Then
            entries.RemoveAt(resourcesAt)
            entries.InsertRange(resourcesAt, BuildResourceMenuEntries(includeNeverUsed:=False))
        End If
        ChromeShowMenu(item, entries)
    End Sub

    ''' <summary>The «Ресурс» button's popup: the shared resource entries, including resources that
    ''' have never been opened (Q118-14 - the button exists while the registry is non-empty, so a
    ''' freshly registered resource must be offered from it).</summary>
    Private Sub ChromeShowResourceMenu(item As ChromeItem)
        ChromeShowMenu(item, BuildResourceMenuEntries(includeNeverUsed:=True))
    End Sub

    Private Sub ChromeShowOverflow(panel As ChromePanel)
        Dim entries As New List(Of ChromeMenuItem)()
        For Each item In panel.Host.Surface.OverflowItems
            ' A counter or a zoom label does nothing when pressed; it has no row in a menu (125 UX-310).
            If Not ChromeMenuRules.OverflowEntryIsActionable(item.Kind) Then Continue For
            Dim source As ChromeItem = item
            entries.Add(New ChromeMenuItem(item.Id) With {.Caption = If(item.AccessibleName.Length > 0, item.AccessibleName, item.Caption),
                .Enabled = item.Enabled, .Checked = item.Checked, .Destructive = item.Destructive,
                .Action = Sub() source.Action?.Invoke()})
        Next
        ' Under the » button itself, like every other menu button - not at the panel's start edge.
        Dim button As ChromeItem = panel.Host.Surface.Items.FirstOrDefault(Function(candidate) candidate.Id = panel.Host.Surface.OverflowId)
        If button IsNot Nothing Then
            ChromeShowMenu(button, entries)
        Else
            ChromeMenu.ShowPopup(Me, panel.Host.PointToScreen(New Point(0, panel.Host.Height)), entries,
                                 chrome_Theme, ChromeScale(), Localization.IsRightToLeft(), chrome_Text_Font, chrome_Symbol_Font,
                                 focusFirstRow:=chrome_Keyboard_Mode)
        End If
    End Sub

    Private Sub TrackChromeGeometry()
        If chrome_Tracking Then Return
        chrome_Tracking = True
        AddHandler Me.LocationChanged, Sub()
                                          PlaceChromePanels()
                                          MarkSettingsDirty()
                                      End Sub
        AddHandler Me.SizeChanged, Sub()
                                      PlaceChromePanels()
                                      MarkSettingsDirty()
                                  End Sub
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
        Dim factor As Single = If(modern_Preferences Is Nothing, UiScale.Factor(0, Me.DeviceDpi), modern_Preferences.InterfaceFactor(Me.DeviceDpi))
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
        chrome_Base_Theme = ChromeTheme.FromScheme(chrome_Scheme_Back, chrome_Scheme_Fore, chrome_High_Contrast)
        If Not chrome_High_Contrast AndAlso modern_Preferences IsNot Nothing Then chrome_Base_Theme.PanelOpacity = modern_Preferences.ChromeOpacityPercent / 100.0R
        chrome_Base_Theme.PanelCornerRadius = ChromeCorner()
        chrome_Theme = chrome_Base_Theme
        If Not chrome_High_Contrast AndAlso modern_Preferences IsNot Nothing AndAlso
           ChromeMetrics.InvertsInk(modern_Preferences.ChromeOpacityPercent) Then
            ' 220: below half opacity the panels' ink flips tone - black becomes white, white becomes
            ' black, the outline with it - while the fills and the accent keep their hue. The base
            ' theme stays un-flipped: the recipients overlay reads its ink there and flips on its own knob.
            chrome_Theme = chrome_Base_Theme.WithInvertedInk()
        End If
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
            ' A panel's window has a name a screen reader can say (125 UX-313g); it was an untitled window.
            p.Host.Text = ChromePanelTitle(p.Name)
            p.Host.AccessibleName = p.Host.Text
            p.Host.SetCornerRadius(UiScale.Px(ChromeCorner(), factor))
            ' The window-controls panel stays physical: its commands hold one physical order with
            ' Close rightmost in every language (188 §3.1), and none of its captions is text.
            Dim rightToLeft As Boolean = Localization.IsRightToLeft() AndAlso p.Name <> Window_Controls_Panel_Name
            p.Host.Surface.Configure(chrome_Theme, factor, rightToLeft, chrome_Text_Font, chrome_Symbol_Font)
            p.Host.Surface.SetAnimationEnabled(Not ReduceMotionActive())
            p.AppliedOpacity = -1.0R
            SyncChromePin(p)
            If p.Name = Window_Controls_Panel_Name Then ChromeSyncWindowControlsItems(p)
            For Each pair In p.Legacy
                SyncChromeItem(pair.Key, pair.Value)
            Next
            p.Host.Surface.LayoutItems()
        Next
        ' The overlay's pin re-reads its words with the panels' (a language switch runs this too).
        SyncRecipientsOverlayPin()
        ConfigureFloatingMediaChrome()
        If recipients_Overlay IsNot Nothing AndAlso (previousScale <> factor OrElse previousFamily <> chrome_Text_Font.FontFamily.Name) Then ApplyRecipientsOverlay()
        PlaceChromePanels()
        If previousTextFont IsNot Nothing Then previousTextFont.Dispose()
        If previousSymbolFont IsNot Nothing Then previousSymbolFont.Dispose()
    End Sub

    Private Shared Function ChromePanelTitle(panelName As String) As String
        Select Case panelName
            Case "chrome_Row1" : Return Localization.T("Панель инструментов, строка 1")
            Case "chrome_Row2" : Return Localization.T("Панель инструментов, строка 2")
            Case Window_Controls_Panel_Name : Return Localization.T("Панель управления окном")
            Case Else : Return Localization.T("Строка состояния")
        End Select
    End Function

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
            ' Every part's size and place is TransportRules.ArrangeRow's (through LayoutVideoControlsRow, run by
            ' PositionVideoControls below) - the speed and the "more" buttons included (121 UX-406); only the bar's own
            ' height is set here, from the scale, and the measured widths are forgotten so they are taken again.
            video_Controls.Height = UiScale.Px(ChromeMetrics.TransportHeight, factor)
            video_Speed_Width = 0
            video_Clock_Width = 0
            For Each text_Part As Control In New Control() {lbl_Video_Time, lbl_Video_Ab, video_Seek, video_Volume}
                If Not Object.ReferenceEquals(text_Part.Font, chrome_Text_Font) Then
                    text_Part.Font = Nothing
                    text_Part.Font = chrome_Text_Font
                End If
            Next
            btn_Video_Speed.RowFont = chrome_Text_Font
            ' The sliders and the A-B chip paint from the theme: the scheme, high contrast and the opacity setting reach
            ' the bar through the same ChromeTheme as the panels (121 UX-403).
            For Each slider As FlatSlider In New FlatSlider() {video_Seek, video_Volume}
                slider.FillColor = chrome_Theme.Accent
                slider.TrackColor = ChromeTheme.Blend(chrome_Theme.PanelFill, chrome_Theme.Foreground, 0.28F)
                slider.ThumbColor = chrome_Theme.Foreground
            Next
            lbl_Video_Ab.ChipColor = chrome_Theme.Accent
            If video_Ring_Control IsNot Nothing Then SetBarRing(video_Ring_Control, True)
            video_Chrome_Host.BackColor = chrome_Theme.PanelFill
            ' The transport is made of real controls, which have no ink layer to keep them legible: its
            ' window never goes below the old 40 % floor, even though the other panels may reach 0.
            video_Chrome_Host.Opacity = Math.Max(0.4R, chrome_Theme.PanelOpacity)
            video_Chrome_Host.SetCornerRadius(UiScale.Px(ChromeCorner(), ChromeScale()))
            For Each control As Control In video_Controls.Controls
                control.BackColor = chrome_Theme.PanelFill
                control.ForeColor = If(control Is lbl_Video_Ab, chrome_Theme.AccentForeground, chrome_Theme.Foreground)
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
            Dim recTheme As ChromeTheme = RecipientsOverlayTheme()
            recipients_Overlay.BackColor = recTheme.PanelFill
            If recipients_Overlay_Strip IsNot Nothing Then recipients_Overlay_Strip.BackColor = recTheme.PanelFill
            If recipients_Overlay_Rows IsNot Nothing Then recipients_Overlay_Rows.BackColor = recTheme.PanelFill
            recipients_Overlay.SetCornerRadius(UiScale.Px(ChromeCorner(), ChromeScale()))
            ' The pin paints from the current theme, so a re-check (and the Invalidate that comes
            ' with it) is all its recolouring takes.
            SyncRecipientsOverlayPin()
            ' The overlay's opacity belongs to the chrome cycle (fade times the configured
            ' translucency, 117 Ф-2) and is applied by ApplyRecipientsOverlayVisibility; setting
            ' it here as well would give the two writers a fight.
            ApplyRecipientsOverlayVisibility()
            ApplyRecipientsOverlayHealth()
        End If
    End Sub

    Private Sub ChromeRefreshText()
        If chrome_Panels.Count > 0 Then ChromeConfigure()
    End Sub

    Private Sub ChromeThemeServiceChanged(sender As Object, e As EventArgs)
        If IsDisposed OrElse Disposing Then Return
        If chrome_Panels.Count > 0 Then ChromeConfigure()
        If Is_Show_Recipients_Overlay Then ApplyRecipientsOverlay()
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
            caption = If(carrier Is lbl_Info, "", If(carrier Is lbl_Status, StatusMessage.Text, carrier.Text))
            If carrier Is lbl_Zoom Then visible = caption.Length > 0
            If carrier Is lbl_Slideshow_Time Then visible = Is_slide_show_mode AndAlso caption.Length > 0
        ElseIf carrier Is cmbox_Sort Then
            caption = SortDisplayText(cmbox_Sort.SelectedIndex)
            item.GlyphId = ""
        ElseIf carrier Is btn_ToVideo Then
            ' The carrier's own text is a private-use code point of an icon font - a box in the panel's font.
            caption = Localization.T("Преобразовать в видео")
        ElseIf carrier Is btn_choose_file Then
            caption = Localization.T("Выбрать файл..")
        ElseIf carrier Is btn_Select_Folder Then
            caption = Localization.T("Выбрать папку..")
        ElseIf carrier Is btn_Image_Menu Then
            caption = Localization.T("Меню текущего файла")
            visible = Not is_Vlc_Playing AndAlso (is_PictureBox1_Visible OrElse is_PictureBox2_Visible) AndAlso
                      Not String.IsNullOrEmpty(Current_File_Name)
        ElseIf carrier Is btn_Prev_File OrElse carrier Is btn_Next_File OrElse carrier Is bt_Delete Then
            caption = Localization.T(IconMap.GetMeaning(carrier.Name).LocalizationKey)
        ElseIf carrier Is btn_Tracks Then
            caption = carrier.Text
        ElseIf TypeOf carrier Is Button AndAlso item.GlyphId.Length = 0 AndAlso carrier IsNot btn_Random_Slideshow Then
            caption = carrier.Text
        ElseIf carrier Is btn_Random_Slideshow Then
            caption = "⤮▶"
            item.CaptionIsSymbol = True
        End If
        If carrier Is btn_Translate OrElse carrier Is btn_TranslateBrowser OrElse carrier Is btn_Edit OrElse
           carrier Is btn_Tracks OrElse carrier Is btn_ToVideo OrElse carrier Is btn_Resource Then visible = Not toolbar_Hidden_Items.Contains(carrier)
        Dim checked As Boolean = If(carrier Is chkbox_Top_Most, chkbox_Top_Most.Checked,
                                    carrier Is btn_Slideshow AndAlso Is_slide_show_mode)
        Dim tip As String = If(toolTip Is Nothing, "", toolTip.GetToolTip(carrier))
        Dim name As String = If(TypeOf carrier Is Button, OverflowText(DirectCast(carrier, Button)), tip)
        If name.Length = 0 Then name = carrier.Text
        Dim meaning As IconMeaning = IconMap.GetMeaning(carrier.Name)
        If name.Length = 0 AndAlso meaning IsNot Nothing Then name = Localization.T(meaning.LocalizationKey)
        tip = If(tip.Length = 0, If(name.Length > 0, name, caption), tip)
        Dim tone As NotifyLevel = NotifyLevel.Info
        If carrier Is lbl_Status Then
            ' The words without the legacy "!" and the level that prefix stood for (124 UX-206): the panel writes the
            ' text in the level's colour, and a screen reader says the level in words instead of "exclamation".
            Dim status As NotifyMessage = StatusMessage
            tone = status.Level
            tip = status.Text
            If tone <> NotifyLevel.Info AndAlso status.Text.Length > 0 Then
                name = Localization.TF("{0}: {1}", Localization.T(If(tone = NotifyLevel.Error, "Ошибка", "Внимание")), status.Text)
            End If
        End If
        ' The name is cut with an ellipsis in the panel; its tooltip is where it shows in full.
        If carrier Is lbl_Current_File AndAlso carrier.Text.Length > 0 Then tip = carrier.Text & vbLf & tip
        If carrier Is lbl_Info Then tip = Localization.T("О файле")
        If carrier Is btn_Image_Menu Then
            name = Localization.T("Меню текущего файла")
            tip = name & " (Menu, Shift+F10)"
        End If
        ' The folder box has a menu on the right button that nothing on screen announced (125 UX-306).
        If carrier Is cmbox_Media_Folder Then tip &= vbLf & Localization.T("Правая кнопка - меню: папка, файл, архив, ресурсы, раздача")
        If carrier Is cmbox_Sort Then tip = Localization.TF("Порядок сортировки файлов: {0}", caption)
        If carrier Is btn_ToVideo Then name = Localization.T("Преобразовать в видео")
        ' A counter or a zoom value keeps its slot while its text changes length (125 UX-313b).
        If carrier Is lbl_File_Number Then item.ReservedText = Localization.TF("{0} из {1}", 9999, 9999)
        If carrier Is lbl_Zoom Then item.ReservedText = "1600 %"
        If carrier Is lbl_Slideshow_Time Then item.ReservedText = "99.9s"
        ' A button that comes and goes with the kind of the file keeps its place while it is away, so the ones after
        ' it do not jump when paging reaches a video (125 UX-313b).
        item.KeepsSlotWhenHidden = ChromeMenuRules.KeepsSlotWhenHidden(carrier.Name, DocHtmlTranslate.IsAvailableCached())
        Dim changed As Boolean = previousGlyph <> item.GlyphId OrElse item.Caption <> caption OrElse item.Visible <> visible OrElse
            item.Checked <> checked OrElse item.Enabled <> carrier.Enabled OrElse item.Tooltip <> tip OrElse item.AccessibleName <> name OrElse
            item.Tone <> tone
        item.Caption = caption
        item.Visible = visible
        item.Enabled = carrier.Enabled
        item.Checked = checked
        item.Tooltip = If(tip.Length = 0, caption, tip)
        item.AccessibleName = name
        item.Tone = tone
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
        ' While a file operation holds the status panel up (RevealStatusOnFileOperation) the hold owns its end:
        ' the text this operation is about to write must not start a second, five-second countdown behind it.
        If changed AndAlso (carrier Is lbl_File_Number OrElse carrier Is lbl_Zoom OrElse carrier Is lbl_Status) AndAlso
           Not FileOperationRevealHeld(chrome_Clock.ElapsedMilliseconds) Then
            ' Paging or wheel-zooming raised both toolbar rows over the picture for five seconds (and a slideshow
            ' under five seconds kept them up). Only the status panel is the readout of these three; the rows
            ' come from the pointer, a pin or F10 (125 UX-309).
            For Each panel In chrome_Panels
                If Not ChromeMenuRules.PanelRevealsOnTextChange(panel.Name) Then Continue For
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
            ' Fixed (121 UX-415): while a video or a track is open the strip stands above the bar's slot whether the bar
            ' is up or not. It used to drop by the bar's height every time the bar went away and rise when it came back.
            top = TransportRules.StatusTop(media.Bottom, size.Height, inset, margin, UiScale.Px(ChromeMetrics.TransportHeight, factor),
                                           is_Vlc_Playing AndAlso Not is_Super_Full_Screen_Mode)
        ElseIf p.Name = "chrome_Row2" AndAlso chrome_Panels.Count > 0 Then
            top += chrome_Panels(0).Host.Surface.PreferredPanelSize().Height + UiScale.Px(ChromeMetrics.Gap, factor)
        End If
        Dim left As Integer = If(Localization.IsRightToLeft(), media.Right - inset - size.Width, media.Left + inset)

        If p.Name = Window_Controls_Panel_Name Then
            ' The window commands sit in the PHYSICAL upper-right corner in every language (188 §3.2),
            ' unclamped: the three commands are always whole (188 §3.7), and the window's minimum
            ' tracking size guarantees them the room.
            left = media.Right - inset - size.Width
            Return New Rectangle(left, top, size.Width, size.Height)
        End If

        If p.Name = "chrome_Row1" OrElse p.Name = "chrome_Row2" Then
            ' The top rows share their band with the window-controls panel: capped to the width left
            ' of it, or wrapped locally below it when their priority core no longer fits (188 §3.5).
            Dim controls As ChromePanel = ChromeWindowControlsPanel()
            If controls IsNot Nothing Then
                Dim controlsSize As Size = controls.Host.Surface.PreferredPanelSize()
                Dim controlsMedia As New Rectangle(media.Right - inset - controlsSize.Width - media.Left, inset,
                                                   controlsSize.Width, controlsSize.Height)
                Dim placement As ChromeLayout.RowBandPlacement = ChromeLayout.PlaceRowBesideCornerPanel(
                    top - media.Top, size.Height, p.Host.Surface.MinimumRowWidth(), controlsMedia, media.Width,
                    UiScale.Px(ChromeMetrics.Gap, factor), inset)
                If placement.MaxWidth < size.Width Then size.Width = placement.MaxWidth
                top = media.Top + placement.Top
                left = If(Localization.IsRightToLeft(), media.Right - inset - placement.ReservedWidth - size.Width,
                          media.Left + inset)
            End If
        End If
        Return New Rectangle(left, top, size.Width, size.Height)
    End Function

    ''' <summary>The window-controls panel of <see cref="Window_Controls_Panel_Name"/>, or Nothing before it is built.</summary>
    Private Function ChromeWindowControlsPanel() As ChromePanel
        For Each p As ChromePanel In chrome_Panels
            If p.Name = Window_Controls_Panel_Name Then Return p
        Next
        Return Nothing
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
            Case Window_Controls_Panel_Name
                ' The reveal zone is LOCAL to the panel: its own rectangle grown by the margin, kept
                ' inside the media (188 §4). It never widens the toolbar rows' band, and visiting it
                ' does not summon the rows with it.
                Dim zone As Rectangle = Rectangle.Inflate(r, zoneHeight, zoneHeight)
                zone.Intersect(media)
                Return zone
            Case Else
                ' The top rows are summoned together: the zone is measured from the bottom of the LOWEST of them
                ' (row 2), for both - from each row's own bottom, row 2 hung alone in the band between the two
                ' (125 UX-308). The window-controls panel keeps out of it: its zone is its own corner (188 §4).
                Dim bottoms As New List(Of Integer)()
                For Each other As ChromePanel In chrome_Panels
                    If other.Name <> "chrome_Status" AndAlso other.Name <> Window_Controls_Panel_Name Then bottoms.Add(ChromePanelRect(other).Bottom)
                Next
                Dim bottom As Integer = ChromeAutoHide.TopHotZoneBottom(bottoms, zoneHeight)
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

    ''' <summary>Whether the viewer (or its path editor) holds the input focus: the auto-hide
    ''' countdown is only meaningful while it does, and losing it drops an unpinned surface at once.
    ''' An open popup or focus on an owned window keeps it active.</summary>
    Private Function ChromeWindowActive() As Boolean
        Return Me.ContainsFocus OrElse (Form.ActiveForm Is Me) OrElse
               (chrome_Path IsNot Nothing AndAlso chrome_Path.ContainsFocus) OrElse
               ChromeMenu.PopupVisible OrElse
               (Form.ActiveForm IsNot Nothing AndAlso Form.ActiveForm.Owner Is Me)
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
            If moved Then NotePointerMovedOnScreen()   ' feeds the cursor-hide stillness clock (UX-108 of 123)
            Dim usable As Boolean = ChromeUsable()
            Dim active As Boolean = ChromeWindowActive()
            ' The toolbar panels' holds: an open chrome popup, path editing, the F10 keyboard mode (117 §2.6).
            Dim hold As Boolean = ChromeMenu.PopupVisible OrElse chrome_Keyboard_Mode OrElse
                                  (chrome_Path IsNot Nothing AndAlso chrome_Path.ContainsFocus)
            ' A window with no media (the first run, an emptied folder) has nothing for a panel to cover and its
            ' commands are ON the panels: they stay up while the window is in use (125 UX-313m).
            If ChromeAutoHide.HoldsForEmptyWindow(ChromeMediaOnScreen(), now, chrome_Empty_Since) Then hold = True
            Dim animating As Boolean = False

            For Each p As ChromePanel In chrome_Panels
                For Each pair In p.Legacy
                    If SyncChromeItem(pair.Key, pair.Value) Then p.Host.Surface.LayoutItems()
                Next
                ' The window commands follow the window state within one tick (188 §5): a snap
                ' shortcut or the system menu flips the Maximize / Restore button on the next poll.
                If p.Name = Window_Controls_Panel_Name Then ChromeSyncWindowControlsItems(p)

                ' The window-controls panel is the removed caption's replacement, not part of the
                ' toolbar: a slideshow asked to hide the toolbar keeps the window commands (188 §3.8).
                ' Super-full-screen hides it through <see cref="ChromeUsable"/> like everything else.
                If Not usable OrElse (p.Name = "chrome_Status" AndAlso SlideshowHidesStatus()) OrElse
                   (p.Name <> "chrome_Status" AndAlso p.Name <> Window_Controls_Panel_Name AndAlso SlideshowHidesToolbar()) Then
                    p.Wanted = False
                    p.Alpha = 0.0R
                    p.TargetAlpha = 0.0R
                Else
                    Dim rect As Rectangle = If(p.Host.Visible, p.Host.Bounds, ChromePanelRect(p))
                    Dim over As Boolean = rect.Contains(pointer)

                    ' Never while the pointer is over it, never in focus mode, never when pinned: a panel
                    ' sliding out from under the reaching hand is worse than one that never appeared.
                    ' The countdown is the panels' own fixed 5 s from the leave (117 Ф-1); the video
                    ' controls' hide setting does not appear here any more.
                    Dim hotZoneReveal As Boolean = active AndAlso moved AndAlso Control.MouseButtons = MouseButtons.None AndAlso
                                                   ChromeHotZone(p).Contains(pointer)
                    p.Wanted = ChromeAutoHide.NextWanted(now, p.Wanted, p.LastActivity,
                                                         ChromePanelPinned(p), over, hold, hotZoneReveal, active)
                    ' A copy, move or delete holds the status panel up, pinned or not (RevealStatusOnFileOperation).
                    ' LastActivity is left alone on purpose: the ordinary countdown must not add its own five seconds
                    ' behind the hold, so the panel goes a fixed time after the LAST operation.
                    If active AndAlso p.Name = "chrome_Status" AndAlso FileOperationRevealHeld(now) Then p.Wanted = True

                    Dim target As Double = If(p.Wanted, 1.0R, 0.0R)
                    Dim fadeElapsed As Integer = elapsed
                    If target <> p.TargetAlpha Then
                        p.TargetAlpha = target
                        fadeElapsed = 0 ' The idle poll precedes this transition; it is not part of the fade.
                    End If
                    p.Alpha = ChromeAutoHide.NextAlpha(target, p.Alpha, ChromeFadeAllowed(), fadeElapsed)
                    If p.Alpha <> target Then animating = True
                End If

                ApplyChromeVisibility(p)

                If p.Host.Visible AndAlso p.Host.Surface.NeedsAnimation Then
                    p.Host.Surface.Animate(elapsed)
                    animating = True
                End If
            Next

            ' The recipients overlay rides the same cycle (117 Ф-2).
            If ChromeTickRecipientsOverlay(now, pointer, active, moved, elapsed, usable, False) Then animating = True

            If chrome_Path IsNot Nothing AndAlso Not chrome_Path.ContainsFocus Then
                chrome_Path.Text = cmbox_Media_Folder.Text
                ' The list is rebuilt only when it changed (125 UX-313k): ten times a second for the life of the
                ' window was a copy of up to a hundred strings for nothing.
                Dim recentSignature As String = RecentFoldersSignature()
                If recentSignature <> chrome_Recent_Signature Then
                    chrome_Recent_Signature = recentSignature
                    chrome_Path.RecentFolders = cmbox_Media_Folder.Items.Cast(Of Object)().Select(Function(entry) entry.ToString()).ToArray()
                End If
            End If
            Dim interval As Integer = If(animating, ChromeMetrics.AnimationTickMilliseconds, ChromeMetrics.IdleTickMilliseconds)
            If chrome_Timer.Interval <> interval Then chrome_Timer.Interval = interval
        Catch ex As Exception
            ' A tick must never raise: this runs ten times a second for the life of the window.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2601: chrome tick failed: " & ex.Message)
        End Try
    End Sub

    Private chrome_Recent_Signature As String = Nothing

    ''' <summary>The chrome clock's reading when a copy, move or delete was last started or finished; -1 = none yet.</summary>
    Private chrome_File_Op_Last As Long = -1L

    ''' <summary>A single-file copy, move or delete has been queued or has finished: the stamp the status panel's
    ''' hold counts from (see <see cref="ChromeAutoHide.FileOperationRevealHeld"/>). Anything else - undo, rename,
    ''' restore, one item of a batch - is not "a copy, a move or a delete" the user just asked for.</summary>
    Private Sub NoteFileOperationForStatusReveal(kind As FileOpKind)
        Select Case kind
            Case FileOpKind.Copy, FileOpKind.Move, FileOpKind.Delete, FileOpKind.RecycleDelete
                chrome_File_Op_Last = chrome_Clock.ElapsedMilliseconds
        End Select
    End Sub

    ''' <summary>Whether a file operation holds the status panel up right now: the setting is on and an operation is
    ''' queued or running, or the last one began or ended less than
    ''' <see cref="ChromeMetrics.FileOperationRevealMilliseconds"/> ago. The queue's own count is the "still running"
    ''' truth - a counter kept here could leak and leave the panel stuck up.</summary>
    Private Function FileOperationRevealHeld(nowMs As Long) As Boolean
        Dim enabled As Boolean = modern_Preferences IsNot Nothing AndAlso modern_Preferences.RevealStatusOnFileOperation
        If Not enabled Then Return False
        Dim inFlight As Integer = If(file_Op_Queue Is Nothing, 0, file_Op_Queue.PendingCount)
        Return ChromeAutoHide.FileOperationRevealHeld(enabled, nowMs, chrome_File_Op_Last, inFlight)
    End Function

    ''' <summary>The chrome clock's reading when the window last became empty; -1 while media is on screen
    ''' (see <see cref="ChromeAutoHide.HoldsForEmptyWindow"/>).</summary>
    Private chrome_Empty_Since As Long = -1L

    ''' <summary>A picture, a text, a page or a playing video/audio fills the media area. The first-run label
    ''' and the empty-folder message do not count: they are what the empty window shows.</summary>
    Private Function ChromeMediaOnScreen() As Boolean
        Return is_PictureBox1_Visible OrElse is_PictureBox2_Visible OrElse is_Vlc_Playing
    End Function

    ''' <summary>Cheap fingerprint of the folder box's list: its size and its two ends. The list only ever changes by
    ''' moving a folder to the front or dropping one off the back, so an unchanged fingerprint is an unchanged list.</summary>
    Private Function RecentFoldersSignature() As String
        Dim count As Integer = cmbox_Media_Folder.Items.Count
        If count = 0 Then Return "0"
        Return count.ToString() & "|" & cmbox_Media_Folder.Items(0).ToString() & "|" & cmbox_Media_Folder.Items(count - 1).ToString()
    End Function

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

    ' --- the recipients overlay's ride on the chrome cycle (117 Ф-2) ------------------------------
    ' The same wanted/alpha pair the panels carry, evaluated in Chrome_Timer_Tick. The overlay has
    ' a pin of its own (2026-10-04 revision of Ф-2) - PanelPinned by its window name, so the global
    ' switch and its own corner button hold it up; the per-panel pins do not. While it is in the
    ' auto-hidden phase nothing else may show it (PositionRecipientsOverlay no longer does).

    Private recipients_Wanted As Boolean
    Private recipients_LastActivity As Long
    Private recipients_Alpha As Double
    Private recipients_TargetAlpha As Double
    Private recipients_AppliedOpacity As Double = -1.0R
    ''' <summary>The overlay's own pin button, built with the overlay (nothing on net48).</summary>
    Private recipients_Overlay_Pin As RecipientsPinButton
    ''' <summary>The header strip's edit button (219 §4): the settings door at the strip's left end
    ''' (nothing on net48).</summary>
    Private recipients_Overlay_Edit As RecipientsEditButton
    ''' <summary>The header strip itself (219 §2): the grip between its two buttons that drags the
    ''' window (nothing on net48).</summary>
    Private recipients_Overlay_Strip As Control
    ''' <summary>The scrollable host of the recipient rows below the pin strip (nothing on net48).</summary>
    Private recipients_Overlay_Rows As Control

    ''' <summary>The overlay's header strip follows the preference and the language: the pin's checked
    ''' state (the glyph's tilt and fill), the strip's own accessible name and the words of both strip
    ''' buttons (219 §2/§4: the drag and the door must not be mouse-only secrets). A pin click, a
    ''' language switch and a theme change all land here; a disposed or missing control makes its
    ''' half a no-op.</summary>
    Private Sub SyncRecipientsOverlayPin()
        If recipients_Overlay_Pin Is Nothing OrElse recipients_Overlay_Pin.IsDisposed OrElse recipients_Overlay_Pin.Disposing Then Return
        Dim pinned As Boolean = ChromeAutoHide.PanelPinned(ChromePinSnapshot(), Recipients_Overlay_Panel_Name)
        recipients_Overlay_Pin.PinChecked = pinned
        recipients_Overlay_Pin.AccessibleName = If(pinned, Localization.T("Открепить таблицу получателей"),
                                                   Localization.T("Закрепить таблицу получателей"))
        If recipients_ToolTip IsNot Nothing Then
            recipients_ToolTip.SetToolTip(recipients_Overlay_Pin, If(pinned,
                Localization.T("Таблица получателей закреплена и не скрывается. Нажмите, чтобы открепить."),
                Localization.T("Закрепить таблицу получателей: она перестанет скрываться.")))
        End If
        recipients_Overlay_Pin.Invalidate()
        If recipients_Overlay_Strip IsNot Nothing AndAlso Not recipients_Overlay_Strip.IsDisposed Then
            recipients_Overlay_Strip.AccessibleName = Localization.T("Заголовок таблицы получателей; панель можно перетащить.")
        End If
        If recipients_Overlay_Edit IsNot Nothing AndAlso Not recipients_Overlay_Edit.IsDisposed AndAlso Not recipients_Overlay_Edit.Disposing Then
            recipients_Overlay_Edit.AccessibleName = Localization.T("Настройки таблицы получателей")
            If recipients_ToolTip IsNot Nothing Then
                recipients_ToolTip.SetToolTip(recipients_Overlay_Edit, Localization.T("Настройки таблицы получателей"))
            End If
            recipients_Overlay_Edit.Invalidate()
        End If
    End Sub

    ''' <summary>One cycle step for the overlay. Pinned, it stays up exactly as the panels did;
    ''' unpinned, it fades out <see cref="ChromeMetrics.PanelHideDelayMilliseconds"/> after the
    ''' pointer leaves it and reveals from a hot zone at its configured corner, with the panels'
    ''' holds. The slideshow's "hide toolbar/status" hides it with the chrome (117 §2.6). Returns
    ''' True while its fade runs, so the tick keeps the fast interval. The net48 overlay is always
    ''' on while enabled and knows nothing of this (Q117-4).</summary>
    Private Function ChromeTickRecipientsOverlay(nowMs As Long, pointer As Point, windowActive As Boolean,
                                                 pointerMoved As Boolean, elapsedMs As Integer, usable As Boolean,
                                                 snapAlpha As Boolean) As Boolean
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return False
        ' Opening the grid is a door: keep the overlay's current fade/visibility through
        ' the modal editor and the close-time rebuild, then resume its normal hide delay.
        If recipients_Edit_Active Then
            ApplyRecipientsOverlayVisibility()
            Return False
        End If
        Try
            Dim wanted As Boolean = False
            If usable AndAlso Is_Show_Recipients_Overlay AndAlso Not SlideshowHidesToolbar() Then
                Dim pinned As Boolean = ChromeAutoHide.PanelPinned(ChromePinSnapshot(), Recipients_Overlay_Panel_Name)
                ' The placed rectangle, not the window's bounds: the overlay may not have been
                ' shown yet, and its geometry is where the cycle would show it anyway.
                Dim over As Boolean = recipients_Drag_Active OrElse RecipientsOverlayPlacedRect().Contains(pointer)
                ' The overlay is its own floating surface (117 revision): it carries no popups,
                ' no path editor and no F10 ring, and toolbar popups (such as btn_Resource) must
                ' never summon or hold the unpinned table.
                Dim hold As Boolean = False
                Dim hotZone As Boolean = windowActive AndAlso pointerMoved AndAlso
                                         Control.MouseButtons = MouseButtons.None AndAlso
                                         RecipientsOverlayHotZone().Contains(pointer)
                wanted = ChromeAutoHide.NextWanted(nowMs, recipients_Wanted, recipients_LastActivity,
                                                   pinned, over, hold, hotZone, windowActive)
            End If
            recipients_Wanted = wanted
            Dim target As Double = If(wanted, 1.0R, 0.0R)
            If snapAlpha Then
                ' A pin toggle or a fresh build: land in the phase the cycle is in right now.
                recipients_TargetAlpha = target
                recipients_Alpha = target
            Else
                Dim fadeElapsed As Integer = elapsedMs
                If target <> recipients_TargetAlpha Then
                    recipients_TargetAlpha = target
                    fadeElapsed = 0
                End If
                recipients_Alpha = ChromeAutoHide.NextAlpha(target, recipients_Alpha, ChromeFadeAllowed(), fadeElapsed)
            End If
            ApplyRecipientsOverlayVisibility()
            Return recipients_Alpha <> target
        Catch ex As Exception
            ' Same contract as the panels' tick: this runs ten times a second for the life of the window.
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2602: recipients overlay tick failed: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Re-evaluates the overlay's lifecycle at once - a pin toggle (either
    ''' ChromeTogglePin or ChromeToggleRecipientsPin), a fresh build (ApplyRecipientsOverlay) - with
    ''' no fade: the overlay lands in the phase the cycle is in, shown when pinned or under the
    ''' pointer, hidden otherwise.</summary>
    Private Sub ChromeSyncRecipientsOverlayLifecycle()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return
        ChromeTickRecipientsOverlay(chrome_Clock.ElapsedMilliseconds, Cursor.Position, ChromeWindowActive(),
                                    False, 0, ChromeUsable(), True)
    End Sub

    ''' <summary>Shows or hides the overlay window for its current fade - the overlay's half of
    ''' <see cref="ApplyChromeVisibility"/>. At zero it is hidden outright: a layered window at
    ''' alpha 0 still takes the clicks. The configured translucency multiplies the fade.</summary>
    Private Sub ApplyRecipientsOverlayVisibility()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return
        If recipients_Alpha <= 0.0R Then
            If recipients_Overlay.Visible Then recipients_Overlay.Visible = False
            recipients_AppliedOpacity = -1.0R
            Return
        End If
        If Not recipients_Overlay.Visible Then
            PositionRecipientsOverlay()
            PinToViewerBand(recipients_Overlay)
            recipients_Overlay.Visible = True
        End If
        Dim configured As Double = If(chrome_High_Contrast, 1.0R,
                                      Math.Max(0.15R, Math.Min(1.0R, RecipientsOverlayAlpha() / 255.0R)))
        Dim opacity As Double = Math.Max(0.02R, Math.Min(1.0R, recipients_Alpha * configured))
        If Math.Abs(opacity - recipients_AppliedOpacity) > 0.004R Then
            recipients_Overlay.Opacity = opacity
            recipients_AppliedOpacity = opacity
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
        ChromeEnsureFocusValid()
        ' On a slider of the transport the vertical arrows and the page keys adjust it (121 UX-403); the bar is pinned
        ' left-to-right, so Right is forward there whatever the language.
        If ChromeFocusOnTransport() AndAlso TransportRingAdjust(code) Then Return True
        If keyData = (Keys.Shift Or Keys.F10) OrElse code = Keys.Apps Then
            If Not ChromeFocusOnTransport() Then chrome_Panels(chrome_Focus_Panel).Host.Surface.ActivateFocusedSecondary()
            Return True
        End If
        Select Case code
            Case Keys.Escape, Keys.F10
                If code = Keys.F10 Then chrome_F10_Pending = True
                ChromeLeaveKeyboardMode()
            Case Keys.Tab
                ChromeStepFocus(forward:=((modifiers And Keys.Shift) = Keys.None))
            Case Keys.Right, Keys.Down
                ChromeStepFocus(forward:=Not (rtl AndAlso code = Keys.Right AndAlso Not ChromeFocusOnTransport()))
            Case Keys.Left, Keys.Up
                ChromeStepFocus(forward:=(rtl AndAlso code = Keys.Left AndAlso Not ChromeFocusOnTransport()))
            Case Keys.Home
                ChromeJumpFocus(last:=False)
            Case Keys.End
                ChromeJumpFocus(last:=True)
            Case Keys.Enter, Keys.Space
                If ChromeFocusOnTransport() Then
                    TransportRingActivate()
                ElseIf chrome_Focus_Panel >= 0 AndAlso chrome_Focus_Panel < chrome_Panels.Count Then
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
        ' The transport bar is the last stop of the ring (121 UX-403): bring it up with the panels while a media plays.
        If is_Vlc_Playing Then ShowVideoControls()
        ' Up at once, not after the next idle tick.
        chrome_Timer.Interval = ChromeMetrics.AnimationTickMilliseconds
        ' While the ring is up every other key is inert, and nothing on screen said how to get out of it
        ' (UX-105 of 123): say it once per session, in the status line.
        If Not chrome_F10_Hint_Shown Then
            chrome_F10_Hint_Shown = True
            lbl_Status.Text = Localization.T("Режим клавиатуры панелей: стрелки и Tab - выбор, Enter - нажать, Esc - выйти")
        End If
    End Sub

    ''' <summary>The "how to leave the F10 mode" line was shown already in this run (UX-105 of 123).</summary>
    Private chrome_F10_Hint_Shown As Boolean

    Private Sub ChromeLeaveKeyboardMode()
        If Not chrome_Keyboard_Mode Then Return
        chrome_Keyboard_Mode = False
        TransportRingLeave()
        For Each p As ChromePanel In chrome_Panels
            p.Host.Surface.LeaveKeyboardMode()
            ' Leaving starts the ordinary countdown from now.
            p.LastActivity = chrome_Clock.ElapsedMilliseconds
        Next
    End Sub

    ''' <summary>The ring stands in the transport bar: it is the stop after the last panel (121 UX-403).</summary>
    Private Function ChromeFocusOnTransport() As Boolean
        Return chrome_Keyboard_Mode AndAlso chrome_Panels.Count > 0 AndAlso chrome_Focus_Panel >= chrome_Panels.Count
    End Function

    ''' <summary>The stops of the ring: every panel, and the transport bar after them while it is up.</summary>
    Private Function ChromeRingCount() As Integer
        Return chrome_Panels.Count + If(TransportRingAvailable(), 1, 0)
    End Function

    ''' <summary>The bar can go away under the ring (the video ended, the file changed): the ring goes back to the last panel.</summary>
    Private Sub ChromeEnsureFocusValid()
        If chrome_Focus_Panel < chrome_Panels.Count OrElse TransportRingAvailable() Then Return
        TransportRingLeave()
        chrome_Focus_Panel = chrome_Panels.Count - 1
        chrome_Panels(chrome_Focus_Panel).Host.Surface.EnterKeyboardMode(last:=True)
    End Sub

    ''' <summary>Takes the ring off whatever it stands on (a panel or the bar).</summary>
    Private Sub ChromeLeaveCurrentStop()
        If ChromeFocusOnTransport() Then
            TransportRingLeave()
        ElseIf chrome_Focus_Panel >= 0 AndAlso chrome_Focus_Panel < chrome_Panels.Count Then
            chrome_Panels(chrome_Focus_Panel).Host.Surface.LeaveKeyboardMode()
        End If
    End Sub

    ''' <summary>Puts the ring on a stop, at its near end (or far end).</summary>
    Private Sub ChromeEnterStop(index As Integer, last As Boolean)
        chrome_Focus_Panel = index
        If index >= chrome_Panels.Count Then
            If Not TransportRingEnter(last) Then
                chrome_Focus_Panel = chrome_Panels.Count - 1
                chrome_Panels(chrome_Focus_Panel).Host.Surface.EnterKeyboardMode(last)
            End If
        Else
            chrome_Panels(index).Host.Surface.EnterKeyboardMode(last)
        End If
    End Sub

    Private Sub ChromeStepFocus(forward As Boolean)
        If chrome_Panels.Count = 0 Then Return
        If ChromeFocusOnTransport() Then
            If TransportRingMove(forward) Then Return
        Else
            If chrome_Panels(chrome_Focus_Panel).Host.Surface.MoveFocus(forward) Then Return
        End If

        ' Off the end of this stop: on to the next one (wrapping), starting at its near end.
        ChromeLeaveCurrentStop()
        Dim count As Integer = ChromeRingCount()
        ChromeEnterStop((chrome_Focus_Panel + If(forward, 1, -1) + count) Mod count, last:=Not forward)
    End Sub

    Private Sub ChromeJumpFocus(last As Boolean)
        If chrome_Panels.Count = 0 Then Return
        ChromeLeaveCurrentStop()
        ChromeEnterStop(If(last, ChromeRingCount() - 1, 0), last)
    End Sub
End Class
#End If
