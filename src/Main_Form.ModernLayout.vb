Option Strict On

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Modern responsive layout for Main_Form.
'
' The original UI hand-placed every toolbar control with absolute pixel math
' (see Main_Form.UILayout.vb history). This partial replaces that model:
'   * flow_Toolbar  (Panel,          Dock=Top)    - all chrome, two rows, each
'                                                    with a right-edge overflow
'                                                    menu (Main_Form.ToolbarOverflow.vb)
'   * panel_Status  (FlowLayoutPanel, Dock=Bottom) - status / current-file
'   * panel_Media   (Panel,          Dock=Fill)    - the media surface
'
' The existing Button/Label/ComboBox/CheckBox instances are reparented at
' runtime, so every "Handles ..." clause and the colour-scheme loop keep working
' unchanged. The media surface lives in panel_Media, so picture-box geometry is
' panel-relative (the old "lbl_Status.Bottom" top offset collapses to 0) and
' full-screen is just a matter of hiding the two chrome panels.
Partial Public Class Main_Form

    Friend WithEvents panel_Media As Panel
    ' Named flow_Toolbar for history, but since the overflow rework it is a plain
    ' Panel laid out by hand (see Main_Form.ToolbarOverflow.vb): two rows, each
    ' collapsing controls that don't fit into a right-edge "more" dropdown instead
    ' of wrapping onto extra rows (which used to push down over the image).
    Friend WithEvents flow_Toolbar As Panel
    Friend WithEvents panel_Status As FlowLayoutPanel

    Private modern_Layout_Built As Boolean = False

    ' --- Dark title bar (DWM) ---------------------------------------------
    <DllImport("dwmapi.dll", PreserveSig:=True)>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attr As Integer, ByRef attrValue As Integer, attrSize As Integer) As Integer
    End Function

    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE_OLD As Integer = 19

    ''' <summary>
    ''' Reparents the existing Designer controls into docked container panels.
    ''' Safe to call once; further calls are ignored.
    ''' </summary>
    Friend Sub BuildModernLayout()
        If modern_Layout_Built Then Return
        modern_Layout_Built = True

        Me.SuspendLayout()

        panel_Media = New Panel With {
            .Name = "panel_Media",
            .Dock = DockStyle.Fill,
            .Margin = New Padding(0)
        }
        flow_Toolbar = New Panel With {
            .Name = "flow_Toolbar",
            .Dock = DockStyle.Top,
            .Padding = New Padding(2),
            .Margin = New Padding(0)
        }
        panel_Status = New FlowLayoutPanel With {
            .Name = "panel_Status",
            .Dock = DockStyle.Bottom,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True,
            .Padding = New Padding(4, 1, 4, 1),
            .Margin = New Padding(0)
        }

        ' Media surface (preserve original front-to-back z-order:
        ' Picture_Box_1 frontmost, then Picture_Box_2, Web_Browser, help label).
        ReparentInto(panel_Media, {CType(lbl_Help_Info, Control), Web_Browser, Picture_Box_2, Picture_Box_1})

        ' Toolbar, left-to-right reading order. The first four are the "priority"
        ' core that stays visible and lets the folder box shrink; everything after
        ' collapses into the right-edge overflow menu when it doesn't fit (see
        ' Main_Form.ToolbarOverflow.vb / LayoutToolbar).
        Dim toolbar_Order As Control() = {
            chkbox_Top_Most, cmbox_Sort, lbl_Folder, cmbox_Media_Folder,
            btn_choose_file, btn_Select_Folder, btn_Review, btn_Panel,
            btn_Full_Screen, lbl_Slideshow_Time, btn_Language, lbl_Info,
            btn_RecentFiles, lbl_File_Number, btn_Prev_File, btn_Next_File,
            btn_Next_Random, btn_Random_Slideshow, btn_Slideshow,
            btn_Move_Table, btn_Rename, bt_Delete, lbl_Zoom}
        ReparentInto(flow_Toolbar, toolbar_Order)

        ' Status line at the bottom.
        ReparentInto(panel_Status, {CType(lbl_Current_File, Control), lbl_Status})

        ' OCR + Translation toolbar button (created in code, not the Designer);
        ' added here so the styling/recolour passes below pick it up uniformly.
        BuildOcrToolbarControls(flow_Toolbar)

        ' "Translate in browser" companion button (doc-html-translate), right after
        ' the OCR button so it joins the overflow set and the uniform styling.
        BuildBrowserTranslateToolbarControls(flow_Toolbar)

#If Not NETFRAMEWORK Then
        ' Audio/subtitle track picker - collapsed out of the row unless the playing
        ' video actually offers a choice (O-ISO-7).
        BuildVideoTracksToolbarControls(flow_Toolbar)
#End If

        ' Right-edge "more" button + the priority/overflow partitioning that
        ' LayoutToolbar() uses. Built after the OCR button so it is part of the
        ' overflow set. Must precede ApplyModernStyling so the button is styled
        ' with the rest of the chrome.
        BuildToolbarOverflow(flow_Toolbar)

        ' Add Fill first (back of z-order) so the docked strips reserve their
        ' space and the media panel takes the remainder.
        Me.Controls.Add(panel_Media)
        Me.Controls.Add(panel_Status)
        Me.Controls.Add(flow_Toolbar)

        ApplyModernStyling()

        Me.ResumeLayout(True)

        ' First manual layout (also fixes the panel height to a single row).
        LayoutToolbar()
    End Sub

    Private Sub ReparentInto(target As Control, controls As Control())
        For Each c As Control In controls
            If c IsNot Nothing Then
                If c.Parent IsNot Nothing Then c.Parent.Controls.Remove(c)
                target.Controls.Add(c)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Flat, modern styling for the chrome + clearer glyphs on the buttons that
    ''' used cryptic ASCII captions. Tooltips (set in InitializeTooltips) remain
    ''' the discoverability layer.
    ''' </summary>
    Private Sub ApplyModernStyling()
        Dim ui_Font As New Font("Segoe UI", 9.0F, FontStyle.Regular)
        Dim glyph_Font As New Font("Segoe UI Symbol", 10.0F, FontStyle.Regular)

        ' Transparent so the toolbar can float over the full-screen image; in
        ' windowed mode it shows the form's themed background between buttons.
        flow_Toolbar.BackColor = Color.Transparent

        ' Folder combo gets room for paths; sort combo stays compact.
        cmbox_Media_Folder.Font = ui_Font
        cmbox_Media_Folder.Width = 320
        cmbox_Sort.Font = ui_Font
        cmbox_Sort.Width = 70

        ' One uniform row height so every button/combo lines up - no vertical
        ' "jumping" from glyph-vs-text font metrics.
        Dim row_H As Integer = Math.Max(cmbox_Media_Folder.PreferredHeight, 24)
        toolbar_Row_Height = row_H

        For Each c As Control In flow_Toolbar.Controls
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then
                b.Font = ui_Font
                b.FlatStyle = FlatStyle.Flat
                b.FlatAppearance.BorderSize = 0
                b.UseVisualStyleBackColor = False
                b.TextAlign = ContentAlignment.MiddleCenter
                b.ImageAlign = ContentAlignment.MiddleCenter
                b.AutoSize = True
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink
                ' Lock the height to row_H (width still auto-fits the caption).
                b.MinimumSize = New Size(0, row_H)
                b.MaximumSize = New Size(0, row_H)
                b.Margin = New Padding(2, 1, 2, 1)
            ElseIf TypeOf c Is ComboBox Then
                c.Font = ui_Font
                c.Margin = New Padding(2, 1, 2, 1)
            ElseIf TypeOf c Is Label Then
                c.Font = ui_Font
                ' Vertically centre the (shorter) label text within the row.
                c.Margin = New Padding(4, Math.Max(1, (row_H - c.PreferredSize.Height) \ 2), 4, 0)
            ElseIf TypeOf c Is CheckBox Then
                c.Font = ui_Font
                c.Margin = New Padding(2, 2, 6, 2)   ' always-on-top: pinned top-left
            End If
        Next

        ' Clearer glyphs for the formerly-cryptic buttons (height already fixed
        ' above, so the larger glyph font doesn't change the row).
        btn_Review.Text = "⟳"
        btn_Panel.Text = "▦"
        btn_Full_Screen.Text = "⛶"
        btn_Slideshow.Text = "▶▶"
        btn_Random_Slideshow.Text = "⤮▶"
        btn_Next_Random.Text = "⚄"
        btn_Rename.Text = "✎"
        btn_RecentFiles.Text = "▾"
        For Each gb As Button In New Button() {btn_Review, btn_Panel, btn_Full_Screen,
                                               btn_Slideshow, btn_Random_Slideshow,
                                               btn_Next_Random, btn_Rename, btn_RecentFiles,
                                               btn_Overflow, btn_Overflow_2}
            If gb IsNot Nothing Then gb.Font = glyph_Font
        Next

        For Each c As Control In panel_Status.Controls
            c.Font = ui_Font
            c.Margin = New Padding(6, 1, 6, 1)
        Next
    End Sub

    ''' <summary>
    ''' Full-screen overlay mode: hosts the toolbar inside panel_Media, floated
    ''' over the (full-screen) image with a see-through background. In windowed
    ''' mode the toolbar is docked at the top of the form instead.
    ''' </summary>
    Friend Sub SetToolbarOverlay(overlay As Boolean)
        If flow_Toolbar Is Nothing OrElse panel_Media Is Nothing Then Return

        If overlay Then
            If flow_Toolbar.Parent IsNot panel_Media Then
                If flow_Toolbar.Parent IsNot Nothing Then flow_Toolbar.Parent.Controls.Remove(flow_Toolbar)
                flow_Toolbar.Dock = DockStyle.None
                flow_Toolbar.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
                panel_Media.Controls.Add(flow_Toolbar)
            End If
            flow_Toolbar.Location = Point.Empty
            flow_Toolbar.Width = panel_Media.ClientSize.Width
            flow_Toolbar.BringToFront()
        Else
            If flow_Toolbar.Parent IsNot Me Then
                If flow_Toolbar.Parent IsNot Nothing Then flow_Toolbar.Parent.Controls.Remove(flow_Toolbar)
                flow_Toolbar.Dock = DockStyle.Top
                Me.Controls.Add(flow_Toolbar)
                flow_Toolbar.BringToFront()
            End If
        End If

        ApplyToolbarRegion(overlay)
    End Sub

    ''' <summary>
    ''' Clips the toolbar window to just the button/combo rectangles so the gaps
    ''' between them are genuinely cut out - the full-screen image shows straight
    ''' through (WinForms "transparent" backgrounds only reveal the parent's
    ''' fill, never a sibling control, so a Region with holes is the real fix).
    ''' Passing False removes the clip (windowed mode = normal solid bar).
    ''' </summary>
    Friend Sub ApplyToolbarRegion(useHoles As Boolean)
        If flow_Toolbar Is Nothing Then Return

        Dim previous As Region = flow_Toolbar.Region

        If Not useHoles Then
            If previous IsNot Nothing Then
                flow_Toolbar.Region = Nothing
                previous.Dispose()
            End If
            Return
        End If

        ' Lay the toolbar out at its current width first so child bounds are final.
        flow_Toolbar.PerformLayout()

        Dim holes As New Region()
        holes.MakeEmpty()
        For Each c As Control In flow_Toolbar.Controls
            If c.Visible Then
                Dim r As Rectangle = c.Bounds
                r.Inflate(1, 1)   ' 1px halo: avoids edge-clipping but keeps the gap see-through
                holes.Union(r)
            End If
        Next

        flow_Toolbar.Region = holes
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

    ''' <summary>
    ''' Re-applies the active colour scheme to the chrome. Replaces the old
    ''' "For Each ctrl In Me.Controls" loop, recursing into the new container
    ''' panels so reparented controls are still recoloured.
    ''' </summary>
    Friend Sub RecolorChrome(back_Color As Color, opposite_Color As Color)
        If Not modern_Layout_Built Then Return

        Dim hover_Color As Color = BlendColor(back_Color, opposite_Color, 0.18F)

        ' Toolbar stays transparent (floats over the image in full-screen and
        ' shows the form's themed background when docked); only the buttons are
        ' opaque. The status bar is an opaque bottom strip.
        flow_Toolbar.BackColor = Color.Transparent
        panel_Status.BackColor = back_Color

        RecolorContainer(flow_Toolbar, back_Color, opposite_Color, hover_Color)
        RecolorContainer(panel_Status, back_Color, opposite_Color, hover_Color)

        ' Overflow dropdowns match the chrome.
        For Each m As ContextMenuStrip In New ContextMenuStrip() {overflow_Menu, overflow_Menu_2}
            If m IsNot Nothing Then
                m.BackColor = back_Color
                m.ForeColor = opposite_Color
            End If
        Next

        ApplyTitleBarTheme(IsDarkColor(back_Color))
    End Sub

    Private Sub RecolorContainer(parent As Control, back_Color As Color, opposite_Color As Color, hover_Color As Color)
        For Each ctrl As Control In parent.Controls
            Dim b As Button = TryCast(ctrl, Button)
            If b IsNot Nothing Then
                b.ForeColor = opposite_Color
                b.BackColor = back_Color
                b.FlatAppearance.MouseOverBackColor = hover_Color
                b.FlatAppearance.MouseDownBackColor = BlendColor(back_Color, opposite_Color, 0.30F)
            ElseIf TypeOf ctrl Is Label Then
                ctrl.ForeColor = opposite_Color
                ctrl.BackColor = Color.Transparent
            ElseIf TypeOf ctrl Is ComboBox Then
                ctrl.ForeColor = opposite_Color
                ctrl.BackColor = back_Color
            ElseIf TypeOf ctrl Is CheckBox Then
                ctrl.ForeColor = opposite_Color
                ctrl.BackColor = back_Color
            End If

            If ctrl.Controls.Count > 0 Then
                RecolorContainer(ctrl, back_Color, opposite_Color, hover_Color)
            End If
        Next
    End Sub

    Private Shared Function BlendColor(a As Color, b As Color, t As Single) As Color
        Return Color.FromArgb(
            ClampByte(a.R + (b.R - a.R) * t),
            ClampByte(a.G + (b.G - a.G) * t),
            ClampByte(a.B + (b.B - a.B) * t))
    End Function

    ''' <summary>Rounds to an Integer and clamps to 0..255 so Color.FromArgb can
    ''' never throw and CInt can never overflow on a stray/NaN input.</summary>
    Private Shared Function ClampByte(value As Single) As Integer
        If Single.IsNaN(value) Then Return 0
        If value <= 0F Then Return 0
        If value >= 255F Then Return 255
        Return CInt(value)
    End Function

    Private Shared Function IsDarkColor(c As Color) As Boolean
        ' Rec. 601 luma; < 128 is treated as a dark background.
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128.0
    End Function

    ''' <summary>Switch the native title bar between dark and light.</summary>
    Friend Sub ApplyTitleBarTheme(useDark As Boolean)
        If Not Me.IsHandleCreated Then Return
        Dim flag As Integer = If(useDark, 1, 0)
        ' Win10 2004+ uses attribute 20; older 10 builds used 19. Try both.
        If DwmSetWindowAttribute(Me.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, flag, 4) <> 0 Then
            DwmSetWindowAttribute(Me.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, flag, 4)
        End If
    End Sub

End Class
