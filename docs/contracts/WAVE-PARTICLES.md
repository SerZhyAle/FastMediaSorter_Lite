# Pointer - `WAVE-PARTICLES`

| | |
| --- | --- |
| **Id** | `WAVE-PARTICLES` |
| **Version** | 0.10, draft (read 2026-09-24). Owner: FastMediaSorter Android. **Implemented: none** - a look-alike |
| **Home** | `animated-backdrop/README.md` + `reference/wave-particles.js` in the shared contracts catalog |
| **Role here** | consumer - the cover-less audio backdrop `PaintAudioVisualiser` in `src/Main_Form.AudioView.vb` (.NET 10 mainline only) and the root page's hero; audit in [specification 029](../specifications/029_SPECIFICATION_CONTRACT_SYNC_ANIMATED_BACKDROP.md) |

## What this repository must do to stay conformant

- The constants of section 3 live in one pure, tested place that cites `WAVE-PARTICLES section 3`, and
  opacity goes through them - today the `x0.70` factor is missing and the backdrop draws brighter.
- Time advances by elapsed time, not by timer ticks; a resize keeps the session; reduced motion gives a
  still frame; the backdrop never reacts to the audio signal (held).
- The site hero is replaced by the catalog's reference implementation rather than ported a third time.
