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
| 0.4.15-world | 13 | 10-04 09:28 | tested (09:44 run) | **joiners get results + upgrade screens** (user: mostly worked); upgrades captured (joiner 1 weapon levels 2,2,..; joiner 2 missile levels 2,2) (log) | joiners stuck on WAITING after the host started the next level | tag v0.4.15 |
| 0.4.16-world | 13 | 10-04 09:53 | tested (09:56 run) | NotReady handled; host's level reached a joiner on the first send (log) | joiner dead at the exit: no end-of-level screens; a joiner that landed in the main menu after the results never loaded the host's level | tag v0.4.16 |
| 0.4.17-world | 13 | 10-04 10:10 | tested (10:15 run) | living joiner: end screens, held host level, loaded it when done (log) | dead joiner at the exit: results screen frozen | tag v0.4.17 |
| 0.4.18-world | 13 | 10-04 10:22 | tested (10:31-10:48 runs) | 3-player exit + end screens + next level (log) | host dead (hardcore) when a joiner exited: host finished dead, menus froze | tag v0.4.18 |
| 0.4.19-world | 14 | 10-04 10:55 | tested (11:05 run) | dead joiner revived into the exit door; host held at 0/2 -> 2/2 ready; user: Devastator, melee, line-up, boss lockdown on entry, lockdown pull, status lines, teammate maps, spectate switching all work | spectators: darker lighting, no headlights from the followed ship | tag v0.4.19 |
| 0.4.20-world | 14 | 10-04 11:30 | tested (11:32 run) | - | spectators still dark: [SPECT] log shows the followed ship's headlightsOn=False on the spectator's copy | tag v0.4.20, build-phase2b |
| 0.5.0-online | 15 | 10-04 12:17 | tested (12:31-13:06, two PCs over Steam) | Steam lobby, invite, join both ways, levels and level changes over Steam (user: incredibly successful) | disconnected players stayed in the game; crash in SteamAPI_RunCallbacks (12:51); boost not shown for other players | tag v0.5.0, build-online |
| 0.5.1-online | 16 | 10-04 13:52 | tested (13:33-13:47, two PCs over Steam) | goodbye on quit dropped the joiner at once (13:38:02); no crash at quit | boost never reported ON; quit to menu kept the session; disconnects broke the other game | tag v0.5.1 |
| 0.5.2-online | 16 | 10-04 14:13 | tested (14:04-14:08, two PCs over Steam) | Esc entries shown; host's boost ON/off reported and relayed to the joiner (log); host quit -> joiner left the level at once | LEAVE SESSION / STOP HOSTING did nothing when clicked; dead joiner got a death screen after the host left | tag v0.5.2 |
| 0.5.3-online | 16 | 10-04 14:20 | tested (14:16-14:23, two PCs over Steam) | LEAVE SESSION and STOP HOSTING work (user); boost ON/off reaches the other side both ways (log) | other side noticed the leave 10-15 s late; no boost flames; rejoin right after leaving dropped | tag v0.5.3 |
| 0.5.4-online | 16 | 10-04 14:38 | not tested (replaced by 0.5.5) | - | - | tag v0.5.4 |
| 0.5.5-online | 17 | 10-04 15:55 | partly run (16:04-16:23, sp_outer_02; no objective level) | - | user: PvP scoreboard on the HUD; Ymir 2nd key missing (0.5.4 run) | tag v0.5.5 |
| 0.5.6-online | 17 | 10-04 17:00 | tested (16:59-17:17, host log only) | [KEY] trace: restored keys deleted right after loading a save | key + audio log missing on a hosted save; joiner could not break a button | tag v0.5.6 |
| 0.5.7-online | 17 | 10-04 17:22 | tested (17:25-17:30 save; 17:33-17:46 fresh Ymir + Tarvos, both logs) | fresh level: host gets both keys + 5 audio logs; joiner broke the button | save: keys + audio log missing; fresh: keys/logs/some power-ups invisible on the joiner (pickup works) | tag v0.5.7 |
| 0.5.8-online | 17 | 10-04 17:35 | not tested (17:33 run still on 0.5.7) | - | - | tag v0.5.8 |
| 0.5.9-online | 17 | 10-04 18:05 | not tested (replaced by 0.5.10) | - | - | tag v0.5.9 |
| 0.5.10-online | 17 | 10-04 17:57 | tested (17:59-18:13, two PCs, both logs + Unity logs) | saved game: 72 saved items re-created incl. 2 keys + 6 audio logs; joiner broke a button through the host on a save; invite opened the Steam friends list; no exceptions in Unity logs (user: looks real good) | - | tag v0.5.10, build-online |
| 0.6.0-alpha | 17 | 10-04 18:25 | tested (19:20-20:53 CA-WI, joiner logs; 21:03-21:12 both logs) | 5 campaign levels over Steam, level hand-offs, saved game (Titan) | map close froze (joiner, host took a key); host: no pickup sounds; joiner: no ammo/energy from pickups, no shared upgrade points, points/ship upgrades lost between levels; hologuide can't find keys on joiner; robot lag at long distance | tag v0.6.0-alpha |
| 0.6.1-alpha | 18 | 10-04 22:29 | tested (06:58-07:17, both logs, saved Tarvos) | host pickup sounds (user); shared upgrade points reached the joiner (log) | host armor/energy above the 120 refill cap (200 from its save); reactor sound maybe doubled (unconfirmed) | tag v0.6.1-alpha |
| 0.6.2-alpha | 19 | 10-05 08:24 | tested (15:51-16:01 LAN, both logs, saved Titan) | session list, starting armor/energy 120, hologuide (user); joiner ammo pickup (log) | ship moves/fires in the map; some joiner pickups and buttons don't register | tag v0.6.2-alpha |
| 0.6.3-alpha | 20 | 10-05 17:03 | not tested (replaced by 0.6.4) | - | - | tag v0.6.3-alpha |
| 0.6.4-alpha | 21 | 10-05 17:16 | tested (16:42-16:52 LAN, both logs, saved Titan, PeetzaGuest host) | joiner touch pickups 16/16 (log); controls held in the map (log); spectate readout drawn (log) | spectating doesn't look like playing; claws sometimes ignore players | tag v0.6.4-alpha |
| 0.6.5-alpha | 22 | 10-05 17:52 | partly tested (user saw the ping sphere) | ping sphere shown | too big; not visible through geometry everywhere | tag v0.6.5-alpha |
| 0.6.6-alpha | 22 | 10-05 18:12 | partly tested (user) | ping sphere teal, through geometry | 25% too small; PING wheel slot not consistent | tag v0.6.6-alpha |
| 0.6.7-alpha | 22 | 10-05 18:28 | tested (user: looks good) | ping size, hologuide to a ping, map hint | - | tag v0.6.7-alpha |
| 0.6.8-alpha | 23 | 10-05 18:45 | tested (19:29-20:45, 4 players over Steam, host DescMax7930; host + 2 joiner logs) | 4-player session, level hand-offs, exits, lockdown regroups (log) | joiner: no lockdown counter/alarm, lockdown doors open (invisible wall), teleport outside the room; Goliath bar host-only; joiner stuck in the exit flight (behind a wall); no READY UP for a player joining between levels; spectating dark | tag v0.6.8-alpha |
| 0.6.9-alpha | 24 | 10-05 21:45 | tested (04:43-05:05, PeetzaGuest host, joiner log) | READY UP from the menus, exit flights from the exit path (log) | fabricator FX, Shredder blades, cryotube message missing on joiner; no READY UP before the first level; spectating shows the death view | tag v0.6.9-alpha |
| 0.6.10-alpha | 25 | 10-06 05:45 | tested (14:28, PeetzaGuest host, joiner log) | - | spectating unchanged (user); far cryotubes "not found" on the joiner | tag v0.6.10-alpha |
| 0.6.11-alpha | 25 | 10-06 14:55 | tested (14:48, both logs) | no black bars while spectating (user) | no HUD while spectating; Esc menu pauses robots | tag v0.6.11-alpha |
| 0.6.12-alpha | 25 | 10-06 15:30 | partly run (15:10, both logs) | - | joining failed: PeetzaGuest's Steam had no server connection (lobby k_EResultNoConnection) and he was left hosting | tag v0.6.12-alpha |
| 0.6.13-alpha | 25 | 10-06 15:40 | untested | - | - | tag v0.6.13-alpha, build-alpha, dist/olcoop-0.6.13-alpha.zip |
| 0.6.14-alpha | 25 | 10-06 | partly tested (15:54 run: user thinks HUD fixes work; restart froze) | - | - | tag v0.6.14-alpha, build-alpha, dist/olcoop-0.6.14-alpha.zip |
| 0.6.15-alpha | 25 | 10-06 | partly tested (16:05 run) | team-wipe restart; spectating HUD/timer (user: almost right) | claws/Shredder on joiner | tag v0.6.15-alpha, build-alpha, dist/olcoop-0.6.15-alpha.zip |
| 0.6.16-alpha | 26 | 10-06 | tested (16:26 run) | - | claw animation glitchy (replayed every update) | tag v0.6.16-alpha, build-alpha, dist/olcoop-0.6.16-alpha.zip |
| 0.6.17-alpha | 27 | 10-06 | tested (16:51 run) | - | Scorpions near joiner stare (host animator culling) | tag v0.6.17-alpha, build-alpha, dist/olcoop-0.6.17-alpha.zip |
| 0.6.18-alpha | 27 | 10-06 | partly tested (user: Scorpions near joiner now attack) | Scorpions near joiner attack | - | tag v0.6.18-alpha, build-alpha, dist/olcoop-0.6.18-alpha.zip |
| 0.7.0-challenge | 28 | 10-06 | untested (superseded before testing) | - | - | tag v0.7.0-challenge, dist/olcoop-0.7.0-challenge.zip |
| 0.7.1-challenge | 28 | 10-06 | tested (18:14 run) | host side ran the challenge (spawns, score, run end) | joiner had no ship; briefing not opened from CO-OP screen | tag v0.7.1-challenge, dist/olcoop-0.7.1-challenge.zip |
| 0.7.2-challenge | 28 | 10-06 | tested (18:33 run) | joiner ship + own loadout in challenge | results screen after team wipe not visible | tag v0.7.2-challenge, dist/olcoop-0.7.2-challenge.zip |
| 0.7.3-challenge | 28 | 10-06 | tested (18:48 run) | no wreck spin at run end; joiner loadout + ship | results screen not shown after team wipe; saved-game doors locked for joiner | tag v0.7.3-challenge, dist/olcoop-0.7.3-challenge.zip |
| 0.7.4-challenge | 29 | 10-06 | untested | - | - | tag v0.7.4-challenge, build-challenge, dist/olcoop-0.7.4-challenge.zip |
