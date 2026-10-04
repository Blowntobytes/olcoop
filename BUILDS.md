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
| 0.4.0-world | 6 | 10-03 19:47 | replaced before test | - | - | tag v0.4.0 |
| 0.4.1-world | 6 | 10-03 20:02 | untested | - | - | tag v0.4.1, build-phase2b |
