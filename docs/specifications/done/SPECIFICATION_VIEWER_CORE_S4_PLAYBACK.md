# Тактическая спецификация С4: слайдшоу и видео

Статус: реализовано 2026-07-16 (обе сборки)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественник: [С3](SPECIFICATION_VIEWER_CORE_S3_NAVIGATION.md) (константы режимов)
Сборки: **обе**
Объём: 7 багов, 3 файла

## 0. Цель

Слайдшоу должно доходить до конца папки и не жечь диск на месте; случайный режим -
быть случайным; видео - не всплывать поверх уже показанной картинки и не
инициализировать LibVLC дважды.

| Ид | П | Что |
|---|---|---|
| Б-25 | П2 | Слайдшоу навсегда застревает на последнем файле и передекодирует его на каждом тике |
| Б-29 | П2 | `PlayVideoWithVlcAsync` не проверяет актуальность после `Await` |
| Б-30 | П2 | `EnsureVlcInitializedAsync` без защиты от повторного входа - два LibVLC |
| Б-26 | П3 | Повторный S/I/F5 не ускоряет интервал |
| Б-27 | П3 | `SlideShowStop` не сбрасывает random-флаг - навсегда отключается префетч |
| Б-28 | П3 | `Rnd()` без `Randomize()` - одна и та же «случайная» последовательность |
| Б-32 | П4 | Колбэк LibVLC зовёт методы плеера со своего потока |

## 1. Слайдшоу

### 1.1 Б-25: wrap вместо clamp

[Main_Form.MediaLoading.vb:133-135](../../src/Main_Form.MediaLoading.vb), ветка
`Mode_InSlideShow`. Решение: **заворачивать на 0**, как ручное листание
(`Mode_Next`, строка 105) - последовательность одна, поведение предсказуемо, и
index = 0 заодно триггерит перечитывание каталога (новые файлы попадут в круг):

```vb
current_File_Index += 1
If current_File_Index > total_File_Count - 1 Then current_File_Index = 0
```

То же в `LoadFilesForRandomOrSlideshow` (строка 337): `current_File_Index += 1` без
проверки границы - добавить тот же wrap (сейчас его спасает только клампинг в
`UpdateCurrentFileAndDisplay`, снова на последний файл).

Побочный эффект бага - передекодирование последнего файла на каждом тике
(`current_Loaded_File_Name = ""` на строке 804 сбрасывает кэш) - исчезает вместе с
застреванием.

### 1.2 Б-26: халвинг интервала с клавиатуры

`KeybUse` первым делом зовёт `SlideShowStop()` (сбрасывает `Is_slide_show_mode`), и
`SetSlideShow` всегда видит «слайдшоу не идёт». Параметр `was_Slide_Show_Mode`
([KeyboardInput.vb:25](../../src/Main_Form.KeyboardInput.vb)) заведён ровно для этого,
но не используется - подключаем:

```vb
' Slideshow.vb
Private Sub SetSlideShow(Optional was_Running As Boolean = False)
    is_Slide_Show_Random_Mode = False
    Dim slide_show_new_interval = Slideshow_Base_Interval_Ms
    If Is_slide_show_mode OrElse was_Running Then
        slide_show_new_interval = CInt(SlideShowTimer.Interval / 2)
        If slide_show_new_interval < slide_show_limit Then slide_show_new_interval = slide_show_limit
    End If
    ...
```

`KeybUse`: `Case Keys.S : SetSlideShow(was_Slide_Show_Mode)` и
`Case Keys.I, Keys.F5 : SetRandomSlideShow(was_Slide_Show_Mode)`. Кнопки тулбара
зовут без параметра (там `Is_slide_show_mode` ещё живой - работает как раньше).

### 1.3 Б-27: random-флаг сбрасывается в одном месте

[Main_Form.Slideshow.vb:40-44](../../src/Main_Form.Slideshow.vb):

```vb
Private Sub SlideShowStop()
    SlideShowTimer.Enabled = False
    Is_slide_show_mode = False
    ' Иначе после случайного слайдшоу, остановленного МЫШЬЮ (клики и кнопки зовут
    ' только SlideShowStop), флаг остаётся True и next_File_After_Current всегда
    ' пуст - предзагрузка молча выключена до следующего нажатия клавиши (Б-27).
    is_Slide_Show_Random_Mode = False
    lbl_Slideshow_Time.Visible = False
End Sub
```

`SetRandomSlideShow` ставит флаг **после** `SlideShowStart()` - порядок не ломается.
Дублирующий сброс из `KeybUse` (строка 27) убрать.

### 1.4 Б-28: один генератор

Заменить `Rnd()` ([MediaLoading.vb:130](../../src/Main_Form.MediaLoading.vb)) и два
локальных `New Random` (строки 318, 333) на одно поле:

```vb
' Rnd() без Randomize() выдаёт одну и ту же последовательность в каждом процессе:
' случайное слайдшоу показывало одни и те же файлы в одном порядке каждый сеанс.
Private ReadOnly slideshow_Rng As New Random()
```
Везде `slideshow_Rng.Next(0, total_File_Count)`.

## 2. Видео

### 2.1 Б-30: единственная инициализация

[Main_Form.VideoPlayer.vb:120-171](../../src/Main_Form.VideoPlayer.vb). Пока первый
вызов ждёт докачку рантайма, `libVlc` = Nothing, и второй проходит проверку - два
`LibVLC`, два `MediaPlayer` (первый теряется вместе с нативными ресурсами и локом на
файле), осиротевший `VideoView` навсегда в `panel_Media.Controls`. Кэшируем **задачу**:

```vb
Private vlc_Init_Task As Task(Of Boolean)

''' <summary>Инициализация ровно одна: все конкурентные вызовы ждут одну и ту же
''' задачу. Метод вызывается только с UI-потока, поэтому присвоение поля гонки не
''' образует (Б-30).</summary>
Private Function EnsureVlcInitializedAsync() As Task(Of Boolean)
    If libVlc IsNot Nothing AndAlso vlc_Media_Player IsNot Nothing Then Return Task.FromResult(True)
    If vlc_Init_Task Is Nothing OrElse (vlc_Init_Task.IsCompleted AndAlso Not vlc_Init_Task.Result) Then
        vlc_Init_Task = InitializeVlcCoreAsync()   ' тело нынешней функции
    End If
    Return vlc_Init_Task
End Function
```

Мёртвое поле `is_Vlc_Init_Attempted` удалить (пишется, нигде не читается).

### 2.2 Б-29: проверка актуальности после `Await`

`PlayVideoWithVlcAsync` - `Async Sub` (fire-and-forget): за время инициализации
(0.5-2 с, при докачке - минуты) пользователь уходит на картинку, а метод потом
безусловно поднимает `VideoView` поверх неё и затирает `current_Loaded_File_Name`.

Вводим счётчик поколений навигации - он же пригодится в
[С7](SPECIFICATION_VIEWER_CORE_S7_MODERN_PIPELINE.md):

```vb
''' <summary>Инкрементируется на каждый показ нового медиа. Любая асинхронная
''' работа, стартовавшая в поколении N, обязана молча свернуться, если к моменту
''' продолжения поколение сменилось.</summary>
Private media_Generation As Integer
```
`UpdateCurrentFileAndDisplay`: `media_Generation += 1` в начале.

```vb
Private Async Sub PlayVideoWithVlcAsync(file_Path As String)
    If String.IsNullOrEmpty(file_Path) OrElse Not File.Exists(file_Path) Then Return
    Dim generation As Integer = media_Generation

    If Not Await EnsureVlcInitializedAsync() Then
        If generation <> media_Generation Then Return
        lbl_Status.Text = OptionalRuntimeManager.GetVlcUnavailableStatusText(Is_Russian_Language)
        TryOpenVideoWithDefaultPlayer(file_Path)
        Return
    End If
    If generation <> media_Generation Then Return   ' пользователь уже долистал дальше
    ...
```

`TryOpenVideoWithDefaultPlayer` - принимает путь параметром (сейчас читает глобальный
`Current_File_Name` и может открыть во внешнем плеере картинку).

### 2.3 Б-32: колбэк LibVLC

[VideoPlayer.vb:241-243](../../src/Main_Form.VideoPlayer.vb): событие `Playing`
приходит с потока libvlc, а обработчик синхронно зовёт методы того же плеера.

```vb
Private Sub Vlc_Media_Player_Playing(sender As Object, e As EventArgs)
    ' Событие приходит с потока libvlc; синхронный вызов методов плеера из его же
    ' колбэка - классический дедлок (особенно если UI-поток в этот момент делает
    ' Stop при быстром листании). BeginInvoke, не Invoke.
    Try
        If Me.IsHandleCreated Then Me.BeginInvoke(New Action(AddressOf ApplyVideoAudioStateToVlc))
    Catch
    End Try
End Sub
```

## 3. Приёмка

1. **Слайдшоу до конца**: папка из 5 картинок, S → дойдя до последней, слайдшоу
   переходит на первую и продолжает; на последнем файле нет постоянного обращения
   к диску (проверить по логу w0870/w0890 - файл не перечитывается каждый тик).
2. **Ускорение**: S, S, S → интервал 10 с → 5 с → 2.5 с (метка `lbl_Slideshow_Time`
   это показывает); то же кнопкой тулбара; ниже `slide_show_limit` не опускается.
3. **Random-префетч**: I (случайное слайдшоу) → остановить **кликом мыши** → листать
   колесом вперёд → переходы мгновенные (в логе виден w1060 «BgWorker is run» и
   w0870 «P2 is found already loaded»).
4. **Случайность**: запустить приложение, I на папке из 20 файлов, запомнить первые
   5 файлов; перезапустить приложение, повторить → последовательность другая.
5. **Видео при холодном VLC**: удалить `libvlc\` (или взять сборку без рантайма),
   открыть видео → пока идёт «Установка поддержки VLC..», листнуть на картинку →
   картинка остаётся на экране, видео **не всплывает** поверх; вернуться на видео →
   оно играет (не «пропущено как уже загруженное»).
6. **Двойная инициализация**: при холодном VLC быстро листнуть по трём видео подряд →
   в `panel_Media` ровно один `VideoView` (проверить в отладчике/по логу w0868 -
   ровно одна строка «LibVLC initialized»); звук не задваивается.
7. **Оба exe**: сценарии 1-4 и 5 (net48 - через VLC-fallback на .avi) проходят в
   `FastMediaSorter_x86.exe`.

## 4. Риски

- Wrap в слайдшоу меняет привычку: раньше оно «останавливалось» (фактически -
  замирало) на последнем файле. Если владелец захочет явную остановку - вариант
  зафиксирован в Б-25 родительской спецификации (`SlideShowStop()` + статус
  «слайдшоу завершено»); выбрать **одно** и задокументировать в README/справке.
- `media_Generation` вводится здесь и переиспользуется в С7 - не переименовывать.
- Кэш `vlc_Init_Task` держит неудачную попытку: повторная попытка разрешена только
  после явного провала (см. условие) - иначе отказ докачки залипнет навсегда.

## 5. Готово, когда

- [ ] 7 багов закрыты; `grep is_Vlc_Init_Attempted src/` пуст; `grep "Rnd()" src/` пуст.
- [ ] `.\build.ps1`; приёмка 1-7 в обоих exe.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
