<#
.SYNOPSIS
    APP-COMMANDS 0.1 section 4, the live half of the conformance cases (ticket L0320): the built
    programs are driven through their command line only, never by simulated keys.

.DESCRIPTION
    The decision half of every case C1-C13 is a unit fact (Lite.Tests: AppCommandsConformanceTests,
    CliManifestStaticTests; Companion.Tests: ShareManagerCommandsConformanceTests). This script is
    what a fact cannot hold: two real processes, a real window, the exit code the shell sees, the
    reply file on disk and the process count afterwards.

    It runs the built bin\Release programs and touches the owner's profile in exactly three
    ways, each put back in a finally block: the viewer it starts is stopped again (only a process it
    started itself, by id, never one found by name); the registry value AllowAppCommands under
    HKCU\Software\VB and VBA Program Settings\SZA\FastMediaSorter is borrowed for S7 and restored
    (or removed when it was absent); and files go only under -WorkDir.

    Scenes (Viewer = FastMediaSorter_LITE.exe, Manager = FastMediaSorterCompanion.exe):
      S1  C1   "commands --json" of each program exits 0, is whole JSON that names its exe and carries a
               non-empty command list, and leaves no process of that name behind.
      S2  C2   "version" exits 0 and prints a version; "status" with nothing running exits 11.
      S3  C10  the cold-start modes with nothing running: oneshot leaves nothing behind (version,
               Manager "schedule list"), refuse is exit 11 (status).
      S4  C4   with a viewer running: an unknown verb and an unknown option exit 10, and a following
               "status" is still taken (exit 0).
      S5  C3   the first instance takes the verbs: five "status" calls leave exactly one viewer process.
      S6  C11  "status --result <file>" leaves one whole JSON file and no half-written sibling; a second
               run replaces it whole.
      S7  C8   with AllowAppCommands = 0 a reads-user-data verb ("file current") is exit 12 and "status"
               still works; with 1 it is not exit 12.
      S8  C13  a copy of the other build in front: the net48 x86 exe running, the mainline asks it with
               "status" and gets exit 2 (older copy); an x86 launch with a file while the mainline runs
               forwards and leaves (the process ends), the old transport unchanged.
      S9  C10  Manager only, with -WithManagerStart: "start --silent" becomes the tray manager,
               "status" is exit 0, "quit" is exit 0 and the process ends. Off by default: it starts the
               Share Manager on the owner's profile, which may restore an active share.

    C5 (a receiver that never answers), C6 (a modal window) and C9 (an elevated copy, another
    session) are manual rungs by the contract itself; their pure halves are unit facts.

    Without a built viewer, or with a viewer already running, the answer is COULD NOT VERIFY, never a pass.
    The last line is the verdict, `AppCommandScenes: <WORD> (..)` (contract CHECK-VERDICT).

.PARAMETER ViewerExe
    The mainline viewer. Default: bin\Release\FastMediaSorter_LITE.exe.
.PARAMETER ManagerExe
    The Share Manager. Default: bin\Release\FastMediaSorterCompanion.exe.
.PARAMETER FallbackExe
    The net48 x86 viewer for S8. Default: bin\Release\FastMediaSorter_x86.exe (S8 is COULD NOT VERIFY without it).
.PARAMETER WorkDir
    Where fixtures and result files go. Default: %TEMP%\fms-appcommand-scenes.
.PARAMETER StartTimeoutSeconds
    How long a started program may take to answer its first "status". Default 45.
.PARAMETER WithManagerStart
    Also run S9.
.PARAMETER Only
    Run only these scenes, for example -Only S1,S2.
#>
[CmdletBinding()]
param(
    [string]$ViewerExe = '',
    [string]$ManagerExe = '',
    [string]$FallbackExe = '',
    [string]$WorkDir = (Join-Path $env:TEMP 'fms-appcommand-scenes'),
    [int]$StartTimeoutSeconds = 45,
    [switch]$WithManagerStart,
    [string[]]$Only = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'tools\lib\Verdict.ps1')
$check = 'AppCommandScenes'
if (-not $ViewerExe)   { $ViewerExe   = Join-Path $repoRoot 'bin\Release\FastMediaSorter_LITE.exe' }
if (-not $ManagerExe)  { $ManagerExe  = Join-Path $repoRoot 'bin\Release\FastMediaSorterCompanion.exe' }
if (-not $FallbackExe) { $FallbackExe = Join-Path $repoRoot 'bin\Release\FastMediaSorter_x86.exe' }
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
function Want([string]$Id) { return ($Only.Count -eq 0) -or ($Only -contains $Id) }

trap {
    Write-Host "  $($_.Exception.Message)"
    Exit-Verdict $check 2 'the harness itself failed'
}

$scenes = New-Object System.Collections.Generic.List[object]
function Add-Scene([string]$Id, [string]$Result, [string]$Reason) {
    $scenes.Add([pscustomobject]@{ Id = $Id; Result = $Result; Reason = $Reason })
}

# --- subject and refusals ---------------------------------------------------------------------
Write-Subject $check "$ViewerExe and $ManagerExe, evidence in $WorkDir"
if (-not (Test-Path -LiteralPath $ViewerExe)) { Exit-Verdict $check 2 "no viewer at $ViewerExe - build one first (see -ViewerExe)" }
if (-not (Test-Path -LiteralPath $ManagerExe)) { Exit-Verdict $check 2 "no Share Manager at $ManagerExe - build one first (see -ManagerExe)" }
if (-not [Environment]::UserInteractive) { Exit-Verdict $check 2 'no interactive desktop (a service session)' }
if (Get-Process -Name FastMediaSorter_LITE, FastMediaSorter_x86, FastMediaSorterCompanion -ErrorAction SilentlyContinue) {
    Exit-Verdict $check 2 'a viewer or a Share Manager is already running - stop it first, or its answer is not the cold-start answer'
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# --- helpers ----------------------------------------------------------------------------------
$started = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

# Runs one command line to its end and returns its exit code, both streams and the wall time. A program
# that does not end inside the limit is killed and reported with a null code.
function Invoke-Cli([string]$Exe, [string[]]$CliArgs, [int]$LimitSeconds = 30) {
    $outFile = Join-Path $WorkDir ('out-' + [guid]::NewGuid().ToString('N') + '.txt')
    $errFile = Join-Path $WorkDir ('err-' + [guid]::NewGuid().ToString('N') + '.txt')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Exe -ArgumentList $CliArgs -PassThru -NoNewWindow -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $null = $p.Handle   # keeps the exit code readable after the process ends
    $code = $null
    if ($p.WaitForExit($LimitSeconds * 1000)) { $p.WaitForExit(); $code = $p.ExitCode } else { try { $p.Kill() } catch { } }
    $text = if (Test-Path -LiteralPath $outFile) { Get-Content -LiteralPath $outFile -Raw } else { '' }
    $err = if (Test-Path -LiteralPath $errFile) { Get-Content -LiteralPath $errFile -Raw } else { '' }
    return [pscustomobject]@{ Code = $code; Out = [string]$text; Err = [string]$err; Ms = [int]$watch.ElapsedMilliseconds }
}

function Count-Processes([string]$Name) { return @(Get-Process -Name $Name -ErrorAction SilentlyContinue).Count }

# Starts a program as the first instance (a "start" verb makes the calling process the program) and waits
# until it answers "status". Returns the process, or $null with the reason in $script:startNote.
$script:startNote = ''
function Start-First([string]$Exe, [string[]]$CliArgs, [string[]]$ReadyArgs) {
    $p = Start-Process -FilePath $Exe -ArgumentList $CliArgs -PassThru
    $started.Add($p)
    $deadline = [DateTime]::UtcNow.AddSeconds($StartTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($p.WaitForExit(500)) { $script:startNote = "the program ended with code $($p.ExitCode) before it answered"; return $null }
        $r = Invoke-Cli $Exe $ReadyArgs 5
        if ($r.Code -eq 0) { return $p }
    }
    $script:startNote = "no answer to status within $StartTimeoutSeconds s"
    return $null
}

function Stop-Started {
    foreach ($p in $started) {
        try { if (-not $p.HasExited) { $p.Kill() } } catch { }
    }
    foreach ($p in $started) { try { $null = $p.WaitForExit(5000) } catch { } }
    $started.Clear()
}

function Test-WholeJson([string]$Text) {
    try { $null = $Text | ConvertFrom-Json -ErrorAction Stop; return $true } catch { return $false }
}

# A scene body returns an array of failure strings (empty = pass) or throws a string that starts with
# "CNV:" for COULD NOT VERIFY.
function Run-Scene([string]$Id, [scriptblock]$Body) {
    if (-not (Want $Id)) { return }
    try {
        $why = @(& $Body)
        if ($why.Count -eq 0) { Add-Scene $Id 'PASS' '' } else { Add-Scene $Id 'FAIL' ($why -join '; ') }
    } catch {
        $m = $_.Exception.Message
        if ($m.StartsWith('CNV:')) { Add-Scene $Id 'COULD NOT VERIFY' $m.Substring(4).Trim() }
        else { Add-Scene $Id 'COULD NOT VERIFY' "the scene failed to run: $m" }
    }
}

# --- registry value borrowed by S7, put back in finally ---------------------------------------
$regPath = 'HKCU:\Software\VB and VBA Program Settings\SZA\FastMediaSorter'
$regName = 'AllowAppCommands'
$priorItem = Get-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue
$hadValue = $null -ne $priorItem
$priorValue = if ($hadValue) { [string]$priorItem.$regName } else { '' }
function Set-Allow([string]$Value) {
    if (-not (Test-Path -Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    Set-ItemProperty -Path $regPath -Name $regName -Value $Value -Type String
}

try {
    # S1 - C1 ------------------------------------------------------------------------------
    Run-Scene 'S1' {
        $why = @()
        foreach ($pair in @(@($ViewerExe, 'FastMediaSorter_LITE.exe', 'FastMediaSorter_LITE'), @($ManagerExe, 'FastMediaSorterCompanion.exe', 'FastMediaSorterCompanion'))) {
            $exe = $pair[0]; $exeName = $pair[1]; $proc = $pair[2]
            $r = Invoke-Cli $exe @('commands', '--json')
            if ($r.Code -ne 0) { $why += "$exeName commands --json exit $($r.Code)"; continue }
            if (-not (Test-WholeJson $r.Out)) { $why += "$exeName commands --json is not whole JSON"; continue }
            $doc = $r.Out | ConvertFrom-Json
            if ($doc.exe -ne $exeName) { $why += "$exeName manifest names exe '$($doc.exe)'" }
            if (@($doc.commands).Count -lt 1) { $why += "$exeName manifest lists no command" }
            if ((Count-Processes $proc) -ne 0) { $why += "$exeName left a process behind" }
            $plain = Invoke-Cli $exe @('commands')
            if ($plain.Code -ne 0 -or -not $plain.Out.Trim()) { $why += "$exeName commands (readable) exit $($plain.Code) or empty" }
        }
        $why
    }

    # S2 - C2 ------------------------------------------------------------------------------
    Run-Scene 'S2' {
        $why = @()
        foreach ($exe in @($ViewerExe, $ManagerExe)) {
            $name = Split-Path -Leaf $exe
            $v = Invoke-Cli $exe @('version')
            if ($v.Code -ne 0 -or $v.Out -notmatch '\d+\.\d+\.\d+\.\d+') { $why += "$name version exit $($v.Code), output '$($v.Out.Trim())'" }
            $s = Invoke-Cli $exe @('status')
            if ($s.Code -ne 11) { $why += "$name status with nothing running exit $($s.Code), expected 11" }
        }
        $why
    }

    # S3 - C10 oneshot / refuse -------------------------------------------------------------
    Run-Scene 'S3' {
        $why = @()
        $r = Invoke-Cli $ViewerExe @('version')
        if ($r.Code -ne 0 -or (Count-Processes 'FastMediaSorter_LITE') -ne 0) { $why += 'viewer oneshot version: exit or a process left' }
        $l = Invoke-Cli $ManagerExe @('schedule', 'list')
        if ($l.Code -ne 0) { $why += "Manager schedule list with no manager exit $($l.Code), expected 0" }
        if ((Count-Processes 'FastMediaSorterCompanion') -ne 0) { $why += 'Manager oneshot schedule list left a process' }
        $s = Invoke-Cli $ManagerExe @('share', 'list')
        if ($s.Code -ne 11) { $why += "Manager share list (refuse) with no manager exit $($s.Code), expected 11" }
        $why
    }

    # S4/S5/S6/S7/S8 need a running viewer ----------------------------------------------------
    $needViewer = (Want 'S4') -or (Want 'S5') -or (Want 'S6') -or (Want 'S7') -or (Want 'S8')
    $viewer = $null
    if ($needViewer) {
        $viewer = Start-First $ViewerExe @('show') @('status')
        if ($null -eq $viewer) {
            foreach ($id in 'S4', 'S5', 'S6', 'S7', 'S8') { if (Want $id) { Add-Scene $id 'COULD NOT VERIFY' "the viewer did not come up: $script:startNote" } }
        }
    }
    if ($null -ne $viewer) {
        Run-Scene 'S4' {
            $why = @()
            $a = Invoke-Cli $ViewerExe @('frobnicate')
            if ($a.Code -ne 10) { $why += "unknown verb exit $($a.Code), expected 10" }
            $b = Invoke-Cli $ViewerExe @('status', '--frob')
            if ($b.Code -ne 10) { $why += "unknown option exit $($b.Code), expected 10" }
            $c = Invoke-Cli $ViewerExe @('status')
            if ($c.Code -ne 0) { $why += "status after two refusals exit $($c.Code), expected 0" }
            $why
        }
        Run-Scene 'S5' {
            $why = @()
            1..5 | ForEach-Object { $r = Invoke-Cli $ViewerExe @('status', '--json'); if ($r.Code -ne 0) { $why += "status $_ exit $($r.Code)" } }
            $n = Count-Processes 'FastMediaSorter_LITE'
            if ($n -ne 1) { $why += "$n viewer processes after five status calls, expected 1" }
            $j = Invoke-Cli $ViewerExe @('status', '--json')
            if (-not (Test-WholeJson $j.Out)) { $why += 'status --json is not whole JSON' }
            else { $d = $j.Out | ConvertFrom-Json; if ($d.exitCode -ne 0 -or $d.schemaVersion -lt 1) { $why += 'status --json answer is not a done response' } }
            $why
        }
        Run-Scene 'S6' {
            $why = @()
            $target = Join-Path $WorkDir 'status-result.json'
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
            $first = Invoke-Cli $ViewerExe @('status', '--result', "`"$target`"")
            if ($first.Code -ne 0 -or -not (Test-Path -LiteralPath $target)) { $why += "status --result exit $($first.Code), file present: $(Test-Path -LiteralPath $target)"; return $why }
            if (-not (Test-WholeJson (Get-Content -LiteralPath $target -Raw))) { $why += 'the result file is not whole JSON' }
            $second = Invoke-Cli $ViewerExe @('status', '--result', "`"$target`"")
            if ($second.Code -ne 0 -or -not (Test-WholeJson (Get-Content -LiteralPath $target -Raw))) { $why += 'a second run did not replace the result whole' }
            $siblings = @(Get-ChildItem -LiteralPath $WorkDir -File | Where-Object { $_.Name -like 'status-result*' -and $_.Name -ne 'status-result.json' })
            if ($siblings.Count -gt 0) { $why += "half-written sibling left: $($siblings[0].Name)" }
            $why
        }
        Run-Scene 'S7' {
            $why = @()
            Set-Allow '0'
            $off = Invoke-Cli $ViewerExe @('file', 'current')
            if ($off.Code -ne 12) { $why += "file current with the setting off exit $($off.Code), expected 12" }
            $safe = Invoke-Cli $ViewerExe @('status')
            if ($safe.Code -ne 0) { $why += "status with the setting off exit $($safe.Code), expected 0" }
            Set-Allow '1'
            $on = Invoke-Cli $ViewerExe @('file', 'current')
            if ($on.Code -eq 12) { $why += 'file current with the setting on is still exit 12' }
            $why
        }
        Run-Scene 'S8' {
            $why = @()
            if (-not (Test-Path -LiteralPath $FallbackExe)) { throw "CNV: no net48 x86 viewer at $FallbackExe (see -FallbackExe)" }
            # (1) the mainline running, an x86 launch with a file forwards and leaves: the old transport, unchanged.
            $png = Join-Path $WorkDir 'fixture.png'
            [IO.File]::WriteAllBytes($png, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='))
            $x = Start-Process -FilePath $FallbackExe -ArgumentList @("`"$png`"") -PassThru
            $started.Add($x)
            if (-not $x.WaitForExit(20000)) { $why += 'the x86 launch with a file did not forward and leave inside 20 s' }
            # (2) the x86 running, the mainline asks it: exit 2 (older copy), never a hang and never a second window.
            $stop = Invoke-Cli $ViewerExe @('quit')
            if (-not $viewer.WaitForExit(15000)) { $why += "quit did not end the mainline viewer (exit $($stop.Code))"; return $why }
            # the x86 exe does not answer commands, so its readiness is its own window, not a status answer
            $y = Start-Process -FilePath $FallbackExe -PassThru
            $started.Add($y)
            $deadline = [DateTime]::UtcNow.AddSeconds($StartTimeoutSeconds)
            $shown = $false
            while ([DateTime]::UtcNow -lt $deadline) {
                if ($y.WaitForExit(500)) { throw "CNV: the x86 viewer ended with code $($y.ExitCode) instead of staying as the first instance" }
                $y.Refresh()
                if ($y.MainWindowHandle -ne [IntPtr]::Zero) { $shown = $true; break }
            }
            if (-not $shown) { throw "CNV: the x86 viewer showed no window within $StartTimeoutSeconds s" }
            $ask = Invoke-Cli $ViewerExe @('status') 10
            if ($ask.Code -ne 2) { $why += "mainline status in front of the x86 copy exit $($ask.Code), expected 2" }
            if (-not ($ask.Out + $ask.Err).Trim()) { $why += 'the older-copy answer printed no sentence' }
            if ((Count-Processes 'FastMediaSorter_LITE') -ne 0) { $why += 'a mainline process stayed behind the older-copy answer' }
            $why
        }
    }

    # S9 - C10 start, Manager ----------------------------------------------------------------
    if ($WithManagerStart) {
        Run-Scene 'S9' {
            $why = @()
            Stop-Started
            $m = Start-First $ManagerExe @('start', '--silent') @('status')
            if ($null -eq $m) { throw "CNV: the Share Manager did not come up: $script:startNote" }
            $s = Invoke-Cli $ManagerExe @('status')
            if ($s.Code -ne 0) { $why += "Manager status exit $($s.Code), expected 0" }
            $q = Invoke-Cli $ManagerExe @('quit')
            if ($q.Code -ne 0) { $why += "Manager quit exit $($q.Code), expected 0" }
            if (-not $m.WaitForExit(20000)) { $why += 'the Share Manager did not end after quit' }
            $why
        }
    }
} finally {
    Stop-Started
    if ($hadValue) { Set-ItemProperty -Path $regPath -Name $regName -Value $priorValue -Type String }
    elseif (Test-Path -Path $regPath) { Remove-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue }
}

# --- verdict ----------------------------------------------------------------------------------
$scenes = @($scenes | Sort-Object { [int]$_.Id.Substring(1) })
Write-Host ''
foreach ($s in $scenes) { Write-Host ("{0,-3} {1,-16} {2}" -f $s.Id, $s.Result, $s.Reason) }
$failed = @($scenes | Where-Object { $_.Result -eq 'FAIL' })
$unknown = @($scenes | Where-Object { $_.Result -eq 'COULD NOT VERIFY' })
$passed = @($scenes | Where-Object { $_.Result -eq 'PASS' })
if ($scenes.Count -eq 0) { Exit-Verdict $check 2 'no scene ran' }
if ($failed.Count) { Exit-Verdict $check 1 "$($failed.Count) failed ($(($failed | ForEach-Object Id) -join ', ')), $($passed.Count) passed" }
if ($unknown.Count) { Exit-Verdict $check 2 "$($unknown.Count) not verified ($(($unknown | ForEach-Object Id) -join ', ')), $($passed.Count) passed" }
Exit-Verdict $check 0 "$($passed.Count) scenes passed"
