# Спецификация: переименование в "Fast Media Sorter for Windows" (лайт-вариант)

Статус: **выполнено** (2026-07-14, лайт-вариант ревизии 4)
Дата: 2026-07-11, ревизия 2

> Outcome (2026-07-14): фазы 1-2 сделаны. Новое имя стоит в заголовке окна
> (`Main_Form.Designer.vb`), свойствах exe (`AssemblyInfo.vb` Title/Product), в
> README (EN/RU/UK, с оговоркой «formerly/раніше»), на сайте, в CLAUDE.md и
> релизных текстах. Каналы (`installer/`, `winget/`, `msix/`, титул Store) по
> решению ревизии 4 оставлены под именем `FastMediaSorter LITE` намеренно -
> это заморозка-якорь корреляции обновлений, а не недоделка. Раздел 9 (полный
> ребрендинг витрин) остаётся отложенным на будущее.

Ревизия 2 - принято решение: **в winget и Microsoft Store не меняется ничего**.
Единственное исключение - обновление текстов описаний, которое едет попутно со
следующим штатным релизом/сабмишеном. Старые скриншоты остаются. Полный
ребрендинг витрин отложен (конспект - в разделе 9).

**Ревизия 3 (2026-07-11) - уточнение по установщику.** Ревизия 2 замораживала
`installer/FastMediaSorter.iss` целиком «чтобы не рисковать winget». Это было
слишком широко: winget/Store сопоставляют установленное приложение с манифестом
по **ARP DisplayName** («Установка и удаление программ»), а не по текстам мастера.
В Inno это разные вещи и они разделяемы. Поэтому теперь **экраны мастера, ярлыки
и свойства setup.exe показывают новое имя** («Fast Media Sorter for Windows»,
`#define AppName`), а ARP-запись пришпилена к `FastMediaSorter LITE` через
`UninstallDisplayName` (`#define AppNameArp`, значение байт-в-байт совпадает с тем,
что раньше давал `AppVerName`). winget-корреляция и апгрейды не затронуты; манифесты
winget/Store и `AppId`/exe/папка установки/имена ассетов остаются заморожены.
Разделы 2, 3 и 8 ниже читать с этой поправкой (запрет на правку `.iss` снят -
но `UninstallDisplayName`, `AppId`, `DefaultDirName`, `OutputBaseFilename` не трогать).

**Ревизия 4 (2026-07-12) - в каналах меняем только ОПИСАНИЯ, имена заморожены.**
Решение пользователя: имя в Store не менять вообще; в каналах трогать только то,
что не рвёт публикацию обновлений, - то есть **описания** и то, что видит
пользователь в приложении/установщике. Поэтому (важно - это отменяет черновой
вариант «сменить PackageName/DisplayName», который был подготовлен и откачен):
- **winget**: `PackageName` остаётся **`FastMediaSorter LITE`** (совпадает с ARP
  DisplayName → `winget upgrade` работает). Обновлены только `ShortDescription`/
  `Description` - ведут с нового бренда («Fast Media Sorter for Windows (published
  as FastMediaSorter LITE)»). `AppsAndFeaturesEntries` НЕ добавляем (был нужен
  только под смену имени - откачен). `PackageIdentifier`/`Moniker` заморожены.
- **Store/MSIX**: `AppxManifest` `DisplayName` и титул листинга - **заморожены**
  как `FastMediaSorter LITE` (Partner Center не трогаем, имя не резервируем).
  Обновляется только текст **Description** листинга - готовые EN/RU-тексты в
  `docs/guides/STORE_PUBLISHING.md`.
- **GitHub-релиз**: заголовок/тело уже на новом имени (`release.yml`); установщик
  переименован в ревизии 3; имена ассетов `FastMediaSorter-*` заморожены.

Итог: раздел 9 «полного ребрендинга витрин» остаётся отложенным. Пользователь при
следующем релизе прикладывает обновлённые описания (winget Description едет в PR
манифеста; Store Description вставляется в Partner Center).

## 1. Цель и рамки

Сменить публичное имя продукта с "FastMediaSorter LITE" на
**"Fast Media Sorter for Windows"** там, где это бесплатно и не несёт риска
для каналов дистрибуции:

- заголовок окна и строки внутри приложения;
- свойства exe (AssemblyTitle/AssemblyProduct);
- сайт GitHub Pages, README (EN/RU/UK), CHANGELOG, доки;
- титул и текст GitHub-релизов.

Витрины **Store и winget продолжают жить под именем "FastMediaSorter LITE"**:
ни манифесты имён, ни листинг, ни инсталлятор не меняются. Продукт получает
два сосуществующих имени - это осознанный компромисс (раздел 6).

## 2. Ключевое правило варианта

Цепочка, которую нельзя разрывать:
`installer .iss AppName` → ARP DisplayName ("Установка и удаление программ") →
корреляция `winget upgrade`/`winget list` с `PackageName`.

Пока все три звена говорят "FastMediaSorter LITE", winget работает без единой
правки. Поэтому **`installer/FastMediaSorter.iss` не трогаем вообще** - даже
"просто тексты мастера". Любая правка `AppName` возвращает нас к полному
сценарию (резерв имени, `AppsAndFeaturesEntries`, уборка ярлыков).

## 3. Замороженный слой (НЕ МЕНЯТЬ)

Всё из прежней заморозки идентичности плюс всё витринное в каналах:

| Что | Значение | Почему |
|---|---|---|
| `installer/FastMediaSorter.iss` - весь файл | AppName "FastMediaSorter LITE", AppId, тексты мастера, ярлыки | Раздел 2: ARP-цепочка winget; ноль правок = ноль рисков |
| winget-манифесты, кроме описаний | `PackageIdentifier SerZhyAle.FastMediaSorter`, `PackageName FastMediaSorter LITE`, `Moniker`, `Tags`, `InstallerType` | Ничего не меняем - валидация и корреляция как раньше |
| Store: имя и манифест | Листинг-титул "FastMediaSorter LITE", `AppxManifest` `DisplayName`/`VisualElements`, Identity `SZA.FastMediaSorterLITE` | Без резервирования нового имени DisplayName менять нельзя, а резервирование в этом варианте не делаем |
| Скриншоты и соцпревью | `assets/store/screenshot-*.png`, `assets/social-preview-1280x640.png` | Решение пользователя: старые скриншоты не пугают |
| Имя exe | `FastMediaSorter_LITE.exe` (`AssemblyName`) | Ассоциации, ARP, MSIX Executable, поиск процесса при форвардинге |
| Мьютекс | `FastMediaSorterSingleInstanceMutex` | Single-instance между версиями |
| Реестр настроек | `SZA\FastMediaSorter` (`Common_Module.vb:7-8`) | Настройки пользователей |
| Папка данных | `%LOCALAPPDATA%\SZA\FastMediaSorter` | tessdata/кэши |
| ProgID | `FastMediaSorter.*` | Выбор "приложения по умолчанию" |
| Репозиторий, имена ассетов | `SerZhyAle/FastMediaSorter_Lite`, `FastMediaSorter-<ver>-*` | Pages-URL без редиректа; шаблоны CI и winget InstallerUrl |

## 4. Что меняется

### 4.1 Код приложения

| Файл | Сейчас | Станет |
|---|---|---|
| `src/Main_Form.Designer.vb:425` | `Me.Text = "Fast Media Sorter LITE by SZA"` | `"Fast Media Sorter for Windows by SZA"` (D1) |
| `src/My Project/AssemblyInfo.vb:12` | `AssemblyTitle("FastMediaSorter LITE")` | `AssemblyTitle("Fast Media Sorter for Windows")` |
| `src/My Project/AssemblyInfo.vb:15` | `AssemblyProduct("fast image and video sorter (LITE)")` | `AssemblyProduct("Fast Media Sorter for Windows")` |
| UI-строки RU/EN/UK с "FastMediaSorter LITE": `src/Companion/ShareGuide.vb`, `src/Share_Wizard_Form.vb`, `src/Table_Form.Share.vb`, `src/Main_Form.Localization.vb` и др. | старое имя | новое имя (найти grep-ом по `src/`) |
| `src/Main_Form.FileAssociation.vb` - описания ProgID (`"JPEG Image - FastMediaSorter"`) | опционально | `".. - Fast Media Sorter"`; сами ProgID не трогать. Заметка: ассоциации, записанные инсталлятором, сохранят старый текст - допустимо |

### 4.2 CI и скрипты (только тексты, не имена файлов)

| Файл | Правки |
|---|---|
| `.github/workflows/release.yml` | Титул релиза `name:` и тело: "FastMediaSorter LITE" → новое имя. Блоки про winget-команду оставить как есть (id не меняется). Имена stage/ассетов не трогать |
| `tools/Release.ps1` | Заголовок Say и tag message - текст на новое имя |
| `build.ps1`, `tools/Build-OfflineRelease.ps1`, `msix/build-msix.ps1` | правок не требуют |

### 4.3 Сайт (обе копии: корень и docs/ - см. site-structure)

| Файл | Правки |
|---|---|
| `index.html` + `docs/index.html` | `<title>`, `og:site_name`, `og:title`, `twitter:title`, `.brand`, `<h1>`, JS-словарь титулов en/ru/ua, alt-тексты. winget-команда и ссылки на релизы - без изменений |
| `publish-folders-android.html` + `docs/publish-folders-android.html`, `docs/privacy.html`, `assets/help/port-forward.html` | упоминания имени |
| canonical / og:url | НЕ менять |
| Картинки (соцпревью, скриншоты) | НЕ менять (решение пользователя) |

Ручные шаги на GitHub (опционально): About-описание репозитория, титул wiki.

### 4.4 Документация

| Файл | Правки |
|---|---|
| `README.md`, `README_RU.md`, `README_UK.md` | заголовок + текст; один раз упомянуть "ранее FastMediaSorter LITE; в Microsoft Store и winget публикуется под этим именем" |
| `CHANGELOG.md` | запись в `[Unreleased]` о переименовании с оговоркой про витрины |
| `CLAUDE.md` | витринное имя + абзац о правиле раздела 2 (iss/winget/Store заморожены) |
| `docs/guides/*`, активные `docs/specifications/*`, `msix/README.md`, `tools/store/*.md`, `.github/agents/*.md` | упоминания |
| `docs/specifications/done/*` | НЕ трогать (история) |

### 4.5 Единственные разрешённые изменения в каналах - описания

Едут попутно, отдельных действий не порождают:

- **winget** (в PR следующей штатной версии): в locale-файле обновить только
  `ShortDescription`/`Description`. Рекомендуемая формула первой строки:
  "Fast Media Sorter for Windows (published as FastMediaSorter LITE) is ..".
  `PackageName` и всё остальное - без изменений.
- **Microsoft Store** (при следующем плановом MSIX-сабмишене, если/когда он
  будет): обновить текст Description в листинге тем же приёмом. Титул,
  DisplayName манифеста, скриншоты - без изменений. Отдельный сабмишен ради
  этого не делается.

## 5. План выполнения

- **Фаза 1 - одна ветка**: правки 4.1-4.4.
  Контроль полноты - grep `FastMediaSorter LITE|Fast Media Sorter LITE`
  по репо: старое имя обязано ОСТАТЬСЯ в `installer/`, `winget/`, `msix/`,
  `docs/specifications/done/`, истории CHANGELOG и заметках "ранее известен
  как" - и обязано исчезнуть из `src/`, корневых HTML, README, guides.
- **Фаза 2 - локальная сборка** (`.\build.ps1`) + чек-лист раздела 7.
- **Фаза 3 - обычный релиз** (`.\tools\Release.ps1 -Push`, платно, по явной
  команде). Попутно - winget PR новой версии с обновлённым описанием (4.5).

Всё. Фаз 0 (резерв имени), winget-корреляции и Store-сабмишена в этом
варианте нет.

## 6. Принятые компромиссы и риски

Компромисс варианта - два имени сосуществуют. Старое имя останется видимым:

- в Store (титул листинга) и в `winget search`/`winget list`;
- в мастере установки, ярлыках Пуска/рабочего стола, ARP;
- на скриншотах в Store и на сайте.

Новое имя - в заголовке окна, свойствах exe, на сайте, в README и релизах.
Это принятое решение, а не недоделка; фиксируется формулой
"published as FastMediaSorter LITE" в описаниях.

| # | Риск | Снятие |
|---|---|---|
| R1 | Правка случайно заденет `installer/`, `winget/`, `msix/` | Правило раздела 2 в CLAUDE.md; инвертированный grep-контроль из Фазы 1; ревью diff-а: эти три папки должны быть пустыми в diff |
| R2 | Пропущенные упоминания в витринном слое | grep из Фазы 1 |
| R3 | Путаница пользователей "поставил LITE - окно называется иначе" | Формула "published as .." в README/описаниях |

Не риски: апгрейды, настройки, ассоциации, `winget upgrade`, сертификация
Store - ничего из этого не затрагивается вообще.

## 7. Чек-лист приёмки

- [ ] Заголовок окна: "Fast Media Sorter for Windows by SZA".
- [ ] Свойства exe (Details): Product name - новое имя; версия YY.M.D.HHmm
      генерится как раньше.
- [ ] Локальный `Build-OfflineRelease.ps1` собирает setup.exe без правок .iss;
      мастер и ярлыки со старым именем - ожидаемо и корректно.
- [ ] `winget install SerZhyAle.FastMediaSorter` (текущая версия) ставится как
      раньше; после следующего релиза `winget upgrade` видит новую версию.
- [ ] Сайт: `<title>`/og обновлены, URL прежний, winget-команда на месте.
- [ ] grep: старое имя только в `installer/`, `winget/`, `msix/`,
      `docs/specifications/done/`, истории CHANGELOG и заметках "ранее ..".
- [ ] diff Фазы 1 не содержит файлов из `installer/`, `winget/`, `msix/`
      (кроме, при желании, комментариев в `msix/README.md`).

## 8. Вне рамок

- Любые правки `installer/FastMediaSorter.iss`.
- Смена `PackageName`/титула Store, резервирование имён в Partner Center.
- Переименование exe, репозитория, ProgID, путей данных.
- Перезасъёмка скриншотов и соцпревью.
- Правка исторических документов в `docs/specifications/done/`.

## 9. Отложенный полный ребрендинг витрин - конспект (на будущее)

Если когда-нибудь решим переименовать и Store/winget, потребуется ровно это:

1. Partner Center: зарезервировать новое имя ДО правок (риск отказа на
   "for Windows"; fallback - "Fast Media Sorter"), затем `AppxManifest`
   `DisplayName` посимвольно = зарезервированному, новый сабмишен.
2. Inno: `AppName` + тексты мастера RU/EN/UK + `[InstallDelete]` для старых
   ярлыков "FastMediaSorter LITE". `AppId`/`DefaultDirName` не трогать.
3. winget: `PackageName` в locale + обязательно `AppsAndFeaturesEntries` с
   `ProductCode: '{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}_is1'` (= Inno AppId +
   `_is1`), иначе `winget upgrade` теряет корреляцию с новым ARP-именем.
   `PackageIdentifier`/`Moniker` не менять никогда.
4. Перезаснять скриншоты Store/сайта и соцпревью.
5. Приёмка: апгрейд поверх старой установки, `winget upgrade` с машины со
   старой версией, отсутствие старых ярлыков.
