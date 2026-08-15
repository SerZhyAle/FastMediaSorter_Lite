#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms

''' <summary>One remappable viewer action: the id stored in the profile, the Russian
''' caption that is also its localization key, and the combination it answers to out of
''' the box.</summary>
Public NotInheritable Class HotkeyAction

    Public Sub New(id As String, caption As String, defaultCombo As String)
        Me.Id = id
        Me.Caption = caption
        Me.DefaultCombo = defaultCombo
    End Sub

    Public ReadOnly Id As String
    Public ReadOnly Caption As String
    Public ReadOnly DefaultCombo As String
End Class

''' <summary>
''' Custom keyboard shortcuts - §3.5 of SPECIFICATION_SETTINGS_EXPANSION, stored in the
''' profile as the `CustomHotkeys` JSON object { actionId: "Ctrl+Shift+.." }.
'''
''' THE MAP ONLY EVER HOLDS OVERRIDES. An action the user has not touched is simply not
''' in it, and the viewer's historical key handling answers for it unchanged - which is
''' what lets a feature that can rebind almost every key ship without rewriting the
''' branch that has dispatched them for years. Two questions are enough to make an
''' override real:
'''   - OwnerOfOverride: does this combination now belong to a moved action?
'''   - IsRetiredDefault: is this combination the OLD home of a moved action, and has
'''     nobody taken it over? Then it must do nothing, or the action would answer to
'''     both its old key and its new one and the remapping would be a lie.
''' Everything else falls through, so an empty map changes nothing at all.
'''
''' Pure and UI-free on purpose: the parsing, the reserved list and the conflict rules
''' are exactly what is worth proving in a test, and none of it needs a window.
''' </summary>
Public NotInheritable Class CustomHotkeys

    Private Sub New()
    End Sub

    ''' <summary>An action the user has deliberately left without a shortcut. It is a
    ''' third state, not the absence of an entry: "no entry" means the factory key still
    ''' works, this means it does not. §3.5's conflict dialog offers it as the answer to
    ''' "somebody else already has that combination".</summary>
    Public Const NoShortcut As String = "-"

    ''' <summary>Every action the shortcuts dialog offers, in the order it shows them.
    ''' Recipient slots keep their own identity per §3.5 - "0" is slot 10, the mapping
    ''' the profile has always used for MoveOn0.</summary>
    Public Shared ReadOnly Catalog As HotkeyAction() = BuildCatalog()

    Private Shared Function BuildCatalog() As HotkeyAction()
        Dim actions As New List(Of HotkeyAction) From {
            New HotkeyAction("nextFile", "Следующий файл", "Right"),
            New HotkeyAction("prevFile", "Предыдущий файл", "Left"),
            New HotkeyAction("randomFile", "Случайный файл", "Y"),
            New HotkeyAction("slideshow", "Слайд-шоу", "S"),
            New HotkeyAction("randomSlideshow", "Случайное слайд-шоу", "I"),
            New HotkeyAction("firstFile", "Первый файл", "Home"),
            New HotkeyAction("lastFile", "Последний файл", "End"),
            New HotkeyAction("back10", "Назад на 10 файлов", "Up"),
            New HotkeyAction("forward10", "Вперёд на 10 файлов", "Down"),
            New HotkeyAction("back100", "Назад на 100 файлов", "Shift+PageUp"),
            New HotkeyAction("forward100", "Вперёд на 100 файлов", "Shift+PageDown"),
            New HotkeyAction("chooseFile", "Выбрать файл", "F4"),
            New HotkeyAction("jumpToNumber", "Перейти к номеру файла", "N"),
            New HotkeyAction("renameFile", "Переименовать файл", "F6"),
            New HotkeyAction("deleteFile", "Удалить файл", "Delete"),
            New HotkeyAction("deletePermanent", "Удалить безвозвратно", "Shift+Delete"),
            New HotkeyAction("undo", "Отменить действие", "U"),
            New HotkeyAction("rotateCw", "Повернуть по часовой стрелке", "R"),
            New HotkeyAction("rotateCcw", "Повернуть против часовой стрелки", "Shift+R"),
            New HotkeyAction("ocrTranslate", "Распознать и перевести", "T"),
            New HotkeyAction("ocrAuto", "Автоматическое распознавание", "Shift+T"),
            New HotkeyAction("fullScreen", "Полноэкранный режим", "F7"),
            New HotkeyAction("settings", "Настройки", "F2"),
            New HotkeyAction("imagePanel", "Панель изображений", "F3"),
            New HotkeyAction("help", "Справка", "F1"),
            New HotkeyAction("zoomIn", "Увеличить масштаб", "Num +"),
            New HotkeyAction("zoomOut", "Уменьшить масштаб", "Num -"),
            New HotkeyAction("zoomFit", "Вписать в окно", "Num /"),
            New HotkeyAction("zoomActual", "Реальный размер", "Num *")}

        ' Slots 1..9 answer to their own digit, slot 10 to "0" - the frozen MoveOn0 rule.
        For slot As Integer = 1 To 10
            Dim digit As String = If(slot = 10, "0", slot.ToString(Globalization.CultureInfo.InvariantCulture))
            actions.Add(New HotkeyAction(RecipientActionId(slot), "", digit))
        Next
        Return actions.ToArray()
    End Function

    Public Shared Function RecipientActionId(slot As Integer) As String
        Return "recipient" & slot.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The slot a recipient action id names, or 0 when it names something else.</summary>
    Public Shared Function RecipientSlotOf(actionId As String) As Integer
        If actionId Is Nothing OrElse Not actionId.StartsWith("recipient", StringComparison.Ordinal) Then Return 0
        Dim slot As Integer
        If Not Integer.TryParse(actionId.Substring("recipient".Length), slot) Then Return 0
        If slot < 1 OrElse slot > 10 Then Return 0
        Return slot
    End Function

    Public Shared Function Find(actionId As String) As HotkeyAction
        Return Catalog.FirstOrDefault(Function(action) String.Equals(action.Id, actionId, StringComparison.Ordinal))
    End Function

    ' ------------------------------------------------------------------ format ----

    ''' <summary>
    ''' A key combination as the profile and the dialog spell it: "Ctrl+Shift+F5".
    ''' Returns "" for anything that is not a usable shortcut - a bare modifier, an
    ''' unknown key, or one of the reserved combinations of §3.5.
    ''' </summary>
    Public Shared Function Format(keyData As Keys) As String
        Dim code As Keys = keyData And Keys.KeyCode
        If Not IsAssignableKey(code) Then Return String.Empty
        If IsReserved(keyData) Then Return String.Empty

        Dim text As New StringBuilder()
        If (keyData And Keys.Control) = Keys.Control Then text.Append("Ctrl+")
        If (keyData And Keys.Alt) = Keys.Alt Then text.Append("Alt+")
        If (keyData And Keys.Shift) = Keys.Shift Then text.Append("Shift+")
        text.Append(KeyName(code))
        Return text.ToString()
    End Function

    ''' <summary>The reverse, tolerant of case and spacing. Keys.None when the text does
    ''' not name a usable combination - a hand-edited profile is data, not a crash.</summary>
    Public Shared Function Parse(text As String) As Keys
        If String.IsNullOrWhiteSpace(text) Then Return Keys.None

        ' Modifiers are stripped from the front one at a time rather than the string
        ' being split on "+", because the KEY NAME can contain one: "Num +" splits into
        ' "Num " and "", and neither half names anything.
        Dim modifiers As Keys = Keys.None
        Dim rest As String = text.Trim()
        Dim stripped As Boolean = True
        While stripped
            stripped = False
            If TryStripModifier(rest, "ctrl") OrElse TryStripModifier(rest, "control") Then
                modifiers = modifiers Or Keys.Control
                stripped = True
            ElseIf TryStripModifier(rest, "alt") Then
                modifiers = modifiers Or Keys.Alt
                stripped = True
            ElseIf TryStripModifier(rest, "shift") Then
                modifiers = modifiers Or Keys.Shift
                stripped = True
            End If
        End While

        Dim code As Keys = KeyFromName(rest)
        If Not IsAssignableKey(code) Then Return Keys.None
        Dim result As Keys = modifiers Or code
        If IsReserved(result) Then Return Keys.None
        Return result
    End Function

    ''' <summary>Takes "ctrl" (and the "+" after it) off the front, or leaves the text
    ''' alone and returns False. "Num +" is untouched: "num" is not a modifier.</summary>
    Private Shared Function TryStripModifier(ByRef text As String, name As String) As Boolean
        Dim trimmed As String = text.TrimStart()
        If Not trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase) Then Return False

        Dim tail As String = trimmed.Substring(name.Length).TrimStart()
        If Not tail.StartsWith("+", StringComparison.Ordinal) Then Return False
        text = tail.Substring(1)
        Return True
    End Function

    ''' <summary>Same combination, spelled the one way this class spells it. "" when the
    ''' text names nothing usable.</summary>
    Public Shared Function Canonical(text As String) As String
        Return Format(Parse(text))
    End Function

    ''' <summary>
    ''' The combinations §3.5 keeps out of the user's hands: the ones Windows owns, and
    ''' the two the viewer itself must always answer to - F11 leaves and enters the
    ''' panel-less full screen, Esc gets out of it.
    ''' </summary>
    Public Shared Function IsReserved(keyData As Keys) As Boolean
        Dim code As Keys = keyData And Keys.KeyCode
        Select Case code
            Case Keys.F11, Keys.Escape, Keys.LWin, Keys.RWin, Keys.Apps : Return True
            Case Keys.Tab
                If (keyData And Keys.Alt) = Keys.Alt Then Return True
            Case Keys.F4
                If (keyData And Keys.Alt) = Keys.Alt Then Return True
            Case Keys.Delete
                If (keyData And Keys.Control) = Keys.Control AndAlso (keyData And Keys.Alt) = Keys.Alt Then Return True
        End Select
        Return False
    End Function

    Private Shared Function IsAssignableKey(code As Keys) As Boolean
        Select Case code
            Case Keys.None, Keys.ControlKey, Keys.ShiftKey, Keys.Menu,
                 Keys.LControlKey, Keys.RControlKey, Keys.LShiftKey, Keys.RShiftKey,
                 Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin
                Return False
        End Select
        Return KeyName(code).Length > 0
    End Function

    ''' <summary>Friendly names for the keys whose enum spelling would be unreadable in a
    ''' table ("D5", "Oemplus", "Add").</summary>
    Private Shared Function KeyName(code As Keys) As String
        Select Case code
            Case Keys.D0 To Keys.D9 : Return ChrW(AscW("0"c) + (code - Keys.D0))
            Case Keys.NumPad0 To Keys.NumPad9 : Return "Num " & ChrW(AscW("0"c) + (code - Keys.NumPad0))
            Case Keys.Add : Return "Num +"
            Case Keys.Subtract : Return "Num -"
            Case Keys.Multiply : Return "Num *"
            Case Keys.Divide : Return "Num /"
            Case Keys.Decimal : Return "Num ."
            Case Keys.Next : Return "PageDown"
            Case Keys.Prior : Return "PageUp"
            Case Keys.Back : Return "Backspace"
            Case Keys.Return : Return "Enter"
            Case Keys.Space : Return "Space"
        End Select

        Dim name As String = code.ToString()
        ' Anything whose enum name is not a plain identifier (an unnamed value comes back
        ' as a number) is not something to write into a profile.
        If name.Length = 0 OrElse Char.IsDigit(name(0)) Then Return String.Empty
        If name.StartsWith("Oem", StringComparison.Ordinal) OrElse name.StartsWith("Browser", StringComparison.Ordinal) Then Return name
        Return name
    End Function

    Private Shared Function KeyFromName(name As String) As Keys
        Dim trimmed As String = name.Trim()
        If trimmed.Length = 1 AndAlso trimmed(0) >= "0"c AndAlso trimmed(0) <= "9"c Then
            Return CType(Keys.D0 + (AscW(trimmed(0)) - AscW("0"c)), Keys)
        End If

        Select Case trimmed.ToLowerInvariant()
            Case "num +" : Return Keys.Add
            Case "num -" : Return Keys.Subtract
            Case "num *" : Return Keys.Multiply
            Case "num /" : Return Keys.Divide
            Case "num ." : Return Keys.Decimal
            Case "pagedown" : Return Keys.Next
            Case "pageup" : Return Keys.Prior
            Case "backspace" : Return Keys.Back
            Case "enter" : Return Keys.Return
            Case "space" : Return Keys.Space
        End Select

        If trimmed.StartsWith("Num ", StringComparison.OrdinalIgnoreCase) AndAlso trimmed.Length = 5 AndAlso
           trimmed(4) >= "0"c AndAlso trimmed(4) <= "9"c Then
            Return CType(Keys.NumPad0 + (AscW(trimmed(4)) - AscW("0"c)), Keys)
        End If

        Dim parsed As Keys
        If [Enum].TryParse(Of Keys)(trimmed, ignoreCase:=True, result:=parsed) Then
            ' TryParse also accepts "17" and comma lists; only a real single name counts.
            If String.Equals(parsed.ToString(), trimmed, StringComparison.OrdinalIgnoreCase) Then Return parsed
        End If
        Return Keys.None
    End Function

    ' ------------------------------------------------------------------- store ----

    ''' <summary>
    ''' The overrides in the profile, cleaned up: unknown ids, unusable or reserved
    ''' combinations and duplicates are dropped, and an "override" that just restates the
    ''' default is dropped too - the map is the list of what the user CHANGED.
    ''' </summary>
    Public Shared Function Load(json As String) As Dictionary(Of String, String)
        Dim map As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Dim claimed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Dim raw As Dictionary(Of String, String) = Nothing
        Try
            raw = JsonSerializer.Deserialize(Of Dictionary(Of String, String))(If(String.IsNullOrWhiteSpace(json), "{}", json))
        Catch
            Return map
        End Try
        If raw Is Nothing Then Return map

        ' Catalog order, not JSON order: the winner of a duplicate must not depend on how
        ' a text editor happened to leave the file.
        For Each action As HotkeyAction In Catalog
            Dim stored As String = Nothing
            If Not raw.TryGetValue(action.Id, stored) Then Continue For

            If String.Equals(If(stored, "").Trim(), NoShortcut, StringComparison.Ordinal) Then
                map(action.Id) = NoShortcut
                Continue For
            End If

            Dim combo As String = Canonical(stored)
            If combo.Length = 0 Then Continue For
            If String.Equals(combo, Canonical(action.DefaultCombo), StringComparison.OrdinalIgnoreCase) Then Continue For
            If Not claimed.Add(combo) Then Continue For
            map(action.Id) = combo
        Next
        Return map
    End Function

    Public Shared Function Save(map As Dictionary(Of String, String)) As String
        Dim ordered As New Dictionary(Of String, String)(StringComparer.Ordinal)
        If map IsNot Nothing Then
            For Each action As HotkeyAction In Catalog
                Dim combo As String = Nothing
                If map.TryGetValue(action.Id, combo) AndAlso Not String.IsNullOrEmpty(combo) Then ordered(action.Id) = combo
            Next
        End If
        Return JsonSerializer.Serialize(ordered)
    End Function

    ''' <summary>What the action answers to right now.</summary>
    Public Shared Function EffectiveCombo(map As Dictionary(Of String, String), action As HotkeyAction) As String
        If action Is Nothing Then Return String.Empty
        Dim combo As String = Nothing
        If map IsNot Nothing AndAlso map.TryGetValue(action.Id, combo) AndAlso Not String.IsNullOrEmpty(combo) Then
            Return If(combo = NoShortcut, String.Empty, combo)
        End If
        Return Canonical(action.DefaultCombo)
    End Function

    ''' <summary>The action that has been MOVED onto this combination, or "". Defaults are
    ''' deliberately not reported: an untouched action is still answered for by the
    ''' viewer's own key handling.</summary>
    Public Shared Function OwnerOfOverride(map As Dictionary(Of String, String), combo As String) As String
        If map Is Nothing OrElse String.IsNullOrEmpty(combo) Then Return String.Empty
        For Each pair As KeyValuePair(Of String, String) In map
            If pair.Value = NoShortcut Then Continue For
            If String.Equals(pair.Value, combo, StringComparison.OrdinalIgnoreCase) Then Return pair.Key
        Next
        Return String.Empty
    End Function

    ''' <summary>The action currently holding this combination - override or default - or
    ''' "". This is the conflict question the dialog asks.</summary>
    Public Shared Function OwnerOfCombo(map As Dictionary(Of String, String), combo As String) As String
        If String.IsNullOrEmpty(combo) Then Return String.Empty
        For Each action As HotkeyAction In Catalog
            Dim effective As String = EffectiveCombo(map, action)
            If effective.Length = 0 Then Continue For
            If String.Equals(effective, combo, StringComparison.OrdinalIgnoreCase) Then Return action.Id
        Next
        Return String.Empty
    End Function

    ''' <summary>
    ''' True when this combination is the factory home of an action that has since moved
    ''' away, and nothing has moved in. The viewer swallows such a key: leaving it working
    ''' would mean the action answers to two combinations and the remapping never happened.
    ''' </summary>
    Public Shared Function IsRetiredDefault(map As Dictionary(Of String, String), combo As String) As Boolean
        If map Is Nothing OrElse map.Count = 0 OrElse String.IsNullOrEmpty(combo) Then Return False
        If OwnerOfCombo(map, combo).Length > 0 Then Return False

        For Each action As HotkeyAction In Catalog
            If Not map.ContainsKey(action.Id) Then Continue For
            If String.Equals(Canonical(action.DefaultCombo), combo, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

End Class
#End If
