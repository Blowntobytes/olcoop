# olmodcoop: co-op campaign for Overload (work in progress)

olmodcoop is an olmod add-on whose goal is to let 2-3 players play the Cronus Frontier campaign together, with the host's game in charge.
**Current state: Phase 0.** It only adds read-only logging. There is no co-op yet.

## Install (players)
Requirements: Overload, plus [olmod](https://github.com/overload-development-community/olmod) 0.5.14 in the game folder.
1. Run `install.bat`. It copies `Mod-olcoop.dll` and `olcoop.bat` into the Overload folder. If your game isn't in the
   default Steam path, set `OLPATH` first.
2. Launch with `olcoop.bat`, which runs `olmod.exe -modded`.
3. To remove it, run `uninstall.bat`, or delete `Mod-olcoop.dll`. olmod and the game files are never modified.

Options: `-coopnolog` turns logging off, and `-coopdump <sec>` changes the state-dump interval (default 5).
Logs are written to `Overload\olcoop_logs\`.

## Build (developers)
- Needs Mono `mcs`. The build references the game's own managed DLLs and the installed olmod `GameMod.dll`.
  Neither is committed or redistributed.
- `GAME_MANAGED=…/Overload_Data/Managed OLMOD_DLL=…/GameMod.dll ./build.sh` writes `build/Mod-olcoop.dll`.
- `tools/VerifyPatches.cs` checks offline that every Harmony patch target and parameter name exists in the game.
- `refs/` holds the local decompilation and is git-ignored. Never commit it.

## Docs
- `docs/architecture.md`: how SP and MP work, and the chosen co-op design.
- `docs/assumptions.md`: every single-player assumption, as a to-do list.
- `docs/recon-flow.md`, `docs/recon-world.md`: the raw findings, with file:line references.
- `tests/`: a manual test script for each phase.
- `CHANGELOG.md`

Licence: MIT, like olmod. The mod contains no Overload code or assets.
