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

#If Not NETFRAMEWORK Then
    ''' <summary>Whether the rows carry a catalog glyph before their caption (WINDOWS-UI 0.4 section 11 item 1). Not in a
    ''' right-to-left layout: the row control paints its picture and caption itself and does not yet mirror the pair, so
    ''' those languages keep the text-only rows rather than an overlapped caption.</summary>
    Private ReadOnly Property recipients_Row_Glyphs As Boolean
        Get
            Return Not Localization.IsRightToLeft()
        End Get
    End Property

    ''' <summary>One glyph, 16 logical px at the overlay's scale, and the 6 px gap to the caption in the same bitmap,
    ''' in the row's current ink. Re-run whenever the row's ink changes.</summary>
    Private Sub ApplyRecipientRowGlyph(row As Button, glyphId As String)
        Dim size As Integer = RecipientPx(ButtonGlyphs.GlyphLogicalSize)
        Dim composed As New Bitmap(size + RecipientPx(ButtonGlyphs.GapLogicalSize), size)
        Try
            Using glyph As Bitmap = GlyphRenderer.Render(glyphId, size, row.ForeColor, AddressOf RuntimeBootstrap.OpenBundledAsset)
                Using g As Graphics = Graphics.FromImage(composed)
                    g.DrawImageUnscaled(glyph, 0, 0)
                End Using
            End Using
        Catch ex As Exception
            composed.Dispose()
            AppFileLogger.LogException("Recipients overlay: row glyph", ex)
            Return
        End Try
        Dim previous As Image = row.Image
        row.Image = composed
        row.ImageAlign = ContentAlignment.MiddleLeft
        If previous Is Nothing Then
            AddHandler row.Disposed, Sub() row.Image?.Dispose()
        Else
            previous.Dispose()
        End If
        row.Invalidate()
    End Sub
#End If

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
        ' The strip's buttons and the rows host die with the window; drop the stale references.
        recipients_Overlay_Pin = Nothing
        recipients_Overlay_Edit = Nothing
        recipients_Overlay_Strip = Nothing
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
#If Not NETFRAMEWORK Then
        ' The glyph of the delete row is the catalog's own (WINDOWS-UI 0.4 section 11); the multiplication sign
        ' that stood in for it is not a picture of the vocabulary (ICON-RENDER rule 1).
        rows.Add(Tuple.Create(-1, Localization.T("Удалить")))
#Else
        rows.Add(Tuple.Create(-1, ChrW(215) & "  " & Localization.T("Удалить")))
#End If

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
        copyW = Math.Max(RecipientPx(38), MeasureRowText(copy_Caption, ui_Font) + RecipientPx(14) + If(recipients_Row_Glyphs, RecipientPx(16 + 6), 0))
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
        ' The header strip (219 §2): the edit button at its left end, the pin at its right, the
        ' empty grip between them - the one surface that moves the window. The rows scroll under
        ' it; its buttons stay put. The strip inherits the window's fill, so it paints nothing.
        Dim strip As New Panel With {
            .Name = "recipients_Overlay_Strip",
            .Size = New Size(panelW, pinSize),
            .BorderStyle = BorderStyle.None,
            .Cursor = Cursors.SizeAll,
            .AccessibleRole = AccessibleRole.TitleBar}
        ' (VB rejects a comment INSIDE a multi-line object initializer, so it sits here.)
        Dim edit As New RecipientsEditButton(Me) With {
            .Name = "recipients_Overlay_Edit",
            .Size = New Size(pinSize, pinSize),
            .Cursor = Cursors.Default,
            .Location = New Point(RecipientPx(2), 0)}
        AddHandler edit.Click, Sub() RecipientsOverlayEditRequested()
        strip.Controls.Add(edit)
        recipients_Overlay_Edit = edit
        ' Two px off the end edge keep the hover box clear of the window's rounded corner.
        Dim pin As New RecipientsPinButton(Me) With {
            .Name = "recipients_Overlay_Pin",
            .Size = New Size(pinSize, pinSize),
            .Cursor = Cursors.Default,
            .Location = New Point(Math.Max(0, panelW - pinSize - RecipientPx(2)), 0)}
        AddHandler pin.Click, Sub() ChromeToggleRecipientsPin()
        strip.Controls.Add(pin)
        recipients_Overlay_Pin = pin
        overlay.Controls.Add(strip)
        recipients_Overlay_Strip = strip
        ' The grip is the strip's empty area: a left press runs the native move loop - the same
        ' drag the window-commands panel's grip runs - so the pointer keeps its grab point, the
        ' window never activates and the viewer keeps the keyboard for the whole drag (219 §2).
        AddHandler strip.MouseDown, Sub(s, e) If e.Button = MouseButtons.Left Then RecipientsOverlayBeginDrag()

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
                .ForeColor = If(isDelete, RecipientDeleteRowInk(), RecipientRowInk()),
                .BackColor = If(isDelete, Color.FromArgb(96, 32, 32), RecipientRowFill(r.Item1))
            }
            b.FlatAppearance.BorderSize = 0
#If Not NETFRAMEWORK Then
            b.RowFont = ui_Font
            If recipients_Row_Glyphs Then ApplyRecipientRowGlyph(b, If(isDelete, "action.delete", "action.move"))
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
                If recipients_Row_Glyphs Then ApplyRecipientRowGlyph(c, "action.copy")
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

#If Not NETFRAMEWORK Then
    ''' <summary>True when the recipients overlay's own opacity setting is below the 220 threshold: the
    ''' panel's ink flips tone - black becomes white, white becomes black, the outline with it - on
    ''' ITS knob, independently of the chrome panels' setting. High contrast never flips.</summary>
    Private Function RecipientsOverlayInkInverted() As Boolean
        If chrome_High_Contrast Then Return False
        If modern_Preferences Is Nothing Then Return False
        Return ChromeMetrics.InvertsInk(modern_Preferences.RecipientsOverlayOpacity)
    End Function

    ''' <summary>The theme the recipients overlay paints from: the BASE palette (never the panels'
    ''' flipped view), flipped here only when the overlay's own knob is below the threshold. The
    ''' fills are identical in both views; only the ink differs.</summary>
    Private Function RecipientsOverlayTheme() As ChromeTheme
        If RecipientsOverlayInkInverted() Then Return chrome_Base_Theme.WithInvertedInk()
        Return chrome_Base_Theme
    End Function
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
        Dim theme As ChromeTheme = RecipientsOverlayTheme()
        If theme.IsHighContrast Then Return theme.PanelFill
        Return RecipientPalette.ColorFor(slot, ThemeService.IsDark)
#End If
    End Function

    ''' <summary>The hover fill: the same blend deepened (129 §3.3), never a different colour role.</summary>
    Private Function RecipientRowHoverFill(slot As Integer) As Color
#If NETFRAMEWORK Then
        Return RecipientPalette.HoverColorFor(slot, True)
#Else
        Dim theme As ChromeTheme = RecipientsOverlayTheme()
        If theme.IsHighContrast Then Return theme.ItemHover
        Return RecipientPalette.HoverColorFor(slot, ThemeService.IsDark)
#End If
    End Function

    ''' <summary>The ink on an identity fill stays the theme's text ink (129 §3.1) - never a
    ''' per-row black/white guess. Below the overlay's own 220 half the ink flips tone like every
    ''' other label of the panel.</summary>
    Private Function RecipientRowInk() As Color
#If NETFRAMEWORK Then
        Return Color.Gainsboro
#Else
        Dim theme As ChromeTheme = RecipientsOverlayTheme()
        If theme.IsHighContrast Then Return theme.Foreground
        If RecipientsOverlayInkInverted() Then Return ChromeTheme.InvertedTone(ThemePalette.ForDark(ThemeService.IsDark).TextPrimary)
        Return ThemePalette.ForDark(ThemeService.IsDark).TextPrimary
#End If
    End Function

    ''' <summary>The delete row's label ink: the theme's destructive foreground, which flips with the
    ''' overlay's own 220 knob (white becomes black below half). net48 keeps its fixed white.</summary>
    Private Function RecipientDeleteRowInk() As Color
#If NETFRAMEWORK Then
        Return Color.White
#Else
        Return RecipientsOverlayTheme().DestructiveForeground
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
            Dim theme As ChromeTheme = RecipientsOverlayTheme()
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
                    row.ForeColor = If(delete_Allowed, theme.DestructiveForeground, theme.DisabledForeground)
                    row.BackColor = If(delete_Allowed, theme.DestructiveRest, theme.PanelFill)
                    row.FlatAppearance.MouseOverBackColor = If(delete_Allowed AndAlso Not theme.IsHighContrast, theme.DestructiveHover, theme.PanelFill)
                    row.FlatAppearance.MouseDownBackColor = If(delete_Allowed AndAlso Not theme.IsHighContrast, theme.DestructivePressed, theme.PanelFill)
                    If recipients_Row_Glyphs Then ApplyRecipientRowGlyph(row, "action.delete")
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
                    row.BackColor = If(unusable OrElse theme.IsHighContrast, theme.PanelFill,
                                       ChromeTheme.Blend(theme.PanelFill, theme.Accent, ChromeMetrics.HoverPercent / 100.0F))
                Else
                    row.BackColor = RecipientRowFill(slot)
                End If
                row.ForeColor = If(is_Copy_Zone, theme.Foreground, RecipientRowInk())
                ' Below the overlay's own 220 half the disabled ink follows the flipped pair (the
                ' theme's disabled blend), never a palette gray of the old tone.
                If unusable Then row.ForeColor = If(is_Copy_Zone OrElse theme.IsHighContrast OrElse RecipientsOverlayInkInverted(),
                                                   theme.DisabledForeground,
                                                   ThemePalette.ForDark(ThemeService.IsDark).TextDisabled)

                row.FlatAppearance.MouseOverBackColor = If(unusable OrElse theme.IsHighContrast, row.BackColor,
                                                            If(is_Copy_Zone, theme.ItemHover, RecipientRowHoverFill(slot)))
                row.FlatAppearance.MouseDownBackColor = If(unusable OrElse theme.IsHighContrast, row.BackColor,
                                                            If(is_Copy_Zone, theme.ItemPressed, RecipientRowHoverFill(slot)))
                If recipients_Row_Glyphs Then ApplyRecipientRowGlyph(row, If(is_Copy_Zone, "action.copy", "action.move"))
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
#If Not NETFRAMEWORK Then
        PersistRecipientsOverlayRepairedPosition(onScreen)
#End If
    End Sub

    ''' <summary>Where the overlay sits over the media surface, in screen coordinates - the one
    ''' geometry <see cref="PositionRecipientsOverlay"/> places by and the modern build's reveal
    ''' hot zone grows from. No visibility side effects: the overlay's window may be hidden while
    ''' this runs.</summary>
    Private Function RecipientsOverlayPlacedRect() As Rectangle
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return Rectangle.Empty
#If Not NETFRAMEWORK Then
        ' Mid-drag the window is the truth (219 §2): the tick's containment checks and the reveal
        ' zone follow the pointer, and nothing re-places the panel out from under it.
        If recipients_Drag_Active Then Return New Rectangle(recipients_Overlay.Location, recipients_Overlay.Size)
#End If
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
#If Not NETFRAMEWORK Then
        ' The anchored corner (219 §3) - the coarse move, one honest function, not a copy that
        ' can drift.
        Dim offset As Point = RecipientsOverlayPlacement.CornerOffset(RecipientsOverlayPosition(),
            panel_Media.ClientSize, recipients_Overlay.Size, margin, topOffset)
        x = offset.X
        y = offset.Y
        ' The first drop of a real drag wrote a custom point (219 §3): logical px relative to
        ' the media area's top-left, back through the same scale seam the corner offsets were
        ' measured by. It overrides the anchor until the next corner pick, the reset row or
        ' the profile reset.
        If modern_Preferences IsNot Nothing AndAlso modern_Preferences.RecipientsOverlayHasCustomPosition Then
            Dim custom As Point = RecipientsOverlayPlacement.ToDeviceOffset(
                New Point(modern_Preferences.RecipientsOverlayCustomX, modern_Preferences.RecipientsOverlayCustomY),
                ChromeScale())
            x = custom.X
            y = custom.Y
        End If
#Else
        Select Case RecipientsOverlayPosition()
            Case "topRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
            Case "bottomLeft"
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
            Case "bottomRight"
                x = Math.Max(margin, panel_Media.ClientSize.Width - recipients_Overlay.Width - margin)
                y = Math.Max(margin, panel_Media.ClientSize.Height - recipients_Overlay.Height - margin)
        End Select
#End If

        ' The media area's top-left, expressed on screen - the overlay is a separate window, so
        ' its Location is in screen coordinates, not the form's client space.
        Dim areaOrigin As Point = panel_Media.PointToScreen(New Point(0, 0))
        Dim location As New Point(areaOrigin.X + x, areaOrigin.Y + y)
#If Not NETFRAMEWORK Then
        ' The recovery clamp (219 §6): every placement validates the placed rectangle against
        ' the media area's CURRENT on-screen rectangle - startup, settings close, form resize,
        ' DPI or monitor change, form move. The panel is a free window over a moving world.
        ' Out of bounds means the header strip (the grip, the only thing that can bring it
        ' back) is not fully inside the area; the panel is then pulled back by the shortest
        ' translation that fits it, margins included.
        Dim area As New Rectangle(areaOrigin, panel_Media.ClientSize)
        If Not RecipientsOverlayPlacement.HeaderInside(location, recipients_Overlay.Size,
                                                       RecipientPx(ChromeMetrics.MinTarget), area) Then
            location = RecipientsOverlayPlacement.PullInside(location, recipients_Overlay.Size,
                                                             Rectangle.Inflate(area, -margin, -margin))
        End If
#End If
        Return New Rectangle(location, recipients_Overlay.Size)
    End Function

#If Not NETFRAMEWORK Then
    ''' <summary>The pointer reveals the auto-hidden overlay (117 Ф-2) from the zone around the
    ''' placed rectangle (219 §7, Q219-4): the panel's own rect grown outward toward the nearest
    ''' edges of the media area, wherever the user put it - the configured corner is a position,
    ''' not a compass. For a panel still on its corner anchor this is exactly the corner zone of
    ''' 117 Ф-2.</summary>
    Private Function RecipientsOverlayHotZone() As Rectangle
        Dim placed As Rectangle = RecipientsOverlayPlacedRect()
        If placed.IsEmpty Then Return Rectangle.Empty
        Return RecipientsOverlayPlacement.HotZone(placed,
            New Rectangle(panel_Media.PointToScreen(New Point(0, 0)), panel_Media.ClientSize),
            RecipientPx(ChromeMetrics.HotZoneMargin))
    End Function

    ''' <summary>True while the header strip's native move loop runs (219 §2): the placed rectangle
    ''' is the window's live one, so the auto-hide cycle sees the pointer inside the panel for the
    ''' whole drag and nothing re-places the panel out from under it.</summary>
    Private recipients_Drag_Active As Boolean

    ''' <summary>The grip's press (219 §2): the whole window follows the pointer through Windows'
    ''' own move loop - the same drag the window-commands panel's grip runs - never activating it,
    ''' so the viewer keeps the keyboard. The reveal rule of 117 Ф-2 holds for the whole drag and
    ''' the pin state is not changed; the auto-hide cycle resumes from where it was on drop.</summary>
    Private Sub RecipientsOverlayBeginDrag()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed OrElse
           Not recipients_Overlay.IsHandleCreated Then Return
        Dim before As Point = recipients_Overlay.Location
        recipients_Drag_Active = True
        Try
            ReleaseCapture()
            Dim pointer As Point = Cursor.Position
            Dim lParam As New IntPtr((pointer.Y << 16) Or (pointer.X And &HFFFF))
            SendMessage(recipients_Overlay.Handle, WmNcLButtonDown, New IntPtr(HtCaption), lParam)
        Finally
            recipients_Drag_Active = False
        End Try
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return
        ' Saved on drop, not per pixel - and a drag that ends where it began writes nothing (219 §3).
        If recipients_Overlay.Location <> before Then SaveRecipientsOverlayDraggedPosition()
    End Sub

    ''' <summary>Writes the dropped position as the custom point (219 §3): logical px relative to the
    ''' media area's top-left, so a DPI or monitor change re-places it at the same visual spot instead
    ''' of compounding device pixels. The drop may sit outside the media area - the drag never clips
    ''' mid-air - and the recovery clamp of 219 §6 repairs it at the next placement.</summary>
    Private Sub SaveRecipientsOverlayDraggedPosition()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed OrElse panel_Media Is Nothing Then Return
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim origin As Point = panel_Media.PointToScreen(New Point(0, 0))
        Dim dropped As Point = RecipientsOverlayPlacement.ToLogicalPoint(
            New Point(recipients_Overlay.Location.X - origin.X, recipients_Overlay.Location.Y - origin.Y),
            ChromeScale())
        prefs.RecipientsOverlayCustomX = dropped.X
        prefs.RecipientsOverlayCustomY = dropped.Y
        prefs.RecipientsOverlayHasCustomPosition = True
        SaveRecipientsOverlayPreferences()
    End Sub

    ''' <summary>The repair half of the recovery (219 §6): when a placement pulled the panel back,
    ''' the pulled-back point is what stays saved - a lost position never survives a run and never
    ''' needs a second repair on the next start. Silent: no dialog, no toast, nothing beyond the
    ''' ordinary trace; the panel is simply where the user last saw it work.</summary>
    Private Sub PersistRecipientsOverlayRepairedPosition(applied As Point)
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed OrElse panel_Media Is Nothing Then Return
        Dim prefs As ModernViewerPreferences = modern_Preferences
        If prefs Is Nothing OrElse Not prefs.RecipientsOverlayHasCustomPosition Then Return
        Dim origin As Point = panel_Media.PointToScreen(New Point(0, 0))
        Dim stored As Point = RecipientsOverlayPlacement.ToDeviceOffset(
            New Point(prefs.RecipientsOverlayCustomX, prefs.RecipientsOverlayCustomY), ChromeScale())
        If origin.X + stored.X = applied.X AndAlso origin.Y + stored.Y = applied.Y Then Return   ' no repair fired
        Dim repaired As Point = RecipientsOverlayPlacement.ToLogicalPoint(
            New Point(applied.X - origin.X, applied.Y - origin.Y), ChromeScale())
        prefs.RecipientsOverlayCustomX = repaired.X
        prefs.RecipientsOverlayCustomY = repaired.Y
        SaveRecipientsOverlayPreferences()
    End Sub

    ''' <summary>Persists the overlay's own block at once - the discipline a pin toggle uses
    ''' (<see cref="ChromeToggleRecipientsPin"/>): write now, and on a failed write fall back to
    ''' the settings dirty-flush retry.</summary>
    Private Sub SaveRecipientsOverlayPreferences()
        If modern_Preferences Is Nothing Then Return
        Try
            modern_Preferences.Save()
        Catch ex As Exception
            AppFileLogger.LogException("Recipients overlay: persisting the position", ex)
            MarkSettingsDirty()
        End Try
    End Sub

    ''' <summary>The header's edit button (219 §4): opens the viewer's settings window already
    ''' standing on the recipients page, the destination slots grid focused. A door, not a move:
    ''' the click changes neither the pin, nor the visibility, nor the position, and the button
    ''' itself never takes the keyboard from the viewer.</summary>
    Private Sub RecipientsOverlayEditRequested()
        ShowSettingsWindowAtRecipientsGrid()
    End Sub

    ''' <summary>The settings' reset row (219 §5): clears the custom point and repositions the
    ''' overlay at once - no restart, no settings reopen - from any state (visible, auto-hidden,
    ''' dragged somewhere the user forgot). Idempotent: with the panel already at its anchor it is
    ''' an honest no-op that still says so through the Notify line.</summary>
    Friend Sub ResetRecipientsOverlayPosition()
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim hadCustom As Boolean = prefs.RecipientsOverlayHasCustomPosition
        prefs.RecipientsOverlayHasCustomPosition = False
        SaveRecipientsOverlayPreferences()
        ApplyRecipientsOverlay()
        Notify(Localization.T(If(hadCustom, "Позиция панели получателей сброшена.",
                                 "Панель получателей уже у своего угла.")), NotifyLevel.Info)
    End Sub
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
    ''' <summary>The shared look of the header strip's two buttons (219 §2/§4): the same ambient
    ''' corner control the chrome panels carry - no fill at rest, a small hover/press cue, the glyph
    ''' in the theme's ink. Never selectable: a click leaves the keyboard focus with the viewer,
    ''' exactly like a recipient row.</summary>
    Private MustInherit Class RecipientsStripButton
        Inherits Control

        Protected ReadOnly owner As Main_Form
        Protected hovering As Boolean
        Private pressing As Boolean

        Public Sub New(form As Main_Form)
            owner = form
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            ' Never selectable: the click must not pull the keyboard focus from the media surface.
            SetStyle(ControlStyles.Selectable, False)
            TabStop = False
        End Sub

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

        ''' <summary>The hover/press cue, then the glyph - the panels' ambient corner button, verbatim.</summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Dim theme As ChromeTheme = owner.RecipientsOverlayTheme()
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
            Dim bounds As New Rectangle(r.X + (r.Width - size) \ 2, r.Y + (r.Height - size) \ 2, size, size)
            PaintGlyph(g, bounds, Ink(theme), scale)
        End Sub

        ''' <summary>The glyph's ink: the theme's foreground blended into the panel fill, full on hover.
        ''' High contrast hands the choice to the system.</summary>
        Protected Overridable Function Ink(theme As ChromeTheme) As Color
            If theme.IsHighContrast Then Return SystemColors.WindowText
            Dim restInk As Color = ChromeTheme.Blend(theme.PanelFill, theme.Foreground, 0.35F)
            Return If(hovering, ChromeTheme.Blend(restInk, theme.Foreground, 1.0F), restInk)
        End Function

        ''' <summary>The button's own picture, inside the cue's rounded box.</summary>
        Protected MustOverride Sub PaintGlyph(g As Graphics, bounds As Rectangle, ink As Color, scale As Single)
    End Class

    ''' <summary>The recipients overlay's own pin button (2026-10-04 revision of 117 Ф-2): an upright
    ''' filled pin while pinned and a tilted outlined one while free - drawn by the shared
    ''' <see cref="ChromeSurface.PaintPin"/>. Its click is the pin algebra's, unchanged by the
    ''' strip's drag around it.</summary>
    Private NotInheritable Class RecipientsPinButton
        Inherits RecipientsStripButton

        Private checkedState As Boolean

        Public Sub New(form As Main_Form)
            MyBase.New(form)
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

        Protected Overrides Function Ink(theme As ChromeTheme) As Color
            If theme.IsHighContrast Then Return If(checkedState, SystemColors.HighlightText, SystemColors.WindowText)
            Dim restInk As Color = ChromeTheme.Blend(theme.PanelFill, theme.Foreground, If(checkedState, 0.5F, 0.35F))
            Return If(hovering, ChromeTheme.Blend(restInk, theme.Foreground, 1.0F), restInk)
        End Function

        Protected Overrides Sub PaintGlyph(g As Graphics, bounds As Rectangle, ink As Color, scale As Single)
            ChromeSurface.PaintPin(g, checkedState, bounds, ink, scale)
        End Sub
    End Class

    ''' <summary>The header's edit button (219 §4): the door to the settings window standing on the
    ''' recipients table - the same owner-drawn look as the pin, its glyph the catalog's pencil
    ''' (WINDOWS-UI 0.4 section 11) rendered through the shared renderer in the theme's ink. Like the
    ''' pin it never takes keyboard focus from the viewer: a non-activating window opens a window.</summary>
    Private NotInheritable Class RecipientsEditButton
        Inherits RecipientsStripButton

        Public Sub New(form As Main_Form)
            MyBase.New(form)
            AccessibleRole = AccessibleRole.PushButton
        End Sub

        Protected Overrides Sub PaintGlyph(g As Graphics, bounds As Rectangle, ink As Color, scale As Single)
            Try
                Using glyph As Bitmap = GlyphRenderer.Render("action.edit", bounds.Width, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
                    g.DrawImageUnscaled(glyph, bounds.X, bounds.Y)
                End Using
            Catch ex As Exception
                AppFileLogger.LogException("Recipients overlay: edit glyph", ex)
            End Try
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
