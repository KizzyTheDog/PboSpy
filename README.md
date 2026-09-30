# PboSpy (Arma modding fork)

A Windows tool for opening Arma 3 PBOs and turning what's inside into things Blender and Roblox Studio can use: models, textures, sounds and configs.

This is a fork of [rvost/PboSpy](https://github.com/rvost/PboSpy) (MIT). What's changed is listed in [CHANGES.md](CHANGES.md), each commit's notes are in [DEVLOG.md](DEVLOG.md), and the to-do list is in [CONTRIBUTING.md](CONTRIBUTING.md).

## What it does

- **Explorer**: open PBOs, files and whole folders (several at once). Search hides everything that doesn't match, like Roblox Studio's explorer. Opened folders refresh when files change on disk.
- **Previews**: textures, sounds (with playback), configs and SQF with highlighting, and a **3D model preview** with textures. The preview finds `_nohq`, `_smdi` and `_as` maps through the rvmats and skips proxy triangles.
- **Model export**: GLB, glTF, FBX or OBJ with PBR materials. `_nohq` becomes a normal map and `_smdi` a metallic/roughness map. Options: max texture size, split parts at 20,000 triangles (Roblox's MeshPart limit), and a folder to look for rvmats in.
- **Convert**: PAA ↔ PNG/JPG/BMP/TGA, WSS/OGG ↔ WAV/MP3/OGG/FLAC, binarised configs and rvmats to text. The window only shows what applies to the selection.
- **P3D Tools**: debinarize ODOL (v75 included) to MLOD, extract rvmats and model.cfg (skeleton and animations), change internal paths, remove proxy triangles.
- **PBR Texture Maker**: Arma textures to base colour, normal, metallic, roughness and AO.
- **Recover scrambled names** for obfuscated PBOs. The originals can't be read back, so readable names are worked out from what references each file.
- **Presets**: buttons per game and job (Arma 3 for now) that open the right tools already set up.
- **MCP server** (`PboSpy.exe --mcp`) so Claude can list, extract, convert and export.
- **Auto-update** from this repository's releases (Settings > Updates; on by default).
- 14 languages, themes, per-window placement.

## Building

Needs the .NET 8 SDK.

```
powershell -File build.ps1 -Cli -Install
```

This builds to `..\PboSpy Built`. Run `build.bat` for the GUI builder, which has shortcut options and "Delete old ones first".

Debinarizing needs `BisDll.dll`. It isn't in this repository, because it isn't redistributable. The build takes it from your own P3D Debinarizer (`P3DDebin.exe` or a loose `BisDll.dll` next to the PboSpy folder) and puts it in `libs\BisDll`.

## Releases and updates

A release is a tag `vX.Y.Z` with `PboSpy-X.Y.Z.zip` attached, which is the build output without BisDll.dll. PboSpy checks the latest release at startup. It downloads the release in the background and asks before restarting; "No" updates when you close it.

## Credits

- PboSpy by Julien Etelain (GrueArbre) and Roman Vostrikov: MIT, see [LICENSE](LICENSE)
- [bis-file-formats](https://github.com/jetelain/bis-file-formats) by Julien Etelain, vendored in `libs/` with fixes (PBO LZSS, ODOL v75 reading)
