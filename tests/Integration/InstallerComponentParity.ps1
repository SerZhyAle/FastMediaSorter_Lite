<#
.SYNOPSIS
    Fixture proof of L0307: the User (viewer-first) and the Server (server-first) installers make the same
    eligible set of capabilities and matching offline payloads available, and neither is ever packaged
    with a payload it claims but lacks.

.DESCRIPTION
    Three tiers, each a different kind of evidence:

    1. Mapping. Both .iss files are held to tools\installer-components.json: every capability's
       component exists in the setup that must offer it, its Source line names the right stage path and
       Components clause, and no payload line may be skipped when its source is missing.
    2. Staging. Throw-away User stages (built under %TEMP% with real version-stamped executables, so the
       version rule is exercised on a version resource, not on a string) go through the real
       New-InstallerPayloadManifest and tools\Stage-ServerViewerPayload.ps1 in every OCR mode and with
       the exchange gate shut and open. The positive cases inspect what lands under viewer\ and
       viewer-support\ file by file (and that the viewer staging never carries the share side or the
       exchange tree); the negative cases each break one thing - no manifest, a stale executable, a
       missing libvlc / PDFium / models, an option the stage contradicts, a file changed, removed or
       added after the record - and expect the packaging to stop with its own message.
    3. Compilation. Both setups are compiled by the real ISCC.exe from those stages in each mode; the
       compiler's own list of the files it embedded is compared per capability between the two setups,
       and a setup whose payload is missing must fail to compile. (An actual installation, upgrade and
       coexistence run needs a disposable machine and is not part of this check.)

    Nothing in the repository is written; ISCC output goes under %TEMP%. Tier 3 takes tens of seconds:
    this is a release-class check (tools\check-placement.jsonl). -SkipCompile runs tiers 1 and 2 only and
    ends in COULD NOT VERIFY, never in PASS, because the setups were then not inspected.

    Ends in the shared one-line verdict (CHECK-VERDICT).
#>
[CmdletBinding()]
param([switch]$SkipCompile)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $RepoRoot 'tools\lib\Verdict.ps1')
. (Join-Path $RepoRoot 'tools\lib\InstallerPayloadSelection.ps1')
trap { Write-Host "  $($_.Exception.Message)"; Exit-Verdict 'InstallerComponentParity' 2 'the check could not run' }
Write-Subject 'InstallerComponentParity' 'both installer scripts against tools\installer-components.json, throw-away User stages, the real staging scripts and ISCC.exe'

$Version = '26.11.13.0018'
$OldVersion = '26.11.12.2359'
$installerDir = Join-Path $RepoRoot 'publishing\installer'
$userIss = Join-Path $installerDir 'FastMediaSorter.iss'
$serverIss = Join-Path $installerDir 'FastMediaSorterServer.iss'
$stageViewer = Join-Path $RepoRoot 'tools\Stage-ServerViewerPayload.ps1'
$stageShare = Join-Path $RepoRoot 'tools\Stage-ServerSharePayload.ps1'
$stageExchange = Join-Path $RepoRoot 'tools\Stage-ExchangePayload.ps1'
$map = Get-InstallerComponentMap

$failures = [Collections.Generic.List[string]]::new()
function Pass([string]$name) { Write-Host "  ok    $name" -ForegroundColor Green }
function Fail([string]$name, [string]$why) { Write-Host "  WRONG $name - $why" -ForegroundColor Red; $failures.Add($name) }
function Expect-True([string]$name, [bool]$condition, [string]$why) { if ($condition) { Pass $name } else { Fail $name $why } }
function Expect-Ok([string]$name, [scriptblock]$action) {
    try { & $action | Out-Null; Pass $name } catch { Fail $name "unexpected error: $($_.Exception.Message)" }
}
function Expect-Throw([string]$name, [string]$message, [scriptblock]$action) {
    try { & $action | Out-Null; Fail $name 'it did not stop' }
    catch {
        if ($_.Exception.Message.Contains($message)) { Pass $name }
        else { Fail $name "stopped for another reason: $($_.Exception.Message)" }
    }
}

# --- tier 1: the mapping against both scripts --------------------------------------------------
Write-Host 'Tier 1 - the mapping'
# A fresh Windows clone (core.autocrlf=true) holds the scripts in CRLF, and a (?m) '$' matches only before a
# bare LF, so every '[^\r\n]*$' below would miss every line there: read them as LF.
$userText = [IO.File]::ReadAllText($userIss, [Text.Encoding]::UTF8).Replace("`r`n", "`n")
$serverText = [IO.File]::ReadAllText($serverIss, [Text.Encoding]::UTF8).Replace("`r`n", "`n")

function Get-SourceLines([string]$text, [string]$stagePath, [string]$prefix) {
    $esc = [regex]::Escape($stagePath)
    $pre = if ($prefix) { [regex]::Escape($prefix) + '\\' } else { '' }
    @([regex]::Matches($text, '(?m)^Source: "\{#SourceDir\}\\' + $pre + $esc + '(\\\*)?";[^\r\n]*$') | ForEach-Object { $_.Value })
}
function Test-ComponentDeclared([string]$text, [string]$component) {
    [regex]::IsMatch($text, '(?m)^Name: "' + [regex]::Escape($component) + '";')
}

foreach ($capability in $map.capabilities) {
    $label = $capability.id
    foreach ($component in ([string]$capability.user.component -split ' ')) {
        Expect-True "User setup declares '$component' ($label)" (Test-ComponentDeclared $userText $component) "no Name: ""$component"" line"
    }
    Expect-True "Server setup declares '$($capability.server.component)' ($label)" (Test-ComponentDeclared $serverText ([string]$capability.server.component)) 'no such Name line'

    foreach ($stagePath in @($capability.stage | Where-Object { $_ })) {
        $userComponents = [string]$capability.user.component
        $lines = @(Get-SourceLines $userText $stagePath '')
        $withComponent = @($lines | Where-Object { $_ -match ('Components: ' + [regex]::Escape($userComponents) + '(;|\s*$)') })
        Expect-True "User setup installs $stagePath through '$userComponents'" ($withComponent.Count -ge 1) "no Source line for $stagePath with Components: $userComponents"
        Expect-True "User setup never skips a missing $stagePath" (@($lines | Where-Object { $_ -match 'skipifsourcedoesntexist' }).Count -eq 0) 'the Source line carries skipifsourcedoesntexist'

        $serverComponent = [string]$capability.server.component
        $serverRoot = [string]$capability.server.root
        if ($serverRoot -eq [string]$map.serverSupportRoot -or $serverComponent -in @('filedo', 'exchange')) {
            $prefix = if ($serverRoot) { $serverRoot } else { '' }
            $lines = @(Get-SourceLines $serverText $stagePath $prefix)
            $withComponent = @($lines | Where-Object { $_ -match ('Components: ' + [regex]::Escape($serverComponent) + '(;|\s*$)') })
            Expect-True "Server setup installs $stagePath through '$serverComponent'" ($withComponent.Count -ge 1) "no Source line for $stagePath with Components: $serverComponent"
            Expect-True "Server setup never skips a missing $stagePath" (@($lines | Where-Object { $_ -match 'skipifsourcedoesntexist' }).Count -eq 0) 'the Source line carries skipifsourcedoesntexist'
        }
    }
}
Expect-True 'Server setup installs the viewer tree through the unticked viewer item' ([regex]::IsMatch($serverText, '(?m)^Source: "\{#SourceDir\}\\viewer\\\*";[^\r\n]*Components: viewer\s*$')) 'no Source line for viewer\*'
$supportItems = @($map.capabilities | Where-Object { [string]$_.server.root -eq [string]$map.serverSupportRoot } | ForEach-Object { [string]$_.server.component } | Sort-Object -Unique)
$expectedRows = $supportItems.Count
$expectedRowsNoOcr = @($supportItems | Where-Object { $_ -ne 'viewer\ocr' }).Count
Expect-True "Server wizard disables $expectedRows support rows with the viewer (2 without OCR models)" ([regex]::IsMatch($serverText, "(?s)#ifdef NoOcrModels\s*#define ViewerSupportRows $expectedRowsNoOcr\s*#else\s*#define ViewerSupportRows $expectedRows\s*#endif")) 'ViewerSupportRows does not match the component map'
foreach ($item in $supportItems) {
    $declared = [regex]::Match($serverText, '(?m)^Name: "' + [regex]::Escape($item) + '";[^\r\n]*$').Value
    Expect-True "Server item '$item' has no Types (starts unticked)" ($declared -and $declared -notmatch 'Types:') 'it has a Types clause, so it would start ticked'
}
foreach ($item in @('codecs', 'pdf', 'ocr')) {
    $declared = [regex]::Match($userText, '(?m)^Name: "' + $item + '";[^\r\n]*$').Value
    Expect-True "User item '$item' keeps its default (ticked in full)" ($declared -match 'Types: full') 'its Types clause changed'
}

# --- tier 2: staging -------------------------------------------------------------------------
Write-Host 'Tier 2 - staging'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { Exit-Verdict 'InstallerComponentParity' 2 'the .NET Framework C# compiler (csc.exe) was not found' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('fms-installer-parity-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$rng = [Random]::new(307)

function New-Exe([string]$name, [string]$version) {
    $src = Join-Path $work "$name.cs"
    [IO.File]::WriteAllText($src, "[assembly: System.Reflection.AssemblyFileVersion(`"$version`")]`n[assembly: System.Reflection.AssemblyVersion(`"$version`")]`nclass P { static void Main() { } }`n")
    $exe = Join-Path $work "$name.exe"
    $out = & $csc /nologo /target:exe "/out:$exe" $src 2>&1
    if ($LASTEXITCODE -ne 0) { throw "csc failed for ${name}: $out" }
    $exe
}
function Write-Blob([string]$path, [int]$bytes = 2048) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    $b = New-Object byte[] $bytes
    $rng.NextBytes($b)
    [IO.File]::WriteAllBytes($path, $b)
}

# A User stage shaped like the real one: every payload the options ask for, nothing the options leave out.
# -Omit removes relative paths afterwards (a payload that went missing); -Extra adds one file.
function New-FixtureStage {
    param([Parameter(Mandatory)][string]$Name, [string]$OcrModels = 'fast', [bool]$Exchange = $false,
          [string]$Exe = $script:goodExe, [string[]]$Omit = @(), [string[]]$Extra = @())
    $stage = Join-Path $work "stages\$Name\FastMediaSorter-$Version-windows-x64"
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($n in 'FastMediaSorter_LITE.exe', 'FastMediaSorter_x86.exe', 'FastMediaSorterCompanion.exe') { Copy-Item -LiteralPath $Exe -Destination (Join-Path $stage $n) }
    foreach ($rel in 'Magick.Native.dll', 'x86\libwebpdecoder.dll', 'webview2\win-x64\WebView2Loader.dll', 'companion\fms-share-worker.exe',
                     'libvlc\win-x64\libvlc.dll', 'libvlc\win-x64\libvlccore.dll', 'libvlc\win-x64\plugins\plugin.dll', 'pdfium.dll') {
        Write-Blob (Join-Path $stage $rel)
    }
    [IO.File]::WriteAllText((Join-Path $stage 'LICENSE'), "fixture licence`n")
    Write-Blob (Join-Path $stage 'filedo\filedo.exe') 4096
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage 'filedo\filedo.exe') -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $stage 'filedo\filedo.sha256'), "$hash  filedo.exe`n")
    [IO.File]::WriteAllText((Join-Path $stage 'filedo\release.json'), '{"fixture":true}')
    if ($OcrModels -ne 'none') { Write-Blob (Join-Path $stage 'tessdata\eng.traineddata') }
    if ($OcrModels -eq 'best') { Write-Blob (Join-Path $stage 'tessdata-best\eng.traineddata') }
    if ($Exchange) {
        Write-Blob (Join-Path $stage 'exchange\fms-exchange-server.exe') 4096
        $xh = (Get-FileHash -LiteralPath (Join-Path $stage 'exchange\fms-exchange-server.exe') -Algorithm SHA256).Hash
        [IO.File]::WriteAllText((Join-Path $stage 'exchange\fms-exchange-server.exe.sha256'), "$xh  fms-exchange-server.exe`n")
    }
    foreach ($rel in $Omit) { Remove-Item -LiteralPath (Join-Path $stage $rel) -Recurse -Force }
    foreach ($rel in $Extra) { Write-Blob (Join-Path $stage $rel) }
    $stage
}
function Record-Stage([string]$Stage, [string]$OcrModels = 'fast', [bool]$Exchange = $false, [string]$StageVersion = $Version) {
    New-InstallerPayloadManifest -Stage $Stage -Version $StageVersion -OcrModels $OcrModels -Exchange $Exchange 6>$null
}
function Invoke-ViewerStaging([string]$Stage, [string]$Destination, [string]$StageVersion = $Version) {
    & $stageViewer -UserStage $Stage -Destination $Destination -Version $StageVersion 6>$null
}
function Get-Relative([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    @(Get-InstallerStageRelativePaths -Stage $Root)
}
function Test-Under([string[]]$Files, [string]$Prefix) { @($Files | Where-Object { $_ -eq $Prefix -or $_.StartsWith($Prefix + '\') }).Count -gt 0 }

try {
    $script:goodExe = New-Exe 'good' $((ConvertTo-InstallerVersionNumbers $Version))
    $staleExe = New-Exe 'stale' $((ConvertTo-InstallerVersionNumbers $OldVersion))

    # One positive run per OCR mode and gate state: what lands where.
    $modes = @(
        @{ Name = 'fast';  Ocr = 'fast'; Exchange = $false },
        @{ Name = 'full';  Ocr = 'best'; Exchange = $true },
        @{ Name = 'none';  Ocr = 'none'; Exchange = $false }
    )
    $staged = @{}
    foreach ($mode in $modes) {
        $stage = New-FixtureStage -Name $mode.Name -OcrModels $mode.Ocr -Exchange $mode.Exchange
        $manifest = Record-Stage $stage $mode.Ocr $mode.Exchange
        $dest = Join-Path $work "server\$($mode.Name)"
        Invoke-ViewerStaging $stage $dest
        $viewer = Get-Relative (Join-Path $dest 'viewer')
        $support = Get-Relative (Join-Path $dest 'viewer-support')
        $all = Get-Relative $dest
        $staged[$mode.Name] = @{ Stage = $stage; Manifest = $manifest; Dest = $dest; Mode = $mode }
        $n = "[$($mode.Name): OCR '$($mode.Ocr)', exchange gate $(if ($mode.Exchange) { 'open' } else { 'shut' })]"

        Expect-True "$n viewer tree holds both executables and the libraries they load" ((@('FastMediaSorter_LITE.exe', 'FastMediaSorter_x86.exe', 'Magick.Native.dll', 'x86\libwebpdecoder.dll', 'LICENSE') | Where-Object { $_ -notin $viewer }).Count -eq 0) 'a viewer file did not arrive under viewer\'
        Expect-True "$n viewer tree carries no support payload and nothing of the share side" (-not ((Test-Under $viewer 'libvlc') -or (Test-Under $viewer 'tessdata') -or (Test-Under $viewer 'tessdata-best') -or ('pdfium.dll' -in $viewer) -or (Test-Under $viewer 'companion') -or (Test-Under $viewer 'filedo') -or (Test-Under $viewer 'exchange') -or ('FastMediaSorterCompanion.exe' -in $viewer))) 'viewer\ holds a file that belongs elsewhere'
        Expect-True "$n codecs and PDFium are offered" (('libvlc\win-x64\libvlc.dll' -in $support) -and ('libvlc\win-x64\libvlccore.dll' -in $support) -and ('libvlc\win-x64\plugins\plugin.dll' -in $support) -and ('pdfium.dll' -in $support)) 'viewer-support\ lacks libvlc or pdfium.dll'
        Expect-True "$n the fast OCR models are offered exactly when the options carry them" ((Test-Under $support 'tessdata') -eq ($mode.Ocr -ne 'none')) 'viewer-support\tessdata does not follow the OCR option'
        Expect-True "$n the best OCR models are offered exactly in the best mode" ((Test-Under $support 'tessdata-best') -eq ($mode.Ocr -eq 'best')) 'viewer-support\tessdata-best does not follow the OCR option'
        Expect-True "$n the viewer staging never carries the share side or the exchange tree" (-not ((Test-Under $all 'exchange') -or (Test-Under $all 'companion') -or (Test-Under $all 'filedo') -or ('FastMediaSorterCompanion.exe' -in $all))) 'the Server stage holds share or exchange files after the viewer staging'
        $defines = [string[]](Get-InstallerIsccDefines -Manifest $manifest)
        $wanted = @(); if (-not $mode.Exchange) { $wanted += '/DNoExchange' }; if ($mode.Ocr -eq 'none') { $wanted += '/DNoOcrModels' }; if ($mode.Ocr -eq 'best') { $wanted += '/DOcrBest' }
        Expect-True "$n both setups take the defines '$($wanted -join ' ')' from the one record" (($defines -join ' ') -eq ($wanted -join ' ')) "got '$($defines -join ' ')'"
    }

    # The record is bound to its stage: every way it can be wrong stops the packaging, with its reason.
    $s = New-FixtureStage -Name 'nomanifest'
    Expect-Throw 'a stage with no manifest is refused' 'has no payload manifest' { Invoke-ViewerStaging $s (Join-Path $work 'server\nomanifest') }

    $s = $staged['fast'].Stage
    Expect-Throw 'a stage staged for another version is refused' 'version 26.11.13.18, expected 26.11.14.1' { Invoke-ViewerStaging $s (Join-Path $work 'server\wrongversion') '26.11.14.0001' }

    $s = New-FixtureStage -Name 'staleexe' -Exe $staleExe
    Expect-Throw 'a stage whose executables are of the previous build cannot be recorded' 'expected 26.11.13.18' { Record-Stage $s }

    foreach ($case in @(
        @{ Name = 'libvlc';        Omit = 'libvlc';        Ocr = 'fast'; Text = 'no libvlc' },
        @{ Name = 'pdfium';        Omit = 'pdfium.dll';    Ocr = 'fast'; Text = 'no pdfium.dll' },
        @{ Name = 'tessdata';      Omit = 'tessdata';      Ocr = 'fast'; Text = 'no tessdata' },
        @{ Name = 'tessdata-best'; Omit = 'tessdata-best'; Ocr = 'best'; Text = 'no tessdata-best' },
        @{ Name = 'worker';        Omit = 'companion\fms-share-worker.exe'; Ocr = 'fast'; Text = 'companion\fms-share-worker.exe' },
        @{ Name = 'filedo';        Omit = 'filedo\release.json'; Ocr = 'fast'; Text = 'filedo\release.json' }
    )) {
        $s = New-FixtureStage -Name "missing-$($case.Name)" -OcrModels $case.Ocr -Omit @($case.Omit)
        Expect-Throw "a stage without $($case.Omit) cannot be recorded (the setups are never packaged without it)" $case.Text { Record-Stage $s $case.Ocr }
    }

    $s = New-FixtureStage -Name 'badfiledo'
    Add-Content -LiteralPath (Join-Path $s 'filedo\filedo.exe') -Value 'x'
    Expect-Throw 'a FileDO that does not match its checksum cannot be recorded' 'FileDO checksum is invalid' { Record-Stage $s }

    $s = New-FixtureStage -Name 'ocr-contradiction' -OcrModels 'fast'
    Expect-Throw 'models in a stage whose options say none are refused' 'not part of this package' { Record-Stage $s 'none' }
    $s = New-FixtureStage -Name 'exchange-contradiction' -OcrModels 'fast' -Exchange $true
    Expect-Throw 'an exchange tree in a stage whose gate is shut is refused' 'not part of this package' { Record-Stage $s 'fast' $false }
    $s = New-FixtureStage -Name 'exchange-missing' -OcrModels 'fast' -Exchange $false
    Expect-Throw 'an open gate without an exchange tree is refused' 'no exchange' { Record-Stage $s 'fast' $true }

    # After the record: changed, removed, added, or a record that claims more than the stage holds.
    $s = New-FixtureStage -Name 'tamper-changed'
    [void](Record-Stage $s)
    Add-Content -LiteralPath (Join-Path $s 'libvlc\win-x64\libvlc.dll') -Value 'x'
    Expect-Throw 'a payload file changed after the record stops the Server packaging' 'changed: libvlc\win-x64\libvlc.dll' { Invoke-ViewerStaging $s (Join-Path $work 'server\tamper-changed') }
    $s = New-FixtureStage -Name 'tamper-removed'
    [void](Record-Stage $s)
    Remove-Item -LiteralPath (Join-Path $s 'pdfium.dll')
    Expect-Throw 'a payload file removed after the record stops the Server packaging' 'missing: pdfium.dll' { Invoke-ViewerStaging $s (Join-Path $work 'server\tamper-removed') }
    $s = New-FixtureStage -Name 'tamper-added'
    [void](Record-Stage $s)
    Write-Blob (Join-Path $s 'tessdata-best\eng.traineddata')
    Expect-Throw 'a payload added after the record stops the Server packaging' 'unexpected: tessdata-best\eng.traineddata' { Invoke-ViewerStaging $s (Join-Path $work 'server\tamper-added') }
    $s = New-FixtureStage -Name 'claims-more'
    $manifestPath = Get-InstallerPayloadManifestPath -Stage $s
    [void](Record-Stage $s 'fast')
    [IO.File]::WriteAllText($manifestPath, ([IO.File]::ReadAllText($manifestPath).Replace('"ocrModels": "fast"', '"ocrModels": "best"')))
    Expect-Throw 'a record that claims the best models the stage does not hold stops the Server packaging' 'records no files' { Invoke-ViewerStaging $s (Join-Path $work 'server\claims-more') }

    $s = $staged['fast'].Stage
    $busy = Join-Path $work 'server\busy'
    New-Item -ItemType Directory -Force -Path (Join-Path $busy 'viewer') | Out-Null
    Expect-Throw 'staging into a Server stage that already has a viewer folder is refused' 'already exists' { Invoke-ViewerStaging $s $busy }

    # --- tier 3: the real compiler ------------------------------------------------------------
    $compiled = $false
    if (-not $SkipCompile) {
        Write-Host 'Tier 3 - ISCC.exe'
        $iscc = $null
        $candidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")
        $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($onPath) { $candidates = @($onPath.Source) + $candidates }
        foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) { $iscc = $c; break } }
        if (-not $iscc) { Fail 'Inno Setup is installed' 'ISCC.exe was not found: the setups could not be compiled' }
        else {
            $compiled = $true
            function Invoke-Iscc([string]$Script, [string]$Stage, [string[]]$Defines) {
                $out = Join-Path $work ('iscc-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
                New-Item -ItemType Directory -Force -Path $out | Out-Null
                $arguments = @("/DVersion=$Version", "/DSourceDir=$Stage", "/O$out", '/DFastCompression') + $Defines + @($Script)
                $lines = @(& $iscc @arguments 2>&1 | ForEach-Object { "$_" })
                $embedded = @($lines | Where-Object { $_ -match '^\s*Compressing: ' } | ForEach-Object { ($_ -replace '^\s*Compressing: ', '').Trim() })
                [pscustomobject]@{ Code = $LASTEXITCODE; Lines = $lines; Embedded = $embedded; OutDir = $out }
            }
            # What a setup embedded, per capability, from the compiler's own list of files.
            function Test-Embedded($Result, [string]$Stage, [string]$Relative) {
                $needle = (Join-Path $Stage $Relative).TrimEnd('\')
                @($Result.Embedded | Where-Object { $_.Equals($needle, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($needle + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
            }

            foreach ($mode in $modes) {
                $info = $staged[$mode.Name]
                $defines = [string[]](Get-InstallerIsccDefines -Manifest $info.Manifest)
                $n = "[$($mode.Name)]"

                # The Server stage exactly as tools\Build-ServerInstaller.ps1 assembles it.
                $serverStage = $info.Dest
                [IO.File]::WriteAllText((Join-Path $serverStage 'LICENSE'), "fixture licence`n")
                & $stageShare -UserStage $info.Stage -Destination $serverStage 6>$null
                if ($mode.Exchange) { & $stageExchange -Source (Join-Path $info.Stage 'exchange') -Destination $serverStage -Server 6>$null }

                $user = Invoke-Iscc $userIss $info.Stage $defines
                $server = Invoke-Iscc $serverIss $serverStage $defines
                foreach ($r in @(@{ Setup = 'User'; Result = $user; Stage = $info.Stage; Name = 'FastMediaSorter-%V-windows-x64-setup.exe' }, @{ Setup = 'Server'; Result = $server; Stage = $serverStage; Name = 'FastMediaSorter-%V-windows-x64-server-setup.exe' })) {
                    $exe = Join-Path $r.Result.OutDir ($r.Name.Replace('%V', $Version))
                    $ok = ($r.Result.Code -eq 0) -and (Test-Path -LiteralPath $exe -PathType Leaf) -and ((Get-Item -LiteralPath $exe).Length -gt 0)
                    if ($ok) { Pass "$n the $($r.Setup) setup compiles from the staged payload ($([Math]::Round((Get-Item -LiteralPath $exe).Length / 1KB)) KB)" }
                    else { Fail "$n the $($r.Setup) setup compiles from the staged payload" "ISCC exit $($r.Result.Code): $((@($r.Result.Lines | Select-Object -Last 4)) -join ' | ')" }
                }
                if ($user.Code -ne 0 -or $server.Code -ne 0) { continue }
                if ($user.Embedded.Count -eq 0) { Fail "$n the compiler's list of embedded files is readable" 'no "Compressing:" lines in the ISCC output'; continue }

                foreach ($capability in $map.capabilities) {
                    if (-not $capability.stage) { continue }
                    $expected = Test-InstallerCapabilityExpected -Capability $capability -OcrModels $mode.Ocr -Exchange $mode.Exchange
                    foreach ($stagePath in @($capability.stage)) {
                        $serverRel = if ([string]$capability.server.root) { Join-Path ([string]$capability.server.root) $stagePath } else { $stagePath }
                        $inUser = Test-Embedded $user $info.Stage $stagePath
                        $inServer = Test-Embedded $server $serverStage $serverRel
                        Expect-True "$n $($capability.label): $stagePath is embedded in the User setup exactly when the options carry it" ($inUser -eq $expected) "embedded=$inUser, expected=$expected"
                        Expect-True "$n $($capability.label): $stagePath is embedded in the Server setup exactly when the options carry it" ($inServer -eq $expected) "embedded=$inServer, expected=$expected"
                    }
                }
                Expect-True "$n the Server setup embeds the viewer" (Test-Embedded $server $serverStage 'viewer\FastMediaSorter_LITE.exe') 'viewer\FastMediaSorter_LITE.exe was not embedded'
                Expect-True "$n the User setup embeds the viewer" (Test-Embedded $user $info.Stage 'FastMediaSorter_LITE.exe') 'FastMediaSorter_LITE.exe was not embedded'
            }

            # A setup whose payload is missing must not compile (never a smaller, apparently complete installer).
            $fast = $staged['fast']
            $defines = [string[]](Get-InstallerIsccDefines -Manifest $fast.Manifest)
            $serverBroken = Join-Path $work 'server\broken'
            Copy-Item -LiteralPath $fast.Dest -Destination $serverBroken -Recurse
            Remove-Item -LiteralPath (Join-Path $serverBroken 'viewer-support\pdfium.dll')
            $r = Invoke-Iscc $serverIss $serverBroken $defines
            Expect-True 'the Server setup does not compile without the viewer PDF payload' (($r.Code -ne 0) -and (($r.Lines -join "`n") -match 'pdfium')) "ISCC exit $($r.Code)"
            $serverBroken2 = Join-Path $work 'server\broken2'
            Copy-Item -LiteralPath $fast.Dest -Destination $serverBroken2 -Recurse
            Remove-Item -LiteralPath (Join-Path $serverBroken2 'viewer-support\tessdata') -Recurse
            $r = Invoke-Iscc $serverIss $serverBroken2 $defines
            Expect-True 'the Server setup does not compile without the OCR models its options carry' ($r.Code -ne 0) "ISCC exit $($r.Code)"
            $userBroken = New-FixtureStage -Name 'user-broken'
            Remove-Item -LiteralPath (Join-Path $userBroken 'libvlc') -Recurse
            $r = Invoke-Iscc $userIss $userBroken $defines
            Expect-True 'the User setup does not compile without the codecs' ($r.Code -ne 0) "ISCC exit $($r.Code)"
        }
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Exit-Verdict 'InstallerComponentParity' 1 "$($failures.Count) case(s) wrong: $((@($failures | Select-Object -First 6)) -join '; ')" }
if ($SkipCompile) { Exit-Verdict 'InstallerComponentParity' 2 'the mapping and the staging hold; the setups were not compiled (-SkipCompile), so they were not inspected' }
if (-not $compiled) { Exit-Verdict 'InstallerComponentParity' 2 'the mapping and the staging hold; the setups could not be compiled' }
Exit-Verdict 'InstallerComponentParity' 0 'both setups offer the same capabilities and carry matching payloads in every OCR mode and gate state; every missing payload stops the packaging'
