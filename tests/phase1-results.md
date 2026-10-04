# Phase 1 results

## Run 1: 2026-10-03 07:18, build 0.1.0-phase1, two copies on one PC, level sp_outer_02. FAIL (host froze)
What worked (from logs):
- Host listened on UDP 7777. The joiner connected on attempt 1. Handshake `1|olcoop 0.1.0-phase1` was verified.
- The host sent its level. The joiner auto-loaded `sp_outer_02` (story index 1) and got its own networked ship (netId 75, local, not server).
- Joiner: 0 exceptions. Netcode came on, and the joiner played for about 3 minutes and quit cleanly.

What failed:
- The host froze. There were about 67,000 NullReferenceExceptions in 6 minutes (the Unity log reached 118 MB), starting the moment co-op
  netcode turned on at level start, before anyone joined.
  1. `UIElement.DrawQuickChatMP` → `GetStringWidth(null)`. The MP quick-chat buffer is only initialised by the MP lobby.
     **Fix 0.1.1:** `NetworkMessageManager.ClearQCMessages()` when co-op netcode turns on.
  2. olmod `MPServerOptimization_FixedUpdateProcessControlsInternal.Postfix`: its static `current` is null on a server
     with a local ship (olmod assumes dedicated servers). The throw aborted `PlayerShip.FixedUpdateAll`, so the joiner's ship
     was never simulated on the host. It sat motionless at the spawn on the host.
     **Fix 0.1.1:** skip that olmod postfix when the co-op host is the server.
- The joiner spawned exactly on the host's start (72,28,-16). Our ChooseSpawnPoint override left no trace in the logs, and
  the cause is unknown. **0.1.1:** the override is now a postfix, so it always runs, and it logs every call. An AddPlayer log was added too.
- A one-off olmod `KeyNotFoundException` happened in `MPTweaksOnLoadoutDataMessage` on the joiner's loadout message. It's harmless and was logged only.

## Run 2: 07:34, the user installed what was supposed to be 0.1.1, but 0.1.0 actually ran. INVALID
A stale DLL was delivered to the Desktop (29,696 bytes instead of 31,232), so the run repeated run 1. Since then: the
version is stamped in the DLL, install.bat verifies the copy and shows the version, and deliveries are checksum-verified on the PC.

## Run 3: 07:44, 0.1.2. Host OK, joiner stuck loading
- **Host: 0 per-frame exceptions** (only the known one-off olmod loadout KeyNotFound). The 0.1.1 fixes work.
- **Spawn works:** `ChooseSpawnPoint … stock=(72,28,-16)` → `joiner spawn at (77,28,-16) (offset 5,0,0)`.
  The run 1 mystery was a log gap, not a logic bug.
- The joiner connected, did the handshake, loaded the level and got netId 75 at (77,28,-16). It then got about 8,300 NREs in olmod
  `MPClientShipReckoning.interpolatePlayer` / `extrapolatePlayer`: the remote (host) ship's `c_mesh_collider_trans`
  is null, because stock `PlayerShip.Start` only detaches the mesh collider when `IsMultiplayer`. The throw came from
  `Client.Update` and aborted `GameManager.Update`, so the joiner never left MENUS for PLAYING ("frozen on start").
- **Fix 0.1.3:** detach the mesh collider like MP in co-op (F3), destroy it with the ship (F4), and add a finalizer so olmod
  smoothing errors can't abort the frame in co-op (F5, logged and rate-limited).

## Run 4: 07:54, 0.1.3. Both ran about 2 min; joiner spawned in a wall and couldn't move
- Loading is fixed: the joiner reached PLAYING and the colliders were detached on all ships. Ship-to-ship contact was seen on both peers
  (0-damage `DMG … from=entity_special_player_ship` on netIds 2 and 75), so the ships exist in both worlds.
- Spawn (77,28,-16) was outside the level shell. `Physics.CheckSphere` treats the void behind walls as free.
  **Fix 0.1.4:** a candidate must be inside a level segment (`FindSegmentContainingWorldPosition`, strict), have line of
  sight from the start, and have room. Every candidate is logged.
- Host: about 4,900 NREs in `Player.DecodePlayerInput`. olmod's client-side-physics input packet has no encoded input,
  and only a dedicated server with the `cphysics` tweak understands it, so the host could never apply the joiner's controls.
  **Fix 0.1.4:** `GameMod.MPServerOptimization.enabled = false` on both peers while co-op netcode is on (restored at level end).
- Joiner: about 1,600 `IndexOutOfRange` in `RobotManager.RobotInRelevantSegment`. This is most likely the out-of-level ship segment.
  Recheck after the spawn fix.

## Run 5: 08:11, 0.1.4. PASS (2 players, one PC)
- The user reports both ships fly and each window shows the other moving.
- Spawn: 5 candidates rejected (4 "outside level", 1 "blocked by door02-Bframe"). Accepted (-4,0,0) → (68,28,-16), seg 1197.
- Exceptions: host 0 repeating (only the known one-off olmod loadout KeyNotFound). Joiner: **0**. The run 4 RobotInRelevantSegment
  errors are gone, confirming they came from the out-of-level spawn.
- Joiner log tracks the host ship moving (72,28,-16) → (35.8,26.2,-12.4) → back, i.e. snapshots flow host→joiner.

### Phase 1 acceptance status
| Criterion | Status |
|---|---|
| Players load the same campaign level | PASS (2 players) |
| See each other's ships move in sync | PASS (user-observed + logs) |
| Each controls only its own ship | PASS |
| No desync errors in logs | PASS |
| **3 players** (host + 2 clients) | UNVERIFIED. Needs a third instance or machine. Nothing in the code limits it to 2 (spawn offsets rotate per joiner). |
| LAN / Internet (separate machines) | UNVERIFIED. Only tested over 127.0.0.1. |
| Robots synced | Out of scope (Phase 2). Each instance still runs its own robots. |
