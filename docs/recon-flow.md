# Overload co-op recon: game flow, SP vs MP, hook points

Paths are relative to `refs/Assembly-CSharp/` unless they start with `olmod/` (meaning `/home/claude/olmod/GameMod/`).
GM = `Overload/GameplayManager.cs`, NM = `NetworkMatch.cs`, NetMgr = `Overload/NetworkManager.cs`.

## 1. GameplayState and the start/load paths

`enum GameplayState { PLAYING, MENUS, AUTOMAP, EXIT }` is defined in `Overload/GameplayState.cs:3`.

### Where `m_gameplay_state` changes

| Where | file:line | Note |
|---|---|---|
| `Initialize(GameManager)` | GM:726 | Sets MENUS. |
| `ChangeGameplayState(GameplayState)` (private) | GM:2190, assignment at 2222 | The only general setter. It handles pause/unpause and HUD creation. |
| `SwitchToMenu(MenuState)` → MENUS | GM:1150 | |
| `OpenAutomap()` → AUTOMAP | GM:1157 | |
| `Update()`: MENUS → PLAYING once the level is loaded | GM:1461 | |
| `Update()`: AUTOMAP → PLAYING | GM:1636 | |
| `TeleportSequenceStart(bool alien_warp)` → EXIT | GM:3306 | |
| `ExitSequenceStart()` → EXIT | GM:3391 | |
| `Client.OnPostgame` → PLAYING (MP) | Client.cs:435 | Writes the field directly. |

### SP campaign path
1. Menu → mission select: `MenuManager.cs:1990 / 2105 / 2206` → `GameplayManager.CreateNewGame(Mission, int level_num, GameOnMatchInfo=null, bool saved_game=false)` (GM:733).
   - It derives `m_game_type` from `Mission.Type`: CHALLENGE, MULTIPLAYER or MISSION.
   - It calls `m_local_player.InitializeForNewGame` and then `SetPendingLevelLoad` (GM:759), which sets `m_level_info`.
2. `MenuState.PLAY_GAME` → `MenuManager.PlayGameUpdate` (MenuManager.cs:6971) → `GameplayManager.LoadLevel(LevelInfo)` (GM:1052). That calls `SceneManager.LoadSceneAsync(level_info.SceneName)`.
   - `LevelInfo.SceneName` = `FileName`, or `"UserLevel"` for add-ons (LevelInfo.cs:91).
   - Then `AllowSceneActivation()`.
3. Unity `sceneLoaded` → `GameManager.OnSceneLoaded` (GameManager.cs:677). It calls `NetworkManager.OnSceneLoaded(name)` and then `GameplayManager.OnSceneLoaded(name)` (GM:1014).
4. GM:1014 → private `StartLevel()` (GM:858). This sets up the automap (SP only), pathfinding, `RobotManager.InitializeForNewLevel`, save/restart deserialisation, stats and challenge init.
5. Once the level is loaded and `!LoadScreenStillNeeded()`, `MenuManager` runs `SwitchToGame()` (MenuManager.cs:7011). `GameplayManager.Update` then flips MENUS → PLAYING (GM:1461).
6. Level end:
   - `DoorExit.OnTriggerEnter` → `ExitSequenceStart()` (GM:3386) → `ExitSequenceFrame` → `EscapeLevel()` (GM:3405/3536).
   - Or `TeleportSequenceStart` (GM:3302), from `ScriptTeleportOut` or `AlienWarp`, → `TeleportSequenceUpdate` (GM:3333) → `EscapeLevel()` (GM:3352).
   - `EscapeLevel()` (GM:2045) → `DoneLevel(Escaped)` (GM:2002) → `SwitchToMenu(DEBRIEF)`, or MAIN_MENU / TRAINING_RESULTS / CHALLENGE_RESULTS.
7. Next level: the debrief menu calls `GameplayManager.AdvanceLevel()` (MenuManager.cs:6367; GM:846) → `GetNextLevel()` (GM:835) → `SetPendingLevelLoad`. The flow then returns to step 2.
   - Player state is carried over through `m_player_data_advance_level = m_local_player.Serialize()`.
   - `m_gm.SaveCheckpoint(between_levels:true)` at GM:2122 saves the checkpoint.
8. Death: `PlayerHasDied()` (GM:2144) → `DoneLevel(Died)` → `MenuState.PLAYER_DIED`.
9. Restart: `CreateRestartGame()` (GM:804). Secret levels: `SwitchToSecret` / `ReturnFromSecret` (GM:766/792), via `secret.dat`.

### MP path
1. NM `Update()` (NM:1657) runs a state machine on `MatchState` (`MatchState.cs`): `NONE, LOBBY, LOBBY_LOAD_COUNTDOWN, LOBBY_LOADING_SCENE, PREGAME, PLAYING, POSTGAME, SCOREBOARD`. It is set with `SetMatchState` (NM:1977).
2. Server: `ProcessLobbyCountdown()` (NM:3027) picks a scene name from `GameManager.MultiplayerMission` or the playlist. It then calls `Server.ChangeScene(name)` (Server.cs:74), which does `SetAllClientsNotReady` and sends msg **48 SceneLoad**.
3. Every client, the host included, handles it in `Client.OnSceneLoadMsg` (Client.cs:368) → `SendPlayerLoadoutToServer` → `NetworkManager.LoadScene(name)` (NetMgr:111).
   - `NetworkManager.LoadScene` looks up the index **only in `GameManager.MultiplayerMission`** (`FindLevelIndex` or add-on hash). If the name is not found it calls `ExitMatchToMainMenu`.
   - Otherwise it calls `GameplayManager.CreateNewGame(MultiplayerMission, idx)` and `MenuState.PLAY_GAME`, and the SP loading path takes over from there.
4. Scene loaded: `NetworkManager.OnSceneLoaded` (NetMgr:129) → `Client.OnSceneLoad` / `Server.OnSceneLoad` (Server.cs:201) → `NetworkMatch.StartPreGame()` (NM:1826).
   - `Server.OnSceneLoad` runs `Item.InitLiveItems()`, `NetworkServer.SpawnObjects()`, sends msg 49 SceneLoaded and calls `RobotManager.ReadMultiplayerModeFile()`.
   - `StartPreGame` sets `IsMultiplayerActive = IsMultiplayer` and MatchState PREGAME.
5. Player spawn:
   - `LevelData.Awake` (LevelData.cs:126). When `NetworkManager.IsServer()`, it calls `Client.AddPlayer()` (`ClientScene.AddPlayer`, Client.cs:176).
   - The server handles UNET msg 37 in `Server.OnAddPlayerMessage` (Server.cs:292). That calls `NetworkSpawnPoints.ChooseSpawnPoint(team)`, `NetworkSpawnPlayer.InstantiatePlayer` and `NetworkServer.AddPlayerForConnection`.
   - Pure remote clients add their player elsewhere (Client update / `PlayerHasBeenAdded`, when ready).
6. `ProcessPregame` (NM:2147) → countdown, msg 58 → `StartPlaying()` (NM:1839). This sends msg 52 MatchStart, calls `PowerupLevelStart` and runs `NetworkSpawnPlayer.Respawn` for every player.
7. `ProcessPlaying` (NM:2212), server only, runs `MaybeEnd` (time/score). Then `End()` (NM:1871) moves PLAYING → POSTGAME (msg 74) → SCOREBOARD (msg 53).
   - On msg 53, `Client.OnMatchEnd` (Client.cs:400) calls `GameplayManager.DoneLevel(Quit)` (Client.cs:417).

## 2. Local player / ship in SP vs MP

| Case | Code | Note |
|---|---|---|
| SP (not the server) | `LevelData.Awake`, LevelData.cs:148-152 | `GameManager.m_player_ship = PlayerShip.Instantiate(); m_local_player = m_player_ship.c_player;`. `PlayerShip.Instantiate()` (PlayerShip.cs:1307) is a plain `Object.Instantiate(NetworkSpawnPlayer.m_player_prefab)`. It uses the same prefab `entity_special_player_ship` (NetworkSpawnPlayer.cs:13), but nothing is spawned on the network. |
| SP spawn position | LevelData.cs:154-157 | `if (!IsMultiplayer) m_player_ship.SetSpawnPosAndRotation(LegacyPlayerStart)`. The LegacyPlayerStart object is then deactivated. |
| MP (server or host) | LevelData.cs:144-146 → Server.cs:292 | The server instantiates the networked player. |
| MP all peers | `NetworkSpawnPlayer.InstantiatePlayer(GameObject,Vector3,Quaternion)` (NetworkSpawnPlayer.cs:30) and the client spawn handler `NetworkSpawnPlayerHandler` (:271) | When `IsMultiplayer`: `m_pregame=true; PrepareForMP()`. The camera is disabled and `NetworkManager.AddPlayer` is called. |
| MP local binding | `Player.OnStartLocalPlayer()` (Player.cs:4102) | Destroys any existing `m_player_ship`, then sets `m_player_ship = c_player_ship`, `m_local_player = this` and `m_viewer`. If not loading a save, it calls `InitializeForNewGame(m_level_info)`. |
| MP respawn | `NetworkSpawnPlayer.Respawn(PlayerShip)` (:256) → `Server.RespawnPlayer` (Server.cs:382, msg 50) | Uses team spawn points. |

`Player`, `PlayerShip`, `MonsterBall` and **`SaveLoadBase`** derive from `NetworkBehaviour`. `SaveLoadBase` is the base of `Robot`, `Item`, `DoorBase`, `PropBase` and `ScriptBase`. Level entities are therefore already UNET-weaved, with mostly empty `OnSerialize`, and `NetworkServer.SpawnObjects()` will spawn any that carry a `NetworkIdentity`.

## 3. Missions, levels, and what MP changes

- `MissionType { BUILT_IN, TRAINING, CHALLENGE, MULTIPLAYER, BUILT_IN_NGP, TEST, ADD_ON }` is in `Overload/MissionType.cs`.
- `GameManager.InitializeMissionList()` (GameManager.cs:1634) registers the missions:
  - `training`, `cronus` (BUILT_IN, which becomes `StoryMission`, GameManager.cs:1629) and `cronus_ngp`. **These are registered only when `!NetworkManager.IsHeadless()`**, so a dedicated server has no story mission.
  - A code-built `_CHALLENGE` mission.
  - `_MULTIPLAYER` (`m_multiplayer_mission.AddLevel("mp_wraith",...)`, :1660). It is exposed as `GameManager.MultiplayerMission` (:388).
- `Mission`:
  - `Open()` (Mission.cs:368) parses the YAML `levels` list.
  - Lookups: `OpenLevel(int)` (:126), `OpenLevel(string)` (:145), `FindLevelIndex(string)` (:140), `GetLevelFileName(int)` (:474), `NumLevels` (:58).
  - Add-ons: `IsLevelAnAddon` (:114), `FindAddOnLevelNumByIdStringHash` (:156).
- `LevelInfo` ctor (LevelInfo.cs:187/227). `SceneName` (:91).
- Load chain: `CreateNewGame` → `SetPendingLevelLoad` → `MenuManager.PlayGameUpdate` → `GameplayManager.LoadLevel` → `SceneManager.LoadSceneAsync` → `GameManager.OnSceneLoaded` → (NetMgr + GM).`OnSceneLoaded` → `StartLevel`.

### What MP does differently at load and runtime

No code explicitly deletes robots or objectives in MP. MP maps are simply authored without them. The differences are gates on `IsMultiplayer` (game type = `MULTIPLAYER`, which comes only from `Mission.Type == MULTIPLAYER`) or on `IsMultiplayerActive`:

| Code | file:line | Effect in MP |
|---|---|---|
| `RobotManager.Update()` | RobotManager.cs:320-326 | **Early return**: activates all chunks and disables reflection probes and lights. No robot list or activation, no flocks, no awakening. **The robot AI never runs in MP.** |
| `RobotManager.InitializeForNewLevel` | RobotManager.cs:271, :298 | `Robot.c_target_transform = GameManager.m_player_ship.transform`. This is a **single static target** (also in `PropReactorTurret.cs:73`). |
| `ActivateRobot/Item/Door/Prop/Trigger` + `*InRelevantSegment` | RobotManager.cs:634-830 | Segment-visibility culling that uses the local ship's segment. It deactivates GameObjects outside it. olmod forces doors and triggers visible in MP (`olmod/MPDoors.cs:34`, `olmod/MPTriggers.cs:8`). |
| `StartLevel` | GM:860-867 | No automap in MP. |
| `LevelData.Awake` | LevelData.cs:144, :154 | Networked player; LegacyPlayerStart is ignored. |
| `Server.OnSceneLoad` | Server.cs:201-209 | `Item.InitLiveItems`, `NetworkServer.SpawnObjects`, and `RobotManager.ReadMultiplayerModeFile()` (RobotManager.cs:1682 → `DoReadMultiplayerModeFile(LevelInfo, Action<string>)` :1848 → `ParseTagMultiplayer`). The mode file is only an item/weapon/missile spawn table. olmod overrides it with `multi_mode_<level>.txt` (`olmod/MPCustomModeFile.cs:17`). |
| `NetworkMatch.PowerupLevelStart` / `MaybeSpawnPowerup` | NM:4809, ProcessPlaying | Random powerup spawning (`NetworkSpawnItem.Spawn` → `NetworkServer.Spawn`). |
| `Item` | Item.cs:256, 277, 367, 446, 467-521, 570 | Only the server picks up and despawns items. MP ammo amounts are fixed. |
| `ProjectileManager.PlayerFire` area | ProjectileManager.cs:210, 254, 265 | Only projectiles fired by a **Player** are sent to clients (msg 70). |
| `ChunkManager` | ChunkManager.cs:180 | The server skips segment-based activation. |
| `LevelCustomInfo.Reset` | LevelCustomInfo.cs:48 | Objective/custom info is reset for CM/MP. |
| `GameManager.Update` | GameManager.cs:1006 | Dynamic objects keep updating while in menus in MP. |
| `LoadScreenStillNeeded` | MenuManager.cs:7018 | In MP it waits until `m_local_player.isLocalPlayer`. |
| `GameplayManager.GamePaused` | GM:652 | MP is never paused. |

`ChallengeManager` already has partial support for MP scenes: `NetworkSpawnItem.InitLiveItems` when `IsMultiplayerSceneLoaded` (ChallengeManager.cs:456), and spawn handling (:505, :612).

## 4. Network messages and replication

UNET channels are set in `NetworkManager.GetConnectionConfig`: 0 ReliableSequenced, 1 UnreliableSequenced, 2 Unreliable, 3 StateUpdate.

Stock custom ids are in `Overload/CustomMsgType.cs` (48-86, plus 101 SetMatchState). Handlers are registered in `Server.RegisterHandlers` (Server.cs:88) and `Client.RegisterHandlers` (Client.cs:289).

| Id | Name | Direction | Payload |
|---|---|---|---|
| 48/49 | SceneLoad / SceneLoaded | S→C | Scene name (StringMessage) |
| 50 | Respawn | S→C | pos/rot |
| 51 | SlowMoTimer | S→C | |
| 52/53 | MatchStart / MatchEnd | S→C | |
| 54-59, 72, 73, 75-78 | Lobby (join, accept, players, countdowns, private data, status, votes, chat, team switch) | both | |
| 60/61 | LoadoutData / SetLoadout | both | |
| 62 | PlayerInputToServer | C→S | Client-predicted input |
| 63/64 | PlayerStateToClient / PlayerSnapshotToClient | S→C | Ship state for the owner, and snapshots of all ships |
| 66/67 | ButtonJustPressed/Released | S→C | |
| 68/69 | ControlOptions / ReadyForCountdown | C→S | |
| 70 | FireProjectileToClient | S→C | `Server.SendProjectileFiredToClients(Player,...)` (Server.cs:461), player projectiles only |
| 71 | AckInputs | S→C | |
| 74 | Postgame | S→C | |
| 79-86 | pings, time limit, Xbox, UnsupportedMatch | | |
| 101 | SetMatchState | S→C | |

- **Other replication:** UNET Rpc/Cmd/SyncVar on `Player` (38 attributes: hp, energy, ammo, weapons, cloak, invul, kills, etc.), `PlayerShip` (damage RPCs, `CmdReadyToRespawn`, charge attack), `Item.RpcMakeSuper` and `MonsterBall`. Item spawning goes through `NetworkServer.Spawn` with `NetworkSpawnItem.RegisterSpawnHandlers` (28 item prefabs). SmoothSync is used for the monsterball.
- **Not replicated in stock code:** robots, robot projectiles, doors, triggers/scripts, reactor and level-script state. Monsterball is the only non-player NetworkBehaviour in active use. CTF and Race are olmod-only (`ExtMatchMode` in `olmod/MPModPrivateData.cs:14`, MatchMode 3/4).
- **olmod message ids** (`olmod/MessageTypes.cs`): 101-155 are in use (for example 104 JIP, 121-128 CTF, 130/131 creeper sync and explode, 133 sniper packets, 149 send damage, 154 enhanced fire, 155 player physics). A co-op mod should take ids **> 160** to avoid collisions.

## 5. SP level end conditions

| Condition | Where evaluated | Local-player coupling |
|---|---|---|
| Exit door reached | `DoorExit.OnTriggerEnter(Collider)` DoorExit.cs:7 (layer 9 = player) → `GameplayManager.ExitSequenceStart()` GM:3386 | Checks `GameManager.m_player_ship.m_dying`. The exit cinematic drives the **local** ship and camera (`ExitSequenceFrame` GM:3485, `CreatePlayerPathToEnd`). |
| Teleport/warp out | `ScriptTeleportOut` (ScriptTeleportOut.cs:21-23), `AlienWarp.cs:130`, `ChallengeManager.cs:2225` → `TeleportSequenceStart(bool)` GM:3302 | Uses `m_local_player.AddXP` and `m_player_ship` throughout. |
| Reactor destroyed → countdown | `Reactor` (Reactor.cs:~104, `PropBase`) → `GameplayManager.ReactorDestroyed(bool)` GM:1185 → `EscapeStart(float)` GM:1161 (`MustEscape`, `EscapeTimer`, matcens off). Level 12 uses `ScriptLevel12` → `Level12EscapeStart` GM:1214 | XP goes to the local player. |
| Countdown expiry | `EscapeUpdate()` GM:1281, kill branch ~GM:1376-1395 | Kills **only** `GameManager.m_player_ship`. |
| Boss / final objective | `BossIsAlive()` GM:2550 (`Robot.m_is_boss`), `GetBossOrReactorSeg()` GM:2567, `FinalObjective()` GM:2588 | These are HUD/guidance queries only. A boss death triggers scripts (ScriptOnDestroy / ScriptOnRobotKills) that open the exit. |
| Robot-kill objectives / lockdowns | `ScriptOnRobotKills`, `ScriptOnCount`, `ScriptLockdown*`; `GameplayManager.Lockdown*` GM:3650-3800 (`LockdownRobotsRemaining`, `LockdownEnd`) | Level scripts are wired through `TriggerBase.OnTrigger(Collider)` (TriggerBase.cs:50), which sends `ActivateScriptLink` messages and fires on any collider in the right layer. |
| Death | `PlayerHasDied()` GM:2144 | Ends the level for the whole game in SP. |
| Completion bookkeeping | `EscapeLevel()` GM:2045 | Upgrade points and XP go to `m_local_player`. Then `SaveCheckpoint` and DEBRIEF. |

In total, GameplayManager.cs has 81 references to `m_player_ship`/`m_local_player`, Robot.cs 185 and RobotManager.cs 23. Robot AI targets the single static `Robot.c_target_transform`.

## 6. Candidate hook points for "co-op campaign"

**Strategy:** reuse the MP server path (lobby → SceneLoad msg → networked players). Load a story level by adding a new olmod-style `ExtMatchMode.COOP = (MatchMode)5+`, then re-enable the SP systems that `IsMultiplayer` gates.

| Hook | Target | Purpose |
|---|---|---|
| Level resolution | `NetworkManager.LoadScene(string)` NetMgr:111 (olmod already patches it in `MPDownloadLevel.cs:228`) | Resolve the scene in `StoryMission` (`cronus`) instead of `MultiplayerMission`, and call `CreateNewGame(StoryMission, idx)`. Watch the knock-on effect: `m_game_type` becomes MISSION, so `IsMultiplayer` turns false, which breaks every MP gate. **Alternative:** keep `m_game_type = MULTIPLAYER` (prefix on `CreateNewGame` or `SetGameType`) and selectively un-gate the SP systems. This is the key design decision. |
| Server scene selection | `NetworkMatch.ProcessLobbyCountdown` NM:3027 / `Server.ChangeScene` | Send the story level name, plus an index, in msg 48 or a new co-op msg. |
| Headless server | `GameManager.InitializeMissionList` GameManager.cs:1634 | Register `cronus` on a dedicated server too. |
| Robot AI | `RobotManager.Update` RobotManager.cs:316 (prefix) | Bypass the MP early return on the server. Pick a target per robot; replace the static `Robot.c_target_transform` (set at :298) with the nearest player. Clients should run no AI and receive robot state. |
| Robot sync | new msgs (>160) | Server-authoritative robot transforms and HP, plus death/explode and robot projectile fire (stock msg 70 only carries `Player` owners, ProjectileManager.cs:254). |
| Culling | `RobotManager.*InRelevantSegment` RobotManager.cs:782-830 | Force everything active on the server, or union over all players' segments. |
| Doors / triggers / scripts | `TriggerBase.OnTrigger` (precedent: `olmod/MatchModeRace.cs:589`), `DoorAnimating`, `DoorExit.OnTriggerEnter` | The server decides and broadcasts script-link activations, door state and reactor state. |
| Level setup | `GameplayManager.StartLevel` GM:858 | Keep `RobotManager.InitializeForNewLevel`. Skip `ReadMultiplayerModeFile` and powerup spawning (`Server.OnSceneLoad` :207, `NetworkMatch.PowerupLevelStart` NM:4809, `MaybeSpawnPowerup`). Leave placed level items as they are. |
| Spawn | `Server.OnAddPlayerMessage` Server.cs:292, `NetworkSpawnPlayer.Respawn` :256, `NetworkMatch.StartPlaying` NM:1839 | Use the LegacyPlayerStart position (the code hides it at LevelData.cs:158) with offsets, or the last checkpoint. |
| Match end | `NetworkMatch.MaybeEnd/MaybeEndScore/MaybeEndTimer` NM:2313-2343 | Suppress time and score endings. End the match when the server's exit/teleport fires. |
| Exit flow | `ExitSequenceStart` / `TeleportSequenceStart` / `EscapeLevel` | Run on the server, broadcast to clients, and play the local cinematic on each client. Then advance to the next level with a new `Server.ChangeScene`, not the SP DEBRIEF menu. |
| Escape timer | `EscapeUpdate` GM:1281 | Run on the server and kill every player. |
| Death | `PlayerHasDied` GM:2144 / `PlayerShip.StartDying` | Use MP respawn instead of `DoneLevel(Died)`. |
| Carried state | `Player.Serialize/Deserialize`, `AdvanceLevel` GM:846 | Keep each player's weapons and upgrades between levels. Server-side per connection. |

**MP-only code to suppress in co-op:**
- powerup spawn and despawn (`PowerupLevelStart`, `MaybeSpawnPowerup`, `Item.MaybeDespawnPowerup`)
- the mode file
- time/score match end
- MP loadouts (`NetworkSpawnPlayer.SetMultiplayerLoadout` :82)
- the MP fixed ammo amounts in `Item`
- MP scoreboard and kill feed
- the `RobotManager.Update` early return
- MP chunk, light and probe disabling
