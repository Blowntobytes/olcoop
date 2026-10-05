# Co-op campaign for Overload: olcoop!

Olcoop is an [olmod](https://github.com/overload-development-community/olmod) add-on that lets 2-3 players fly the
Cronus Frontier campaign together. The host's game runs the level (robots, doors, pickups, exits); joiners (up to 2) fly their own
ships in it. Works flat-screen and in VR.

**Status:** early alpha but playable (first public release: 0.6.0-alpha). It has been played over the internet between two PCs;
expect rough edges. Download the latest zip from [Releases](https://github.com/Blowntobytes/olcoop/releases).

## What works
- Up to 3 players in the host's campaign level, joining next to the host; joining from a saved game.
- Robots, bosses, doors, switches, keys, audio logs, lockdowns (everyone is pulled into the room), exits (everyone leaves
  together, lined up behind the player who reached the exit).
- Weapons, ammo weapons, missiles and pickups is per instance, not per player yet(WIP); upgrades and loadouts carry over to the next level.
- Death modes (CO-OP OPTIONS): Respawn next to a teammate, Spectate until the level ends, or Hardcore (any death restarts).
- Between levels: everyone gets the results and upgrade screens; story scenes are skipped; the next level starts when every
  joiner has pressed READY UP.
- Online through Steam: the host invites friends from the game; no port forwarding.

## Known issues
- Pickups don't always show up for everyone(WIP).
- INVITE THROUGH STEAM opens the Steam friends list on your desktop (the Steam overlay isn't available when the game runs
  through olmod): right-click a friend > Invite to Game. The INVITE button next to each friend on the CO-OP screen works in-game.
- Every player must run the same olcoop version; the game refuses to connect otherwise.
- Improper disconnections can cause game freezing. Either 'leave session' or 'stop hosting' for proper disconnect.

## Install (players)
Requirements: Overload on Steam, Steam running, and olmod 0.5.14 in the Overload folder. Every player needs the same olcoop version.
1. Download the release zip and unzip it anywhere.
2. Run `install.bat`. It finds your Overload folder (or asks for it), copies `Mod-olcoop.dll` and `olcoop.bat`, checks the copy,
   and puts an **olcoop** shortcut (orange and black icon) on your Desktop.
3. Start the game with the **olcoop** shortcut, or `olcoop.bat` IN THE GAME FOLDER, not the zip folder (not Steam's Play button,
   which starts the vanilla game).
4. Main menu, bottom right: **CO-OP: HOST / JOIN**.
   - Host: HOST A CO-OP GAME, invite friends from the list, then BACK and start or continue the campaign.
   - Friend: accept the host's Steam invite while the game is running, or open CO-OP and pick the friend who is hosting.
5. To remove it, run `uninstall.bat`. olmod and the game files are never modified.

Same-PC testing (developers): `installer/olcoop-host.bat` and `installer/olcoop-join.bat [ip]` start a host and joiners that
connect over UDP port 7777.
Options: `-coopnolog` turns logging off, `-coopdump <sec>` sets the state-dump interval. Logs: `Overload\olcoop_logs\`.

## Build (developers)
- Needs Mono `mcs`. The build references the game's own managed DLLs and the installed olmod `GameMod.dll`; neither is
  committed or redistributed.
- `GAME_MANAGED=…/Overload_Data/Managed OLMOD_DLL=…/GameMod.dll ./build.sh` writes `build/Mod-olcoop.dll`;
  `./package.sh` then writes the player zip `dist/olcoop-<version>.zip` (mod DLL + `installer/` files).
- `tools/VerifyPatches.cs` checks offline that every Harmony patch target and parameter name exists in the game.
- `refs/` holds the local decompilation (`tools/ildump.py`) and is git-ignored. Never commit it.



## Docs
- `HANDOFF.md`: how the mod works, version by version, and the working rules.
- `BUILDS.md`, `CHANGELOG.md`: every build and what changed.
- `docs/`: design notes and research. `tests/`: manual test scripts and results.

## Credits
- The olmod team and contributors at the [Overload Development Community](https://github.com/overload-development-community/olmod):
  olcoop is built on olmod and would not exist without it.
- olcoop maintained by Blowntobytes ([github.com/Blowntobytes](https://github.com/Blowntobytes)).

Licence: MIT, like olmod. The mod contains no Overload code or assets.
