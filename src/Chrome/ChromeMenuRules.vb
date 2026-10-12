#If Not NETFRAMEWORK Then
Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

''' <summary>What a key means to an open popup menu (125 UX-302, UX-322, UX-323).</summary>
Friend Enum MenuKeyAction
    ''' <summary>Not the menu's: the key goes on to whatever had it (the path editor, for the suggestion list).</summary>
    PassThrough
    ''' <summary>Eaten, nothing happens - a viewer key must not run while a menu is browsed.</summary>
    Swallow
    ''' <summary>Close this menu (the only level of a root menu).</summary>
    Close
    ''' <summary>F10: close, and let the chrome eat the matching key-up.</summary>
    CloseAndEatF10
    ''' <summary>Left / Esc inside a submenu: back to its parent, which stays open.</summary>
    BackOneLevel
    FocusNext
    FocusPrevious
    FocusFirst
    FocusLast
    ''' <summary>Enter / Space on the focused row.</summary>
    Activate
    ''' <summary>Right (Left in a right-to-left UI) / Enter on a row that has a submenu.</summary>
    OpenSubmenu
    ''' <summary>A digit that names a row: run it. <see cref="MenuKeyResult.Index"/> is the row.</summary>
    ByAccelerator
    ''' <summary>A letter that jumps the focus to the next row with that initial.</summary>
    TypeAhead
End Enum

Friend Structure MenuKeyResult
    Public Action As MenuKeyAction
    Public Index As Integer
    Public Sub New(action As MenuKeyAction, Optional index As Integer = -1)
        Me.Action = action
        Me.Index = index
    End Sub
End Structure

''' <summary>The facts about one open menu that decide what a key does. Arrays run in row order and
''' have one entry per visible row (separators included, with accelerator 0 and not enabled).</summary>
Friend Structure MenuKeyContext
    ''' <summary>The path-suggestion list: it is a helper of a text box, so typing belongs to the box.</summary>
    Public IsSuggestionList As Boolean
    Public IsSubmenu As Boolean
    Public RightToLeft As Boolean
    ''' <summary>A row has the keyboard ring now (an arrow key put it there, or the menu was opened from the keyboard).</summary>
    Public HasFocusedRow As Boolean
    Public FocusedRowOpensSubmenu As Boolean
    Public FocusedIndex As Integer
    ''' <summary>The digit or letter a row answers to directly, or ChrW(0).</summary>
    Public Accelerators As Char()
    ''' <summary>The first letter of each row's caption, upper-cased (type-ahead), or ChrW(0) for none.</summary>
    Public Initials As Char()
    Public Enabled As Boolean()
    ''' <summary>The letter the pressed key types on the ACTIVE keyboard layout (a Cyrillic letter on a Russian layout),
    ''' or ChrW(0) when the caller does not know. Type-ahead matches on it, so a menu in Russian can be
    ''' typed at; without it only Latin A-Z jump.</summary>
    Public TypedChar As Char
End Structure

''' <summary>
''' The pure half of the painted popup menu (125). Key routing, the shape of a row's hint, where a
''' submenu opens, how rows stack with separators and whether a scroll cue is due - all arithmetic
''' on plain values, so it can be tested without a window. The WinForms glue in
''' <c>ChromeMenu.vb</c> and <c>Main_Form.ChromeMenus.vb</c> only asks and obeys.
''' </summary>
Friend NotInheritable Class ChromeMenuRules

    Private Sub New()
    End Sub

    ' --- keys ----------------------------------------------------------------------------------

    Private Shared Function IsDigitKey(code As Keys, ByRef digit As Char) As Boolean
        If code >= Keys.D0 AndAlso code <= Keys.D9 Then
            digit = ChrW(AscW("0"c) + (code - Keys.D0))
            Return True
        End If
        If code >= Keys.NumPad0 AndAlso code <= Keys.NumPad9 Then
            digit = ChrW(AscW("0"c) + (code - Keys.NumPad0))
            Return True
        End If
        Return False
    End Function

    Private Shared Function IsLetterKey(code As Keys, ByRef letter As Char) As Boolean
        If code >= Keys.A AndAlso code <= Keys.Z Then
            letter = ChrW(AscW("A"c) + (code - Keys.A))
            Return True
        End If
        Return False
    End Function

    ''' <summary>What the menu does with a key. A Ctrl or Alt chord is Windows' or the viewer's, never the
    ''' menu's. In the suggestion list every key that edits text (printable keys, Backspace, Delete, the
    ''' caret keys, Space and Enter) stays the text box's until an arrow key has put a row in focus.</summary>
    Public Shared Function ResolveKey(keyData As Keys, context As MenuKeyContext) As MenuKeyResult
        Dim code As Keys = keyData And Keys.KeyCode
        If (keyData And (Keys.Control Or Keys.Alt)) <> Keys.None Then Return New MenuKeyResult(MenuKeyAction.PassThrough)
        Dim shift As Boolean = (keyData And Keys.Shift) <> Keys.None
        Dim editorOwnsKey As Boolean = context.IsSuggestionList AndAlso Not context.HasFocusedRow
        Dim opens As Keys = If(context.RightToLeft, Keys.Left, Keys.Right)
        Dim back As Keys = If(context.RightToLeft, Keys.Right, Keys.Left)

        Select Case code
            Case Keys.Escape
                Return New MenuKeyResult(If(context.IsSubmenu, MenuKeyAction.BackOneLevel, MenuKeyAction.Close))
            Case Keys.F10
                Return New MenuKeyResult(MenuKeyAction.CloseAndEatF10)
            Case Keys.Down
                Return New MenuKeyResult(If(context.HasFocusedRow, MenuKeyAction.FocusNext, MenuKeyAction.FocusFirst))
            Case Keys.Up
                Return New MenuKeyResult(If(context.HasFocusedRow, MenuKeyAction.FocusPrevious, MenuKeyAction.FocusLast))
            Case Keys.Tab
                If shift Then Return New MenuKeyResult(If(context.HasFocusedRow, MenuKeyAction.FocusPrevious, MenuKeyAction.FocusLast))
                Return New MenuKeyResult(If(context.HasFocusedRow, MenuKeyAction.FocusNext, MenuKeyAction.FocusFirst))
            Case Keys.Home
                If editorOwnsKey Then Return New MenuKeyResult(MenuKeyAction.PassThrough)
                Return New MenuKeyResult(MenuKeyAction.FocusFirst)
            Case Keys.End
                If editorOwnsKey Then Return New MenuKeyResult(MenuKeyAction.PassThrough)
                Return New MenuKeyResult(MenuKeyAction.FocusLast)
            Case Keys.Enter, Keys.Space
                If editorOwnsKey Then Return New MenuKeyResult(MenuKeyAction.PassThrough)
                If Not context.HasFocusedRow Then Return New MenuKeyResult(MenuKeyAction.Swallow)
                If context.FocusedRowOpensSubmenu Then Return New MenuKeyResult(MenuKeyAction.OpenSubmenu)
                Return New MenuKeyResult(MenuKeyAction.Activate)
        End Select

        If code = opens Then
            If context.HasFocusedRow AndAlso context.FocusedRowOpensSubmenu Then Return New MenuKeyResult(MenuKeyAction.OpenSubmenu)
            Return New MenuKeyResult(If(editorOwnsKey OrElse (context.IsSuggestionList AndAlso Not context.FocusedRowOpensSubmenu), MenuKeyAction.PassThrough, MenuKeyAction.Swallow))
        End If
        If code = back Then
            If context.IsSubmenu Then Return New MenuKeyResult(MenuKeyAction.BackOneLevel)
            Return New MenuKeyResult(If(context.IsSuggestionList, MenuKeyAction.PassThrough, MenuKeyAction.Swallow))
        End If

        If context.IsSuggestionList Then Return New MenuKeyResult(MenuKeyAction.PassThrough)

        Dim digit As Char
        If IsDigitKey(code, digit) AndAlso context.Accelerators IsNot Nothing Then
            For index As Integer = 0 To context.Accelerators.Length - 1
                If context.Accelerators(index) = digit AndAlso context.Enabled IsNot Nothing AndAlso index < context.Enabled.Length AndAlso context.Enabled(index) Then
                    Return New MenuKeyResult(MenuKeyAction.ByAccelerator, index)
                End If
            Next
            Return New MenuKeyResult(MenuKeyAction.Swallow)
        End If

        ' The letter the key types on the active layout first; the Latin letter of the key as the fallback (a menu
        ' whose captions start with a Latin letter is still reachable on a Cyrillic layout).
        If context.Initials IsNot Nothing Then
            If context.TypedChar <> ChrW(0) AndAlso Char.IsLetter(context.TypedChar) Then
                Dim typedAt As Integer = NextWithInitial(context.Initials, context.Enabled, context.TypedChar, context.FocusedIndex)
                If typedAt >= 0 Then Return New MenuKeyResult(MenuKeyAction.TypeAhead, typedAt)
            End If
            Dim letter As Char
            If IsLetterKey(code, letter) Then
                Dim found As Integer = NextWithInitial(context.Initials, context.Enabled, letter, context.FocusedIndex)
                If found >= 0 Then Return New MenuKeyResult(MenuKeyAction.TypeAhead, found)
            End If
        End If
        Return New MenuKeyResult(MenuKeyAction.Swallow)
    End Function

    ''' <summary>The next enabled row after <paramref name="after"/> (wrapping) whose initial is
    ''' <paramref name="letter"/>; -1 when none. Pressing the same letter again walks on to the next match.</summary>
    Public Shared Function NextWithInitial(initials As Char(), enabled As Boolean(), letter As Char, after As Integer) As Integer
        If initials Is Nothing OrElse initials.Length = 0 Then Return -1
        Dim upper As Char = Char.ToUpperInvariant(letter)
        For offset As Integer = 1 To initials.Length
            Dim index As Integer = ((after + offset) Mod initials.Length + initials.Length) Mod initials.Length
            If initials(index) = upper AndAlso (enabled Is Nothing OrElse (index < enabled.Length AndAlso enabled(index))) Then Return index
        Next
        Return -1
    End Function

    ''' <summary>The initial type-ahead matches a caption on: its first letter or digit, upper-cased.</summary>
    Public Shared Function InitialOf(caption As String) As Char
        If String.IsNullOrEmpty(caption) Then Return ChrW(0)
        For Each c As Char In caption
            If Char.IsLetterOrDigit(c) Then Return Char.ToUpperInvariant(c)
        Next
        Return ChrW(0)
    End Function

    ' --- captions and hints --------------------------------------------------------------------

    Private Shared ReadOnly HintSuffix As New Regex("^(?<caption>.*?)\s*[\(（](?<hint>[^\(\)（）]{1,24})[\)）]\s*$", RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    ''' <summary>Splits a menu caption that carries its hotkey in brackets ("Удалить (Del)") into the words and
    ''' the bracketed text. A caption with no short bracketed tail comes back whole with an empty hint. The
    ''' hint is dropped by the caller when the live key map knows better (125 UX-325).</summary>
    Public Shared Function SplitHintSuffix(caption As String, ByRef hint As String) As String
        hint = ""
        If String.IsNullOrEmpty(caption) Then Return If(caption, "")
        Dim match As Match = HintSuffix.Match(caption)
        If Not match.Success OrElse match.Groups("caption").Value.Length = 0 Then Return caption
        hint = match.Groups("hint").Value
        Return match.Groups("caption").Value
    End Function

    ''' <summary>The key a menu row shows for an action: the effective combination of the live map, the
    ''' fallback (a key outside the catalogue, e.g. F11) when the action is not in it, nothing when the user
    ''' left the action with no shortcut. "Num +" is shown as is - it is the dialog's own spelling.</summary>
    Public Shared Function HintFor(map As Dictionary(Of String, String), actionId As String, fallback As String) As String
        If String.IsNullOrEmpty(actionId) Then Return If(fallback, "")
        Dim action As HotkeyAction = CustomHotkeys.Find(actionId)
        If action Is Nothing Then Return If(fallback, "")
        Return PrettyCombo(CustomHotkeys.EffectiveCombo(map, action))
    End Function

    ''' <summary>A combination as a menu shows it: the symbol keys by their symbol (the profile spells them
    ''' "OemPeriod"), everything else as the shortcuts dialog spells it.</summary>
    Public Shared Function PrettyCombo(combo As String) As String
        If String.IsNullOrEmpty(combo) Then Return ""
        Dim prefix As String = ""
        Dim rest As String = combo
        For Each modifier As String In New String() {"Ctrl+", "Alt+", "Shift+"}
            If rest.StartsWith(modifier, StringComparison.Ordinal) AndAlso rest.Length > modifier.Length Then
                prefix &= modifier
                rest = rest.Substring(modifier.Length)
            End If
        Next
        Select Case rest
            Case "OemPeriod" : rest = "."
            Case "OemOpenBrackets" : rest = "["
            Case "OemCloseBrackets" : rest = "]"
            Case "OemPipe" : rest = "\"
        End Select
        Return prefix & rest
    End Function

    ' --- structure -----------------------------------------------------------------------------

    ''' <summary>The rows without the separators that divide nothing: none first or last, never two in a row.
    ''' Builders put a separator between every group and leave it to this to drop the ones a hidden group
    ''' (an archive's file operations, a stream's file row) would leave stranded.</summary>
    Public Shared Function TidySeparators(Of T)(rows As IList(Of T), isSeparator As Func(Of T, Boolean)) As List(Of T)
        Dim result As New List(Of T)()
        For Each row As T In rows
            If isSeparator(row) Then
                If result.Count = 0 OrElse isSeparator(result(result.Count - 1)) Then Continue For
            End If
            result.Add(row)
        Next
        While result.Count > 0 AndAlso isSeparator(result(result.Count - 1))
            result.RemoveAt(result.Count - 1)
        End While
        Return result
    End Function

    ' --- geometry ------------------------------------------------------------------------------

    ''' <summary>The height of a separator row: a hairline with breathing room, never a full item.</summary>
    Public Shared Function SeparatorHeight(gap As Integer) As Integer
        Return Math.Max(3, gap * 2 + 1)
    End Function

    ''' <summary>Rows stacked top to bottom: where each starts and how tall the stack is, padding included.</summary>
    Public Shared Function StackRows(heights As IList(Of Integer), padding As Integer, ByRef total As Integer) As Integer()
        Dim tops(heights.Count - 1) As Integer
        Dim y As Integer = padding
        For index As Integer = 0 To heights.Count - 1
            tops(index) = y
            y += heights(index)
        Next
        total = y + padding
        Return tops
    End Function

    ''' <summary>Where a submenu opens: beside its parent row, overlapping the parent's edge by
    ''' <paramref name="overlap"/>, on the side the reading direction opens to and flipped when it would
    ''' leave the work area; its first row level with <paramref name="rowTop"/>; kept inside the work area.
    ''' All in screen coordinates.</summary>
    Public Shared Function SubmenuOrigin(parent As Rectangle, rowTop As Integer, childSize As Size,
                                         work As Rectangle, rightToLeft As Boolean, overlap As Integer) As Point
        Dim x As Integer
        If Not rightToLeft Then
            x = parent.Right - overlap
            If x + childSize.Width > work.Right Then x = parent.Left - childSize.Width + overlap
        Else
            x = parent.Left - childSize.Width + overlap
            If x < work.Left Then x = parent.Right - overlap
        End If
        x = Math.Max(work.Left, Math.Min(x, work.Right - childSize.Width))
        Dim y As Integer = Math.Min(rowTop, work.Bottom - childSize.Height)
        y = Math.Max(work.Top, y)
        Return New Point(x, y)
    End Function

    ''' <summary>Where a popup opened from a control goes: at <paramref name="location"/> when it fits below,
    ''' otherwise ABOVE the control (its bottom edge on <paramref name="anchorTop"/>) instead of sliding up over
    ''' the very button that opened it - the transport bar sits at the foot of the window, and a tall menu
    ''' clamped to the work area covered the bar (121 Ф-2). <c>Integer.MinValue</c> = no anchor, the plain clamp.
    ''' Always inside the work area. Screen coordinates.</summary>
    Public Shared Function PopupOrigin(location As Point, size As Size, work As Rectangle, anchorTop As Integer) As Point
        Dim y As Integer = location.Y
        If anchorTop <> Integer.MinValue AndAlso location.Y + size.Height > work.Bottom Then y = anchorTop - size.Height
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height))
        Dim x As Integer = Math.Max(work.Left, Math.Min(location.X, work.Right - size.Width))
        Return New Point(x, y)
    End Function

    ''' <summary>Whether content is hidden above and below the visible part of a scrolling menu - the two
    ''' scroll cues (125 UX-329).</summary>
    Public Shared Sub ScrollCues(scrollOffset As Integer, contentHeight As Integer, viewHeight As Integer,
                                 ByRef moreAbove As Boolean, ByRef moreBelow As Boolean)
        moreAbove = scrollOffset > 0
        moreBelow = contentHeight > viewHeight AndAlso scrollOffset + viewHeight < contentHeight
    End Sub

    ' --- small rules ---------------------------------------------------------------------------

    ''' <summary>Whether a row of the "»" overflow list is something to press: a counter or a zoom label
    ''' (<see cref="ChromeItemKind.Text"/>) does nothing and has no place in a menu (125 UX-310).</summary>
    Public Shared Function OverflowEntryIsActionable(kind As ChromeItemKind) As Boolean
        Return kind <> ChromeItemKind.Text
    End Function

    ''' <summary>The Windows signature in <c>GetMessageExtraInfo()</c> of a mouse message that a touch screen
    ''' or a pen generated (MI_WP_SIGNATURE: 0xFF515700 under the 0xFFFFFF00 mask; bit 0x80 then says touch rather than pen). A real mouse carries none
    ''' (125 Q125-1: the long press is for touch and pen only).</summary>
    Public Shared Function IsTouchOrPenExtraInfo(extraInfo As Long) As Boolean
        Return (extraInfo And &HFFFFFF00L) = &HFF515700L
    End Function

    ''' <summary>Whether a press on a panel's free background is the second press of a double click
    ''' (188 §5: double-clicking the window-controls grip toggles maximize). Within the system's
    ''' double-click time of the previous press and inside its rectangle. Pure; the ticks are
    ''' <c>Environment.TickCount</c>-style values and may wrap, so the distance is the raw
    ''' two's-complement difference - negative means "before the previous press", never a match.</summary>
    Public Shared Function IsDoubleClickPress(previousTickMs As Integer, currentTickMs As Integer,
                                              deltaX As Integer, deltaY As Integer) As Boolean
        If previousTickMs = Integer.MinValue Then Return False
        Dim elapsed As Integer = currentTickMs - previousTickMs
        If elapsed < 0 OrElse elapsed > SystemInformation.DoubleClickTime Then Return False
        Dim size As Size = SystemInformation.DoubleClickSize
        Return Math.Abs(deltaX) <= size.Width AndAlso Math.Abs(deltaY) <= size.Height
    End Function

    ''' <summary>A panel reacts to a text change of the counter, the zoom label or the status line only if it
    ''' is the status panel: the toolbar rows are summoned by the pointer, a pin or F10, never by paging
    ''' through files (125 UX-309).</summary>
    Public Shared Function PanelRevealsOnTextChange(panelName As String) As Boolean
        Return String.Equals(panelName, "chrome_Status", StringComparison.Ordinal)
    End Function

    ''' <summary>The sort button's words: the localization key of the order's name and the arrow for its
    ''' direction (ChrW(0) = none). Index order is the combo's: abc, xyz, rnd, >size, <size, >time, <time,
    ''' <0123, >3210 - the stored index never changes (125 UX-304, Q125-3).</summary>
    Public Shared Function SortOrderLabel(index As Integer, ByRef arrow As Char) As String
        arrow = ChrW(0)
        Select Case index
            Case 0 : arrow = ChrW(&H2191) : Return "Имя"
            Case 1 : arrow = ChrW(&H2193) : Return "Имя"
            Case 2 : Return "Случайно"
            Case 3 : arrow = ChrW(&H2193) : Return "Размер"
            Case 4 : arrow = ChrW(&H2191) : Return "Размер"
            Case 5 : arrow = ChrW(&H2193) : Return "Дата"
            Case 6 : arrow = ChrW(&H2191) : Return "Дата"
            Case 7 : arrow = ChrW(&H2191) : Return "Имя по числам"
            Case 8 : arrow = ChrW(&H2193) : Return "Имя по числам"
            Case Else : Return ""
        End Select
    End Function

End Class
#End If
