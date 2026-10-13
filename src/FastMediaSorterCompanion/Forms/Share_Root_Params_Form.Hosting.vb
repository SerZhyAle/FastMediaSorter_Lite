Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Partial Public Class Share_Root_Params_Form
    Private _isHosted As Boolean
    Private _ownedBoldFont As Font
    Private _ownedTip As ToolTip
    Private ReadOnly _hostFonts As New List(Of Font)

    Friend ReadOnly Property ProfileCaption As String
        Get
            Return cmbProfile.Text
        End Get
    End Property

    ' The very same controls and ReadDraft are used by Configure and the guided add host.
    Friend Function CreateHostedEditor(width As Integer, Optional dpi As Integer = 96) As Control
        _isHosted = True
        Dim rows = _content.Controls.Cast(Of Control)().OrderBy(Function(c) _content.GetRow(c)).
            ThenBy(Function(c) _content.GetColumn(c)).ToArray()
        Dim advancedStart As Integer = _content.GetRow(txtComment)
        Dim positions = rows.Select(Function(c) _content.GetRow(c)).ToArray()
        Dim root As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Margin = Padding.Empty}
        Dim advanced As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Visible = False, .Margin = Padding.Empty}
        Dim toggle As New Button With {.AutoSize = True, .MinimumSize = New Size(120, 44),
            .Text = Localization.T("Дополнительные параметры"), .Margin = New Padding(0, 10, 0, 0)}
        ButtonGlyphs.Apply(toggle, "nav.expand")
        AddHandler toggle.Click, Sub()
            advanced.Visible = Not advanced.Visible
            ButtonGlyphs.Apply(toggle, If(advanced.Visible, "nav.collapse", "nav.expand"))
        End Sub
        For i As Integer = 0 To rows.Length - 1
            Dim control As Control = rows(i)
            _content.Controls.Remove(control)
            If positions(i) >= advancedStart Then
                If toggle.Parent Is Nothing Then root.Controls.Add(toggle)
                advanced.Controls.Add(control)
            Else
                root.Controls.Add(control)
            End If
            PrepareHostedControl(control, width, dpi)
        Next
        root.Controls.Add(advanced)
        txtPin.UseSystemPasswordChar = True
        Return root
    End Function

    Private Sub PrepareHostedControl(control As Control, width As Integer, dpi As Integer)
        control.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        control.MaximumSize = New Size(width, 0)
        If TypeOf control Is Label OrElse TypeOf control Is ButtonBase Then
            control.AutoSize = True
        End If
        If TypeOf control Is ButtonBase Then control.MinimumSize = New Size(0, 44)
        If TypeOf control Is TextBox Then
            control.AutoSize = False
            control.MinimumSize = New Size(0, 45)
            control.Height = 45
        End If
        If TypeOf control Is ComboBox Then
            Dim combo = DirectCast(control, ComboBox)
            combo.DrawMode = DrawMode.OwnerDrawFixed
            combo.ItemHeight = CInt(Math.Ceiling(44 * dpi / 96.0)) - 6
            AddHandler combo.DrawItem, Sub(sender, e)
                e.DrawBackground()
                If e.Index < 0 Then Return
                Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis
                If combo.RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.Right Or TextFormatFlags.RightToLeft
                Dim ink = If((e.State And DrawItemState.Selected) <> 0, SystemColors.HighlightText, ThemeService.Palette.TextPrimary)
                TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items(e.Index)), combo.Font, e.Bounds, ink, flags)
                e.DrawFocusRectangle()
            End Sub
        End If
        If TypeOf control Is NumericUpDown Then
            Dim font As New Font(Me.Font.FontFamily, 26.0F)
            _hostFonts.Add(font)
            control.Font = font
            Return ' Native spinner children are part of its one target, not separate editors.
        End If
        For Each child As Control In control.Controls
            PrepareHostedControl(child, width - 12, dpi)
        Next
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If txtPin IsNot Nothing Then txtPin.Text = ""
            If _isHosted AndAlso _params IsNot Nothing Then _params.AccessPin = ""
            If _ownedTip IsNot Nothing Then _ownedTip.Dispose()
            ShareIcons.FreeIcon(Me.Icon, _iconHandle)
        End If
        MyBase.Dispose(disposing)
        If disposing Then
            If _ownedBoldFont IsNot Nothing Then _ownedBoldFont.Dispose()
            For Each font As Font In _hostFonts : font.Dispose() : Next
            _hostFonts.Clear()
        End If
    End Sub
End Class
