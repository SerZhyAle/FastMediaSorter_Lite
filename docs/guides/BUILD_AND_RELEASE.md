# Сборка и Релиз - два разных понятия

Этот файл - единый источник правды о том, что такое "сборка" и что такое "релиз" в проекте,
и какими скриптами они выполняются. Цель: тестовая **сборка** не должна стоить ни минуты
GitHub Actions, а **релиз** не должен ничего забыть.

## Золотое правило биллинга

> GitHub Actions ([.github/workflows/release.yml](../../.github/workflows/release.yml)) запускается
> **только при push тега `v*`**. Больше ничто (push в `main`, в любую ветку, правки доков) его не триггерит.

Следствие: пока не запушен тег `vYY.M.D.HHmm`, на GitHub **не тратится ни одной платной минуты**.
Поэтому "сборка" (которая тега не создаёт) по определению бесплатна, а единственная команда,
запускающая платную работу - это `git push origin <tag>` внутри релизного флоу.

---

## Что вообще собирается: три exe в одной папке

| exe | Проект | Рантайм | Чем делается | Для кого |
| --- | --- | --- | --- | --- |
| `FastMediaSorter_LITE.exe` | `src/Modern/FastMediaSorter.Modern.vbproj` | net10.0-windows, x64, self-contained single-file | **только `dotnet publish`** | мейнлайн: Win10 1607+ / 11 / Server 2016+ |
| `FastMediaSorter_x86.exe` | `src/FastMediaSorter.vbproj` | net48, x86 | `msbuild` | Win7/8.1 и 32-битная Windows |
| `FastMediaSorterCompanion.exe` | `src/FastMediaSorterCompanion/` | net10, x64 | `dotnet publish` | Share Manager, без изменений |

Имя `FastMediaSorter_LITE.exe` заморожено и теперь принадлежит **мейнлайну** (x64) - он встаёт
на место установленного exe. Оба вьюера кладутся **рядом, в одну папку** и делят соседние
библиотеки (кодеки/OCR/воркер). Это **одна программа**: общий мьютекс, общая ветка реестра,
общие настройки - второй запуск просто передаёт файл в уже открытое окно, а не поднимает второе.
Нативные рантаймы каждый берёт из одного и того же дерева по битности своего процесса
(`libvlc\win-x64` vs `win-x86`, `x64\` vs `x86\` у tesseract).

> ### Главное правило сборки
> **`msbuild` сам по себе НЕ делает мейнлайн-exe.** Из решения он собирает net48-вьюер
> (`bin\Release\FastMediaSorter_x86.exe`); net10-проекты (`Modern`, Companion) в решении тоже
> компилируются, но их msbuild-выхлоп - обычный framework-dependent exe в собственной папке проекта
> (`src\Modern\bin\Release\net10.0-windows\`), а не то, что мы отгружаем: self-contained single-file
> включается только при `-r win-x64`. Готовый `FastMediaSorter_LITE.exe` появляется **только** после
>
> ```powershell
> dotnet publish src\Modern\FastMediaSorter.Modern.vbproj -c Release -r win-x64
> ```
>
> **Любой скрипт или шаг, который зовёт msbuild, а потом ищет `FastMediaSorter_LITE.exe` - сломан.**
> Поэтому каждый упаковщик ([build.ps1](../../build.ps1),
> [tools/Build-Installer.ps1](../../tools/Build-Installer.ps1),
> [tools/Build-OfflineRelease.ps1](../../tools/Build-OfflineRelease.ps1),
> [release.yml](../../.github/workflows/release.yml), [publishing/msix/build-msix.ps1](../../publishing/msix/build-msix.ps1))
> делает **свой** `dotnet publish` модерн-проекта и кладёт в пакет **оба** exe.

**Нужно локально и на CI-раннере:** .NET 10 SDK (он уже стоял ради Companion) + MSBuild (VS 2022).

**Инсталлятор** ([publishing/installer/FastMediaSorter.iss](../../publishing/installer/FastMediaSorter.iss)) ставит **оба** exe,
а ярлык, запуск после установки и файловые ассоциации наводит на тот, который **реально пойдёт**
на этой машине (`UseModernExe`: Windows 10 build >= 14393 -> мейнлайн, иначе x86-собрат).
`MinVersion` инсталлятора по-прежнему `6.1` (Win7).

**Оффлайн-payload** ([tools/Prepare-OcrOfflinePayload.ps1](../../tools/Prepare-OcrOfflinePayload.ps1))
по умолчанию **вырезает** 32-битные нативы (~100 МБ): x86-вьюер догрузит их сам при первом запуске.
Нужен полностью оффлайновый x86 - зовите с `-KeepX86`.

> x86-вьюер пока **не урезан** по фичам: в нём те же OCR/перевод/Share, что и в мейнлайне.
> Отдельный "тонкий" x86 без этих подсистем - будущий шаг, не текущее состояние.

---

## 1. СБОРКА (build) - локально, бесплатно

**Что это:** собрать exe, проверить вручную, при удаче - закоммитить. Всё на своей машине.

**Чем делается:** [build.ps1](../../build.ps1) - собирает **все три** программы в одну команду.

```powershell
.\build.ps1
```

Что происходит:
1. Находит MSBuild (vswhere), restore NuGet, `Rebuild` в Release -> `bin\Release\FastMediaSorter_x86.exe`.
2. `dotnet publish` модерн-проекта -> `bin\ModernPublish\`, затем кладёт мейнлайн-exe рядом
   с x86-собратом в `bin\Release\`.
3. `dotnet publish` Companion -> `bin\CompanionPublish\FastMediaSorterCompanion.exe`.
4. Копирует **оба** вьюера + Companion + воркер (`companion\`) в рабочие папки
   (`C:\GD\i\`, `C:\GD\tc\SZA\_APP\`).
5. **Тег НЕ создаётся, на GitHub ничего не уходит.**

Флаги: `-SkipModern` (без x64-мейнлайна), `-SkipCompanion` (без Share Manager),
`-NoClean` (без предварительной уборки).

Что где лежит после сборки:
- `bin\Release\` - **это и есть форма дистрибутива**: оба exe + общие библиотеки рядом.
- `bin\SingleFile\` - "тонкий" standalone x86-вьюер (exe сам по себе, нативы тянет
  в `%LOCALAPPDATA%` при первом запуске).
- `bin\ModernPublish\`, `bin\CompanionPublish\` - сырые выхлопы `dotnet publish`.

**Флоу:**
1. `.\build.ps1`
2. Прогнать **оба** вьюера из `bin\Release\` (чек-лист ниже).
3. Если хорошо - `git add -A && git commit`. Если надо - `git push` (это всё ещё **бесплатно**:
   push в ветку Actions не запускает).

### Что проверить руками после сборки (оба exe)

Оба вьюера - одна программа, но **разные рантаймы**, и регрессия спокойно живёт только в одном из них:

- [ ] `bin\Release\FastMediaSorter_LITE.exe` (x64, net10) - открывается папка, листание, картинки.
- [ ] `bin\Release\FastMediaSorter_x86.exe` (net48) - то же самое.
- [ ] Видео в **обоих**: нативы разной битности берутся из одного дерева - самое хрупкое место
      двух-exe раскладки. Учтите, что видео-тракты у них разные: x86 играет H.264 через
      IE WebBrowser с фолбэком на LibVLC, мейнлайн - только LibVLC (иначе внешний плеер).
- [ ] Единство приложения: запустить один exe, потом **второй** с файлом в аргументе - файл должен
      уехать в **уже открытое** окно (общий мьютекс), а не поднять второе.
- [ ] Настройки, выставленные в одном exe, видны во втором (общая ветка реестра).
- [ ] Трогали OCR/перевод или Share - проверьте в **мейнлайне** (в Store/MSIX едет только он),
      но помните, что в x86 этот код тоже есть.

**Запрещено в рамках сборки:** `git tag v*` и `git push --tags` / `git push origin v*`.
Это уже релиз (см. ниже) и это платно.

> Когда меня (Claude) просят "сделай сборку" / "собери" / "проверь сборку" - я выполняю
> **только** этот локальный флоу и **никогда** не создаю и не пушу тег без явной команды "релиз".

---

## 2. РЕЛИЗ (release) - GitHub + публикации, платно

**Что это:** обновить документацию и сайт, собрать на GitHub, опубликовать в winget и Microsoft Store.

**Оркестратор:** [tools/Release.ps1](../../tools/Release.ps1) - делает локальную контрольную сборку
(чтобы НЕ платить за заведомо падающий CI), создаёт и пушит тег, печатает чек-лист публикаций.

```powershell
# 1) Сухой прогон - покажет версию/тег и проверит CI-сборку локально, БЕЗ push (бесплатно):
.\tools\Release.ps1

# 2) Реальный релиз - то же самое, но в конце пушит тег и запускает GitHub Actions:
.\tools\Release.ps1 -Push
```

### Чек-лист релиза (что нельзя забыть)

**A. Документация и сайт (до тега):**
- [ ] Обновлены `README.md`, `README_RU.md`, `README_UK.md` если менялись фичи.
- [ ] Обновлён сайт [docs/index.html](../index.html) / [docs/privacy.html](../privacy.html) по контенту.
      Версию править НЕ нужно - кнопки ведут на `/releases/latest` и на winget id, без захардкоженного номера.
- [ ] `CLAUDE.md` отражает изменения архитектуры (если были).
- [ ] Всё закоммичено и запушено в `main` (бесплатно).

**B. Сборка и публикация на GitHub (тег):**
- [ ] `.\tools\Release.ps1 -Push` - создаёт тег `vYY.M.D.HHmm` и пушит его.
- [ ] Workflow сам ставит .NET 10 SDK (`setup-dotnet`), собирает msbuild-ом x86-вьюер и делает
      **свои** `dotnet publish` мейнлайна и Companion, после чего стейджит в пакет **оба** exe.
      Версия из тега пробрасывается в publish через `-p:ReleaseVersion=`.
- [ ] GitHub Actions собрал Release и приложил 4 ассета (setup.exe + zip + два `.sha256`).
      Следить: https://github.com/SerZhyAle/FastMediaSorter_Lite/actions

**C. winget (после того как GitHub release готов):**
- [ ] Манифест указывает на Inno **`setup.exe`** напрямую (`InstallerType: inno`), без зависимостей,
      без `Scope`. Подробности и грабли - [SPECIFICATION_WINGET_PUBLISHING.md](../specifications/done/SPECIFICATION_WINGET_PUBLISHING.md).
- [ ] PR в `microsoft/winget-pkgs` для `SerZhyAle.FastMediaSorter` обновлён на новую версию + SHA256.

**D. Microsoft Store (MSIX, опционально, не блокирует A-C):**
- [ ] `cd publishing\msix; .\build-msix.ps1 -IdentityName "<имя из Partner Center>"` (БЕЗ `-SelfSign` для Store).
      Пакет **только x64 и только мейнлайн**: скрипт сам публикует модерн-проект, а
      `FastMediaSorter_x86.exe` из стейджа исключается.
- [ ] Загрузить unsigned `.msix` в Partner Center (Microsoft подпишет сам).
- [ ] Полный плейбук: [STORE_PUBLISHING.md](STORE_PUBLISHING.md), [publishing/msix/README.md](../../publishing/msix/README.md),
      промпт-памятка [publishing/store/STORE_PUBLISHING_PROMPT.md](../../publishing/store/STORE_PUBLISHING_PROMPT.md).

### Чего ещё никто не проверял (честно)

Раскладка "два exe" собирается и гоняется локально, но на 2026-07-16 **не подтверждено на живую**:
- [ ] установка **с правами админа** (elevated install);
- [ ] ветка Win7/8.1 в инсталляторе (`UseModernExe = False`): ярлык/ассоциации на
      `FastMediaSorter_x86.exe` на настоящей старой ОС;
- [ ] **настоящий релиз по тегу** - workflow с publish-шагами по тегу ещё ни разу не гонялся.

Это не "сломано", это "не проверено". Первый релиз после перехода стоит вести с оглядкой на эти пункты.

---

## Кратко

| | Сборка | Релиз |
| --- | --- | --- |
| Где | локально | GitHub + winget + Store |
| Команда | `.\build.ps1` | `.\tools\Release.ps1 -Push` |
| Что на выходе | оба вьюера в `bin\Release` + Companion | setup.exe + zip + два `.sha256` в GitHub Release |
| Тег `v*` | нет | да (это и есть триггер) |
| Стоимость Actions | **$0** | платные минуты (~1 Windows-job на тег) |
| Триггер CI | - | push тега `vYY.M.D.HHmm` |
