# Contributing and to do

## How to help

- **Bugs and ideas**: open an issue. Say what file you opened (PBO, P3D, texture…), what you did and what happened.
- **Code**: fork, make the change on a branch, open a pull request. Build with `powershell -File build.ps1 -Cli` (.NET 8 SDK).
- **Translations**: the language files are `src/PboSpy/Localization/Languages/*.json`. Fixes from native speakers are very welcome.
- Don't commit `BisDll.dll`; it isn't redistributable.

## To do

Open work, roughly in order. Done items move to [DEVLOG.md](DEVLOG.md) and [CHANGES.md](CHANGES.md).

### Features

- [ ] **Roblox `.mesh` export**, with the 20k-triangle split
- [ ] **Convert window**
  - [ ] Default output folder next to the source, and a remembered per-folder override
  - [ ] Option to write each output next to its own source (inputs from different places)
  - [ ] After converting: delete the originals, or move them into a backup folder that mirrors the structure
  - [ ] Pick which input types each section converts
- [ ] **Presets**: have buttons run the action (e.g. Debinarize) instead of only opening the tool; presets for Microsoft Flight Simulator 2024
- [ ] **3D preview**
  - [ ] Real normal mapping (needs a DirectX renderer; WPF 3D can't do it, so `_nohq` is baked in as lighting for now)
  - [ ] Use `_as` ambient maps (they use the second UV set)
  - [ ] Keep parsed models/textures on disk so the first open after a restart is fast too
- [ ] **ImageSharp upgrade** from 2.1.13 (note: v3+ uses the Six Labors Split License)
- [ ] **Tidy SQF** action: strip the blank lines obfuscators leave, normalise indentation
- [ ] **Name recovery**: fewer `unnamed_###` fallbacks; show the new names in the config preview once applied

- [ ] **Wireframe like Blender** (X-ray, 1 px lines) with a "dense areas" heatmap
- [ ] **Faster loading of huge models** (400 MB+ P3Ds)

### Ideas

- [ ] RTM preview (bones, frames, duration)
- [ ] Search / diff configs across all opened PBOs
- [ ] WRP (terrain) info
- [ ] Signature / bikey checker

### Needs testing in real use

- [x] Auto-update end to end (2.1.0 → 2.1.3: downloaded, swapped, restarted)
- [ ] 3D preview cursor wrap while dragging
- [ ] Export window (GLB / glTF / FBX / OBJ) from the UI, and importing the results into Roblox Studio
- [ ] Convert window: Add files / Add folder / Remove, "Select the file(s) when done"
- [ ] Presets window, Hide from the explorer, builder "Delete old ones first"
- [ ] Translations checked by native speakers
