# Инструкция по добавлению FastMediaSorter в WinGet

## Что такое WinGet?

WinGet (Windows Package Manager) — официальный менеджер пакетов от Microsoft.  
После добавления пользователи смогут устанавливать программу одной командой:

```powershell
winget install SerZhyAle.FastMediaSorter
```

---

## Предварительные требования

- Аккаунт на [GitHub](https://github.com)
- Установленный [Git](https://git-scm.com)
- Готовый `.exe` файл релизной сборки
- Опубликованный релиз на GitHub с загруженным `.exe`

---

## ШАГ 1 — Создать релиз на GitHub

### 1.1 Создать тег версии

```powershell
cd C:\GIT\FastMediaSorter_Lite
git tag v1.0.0
git push origin v1.0.0
```

### 1.2 Собрать Release-версию

В Visual Studio:
- **Build → Configuration Manager** → выбрать `Release`
- **Build → Rebuild Solution**

Файл будет в: `bin\Release\FastMediaSorter_LITE.exe`

### 1.3 Опубликовать релиз на GitHub

1. Перейти на https://github.com/SerZhyAle/FastMediaSorter_Lite/releases
2. Нажать **"Draft a new release"**
3. Заполнить поля:
   - **Tag:** `v1.0.0`
   - **Title:** `FastMediaSorter v1.0.0`
   - **Description:** описание изменений
4. В разделе **"Attach binaries"** загрузить `FastMediaSorter_LITE.exe`
5. Нажать **"Publish release"**

---

## ШАГ 2 — Получить SHA256 хеш файла

После загрузки файла в релиз нужно узнать его хеш.  
Выполнить в PowerShell:

```powershell
(Get-FileHash "bin\Release\FastMediaSorter_LITE.exe" -Algorithm SHA256).Hash
```

Сохранить результат — он нужен для манифеста.

> **Важно:** хеш берётся именно от того файла, который загружен в релиз на GitHub.  
> После любого перекомпилирования хеш изменится и манифест нужно обновить.

---

## ШАГ 3 — Установить инструмент winget-create (опционально)

Инструмент `wingetcreate` автоматически создаёт манифест:

```powershell
winget install Microsoft.WingetCreate
```

Использование:

```powershell
wingetcreate new https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/download/v1.0.0/FastMediaSorter_LITE.exe
```

Инструмент задаст вопросы и создаст готовый манифест.  
Если хочется сделать вручную — перейти к Шагу 4.

---

## ШАГ 4 — Создать манифест вручную

### Структура папок манифеста

В репозитории `winget-pkgs` манифесты хранятся по схеме:

```
manifests/
  s/
    SerZhyAle/
      FastMediaSorter/
        1.0.0/
          SerZhyAle.FastMediaSorter.yaml
```

Первая буква — первая буква Publisher в нижнем регистре (`s` от `SerZhyAle`).

### Содержимое файла манифеста

Файл `SerZhyAle.FastMediaSorter.yaml`:

```yaml
PackageIdentifier: SerZhyAle.FastMediaSorter
PackageVersion: 1.0.0
PackageName: FastMediaSorter
Publisher: SerZhyAle
PublisherUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite
PublisherSupportUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite/issues
PackageUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite
License: MIT
LicenseUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite/blob/main/LICENSE
ShortDescription: Fast media file sorter and organizer for Windows
Description: |
  FastMediaSorter — лёгкая утилита для Windows для быстрой сортировки
  и перемещения/копирования изображений и видео в нужные папки с помощью горячих клавиш.

  Возможности:
  - Быстрая сортировка файлов горячими клавишами (1-0)
  - Поддержка изображений (JPG, PNG, GIF, BMP и др.) и видео (MP4, MKV и др.)
  - Режим слайдшоу и случайный выбор файлов
  - Поворот изображений
  - Настраиваемые цветовые схемы
  - Поддержка русского и английского языков

Homepage: https://github.com/SerZhyAle/FastMediaSorter_Lite
Tags:
  - media
  - sorter
  - organizer
  - images
  - videos
  - utility

ReleaseNotes: Initial release v1.0.0
ReleaseNotesUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/tag/v1.0.0

Installers:
  - Architecture: x64
    InstallerType: portable
    InstallerUrl: https://github.com/SerZhyAle/FastMediaSorter_Lite/releases/download/v1.0.0/FastMediaSorter_LITE.exe
    InstallerSha256: ВСТАВИТЬ_SHA256_ХЕШ_СЮДА
    InstallerLocale: en-US
    Commands:
      - FastMediaSorter

ManifestType: singleton
ManifestVersion: 1.4.1
```

> Заменить `ВСТАВИТЬ_SHA256_ХЕШ_СЮДА` на значение из Шага 2.

---

## ШАГ 5 — Локальная проверка манифеста

Перед отправкой проверить манифест локально:

```powershell
# Установить winget CLI если ещё не установлен
# Проверить манифест
winget validate SerZhyAle.FastMediaSorter.yaml

# Тестовая установка из локального манифеста
winget install --manifest SerZhyAle.FastMediaSorter.yaml
```

---

## ШАГ 6 — Fork репозитория winget-pkgs

1. Открыть https://github.com/microsoft/winget-pkgs
2. Нажать кнопку **Fork** (верхний правый угол)
3. Выбрать свой аккаунт → нажать **Create fork**

---

## ШАГ 7 — Клонировать fork и добавить манифест

```powershell
# Клонировать свой fork
git clone https://github.com/ВАШ_ЛОГИН/winget-pkgs.git
cd winget-pkgs

# Добавить upstream (оригинальный репозиторий)
git remote add upstream https://github.com/microsoft/winget-pkgs.git

# Синхронизировать с upstream
git fetch upstream
git merge upstream/master

# Создать ветку для нового пакета
git checkout -b add-fastmediasorter-1.0.0
```

Создать папку и скопировать манифест:

```powershell
# Создать папку для манифеста
$dir = "manifests\s\SerZhyAle\FastMediaSorter\1.0.0"
New-Item -ItemType Directory -Path $dir -Force

# Скопировать манифест (если он уже создан)
Copy-Item "C:\GIT\FastMediaSorter_Lite\SerZhyAle.FastMediaSorter.yaml" -Destination "$dir\SerZhyAle.FastMediaSorter.yaml"
```

Или создать файл манифеста вручную в этой папке.

---

## ШАГ 8 — Сделать коммит и Push

```powershell
# Добавить файлы
git add manifests/s/SerZhyAle/FastMediaSorter/

# Сделать коммит (название строго по шаблону!)
git commit -m "Add FastMediaSorter v1.0.0"

# Отправить в свой fork
git push origin add-fastmediasorter-1.0.0
```

---

## ШАГ 9 — Создать Pull Request

1. Открыть свой fork: `https://github.com/ВАШ_ЛОГИН/winget-pkgs`
2. GitHub покажет баннер **"Compare & pull request"** — нажать на него
3. Заполнить описание PR:
   - **Title:** `Add FastMediaSorter v1.0.0`
   - **Description:** краткое описание программы на английском
4. Нажать **"Create pull request"**

### Шаблон описания PR:

```
## Description
FastMediaSorter is a lightweight Windows utility for quick sorting and 
moving/copying image and video files using keyboard shortcuts.

## Type of change
- [x] New package

## Validation
- [x] I have tested the manifest locally with `winget install --manifest`
- [x] The SHA256 hash matches the file in the release
- [x] The installer URL is publicly accessible
```

---

## ШАГ 10 — Ожидание проверки

После создания PR автоматически запустится бот `winget-bot`, который:

1. ✅ Проверит структуру манифеста
2. ✅ Скачает и проверит файл по SHA256
3. ✅ Запустит тестовую установку
4. ✅ Проверит правила оформления

Если все проверки пройдут — Microsoft-ревьюер одобрит PR.  
Обычно это занимает **1–7 дней**.

---

## Возможные ошибки и их решения

| Ошибка | Решение |
|--------|---------|
| SHA256 не совпадает | Пересчитать хеш от актуального файла из релиза |
| URL недоступен | Убедиться, что релиз опубликован (не Draft) |
| Неверная структура папок | Проверить путь: `manifests/s/SerZhyAle/FastMediaSorter/1.0.0/` |
| ManifestVersion устарел | Проверить актуальную версию на [winget-pkgs/wiki](https://github.com/microsoft/winget-pkgs/wiki) |
| PackageIdentifier неверный | Формат: `Издатель.НазваниеПриложения` только латиница |

---

## Обновление версии (при выходе новой версии)

```powershell
# Синхронизировать с upstream
git fetch upstream
git checkout master
git merge upstream/master

# Создать ветку для новой версии
git checkout -b add-fastmediasorter-1.1.0

# Создать папку для новой версии
New-Item -ItemType Directory -Path "manifests\s\SerZhyAle\FastMediaSorter\1.1.0" -Force

# Скопировать и отредактировать манифест (изменить версию и URL)
# ...

git add manifests/s/SerZhyAle/FastMediaSorter/1.1.0/
git commit -m "Update FastMediaSorter to v1.1.0"
git push origin add-fastmediasorter-1.1.0
```

Затем создать новый Pull Request.

---

## Полезные ссылки

- 📦 [Репозиторий winget-pkgs](https://github.com/microsoft/winget-pkgs)
- 📖 [Документация по манифестам](https://github.com/microsoft/winget-pkgs/wiki/Authoring-a-Package-Manifest)
- 🛠️ [Инструмент winget-create](https://github.com/microsoft/winget-create)
- ✅ [Правила оформления пакетов](https://github.com/microsoft/winget-pkgs/wiki/Submitting-packages)
- 🔍 [Поиск существующих пакетов](https://winget.run)
