# Тактическая спецификация С7 (modern): конвейер декодирования

Статус: план (не начато)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественники: [С3](SPECIFICATION_VIEWER_CORE_S3_NAVIGATION.md) (транзакционный префетч), [С4](SPECIFICATION_VIEWER_CORE_S4_PLAYBACK.md) (`media_Generation`)
Сборки: **только .NET 10 mainline** (за швом `#If Not NETFRAMEWORK`; net48 остаётся на `BackgroundWorker`)
Объём: У-01, У-13, У-15 - первая архитектурная стадия

## 0. Цель

Сделать листание мгновенным в **обе** стороны и убрать декодирование с UI-потока.
Сегодня кэшируется ровно один файл вперёд: любое «назад», прыжки ±10/±100 и промахи
при быстром листании декодируют синхронно на UI-потоке
(`File.ReadAllBytes` + `Image.FromStream` + EXIF), а `BackgroundWorker` без отмены
продолжает грузить файлы, мимо которых пользователь давно пролистал.

| Ид | Ценность | Что |
|---|---|---|
| У-01 | ★★★ | Асинхронный конвейер: отмена, префетч в обе стороны, LRU prev/current/next |
| У-13 | ★ | WEBP: не удерживать исходный `MemoryStream` (декодер отдаёт отвязанный Bitmap) |
| У-15 | ★ | Анализ цвета фона: `LockBits` вместо `GetPixel` |

## 1. Архитектура

Новый файл `src/Modern/MediaCache.vb` (или `src/MediaCache.vb` целиком под
`#If Not NETFRAMEWORK`) - слой, о котором `Main_Form` знает ровно три вещи:
запросить, получить, отменить.

```vb
''' <summary>Декодированное изображение вместе с тем, ЧТО именно декодировано.
''' Ключ - путь + время записи: перезаписанный снаружи файл обязан перечитаться.</summary>
Friend NotInheritable Class MediaEntry
    Public Property Path As String
    Public Property WriteTimeUtc As DateTime
    Public Property Picture As Image
    Public Property Data As IO.MemoryStream   ' Nothing, если декодер отдал отвязанный образ (У-13)
End Class
```

Публичный контракт:

```vb
''' <summary>Готовое изображение для path, если оно уже в кэше и не устарело.</summary>
Friend Function TryGet(path As String) As MediaEntry

''' <summary>Декодировать path (если ещё не декодирован) и вернуть результат.
''' Отменяется, когда пользователь уходит дальше.</summary>
Friend Function GetAsync(path As String, token As CancellationToken) As Task(Of MediaEntry)

''' <summary>Фоново подготовить соседей вокруг текущей позиции. Вызывается после
''' каждого показа; предыдущие незавершённые подготовки отменяются.</summary>
Friend Sub Prefetch(paths As IEnumerable(Of String))
```

Внутри:
- `Channel(Of DecodeRequest)` (unbounded, single consumer) - очередь заявок;
- декод в `Task.Run` через существующий шов `LoadImageWithStream` /
  `ImageDecoderProvider` - **декодеры не трогаем**;
- `CancellationTokenSource` на поколение навигации: новая навигация → `Cancel()`
  предыдущего поколения (ImageSharp принимает токен; для GDI+-путей проверка токена
  между файлами - декод одного файла не прерываем);
- LRU на 5 записей (`prev-1, prev, current, next, next+1`), вытеснение с `Dispose`
  образа и потока.

## 2. Что меняется в `Main_Form`

`UpdateCurrentFileAndDisplay` (ветка изображения) вместо нынешней связки
«bgWorker_Result / current_Second_File_Name / is_First_Picture_Box_Need_To_Be_Cached»:

```vb
#If Not NETFRAMEWORK Then
    Await ShowImageFromCacheAsync(Current_File_Name, media_Generation)
#Else
    LoadStandardImageInPictureBox()
#End If
```

```vb
''' <summary>Показ изображения через кэш. Единственный источник истины - словарь
''' "путь → образ": устаревший результат невозможен по конструкции (раньше решение
''' принималось по паре флагов, обновляемых из разных потоков - Б-13).</summary>
Private Async Function ShowImageFromCacheAsync(path As String, generation As Integer) As Task
    Dim entry As MediaEntry = media_Cache.TryGet(path)
    If entry Is Nothing Then
        ' Промах: показать текущий кадр как есть (не мигать), декодировать асинхронно.
        lbl_Status.Text = If(Is_Russian_Language, "загрузка..", "loading..")
        entry = Await media_Cache.GetAsync(path, CurrentToken())
        If entry Is Nothing OrElse generation <> media_Generation Then Return   ' пользователь ушёл дальше
    End If

    SwapMediaSurface(entry)       ' та же двойная буферизация: положить в невидимый бокс, показать его
    media_Cache.Prefetch(NeighbourPaths())   ' prev/next вокруг новой позиции
End Function
```

**Двойная буферизация сохраняется** (инвариант родительской спецификации): меняется
только доставка образа. `SwapMediaSurface` кладёт `entry.Picture` в неактивный бокс,
переключает видимость (как сейчас в `LoadStandardImageInPictureBox`), запускает
`StartGifLoopPlayback`. Владелец образа - кэш; боксам образ **одалживается**, поэтому:

- `SwapMediaSurface` **не диспозит** предыдущий `Picture_Box_X.Image` (его вытеснит
  LRU) - вместо этого перед вытеснением кэш проверяет, что образ не показан;
- пути файловых операций (`ReleaseActiveMedia` из [С2](SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md))
  в modern зовут `media_Cache.Evict(path)` вместо прямого `Dispose` - иначе
  удаляемый/переносимый файл останется залоченным своим потоком.

`NeighbourPaths()` - `current ± 1` (и `± 2` при быстром листании в одну сторону -
направление уже известно из `last_Nav_Direction`, введённого в С3); в random-режиме
префетч не имеет смысла - возвращать пусто (это же снимает костыль Б-27).

## 3. У-13: не держать байты WEBP

[FileManager.vb:23-27, 53](../../src/FileManager.vb): для GDI+-форматов
`MemoryStream` держать обязательно (ленивый декод), а `ModernImageSharpDecoder`
возвращает **отвязанный** `Bitmap` - исходные байты никому не нужны, но живут в
`pictureBoxX_Stream` до следующей перезаписи бокса. Для 30-МБ анимированного webp в
памяти одновременно: байты файла (зря) + транскодированный GIF-стрим + растр, и так
на обоих боксах плюс префетч.

- В `IImageDecoder` добавить `ReadOnly Property ImageOwnsData As Boolean`
  (`True` для `ModernImageSharpDecoder`, `False` для `LegacyWicImageDecoder`).
- `LoadImageWithStream`: если `ImageOwnsData` - `ms.Dispose()` и вернуть
  `Tuple.Create(nextImage, CType(Nothing, IO.MemoryStream))`. Вызывающие уже готовы
  к `Nothing` (везде `?.Dispose()`).
- `MediaEntry.Data` при этом просто `Nothing` - кэш меньше на размер файла.

## 4. У-15: `LockBits` вместо `GetPixel`

Динамическая схема фона ([MediaLoading.vb:641-764](../../src/Main_Form.MediaLoading.vb))
и перспектива ([Main_Form.PerspectiveBackground.vb](../../src/Main_Form.PerspectiveBackground.vb))
опрашивают пиксели по одному на **каждом** показе файла: на 4K-изображении это
заметная доля времени перехода.

- Один `LockBits` (`ImageLockMode.ReadOnly`, `Format32bppArgb`) на кадр, чтение из
  `Marshal.Copy`-буфера; `Try/Catch` вокруг остаётся (GDI+ транзиентно падает -
  инвариант «анализ не должен ронять показ» сохраняется).
- Вынести в общий хелпер `EdgeSampler`, которым пользуются оба места.
- Делать **после** У-01: в конвейере анализ можно перенести в фоновую задачу вместе
  с декодом (цвет фона и края - функция от изображения, не от UI).

## 5. Приёмка

1. **Листание назад**: папка из 200 фото 24 Мп на SMB-шаре → зажать Left → переходы
   такие же мгновенные, как вперёд; окно не «белеет» и не помечается «Не отвечает».
2. **Промах кэша**: прыжок Ctrl+клик (+100) → статус «загрузка..», предыдущий кадр
   остаётся на экране до готовности нового (не мигает), после - новый кадр.
3. **Отмена**: зажать Right на 3 с и отпустить → в диспетчере задач нет всплеска
   чтения после отпускания (устаревшие декоды отменены); показан ровно тот файл,
   что в счётчике.
4. **Память**: пролистать 500 файлов подряд → рабочий набор не растёт линейно
   (LRU держит ≤5 образов); анимированный webp 30 МБ - в памяти нет копии байтов
   файла (У-13, проверить дампом/профайлером).
5. **Операции над файлом из кэша**: DEL/перенос показанного файла → операция
   проходит (файл не залочен потоком кэша), кэш инвалидирован.
6. **Изменение файла снаружи**: перезаписать показанный jpg другим содержимым →
   вернуться на него → показана новая картинка (ключ учитывает `LastWriteTime`).
7. **net48 не изменился**: `FastMediaSorter_x86.exe` работает на прежнем
   `BackgroundWorker`-пути; сценарии С3 (приёмка 1, 8) там проходят.

## 6. Риски

- **Владение образом** - главный риск: кэш и `PictureBox` теперь ссылаются на один
  `Image`. Правило: диспозит **только** кэш (LRU/Evict), и только если образ не
  присвоен ни одному видимому боксу. Нарушение = «Parameter is not valid» в `OnPaint`.
- GIF: `StartGifLoopPlayback` держит ссылку `gif_Restart_Image_Ref` на образ из кэша -
  при вытеснении обязателен `StopGifLoopPlayback()`.
- Поворот (`RotateActiveImage`, R/Shift+R) мутирует образ **на месте** - т.е. мутирует
  кэшированный объект. Либо инвалидировать запись при повороте, либо клонировать
  перед мутацией; выбрать и задокументировать.
- OCR-оверлей берёт геометрию из бокса - не затрагивается (двойная буферизация та же).

## 7. Готово, когда

- [ ] У-01, У-13, У-15 реализованы за швом; `grep BgWorker src/` в modern-пути только
      под `#If NETFRAMEWORK`.
- [ ] `.\build.ps1`; приёмка 1-7; x86 - без регрессий.
- [ ] Замер до/после: время перехода назад на 24-Мп фото с сетевой шары - в
      `PROGRESS_DOTNET10_MODERN_BUILD.md`.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
