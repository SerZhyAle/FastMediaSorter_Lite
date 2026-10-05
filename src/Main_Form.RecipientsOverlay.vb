Option Strict On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Floating "recipients" panel shown top-left over the media surface.
'
' It restores the original intent of the (now renamed) "keep on top" checkbox:
' while sorting, the user sees the destination folders over the image and clicks
' one to move/copy (or Delete) the current file - one hand on the mouse. See
' docs/specifications/SPECIFICATION_RECIPIENTS_OVERLAY_DOTNET48.md.
'
' The panel is a child of panel_Media (same pattern as the full-screen toolbar
' overlay), narrow-but-tall, sized to the number of *registered* destination
' folders + a Delete row. A click is exactly equivalent to pressing the matching
' key: recipients go through PoMove(slot), Delete through
' ReadShowMediaFile(Mode_Delete) - identical behaviour, no new file logic.
Partial Public Class Main_Form

    ''' <summary>The name the overlay's window carries - and the name its own pin answers by in
    ''' the chrome's pin algebra (2026-10-04 revision of 117 Ф-2).</summary>
    Private Const Recipients_Overlay_Panel_Name As String = "recipients_Overlay"

    Private Function RecipientPx(logical As Integer) As Integer
#If NETFRAMEWORK Then
        Return LogicalToDeviceUnits(logical)
#Else
        Return UiScale.Px(logical, ChromeScale())
#End If
    End Function

    Private recipients_Overlay As RecipientsOverlayWindow
    Private recipients_ToolTip As ToolTip
    Private recipients_Overlay_Tracking As Boolean

    ''' <summary>The font the current overlay generation was built with. A Font assigned
    ''' to a control is NOT disposed by Control.Dispose, so without holding it here every
    ''' rebuild (settings closed, form resized, flag toggled) abandoned one.</summary>
    Private recipients_Overlay_Font As Font

    ''' <summary>Final teardown for the overlay's own long-lived bits - called from
    ''' Form1_FormClosing. ApplyRecipientsOverlay handles the per-rebuild half.</summary>
    Private Sub DisposeRecipientsOverlayResources()
        Try
            If recipients_Overlay IsNot Nothing Then
                recipients_Overlay.Close()
                recipients_Overlay.Dispose()
                recipients_Overlay = Nothing
            End If
            If recipients_ToolTip IsNot Nothing Then
                recipients_ToolTip.RemoveAll()
                recipients_ToolTip.Dispose()
                recipients_ToolTip = Nothing
            End If
            If recipients_Overlay_Font IsNot Nothing Then
                recipients_Overlay_Font.Dispose()
                recipients_Overlay_Font = Nothing
            End If
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Builds/shows the recipients overlay when <c>Is_Show_Recipients_Overlay</c>
    ''' is on, or tears it down when off. Single entry point - call it after the
    ''' flag changes, when destinations change (settings closed), and once the form
    ''' is shown/sized.
    ''' </summary>
    Friend Sub ApplyRecipientsOverlay()
        ' Tear down any existing overlay first (rebuild from current data).
        If recipients_Overlay IsNot Nothing Then
            Try
                ' RemoveAll BEFORE the window goes: a ToolTip keeps an entry per control it
                ' was set on, so without this it held every generation of dead buttons for
                ' the lifetime of the form.
                If recipients_ToolTip IsNot Nothing Then recipients_ToolTip.RemoveAll()
                recipients_Overlay.Close()
                recipients_Overlay.Dispose()
            Catch
            End Try
            recipients_Overlay = Nothing
        End If
#If Not NETFRAMEWORK Then
        ' The pin button and the rows host die with the window; drop the stale references.
        recipients_Overlay_Pin = Nothing
        recipients_Overlay_Rows = Nothing
#End If

        If recipients_Overlay_Font IsNot Nothing Then
            recipients_Overlay_Font.Dispose()
            recipients_Overlay_Font = Nothing
        End If

        If Not Is_Show_Recipients_Overlay Then Return
        If panel_Media Is Nothing OrElse Not Me.IsHandleCreated Then Return

        Try
            BuildRecipientsOverlay()
            If recipients_Overlay Is Nothing Then Return
            TrackRecipientsOverlayGeometry()
            recipients_Overlay.Owner = Me
            PositionRecipientsOverlay()
            ' An owned form is not automatically born in its owner's topmost band.
            ' Pin it before Show so an overlay created during startup is visible when
            ' the viewer already has "always on top" enabled.
            PinToViewerBand(recipients_Overlay)
#If Not NETFRAMEWORK Then
            ' The overlay is born in the phase the chrome cycle is in right now (117 Ф-2):
            ' pinned, or with the pointer on it, it appears at once; otherwise it stays hidden
            ' and the cycle owns it from here. The net48 build keeps its always-on overlay.
            ChromeSyncRecipientsOverlayLifecycle()
            ConfigureFloatingMediaChrome()
#Else
            recipients_Overlay.Show()
#End If
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2400: recipients overlay build failed: " & ex.Message)
            ' Debug.WriteLine is stripped from Release: without this a table that never
            ' appears (the old "Error creating window handle") left nothing in current.log.
            AppFileLogger.LogException("Recipients overlay: build", ex)
        End Try
    End Sub

    ''' <summary>The overlay is a window of its own, so - unlike a child panel - it does
    ''' not travel with the form for free. Subscribed once; the handlers no-op while the
    ''' overlay is off.</summary>
    Private Sub TrackRecipientsOverlayGeometry()
        If recipients_Overlay_Tracking Then Return
        recipients_Overlay_Tracking = True
        AddHandler Me.LocationChanged, Sub() KeepRecipientsOverlayOnTop()
        AddHandler Me.SizeChanged, Sub() KeepRecipientsOverlayOnTop()
#If Not NETFRAMEWORK Then
        ' A verdict that lands after the overlay was built has to reach it, and by
        ' RESTATING the rows rather than rebuilding them: a rebuild disposes every button
        ' and its font, and a probe answering mid-click would then take the button out from
        ' under the pointer.
        AddHandler Me.SlotHealthChanged, Sub() ApplyRecipientsOverlayHealth()
#End If
    End Sub

    ''' <summary>Re-asserts the overlay's position and z-order after the media
    ''' surface changed (video/webbrowser can BringToFront over it). Cheap no-op
    ''' when the overlay is off.</summary>
    Friend Sub KeepRecipientsOverlayOnTop()
        If recipients_Overlay Is Nothing OrElse Not Is_Show_Recipients_Overlay Then Return
        Try
            PositionRecipientsOverlay()
        Catch
        End Try
    End Sub

    ''' <summary>What a recipient row says after its key: the folder - or, on the mainline, for a
    ''' folder of a saved resource, "«Name» /folder" (007 §4.2) instead of the bare address.</summary>
    Private Function RecipientRowCaption(destination As String) As String
#If NETFRAMEWORK Then
        Return destination
#Else
        Return SlotCaption(destination)
#End If
    End Function

    Private Sub BuildRecipientsOverlay()
        ' Rows: registered destinations (keys 1..9, then 0) + Delete last.
        ' Item1 = slot (1..10; -1 = delete). Runtime convention: key "k" -> slot k,
        ' key "0" -> slot 10 (matches the keyboard, NOT the legacy grid DoKey).
        Dim rows As New List(Of Tuple(Of Integer, String))()
        For k As Integer = 1 To 9
            Dim keyPath As String = Hardkeys_to_move_mediafile(k)
#If Not NETFRAMEWORK Then
            ' The key written on the row is the one the slot answers to now - a rebound slot said "3" while Q moved
            ' (125 UX-334).
            If Not String.IsNullOrEmpty(keyPath) Then rows.Add(Tuple.Create(k, SlotKeyLabel(k) & "  " & RecipientRowCaption(keyPath)))
#Else
            If Not String.IsNullOrEmpty(keyPath) Then rows.Add(Tuple.Create(k, k.ToString() & "  " & RecipientRowCaption(keyPath)))
#End If
        Next
        Dim zeroPath As String = Hardkeys_to_move_mediafile(10)
#If Not NETFRAMEWORK Then
        If Not String.IsNullOrEmpty(zeroPath) Then rows.Add(Tuple.Create(10, SlotKeyLabel(10) & "  " & RecipientRowCaption(zeroPath)))
#Else
        If Not String.IsNullOrEmpty(zeroPath) Then rows.Add(Tuple.Create(10, "0  " & RecipientRowCaption(zeroPath)))
#End If
        ' Delete is always available (a valid sort action even with no folders set).
        rows.Add(Tuple.Create(-1, ChrW(215) & "  " & Localization.T("Удалить")))

        If recipients_ToolTip Is Nothing Then recipients_ToolTip = New ToolTip()

        ' Held in a field so the NEXT rebuild can dispose it (see ApplyRecipientsOverlay).
#If NETFRAMEWORK Then
        Dim ui_Font As New Font(Me.Font.FontFamily, RecipientsOverlayFontSize(), Me.Font.Style)
#Else
        Dim ui_Font As Font = CreateRecipientChromeFont()
#End If
        recipients_Overlay_Font = ui_Font
        Dim rowH As Integer = ui_Font.Height + RecipientPx(10)

        ' Width from the widest caption, capped so it never eats more than half the
        ' media panel (longer paths ellipsize; full path stays in the tooltip).
        Dim maxTextW As Integer = 0
        For Each r In rows
            Dim w As Integer = TextRenderer.MeasureText(r.Item2, ui_Font).Width
            If w > maxTextW Then maxTextW = w
        Next
        Dim desiredW As Integer = RecipientPx(RecipientsOverlayWidth())
        Dim capW As Integer = Math.Min(RecipientPx(480), CInt(panel_Media.ClientSize.Width * 0.5))
        If capW < RecipientPx(120) Then capW = RecipientPx(120)   ' usable floor on tiny windows
        Dim panelW As Integer = Math.Min(desiredW, capW)

        ' WinForms does not alpha-compose a normal child Panel over its sibling
        ' PictureBox/VideoView, so the overlay needs a layered window to let the
        ' configured opacity reveal the media beneath it. A layered CHILD window is not
        ' it: WS_EX_LAYERED on a child fails CreateWindowEx outright here ("Error
        ' creating window handle" in current.log, reproduced on .NET 10 / Windows 11),
        ' which is why the table never appeared. A borderless owned top-level window is
        ' layered by the shell in the normal way - and it also floats over the LibVLC
        ' VideoView, which a sibling control cannot reliably do.
        Dim overlay As New RecipientsOverlayWindow With {
            .Name = Recipients_Overlay_Panel_Name,
            .BackColor = Color.FromArgb(32, 32, 32),
            .Opacity = Math.Max(0.15R, Math.Min(1.0R, RecipientsOverlayAlpha() / 255.0R)),
            .Padding = New Padding(0)
        }

        ' Mainline: every recipient row carries a second, narrow zone that COPIES into the
        ' same folder - the row itself still moves, so the muscle memory is untouched and
        ' copying stops being a hidden global mode. The delete row stays one action: a
        ' deletion has no copy. On net48 the row keeps its single meaning, which the
        ' global copy-mode checkbox there decides (SPECIFICATION_COPY_ACTIONS_REWORK.md §4.3).
        Dim copy_Caption As String = ""
        Dim copyW As Integer = 0
#If Not NETFRAMEWORK Then
        copy_Caption = Localization.T("копия")
        copyW = Math.Max(RecipientPx(38), MeasureRowText(copy_Caption, ui_Font) + RecipientPx(14))
        ' A narrow window must not lose the path to the copy zone: below a usable width the
        ' row goes back to move-only, and the copy stays reachable by Shift+digit.
        If copyW > panelW - RecipientPx(80) Then copyW = 0
#End If

        Dim y As Integer = 0
        Dim visibleRows As Integer = RecipientsOverlayVisibleRows()
        Dim scrollRows As Boolean = rows.Count > visibleRows
        ' With a scrollbar the rows must fit beside it: full-width rows grew a horizontal
        ' scrollbar too, and the copy zones sat under the vertical one (audit B-13).
        Dim rowW As Integer = If(scrollRows, panelW - SystemInformation.VerticalScrollBarWidth, panelW)

        ' The rows' host. net48 adds them to the window itself. The modern build puts them into an
        ' inner scrollable panel below a slim header strip, so the strip's pin button stays put
        ' while the rows scroll under it - the overlay pins like a panel now (2026-10-04 revision
        ' of 117 Ф-2), and its pin must never scroll out of reach.
        Dim rowsHost As Control = overlay
        Dim headerH As Integer = 0
#If Not NETFRAMEWORK Then
        Dim pinSize As Integer = RecipientPx(ChromeMetrics.MinTarget)
        ' Two px off the end edge keep the hover box clear of the window's rounded corner.
        ' (VB rejects a comment INSIDE a multi-line object initializer, so it sits here.)
        Dim pin As New RecipientsPinButton(Me) With {
            .Name = "recipients_Overlay_Pin",
            .Size = New Size(pinSize, pinSize),
            .Location = New Point(Math.Max(0, panelW - pinSize - RecipientPx(2)), 0)}
        AddHandler pin.Click, Sub() ChromeToggleRecipientsPin()
        overlay.Controls.Add(pin)
        recipients_Overlay_Pin = pin

        Dim rowsPanel As New Panel With {
            .Location = New Point(0, pinSize),
            .Size = New Size(panelW, 0),
            .BorderStyle = BorderStyle.None}
        overlay.Controls.Add(rowsPanel)
        recipients_Overlay_Rows = rowsPanel
        rowsHost = rowsPanel
        rowsPanel.AutoScroll = scrollRows
        headerH = pinSize
        SyncRecipientsOverlayPin()
#End If
        For Each r In rows
            Dim isDelete As Boolean = (r.Item1 = -1)
            Dim showCopy As Boolean = Not isDelete AndAlso copyW > 0
            Dim b As New NonFocusButton() With {
                .Text = r.Item2,
                .Tag = r.Item1,
                .Font = ui_Font,
                .FlatStyle = FlatStyle.Flat,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Padding = New Padding(RecipientPx(6), 0, RecipientPx(2), 0),
                .AutoEllipsis = True,
                .TabStop = False,
                .UseVisualStyleBackColor = False,
                .Location = New Point(0, y),
                .Size = New Size(If(showCopy, rowW - copyW, rowW), rowH),
                .ForeColor = If(isDelete, Color.White, RecipientRowInk()),
                .BackColor = If(isDelete, Color.FromArgb(96, 32, 32), RecipientRowFill(r.Item1))
            }
            b.FlatAppearance.BorderSize = 0
#If Not NETFRAMEWORK Then
            b.RowFont = ui_Font
#End If
            ' Hover deepens the row's own blend (129 §3.3); only the DEL row keeps its own reds.
            b.FlatAppearance.MouseOverBackColor = If(isDelete, Color.FromArgb(140, 44, 44), RecipientRowHoverFill(r.Item1))
            b.FlatAppearance.MouseDownBackColor = If(isDelete, Color.FromArgb(170, 50, 50), RecipientRowHoverFill(r.Item1))
            ' Full path in the tooltip - the caption may be ellipsized.
            If Not isDelete Then
#If Not NETFRAMEWORK Then
                b.Name = Recipients_Move_Row_Name
#End If
                recipients_ToolTip.SetToolTip(b, If(showCopy,
                                                    Localization.TF("Переместить в: {0}", Hardkeys_to_move_mediafile(r.Item1)),
                                                    Hardkeys_to_move_mediafile(r.Item1)))
            End If
            AddHandler b.Click, AddressOf RecipientOverlayButton_Click
            rowsHost.Controls.Add(b)

#If Not NETFRAMEWORK Then
            If showCopy Then
                Dim c As New NonFocusButton() With {
                    .Text = copy_Caption,
                    .Tag = r.Item1,
                    .Font = ui_Font,
                    .FlatStyle = FlatStyle.Flat,
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .Padding = New Padding(0),
                    .AutoEllipsis = True,
                    .TabStop = False,
                    .UseVisualStyleBackColor = False,
                    .Location = New Point(rowW - copyW, y),
                    .Size = New Size(copyW, rowH),
                    .ForeColor = Color.Gainsboro,
                    .BackColor = Color.FromArgb(38, 62, 38)
                }
                c.Name = Recipients_Copy_Row_Name
                c.RowFont = ui_Font
                c.FlatAppearance.BorderSize = 0
                c.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 88, 52)
                c.FlatAppearance.MouseDownBackColor = Color.FromArgb(66, 112, 66)
                recipients_ToolTip.SetToolTip(c, Localization.TF("Скопировать в: {0}", Hardkeys_to_move_mediafile(r.Item1)))
                AddHandler c.Click, AddressOf RecipientOverlayCopyButton_Click
                rowsHost.Controls.Add(c)
            End If
#End If

            y += rowH
        Next

#If Not NETFRAMEWORK Then
        rowsPanel.Size = New Size(panelW, If(scrollRows, rowH * visibleRows, y))
        overlay.ClientSize = New Size(panelW, headerH + If(scrollRows, rowH * visibleRows, y))
#Else
        overlay.AutoScroll = scrollRows
        overlay.ClientSize = New Size(panelW, If(scrollRows, rowH * visibleRows, y))
#End If
        recipients_Overlay = overlay
#If Not NETFRAMEWORK Then
        ApplyRecipientsOverlayHealth()
#End If
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>The width a caption takes when it is drawn the way the rows draw it (GDI+, in pixels), so the
    ''' copy zone is sized by what is painted and not by a second, differently-scaled measurement.</summary>
    Private Shared Function MeasureRowText(text As String, font As Font) As Integer
        Using scratch As New Bitmap(1, 1)
            Using g As Graphics = Graphics.FromImage(scratch)
                Return CInt(Math.Ceiling(g.MeasureString(text, font, Integer.MaxValue, StringFormat.GenericTypographic).Width))
            End Using
        End Using
    End Function

    ''' <summary>Names for the two zones of a recipient row, so the health pass can find
    ''' them again without rebuilding the overlay (which would dispose and remake every
    ''' button and its font).</summary>
    Private Const Recipients_Move_Row_Name As String = "recipient_move_row"
    Private Const Recipients_Copy_Row_Name As String = "recipient_copy_row"
#End If

    ''' <summary>The identity fill of a recipient row: the slot's pastel from the one palette the
    ''' settings grid reads too, so a slot is the same colour on both surfaces (129 §3.3). The
    ''' themed build turns high contrast back to the panel fill - identity colour is an aid,
    ''' never a substitute for the system scheme; the legacy overlay, which has no theme, is
    ''' always dark and says so.</summary>
    Private Function RecipientRowFill(slot As Integer) As Color
#If NETFRAMEWORK Then
        Return RecipientPalette.ColorFor(slot, True)
#Else
        If chrome_Theme.IsHighContrast Then Return chrome_Theme.PanelFill
        Return RecipientPalette.ColorFor(slot, ThemeService.IsDark)
#End If
    End Function

    ''' <summary>The hover fill: the same blend deepened (129 §3.3), never a different colour role.</summary>
    Private Function RecipientRowHoverFill(slot As Integer) As Color
#If NETFRAMEWORK Then
        Return RecipientPalette.HoverColorFor(slot, True)
#Else
        If chrome_Theme.IsHighContrast Then Return chrome_Theme.ItemHover
        Return RecipientPalette.HoverColorFor(slot, ThemeService.IsDark)
#End If
    End Function

    ''' <summary>The ink on an identity fill stays the theme's text ink (129 §3.1) - never a
    ''' per-row black/white guess.</summary>
    Private Function RecipientRowInk() As Color
#If NETFRAMEWORK Then
        Return Color.Gainsboro
#Else
        If chrome_Theme.IsHighContrast Then Return chrome_Theme.Foreground
        Return ThemePalette.ForDark(ThemeService.IsDark).TextPrimary
#End If
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>
    ''' Redraws the rows from the CACHED slot verdicts (§3.5). It probes nothing: opening the
    ''' overlay must never be able to cost a timeout, and a slot nobody has asked about yet is
    ''' drawn normally - dimming what has not been checked would be a lie of the same shape as
    ''' the one this feature removes.
    ''' </summary>
    Friend Sub ApplyRecipientsOverlayHealth()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return

        Try
            ' A resource that cannot delete draws its move and DEL rows disabled, from the
            ' same answer the refusal point gives (006 PV-7). Always True for a plain folder.
            Dim move_Allowed As Boolean = ResourceAllows(ResourceVerb.Move)
            Dim delete_Allowed As Boolean = ResourceAllows(ResourceVerb.Delete)

            ' The rows live in the inner scrollable panel below the pin strip (modern build); the
            ' pin is not a Button, so a fallback to the window itself skips it either way.
            Dim rowsHost As Control = recipients_Overlay
            If recipients_Overlay_Rows IsNot Nothing AndAlso Not recipients_Overlay_Rows.IsDisposed Then rowsHost = recipients_Overlay_Rows
            For Each control As Control In rowsHost.Controls
                Dim row As Button = TryCast(control, Button)
                If row Is Nothing OrElse row.Tag Is Nothing Then Continue For
                Dim slot As Integer = CInt(row.Tag)
                If slot = -1 Then
                    ' The delete row: its own colours, as BuildRecipientsOverlay paints them.
                    row.ForeColor = If(delete_Allowed, chrome_Theme.DestructiveForeground, chrome_Theme.DisabledForeground)
                    row.BackColor = If(delete_Allowed, chrome_Theme.DestructiveRest, chrome_Theme.PanelFill)
                    row.FlatAppearance.MouseOverBackColor = If(delete_Allowed AndAlso Not chrome_Theme.IsHighContrast, chrome_Theme.DestructiveHover, chrome_Theme.PanelFill)
                    row.FlatAppearance.MouseDownBackColor = If(delete_Allowed AndAlso Not chrome_Theme.IsHighContrast, chrome_Theme.DestructivePressed, chrome_Theme.PanelFill)
                    Continue For
                End If
                If slot < 1 OrElse slot > 10 Then Continue For   ' the delete row has no destination

                Dim is_Copy_Zone As Boolean = (row.Name = Recipients_Copy_Row_Name)
                Dim destination As String = Hardkeys_to_move_mediafile(slot)
                Dim verdict As SlotVerdict = CachedSlotVerdict(destination)
                Dim unusable As Boolean = (verdict IsNot Nothing AndAlso Not SlotHealthPolicy.IsUsable(verdict.State)) OrElse
                                          (Not is_Copy_Zone AndAlso Not move_Allowed)

                ' The fill is the slot's identity (129 §3.3): health answers in the ink and the
                ' tooltip, never by swapping the fill. The copy zone keeps its accent marker and
                ' the DEL row its destructive red - states of the panel, not recipients.
                If is_Copy_Zone Then
                    row.BackColor = If(unusable OrElse chrome_Theme.IsHighContrast, chrome_Theme.PanelFill,
                                       ChromeTheme.Blend(chrome_Theme.PanelFill, chrome_Theme.Accent, ChromeMetrics.HoverPercent / 100.0F))
                Else
                    row.BackColor = RecipientRowFill(slot)
                End If
                row.ForeColor = If(is_Copy_Zone, chrome_Theme.Foreground, RecipientRowInk())
                If unusable Then row.ForeColor = If(is_Copy_Zone OrElse chrome_Theme.IsHighContrast,
                                                   chrome_Theme.DisabledForeground,
                                                   ThemePalette.ForDark(ThemeService.IsDark).TextDisabled)

                row.FlatAppearance.MouseOverBackColor = If(unusable OrElse chrome_Theme.IsHighContrast, row.BackColor,
                                                            If(is_Copy_Zone, chrome_Theme.ItemHover, RecipientRowHoverFill(slot)))
                row.FlatAppearance.MouseDownBackColor = If(unusable OrElse chrome_Theme.IsHighContrast, row.BackColor,
                                                            If(is_Copy_Zone, chrome_Theme.ItemPressed, RecipientRowHoverFill(slot)))
                If recipients_ToolTip IsNot Nothing Then
                    Dim note As String = SlotHealthNote(verdict)
                    Dim tip As String = If(is_Copy_Zone,
                                           Localization.TF("Скопировать в: {0}", destination),
                                           Localization.TF("Переместить в: {0}", destination))
                    If note <> "" Then tip &= Environment.NewLine & note
                    recipients_ToolTip.SetToolTip(row, tip)
                End If
            Next
        Catch ex As Exception
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2401: recipients overlay health pass failed: " & ex.Message)
        End Try
    End Sub
#End If

    Private Sub PositionRecipientsOverlay()
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return
#If Not NETFRAMEWORK Then
        If is_Super_Full_Screen_Mode Then
            recipients_Overlay.Hide()
            Return
        End If
#End If

        ' A window of its own is not clipped by the form, so it must hide itself whenever
        ' the media surface is not on screen - minimized, or the form hidden.
        If Me.WindowState = FormWindowState.Minimized OrElse Not Me.Visible OrElse
           Not panel_Media.Visible OrElse panel_Media.ClientSize.Width <= 0 Then
            If recipients_Overlay.Visible Then recipients_Overlay.Visible = False
            Return
        End If
#If NETFRAMEWORK Then
        ' net48 keeps the overlay always on while enabled (117 Q117-4). The modern build's chrome
        ' cycle owns the overlay's visibility (117 Ф-2), so there it must NOT be force-shown:
        ' while the overlay is in its auto-hidden phase it stays hidden even though the media
        ' surface is on screen, and the chrome timer shows it through ApplyRecipientsOverlayVisibility.
        If Not recipients_Overlay.Visible AndAlso recipients_Overlay.IsHandleCreated Then recipients_Overlay.Visible = True
#End If

        Dim onScreen As Point = RecipientsOverlayPlacedRect().Location
        If recipients_Overlay.Location <> onScreen Then recipients_Overlay.Location = onScreen
    End Sub

    ''' <summary>Where the overlay sits over the media surface, in screen coordinates - the one
    ''' geometry <see cref="PositionRecipientsOverlay"/> places by and the modern build's reveal
    ''' hot zone extends toward the configured corner. No visibility side effects: the overlay's
    ''' window may be hidden while this runs.</summary>
    Private Function RecipientsOverlayPlacedRect() As Rectangle
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return Rectangle.Empty
        Dim margin As Integer = RecipientPx(8)
        Dim topOffset As Integer = margin
        ' In full-screen (not super) the floating toolbar sits over the top strip;
        ' drop the overlay below it so it never covers the toolbar buttons.
        If is_Full_Screen_Mode AndAlso Not is_Super_Full_Screen_Mode AndAlso
           flow_Toolbar IsNot Nothing AndAlso flow_Toolbar.Parent Is panel_Media Then
            topOffset = flow_Toolbar.Height + margin
        End If
#If Not NETFRAMEWORK Then
        If chrome_Panels.Count >= 2 AndAlso Not is_Super_Full_Screen_Mode Then
            topOffset = margin + chrome_Panels(0).Host.Surface.PreferredPanelSize().Height +
                chrome_Panels(1).Host.Surface.PreferredPanelSize().Height + UiScale.Px(ChromeMetrics.Gap, ChromeScale())
        End If
#End If
        Dim x As Integer = margin
        Dim y As Integer = topOffset
        Select Case RecipientsOverlayPosition()
            Case "topRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
            Case "bottomLeft"
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
            Case "bottomRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
        End Select

        ' The corner is the media area's, expressed on screen - the overlay is a separate
        ' window, so its Location is in screen coordinates, not the form's client space.
        Return New Rectangle(panel_Media.PointToScreen(New Point(x, y)), recipients_Overlay.Size)
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>The pointer reveals the auto-hidden overlay from near its configured corner
    ''' (117 Ф-2): the placed rectangle extended outward toward that corner, by the panels'
    ''' hot-zone reach. It follows <see cref="RecipientsOverlayPosition"/>, so a moved overlay
    ''' carries its zone with it.</summary>
    Private Function RecipientsOverlayHotZone() As Rectangle
        Dim placed As Rectangle = RecipientsOverlayPlacedRect()
        If placed.IsEmpty Then Return Rectangle.Empty
        Dim reach As Integer = RecipientPx(ChromeMetrics.HotZoneMargin)
        Dim zone As Rectangle = placed
        Select Case RecipientsOverlayPosition()
            Case "topRight"
                zone.Y -= reach
                zone.Height += reach
                zone.Width += reach
            Case "bottomLeft"
                zone.Height += reach
                zone.X -= reach
                zone.Width += reach
            Case "bottomRight"
                zone.Height += reach
                zone.Width += reach
            Case Else   ' topLeft
                zone.Y -= reach
                zone.Height += reach
                zone.X -= reach
                zone.Width += reach
        End Select
        Return zone
    End Function
#End If

    ''' <summary>Remembers the last click per row, to drop a double-click's second one.</summary>
    Private ReadOnly recipient_Click_Guard As New RepeatClickGuard()

    ''' <summary>A click on a recipient row is exactly the matching key press.</summary>
    Private Sub RecipientOverlayButton_Click(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b Is Nothing OrElse b.Tag Is Nothing Then Return
        Dim slot As Integer = CInt(b.Tag)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2410: recipients overlay click, slot=" & slot.ToString())

        ' A habitual double-click on a row is two Clicks on a Button - two files moved
        ' where one was meant (audit 047, C-20). The second, within 300 ms, is dropped.
        If recipient_Click_Guard.IsRepeat(slot, input_Clock.ElapsedMilliseconds) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2412: recipients overlay repeat click ignored")
            Return
        End If

        ' Match the keyboard path: it stops the slideshow first (KeybUse).
        SlideShowStop()
        If slot = -1 Then
            ReadShowMediaFile(Mode_Delete)
        Else
#If Not NETFRAMEWORK Then
            ' The refusal says "click again" to someone who clicked (125 UX-332); the net48 gate has no such wording.
            slot_Press_By_Pointer = True
            Try
                PoMove(slot)
            Finally
                slot_Press_By_Pointer = False
            End Try
#Else
            PoMove(slot)
#End If
        End If

        ' The media surface may have changed under us - stay clickable on top.
        KeepRecipientsOverlayOnTop()
    End Sub

#If Not NETFRAMEWORK Then
    ''' <summary>A click on a row's copy zone is exactly Shift + that digit.</summary>
    Private Sub RecipientOverlayCopyButton_Click(sender As Object, e As EventArgs)
        Dim b As Button = TryCast(sender, Button)
        If b Is Nothing OrElse b.Tag Is Nothing Then Return
        Dim slot As Integer = CInt(b.Tag)
        Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2411: recipients overlay copy click, slot=" & slot.ToString())

        ' Same guard as the move zone, under its own key so a move and a copy of one row
        ' in quick succession are two gestures (C-20).
        If recipient_Click_Guard.IsRepeat(100 + slot, input_Clock.ElapsedMilliseconds) Then
            Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w2413: recipients overlay repeat copy click ignored")
            Return
        End If

        SlideShowStop()
        slot_Press_By_Pointer = True
        Try
            ExecuteRecipientAction(slot, RecipientActionKind.Copy)
        Finally
            slot_Press_By_Pointer = False
        End Try
        KeepRecipientsOverlayOnTop()
    End Sub
#End If

    ''' <summary>A flat button that never takes keyboard focus, so clicking a row
    ''' does not steal focus from the media surface (the app is keyboard-driven;
    ''' with focus here, Space/Enter would re-activate the button via KeyPreview).</summary>
    Private NotInheritable Class NonFocusButton
        Inherits Button

        Public Sub New()
            SetStyle(ControlStyles.Selectable, False)
            Me.TabStop = False
            ' Captions are folder paths: "R&D" must not become an underlined D (audit B-20).
            Me.UseMnemonic = False
        End Sub

#If Not NETFRAMEWORK Then
        Private hovering As Boolean
        Private pressing As Boolean
        Private ringColor As Color = Color.Empty

        ''' <summary>The F10 ring's mark (121 UX-403): a frame in this colour while the keyboard ring stands on this
        ''' button, nothing when it is <see cref="Color.Empty"/>. The button never takes real focus, so the ring is
        ''' the host's to say, and it is painted here, with the button.</summary>
        Public Property FocusRingColor As Color
            Get
                Return ringColor
            End Get
            Set(value As Color)
                If ringColor = value Then Return
                ringColor = value
                Invalidate()
            End Set
        End Property

        ''' <summary>The frame's width in device pixels.</summary>
        Public Property FocusRingWidth As Integer = 2

        ''' <summary>Tells assistive technology the ring arrived here (the control is not focusable, so the framework
        ''' would never say it).</summary>
        Public Sub NotifyFocused()
            AccessibilityNotifyClients(AccessibleEvents.Focus, -1)
        End Sub

        ''' <summary>The caption's font, held here and not read back from <c>Font</c>: a pixel font handed to a
        ''' control came out of its GDI text path about twice the size it was built at on a scaled display,
        ''' and the table's captions overflowed their rows.</summary>
        Public Property RowFont As Font

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            hovering = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            hovering = False
            pressing = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(mevent As MouseEventArgs)
            MyBase.OnMouseDown(mevent)
            If mevent.Button = MouseButtons.Left Then pressing = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(mevent As MouseEventArgs)
            MyBase.OnMouseUp(mevent)
            pressing = False
            Invalidate()
        End Sub

        ''' <summary>Fill, then the caption with the outline that keeps it readable over a see-through table.</summary>
        Protected Overrides Sub OnPaint(pevent As PaintEventArgs)
            Dim g As Graphics = pevent.Graphics
            Dim fill As Color = If(Not Enabled, BackColor,
                                   If(pressing, FlatAppearance.MouseDownBackColor,
                                      If(hovering, FlatAppearance.MouseOverBackColor, BackColor)))
            Using brush As New SolidBrush(fill)
                g.FillRectangle(brush, ClientRectangle)
            End Using
            If ringColor <> Color.Empty Then
                Dim ringWidth As Integer = Math.Max(1, FocusRingWidth)
                Using pen As New Pen(ringColor, ringWidth)
                    g.DrawRectangle(pen, ringWidth \ 2, ringWidth \ 2, Width - ringWidth, Height - ringWidth)
                End Using
            End If
            ' The icon is drawn by the control, not by whoever sets it (121 Ф-1): this override used to paint
            ' the caption only, so the transport's Play and Mute - Text "" and the glyph in Image - came out as
            ' flat empty squares. A caption-only row never reaches this branch, so its pixels are unchanged.
            Dim imageEnd As Integer = 0
            If Image IsNot Nothing Then
                Dim box As Rectangle = ImageBox(Image.Size)
                g.DrawImage(Image, box)
                imageEnd = box.Right
            End If
            If String.IsNullOrEmpty(Text) Then Return
            Dim captionFont As Font = If(RowFont, Font)
            Using format As New StringFormat(StringFormatFlags.NoWrap)
                format.LineAlignment = StringAlignment.Center
                format.Trimming = StringTrimming.EllipsisCharacter
                format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None
                format.Alignment = If(TextAlign = ContentAlignment.MiddleCenter, StringAlignment.Center, StringAlignment.Near)
                If RightToLeft = RightToLeft.Yes Then format.FormatFlags = format.FormatFlags Or StringFormatFlags.DirectionRightToLeft
                Dim left As Single = If(imageEnd > 0, imageEnd, Padding.Left)
                Dim area As New RectangleF(left, 0, Math.Max(0, Width - left - Padding.Right), Height)
                ChromeInkOutline.DrawText(g, Text, captionFont, area, ForeColor, format, ChromeInkOutline.RadiusFor(DeviceDpi / 96.0F))
            End Using
        End Sub

        ''' <summary>Where the icon goes: <c>ImageAlign</c> inside the client area, at the icon's own size.</summary>
        Private Function ImageBox(size As Size) As Rectangle
            Dim x As Integer = (Width - size.Width) \ 2
            Dim y As Integer = (Height - size.Height) \ 2
            Select Case ImageAlign
                Case ContentAlignment.TopLeft, ContentAlignment.MiddleLeft, ContentAlignment.BottomLeft
                    x = Padding.Left
                Case ContentAlignment.TopRight, ContentAlignment.MiddleRight, ContentAlignment.BottomRight
                    x = Width - size.Width - Padding.Right
            End Select
            Select Case ImageAlign
                Case ContentAlignment.TopLeft, ContentAlignment.TopCenter, ContentAlignment.TopRight
                    y = Padding.Top
                Case ContentAlignment.BottomLeft, ContentAlignment.BottomCenter, ContentAlignment.BottomRight
                    y = Height - size.Height - Padding.Bottom
            End Select
            Return New Rectangle(x, y, size.Width, size.Height)
        End Function
#End If
    End Class

#If Not NETFRAMEWORK Then
    ''' <summary>The recipients overlay's own pin button (2026-10-04 revision of 117 Ф-2): the same
    ''' ambient corner control the chrome panels carry - no fill at rest, a small hover/press cue, an
    ''' upright filled pin while pinned and a tilted outlined one while free - drawn by the shared
    ''' <see cref="ChromeSurface.PaintPin"/>. It never takes focus, so clicking it leaves the
    ''' keyboard with the viewer, exactly like a recipient row.</summary>
    Private NotInheritable Class RecipientsPinButton
        Inherits Control

        Private ReadOnly owner As Main_Form
        Private hovering As Boolean
        Private pressing As Boolean
        Private checkedState As Boolean

        Public Sub New(form As Main_Form)
            owner = form
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            ' Never selectable: the click must not pull the keyboard focus from the media surface.
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
            AccessibleRole = AccessibleRole.CheckButton
        End Sub

        ''' <summary>Set by SyncRecipientsOverlayPin from the persisted preference; drives the glyph.</summary>
        Public Property PinChecked As Boolean
            Get
                Return checkedState
            End Get
            Set(value As Boolean)
                If value = checkedState Then Return
                checkedState = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            hovering = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            hovering = False
            pressing = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(mevent As MouseEventArgs)
            MyBase.OnMouseDown(mevent)
            If mevent.Button = MouseButtons.Left Then pressing = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseUp(mevent As MouseEventArgs)
            MyBase.OnMouseUp(mevent)
            pressing = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
            ' Nothing: the strip is the window's own flat fill, painted under this control already.
        End Sub

        ''' <summary>The hover/press cue, then the pin - the panels' ambient corner button, verbatim.</summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim theme As ChromeTheme = owner.chrome_Theme
            Dim scale As Single = owner.ChromeScale()
            Dim r As Rectangle = ClientRectangle
            Dim size As Integer = Math.Min(r.Width, Math.Min(r.Height, UiScale.Px(ChromeMetrics.PinButtonSize, scale)))
            If hovering OrElse pressing Then
                Dim fill As Color = If(pressing, theme.ItemPressed, theme.ItemHover)
                fill = ChromeTheme.Blend(theme.PanelFill, fill, 1.0F)
                Using path As GraphicsPath = ChromeSurface.RoundedRect(
                        New RectangleF(r.X + (r.Width - size) \ 2, r.Y + (r.Height - size) \ 2, size, size),
                        UiScale.Px(ChromeMetrics.ItemCorner, scale))
                    Using brush As New SolidBrush(fill)
                        g.FillPath(brush, path)
                    End Using
                End Using
            End If
            Dim ink As Color
            If theme.IsHighContrast Then
                ink = If(checkedState, SystemColors.HighlightText, SystemColors.WindowText)
            Else
                Dim restInk As Color = ChromeTheme.Blend(theme.PanelFill, theme.Foreground, If(checkedState, 0.5F, 0.35F))
                ink = If(hovering, ChromeTheme.Blend(restInk, theme.Foreground, 1.0F), restInk)
            End If
            ChromeSurface.PaintPin(g, checkedState,
                                   New Rectangle(r.X + (r.Width - size) \ 2, r.Y + (r.Height - size) \ 2, size, size),
                                   ink, scale)
        End Sub
    End Class
#End If

    ''' <summary>
    ''' The overlay's host: a borderless owned window, so Form.Opacity gives it a real
    ''' compositor alpha channel over the media (a child control cannot alpha-compose over
    ''' a sibling PictureBox/VideoView, and WS_EX_LAYERED on a CHILD window fails to be
    ''' created at all - see the comment at BuildRecipientsOverlay). It never activates:
    ''' the app is keyboard-driven and a click here must not take focus from the viewer.
    ''' </summary>
#If Not NETFRAMEWORK Then
    Private NotInheritable Class RecipientsOverlayWindow
        Inherits ChromeHost
        Public Sub New()
            MyBase.New(False)
        End Sub
    End Class
#Else
    Private NotInheritable Class RecipientsOverlayWindow
        Inherits Form

        Private Const WS_EX_NOACTIVATE As Integer = &H8000000
        Private Const WS_EX_TOOLWINDOW As Integer = &H80

        Public Sub New()
            FormBorderStyle = FormBorderStyle.None
            StartPosition = FormStartPosition.Manual
            ShowInTaskbar = False
            ControlBox = False
            MinimizeBox = False
            MaximizeBox = False
            KeyPreview = False
            AutoScaleMode = AutoScaleMode.None
        End Sub

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim parameters As CreateParams = MyBase.CreateParams
                parameters.ExStyle = parameters.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
                Return parameters
            End Get
        End Property
    End Class

#End If
End Class
