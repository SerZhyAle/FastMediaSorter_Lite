# Автоматическое тестирование (три программы пакета)

Дата аудита: 2026-07-13

Пакет Fast Media Sorter состоит из трёх отдельных программ, у каждой свой рантайм
и своя тестовая система. Один оркестратор запускает всё сразу:

```powershell
.\tools\Run-AllTests.ps1              # все три набора (unit/логика), быстро, без сети/телефона
.\tools\Run-AllTests.ps1 -Integration # + живой round-trip по пайпу к запущенному воркеру
.\tools\Run-AllTests.ps1 -SkipGo      # без Go-набора (если репозиторий воркера не склонирован)
```

Код возврата ненулевой, если хоть один набор упал - годится для pre-commit / CI.

| # | Программа | Рантайм | Проект/набор | Команда |
|---|---|---|---|---|
| 1 | Сортировщик (LITE) | net48 | `tests/Lite.Tests` (xUnit) | `dotnet test` |
| 2 | Менеджер воркера / Share Manager | net10.0-windows | `tests/Companion.Tests` (xUnit) | `dotnet test` |
| 3 | SFTP-воркер | Go | `P:\windows\fms_companion` | `go test ./...` |
| + | Живой протокол (интеграция) | PowerShell | `tests/Integration/WorkerRoundTrip.ps1` | `-Integration` |

Тестовые проекты **намеренно не входят** в `FastMediaSorter.sln`, чтобы не менять
релизную сборку (`build.ps1` / `release.yml`); их гоняет только оркестратор через
`dotnet test` (сам делает restore).

---

## 1. Сортировщик (LITE, net48) - `tests/Lite.Tests`

Приложение почти целиком UI (WinForms/GDI+/WebBrowser/LibVLC) и потому в основном
проверяется вручную (см. CLAUDE.md «Testing & Validation»). Юнит-тестами покрыта
**чистая логика, живущая только в LITE** (не в Share-подсистеме). Исходники не
референсятся как сборка, а **линкуются** (`<Compile Include>`), чтобы не тянуть
весь ILMerge/LibVLC/Tesseract-хвост; `RootNamespace=fmsl`, так что модули видны по
своим именам.

Покрыто (12 тестов): `Utils` -
- `AddAt`/`RemoveAt` - вставка/удаление в массиве по индексу (края и середина);
- `GetOppositeColor` - контрастный чёрный/белый по яркости фона;
- **`GetImageDimensions`** - разбор размеров из ЗАГОЛОВКА файла (PNG/GIF/BMP/JPEG)
  без GDI+-декодирования. Это критично: фоновый воркер читает размер, пока UI-поток
  может делать `GetPixel` по тому же изображению, и параллельный GDI+-декод портит
  общее состояние. Плюс поведение на неизвестном/отсутствующем файле (`Size.Empty`).

WEBP-ветка `GetImageDimensions` (через WPF `BitmapDecoder`) в юнит-тестах не гоняется
(нужен реальный webp + инициализация WIC) - проверяется вручную при загрузке webp.

---

## 2. Менеджер воркера / Share Manager (net10) - `tests/Companion.Tests`

Референсит проект `FastMediaSorterCompanion` и проверяет **портированную Share-логику**
(она же покрывает соответствующий код, ранее живший в LITE - это один и тот же код).

Покрыто (25 тестов):
- **`ShareRootParams`** - таблица истинности `IsWritable()` (обычная/RO/destination),
  `IsDefault()` (все дефолты -> v1; любое поле -> не дефолт; слайдшоу 10с = дефолт),
  `Clone()` (глубокая копия + нормализация null), `Store.GetFor` неизвестного пути ->
  дефолт без записи в реестр. Это единый источник флага readOnly - если он «поедет»,
  вернётся баг «телефон показывает Move/Delete, а SFTP rm запрещён».
- **`WorkerIpc` (замороженный протокол пайпа)** - точная форма JSON запроса
  `SetSharedFolders` (`schemaVersion`/`type`/`folders[].name/hostPath/readOnly`),
  пропуск `folders` у `GetStatus` (`WhenWritingNull`), разбор ответа воркера
  (camelCase -> PascalCase DTO, вложенные status/reachability/roots), envelope ошибки.
- **`ShareConfigBuilder` (замороженный контракт `.fmscfg`)** - `Build` строится из
  in-memory статуса (без воркера): обязательные ключи, порядок `accessPaths`
  (`lan` -> `ipv6` -> `portforward`), пропуск внешних адресов при «только LAN»,
  сейф-гард «не включать пароль», per-root `readOnly`, рендер QR-PNG. **Android-клиент
  выпускается ровно против этой формы** (SPECIFICATION_QR_IMPORT_ANDROID.md).
- **`ShareText`/`NetworkInfo`/`ServerFeatures`** - локализованные строки RU/EN
  непусты и различаются, `AccessNote` не падает на null-reach, `LocalIPv4` - валидный
  IPv4 или пусто, гейт серверных функций (`IsEnabled/CanEnable/MarkerPath`) безопасно
  вызывается в любой момент.

`WorkerIpc.Send` по РЕАЛЬНОМУ пайпу проверяется интеграционным набором ниже, а не
юнитом (нужен запущенный воркер).

---

## 3. SFTP-воркер (Go) - `P:\windows\fms_companion`

`go test ./...` (Go в `C:\Program Files\Go\bin`). Замороженный бинарник не трогаем -
исходники живут в **отдельном репозитории** (правило CLAUDE.md), тесты запускаются
там же.

Покрыто (4 пакета, все зелёные): `internal/config` (схема `.fmscfg`), `internal/ipc`
(протокол пайпа), `internal/netaccess` (проброс порта/reachability), `internal/sftpserver`.

**Пробел покрытия (для follow-up):** без тестов - `internal/app`, `internal/service`,
`cmd/worker`, `cmd/devserve` и корневой пакет. Это в основном lifecycle/точки входа;
кандидаты на добавление unit-тестов - `internal/app` (сборка статуса, применение
shares) и `internal/service`. Оркестратор явно печатает число пакетов без тестов,
чтобы пробел не выглядел как «всё покрыто».

---

## 4. Живой протокол (интеграция) - `tests/Integration/WorkerRoundTrip.ps1`

Framework-free проверка: коннектится к `\\.\pipe\fms-companion` запущенного воркера,
шлёт `GetStatus`, валидирует форму ответа (Appendix A). **Только чтение** - никаких
`SetSharedFolders`, `shares.json` пользователя не трогается. Если воркер не запущен -
SKIP (exit 0), а не падение: запустите Share Manager (или воркер) и повторите с
`-Integration`.

---

## Матрица покрытия и пробелы

| Область | Тип | Статус |
|---|---|---|
| `.fmscfg` контракт (сборка) | unit (Companion) | покрыто |
| Пайп-протокол (форма JSON) | unit (Companion) + integration | покрыто |
| Правила readOnly / per-root params | unit (Companion) | покрыто |
| Разбор размеров изображения (header) | unit (LITE) | покрыто |
| Массивы/цвет-хелперы | unit (LITE) | покрыто |
| Воркер: config/ipc/netaccess/sftp | unit (Go) | покрыто |
| Воркер: app/service/cmd | - | **пробел** (follow-up) |
| Формы Share Manager (Ф2 UI) | - | вручную (UI); появятся после Ф2 |
| LITE UI (просмотр/видео/OCR/перспектива) | - | вручную (сильно завязано на GDI+/COM) |
| `FileManager` EXIF-поворот / GIF-детект | - | кандидат (завязан на GDI+/глобалы) |

**Рекомендуемые follow-up:** (1) Go-тесты для `internal/app`/`internal/service`;
(2) после Ф2 - smoke-запуск форм Share Manager; (3) при желании - вынести из
`FileManager` чистую логику EXIF-ориентации для юнит-теста.

CI: наборы гоняются локально/перед коммитом. В `release.yml` пока не встроены
(релиз - отдельная платная операция); при необходимости добавить job `dotnet test`
+ `go test` до сборки артефактов.
