#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' The F1 reference card (UX-115 of ticket 123): a scrollable, themed surface that lists the keys and the
''' mouse chords, grouped, from the rows <see cref="HelpCardRows"/> builds. It replaces the one static white
''' Label that clipped in a small window, ignored the theme and could not scroll.
'''
''' Drawn by hand over the app theme palette (<see cref="ThemeService"/>) in the script font of the active
''' language (<c>Localization.FontFamily()</c>), laid out for the current width, mirrored for right-to-left text
''' (the key column goes to the right; never RightToLeftLayout, which would mirror the device context) and
''' scaled by the monitor's DPI. It never takes the keyboard focus - the viewer's keys keep working, and
''' Esc closes it through the Escape ladder - and a click anywhere on it closes it too. The wheel and the
''' scroll bar scroll; the wheel event is consumed so it cannot flip a file behind the card.
''' </summary>
Friend NotInheritable Class HelpCardPanel
    Inherits Control

    ''' <summary>Raised by a click on the card: the host hides it.</summary>
    Public Event CloseRequested As EventHandler

    Private Enum ItemKind
        Title
        GroupHeader
        KeyText
        DescText
        Footer
    End Enum

    Private Structure DrawItem
        Public Kind As ItemKind
        Public Text As String
        Public Bounds As Rectangle
        Public Muted As Boolean
        Public TechnicalKeys As Boolean
    End Structure

    Private ReadOnly bar As New VScrollBar()
    Private ReadOnly items As New List(Of DrawItem)()
    Private groups As List(Of HelpCardGroup)
    Private contentHeight As Integer
    ''' <summary>The content is taller than the card, so the scroll bar is up. Kept here rather than read back
    ''' from <c>bar.Visible</c>, which answers False for as long as the card itself is hidden.</summary>
    Private barNeeded As Boolean
    Private bodyFont As Font
    Private keyFont As Font
    Private headerFont As Font
    Private titleFont As Font

    Public Sub New()
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        ' Never selectable: the keyboard stays with the viewer, so Esc and every other key keep working.
        SetStyle(ControlStyles.Selectable, False)
        TabStop = False
        Visible = False
        bar.Dock = DockStyle.Right
        bar.Visible = False
        bar.TabStop = False
        AddHandler bar.ValueChanged, Sub() Invalidate()
        Controls.Add(bar)
        AddHandler ThemeService.Changed, AddressOf OnThemeChanged
    End Sub

    ''' <summary>Fills the card from fresh rows (the live shortcut map) and re-lays it out at the current width.</summary>
    Public Sub SetContent(rows As List(Of HelpCardGroup))
        groups = rows
        ' Fresh fonts every time: the script font follows the UI language, which may have changed since.
        DisposeFonts()
        Relayout()
        If barNeeded Then bar.Value = 0
        Invalidate()
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        Relayout()
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        If Visible Then Relayout()
    End Sub

    Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
        MyBase.OnDpiChangedAfterParent(e)
        DisposeFonts()
        Relayout()
    End Sub

    Protected Overrides Sub OnClick(e As EventArgs)
        MyBase.OnClick(e)
        RaiseEvent CloseRequested(Me, EventArgs.Empty)
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        ' Not passed on: the base would bubble the wheel to the viewer, which flips files.
        ScrollByWheel(e.Delta)
        Dim handled As HandledMouseEventArgs = TryCast(e, HandledMouseEventArgs)
        If handled IsNot Nothing Then handled.Handled = True
    End Sub

    ''' <summary>One wheel message: three lines per notch, up for a positive delta. The viewer calls this too
    ''' when the wheel reaches the form while the card is up.</summary>
    Public Sub ScrollByWheel(delta As Integer)
        If delta <> 0 AndAlso barNeeded Then ScrollBy(-Math.Sign(delta) * SmallStep() * 3)
    End Sub

    ''' <summary>The keys a card that is up takes for itself while the viewer's own keys stay live for everything
    ''' else: the six that scroll. True = consumed.</summary>
    Public Function TryScrollKey(code As Keys) As Boolean
        Select Case code
            Case Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End
                If Not barNeeded Then Return True
            Case Else
                Return False
        End Select
        Select Case code
            Case Keys.Up : ScrollBy(-SmallStep())
            Case Keys.Down : ScrollBy(SmallStep())
            Case Keys.PageUp : ScrollBy(-bar.LargeChange)
            Case Keys.PageDown : ScrollBy(bar.LargeChange)
            Case Keys.Home : bar.Value = 0
            Case Keys.End : bar.Value = MaxScroll()
            Case Else : Return False
        End Select
        Return True
    End Function

    Private Function SmallStep() As Integer
        Return Math.Max(1, bar.SmallChange)
    End Function

    Private Function MaxScroll() As Integer
        Return Math.Max(0, bar.Maximum - bar.LargeChange + 1)
    End Function

    Private Sub ScrollBy(delta As Integer)
        bar.Value = Math.Max(0, Math.Min(MaxScroll(), bar.Value + delta))
    End Sub

    ' --- layout ----------------------------------------------------------------------------------

    Private Function ScaleFactor() As Single
        Return Math.Max(1, DeviceDpi) / 96.0F
    End Function

    Private Function Px(logical As Integer) As Integer
        Return CInt(Math.Round(logical * ScaleFactor()))
    End Function

    Private Sub EnsureFonts()
        If bodyFont IsNot Nothing Then Return
        Dim family As String = Localization.FontFamily()
        If String.IsNullOrEmpty(family) Then family = "Segoe UI"
        ' Sized in pixels from this window's DPI (points x DPI / 72), like the floating chrome: the viewer's
        ' layout is DPI-aware per monitor, and a point size read through a screen DC would not follow it.
        Dim perPoint As Single = Math.Max(1, DeviceDpi) / 72.0F
        bodyFont = MakeFont(family, 10.0F * perPoint, FontStyle.Regular)
        keyFont = MakeFont(family, 10.0F * perPoint, FontStyle.Bold)
        headerFont = MakeFont(family, 11.5F * perPoint, FontStyle.Bold)
        titleFont = MakeFont(family, 14.0F * perPoint, FontStyle.Bold)
    End Sub

    Private Shared Function MakeFont(family As String, size As Single, style As FontStyle) As Font
        Try
            Return New Font(family, size, style, GraphicsUnit.Pixel)
        Catch
            ' A family that cannot be built falls back to one that always exists.
            Return New Font("Segoe UI", size, style, GraphicsUnit.Pixel)
        End Try
    End Function

    Private Sub DisposeFonts()
        For Each f As Font In {bodyFont, keyFont, headerFont, titleFont}
            If f IsNot Nothing Then f.Dispose()
        Next
        bodyFont = Nothing
        keyFont = Nothing
        headerFont = Nothing
        titleFont = Nothing
    End Sub

    Private Sub Relayout()
        If groups Is Nothing OrElse ClientSize.Width <= 0 Then Return
        EnsureFonts()
        Dim barWidth As Integer = SystemInformation.VerticalScrollBarWidth
        Build(ClientSize.Width)
        barNeeded = contentHeight > ClientSize.Height
        ' The bar covers the right strip: the text is laid out beside it, not under it.
        If barNeeded Then Build(Math.Max(1, ClientSize.Width - barWidth))
        bar.Visible = barNeeded
        If barNeeded Then
            bar.Minimum = 0
            bar.Maximum = Math.Max(0, contentHeight - 1)
            bar.LargeChange = Math.Max(1, Math.Min(ClientSize.Height, contentHeight))
            bar.SmallChange = Math.Max(1, bodyFont.Height)
            If bar.Value > MaxScroll() Then bar.Value = MaxScroll()
        End If
        Invalidate()
    End Sub

    ''' <summary>Measures every item for a width and records its rectangle in content coordinates.</summary>
    Private Sub Build(width As Integer)
        items.Clear()
        Dim rtl As Boolean = Localization.IsRightToLeft()
        Dim pad As Integer = Px(16)
        Dim gap As Integer = Px(12)
        Dim inner As Integer = Math.Max(1, width - 2 * pad)
        ' At high DPI a small window cannot afford the usual minimum column widths.
        ' Keep both columns inside the viewport; wrapping supplies the extra height.
        gap = Math.Min(gap, Math.Max(0, inner - 2))
        Dim keyWidth As Integer = Math.Max(1, Math.Min(Px(280), CInt((inner - gap) * 0.36)))
        Dim descWidth As Integer = Math.Max(1, inner - keyWidth - gap)
        Dim keyLeft As Integer = If(rtl, pad + descWidth + gap, pad)
        Dim descLeft As Integer = If(rtl, pad, pad + keyWidth + gap)
        Dim y As Integer = pad

        y = AddBlock(ItemKind.Title, Localization.T("Клавиши и мышь"), titleFont, pad, y, inner, False) + Px(10)

        For Each group As HelpCardGroup In groups
            If group.Rows.Count = 0 Then Continue For
            y += Px(8)
            y = AddBlock(ItemKind.GroupHeader, Localization.T(group.Title), headerFont, pad, y, inner, False) + Px(4)
            For Each row As HelpCardRow In group.Rows
                Dim keysText As String = If(row.KeysAreWords, Localization.T(row.Keys), row.Keys)
                Dim unassigned As Boolean = keysText.Length = 0
                If unassigned Then keysText = Localization.T("Не назначено")
                Dim technical As Boolean = Not row.KeysAreWords AndAlso Not unassigned
                keysText = WrapLongWords(keysText, keyFont, keyWidth)
                Dim descText As String = WrapLongWords(Localization.T(row.Description), bodyFont, descWidth)
                Dim keysSize As Size = TextRenderer.MeasureText(keysText, keyFont, New Size(keyWidth, 0), KeyFlags(rtl, technical))
                Dim descSize As Size = TextRenderer.MeasureText(descText, bodyFont, New Size(descWidth, 0), Flags(rtl))
                Dim rowHeight As Integer = Math.Max(keysSize.Height, descSize.Height)
                items.Add(New DrawItem With {.Kind = ItemKind.KeyText, .Text = keysText, .Muted = unassigned, .TechnicalKeys = technical,
                                             .Bounds = New Rectangle(keyLeft, y, keyWidth, rowHeight)})
                items.Add(New DrawItem With {.Kind = ItemKind.DescText, .Text = descText,
                                             .Bounds = New Rectangle(descLeft, y, descWidth, rowHeight)})
                y += rowHeight + Px(4)
            Next
        Next

        y += Px(12)
        y = AddBlock(ItemKind.Footer, Localization.T("Esc или щелчок - закрыть. Клавиши можно изменить в настройках."), bodyFont, pad, y, inner, True)
        contentHeight = y + pad
    End Sub

    ''' <summary>WordBreak does not split a long key name such as Ctrl+Home. Supply line breaks
    ''' inside words that exceed the key column, so high DPI never clips their last letters.</summary>
    Private Shared Function WrapLongWords(text As String, font As Font, width As Integer) As String
        Dim result As New System.Text.StringBuilder()
        Dim word As New System.Text.StringBuilder()
        Dim elements As Globalization.TextElementEnumerator = Globalization.StringInfo.GetTextElementEnumerator(text)
        While elements.MoveNext()
            Dim element As String = elements.GetTextElement()
            If Char.IsWhiteSpace(element, 0) Then
                result.Append(word).Append(element)
                word.Clear()
                Continue While
            End If
            Dim candidate As String = word.ToString() & element
            If word.Length > 0 AndAlso TextRenderer.MeasureText(candidate, font, Size.Empty,
                TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine).Width > width Then
                result.Append(word).Append(vbLf)
                word.Clear()
            End If
            word.Append(element)
        End While
        Return result.Append(word).ToString()
    End Function

    Private Shared Function KeyFlags(rtl As Boolean, technical As Boolean) As TextFormatFlags
        Dim result As TextFormatFlags = Flags(rtl AndAlso Not technical)
        ' Key names stay left-to-right, even when their column is on the right of Arabic text.
        If rtl AndAlso technical Then result = result Or TextFormatFlags.Right
        Return result
    End Function

    Private Function AddBlock(kind As ItemKind, text As String, font As Font, left As Integer, top As Integer,
                              width As Integer, muted As Boolean) As Integer
        text = WrapLongWords(text, font, width)
        Dim size As Size = TextRenderer.MeasureText(text, font, New Size(width, 0), Flags(Localization.IsRightToLeft()))
        items.Add(New DrawItem With {.Kind = kind, .Text = text, .Muted = muted,
                                     .Bounds = New Rectangle(left, top, width, size.Height)})
        Return top + size.Height
    End Function

    Private Shared Function Flags(rtl As Boolean) As TextFormatFlags
        Dim f As TextFormatFlags = TextFormatFlags.NoPadding Or TextFormatFlags.Top Or TextFormatFlags.WordBreak
        If rtl Then
            f = f Or TextFormatFlags.RightToLeft Or TextFormatFlags.Right
        Else
            f = f Or TextFormatFlags.Left
        End If
        Return f
    End Function

    ' --- painting --------------------------------------------------------------------------------

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim palette As ThemePalette = ThemeService.Palette
        e.Graphics.Clear(palette.SurfaceWindow)
        If groups Is Nothing OrElse bodyFont Is Nothing Then Return

        Dim rtl As Boolean = Localization.IsRightToLeft()
        Dim offset As Integer = If(barNeeded, bar.Value, 0)
        Dim visibleTop As Integer = offset
        Dim visibleBottom As Integer = offset + ClientSize.Height
        For Each item As DrawItem In items
            If item.Bounds.Bottom < visibleTop OrElse item.Bounds.Top > visibleBottom Then Continue For
            Dim r As Rectangle = item.Bounds
            r.Offset(0, -offset)
            Dim font As Font = bodyFont
            Dim ink As Color = palette.TextPrimary
            Select Case item.Kind
                Case ItemKind.Title
                    font = titleFont
                Case ItemKind.GroupHeader
                    font = headerFont
                    ink = palette.Accent
                Case ItemKind.KeyText
                    font = keyFont
                    If item.Muted Then ink = palette.TextMuted
                Case ItemKind.Footer
                    ink = palette.TextMuted
            End Select
            Dim textFlags As TextFormatFlags = If(item.Kind = ItemKind.KeyText, KeyFlags(rtl, item.TechnicalKeys), Flags(rtl))
            TextRenderer.DrawText(e.Graphics, item.Text, font, r, ink, textFlags)
            If item.Kind = ItemKind.GroupHeader Then
                Using pen As New Pen(palette.Border)
                    e.Graphics.DrawLine(pen, r.Left, r.Bottom + Px(1), r.Right, r.Bottom + Px(1))
                End Using
            End If
        Next
    End Sub

    Private Sub OnThemeChanged(sender As Object, e As EventArgs)
        If IsDisposed Then Return
        Invalidate()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler ThemeService.Changed, AddressOf OnThemeChanged
            DisposeFonts()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
#End If
