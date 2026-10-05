# Pointer - `ANYWHERE-ACCESS`

| | |
| --- | --- |
| **Id** | `ANYWHERE-ACCESS` |
| **Version** | 0.10, draft, [PROPOSED] in full (written 2026-10-05 by FastMediaSorter Android, ticket S4094; nothing implements it yet) |
| **Home** | `anywhere-access/README.md` in the shared contracts catalog |
| **Role here** | exchange-server implementer (sections 3, 5.1): the subproduct specified by local specification 133. The future anywhere-capable FMS_W server versions (producer/consumer, dual-mode) are the FMS_W session's own ticket (S4094 work/02), not this pointer's burden yet |

The contract names this product the exchange-server implementer: a Go program for Windows distributed
inside this product's package, the language and the packaging not normative. The share descriptor v2
(`FMSSFTP2`), the `.fmscfg` additive kind, the Drive channel and the producer/consumer anywhere features
belong to the other products' tickets and change nothing here until they land.

## What this repository holds while the contract is [PROPOSED]

1. **The server-role review** is filed where the catalog requires it: `PROPOSAL-2026-10-05-fmsw-server-role-review.md`
   in the contract's catalog folder - four asks (consumer-leg TLS, the port `refused` reason, the
   self-hosted certificate model, the Drive appDataFolder scope). The owner decides; no local divergence
   is kept anywhere.
2. **Local specification 133** is the subproduct's specification: responsibilities, the §5.3 discipline,
   deploy configuration, package integration, dual-mode boundary, phases aligned with the contract's
   freeze ladder. It freezes nothing; it releases nothing before the wire freezes (rung 2), and the
   rung-2 engineering build it allows is the ladder's own instrument, not a shipped feature.
3. **No conformance row yet**: the catalog registry gains this product's row only at rung 4, added by
   this product's own session, with the dual-mode regression behind it (contract section 9).
