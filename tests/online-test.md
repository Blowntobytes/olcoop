# Online / phase 0.5.0 test (build 0.5.8-online)
All windows / PCs must show OLCOOP 0.5.8-ONLINE at the top-left of the main menu. Steam must be running.

## Same PC (two or three windows, olcoop-host.bat / olcoop-join.bat as before)
1. Level end: results -> upgrades directly (no story scene in between); the joiners' briefing button says READY UP; the host's
   next level starts after every joiner pressed READY UP.
2. New campaign from the main menu (PLAY MISSION): no intro scene, straight to the upgrade/briefing screens.
3. Headlights: each player toggles headlights; the others see them. Spectate a live player with headlights on: the level is lit.
4. Respawn mode: die and respawn with headlights on: the others still see your headlights.
5. Main menu shows CO-OP: HOSTING / CO-OP: JOINED bottom right; the CO-OP screen opens and BACK returns.

## Two PCs (you + a friend, different Steam accounts) - friend installs dist/olcoop-0.5.8-online.zip
6. Both start olcoop.bat. Host: CO-OP -> HOST A CO-OP GAME (status: HOSTING - FRIENDS CAN JOIN OR BE INVITED), INVITE the friend.
7. Friend accepts the Steam chat invite (game running) -> joins; or friend opens CO-OP and picks the host -> JOIN.
8. Host: BACK -> PLAY MISSION or LOAD SAVED GAME. The friend is taken into the host's level next to the host.
9. Play a level to the exit: results, upgrades, READY UP, next level for both.
10. Send both olcoop_logs folders (the [STEAM] lines show the route: relay=True/False).
11. Disconnect, joiner: a joiner closes the game (or LEAVE CO-OP, or pulls the network): within 20 s (at once for a normal quit)
    its ship disappears for the host and the other joiner.
12. Disconnect, host: the host closes the game: joiners return to the main menu with THE HOST LEFT THE GAME.
13. Boost: each player boosts in turn; everyone else sees the flames and hears the boost on that ship.
14. Esc menu: the joiner has LEAVE SESSION, the host STOP HOSTING (under QUIT TO MAIN MENU). Joiner leaves: its ship disappears
    for the others, the joiner is at the main menu and not reconnected. Host stops: joiners go to the MAIN menu (not multiplayer).
15. Force-close a game (Alt+F4 / Task Manager): the others carry on (joiner gone) or go to the main menu (host gone) within 20 s.
16. HUD + objective counter: top right shows SCORE/DESTROYED like single player (no PvP scoreboard). Objective counter (needs a level with a kill objective, e.g. Ymir Outpost): host and joiners kill operators; every
    player's OPERATORS counter (HUD, top) counts down together and the "N AUTONOMOUS-OPERATORS REMAINING" popups show on
    everyone. Same for CORES REMAINING on an alien-core level if you reach one. Logs: [OBJ] lines on host and joiners.
17. Installer: run install.bat from the zip: an "olcoop" shortcut with the orange/black icon appears on the Desktop and starts
    the game like olcoop.bat.
18. Security keys (Ymir Outpost = sp_outer_01, the operator level): get both keys. If a key can't be found, note roughly
    where you looked and when, then send both logs ([KEY] lines on host and joiner). Repeat on any level where a key is missing.
19. Saved game: host continues a saved game (LOAD SAVED GAME, e.g. Tarvos Outpost): the security key(s), audio logs and power-ups
    are there for the host and the joiner; both can pick them up. Host log: [ITEM] "network-spawned restored ..." lines.
20. Joiner breaks a shoot-to-open button (and the host one): the door opens for everyone. Send BOTH logs.
