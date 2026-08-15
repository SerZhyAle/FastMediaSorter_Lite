# Тактическая спецификация С8 (modern): очередь файловых операций и обратимость

> **Справочный материал, а не очередь работ** (2026-08-14). Утверждённая владельцем часть
> этого документа извлечена в [С11](SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md) и там
> построена (У-02, У-07); остальное **никогда не утверждалось**. Документ сохранён ради
> разбора и аргументов, но не является списком запланированных задач.
> Ср. [ROADMAP_SPECIFICATION_QUEUE.md](../roadmaps/ROADMAP_SPECIFICATION_QUEUE.md) §1.

Статус: план. Утверждённая владельцем часть вынесена в
[С11](SPECIFICATION_VIEWER_CORE_S11_MODERN_ASYNC.md) и там реализована 2026-07-16
(У-02 - очередь операций, У-07); остаток ниже - по-прежнему план
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](done/SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественник: [С2](done/SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md) (тип `FileOp`, хелперы списка)
Сборки: **только .NET 10 mainline** (net48 остаётся на `BackgroundWorker` + `TryQueueFileOp`)
Объём: У-02, У-06, У-07, У-08

## 0. Цель

Основной сценарий приложения - раскладка файлов хоткеями 0..9 со скоростью нажатий.
Сделать так, чтобы **темп сортировки ограничивала только рука**, а ошибка была
обратимой: очередь вместо одноразового воркера, корзина вместо `File.Delete`,
автоимя вместо MsgBox, стек undo вместо двух строк.

| Ид | Ценность | Что |
|---|---|---|
| У-02 | ★★★ | Очередь операций (Channel + consumer) вместо одноразового воркера |
| У-06 | ★★ | Удаление в корзину + undo удаления |
| У-07 | ★★ | Автоимя `name (2).ext` при коллизии |
| У-08 | ★ | Многоуровневый undo (стек) |

## 1. У-02: очередь

`FileOp` уже введён в [С2](done/SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md) и несёт всё
нужное (тип, source, destination, слот, снимок индекса) - в modern меняется только
**транспорт**: понятие «воркер занят» исчезает.

Новый файл `src/Modern/FileOpQueue.vb` (или `#If Not NETFRAMEWORK` целиком):

```vb
''' <summary>Одна очередь файловых операций с единственным потребителем: нажатия
''' любой частоты просто встают в неё и выполняются по порядку. Раньше вторая
''' операция при занятом BackgroundWorker либо терялась с MsgBox E014, либо
''' перетирала аргументы первой (Б-04).</summary>
Friend NotInheritable Class FileOpQueue
    Private ReadOnly _channel As Channel(Of FileOp) = Channel.CreateUnbounded(Of FileOp)(New UnboundedChannelOptions With {.SingleReader = True})
    ...
    Friend Sub Enqueue(op As FileOp)
    Friend ReadOnly Property PendingCount As Integer
    Friend Event OpCompleted As EventHandler(Of FileOpResult)   ' маршалится на UI
    Friend Function DrainAsync(timeout As TimeSpan) As Task     ' для FormClosing
End Class
```

- Потребитель - один `Task`: `Await foreach` по каналу, каждая операция в
  `Task.Run` (I/O), результат (успех/исключение) - в `FileOpResult`.
- Завершение маршалится на UI (`SynchronizationContext`, захваченный при создании):
  обработчик обновляет статус и, при ошибке, откатывает мутацию списка
  (`InsertFileIntoList(op.Source, op.ListIndex)` из С2).
- `PoMove`/`Undo`/DEL в modern-ветке: `file_Op_Queue.Enqueue(op)` - без `IsBusy`,
  без отказа. Оптимистичная мутация списка (как в С2) - сразу.
- `FormClosing` в modern: `Await file_Op_Queue.DrainAsync(TimeSpan.FromSeconds(5))`
  вместо ожидания сигнала (Б-36 остаётся действующим для net48).
- Статус: при `PendingCount > 1` показывать «в очереди: N» - пользователь видит, что
  хвост операций жив.

## 2. У-06: корзина и undo удаления

[FileManager.vb:110-114](../../src/FileManager.vb). `File.Delete` необратим, а
D/Del - рабочая клавиша рядом с цифрами переноса, причём подтверждение отключаемо
(`Is_no_request_before_file_operation`). Одно промахнувшееся нажатие уничтожает файл.

```vb
#If Not NETFRAMEWORK Then
''' <summary>Modern: удаление - в корзину. Ошибка пользователя в сортировщике
''' обязана быть обратимой; UIOption.OnlyErrorDialogs - без лишних диалогов.</summary>
Public Sub DeleteFile(filePath As String)
    If Not File.Exists(filePath) Then Return
    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(filePath,
        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
End Sub
#End If
```

- Текст подтверждения ([MediaLoading.vb:198](../../src/Main_Form.MediaLoading.vb))
  в modern смягчить: «удалить файл 'X' в корзину?» (нынешний «безвозвратно.. обратно
  его уже не уговорить» перестаёт быть правдой - и это хорошо).
- Удаление записывается в стек undo (У-08) как `FileOpKind.Delete` с `Source`; откат -
  восстановление из корзины через `Shell32` (`Verb "restore"`) либо, если это
  окажется хрупким, - через `IFileOperation`. Если восстановление не реализуем в
  этой стадии - undo удаления просто открывает корзину и сообщает об этом; решение
  зафиксировать в `PROGRESS_DOTNET10_MODERN_BUILD.md`.
- Сетевые пути корзины не имеют - `RecycleOption` там молча удаляет; для UNC-путей
  сохранить прежнее предупреждение о безвозвратности.

## 3. У-07: коллизии имён

При сортировке фотосессий одноимённые `IMG_0001.jpg` с разных карт - норма. Сейчас
второй перенос падает `IOException` → `MsgBox E014`, а в async-режиме файл уже
выброшен из списка (Б-03).

```vb
''' <summary>Свободное имя в стиле проводника: "IMG_0001 (2).jpg". Возвращённый
''' путь ОБЯЗАН попасть в history (undo должен вернуть именно тот файл, который
''' реально создан).</summary>
Private Function ResolveDestinationCollision(dest_Path As String) As String
    If Not File.Exists(dest_Path) Then Return dest_Path
    Dim dir_Path = Path.GetDirectoryName(dest_Path)
    Dim base_Name = Path.GetFileNameWithoutExtension(dest_Path)
    Dim ext = Path.GetExtension(dest_Path)
    For n = 2 To 9999
        Dim candidate = Path.Combine(dir_Path, base_Name & " (" & n.ToString() & ")" & ext)
        If Not File.Exists(candidate) Then Return candidate
    Next
    Return dest_Path   ' пусть операция честно упадёт
End Function
```

Вызов - в `PoMove` перед постановкой `FileOp` (тогда `op.Destination` уже
окончательный и его же увидит `history`/undo). В статусе показывать фактическое имя,
если оно отличается от исходного. Опционально (не в этой стадии): при совпадении
размера+времени предлагать пропуск вместо копии.

## 4. У-08: стек undo

[Main_Form.vb:210-211](../../src/Main_Form.vb) - история это два строковых поля,
которые перетирает каждая операция: разложил серию, понял, что последние пять ушли
не туда - вернуть можно один.

```vb
Private ReadOnly undo_Stack As New Stack(Of FileOp)   ' FileOp уже содержит ListIndex
Private Const max_Undo_Depth As Integer = 50
```

- Каждая **успешно завершённая** операция (в обработчике `OpCompleted`) кладёт себя
  в стек; неуспешная - не кладёт (сейчас `history_*` заполняются до факта - Б-03).
- U: снять вершину, построить обратную операцию (`Move` → обратный `Move`,
  `Copy` → удаление копии, `Delete` → восстановление из корзины, `Rename` → обратный
  rename) и поставить её в ту же очередь; вставка в список - по `op.ListIndex` с
  зажимом (`InsertFileIntoList`, С2).
- Поля `history_Source_File_Name`/`history_Destination_File_Name` в modern больше не
  нужны - остаются только под `#If NETFRAMEWORK`.
- Статус при пустом стеке - прежний «Нет истории о переносе».

## 5. Приёмка

1. **Темп нажатий**: включить «независимый поток», разложить 30 файлов хоткеями в
   максимально быстром темпе (в т.ч. на сетевую папку) → все 30 доехали, ни одного
   MsgBox, счётчик и содержимое папок совпадают; в статусе видно «в очереди: N».
2. **Коллизия**: перенести `IMG_0001.jpg` в папку, где такой уже есть → создан
   `IMG_0001 (2).jpg`, статус показывает фактическое имя; U возвращает **именно его**.
3. **Корзина**: DEL → файл в корзине Windows; U → файл вернулся в папку и в список
   (либо, если восстановление отложено, - честное сообщение с открытием корзины).
4. **Многоуровневый undo**: разложить 5 файлов по разным хоткеям → 5 раз U → все
   пять вернулись, каждый на своё место в списке; шестое U - «Нет истории».
5. **Ошибка в очереди**: занять файл-приёмник эксклюзивно (открыть в другой
   программе) → перенос → откат: файл остался в списке и на экране; следующая
   операция в очереди выполняется (очередь не встала).
6. **Закрытие с непустой очередью**: поставить 5 переносов больших файлов и закрыть
   окно → окно закрывается после завершения (≤5 с) без крэшей; незавершённые
   операции не оставляют полуфайлов.
7. **net48 не изменился**: в x86 действует поведение С2 (гвард `IsBusy`,
   безвозвратное удаление, одноуровневый undo).

## 6. Риски

- **Корзина в MSIX-контейнере**: проверить, что `FileSystem.DeleteFile` с
  `SendToRecycleBin` работает в упакованной сборке (Store) - если нет, оставить
  прямое удаление для packaged-режима и сказать об этом в подтверждении.
- Оптимистичная мутация списка + очередь: при откате N-й операции индексы уже
  сдвинуты последующими - `InsertFileIntoList` зажимает, но порядок может отличаться
  от исходного. Приёмка 4 проверяет это явно.
- `Microsoft.VisualBasic.FileIO` в .NET 10 доступен - убедиться, что не тянет
  лишних зависимостей в single-file publish.
- Стек undo и `media_Cache` (С7): восстановленный файл обязан инвалидировать запись
  кэша по своему пути.

## 7. Готово, когда

- [ ] У-02, У-06, У-07, У-08 реализованы за швом; `history_*` в modern-пути нет.
- [ ] `.\build.ps1`; приёмка 1-7; x86 - без регрессий.
- [ ] Решение по восстановлению из корзины зафиксировано в `PROGRESS_DOTNET10_MODERN_BUILD.md`.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
