# PboSpy 2.0 changes

## Removed or replaced

| What | Why / what replaces it |
|---|---|
| About page opening as the default document | Replaced by the Start page. Closing it asks once whether to keep showing it. |
| Gemini's "General" settings page | It saved a stale theme name over the one you picked, so the theme couldn't be changed back. Its options now live in PboSpy's own settings page. |
| Settings stored in `user.config` (Properties.Settings) | Tied to the exe path, so settings were lost on every rebuild or move. Replaced by `%AppData%\PboSpy\settings.json`. |
| Dock layout file next to the exe | Moved to `%AppData%\PboSpy\layout.bin` (a relative path gave an empty layout depending on the start folder). |
| Modal export and convert dialogs | They blocked the main window. They're now normal windows you can keep open. |
| BisDll's own rvmat writer (P3D Tools) | Wrote `Stage01`/`Stage11`, unquoted strings and `PS`/`VS` shader prefixes that Arma won't read. Replaced by PboSpy's own writer. |
| Python requirement of the "aundrei pbr" scripts | Rebuilt in C# inside PboSpy (PBR Texture Maker). PNG pre-conversion and batch files are no longer needed. |
| Hard-coded light background in text and model previews | Text previews (SQF, configs, rvmats…) and the P3D details view now follow the theme. |
| Extract/Open items that don't apply in the explorer's right-click menu | Hidden for items they can't act on (for example Extract PBO on a PNG). |
| .NET 6 target | Now .NET 8. |

## Added

- **Single click / double click previews.** A single click shows the file in one reusable preview tab (title ends in "(preview)"); a double click opens it in its own tab and switches to it. Turn it off in Settings.
- **3D model preview.** P3D files open in a 3D view with textures: LOD picker, rotate/pan/zoom, texture list showing where each texture was found. Textures come from opened PBOs, folders around the model and a texture folder you can set (for example your P: drive). Missing ones can be picked by hand with "…". Right-click a P3D and choose "Open model preview".
- **Rename.** F2 or right-click > Rename. Files and folders on disk are renamed for real. Files inside a PBO keep their stored name, and the new name is used when exporting, extracting or dragging out (shown in italics).
- **Recover scrambled names** (right-click an obfuscated PBO). Works out readable names from configs, models, materials and scripts, and can extract the PBO with those names while fixing the references inside configs, rvmats, scripts and models.
- **MCP server.** `PboSpy.exe --mcp` lets Claude Desktop list, read, extract and convert PBOs, textures, models and sounds. Settings > Claude (MCP) adds it to Claude Desktop for you (or copies the config).
- Obfuscated (UTF-8) file names are shown as real letters instead of mojibake.

## Fixed

- Sound preview: pressing Play did nothing (the position timer was created on a worker thread and the float output was refused by some drivers).
- Text previews always white with black text whatever the theme.

## 2026-09-29

### Added

- **Presets** (Tools > Presets…, Ctrl+Shift+G). Pick a game, then buttons per job open the right tools already set up: Arma 3 has "Import a vehicle into Blender" in order (open PBO, recover names, debinarize, PAA → PNG, PBR maps), Models, Textures, Sounds and Configs. Microsoft Flight Simulator 2024 is listed as planned.
- **Explorer search filters the tree** like Roblox Studio: only matches and the folders leading to them stay visible, and those folders open. Clearing the search folds the tree back.
- **Hide from the explorer** on folders (right-click). Takes the folder out of the tree, nothing is touched on disk.
- **Convert window: edit the inputs.** Add files…, Add folder… and Remove (or Delete key). Files and folders from different places can be mixed; each folder keeps its own structure.
- **3D preview: linked maps.** Each material's rvmat is read (or files named like the texture are used) to find `_nohq`, `_smdi` and `_as`. The texture list shows what was linked. With "Detail maps" on, the normal map is baked in as lighting and `_smdi` gives the shine (WPF 3D can't do real normal mapping). See-through `_ca` parts draw last so they don't hide what's behind them.
- **3D preview: Blender-style cursor wrap.** Dragging past an edge of the view carries on from the opposite edge.
- **Texture lookup** also tries names as they look after extraction (percent-encoded, or `_` for `*`/`?`).
- **Builder: "Delete old ones first".** Removes shortcuts and right-click entries from earlier builds, including shortcuts that point at a PboSpy.exe in an old folder, before making new ones.
- Blender addon (auto_texture_linker 1.8.0): **Split Meshes Over Limit** (default 20,000 triangles, Roblox's MeshPart limit). Keeps whole loose parts together, packs them by position, and cuts a single too-big part in half until it fits.

### Changed

- **3D preview is faster**: normals are worked out in the background instead of on the UI thread when WPF first draws; decoded textures are shared between previews (up to 384 MB); the last 4 parsed models and their meshes are kept, so switching back to one is instant; PBO entry lists are indexed once per PBO.
- Convert window title says "Convert Files" or "Convert Folder" depending on what opened it.
- Opening a folder only adds the folder to Recent, not every file under it.
- Blender addon: Remove Proxy Triangles now recognises ArmaToolbox's `@@armaproxy` groups (it only knew `proxy:` before, so it fell back to guessing by shape). A mesh that is only proxy triangles is deleted as an object. The emptied proxy groups and ArmaToolbox's proxy list are cleared. "Keep positions as empties" is now off by default and the empties are parented to their mesh.

### Fixed

- Changing the output folder in the Convert window sent it behind the main window (looked like it closed). The folder picker now belongs to the Convert window.
- Opening a folder expanded every subfolder (the inherited theme style did it).
- Build error: ambiguous `Action` in the 3D preview.

### Removed

- Nothing removed.

## 2026-09-30

- Added: Convert window "Select the file(s) when done", next to "Open the folder when done". Greyed out (keeps its tick) while the folder option is off. Selects what the run wrote in the folder that got most of it.
- Fixed: opening a file from Windows Explorer (or P3D Tools' "Open in PboSpy") could open both a "(preview)" tab and a normal tab for it.
- Changed: opening a folder again removes the old per-file Recent entries under it.
- Added: **Export OBJ…** in the 3D preview. Saves the shown LOD (proxy triangles left out) as OBJ + MTL for Blender, with the colour, normal (`map_Bump`) and `_smdi` (`map_Ks`) textures as full size PNGs in `<name>_textures`. `_ca` textures also go in as alpha.
- Checked: RTM files extract with a valid `BMTR` header now. Their "obfuscation" was the LZSS bug; there's nothing else to undo.

### Checked

- ODOL v75 (cca.p3d): debinarizes to MLOD, model.cfg extraction gives the skeleton (255 bones) and 282 animation classes. Every bone that has geometry has its named selection in the MLOD; the 11 without are bones with no geometry in the original either.

## 2026-09-30 (later)

- Added: **Export model** (3D preview button, right-click .p3d > P3D > Export as GLB, MCP `p3d_export`). GLB with PBR materials: colour texture, `_nohq` turned into a plain normal map (DXT5nm unpacked, green flipped), `_smdi` turned into a metallic/roughness map (same maths as the PBR Texture Maker), `_ca` as alpha blend. OBJ + MTL + PNGs as the other choice. Textures capped at 2048 and shared between materials. Checked: cca.p3d imports into Blender with all 224,208 triangles and normal maps on 22 of 28 materials.
- Added: opened folders **refresh by themselves** when files are added, deleted or renamed on disk (expanded folders stay as they are; hidden folders stay hidden).
- Added: **Open Folder** can pick several folders at once.
- Changed: the Convert window only shows the sections that apply to what's in it, and ticks their conversion (not when a preset already chose). Its background and input list now follow the theme.
- Changed: the 3D preview's texture search also scans the folders around the model (up to two levels up) and matches names loosely (case, extension, separators, percent-encoding), like the Blender addon.
- Changed: `(argb…)`/`(rgb…)` textures count as procedural even without the leading `#`, so they don't show as missing.
- Fixed: debinarizing several .p3d files from the explorer wrote the results into a temp folder; they now go to the files' own folder.

## 2026-09-30 (export options)

- Added: **Export model window** (3D preview "Export model…", right-click .p3d > P3D > Export model). Choose:
  - format: GLB, glTF (separate .bin + PNGs), FBX (binary 7.4) or OBJ. All four import into Roblox Studio and Blender.
  - max texture size (512 to 8192; Roblox uses up to 1024)
  - split parts over N triangles (default 20,000, Roblox's MeshPart limit); pieces are cut along the part's longest side
  - an RVMAT folder searched first for materials, and the texture folder
  Settings are remembered. MCP `p3d_export` takes `max_texture`, `split_at` and `rvmat_folder`.
  Checked on cca.p3d in Blender: all four formats import with 224,208 triangles, 35 objects, none over 20,000, same size and orientation.
- Added: TGA as an image output format in Convert (Roblox and Blender read it). With PNG, JPG and BMP, and WAV/MP3/OGG/FLAC for audio, every image and sound type Roblox imports is covered.

## v2.1.1

- **Export material names match the textures** (`exterior_misc2_co` instead of `m01_exteriormisc2co`), scrambled names decoded. The Blender Auto Texture Linker matched nothing before because of those names; UVs were never changed by the split. Normal maps are written as `<name>_normal`, shine as `<name>_orm`.
- **Less shiny exports**: `_smdi` green was used as "metallic", which made most surfaces black chrome. Metallic is now 0 and `_smdi` only lowers roughness (floor 0.3).
- **Scrambled texture names** are also looked up decoded, the way they're spelled once extracted.
- **3D preview view modes** like Blender: Wireframe, Solid, Material, Rendered (remembered). They replace the Textures and Detail maps checkboxes.
- **FPS and stats** bottom right of the 3D preview (FPS, triangles shown, parts, textures, memory). Settings > "Show FPS and model stats", on by default.
- Checked: PGS_MH47G Block_1 exports to GLB with 56 of 57 materials textured (the other is `empty_ca`, blank by design); wireframe and stats on cca.p3d.

- Removed: the "Textures" and "Detail maps" checkboxes in the 3D preview (replaced by the view modes).

## v2.1.2

- The updater could stall mid-download: it started `gh` with its error output redirected but never read it, `gh` writes progress there, and once that pipe was full `gh` waited forever (found testing 2.1.0 → 2.1.1: the zip stopped at 1.4 of 5.1 MB).
- Now downloads directly from GitHub (the repo is public), with `gh` only as a fallback, and reads both of `gh`'s outputs.
- A half-finished earlier download is thrown away instead of reused.
- 2.1.0 and 2.1.1 have the broken updater, so those need 2.1.2 installed by hand once.

## v2.1.3

- The real reason updates never arrived: GitHub's file host was slow from this PC (about 140 KB/s, one 5 MB request took over 2 minutes), and the download gave up at HttpClient's default 100 s. That error wasn't the kind the fallback caught, so it failed silently.
- It now streams to a `.part` file with a 30-minute limit, uses the normal release download link, and any failure falls back to `gh`.

## v2.1.4

- **Triangles were wound backwards** in the preview and exports: mirroring Arma's left-handed Z flips facing, and drawing both sides hid it. Normals pointed inwards too, so lighting (and exported shading) was off. Fixed in the mesh builder.
- **Back faces aren't drawn any more** (the game culls them too), except on see-through `_ca` parts. That halves the drawing work.
- **Wireframe like Blender's**: thin lines drawn by the GPU from a texture on every triangle, and the model hides the edges behind it. It replaces the thick per-edge strips, and costs nothing per frame.
- **FPS counter**: listening for frames made WPF redraw non-stop, which cost performance and read about 800 FPS. It now counts only real frames while the camera moves; `*` means the number is from the last movement.
- **Less stutter**: texture list thumbnails are made once at 64 px in the background (they were full textures scaled on every layout pass). More textures decode in parallel, and the model is built off-screen and added in one go.
- Checked on cca.p3d: rendered view closed and solid, wireframe with hidden lines.

## v2.1.5

- **Scrambled texture names now match when a model is opened straight from its PBO.** The name was lowercased before being decoded, which corrupted it, so an obfuscated Chinook found only 10 of 48 textures (47 of 48 once extracted). Now 48 of 48. "Show models using this texture" had the same bug.
- **Base-game textures and materials** (`a3\...`, e.g. glass and light rvmats) are read from your installed Arma 3's PBOs (found through the registry).
- **Placeholders the game draws as nothing** (`empty`, `clear_empty`, the clan logo slot `bis_klan`) are hidden in the preview and left out of exports, instead of showing as missing.
- MCP `p3d_export` takes a model inside a PBO (`entry`) and reports which textures and maps it found.
- Checked: C-130J 17/17, CCA 26/26, Chinook from the PBO 48/48 and from the folder 48/48.

## Unreleased (after 2.1.5)

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
- Removed: the GPU texture wireframe (WireMesh/WireLines/WireFill) and the 90° orbit limit.
- **RTM rigs**: pick the body the animation plays on (body, NATO, CSAT, civilian, pilot, each with a head, from your Arma 3 install) or add your own binarised .p3d; the choice is remembered.
- **Steam Workshop** (Tools menu): every Arma 3 workshop item on this PC (Steam subscriptions, named from meta.cpp or the !Workshop links, and steamcmd downloads) with a filter; download by link or ID with steamcmd in its own window. Opening asks to copy the item to DownloadsPboSpy Workshop first.
- **LOD copies** in the export window: extra files at e.g. 50 / 25 / 10 % of the triangles (name_lod1, name_lod2…), same textures.
- **Presets** for Debinarize, model.cfg and Strip proxies run the action instead of only opening P3D Tools.
- **Tidy script** (Edit menu, Ctrl+Shift+T): re-indents by brackets and drops padding blank lines, in the view only.
- **Test mode** (`--test`): off-screen, no focus or taskbar button, own settings, runs next to a normal PboSpy; driven through %TEMP%PboSpyTest. Windows no longer force themselves to the front in that mode.
- FPS stats moved to the top right of the 3D view.
- Checked in test mode: Workshop lists 82 items with names, wireframe, outline (12 LODs, meshes per LOD); LOD export (CCA 10 / 6 / 3.2 MB); Tidy self-check.
