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

''' <summary>Scales and paints the existing NumericUpDown buttons. The native control
''' retains validation, keyboard input, accessibility, limits and press-and-hold acceleration.</summary>
Friend NotInheritable Class SettingsNumericStepper
    Friend Const ButtonWidthLogical As Integer = 28

    ' .NET 10 PositionControls and its native button painter share this width. Updating
    ' it keeps the edit's scrolling/caret area and both button hit targets in agreement.
    Private Shared ReadOnly nativeWidth As FieldInfo = GetType(UpDownBase).GetField(
        "_defaultButtonsWidth", BindingFlags.Instance Or BindingFlags.NonPublic)
    Private Shared ReadOnly states As New ConditionalWeakTable(Of NumericUpDown, State)()

    Private NotInheritable Class State
        Public Scale As Single
        Public TrackDpi As Boolean
        Public Buttons As Control
        Public Back As Color
        Public Ink As Color
        Public Border As Color
        Public Accent As Color
        Public PressedHalf As Integer = -1
    End Class

    Friend Shared Sub Apply(number As NumericUpDown, scale As Single, back As Color,
                            ink As Color, border As Color, accent As Color, Optional trackDpi As Boolean = False)
        If nativeWidth Is Nothing Then Throw New NotSupportedException("NumericUpDown button layout requires the .NET 10 width field.")
        Dim state As State = Nothing
        If Not states.TryGetValue(number, state) Then
            state = New State()
            For Each child As Control In number.Controls
                If Not TypeOf child Is TextBox Then
                    state.Buttons = child
                    Exit For
                End If
            Next
            If state.Buttons Is Nothing Then Throw New NotSupportedException("NumericUpDown button control is unavailable.")
            states.Add(number, state)
            Dim attached As State = state
            AddHandler state.Buttons.Paint, Sub(sender, e) PaintButtons(number, attached, e.Graphics)
            AddHandler number.MouseDown,
                Sub(sender, e)
                    If e.Button <> MouseButtons.Left Then Return
                    Dim point As Point = attached.Buttons.PointToClient(Control.MousePosition)
                    attached.PressedHalf = If(attached.Buttons.ClientRectangle.Contains(point),
                                              If(point.Y < attached.Buttons.Height \ 2, 0, 1), -1)
                    attached.Buttons.Invalidate()
                End Sub
            AddHandler number.MouseUp, Sub()
                                          attached.PressedHalf = -1
                                          attached.Buttons.Invalidate()
                                      End Sub
            AddHandler number.EnabledChanged, Sub() attached.Buttons.Invalidate()
            AddHandler number.MouseMove, Sub() attached.Buttons.Invalidate()
            AddHandler number.MouseLeave, Sub() attached.Buttons.Invalidate()
            AddHandler number.DpiChangedAfterParent,
                Sub() Apply(number, If(attached.TrackDpi, number.DeviceDpi / 96.0F, attached.Scale),
                            attached.Back, attached.Ink, attached.Border, attached.Accent, attached.TrackDpi)
        End If
        state.Scale = scale
        state.TrackDpi = trackDpi
        state.Back = back
        state.Ink = ink
        state.Border = border
        state.Accent = accent
        nativeWidth.SetValue(number, CInt(Math.Round(ButtonWidthLogical * scale)))
        number.PerformLayout()
        state.Buttons.Invalidate()
    End Sub

    Private Shared Sub PaintButtons(number As NumericUpDown, state As State, graphics As Graphics)
        Dim buttons As Control = state.Buttons
        Dim point As Point = buttons.PointToClient(Control.MousePosition)
        Dim half As Integer = buttons.Height \ 2
        graphics.SmoothingMode = SmoothingMode.AntiAlias
        For index As Integer = 0 To 1
            Dim bounds As New Rectangle(0, If(index = 0, 0, half), buttons.Width,
                                        If(index = 0, half, buttons.Height - half))
            Dim hovered As Boolean = number.Enabled AndAlso bounds.Contains(point)
            Dim pressed As Boolean = hovered AndAlso buttons.Capture AndAlso state.PressedHalf = index
            Dim fill As Color = If(pressed, state.Accent, state.Back)
            Dim ink As Color = If(number.Enabled, state.Ink, state.Border)
            Using brush As New SolidBrush(fill)
                graphics.FillRectangle(brush, bounds)
            End Using
            Using pen As New Pen(If(hovered, state.Accent, state.Border), Math.Max(1.0F, state.Scale))
                graphics.DrawRectangle(pen, bounds.X, bounds.Y, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1))
            End Using
            Dim width As Single = Math.Min(10.0F * state.Scale, bounds.Width * 0.45F)
            Dim rise As Single = Math.Min(4.0F * state.Scale, bounds.Height * 0.24F)
            Dim x As Single = bounds.Left + bounds.Width / 2.0F
            Dim y As Single = bounds.Top + bounds.Height / 2.0F
            Dim direction As Single = If(index = 0, -1.0F, 1.0F)
            Using pen As New Pen(ink, Math.Max(1.5F, 1.75F * state.Scale)) With {
                .StartCap = LineCap.Round, .EndCap = LineCap.Round, .LineJoin = LineJoin.Round}
                graphics.DrawLines(pen, New PointF() {
                    New PointF(x - width / 2, y - direction * rise / 2),
                    New PointF(x, y + direction * rise / 2),
                    New PointF(x + width / 2, y - direction * rise / 2)})
            End Using
        Next
    End Sub
End Class
#End If
