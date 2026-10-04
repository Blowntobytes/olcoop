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
