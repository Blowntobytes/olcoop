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
| 0.4.8-world | 10 | 10-03 23:10 | untested | - | - | tag v0.4.8, build-phase2b |
