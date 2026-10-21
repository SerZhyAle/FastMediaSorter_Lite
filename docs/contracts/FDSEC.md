# Pointer - `FDSEC-FORMAT` and `FDSEC-BEHAVIOUR`

| | |
| --- | --- |
| **Ids** | `FDSEC-FORMAT` (the bytes) and `FDSEC-BEHAVIOUR` (what a program handling them must do) |
| **Version** | `FDSEC-FORMAT` 1.4 and `FDSEC-BEHAVIOUR` 1.6, both active (since 2026-09-22; both of 2026-10-19; read on 2026-10-20 with the sealed-name amendments, FORMAT section 20 and BEHAVIOUR section 18). Owner: FileDO |
| **Home** | `secure-container/README.md`, with `FDSEC-FORMAT.md`, `FD-SEC-CONTRACT.md` and `vectors/` in the shared contracts catalog |
| **Role here** | **consumer, reader half, suite 1** (`src/Secure/`; the viewer's side is `src/Main_Form.Secure.vb` and `src/Secure_Password_Form.vb`; specification 026). Plus **one narrow writer**: `src/Secure/FdSecWriter.vb` seals suite-1 containers that are ephemeral, from a finished in-memory plaintext under a per-run random credential, with no notion of an original file; its only caller is `src/Video/AnimationVideoStore.vb` (specification 242, owner decision Q242-1). Modern only: every `src/Secure/` file is whole-file `#If Not NETFRAMEWORK` |

Two ids, deliberately separate: the format is what the bytes are, the behaviour is what a program that reads and
writes them must do to be safe. A user-facing "pack a file" feature is not part of this repository and stays its own ticket.

## What this repository must do to stay conformant

- **The bytes (FORMAT sections 2 to 5, 11, 13).** Reproduce them exactly; the catalog's vectors decide, not the
  prose. Head fields are read only after slot 0 authenticates; an unknown version, suite, flag or slot type is
  *unsupported*. Suite 3 is refused by name; a suite-2 file falls to the length screen or the wrong-credential
  class (13.3, 18.8), which the contract says a reader without those suites does without doing anything wrong.
- **Three outcomes, never merged (BEHAVIOUR 7).** *Wrong credential / not a container / tampering*, *damaged* and
  *unsupported* stay apart; a wrong key and a flipped byte fail an AEAD tag identically.
- **Sealed names and sizes (FORMAT 7, 11, 20).** An unsafe sealed true name and an impossible sealed size are refused
  as *damaged*, never sanitised, with the length arithmetic bounded before it multiplies. The four characters
  `"`, `<`, `>` and `|` are not in the refusal set: a valid authenticated name holding one is not damaged because a
  destination cannot create it. This reader restores no name to a file system, so BEHAVIOUR 18 applies only to a
  restore or export path it does not have.
- **The only screen before the credential** is the length rule (a multiple of 512, at least the smallest
  container). A container carries no marker; there is no dispatch by looking.
- **Hygiene (BEHAVIOUR 6, 8.2, 10).** The published KDF profile only, since it is not stored in the file; no trivial
  credential presented as protection; neither the credential nor the sealed name in a log, a window title, a
  temporary file or the console; no decrypted byte on disk, and nothing recovered is launched or handed to
  another program by path.
- **Streaming reader (14.4).** It authenticates every byte it returns and cannot verify the sealed digest of bytes
  it never read, so the viewer never claims a container is intact and offers an explicit "verify this container".
- **The ephemeral writer.** It owes BEHAVIOUR 5.1 (normalise, build, write, flush, read back in full, rename into
  place) and the section 6 rules that do not concern an original file (2, 3, 8, 10), and it reproduces the writer
  vectors byte for byte with injected randomness in the contract's draw order.
- **Evidence (FORMAT 16, BEHAVIOUR 12).** The catalog's vectors are vendored into `tests/Lite.Tests/TestData/fdsec-vectors/`
  beside a `PROVENANCE.txt` whose SHA-256 the suite asserts first (an artifact copy, never a document copy). The
  generated boundary vectors for the four characters (FORMAT 20) are owed by the reference writer and are replayed
  here when they exist. The interoperability claim is stated only after a fresh run of the vectors with its output cited.

## Where it stands

The registry rows of this product (the release state, the writer's scope and the open items) and its dated
exceptions say what is proven and what is open; this pointer never repeats them. Specification 244 holds the
clause-by-clause audit.
