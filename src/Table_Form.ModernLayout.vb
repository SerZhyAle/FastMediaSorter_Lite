#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Diagnostics
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports Microsoft.Win32

' The x64 .NET 10 Settings window keeps the existing controls and event handlers,
' but gives them a new shell: navigation on the left, a clear context header and
' roomy card-like sections.  The old tab headers are deliberately hidden rather
' than replaced, so the existing five TabPages remain the source of truth for
' keyboard navigation, OCR activation and all legacy bindings.
Partial Public Class Table_Form
    Implements IThemeAware

    Private SettingsCanvas As Color
    Private SettingsSurface As Color
    Private SettingsSidebar As Color
    Private SettingsSidebarMuted As Color
    Private SettingsAccent As Color
    Private SettingsLink As Color
    Private SettingsText As Color
    Private SettingsMutedText As Color
    Private SettingsBorder As Color
    Private SettingsEditorBack As Color
    Private SettingsEditorText As Color
    Private SettingsOnAccent As Color
    Private SettingsCheckTick As Color
    Private settingsDarkMode As Boolean

    Private Const DwmwaUseImmersiveDarkMode As Integer = 20

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer,
                                                   ByRef value As Integer, valueSize As Integer) As Integer
    End Function

    Private modernSettingsBuilt As Boolean
    Private modernSettingsShell As Panel
    Private modernSettingsSidebar As Panel
    Private modernSettingsHeader As Panel
    Private modernSettingsContent As Panel
    Private modernSettingsFooter As Panel
    Private modernSettingsTitle As Label
    Private modernSettingsSubtitle As Label
    Private modernSettingsFooterText As Label
    Private modernSettingsNavButtons As Button()
    Private modernSidebarSectionLabel As Label
    Private modernProductLabel As Label
    Private modernDataLayoutBuilt As Boolean
    Private modernTranslationPage As TabPage
    Private modernAudioPage As TabPage
    Private modernAboutPage As TabPage
    ' The shared value column of each page's row grid, in device pixels (127 §3.1.3):
    ' recomputed from the widest editor of the page on every resize, language change and
    ' scale change, so captions and editors align in two visual columns.
    Private ReadOnly pageValueColumns As New Dictionary(Of FlowLayoutPanel, Integer)()
    ' The resources page (ticket 118 Ф-1): the shared panel lives here, and the window records
    ' what the user changed so the main form can react after ShowDialog (13.4).
    Private modernResourcesPage As TabPage
    Private resourcesPanel As Resource_List_Panel
    Private resourcesTouchedInSettings As Boolean
    Private ReadOnly settingsRemovedResourceIdList As New List(Of String)()
    Private ReadOnly modernSettingsRows As New List(Of ModernSettingRow)()
    Private ReadOnly modernPageFlows As New List(Of FlowLayoutPanel)()

    Private NotInheritable Class ModernSettingRow
        Public Key As String
        Public Host As Control
        Public Title As Label
        Public Description As Label
        Public Editor As Control
        Public Compact As Boolean
        Public Arranging As Boolean
        ' The row grid of 127 §3.1: which page's shared value column this row belongs to,
        ' and the muted unit suffix that sits immediately left of a numeric editor.
        Public PageFlow As FlowLayoutPanel
        Public UnitLabel As Label
    End Class

    ''' <summary>
    ''' Creates the .NET 10-only visual shell. Existing controls are reparented,
    ''' not recreated, so no setting behaviour or Handles-based event wiring is
    ''' duplicated here.
    ''' </summary>
    ''' <summary>
    ''' The interface scale of §4.1: 1.0 unless the user chose a percentage. Read ONCE,
    ''' here, because every metric below is baked into a control at build time - which is
    ''' exactly why changing it asks to restart the window rather than pretending to
    ''' apply live. At 1.0 every Sp/Sf below returns its argument unchanged, so the
    ''' default window is pixel-for-pixel the one that shipped.
    ''' </summary>
    Private settingsScale As Single = 1.0F
    Private fontScale As Single = 1.0F

    Private Function Sp(value As Integer) As Integer
        If settingsScale = 1.0F Then Return value
        Return CInt(Math.Round(value * settingsScale))
    End Function

    Private Function Sf(points As Single) As Single
        If fontScale = 1.0F Then Return points
        Return points * fontScale
    End Function

    ''' <summary>The window's fonts, one per family/size/style, shared by every control that
    ''' asks for it. A control never owns the Font it is handed, so the per-control
    ''' <c>New Font(..)</c> this replaces left one native font per control behind each time the
    ''' window was built (S14-5); here they are released together when the window is.</summary>
    Private ReadOnly settingsFonts As New Dictionary(Of String, Font)()

    Private Function SettingsFont(family As String, size As Single, Optional style As FontStyle = FontStyle.Regular) As Font
        Dim key As String = family & "|" & size.ToString("R", Globalization.CultureInfo.InvariantCulture) & "|" &
                            CInt(style).ToString(Globalization.CultureInfo.InvariantCulture)
        Dim shared_Font As Font = Nothing
        If Not settingsFonts.TryGetValue(key, shared_Font) Then
            shared_Font = New Font(family, size, style)
            settingsFonts.Add(key, shared_Font)
        End If
        Return shared_Font
    End Function

    ''' <summary>A font sized in device pixels from a logical size (12 = what 9 pt is at 96 dpi).
    ''' A native list or edit box measures itself from its font; a pixel font carries no
    ''' point-to-pixel conversion for the system to get wrong, so the control follows
    ''' <see cref="Sp"/> like everything else in this window.</summary>
    Private Function SettingsPixelFont(family As String, logicalPixels As Integer, Optional style As FontStyle = FontStyle.Regular) As Font
        Dim pixels As Integer = Math.Max(8, Sp(logicalPixels))
        Dim key As String = family & "|" & pixels.ToString(Globalization.CultureInfo.InvariantCulture) & "px|" &
                            CInt(style).ToString(Globalization.CultureInfo.InvariantCulture)
        Dim shared_Font As Font = Nothing
        If Not settingsFonts.TryGetValue(key, shared_Font) Then
            shared_Font = New Font(family, CSng(pixels), style, GraphicsUnit.Pixel)
            settingsFonts.Add(key, shared_Font)
        End If
        Return shared_Font
    End Function

    ''' <summary>
    ''' Every drop-down list of this window is drawn here: the field and the list share one
    ''' item height of at least 28 logical px (ICON-RENDER section 3 rule 5, APP-SETTINGS rule 7)
    ''' and one pixel font, in the window's own colours. Left to the system, a list took the
    ''' size of its point font at the wrong scale and came out smaller than the buttons beside it.
    ''' A list that is already owner-drawn (the OCR languages) keeps its own handler.
    ''' </summary>
    Private Sub StyleModernComboBox(combo As ComboBox)
        ' Idempotent: it runs again after every DPI change (the pixel font and the item height are scale
        ' dependent) and for the combos that were added to a row after the first styling pass.
        combo.Font = SettingsPixelFont("Segoe UI", 12)
        If Not styledSettingsCombos.Contains(combo) Then
            styledSettingsCombos.Add(combo)
            If combo.DrawMode = DrawMode.Normal Then
                combo.DrawMode = DrawMode.OwnerDrawFixed
                AddHandler combo.DrawItem, AddressOf ModernComboBox_DrawItem
            End If
            If combo.DropDownStyle = ComboBoxStyle.DropDownList Then HookModernComboField(combo)
        End If
        If combo.DrawMode = DrawMode.OwnerDrawFixed Then
            ' The closed field is this height plus the control's own 6 px frame: >= 36 logical px, comfortable to hit.
            combo.ItemHeight = Math.Max(Sp(SettingsComboItemHeight), SettingsPixelFont("Segoe UI", 12).Height + Sp(8))
            combo.MaxDropDownItems = 16
        End If
    End Sub

    ''' <summary>Logical item height of every list row and (plus the control's 6 px frame) of the closed field.</summary>
    Private Const SettingsComboItemHeight As Integer = 38
    Private ReadOnly styledSettingsCombos As New HashSet(Of ComboBox)()

    ''' <summary>Re-applies the combo look to EVERY drop-down of the window - the General page's rows are added
    ''' after the first styling pass and were never reached by it, and a DPI change leaves the pixel fonts of
    ''' the combos at the old scale.</summary>
    Private Sub RestyleAllSettingsCombos()
        For Each combo As ComboBox In FindModernControls(Of ComboBox)(Me)
            StyleModernComboBox(combo)
        Next
    End Sub

    ''' <summary>The closed field of a drop-down LIST is painted by us (UserPaint): the system's flat style drew a
    ''' small white square for the arrow on the dark theme and put the value in a frame the size of a point font.
    ''' The list that opens stays the system's, drawn by <see cref="ModernComboBox_DrawItem"/>.</summary>
    Private Sub HookModernComboField(combo As ComboBox)
        Dim setStyle As Reflection.MethodInfo = GetType(Control).GetMethod("SetStyle",
            Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
        If setStyle Is Nothing Then Return
        setStyle.Invoke(combo, New Object() {ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                                             ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True})
        AddHandler combo.Paint, AddressOf ModernComboBox_Paint
        Dim repaint As EventHandler = Sub(sender As Object, e As EventArgs) combo.Invalidate()
        AddHandler combo.MouseEnter, repaint
        AddHandler combo.MouseLeave, repaint
        AddHandler combo.GotFocus, repaint
        AddHandler combo.LostFocus, repaint
        AddHandler combo.DropDown, repaint
        AddHandler combo.DropDownClosed, repaint
        AddHandler combo.SelectedIndexChanged, repaint
        AddHandler combo.EnabledChanged, repaint
        AddHandler combo.RightToLeftChanged, repaint
        AddHandler combo.TextChanged, repaint
        AddHandler combo.BackColorChanged, repaint
        AddHandler combo.ForeColorChanged, repaint
        combo.Invalidate()
    End Sub

    Private Sub ModernComboBox_Paint(sender As Object, e As PaintEventArgs)
        Dim combo As ComboBox = DirectCast(sender, ComboBox)
        Dim g As Graphics = e.Graphics
        Dim bounds As Rectangle = combo.ClientRectangle
        If bounds.Width < 8 OrElse bounds.Height < 8 Then Return
        Dim rtl As Boolean = combo.RightToLeft = RightToLeft.Yes
        Dim enabled As Boolean = combo.Enabled
        Dim hot As Boolean = enabled AndAlso bounds.Contains(combo.PointToClient(Control.MousePosition))
        Dim active As Boolean = enabled AndAlso (combo.DroppedDown OrElse combo.Focused)
        Dim back As Color = If(enabled, combo.BackColor, BlendSettingsColor(combo.BackColor, SettingsCanvas, 0.5F))
        Dim ink As Color = If(enabled, SettingsText, SettingsMutedText)
        Using brush As New SolidBrush(back)
            g.FillRectangle(brush, bounds)
        End Using

        ' The arrow zone: a calmer tone than the field, ink in the text colour so it reads on both themes.
        Dim zoneWidth As Integer = Math.Min(bounds.Width \ 2, Math.Max(Sp(36), bounds.Height))
        Dim zone As Rectangle = If(rtl, New Rectangle(bounds.X, bounds.Y, zoneWidth, bounds.Height),
                                   New Rectangle(bounds.Right - zoneWidth, bounds.Y, zoneWidth, bounds.Height))
        Using brush As New SolidBrush(BlendSettingsColor(back, SettingsText, If(hot OrElse combo.DroppedDown, 0.16F, 0.08F)))
            g.FillRectangle(brush, zone)
        End Using
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim cx As Single = zone.X + zone.Width / 2.0F
        Dim cy As Single = zone.Y + zone.Height / 2.0F
        Dim half As Single = Math.Max(4.0F, Sp(6))
        Using pen As New Pen(ink, Math.Max(1.75F, Sp(2))) With {.StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
            g.DrawLines(pen, New PointF() {New PointF(cx - half, cy - half / 2.0F), New PointF(cx, cy + half / 2.0F), New PointF(cx + half, cy - half / 2.0F)})
        End Using

        Dim item As Object = combo.SelectedItem
        If item IsNot Nothing Then
            Dim text As String = If(TypeOf item Is LanguageEntry, DirectCast(item, LanguageEntry).DisplayName(), combo.GetItemText(item))
            Dim pad As Integer = Sp(10)
            Dim area As New RectangleF(If(rtl, zone.Right + pad, bounds.X + pad), bounds.Y, Math.Max(0, bounds.Width - zoneWidth - 2 * pad), bounds.Height)
            ' DrawString: a pixel font is drawn in pixels there, exactly as in the list rows (see ModernComboBox_DrawItem).
            Using sf As New StringFormat(StringFormatFlags.NoWrap)
                sf.LineAlignment = StringAlignment.Center
                sf.Trimming = StringTrimming.EllipsisCharacter
                sf.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None
                If rtl Then sf.FormatFlags = sf.FormatFlags Or StringFormatFlags.DirectionRightToLeft
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
                Using textBrush As New SolidBrush(ink)
                    g.DrawString(text, combo.Font, textBrush, area, sf)
                End Using
            End Using
        End If

        g.SmoothingMode = SmoothingMode.None
        Dim lineWidth As Integer = If(active, Math.Max(2, Sp(2)), Math.Max(1, Sp(1)))
        Using pen As New Pen(If(active, SettingsAccent, SettingsBorder), lineWidth) With {.Alignment = PenAlignment.Inset}
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1)
        End Using
    End Sub

    Private Sub ModernComboBox_DrawItem(sender As Object, e As DrawItemEventArgs)
        Dim combo As ComboBox = DirectCast(sender, ComboBox)
        Dim inField As Boolean = (e.State And DrawItemState.ComboBoxEdit) <> 0
        Dim highlighted As Boolean = Not inField AndAlso (e.State And DrawItemState.Selected) <> 0
        Dim back As Color = If(highlighted, SettingsAccent, combo.BackColor)
        Dim fore As Color = If(Not combo.Enabled, SettingsMutedText,
                               If(highlighted, SettingsOnAccent, combo.ForeColor))
        Using brush As New SolidBrush(back)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        If e.Index >= 0 AndAlso e.Index < combo.Items.Count Then
            Dim padding As Integer = Sp(8)
            Dim area As New RectangleF(e.Bounds.X + padding, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 2 * padding), e.Bounds.Height)
            ' DrawString, not TextRenderer: TextRenderer converts a pixel font through the
            ' point size at the device DPI, which scaled this one a second time (about 1.75x
            ' too large at 175 %) while the numeric boxes beside it, on the same font, were
            ' right. GDI+ draws a pixel font in pixels, so every list is one size at any
            ' scale - the font is the window's shared one, not whatever the control carries.
            Using sf As New StringFormat(StringFormatFlags.NoWrap)
                sf.LineAlignment = StringAlignment.Center
                sf.Trimming = StringTrimming.EllipsisCharacter
                sf.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None
                If combo.RightToLeft = RightToLeft.Yes Then sf.FormatFlags = sf.FormatFlags Or StringFormatFlags.DirectionRightToLeft
                Using textBrush As New SolidBrush(fore)
                    ' combo.Font, not a font rebuilt from the layout scale: the control's own font is the one
                    ' WinForms rescales when the window changes monitor, so the list and the field agree.
                    e.Graphics.DrawString(If(TypeOf combo.Items(e.Index) Is LanguageEntry, DirectCast(combo.Items(e.Index), LanguageEntry).DisplayName(), combo.GetItemText(combo.Items(e.Index))), combo.Font, textBrush, area, sf)
                End Using
            End Using
        End If
        e.DrawFocusRectangle()
    End Sub

    Private Shared Function IsDarkColor(c As Color) As Boolean
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140.0
    End Function

    Private Sub DisposeSettingsFonts()
        For Each owned_Font As Font In settingsFonts.Values
            owned_Font.Dispose()
        Next
        settingsFonts.Clear()
    End Sub

    Private Sub BuildModernSettingsLayout()
        If modernSettingsBuilt Then Return
        modernSettingsBuilt = True

        ' This shell sizes and scales everything itself (Sp, Sf, settingsScale, DpiChanged), so WinForms
        ' must not scale the control tree as well. The Designer's AutoScaleMode.Font re-scaled every
        ' control on each Font assignment and each DPI change - about 10 s per open on a 175 % display.
        ' The window is placed by hand before its handle exists (PositionModernSettingsBeside).
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual

        Dim percent As Integer = Main_Form.GetModernPreferences().InterfaceScalePercent
        Dim dpiFactor As Single = Math.Max(1.0F, CSng(DeviceDpi) / 96.0F)
        Dim relative As Integer = Main_Form.GetModernPreferences().InterfaceScaleRelativePercent
        Dim userFactor As Single = If(relative > 0, relative / 100.0F, If(percent <= 0, 1.0F, percent / 100.0F))
        settingsScale = dpiFactor * userFactor
        fontScale = userFactor

        LoadSystemSettingsPalette()

        SuspendLayout()

        ' Compact by default, resizeable when a long translation/status needs it.
        FormBorderStyle = FormBorderStyle.Sizable
        MaximizeBox = True
        MinimizeBox = False
        MinimumSize = New Size(Sp(780), Sp(400))
        ClientSize = New Size(Sp(960), Sp(640))
        RestoreModernSettingsWindowSize()
        BackColor = SettingsCanvas
        Font = SettingsFont("Segoe UI", Sf(9.0F), FontStyle.Regular)

        modernSettingsShell = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = SettingsCanvas,
            .Padding = Padding.Empty
        }
        modernSettingsSidebar = New Panel With {
            .Dock = DockStyle.Left,
            .Width = Sp(218),
            .BackColor = SettingsSidebar,
            .Padding = New Padding(16, 20, 16, 16)
        }
        modernSettingsHeader = New Panel With {
            .Dock = DockStyle.Top,
            .Height = Sp(68),
            .BackColor = SettingsSurface,
            .Padding = New Padding(22, 10, 22, 8)
        }
        modernSettingsFooter = New Panel With {
            .Dock = DockStyle.Bottom,
            .Height = 1,
            .BackColor = SettingsSurface,
            .Visible = False
        }
        modernSettingsContent = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = SettingsCanvas,
            .Padding = New Padding(8, 6, 8, 6)
        }

        ' The full name is longer than the sidebar is wide, so the label wraps to two lines
        ' (UseMnemonic off: the name carries a plain ampersand) and the rows below start lower.
        ' A fixed height held one line at 225 % and clipped the second: the label sizes itself to its text
        ' within the sidebar width, so it stays whole at any DPI (the font changes with the monitor).
        modernProductLabel = New Label With {
            .AutoSize = True,
            .MaximumSize = New Size(Sp(182), 0),
            .UseMnemonic = False,
            .Text = ProductNames.Full,
            .ForeColor = SettingsText,
            .Font = SettingsFont("Segoe UI", Sf(9.0F), FontStyle.Bold),
            .Location = New Point(Sp(18), Sp(18))
        }
        modernSidebarSectionLabel = New Label With {
            .AutoSize = True,
            .ForeColor = SettingsSidebarMuted,
            .Font = SettingsFont("Segoe UI", Sf(8.5F), FontStyle.Regular),
            .Location = New Point(Sp(19), Sp(55)),
            .Visible = False
        }
        modernSettingsSidebar.Controls.Add(modernProductLabel)
        modernSettingsSidebar.Controls.Add(modernSidebarSectionLabel)

        modernSettingsTitle = New Label With {
            .AutoSize = False,
            .ForeColor = SettingsText,
            .Font = SettingsFont("Segoe UI Semibold", Sf(13.0F), FontStyle.Regular),
            .Location = New Point(Sp(20), Sp(3)),
            .Height = Sp(36),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        modernSettingsSubtitle = New Label With {
            .AutoSize = False,
            .ForeColor = SettingsMutedText,
            .Font = SettingsFont("Segoe UI", Sf(8.5F), FontStyle.Regular),
            .Location = New Point(Sp(20), Sp(39)),
            .Height = Sp(22),
            .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        }
        modernSettingsHeader.Controls.Add(modernSettingsTitle)
        modernSettingsHeader.Controls.Add(modernSettingsSubtitle)

        ' Version/contact remains available without consuming a whole footer row.
        ReparentModernSettingsControl(LinkLabel1, modernSettingsSidebar)
        LinkLabel1.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        LinkLabel1.AutoSize = True
        LinkLabel1.ForeColor = SettingsLink
        LinkLabel1.Location = New Point(18, modernSettingsSidebar.ClientSize.Height - LinkLabel1.Height - 16)
        AddHandler modernSettingsSidebar.Resize,
            Sub()
                LinkLabel1.Left = Sp(2)
                LinkLabel1.Top = Sp(4)
            End Sub
        AddHandler modernSettingsHeader.Resize,
            Sub()
                Dim width As Integer = Math.Max(100, modernSettingsHeader.ClientSize.Width - 48)
                modernSettingsTitle.Width = width
                modernSettingsSubtitle.Width = width
            End Sub

        BuildExtraModernSettingsPages()
        BuildModernSettingsNavigation()

        ' Preserve the real TabControl for accessibility and existing event code,
        ' but remove its dated strip of tab headers. Navigation buttons select the
        ' same TabPages and Ctrl+Tab still works as a standard WinForms control.
        ReparentModernSettingsControl(Tab_Control, modernSettingsContent)
        Tab_Control.Dock = DockStyle.Fill
        Tab_Control.Appearance = TabAppearance.FlatButtons
        Tab_Control.SizeMode = TabSizeMode.Fixed
        Tab_Control.ItemSize = New Size(0, 1)
        Tab_Control.Padding = New Point(0, 0)
        Tab_Control.Multiline = True
        Tab_Control.BackColor = SettingsCanvas

        For Each page As TabPage In Tab_Control.TabPages
            page.BackColor = SettingsCanvas
            page.UseVisualStyleBackColor = False
            page.Padding = New Padding(0)
            page.AutoScroll = False
        Next

        ReparentModernSettingsControl(modernSettingsContent, modernSettingsShell)
        ReparentModernSettingsControl(modernSettingsHeader, modernSettingsShell)
        ReparentModernSettingsControl(modernSettingsSidebar, modernSettingsShell)
        Controls.Add(modernSettingsShell)
        modernSettingsShell.BringToFront()

        AddHandler Tab_Control.SelectedIndexChanged, AddressOf ModernSettingsTabChanged
        AddHandler Tab_Control.Deselecting, Sub(sender, e) CaptureSettingsPage(e.TabPage)
        AddHandler modernSettingsContent.Resize, AddressOf ModernSettingsContentResized
        AddHandler VisibleChanged, AddressOf ModernSettingsVisibleChanged
        AddHandler Shown, AddressOf ModernSettingsShown
        AddHandler FormClosing, AddressOf SaveModernSettingsWindowSize
        AddHandler DpiChanged, Sub()
                                   settingsScale = Math.Max(1.0F, CSng(DeviceDpi) / 96.0F) * fontScale
                                   resourcesPanel?.SetDeviceScale(settingsScale)
                                   ResizeResourcesListHost()
                                   For Each group In settingsGroups.Values
                                       group.SetLayoutScale(settingsScale)
                                   Next
                                   For Each row As ModernSettingRow In modernSettingsRows
                                       For Each check As CheckBox In FindModernControls(Of CheckBox)(row.Host)
                                           ConfigureModernCheckbox(check, row)
                                       Next
                                   Next
                                   StyleModernSettingsControls()
                                   LayoutModernSettingsPages()
                                   ' The nav glyphs are baked bitmaps at Sp(20): a tier change
                                   ' must re-render them, or the old size survives the DPI
                                   ' change (128 §5 scenario 3).
                                   RefreshModernSettingsHeader()
                                   QueueRestoreSettingsContext()
                               End Sub
        BuildModernDataLayout()
        BuildSettingsGroups()
        BuildSettingsDiscovery()
        StyleModernSettingsControls()
        LayoutModernSettingsPages()
        ApplySystemSettingsPalette()
        ResumeLayout(True)
        FitInitialSettingsWindowToContent()
    End Sub

    Private Sub ReparentModernSettingsControl(control As Control, target As Control)
        If control Is Nothing Then Return
        If control.Parent IsNot Nothing Then control.Parent.Controls.Remove(control)
        target.Controls.Add(control)
    End Sub

    ''' <summary>Control names of the nav buttons, in tab order. Each is a row of
    ''' <see cref="IconMap"/>: the picture and the canonical name come from the vocabulary
    ''' (ICON-SET), never from a second table kept here.</summary>
    Private Shared ReadOnly ModernNavControlNames As String() = {
        "settingsNavGeneral", "settingsNavRecipients", "settingsNavViewing", "settingsNavVideo", "settingsNavAudio", "settingsNavFiles",
        "settingsNavResources",
        "settingsNavOcr", "settingsNavTranslation", "settingsNavStorage", "settingsNavAbout"}

    Private ReadOnly modernNavTips As New ToolTip()

    Private Sub BuildModernSettingsNavigation()
        ' A dense list (ICON-RENDER rule 5: 20 px glyph, a row of 34 - above the 28 px
        ' mouse floor). The list AutoScrolls, so a shortened window shows a slice of it instead of clipping entries away.
        Dim top As Integer = 0
        Dim navLabels As String() = {"General", "Destination folders", "Images", "Video", "Audio", "Files and system",
                                     "Server and resources",
                                     "OCR", "Translation", "Storage", "About"}
        Dim navigation As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = Padding.Empty}
        Dim branding As New Panel With {.Dock = DockStyle.Top, .Height = Sp(66)}
        branding.Controls.Add(modernProductLabel)
        Dim versionFooter As New Panel With {.Dock = DockStyle.Bottom, .Height = Sp(28)}
        versionFooter.Controls.Add(LinkLabel1)
        LinkLabel1.Location = New Point(Sp(2), Sp(4))
        LinkLabel1.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        modernSettingsSidebar.Controls.Add(versionFooter)
        modernSettingsSidebar.Controls.Add(navigation)
        modernSettingsSidebar.Controls.Add(branding)
        navigation.BringToFront()
        top = 0
        modernSettingsNavButtons = New Button(navLabels.Length - 1) {}

        For index As Integer = 0 To navLabels.Length - 1
            Dim button As New Button With {
                .Name = ModernNavControlNames(index),
                .Tag = index,
                .Text = navLabels(index),
                .FlatStyle = FlatStyle.Flat,
                .UseVisualStyleBackColor = False,
                .BackColor = SettingsSidebar,
                .ForeColor = SettingsSidebarMuted,
                .Font = SettingsFont("Segoe UI Semibold", Sf(8.5F), FontStyle.Regular),
                .TextAlign = ContentAlignment.MiddleLeft,
                .ImageAlign = ContentAlignment.MiddleLeft,
                .TextImageRelation = TextImageRelation.ImageBeforeText,
                .AutoEllipsis = False,
                .Height = Sp(34),
                .Width = Math.Max(1, modernSettingsSidebar.ClientSize.Width - Sp(48)),
                .Location = New Point(Sp(12), top),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Padding = New Padding(Sp(10), 0, Sp(6), 0),
                .Cursor = Cursors.Hand,
                .TabStop = True
            }
            button.FlatAppearance.BorderSize = 0
            AddHandler button.Click, AddressOf ModernSettingsNavClicked
            AddHandler button.Paint, AddressOf PaintModernNavEdge
            AddHandler button.GotFocus, Sub() button.Invalidate()
            AddHandler button.LostFocus, Sub() button.Invalidate()
            navigation.Controls.Add(button)
            modernSettingsNavButtons(index) = button
            top += Sp(38)
        Next
    End Sub

    ''' <summary>Draws the vocabulary glyph of a nav button in the page's identity ink
    ''' (128 §3.3), not the caption ink - the picture carries the page's own hue in both
    ''' themes and keeps it on the selected row. The picture is padded on the right so
    ''' the gap to the caption is ours and not the button layout's; it is re-rendered
    ''' whenever the colour changes (selection, theme), because a cached bitmap would
    ''' keep the colour it was first drawn in.</summary>
    Private Sub RenderModernNavGlyph(button As Button)
        Dim meaning As IconMeaning = IconMap.GetMeaning(button.Name)
        If meaning Is Nothing OrElse String.IsNullOrEmpty(meaning.GlyphId) Then Return
        Dim tier As Integer = Sp(20)
        Dim gap As Integer = Sp(10)
        Dim padded As New Bitmap(tier + gap, tier)
        Try
            padded.SetResolution(96.0F, 96.0F)
            Dim ink As Color = SettingsNavIconPalette.ResolveInk(button.Name, settingsDarkMode,
                {button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor},
                button.ForeColor, SystemInformation.HighContrast)
            Using glyph As Bitmap = GlyphRenderer.Render(meaning.GlyphId, tier, ink, AddressOf RuntimeBootstrap.OpenBundledAsset)
                Using g As Graphics = Graphics.FromImage(padded)
                    g.DrawImageUnscaled(glyph, 0, 0)
                End Using
            End Using
        Catch
            padded.Dispose()
            Throw
        End Try
        Dim previous As Image = button.Image
        button.Image = padded
        If previous IsNot Nothing Then previous.Dispose()
    End Sub

    Private Sub BuildExtraModernSettingsPages()
        modernGeneralPage = New TabPage With {.Name = "Tab_Page_General", .Text = "General"}
        modernStoragePage = New TabPage With {.Name = "Tab_Page_Storage", .Text = "Storage"}
        modernAudioPage = New TabPage With {.Name = "Tab_Page_Audio", .Text = "Audio"}
        modernTranslationPage = New TabPage With {.Name = "Tab_Page_Translation", .Text = "Translation"}
        modernAboutPage = New TabPage With {.Name = "Tab_Page_About", .Text = "About"}
        modernResourcesPage = New TabPage With {.Name = "Tab_Page_Resources", .Text = "Server and resources"}
        Tab_Control.TabPages.Insert(3, modernAudioPage)
        ' «Ресурсы» between Files (Tab_Page_4) and OCR (Tab_Page_5): Insert(5) lands right there
        ' (ticket 118 Q118-5), and the nav array above says the same order.
        Tab_Control.TabPages.Insert(5, modernResourcesPage)
        Tab_Control.TabPages.Add(modernTranslationPage)
        Tab_Control.TabPages.Add(modernAboutPage)
        Tab_Control.TabPages.Insert(0, modernGeneralPage)
        Tab_Control.TabPages.Insert(Tab_Control.TabPages.IndexOf(modernAboutPage), modernStoragePage)
    End Sub

    Private Sub ModernSettingsNavClicked(sender As Object, e As EventArgs)
        Dim button As Button = TryCast(sender, Button)
        If button Is Nothing Then Return
        Dim index As Integer = CInt(button.Tag)
        If index >= 0 AndAlso index < Tab_Control.TabCount Then Tab_Control.SelectedIndex = index
    End Sub

    Private Sub ModernSettingsTabChanged(sender As Object, e As EventArgs)
        If Not restoringSettingsContext AndAlso Tab_Control.SelectedTab Is modernTranslationPage Then OnEnterOcrTab()
        RefreshModernSettingsHeader()
        ChangeSettingsContextPage()
    End Sub

    Private Sub ModernSettingsShown(sender As Object, e As EventArgs)
        PositionModernSettingsOnOwnerMonitor()
        ' The recipients overlay's edit door (219 §4): one open-at entry, honoured when the
        ' window is shown - a plain open never sees it.
        If pendingOpenAtRecipientsGrid Then
            pendingOpenAtRecipientsGrid = False
            RevealModernRecipientsGrid()
        End If
        QueueRestoreSettingsContext()
    End Sub

    ''' <summary>
    ''' Restores the size independently of the main viewer's geometry. The values are
    ''' bounded by the current monitor so a size saved on a larger display cannot make
    ''' the settings window unusable on a smaller one.
    ''' </summary>
    Private Sub RestoreModernSettingsWindowSize()
        Dim savedWidth As Integer
        Dim savedHeight As Integer
        If Not Integer.TryParse(GetSetting(App_name, Second_App_Name, "SettingsWindowWidth", "0"), savedWidth) OrElse
           Not Integer.TryParse(GetSetting(App_name, Second_App_Name, "SettingsWindowHeight", "0"), savedHeight) OrElse
           savedWidth <= 0 OrElse savedHeight <= 0 Then Return

        savedWidth = Math.Max(savedWidth, MinimumSize.Width)
        savedHeight = Math.Max(savedHeight, MinimumSize.Height)
        FitModernSettingsClientSize(Screen.FromControl(Me).WorkingArea, savedWidth, savedHeight)
    End Sub

    ''' <summary>Places the settings beside its owner rather than on whichever monitor
    ''' Windows chooses for a not-yet-visible form.</summary>
    Private Sub PositionModernSettingsOnOwnerMonitor()
        PositionModernSettingsBeside(Owner)
    End Sub

    ''' <summary>The placement itself, for an owner that is known before ShowDialog sets it. A window
    ''' moved to another monitor AFTER its handle exists changes DPI, and then every control is scaled
    ''' again, so the first placement is made while the window is still hidden.</summary>
    Friend Sub PositionModernSettingsBeside(ownerForm As Form)
        If ownerForm Is Nothing Then Return

        Dim workArea As Rectangle = Screen.FromControl(ownerForm).WorkingArea
        FitModernSettingsClientSize(workArea, ClientSize.Width, ClientSize.Height)
        Dim savedX As Integer
        Dim savedY As Integer
        If Not Integer.TryParse(GetSetting(App_name, Second_App_Name, "SettingsWindowX", ""), savedX) OrElse
           Not Integer.TryParse(GetSetting(App_name, Second_App_Name, "SettingsWindowY", ""), savedY) Then
            savedX = If(ownerForm.Right + Width <= workArea.Right, ownerForm.Right,
                If(ownerForm.Left - Width >= workArea.Left, ownerForm.Left - Width, workArea.Left))
            savedY = ownerForm.Top
        End If
        Location = New Point(Math.Max(workArea.Left, Math.Min(savedX, workArea.Right - Width)),
            Math.Max(workArea.Top, Math.Min(savedY, workArea.Bottom - Height)))
    End Sub

    ''' <summary>Everything the window needs before its handle exists: no WinForms auto-scaling (see
    ''' BuildModernSettingsLayout), no centring on the owner, and the place beside the owner. Called
    ''' BEFORE PrepareForDisplay, whose tooltips create the handle, and again once the layout has set
    ''' the size. A window moved to another monitor after it has a handle changes DPI, and then every
    ''' control is scaled again.</summary>
    Friend Sub PrepareShowBeside(ownerForm As Form)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None
        Me.StartPosition = System.Windows.Forms.FormStartPosition.Manual
        PositionModernSettingsBeside(ownerForm)
    End Sub

    Private Sub FitModernSettingsClientSize(workArea As Rectangle, desiredWidth As Integer, desiredHeight As Integer)
        Dim chromeWidth As Integer = Width - ClientSize.Width
        Dim chromeHeight As Integer = Height - ClientSize.Height
        Dim maxClientWidth As Integer = Math.Max(1, workArea.Width - chromeWidth)
        Dim maxClientHeight As Integer = Math.Max(1, workArea.Height - chromeHeight)
        Dim minClientWidth As Integer = Math.Max(1, MinimumSize.Width - chromeWidth)
        Dim minClientHeight As Integer = Math.Max(1, MinimumSize.Height - chromeHeight)

        ClientSize = New Size(
            Math.Min(Math.Max(desiredWidth, minClientWidth), maxClientWidth),
            Math.Min(Math.Max(desiredHeight, minClientHeight), maxClientHeight))
    End Sub

    ''' <summary>Saves only a normal window's client area; maximising it must not
    ''' replace the user's preferred size with the dimensions of one monitor.</summary>
    Private Sub SaveModernSettingsWindowSize(sender As Object, e As FormClosingEventArgs)
        If WindowState <> FormWindowState.Normal Then Return

        SaveSetting(App_name, Second_App_Name, "SettingsWindowX", Location.X.ToString())
        SaveSetting(App_name, Second_App_Name, "SettingsWindowY", Location.Y.ToString())
        SaveSetting(App_name, Second_App_Name, "SettingsWindowWidth", ClientSize.Width.ToString())
        SaveSetting(App_name, Second_App_Name, "SettingsWindowHeight", ClientSize.Height.ToString())
    End Sub

    Private Sub ModernSettingsContentResized(sender As Object, e As EventArgs)
        CaptureSettingsPage(Tab_Control.SelectedTab)
        LayoutModernSettingsPages()
        QueueRestoreSettingsContext()
    End Sub

    ''' <summary>Selects button caption based on packaged (MSIX) status (057.S14-2).</summary>
    Friend Shared Function AssociationButtonCaption(isPackaged As Boolean) As String
        Return AssociationUx.Caption(AssociationUx.ModeFor(isPackaged))
    End Function

    ''' <summary>Called by PrepareForDisplay after the existing localization code.</summary>
    Private Sub LocalizeModernSettingsLayout()
        If Not modernSettingsBuilt Then Return

        Dim navLabels As String() = {Localization.T("Общие"), Localization.T("Получатели"),
             Localization.T("Изображения"),
             Localization.T("Видео"),
             Localization.T("Аудио"),
             Localization.T("Файлы"),
             Localization.T("Сервер и ресурсы"),
             Localization.T("OCR"),
             Localization.TC("settings", "Перевод"),
             Localization.T("Хранилище"),
             Localization.T("О программе")}
        For index As Integer = 0 To modernSettingsNavButtons.Length - 1
            modernSettingsNavButtons(index).Text = navLabels(index)
            Tab_Control.TabPages(index).Text = navLabels(index)
            ' The compact row of the build (34 logical px, above the 28 px mouse floor):
            ' eleven entries take 418 logical px instead of 528, so the whole navigation
            ' fits an ordinary monitor without scrolling.
            modernSettingsNavButtons(index).Height = Math.Max(Sp(34), TextRenderer.MeasureText(navLabels(index), modernSettingsNavButtons(index).Font,
                New Size(Math.Max(1, modernSettingsNavButtons(index).Width - Sp(48)), Integer.MaxValue), TextFormatFlags.WordBreak).Height + Sp(6))
            modernSettingsNavButtons(index).Top = If(index = 0, 0, modernSettingsNavButtons(index - 1).Bottom + Sp(4))
            ' The caption can be clipped by a long translation: the tooltip and the
            ' accessible name always carry the whole word (ICON-RENDER rule 8).
            modernSettingsNavButtons(index).AccessibleName = navLabels(index)
            modernNavTips.SetToolTip(modernSettingsNavButtons(index), navLabels(index))
        Next

        LinkLabel1.Text = ShortSettingsVersion()
        ' The window's title bar already says "Settings"; the sidebar names only the product.
        modernSidebarSectionLabel.Text = String.Empty
        LocalizeModernSettingRows()
        LocalizeSettingsGroups()
        LocalizeSettingsDiscovery()
        RefreshModernSettingsHeader()
        QueueRestoreSettingsContext()
    End Sub

    Private Sub RefreshModernSettingsHeader()
        If Not modernSettingsBuilt Then Return
        Dim index As Integer = Math.Max(0, Tab_Control.SelectedIndex)
        Dim subtitles As String() = {Localization.T("Язык, оформление и запуск приложения."), Localization.T("Назначьте папки для быстрого перемещения и копирования."),
             Localization.T("Фон изображения") & ". " & Localization.T("Поведение слайд-шоу") & ".",
             Localization.T("Дорожки и субтитры") & ". " & Localization.T("Информация и управление") & ".",
             Localization.T("Обложки, управление и поведение аудиодорожек."),
             Localization.T("Операции с файлами") & ". " & Localization.T("Типы файлов") & ".",
             Localization.T("Серверы и сетевые папки") & ". " & Localization.T("SFTP-сервер и мобильное приложение Android."),
             Localization.T("Распознавание текста на изображениях и параметры OCR."),
             Localization.T("Сервис и параметры перевода распознанного текста."),
             Localization.T("Управление кэшем"),
             Localization.T("Версия приложения, документация и ссылки проекта.")}

        If index >= modernSettingsNavButtons.Length Then index = 0
        modernSettingsTitle.Text = modernSettingsNavButtons(index).Text
        modernSettingsSubtitle.Text = subtitles(index)
        ArrangeSettingsDiscoveryHeader()

        For buttonIndex As Integer = 0 To modernSettingsNavButtons.Length - 1
            Dim selected As Boolean = buttonIndex = index
            Dim button As Button = modernSettingsNavButtons(buttonIndex)
            ' The selected row of 128 §3.3: a softened accent fill plus the leading bar,
            ' caption in the normal text ink. A solid accent fill forces the identity
            ' glyph either back to monochrome or into a hue clash, so every row keeps
            ' its colored picture on its own background.
            button.BackColor = If(selected, BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.22F), SettingsSidebar)
            button.ForeColor = If(selected, SettingsText, SettingsSidebarMuted)
            button.FlatAppearance.MouseOverBackColor = If(selected, BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.30F), BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.18F))
            button.FlatAppearance.MouseDownBackColor = If(selected, BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.40F), BlendSettingsColor(SettingsSidebar, SettingsAccent, 0.28F))
            If SystemInformation.HighContrast Then
                button.BackColor = If(selected, SettingsAccent, SettingsSidebar)
                button.ForeColor = If(selected, SettingsOnAccent, SettingsSidebarMuted)
                button.FlatAppearance.MouseOverBackColor = button.BackColor
                button.FlatAppearance.MouseDownBackColor = button.BackColor
            End If
            RenderModernNavGlyph(button)
        Next
    End Sub

    ''' <summary>True when <paramref name="button"/> is the row of the selected page -
    ''' the paint handler asks this rather than caching a flag, so a tab switch through
    ''' the keyboard (Ctrl+Tab) repaints the bar with the background.</summary>
    Private Function IsModernNavButtonSelected(button As Button) As Boolean
        If modernSettingsNavButtons Is Nothing OrElse Tab_Control Is Nothing Then Return False
        Dim index As Integer = Array.IndexOf(modernSettingsNavButtons, button)
        Return index >= 0 AndAlso index = Math.Max(0, Tab_Control.SelectedIndex)
    End Function

    ''' <summary>The leading accent bar of the selected row and the keyboard focus ring
    ''' (128 §3.3, the ring rules of 127 §3.3: 2 px accent, offset 2 px, never removed).
    ''' The bar rides the leading edge and mirrors under RTL with the row's own content;
    ''' the bar marks selection and the ring marks focus, so the two signals stay
    ''' independent and a focused row is told from the selected one.</summary>
    Private Sub PaintModernNavEdge(sender As Object, e As PaintEventArgs)
        Dim button As Button = TryCast(sender, Button)
        If button Is Nothing Then Return
        If IsModernNavButtonSelected(button) Then
            Dim barWidth As Integer = Sp(3)
            Dim barX As Integer = If(button.RightToLeft = RightToLeft.Yes, button.Width - barWidth, 0)
            Using accent As New SolidBrush(SettingsAccent)
                e.Graphics.FillRectangle(accent, barX, 0, barWidth, button.Height)
            End Using
        End If
        If button.Focused Then
            Dim inset As Integer = Sp(2) + 1
            If button.Width > inset * 2 + 2 AndAlso button.Height > inset * 2 + 2 Then
                Using ringPen As New Pen(SettingsAccent, Math.Max(1.0F, 2.0F * settingsScale))
                    e.Graphics.DrawRectangle(ringPen, inset, inset, button.Width - inset * 2 - 1, button.Height - inset * 2 - 1)
                End Using
            End If
        End If
    End Sub

    Private Sub LocalizeModernSettingRows()
        For Each row As ModernSettingRow In modernSettingsRows
            row.Title.Text = ModernSettingTitle(row.Key)
            If row.Description IsNot Nothing Then row.Description.Text = ModernSettingDescription(row.Key)
            For Each check As CheckBox In FindModernControls(Of CheckBox)(row.Host)
                ConfigureModernCheckbox(check, row)
            Next
            Select Case row.Key
                Case "image_associations", "video_associations"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then
                        button.Text = AssociationButtonCaption(Main_Form.IsPackagedApp())
                        ApplySettingsCommandGlyph(button, row.Key)
                    End If
                Case "sftp_manager"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = Localization.T("Управление SFTP")
                Case "support_send_logs"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then button.Text = Localization.T("Собрать и отправить")
                Case "profile_reset_all"
                    Dim button As Button = TryCast(row.Editor, Button)
                    If button IsNot Nothing Then
                        button.Text = Localization.T("Сбросить настройки")
                        UpdateResetAllButtonState()
                        If Not button.Enabled AndAlso toolTip IsNot Nothing AndAlso row.Host IsNot Nothing Then
                            toolTip.SetToolTip(row.Host, button.AccessibleDescription)
                        End If
                    End If
                    If row.Description IsNot Nothing AndAlso toolTip IsNot Nothing Then
                        toolTip.SetToolTip(row.Description, Localization.T("Сбрасываются получатели, вид и поведение, видео и звук, OCR и перевод, положение окон. Не затрагиваются: язык и тема (они общие с Share Manager), сохранённые пароли и ключи, список ресурсов, история папок и файлов. Прежние значения записываются в файл, который можно открыть двойным щелчком, закрыв программу. Сброс выполняется при следующем запуске."))
                    End If
                Case "ocr_server"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = Localization.T("Установить")
                    If buttons.Count > 1 Then buttons(1).Text = Localization.T("Запустить")
                Case "ocr_model"
                    Dim buttons As List(Of Button) = FindModernControls(Of Button)(row.Host).ToList()
                    If buttons.Count > 0 Then buttons(0).Text = Localization.T("Загрузить")
                Case "project_trust"
                    Dim link As LinkLabel = TryCast(row.Title, LinkLabel)
                    If link IsNot Nothing Then link.Tag = SitePages.TrustPageUrl(Localization.CurrentCode)
                Case "exchange_portal"
                    Dim link As LinkLabel = TryCast(row.Title, LinkLabel)
                    If link IsNot Nothing Then link.Tag = SitePages.ExchangePortalUrl("home-network", Localization.CurrentCode)
                Case "slideshow_interval"
                    ' PrepareForDisplay's legacy block (which runs before this pass on every
                    ' opening) pins DecimalPlaces = 3; restate the whole-seconds presentation
                    ' after it, so 10 s renders "10", never "10.000" (127 §3.2).
                    RefreshSlideshowIntervalDecimals(row.Editor, EventArgs.Empty)
            End Select
            For Each link As LinkLabel In FindModernControls(Of LinkLabel)(row.Host)
                If link IsNot row.Title Then link.Text = Localization.T("Открыть")
            Next
            If row.Description IsNot Nothing AndAlso toolTip IsNot Nothing Then toolTip.SetToolTip(row.Description, row.Description.Text)
            SizeModernSettingRow(row)
        Next
        LayoutModernSettingsPages()
    End Sub

    Private Shared Function FindModernControls(Of T As Control)(parent As Control) As IEnumerable(Of T)
        Dim found As New List(Of T)()
        For Each child As Control In parent.Controls
            If TypeOf child Is T Then found.Add(DirectCast(child, T))
            If child.HasChildren Then found.AddRange(FindModernControls(Of T)(child))
        Next
        Return found
    End Function

    Private Sub ConfigureModernCheckbox(check As CheckBox, row As ModernSettingRow)
        ' PrepareForDisplay localizes the legacy controls on every opening and can
        ' therefore restore their old long captions. In the modern row the caption
        ' is already rendered by row.Title; keeping it on the tiny editor produces
        ' the stray first letters seen to the right of the checkbox.
        check.Text = String.Empty
        ' The drawn box of 127 §3.3: fill AND glyph distinguish the states in both themes.
        SettingsCheckBox.Attach(check, settingsScale)
        SettingsCheckBox.ApplyPalette(check, SettingsSurface, SettingsEditorBack, SettingsMutedText,
                                      SettingsText, SettingsAccent, SettingsCheckTick)
        check.AccessibleName = row.Title.Text
        check.AccessibleDescription = If(row.Description Is Nothing, String.Empty, row.Description.Text)
    End Sub

    Private Function ModernSettingTitle(key As String) As String
        Select Case key
            Case "recipients_overlay" : Return Localization.T("Таблица получателей поверх изображения")
            Case "section_overlay_layout" : Return Localization.T("Вид таблицы получателей")
            Case "recipients_position" : Return Localization.T("Положение таблицы")
            Case "recipients_width" : Return Localization.T("Ширина таблицы")
            Case "recipients_font" : Return Localization.T("Размер текста")
            Case "recipients_opacity" : Return Localization.T("Непрозрачность")
            Case "recipients_rows" : Return Localization.T("Видимые строки")
            Case "recipients_position_reset" : Return Localization.T("Сброс позиции панели получателей")
            Case "section_appearance" : Return Localization.T("Оформление")
            Case "ui_theme" : Return Localization.T("Тема оформления")
            Case "section_background" : Return Localization.T("Фон изображения")
            Case "background_color" : Return Localization.T("Цвет фона")
            Case "perspective" : Return Localization.T("Перспективный фон")
            Case "dynamic_perspective" : Return Localization.T("Динамический ореол")
            Case "animated_perspective" : Return Localization.T("Анимация ореола")
            Case "halo_speed" : Return Localization.T("Скорость анимации ореола")
            Case "section_information" : Return Localization.T("Информация и управление")
            Case "show_picture_size" : Return Localization.T("Размеры изображения")
            Case "show_file_size" : Return Localization.T("Размер файла")
            Case "show_file_date" : Return Localization.T("Дата изменения файла")
            Case "show_info_overlay" : Return Localization.T("Информация поверх изображения")
            Case "wheel_zooms" : Return Localization.T("Масштабирование колёсиком")
            Case "slideshow_interval" : Return Localization.T("Интервал слайдшоу")
            Case "thumbnail_size" : Return Localization.T("Размер карточки изображения")
            Case "section_accessibility" : Return Localization.T("Комфорт просмотра")
            Case "interface_scale" : Return Localization.T("Масштаб интерфейса")
            Case "new_image_scale" : Return Localization.T("Масштаб нового изображения")
            Case "reduce_motion" : Return Localization.T("Уменьшать анимацию")
            Case "chrome_size" : Return Localization.T("Размер панелей")
            Case "chrome_opacity" : Return Localization.T("Непрозрачность панели")
            Case "chrome_corners" : Return Localization.T("Скругление углов панелей")
            Case "chrome_pinned" : Return Localization.T("Не скрывать плавающие панели")
            Case "section_slideshow_behavior" : Return Localization.T("Поведение слайд-шоу")
            Case "slideshow_random_order" : Return Localization.T("Порядок показа")
            Case "stop_slideshow_manual" : Return Localization.T("Останавливать при ручной навигации")
            Case "slideshow_ui" : Return Localization.T("Интерфейс во время слайд-шоу")
            Case "exif_rotate" : Return Localization.T("Автоповорот по EXIF")
            Case "hq_scaling" : Return Localization.T("Качественное масштабирование")
            Case "video_loop" : Return Localization.T("Повторять видео")
            Case "video_mute" : Return Localization.T("Запускать без звука")
            Case "video_volume" : Return Localization.T("Громкость по умолчанию (%):")
            Case "shared_audio_settings" : Return Localization.T("Общий звук: аудио и видео")
            Case "section_video_behavior" : Return Localization.T("Поведение видео")
            Case "include_video_navigation" : Return Localization.T("Учитывать видео при пролистывании файлов")
            Case "video_autoplay" : Return Localization.T("Запускать видео автоматически")
            Case "video_remember_position" : Return Localization.T("Запоминать позицию просмотра")
            Case "video_controls_delay" : Return Localization.T("Задержка скрытия панели управления видео, с")
            Case "video_controls_paused" : Return Localization.T("Показывать панель при паузе")
            Case "video_click_action" : Return Localization.T("Одиночный клик по видео")
            Case "video_end_action" : Return Localization.T("После окончания видео")
            Case "video_seek_short" : Return Localization.T("Короткий шаг перемотки, с")
            Case "video_seek_medium" : Return Localization.T("Средний шаг перемотки, с")
            Case "video_seek_long" : Return Localization.T("Длинный шаг перемотки, с")
            Case "video_hide_cursor" : Return Localization.T("Скрывать указатель мыши через, с (0 - не скрывать)")
            Case "section_audio_behavior" : Return Localization.T("Поведение аудио")
            Case "include_audio_navigation" : Return Localization.T("Учитывать аудио при пролистывании файлов")
            Case "audio_end_action" : Return Localization.T("После окончания аудио")
            Case "audio_controls_visible" : Return Localization.T("Всегда показывать панель аудио")
            Case "audio_visualiser" : Return Localization.T("Визуализатор без обложки")
            Case "audio_sleep_timer" : Return Localization.T("Таймер сна, мин")
            Case "preferred_audio_language" : Return Localization.T("Предпочтительный язык звука")
            Case "preferred_subtitle_language" : Return Localization.T("Предпочтительный язык субтитров")
            Case "advance_after_copy" : Return Localization.T("Переходить к следующему файлу после копирования")
            Case "no_confirmation" : Return Localization.T("Не запрашивать подтверждение")
            Case "image_associations" : Return Localization.T("Форматы изображений")
            Case "video_associations" : Return Localization.T("Форматы видео")
            Case "interface_language" : Return Localization.T("Язык интерфейса")
            Case "section_file_behavior" : Return Localization.T("Поведение файлов")
            Case "name_collision" : Return Localization.T("Совпадение имён")
            Case "after_file_operation" : Return Localization.T("После копирования или перемещения")
            Case "delete_to_recycle_bin" : Return Localization.T("Удалять в Корзину")
            Case "confirm_delete" : Return Localization.T("Спрашивать перед удалением")
            Case "create_missing_destination" : Return Localization.T("Создавать отсутствующий каталог получателя")
            Case "include_subfolders" : Return Localization.T("Просматривать вложенные папки")
            Case "included_extensions" : Return Localization.T("Типы файлов")
            Case "include_document_navigation" : Return Localization.T("Сортировать документы")
            Case "sort_other_files" : Return Localization.T("Сортировать другие файлы")
            Case "document_extensions" : Return Localization.T("Расширения документов")
            Case "text_preview_max_kb" : Return Localization.T("Размер просмотра текста, КБ")
            Case "other_extensions" : Return Localization.T("Расширения других файлов")
            Case "custom_hotkeys" : Return Localization.T("Сочетания клавиш")
            Case "recent_files_limit" : Return Localization.T("Размер истории файлов")
            Case "recent_folders_limit" : Return Localization.T("Размер истории папок")
            Case "recent_history" : Return Localization.T("Недавние файлы и папки")
            Case "startup_open" : Return Localization.T("При запуске открывать")
            Case "resume_last_playback" : Return Localization.T("Продолжать с последнего файла")
            Case "section_archives" : Return Localization.T("Архивы")
            Case "archive_cache_limit" : Return Localization.T("Лимит кэша архивов, МБ")
            Case "archive_entry_limit" : Return Localization.T("Лимит одной записи архива, МБ")
            Case "archive_entries_limit" : Return Localization.T("Лимит записей в архиве")
            Case "section_secure" : Return Localization.T("Защищённые файлы")
            Case "secure_in_navigation" : Return Localization.T("Показывать защищённые файлы в папке")
            Case "secure_remember_minutes" : Return Localization.T("Помнить пароль, минут")
            Case "secure_show_true_name" : Return Localization.T("Показывать настоящее имя файла")
            Case "secure_open_with" : Return Localization.T("Контейнеры .fd-sec")
            Case "live_open_with" : Return Localization.T("Файлы .fmsbcast (live-трансляции)")
            Case "settings_export_personal" : Return Localization.T("Включить личные данные")
            Case "settings_export" : Return Localization.T("Экспорт дополнительных настроек")
            Case "settings_import" : Return Localization.T("Импорт дополнительных настроек")
            Case "settings_defaults" : Return Localization.T("Восстановить по умолчанию")
            Case "welcome_replay" : Return Localization.T("Показать приветственные шаги снова")
            Case "remote_cache_limit" : Return Localization.T("Кэш сетевых ресурсов, МБ")
            Case "thumb_cache_clear" : Return Localization.T("Кэш миниатюр")
            Case "allow_new_windows" : Return Localization.T("Разрешать запуск в новых окнах")
            Case "secondary_instance_warning" : Return Localization.T("Это дополнительное окно. Изменения настроек действуют до его закрытия и не сохраняются - настройки хранит первое окно.")
            Case "ocr_enabled" : Return Localization.T("Включить OCR")
            Case "ocr_auto" : Return Localization.T("Запускать автоматически")
            Case "section_translation" : Return Localization.TC("settings", "Перевод")
            Case "ocr_provider" : Return Localization.T("Сервис перевода")
            Case "ocr_endpoint" : Return Localization.T("Адрес сервиса")
            Case "ocr_server" : Return Localization.T("Локальный сервер Ollama")
            Case "ocr_model" : Return Localization.T("Модель перевода")
            Case "ocr_api" : Return Localization.T("API-ключ")
            Case "ocr_target" : Return Localization.T("Язык перевода")
            Case "section_recognition" : Return Localization.T("Распознавание текста")
            Case "ocr_source" : Return Localization.T("Язык исходного текста")
            Case "ocr_quality" : Return Localization.T("Качество распознавания")
            Case "ocr_mode" : Return Localization.T("Режим OCR")
            Case "ocr_download" : Return Localization.T("Языковые данные")
            Case "section_overlay" : Return Localization.T("Панель перевода")
            Case "ocr_opacity" : Return Localization.T("Непрозрачность панели")
            Case "ocr_overlay_visible" : Return Localization.T("Показывать панель перевода")
            Case "ocr_disk_cache" : Return Localization.T("Кэшировать результаты на диске")
            Case "ocr_cache_limit" : Return Localization.T("Максимальный размер OCR-кэша, МБ")
            Case "ocr_cache_clear" : Return Localization.T("OCR")
            Case "archive_cache_clear" : Return Localization.T("Архивы")
            Case "remote_cache_clear" : Return Localization.T("Сетевые ресурсы")
            Case "decode_cache_limit" : Return Localization.T("Кэш декодирования, МБ")
            Case "decode_cache_clear" : Return Localization.T("Изображения")
            Case "thumb_cache_limit" : Return Localization.T("Кэш миниатюр панели, МБ")
            Case "sftp_intro" : Return Localization.T("Доступ к медиатеке с телефона")
            Case "resources_intro" : Return Localization.T("Серверы и сетевые папки")
            Case "sftp_manager" : Return Localization.T("Публикация папок по SFTP")
            Case "sftp_guide" : Return Localization.T("Инструкция по подключению")
            Case "android_app" : Return Localization.T("Приложение для Android")
            Case "exchange_portal" : Return Localization.T("Доступ из дома и из других сетей")
            Case "about_intro" : Return ProductNames.FullForLabel
            Case "support_send_logs" : Return Localization.T("Отправить логи автору")
            Case "profile_reset_all" : Return Localization.T("Сброс настроек")
            Case "doc_html_intro" : Return "Doc HTML Translate"
            Case "doc_html_site" : Return Localization.T("Сайт Doc HTML Translate")
            Case "project_site" : Return Localization.T("Сайт проекта")
            Case "project_github" : Return "GitHub"
            Case "project_releases" : Return Localization.T("Новые версии")
            Case "project_privacy" : Return Localization.T("Политика конфиденциальности")
            Case "project_license" : Return Localization.T("Лицензия MIT")
            Case "project_trust" : Return Localization.T("Почему Windows предупреждает")
            Case "project_email" : Return Localization.T("Связаться с автором")
            Case Else : Return key
        End Select
    End Function

    Private Function ModernSettingDescription(key As String) As String
        Select Case key
            Case "recipients_overlay" : Return Localization.T("Показывает компактный список папок поверх изображения; закрепляется и открепляется своей булавкой, как панели.")
            Case "recipients_position" : Return Localization.T("Угол области просмотра; панель можно перетащить за заголовок.")
            Case "recipients_width" : Return Localization.T("Ширина панели в пикселях.")
            Case "recipients_font" : Return Localization.T("Размер текста в пунктах.")
            Case "recipients_opacity" : Return Localization.T("Прозрачность фона таблицы в процентах.")
            Case "recipients_rows" : Return Localization.T("Лишние строки будут доступны прокруткой.")
            Case "recipients_position_reset" : Return Localization.T("Возвращает панель в выбранный угол из любого состояния, включая потерянную позицию.")
            Case "ui_theme" : Return Localization.T("Светлая, тёмная или как в Windows - для всех окон просмотрщика и менеджера общего доступа.")
            Case "background_color" : Return Localization.T("Выберите, как вычислять цвет свободной области вокруг фотографии.")
            Case "perspective" : Return Localization.T("Продолжает края фотографии на свободную область экрана.")
            Case "dynamic_perspective" : Return Localization.T("Плавно смешивает продолжение изображения с выбранным фоном.")
            Case "animated_perspective" : Return Localization.T("Проявляет ореол короткой анимацией при открытии следующего файла.")
            Case "halo_speed" : Return Localization.T("Медленная") & ": 350 ms; " & Localization.T("Средняя") & ": 175 ms; " & Localization.T("Быстрая") & ": 88 ms."
            Case "show_picture_size" : Return Localization.T("Показывает ширину и высоту текущего изображения.")
            Case "show_file_size" : Return Localization.T("Добавляет размер текущего файла в информационную строку.")
            Case "show_file_date" : Return Localization.T("Добавляет дату и время последнего изменения файла.")
            Case "show_info_overlay" : Return Localization.T("Показывает имя файла и позицию в списке прямо на изображении.")
            Case "wheel_zooms" : Return Localization.T("Колесо меняет масштаб; при отключении оно листает файлы.")
            Case "slideshow_interval" : Return Localization.T("Базовая пауза между изображениями, в секундах.")
            Case "thumbnail_size" : Return Localization.T("Размер карточек в панели предварительного просмотра.")
            Case "interface_scale" : Return Localization.T("Множитель масштаба Windows для настроек, плавающих панелей и текстового просмотра. Системный - без изменения.")
            Case "new_image_scale" : Return Localization.T("Начальный масштаб при открытии следующего изображения.")
            Case "reduce_motion" : Return Localization.T("Отключает декоративные переходы, анимацию ореола и движение фона у аудио без обложки.")
            Case "chrome_size" : Return Localization.T("Малые, средние или крупные; адаптивный следит и за шириной окна.")
            Case "chrome_opacity" : Return Localization.T("Чем меньше значение, тем прозрачнее плавающие панели. Ниже 50 % надписи и значки инвертируются: чёрное становится белым.")
            Case "chrome_corners" : Return Localization.T("Скругление углов панелей")
            Case "chrome_pinned" : Return Localization.T("Панели не исчезают сами и остаются над изображением постоянно.")
            Case "slideshow_random_order" : Return Localization.T("Выберите обычный, случайный или перемешанный порядок.")
            Case "stop_slideshow_manual" : Return Localization.T("Ручной переход останавливает таймер слайд-шоу.")
            Case "slideshow_ui" : Return Localization.T("Управление и статус можно временно скрывать для просмотра.")
            Case "exif_rotate" : Return Localization.T("Учитывает ориентацию, записанную камерой или телефоном.")
            Case "hq_scaling" : Return Localization.T("Делает уменьшенные изображения резче, используя качественную интерполяцию.")
            Case "video_loop" : Return Localization.T("После окончания видео запускается снова.")
            Case "video_mute" : Return Localization.T("Общий звук: аудио и видео")
            Case "video_volume" : Return Localization.T("Начальная громкость воспроизведения, от 0 до 100 %.") & " " & Localization.T("Общий звук: аудио и видео")
            Case "shared_audio_settings" : Return Localization.T("Общий звук: аудио и видео")
            Case "video_autoplay" : Return Localization.T("Если выключено, видео открывается на паузе.")
            Case "include_video_navigation", "include_audio_navigation" : Return Localization.T("Выключенные типы не показываются в текущей папке и не входят в счётчик.")
            Case "include_document_navigation", "sort_other_files" : Return Localization.T("Вместо картинки - имя, размер и дата. Клавиши сортировки работают как обычно.")
            Case "text_preview_max_kb" : Return Localization.T("Читается только начало файла. Enter открывает файл целиком во внешнем редакторе.")
            Case "document_extensions", "other_extensions" : Return Localization.T("Расширения через пробел или точку с запятой, например .pdf;.txt. Без масок.")
            Case "video_remember_position" : Return Localization.T("Изменившийся файл всегда начинается с начала.")
            Case "video_controls_delay" : Return Localization.T("Через сколько секунд бездействия скрывать панель управления видео.")
            Case "video_controls_paused" : Return Localization.T("Не скрывает управление, пока видео поставлено на паузу.")
            Case "video_click_action" : Return Localization.T("Действие левой кнопки мыши на поверхности видео.")
            Case "video_end_action" : Return Localization.T("Что делать, когда воспроизведение достигло конца.")
            Case "video_seek_short" : Return Localization.T("Прыжок назад или вперёд по Shift+стрелкам, от 1 до 600 секунд.")
            Case "video_seek_medium" : Return Localization.T("Прыжок назад или вперёд по Alt+стрелкам.")
            Case "video_seek_long" : Return Localization.T("Прыжок назад или вперёд по Ctrl+стрелкам.")
            Case "video_hide_cursor" : Return Localization.T("Скрывает указатель после стольких секунд неподвижности во время слайд-шоу и полноэкранного просмотра. 0 - не скрывать.")
            Case "audio_end_action" : Return Localization.T("Что делать, когда аудиодорожка достигла конца.")
            Case "audio_controls_visible" : Return Localization.T("Оставляет транспортную панель видимой во время аудио.")
            Case "audio_visualiser" : Return Localization.T("Показывает фирменные волны и частицы, если в аудио нет встроенной обложки.")
            Case "audio_sleep_timer" : Return Localization.T("Через сколько минут остановить воспроизведение. 0 отключает таймер.")
            Case "preferred_audio_language", "preferred_subtitle_language" : Return Localization.T("Если такой дорожки нет, выбор остаётся за плеером.")
            Case "advance_after_copy" : Return Localization.T("После копирования сразу показывается следующий файл. Снимите галочку, чтобы остаться на текущем.")
            Case "no_confirmation" : Return Localization.T("Отключает вопросы при перекодировании JPEG и операциях панели миниатюр. Удаление настраивается отдельно.")
            Case "image_associations" : Return Localization.T("Регистрирует программу для открытия изображений у текущего пользователя, без прав администратора.")
            Case "video_associations" : Return Localization.T("Регистрирует программу для открытия видео и аудио у текущего пользователя, без прав администратора.")
            Case "interface_language" : Return Localization.T("Выберите, на каком языке показывать интерфейс.")
            Case "name_collision" : Return Localization.T("Что делать, если в папке-получателе уже есть файл с тем же именем.")
            Case "delete_to_recycle_bin" : Return Localization.T("На сетевом диске и съёмном носителе Корзины нет - там файл удаляется безвозвратно, и программа скажет об этом. Shift+DEL всегда удаляет мимо Корзины.")
            Case "confirm_delete" : Return Localization.T("Средний вариант удобен для быстрой разборки: удаление в Корзину проходит молча, а безвозвратное всё равно спросит.")
            Case "after_file_operation" : Return Localization.T("Выберите, что показывать после успешной операции.")
            Case "create_missing_destination" : Return Localization.T("Если у каталога-получателя нет только последней папки, а та, что над ней, доступна - папка создаётся при первом переносе. Выключено - такой каталог считается ненайденным.")
            Case "include_subfolders" : Return Localization.T("Добавляет подходящие файлы из всех вложенных папок.")
            Case "included_extensions" : Return Localization.T("Выберите группы и отдельные форматы. Если отмечено всё - показываются все поддерживаемые.")
            Case "custom_hotkeys" : Return Localization.T("Переназначьте клавиши действий. Системные сочетания, F11 и Esc остаются за программой.")
            Case "recent_files_limit", "recent_folders_limit" : Return Localization.T("0 отключает сохранение новых записей.")
            Case "recent_history" : Return Localization.T("Открыть запись, удалить одну или очистить историю целиком.")
            Case "startup_open" : Return Localization.T("Что будет показано при обычном запуске без файла в командной строке.")
            Case "resume_last_playback" : Return Localization.T("При запуске открыть файл, который был на экране, и продолжить видео с той же позиции. Если файл недоступен - окно остаётся пустым.")
            Case "archive_cache_limit" : Return Localization.T("Сколько места на диске может занимать временная распаковка одного открытого архива.")
            Case "archive_entry_limit" : Return Localization.T("Запись крупнее этого не распаковывается, а показывает честный отказ вместо картинки.")
            Case "archive_entries_limit" : Return Localization.T("Архив с большим числом записей показывается обрезанным до этого количества, и строка состояния предупреждает об этом.")
            Case "secure_in_navigation" : Return Localization.T("Файлы .fd-sec попадают в список папки и считаются в счётчике. Что внутри - неизвестно, пока файл не открыт паролем.")
            Case "secure_remember_minutes" : Return Localization.T("Пароль хранится только в памяти программы и нигде не записывается. 0 - помнить до закрытия программы.")
            Case "secure_show_true_name" : Return Localization.T("Открытый контейнер показывает имя файла, который в нём спрятан - в строке состояния и в подписи поверх картинки. Выключите, если на экран смотрит кто-то ещё.")
            Case "secure_open_with" : Return Localization.T("Добавляет программу в меню «Открыть с помощью» для .fd-sec, ничего не отбирая у той программы, которой эти файлы принадлежат сейчас.")
            Case "live_open_with" : Return Localization.T("Файлы .fmsbcast присылают телефон и часы, когда вещают звук. Если у них ещё нет программы по умолчанию, их будет открывать эта; чужую запись она не заменяет, а только появляется в меню «Открыть с помощью».")
            Case "settings_export_personal" : Return Localization.T("История папок и позиции просмотра попадут в файл. API-ключи и пароли - никогда.")
            Case "settings_export" : Return Localization.T("Файл содержит только дополнительные настройки. Получатели, язык, тема, звук и OCR не включаются.")
            Case "settings_import" : Return Localization.T("Файл содержит только дополнительные настройки. Получатели, язык, тема, звук и OCR не включаются.") & " " & Localization.T("Читает параметры из файла. Прежний профиль сохраняется в резервную копию.")
            Case "allow_new_windows" : Return Localization.T("Каждый запуск открывает отдельное окно. Применяется при следующем запуске.")
            Case "ocr_enabled" : Return Localization.T("Разрешает распознавание текста на открытых изображениях.")
            Case "ocr_auto" : Return Localization.T("Распознаёт текст без отдельной команды после открытия изображения.")
            Case "ocr_provider" : Return Localization.T("Выберите локальный или сетевой движок, который выполнит перевод.")
            Case "ocr_endpoint" : Return Localization.T("URL API выбранного сервиса перевода.")
            Case "ocr_server" : Return Localization.T("Установите или запустите Ollama для локального перевода.")
            Case "ocr_model" : Return Localization.T("Модель Ollama; отсутствующую модель можно загрузить этой же строкой.")
            Case "ocr_api" : Return Localization.T("Ключ доступа нужен только сервисам, которые его требуют.")
            Case "ocr_target" : Return Localization.T("Язык, на который будет переведён распознанный текст.")
            Case "ocr_source" : Return Localization.T("Язык надписей на изображении; автоопределение подходит большинству случаев.")
            Case "ocr_quality" : Return Localization.T("Баланс между скоростью обработки и точностью результата.")
            Case "ocr_mode" : Return Localization.T("Выберите подходящий способ разметки текста на изображении.")
            Case "ocr_download" : Return Localization.T("Загрузите недостающие данные для выбранного языка OCR.")
            Case "ocr_opacity" : Return Localization.T("Непрозрачность фона перевода: от 30 до 100 %.")
            Case "ocr_overlay_visible" : Return Localization.T("Показывает или скрывает уже распознанный перевод поверх изображения.")
            Case "ocr_disk_cache" : Return Localization.T("Сохраняет распознанный текст, чтобы повторно не обрабатывать файл.")
            Case "ocr_cache_limit" : Return Localization.T("0 отключает дисковый кэш; -1 снимает ограничение. При заполнении удаляются старые результаты.")
            Case "ocr_cache_clear" : Return Localization.T("Сколько места занимают сохранённые результаты распознавания и перевода. Очистка не меняет настройки OCR и не удаляет языковые данные - текст распознаётся заново.")
            Case "archive_cache_clear" : Return Localization.T("Сколько места занимают временные копии записей открытых архивов. Очистка убирает только оставшиеся копии - открытый архив продолжит работать.")
            Case "remote_cache_clear" : Return Localization.T("Сколько места занимают временные копии файлов сетевых ресурсов. Очистка не прерывает активные подключения - копия загрузится заново при обращении.")
            Case "decode_cache_limit" : Return Localization.T("Ускоряет повторное открытие анимаций и медленных форматов. 0 отключает кэш.")
            Case "decode_cache_clear" : Return Localization.T("Сколько места занимают сохранённые результаты декодирования. Очистка не меняет сами изображения - они декодируются заново.")
            Case "settings_defaults" : Return Localization.T("Файл содержит только дополнительные настройки. Получатели, язык, тема, звук и OCR не включаются.")
            Case "welcome_replay" : Return Localization.T("Открывает пошаговую настройку: язык, фон, сортировка, удаление и связи файлов.")
            Case "remote_cache_limit" : Return Localization.T("Ограничивает временные копии файлов сетевых ресурсов.")
            Case "thumb_cache_clear" : Return Localization.T("Сколько места занимают сохранённые миниатюры. Очистка не удаляет исходные файлы - миниатюры строятся заново.")
            Case "thumb_cache_limit" : Return Localization.T("Ускоряет повторное открытие панели F3. 0 отключает кэш.")
            Case "sftp_intro" : Return Localization.T("Опубликуйте выбранные папки встроенным SFTP-сервером и открывайте их в мобильном приложении.")
            Case "resources_intro" : Return Localization.T("Сохранённые серверы SFTP и сетевые папки с паролем: открываются как обычные папки.")
            Case "sftp_manager" : Return Localization.T("Открывает отдельное приложение управления SFTP-доступом и опубликованными папками.")
            Case "sftp_guide" : Return Localization.T("Пошаговая настройка сервера, сети и подключения мобильного клиента.")
            Case "android_app" : Return Localization.T("Страница мобильного приложения для Android.")
            Case "exchange_portal" : Return Localization.T("Как открыть папки ПК и телефона на других устройствах: домашняя сеть, проброс порта, постоянный сервер.")
            Case "about_intro" : Return Localization.TF("Современный просмотрщик и сортировщик фото и видео." & vbCrLf & "Версия {0}", ShortSettingsVersion())
            Case "support_send_logs" : Return Localization.T("Соберёт журналы работы программы в архив и создаст письмо автору. Ничего не отправляется без вашего подтверждения.")
            Case "profile_reset_all" : Return Localization.T("Вернёт настройки просмотрщика к значениям по умолчанию. Язык, тема, пароли и история останутся. Прежние значения сохранятся в файл.")
            Case "doc_html_intro" : Return Localization.T("Преобразует EPUB, PDF и другие документы в локальный HTML с оглавлением и переводом в браузере. Для изображений, сканов и комиксов создаёт переводимый OCR-слой.")
            Case "doc_html_site" : Return Localization.T("Описание возможностей, поддерживаемых форматов, установки и работы с документами и изображениями.")
            Case "project_site" : Return Localization.T("Описание возможностей, инструкции и материалы проекта.")
            Case "project_github" : Return Localization.T("Исходный код, задачи и техническая документация.")
            Case "project_releases" : Return Localization.T("Список опубликованных версий и файлов для установки.")
            Case "project_privacy" : Return Localization.T("Как приложение обрабатывает пользовательские данные.")
            Case "project_license" : Return "MIT"
            Case "project_trust" : Return Localization.T("Предупреждение SmartScreen, на что нажать и зачем нужны права администратора.")
            Case "project_email" : Return Localization.T("Написать автору проекта по электронной почте.")
            Case Else : Return String.Empty
        End Select
    End Function

    Private Shared Function ShortSettingsVersion() As String
        Return Application.ProductVersion.Split("+"c)(0)
    End Function

    Private Sub BuildModernDataLayout()
        If modernDataLayoutBuilt Then Return
        modernDataLayoutBuilt = True

        Dim destinations As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_1)
        ' APP-SETTINGS rule 5: the language selector is on the first page.
        AddUiLanguageRow(destinations)
        AddSettingRow(destinations, "recipients_overlay", SetOnTop, 34)
        Dim gridHost As New Panel With {.BackColor = SettingsSurface, .Padding = New Padding(Sp(12))}
        ReparentModernSettingsControl(lbl_Grid_Hint, gridHost)
        lbl_Grid_Hint.Dock = DockStyle.Bottom
        ' Two lines' worth: the hint names both actions now (move on a double-click, copy on
        ' Shift + double-click) and at 34 px the wrapped second line was simply cut off.
        lbl_Grid_Hint.Height = 50
        lbl_Grid_Hint.AutoSize = False
        lbl_Grid_Hint.Padding = New Padding(2, 8, 2, 0)
        ReparentModernSettingsControl(Data_Grid_View, gridHost)
        Data_Grid_View.Dock = DockStyle.Fill
        Data_Grid_View.ScrollBars = ScrollBars.Vertical
        ' Not reachable by Tab, so it is not what WinForms focuses when the page opens:
        ' an AutoScroll container scrolls its newly focused child into view, and the grid
        ' is tall enough that doing so pushed the row above it - the recipients-overlay
        ' switch - out of sight. A click still focuses and edits the grid as before.
        Data_Grid_View.TabStop = False
        AddRecipientGridActions(gridHost)
        destinations.Controls.Add(gridHost)

        Dim viewing As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_2)
        ' The application theme first (spec 060): it is about every window, not the picture, and
        ' the background scheme right below is the picture's - two different questions.
        AddSectionHeader(viewing, "section_appearance")
        AddUiThemeRow(viewing)
        AddSectionHeader(viewing, "section_background")
        AddSettingRow(viewing, "background_color", cmbox_color_schema, 210)
        AddSettingRow(viewing, "perspective", chb_perspectiva, 34, True)
        AddSettingRow(viewing, "dynamic_perspective", chk_Dynamic_Perspective, 34, True)
        AddSettingRow(viewing, "animated_perspective", chk_Animated_Perspective, 34, True)
        AddSettingRow(viewing, "halo_speed", cmb_Halo_Speed, 180, True)
        AddSectionHeader(viewing, "section_information")
        AddSettingRow(viewing, "show_picture_size", chkb_show_pic_size, 34, True)
        AddSettingRow(viewing, "show_file_size", chkb_show_file_size, 34, True)
        AddSettingRow(viewing, "show_file_date", chkb_is_to_show_file_datetime, 34, True)
        AddSettingRow(viewing, "show_info_overlay", chk_Show_Info_Overlay, 34, True)
        AddSettingRow(viewing, "wheel_zooms", chk_Wheel_Zooms, 34, True)
        AddSettingRow(viewing, "slideshow_interval", num_Slideshow_Interval, 100, True)
        AddSettingRow(viewing, "thumbnail_size", cmb_Picture_Size, 180, True)

        Dim video As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_3)
        AddSettingRow(video, "exif_rotate", chk_Exif_AutoRotate, 34, True)
        AddSettingRow(video, "hq_scaling", chk_Hq_Scaling, 34, True)
        chkb_video_loop.Visible = False
        AddSettingRow(video, "video_mute", chk_Video_Mute, 34, True)
        AddSettingRow(video, "video_volume", num_Video_Volume, 100, True)

        Dim audio As FlowLayoutPanel = CreateModernPageFlow(modernAudioPage)

        Dim files As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_4)
        ' The global "copy mode" is gone from this window: copying is now asked for at the
        ' moment of the action (Shift+digit, the recipients overlay, both media menus,
        ' Shift+double-click in the grid), not switched on beforehand and forgotten about.
        ' The control itself stays hidden here for net48 persistence compatibility, exactly
        ' like the independent-thread checkbox below.
        ' SPECIFICATION_COPY_ACTIONS_REWORK.md §4.1.
        chkbox_Copy_Mode.Visible = False
        AddAdvanceAfterCopyRow(files)
        ' .NET 10 always uses the background file-operation queue. Do not expose a
        ' switch that cannot change anything; the hidden control remains for net48
        ' persistence compatibility.
        chkbox_Independent_Thread_For_File_Operation.Visible = False
        AddSettingRow(files, "no_confirmation", chkb_no_request_before_file_operation, 34, True)
        AddSettingRow(files, "image_associations", btn_Set_As_Default, 230, True)
        AddSettingRow(files, "video_associations", btn_Set_As_Default_Video, 230, True)
        ' The language button (a flag menu) is replaced by the first page's selector.
        btn_Language.Visible = False

        Dim ocr As FlowLayoutPanel = CreateModernPageFlow(Tab_Page_5)
        Dim translation As FlowLayoutPanel = CreateModernPageFlow(modernTranslationPage)
        ocrInner.Visible = False
        AddSettingRow(ocr, "ocr_enabled", chkOcrEnabled, 34, True)
        AddSettingRow(ocr, "ocr_auto", chkOcrAuto, 34, True)
        AddSectionHeader(ocr, "section_recognition")
        AddSettingRow(ocr, "ocr_source", cmbOcrSource, 300, True)
        AddSettingRow(ocr, "ocr_quality", cmbOcrQuality, 300, True)
        AddSettingRow(ocr, "ocr_mode", cmbOcrMode, 300, True)
        AddSettingRow(ocr, "ocr_download", btnOcrDownload, 300, True)
        ' The shared status line lives on the Translation page; the download button's own
        ' answer ("already installed", "engine missing") has to show where the button is
        ' (audit 057.S10-2).
        lblOcrRecognitionStatus = New Label With {
            .AutoSize = False, .Height = 34, .Margin = New Padding(8, 4, 8, 8),
            .ForeColor = SettingsMutedText, .AutoEllipsis = True, .Tag = "modern:full"}
        ocr.Controls.Add(lblOcrRecognitionStatus)

        AddSectionHeader(translation, "section_translation")
        AddSettingRow(translation, "ocr_provider", cmbOcrProvider, 260, True)
        AddSettingRow(translation, "ocr_endpoint", txtOcrEndpoint, 300, True)
        AddSettingRow(translation, "ocr_server", MakeModernControlStrip({btnOcrInstallOllama, btnOcrStartOllama}, 310), 310, True)
        AddSettingRow(translation, "ocr_model", MakeModernControlStrip({cmbOcrModelName, btnOcrPullModel}, 310), 310, True)
        AddSettingRow(translation, "ocr_api", txtOcrApi, 300, True)
        AddSettingRow(translation, "ocr_target", cmbOcrTarget, 300, True)
        AddSectionHeader(translation, "section_overlay")
        AddSettingRow(translation, "ocr_opacity", MakeModernOpacityStrip(), 330, True)
        AddSettingRow(translation, "ocr_overlay_visible", chkOcrOverlayVisible, 34, True)
        AddSettingRow(translation, "ocr_disk_cache", chkOcrDisk, 34, True)
        ReparentModernSettingsControl(lblOcrStatus, translation)
        lblOcrStatus.AutoSize = False
        lblOcrStatus.Height = 34
        lblOcrStatus.Margin = New Padding(8, 4, 8, 8)

        BuildModernResourcesPage()
        BuildModernSftpPage()
        BuildModernAboutPage()
        If MultiWindowPolicy.IsSecondaryInstance() Then AddInformationBlock(files, "secondary_instance_warning")
        AddExpandedSettingsRows(destinations, viewing, video, audio, files, ocr)

        For Each oldLabel As Control In New Control() {lbl_Color, lbl_Slideshow_Interval, lbl_Picture_at_Panel_Size,
                                                        lbl_Video_Volume, lblOcrTranslator, lblOcrEndpoint, lblOcrServer,
                                                        lblOcrModel, lblOcrApi, lblOcrTarget, lblOcrSource, lblOcrQuality,
                                                        lblOcrMode, lblOcrOpacity}
            oldLabel.Visible = False
        Next
        For Each oldGroup As GroupBox In New GroupBox() {grp_Background, grp_OnScreen, grp_Slideshow, grp_Panel,
                                                          grp_Quality, grp_Video, grp_FileOps, grp_Integration, grp_Language}
            oldGroup.Visible = False
        Next
    End Sub

    Private Function CreateModernPageFlow(page As TabPage) As FlowLayoutPanel
        Dim flow As New FlowLayoutPanel With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False,
            .Padding = New Padding(6),
            .BackColor = SettingsCanvas
        }
        page.Controls.Add(flow)
        flow.BringToFront()
        modernPageFlows.Add(flow)
        AddHandler flow.ClientSizeChanged, Sub() ResizeModernPageItems(flow)
        Return flow
    End Function

    ''' <summary>The page's preferred value column follows the interface scale (§4.1)
    ''' and is recomputed here from the widest editor it has to hold
    ''' (127 §3.1.3), before the groups pass it down to their rows.</summary>
    Private Sub ResizeModernPageItems(flow As FlowLayoutPanel)
        Dim width As Integer = Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal - Sp(18))
        Dim widestEditor As Integer = 0
        For Each row As ModernSettingRow In modernSettingsRows
            If row.PageFlow IsNot flow OrElse row.Editor Is Nothing OrElse TypeOf row.Editor Is CheckBox Then Continue For
            widestEditor = Math.Max(widestEditor, ToLogicalPx(MeasureModernEditorControl(row)))
        Next
        pageValueColumns(flow) = Sp(SettingsRowGridRules.PageValueColumn(widestEditor))
        Dim useTwoColumns As Boolean = width >= Sp(820)
        For Each child As Control In flow.Controls
            Dim group As SettingsCollapsibleGroup = TryCast(child, SettingsCollapsibleGroup)
            If group IsNot Nothing Then
                group.Width = width
                group.ArrangeContents()
                Continue For
            End If
            Dim compact As Boolean = String.Equals(TryCast(child.Tag, String), "modern:compact", StringComparison.Ordinal)
            child.Width = If(compact AndAlso useTwoColumns, Math.Max(Sp(300), (width - 8) \ 2), width)
        Next
        ' A new page column does not necessarily change host widths (language switches
        ' in particular). Reflow even when WinForms has no Resize event to deliver.
        For Each row As ModernSettingRow In modernSettingsRows
            If row.PageFlow Is flow Then SizeModernSettingRow(row)
        Next
    End Sub

    Private Sub AddSectionHeader(flow As FlowLayoutPanel, key As String)
        Dim host As New Panel With {.Height = Sp(28), .Margin = New Padding(0, 6, 0, 0), .BackColor = SettingsCanvas, .Tag = "modern:full"}
        Dim title As New Label With {.AutoSize = False, .Dock = DockStyle.Fill, .Font = SettingsFont("Segoe UI Semibold", Sf(9.5F)),
                                     .ForeColor = SettingsText, .TextAlign = ContentAlignment.MiddleLeft, .Padding = New Padding(4, 0, 0, 0)}
        host.Controls.Add(title)
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = title})
    End Sub

    ''' <summary>
    ''' One regular settings row of the two-column grid (127 §3.1): the caption and its
    ''' hint wrap inside the label column on the left, the editor sits in the page's
    ''' shared value column on the right, flush to the row's right padding. The column
    ''' shrinks in a narrow window to keep the editor beside the labels.
    ''' </summary>
    Private Sub AddSettingRow(flow As FlowLayoutPanel, key As String, editor As Control, editorWidth As Integer, Optional compact As Boolean = False)
        Dim host As New Panel With {.Height = Sp(56), .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12, 5, 12, 5),
                                   .Tag = If(compact, "modern:compact", "modern:full")}
        Dim title As New Label With {.AutoSize = False, .Location = New Point(Sp(14), Sp(8)), .Height = Sp(20),
                                     .Font = SettingsFont("Segoe UI Semibold", Sf(8.75F)), .ForeColor = SettingsText}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(Sp(14), Sp(31)), .Height = Sp(22),
                                           .Font = SettingsFont("Segoe UI", Sf(8.0F)), .ForeColor = SettingsMutedText,
                                           .AutoEllipsis = True}
        Dim row As New ModernSettingRow With {.Key = key, .Host = host, .Title = title, .Description = description, .Editor = editor, .Compact = compact, .PageFlow = flow}
        Dim check As CheckBox = TryCast(editor, CheckBox)
        Dim numeric As NumericUpDown = TryCast(editor, NumericUpDown)
        ReparentModernSettingsControl(editor, host)

        If check IsNot Nothing Then
            ' Boolean rows keep the box immediately before the caption and the hint
            ' beneath the caption. Label clicks toggle the original control.
            ConfigureModernCheckbox(check, row)
            title.Cursor = Cursors.Hand
            description.Cursor = Cursors.Hand
            title.Tag = check
            description.Tag = check
            AddHandler title.Click, AddressOf ToggleModernCheckboxFromText
            AddHandler description.Click, AddressOf ToggleModernCheckboxFromText
        ElseIf numeric IsNot Nothing Then
            AddModernNumericUnit(row, key)
        ElseIf TypeOf editor Is Button Then
            ' The glyph first, so the measured width below already counts it (WINDOWS-UI section 11 item 3).
            ApplySettingsCommandGlyph(DirectCast(editor, Button), key)
            StyleModernCommandButton(DirectCast(editor, Button))
        End If

        host.Controls.Add(title)
        host.Controls.Add(description)
        If row.UnitLabel IsNot Nothing Then host.Controls.Add(row.UnitLabel)
        title.BringToFront()
        description.BringToFront()
        AddHandler host.Resize, Sub() SizeModernSettingRow(row)
        AddHandler editor.EnabledChanged, Sub() RefreshSettingsDependencies()
        flow.Controls.Add(host)
        modernSettingsRows.Add(row)
        SizeModernSettingRow(row)
    End Sub

    Private Shared Sub ToggleModernCheckboxFromText(sender As Object, e As EventArgs)
        Dim label As Label = TryCast(sender, Label)
        Dim check As CheckBox = If(label Is Nothing, Nothing, TryCast(label.Tag, CheckBox))
        If check Is Nothing OrElse Not check.Enabled Then Return
        check.Checked = Not check.Checked
        check.Focus()
    End Sub

    ''' <summary>The muted unit suffix of 127 §3.2: immediately left of a numeric editor.
    ''' The slideshow interval is the one key whose backing store carries decimals - a
    ''' whole-seconds value renders "10", never "10.000".</summary>
    Private Sub AddModernNumericUnit(row As ModernSettingRow, key As String)
        Dim unit As String = ModernSettingUnit(key)
        If unit.Length = 0 Then Return
        Dim label As New Label With {.AutoSize = False, .Text = unit,
                                     .Font = SettingsPixelFont("Segoe UI", 12), .ForeColor = SettingsMutedText,
                                     .TextAlign = ContentAlignment.MiddleRight, .BackColor = SettingsSurface}
        row.Host.Controls.Add(label)
        row.UnitLabel = label
        If key = "slideshow_interval" Then
            AddHandler DirectCast(row.Editor, NumericUpDown).ValueChanged, AddressOf RefreshSlideshowIntervalDecimals
            RefreshSlideshowIntervalDecimals(row.Editor, EventArgs.Empty)
        End If
    End Sub

    Private Shared Sub RefreshSlideshowIntervalDecimals(sender As Object, e As EventArgs)
        Dim number As NumericUpDown = TryCast(sender, NumericUpDown)
        If number Is Nothing Then Return
        ' Decimals only when the backing key carries them: 1.5 s shows "1.5", 10 s shows "10".
        Dim needed As Integer = If(number.Value = Decimal.Truncate(number.Value), 0, 3)
        If number.DecimalPlaces <> needed Then number.DecimalPlaces = needed
    End Sub

    ''' <summary>The unit suffix per key, from the units the captions already name. Captions
    ''' and hints stay with 120 (127 §3.6) - this is editor presentation only.</summary>
    Private Shared Function ModernSettingUnit(key As String) As String
        Select Case key
            Case "slideshow_interval", "video_controls_delay", "video_seek_short", "video_seek_medium", "video_seek_long", "video_hide_cursor"
                Return "s"
            Case "audio_sleep_timer", "secure_remember_minutes"
                Return "min"
            Case "recipients_opacity", "video_volume"
                Return "%"
            Case "archive_cache_limit", "archive_entry_limit", "archive_entries_limit", "ocr_cache_limit", "decode_cache_limit", "thumb_cache_limit", "remote_cache_limit"
                Return "MB"
            Case "text_preview_max_kb"
                Return "KB"
            Case "recipients_width", "chrome_corners"
                Return "px"
            Case Else
                Return String.Empty
        End Select
    End Function

    ''' <summary>The value column of the row's page, in device pixels. Computed per page by
    ''' <see cref="ResizeModernPageItems"/> from the widest editor on the page; until that
    ''' has run, the floor applies.</summary>
    Private Function PageValueColumnWidth(flow As FlowLayoutPanel) As Integer
        Dim value As Integer = Nothing
        If flow IsNot Nothing AndAlso pageValueColumns.TryGetValue(flow, value) Then Return value
        Return Sp(SettingsRowGridRules.MinValueColumnWidth)
    End Function

    Private Function ToLogicalPx(devicePixels As Integer) As Integer
        If settingsScale <= 0.0F Then Return devicePixels
        Return CInt(Math.Round(devicePixels / settingsScale))
    End Function

    ''' <summary>Text-like editors (endpoints, the editable model field) take the whole
    ''' value column; fixed ones keep their measured width.</summary>
    Private Function RowEditorFillsColumn(row As ModernSettingRow) As Boolean
        Dim editor As Control = row.Editor
        If editor Is Nothing Then Return False
        If TypeOf editor Is TextBox Then Return True
        Dim combo As ComboBox = TryCast(editor, ComboBox)
        If combo IsNot Nothing Then Return combo.DropDownStyle <> ComboBoxStyle.DropDownList
        Dim strip As FlowLayoutPanel = TryCast(editor, FlowLayoutPanel)
        If strip IsNot Nothing Then
            For Each child As Control In strip.Controls
                Dim inner As ComboBox = TryCast(child, ComboBox)
                If inner IsNot Nothing AndAlso inner.DropDownStyle <> ComboBoxStyle.DropDownList Then Return True
                If TypeOf child Is TextBox Then Return True
            Next
        End If
        Return False
    End Function

    ''' <summary>The width of the editor CONTROL itself (unit suffix excluded), device px.</summary>
    Private Function MeasureModernEditorControl(row As ModernSettingRow) As Integer
        Dim editor As Control = row.Editor
        If editor Is Nothing Then Return 0
        If TypeOf editor Is CheckBox Then Return Sp(28)
        If TypeOf editor Is NumericUpDown Then Return Sp(SettingsRowGridRules.NumericEditorWidth)
        Dim combo As ComboBox = TryCast(editor, ComboBox)
        If combo IsNot Nothing Then
            If combo.DropDownStyle = ComboBoxStyle.DropDownList Then Return MeasureModernComboWidth(combo)
            Return Sp(200)
        End If
        If TypeOf editor Is TextBox Then Return Sp(200)
        Dim button As Button = TryCast(editor, Button)
        If button IsNot Nothing Then Return MeasureModernCommandButtonWidth(button)
        Dim strip As FlowLayoutPanel = TryCast(editor, FlowLayoutPanel)
        If strip IsNot Nothing Then Return MeasureModernStripWidth(strip)
        ' A slider host (the track bar with its exact numeric companion) or a similar panel.
        Return Sp(SettingsRowGridRules.SliderEditorWidth + SettingsRowGridRules.SliderCompanionWidth)
    End Function

    ''' <summary>A closed drop-down field is sized for its longest localized item plus the
    ''' chevron zone, clamped to the §3.2 bounds.</summary>
    Private Function MeasureModernComboWidth(combo As ComboBox) As Integer
        Dim font As Font = SettingsPixelFont("Segoe UI", 12)
        Dim widest As Integer = 0
        For Each item As Object In combo.Items
            Dim text As String = If(TypeOf item Is LanguageEntry, DirectCast(item, LanguageEntry).DisplayName(), combo.GetItemText(item))
            If String.IsNullOrEmpty(text) Then Continue For
            widest = Math.Max(widest, TextRenderer.MeasureText(text, font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width)
        Next
        Dim field As Integer = widest + Sp(10) * 2 + Sp(36) + Sp(6)
        Return Math.Max(Sp(160), Math.Min(Sp(340), field))
    End Function

    ''' <summary>A command button fits its text with 16 logical px of horizontal padding
    ''' (127 §3.2) - never docked, never stretched.</summary>
    Private Sub StyleModernCommandButton(button As Button)
        button.Dock = DockStyle.None
        button.AutoSize = False
        button.Height = Sp(SettingsRowGridRules.ButtonHeight)
        button.TextAlign = ContentAlignment.MiddleCenter
    End Sub

    Private Function MeasureModernCommandButtonWidth(button As Button) As Integer
        Dim text As String = button.Text
        If String.IsNullOrEmpty(text) Then Return Sp(96)
        Dim textWidth As Integer = TextRenderer.MeasureText(text, SettingsFont("Segoe UI Semibold", Sf(9.0F)),
            New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width
        ' WINDOWS-UI section 11 item 3: the verb's glyph and its gap come on top of the caption, never out of it.
        ' The picture holds its own gap; the settings scale (which already includes the monitor's DPI) is the
        ' floor, because the bitmap may have been drawn before the window knew its final monitor.
        Dim glyphWidth As Integer = 0
        If ButtonGlyphs.CarriesGlyph(button) Then
            glyphWidth = Math.Max(Sp(ButtonGlyphs.GlyphLogicalSize + ButtonGlyphs.GapLogicalSize), ButtonGlyphs.ExtraWidth(button))
        End If
        Return Math.Max(Sp(64), textWidth + glyphWidth + 2 * Sp(SettingsRowGridRules.ButtonHorizontalPadding))
    End Function

    ''' <summary>The ONE table from a settings command to its verb's glyph (WINDOWS-UI section 11): the id is
    ''' the row key of a button that is a row's editor, or the control name of a button inside a strip.
    ''' Every settings-page command is Neutral - colour is spent on dialog answers only (item 5). Header tools,
    ''' navigation and group disclosure buttons are not here: they have pictures of their own.</summary>
    Private Function SettingsCommandGlyph(id As String) As String
        Select Case id
            Case "image_associations", "video_associations"
                Return If(AssociationUx.ModeFor(Main_Form.IsPackagedApp()) = AssociationMode.OpenWindowsSettings,
                          "nav.open-external", "action.install")
            Case "included_extensions", "document_extensions", "other_extensions", "custom_hotkeys", "shared_audio_settings"
                Return "action.adjust"
            Case "recent_history"
                Return "nav.open-external"
            Case "settings_export"
                Return "action.export"
            Case "settings_import"
                Return "action.import"
            Case "settings_defaults", "profile_reset_all", "recipients_position_reset"
                Return "action.reset"
            Case "welcome_replay"
                Return "app.help"
            Case "sftp_manager"
                Return "action.share"
            Case "support_send_logs"
                Return "action.email"
            Case "ocr_download", "btnOcrPullModel"
                Return "action.download"
            Case "btnOcrInstallOllama"
                Return "action.install"
            Case "btnOcrStartOllama"
                Return "action.run"
            Case "settingsCacheRefresh"
                Return "action.refresh"
            Case "settingsCacheClear"
                Return "action.clear-all"
            Case "settingsSlotBrowse"
                Return "action.choose-folder"
            Case "settingsSlotResources"
                Return "source.resource"
            Case "settingsSlotClear"
                Return "action.clear-input"
            Case Else
                Return String.Empty
        End Select
    End Function

    ''' <summary>Gives a settings command its glyph, Neutral, from <see cref="SettingsCommandGlyph"/>. Safe to
    ''' call again (the verb of a toggling caption is applied by its own refresh); a button with no entry
    ''' keeps exactly what it had.</summary>
    Private Sub ApplySettingsCommandGlyph(button As Button, id As String)
        If button Is Nothing OrElse String.IsNullOrEmpty(id) Then Return
        Dim glyphId As String = SettingsCommandGlyph(id)
        If glyphId.Length = 0 Then Return
        ButtonGlyphs.Apply(button, glyphId, ButtonGlyphRole.Neutral)
    End Sub

    ''' <summary>The natural width of a strip: its controls' own widths plus the 8 px gaps.</summary>
    Private Function MeasureModernStripWidth(strip As FlowLayoutPanel) As Integer
        Dim total As Integer = 0
        For Each child As Control In strip.Controls
            Dim childWidth As Integer
            Dim button As Button = TryCast(child, Button)
            If button IsNot Nothing Then
                childWidth = MeasureModernCommandButtonWidth(button)
            ElseIf TypeOf child Is Label AndAlso child.AutoSize Then
                childWidth = TextRenderer.MeasureText(child.Text, child.Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width
            Else
                childWidth = child.Width
            End If
            total += childWidth + child.Margin.Horizontal
        Next
        ' MakeModernControlStrip already carries each gap in the preceding margin.
        Return Math.Max(Sp(64), total)
    End Function

    ''' <summary>
    ''' Sizes one row with the editor beside its labels at every window width.
    ''' Also the single owner of the host's height, so the Resize
    ''' handler, localization and layout passes all end here.
    ''' </summary>
    Private Sub SizeModernSettingRow(row As ModernSettingRow)
        If row.Description Is Nothing OrElse row.Arranging Then Return
        row.Arranging = True
        Try
            Dim host As Panel = DirectCast(row.Host, Panel)
            Dim hostWidth As Integer = host.ClientSize.Width
            Dim valueColumn As Integer = Sp(SettingsRowGridRules.FitValueColumn(
                ToLogicalPx(hostWidth), ToLogicalPx(PageValueColumnWidth(row.PageFlow))))
            Dim check As CheckBox = TryCast(row.Editor, CheckBox)
            Dim twoColumn As Boolean = row.Editor IsNot Nothing AndAlso check Is Nothing

            ' The shared label column: everything left of the page's value column.
            Dim labelLeft As Integer = Sp(14)
            If check IsNot Nothing Then labelLeft += check.Width + Sp(CheckBoxLayout.CaptionGapLogical)
            Dim labelWidth As Integer = If(twoColumn,
                Math.Max(Sp(40), Sp(SettingsRowGridRules.LabelColumnWidth(ToLogicalPx(hostWidth), ToLogicalPx(valueColumn)))),
                Math.Max(Sp(40), hostWidth - labelLeft - Sp(14)))

            ' Heights come from the fonts, so they follow the interface scale on their own.
            row.Title.AutoEllipsis = False
            Dim titleHeight As Integer = Math.Max(Sp(20), TextRenderer.MeasureText(row.Title.Text, row.Title.Font,
                New Size(Math.Max(1, labelWidth), Integer.MaxValue), TextFormatFlags.WordBreak).Height + Sp(4))
            row.Description.AutoEllipsis = False
            Dim descriptionHeight As Integer = Math.Max(Sp(18), TextRenderer.MeasureText(row.Description.Text, row.Description.Font,
                New Size(Math.Max(1, labelWidth), Integer.MaxValue), TextFormatFlags.WordBreak).Height + Sp(4))
            Dim titleTop As Integer = Sp(7)
            If twoColumn Then titleTop = Math.Max(titleTop, Sp(4) + (row.Editor.Height - titleHeight) \ 2)
            If check IsNot Nothing Then titleTop = Math.Max(titleTop, Sp(4) + (check.Height - titleHeight) \ 2)
            row.Title.SetBounds(labelLeft, titleTop, labelWidth, titleHeight)
            row.Description.SetBounds(labelLeft, row.Title.Bottom + Sp(2), labelWidth, descriptionHeight)
            row.Description.AutoEllipsis = True

            Dim editor As Control = row.Editor
            If editor Is Nothing Then
                host.Height = row.Description.Bottom + Sp(7)
                Return
            End If

            If check IsNot Nothing Then
                check.Left = Sp(14)
                check.Top = row.Title.Top + (row.Title.Height - check.Height) \ 2
                host.Height = Math.Max(row.Description.Bottom + Sp(7), check.Bottom + Sp(8))
                Return
            End If

            Dim unitWidth As Integer = 0
            If row.UnitLabel IsNot Nothing Then
                ' The generous slack: GDI+ measures a pixel font tighter than the label
                ' draws it, and a clipped unit ("p" for "px") reads as a typo.
                row.UnitLabel.Width = Math.Max(Sp(20), TextRenderer.MeasureText(row.UnitLabel.Text, row.UnitLabel.Font,
                    New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width + Sp(12))
                unitWidth = row.UnitLabel.Width + Sp(4)
            End If

            Dim controlWidth As Integer = Math.Max(Sp(24), MeasureModernEditorControl(row))
            If twoColumn Then
                controlWidth = If(RowEditorFillsColumn(row), Math.Max(Sp(24), valueColumn - unitWidth),
                                  Math.Min(controlWidth, Math.Max(Sp(24), valueColumn - unitWidth)))
                editor.Width = controlWidth
                editor.Left = hostWidth - Sp(12) - controlWidth
                ' The editor is centred on the CAPTION line, not on the whole row (§3.1.4):
                ' a two-line hint never pushes its control off its caption.
                editor.Top = row.Title.Top + (row.Title.Height - editor.Height) \ 2
                If row.UnitLabel IsNot Nothing Then
                    row.UnitLabel.Height = editor.Height
                    row.UnitLabel.Left = editor.Left - Sp(4) - row.UnitLabel.Width
                    row.UnitLabel.Top = editor.Top
                    row.UnitLabel.BringToFront()
                End If
                host.Height = Math.Max(row.Description.Bottom + Sp(7), editor.Top + editor.Height + Sp(8))
            End If
        Finally
            row.Arranging = False
        End Try
    End Sub

    ''' <summary>A strip of commands beside a description: every button text-fitted at the
    ''' uniform 32 px height, the strip as wide as its controls plus the gaps between them
    ''' (carried in the controls' own right margins), right aligned in the value column by
    ''' the row grid (127 §3.2). The proportional width split for command buttons is gone;
    ''' only an editable field beside a command (the model field and Pull) flexes, through
    ''' <see cref="LayoutModernControlStrip"/>. The strip never sizes ITSELF: its width and
    ''' position belong to the row grid alone.</summary>
    Private Function MakeModernControlStrip(controls As Control(), width As Integer) As FlowLayoutPanel
        Dim strip As New FlowLayoutPanel With {.Width = Sp(width), .Height = Sp(40), .FlowDirection = FlowDirection.LeftToRight,
                                               .WrapContents = False, .Margin = Padding.Empty, .Padding = Padding.Empty,
                                               .BackColor = SettingsSurface}
        For index As Integer = 0 To controls.Length - 1
            Dim control As Control = controls(index)
            ReparentModernSettingsControl(control, strip)
            control.Dock = DockStyle.None
            control.Anchor = AnchorStyles.Top Or AnchorStyles.Left
            control.Margin = If(index < controls.Length - 1,
                                New Padding(0, Sp(2), Sp(SettingsRowGridRules.StripGap), Sp(2)),
                                New Padding(0, Sp(2), 0, Sp(2)))
            Dim button As Button = TryCast(control, Button)
            If button IsNot Nothing Then
                ' A strip's commands are found by their control names (the table keys them).
                ApplySettingsCommandGlyph(button, button.Name)
                StyleModernCommandButton(button)
            End If
        Next
        AddHandler strip.Resize, Sub() LayoutModernControlStrip(strip)
        For Each control As Control In controls
            AddHandler control.TextChanged, Sub() LayoutModernControlStrip(strip)
        Next
        LayoutModernControlStrip(strip)
        Return strip
    End Function

    ''' <summary>Buttons keep their text-fitted width; only an editable field beside a
    ''' command (model field + Pull) flexes to the strip's width.</summary>
    Private Sub LayoutModernControlStrip(strip As FlowLayoutPanel)
        Dim fixedTotal As Integer = 0
        Dim field As Control = Nothing
        For Each child As Control In strip.Controls
            Dim button As Button = TryCast(child, Button)
            If button IsNot Nothing Then
                StyleModernCommandButton(button)
                button.Width = MeasureModernCommandButtonWidth(button)
                fixedTotal += button.Width + child.Margin.Horizontal
            ElseIf TypeOf child Is ComboBox AndAlso DirectCast(child, ComboBox).DropDownStyle = ComboBoxStyle.DropDownList Then
                Dim list As ComboBox = DirectCast(child, ComboBox)
                list.Width = MeasureModernComboWidth(list)
                fixedTotal += list.Width + child.Margin.Horizontal
            ElseIf TypeOf child Is TextBox OrElse TypeOf child Is ComboBox Then
                field = child
            Else
                fixedTotal += child.Width + child.Margin.Horizontal
            End If
        Next
        If field IsNot Nothing Then
            field.Width = Math.Max(Sp(120), strip.ClientSize.Width - fixedTotal - field.Margin.Horizontal)
        End If
        Dim requiredHeight As Integer = Sp(SettingsRowGridRules.ButtonHeight)
        For Each child As Control In strip.Controls
            requiredHeight = Math.Max(requiredHeight, child.Height + child.Margin.Vertical)
        Next
        strip.Height = requiredHeight
    End Sub

    Private Function MakeModernOpacityStrip() As Panel
        Dim strip As New Panel With {.Width = Sp(SettingsRowGridRules.SliderEditorWidth + SettingsRowGridRules.SliderCompanionWidth), .Height = Sp(48), .Margin = Padding.Empty, .Padding = Padding.Empty,
                                     .BackColor = SettingsSurface}
        ReparentModernSettingsControl(trkOcrOpacity, strip)
        trkOcrOpacity.Location = Point.Empty
        trkOcrOpacity.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        ReparentModernSettingsControl(lblOcrOpacityVal, strip)
        lblOcrOpacityVal.AutoSize = False
        lblOcrOpacityVal.Visible = True
        lblOcrOpacityVal.Width = Sp(SettingsRowGridRules.SliderCompanionWidth)
        lblOcrOpacityVal.Height = Sp(34)
        lblOcrOpacityVal.TextAlign = ContentAlignment.MiddleLeft
        lblOcrOpacityVal.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        lblOcrOpacityVal.BringToFront()
        AddHandler strip.Resize,
            Sub()
                lblOcrOpacityVal.Width = Sp(SettingsRowGridRules.SliderCompanionWidth)
                lblOcrOpacityVal.Left = Math.Max(0, strip.ClientSize.Width - lblOcrOpacityVal.Width)
                lblOcrOpacityVal.Top = Sp(6)
                trkOcrOpacity.Width = Math.Max(Sp(100), lblOcrOpacityVal.Left)
            End Sub
        Return strip
    End Function

    ''' <summary>The resources page (tickets 118 and 131): the shared panel the manager dialog
    ''' hosts too, embedded once, reading top-to-bottom as introduction, actions, list, status.
    ''' The panel is not an AddSettingRow - a list is not a label+editor row - and its host no
    ''' longer sits at a fixed 380 px: the host takes the page's remaining height with a 320
    ''' logical px floor (131 §3.1), so the list answers the window. The Open action lives in
    ''' the panel's action row as its primary command (131 §3.2) and answers ItemActivated the
    ''' same way a double-click does: the window closes first, the viewer connects (120 §15).
    ''' The removal/add/import facts travel to the main form through the panel's
    ''' ResourcesChanged and the window's own ResourcesTouched / RemovedResourceIds (13.4).</summary>
    Private Sub BuildModernResourcesPage()
        If modernResourcesPage Is Nothing OrElse resourcesPanel IsNot Nothing Then Return
        Dim flow As FlowLayoutPanel = CreateModernPageFlow(modernResourcesPage)
        AddInformationBlock(flow, "resources_intro")

        resourcesPanel = New Resource_List_Panel(settingsScale, withPrimaryOpen:=True, deviceScale:=True,
                                                 openShareManager:=Sub() Main_Form.OpenShareManagerWindow()) With {
            .Dock = DockStyle.Fill, .Tag = "modern:full", .BackColor = SettingsSurface}
        AddHandler resourcesPanel.ResourcesChanged,
            Sub(removedIds As IReadOnlyList(Of String))
                resourcesTouchedInSettings = True
                For Each id As String In removedIds
                    If Not settingsRemovedResourceIdList.Contains(id) Then settingsRemovedResourceIdList.Add(id)
                Next
                ResizeResourcesListHost()
            End Sub
        AddHandler resourcesPanel.ItemActivated,
            Sub(resource As ResourceDescriptor)
                If resource Is Nothing Then Return
                Close()
                Main_Form.BeginInvoke(New Action(Sub() Main_Form.ConnectAndEnterResource(resource, String.Empty)))
            End Sub
        ' The window outlives a language switch; the panel's captions travel with it.
        Dim host As New Panel With {.BackColor = SettingsSurface, .Padding = New Padding(12, 0, 12, 0),
                                    .Margin = New Padding(0, 0, 6, 6), .Tag = "modern:full"}
        host.Controls.Add(resourcesPanel)
        flow.Controls.Add(host)
        host.BringToFront()
        ' The column strip's height is an estimate until the list has its handle; the
        ' first exact measure settles the table's height.
        AddHandler resourcesPanel.HandleCreated, Sub() BeginInvoke(New MethodInvoker(Sub() SizeResourcesListHost(host)))
        expandedRefreshActions.Add(Sub()
                                       resourcesPanel.Localize()
                                       SizeResourcesListHost(host)
                                   End Sub)
        SizeResourcesListHost(host)
    End Sub

    ''' <summary>The content-sized list host: the table stands at the height of its own
    ''' rows - more rows, a taller table - so the page's height belongs to the sections
    ''' below it and no longer to the list. Re-run on every refill, scale change and
    ''' language switch.</summary>
    Private Sub SizeResourcesListHost(host As Panel)
        resourcesPanel.SetDeviceScale(settingsScale)
        host.Height = resourcesPanel.PreferredHeight()
    End Sub

    ''' <summary>The same measure through the panel's own host, for the places that hold
    ''' no local reference (the DPI change).</summary>
    Private Sub ResizeResourcesListHost()
        If resourcesPanel Is Nothing OrElse resourcesPanel.Parent Is Nothing Then Return
        SizeResourcesListHost(DirectCast(resourcesPanel.Parent, Panel))
    End Sub

    ''' <summary>True when anything in the resources list was changed while the window was open;
    ''' the main form reads it (and <see cref="SettingsRemovedResourceIds"/>) after ShowDialog.</summary>
    Friend ReadOnly Property ResourcesTouched As Boolean
        Get
            Return resourcesTouchedInSettings
        End Get
    End Property

    ''' <summary>Ids removed while the window was open, for closing a resource that is on screen.</summary>
    Friend ReadOnly Property SettingsRemovedResourceIds As IReadOnlyList(Of String)
        Get
            Return settingsRemovedResourceIdList
        End Get
    End Property

    Private Sub BuildModernSftpPage()
        Dim flow As FlowLayoutPanel = PageFlow(modernResourcesPage)
        AddInformationBlock(flow, "sftp_intro")
        If btn_Share_Manager IsNot Nothing Then AddSettingRow(flow, "sftp_manager", btn_Share_Manager, 230, True)
        AddProjectLinkRow(flow, "sftp_guide", SitePages.ForLanguage("https://serzhyale.github.io/FastMediaSorter_Lite/publish-folders-android.html", Localization.CurrentCode))
        AddProjectLinkRow(flow, "android_app", SitePages.AndroidAppSiteUrl(Localization.CurrentCode))
        ' The portal is the shared "media exchange" section of the product family, on the Android
        ' app's site: the page about reaching folders from home and from other networks.
        AddProjectLinkRow(flow, "exchange_portal", SitePages.ExchangePortalUrl("home-network", Localization.CurrentCode))
    End Sub

    Private Sub BuildModernAboutPage()
        Dim flow As FlowLayoutPanel = CreateModernPageFlow(modernAboutPage)
        AddInformationBlock(flow, "about_intro")
        ' Support first, project links after: this row is looked for in trouble, the
        ' links out of curiosity (SPECIFICATION_SEND_LOGS_TO_AUTHOR.md §2).
        EnsureSendLogsButton()
        AddSettingRow(flow, "support_send_logs", btn_Send_Logs, 230, True)
        EnsureResetAllButton()
        AddSettingRow(flow, "profile_reset_all", btn_Reset_All_Settings, 230, True)
        AddInformationBlock(flow, "doc_html_intro")
        AddProjectLinkRow(flow, "doc_html_site", SitePages.DocHtmlTranslateSiteUrl(Localization.CurrentCode))
        AddProjectLinkRow(flow, "project_site", SitePages.ForLanguage("https://serzhyale.github.io/FastMediaSorter_Lite/", Localization.CurrentCode))
        AddProjectLinkRow(flow, "project_github", "https://github.com/SerZhyAle/FastMediaSorter_Lite")
        AddProjectLinkRow(flow, "project_releases", "https://github.com/SerZhyAle/FastMediaSorter_Lite/releases")
        ' The policy lives under docs/ - the root /privacy.html this row used to open is a 404.
        AddProjectLinkRow(flow, "project_privacy", SitePages.ForLanguage(SitePages.PrivacyPageUrl, Localization.CurrentCode))
        AddProjectLinkRow(flow, "project_license", "https://github.com/SerZhyAle/FastMediaSorter_Lite/blob/main/LICENSE")
        ' Beside the privacy page, because it answers the same question from the other side:
        ' the warnings Windows raises and what administrator rights are used for (INSTALL-TRUST
        ' rule 7, ticket 037 Ф-4). In the language the settings window is speaking.
        AddProjectLinkRow(flow, "project_trust", SitePages.TrustPageUrl(Localization.CurrentCode))
        AddProjectLinkRow(flow, "project_email", "mailto:" & Author_Email & "?subject=" & Uri.EscapeDataString(ProductNames.Full))
    End Sub

    Private Sub AddInformationBlock(flow As FlowLayoutPanel, key As String)
        Dim host As New Panel With {.Height = 62, .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12), .Tag = "modern:full"}
        Dim title As New Label With {.AutoSize = False, .Location = New Point(12, 6), .Height = 20,
                                     .Font = SettingsFont("Segoe UI Semibold", 9.5F), .ForeColor = SettingsText}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(12, 27), .Height = 30,
                                           .Font = SettingsFont("Segoe UI", 8.0F), .ForeColor = SettingsMutedText, .AutoEllipsis = True}
        host.Controls.Add(title)
        host.Controls.Add(description)
        AddHandler host.Resize,
            Sub()
                title.Width = host.ClientSize.Width - 24
                description.Width = host.ClientSize.Width - 24
            End Sub
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = title, .Description = description})
    End Sub

    Private Sub AddProjectLinkRow(flow As FlowLayoutPanel, key As String, url As String)
        Dim host As New Panel With {.Height = 50, .Margin = New Padding(0, 0, 6, 6), .BackColor = SettingsSurface, .Padding = New Padding(12), .Tag = "modern:compact"}
        Dim link As New LinkLabel With {.AutoSize = False, .Location = New Point(12, 6), .Height = 19,
                                        .Font = SettingsFont("Segoe UI Semibold", 8.75F), .Tag = url,
                                        .TextAlign = ContentAlignment.MiddleLeft}
        Dim description As New Label With {.AutoSize = False, .Location = New Point(12, 26), .Height = 18,
                                           .Font = SettingsFont("Segoe UI", 8.0F), .ForeColor = SettingsMutedText, .AutoEllipsis = True}
        AddHandler link.LinkClicked, AddressOf ModernProjectLinkClicked
        host.Controls.Add(link)
        host.Controls.Add(description)
        AddHandler host.Resize,
            Sub()
                link.Width = host.ClientSize.Width - 24
                description.Width = host.ClientSize.Width - 24
            End Sub
        flow.Controls.Add(host)
        modernSettingsRows.Add(New ModernSettingRow With {.Key = key, .Host = host, .Title = link, .Description = description, .Compact = True})
    End Sub

    Private Sub ModernProjectLinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
        Dim link As LinkLabel = TryCast(sender, LinkLabel)
        If link Is Nothing Then Return
        Try
            Process.Start(New ProcessStartInfo(Convert.ToString(link.Tag)) With {.UseShellExecute = True})
        Catch ex As Exception
            ' The action is the address itself, so the user can open it by hand
            ' (APP-BEHAVIOUR rule 6, ticket 028 Ф-3); the exception goes to the log.
            AppFileLogger.LogException("Settings: opening a project link", ex)
            ViewerBox.Show(Me, Localization.TF("Не удалось открыть ссылку в браузере. Откройте её вручную: {0}", Convert.ToString(link.Tag)),
                            Me.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub LoadSystemSettingsPalette()
        If SystemInformation.HighContrast Then
            settingsDarkMode = GetSettingsBrightness(SystemColors.Window) < 128
            SettingsCanvas = SystemColors.Control
            SettingsSurface = SystemColors.Window
            SettingsSidebar = SystemColors.Control
            SettingsSidebarMuted = SystemColors.ControlText
            SettingsAccent = SystemColors.Highlight
            SettingsLink = SystemColors.HotTrack
            SettingsText = SystemColors.WindowText
            SettingsMutedText = SystemColors.GrayText
            SettingsBorder = SystemColors.ControlDark
            SettingsEditorBack = SystemColors.Window
            SettingsEditorText = SystemColors.WindowText
            SettingsOnAccent = SystemColors.HighlightText
            SettingsCheckTick = SystemColors.HighlightText
            Return
        End If

        ' The application theme decides (spec 060): system, light or dark - resolved once, for
        ' every window of both programs. This window used to read Windows' own setting itself,
        ' which was right only while "system" was the only mode there was.
        settingsDarkMode = ThemeService.IsDark
        SettingsAccent = SystemColors.Highlight
        ' Ink that survives on the accent (selected rows, checked boxes, list highlights)
        ' is a palette decision, not a brightness branch at each call site (127 §3.5).
        If settingsDarkMode Then
            SettingsCanvas = Color.FromArgb(30, 31, 34)
            SettingsSurface = Color.FromArgb(39, 41, 45)
            SettingsSidebar = Color.FromArgb(25, 26, 29)
            SettingsSidebarMuted = Color.FromArgb(185, 188, 195)
            SettingsText = Color.FromArgb(244, 245, 247)
            SettingsMutedText = Color.FromArgb(177, 181, 189)
            SettingsBorder = Color.FromArgb(68, 71, 78)
            SettingsEditorBack = Color.FromArgb(50, 52, 57)
            SettingsEditorText = Color.FromArgb(244, 245, 247)
            ' A standard accent blue is too dark and saturated for small text on
            ' a charcoal surface. Keep links recognisably blue, but use a calm,
            ' high-contrast tint that is comfortable in a long list of links.
            SettingsLink = Color.FromArgb(145, 205, 245)
        Else
            SettingsCanvas = Color.FromArgb(245, 246, 248)
            SettingsSurface = SystemColors.Window
            SettingsSidebar = Color.FromArgb(238, 240, 244)
            SettingsSidebarMuted = SystemColors.GrayText
            SettingsText = SystemColors.WindowText
            SettingsMutedText = SystemColors.GrayText
            SettingsBorder = Color.FromArgb(214, 217, 223)
            SettingsEditorBack = SystemColors.Window
            SettingsEditorText = SystemColors.WindowText
            SettingsLink = SystemColors.HotTrack
        End If
        ' The checkbox needs a white tick AND a fill distinguishable from its row.
        ' Extreme custom Windows highlights cannot satisfy both; use the standard blue
        ' in that case. High contrast retains the OS highlight pair above.
        SettingsCheckTick = Color.White
        If Utils.ContrastRatio(SettingsAccent, SettingsSurface) < 3.0 OrElse
           Utils.ContrastRatio(SettingsCheckTick, SettingsAccent) < 3.0 Then
            SettingsAccent = Color.FromArgb(0, 120, 215)
        End If
        SettingsOnAccent = Utils.GetOppositeColor(SettingsAccent)
    End Sub

    Private Sub ApplySystemSettingsPalette()
        If Not modernSettingsBuilt OrElse modernSettingsShell Is Nothing Then Return

        BackColor = SettingsCanvas
        modernSettingsShell.BackColor = SettingsCanvas
        modernSettingsSidebar.BackColor = SettingsSidebar
        modernSettingsHeader.BackColor = SettingsSurface
        modernSettingsContent.BackColor = SettingsCanvas
        modernSettingsFooter.BackColor = SettingsSurface
        Tab_Control.BackColor = SettingsCanvas
        modernProductLabel.ForeColor = SettingsText
        modernSidebarSectionLabel.ForeColor = SettingsSidebarMuted
        modernSettingsTitle.ForeColor = SettingsText
        modernSettingsSubtitle.ForeColor = SettingsMutedText
        LinkLabel1.LinkColor = SettingsLink
        LinkLabel1.ActiveLinkColor = SettingsLink
        LinkLabel1.VisitedLinkColor = SettingsLink

        For Each page As TabPage In Tab_Control.TabPages
            page.BackColor = SettingsCanvas
        Next
        For Each flow As FlowLayoutPanel In modernPageFlows
            flow.BackColor = SettingsCanvas
        Next
        For Each row As ModernSettingRow In modernSettingsRows
            row.Host.BackColor = If(row.Description Is Nothing, SettingsCanvas, SettingsSurface)
            row.Title.ForeColor = SettingsText
            If row.Description IsNot Nothing Then row.Description.ForeColor = SettingsMutedText
        Next

        ApplyModernPaletteToTree(modernSettingsShell)

        Data_Grid_View.BackgroundColor = SettingsSurface
        Data_Grid_View.GridColor = SettingsBorder
        Data_Grid_View.ColumnHeadersDefaultCellStyle.BackColor = BlendSettingsColor(SettingsSurface, SettingsCanvas, 0.7F)
        Data_Grid_View.ColumnHeadersDefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.BackColor = SettingsSurface
        Data_Grid_View.DefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.SelectionBackColor = BlendSettingsColor(SettingsAccent, SettingsSurface, 0.72F)
        Data_Grid_View.DefaultCellStyle.SelectionForeColor = SettingsText

        ' The resources panel owns the colours the walker above has no branch for (13.1, 131 §3.3):
        ' the whole panel follows the same palette as the grid here, Open takes the accent, and
        ' the status roles come from the application theme's own palette.
        resourcesPanel?.ApplyPalette(New ResourcePanelPalette With {
            .Surface = SettingsSurface, .Text = SettingsText, .Muted = SettingsMutedText, .Border = SettingsBorder,
            .EditorBack = SettingsEditorBack, .EditorText = SettingsEditorText,
            .Accent = SettingsAccent, .OnAccent = SettingsOnAccent,
            .Success = ThemeService.Palette.Success, .Warning = ThemeService.Palette.Warning,
            .Danger = ThemeService.Palette.Danger, .Dark = settingsDarkMode})

        RefreshModernSettingsHeader()
        For Each group As SettingsCollapsibleGroup In settingsGroups.Values
            ' The header band of 127 §3.4: a blended fill, no border, so a group header
            ' reads as a band and not as one more row - and the summary line under the
            ' caption is muted small text.
            group.Header.ForeColor = SettingsText
            group.Header.BackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.08F)
            group.Header.FlatAppearance.BorderColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.08F)
            group.Header.FlatAppearance.BorderSize = 0
            group.SetSummaryStyle(SettingsFont("Segoe UI", Sf(8.0F)), SettingsMutedText)
        Next
        ApplySettingsTitleBarTheme()
        ThemeApplier.ApplyNativeTree(Me, settingsDarkMode)
        ThemeApplier.ApplyButtonCaptions(Me, ThemePalette.ForDark(settingsDarkMode))
        RefreshSettingsDependencies()
        ApplySlotHealthToGrid()
        Invalidate(True)
    End Sub

    Private Sub ApplyModernPaletteToTree(parent As Control)
        For Each control As Control In parent.Controls
            If TypeOf control Is FlowLayoutPanel Then
                control.BackColor = If(modernSettingsSidebar.Contains(control), SettingsSidebar,
                    If(modernPageFlows.Contains(DirectCast(control, FlowLayoutPanel)), SettingsCanvas, SettingsSurface))
            ElseIf TypeOf control Is Panel Then
                Dim row As ModernSettingRow = modernSettingsRows.Find(Function(candidate) candidate.Host Is control)
                If row IsNot Nothing Then
                    control.BackColor = If(row.Description Is Nothing, SettingsCanvas, SettingsSurface)
                ElseIf control IsNot modernSettingsSidebar AndAlso control IsNot modernSettingsHeader AndAlso
                       control IsNot modernSettingsContent AndAlso control IsNot modernSettingsFooter Then
                    ' Composite editors have panels several levels below a row. Their original
                    ' literal fill must follow the palette just like the row itself.
                    control.BackColor = If(modernSettingsSidebar.Contains(control), SettingsSidebar, SettingsSurface)
                End If
            ElseIf TypeOf control Is Button Then
                Dim button As Button = DirectCast(control, Button)
                If modernSettingsNavButtons Is Nothing OrElse Array.IndexOf(modernSettingsNavButtons, button) < 0 Then
                    button.UseVisualStyleBackColor = False
                    button.BackColor = SettingsSurface
                    button.ForeColor = SettingsLink
                    button.FlatAppearance.BorderColor = SettingsBorder
                    button.FlatAppearance.MouseOverBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.1F)
                    button.FlatAppearance.MouseDownBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.18F)
                End If
            ElseIf TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse TypeOf control Is NumericUpDown OrElse
                   TypeOf control Is ListBox OrElse TypeOf control Is ListView OrElse TypeOf control Is TreeView Then
                control.BackColor = SettingsEditorBack
                control.ForeColor = SettingsEditorText
                ' A visual-styles drop-down list ignores BackColor and stayed white on the dark
                ' cards; the flat style paints with it (spec 060).
                Dim combo As ComboBox = TryCast(control, ComboBox)
                If combo IsNot Nothing Then combo.FlatStyle = If(settingsDarkMode, FlatStyle.Flat, FlatStyle.Standard)
                Dim number As NumericUpDown = TryCast(control, NumericUpDown)
                If number IsNot Nothing Then SettingsNumericStepper.Apply(number, settingsScale, SettingsEditorBack,
                                                                         SettingsEditorText, SettingsBorder, SettingsAccent)
            ElseIf TypeOf control Is TrackBar OrElse TypeOf control Is RadioButton Then
                control.BackColor = SettingsSurface
                control.ForeColor = SettingsText
            ElseIf TypeOf control Is TabControl OrElse TypeOf control Is TabPage Then
                control.BackColor = SettingsCanvas
            ElseIf TypeOf control Is CheckBox Then
                Dim box As CheckBox = DirectCast(control, CheckBox)
                If SettingsCheckBox.IsAttached(box) Then
                    SettingsCheckBox.ApplyPalette(box, SettingsSurface, SettingsEditorBack, SettingsMutedText,
                                                  SettingsText, SettingsAccent, SettingsCheckTick)
                Else
                    control.BackColor = SettingsSurface
                    control.ForeColor = SettingsText
                End If
            ElseIf TypeOf control Is LinkLabel Then
                Dim link As LinkLabel = DirectCast(control, LinkLabel)
                link.BackColor = Color.Transparent
                link.LinkColor = SettingsLink
                link.ActiveLinkColor = SettingsLink
                link.VisitedLinkColor = SettingsLink
            ElseIf TypeOf control Is Label Then
                ' Units, slider values and cache usage labels used explicit fills at construction.
                ' Inherit the current parent surface instead of retaining that first theme.
                control.BackColor = Color.Transparent
                ' Titles and descriptions already carry their semantic colour.
                If control IsNot modernSettingsTitle AndAlso control IsNot modernProductLabel Then
                    If Not modernSettingsRows.Exists(Function(row) row.Title Is control OrElse row.Description Is control) Then
                        control.ForeColor = SettingsMutedText
                    End If
                End If
            End If

            If control.HasChildren Then ApplyModernPaletteToTree(control)
        Next
    End Sub

    ' --- the application theme (spec 060 rule 10) -----------------------------------------
    ' This window THEMES ITSELF: it had a complete two-palette implementation before the
    ' application theme existed (cards, sidebar, the destination grid), so the service does not
    ' walk it - it only tells it when the resolved theme changed, and a system light/dark switch
    ' reaches it the same way (ThemeService follows UserPreferenceChanged for every window).

    Private ReadOnly Property ThemesItself As Boolean Implements IThemeAware.ThemesItself
        Get
            Return True
        End Get
    End Property

    Private Sub ApplyTheme(palette As ThemePalette) Implements IThemeAware.ApplyTheme
        If IsDisposed OrElse Not modernSettingsBuilt Then Return
        LoadSystemSettingsPalette()
        ApplySystemSettingsPalette()
    End Sub

    ''' <summary>The F2 path shows this instance modally and never disposes it, so the next open
    ''' reuses the same window - one that may have been hidden while the theme changed, and a
    ''' hidden window is not always among the open forms the service repaints (053 F-09 was the
    ''' same trap with the old per-window subscription). Catch up on every show.</summary>
    Private Sub ModernSettingsVisibleChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        If Not Visible Then
            SaveSettingsContext()
            Return
        End If
        If settingsDarkMode <> ThemeService.IsDark AndAlso Not SystemInformation.HighContrast Then
            LoadSystemSettingsPalette()
            ApplySystemSettingsPalette()
        End If
        PositionModernSettingsOnOwnerMonitor()
        QueueRestoreSettingsContext()
    End Sub

    Private Sub ApplySettingsTitleBarTheme()
        If Not IsHandleCreated Then Return
        Try
            Dim enabled As Integer = If(settingsDarkMode, 1, 0)
            DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, enabled, Marshal.SizeOf(Of Integer)())
        Catch
            ' Older Windows builds may not support this DWM attribute.
        End Try
    End Sub

    Private Shared Function GetSettingsBrightness(color As Color) As Integer
        Return CInt((CInt(color.R) * 299 + CInt(color.G) * 587 + CInt(color.B) * 114) / 1000)
    End Function

    Private Shared Function BlendSettingsColor(baseColor As Color, overlay As Color, overlayAmount As Single) As Color
        Dim amount As Single = Math.Max(0.0F, Math.Min(1.0F, overlayAmount))
        Return Color.FromArgb(
            CInt(CSng(baseColor.R) + (CSng(overlay.R) - CSng(baseColor.R)) * amount),
            CInt(CSng(baseColor.G) + (CSng(overlay.G) - CSng(baseColor.G)) * amount),
            CInt(CSng(baseColor.B) + (CSng(overlay.B) - CSng(baseColor.B)) * amount))
    End Function

    ''' <summary>Lightens (positive) or darkens (negative) a palette colour without naming
    ''' literal whites and blacks - hover and pressed states, 127 §3.5.</summary>
    Private Shared Function ScaleSettingsColor(baseColor As Color, amount As Single) As Color
        Dim target As Single = If(amount >= 0.0F, 255.0F, 0.0F)
        Dim a As Single = Math.Max(0.0F, Math.Min(1.0F, Math.Abs(amount)))
        Return Color.FromArgb(
            CInt(CSng(baseColor.R) + (target - CSng(baseColor.R)) * a),
            CInt(CSng(baseColor.G) + (target - CSng(baseColor.G)) * a),
            CInt(CSng(baseColor.B) + (target - CSng(baseColor.B)) * a))
    End Function

    Private Sub StyleModernSettingsControls()
        For Each group As GroupBox In New GroupBox() {grp_Background, grp_OnScreen, grp_Slideshow, grp_Panel, grp_Quality, grp_Video, grp_FileOps, grp_Integration, grp_Language}
            group.BackColor = SettingsSurface
            group.ForeColor = SettingsText
            group.Font = SettingsFont("Segoe UI Semibold", 10.0F, FontStyle.Regular)
            group.FlatStyle = FlatStyle.Flat
            group.Padding = New Padding(16, 20, 16, 12)
            group.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            AddHandler group.Paint, AddressOf PaintModernSettingsCard
        Next

        Data_Grid_View.BackgroundColor = SettingsSurface
        Data_Grid_View.BorderStyle = BorderStyle.None
        Data_Grid_View.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        Data_Grid_View.GridColor = SettingsBorder
        Data_Grid_View.EnableHeadersVisualStyles = False
        Data_Grid_View.ColumnHeadersDefaultCellStyle.BackColor = BlendSettingsColor(SettingsSurface, SettingsCanvas, 0.7F)
        Data_Grid_View.ColumnHeadersDefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.ColumnHeadersDefaultCellStyle.Font = SettingsFont("Segoe UI Semibold", 9.0F, FontStyle.Regular)
        Data_Grid_View.DefaultCellStyle.BackColor = SettingsSurface
        Data_Grid_View.DefaultCellStyle.ForeColor = SettingsText
        Data_Grid_View.DefaultCellStyle.SelectionBackColor = BlendSettingsColor(SettingsAccent, SettingsSurface, 0.72F)
        Data_Grid_View.DefaultCellStyle.SelectionForeColor = SettingsText
        Data_Grid_View.Font = SettingsPixelFont("Segoe UI", 12)
        Dim rowHeight As Integer = Math.Max(Sp(30), Data_Grid_View.Font.Height + Sp(10))
        Data_Grid_View.RowTemplate.Height = rowHeight
        For Each row As DataGridViewRow In Data_Grid_View.Rows
            row.Height = rowHeight
        Next
        Dim gridHost As Panel = TryCast(Data_Grid_View.Parent, Panel)
        If gridHost IsNot Nothing Then
            Dim gridHeight As Integer = Data_Grid_View.ColumnHeadersHeight + Data_Grid_View.Rows.Count * rowHeight + Sp(4)
            Dim otherHeight As Integer = gridHost.Padding.Vertical
            For Each child As Control In gridHost.Controls
                If child Is Data_Grid_View Then Continue For
                otherHeight += Math.Max(child.Height, If(TypeOf child Is FlowLayoutPanel, Sp(44), 0))
            Next
            gridHost.Height = gridHeight + otherHeight + Sp(8)
        End If
        Data_Grid_View.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right

        ' Placement can move rows to new pages. Style the actual tab collection so
        ' General, Audio, Storage and future pages receive the same editor metrics.
        For Each page As TabPage In Tab_Control.TabPages
            StyleModernSettingsControlTree(page)
        Next

        ' OCR is built in code and has its own inner tabs. Give that otherwise
        ' classic grey control the same light surface as the rest of Settings.
        If ocrInner IsNot Nothing Then
            ocrInner.BackColor = SettingsCanvas
            ocrInner.Appearance = TabAppearance.FlatButtons
            ocrTabTranslate.BackColor = SettingsSurface
            ocrTabTranslate.UseVisualStyleBackColor = False
            ocrTabRecognition.BackColor = SettingsSurface
            ocrTabRecognition.UseVisualStyleBackColor = False
        End If
    End Sub

    ''' <summary>Repaints the legacy GroupBox border as a quiet rounded settings card.
    ''' The child controls remain owned by the original GroupBox, so existing layout
    ''' and code-behind do not have to know that its visual chrome changed.</summary>
    Private Sub PaintModernSettingsCard(sender As Object, e As PaintEventArgs)
        Dim card As GroupBox = TryCast(sender, GroupBox)
        If card Is Nothing OrElse card.Width < 8 OrElse card.Height < 8 Then Return

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(1, 1, card.ClientSize.Width - 3, card.ClientSize.Height - 3)
        Using path As GraphicsPath = ModernRoundedRectangle(rect, 10)
            Using fill As New SolidBrush(SettingsSurface)
                e.Graphics.FillPath(fill, path)
            End Using
            Using border As New Pen(SettingsBorder)
                e.Graphics.DrawPath(border, path)
            End Using
        End Using

        Dim titleRect As New Rectangle(16, 7, Math.Max(0, card.ClientSize.Width - 32), 20)
        Using titleFont As New Font("Segoe UI Semibold", 10.0F, FontStyle.Regular)
            TextRenderer.DrawText(e.Graphics, card.Text, titleFont, titleRect, SettingsText,
                                  TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
        End Using
    End Sub

    Private Shared Function ModernRoundedRectangle(rect As Rectangle, radius As Integer) As GraphicsPath
        Dim d As Integer = Math.Min(Math.Min(radius * 2, rect.Width), rect.Height)
        Dim path As New GraphicsPath()
        If d <= 1 Then
            path.AddRectangle(rect)
            Return path
        End If

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90)
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90)
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90)
        path.CloseFigure()
        Return path
    End Function

    Private Sub StyleModernSettingsControlTree(parent As Control)
        For Each control As Control In parent.Controls
            Dim button As Button = TryCast(control, Button)
            If button IsNot Nothing Then
                button.FlatStyle = FlatStyle.Flat
                button.FlatAppearance.BorderColor = SettingsBorder
                button.FlatAppearance.BorderSize = 1
                button.FlatAppearance.MouseOverBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.1F)
                button.FlatAppearance.MouseDownBackColor = BlendSettingsColor(SettingsSurface, SettingsAccent, 0.18F)
                button.UseVisualStyleBackColor = False
                button.BackColor = SettingsSurface
                button.ForeColor = SettingsAccent
                Dim group As SettingsCollapsibleGroup = TryCast(button.Parent, SettingsCollapsibleGroup)
                button.Font = SettingsFont("Segoe UI Semibold", Sf(If(group IsNot Nothing AndAlso group.Header Is button, 10.5F, 9.0F)), FontStyle.Regular)
                ThemeApplier.ApplyButtonCaptions(button, ThemePalette.ForDark(settingsDarkMode))
            ElseIf TypeOf control Is ComboBox OrElse TypeOf control Is TextBox OrElse TypeOf control Is NumericUpDown Then
                control.BackColor = SettingsEditorBack
                control.ForeColor = SettingsEditorText
                ' Pixel-sized, so a native control is exactly as tall as its neighbours at any
                ' display scale instead of taking whatever size the system picks for a point font.
                control.Font = SettingsPixelFont("Segoe UI", 12)
                Dim number As NumericUpDown = TryCast(control, NumericUpDown)
                If number IsNot Nothing Then
                    number.Font = SettingsPixelFont("Segoe UI", SettingsRowGridRules.NumericFontPixels)
                    ' WinForms fixes a spinner's height to its native text metrics.
                    ' Size through the font instead of requesting an ignored height.
                    number.Height = number.PreferredHeight
                    SettingsNumericStepper.Apply(number, settingsScale, SettingsEditorBack,
                                                 SettingsEditorText, SettingsBorder, SettingsAccent)
                End If
                Dim list As ComboBox = TryCast(control, ComboBox)
                If list IsNot Nothing Then StyleModernComboBox(list)
            ElseIf TypeOf control Is CheckBox Then
                control.BackColor = SettingsSurface
                control.ForeColor = SettingsText
                control.Font = SettingsFont("Segoe UI", Sf(9.0F), FontStyle.Regular)
            ElseIf TypeOf control Is Label Then
                Dim isTitle As Boolean = modernSettingsRows.Exists(Function(r) r.Title Is control)
                If isTitle Then
                    control.ForeColor = SettingsText
                Else
                    control.ForeColor = SettingsMutedText
                    control.Font = SettingsFont("Segoe UI", Sf(8.0F), FontStyle.Regular)
                End If
            End If

            ' UpDownBase owns its internal edit and arrow controls. They inherit the
            ' numeric font; walking them as ordinary text fields overwrites that font.
            If control.HasChildren AndAlso Not TypeOf control Is UpDownBase Then StyleModernSettingsControlTree(control)
        Next
    End Sub

    Private Sub LayoutModernSettingsPages()
        If Not modernSettingsBuilt OrElse Tab_Control Is Nothing Then Return
        For Each flow As FlowLayoutPanel In modernPageFlows
            ResizeModernPageItems(flow)
            ' WinForms can retain the old horizontal extent after a font/language or width
            ' change, even though every current group fits. Recompute it without losing Y.
            If flow.HorizontalScroll.Visible Then
                Dim offset = flow.AutoScrollPosition.Y
                Dim wasRestoring = restoringSettingsContext
                restoringSettingsContext = True
                Try
                    flow.AutoScroll = False
                    ResizeModernPageItems(flow)
                    flow.AutoScroll = True
                    ResizeModernPageItems(flow)
                    flow.PerformLayout()
                    flow.AutoScrollPosition = New Point(0, Math.Max(0, -offset))
                Finally
                    restoringSettingsContext = wasRestoring
                End Try
            End If
        Next
    End Sub
End Class
#End If
