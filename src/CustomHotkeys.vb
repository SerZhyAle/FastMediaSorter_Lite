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

    Public Sub New(id As String, caption As String, defaultCombo As String, ParamArray extraDefaults As String())
        Me.Id = id
        Me.Caption = caption
        Me.DefaultCombo = defaultCombo
        Me.ExtraDefaults = If(extraDefaults, Array.Empty(Of String)())
    End Sub

    Public ReadOnly Id As String
    Public ReadOnly Caption As String
    ''' <summary>The combination the dialog shows and the conflict check asks about.</summary>
    Public ReadOnly DefaultCombo As String
    ''' <summary>
    ''' The OTHER keys the viewer's historical key map answers with the same action - D
    ''' beside Delete, Space and PageDown beside Right, NumPad 1 beside 1. Moving the action
    ''' must retire every one of them, or the dialog shows it rebound while a factory key
    ''' still performs it (audit 047, C-19).
    ''' </summary>
    Public ReadOnly ExtraDefaults As String()
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
        ' The playback extras at the end of this catalog (ticket 119 §5.0.2). The Oem*
        ' defaults are PHYSICAL-key bindings: on a layout where "[" needs AltGr the dialog
        ' still shows the US name - that is what the shortcuts dialog is for, and the
        ' persisted spelling is the enum name exactly as any other Oem* key has always
        ' been spelled. The defaults dodge the keys this viewer already answers with
        ' (E, L, Backspace - audit-checked in 119 §5.0.3).
        '
        ' Ticket 123: Ctrl+Home / Ctrl+End were hard-coded in KeybUse outside this map (UX-112) - as
        ' extra factory keys of firstFile / lastFile they are retired when the action is rebound. The
        ' last six entries (Ф-3, UX-102/UX-112) are the video basics and the track keys, which had no
        ' entry to rebind; their defaults were checked free against KeybUse and the reserved list
        ' (Q123-3): K and M are unused letters, Ctrl+Up / Ctrl+Down are Ctrl chords the historical
        ' map throws away. The four basics live only while a video or audio file is the current media.
        '
        ' The rest of 123 (UX-112, UX-113): "openContent" is what Enter always did - open the document in its
        ' program, unlock a locked container - now an action of its own, so it can be moved and the key it is
        ' moved off is retired like any other. Zoom also answers to the MAIN row ("=" and "+", "-"): a laptop has
        ' no numeric block, and "/" and "*" are Shift+7 / Shift+8 there, which are already "copy to slot 7 / 8",
        ' so only the two zoom steps get a second home. "+" is Shift+"=" on most layouts, hence both spellings.
        ' VIEWER-KEYBOARD as INPUT-CHORD / INPUT-PARITY 0.4 incorporate it (specification 252 T-3, the
        ' defaults half): F is the family's fullscreen key and F4 the picker; the viewer's historical F7
        ' alias remains, as the table allows ("the inherited F7 alias may remain on Windows"). G opens the
        ' thumbnail panel beside the viewer's F3.
        Dim actions As New List(Of HotkeyAction) From {
            New HotkeyAction("nextFile", "Следующий файл", "Right", "Space", "PageDown", "BrowserForward"),
            New HotkeyAction("prevFile", "Предыдущий файл", "Left", "P", "B", "Backspace", "PageUp", "BrowserBack"),
            New HotkeyAction("randomFile", "Случайный файл", "Y"),
            New HotkeyAction("slideshow", "Слайд-шоу", "S"),
            New HotkeyAction("randomSlideshow", "Случайное слайд-шоу", "I", "F5"),
            New HotkeyAction("firstFile", "Первый файл", "Home", "H", "BrowserHome", "Ctrl+Home"),
            New HotkeyAction("lastFile", "Последний файл", "End", "E", "L", "BrowserStop", "Ctrl+End"),
            New HotkeyAction("back10", "Назад на 10 файлов", "Up"),
            New HotkeyAction("forward10", "Вперёд на 10 файлов", "Down"),
            New HotkeyAction("back100", "Назад на 100 файлов", "Shift+PageUp"),
            New HotkeyAction("forward100", "Вперёд на 100 файлов", "Shift+PageDown"),
            New HotkeyAction("chooseFile", "Выбрать файл", "F4"),
            New HotkeyAction("jumpToNumber", "Перейти к номеру файла", "N"),
            New HotkeyAction("openContent", "Открыть документ в программе или защищённый файл", "Enter"),
            New HotkeyAction("renameFile", "Переименовать файл", "F6"),
            New HotkeyAction("deleteFile", "Удалить файл", "Delete", "D"),
            New HotkeyAction("deletePermanent", "Удалить безвозвратно", "Shift+Delete", "Shift+D"),
            New HotkeyAction("undo", "Отменить действие", "U"),
            New HotkeyAction("rotateCw", "Повернуть по часовой стрелке", "R"),
            New HotkeyAction("rotateCcw", "Повернуть против часовой стрелки", "Shift+R"),
            New HotkeyAction("ocrTranslate", "Распознать и перевести", "T"),
            New HotkeyAction("ocrAuto", "Автоматическое распознавание", "Shift+T"),
            New HotkeyAction("fullScreen", "Полноэкранный режим", "F7", "F"),
            New HotkeyAction("settings", "Настройки", "F2"),
            New HotkeyAction("imagePanel", "Панель изображений", "F3", "G"),
            New HotkeyAction("help", "Справка", "F1"),
            New HotkeyAction("zoomIn", "Увеличить масштаб", "Num +", "Oemplus", "Shift+Oemplus"),
            New HotkeyAction("zoomOut", "Уменьшить масштаб", "Num -", "OemMinus"),
            New HotkeyAction("zoomFit", "Вписать в окно", "Num /"),
            New HotkeyAction("zoomActual", "Реальный размер", "Num *"),
            New HotkeyAction("speedSlower", "Медленнее", "OemOpenBrackets"),
            New HotkeyAction("speedFaster", "Быстрее", "OemCloseBrackets"),
            New HotkeyAction("speedReset", "Нормальная скорость", "OemPipe"),
            New HotkeyAction("seekBackShort", "Назад на короткий шаг", "Shift+Left"),
            New HotkeyAction("seekFwdShort", "Вперёд на короткий шаг", "Shift+Right"),
            New HotkeyAction("seekBackMedium", "Назад на средний шаг", "Alt+Left"),
            New HotkeyAction("seekFwdMedium", "Вперёд на средний шаг", "Alt+Right"),
            New HotkeyAction("seekBackLong", "Назад на длинный шаг", "Ctrl+Left"),
            New HotkeyAction("seekFwdLong", "Вперёд на длинный шаг", "Ctrl+Right"),
            New HotkeyAction("frameStep", "Следующий кадр", "OemPeriod"),
            New HotkeyAction("frameBack", "Предыдущий кадр", "Oemcomma"),
            New HotkeyAction("abLoop", "Повтор A-B", "Shift+L"),
            New HotkeyAction("playPause", "Пауза / продолжить", "K", "MediaPlayPause"),
            New HotkeyAction("mute", "Звук вкл/выкл", "M"),
            New HotkeyAction("volumeUp", "Громче", "Ctrl+Up"),
            New HotkeyAction("volumeDown", "Тише", "Ctrl+Down"),
            New HotkeyAction("audioTrack", "Следующая звуковая дорожка", "A"),
            New HotkeyAction("subtitleTrack", "Следующие субтитры", "V"),
            New HotkeyAction("mediaMenu", "Меню файла - ещё одна клавиша (Menu и Shift+F10 работают всегда)", "")}

        ' Slots 1..9 answer to their own digit, slot 10 to "0" - the frozen MoveOn0 rule.
        For slot As Integer = 1 To 10
            Dim digit As String = If(slot = 10, "0", slot.ToString(Globalization.CultureInfo.InvariantCulture))
            actions.Add(New HotkeyAction(RecipientActionId(slot), "", digit, "Num " & digit))
        Next
        ' Shift + a TOP-ROW digit copies into the same slot (UX-112): a catalogue action of its own, so it
        ' can be rebound. Top row only - Shift+NumPad1 arrives as Keys.End (see KeybUse).
        For slot As Integer = 1 To 10
            Dim digit As String = If(slot = 10, "0", slot.ToString(Globalization.CultureInfo.InvariantCulture))
            actions.Add(New HotkeyAction(CopyActionId(slot), "", "Shift+" & digit))
        Next
        Return actions.ToArray()
    End Function

    Public Shared Function CopyActionId(slot As Integer) As String
        Return "copySlot" & slot.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>The slot a copy-to-slot action id names, or 0 when it names something else.</summary>
    Public Shared Function CopySlotOf(actionId As String) As Integer
        If actionId Is Nothing OrElse Not actionId.StartsWith("copySlot", StringComparison.Ordinal) Then Return 0
        Dim slot As Integer
        If Not Integer.TryParse(actionId.Substring("copySlot".Length), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, slot) Then Return 0
        If slot < 1 OrElse slot > 10 Then Return 0
        Return slot
    End Function

    ''' <summary>The digit a slot is labelled with: 1..9, and 0 for slot 10.</summary>
    Public Shared Function SlotDigit(slot As Integer) As String
        Return If(slot = 10, "0", slot.ToString(Globalization.CultureInfo.InvariantCulture))
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
        If (keyData And Keys.Shift) = Keys.Shift Then text.Append("Shift+")
        If (keyData And Keys.Alt) = Keys.Alt Then text.Append("Alt+")
        text.Append(KeyName(code))
        Return text.ToString()
    End Function

    ''' <summary>The reverse, tolerant of case and spacing. Keys.None when the text does
    ''' not name a usable combination - a hand-edited profile is data, not a crash.</summary>
    Public Shared Function Parse(text As String) As Keys
        text = Canonical(text)
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

    ''' <summary>Recognized notation, independent of assignment policy. "" when the
    ''' text contains no single recognized trigger.</summary>
    Public Shared Function Canonical(text As String) As String
        If String.IsNullOrWhiteSpace(text) Then Return String.Empty
        ' INPUT-CHORD rules 1-3: notation is independent of assignment policy. Win and
        ' reserved OS chords can be spelled even though this viewer cannot bind them.
        ' Keep the old viewer-only trigger vocabulary readable (Num +, arrows, OEM and
        ' media keys); it must not make an unknown token disappear into another chord.
        Dim rest As String = text.Trim()
        Dim ctrl, shift, alt, win As Boolean
        Do
            rest = rest.Trim()
            If rest.StartsWith("+", StringComparison.Ordinal) Then
                rest = rest.Substring(1)
            ElseIf TryStripModifier(rest, "ctrl") OrElse TryStripModifier(rest, "control") Then
                ctrl = True
            ElseIf TryStripModifier(rest, "shift") Then
                shift = True
            ElseIf TryStripModifier(rest, "alt") Then
                alt = True
            ElseIf TryStripModifier(rest, "win") OrElse TryStripModifier(rest, "meta") OrElse TryStripModifier(rest, "super") Then
                win = True
            Else
                Exit Do
            End If
        Loop
        ' The legacy Add spelling contains a literal plus; preserve it before trimming
        ' tolerated trailing separators. A second trigger still fails KeyFromName.
        If Not String.Equals(rest, "Num +", StringComparison.OrdinalIgnoreCase) Then rest = rest.TrimEnd("+"c, " "c)
        Dim code As Keys = KeyFromName(rest)
        If Not IsAssignableKey(code) Then Return String.Empty
        Dim trigger As String = If(code = Keys.Escape, "Esc", KeyName(code))
        Return If(ctrl, "Ctrl+", "") & If(shift, "Shift+", "") & If(alt, "Alt+", "") & If(win, "Win+", "") & trigger
    End Function

    ''' <summary>
    ''' The combinations §3.5 keeps out of the user's hands: the ones Windows owns, and
    ''' the ones the viewer itself must always answer to - F11 leaves and enters the
    ''' panel-less full screen, Esc gets out of it (one layer at a time, 123 §3.1), F10 is
    ''' the chrome's keyboard mode (taken in ProcessCmdKey before this map is ever asked, so a
    ''' binding on it would be silently dead - UX-111 of 123), Apps / Shift+F10 open the media menu.
    ''' </summary>
    Public Shared Function IsReserved(keyData As Keys) As Boolean
        Dim code As Keys = keyData And Keys.KeyCode
        Select Case code
            Case Keys.F11, Keys.F10, Keys.F1, Keys.Escape, Keys.LWin, Keys.RWin, Keys.Apps : Return True
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
        If (code And Keys.KeyCode) <> code Then Return False
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
            Case Keys.NumPad0 To Keys.NumPad9 : Return "Numpad" & ChrW(AscW("0"c) + (code - Keys.NumPad0))
            Case Keys.Add : Return "NumpadAdd"
            Case Keys.Subtract : Return "NumpadSubtract"
            Case Keys.Multiply : Return "NumpadMultiply"
            Case Keys.Divide : Return "NumpadDivide"
            Case Keys.Decimal : Return "NumpadDecimal"
            Case Keys.Next : Return "PageDown"
            Case Keys.Prior : Return "PageUp"
            Case Keys.Back : Return "Backspace"
            Case Keys.Return : Return "Enter"
            Case Keys.Space : Return "Space"
                ' The playback defaults of ticket 119. These values carry a second enum
                ' name (Oem4 = OemOpenBrackets = 219, Oem5 = OemPipe = 220, Oem6 =
                ' OemCloseBrackets = 221) and Keys.ToString() answers with whichever alias
                ' was declared first - so the spelling is pinned here, like "Num +" above,
                ' or a round-trip through Canonical would rename the key.
            Case Keys.OemOpenBrackets : Return "BracketLeft"
            Case Keys.OemPipe : Return "Backslash"
            Case Keys.OemCloseBrackets : Return "BracketRight"
            Case Keys.OemPeriod : Return "Period"
            Case Keys.Oemcomma : Return "Comma"
            Case Keys.Oemplus : Return "Equal"
            Case Keys.OemMinus : Return "Minus"
            Case Keys.MediaNextTrack : Return "MediaTrackNext"
            Case Keys.MediaPreviousTrack : Return "MediaTrackPrevious"
            Case Keys.VolumeUp : Return "AudioVolumeUp"
            Case Keys.VolumeDown : Return "AudioVolumeDown"
            Case Keys.VolumeMute : Return "AudioVolumeMute"
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
            Case "numpadadd" : Return Keys.Add
            Case "numpadsubtract" : Return Keys.Subtract
            Case "numpadmultiply" : Return Keys.Multiply
            Case "numpaddivide" : Return Keys.Divide
            Case "numpaddecimal" : Return Keys.Decimal
            Case "bracketleft" : Return Keys.OemOpenBrackets
            Case "bracketright" : Return Keys.OemCloseBrackets
            Case "period" : Return Keys.OemPeriod
            Case "comma" : Return Keys.Oemcomma
            Case "equal" : Return Keys.Oemplus
            Case "minus" : Return Keys.OemMinus
            Case "backslash" : Return Keys.OemPipe
            Case "mediatracknext" : Return Keys.MediaNextTrack
            Case "mediatrackprevious" : Return Keys.MediaPreviousTrack
            Case "audiovolumeup" : Return Keys.VolumeUp
            Case "audiovolumedown" : Return Keys.VolumeDown
            Case "audiovolumemute" : Return Keys.VolumeMute
            Case "num +" : Return Keys.Add
            Case "num -" : Return Keys.Subtract
            Case "num *" : Return Keys.Multiply
            Case "num /" : Return Keys.Divide
            Case "num ." : Return Keys.Decimal
            Case "pagedown", "pgdn" : Return Keys.Next
            Case "pageup", "pgup" : Return Keys.Prior
            Case "backspace" : Return Keys.Back
            Case "enter", "return" : Return Keys.Return
            Case "esc", "escape" : Return Keys.Escape
            Case "del" : Return Keys.Delete
            Case "ins" : Return Keys.Insert
            Case "prtsc", "prtscn", "printscreen" : Return Keys.PrintScreen
            Case "space" : Return Keys.Space
                ' The aliases whose enum name is not what ToString() reports - the generic
                ' TryParse below would refuse them (119 playback defaults).
            Case "oemopenbrackets" : Return Keys.OemOpenBrackets
            Case "oempipe" : Return Keys.OemPipe
            Case "oemclosebrackets" : Return Keys.OemCloseBrackets
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
    ''' Unknown ids and redundant defaults are dropped. An unusable, reserved or duplicate
    ''' stored binding stays explicitly disabled: INPUT-CHORD rule 3 forbids resurrecting
    ''' its factory action. Old spellings are normalized without changing their key.
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
            If combo.Length = 0 OrElse Parse(combo) = Keys.None Then
                map(action.Id) = NoShortcut
                Continue For
            End If
            If String.Equals(combo, Canonical(action.DefaultCombo), StringComparison.OrdinalIgnoreCase) Then Continue For
            If Not claimed.Add(combo) Then
                map(action.Id) = NoShortcut
                Continue For
            End If
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
        ' Normalize hand-built maps as well as loaded profiles on every write.
        Return JsonSerializer.Serialize(Load(JsonSerializer.Serialize(ordered)))
    End Function

    ''' <summary>The stored map and the factory table are in the INPUT-CHORD 0.4 vocabulary; a caller may still
    ''' hold a legacy spelling ("Oemplus", "Num +"), which names the same physical key. A text that names no
    ''' key is returned as it came, so it still matches nothing.</summary>
    Private Shared Function InCurrentVocabulary(combo As String) As String
        Dim current As String = Canonical(combo)
        Return If(current.Length > 0, current, combo)
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
        combo = InCurrentVocabulary(combo)
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
        combo = InCurrentVocabulary(combo)
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
        combo = InCurrentVocabulary(combo)
        If OwnerOfCombo(map, combo).Length > 0 Then Return False

        For Each action As HotkeyAction In Catalog
            If Not map.ContainsKey(action.Id) Then Continue For
            For Each factory As String In FactoryCombos(action)
                If String.Equals(factory, combo, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>The actions that move, copy or delete the file on screen - the ones whose
    ''' factory keys <see cref="KeyRouting.IsDestructiveKey"/> lists (DEL / D, Shift+DEL,
    ''' the recipient digits). One press is one operation for these (047 C-20).</summary>
    Public Shared Function IsDestructiveAction(actionId As String) As Boolean
        If String.IsNullOrEmpty(actionId) Then Return False
        Return actionId = "deleteFile" OrElse actionId = "deletePermanent" OrElse RecipientSlotOf(actionId) > 0 OrElse CopySlotOf(actionId) > 0
    End Function

    ' ------------------------------------------------- ticket 119: playback keys ----

    ''' <summary>The video basics of ticket 123 Ф-3: pause, mute and the two volume keys. They sit
    ''' beside the eleven playback extras of 119 - same gate (a media that LibVLC owns is on screen),
    ''' same dispatch, same "does not stop a slideshow" rule - but are listed apart so a test can say
    ''' which is which.</summary>
    Private Shared ReadOnly VideoBasicsActionIds As String() = {"playPause", "mute", "volumeUp", "volumeDown"}

    ''' <summary>The ids of the playback extras, in catalogue order. A factory key of one
    ''' of these is answered by the playback dispatch even without an override, because
    ''' the historical map never accepted the Ctrl/Alt chords three of them need (and a
    ''' fourth, Ctrl+Up / Ctrl+Down, is the volume pair of 123).</summary>
    Private Shared ReadOnly PlaybackActionIds As String() = {
        "speedSlower", "speedFaster", "speedReset",
        "seekBackShort", "seekFwdShort", "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong",
        "frameStep", "frameBack", "abLoop",
        "playPause", "mute", "volumeUp", "volumeDown"}

    ''' <summary>True for the eleven playback actions of ticket 119 and the four video basics of 123.</summary>
    Public Shared Function IsPlaybackAction(actionId As String) As Boolean
        If String.IsNullOrEmpty(actionId) Then Return False
        For Each id As String In PlaybackActionIds
            If String.Equals(id, actionId, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>The manual-navigation actions of MEDIA-VIEWER 1.0 rule 5, in catalogue
    ''' order: the four file moves, the four relative jumps, the two absolute ones and the
    ''' jump-to-number dialog. Everything else (sort surfaces, the picker, playback, zoom)
    ''' is not navigation: those keys keep the historical slideshow stop.</summary>
    Private Shared ReadOnly NavigationActionIds As String() = {
        "nextFile", "prevFile", "randomFile",
        "back10", "forward10", "back100", "forward100",
        "firstFile", "lastFile", "jumpToNumber"}

    ''' <summary>True for the ten manual-navigation actions of MEDIA-VIEWER 1.0 rule 5.</summary>
    Public Shared Function IsNavigationAction(actionId As String) As Boolean
        If String.IsNullOrEmpty(actionId) Then Return False
        For Each id As String In NavigationActionIds
            If String.Equals(id, actionId, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' The navigation action whose FACTORY key is this combination, or "" (MEDIA-VIEWER
    ''' 1.0 rule 5): KeybUse asks this before its historical slideshow stop, so a navigation
    ''' key continues a running show while every other key still stops it. Built exactly
    ''' like <see cref="PlaybackActionOfFactoryKey"/>: the custom map has already answered
    ''' by the time this is consulted, so only the factory spellings count, every spelling
    ''' (Right and Space and PageDown alike), not just the shown default.
    ''' </summary>
    Public Shared Function NavigationActionOfFactoryKey(keyData As Keys) As String
        Dim combo As String = Format(keyData)
        If combo.Length = 0 Then Return String.Empty
        For Each id As String In NavigationActionIds
            Dim action As HotkeyAction = Find(id)
            If action Is Nothing Then Continue For
            For Each factory As String In FactoryCombos(action)
                If String.Equals(factory, combo, StringComparison.OrdinalIgnoreCase) Then Return id
            Next
        Next
        Return String.Empty
    End Function

    ''' <summary>
    ''' The two keys that open the media menu of the current file from the keyboard (UX-104 of 123,
    ''' Q123-6): the Menu key and Shift+F10, the OS-standard "context menu" pair. They are reserved
    ''' (nothing else can be bound to them) and always work - the catalogue's <c>mediaMenu</c> action
    ''' only adds one more key of the user's choice, which is why it ships with no default of its own.
    ''' </summary>
    Public Shared Function IsMediaMenuKey(keyData As Keys) As Boolean
        Return keyData = Keys.Apps OrElse keyData = (Keys.Shift Or Keys.F10)
    End Function

    ''' <summary>True for pause, mute, volume up and volume down (ticket 123 Ф-3).</summary>
    Public Shared Function IsVideoBasicsAction(actionId As String) As Boolean
        If String.IsNullOrEmpty(actionId) Then Return False
        For Each id As String In VideoBasicsActionIds
            If String.Equals(id, actionId, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' The playback action whose FACTORY key is this combination, or "" - the overrides-only
    ''' rule is untouched: a moved action still answers only through the override map, a
    ''' retired factory key is still swallowed, and an untouched action is answered here.
    ''' "" for anything that is not a playback default, so the historical dispatch sees the
    ''' key exactly as before. Every factory spelling counts (the shown default and the
    ''' extras: <c>MediaPlayPause</c> is a second home of the pause key), not just the default.
    ''' </summary>
    Public Shared Function PlaybackActionOfFactoryKey(keyData As Keys) As String
        Dim combo As String = Format(keyData)
        If combo.Length = 0 Then Return String.Empty
        For Each id As String In PlaybackActionIds
            Dim action As HotkeyAction = Find(id)
            If action Is Nothing Then Continue For
            For Each factory As String In FactoryCombos(action)
                If String.Equals(factory, combo, StringComparison.OrdinalIgnoreCase) Then Return id
            Next
        Next
        Return String.Empty
    End Function

    ''' <summary>The four zoom actions, in catalogue order (the zoom keys of the picture and of a text document).</summary>
    Private Shared ReadOnly ZoomActionIds As String() = {"zoomIn", "zoomOut", "zoomFit", "zoomActual"}

    ''' <summary>True for the four zoom actions.</summary>
    Public Shared Function IsZoomAction(actionId As String) As Boolean
        If String.IsNullOrEmpty(actionId) Then Return False
        For Each id As String In ZoomActionIds
            If String.Equals(id, actionId, StringComparison.Ordinal) Then Return True
        Next
        Return False
    End Function

    ''' <summary>
    ''' The zoom action whose FACTORY key is this exact combination, or "" (UX-113 of 123): the numeric block
    ''' and the main-row "=" / "+" / "-". Exact key data on purpose - Shift+"-" is "_" and is nobody's zoom.
    ''' Like <see cref="PlaybackActionOfFactoryKey"/> it answers only for factory spellings: a moved action
    ''' is handled through the override map and its abandoned keys are swallowed as retired defaults first.
    ''' </summary>
    Public Shared Function ZoomActionOfFactoryKey(keyData As Keys) As String
        Dim combo As String = Format(keyData)
        If combo.Length = 0 Then Return String.Empty
        For Each id As String In ZoomActionIds
            Dim action As HotkeyAction = Find(id)
            If action Is Nothing Then Continue For
            For Each factory As String In FactoryCombos(action)
                If String.Equals(factory, combo, StringComparison.OrdinalIgnoreCase) Then Return id
            Next
        Next
        Return String.Empty
    End Function

    ''' <summary>
    ''' Does this key press move, copy or delete a file? Decided by the ACTION it resolves
    ''' to with the custom map applied - the same resolution TryHandleCustomHotkey makes -
    ''' never by the key code alone (audit 057.S05-5): a custom shortcut bound to "delete"
    ''' or a slot on F8 or Q still auto-repeated, and "next file" moved onto a digit lost
    ''' its repeat. A retired factory key does nothing at all, so its repeat is harmless;
    ''' anything the map does not answer falls back to the historical key list.
    ''' </summary>
    Public Shared Function IsDestructivePress(map As Dictionary(Of String, String), keyData As Keys) As Boolean
        Dim combo As String = Format(keyData)
        If combo.Length > 0 Then
            Dim owner As String = OwnerOfOverride(map, combo)
            If owner.Length > 0 Then Return IsDestructiveAction(owner)
            If IsRetiredDefault(map, combo) Then Return False
        End If
        Return KeyRouting.IsDestructiveKey(keyData And Keys.KeyCode)
    End Function

    ''' <summary>Every combination the viewer answers with this action out of the box, in
    ''' canonical spelling: the shown default first, then the historical extras.</summary>
    Public Shared Function FactoryCombos(action As HotkeyAction) As IEnumerable(Of String)
        Dim combos As New List(Of String)()
        If action Is Nothing Then Return combos
        Dim primary As String = Canonical(action.DefaultCombo)
        If primary.Length > 0 Then combos.Add(primary)
        For Each extra As String In action.ExtraDefaults
            Dim combo As String = Canonical(extra)
            If combo.Length > 0 AndAlso Not combos.Contains(combo) Then combos.Add(combo)
        Next
        Return combos
    End Function

End Class
#End If
