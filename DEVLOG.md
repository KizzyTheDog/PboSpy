# Dev log

Newest first. Each commit adds an entry: what changed, why, and what was checked.

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
