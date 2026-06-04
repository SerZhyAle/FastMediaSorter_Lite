Option Strict On

Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

' Modern responsive layout for Main_Form.
'
' The original UI hand-placed every toolbar control with absolute pixel math
' (see Main_Form.UILayout.vb history). This partial replaces that model:
'   * flow_Toolbar  (FlowLayoutPanel, Dock=Top)    - all chrome, auto-wraps
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
    Friend WithEvents flow_Toolbar As FlowLayoutPanel
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
        flow_Toolbar = New FlowLayoutPanel With {
            .Name = "flow_Toolbar",
            .Dock = DockStyle.Top,
            .AutoSize = True,
            .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .WrapContents = True,
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

        ' Toolbar, left-to-right reading order. Row 2 (navigation/actions) is
        ' forced onto a new line at wide widths via a flow break after lbl_Info.
        Dim toolbar_Order As Control() = {
            chkbox_Top_Most, cmbox_Sort, lbl_Folder, cmbox_Media_Folder,
            btn_choose_file, btn_Select_Folder, btn_Review, btn_Panel,
            btn_Full_Screen, lbl_Slideshow_Time, btn_Language, lbl_Info,
            btn_RecentFiles, lbl_File_Number, btn_Prev_File, btn_Next_File,
            btn_Next_Random, btn_Random_Slideshow, btn_Slideshow,
            btn_Move_Table, btn_Rename, bt_Delete, lbl_Zoom}
        ReparentInto(flow_Toolbar, toolbar_Order)
        flow_Toolbar.SetFlowBreak(lbl_Info, True)

        ' Status line at the bottom.
        ReparentInto(panel_Status, {CType(lbl_Current_File, Control), lbl_Status})

        ' Add Fill first (back of z-order) so the docked strips reserve their
        ' space and the media panel takes the remainder.
        Me.Controls.Add(panel_Media)
        Me.Controls.Add(panel_Status)
        Me.Controls.Add(flow_Toolbar)

        ApplyModernStyling()

        Me.ResumeLayout(True)
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
        Dim glyph_Font As New Font("Segoe UI Symbol", 11.0F, FontStyle.Regular)

        For Each c As Control In flow_Toolbar.Controls
            c.Margin = New Padding(2)
            Dim b As Button = TryCast(c, Button)
            If b IsNot Nothing Then
                b.Font = ui_Font
                b.FlatStyle = FlatStyle.Flat
                b.FlatAppearance.BorderSize = 0
                b.AutoSize = True
                b.AutoSizeMode = AutoSizeMode.GrowAndShrink
                b.Padding = New Padding(6, 2, 6, 2)
                b.UseVisualStyleBackColor = False
                Continue For
            End If
            If TypeOf c Is ComboBox OrElse TypeOf c Is Label OrElse TypeOf c Is CheckBox Then
                c.Font = ui_Font
            End If
        Next

        For Each c As Control In panel_Status.Controls
            c.Font = ui_Font
            c.Margin = New Padding(6, 1, 6, 1)
        Next

        ' Give the folder combo room for paths; keep the sort combo compact.
        cmbox_Media_Folder.Width = 320
        cmbox_Media_Folder.Font = ui_Font
        cmbox_Sort.Width = 70
        cmbox_Sort.Font = ui_Font

        ' Clearer glyphs for the formerly-cryptic symbolic buttons.
        btn_Review.Font = glyph_Font : btn_Review.Text = "⟳"
        btn_Panel.Font = glyph_Font : btn_Panel.Text = "▦"
        btn_Full_Screen.Font = glyph_Font : btn_Full_Screen.Text = "⛶"
        btn_Slideshow.Font = glyph_Font : btn_Slideshow.Text = "▶▶"
        btn_Random_Slideshow.Font = glyph_Font : btn_Random_Slideshow.Text = "⤮▶"
        btn_Next_Random.Font = glyph_Font : btn_Next_Random.Text = "⚄"
        btn_Rename.Font = glyph_Font : btn_Rename.Text = "✎"
        btn_RecentFiles.Font = glyph_Font : btn_RecentFiles.Text = "▾"
    End Sub

    ''' <summary>
    ''' Re-applies the active colour scheme to the chrome. Replaces the old
    ''' "For Each ctrl In Me.Controls" loop, recursing into the new container
    ''' panels so reparented controls are still recoloured.
    ''' </summary>
    Friend Sub RecolorChrome(back_Color As Color, opposite_Color As Color)
        If Not modern_Layout_Built Then Return

        Dim hover_Color As Color = BlendColor(back_Color, opposite_Color, 0.18F)

        flow_Toolbar.BackColor = back_Color
        panel_Status.BackColor = back_Color

        RecolorContainer(flow_Toolbar, back_Color, opposite_Color, hover_Color)
        RecolorContainer(panel_Status, back_Color, opposite_Color, hover_Color)

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
            CInt(a.R + (b.R - a.R) * t),
            CInt(a.G + (b.G - a.G) * t),
            CInt(a.B + (b.B - a.B) * t))
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
