# Pointer - `FDSEC-FORMAT` and `FDSEC-BEHAVIOUR`

| | |
| --- | --- |
| **Ids** | `FDSEC-FORMAT` (the bytes) and `FDSEC-BEHAVIOUR` (what a program handling them must do) |
| **Version** | catalog 1.1, active (format version 1). **This reader implements and reads 1.1** - brought up from 1.0 by ticket 038 on 2026-09-24, which closed the registry exception. Owner: FileDO |
| **Home** | `secure-container/README.md`, with `FDSEC-FORMAT.md`, `FD-SEC-CONTRACT.md` and `vectors/` in the shared contracts catalog |
| **Role here** | consumer, reader half - [specification 026](../specifications/done/026_SPECIFICATION_SECURE_CONTAINER_FD_SEC_DOTNET10.md), shipped 2026-09-22 in `src/Secure/` |

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
3. **The amendment**, proposed and not taken: the streaming reader of §4.2 hands a seekable decrypting
   stream to a decoder instead of unpacking a whole file, so it authenticates every byte it returns but
   cannot verify the sealed digest of bytes it never read. That mode is not described in the contract,
   and the nearest named surface, `14.1 Reveal`, is not it - `reveal` writes a copy into a sandbox and
   this writes nothing at all. The proposal asks for it as `14.4`, with its weaker guarantee written
   down, and lives beside the contract in the catalog as `PROPOSAL-2026-09-22-streaming-reader.md`. It is
   recorded as a dated deviation until the owner rules. Comply or amend - never deviate in silence.
4. **A declaration that the vectors are vendored** into this repository's test data, which is an artifact
   copy and not a document copy.
