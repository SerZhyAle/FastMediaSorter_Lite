# Тактическая спецификация С2: файловые операции (перенос, копия, удаление, undo)

Статус: реализовано 2026-07-16 (обе сборки; + Б-46: undo читал текущий режим копирования вместо записанного)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественник: [С1](SPECIFICATION_VIEWER_CORE_S1_DATALOSS.md) (сужение гварда `IsBusy`)
Сборки: **обе**
Объём: 10 багов, 3 файла

## 0. Цель

Сделать конвейер «нажал хоткей → файл переехал → показан следующий» честным:
список меняется **только по факту успеха**, повторное нажатие не рушит состояние,
медиа-хэндлы освобождаются одинаково на всех путях, undo не врёт.

| Ид | П | Что |
|---|---|---|
| Б-03 | П2 | Async move/delete мутирует список до завершения; при ошибке файл пропадает из просмотра |
| Б-04 | П2 | Повторная операция при занятом воркере: перетирание глобалов, крэш в Undo, чужая ветка Completed |
| Б-05 | П2 | Undo после переноса последнего файла: `Insert(-1)` |
| Б-31 | П2 | net48: Delete/Move не останавливают VLC-fallback - E001/E014 |
| Б-06 | П3 | Rename не освобождает играющее видео - E011 |
| Б-07 | П3 | `history_*` чистятся с рабочего потока; async-delete стирает историю undo |
| Б-24 | П3 | DeleteFile диспозит показанную картинку, оставляя её присвоенной боксу |
| Б-08 | П4 | Перенос в текущую же папку выбрасывает файл из списка |
| Б-10 | П4 | Sync-Move проверяет видимость не того бокса - dispose-ветка мертва |
| Б-12* | П2 | Удаление из списка по индексу вместо значения (**частично**: хелпер здесь, корень - в [С3](SPECIFICATION_VIEWER_CORE_S3_NAVIGATION.md)) |

## 1. Общие хелперы стадии

Три копипаст-блока в [Main_Form.FileOperations.vb](../../src/Main_Form.FileOperations.vb) и
[Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb) расходятся между собой
(Б-06, Б-10, Б-24, Б-31 - всё это разные проявления одного и того же). Заводим два
хелпера и переводим на них **все** пути: `PoMove` (4 ветки), `Undo` (2 ветки),
`UpdateFileIndexAndList` ▸ `"DeleteFile"`, `RenameCurrentFile`.

### 1.1 `ReleaseActiveMedia()` - закрывает Б-06, Б-10, Б-24, Б-31

```vb
''' <summary>Освобождает всё, что держит текущий файл открытым, перед файловой
''' операцией: анимацию GIF, образ активного бокса вместе с его потоком и
''' воспроизведение VLC. StopVlcPlayback вызывается в ОБЕИХ сборках: на net48
''' видео, которое не осилил IE (AVI/ZMBV/VP9/MKV), играет через тот же LibVLC и
''' точно так же держит файл (Б-31).</summary>
Private Sub ReleaseActiveMedia()
    StopGifLoopPlayback()

    If is_PictureBox2_Visible Then
        If Picture_Box_2.Image IsNot Nothing Then Picture_Box_2.Image.Dispose()
        Picture_Box_2.Image = Nothing
        If pictureBox2_Stream IsNot Nothing Then pictureBox2_Stream.Dispose() : pictureBox2_Stream = Nothing
    ElseIf is_PictureBox1_Visible Then
        If Picture_Box_1.Image IsNot Nothing Then Picture_Box_1.Image.Dispose()
        Picture_Box_1.Image = Nothing
        If pictureBox1_Stream IsNot Nothing Then pictureBox1_Stream.Dispose() : pictureBox1_Stream = Nothing
    End If

    If is_Vlc_Playing Then StopVlcPlayback()
    current_Loaded_File_Name = ""
#If NETFRAMEWORK Then
    Web_Browser.DocumentText = ""
#End If
End Sub
```

Ключевое отличие от нынешних блоков: выбор бокса по **видимости** (`is_PictureBox2_Visible`,
а не `is_Second_PictureBox_Active` + опечатка Б-10), обязательный `.Image = Nothing`,
освобождение потока и `StopVlcPlayback` вне шва.

### 1.2 `RemoveCurrentFileFromList()` - страховка от Б-12

Удаление **по значению**: индекс мог разъехаться с показанным файлом (тихий отказ
`ReadShowMediaFile`, см. Б-12). Хелпер удаляет ровно тот файл, над которым
выполнена операция, и возвращает индекс, по которому он лежал:

```vb
''' <summary>Убирает file_Path из активной коллекции по ЗНАЧЕНИЮ и возвращает его
''' бывший индекс (-1, если не найден). По индексу удалять нельзя: current_File_Index
''' мог уехать вперёд относительно показанного файла (Б-12).</summary>
Private Function RemoveCurrentFileFromList(file_Path As String) As Integer
    Dim at As Integer = If(is_Files_Array_Active, Array.IndexOf(files_Array, file_Path), files_List.IndexOf(file_Path))
    If at < 0 Then Return -1
    If is_Files_Array_Active Then files_Array = RemoveAt(files_Array, at) Else files_List.RemoveAt(at)
    total_File_Count -= 1
    If current_File_Index > at Then current_File_Index -= 1
    If current_File_Index > total_File_Count - 1 Then current_File_Index = total_File_Count - 1
    Return at
End Function
```

Парная `InsertFileIntoList(file_Path, at)` для откатов и undo - с зажимом индекса
(закрывает Б-05):

```vb
Private Sub InsertFileIntoList(file_Path As String, at As Integer)
    Dim insert_At As Integer = Math.Max(0, Math.Min(at, total_File_Count))
    If is_Files_Array_Active Then files_Array = AddAt(files_Array, file_Path, insert_At) Else files_List.Insert(insert_At, file_Path)
    total_File_Count += 1
    current_File_Index = insert_At
End Sub
```

## 2. Б-04 + Б-07: операция как данные, а не как глобальные поля

Разделяемые `current_File_Operation` / `current_File_Operation_Args`
([Main_Form.vb:224-225](../../src/Main_Form.vb)) - корень Б-04 (перетирание, чужая
ветка Completed) и Б-07 (запись `history_*` с рабочего потока). Вводим тип операции
и передаём его через `RunWorkerAsync(argument)`:

```vb
Private Enum FileOpKind
    Copy
    Move
    Delete
    DeleteUndo
    MoveUndo
End Enum

''' <summary>Одна файловая операция целиком: воркер не читает НИЧЕГО из полей формы.
''' Индекс - снимок на момент постановки, нужен для отката при ошибке.</summary>
Private NotInheritable Class FileOp
    Public Property Kind As FileOpKind
    Public Property Source As String
    Public Property Destination As String
    Public Property SlotKey As String
    Public Property ListIndex As Integer
End Class
```

- `FileOperationWorker_DoWork`: `Dim op = DirectCast(e.Argument, FileOp)`, `Select Case op.Kind`,
  результат - `e.Result = op` (чтобы Completed знал, что именно завершилось).
  Из DoWork **убрать** все записи в `history_*` (Б-07) - они переезжают в Completed.
- `FileOperationWorker_RunWorkerCompleted`: `Dim op = TryCast(e.Result, FileOp)`;
  ветка по `op.Kind`, а не по глобальному полю. Сначала сообщение (оно читает
  `history_*`), потом очистка `history_*`.
- Ветку `"Delete"` привести к синхронному пути: синхронное удаление историю переноса
  не трогает - асинхронное тоже не должно (сейчас поведение undo зависит от галочки).
- Поля `current_File_Operation` / `current_File_Operation_Args` удалить.

**Гвард занятости** (Б-04) - единая точка постановки:

```vb
''' <summary>Ставит операцию в воркер. False = воркер занят: НИЧЕГО не мутируем и
''' не перетираем (раньше повторное нажатие роняло RunWorkerAsync исключением, а
''' Undo при занятом воркере вдобавок исполнял чужую ветку завершения).</summary>
Private Function TryQueueFileOp(op As FileOp) As Boolean
    If FileOperationWorker.IsBusy Then
        lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. предыдущая операция ещё выполняется", "!Wait.. previous operation still running")
        Return False
    End If
    FileOperationWorker.RunWorkerAsync(op)
    Return True
End Function
```

Все async-ветки `PoMove`, `Undo`, `UpdateFileIndexAndList ▸ "DeleteFile"` ставят
операцию **только** через неё и мутируют список только при `True`. Async-ветки Undo
дополнительно обернуть в `Try/Catch` (как PoMove).

> В modern это - промежуточный слой: `FileOp` переиспользуется очередью на Channel
> ([С8](SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md), У-02), где «воркер занят»
> исчезает как понятие. Тип и снапшот аргументов специально введены так, чтобы
> С8 менял только транспорт.

## 3. Б-03: список меняется по факту успеха

Порядок в async-ветках `PoMove`/`Delete` сейчас: мутировать список → показать →
(потом) выполнить. Новый порядок:

1. `ReleaseActiveMedia()` (файл может быть залочен плеером).
2. `TryQueueFileOp(op)`; при False - выход, список не тронут.
3. Оптимистично убрать файл из списка: `op.ListIndex = RemoveCurrentFileFromList(op.Source)`.
4. Показать следующий (`ReadShowMediaFile("SetFile")` - после С1 гвард его пропускает).
5. В `RunWorkerCompleted` при `e.Error IsNot Nothing`:
   - вернуть файл: `InsertFileIntoList(op.Source, op.ListIndex)`;
   - сбросить `history_Source_File_Name` / `history_Destination_File_Name` (перенос
     не состоялся - undo обязан отказать, а не «возвращать» пустоту);
   - статус: «Ошибка операции: ..» + `ReadShowMediaFile("SetFile")`, чтобы вернувшийся
     файл был виден.

Оптимистичное удаление сохраняем осознанно (сортировка должна быть мгновенной);
цена ошибки теперь - откат, а не потеря файла из просмотра.

## 4. Точечные правки

- **Б-05**: `InsertFileIntoList` с зажимом (см. 1.2) вместо голых
  `files_List.Insert(current_File_Index, ..)` / `AddAt(.., current_File_Index)` в `Undo`.
- **Б-06**: `RenameCurrentFile` - первым делом `ReleaseActiveMedia()`, затем
  `RenameFile` (контракт уже поправлен в С1). После успешного переименования показ
  идёт через `ReadShowMediaFile("SetFile")` - файл перечитается.
- **Б-08**: в начале `PoMove`, до всего остального:
  ```vb
  If String.Equals(Path.GetFullPath(destination_Folder_Path).TrimEnd("\"c),
                   Path.GetDirectoryName(Current_File_Name), StringComparison.OrdinalIgnoreCase) Then
      lbl_Status.Text = If(Is_Russian_Language, "! Файл уже в этой папке", "! File is already in that folder")
      Return
  End If
  ```
- **Б-10, Б-24, Б-31**: снимаются переводом всех веток на `ReleaseActiveMedia()`
  (раздел 1.1). Отдельно в [Main_Form.GifPlayback.vb:66-72](../../src/Main_Form.GifPlayback.vb):
  проверку `ReferenceEquals(Picture_Box_X.Image, gif_Restart_Image_Ref)` перенести
  **перед** обращением к `FrameDimensionsList`/`SelectActiveFrame` (сейчас тик
  работает «на честном слове» пустого Catch).

## 5. Приёмка

1. **Ошибка переноса**: в папке-приёмнике уже есть файл с тем же именем → перенос
   (и sync, и async) → статус ошибки, файл **остался в списке и на экране**,
   счётчик не изменился; U сообщает «Нет истории» (а не «файл возвращён»).
2. **Быстрая сортировка**: включить «независимый поток», разложить 20 файлов
   хоткеями 1..3 в темпе печати → ни одного MsgBox E014, ни одной потерянной
   операции, счётчик совпадает с фактическим содержимым папок.
3. **Undo при занятом воркере**: переносим большой файл на медленный диск, сразу
   жмём U → статус «предыдущая операция ещё выполняется», **без крэша**; после
   завершения переноса U возвращает файл, статус «файл возвращён».
4. **Undo последнего файла**: папка из одного файла → перенос → «Нет файлов в
   папке» → U → файл вернулся в список, показан, счётчик «1 из 1» (обе ветки:
   sync и async).
5. **Rename играющего видео**: открыть mp4 (modern) и avi (x86, VLC-fallback) →
   F6 → переименование проходит без E011.
6. **DEL играющего видео на x86**: avi через VLC-fallback → DEL → удаляется без
   E001, показан следующий файл.
7. **Перенос в текущую папку**: назначить хоткею папку, которую смотрим → нажать →
   «Файл уже в этой папке», список не изменился.
8. **GIF + DEL**: анимированный gif → DEL → анимация остановлена, экран не мигает
   красным крестом, следующий файл показан (в т.ч. если следующий - видео при
   холодном VLC).

## 6. Риски

- `ReleaseActiveMedia` трогает 6 мест сразу - главный риск стадии. Каждую ветку
  переводить отдельным коммитом-шагом и прогонять сценарий 8 (GIF + DEL) и 5
  (rename видео) в **обеих** сборках: net48-ветка теперь тоже зовёт
  `StopVlcPlayback` (было только `Web_Browser.DocumentText = ""`).
- `RemoveCurrentFileFromList` меняет `current_File_Index` иначе, чем прежний код
  (`> at` вместо безусловного клампа). Сценарии 2 и 4 проверяют это напрямую.
- Удаление глобалов `current_File_Operation*` - `grep` по всему `src/` перед
  удалением (в т.ч. Table_Form).

## 7. Готово, когда

- [ ] Все 10 багов закрыты; `grep current_File_Operation src/` пуст.
- [ ] `.\build.ps1` - обе сборки; приёмка 1-8 в обоих exe.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
