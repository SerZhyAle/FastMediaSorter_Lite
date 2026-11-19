# docs/ - documentation index

Start here. This tree holds all internal documentation; user-facing files (`README*.md`, `CHANGELOG.md`)
stay at the repo root. Structure and naming follow
[guides/REPOSITORY_LAYOUT.md](guides/REPOSITORY_LAYOUT.md).

The maintained corpus is declared in [DOCUMENT_REGISTRY.jsonl](DOCUMENT_REGISTRY.jsonl), with
exclusions in [document-registry-policy.json](document-registry-policy.json). Add a record with topic,
product area, update triggers and `source`/`render` role when adding a document. Render entries name
an automated generator. `tools/Test-DocRegistry.ps1` runs in the default test runner and checks
coverage, links, images, prose and documented source facts. Owner-local specifications are cited in
plain text so maintained documentation works in a fresh clone.

## Where things live

| Folder | Holds | Filename prefix |
| --- | --- | --- |
| [guides/](guides/) | living operational docs - read while doing the task | *(descriptive names)* |
| `specifications/` (local specification) | specs still being built | `NNN_SPECIFICATION_` |
| `specifications/done/` (local specification) | shipped specs (archive - historical, do not churn) | `SPECIFICATION_` |
| roadmaps/ | forward-looking plans - none open right now, so the folder is absent | `ROADMAP_` |
| [contracts/](contracts/) | one short **pointer** per contract this product touches, into the shared contracts catalog (see [contracts/README.md](contracts/README.md)); never a copy of the contract - a disagreement is an amendment or a dated registry exception, not a local edit | contract id (`FDSEC.md`) |
| [dev-notes/](dev-notes/) | throwaway diagnostics/captures (not authoritative) | *(none)* |
| assets/, images/ | doc & site images | - |
| privacy.html | hosted privacy policy (Store-required) | - |
| how-to-*.html | the hosted task guides (published pages; the old root addresses are forwarders, see [guides/SITE_ADDRESSES.md](guides/SITE_ADDRESSES.md)) | - |

## The meta docs (read these first for process)

- [guides/REPOSITORY_LAYOUT.md](guides/REPOSITORY_LAYOUT.md) - the portable folder/naming/secrets/
  artifacts convention, reusable across all our products.
- [guides/DOCUMENTATION_CONCEPT.md](guides/DOCUMENTATION_CONCEPT.md) - sources of truth, versioning,
  SEO/indexing, mandatory site pages, support & voice.
- [guides/PRODUCT_NAMING.md](guides/PRODUCT_NAMING.md) - the one table of the product's names (full, Store,
  short, aliases, Share Manager, Server edition), the frozen technical anchors and the naming gate.
- [guides/SITE_ADDRESSES.md](guides/SITE_ADDRESSES.md) - the not-found page, where the release notes live, and what a moved or retired page leaves behind.
- [guides/POSITIONING.md](guides/POSITIONING.md) - the one positioning source: the family sentence, the three optional components in order, the two install intents, the availability matrix, the public editions and the facts typed once (language count, platform floor); machine form `tools/site-facts.json`, gate `tools/Test-SitePositioning.ps1`.
- [guides/BUILD_AND_RELEASE.md](guides/BUILD_AND_RELEASE.md) - the concrete build-vs-release flow.
- `specifications/RELEASE_QUEUE.md` (local specification) - the common release order for active
  specification tickets. The harness's plan tool keeps it current from the ledger on every write; the built
  work is in `RELEASE_READY.md` beside it and the shipped packages are in `RELEASE_QUEUE_DONE.md`.
- [guides/STORE_PUBLISHING.md](guides/STORE_PUBLISHING.md) - the Microsoft Store playbook + listing copy.
- [guides/SERVER_EDITION_BUILD_AND_TEST.md](guides/SERVER_EDITION_BUILD_AND_TEST.md) - the always-on
  Folder Share Server: what differs from the User edition, how to build and publish it, and the
  VM matrix it has to pass (it registers a service, so it cannot be verified on the dev box).
- [guides/FILE_MANAGER.md](guides/FILE_MANAGER.md) - the integrated file manager mode in the modern viewer's Grid panel (F3): independent browsing, places list, content filters, safe opening, shared queue operations, verified ZIP creation and settings.
- [CLI.md](CLI.md) - the command line of the viewer and the Share Manager (contract `APP-COMMANDS` 0.1): every verb, option, risk class and exit code, generated from the command tables in code by `tools/Build-CliDocs.ps1`; the machine forms are `cli-commands.json` and `cli-commands.share-manager.json`, and each program prints its own with `commands --json`.
- [guides/COMMAND_LINE_RECIPES.md](guides/COMMAND_LINE_RECIPES.md) - the page for scripts: how to run a command and read its exit code, the one setting that holds the commands that work with files, screen, clipboard or network, and the schedule-a-copy and screenshot-to-file recipes; generated from the example lines of the same tables.
- [guides/NETWORK_ARCHITECTURE_AND_SCHEMES.md](guides/NETWORK_ARCHITECTURE_AND_SCHEMES.md) - the ways to connect devices (home
  network, exchange server, live broadcast): state on the Windows side, the owning contracts, what must not be claimed. The
  reader-facing explanation is the shared portal [Media exchange between your devices](https://serzhyale.github.io/FastMediaSorter_mob_v2/docs/media-exchange-portal/), not this repository.

## Conventions in one line

New doc = `TYPE_` prefix (`SPECIFICATION_`, `ROADMAP_`, `PROGRESS_`, `RESEARCH_`, `PLAN_`)
in the matching folder. A contract is never a new doc here: it lives in the shared catalog, and
`contracts/` only gains a pointer named by the contract's id. Active specifications additionally start with their three-digit sequential
ticket: `NNN_SPECIFICATION_...`; see `specifications/README.md` (local specification). A spec
that ships moves to `specifications/done/`. Text style: hyphen not em-dash, `..` not `...`, `ё` in
Russian.

## Sharing settings navigation

The [settings windows guide](guides/SHARING_SETTINGS_WINDOWS.md) owns current SFTP, local-server and remote-client navigation, shared preferences, draft behavior and help links. Earlier builds retain the combined settings window.
