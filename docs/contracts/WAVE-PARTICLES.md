# Pointer - `WAVE-PARTICLES`

| | |
| --- | --- |
| **Id** | `WAVE-PARTICLES` |
| **Version** | 0.14, draft. Owner: FastMediaSorter Android. **Implemented: 0.14** (ticket 029 implemented the picture against 0.12 on 2026-10-01; 0.13/0.14, folded from this product's own host-signals proposal, describe what the code already does - the recorded version moved 2026-10-02) |
| **Home** | `animated-backdrop/README.md` + `reference/wave-particles.js` in the shared contracts catalog |
| **Role here** | consumer - the cover-less audio backdrop (surface class "audio player backdrop": `ambient`, `full`, no tuning; .NET 10 mainline only) and the root page's hero (class "web page hero band": `decorative`, `full`, no tuning) |

## Where it lives

- **Viewer.** Section 3's constants once, each group citing its section, in
  `src/Backdrop/WaveParticlesConstants.vb`; the rolls and the section 4 arithmetic in the pure
  `src/Backdrop/WaveParticlesSession.vb` (no `Graphics`); the Windows host signals (cloaked window, energy
  saver on battery) in `src/Backdrop/BackdropHostSignals.vb`; the drawing and the timer in
  `src/Main_Form.AudioView.vb`. Held by `WaveParticlesSessionTests` (value-by-value constants, roll ranges,
  the same second at 30/60/120/144 Hz, the ramp, rule 16 opacities, resize, the 36-pass still frame, the
  0.12 particle step, and a source scan that keeps a contract number out of the drawing file).
- **Site.** `index.html` embeds `assets/wave-particles.js`, byte-identical to the catalog's reference
  implementation (rung 2 of section 7), palette `GREEN`, wash from the page's own `--bg`. The twelve generated
  `lang/<code>/` pages carry no hero.

## What this repository does about the rules the contract leaves open

- **Clock step.** One tick covers at most 4 reference frames, the same cap the reference implementation
  applies and marks as not a contract value.
- **Rule 9 on Windows (0.13 + 0.14).** "Animation effects" is `SPI_GETCLIENTAREAANIMATION`. The system
  wins and the product's own switch may only add stillness: motion is off when **either** the system
  switch says animations are off **or** the in-app *Reduce animation* switch says so
  (`ReduceMotionActive()` - an OR in which the product's switch can turn the animation off but never on),
  the same answer the perspective halo obeys. The backdrop then shows the settled still frame. The system
  answer is read once per process.
- **Rule 11 on Windows (0.13).** `ambient`: freezes at the stricter level only - energy saver on **and**
  on battery (`GetSystemPowerStatus`, read at most once a second); it then holds its last frame.
- **Rule 10 on Windows (0.13).** Minimised, hidden and cloaked (`DWMWA_CLOAKED`) windows draw nothing and
  advance no clock; a window merely covered by another cannot be detected cheaply from WinForms and still
  draws.

## What this repository must keep doing

- A visible change to the animation is an amendment in the catalog first, never a local constant.
- The backdrop never reacts to the audio signal (rule 17).
- The served site script stays byte-identical to the reference; update it by copying, not by editing.
