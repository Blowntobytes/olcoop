# Phase 2b world sync test (build 0.4.0)

Host + 1 joiner, a level with keys, a shootable button that opens a door, and comm (voice) messages, e.g. the first Cronus levels.
Both windows must show `[INIT] olcoop 0.4.0-world`.

1. **Keys:** the host picks up a security key. Both get "SECURITY ACCESS GRANTED!" (the joiner's says "BY A TEAMMATE"), and the joiner
   can open that key's door. Repeat with the joiner picking up a key.
2. **Destroyable button:** the joiner shoots a button with the primary laser. It breaks on BOTH screens, and the door it controls opens on both.
   Repeat with the host shooting one.
3. **Voice/comm messages and objective text:** fly through a spot that triggers one. It plays once on each screen, at the same time.
   It must not play twice on either.
4. **Script doors/forcefields/lockdowns:** the same doors open or lock and the same forcefields drop on both screens.
5. **Late join:** start the joiner after the host has broken a button and opened some doors. The joiner sees them broken/open.
6. Note anything out of sync: what, where, and which window.
