# Тактическая спецификация С1: аварийные фиксы потери данных

Статус: реализовано 2026-07-16 (обе сборки; Б-01 закреплён юнит-тестами FileManagerRenameTests)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Сборки: **обе** (правки в общем коде)
Объём: 3 бага, ~4 файла, один цикл «сборка → ручная проверка → коммит»

## 0. Цель

Убрать три сценария, в которых пользователь **теряет файл или оперирует не тем
файлом, что видит**. Стадия намеренно узкая и без рефакторингов: её можно
выпустить сразу, не дожидаясь остальных стадий.

| Ид | Что | Файлы |
|---|---|---|
| Б-01 | Rename создаёт `photo.jpg.jpg` и выбрасывает файл из списка | [FileManager.vb](../../src/FileManager.vb), [Main_Form.FileOperations.vb](../../src/Main_Form.FileOperations.vb) |
| Б-02 | Гвард `IsBusy` глотает показ после фоновой операции - чёрный экран | [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb) |
| Б-11 | HEIC/AVIF/SVG не отображаются - DEL удаляет невидимый файл | [Main_Form.MediaLoading.vb](../../src/Main_Form.MediaLoading.vb), [Main_Form.FileAssociation.vb](../../src/Main_Form.FileAssociation.vb) |

## 1. Б-01: переименование

### 1.1 `FileManager.RenameFile` ([FileManager.vb:98-105](../../src/FileManager.vb))

Контракт меняется на «вызывающий передаёт готовое имя С расширением» - именно так
его и зовёт единственный вызывающий (`RenameCurrentFile`, строка 39). Второе
дописывание расширения убрать:

```vb
''' <summary>Переименовывает файл. newFileNameWithExtension - готовое имя файла
''' вместе с расширением; функция ничего к нему не дописывает и возвращает
''' фактический новый полный путь.</summary>
Public Function RenameFile(currentFileName As String, newFileNameWithExtension As String) As String
    Dim directory As String = Path.GetDirectoryName(currentFileName)
    Dim newFullPath As String = Path.Combine(directory, newFileNameWithExtension)
    If String.Equals(newFullPath, currentFileName, StringComparison.OrdinalIgnoreCase) Then Return currentFileName
    File.Move(currentFileName, newFullPath)
    Return newFullPath
End Function
```

Сверить `grep RenameFile src/` - вызывающий ровно один; если появится второй,
он обязан передавать имя с расширением.

### 1.2 `RenameCurrentFile` ([Main_Form.FileOperations.vb:14-56](../../src/Main_Form.FileOperations.vb))

- Класть в список и в `Current_File_Name` **путь, возвращённый** `RenameFile`, а не
  собранный самостоятельно `new_File_Full_Path` (сейчас они расходятся - в этом и баг):
  ```vb
  Dim renamed_Path As String = RenameFile(Current_File_Name, new_File_Name & current_File_Extension)
  If is_Files_Array_Active Then files_Array(current_File_Index) = renamed_Path Else files_List(current_File_Index) = renamed_Path
  Current_File_Name = renamed_Path
  ```
- **Попутно** (тот же отказ, та же функция): перед `RenameFile` проверять коллизию -
  `If File.Exists(new_File_Full_Path)` → статус «! Файл с таким именем уже есть» /
  «! A file with that name already exists» и `Return`, без `File.Move` (иначе
  IOException → голый `MsgBox E011`).
- Освобождение играющего видео перед `File.Move` - **не здесь**: это Б-06, стадия
  [С2](SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md) (там появляется общий хелпер).

## 2. Б-02: гвард `FileOperationWorker.IsBusy`

[Main_Form.MediaLoading.vb:30-33](../../src/Main_Form.MediaLoading.vb). Гвард обязан
касаться только режимов, которые сами **запускают** файловую операцию (сейчас такой
ровно один - `"DeleteFile"`; `PoMove`/`Undo` зовут воркер напрямую, минуя
`ReadShowMediaFile`). Режимы показа блокировать нельзя: список к этому моменту уже
мутирован на UI-потоке, и именно их блокировка оставляет чёрный экран после move и
не даёт copy пролистнуть.

```vb
' Ждать воркера обязаны только режимы, сами порождающие файловую операцию:
' список к моменту показа уже мутирован на UI-потоке, а блокировка показа
' оставляла пользователя с чёрным экраном после переноса (w0340).
If FileOperationWorker.IsBusy AndAlso read_Mode_Type = "DeleteFile" Then
    lbl_Status.Text = If(Is_Russian_Language, "!Ждите.. предыдущая операция ещё выполняется", "!Wait.. previous operation still running")
    Debug.WriteLine(Now().ToString("HH:mm:ss.ffff") & " w0340: DeleteFile skiped while FileOperationWorker")
    Exit Sub
End If
```

Строковый литерал `"DeleteFile"` - временно: в
[С3](SPECIFICATION_VIEWER_CORE_S3_NAVIGATION.md) режимы переезжают на константы.
Проверка `IsBusy` **перед** `RunWorkerAsync` в `PoMove`/`Undo` - это Б-04, стадия С2;
до неё поведение DEL при занятом воркере не меняется (как и было - отказ), поэтому
С1 самодостаточна.

## 3. Б-11: форматы без декодера

Решение владельца по умолчанию для этой стадии: **файлы остаются в списке**
(папку с iPhone-фото нужно уметь разложить хоткеями), но состояние показывается
честно, а ассоциации на форматы без декодера не регистрируются.

### 3.1 Ветка Else диспетчера ([Main_Form.MediaLoading.vb:910-912](../../src/Main_Form.MediaLoading.vb))

Вместо `Debug.WriteLine("w1045: No selected control to show!?")`:

```vb
Else
    ' Формат в списке (сканируется и сортируется), но декодера для него нет -
    ' честно гасим поверхность, иначе на экране остаётся ПРЕДЫДУЩАЯ картинка,
    ' а DEL/перенос уходят в файл, которого пользователь не видит.
    ShowUnsupportedFormat(Current_File_Name)
End Sub
```

Новый хелпер (рядом, в том же файле):

```vb
''' <summary>Гасит медиаповерхность и показывает статус для файла, который есть
''' в списке, но не имеет пути отображения (heic/avif/svg). Образы НЕ диспозятся -
''' только скрываются: диспоз показанной картинки - отдельный класс отказов
''' (см. Б-24).</summary>
Private Sub ShowUnsupportedFormat(file_Path As String)
    StopGifLoopPlayback()
    If is_Vlc_Playing Then StopVlcPlayback()
    is_PictureBox1_Visible = False
    is_PictureBox2_Visible = False
    is_WebBrowser_Visible = False
    UpdateControlVisibility()
    current_Loaded_File_Name = file_Path
    lbl_Status.Text = If(Is_Russian_Language,
                         "Формат не поддерживается: " & Path.GetFileName(file_Path),
                         "Unsupported format: " & Path.GetFileName(file_Path))
End Sub
```

Предзагрузка следующего файла (блок `BgWorker` ниже по коду) при этом продолжает
работать - ветка Else внутри того же `Try`.

### 3.2 Ассоциации ([Main_Form.FileAssociation.vb:135-139](../../src/Main_Form.FileAssociation.vb))

Убрать `".heic", ".avif", ".svg"` из `all_Image_Extensions`: приложение не может их
показать, а регистрация делает его обработчиком по умолчанию - двойной клик даёт
пустое окно. `.webp` остаётся (декодер есть в обеих сборках).

### 3.3 Что НЕ делаем в этой стадии

Настоящий декодер HEIC/AVIF/SVG для modern (Magick.NET / Windows HEIF Extensions /
растеризация SVG) - **требует решения владельца по зависимости** (вес пакета,
лицензия) и в стадии не запланирован; см. раздел «Открытые вопросы» родительской
спецификации. Диалог «Выбрать файл» фильтр не меняет: выбор heic даст честный
статус вместо пустого окна.

## 4. Приёмка

1. **Rename**: файл `photo.jpg` → переименовать в `photo2` → на диске ровно
   `photo2.jpg` (не `photo2.jpg.jpg`), файл остался в списке, показан, счётчик не
   изменился. Повторно переименовать в имя существующего соседа → статус «файл с
   таким именем уже есть», без MsgBox, файл на месте.
2. **Фоновый move** (галочка «независимый поток для файловых операций» в
   Настройки ▸ Файлы и система): встать на файл в середине папки, нажать хоткей
   переноса → сразу показывается следующий файл (не чёрный экран), счётчик -1.
3. **Фоновый copy**: тот же хоткей в режиме копирования → просмотр пролистнул на
   следующий файл (как в синхронном режиме).
4. **HEIC**: папка с jpg+heic, листать на heic → экран гаснет, статус «Формат не
   поддерживается: IMG_0001.heic», счётчик показывает позицию heic; DEL удаляет
   именно heic; листание дальше показывает следующий jpg.
5. **Ассоциации**: Настройки ▸ «Сделать программой по умолчанию для изображений»
   → в `HKCU\Software\Classes` нет ProgID `FastMediaSorter.heic/.avif/.svg`.
6. **Оба exe**: `.\build.ps1` собирает обе сборки; сценарии 1-4 проходят и в
   `FastMediaSorter_x86.exe`.

## 5. Риски

- Сужение гварда `IsBusy` открывает показ во время фоновой операции - это и есть
  цель, но список в этот момент уже мутирован (Б-03 из С2 ещё не сделан): при
  **ошибке** фоновой операции файл останется выброшенным из списка. Поведение не
  хуже текущего (сейчас он выброшен и экран чёрный), но окончательно чинится в С2.
- `ShowUnsupportedFormat` скрывает боксы, не диспозя образы - осознанно: диспоз
  показанного образа лечится в С2 (Б-24) единым хелпером.

## 6. Готово, когда

- [ ] Три бага исправлены, диффы не выходят за перечисленные файлы.
- [ ] `.\build.ps1` - обе сборки без новых предупреждений (`WarningsAsErrors`).
- [ ] Приёмка 1-6 пройдена вручную в обоих exe.
- [ ] `CHANGELOG.md` ▸ [Unreleased]: rename fix, «чёрный экран после переноса»,
      честный статус для heic/avif/svg (по-английски).
- [ ] Коммит одной темой: `fix(viewer-core): rename extension, post-op refresh, unsupported formats`.
