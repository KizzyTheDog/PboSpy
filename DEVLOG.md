# Dev log

Newest first. Each commit adds an entry: what changed, why, and what was checked.

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
