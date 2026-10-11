Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Windows.Forms

' WINDOWS-UI section 4: the editor spans the caption and hint in the trailing column.
' Measure and arrange directly: nested auto-sized tables otherwise remeasure on every probe.
Friend NotInheritable Class ManagerSettingRow
    Inherits Panel

    Friend ReadOnly Caption As Label
    Friend ReadOnly Hint As Label
    Friend ReadOnly Editor As Control
    Private arranging As Boolean
    Private ReadOnly declaredWidth As Integer
    Private ReadOnly textMeasurements As New Dictionary(Of Integer, Size)()

    Friend Sub New(caption As Label, editor As Control, margin As Padding, Optional hintText As String = "")
        Me.Caption = caption
        Me.Editor = editor
        declaredWidth = editor.Width
        Width = 520
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        Dock = DockStyle.Top
        Me.Margin = margin
        SuspendLayout()
        caption.Dock = DockStyle.None
        caption.Margin = New Padding(0, 2, 12, 0)
        caption.MaximumSize = Size.Empty
        Hint = New Label With {.AutoSize = True, .Text = hintText,
            .ForeColor = SystemColors.GrayText, .Margin = New Padding(0, 4, 12, 2), .Visible = hintText.Length > 0}
        editor.AccessibleName = caption.Text
        editor.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        Controls.AddRange({caption, Hint, editor})
        For Each label In {caption, Hint}
            AddHandler label.TextChanged, Sub() textMeasurements.Clear()
            AddHandler label.FontChanged, Sub() textMeasurements.Clear()
            AddHandler label.MarginChanged, Sub() textMeasurements.Clear()
        Next
        ResumeLayout(False)
    End Sub

    Friend Sub SetHint(text As String)
        Hint.Text = text
        Hint.Visible = text.Length > 0
        Editor.AccessibleDescription = text
    End Sub

    Private Function ValueWidth(width As Integer) As Integer
        Return Math.Min(LogicalToDeviceUnits(220), Math.Max(2, width - Padding.Horizontal) \ 2)
    End Function

    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        If Caption Is Nothing OrElse Editor Is Nothing OrElse Hint Is Nothing Then Return MyBase.GetPreferredSize(proposedSize)
        Dim width = If(proposedSize.Width > 0 AndAlso proposedSize.Width < 8192, proposedSize.Width, Me.Width)
        Dim textWidth = Math.Max(1, width - ValueWidth(width) - Caption.Margin.Horizontal - Padding.Horizontal)
        Dim measured = MeasureTextColumns(textWidth)
        Dim textHeight = measured.Width + measured.Height
        Dim valueHeight = Editor.GetPreferredSize(New Size(ValueWidth(width), 0)).Height + Editor.Margin.Vertical
        Return New Size(width, Math.Max(textHeight, valueHeight) + Padding.Vertical)
    End Function

    Private Function MeasureTextColumns(width As Integer) As Size
        Dim measured As Size
        If textMeasurements.TryGetValue(width, measured) Then Return measured
        measured = New Size(Caption.GetPreferredSize(New Size(width, 0)).Height + Caption.Margin.Vertical,
            If(Hint.Text.Length > 0, Hint.GetPreferredSize(New Size(width, 0)).Height + Hint.Margin.Vertical, 0))
        If textMeasurements.Count >= 16 Then textMeasurements.Clear()
        textMeasurements(width) = measured
        Return measured
    End Function

    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        ArrangeColumns()
        MyBase.OnLayout(e)
    End Sub

    Friend Sub ArrangeColumns()
        If arranging OrElse Caption Is Nothing OrElse Hint Is Nothing OrElse ClientSize.Width < 2 Then Return
        arranging = True
        Try
            Dim value = ValueWidth(ClientSize.Width)
            Dim maximum As New Size(Math.Max(1, ClientSize.Width - value - Caption.Margin.Horizontal - Padding.Horizontal), 0)
            If Caption.MaximumSize <> maximum OrElse Hint.MaximumSize <> maximum Then
                textMeasurements.Clear()
                Caption.MaximumSize = maximum
                Hint.MaximumSize = maximum
            End If
            Dim measured = MeasureTextColumns(maximum.Width)
            Dim captionHeight = measured.Width
            Dim hintHeight = measured.Height
            Dim fill = TypeOf Editor Is TextBox OrElse TypeOf Editor Is ComboBox OrElse TypeOf Editor Is FlowLayoutPanel
            Dim editorWidth = Math.Max(1, If(fill, value - Editor.Margin.Horizontal, Math.Min(declaredWidth, value - Editor.Margin.Horizontal)))
            If TypeOf Editor Is NumericUpDown Then editorWidth = Math.Min(value, LogicalToDeviceUnits(110))
            If Editor.Width <> editorWidth Then Editor.Width = editorWidth
            Dim flow = TryCast(Editor, FlowLayoutPanel)
            If flow IsNot Nothing Then
                flow.MaximumSize = New Size(editorWidth, 0)
                flow.MinimumSize = New Size(editorWidth, 0)
                flow.WrapContents = True
            End If
            Dim rtl = RightToLeft = RightToLeft.Yes
            Dim textLeft = If(rtl, Padding.Left + value + Caption.Margin.Right, Padding.Left + Caption.Margin.Left)
            Caption.Bounds = New Rectangle(textLeft, Padding.Top + Caption.Margin.Top, maximum.Width, captionHeight - Caption.Margin.Vertical)
            Hint.Bounds = New Rectangle(textLeft, Padding.Top + captionHeight + Hint.Margin.Top, maximum.Width, Math.Max(0, hintHeight - Hint.Margin.Vertical))
            Dim editorLeft = If(rtl, Padding.Left + Editor.Margin.Left, ClientSize.Width - Padding.Right - Editor.Margin.Right - editorWidth)
            Editor.Location = New Point(editorLeft, Padding.Top + Editor.Margin.Top)
            If flow IsNot Nothing Then flow.Height = flow.GetPreferredSize(New Size(editorWidth, 0)).Height
        Finally
            arranging = False
        End Try
    End Sub
End Class
