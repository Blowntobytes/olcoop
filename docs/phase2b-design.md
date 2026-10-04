# Phase 2b: world sync (doors, keys, destroyable buttons, scripts, audio prompts)

## How Overload drives level logic
- Level logic is an event graph. Sources (TriggerBase.OnTrigger, TimedSwitch/AlienPower.ActivateScripts, Item pickups, Robot.StartExploding,
  Destroyable.ExplodeNow) call `SendMessage("ActivateScriptLink")` on their `c_go_link` objects.
- 32 Script types receive it (`ActivateScriptLink()`; ScriptLevel1 takes an index). Each runs `ActivateScript()` at once, or sets
  `should_activate` and runs after `delay` in `ScriptBase.UpdateStatic`. Scripts open/lock/unlock doors, play comm messages (audio prompts),
  objective/tutorial text, music fades, forcefields, lights, objects, lockdowns, matcens, robot ambushes, checkpoint saves, secret level, teleport out.
- Before 0.4 every peer ran the graph on its own (triggers fire for any ship collider on every peer), so peers diverged whenever they saw
  different events: robots die only on the host, destroyables were damaged per peer, etc.
- Doors open on collision with a ship/robot on each peer. Locked doors compare `DoorBase.LockType` with `GameManager.m_local_player.m_unlock_level`.
- Keys: with co-op netcode on (`IsMultiplayerActive`), `Item.OnTriggerEnter` does a real pickup only on the server (clients `TryFakePickup`).
  So `Player.AddKey` runs on the host's copy of whoever picked the key up; a joiner's own `m_unlock_level` never changes.

## Design (host-authoritative)
- Registry: every ScriptBase and Destroyable in the loaded scene, keyed `S|<hierarchy path>|<type>` / `D|<hierarchy path>`, sorted,
  FNV-1a hash. Built at `RobotManager.InitializeForNewLevel` on both peers. Joiner sends its hash (182); host replies with its hash plus
  catch-up (183). Mismatch => joiner keeps running its own level logic (old behaviour) and logs it.
- Scripts (177): host postfix on every `Script*.ActivateScriptLink` sends the script id. Joiner (matched) blocks every local activation
  and runs only host-sent ones, except host-only types (matcens, robot attacks, lockdown robot/boss/master, reveal robot, counters
  OnCount/OnDestroy/OnPickup/OnRobotKills, checkpoint save, secret level, teleport out, analytics, level scripts).
- Destroyables (178): damage and destruction only on the host; joiner replays `StartExploding` on the host's word.
- Keys (180): host sends the team's best `m_unlock_level` (max over all Player objects) whenever it changes; every peer raises all its
  players to it, calls `SecurityManager.UpdateSecurityLevel` and `DoorAnimating.UpdateLock`; joiner shows "SECURITY KEY ACQUIRED BY A TEAMMATE!".
- Late join catch-up: destroyed destroyables, history of state scripts (not comm/objective/tutorial/music: those are live only), key level.

## Known gaps (0.4.0)
- Joiner missiles/ammo weapons don't reach the host (loadout sync phase), so a button hit only by those won't break.
- TimedSwitch/AlienPower visuals on the joiner don't change (their scripts do run).
- Key items and other placed pickups stay visible on the joiner after someone else took them (fake pickup only).
- ScriptLevel1 (index entry point) is not covered; it runs locally on a joiner in level 1.
- Door open/close timing is still per peer (collision); only script-driven door state and locks are host-driven.
