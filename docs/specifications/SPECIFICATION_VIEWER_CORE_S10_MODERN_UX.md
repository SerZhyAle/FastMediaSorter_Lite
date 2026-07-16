# Тактическая спецификация С10 (modern): видео в слайдшоу, отзывчивость, панорама

Статус: план (не начато)
Дата: 2026-07-16, ревизия 1
Родитель: [SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md](SPECIFICATION_VIEWER_CORE_AUDIT_DOTNET10.md)
Предшественники: [С4](SPECIFICATION_VIEWER_CORE_S4_PLAYBACK.md) (`media_Generation`, единая инициализация VLC), [С5](SPECIFICATION_VIEWER_CORE_S5_ZOOM.md) (панорама)
Сборки: **только .NET 10 mainline**
Объём: У-09, У-10, У-11, У-14 - завершающая стадия

## 0. Цель

Убрать оставшиеся места, где .NET 10-сборка ведёт себя как программа 2010 года:
слайдшоу режет видео по таймеру, `Stop()` и сетевые пробы блокируют UI, панорама
позволяет потерять изображение за краем панели.

| Ид | Ценность | Что |
|---|---|---|
| У-09 | ★★ | Слайдшоу ждёт конца видео (`EndReached`) |
| У-11 | ★★ | Пробы доступности файла - без блокировки UI |
| У-10 | ★ | Асинхронная остановка VLC |
| У-14 | ★ | Кламп панорамы (изображение всегда достижимо) |

## 1. У-09: слайдшоу и видео

[Main_Form.Slideshow.vb:35-38](../../src/Main_Form.Slideshow.vb). В папке со смешанным
содержимым слайдшоу обращается с видео как с картинкой: тик режет 3-минутный ролик
на 10-й секунде, а 2-секундный клип оставляет чёрный экран до конца интервала.
На modern движок один (LibVLC) - решается штатно.

```vb
' В PlayVideoWithVlcAsync, после успешного Play (и после проверки поколения из С4):
If Is_slide_show_mode Then
    ' Слайдшоу отдаёт темп ролику: таймер молчит, пока видео не кончится.
    SlideShowTimer.Enabled = False
    slideshow_Waits_For_Video = True
End If
```

```vb
AddHandler vlc_Media_Player.EndReached, AddressOf Vlc_End_Reached

Private Sub Vlc_End_Reached(sender As Object, e As EventArgs)
    ' Событие приходит с потока libvlc: только BeginInvoke, и никаких вызовов
    ' плеера отсюда (Б-32). Продвигаем слайдшоу уже на UI-потоке.
    Try
        If Me.IsHandleCreated Then Me.BeginInvoke(New Action(AddressOf AdvanceSlideshowAfterVideo))
    Catch
    End Try
End Sub

Private Sub AdvanceSlideshowAfterVideo()
    If Not slideshow_Waits_For_Video Then Return
    slideshow_Waits_For_Video = False
    If Not Is_slide_show_mode Then Return
    SlideShowTimer.Enabled = True
    ReadShowMediaFile(Mode_InSlideShow)
End Sub
```

- **Fallback-таймаут**: если `EndReached` не пришёл (зависший декодер, битый файл) -
  страховочный таймер на `max(длительность + 5 с, 60 с)` продвигает слайдшоу сам.
- **`Is_Video_Loop`**: в слайдшоу зацикливание бессмысленно - при
  `Is_slide_show_mode` не добавлять `:input-repeat=65535`, проигрывать один проход.
  Вне слайдшоу - как сейчас.
- Уход с видео вручную (листание/остановка слайдшоу) обязан сбросить
  `slideshow_Waits_For_Video` и вернуть таймер, иначе слайдшоу молча встанет.

## 2. У-11: пробы доступности без блокировки

[Main_Form.Lifecycle.vb:286-311](../../src/Main_Form.Lifecycle.vb). Логика правильная
(различает «нет файла» / «занят» / «сеть моргнула»), блокировка - нет: до 8 ретраев
по сетевому таймауту SMB **на UI-потоке**, между ними `Thread.Sleep(250)`. Для
уснувшей шары окно висит «не отвечает» минутами; вдобавок зависает процесс-отправитель
(`SendMessage` синхронен - см. Б-38 в [С6](SPECIFICATION_VIEWER_CORE_S6_LIFECYCLE.md)).

- Классификацию исключений и число попыток **сохранить один в один**; заменить
  только исполнение:
  ```vb
  #If Not NETFRAMEWORK Then
  ''' <summary>Та же классификация (missing / denied / network), но вне UI-потока и
  ''' с общим дедлайном: окно остаётся живым, статус - "проверяю доступность..".</summary>
  Private Async Function ProbeArgumentAsync(path As String, token As CancellationToken) As Task(Of ProbeResult)
      Return Await Task.Run(Function() ProbeCore(path, token), token)   ' Task.Delay вместо Thread.Sleep внутри
  End Function
  #End If
  ```
- Общий дедлайн ~10 с; статус на время пробы; результат обрабатывается на UI.
- `ProcessArgument` в modern становится `Async Sub` - проверять `media_Generation`
  (С4) после `Await`: пользователь мог за это время открыть другой файл.
- Обработчик `WM_COPYDATA` ([Main_Form.vb:304-328](../../src/Main_Form.vb)) обязан
  вернуться **мгновенно**: складывать аргумент в очередь и `BeginInvoke` обработку -
  тогда отправитель не ждёт нашу сетевую пробу.

## 3. У-10: асинхронная остановка VLC

[VideoPlayer.vb:224-235](../../src/Main_Form.VideoPlayer.vb). `libvlc_media_player_stop`
синхронен: дожидается остановки демультиплексора/декодера, на сетевом источнике -
сотни миллисекунд и больше. Зовётся на горячих путях: каждый переход видео → изображение
([MediaLoading.vb:601](../../src/Main_Form.MediaLoading.vb)) и каждая операция над
играющим видео.

```vb
#If Not NETFRAMEWORK Then
''' <summary>Скрыть поверхность сразу (визуально мгновенно), а блокирующий Stop
''' увести с UI-потока. ВАЖНО: перед файловой операцией результат обязательно
''' await-ить - лок должен быть снят до Move/Delete.</summary>
Friend Function StopVlcPlaybackAsync() As Task
    If vlc_Video_View IsNot Nothing Then vlc_Video_View.Visible = False
    is_Vlc_Playing = False
    Dim player = vlc_Media_Player
    If player Is Nothing Then Return Task.CompletedTask
    Return Task.Run(Sub()
                        Try
                            player.Stop()
                            player.Media = Nothing
                        Catch
                        End Try
                    End Sub)
End Function
#End If
```

- Навигация (видео → изображение): звать без `await` - показ не ждёт остановки.
- `ReleaseActiveMedia` ([С2](SPECIFICATION_VIEWER_CORE_S2_FILEOPS.md)) в modern:
  `Await StopVlcPlaybackAsync()` **до** `File.Move`/`Delete` - иначе вернётся
  «файл занят». Это делает `ReleaseActiveMedia` асинхронным в modern-ветке - учесть
  при переводе `PoMove`/`Undo`/DEL на очередь (С8): остановка становится частью
  постановки операции.
- Синхронный `StopVlcPlayback` остаётся для net48 и для `FormClosing`.

## 4. У-14: кламп панорамы

[MouseInput.vb:343-350](../../src/Main_Form.MouseInput.vb) пишет `Left`/`Top` без
ограничений: одно энергичное движение уводит зумленное изображение целиком за край
`panel_Media` - на экране пустой фон, клик не листает (`zoom_Scale = 0`), вернуть
можно только зная про Alt+колесо / NumPad `/` / клик по `lbl_Zoom`. Зум-слой ровно
эту задачу уже решил (`Keep_Visible_Px = 100` в `ZoomMath.AnchoredBounds`), но
панорама правилом не пользуется.

- Вынести кламп в чистую функцию рядом с остальной арифметикой:
  ```vb
  ' ZoomMath.vb
  ''' <summary>Не даёт увести бокс целиком за пределы панели: те же 100 px, что
  ''' гарантирует AnchoredBounds - одно правило на шаг зума и на drag.</summary>
  Friend Function ClampPan(location As Point, boxSize As Size, panelSize As Size) As Point
  ```
- Звать из `AnchoredBounds` **и** из `Pic_MouseMove` (modern-ветка). x86 -
  исторический код без клампа.

## 5. Приёмка

1. **Слайдшоу с видео**: папка из фото и роликов (3 мин и 2 с), интервал 10 с, S →
   каждый ролик проигрывается **целиком**, после конца сразу следующий файл; фото
   идут по 10 с; ручное листание во время ролика не ломает слайдшоу (таймер вернулся).
2. **Зависшее видео**: подсунуть битый mkv → слайдшоу не встаёт навсегда (сработал
   fallback-таймаут).
3. **Loop + слайдшоу**: `Is_Video_Loop` включён → в слайдшоу ролик проигрывается один
   раз и идёт дальше; вне слайдшоу - зацикливается как раньше.
4. **Уснувшая шара**: открыть файл с `\\server`, который offline → окно **не** висит
   «не отвечает», статус «проверяю доступность..», через ~10 с - «Сетевой ресурс
   недоступен»; всё это время можно листать локальную папку.
5. **WM_COPYDATA**: при работающем первом экземпляре открыть файл с недоступной шары
   вторым запуском → **отправитель** завершается мгновенно (не ждёт пробу).
6. **Видео → изображение**: листание по папке с 4K-роликами на NAS → переход на фото
   мгновенный (нет паузы на `Stop()`).
7. **DEL играющего видео**: удаляется с первой попытки (остановка успевает
   завершиться до `File.Delete`).
8. **Кламп панорамы**: зум 800 %, резко утащить изображение в угол → минимум 100 px
   изображения остаётся в панели, клик по нему работает.

## 6. Риски

- **У-10 + файловые операции** - самое опасное место стадии: если хоть один путь
  операции забудет `await`, вернётся «файл занят» (E001/E014). Приёмка 7 обязательна
  для mp4/mkv/avi.
- `EndReached` приходит и при штатной остановке `Stop()` в некоторых версиях
  LibVLC - `slideshow_Waits_For_Video` обязан отсекать ложные продвижения (проверить
  сценарием: остановить слайдшоу на видео кнопкой → следующий файл **не** появляется).
- `ProcessArgument` как `Async Sub` меняет порядок: `Form1_Load` больше не гарантирует,
  что к моменту `Main_Form_Shown` файл загружен - проверить первый показ перспективы
  (`is_form_shown`-гейт) на командной строке с файлом.

## 7. Готово, когда

- [ ] У-09, У-10, У-11, У-14 реализованы за швом.
- [ ] `.\build.ps1`; приёмка 1-8; x86 - без регрессий.
- [ ] `CHANGELOG.md` ▸ [Unreleased] (по-английски).
- [ ] Родительская спецификация переведена в `done/`, если все стадии закрыты.
