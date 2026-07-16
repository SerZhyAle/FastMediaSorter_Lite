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
- Стадия: **Ф0 в работе** - швы в общем коде, legacy остаётся зелёным.
- Следующий шаг: см. «Следующий шаг» ниже.

## СЛЕДУЮЩИЙ ШАГ

1. Ф0: добавить `NETFRAMEWORK=True` в DefineConstants обеих конфигураций
   `src/FastMediaSorter.vbproj` (old-style проект НЕ определяет NETFRAMEWORK сам -
   без этого `#If NETFRAMEWORK` в общих файлах в legacy = False = катастрофа).
2. Ф0: создать `src/Imaging/` - `ImageDecoder.vb` (IImageDecoder + провайдер с `#If`),
   `LegacyWicDecoder.vb` (весь файл в `#If NETFRAMEWORK`; код переносится из
   FileManager.LoadBitmapViaWic/ViaImageSharp), `ModernImageDecoder.vb` (весь файл в
   `#If Not NETFRAMEWORK`; ImageSharp 3.x). FileManager/Utils переключить на шов.
3. Ф0: `#If NETFRAMEWORK` вокруг WebBrowser-веток (точки ниже) и RuntimeBootstrap
   AssemblyResolve.
4. Проверить legacy: msbuild Release зелёный (реальный VS MSBuild через vswhere,
   НЕ `dotnet msbuild` - см. память local-build-toolchain). Коммит.
5. Ф1: `src/Modern/FastMediaSorter.Modern.vbproj` - см. заготовку решений ниже.

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

Этап 1 - **Ф0 швы** (в общем коде, legacy зелёный): [ ]
Этап 2 - **Ф1 modern-проект компилируется** (`dotnet build`): [ ]
Этап 3 - **Ф2 изображения** (ImageSharp 3.x, анимированный webp): [ ]
Этап 4 - **Ф3 видео LibVLC-only** (WebBrowser дремлет, диспетчер мимо него): [ ]
Этап 5 - **Ф4 publish** (self-contained single-file, libvlc рядом): [ ]
Этап 6 - **смоук** (запуск exe, лог, картинка/видео вручную владельцем): [ ]
Этап 7 - **Ф5 упаковка** (build.ps1 -Modern, Build-OfflineRelease, .iss): [ ]
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
| `src/FileManager.vb` | Imports WPF + LoadBitmapPortable/ViaWic/ViaImageSharp -> уходят в шов | [ ] |
| `src/Utils.vb` | Imports WPF + ReadBitmapSourceSize -> в шов (TryGetPixelSize) | [ ] |
| `src/Main_Form.MediaLoading.vb:880-883` | диспетчер видео: WB (net48) vs VLC напрямую (modern) | [ ] |
| `src/Main_Form.VideoPlayer.vb` | LoadVideoInWebBrowser + TryOpenVideoWithDefaultPlayer(HTML) под `#If NETFRAMEWORK`; HandleVideoError-фолбэк | [ ] |
| `src/Main_Form.Lifecycle.vb:129-141,177-178` | SetWebBrowserCompatibilityMode + вызов | [ ] |
| `src/Main_Form.DragDrop.vb:48-70` | AllowWebBrowserDrop wiring + Web_Browser_Navigating | [ ] |
| `src/Main_Form.vb` (ctor) | modern: Web_Browser.Visible=False после InitializeComponent | [ ] |
| `src/RuntimeBootstrap.vb` | AssemblyResolve только net48 | [ ] |
| `src/OptionalRuntimeManager.vb` | оставить как есть (страховка slim); проверить компиляцию | [ ] |

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
- (обновлять по ходу..)
