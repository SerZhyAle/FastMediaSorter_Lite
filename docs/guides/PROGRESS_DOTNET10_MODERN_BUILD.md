# ПРОГРЕСС: современная сборка на .NET 10 (реализация эпика)

> **Назначение файла**: живой журнал реализации. Обновляется после КАЖДОГО шага,
> чтобы работу можно было продолжить с того же места после обрыва сессии/токенов.
> Читать сверху вниз: «Текущее состояние» + «Следующий шаг» - всё, что нужно для
> продолжения. Работа ведётся в ветке **`feature/dotnet10-modern`** (main не трогаем
> до готовности; владелец мержит сам).

Спецификации-источники (все решения - там):
- `SPECIFICATION_DOTNET10_MODERN_BUILD.md` (local specification) - эпик (фазы Ф0-Ф7)
- `012_SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md` (local specification) - зум/панорама (Ф-Z0..Ф-Z5, все O-Z-* решены)
- `SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md` (local specification) - MKV/ISO (Ф-A..Ф-G)

Дата старта: 2026-07-16.

---

## ТЕКУЩЕЕ СОСТОЯНИЕ (обновлять при каждом коммите!)

- Ветка: `feature/dotnet10-modern` (создана от main @ dcc066e + доки-коммит)
- **Форма поставки задана владельцем: ДВА EXE рядом** (см. раздел ниже) -
  `FastMediaSorter_LITE.exe` (x64, .NET 10, мейнлайн) + `FastMediaSorter_x86.exe`
  (net48, x86). Сделано и проверено вживую: оба запускаются, кросс-форвардинг
  между разными именами работает, общие либы резолвятся по разрядности.
- **Аудит кода пройден** (3 агента), все находки исправлены - см. раздел «АУДИТ».
- Стадия: **Ф0-Ф4 ГОТОВЫ И ПРОВЕРЕНЫ.** Modern-приложение СУЩЕСТВУЕТ и работает:
  - `dotnet build` зелёный; `dotnet publish -c Release -r win-x64` -> один exe
    ~113 МБ + рассыпной `libvlc\win-x64` (плагины обязаны лежать на диске) +
    `flags\` + tesseract-нативы; итого ~226 МБ publish-папка.
  - Смоук published-exe: окно с правильным заголовком, лог у exe (фикс
    AppContext.BaseDirectory работает), 64-битный процесс, настоящая версия ОС
    (NT 10.0.26200 - net48 видел 6.2 из-за манифест-шима).
  - **Статический И анимированный webp открываются без ошибок** (ImageSharp 3;
    анимированный - тот самый триггер-баг Server 2025). Тестовые webp
    генерятся скриптом в скратчпаде `webpgen` (мини net10-проект).
  - Single-instance форвардинг между процессами (WM_COPYDATA) работает на .NET 10.
  - Legacy net48 пересобран и смоук-запущен зелёным после ВСЕХ правок общих файлов.
- Следующая стадия: **Ф5 упаковка** (build.ps1 -Modern), затем этапы 8-11.

## УПАКОВКА - СДЕЛАНА И ПРОВЕРЕНА (2026-07-16)

Вся упаковка переведена на два exe. **Правило:** msbuild даёт только
`FastMediaSorter_x86.exe`; `FastMediaSorter_LITE.exe` рождается ТОЛЬКО из
`dotnet publish src/Modern/...` - каждый упаковщик теперь делает этот publish сам
и кладёт в стейдж только exe (деревья поддержки уже пришли из `bin\Release`).

- `tools/Build-Installer.ps1`, `tools/Build-OfflineRelease.ps1` - publish modern +
  оба exe в стейдж; проверка наличия перевешена на x86-exe.
- `.github/workflows/release.yml` - шаг «Publish viewer (.NET 10 x64
  self-contained)» -> `modern-publish/`, стейджится рядом с x86. `-p:ReleaseVersion`
  прокинут (версия единая для обоих). .NET 10 SDK на раннере уже был (Companion).
- `msix/build-msix.ps1` - Store-пакет **x64-only**: publish modern -> из него
  берётся exe И версия для remap; `FastMediaSorter_x86.exe` из стейджа
  **исключён** (в манифесте один `<Application>`, Store сам гейтит по арх.).
- `installer/FastMediaSorter.iss`:
  - `[Files]` - оба exe едут одним wildcard'ом (правки не потребовалось).
  - **Ярлык/автозапуск/ассоциации выбираются по версии ОС** (`UseModernExe`:
    Win10 build >= 14393). На Win7/8.1 (installer MinVersion=6.1!) x64-мейнлайн
    физически не запустится - там всё ведёт на `FastMediaSorter_x86.exe`. Оба exe
    ставятся всегда. **Решение владельца 2026-07-16.**
  - Исключение `win-x86` из компонента кодеков убрано: решение о payload теперь
    живёт в ОДНОМ месте - `Prepare-OcrOfflinePayload.ps1`.
- `tools/Prepare-OcrOfflinePayload.ps1` - трим x86 стал ключом **`-KeepX86`**
  (по умолчанию ТРИМИТ). **Решение владельца:** x86-либы (~100 МБ) в пакет НЕ
  везём - x86-вьювер докачивает кодеки/OCR в `%LOCALAPPDATA%` при первом
  использовании (`OptionalRuntimeManager`). `-KeepX86` - для будущего
  standalone-x86-артефакта (сайт/GitHub, спека §8.3).
- `reinstall.ps1` - `Stop-Quiet 'FastMediaSorter_x86'` добавлен.

**Проверено сквозняком:** `Build-Installer.ps1 -SkipOcr` -> setup.exe 124.5 МБ
собрался; silent-инсталл (`/VERYSILENT /DIR=..`) exit 0 -> **оба exe на месте**;
установленный x64-вьювер запускается. Стейдж: 346 МБ, `libvlc\win-x64` есть,
`win-x86`/`x86`/`runtimes\win-x86` вычищены. (В silent-инсталле без прав админа
компоненты codecs/ocr/share пропускаются - это ДЕЙСТВУЮЩИЙ дизайн с 26.7.15.2200
«per-user = только лёгкий вьювер», а не регрессия.)

## ЭТАП 8 - ЗУМ/ПАНОРАМА (в работе, владелец заказал 8+9+10)

**Сделано (Ф-Z1 + Ф-Z2), собрано и смоук-проверено:**
- Новый `src/Main_Form.Zoom.vb` - **весь файл `#If Not NETFRAMEWORK`** (спека: legacy
  заморожен). Чистый API: `ZoomToFit` / `ZoomToActualSize` / `ZoomStepAt` +
  `TryHandleWheelZoom` / `TryHandleZoomKey`. Константы по O-Z-7: шаг 1.25,
  Ctrl 1.5, границы 5%..4000%, snap ±3% на Fit/100%.
- **Колесо (Ф-Z1):** флаг `Zoom_Wheel_Zooms` (реестр `WheelZooms`, дефолт **0 =
  листает**, O-Z-2 - мышь по умолчанию не меняется). Чекбокс в Настройках
  «Просмотр» (в x86-сборке **скрыт** - фичи там нет). Модификаторы сохранены;
  над видео колесо всегда листает.
- **Клавиатура (Ф-Z2):** NumPad `+`/`-` (зум от курсора), `/` = Fit, `*` = 100%
  (O-Z-4: только серый блок, никаких Ctrl-комбо). Проценты в `lbl_Zoom`
  («Вписать 38 %» / «250 %»).
- **Попутно закрыт исторический баг:** старый Ctrl+колесо писал в `zoom_Scale` сырое
  произведение, и зум ВНИЗ (0.9, 0.81..) уходил ниже 1 - это молча ломало панораму и
  делегирование клика. Теперь `zoom_Scale` несёт ровно два смысла (1 = fit, 0 = зум),
  а реальный масштаб живёт в `zoom_Factor`.

**Архитектурное решение (важно для Ф-Z3):** зум пока по-прежнему *выражается*
ресайзом боксов - потому что перспективный фон И OCR-оверлей выводят свою геометрию
из этого же прямоугольника через общий `GetZoomedImageRectangle` и совпадают
даром. Слой ввода зовёт только API выше, поэтому переход на viewport = переписать
`ApplyZoomFactor`+`CurrentZoomFactor`, не трогая ввод.

**Ф-Z3 (viewport) - НЕ сделан, оценить трезво.** Он трогает `HqPictureBox.OnPaint`,
`Draw_Perspective` (≈150 строк пиксельной работы, читает `Picture_Box_1.Width/Height`)
и `PaintOcrOverlay`. Пользователю он не виден: вся видимая ценность спеки (классическая
модель, клавиатурный зум, якоря, границы) уже есть. Спека §5 прямо разрешает
запасной путь: «ограничиться перепривязкой колеса и клавиатурным зумом поверх
текущего ресайза, а viewport отложить». **Решение владельца нужно, если хочется
именно чистой геометрии.**

**Ф-Z5 - СДЕЛАН** (коммит следом за 9bfbdc3):
- Чистая математика вынесена в новый **`src/ZoomMath.vb`** (whole-file
  `#If Not NETFRAMEWORK`): `FitFactor` / `Clamp` / `Snap` / `StepFrom` /
  `AnchorFraction` / `AnchoredBounds` + константы. Форма (`Main_Form.Zoom.vb`) стала
  тонкой обёрткой: читает `panel_Media`, двигает боксы. Причина выноса - иначе
  математику нечем накрыть тестом, она была заперта внутри WinForms-формы.
- **33 теста** в `tests/Lite.Tests/ZoomMathTests.vb` (тоже `#If Not NETFRAMEWORK`,
  так что на ноге net48 файл компилируется в ничто - честная форма: модели зума в
  x86 нет). Итог прогона: **net10 = 51 тест** (было 18), **net48 = 15**, все зелёные.
  Ключевой тест - `AnchoredBounds_KeepsThePixelUnderTheCursorStill`.
- Побочно найдено и починено тестом: сторож «оставить 100 px на экране» при
  крошечном боксе (образ < 100 px после зума вниз) отпихивал картинку от края,
  выполняя обещание, которое бокс физически не может сдержать. Теперь полоса
  сужается до размера бокса (`Math.Min(Keep_Visible_Px, newWidth)`).
- Справка: новый `ZoomHelpLine()` в `Main_Form.Localization.vb` - RU+EN строка про
  серый блок NumPad, на net48 возвращает `""`, чтобы x86-справка не обещала клавиш,
  которых там нет. Карта клавиш добавлена в CLAUDE.md.
- **Проверено:** NuGet ругался NU1903 про ImageSharp 2.1.8 с пометкой
  `[TargetFramework=net10.0-windows]` - это **переигрывание restore-варнинга** на
  каждую ногу, а не протечка. `project.assets.json` показывал честно:
  net48 -> 2.1.8, net10 -> 3.1.12. (Сама уязвимость закрыта ниже.)

## ImageSharp CVE - ЗАКРЫТО, И МОЁ ПРЕЖНЕЕ УТВЕРЖДЕНИЕ БЫЛО НЕВЕРНЫМ (2026-07-16)

**Исправление ошибки в этом же журнале и в TESTING.md.** Я писал, что этап 10
(урезка x86 по `FEATURE_FULL`) «заодно выкинет уязвимый ImageSharp 2.1.8». **Это
неверно.** Проверено `grep`: ImageSharp в x86 тянет **только**
`LegacyWicImageDecoder` (запасной декодер WEBP); `src/Ocr/`, `src/Translate/` и
Companion его не используют. Урезка фич эту зависимость **не убрала бы**.

**Настоящее решение - скучный бамп: 2.1.8 -> 2.1.13** (сделано).
- В линии 2.1 есть версии до **2.1.13**; проверено по nupkg: тот же **net472**, та же
  лицензия **Apache-2.0**, и **дельта зависимостей нулевая** - те же 5 пакетов
  (Buffers 4.5.1 / Memory 4.5.4 / Numerics.Vectors 4.5.0 / Unsafe 5.0.0 /
  Text.Encoding.CodePages 5.0.0). Ничего доводендоривать не пришлось.
- Завендорено в `packages\SixLabors.ImageSharp.2.1.13\` (та же раскладка, что у
  2.1.8), обновлены `src/packages.config`, `<Reference>` + `<EmbeddedResource>` в
  `src/FastMediaSorter.vbproj` и net48-нога `tests/Lite.Tests`.
- **Результат: все 4 предупреждения NU1903/NU1902 исчезли**, оба вьювера собираются,
  наборы зелёные. Мелочь на будущее: `System.Text.Encoding.CodePages` объявлен в
  зависимостях ImageSharp, но **не завендорен** и не в `packages.config` - так было и
  при 2.1.8, приложение работает; бамп это не менял.

**Найденный при этом настоящий предел (НЕ регрессия):** ImageSharp **2.x не умеет
анимированные WEBP** («Animated webp are not yet supported» - фича только в 3.x).
Значит на **Windows 7/8.1** (у WIC нет WebP-кодека) x86-вьювер анимированный WEBP не
откроет **и никогда не мог**; на Win10/11 он открывается через WIC до фолбэка.
Исключение ловится `Try/Catch` в `FileManager.LoadImageWithStream` - не падает.
Зафиксировано тестом `BundledImageSharp_CannotDecodeAnimatedWebp_KnownLegacyGap`.

**Ловушка, которую стоит запомнить:** новые тесты этой библиотеки я сперва вставил
**внутрь** региона `#If Not NETFRAMEWORK` -> `#If NETFRAMEWORK` там невыполнимо, VB
скомпилировал их в ничто, **а прогон остался зелёным**: счётчик net48 просто не
изменился (15 -> 15). Поймано только сверкой счётчика и `--list-tests`.
**Проверять по числу тестов, а не по слову `Passed!`.**

## ЭТАП 9 (MKV/ISO): ЧТО УЖЕ ПРОВЕРЕНО ЭМПИРИЧЕСКИ (2026-07-16)

Разведка перед вопросами владельцу - чтобы решения принимались по фактам, а не по
ожиданиям. Стенд: Windows 11 build 26200, шелл **НЕ элевирован** (проверено).

- **O-ISO-1 - ЗАКРЫТ, риск снят.** Спека называла «монтирование без админа» гвоздём
  (§10). Проверено дважды: (1) `Mount-DiskImage -Access ReadOnly` из
  неэлевированного шелла - **успех**, образ примонтирован как `D:`, содержимое
  читается; (2) **тот самый механизм, что поедет в продукт** - Win32
  `OpenVirtualDisk` + `AttachVirtualDisk` с `ATTACH_VIRTUAL_DISK_FLAG_READ_ONLY`
  через P/Invoke `virtdisk.dll` - **успех без админа**, `DetachVirtualDisk` rc=0.
  Скрипт-стенд: scratchpad/attach-test.ps1. Подопытный образ:
  `C:\GD\i\INTERNET\anyconnect-win-3.1.12020-pre-deploy-k9.iso` (22 МБ, data-ISO).
  **Нюанс:** `GetVirtualDiskPhysicalPath` отдаёт `\\.\CDROM0`, а НЕ букву диска -
  нужен ещё шаг устройство -> том -> буква (спека это предвидела, §5.2).
- **§3 подтверждён:** в завендоренном и задеплоенном дереве плагинов есть
  `liblibbluray_plugin.dll`, `libvcd`, `libcdda`, `libsvcdsub`; **dvdnav/dvdread
  отсутствуют** -> DVD-ветка без вендоринга плагинов не заиграет. Отдельно проверено:
  `libdvdcss`/`libaacs`/`libbdplus`/`keydb.cfg` **нигде в репозитории нет** - чисто.
- **MKV локальный - РАБОТЫ НОЛЬ.** `.mkv` уже в `video_File_Extensions`
  ([Main_Form.vb:83](../../src/Main_Form.vb#L83)), а modern-диспетчер шлёт видео
  прямо в `PlayVideoWithVlcAsync`. Ф-A по локальному MKV = уже выполнено ранее при
  снятии WebBrowser. Остаток Ф-A - **только** гейт `File.Exists`
  ([Main_Form.VideoPlayer.vb:173](../../src/Main_Form.VideoPlayer.vb#L173)) для
  удалённых MRL, а он имеет смысл только если O-ISO-5 скажет «нужны URL-схемы»:
  без поля «Открыть URL..» удалённый URI в приложение просто неоткуда взять.
- **O-ISO-2 - НЕ проверен, и честно: проверить нечем.** Нужен настоящий
  DVD/BD-образ (у нас только data-ISO). Не выдавать за проверенное.

**Наблюдение для решения по scope:** внутри read-only образа **сортировка
невозможна** (move/delete упрутся в read-only), но **режим «копировать, а не
переносить» (`Is_Copying_not_Moving`) из образа работает штатно**. То есть
осмысленный сценарий для сортировщика - «примонтировал data-ISO -> раскидал
копированием наружу». Это и дёшево: смонтированный образ - обычная папка, её жуёт
существующий сканер без единой строки нового кода, кроме lifecycle монтирования.

## ЭТАП 9 - РЕШЕНИЯ ВЛАДЕЛЬЦА (2026-07-16) И ЧТО СДЕЛАНО

**Решения:** ISO - **не делать вовсе** (отпали O-ISO-1/2/3/6 и фазы Ф-B..Ф-E);
O-ISO-5 (URL-схемы) - **да**; O-ISO-7 (дорожки) - **да, в рамках этапа 9**;
Ф-Z3 (viewport) - **оставить как есть** => **этап 8 закрыт полностью**.

**Сделано (коммит 8a95dc3 + следующий):**
- **Ф-A**: `CanVlcReachMedia` вместо голого `File.Exists` (у удалённого MRL нет файла,
  который можно `stat`); `CreateVlcMedia` передаёт удалённый MRL **строкой** через
  `FromType.FromLocation` (`New Uri` искажает его и срезает схему, по которой
  выбирается access-плагин). **UNC намеренно не считается удалённым.**
- Поле **«Открыть URL..»** - правый клик по кнопке «Выбрать файл» (тот же шаблон, что
  у «Поделиться этой папкой»), тултип это рекламирует. Только видео; картинку по URL
  отклоняем фразой. Новый `src/MediaUrl.vb` (чистые функции) + 33 теста.
- **Дорожки** (`src/Main_Form.VideoTracks.vb`): кнопка «Дорожки» через
  `SetToolbarItemHidden` (появляется, только когда есть выбор) + клавиши **A**/**V** +
  запоминание **по коду языка**.

**Что дал стенд на живом движке** (scratchpad/vlctracks + MKV с 2 звуковыми
дорожками и субтитрами, собранный ffmpeg) - всё это было бы не угадать:
- `AudioTrackDescription` включает служебную `Disable` с **id=-1** => у файла с одной
  дорожкой `Length`=2, и наивная проверка показывала бы кнопку там, где выбора нет.
  Считаем **настоящие** дорожки (`RealTrackCount`).
- Имена приходят композитом **`Russian - [Russian]`**, а не `Russian` - ключ ненадёжен.
- **`player.Media.Tracks` сохраняет чистое `Language` (`rus`/`eng`) даже после
  `Media.Dispose()`** и без явного `Parse` => запоминаем выбор по языку. Это главное
  открытие: язык стабилен между релизами, id - нет, имя - нет.
- `SetAudioTrack(id)` реально применяется (проверено сравнением после вызова).

**GUI-смоук моей сборки НЕ выполнен, и вот почему:** у владельца запущен свой вьювер
(`C:\GD\tc\SZA\_APP\FastMediaSorter_LITE.exe`). Мьютекс один на оба exe и
path-independent, поэтому мой тестовый запуск переслал файл **в его окно** и вышел за
40 мс с кодом 0. Пока его вьювер запущен, поднять свою сборку в GUI невозможно -
это то самое ограничение, записанное как вопрос владельцу. Все допущения об API
проверены headless; экранная проверка - за владельцем.

## ЭТАП 10 - СДЕЛАН (2026-07-16). Решение владельца: резать ТОЛЬКО Share

**Почему только Share, и это не компромисс, а факт:** Companion - `net10.0-windows`
**x64**; он не стартует ни на Win7/8.1, ни на 32-битной Windows, то есть ровно там,
ради чего x86 и существует. OCR и перевод там работают - их оставили.

**Побочно найден и починен баг:** `MinVersion=6.1` пускает Setup на Win7, и компонент
`share` клал туда net10-x64-exe, который не запускается => кнопка «Share Manager..»
вела к exe, который машина отказывается запустить. Теперь у компонента
`MinVersion: 10.0.14393` (тот же порог, что у `UseModernExe`).

**O-7 закрыт и оказался пустым вопросом.** `FEATURE_FULL` и отдельная
build-конфигурация НЕ нужны: net48-сборка ровно одна и она И ЕСТЬ x86-фолбэк, поэтому
`#If Not NETFRAMEWORK` уже называет отсекаемое точно. Второй константы нет.

**Проверено по бинарям** (а не «собралось - значит вырезано»): в
`FastMediaSorter_x86.exe` (2.87 МБ) маркеров `ActivateShareEntryPoint`,
`FastMediaSorterCompanionSingleInstanceMutex`, `OpenShareManagerWindow` (а также
`IsRemoteMediaUrl`/`TryHandleTrackKey`) **нет**; в modern-exe - **все есть**.
*Грабля стенда:* первый скан искал UTF-16 только от чётного смещения и показал
«absent» для строки, которая на месте. Сканировать надо оба выравнивания, иначе
«вырезано» - это удача, а не доказательство.

**НЕ ПРОВЕРЕНО:** правка `installer/FastMediaSorter.iss` не скомпилирована - **Inno
Setup на машине не установлен**. Синтаксис сверен по документации: `Check` в
`[Components]` **недопустим** (общие параметры - только `Languages`/`MinVersion`/
`OnlyBelowVersion`), поэтому и взят `MinVersion`; «Build numbers .. may be included»
подтверждает формат `10.0.14393`. Первый же прогон `Build-Installer.ps1` это проверит.

## СЛЕДУЮЩИЙ ШАГ

Остаток (winget/Store при релизе). Прежний текст про этап 10 (урезка x86
по `FEATURE_FULL` - **имя и разрядность уже сделаны**. **ВНИМАНИЕ: довод «заодно
выкинет уязвимый ImageSharp» БОЛЬШЕ НЕ ДЕЙСТВУЕТ** - уязвимость закрыта бампом до
2.1.13, а ImageSharp тянет декодер WEBP, а не OCR/перевод/Share. Урезка теперь
оправдана только весом и площадью атаки), этап 11-остаток (winget/Store при релизе).

**Открытый вопрос к владельцу по этапу 10:** директива «два exe рядом + смежные
библиотеки» противоречит §1.1 эпика, где legacy описан как **standalone без
OCR/перевода/Share, только сайт+GitHub**. Если x86 лежит в одной папке с x64, то
tessdata/companion/libvlc там стоят в любом случае ради x64 - и урезка x86 экономит
только сам exe. Нужно подтверждение, что именно резать.

**Не проверено (нужен реальный прогон/машина):** элевированный инсталл (компоненты
codecs/ocr/share); Win7/8.1-ветка `UseModernExe` (нет такой машины - логика прямая,
но глазами не видел); релиз по тегу в CI.

## АУДИТ КОДА 2026-07-16 (три агента: паритет проектов / полный дифф от dcc066e / .NET-ловушки)

**Вердикты:** структурный паритет ПОЛНЫЙ (все 56 общих Compile-итемов, ресурсы,
resx-имена побайтово); **[LEGACY-CHANGED]: none** - net48-ветки эквивалентны
dcc066e; найдено и **ИСПРАВЛЕНО** (коммит см. журнал):

- P1 **OCR был мёртв под single-file**: InteropDotNet-загрузчик Tesseract видит
  Assembly.Location="" -> ArgumentNullException (эмпирически воспроизведено
  стендом). Фикс: `InteropDotNet.LibraryLoader.Instance.CustomSearchPath =
  parent(GetOcrRuntimeDir())` в TryPrepareOcrRuntime (#If Not NETFRAMEWORK).
- P1 **Process.Start ×3** (mailto в Main_Form/Table_Form, внешний плеер в
  VideoPlayer): на .NET UseShellExecute по умолчанию False -> Win32Exception.
  Фикс: явный ProcessStartInfo UseShellExecute=True (портируемо, без #If).
- P1 **Move/Delete играющего видео падал** (VLC держит файл; net48 отпускал через
  очистку WebBrowser). Фикс: `StopVlcPlayback()` в modern-ветках всех 4 файловых
  точек (FileOperations ×3, MediaLoading delete/empty-folder).
- P2 **6 незагейченных касаний Web_Browser.DocumentText** будили IE ActiveX в
  modern (в т.ч. на КАЖДОЙ картинке; на IE-less системах = краш) + строка
  `AllowWebBrowserDrop=False` в Designer инстанцировала ActiveX прямо в ctor
  (эмпирически доказано). Фикс: #If-гейты всех 6 точек; строка из Designer
  удалена (net48 переустанавливает True в WireSurfaceDragDrop; окно без
  контента между ctor и Load - поведенчески ноль).
- P2 **Сортировка abc/xyz расходилась** (net48 NLS vs .NET ICU). Фикс: 
  `RuntimeHostConfigurationOption System.Globalization.UseNls=true` в modern
  vbproj - обе сборки сортируют идентично, общий код не тронут.
- P2 **DPI**: шипованный net48 exe НИКОГДА не встраивал app.manifest (байт-скан) =
  DPI-unaware. Фикс паритета: у modern убран ApplicationManifest,
  ApplyApplicationDefaults -> **DpiUnaware** (было PerMonitorV2). **PerMonitorV2 =
  осознанный будущий шаг** (владелец решает; тест вёрстки на 125/150%).
- P2 **FolderBrowserDialog.Description** невидим на .NET -> UseDescriptionForTitle
  (#If Not NETFRAMEWORK, 2 места).
- P2 **STJ эскейпил кириллицу в промпт Ollama** -> Encoder=UnsafeRelaxedJsonEscaping.
- P2 **Пустой 200-ответ Ollama ронял перевод** -> JsonDeserializeObject("") =
  Nothing как у JavaScriptSerializer.
- P3 Copyright выровнен (2013-2025 как в legacy), AssemblyTrademark("sza")
  добавлен, +git-sha убран из InformationalVersion, x86-тримминг publish
  (AfterTargets), мёртвые x86-нативы больше не едут.

**Осознанно отложено (НЕ баги паритета, задокументировано):**
- **Перемотки видео в modern нет** - у net48 сикбар давал IE `<video controls>`
  (только для H.264-мейнстрима; VLC-fallback и в net48 не умел перемотку). Все
  очевидные клавиши заняты замороженной картой хоткеев -> дизайн транспорта
  (полоска-оверлей на VLC-вью / клавиши) - отдельная задача этапа 8, решает
  владелец.
- Инфо-страница «видео открыто во внешнем плеере» (net48 рисовал её в WB) - в
  modern только статус-строка. Минорно; вернуть оверлеем при желании.
- Form.Closing (WFDEV004, 2 места) - работает, предупреждение; мигрировать на
  FormClosing при случае. ServicePointManager TLS-пин - no-op на .NET, загрузки
  работают на системных дефолтах TLS 1.2/1.3.
- CLAUDE.md «msbuild alone builds only LITE» устарел (sln теперь собирает и
  Modern) - поправить в этапе 11.

**Ручная проверка владельцем (набралось за Ф0-Ф4):**
- [ ] Анимированный webp: КАЧЕСТВО и скорость анимации (GIF-транскод, 256 цветов;
      тайминги скопированы из webp FrameDelay мс -> gif cs - проверить глазами).
- [ ] Видео в modern: VLC-путь (WebBrowser выключен навсегда), звук/громкость/луп.
- [ ] Вёрстка форм: шрифт зафиксирован MS Sans Serif 8.25 + PerMonitorV2 через
      ApplyApplicationDefaults - сверить с net48 на 100%/150% DPI.
- [ ] OCR/перевод end-to-end (Tesseract native загрузка на .NET 10, Ollama JSON
      через System.Text.Json-фасад).
- [ ] Ассоциации файлов/реестр: modern пишет те же ключи (общие с net48).

---

## ФОРМА ПОСТАВКИ (директива владельца 2026-07-16) - ДВА EXE РЯДОМ

Владелец задал форму дистрибуции; она **отменяет мою выдуманную схему с подпапкой
`modern\`** (её в спеке не было - это был костыль вокруг одинакового имени exe) и
уточняет §8.3 эпика (там legacy раздавался ОТДЕЛЬНО с сайта; теперь оба едут
вместе):

| Артефакт | Имя | Что это |
|---|---|---|
| `.NET 10 x64` мейнлайн | **`FastMediaSorter_LITE.exe`** | забирает ЗАМОРОЖЕННОЕ имя - заменяет установленный exe на месте (корреляция обновлений, ассоциации, MSIX Executable, ARP - всё цело) |
| `net48` legacy | **`FastMediaSorter_x86.exe`** | лёгкий вьювер для старых/32-битных Windows; `PlatformTarget=x86` (реально 32-битный процесс) |

- **Оба лежат в ОДНОЙ папке.** Смежные библиотеки общие - «если инсталлируются
  OCR, перевод, кодеки, SFTP-воркер». Это работает без единой правки резолвинга:
  `CurrentArchFolder()`/`CurrentVlcArchFolder()` уже выбирают по разрядности
  процесса, а в дереве лежат ОБЕ ветки (`libvlc\win-x64` + `win-x86`,
  `x64\tesseract50.dll` + `x86\`). x64-exe берёт x64, x86-exe берёт x86.
- **`bin\Release` = дистрибутивная форма** (msbuild кладёт x86-exe + все либы,
  build.ps1 доносит туда опубликованный x64-exe). `bin\ModernPublish` - полное
  standalone-дерево x64. `bin\SingleFile` - лёгкий x86-standalone (спека §8.3).
- **Единственность экземпляра:** мьютекс и реестр общие -> это ОДНО приложение,
  два окна одновременно не открыть (второй запуск форвардит файл в работающее).
  Так сохраняется 14-летнее поведение и нет гонки за сохранение настроек при
  закрытии. **Побочный эффект: сравнить старый и новый вьювер «бок о бок» на
  экране нельзя** - если для аудита нужно иначе, дать x86-сборке свой мьютекс
  (решение владельца, 5 минут работы).
- **КРИТИЧНЫЙ ФИКС этой смены:** форвардинг искал процесс `GetProcessesByName`
  ПО СВОЕМУ ИМЕНИ - с разными именами запуск одного при работающем другом тихо
  выходил бы без окна и без открытия файла. Теперь ищутся ОБА имени
  (`Viewer_Process_Names` в `Application_Events.vb`). Проверено вживую.

## Решения по открытым вопросам эпика (зафиксированы 2026-07-16)

| Вопрос | Решение | Основание |
|---|---|---|
| O-1 target | **net10.0-windows** | рекомендация эпика §2 |
| O-2 поставка | **self-contained single-file win-x64** | рекомендация эпика §8.1; паттерн уже обкатан Companion'ом |
| O-3 форматы | v1 = **ImageSharp 3.x** (webp вкл. анимированный); **ВЫПОЛНЕНО 2026-07-17**: Magick.NET-Q8-x64 добавлен за швом `IImageDecoder` - AVIF/HEIC/HEIF декодируются в modern (ftyp-sniff -> Magick, AutoOrient по `Is_Exif_AutoRotate`); x86 честно показывает «формат не поддерживается»; JXL пока не включён | O-3 «шире, но тяжелее» - добавлено отдельным шагом, как и планировалось |
| O-4 лицензия ImageSharp 3 | Split License: бесплатно при годовом доходе < $1M (Community). Наш случай - бесплатное приложение частного лица - проходит. **Показать владельцу для подтверждения** | условия Six Labors |
| O-5 заплатка webp в net48 | **не делаем** - modern закрывает | эпик §10 «если релиз близко - пропустить» |
| O-6 WebView2 | **выпиливаем из modern** (в legacy vbproj он только native-loader import, managed-кода нет вообще) | эпик §6.2; grep подтвердил 0 использований |
| O-7 механизм urезки legacy x86 | `#If FEATURE_FULL` + отдельная конфигурация того же net48-проекта (проще держать в синхроне) | этап «Legacy x86» ниже; НЕ в этой сессии |
| O-8 видео в legacy x86 | оставить LibVLC win-x86 (видео - ядро приложения) | вес вторичен против функции |

Решения зум/панорамы: все O-Z-* уже решены владельцем в спеке (колесо по умолчанию
листает, NumPad-зум, viewport-рендер, шаг 1.25/1.5, snap ±3%, границы 5%..4000%).

## Порядок работ (сводный план, отметки прогресса)

Этап 1 - **Ф0 швы** (в общем коде, legacy зелёный): [x] (коммит 9410829)
Этап 2 - **Ф1 modern-проект компилируется** (`dotnet build`): [x] (коммит 4191315)
Этап 3 - **Ф2 изображения** (ImageSharp 3.x, анимированный webp): [x] (в Ф0-швах; смоук OK)
Этап 4 - **Ф3 видео LibVLC-only** (WebBrowser дремлет, диспетчер мимо него): [x] (в Ф0-швах; видео-смоук - владелец)
Этап 5 - **Ф4 publish** (self-contained single-file, libvlc рядом): [x] (vbproj-условие RuntimeIdentifier=win-x64)
Этап 6 - **смоук** (запуск exe, лог, webp статик+аним): [x] (визуальная приёмка - владелец)
Этап 7 - **Ф5 упаковка**: build.ps1 [x] (publish в bin\ModernPublish + деплой в
        `<цель>\modern\`, exe всегда/статичные деревья при отсутствии; полный
        прогон зелёный, задеплоенная копия запускается); Build-OfflineRelease/
        Build-Installer/.iss/CI - [ ] (следующая итерация): [~]
Этап 8 - **зум/панорама Ф-Z1..Ф-Z5** (по спеке, только modern): [ ]
Этап 9 - **MKV/ISO Ф-A..Ф-G** (по спеке, только modern): [ ]
Этап 10 - **Legacy x86 viewer** (FEATURE_FULL-урезка, PlatformTarget=x86, standalone zip): [ ]
Этап 11 - **Ф6 каналы** (winget/Store на modern-ассет, сайт) + **Ф7 доки** (CLAUDE.md и пр.): [ ]

## Архитектурные решения реализации (чтобы не передумывать заново)

1. **Два проекта, одно дерево** (эпик §3). Legacy `src/FastMediaSorter.vbproj` не
   трогаем структурно (только DefineConstants + 3 новых Compile include). Modern -
   **`src/Modern/FastMediaSorter.Modern.vbproj`** (SDK-style) в ОТДЕЛЬНОЙ папке,
   чтобы не столкнуться obj/ с old-style проектом в src/. Исходники линкуются
   глобом `..\**\*.vb` с Exclude (Companion, obj, bin и файлы-исключения).
2. **Расхождения - `#If NETFRAMEWORK` / `#If Not NETFRAMEWORK`**. КРИТИЧНО:
   old-style проект НЕ определяет NETFRAMEWORK - добавляем руками в оба
   PropertyGroup (`<DefineConstants>NETFRAMEWORK=True</DefineConstants>` в
   VB-синтаксисе old-style это отдельное свойство DefineConstants со значением
   `NETFRAMEWORK=True`). SDK-проект определяет NET, NET10_0 и пр. сам.
3. **Шов декодера**: `src/Imaging/ImageDecoder.vb` - `IImageDecoder`
   (`DecodeToBitmap(MemoryStream) As Bitmap`, `TryGetPixelSize(Stream) As Size`) +
   `ImageDecoderProvider.Current`. Реализации в отдельных файлах, каждый целиком
   под своим `#If` (legacy vbproj их и так включает только явно, но `#If` страхует
   глоб modern-проекта). WPF (PresentationCore/WindowsBase) в modern НЕ подключаем.
4. **Шов видео**: полный IVideoSurface НЕ извлекаем (слишком вплетён в форму) -
   осознанное отступление от буквы Ф0 при сохранении цели: в modern диспетчер
   `MediaLoading` шлёт видео сразу в `PlayVideoWithVlcAsync` (`#If`), WebBrowser-
   ветки (`LoadVideoInWebBrowser`, `SetWebBrowserCompatibilityMode`-вызов,
   `AllowWebBrowserDrop`-wiring, `TryOpenVideoWithDefaultPlayer`-HTML) - под
   `#If NETFRAMEWORK`. Контрол Web_Browser остаётся в Designer (Designer общий,
   не трогаем!), но в modern после InitializeComponent сразу Visible=False и
   никогда не навигируется -> ActiveX-хэндл не создаётся вообще (ленивое создание
   хэндлов у невидимых контролов), IE-less системы в безопасности.
5. **RuntimeBootstrap**: AssemblyResolve-хук только `#If NETFRAMEWORK` (в modern
   managed-зависимости кладёт publish). `OpenBundledAsset` - общий; modern embed'ит
   ТОЛЬКО ассеты: `FmsPayloadflags/*.png`, `FmsPayloadhelp/port-forward.html`,
   `FmsPayloadicons/doc-html-translate.png` (те же LogicalName!). FmsPayloadmanaged/*
   в modern НЕ embed'ится.
6. **My-инфраструктура переезжает линком**: `My Project/Application.Designer.vb`
   (IsSingleInstance=True + MyApplication), `Settings.Designer.vb`, `Resources.Designer.vb`
   + `Resources.resx`. VB-правило имён ресурсов = RootNamespace.ИмяФайла (без пути) -
   `fmsl.Resources`, `fmsl.Main_Form`, `fmsl.Table_Form` - совпадает с old-style,
   ничего переименовывать не надо. `AssemblyInfo.vb`/`VersionInfo.vb` НЕ линкуем:
   SDK генерит атрибуты из свойств vbproj; версия YY.M.D.HHmm считается в vbproj
   (те же DateTime-функции, override через `-p:ReleaseVersion` как в legacy);
   Guid + ComVisible - в новом мелком `src/Modern/ModernAssemblyInfo.vb`.
7. **Пакеты modern**: SixLabors.ImageSharp 3.1.x, LibVLCSharp 3.9.3,
   LibVLCSharp.WinForms 3.9.3, VideoLAN.LibVLC.Windows 3.0.21, Tesseract 5.2.0,
   System.Security.Cryptography.ProtectedData. БЕЗ QRCoder (0 использований в просмотрщике),
   БЕЗ WebView2, БЕЗ ImageSharp 2. Registry/GetSetting - в net10-windows из коробки.
8. **Замороженные якоря в modern**: AssemblyName **FastMediaSorter_LITE**,
   RootNamespace fmsl, StartupObject fmsl.My.MyApplication, mutex тот же, реестр
   тот же, иконка та же, app.manifest линкуется (PerMonitorV2).
9. **Publish**: как у Companion, НО `IncludeNativeLibrariesForSelfExtract` НЕ
   включать безоглядно - libvlc обязан остаться ФАЙЛОВЫМ деревом рядом с exe
   (plugins-директория ищется на диске). Проверить, что VideoLAN targets кладут
   libvlc/win-x64 в publish-папку как loose-файлы.

## Карта точек `#If` (все места, где расходимся)

| Файл | Что | Статус |
|---|---|---|
| `src/FileManager.vb` | Imports WPF + LoadBitmapPortable/ViaWic/ViaImageSharp -> уходят в шов | [x] |
| `src/Utils.vb` | Imports WPF + ReadBitmapSourceSize -> в шов (TryGetPixelSize) | [x] |
| `src/Main_Form.MediaLoading.vb` (~880) | диспетчер видео: WB (net48) vs `PlayVideoWithVlcAsync(Current_File_Name)` (modern) | [x] |
| `src/Main_Form.VideoPlayer.vb` | LoadVideoInWebBrowser целиком + DocumentText-очистка в PlayVideoWithVlcAsync + HTML в TryOpenVideoWithDefaultPlayer | [x] |
| `src/Main_Form.Lifecycle.vb` | SetWebBrowserCompatibilityMode + вызов + `Web_Browser.ObjectForScripting = Me` | [x] |
| `src/Main_Form.DragDrop.vb` | AllowWebBrowserDrop wiring + Web_Browser_Navigating целиком | [x] |
| `src/Main_Form.vb` | добавлен явный `Sub New`: InitializeComponent + (modern) Web_Browser.Visible=False | [x] |
| `src/RuntimeBootstrap.vb` | AssemblyResolve только net48 | [x] |
| `src/OptionalRuntimeManager.vb` | оставлен как есть (страховка slim) | [x] |
| `src/FastMediaSorter.vbproj` | DefineConstants NETFRAMEWORK=True (Debug+Release) + 3 Imaging-файла | [x] |

Новые файлы Ф0: `src/Imaging/ImageDecoder.vb` (IImageDecoder + ImageDecoderProvider),
`src/Imaging/LegacyWicImageDecoder.vb` (#If NETFRAMEWORK), 
`src/Imaging/ModernImageSharpDecoder.vb` (#If Not NETFRAMEWORK; ImageSharp 3:
статика -> PNG -> Bitmap; анимация -> GIF-транскод с копированием webp-таймингов
кадров; TryGetPixelSize через Image.Identify). Проверить при смоуке: скорость
анимации webp (маппинг GetWebpMetadata/FrameDuration - названия API по памяти,
компиляция подтвердит).

## Грабли/находки (пополнять!)

- Old-style vbproj НЕ определяет `NETFRAMEWORK` - обязателен ручной DefineConstants
  (иначе `#If NETFRAMEWORK` в legacy тихо равен False и legacy соберёт modern-ветки).
- `System.Web.Extensions` referenced в legacy, но `JavaScriptSerializer` в src НЕ
  используется (наследие до-Companion) - в modern ничего не надо.
- WebView2 в legacy - только import .targets (native loader), managed-Reference
  отсутствует - в modern просто не подключаем.
- QRCoder: 0 использований в src/*.vb после миграции Share (только мёртвый
  FmsPayloadmanaged-embed в legacy) - в modern не нужен.
- `Utils.ReadBitmapSourceSize` - второе (кроме FileManager) место WPF.
- Двойной клик по видео из WebBrowser шёл через `window.external` +
  `HandleWebBrowserDoubleClick` - в modern этого пути нет (VLC-вью уже имеет свой
  обработчик), функция остаётся (безвредна).
- В `Main_Form.Designer.vb` Web_Browser создаётся всегда - НЕ редактировать Designer
  (общий с legacy); гасим видимость после InitializeComponent.

## Журнал сессий

### Сессия 1 - 2026-07-16
- Прочитаны все 3 спеки; разведка legacy vbproj/Application_Events/RuntimeBootstrap/
  FileManager/VideoPlayer/MediaLoading/DragDrop/app.manifest; решения зафиксированы.
- Создана ветка `feature/dotnet10-modern`, заведён этот файл.
- Ф0 (коммит `9410829`): IImageDecoder-шов + все #If-гейты + NETFRAMEWORK=True в
  legacy vbproj; legacy собран и смоук-запущен.
- Ф1 (коммит `4191315`): `src/Modern/FastMediaSorter.Modern.vbproj` компилируется;
  JSON-фасад (JavaScriptSerializer/System.Text.Json) в TranslateHttp; фиксы
  портируемости (Trace.Listeners, AppContext.BaseDirectory, System.Windows.Forms-
  квалификация, ImageSharp GetFormatMetadata/FrameDelay); ApplyApplicationDefaults
  (PerMonitorV2 + MS Sans Serif 8.25); проект в sln.
- Ф4: publish отработал (113 МБ exe + libvlc-дерево); published-смоук зелёный;
  webp статик+аним открылись без ошибок; single-instance форвардинг работает.
- Грабли, добавленные в этой сессии: SDK-VB не имеет WinForms в implicit-Imports
  (нужны Import-итемы System.Drawing/System.Windows.Forms/System.Data); WFO1000
  (error-severity) стреляет на WithEvents-полях -> NoWarn; ProtectedData на
  net10.0-windows inbox (NU1510); на net10 корневой namespace `Windows.` затенён -
  писать `System.Windows.Forms.`; `Debug.Listeners` нет на .NET -> Trace.Listeners;
  WebpFrameMetadata.FrameDelay (мс, uint) vs GifFrameMetadata.FrameDelay (сс, int).

### Session - 2026-10-02 (specification 014, S11: the two parts that were left)
Written in English, like every file the repository's rules cover; the entries above predate that.

- **U-03(c)**, the folder walk off the UI thread: `Main_Form.FolderScanAsync.vb`. The UI thread waits
  100 ms for the scan (the fast path is byte-for-byte the old synchronous behaviour); a slower scan
  returns the call, shows `чтение каталога.. ждите!`, and re-runs the same read mode when the
  listing lands. Seams: `EnsureFolderList` (called from `UpdateFileIndexAndList`), a gate at the
  top of `ReadShowMediaFile`, `TakeScannedListing` (consumed by `ProviderFileEntries`). Details and
  the reasons for each rule: 014 §9.1.
- **U-01**, the decoded-picture cache: `MediaCache.vb` + `Main_Form.MediaCache.vb`. Built by MOVING a
  picture between a picture box and the cache (one owner at a time), not by sharing it as 014 §2
  sketched - see 014 §9.2. The prefetch now follows the direction of travel.
- New whole-file modern seams are listed in `src/FastMediaSorter.vbproj` by hand (they compile to
  nothing there) and `MediaCache.vb` is linked into `tests/Lite.Tests`.
- Measurements. The network folder the specification is about was **not measurable**: `\\p7` did not
  answer on this day (the destination-slot probe of `\\p7\down` timed out). What was measured is
  local, on 12 JPEGs of 2400x1600, from the Debug build's own log lines: decode-and-place of one
  photo ~23 ms (`w1030` -> `w0900`), a cache hit ~3-4 ms (`n0050` -> `w0900`); a backwards flip
  after a forward run is a cache hit, the second one a prefetch hit. The before/after numbers on a
  network share (time to first frame, paging back) remain to be taken by the owner with acceptance
  014 §6 - on `\\szamedia\common`, which the owner decided on 2026-10-02 to use instead of `\\p7`
  once it is up (it is not yet).
- Seen, NOT investigated: the Debug log shows `w0946: background analysis skipped
  [OverflowException]` for some photos in the smoke runs. It is swallowed by the try/catch around
  the dynamic-background analysis in `UpdateControlVisibility` and the picture is shown normally.
  It looks deterministic per PICTURE, not per path: of the four photos the runs displayed, `p02`
  threw both through the prefetch box and after a cache hit, `p04` through the prefetch box, and
  `p01` and `p03` never did, by any path -
  so it is a property of those pictures' colours in that analysis, not of the cache. The cause
  was not chased (out of this ticket); no baseline build was run.


Modern main-window chrome update (2026-10-02): the toolbar and status strip now float
over the media through the shared chrome host. The mainline selects PerMonitorV2 in
ApplyApplicationDefaults and remeasures/reanchors the chrome on monitor DPI changes.
The DPI-unaware entries above describe the earlier migration baseline; the x86
viewer retains that baseline. Actual-window chrome scenes run separately through
`tests/Integration/ModernChromeScenes.ps1`, without reading or saving viewer settings.

### Session - 2026-10-03 (specification 020, S9 remainder: U-04 live folder list, U-05 sorting large folders)
- Implemented per `020_SPECIFICATION_VIEWER_CORE_S9_MODERN_FOLDER.md` (local specification; section 8 there):
  `src/FolderLiveList.vb` (pure logic, tests `FolderLiveListTests`) + `src/Main_Form.FolderWatch.vb`; both
  builds compiled and the unit suites were green at the time (viewer net48 459 + net10 1206, Share Manager 282 -
  counted on the post-reset tree, before the 2026-10-02 work was restored).
- Memory and sort-time measurement (synthetic rows shaped like `\\p7\_i\output\C\sub\IMG_..._N.jpg`,
  `OrderBy(FileName)` with the culture comparison `GetFiles` uses; x64, PowerShell 7 / .NET 10):
  | files | scan rows (FileEntry) | path list after sort | OrderBy + ToList | OrderBy + ToArray |
  |---|---|---|---|---|
  | 100 000 | ~20 MB | ~0 MB | 263 ms | 187 ms |
  | 500 000 | ~102 MB | ~3 MB | 873 ms | 1019 ms |
  Conclusion: the 100k cap can be lifted in the mainline (peak ~100 MB for 500k rows, sort ~1 s); the safety
  maximum is 2 000 000 (~400 MB of rows). The measurement was taken on a tree where the scan still ran on the UI
  thread; S11 U-03(c) above has since moved the folder walk off it.
- Smoke of the real window (Debug build in a scratchpad, the owner's registry saved and restored byte for byte):
  opened a file in a folder of 6 pictures; copied 3 new ones -> `live folder list +3 -0, now 9`; deleted a file
  not on screen -> `+0 -1, now 8`; a file created empty and then filled -> `+1 -0, now 9`; a `.txt` was ignored.
- Not verified: acceptance on a network share (`\\szamedia\common` is not up yet, `\\p7` did not answer), a full
  `.\build.ps1`.

### Session - 2026-10-07 (specification 018, S7: U-13 and U-15 - and why U-15 does not copy the raster)
- 018 is closed. Its approved half (U-01) was built in S11 (S11 section 9.2, with the deviation from 018 section 2);
  U-13 and U-15 were the rest of the document and are finished here.
- **U-13 (do not keep the source bytes of a decoded picture):** on the mainline the decision is made in
  `DecodeCache.Materialize` - a static WEBP/AVIF/HEIC becomes a detached `Bitmap` and no stream is returned (the
  file's bytes are not kept beside the pixels while the picture is on screen), an animation keeps its GIF payload
  stream because GDI+ reads later frames from it. `IImageDecoder.ImageOwnsData` is the seam's declaration (modern
  True, WIC False); the check in `FileManager` sits in the `#If NETFRAMEWORK` branch and is inert there. Pinned by
  `ImageDecoderSeamTests.U13_a_static_webp_holds_no_stream_but_an_animation_keeps_its_own` (net10). Not profiled:
  the "no copy of the file's bytes in a dump" check of acceptance 4 is by reading the code, not by a profiler.
- **U-15 (LockBits instead of GetPixel) - built, measured, and changed:** `EdgeSampler` first copied the whole
  raster (one `LockBits` + `Marshal.Copy`, as the specification wrote). Measured in a throw-away harness
  (synthetic uniform bitmaps, median of 7, copy as `EdgeSampler.TryCreate` did it vs `Bitmap.GetPixel`):

  | bitmap | copy the raster | 3 reads (automatic scheme) | ~40 reads (side/top/bottom scheme) | 2 edge columns x H (perspective) |
  |---|---|---|---|---|
  | 6000x4000, 24 bpp | 37.6 ms (~96 MB) | 0.003 ms | 0.011 ms | 2.4 ms |
  | 6000x4000, 32 bpp | 18.6 ms | 0.001 ms | 0.011 ms | 2.5 ms |
  | 3840x2160, 24 bpp | 13.8 ms | 0.001 ms | 0.006 ms | 1.4 ms |
  | 1920x1080, 32 bpp (panel) | 1.2 ms | - | - | 0.43 ms |

  Both callers read a few dozen pixels, never a large share of the bitmap, so the copy cost thousands of times what
  the reads saved - on the default colour scheme it would have added 14-38 ms and up to ~96 MB (large-object heap)
  to every page flip, and was still 0.7 ms slower on the 1080p perspective edges. `EdgeSampler` therefore keeps the
  sampling rules in one place (shared by the dynamic background and the perspective bars) but reads through
  `Bitmap.GetPixel` and copies nothing. Specification 018 section 4's mechanism is dropped for that reason; its goal
  (no per-pixel overhead worth speaking of) was never present in this access pattern. This agrees with S11 section 0,
  which had already deferred U-15 as unproven.
- **A parity bug found on the way:** the side/top/bottom averages of `EdgeSampler` used integer division (truncation)
  where the code it replaced used `CInt(sum / count)` (rounding), so a tint could come out one level darker. Fixed;
  `U15_EdgeSampler_side_top_bottom_averages_round_like_the_original` pins it.
- Also removed: two unused private wrappers in `Main_Form.PerspectiveBackground.vb` that made a fresh sampler per
  call; the two edges of one perspective frame now share one sampler.
- Verified: `dotnet test` (EdgeSampler, decoder seam, async-core suites) green on net10 and net48; `dotnet build` of the
  modern project and `msbuild` of the x86 project: 0 errors.
- Not verified: acceptance 1-7 of 018 on a network share (`\\szamedia\common` is still the agreed place), a
  before/after page-flip timing on a share, a full `.\build.ps1`.
