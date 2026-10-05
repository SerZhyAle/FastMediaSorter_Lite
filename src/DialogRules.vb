#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Drawing

' The pure half of the dialog layer (ticket 124): everything a modal window, a question and a notification
' DECIDE, with no control, no handle and no Graphics in it - so the decisions can be held by tests and the
' WinForms glue (DialogLayer.vb, Ask_Dialog.vb, Main_Form.Notify.vb) stays thin.
'
' Whole-file "#If Not NETFRAMEWORK": the net48 fallback keeps its system boxes and its "!" prefix (the
' maintenance policy of CLAUDE.md), so nothing here exists there.

''' <summary>How much a message matters. One scale for the status line, the toast and the thumbnail panel -
''' replaces the leading "!" that used to be the only carrier (124 UX-206).</summary>
Friend Enum NotifyLevel
    Info = 0
    Warning = 1
    [Error] = 2
End Enum

''' <summary>A message split into what is said and how much it matters.</summary>
Friend Structure NotifyMessage
    Friend ReadOnly Text As String
    Friend ReadOnly Level As NotifyLevel

    Friend Sub New(text As String, level As NotifyLevel)
        Me.Text = If(text, String.Empty)
        Me.Level = level
    End Sub
End Structure

''' <summary>What a <see cref="NotifyLevel"/> turns into on screen (124 section 3.3). Pure.</summary>
Friend NotInheritable Class NotifyRules

    Private Sub New()
    End Sub

    ''' <summary>The old carrier of "this is a problem": a leading exclamation mark (with or without a space
    ''' after it - both "! Идёт.." and "!Ждите.." exist in the tree).</summary>
    Friend Const LegacyPrefix As Char = "!"c

    ''' <summary>Toast lifetime for a warning, in milliseconds (the spec asks for two to three seconds).</summary>
    Friend Const WarningToastMs As Integer = 2500

    ''' <summary>Toast lifetime for an error, in milliseconds.</summary>
    Friend Const ErrorToastMs As Integer = 3000

    ''' <summary>Splits a status text into its words and its level. A leading "!" is the legacy warning
    ''' marker and is removed (the level carries it now); every other text is information. Never throws on
    ''' Nothing.</summary>
    Friend Shared Function Classify(text As String) As NotifyMessage
        If String.IsNullOrEmpty(text) Then Return New NotifyMessage(String.Empty, NotifyLevel.Info)
        If text(0) <> LegacyPrefix Then Return New NotifyMessage(text, NotifyLevel.Info)
        Dim start As Integer = 1
        While start < text.Length AndAlso text(start) = " "c
            start += 1
        End While
        Return New NotifyMessage(text.Substring(start), NotifyLevel.Warning)
    End Function

    ''' <summary>True for a legacy "problem" text - what the thumbnail panel used to detect by looking at the
    ''' first character.</summary>
    Friend Shared Function IsWarningOrWorse(text As String) As Boolean
        Return Classify(text).Level >= NotifyLevel.Warning
    End Function

    ''' <summary>Whether a message raises a toast over the media. Only a warning or an error, and only when the
    ''' status line cannot be seen (full screen without panels, 124 Q124-3): an information toast per
    ''' navigation would defeat the very mode it speaks in.</summary>
    Friend Shared Function ShouldToast(level As NotifyLevel, statusLineVisible As Boolean) As Boolean
        Return level >= NotifyLevel.Warning AndAlso Not statusLineVisible
    End Function

    ''' <summary>An error that had to be shown as a toast also plays the system "hand" sound - the one signal
    ''' that reaches a person who is looking at a picture rather than at a status line.</summary>
    Friend Shared Function ShouldPlaySound(level As NotifyLevel, toasted As Boolean) As Boolean
        Return toasted AndAlso level = NotifyLevel.Error
    End Function

    ''' <summary>How long a toast stays: zero for information (it is never one).</summary>
    Friend Shared Function ToastMilliseconds(level As NotifyLevel) As Integer
        Select Case level
            Case NotifyLevel.Error : Return ErrorToastMs
            Case NotifyLevel.Warning : Return WarningToastMs
            Case Else : Return 0
        End Select
    End Function

    ''' <summary>The ink a message of this level is written in, on a surface of <paramref name="background"/>.
    ''' Information is the plain foreground. A warning or an error takes the application theme's own role colour
    ''' (124 Q124-2) and is blended towards the foreground until it reads at <paramref name="minimumContrast"/>:
    ''' the status line sits on a colour taken from the picture, so no fixed hue can be trusted on it.</summary>
    Friend Shared Function InkFor(level As NotifyLevel, roleColour As Color, foreground As Color, background As Color,
                                  Optional minimumContrast As Double = 4.5R) As Color
        If level = NotifyLevel.Info Then Return foreground
        Dim candidate As Color = Color.FromArgb(255, roleColour)
        Dim step_Count As Integer = 0
        While ChromeTheme.ContrastRatio(candidate, background) < minimumContrast AndAlso step_Count < 20
            candidate = ChromeTheme.Blend(candidate, foreground, 0.1F)
            step_Count += 1
        End While
        ' On a mid-grey the supplied foreground itself may not reach the floor (white on #808080 is 3.9:1): the
        ' plain black or white that reads better on this surface is the last resort.
        If ChromeTheme.ContrastRatio(candidate, background) < minimumContrast Then
            Dim black As Color = Color.Black
            Dim white As Color = Color.White
            candidate = If(ChromeTheme.ContrastRatio(black, background) >= ChromeTheme.ContrastRatio(white, background), black, white)
        End If
        Return candidate
    End Function

    ''' <summary>The ink for a message of this level on a surface of colour <paramref name="fill"/>, with the
    ''' application theme's own warning and danger roles of the light or the dark table, whichever the surface is
    ''' (a dark surface takes the lightened hues). The result is contrast-checked against the fill
    ''' (<see cref="InkFor"/>); information is the plain foreground.</summary>
    Friend Shared Function InkOnSurface(level As NotifyLevel, fill As Color, foreground As Color) As Color
        If level = NotifyLevel.Info Then Return foreground
        Dim palette As ThemePalette = ThemePalette.ForDark(ChromeTheme.IsDark(fill))
        Dim role As Color = palette.ColorOf(If(level = NotifyLevel.Error, ThemeRole.Danger, ThemeRole.Warning))
        Return InkFor(level, role, foreground, fill)
    End Function

End Class

''' <summary>The face a piece of UI the viewer draws itself (a status footer, a badge, the file HUD) is written in: the
''' script face of the interface language when it needs one (Arabic, Urdu, Hindi, Bengali, Chinese - the pinned
''' Microsoft Sans Serif and the stock "Segoe UI" the paint code asked for cover none of them well), else the face
''' the code always used (124 UX-216). Never for the CONTENT of a file - a text file's own words are not the UI language.</summary>
Friend NotInheritable Class ScriptFonts

    Private Sub New()
    End Sub

    Friend Shared Function Family(fallback As String) As String
        Dim script As String = Localization.FontFamily()
        Return If(String.IsNullOrEmpty(script), fallback, script)
    End Function

End Class

''' <summary>What the empty surface says (124 UX-209): the folder has no files, and the one key that goes on.
''' F4 opens the file chooser - choosing a file opens its whole folder - so the sentence names exactly that.</summary>
Friend NotInheritable Class EmptyStateRules

    Private Sub New()
    End Sub

    ''' <summary>The title and the hint, localized.</summary>
    Friend Shared Function Lines() As String()
        Return New String() {Localization.T("Папка пустая"), Localization.T("F4 - открыть файл из другой папки")}
    End Function

End Class

''' <summary>Where a window opened by the viewer goes. Pure arithmetic over rectangles.</summary>
Friend NotInheritable Class DialogPlacement

    Private Sub New()
    End Sub

    ''' <summary>The top-left of a window of <paramref name="size"/> centred on <paramref name="anchor"/> (the
    ''' viewer), kept entirely inside <paramref name="workArea"/> (the viewer's monitor). A window larger than
    ''' the area is pinned to its start corner.</summary>
    Friend Shared Function CenterOn(size As Size, anchor As Rectangle, workArea As Rectangle) As Point
        Dim x As Integer = anchor.Left + (anchor.Width - size.Width) \ 2
        Dim y As Integer = anchor.Top + (anchor.Height - size.Height) \ 2
        x = Math.Min(x, workArea.Right - size.Width)
        y = Math.Min(y, workArea.Bottom - size.Height)
        x = Math.Max(x, workArea.Left)
        y = Math.Max(y, workArea.Top)
        Return New Point(x, y)
    End Function

    ''' <summary>Where a toast sits over the media: centred horizontally, its bottom edge <paramref name="bottomMargin"/>
    ''' above the media area's bottom (low enough not to cover the picture's subject, high enough to clear the
    ''' status strip's reveal zone), and never outside the media area - a toast larger than the area is cut to it.</summary>
    Friend Shared Function ToastBounds(media As Rectangle, size As Size, bottomMargin As Integer) As Rectangle
        Dim w As Integer = Math.Min(size.Width, media.Width)
        Dim h As Integer = Math.Min(size.Height, media.Height)
        Dim x As Integer = media.Left + (media.Width - w) \ 2
        Dim y As Integer = Math.Max(media.Top, media.Bottom - bottomMargin - h)
        Return New Rectangle(x, y, w, h)
    End Function

    ''' <summary>The size of a window capped to its monitor's working area.</summary>
    Friend Shared Function Capped(size As Size, workArea As Rectangle) As Size
        Return New Size(Math.Min(size.Width, workArea.Width), Math.Min(size.Height, workArea.Height))
    End Function

End Class

''' <summary>Makes a hand-authored absolute layout survive a longer caption (124 UX-203): a label that
''' wrapped onto a second line needs room, so everything below it moves down by exactly that much; a button
''' whose caption outgrew it widens towards the start edge of the row, pushing its neighbours aside. Pure
''' arithmetic over rectangles, so the rules are held without a window.</summary>
Friend NotInheritable Class DialogReflow

    Private Sub New()
    End Sub

    ''' <summary>New bounds for every item after the ones that need more height have grown. An item needing
    ''' no more than it has is only moved. The shift of an item is the largest total growth along any chain of
    ''' items above it (an item is "above" when its bottom edge is at or over this one's top), so two labels
    ''' growing side by side push what is under them once, not twice.</summary>
    ''' <param name="bounds">Current bounds, in the order of the controls.</param>
    ''' <param name="neededHeights">The height each item needs, or 0 for "as it is".</param>
    ''' <param name="clientGrowth">How much taller the client area has to become so nothing falls off the bottom.</param>
    Friend Shared Function GrowVertical(bounds As IList(Of Rectangle), neededHeights As IList(Of Integer), ByRef clientGrowth As Integer) As Rectangle()
        Dim count As Integer = bounds.Count
        Dim result(count - 1) As Rectangle
        Dim delta(count - 1) As Integer
        Dim shift(count - 1) As Integer
        For i As Integer = 0 To count - 1
            Dim need As Integer = If(i < neededHeights.Count, neededHeights(i), 0)
            delta(i) = If(need > bounds(i).Height, need - bounds(i).Height, 0)
        Next

        Dim order As New List(Of Integer)()
        For i As Integer = 0 To count - 1
            order.Add(i)
        Next
        order.Sort(Function(a, b) bounds(a).Top.CompareTo(bounds(b).Top))

        For Each j As Integer In order
            Dim best As Integer = 0
            For Each i As Integer In order
                If i = j Then Continue For
                If bounds(i).Top >= bounds(j).Top Then Continue For
                If bounds(i).Bottom <= bounds(j).Top Then best = Math.Max(best, shift(i) + delta(i))
            Next
            shift(j) = best
        Next

        Dim oldBottom As Integer = Integer.MinValue
        Dim newBottom As Integer = Integer.MinValue
        For i As Integer = 0 To count - 1
            Dim r As Rectangle = bounds(i)
            result(i) = New Rectangle(r.Left, r.Top + shift(i), r.Width, r.Height + delta(i))
            oldBottom = Math.Max(oldBottom, r.Bottom)
            newBottom = Math.Max(newBottom, result(i).Bottom)
        Next
        clientGrowth = If(count = 0, 0, Math.Max(0, newBottom - oldBottom))
        Return result
    End Function

    ''' <summary>Widens every item that needs more width towards the left (the start edge of a row of
    ''' right-aligned buttons keeps its right edge), then moves any neighbour in the same row that the wider one
    ''' now covers further left by the overlap, cascading, with <paramref name="gap"/> between them. Items on
    ''' other rows are untouched.</summary>
    ''' <param name="neededWidths">The width each item needs, or 0 for "as it is".</param>
    Friend Shared Function GrowTowardsStart(bounds As IList(Of Rectangle), neededWidths As IList(Of Integer), gap As Integer) As Rectangle()
        Dim count As Integer = bounds.Count
        Dim result(count - 1) As Rectangle
        For i As Integer = 0 To count - 1
            Dim r As Rectangle = bounds(i)
            Dim need As Integer = If(i < neededWidths.Count, neededWidths(i), 0)
            If need > r.Width Then
                result(i) = New Rectangle(r.Right - need, r.Top, need, r.Height)
            Else
                result(i) = r
            End If
        Next
        ' Right to left: whoever is covered by the item to its right gives way.
        Dim order As New List(Of Integer)()
        For i As Integer = 0 To count - 1
            order.Add(i)
        Next
        order.Sort(Function(a, b) result(b).Right.CompareTo(result(a).Right))
        For Each i As Integer In order
            For Each j As Integer In order
                If i = j Then Continue For
                If result(j).Right > result(i).Right Then Continue For
                If result(j).Bottom <= result(i).Top OrElse result(j).Top >= result(i).Bottom Then Continue For
                If result(j).Right + gap > result(i).Left AndAlso result(j).Left < result(i).Left Then
                    Dim overlap As Integer = result(j).Right + gap - result(i).Left
                    result(j) = New Rectangle(result(j).Left - overlap, result(j).Top, result(j).Width, result(j).Height)
                End If
            Next
        Next
        Return result
    End Function

    ''' <summary>The mirror of <see cref="GrowTowardsStart"/> for a row that starts at the left edge: an item that
    ''' needs more width keeps its left edge and grows to the right, and the neighbours it now covers move right.
    ''' Done by reflecting the row, so the two cannot disagree.</summary>
    Friend Shared Function GrowTowardsEnd(bounds As IList(Of Rectangle), neededWidths As IList(Of Integer), gap As Integer) As Rectangle()
        Dim mirrored As New List(Of Rectangle)()
        For Each r As Rectangle In bounds
            mirrored.Add(New Rectangle(-r.Right, r.Top, r.Width, r.Height))
        Next
        Dim grown As Rectangle() = GrowTowardsStart(mirrored, neededWidths, gap)
        Dim result(grown.Length - 1) As Rectangle
        For i As Integer = 0 To grown.Length - 1
            result(i) = New Rectangle(-grown(i).Right, grown(i).Top, grown(i).Width, grown(i).Height)
        Next
        Return result
    End Function

End Class

''' <summary>The Settings window's header: one row of square icon tools (search, expand all, collapse all)
''' on the END side of the header and the title block in the room that is left. Pure arithmetic, held by tests.</summary>
Friend NotInheritable Class SettingsHeaderRules

    Private Sub New()
    End Sub

    ''' <summary>Left edge of each of <paramref name="count"/> tools, in reading order (the first tool is the one
    ''' the eye meets first: leftmost in a left-to-right window, rightmost in a right-to-left one). The group sits
    ''' <paramref name="edge"/> from the END side - right, or left under RTL - and never starts before 0.</summary>
    Friend Shared Function ToolLefts(headerWidth As Integer, count As Integer, side As Integer, gap As Integer, edge As Integer, rtl As Boolean) As Integer()
        Dim lefts(Math.Max(0, count) - 1) As Integer
        Dim groupWidth As Integer = ToolsWidth(count, side, gap)
        Dim groupLeft As Integer = If(rtl, edge, Math.Max(0, headerWidth - groupWidth - edge))
        For index As Integer = 0 To count - 1
            lefts(index) = groupLeft + If(rtl, groupWidth - side - index * (side + gap), index * (side + gap))
        Next
        Return lefts
    End Function

    Friend Shared Function ToolsWidth(count As Integer, side As Integer, gap As Integer) As Integer
        Return Math.Max(0, count) * side + Math.Max(0, count - 1) * gap
    End Function

    ''' <summary>The horizontal extent (left, width) the title and subtitle may use: from the start margin to the
    ''' tools, or - under RTL - from the tools to the right margin. At least one pixel wide whatever the window.</summary>
    Friend Shared Function TitleSpan(headerWidth As Integer, count As Integer, side As Integer, gap As Integer, edge As Integer,
                                     startMargin As Integer, toolGap As Integer, rtl As Boolean) As Integer()
        Dim groupWidth As Integer = ToolsWidth(count, side, gap)
        Dim groupLeft As Integer = If(rtl, edge, Math.Max(0, headerWidth - groupWidth - edge))
        Dim left As Integer = If(rtl, groupLeft + groupWidth + toolGap, startMargin)
        Dim right As Integer = If(rtl, headerWidth - startMargin, groupLeft - toolGap)
        Return New Integer() {left, Math.Max(1, right - left)}
    End Function

End Class
#End If
