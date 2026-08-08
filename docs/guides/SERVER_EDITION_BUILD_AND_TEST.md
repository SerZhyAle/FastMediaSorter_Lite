# Server edition - build, publish and verify

Operational companion to [SPECIFICATION_SHARE_SYSTEM_SERVICE.md](../specifications/done/SPECIFICATION_SHARE_SYSTEM_SERVICE.md).
The user-facing documentation is the public page [server.html](../../server.html) - this file
is for whoever builds and ships the thing.

The Server edition is the **always-on** host for the Android Folder Share worker: the same
`fms-share-worker.exe`, started by the Windows SCM instead of by an interactive session.
It is a packaging and host-mode variant, never a fork.

## What is different from the User edition, at a glance

| | User edition | Server edition |
| --- | --- | --- |
| Inno script | `publishing/installer/FastMediaSorter.iss` | `publishing/installer/FastMediaSorterServer.iss` |
| Inno `AppId` | `{7371E7F1-B8A8-4786-8173-5F5B2B6E6AC9}` | `{A9F3C61B-4D2E-4F58-9C7A-1E6B0D3F82A4}` |
| ARP DisplayName | `FastMediaSorter LITE` (frozen) | `Fast Media Sorter Folder Share Server` |
| Install dir | `FastMediaSorter_LITE` | `FastMediaSorter_Server` |
| Release asset | `FastMediaSorter-<v>-windows-x64-setup.exe` | `FastMediaSorter-<v>-windows-x64-server-setup.exe` |
| winget package | `SerZhyAle.FastMediaSorter` | `SerZhyAle.FastMediaSorter.Server` (`publishing/winget/server/`) |
| Elevation | `lowest`, dialog override | `admin`, no override |
| OS floor | `MinVersion=6.1` (the x86 viewer) | `MinVersion=10.0.14393` |
| Payload | viewer x64 + x86, VLC, OCR, Share Manager, worker | Share Manager + worker + helper scripts **only** |
| Store / MSIX | yes | **never** |

Everything in that table is an *identity* anchor: once a version ships, changing a row
orphans the installs that correlate on it. The two things the editions must keep
**identical** are owned by neither installer - the SCM service name
`FastMediaSorterCompanionSFTP` and the on-disk state format, both defined by the worker.

## Building locally (a "сборка" - never tags, never publishes)

```powershell
.\tools\Build-ServerInstaller.ps1                 # -> dist\FastMediaSorter-<v>-windows-x64-server-setup.exe (+ .sha256)
.\tools\Build-ServerInstaller.ps1 -MaxCompress    # release-grade compression, several minutes slower
.\tools\Build-ServerInstaller.ps1 -SkipBuild      # repackage the existing bin\Release Share Manager
```

Needs Inno Setup 6 (`winget install JRSoftware.InnoSetup`) and a vendored worker at
`payload\companion\fms-share-worker.exe`. The worker is **built in its own repository**
(`P:\windows\fms_companion`) and vendored here - never built or hand-edited from this repo.

The release workflow builds the same asset from its own lean stage
(`.github/workflows/release.yml`, step *Build Server edition installer*) and attaches it
plus its `.sha256` to the GitHub Release.

## Worker-side requirements

The Server edition needs a worker that has these; a build without them installs a service
that cannot start:

- `--service` - enters the SCM dispatcher (`internal/service/scm.go`). It reports
  StartPending then Running, and **terminates with a non-zero service-specific exit code**
  if the worker's main loop ever ends without a stop request. A service left marked
  RUNNING with a dead worker is the failure mode the whole design exists to prevent -
  which is also why the installer sets `sc failureflag 1`, without which Windows ignores a
  service that reported a stop with an error.
- `--manage-sid` - the string SID(s) that may drive the control pipe. In Session 0 the
  process identity is LocalService, so the pipe DACL cannot be derived from it.
- `--machine-datadir` / `--datadir` - the persisted-state location.
- `--inspect-datadir <dir>` - a read-only JSON verdict (exists / host key present /
  fingerprint). The migration helper uses it to prove the identity survived the copy
  **before** the service is registered.

## Publishing to winget

Same rules as the User edition (see
[SPECIFICATION_WINGET_PUBLISHING.md](../specifications/done/SPECIFICATION_WINGET_PUBLISHING.md)):
point at the Inno `setup.exe`, no `Dependencies`, no `NestedInstallerType`. Two deliberate
differences, both explained in the manifest comments:

- `Scope: machine` **is** declared here (the User manifest must not declare `Scope` at all)
  - the product is a machine service, and the installer is `PrivilegesRequired=admin`.
- `MinimumOSVersion: 10.0.14393` **is** declared here (the User manifest must not) - there
  is no 32-bit half of this package to keep visible on Windows 7/8.1.

The silent switches carry `/MIGRATEFROMUSER=yes`, because a silent Server install
otherwise refuses to run when a User edition is present rather than migrating state
unattended.

First submission: the manifests under `publishing/winget/server/` carry placeholder
version/URL/SHA until the first Server release exists. Fill them with `wingetcreate` from
the published asset - never hand-edit a SHA - and fill the winget PR description as usual.

## The verification matrix

Acceptance criterion 10 of the spec. Everything below needs an isolated VM: the flows
create a service, rewrite ACLs and open a firewall port, and several of them can only be
observed on a machine you are willing to reboot without logging in.

1. **Fresh Server install.** After UAC and root configuration:
   `sc query FastMediaSorterCompanionSFTP` reports `RUNNING`, the program-scoped firewall
   rule exists, and a phone browses the configured root.
2. **Reboot with no logon** - the criterion that actually distinguishes this edition.
   Reboot, do **not** sign in, browse from the phone.
3. **User install unchanged.** The existing installer, the existing winget package and the
   Store MSIX create no service and behave exactly as before.
4. **Migration User -> Server.** One elevated path; the host-key fingerprint, credentials,
   roots, port and stats survive; paired phones reconnect without re-pairing; no second
   worker is spawned (`Get-Process fms-share-worker` shows exactly one).
5. **Migration Server -> User.** Service stops and is deleted; the foreground worker
   resumes from the preserved identity; phones still connect.
6. **Permissions.** A configured NTFS root is readable by LocalService only through the
   recorded grant; a non-authorized local user cannot open the control pipe or obtain the
   credentials; a UNC root fails with the precise explanation rather than silently.
7. **Network readiness.** Boot with the network unavailable, then restore it - status and
   the LAN announce come good without an interactive restart.
8. **Update over a real prior install** (not only a fresh one) - canon invariant 6.
9. **Uninstall.** The service is stopped and deleted before file removal; only app-owned
   firewall/ACL state is removed; no listening port and no orphaned SCM registration
   remain; the identity is kept unless deleted explicitly.

Where to look when one of these fails: `%ProgramData%\FastMediaSorterCompanion\` holds
`install-share-service.log` (every elevated action, with its verdict) and `service.log`
(the SCM host's own failures). Neither leaves the machine.
