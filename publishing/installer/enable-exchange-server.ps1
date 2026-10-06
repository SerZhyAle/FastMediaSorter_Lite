<#
.SYNOPSIS
    Adds (or, with -Disable, removes) the one Windows Firewall exception that makes the
    Media Exchange Server reachable from other devices (ticket 143, T-1).

.DESCRIPTION
    Run ELEVATED. The Share Manager's explicit "Allow access from other devices.." button and
    its withdrawal launch this script through ShellExecute "runas" (one UAC prompt); an
    elevated installer or uninstaller may run it directly. Nothing installs it silently and
    the server itself never calls it (133 section 5, owner decision Q-133-9).

    Beside, not merged into, enable-share-server.ps1: a different program, a different rule
    name, and a stricter contract (elevation is checked here, the outcome is logged).

    The rule is a PROGRAM-scoped inbound allow, never a port rule. The exchange server's
    listen port or port range can change in its settings without any elevation, because the
    rule names the program and no port. profile=domain,private,public because the point of
    the server is reachability from other networks; the per-program scope is the safety
    (Q143-1).

    -Disable deletes by PROGRAM, never by name. By name alone a withdrawal left behind the
    rule Windows' own first-listen prompt adds (named after the program) - the Share helper
    learned that the hard way (ticket 054 H-23) - and by name it could also take a rule that
    is not ours. By program it catches every rule aimed at this exe whatever its name and
    cannot match another installation's, which has another path.

    One line per run goes to <ProgramData>\FastMediaSorterExchange\enable-exchange-server.log
    when that directory already exists (never created here), else to %TEMP%.

    Exit codes: 0 ok | 1 unexpected error | 2 missing or unusable -ExePath (enable only
    needs the file to exist) | 5 not elevated | 8 netsh could not add the rule.

.PARAMETER ExePath
    Absolute path of fms-exchange-server.exe - the staged copy the program will actually run
    from (the service's copy under <DataDir>\bin, or the installed one).

.PARAMETER Disable
    Remove every rule aimed at -ExePath instead of adding the rule.
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [switch]$Disable
)

$ErrorActionPreference = 'Stop'
$rule = 'FastMediaSorter Media Exchange Server'
$logName = 'enable-exchange-server.log'

function Write-RunLog([string]$message) {
    $line = '{0} {1}' -f (Get-Date).ToUniversalTime().ToString('s'), $message
    try {
        $dir = $null
        if ($env:ProgramData) {
            $candidate = Join-Path $env:ProgramData 'FastMediaSorterExchange'
            $attr = $null
            try { $attr = [System.IO.File]::GetAttributes($candidate) } catch { }
            # An existing, ordinary directory only: a link would turn an elevated append into a
            # write wherever it points.
            if ($null -ne $attr -and ($attr -band [System.IO.FileAttributes]::Directory) -and -not ($attr -band [System.IO.FileAttributes]::ReparsePoint)) { $dir = $candidate }
        }
        if (-not $dir) { $dir = [System.IO.Path]::GetTempPath() }
        $path = Join-Path $dir $logName
        $fileAttr = $null
        try { $fileAttr = [System.IO.File]::GetAttributes($path) } catch { }
        if ($null -ne $fileAttr -and ($fileAttr -band [System.IO.FileAttributes]::ReparsePoint)) { return }
        Add-Content -LiteralPath $path -Value $line -Encoding UTF8
    } catch {
        # The log is a record, never a reason to change the outcome.
    }
}

function Stop-Run([int]$code, [string]$message) {
    Write-RunLog ("{0} (exit {1})" -f $message, $code)
    exit $code
}

# Elevation first, before anything is looked at or changed.
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-Run 5 'this helper must run elevated (administrator rights are required for the firewall rule)'
}

try {
    # A path with a quote in it would end the program="<path>" argument early and let the rest of
    # the string become more netsh arguments.
    if ([string]::IsNullOrWhiteSpace($ExePath) -or $ExePath.Contains('"') -or -not [System.IO.Path]::IsPathRooted($ExePath)) {
        Stop-Run 2 "-ExePath must be an absolute path without quotes: '$ExePath'"
    }

    if (-not $Disable) {
        if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
            Stop-Run 2 "the exchange server executable was not found: '$ExePath'"
        }
        # Idempotent: drop any rule of this name first. Its exit code is ignored on purpose -
        # "No rules match" is non-zero when nothing was there, which is fine.
        & netsh.exe advfirewall firewall delete rule name="$rule" | Out-Null
        & netsh.exe advfirewall firewall add rule name="$rule" dir=in action=allow `
            program="$ExePath" protocol=TCP profile=domain,private,public enable=yes | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Stop-Run 8 "netsh could not add the firewall rule (netsh exit $LASTEXITCODE) for '$ExePath'"
        }
        Write-RunLog "firewall rule '$rule' in place for '$ExePath'"
    } else {
        # By PROGRAM only, never by name (see the description). Exit code ignored: "No rules
        # match" is the normal answer on a second withdrawal.
        & netsh.exe advfirewall firewall delete rule name=all program="$ExePath" | Out-Null
        Write-RunLog "firewall rules aimed at '$ExePath' withdrawn"
    }
    exit 0
} catch {
    Stop-Run 1 ("unexpected error: {0}" -f $_.Exception.Message)
}
