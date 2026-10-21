# Pointer - `SHARE-SESSION`

| | |
| --- | --- |
| **Id** | `SHARE-SESSION` |
| **Version** | 1.3, active (since 2026-07-15; 1.1 of 2026-10-02, 1.2 of 2026-10-12, 1.3 of 2026-10-17; read on 2026-10-20: the README, `share-session-reconnect.md` and both amendments). Owner: FMS Companion (the Go worker holds the server guarantees) |
| **Home** | `remote-folder-access/README.md` and `share-session-reconnect.md` in the shared contracts catalog |
| **Role here** | **client** of the session for the modern viewer's SFTP resources (`src/Resources/SftpProvider.vb`, `ResourceConnect.vb`, `EndpointResolver.vb`, `ResourceProbe.vb`, `ResourceFailure.vb`) and for the Share Manager's credential-less banner probe (`src/FastMediaSorterCompanion/Core/SftpProbe.vb`); **custodian of guarantee 3's state** across installs, uninstalls and host-mode changes (`WorkerProcess.vb`, `publishing/installer/install-share-service.ps1`, `FastMediaSorter.iss`). No server half here: the Go worker is the server. Modern only |

## What this repository must do to stay conformant

- **Reconnect transparently (rule 6, O1).** On a transport failure, before the person sees anything: race the stored
  access paths, re-verify the host key against the stored pin, re-authenticate, reopen the subsystem and retry the
  pending operation. Only a failure of the whole sequence is shown.
- **Four classes, four reactions (rule 7, O2).** Transport reconnects; an auth rejection stops and asks to re-pair; a
  host-key mismatch refuses and warns, never auto-accepts the new key; an SFTP status is an ordinary result and
  never a reconnect. `ResourceFailureKind` and `ResourceMessages` keep them apart.
- **Bounded, with the old slot released (rules 8, 10, O3).** A small number of backed-off attempts, never an
  unbounded loop; the previous connection is released before a new one is taken; a refusal at the connection limit
  is a transport failure, never an auth or host-key failure. A keepalive is an optimization only (rule 9, O4).
- **A close before the banner is undetermined (1.3).** An end of stream before any banner byte is never read as "not
  an SSH server" or as bad credentials, and a banner is positive evidence only. A probe sends no credential, so it
  cannot count against a lockout. `ProbeVerdict.ClosedBeforeBanner` and its Share Manager twin carry it.
- **Resource PIN (1.2).** A protected root is opened only with the contract's keyboard-interactive proof, and a
  challenge of another shape is refused; the PIN travels on SSH only, never in an exchange envelope or a
  descriptor.
- **Guarantee 3 is kept by custody (rule 3, 1.1).** The identity survives until the host's state store is removed by
  hand. `WorkerProcess.vb` never touches or regenerates the worker's data directory; `install-share-service.ps1`
  checks the host-key fingerprint with the worker itself before and after a copy, stops on a mismatch, keeps an
  existing machine identity on an upgrade and stops rather than guess among several user profiles; removing the
  server role and uninstalling the viewer leave the state directory in place. No text states the idle window as a
  number or promises re-pairing after an update, restart, reboot or edition switch.
- **Evidence (section 4).** The four acceptance scenarios against a live share: `tests/Integration/SftpConsumerLive.ps1`
  (hand-run) and, for custody, `tests/Integration/ShareIdentityMigration.ps1`. The contract has no vectors.

Pairing, the export payload and the access-path race are `FMSCFG`, not this contract.

## Where it stands

The registry row of this product and its dated exceptions say which rules are proven and which are open; this
pointer never repeats them. Specification 244 holds the clause-by-clause audit.
