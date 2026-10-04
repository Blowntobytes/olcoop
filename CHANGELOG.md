# Changelog

## 0.4.10-world (2026-10-04), UNTESTED in game
- Fix (3 players): two joiners were put on exactly the same spot when joining a level, respawning, and on lockdown and exit regroups.
  Each placement rotated through the same short list of spots near the teammate, and in tight places only one spot was free, so both
  got it. Overlapping ships jam each other: in the 06:33 run both joiners barely moved after the level restart and in sp_titan_06
  (user: the second joiner could only turn his ship), and the second joiner's exit flight stalled. Spots are now rejected when a ship
  is already within 2.4 units or another ship was placed there in the last 3 s.
- The nearest-open-segment fallback also accepts tighter open spots before giving up.
- Lockdown/exit: when there is no free spot at all, the ship is no longer pushed 4 units behind the teammate (that could be inside a
  wall, as at the Goliath exit); it stays where it is and plays the exit from there.

## 0.4.9-world (2026-10-03), 3-player run 06:33: black hold after exit worked for joiner 1; joiners stacked on one spot (stuck, bad exit)
- Fix: after the exit cutscene the joiner kept spinning (rough in VR and flat). Once the exit flight ends, the game re-attaches the
  camera to the ship, clears the fade and keeps flying the ship every frame, waiting to finish a level that only the host can finish.
  Joiners now hold a black screen and a still ship until the host's next level loads; the status line stays readable on top. The
  fade is cleared when the next level starts.

## 0.4.8-world (2026-10-03), run 23:18: user "worked pretty good"; joiner spawned next to the host at the checkpoint; joiner spun after the exit
- Fix: a joiner could spawn at the level start instead of next to the host. When the host continues a saved game it starts at the
  checkpoint, and in a tight spot every close spawn offset around the host was in a wall or outside the level (run of 23:00: 12/12
  rejected, so the joiner went to the level start, far from the boss room). Now, when the close offsets fail, the joiner spawns at the
  nearest open segment within 4 segments of the host, never through a door. Lockdown and exit teleports use the same search.
- Protocol unchanged (10); host-side change only, but both players need 0.4.8 (the version must match to join).

## 0.4.7-world (2026-10-03), run 23:00: exit flight smooth (user), status line shown, lockdown moved the joiner; joiner spawned at level start
- Lockdown regroup now ignores distance: every other living player is teleported next to whoever set the lockdown off, wherever they
  are, so nobody gets locked out of the room. (0.4.6 skipped anyone within 25 units; on the Goliath run the joiner wasn't moved.)
- Fix: the joiner's exit flight didn't play properly at first (Goliath run: 26.6 s on the joiner vs 14.4 s on the host). Every physics
  tick the game pulls the joiner's own ship back to the host's copy of it, which doesn't fly the exit path, so the joiner's flight only
  played properly once the host had left the level. Joiners now ignore those corrections during the exit sequence.
- Joiners now see where the host is after an exit, as a status line that stays on screen: "LEVEL COMPLETE - WAITING FOR THE HOST",
  then "THE HOST IS ON THE LEVEL SUMMARY" when the host finishes the level, then "THE HOST IS STARTING THE NEXT LEVEL..." when the
  host loads the next one (the joiner also starts asking for it). It clears when the joiner's next level loads.
- The joiner's log no longer gets an EscapeLevel line every frame while it waits (at most one per 10 s).
- Protocol 10 (message 188 = host status).

## 0.4.6-world (2026-10-03), Goliath run: boss lockdown ran on both; regroup skipped the joiner (25-unit limit); joiner exit glitchy
- Exit regroup: when anyone reaches the exit door or an alien warp, the host first moves every other player's ship next to whoever got
  there, then starts the exit. The exit message (186) now carries that position, so every player plays the same exit/teleport sequence
  from the same spot. On a joiner-triggered exit the host also moves its own ship next to that joiner first. Dead players are not moved
  but still get the exit.
- Lockdown regroup: when a lockdown starts on the host (ScriptLockdownMaster / ScriptLockdownBoss), every other living player more than
  25 units away is teleported next to the player who set it off (the last ship to hit a trigger within 10 s, otherwise the living player
  nearest the lockdown script). Joiners see "CO-OP: LOCKDOWN - TELEPORTED TO YOUR TEAMMATE", the host "CO-OP: LOCKDOWN - TEAM REGROUPED".
  Each lockdown regroups once per level.
- Fix: the boss lockdown (ScriptLockdownBoss) ran only on the host (seen on the Goliath level with 0.4.5). It is no longer host-only, so
  joiners run it on the host's word like other replicated scripts.
- Protocol 9 (exit message 186 is now a position message; new message 187 = lockdown teleport).

## 0.4.5-world (2026-10-03), replaced by 0.4.6 before testing
- Fix: audio logs (log entry pickups) only played for one player. Real pickups run only on the host in co-op, so `PickupLogEntry` (chime +
  voice/text) never ran on joiners. The host now sends each log pickup and joiners play the same log.
- Exits together: when anyone reaches the exit door or an alien warp, every player plays the same exit/teleport sequence. Joiners ask the
  host, the host starts its own sequence and tells everyone; joiners then wait ("LEVEL COMPLETE - WAITING FOR THE HOST") for the host's
  next level instead of finishing the level alone. Teleport-out scripts on the host also take joiners along. If the host is dead
  (spectating) when a joiner reaches the exit, the host finishes the level directly. With a map or menu open, a joiner's exit starts
  as soon as they're back in play.
- Automap: other players' ships are shown on your map (their map icons are switched on while your map is open).
- Protocol 8 (messages 184/185/186).

## 0.4.4-world (2026-10-03): run 4, shots now shared (user didn't report a problem); audio logs only for the picker
- Fix: the joiner couldn't see the host's shots. The game only sends player shots to other machines in a real multiplayer match (game type
  check), and co-op runs as a campaign game. The host now sends every player shot (its own and the joiners' it simulates) to every other
  player except the shooter, the same way stock multiplayer does (message 70). This also covers joiners seeing each other's shots with 3 players.
- Destroyable hit forwarding (0.4.2) now only sends the joiner's own hits, so replicated shots of other players aren't counted twice.

## 0.4.3-world (2026-10-03): run 3, map key not reported on; host shots invisible on the joiner
- Fix: the map key did nothing in co-op (since Phase 1). Stock code ignores the map key whenever multiplayer netcode is on, which co-op
  needs. The map opens again in co-op levels; it never pauses the game in co-op.
- Host death logic (respawn countdown, team resets) now keeps running while the host has the map or the Esc menu open. It used to wait
  for PLAYING. Sending the level to a joiner also works while the host's map is open.
- Otherwise identical to 0.4.2 (joiner button hits, key sound), which was replaced before testing.

## 0.4.2-world (2026-10-03), replaced by 0.4.3 before testing
- Fix: a joiner couldn't break destroyable buttons (run 1 of phase 2b, 0.4.0). Only the host applies button damage, and the host never got
  the joiner's hits. The joiner now sends its button hits to the host, which applies them credited to the joiner's ship. The host also logs
  every button hit and whose shot it was (`[WORLD] host: destroyable ... hit ... by netId=`).
- Fix: when a joiner picked up a key, only the host heard the "security key acquired" sound (the pickup runs on the host). Joiners now play
  the same sound with the popup when the team key level rises.
- Protocol 7 (message 179).

## 0.4.1-world (2026-10-03), replaced by 0.4.2 before testing
- The version is now always visible: top-left of the main menu ("OLCOOP 0.4.1-WORLD - HOST/JOINER") and under the CO-OP OPTIONS title.
  (The old level-start HUD message scrolled away and was easy to miss.)
- Otherwise identical to 0.4.0 (world sync), which was replaced before testing.

## 0.4.0-world (2026-10-03), replaced by 0.4.1 before testing. New phase 2b, folder build-phase2b
- Host-authoritative level logic (`docs/phase2b-design.md`, `src/olcoop/Phase2bWorld.cs`): the host runs triggers, switches, pickups and
  every level script; joiners run only what the host tells them (doors, comm/voice messages, objective text, music, forcefields, lights,
  objects). Robot, counter, save and level-flow scripts are host-only. Voice messages should now play once, at the same time, for everyone.
- Fix: security keys out of sync. Only the host does real pickups in co-op, so a joiner never got the key. The team's best key level now
  applies to every player on every machine (with a "KEY ACQUIRED BY A TEAMMATE" popup on joiners).
- Fix: destroyable buttons out of sync. Destroyables are damaged and destroyed only on the host; joiners replay the destruction, so the
  doors they open match.
- Late joiners catch up on broken buttons, opened doors and the key level. World registry hash check; mismatch falls back to old behaviour.
- Protocol 6 (messages 177/178/180/182/183). 126 patches verified (the verifier now runs TargetMethods patches).
- Not yet tested: 0.3.10 Hardcore restart fix (carried over).

## 0.3.10-coop-options (2026-10-03), UNTESTED in game
- Hardcore / team-wipe restart reworked after run 7:
  - The joiner no longer tries to leave the level by itself (in 0.3.9 that left a frozen picture, not the main menu). It stays in the old
    level showing "CO-OP: WAITING FOR THE HOST'S RESTARTED LEVEL" and asks the host for it every 3 s until it arrives (was 10 s).
  - Host: after removing a joiner's old ship it now waits 1.5 s before sending the level. The joiner dropped a level message that arrived
    in the same burst (runs 6 and 7), which is why it took a second try (~10 s).
  - A duplicate level message within 6 s is ignored instead of reloading twice.
- Fix: the "cinematic panorama" view after the restart. The stock PVP death-pause flag (static, set when a ship dies) survived the reload,
  because the Hardcore path never cleared it. Every new local ship in a co-op level now starts with it cleared (also the pregame flag,
  cinematic bars and screen fade), and a `local ship start` log line records where the camera is.
- Respawn countdown confirmed working in game (run 7, 0.3.9).

## 0.3.9-coop-options (2026-10-03): run 7, respawn timer works; Hardcore joiner froze, then panorama view
- Respawn countdown drawn the way PVP does it: the stock game draws its timer from an overlay element (`CreateOverlayElement(zero, 1,
  MP_DEATH_OVERLAY)` at death), which the overlay camera renders above the death fade. Co-op now creates its own overlay element in that
  slot, so none of the PVP scoreboard/loadout appears. 0.3.8 drew from the HUD element; the log confirmed it ran, but nothing appeared.
- Fix (Hardcore/team wipe, joiner): the joiner stayed connected in 0.3.8, but after the host's reload removed its ship it never processed
  the new level. Now, as soon as the joiner gets the host's "restarting level" notice, it leaves the level cleanly itself, waits in the
  main menu while connected, and asks the host for the new level (the host sends it once loaded).

## 0.3.8-coop-options (2026-10-03): run 6, timer still invisible; Hardcore joiner stuck
- Fix: the respawn countdown was wiped the same frame it started. The host starts it when a ship begins DYING, and the local check cleared
  it whenever the ship wasn't yet DEAD. Now only a living ship clears it, and the digits show from the moment you die.
- Fix (Hardcore, joiner died): when the host restarts a level it removes every ship. Stock code took "my ship was removed" as "match over"
  (exit to menu, multiplayer off), so the joiner's co-op death handling switched off and the single-player death screen opened. While
  connected to the host the joiner now stays put and waits for the host's new level. A joiner choosing QUIT still exits normally.
- Fix: if a joiner asks for the level while the host is still loading (a restart), the host now remembers and sends it as soon as its
  level is playing (it used to give up).
- Safety net: the single-player death screen is blocked whenever a co-op level is running, even if game code flips the game type.

## 0.3.7-coop-options (2026-10-03): run 5, names work; timer still invisible; Hardcore broke the joiner
- Fix: the respawn countdown never showed in 0.3.6. It was drawn in the name-tag layer, which uses tiny world-overlay units, so the digits
  were huge and off-screen. It's now drawn by the HUD element in normal screen space, like the PVP timer (a HUD element is created if none
  exists while dead). New `[HUD]` log lines.
- Fix: names never showed in 0.3.6. Stock code only tags teammates in team matches, and co-op isn't one. Co-op now shows every player's tag
  during the name pass (stock "show enemy names: always", restored afterwards).
- Teammate health bar: olmod's green team-health bar has the same team-match check, so co-op draws an identical bar itself under the name.
  SHOW PLAYER NAMES off hides the names and keeps the bars.

## 0.3.6-coop-options (2026-10-03): run 4, timer and names did not show
- RESPAWN mode now shows the big yellow respawn countdown in the middle of the screen, the same one a PVP match uses. It replaces the
  "RESPAWNING IN n" messages.
- Pilot names above teammates, over olmod's team health bar. The host gives every player their pilot name (joiners send theirs after the
  handshake). The spectate message now says who you are watching.
- New **SHOW PLAYER NAMES** checkbox in CO-OP OPTIONS. It is each player's own setting (joiners can change it too), saved in
  `olcoop-settings.txt`. Turning it off hides the names but keeps the health bars.
- Rejoin: a joiner that is still connected and back at the main menu (for example after quitting the level) asks the host for its level
  again every 10 s and drops back into the game.
- Kept as is (user decision): if the host is dead and spectating and the last living joiner leaves, the level restarts.
- Protocol 5 (new name message 176). 85 patches, all verified offline.

## 0.3.5-coop-options (2026-10-03)
- Fix: after a RESPAWN the joiner's camera was destroyed (thousands of errors per second, broken view). The death camera rig was deleted
  while the game's main camera was still attached to it. The camera is now always moved back to the ship before the rig is removed.

## 0.3.4-coop-options (2026-10-03)
- Fix attempt: the host's view froze after dying in SPECTATE mode (first real spectate test; the game kept running underneath).
  The spectate camera now drives the main camera directly, re-attaches it every frame if stock death/pause code takes it,
  clears the death-pause flag, and also ticks from the per-frame hook. New `[SPECT]` log lines show what the camera is doing.

## 0.3.3-coop-options (2026-10-03)
- Fix: a joiner's second death could open the single-player death menu (co-op mode had been switched off by game code). Co-op now tracks
  its own in-level state, with a watchdog that keeps co-op networking on for the whole level.
- Fix: friendly fire was inconsistent after toggling (recycled projectiles kept old "ignore this ship" rules).

## 0.3.2-coop-options (2026-10-03)
- Fix: the screen was darker after a respawn (headlights stayed off, plus leftover damage blur and wrongly re-shown meshes).
- Respawn explicitly restores ship collision, and logs collider state for diagnosis (the "no collision" report).
- New **FRIENDLY FIRE** checkbox in CO-OP OPTIONS (default off: players can't hurt each other at all). Protocol 4.

## 0.3.1-coop-options (2026-10-03), UNTESTED in game
- Replaced the F8 window (F8 is the map key) with a native **OPTIONS → CO-OP OPTIONS** screen, directly below MULTIPLAYER OPTIONS. It's reachable
  from the main menu and from Esc → OPTIONS. RESPAWN / SPECTATE / HARDCORE are clickable, with the game's own hover description, plus a RESPAWN
  COOLDOWN option (3-60 s). Joiners see the host's values read-only. Design: `docs/menu-design.md`.

## 0.3.0-coop-options (2026-10-03), UNTESTED in game
- **Co-op Options window (F8):** death mode Spectate / Respawn (3-60 s cooldown) / Hardcore. The host's choice is saved and pushed to joiners.
- Death handling (`docs/death-design.md`, `src/olcoop/Phase3Death.cs`): a death no longer sends every player to their single-player death screen.
  The host decides respawns (next to the nearest living teammate, campaign loadout kept), spectating (camera follows living
  teammates), and team restarts (all dead, or any death in Hardcore).
- Joiners now follow the host into a restarted or next level while already in a level. Stale UNET players are cleared on level change.
- Protocol 3. 73 patches, all verified offline.

## 0.2.4-phase2a (2026-10-03): Phase 2a core PASS (run 5)
- Robot aggro: robots pick targets by distance, prefer whoever shot them recently, and switch players sensibly instead of locking onto
  whoever woke them. Also fixes a robot keeping half its targeting on the previous player.

## 0.2.3-phase2a (2026-10-03)
- Fix: a robot fired a continuous stream (olmod projectile smoothing crashed on robot shots mid-fire). Robot shots now also replicate to joiners.
- Fix: the HUD errored on every robot kill (MP kill feed). The kill feed is disabled in co-op.
- Confirmed in run 3: joiner kills count on the host, and robots damage players.

## 0.2.2-phase2a (2026-10-03)
- Fix: robots never fought in co-op (since Phase 1). Projectiles now use single-player team, layer and damage rules, and the host keeps the
  single-player ship collider layout so robots can see players.
- 3-player co-op confirmed working (run 2: host + 2 joiners, the same 73 robots on all three).

## 0.2.1-phase2a (2026-10-03)
- Fix ("two sets of robots" after the host used Continue): the joiner now discards its own robots and rebuilds the host's exact robot set on join.
- Joiners spawn next to the host's current position. Spawn probes ignore ships.

## 0.2.0-phase2a (2026-10-03), UNTESTED in game
- Host-authoritative robots (`docs/phase2a-design.md`, `src/olcoop/Phase2Robots.cs`):
  - deterministic robot ids from the scene hierarchy, with a hash check between host and joiner (msgs 162/163)
  - joiner robots are kinematic puppets: no local AI, firing, pathing, damage or self-death
  - the host streams robot state at 20 Hz (165), plus spawns (166), deaths (167), explosions (168) and robot fire (169)
  - the joiner suppresses local death drops (the host's drops are networked items) and local matcen spawns
  - host robots target the nearest visible player (per-robot target swap), and host robot relevance covers every player's segment
- The joiner adopts the host's difficulty (msg 171) so both load the same robot set. Protocol bumped to 2.
- 57 patches, all verified offline.

## 0.1.4-phase1 (2026-10-03): Phase 1 PASS for 2 players on one PC (run 5)
- Fix: the host couldn't apply the joiner's controls. olmod's client-physics input protocol is now switched off during co-op on both peers.
- Fix: the joiner spawned outside the level. Spawn candidates must be inside a level segment with line of sight from the start.

## 0.1.3-phase1 (2026-10-03)
- Fix: the joiner was stuck loading because olmod's remote-ship smoothing threw every frame (missing MP collider layout in campaign games).
- Safety net: olmod ship-smoothing exceptions are caught and logged in co-op instead of aborting the frame.
- The verifier checks olmod-targeted patches through an `OlmodTarget` const. 42 patches, all verified.

## 0.1.2-phase1 (2026-10-03)
- The version is stamped into the DLL from the `VERSION` file (single source).
- `install.bat` shows the version being installed and the version already installed, does a byte-for-byte verify, and reads the version back from the installed file.
- In-game HUD shows "OLCOOP x.y.z - HOST/JOINED" when co-op netcode turns on.
- Delivery process: each release is staged under a unique path, and the file size on the user's PC is checked after copying (0.1.1 was first delivered stale).

## 0.1.1-phase1 (2026-10-03)
- Fix: host froze from per-frame exceptions when co-op netcode was on (MP quick-chat buffer, and olmod's physics sender on a non-dedicated server).
- Spawn override is now a postfix, with diagnostics on every spawn decision. See `tests/phase1-results.md`, run 1.
- The verifier also checks patches that target olmod's own code. 39 patches, all verified.

## 0.1.0-phase1 (2026-10-03), UNTESTED in game
- Design changed to approach A (`docs/phase1-design.md`): joiners connect to a normal single-player host.
- `-coophost` / `-coopjoin <ip>` / `-coopport <n>`. The host listens on fixed UDP 7777. The joiner auto-connects from the main menu.
- Version handshake (msgs 160/161): a mismatched client is rejected with a HUD message and disconnected.
- The host sends its campaign level to verified joiners. Joiners spawn next to the host's level start, using a free-space check.
- Player netcode switched on in campaign games on both peers (`IsMultiplayerActive`, match PLAYING).
- Joiner keeps its host connection through menus, and local robots retarget to the joiner's networked ship.
- Log files include the PID, so two copies can run on one PC. New `olcoop-host.bat` and `olcoop-join.bat`.
- 36 Harmony patches, all verified offline against the game assembly.

## 0.0.1-phase0 (2026-10-02)
- Repo skeleton. Offline toolchain: Mono `mcs` build, a locally built ILSpy decompiler, and an offline Harmony patch verifier.
- Recon: `docs/architecture.md`, `docs/assumptions.md` (648 single-player references triaged into 29 to-dos),
  `docs/recon-flow.md`, `docs/recon-world.md`.
- Design decision: run co-op as a new MP match mode that loads story levels (option B in architecture.md).
- `Mod-olcoop.dll` loads via olmod's `-modded` Mod-*.dll mechanism. 19 read-only Harmony patches log gameplay and
  match state, level lifecycle, local and network players (netIds), robot counts and state, the reactor and escape
  timer, cryotubes, robot deaths and damage to players.
- Verified: compiles; all 19 patches resolve against the game assembly offline.
  **Not yet verified in game:** that needs the user's run of `tests/phase0-test.md`.
- In-game run (sp_outer_01, about 20 min): mod loads cleanly with 0 errors. SP turns out to run as a local UNET host,
  and at most 10 robots were active at once. See `tests/phase0-results.md`. Reactor/exit and LAN tests are still open.
