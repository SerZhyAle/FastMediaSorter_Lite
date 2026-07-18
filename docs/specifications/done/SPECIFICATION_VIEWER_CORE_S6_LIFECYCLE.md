# Тактическая спецификация С6: состояние, настройки, жизненный цикл

Статус: реализовано 2026-07-16 (обе сборки; пересылка между экземплярами проверена живым прогоном)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Сборки: **обе**
Объём: 10 багов, 2 файла

## 0. Цель

Приложение должно честно запускаться, честно закрываться и восстанавливать ровно то
состояние, в котором его оставили - на любом мониторе и с любым содержимым реестра.

| Ид | П | Что |
|---|---|---|
| Б-36 | П3 | Закрытие во время фоновой операции: 5-6 с замирания и работа с уже освобождёнными объектами |
| Б-37 | П3 | Позиция окна не восстанавливается на больших/мульти-мониторных конфигурациях; перепутаны пороги |
| Б-38 | П3 | `e.Cancel=True` даже когда аргумент никому не доставлен - файл молча не открывается |
| Б-39 | П3 | `LastCounter` не сохраняется при индексе 0 - чужой индекс из прошлой папки |
| Б-40 | П4 | `pending_Unlock_Timer` перехватывает просмотр спустя до 45 с; удалённый файл считает занятым |
| Б-41 | П4 | Окно гонки одноэкземплярности: мьютекс создаётся в `Form1_Load` |
| Б-42 | П4 | Catch в `ProcessArgument` затирает сессию |
| Б-43 | П4 | `-noback` вырезается из любого места пути |
| Б-44 | П4 | `SortDir` без верхней границы - крэш в `Form1_Load` |
| Б-45 | П4 | Размер файла: деление на 1000, подпись KiB/MiB |

## 1. Б-36: закрытие без ложного ожидания

[Main_Form.Lifecycle.vb:659-677](../../src/Main_Form.Lifecycle.vb). `IsBusy` сбрасывается
колбэком, который постится в **UI-поток**, а `FormClosing` крутит `Thread.Sleep(10)`
без прокачки сообщений: колбэк не доставится никогда, оба цикла всегда выгорают по
полному таймауту (1 с + 5 с), после чего ресурсы освобождаются под работающим воркером.

Ждать собственный сигнал воркера, а не `IsBusy`:

```vb
' Выставляется в Finally блока DoWork - в отличие от IsBusy, он не зависит от
' доставки колбэка в заблокированный UI-поток (Б-36).
Private ReadOnly bgworker_Done As New ManualResetEventSlim(True)
Private ReadOnly fileop_Done As New ManualResetEventSlim(True)
```

- `BgWorker_DoWork` / `FileOperationWorker_DoWork`: `bgworker_Done.Reset()` в начале
  (точнее - при постановке, на UI-потоке, чтобы не было гонки), `Try/Finally` с
  `bgworker_Done.Set()` в `Finally`.
- `FormClosing`: `BgWorker.CancelAsync()` + `bgworker_Done.Wait(1000)`;
  `FileOperationWorker` - `fileop_Done.Wait(5000)`. Ожидание становится настоящим и в
  типичном случае мгновенным.
- В связке с Б-15 (`Exit Sub` после `e.Cancel`, стадия
  [С3](SPECIFICATION_VIEWER_CORE_S3_NAVIGATION.md)) отмена наконец что-то отменяет.

> В modern после [С7](SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md)/[С8](SPECIFICATION_VIEWER_CORE_S8_MODERN_FILEOPS.md)
> это место переедет на `CancellationToken` + `await`; сигналы останутся для net48.

## 2. Б-37: геометрия окна

Константы эпохи 1366x768 ([Main_Form.vb:67-72](../../src/Main_Form.vb):
`main_form_position_Limit_Top = 720`, `..._Left = 1000`) отвергают любую позицию на
современном мониторе; отрицательные координаты (второй монитор слева/сверху) не
сохраняются и не принимаются. В строках 643-644 вдобавок перепутаны пороги: высота
сверяется с `Limit_Width_Low`, ширина - с `Limit_Height_Low`.

Загрузка ([Lifecycle.vb:550-555](../../src/Main_Form.Lifecycle.vb)):

```vb
' Прямоугольник валиден, если он ХОТЬ ЧАСТЬЮ попадает на существующий рабочий стол
' (мульти-монитор даёт отрицательные координаты - жёсткие лимиты 720/1000 отвергали
' и их, и любую позицию на мониторе шире 1366).
Dim saved As New Rectangle(app_Left_Int, app_Top_Int, app_Width_Int, app_Height_Int)
If app_Width_Int < main_form_position_Limit_Width_Low OrElse app_Width_Int > main_form_position_Limit_Width OrElse
   app_Height_Int < main_form_position_Limit_Height_Low OrElse app_Height_Int > main_form_position_Limit_Height OrElse
   Not SystemInformation.VirtualScreen.IntersectsWith(saved) Then
    saved = New Rectangle(first_run_left, first_run_top, first_run_width, first_run_height)
End If
Me.SetBounds(saved.X, saved.Y, saved.Width, saved.Height)
```

Сохранение (строки 641-644): убрать `>= 0` для Top/Left (отрицательные координаты
легальны), пороги Width/Height поменять местами (`Me.Height >= main_form_position_Limit_Height_Low`,
`Me.Width >= main_form_position_Limit_Width_Low`). Константы `main_form_position_Limit_Top/Left`
удалить - их работу делает `VirtualScreen`.

## 3. Б-38 + Б-41: одноэкземплярность

### 3.1 Мьютекс - в `Startup`, а не в `Form1_Load` (Б-41)

Проверка `TryOpenExisting` живёт в [Application_Events.vb:146](../../src/Application_Events.vb),
а сам мьютекс создаётся в `InitNew` из `Form1_Load` - между стартом процесса и
загрузкой формы (сотни мс у self-contained .NET 10) мьютекса нет: два быстрых
открытия (или пара LITE + x86) дают два экземпляра, наперегонки пишущих один куст
реестра при выходе. Плюс результат создания (`is_New_Instance_Created`) не проверяется.

- Создавать мьютекс в `MyApplication_Startup` **до** логики пересылки:
  `mutex = New Mutex(True, app_Mutex_Name, created_New)`; если `created_New = False` -
  идти по ветке пересылки. `TryOpenExisting` больше не нужен.
- Из `InitNew` создание убрать; ссылку на мьютекс держать там же, где сейчас
  (`Private Shared mutex As Mutex` в `Main_Form`) - имя `FastMediaSorterSingleInstanceMutex`
  **заморожено**, не трогать.

### 3.2 `e.Cancel` только по факту доставки (Б-38)

```vb
Dim delivered As Boolean = False
For Each proc In GetRunningViewerProcesses(current_Id)
    ...
    For Each target_Handle In target_Handles
        ' SendMessageTimeout: SendMessage синхронен и без таймаута - зависшее окно
        ' получателя вешало НОВЫЙ процесс навсегда (Б-38).
        Dim result As IntPtr
        If SendMessageTimeout(target_Handle, WM_COPYDATA_LOCAL, IntPtr.Zero, cds,
                              SMTO_ABORTIFHUNG, 3000, result) <> IntPtr.Zero Then delivered = True
    Next
    If delivered Then Exit For
Next

' Отменять запуск можно ТОЛЬКО если аргумент реально доставлен: иначе (переименованный
' exe, окно ещё не создано, получатель завис) пользователь не получал ни окна, ни
' файла, ни сообщения.
If delivered Then
    e.Cancel = True
    Return
End If
' Не доставили - продолжаем обычный запуск.
```

Объявление `SendMessageTimeout` добавить рядом с `SendMessageCopyData`
(`SMTO_ABORTIFHUNG = &H2`).

## 4. Точечные правки

- **Б-39**: [Lifecycle.vb:591](../../src/Main_Form.Lifecycle.vb) - убрать условие
  `If Not current_File_Index = 0`, сохранять всегда (ноль - валидное состояние; иначе
  папка, закрытая на первом файле, при старте открывается на индексе из прошлой папки).
- **Б-40**: `pending_Unlock_Path`/таймер сбрасывать при любой явной смене файла или
  папки (в начале `ProcessArgument` и в `UpdateFileIndexAndList`); в тике различать
  исключения:
  ```vb
  Try
      File.GetAttributes(pending_Unlock_Path)
  Catch ex As FileNotFoundException
      CancelPendingUnlock(If(Is_Russian_Language, "Файл удалён: ", "File deleted: "))
      Return
  Catch ex As DirectoryNotFoundException
      CancelPendingUnlock(...) : Return
  Catch
      Return   ' занят - ждём следующий тик
  End Try
  ```
- **Б-42**: в `Catch` `ProcessArgument` ([Lifecycle.vb:356-362](../../src/Main_Form.Lifecycle.vb))
  **не трогать** `Current_Folder_Path`/комбобокс/`Is_No_Background_Tasks`: ошибочен
  аргумент, а не сессия. Показать `lbl_Status` «Не удалось открыть: ..» и выйти.
- **Б-43**: флаг `-noback` разбирать по токенам - `My.Application.CommandLineArgs`
  поэлементно (`String.Equals(arg, "-noback", OrdinalIgnoreCase)`), путь собирать из
  остальных аргументов. Убрать `Regex.Replace` по всей строке (сейчас
  `C:\pics\photo-noback.jpg` невозможно открыть в принципе). Затрагивает и
  `Form1_Load` (строка 502-504: `String.Join(" ", CommandLineArgs)`), и
  `MyApplication_Startup`/`StartupNextInstance`, которые склеивают строку так же.
- **Б-44**: [Lifecycle.vb:440-444](../../src/Main_Form.Lifecycle.vb) -
  `If sort_Direction_Index < 0 OrElse sort_Direction_Index >= cmbox_Sort.Items.Count Then sort_Direction_Index = 0`.
- **Б-45**: [Main_Form.FileScanning.vb:54-60](../../src/Main_Form.FileScanning.vb) -
  выбрать одну систему. Рекомендация: делить на 1024/1048576 и оставить KiB/MiB
  (совпадёт с Проводником по числам, подпись честная).

## 5. Приёмка

1. **Закрытие при операции**: включить «независимый поток», начать перенос большого
   файла на медленный диск, сразу закрыть окно → закрывается без 5-секундного
   замирания; в `current.log` нет `ObjectDisposedException`.
2. **Мультимонитор**: перенести окно на второй монитор (в т.ч. расположенный слева →
   отрицательный `Left`), закрыть, открыть → окно там же. То же для монитора 2560x1440
   с окном в правой половине.
3. **Битый реестр**: записать `SortDir = 42` в `HKCU\Software\SZA\FastMediaSorter` →
   приложение стартует нормально (сортировка «abc»), без крэша и без пропуска
   инициализации (проверить, что папка и позиция окна восстановились) - в **обоих** exe.
4. **LastCounter**: папка A → уйти на файл 50 → закрыть; открыть, выбрать папку B →
   остаться на первом файле → закрыть → открыть → показан **первый** файл папки B.
5. **`-noback` в пути**: создать `C:\pics\photo-noback.jpg`, открыть двойным кликом →
   файл открывается, фоновые задачи не отключены (в статусе нет «-NoBack»).
6. **Плохой аргумент**: листать папку X, прислать в неё drag-drop файл со слишком
   длинным путём → статус об ошибке, **листание папки X продолжает работать**.
7. **Гонка запуска**: выделить в Проводнике 2 файла и открыть их одновременно
   (Enter) → ровно один экземпляр, открыт один из файлов; повторить парой
   LITE.exe + x86.exe.
8. **Ожидание разблокировки**: начать копировать большой файл в папку, открыть его
   недокачанным → «Файл занят, ждём разблокировки»; уйти в другую папку → по
   завершении копирования просмотр **не перескакивает** на тот файл. Удалить
   недокачанный файл во время ожидания → «Файл удалён», ожидание прекращено.

## 6. Риски

- **Б-41 трогает одноэкземплярность - самый чувствительный инвариант проекта.**
  Имя мьютекса, поиск обоих имён процессов и UTF-8 в `WM_COPYDATA` остаются как есть;
  меняется только момент создания мьютекса. Сценарий 7 обязателен в обеих сборках,
  включая случай «первый экземпляр ещё грузится».
- Б-43 меняет разбор командной строки в трёх местах - проверить сценарий «файл с
  пробелами в пути» (`String.Join(" ", args)` исторически лечил именно его; при
  переходе на токены путь = первый не-флаговый аргумент, кавычки уже сняты Windows).
- Б-37 удаляет две константы - `grep` перед удалением.

## 7. Готово, когда

- [ ] 10 багов закрыты; `grep main_form_position_Limit_Top src/` пуст.
- [ ] `.\build.ps1`; приёмка 1-8 в обоих exe.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
