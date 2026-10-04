# Phase 2b world sync results

## Run 1: 19:46-19:55, 0.4.0 (both logs show 0.4.0; 0.4.1 was installed mid-run, identical world sync)
- User: the joiner can't shoot a button, only the host can. When the joiner picks up a key, the audio prompt plays only for the host.
- World registry: 33 scripts + 6 destroyables, hash D0A21531 on both peers, matched.
- Worked (log): two buttons broken by the host (19:54:08, 19:54:58) broke on the joiner, and their ScriptDoorOpen ran on the joiner; comm
  messages (19:49:45, 19:53:02) ran on the joiner once each, local copies suppressed; door open + activate/deactivate object scripts replayed.
- Joiner buttons: only the host applies button damage, and no joiner hit ever reached it. 0.4.2 forwards the joiner's hits.
- Key prompt: Player.AddKey (sound 369 + popup) only runs on the host. 0.4.2 plays the sound on joiners.
