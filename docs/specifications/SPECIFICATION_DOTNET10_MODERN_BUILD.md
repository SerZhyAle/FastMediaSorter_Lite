# Спецификация: параллельная современная сборка на .NET 10 (legacy net48 остаётся)

Статус: план (не начато)
Дата: 2026-07-14, ревизия 2

> Ревизия 2 (2026-07-14): переопределён смысл legacy-сборки. Раньше legacy = текущая
> полная net48-сборка «как есть» (со всеми фичами) под x64 для Windows 7/8.1. Теперь
> legacy = **отдельный лёгкий x86-вьювер на .NET Framework 4.8, standalone-exe, без
> OCR, перевода и Android Folder Share (SFTP)** - «работает везде» минимальный
> просмотрщик для 32-битных и старых Windows, раздаётся только с сайта и GitHub.
> Полный набор фич живёт в modern-сборке (.NET 10, x64). См. новый раздел 1.1.

## 0. Что послужило триггером

На Windows Server 2025 приложение (сборка net48, поставлена штатным
установщиком) не открывает **анимированные** WEBP. Лог показал точную причину -
падают оба пути декодирования:

- **WIC** (`FileManager.LoadBitmapViaWic`, через WPF `BitmapDecoder`): `0x88982F8B`
  «не найден компонент обработки изображений». На Server 2025 нет WIC-кодека WebP
  (на клиентских Windows он приезжает Store-компонентом «WebP Image Extensions»).
- **ImageSharp 2.1.8** fallback (`LoadBitmapViaImageSharp`):
  `NotSupportedException: Animated webp are not yet supported`.

Статические WEBP на сервере открывались бы (их тянет ImageSharp 2.x без кодека ОС);
ломаются только анимированные. Обновить пакет нельзя: поддержка анимированного
WEBP появилась в **ImageSharp 3.x**, а он **не таргетит .NET Framework 4.8**
(только .NET 6+).

Это частный симптом общей проблемы: net48 отрезан от современного стека
декодеров изображений/видео. Точечная заплатка для net48 возможна (см. раздел 10),
но стратегически правильнее вынести приложение на .NET 10, где эта и подобные
проблемы (AVIF, HEIC, анимированный PNG, современные видеокодеки) закрываются
штатными актуальными пакетами.

## 1. Цель и рамки

- **Legacy = отдельный лёгкий x86-вьювер на .NET Framework 4.8** (заморожен,
  чинить только критичное). Это НЕ полная текущая сборка, а урезанный
  standalone-просмотрщик **без OCR, перевода и Android Folder Share (SFTP)** -
  «работает везде» вариант для 32-битных и старых Windows (7/8.1) и машин без
  .NET 10. Раздаётся только с сайта и GitHub. Подробно - раздел 1.1.
- **Создать параллельную современную сборку на .NET 10 (LTS)** из того же
  дерева исходников, с современными пакетами для изображений, анимаций и видео -
  это **полнофункциональный мейнлайн** (OCR, перевод, Share на месте), только x64.
- **Каналы витрин (winget, Microsoft Store) перевести на новую сборку**; сайт
  раздаёт обе (новая - основная полнофункциональная, legacy - вторая ссылка
  «лёгкий просмотрщик для старых/32-битных Windows»).
- Публичное имя не меняется: **Fast Media Sorter for Windows**. Все
  замороженные технические якоря (exe, mutex, реестр, ProgID, AppId,
  UninstallDisplayName, идентичность MSIX, winget PackageIdentifier/PackageName)
  **переходят на новую сборку без изменений** - это обеспечивает апгрейд на месте,
  сохранение настроек пользователя и корреляцию обновлений (см. раздел 8).

Вне рамок: переписывание UI на WPF/MAUI/WinUI. Новая сборка остаётся
**VB.NET WinForms** - WinForms и VB (включая `My`/Application Framework)
полноценно поддерживаются на .NET 10. Меняется платформа и часть подсистем,
а не UI-фреймворк.

## 1.1 Legacy = лёгкий x86 standalone-вьювер (переопределение, рев. 2)

Модерн-сборка (.NET 10) поддерживает только Windows 10 1607+/11/Server 2016+ и
поставляется как **x64**. Это оставляет две непокрытые категории машин: 32-битные
Windows и старые ОС (Windows 7/8.1). Их закрывает legacy - но не «полная сборка как
раньше», а сознательно **минимальный** артефакт:

- **Платформа**: .NET Framework 4.8, собранная под **x86** (`PlatformTarget=x86`,
  либо AnyCPU + `Prefer32Bit`), чтобы гарантированно запускаться и на 32-битной, и
  на 64-битной Windows как 32-битный процесс. Соответственно - только `libvlc\win-x86`.
- **Форма поставки**: **standalone-exe** (copy-and-run, без инсталлятора, без
  админ-прав, без записи в реестр сверх пользовательских настроек). Раздаётся
  **только с сайта и GitHub-релиза**, в winget/Store не идёт.
- **Урезанный набор фич - чистый просмотрщик/сортировщик**. Вырезаны:
  - **OCR** (Tesseract, `src/Ocr/*`, tessdata - минус ~200 МБ моделей),
  - **Перевод** (`src/Translate/*`, Ollama/LibreTranslate),
  - **Android Folder Share / SFTP** (весь companion + Go-воркер + `ServerFeatures`
    гейт + firewall/opt-in - минус ~120 МБ self-contained companion).
  Остаётся: просмотр изображений и видео, сортировка/файловые операции, слайдшоу,
  перспективный фон, ассоциации файлов, single-instance, drag-drop.
- **Результат по весу**: без tessdata и без companion legacy-артефакт - это по сути
  exe + `libvlc\win-x86` (видео) + мелкие managed-зависимости. Порядок - десятки МБ
  вместо сотен.

Реализационно это отдельная **конфигурация того же net48-проекта** (или отдельный
build-профиль), где OCR/Translate/Share-код исключается из компиляции директивой
(напр. `#If FEATURE_FULL` вокруг вызовов и кнопок) или условной ссылкой, а не
удаляется из дерева. Кнопки/пункты этих фич в UI скрываются в облегчённой сборке.
Точный механизм отсечения - открытый вопрос **O-7**.

## 2. Выбор целевой платформы: .NET 10 (LTS)

На июль 2026 расклад LTS/STS такой:

| Версия | Тип | GA | Конец поддержки |
|--------|-----|----|-----------------|
| .NET 8 | LTS | ноя 2023 | ноя 2026 (осталось ~4 мес.) |
| .NET 9 | STS | ноя 2024 | май 2026 (**уже EOL**) |
| .NET 10 | LTS | ноя 2025 | ноя 2028 |

Решение: **`net10.0-windows`**. Это текущий LTS, самый долгоживущий и «самый
распространённый на перспективу» вариант. Консервативная альтернатива - `net8.0-windows`
(тоже LTS, но окно поддержки закрывается уже в этом году, брать не стоит).

Минимальная ОС для .NET 10: Windows 10 версии 1607+, Windows 11, Windows Server
2016+. То есть **Server 2025 поддержан полностью** - исходная проблема с
анимированным WEBP на нём в новой сборке исчезает (декодит ImageSharp 3.x,
кодек ОС не нужен). Windows 7/8.1 остаются за legacy-сборкой.

## 3. Стратегия сосуществования: два проекта, одно дерево исходников

Требование «legacy заморожен, новая рядом» лучше всего выполняется так:

- **Legacy-проект остаётся как есть**: `src/FastMediaSorter.vbproj` (old-style,
  `packages.config`, ILMerge-таргет, встроенные ресурсы `FmsPayload*`) - **не
  трогаем**, чтобы не расшатать работающую net48-сборку.
- **Новый SDK-style проект** `src/FastMediaSorter.Modern.vbproj`
  (`<Project Sdk="Microsoft.NET.Sdk">`, `TargetFramework=net10.0-windows`,
  `UseWindowsForms=true`, `PackageReference`) **линкует те же `.vb`-файлы** через
  glob (`<Compile Include="**\*.vb" />` относительно `src/`), исключая расходящиеся
  файлы и добавляя их современные замены (раздел 6).
- Расходящийся код в общих файлах разделяется директивами
  `#If NETFRAMEWORK Then ... #Else ... #End If` (или `#If NET Then`). ~90% кода
  общее: одна правка/фикс попадает в обе сборки.

Почему не мультитаргет одного проекта (`<TargetFrameworks>net48;net10.0-windows</TargetFrameworks>`):
это потребовало бы конвертировать текущий рабочий проект в SDK-style и увязать
ILMerge (работает только на net48) с single-file publish (.NET 10) в одном файле -
риск для замороженного legacy. Два проекта с общими исходниками дают ту же
экономию кода без риска для legacy.

Solution `FastMediaSorter.sln` в корне получает оба проекта. `build.ps1`/CI
собирают нужный по параметру (раздел 8).

## 4. Что переносится на .NET 10 без изменений (или почти)

- **WinForms UI целиком** (`Main_Form.*`, `Table_Form.*`, все формы, Designer/resx).
- **VB `My`/Application Framework и single-instance** (`Application_Events.vb`):
  `WindowsFormsApplicationBase`, `My.Application`, mutex, `WM_COPYDATA`-форвардинг -
  поддерживаются на .NET.
- **P/Invoke** (`ShowWindow`, `SetForegroundWindow`, `EnumWindows`,
  `ChangeWindowMessageFilter`, `LoadLibrary` и т.д.) - без изменений.
- **Реестр**: добавить пакет `Microsoft.Win32.Registry` (в .NET он вынесен из BCL).
  Пути реестра (`SZA\FastMediaSorter`) те же - настройки пользователя переносятся
  как есть.
- **DPAPI** (`Security/DpapiSecrets.vb`): добавить пакет
  `System.Security.Cryptography.ProtectedData`.
- **OCR**: `Tesseract 5.2.0` работает на .NET 6+ (netstandard2.0). Логика
  `src/Ocr/*`, `OcrTranslateSettings`, скачивание tessdata - без изменений.
- **QR**: `QRCoder` поддерживает net5+ - без изменений.
- **Перевод** (`src/Translate/*`, HttpClient, Ollama/LibreTranslate) - без изменений.
- **Android Folder Share**: Go-воркер `fms-share-worker.exe` - отдельный exe,
  от TFM приложения не зависит, переносится дословно. Named-pipe IPC, `ShareController`,
  `ShareConfigBuilder` и т.д. - без изменений (named pipes есть в .NET).
- **Логирование** (`AppFileLogger.vb`) - без изменений.

## 5. Что упрощается или отмирает на .NET 10

- **ILMerge → встроенный single-file publish.** ILMerge на .NET 5+ не работает;
  вместо него `PublishSingleFile=true` (+ по желанию `IncludeNativeLibrariesForSelfExtract`).
  Это делает **`RuntimeBootstrap.vb` (встроенные `FmsPayloadmanaged/*.dll` через
  `AssemblyResolve`) в новой сборке ненужным** - managed-зависимости кладёт сам
  publish. Ассеты (`FmsPayloadflags/`, `FmsPayloadhelp/port-forward.html`) остаются
  встроенными ресурсами, но `OpenBundledAsset` для них сохраняется - это тонкий
  слой, менять не нужно.
- **`OptionalRuntimeManager.vb` (докачка native OCR/VLC с NuGet на лету) - в новой
  сборке опционален**: при self-contained поставке native tesseract/libvlc кладутся
  рядом штатно. Механизм докачки можно оставить как страховку для slim-сборки.
- **WPF WIC-путь декодирования (`System.Windows.Media.Imaging`) убирается** -
  вместе с зависимостью от кодеков ОС (первопричина бага на Server 2025).

## 6. Новый стек пакетов (изображения, анимации, видео)

Ядро миграции. Расходящиеся подсистемы прячутся за тонкими интерфейсами, чтобы
общий код их не замечал.

### 6.1 Изображения и анимации

| Задача | Legacy (net48) | Modern (.NET 10) |
|--------|----------------|------------------|
| Основной декодер | WIC → ImageSharp 2.1.8 | **ImageSharp 3.x** |
| Анимированный WEBP | нет (баг) | **да, нативно** |
| Анимированный PNG (APNG) | нет | да |
| GIF-анимация | GDI+ | ImageSharp 3.x / GDI+ |
| HEIC / AVIF / JXL | нет | **Magick.NET** (fallback) |

- **SixLabors.ImageSharp 3.x** - основной кросс-форматный декодер (WebP вкл.
  анимированный, APNG, GIF, BMP, TGA, PBM, QOI). Чистый managed, кодек ОС не нужен.
  Лицензия: с v3 - Six Labors Split License (бесплатно для проектов, попадающих под
  их условия; **проверить применимость к нашему кейсу** - см. открытый вопрос O-4).
- **Magick.NET (`Magick.NET-Q8-x64`)** - fallback для экзотики: HEIC, AVIF, JXL,
  TIFF-варианты, PSD. Тяжёлый native, но огромный охват. Подключать как второй
  эшелон декодера (после ImageSharp), лениво.
- Рендер оставляем на `System.Drawing`/GDI+ (`HqPictureBox`): декодер отдаёт
  `System.Drawing.Bitmap`, остальной пайплайн (perspective-фон, overlay, HQ-scaling)
  не меняется. `System.Drawing.Common` на .NET 7+ Windows-only - для нашего
  Windows-приложения это ок; добавить пакет `System.Drawing.Common`.

Интерфейс-шов: `IImageDecoder` (напр. `DecodeToBitmap(bytes) As Bitmap`) с двумя
реализациями. Legacy-реализация = текущий WIC+ImageSharp2 код; modern = ImageSharp3
(+Magick fallback). `FileManager.LoadImageWithStream` вызывает шов, а не конкретику.

> **Пересмотр зума/панорамы и реакции мыши/клавиатуры при просмотре изображений.**
> Отдельное пополнение к этой спецификации - [SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md](SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md).
> Кратко: добавляем клавиатурный зум на **NumPad** (`+`/`-` от позиции курсора,
> `/` = Fit, `*` = 100 %) и опциональный «классический» зум на колесе. **По
> решению владельца мышь по умолчанию не меняется** - колесо листает как сейчас
> (зум-на-колесе строго опт-ин), клики остаются навигацией, двойной клик остаётся
> полноэкраном; никаких `Ctrl`-комбо (заняты). Внутри - переход от
> зума-через-ресайз-контрола к viewport-трансформации (`srcRect→destRect`), что
> снимает магический `zoom_Scale = 0` и рассинхрон геометрии. Legacy x86-вьювер не
> трогаем. Все открытые вопросы O-Z-* закрыты дефолтами (шаг зума 1.25, snap к
> Fit/100 %, границы 5 %..4000 %).

### 6.2 Видео

| Задача | Legacy (net48) | Modern (.NET 10) |
|--------|----------------|------------------|
| H.264/MP4 | IE WebBrowser (ActiveX) | **LibVLC** |
| Прочие кодеки (AVI/MKV/VP9/…) | LibVLC fallback | LibVLC |
| HTML-контент (если нужен) | WebBrowser | WebView2 (Chromium) |

- **IE WebBrowser убирается.** Internet Explorer/MSHTML удалён из современных
  Windows, ActiveX-хост ненадёжен, а на .NET он тем более легаси. H.264 в новой
  сборке играет **LibVLC** - он и так уже основной fallback. Итог: **единый
  видеодвижок LibVLC**, двойной путь `Main_Form.VideoPlayer.vb` схлопывается в один
  (минус много кода и багов drag-drop через ActiveX).
- **LibVLCSharp 3.x + VideoLAN.LibVLC.Windows** поддерживают .NET 6+ - переносятся,
  становятся первичными.
- **Microsoft.Web.WebView2** - только если где-то реально нужен HTML5-рендер;
  сейчас в CLAUDE.md он помечен «mostly unused», в новой сборке скорее выпиливается.

Интерфейс-шов: `IVideoSurface` (Play/Pause/Stop/Seek/Volume/Mute). Legacy =
WebBrowser+VLC; modern = только VLC. `Main_Form.VideoPlayer.vb` разбивается на
общий контроллер + платформенную реализацию за `#If`.

> **Расширение видео-подсистемы: .mkv и .iso (локальные и удалённые).** Отдельное
> пополнение к этой спецификации - [SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md](SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md).
> Кратко: MKV уже играет через LibVLC (в modern - без WebBrowser-round-trip);
> удалённые URI открываются после снятия гейта `File.Exists`. **VLC не монтирует
> ISO** - он читает образ сам (libdvdread/libbluray), поэтому DVD/BD-ISO играются
> прямым MRL (`dvd:///..iso`, `bluray:///..iso`) без монтирования; монтирование ОС
> (`AttachVirtualDisk`) или managed-парсер DiscUtils нужны только для **data-ISO**
> (обычные файлы внутри, куда VLC не заглянет), причём DiscUtils закрывает и
> удалённый data-ISO. **Важный факт:** наш `VideoLAN.LibVLC.Windows` содержит плагин
> Blu-ray, но **не содержит DVD-плагинов** (`libdvdnav`/`libdvdread`) - их надо
> довезти для DVD-ISO (это навигация, не дешифровка - Store-safe). Граница охвата
> проходит по DRM: незашифрованные образы/контейнеры - поддерживаем; дешифровка
> коммерческих CSS/AACS-дисков (`libdvdcss`/`libaacs`) - **вне рамок** (проект в
> Store, ничего с обходом DRM не делаем: ни бандла, ни докачки, ни хука). Подробности,
> план фаз и открытые вопросы O-ISO-* - в дополнении.

### 6.3 Итоговая выгода для пользователя (аргумент миграции)

Новый стек не просто чинит анимированный WEBP на Server 2025, а добавляет:
AVIF/HEIC/JXL, анимированный PNG, надёжное видео без IE, и снимает зависимость от
Store-кодеков и от `.NET Framework` в системе.

## 7. Расходящиеся файлы (черновой список для швов)

Требуют `#If` или отдельной modern-реализации:

- `src/FileManager.vb` - путь декодирования (`LoadBitmapPortable`/`ViaWic`/`ViaImageSharp`).
- `src/Main_Form.VideoPlayer.vb` - WebBrowser vs LibVLC-only.
- `src/Main_Form.DragDrop.vb` - ветка `AllowWebBrowserDrop`/`Navigating` (нет
  WebBrowser в modern).
- `src/RuntimeBootstrap.vb` - в modern не нужен для сборок; оставить только
  `OpenBundledAsset` (ассеты) или заменить на прямое чтение из ресурсов.
- `src/OptionalRuntimeManager.vb` - опционален в modern.
- `.vbproj` - два файла (раздел 3).
- `Security/DpapiSecrets.vb`, обращения к реестру - те же исходники, но зависят от
  доп. пакетов в modern (не `#If`, а PackageReference).

Всё остальное (~все `Main_Form.*` кроме двух, все формы Share/OCR/Table, Utils,
Common_Module, FileManager кроме декодера) - общее.

## 8. Упаковка и дистрибуция

### 8.1 Модель поставки новой сборки

**Self-contained single-file** (`PublishSingleFile=true`,
`SelfContained=true`, `RuntimeIdentifier=win-x64`): в комплекте среда .NET 10,
на машине ничего доустанавливать не нужно, работает офлайн - совпадает с
офлайн-первым принципом приложения и снимает целый класс проблем winget с
зависимостями (см. CLAUDE.md про VCRedist-петлю). Цена - больше вес загрузки
(ориентир: база .NET desktop ~70-90 МБ + native libvlc/tesseract + tessdata).
Альтернатива - framework-dependent (маленький, но требует «.NET Desktop Runtime
10» на машине) - **не берём** для дистрибутива, возможно как отдельный slim-вариант.
См. открытый вопрос O-2.

### 8.2 Замороженные якоря переходят на новую сборку

Чтобы существующие пользователи получили новую сборку как **апгрейд на месте** и
сохранили настройки, идентичность не меняется:

- exe **`FastMediaSorter_LITE.exe`**, mutex `FastMediaSorterSingleInstanceMutex`,
  реестр `SZA\FastMediaSorter`, ProgID `FastMediaSorter.*` - без изменений.
- Inno: `AppId {7371E7F1-…}`, `UninstallDisplayName`, `DefaultDirName`
  (`FastMediaSorter_LITE`), `OutputBaseFilename` - без изменений. Установщик новой
  сборки апгрейдит legacy-инсталляцию на месте (тот же AppId).
- MSIX identity (`Name`/`Publisher`), манифест `DisplayName` - без изменений;
  remap версии `YY.(M*100+D).HHmm.0` сохраняется.
- winget `PackageIdentifier`/`PackageName`/`Moniker` - без изменений; переход на
  новую сборку = очередная версия того же пакета (для пользователя - обычный
  `winget upgrade`).

### 8.3 Разведение артефактов legacy vs modern

- **Modern - мейнлайн**, держит замороженную базу имени ассета
  `FastMediaSorter-<ver>-windows-x64.*` (portable zip + Inno setup.exe), на неё
  указывают winget и Store. Полный набор фич (OCR/перевод/Share).
- **Legacy - лёгкий x86 standalone** (см. 1.1): один exe (+ `libvlc\win-x86` рядом),
  **без инсталлятора**, напр. `FastMediaSorter-<ver>-legacy-net48-windows-x86.zip`
  (или самораспаковывающийся exe). Без OCR/tessdata, без перевода, без companion/SFTP.
  Только на сайте/в GitHub-релизе, в витрины не идёт.
- Версия `YY.M.D.HHmm` общая для обеих (собираются из одного тега).

### 8.4 CI (`release.yml`)

1. Собрать modern (`dotnet publish` self-contained, x64) и legacy (net48, **x86**,
   облегчённая конфигурация без OCR/Translate/Share - см. 1.1 и O-7).
2. Modern: прогнать `Prepare-OcrOfflinePayload.ps1` (tessdata, обрезка x86).
   Legacy: **без** tessdata и companion; только `libvlc\win-x86` рядом с exe.
3. Modern: Inno `setup.exe` + portable zip под замороженными именами.
4. Legacy: **portable zip / standalone-exe** с суффиксом `-legacy-net48-windows-x86`
   (без инсталлятора).
5. Приложить все к GitHub-релизу; winget/Store-автоматика берёт только modern-ассеты.

Инсталлятор `installer/FastMediaSorter.iss` относится только к modern (x64)-мейнлайну.
Он уже x64-only (`ArchitecturesAllowed=x64compatible`) и явно исключает `win-x86` из
libvlc-компонента - legacy x86 идёт мимо него, как отдельный standalone-артефакт.

### 8.5 Сайт

Страница загрузок: основная кнопка «Fast Media Sorter for Windows (Windows 10/11,
Server 2016+, полный набор фич)» → modern; вторая ссылка «Лёгкий просмотрщик для
старых и 32-битных Windows (7/8.1, x86, без OCR/перевода/Share)» → legacy
standalone-exe. Обновить `index.html`/`docs/` (помнить про дубль корень+`docs/`).

## 9. План работ по фазам

- **Ф0. Подготовка швов (в legacy, без смены платформы).** Ввести интерфейсы
  `IImageDecoder`/`IVideoSurface`, спрятать за ними текущую реализацию. Legacy
  собирается и работает как прежде. Это бесплатно снижает диф между сборками.
- **Ф1. SDK-style modern-проект.** Создать `FastMediaSorter.Modern.vbproj`
  (net10, линк исходников), добиться компиляции с временными заглушками
  расходящихся подсистем. `packages.config` → `PackageReference` для modern.
- **Ф2. Изображения.** ImageSharp 3.x + Magick.NET fallback за `IImageDecoder`.
  Проверить: анимированный WEBP (в т.ч. на Server 2025), APNG, AVIF/HEIC.
- **Ф3. Видео.** LibVLC-only `IVideoSurface`, выпилить WebBrowser-ветки под `#If`.
- **Ф4. Реестр/DPAPI/ресурсы.** Пакеты `Microsoft.Win32.Registry`,
  `System.Security.Cryptography.ProtectedData`, `System.Drawing.Common`; убрать
  ILMerge/RuntimeBootstrap-зависимость из modern (single-file publish).
- **Ф5. Упаковка.** `dotnet publish` self-contained single-file; параметризовать
  `.iss`; собрать оба setup.exe; проверить апгрейд legacy→modern на месте
  (сохранение настроек из реестра).
- **Ф6. Каналы.** Перевести winget-манифест и Store-MSIX на modern-ассет
  (идентичность не меняется). Обновить сайт (обе загрузки). Обновить CI.
- **Ф7. Документация.** CLAUDE.md (двойная сборка), guides/BUILD_AND_RELEASE.md,
  STORE_PUBLISHING.md, winget-доки.

## 10. Заплатка для legacy на переходный период (опционально)

Пока новая сборка не готова, анимированный WEBP в net48 можно закрыть малой
правкой: завендорить **современный native libwebp** (`libwebp.dll` +
`libwebpdemux.dll`, v1.6.1, из NuGet `Imazen.WebP.NativeRuntime.win-x64` - Imazen,
репутабельный автор ImageResizer/imageflow; официальные Windows-сборки Google
DLL не содержат вовсе, только `.lib`) и декодить через P/Invoke `WebPAnimDecoder`
(демукс-API отдаёт первый кадр и для статических, и для анимированных WEBP в BGRA).
Порядок для `.webp`: **libwebp (native) → ImageSharp 2.x → WIC**. Раскладка native
рядом с exe + `PATH` - тем же паттерном, что `OptionalRuntimeManager` для
tesseract/libvlc; в `installer/FastMediaSorter.iss` добавить `webp\`-папку.

Решение по этой заплатке принимать отдельно: если релиз modern-сборки близко,
можно её пропустить и сразу дать пользователю на Server 2025 modern-сборку.

## 11. Риски

- **VB.NET WinForms на .NET 10**: поддерживается, но Designer/`My`/resx на редких
  контролах может преподнести сюрпризы - проверять по ходу Ф1.
- **Six Labors Split License (ImageSharp 3.x)**: платная для части коммерческих
  сценариев. Нужно подтвердить применимость к нашему кейсу (O-4); если не подходит -
  основным декодером сделать **Magick.NET** (Apache-2.0) или **SkiaSharp** (MIT).
- **Вес загрузки** self-contained (десятки-сотня МБ) - для части пользователей
  минус; смягчается slim/framework-dependent вариантом (O-2).
- **Дрейф двух сборок**: митигируется общими исходниками и швами (раздел 3), но
  требует дисциплины «фикс в общий код, а не в одну ветку».
- **Апгрейд на месте legacy→modern**: обязателен тест, что тот же `AppId` и реестр
  дают бесшовный переход без потери настроек и без дублей в «Установка и удаление».

## 12. Открытые вопросы (нужно решение пользователя)

- **O-1. Целевой framework**: подтвердить **.NET 10 (LTS)** (рекомендуется) против
  .NET 8.
- **O-2. Модель поставки modern**: self-contained single-file (рекомендуется, офлайн,
  тяжелее) против framework-dependent (легче, требует рантайм) - или обе.
- **O-3. Охват форматов**: ограничиться ImageSharp 3.x (WebP/APNG/GIF) или сразу
  тянуть Magick.NET ради HEIC/AVIF/JXL (шире, но тяжелее и +native).
- **O-4. Лицензия ImageSharp 3.x**: проверить Split License; при несовместимости -
  Magick.NET/SkiaSharp основным декодером.
- **O-5. Заплатка для legacy (раздел 10)**: делать сейчас или сразу ждать modern?
- **O-6. Судьба WebView2**: выпиливаем в modern или оставляем для HTML?
- **O-7. Механизм отсечения фич в legacy x86 (раздел 1.1)**: условная компиляция
  (`#If FEATURE_FULL` вокруг OCR/Translate/Share) в том же net48-проекте с отдельной
  build-конфигурацией, либо отдельный урезанный `.vbproj`? Первое проще держать в
  синхроне, второе - чище по зависимостям (не тянуть Tesseract/QRCoder в x86-сборку).
- **O-8. Видео в legacy x86**: оставить LibVLC (`libvlc\win-x86` рядом с exe, ~вес)
  или для «самого лёгкого» варианта ограничиться IE WebBrowser (H.264) без libvlc?
  Влияет на то, будет ли legacy действительно «один exe» или «exe + папка libvlc».
