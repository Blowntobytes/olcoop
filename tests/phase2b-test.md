# Phase 2b world sync test (build 0.4.10)

Host + 1 joiner, a level with keys, a shootable button that opens a door, and comm (voice) messages, e.g. the first Cronus levels.
Both windows must show OLCOOP 0.4.10-WORLD at the top-left of the main menu.

1. **Keys:** the host picks up a security key. Both get "SECURITY ACCESS GRANTED!" (the joiner's says "BY A TEAMMATE"), and the joiner
   can open that key's door. Repeat with the joiner picking up a key.
2. **Destroyable button:** the joiner shoots a button with the primary laser. It breaks on BOTH screens, and the door it controls opens on both.
   Repeat with the host shooting one.
3. **Voice/comm messages and objective text:** fly through a spot that triggers one. It plays once on each screen, at the same time.
   It must not play twice on either.
4. **Script doors/forcefields/lockdowns:** the same doors open or lock and the same forcefields drop on both screens.
5. **Late join:** start the joiner after the host has broken a button and opened some doors. The joiner sees them broken/open.
6. **Audio logs:** the joiner picks up a log entry: both hear it and see its text. Then the host picks one up.
7. **Exit together (regroup):** stay far apart. The joiner flies into the exit door first: the host's ship jumps next to the joiner and
   both play the same exit flight from that spot; the joiner shows "WAITING FOR THE HOST" until the host's next level loads.
   Next level: the host exits first, with the joiner far away: the joiner jumps next to the host. Also try an alien warp if the level has one,
   and one exit while the other player is dead.
   The joiner's exit flight must play smoothly from the start, the same length as the host's. Afterwards the joiner keeps a status line on
   screen: "WAITING FOR THE HOST", then "THE HOST IS ON THE LEVEL SUMMARY", then "THE HOST IS STARTING THE NEXT LEVEL...", and then
   the joiner loads into the next level. After its exit flight the joiner sees a steady black screen with the status line (no spinning),
   and the next level is not black.
8. **Map:** open the map: the other player's ship is shown on it.
9. **Lockdown regroup:** let one player set off a lockdown (robot ambush that seals the doors) while the other is anywhere else, near or far.
   The other player is teleported next to them, inside the locked room, with "CO-OP: LOCKDOWN - TELEPORTED TO YOUR TEAMMATE" (joiner) /
   "TEAM REGROUPED" (host). Repeat with the other player triggering it.
10. **Join next to the host:** host continues a saved game (starts at a checkpoint), then the joiner joins: the joiner appears next to
    the host, not at the level start. Then fly apart for the lockdown test.
11. **Boss level (Goliath):** the boss lockdown starts on both screens (doors seal, music), and the exit after the boss works for both.
12. **3 players:** host + 2 joiners. After joining, a level restart and the next level, both joiners can fly (not only turn) and are
    never on top of each other. Lockdown and exit with both joiners far away: both arrive next to the trigger player, apart from each
    other, and both exit flights play fully.
13. Note anything out of sync: what, where, and which window.
