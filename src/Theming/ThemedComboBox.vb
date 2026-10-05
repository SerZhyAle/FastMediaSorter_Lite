#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

''' <summary>The native dropdown-list retains keyboard/value/accessibility semantics, while
''' its field, arrow and list use the same current palette in every secondary window.</summary>
Friend NotInheritable Class ThemedComboBox
    Private NotInheritable Class State
        Public Palette As ThemePalette
    End Class

    Private Shared ReadOnly States As New ConditionalWeakTable(Of ComboBox, State)()

    Friend Shared Sub Apply(combo As ComboBox, palette As ThemePalette)
        If combo.DropDownStyle <> ComboBoxStyle.DropDownList Then Return
        Dim state As State = Nothing
        If Not States.TryGetValue(combo, state) Then
            ' Keep existing specialized list renderers under their own window's control.
            If combo.DrawMode <> DrawMode.Normal Then Return
            state = New State With {.Palette = palette}
            States.Add(combo, state)
            Dim attached = state
            combo.DrawMode = DrawMode.OwnerDrawFixed
            GetType(Control).GetMethod("SetStyle", BindingFlags.Instance Or BindingFlags.NonPublic).Invoke(combo,
                {CObj(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or
                      ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw), CObj(True)})
            AddHandler combo.Paint, AddressOf PaintField
            AddHandler combo.DrawItem, AddressOf DrawItem
            Dim repaint As EventHandler = Sub() combo.Invalidate()
            AddHandler combo.SelectedIndexChanged, repaint
            AddHandler combo.EnabledChanged, repaint
            AddHandler combo.GotFocus, repaint
            AddHandler combo.LostFocus, repaint
            AddHandler combo.DropDown, repaint
            AddHandler combo.DropDownClosed, repaint
            AddHandler combo.RightToLeftChanged, repaint
            AddHandler combo.FontChanged, Sub() Apply(combo, attached.Palette)
            AddHandler combo.DpiChangedAfterParent, Sub() Apply(combo, attached.Palette)
        End If
        state.Palette = palette
        combo.ItemHeight = Math.Max(combo.LogicalToDeviceUnits(28), combo.Font.Height + combo.LogicalToDeviceUnits(4))
        combo.Invalidate()
    End Sub

    Private Shared Sub PaintField(sender As Object, e As PaintEventArgs)
        Dim combo = DirectCast(sender, ComboBox)
        Dim state As State = Nothing
        If Not States.TryGetValue(combo, state) Then Return
        Dim palette = state.Palette
        Dim bounds = combo.ClientRectangle
        If bounds.Width < 2 OrElse bounds.Height < 2 Then Return
        Dim rtl = combo.RightToLeft = RightToLeft.Yes
        Dim width = Math.Min(bounds.Width \ 2, Math.Max(combo.LogicalToDeviceUnits(28), bounds.Height))
        Dim zone As New Rectangle(If(rtl, 0, bounds.Right - width), 0, width, bounds.Height)
        Dim ink = If(combo.Enabled, combo.ForeColor, palette.TextDisabled)
        Using back As New SolidBrush(combo.BackColor), face As New SolidBrush(palette.ColorOf(ThemeRole.Control))
            e.Graphics.FillRectangle(back, bounds)
            e.Graphics.FillRectangle(face, zone)
        End Using
        Dim pad = combo.LogicalToDeviceUnits(6)
        Dim textArea As New Rectangle(If(rtl, width + pad, pad), 0,
                                      Math.Max(1, bounds.Width - width - 2 * pad), bounds.Height)
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or
                    TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis
        If rtl Then flags = flags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
        TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.SelectedItem), combo.Font, textArea, ink, flags)
        Dim cx = zone.Left + zone.Width / 2.0F
        Dim cy = zone.Height / 2.0F
        Dim half = CSng(combo.LogicalToDeviceUnits(4))
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Using pen As New Pen(ink, Math.Max(1.5F, combo.DeviceDpi / 96.0F))
            e.Graphics.DrawLines(pen, {New PointF(cx - half, cy - half / 2),
                                      New PointF(cx, cy + half / 2), New PointF(cx + half, cy - half / 2)})
        End Using
        e.Graphics.SmoothingMode = SmoothingMode.None
        Using pen As New Pen(If(combo.Focused, palette.Accent, palette.Border), If(combo.Focused, 2, 1))
            e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1)
        End Using
    End Sub

    Private Shared Sub DrawItem(sender As Object, e As DrawItemEventArgs)
        Dim combo = DirectCast(sender, ComboBox)
        Dim state As State = Nothing
        If Not States.TryGetValue(combo, state) Then Return
        Dim palette = state.Palette
        Dim selected = (e.State And DrawItemState.Selected) <> 0
        Dim back = If(selected, palette.Accent, combo.BackColor)
        Dim ink = If(selected, palette.AccentInk, combo.ForeColor)
        Using brush As New SolidBrush(back)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        If e.Index >= 0 AndAlso e.Index < combo.Items.Count Then
            Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix
            If combo.RightToLeft = RightToLeft.Yes Then flags = flags Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items(e.Index)), combo.Font, e.Bounds, ink, flags)
        End If
        e.DrawFocusRectangle()
    End Sub
End Class
#End If
