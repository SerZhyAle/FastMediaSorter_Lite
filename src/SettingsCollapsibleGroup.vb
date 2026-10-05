#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Independent, keyboard-operable presentation container; owns no preference values.</summary>
Friend NotInheritable Class SettingsCollapsibleGroup
    Inherits Panel

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
    Private summaryFontValue As Font
    Private summaryInkValue As Color
    Private ReadOnly renderDisclosure As Func(Of Boolean, Color, Bitmap)

    Friend Sub New(id As String, logicalScale As Single, headerFont As Font, Optional glyphFactory As Func(Of Boolean, Color, Bitmap) = Nothing)
        renderDisclosure = glyphFactory
        GroupId = id
        Name = "settingsGroup_" & id
        headerHeight = Math.Max(44, CInt(Math.Ceiling(44 * logicalScale)))
        headerTextInset = CInt(Math.Ceiling(8 * logicalScale))
        Margin = New Padding(0, 0, 0, CInt(Math.Ceiling(8 * logicalScale)))
        Header = New GroupHeader(Me) With {
            .Name = Name & "_header", .FlatStyle = FlatStyle.Flat,
            .UseMnemonic = False, .Font = headerFont, .TabStop = True,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoSize = False,
            .TextImageRelation = TextImageRelation.ImageBeforeText, .ImageAlign = ContentAlignment.MiddleLeft,
            .Padding = New Padding(CInt(Math.Ceiling(8 * logicalScale)), 0, 0, 0)}
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
        AddHandler Header.Paint, AddressOf PaintHeaderSummary
        AddHandler Disposed, Sub()
                                 If Header.Image IsNot Nothing Then Header.Image.Dispose()
                             End Sub
        AddHandler Body.Layout, Sub() ArrangeContents()
        AddHandler Resize, Sub() ArrangeContents()
        RefreshDisclosure()
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
            Dim hasPaintedSummary As Boolean = Not expandedValue AndAlso summaryValue.Length > 0 AndAlso summaryFontValue IsNot Nothing
            Header.TextAlign = If(hasPaintedSummary, ContentAlignment.TopLeft, ContentAlignment.MiddleLeft)
            Header.Padding = New Padding(headerTextInset, If(hasPaintedSummary, headerTextInset, 0), 0, 0)
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
        headerHeight = Math.Max(44, CInt(Math.Ceiling(44 * scale)))
        headerTextInset = CInt(Math.Ceiling(8 * scale))
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
        Dim imageWidth As Integer = If(headerButton.Image IsNot Nothing, headerButton.Image.Width + 2, 0)
        Dim textWidth As Integer = Math.Max(1, headerButton.ClientSize.Width - headerButton.Padding.Horizontal - imageWidth - 4)
        Dim captionHeight As Integer = TextRenderer.MeasureText(captionValue, headerButton.Font,
            New Size(textWidth, Integer.MaxValue), TextFormatFlags.WordBreak).Height
        Dim top As Integer = headerButton.Padding.Top + captionHeight + 2
        Dim bounds As New Rectangle(headerButton.Padding.Left + imageWidth, top,
                                    textWidth, Math.Max(1, headerButton.ClientSize.Height - top - headerTextInset))
        Dim flags As TextFormatFlags = TextFormatFlags.NoPrefix Or TextFormatFlags.WordBreak
        If headerButton.RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.RightToLeft
        TextRenderer.DrawText(e.Graphics, summaryValue, summaryFontValue, bounds, summaryInkValue, flags)
    End Sub

    Private Sub RefreshDisclosure()
        If renderDisclosure Is Nothing OrElse Header Is Nothing Then Return
        Dim oldImage = Header.Image
        Header.Image = renderDisclosure(expandedValue, Header.ForeColor)
        If oldImage IsNot Nothing Then oldImage.Dispose()
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
            expandedValue = value
            Body.Visible = value
            Caption = captionValue
            RefreshDisclosure()
            ArrangeContents()
            DirectCast(Header, GroupHeader).NotifyState()
            RaiseEvent ExpansionChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Friend Sub ArrangeContents()
        If arranging Then Return
        arranging = True
        Try
            Dim measuredHeight As Integer = TextRenderer.MeasureText(Header.Text, Header.Font,
                New Size(Math.Max(1, ClientSize.Width - Header.Padding.Horizontal - 16 - If(Header.Image Is Nothing, 0, Header.Image.Width)), Integer.MaxValue), TextFormatFlags.WordBreak).Height + 2 * headerTextInset
            ' The painted summary line grows the collapsed header the same way the old
            ' two-line Text did (127 §3.4).
            If Not expandedValue AndAlso summaryValue.Length > 0 AndAlso summaryFontValue IsNot Nothing Then
                measuredHeight += TextRenderer.MeasureText(summaryValue, summaryFontValue,
                    New Size(Math.Max(1, ClientSize.Width - Header.Padding.Horizontal - 16 - If(Header.Image Is Nothing, 0, Header.Image.Width)), Integer.MaxValue), TextFormatFlags.WordBreak).Height + 2
            End If
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
