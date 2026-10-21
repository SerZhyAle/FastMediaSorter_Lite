#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Independent, keyboard-operable presentation container; owns no preference values.
''' The header carries the COLLAPSIBLE-GROUP 1.1 section 3 state marker: a filled triangle at the
''' reading start (right when collapsed, down when open, left when collapsed under RTL), drawn as a
''' vector in the header's own ink - not an up/down action chevron, and not a baked bitmap, so a
''' theme, DPI or RightToLeft change repaints it with nothing to re-render.</summary>
Friend NotInheritable Class SettingsCollapsibleGroup
    Inherits Panel

    ''' <summary>Where the state triangle points.</summary>
    Friend Enum MarkerDirection
        Right
        Down
        Left
    End Enum

    ''' <summary>Marker glyph tier and the gap to the caption, logical pixels (COLLAPSIBLE-GROUP section 3 rules 2 and 6).
    ''' Both come from the shared token table (<c>UiSizing</c>: <c>MarkerTier</c>, <c>GlyphGap</c>); a Const cannot call
    ''' the resolver, so each takes the table's own constant, and a test pins the pair.</summary>
    Friend Const MarkerTierLogical As Integer = UiSizing.MarkerTier
    Friend Const MarkerGapLogical As Integer = UiSizing.GlyphGap

    Friend ReadOnly GroupId As String
    Friend ReadOnly Header As Button
    Friend ReadOnly Body As FlowLayoutPanel
    Friend Event ExpansionChanging As EventHandler
    Friend Event ExpansionChanged As EventHandler
    Private expandedValue As Boolean = True
    Private captionValue As String = String.Empty
    Private summaryValue As String = String.Empty
    Private arranging As Boolean
    Private headerHeight As Integer
    Private headerTextInset As Integer
    Private markerSize As Integer
    Private markerGap As Integer
    Private summaryFontValue As Font
    Private summaryInkValue As Color

    Friend Sub New(id As String, logicalScale As Single, headerFont As Font)
        GroupId = id
        Name = "settingsGroup_" & id
        headerHeight = Math.Max(32, CInt(Math.Ceiling(32 * logicalScale)))
        headerTextInset = CInt(Math.Ceiling(6 * logicalScale))
        markerSize = Math.Max(MarkerTierLogical, CInt(Math.Round(MarkerTierLogical * logicalScale)))
        markerGap = Math.Max(MarkerGapLogical, CInt(Math.Ceiling(MarkerGapLogical * logicalScale)))
        Margin = New Padding(0, 0, 0, CInt(Math.Ceiling(6 * logicalScale)))
        Header = New GroupHeader(Me) With {
            .Name = Name & "_header", .FlatStyle = FlatStyle.Flat,
            .UseMnemonic = False, .Font = headerFont, .TabStop = True,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoSize = False}
        ApplyHeaderInsets()
        ' The header is a band, not a bordered row (127 §3.4): the blended fill and the
        ' larger Semibold caption set it apart, colour loss only strengthens the band.
        Header.FlatAppearance.BorderSize = 0
        Body = New FlowLayoutPanel With {
            .Name = Name & "_body", .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .AutoScroll = False, .TabStop = False,
            .Margin = Padding.Empty, .Padding = Padding.Empty}
        Controls.Add(Body)
        Controls.Add(Header)
        AddHandler Header.Click, Sub() Expanded = Not Expanded
        AddHandler Header.ForeColorChanged, Sub() RefreshDisclosure()
        ' The marker sits at the reading start: a RightToLeft change (ambient from the window
        ' included) moves the caption's inset to the other side and repaints the triangle.
        AddHandler Header.RightToLeftChanged, Sub() Caption = captionValue
        AddHandler Header.Paint, AddressOf PaintMarker
        AddHandler Header.Paint, AddressOf PaintHeaderSummary
        AddHandler Body.Layout, Sub() ArrangeContents()
        AddHandler Resize, Sub() ArrangeContents()
        RefreshDisclosure()
    End Sub

    ''' <summary>The direction of the state triangle: down when open; right when collapsed, left when
    ''' collapsed under RTL (it points toward the reading end). Forwards to the shared <see cref="GroupMarker"/>.</summary>
    Friend Shared Function DirectionFor(expanded As Boolean, rightToLeft As Boolean) As MarkerDirection
        Return CType(GroupMarker.DirectionFor(expanded, rightToLeft), MarkerDirection)
    End Function

    ''' <summary>The three vertices of the triangle inside a square <paramref name="cell"/>, on the 16-unit grid of the
    ''' shared <see cref="GroupMarker"/> (the same shape the Share Manager's group headers draw).</summary>
    Friend Shared Function MarkerPolygon(direction As MarkerDirection, cell As Rectangle) As Point()
        Return GroupMarker.Polygon(CType(direction, GroupMarker.Direction), cell)
    End Function

    ''' <summary>The marker's square cell in the header's client coordinates: tier 16 scaled, at the
    ''' reading start (left, or right under RTL), centred on the first caption line.</summary>
    Friend Function MarkerBounds() As Rectangle
        Dim headerButton As Button = Header
        Dim rtl As Boolean = headerButton.RightToLeft = RightToLeft.Yes
        Dim x As Integer = If(rtl, headerButton.ClientSize.Width - headerTextInset - markerSize, headerTextInset)
        ' The caption is MiddleLeft inside the zone the Padding leaves (all of the header, or - with a painted
        ' summary - the part above the summary line), so its first line starts at a computable place: the
        ' button centres its text in that zone, and the marker is centred on that first line.
        Dim lineHeight As Integer = headerButton.Font.Height
        Dim block As Integer = Math.Max(lineHeight, TextRenderer.MeasureText(headerButton.Text, headerButton.Font,
            New Size(CaptionWrapWidth(), Integer.MaxValue), TextFormatFlags.WordBreak).Height)
        Dim zoneHeight As Integer = Math.Max(1, headerButton.ClientSize.Height - headerButton.Padding.Vertical)
        Dim firstLineTop As Integer = headerButton.Padding.Top + (zoneHeight - block) \ 2
        Dim centre As Integer = (2 * firstLineTop + lineHeight + 1) \ 2
        Return New Rectangle(x, centre - markerSize \ 2, markerSize, markerSize)
    End Function

    ''' <summary>The width the caption is wrapped at when the header is sized (the same fudge everywhere); the
    ''' header always takes the group's full width.</summary>
    Private Function CaptionWrapWidth() As Integer
        Return Math.Max(1, ClientSize.Width - Header.Padding.Horizontal - 16)
    End Function

    Private Function PaintsSummary() As Boolean
        Return Not expandedValue AndAlso summaryValue.Length > 0 AndAlso summaryFontValue IsNot Nothing
    End Function

    ''' <summary>Height the painted summary line takes in its zone: the text plus the 2 above it.</summary>
    Private Function SummaryBlockHeight() As Integer
        If Not PaintsSummary() Then Return 0
        Return TextRenderer.MeasureText(summaryValue, summaryFontValue,
            New Size(CaptionWrapWidth(), Integer.MaxValue), TextFormatFlags.WordBreak).Height + 2
    End Function

    ''' <summary>Insets of the caption: the marker cell and its gap on the reading-start side, nothing on the other
    ''' (Padding is physical, so RTL puts the inset on the right); with a painted summary also the top inset and,
    ''' below the caption, the summary line and the bottom inset - the caption stays MiddleLeft in what is left.</summary>
    Private Sub ApplyHeaderInsets()
        Dim startInset As Integer = headerTextInset + markerSize + markerGap
        Dim top As Integer = 0
        Dim bottom As Integer = 0
        If PaintsSummary() Then
            top = headerTextInset
            bottom = headerTextInset + SummaryBlockHeight()
        End If
        Dim wanted As Padding = If(Header.RightToLeft = RightToLeft.Yes,
                                   New Padding(0, top, startInset, bottom), New Padding(startInset, top, 0, bottom))
        If Header.Padding <> wanted Then Header.Padding = wanted
    End Sub

    ''' <summary>Fills the state triangle (shared <see cref="GroupMarker"/>) in the header's current ink (theme, high contrast
    ''' and hover follow it).</summary>
    Private Sub PaintMarker(sender As Object, e As PaintEventArgs)
        Dim cell As Rectangle = MarkerBounds()
        GroupMarker.Fill(e.Graphics, Header.ForeColor,
                         GroupMarker.DirectionFor(expandedValue, Header.RightToLeft = RightToLeft.Yes), cell)
    End Sub

    Friend Sub SetSummaryStyle(font As Font, ink As Color)
        summaryFontValue = font
        summaryInkValue = ink
        Caption = captionValue
        ArrangeContents()
    End Sub

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Caption As String
        Get
            Return captionValue
        End Get
        Set(value As String)
            captionValue = value
            Header.AccessibleName = value
            ' The collapsed summary is painted under the caption in muted small text
            ' (127 §3.4). Until a summary style has arrived the old two-line Text
            ' behaviour still applies.
            Header.Text = value & If(Not expandedValue AndAlso summaryValue.Length > 0 AndAlso summaryFontValue Is Nothing,
                                      Environment.NewLine & summaryValue, String.Empty)
            ApplyHeaderInsets()
            Header.Invalidate()
        End Set
    End Property

    Friend Sub SetSummary(value As String)
        summaryValue = value
        Header.AccessibleDescription = value
        Caption = captionValue
        ArrangeContents()
    End Sub

    Friend Sub SetLayoutScale(scale As Single)
        headerHeight = Math.Max(32, CInt(Math.Ceiling(32 * scale)))
        headerTextInset = CInt(Math.Ceiling(6 * scale))
        markerSize = Math.Max(MarkerTierLogical, CInt(Math.Round(MarkerTierLogical * scale)))
        markerGap = Math.Max(MarkerGapLogical, CInt(Math.Ceiling(MarkerGapLogical * scale)))
        Caption = captionValue
        RefreshDisclosure()
        ArrangeContents()
    End Sub

    ''' <summary>Draws the collapsed summary line in its muted small font, under the
    ''' caption the button itself renders (127 §3.4). Expanded headers have no summary.</summary>
    Private Sub PaintHeaderSummary(sender As Object, e As PaintEventArgs)
        If summaryFontValue Is Nothing OrElse expandedValue OrElse summaryValue.Length = 0 OrElse captionValue.Length = 0 Then Return
        ' Not "header": a local of that name shadows the Header field - VB is
        ' case-insensitive, so the initializer would read the local itself.
        Dim headerButton As Button = Header
        Dim rtl As Boolean = headerButton.RightToLeft = RightToLeft.Yes
        ' The summary line owns the bottom band of the header (Padding.Bottom reserves it); the marker's
        ' inset is part of Padding on the reading-start side. The 6 is the button's own text margin (measured:
        ' the caption's ink starts that far inside the padding), so the summary starts where the caption above
        ' it does - and ends where it ends under RTL.
        Dim margin As Integer = 6
        Dim textWidth As Integer = Math.Max(1, headerButton.ClientSize.Width - headerButton.Padding.Horizontal - 2 * margin)
        Dim summaryHeight As Integer = SummaryBlockHeight() - 2
        Dim top As Integer = headerButton.ClientSize.Height - headerTextInset - summaryHeight
        Dim bounds As New Rectangle(headerButton.Padding.Left + margin, top, textWidth, Math.Max(1, summaryHeight))
        Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding
        If rtl Then flags = flags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
        TextRenderer.DrawText(e.Graphics, summaryValue, summaryFontValue, bounds, summaryInkValue, flags)
    End Sub

    ''' <summary>The marker is painted, not baked: a new state, ink or scale only needs a repaint.</summary>
    Private Sub RefreshDisclosure()
        If Header Is Nothing Then Return
        Header.Invalidate()
    End Sub

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Expanded As Boolean
        Get
            Return expandedValue
        End Get
        Set(value As Boolean)
            If expandedValue = value Then Return
            RaiseEvent ExpansionChanging(Me, EventArgs.Empty)
            If Not value AndAlso Body.ContainsFocus Then Header.Focus()
            ' One layout pass (WINDOWS-UI section 5, amended 0.2): the group and the page it stands in are held
            ' while the group changes state - body visibility, caption, disclosure picture, header and body
            ' bounds, its own height - and laid out once when the change is complete. Each of those used to
            ' re-measure the page as it happened. ExpansionChanged is raised only after that pass, so a listener
            ' that measures the group or restores a scroll position reads the finished layout, never a
            ' half-built one (the same section: a remembered position is restored after layout).
            Dim page As Control = Parent
            If page IsNot Nothing Then page.SuspendLayout()
            SuspendLayout()
            Try
                expandedValue = value
                Body.Visible = value
                Caption = captionValue
                RefreshDisclosure()
                ArrangeContents()
                DirectCast(Header, GroupHeader).NotifyState()
            Finally
                ResumeLayout(True)
                If page IsNot Nothing Then page.ResumeLayout(True)
            End Try
            RaiseEvent ExpansionChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Friend Sub ArrangeContents()
        If arranging Then Return
        arranging = True
        Try
            ' The summary line's band (Padding.Bottom) depends on the width it is wrapped at.
            ApplyHeaderInsets()
            Dim measuredHeight As Integer = TextRenderer.MeasureText(Header.Text, Header.Font,
                New Size(CaptionWrapWidth(), Integer.MaxValue), TextFormatFlags.WordBreak).Height + 2 * headerTextInset
            ' The painted summary line grows the collapsed header the same way the old
            ' two-line Text did (127 §3.4).
            measuredHeight += SummaryBlockHeight()
            Dim currentHeaderHeight As Integer = Math.Max(headerHeight, measuredHeight)
            Header.SetBounds(0, 0, ClientSize.Width, currentHeaderHeight)
            Dim contentHeight As Integer = 0
            For Each child As Control In Body.Controls
                child.Width = Math.Max(1, ClientSize.Width - child.Margin.Horizontal)
                ' Visible depends on the selected TabPage too; layout must include hidden pages.
                contentHeight += child.Height + child.Margin.Vertical
            Next
            Body.SetBounds(0, currentHeaderHeight, ClientSize.Width, Math.Max(1, contentHeight))
            Dim targetHeight As Integer = currentHeaderHeight + If(expandedValue, contentHeight, 0)
            If Height <> targetHeight Then Height = targetHeight
        Finally
            arranging = False
        End Try
    End Sub

    Private NotInheritable Class GroupHeader
        Inherits Button
        Private ReadOnly group As SettingsCollapsibleGroup
        Friend Sub New(owner As SettingsCollapsibleGroup)
            group = owner
        End Sub
        Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
            Return New HeaderAccessibility(Me, group)
        End Function
        Friend Sub NotifyState()
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1)
        End Sub
    End Class

    Private NotInheritable Class HeaderAccessibility
        Inherits Control.ControlAccessibleObject
        Private ReadOnly group As SettingsCollapsibleGroup
        Friend Sub New(owner As Button, container As SettingsCollapsibleGroup)
            MyBase.New(owner)
            group = container
        End Sub
        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.PushButton
            End Get
        End Property
        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Return MyBase.State Or If(group.Expanded, AccessibleStates.Expanded, AccessibleStates.Collapsed)
            End Get
        End Property
        Public Overrides Sub DoDefaultAction()
            group.Expanded = Not group.Expanded
        End Sub
    End Class
End Class
#End If
