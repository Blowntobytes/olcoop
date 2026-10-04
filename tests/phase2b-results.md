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
