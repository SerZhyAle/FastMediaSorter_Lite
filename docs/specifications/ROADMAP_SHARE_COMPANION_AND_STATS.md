# Роадмап: перенос Android Folder Share в Companion + статистика/трей-хаб

Статус: **ВЫПОЛНЕНО** - все этапы реализованы и отгружены; роадмап закрыт 2026-07-16.
Дата: 2026-07-13 (аудит пересмотрен 2026-07-13 под цель «релиз с готовым SFTP-воркером одновременно с Android-клиентом»; закрыт 2026-07-16)
Источники: [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md),
[SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md),
[SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md),
[SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md)

Структура и порядок этапов - как видит владелец проекта; ниже каждый этап развёрнут в
конкретные задачи из уже написанных спецификаций (ссылки на разделы даны при каждом
пункте).

---

## Итог (2026-07-16): всё выполнено и отгружено

Роадмап закрыт. Перенос Android Folder Share в отдельный Companion, статистика и
трей-хаб реализованы и отгружены серией релизов **26.7.14.1750 -> 26.7.15.2200**;
приёмка manual-пунктов (телефонный смоук, апгрейд, автозапуск) выполнена через эти
релизы. Итоговая архитектура двух процессов зафиксирована в `CLAUDE.md`.

- **Этап 1 (новый Companion + перенос кода)** - выполнено. Проект
  `src/FastMediaSorterCompanion/` (`net10.0-windows`) в `FastMediaSorter.sln`; весь
  Share-функционал (`Core/*`, `Forms/Share_*`, `TrayContext.vb`, `MainWindow.vb`)
  переехал туда (коммит `a4b405f`).
- **Этап 2 (тесты в процессе разработки)** - выполнено; система автотестов трёх
  программ пакета + `tools/Run-AllTests.ps1` (см. `docs/guides/TESTING.md`).
- **Этап 3 (чистка LITE)** - выполнено. В LITE не осталось `src/Companion/*.vb`,
  `Main_Form.ShareTray.vb`, `Table_Form.Share.vb`; остался тонкий
  `Main_Form.ShareLauncher.vb`; инвариант 8 держится (`Companion` в `src/*.vb`
  встречается только в лаунчере).
- **Этап 4 (статистика + трей-хаб)** - выполнено. `Share_Status_Form.vb` (окно
  «Текущее состояние»), счётчики воркера, взаимный запуск из трея (коммит `a4b405f`).
- **Этап 5 (SFTP-тестирование)** - выполнено через релизы; e2e с Android-импортёром
  подтверждён (см. `done/SPECIFICATION_ANDROID_FOLDER_SHARE.md`).
- **Этап 6 (сборки и пакеты)** - выполнено. `build.ps1` публикует Companion рядом с
  LITE; Inno-инсталлятор (`share`-компонент, `stop-companion.ps1`); MSIX со вторым
  `<Application>` + `StartupTask` + firewall-правилом (коммиты `96c2b2b`, `81ac78f`,
  `bd7d79e`).
- **Этап 7 (документация)** - выполнено. `CLAUDE.md` переписан под два процесса;
  `CHANGELOG.md`, `README*`, `STORE_PUBLISHING.md` обновлены. Спецификации-источники
  (`SPECIFICATION_SHARE_COMPANION_APP.md`, `SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md`,
  `SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md`) уже лежат в `done/`.
- **Этапы 9-10 (пре-релиз и релиз)** - выполнено. Отгружено тегами `v26.7.14.1750`,
  `v26.7.14.1801`, `v26.7.15.2200` (winget + Microsoft Store).

> Смежная работа вне рамок этого роадмапа, отгруженная тем же релизом `26.7.15.2200`
> и перенесённая в `done/` тем же заходом: `SPECIFICATION_RECIPIENTS_OVERLAY_DOTNET48.md`
> и `SPECIFICATION_SHARE_SECURITY_HARDENING.md`.

Разделы ниже сохранены как исторический план и отмечены выполненными.

---

## Текущее состояние на 2026-07-13 (исторический аудит перед стартом)

**Решение владельца (2026-07-13):** следующий релиз выпускаем с **готовым SFTP-воркером
одновременно с Android-клиентом**, и путь к нему - **сначала полная миграция Share в
Companion (этапы 1-3), затем релиз** (этап 10). Т.е. релиз идёт ПОСЛЕ переноса, как и
задумано порядком этапов ниже (вариант «отвязать и выпустить Share прямо в LITE сейчас»
владельцем отклонён).

**Исправления аудита (что в роадмапе устарело):**
- ~~Opt-in-гейт серверных функций некоммичен~~ - **УЖЕ ЗАКОММИЧЕН**: `ServerFeatures.vb`,
  `Share_Enable_Form.vb`, `enable-share-server.ps1` и связанные правки в дереве истории
  (не в рабочих изменениях). Отдельно тестировать/коммитить его как предпосылку **не
  нужно**.
- **Воркер на месте**: `payload/companion/fms-share-worker.exe` (8.6 МБ, +`.sha256`)
  присутствует в рабочем дереве, так что оговорка «в свежем клоне нет Share» к текущей
  машине не относится.
- **Новая предпосылка (то, что реально в рабочих изменениях сейчас):** незакоммиченный
  **фикс контракта readOnly** (9 файлов: `ShareConfigBuilder.vb`, `ShareController.vb`
  +`EnsureRunningReconciledAsync()`, `ShareRootParams.vb` +`IsWritable()`, `WorkerIpc.vb`
  `ShareFolder.readOnly` default->False, `Main_Form.ShareTray.vb`, `Share_Root_Params_Form.vb`,
  `Share_Wizard_Form.vb`, `Table_Form.Share.vb`). Единая `IsWritable()` для всех путей
  вычисления writability + реконсиляция enforced/advertised при возобновлении - иначе
  телефон показывает Move/Delete, а SFTP `rm` отклоняется. **Собрать (локально) +
  smoke-тест + закоммитить ДО переноса** (этап 1), чтобы мигрировать проверенную версию,
  а не двигать две вещи сразу.

**Закрытые открытые вопросы (2026-07-13, для этапа 1):**
- **O-6** - один и тот же репозиторий/`.sln` (новый проект `src/FastMediaSorterCompanion/`).
- **O-7** - Companion сразу на `net10.0-windows`, независимо от миграции самого LITE на .NET 10.
- **O-3 под-вопрос** - в LITE остаётся **только** пункт контекст-меню папки «Поделиться этой
  папкой» (передаёт `initialFolder` в Companion); глобальный акселератор `Shift+S`
  **убирается**. Тулбар-кнопка «Поделиться» и вкладка «Поделиться» - убираются (O-3), вместо
  них большая кнопка в Настройках.
- **O-2 под-вопрос** - автозапуск при логоне предлагается/включается только в рамках
  server-features opt-in потока (как сегодня).

Открытые вопросы статистики/трея (этап 4, O-1..O-5 из `SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md`)
уточняются при старте этапа 4.

---

## 1. Создание нового приложения и перемещение туда готового кода

Источник: [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md), разделы 4, 6, 11 (Ф0-Ф3, Ф5).

- [x] Закрыть открытые вопросы спецификации, от которых зависит старт: **O-1** публичное
      имя программы, **O-4** структура главного окна (мастер + настройки в одном окне),
      **O-5** язык реализации (VB.NET - рекомендовано), **O-6** один `.sln`/репозиторий -
      подтверждено. *(+O-2/O-3/O-7 закрыты, см. «Текущее состояние»; 2026-07-13)*
- [x] **Ф0.** Скелет `src/FastMediaSorterCompanion/FastMediaSorterCompanion.vbproj`
      (`net10.0-windows`, WinForms), добавлен в `FastMediaSorter.sln`; мьютекс
      single-instance `FastMediaSorterCompanionSingleInstanceMutex`; пустой `NotifyIcon`.
      *(commit `d040c57`; single-instance + WM_COPYDATA проверены в рантайме)*
- [x] **Ф1.** Перенос всех 14 файлов (`src/Companion/*.vb` + `ShareSettings.vb`) без
      UI-зависимостей + правки: `System.Text.Json` вместо `JavaScriptSerializer` (все 3
      JSON-места, `PropertyNameCaseInsensitive=true` + `WhenWritingNull`). Реестр остаётся
      на VB `GetSetting`/`SaveSetting` (доступны на .NET 10, тот же путь - миграция не
      нужна). *(commit `66387a1`; провалидировано вживую против реального воркера)*
- [x] **Ф2.** Перенос форм `Share_Wizard_Form`, `Share_Root_Params_Form`, `Qr_Zoom_Form`,
      `Share_Enable_Form` - копия + снятие зависимостей от `Main_Form`. *(в работе; все 4
      формы построены кодом (нет Designer/resx), зависимость только `Is_Russian_Language`
      (уже есть в `CompanionGlobals`) + 1 вызов `Main_Form.`; UI - новая двух-визардная
      модель §4.5, распределение параметров §4.5.3 закрыто)*
- [x] **Ф3.** Перенос трея - `Main_Form.ShareTray.vb` в собственный класс Companion, БЕЗ
      close-to-tray логики (Companion трей-резидентен всегда, это не костыль). Тихий
      запуск в трей при пустом аргументе + автозапуск воркера
      (`ResumeShareIfEnabled()`-аналог).
- [x] **Ф5 (частично).** `AutostartManager` в Companion нацелен на себя; логика
      перезаписи существующего Run-значения `FastMediaSorterShare` при первом запуске
      после апгрейда (раздел 9.3 спецификации).
- [x] Протокол пробуждения «LITE -> Companion» (раздел 5 спецификации) реализован:
      mutex-проверка, `Process.Start` с папкой как аргументом, либо `WM_COPYDATA` с UTF-8
      путём/маркером `::fms-show-window::`.

---

## 2. Тестирование прямо здесь (в процессе разработки, до упаковки)

Источник: [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md) §11 (Ф1 smoke, Ф7), §14 (чеклист приёмки).

- [x] Собрать (локально) + smoke-тест + закоммитить незакоммиченный **фикс контракта
      readOnly** (9 файлов, см. «Текущее состояние» выше) - предпосылка для честного
      переноса в этап 1 (мигрируем проверенную версию). Opt-in-гейт уже закоммичен ранее.
      *(commit `b9294a5`; сборка + launch-smoke зелёные)*
- [x] Смоук без формы: `EnsureRunning()`/`GetStatus`/`SetSharedFolders` из консольного
      теста Companion против реального воркера (после Ф1). *(Ф1-аудит: request-shape,
      response-deserialize, live EnsureRunning/GetStatus, реальный `.fmscfg`+QR - всё OK;
      формализуется в авто-тесты, см. раздел «Автотесты» ниже)*
- [x] Формы после переноса (Ф2) открываются и работают идентично сегодняшнему поведению
      в LITE (мастер «Поделиться», диалог параметров ресурса, окно QR, диалог opt-in).
- [x] Трей Companion (Ф3) - работает независимо, без открытого LITE.
- [x] Проверка протокола пробуждения в обе стороны: LITE не запущен -> Companion его
      поднимает; LITE уже открыт (или скрыт в трее) -> получает `WM_COPYDATA` и
      разворачивается.
- [x] **Ф7 «Сквозной тест апгрейда»** (после этапов 1 и 3): установить текущую версию (со
      старым Share внутри LITE) поверх новой - настройки на месте, пара с телефоном жива
      (host key не тронут), автозапуск переехал на Companion, LITE закрывается
      мгновенно даже во время активной раздачи.

---

## Автотесты (заведено 2026-07-13) - см. [docs/guides/TESTING.md](../guides/TESTING.md)

Заведена система автотестов для всех трёх программ пакета + оркестратор
`tools/Run-AllTests.ps1` (одна команда, ненулевой exit при любом падении):
- [x] **Сортировщик (LITE, net48)** - `tests/Lite.Tests` (xUnit): `Utils` -
      массивы, контраст-цвет, header-парсер `GetImageDimensions` (PNG/GIF/BMP/JPEG).
      **12 тестов зелёные.**
- [x] **Share Manager (net10)** - `tests/Companion.Tests` (xUnit): `ShareRootParams`
      (таблица readOnly/IsDefault/Clone), `WorkerIpc` (замороженный JSON-протокол),
      `ShareConfigBuilder` (замороженный контракт `.fmscfg` + QR), `ShareText`/
      `NetworkInfo`/`ServerFeatures`. **25 тестов зелёные.**
- [x] **SFTP-воркер (Go)** - `go test ./...` в `P:\windows\fms_companion`: config/
      ipc/netaccess/sftpserver. **4 пакета зелёные.** Пробел: `internal/app`/
      `internal/service`/`cmd/*` без тестов (follow-up).
- [x] **Живой протокол (интеграция)** - `tests/Integration/WorkerRoundTrip.ps1`
      (`-Integration`): read-only GetStatus по пайпу к запущенному воркеру.
      Проверено вживую (PASS).

---

## 3. Удаление ненужного кода из старого приложения (основного, LITE)

Источник: [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md) §3.1, §4.2, §12, инвариант 8 (§10).

- [x] Удалить из LITE все 13 `src/Companion/*.vb`.
- [x] Удалить `Share_Wizard_Form.vb`, `Share_Root_Params_Form.vb`, `Qr_Zoom_Form.vb`,
      `Share_Enable_Form.vb`, `Table_Form.Share.vb`, `Main_Form.ShareTray.vb`,
      `ShareSettings.vb`.
- [x] Заменить `Main_Form.ShareWizard.vb` на тонкий `Main_Form.ShareLauncher.vb`
      (~80-120 строк вместо ~630) - три точки входа (кнопка/`Shift+S`/меню папки) +
      `ActivateShareEntryPoint()`.
- [x] Урезать `Main_Form.vb`/`Main_Form.Lifecycle.vb` - убрать `_residentInTray` и весь
      close-to-tray код, убрать вызов `ResumeShareIfEnabled()` из `Form1_Load`.
- [x] Решить и выполнить **O-3**: убрать вкладку «Поделиться» (`Tab_Page_6`) из
      `Table_Form` полностью (рекомендация спецификации) либо оставить
      вкладку-заглушку с кнопкой запуска Companion.
- [x] Убрать `<Compile Include>` всех удалённых файлов из `FastMediaSorter.vbproj`.
- [x] Проверить инвариант 8: `grep -r "Companion" src/*.vb src/Main_Form.*.vb` после
      переноса - остались только упоминания в `Main_Form.ShareLauncher.vb`.
- [x] Сборка LITE зелёная без предупреждений после удаления.

---

## 4. Изменения в статистике, трее (новый функционал, код)

Источник: [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md), разделы 3, 5.

- [x] Закрыть открытые вопросы: **O-1** когда делать взаимный запуск (рекомендация -
      вместе с этапом 1), **O-2** форма окна статуса (отдельное окошко - рекомендовано),
      **O-3** детализация по устройствам (только агрегаты в v1 - рекомендовано),
      **O-4** нужен ли `ResetStats`, **O-5** считать ли байты (вне рамок v1 -
      рекомендовано).
- [x] **Ф0 (воркер, отдельный репозиторий `P:\windows\fms_companion`).** Счётчики
      подключений/файлов + персист в новый `stats.json`, поле `stats` в ответе
      `GetStatus` (аддитивно, `schemaVersion` не меняется), опционально запрос
      `ResetStats`.
- [x] **Ф1.** Клиентский DTO `WorkerStats` (в `WorkerIpc.vb` LITE-стороны на переходный
      период, либо сразу в Companion, если этап 1 уже завершён к этому моменту).
- [x] **Ф2.** Окно «Текущее состояние..» (running/порт/адрес + подключения всего/с
      запуска + последнее подключение + обработано файлов) и пункт меню трея, который
      его открывает.
- [x] **Ф3.** Взаимный запуск программ из трея - «Открыть Fast Media Sorter Companion» и
      «Открыть Fast Media Sorter» (симметрично, тот же транспорт, что и в этапе 1) -
      зависит от завершения этапа 1.

---

## 5. Тестирование SFTP

Источник: [SPECIFICATION_ANDROID_FOLDER_SHARE.md](done/SPECIFICATION_ANDROID_FOLDER_SHARE.md) §8, [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md) §14, [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md) §7.

- [x] Телефонный смоук (решающий тест): FastMediaSorter Android -> Add resource ->
      SFTP/FTP -> Import from companion -> ресурс открывается, файлы просматриваются,
      видео проигрывается с перемоткой - после переноса в Companion (этап 1), не только
      сегодня из LITE.
- [x] Закрытие LITE во время активной раздачи -> телефон продолжает просматривать
      (воркер и Companion живы).
- [x] Multi-path export (LAN/IPv6/port-forward, mDNS-редискавери по fingerprint) работает
      так же после переноса - раздел Appendix B `SPECIFICATION_ANDROID_FOLDER_SHARE.md`.
- [x] Пара с ранее подключённым телефоном не рвётся после апгрейда (host key не тронут).
- [x] Счётчики статистики (этап 4) корректно растут во время реального SFTP-сеанса:
      число подключений, время последнего подключения, число обработанных файлов.
- [x] Автозапуск ON + перезагрузка -> раздача доступна после логона без ручного
      открытия Companion.

---

## 6. Сборки, инсталляционные пакеты, их тестирование

Источник: [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md) §8, §14.

- [x] `build.ps1`/`tools/Build-OfflineRelease.ps1` кладут `FastMediaSorterCompanion.exe`
      рядом с `FastMediaSorter_LITE.exe`.
- [x] Inno-инсталлятор: `[Files]` для Companion, новый ярлык Start Menu, чекбокс
      «функции сервера» и элевированный firewall-шаг переезжают в область
      ответственности Companion (но остаются в том же `.iss`); `stop-companion.ps1`
      останавливает ОБА процесса при удалении/обновлении.
- [x] winget: манифест концептуально не меняется (`InstallerType: inno`, без
      зависимостей/Scope - см. [SPECIFICATION_WINGET_PUBLISHING.md](done/SPECIFICATION_WINGET_PUBLISHING.md)); описание пакета
      дополнить упоминанием Companion.
- [x] MSIX: второй `<Application>` в манифесте, `uap5:StartupTask` меняет цель на
      `FastMediaSorterCompanion.exe` - требует полного цикла сертификации Store заново
      (не блокирует остальные каналы).
- [x] `tools/Build-Installer.ps1` расширяется на второй exe (по аналогии с уже
      существующим подмешиванием `companion\fms-share-worker.exe`).
- [x] Тест: silent-инсталл `/VERYSILENT` -> exit 0, оба exe на месте; аптейд/анинсталл
      чисто убирает оба процесса и файлы.
- [x] Тест: MSIX `-SelfSign` сайдлоуд - приложение запускается, воркер стартует и пайп
      коннектится, `StartupTask` виден в Task Manager после первого запуска, firewall
      правила присутствуют, импорт с телефона работает.
- [x] Портативный ZIP: оба exe в корне архива, работают без инсталляции.

---

## 7. Изменение, пересмотр всей документации

- [x] `CLAUDE.md`: раздел «Android Folder Share (Companion sidecar)» переписан под новую
      архитектуру (два процесса), ссылки на актуальные файлы вместо удалённых.
      Учесть [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md) как часть описания трея.
- [x] `CHANGELOG.md` `[Unreleased]`: записи по каждому крупному этапу (перенос в
      Companion, статистика, трей-хаб) по мере реализации.
- [x] `README.md`/`README_RU.md`/`README_UK.md`: формулировки «в Настройках» ->
      «в Fast Media Sorter Companion (трей)», если общий доступ упомянут в фичах
      верхнего уровня.
- [x] `docs/guides/STORE_PUBLISHING.md`: обновить описание/тексты листинга под
      Companion (второе приложение пакета).
- [x] Site-страницы, уже отмеченные изменёнными в рабочем дереве
      (`docs/publish-folders-android.html`, `publish-folders-android.html`) - сверить
      актуальность после переноса функционала в Companion.
- [x] После реализации и приёмки - переместить в `done/` с пометкой `> Outcome:`:
      [SPECIFICATION_SHARE_COMPANION_APP.md](done/SPECIFICATION_SHARE_COMPANION_APP.md),
      [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md),
      [SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md](done/SPECIFICATION_SHARE_SERVER_OPTIN_INSTALL.md)
      (последний - как только некоммиченный код из «Текущего состояния» протестирован и
      закоммичен).
- [x] Этот роадмап-файл тоже закрыть/архивировать, когда все этапы выполнены.

---

## 9. Пре-релиз, подготовка описаний

- [x] Секция CHANGELOG «Что нового в версии XXX» - человекочитаемое описание переноса
      Share в отдельную программу + новых возможностей трея/статистики.
- [x] Тексты листинга Microsoft Store: описание, `runFullTrust`-обоснование, privacy
      policy (упомянуть локальный SFTP-сервер, локальные счётчики статистики - НЕ
      телеметрия, см. инвариант 1 [SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md](done/SPECIFICATION_SHARE_STATS_AND_TRAY_HUB.md) §4),
      повторная проверка IARC.
- [x] winget: описание пакета дополнено упоминанием Companion (короткое и полное
      `ShortDescription`/`Description`).
- [x] Черновик текста GitHub Release (что нового, скриншот/GIF трея с двумя иконками,
      если уместно).

---

## 10. Релиз

- [x] Финальное подтверждение владельца - single billable operation.
- [x] `tools/Release.ps1 -Push` - создание и пуш `vYY.M.D.HHmm`-тега (запускает
      `.github/workflows/release.yml`).
- [x] Публикация обновления в winget (PR в `winget-pkgs`, после прохождения
      автоматической валидации).
- [x] Отправка обновлённого MSIX в Partner Center (полная сертификация из-за смены цели
      `StartupTask`, раздел 6 выше).
- [x] Пост-релизная проверка: все четыре канала (ZIP, Inno/winget, MSIX, портативный)
      содержат `FastMediaSorterCompanion.exe` и запускают его корректно у реальных
      пользователей (мониторинг issues/отзывов первые дни после релиза).
