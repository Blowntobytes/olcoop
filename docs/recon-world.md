# Overload co-op recon: world, AI, objectives

Source: `refs/Assembly-CSharp` (decompiled). Paths below are relative to it. Line numbers are the start of the method (from `grep -n`).

## 0. Headline facts
- **`SaveLoadBase : NetworkBehaviour`** (`Overload/SaveLoadBase.cs:6`). So Robot, RobotMatcen, Item, PropBase (Reactor, Forcefield, TimedSwitch, AlienPower, PropCryotube, ...), DoorBase, TriggerBase and ScriptBase are already NetworkBehaviours, but they have **no SyncVars/RPCs** and `OnSerialize` is a stub. State persists only through JSON `Serialize()`/`Deserialize()` for save games.
- **AI has one global target.** `public static Transform Robot.c_target_transform` (`Robot.cs:628`), cached as `static Vector3 c_target_transform_position` (`:631`) and `private static PlayerShip m_player_ship` (`:633`, set in `Start()` at `:1949`). `RobotManager.InitializeForNewLevel` assigns `Robot.c_target_transform = GameManager.m_player_ship.transform` (`RobotManager.cs:298`). Robot.cs has 98 uses of `c_target_transform` and 164 uses of `m_target_pos/diff/dist`.
- **In MP, robots are not simulated.** `RobotManager.Update()` (`RobotManager.cs:317`) returns early when `GameplayManager.IsMultiplayer` (line 320), after only chunk/light activation. olmod only uses RobotManager for MP spawnable lists and patches `Robot.ExplodeNow` for the Boss2B fix (`olmod/GameMod/Boss2B.cs:12`) and `RobotMatcen.Start` for matcen HP (`MatcenHp.cs:15`). It has no networked robot AI.
- **Single-player assumption count:** 648 matching lines for `m_local_player|m_player_ship|GameManager.m_local` in 46 files, spread over 278 distinct methods. Of those, 231 lines in 83 methods are UI, Menu or Challenge. Robot.cs alone has 105 lines in 61 methods. On top of that, Robot.cs has the 98 implicit `c_target_transform` uses.

## 1. Robot lifecycle
| Stage | Where | Notes |
|---|---|---|
| Level placement | Robots are prefab instances in the scene, tagged `"Robot"`. `UpdateStaticManager.GatherObjects(GameObject.Find("_container_placed_entities"))` at `Overload/GameplayManager.cs:873` (StartLevel) | No runtime spawn for placed robots |
| Init per level | `RobotManager.InitializeForNewLevel(bool loading_saved_game)` at `RobotManager.cs:271`, called from `GameplayManager.cs:932/955` | Sets the global target (`:298`) and builds the lists |
| Master list | `RobotManager.m_master_robot_list` (`:145`), filled by `InitMasterRobotList()` (`:866`) using `FindGameObjectsWithTag("Robot")`. `robot_id = i` is the scene-search index, which is **not stable across peers** | `m_robot_list` (`:143`) is rebuilt by `NewUpdateRobotList()` (`:847`) whenever chunks change |
| Matcen spawn | `RobotMatcen.UpdateStatic()` (`RobotMatcen.cs:187`) calls `MatcenFrame()` (`:290`), which gates on distance to `GameManager.m_player_ship` (`:300`). That calls `SpawnRobot(Vector3,Quaternion)` (`:339`), which calls `static Robot.SpawnRobotFromMatcen(Vector3, Quaternion, int, int, RobotMatcen)` (`Robot.cs:8116`) | The random type choice uses `UnityEngine.Random`. The robot is seeded with the player position and segment. Cap is `m_matcen_max_robots_alive`. |
| Generic spawn | `RobotManager.SpawnNewRobotNoParent(Vector3 pos, int etype, bool position_is_valid=true)` (`:922`) runs `ActivateRobot(force_active:true)` and adds the robot to the master list. Also `SpawnRobotType(EnemyType,int segnum,Vector3,Quaternion)` (`:959`), `SpawnRobotMinimal(int)` (`:910`), and `Robot.SpawnGuideBot()` (`Robot.cs:8178`) | Uses plain `Instantiate`. There is no `NetworkServer.Spawn`. |
| Activation | `RobotManager.ActivateRobot(Robot, bool force_active=false)` (`:634`) calls `c_go.SetActive(...)` based on `RobotInRelevantSegment` (`:782`), which uses the player's chunk. Driven by `UpdateActiveStatusAll(bool)` (`:459`) and `UpdateActiveStatusRobotsOnly()` (`:475`) from `RobotManager.Update()` | Activation is culled around one player. For co-op it needs the union of all players' relevant segments. |
| Wake | `MaybeAwakenRobots(Vector3, int flare_id=-1)` (`:1143`), `Robot.MaybeExitAsleep()` (`Robot.cs:7393`), `ModifiedAwakenDist()` (`:7379`), `MaybeAwakenRobotDueToHeadlightOrBoost()` (`:2093`) | All are relative to the local player |
| Update | Unity `Robot.Update()` (`Robot.cs:2106`) and `Robot.FixedUpdate()` (`:2966`), gated by `RobotManager.m_AI_enabled` and the `GamePaused`/stasis checks. Global driver: `GameplayManager.Update()` → `RobotManager.Update()` (`GameplayManager.cs:1602`), then `UpdateStaticManager.UpdateStaticObjects()` and `UpdateDynamicManager.UpdateDynamicObjects()` (`:1622-1623`) | `UpdateTargetInfo()` (`:2221`) computes `m_target_pos` from the static target, or from `m_believed_player_cloak_pos` if the local player is cloaked |
| Damage → death | `public bool Robot.ApplyDamage(DamageInfo di)` (`:11953`) → `StartExploding(DamageInfo)` (`:10514`, which records stats at `:10601`) → `DyingAction()` (`:10846`) → `public void ExplodeNow()` (`:10962`) → `MaybeDropItem(ItemPrefab,float)` (`:11330`) | Drops use `Random` and depend on the local player's ammo and weapons |
| Cleanup | `UpdateActiveStatusRobotsOnly` removes null entries (`:503`). `DeactivateAllRobots()` (`:831`) | |

## 2. Single-player assumptions (grouped). MUST = AI, combat or objective code.
The proposed replacement is the same in most rows: `CoopTargets.Nearest(pos)` (nearest live, visible, non-cloaked player ship), `CoopTargets.All`, or "host-only + broadcast".

### 2a. Robot AI (Robot.cs, 61 methods, 105 lines + static target): MUST
| file:line | method | does | N-player replacement |
|---|---|---|---|
| Robot.cs:628/631/633 | static fields | Single global target transform, position and ship | Make them per-robot `m_target_ship`, re-chosen every ~0.25 s by nearest/visible/threat |
| RobotManager.cs:298 | InitializeForNewLevel | Assigns the global target | Delete. Assign per robot. |
| RobotManager.cs:317 (5) | Update | Player segment, awaken-on-fire, global target pos, MP early return | Loop over all players. Host runs the AI. Remove the MP early return for co-op. |
| Robot.cs:2221 | UpdateTargetInfo | target pos/diff/dist + cloak | Use `m_target_ship` and that player's cloak |
| Robot.cs:1946 (2) | Start | Caches m_player_ship | Remove |
| Robot.cs:2093, 7379, 7393, 9323 | MaybeAwakenRobotDueToHeadlightOrBoost, ModifiedAwakenDist, MaybeExitAsleep, AlertNearbyRobots | Wake distance/headlight from the local player | Min over all players |
| Robot.cs:2318, 2331(2), 2650 | OKToFirePlayerCloaked, MaybeFire, Boss3DefaultFiring | Fire decision | Per target |
| Robot.cs:7915, 7957, 7982, 8053, 7893 | VisibilityRaycast, ...Cloaked, UpdateVisibility, FOVPlayerToRobot, DotFromMeToTarget | LOS/FOV to the player | Per candidate player (cost: N raycasts) |
| Robot.cs:8558, 8648, 8782 | MaybeLeadTarget, AimTowardsTarget, AimTowardsTarget2 | Aim and lead the target using player velocity | Use the target ship's rigidbody |
| Robot.cs:2761, 6698, 7012, 6418, 7359, 7325, 7598, 7631, 5962, 6110, 7298, 9228 | DoPathfindMovement, DoModePathfind/Approach/Still/Searching/SearchingCloaked/Sniper/Coward, ChooseRunawaySegment, ChooseNewCloakPos, TurnTowardsPortal | Mode logic using player pos/segment | Per target |
| Robot.cs:12805, 12913, 2199, 2948, 6338 | CreatePathToPlayer, CreatePathToBelievedPlayerPosition, MaybePathfindForce, MaybeCloakedUpdateBelievedPosition, AlertHeadlightOnWhileCloaked | Pathing to the player | Per target |
| Robot.cs:5119, 5137, 5173(3), 5633(4), 8978 | ApplyDetonatorImpulse, ApplyClawImpulse, DoModeDetonator, DoModeClaw, ApplyChargerImpulse | Melee/claw/charger push the local ship | Push the hit/target ship. Host-side, then sync. |
| Robot.cs:11953 (9) | ApplyDamage | Charge-kill armor/XP and Falcon-2A instant explode read `m_local_player` | Credit `di.owner`'s Player |
| Robot.cs:11330 (15) | MaybeDropItem | Drop tables depend on local ammo/weapons | Use the killer, or the team aggregate. Host decides. |
| Robot.cs:8116, 8178(3) | SpawnRobotFromMatcen, SpawnGuideBot | Seed believed location; guidebot follows the player | Nearest player; one guidebot per player or none |
| Robot.cs:3493(2), 4064(2), 13022, 13039(2), 13048 | DoModeGuidebot, FindSegmentContainingSecurityDoor, GuidebotCantPass, FindTeleportationSeg, FindRandomNearbySegment | Guidebot/teleporter around the player | Per owner/target |
| Robot.cs:10514, 10846, 10962(4) | StartExploding, DyingAction, ExplodeNow | Stats, camera shake, audio | Stats on host; FX on all clients |
| Robot.cs:55, 10284, 10310, 10328, 11525, 12376 | Refresh, DrawSpawnMesh, DrawMeshEffect*, UpdateMaterialGlows, SetPathSegmentInfo | Visual distance LOD | Local camera. OK as is. |

### 2b. RobotManager / Matcen / Pathfinding: MUST
| file:line | method | replacement |
|---|---|---|
| RobotManager.cs:308, 459, 573(2), 727, 782, 806, 817, 822, 1101, 1289, 1332 | UpdateChunkActivationDueToPlayerMovement, UpdateActiveStatusAll, RobotAsleepOrLurking, *InRelevantSegment, ForcePlayerSegmentCompute, MaybeActivateAllRobots, MaybeForceCreateGuideBot | Relevance = union over players |
| RobotManager.cs:1143, 1193, 959 | MaybeAwakenRobots, PlayerBecameCloaked, SpawnRobotType | Per player |
| RobotMatcen.cs:187, 290, 238, 268 | UpdateStatic, MatcenFrame (distance gate), DestroyMatcen, SpewPowerups | Host only. Min distance over players. |
| Overload/Pathfinding.cs:362, 516, 532, 548, 559 | GuidebotCantPass, Find*NearPlayer | Parameterize by player |
| Overload/ChunkManager.cs:176, 380, 401, 420, 541 | ActivateChunks, lights/probes/ambient | Union for activation; lights local OK |

### 2c. Projectiles: MUST
| file:line | method | replacement |
|---|---|---|
| Projectile.cs:414 (5) | FindPlayerTarget (enemy homing) | Nearest visible non-cloaked player |
| Projectile.cs:1143 (3) | GetBestPlayerTarget | Same, over all ships |
| Projectile.cs:1312 (2) | Fire | Owner/team logic |
| Projectile.cs:518/530 | lock-on sound when `m_cur_target_player.isLocalPlayer` | Already per-player. OK. |
| Overload/ProjectileManager.cs:230 (3), 310 (2) | FireProjectile, MaybeExplodeRobotDevastator | Per owner |
| PropReactorTurret.cs:69, 79 | Start/UpdateStatic: own `c_target_transform = m_player_ship` | Nearest player |
| Destroyable.cs:421, 483; Explosion shakes `Overload/ExplosionManager.cs:285` | AboutToExplode/ExplodeNow, camera shake | Shake local; logic host |

### 2d. Objectives / level flow (GameplayManager.cs, 81 lines): MUST
| line | method | replacement |
|---|---|---|
| 858 (9), 1052, 759, 846 | StartLevel, LoadLevel, SetPendingLevelLoad, AdvanceLevel | Host-driven level load; spawn all ships |
| 1185 | ReactorDestroyed (XP to local) | Host → RPC to all; XP to everyone |
| 1281 (11) | EscapeUpdate (countdown death of local ship) | Each client kills own ship at 0; host tracks who escaped |
| 1452 (12), 1418 | Update, FixedUpdate | Split host/local |
| 2002, 2045 | DoneLevel, EscapeLevel (XP, upgrade points) | Host decides level-done when all alive players exited/escaped; give points to all |
| 2357 | CollectCryotube (flash + bonus point) | Host counts; RPC popup to all |
| 3302, 3333, 3386, 3485 | TeleportSequence*/ExitSequence* (drive local ship cinematic) | Per-player exit; wait for all |
| 766, 792 | SwitchToSecret, ReturnFromSecret | Whole party moves |
| 4119, 4173, 3640, 3445, 3469, 1399, 1442, 2707, 2764 | stats, tutorial, camera helpers, VR recenter, automap, NG+ | local |
| DoorExit.cs:7 | OnTriggerEnter → ExitSequenceStart | Per player; host aggregates |
| LevelStart.cs:46 (10) | Start: positions local ship at start | Use N start slots/offsets |
| ScriptLevel1.cs:59,127; ScriptLevel12.cs:32,96; ScriptLevel16.cs:9; ScriptTeleportOut.cs:18; ScriptDisableOcclusion.cs:18; SovereignExploding.cs:26 (6) | Scripted level/boss sequences keyed on local ship | Host-run; any/all players |
| AlienPower.cs:179 (5), AlienSecretDoorTriggerStay.cs:20, TimedSwitch.cs:85, DoorAnimating.cs:274/300, SecurityManager.cs:111, Item.cs:244/394/630/777 | Collision/trigger with "the" player, door lock checks player keys, item radius/spew rules | Any player; keys shared |

### 2e. Player/ship, HUD/UI, audio, camera, save
| group | locations | action |
|---|---|---|
| PlayerShip.cs (47) | FixedUpdateAll 3339, DyingUpdate 2989, FixedUpdateReadCachedControls 4496, cheats 4289-4397, DebugInitMissiles 5986, rearview, draw effects | Mostly already MP-aware (`isLocalPlayer`). Cheats/debug ignore. |
| Player.cs (11) | OnStartLocalPlayer 4102 sets `GameManager.m_local_player/m_player_ship` | Keep: correct per client |
| GameManager.cs (14) | Update 859, pause 1053, DoSaveGame 1102 | Pause must be disabled/host-only in co-op |
| HUD/UI: UIElement (95), UIManager (11), MenuManager (84), Automap (9), MapCamera, AutomapMarker, ChallengeManager (41) | Local display. Fine as long as the stats they read are synced. Automap reveal could be shared. |
| Audio/camera/VFX: Viewer.cs:105, ParticleSwirlPlayer.cs:26, ParticleManager.cs:114, OcclusionHacker.cs:8, RobotDebris.cs:176, LevelData.cs:126 | Local, OK |
| Save: SaveLoad.cs:303/408/484, GameplayManager serialize 2777-2988 | Host-only save; clients have no save |
| MP plumbing: NetworkMatch.cs (7), Client.cs (6), NetworkMessageManager.cs (5), Scores.cs | Existing MP, reuse |

## 3. Damage
- `public void PlayerShip.ApplyDamage(DamageInfo di)` (`Overload/PlayerShip.cs:1605`) is **already server-authoritative**: it returns if `!NetworkManager.IsServer()`. Team-damage is checked via `NetworkMatch.ShouldIgnoreTeamDamage`. SP uses `dl_player_damage[difficulty] * ARMOR_DAMAGE[...]`; MP uses only `ARMOR_DAMAGE`. For co-op, force the SP difficulty multiplier on the server.
- `struct DamageInfo` (`Overload/DamageInfo.cs:5`) has: `GameObject owner; float damage, stun_multiplier, push_force, push_torque; Vector3 pos, push_dir; DamageType type; ProjPrefab weapon; bool force_death; Robot robot_owner`.
- Delivery: projectiles call `collider.SendMessage("ApplyDamage", di)` (`Projectile.cs:622, 662, 865, 943`) or `Destroyable.ApplyDamage` (`:890`). Other paths: explosions (`Explosion.cs:198/258/292`, `ExplosionDelayed.cs:119/183/217`), singularity (`SingularityPull.cs:109/136`), Forcefield (`Forcefield.cs:111`), robot melee (`Robot.cs:5134, 5158, 9004`), matcen (`RobotMatcen.cs:389`), and `PlayerMeshCollider.cs:12`, which forwards to the ship.
- Teams: `enum ProjTeam {PLAYER, ENEMY, MP, NUM}`. Player shots use `ProjTeam.MP` when an MP scene is loaded (`PlayerShip.cs:5909`). In co-op they must be PLAYER, so they hit robots and not teammates.
- Robot damage: `public bool Robot.ApplyDamage(DamageInfo)` (`Robot.cs:11953`) returns true on the killing blow. Bosses ignore non-`"PlayerShip"`-tagged owners (`:11966`). There is **no server check**: every client would apply damage locally.
- Kill credit: there is none per player. XP goes to `GameManager.m_local_player.AddXP` (`:12036-12041`) and stats to `GameplayManager.AddStatsRobotKilled(Robot, DamageInfo)` (`GameplayManager.cs:3939`, called from `Robot.cs:10601`). Co-op needs `di.owner.GetComponent<Player>()` for credit.

## 4. Dynamic world entities (all NetworkBehaviour via SaveLoadBase unless noted; none sync)
| Class | Base | State fields (persisted via JSON `Serialize`) | Notes |
|---|---|---|---|
| Robot | SaveLoadBase | `m_hp`, AI_mode, pos/rot/vel, many AI fields | Highest bandwidth |
| RobotMatcen | SaveLoadBase | `m_matcen_activated, m_destroyed, m_invulnerable, m_spawn_timer, m_total_spawns` | Host spawns; replicate destroyed/active |
| DoorBase / DoorAnimating / DoorExit | SaveLoadBase | `m_door_open, m_active` (`DoorBase.cs:22-24`), `m_anim_state, m_security_door, m_is_secret`, timers (`DoorAnimating.cs:42-64`) | `OpenDoor(bool force_open=false, bool play_sound_if_locked=false, ...)` (`:300`) is triggered by local ship proximity |
| Forcefield | PropBase | `m_disabled` (`Forcefield.cs:14`) | |
| Reactor | PropBase | `m_destroyed, m_long_countdown` (`Reactor.cs:18-20`); calls `GameplayManager.ReactorDestroyed` (`:111`) | Has a Destroyable child |
| PropReactorTurret | PropBase | `m_done_firing` | Own target |
| TimedSwitch | PropBase | `m_timer_active, m_timer` | |
| AlienPower | PropBase | `m_timer_active, m_full_active, m_timer` | |
| PropCryotube (survivors) | PropBase | `m_has_been_collected` (`PropCryotube.cs:20`) | → `CollectCryotube(id)` |
| PropAlienContainer, PropShielded | PropBase | `m_destroyed` | |
| AlienWarp, EnergyCenterEntrance, PropAlienStasis, PropGeneric/Monitor/EmLight/SimpleFan | PropBase | small | Energy center refill is per player |
| TriggerBase → TriggerEnter/Exit/Stay/Warper | SaveLoadBase | `m_one_time`, `m_repeat_delay`, `m_player_weapons`; Warper `m_active` | Fires level scripts. Host must evaluate for any player ship. |
| ScriptBase + ScriptLevelN | SaveLoadBase | per-script state | Lockdowns/ambushes live here and in trigger→matcen links |
| Item | SaveLoadBase | `m_amount, m_super, m_secret, m_index, m_respawning` | Pickups are **already partly networked**: `OnTriggerEnter` (`Item.cs:428`) has server/isLocalPlayer branches (`:446, :536`), and MP items use `NetworkServer.Spawn` (`Overload/NetworkSpawnItem.cs:39`). Robot drops use `Item.Spew` (local). |
| Destroyable | **MonoBehaviour** | `m_hp, m_alive, m_dying` | Needs an ID-based sync |
| AlienSecretDoor, DoorAlienController, SecurityManager, BasicTrigger | **MonoBehaviour** | effects / security level | SecurityManager level follows the escape state |
| SimpleShield, PropEmpty | PropBase | – | static |
Existing NetworkBehaviours with real sync: only `Player`, `SmoothSync`, `MonsterBall*`.

## 5. Objectives
| Objective | Evaluated | State | Saved |
|---|---|---|---|
| Objective type | `LevelCustomInfo.Objective` (enum in `LevelCustomInfo.cs`: NORMAL, DESTROY_BOTS, SECURE_CRASH, LEVEL_16, ...); prompt in GameplayManager ~1717 | static | level data |
| Reactor → countdown | `Reactor` dies → `GameplayManager.ReactorDestroyed(bool long_countdown=false)` (`:1185`) sets `MustEscape`, `Emergency`, `EscapeBlast`, and `EscapeStart(dl_countdown_time[diff])` | statics `EscapeTimer`, `m_escape_count`, `MustEscape` | `escape_count` etc. (`:2777`) |
| Countdown tick/death | `EscapeUpdate()` (`:1281`); at timer 0 the local player dies → `DoneLevel(DoneReason.Died)` (`:2148`) | | |
| Exit | `DoorExit.OnTriggerEnter` (`DoorExit.cs:7`) → `ExitSequenceStart()` (`:3386`) → `ExitSequenceFrame()` (`:3485`) → `EscapeLevel()` (`:2045`) → `DoneLevel(DoneReason.Escaped)` (`:2002`). Teleport variant: `TeleportSequenceStart/Update` (`:3302/3333`) | `enum DoneReason {Escaped, Died, Secret, Quit}` | |
| Kill all robots | DESTROY_BOTS: the trigger appears to come from level scripts/robot counts (`RobotManager.GetTotalRobotCount()` `:1450`) | | |
| Boss | `Robot.m_is_boss`; boss levels scripted in `ScriptLevel12.cs`, `ScriptLevel16.cs`, `SovereignExploding.cs` | | |
| Survivors | `CollectCryotube(int id)` (`:2357`): `m_cryos_picked_up`, `m_cryo_pickup_mask`; mission totals `m_cryos_picked_up_mission`, `m_num_cryos_mission` | | `:2856-2863` |
| Ending branch | `m_use_debrief2 = m_cryos_picked_up_mission < 20` (`:936`, level 15). Achievement at 44 (`:1967`) | | serialized with game |
| Secret level | `SwitchToSecret`/`ReturnFromSecret` (`:766/792`) → `DoneLevel(Secret)` | | |
| Progress save | `GameManager.DoSaveGame` (`Overload/GameManager.cs:1102`), `SaveLoad.SerializeGame(bool)` (`Overload/SaveLoad.cs:303`), which walks all SaveLoadBase `Serialize()` | | Host only |

## 6. Robot counts / bandwidth
- Level files are not in refs, so counts are estimates: a typical campaign level places ~40–90 robots, plus matcens (≤ `m_matcen_max_robots_alive`, default 4 each). Only robots in "relevant segments" are active (`ActivateRobot`). Typically 5–25 are awake at once.
- Per-frame cost per active robot: `UpdateTargetInfo`, a visibility raycast (`VisibilityRaycast`), and the mode logic in `FixedUpdate`; pathfinding is periodic. With N players, target selection adds up to N raycasts per robot per selection tick, so throttle it.
- Bandwidth estimate: active robot snapshot ≈ pos(12)+rot(8 compressed)+vel(6)+hp/mode(4) ≈ 32 B. 25 robots × 20 Hz × 32 B ≈ 16 KB/s per client, before delta and dormancy. Firing/death/door/item events are rare reliable messages. This fits easily for 3 players.
