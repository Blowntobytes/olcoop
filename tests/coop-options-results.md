# Co-op options + death modes results

## Run 1: 10:55, 0.3.1, RESPAWN 5 s. Mostly works
- The menu works (OPTIONS → CO-OP OPTIONS). The joiner received the host's settings ("host settings: RESPAWN after 5s").
- The joiner (netId 75) died at 11:02:37 and was respawned by the host 5 s later next to the host (28.4,27.7,-7.1). The restore ran on both
  peers. 0 repeating exceptions.
- User: "after a player died and respawned the screen was darker and the player no longer had collision".
  - Darker: `StartDying` turns off the ship lights **and headlights** (PlayerShip.cs:2799-2800), and it adds damage blur to the view. Stock
    `RestorePlayerShipDataAfterRespawn` only calls `RestoreLights()`, never the headlights, because SP never respawns mid-level.
    Also, our hide/show un-hid **every** MeshRenderer, including ones that are normally off.
    **Fix 0.3.2:** remember the headlight state at `StartDying` and toggle it back on after respawn. Clear the view damage blur/overbrighten and
    the screen fade. Hide/show only the renderers we hid.
  - No collision: the cause was not found in code. **0.3.2** re-enables the mesh and level colliders, sets the rigidbody non-kinematic with collisions
    detected, and re-syncs the detached collider. A diagnostic line `after respawn … meshCol= levelCol= kinematic= detect= layer=` is logged on every peer.
- New: **FRIENDLY FIRE** checkbox (default OFF). OFF blocks all player→player damage (shots, splash, ramming; before this a ship bump did 13 damage).
  ON lets player shots collide with teammates' ships (own ship always ignored).

## Run 2: 13:35, 0.3.2, host + 2 joiners (one after the other), RESPAWN 5→10 s, friendly fire toggled
- Dim screen fixed (user). The respawn collider diagnostics all read meshCol/levelCol True, kinematic False, detect True. The collision issue was not
  reproduced.
- Respawns worked 4 times (host once, joiner 75 twice, joiner 78 once), each placed next to a living teammate.
- **Bug: on a joiner's death the single-player death menu opened** (joiner logs: `PlayerHasDied` → `DoneLevel reason=Died` at 13:44:09 and 13:47:54).
  The host had respawned the ship, but the joiner had already left the level locally. Cause: our death handling was gated on
  `IsMultiplayerActive`, which game code reset on the joiner mid-level. The culprit was not identified; `Client.OnMatchStart` and other paths
  assign it from `IsMultiplayer` (false). **Fix 0.3.3:** our own `CoopSession.InLevel` flag drives death handling, and a per-frame watchdog
  re-enables `IsMultiplayerActive` and match PLAYING during a co-op level, logging "watchdog: … re-enabled".
- **Friendly fire inconsistent after toggling:** the layer switch logged correctly on all peers. Cause: projectiles are pooled, and
  `Physics.IgnoreCollision(shot, ownerShip)` pairs persisted when a pooled shot was reused by another player, so some shots could never hit a
  given teammate. **Fix 0.3.3:** on every Fire, ignore pairs are reset for all ships (only the owner's own ship is ignored).

## Run 3: 14:57-15:05, 0.3.5 (all three logs show `[INIT] olcoop 0.3.5-coop-options`), host + 1 joiner
- User: "everything mostly working so far."
- RESPAWN 10 s (session 1): host netId 2 died 15:00:09, spectated the joiner during the countdown, respawned 15:00:19. Joiner netId 75 died
  15:00:58, spectated the host, respawned 15:01:08. Spectate camera started/stopped cleanly on both peers. **No camera NREs** in unity-join.log
  (the 0.3.3 respawn camera bug did not reproduce on 0.3.5).
- SPECTATE (session 2, joiner relaunched as netId 150): host netId 77 died 15:03:49 and followed netId 150 until the end. `[SPECT] tick` lines
  every 5 s show the camera moving with the joiner, parent=olcoop_spectate_cam, camEnabled=True. The 0.3.3 "host view froze" bug did not reproduce.
- "TEAM WIPED" at 15:04:59: **the joiner did not die**. It went to the menu at 15:04:52 (hp 67, alive) and its connection dropped at 15:04:59;
  the host, dead and spectating, then had no living player left and reset the level. Open question whether a disconnect should count as a wipe.
- Only exceptions: olmod's own `MPTweaksOnLoadoutDataMessage.ClientModifiersValid` KeyNotFoundException, once per joiner connect (host side).
- To look at: joiner logs `[DMG] player netId=150 … from=…(Local Player)` hits of 0-5 damage (self-damage source unclear), and robot relevance
  near the joiner (host dumps show only 1-7 robots active).

## Run 4: 15:41-15:53, 0.3.6 (all three logs show 0.3.6), host + joiner (joiner relaunched once)
- User: the respawn timer did not show, and neither did player names.
- Names: the host named both players (both pilots are 'NBOOB'). Nothing drew because stock `DrawMultiplayerNames` only marks a player visible
  if it's a team match and same team, or `m_show_enemy_names` is set. olmod's health bar needs a team match too. Fixed in 0.3.7.
- Timer: drawn inside `DrawMultiplayerNames` (world-overlay units, e.g. name scale 0.8, bar width 3.5), so the PVP digits were off-screen.
  Fixed in 0.3.7 by drawing from the HUD element.
- Respawns (host 15:48:45, joiner 15:49:28), Spectate wipe and Hardcore reset all behaved.
- Rejoin: the joiner returned to the main menu after the 15:50:43 Hardcore reset, asked for the level at 15:51:23 and came back in.
- Joiner Unity log: 66x `Smooth.SmoothSync.OnStartClient` NullReferenceException (olmod), not seen before; to watch.

## Run 5: 16:00-16:05, 0.3.7 (both logs show 0.3.7), host + joiner
- User: player names now visible. Respawn timer still invisible. Hardcore breaks the joiner's game when it dies.
- Timer: no `[HUD]` lines at all, so it never got to the drawing step. The host sends the countdown when a ship starts dying, and the joiner's
  LocalTick cleared it every frame until the ship was fully dead. Fixed in 0.3.8.
- Hardcore (joiner died 16:05:21): the host restarted, its reload unspawned the joiner's ship, and stock `NetworkUnspawnPlayerHandler` called
  `ExitMatchToMainMenu` ("EXIT MATCH", match NONE, game type MULTIPLAYER). Co-op death handling went off, so stock DeadUpdate ran
  `PlayerHasDied` -> `DoneLevel reason=Died` -> SP death menu, and the joiner disconnected at 16:05:30. The host had tried to send the
  level while still loading and never retried. Fixed in 0.3.8.
- Respawns (joiner 16:02:11, host 16:02:58) worked. SHOW PLAYER NAMES toggled on the joiner.

## Run 6: 16:17-16:21, 0.3.8 (both logs show 0.3.8), host + joiner
- User: no respawn timer visible (asked to follow olmod's PVP approach). Hardcore still breaks the other player's game on death.
- Timer: the drawing code now ran on both peers (`[HUD] drawing respawn timer 11`), but nothing showed in the HUD element layer. PVP draws it
  from an overlay element (MP_DEATH_OVERLAY via CreateOverlayElement at death). 0.3.9 does the same.
- Hardcore (joiner died 16:21:10): the joiner stayed connected (new C5 line). The host sent the new level once loaded (16:21:18, new Pending
  path), but the joiner never handled it (`Unknown message ID 36`). It sat in the old level until closed at 16:21:59. 0.3.9: the joiner
  leaves on the restart notice and rejoins from the main menu.
- Respawn mode respawns (host 16:19:27, joiner 16:20:04) worked.

## Run 7: 16:41-16:47, 0.3.9 (both logs show 0.3.9), host + joiner
- User: **respawn timer works correctly.** Hardcore, joiner died: no main menu, the screen looked frozen; about 10 s later it came back into
  the new level but stuck in a "cinematic panorama" view.
- Log: the joiner ran DoneLevel at 16:44:48 but the level never unloaded (frozen picture). The host's scene at 16:44:54, sent right after
  clearing the joiner's old player, was dropped again. The joiner's 10-s re-ask at 16:45:02 loaded it. The ship then flew normally (positions
  change), so the view was the camera: the stock MP death-pause flag left set by the Hardcore death. Fixed in 0.3.10.
