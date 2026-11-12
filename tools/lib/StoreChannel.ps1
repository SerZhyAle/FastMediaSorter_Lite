# The Microsoft Store submission channel as the release flow uses it (L0305 Part 2). Dot-source it:
#     . (Join-Path $PSScriptRoot 'lib\StoreChannel.ps1')
#
# The one implementation of the read-only preflight is the canon's Store-Preflight.ps1 (the sza plugin,
# skill store-publish, WINDOWS-STORE 0.4 section D). Nothing in this file talks to msstore and nothing
# here can write to the Store: it finds that script, derives the four-part package version it compares
# against the live submission, runs it in its own process and reads its exit code - 0 the checks passed,
# 1 the channel refuses this run (a pending submission, a version not above the live one, a price that is
# not Free, an unusable identity file), 2 the channel is unavailable (CLI missing or unconfigured, token
# refused, recorded credential expired) and the Store step is manual.
#
# Where it is looked up, first hit wins:
#   1. $env:SZA_STORE_PREFLIGHT (the script itself, for a test or a local checkout; a value that names no
#      file means "not installed", never a fall-through to the plugin);
#   2. the installed plugin versions under the user's Claude plugin cache, newest first, compared as
#      versions (the same lookup as tools\harness.ps1).
#
# $env:SZA_STORE_CREDENTIAL_EXPIRY (an optional date, never a secret) is the owner's recorded expiry of the
# channel's credential; the preflight answers "manual" once it has passed (WINDOWS-STORE 0.4 section D, rule 21).

function Find-StorePreflight {
    if ($env:SZA_STORE_PREFLIGHT) {
        if (Test-Path -LiteralPath $env:SZA_STORE_PREFLIGHT) { return $env:SZA_STORE_PREFLIGHT }
        return $null
    }
    $home_ = if ($env:USERPROFILE) { $env:USERPROFILE } else { $env:HOME }
    if (-not $home_) { return $null }
    $cache = Join-Path $home_ '.claude/plugins/cache/sza-unified-rules/sza'
    if (-not (Test-Path -LiteralPath $cache)) { return $null }
    $versions = Get-ChildItem -LiteralPath $cache -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $v = $null
        $isVersion = [version]::TryParse($_.Name, [ref]$v)
        [pscustomobject]@{ Dir = $_.FullName; IsVersion = $isVersion; Version = $v }
    } | Sort-Object -Property @{ Expression = 'IsVersion'; Descending = $true }, @{ Expression = 'Version'; Descending = $true }
    foreach ($entry in $versions) {
        $candidate = Join-Path $entry.Dir 'tools/Store-Preflight.ps1'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

# The same remap as publishing\msix\build-msix.ps1 step 2 (the Store needs revision 0 and each part
# within 65535): YY.M.D.HHmm becomes YY.(M*100+D).HHmm.0.
function ConvertTo-StoreMsixVersion([string]$Version) {
    if ($Version -notmatch '^(\d{2})\.(\d{1,2})\.(\d{1,2})\.(\d{4})$') {
        throw "'$Version' is not a YY.M.D.HHmm version."
    }
    $yy = [int]$Matches[1]; $m = [int]$Matches[2]; $d = [int]$Matches[3]; $hhmm = [int]$Matches[4]
    return "$yy.$($m * 100 + $d).$hhmm.0"
}

# Runs the shared preflight for the version about to be tagged. Never throws: whatever goes wrong on the
# way is "manual", because the channel never fails a release for its own sake (rule 21). Returns
#   Mode      'ready' (exit 0), 'refused' (exit 1, with a reason) or 'manual' (the Store step is by hand)
#   Lines     the lines to show: the preflight's own sanitized lines, or one manual line
#   Detail    the last of them, for a verdict line
#   Evidence  one record without account identifiers, credentials or an upload address (rule 26)
function Invoke-StoreChannelCheck {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Version
    )
    $checkedAt = (Get-Date).ToString('o')
    $name = 'FastMediaSorter'
    $storeId = ''
    $msixVersion = ''
    $identityFile = Join-Path $RepoRoot 'publishing\msix\identity.json'
    $make = {
        param([string]$Mode, [string[]]$Lines, [string]$Detail)
        $evidence = [ordered]@{ checkedAt = $checkedAt; mode = $Mode; storeId = $storeId; msixVersion = $msixVersion; detail = $Detail }
        [pscustomobject]@{ Mode = $Mode; Lines = $Lines; Detail = $Detail; Evidence = $evidence }
    }
    try {
        $msixVersion = ConvertTo-StoreMsixVersion $Version
        if (Test-Path -LiteralPath $identityFile) {
            try {
                $identity = Get-Content -LiteralPath $identityFile -Raw | ConvertFrom-Json -ErrorAction Stop
                if ($identity.IdentityName -cmatch '^[A-Za-z][A-Za-z0-9 ._-]{0,79}$') { $name = [string]$identity.IdentityName }
                if ($identity.StoreId -cmatch '^[A-Za-z0-9]{12}$') { $storeId = [string]$identity.StoreId }
            } catch { }   # the preflight names the unreadable file itself
        }

        $preflight = Find-StorePreflight
        if (-not $preflight) {
            $line = "${name}: Store step is manual (the shared Store preflight is not installed)."
            return & $make 'manual' @($line) $line
        }

        $arguments = @('-ProductName', $name, '-PackageVersion', $msixVersion, '-IdentityFile', $identityFile)
        if ($env:SZA_STORE_CREDENTIAL_EXPIRY) { $arguments += @('-CredentialExpiry', $env:SZA_STORE_CREDENTIAL_EXPIRY) }
        $pwsh = (Get-Process -Id $PID).Path
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'   # a child's stderr line is output here, not an error
        try {
            $output = @(& $pwsh -NoProfile -File $preflight @arguments 2>&1 | ForEach-Object { "$_" })
            $code = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $prevEap
        }

        # Only the preflight's own lines cross this boundary: each starts with the product name or with
        # "Store preflight:". Anything else (an exception text, a stack) is not shown and not recorded.
        $known = @($output | Where-Object { $_ -like "${name}: *" -or $_ -like 'Store preflight: *' } |
            ForEach-Object { if ($_.Length -gt 200) { $_.Substring(0, 200) } else { $_ } })
        if ($known.Count -eq 0 -or $code -notin 0, 1, 2) {
            $line = "${name}: Store step is manual (the Store preflight gave no readable answer)."
            return & $make 'manual' @($line) $line
        }
        $detail = $known[-1]
        switch ($code) {
            0 { return & $make 'ready' $known $detail }
            1 { return & $make 'refused' $known $detail }
            default { return & $make 'manual' $known $detail }
        }
    } catch {
        $line = "${name}: Store step is manual (the Store preflight could not run)."
        return & $make 'manual' @($line) $line
    }
}

# One line per release in the release record, appended after the tag is pushed. The record holds the
# release tag, the Store ID, the package version and the preflight's sanitized answer (rule 26).
function Add-StoreChannelEvidence([string]$Path, [string]$Tag, $Evidence) {
    $record = [ordered]@{ tag = $Tag }
    foreach ($key in $Evidence.Keys) { $record[$key] = $Evidence[$key] }
    $line = ConvertTo-Json $record -Compress
    [System.IO.File]::AppendAllText($Path, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}
