# Phase 0 results (2026-10-02, user's PC "3090badass", game 1.1.1886.0, olmod 0.5.14)

Log: `tests/logs/phase0-sp_outer_01.log`

| Test | Result |
|---|---|
| 1. Mod loads, game unchanged | PASS. INIT line present; user reported nothing unusual; 0 ERROR lines. |
| 2. SP campaign level (sp_outer_01) | PASS (2nd run, `phase0-sp_outer_01-complete.log`): level completed. Level 1 has no reactor; it ends by teleport: `TeleportSequenceStart` → `EscapeLevel` → `DoneLevel Escaped`, with 0 errors and the GUIDEBOT as the only robot left. The reactor/escape countdown is still unseen (later level). First run: PARTIAL. About 20 minutes of play, 41 robot kills, 142 damage events, automap use, quit. The level was not completed, so the reactor, escape and exit events were not exercised. |
| 3. olmod LAN match | SKIPPED. The lobby needs 2 players; it will be covered by the Phase 1 two-instance test. The lobby was opened twice (`match NONE -> LOBBY -> NONE`) but no match was started. |

## Key findings
1. **Single-player already runs as a local UNET host.** In SP: `NetworkServer.active=True`,
   `NetworkClient.active=True`, the local `Player` is a networked object (`netId=2`, `isServer=True`), and
   `NetworkMatch` goes `NONE -> PREGAME` and stays there. `IsMultiplayer` / `IsMultiplayerActive` are False.
   This means a campaign level is *already* hosted on the network stack, so remote clients joining an SP host
   (architecture option A) may be cheaper than first estimated. Phase 1 will spike both A and B before committing.
2. **Robot load:** sp_outer_01 starts with 55 robots (GRUNTA 19, RECOILA 13, HULKA 10, GRUNTB 8, CLAWBOTB 5).
   At most **10 were active at once** across about 230 samples, and usually 0-5. Bandwidth for robot sync
   will be small: roughly 10 × 20 Hz × 32 B ≈ 6.4 KB/s per client.
3. `m_master_robot_list` empties over time (55 → 0 while about 14 robots were still alive), so it isn't the
   whole-level list. It probably holds only robots in relevant chunks. Phase 2 needs its own registry for entity IDs.
4. A Player with netId=1 is created at the main menu before any level loads (the menu "attract" or host player).
