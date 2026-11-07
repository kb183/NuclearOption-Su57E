# Su-57 development conventions

- This repository is a source snapshot of the Nuclear Option Su-57 MOD, version 0.2.34.
- The maintainer's active build source remains outside this export. Do not treat this snapshot as a replacement without an explicit migration request.
- Validation is owned by the maintainer. Do not run validation scripts, launch Unity or the game, or start automatic flight tests unless explicitly asked. Required packaging is allowed; do not describe packaging as game acceptance.
- Deliver exactly one Su-57 installation file: Builds/SU57/Install/Su-57.dll. Embed aircraft resources and runtime animation code together. Keep the existing general Blueprinter loader.
- Keep game assemblies, _donotship, Broken Arrow models/textures, other mods, cached dependencies and local reports out of Git. See docs/ASSETS_ZH.md.
- Preserve Unity .meta files for tracked source assets.
- Use project-relative paths and documented environment variables; do not introduce personal drive paths.
- The 0.2.34 DLL currently reuses 0.2.32 aircraft resources. Its seam display and spawn fixes await game acceptance. Historical 0.2.2 flight results do not validate the current version.
