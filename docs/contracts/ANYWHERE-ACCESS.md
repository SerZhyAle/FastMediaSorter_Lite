# Pointer - `ANYWHERE-ACCESS`

| | |
| --- | --- |
| **Id** | `ANYWHERE-ACCESS` |
| **Version** | 1.0, active (since 2026-10-07; read 2026-10-16: sections 1 to 4, 5.5, 8, the amendments A to L and `vectors/descriptor-v2.json`; the catalog is still at 1.0). Sections 5.1 to 5.4, 6 and 7 are the superseded 0.10 record and bind nothing: the exchange server is `DEVICE-EXCHANGE`. Owner: FastMediaSorter Android |
| **Home** | `anywhere-access/README.md` in the shared contracts catalog |
| **Role here** | **producer and consumer of the share descriptor** `FMSSFTP1`/`FMSSFTP2` (`src/Exchange/FmsSftpCode.vb`, `src/Resources/PhoneConnectionIntake.vb`; modern only), of the third-party port (section 5.5) and of the outward link and per-grant login (amendments K and L). The exchange-server half is carried by `DEVICE-EXCHANGE` |

## What this repository must do to stay conformant

- **The code (section 4.3, amendment J).** A case-sensitive `FMSSFTP1:` or `FMSSFTP2:` prefix and `&`-separated
  fields written in the order `h`, `p`, `u`, `pw`, `fp`, `x`, `sid`, `drv`; `x` and `sid` both present or both
  absent; an unknown key ignored; an incomplete or malformed code refused whole, never partly read. A reader
  takes a code from pasted text, a camera QR and a QR in an image file or on the clipboard.
  `vectors/descriptor-v2.json` prevails over the text where they differ.
- **The third-party port (section 5.5, amendment G).** It travels in `publicPort` of the exchange resource record
  and in a `.fmscfg` access path, never in `FMSSFTP2`.
- **Backward compatibility (section 8).** `FMSSFTP1` is unchanged forever and a v2 reader accepts both prefixes;
  `.fmscfg` stays schema 2; with no Google account and no exchange server the product serves and exports exactly as
  before; no host-key rule weaker than today's pinning.
- **Drive and TLS (amendments H and I).** A share's `access` reaches Drive only by the user's explicit opt-in, off
  by default and worded to name Google; `connect {shareId}` runs only inside TLS.
- **Outward link and guest grant (amendments K and L).** A link is an `FMSSFTP2` code with its own `u` and `sid`
  and no `pw`, the password told separately. A guest grant and a link each get a login confined to the granted
  root, enforced at the producer's SFTP boundary; a producer whose worker cannot confine a login to one root
  offers no guest grant and no link.

## Where it stands

The registry row of this product and its dated exceptions say which clauses are proven and which are open; this
pointer never repeats them. Specification 244 holds the clause-by-clause audit.
