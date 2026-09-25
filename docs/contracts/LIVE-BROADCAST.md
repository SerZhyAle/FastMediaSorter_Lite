# Pointer - `LIVE-BROADCAST`

| | |
| --- | --- |
| **Id** | `LIVE-BROADCAST` |
| **Version** | 0.12, draft (read 2026-09-24). Owner: FastMediaSorter Android |
| **Home** | `live-broadcast/README.md` + `11_live_broadcast_contract.md` in the shared contracts catalog |
| **Role here** | planned consumer - specification 023, a proposal; no code exists yet |

This file replaces a **copy** of the contract that was taken on 2026-09-13 and had already fallen behind:
it was missing the no-loopback producer rule, the link `package=` hint and the watch measurements of
2026-09-17. The copy was deleted rather than refreshed, which is the only thing that stops it drifting
again.

## What this repository must do when 023 is taken

- Start at the live edge with a small buffer, and **never let the delay grow**: after a stall or a
  reconnect, rejoin at the live edge and drop what was missed.
- Put the resilience in bounded reconnects, not in a deep buffer. On a LAN the producer adds milliseconds;
  anything above half a second end to end was added here.
- Open exactly one connection per listener - a probe connection costs a listener slot, and the watch has
  four.
- Do not compensate for a quiet producer with gain of your own; that hides a defect that has a ticket.
- Read `isLive` from the descriptor rather than probing, and refuse a `schemaVersion` above the one this
  build supports with "update the app", never "invalid".
- Sections marked `[PROPOSED]` in the contract bind nobody: do not assume the address, path or SDP of the
  two video kinds until they are `[CONTRACT]`.
