#If Not NETFRAMEWORK Then
Option Strict On

Imports System.Collections.Generic
Imports System.Text

''' <summary>One line of the F1 reference card: the keys, and what they do.</summary>
Friend NotInheritable Class HelpCardRow

    Public Sub New(keys As String, description As String, Optional keysAreWords As Boolean = False, Optional actionId As String = "")
        Me.Keys = keys
        Me.Description = description
        Me.KeysAreWords = keysAreWords
        Me.ActionId = actionId
    End Sub

    ''' <summary>The key column as shown. Empty means "no key assigned" - the card says so in words.</summary>
    Public ReadOnly Keys As String
    ''' <summary>A registered Russian key (the localization key IS the Russian source), passed through <c>T</c> by the card.</summary>
    Public ReadOnly Description As String
    ''' <summary>True when <see cref="Keys"/> is itself a registered key ("Левый клик") rather than key names.</summary>
    Public ReadOnly KeysAreWords As Boolean
    ''' <summary>The catalogue action behind the row, or "" for a fixed key the user cannot rebind.</summary>
    Public ReadOnly ActionId As String
End Class

''' <summary>A titled block of rows. The title is a registered Russian key.</summary>
Friend NotInheritable Class HelpCardGroup

    Public Sub New(title As String)
        Me.Title = title
    End Sub

    Public ReadOnly Title As String
    Public ReadOnly Rows As New List(Of HelpCardRow)()
End Class

''' <summary>
''' The row LIST of the F1 reference card (UX-115 and UX-116 of ticket 123). The card used to be one static
''' Label that said "P/N (PgDn/PgUp)" - which reads as P forward and N back, when in code P is previous and N
''' is "go to number" - and left out F2, F7, F10, F11, Ctrl+Home/End, the video keys, the mouse chords,
''' Shift+D and every key the user had rebound. Now the keys are read from the live shortcut map, so a
''' rebinding shows, and the list is grouped Navigation / Files / View / Video / Mouse / Panels.
'''
''' Pure and UI-free: the rows carry registered Russian KEYS (the text lives in the localization tables, 013),
''' and <c>HelpCardPanel</c> only draws what this returns. The one test that matters walks the shortcut
''' catalogue and fails when an action has no row, so a new key cannot ship undocumented.
''' </summary>
Friend NotInheritable Class HelpCardRows

    Private Sub New()
    End Sub

    ''' <summary>The most key names one row lists before it stops (Previous file has five).</summary>
    Private Const MaxKeysPerRow As Integer = 5

    ''' <summary>The card's groups, in the order it shows them, for the given overrides map (may be Nothing).</summary>
    Public Shared Function Build(map As Dictionary(Of String, String)) As List(Of HelpCardGroup)
        Dim groups As New List(Of HelpCardGroup)()

        Dim navigation As New HelpCardGroup("Навигация")
        For Each id As String In {"nextFile", "prevFile", "jumpToNumber", "firstFile", "lastFile",
                                  "back10", "forward10", "back100", "forward100",
                                  "randomFile", "slideshow", "randomSlideshow", "chooseFile"}
            AddAction(navigation, map, id)
        Next
        groups.Add(navigation)

        Dim files As New HelpCardGroup("Файлы")
        files.Rows.Add(New HelpCardRow(SlotKeys(map, copy:=False), "Переместить в папку-получатель"))
        files.Rows.Add(New HelpCardRow(SlotKeys(map, copy:=True), "Копировать в папку-получатель"))
        For Each id As String In {"deleteFile", "deletePermanent", "undo", "renameFile"}
            AddAction(files, map, id)
        Next
        groups.Add(files)

        Dim view As New HelpCardGroup("Вид")
        For Each id As String In {"zoomIn", "zoomOut", "zoomFit", "zoomActual", "rotateCw", "rotateCcw",
                                  "ocrTranslate", "ocrAuto", "fullScreen"}
            AddAction(view, map, id)
        Next
        view.Rows.Add(New HelpCardRow("F11", "Во весь экран без панелей"))
        groups.Add(view)

        Dim video As New HelpCardGroup("Видео и аудио")
        For Each id As String In {"playPause", "mute", "volumeUp", "volumeDown",
                                  "speedSlower", "speedFaster", "speedReset",
                                  "seekBackShort", "seekFwdShort", "seekBackMedium", "seekFwdMedium", "seekBackLong", "seekFwdLong",
                                  "frameStep", "abLoop", "audioTrack", "subtitleTrack"}
            AddAction(video, map, id)
        Next
        groups.Add(video)

        Dim mouse As New HelpCardGroup("Мышь")
        mouse.Rows.Add(New HelpCardRow("Левый клик", "Следующий файл", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Правый клик", "Предыдущий файл", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Средняя кнопка", "Меню картинки со всеми действиями над ней", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Колесо", "Листать файлы (в настройках колесо можно переключить на масштаб)", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Shift / Ctrl / Alt + клик", "На 10 / 100 / 1000 файлов вперёд; правой кнопкой - назад", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Ctrl / Shift / Alt + колесо", "Масштаб / 100 % / вписать в окно", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Двойной клик", "Во весь экран", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Клик по видео", "Пауза / продолжить", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Правая кнопка по видео", "Меню видео со всеми действиями над ним", keysAreWords:=True))
        mouse.Rows.Add(New HelpCardRow("Боковые кнопки мыши", "Следующий / предыдущий файл", keysAreWords:=True))
        groups.Add(mouse)

        Dim panels As New HelpCardGroup("Панели и окна")
        For Each id As String In {"settings", "imagePanel", "help"}
            AddAction(panels, map, id)
        Next
        panels.Rows.Add(New HelpCardRow("F10", "Клавиатурный режим панелей (Tab и стрелки - выбор, Enter - нажать)"))
        panels.Rows.Add(New HelpCardRow(MediaMenuKeys(map), "Меню текущего файла", actionId:="mediaMenu"))
        panels.Rows.Add(New HelpCardRow("Esc", "Шаг назад: меню, справка, перевод, масштаб, слайд-шоу, полный экран, затем закрытие окна"))
        groups.Add(panels)

        Return groups
    End Function

    ''' <summary>The catalogue actions the card lists on rows of their own - everything except the slot actions
    ''' (the two slot rows) and the ones with a fixed row. A test compares this with the catalogue.</summary>
    Public Shared Function ActionIdsOnTheCard(map As Dictionary(Of String, String)) As List(Of String)
        Dim ids As New List(Of String)()
        For Each group As HelpCardGroup In Build(map)
            For Each row As HelpCardRow In group.Rows
                If row.ActionId.Length > 0 Then ids.Add(row.ActionId)
            Next
        Next
        Return ids
    End Function

    Private Shared Sub AddAction(group As HelpCardGroup, map As Dictionary(Of String, String), actionId As String)
        Dim action As HotkeyAction = CustomHotkeys.Find(actionId)
        If action Is Nothing Then Return
        group.Rows.Add(New HelpCardRow(KeysOf(map, action), action.Caption, actionId:=action.Id))
    End Sub

    ''' <summary>What an action answers to right now, spelled for reading. A rebound action shows its one new
    ''' key (or nothing, when the user cleared it); an untouched one shows its factory keys, except those another
    ''' action has been moved onto and the system media keys.</summary>
    Public Shared Function KeysOf(map As Dictionary(Of String, String), action As HotkeyAction) As String
        Dim combo As String = Nothing
        If map IsNot Nothing AndAlso map.TryGetValue(action.Id, combo) AndAlso Not String.IsNullOrEmpty(combo) Then
            Return If(combo = CustomHotkeys.NoShortcut, String.Empty, DisplayKey(combo))
        End If

        Dim shown As New List(Of String)()
        For Each factory As String In CustomHotkeys.FactoryCombos(action)
            If factory.StartsWith("Browser", StringComparison.Ordinal) OrElse factory.StartsWith("Media", StringComparison.Ordinal) Then Continue For
            If CustomHotkeys.OwnerOfOverride(map, factory).Length > 0 Then Continue For
            shown.Add(DisplayKey(factory))
            If shown.Count >= MaxKeysPerRow Then Exit For
        Next
        Return String.Join(", ", shown)
    End Function

    ''' <summary>The recipient slot keys: "1 - 0" while nothing is rebound, otherwise every slot's own key.</summary>
    Private Shared Function SlotKeys(map As Dictionary(Of String, String), copy As Boolean) As String
        Dim any As Boolean = False
        Dim all As New List(Of String)()
        For slot As Integer = 1 To 10
            Dim id As String = If(copy, CustomHotkeys.CopyActionId(slot), CustomHotkeys.RecipientActionId(slot))
            Dim action As HotkeyAction = CustomHotkeys.Find(id)
            ' An override of ANOTHER action may have taken this slot's digit away.
            For Each factory As String In CustomHotkeys.FactoryCombos(action)
                If CustomHotkeys.OwnerOfOverride(map, factory).Length > 0 Then any = True
            Next
            Dim combo As String = Nothing
            If map IsNot Nothing AndAlso map.TryGetValue(id, combo) AndAlso Not String.IsNullOrEmpty(combo) Then any = True
            Dim keys As String = KeysOf(map, action)
            ' The numeric block answers a move too (NumPad 1 beside 1); the card names the top row only.
            Dim comma As Integer = keys.IndexOf(","c)
            If comma > 0 Then keys = keys.Substring(0, comma)
            If keys.Length > 0 Then all.Add(keys)
        Next
        If Not any Then Return If(copy, "Shift+1 - Shift+0", "1 - 0")
        Return String.Join(", ", all)
    End Function

    ''' <summary>Menu and Shift+F10 always work; a key of the user's own for the same action is added.</summary>
    Private Shared Function MediaMenuKeys(map As Dictionary(Of String, String)) As String
        Dim extra As String = String.Empty
        Dim action As HotkeyAction = CustomHotkeys.Find("mediaMenu")
        If action IsNot Nothing Then extra = KeysOf(map, action)
        Return If(extra.Length > 0, "Menu, Shift+F10, " & extra, "Menu, Shift+F10")
    End Function

    ''' <summary>The spelling of a key for the reader: the enum names of the punctuation keys become the
    ''' characters on the keycap, and PageUp / PageDown the short forms keyboards print.</summary>
    Public Shared Function DisplayKey(combo As String) As String
        If String.IsNullOrEmpty(combo) Then Return String.Empty
        Dim text As New StringBuilder(combo)
        text.Replace("OemOpenBrackets", "[")
        text.Replace("OemCloseBrackets", "]")
        text.Replace("OemPipe", "\")
        text.Replace("OemPeriod", ".")
        text.Replace("PageDown", "PgDn")
        text.Replace("PageUp", "PgUp")
        Return text.ToString()
    End Function

End Class
#End If
