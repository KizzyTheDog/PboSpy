# Dev log

Newest first. Each commit adds an entry: what changed, why, and what was checked.

## 2026-10-01 · v2.2.1 · textured rigs, find crash fix

- **Textured rigs** in the RTM preview: each rig shows its game textures (uniform, boots, face), with a Textures toggle in the toolbar (on by default, remembered). Blank slots fall back to the model's own data\<name>_co.paa.
- **Fixed: Find in scripts crashed PboSpy** as soon as you typed (parallel search set up in the wrong order).
- **Fixed: base-game textures could go missing** when several were looked up at once (the game PBO index was searched while still being built). Hit the rig's head; could also hit models using a3\ textures.
- Find results show the last three folders of each path instead of the whole disk path.
- Checked in test mode: NATO soldier idle fully textured including the head; searching the Chinook config finds 94 results.

## 2026-10-01 · v2.2.0 · RTM playback fixed

- **RTM playback fixed**: binarised animations are stored per bone relative to the parent, with the rotation conjugated and turned 180° about Y. Found by converting the walk with Mikero's DeRtm and matching every bone; the body, arms and hands now pose correctly.
- Text RTMs that start with an RTM_MDAT block (as DeRtm writes them) now open.
- Mikero's free Linux tools (DeRtm etc.) are set up in WSL Ubuntu for checking formats.
- Checked in test mode: walk on the body and on the NATO soldier rig.

## 2026-10-01 · v2.2.0 · rigs, workshop, LOD export, test mode

- **RTM rigs**: pick the body the animation plays on (body, NATO, CSAT, civilian, pilot, each with a head, from your Arma 3 install) or add your own binarised .p3d; the choice is remembered.
- **Steam Workshop** (Tools menu): every Arma 3 workshop item on this PC (Steam subscriptions, named from meta.cpp or the !Workshop links, and steamcmd downloads) with a filter; download by link or ID with steamcmd in its own window. Opening asks to copy the item to Downloads\PboSpy Workshop first.
- **LOD copies** in the export window: extra files at e.g. 50 / 25 / 10 % of the triangles (name_lod1, name_lod2…), same textures.
- **Presets** for Debinarize, model.cfg and Strip proxies run the action instead of only opening P3D Tools.
- **Tidy script** (Edit menu, Ctrl+Shift+T): re-indents by brackets and drops padding blank lines, in the view only.
- **Test mode** (`--test`): off-screen, no focus or taskbar button, own settings, runs next to a normal PboSpy; driven through %TEMP%\PboSpyTest. Windows no longer force themselves to the front in that mode.
- FPS stats moved to the top right of the 3D view.
- Checked in test mode: Workshop lists 82 items with names, wireframe, outline (12 LODs, meshes per LOD); LOD export (CCA 10 / 6 / 3.2 MB); Tidy self-check.

## 2026-10-01 · v2.2.0 · big feature batch

- **Wireframe like Blender's X-ray**: drawn as a 2D overlay, faces invisible, every edge once as a 1 px line, overlaps brighter; redrawn at most once per frame. **Dense areas** button colours edges by triangle size.
- **Decals**: see-through textures no longer get a borrowed shine map (that painted their backgrounds grey). Plain white placeholder slots (decals, tail numbers filled in by scripts) are hidden like in game.
- **Pick from unused textures…** in a texture's menu: searchable list with thumbnails of textures near the model it doesn't use yet. Works on placeholder slots too; picks can point inside PBOs.
- **Walk navigation** (Shift+`): WASD/arrows, Q/E, mouse look, Shift/Alt speed, wheel; click/Enter keeps, Esc/right click goes back.
- No up/down limit when orbiting; panning follows the camera's up direction.
- **Export**: "Split" (on), "One object per material" (on) and "Decimate to N%" (off, meshoptimizer) at the top of the export window; MCP `p3d_export` gets `decimate` and `by_material`.
- **Find in scripts** (Ctrl+F outside a script, Tools menu): searches every script and config in the opened PBOs and folders, click to open at the line.
- **Explorer outline**: .p3d files expand into their LODs and meshes (texture, material, triangles), loaded on expand; double click opens that LOD.
- **RTM preview**: Animation tab plays it on the game's body (read from your Arma 3 install) with a dropdown of every .rtm in the folder; Details tab lists bones, frames and times. Binarised playback is still wrong on the arms (the format's convention isn't documented).
- **texHeaders.bin** shows as a readable table (size, format, mipmaps, alpha, average colour, path).
- Volume slider: 0–200%, mark at 100% that it snaps to, shows the percentage, remembered between files.
- Checked: export in Blender (CCA 50% decimate: 112k tris, 7 joined objects all under 20k, 28/28 textured; FBX multi-material works). Not yet checked in the app: wireframe, walk, find, outline, picker.

## 2026-10-01 · v2.1.5 · texture matching fixes

- **Scrambled texture names now match when a model is opened straight from its PBO.** The name was lowercased before being decoded, which corrupted it, so an obfuscated Chinook found only 10 of 48 textures (47 of 48 once extracted). Now 48 of 48. "Show models using this texture" had the same bug.
- **Base-game textures and materials** (`a3\...`, e.g. glass and light rvmats) are read from your installed Arma 3's PBOs (found through the registry).
- **Placeholders the game draws as nothing** (`empty`, `clear_empty`, the clan logo slot `bis_klan`) are hidden in the preview and left out of exports, instead of showing as missing.
- MCP `p3d_export` takes a model inside a PBO (`entry`) and reports which textures and maps it found.
- Checked: C-130J 17/17, CCA 26/26, Chinook from the PBO 48/48 and from the folder 48/48.

## 2026-10-01 · v2.1.4 · faster preview, real wireframe, winding fix

- **Triangles were wound backwards** in the preview and exports: mirroring Arma's left-handed Z flips facing, and drawing both sides hid it. Normals pointed inwards too, so lighting (and exported shading) was off. Fixed in the mesh builder.
- **Back faces aren't drawn any more** (the game culls them too), except on see-through `_ca` parts. That halves the drawing work.
- **Wireframe like Blender's**: thin lines drawn by the GPU from a texture on every triangle, and the model hides the edges behind it. It replaces the thick per-edge strips, and costs nothing per frame.
- **FPS counter**: listening for frames made WPF redraw non-stop, which cost performance and read about 800 FPS. It now counts only real frames while the camera moves; `*` means the number is from the last movement.
- **Less stutter**: texture list thumbnails are made once at 64 px in the background (they were full textures scaled on every layout pass). More textures decode in parallel, and the model is built off-screen and added in one go.
- Checked on cca.p3d: rendered view closed and solid, wireframe with hidden lines.

## 2026-09-30 · v2.1.3 · updater download timeout

- The real reason updates never arrived: GitHub's file host was slow from this PC (about 140 KB/s, one 5 MB request took over 2 minutes), and the download gave up at HttpClient's default 100 s. That error wasn't the kind the fallback caught, so it failed silently.
- It now streams to a `.part` file with a 30-minute limit, uses the normal release download link, and any failure falls back to `gh`.

## 2026-09-30 · v2.1.2 · updater fix

- The updater could stall mid-download: it started `gh` with its error output redirected but never read it, `gh` writes progress there, and once that pipe was full `gh` waited forever (found testing 2.1.0 → 2.1.1: the zip stopped at 1.4 of 5.1 MB).
- Now downloads directly from GitHub (the repo is public), with `gh` only as a fallback, and reads both of `gh`'s outputs.
- A half-finished earlier download is thrown away instead of reused.
- 2.1.0 and 2.1.1 have the broken updater, so those need 2.1.2 installed by hand once.

## 2026-09-30 · v2.1.1 · export fixes, viewport modes

- **Export material names match the textures** (`exterior_misc2_co` instead of `m01_exteriormisc2co`), scrambled names decoded. The Blender Auto Texture Linker matched nothing before because of those names; UVs were never changed by the split. Normal maps are written as `<name>_normal`, shine as `<name>_orm`.
- **Less shiny exports**: `_smdi` green was used as "metallic", which made most surfaces black chrome. Metallic is now 0 and `_smdi` only lowers roughness (floor 0.3).
- **Scrambled texture names** are also looked up decoded, the way they're spelled once extracted.
- **3D preview view modes** like Blender: Wireframe, Solid, Material, Rendered (remembered). They replace the Textures and Detail maps checkboxes.
- **FPS and stats** bottom right of the 3D preview (FPS, triangles shown, parts, textures, memory). Settings > "Show FPS and model stats", on by default.
- Checked: PGS_MH47G Block_1 exports to GLB with 56 of 57 materials textured (the other is `empty_ca`, blank by design); wireframe and stats on cca.p3d.

## 2026-09-30 · repository live

- Private repo created at KizzyTheDog/PboSpy and release **v2.1.0** published (`PboSpy-2.1.0.zip`, 5.1 MB, without BisDll.dll or .pdb files).
- Updater: finds `gh.exe` in its install folder too, because apps started before gh was installed don't see it on PATH.
- Checked: `gh api repos/KizzyTheDog/PboSpy/releases/latest` returns v2.1.0 with the zip asset.

## 2026-09-30 · v2.1.0 · first commit of the fork

**State at the start of the repository**, after several sessions of work (details in CHANGES.md):

- 3D preview with texture linking (rvmat stages, `_nohq`/`_smdi`/`_as`), cursor wrap, caching, proxy triangles skipped
- Model export to GLB / glTF / FBX / OBJ with PBR maps, max texture size, 20k-triangle split, rvmat folder
- Explorer: Roblox-style search filter, auto-refresh of opened folders, open several folders, hide folders, clean Recent list
- Convert window: editable inputs, only relevant sections, "select the files when done", TGA output
- P3D Tools, PBR Texture Maker, name recovery, Presets window, MCP server, 14 languages
- **New: auto-updater** (Settings > Updates, on by default). It checks this repository's latest release at startup, downloads the zip, then asks to restart. If you say no, the files are swapped in when PboSpy closes. BisDll.dll and settings are never touched.

**Checked:** release build; cca.p3d (ODOL v75) exports to all four formats and imports into Blender at the right size, with no part over 20,000 triangles.

**Repository notes:**
- `libs/bis-file-formats` is vendored (it has our fixes) instead of being a submodule. The upstream `.git` is kept locally as `.git_upstream` and ignored.
- The upstream `release.yml` workflow was taken out; it would have fired on our `v*` tags.
- `BisDll.dll`, `.claude_tmp/` and the build output are ignored.
