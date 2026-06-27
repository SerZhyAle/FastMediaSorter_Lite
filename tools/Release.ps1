<#
.SYNOPSIS
    Оркестратор РЕЛИЗА FastMediaSorter LITE. См. ../BUILD_AND_RELEASE.md.

.DESCRIPTION
    "Релиз" = собрать на GitHub + опубликовать. GitHub Actions (release.yml) запускается
    ТОЛЬКО при push тега 'v*', поэтому единственная платная операция здесь - это push тега.

    Скрипт по умолчанию работает как СУХОЙ ПРОГОН (dry-run): печатает версию/тег,
    локально проверяет, что CI-сборка пройдёт (Build-OfflineRelease.ps1), но НИЧЕГО не пушит.
    Тег создаётся и пушится только с явным -Push. Это страхует от случайной оплаты Actions.

    Локальная контрольная сборка нужна для экономии: если упадёт, мы узнаем об этом
    БЕСПЛАТНО, а не заплатив за упавший Windows-job на GitHub.

.PARAMETER Version
    Версия в формате YY.M.D.HHmm. По умолчанию - текущие дата/время.

.PARAMETER Push
    Реально создать и запушить тег 'v<Version>' (запускает платный GitHub Actions).
    Без него скрипт только показывает план и проверяет сборку.

.PARAMETER SkipLocalCheck
    Пропустить локальную контрольную сборку (Build-OfflineRelease.ps1). Не рекомендуется:
    локальная проверка ловит ошибки до оплаты CI.

.PARAMETER AllowDirty
    Разрешить релиз при незакоммиченных изменениях (по умолчанию запрещено).

.EXAMPLE
    .\tools\Release.ps1
    Сухой прогон: версия, тег, локальная проверка сборки. Ничего не пушится.

.EXAMPLE
    .\tools\Release.ps1 -Push
    Полный релиз: проверка + создание и push тега -> GitHub Actions собирает и публикует.
#>
[CmdletBinding()]
param(
    [string]$Version = (Get-Date -Format "yy.M.d.HHmm"),
    [switch]$Push,
    [switch]$SkipLocalCheck,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$tag = "v$Version"

function Say([string]$msg, [string]$color = "Cyan") { Write-Host $msg -ForegroundColor $color }
function Step([string]$msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Yellow }

Say "FastMediaSorter LITE - RELEASE" "Green"
Say "Version : $Version"
Say "Tag     : $tag"
Say ("Mode    : " + $(if ($Push) { "PUSH (запустит платный GitHub Actions)" } else { "DRY-RUN (ничего не пушится)" })) `
    $(if ($Push) { "Red" } else { "Gray" })

# --- Pre-flight: git состояние ---------------------------------------------
Step "Проверка git"

Push-Location $repoRoot
try {
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    Say "Ветка: $branch"
    if ($branch -ne "main") {
        Write-Warning "Релиз обычно режется с 'main'. Текущая ветка: $branch."
    }

    $dirty = (git status --porcelain)
    if ($dirty) {
        if ($AllowDirty) {
            Write-Warning "Есть незакоммиченные изменения (разрешено через -AllowDirty)."
        } else {
            Write-Error "Рабочее дерево не чистое. Закоммить/отложи изменения или передай -AllowDirty.`n$dirty"
        }
    } else {
        Say "Рабочее дерево чистое."
    }

    # Тег не должен уже существовать
    $existing = (git tag --list $tag)
    if ($existing) {
        Write-Error "Тег $tag уже существует. Выбери другую версию (-Version) или удали тег."
    }
} finally {
    Pop-Location
}

# --- Локальная контрольная сборка (бесплатно, ловит падение CI заранее) -----
if ($SkipLocalCheck) {
    Write-Warning "Локальная контрольная сборка пропущена (-SkipLocalCheck). CI может упасть платно."
} else {
    Step "Локальная контрольная сборка (как на CI) - чтобы не платить за упавший GitHub-job"
    & (Join-Path $PSScriptRoot "Build-OfflineRelease.ps1") -Version $Version
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Локальная сборка упала. Релиз остановлен ДО оплаты GitHub Actions."
    }
    Say "Локальная сборка успешна - CI почти наверняка тоже соберётся." "Green"
}

# --- Тег + push (единственная платная операция) -----------------------------
if (-not $Push) {
    Step "DRY-RUN завершён"
    Say "Тег НЕ создан, GitHub Actions НЕ запущен. Платных минут потрачено: 0." "Green"
    Say "Для реального релиза повтори с -Push:" "Gray"
    Say "    .\tools\Release.ps1 -Version $Version -Push" "Gray"
    return
}

Step "Создание и push тега $tag (запускает GitHub Actions)"
Push-Location $repoRoot
try {
    git tag -a $tag -m "FastMediaSorter LITE $Version"
    if ($LASTEXITCODE -ne 0) { Write-Error "Не удалось создать тег $tag." }

    git push origin $tag
    if ($LASTEXITCODE -ne 0) { Write-Error "Не удалось запушить тег $tag." }
} finally {
    Pop-Location
}

Say "Тег $tag запушен. GitHub Actions собирает релиз." "Green"

# --- Пост-релизный чек-лист (ручные шаги) -----------------------------------
Step "Дальше вручную (см. BUILD_AND_RELEASE.md)"
@"
1) GitHub: дождаться сборки и 4 ассетов (setup.exe + zip + два .sha256)
   https://github.com/SerZhyAle/FastMediaSorter_Lite/actions
2) winget: PR в microsoft/winget-pkgs для SerZhyAle.FastMediaSorter
   (Inno setup.exe напрямую, без зависимостей, без Scope) -> SPECIFICATION_WINGET_PUBLISHING.md
3) Microsoft Store (опц.): cd msix; .\build-msix.ps1 -IdentityName "<имя>" (БЕЗ -SelfSign),
   загрузить unsigned .msix в Partner Center -> STORE_PUBLISHING.md
"@ | Write-Host -ForegroundColor Cyan
