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
    ''' <summary>Whether the rows that carry a catalog glyph (the delete row's bin and the copy zone's sheets - never the
    ''' folder rows, which say it with the word at their end) draw it before their caption (WINDOWS-UI 0.4 section 11
    ''' item 1). Not in a right-to-left layout: the row control paints its picture and caption itself and does not yet
    ''' mirror the pair, so those languages keep the text-only rows rather than an overlapped caption.</summary>
    Private ReadOnly Property recipients_Row_Glyphs As Boolean
        Get
            Return Not Localization.IsRightToLeft()
        End Get
    End Property

    ''' <summary>One glyph, as tall as the row allows (<see cref="RecipientsOverlayRows.GlyphSide"/> - it follows the
    ''' row, which follows the table's font), and the 6 px gap to the caption in the same bitmap, in the row's current
    ''' ink. Re-run whenever the row's ink changes.</summary>
    Private Sub ApplyRecipientRowGlyph(row As Button, glyphId As String)
        Dim size As Integer = RecipientsOverlayRows.GlyphSide(row.Height)
        Dim gap As Integer = RecipientPx(ButtonGlyphs.GapLogicalSize)
        Dim composed As New Bitmap(size + gap, size)
        Try
            composed.SetResolution(96.0F, 96.0F)
            Using glyph As Bitmap = GlyphRenderer.Render(glyphId, size, row.ForeColor, AddressOf RuntimeBootstrap.OpenBundledAsset)
                Using g As Graphics = Graphics.FromImage(composed)
                    g.Clear(Color.Transparent)
                    g.DrawImage(glyph, New Rectangle(0, 0, size, size), 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel)
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
#If Not NETFRAMEWORK Then
        Dim theme As ChromeTheme = RecipientsOverlayTheme()
        Dim shellFill As Color = theme.PanelFill
#Else
        Dim shellFill As Color = Color.FromArgb(32, 32, 32)
#End If
        Dim overlay As New RecipientsOverlayWindow With {
            .Name = Recipients_Overlay_Panel_Name,
            .BackColor = shellFill,
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
        ' The move zone says its verb too, at its end, dimmed (125 UX-332): the copy zone had a word and the move
        ' zone, which is most of the row, had only its tooltip. Drawn only while the folder still has room.
        Dim move_Verb As String = Localization.T("переместить")
        copy_Caption = Localization.T("копия")
        If move_Verb.Length > 0 Then move_Verb = Char.ToUpperInvariant(move_Verb(0)) & move_Verb.Substring(1)
        If copy_Caption.Length > 0 Then copy_Caption = Char.ToUpperInvariant(copy_Caption(0)) & copy_Caption.Substring(1)
        copyW = Math.Max(RecipientPx(42), MeasureRowText(copy_Caption, ui_Font) + RecipientPx(16) +
                         If(recipients_Row_Glyphs, RecipientsOverlayRows.GlyphSide(rowH) + RecipientPx(ButtonGlyphs.GapLogicalSize), 0))
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
            .BackColor = shellFill,
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
            .BackColor = shellFill,
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
#If Not NETFRAMEWORK Then
            ' The delete row stands apart from the folder rows: the one row that destroys is not flush under the
            ' last folder, one slip of the pointer from "move there" (125 UX-332).
            If isDelete Then y += RecipientsOverlayRows.DeleteRowGap(rows.Count - 1, rowH)
#End If
#If Not NETFRAMEWORK Then
            Dim deleteFill As Color = If(ThemeService.IsDark, Color.FromArgb(96, 32, 32), Color.FromArgb(250, 224, 224))
            Dim deleteHover As Color = If(ThemeService.IsDark, Color.FromArgb(140, 44, 44), Color.FromArgb(240, 196, 196))
            Dim deletePress As Color = If(ThemeService.IsDark, Color.FromArgb(170, 50, 50), Color.FromArgb(230, 172, 172))
#Else
            Dim deleteFill As Color = Color.FromArgb(96, 32, 32)
            Dim deleteHover As Color = Color.FromArgb(140, 44, 44)
            Dim deletePress As Color = Color.FromArgb(170, 50, 50)
#End If
            Dim b As New NonFocusButton() With {
                .Text = r.Item2,
                .Tag = r.Item1,
                .Font = ui_Font,
                .FlatStyle = FlatStyle.Flat,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Padding = New Padding(RecipientPx(6), 0, RecipientPx(8), 0),
                .AutoEllipsis = True,
                .TabStop = False,
                .UseVisualStyleBackColor = False,
                .Location = New Point(0, y),
                .Size = New Size(If(showCopy, rowW - copyW, rowW), rowH),
                .ForeColor = If(isDelete, RecipientDeleteRowInk(), RecipientRowInk()),
                .BackColor = If(isDelete, deleteFill, RecipientRowFill(r.Item1))
            }
            b.FlatAppearance.BorderSize = 0
#If Not NETFRAMEWORK Then
            b.RowFont = ui_Font
            If Not isDelete Then
                b.TrailingText = move_Verb
                b.MinimumCaptionWidth = RecipientPx(96)
            End If
            ' Only the delete row carries a picture. The folder rows used to start with the catalog's folder-and-arrow
            ' on every line: one drawing repeated down the table, before a path that already says "folder" and a verb
            ' that says the rest - symbols with nothing left to tell.
            If recipients_Row_Glyphs AndAlso isDelete Then ApplyRecipientRowGlyph(b, "action.delete")
#End If
            ' Hover deepens the row's own blend (129 §3.3); only the DEL row keeps its own reds.
            b.FlatAppearance.MouseOverBackColor = If(isDelete, deleteHover, RecipientRowHoverFill(r.Item1))
            b.FlatAppearance.MouseDownBackColor = If(isDelete, deletePress, RecipientRowHoverFill(r.Item1))
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
                Dim copyFill As Color = If(theme.IsHighContrast, theme.PanelFill,
                                           If(ThemeService.IsDark, Color.FromArgb(38, 62, 38), Color.FromArgb(224, 240, 224)))
                Dim copyHover As Color = If(theme.IsHighContrast, theme.PanelFill,
                                            If(ThemeService.IsDark, Color.FromArgb(52, 88, 52), Color.FromArgb(204, 230, 204)))
                Dim copyPress As Color = If(theme.IsHighContrast, theme.PanelFill,
                                            If(ThemeService.IsDark, Color.FromArgb(66, 112, 66), Color.FromArgb(184, 220, 184)))
                Dim copyInk As Color = If(theme.IsHighContrast, theme.Foreground,
                                          If(ThemeService.IsDark, Color.Gainsboro, RecipientRowInk()))
                Dim c As New NonFocusButton() With {
                    .Text = copy_Caption,
                    .Tag = r.Item1,
                    .Font = ui_Font,
                    .FlatStyle = FlatStyle.Flat,
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .Padding = New Padding(RecipientPx(4), 0, RecipientPx(4), 0),
                    .AutoEllipsis = True,
                    .TabStop = False,
                    .UseVisualStyleBackColor = False,
                    .Location = New Point(rowW - copyW, y),
                    .Size = New Size(copyW, rowH),
                    .ForeColor = copyInk,
                    .BackColor = copyFill
                }
                c.Name = Recipients_Copy_Row_Name
                c.RowFont = ui_Font
                If recipients_Row_Glyphs Then ApplyRecipientRowGlyph(c, "action.copy")
                c.FlatAppearance.BorderSize = 0
                c.FlatAppearance.MouseOverBackColor = copyHover
                c.FlatAppearance.MouseDownBackColor = copyPress
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
        Dim theme As ChromeTheme = chrome_Base_Theme
        If (Not ThemeService.IsDark OrElse Not ChromeTheme.IsDark(theme.PanelFill)) AndAlso Not theme.IsHighContrast Then
            theme = ChromeTheme.FromScheme(ThemePalette.ForDark(False).SurfaceWindow,
                                          ThemePalette.ForDark(False).TextPrimary,
                                          False)
        End If
        If RecipientsOverlayInkInverted() Then Return theme.WithInvertedInk()
        Return theme
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
        If Not ChromeTheme.IsDark(theme.PanelFill) Then Return RecipientPalette.ColorFor(slot, False)
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
        If Not ChromeTheme.IsDark(theme.PanelFill) Then Return RecipientPalette.HoverColorFor(slot, False)
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
        If Not ChromeTheme.IsDark(theme.PanelFill) Then
            Return If(RecipientsOverlayInkInverted(),
                      ChromeTheme.InvertedTone(ThemePalette.ForDark(False).TextPrimary),
                      ThemePalette.ForDark(False).TextPrimary)
        End If
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
        Dim theme As ChromeTheme = RecipientsOverlayTheme()
        If theme.IsHighContrast Then Return theme.DestructiveForeground
        Return If(ChromeTheme.IsDark(theme.PanelFill), theme.DestructiveForeground, Color.FromArgb(160, 20, 20))
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
                    Dim delFill As Color = If(delete_Allowed,
                                              If(ChromeTheme.IsDark(theme.PanelFill), theme.DestructiveRest, Color.FromArgb(250, 224, 224)),
                                              theme.PanelFill)
                    Dim delHover As Color = If(ChromeTheme.IsDark(theme.PanelFill), theme.DestructiveHover, Color.FromArgb(240, 196, 196))
                    Dim delPress As Color = If(ChromeTheme.IsDark(theme.PanelFill), theme.DestructivePressed, Color.FromArgb(230, 172, 172))
                    row.ForeColor = If(delete_Allowed, RecipientDeleteRowInk(), theme.DisabledForeground)
                    row.BackColor = delFill
                    row.FlatAppearance.MouseOverBackColor = If(delete_Allowed AndAlso Not theme.IsHighContrast, delHover, theme.PanelFill)
                    row.FlatAppearance.MouseDownBackColor = If(delete_Allowed AndAlso Not theme.IsHighContrast, delPress, theme.PanelFill)
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
                                       If(ChromeTheme.IsDark(theme.PanelFill),
                                          ChromeTheme.Blend(theme.PanelFill, theme.Accent, ChromeMetrics.HoverPercent / 100.0F),
                                          Color.FromArgb(224, 240, 224)))
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
                                                            If(is_Copy_Zone,
                                                               If(ChromeTheme.IsDark(theme.PanelFill), theme.ItemHover, Color.FromArgb(204, 230, 204)),
                                                               RecipientRowHoverFill(slot)))
                row.FlatAppearance.MouseDownBackColor = If(unusable OrElse theme.IsHighContrast, row.BackColor,
                                                            If(is_Copy_Zone,
                                                               If(ChromeTheme.IsDark(theme.PanelFill), theme.ItemPressed, Color.FromArgb(184, 220, 184)),
                                                               RecipientRowHoverFill(slot)))
                ' The folder rows carry no picture (see BuildRecipientsOverlay); the copy zone keeps its sheets.
                If recipients_Row_Glyphs AndAlso is_Copy_Zone Then ApplyRecipientRowGlyph(row, "action.copy")
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
        ' The native move loop owns geometry until drop; timer/resize callbacks must not
        ' persist its intermediate position as a recovery or move it under the pointer.
        If recipients_Drag_Active Then Return
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

        ' L0281 T-6 (Q281-3): a placement only PLACES. The effective rectangle of a free table is the intended place
        ' fitted into the allowed area as it is now; nothing is written back (219 §6's repair is retired), so a
        ' window made smaller and larger again returns the table to where the user left it.
        Dim onScreen As Point = RecipientsOverlayPlacedRect().Location
        If recipients_Overlay.Location <> onScreen Then recipients_Overlay.Location = onScreen
    End Sub

    ''' <summary>Where the overlay sits over the media surface, in screen coordinates - the one
    ''' geometry <see cref="PositionRecipientsOverlay"/> places by and the modern build's reveal
    ''' hot zone grows from. No visibility side effects: the overlay's window may be hidden while
    ''' this runs, and NOTHING is written (L0281 T-6, Q281-3): a free table is its intended place resolved and
    ''' fitted afresh in the allowed area; the anchored table is <see cref="RecipientsOverlayAnchoredRect"/>.</summary>
    Private Function RecipientsOverlayPlacedRect() As Rectangle
        If recipients_Overlay Is Nothing OrElse panel_Media Is Nothing Then Return Rectangle.Empty
#If Not NETFRAMEWORK Then
        ' Mid-drag the window is the truth (219 §2): the tick's containment checks and the reveal
        ' zone follow the pointer, and nothing re-places the panel out from under it.
        If recipients_Drag_Active Then Return New Rectangle(recipients_Overlay.Location, recipients_Overlay.Size)
        ' A free table (L0281 T-6): the stored place - the shared corner:dx,dy value, or an old 219 point read as
        ' TL:x,y - resolved in the allowed area as it is NOW and fitted, at every placement. The place is the
        ' intended one; the fit is the effective one and never goes back into the preferences.
        Dim place As PanelPlace
        If RecipientsOverlayFreePlace(place) Then
            Dim factor As Single = ChromeScale()
            Return FloatingPanelPlacement.Effective(place, RecipientsOverlayAllowedArea(factor), recipients_Overlay.Size,
                                                    factor, Localization.IsRightToLeft())
        End If
#End If
        Return RecipientsOverlayAnchoredRect()
    End Function

    ''' <summary>Where the corner choice puts the table over the media surface, in screen coordinates: the anchored
    ''' state (219 §3), the coarse move. net48 only ever has this one.</summary>
    Private Function RecipientsOverlayAnchoredRect() As Rectangle
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
#If Not NETFRAMEWORK Then
        ' The anchored corner (219 §3) - the coarse move, one honest function, not a copy that
        ' can drift.
        Dim offset As Point = RecipientsOverlayPlacement.CornerOffset(RecipientsOverlayPosition(),
            panel_Media.ClientSize, recipients_Overlay.Size, margin, topOffset)
        x = offset.X
        y = offset.Y
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
        ' The anchored table stays as it was (L0281 T-6): a corner anchor whose header would fall outside the media
        ' area (chrome rows taller than a short window) is still carried inside by the shortest translation, margins
        ' included - computed afresh at every placement and, unlike 219 §6, never written anywhere.
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

    ''' <summary>The allowed rectangle a drag in progress is clamped into (screen coordinates), taken once at the
    ''' press: nothing resizes the window while the native move loop owns the pointer.</summary>
    Private recipients_Drag_Allowed As Rectangle
    Private recipients_Drag_StartIsRight As Boolean

    ''' <summary>The allowed rectangle of the table (L0281 §3.3, T-6): exactly the chrome panels' one,
    ''' <see cref="ChromeAllowedArea"/> - the media area inside the window's client area, inside the visible part of
    ''' the screen, inset by <see cref="ChromeMetrics.PanelEdgeInset"/> (0, flush against any edge like the rows and
    ''' the status strip). 219's 8 px margin (<c>RecipientPx(8)</c>) stays the margin of the ANCHORED corners only; it
    ''' existed in the free state only inside the repair pull-back, which this slice retires. Keeping the area
    ''' un-inset also keeps an old 219 point (measured from the media area's top-left) reading as <c>TL:x,y</c> at
    ''' the very same visual spot. One place to change if the owner wants air at the edges.</summary>
    Private Function RecipientsOverlayAllowedArea(factor As Single) As Rectangle
        Return ChromeAllowedArea(factor)
    End Function

    ''' <summary>True and the table's intended free place; False when the table stands at its corner choice. The
    ''' reader rule of <see cref="RecipientsOverlayPlacement.TryReadPlace"/> applied to the stored preferences.</summary>
    Private Function RecipientsOverlayFreePlace(ByRef place As PanelPlace) As Boolean
        place = New PanelPlace(PanelCorner.TopLeft, 0, 0)
        Dim prefs As ModernViewerPreferences = modern_Preferences
        If prefs Is Nothing Then Return False
        Return RecipientsOverlayPlacement.TryReadPlace(prefs.RecipientsOverlayPlace, prefs.RecipientsOverlayHasCustomPosition,
                                                       prefs.RecipientsOverlayCustomX, prefs.RecipientsOverlayCustomY, place)
    End Function

    ''' <summary>One proposed rectangle of the native move loop (WM_MOVING) through the fit, BEFORE the window is
    ''' placed: no pixel of the table is ever outside the allowed rectangle, not even for a frame (R2, Q281-3).</summary>
    Private Function RecipientsOverlayFitMoving(proposed As Rectangle) As Rectangle
        Return FloatingPanelPlacement.Fit(proposed, recipients_Drag_Allowed, recipients_Drag_StartIsRight)
    End Function

    ''' <summary>The grip's press (219 §2, L0281 T-6): the whole window follows the pointer through Windows'
    ''' own move loop - the same drag the window-commands panel's grip runs - never activating it,
    ''' so the viewer keeps the keyboard. The loop is CONSTRAINED, not replaced: the overlay window answers
    ''' WM_MOVING by fitting every proposed rectangle into the allowed area (live no-pixel-outside clamp). The
    ''' reveal rule of 117 Ф-2 holds for the whole drag and the pin state is not changed; the drop is decided
    ''' once, here, when the loop returns (<see cref="RecipientsOverlayFinishDrag"/>).</summary>
    Private Sub RecipientsOverlayBeginDrag()
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed OrElse
           Not recipients_Overlay.IsHandleCreated Then Return
        Dim host As RecipientsOverlayWindow = recipients_Overlay
        Dim started As New Rectangle(host.Location, host.Size)
        recipients_Drag_Allowed = RecipientsOverlayAllowedArea(ChromeScale())
        recipients_Drag_StartIsRight = Localization.IsRightToLeft()
        recipients_Drag_Active = True
        host.MovingFit = AddressOf RecipientsOverlayFitMoving
        Try
            ReleaseCapture()
            Dim pointer As Point = Cursor.Position
            Dim lParam As New IntPtr((pointer.Y << 16) Or (pointer.X And &HFFFF))
            SendMessage(host.Handle, WmNcLButtonDown, New IntPtr(HtCaption), lParam)
        Finally
            recipients_Drag_Active = False
            host.MovingFit = Nothing
        End Try
        If recipients_Overlay Is Nothing OrElse recipients_Overlay.IsDisposed Then Return
        RecipientsOverlayFinishDrag(started, New Rectangle(recipients_Overlay.Location, recipients_Overlay.Size))
    End Sub

    ''' <summary>A finished drag decided like every floating panel's (L0281 §3.5): nothing to write when it ended where
    ''' it began, the place cleared when it ended within the snap distance of the corner anchor, a refusal when no free
    ''' fit clear of the window commands and the visible transport bar exists (the table returns to where the drag began,
    ''' nothing written), otherwise the new place - stored once. The window is placed afresh in every case, so a refused
    ''' or unchanged drag puts it back where it was.</summary>
    Private Sub RecipientsOverlayFinishDrag(started As Rectangle, dropped As Rectangle)
        Try
            Dim factor As Single = ChromeScale()
            Dim allowed As Rectangle = RecipientsOverlayAllowedArea(factor)
            Dim result As Rectangle
            Select Case FloatingPanelPlacement.DecideDrop(started, dropped, RecipientsOverlayAnchoredRect(), allowed,
                                                          ChromeProtectedRects(), UiScale.Px(ChromeMetrics.AnchorSnap, factor),
                                                          Localization.IsRightToLeft(), result)
                Case PanelDropOutcome.Placed
                    StoreRecipientsOverlayPlace(result, allowed, factor)
                Case PanelDropOutcome.Anchor
                    ClearRecipientsOverlayPlace()
            End Select
        Catch ex As Exception
            AppFileLogger.LogException("Recipients overlay: deciding a drop", ex)
        End Try
        PositionRecipientsOverlay()
    End Sub

    ''' <summary>Writes a dropped table's place (L0281 §5, Q281-3): the shared <c>corner:dx,dy</c> value, and the old
    ''' triple kept meaningful for an older build - HasCustomPosition = True with X and Y the dropped rectangle's top-left
    ''' in logical px from the media area (219 §3). One write per drop; the same place again writes nothing.</summary>
    Private Sub StoreRecipientsOverlayPlace(rect As Rectangle, allowed As Rectangle, factor As Single)
        If panel_Media Is Nothing Then Return
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim text As String = FloatingPanelPlacement.Encode(FloatingPanelPlacement.FromRectangle(rect, allowed, factor))
        Dim origin As Point = panel_Media.PointToScreen(New Point(0, 0))
        Dim legacy As Point = RecipientsOverlayPlacement.ToLogicalPoint(New Point(rect.X - origin.X, rect.Y - origin.Y), factor)
        If prefs.RecipientsOverlayPlace = text AndAlso prefs.RecipientsOverlayHasCustomPosition AndAlso
           prefs.RecipientsOverlayCustomX = legacy.X AndAlso prefs.RecipientsOverlayCustomY = legacy.Y Then Return
        prefs.RecipientsOverlayPlace = text
        prefs.RecipientsOverlayCustomX = legacy.X
        prefs.RecipientsOverlayCustomY = legacy.Y
        prefs.RecipientsOverlayHasCustomPosition = True
        SaveRecipientsOverlayPreferences()
    End Sub

    ''' <summary>The table is anchored at its corner choice again: the place and the old triple are cleared together
    ''' (nothing is written when there was nothing to clear).</summary>
    Private Sub ClearRecipientsOverlayPlace()
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        If Not prefs.RecipientsOverlayHasCustomPosition AndAlso String.IsNullOrEmpty(prefs.RecipientsOverlayPlace) Then Return
        prefs.RecipientsOverlayHasCustomPosition = False
        prefs.RecipientsOverlayPlace = ""
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
        recipients_Edit_Active = True
        Try
            ShowSettingsWindowAtRecipientsGrid()
        Finally
            recipients_Edit_Active = False
            recipients_LastActivity = chrome_Clock.ElapsedMilliseconds
        End Try
    End Sub

    Private recipients_Edit_Active As Boolean

    ''' <summary>The settings' reset row (219 §5): clears the custom point - and, since L0281 T-6, the shared place
    ''' with it - and repositions the overlay at once - no restart, no settings reopen - from any state (visible,
    ''' auto-hidden, dragged somewhere the user forgot). Idempotent: with the panel already at its anchor it is
    ''' an honest no-op that still says so through the Notify line.</summary>
    Friend Sub ResetRecipientsOverlayPosition()
        Dim prefs As ModernViewerPreferences = GetModernPreferences()
        Dim hadCustom As Boolean = prefs.RecipientsOverlayHasCustomPosition OrElse
            Not String.IsNullOrEmpty(prefs.RecipientsOverlayPlace)
        prefs.RecipientsOverlayHasCustomPosition = False
        prefs.RecipientsOverlayPlace = ""
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

        ''' <summary>A word drawn dimmed at the row's end (the move zone's verb, 125 UX-332); empty = none. It is
        ''' not part of <c>Text</c>: the caption keeps being the slot and the folder, and it is the first thing a
        ''' narrow row gives up.</summary>
        Public Property TrailingText As String = ""

        ''' <summary>The room the caption must keep before the trailing word may be drawn at all.</summary>
        Public Property MinimumCaptionWidth As Integer

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
                g.DrawImage(Image, box, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel)
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
                Dim areaLeft As Single = left
                Dim areaRight As Single = Width - Padding.Right
                Dim radius As Integer = ChromeInkOutline.RadiusFor(DeviceDpi / 96.0F)
                ' The verb at the row's far end, dimmed, while the folder keeps its room (125 UX-332). It is drawn
                ' at the end the text finishes at: the left in a right-to-left language.
                If Not String.IsNullOrEmpty(TrailingText) Then
                    Using verbFormat As New StringFormat(StringFormat.GenericTypographic)
                        verbFormat.LineAlignment = StringAlignment.Center
                        verbFormat.FormatFlags = verbFormat.FormatFlags Or StringFormatFlags.NoWrap
                        If RightToLeft = RightToLeft.Yes Then verbFormat.FormatFlags = verbFormat.FormatFlags Or StringFormatFlags.DirectionRightToLeft
                        Dim verbWidth As Integer = CInt(Math.Ceiling(g.MeasureString(TrailingText, captionFont, Integer.MaxValue, verbFormat).Width)) + 1
                        Dim verbGap As Integer = Math.Max(4, DeviceDpi \ 16)
                        If RecipientsOverlayRows.ShowsMoveVerb(Width, CInt(left), Padding.Right, verbWidth + verbGap, MinimumCaptionWidth) Then
                            Dim verbInk As Color = ChromeTheme.Blend(fill, ForeColor, 0.7F)
                            Dim verbLeft As Single
                            If RightToLeft = RightToLeft.Yes Then
                                verbLeft = areaLeft
                                areaLeft += verbWidth + verbGap
                            Else
                                verbLeft = areaRight - verbWidth
                                areaRight -= verbWidth + verbGap
                            End If
                            ChromeInkOutline.DrawText(g, TrailingText, captionFont, New RectangleF(verbLeft, 0, verbWidth, Height), verbInk, verbFormat, radius)
                        End If
                    End Using
                End If
                Dim area As New RectangleF(areaLeft, 0, Math.Max(0, areaRight - areaLeft), Height)
                ChromeInkOutline.DrawText(g, Text, captionFont, area, ForeColor, format, radius)
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
            Using bgBrush As New SolidBrush(theme.PanelFill)
                g.FillRectangle(bgBrush, r)
            End Using
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
            Dim restInk As Color = ChromeTheme.Blend(theme.PanelFill, theme.Foreground, 0.55F)
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
            Dim restInk As Color = ChromeTheme.Blend(theme.PanelFill, theme.Foreground, If(checkedState, 0.8F, 0.55F))
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
                    g.DrawImage(glyph, bounds, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel)
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

        Private Const WM_MOVING As Integer = &H216

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MovingRect
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        Private movingFaultLogged As Boolean

        Public Sub New()
            MyBase.New(False)
        End Sub

        ''' <summary>The fit the native move loop's proposed rectangles pass through while a header drag runs (L0281 T-6):
        ''' set by the owner for the length of the drag, Nothing otherwise - a window move that is not the header drag
        ''' (none exists today) is never constrained.</summary>
        Public Property MovingFit As Func(Of Rectangle, Rectangle)

        ''' <summary>WM_MOVING is sent by the system's move loop with a pointer to the rectangle the window is ABOUT to
        ''' take (screen coordinates); rewriting it before returning TRUE makes the loop place the window there instead,
        ''' so a pixel outside the allowed area is never drawn, even for one frame.</summary>
        Protected Overrides Sub WndProc(ByRef m As Message)
            Dim fit As Func(Of Rectangle, Rectangle) = MovingFit
            If m.Msg = WM_MOVING AndAlso fit IsNot Nothing AndAlso m.LParam <> IntPtr.Zero Then
                Try
                    Dim r As MovingRect = Marshal.PtrToStructure(Of MovingRect)(m.LParam)
                    Dim proposed As New Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
                    Dim fitted As Rectangle = fit(proposed)
                    If fitted <> proposed Then
                        Marshal.StructureToPtr(New MovingRect With {
                            .Left = fitted.Left, .Top = fitted.Top, .Right = fitted.Right, .Bottom = fitted.Bottom},
                            m.LParam, False)
                    End If
                    m.Result = New IntPtr(1)
                    Return
                Catch ex As Exception
                    ' A fault must not end the drag: the rectangle stays as proposed and the drop decision
                    ' (DecideDrop) fits it once more. Logged once per window, not per mouse move.
                    If Not movingFaultLogged Then
                        movingFaultLogged = True
                        AppFileLogger.LogException("Recipients overlay: constraining the move", ex)
                    End If
                End Try
            End If
            MyBase.WndProc(m)
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
