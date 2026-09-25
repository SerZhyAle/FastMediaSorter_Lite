# Pointer - `APP-ACTIVATION`

| | |
| --- | --- |
| **Id** | `APP-ACTIVATION` |
| **Version** | 0.9.1, draft (read 2026-09-24). Owner: CyrFlip. **Implemented: none** - the product's transport predates the draft |
| **Home** | `app-activation/README.md` in the shared contracts catalog |
| **Role here** | consumer - single-instance forwarding in the viewer (`src/Application_Events.vb`, both exes) and the Share Manager (`src/FastMediaSorterCompanion/Program.vb`), and the viewer's wake of the Share Manager (`src/Main_Form.ShareLauncher.vb`) |

## What this repository must do to stay conformant

- The transport is `WM_COPYDATA` with a UTF-8 path or the sentinel `::fms-show-window::`, and it is part of
  the installed base: old and new builds of both viewer exes and the Share Manager must keep understanding
  each other. It is not replaced locally; the proposal to recognise it is filed beside the contract, and the
  registry carries the dated exception.
- **Every send is bounded and actually bound.** `SendMessageTimeout` with `SMTO_ABORTIFHUNG`, never plain
  `SendMessage`, declared with an explicit `EntryPoint`. The viewer's wake of the Share Manager has no entry
  point today, so it throws `EntryPointNotFoundException`, which is swallowed, and nothing is sent; fixed by
  [ticket 040](../specifications/done/040_SPECIFICATION_ACTIVATION_BOUNDED_SEND.md).
- The mutex `FastMediaSorterSingleInstanceMutex` is a frozen anchor and never takes the draft's naming
  pattern. The receiver validates the payload and never trusts it.
