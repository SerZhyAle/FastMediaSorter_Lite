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

## СЛЕДУЮЩИЙ ШАГ

1. Ф5: `build.ps1` - добавить publish modern (параметр/этап), НЕ ломая текущий
   флоу; `tools/Build-OfflineRelease.ps1`/`Build-Installer.ps1` - на следующей
   итерации (staging modern-дерева вместо bin/Release; `Prepare-OcrOfflinePayload.ps1`
   уже умеет тримить x86 и качать tessdata - переиспользовать).
2. ВАЖНО для CI (когда дойдём до Ф6): `.github/workflows/release.yml` собирает
   sln msbuild'ом - modern-проект в sln требует restore (nuget restore на sln
   восстанавливает PackageReference-проекты; проверить на runner'е .NET 10 SDK).
3. Этап 8: зум/панорама (Ф-Z1..Z5 по SPECIFICATION_ZOOM_PAN_CLASSIC_DOTNET10.md).
4. Этап 9: MKV/ISO (Ф-A..Ф-G по SPECIFICATION_MKV_ISO_PLAYBACK_DOTNET10.md).
5. Этап 10: legacy x86 LITE-урезка (FEATURE_FULL, PlatformTarget=x86, standalone).
6. Этап 11: каналы (winget/Store на modern) + вся документация (CLAUDE.md и пр.).

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
