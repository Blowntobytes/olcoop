# Single-player assumptions to remove (to-do list)

There are 648 lines across 278 methods in 46 files that read `GameManager.m_local_player` or
`m_player_ship`, or otherwise assume one player. The row-level detail is in `recon-world.md` §2.
MUST rows are combat, AI or objective code that has to change for co-op. Display-only rows (HUD, menus,
camera) mostly stay as they are, because each client renders its own player.

| # | Area | Key locations | Fix | Phase | Status |
|---|---|---|---|---|---|
| A1 | Global static robot target | `Robot.c_target_transform` / `c_target_transform_position` / `m_player_ship` (Robot.cs:628-633), set at RobotManager.cs:298 | Per-robot target chosen by `CoopTargets` and swapped into the statics around each robot's update | 3 | todo |
| A2 | Robot AI modes/aim/fire/pathing | ~60 Robot.cs methods (UpdateTargetInfo, MaybeFire, AimTowardsTarget, DoMode*, CreatePathToPlayer…) | Covered by A1 if done by swapping statics per robot; verify each | 3 | todo |
| A3 | Wake/alert distance | MaybeAwakenRobotDueToHeadlightOrBoost, ModifiedAwakenDist, MaybeExitAsleep, AlertNearbyRobots, RobotManager.MaybeAwakenRobots | Minimum over all players | 3 | todo |
| A4 | Melee/claw/charger push | ApplyDetonatorImpulse, ApplyClawImpulse, DoModeClaw, ApplyChargerImpulse | Push the ship that was hit | 3 | todo |
| A5 | Damage, kill credit, drops | Robot.ApplyDamage (11953), MaybeDropItem (11330) | Host-only; credit the `DamageInfo.owner` player | 3 | todo |
| A6 | Guidebot | SpawnGuideBot, DoModeGuidebot, Pathfinding.Find*NearPlayer | None in co-op at first; per-player later | 5 | todo |
| B1 | MP early return in RobotManager.Update | RobotManager.cs:320 | Host: run SP path in COOP. Client: skip AI | 2 | todo |
| B2 | Segment culling by local ship | RobotManager *InRelevantSegment, UpdateChunkActivationDueToPlayerMovement, ChunkManager | Host: union of player segments. Client: own segment for visuals | 2 | todo |
| B3 | Matcens | RobotMatcen UpdateStatic / MatcenFrame distance gate | Host-only, minimum distance over players | 2 | todo |
| C1 | Enemy homing | Projectile.FindPlayerTarget (414), GetBestPlayerTarget (1143) | Over all ships | 3 | todo |
| C2 | Robot projectiles not networked | ProjectileManager.cs:254 sends only Player-owned shots | New RobotFire message | 2 | todo |
| C3 | Reactor turret own target | PropReactorTurret.cs:69-79 | Nearest player | 3 | todo |
| D1 | Level load only from MultiplayerMission | NetworkManager.LoadScene (NetMgr:111) | Resolve in StoryMission when COOP | 1 | todo |
| D2 | Story mission missing on dedicated server | GameManager.InitializeMissionList (:1634) | Register cronus when headless | 1 | todo |
| D3 | Spawn position | LevelData.cs:154 (LegacyPlayerStart), LevelStart.cs:46, Server.OnAddPlayerMessage | Slots around the level start | 1 | todo |
| D4 | MP extras | PowerupLevelStart, MaybeSpawnPowerup, mode file, MP loadouts, MaybeEnd* | Suppress in COOP | 1 | todo |
| D5 | Reactor/escape | ReactorDestroyed (1185), EscapeStart, EscapeUpdate (1281) kills only the local ship | Host-run timer, broadcast; kill everyone not escaped | 4 | todo |
| D6 | Exit/teleport | DoorExit.OnTriggerEnter, ExitSequenceStart/Frame, TeleportSequence* | Per player; level ends when all living players exit | 4 | todo |
| D7 | Death | PlayerHasDied → DoneLevel(Died) | Team respawn rules | 4 | todo |
| D8 | Cryotubes / ending | CollectCryotube (2357), m_cryos_picked_up*, debrief2 when < 20 (936) | Host counts, broadcasts; shared tally | 4 | todo |
| D9 | XP/upgrade points | EscapeLevel (2045), ReactorDestroyed | Give to every player | 4 | todo |
| D10 | Secret levels | SwitchToSecret / ReturnFromSecret | Whole party moves | 4 | todo |
| D11 | Level scripts | ScriptLevel1/12/16, ScriptTeleportOut, SovereignExploding, TriggerBase.OnTrigger | Host runs scripts; broadcast links | 2/4 | todo |
| D12 | Doors/keys/switches | DoorAnimating 274/300, SecurityManager 111, TimedSwitch 85, AlienPower 179 | Any player; keys shared | 2 | todo |
| E1 | Pause | GameManager pause (1053), GamePaused | Already off in MP; keep | 1 | todo (verify) |
| E2 | Save | SaveLoad, GameplayManager serialize | Host-only save between levels | 4 | todo |
| E3 | Player damage multiplier | PlayerShip.ApplyDamage MP branch drops difficulty scaling | Use SP `dl_player_damage[difficulty]` in COOP | 3 | todo |
| E4 | Entity IDs | robot_id from scene search order | Deterministic co-op IDs | 2 | todo |
