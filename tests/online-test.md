# Online test (build 0.6.1-alpha)
All windows / PCs must show OLCOOP 0.6.1-ALPHA at the top-left of the main menu (0.6.0 can't join 0.6.1: protocol 18).
Steam must be running. Send BOTH PCs' `Overload\olcoop_logs` folders after the run (the host's log matters most for
pickups and upgrade points), and say which PC hosted.

## New in 0.6.1 (check these first)
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
