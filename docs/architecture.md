# olcoop architecture

Status: Phase 0 (recon + instrumentation). The detailed evidence, with file:line references into the
decompiled `Assembly-CSharp.dll` (game build 1.0.1885+), is in `recon-flow.md` and `recon-world.md`.
This document gives the conclusions and the design we're committing to.

## 1. How the mod is loaded

```
olmod.exe -modded
  └─ UnityMain → Mono → GameMod.dll (stock olmod 0.5.14, untouched)
       └─ Harmony.PatchAll(GameMod)
       └─ for each Overload\Mod-*.dll: Assembly.LoadFile + Harmony.PatchAll   ← olcoop lives here
```

- olcoop ships as **one file**, `Mod-olcoop.dll`, in the Overload folder. Deleting it uninstalls the mod.
  Without `-modded`, olmod never loads it.
- It compiles against the installed `GameMod.dll`, so it can reuse olmod types later (`MPModPrivateData`,
  `ExtMatchMode`, the message-id registry) without forking olmod.
- Build: `build.sh` (Mono `mcs`, C# 7.2, .NET 3.5 profile, using the game's own mscorlib).
  `tools/VerifyPatches.cs` checks offline that every Harmony target method and parameter name exists.
  That matters because Harmony only reports a mismatch at game launch.

## 2. Single-player vs multiplayer control flow (as shipped)

| | Single-player campaign | Multiplayer (UNET HLAPI) |
|---|---|---|
| Entry | Menu → `GameplayManager.CreateNewGame(StoryMission, n)` | Lobby → `NetworkMatch.ProcessLobbyCountdown` → `Server.ChangeScene` → msg 48 → `NetworkManager.LoadScene` → `CreateNewGame(MultiplayerMission, n)` |
| Game type | `m_game_type = MISSION`, which is derived from `Mission.Type` | `MULTIPLAYER`, which makes `IsMultiplayer` true and turns on every MP gate |
| Scene load | `LoadLevel` → `LoadSceneAsync` → `OnSceneLoaded` → `StartLevel` | Same path. Then `Server.OnSceneLoad` spawns items and reads the mode file, followed by `StartPreGame` and `StartPlaying` |
| Player ship | `LevelData.Awake` creates a plain `Instantiate` of the ship prefab, placed at LegacyPlayerStart | The server creates the same prefab in `OnAddPlayerMessage` with `NetworkServer.AddPlayerForConnection`. `Player.OnStartLocalPlayer` binds `m_local_player`/`m_player_ship` on each client |
| Robots | `RobotManager.Update` runs culling, activation and AI. Every robot targets the static `Robot.c_target_transform` (the one ship) | `RobotManager.Update` **returns early**, so no AI runs. MP maps have no robots |
| Replication | none | Player input/state/snapshots (62-64), player projectiles (70), items (`NetworkServer.Spawn`), SyncVars on Player. **Not synced:** robots, robot shots, doors, triggers, scripts, reactor |
| Level end | Exit door, teleport or warp → `EscapeLevel` → `DoneLevel` → debrief → `AdvanceLevel` | Time/score `MaybeEnd` → POSTGAME → scoreboard |
| Death | `PlayerHasDied` → `DoneLevel(Died)` → restart menu | Respawn at a spawn point |

The world entities (Robot, Item, DoorBase, PropBase, ScriptBase) all inherit `SaveLoadBase : NetworkBehaviour`.
They are UNET-weaved but synchronise nothing. Robot IDs come from scene search order and are not
guaranteed to match across machines, so co-op needs its own deterministic entity IDs.

## 3. Key design decision (REVISED 2026-10-03: approach A, see phase1-design.md)

> Phase 0 logs showed SP already runs a UNET server and a networked local Player, and the player netcode is
> gated on the settable flag `IsMultiplayerActive`, not on the game type. So Phase 1 uses **A**: joiners connect to a
> normal SP host, which keeps robot AI, level scripts and objectives native. B remains the fallback. Original
> analysis below.

### Original analysis: keep the game type MULTIPLAYER

There are two ways to run a campaign level with other people:

- **A. Campaign type plus networking.** Load the level as `MISSION` and turn the MP network stack back on.
  `IsMultiplayerActive` and player networking (input prediction, snapshots, spawn handlers) are woven
  through hundreds of gates, and all of them would have to be re-enabled.
- **B. MP type plus campaign systems (chosen).** Load a story level through the stock MP path with a new
  olmod-style `ExtMatchMode.COOP`. Every MP system that already works (lobby, join, ship replication,
  damage authority, respawn, olmod's JIP and level download) keeps working. We then switch the SP
  systems back on one at a time (robot AI, culling, objectives, exit flow), and each one is a discrete,
  testable patch.

Phase 1 must prove B. Its riskiest assumption is that a campaign scene works with networked players
(LegacyPlayerStart, LevelStart script, missing MP spawn points). Phase 0's logs will show what a
campaign level looks like at runtime before we change anything.

## 4. Target runtime design (Phases 1-6)

```
HOST (NetworkServer + local client)                    CLIENT x2
 ├─ Lobby: COOP mode, campaign level, difficulty        ├─ Joins (olmod lobby), version handshake (msg ≥160)
 ├─ ChangeScene(story level) ──msg 48──────────────────▶├─ LoadScene resolves level in StoryMission
 ├─ Robot AI runs here ONLY (RobotManager.Update)       ├─ Robot AI disabled; robots are puppets
 │   target = CoopTargets.Best(robot) over all ships    │
 ├─ RobotSync: id, pos, rot, vel, hp, mode @ 10-20 Hz ──▶├─ interpolate
 ├─ RobotEvent: spawn (matcen), damage, die/explode ───▶├─ play FX / ExplodeNow locally
 ├─ RobotFire: weapon, origin, dir, target ────────────▶├─ spawn cosmetic projectile
 ├─ WorldEvent: door/forcefield/trigger/script link, ──▶├─ apply
 │   reactor destroyed, escape timer, cryotube, exit
 └─ Level end: when all living players have exited ───▶└─ debrief, then the next ChangeScene
```

- **Entity IDs:** a deterministic ordering, for example sorting placed robots by segment index and then
  rounded position, plus a server-assigned counter for matcen spawns.
- **Bandwidth:** about 25 active robots × 20 Hz × ~32 B ≈ 16 KB/s per client. That's an estimate to be
  measured in Phase 2. Only robots relevant to some player get prioritised.
- **Message ids:** 160 and above. olmod uses 101-155.
- **Authority:** the server applies all damage. Stock `PlayerShip.ApplyDamage` is already server-only.
  `Robot.ApplyDamage` is not, so in co-op the clients' copy must become a no-op.

## 5. Open questions (to be answered with Phase 0 logs or Phase 1 spikes)

1. Does `LevelData.Awake` on a story scene, loaded through the MP path, create exactly one networked ship
   per player and no stray SP ship?
2. Which MP gates break campaign scripts, such as the `LevelCustomInfo.Reset` and `GamePaused`
   differences?
3. How many robots do real campaign levels have, and how many are active at once? Phase 0's periodic
   dump measures this.
4. Do level scripts depend on `GameManager.m_player_ship` beyond what grep shows (via `Viewer` or camera)?
