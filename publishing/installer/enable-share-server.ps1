# Adds (or, with -Disable, removes) the one Windows Firewall exception that makes
# the Android Folder Share SFTP worker reachable. Run ELEVATED - the Inno installer
# calls netsh directly (it is already elevated), while the in-app deferred opt-in
# launches this script via ShellExecute "runas" (a single UAC prompt).
#
# Program-scoped inbound allow (not port-scoped) so the rule keeps working when the
# worker's dynamically-assigned SFTP listen port changes. profile=domain,private,public
# so a dedicated server (whose network is usually classified Public) is covered.
#
# Exit 0 on success, non-zero on failure - ServerFeatures.EnableViaElevation reads it.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [switch]$Disable
)

$ErrorActionPreference = "Stop"
$rule = "FastMediaSorter Companion SFTP"

try {
    # Idempotent: drop any existing rule of this name first (ignore its exit code -
    # "No rules match" returns non-zero when nothing was there, which is fine).
    netsh advfirewall firewall delete rule name="$rule" | Out-Null

    if (-not $Disable) {
        netsh advfirewall firewall add rule name="$rule" dir=in action=allow `
            program="$ExePath" protocol=TCP profile=domain,private,public enable=yes | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    exit 0
} catch {
    exit 1
}
