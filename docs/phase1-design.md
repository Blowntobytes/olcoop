# Phase 1 design: second instance joins an SP host and both ships move

Goal: the host plays `sp_outer_01` in single-player. A second Overload instance on the same PC
(127.0.0.1) connects to the host's UNET server, loads the same scene and gets its own networked
`PlayerShip`, which it controls. Each instance sees the other ship move. Robots are not synced.

Paths are relative to `refs/Assembly-CSharp/` unless they start with `olmod/` (meaning
`/home/claude/olmod/GameMod/`). Line numbers are for the decompiled build in `refs/`.
Anything not proven by code or by the Phase 0 logs is marked **UNVERIFIED**.

---

## 0. Recommendation (TL;DR)

**Use approach A ("join an SP host").** It needs **9 Harmony patches** (8 at the bare minimum), plus 1 optional one. Approach B
would get Phase 1 working with roughly the same number of patches. The difference is what comes after
Phase 1: B turns off the SP world (robot AI, LegacyPlayerStart, level scripts that check
`IsMultiplayer`), and every later phase would have to turn it back on.

Why A is cheaper than `architecture.md` §3 assumed:

- The player netcode is gated almost entirely on **`GameplayManager.IsMultiplayerActive`**, plus
  `NetworkMatch.InGameplay()`. It is not gated on the game type.
- `IsMultiplayerActive` is a plain public static field (`Overload/GameplayManager.cs:277`).
  `IsMultiplayer` is computed from `m_game_type` (`GameplayManager.cs:654`).
- So we can run the full MP player netcode (prediction, reconciliation, snapshots, server-side
  simulation of remote ships) while `m_game_type` stays `MISSION`. Every `IsMultiplayer` gate (about
  80 references in 19 files: robot AI, LegacyPlayerStart, automap, mode file) keeps its SP behaviour
  without any further patching.

**This reverses the decision in `architecture.md` §3.** Update that document if the Phase 1 test passes.

---

## 1. What already exists in SP (verified)

| Fact | Evidence |
|---|---|
| Every instance opens a real UNET server at startup on a random port from 7000 to 7999 and connects to it locally. | `GameManager.Awake` → `NetworkManager.Init()` (GameManager.cs:405) → `StartServerWithLocalConnection()` (NetworkManager.cs:93-109) → `Server.Listen()` with no port (Server.cs:44-58, random 7000-7999 with 500 retries) → `Client.ConnectLocal()` (Client.cs:143-151). The port is logged: `"Server listening on port {0}"` (NetworkManager.cs:106). |
| The host's SP ship is a networked UNET player. | `LevelData.Awake` (LevelData.cs:126). `if (NetworkManager.IsServer()) Client.AddPlayer();` (LevelData.cs:144-147) → `ClientScene.AddPlayer(conn,0)` (Client.cs:176-184) → msg 37 → `Server.OnAddPlayerMessage` (Server.cs:292-331) → `NetworkSpawnPlayer.InstantiatePlayer` + `NetworkServer.AddPlayerForConnection(conn, go, 0)` (Server.cs:322) → `Player.OnStartLocalPlayer` (Player.cs:4102) sets `GameManager.m_player_ship` / `m_local_player`. The local connection runs this synchronously, so `LevelData.cs:154-157` then moves that networked ship to `LegacyPlayerStart` (`!IsMultiplayer`). Phase 0 log: `OnStartLocalPlayer netId=2 local=True server=True pos=(0.0, -12.0, -157.0)`. |
| `sp_outer_01` appears to have at least one `m_player_spawn_points` entry. | In the Phase 0 log the ship is at (0,-12,-157) **inside** `OnStartLocalPlayer`. That is before LevelData.cs:156 moves it, so `ChooseSpawnPoint` returned a real point. The main-menu player (netId=1) logs pos=(0,0,0), which is the null-spawn-point fallback (Server.cs:301-304). **UNVERIFIED:** whether this point is the same as LegacyPlayerStart, and how many points there are. |
| In SP, MatchState goes NONE→PREGAME and stays there. Nothing MP-specific runs. | `NetworkManager.OnSceneLoaded` → `NetworkMatch.StartPreGame()` (NetworkManager.cs:144; NetworkMatch.cs:1826-1837) sets `IsMultiplayerActive = IsMultiplayer` (= false) and PREGAME. `ProcessPregame` (NetworkMatch.cs:2147) and `ProcessPlaying` (2212) both return immediately while `!IsMultiplayerSceneLoaded()`. That flag only becomes true when the scene came through `NetworkManager.LoadScene` (NetworkManager.cs:122, 131-135, 160-163), which SP never calls. |
| `Server.OnSceneLoad` runs in SP, but msg 49 and the mode file are MP-only. | Server.cs:201-210: `Item.InitLiveItems(); NetworkServer.SpawnObjects();`, then `if (IsMultiplayer) { SendSceneLoadedToAllClients; ReadMultiplayerModeFile; }`. |

---

## 2. Traced join flow for approach A

Below, ✅ means it works as shipped, and ⛔ means it is blocked. Each blocker is fixed by a patch from §4.

### 2.1 Client starts and connects

1. The client instance boots. It also opens its own random listen server and connects locally
   (NetworkManager.cs:93-109). This is harmless.
2. At the main menu, our hook calls `Client.Connect("127.0.0.1", port)` (Client.cs:118-141):
   - `if (NetworkServer.active) NetworkServer.Shutdown();` (121-124). The client's own server is shut down.
   - It disconnects the local client (125-128), creates a fresh `NetworkClient` (`Create()` → `ConfigureConnection` + `RegisterHandlers`, 275-287), and calls `Connect`.
   - There is no stock UI for this. The only stock caller is the dev console command `client_connect <ip> <port>` (ClientDebug.cs:9, 20-29). → **patch C2**.
   - ⛔ The client's own menus can call `NetworkManager.StartServerWithLocalConnection()`: Mission select (MenuManager.cs:1957), challenge select (2144) and load game (4328). On the client, `Server.IsActive()` is now false, so that call would `Listen` and then `ConnectLocal()`. `ConnectLocal` calls `Disconnect()` first (Client.cs:145-148), which **drops the connection to the host**. The main menu itself does not call it. → **patch C3**.
3. Host side: UNET msg 32 → `Server.OnConnect` (Server.cs:152-155). It only logs. olmod adds postfixes that send a status string and clear capabilities (`olmod/MPDownloadLevel.cs:268`, `olmod/MPTweaks.cs:258`).
   - ✅ **No lobby or match state check, and no rejection.** `NetworkServer.Configure(cfg, 16)` (Server.cs:143-150) allows 16 connections. Encryption only applies to headless servers (NetworkEncryption.cs:33-41, 99-106).
   - The lobby join (msg 54 → `NetworkMatch.AcceptNewConnection`, the version check) only happens if the client sends it. The client's `OnConnectMsg` sends msg 54 only `if (NetworkMatch.InLobby())` (Client.cs:336-339), which is false at the main menu.
   - So no lobby data exists for this connection, and every `Get*FromLobbyData` call returns a default without throwing (NetworkMatch.cs:5678-5750).
4. Client side: `OnConnectMsg` (Client.cs:327-340) registers the player spawn handler (`NetworkSpawnPlayer.RegisterSpawnHandler`) and the monsterball spawn handler. ✅

### 2.2 The client learns the scene

5. ⛔ The host never sends msg 48 SceneLoad in SP. 48 is only sent by `Server.ChangeScene` (Server.cs:75-80), which only `NetworkMatch.ProcessLobbyCountdown` calls (NetworkMatch.cs:3027ff). msg 49 SceneLoaded is gated on `IsMultiplayer` (Server.cs:205-209).
   - Host patches **H2** (on connect, if a level is loaded) and **H3** (on host scene load, for already-connected remotes) send `48(name)` and then `49(name)` to each remote connection (`connectionId != 0`) only.
   - Both go on the reliable-sequenced default channel, so 48 is processed first. `OnSceneLoadMsg` clears `m_scene_loaded_on_server` (Client.cs:372-374), and then 49 sets it (Client.cs:384-391).
   - The name to send is `GameplayManager.m_level_info.FileName` (which equals `SceneName` for built-in levels, LevelInfo.cs:91).
6. Client `OnSceneLoadMsg` (Client.cs:368-382): it resets the tick counters, sends loadout msg 60 (harmless: `NetworkMatch.UpdatePlayerLoadout` with lobby_id 0, **UNVERIFIED** but it is a dictionary write), and calls `NetworkManager.LoadScene(name)`.
7. ⛔ `NetworkManager.LoadScene` (NetworkManager.cs:111-127) searches **only `GameManager.MultiplayerMission`**. `sp_outer_01` is not found, so it calls `ExitMatchToMainMenu()`. → **patch C1** resolves the name in `GameManager.StoryMission` (GameManager.cs:392, set at 1629) with `Mission.FindLevelIndex` (Mission.cs:140-143). It then calls `GameplayManager.CreateNewGame(StoryMission, idx)` (GameplayManager.cs:733) and `MenuManager.ChangeMenuState(MenuState.PLAY_GAME)`.
   - C1 deliberately does **not** set `m_network_scene_loading`. That keeps `IsMultiplayerSceneLoaded()` false on the client too, so `ProcessPregame` and `ProcessPlaying` stay inert, just as they are on the host.
   - The result is `m_game_type = MISSION` on the client as well.
   - olmod also prefixes `LoadScene` (`olmod/MPDownloadLevel.cs:228-262`). For a name without `:`, that prefix just calls `CheckExtraSpawnpoints` and returns true. C1 should be `[HarmonyPriority(Priority.First)]` and return false. **UNVERIFIED:** whether olmod's prefix still runs after ours returns false. It is harmless either way.
8. The client loads the level through the normal SP path: PLAY_GAME → `LoadLevel` → `OnSceneLoaded` → `StartLevel` (recon-flow §1).
   - `LevelData.Awake` on the client: `IsServer()` is false, so it creates a **plain, non-networked** ship as `m_player_ship` (LevelData.cs:148-152). Because `!IsMultiplayer`, that ship is placed at LegacyPlayerStart (154-157).
   - `RobotManager.InitializeForNewLevel` aims every robot at this plain ship (RobotManager.cs:298). `Robot.Start` caches it in `Robot.m_player_ship` (Robot.cs:633, 1949), and `PropReactorTurret.Start` does the same (PropReactorTurret.cs:73-74).
   - `MenuManager.LoadScreenStillNeeded` returns false for non-MP (MenuManager.cs:7024-7027), so the client starts playing on the plain ship right away.
9. `NetworkManager.OnSceneLoaded` (client) → `Client.OnSceneLoad(name)` sets `m_scene_loaded` (Client.cs:79-82) → `NetworkMatch.StartPreGame()` → PREGAME, `IsMultiplayerActive = false`. ⛔ The netcode is still off at this point (see §3). → **patch S1**.

### 2.3 AddPlayer and spawning

10. `Client.Update` (Client.cs:61-72): when `m_scene_loaded_on_server == m_scene_loaded` (case-insensitive), it calls `AddPlayer()` because `!IsServer()`. ✅ This depends on msg 49 from step 5.
    - UNET `ClientScene.AddPlayer(conn,0)` marks the connection ready.
    - Stock MP remote clients use exactly this path and never call `ClientScene.Ready` themselves (the only call site is Client.cs:183). This is the same mechanism stock MP uses, so it should work. UNET internals themselves are **UNVERIFIED**.
11. Host `Server.OnAddPlayerMessage` (Server.cs:292-331):
    - `ChooseSpawnPoint(TEAM0)` (NetworkSpawnPoints.cs:11-34). olmod replaces it for ANARCHY/TEAM_ANARCHY (`olmod/MPRespawn.cs:109-134`). `NetworkMatch.GetMode()` defaults to ANARCHY (**UNVERIFIED**), so olmod's version is the one that runs.
    - In the campaign level this returns the level's player spawn point, apparently the same spot where the host spawned (§1).
    - ⛔ That puts both ships on top of each other. If the level has no spawn points, the code logs an error and uses the origin (Server.cs:299-304), which is probably inside geometry. → **patches H4 + H5** (spawn at the host's LegacyPlayerStart pose plus an offset).
    - `InstantiatePlayer` (NetworkSpawnPlayer.cs:30-48): `IsMultiplayer` is false, so there is **no `m_pregame` and no `PrepareForMP`**. The camera is disabled and the player is added to `NetworkManager.m_Players`.
    - `AddPlayerForConnection` follows. The MP-only name and team block is skipped (Server.cs:324-330). The remote ship on the host has no name, which is fine.
12. Client: the UNET spawn message → `NetworkSpawnPlayerHandler` (NetworkSpawnPlayer.cs:271-290) → `InstantiatePlayer` → `Player.OnStartLocalPlayer` (Player.cs:4102-4157).
    - This **destroys the plain SP ship** (4105-4109) and binds `m_player_ship` / `m_local_player` / `m_viewer`.
    - It calls `InitializeForNewGame(m_level_info)`, which gives the SP starting loadout locally (4116-4119).
    - ⛔ Robots on the client still point at the destroyed plain ship (`Robot.c_target_transform`, the private static `Robot.m_player_ship`, and `PropReactorTurret.c_target_transform`). The likely result is MissingReferenceException spam, **UNVERIFIED**. → **patch C4** re-aims them.
    - The host's ship (netId 2) is spawned on the client at the same moment, because the connection became ready and the server sends all observed objects. It becomes a remote `Player` in the client's `m_Players`. ✅
13. Scene objects: `NetworkServer.SpawnObjects()` runs on the host in SP (Server.cs:204). **UNVERIFIED:** whether campaign-scene objects (Robot, Item, Door, all `NetworkBehaviour`s) carry a `NetworkIdentity` with a sceneId. If they do, UNET sends spawn and destroy messages for them to the client. That could hide or move robots on the client, or log "no scene object" errors. Check the client log for this in test step 5. Only player objects are expected.

### 2.4 Per-tick pipelines and their gates

All of these run from `GameManager` (GameManager.cs:796) → `PlayerShip.FixedUpdateAll()` (PlayerShip.cs:3339-3376).

| Pipeline | Code | Gate | Blocked in SP? |
|---|---|---|---|
| Client reads its input and sends it to the server | `FixedUpdateReadControls` (PlayerShip.cs:4436-4454) → `Player.SendPlayerControlsToServer` msg 62 (Player.cs:4794-4823). olmod transpiles this to `SendPlayerPhysicsToServer` and sends `MsgPlayerPhysics` from the postfix instead (`olmod/MPServerOptimization.cs:357-410, 160-169`) | `IsMultiplayerActive && !Server.IsActive() && NeedToSendFixedUpdateMessages()`. That last check requires `NetworkMatch.InGameplay()` = PLAYING or POSTGAME (Player.cs:4913-4920; NetworkMatch.cs:1764-1771). olmod adds `IsMultiplayerActive && NeedToSend…` | ⛔ IsMultiplayerActive=false, MatchState=PREGAME |
| Client prediction history | `Client.AddPlayerStateToHistory` (PlayerShip.cs:3362-3365) | none | ✅ |
| Client reconciles with server state 63 | `Client.ReconcileServerPlayerState` (PlayerShip.cs:3341-3344; Client.cs:823-855). olmod replaces it (`olmod/MPSkipClientResimulation.cs:30`) | `IsMultiplayerActive`, and inside it `NetworkMatch.InGameplay()` (Client.cs:841) | ⛔ |
| Smoothing error | PlayerShip.cs:3350-3353, 3374-3375 | `IsMultiplayerActive` | ⛔ (cosmetic) |
| Server receives input | `Server.OnPlayerInputToServer` msg 62 (Server.cs:573-581). olmod `OnPlayerPhysicsToServer` (`olmod/MPServerOptimization.cs:430ff`). Both are registered in `Server.RegisterHandlers` | none (the sender only needs to be non-local) | ✅ |
| Server simulates the remote ship | `PlayerShip.FixedUpdatePre` (PlayerShip.cs:3990-4015) → `Server.ProcessCachedControlsRemote` (Server.cs:583-605; olmod prefix `olmod/MPServerOptimization.cs:499ff`) | `Server.IsActive()` only. `FixedUpdateProcessControls` requires `!m_pregame` (PlayerShip.cs:3382), and `m_pregame` is false because IsMultiplayer is false | ✅ |
| Server acks, sends state 63, sends snapshots 64 | `Server.AccelerateInputs`, `SendUpdatedStateToPlayers`, `SendSnapshotsToPlayers` (PlayerShip.cs:3366-3373; Server.cs:607-770). olmod's prefix sends vanilla 64 unless the client advertised `nocompress_0_3_6`. Our client never sends capabilities, because that only happens in `OnAcceptedToLobby` (`olmod/MPTweaks.cs:268ff`; `olmod/MPNoPositionCompression.cs:250-320`). Ack msg 71 is sent from `QueueNewInputsForProcessingOnServer` (Server.cs:704-709) with no gate | `IsMultiplayerActive && Server.IsActive()` | ⛔ on the host |
| Client receives snapshots | `Client.OnPlayerSnapshotToClient`, olmod prefix (`olmod/MPNoPositionCompression.cs:193-206`; stock Client.cs:615-622) | MatchState PREGAME **or** InGameplay | ✅ |
| Client draws the remote ship (the host's) | Stock: `Client.FixedUpdate` → `UpdateInterpolationBuffer` and `Client.Update` → `InterpolateRemotePlayers` (Client.cs:61-77, 717-802). With olmod: `Client.FixedUpdate` is disabled, and `InterpolateRemotePlayers` → `MPClientShipReckoning.updatePlayerPositions()` (`olmod/MPClientExtrapolation.cs:737-760`) | olmod: `!IsServer() && MatchState ∈ {PLAYING, POSTGAME}` | ⛔ MatchState=PREGAME |
| Host draws the remote ship (the client's) | The server simulates it directly, so its transform is authoritative | none | ✅ |
| Remote firing | `Client.ProcessCachedControlsRemote` (Client.cs:881-889), `FixedUpdateProcessControls` firing (PlayerShip.cs:3384-3387), msgs 66/67/70 (`OnFireProjectileToClient`, Client.cs:650-674) | `IsMultiplayer && InGameplay` on the client for remote ships. That is false for MISSION, so a remote ship's shots are not drawn on the client except through msg 70 | not needed for Phase 1 |

**Physics must match on both ends.** Ship tuning depends on `IsMultiplayerActive`: boost 1.65 vs 1.6 (PlayerShip.cs:3403-3414), the ×1.2 roll and turn factors (3871, 3930) and others (3490, 3526, 3649-3713). If the host simulates the client's ship with `false` while the client predicts with `true`, every reconcile corrects the ship and it rubber-bands. **Both peers must therefore have `IsMultiplayerActive = true`.** That is patch S1.

### 2.5 Client running as MISSION while connected to a remote host

- **Robots:** the client runs the full SP robot AI locally, against its own ship (after C4), so they desync from the host. That is acceptable for Phase 1. Robot damage and kills are local to each instance.
- **Its own server:** `Client.Connect` shuts down the client's local server (Client.cs:121-124). Nothing in the PLAY_GAME or level path restarts it. Only the three menu screens do (step 2), and patch C3 blocks those.
- **Disconnect:** `Client.OnDisconnectMsg` (Client.cs:342-361) only cleans up if `IsMultiplayerSceneLoaded()`. With C1 that is false, so if the host quits, the client stays in its now-offline level. That is acceptable for Phase 1, but log it. A Phase 2 patch should return the client to the menu.
- **Level end:** each instance runs its own SP exit and debrief independently. Out of scope for now.

### 2.6 Side effects of `IsMultiplayerActive = true` in a MISSION game (both peers)

The flag has 136 references in 13 files. The ones that matter here:

- The game is never paused (`GamePaused`, GameplayManager.cs:652). That is good for co-op.
- Cheats off (PlayerShip.cs:4462).
- **Automap disabled** (PlayerShip.cs:4935).
- Quicksave off (PlayerShip.cs:5109).
- MP ship tuning (above).
- MP weapon behaviour: flak (PlayerShip.cs:1831), firing paths (6109-6797), and 19 sites in Projectile.cs.
- Item pickup rules (Item.cs, 9 sites).
- Recent-damage tracking (PlayerShip.cs:1670).
- **MP death handling:** loadout toggle and respawn UI (PlayerShip.cs:956ff).
  - **UNVERIFIED** whether SP `PlayerHasDied` → `DoneLevel(Died)` still fires.
  - Do not die during the Phase 1 test.

---

## 3. Gate list (everything that blocks Phase 1 under A)

| # | Gate | Location | Side | Fix |
|---|---|---|---|---|
| G1 | No way to connect at the menu | Client.cs:118 only reachable from the console (ClientDebug.cs:20-29) | client | C2 |
| G2 | Menus restart the local server, which drops the connection | MenuManager.cs:1957/2144/4328 → NetworkManager.cs:93-109 → Client.cs:143-151 | client | C3 |
| G3 | msg 48 never sent in SP | Server.cs:75-80 (only from the lobby) | host | H2/H3 |
| G4 | msg 49 only `if (IsMultiplayer)`, so Client.Update never calls AddPlayer | Server.cs:205-209; Client.cs:63 | host | H2/H3 |
| G5 | LoadScene only searches MultiplayerMission and otherwise exits to the menu | NetworkManager.cs:113-119 | client | C1 |
| G6 | Spawn point is the same spot as the host, or the origin | Server.cs:299-304; NetworkSpawnPoints.cs:11-34 | host | H4/H5 |
| G7 | Robots on the client aim at the destroyed plain ship | RobotManager.cs:298; Robot.cs:1949; PropReactorTurret.cs:73; Player.cs:4105-4109 | client | C4 |
| G8 | `IsMultiplayerActive` is false. This gates input send, reconcile, smoothing, and server state/snapshot send | PlayerShip.cs:3341/3350/3366/4449; NetworkMatch.cs:1828 | both | S1 |
| G9 | `NetworkMatch.InGameplay()` is false (PREGAME). This gates input send, reconcile and olmod interpolation | Player.cs:4915; Client.cs:841; `olmod/MPClientExtrapolation.cs:741` | both | S1 |
| — | Not a gate (verified): Server.OnConnect / handshake / lobby | Server.cs:152-155; Client.cs:336 | | |
| — | Not a gate: server-side remote simulation; snapshot receive in PREGAME | PlayerShip.cs:4007-4010; Client.cs:617 | | |
| — | Not a gate: `ProcessPlaying`/`MaybeEnd` with MatchState=PLAYING. It returns early while `!IsMultiplayerSceneLoaded()`, which C1 keeps false on both peers | NetworkMatch.cs:2212-2217 | | |

---

## 4. Patch list (approach A)

All patches only activate when the coop flags are set (`CoopConfig.IsHost` from `-coophost`, `CoopConfig.JoinIp` from
`-coopjoin <ip>`, `CoopConfig.Port` from `-coopport <n>`, default **7777**). Without the flags the game is
unchanged.

| ID | Target | Kind | What it does |
|---|---|---|---|
| **H1** (optional) | `Server.Listen(int port)` | Prefix | Host only: if `port == 0`, set `port = CoopConfig.Port`. This is the same thing olmod's `-port` does (`olmod/ServerPort.cs:6-22`), so H1 can be dropped by launching the host with `-port 7777`. Keep H1 so `-coophost` works on its own, and it is harmless if both set the same value. Note that `Listen(port)` with a fixed port does **not** retry (Server.cs:58). If the port is taken there is no server; log `Server.GetListenPort()` after Init and warn when it is 0. |
| **H2** | `Server.OnConnect(NetworkMessage msg)` | Postfix | Host: if `msg.conn.connectionId != 0` and `GameplayManager.LevelIsLoaded` (GameplayManager.cs:664) and `m_level_info != null`, call `CoopHost.SendScene(msg.conn)`. That does `NetworkServer.SendToClient(id, 48, new StringMessage(FileName))` and then `SendToClient(id, 49, same)`. If the host is still in the menu, do nothing; H3 covers it. |
| **H3** | `Server.OnSceneLoad(string name)` | Postfix | Host, `!IsMultiplayer`: for each connection in `NetworkServer.connections` with `connectionId != 0` that is connected, call `NetworkServer.SetClientNotReady(conn)` and then `CoopHost.SendScene(conn)`. **Never send to connection 0**, because that would make the host run `LoadScene` itself. |
| **H4** | `LevelData.Awake()` | Postfix | Host: record `CoopHost.SpawnPos/SpawnRot = GameManager.m_player_ship.c_transform` pose. At that point the ship has just been placed at LegacyPlayerStart (LevelData.cs:154-157). |
| **H5** | `NetworkSpawnPoints.ChooseSpawnPoint(MpTeam team)` | Postfix (runs after olmod's prefix) | Host, `!IsMultiplayer`, and `CoopHost.SpawnPosValid`: set `__result = new LevelData.SpawnPoint(SpawnPos + SpawnRot * offset(n), SpawnRot, 0)` (constructor LevelData.cs:27). Use `offset(n)` = n × (0, 0, -4) m, i.e. behind the host, where n is the number of remote players. This leaves `Server.OnAddPlayerMessage` untouched. The **host's own** AddPlayer also goes through ChooseSpawnPoint before H4 has recorded anything, so guard on `SpawnPosValid` (set in H4, cleared on scene unload). The host is then moved to LegacyPlayerStart by stock code anyway. **UNVERIFIED** whether 4 m behind the start is open space in sp_outer_01; tune it after the first test. |
| **S1** | `NetworkMatch.StartPreGame()` | Postfix | Both peers, coop active and `!IsMultiplayer`: set `GameplayManager.IsMultiplayerActive = true` and call `NetworkMatch.SetMatchState(MatchState.PLAYING)` (NetworkMatch.cs:1977). This removes G8 and G9. It does not start any MP match logic, because `ProcessPlaying` stays inert (§3). Also reset `IsMultiplayerActive = false` when leaving to the menu: postfix `GameplayManager.DoneLevel`, or rely on `Player.ExitMultiplayerToMainMenu` / `MenuManager.cs:6214`, **UNVERIFIED** which path SP quit takes. |
| **C1** | `Overload.NetworkManager.LoadScene(string name)` | Prefix, `Priority.First`, returns false when handled | Client in coop: `idx = GameManager.StoryMission.FindLevelIndex(name)`. If `idx >= 0`: `UIManager.DestroyAll(true); GameplayManager.CreateNewGame(GameManager.StoryMission, idx); MenuManager.ChangeMenuState(MenuState.PLAY_GAME); return false;`. Otherwise return true, which falls through to the stock MP lookup. Do **not** set `m_network_scene_loading`. |
| **C2** | `MenuManager.MainMenuUpdate()` (MenuManager.cs:1821; called at 875-877) | Postfix | Client: one-shot. Once `m_menu_sub_state == MenuSubState.ACTIVE` and `PilotManager.ActivePilot != null`, wait about 1 s and call `Client.Connect(CoopConfig.JoinIp, CoopConfig.Port)`. Retry every 5 s while `!Client.IsConnected()`, up to N times. Show a HUD or log line. |
| **C3** | `Overload.NetworkManager.StartServerWithLocalConnection()` | Prefix | Client: return false while `CoopClient.Joined` (set by C2, cleared on disconnect). This keeps the connection to the host. |
| **C4** | `Player.OnStartLocalPlayer()` | Postfix | Client (`!Server.IsActive()`): `Robot.c_target_transform = GameManager.m_player_ship.transform`. Set the private static `Robot.m_player_ship` through `AccessTools.Field`. For each `PropReactorTurret` in the scene, set `c_target_transform` / `c_target_go` (field visibility **UNVERIFIED**). On the host, the networked ship already exists before `StartLevel`, so this is not needed there. |

**Count: 9 patches (H2, H3, H4, H5, S1, C1, C2, C3, C4), plus H1 as an optional tenth.**
The bare minimum to see two ships move is 8: C4 only stops errors from client-side robots and can be
dropped. Plan on 9.

Also add a small `CoopConfig` parser in `CoopCore.EnsureInit`, which already parses args at
`src/olcoop/CoopCore.cs:34`. Change the log file name to include the process id: the current name
`olcoop-YYYYMMDD-HHMMSS.log` can collide when both instances start in the same second, and the two
processes would fight over one file.

Run `tools/VerifyPatches.cs` against all the new targets. `ChooseSpawnPoint`, `OnConnect`, `OnSceneLoad`
and `MainMenuUpdate` are static, and two of them are private.

---

## 5. Approach B (MP mode loading a story level): gates for comparison

Host creates an olmod LAN match → lobby → countdown → `ProcessLobbyCountdown` (NetworkMatch.cs:3027) picks a
scene from `MultiplayerMission` or the playlist → `Server.ChangeScene` → msg 48 → `NetworkManager.LoadScene`.

| Gate | Location | Patch |
|---|---|---|
| Scene picked from the MP mission or playlist | NetworkMatch.cs:3046-3075 | Prefix to substitute `sp_outer_01` |
| LoadScene searches MP only. If the client resolves the level in StoryMission, `m_game_type` becomes MISSION and the MP gates switch off. If it keeps MULTIPLAYER, `CreateNewGame` needs a fake MULTIPLAYER mission entry | NetworkManager.cs:111-127; GameplayManager.cs:733 | C1-like, plus a game-type override |
| Lobby needs 2 players | Phase 0 test 3 (`tests/phase0-results.md`). Two instances satisfy this | none |
| Robots frozen: `RobotManager.Update` MP early return | RobotManager.cs:316-326 | Must be un-gated in Phase 2 |
| LegacyPlayerStart ignored, MP spawn points used | LevelData.cs:154; Server.cs:298 | spawn patch |
| Mode file `ReadMultiplayerModeFile` for an SP level | Server.cs:208; olmod `MPCustomModeFile` | Probably suppress. **UNVERIFIED** what happens when it is missing |
| Random powerups (`PowerupLevelStart`, `MaybeSpawnPowerup`) | NetworkMatch.cs:1847, 2233 | suppress |
| Time/score end (`MaybeEnd`), and `MaybeAdjustMatchTimeLimit` sets a 10 s limit when there is 1 remote player in non-private matches | NetworkMatch.cs:2235, 2243-2251 | suppress |
| MP loadouts replace SP weapons on respawn | Client.cs:526-535; NetworkSpawnPlayer.cs:82 | suppress |
| Pregame camera and countdown, `m_pregame` | NetworkSpawnPlayer.cs:41-45; NetworkMatch.cs:2147 | keep or speed up |
| Plus 80 `IsMultiplayer` gates across 19 files (automap, robot AI, scripts, `LevelCustomInfo.Reset`) | recon-flow §3 | Phase 2+ |

B reaches "two ships moving" with about 2 patches (scene choice and level lookup), because all the netcode
is native. But the host is no longer "playing campaign in SP": robots are inert and every later phase starts
by undoing MP gates. A keeps the SP world intact and only switches on the netcode. **A is recommended.**
If A fails on an UNVERIFIED point (for example scene NetworkIdentities in step 13), B is the fallback.

---

## 6. Two instances on one PC

- **Single-instance lock:** none in the game. A grep for `Mutex`, `SingleInstance` and similar finds only an OpenVR enum (Valve.VR/EVRInitError.cs:58).
- **Steam:** `SteamManager.Awake` calls `SteamAPI.RestartAppIfNecessary(AppId_t.Invalid)`, which quits if it returns true (SteamManager.cs:47-53), and then `SteamAPI.Init()`.
  - **A failed Init is only a warning** (SteamManager.cs:61-65). The game keeps running and code checks `SteamManager.Initialized` (Steam.cs:128, Platform.cs:139).
  - **UNVERIFIED:** whether the Steam client refuses to start a second copy launched through Steam ("game already running"). Launch both copies **directly** with `olmod.exe` (not through Steam) to avoid this.
- **Ports:**
  - The host binds 7777 (H1, or `-port 7777`).
  - The client also binds a random port from 7000 to 7999 at startup (NetworkManager.cs:105). If 7777 is taken, `NetworkServer.Listen` fails and it retries (Server.cs:47-54), and `Client.Connect` shuts this server down anyway.
  - **Start the host first.** If the client starts first, there is a 1-in-1000 chance its random port is 7777, and the host's fixed `Listen(7777)` would then fail.
- **Background running:** `MenuManager.InitUpdate` sets `Application.runInBackground = m_run_in_backgroud || IsMultiplayer` (MenuManager.cs:1101). For a MISSION game, an unfocused instance would stop updating and time out the connection (`DisconnectTimeout` = 4000 ms, NetworkManager.cs:86).
  - **Pass `-runInBackground` on both** (GameManager.cs:422-424).
  - Keyboard and mouse input only reaches the focused window, so test by alternating focus. Or put a gamepad on one instance (**UNVERIFIED** whether Unity routes a pad to an unfocused window).
- **VR:** run both in flat mode (no `-vrmode`). One headset cannot drive two instances.
- **Window size:** add `-screen-fullscreen 0 -screen-width 960 -screen-height 540` (Unity standard args) so both fit on screen.
- **Logs:** Unity `output_log.txt` / `Player.log` is one file per user profile for Unity 5 standalone, so two instances overwrite each other (**UNVERIFIED** for this build). Use `-logFile host.log` / `-logFile client.log`, plus the olcoop log with the PID in its name.
- **olmod config and pilot files:** both instances share the same pilot. Saving pilot prefs at quit (`GameManager.OnApplicationQuit` → `PilotManager.Save`) from two processes is last-writer-wins. That is fine for testing.

## 7. Test procedure (Phase 1)

Build `Mod-olcoop.dll` with the patches above and run VerifyPatches. Then:

1. **Host:** `olmod.exe -modded -coophost -coopport 7777 -runInBackground -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile host.log`
   - Check the host log for `Server listening on port 7777` (NetworkManager.cs:106) and the olcoop INIT line.
   - Start a new campaign game on level 1 (`sp_outer_01`) as usual and fly a few metres from the start.
2. **Client:** `olmod.exe -modded -coopjoin 127.0.0.1 -coopport 7777 -runInBackground -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile client.log`
   - Pick the pilot and **stay on the main menu** (C3 guards the menus, but don't rely on it in the first test).
3. Expected log sequence:
   - Client: `Attempting to connect to ip: 127.0.0.1 port: 7777` (Client.cs:120), then `Connected to server` (Client.cs:329).
   - Host: `Client connected` (Server.cs:154), then `[olcoop] sent scene sp_outer_01 to conn 1`.
   - Client: `[olcoop] LoadScene sp_outer_01 -> StoryMission idx 0`, then the level loads.
   - Client: `OnStartLocalPlayer netId=3 local=True server=False` (Phase 0 instrumentation already logs this).
   - Both: `[olcoop] coop netcode active: IsMultiplayerActive=True state=PLAYING`.
4. **Pass criteria:**
   - (a) The client flies its own ship and the movement is smooth, with no constant rubber-banding.
   - (b) The host window shows the client's ship moving.
   - (c) The client window shows the host's ship moving.
   - (d) Neither log shows `ExitMatchToMainMenu` / `not found in list of multiplayer levels` (NetworkManager.cs:116).
   - (e) No `Could not find Spawn Point` (Server.cs:301) and no MissingReferenceException spam.
5. **Record:**
   - The number of `NetworkIdentity` scene objects spawned on the client. Log `ClientScene.objects.Count` after AddPlayer (§2.3 step 13).
   - Whether robots on the client behave (local AI).
   - Ping and jitter of the host ship as seen by the client. Expect some jitter: olmod extrapolates the host's own ship because `m_send_updated_state` is never set for the local player (`olmod/MPNoPositionCompression.cs:276-298`). If it looks bad, set it for the host player before the snapshot send.
6. **Negative tests:**
   - Start the client first, then the host. The client should retry until the host is in the level (H3 path).
   - Quit the host: the client keeps running offline (known gap, §2.5).
   - Do **not** die in this test (§2.6).

## 8. Risks

1. **Chunk culling on the host.** `ChunkManager.ActivateChunks` (ChunkManager.cs:176-183) and `RobotManager` segment culling follow the host's own ship only. If the client's ship flies into a chunk that is inactive on the host, the server may simulate it without level colliders (**UNVERIFIED** whether chunks hold collision). The client would then be reconciled through walls. Phase 1: keep the ships together. Phase 2: activate the union of all players' segments on the host.
2. Scene `NetworkIdentity` objects being spawned or destroyed on the client (§2.3 step 13). **UNVERIFIED.**
3. **Server-side state of the remote player.** The host's copy of the client's `Player` never runs `InitializeForNewGame` (it only runs for the local player, Player.cs:4116). Its weapons, energy and ammo are defaults. SyncVars from the server may overwrite the client's SP loadout. Firing will not be consistent. That is out of scope for Phase 1, and Phase 2 needs a loadout sync.
4. **The `IsMultiplayerActive = true` side effects** (§2.6): MP handling tuning, no automap or quicksave, the MP death path. That is acceptable for co-op, but it is a deliberate gameplay change.
5. **The host ship on the client is extrapolated by olmod** (step 5 of §7).
6. The Harmony prefix order with olmod's `LoadScene` prefix (C1). **UNVERIFIED.**
7. **Disconnect handling is minimal** (§2.5). A UNET timeout of 4 s applies, so pass `-runInBackground` on both.
8. **H1 vs olmod `-port`.** If both are present they set the same value. If someone passes a different `-port`, olmod's prefix runs in an unspecified order relative to ours, and the port is ambiguous. Document that `-coopport` wins, or read olmod's arg.
