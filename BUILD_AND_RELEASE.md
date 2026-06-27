# Сборка и Релиз - два разных понятия

Этот файл - единый источник правды о том, что такое "сборка" и что такое "релиз" в проекте,
и какими скриптами они выполняются. Цель: тестовая **сборка** не должна стоить ни минуты
GitHub Actions, а **релиз** не должен ничего забыть.

## Золотое правило биллинга

> GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)) запускается
> **только при push тега `v*`**. Больше ничто (push в `main`, в любую ветку, правки доков) его не триггерит.

Следствие: пока не запушен тег `vYY.M.D.HHmm`, на GitHub **не тратится ни одной платной минуты**.
Поэтому "сборка" (которая тега не создаёт) по определению бесплатна, а единственная команда,
запускающая платную работу - это `git push origin <tag>` внутри релизного флоу.

---

## 1. СБОРКА (build) - локально, бесплатно

**Что это:** собрать exe, проверить вручную, при удаче - закоммитить. Всё на своей машине.

**Чем делается:** [build.ps1](build.ps1)

```powershell
.\build.ps1
```

Что происходит:
1. Находит MSBuild (vswhere), restore NuGet, `Rebuild` в Release.
2. Кладёт single-file exe в `bin\SingleFile\` и копирует в рабочие папки (`C:\GD\...`).
3. **Тег НЕ создаётся, на GitHub ничего не уходит.**

**Флоу:**
1. `.\build.ps1`
2. Запустить `bin\Release\FastMediaSorter_LITE.exe`, проверить нужное вручную.
3. Если хорошо - `git add -A && git commit`. Если надо - `git push` (это всё ещё **бесплатно**:
   push в ветку Actions не запускает).

**Запрещено в рамках сборки:** `git tag v*` и `git push --tags` / `git push origin v*`.
Это уже релиз (см. ниже) и это платно.

> Когда меня (Claude) просят "сделай сборку" / "собери" / "проверь сборку" - я выполняю
> **только** этот локальный флоу и **никогда** не создаю и не пушу тег без явной команды "релиз".

---

## 2. РЕЛИЗ (release) - GitHub + публикации, платно

**Что это:** обновить документацию и сайт, собрать на GitHub, опубликовать в winget и Microsoft Store.

**Оркестратор:** [tools/Release.ps1](tools/Release.ps1) - делает локальную контрольную сборку
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
- [ ] Обновлён сайт [docs/index.html](docs/index.html) / [docs/privacy.html](docs/privacy.html) по контенту.
      Версию править НЕ нужно - кнопки ведут на `/releases/latest` и на winget id, без захардкоженного номера.
- [ ] `CLAUDE.md` отражает изменения архитектуры (если были).
- [ ] Всё закоммичено и запушено в `main` (бесплатно).

**B. Сборка и публикация на GitHub (тег):**
- [ ] `.\tools\Release.ps1 -Push` - создаёт тег `vYY.M.D.HHmm` и пушит его.
- [ ] GitHub Actions собрал Release и приложил 4 ассета (setup.exe + zip + два `.sha256`).
      Следить: https://github.com/SerZhyAle/FastMediaSorter_Lite/actions

**C. winget (после того как GitHub release готов):**
- [ ] Манифест указывает на Inno **`setup.exe`** напрямую (`InstallerType: inno`), без зависимостей,
      без `Scope`. Подробности и грабли - [SPECIFICATION_WINGET_PUBLISHING.md](SPECIFICATION_WINGET_PUBLISHING.md).
- [ ] PR в `microsoft/winget-pkgs` для `SerZhyAle.FastMediaSorter` обновлён на новую версию + SHA256.

**D. Microsoft Store (MSIX, опционально, не блокирует A-C):**
- [ ] `cd msix; .\build-msix.ps1 -IdentityName "<имя из Partner Center>"` (БЕЗ `-SelfSign` для Store).
- [ ] Загрузить unsigned `.msix` в Partner Center (Microsoft подпишет сам).
- [ ] Полный плейбук: [STORE_PUBLISHING.md](STORE_PUBLISHING.md), [msix/README.md](msix/README.md),
      промпт-памятка [tools/store/STORE_PUBLISHING_PROMPT.md](tools/store/STORE_PUBLISHING_PROMPT.md).

---

## Кратко

| | Сборка | Релиз |
| --- | --- | --- |
| Где | локально | GitHub + winget + Store |
| Команда | `.\build.ps1` | `.\tools\Release.ps1 -Push` |
| Тег `v*` | нет | да (это и есть триггер) |
| Стоимость Actions | **$0** | платные минуты (~1 Windows-job на тег) |
| Триггер CI | - | push тега `vYY.M.D.HHmm` |
