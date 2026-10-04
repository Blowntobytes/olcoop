# Phase 2a test: shared robots (build 0.2.0-phase2a)

**What's new:** the host runs all robots. The joiner sees the host's robots move, shoot and die. Robots attack the nearest
player. Only the host decides damage and deaths, so you're fighting the same robots now.

**Known gaps in this build (expected):**
- On the joiner, only the energy weapons (impulse etc.) actually hurt robots. Ammo weapons and missiles show locally but do nothing on the host yet (loadout sync is next).
- Doors, switches, pickups placed in the level, and the reactor are still separate per window (Phase 2b).
- Claw and charger robots that hit the joiner may damage the *host* instead (known bug, 2b).
- Some robot animations (charging, blade spin) may look idle on the joiner.
- Don't die. Player death in co-op isn't handled yet.

## Steps
1. Run `build-phase2a\install.bat` as administrator. It should say **0.2.0-phase2a installed and verified**.
2. **Host:** `olcoop-host.bat`, start a **new game on level 1** (not Continue, so the robot lists match exactly).
3. **Joiner:** `olcoop-join.bat`. It loads the level next to you. The HUD shows "OLCOOP 0.2.0-PHASE2A - JOINED".
4. In the **host** window, fly toward the first robots. In the **joiner** window, watch the same robots move and shoot.
5. Kill a robot with the **host**. It should explode in **both** windows, once.
6. Fly the **joiner** to a robot and shoot it with the default impulse gun. It should die in both windows.
7. Park the **joiner** near awake robots and leave the host elsewhere. The robots should attack the joiner, and its armor should drop.
8. Play for about 3-5 minutes, then quit the joiner first, then the host.

## Tell me
- Do robots look the same in both windows (same place, roughly the same movement)?
- Did kills show in both windows?
- Did robots go after the joiner?
- Anything frozen, jittery or broken.
