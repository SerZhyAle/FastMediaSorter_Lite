#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

' DPI and long-caption layout of the viewer's secondary windows (ticket 124, UX-202/UX-203).
'
' WHY: the viewer has been PerMonitorV2 since the chrome migration, so at the owner's 175 % display a
' point font grows 1.75x while a window built from literal pixel positions does not - captions clip and
' fields overlap. The comments that said "the viewer is pinned to DpiUnaware" were true of the net48
' reference build only. Every code-built window now (a) turns on DPI scaling as the LAST line of its
' builder - AutoScaleMode alone is a no-op without AutoScaleDimensions (the Companion learned that, see
' its DpiLayout) - and (b) calls FitContent from OnLoad, so a caption that wraps in a longer language
' pushes what is under it down by exactly that much instead of being cut.
'
' It deliberately keeps each window's authored layout (absolute positions in 96-dpi pixels) instead of
' rebuilding it around TableLayoutPanels: at 100 % and in the language it was drawn in nothing moves, so a
' window this change cannot look at cannot be made worse; only what does not fit is reflowed. The rules
' themselves are pure (DialogReflow in DialogRules.vb) and held by tests.
'
' Linked into the test project: it names no viewer window and no application service.
Friend NotInheritable Class DialogLayout

    Private Sub New()
    End Sub

    ''' <summary>Margin kept between a growing control and the window's far edge, in 96-dpi pixels.</summary>
    Private Const EdgeMargin As Integer = 14

    ''' <summary>Gap kept between two buttons of a row, in 96-dpi pixels.</summary>
    Private Const ButtonGap As Integer = 8

    ''' <summary>Turns on DPI scaling for a hand-built window. Call it as the LAST statement of the builder,
    ''' once every child has been added: scaling runs on the first layout after the flag is set and only
    ''' touches the children that exist then. 96 dpi, not font metrics: these layouts are authored in 96-dpi
    ''' pixels, so the honest factor is the DPI ratio (font mode rounds to 2.0x vertically at 168 dpi).</summary>
    Friend Shared Sub ApplyAutoScale(f As ContainerControl)
        If f Is Nothing Then Return
        f.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        f.AutoScaleMode = AutoScaleMode.Dpi
    End Sub

    ''' <summary>Scales a logical (96-dpi) width the way WinForms never scales a ListView column. Call it where
    ''' the control has its real DPI - OnLoad, not the constructor.</summary>
    Friend Shared Function Device(c As Control, logical As Integer) As Integer
        Return If(c Is Nothing, logical, c.LogicalToDeviceUnits(logical))
    End Function

    ''' <summary>Caps a window to the working area of the monitor it is on and keeps it fully visible.</summary>
    Friend Shared Sub ClampToWorkingArea(f As Form)
        If f Is Nothing OrElse f.IsDisposed Then Return
        Try
            Dim area As Rectangle = Screen.FromControl(f).WorkingArea
            If f.MinimumSize.Width > area.Width OrElse f.MinimumSize.Height > area.Height Then
                f.MinimumSize = DialogPlacement.Capped(f.MinimumSize, area)
            End If
            Dim size As Size = DialogPlacement.Capped(f.Size, area)
            If size <> f.Size Then f.Size = size
            Dim x As Integer = Math.Max(area.Left, Math.Min(f.Left, area.Right - f.Width))
            Dim y As Integer = Math.Max(area.Top, Math.Min(f.Top, area.Bottom - f.Height))
            If x <> f.Left OrElse y <> f.Top Then f.Location = New Point(x, y)
        Catch ex As Exception
            AppFileLogger.LogException("DialogLayout clamp", ex)
        End Try
    End Sub

    ''' <summary>Reflows the window's absolutely positioned top-level children so none of them is cut by its
    ''' own text: wrapping labels and check boxes grow taller, a button whose caption outgrew it widens
    ''' towards the start of its row, and everything below a grown control moves down. Call it once from
    ''' OnLoad, after MyBase.OnLoad. Docked children are left to their dock; a window with none of the
    ''' problems is not touched at all.</summary>
    Friend Shared Sub FitContent(f As Form)
        If f Is Nothing OrElse f.IsDisposed Then Return
        Try
            f.PerformAutoScale()
            Dim kids As New List(Of Control)()
            For Each c As Control In f.Controls
                If c.Visible AndAlso c.Dock = DockStyle.None Then kids.Add(c)
            Next
            If kids.Count = 0 Then Return

            Dim scale As Single = f.DeviceDpi / 96.0F
            Dim sizeBefore As Size = f.Size
            f.SuspendLayout()
            Try
                WidenButtons(kids, scale, f)

                Dim originals As New List(Of Rectangle)()
                For Each c As Control In kids
                    originals.Add(c.Bounds)
                Next
                Dim needed As New List(Of Integer)()
                Dim anyGrowth As Boolean = False
                For Each c As Control In kids
                    Dim h As Integer = NeededHeight(c, f, scale)
                    needed.Add(h)
                    If h > 0 Then anyGrowth = True
                Next
                If Not anyGrowth Then Return

                Dim clientGrowth As Integer
                Dim grown As Rectangle() = DialogReflow.GrowVertical(originals, needed, clientGrowth)
                For i As Integer = 0 To kids.Count - 1
                    Dim c As Control = kids(i)
                    ' An auto-sized control already holds its new height; a fixed one gets it here. Width is
                    ' never changed by this pass.
                    c.SetBounds(grown(i).Left, grown(i).Top, c.Width, Math.Max(c.Height, grown(i).Height))
                Next
                If clientGrowth > 0 Then f.ClientSize = New Size(f.ClientSize.Width, f.ClientSize.Height + clientGrowth)
            Finally
                f.ResumeLayout(True)
            End Try
            ' The start position was decided for the size the window had; a window that grew keeps its centre.
            If f.Size <> sizeBefore AndAlso (f.StartPosition = FormStartPosition.CenterParent OrElse f.StartPosition = FormStartPosition.CenterScreen) Then
                f.Location = New Point(f.Left - (f.Width - sizeBefore.Width) \ 2, f.Top - (f.Height - sizeBefore.Height) \ 2)
            End If
            ClampToWorkingArea(f)
        Catch ex As Exception
            ' A layout nicety must never stop a window from opening.
            AppFileLogger.LogException("DialogLayout fit", ex)
        End Try
    End Sub

    ''' <summary>Keeps the docked answer row (<see cref="DialogButtonRow"/>) of a hand-positioned window clear of the
    ''' content above it (ticket 243, T-8b). The window is authored with the room of a one-line row; a row that wraps
    ''' in a longer language or at a higher scale is taller, and a row docked to the bottom grows upwards over the
    ''' controls. The window is made taller by exactly the missing amount (and kept on its monitor). Call it from
    ''' OnLoad after <see cref="FitContent"/>; a window whose row fits is not touched. The row's need is asked of it
    ''' for the window's width, so it does not depend on whether the first layout pass has already run.</summary>
    Friend Shared Sub KeepRoomForRow(f As Form, row As Control)
        If f Is Nothing OrElse f.IsDisposed OrElse row Is Nothing Then Return
        Try
            f.PerformAutoScale()
            Dim lowest As Integer = 0
            For Each c As Control In f.Controls
                If Object.ReferenceEquals(c, row) OrElse Not c.Visible OrElse c.Dock <> DockStyle.None Then Continue For
                lowest = Math.Max(lowest, c.Bottom)
            Next
            Dim rowHeight As Integer = Math.Max(row.Height, row.GetPreferredSize(New Size(f.ClientSize.Width, 0)).Height)
            Dim missing As Integer = lowest + rowHeight - f.ClientSize.Height
            If missing <= 0 Then Return
            Dim sizeBefore As Size = f.Size
            f.ClientSize = New Size(f.ClientSize.Width, f.ClientSize.Height + missing)
            If f.Size <> sizeBefore AndAlso (f.StartPosition = FormStartPosition.CenterParent OrElse f.StartPosition = FormStartPosition.CenterScreen) Then
                f.Location = New Point(f.Left - (f.Width - sizeBefore.Width) \ 2, f.Top - (f.Height - sizeBefore.Height) \ 2)
            End If
            ClampToWorkingArea(f)
        Catch ex As Exception
            ' A layout nicety must never stop a window from opening.
            AppFileLogger.LogException("DialogLayout row room", ex)
        End Try
    End Sub

    ''' <summary>Grows ONE fixed-size label to the height its current text needs, after the text was changed at
    ''' run time (a refusal, a warning, a status line), and moves everything under it down by the same amount,
    ''' taking the window with it. Does nothing when the text already fits.</summary>
    Friend Shared Sub FitLabel(label As Label)
        If label Is Nothing OrElse label.IsDisposed OrElse label.AutoSize OrElse label.Parent Is Nothing Then Return
        Try
            Dim need As Integer = NeededLabelHeight(label)
            If need <= 0 Then Return
            Dim parent As Control = label.Parent
            Dim bounds As New List(Of Rectangle)()
            Dim wants As New List(Of Integer)()
            Dim kids As New List(Of Control)()
            For Each c As Control In parent.Controls
                If c.Dock <> DockStyle.None Then Continue For
                kids.Add(c)
                bounds.Add(c.Bounds)
                wants.Add(If(c Is label, need, 0))
            Next
            Dim growth As Integer
            Dim moved As Rectangle() = DialogReflow.GrowVertical(bounds, wants, growth)
            For i As Integer = 0 To kids.Count - 1
                If moved(i) <> bounds(i) Then kids(i).SetBounds(moved(i).Left, moved(i).Top, moved(i).Width, moved(i).Height)
            Next
            Dim host As Form = TryCast(parent, Form)
            If host IsNot Nothing AndAlso growth > 0 Then host.ClientSize = New Size(host.ClientSize.Width, host.ClientSize.Height + growth)
            If host IsNot Nothing Then ClampToWorkingArea(host)
        Catch ex As Exception
            AppFileLogger.LogException("DialogLayout fit label", ex)
        End Try
    End Sub

    ''' <summary>The height a fixed-size, wrapping label needs for its text, or 0 when it has enough.</summary>
    Private Shared Function NeededLabelHeight(label As Label) As Integer
        If label.AutoEllipsis OrElse label.Text.Length = 0 Then Return 0
        Dim flags As TextFormatFlags = TextFormatFlags.WordBreak Or If(label.UseMnemonic, TextFormatFlags.Default, TextFormatFlags.NoPrefix)
        Dim width As Integer = Math.Max(1, label.Width - label.Padding.Horizontal)
        Dim h As Integer = TextRenderer.MeasureText(label.Text, label.Font, New Size(width, Integer.MaxValue), flags).Height + label.Padding.Vertical + 2
        Return If(h > label.Height, h, 0)
    End Function

    ''' <summary>A button whose caption outgrew it widens away from the side its row is anchored to: a button in
    ''' the right half of the window keeps its right edge and grows to the left, one in the left half keeps its left
    ''' edge and grows to the right (<see cref="DialogReflow.GrowTowardsStart"/> and its mirror). Buttons it now
    ''' covers are moved aside; any other control in the same row - the text box a "Browse.." button sits beside -
    ''' gives up width instead of moving, so a field never slides off the window's edge. A row that ends up wider
    ''' than the window widens the window.</summary>
    Private Shared Sub WidenButtons(kids As List(Of Control), scale As Single, f As Form)
        Dim buttons As New List(Of Control)()
        Dim bounds As New List(Of Rectangle)()
        For Each c As Control In kids
            If TypeOf c Is Button Then
                buttons.Add(c)
                bounds.Add(c.Bounds)
            End If
        Next
        If buttons.Count = 0 Then Return
        Dim gap As Integer = CInt(Math.Round(ButtonGap * scale))
        Dim half As Integer = f.ClientSize.Width \ 2
        Dim wantsEnd As New List(Of Integer)()
        Dim wantsStart As New List(Of Integer)()
        Dim any As Boolean = False
        For Each c As Control In buttons
            Dim textWidth As Integer = TextRenderer.MeasureText(c.Text, c.Font).Width
            ' A glyph before the caption (ButtonGlyphs) is part of what the button must hold: its bitmap
            ' already carries the 6 px gap, so the caption is never clipped to make room for it.
            Dim glyphWidth As Integer = If(TypeOf c Is Button, ButtonGlyphs.ExtraWidth(DirectCast(c, Button)), 0)
            Dim padded As Integer = textWidth + glyphWidth + CInt(Math.Round(2 * 12 * scale))
            Dim need As Integer = If(padded > c.Width, padded, 0)
            If need > 0 Then any = True
            Dim rightHalf As Boolean = c.Left + c.Width \ 2 > half
            wantsStart.Add(If(rightHalf, need, 0))
            wantsEnd.Add(If(rightHalf, 0, need))
        Next
        If Not any Then Return
        Dim after As Rectangle() = DialogReflow.GrowTowardsStart(bounds, wantsStart, gap)
        after = DialogReflow.GrowTowardsEnd(after, wantsEnd, gap)
        Dim widest As Integer = f.ClientSize.Width
        For i As Integer = 0 To buttons.Count - 1
            If after(i) = bounds(i) Then Continue For
            buttons(i).SetBounds(after(i).Left, after(i).Top, after(i).Width, after(i).Height)
            widest = Math.Max(widest, after(i).Right + CInt(Math.Round(EdgeMargin * scale)))
            For Each other As Control In kids
                If TypeOf other Is Button OrElse other.Bottom <= after(i).Top OrElse other.Top >= after(i).Bottom Then Continue For
                If other.Left < after(i).Left AndAlso other.Right + gap > after(i).Left Then
                    other.Width = Math.Max(1, after(i).Left - gap - other.Left)
                End If
            Next
        Next
        If widest > f.ClientSize.Width Then f.ClientSize = New Size(widest, f.ClientSize.Height)
    End Sub

    ''' <summary>The height this control needs for its text, or 0 when what it has is enough (or the control is
    ''' not one that wraps).</summary>
    Private Shared Function NeededHeight(c As Control, f As Form, scale As Single) As Integer
        ' A boolean option row (ticket 243 T-11b) is as tall as its caption and explanation need at this scale; it is
        ' arranged here, once the window has its scale, and the controls under it move down by what it grew.
#If Not NETFRAMEWORK Then
        If DialogOptionRow.IsRow(c) Then Return DialogOptionRow.NeededHeight(c)
#End If
        Dim label As Label = TryCast(c, Label)
        Dim check As ButtonBase = If(TypeOf c Is CheckBox OrElse TypeOf c Is RadioButton, DirectCast(c, ButtonBase), Nothing)
        If label IsNot Nothing AndAlso Not label.AutoSize Then Return NeededLabelHeight(label)
        If (label IsNot Nothing AndAlso label.AutoSize) OrElse (check IsNot Nothing AndAlso check.AutoSize) Then
            ' It sizes itself; give it the room to the window's far edge to wrap in, and report the height it
            ' ends with so what is under it can move.
            Dim before As Integer = c.Height
            Dim available As Integer = f.ClientSize.Width - c.Left - CInt(Math.Round(EdgeMargin * scale))
            If available > 0 AndAlso c.PreferredSize.Width > available Then c.MaximumSize = New Size(available, 0)
            Dim after As Integer = Math.Max(c.Height, c.PreferredSize.Height)
            Return If(after > before, after, 0)
        End If
        Return 0
    End Function

End Class
#End If
