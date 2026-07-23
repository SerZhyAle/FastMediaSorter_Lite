# docs/ - documentation index

Start here. This tree holds all internal documentation; user-facing files (`README*.md`, `CHANGELOG.md`)
stay at the repo root. Structure and naming follow
[guides/REPOSITORY_LAYOUT.md](guides/REPOSITORY_LAYOUT.md).

## Where things live

| Folder | Holds | Filename prefix |
| --- | --- | --- |
| [guides/](guides/) | living operational docs - read while doing the task | *(descriptive names)* |
| [specifications/](specifications/) | specs still being built | `SPECIFICATION_` |
| [specifications/done/](specifications/done/) | shipped specs (archive - historical, do not churn) | `SPECIFICATION_` |
| [roadmaps/](roadmaps/) | forward-looking plans | `ROADMAP_` |
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
- [guides/STORE_PUBLISHING.md](guides/STORE_PUBLISHING.md) - the Microsoft Store playbook + listing copy.

## Conventions in one line

New doc = `TYPE_` prefix (`SPECIFICATION_`, `ROADMAP_`, `CONTRACT_`, `PROGRESS_`, `RESEARCH_`, `PLAN_`)
in the matching folder. A spec that ships moves to `specifications/done/`. Text style: hyphen not
em-dash, `..` not `...`, `ё` in Russian.
