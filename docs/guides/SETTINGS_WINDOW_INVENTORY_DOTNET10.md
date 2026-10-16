# Инвентаризация окна «Настройки» для современного UI .NET 10

> Modern revision, verified 2026-10-04: the current modern placement and restoration behavior are described
> below. The original dated inventory remains the compatibility reference for keys/defaults and legacy
> behavior; its RU/EN-only and former page-layout statements do not describe the modern viewer today.

## Current modern settings revision

The modern viewer has twelve pages: General, Destination folders, Images, Video, Audio, Files and system,
Resources, OCR, Translation, Storage, Android/SFTP and About. The **Resources** page (ticket 118) hosts the
same [Resource_List_Panel.vb](../../src/Resource_List_Panel.vb) the resource-manager dialog embeds - one
list, add/edit/remove/test/import/export - and records `ResourcesTouched` plus the removed ids so the main
viewer reacts after the window closes. The net48 fallback retains its five Designer pages (no resources).

The placement catalog is `AddSettingsGroup` in
[Table_Form.SettingsGroups.vb](../../src/Table_Form.SettingsGroups.vb), keyed by stable page/group/row ids.
Existing row captions/hints come from `ModernSettingTitle`/`ModernSettingDescription`, editors remain
bound to the original services, and their storage keys/defaults remain unchanged. Moving a row does not
create a second setting or migrate its value. Section headings are replaced by independently collapsible
groups; Basic defaults to expanded, Advanced to collapsed, other defaults are explicit in the catalog.

| Area | Current placement |
| --- | --- |
| Language and theme | General / Basic; all thirteen modern languages by endonym, applied live |
| Chrome and information | General / Appearance; startup preferences in their own group |
| Image orientation, fit and quality | Images / Basic or Advanced; background and slideshow have separate groups |
| Audio/subtitle track languages | Video / Tracks and subtitles |
| Shared startup volume/mute | Audio / Basic; values are still shared with video |
| Image/thumbnail caches | Storage, including OCR policy, archive and remote-resource budgets |
| File, archive and secure preferences | Files, with separate task groups |

The per-user `ModernSettingsContext` registry value is private modern-viewer state, with schema version 1.
It remembers the selected page, each group's expansion, a semantic viewport anchor and safe focus.
Closing/reopening, switching pages and a fresh process restore context. Missing pages fall back to General,
missing groups to declared defaults, and malformed/future context resets only UI state. The x86 fallback
does not consume it; secondary modern viewer instances do not overwrite it. Profile exchange excludes it.
Persistence uses `SettingsFlushScheduler`, including bounded retries and a close/shutdown flush; failure
keeps context pending, logs the fault and offers a retry instruction. Geometry remains fitted to the owner's
monitor. Modern settings navigation keys stay in the settings window; legacy forwarding is preserved.

Keyboard focus on a group's header toggles it with Enter/Space. Collapse preserves values and unfinished
edits, moves focus out of hidden contents, and exposes expanded/collapsed state to accessibility.
Rows form one vertical sequence and hints wrap. The left navigation scrolls and captions wrap. Palette
changes retain the existing dynamic theme mechanism and high-contrast override.

Search settings (`Ctrl+F`) opens a temporary discovery dialog. It searches localized captions, hints and
noneditable option labels; results show page/group breadcrumbs and safe checkbox/number/choice values.
Editable fields, API keys, paths, resources and history values are excluded. Choosing a result reveals the
original row and expands only its owning group. Every opening starts with an empty search; searching does
not filter the settings viewport or change unrelated groups. Expand all / Collapse all affects the current
page; collapsed groups summarize up to two safe values and any input validation error.

Rows stack their editor beneath wrapped text in a narrow window. Group/navigation/action targets have
44 logical-pixel floors; checkboxes have a clickable caption and explanation. Native number/text editors
have enlarged targets, choice lists measure their localized options when opened, and the opacity slider
has an exact numeric editor. Text values for OCR service/model/key commit on blur or closing settings;
nonempty endpoints must be HTTP(S), while malformed edits remain pending through grouping/language refresh.

Cache clearing and shell-association entries open separate owned operation dialogs; existing history,
profile import/export, model downloads and support-package flows keep their explicit operation dialogs,
consent, progress and error handling. About includes the MIT license link. Video has a link to the shared
volume/mute owner in Audio / Basic. Initial window sizing uses General content and monitor constraints;
an explicitly remembered normal size wins. A repeated visible-window entry fits the current owner's monitor.

Actual UI evidence for ticket 120 is generated locally under `bin/Settings120Scenes`, at DeviceDpi 168
(175%), including the search dialog, all thirteen language refreshes and narrow stacked rows. Physical
mixed-DPI transitions, Windows text scaling, high-contrast switching and real screen-reader speech remain
unverified and are recorded as dated APP-SETTINGS acceptance exceptions; accessibility properties alone
are not evidence of spoken output.

Verification: context/group unit tests and full .NET suites; actual window scenes at 175% system DPI in
English, Russian and Arabic, including independent expansion, row ownership, viewport return and
fresh-process page/group restoration. Search, full editor revision, all DPI/touch scenarios and real
screen-reader speech were foundation gaps; search and the original editor revision are now implemented.
Physical DPI/touch/text-scale and screen-reader acceptance remain unverified. The section 13 audit changes are implemented; full APP-SETTINGS conformance still depends on physical driven acceptance.

### Audit completion (2026-10-04)

Ordinary additional-preference edits preserve the current folder list and shuffle progress.
Registry writes use a 1.5-second trailing debounce with the existing bounded failure retries;
closing flushes changes. Volume, mute, normal geometry, no-panels full-screen mode and per-folder
zoom participate in this persistence. The modern repeat checkbox is retired: the end-action choice
and playback menu share the same value; an old loop flag migrates to Repeat once.

Thumbnail size selects the actual 96/128/192/256 px panel preference. Slideshow seconds retain
millisecond precision. Numeric hints show ranges and units. Closed choices, numbers and sliders
route the wheel to page scrolling instead of changing values. Disabled dependent rows have neutral
cursors, muted captions and a visible reason. The API-key editor is unavailable for Ollama.

All cache budgets are on Storage. Decode/thumbnail zero remains off; the OCR editor displays zero
for its existing off switch and -1 for the legacy unlimited budget, preserving old registry/profile
semantics. Archive/remote session budgets retain their nonzero minimum. Thumbnail cache size and
explicit clearing join the existing OCR/decode operations; active archive/remote sessions are not
deleted by settings cleanup.

General offers an explicit additional-preference reset with confirmation and a required backup.
Destination folders, language, theme, sound, OCR and personal histories are retained. Export/import
continue to identify their limited scope. Settings normal position is remembered and clamped to the
owner's current monitor; first opening prefers space beside the viewer when available.
Destination-grid buttons browse, choose resources or clear a selected slot; folder drops assign a
slot, and a visible text line explains its checked state. Key labels follow the current shortcut map.
Resources has a deliberate Open action; navigating its page does not open or probe a resource.

Fresh local verification artifacts: `temp/Settings120Scenes` (isolated profile, source/project,
command logs and EN/RU/DE/Arabic screenshots at 175%). Physical DPI/text-scale/high-contrast/touch
and screen-reader speech remain explicit acceptance exceptions rather than inferred passes.

## Historical compatibility inventory (2026-07-18)

Дата среза: 2026-07-18.  Это контракт сохранения функциональности при
перерисовке `Table_Form`: новый интерфейс может менять компоновку и названия
контролов, но не должен потерять описанное ниже поведение, зависимости и
формат хранения настроек.

## Границы и исходники

| Область | Текущая реализация |
|---|---|
| Окно | `src/Table_Form.vb`, `src/Table_Form.Designer.vb` содержат совместимые legacy-контролы и обработчики. `src/Table_Form.ModernLayout.vb` в .NET 10 переносит их в современную адаптивную оболочку 900×600 (минимум 760×520) с прокруткой; net48 остаётся на Designer-компоновке. |
| OCR-вкладка | `src/Table_Form.Ocr.vb` - строится программно один раз; не является контролами Designer. |
| Share Manager | `src/Table_Form.ShareLauncher.vb` - только в современной .NET 10-сборке. |
| Хранение | VB `GetSetting`/`SaveSetting`: `HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter`. OCR API-ключ шифруется Windows DPAPI. |
| Загрузка/сохранение | `src/Main_Form.Lifecycle.vb`; обычные параметры сохраняются при закрытии приложения, OCR дополнительно сохраняется при закрытии окна настроек. |

## Общие правила поведения

- При открытии окна подписи и варианты выбора обновляются на текущем языке; чистые
  поля получают внешние изменения, а незавершённый ввод числа сохраняется даже после
  потери фокуса. Поздние измерения кэша не обновляют закрытое окно.
- Неудачная запись настроек остаётся ожидающей. Автоматические повторы выполняются
  через 1, 4 и 10 секунд; после исчерпания попыток изменения остаются в памяти до
  следующего редактирования или явного сохранения. Закрытие не ждёт этих задержек.
- Импорт сначала сохраняет текущий профиль с личной историей в защищённой папке
  `AppPaths.LocalAppDataRoot()/settings-backups`, обычно
  `%LOCALAPPDATA%\SZA\FastMediaSorter\settings-backups`. Нет копий рядом с источником
  или в общей TEMP; отсутствие защищённого хранилища отменяет импорт. Ранее созданные
  копии не перезаписываются и не удаляются автоматически. Восстановление выполняется
  обычным импортом выбранной копии; ненужные копии пользователь удаляет вручную.

- Интерфейс двухъязычный: русский и английский. Кнопка языка меняет язык окна и
  главного окна сразу; новый язык переживает перезапуск (`Is_Russian_Language`).
- Почти все изменения применяются немедленно к живому состоянию приложения.
  Обычные параметры записываются в реестр при закрытии приложения.
- Вход на вкладку OCR очищает кэш результатов перевода, повторно читает живые
  настройки и проверяет состояние Ollama/список моделей.
- Закрытие окна применяет обновлённый список папок к плавающей панели получателей
  на главном окне и сохраняет OCR-настройки. Нажатия клавиш передаются главному
  окну, кроме ввода в редактируемой ячейке, `TextBox` или `ComboBox`.
- Подсказки есть у большей части интерактивных элементов. В legacy-окне ими не
  снабжены кнопка ассоциаций изображений, Share Manager и ссылка версии/e-mail;
  в новом UI стоит добавить понятное доступное описание и для них.
- Для .NET Framework/x86 скрыты `Колесо мыши приближает`, `Динамическая
  перспектива`, `Анимированная перспектива` и кнопка Share Manager. Это не
  ограничение новой .NET 10-версии: в ней все эти элементы должны быть доступны.

## 1. Каталоги-получатели

Главный элемент - таблица из ровно 11 строк, без добавления/удаления строк и без
пустой растянутой области. Колонки: `Клавиша` (read-only) и
`Каталог-получатель`.

| Строка/элемент | Поведение | Состояние и хранение |
|---|---|---|
| `DEL` | Называет действие удаления; двойной клик по номеру запускает `Main_Form.DoKey(0)`. Выбор папки по двойному клику недоступен. | Семантически не имеет каталога; обе ячейки строки read-only, чтобы текст действия нельзя было записать в неиспользуемый слот `Hardkeys_to_move_mediafile(0)`. |
| Клавиши `1`-`9` | Двойной клик по номеру запускает соответствующее действие сортировки; двойной клик по пути открывает выбор папки. Путь также можно отредактировать прямо в таблице. В .NET 10 двойной клик = **перенос**, `Shift` + двойной клик = **копирование** (`Main_Form.DoKeyAsCopy`). | `Hardkeys_to_move_mediafile(1..9)`; `MoveOn1`-`MoveOn9`. |
| Клавиша `0` | Работает как слот массива `10`, а не `0`; двойной клик аналогичен остальным папкам. | `Hardkeys_to_move_mediafile(10)`; **`MoveOn0`**. Это соответствие нельзя сломать при миграции. |
| Режим копирования | **Только net48:** вместо переноса копирует текущий файл; используется действиями с горячими клавишами, меню и панелью получателей. В .NET 10 галочка скрыта (`Visible = False`), а тип операции выбирается в момент действия - см. `SPECIFICATION_COPY_ACTIONS_REWORK.md`; ключ продолжает читаться и сохраняться ради net48 из общего улья. | `Is_Copying_not_Moving`; `CopyMode`, default `false`. |
| Переходить к следующему файлу после копирования | **Только .NET 10:** занимает место скрытой галочки режима копирования. Включено - после копирования показывается следующий файл (историческое поведение); выключено - остаётся текущий. На перенос не влияет. | `ModernViewerPreferences.AdvanceAfterCopy`; `AdvanceAfterCopy`, default `true`. |
| Фоновая файловая операция | **Только net48:** определяет, выполнять ли файловые операции в фоне. В .NET 10 очередь файловых операций всегда асинхронна - значение чекбокса сохраняется для совместимости, но на поведение не влияет. | `chkbox_Independent_Thread_For_File_Operation.Checked`; `UseIndependentThreadForOperationsWithFiles`, default `false`. Для нового .NET 10 UI удалить либо показать как недоступную историческую настройку, но не создавать ложный переключатель. |
| Без подтверждения | Пропускает запрос пользователя перед файловой операцией. | `Is_no_request_before_file_operation`; `NoRequestBeforeFileOperation`, default `false`. |

## 2. Просмотр

### Фон

| Элемент | Значения/зависимости | Применение и хранение |
|---|---|---|
| Цвет фона | Выпадающий список: «По углу» (0), чёрный (1), белый (2), «По краю» (3), «По верху» (4), «По низу» (5). | `Form_Color_Scheme`; `color_scheme`. Меняет фон/оформление просмотрщика. |
| Перспектива | Включает фон из продолжения изображения по бокам/полям. | `Is_Pespective`; `isPerspective`, default `true`. |
| Динамическая перспектива | Дополнительный эффект: полосы плавно уходят в цвет фона, образуя ореол. Доступна только когда включена «Перспектива». | `Is_Dynamic_Perspective`; `DynamicPerspective`, default `false`. |
| Анимированная перспектива | Рост ореола от изображения к краю экрана на новом фото; доступна только когда включены обе родительские опции. Не должна запускаться от resize/zoom. | `Is_Animated_Perspective`; `AnimatedPerspective`, default `true`. |
| Скорость анимации ореола | Выпадающий список: «Медленная» (350 ms - историческая), «Средняя» (175 ms, по умолчанию), «Быстрая» (87.5 ms). Доступен только когда включены «Перспектива», «Динамическая перспектива» и «Анимированная»; при отключении сереет, но значение не сбрасывается. Применяется со следующего запуска анимации, идущий ореол не перезапускается. | `Halo_Animation_Speed` (`HaloAnimationSpeed`); `AnimatedPerspectiveSpeed` = `slow`/`medium`/`fast`, отсутствующее или неизвестное значение = `medium`. |

### Экранная информация и слайдшоу

| Элемент | Поведение | Состояние и хранение |
|---|---|---|
| Показывать размеры изображения | Включает ширину × высоту в информации о файле. | `Main_Form.Is_to_show_picture_sizes`; `ShowPictureSizes`, default `true`. |
| Показывать дату/время файла | Показывает дату последнего изменения. | `Main_Form.Is_to_show_file_datetime`; `ShowFileDates`, default `true`. |
| Показывать размер файла | Показывает размер файла. | `Main_Form.Is_to_show_file_sizes`; `ShowFileSizes`, default `true`. |
| Имя и позиция поверх изображения | Накладывает имя файла и `N/всего`; изменение сразу перерисовывает медиа. | `Is_Show_Info_Overlay`; `ShowInfoOverlay`, default `false`. |
| Колесо мыши приближает | Включено: колесо масштабирует под курсором. Выключено: листает файлы. `Ctrl`+колесо масштабирует всегда, `Shift` даёт 100 %, `Alt` вписывает; над видео колесо всегда листает. | `Zoom_Wheel_Zooms`; `WheelZooms`, default `false`. |
| Интервал слайдшоу | `NumericUpDown`, 1..120 секунд, default 10. Повторный запуск слайдшоу сокращает текущую задержку вдвое до внутреннего лимита. | `Slideshow_Base_Interval_Ms`; `SlideshowIntervalSec`. |
| Размер карточки панели миниатюр | Выпадающий список: `30x40`, `50x50`, `40x90`, `90x40`, `80x80`, `100x100`, `90x160`, `160x90`, `200x200`, `340x200`; при неизвестном сохранённом размере сброс на `80x80`. | `Picture_Box_Width_At_Panel` + `Picture_Box_Height_At_Panel`; одноимённые ключи. |

## 3. Видео и качество

| Элемент | Поведение | Состояние и хранение |
|---|---|---|
| Авто-поворот EXIF | Поворачивает фото по EXIF Orientation при загрузке. | `Is_Exif_AutoRotate`; `ExifAutoRotate`, default `true`. |
| Качественное масштабирование | Бикубическое уменьшение изображений; изменение вызывает немедленную перерисовку. | `Is_HighQuality_Scaling`; `HighQualityScaling`, default `true`. |
| Зациклить видео | Повторяет воспроизведение видео. | `Is_Video_Loop`; `IsVideoLoop`, default `false`. |
| Без звука по умолчанию | Изменяет текущее и начальное аудиосостояние видео вместе с громкостью. | `Main_Form.SetVideoAudioState`; `VideoMuted`, default `false`. |
| Громкость по умолчанию | Число 0..100 %, default 100; при любом изменении вызывается `SetVideoAudioState(volume, muted)`. | Частное поле `Main_Form`; `VideoVolume` как инвариантное число `0.00`-`1.00`. |

## 4. Файлы и система

| Элемент | Поведение | Состояние и хранение |
|---|---|---|
| Плавающая таблица получателей поверх медиа | Показывает в левом верхнем углу изображения/видео список каталогов и Delete. Нажатие строки делает то же, что соответствующая горячая клавиша: копирует/переносит либо удаляет текущий файл. Сразу перестраивает панель. | `Is_Show_Recipients_Overlay`; `ShowRecipientsOverlay`, default `false`. Текущее имя `SetOnTop` - устаревшее и **не** означает always-on-top. |
| Разрешать запуск в новых окнах | При включении каждый следующий запуск .NET 10 viewer создаёт отдельный процесс/окно; дополнительное окно не сохраняет общий профиль. Значение применяется сразу к следующим запускам. | `ModernViewerPreferences.AllowNewWindows`; `AllowNewWindows`, default `false`. Только .NET 10; x86/net48 всегда сохраняет прежнее single-instance поведение. |
| Сделать программой просмотра изображений | Записывает per-user ProgID в `HKCU\Software\Classes` и уведомляет Shell. В .NET 10 заявляет `.jpg`, `.jpeg`, `.gif`, `.png`, `.bmp`, `.tiff`, `.ico`, `.wmf`, `.emf`, `.exif`, `.webp`, `.avif`, `.heic`, `.heif`; показывает результат/ошибки. | `Main_Form.AssociateAllImageFormatsWithThisApp()`; внешнее изменение ассоциаций Windows. |
| Сделать видеоплеером по умолчанию | Аналогично регистрирует все форматы набора `video_File_Extensions`: `.webm`, `.ogg`, `.3g2`, `.mkv`, `.3gp`, `.mp4`, `.m4v`, `.m4a`, `.mov`, `.mp3`, `.avi`, `.wmv`, `.asf`, `.mpg`, `.mpeg`, `.flv`, `.wav`, `.wma`; показывает результат/ошибки. | `Main_Form.AssociateAllVideoFormatsWithThisApp()`; внешнее изменение ассоциаций Windows. |
| Управление SFTP-сервером для Android-клиента | Открывает отдельное приложение `FastMediaSorterCompanion.exe` в режиме управления (не мастер «поделиться этой папкой»). При отсутствии companion выводится понятная ошибка переустановки, при ошибке запуска - предупреждение. | Только .NET 10 x64; `Main_Form.OpenShareManagerWindow()`. Не переносить в окно сам сервер/его настройки. |
| Язык интерфейса | **Первая страница** («Каталоги-получатели», `APP-SETTINGS` правило 5): выпадающий список всех языков сборки по **эндонимам** (13 на .NET 10, без флагов - `ICON-EXTERNAL` правило 6); выбор тут же локализует оба окна, списки и подсказки. При первом запуске default берётся из языка Windows, затем используется сохранённый выбор. На x86-сборке прежняя кнопка RU/EN на «Файлах и системе». | `UiLanguage` (+ зеркало `Is_Russian_Language`). |
| Версия и e-mail | Ссылка вида `<версия> sza@ukr.net`, открывает почтовый клиент с адресом `sza@ukr.net` и темой Fast Media Sorter & Sharing for Windows. | Не настройка; действие `mailto:`. |

## 5. OCR и перевод

Это единственная вкладка с вложенной навигацией: общие настройки сверху,
внутренние страницы «Перевод» и «Распознавание (OCR)», общий footer снизу.
Языковые выпадающие списки рисуют флаг и локализованное имя языка. Все поля
редактируют живой `OcrTranslateSettings`; загрузочный guard не позволяет
событиям записи срабатывать во время наполнения формы.

| Область | Элемент и поведение | Поле/ключ |
|---|---|---|
| Общие | Включить OCR и перевод. | `Enabled`; `OcrEnabled`, default `false`. |
| Общие | Автоматически распознавать и переводить после показа каждого изображения. | `AutoMode`; `OcrAutoMode`, default `false`. |
| Перевод | Провайдер: `ollama` или `libretranslate`. Для LibreTranslate отключаются элементы управления Ollama. | `Provider`; `TranslateProvider`, default `ollama`. |
| Перевод | Endpoint URL сервера перевода; пустое значение означает URL по умолчанию провайдера. | `Endpoint`; `TranslateEndpoint`. |
| Перевод | Установить Ollama: подтверждение, асинхронное скачивание и запуск установщика; при ошибке открывает сайт Ollama. Если главное окно закреплено поверх остальных, временно снимает этот флаг. | Внешнее действие, статусная строка. |
| Перевод | Запустить Ollama: запускает сервер и до 10 секунд проверяет доступность. | Внешнее действие, статусная строка. |
| Перевод | Модель Ollama: редактируемый список. Сначала установленные модели, затем рекомендации (`qwen2.5:3b`, `gemma2:2b`, `qwen2.5:1.5b`, `llama3.2:3b` и др.). | `OllamaModel`; `OllamaModel`, default `qwen2.5:3b`. |
| Перевод | Загрузить выбранную модель в Ollama с прогрессом. | Внешнее действие, статусная строка. |
| Перевод | API-ключ: поле парольного типа. | `ApiKey`; `TranslateApiKey`, DPAPI-зашифрован. |
| Перевод | Язык перевода (target), из языкового каталога. | `TargetLang`; `TranslateTargetLang`, default `en`. |
| OCR | Язык распознавания: auto или конкретный язык из каталога. Auto пытается `rus`, `ukr`, `eng` по очереди; выбранный язык дополняется подходящими fallback-языками. | `SourceLang`; `OcrSourceLang`, default `auto`. |
| OCR | Качество модели: `fast` или `best` (точнее, но медленнее). Перемена инвалидирует результаты. | `OcrModelQuality`; `OcrModelQuality`, default `fast`. |
| OCR | Режим сегментации: `auto`, `block`, `sparse`, `line`, `vertical`. Перемена инвалидирует результаты. | `OcrPageMode`; `OcrPageMode`, default `auto`. |
| OCR | Скачать языковой пакет: при необходимости интерактивно устанавливает runtime OCR, затем скачивает нужные модели Tesseract для выбранного источника и качества. | Внешнее действие, статусная строка. |
| Footer | Непрозрачность оверлея: в UI показывается понятный диапазон **30..100 %**. На границе UI значение переводится в alpha 0..255, поэтому существующий профиль читается без миграции; изменение применяется без очистки кэша. | `OverlayOpacity`; `OverlayOpacity`, default `210` (примерно 82 %). |
| Footer | Кэшировать результаты на диск. | `DiskCache`; `OcrDiskCache`, default `true`. |
| Footer | Статус: установка/запуск Ollama, загрузка моделей и пакетов, ошибки. Это отображение, не настройка. | Не сохраняется. |

### Сохраняемые OCR-поля, не представленные на вкладке

| Поле/ключ | Фактическое поведение | Решение для нового UI |
|---|---|---|
| `Engine` / `OcrEngine` | Загружается и сохраняется, но текущая реализация всегда создаёт `TesseractOcrEngine`; форма не даёт выбора движка. | Не выводить как ложный выбор. Ключ можно читать для совместимости, а удалить/мигрировать только отдельным решением. |
| `OverlayVisible` / `OverlayVisible` | Сохраняет видимость уже полученного OCR-оверлея. | Больше не скрыто: в современном UI есть переключатель «Показывать панель перевода», который немедленно показывает или скрывает текущий оверлей. |

## 6. О программе (только .NET 10)

| Элемент | Поведение | Состояние и хранение |
|---|---|---|
| Отправить логи автору | Кнопка «Собрать и отправить» собирает `current.log`, `companion.log` (если есть) и сгенерированный `report.txt` в ZIP в `%TEMP%`, показывает диалог согласия со списком содержимого и предупреждением о путях/именах файлов, затем передаёт архив почтовому клиенту через Simple MAPI. Без MAPI-клиента - архив в буфер обмена, папка открыта, `mailto:` без вложения. «Отмена» удаляет архив. | Не настройка; ничего не сохраняется. Сеть не используется: письмо отправляет сам пользователь. Секреты (`*ApiKey`, `*Password`, `*Token`) в отчёт не попадают, лог обрезается до последних 2 МБ. См. `SPECIFICATION_SEND_LOGS_TO_AUTHOR.md` (local specification). |
| Сбросить настройки (`profile_reset_all`) | Кнопка «Сбросить настройки» (`btn_Reset_All_Settings`, ширина 230 px) сбрасывает все настройки просмотрщика (получатели, вид, видео, OCR, положение окон) к значениям по умолчанию со следующего запуска; сохраняет резервную копию `.reg` в `%APPDATA%\FastMediaSorter\backups\`; сохраняет язык, тему, пароли, список ресурсов и историю; немедленно закрывает программу после подтверждения; блокируется во вторичных окнах. См. `169_SPECIFICATION_SETTINGS_RESET_ALL_ON_ABOUT_PAGE.md`. | Действие; выставляет маркер отложенного сброса `PendingProfileReset`. Настройки очищаются при следующем первичном запуске. |
| Ссылки проекта | Сайт, GitHub, релизы, политика конфиденциальности, письмо автору - синие кликабельные названия без отдельных кнопок «Открыть». | Адрес автора - одна константа `Common_Module.Author_Email`. |

## Обязательные динамические состояния для нового UI

1. `Анимированная перспектива` disabled, если выключена динамическая; динамическая
   и анимированная disabled, если выключена перспектива.
2. Элементы Ollama (модель, Pull, Install, Start) disabled при выбранном
   LibreTranslate и на время длительной OCR/Ollama-операции.
3. При длительных OCR-операциях отображать busy-состояние и не допускать
   параллельных запусков; кнопку загрузки языкового пакета также disable.
4. OCR API key не показывать открытым текстом и не хранить в открытом виде.
5. При выборе вкладки OCR сбрасывать кэш перевода, обновлять поля и асинхронно
   получать список моделей; отсутствие доступа к Ollama не должно очищать
   рекомендуемый список.
6. Форма должна поддерживать DPI: текущая Designer-часть масштабируется шрифтом,
   а OCR-часть вручную переводит координаты из 96 DPI.

## Рекомендуемая декомпозиция при перерисовке

- `SettingsViewModel`/сервис настроек - вместо прямой записи из контролов в
  глобальные переменные, но с сохранением существующих ключей и defaults.
- Семь разделов левой навигации: Получатели, Просмотр, Видео, Файлы, OCR и
  перевод, Android/SFTP, О программе. В OCR визуально разделить перевод,
  распознавание и панель результата; SFTP не смешивать с файловыми настройками.
- Для опасных внешних действий (ассоциации, установка Ollama, скачивание моделей)
  оставить явные кнопки, подтверждение и статус/ошибку рядом с действием.
- Не переносить историческое имя `SetOnTop`: в новом коде назвать настройку
  `ShowRecipientsOverlay`.
- Не переносить в .NET 10 переключатель `UseIndependentThreadForOperationsWithFiles`:
  современная очередь уже всегда работает в фоне. Сам ключ продолжать читать и
  записывать лишь пока нужна бинарная совместимость с x86/net48.

## Результат аудита исходной инвентаризации

В этой ревизии исправлены четыре неточности первоначального описания: отмечено,
что не каждый legacy-контрол имеет tooltip; уточнён реальный статус фоновых
файловых операций в .NET 10; зафиксирована редактируемая, но бессмысленная ячейка
DEL; перечислены точные наборы ассоциаций. Также добавлены два сохранённых OCR
поля, отсутствующие на вкладке.

Дополнительно выявлены две особенности, которые при перерисовке стоит принять
осознанно, а не считать контрактом качества:

- Изменение endpoint/API key/Ollama model записывает живые поля, но не вызывает
  немедленный автозапуск перевода. Автозапуск вызывают enabled, auto, provider,
  source/target language, quality и page mode.
- Ключ кэша OCR включает provider, модель, языки, quality и page mode, но не
  endpoint и не API key. Поэтому смена endpoint/key во время открытого окна может
  повторно использовать прежний результат. Новый UI/сервис разумно должен
  инвалидировать кэш и по этим двум параметрам; это улучшение, а не требование
  сохранять legacy-ошибку.

## Аудит современной компоновки .NET 10

Проверена не только структура контролов, но и фактические снимки всех семи
вкладок при системной тёмной теме и увеличенном масштабе текста. Исправлены
следующие проблемы ранней версии макета:

- англоязычная надпись `SETTINGS` заменена локализуемым названием, пункты боковой
  навигации сокращены так, чтобы не обрезаться при высоком DPI;
- форма уменьшена до компактного рабочего размера, но оставлена изменяемой и с
  прокруткой для длинной OCR-вкладки;
- палитра берётся из светлой/тёмной темы Windows, системного accent color и
  high-contrast цветов; изменение темы применяется к уже открытому окну;
- старые `GroupBox`, в том числе группы с одним элементом, не участвуют в
  современной разметке;
- каждый параметр получил заголовок и пояснение; элементы управления стоят в
  единой правой колонке, а длинные пояснения переносятся на две строки;
- логические переключатели используют отдельный шаблон: checkbox слева, сразу за
  ним название, ниже под названием - пояснение; клик по названию или пояснению
  переключает checkbox так же, как клик по самому квадрату;
- переключатель таблицы получателей вынесен в собственную строку над таблицей и
  больше не пересекается с ней; строка `DEL` полностью запрещена для редактирования;
- SFTP и Android вынесены в отдельную вкладку с запуском менеджера и ссылками на
  инструкцию/мобильное приложение;
- добавлена вкладка «О программе» с версией, описанием и ссылками проекта;
- значение непрозрачности показывается в процентах 30..100, сохраняя прежний
  alpha-формат настройки для обратной совместимости;
- добавлен переключатель видимости уже распознанного OCR-оверлея; смена URL
  сервиса или API-ключа очищает результаты, чтобы не использовать ответ
  предыдущего сервиса;
- на широком окне независимые настройки размещаются в двух колонках; компактная
  шапка не содержит пустого пространства, а нижняя строка «изменения
  сохраняются автоматически» удалена как очевидная. Короткая версия осталась
  ссылкой в левом нижнем углу боковой панели;
- ссылки на вкладке «О программе» сами являются синими кликабельными названиями:
  отдельных кнопок «Открыть» нет.

## Контрольный список приёмки

- [ ] Значения всех перечисленных ключей из существующего профиля считываются без
  миграции и имеют те же defaults.
- [ ] `0` хранится в `MoveOn0`, а не `MoveOn10`; DEL остаётся только действием удаления.
- [ ] Строка DEL не редактируется в новом UI; устаревший переключатель фоновых
  файловых операций не создаёт ложного обещания в .NET 10.
- [ ] Изменение HQ scaling и info overlay немедленно перерисовывает медиа.
- [ ] Изменение громкости и mute вызывает единый метод установки аудиосостояния.
- [ ] Overlay получателей перестраивается при изменении списка и при закрытии настроек.
- [ ] OCR API key остаётся DPAPI-защищённым; OCR сохраняется при закрытии настроек.
- [ ] Все зависимости disabled/busy и RU/EN локализация из раздела выше проверены.

<!-- settings-catalog:start -->
## Current catalog generated from production declarations

Regenerate this region with `tools/Export-SettingsInventory.ps1`. It contains no profile values.
Every id uses `ModernSettingTitle(id)` and `ModernSettingDescription(id)` for its localized caption/hint;
the matching constructor in `Table_Form.ModernLayout.vb` or `Table_Form.ExpandedSettings.vb` owns its editor,
numeric range/step, units and stable choice tokens. `Table_Form.Ocr.vb` owns OCR dependencies and busy states;
`SyncDynamicPerspectiveEnabled` owns halo dependencies. Those authorities are reused by the actual controls
and search, so translation, defaults, ranges and persistence are never copied into another profile model.

Modern preference booleans/choices/numbers apply live through their existing handlers. Interface scale
requires reopening settings; startup options affect subsequent starts; initial image/video/track choices
apply when opening media; disk budgets affect subsequent cache use. OCR text edits commit on blur/close,
validating nonempty HTTP(S) endpoints before applying. Invalid pending input survives regrouping and language
refresh. Empty endpoint/model retains the existing automatic behavior. Secrets remain masked and DPAPI-protected.
Actions are entry points, not preferences: no stored default/range, and opening a dialog does not execute them.

| Stable id | Page / group | Persistence / default authority | Values / validation authority |
| --- | --- | --- | --- |
| `interface_language` | general / basic | Localization / UiLanguage; first-run Windows language; live | Matching row constructor |
| `ui_theme` | general / basic | ThemeService / UiTheme; system; live across applications | Matching row constructor |
| `interface_scale` | general / interface | ModernViewerPreferences.InterfaceScaleRelativePercent; Integer; default type default; Load/Save key InterfaceScaleRelativePercent | Bound property type; matching row constructor / Normalize |
| `reduce_motion` | general / interface | ModernViewerPreferences.ReduceMotion; Boolean; default False; Load/Save key ReduceMotion | Bound property type; matching row constructor / Normalize |
| `chrome_pinned` | general / interface | ModernViewerPreferences.ChromePinned; Boolean; default False; Load/Save key ChromePinned | Bound property type; matching row constructor / Normalize |
| `chrome_size` | general / interface | ModernViewerPreferences.ChromeSize; String; default "medium"; Load/Save key ChromeSize | tokens: small, medium, large, adaptive, touch |
| `chrome_opacity` | general / interface | ModernViewerPreferences.ChromeOpacityPercent; Integer; default 86; Load/Save key ChromeOpacityPercent | range: 0, 100 |
| `chrome_corners` | general / interface | ModernViewerPreferences.ChromeCornerRadius; Integer; default 6; Load/Save key ChromeCornerRadius | range: 0, 16 |
| `show_info_overlay` | general / interface | Main_Form.Lifecycle; key ShowInfoOverlay; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `video_hide_cursor` | general / interface | ModernViewerPreferences.HideCursorAfterSec; Integer; default 3; Load/Save key HideCursorAfterSec | range: 0, 30 |
| `startup_open` | general / startup | ModernViewerPreferences.StartupOpenMode; String; default "home"; Load/Save key StartupOpenMode | tokens: home, lastFolder, lastFile |
| `resume_last_playback` | general / startup | ModernViewerPreferences.ResumeLastPlayback; Boolean; default False; Load/Save key ResumeLastPlayback | Bound property type; matching row constructor / Normalize |
| `allow_new_windows` | general / startup | ModernViewerPreferences.AllowNewWindows; Boolean; default False; Load/Save key AllowNewWindows | Bound property type; matching row constructor / Normalize |
| `secondary_instance_warning` | general / startup | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `welcome_replay` | general / startup | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `settings_export_personal` | general / advanced | Per-export choice; False; not persisted | Matching row constructor |
| `settings_export` | general / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `settings_import` | general / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `settings_defaults` | general / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `recipients_overlay` | recipients / basic | Main_Form.Lifecycle; key ShowRecipientsOverlay; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `recipients_position` | recipients / overlay | ModernViewerPreferences.RecipientsOverlayPosition; String; default "topLeft"; Load/Save key RecipientsOverlayPosition | tokens: topLeft, topRight, bottomLeft, bottomRight |
| `recipients_width` | recipients / overlay | ModernViewerPreferences.RecipientsOverlayWidth; Integer; default 280; Load/Save key RecipientsOverlayWidth | range: 180, 520 |
| `recipients_font` | recipients / overlay | ModernViewerPreferences.RecipientsOverlayFontSize; Integer; default 9; Load/Save key RecipientsOverlayFontSize | range: 9, 18 |
| `recipients_opacity` | recipients / overlay | ModernViewerPreferences.RecipientsOverlayOpacity; Integer; default 88; Load/Save key RecipientsOverlayOpacity | range: 40, 100 |
| `recipients_rows` | recipients / overlay | ModernViewerPreferences.RecipientsOverlayVisibleRows; Integer; default 11   ' ten slots + Delete fit without scrolling (audit B-13); Load/Save key RecipientsOverlayVisibleRows | range: 3, 11 |
| `recipients_position_reset` | recipients / overlay | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `new_image_scale` | images / basic | ModernViewerPreferences.NewImageScaleMode; String; default "fit"; Load/Save key NewImageScaleMode | tokens: fit, actual, perFolder |
| `exif_rotate` | images / basic | Main_Form.Lifecycle; key ExifAutoRotate; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `wheel_zooms` | images / basic | Main_Form.Lifecycle; key WheelZooms; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `show_picture_size` | images / basic | Main_Form.Lifecycle; key ShowPictureSizes; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `show_file_size` | images / basic | Main_Form.Lifecycle; key ShowFileSizes; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `show_file_date` | images / basic | Main_Form.Lifecycle; key ShowFileDates; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `background_color` | images / background | Main_Form.Lifecycle; key color_scheme; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `perspective` | images / background | Main_Form.Lifecycle; key isPerspective; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `dynamic_perspective` | images / background | Main_Form.Lifecycle; key DynamicPerspective; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `animated_perspective` | images / background | Main_Form.Lifecycle; key AnimatedPerspective; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `halo_speed` | images / background | Main_Form.Lifecycle; key AnimatedPerspectiveSpeed; storage default '' | Original native editor bounds/choices; Lifecycle loader validation |
| `slideshow_interval` | images / slideshow | Main_Form.Lifecycle; key SlideshowIntervalSec; storage default '10' | Original native editor bounds/choices; Lifecycle loader validation |
| `slideshow_random_order` | images / slideshow | ModernViewerPreferences.SlideshowRandomOrder; String; default "natural"; Load/Save key SlideshowRandomOrder | tokens: natural, random, shuffleCycle |
| `stop_slideshow_manual` | images / slideshow | ModernViewerPreferences.StopSlideshowOnManualNavigation; Boolean; default True; Load/Save key StopSlideshowOnManualNavigation | Bound property type; matching row constructor / Normalize |
| `slideshow_ui` | images / slideshow | ModernViewerPreferences.SlideshowUiMode; String; default "none"; Load/Save key SlideshowUiMode | tokens: none, toolbar, toolbarAndStatus |
| `hq_scaling` | images / advanced | Main_Form.Lifecycle; key HighQualityScaling; storage default '1' | Original native editor bounds/choices; Lifecycle loader validation |
| `thumbnail_size` | images / advanced | Main_Form.Lifecycle; key Picture_Box_Width_At_Panel; storage default '80'; paired Picture_Box_Height_At_Panel | Original native editor bounds/choices; Lifecycle loader validation |
| `include_video_navigation` | video / basic | ModernViewerPreferences.IncludeVideoInNavigation; Boolean; default True; Load/Save key IncludeVideoInNavigation | Bound property type; matching row constructor / Normalize |
| `video_autoplay` | video / basic | ModernViewerPreferences.VideoAutoplay; Boolean; default True; Load/Save key VideoAutoplay | Bound property type; matching row constructor / Normalize |
| `video_end_action` | video / basic | ModernViewerPreferences.VideoEndAction; String; default "stay"; Load/Save key VideoEndAction | tokens: stay, nextFile, repeat |
| `video_remember_position` | video / basic | ModernViewerPreferences.RememberVideoPosition; Boolean; default True; Load/Save key RememberVideoPosition | Bound property type; matching row constructor / Normalize |
| `shared_audio_settings` | video / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `preferred_audio_language` | video / tracks | ModernViewerPreferences.PreferredAudioLanguage; String; default ""; Load/Save key PreferredAudioLanguage | Bound property type; matching row constructor / Normalize |
| `preferred_subtitle_language` | video / tracks | ModernViewerPreferences.PreferredSubtitleLanguage; String; default ""; Load/Save key PreferredSubtitleLanguage | Bound property type; matching row constructor / Normalize |
| `video_click_action` | video / controls | ModernViewerPreferences.VideoSingleClickAction; String; default "pauseResume"; Load/Save key VideoSingleClickAction | tokens: pauseResume, nextFile |
| `video_controls_delay` | video / controls | ModernViewerPreferences.VideoControlsHideDelaySec; Integer; default 3; Load/Save key VideoControlsHideDelaySec | range: 1, 15 |
| `video_controls_paused` | video / controls | ModernViewerPreferences.ShowVideoControlsWhenPaused; Boolean; default True; Load/Save key ShowVideoControlsWhenPaused | Bound property type; matching row constructor / Normalize |
| `video_seek_short` | video / controls | ModernViewerPreferences.SeekShortSec; Integer; default 3; Load/Save key SeekShortSec | range: 1, 600 |
| `video_seek_medium` | video / controls | ModernViewerPreferences.SeekMediumSec; Integer; default 10; Load/Save key SeekMediumSec | range: 1, 600 |
| `video_seek_long` | video / controls | ModernViewerPreferences.SeekLongSec; Integer; default 60; Load/Save key SeekLongSec | range: 1, 600 |
| `include_audio_navigation` | audio / basic | ModernViewerPreferences.IncludeAudioInNavigation; Boolean; default True; Load/Save key IncludeAudioInNavigation | Bound property type; matching row constructor / Normalize |
| `audio_end_action` | audio / basic | ModernViewerPreferences.AudioEndAction; String; default "next"; Load/Save key AudioEndAction | tokens: next, stay, repeat |
| `video_volume` | audio / basic | Main_Form.Lifecycle; key VideoVolume; storage default '1.0'; UI percent 0-100, shared with audio | Original native editor bounds/choices; Lifecycle loader validation |
| `video_mute` | audio / basic | Main_Form.Lifecycle; key VideoMuted; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `audio_controls_visible` | audio / display | ModernViewerPreferences.AudioControlsAlwaysVisible; Boolean; default True; Load/Save key AudioControlsAlwaysVisible | Bound property type; matching row constructor / Normalize |
| `audio_visualiser` | audio / display | ModernViewerPreferences.AudioVisualiser; Boolean; default True; Load/Save key AudioVisualiser | Bound property type; matching row constructor / Normalize |
| `audio_sleep_timer` | audio / display | ModernViewerPreferences.SleepTimerMinutes; Integer; default 0; Load/Save key SleepTimerMinutes | range: 0, 180 |
| `advance_after_copy` | files / basic | ModernViewerPreferences.AdvanceAfterCopy; Boolean; default True; Load/Save key AdvanceAfterCopy | Bound property type; matching row constructor / Normalize |
| `name_collision` | files / basic | ModernViewerPreferences.NameCollisionPolicy; String; default "ask"; Load/Save key NameCollisionPolicy | tokens: ask, skip, rename, replace |
| `after_file_operation` | files / basic | ModernViewerPreferences.AfterFileOperation; String; default "next"; Load/Save key AfterFileOperation | tokens: next, stay, closeIfEmpty |
| `reveal_status_on_file_op` | files / basic | ModernViewerPreferences.RevealStatusOnFileOperation; Boolean; default False; Load/Save key RevealStatusOnFileOperation | Bound property type; matching row constructor / Normalize |
| `create_missing_destination` | files / basic | ModernViewerPreferences.CreateMissingDestination; Boolean; default True; Load/Save key CreateMissingDestination | Bound property type; matching row constructor / Normalize |
| `delete_to_recycle_bin` | files / basic | ModernViewerPreferences.DeleteToRecycleBin; Boolean; default True; Load/Save key DeleteToRecycleBin | Bound property type; matching row constructor / Normalize |
| `confirm_delete` | files / basic | ModernViewerPreferences.ConfirmDelete; String; default "always"; Load/Save key ConfirmDelete | tokens: always, permanentOnly, never |
| `no_confirmation` | files / basic | Main_Form.Lifecycle; key NoRequestBeforeFileOperation; storage default '0' | Original native editor bounds/choices; Lifecycle loader validation |
| `include_subfolders` | files / content | ModernViewerPreferences.IncludeSubfolders; Boolean; default False; Load/Save key IncludeSubfolders | Bound property type; matching row constructor / Normalize |
| `included_extensions` | files / content | ModernViewerPreferences.IncludedExtensions; String; default ""; Load/Save key IncludedExtensions | Bound property type; matching row constructor / Normalize |
| `include_document_navigation` | files / content | ModernViewerPreferences.IncludeDocumentInNavigation; Boolean; default False; Load/Save key IncludeDocumentInNavigation | Bound property type; matching row constructor / Normalize |
| `document_extensions` | files / content | ModernViewerPreferences.DocumentExtensions; String; default ".txt;.md;.log;.csv;.json;.xml;.html;.htm;.pdf;.epub;.fb2;.mobi"; Load/Save key DocumentExtensions | Bound property type; matching row constructor / Normalize |
| `text_preview_max_kb` | files / content | ModernViewerPreferences.TextPreviewMaxKb; Integer; default 2048; Load/Save key TextPreviewMaxKb | range: ModernViewerPreferences.TextPreviewMaxKbMin, ModernViewerPreferences.TextPreviewMaxKbMax |
| `sort_other_files` | files / content | ModernViewerPreferences.SortOtherFiles; Boolean; default False; Load/Save key SortOtherFiles | Bound property type; matching row constructor / Normalize |
| `other_extensions` | files / content | ModernViewerPreferences.OtherExtensions; String; default ""; Load/Save key OtherExtensions | Bound property type; matching row constructor / Normalize |
| `recent_files_limit` | files / history | ModernViewerPreferences.RecentFilesLimit; Integer; default 50; Load/Save key RecentFilesLimit | range: 0, 200 |
| `recent_folders_limit` | files / history | ModernViewerPreferences.RecentFoldersLimit; Integer; default 100; Load/Save key RecentFoldersLimit | range: 0, 200 |
| `recent_history` | files / history | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `archive_entry_limit` | files / archives | ModernViewerPreferences.ArchiveMaxEntryMb; Integer; default 512; Load/Save key ArchiveMaxEntryMb | range: ModernViewerPreferences.ArchiveMaxEntryMbMin, ModernViewerPreferences.ArchiveMaxEntryMbMax |
| `archive_entries_limit` | files / archives | ModernViewerPreferences.ArchiveMaxEntries; Integer; default 20000; Load/Save key ArchiveMaxEntries | range: ModernViewerPreferences.ArchiveMaxEntriesMin, ModernViewerPreferences.ArchiveMaxEntriesMax |
| `secure_in_navigation` | files / secure | ModernViewerPreferences.SecureIncludeInNavigation; Boolean; default True; Load/Save key SecureIncludeInNavigation | Bound property type; matching row constructor / Normalize |
| `secure_remember_minutes` | files / secure | ModernViewerPreferences.SecureRememberMinutes; Integer; default 15; Load/Save key SecureRememberMinutes | range: ModernViewerPreferences.SecureRememberMinutesMin, ModernViewerPreferences.SecureRememberMinutesMax |
| `secure_show_true_name` | files / secure | ModernViewerPreferences.SecureShowTrueName; Boolean; default True; Load/Save key SecureShowTrueName | Bound property type; matching row constructor / Normalize |
| `secure_open_with` | files / secure | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `custom_hotkeys` | files / advanced | ModernViewerPreferences.CustomHotkeysJson; empty map; explicit editor commit | Matching row constructor |
| `image_associations` | files / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `video_associations` | files / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `live_open_with` | files / advanced | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `ocr_enabled` | ocr / basic | OcrTranslateSettings.Enabled; False; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_auto` | ocr / basic | OcrTranslateSettings.AutoMode; False; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_source` | ocr / recognition | OcrTranslateSettings.SourceLang; "auto"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_quality` | ocr / recognition | OcrTranslateSettings.OcrModelQuality; "fast"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_mode` | ocr / recognition | OcrTranslateSettings.OcrPageMode; "auto"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_download` | ocr / recognition | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `ocr_provider` | translation / basic | OcrTranslateSettings.Provider; "ollama"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_target` | translation / basic | OcrTranslateSettings.TargetLang; "en"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_endpoint` | translation / provider | OcrTranslateSettings.Endpoint; ""; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_server` | translation / provider | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `ocr_model` | translation / provider | OcrTranslateSettings.OllamaModel; "qwen2.5:3b"; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_api` | translation / provider | OcrTranslateSettings.ApiKey; ""; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_opacity` | translation / overlay | OcrTranslateSettings.OverlayOpacity; DefaultOverlayOpacity; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_overlay_visible` | translation / overlay | OcrTranslateSettings.OverlayVisible; True; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `decode_cache_limit` | storage / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `decode_cache_clear` | storage / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `thumb_cache_limit` | storage / basic | ModernViewerPreferences.PanelThumbCacheMb; Integer; default 256; Load/Save key PanelThumbCacheMb | range: 0, 4096 |
| `thumb_cache_clear` | storage / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `archive_cache_limit` | storage / basic | ModernViewerPreferences.ArchiveCacheMaxMb; Integer; default 2048; Load/Save key ArchiveCacheMaxMb | range: ModernViewerPreferences.ArchiveCacheMaxMbMin, ModernViewerPreferences.ArchiveCacheMaxMbMax |
| `archive_cache_clear` | storage / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `remote_cache_limit` | storage / basic | ModernViewerPreferences.RemoteCacheMaxMb; Integer; default RemoteCacheStore.Default_Max_Mb; Load/Save key RemoteCacheMaxMb | range: ModernViewerPreferences.RemoteCacheMaxMbMin, ModernViewerPreferences.RemoteCacheMaxMbMax |
| `remote_cache_clear` | storage / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `ocr_disk_cache` | storage / ocr | OcrTranslateSettings.DiskCache; True; Load/Save owns key | OcrTranslateSettings.Load; Table_Form.Ocr editor/dependency rules |
| `ocr_cache_limit` | storage / ocr | ModernViewerPreferences.DecodeCacheMaxMb; Integer; default 512; Load/Save key DecodeCacheMaxMb | range: -1, 1024 |
| `ocr_cache_clear` | storage / ocr | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `resources_intro` | resources / basic | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `sftp_intro` | resources / server | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `sftp_manager` | resources / server | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `sftp_guide` | resources / server | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `android_app` | resources / server | Action/information; matching row constructor (no preference default) | Matching row constructor |
| `exchange_portal` | resources / server | Action/information; matching row constructor (no preference default) | Matching row constructor |

Recipients additionally persist the grid slots under `MoveOn1` through `MoveOn9` and `MoveOn0`;
DEL is an action. Resources use the shared resource registry and dedicated editors (ticket 118). About
contains information/links, the MIT license and the existing explicit support-package dialog.
<!-- settings-catalog:end -->
