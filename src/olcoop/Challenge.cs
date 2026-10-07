// Challenge mode in co-op (0.7.0, phase "challenge").
//  Stock challenge mode is single-player: ChallengeManager.Update (run from GameplayManager.Update while IsChallengeMode) spawns the
//  robots, counts score/kills/time, awards weapon upgrades every 25 (countdown: 20) kills and ends the run through the local player's
//  death (PlayerHasDied -> DoneLevel(Died) -> CHALLENGE_RESULTS). In co-op:
//   - host: runs ChallengeManager as stock; robots it spawns go through the mod's dynamic-spawn sync (Robots.cs P2). Each spawn is
//     placed around a random living player (spawn choice reads GameManager.m_player_ship). Score, kills, time and combo go to joiners
//     4x a second (210); every kill upgrade is sent (211) so every player gets one; the run ends for everyone (212) when the countdown
//     runs out or the co-op death rules say the run is over (team wiped / hardcore death) - see CoopDeath.DoReset.
//   - joiner: never runs the challenge simulation (it would spawn its own robots); shows the host's numbers, counts the countdown
//     down between updates, plays the last-10-seconds beeps, applies kill upgrades to its own ship and opens the results screen
//     when the host ends the run.
//   - menus: the host picks a level from CHALLENGE on the CO-OP screen. When it reaches the briefing (loadout) screen it tells
//     joiners (209: level, countdown mode, difficulty); a joiner in the main menu is taken to the same briefing, picks its own
//     loadout and presses START, which marks it ready (the host's level start waits for ready players, PostLevel.HostReady).
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Challenge
{
    public class ChallengeInfoMsg : MessageBase
    {
        public int level; public bool countdown; public int difficulty;
        public override void Serialize(NetworkWriter w) { w.Write(level); w.Write(countdown); w.Write(difficulty); }
        public override void Deserialize(NetworkReader r) { level = r.ReadInt32(); countdown = r.ReadBoolean(); difficulty = r.ReadInt32(); }
    }

    public class ChallengeStateMsg : MessageBase
    {
        public int score, kills; public float time, combo; public byte endReason;
        public override void Serialize(NetworkWriter w) { w.Write(score); w.Write(kills); w.Write(time); w.Write(combo); w.Write(endReason); }
        public override void Deserialize(NetworkReader r) { score = r.ReadInt32(); kills = r.ReadInt32(); time = r.ReadSingle(); combo = r.ReadSingle(); endReason = r.ReadByte(); }
    }

    public class ChallengeEndMsg : MessageBase
    {
        public int score, kills; public string reason;
        public override void Serialize(NetworkWriter w) { w.Write(score); w.Write(kills); w.Write(reason ?? ""); }
        public override void Deserialize(NetworkReader r) { score = r.ReadInt32(); kills = r.ReadInt32(); reason = r.ReadString(); }
    }

    public static class CoopChallenge
    {
        public const short MsgInfo = 209;     // H->J ChallengeInfoMsg (host on the challenge briefing, and before every scene send)
        public const short MsgState = 210;    // H->J ChallengeStateMsg, 4/s in a challenge level
        public const short MsgUpgrade = 211;  // H->J IntegerMessage: 0 weapon upgrade, 1 missile upgrade
        public const short MsgEnd = 212;      // H->J ChallengeEndMsg: the run is over

        /// Set while our own code calls GameplayManager.PlayerHasDied to open the results screen (D2 lets it through).
        public static bool AllowResults;
        /// Host: the run has been ended (no second end, no restart).
        public static bool Ending;
        /// Set while the host applies a kill upgrade to its own ship (so the postfix sends exactly one event per award).
        static bool s_in_update;

        public static bool InChallenge { get { return CoopConfig.Active && GameplayManager.IsChallengeMode; } }

        static IEnumerable<NetworkConnection> Joiners()
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) yield return c;
        }
        static void SendAll(short type, MessageBase m) { foreach (var c in Joiners()) c.Send(type, m); }

        // ================================================================ per-player scores (0.7.11, user: no combined tally)
        // The stock ChallengeScore / ChallengeRobotsDestroyed stay the team totals on the host (stock logic: combo, kill upgrades every
        // N kills - shared by design). Each player sees only their own: the host keeps a score per player from the robot's killer;
        // each joiner is sent its own numbers (msgs 210/212 unchanged, now per connection); the host's own are swapped in for drawing.
        static readonly Dictionary<uint, int[]> s_scores = new Dictionary<uint, int[]>();
        public static uint PendingKiller;
        public static void CreditKill(int points)
        {
            uint k = PendingKiller; PendingKiller = 0;
            if (k == 0) return; // no player did it (blast, robot friendly fire): team total only
            int[] e; if (!s_scores.TryGetValue(k, out e)) s_scores[k] = e = new int[2];
            e[0] += points; e[1]++;
        }
        public static int[] ScoreOf(uint netId) { int[] e; return s_scores.TryGetValue(netId, out e) ? e : new int[2]; }
        static uint NetIdOf(NetworkConnection c)
        {
            if (c == null || c.playerControllers == null) return 0;
            foreach (var pc in c.playerControllers)
                if (pc != null && pc.gameObject != null) { var pl = pc.gameObject.GetComponent<Player>(); if (pl != null) return pl.netId.Value; }
            return 0;
        }
        public static bool HostOwnScore(out int score, out int kills)
        {
            score = kills = 0;
            if (!CoopConfig.IsHost || !InChallenge || GameManager.m_local_player == null) return false;
            var e = ScoreOf(GameManager.m_local_player.netId.Value); score = e[0]; kills = e[1]; return true;
        }
        static string ScoreList()
        {
            var parts = new List<string>();
            foreach (var kv in s_scores) parts.Add("netId " + kv.Key + ": " + kv.Value[0] + " (" + kv.Value[1] + " kills)");
            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        public static void ResetForLevel() { s_scores.Clear(); PendingKiller = 0; JoinerLoadoutGiven = false; Ending = false; s_end_pending = null; s_next_state = 0f; s_end_alert = false; s_upgrades_sent = 0; }

        // ================================================================ host: menus
        static float s_next_info;
        static MenuState s_last_menu;
        static string s_last_key;

        public static ChallengeInfoMsg CurrentInfo()
        {
            var li = GameplayManager.Level;
            int idx = li != null && GameManager.ChallengeMission != null ? GameManager.ChallengeMission.FindLevelIndex(li.FileName) : -1;
            return new ChallengeInfoMsg { level = idx, countdown = ChallengeManager.CountdownMode, difficulty = (int)GameplayManager.DifficultyLevel };
        }

        /// Every menu frame on the host: on the challenge briefing, announce the level (at once, then every 3 s for late joiners).
        static float s_results_watch = -1f, s_next_watch;
        /// Every menu frame for 6 s after the results screen was asked for: log what the game is doing (once per second).
        public static void ResultsWatch()
        {
            if (s_results_watch < 0f) return;
            float t = Time.realtimeSinceStartup - s_results_watch;
            if (t > 6f) { s_results_watch = -1f; return; }
            if (t < s_next_watch) return;
            s_next_watch = t + 0.5f;
            var cam = Camera.main;
            var ui = GameManager.m_viewer != null ? GameManager.m_viewer.c_ui_mesh_transform : null;
            var mr = ui != null ? ui.GetComponent<MeshRenderer>() : null;
            CoopLog.Write("CHAL", "results watch +" + t.ToString("F1") + "s: game=" + GameManager.m_game_state + " gameplay=" + GameplayManager.m_gameplay_state +
                " menu=" + MenuManager.m_menu_state + "/" + MenuManager.m_menu_sub_state + " resultsUi=" + UIManager.TypeExists(UIElementType.CHALLENGE_RESULTS) +
                " deathPaused=" + PlayerShip.DeathPaused + " bgFade=" + UIManager.ui_bg_fade.ToString("F1") + " timeScale=" + Time.timeScale +
                " cam=" + (cam != null ? (cam.transform.parent != null ? cam.transform.parent.name : "null") + " enabled=" + cam.enabled : "none") +
                " elements=" + UIManager.m_num_elements + " uiMesh=" + (ui != null ? (ui.parent != null ? ui.parent.name : "null") + " active=" + ui.gameObject.activeInHierarchy + " renderer=" + (mr != null && mr.enabled) +
                " dist=" + (cam != null ? Vector3.Distance(cam.transform.position, ui.position).ToString("F2") : "-") : "none") +
                " blocker=" + (GameManager.m_player_ship != null && GameManager.m_player_ship.c_bright_blocker_go != null));
        }

        public static void HostMenuTick()
        {
            ResultsWatch();
            if (!CoopConfig.IsHost || !NetworkServer.active) return;
            var ms = MenuManager.m_menu_state;
            bool entered = ms != s_last_menu; s_last_menu = ms;
            if (ms != MenuState.CHALLENGE_BRIEFING || !GameplayManager.IsChallengeMode) return;
            if (!entered && Time.realtimeSinceStartup < s_next_info) return;
            s_next_info = Time.realtimeSinceStartup + 3f;
            var m = CurrentInfo();
            if (m.level < 0) return;
            string key = m.level + "|" + m.countdown + "|" + m.difficulty;
            if (entered || key != s_last_key)
            {
                // a joiner marked ready earlier (main menu READY UP / another level) must pick this level's loadout first
                s_last_key = key;
                OlCoop.World.HostReady.ClearMarks();
            }
            SendAll(MsgInfo, m);
            if (entered) CoopLog.Write("CHAL", "host: on the challenge briefing; told joiners level " + m.level + " (" + GameplayManager.Level.FileName + ") countdown=" + m.countdown + " difficulty=" + m.difficulty);
        }

        /// Host, right before a scene send to one joiner: the challenge settings that level needs.
        public static void SendInfoTo(NetworkConnection conn)
        {
            if (!GameplayManager.IsChallengeMode) return;
            var m = CurrentInfo();
            if (m.level < 0) return;
            NetworkServer.SendToClient(conn.connectionId, MsgInfo, m);
            CoopLog.Write("CHAL", "host: challenge settings to conn " + conn.connectionId + ": level " + m.level + " countdown=" + m.countdown);
        }

        // ================================================================ host: in level
        static float s_next_state;
        static int s_upgrades_sent;

        public static void HostTick()
        {
            if (!CoopConfig.IsHost || !NetworkServer.active || !GameplayManager.IsChallengeMode || !GameplayManager.LevelIsLoaded || Ending) return;
            if (s_end_pending != null) { HostEndRun(s_end_pending); return; }
            if (Time.realtimeSinceStartup < s_next_state) return;
            s_next_state = Time.realtimeSinceStartup + 0.25f;
            foreach (var c in Joiners())
            {
                var e = ScoreOf(NetIdOf(c));
                c.Send(MsgState, new ChallengeStateMsg { score = e[0], kills = e[1],
                    time = ChallengeManager.ChallengeModeTime, combo = ChallengeManager.CMComboTimer, endReason = (byte)ChallengeManager.m_secret_end_reason });
            }
        }

        /// Host prefix of ChallengeManager.Update. False = skip the stock update this frame.
        public static bool HostUpdatePrefix()
        {
            if (Ending || s_end_pending != null) return false;
            s_in_update = true;
            if (!ChallengeManager.CountdownMode || GameplayManager.Level == null || GameplayManager.Level.IsSecret) return true;
            float left = ChallengeManager.ChallengeModeTime;
            if (left > RUtility.FRAMETIME_GAME) return true;
            var ship = GameManager.m_player_ship;
            bool hostAlive = ship != null && !(bool)ship.m_dying && !(bool)ship.m_dead;
            // first expiry with the host alive: stock "COUNTDOWN COMPLETE" (popup, slow motion, 2 more seconds); the second expiry (or a
            // dead host, where stock would wait forever for a living ship) ends the run for everyone instead of the stock self-kill
            if (ChallengeManager.m_secret_end_reason == ChallengeManager.SecretLevelEnd.NONE && hostAlive) return true;
            HostEndRunSoon("TIME EXPIRED");
            return false;
        }
        public static void HostUpdateFinalizer() { s_in_update = false; }

        /// Host: a kill upgrade was just given to the host's ship from ChallengeManager.Update - give every joiner one too.
        public static void HostUpgraded(bool missile)
        {
            if (!s_in_update || !CoopConfig.IsHost || !NetworkServer.active) return;
            SendAll(MsgUpgrade, new UnityEngine.Networking.NetworkSystem.IntegerMessage(missile ? 1 : 0));
            if (s_upgrades_sent++ < 20) CoopLog.Write("CHAL", "host: kill upgrade (" + (missile ? "missile" : "weapon") + ") at " + (int)ChallengeManager.ChallengeRobotsDestroyed + " kills; sent to joiners");
        }

        /// Host: the run is over (countdown, team wiped, hardcore death). Everyone gets the results screen.
        static string s_end_pending;
        /// From inside ChallengeManager.Update (GameplayManager.Update): end the run after this frame's update, not mid-update.
        public static void HostEndRunSoon(string reason) { if (!Ending && s_end_pending == null) s_end_pending = reason; }

        public static void HostEndRun(string reason)
        {
            s_end_pending = null;
            if (Ending) return;
            Ending = true;
            int score = ChallengeManager.ChallengeScore, kills = ChallengeManager.ChallengeRobotsDestroyed;
            CoopLog.Write("CHAL", "host: run over (" + reason + "): team score " + score + ", " + kills + " kills; per player: " + ScoreList() + "; results for everyone");
            foreach (var c in Joiners()) { var e = ScoreOf(NetIdOf(c)); c.Send(MsgEnd, new ChallengeEndMsg { score = e[0], kills = e[1], reason = reason }); }
            foreach (var c in Joiners()) c.FlushChannels();
            OpenResults();
        }

        static void OpenResults()
        {
            try { OlCoop.Death.Spectate.Stop(GameManager.m_player_ship); } catch { }
            try { OlCoop.Hud.CoopHud.ClearRespawn(); } catch { }
            try
            {
                var ls = GameManager.m_player_ship;
                if (ls != null && ((bool)ls.m_dying || (bool)ls.m_dead)) OlCoop.World.PostLevel.ClearDeathForMenus(CoopConfig.IsHost ? "host" : "joiner");
            }
            catch (Exception ex) { CoopLog.Error("CHAL results", ex); }
            // 0.7.4: do what the stock death sequence does right before and after PlayerHasDied (PlayerShip.DeadUpdate, 3115-3135):
            // ship no longer dying/dead, HUD gone, menu background on; then camera back on its own mount (m_camera_parent) and reset.
            // 0.7.3 put the camera on the cam controller and left the ship 'dead' - the results screen still didn't show (18:54 run).
            var ship = GameManager.m_player_ship;
            try
            {
                if (ship != null)
                {
                    if (ship.c_level_collider != null) ship.c_level_collider.enabled = true;
                    if (ship.c_mesh_collider != null) ship.c_mesh_collider.enabled = true;
                    if (ship.c_rigidbody != null) { ship.c_rigidbody.angularVelocity = Vector3.zero; ship.c_rigidbody.velocity = Vector3.zero; ship.c_rigidbody.drag = 2.5f; ship.c_rigidbody.angularDrag = 5.5f; }
                    ship.m_death_stats_recorded = false;
                    ship.m_dying = false; ship.m_dead = false;
                    try { OlCoop.Death.CoopDeath.HideShip(ship, false); } catch { }
                }
                PlayerShip.DeathPaused = false;
                UIManager.DestroyType(UIElementType.HUD, true);
                UIManager.ui_bg_fade = 2f;
            }
            catch (Exception ex) { CoopLog.Error("CHAL results pre", ex); }
            AllowResults = true;
            try { GameplayManager.PlayerHasDied(); }
            finally { AllowResults = false; }
            try
            {
                UIManager.ShowCinematicBars(false); UIManager.SetScreenFade(0f); UIManager.SetOverlayAntiAlias(false);
                if (ship != null && ship.c_camera_transform != null)
                {
                    // 0.7.5: a death while spectating records the spectate rig as the camera's home (PlayerShip.StartDying:
                    // m_camera_parent = camera.parent). The rig is destroyed at the end of the frame - with the camera and its bright
                    // blocker under it: every UI draw then threw (19:13 run, HUD9 NRE in DrawFullScreenEffects) = no menu, frozen view.
                    var home = OlCoop.Death.Spectate.SafeCameraHome(ship);
                    ship.m_camera_parent = home;
                    ship.c_camera_transform.parent = home;
                    ship.c_camera_transform.localPosition = Vector3.zero;
                    ship.ResetCameraPosition();
                    ship.ResetCameraSway();
                }
                var ui = ship != null && ship.c_viewer != null ? ship.c_viewer.c_ui_mesh_transform : null;
                if (ui != null && ship.c_cam_controller != null)
                {
                    ui.parent = ship.c_cam_controller; ui.localPosition = Vector3.zero; ui.localRotation = Quaternion.identity;
                    var mr = ui.GetComponent<MeshRenderer>(); if (mr != null) mr.enabled = true;
                    ui.gameObject.SetActive(true);
                }
                if (Camera.main != null) Camera.main.enabled = true;
            }
            catch (Exception ex) { CoopLog.Error("CHAL results post", ex); }
            OlCoop.World.CoopWorldTick.MenuOpen = false; // results screen, not an Esc menu: nothing keeps running behind it
            s_results_watch = Time.realtimeSinceStartup; s_next_watch = 0f;
            CoopLog.Write("CHAL", "results screen opened (menu " + MenuManager.m_menu_state + ", camera on " + (Camera.main != null && Camera.main.transform.parent != null ? Camera.main.transform.parent.name : "null") + ")");
        }

        /// Host: spawn placement reads GameManager.m_player_ship; place each new robot around a random living player.
        static readonly System.Random s_rng = new System.Random();
        public static PlayerShip PickSpawnFocus()
        {
            var alive = new List<PlayerShip>();
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                var s = p != null ? p.c_player_ship : null;
                if (s == null || (bool)s.m_dying || (bool)s.m_dead || !s.gameObject.activeInHierarchy) continue;
                if (!s.isLocalPlayer)
                {
                    int sg = s.c_moving_object != null ? s.c_moving_object.CurrentSegmentIndex : -1;
                    if (sg < 0) continue; // unknown segment: spawn choice indexes segment tables with it
                    s.SegmentIndex = sg;  // only kept current for the local ship
                }
                alive.Add(s);
            }
            if (alive.Count == 0) return null; // nobody alive: stock (around the host)
            return alive[s_rng.Next(alive.Count)];
        }

        /// Host: a joiner's player object was just created in a challenge level - the stock per-player setup (countdown +50 armor).
        public static void HostNewPlayer(NetworkConnection conn)
        {
            if (!GameplayManager.IsChallengeMode || conn == null || conn.playerControllers == null) return;
            foreach (var pc in conn.playerControllers)
            {
                var pl = pc != null && pc.gameObject != null ? pc.gameObject.GetComponent<Player>() : null;
                if (pl == null || pl.isLocalPlayer) continue;
                ChallengeManager.InitChallengeForPlayer(pl);
                OlCoop.World.CoopCaps.Clamp(pl, "challenge start");
                CoopLog.Write("CHAL", "host: challenge start armor for netId=" + pl.netId.Value + ": " + ((float)pl.m_hitpoints).ToString("F0"));
            }
        }

        // ================================================================ joiner
        public static ChallengeInfoMsg Pending;      // last settings from the host
        public static bool LoadoutChosen;
        public static bool JoinerLoadoutGiven;
        static int s_declined = -1;            // the joiner pressed START on the briefing for Pending.level
        static bool s_end_alert;

        public static void OnInfo(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<ChallengeInfoMsg>();
                bool changed = Pending == null || Pending.level != m.level || Pending.countdown != m.countdown || Pending.difficulty != m.difficulty;
                if (changed) { LoadoutChosen = false; CoopLog.Write("CHAL", "joiner: host chose challenge level " + m.level + " countdown=" + m.countdown + " difficulty=" + m.difficulty); }
                Pending = m;
                ChallengeManager.CountdownMode = m.countdown;
                // 0.7.2: from any menu outside a level (main menu, CO-OP screen, options) - not mid-briefing/results/loading
                var ms = MenuManager.m_menu_state;
                if (GameManager.m_game_state != GameManager.GameState.MENU || GameplayManager.LevelIsLoaded && GameManager.m_game_state != GameManager.GameState.MENU ||
                    ms == MenuState.CHALLENGE_BRIEFING || ms == MenuState.CHALLENGE_RESULTS || ms == MenuState.PLAY_GAME || ms == MenuState.PAUSE_MENU) return;
                if (LoadoutChosen) return; // already waiting with a loadout for this level
                if (s_declined == m.level) return; // backed out of this briefing; READY UP on the CO-OP screen still works
                OpenBriefing(m);
            }
            catch (Exception ex) { CoopLog.Error("CHAL info", ex); }
        }

        static void OpenBriefing(ChallengeInfoMsg m)
        {
            var mission = GameManager.ChallengeMission;
            if (mission == null || m.level < 0 || m.level >= mission.NumLevels) { CoopLog.Write("CHAL", "joiner: challenge level " + m.level + " not found"); return; }
            GameplayManager.DifficultyLevel = m.difficulty;
            ChallengeManager.CountdownMode = m.countdown;
            MenuManager.m_selected_mission = mission;
            OlCoop.World.PostLevel.AllowPlay = false;
            OlCoop.World.PostLevel.ManualReady = false; // ready again only after START on this briefing
            s_declined = -1;
            OlCoop.World.CoopStatus.Clear();
            UIManager.DestroyAll();
            GameplayManager.CreateNewGame(mission, m.level);
            MenuManager.ChangeMenuState(MenuState.CHALLENGE_BRIEFING);
            CoopLog.Write("CHAL", "joiner: opened the challenge briefing for level " + m.level + " - pick a loadout and press START");
        }

        /// READY UP on the challenge briefing: keep the loadout, tell the host we're ready (PostLevel.ReadyTick, every 3 s).
        public static void MarkReady(string where)
        {
            LoadoutChosen = true;
            OlCoop.World.PostLevel.ManualReady = true;
            OlCoop.World.PostLevel.HostWaiting = true;
            CoopLog.Write("CHAL", "joiner: loadout chosen (weapons " + string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_weapons, x => x.ToString())) +
                " missiles " + string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_missiles, x => x.ToString())) + "); READY UP - waiting for the host on the " + where);
            OlCoop.World.CoopStatus.Set(0, "READY - WAITING FOR THE HOST TO START THE CHALLENGE");
        }

        /// ChangeMenuState prefix on a joiner. False = state replaced/blocked.
        public static bool JoinerRedirect(ref MenuState state)
        {
            if (!CoopConfig.IsJoiner || !Session.CoopClient.ConnectIssued) return true;
            if (state == MenuState.PLAY_GAME && GameplayManager.IsChallengeMode && !OlCoop.World.PostLevel.AllowPlay &&
                MenuManager.m_menu_state == MenuState.CHALLENGE_BRIEFING)
            {
                // fallback (CH18 normally keeps the joiner on the briefing): ready, wait in the main menu
                MarkReady("main menu");
                state = MenuState.MAIN_MENU;
                return true;
            }
            if (state == MenuState.CHALLENGE_SELECT || state == MenuState.DIFFICULTY_SELECT && GameplayManager.IsChallengeMode)
            {
                if (MenuManager.m_menu_state == MenuState.CHALLENGE_BRIEFING && Pending != null) s_declined = Pending.level;
                // the host picks the level; a joiner backing out of the briefing or the results screen waits in the main menu
                CoopLog.Write("CHAL", "joiner: " + state + " -> main menu (the host picks the challenge level)");
                state = MenuState.MAIN_MENU;
                return true;
            }
            return true;
        }

        /// Joiner level loader: the scene name is a challenge level. Mirrors Session.C1 for the story mission.
        public static bool TryLoadChallenge(string name, bool inLevel)
        {
            var mission = GameManager.ChallengeMission;
            int idx = mission != null ? mission.FindLevelIndex(name) : -1;
            if (idx < 0) return false;
            CoopLog.Write("JOIN", "loading host's challenge level '" + name + "' (index " + idx + ")" + (inLevel ? " from inside a level" : "") +
                " countdown=" + (Pending != null && Pending.countdown) + " loadout=" + (LoadoutChosen ? "chosen" : "default"));
            if (inLevel) { OlCoop.Death.Spectate.Stop(null); GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit); }
            UIManager.DestroyAll(true);
            if (Pending != null) { ChallengeManager.CountdownMode = Pending.countdown; GameplayManager.DifficultyLevel = Pending.difficulty; }
            else if (OlCoop.Robots.CoopRobots.HostDifficulty >= 0) GameplayManager.DifficultyLevel = OlCoop.Robots.CoopRobots.HostDifficulty;
            // CreateNewGame re-rolls the loadout (FauxGiveWeaponsAndMissiles); keep the one picked on the briefing
            var sw = (int[])ChallengeManager.m_starting_weapons.Clone(); var sm = (int[])ChallengeManager.m_starting_missiles.Clone();
            var aw = (bool[])ChallengeManager.AvailableWeapons.Clone(); var am = (bool[])ChallengeManager.AvailableMissiles.Clone();
            bool keep = LoadoutChosen && Pending != null && Pending.level == idx;
            MenuManager.m_selected_mission = mission;
            GameplayManager.CreateNewGame(mission, idx);
            if (keep)
            {
                Array.Copy(sw, ChallengeManager.m_starting_weapons, sw.Length); Array.Copy(sm, ChallengeManager.m_starting_missiles, sm.Length);
                Array.Copy(aw, ChallengeManager.AvailableWeapons, aw.Length); Array.Copy(am, ChallengeManager.AvailableMissiles, am.Length);
            }
            else
            {
                // no briefing on this machine (joined mid-run or pressed READY UP): the stock random loadout
                ChallengeManager.SetAvailableMissiles(GameManager.m_local_player);
                ChallengeManager.SetAvailableWeapons(GameManager.m_local_player);
                ChallengeManager.CopyStartingToSelected();
            }
            LoadoutChosen = false;
            try { OlCoop.Combat.CoopLoadout.DropCarry(); } catch (Exception ex) { CoopLog.Error("CHAL carry", ex); }
            OlCoop.World.PostLevel.ManualReady = false;
            OlCoop.World.PostLevel.AllowPlay = true;
            if (inLevel) { GameplayManager.m_between_level_start = Time.realtimeSinceStartup; GameplayManager.SwitchToMenu(MenuState.PLAY_GAME); }
            else MenuManager.ChangeMenuState(MenuState.PLAY_GAME);
            return true;
        }

        /// Joiner replacement for ChallengeManager.Update: show the host's run, no simulation.
        public static void JoinerUpdate()
        {
            if (Ending) return;
            if (ChallengeManager.CountdownMode)
            {
                ChallengeManager.ChallengeModeTime = Mathf.Max(0f, (float)ChallengeManager.ChallengeModeTime - RUtility.FRAMETIME_GAME);
                int left = (int)(float)ChallengeManager.ChallengeModeTime;
                if (left <= ChallengeManager.NextCMCountdownTime && ChallengeManager.NextCMCountdownTime >= 0)
                {
                    SFXCueManager.PlayCue2D((SFXCue)(12 - ChallengeManager.NextCMCountdownTime), 1f, 0f, 0f, reverb: true);
                    ChallengeManager.NextCMCountdownTime--;
                }
            }
            ChallengeManager.CMComboDelay -= RUtility.FRAMETIME_GAME;
            if (ChallengeManager.CMComboDelay < 0f) ChallengeManager.CMComboTimer = Mathf.Clamp(ChallengeManager.CMComboTimer - RUtility.FRAMETIME_GAME / 20f, 0f, 1f);
        }

        static int s_state_logs;
        public static void OnState(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<ChallengeStateMsg>();
                if (!GameplayManager.IsChallengeMode || Ending) return;
                int oldKills = ChallengeManager.ChallengeRobotsDestroyed;
                ChallengeManager.ChallengeScore = m.score;
                ChallengeManager.ChallengeRobotsDestroyed = m.kills;
                ChallengeManager.KillCountLast = m.kills;
                ChallengeManager.ChallengeModeTime = m.time;
                ChallengeManager.CMComboTimer = m.combo;
                if (m.kills > oldKills) UIElement.CM_KILL_FLASH = 0.6f;
                if (m.endReason == (byte)ChallengeManager.SecretLevelEnd.FINISHED && !s_end_alert)
                {
                    s_end_alert = true;
                    GameManager.m_audio.PlayCue2D(285, 1f, 0.2f);
                    GameplayManager.AlertPopup(Loc.LS("COUNTDOWN COMPLETE"), Loc.LS("TIME EXPIRED"));
                }
                if (s_state_logs++ == 0) CoopLog.Write("CHAL", "joiner: showing the host's run (score " + m.score + ", " + m.kills + " kills, time " + m.time.ToString("F0") + ")");
            }
            catch (Exception ex) { CoopLog.Error("CHAL state", ex); }
        }

        public static void OnUpgrade(NetworkMessage msg)
        {
            try
            {
                int kind = msg.ReadMessage<UnityEngine.Networking.NetworkSystem.IntegerMessage>().value;
                var me = GameManager.m_local_player;
                if (me == null || !GameplayManager.IsChallengeMode) return;
                if (kind == 1) ChallengeManager.UpgradeRandomMissile(me); else ChallengeManager.UpgradeRandomWeapon(me);
                OlCoop.Combat.CoopLoadout.SendSoon(); // the host's copy of our ship must know the new level/ammo
                CoopLog.Write("CHAL", "joiner: team kill upgrade (" + (kind == 1 ? "missile" : "weapon") + ") applied to our ship");
            }
            catch (Exception ex) { CoopLog.Error("CHAL upgrade", ex); }
        }

        public static void OnEnd(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<ChallengeEndMsg>();
                CoopLog.Write("CHAL", "joiner: host ended the run (" + m.reason + "): score " + m.score + ", " + m.kills + " kills");
                if (!GameplayManager.IsChallengeMode || !GameplayManager.LevelIsLoaded || Ending) return;
                Ending = true;
                ChallengeManager.ChallengeScore = m.score;
                ChallengeManager.ChallengeRobotsDestroyed = m.kills;
                OpenResults();
            }
            catch (Exception ex) { CoopLog.Error("CHAL end", ex); }
        }
    }

    // ==================================================================== patches

    [HarmonyPatch(typeof(ChallengeManager), "Update")]
    static class CH1_Update
    {
        static bool Prefix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return true;
            try
            {
                if (OlCoop.Robots.CoopRobots.IsJoiner) { CoopChallenge.JoinerUpdate(); return false; }
                if (OlCoop.Robots.CoopRobots.IsHost) return CoopChallenge.HostUpdatePrefix();
            }
            catch (Exception ex) { CoopLog.Error("CH1", ex); }
            return true;
        }
        static Exception Finalizer(Exception __exception) { CoopChallenge.HostUpdateFinalizer(); return __exception; }
    }

    /// Joiner: kills are counted on the host (its numbers arrive 4/s); no local score, combo, achievements.
    [HarmonyPatch(typeof(ChallengeManager), "AddKill")]
    static class CH2_NoJoinerKills
    {
        static bool Prefix() { return !OlCoop.Robots.CoopRobots.IsJoiner; }
    }

    [HarmonyPatch(typeof(ChallengeManager), "UpgradeRandomWeapon")]
    static class CH3_WeaponUpgrade
    {
        static void Postfix() { if (OlCoop.Robots.CoopRobots.IsHost) CoopChallenge.HostUpgraded(false); }
    }

    [HarmonyPatch(typeof(ChallengeManager), "UpgradeRandomMissile")]
    static class CH4_MissileUpgrade
    {
        static void Postfix() { if (OlCoop.Robots.CoopRobots.IsHost) CoopChallenge.HostUpgraded(true); }
    }

    /// Host: each new challenge robot is placed around a random living player (stock: always around the host).
    [HarmonyPatch(typeof(ChallengeManager), "SpawnRobot")]
    static class CH5_SpawnNearAnyPlayer
    {
        static void Prefix(out PlayerShip __state)
        {
            __state = null;
            if (!OlCoop.Robots.CoopRobots.IsHost || !GameplayManager.IsChallengeMode) return;
            try
            {
                var focus = CoopChallenge.PickSpawnFocus();
                if (focus == null || focus == GameManager.m_player_ship) return;
                __state = GameManager.m_player_ship;
                GameManager.m_player_ship = focus;
            }
            catch (Exception ex) { CoopLog.Error("CH5", ex); }
        }
        static Exception Finalizer(PlayerShip __state, Exception __exception)
        {
            if (__state != null) GameManager.m_player_ship = __state;
            return __exception;
        }
    }

    /// Joiner: don't move our ship to the single-player start point (the host places joiners).
    [HarmonyPatch(typeof(ChallengeManager), "ChooseSpawnPointSinglePlayer")]
    static class CH6_JoinerStartPoint
    {
        static bool Prefix(ref LevelData.SpawnPoint __result)
        {
            if (!CoopConfig.IsJoiner || !CoopConfig.Active) return true;
            __result = null;
            return false;
        }
    }

    /// Joiner menu redirects (briefing START -> ready + wait; no own level select).
    [HarmonyPatch(typeof(MenuManager), "ChangeMenuState")]
    static class CH7_JoinerMenus
    {
        [HarmonyPriority(Priority.High)]
        static bool Prefix(ref MenuState new_state)
        {
            try { return CoopChallenge.JoinerRedirect(ref new_state); }
            catch (Exception ex) { CoopLog.Error("CH7", ex); return true; }
        }
    }

    [HarmonyPatch(typeof(MenuManager), "Update")]
    static class CH8_HostMenuTick
    {
        static void Postfix() { try { CoopChallenge.HostMenuTick(); } catch (Exception ex) { CoopLog.Error("CH8", ex); } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class CH9_HostTick
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { CoopChallenge.ResultsWatch(); CoopChallenge.HostTick(); } catch (Exception ex) { CoopLog.Error("CH9", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "OnAddPlayerMessage")]
    static class CH10_NewPlayer
    {
        static void Postfix(NetworkMessage msg)
        {
            if (!CoopConfig.IsHost || !CoopConfig.Active) return;
            try { CoopChallenge.HostNewPlayer(msg.conn); } catch (Exception ex) { CoopLog.Error("CH10", ex); }
        }
    }

    /// CH15 (0.7.7): the joiner's challenge loadout. Stock ChallengeManager.InitChallenge gives the briefing loadout
    /// (ActuallyGiveWeaponsAndMissiles) to GameManager.m_local_player at level start. On a joiner that is still the temporary
    /// single-player ship: its networked ship is created ~0.1 s later (04:39 run: StartLevel 27.619, OnStartLocalPlayer netId=25 27.733)
    /// and started with no weapons, missiles or ammo, which it then sent to the host. Give the loadout to the networked ship.
    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class CH15_JoinerLoadout
    {
        [HarmonyPriority(Priority.VeryLow)]
        static void Postfix(Player __instance)
        {
            if (!CoopConfig.Active || !CoopConfig.IsJoiner || !GameplayManager.IsChallengeMode || CoopChallenge.JoinerLoadoutGiven) return;
            try
            {
                if (GameplayManager.Level == null || GameplayManager.Level.IsSecret) return;
                if (ChallengeManager.m_starting_weapons[0] < 0 || ChallengeManager.m_starting_missiles[0] < 0)
                {
                    CoopLog.Write("CHAL", "joiner: no starting loadout (weapons " + string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_weapons, x => x.ToString())) +
                        " missiles " + string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_missiles, x => x.ToString())) + "); ship keeps what it has");
                    return;
                }
                CoopChallenge.JoinerLoadoutGiven = true;
                ChallengeManager.ActuallyGiveWeaponsAndMissiles(__instance);
                CoopLog.Write("CHAL", "joiner: briefing loadout given to our networked ship netId=" + __instance.netId.Value + " (weapons " +
                    string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_weapons, x => x.ToString())) + " missiles " +
                    string.Join(",", Array.ConvertAll(ChallengeManager.m_starting_missiles, x => x.ToString())) + ", ammo " + (int)__instance.m_ammo + ")");
                OlCoop.Combat.CoopLoadout.SendSoon();
            }
            catch (Exception ex) { CoopLog.Error("CH15", ex); }
        }
    }

    /// CH16 (0.7.7): olmod's creeper team colours (MPTeams_Projectile_FixedUpdateDynamic.Postfix) run for every creeper the local player
    /// owns whenever IsMultiplayerActive - which co-op sets - and throw on co-op creepers (04:40 run: ~1,800-2,000 NREs on both PCs, one per
    /// creeper per physics frame). The exception aborts UpdateDynamicManager.FixedUpdateDynamicObjects, so every projectile after it in the
    /// list stopped updating (frozen shots). Co-op has no teams to colour: skip it.
    [HarmonyPatch]
    static class CH16_NoOlmodCreeperColors
    {
        public const string OlmodTarget = "GameMod.MPTeams_Projectile_FixedUpdateDynamic:Postfix";
        static bool Prepare() { return AccessTools.TypeByName("GameMod.MPTeams_Projectile_FixedUpdateDynamic") != null; }
        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("GameMod.MPTeams_Projectile_FixedUpdateDynamic"), "Postfix"); }
        static bool Prefix() { return !CoopConfig.Active || GameplayManager.IsMultiplayer; }
    }

    /// CH17 (0.7.9): GameplayManager.StartLevel builds the automap around GameManager.m_player_ship (its moving object for the
    /// visibility search, its flare camera). On a joiner that is the temporary single-player ship, which Player.OnStartLocalPlayer
    /// destroys ~0.1 s later, so the joiner's map worked from a deleted ship. Rebuild it for the networked ship (all co-op modes).
    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class CH17_JoinerAutomap
    {
        [HarmonyPriority(Priority.VeryLow)]
        static void Postfix(Player __instance)
        {
            if (!CoopConfig.Active || !CoopConfig.IsJoiner || GameplayManager.IsMultiplayer) return;
            try
            {
                var ship = __instance.c_player_ship;
                var gm = GameplayManager.m_gm;
                if (ship == null || gm == null || GameManager.m_level_data == null) return;
                GameplayManager.m_automap = new Automap(gm.m_automap_portals, ship.m_flare_cam_object, GameplayManager.IsChallengeMode);
                CoopLog.Write("FLOW", "joiner: automap rebuilt for our networked ship netId=" + __instance.netId.Value + " (whole map=" + (GameplayManager.IsChallengeMode) + ")");
            }
            catch (Exception ex) { CoopLog.Error("CH17", ex); }
        }
    }

    /// CH18 (0.7.11, user): READY UP on the challenge briefing keeps the joiner on that screen ("waiting for host" shows there).
    /// Stock ChallengeBriefingUpdate: selection 0 -> sub-state START (UI destroyed, loadout copied) -> after 0.25 s PLAY_GAME. On a joiner,
    /// at START: mark ready, rebuild the briefing UI like its INIT (without FauxGiveWeaponsAndMissiles, which would wipe the loadout).
    [HarmonyPatch(typeof(MenuManager), "ChallengeBriefingUpdate")]
    static class CH18_JoinerReadyStays
    {
        static bool Prefix()
        {
            if (!CoopConfig.Active || !CoopConfig.IsJoiner || !Session.CoopClient.ConnectIssued || OlCoop.World.PostLevel.AllowPlay) return true;
            if (MenuManager.m_menu_sub_state != MenuSubState.START) return true;
            try
            {
                CoopChallenge.MarkReady("briefing");
                UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, UIElementType.LEVEL_DESCRIPTION);
                UIManager.SetLevelTexture(GameplayManager.Level);
                MenuManager.m_menu_sub_state = MenuSubState.ACTIVE;
                MenuManager.SetDefaultSelection(0);
                return false;
            }
            catch (Exception ex) { CoopLog.Error("CH18", ex); return true; }
        }
    }

    /// CH19 (0.7.11): host - remember who destroyed the robot (StartExploding runs AddKill inside it).
    [HarmonyPatch(typeof(Robot), "StartExploding")]
    static class CH19_KillerForScore
    {
        static void Prefix(DamageInfo di)
        {
            if (!CoopConfig.IsHost || !CoopChallenge.InChallenge) return;
            try
            {
                uint k = 0;
                if (di.owner != null) { var p = di.owner.GetComponent<Player>(); if (p != null) k = p.netId.Value; }
                CoopChallenge.PendingKiller = k;
            }
            catch { CoopChallenge.PendingKiller = 0; }
        }
        static void Finalizer() { CoopChallenge.PendingKiller = 0; }
    }

    /// CH20 (0.7.11): host - credit the kill's points (score value + combo) to that player.
    [HarmonyPatch(typeof(ChallengeManager), "AddKill")]
    static class CH20_CreditKill
    {
        static void Postfix(int scored, int combo)
        {
            if (!CoopConfig.IsHost || !CoopChallenge.InChallenge) return;
            try { CoopChallenge.CreditKill(scored + combo); } catch (Exception ex) { CoopLog.Error("CH20", ex); }
        }
    }

    /// CH21 (0.7.11): host - its HUD and results screen show its own score and kills, not the team totals.
    [HarmonyPatch]
    static class CH21_HostOwnScoreDrawn
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIElement), "DrawHUD");
            yield return AccessTools.Method(typeof(UIElement), "DrawChallengeResults");
        }
        struct Saved { public bool on; public int score, kills; }
        static void Prefix(out Saved __state)
        {
            __state = new Saved();
            int sc, k;
            if (!CoopChallenge.HostOwnScore(out sc, out k)) return;
            __state.on = true; __state.score = ChallengeManager.ChallengeScore; __state.kills = ChallengeManager.ChallengeRobotsDestroyed;
            ChallengeManager.ChallengeScore = sc; ChallengeManager.ChallengeRobotsDestroyed = k;
        }
        static void Finalizer(Saved __state)
        {
            if (!__state.on) return;
            ChallengeManager.ChallengeScore = __state.score; ChallengeManager.ChallengeRobotsDestroyed = __state.kills;
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class CH11_Reset
    {
        static void Prefix() { if (CoopConfig.Active) CoopChallenge.ResetForLevel(); }
    }

    /// Co-op team scores stay off the public (solo) Steam leaderboards; the local best score is still saved.
    [HarmonyPatch(typeof(GameplayManager), "UpdateChallengeLeaderboardScore")]
    static class CH13_NoLeaderboard
    {
        static System.Collections.IEnumerator Empty() { yield break; }
        static bool Prefix(ref System.Collections.IEnumerator __result)
        {
            if (!CoopConfig.Active) return true;
            CoopLog.Write("CHAL", "co-op run: leaderboard upload skipped (local best score kept)");
            __result = Empty();
            return false;
        }
    }

    /// 0.7.5 safety: a missing bright blocker (destroyed camera child) must not abort the whole UI draw (= no menus at all).
    [HarmonyPatch(typeof(UIManager), "DrawFullScreenEffects")]
    static class CH14_SafeFullScreenEffects
    {
        static int s_logged;
        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !CoopConfig.Active) return __exception;
            if (s_logged++ < 3) CoopLog.Write("CHAL", "full-screen effects failed (" + __exception.GetType().Name + "); menus still drawn");
            return null;
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class CH12_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            var c = Client.GetClient();
            c.RegisterHandler(CoopChallenge.MsgInfo, CoopChallenge.OnInfo);
            c.RegisterHandler(CoopChallenge.MsgState, CoopChallenge.OnState);
            c.RegisterHandler(CoopChallenge.MsgUpgrade, CoopChallenge.OnUpgrade);
            c.RegisterHandler(CoopChallenge.MsgEnd, CoopChallenge.OnEnd);
        }
    }
}
