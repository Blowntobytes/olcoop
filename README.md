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
  A weapon one player picks up is given to every player.
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
- Challenge co-op (new): the "INCOMING SUPER AUTO-OP" warning only shows for the host. Co-op challenge scores are not sent to the Steam leaderboards.
- Over long-distance connections robots lag behind on joiners' screens (they are shown about 0.1 s behind the host plus the ping).

## Install (players)
Requirements: Overload on Steam, Steam running, and olmod 0.5.14 in the Overload folder. Every player needs the same olcoop version.
1. Download the release zip and unzip it anywhere.
2. Run `install.bat`. It finds your Overload folder (or asks for it), copies `Mod-olcoop.dll` and `olcoop.bat`, checks the copy,
   and puts an **olcoop** shortcut (orange and black icon) on your Desktop.
3. Start the game with the **olcoop** shortcut, or `olcoop.bat` IN THE GAME FOLDER, not the zip folder (not Steam's Play button,
   which starts the vanilla game).
   VR: SteamVR headsets - start SteamVR, then **olcoop SteamVR** (`olcoop-steamvr.bat`, `-vrmode openvr`); Oculus/Meta
   headsets - start the Oculus app, then **olcoop Oculus** (`olcoop-oculus.bat`, `-vrmode oculus`).
4. Main menu, bottom right: **CO-OP: HOST / JOIN**.
   - Host: HOST A CO-OP GAME, invite friends from the list, then BACK and start or continue the campaign.
   - Friend: accept the host's Steam invite while the game is running, or open CO-OP and pick the friend who is hosting.
5. To remove it, run `uninstall.bat`. olmod and the game files are never modified.

Same-PC testing (developers): `installer/olcoop-host.bat` and `installer/olcoop-join.bat [ip]` start a host and joiners that
connect over UDP port 7777.
Options: `-coopnolog` turns logging off, `-coopdump <sec>` sets the state-dump interval, `-cooprobots interp` / `-cooprobotlead <ms>`
change how joiners display robots. Logs: `Overload\olcoop_logs\`.

## Build (developers)
- Needs Mono `mcs`. The build references the game's own managed DLLs and the installed olmod `GameMod.dll`; neither is
  committed or redistributed.
- `GAME_MANAGED=…/Overload_Data/Managed OLMOD_DLL=…/GameMod.dll ./build.sh` writes `build/Mod-olcoop.dll`;
  `./package.sh` then writes the player zip `dist/olcoop-<version>.zip` (mod DLL + `installer/` files).
- `tools/VerifyPatches.cs` checks offline that every Harmony patch target and parameter name exists in the game.
- `refs/` holds the local decompilation (`tools/ildump.py`) and is git-ignored. Never commit it.

### Source layout (`src/olcoop/`)
| File | What it does |
|---|---|
| `CoopCore.cs` | Mod entry, role (host/joiner), command line, logging |
| `Session.cs` | Joining the host's campaign level, handshake, player spawn |
| `NetcodeFixes.cs` | Side effects of running campaign levels with multiplayer netcode on |
| `SteamTransport.cs` | Steam P2P connection, lobby, invites, disconnect detection |
| `CoopScreen.cs` | CO-OP main-menu screen, Esc-menu entries, skipped story scenes, READY UP |
| `Lobby.cs` | Session list (who is in the game, state, ping; CO-OP screen, Esc menu, F8) and round-trip measurement |
| `OptionsScreen.cs`, `OptionsWindow.cs` | CO-OP OPTIONS (death mode, friendly fire etc.), in-menu and F8 window |
| `Robots.cs` | Host-authoritative robots streamed to joiners, predicted on joiners |
| `WorldSync.cs` | Level scripts, doors, switches, destroyables, security keys |
| `LevelFlow.cs` | Audio logs, exiting together (everyone lined up in the exit tunnel), lockdown regroups, teammates on the automap, closing the map safely, no flying in the map |
| `Lockdown.cs` | Lockdowns on joiners (doors, counter, alarm) and the boss/reactor health bar |
| `WorldFx.cs` | Auto-op fabricator (robot maker) effects and cryotube pickups on joiners |
| `Challenge.cs` | Challenge mode in co-op: shared score/kills/timer, kill upgrades for everyone, joiner loadout briefing, results for everyone |
| `Ping.cs` | Map pings shown to every player, hologuide to a ping |
| `FlareColors.cs` | Per-player flare colors |
| `Items.cs` | Saved-game items, late-activated items, joiner pickups, pickup sounds/ammo/energy, shared upgrade points and weapons, armor/energy cap, hologuide item list |
| `Combat.cs` | Melee damage routing, joiner loadouts and carry-over between levels, shot rate checks |
| `Death.cs`, `Hud.cs` | Death modes (respawn / spectate / hardcore), friendly fire, spectating with the full HUD, respawn countdown, names and health bars |
| `ShipState.cs` | Headlights and boost on other players' ships |
| `Objectives.cs` | Objective counters (operators, cores) and the single-player score block |
| `PostLevel.cs` | End-of-level screens on joiners, waiting for everyone to be ready |
| `Diagnostics.cs` | Read-only state logging to `olcoop_logs` |

## Docs
- `HANDOFF.md`: how the mod works, version by version, and the working rules.
- `BUILDS.md`, `CHANGELOG.md`: every build and what changed.
- `docs/`: design notes and research. `tests/`: manual test scripts and results.

## Credits
- The olmod team and contributors at the [Overload Development Community](https://github.com/overload-development-community/olmod):
  olcoop is built on olmod and would not exist without it.
- olcoop maintained by Blowntobytes ([github.com/Blowntobytes](https://github.com/Blowntobytes)).

Licence: MIT, like olmod. The mod contains no Overload code or assets.
