# Co-op options + death modes test (build 0.3.6)

## The CO-OP OPTIONS screen
- **Main menu → OPTIONS → CO-OP OPTIONS** (directly below MULTIPLAYER OPTIONS), or in a level **Esc → OPTIONS → CO-OP OPTIONS**.
- Three clickable choices: **RESPAWN**, **SPECTATE**, **HARDCORE**. The ticked one is active. Highlighting one (mouse over, or arrow keys/pad)
  shows its description in the bar at the bottom.
- **RESPAWN COOLDOWN** 3-60 s (left/right arrows, 5 s steps). It's greyed out unless RESPAWN is selected.
- The host's choice is saved (`Overload\olcoop-settings.txt`) and sent to joiners right away. Joiners see the host's choice greyed out
  ("SET BY THE HOST").
- **FRIENDLY FIRE** checkbox (default off). Hover over it for its description.
- When a level starts, everyone sees a HUD line: "CO-OP DEATH MODE: …".
- Check that the OPTIONS list still looks right: the CONTROL … TOBII items and BACK, with nothing overlapping.

## Rules
| Mode | One player dies | Everyone dead at once |
|---|---|---|
| Spectate | Watches living teammates (FIRE switches who). Back at the start of the next level | Level restarts for everyone |
| Respawn | "RESPAWNING IN n" countdown, then reappears next to a living teammate with full armor/energy and their weapons | Level restarts for everyone (also if the last living player dies during a countdown) |
| Hardcore | Level restarts for everyone about 4 s later | Same |

## Tests (host + 1 joiner is enough; let robots kill you, it's the quickest way)
1. **Respawn, 10 s:** set it in CO-OP OPTIONS on the host before starting. Get the **joiner** killed. Expect a countdown on the joiner, then it
   respawns next to the host. Repeat by getting the **host** killed: the host should respawn next to the joiner.
2. **Respawn, wipe:** get the joiner killed, then get the host killed during the countdown. Expect "TEAM WIPED - RESTARTING LEVEL", and
   **both** windows reload the level and keep playing together.
3. **Spectate:** switch in Esc → OPTIONS → CO-OP OPTIONS (it applies right away). Get the joiner killed. Expect its camera to follow the host and FIRE to switch players
   (with only 2 players it stays on the host). The robots ignore the dead ship.
4. **Hardcore:** switch the same way. Anyone dies, and both windows restart the level.

5. **Friendly fire:** with it OFF, shoot your teammate and ram them: no damage. Turn it ON (Esc → OPTIONS → CO-OP OPTIONS): shots and
   missiles now hurt them. Your own shots never hit you.
6. **After a respawn:** the screen brightness and headlights should be normal, and you should bump into walls and robots again.

## New in 0.3.6
7. **Respawn countdown:** RESPAWN mode, get killed. After the death explosion, a big yellow number counts down in the middle of the screen,
   like in a PVP match. Then "RESPAWNING NEXT TO A TEAMMATE..." and you respawn.
8. **Player names:** each player sees their teammate's pilot name above the green health bar. While spectating, the message says
   "SPECTATING <name>".
9. **SHOW PLAYER NAMES:** untick it in Esc -> OPTIONS -> CO-OP OPTIONS (works on the joiner too). The names go away and the health bars stay.
   Tick it again and they come back.
10. **Rejoin:** on the joiner, Esc -> quit to the main menu (don't close the game). Within about 10 s it should say "CO-OP: REJOINING THE
    HOST'S LEVEL" and load back into the host's level next to the host.

## Known gaps
- If the **host** is spectating, a joiner flying into the exit doesn't finish the level yet (the level exit still follows the host's ship).
- The joiner gets the level's default weapons on a level restart (no carry-over yet).
- Joiner ammo weapons and missiles still don't damage robots (loadout sync is next).
