# Phase 2a results

## Run 1: 08:39, 0.2.0. FAIL: "two sets of robots, no aggression, not destroyable"
- The host loaded `sp_outer_02` **from a save** (`CreateNewGame … saved=True`). SaveLoad destroys the scene's placed robots
  and re-creates them as clones (`SaveLoad.DeserializeObjectsTransient`), so the host registry was 166 entries
  (93 originals pending destroy + 73 live clones) while the joiner's fresh load had 93. The hash didn't match, so the joiner kept
  its own frozen puppets (no AI, damage blocked) and the host's state went to unrelated ids. That's the "two sets".
- The netcode itself worked: the host sent about 3 robot states/s, the joiner received them, and there were 0 exceptions on either side.
- The joiner spawn fell back to overlapping the host ("no safe offset"). The host ship was at the level start, and its
  collider blocked every probe because the probes used all layers.
- **Fix 0.2.1:** stop matching robots. On join, the host sends a world reset (162) and then a spawn (166) for every live robot
  it has (type, pose, super/variant, hidden). The joiner deletes its locally loaded robots and builds the host's exact set. This
  works for saves, mid-level joins, NG+ and difficulty. Spawn now anchors on the host ship's current position (fallback: the level start), and
  probes use the robot LOS layer mask (level geometry + doors, not ships).

## Run 2: 08:48, 0.2.1, **3 players** (host + 2 joiners on one PC). Sync OK; robots didn't fight
- **3-player join works:** both joiners did the handshake, loaded the level, spawned near the host, got the host's exact 73 robots
  (`world sync: removed 73 local robots` and 73 spawns each), and received the state stream (up to about 290 entries/5 s). 0 unknown ids.
- **Robot combat was dead on the host for the whole session:** fire=0, no ExplodeNow, no robot damage. Phase 1 runs 4/5 also
  show 0 robot kills and 0 robot hits with co-op on, while Phase 0 (stock SP) had 41 kills. Two root causes:
  1. `Projectile.Fire` with `IsMultiplayerActive` sets every projectile's team to ENEMY and moves player shots to MP layer 13
     (Projectile.cs:1360-1366, 1859-1864), so player shots can't damage robots. `ProcessCollision` also skips
     `MaybeAwakenRobots` (567). **Fix 0.2.2:** run `Projectile.Fire` and `ProcessCollision` under SP rules
     (`IsMultiplayerActive=false` for the duration) in co-op.
  2. `Robot.VisibilityRaycast` only counts a hit on `target_go` itself (Robot.cs:7920). The Phase 1 F3 fix detached every
     ship's mesh collider (MP layout), so robots never saw any player, never woke and never fired.
     **Fix 0.2.2:** the MP collider layout is joiner-only. The host keeps the SP layout.
- New host diagnostics: `robotShots` and `robotHitsTaken` per 5 s in the RSYNC line.

## Run 3: 09:05, 0.2.2, 3 players. Combat partly alive; one robot fired a stream of shots
- **Damage authority works:** host `robotHitsTaken` up to 9 per 5 s, and **the joiner killed a GRUNTB** (`death id=50 killer=75`), replicated
  as `ExplodeNow` on the host. Robots hit players: 26× `dmg=9.0 from=GruntA` on the host. The host died once (robot fire spam).
- **Shot spam:** 568 NREs in olmod `MPClientExtrapolation.LerpProjectile`, called from `FireProjectile` ← `FireProjectileRobot` ← `Robot.MaybeFire`.
  olmod's bail-out uses legacy `Network.isServer` (always false under UNET), then dereferences `c_proj.m_owner_player`, which is null for robot
  shots. The throw aborted `MaybeFire` before the fire timer reset, so the robot fired every frame. It also skipped our P10 postfix, which is why
  `robotShots=0` and no fire was replicated. **Fix 0.2.3:** skip `LerpProjectile` in co-op for ownerless shots and on the host.
- 349 (host) / 765 (joiner) IndexOutOfRange in `UIElement.DrawRecentKillsMP` (the MP kill feed drawn because of `IsMultiplayerActive`).
  **Fix 0.2.3:** no MP kill feed in co-op.
- "Other robots moved aimlessly": not yet explained. Re-check once the spam is gone (it may be the target swap, or robots that were never awake).

## Run 4: 09:20, 0.2.3. Robots fight normally but lock onto whoever woke them
- The user reports: firing rate normal, robots attack, but "robots attack the first person who triggers them and ignore the second player".
- Cause 1: the 0.2.0 target choice kept the current target for as long as it stayed visible (hard hysteresis), with no aggro from damage.
- Cause 2 (bug): when the chosen target was the host ship, `Push` returned early without resetting the robot's per-robot `target_go`.
  After a robot had once targeted a joiner, `target_go` stayed on the joiner while the statics pointed at the host. `VisibilityRaycast`
  then compared hits against the wrong ship, so the robot effectively saw neither player properly.
- **Fix 0.2.4:** threat score = distance × 0.45 if that player hit the robot in the last 4 s × 0.75 if it's the current target
  (soft stickiness), re-checked every 0.4 s and immediately after a hit. `target_go`/`target_rigidbody` always follow the chosen ship.
  On a switch, the robot's believed player location and last visible segment move to the new target.

## Run 5: 0.2.4. PASS (user: "yay!")
- Robots switch between players. Combat, kills, robot fire and the single shared robot set work across host + joiner.

### Phase 2a status
| Item | Status |
|---|---|
| One shared, host-run robot set on every peer (incl. Continue/saves, mid-level join) | PASS |
| Robots attack any player, aggro switches | PASS (0.2.4) |
| Kills by host and joiner, shown everywhere | PASS |
| Robot shots visible on joiners, normal fire rate | PASS (0.2.3+) |
| 3 players | Join + sync PASS (run 2). Combat with 3 not separately tested |
| Joiner ammo weapons / missiles damage robots | TODO (loadout sync, msg 170) |
| Host chunk/wall activation around joiners | TODO (P13), not yet observed as a problem |
| Claw/charger melee hitting the joiner damages the host | KNOWN BUG (2b) |
| Doors, switches, placed pickups, reactor, escape timer, survivors | Phase 2b/4 |
| Player death in co-op | Untested / unhandled |
