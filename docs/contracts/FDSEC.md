# Pointer - `FDSEC-FORMAT` and `FDSEC-BEHAVIOUR`

| | |
| --- | --- |
| **Ids** | `FDSEC-FORMAT` (the bytes) and `FDSEC-BEHAVIOUR` (what a program handling them must do) |
| **Version** | catalog `FDSEC-FORMAT` 1.3, `FDSEC-BEHAVIOUR` 1.5, active (re-read 2026-10-07). **This reader implements suite 1 of the format and the reader-half sections of the behaviour** (its 1.1 level of 2026-09-24, ticket 038), and reads 1.3 / 1.5. 1.2 adds suite 3 (directory containers) and 1.3 suite 2 (the quiet suite): this reader opens neither - it refuses a suite-3 file by name as *unsupported* (`FdSecHead.Validate`, run on the catalog's four suite-3 containers) and a suite-2 file fails the length screen as *damaged or incomplete* (`FDSEC-FORMAT` 18.8), which the contract says does nothing wrong. 1.4 (the `p:<value>` token) and 1.5 (a "remembered on this device" credential source) concern a command-line credential and a keystore this product does not have. Owner: FileDO |
| **Home** | `secure-container/README.md`, with `FDSEC-FORMAT.md`, `FD-SEC-CONTRACT.md` and `vectors/` in the shared contracts catalog |
| **Role here** | consumer, reader half - `specification 026` (local specification), in `src/Secure/` on `main`, in **no release yet** (the latest release predates it; first release containing it: to be recorded, with a fresh cross-product vector run, when it ships) |

Two ids, deliberately separate: the format is what the bytes are, the behaviour is what a program that
reads and writes them must do to be safe. This repository implements the **reader half of both** -
opening a `.fd-sec` container and showing what is inside as ordinary media. Writing containers is a
later ticket, deliberately, because `FD-SEC-CONTRACT` §6 is where the contract turns from a list a reader
can mostly ignore into the thing that decides whether a feature destroys data.

## What this repository does

- The whole format lives in [`src/Secure/`](../../src/Secure), with no product dependencies: it never
  logs, never shows a dialog, never reads a setting and never touches a path outside the container it was
  handed. The viewer's side is `src/Main_Form.Secure.vb` and nothing else reaches the crypto.
- Conformance is run against the **catalog's own vectors**, vendored as artifacts into
  `tests/Lite.Tests/TestData/fdsec-vectors/` beside a `PROVENANCE.txt` that names this contract, its
  version, the date taken and each file's SHA-256 - which the suite asserts before trusting a byte.
  Vendoring an artifact so a test can run for anyone who clones this repository is a different act from
  vendoring a document; the documents stay in the catalog and are only cited.
- The two deviations 026 recorded - the **streaming reader** (see below) and the absence of the writer -
  were both closed by 1.1 (§14.4 and the consumer-only scope). The registry, not this page, says what
  is open today.
- The vendored `vectors.json` and the two suite-1 containers are byte-identical to the catalog's current
  ones (checked 2026-10-07, SHA-256 against `PROVENANCE.txt`). Four suite-3 containers were added to the same
  folder on that date for one negative test - they are never opened, only refused - and the suite-2 files are
  not vendored (their three lengths are pinned instead).

## What this repository must keep doing

- Reproduce sections 2-5 of the contract **exactly**. They are not preferences: a one-byte deviation
  produces files nobody else can open, and the conformance vectors decide, not the prose.
- Keep the three outcomes apart - *wrong credential / not a container / tampering*, *damaged*,
  *unsupported*. A wrong key and a flipped byte fail an AEAD tag identically; a surface that reports one
  as the other is a defect, not a wording choice.
- Refuse, never sanitise, an unsafe sealed true name, and refuse an impossible sealed size, both as
  *damaged* (1.1: `FDSEC-FORMAT` §7 and §11 steps 6-7). A size is compared while still unsigned and the
  length arithmetic is bounded before it multiplies - an `OverflowException` is no outcome class.
- Apply the only screen that exists before the credential (length a multiple of 512, at least the smallest
  possible container) and nothing else. A container carries no marker; there is no dispatch by looking.
- Never describe an empty or trivial credential as protection, and never write the credential or the
  sealed true name into a log, a history file, a window title, a temporary file or the console.
- Never launch anything recovered from a container, and never hand its content to another program by path.
- Derive with the published KDF profile only. It is a property of the format version and is not stored in
  the file, so a lowered work factor silently produces containers only that build can read.
- State the interoperability claim only after a fresh run of the vectors with its output cited.

## What 026 owed the catalog, and what was paid

All four, on 2026-09-22:

1. **This pointer**, added with the specification.
2. **Consumer rows in the registry** for both ids, carrying the conformance run rather than a reading.
3. **The amendment**, proposed on 2026-09-22 and **accepted into `FDSEC-BEHAVIOUR` 1.1 as §14.4**: the
   streaming reader of §4.2 hands a seekable decrypting stream to a decoder instead of unpacking a whole
   file, so it authenticates every byte it returns but cannot verify the sealed digest of bytes it never
   read, and the viewer therefore never claims a container is intact and offers an explicit "verify this
   container". The dated deviation it was recorded as is closed; the proposal stays beside the contract in
   the catalog as `PROPOSAL-2026-09-22-streaming-reader.md`.
4. **A declaration that the vectors are vendored** into this repository's test data, which is an artifact
   copy and not a document copy.

## Open, and not this repository's to settle alone

- **Suites 2 and 3.** A suite-2 or suite-3 file written by FileDO does not open in this viewer (the reader says
  so honestly: *damaged or incomplete* for suite 2, *unsupported* for suite 3). Whether to implement them is a
  product decision, not a conformance debt - `FDSEC-FORMAT` 1.2 and 1.3 say a reader without them does nothing
  wrong.
- **Sealed-name parity** (`PROPOSAL-2026-09-30-sealed-name-parity.md`, awaiting the owner): this reader follows
  the 1.1 text and so accepts a sealed true name carrying `"`, `<`, `>` or `|`, which FileDO refuses as damaged.
  No code changes until the catalog rules.
