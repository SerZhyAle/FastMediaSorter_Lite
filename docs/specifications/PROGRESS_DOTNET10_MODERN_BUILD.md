# ПРОГРЕСС: современная сборка на .NET 10 (реализация эпика)

> **Назначение файла**: живой журнал реализации. Обновляется после КАЖДОГО шага,
> чтобы работу можно было продолжить с того же места после обрыва сессии/токенов.
> Читать сверху вниз: «Текущее состояние» + «Следующий шаг» - всё, что нужно для
> продолжения. Работа ведётся в ветке **`feature/dotnet10-modern`** (main не трогаем
> до готовности; владелец мержит сам).

Спецификации-источники (все решения - там):
- [SPECIFICATION_DOTNET10_MODERN_BUILD.md](SPECIFICATION_DOTNET10_MODERN_BUILD.md) - эпик (фазы Ф0-Ф7)
- [SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md](SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md) - зум/панорама (Ф-Z0..Ф-Z5, все O-Z-* решены)
- [SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md](SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md) - MKV/ISO (Ф-A..Ф-G)

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

## СЛЕДУЮЩИЙ ШАГ

Ф-Z5 (тесты на математику зума + справка `lbl_Help_Info`), затем этап 9 (MKV/ISO -
сперва закрыть O-ISO-1..7), этап 10 (урезка x86 по `FEATURE_FULL` - **имя и
разрядность уже сделаны**; заодно выкинет из x86 уязвимый ImageSharp 2.1.8),
этап 11-остаток (winget/Store при релизе).

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
| O-3 форматы | v1 = **ImageSharp 3.x** (webp вкл. анимированный); Magick.NET (HEIC/AVIF/JXL) - отложен, шов готов | O-3 «шире, но тяжелее» - добавится отдельным шагом |
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
Этап 10 - **Legacy x86 LITE** (FEATURE_FULL-урезка, PlatformTarget=x86, standalone zip): [ ]
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
   System.Security.Cryptography.ProtectedData. БЕЗ QRCoder (0 использований в LITE),
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
