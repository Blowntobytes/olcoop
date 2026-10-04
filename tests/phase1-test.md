# Phase 1 test: two copies on one PC, both ships in the same campaign level

**Build:** olcoop 0.1.0-phase1. **What should work:** the second copy joins your campaign level and you see each other's
ships fly. **What will not work yet (expected):** each copy has its *own* robots. Robots, kills, doors and pickups are not
shared until Phase 2. Shots are not shown across copies.

## Setup (once)
Run `build-phase1\install.bat` as administrator. This replaces the Phase 0 DLL.

## Steps
1. In the Overload folder, double-click **`olcoop-host.bat`**. A 1280×720 window opens. Pick your pilot, then
   **start a new campaign game (level 1)** and wait until you're flying.
2. Double-click **`olcoop-join.bat`**. A second window opens. Just pick the pilot and wait at the main menu.
   Within about 5 seconds it should say **"CO-OP: CONNECTED"** and then load level 1 by itself.
   - The host window should briefly show "CO-OP: PLAYER JOINING".
3. Arrange the two windows side by side. Click a window to control it.
4. In the **joiner**, look around for the host ship. It should be about 5 m away at the level start. Fly a bit.
5. Click the **host** window and fly around. Watch the joiner window: the host ship should move smoothly there too.
6. Do the reverse: fly the joiner and watch it in the host window.
7. Fly both for about 2 minutes. **Try not to die** (death isn't handled in co-op yet).
8. Quit the **joiner** first (Esc → quit), then the host.

## Expected
| Check | Pass if |
|---|---|
| Join | The joiner loads level 1 with no clicks after the pilot, and you don't get bounced back to the menu. |
| Two ships | Each window shows the other player's ship. |
| Movement sync | The other ship moves smoothly and roughly matches where it really is. A little lag is fine. |
| Control | Each window only controls its own ship. |
| Stability | No freeze or crash for 2+ minutes. |

## Notes you can give me
Just tell me what you saw, for example "joined fine, host ship jittery" or "joiner stuck at menu". I'll read both
`olcoop_logs\olcoop-…-pid….log` files and `unity-host.log` / `unity-join.log` myself.

## Known limitations in this build
- Robots aren't shared: each window fights its own copy of the robots.
- The automap is disabled during co-op (an MP side effect). Ship handling uses MP tuning, which is very slightly different.
- If the joiner can't connect, it retries every 5 s for 2 minutes, then shows "COULD NOT REACH HOST".
