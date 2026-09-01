# docs/ - documentation index

Start here. This tree holds all internal documentation; user-facing files (`README*.md`, `CHANGELOG.md`)
stay at the repo root. Structure and naming follow
[guides/REPOSITORY_LAYOUT.md](guides/REPOSITORY_LAYOUT.md).

## Where things live

| Folder | Holds | Filename prefix |
| --- | --- | --- |
| [guides/](guides/) | living operational docs - read while doing the task | *(descriptive names)* |
| [specifications/](specifications/) | specs still being built | `NNN_SPECIFICATION_` |
| [specifications/done/](specifications/done/) | shipped specs (archive - historical, do not churn) | `SPECIFICATION_` |
| roadmaps/ | forward-looking plans - none open right now, so the folder is absent | `ROADMAP_` |
| [contracts/](contracts/) | frozen wire/interface contracts with other products | `CONTRACT_` |
| [dev-notes/](dev-notes/) | throwaway diagnostics/captures (not authoritative) | *(none)* |
| assets/, images/ | doc & site images | - |
| privacy.html | hosted privacy policy (Store-required) | - |

## The meta docs (read these first for process)

- [guides/REPOSITORY_LAYOUT.md](guides/REPOSITORY_LAYOUT.md) - the portable folder/naming/secrets/
  artifacts convention, reusable across all our products.
- [guides/DOCUMENTATION_CONCEPT.md](guides/DOCUMENTATION_CONCEPT.md) - sources of truth, versioning,
  SEO/indexing, mandatory site pages, support & voice.
- [guides/BUILD_AND_RELEASE.md](guides/BUILD_AND_RELEASE.md) - the concrete build-vs-release flow.
- [specifications/RELEASE_QUEUE.md](specifications/RELEASE_QUEUE.md) - the common release order for active
  specification tickets.
- [guides/STORE_PUBLISHING.md](guides/STORE_PUBLISHING.md) - the Microsoft Store playbook + listing copy.
- [guides/SERVER_EDITION_BUILD_AND_TEST.md](guides/SERVER_EDITION_BUILD_AND_TEST.md) - the always-on
  Folder Share Server: what differs from the User edition, how to build and publish it, and the
  VM matrix it has to pass (it registers a service, so it cannot be verified on the dev box).

## Conventions in one line

New doc = `TYPE_` prefix (`SPECIFICATION_`, `ROADMAP_`, `CONTRACT_`, `PROGRESS_`, `RESEARCH_`, `PLAN_`)
in the matching folder. Active specifications additionally start with their three-digit sequential
ticket: `NNN_SPECIFICATION_...`; see [specifications/README.md](specifications/README.md). A spec
that ships moves to `specifications/done/`. Text style: hyphen not em-dash, `..` not `...`, `ё` in
Russian.
