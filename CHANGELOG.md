# Changelog

## 0.7.1-challenge (2026-10-06) - challenge death rules, UNTESTED build (all players must run 0.7.1-challenge; protocol 28)
User's revised rules for challenge co-op (replaces 0.7.0's "follow the CO-OP OPTIONS death setting" in challenge mode only):
- Infinite: dead players spectate; the run ends (results for everyone) when every player is dead.
- Countdown: dead players respawn after the cooldown, like multiplayer; deaths never end the run, only the clock does. If nobody
  is alive, players come back at the level's start points.
- The CO-OP OPTIONS death setting still applies to the campaign.

## 0.7.0-challenge (2026-10-06) - NEW PHASE: challenge mode in co-op, UNTESTED build (all players must run 0.7.0-challenge; protocol 28)
New build folder: build-challenge (build-alpha keeps 0.6.18-alpha).
- New: PLAY CHALLENGE on the host's CO-OP screen opens the stock challenge level select (level, countdown/infinite, difficulty).
- When the host reaches the loadout (briefing) screen, joiners in the main menu are taken to the same briefing to pick their own
  weapons and missiles; START marks them ready and they wait. The host's START waits until every joiner is ready. A joiner who
  presses READY UP instead (or joins mid-run) gets the game's random loadout.
- The host runs the challenge (robot waves, timer, score); joiners see the same score, kills, combo and countdown (with the last
  10 seconds' beeps). Kills by any player count. New robots appear around a random living player, not only around the host.
- Kill upgrades (every 25 kills, 20 in countdown) are given to every player's ship.
- Deaths follow the CO-OP OPTIONS death setting: Respawn and Spectate keep the run going while anyone is alive; Hardcore ends it
  on the first death. The run ends for everyone (results screen on every machine) when the countdown runs out or nobody is left.
- Co-op runs keep your local best score but are not uploaded to the Steam leaderboards.
- Known gap: the "INCOMING SUPER AUTO-OP" warning shows only on the host.

## 0.6.18-alpha (2026-10-06) - Scorpions near a joiner, UNTESTED build (all players must run 0.6.18-alpha; protocol 27)
From the 16:51 run on 0.6.17 (both logs, PeetzaGuest hosting). User: Scorpions near the joiner just stare at him unless the host is near.
- Fix: the host only animated robots its own camera could see. A Scorpion only moves on to its next action when its current
  animation finishes, so one near the joiner but out of the host's view stayed "waking up" forever and just turned to face him
  (joiner log: Scorpion 3 stuck in its waking animation from 16:54:35 to 16:57:17). The host now animates every robot.

## 0.6.17-alpha (2026-10-06) - Scorpion animation fix, UNTESTED build (all players must run 0.6.17-alpha; protocol 27)
From the 16:26 run on 0.6.16 (both logs). User: Scorpion animation partial and glitchy; still didn't attack with the host far away.
- Fix: the joiner restarted the same animation every update (joiner log: "animation set from the host" for the same claw every
  50 ms), so swings never played through. Animations now change only when the host's robot changes animation, and the animation speed
  is sent too (sleeping robots run their animation at speed 0).
- Attacks: the host log shows claws hitting the joiner with the host far away (16:28:20, host ~57 units away; also 16:27:58 and
  16:29:14). With the swing animation broken those hits had no visible attack. If a Scorpion still ignores the joiner, note the time.
  Protocol 27.

## 0.6.16-alpha (2026-10-06) - robots hunting joiners, robot animations, UNTESTED build (all players must run 0.6.16-alpha; protocol 26)
From the 16:05 run on 0.6.15 (both logs). User: spectating HUD almost right (selected weapon not highlighted); Scorpions show no
animation for the joiner and don't attack until the host comes near; Shredders show sparks but not the full animation.
Confirmed in that run: team-wipe restart now works (host log "level restart; no ready check").
- Fix: robots going after a joiner used a stale "where is the player" segment (the host only keeps its own ship's segment up to date,
  a joiner's stayed 0). Claws chased the wrong place and never switched to attack; the host log shows the joiner's first claw hit only
  once the host arrived. The joiner's segment is now kept current for the robot AI.
- Fix: robot animations (claw swings, Shredder attack arms, waking, etc.) are now sent from the host and played on joiners.
  Protocol 26: every player must update.
- Not changed: the selected-weapon highlight in the spectating HUD.

## 0.6.15-alpha (2026-10-06) - team-wipe restart freeze, UNTESTED build (all players must run 0.6.15-alpha; protocol 25)
From the 15:54 run on 0.6.14 (joiner log). User: one player spectating, the other died, both screens froze instead of restarting.
- Fix: on a team wipe the host reloads the level, but since 0.6.10 its level start waited for every joiner to press READY UP - and
  the joiners were still in the level, dead, with no button to press. Joiner log: "TEAM WIPED" at 15:57:09, then "the host is waiting
  for players to ready up" at 15:57:13, nothing more until the host quit. Restarts (team wipe, hardcore) now skip the ready check.

## 0.6.14-alpha (2026-10-06) - spectating HUD, respawn timer, Esc while spectating, READY UP on join, UNTESTED build (all players must run 0.6.14-alpha; protocol 25)
From the 15:18/15:25 run on 0.6.13 (both logs). User: READY UP only appears when the campaign is about to start; Esc does nothing while
spectating; still no HUD while spectating; the respawn timer is gone.
- Fix (spectating HUD): both logs showed `uiRenderer=False` - the HUD surface itself was switched off. Cause was ours: hiding your
  wrecked ship also switched off every renderer under it, and the HUD surface hangs off the ship's camera. It is now left alone (and
  switched back on, with a log line, if anything else turns it off).
- Fix (respawn timer): while spectating, the screen is drawn as the watched (alive) ship, so the timer thought you weren't dead and
  hid itself. It now checks your own ship.
- New: Esc while spectating opens the pause menu (the stock game refuses while your ship is wrecked). Menus draw normally while open.
- Fix: a player who joins while the host is in the menus gets READY UP straight away again; pressing it carries into level start, so
  ready players don't wait a second time. Leaving the host alone drops the check.

## 0.6.13-alpha (2026-10-06) - Steam lobby failure handling, UNTESTED build (all players must run 0.6.13-alpha; protocol 25)
From the 15:10 run on 0.6.12 (both logs): PeetzaGuest pressed HOST at 15:10:18 and Steam refused the lobby ("no connection": his Steam
client could not reach Steam's servers), so he stayed "hosting" with no lobby: the friend list showed INVITE buttons that did nothing,
he got no invite from BlownToBits (sent 15:11:40), and friends' online status loaded slowly. Not caused by 0.6.12's changes.
- Fix: when Steam can't create the lobby you are no longer left hosting: the CO-OP screen says so ("STEAM CAN'T REACH ITS SERVERS -
  CHECK STEAM ...") and shows the JOIN list again. Inviting before the lobby exists says so too.

## 0.6.12-alpha (2026-10-06) - spectating HUD, no pause in co-op, PLAY CAMPAIGN, friend list pages, UNTESTED build (all players must run 0.6.12-alpha; protocol 25)
From the 14:48 run on 0.6.11 (both logs). User: no black bars any more, but no HUD at all while spectating.
- Spectating: the HUD is drawn onto a curved surface that belongs to your ship's camera; it is now kept on the spectate camera every
  frame (like the camera itself), and the log says if anything moved it. More [SPECT] "ui:" detail (camera vs UI surface).
- Change: opening the Esc menu (or the map) no longer pauses the level when other players are in it - robots, timers and the other
  players keep going, like multiplayer.
- New: PLAY CAMPAIGN on the host's CO-OP screen (the campaign / level select).
- New: the friend list on the CO-OP screen has pages (< PREV / NEXT >) so every online friend can be reached.
- Not yet: co-op challenge mode (needs its own phase; see HANDOFF).

## 0.6.11-alpha (2026-10-06) - spectating, cryotubes far away, UNTESTED build (all players must run 0.6.11-alpha; protocol 25)
From the 14:28 run on 0.6.10 (PeetzaGuest hosting, BlownToBits joining; joiner log). User: spectating unchanged; only the last
cryotube showed for both.
- Fix: cryotubes picked up far from a joiner still didn't count there: the host's message arrived ("cryotube 5 not found") but the pod
  was switched off on the joiner (out of sight) and the lookup skipped switched-off objects. It now finds them.
- Spectating: the whole screen is now drawn as if the watched ship were yours (before, only the HUD drawing was; the game decided from
  your wrecked ship whether to draw the HUD and screen effects at all). The HUD is re-created if missing. Diagnostics: a [SPECT] "ui:"
  line every 5 s while spectating (HUD element, alphas, bars, background, VR, UI mesh and camera) - please send the log after testing.

## 0.6.10-alpha (2026-10-06) - fabricators, Shredder blades and cryotubes for joiners, READY UP before every level, upgrade points for late joiners, spectating like normal play, UNTESTED build (all players must run 0.6.10-alpha; protocol 25)
From the 04:43-05:05 runs on 0.6.9 (PeetzaGuest hosting, BlownToBits joining; joiner log). Confirmed in the log: READY UP from the
menus (04:54:55), exit flights starting on the exit path.
- Fix: auto-op fabricators (robot makers) showed nothing on joiners - no glow when switched on, no spawn countdown sparks, sound or
  spawn flash. Joiners now see and hear them like the host (robots still come from the host).
- Fix: Shredders' blades didn't spin on joiners (no blades, sparks or blade sound). They spin when the robot attacks, as on the host.
- Fix: a cryotube picked up by someone else could show nothing on a joiner (no pod effect, no "PICKED UP CRYOTUBE: RESCUED ..."). The
  host now tells everyone.
- New: READY UP before the first level too. When the host starts a campaign or loads a game with players connected, the level waits
  until every joiner has pressed READY UP (main menu CO-OP button or the CO-OP screen); after pressing, it reads WAITING FOR HOST.
- New: a player who joins while the host is on the upgrade screen gets the same upgrade points the host had to spend: READY UP opens
  the upgrade screen for the host's next level, then the level briefing (READY UP).
- Fix: spectating showed the death view (black cinematic bars, dark backdrop, faded HUD) and always the watched ship's cockpit. It now
  looks like normal play: your usual HUD layout (missiles left, weapons right, armor/energy/ammo in the middle) with the watched
  player's values, and their cockpit only if you play with the cockpit shown.
- The version on the main menu moved 40% to the right.

## 0.6.9-alpha (2026-10-05) - lockdowns and boss bar for joiners, exit tunnel, READY UP from the menus, spectating, UNTESTED build (all players must run 0.6.9-alpha; protocol 24)
From the 19:29-20:45 runs on 0.6.8 (DescMax7930 hosting, BlownToBits, JosheM and PeetzaGuest joining; host log + BlownToBits and
PeetzaGuest logs).
- Fix: lockdowns on joiners. Joiners never ran the lockdown itself (it was host-only): no LOCKDOWN / TARGETS REMAINING counter, no
  alarm, no warning, and the lockdown doors stayed open on the joiner while they were closed on the host - an invisible wall. Joiners
  now run it (doors close, counter, alarm, "LOCKDOWN PROCEDURES INITIATED!"); the counter follows the host's kills (with the kill
  sound) and the lockdown ends when the host's ends. Robots stay the host's.
- Fix: lockdown teleports put players outside the lockdown room. The spot was picked while the doors were still closing, so it could
  be on the far side of a door. Teleport, spawn and respawn spots are now never behind a door from the player they're next to.
- Fix: the boss health bar (Goliath and other bosses) and the reactor bar didn't move on joiners. Joiners now show the host's bar.
- Change: every exit door puts all players inside the exit tunnel, lined up (whoever reached the exit in front), and everyone flies
  out from their own spot; players don't collide. Fix: a player who was placed behind a wall at the exit (19:59, PeetzaGuest) pushed
  against it forever and his game never left the level; an exit flight that stops moving now finishes by itself.
- New: READY UP from the menus. A player who joins while the host is on the results/upgrade screens gets a READY UP button (main
  menu CO-OP button and the CO-OP screen); the host's next level starts once everyone is ready.
- Fix: spectating was dark or half lit depending on where you died: the level's lights and rooms were switched on around your own
  wreck. They now follow the player you're watching.
- Change: spectating shows the watched player's HUD with their live values: armor, energy, ammo, all missiles, weapon and missile
  levels, boost heat, updated 10 times a second (a joiner's own energy/ammo come from that joiner).
- The version on the main menu is 20% lower (it was cut off at the top).

## 0.6.8-alpha (2026-10-05) - flare colors, 30 s pings, UNTESTED build (all players must run 0.6.8-alpha; protocol 23)
- New: flare colors. CO-OP OPTIONS > FLARE COLOR (your own setting): ORIGINAL, RED, ORANGE, YELLOW, GREEN or PURPLE. Every player
  sees your flares in your color - the flare's two lights, its glow and its lens flare. Darker colors (red, purple) get a stronger
  light so they light the level as much as the original flare.
- Pings last 30 seconds (hologuide too).

## 0.6.7-alpha (2026-10-05) - ping tweaks, UNTESTED build (all players must run 0.6.7-alpha; protocol 22)
- Ping markers and the map centre sphere 25% larger than in 0.6.6.
- The guide wheel's CRYOTUBE slot stays CRYOTUBE. Instead, when the hologuide is out and a ping arrives (or it comes out while a
  ping is shown), it heads straight to the ping; any command picked from its wheel takes over.
- The map shows "USE FLARE TO PING".

## 0.6.6-alpha (2026-10-05) - ping markers smaller, teal, visible from anywhere, UNTESTED build (all players must run 0.6.6-alpha; protocol 22)
- Ping markers and the map's centre sphere are 75% smaller and teal.
- Ping markers show through all geometry from anywhere: Unity's occlusion culling no longer hides them behind walls, and a ping
  beyond the camera's view distance is drawn along the same line of sight inside it.

## 0.6.5-alpha (2026-10-05) - spectating with the full HUD, map pings + hologuide to a ping, sleeping robots wake for joiners, VR launchers, UNTESTED build (all players must run 0.6.5-alpha; protocol 22)
From the 16:42-16:52 run on 0.6.4 (PeetzaGuest hosting a saved Titan level, BlownToBits joining; both logs): joiner pickups by
touch worked (16 of 16 counted), spectating started and showed the readout; no button was shot in this run.
- Spectating looks like playing: the followed player's cockpit and the normal HUD with their armor, energy, ammo, weapons and
  reticle, plus "SPECTATING <name>". (The HUD was left behind with your wrecked ship and destroyed by the death sequence.)
- New: map pings. In the map a small white sphere marks the centre of the view; the map-marker key (FIRE FLARE) pings that spot
  for everyone: "<NAME> PINGED", a sound and a white marker visible through walls for 15 s, in the level and on the map.
- New: hologuide to a ping. For a minute after a ping the guide wheel's CRYOTUBE slot reads PING and leads you there.
- Fix: claws and other sleeping robots ignored joiners. Gunfire wakes sleeping robots near the shooter - but the game always used
  the host's position, also for joiners' shots. A joiner's shots now wake robots near the joiner.
- Fix: items, doors and props (destructible buttons) were switched on only around the host and only re-checked when the host moved;
  now around every player and re-checked when anyone moves. This is likely the cause of the README known issue "pickups don't
  always show up for everyone", and of buttons that a joiner far from the host couldn't break.
- Robots pick their target with the same line-of-sight test they use to see a player.
- VR: two launchers, "olcoop SteamVR" (-vrmode openvr) and "olcoop Oculus" (-vrmode oculus), replace "olcoop VR".

## 0.6.4-alpha (2026-10-05) - spectator cockpit + readout, shot diagnostics, UNTESTED build (all players must run 0.6.4-alpha; protocol 21)
Includes the untested 0.6.3 changes.
- New: when spectating (Spectate mode or waiting to respawn) you see the followed player's cockpit from their seat (it used to
  hide their whole ship), and their armor, energy, ammo, weapon and missile count at the bottom of the screen. The host sends every
  player's values to the joiners 4 times a second (a joiner's own copy of the host's energy and ammo isn't kept up to date).
- Diagnostics: in the 15:53 run no shot reached any button's damage code, not even the host's own. The logs now name what each
  player shot hits when it is a button/destroyable (with its health and whether it is shielded), a force field or a shield, and
  when an upgraded shot passes through a button without damaging it.

## 0.6.3-alpha (2026-10-05) - map controls, joiner pickups and buttons, Esc-menu session list, shared weapons, UNTESTED build (all players must run 0.6.3-alpha; protocol 20)
From the 15:51-16:01 run on 0.6.2 (BlownToBits hosting a saved Titan level, PeetzaGuest joining; both logs). Confirmed by the user:
hologuide, starting armor/energy, session list. Joiner ammo pickups worked (log: 200 -> 250).
- Fix: the ship flew and fired while the map was open (co-op doesn't pause the game; the map keys also steered the ship).
  Controls and weapons are held while your map is open.
- Fix: keys, upgrade points and audio logs sometimes didn't count when a joiner flew through them. The joiner's game hides the item
  and plays the sound right away, but the real pickup only happened if the host's copy of the joiner's ship touched it too. The
  joiner now tells the host what it touched; the host checks and gives it (or the item shows up again if it can't be taken).
- Fix: some shoot-to-open buttons couldn't be broken by a joiner. With upgraded weapons a joiner's own shots never registered a
  button hit (only the host's copy of the shot could). The joiner now reports those hits like its other button hits.
- New: the session list (players, state, ping) is in the Esc menu too.
- Change: weapons are shared. When anyone picks up a new weapon, every player gets it. Armor, energy, ammo and missiles are not shared.

## 0.6.2-alpha (2026-10-05) - session list, robot prediction, 120 armor/energy cap, UNTESTED build (all players must run 0.6.2-alpha; protocol 19)
From the 06:58-07:17 run on 0.6.1 (BlownToBits hosting a saved game, PeetzaGuest joining; both logs). Host pickup sounds confirmed
by the user ("appears to be working now").
- New: session list. The CO-OP screen and the F8 window show everyone in the session with pilot name, Steam name, what they are
  doing (in menus, in the level, dead, on the results screen, ready) and their ping. The host sends it to joiners every second.
- New: ping measurement in both directions (shown in the list and logged every 30 s).
- Change: robots on joiners are drawn where they are on the host now (predicted from their speed, one round trip ahead, at most
  150 ms) instead of 0.1 s behind the host plus the network delay. Corrections fade out instead of jumping. `-cooprobots interp`
  brings back the old display, `-cooprobotlead <ms>` changes the limit.
- Fix: the host had up to 200 armor and energy (single-player maximum, carried in from its saves) while refills stopped at 120
  (multiplayer maximum, switched on when a joiner's ship appears). Co-op now uses the multiplayer maximum, 120, for everyone
  (user decision); higher values are lowered at level start and after loading a save.
- Fix: changing the death mode in the F8 window turned friendly fire off on joiners (the setting wasn't sent along).

## 0.6.1-alpha (2026-10-04) - fixes from the first long internet session, UNTESTED build (all players must run 0.6.1-alpha; protocol 18)
From the 19:20-20:53 run (California - Wisconsin, JosheM hosting) and the 21:03-21:12 run (PeetzaGuest hosting):
- Fix: closing the map could freeze the game. When the map opens, the game collects the map markers of keys, audio logs,
  switches and doors, and turns them off again when it closes. In co-op another player can pick one up while your map is open
  (19:34: the host took the security key 4 s after the joiner opened the map); turning off the removed marker failed every frame,
  so the map never finished closing. Removed markers are now skipped.
- Fix: the host had no pickup sound (or flash) for most items. In multiplayer the server announces a pickup to the other players
  and expects the picker's own game to have played it already, which never happens on the host. The host now plays its own.
- Fix: joiners got no energy or ammo from pickups (and energy centers didn't recharge them). olmod only sends those to players
  that announced themselves in the multiplayer lobby, which co-op skips, so nothing was sent. The host now treats every co-op
  joiner as such a player. Missiles still come through olcoop's own message (tested in 0.4.14), not twice.
- Fix/change: upgrade points are shared. Whoever picks up an upgrade point, every player gets it ("(TEAMMATE)" on the others).
  A joiner's own pickup used to show its message on the host's screen; joiners now get their own message and sound.
- Fix: joiners lost their unspent upgrade points and bought ship upgrades (energy use, ammo capacity, ...) at every level change.
  Both now carry over with the rest of the joiner's loadout (this is the protocol change).
- Friendly fire does half damage (CO-OP OPTIONS, FRIENDLY FIRE on).
- Fix: the hologuide on a joiner couldn't lead to security keys ("next objective"), and didn't notice a key had been taken. The
  guide only searches the items that existed at level start; a joiner's items all arrive later over the network (and on the host,
  items restored from a save are new too). Every co-op item is now added to the guide's list, and removed when it goes away.
- New: VR launcher. install.bat adds an "olcoop VR" shortcut (olcoop-vr.bat: olcoop.bat + -vrmode openvr).
- Repo: README source-layout table restored; tests/online-test.md lists the real log lines; tools/decompile builds ILSpy's
  decompiler engine from source (NuGet is blocked in the cloud workspace) to decompile the game and olmod into refs/.

## 0.6.0-alpha (2026-10-04) - first public release, UNTESTED build (all players must run 0.6.0-alpha; protocol 17)
Same gameplay code as 0.5.10, which passed the 17:59-18:13 run (two PCs over Steam; see 0.5.10 below). Release cleanup only:
- Removed the temporary security-key and item diagnostics (they found the saved-game and joiner-visibility causes).
- Source files named by feature instead of development phase (see README "Source layout"); no compiler warnings.
- Repository: installer files in installer/, player zip built with package.sh (dist/ is no longer committed), LICENSE (MIT).
- README: release download, current known issues (power-up and destructible issues fixed in 0.5.9/0.5.8 and confirmed).

## 0.5.10-online (2026-10-04), 17:59-18:13 run: saved-game keys/audio logs restored, joiner sees items, joiner breaks buttons on a save, invite opens the Steam friends list (user: "looks real good") (all players must run 0.5.10; protocol 17)
- Fix: INVITE THROUGH STEAM did nothing. It asked Steam for its in-game overlay, which isn't attached when the game is started
  through olmod (and isn't visible in VR). Now it opens the overlay invite if the overlay is there; otherwise it opens your Steam
  friends list on the desktop, where right-click a friend > Invite to Game invites them to your co-op game. The INVITE buttons
  next to each friend on the CO-OP screen invite directly and don't need the overlay.
- Includes 0.5.9 (items visible on joiners) and 0.5.8 (saved games), both untested.

## 0.5.9-online (2026-10-04), not tested before 0.5.10 (all players must run 0.5.9; protocol 17)
- Fix: joiners could not see security keys, audio logs and some power-ups (they could still pick them up). The game switches
  many items on only after the level has started (when a player gets close); the network layer only sends a joining player the
  items that are already switched on at that moment, so those items never appeared for joiners (17:33-17:46 runs, fresh Ymir
  and Tarvos). The host now sends each item to the joiners as soon as its game switches it on. [ITEM] "sent ... to joiners" lines.
- Includes 0.5.8 (saved games: missing keys/audio logs re-created by the host), which the 17:33 run did not test (it ran 0.5.7).
- Known issues: the destructible-button problem only showed on a saved game (fresh level: the joiner could break it).

## 0.5.8-online (2026-10-04), not tested before 0.5.9 (all players must run 0.5.8; protocol 17)
- Fix (second attempt): security keys and audio logs missing when the host continues a saved game (17:25 run on 0.5.7: still
  missing; the 0.5.7 change never triggered, so its explanation was wrong). After a saved game is loaded, the host now checks
  every item the save lists and re-creates any that are missing, spawned over the network so they stay and every player gets
  them (same way as robot drops). [ITEM] log lines list what the save contains and what was re-created.
- Diagnostics: the exact code that removes a key or audio log during those first 10 s is logged with its call stack.

## 0.5.7-online (2026-10-04), 17:25 run: did not fix it (fix never triggered) (all players must run 0.5.7; protocol 17)
- Fix: security keys, audio logs and power-ups were missing for everyone when the host continued a saved game (17:01 and 17:16
  runs, Tarvos Outpost from a save; in single player the same save had the key). Loading a save makes the game remove the
  level's items and put back the ones the save still has; olmod deletes such re-created items in multiplayer games because a
  multiplayer server normally spawns all items itself. The host now spawns the restored items over the network, so they stay
  and the other players get them too. New games were not affected (Ymir 15:07 run: first key picked up normally).
- [ITEM] log lines: items restored from a save and network-spawned by the host.
- Still open: a joiner could not break a button to open a door in the 17:01 run (the host received no hit from the joiner);
  needs the joiner's log.

## 0.5.6-online (2026-10-04), 16:59-17:17 runs: key trace found the cause (keys deleted after loading a save); joiner button not breakable (all players must run 0.5.6; protocol 17)
- HUD: co-op now shows the single-player score block (top right) instead of olmod's multiplayer PvP scoreboard: DESTROYED
  on normal levels, OPERATORS remaining on kill-objective levels (Ymir Outpost), with the team's numbers from 0.5.5.
- Security key investigation (Ymir Outpost, 15:07 run): the first key was picked up (team security level 1 at 15:13:29); the
  second key's pickup never happened. All 17 campaign levels were checked from the level files: every security key is placed
  the same way as every other pickup, and nothing in the level data is different for Ymir (both its keys sit behind secret
  walls). This build logs every key on host and joiners ([KEY] lines: where it is, whether it is active, nearby doors, when
  it disappears and why, who touches it, who picks it up, secret walls opened), so the next run shows what happens to it.
- tools/levelscan.py: the level-file key scan, for re-checking levels.

## 0.5.5-online (2026-10-04), not tested before 0.5.6 (all players must run 0.5.5; protocol 17)
- Level objective counters for joiners: the HUD counter of levels with a kill objective (OPERATORS remaining, e.g. Ymir
  Outpost) and the CORES REMAINING counter now show the team's progress on every player. They counted only the robots/cores
  destroyed in each player's own game, and in co-op those are destroyed in the host's game, so joiners' counters never moved.
  The host now sends its counts; joiners' own end-of-level stats are unchanged. Objective popups (e.g. "5 AUTONOMOUS-OPERATORS
  REMAINING") were already shared, and the boss/reactor escape countdown already reached joiners (0.4.6/0.4.10 logs).
- Installer: creates an "olcoop" shortcut with an orange and black olcoop icon (olmod style) on the Desktop and in the
  Overload folder; it starts olcoop.bat. Uninstall removes it. (A .bat file can't have its own icon in Windows.)
- README: known issues (some power-ups not showing for all players, destructibles not synchronized) and credits (the
  olmod team at the Overload Development Community, then Blowntobytes), on top of the GitHub README edit (e28d136).
- Installer files (install.bat, uninstall.bat, find-overload.ps1, README.txt, olcoop.ico) are now tracked in dist/.

## 0.5.4-online (2026-10-04), not tested before 0.5.5 (all players must run 0.5.4)
- Boost and thruster flames on other players' ships: the game only draws another ship's thrusters (and makes them bigger when it
  boosts) for ships marked as remote players, a mark the multiplayer spawn sets and co-op ships never got. The boost state was
  already arriving (14:18 logs); now other ships are marked, so their thrusters and boost flames are drawn. This is the same
  code path that shows boosts in normal multiplayer.
- Leaving shows up at once for the other player: the goodbye was thrown away because the Steam connection was closed in the
  same moment (14:20 run: the host noticed a LEAVE SESSION 14 s later). Connections now close 2 s after the goodbye.
- Fix: rejoining right after leaving dropped the new connection at once ("no packets for 20 s" from the old session).

## 0.5.3-online (2026-10-04), 14:16 run: LEAVE SESSION / STOP HOSTING work (slow to show); boost state flows both ways but no flames (all players must run 0.5.3)
- Fix: LEAVE SESSION / STOP HOSTING did nothing when clicked (14:07 run: logged as chosen 3-9 times each). 0.5.2 passed the click
  on to QUIT TO MAIN MENU, but the game re-selects the entry under the mouse in the same frame, so nothing happened. The entry
  now ends the session itself: back to the main menu, the other players are told, the Steam lobby is left.
- Fix: a dead player whose host left went through the game's own death sequence after leaving the level (14:08 run:
  PlayerHasDied -> death screen). The ship is now cleared first, and the level is left while co-op is still active.

## 0.5.2-online (2026-10-04), 14:04 run: Esc entries shown but did nothing; host's boost reached the joiner (log) (all players must run 0.5.2)
- Esc menu: LEAVE SESSION (joiner) / STOP HOSTING (host) under QUIT TO MAIN MENU. Quitting to the main menu now ends the
  co-op session too: before, a joiner was reconnected to the host straight away and the host kept hosting.
- Disconnects no longer break the other players' games: a Steam connection that is closed sends a goodbye first, so the other
  side reacts at once; a joiner whose host is gone goes to the MAIN menu (the game's own handler sent it to the multiplayer
  menu with the campaign level half torn down), for Steam and LAN joins.
- Boost: 0.5.1 never reported a boost from anyone (13:33/13:44 logs). The owner's boost is now taken from every physics step
  since the last frame and from the boost button (same conditions as the game), and sent reliably. [BOOST] "my boost" lines
  log the first changes with the button/unlock/overheat state.
- CO-OP screen: INVITE WITH THE STEAM OVERLAY renamed INVITE THROUGH STEAM.
- Installer: finds Overload on any drive (Steam's library list from the registry, then the usual Steam folders on every
  drive; prefers a copy with olmod installed), and only asks for the folder if none is found (find-overload.ps1).

## 0.5.1-online (2026-10-04), 13:33-13:47 runs: no crash; boost still not visible; quitting to the menu kept players in the session; disconnects broke the other game (protocol 16: all players must run 0.5.1)
- Disconnects: a player whose game closes, crashes or loses the connection is removed for everyone. Over Steam every player
  sends a small "still here" signal each second; 20 s of silence, or a goodbye when someone quits or leaves co-op, removes that
  player (the host removes the ship for all; joiners whose host is gone return to the main menu with THE HOST LEFT THE GAME).
- Crash fix: the game's own Steam code also ran Steam callbacks from a background timer thread, which crashed in
  SteamAPI_RunCallbacks when Steam was already shut down (12:51 crash report). That timer is skipped; Steam's per-frame update
  on the main thread already does the work. No Steam call is made once Steam starts shutting down.
- Boost: every player's own boost state is sent to the host and on to everyone; other players' ships show the owner's boost
  (flames and sound) instead of the host's simulated guess.
- Repository renamed to https://github.com/Blowntobytes/olcoop: README rewritten for the current state, publish script and
  bundle (publish/olcoop-latest.bundle) renamed.

## 0.5.0-online (2026-10-04), 12:31-13:06 runs: first internet game over Steam (direct route, no relay); host's game stopped at 12:50; a crash in SteamAPI_RunCallbacks at 12:51 (protocol 15: all players must run 0.5.0; new build folder build-online)
- Play with friends over the internet through Steam, no port forwarding: the game's network connection to a Steam friend
  travels over Steam's peer-to-peer/relay network instead of a UDP port.
- One launcher: olcoop.bat. Main menu, bottom right: CO-OP: HOST / JOIN. The host clicks HOST A CO-OP GAME (creates a
  friends-only Steam lobby) and invites friends from the list (or the Steam overlay); a friend accepts the Steam invite or picks
  the hosting friend on their CO-OP screen. olcoop-host.bat / olcoop-join.bat still work for same-PC testing.
- Between levels the story scenes (prologue, briefings, intros, entity briefings, debrief) are skipped in co-op:
  results (stats) -> upgrades -> level briefing -> play.
- Joiners' PLAY button on the level briefing reads READY UP.
- Headlights: every player's headlight state is now synced to all copies of their ship (11:34 run: spectators' copies of the live
  ship had headlights off, so the level looked dark). The stock toggle message is ignored in co-op; respawns keep the state.
- Friend package: dist/olcoop-0.5.0-online.zip (mod DLL, olcoop.bat, install.bat, uninstall.bat, README; no game or olmod files).

## 0.4.20-world (2026-10-04), 11:32 run: spectate lighting not fixed (followed ship's headlights off on the spectator's copy)
- Spectating: the ship you follow now lights the level like your own ship would (its lights render at full quality instead of
  competing for the few per-pixel light slots), and leftover death screen effects are cleared. The followed ship's light state is
  logged so the cause can be confirmed if it's still dark.

## 0.4.19-world (2026-10-04), 11:05 run: dead joiner revived into the exit (log); ready check held the host until 2/2 (log); spectators dark, no headlights
- Dead players are put into the exit: when anyone exits while a player (joiner or host) is dead or dying, the host revives that player
  in the exit line-up behind the exiting player and starts its exit flight, so everyone gets the cutscene and the end-of-level screens.
  If a revive doesn't happen within 8 s (host) / 3 s (joiner), the old path (finish the level from the death screen) is the fallback.
- Fix: a host who was dead when a joiner exited (10:45 run, hardcore) finished the level without the exit flight and its menus froze.
  The host is now revived into the exit; on the fallback path its death pause is cleared so the menus respond.
- An exit cancels a pending hardcore / team-wipe restart (10:45 run: a death 4 s before the exit had scheduled a restart).
- Ready check: the host's next level doesn't start until every connected joiner has finished its end-of-level screens. The host sees
  "WAITING FOR PLAYERS - n OF m READY". A joiner that disconnects no longer counts.

## 0.4.18-world (2026-10-04), 10:31-10:48 runs: living exits OK; host dead at a joiner's exit froze the host (fixed in 0.4.19)
- Fix: a joiner who was dead when the team exited reached the results screen, but the screen didn't respond (game "broken"). Overload
  stops handling its menus while the death pause from dying is still on, and that pause normally only ends on respawn. The dead joiner
  now leaves the death state like the game does when leaving its own death menu (death pause off, camera restored, respawn countdown
  and fade cleared) before the end-of-level screens, and the pause is kept off while on those screens.

## 0.4.17-world (2026-10-04), 10:15 run: dead joiner now goes to the end-of-level screens but they froze; living joiner OK
- Fix: a joiner who was dead when the team exited got no end-of-level screens and stayed spectating in the old level. It now stops
  spectating and finishes the level like everyone else (results, upgrades, then the host's next level).
- Fix: a joiner whose end-of-level menus ended up somewhere else (10:05 run: the main menu) never loaded the host's level - it was
  being held back "until the end-of-level screens are done". The host's level is now only held back while the joiner is actually on
  an end-of-level screen (results, stats, upgrades, save, briefing); anywhere else it loads at once.

## 0.4.16-world (2026-10-04), 09:56 run: NotReady handled, level hand-off arrived first time on the dead joiner; dead joiner had no screens; one joiner stuck in the main menu
- Fix: after the end-of-level screens, joiners stayed on "WAITING FOR THE HOST'S NEXT LEVEL" when the host started the next level.
  The host's level hand-off starts with a "not ready" network message that Overload clients have no handler for, and the network
  layer then throws away the rest of that batch - which held the level itself. Inside the old level the joiner asked again every
  3 s and got it the second time; in the menus that retry didn't run. Joiners now accept the "not ready" message (so the batch
  arrives), and the waiting screen runs the retry as well. (This also removes the dropped first level message seen in Hardcore
  restarts back in 0.3.9/0.3.10.)

## 0.4.15-world (2026-10-04), 09:44 run: joiner end-of-level screens and upgrades worked; joiners stuck on "waiting" after the host's next level started
- New: joiners get the end-of-level screens like the host (results/stats, upgrade menu, briefing). The joiner's game now finishes the
  level normally; when its menus reach "start next level" it does not load a level of its own. It shows "WAITING FOR THE HOST'S NEXT
  LEVEL" and loads the host's level. If the host's level is ready while a joiner is still in its menus, it waits until that joiner is
  done instead of pulling them out. Upgrades bought there go with the joiner into the next level (loadout carry-over now runs after the
  upgrade screen) and to the host.
- (A joiner who is dead/spectating when the team exits still skips the screens and waits in the level, as before.)

## 0.4.14-world (2026-10-04), 08:24-08:49 runs: Devastator fixed (reaches joiner, selectable, fires); melee hits joiners; 0 shots dropped
- Fix: a Devastator (or any missile) picked up by a joiner could not be selected. The pickup ran on the host, and the unlock reached the
  joiner, but the missile ammo never did (08:09 run: Devastator unlocked with 0 rounds on the joiner; the game won't select a missile
  with no ammo). The host now sends every missile pickup straight to that joiner (message 174).
- Fix: the weapon carry-over replaced the new level's loadout and could take away a weapon the new level gives you (08:10 run: the
  Flak). It now merges: unlocked weapons/missiles/upgrades are the best of both; ammo, missile counts, energy and the selected weapon
  come from the previous level.
- Protocol 13.

## 0.4.13-world (2026-10-04), 08:01 run: no joiner shots dropped any more (0, was 278); carry-over worked (log); Devastator unselectable on a joiner (0 ammo)
- Fix: the host dropped many joiner shots, missiles included (07:47 run: 278 dropped - impulse, missile pod, hunter, creeper, falcon -
  "Fire packet dropped, client is bursting"). olmod rate-checks each client shot against the multiplayer match clock, and that clock
  never runs in a campaign level, so shots looked like they all came at once. The clock now runs in co-op.
- Exit: player ships pass through each other during the exit/teleport sequence, so a joiner going through the exit first no longer
  clogs the pipe.
- Joiner weapons carry over to the next level: the loadout at the end of the exit flight (weapons, upgrade levels, ammo, missiles,
  energy, ship upgrades, selected weapon/missile) is restored when the next level's ship starts. Before, each joiner started every
  co-op level with that level's default loadout. (This game session only; not saved to disk.)
- Devastator report: logging added to find it next run - joiner logs its own weapon/missile state whenever it changes; host logs each
  joiner missile unlock, missile ammo pickup, weapon unlock and missile selection.

## 0.4.12-world (2026-10-04), 07:47 run: loadout + upgrades applied (log); exit lined up but clogged with a joiner first; joiner shots dropped by olmod; Devastator not selectable on a joiner
- Fix: other players never saw a joiner's boost, and a joiner's boost was corrected back to normal speed. The host simulates every
  ship, and the game only lets a ship boost when its pilot has the boost upgrade (Player.m_unlock_boost). The host's copy of a joiner
  had no upgrades, so it never boosted and never sent the "boosting" state that shows the boost effect on other screens. The joiner's
  loadout message now includes its ship upgrades (boost, boost speed, boost heatsink, headlight, red headlight, sticky flares, free
  accessories, self-damage reduction, item duration, smash damage, fast forward, blast damage).
- Protocol 12 (loadout message carries the upgrades).

## 0.4.11-world (2026-10-04), replaced by 0.4.12 before testing
- Fix: the Goliath boss lockdown started the moment a joiner joined (07:13 run: a joiner spawned inside the boss room's trigger). Spawns,
  respawns and lockdown teleports no longer place a ship within 2.5 units of a level trigger (script/lockdown triggers, warpers, wind
  tunnels, exit doors, alien warps), so events start only when someone flies in.
- Exit regroup in narrow exit tunnels: the other players are lined up behind the player who reached the exit (4-20 units back along the
  way they came, each on its own spot) before the exit flight starts. If that fails, the old spots next to them are tried, then
  "exit from where you are".
- Fix: melee robots (claw/blade "Shredder", detonators, chargers) only hurt the host. The game always applied melee damage to the local
  ship, which on the host is the host. It now goes to the ship the robot actually hit.
- Fix: joiner ammo weapons and missiles did nothing. The host's copy of a joiner started with an empty loadout (no ammo, no missiles, no
  upgrades), so the host refused those shots. A joiner now sends its loadout (weapons, upgrade levels, ammo, missiles, energy) to the
  host when its ship spawns; after that the game's own ammo/energy sync keeps both sides in step.
- Protocol 11 (message 170 = joiner loadout).

## 0.4.10-world (2026-10-04), 3-player run 07:13 (working pilots): spawns apart, all ships fly, all three exit; lockdown started on join
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
