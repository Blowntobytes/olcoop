# olcoop handoff (state as of 2026-10-03 17:00 PT)

Read this first in a new session, then CHANGELOG.md, docs/, tests/.

## Where things are
- Repo copy (source of truth): `C:\Users\atyou\Desktop\AI crap\olmodcoop` (src, docs, tests, tools, build.sh, VERSION, lib\0Harmony.dll).
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Overload` (olmod 0.5.14, `olmod.exe -modded`). Logs: `olcoop_logs\` there
  (`olcoop-<date>-pid<pid>.log` per process, plus `unity-host.log` / `unity-join.log`).
- Decompiled game source (`refs/`) and the offline ILSpy build are NOT on the PC (game code, not redistributed). Re-decompile
  `Overload_Data\Managed\Assembly-CSharp.dll` locally if needed. Build references the game's Managed DLLs + GameMod.dll staged from the game folder.

## Build + delivery rules (user is strict about these)
- Build: `./build.sh` (Mono mcs). Version comes from `VERSION` ("0.4.2 world"). Protocol = 7 (in build.sh).
- Verify: compile tools/VerifyPatches.cs and run it against the DLL — must report 0 problems (last: 89 patches, 0 problems).
- ONE build folder per phase: build-phase0, build-phase1, build-phase2a, build-coop-options, build-phase2b (current). Bug fixes overwrite the current phase
  folder with a bumped version. Never create per-fix folders. New phase = new folder, announced.
- Also install the DLL directly into the game folder via the device bridge and verify size/mtime (user's install.bat runs proved unreliable).
- Check the `[INIT] olcoop x.y.z` line of every log before analysing a test — twice the user tested a stale version.
- Never claim success without an in-game test; mark untested builds as untested.

## GitHub publishing (same as the user's other mods)
- Repo: github.com/Blowntobytes/olmodcoop. Cloud sessions can only push to branches named `claude/...` and cannot push tags or main.
  Do NOT push anything to GitHub from a cloud session unless the user asks; main + tags are published by the user's script.
- After each build: commit `<version>: description`, tag `v<version>`, `git bundle create olmodcoop-latest.bundle main --tags`, deliver it to
  `olmodcoop\publish\` next to Publish-ToGitHub.cmd/.ps1. The user double-clicks Publish-ToGitHub.cmd, which pushes with their own login.

## Current status
- 0.4.2-world (phase 2b) installed 2026-10-03 19:59 (121344 bytes, SHA1 e6abe880…, verified), NOT tested. Folder build-phase2b.
  Version shown on main menu (top-left) and CO-OP OPTIONS (since 0.4.1). 0.4.2: joiner button hits forwarded to host (msg 179), key sound on joiners.
  2b run 1 (0.4.0, 19:46-19:55): host-side button breaks, script doors, comm messages replicated correctly; joiner shots never hit host buttons.
  Host-authoritative scripts/destroyables/keys. User-reported targets: keys out of sync, destroyable buttons out of sync. Test: tests/phase2b-test.md.
- 0.3.10 installed (2026-10-03 16:58, 103936 bytes, SHA1 39ffbd8a…, verified byte-for-byte), NOT yet tested in game.
  PASSED in game: respawn countdown (0.3.9, overlay slot 1 / element type 121), player names + health bars (0.3.7).
  Run 7 (0.3.9) Hardcore: joiner "left" at 16:44:48 but the level never unloaded (frozen picture); host's first scene send (right after
  DestroyPlayersForConnection) was dropped again; 10-s retry loaded it; then panorama view = PlayerShip.DeathPaused (static) left true.
  0.3.10: no self-leave; CoopClient.AwaitLevel/AwaitTick (hello every 3 s after C5); host delays scene 1.5 s after clearing players
  (s_not_before); C1 ignores duplicate scene <6 s; S3 clears DeathPaused/m_pregame/bars/fade on every co-op OnStartLocalPlayer.
- DELIVERY GOTCHA (hit again 16:58): device_commit_files reused an earlier upload when the staged path was the same
  (outputs/rel/Mod-olcoop.dll) and wrote stale 0.3.9 bytes. Stage every release under a unique folder (outputs/rel-<version>/) and
  always read the installed file back and compare SHA1.
- Decided: host dead + spectating and the last living joiner disconnects -> level restarts (current behaviour, keep it).
- Decompiling without NuGet: `tools/ildump.py <dll> <outdir>` (pip install dnfile dncil) dumps per-type IL with resolved names into refs/ (git-ignored, never commit the output).
- 0.3.5 installed (2026-10-03 14:51). Tested 14:57-15:05 (run 3 in tests/coop-options-results.md): user says mostly working; respawn camera and host spectate camera both behaved, no camera errors.
  - 0.3.4: spectate camera drives Camera.main, re-attaches every frame, clears PlayerShip.DeathPaused, `[SPECT]` logging.
  - 0.3.5: fixes camera destroyed on respawn (rig destroyed with Camera.main still parented -> thousands of NREs in
    PlayerShip.AddCameraSway / GameManager.LateUpdate on the joiner). Camera returned to c_cam_controller before rig destroyed.
- Open bugs to check on next run:
  1. Host death in SPECTATE: host view "froze" (game kept running). Expect fixed by 0.3.4; confirm via [SPECT] lines.
  2. Joiner camera broken after RESPAWN (0.3.3). Expect fixed by 0.3.5.
  3. (Answered in run 3: the wipe came from the joiner disconnecting, not dying. Decide whether a disconnect should trigger a wipe.) Old note, 14:51 run: "level reset scheduled: TEAM WIPED" 40 s after host died in SPECTATE — verify the joiner really died.
  4. After host death almost no robots active near the joiner (robot relevance seems host-centric; remote ship seg=0 in dumps).
- Pending optimisation (agreed, not done): FriendlyFireRules.IgnoreOwner runs on every Projectile.Fire; skip when FF off
  (clear pairs once on OFF->ON) and only reset when a pooled projectile's owner changed.
- Later phases (not started): joiner loadout sync (msg 170/P15, ammo weapons/missiles don't hurt robots), doors/switches/pickups,
  objectives (reactor, exit with all players, exit while host spectates), melee damage routing, chunk union P13.
