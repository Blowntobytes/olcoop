# Phase 2b world sync results

## Run 1: 19:46-19:55, 0.4.0 (both logs show 0.4.0; 0.4.1 was installed mid-run, identical world sync)
- User: the joiner can't shoot a button, only the host can. When the joiner picks up a key, the audio prompt plays only for the host.
- World registry: 33 scripts + 6 destroyables, hash D0A21531 on both peers, matched.
- Worked (log): two buttons broken by the host (19:54:08, 19:54:58) broke on the joiner, and their ScriptDoorOpen ran on the joiner; comm
  messages (19:49:45, 19:53:02) ran on the joiner once each, local copies suppressed; door open + activate/deactivate object scripts replayed.
- Joiner buttons: only the host applies button damage, and no joiner hit ever reached it. 0.4.2 forwards the joiner's hits.
- Key prompt: Player.AddKey (sound 369 + popup) only runs on the host. 0.4.2 plays the sound on joiners.

## Run 2: 20:06-20:12, 0.4.2 and Run 3: 20:39-20:51, 0.4.3 (logs show those versions), host + joiner
- Joiner shots break buttons: host logged `destroyable 4 hit dmg=13.0 type=ENERGY by netId=75 (joiner ship on host)` (20:07:45, 20:40:34)
  and the joiner replayed the destruction. The host sees joiner shots by itself; the 0.4.2 forwarding wasn't needed for that.
- User (run 3): the host's shots aren't visible to the joiner. Cause: stock FireProjectile only sends player shots to clients when
  GameplayManager.IsMultiplayer. Fixed in 0.4.4 (W7).

## Goliath run (0.4.5, log only, ~22:16)
- Exit sync fired (22:16:57). The boss lockdown ran only on the host: ScriptLockdownBoss was on the host-only list. Fixed in 0.4.6.

## Goliath run (0.4.6, 22:27-22:30; both logs show 0.4.6-world protocol 9)
- Boss lockdown: host ran ScriptLockdownBoss and the joiner ran it on the host's word (22:27:38). The regroup logged "regrouping" but moved
  nobody: the joiner was inside the 25-unit limit. User: distance must not matter, or a player can be locked out of the room. 0.4.7.
- Exit: host started the exit at 22:29:17.9 and moved the joiner next to itself; the joiner started the same sequence 20 ms later.
  Host finished in 14.4 s, joiner in 26.6 s ("Took this long to exit the level"). The joiner's flight only completed ~13.7 s after the
  host's netcode went off (22:29:32): Client.ReconcileServerPlayerState kept rewinding the joiner's ship to the host's copy. User: it
  didn't play properly at first, then did. 0.4.7 skips reconciliation on joiners during EXIT.
- After the exit the host sat on the level summary (MENUS from 22:29:32) and the joiner got no notification. 0.4.7 adds host status 188
  and a persistent status line.

## Run 23:00-23:05, 0.4.7 (both logs show 0.4.7-world protocol 10), Goliath from a saved game, then sp_titan_06
- Exit: user says the exit sequence looks better. Joiner flight 23:03:04.8 -> 23:03:19.4 (14.5 s, was 26.6 s); corrections paused (F14).
- Status line: WAITING (23:03:19.4) -> LEVEL SUMMARY (19.9) -> STARTING NEXT LEVEL (23:03:52.2); joiner loaded sp_titan_06 at 23:04:07.
- Lockdown: host moved the joiner next to itself (23:01:54.6), joiner applied it.
- Problem: the joiner spawned at the level start. The host continued a saved game, so its ship was at the checkpoint by the boss room
  (54, 2, 86); all 12 spawn offsets around it were rejected (6 outside level, 6 no room). User could not test the lockdown pull from far
  away as intended. 0.4.8 adds a segment-graph fallback near the host.

## Run 23:18-23:21, 0.4.8 (both logs show 0.4.8-world protocol 10), Goliath from a saved game
- User: "That worked pretty good." Joiner spawned at (50.7, 2.6, 86.6) next to the host at the checkpoint (close offsets, no fallback
  needed this time). Lockdown regroup moved the joiner (23:19:07). Joiner-triggered exit: host moved itself next to the joiner, both
  exited; status 1 then 2; next level sp_titan_06, joiner spawned next to the host.
- User: after the cutscene the joiner keeps spinning after the ship exits the mine. Cause (IL, ExitSequenceFrame): when the exit timer
  ends it re-parents the camera, calls EscapeLevel (blocked on joiners), sets fade 0 and keeps driving the ship, every frame. 0.4.9 F17.

## 3-player run 06:33-06:45, 0.4.9 (all three logs show 0.4.9-world protocol 10)
- User: the cutscene did not go well for the 3rd ship (2nd joiner); the 2nd joiner could only spin his ship, no other control.
- Logs: lockdown pulled both joiners (06:36:00, 06:40:41); joiner 1 completed the exit (to (271.7, 196.2, 75.5)), got the black hold
  (F17) and the next level. Joiner 2 (TESTEE) stalled at (111.4, 19.1, 79.4) for ~30 s and never reached "level complete".
- Cause: both joiners got the same position every time: level load after the host restart (06:38:16, both (-16.3, 41.2, 28.6)),
  sp_titan_06 (06:42:21, both (-5.0, 9.0, -67.9)), respawn (06:37:19), lockdown (06:40:41), exit (06:41:29: all spots rejected,
  blind fallback 4 u behind the host). In the levels with stacked spawns both joiners barely moved for minutes; in the 06:35 load
  (spawns 3 u apart) they flew normally. 0.4.10 adds an occupancy check and removes the blind fallback.

## Runs 06:55 (0.4.10, 3 players) and 07:03/07:07 (A/B: 0.4.10 and 0.4.9, host alone): "every ship can only rotate slowly, not move"
- Not the mod: 0.4.9 behaved the same, also with the host alone. All affected windows used pilot TESTEE (host pilot was OBSERVERB2B in
  every earlier run). Control bindings are per pilot (testee.xconfigmod). Also explains the 06:33 run's 2nd joiner (TESTEE).
- The stacked-spawn fix in 0.4.10 is still valid (stacking is in the logs), but its effect on "stuck" ships was over-claimed.

## 3-player run 07:11-07:16, 0.4.10 (all three logs 0.4.10-world protocol 10), working pilot in every window. User: "that worked better"
- Spawns apart: joiners at (51.0, 2.2, 87.6) and (61.0, 1.3, 83.1). All three ships moved.
- The boss lockdown fired 26 ms after the joiners spawned, "triggered near netId=92": joiner 2 spawned inside the boss trigger. Regroup
  moved host and joiner 1 next to it, on separate spots. Respawns placed apart (one via the segment fallback, 10.5 u).
- Exit (joiner 2 first, 07:15:45): no free spot next to joiner 2 at the exit door, so host and joiner 1 exited from where they were
  (0.4.10 rule). All three completed: joiner 2 in 14.5 s, joiner 1 in 18.4 s, host 18.9 s; both joiners black hold + status 1.

## Runs 07:38 (0.4.11) and 07:44 (0.4.12), 3 players (all logs show the right version)
- Loadout reached the host on both joiners (07:47:30, upgrades=boost,headlight). Both joiners started with the level default loadout
  (IMPULSE/FALCON, wlevel 1,1,1,0,1,0,0,0, mlevel 1,1,1,1,0,0,0,0, ammo 200, missiles 10/60/8/24) - nothing carried from earlier levels.
- Exit line-up placed ships 4/7/10 u behind the exiting player (07:41:20, 07:49:44). User: with a joiner first the pipe clogs.
- unity-host.log: 278 "Fire packet dropped, client is bursting" for OBSERVERB2B (joiner) shots. olmod MPSniperPacketsServerHandlers
  .OnSniperPacket times shots with NetworkMatch.m_match_elapsed_seconds, which only ProcessPlaying advances, and only in an MP scene.
- User: a picked-up Devastator was in the joiner's inventory but could not be selected. No log covered missile state; 0.4.13 adds it.
  IL: pickup on the host calls UnlockMissile (-> RpcSetMissileLevel) + AddMissileAmmo (olmod sniper: PlayerAddResourceMessage 135).

## Run 08:01-08:11, 0.4.13 (all three logs 0.4.13-world protocol 12), 3 players
- unity-host.log: 0 "Fire packet dropped" (07:47 run: 278). Match clock fix works.
- Devastator (user: not selectable on a joiner): host 08:09:14 "netId=92 unlocked missile DEVASTATOR level=LEVEL_0", "+1 DEVASTATOR -> 1".
  Joiner: mlevel slot 5 went 0->1 at 08:09:14 (unlock arrived) but missile ammo slot 5 stayed 0 all level. Ammo didn't arrive.
- Carry-over: joiner kept DEVASTATOR selected/creeper 2/energy 92 into the next level, but the new level had unlocked FLAK
  (wlevel 1,1,1,0,1,1,0,0) and the restore overwrote it (1,1,1,0,1,0,0,0). 0.4.14 merges.
