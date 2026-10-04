# Phase 2b world sync test (build 0.4.5)

Host + 1 joiner, a level with keys, a shootable button that opens a door, and comm (voice) messages, e.g. the first Cronus levels.
Both windows must show OLCOOP 0.4.5-WORLD at the top-left of the main menu.

1. **Keys:** the host picks up a security key. Both get "SECURITY ACCESS GRANTED!" (the joiner's says "BY A TEAMMATE"), and the joiner
   can open that key's door. Repeat with the joiner picking up a key.
2. **Destroyable button:** the joiner shoots a button with the primary laser. It breaks on BOTH screens, and the door it controls opens on both.
   Repeat with the host shooting one.
3. **Voice/comm messages and objective text:** fly through a spot that triggers one. It plays once on each screen, at the same time.
   It must not play twice on either.
4. **Script doors/forcefields/lockdowns:** the same doors open or lock and the same forcefields drop on both screens.
5. **Late join:** start the joiner after the host has broken a button and opened some doors. The joiner sees them broken/open.
6. **Audio logs:** the joiner picks up a log entry: both hear it and see its text. Then the host picks one up.
7. **Exit together:** the joiner flies into the exit door first: both play the exit flight; the joiner shows "WAITING FOR THE HOST" until
   the host's next level loads. Next level: the host exits first. Also try an alien warp if the level has one.
8. **Map:** open the map: the other player's ship is shown on it.
9. Note anything out of sync: what, where, and which window.
