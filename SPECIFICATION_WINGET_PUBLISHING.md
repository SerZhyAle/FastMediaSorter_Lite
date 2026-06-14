# winget Publishing - Hard-Won Gotchas

Lessons captured from PR [microsoft/winget-pkgs#386820](https://github.com/microsoft/winget-pkgs/pull/386820)
(version `26.6.12.0031`), where ~12 hours were lost flip-flopping between
installer formats. **The root causes were never the manifest *shape* - they
were the payload and the dependency declaration.** This document exists so we
don't repeat that.

## Outcome (recorded 2026-06-12)

PR #386820 **passed validation** (`Azure-Pipeline-Passed` + `Validation-Completed`,
build 341141: `Installation Validation` succeeded) only after the manifest was
reduced to a plain direct Inno installer with **no `Scope` and no `Dependencies`**.
The winning manifest is mirrored in
[winget/SerZhyAle.FastMediaSorter.installer.yaml](winget/SerZhyAle.FastMediaSorter.installer.yaml)
- start the next release from that shape, not from a `wingetcreate` default that
re-adds `Scope`/`Dependencies`. After the pipeline passed, the only remaining gate
was manual moderator approval (normal for community PRs), and the
`Manifest-Metadata-Consistency` / `Validation-Guide` note about
`NestedInstallerType` is cosmetic, not a failure.

## TL;DR - the manifest that works

Point the manifest at the **Inno Setup `setup.exe` directly**, with **no
declared dependencies** and **no `Scope` field**:

```yaml
PackageIdentifier: SerZhyAle.FastMediaSorter
PackageVersion: <YY.M.D.HHmm>
InstallerType: inno
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART /SP-
Installers:
- Architecture: x64
  InstallerUrl: .../v<version>/FastMediaSorter-<version>-windows-x64-setup.exe
  InstallerSha256: <hash of setup.exe>
ManifestType: installer
ManifestVersion: 1.12.0
```

Why this passes:
- Not an archive → **no local-archive malware scan, no slow extraction**.
- No dependency → **no dependency-resolution churn**.
- `installer/FastMediaSorter.iss` sets `AppVersion={#Version}`, so the Inno
  installer registers an **ARP entry whose `DisplayVersion` equals
  `PackageVersion`** - exactly what the "Installation Validation" step matches on.
- `PrivilegesRequired=lowest` → installs per-user without elevation.

## The three traps (one per format we tried)

The validation pipeline runs in a clean Windows VM and does
`winget install --manifest ...` then checks for a matching ARP entry. The real
error codes come from the **`InstallationVerificationLogs`** build artifact
(see "How to read the real error" below), not from the GitHub comments.

| Format we tried | What actually failed |
|---|---|
| **`single-exe.zip`** (single-file runtime bootstrap) | Defender ML flags it as `Program:Script/Wacapew.A!ml`. This is a **persistent false positive** on the self-extracting bootstrap exe (a different binary from the normal ILMerged exe). **Never point winget at `single-exe.zip`.** Keep it only as a convenience download on GitHub. |
| **Portable `windows-x64.zip`** (`InstallerType: zip` + `NestedInstallerType: portable`) | Archive scan passes, but extracting the ~99 MB payload takes ~2 min and the install then aborts with **`0x80004004` (E_ABORT)** while placing files. The payload is too heavy for the portable flow even though the published `26.4.27.1200` used the same shape with a smaller payload. |
| **Inno + `Microsoft.VCRedist.2015+.x64` dependency** | winget enters a long dependency-resolution loop, VCRedist returns `0x8A150010` (not applicable / already present), and the package install aborts with **`0x8A150044`**. The app does not need VCRedist *declared* to install (validation never launches it), and the published version declared no dependencies. **Don't declare VCRedist.** |
| **Inno + `Scope: user`** | Declaring `Scope: user` makes the harness run the whole flow with `--scope user` and `Elevation: False`. The package install then returns **`0x8A150044`** ("no suitable installer found for `--scope user`"), and the user-scope VCRedist baseline step also has no user installer. **Omit `Scope` entirely** - the published version had none. With no `Scope`, validation runs elevated/machine and the Inno installer registers its ARP entry normally. |

## Non-blocking noise to ignore

- **"Inconsistencies detected … Missing property `NestedInstallerType` /
  `NestedInstallerFiles` / Sequence `Tags` contains fewer items"** - this is a
  `Validation-Guide` (informational) note, **not** a failure. It fires whenever
  the installer shape differs from the previously published version (the old one
  was a portable zip). Moderators routinely approve installer-type changes
  between versions. Keeping the `portable` tag in the locale manifest silences
  the Tags part; the NestedInstaller part is unavoidable with a direct installer
  and is harmless.
- **"Signature Update failed / Inconclusive Signature update"** in the install
  log - that's the validation VM failing to update Defender signatures; it is
  environmental, not our package.

## Process discipline

- **Pick one shape and stop changing it.** Every push/`@wingetbot run` cancels
  the in-flight run and re-queues from scratch. The PR sat in `REVIEW_REQUIRED`
  waiting for a human moderator, not because of an error - thrashing only reset
  the queue. Wait for a result before changing anything.
- **Verify locally before pushing**, with real checks (not "it worked on my
  machine"):
  - `winget validate --manifest <dir>`
  - run `setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CURRENTUSER`,
    confirm **exit 0** and an ARP entry with the right `DisplayVersion`
  - `MpCmdRun.exe -Scan -ScanType 3 -File <asset>` to pre-check Defender
- Confirm the `InstallerSha256` against the **actual uploaded asset**, not just
  the `.sha256` sidecar (sidecars can go stale after a rebuild).

## How to read the real error (don't guess from GitHub comments)

The GitHub bot comments are generic. The actual exit code and screenshots are in
the build's `InstallationVerificationLogs` artifact, which is anonymously
downloadable from the Azure DevOps validation build:

```powershell
$org="shine-oss"; $proj="8b78618a-7973-49d8-9174-4360829d979b"; $build=<id>
$arts = Invoke-RestMethod "https://dev.azure.com/$org/$proj/_apis/build/builds/$build/artifacts?api-version=7.0"
$u = ($arts.value | ? name -eq 'InstallationVerificationLogs').resource.downloadUrl
Invoke-WebRequest $u -OutFile inst.zip; Expand-Archive inst.zip .\inst -Force
# read *Log_InstallationClient*.txt for the install exit code,
# and *WinGet-*.log for the detailed step-by-step (terminating context HRESULT)
```

The `<id>` is in the `WinGetSvc-Validation-...-<id>` link the bot posts. The
per-step **timeline** is also anonymous
(`.../builds/<id>/timeline?api-version=7.0`); the raw step **logs** require auth,
but the artifact above does not.
