# Su-57 for Nuclear Option

[简体中文](README.md) · [Installation](docs/INSTALLING_ZH.md) · [Build setup](docs/BUILDING_ZH.md)

An unofficial Su-57 aircraft mod for Nuclear Option, built with Blueprinter and BepInEx. It combines a Broken Arrow Su-57 exterior with the game's native KR-67 flight-system baseline.

**Version 0.2.34 — model refinement in progress.** The existing DLL is compiled; seam visibility, spawning collisions, damage separation and repairs await maintainer acceptance in the game. Repository preparation did not rebuild or test the mod.

## Features

- Independent `SU57` aircraft registration, rank 4, value 100 million, and shelter integration.
- Three LODs, twelve moving flight surfaces, thrust vectoring and staged landing-gear motion.
- Five liveries, transparent canopy and custom surface materials.
- 37 visible sections and 34 physical parts with custom collision shells and fracture-face display.
- Nine weapon stations, main and side weapon-bay doors using native weapon systems.
- One install file, `Su-57.dll`, embedding aircraft resources and runtime patches.

The prototype does not claim real-world Su-57 flight performance.

## Installation

Install Nuclear Option, BepInEx and the general Blueprinter loader first. The existing development environment uses Nuclear Option 0.34.6 and Blueprinter 2.1.1; compatibility with other versions is unconfirmed.

Download `Su-57.dll` from this repository's Releases and place it at `BepInEx/plugins/Su-57/Su-57.dll` inside the game directory while the game is closed. Keep Blueprinter installed. Remove old loose Su-57 `.nobp` files, `SU57.GearMotion.dll` and duplicate Su-57 plugins when upgrading.

## Development

This repository contains custom code, Unity settings and authoring tools. **It does not redistribute aircraft models, textures, extracted data or game assemblies. A clean clone requires local assets before a complete build is possible.**

Use Unity **2022.3.62f2**, follow the [upstream Blueprinter setup guide](https://github.com/nikkorap/NOBlueprinter-Editor/blob/6a9b43c8f245b27538700810ea769b79736d277d/GUIDE.md), then restore Su-57 authoring assets from an existing project:

```powershell
.\Tools\import_local_assets.ps1 -SourceProject 'X:\YourAuthoringProject'
```

Configure the installed game executable in `Blueprinter > Project Setup`, or set `SU57_GAME_DIR` before starting Unity. Select `Blueprinter > Su-57 > Build and Validate`. Only `Builds/SU57/Install/Su-57.dll` is installed in the game.

The Blueprinter package is pinned to commit `6a9b43c8f245b27538700810ea769b79736d277d`. No local MCP package, cached tooling or game binaries are required from Git.

The existing 0.2.34 DLL reuses 0.2.32 embedded aircraft resources. A new complete build will produce matching code and resource versions. Historical unarmed flight runs for 0.2.2 do not establish acceptance of 0.2.34. Manual handling, weapon behavior and multiplayer remain unverified.

See [build details](docs/BUILDING_ZH.md), [asset boundaries](docs/ASSETS_ZH.md), [changes](CHANGELOG.md) and [contribution guidelines](CONTRIBUTING.md). Documentation beyond this page is primarily Chinese.

## License

Custom code and project documentation are [MIT licensed](LICENSE). Blueprinter attribution is preserved in [its upstream MIT notice](LICENSES/Blueprinter-MIT.txt). Third-party models, textures and game content are excluded from this license; see [third-party notices](THIRD_PARTY_NOTICES.md).
