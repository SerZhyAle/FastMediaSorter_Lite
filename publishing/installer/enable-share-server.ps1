# Adds (or, with -Disable, removes) the one Windows Firewall exception that makes
# the Android Folder Share SFTP worker reachable. Run ELEVATED - an elevated Inno
# install calls netsh directly, while the in-app opt-in and its withdrawal, the
# end of a per-user Setup and a per-user uninstall launch this script via
# ShellExecute "runas" (a single UAC prompt).
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
    if (-not $Disable) {
        # Idempotent: drop any existing rule of this name first (ignore its exit code -
        # "No rules match" returns non-zero when nothing was there, which is fine).
        netsh advfirewall firewall delete rule name="$rule" | Out-Null
        netsh advfirewall firewall add rule name="$rule" dir=in action=allow `
            program="$ExePath" protocol=TCP profile=domain,private,public enable=yes | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } else {
        # By PROGRAM only, never by name. By name alone the withdrawal left rules behind:
        # the first time the worker listens, Windows' own firewall prompt can add a rule
        # named after the program, and that inbound allow outlived the consent (ticket 054
        # H-23). And by name it also took rules that were not ours to take: the Server
        # edition and a service role handed over from the Share Manager carry a rule of
        # the SAME name aimed at another copy of the worker, and a per-user uninstall now
        # runs this path too (ticket 037 Ф-3.1). Deleting by PROGRAM catches every rule
        # aimed at this exe whatever its name, and cannot match another installation's,
        # which has another path. Exit code ignored: "No rules match" is fine.
        netsh advfirewall firewall delete rule name=all program="$ExePath" | Out-Null
        # Withdrawing the opt-in from the app (ticket 028 phase 6) also removes the
        # machine marker an elevated install may have written next to the worker -
        # otherwise the consent would stay given no matter what the user chose.
        $marker = Join-Path (Split-Path -Parent $ExePath) 'server-features.enabled'
        if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
    }
    exit 0
} catch {
    exit 1
}
