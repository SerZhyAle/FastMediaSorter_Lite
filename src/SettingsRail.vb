#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The page rail of the viewer's settings window (SETTINGS-NAVIGATION 2.0 section 2, ticket 243 slice T-7a):
''' a LIST of <see cref="SettingsRailRow"/> that assistive technology reads as a list with one selected item,
''' and that the keyboard crosses as ONE tab stop (rule 7).
'''
''' What it owns: the accessible role (<see cref="AccessibleRole.List"/>), the selected row, the roving tab stop
''' and the movement of the focus ring. What it does not own: the pages. Choosing a page stays the window's own
''' path - a row's <c>Click</c> (mouse, Enter or Space) - so an arrow key moves only the focus and never runs the
''' work of a page switch. The window sets <see cref="SelectedIndex"/> from the page that is shown.
'''
''' The tab stop ROVES: at rest it is the selected row alone; while the focus is inside, it is the row that has the
''' focus, so Tab and Shift+Tab leave the rail from wherever the arrow keys took the focus instead of walking back
''' to the selected row first. When the focus leaves, the tab stop returns to the selected row.
'''
''' Pure of the window: it knows no page, no palette and no <c>Table_Form</c>, so the same object is built by the
''' window and by <c>SettingsRailTests</c>.
''' </summary>
Friend NotInheritable Class SettingsRailList
    Inherits Panel

    ' The rail's own horizontal rhythm, in logical px (the window scales them with its Sp; a test with
    ' UiSizing.DevicePx). The sizes of the glyph, the gap and the row are SettingsRailRules', never these.
    ''' <summary>Room a row leaves at the rail's reading START.</summary>
    Friend Const LeadingInsetLogical As Integer = 12
    ''' <summary>Room a row leaves at the rail's reading END, towards the content.</summary>
    Friend Const TrailingInsetLogical As Integer = 4
    ''' <summary>Vertical gap between two rows.</summary>
    Friend Const RowGapLogical As Integer = 4
    ''' <summary>A row's own padding at its reading start.</summary>
    Friend Const RowPaddingStartLogical As Integer = 10
    ''' <summary>A row's own padding at its reading end.</summary>
    Friend Const RowPaddingEndLogical As Integer = 6
    ''' <summary>A little room the text renderer needs beyond the measured caption.</summary>
    Friend Const RenderSlackLogical As Integer = 2

    Private ReadOnly railRows As New List(Of SettingsRailRow)()
    Private selectedValue As Integer = -1

    Friend Sub New()
        MyBase.New()
        AccessibleRole = AccessibleRole.List
        AutoScroll = True
        Padding = System.Windows.Forms.Padding.Empty
    End Sub

    ''' <summary>Every horizontal pixel of the rail that is not caption, glyph or gap: the side panel's own padding
    ''' (<paramref name="sidePaddingPx"/>, both edges), the two insets, a row's own padding and the renderer's slack.
    ''' The padding input of <see cref="SettingsRailRules.RailWidth"/>.</summary>
    Friend Shared Function ChromePx(sidePaddingPx As Integer, factor As Single) As Integer
        Return Math.Max(0, sidePaddingPx) + UiSizing.DevicePx(LeadingInsetLogical, factor) + UiSizing.DevicePx(TrailingInsetLogical, factor) +
            UiSizing.DevicePx(RowPaddingStartLogical, factor) + UiSizing.DevicePx(RowPaddingEndLogical, factor) +
            UiSizing.DevicePx(RenderSlackLogical, factor)
    End Function

    ''' <summary>The single-line width of the longest caption among the rows, in each row's own font; 0 for an empty
    ''' rail or empty captions. The caption input of <see cref="SettingsRailRules.RailWidth"/>.</summary>
    Friend Function LongestCaptionPx() As Integer
        Dim longest As Integer = 0
        For Each row As SettingsRailRow In railRows
            If String.IsNullOrEmpty(row.Text) Then Continue For
            Dim measured As Integer = TextRenderer.MeasureText(row.Text, row.Font, New Size(Integer.MaxValue, Integer.MaxValue),
                                                               TextFormatFlags.SingleLine).Width
            If measured > longest Then longest = measured
        Next
        Return longest
    End Function

    ''' <summary>Places the rows one under another: each <paramref name="rowWidthPx"/> wide at
    ''' <paramref name="startInsetPx"/> from the left, <paramref name="gapPx"/> apart, and as tall as
    ''' <see cref="SettingsRailRules.RowHeightPx"/> says for the caption wrapped in the room beside the glyph - the 32 px
    ''' target, or taller when the caption wraps. A caption is never clipped and never ends in an ellipsis. Returns
    ''' the height the rows use together.</summary>
    Friend Function LayoutRows(rowWidthPx As Integer, startInsetPx As Integer, gapPx As Integer, factor As Single) As Integer
        Dim textSlack As Integer = SettingsRailRules.GlyphPx(factor) + SettingsRailRules.GlyphGapPx(factor) +
            UiSizing.DevicePx(RenderSlackLogical, factor)
        Dim top As Integer = 0
        SuspendLayout()
        Try
            For Each row As SettingsRailRow In railRows
                Dim available As Integer = Math.Max(1, rowWidthPx - row.Padding.Horizontal - textSlack)
                Dim captionHeight As Integer = TextRenderer.MeasureText(row.Text, row.Font, New Size(available, Integer.MaxValue),
                                                                        TextFormatFlags.WordBreak).Height
                row.SetBounds(startInsetPx, top, rowWidthPx, SettingsRailRules.RowHeightPx(factor, captionHeight))
                top = row.Bottom + gapPx
            Next
        Finally
            ResumeLayout(True)
        End Try
        Return Math.Max(0, top - gapPx)
    End Function

    ''' <summary>The rows in page order.</summary>
    Friend ReadOnly Property Rows As IReadOnlyList(Of SettingsRailRow)
        Get
            Return railRows
        End Get
    End Property

    ''' <summary>Where the rail docks for a reading direction (rule 1): at the reading START, left in left-to-right
    ''' and right in right-to-left. <c>DockStyle.Left</c> does not mirror under <c>RightToLeft = Yes</c> (the
    ''' project never sets <c>RightToLeftLayout</c>), so the window sets the physical side itself.</summary>
    Friend Shared Function DockFor(rightToLeft As Boolean) As DockStyle
        Return If(rightToLeft, DockStyle.Right, DockStyle.Left)
    End Function

    ''' <summary>Docks <paramref name="rail"/> (the rail's side panel) at the reading start of
    ''' <paramref name="rightToLeft"/>; no change, and no layout pass, when it is there already.</summary>
    Friend Shared Sub ApplyDirection(rail As Control, rightToLeft As Boolean)
        If rail Is Nothing Then Return
        Dim dock As DockStyle = DockFor(rightToLeft)
        If rail.Dock <> dock Then rail.Dock = dock
    End Sub

    ''' <summary>Adds a row at the end of the list. The first row is the tab stop until a page is selected.</summary>
    Friend Sub AddRow(row As SettingsRailRow)
        If row Is Nothing Then Throw New ArgumentNullException(NameOf(row))
        row.Attach(Me)
        row.TabStop = False
        railRows.Add(row)
        Controls.Add(row)
        If railRows.Count = 1 Then SetTabStop(0)
    End Sub

    ''' <summary>The row of the page that is shown, or -1. Setting it marks exactly that row selected (the
    ''' accessible Selected state) and none of the others.</summary>
    <System.ComponentModel.Browsable(False)>
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Friend Property SelectedIndex As Integer
        Get
            Return selectedValue
        End Get
        Set(value As Integer)
            Dim index As Integer = If(value < 0 OrElse value >= railRows.Count, -1, value)
            selectedValue = index
            For position As Integer = 0 To railRows.Count - 1
                railRows(position).SetSelected(position = index)
            Next
            ' While the focus is inside, the tab stop is where the focus is; otherwise it follows the page.
            If Not ContainsFocus Then ResetTabStop()
        End Set
    End Property

    ''' <summary>How many rows are a tab stop right now (one, as soon as the rail has a row).</summary>
    Friend Function TabStopCount() As Integer
        Dim count As Integer = 0
        For Each row As SettingsRailRow In railRows
            If row.TabStop Then count += 1
        Next
        Return count
    End Function

    ''' <summary>Moves the tab stop back to the selected row (the first row when none is selected).</summary>
    Friend Sub ResetTabStop()
        If railRows.Count = 0 Then Return
        SetTabStop(If(selectedValue >= 0, selectedValue, 0))
    End Sub

    ''' <summary>A row took the focus (mouse or keyboard): it becomes the one tab stop.</summary>
    Friend Sub NoteFocus(row As SettingsRailRow)
        Dim index As Integer = railRows.IndexOf(row)
        If index >= 0 Then SetTabStop(index)
    End Sub

    ''' <summary>Up, Down, Home and End: moves the FOCUS ring to the row <see cref="SettingsRailRules.NextIndex"/>
    ''' names. The selected page does not change.</summary>
    Friend Sub MoveFocus(from As SettingsRailRow, key As RailKey)
        Dim current As Integer = railRows.IndexOf(from)
        Dim target As Integer = SettingsRailRules.NextIndex(current, railRows.Count, key)
        If target < 0 OrElse target = current Then Return
        Dim row As SettingsRailRow = railRows(target)
        SetTabStop(target)
        row.Select()
        ScrollControlIntoView(row)
    End Sub

    Protected Overrides Sub OnLeave(e As EventArgs)
        MyBase.OnLeave(e)
        ResetTabStop()
    End Sub

    Private Sub SetTabStop(index As Integer)
        For position As Integer = 0 To railRows.Count - 1
            Dim wanted As Boolean = position = index
            If railRows(position).TabStop <> wanted Then railRows(position).TabStop = wanted
        Next
    End Sub

End Class

''' <summary>
''' One destination of the rail: a flat button that reads as a list item (rule 7) - role
''' <see cref="AccessibleRole.ListItem"/>, selectable and focusable, with the Selected state kept in step with
''' the page by <see cref="SettingsRailList.SelectedIndex"/>. The caption is the accessible name (the window sets
''' <c>AccessibleName</c>). The selected row also keeps a drawn, non-colour cue (the window's leading bar).
''' </summary>
Friend NotInheritable Class SettingsRailRow
    Inherits Button

    Private owningRail As SettingsRailList
    Private selectedValue As Boolean

    Friend Sub New()
        MyBase.New()
        AccessibleRole = AccessibleRole.ListItem
    End Sub

    ''' <summary>True for the row of the page that is shown.</summary>
    Friend ReadOnly Property Selected As Boolean
        Get
            Return selectedValue
        End Get
    End Property

    Friend Sub Attach(rail As SettingsRailList)
        owningRail = rail
    End Sub

    ''' <summary>Called by the rail only. Tells assistive technology when the state really changed.</summary>
    Friend Sub SetSelected(value As Boolean)
        If selectedValue = value Then Return
        selectedValue = value
        Invalidate()
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1)
        If value Then AccessibilityNotifyClients(AccessibleEvents.SelectionAdd, -1)
    End Sub

    Protected Overrides Function CreateAccessibilityInstance() As AccessibleObject
        Return New RowAccessibility(Me)
    End Function

    ''' <summary>The keys of a vertical list belong to the row: Up, Down, Home and End move the focus, Enter and
    ''' Space choose, and Left and Right do nothing (a dialog would otherwise walk the focus sideways and wrap).
    ''' Combinations with a modifier are left to the window.</summary>
    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        If (keyData And Keys.Modifiers) = Keys.None Then
            Select Case keyData And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Home, Keys.End, Keys.Left, Keys.Right, Keys.Return
                    Return True
            End Select
        End If
        Return MyBase.IsInputKey(keyData)
    End Function

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Modifiers = Keys.None Then
            Dim key As RailKey = RailKey.None
            If TryMapKey(e.KeyCode, key) Then
                e.Handled = True
                e.SuppressKeyPress = True
                If SettingsRailRules.IsSelectionKey(key) Then
                    PerformClick()
                ElseIf owningRail IsNot Nothing Then
                    owningRail.MoveFocus(Me, key)
                End If
                Return
            End If
        End If
        MyBase.OnKeyDown(e)
    End Sub

    ''' <summary>Space was answered on key down; the button's own key-up click would choose the page a second time.</summary>
    Protected Overrides Sub OnKeyUp(e As KeyEventArgs)
        If e.KeyCode = Keys.Space AndAlso e.Modifiers = Keys.None Then
            e.Handled = True
            Return
        End If
        MyBase.OnKeyUp(e)
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e)
        If owningRail IsNot Nothing Then owningRail.NoteFocus(Me)
    End Sub

    ''' <summary>The key codes the rail answers; Left and Right are claimed and ignored (<see cref="RailKey.None"/>).</summary>
    Private Shared Function TryMapKey(code As Keys, ByRef key As RailKey) As Boolean
        Select Case code
            Case Keys.Up : key = RailKey.Up
            Case Keys.Down : key = RailKey.Down
            Case Keys.Home : key = RailKey.Home
            Case Keys.End : key = RailKey.End
            Case Keys.Return : key = RailKey.Enter
            Case Keys.Space : key = RailKey.Space
            Case Keys.Left, Keys.Right : key = RailKey.None
            Case Else : Return False
        End Select
        Return True
    End Function

    Private NotInheritable Class RowAccessibility
        Inherits Control.ControlAccessibleObject

        Private ReadOnly row As SettingsRailRow

        Friend Sub New(owner As SettingsRailRow)
            MyBase.New(owner)
            row = owner
        End Sub

        Public Overrides ReadOnly Property Role As AccessibleRole
            Get
                Return AccessibleRole.ListItem
            End Get
        End Property

        Public Overrides ReadOnly Property State As AccessibleStates
            Get
                Dim value As AccessibleStates = MyBase.State Or AccessibleStates.Selectable Or AccessibleStates.Focusable
                If row.Selected Then value = value Or AccessibleStates.Selected
                Return value
            End Get
        End Property

        Public Overrides Sub DoDefaultAction()
            row.PerformClick()
        End Sub
    End Class

End Class
#End If
