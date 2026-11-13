<#
.SYNOPSIS
    Puts the Google Drive OAuth client pair into the repository's GitHub Actions secrets FMS_DRIVE_CLIENT_ID and
    FMS_DRIVE_CLIENT_SECRET (specification L0306 section 4.3), printing no value. Run by the owner only.

.DESCRIPTION
    release.yml maps those two secrets to the MSBuild properties FmsDriveClientId / FmsDriveClientSecret of the
    Share Manager publish step; without them a tag ships a Share Manager whose Drive page says "not configured".

    The pair comes from one source, never mixed:
      - default: this user's Windows environment (User scope) FMS_DRIVE_CLIENT_ID / FMS_DRIVE_CLIENT_SECRET;
      - -ClientJson: the Desktop app client JSON Google Cloud Console downloads (its "installed" section).
    It is refused when a value is empty or the client id is not shaped like <digits>-<token>.apps.googleusercontent.com
    (a swapped pair is named). Each value goes to `gh secret set` on its STANDARD INPUT, written without a newline -
    never on a command line, where a process list or a log could show it. Afterwards `gh secret list` must show both
    names; the values cannot be read back from GitHub by design.

    Setting a repository secret is outward-facing and persistent: this script is the owner's to run.

.PARAMETER Repo
    owner/name. Default: SerZhyAle/FastMediaSorter_Lite.

.PARAMETER ClientJson
    Take the pair from a Desktop app client JSON instead of the User environment.

.PARAMETER DryRun
    Resolve and judge the pair and check that gh is signed in; set nothing.

.NOTES
    Exit codes: 0 both secrets set and listed (or -DryRun found nothing in the way), 1 refused (no or a misshapen
    pair, gh not signed in), 2 gh failed or a name is missing from the listing afterwards.

.EXAMPLE
    pwsh -NoProfile -File P:\WINDOWS\FastMediaSorter_Lite\tools\Set-DriveClientSecrets.ps1
#>
[CmdletBinding()]
param(
    [string]$Repo = 'SerZhyAle/FastMediaSorter_Lite',
    [string]$ClientJson = '',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$idName = 'FMS_DRIVE_CLIENT_ID'
$secretName = 'FMS_DRIVE_CLIENT_SECRET'
$idShape = '^\d+-[A-Za-z0-9_]+\.apps\.googleusercontent\.com$'
function Get-Trimmed([string]$Value) { if ($Value) { $Value.Trim() } else { '' } }

if ($ClientJson) {
    if (-not (Test-Path -LiteralPath $ClientJson -PathType Leaf)) { Write-Host "Client JSON not found: $ClientJson" -ForegroundColor Red; exit 1 }
    try { $json = Get-Content -LiteralPath $ClientJson -Raw | ConvertFrom-Json } catch { $json = $null }
    if (-not $json -or $json.PSObject.Properties.Name -notcontains 'installed') {
        Write-Host 'Refused: the JSON is not a Desktop app client (no "installed" section); nothing set.' -ForegroundColor Red
        exit 1
    }
    $id = Get-Trimmed ([string]$json.installed.client_id)
    $secret = Get-Trimmed ([string]$json.installed.client_secret)
    $json = $null
    $source = 'the client JSON'
} else {
    $id = Get-Trimmed ([Environment]::GetEnvironmentVariable($idName, 'User'))
    $secret = Get-Trimmed ([Environment]::GetEnvironmentVariable($secretName, 'User'))
    $source = 'the User environment'
}
if (-not $id -or -not $secret) {
    Write-Host "Refused: $source does not hold both values; nothing set." -ForegroundColor Red
    exit 1
}
if ($id -notmatch $idShape) {
    $why = if ($secret -match $idShape) { 'the two values look swapped' } else { 'the client id is not shaped like a Google OAuth client id' }
    Write-Host "Refused: $why; nothing set." -ForegroundColor Red
    exit 1
}

$gh = (Get-Command gh -ErrorAction SilentlyContinue)
if (-not $gh) { Write-Host 'Refused: the GitHub CLI (gh) is not on PATH; nothing set.' -ForegroundColor Red; exit 1 }
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& $gh.Source auth status *> $null
$authCode = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($authCode -ne 0) { Write-Host 'Refused: gh is not signed in (gh auth login); nothing set.' -ForegroundColor Red; exit 1 }

if ($DryRun) {
    Write-Host "Ready: a well-formed pair from $source, gh signed in, target $Repo. Nothing set (-DryRun)."
    exit 0
}

# One secret, its value on standard input without a trailing newline.
function Set-Secret([string]$Name, [string]$Value) {
    $psi = [Diagnostics.ProcessStartInfo]::new($gh.Source)
    foreach ($a in 'secret', 'set', $Name, '--repo', $Repo, '--app', 'actions') { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.Write($Value)
    $p.StandardInput.Close()
    $null = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) {
        # gh's own message never carries the value it was given on stdin
        Write-Host "gh secret set $Name failed (exit $($p.ExitCode)): $($err.Trim())" -ForegroundColor Red
        return $false
    }
    Write-Host "Set $Name in $Repo."
    return $true
}

$ok = (Set-Secret $idName $id) -and (Set-Secret $secretName $secret)
$id = $null; $secret = $null
if (-not $ok) { exit 2 }

$names = @(& $gh.Source secret list --repo $Repo --app actions --json name --jq '.[].name' 2>$null)
$missing = @($idName, $secretName | Where-Object { $_ -notin $names })
if ($missing.Count) {
    Write-Host "Not listed afterwards: $($missing -join ', ')" -ForegroundColor Red
    exit 2
}
Write-Host "Both secrets are listed in $Repo. The next release build's Share Manager carries the pair; judge it with tools\Test-DriveClientArtifact.ps1 -CompareEnvironment."
exit 0
