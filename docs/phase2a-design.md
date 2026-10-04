# Phase 2a design: host-authoritative robots

Goal: the host runs the only real robot AI. On the joiner, every robot is a puppet. It is drawn,
animated and culled locally, but its pose, mode, HP, spawn and death come from the host. Robot
shots are visible on the joiner, robot shots hit the joiner's ship on the host, and the joiner's
shots hurt robots on the host.

Out of scope for this phase (Phase 2b and later): doors, triggers, scripts, placed items, matcens'
activation state, reactor and escape, player death and respawn.

Paths are relative to `refs/Assembly-CSharp/` unless they start with `olmod/` (meaning
`/home/claude/olmod/GameMod/`) or `src/` (meaning our mod). Line numbers are for the decompiled build in `refs/`.
Anything not proven by code or logs is marked **UNVERIFIED**.

---

## 0. TL;DR

- **Robot identity:** use our own registry, built in a postfix on `RobotManager.InitializeForNewLevel`.
  - Every placed robot is enumerated with `Resources.FindObjectsOfTypeAll<Robot>()`, which also finds inactive ones.
  - Each robot is keyed by its **scene hierarchy path** (names plus sibling indices), and ids are assigned in sorted key order.
  - The host sends a hash of the key list. On a mismatch, it falls back to matching by type and position.
  - Runtime spawns all go through one choke point, `RobotManager.SpawnNewRobotNoParent` (RobotManager.cs:922). The host gives each one a dynamic id and broadcasts it.
  - Do not use `robot_id`. It is a `FindGameObjectsWithTag` index that is rewritten on every chunk change (RobotManager.cs:847-866).
- **Puppets:** on the joiner, prefix `Robot.Update` (Robot.cs:2106) and `Robot.FixedUpdate` (2966) to skip the AI.
  - A small cosmetic tick (glows, hide and spawn effects) replaces it.
  - The rigidbody is made kinematic, and the transform is set from interpolated host snapshots.
  - Dying robots are handed back to the native `FixedUpdate`, so the stock death animation and `ExplodeNow` play locally.
- **Damage:** on the joiner, **projectile hits already do no damage**, because `Projectile.ProcessCollision` and `OnTriggerEnter` only call
  `ApplyDamage` when `NetworkManager.IsServer()` (Projectile.cs:659-662, 863-866). Explosions, singularity and debug kills are *not* gated, so a `Robot.ApplyDamage` prefix is still required.
  - **The host already simulates the joiner's firing** (`PlayerShip.FixedUpdateProcessControls` → `ProcessFiringControls` when `IsMultiplayerActive`, PlayerShip.cs:3379-3392, driven by `Server.ProcessCachedControlsRemote` at 4007-4010). The joiner's shots therefore already damage the host's robots.
  - The host's copy of the joiner has a default loadout: impulse, 100 energy, 0 ammo, 0 missiles. A loadout sync (msg 170) is needed for parity.
- **Robot shots on the host hit the joiner's ship already.** Projectile → detached mesh collider (`PlayerMeshCollider.ApplyDamage`, PlayerMeshCollider.cs:8-13) → server-only `PlayerShip.ApplyDamage` → `RpcApplyDamage` to the joiner (PlayerShip.cs:1605-1706). The mesh collider position follows the ship each tick (PlayerShip.cs:3983-3986).
  - **But host robots only ever target the host ship** (one static target, Robot.cs:628-633; RobotManager.cs:298). So in 2a they will rarely shoot at the joiner unless we add the per-robot target swap (patch P5/P4 host branch).
- **Item drops already replicate.** On the host, `Item.Spew` calls `NetworkSpawnItem.Spawn` → `NetworkServer.Spawn` (Item.cs:731-734; NetworkSpawnItem.cs:30-41), and the joiner has the item spawn handlers registered at startup (GameManager.cs:593). The joiner only has to suppress its own local drops (`Item.Spew` prefix).
- **Patch count: 16 Harmony patch methods on 15 targets**, listed in §9. Plus 9 new message ids (162-170) registered from the existing handler-registration patches.
- **Biggest risks:** host robots chase only the host (target swap); chunk geometry and colliders on the host follow only the host (ActivateChunks union); the joiner's host-side loadout; joining mid-level; and NG+/difficulty mismatch changing the robot set.

---

## 1. Robot identity

### 1.1 How robots come to exist

| Origin | Code | Notes |
|---|---|---|
| **Placed in scene** | Scene prefab instances tagged `"Robot"` under `_container_placed_entities` (GameplayManager.cs:873). No runtime instantiate. | Same scene asset on both peers, so same hierarchy. |
| Level init | `GameplayManager.OnSceneLoaded` (GameplayManager.cs:1014) → … → `StartLevel()` (call at :1036, body :858) → `RobotManager.InitializeForNewLevel(false)` (:932, or `true` at :955 for saved games) → `UpdateRobotList()` + `InitMasterRobotList()` (RobotManager.cs:282-283) | `InitMasterRobotList` uses `GameObject.FindGameObjectsWithTag("Robot")` (RobotManager.cs:868-881). That **only finds active objects**. |
| `Robot.Start` (Robot.cs:1946) | Placed robots: sets AI mode, `target_go = GameManager.m_player_ship`. **Destroys itself if `m_init_ng_plus && !NewGamePlus`** (2047-2052). **Sets itself inactive if `m_init_hidden`** (2072-2075). | Runs after `InitializeForNewLevel`, because Unity runs `sceneLoaded` before the first `Start` (**UNVERIFIED** for this build, but consistent with Phase 0: at the first dump all 55 were present and active, including the 15 that later vanished). |
| Matcen | `RobotMatcen.UpdateStatic` (RobotMatcen.cs:187) → `MatcenFrame` (290; host-ship distance < 50 at 300/320) → private `SpawnRobot(Vector3,Quaternion)` (339) → `Robot.SpawnRobotFromMatcen` (Robot.cs:8116; random type 8119) → `RobotManager.SpawnNewRobotNoParent` (RobotManager.cs:922) | Random type choice, so only the host may decide. |
| Guidebot | `Robot.SpawnGuideBot()` (Robot.cs:8178), from `RobotManager.MaybeForceCreateGuideBot` (1332) or the cheat at PlayerShip.cs:4384 → `SpawnNewRobotNoParent(…, 25)` | Per local player. **Keep it local on each peer and never sync it** (etype 25, `m_is_guide_bot`). |
| Boss splits | `ExplodeNow` BOSS1 → `SpawnNewRobotNoParent(pos, 22)` (Robot.cs:11177); BOSS3 → `SpawnRobotType(BOSS3B, …)` (Robot.cs:4798 → RobotManager.cs:959 → 922) | Same choke point. |
| Scripted | `ScriptRevealRobot` → `Robot.RevealRobot` (Robot.cs:9146; hidden placed robots). `ScriptRobotAttack` SetActive (ScriptRobotAttack.cs:30-33). `ScriptActivateMatcen`. | These **do not create** robots. They reveal or activate placed ones, or switch matcens on. There are no `ScriptSpawn*` classes. |
| Save load | `SaveLoad.CreateNew<Robot>` → `SpawnRobotMinimal` (SaveLoad.cs:599-601) | Host-only concern. A joiner never loads saves. |
| Challenge mode | ChallengeManager.cs:2427 | Not relevant. |

**Conclusion: every runtime robot passes through `RobotManager.SpawnNewRobotNoParent(Vector3 pos, int etype, bool position_is_valid)` (RobotManager.cs:922-957).** It calls `Start()` and `ActivateRobot(force)`, and it adds the robot to `m_master_robot_list`.

### 1.2 Why `m_master_robot_list` empties over time (Phase 0 finding, explained)

It is **not** a culling list. It is the live list, minus two kinds of removal:

1. **Death:** `ExplodeNow` does `RobotManager.m_master_robot_list.Remove(this)` (Robot.cs:11193), then `Destroy(c_go, 3f)`.
2. **Destroyed objects become null entries.** `UpdateActiveStatusRobots` removes **one null entry per call** (RobotManager.cs:487-504). Placed robots flagged `m_init_ng_plus` destroy themselves in `Start` when not in NG+ (Robot.cs:2047-2052), so they are in the list at `InitMasterRobotList` time and drop out over the next frames.

Phase 0 evidence (`tests/logs/phase0-sp_outer_01*.log`):

- The start is 55 robots: GRUNTA 19, RECOILA 13, HULKA 10, GRUNTB 8, CLAWBOTB 5.
- The 41 `ExplodeNow` lines are exactly GRUNTA 19 + GRUNTB 8 + RECOILA 14.
- **All 10 HULKA and 5 CLAWBOTB left the list within the first minute without any `ExplodeNow`.** So they are the NG+-only robots (**UNVERIFIED** that the flag is `m_init_ng_plus` on exactly those; it is the only self-destroy path in `Start`).
- The 14th RECOILA is most likely a matcen or runtime spawn added at RobotManager.cs:945 (**UNVERIFIED**).
- So the "14 still alive" in phase0-results was a misreading. The list was complete.

Consequences:

- `m_master_robot_list` *is* usable as "alive robots", but it misses robots that were inactive at init (e.g. inside inactive parents, **UNVERIFIED** whether any exist).
- `robot_id` is useless as a network id. It is rewritten by `NewUpdateRobotList` (RobotManager.cs:859) on every chunk change, from an unordered `FindGameObjectsWithTag` array.

### 1.3 Stable id scheme

There is no stable id field. Candidates:

| Candidate | Verdict |
|---|---|
| `robot_id` | No (above). |
| `GetInstanceID()` | Per process. No. |
| Save-game matching | Persistent objects are matched by **name and exact position** (SaveLoad.cs:560-575). Robots are "transient" (re-created). That is precedent for position matching, but positions drift as soon as physics runs. |
| `NetworkIdentity.sceneId` | Robot is a `NetworkBehaviour` (SaveLoadBase.cs:6), so each robot GameObject must carry a `NetworkIdentity`, whose `sceneId` is baked per scene and identical on both peers. **UNVERIFIED:** whether it is non-zero for these prefab instances. Log it as a cross-check (§8). Do **not** depend on it in 2a. |
| **Hierarchy path** | **Use this.** `"/_container_placed_entities/<parent>#<sibling>/…/<name>#<sibling>"`. It is deterministic because both peers load the same scene asset. Robots are not reparented before death (Robot.cs reparent sites 10418-10508 and 11728-11813 are death and debris only). |

**Algorithm (both peers, `RobotManager.InitializeForNewLevel` postfix):**

```
all = Resources.FindObjectsOfTypeAll<Robot>()
        .Where(r => r.gameObject.scene.IsValid() && r.gameObject.scene.isLoaded)   // excludes prefabs (cf. SaveLoad.cs:340-342)
keys = all.Select(r => (HierPath(r.transform), (int)r.robot_type, Quantize(r.transform.position, 0.25m)))
sort by HierPath (ordinal)
id = index (ushort, 0..N-1); placed ids < 0x8000; dynamic ids 0x8000.. assigned by host
manifestHash = FNV1a(over HierPath + type for each, in order)
CoopRobots.ById[id] = robot; CoopRobots.Id[robot] = id
```

- The NG+-only robots get ids too. They simply die locally in `Start` on both peers.
- If a peer's NG+ flag differs, the hash still matches, but the alive sets differ. That is handled by the alive bitset in msg 162.
- On a hash mismatch the joiner requests the **manifest list** (msg 164: id, type, position). It binds each entry to the nearest unbound local robot of the same type within 1 m and logs every miss.
- Saved-game levels (`loading_saved_game=true`): the robots were re-created by `SaveLoad` (SaveLoad.cs:505, 599-601), so the joiner, which loads fresh, will not match by path. Fall back to manifest-by-position, and treat unmatched host robots as spawns (msg 166). **Phase 2a test: new games only.**

**Mid-level join.** The host sends msg 162 with an **alive bitset** over the placed ids, then msg 166 for every live dynamic robot. The joiner silently destroys placed robots the host says are dead: `m_master_robot_list.Remove` + `Destroy(go)`, with no FX and no stats.

---

## 2. Making joiner robots puppets

### 2.1 What drives a robot each frame

| Driver | Where | Contents |
|---|---|---|
| `Robot.Update()` (private) | Robot.cs:2106-2167 | Cosmetic: `m_delayed_action`, `MaybeAwakenRobotDueToHeadlightOrBoost` (2125; host-ship based), hide-frame countdown → `UnhideMesh` (2126-2133), `DrawSpawnMesh` (2134-2137), pull effect (2138-2142). **AI branch gated on `RobotManager.m_AI_enabled`** (2147): `AlertNearbyRobots`, **`MaybeFire`** (2155), `UpdateMaterialGlows` (2162), `WindTunnelMaybePathfind`, `MaybeForcePenaltyRunaway`. |
| `Robot.FixedUpdate()` (private) | Robot.cs:2966-3047 | Returns at once if `!m_AI_enabled`, `m_stasis` or paused (2968). Otherwise: caches `c_transform_position` and orientation vectors (2972-2976), timers, `UpdateTargetInfo` (2221), visibility, dodge, **`DoAIModes`** (5005; DYING → `DyingAction` 5083-5085 → `ExplodeNow` 10958), flocking, **`DoPathFollow`** (12437). |
| `Robot.LateUpdate()` | Robot.cs:12232 | Blade spin visuals only (BLADESA/BOSS2). Keep. |
| `OnCollisionEnter/Stay` | Robot.cs:8898 / 8825 | AddForce on its own rigidbody (no effect when kinematic), flags. Keep. |
| `RobotManager.Update()` | RobotManager.cs:317-350, from GameplayManager.Update (:1602) | Activation and culling, flocking, awaken-on-fire, `c_target_transform_position`, `MaybeActivateAllRobots`. |
| Global switch | `RobotManager.m_AI_enabled` (RobotManager.cs:101). Set true at :296, toggled by `GameplayManager.PauseGameplay` / `UnPauseGameplay` (GameplayManager.cs:2172/2179). | Read only at Robot.cs:2147 and 2968. |

**Why not just set `m_AI_enabled=false` on the joiner?** It would work: it kills `MaybeFire`, all movement and pathing, and `DyingAction`. But `UnPauseGameplay` re-enables it, and it also stops `DyingAction`, which we want for local death visuals. Use **per-robot prefixes** instead (P4 and P5).

### 2.2 The puppet switch (joiner)

`CoopRobots.IsPuppet(robot)` is true when we are the joiner, the robot has an id, and `!robot.m_is_guide_bot`.

- **P4 `Robot.Update` prefix (joiner):** if puppet, run the cosmetic tick and `return false`. The cosmetic tick is:
  - `m_hide_frames` countdown → `UnhideMesh()` (public, Robot.cs:10063)
  - `if (m_spawn_effect_on) DrawSpawnMesh()` (private; `AccessTools.MethodDelegate`)
  - `UpdateTargetInfo()` (public, 2221; computes `m_target_dist` against the joiner's own ship, which `UpdateMaterialGlows` uses for LOD at 11527)
  - `UpdateMaterialGlows()` (public, 11525)
- **P5 `Robot.FixedUpdate` prefix (joiner):** if puppet and not `CoopRobots.LocallyDying(robot)`, `return false`. This is the switch that stops AI, firing, pathfinding, dodging and self-initiated death. A robot whose `m_hp <= 0` locally can no longer reach DYING by itself (Update 2149-2152 is skipped too).
- **P6 `Robot.ApplyDamage(DamageInfo)` prefix (joiner):** `__result = false; return false;`. Optionally add the hit flash `m_dmg_flash += 0.03f + d*0.05f` and an impact sound (cosmetic copy of 11991-12001 and 12049-12058). Needed because these sources are **not** server-gated:
  - Explosion.cs:258/292
  - ExplosionDelayed.cs:119/183/217
  - SingularityPull.cs:109
  - RobotManager.DebugMaybeKillRobot (RobotManager.cs:383)
- **`PrepareToFight`, `set_AI_mode` etc. are not called on puppets.** Replicated mode values are written **directly to the fields** `AI_mode` / `AI_submode` (Robot.cs:335/337). `set_AI_mode` (1802) has side effects: timers, `Random`, path reset, the waking animation.

### 2.3 Rigidbody and interpolation

- On the joiner, at registration (the postfix in §1.3) and in the remote-spawn handler: `c_rigidbody.isKinematic = true; c_rigidbody.interpolation = None`.
  - Robot.cs never touches `isKinematic` (grep: no hits), so nothing fights this.
  - `Start` only scales the inertia tensor (2021-2028), which is harmless when kinematic.
- Each frame, the **P14 postfix on `RobotManager.Update`** iterates `CoopRobots.ById`, **including inactive GameObjects**. For each robot it samples the snapshot buffer at `renderTime = hostTimeEstimate - 0.1 s`:
  - Hermite/lerp position with velocity, slerp rotation.
  - Extrapolate at most 0.2 s, then hold.
  - Sets `c_transform.position/rotation` and the cached `c_transform_position`, `c_transform_rotation` and `c_transform_forward/right/up` (normally refreshed only by FixedUpdate, 2972-2976, and read by visuals, LOD and homing).
- Setting the transform of an inactive robot is what makes joiner-side culling follow the host (§3.1).
- **Death hand-off:** on msg 167 set `isKinematic = false` and `c_rigidbody.velocity = vel`, so the native `DyingAction` spin and drift (10889-10900) look right.

### 2.4 State that the visuals depend on (minimal replication set)

| Field | Why | Size |
|---|---|---|
| `AI_mode`, `AI_submode` | `RobotAsleepOrLurking` drives joiner culling (RobotManager.cs:573-605). Headlight shows only when not ASLEEP (11570). The sleeping/waking animation (5017-5025). | 2 B |
| sleep→awake edge | Call private `StartAnimation(RobotAnimState.waking)` (1775) on the joiner when mode leaves ASLEEP, mirroring 1846-1849 | derived |
| `m_hp` (and `m_hp_starting` known locally) | The low-HP flicker in `UpdateMaterialGlows` (11554-11561) | 2 B |
| `m_dmg_flash` (quantised) | Hit flash. The joiner can't compute it, because its ApplyDamage is blocked. | 1 B |
| `m_cloaked_robot` | CLAWBOTB and boss cloak (1999, 4849-4864) | flag |
| `m_headlight_on` | 11570 | flag |
| `m_init_hidden` cleared / revealed | Hidden robots are never activated by `ActivateRobot` (RobotManager.cs:636). The joiner must call `RevealRobot()` (Robot.cs:9146) when the host says it is revealed. | flag |
| `m_stasis` | Stasis is set locally, but `StasisUpdate` pins position (12163-12166) | flag |
| `m_spawn_effect_on` and amount | Spawn shimmer. Set by the spawn handler, not streamed. | spawn msg |

Not replicated in 2a (cosmetic gaps, accept them):

- Charge, claw and detonator animations driven from inside the mode functions
- Teleport fades (`m_teleporting`, `MaybeTeleport`)
- Blade spin speed: `m_blade_spin_speed` is set by `MaybeSpinBlades` in FixedUpdate. Could be added as 1 B later.
- Eye-glow goal

---

## 3. Culling and activation

### 3.1 Joiner: keep local culling, but drive it with host poses

- **Keep** `RobotManager.Update`'s activation on the joiner (RobotManager.cs:327-336). It is relative to the joiner's own ship (`GameManager.m_player_ship` is the networked joiner ship after Phase 1 C4).
- `ActivateRobot` (634-655) → `RobotInRelevantSegment` (782-803) uses `m_current_segment` only when `m_believed_valid_current_segment`. That flag is set only for asleep or lurking robots (797-800) and cleared by `set_AI_mode` and `Start`. Otherwise it recomputes from **`c_transform.localPosition`** (790).
  - Because P14 moves the transform of inactive puppets, a robot the host moves into view becomes active on the joiner.
  - **P14 must also clear `m_believed_valid_current_segment` whenever the replicated position moves more than 0.5 m.** Otherwise a robot frozen while it was asleep keeps its stale segment.
- **Early-return trap:** `ActivateRobot` returns immediately for any active, non-asleep robot (636), so robots are never deactivated again. That is stock behaviour and fine.
- **Host says "inactive":** the robot keeps its last pose. No special handling.
- `MaybeActivateAllRobots` (1289-1330) can SetActive and write AI vars on the joiner (sp_outer_01 only). The AI vars are ignored by puppets. Harmless.

### 3.2 Host: simulate robots near *any* player

| Patch | Target | Change |
|---|---|---|
| **P11** | `RobotManager.RobotInRelevantSegment(Robot)` private static (RobotManager.cs:782) | Postfix, host: `if (!__result)` take `seg = robot.m_current_segment` (just set inside) and return true if `m_segment_visibility[s, seg] > 0` for any remote ship segment `s`. The segment comes from `ship.c_moving_object.CurrentSegmentIndex` (MovingObject.cs:16, updated in its own LateUpdate:26); remote ships are in `NetworkManager.m_Players`. Guard `s >= 0`. |
| **P12** | `RobotManager.UpdateChunkActivationDueToPlayerMovement()` (RobotManager.cs:308) | Postfix, host: also true if any remote ship's segment changed since the last call (per-netId cache). This makes `UpdateActiveStatusAll` re-run. Robots are re-evaluated every frame anyway through `UpdateActiveStatusRobotsOnly(force:true)` (475-480), so P12 mainly matters for chunks (P13) and the 2b item/door/prop lists. |
| **P13** | `ChunkManager.ActivateChunks()` (ChunkManager.cs:176-221) | **olmod already replaces it** with a prefix that returns false (`olmod/MPFixGhostProjectiles.cs:9-…`). Add **P13a**: our prefix with `[HarmonyPriority(Priority.First)]` that, on the coop host, computes the chunk set as the **union over the host segment and all remote segments**, keeps olmod's `Renderer.enabled=true` fix for the no-visibility path, keeps the active-robot-segment chunk pass (214-220), and returns false. Add **P13b**: a prefix on olmod's `MPFixGhostProjectiles_ChunkManager_ActivateChunks.Prefix` that, on the coop host, sets `__result = false` and skips it (same pattern as our F1). Without P13b the two prefixes would fight and toggle chunks every frame. **UNVERIFIED:** whether level chunks carry the colliders the server needs for the joiner's ship (Phase 1 risk 1). P13 fixes it either way. |
| (2b) | `RobotMatcen.MatcenFrame` distance gate (RobotMatcen.cs:300, 320) | Use the min distance over players. Not in 2a. |
| (2b) | `RobotManager.MaybeAwakenRobots`, `Robot.MaybeExitAsleep`, `ModifiedAwakenDist` | Host-ship based. Partly covered by the target swap below. |

### 3.3 Host: robots must be able to target the joiner (strongly recommended for 2a)

Without this, host robots only see, chase and fire at `Robot.c_target_transform` = the host ship (static at Robot.cs:628, assigned at RobotManager.cs:298). They also refresh `c_target_transform_position` every frame (RobotManager.cs:338).

The minimal approach is a per-robot **target swap** around that robot's own tick. Implement it as the host branch of the same P4 and P5 patch classes:

- **Prefix:** `CoopTargets.Push(robot)` saves the statics and sets:
  - `Robot.c_target_transform = T.transform` and `Robot.c_target_transform_position = T.c_transform_position`
  - the private static `Robot.m_player_ship = T` (Robot.cs:633)
  - `robot.target_go`, `robot.target_rigidbody` (set in Start, 2032-2037)
- **Finalizer:** restore them.
- **Choosing the target:** re-choose `T` every 0.5 s per robot. Take the nearest live, non-dying ship with line of sight (layer mask 67256321 as at Robot.cs:2097). Keep the current target while it is still visible, which gives hysteresis.

Known leftovers (**UNVERIFIED** impact):

- Melee damage calls `GameManager.m_player_ship.ApplyDamage` directly (Robot.cs:5134, 5158, 9004), so a claw or charger hitting the joiner damages the **host**. Fix in 2b by routing to the target ship.
- Cloak checks read `GameManager.m_local_player.m_cloaked` (Robot.cs:2236, 2093).
- `RobotManager.RobotAsleepOrLurking` sniper distance uses the host ship (RobotManager.cs:590-593).

---

## 4. Death, explosion, drops

### 4.1 Host death path

`Robot.ApplyDamage(DamageInfo)` (Robot.cs:11953) runs as follows:

- When `m_hp <= 0`, it sets `m_instant_explode` (12016) and `AI_mode = DYING` (12017).
- On the first kill (the `!m_dying` branch, 12023):
  - it gives XP to `GameManager.m_local_player` (12036-12043; the host gets XP for every kill, including the joiner's)
  - it calls private **`StartExploding(DamageInfo)`** (10514)
- `StartExploding` then:
  - picks a **random** `m_explosion_type` (10535-10563)
  - runs `triggered_on_death` script links (10531-10534)
  - calls `AddStatsRobotKilled` (10601) and sets `m_dying = true` (10623)
  - fires the detonator burst (10624-10641)
  - may call `ExplodeNow()` immediately for instant types (10662, 10668)
- Otherwise `FixedUpdate` → `DoAIModes` → `DyingAction` (10846) counts down and calls `ExplodeNow()` (10958).
- `ExplodeNow` (10962-11196) plays FX, spawns boss splits, drops items (11108-11160 → `Item.Spew` / `MaybeDropItem` 11330), then sets `alive = false`, removes the robot from the master list, and destroys it after 3 s (11192-11195).

Hooks (host):

- **P7 `Robot.StartExploding(DamageInfo)` postfix** (private): if no death has been sent for this id yet, send **msg 167 RobotDeath**. It carries:
  - id and the **chosen `m_explosion_type`** (private field)
  - `m_instant_explode`
  - killer = `di.owner?.GetComponent<Player>()?.netId` (0 = host or unknown)
  - `di.weapon`, pos, vel
- **P8 `Robot.ExplodeNow()` postfix:** send **msg 168 RobotExplode** (id, pos).
  - For instant explosions, `ExplodeNow` runs *inside* `StartExploding`, before P7's postfix. So P8 checks "death not yet sent", sends 167 first with flag `EXPLODED`, and P7 then skips. Ordering is preserved on the reliable channel.
- Guidebot: excluded (no id). `ApplyDamage` returns at once for it anyway (11956-11959).

### 4.2 Joiner reaction, without double counting

On **167**:

- Unknown id: ignore.
- If the GameObject is **inactive** (not visible to the joiner): `m_master_robot_list.Remove(r); Destroy(r.gameObject)`, with no FX and no stats.
- Otherwise:
  1. `r.m_hp = -1; r.AI_mode = DYING; m_instant_explode = flag`.
  2. Unkinematic, and set the velocity.
  3. Invoke private `StartExploding(di)` with `di.weapon` and `di.owner` = the killer's ship GO when known.
  4. After the call, overwrite `m_explosion_type` with the host value. Leave `m_last_explosion_type` as is.
  5. Mark it `LocallyDying`, so P5 lets the native `FixedUpdate` → `DyingAction` → `ExplodeNow` run.

This gives the full local death show:

- **Stats:** `AddStatsRobotKilled` and the kill feed run once on the joiner. The joiner's `ApplyDamage` never runs (P6), so the joiner counts each kill exactly once and the host counts it once. Each peer has its own debrief, so there is **no double counting**.
- **XP:** the joiner adds XP itself only if `killerNetId == own netId`, using the same formula as 12036-12043 (+1, +2 super/alien, +20 boss).
- `triggered_on_death` script links fire locally on the joiner too. That keeps the joiner's door and lockdown state roughly consistent until 2b. **Risk:** a script that spawns or activates something. Matcen spawns are blocked on the joiner (P3), and other effects are local-only.
- The detonator burst projectiles from `StartExploding` are cosmetic on the joiner (see §6). The host must **not** broadcast fires from dying robots (P10 checks `robot.m_dying`).

On **168**: if the robot is still `alive`, call `ExplodeNow()` now. This snaps the timing to the host.

**P8 prefix (joiner):** `if (!__instance.alive) return false;`, so `ExplodeNow` is idempotent. It also sets `CoopRobots.InJoinerExplode = true` (cleared in the postfix), so P9 can drop local spews.

### 4.3 Items

- **Host drops are already networked.** `Item.Spew` (Item.cs:718) → `if (NetworkManager.IsServer()) NetworkSpawnItem.Spawn(go)` (731-734) → `NetworkServer.Spawn(item, assetId)` (NetworkSpawnItem.cs:30-41). Super items get `CallRpcMakeSuper` (742-745).
  - The joiner has spawn handlers for all 28 item prefabs, registered once at startup (`NetworkSpawnItem.RegisterSpawnHandlers`, GameManager.cs:593, through `ClientScene` statics). It instantiates them through `NetworkSpawnItemHandler` (NetworkSpawnItem.cs:70-84).
  - **UNVERIFIED:** that the SP item prefabs carry a `NetworkIdentity`. There is a warning at NetworkSpawnItem.cs:34 if not. MP uses the same prefabs, so this is likely.
- **Joiner local drops must be suppressed.** **P9 `Item.Spew(GameObject,Vector3,Vector3,int,bool)` prefix (joiner):** `return false` while `CoopRobots.InJoinerExplode`. Also block `RobotMatcen.SpewPowerups` in 2b. Do **not** block `Spew` globally, because 2b may need local spews.
- **Pickups already follow the MP path once `IsMultiplayerActive` is true:**
  - The joiner fakes the pickup locally (Item.cs:446-453, `TryFakePickup` 1172).
  - The host applies it to its copy of the joiner's `Player` (Item.cs:454-590; olmod `MPSpew` prefix for missiles, `olmod/MPSpew.cs:15-60`) and destroys the networked item, which unspawns on the joiner.
  - The ammo amounts are the MP ones (Item.cs:467, 497-521).
  - **UNVERIFIED:** whether armor, energy and ammo gains on the host copy reach the joiner's HUD in a MISSION game. MP uses the Player RPCs for this, so it is probably fine. That belongs to the 2a loadout work (msg 170) and the 2b test.
- **Placed (scene) items are not networked.** Each peer has its own copy, so they can be picked up twice. That is out of scope (2b).

---

## 5. Damage authority (traced)

1. **The joiner cannot hurt robots locally through projectiles.**
   - Projectile collision: `ProcessCollision` calls `collider.SendMessage("ApplyDamage")` only if `NetworkManager.IsServer()` (Projectile.cs:659-662).
   - Pass-through hits: the same gate (863-866).
   - `IsServer()` is `Server.IsActive()` (NetworkManager.cs:170-173). On the joiner it is false, because `Client.Connect` shut its server down (phase1 §2.1).
   - **Not gated:** Explosion.cs:258/292, ExplosionDelayed.cs:119/183/217, SingularityPull.cs:109, Projectile.cs:890 (Destroyable). Hence **P6** on the joiner.
2. **The host simulates the joiner's firing.**
   - `PlayerShip.FixedUpdatePre` → `Server.ProcessCachedControlsRemote(c_player)` for non-local ships on the server (PlayerShip.cs:4007-4010; Server.cs:583-605) → `FixedUpdateProcessControls` (PlayerShip.cs:3379).
   - **`if (GameplayManager.IsMultiplayerActive) ProcessFiringControls();`** (3384-3387). That is true on the host (S1).
   - → `MaybeFireWeapon` (6075) → `ProjectileManager.PlayerFire(player, …)` (ProjectileManager.cs:208).
     - `IgnoreFiringProjectileTypeForRemotePlayer` is false on the server (Player.cs:4708-4714 needs `!Server.IsActive()`).
     - The team is `PLAYER`, because `IsMultiplayerSceneLoaded()` is false (216).
   - → `FireProjectile` with `owner = joiner's Player GO`.
   - **So the joiner's shots exist on the host and damage host robots through the normal server-gated path.** Boss filter: `di.owner.tag` must be `"PlayerShip"` (Robot.cs:11966). The owner is the player GameObject, the same as for the host, so this is fine (**UNVERIFIED** tag on the remote instance, but it is the same prefab).
   - **msg 70 FireProjectileToClient** is only sent when `owner != null && GameplayManager.IsMultiplayer` and only for launch-synced types (ProjectileManager.cs:256-269; Server.cs:461-491). It is never sent in MISSION. It is irrelevant for robots, and that is why the host's shots are not drawn on the joiner (Phase 1 note). Drawing the host's and joiner's player shots on the other peer is a separate item (2b: force-send 70 under coop).
3. **What is still needed for parity:** the host's copy of the joiner has a default loadout.
   - `m_weapon_type` = enum 0, `m_energy` = 100, `m_ammo` = 0, `m_missile_ammo` all 0, `m_weapon_level` all 0 (Player.cs:122-335).
   - `InitializeForNewGame` runs only for the local player (Player.cs:4116).
   - Weapon and missile **selection** already reaches the host through `CmdSetCurrentWeapon` / `CmdSetCurrentMissile` (Player.cs:2983-3012; called from PlayerShip.cs:5565/5576/5613/5752 and NetworkSpawnPlayer.cs:210-217).
   - Ammo, missiles, energy and upgrade levels do not reach the host. As a result:
     - ammo weapons auto-switch to energy on the host (PlayerShip.cs:6082-6100)
     - missiles never fire on the host
     - upgrade levels are LEVEL_0
   - **Fix (2a):** joiner → host **msg 170 PlayerLoadout**. Plus **P15 `Server.ProcessCachedControlsRemote(Player)` prefix (host):** apply the last loadout to that Player and top up energy and ammo to the joiner-reported values ("trust the joiner"), so the host never refuses a shot the joiner predicted.
4. **Robot shots on the host damaging the joiner** are covered in §6.

---

## 6. Robot projectiles

- **Choke point:** `ProjectileManager.FireProjectileRobot(Robot robot, ProjPrefab type, Vector3 pos, Quaternion rot, GameObject owner = null, float strength = 0f, ProjTeam proj_team = PLAYER, WeaponUnlock upgrade_lvl = LEVEL_0, bool save_pos = false)` (ProjectileManager.cs:223-228).
  - All robot fire sites use it: Robot.cs:2454-2700, and the detonator death burst at 10639.
  - The only other ENEMY fire is `PropReactorTurret` → `FireProjectile` (PropReactorTurret.cs:120), which is a 2b prop.
- **Host P10 postfix:** `if (CoopRobots.Id.TryGetValue(robot, out id) && !robot.m_dying && AnyJoinerRelevant(robot))` → queue `RobotFire{id, type, upgrade, save_pos, pos, rot}`. Flush once per frame as **msg 169** on channel 2 (unreliable). Lost shots are cosmetic.
- **Joiner handler:** `ProjectileManager.FireProjectileRobot(robot, type, pos, rot, robot.c_go, 0f, ProjTeam.ENEMY, upgrade, save_pos)`.
  - It adds the muzzle flash (`robot.AddMuzzleFlash`) and plays the sound.
  - **These projectiles are harmless on the joiner:** the player-ship damage is server-only (PlayerShip.cs:1607), and robot or Destroyable hits are server-gated or blocked by P6.
  - Homing shots (`Projectile.FindPlayerTarget` 414, `GetBestPlayerTarget` 1143) home on the joiner's local view. Visual divergence only.
  - The `m_player_weapons` and `MaybeAwakenRobots` side effects in `FireProjectile` (236-251) apply only to `PLAYER` team shots, not `ENEMY`.
- **Host: do robot projectiles hit the joiner's ship? Yes, by the existing path.**
  - The joiner's ship mesh collider is detached under co-op (our F3; stock is PlayerShip.cs:843-844 for MP) and is moved to the ship position every tick while `IsMultiplayerActive` (PlayerShip.cs:3983-3986). Only position is synced, not rotation, which is the same as stock MP.
  - `Projectile.ProcessCollision` → `SendMessage("ApplyDamage")` → `PlayerMeshCollider.ApplyDamage` (PlayerMeshCollider.cs:8-13) → `PlayerShip.ApplyDamage` (server).
    - It uses the SP difficulty multiplier, since `!IsMultiplayer` (PlayerShip.cs:1635-1638).
    - Then it calls `CallRpcApplyDamage(...)` to the owner (1672) and `RpcApplyDamageEffects` (1686).
  - Phase 1 run 4 already logged `DMG … netId=75` on the host from ship contact, which proves the collider exists on the host.
  - **UNVERIFIED:** an actual robot-projectile hit on the joiner. That is test step 6.
  - **The constraint is §3.3:** robots must target the joiner. Without it, only stray shots will hit.
- **Robot devastator detonation** (`ProjectileManager.MaybeExplodeRobotDevastator`, 310) is keyed on `GameManager.m_player_ship`, i.e. the host. Accept this for 2a.

---

## 7. Messages

olmod uses 101-155 (`MsgPlayerPhysics = 155` is the highest, olmod/*.cs). We use 160-161. **New ids: 162-170.**

Channels (`NetworkManager.cs:81-89`): **0 = ReliableSequenced** (the default `Send`), **1 = UnreliableSequenced**, **2 = Unreliable**, **3 = StateUpdate**. `PacketSize = 1100`. There is no fragmenting channel, so **every message must stay at or under about 1000 B** (allowing for UNET headers, **UNVERIFIED** exact overhead).

| id | Name | Dir | Ch | Format (little-endian, `NetworkWriter`) |
|---|---|---|---|---|
| 162 | RobotManifest | H→J | 0 | `u32 levelHash, u32 manifestHash, u16 placedCount, u8 ngPlus, u8 difficulty, u16 dynCount, bytes aliveBits[ceil(placedCount/8)]`. Sent after H3 SendScene and again once the joiner's 163 arrives. For more than about 7900 placed robots, split (never happens). |
| 163 | ManifestAck | J→H | 0 | `u32 manifestHash, u16 count, u8 ok`. If `ok=0`, the host sends 164. |
| 164 | ManifestList | H→J | 0 | `u16 first, u8 n, n × {u16 id, u8 type, f32×3 pos}` (15 B, n ≤ 60) |
| 165 | RobotStateBatch | H→J | **1** | `f32 hostTime, u8 n, n × Entry`. Entry (30 B): `u16 id, u8 flags, u8 aiMode, u8 aiSub, f32×3 pos, u32 rotSmallest3, i16×3 vel (cm/s, ±327 m/s), u16 hp (ceil), u8 dmgFlash×100`. Flags: 1 activeOnHost, 2 cloaked, 4 headlight, 8 stasis, 16 revealed, 32 super, 64 variant, 128 keyframe. n ≤ 32 → 8 + 32×30 = 968 B, so split into several 165s. |
| 166 | RobotSpawn | H→J | 0 | `u16 id, u8 etype, u8 flags (super, variant, fromMatcen, minion), f32×3 pos, f32×4 rot, i16×3 vel` = 40 B. The joiner calls `SpawnNewRobotNoParent(pos, etype)` under `CoopRobots.ApplyingRemoteSpawn` (so P2 does not orphan it), then: set rot and kinematic; `MakeRobotSuper()` / `MakeRobotVariant()` / `MaybeReplaceShader()` per flags (as Robot.cs:8133-8144); `m_spawn_effect_on = true; m_spawn_effect_amt = 1; HideMesh(2)`; `ExplosionManager.CreateExpElementFromResources(FXExpElement.spawn_flash1, pos, rot)` if fromMatcen. |
| 167 | RobotDeath | H→J | 0 | `u16 id, u8 explosionType, u8 flags (instant, exploded), u32 killerNetId, u8 weapon, f32×3 pos, i16×3 vel` = 26 B |
| 168 | RobotExplode | H→J | 0 | `u16 id, f32×3 pos` |
| 169 | RobotFireBatch | H→J | **2** | `u8 n, n × {u16 id, u8 proj, u8 upgrade, u8 flags(save_pos), f32×3 pos, u32 rotSmallest3}` (21 B, n ≤ 45) |
| 170 | PlayerLoadout | J→H | 0 | `u8 weaponType, u8 missileType, u8[8] weaponLevel, u8[8] missileLevel, u16 ammo, u16[8] missileAmmo, u16 energy×10, u8 upgradeLevel0`. Sent on spawn, on change, and at 2 Hz at most. |

**Ordering:** all events (162, 164, 166, 167, 168) go on channel 0, so a spawn always arrives before the death of that id. State (165) on channel 1 may arrive for an id that is not yet spawned: buffer up to 1 s keyed by id, then drop it.

**Host send policy (P14, 20 Hz, `Time.realtimeSinceStartup` accumulator):** for each joiner connection (`CoopHost.Verified`), the candidates are the robots with an id, `alive`, and `c_go.activeSelf` on the host. A candidate is relevant if it is visible from the joiner's segment or within 80 m of the joiner's ship. Among the relevant ones:

- send if pos moved > 2 cm, rot changed > 1°, the mode or flags changed, or the hp changed
- send every robot as a keyframe each 1 s
- when a robot drops out of the set (deactivated on the host), send one entry with `activeOnHost = 0`

Send with `conn.SendByChannel(165, msg, 1)` per joiner connection, not a broadcast, because relevance is per joiner.

**Bandwidth:** Phase 0 shows at most 10 active and typically 0-5. 10 × 30 B × 20 Hz ≈ 6 KB/s, plus fire and events under 2 KB/s per joiner. With 2 joiners, about 16 KB/s upstream from the host.

**Clock:** the joiner estimates `offset = min over a 2 s window of (localRecvTime − hostTime)` and renders at `hostNow − 0.1 s`.

---

## 8. Test procedure (two instances on one PC)

Same launch as Phase 1 (`tests/phase1-test.md`), level `sp_outer_01`, **new game, same difficulty on both, no NG+**.

Log lines (tag `RSYNC`; also mirrored to the Unity log by `CoopLog`):

1. **Registry, both peers:**
   - `RSYNC registry n=55 placed hash=XXXXXXXX inactiveAtInit=K sceneIdNonZero=M`
   - Log the first 3 keys and each robot's `NetworkIdentity.sceneId` once, to answer the sceneId question.
   - Expect the same `n` and `hash` on both.
2. **Manifest:**
   - Host: `RSYNC manifest -> conn 1 placed=55 alive=55 dyn=0`
   - Joiner: `RSYNC manifest hash match=True alive=55 killedOnJoin=0`
3. **Puppets (joiner):** `RSYNC puppets=55 kinematic=55`.
   - A counter of native `Robot.FixedUpdate` bodies run on the joiner, logged every 5 s, must be `0` except while robots are dying.
   - A counter of `FireProjectileRobot` calls *not* triggered by 169 must be `0`, except detonator deaths.
4. **State flow:**
   - Every 5 s, host: `RSYNC tx robots=R msgs=M bytes/s=B`
   - Joiner: `RSYNC rx entries=E unknownId=0 late=L maxExtrapMs=X`
   - Fly the host to wake robots. On the joiner, robots move smoothly, with no snapping above 1 m.
   - Add a debug dump: for 3 robots, both peers log `id pos` every 5 s, and the difference must be under 1 m (allowing for the 100 ms delay).
5. **Kill by host:**
   - Host: `ROBOT ExplodeNow …` (existing P0 log) and `RSYNC death id=… type=GRUNTA killer=0`
   - Joiner: `RSYNC apply death id=… visible=True`, then **exactly one** `ROBOT ExplodeNow` for that robot.
   - No `Item.Spew` on the joiner: `RSYNC suppressed local spew` lines. Drops appear on the joiner as UNET item spawns: log `Item.Awake` / `NetworkSpawnItemHandler` count.
6. **Kill by joiner:** the joiner shoots a robot (impulse first).
   - Host: `RSYNC robotdmg id=… from netId=75 dmg=…`. Add a temporary host-side `Robot.ApplyDamage` log, or reuse P0.
   - Host: `death … killer=75`. Joiner: `+XP` line.
   - Then test an ammo weapon and a missile after msg 170 is in place: `RSYNC loadout from conn 1 …`.
7. **Robot shoots joiner:** with the target swap on, park the joiner alone near a robot.
   - Host: `DMG player netId=75 dmg=… from=robot:GRUNTA` (existing P0 `PlayerShip.ApplyDamage` log)
   - Joiner: its HUD armor drops, and `RSYNC fire rx` counters increase.
8. **Culling union:** the host stays at the start. The joiner flies 2-3 rooms away.
   - Host: `RSYNC relevance remoteSeg=… activeRobots=…` must include robots in the joiner's area.
   - Host: the chunk count active from P13 is greater than with the host-only set.
   - The joiner's ship must not pass through walls (Phase 1 risk 1).
9. **Matcen:** in a level with a matcen.
   - Host: `RSYNC spawn id=0x8000 etype=… fromMatcen`
   - Joiner: `RSYNC apply spawn …` and **no** `orphan destroyed` lines from P3.
10. **Join mid-level:** the host kills 5 robots, then the joiner joins. Joiner: `killedOnJoin=5`.
11. **Negative:** `-coophost` and `-coopjoin` off → the registry, puppets and messages stay silent (all patches gated on `CoopConfig.Active`).
12. **Exceptions:** 0 repeating exceptions on both peers, as in Phase 1 run 5.

---

## 9. Harmony patch list

All patches are gated on `CoopConfig.Active && !GameplayManager.IsMultiplayer`, plus the role noted. H = host (`Server.IsActive()`), J = joiner.

| # | Target (file:line) | Kind | Role | Purpose |
|---|---|---|---|---|
| P1 | `RobotManager.InitializeForNewLevel(bool)` (RobotManager.cs:271) | Postfix | both | Build the id registry (§1.3). J: make puppets kinematic. H: queue msg 162 to verified joiners. |
| P2 | `RobotManager.SpawnNewRobotNoParent(Vector3,int,bool)` (RobotManager.cs:922) | Postfix | both | H: if `etype != 25`, assign a dynamic id and queue 166 (flushed at end of frame, so the caller's super, variant and rot are captured). J: if `!ApplyingRemoteSpawn && etype != 25`, `Destroy(__result)` (orphan; covers boss splits 11177/4798). |
| P3 | `RobotMatcen.SpawnRobot(Vector3,Quaternion)` private (RobotMatcen.cs:339) | Prefix | J | `return false`. No local matcen spawn, flash or sound. |
| P4 | `Robot.Update()` private (Robot.cs:2106) | Prefix + Finalizer | J / H | J: cosmetic tick, skip the original. H: target swap push and pop (§3.3). |
| P5 | `Robot.FixedUpdate()` private (Robot.cs:2966) | Prefix + Finalizer | J / H | J: skip unless locally dying. H: target swap push and pop. |
| P6 | `Robot.ApplyDamage(DamageInfo)` (Robot.cs:11953) | Prefix | J | No HP, death or XP on the joiner. Optional hit flash. |
| P7 | `Robot.StartExploding(DamageInfo)` private (Robot.cs:10514) | Postfix | H | Send 167 (unless P8 already sent it). |
| P8 | `Robot.ExplodeNow()` (Robot.cs:10962) | Prefix + Postfix | both | J: idempotence guard and `InJoinerExplode` flag. H: send 168 (and 167 if instant). Coexists with olmod's Boss2B patch (olmod/Boss2B.cs:12) and our P0 log prefix. |
| P9 | `Item.Spew(GameObject,Vector3,Vector3,int,bool)` (Item.cs:718) | Prefix | J | Drop local spews while `InJoinerExplode`. |
| P10 | `ProjectileManager.FireProjectileRobot(...)` (ProjectileManager.cs:223) | Postfix | H | Queue 169 for live, relevant robots. |
| P11 | `RobotManager.RobotInRelevantSegment(Robot)` private (RobotManager.cs:782) | Postfix | H | Union over remote ship segments. |
| P12 | `RobotManager.UpdateChunkActivationDueToPlayerMovement()` (RobotManager.cs:308) | Postfix | H | Remote segment change triggers a refresh. |
| P13a | `ChunkManager.ActivateChunks()` (ChunkManager.cs:176) | Prefix, `Priority.First`, returns false | H | Chunk union over all ships. |
| P13b | `GameMod.MPFixGhostProjectiles_ChunkManager_ActivateChunks:Prefix` (olmod/MPFixGhostProjectiles.cs:12) | Prefix (`TargetMethod`, like F1) | H | Skip olmod's replacement on the coop host. |
| P14 | `RobotManager.Update()` (RobotManager.cs:317) | Postfix | both | H: 20 Hz state send, flush the spawn, death and fire queues. J: apply interpolation to all puppets (including inactive ones), clear `m_believed_valid_current_segment` on movement, handle reveal and wake edges. Note that olmod/MPObserver.cs:172 prefixes this too (spectator only). |
| P15 | `Server.ProcessCachedControlsRemote(Player)` (Server.cs:583) | Prefix | H | Apply the joiner's loadout (msg 170) and top up energy and ammo. |

**Count: 16 patch methods on 15 targets.** P4 and P5 each carry a prefix and a finalizer. The minimum for "puppets plus host-authoritative death" without targeting, culling union or loadout is P1, P2, P4, P5, P6, P7, P8, P9, P10, P14: **10**.

**Message registration** extends the existing patches with no new targets:

- `H2_ServerRegisterHandlers` (src/olcoop/Phase1Session.cs): 163, 170
- `C2b_ClientRegisterHandlers`: 162, 164-169

**Joiner extra, in 2a or 2b (not counted):** `Robot.SpawnGuideBot` is left local on both peers by design.

New code (not patches): `CoopRobots` (registry, id maps, snapshot buffers, flags), `CoopTargets` (target choice and swap), and `CoopRobotNet` (message classes and serialisers). Run `tools/VerifyPatches.cs` against all targets. P3, P4, P5, P7 and P11 are private, and P13b is olmod-internal.

---

## 10. Risks (ranked)

1. **Targeting.** Without the §3.3 swap, host robots ignore the joiner: no LOS wake, no chase, no fire. With the swap, the static target is reassigned around every robot tick, and some code still reads `GameManager.m_player_ship` / `m_local_player` directly:
   - melee damage (Robot.cs:5134/5158/9004)
   - cloak (2236)
   - sniper lurk (RobotManager.cs:590)
   - matcen distance (RobotMatcen.cs:300)
   - wake on headlight (2093)

   **UNVERIFIED:** that the swap has no lingering effect through cached fields (`m_believed_player_location`, `AI_last_visible_player_segment`). These blend naturally when the target changes.
2. **Host chunk and collider culling** follows only the host unless P13 lands. **UNVERIFIED** whether chunks hold the colliders. If they do, the joiner's ship and robots near it are simulated against missing walls on the host.
3. **Loadout parity.** Until P15 and msg 170, the joiner's ammo weapons and missiles do not exist on the host. The shots show on the joiner, as local projectiles, but do nothing.
4. **Identity mismatch.** The robot set is affected by NG+ (`m_init_ng_plus`, Robot.cs:2047) and possibly by difficulty-dependent placement (**UNVERIFIED**). The joiner creates its game with **its own** difficulty and NG+ settings (Phase 1 C1 `CreateNewGame`). Send difficulty and NG+ in 162 and **force them on the joiner before `CreateNewGame`** (extend C1); otherwise the alive bitset handles mismatches. Saved-game levels do not match by path (§1.3).
5. **Ordering assumption.** `InitializeForNewLevel` runs before `Robot.Start` (needed for "all placed robots active at init"). Using `Resources.FindObjectsOfTypeAll` makes the registry independent of this. Only the NG+ and hidden *state* would differ, and the alive bitset and revealed flag fix that.
6. **Joiner-side scripts.** `triggered_on_death` links (Robot.cs:10531), triggers and level scripts still run locally on the joiner from its own ship and from replayed deaths. Doors and lockdowns may diverge until 2b. Matcen spawns are blocked (P3), so a script-activated matcen on the joiner only shows its "active" effects.
7. **Visual gaps.** Animations driven inside mode logic (charge, claw, detonator thrusters, teleport fade, blade spin speed) will look idle on the joiner. Add bytes later if needed.
8. **Kinematic puppets.** Ship-vs-robot contact on the joiner is a kinematic push. On the host, robots ram the joiner's ship for real, so there are small differences in bumping. Charger and claw impulses on the host apply to `target_rigidbody`, which is the joiner's ship after the swap. That pushes the server copy, and the joiner sees it through reconciliation (**UNVERIFIED** how smooth this is).
9. **Item replication** relies on item prefabs having a `NetworkIdentity` (**UNVERIFIED**) and on `NetworkServer.Spawn` working in a non-MP scene (ours is a live server, so it should).
10. **Bandwidth and packet size.** It fits easily, but every message must stay ≤ about 1000 B (no fragmenting channel). Batch splitting is mandatory.
11. **Player death** in co-op is still the MP path (`RpcApplyDamageWhenDying`, PlayerShip.cs:1705). It is untested and belongs to Phase 2c. Avoid dying in the 2a test.
12. **Host XP** includes the joiner's kills (Robot.cs:12036-12043 reads `m_local_player`). That is acceptable for 2a, but note it for the debrief and balance work.
