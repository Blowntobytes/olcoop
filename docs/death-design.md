# Co-op death design: SPECTATE / RESPAWN / HARDCORE

Paths are relative to `refs/Assembly-CSharp/Overload/` unless prefixed (`olmod/` = `/home/claude/olmod/GameMod/`,
`src/` = `src/olcoop/`). Line numbers are for the decompiled build. **UNVERIFIED** marks anything not proven by code or logs.

---

## 0. Key findings (read first)

1. **The test death was the JOINER's, not the host's.** `tests/logs/phase2a-run3/olcoop-20261003-090513-pid25484.log:560-584`:
   the host ship (netId 2) had hp=195 and was alive. Joiner netId 75 had hp=-17 and was dying/dead. Even so, the **host** logged
   `PlayerHasDied` → `DoneLevel reason=Died`. **Both joiners** did the same within 50 ms (pid27472:429, pid28132:350).
2. Why: in co-op `NetworkManager.IsMultiplayerSceneLoaded()` is false on every peer (C1 deliberately never sets
   `m_network_scene`; NetworkManager.cs:160-163). So `PlayerShip.Update` (PlayerShip.cs:1039-1049) runs the **single-player
   `DeadUpdate()` for every ship on every machine**, including remote ones. The SP `DeadUpdate` (PlayerShip.cs:3101-3137)
   calls `GameplayManager.PlayerHasDied()` with **no `isLocalPlayer` check** (3126). **Any ship that dies ends the level on every peer.**
3. The MP respawn pipeline (`NetworkSpawnPlayer.DeadUpdate` → `CmdReadyToRespawn` → `Respawn`) is therefore **never reached** in
   co-op. Automatic respawn is already blocked, and there is nothing to un-block. We drive respawn ourselves.
4. The SP death branch in `DyingUpdate` calls `ExplodeCockpit()` (PlayerShip.cs:3065-3072). This **permanently detaches the ship's mesh
   pieces** (`component.parent = null`, 2706) as debris, on every peer. A ship respawned after that would be missing its body.
   It must be patched for RESPAWN, and for SPECTATE too if the ship is reused next level (it isn't, because ships are re-created per level).
5. A joiner that is **already in a level** and receives msg 48 again will **not** reload with today's C1. C1 calls
   `MenuManager.ChangeMenuState(PLAY_GAME)`, which only sets `m_next_menu_state`. `MenuManager.Update` only runs while
   `GameManager.m_game_state == MENU` (GameManager.cs:999-1009). A joiner in GAMEPLAY never runs `PlayGameUpdate`.
   C1 must also switch the game state (§4.3).

---

## 1. Death pipeline

### 1.1 Server (host) side, for any ship

| Step | Code | Gate / notes |
|---|---|---|
| Damage | `public void PlayerShip.ApplyDamage(DamageInfo di)` PlayerShip.cs:1605 | `!NetworkManager.IsServer() → return` (1607). Server-only. `IsMultiplayer` picks MP vs SP armor and difficulty scaling (1615, 1626-1638). Co-op = SP scaling. `IsMultiplayerActive` → `AddRecentDamage` (1670). |
| HP sync | `CallRpcApplyDamage(hp, …)` 1676 → `[ClientRpc] RpcApplyDamage(float hitpoints, float damage, float damage_scaled, float damage_min)` 1495 | Runs on every client, the host's local client included. Early-out `IsMultiplayerActive && !InGameplay()` (1497). InGameplay holds because MatchState is PLAYING (S1). |
| hp ≤ 0 | 1694: `if (m_cannot_die \|\| !(hp <= 0)) return;` then stats (`AddStatsPlayerKiller` only if `isLocalPlayer`, 1698-1701). `OnKilledByPlayer` only if MP scene (1702). Then `CallRpcApplyDamageWhenDying(push_force, push_dir, damage)` 1708 | **Called on every damage tick while hp ≤ 0**. `m_death_stats_recorded` only guards the stats. There is no explicit "StartDying" on the server: dying starts from the RPC. |
| RPC send | `CallRpcApplyDamageWhenDying` 7164-7180 | Requires `NetworkServer.active`. Sent to all ready connections, the local (host) connection included. |
| Start dying (every peer) | `[ClientRpc] RpcApplyDamageWhenDying(float push_force, Vector3 push_dir, float damage)` 1569 | Returns if `(IsMultiplayerActive && !InGameplay()) \|\| m_dead_timer <= 0`. If `!m_dying`: shake and sound if `isLocalPlayer`, push. The MP death overlay is only created when `IsMultiplayer` (1586-1593), so not in co-op. Then `StartDying(vec)` 1597. Else (already dying) it only adds force. |
| `public void StartDying(Vector3 dir)` | 2770-2830 | Returns if MP scene and not InGameplay (n/a). Sets `m_dying=true, m_dying_timer=3.5, m_dead_timer=100, m_ready_to_respawn=false`. FX. `isLocalPlayer`: FadeMusic if `!IsMultiplayer`, dying camera (`StartDyingCamera`), `UIElement.ReadyToRespawn` / `RespawnTimer=26.5`. Remote ship: MP flash FX. |
| Per frame while dying | `PlayerShip.Update` 1051-1055 → `private void DyingUpdate()` 2989 | At `m_dying_timer <= 0` (3047): colliders off, `m_dead=true` (3053). `m_dead_timer = IsMultiplayer ? NetworkMatch.m_respawn_time_seconds : Random(1.6,1.8)` (3054-3061) → **SP value in co-op**. `IsMultiplayerSceneLoaded ? SpewItemsOnDeath() : ExplodeCockpit()` (3065-3072) → **ExplodeCockpit in co-op**. `c_cockpit.SetActive(false)`. Note: `m_dying` is **not** cleared, so it stays true together with `m_dead`. |
| Per frame while dead | `PlayerShip.Update` 1039-1049: `IsMultiplayerSceneLoaded() ? NetworkSpawnPlayer.DeadUpdate(this) : DeadUpdate()` | **Co-op takes `DeadUpdate()` (SP) for every ship.** |
| `private void DeadUpdate()` | 3101-3137 | Timer countdown and screen fade (local). At ≤0: colliders on, `m_dying=m_dead=false`, **`GameplayManager.PlayerHasDied()` (3126) with no `isLocalPlayer` check**, camera reset. |
| `public static void GameplayManager.PlayerHasDied()` | GameplayManager.cs:2144-2165 | `DoneLevel(DoneReason.Died)` (2148), then for MISSION: `Scores.UpdateLifetimeStatsSP` + `SwitchToMenu(MenuState.PLAYER_DIED)` (2162-2163). |
| `public static void DoneLevel(DoneReason reason)` | GameplayManager.cs:2002 | `KillLevelEntities` (robots deactivated, projectiles and particles destroyed), stats, prefs. Our postfixes S1b (`src/Phase1Session.cs` S1b_DoneLevel) and F6 (`src/Phase1Fixes.cs`) turn co-op netcode **off**. |
| PLAYER_DIED menu | MenuManager.cs:917 → `GameOverUpdate(false)` 6395 | DEATH_MENU: Retry → `DIFFICULTY_SELECT` with `m_goto_restart_from_difficulty` → `CreateRestartGame()` + `PLAY_GAME` (MenuManager.cs:2716-2727). Load → LOAD_MENU. Quit → MAIN_MENU. |

### 1.2 Which gate matters where

- `IsMultiplayer` (false in co-op): damage scaling, `m_dead_timer` value, MP death overlay, `m_pregame`, FadeMusic. All of these take the SP branch.
- `IsMultiplayerActive` (true in co-op): the RPC early-outs (with InGameplay), recent damage, and the **MP dead/dying input block in
  `Update` (PlayerShip.cs:956-994)**. That block contains the loadout toggle `CallCmdToggleLoadout` (959-963), the FIRE_FLARE `ReadyToRespawn`
  toggle (964-971), PAUSE → death pause menu with the VR death-cam parent (973-993), and `ShowMpScoreboard = VIEW_MAP` (994).
  **It is active in co-op for the dead local ship and must be suppressed.**
- `IsMultiplayerSceneLoaded()` (false in co-op): picks SP `DeadUpdate`, `ExplodeCockpit` and no item spew. This is **the root cause**.
- `isLocalPlayer`: camera, UI, sound only. It is **not** a gate on `PlayerHasDied`.

### 1.3 What happens today

- **(a) Host ship dies:** host `ApplyDamage` → RPC (to the host's local client and to joiners) → `StartDying` on all peers → each peer's
  SP `DeadUpdate` after ~3.5 s + ~1.7 s → `PlayerHasDied` on **every** peer → everyone gets DoneLevel(Died) and their own death menu,
  and netcode turns OFF everywhere.
- **(b) Joiner ship dies on the host:** exactly the same. The host's SP `DeadUpdate` runs for the remote ship and the host gets its death menu
  (this is the observed log).
- **(c) On the joiner's own machine:** the joiner never runs `ApplyDamage` (server-only). It receives `RpcApplyDamage` (hp) and then
  `RpcApplyDamageWhenDying` → `StartDying` (dying camera because it is local) → `DyingUpdate` → `ExplodeCockpit` → SP `DeadUpdate` →
  **`PlayerHasDied` → `DoneLevel(Died)` → PLAYER_DIED locally**. It also does this for the host's and other joiners' ships.
  While dying or dead, the joiner stops sending input (`Player.NeedToSendFixedUpdateMessages`, Player.cs:4915). The server
  also skips simulating a dying or dead remote ship (Server.cs:599, 633).
  A Retry on the joiner's death menu would run `CreateRestartGame` locally, which desyncs it from the host.

---

## 2. Respawn pipeline

### 2.1 Stock MP (never reached in co-op, see §0.3)

- `public static void NetworkSpawnPlayer.DeadUpdate(PlayerShip)` NetworkSpawnPlayer.cs:222-248. When the local player is ready
  (`UIElement.ReadyToRespawn` or `RespawnTimer<=0`), it calls `CallCmdReadyToRespawn()` (PlayerShip.cs:6962) → `[Command] CmdReadyToRespawn()` 2586
  sets `m_ready_to_respawn`. On the server, `if (IsServer && InGameplay && m_ready_to_respawn) Respawn(ship)`, then `m_dead_timer = ∞`.
- `public static void NetworkSpawnPlayer.Respawn(PlayerShip)` 256-264: `ChooseSpawnPoint(team)` (our H5 postfix moves it next to
  the **host** ship), sets the transform, `StartSpawnInvul` (→ `Player.StartInvul`, server-only, Player.cs:2499), `m_input_deficit=0`,
  `Server.RespawnPlayer(player,pos,rot)`.
- `public static void Server.RespawnPlayer(Player, Vector3, Quaternion)` Server.cs:382-391: `RespawnMessage{netId, lobby_id=connectionToClient.connectionId,
  pos, rot, use_loadout1}` → `NetworkServer.SendToAll(50, …)`. Like every server message, it also reaches the host's local client.
- `private static void Client.OnRespawnMsg(NetworkMessage)` Client.cs:506-547, on every peer, the host included:
  `m_pregame` ? cockpit on : **`Player.RestorePlayerShipDataAfterRespawn()`** (Player.cs:4183-4218: hp=100, energy=100, **ammo=0**,
  `m_dying=m_dead=false`, `m_dead_timer=∞`, colliders on, camera re-parented and cockpit on for local).
  Then **if `!m_spectator` and `NetworkMatch.m_player_loadout_data` contains `lobby_id` → `SetMultiplayerLoadout`** (Client.cs:525-539).
  That function **locks all weapons and missiles, zeroes upgrades and ammo** (NetworkSpawnPlayer.cs:82-133). After that: transform, rim colour,
  `m_lerp_wait_for_respawn_pos`, `DoSpawnEffects`.
- Loadout dictionary: each joiner's `Client.OnSceneLoadMsg` sends msg 60 with `lobby_id = NetworkMatch.m_my_lobby_id` (Client.cs:220, 380).
  On the host that stores `m_player_loadout_data[0]` (Server.cs:286-290 → NetworkMatch.cs:2824). The host's own respawn uses
  `lobby_id = 0` (local connection), so **the host's SP loadout would be wiped** by `SetMultiplayerLoadout`. A joiner's respawn uses lobby_id 1, 2, …
  and is not in the dictionary. On joiners the dictionary is probably empty (no msg 61 in co-op; **UNVERIFIED**, it could hold data from an
  earlier MP session).

### 2.2 What to patch

**(i) Block joiner-initiated or automatic MP respawn.** It is already unreachable (§0.3). Defensive patches:
- `PlayerShip.CmdReadyToRespawn` prefix: in co-op, `return false`.
- Suppress the dead-input block at PlayerShip.cs:956-994 (loadout toggle command, ReadyToRespawn toggle, MP death-pause menu,
  scoreboard). Approach: prefix and finalizer on `PlayerShip.Update`. For `isLocalPlayer && (m_dying||m_dead)` in co-op, temporarily set
  `IsMultiplayerActive=false` (reuse `SpRulesScope`). Inside `Update`, the dying and dead paths read `IsMultiplayerActive` only at
  956, because they return at 1048 and 1054 before any other use. The callees `DyingUpdate` and `DeadUpdate` do not read it.
  Alternative: a transpiler on the 956 condition.
- `PlayerShip.ApplyDamage` prefix: in co-op, `if (m_dead) return false`. This stops repeated death RPCs and stats on a corpse.

**(ii) Server-side respawn at an arbitrary pose after our cooldown.** Add the host-only `CoopDeath.ServerRespawn(PlayerShip s, Vector3 pos, Quaternion rot)`,
which mirrors `NetworkSpawnPlayer.Respawn` without `ChooseSpawnPoint`:
```
s.c_transform.position = pos; s.c_transform.rotation = rot;
NetworkSpawnPlayer.StartSpawnInvul(s.c_player);   // server-only invul
s.c_player.m_input_deficit = 0;
s.m_death_stats_recorded = false;
Server.RespawnPlayer(s.c_player, pos, rot);        // msg 50 to all, host's local client included
```
`Client.OnRespawnMsg` then runs `RestorePlayerShipDataAfterRespawn` on every peer. Pick the pose with `CoopHost.TryAround(label, anchor.position,
anchor.rotation, k, ref sp)` (`src/Phase1Session.cs`), anchored on a **living** ship chosen by the host (not the dying ship). Generalise
`NextSpawnPoint` to take the anchor ship. Fallback order: another living ship, then the level start (`CoopHost.SpawnPos`).

**(iii) Full hp/energy, keep the SP loadout.**
- `NetworkSpawnPlayer.SetMultiplayerLoadout` prefix: in co-op `return false`. Also `SetMultiplayerCustomization` (only reached on pregame,
  so harmless; skip it too).
- `Player.RestorePlayerShipDataAfterRespawn`: prefix saves `m_ammo` (and anything else we want to keep), postfix restores it
  (stock zeroes ammo at 4189). hp and energy = 100 is the stock SP baseline. Weapons, missiles and upgrades are untouched by `Restore`.
  **UNVERIFIED:** which peer is authoritative for ammo and energy of a joiner's ship in co-op (Player state msg 63 contents). Restoring locally on
  every peer is safe either way.
- `ExplodeCockpit` (private, PlayerShip.cs:2661): prefix → in co-op `return false` and instead hide the ship's renderers
  (`foreach MeshRenderer in ship.GetComponentsInChildren<MeshRenderer>() enabled=false`, as olmod's `MPObserver.SetPlayerVisibility` does,
  `olmod/MPObserver.cs:57-63`). Optionally spawn the explosion FX that `DyingUpdate` already emits. On respawn (postfix
  `Client.OnRespawnMsg`, or `RestorePlayerShipDataAfterRespawn` postfix) set the renderers back to `enabled=true`.
  Do not use `c_main_ship_go.SetActive(false)` (the automap uses that, PlayerShip.cs:1814). **UNVERIFIED** whether it contains the camera rig.
  Also note that olmod's `MPObserver` "SetPlayerVisibility" only flips `MeshRenderer`. Check whether the ship has `SkinnedMeshRenderer`s (**UNVERIFIED**, probably not).

**Host's own ship.** It is the same code. The host ship is both a server object and the local player. `Server.RespawnPlayer` → local connection
→ the host's `Client.OnRespawnMsg` restores it and re-parents the camera (`isLocalPlayer` branch, Player.cs:4200-4211). `lobby_id=0` is
covered by the SetMultiplayerLoadout skip. `StartSpawnInvul` runs on the server, so it is valid on the host.

---

## 3. Spectator

### 3.1 How stock MP and olmod do it
- `Player.m_spectator` SyncVar (Player.cs:364, `Networkm_spectator` 717-727, set at 4142 from lobby data). A spectator ship has its
  cockpit, colliders and lights off and layer 2 (`UpdateNetworkPlayer` 4599-4605). The server skips it for snapshots and state
  (Server.cs:623, 654, 716, 734). Clients skip interpolation (Client.cs:792). `OnRespawnMsg` skips the loadout (525).
- olmod `MPObserver` (`olmod/MPObserver.cs`): it **moves the local ship** onto the observed player every frame in a `PlayerShip.Update`
  postfix (231-262), disables reconcile (12-19) and skips `FixedUpdateProcessControlsInternal` (264-270). It cycles with
  `SwitchObservedPlayer(prev)` over `NetworkManager.m_Players`, skipping spectators (92-114), and hides the observed ship's MeshRenderers in first person.

### 3.2 Recommendation: camera-only follow, don't touch `m_spectator`
Setting `Networkm_spectator` would change server snapshot and state behaviour, and must be cleared again for respawn. Moving the ship (MPObserver
style) fights reconcile on joiners. Instead, keep the dead ship exactly where it died, in `m_dead` (hidden, colliders off), and move **only
the camera**:
- On the local peer, when our dead timer finishes (§5.1), create `GameObject "olcoop_spectate_cam"`. Re-parent `ship.c_camera_transform`
  to it (localPos/rot = identity), and also `ship.c_viewer.c_ui_mesh_transform`. This is exactly what the stock VR death-pause does
  (PlayerShip.cs:978-987), so it is VR-safe: the HMD still drives the camera's local pose under the parent.
- Each frame (`PlayerShip.Update` postfix for the local dead ship, or `LateUpdate` on a helper MonoBehaviour), set the parent pose to the
  target's `c_transform` pose (first person; optionally offset back and up for third person) and lerp the rotation. Hide the target's
  MeshRenderers in first person, and restore them when switching away.
- `UIManager.SetScreenFade(0)`, `ShowCinematicBars(false)`, `GameManager.m_viewer.SetDamageEffects(-999)` (as MPObserver does). Show
  the HUD text "SPECTATING PLAYER n". In co-op `m_mp_name` is empty (log shows `name=''`), so use netId or a slot index, or send names
  in the new message 172.
- Undo: re-parent the camera to `ship.m_camera_parent`, localPos zero, `ResetCameraPosition()` (same as Player.cs:4202-4204).
  `RestorePlayerShipDataAfterRespawn` already does this for the local ship. Destroy the helper GameObject.
- Cycling: in the same postfix, `Controls.JustPressed(CCInput.FIRE_PRIMARY)` / `SWITCH_WEAPON` → next living ship in
  `NetworkManager.m_Players` (skip `m_dying||m_dead`). If the current target dies, auto-advance. If nobody is alive, freeze the camera
  (a team wipe is imminent).
- Input: the dead ship already ignores flight and fire input (`FixedUpdateProcessControls` requires `!m_dying && !m_dead`, PlayerShip.cs:3382). The
  dead-state MP UI block (956) is suppressed per §2.2(i). The host pause menu still works (the game never pauses when MP is active).
- Robots: `CoopTargets.Usable` (`src/Phase2Robots.cs:326`) already rejects `m_dying||m_dead`. Because our DeadUpdate replacement
  **keeps `m_dead=true`** until respawn or level end, robots ignore spectators. Remaining risk: `Choose()` falls back to `GameManager.m_player_ship`
  (Phase2Robots.cs:300), and `Push` returns early when `m_Players.Count < 2` (330). With the host dead and no usable target, robots aim at the
  host corpse. That is harmless because the colliders are off and `ApplyDamage` is blocked for `m_dead`. Optional: make robots idle.

---

## 4. Level reset for everyone

### 4.1 Host: cleanest restart
Use the same sequence as the death menu Retry (MenuManager.cs:2716-2727), skipping the menus:
```
GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit);   // KillLevelEntities; S1b/F6 turn netcode off
GameplayManager.CreateRestartGame();                          // GM:804 – m_restarting_level=true, CreateNewGame(same mission, same LevelNum)
GameplayManager.m_between_level_start = Time.realtimeSinceStartup;
GameplayManager.SwitchToMenu(MenuState.PLAY_GAME);            // GM:1148 – gameplay state MENUS + GameManager next state MENU
```
`PLAY_GAME` → `PlayGameUpdate` (MenuManager.cs:6965) → `LoadLevel` (GM:1052, async, same scene name reloads) → `GameManager.OnSceneLoaded`
(GameManager.cs:677-689) → `NetworkManager.OnSceneLoaded` → `Server.OnSceneLoad` → **our H3 postfix → `SendScene` to every verified joiner**
→ `StartPreGame` (S1, netcode on again) → `GameplayManager.OnSceneLoaded` (GM:1014) → `StartLevel` (GM:858). Because `m_restarting_level` is set,
StartLevel restores `m_level_start_gameplay_data` and `m_level_start_player_data` for the host's local player (GM:938-951), so the host gets its level-start
loadout back. `GameplayManager.ReloadLevel()` (GM:1073) is an alternative, but it expects a pre-started async load. Don't use it.

### 4.2 Joiners on reload
- **Problem (§0.5):** C1 (`src/Phase1Session.cs` C1_LoadSceneStory) calls `CreateNewGame` + `MenuManager.ChangeMenuState(PLAY_GAME)`. That works from
  the main menu (game state MENU) but **not from inside a level** (game state GAMEPLAY, MenuManager not updated).
  **Fix C1:** `if (GameManager.m_game_state == GameManager.GameState.GAMEPLAY || GameplayManager.LevelIsLoaded) GameplayManager.DoneLevel(Quit);`
  then `CreateNewGame(story, idx)` and **`GameplayManager.SwitchToMenu(MenuState.PLAY_GAME)`** (instead of `MenuManager.ChangeMenuState`)
  whenever the joiner is not already in the menus. This is also needed for "host advanced to the next level while joiners are in the old one".
  **UNVERIFIED:** whether a pending `PLAYER_DIED` or a pause menu on the joiner interferes. With §5 there is no death menu.
- Client side of msg 48: `Client.OnSceneLoadMsg` (Client.cs:368-382) clears `m_scene_loaded`, `m_scene_loaded_on_server` and `m_player_added`, and
  resets ticks. Msg 49 re-arms `AddPlayer` in `Client.Update` (Client.cs:61-72) once the scene has loaded. The same scene name works because both
  strings are cleared first.
- **UNVERIFIED (UNET):** reloading the host scene destroys the joiners' `Player` objects on the host. `SendScene` calls `SetClientNotReady(conn)`.
  If UNET keeps a stale `playerControllers[0]` entry for the connection, the joiner's new `AddPlayer` would be refused ("player already exists").
  The first level load after joining is a fresh connection, so the tests never covered this. Mitigation: in `SendScene`, before `SetClientNotReady`, call
  `NetworkServer.DestroyPlayersForConnection(conn)` when `conn.playerControllers.Count > 0`. Test this first (it also affects level advance).
- Joiner loadout: `Player.OnStartLocalPlayer` calls `InitializeForNewGame(m_level_info)` (phase1-design §2.3 step 12). Joiners therefore always
  get the level's default SP loadout on every level load, restart included. That is an existing limitation and is acceptable for "reset".
- World sync on reload: `P1_Registry` (`RobotManager.InitializeForNewLevel` postfix) runs `ResetForLevel` + `BuildRegistry` on both peers
  for every level start. The joiner then sends `RegistryReady` and the host replies with `Manifest` and the full robot set, as on a first join. Ordering
  is safe: the host's H3 (in `NetworkManager.OnSceneLoaded`) runs before the host's `StartLevel`/`InitializeForNewLevel` in the same frame
  (GameManager.cs:686-687), and the joiner only starts loading after receiving 48. **UNVERIFIED:** `RobotHostNet.OnRegistryReady`
  arriving while the host is in the menus. The host's level is always loaded first, so this should not happen.
  `CoopRobots.HostDifficulty` is resent (msg 171) inside `SendScene`.

### 4.3 Next level ("respawn at start of next level")
The host completes the level: `EscapeLevel` (GM:2045) → DoneLevel(Escaped) → debrief → CONTINUE → `AdvanceLevel()` (GM:846, `SetPendingLevelLoad`)
→ `GoToNextBriefing` → … → PLAY_GAME → `LoadLevel` → scene load → **`Server.OnSceneLoad` → H3 → `SendScene` to joiners**. So yes, the host
advancing brings joiners along (it needs the C1 in-level fix above and the UNET stale-player check). Every level load creates fresh ships
(the host through `LevelData.Awake` → `Client.AddPlayer`, joiners through `AddPlayer`), so the dead and spectator state disappears naturally. Our per-level
state must also be cleared (hook `LevelData.Awake`, as H4b does).

**Open issue (SPECTATE/RESPAWN):** the level exit sequence is driven by the **host's** `GameManager.m_player_ship` (GM:3340-3356, 3395-3407,
3525-3540). If the host is spectating, a joiner reaching the exit does not end the level on the host. Out of scope here, but it must be handled
before SPECTATE is fully playable. The reactor escape timer kills the local ship (GM:1381-1392) and goes through the same death path.

---

## 5. SP death screen in co-op

### 5.1 Replace SP `DeadUpdate` (all modes, all peers)
Prefix `PlayerShip.DeadUpdate` (private, PlayerShip.cs:3101). In co-op (`CoopConfig.Active && !IsMultiplayer && IsMultiplayerActive`) → `return false`
and run `CoopDeath.DeadUpdate(ship)`:
- `m_dead_timer -= FRAMETIME_GAME; c_rigidbody.velocity *= 0.95f;` For local: fade as stock until the timer reaches 0, then
  `SetScreenFade(0)`, `UIManager.DestroyType(HUD)`, and enter spectate (§3.2) once.
- **Never** call `PlayerHasDied`. Keep `m_dying=m_dead=true`. Clamp `m_dead_timer` to a value ≤ 0 (e.g. `-1`) so `RpcApplyDamageWhenDying` early-outs (1571).
- The host (only) notes the death time per netId for the RESPAWN cooldown and runs the all-dead check (§5.3).

As a safety net, also add a `GameplayManager.PlayerHasDied` prefix: in co-op → log and `return false`. This catches any path we missed, for example
the secret-level and escape-timer variants.

### 5.2 HARDCORE: evaluated
"Let the SP flow run and bring the joiners" means the host gets the SP death menu and must pick Retry → difficulty select → restart.
Meanwhile, the joiners would each also be in their own death menu (today's behaviour) or in a dead level, and the Retry/Load/Quit options make no
sense for the team (Load especially would desync). **Rejected.** HARDCORE should use the same automatic team reset as the other modes, with no menu:
on any death, the host broadcasts 175 TeamReset (reason, ~3 s delay so the death animation plays) and then runs §4.1. Players who want to quit
still have the pause menu.

### 5.3 Host-side mode logic (one tick, e.g. in our existing `RobotManager.Update` postfix P14 or a `GameplayManager.Update` postfix)
- `alive = m_Players.Count(p => p && p.c_player_ship && !m_dying && !m_dead)`.
- HARDCORE: on the first `StartDying` of any ship (patch: `PlayerShip.StartDying` postfix on the host) → schedule a reset.
- SPECTATE: `alive == 0` → schedule a reset.
- RESPAWN: for each dead ship, when `now - deathTime >= cooldown` and `alive > 0` → `ServerRespawn` near a living ship. If `alive == 0` at any
  moment → schedule a reset, which cancels pending respawns.
- Treat a ship that is still dying as not alive (no clutch respawn after the last player's ship starts exploding).
- Schedule a reset only once per level (latch). Clear the latch in `LevelData.Awake`.

---

## 6. Patch list

| # | Target | Kind | Peer | Purpose |
|---|---|---|---|---|
| D1 | `PlayerShip.DeadUpdate()` (private) | Prefix, return false | all | §5.1. No `PlayerHasDied`, keep m_dead, enter spectate. **Root fix.** |
| D2 | `GameplayManager.PlayerHasDied()` | Prefix, return false | all | Safety net with a log line. |
| D3 | `PlayerShip.ExplodeCockpit()` (private) | Prefix, return false and hide renderers | all | Keep the ship mesh intact for respawn (§2.2 iii). |
| D4 | `PlayerShip.Update()` | Prefix and finalizer (`SpRulesScope`) for local dying/dead ship | all | Suppress the MP dead-input block 956-994. |
| D5 | `PlayerShip.Update()` | Postfix (local, dead, spectating) | all | Spectate camera follow and cycling (§3.2). May merge with D4. |
| D6 | `PlayerShip.CmdReadyToRespawn()` | Prefix, return false | host | Defensive (i). |
| D7 | `PlayerShip.ApplyDamage(DamageInfo)` | Prefix: `m_dead` → return false | host | No repeated death RPCs or stats on a corpse. |
| D8 | `NetworkSpawnPlayer.SetMultiplayerLoadout` (+ `SetMultiplayerCustomization`) | Prefix, return false | all | Keep the SP loadout on respawn (host lobby_id 0 case). |
| D9 | `Player.RestorePlayerShipDataAfterRespawn()` | Prefix (save ammo) and postfix (restore ammo, re-enable renderers, end spectate) | all | (iii). |
| D10 | `PlayerShip.StartDying(Vector3)` | Postfix | host | Record the death and trigger HARDCORE. |
| D11 | host tick (P14 postfix or a new `GameplayManager.Update` postfix) | Postfix | host | Cooldowns, all-dead check, `ServerRespawn`, team reset (§5.3, §4.1). |
| D12 | **C1 change** `NetworkManager.LoadScene` | Modify the existing prefix | joiner | In-level reload: `DoneLevel(Quit)` + `GameplayManager.SwitchToMenu(PLAY_GAME)` (§4.2). |
| D13 | **SendScene change** | Modify | host | `NetworkServer.DestroyPlayersForConnection(conn)` before `SetClientNotReady` if needed (UNVERIFIED), and send 172. |
| D14 | `LevelData.Awake` (extend H4b) | Prefix | all | Reset death and spectate state, reset latch. |
| D15 | `Client.OnRespawnMsg` | Postfix | all | Make sure renderers are visible and the spectate cam is torn down (if not done in D9). |

### New messages (≥172)
| Id | Name | Dir | Payload | Notes |
|---|---|---|---|---|
| 172 | DeathConfig | H→J | `u8 mode (0 SPECTATE,1 RESPAWN,2 HARDCORE), f32 cooldown` | Sent in `SendScene` before 48. Joiners use it for UI text only. |
| 173 | RespawnTimer | H→J | `u32 netId, f32 secondsLeft` (or -1 = waiting for a living player) | Sent on death and when the timer changes state. Drives the "RESPAWN IN n" HUD. |
| 174 | PlayerSlotNames | H→J | `u32 netId[], string name[]` | Optional, for "SPECTATING <name>" (m_mp_name is empty in co-op). |
| 175 | TeamReset | H→J | `u8 reason (all dead / hardcore), f32 delay` | HUD message "TEAM WIPED – RESTARTING". The actual reload is msg 48 from H3. |

Death itself needs no new message: `RpcApplyDamageWhenDying` already reaches every peer. Respawn uses stock msg 50.

### UNVERIFIED summary
- UNET stale `playerControllers` after a host scene reload (blocks joiner AddPlayer on restart and next level). **Test first.**
- C1 in-level reload behaviour with `SwitchToMenu(PLAY_GAME)` from GAMEPLAY on the joiner.
- Whether `NetworkServer.SendToAll(50)` reaches the host's local client (expected: yes, as for all server msgs).
- Ammo and energy authority for joiner ships. Whether ship renderers are only `MeshRenderer`. Contents of `c_main_ship_go`.
- Level exit while the host is spectating (§4.3 open issue).
- The joiner's `m_player_loadout_data` being empty.
