# Online test (build 0.6.6-alpha)
All windows / PCs must show OLCOOP 0.6.6-ALPHA at the top-left of the main menu (older versions can't join: protocol 22).
Steam must be running. Send BOTH PCs' `Overload\olcoop_logs` folders after the run, and say which PC hosted.

## New in 0.6.5 (check these first)
8. Spectating: the screen looks like playing from the followed player's seat - their cockpit and the normal HUD with THEIR armor,
   energy, ammo, weapons, reticle - plus "SPECTATING <name>" at the top. Log: [SPECT] "HUD moved to the spectate camera",
   "drawing the HUD for netId=..".
9. Map ping: open the map; a small teal sphere sits at the centre of the view. Press FIRE FLARE (the map-marker key): every player
   gets "<NAME> PINGED" and a sound, and sees a small teal marker at that spot through all walls, from anywhere in the level, (in the level and on their map) for 15 s.
   Log: [PING] "sent at ..." / "from NBOOB at ...".
10. Hologuide to a ping: within 60 s of a ping, the guide wheel's CRYOTUBE slot reads PING; choose it: "FOLLOW ME TO THE PING",
    the guide leads there and stops when you are within 8 units. Log: [PING] "hologuide: lead to ..'s ping" / "reached the ping".
11. Claws and other sleeping robots: a joiner flies (and shoots) near lurking claws while the host is elsewhere / sitting still:
    they wake up and attack the joiner. Host log: [RSYNC] "host: joiner netId=.. fired; waking sleeping robots near it",
    "host: a joiner changed segment; re-checking which robots/items/doors/props are active".
12. Items and buttons far from the host: the joiner explores alone while the host waits: items appear for the joiner and buttons
    break when the joiner shoots them.
13. VR launchers: install.bat makes "olcoop SteamVR" and "olcoop Oculus" shortcuts (the old "olcoop VR" one is removed); each starts
    the game in that VR runtime.

## New in 0.6.4
6. Spectating (Spectate mode, and while waiting to respawn in Respawn mode): you see the followed player's cockpit from their seat,
   and at the bottom: SPECTATING <name>, ARMOR / ENERGY / AMMO, weapon and missile with count. FIRE switches player. Check both
   ways (host spectating a joiner, joiner spectating the host): the numbers change as they fire and get hit.
   Log: [SPECT] "drawing spectator readout for netId=..".
7. Buttons: shoot a shoot-to-open button (host and joiner) and note which one. Logs show what each shot hit: [WORLD] "shot X by
   netId=.. hit destroyable '...' hp=.." (or "INVULNERABLE (shielded)", or a force field/shield), and "upgraded X passed through
   destroyable" if the shot went through it without damage.

## New in 0.6.3 (untested; check these too)
1. Map: open the map (host and joiner) while holding thrust/fire keys: the ship doesn't move or fire while the map is open; after
   closing it, controls work normally. Log: [FLOW] "automap open: ship controls and weapons held".
2. Joiner pickups: the joiner flies through security keys, upgrade points, audio logs and powerups (also on a saved game): each one
   counts. Joiner log: [ITEM] "joiner: touched X netId=.. -> host"; host log: [ITEM] "host: conn 20 touched X ...: picked up"
   (or the reason it wasn't, then the item shows again for the joiner: "shown again").
3. Joiner buttons: the joiner shoots shoot-to-open buttons with an upgraded weapon (impulse/cyclone level 1+): they break and the
   door opens for everyone. Joiner log: [WORLD] "joiner: hit destroyable .. -> host"; host log: "host: joiner conn 20 hit destroyable".
   Note which button, if one still doesn't break.
4. Esc menu: the left side shows IN THIS SESSION with each player's name, state and ping (host and joiner).
5. Weapons for everyone: one player picks up a new weapon: every player gets it (each gets the "weapon unlocked" message);
   armor/energy/ammo/missile pickups stay with the one who took them. Host log: [ITEM] "host: X picked up by ..; unlocked for N".

## From 0.6.2 (confirmed: session list, starting armor/energy; still to check: robots at long distance, reactor sound)
A. Session list: host and joiner open CO-OP (main menu): "IN THIS SESSION (n/4)" lists every player - pilot name (Steam name),
   IN MENUS / IN LEVEL / DEAD / LEVEL RESULTS / READY / CONNECTING, ping in ms. Same list in the F8 window (also in a level).
   Host log: [LOBBY] "n/4: ..." on every change and "host: round trip conn 20=.. ms" every 30 s; joiner: "round trip to host".
B. Robots on the joiner (long-distance game): they should feel closer to where the host sees them, and joiner shots should hit
   moving robots more reliably. Joiner log every 15 s: [RSYNC] "joiner robots: predict lead=.. ms (rtt ..) corrections avg .. u,
   snaps ..". A/B: add -cooprobots interp to the joiner's olcoop.bat line for the old 0.6.1 display; -cooprobotlead 80 limits
   how far ahead robots are drawn (default 150 ms). Report robots jittering, sliding into walls or popping.
C. Armor/energy cap 120 for everyone (multiplayer rules): host continuing a save that had 200 starts at 120; pickups stop at 120.
   Host log: [ITEM] "cap 120 (...)" when something was clamped.
D. F8 window: changing the death mode no longer turns friendly fire off for joiners.
E. Reactor: does the reactor explosion sound play twice on the host? (Reported once, unconfirmed; nothing changed for it.)

## Still to confirm from 0.6.1
1. Map while the host picks something up (the 19:34 freeze): joiner opens the map (M) and keeps it open while the host picks
   up a security key or audio log, or breaks a destructible switch. Joiner closes the map: it closes normally.
   Joiner log: [FLOW] "automap: closing; skipped N map marker(s) removed while the map was open".
2. Host pickup sounds: the host picks up energy, ammo, armor, a missile and a weapon: each has its pickup sound (and the screen
   flash). Host log: [ITEM] "host: pickup sound for my ..." (first 5).
3. Joiner energy and ammo: the joiner fires the Driller/Cyclone etc. until ammo is low, uses energy, then picks up an ammo box
   and an energy powerup: the HUD shows AMMO INCREASED TO / ENERGY INCREASED TO and the counters go up. Also an energy center
   recharges the joiner. Missiles: picking up a missile pack adds the right number once (not twice).
4. Upgrade points: the host picks up an upgrade point: the joiner(s) get "UPGRADE POINT ACQUIRED! (n TOTAL) (TEAMMATE)" and the
   host gets its own message. A joiner picks one up: the joiner (not the host) shows the message, and everyone gets the point.
   Host log: [ITEM] "... point given to every player", "host: netId=.. +1 upgrade point"; joiner log: [ITEM] "joiner: +1 ...".
5. Upgrades kept between levels: a joiner saves points (doesn't spend them all) and buys a ship upgrade (e.g. ammo capacity);
   next level: the unspent points and the ship upgrade are still there (upgrade menu after the next level shows them).
   Joiner log: [COMBAT] "... ship=[..] points=a/b" on "keeping loadout" and "restored loadout".
6. Friendly fire (CO-OP OPTIONS: FRIENDLY FIRE on): shots between players do half the damage they did in 0.6.0. FF off: none.
7. Hologuide on a joiner: in a level with a security key the joiner calls the guide and picks "next objective": it leads to
   the key (FOLLOW ME TO THE SECURITY KEY); after anyone picks up the key it moves on (security door / reactor / exit).
   Joiner log: [ITEM] "joiner: KEY_SECURITY added to the hologuide's item list".
8. VR launcher: install.bat from the zip creates "olcoop VR" next to "olcoop" on the Desktop; with SteamVR running it starts the
   game in the headset (olcoop-vr.bat = olcoop.bat + -vrmode openvr).

## Same PC (two or three windows, olcoop-host.bat / olcoop-join.bat as before)
9. Level end: results -> upgrades directly (no story scene in between); the joiners' briefing button says READY UP; the host's
   next level starts after every joiner pressed READY UP.
10. New campaign from the main menu (PLAY MISSION): no intro scene, straight to the upgrade/briefing screens.
11. Headlights: each player toggles headlights; the others see them. Spectate a live player with headlights on: the level is lit.
12. Main menu shows CO-OP: HOSTING / CO-OP: JOINED bottom right; the CO-OP screen opens and BACK returns.

## Two PCs (you + a friend, different Steam accounts) - friend installs dist/olcoop-0.6.1-alpha.zip
13. Both start olcoop.bat. Host: CO-OP -> HOST A CO-OP GAME, INVITE the friend; friend accepts (or picks the host -> JOIN).
14. Host: BACK -> PLAY MISSION or LOAD SAVED GAME. The friend is taken into the host's level next to the host.
15. Disconnects: LEAVE SESSION / STOP HOSTING in the Esc menu; a closed game is noticed within 20 s.
16. Saved game (LOAD SAVED GAME, e.g. Tarvos Outpost): keys, audio logs and power-ups are there for host and joiner.
    Host log: [ITEM] "host: saved items: N present, N re-created, N failed".
17. Joiner visibility: on a fresh level the joiner flies to the security keys, audio logs and power-ups: all visible.
    Host log: [ITEM] "host: sent ... to joiners (switched on after they joined)".
18. Joiner breaks a shoot-to-open button (and the host one): the door opens for everyone.
19. INVITE THROUGH STEAM (host): the Steam overlay invite or the Steam friends list opens. Host log: [STEAM] "invite button: overlay enabled=...".
