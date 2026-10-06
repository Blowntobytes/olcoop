# olcoop handoff (state as of 2026-10-05 21:45 PT)

Read this first in a new session, then CHANGELOG.md, docs/, tests/.

## Where things are
- Repo copy (source of truth): `C:\Users\atyou\Desktop\AI crap\olmodcoop` (src, docs, tests, tools, build.sh, VERSION, lib\0Harmony.dll).
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Overload` (olmod 0.5.14, `olmod.exe -modded`). Logs: `olcoop_logs\` there
  (`olcoop-<date>-pid<pid>.log` per process, plus `unity-host.log` / `unity-join.log`).
- Decompiled game source (`refs/`) is NOT on the PC (game code, not redistributed). Regenerate it in the cloud workspace:
  `bash tools/decompile/build-and-run.sh` (apt-get install dotnet-sdk-8.0; builds ILSpy's decompiler engine v8.2 from GitHub
  source, since NuGet is blocked) -> readable C# in refs/cs-game, cs-firstpass, cs-unet, cs-olmod. `tools/ildump.py` (IL only)
  still works as a fallback. Build references the game's Managed DLLs + GameMod.dll staged from the game folder.

## Build + delivery rules (user is strict about these)
- Build: `./build.sh` (Mono mcs; mcs can't compile Harmony's AccessTools.FieldRef ref-returns - use FieldInfo). Version comes from
  `VERSION` ("0.6.9 alpha"). Protocol = 24 (in build.sh). Message ids used so far: 160-205. Current build folder: build-alpha (since 0.6.0-alpha; build-online held 0.5.x).
- Verify: compile tools/VerifyPatches.cs and run it against the DLL — must report 0 problems (last: 192 patches, 0 problems;
  it needs ALL of Overload_Data\Managed staged (e.g. UnityEngine.AnimationModule), not just the build references; run `mono vp.exe <dll> <Managed dir> <game dir>`).
- ONE build folder per phase: build-phase0, build-phase1, build-phase2a, build-coop-options, build-phase2b, build-online, build-alpha (current).
  Bug fixes overwrite the current phase folder with a bumped version. Never create per-fix folders. New phase = new folder, announced.
- Also install the DLL directly into the game folder via the device bridge and verify size/mtime (user's install.bat runs proved unreliable).
- Check the `[INIT] olcoop x.y.z` line of every log before analysing a test — twice the user tested a stale version.
- Never claim success without an in-game test; mark untested builds as untested.

## GitHub publishing (same as the user's other mods)
- Repo: github.com/Blowntobytes/olcoop (renamed from olmodcoop on 2026-10-04; the local folder is still `olmodcoop`). Cloud sessions can only push to branches named `claude/...` and cannot push tags or main.
  Do NOT push anything to GitHub from a cloud session unless the user asks; main + tags are published by the user's script.
- After each build: commit `<version>: description`, tag `v<version>`, `git bundle create publish/olcoop-latest.bundle main --tags`, deliver it to
  `olmodcoop\publish\` next to Publish-ToGitHub.cmd/.ps1. The user double-clicks Publish-ToGitHub.cmd, which pushes with their own login.
- The user also edits README.md on github.com (first: commit e28d136 "Revise README for clarity and details", 2026-10-04 15:35).
  Before a release, check GitHub main for commits that aren't in the bundle (add_repo Blowntobytes/olcoop, clone, compare) and
  fast-forward local main onto them (`git fetch <clone> main && git merge --ff-only FETCH_HEAD`) - never re-author or squash them -
  so Publish-ToGitHub.cmd fast-forwards instead of offering a force-push that would delete the user's edit.
- README.md is the user's text: keep their wording ("early alpha", "joiners (up to 2)", "pickups is per instance, not per
  player yet (WIP)", start olcoop.bat IN THE GAME FOLDER). Sections required by the user: Known issues (some power-ups not showing
  up for all players; destructibles not synchronizing between all players) and Credits - the olmod team / Overload Development
  Community (https://github.com/overload-development-community/olmod) FIRST, then Blowntobytes. Update Known issues as bugs are
  fixed or found; only the user decides wording beyond that.

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

## 0.4.10 status (2026-10-04 06:52) - installed, UNTESTED
- Installed SHA1 928e31d34c2a34fabaa223e67a7708b302730635 (141312 bytes), verified in game folder + build-phase2b. Protocol 10. Tag v0.4.10. 147 patches, 0 problems.
- 0.4.9 3-player run: joiners stacked on one spot (spawn, respawn, lockdown, exit) -> stuck / stalled exit. F17 black hold worked (joiner 1).
- CoopHost.SpawnOk now rejects spots within 2.4 u of a living ship (except CoopHost.IgnoreShip, the ship being placed) or a spot reserved
  in the last 3 s. TrySegments: same check, tight tier (1.0 u room) as fallback. CoopFlow.SpotNear returns false instead of a blind spot;
  Regroup then leaves the ship (exit msg with pos zero), HostPlaceSelfForExit leaves the host.
- Still to confirm: that the "only spin" control loss was the overlap (if it recurs without overlapping spawns, look at input/reconcile).

## A/B test (2026-10-04 07:07)
- 0.4.9 rebuilt from tag v0.4.9 installed in the game folder only (SHA1 e375d85a197de85aed85e9bd16aa5e5e75577cd7, 139776 bytes) to test the
  0.4.10 "every ship can only rotate, not move" report. build-phase2b still holds 0.4.10. Reinstall the A/B winner afterwards.
- A/B RESULT (07:10): 0.4.9 froze too, even host alone in sp_outer_02. Cause is NOT the mod: the pilot profile changed from OBSERVERB2B
  (host in every run up to 06:45) to TESTEE (06:55 on). Controls load per pilot (testee.xconfigmod; its stored device list is
  Tetherscript/3Dconnexion/Generic USB - not plugged-in hardware), so TESTEE has no working thrust bindings on this PC. The 06:33 run's
  "second joiner could only spin" was the joiner using TESTEE. 0.4.10 reinstalled (SHA1 928e31d3...). Use the same pilot (with working
  bindings) in every window; worth a launcher check that warns when a pilot has no thrust binding.

## 0.4.11 status (2026-10-04 07:34) - installed, UNTESTED
- Installed SHA1 34f2b0079740ce03a8fbaec4f21cf0113bdc1f5a (151040 bytes), verified in game folder + build-phase2b. Protocol 11. Tag v0.4.11. 153 patches, 0 problems.
- CoopHost.InTrigger: OverlapSphere 2.5 u, Collide; rejects TriggerBase/DoorExit/AlienWarp volumes (AvoidTriggers, default on).
- CoopHost.TryLane: exit regroup spots 4/7/10/13/16/20 u behind the anchor (LOS, 1.0 u room, occupancy, no trigger check).
- src/olcoop/Phase4Combat.cs: M1 transpiler on Robot.ApplyClawImpulse/ApplyDetonatorImpulse/ApplyChargerImpulse (1 ldsfld
  GameManager.m_player_ship each, the ApplyDamage receiver) -> MeleeVictim(robot) = target_rigidbody's PlayerShip. Log [COMBAT] melee.
- Loadout: msg 170 J->H sent 1 s after the joiner's OnStartLocalPlayer; host applies weapon/missile levels, picked-up flags, ammo,
  missile ammo, energy, current weapon/missile to its copy. Ammo/energy then follow stock server sync (Player.Update RpcSetAmmo/Energy).
  Log [COMBAT] joiner: sent loadout / host: applied loadout. Not covered: boost/headlight unlocks; loadout on later respawns (D9 keeps ammo).

## 0.4.12 status (2026-10-04 07:46) - installed, UNTESTED (0.4.11 replaced before testing)
- Installed SHA1 b8a488f4c515e014dff22a6885c82c4d83262f51 (152576 bytes), verified in game folder + build-phase2b. Protocol 12. Tag v0.4.12. 153 patches, 0 problems.
- Loadout msg 170 now also carries the 12 Player.m_unlock_* ship upgrades. Boost needs m_unlock_boost
  (FixedUpdateProcessControlsInternal); the host's copy only boosts, and Player.Update only sends RpcSetBoosting, with it.
  Remote boost flames: PlayerShip.Update -> UpdateThrusters (m_remote_player) scales thrusters while m_boosting.

## 0.4.13 status (2026-10-04 07:56) - installed, UNTESTED
- Installed SHA1 a933e31db7d168c026f8663374caf4f8ccd83831 (157184 bytes), verified in game folder + build-phase2b. Protocol 12. Tag v0.4.13. 160 patches, 0 problems.
- olmod SNIPER PACKETS are active in co-op (client-side shots + client-owned ammo/energy; server rate-checks shots). K1 advances
  NetworkMatch.m_match_elapsed_seconds in co-op (its burst-check clock). Earlier notes that ammo/energy are host-pushed via
  Player.Update RPCs are wrong under sniper packets (olmod DisableRpcSetAmmo/Energy for "sniper" clients).
- olmod MPTweaksOnLoadoutDataMessage throws KeyNotFoundException on the host for each joiner (ClientModifiersValid) - not ours, watch it.
- X1: IgnoreCollision between all player ship colliders on ExitSequenceStart/TeleportSequenceStart (all peers).
- Loadout carry-over: joiner captures its loadout when the exit flight ends (CoopFlow.Waiting) and re-applies it 1 s after the next
  level's OnStartLocalPlayer, before sending msg 170. In memory only.
- Diagnostics: [COMBAT] joiner: local loadout now ... (on change, 1 Hz poll); host: unlocked missile/+ammo/unlock weapon/selected missile.
- User asked (07:52): joiners should get the end-of-level stats + upgrade screen like the host. Not started; needs the post-level menu
  flow on joiners (stock DoneLevel(Escaped) path) with "continue" replaced by waiting for the host's next level.

## 0.4.14 status (2026-10-04 08:23) - installed, UNTESTED
- Installed SHA1 99f1794354a3f01fea68c3446be8b8e149c49d10 (158720 bytes), verified in game folder + build-phase2b. Protocol 13. Tag v0.4.14. 161 patches, 0 problems.
- 0.4.13 run: olmod drops gone (0), carry-over works; Devastator: unlock reached joiner, missile ammo didn't.
- D2 (Player.AddMissileAmmo, host, remote player): delta -> msg 174 MissileGrant to that joiner; joiner adds it (clamped to
  GetMaxMissileAmmo) and unlocks the missile if still LOCKED. Watch for double-adding if olmod's own path ever starts delivering.
- Carry-over now Merge(): levels/picked-up/upgrades = max; counts/energy/selection = carried.
- Not checked yet: weapon pickups (UnlockWeapon -> RpcUnlockWeaponClient) and ammo/energy pickups on joiners under olmod sniper.

## 0.4.15 status (2026-10-04 09:28) - installed, UNTESTED
- Installed SHA1 94f904c861f1dd6b67318166daef92979a1789cf (160768 bytes), verified in game folder + build-phase2b. Protocol 13. Tag v0.4.15. 162 patches, 0 problems.
- 0.4.14 tested OK (Devastator, melee, missile pickups, no drops).
- Joiner end-of-level screens (src/olcoop/Phase5PostLevel.cs): F6 now lets joiner EscapeLevel run (PostLevel.Begin) -> stock
  DoneLevel(Escaped) -> LEVEL_RESULTS/UPGRADE_MENU/briefing. P1 prefix on MenuManager.PlayGameUpdate holds the joiner (unless C1 set
  PostLevel.AllowPlay), captures the loadout (after upgrades), shows the status line, then loads the pending host scene via
  NetworkManager.LoadScene (C1) or asks for it (CoopClient.AwaitLevel). C1 defers the host's scene while PostLevel.InMenus.
- Risks to watch: host messages arriving while the joiner is in menus after its own DoneLevel (netcode OFF there); joiner XP/upgrade
  points (host-side pickups; SyncVar m_upgrade_points); last level of the campaign (VICTORY/credits path not handled).

## 0.4.16 status (2026-10-04 09:53) - installed, UNTESTED
- Installed SHA1 78bcf2c61ac20ae87cac0df6646be26a9a138627 (161280 bytes), verified in game folder + build-phase2b. Protocol 13. Tag v0.4.16. 163 patches, 0 problems.
- 0.4.15: joiner end-of-level screens + upgrades worked; the hand-off to the host's next level failed.
- P2: joiner registers a no-op handler for UNET msg 36 (NotReady). Without it UNET aborts the batch and the scene messages behind it
  (SendScene: NotReady, 172, 171, 48, 49) are lost. This was also the "dropped first scene message" in 0.3.9/0.3.10 Hardcore restarts.
- PostLevel.Gate calls CoopClient.AwaitTick every frame while waiting (S2 tick is GameplayManager.Update, not running in menus).

## 0.4.17 status (2026-10-04 10:10) - installed, UNTESTED
- Installed SHA1 242608cae1086fb475720c1ae87638c827a2aabd (162304 bytes), verified in game folder + build-phase2b. Protocol 13. Tag v0.4.17. 163 patches, 0 problems.
- PostLevel.DeadJoinerExit (CoopFlow.OnExit / Tick when !LocalAlive): Spectate.Stop, GameplayManager.EscapeLevel -> end screens.
  Falls back to ShowWaiting on an exception.
- PostLevel.ShouldDefer only defers while MenuManager.m_menu_state is an end-of-level screen (LEVEL_RESULTS, STATS, UPGRADE_MENU,
  SAVE_MENU, SAVE_ERROR, DEBRIEF, LEVEL_BRIEFING, BRIEFING, ENTITY_BRIEFING, MISSION_COMPLETE); otherwise captures the loadout and loads.
- Open question: why joiner 9580's menus went to MAIN_MENU after LEVEL_RESULTS at 10:05:14 (user action or stock flow).

## 0.4.18 status (2026-10-04 10:22) - installed, UNTESTED
- Installed SHA1 8477c44eee917d9ace6e0ac05122dee3e33c5870 (162816 bytes), verified in game folder + build-phase2b. Protocol 13. Tag v0.4.18. 164 patches, 0 problems.
- DeadJoinerExit: Spectate.Stop, DeathPaused=false, CoopHud.ClearRespawn + ClearOverlayElement(1), SetScreenFade(0),
  MenuManager.RecoverFromDeathMenu(false,false) (reflection), then EscapeLevel. P3: MenuManager.Update prefix keeps DeathPaused off
  while PostLevel.Active.

## 0.4.19 status (2026-10-04 10:55) - installed, UNTESTED
- Installed SHA1 beaed599a6394ee8faee72fd39b2b4330b3f94b5 (168448 bytes), verified in game folder + build-phase2b. Protocol 14. Tag v0.4.19. 164 patches, 0 problems.
- 10:44 run (0.4.18, hardcore): host died 10:45:52 (reset scheduled), joiner exited 10:45:56; host took the old dead path (EscapeLevel
  without flight), log stops at MENUS - frozen menus (DeathPaused), user restarted the host.
- Exit revive (CoopFlow): Regroup queues dead/dying ships (QueueRevive) instead of skipping them; OnExitRequest with a dead host
  sends the exit and queues the host. CoopFlow.HostTick (F11, host, every frame): m_dead ship -> SpotNear lane spot ->
  CoopDeath.RespawnAt (Server.RespawnPlayer); once alive: host -> ExitSequenceStart/TeleportSequenceStart, joiner -> Exit PoseMsg.
  8 s timeout -> old path (host: PostLevel.ClearDeathForMenus("host") + EscapeLevel; joiner: Exit with pos zero).
- Joiner OnExit while dead: pending exit, waits up to 3 s for the respawn, then DeadJoinerExit. OnExit also cancels a pending
  hardcore leave (CoopDeath.CancelLeave). Host SendExit -> CoopDeath.CancelForExit (ExitInProgress: no timers, no restart).
- Ready check: FNet.Ready=189 (J->H). Joiner PostLevel.ReadyTick (from P3, MenuManager.Update) sends every 3 s while at the PLAY_GAME
  gate, in MAIN_MENU after the level, or holding black (CoopFlow.Waiting). Host HostReady: Begin on DoneLevel(Escaped) (F15), Gate in
  MenuManager.PlayGameUpdate (P1) holds until all verified connected joiners are ready, banner on overlay slot 3; End on LoadLevel (F16)
  or MAIN_MENU. No timeout (user: host must not continue without them).
- Known race: hardcore death, joiner leaves for the restart 2 s after the death message; an exit 2-4 s after a death can come too late.

## 0.4.20 status (2026-10-04 11:30) - installed, UNTESTED
- Installed SHA1 e3ffaa408b93bc01b754c3209a41826437fc5f13 (169984 bytes), verified in game folder + build-phase2b. Protocol 14. Tag v0.4.20. 164 patches, 0 problems.
- User-confirmed working (11:05-11:18 runs): joiner Devastator, Shredder/claw melee, exit line-up, boss lockdown on entry, lockdown pull
  from far away, status lines after the exit, teammate maps, switching the spectated player.
- Spectate lighting (user: spectators darker, no headlights from the live player): Spectate.BoostLights sets the followed ship's
  c_lights (0-2 headlights, 3 fill/boost, 4 thunderbolt) to LightRenderMode.ForcePixel, restored on switch/stop; Viewer damage
  blur/overbright cleared each tick. [SPECT] lines log headlightsOn/unlock/pixelLightCount and each light's enabled/intensity/mode
  (on follow and every 15 s) - check them if still dark (e.g. headlightsOn=False on the spectator's copy = headlight state not synced).
- Open: revived/respawned ships log headlights=False after respawn (state not restored on remote copies).

## 0.5.0 status (2026-10-04 12:17) - installed, UNTESTED. New phase "online", build folder build-online
- Installed SHA1 915b3c4308ca8f9bdd8e7e7d54b8d213f1269568 (196608 bytes), verified in game folder + build-online. Protocol 15.
  Tag v0.5.0. 177 patches, 0 problems. olcoop.bat also installed in the game folder. Friend zip dist/olcoop-0.5.0-online.zip
  (SHA1 4d78f57ffdfc75204a5ce5f671a1f41bc316d8d4).
- 11:32 run (0.4.20): [SPECT] showed headlightsOn=False on the spectator's copy of the live ship -> Phase6Lights.cs: state sync
  (190 J->H own state, 191 H->J every ship, 1 Hz + on change), set via ToggleHeadlights only when different; stock
  RpcToggleHeadlights ignored in co-op. Stock: host's own toggles never sent; RpcToggleHeadlights dropped unless InGameplay.
- Steam transport (Phase7Steam.cs): SteamConnection : NetworkConnection, TransportSend -> SteamNetworking.SendP2PPacket (bytes +
  trailing channel byte, Steam channel 0; reliable UNET channels or >1200 B -> k_EP2PSendReliable, else UnreliableNoDelay).
  Pump in Overload.NetworkManager.Update prefix: ReadP2PPacket -> TransportReceive. Host: AddExternalConnection (conn ids from 20,
  hostId = NetworkServer.serverHostId so isConnected is true), packets that arrive before the server runs are queued. Joiner:
  Client.m_network_client = new NetworkClient(conn) (starts connected), Client.RegisterHandlers, InvokeHandlerNoData(Connect);
  FlushChannels every frame (NetworkClient.Update returns early with no transport host). NetworkConnection.Disconnect prefixed for
  Steam conns. P2PSessionRequest accepted for lobby members/friends only.
- Lobby/invites: CreateLobby(FriendsOnly, 4), lobby data olcoop=<version>; rich presence connect=+connect_lobby <id>.
  InviteUserToLobby from the CO-OP screen; GameLobbyJoinRequested -> JoinLobby -> LobbyEnter -> owner -> CoopConfig.SetJoinSteam.
  Version mismatch (lobby data) refused before connecting.
- Runtime role (CoopCore.cs): SetHost / SetJoinSteam / SetJoinIp / ClearRole; command line still works.
- CO-OP screen (Phase7Menus.cs): MenuState 121, UIElementType 123, main menu button id 40 drawn after OVERLOAD ON DISCORD
  (transpiler on DrawMainMenu, x=+500). Joiner MenuTick also runs on this screen.
- Scene skip: MenuManager.ChangeMenuState prefix. PROLOGUE/BRIEFING/INTRO/INTRO_ALIEN/INTRO_REVIVAL/ENTITY_BRIEFING -> set
  m_menu_state to that scene, call GoToNextBriefing, restore (depth guard 12). DEBRIEF -> LEVEL_RESULTS unless IsLastLevel.
- READY UP: transpiler on UIElement.DrawLevelBriefing after Loc.LS("PLAY"/"BEGIN SIMULATION").
- Not testable on one PC: Steam P2P needs two Steam accounts. Same-PC tests keep using olcoop-host.bat / olcoop-join.bat (IP).
- Known limits: the CO-OP screen is the game's own menu (works in VR); a Steam invite accepted while the game is closed starts
  Overload without olmod (start olcoop.bat first); the Steam overlay invite only works if the overlay attaches to the olmod-launched game.

## 0.5.1 status (2026-10-04 13:52) - installed, UNTESTED
- Installed SHA1 b4e991e33f55cf813f2e4579d4b8e2a59e0c4e6e (202752 bytes), verified in game folder + build-online. Protocol 16.
  Tag v0.5.1. 182 patches, 0 problems (tools/VerifyPatches.cs now also accepts OlmodTarget "game:<Type>:<Method>").
  Friend zip dist/olcoop-0.5.1-online.zip (SHA1 aa3900609d6b40c380bf683ec8bd5d58aeed614c).
- First internet run (0.5.0, 12:31-13:06): user BlownToBits (C: PC) hosted, PeetzaGuest (D: PC) joined via invite; route direct
  (relay=0). Then roles swapped. Host log 12:31 stops at 12:50:46 with no disconnect line. The D: PC crashed 12:51:10:
  access violation in steam_api64 SteamAPI_RunCallbacks (null interface = Steam API already shut down / unusable).
  Overload.Steam.Initialize starts a System.Timers.Timer (1 s) whose CallbackTimerTick calls SteamAPI.RunCallbacks on a worker
  thread, in parallel with SteamManager.Update on the main thread -> ST4 skips it. ST3: SteamManager.OnDestroy prefix sends BYE
  and sets SteamLink.ShuttingDown (Pump stops).
- Liveness: Steam P2P channel 1, 1-byte PING (1/s, also to joiners queued while the host is in menus) and BYE (reliable).
  Any packet refreshes last-heard; 20 s silence -> host DropServerConn (stock Server.OnDisconnect removes the ship for all),
  joiner HostGone (stock client disconnect -> main menu, Client.Disconnect, leave lobby, ClearRole). A >2 s gap in our own
  Pump calls (level load) resets the timers.
- Boost (Phase6Lights.cs CoopBoost): 192 J->H own m_boosting (unreliable, on change + 1 Hz), 193 H->J every ship. Non-local
  copies: PlayerShip.Update prefix applies the owner's state (UpdateBoostLoop / BoostStopped like RpcSetBoosting), host keeps it
  after FixedUpdateProcessControlsInternal; stock RpcSetBoosting ignored for ships we have a state for. [BOOST] log lines
  (first 6 changes per ship).
- Repo renamed to github.com/Blowntobytes/olcoop. Bundle is now publish/olcoop-latest.bundle (the old
  olmodcoop-latest.bundle in the user's publish folder is obsolete).

## 0.5.2 status (2026-10-04 14:13) - installed, UNTESTED
- Installed SHA1 89e80ff79bb7d88f4bb6639234191fa9a5ff20d2 (206336 bytes), verified in game folder + build-online. Protocol 16
  (handshake also compares the full version). Tag v0.5.2. 186 patches, 0 problems. Friend zip dist/olcoop-0.5.2-online.zip
  (SHA1 034791ec7453313f1d4844c37454655d4ece2689).
- 13:33 run (user host): joiner quit to menu at 13:37:29 and was re-verified at once (CoopClient.MenuTick rejoin); joiner closed
  the game 13:38:02 -> host got BYE and dropped conn 20 (works). Only "boost off" ever logged for every ship.
- Stock Client.OnDisconnectMsg -> Player.ExitMultiplayerToMainMenu -> SwitchToMenu(MP_MENU=61): wrong for co-op. C2d is now a
  prefix that replaces it for welcomed joiners with SteamLink.ReturnToMainMenu (PostLevel.Reset, CoopStatus.Clear, Spectate.Stop,
  DoneLevel(Quit) + SwitchToMenu(MAIN_MENU) if in a level, else ChangeMenuState(MAIN_MENU)). HostGone uses it too.
- ST2 (Steam NetworkConnection.Disconnect) sends BYE before closing; stock host quit (Server.DisconnectAllRemoteClients) and joiner
  quit (Client.Disconnect) from the PAUSE_MENU state end the session (ST6/ST7 -> SessionEnd.Leave: SteamLink.Leave + ClearRole).
- Esc menu item id 30 (PM1 transpiler after the QUIT TO MAIN MENU item, position local by ref, +62); PM2 maps it to selection 10
  so the stock quit flow + ARE YOU SURE? runs.
- Boost owner side: FixedUpdateProcessControlsInternal postfix latches m_boosting for the local ship (not while resimulating);
  owner state = latch || m_boosting || (USE_BOOST pressed && m_unlock_boost && overheat <= 0). Report 192 is now reliable.
- Installer (14:20): install.bat calls find-overload.ps1 (registry SteamPath/InstallPath -> libraryfolders.vdf paths, plus
  <drive>:\SteamLibrary, Steam, Program Files (x86)\Steam, Program Files\Steam, Games\Steam, Games\SteamLibrary, Games on every
  drive; Overload.exe required, olmod.exe preferred). Not run on Windows yet (no PowerShell in the cloud workspace).

## 0.5.3 status (2026-10-04 14:20) - installed, UNTESTED
- Installed SHA1 af2212f147cd23342fc99b6841fe4e85f1070bb2 (207360 bytes), verified in game folder + build-online. Protocol 16.
  Tag v0.5.3. 186 patches, 0 problems. Friend zip dist/olcoop-0.5.3-online.zip (SHA1 4d2176e21ef871c4c90984f848221fe3e72ecee6).
- 14:04 run (0.5.2, both logs supplied by the user): "[UI] LEAVE SESSION chosen" x9 / "STOP HOSTING chosen" x4, nothing else.
  PausedUpdate calls UIManager.MouseSelectUpdate() before its switch, which puts the mouse-hovered id (30) back, so the redirect
  to 10 never reached case 10. PM2 is now a prefix that runs MouseSelectUpdate itself and, on our item, calls
  SessionEnd.LeaveNow (ReturnToMainMenu while co-op is active -> SteamLink.Leave -> DisconnectAllRemoteClients / Client.Disconnect
  -> ClearRole) and skips PausedUpdate that frame.
- Boost: host log "my boost ON (flag=True button=True ...)" and joiner log "netId=2 boost ON (host)" - the state now flows.
  Visual confirmation still pending (the joiner was dead/spectating at the time).
- ReturnToMainMenu clears m_dead/m_dying on the local ship (14:08: PlayerHasDied -> DoneLevel(Died) after the host left).

## 0.5.4 status (2026-10-04 14:38) - installed, UNTESTED
- Installed SHA1 584874ccbd9f919d33e998065baf3dc74fc81cbf (207872 bytes), verified in game folder + build-online. Protocol 16.
  Tag v0.5.4. 186 patches, 0 problems. Friend zip dist/olcoop-0.5.4-online.zip (SHA1 3a14569daed6a22328812d7b60373e8f6b070bd3).
- 14:16 run (0.5.3, both logs): user confirmed LEAVE SESSION / STOP HOSTING work. Joiner left 14:20:01; host dropped it 14:20:15
  via P2PSessionConnectFail (error 4), not the BYE: CloseP2PSessionWithUser right after the reliable BYE discards it. Now
  SteamLink.CloseLater (2 s, skipped if the peer is in use again) everywhere; BYE sent reliable + unreliable.
- Rejoin 14:20:26: "lost the host: no packets for 20 s" 10 ms after connecting - s_last_heard kept the old session's time.
  ClientConnect / ServerConn now reset it.
- Boost: [BOOST] ON/off reached the other side both ways, still no flames. IL: PlayerShip.Update calls UpdateThrusters (thruster
  flame scale, +1.75 when m_boosting) only if c_player.m_remote_player && !m_pregame. m_remote_player is set by
  Player.PrepareForMP (NetworkSpawnPlayer, MP only). LT8 (PlayerShip.Update prefix, non-local ships in co-op) sets
  m_remote_player = true, m_pregame = false, logs once per ship. m_remote_player has no other reader in PlayerShip.

## 0.5.5 status (2026-10-04 15:55) - installed, UNTESTED (0.5.4 was never tested; its items are still open)
- Installed SHA1 a3cdcdb4980029a5fa9c343f8a429cc1acd42cff (210432 bytes), verified in game folder + build-online. Protocol 17.
  Tag v0.5.5. 189 patches, 0 problems. Friend zip dist/olcoop-0.5.5-online.zip (SHA1 46a8dee0201342e43ea20592531990d57e9d53f2).
- Objective counters (src/olcoop/Phase8Objectives.cs). IL: UIElement.DrawHUD -> DrawHUDScoreInfo draws, for
  LevelCustomInfo.Objective == DESTROY_BOTS (1), CustomCount - GameplayManager.m_total_robots_killed ("OPERATORS"); DrawHUD draws
  CustomCount for ALIEN_WARP (3) ("CORES REMAINING"). m_total_robots_killed only grows in AddStatsRobotKilled (where the robot dies =
  host); CustomCount-- in AlienPower.OnCollisionEnter. Enum: NORMAL 0, DESTROY_BOTS 1, SECURE_CRASH 2, ALIEN_WARP 3, LEVEL_16 4,
  TRAINING 5. OB1 (GameplayManager.Update postfix, host): msg 194 ObjMsg {killed, custom, objective} on change + 1 Hz to verified
  joiners. OB2 (UIElement.DrawHUD prefix/finalizer, joiner): swaps in the host values only while DrawHUD runs, if a 194 arrived
  in the last 5 s (joiner stats untouched). [OBJ] log lines (host on change; joiner with its local values for comparison).
- Already fine (checked): ScriptOnRobotKills is host-only and its links (ScriptObjectiveMessage popups) replay on joiners;
  boss/reactor escape (MustEscape/EscapeTimer) reached joiners in the 0.4.6 and 0.4.10 runs (joiner logs show the host's timer).
  ScriptLevel1 (kill counts + "near the surface" checks on m_player_ship = host's ship only) stays host-only.
- Icon: art/olcoop.ico (16-256 px, olmod-style pixel art, black + orange "OL / COOP"; art/make_icon.py regenerates it).
  install.bat copies it to the game folder and creates olcoop.lnk (Desktop + game folder, target olcoop.bat, WScript.Shell via
  PowerShell); uninstall.bat removes both. Not run on Windows yet. The user's own game folder got olcoop.ico copied by the bridge
  but no shortcut (no shell on the PC): rerun install.bat from build-online or the zip to get it.
- Installer sources are now tracked in dist/ (install.bat, uninstall.bat, olcoop*.bat, find-overload.ps1, README.txt, olcoop.ico);
  build-online holds the same files plus the DLL. Edit dist/ and copy to build-online (or the reverse) - keep them identical.
- README: Known issues + Credits added on top of the user's GitHub edit e28d136 (fast-forwarded into local main).

## 0.5.6 status (2026-10-04 17:00) - installed, UNTESTED (0.5.4/0.5.5 items still open)
- Installed SHA1 f57efec9683dbc20cfb2e3183826ff6e214cc905 (217088 bytes), verified in game folder + build-online. Protocol 17 (no wire change; the handshake still
  needs the same full version). Tag v0.5.6. 195 patches, 0 problems. Friend zip dist/olcoop-0.5.6-online.zip (SHA1 310ec4142566e2b77ea1ef56461d70246bf6a340).
- User (16:16): the HUD shows olmod's MP PvP scoreboard in co-op. IL: UIElement.DrawHUD calls DrawHUDScoreInfo unless
  GameplayManager.ShowMpScoreboard (held key -> DrawMpScoreboardRaw); DrawHUDScoreInfo draws the MP block (match time, ping,
  anarchy/team mini scoreboard; olmod MPScoreboards prefix by MatchMode) when IsMultiplayerActive, else the SP block (bars,
  DESTROYED / OPERATORS for Objective 1, with the OB2 team values). OB4 (Phase8Objectives.cs): DrawHUDScoreInfo prefix
  (Priority.First) clears IsMultiplayerActive in co-op, finalizer restores. The held-key full scoreboard is still the PvP one.
  A SCORE line, if drawn, is the local player's own score (joiners' kills happen on the host) - host-sent scores if needed.
- Key report (16:30): "Ymir Outpost: the security key does not exist". Ymir = sp_outer_01 (Objective DESTROY_BOTS, count 40).
  15:07 run (0.5.4, host log pid1920): first key picked up 15:13:29 (ScriptDeactivateObject 13 = ScriptOnPickup chain, team level 1);
  the second key's ScriptOnPickup (-> ScriptActivateObject) never fired. Not a team-key bug: ApplyKeys raises every copy to the team
  level, AddKey then increments from it.
- Static scan (tools/levelscan.py, UnityPy + TypeTreeGeneratorAPI on Overload_Data/levelN; scene index -> name from
  globalgamemanagers BuildSettings: 5 sp_outer_02, 7 outer_03, 8 outer_04, 11-14 titan_06-09, 21 outer_05, 22 outer_01, 25 secret_01,
  26 titan_10, 27-28 inner_11-12, 29 alien_14, 30 alien_13, 31-32 alien_15-16): every key in all 17 levels is a placed Item
  (m_type 25, m_amount 1) with NetworkIdentity + SmoothSync, inactive in the file (UNET scene object, activated by
  NetworkServer.SpawnObjects in Server.OnSceneLoad like every pickup), under <scene>/_container_placed_entities/ITEM, parents
  active; watched only by ScriptOnPickup. Alien levels and sp_secret_01 have no keys. Objectives: outer_01 = 1, inner_12 = 2,
  alien_13/14/15 = 3, alien_16 = 4, rest 0. Ymir keys: #3020 at (10,-24,38) near secret wall omwall18a#507 (unlocked by
  switch_onetime#160 -> ScriptOnDestroy#4741 -> ScriptDoorUnlock#5599); #2593 at (-1,-93,104) near secret wall omwall19a#2788
  (no unlock script; secret doors open on collision, DoorAnimating.OnCollisionEnter -> OpenDoor). Placement is not the difference.
- Checked and ruled out: olmod UpdateDynamicManager_AddItem prefix (destroys items with netId 0 while IsMultiplayerActive - the
  host is a UNET server at scene load, netServer=True, so placed items have netIds), MPClassic MaybeDespawnPowerup (spewed only),
  Item.MaybeDespawnPowerup (spewed only), NGPAlterItemSpawns (New Game+ only), CTF (match mode CTF only), Robot carried drops
  (no MP gate).
- KeyTrace (Phase9KeyTrace.cs): [KEY] census 3 s after level start (netId, activeSelf/inHierarchy, pos, segment, nearest 4 doors
  within 45u: SECRET/open), 10-s diff (APPEARED / changed / GONE), Item.OnDestroy of keys with stack, Item.OnTriggerEnter of keys
  (who, server, throttled 2 s), Player.AddKey postfix, secret DoorAnimating.OpenDoor (first per door). Next run on sp_outer_01
  decides the fix; remove or reduce KeyTrace afterwards.

## 0.5.7 status (2026-10-04 17:40) - installed, UNTESTED
- Installed SHA1 f017d824448178e791d41dfa8a58801df631d90b (218112 bytes), verified in game folder + build-online. Protocol 17. Tag v0.5.7. 196 patches, 0 problems.
  Friend zip dist/olcoop-0.5.7-online.zip (SHA1 6606e666d0ab713709e7587eb0a8a2b9e08b145d).
- 0.5.6 runs 16:59 / 17:14 (host log only; user: "not hosting the key is there, hosting it disappears"; also an audio log missing;
  joiner could not break a button). Level was sp_outer_02 = TARVOS OUTPOST (Unity analytics line), continued from a save
  (CreateNewGame saved=True), not Ymir. The 15:07 Ymir run was a new game and its first key worked.
- Cause (IL + [KEY] log): SaveLoad.CompleteGameLoad -> DeserializeObjectsTransient<Item> SetActive(false)+Destroy on every scene Item,
  then SaveLoad.CreateNew (Instantiate of ItemTypeToPrefab, netId 0) per saved item. Item.Start -> UpdateDynamicManager.AddItem ->
  olmod GameMod.UpdateDynamicManager_AddItem prefix: IsMultiplayerActive && netId == 0 -> Destroy(c_go). Log 17:16:08.842: scene keys
  (netId 84/114, by DeserializeObjectsTransient) and the restored keys (netId 0, negative instance ids) destroyed after StartLevel.
  KeyTrace OnDestroy stacks are useless (Destroy is deferred to the end of the frame).
- SV1 (Phase9KeyTrace.cs): UpdateDynamicManager.AddItem prefix, Priority.First (before olmod's): host, co-op, NetworkServer.active,
  netId 0, has NetworkIdentity -> NetworkSpawnItem.Spawn(go) (NetworkServer.Spawn with the prefab assetId; joiners have
  NetworkSpawnItemHandler from NetworkSpawnItem.RegisterSpawnHandlers at game init). [ITEM] log. Unverified: whether a restored
  super item keeps its super state on joiners (spawn handler instantiates the plain prefab), and pickup sync of these items.
- Open: joiner could not break a button (17:01 run, host log shows no joiner hit at all; host's own hit at 17:03:23 worked). Need the
  joiner's log. Possibly save-related too (host level from a save, joiner's from the scene) - check the joiner's [WORLD] lines.

## 0.5.8 status (2026-10-04 17:50) - installed, UNTESTED
- Installed SHA1 7a85ecdc12654a50188c8c281a8093511ee55685 (223232 bytes), verified in game folder + build-online. Protocol 17. Tag v0.5.8. 199 patches, 0 problems.
  Friend zip dist/olcoop-0.5.8-online.zip (SHA1 0323addb5216743ae9ce73e039ae9e292ea6c917).
- 0.5.7 run 17:25 (host pid19988, saved=True sp_outer_02) / 17:26 (joiner pid11832, scene load): keys still missing. 0.5.7's
  diagnosis was WRONG: no [ITEM] line, SV1 never ran; the destroyed restored keys had activeSelf=False (destroyed before Start).
  Order of the Destroy batch (OnDestroy at end of frame): CTF flag objects (entity_item_cloak(Clone), m_type 25, pos 0, olmod
  CTFRegisterSpawnHandlers - DontDestroyOnLoad, not in item_prefabs), scene keys netId 5/35, then the restored keys at the key
  positions. Host census at +3 s: 0 keys. Joiner: scene keys exist locally but never spawned (netId 0, inactive) - host destroyed them.
  Remover of the restored keys still unidentified (single CompleteGameLoad call in StartLevel; not mod code: grep for Destroy/SetActive).
- Fix independent of the remover (Phase10SaveItems.cs): SR1 CompleteGameLoad prefix copies m_game_root["Items"] (private static, nulled at
  the end of CompleteGameLoad; entry = {"Item":{type,index,secret,amount,super,...},"gameObject":{"transform":{localPosition,
  localRotation}}}). SR2: 1.5 s after gameplay PLAYING, for each saved item without a live Item of that type within 1.5 u ->
  Instantiate(SaveLoad.GetPrefabFromItemType(type)) at the saved pose, copy index/secret/super/amount, NetworkSpawnItem.Spawn.
  SR3: Object.Destroy(Object) prefix (TargetMethods, 10 s window from the load) logs the stack for KEY_SECURITY / LOG_ENTRY items.
- Next run: read [ITEM] "save lists ..." (are keys/LOG_ENTRY in the save?), "restored missing saved ...", "Destroy(...) by:" (root
  cause), and [KEY] GONE/DESTROYED after the restore (would mean the remover also acts later). Joiner [KEY] APPEARED lines.

## 0.5.9 status (2026-10-04 18:05) - installed, UNTESTED (includes untested 0.5.8)
- Installed SHA1 c2b5b58b061f497456dfbb886eef8116a2e070f3 (224768 bytes), verified in game folder + build-online. Protocol 17. Tag v0.5.9. 200 patches, 0 problems.
  Friend zip dist/olcoop-0.5.9-online.zip (SHA1 cec1c3659e926dc53d23690ab4e7e44ad08bb8d5).
- 17:33 host (pid27288) / 17:38 joiner (pid19588) logs were STILL 0.5.7 (games started before the 0.5.8 install). Fresh new game
  sp_outer_01 then sp_outer_02. User: host sees keys + audio logs; joiner doesn't see them but can pick them up; also some power-ups;
  joiner could break the button on the fresh level (the unbreakable button was on the saved game).
- Log: host keys netId 16/21 activeSelf=False at the +3 s census, switched on at 17:35:05 / 17:39:25 (player nearby), picked up by the
  host (AddKey -> level 1, 2). Joiner keys stayed netId=0 inactive all level (never spawned). Audio logs: host picked 5, joiner played them.
- IL (UnityEngine.Networking, refs/UNET via tools/ildump.py): NetworkServer.SetClientReadyInternal spawns only objects with
  gameObject.activeSelf; nothing re-sends when an object is switched on later. NetworkIdentity.AddObserver -> NetworkConnection.AddToVisList
  -> NetworkServer.ShowForConnection (sends the spawn); RebuildObservers(true) adds every ready connection.
- IV1 (Phase10SaveItems.cs): host, every 0.5 s, Item.m_ItemList: activeSelf && netId != 0 && a ready joiner conn (id != 0) missing from
  identity.observers -> RebuildObservers(true). Items only. [ITEM] "sent ... to joiners". Unverified in game.
- Remaining: save-game button (joiner could not break it; on a fresh level it worked) - check after 0.5.8/0.5.9 on a save with both logs.

## 0.5.10 status (2026-10-04 18:15) - installed, UNTESTED (includes untested 0.5.8 + 0.5.9)
- Installed SHA1 af21132f2c0af43b57851c69a68ec1344d796fa1 (225280 bytes), verified in game folder + build-online. Protocol 17. Tag v0.5.10. 200 patches, 0 problems.
  Friend zip dist/olcoop-0.5.10-online.zip (SHA1 264baafebb88ad812022a3f4b294202e176a647a).
- User (17:55): INVITE THROUGH STEAM does nothing. Code: SteamLink.OpenInviteOverlay called SteamFriends.ActivateGameOverlayInviteDialog
  only, no log (17:33 host log has no line for it). The overlay is not attached when Overload runs under olmod.exe (HANDOFF 0.5.0 known
  limit) and is not visible in VR. Now: SteamUtils.IsOverlayEnabled() -> dialog; else Application.OpenURL("steam://open/friends")
  (Steam client friends list; Invite to Game uses rich presence connect=+connect_lobby <id>). Status line + [STEAM] log either way.
  Per-friend INVITE rows (InviteUserToLobby) are unchanged and need no overlay.

## 0.6.0-alpha status (2026-10-04 18:25) - installed, UNTESTED. First public release. New phase "alpha", build folder build-alpha
- Installed SHA1 215d6225b2bdd70b246c6bbf5ddadda3410539c0 (216576 bytes), verified in game folder + build-alpha. Protocol 17. Tag v0.6.0-alpha. 192 patches, 0 problems,
  0 compiler warnings. Player zip dist/olcoop-0.6.0-alpha.zip (SHA1 b338b1eb6fdac2fa63c6ca535d2658878ed467dc) from ./package.sh.
- 0.5.10 run 17:59-18:13 (host pid11840 + joiner pid11972 + both unity.log, all 0.5.10): user "looks real good". Saved game (sp_outer_02):
  SR captured 72 items, all 72 re-created and network-spawned (2 keys, 6 audio logs); Destroy trace confirmed the remover:
  GameMod.UpdateDynamicManager_AddItem.Prefix destroyed the restored netId-0 items (so 0.5.7's SV1 idea was right but its prefix
  never ran - unexplained; superseded by the restore). Joiner hit the button through the host on a save (18:05:59). Invite button:
  overlay enabled=False -> steam://open/friends. Errors: only olmod MPTweaksOnLoadoutDataMessage KeyNotFound (host, per join) and
  olmod ship smoothing NRE (joiner, once, suppressed). Unity logs: no exceptions.
- Cleanup (no gameplay change; patch-target diff vs 0.5.10 = exactly the removed diagnostics): removed KeyTrace (KT1-KT5), SV1, SR3
  Object.Destroy trace; SR1/SR2/IV1 -> Items.cs CoopItems (IT1 CompleteGameLoad prefix, IT2 one GameplayManager.Update postfix doing
  restore + visibility); warnings fixed (Steam Callback fields kept - GC - with #pragma 414); version/phase labels stripped from
  summaries; Objectives.cs single namespace.
- File renames (history above uses the old names): Phase0Instrumentation=Diagnostics, Phase1Session=Session, Phase1Fixes=NetcodeFixes,
  Phase2Robots=Robots, Phase2bWorld=WorldSync, Phase2bFlow=LevelFlow, Phase3Death=Death, Phase3Hud=Hud, Phase4Combat=Combat,
  Phase5PostLevel=PostLevel, Phase6Lights=ShipState, Phase7Menus=CoopScreen, Phase7Steam=SteamTransport, Phase8Objectives=Objectives,
  Phase10SaveItems=Items, CoopMenu=OptionsWindow, CoopOptionsMenu=OptionsScreen. Phase9KeyTrace deleted.
- Repo: installer sources moved dist/ -> installer/ (git mv); dist/ ignored, old committed zips untracked; package.sh; LICENSE (MIT,
  Blowntobytes); lib/README.md (Harmony 2.2.2, MIT); .gitignore also ignores publish/ and *.bundle.
- Release flow from now on: ./build.sh, VerifyPatches, ./package.sh, copy DLL + installer files to the phase build folder, install,
  SHA1 check, commit + tag, bundle. GitHub Release: the user creates it on github.com from the pushed tag and attaches the zip.

## 0.6.1-alpha status (2026-10-04 23:30) - installed, UNTESTED. Protocol 18 (LoadoutMsg 170 grew; new msg 195)
- Installed SHA1 7447093701460894f5b86bfd5f6f72cd816cd4d6 (223744 bytes), verified in game folder + build-alpha (olcoop-vr.bat also in
  both). Tag v0.6.1-alpha. 200 patches, 0 problems, 0 warnings. Player zip dist/olcoop-0.6.1-alpha.zip (SHA1
  aba5a1634e67e6ccbbc2a3d14569607c32e56bcd); release notes dist/RELEASE-NOTES-0.6.1-alpha.md.
- Runs analysed (all 0.6.0-alpha): 19:20/19:34 joiner logs (user BlownToBits = JOINER, host JosheM steam ...011202 in Wisconsin; host
  log not supplied - asked for it), 21:03 PeetzaGuest host + 21:04 user joiner (both logs, sp_titan_06 from a save), 21:36 user host
  (+ unity.log). README/known issue "Pickups don't always show up for everyone" not re-investigated (needs a host log).
- Map freeze (19:34:22 joiner opened the map, 19:34:26 host took the Ymir key -> [DUMP]/[RSYNC] (GameplayManager.Update postfix)
  stop, network-driven lines continue): Automap.Update EXIT does SetActive(false) on m_automap_objects ("_automap" children of
  Key-tagged objects = security keys AND audio logs, cryotubes, PropGeneric incl. switch_onetime - tags confirmed in level22 data)
  and GetComponentInChildren on m_doors; a network-destroyed one throws every frame -> EXIT never returns true. F18 (LevelFlow.cs)
  prunes destroyed entries in EXIT. unity.log of that session was overwritten, so the exception text itself wasn't seen.
- Host pickup FX: Item.OnTriggerEnter (server) -> CallRpcPlayItemPickupFX; RpcPlayItemPickupFX skips isLocalPlayer (stock MP: the
  picker played it in TryFakePickup on its client). IT3a tracks the item, IT3 (CallRpcPlayItemPickupFX postfix) invokes the item's
  PlayItemPickupFX for the host's own ship.
- Joiner ammo/energy: joiner = olmod sniper client (ignores RpcSetEnergy/RpcSetAmmo); server sends msg 135 PlayerAddResource only if
  MPTweaks.ClientHasTweak(conn,"sniper"); capabilities (msg 119) are sent in NetworkMatch.OnAcceptedToLobby (never in co-op), so
  ClientInfos[conn] was empty (unity.log 21:37 "MPTweaks: conn 20 OnLoadoutDataMessage clientInfo is now " - empty). IT4 patches
  ClientHasTweak: "sniper" = true for CoopHost.Verified conns on a co-op host (table is also cleared by MPTweaks.InitMatch). This also
  turns on 135 for AddEnergyDefault (energy centers), AddWeakEnergy, HUD messages client-side, weapon-sync 134 relays. IT5 disables
  olmod's MPSniperPacketsAddMissileAmmo.Prefix in co-op so missiles stay on our msg 174 (D2) only. olmod client range checks: energy
  <= 20, ammo <= 200 (co-op ammo box = 50 because IsMultiplayerActive; super ammo 1000 would be rejected), missiles <= 80.
- Upgrade points (SyncVars m_upgrade_points1/2, set in Player.AddUpgradePoint which also shows HUD + sound where it runs = host):
  IT6 prefix on the host: pickup of UPGRADE_L1/L2 (CoopPickups.Current) gives every other player AddUpgradePoint too; for joiner
  copies only the SyncVar is raised + msg 195 UpgradePointMsg {super, shared, total} -> joiner shows the stock message (+ "(TEAMMATE)").
  Host's own ship keeps the stock path.
- Carry-over: LoadoutMsg 170 now has slevel (Player.m_upgrade_level[10], ship upgrades) and points1/points2. Merge: slevel = max,
  points = carried (local fields); host Apply sets m_upgrade_level and the points SyncVars (the host copy is what syncs back). Before,
  each level's new Player started with 0 points for joiners and lost bought ship upgrades 0-3 (unlock flags 4-9 were already carried).
- Friendly fire: FF1 prefix now takes ref DamageInfo: teammate damage x0.5 when FF on (TeammateDamageScale), none when off.
- Hologuide: FindNextGuidebotObjective -> FindSegmentContainingKey searches RobotManager.m_master_item_list = FindObjectsOfType<Item>
  at level init + spewed items only (Item.Start). Joiner items arrive later (network spawn / IV1), host save-restored items are new
  instances. IT7 (Item.Start postfix) adds unlisted items in co-op; IT8 (Item.OnDestroy prefix) removes them (FindPowerupNearPlayer has
  no null check). Joiner key: ApplyKeys (msg 180) sets Robot.m_player_picked_up_security_key (stock sets it only in the server pickup).
- VR launcher: installer/olcoop-vr.bat (olcoop.bat + -vrmode openvr), "olcoop VR.lnk" from install.bat, removed by uninstall.bat,
  in package.sh. Not run on Windows yet.
- Robot lag (CA-WI): puppets render HostNow()-0.1 s (Robots.cs Tick, 20 Hz StateBatch) + one-way latency. Options proposed to the
  user (adaptive delay, dead reckoning to host time, RTT logging first); nothing changed.
- Wishlist from the user (not started): lobby member list on the CO-OP screen, 4 players, map/world ping visible through walls for
  15 s to everyone, weapons available to every player (per-player weapon pickups).

## 0.6.2-alpha status (2026-10-05 08:30) - installed, UNTESTED. Protocol 19 (new msgs 196 roster, 197 ping)
- Installed SHA1 daf3a050f91140daf67abae46abaadddb367157c (233472 bytes), verified in game folder + build-alpha. Tag v0.6.2-alpha.
  206 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.2-alpha.zip (SHA1 1e0cc80153a55b042c6b4f04753830defe85f374).
- 0.6.1 run 06:58-07:17 (host BlownToBits pid12164 + joiner PeetzaGuest pid19068, both 0.6.1, saved sp_outer_02): user confirms
  host pickup sounds. Host hp=200 at OnStartLocalPlayer (save), refills later stopped at exactly 120 (07:14:07, 07:17:12).
  Cause: static Player.MAX_HITPOINTS/MAX_ENERGY set in every Player.Awake (200 SP / 120 IsMultiplayerActive); host's Player is from
  the main menu, joiner Players flip it to 120. User chose multiplayer rules: CoopCaps (Items.cs IT9/IT9b/IT9c): 120 on co-op
  Player.Awake, OnStartLocalPlayer, after SaveLoad.CompleteGameLoad; clamps armor (server) and energy (owner); Merge clamps energy.
  Reactor "maybe doubled" sound: not reproduced in logs (no per-sound logging); nothing changed, on the test list.
- Lobby.cs: msg 197 PingMsg {reply, t} on unreliable channel 2, 2/s both ways (host -> verified conns, joiner -> host), EWMA 0.2;
  CoopLobby.MyRtt (joiner). Msg 196 RosterMsg (host -> verified, 1/s): {pilot, steam, RosterState, ping ms, host}. States from
  CoopHost.Verified, m_Players (dead flag), HostReady.On/IsReady (new accessor), host GameManager state. CO-OP screen section
  "IN THIS SESSION (n/4)" (friend rows now stop above BACK), F8 window section (IMGUI: not visible in VR). [LOBBY] log on change.
  Tick: Overload.NetworkManager.Update postfix (runs in menus and levels).
- Robots.cs joiner display (default "predict"): render = HostNow() + Lead(), Lead = clamp(MyRtt, 0, RobotLeadMax=0.15);
  target = p1 + v1 * clamp(render - t1, 0, 0.35); on a new state Puppet.err = shown - target (snap > 4 u), decays with tau 0.1 s;
  rotation the same via Puppet.rerr. Old display in TickInterp (-cooprobots interp). [RSYNC] "joiner robots: predict ..." every 15 s
  with average correction (u) and snaps - use it to tune RobotLeadMax. Not changed: robot projectiles still start at the host's
  muzzle position (now slightly behind the drawn robot); host-side lag compensation for joiner shots (rewind) not done.
- F8 window Broadcast sent ConfigMsg without ff (friendly fire switched off on joiners when the mode was changed there): fixed.


## 0.6.3-alpha status (2026-10-05 17:00) - installed, UNTESTED. Protocol 20 (new msgs 198 item touch J->H, 199 reject H->J)
- Installed SHA1 3d2910665b1be4d05a9d62b7ab24ac842e8912e1 (239616 bytes), verified in game folder + build-alpha. Tag v0.6.3-alpha.
  213 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.3-alpha.zip (SHA1 2ac3bcf487203e6193f63b1b1a2e1ddf0a6a9485).
- 0.6.2 run 15:51-16:01 (host BlownToBits pid16556, joiner PeetzaGuest pid21632, LAN rtt 8-23 ms, saved sp_titan_06, 93 items
  re-created): user confirms hologuide, starting armor/energy, session list. Joiner ammo 200 -> 250 (0.6.1 sniper fix works).
  No destroyable hit at all in either log (no button attempt captured).
- Map (F19, LevelFlow.cs): PlayerShip.FixedUpdateReadControls prefix while GameplayState.AUTOMAP in co-op: ClearCachedInput,
  UpdateCachedButtons, ClearCachedButtons, joiner still SendPlayerControlsToServer (empty). Same as the stock non-GAMEPLAY branch.
- Joiner pickups (Items.cs CoopTouch): Item.OnTriggerEnter on a client = TryFakePickup -> HideItem (SetActive(false), never undone)
  + FX; the real pickup needed the host-side joiner ship in the trigger. IT10 (TryFakePickup postfix, joiner, m_fake_picked_up)
  sends 198 {item netId}. Host OnTouch: item via NetworkServer.FindLocalObject, joiner Player by conn, alive, distance <= 12 u,
  then invokes the item's own OnTriggerEnter with the joiner ship's c_mesh_collider (stock checks incl. ItemIsReachable). Not
  picked up (full / unreachable / too far / no ship) -> 199 -> joiner sets m_fake_picked_up=false + SetActive(true). Double pickups:
  IT3 marks an item consumed (instance id) when CallRpcPlayItemPickupFX fires; IT3a prefix skips OnTriggerEnter for consumed
  items (also covers same-frame double triggers). Reset on GameplayManager.StartLevel. Why the host-side trigger misses was not
  proven (no per-touch logs before this build); the new [ITEM] touch lines show distance on the host for every joiner touch.
- Joiner buttons (W8, WorldSync.cs): Projectile.ProcessCollision applies damage only if NetworkManager.IsServer(); the always-on
  path (Projectile.OnTriggerEnter -> Destroyable.ApplyDamage) is skipped for upgraded player shots (m_upgrade >= LEVEL_1). W8
  prefix on the joiner: own shot + Destroyable in the collider's parents -> CoopWorld.SendHit (msg 179, existing host path).
  The host's copy of the same shot can also hit: a destroyable may take two hits for one shot (buttons only need one or two).
- Weapons shared (CoopPickups.ShareWeapon from IT3): WEAPON_* pickup -> UnlockWeapon(wt, false, true) on every other player still
  LOCKED (stock gives starter ammo 200 / energy 10 with an unlock). Missiles/armor/energy/ammo not shared (user).
- Esc menu (PM3, CoopScreen.cs): DrawPauseMenu postfix draws the roster at UI_LEFT + 40, y from -124 (two lines per player).

## 0.6.4-alpha status (2026-10-05 17:45) - installed, UNTESTED (includes untested 0.6.3). Protocol 21 (new msg 200 ship stats)
- Installed SHA1 2e8051b8fec703d19bbc53963c4e7c2f0f5eae1c (245760 bytes), verified in game folder + build-alpha. Tag v0.6.4-alpha.
  218 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.4-alpha.zip (SHA1 798466882fc1fa46591d5872030923be226bce13).
- User (16:33): "clearly shot at a button" in the 15:53 run. Both logs have no Destroyable.ApplyDamage at all (W2 logs every call on
  the host via HostNoteHit, first 30), so no shot reached a destroyable's damage code - not the joiner's and not the host's own. Not
  explained yet. Candidates (unproven): the shots hit a Forcefield/SimpleShield (layer 24 "Bounce"; SimpleShield.ApplyDamage, not
  logged) in front of the button; or the button's collider is a trigger and upgraded shots take the trigger branch that ignores
  destroyables (Projectile.OnTriggerEnter, m_upgrade >= LEVEL_1). W9 (ProcessCollision prefix) logs player shots that hit a
  Destroyable (hp, m_invulnerable, dying), Forcefield, SimpleShield or layer 24 (40 per level); W9b (OnTriggerEnter prefix) logs
  upgraded player shots passing a Destroyable through the trigger branch (20). Next run decides.
- Spectate (Death.cs): HideShip(s, hide, keepCockpit) skips renderers under PlayerShip.c_cockpit (active on remote ships, see
  Player.RestorePlayerShipDataAfterRespawn else-branch); followed ship's c_cockpit forced active and restored; rig follows
  m_camera_parent (eye) instead of c_transform. Spectate.Target exposed.
- SpectateHud (Hud.cs): overlay slot 2 (stock uses 0/1), UIElementType 124, y 205/235/265 from centre: name, ARMOR/ENERGY/AMMO,
  weapon + missile xN. Host sends msg 200 StatsMsg {netId, hp, energy, ammo, weapon, missile, missile ammo} 4/s on channel 2 to
  verified joiners while a level runs (>= 2 players); joiner uses entries < 2 s old, else its own copy. Joiner copies of the host's
  energy/ammo are stale otherwise (olmod MPSniperPacketsDisableRpcSetEnergy/Ammo: client side always skipped).

## 0.6.5-alpha status (2026-10-05 19:00) - installed, UNTESTED. Protocol 22 (new msg 201 ping)
- Installed SHA1 b708bb60b2020c03a6b37d8bb86a2662f584f134 (257024 bytes), verified in game folder + build-alpha (+ olcoop-steamvr.bat,
  olcoop-oculus.bat in both). Tag v0.6.5-alpha. 232 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.5-alpha.zip (SHA1
  5bff19728044bc8474e83651668c550a217e91eb). The old olcoop-vr.bat is still in the user's game folder until install.bat runs.
- 0.6.4 run 16:42-16:52 (host PeetzaGuest pid22284, joiner BlownToBits pid8312, LAN, saved sp_titan_06): 16 joiner touches, all
  picked up on the host at ~2.2 u (host log printed type NONE: read after the pickup; fixed). F19 logged. Spectate start/readout
  logged. No W9/W9b lines (no shot hit a destroyable/shield in either game).
- Spectate HUD: Viewer.c_ui_mesh_transform (child "_ui_mesh" of the ship's c_cam_controller) moved under the spectate rig on Start and
  back on Stop; HUD element re-created (the death sequence DestroyType(HUD) at m_dead_timer <= 0). HUD9: UIElement.DrawHUD
  prefix/finalizer swaps GameManager.m_player_ship / m_local_player to the followed ship for that call only. Joiner applies msg 200
  stats to its copies of OTHER players (hp, energy, ammo, weapon, missile + ammo). Overlay now only "SPECTATING <name>" (y -250).
- Claws: RobotManager.Update wakes sleeping/lurking robots within 15 u of GameManager.m_player_ship 0.25 s after
  m_local_player.m_player_projectile_fired, which ProjectileManager.FireProjectile sets for EVERY PLAYER projectile (host copies of
  joiner shots too). P19 (FireProjectile prefix/postfix, host): joiner-owned shot -> restore the host's flag/timestamp, queue
  CoopAwaken for that joiner's position; P20 (RobotManager.Update postfix) calls MaybeAwakenRobots(joiner pos) after 0.25 s.
  CoopTargets.Choose visibility now = Robot.VisibilityRaycast (mask 67256832, first hit = ship root on layer 9) instead of the
  LOS_MASK linecast. Not changed: many robot decisions read the host's m_local_player.m_cloaked / m_player_ship.m_headlights_on.
  Robot activation itself was fine (UpdateActiveStatusRobotsOnly(force) runs every frame, P11 counts joiners).
- Relevance: RobotManager.UpdateActiveStatusAll (items, doors, triggers, props) only runs when UpdateChunkActivationDueToPlayerMovement
  (host segment change); Item/Door/PropInRelevantSegment use the host segment only. P15 makes the movement check true when any
  joiner changed segment (CoopRelevance, once per change); P16-P18 postfixes count segments visible from any living joiner.
  Expected to fix README "pickups don't always show up" and joiner-only buttons far from the host (props off on the host). Untested.
- Ping.cs: MapCamera.SetMarker postfix (stock FIRE_FLARE in the map; AutomapMarker.MarkerPosition after its wall snap) -> msg 201
  {pos, name}; joiner -> host -> other joiners; host -> joiners. Markers: primitive spheres, material Hidden/Internal-Colored with
  _ZTest Always (through walls), queue 4000; world copy layer 0 scaled by camera distance, map copy on MapCamera.m_map_focus's
  layer; 15 s; one per player name. Map centre sphere = child of MapCamera.m_map_focus while GameplayState.AUTOMAP.
  Hologuide: PlayerShip.GuidebotCommandStrings[3] (CRYOTUBE) shows PING for 60 s after a ping; IssueGuidebotCommandFromWheel prefix
  with m_guidebot_command 3 sets the lead flag; Robot.FindSegmentContainingCryotube prefix returns the ping segment
  (CreatePathToSegment) and sets Robot.m_guidebot_special_goal_segment; within 8 u -> Robot.m_player_reached_cryotube = true.
- Installer: olcoop-vr.bat renamed olcoop-steamvr.bat; new olcoop-oculus.bat (-vrmode oculus); shortcuts "olcoop SteamVR" /
  "olcoop Oculus" (install removes "olcoop VR.lnk" and the old bat).

## 0.6.6-alpha status (2026-10-05 19:30) - installed, UNTESTED. Protocol 22 (no wire change)
- Installed SHA1 a9c712dddd8d2d76c7c9875bd312f26d256d2412 (257536 bytes), verified in game folder + build-alpha. Tag v0.6.6-alpha.
  232 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.6-alpha.zip (SHA1 f3135f15548437cafe94d1f1a90837fe80b971dd).
- User (17:31): sphere 75% smaller, teal, through all geometry from anywhere. Ping.cs: material colour (0, 0.8, 0.75, 0.9); focus
  sphere 1.2 -> 0.3, map ping 2.5 -> 0.625, world ping max(0.6, d*0.025) -> max(0.15, d*0.00625); MeshRenderer
  allowOcclusionWhenDynamic = false (Unity occlusion culling skipped it behind walls before _ZTest Always applied - likely, not
  confirmed); beyond 0.9 x Camera.farClipPlane the world marker is placed on the same line of sight at that distance, scaled to keep
  its apparent size.

## 0.6.7-alpha status (2026-10-05 20:00) - installed, UNTESTED. Protocol 22 (no wire change)
- Installed SHA1 416f99b15f66949c9f90fc8e5ae4cd4718530806 (257536 bytes), verified in game folder + build-alpha. Tag v0.6.7-alpha.
  233 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.7-alpha.zip (SHA1 e422259f77659ed60cb144df5f06a5805e12cbdc).
- User (17:47): markers 25% larger; drop the CRYOTUBE -> PING wheel label ("not consistent enough"); guide out + active ping -> go
  there unless commanded from the wheel; map text "USE FLARE TO PING".
- Ping.cs: sizes 0.375 / 0.78 / max(0.19, d*0.0078). Wheel label code removed. Show() -> OnNewPing (s_pending); GuideTick: pending and
  ping < 15 s old and Robot.m_guidebot != null && Robot.GuidebotAlive -> Robot.m_guidebot_goal = GO_TO_CRYOTUBE (stock
  GuidebotSetGoal -> FindSegmentContainingCryotube, PG4 returns the ping segment while s_guiding). PG3 is now a postfix:
  IssueGuidebotCommandFromWheel returned true -> CancelGuide. PG8: UIElement.DrawMapHUD postfix draws the hint at
  (0, UI_BOTTOM - 32), teal.

## 0.6.8-alpha status (2026-10-05 20:45) - installed, UNTESTED. Protocol 23 (new msgs 202 own flare color J->H, 203 table H->J)
- Installed SHA1 7169d434534dfedfbfbc9bf192f048c2542d2a1c (264704 bytes), verified in game folder + build-alpha. Tag v0.6.8-alpha.
  239 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.8-alpha.zip (SHA1 2e12878398b678bda08b065ac6d079b44d2e3138).
- User (18:01): 0.6.7 "looks good"; pings 30 s; per-player flare colors (6 choices), as bright as the original.
- Flare prefab read from Overload_Data/resources.assets with UnityPy (tools: /tmp script, not committed): proj_flare = Light
  (0.353, 0.647, 1; intensity set every frame in Projectile.UpdateDynamic), child _fill_light Light (0.551, 0.703, 1, 0.4, range 20),
  _glow/_glow2/_glow3 ParticleSystems, _lens_flare ProFlare (GlobalTintColor). Materials live in sharedassets0.assets (not read).
- FlareColors.cs: CoopSettings.FlareColor (own setting, flarecolor= in olcoop-settings.txt; CO-OP OPTIONS item ID_FLARE, cycles,
  never read-only). Projectile.Fire postfix (flare types, co-op): owner ship -> color (local setting, else table by netId) -> Apply:
  originals remembered per projectile instance (pooled) and restored for ORIGINAL; lights' color = tint; fill intensity x Boost;
  particle startColor = tint x max(original rgb) (alpha kept) + already-emitted particles recolored; ProFlare.GlobalTintColor = tint.
  UpdateDynamic postfix: main light intensity x Boost. Boost = clamp(lum(original light)/lum(tint), 1, 2): RED 1.66, PURPLE 1.24,
  others 1. Table: joiner sends 202 every 2 s in a level; host sends 203 (all players incl. its own) every 2 s.
  build.sh now references UnityEngine.ParticleSystemModule.
- Ping.cs Lifetime 15 -> 30 s (markers and the hologuide start window).

## 0.6.9-alpha status (2026-10-05 21:45) - installed, UNTESTED. Protocol 24 (msg 200 grew; new 204 lockdown/boss bar H->J, 205 own HUD J->H; status 188 code 3)
- Installed SHA1 bc3a26fccf0c116602baa9663176e85941cc3772 (281600 bytes), verified in game folder + build-alpha. Tag v0.6.9-alpha.
  254 patches, 0 problems, 0 warnings. Zip dist/olcoop-0.6.9-alpha.zip (SHA1 5ee875834dc8e991adbaa8229782c485250bf2ed).
- 0.6.8 runs 19:29-20:45 PT, 4 players over Steam, host DescMax7930 (host clock = PT + 2 h; match by Steam lobby id), joiners
  BlownToBits (pid26932 20:12, pid28112 19:29 from the PC), JosheM (no log), PeetzaGuest (pid20216). Levels outer_01-05, titan_06.
  All [INIT] 0.6.8-alpha protocol 23.
- Lockdown: joiners logged "script N ScriptLockdownMaster is host-only; not run here" -> no LockdownBegin on joiners (counter
  UIElement ~7272 reads LockdownActive/LockdownRobotsRemaining; alarm cues 361/351; WarningPopup), and LockdownDoors never
  ForceClose'd there. Host copy of a joiner's ship is authoritative -> invisible wall at the host's closed door.
  Lockdown.cs: ScriptLockdownMaster removed from HostOnly (and both lockdown scripts kept out of the catch-up history: NoHistory);
  joiner guards LK1 LockdownSpawnBot, LK2 LockdownUpdate (private), LK3 LockdownDestroyedBot -> skipped while CoopWorld.Matched.
  Host msg 204 LevelHudMsg {active, boss, showHP, clear, remaining, hpPct, name, script id} on change (<=10/s) + 1/s. Joiner: runs the
  script by id if the host is in a lockdown we haven't started (late join), sets the count (bot-died cue on a drop), LockdownEnd(clear)
  when the host's ends (LK7 records the host's clear flag). Boss lockdowns untouched (each machine's boss copies count down and start
  the escape; Goliath = BOSS1 + BOSS1B). [LOCK] log lines.
- Boss/reactor bar: ReactorShowHP/ReactorHPPct/ReactorName are set by Robot damage (host) and each machine's Reactor.Update from its own
  copy. LK5 (DrawHUD prefix, Priority.First) applies the host's values from msg 204 on joiners (< 3 s old).
- Lockdown teleport outside the room: HostScriptActivated is a W1 postfix, DoorAnimating.ForceClose only starts the close
  (OpenForever=false, m_open_timer=0), so the regroup linecast passed the open doorway. CoopHost.FindNear now restricts every spot
  (TryAround via SpawnOk, TrySegments already) to RoomOf(anchor): segments reachable without crossing a door portal (DoorData != null).
  Applies to lockdown regroups, joiner spawns and respawns. The logs show every lockdown regroup moved the others ("moved netId=..");
  "not teleported" was most likely the out-of-room spots - check [FLOW] "lockdown: moved" against what the players saw.
- Exit stuck (PeetzaGuest 19:59:53-20:01:01, gpState=EXIT, ship parked at (24, 2.7, 54) seg 1272): stock ExitSequenceFrame pushes the
  ship straight (AddForce) at m_path_to_exit[m_exit_path_index] centres, starting at index 0 = the ExitSegmentType.Start segment
  (CreatePlayerPathToEnd), and only EscapeLevels within 2 u of path[len-2]. Our 10-u "lane spot" below the exiting ship had a wall
  between it and the Start segment. LevelFlow.cs ExitTunnel: host builds one slot per player on the path polyline (Start ->
  path[len-2]), 5 u apart (min 2.5, compressed for short paths), trigger-er in front; HostPlaceSelfForExit(door) / Regroup / revive
  use the slots; X2 (ExitSequenceStart postfix, every machine) sets m_exit_path_index to the next path point ahead of the local ship
  and m_camera_path_index to match; X3 (ExitSequenceFrame postfix) forces the stock completion (m_exit_completing, timer 1.1) after 5 s
  without getting 1 u closer to the end or 25 s total. X1 (ship colliders ignore each other) unchanged. Teleport/warp exits keep the
  old placement (no flight). Log: [FLOW] "exit tunnel: n slot(s)", "placed in the exit tunnel", "exit flight starts at path point",
  "exit flight stuck ... finishing it".
- READY UP from the menus: the host's ready check counted a joiner who joined during the end-of-level screens (22:12:41 host) but that
  joiner never sent Ready (ReadyTick only after its own level end). HostReady.Remind (MenuManager.Update) sends status 3 every 3 s
  to verified joiners not ready; joiner PostLevel.HostWaiting (expires after 10 s), ReadyButton -> main menu CO-OP button reads
  "CO-OP: READY UP" (select = ready) and the CO-OP screen gets a READY UP item; ManualReady makes ReadyTick send Ready. Cleared when
  not connected / host stops waiting / level start. GameplayManager.LevelIsLoaded is NOT cleared on returning to the menus: use
  GameManager.m_game_state == MENU.
- Spectate lighting: ChunkManager.ActivateChunks / DisableLights / UpdateLights / DisableReflectionProbes / AmbientSoundsEnable all use
  GameManager.m_player_ship (segment visibility, lights within 60 u). SP1 (Death.cs SpectateView) swaps in the followed ship for those
  calls (its SegmentIndex computed from its position - remote copies keep 0 on joiners - and restored); Spectate.Next/Tick re-run
  ActivateChunks/DisableReflectionProbes/DisableLights when it changes segment; Stop restores. [SPECT] "level lights/chunks now follow".
- Spectate HUD: msg 200 now carries hp, energy, ammo, weapon, missile, missile ammo[8], weapon/missile levels[8], boost heat,
  overheat timer, boosting, 10/s. Joiners send their own values (205, unreliable, 10/s); the host uses them for joiner entries
  (armor from its own copy). HUD9 writes the followed player's newest values (< 1.5 s) into its Player/PlayerShip for the DrawHUD call
  only and restores them. [SPECT] "HUD drawn with netId=..'s live values".
- Main menu version label 20% of the UI height lower (OptionsScreen M5).
- Reviewed by a separate agent before release: fixed report staleness (shared Stats object), lockdown clear flag, exit indices reset by
  a late CreatePlayerPathToEnd (PathPoints keeps idx/cam during EXIT), READY UP state not cleared on leave.
- Still open: buttons a joiner couldn't break (W9/W9b: no hit lines in these logs either), robot lag at long distance, reactor sound
  maybe doubled, README pickups issue (P16), per-player pickups, 4 players (this run had 4: lobby/joins/levels worked per the logs).
