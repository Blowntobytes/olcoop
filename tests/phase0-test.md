# Phase 0 manual test: read-only instrumentation

**Goal:** prove the mod loads, changes nothing, and logs the state we will need to sync.
**Machines:** one PC is enough. Test 3 can optionally use a second machine or a second game instance.

## Setup
1. Run `install.bat` from the build folder. It copies `Mod-olcoop.dll` and `olcoop.bat` into the Overload folder.
2. Start the game with `olcoop.bat` (not Steam's Play button, and not plain `olmod.exe`).

## Test 1: mod loads, vanilla untouched
| Step | Expected |
|---|---|
| Reach the main menu | The game looks and behaves exactly as with plain olmod. |
| Open `Overload\olcoop_logs\` | There is a new `olcoop-<date>-<time>.log` whose first line contains `[INIT] olcoop 0.0.1-phase0 protocol=1 game=…`. |
| Quit, start with plain `olmod.exe` | No new log file is created, which shows the mod is inactive without `-modded`. |

## Test 2: single-player campaign level (one reactor level)
| Step | Expected |
|---|---|
| New campaign game, level 1, any difficulty | The log has `CreateNewGame mission=cronus/BUILT_IN level_num=0`, then `LoadLevel scene=…`, then `DUMP StartLevel (post)`. |
| Fly for about a minute and fight some robots | A `DUMP periodic` entry appears every 5 s. `robots total=` stays steady, `active=` changes as you move, and there are `[DMG] … from=robot:…` lines when you're hit and `[ROBOT] ExplodeNow` lines on kills. |
| Destroy the reactor | The log has `[OBJ] ReactorDestroyed`, then `EscapeStart timer=…`, then `mustEscape=True escapeTimer=` counting down in the dumps. |
| Exit the level | The log has `ExitSequenceStart`, then `EscapeLevel`, then `DoneLevel reason=Escaped`. |
| Optional: die once | The log has `PlayerHasDied` and `DoneLevel reason=Died`. |

## Test 3: olmod LAN match (baseline for Phase 1)
| Step | Expected |
|---|---|
| Multiplayer → Create LAN game, any map, start | The log shows `match LOBBY -> …`, then `NetworkManager.LoadScene mp_…`, then `OnStartLocalPlayer {… netId=N local=True …}`, then `match … -> PLAYING`. Dumps show `isMP=True robots total=0` and `netPlayers=1` (2 if a second instance joins). |

## Send back
Zip or attach the `olcoop_logs` folder, or just tell me where it is, because I can read it straight from your
Overload folder. Also note anything that looked different from plain olmod.

## Result (fill in)
- [ ] Test 1  - [ ] Test 2  - [ ] Test 3. Unverified until the logs are reviewed.
