# olcoop handoff (state as of 2026-10-03 17:00 PT)

Read this first in a new session, then CHANGELOG.md, docs/, tests/.

## Where things are
- Repo copy (source of truth): `C:\Users\atyou\Desktop\AI crap\olmodcoop` (src, docs, tests, tools, build.sh, VERSION, lib\0Harmony.dll).
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Overload` (olmod 0.5.14, `olmod.exe -modded`). Logs: `olcoop_logs\` there
  (`olcoop-<date>-pid<pid>.log` per process, plus `unity-host.log` / `unity-join.log`).
- Decompiled game source (`refs/`) and the offline ILSpy build are NOT on the PC (game code, not redistributed). Re-decompile
  `Overload_Data\Managed\Assembly-CSharp.dll` locally if needed. Build references the game's Managed DLLs + GameMod.dll staged from the game folder.

## Build + delivery rules (user is strict about these)
- Build: `./build.sh` (Mono mcs). Version comes from `VERSION` ("0.4.9 world"). Protocol = 10 (in build.sh).
- Verify: compile tools/VerifyPatches.cs and run it against the DLL — must report 0 problems (last: 146 patches, 0 problems; run `mono vp.exe <dll> <Managed dir> <game dir>`).
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
- 0.4.5-world (phase 2b) installed 2026-10-03 21:50 (129536 bytes, SHA1 1241fafb…, verified), NOT tested. Folder build-phase2b.
  0.4.5 (src/olcoop/Phase2bFlow.cs): audio log pickups replicated (184), shared exits (185 J->H request, 186 H->J exit; joiner EscapeLevel blocked), teammates on automap (AutomapOn/Off mirrored).
  User asked about Steam invites (like BZMultiplayer): Steamworks.NET is in Assembly-CSharp-firstpass (SteamMatchmaking, GameLobbyJoinRequested_t, LobbyInvite_t). Proposal pending.
  0.4.4: W7 host sends player shots (msg 70) to other players (stock gate is IsMultiplayer). Runs 2-3 (0.4.2/0.4.3): joiner shot breaks buttons via host.
  0.4.3 = 0.4.2 + map key re-enabled (S4, stock ignores VIEW_MAP when IsMultiplayerActive) + host death tick runs in AUTOMAP/MENUS.
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

## 0.4.6 status (2026-10-03) — installed, UNTESTED
- Installed DLL SHA1 f6de7c7d9b04c2377f971f683a5d6224d0c4d7ed (verified in game folder + build-phase2b). Protocol 9. Tag v0.4.6.
- Exit regroup: host moves all ships next to the exit trigger-er, Exit msg 186 carries pose, joiner plays same Exit/Teleport sequence.
- Lockdown regroup: ScriptLockdownMaster/Boss on host -> CoopFlow.HostLockdown teleports others (>25u) next to last trigger ship (10 s) or nearest; msg 187.
- ScriptLockdownBoss removed from HostOnly (Goliath run on 0.4.5 showed it host-only; exit sync fired 22:16:57).
- CHANGELOG/BUILDS/tests updated for 0.4.6 (docs-only commit, DLL unchanged). Awaiting user tests: exit both orders, lockdown while apart, Goliath exit.

## 0.4.7 status (2026-10-03 22:52) - installed, UNTESTED
- Installed SHA1 892331e1d856ae50dc614c40c6d0f17d1925c750 (136704 bytes), verified in game folder + build-phase2b. Protocol 10. Tag v0.4.7.
- Lockdown regroup: no distance limit (user requirement: anyone anywhere goes into the locked room).
- F14: joiner skips Client.ReconcileServerPlayerState while gameplay state is EXIT (exit flight was dragged back by host corrections).
- Host status 188 (1 = DoneLevel Escaped, 2 = LoadLevel) -> CoopStatus banner on overlay slot 3 (element type 122); status 2 also starts
  CoopClient.AwaitLevel. Banner visibility during EXIT / over the fade is unverified.
- Build env in cloud: apt-get install mono-mcs mono-runtime; stage the game's Managed DLLs + GameMod.dll; `bash build.sh`.

## 0.4.8 status (2026-10-03 23:10) - installed, UNTESTED
- Installed SHA1 7253c54470c90db5bc9a8ba4eaf3d4d43ba4147c (138752 bytes), verified in game folder + build-phase2b. Protocol 10. Tag v0.4.8.
- 0.4.7 run (23:00): exit flight fixed (user), status line + next-level hand-off worked, lockdown regroup moved the joiner.
- CoopHost.FindNear = TryAround (12 close offsets) then TrySegments (BFS over portals from the anchor's segment, depth <= 4, no door
  portals, segment centre with 1.6u room). Used for joiner spawn near the host and for lockdown/exit regroup (SpotNear).

## 0.4.9 status (2026-10-03 23:25) - installed, UNTESTED
- Installed SHA1 93d77bb256f54ee5556dd47e2734584631f932df (139776 bytes), verified in game folder + build-phase2b. Protocol 10. Tag v0.4.9. 147 patches, 0 problems.
- 0.4.8 passed the user's run (spawn near host at checkpoint, lockdown, exit, next level).
- F17: joiner skips GameplayManager.ExitSequenceFrame once waiting (CoopFlow.Waiting), holds UIManager.SetScreenFade(1) and zeroes ship
  velocity. ResetForLevel clears the fade if the joiner was waiting. Check: next level must not stay black.
