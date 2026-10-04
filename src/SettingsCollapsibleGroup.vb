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
    Private arranging As Boolean
    Private ReadOnly headerHeight As Integer

    Friend Sub New(id As String, logicalScale As Single, headerFont As Font)
        GroupId = id
        Name = "settingsGroup_" & id
        headerHeight = Math.Max(28, CInt(Math.Ceiling(36 * logicalScale)))
        Margin = New Padding(0, 0, 0, CInt(Math.Ceiling(8 * logicalScale)))
        Header = New GroupHeader(Me) With {
            .Name = Name & "_header", .FlatStyle = FlatStyle.Flat,
            .UseMnemonic = False, .Font = headerFont, .TabStop = True,
            .TextAlign = ContentAlignment.MiddleLeft, .AutoSize = False,
            .Padding = New Padding(CInt(Math.Ceiling(8 * logicalScale)), 0, 0, 0)}
        Header.FlatAppearance.BorderSize = 1
        Body = New FlowLayoutPanel With {
            .Name = Name & "_body", .FlowDirection = FlowDirection.TopDown,
            .WrapContents = False, .AutoScroll = False, .TabStop = False,
            .Margin = Padding.Empty, .Padding = Padding.Empty}
        Controls.Add(Body)
        Controls.Add(Header)
        AddHandler Header.Click, Sub() Expanded = Not Expanded
        AddHandler Body.Layout, Sub() ArrangeContents()
        AddHandler Resize, Sub() ArrangeContents()
    End Sub

    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property Caption As String
        Get
            Return captionValue
        End Get
        Set(value As String)
            captionValue = value
            Header.AccessibleName = value
            Header.Text = If(expandedValue, "−  ", "+  ") & value
        End Set
    End Property

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
            ArrangeContents()
            DirectCast(Header, GroupHeader).NotifyState()
            RaiseEvent ExpansionChanged(Me, EventArgs.Empty)
        End Set
    End Property

    Friend Sub ArrangeContents()
        If arranging Then Return
        arranging = True
        Try
            Header.SetBounds(0, 0, ClientSize.Width, headerHeight)
            Dim contentHeight As Integer = 0
            For Each child As Control In Body.Controls
                child.Width = Math.Max(1, ClientSize.Width - child.Margin.Horizontal)
                ' Visible depends on the selected TabPage too; layout must include hidden pages.
                contentHeight += child.Height + child.Margin.Vertical
            Next
            Body.SetBounds(0, headerHeight, ClientSize.Width, Math.Max(1, contentHeight))
            Dim targetHeight As Integer = headerHeight + If(expandedValue, contentHeight, 0)
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
