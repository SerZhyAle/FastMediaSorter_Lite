# Тактическая спецификация С3: листание, индекс, предзагрузка

Статус: план (не начато)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественники: [С1](SPECIFICATION_VIEWER_CORE_S1_DATALOSS.md), [С2](SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md) (хелперы списка)
Сборки: **обе**
Объём: 12 багов, 5 файлов - **самая крупная стадия исправлений**

## 0. Цель

Устранить рассинхрон «индекс ↔ показанный файл ↔ счётчик» и гонки предзагрузки:
что видно на экране, то и есть текущий файл - всегда, включая быстрое листание,
битые файлы и внешнее открытие.

| Ид | П | Что |
|---|---|---|
| Б-12 | П2 | Прыжки мутируют индекс до вызова; тихий отказ не откатывает - delete/move бьёт по чужому элементу |
| Б-13 | П2 | Гонка префетча: чужая картинка под именем текущего файла |
| Б-14 | П2 | Автоскип битого файла молча гасится троттлингом; рекурсия по кругу |
| Б-15 | П3 | `BgWorker_DoWork`: UI-контрол с фонового потока; `e.Cancel` без `Exit Sub` |
| Б-16 | П3 | `total_File_Count` затирается нефильтрованным счётом всех файлов папки |
| Б-17 | П3 | Fallback показывает `Current_Image_Path` из чужой папки |
| Б-18 | П3 | `KeyPreview` без `e.Handled`: Space на кнопке тулбара - двойное действие |
| Б-23 | П4 | Пустая папка обрабатывается как ошибка - затирается сессия |
| Б-19 | П4 | Клавиша N мертва |
| Б-20 | П4 | Автоскип всегда вперёд - пинг-понг при листании назад |
| Б-21 | П4 | Отмена «Перейти к файлу» → «Invalid file number» |
| Б-22 | П4 | Режим `"ReadForJumpToFile"` без Case - работает случайно |

## 1. Режимы `ReadShowMediaFile` - константы (Б-09*, Б-22)

Магические строки режимов породили уже два дефекта: опечатку `"ReadAfterUndo"`
(Б-09, чинится литералом в С2) и режим `"ReadForJumpToFile"` без ветки Case (Б-22).
Заводим в [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb):

```vb
' Режимы ReadShowMediaFile. Строками они были 15 лет и дважды разошлись между
' вызывающим и Select Case (Б-09: "ReadAfterUndo" vs "AfterUndo"; Б-22:
' "ReadForJumpToFile" без ветки вовсе).
Friend Const Mode_Next As String = "ReadNextFile"
Friend Const Mode_Prev As String = "ReadPrevFile"
Friend Const Mode_SetFile As String = "SetFile"
Friend Const Mode_Files As String = "ReadFiles"
Friend Const Mode_FolderAndFile As String = "ReadFolderAndFile"
Friend Const Mode_FolderAndKnownFile As String = "ReadFolderAndKnownFile"
Friend Const Mode_Delete As String = "DeleteFile"
Friend Const Mode_InSlideShow As String = "InSlideShow"
Friend Const Mode_ForRandom As String = "ReadForRandom"
Friend Const Mode_ForSlideShow As String = "ReadForSlideShow"
Friend Const Mode_AfterUndo As String = "AfterUndo"
```

Перевести все вызовы (`grep ReadShowMediaFile src/` - ~30 мест). `"ReadForJumpToFile"`
([Main_Form.vb:673](../../src/Main_Form.vb)) заменить на `Mode_JumpTo` (см. ниже).

## 2. Б-12: индекс меняется только внутри конвейера

Сейчас 12 мест (клавиатура: ±10/±100/Home/End; мышь: Shift/Ctrl/Alt+клик) пишут
`current_File_Index` **до** `ReadShowMediaFile`, который может тихо выйти по
троттлингу или `IsBusy` - индекс уехал, экран нет.

Добавляем два режима с параметром - единственный способ сдвинуть индекс:

```vb
Friend Const Mode_JumpBy As String = "JumpBy"   ' pending_Jump_Delta
Friend Const Mode_JumpTo As String = "JumpTo"   ' pending_Jump_Target
```

```vb
Private pending_Jump_Delta As Integer
Private pending_Jump_Target As Integer

''' <summary>Листнуть на delta файлов. Индекс двигается ВНУТРИ конвейера, после
''' ранних проверок: раньше вызывающий менял его сам, и тихий выход (троттлинг /
''' занятый воркер) оставлял индекс уехавшим относительно показанного файла -
''' следующий DEL удалял из списка чужой элемент (Б-12).</summary>
Private Sub JumpBy(delta As Integer, status_Ru As String, status_En As String)
    pending_Jump_Delta = delta
    pending_Jump_Status_Ru = status_Ru : pending_Jump_Status_En = status_En
    ReadShowMediaFile(Mode_JumpBy)
End Sub

Private Sub JumpTo(target_Index As Integer)
    pending_Jump_Target = target_Index
    ReadShowMediaFile(Mode_JumpTo)
End Sub
```

В `UpdateFileIndexAndList`:

```vb
Case Mode_JumpBy
    current_File_Index += pending_Jump_Delta
    If current_File_Index < 0 Then current_File_Index = 0
    If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
    lbl_Status.Text = If(Is_Russian_Language, pending_Jump_Status_Ru, pending_Jump_Status_En)

Case Mode_JumpTo
    current_File_Index = pending_Jump_Target
    If current_File_Index < 0 Then current_File_Index = 0
    If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
```

Переводятся: [Main_Form.KeyboardInput.vb](../../src/Main_Form.KeyboardInput.vb) строки
55, 60, 109, 114, 172, 177 и [Main_Form.MouseInput.vb](../../src/Main_Form.MouseInput.vb)
строки 29, 40, 58, 69, 87, 98. Статус («+100 файлов» и т.д.) выставляет конвейер, а
не вызывающий - сейчас он врёт при тихом отказе. Предварительные проверки в мыши
(`If total_File_Count > current_File_Index + 10`) удаляются: клампинг делает
конвейер, а сообщения «Недостаточно файлов..» только путают (Home/End их не имеют).

Вторая половина Б-12 - удаление из списка по значению - закрыта хелпером
`RemoveCurrentFileFromList` из [С2](SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md).

## 3. Б-13 + Б-15: транзакционный префетч

Корень: воркер пишет разделяемые поля (`current_Second_File_Name`) и читает живые
флаги (`is_First_Picture_Box_Need_To_Be_Cached`), а `bgWorker_Result` не
сбрасывается при новой навигации.

### 3.1 Снапшот в аргументе

```vb
Private NotInheritable Class PrefetchRequest
    Public Property CurrentFile As String     ' для метаданных статус-строки
    Public Property NextFile As String        ' что декодировать
    Public Property TargetIsBox1 As Boolean   ' СНИМОК на момент постановки
    Public Property FolderPath As String      ' снимок: воркер не читает глобал (Б-15)
End Class

Private NotInheritable Class PrefetchResult
    Public Property NextFile As String
    Public Property TargetIsBox1 As Boolean
    Public Property Picture As Image
    Public Property Data As IO.MemoryStream
End Class
```

- `BgWorker_DoWork`: работает **только** с `e.Argument`; никаких записей в поля формы
  (`current_Second_File_Name` - убрать), `Current_Folder_Path` - из снапшота (Б-15).
- `lbl_Current_File.Text = ""` из DoWork (строка 38) - убрать; передавать через
  `ReportProgress`, как остальные метаданные (Б-15). Сейчас это обращение к
  контролу с потока пула глотается общим Catch и срубает **всю** предзагрузку.
- После `e.Cancel = True` - `Exit Sub` (Б-15): сейчас отменённый воркер продолжает
  качать картинку с шары, а `FormClosing` ждёт его впустую.

### 3.2 Валидация в Completed (UI-поток)

```vb
Dim res = TryCast(e.Result, PrefetchResult)
If res Is Nothing Then bgWorker_Result = "SKIPED" : Return
' Пользователь мог уйти дальше, пока мы декодировали: принимаем префетч ТОЛЬКО
' если он всё ещё описывает следующий файл от актуальной позиции. Иначе - в утиль
' (раньше устаревший результат мог лечь в ВИДИМЫЙ бокс, подменив картинку).
If Not String.Equals(res.NextFile, next_File_After_Current, StringComparison.OrdinalIgnoreCase) Then
    res.Picture?.Dispose() : res.Data?.Dispose()
    bgWorker_Result = "SKIPED"
    Return
End If
' ... разложить в бокс по res.TargetIsBox1, current_Second_File_Name = res.NextFile
bgWorker_Result = "LOADED"
```

### 3.3 Сброс на каждой навигации

В `UpdateCurrentFileAndDisplay` перед постановкой новой задачи:
`bgWorker_Result = "EMPTY"` и `current_Second_File_Name = ""` - иначе условие показа
префетча (строка 458) срабатывает по устаревшему «LOADED».

Условие показа дополнить проверкой, что в боксе действительно лежит образ:
```vb
If bgWorker_Result = "LOADED" AndAlso current_Second_File_Name = Current_File_Name AndAlso
   (If(is_Second_PictureBox_Active, Picture_Box_2.Image, Picture_Box_1.Image)) IsNot Nothing Then
```

> В modern это временный слой: [С7](SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md)
> (У-01) заменит `BackgroundWorker` на конвейер с отменой и кэшем prev/current/next,
> где «устаревший результат» невозможен по конструкции. Правки 3.1-3.3 остаются
> действующими для net48.

## 4. Б-14 + Б-20: автоскип битых файлов

**Автоскип - задуманное поведение** (подтверждено владельцем): битый/пропавший файл
не должен останавливать листание. Сейчас он не работает: рекурсивный вызов приходит
через ~1 мс после того, как этот же вызов установил `last_Action_Time`, и 40-мс
троттлинг его глотает (w0330). Чиним так, чтобы дизайн начал исполняться:

1. **Троттлинг - только для действий пользователя.** Ввести параметр:
   ```vb
   Private Sub ReadShowMediaFile(ByVal read_Mode_Type As String, Optional is_Auto_Skip As Boolean = False)
       ...
       If Not is_Auto_Skip Then
           If last_Action_Time.AddSeconds(minimum_time_before_next_media_file) > current_Operation_Time Then Exit Sub
           last_Action_Time = current_Operation_Time
       End If
   ```
   Все ветки отказа в `LoadStandardImageInPictureBox` (строки 492, 503, 550, 558,
   565, 572) зовут с `is_Auto_Skip:=True`.
2. **Рекурсию заменить циклом.** Ветки отказа не вызывают `ReadShowMediaFile`
   рекурсивно, а возвращают признак «файл не показан» наверх; `UpdateCurrentFileAndDisplay`
   крутит попытки в цикле с ограничителем `total_File_Count` (папка целиком из битых
   файлов не должна ни зациклиться, ни разрастить стек) и при исчерпании пишет
   «Нет читаемых файлов в папке».
3. **Битый файл убирать из списка** (`RemoveCurrentFileFromList`, С2) - по аналогии с
   веткой w0975 для пропавшего: иначе Prev возвращает на него снова.
4. **Направление** (Б-20): поле `last_Nav_Direction` (Next/Prev), выставляется в
   `UpdateFileIndexAndList`; автоскип идёт в ту же сторону - сейчас всегда вперёд,
   и листание назад пинг-понгует между i и i+1.
5. Поправить комментарий w0330 («less than 0.4s» → 0.04 s).

## 5. Точечные правки

- **Б-16**: в `BgWorker_DoWork` считать файлы **с тем же фильтром**, что `GetFiles`:
  `.Where(Function(f) all_Supported_Extensions.Contains(f.Extension.ToLower())).Count()`;
  папку брать из снапшота (3.1). В `ProgressChanged` (строка 180) выводить
  `current_File_Index + 1`, а не хардкод «1». **Не перезаписывать** `total_File_Count`,
  пока список не пересканирован - только текст метки (иначе End после внешнего
  открытия ловит `ArgumentOutOfRange`, а DEL опустошает список при `total > 0`).
- **Б-17**: в `UpdateCurrentFileAndDisplay` ветка `is_File_Found = False` не должна
  слепо подставлять `Current_Image_Path`: искать в свежем списке `Current_File_Name`,
  проверять `Path.GetDirectoryName(..) = Current_Folder_Path`; иначе `current_File_Index = 0`
  и показать `files(0)`. Плюс `Current_Image_Path = Current_File_Name` при каждом
  успешном показе - тогда он перестаёт быть «памятью о прошлой папке».
- **Б-18**: в `KeybUse` для всех обработанных клавиш - `e.Handled = True` и
  `e.SuppressKeyPress = True`; кнопкам тулбара в
  [Main_Form.ModernLayout.vb](../../src/Main_Form.ModernLayout.vb) - `TabStop = False`
  (Space на сфокусированной кнопке даёт и навигацию, и Click).
- **Б-19**: убрать `Keys.N` из списка навигации ([KeyboardInput.vb:74](../../src/Main_Form.KeyboardInput.vb)) -
  там остаются Space/Right/PageDown/BrowserForward; нижний `Case Keys.N` оживает.
- **Б-21**: `Jump_To_file_Number` - `If String.IsNullOrEmpty(take_number) Then Return`
  перед `TryParse`; текст ошибки локализовать.
- **Б-23**: `GetFiles` должен различать «папка пуста» и «ошибка чтения» (сейчас оба -
  `Nothing`, и `LoadFiles` затирает `Current_Folder_Path` + комбобокс, т.е. рассыпает
  сессию на валидной пустой папке). Вариант: `ByRef is_Read_Error As Boolean`; при
  пустой папке - сохранить папку, показать «Папка пустая», очистить только
  медиаповерхность.

## 6. Приёмка

1. **Быстрое листание**: зажать Right на 5 с в папке из 300 фото на сетевой шаре →
   после отпускания показанный файл = счётчику = `lbl_Current_File`; ни одной
   «чужой» картинки; DEL удаляет именно показанный файл.
2. **Прыжки при занятом воркере**: включить «независимый поток», начать перенос
   большого файла, сразу нажать Home → либо переход выполнен, либо ничего не
   произошло, но **счётчик и экран согласованы**; следующий DEL удаляет показанный
   файл (не первый в списке).
3. **Битый файл**: положить в папку 0-байтный `.jpg` и обрезанный `.png` → листание
   вперёд проскакивает их **автоматически** (статус мелькает), назад - тоже
   автоматически и **назад** (не отбрасывает вперёд); битые исчезают из счётчика.
   Папка целиком из битых → «Нет читаемых файлов в папке», без зацикливания.
4. **Счётчик после внешнего открытия**: папка 200 медиа + 300 txt, открыть jpg
   двойным кликом из Проводника → счётчик показывает «N из 200» (не 500); End
   ведёт на последний медиафайл без ошибки; DEL сразу после открытия показывает
   следующий файл папки.
5. **Space на кнопке**: кликнуть по кнопке «Слайд-шоу», затем нажать Space →
   ровно одно действие (не «стоп+листнул+старт»).
6. **N** открывает диалог «Перейти к файлу»; Esc в нём - тихо, без «Invalid file number».
7. **Пустая папка**: выбрать заведомо пустую папку → «Папка пустая», путь в
   комбобоксе сохранён, возврат к прежней папке через историю работает.
8. **Смена папки при живом префетче**: листать папку A, на ходу выбрать папку B →
   первый файл B показан, никаких картинок из A.

## 7. Риски

- Стадия трогает горячий путь целиком. Порядок работ: (1) константы режимов,
  (2) JumpBy/JumpTo, (3) префетч, (4) автоскип, (5) точечные - каждый шаг
  собирается и проверяется отдельно.
- `e.Handled = True` в `KeybUse` может «съесть» клавиши у комбобокса папки: ветка
  раннего выхода при `cmbox_Media_Folder.Focused` (строки 29-38) обязана остаться
  **до** установки флага.
- Сброс `bgWorker_Result` на каждой навигации временно снизит долю попаданий в
  префетч при очень быстром листании - это правильнее показа чужой картинки;
  окончательно решает У-01 (С7).

## 8. Готово, когда

- [ ] 12 багов закрыты; `grep "ReadShowMediaFile(\"" src/` не находит литералов.
- [ ] `.\build.ps1`; приёмка 1-8 в обоих exe.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
