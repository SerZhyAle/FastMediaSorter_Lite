Option Strict On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' One collapsible block of the Share Manager main window
''' (SPECIFICATION_SHARE_MANAGER_COMPACT_WINDOW.md §3.2): a full-width header row that
''' toggles a body panel under it.
'''
''' The rule that shapes the whole control is "collapsed is not hidden" (§2 rule 2): the
''' header carries a LIVE one-line <see cref="Summary"/>, refreshed by the same code path
''' that fills the body, so a folded section still answers the question it exists for -
''' the address and login, the internet verdict, the two counters. A fold that stopped
''' updating would turn twelve blocks of clutter into twelve blocks of stale clutter.
'''
''' Two mechanics are deliberate and must not be traded away:
''' - The header is DRAWN, not composed of child labels. The chevron then scales with the
'''   font at any display scaling with no bitmap to rebuild on OnDpiChanged, the title is
'''   never ellipsized while the summary always is (§7.8 measures exactly that), and the
'''   RTL mirror is a RightToLeft property read rather than a RightToLeftLayout flag - the
'''   flag mirrors the whole device context and was rejected repo-wide.
''' - Toggling NEVER resizes the window from in here. Who gives up the height is the
'''   window's decision (§3.2 G4: the folder list down to a three-row floor, then the
'''   window grows downwards, then the outer column scrolls); a control that resized its
'''   own form would move the buttons out from under the user's cursor.
''' </summary>
Public NotInheritable Class CollapsibleSection
    Inherits Panel

    Private ReadOnly _key As String
    Private ReadOnly _header As SectionHeader
    Private ReadOnly _body As TableLayoutPanel
    Private _expanded As Boolean
    ''' <summary>FlagAttention has already opened this section once in this window
    ''' session. Rule 3 of §2: a problem opens its own section ONCE - re-opening what the
    ''' user has just folded by hand would make the fold useless.</summary>
    Private _attentionShown As Boolean

    Public Event ExpandedChanged As EventHandler

    Public Sub New(key As String, title As String)
        _key = If(key, "")
        Me.AutoSize = True
        Me.AutoSizeMode = AutoSizeMode.GrowAndShrink
        Me.Margin = New Padding(0, 2, 0, 2)

        _body = New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 1,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Margin = New Padding(0), .Padding = New Padding(22, 2, 4, 8), .Visible = False}
        _body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _header = New SectionHeader(title) With {.Dock = DockStyle.Top}
        AddHandler _header.Toggled, AddressOf OnHeaderToggled

        ' Body first, header second: WinForms docks from the END of the collection, so the
        ' header (added last) takes the top edge and the body lands under it.
        Me.Controls.Add(_body)
        Me.Controls.Add(_header)
    End Sub

    Friend Sub ConfigureSettingsHeader()
        _header.WrapSettingsTitle = True
        _header.MinimumSize = New Size(0, 44)
        _header.Height = 44
        _body.Padding = New Padding(12, 4, 12, 12)
        Dock = DockStyle.Top
    End Sub

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        If _header Is Nothing OrElse Not _header.WrapSettingsTitle Then Return MyBase.GetPreferredSize(proposedSize)
        ' The body is already auto-sized inside the held subtree. Parent tables need its resulting
        ' height, not another recursive measurement of every editor for each preferred-size probe.
        Return New Size(0, Padding.Vertical + _header.Height + If(_expanded, _body.Height, 0))
    End Function

    ' Reuse the existing editors and state paths as a settings page, without a disclosure header.
    Friend Sub PresentAsPage()
        SuspendLayout()
        _header.Visible = False
        _header.TabStop = False
        _body.Padding = New Padding(0, 2, 0, 8)
        Expanded = True
        ResumeLayout(True)
    End Sub

    ''' <summary>The token stored in Share_ExpandedSections.</summary>
    Public ReadOnly Property Key As String
        Get
            Return _key
        End Get
    End Property

    ''' <summary>Never ellipsized - a clipped section title is the failure §7.8 guards.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Title As String
        Get
            Return _header.Title
        End Get
        Set(value As String)
            _header.Title = value
        End Set
    End Property

    ''' <summary>The live one-liner shown next to the title; ellipsized when it does not fit.</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Summary As String
        Get
            Return _header.Summary
        End Get
        Set(value As String)
            _header.Summary = value
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property SummaryColor As Color
        Get
            Return _header.SummaryColor
        End Get
        Set(value As Color)
            _header.SummaryColor = value
        End Set
    End Property

    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Expanded As Boolean
        Get
            Return _expanded
        End Get
        Set(value As Boolean)
            If _expanded = value Then Return
            Dim held As New List(Of Control)()
            Dim children As New List(Of Control)()
            If _header.WrapSettingsTitle Then
                HoldChildren(_body, children)
                Dim ancestor As Control = Me
                While ancestor IsNot Nothing AndAlso Not TypeOf ancestor Is Form
                    ancestor.SuspendLayout()
                    held.Add(ancestor)
                    ' The fixed viewport owns scrolling. Its outer dock/rail geometry does not change.
                    Dim viewport = TryCast(ancestor, ScrollableControl)
                    If viewport IsNot Nothing AndAlso viewport.AutoScroll Then Exit While
                    ancestor = ancestor.Parent
                End While
            End If
            Try
                If Not value AndAlso _body.ContainsFocus Then _header.Focus()
                _expanded = value
                _header.Expanded = value
                _body.Visible = value
                ' Not optional. AutoSize caches the preferred size, and HIDING a docked child
                ' does not clear that cache: the section then reports the height of the body it
                ' has just folded away - measured, 398 px for a 26 px header - which is the one
                ' thing a collapsible control must not do. Growing works without this because
                ' showing a child invalidates the cache on its own; only the shrink is affected,
                ' so the defect shows up as "collapsing gives nothing back".
                Me.PerformLayout()
                RaiseEvent ExpandedChanged(Me, EventArgs.Empty)
            Finally
                For i = children.Count - 1 To 0 Step -1
                    children(i).ResumeLayout(value AndAlso i = 0)
                Next
                For i = 0 To held.Count - 1
                    held(i).ResumeLayout(True)
                Next
            End Try
        End Set
    End Property

    Private Shared Sub HoldChildren(parent As Control, held As List(Of Control))
        parent.SuspendLayout()
        held.Add(parent)
        For Each child As Control In parent.Controls
            HoldChildren(child, held)
        Next
    End Sub

    ''' <summary>Host for this section's controls - a single-column auto-height panel;
    ''' append rows with <see cref="AddBodyRow"/>.</summary>
    Public ReadOnly Property Body As Panel
        Get
            Return _body
        End Get
    End Property

    ''' <summary>Appends a control on a new auto-height row of the body.</summary>
    Public Sub AddBodyRow(c As Control)
        If c Is Nothing Then Return
        Dim button = TryCast(c, Button)
        If button IsNot Nothing Then
            ' WINDOWS-UI section 11 item 3: adjacent commands share a wrapping row.
            ' A note or editor ends the row, keeping each explanation beside its action.
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

    ''' <summary>
    ''' A state that needs the user (the external check ran and failed) opens this section
    ''' and tints its summary - once per window session. Returns True when it actually
    ''' expanded, so the caller can re-run its layout.
    ''' </summary>
    Public Function FlagAttention(reason As String) As Boolean
        If reason IsNot Nothing AndAlso reason.Length > 0 Then Summary = reason
        SummaryColor = AttentionColor
        If _attentionShown OrElse _expanded Then
            _attentionShown = True
            Return False
        End If
        _attentionShown = True
        Expanded = True
        Return True
    End Function

    ''' <summary>The amber the whole app uses for "you set something up and it does not
    ''' answer" - same value as MainWindow's access-state amber.</summary>
    Public Shared ReadOnly Property AttentionColor As Color
        Get
            Return ThemeService.Palette.Warning
        End Get
    End Property

    Private Sub OnHeaderToggled(sender As Object, e As EventArgs)
        Expanded = Not Expanded
    End Sub

    ' ------------------------------------------------------------------------------
    ''' <summary>
    ''' The clickable header row: chevron, bold title, right-aligned summary. Owner-drawn
    ''' for the reasons in the class remark; keyboard- and screen-reader-reachable because
    ''' a fold that only a mouse can open is a fold that hides things.
    ''' </summary>
    Private NotInheritable Class SectionHeader
        Inherits Control

        Private _title As String
        Private _summary As String = ""
        Private _summaryColor As Color = SystemColors.GrayText
        Private _expanded As Boolean
        Private _hot As Boolean
        Private _markerScale As Single
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Friend Property WrapSettingsTitle As Boolean

        Public Event Toggled As EventHandler

        Public Sub New(title As String)
            _title = If(title, "")
            Me.TabStop = True
            Me.Margin = New Padding(0)
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or
                        ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            ' AccessibleRole is what turns an owner-drawn panel into something a screen
            ' reader announces as an expandable control rather than skipping silently.
            Me.AccessibleRole = AccessibleRole.ButtonDropDown
            Me.Height = 26
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
                FitHeaderTitle()
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Summary As String
            Get
                Return _summary
            End Get
            Set(value As String)
                Dim v As String = If(value, "")
                If String.Equals(v, _summary, StringComparison.Ordinal) Then Return
                _summary = v
                UpdateAccessibleName()
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SummaryColor As Color
            Get
                Return _summaryColor
            End Get
            Set(value As Color)
                If _summaryColor = value Then Return
                _summaryColor = value
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

        ''' <summary>Title + summary, so a screen reader hears everything the folded row
        ''' shows a sighted user - the point of rule 2 restated for assistive tech.</summary>
        Private Sub UpdateAccessibleName()
            Me.AccessibleName = If(_summary.Length > 0, _title & " - " & _summary, _title)
            Me.AccessibleDescription = If(_expanded, "expanded", "collapsed")
        End Sub

        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New HeaderAccessibility(Me)
        End Function

        Private NotInheritable Class HeaderAccessibility
            Inherits ControlAccessibleObject

            Private ReadOnly header As SectionHeader

            Public Sub New(owner As SectionHeader)
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
            ' One line of text plus breathing room; follows the script font, which is taller
            ' for hi/bn than for Latin.
            ' ...and never shorter than the state marker's cell with a little air (tier 16 scaled with the window).
            Me.Height = Math.Max(Math.Max(24, Me.Font.Height + 10), GroupMarker.CellPx(MarkerFactor()) + 4)
            FitHeaderTitle()
        End Sub

        Protected Overrides Sub OnSizeChanged(e As EventArgs)
            MyBase.OnSizeChanged(e)
            FitHeaderTitle()
        End Sub

        Private Sub FitHeaderTitle()
            If Not WrapSettingsTitle OrElse Width < LogicalToDeviceUnits(200) Then Return
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
            Me.Focus()
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

        ''' <summary>Space/Enter toggle; Left/Right collapse/expand (mirrored in RTL, where
        ''' the arrow that points "outward" is the other one).</summary>
        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Dim rtl As Boolean = (Me.RightToLeft = RightToLeft.Yes)
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
            Dim rtl As Boolean = (Me.RightToLeft = RightToLeft.Yes)
            Dim w As Integer = Me.ClientSize.Width
            Dim h As Integer = Me.ClientSize.Height

            ' Spec 060: the palette in dark mode. The two light tints predate the palette and are
            ' kept, so the light header is the one it always was.
            Dim theme As ThemePalette = ThemeService.Palette
            Dim hotBack As Color = theme.ControlHover
            Dim hairline As Color = theme.Border
            g.Clear(If(_hot, hotBack,
                       If(Me.Parent IsNot Nothing, Me.Parent.BackColor, theme.SurfaceWindow)))
            ' A hairline under the row: three stacked sections read as three rows rather
            ' than as one block of text when they are all folded.
            Using p As New Pen(hairline)
                g.DrawLine(p, 0, h - 1, w, h - 1)
            End Using
            If Me.Focused Then ControlPaint.DrawFocusRectangle(g, New Rectangle(1, 1, w - 2, h - 2))

            Dim textFlags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding
            If WrapSettingsTitle Then textFlags = (textFlags And Not TextFormatFlags.SingleLine) Or TextFormatFlags.WordBreak
            If rtl Then textFlags = textFlags Or TextFormatFlags.RightToLeft

            Dim pad As Integer = HeaderPad()
            Dim left As Integer = pad + GroupMarker.CellPx(MarkerFactor()) + GroupMarker.GapPx(MarkerFactor())
            Dim right As Integer = w - pad
            ' The header's ink: the caption and the state marker are drawn in it.
            Dim ink As Color = theme.TextPrimary
            Dim titleWidth As Integer
            Using bold As New Font(Me.Font, FontStyle.Bold)
                titleWidth = TextRenderer.MeasureText(g, _title, bold, New Size(Integer.MaxValue, h),
                                                      TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
                Dim titleRect As Rectangle = If(rtl,
                    New Rectangle(pad, 0, w - left - pad, h),
                    New Rectangle(left, 0, right - left, h))
                ' COLLAPSIBLE-GROUP 1.1 section 3: the filled state triangle at the reading start, in the
                ' caption's own ink - the viewer's settings groups draw the same shape (GroupMarker).
                GroupMarker.Fill(g, ink, GroupMarker.DirectionFor(_expanded, rtl), MarkerCell(bold, titleRect))
                TextRenderer.DrawText(g, _title, bold, titleRect, ink,
                                      textFlags Or If(rtl, TextFormatFlags.Right, TextFormatFlags.Left))
            End Using

            If _summary.Length = 0 Then Return
            ' The summary takes whatever the title left and ellipsizes - the title never
            ' does, so an over-long translation costs the summary, not the section's name.
            Dim gap As Integer = 14
            Dim available As Integer = (right - left) - titleWidth - gap
            If available < 24 Then Return
            Dim sumRect As Rectangle = If(rtl,
                New Rectangle(pad, 0, available, h),
                New Rectangle(right - available, 0, available, h))
            Dim surface As Color = If(_hot, hotBack, If(Parent IsNot Nothing, Parent.BackColor, theme.SurfaceWindow))
            Dim summaryInk As Color = ThemePalette.ReadableText(theme.Restate(_summaryColor, False), surface)
            TextRenderer.DrawText(g, _summary, Me.Font, sumRect, summaryInk,
                                  textFlags Or TextFormatFlags.EndEllipsis Or If(rtl, TextFormatFlags.Left, TextFormatFlags.Right))
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

        ''' <summary>The inset of the header's content from either edge.</summary>
        Private Function HeaderPad() As Integer
            Return UiSizing.DevicePx(6, MarkerFactor())
        End Function

        ''' <summary>The marker's square cell, in client coordinates: tier 16 scaled with the window, at the reading
        ''' start, centred on the FIRST caption line (a caption that wraps keeps its marker beside the first line).</summary>
        Private Function MarkerCell(bold As Font, titleRect As Rectangle) As Rectangle
            Dim rtl As Boolean = (Me.RightToLeft = RightToLeft.Yes)
            Dim side As Integer = GroupMarker.CellPx(MarkerFactor())
            Dim block As Integer = bold.Height
            If WrapSettingsTitle Then
                block = TextRenderer.MeasureText(_title, bold, New Size(Math.Max(1, titleRect.Width), Integer.MaxValue),
                                                 TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding).Height
            End If
            Dim centre As Integer = GroupMarker.FirstLineCentre(0, Me.ClientSize.Height, block, bold.Height)
            Return New Rectangle(If(rtl, Me.ClientSize.Width - HeaderPad() - side, HeaderPad()), centre - side \ 2, side, side)
        End Function

        ''' <summary>The marker's square cell for the current state, in client coordinates.</summary>
        Friend Function MarkerBounds() As Rectangle
            Using bold As New Font(Me.Font, FontStyle.Bold)
                Dim pad As Integer = HeaderPad()
                Dim start As Integer = pad + GroupMarker.CellPx(MarkerFactor()) + GroupMarker.GapPx(MarkerFactor())
                Dim textWidth As Integer = Math.Max(1, Me.ClientSize.Width - start - pad)
                Return MarkerCell(bold, New Rectangle(start, 0, textWidth, Me.ClientSize.Height))
            End Using
        End Function

    End Class

End Class
