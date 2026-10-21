Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' One collapsible group card of the manager settings pages (spec 238): a tinted header
''' bar - bold caption, state-marker triangle (COLLAPSIBLE-GROUP 1.1 section 3) - over the block's rows, framed by a hairline
''' border that gives every subordinate section a visible boundary on the shared page.
'''
''' The mechanics are the main window's <see cref="CollapsibleSection"/>, kept because
''' each of them was paid for once:
''' - the header is DRAWN, not composed of child labels: the marker scales with the window,
'''   the caption wraps instead of clipping, and the RTL mirror is a property read;
''' - toggling never resizes the window from in here, and the setter performs its own
'''   layout before raising <see cref="ExpandedChanged"/> - an AutoSize control caches its
'''   preferred size, and hiding a docked child does not clear that cache, so a fold
'''   without that call "gives nothing back";
''' - the keyboard opens the card as well as the mouse does (Space/Enter toggle,
'''   Left/Right fold, mirrored in RTL), and the state travels to assistive tech;
''' - palette colours only - the band, the border and the ink are read from the theme at
'''   paint time, never hardcoded.
''' </summary>
Public NotInheritable Class SettingsGroupCard
    Inherits Panel

    Private ReadOnly _header As CardHeader
    Private ReadOnly _body As TableLayoutPanel
    Private _expanded As Boolean

    Public Event ExpandedChanged As EventHandler

    ''' <summary>Fills the card. The constructor leaves the card and its body suspended -
    ''' rows are appended while the whole settings build is held, and
    ''' <c>ReleaseSettingsLayout</c> resumes them once, the same handoff the
    ''' <see cref="CollapsibleSection"/> sections get from <c>CreateSettingsSection</c>.</summary>
    Public Sub New(caption As String, defaultExpanded As Boolean)
        _expanded = defaultExpanded
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Dock = DockStyle.Top
        Margin = New Padding(0, 10, 0, 10)
        ' The 1 px frame the border paints into; the children dock inside it.
        Padding = New Padding(1)
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                 ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
        BackColor = If(SystemInformation.HighContrast, SystemColors.Control, ThemeService.Palette.SurfaceWindow)
        ' A screen reader announces the card as the region its header captions.
        AccessibleRole = AccessibleRole.Grouping

        _body = New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 1,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Margin = New Padding(0), .Padding = New Padding(12, 4, 12, 12), .Visible = defaultExpanded}
        _body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _header = New CardHeader(caption) With {.Dock = DockStyle.Top, .Expanded = defaultExpanded}
        AddHandler _header.Toggled, Sub() Expanded = Not Expanded

        ' Body first, header second: WinForms docks from the END of the collection, so the
        ' header takes the top edge and the body lands under it.
        Controls.Add(_body)
        Controls.Add(_header)
        SuspendLayout()
        _body.SuspendLayout()
    End Sub

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        ' Parent tables need the resulting height, not another recursive measurement of
        ' every editor for each preferred-size probe (the CollapsibleSection recipe).
        Return New Size(0, Padding.Vertical + _header.Height + If(_expanded, _body.Height, 0))
    End Function

    ''' <summary>The caption on the header bar - the refined group caption (238 section 3.2),
    ''' refreshed in place on a live language switch.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Caption As String
        Get
            Return _header.Title
        End Get
        Set(value As String)
            _header.Title = value
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Expanded As Boolean
        Get
            Return _expanded
        End Get
        Set(value As Boolean)
            If _expanded = value Then Return
            ' Hold the card and its ancestors up to the scrolling viewport, so the fold is
            ' one layout pass and the page's dock/rail geometry does not move (WINDOWS-UI
            ' section 5).
            Dim held As New List(Of Control)()
            Dim ancestor As Control = Me
            While ancestor IsNot Nothing AndAlso Not TypeOf ancestor Is Form
                ancestor.SuspendLayout()
                held.Add(ancestor)
                Dim viewport = TryCast(ancestor, ScrollableControl)
                If viewport IsNot Nothing AndAlso viewport.AutoScroll Then Exit While
                ancestor = ancestor.Parent
            End While
            Try
                If Not value AndAlso _body.ContainsFocus Then _header.Focus()
                _expanded = value
                _header.Expanded = value
                _body.Visible = value
                ' Not optional: AutoSize caches the preferred size, and hiding a docked child
                ' does not clear that cache - the card would keep reporting the height of the
                ' body it just folded away (the measured 398-for-26 defect of the section).
                Me.PerformLayout()
                RaiseEvent ExpandedChanged(Me, EventArgs.Empty)
            Finally
                For i = held.Count - 1 To 0 Step -1
                    held(i).ResumeLayout(True)
                Next
            End Try
        End Set
    End Property

    ''' <summary>The clickable, focusable header bar - the scroll anchor a subordinate-item
    ''' reveal brings into view.</summary>
    Friend ReadOnly Property Header As Control
        Get
            Return _header
        End Get
    End Property

    ''' <summary>Host for the block's rows - a single-column auto-height panel.</summary>
    Friend ReadOnly Property Body As TableLayoutPanel
        Get
            Return _body
        End Get
    End Property

    ''' <summary>Appends a control on a new auto-height row of the body. Adjacent commands
    ''' share a wrapping row (WINDOWS-UI section 11 item 3), the section's own rule.</summary>
    Friend Sub AddBodyRow(c As Control)
        If c Is Nothing Then Return
        Dim button = TryCast(c, Button)
        If button IsNot Nothing Then
            button.Dock = DockStyle.None
            button.Anchor = AnchorStyles.Top Or AnchorStyles.Left
            button.AutoSize = True
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink
            Dim commands As FlowLayoutPanel = Nothing
            If _body.RowCount > 0 Then
                commands = TryCast(_body.GetControlFromPosition(0, _body.RowCount - 1), FlowLayoutPanel)
                If commands IsNot Nothing AndAlso Not Object.Equals(commands.Tag, "sectionCommands") Then commands = Nothing
            End If
            If commands Is Nothing Then
                commands = New FlowLayoutPanel With {
                    .Tag = "sectionCommands", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .Dock = DockStyle.Top, .FlowDirection = FlowDirection.LeftToRight,
                    .WrapContents = True, .Margin = Padding.Empty}
                AddBodyRow(commands)
            End If
            commands.Controls.Add(button)
            Return
        End If
        Dim row As Integer = _body.RowCount
        _body.RowCount = row + 1
        _body.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        _body.Controls.Add(c, 0, row)
    End Sub

    ''' <summary>The theme pass of the settings window re-inks the cards with the palette.</summary>
    Friend Sub ApplyPalette(palette As ThemePalette)
        BackColor = If(SystemInformation.HighContrast, SystemColors.Control, palette.SurfaceWindow)
        _header.Invalidate()
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Using pen As New Pen(If(SystemInformation.HighContrast, SystemColors.ControlDark, ThemeService.Palette.Border))
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1)
        End Using
    End Sub

    ''' <summary>One blend of the theme both the nav rail's fill and this band read - here
    ''' at the milder strength a resting card header wants.</summary>
    Friend Shared Function HeaderFill(palette As ThemePalette) As Color
        If SystemInformation.HighContrast Then Return SystemColors.Control
        Return Color.FromArgb(CInt(palette.SurfaceWindow.R * 0.88 + palette.Accent.R * 0.12),
            CInt(palette.SurfaceWindow.G * 0.88 + palette.Accent.G * 0.12),
            CInt(palette.SurfaceWindow.B * 0.88 + palette.Accent.B * 0.12))
    End Function

    ' ------------------------------------------------------------------------------
    ''' <summary>
    ''' The card's header bar: state marker (the shared filled triangle), bold caption, no summary - the page itself answers
    ''' "what is this". Owner-drawn for the reasons in the card's remark; keyboard- and
    ''' screen-reader-reachable because a fold that only a mouse can open hides things.
    ''' </summary>
    Private NotInheritable Class CardHeader
        Inherits Control

        Private _title As String
        Private _expanded As Boolean
        Private _hot As Boolean
        Private _markerScale As Single

        Public Event Toggled As EventHandler

        Public Sub New(title As String)
            _title = If(title, "")
            TabStop = True
            Margin = New Padding(0)
            MinimumSize = New Size(0, 44)
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            ' What turns an owner-drawn row into something a screen reader announces as an
            ' expandable control rather than skipping silently.
            AccessibleRole = AccessibleRole.ButtonDropDown
            Height = 44
            UpdateAccessibleName()
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Title As String
            Get
                Return _title
            End Get
            Set(value As String)
                _title = If(value, "")
                UpdateAccessibleName()
                FitTitle()
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Expanded As Boolean
            Get
                Return _expanded
            End Get
            Set(value As Boolean)
                If _expanded = value Then Return
                _expanded = value
                UpdateAccessibleName()
                Invalidate()
            End Set
        End Property

        ''' <summary>The caption, so a screen reader hears what the folded bar shows.</summary>
        Private Sub UpdateAccessibleName()
            AccessibleName = _title
            AccessibleDescription = If(_expanded, "expanded", "collapsed")
        End Sub

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New HeaderAccessibility(Me)
        End Function

        Private NotInheritable Class HeaderAccessibility
            Inherits ControlAccessibleObject

            Private ReadOnly header As CardHeader

            Public Sub New(owner As CardHeader)
                MyBase.New(owner)
                header = owner
            End Sub

            Public Overrides ReadOnly Property State As AccessibleStates
                Get
                    Return MyBase.State Or If(header.Expanded, AccessibleStates.Expanded, AccessibleStates.Collapsed)
                End Get
            End Property

            Public Overrides Sub DoDefaultAction()
                header.ActivateHeader()
            End Sub
        End Class

        Private Sub ActivateHeader()
            RaiseEvent Toggled(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnFontChanged(e As EventArgs)
            MyBase.OnFontChanged(e)
            FitTitle()
        End Sub

        Protected Overrides Sub OnSizeChanged(e As EventArgs)
            MyBase.OnSizeChanged(e)
            FitTitle()
        End Sub

        ''' <summary>One line where the caption fits, wrapped captions where they do not -
        ''' a clipped group name is a hidden group (7.8's rule at card scale).</summary>
        Private Sub FitTitle()
            If Width < LogicalToDeviceUnits(200) Then Return
            Using bold As New Font(Font, FontStyle.Bold)
                Dim measured = TextRenderer.MeasureText(_title, bold, New Size(Math.Max(1, Width - LogicalToDeviceUnits(48)), Integer.MaxValue), TextFormatFlags.WordBreak)
                Dim desired = Math.Max(LogicalToDeviceUnits(44), measured.Height + LogicalToDeviceUnits(16))
                If Height <> desired Then Height = desired
            End Using
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            MyBase.OnMouseEnter(e)
            _hot = True
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hot = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If e.Button = MouseButtons.Left Then RaiseEvent Toggled(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        ''' <summary>Space/Enter toggle; Left/Right collapse/expand, mirrored in RTL.</summary>
        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Dim rtl As Boolean = (RightToLeft = RightToLeft.Yes)
            Select Case e.KeyCode
                Case Keys.Space, Keys.Enter
                    RaiseEvent Toggled(Me, EventArgs.Empty)
                    e.Handled = True
                Case Keys.Left
                    If (Not rtl AndAlso _expanded) OrElse (rtl AndAlso Not _expanded) Then RaiseEvent Toggled(Me, EventArgs.Empty)
                    e.Handled = True
                Case Keys.Right
                    If (Not rtl AndAlso Not _expanded) OrElse (rtl AndAlso _expanded) Then RaiseEvent Toggled(Me, EventArgs.Empty)
                    e.Handled = True
            End Select
        End Sub

        ''' <summary>Without this the control never sees Space/Enter/arrows - WinForms hands
        ''' them to the form's dialog navigation instead.</summary>
        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.Space, Keys.Enter, Keys.Left, Keys.Right
                    Return True
            End Select
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g As Graphics = e.Graphics
            Dim rtl As Boolean = (RightToLeft = RightToLeft.Yes)
            Dim w As Integer = ClientSize.Width
            Dim h As Integer = ClientSize.Height
            Dim palette As ThemePalette = ThemeService.Palette
            Dim band As Color = HeaderFill(palette)

            g.Clear(If(_hot, palette.ControlHover, band))
            ' The line that reads "header bar" against the card body under it.
            Using p As New Pen(If(SystemInformation.HighContrast, SystemColors.ControlDark, palette.Border))
                g.DrawLine(p, 0, h - 1, w, h - 1)
            End Using
            If Focused Then ControlPaint.DrawFocusRectangle(g, New Rectangle(1, 1, w - 2, h - 2))

            Dim ink As Color = ThemePalette.ReadableText(palette.TextPrimary, If(_hot, palette.ControlHover, band))
            Dim textFlags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding
            If rtl Then textFlags = textFlags Or TextFormatFlags.RightToLeft
            Using bold As New Font(Font, FontStyle.Bold)
                Dim cell As Rectangle
                Dim textRect As Rectangle
                Arrange(bold, cell, textRect)
                ' COLLAPSIBLE-GROUP 1.1 section 3: the filled state triangle at the reading start, in the
                ' caption's own ink - the viewer's settings groups draw the same shape (GroupMarker).
                GroupMarker.Fill(g, ink, GroupMarker.DirectionFor(_expanded, rtl), cell)
                TextRenderer.DrawText(g, _title, bold, textRect, ink, textFlags Or TextFormatFlags.WordBreak)
            End Using
        End Sub

        ''' <summary>The test seam of the interface factor (device DPI / 96) the marker is sized with; 0 = the
        ''' control's own DPI. A test cannot move a window to a 175 percent monitor, so it names the factor.</summary>
        Friend Sub SetMarkerScale(factor As Single)
            _markerScale = factor
            Invalidate()
        End Sub

        Private Function MarkerFactor() As Single
            If _markerScale > 0.0F Then Return _markerScale
            Return DeviceDpi / 96.0F
        End Function

        ''' <summary>The marker's square cell, in client coordinates: tier 16 scaled with the window, at the reading
        ''' start, centred on the FIRST caption line.</summary>
        Friend Function MarkerBounds() As Rectangle
            Using bold As New Font(Font, FontStyle.Bold)
                Dim cell As Rectangle
                Dim textRect As Rectangle
                Arrange(bold, cell, textRect)
                Return cell
            End Using
        End Function

        ''' <summary>Where the marker and the caption go: the marker's cell at the reading start (left; right under
        ''' RTL), then the 8 logical px gap, then the caption in the rest of the bar. The caption wraps inside its
        ''' rectangle and is never clipped; the cell is centred on the first line of it.</summary>
        Private Sub Arrange(bold As Font, ByRef cell As Rectangle, ByRef textRect As Rectangle)
            Dim rtl As Boolean = (RightToLeft = RightToLeft.Yes)
            Dim factor As Single = MarkerFactor()
            Dim pad As Integer = UiSizing.DevicePx(6, factor)
            Dim side As Integer = GroupMarker.CellPx(factor)
            Dim start As Integer = pad + side + GroupMarker.GapPx(factor)
            Dim w As Integer = ClientSize.Width
            Dim h As Integer = ClientSize.Height
            Dim textWidth As Integer = Math.Max(1, w - start - pad)
            textRect = If(rtl, New Rectangle(pad, 0, textWidth, h), New Rectangle(start, 0, textWidth, h))
            Dim block As Integer = TextRenderer.MeasureText(_title, bold, New Size(textWidth, Integer.MaxValue),
                                                             TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding).Height
            Dim centre As Integer = GroupMarker.FirstLineCentre(0, h, block, bold.Height)
            cell = New Rectangle(If(rtl, w - pad - side, pad), centre - side \ 2, side, side)
        End Sub

    End Class

End Class
