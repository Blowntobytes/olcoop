# Builds

One row per build that was installed for testing. Status is only `pass` for features the user confirmed in game.
Built DLLs for tagged versions are attached to the matching GitHub Release (`vX.Y.Z`). Earlier builds were not archived:
only the last build of each phase folder survives (`build-phase0`, `build-phase1`, `build-phase2a`, `build-coop-options`).

Targets unless noted: Overload 1.1.1886, olmod 0.5.14.

| Version | Proto | Installed | Status | Confirmed working in game | Known bad / not working | Recoverable |
|---|---|---|---|---|---|---|
| 0.0.1-phase0 | - | 10-02 | pass (run 1) | mod loads, read-only logging | - | build-phase0 |
| 0.1.4-phase1 | 1 | 10-03 | pass (run 5) | 2 players on one PC, joiner controls, spawn | - | build-phase1 |
| 0.2.4-phase2a | 2 | 10-03 | pass (run 5) | host-authoritative robots, 3 players, aggro | joiner ammo weapons/missiles don't hurt robots | build-phase2a |
| 0.3.1 - 0.3.4 | 3-4 | 10-03 | tested | co-op options menu, respawn, friendly fire toggle | see tests/coop-options-results.md | no |
| 0.3.5 | 4 | 10-03 14:51 | tested (run 3) | joiner camera after respawn, host spectate camera | - | no |
| 0.3.6 | 5 | 10-03 15:28 | tested (run 4) | rejoin from main menu (likely) | respawn timer, names not shown | no |
| 0.3.7 | 5 | 10-03 15:56 | tested (run 5) | player names + health bars | respawn timer; Hardcore breaks joiner | no |
| 0.3.8 | 5 | 10-03 16:08 | tested (run 6) | - | respawn timer; Hardcore joiner stuck | no |
| 0.3.9 | 5 | 10-03 16:25 | tested (run 7) | **respawn countdown** | Hardcore: joiner frozen, then panorama view | no |
| 0.3.10 | 5 | 10-03 16:58 | untested | - | - | tag v0.3.10, build-coop-options |
| 0.4.0-world | 6 | 10-03 19:47 | tested (2b run 1) | host-broken buttons + script doors + comm messages sync to joiner | joiner can't break buttons; key sound host-only | tag v0.4.0 |
| 0.4.1-world | 6 | 10-03 19:49 | not tested (version display only) | - | - | tag v0.4.1 |
| 0.4.2-world | 7 | 10-03 19:59 | tested (2b run 2, log) | joiner breaks a button via the host (20:07:45) | - | tag v0.4.2 |
| 0.4.3-world | 7 | 10-03 20:34 | tested (2b run 3) | joiner breaks a button (20:40:34) | host shots invisible on joiner | tag v0.4.3 |
| 0.4.4-world | 7 | 10-03 20:54 | tested (2b run 4) | - | some audio logs only for the picker; no shared exits; no teammates on map | tag v0.4.4 |
| 0.4.5-world | 8 | 10-03 21:50 | partly run (Goliath exit, log) | - | boss lockdown host-only | tag v0.4.5 |
| 0.4.6-world | 9 | 10-03 22:24 | tested (Goliath run) | boss lockdown runs on joiner; exit regroup moved joiner next to host | lockdown regroup skipped nearby joiner; joiner exit flight dragged; no host status after exit | tag v0.4.6 |
| 0.4.7-world | 10 | 10-03 22:52 | tested (23:00 run) | **joiner exit flight** (user: looks better); status line + next level loaded (log); lockdown moved joiner (log) | joiner spawned at level start, not near host | tag v0.4.7 |
| 0.4.8-world | 10 | 10-03 23:10 | tested (23:18 run) | **joiner spawns next to host at a checkpoint**, lockdown regroup, exit, next level (user: worked pretty good) | joiner view keeps spinning after its exit flight | tag v0.4.8 |
| 0.4.9-world | 10 | 10-03 23:25 | tested (3 players, 06:33) | 3 players join; lockdown pulls both joiners; joiner 1 exit + black hold + next level (log) | joiners placed on the same spot: stuck after reload/next level, 2nd joiner exit stalled | tag v0.4.9 |
| 0.4.10-world | 10 | 10-04 06:52 | tested (3 players, 07:13, working pilot) | separate spawns; all 3 ships fly; lockdown pulls everyone; all 3 exit, joiners black hold + status (log; user: "worked better") | at the Goliath exit no free spot next to the joiner, so each ship exits from its own spot; a joiner spawning in the boss trigger starts the lockdown at once | tag v0.4.10 |
| 0.4.11-world | 11 | 10-04 07:34 | not tested (replaced by 0.4.12) | - | - | tag v0.4.11 |
| 0.4.12-world | 12 | 10-04 07:46 | tested (07:47 run) | joiner loadout + upgrades reach the host (log); exit line-up placed (log) | exit clog with a joiner first; 278 joiner shots dropped by olmod rate check; Devastator not selectable on a joiner | tag v0.4.12 |
| 0.4.13-world | 12 | 10-04 07:56 | tested (08:01 run) | joiner shots no longer dropped by olmod (0 vs 278); loadout carried to next level (log) | Devastator unlocked but 0 ammo on the joiner; carry-over dropped the new level's Flak | tag v0.4.13 |
| 0.4.14-world | 13 | 10-04 08:23 | tested (08:24-08:49, 3 runs) | **Devastator picked up by a joiner is selectable and fires**; melee hits the joiner it attacks; missile pickups reach joiners without double counting; 0 shots dropped (log) | - | tag v0.4.14 |
| 0.4.15-world | 13 | 10-04 09:28 | untested | - | - | tag v0.4.15, build-phase2b |
