Option Strict On

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
        End Set
    End Property

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
            Return Color.FromArgb(176, 96, 0)
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

        Protected Overrides Sub OnFontChanged(e As EventArgs)
            MyBase.OnFontChanged(e)
            ' One line of text plus breathing room; follows the script font, which is taller
            ' for hi/bn than for Latin.
            Me.Height = Math.Max(24, Me.Font.Height + 10)
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

            g.Clear(If(_hot, Color.FromArgb(238, 243, 250),
                       If(Me.Parent IsNot Nothing, Me.Parent.BackColor, SystemColors.Control)))
            ' A hairline under the row: three stacked sections read as three rows rather
            ' than as one block of text when they are all folded.
            Using p As New Pen(Color.FromArgb(224, 228, 234))
                g.DrawLine(p, 0, h - 1, w, h - 1)
            End Using
            If Me.Focused Then ControlPaint.DrawFocusRectangle(g, New Rectangle(1, 1, w - 2, h - 2))

            Dim glyph As Integer = Math.Max(7, CInt(Me.Font.Height * 0.42F))
            Dim pad As Integer = 6
            Dim chevronX As Integer = If(rtl, w - pad - glyph, pad)
            DrawChevron(g, New Rectangle(chevronX, (h - glyph) \ 2, glyph, glyph), _expanded, rtl)

            Dim textFlags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPadding
            If rtl Then textFlags = textFlags Or TextFormatFlags.RightToLeft

            Dim left As Integer = pad + glyph + 8
            Dim right As Integer = w - pad
            Dim titleWidth As Integer
            Using bold As New Font(Me.Font, FontStyle.Bold)
                titleWidth = TextRenderer.MeasureText(g, _title, bold, New Size(Integer.MaxValue, h),
                                                      TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width
                Dim titleRect As Rectangle = If(rtl,
                    New Rectangle(pad, 0, w - left - pad, h),
                    New Rectangle(left, 0, right - left, h))
                TextRenderer.DrawText(g, _title, bold, titleRect, SystemColors.ControlText,
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
            TextRenderer.DrawText(g, _summary, Me.Font, sumRect, _summaryColor,
                                  textFlags Or TextFormatFlags.EndEllipsis Or If(rtl, TextFormatFlags.Left, TextFormatFlags.Right))
        End Sub

        ''' <summary>Solid triangle: down when open, pointing "into the row" when closed -
        ''' which is right in RTL and left in LTR.</summary>
        Private Shared Sub DrawChevron(g As Graphics, r As Rectangle, expanded As Boolean, rtl As Boolean)
            Dim pts As Point()
            If expanded Then
                pts = New Point() {New Point(r.Left, r.Top + r.Height \ 4),
                                   New Point(r.Right, r.Top + r.Height \ 4),
                                   New Point(r.Left + r.Width \ 2, r.Bottom)}
            ElseIf rtl Then
                pts = New Point() {New Point(r.Right, r.Top),
                                   New Point(r.Right, r.Bottom),
                                   New Point(r.Left, r.Top + r.Height \ 2)}
            Else
                pts = New Point() {New Point(r.Left, r.Top),
                                   New Point(r.Left, r.Bottom),
                                   New Point(r.Right, r.Top + r.Height \ 2)}
            End If
            Dim saved As SmoothingMode = g.SmoothingMode
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using b As New SolidBrush(Color.FromArgb(92, 104, 122))
                g.FillPolygon(b, pts)
            End Using
            g.SmoothingMode = saved
        End Sub

    End Class

End Class
