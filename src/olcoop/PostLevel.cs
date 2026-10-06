// Joiners run the stock end-of-level screens (results, stats, upgrade menu, briefing) like the host.
// F6 lets EscapeLevel/DoneLevel(Escaped) run, so the joiner's own menus come up. When its menu flow reaches PLAY_GAME it would
// load the next campaign level by itself (MenuManager.PlayGameUpdate -> GameplayManager.LoadLevel); P1 holds it there instead,
// keeps the loadout (with any upgrades just bought) for the next level, and waits for the host's level (C1). If the host's level
// arrives while the joiner is still in its menus, C1 defers it until the joiner reaches that gate.
using System;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    public static class PostLevel
    {
        /// Set by C1 right before it switches to PLAY_GAME for the host's level: that load must go through.
        public static bool AllowPlay;
        static bool s_active, s_at_gate, s_logged_defer;
        static string s_pending;
        static float s_gate_since;

        public static bool InMenus { get { return s_active && !s_at_gate; } }

        // ---------------- 0.6.9: READY UP for a joiner who joined while the host was on its end-of-level screens
        /// Joiner: the host is holding the next level for ready players (status 3, repeated every 3 s to joiners that aren't ready).
        public static bool HostWaiting { get { return s_waiting_until > Time.realtimeSinceStartup; } set { s_waiting_until = value ? Time.realtimeSinceStartup + 10f : 0f; } }
        static float s_waiting_until;
        /// Joiner: READY UP pressed on the main menu / CO-OP screen.
        public static bool ManualReady;
        /// Show the READY UP button: joined from the menus (no end-of-level screens of our own), host waiting, not ready yet.
        public static bool ReadyButton { get { return CoopConfig.IsJoiner && HostWaiting && !ManualReady && !s_active && GameManager.m_game_state == GameManager.GameState.MENU; } }
        /// 0.6.10: the host's next level and the upgrade points it had for it (msg 206), sent with status 3 after a level end.
        static string s_offer_scene; static int s_offer_p1, s_offer_p2; static float s_offer_time = -100f;
        public static void ClearOffer() { s_offer_scene = null; }
        public static void OnOffer(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<UpgradeOfferMsg>();
                bool first = s_offer_scene != m.scene;
                s_offer_scene = m.scene; s_offer_p1 = m.points1; s_offer_p2 = m.points2; s_offer_time = Time.realtimeSinceStartup;
                if (first) CoopLog.Write("FLOW", "joiner: host's next level '" + m.scene + "', upgrade points " + m.points1 + "/" + m.points2);
            }
            catch (Exception ex) { CoopLog.Error("OnOffer", ex); }
        }

        public static void PressReady()
        {
            if (s_offer_scene != null && Time.realtimeSinceStartup - s_offer_time < 10f && (s_offer_p1 > 0 || s_offer_p2 > 0) && StartUpgrades()) return;
            ManualReady = true; s_ready_next = 0f;
            CoopLog.Write("FLOW", "joiner: READY UP pressed (joined while the host was between levels)");
        }

        /// A player who joins while the host is on its upgrade screen spends the same upgrade points: the stock upgrade menu for the
        /// host's next level, then its level briefing (READY UP) and the PLAY_GAME gate - the same flow as after a level of our own.
        static bool StartUpgrades()
        {
            try
            {
                var story = GameManager.StoryMission;
                int idx = story != null ? story.FindLevelIndex(s_offer_scene) : -1;
                if (idx < 0) { CoopLog.Write("FLOW", "joiner: host's next level '" + s_offer_scene + "' not in the campaign; plain READY UP"); return false; }
                GameplayManager.CreateNewGame(story, idx);
                var lp = GameManager.m_local_player;
                lp.m_upgrade_points1 = s_offer_p1; lp.m_upgrade_points2 = s_offer_p2;
                Begin();
                UIManager.DestroyAll();
                MenuManager.ChangeMenuState(MenuState.UPGRADE_MENU);
                CoopLog.Write("FLOW", "joiner: READY UP -> upgrade screen for '" + s_offer_scene + "' with the host's " + s_offer_p1 + "/" + s_offer_p2 + " upgrade points");
                return true;
            }
            catch (Exception ex) { CoopLog.Error("StartUpgrades", ex); return false; }
        }
        public static bool Active { get { return s_active; } }

        /// The stock end-of-level screens. Only while the joiner is in one of these is the host's level held back (10:05 run: a joiner
        /// whose menus went to MAIN_MENU instead of PLAY_GAME waited there forever).
        static readonly System.Collections.Generic.HashSet<MenuState> s_post_menus = new System.Collections.Generic.HashSet<MenuState> {
            MenuState.LEVEL_RESULTS, MenuState.STATS, MenuState.UPGRADE_MENU, MenuState.SAVE_MENU, MenuState.SAVE_ERROR,
            MenuState.DEBRIEF, MenuState.LEVEL_BRIEFING, MenuState.BRIEFING, MenuState.ENTITY_BRIEFING, MenuState.MISSION_COMPLETE };

        /// Joiner dead/spectating when the team exits (10:04 run: it stayed spectating with no end-of-level screens). Stop spectating
        /// and finish the level like everyone else.
        public static void DeadJoinerExit()
        {
            CoopLog.Write("FLOW", "joiner: the team exited while we were dead; finishing the level (end-of-level screens)");
            try
            {
                ClearDeathForMenus("joiner");
                GameplayManager.EscapeLevel();   // F6 lets it run and calls Begin()
                PlayerShip.DeathPaused = false;
                CoopLog.Write("FLOW", "joiner: death state cleared for the end-of-level screens (DeathPaused=" + PlayerShip.DeathPaused + ")");
            }
            catch (Exception ex) { CoopLog.Error("DeadJoinerExit", ex); CoopFlow.ShowWaiting(); }
        }

        /// 10:18 run (0.4.17): a dead player reached the results screen but it never responded. MenuManager.Update skips menu handling
        /// while PlayerShip.DeathPaused is set (set by StartDying, normally cleared on respawn). Clear the death state the way the game
        /// does when leaving its death menu, and drop our respawn countdown overlay. Used for joiners and (0.4.19) the host.
        public static void ClearDeathForMenus(string who)
        {
            OlCoop.Death.Spectate.Stop(null);
            PlayerShip.DeathPaused = false;
            try { OlCoop.Hud.CoopHud.ClearRespawn(); UIManager.ClearOverlayElement(1); } catch { }
            try { UIManager.SetScreenFade(0f); } catch { }
            try { AccessTools.Method(typeof(MenuManager), "RecoverFromDeathMenu").Invoke(null, new object[] { false, false }); }
            catch (Exception ex) { CoopLog.Write("FLOW", who + ": RecoverFromDeathMenu failed: " + ex.GetType().Name); }
            if (who == "host") HostReady.DeadFinish = true;
        }

        // ---------------- 0.4.19: joiner tells the host it's done with its end-of-level screens
        static float s_ready_next;
        static int s_ready_sent;

        /// Joiner, every menu frame: once past the end-of-level screens (at the PLAY_GAME gate, or dropped to the main menu), or holding black after an
        /// exit without menus, tell the host we're ready. Repeated every 3 s until the next level loads (cheap; survives a lost message).
        public static void ReadyTick()
        {
            if (!CoopConfig.IsJoiner || Client.GetClient() == null || !Client.IsConnected()) { ManualReady = false; HostWaiting = false; return; }
            if (ManualReady && !HostWaiting) ManualReady = false; // host stopped waiting (started the level or went back to its menu)
            bool ready = s_at_gate || (s_active && MenuManager.m_menu_state == MenuState.MAIN_MENU) || (!s_active && CoopFlow.Waiting) || (ManualReady && GameManager.m_game_state == GameManager.GameState.MENU);
            if (!ready || Time.realtimeSinceStartup < s_ready_next) return;
            var c = Client.GetClient();
            if (c == null || !Client.IsConnected()) return;
            s_ready_next = Time.realtimeSinceStartup + 3f;
            c.Send(FNet.Ready, new UnityEngine.Networking.NetworkSystem.IntegerMessage(1));
            if (s_ready_sent++ == 0) CoopLog.Write("FLOW", "joiner: told the host we're ready for the next level (menu " + MenuManager.m_menu_state + ")");
        }

        /// Host: a joiner is ready.
        public static void OnReady(NetworkMessage msg)
        {
            try { HostReady.Mark(msg.conn.connectionId); } catch (Exception ex) { CoopLog.Error("OnReady", ex); }
        }

        public static void Begin()
        {
            if (s_active) return;
            s_active = true; s_at_gate = false; s_pending = null; s_logged_defer = false;
            CoopLog.Write("FLOW", "joiner: level complete - running the end-of-level screens (results, upgrades)");
        }

        public static void Reset() { s_active = false; s_at_gate = false; s_pending = null; AllowPlay = false; s_ready_sent = 0; s_ready_next = 0f; ManualReady = false; HostWaiting = false; }

        /// C1: host's level arrived. Hold it while the joiner is still in its end-of-level menus.
        public static bool ShouldDefer(string name)
        {
            if (!InMenus) return false;
            if (!s_post_menus.Contains(MenuManager.m_menu_state))
            {
                CoopLog.Write("FLOW", "joiner: host's level '" + name + "' ready and we're in " + MenuManager.m_menu_state + " (not an end-of-level screen); loading it now");
                try { OlCoop.Combat.CoopLoadout.CaptureForNextLevel("left the end-of-level screens"); } catch (Exception ex) { CoopLog.Error("PostLevel capture", ex); }
                s_at_gate = true;
                return false;
            }
            s_pending = name;
            if (!s_logged_defer) { s_logged_defer = true; CoopLog.Write("FLOW", "joiner: host's level '" + name + "' ready; loading it once the end-of-level screens are done (menu " + MenuManager.m_menu_state + ")"); }
            return true;
        }

        /// MenuManager.PlayGameUpdate on a joiner after its own level end: hold, then load the host's level.
        public static bool Gate()
        {
            if (AllowPlay || !s_active) return true;
            if (!s_at_gate)
            {
                s_at_gate = true; s_gate_since = Time.realtimeSinceStartup;
                try { OlCoop.Combat.CoopLoadout.CaptureForNextLevel("end-of-level screens done"); } catch (Exception ex) { CoopLog.Error("PostLevel capture", ex); }
                CoopStatus.Set(0, "WAITING FOR THE HOST'S NEXT LEVEL");
                CoopLog.Write("FLOW", "joiner: end-of-level screens done; " + (s_pending != null ? "loading the host's level '" + s_pending + "'" : "waiting for the host's next level"));
                if (s_pending != null) { var n = s_pending; s_pending = null; Overload.NetworkManager.LoadScene(n); return false; }
                if (!Session.CoopClient.Awaiting) Session.CoopClient.AwaitLevel();
            }
            else if (s_pending != null) { var n = s_pending; s_pending = null; Overload.NetworkManager.LoadScene(n); }
            else if (Time.realtimeSinceStartup - s_gate_since > 70f && !Session.CoopClient.Awaiting) { s_gate_since = Time.realtimeSinceStartup; Session.CoopClient.AwaitLevel(); }
            // The level-retry tick (CoopClient.AwaitTick) normally runs from GameplayManager.Update, which doesn't tick in the menus
            // (09:44 run: both joiners sat on this screen with no retries). Run it from here while waiting.
            Session.CoopClient.AwaitTick();
            return false;
        }
    }

    /// the host doesn't start the next level until every connected joiner has finished its end-of-level screens.
    public static class HostReady
    {
        static bool s_on;
        static readonly System.Collections.Generic.HashSet<int> s_ready = new System.Collections.Generic.HashSet<int>();
        static int s_shown_ready = -1, s_shown_total = -1;
        /// Host finished the level dead (fallback path): keep its menus unfrozen.
        public static bool DeadFinish;
        public static bool On { get { return s_on; } }
        public static bool AfterLevel { get { return s_after_level; } }

        static bool s_after_level, s_released;
        static int s_points1, s_points2;
        public static void Begin()
        {
            s_on = true; s_after_level = true; s_ready.Clear(); s_shown_ready = -1; s_shown_total = -1;
            var lp = GameManager.m_local_player;
            s_points1 = lp != null ? lp.m_upgrade_points1 : 0; s_points2 = lp != null ? lp.m_upgrade_points2 : 0;
            CoopLog.Write("FLOW", "host: level done; the next level waits until every joiner is ready (upgrade points " + s_points1 + "/" + s_points2 + ")");
        }
        public static void End() { s_released = false; if (s_on || DeadFinish) { s_on = false; DeadFinish = false; s_ready.Clear(); CoopStatus.Clear(); } }

        static int Joiners()
        {
            int n = 0;
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) n++;
            return n;
        }

        /// The host's next level: after the results screen it is GameplayManager.Level (AdvanceLevel), before it the one after.
        static string NextScene()
        {
            try
            {
                var li = GameplayManager.LevelIsLoaded ? GameplayManager.GetNextLevel() : GameplayManager.Level;
                return li != null ? li.FileName : null;
            }
            catch { return null; }
        }

        public static bool IsReady(int conn) { return s_on && s_ready.Contains(conn); }

        static float s_next_remind;
        /// Host, every menu frame: while holding for ready players, tell every verified joiner (status 3), so a
        /// player who joined from the main menu during the end-of-level screens gets a READY UP button.
        static readonly FieldInfo f_back_stack = AccessTools.Field(typeof(MenuManager), "m_back_stack");
        /// 0.6.14: host in its menus outside a level (main menu, co-op screen, level select - not the pause menu or a level start).
        static bool HostInLobby()
        {
            if (GameManager.m_game_state != GameManager.GameState.MENU) return false;
            var ms = MenuManager.m_menu_state;
            if (ms == MenuState.PLAY_GAME || ms == MenuState.PAUSE_MENU) return false;
            var st = f_back_stack != null ? f_back_stack.GetValue(null) as System.Collections.Generic.Stack<MenuState> : null;
            return st == null || !st.Contains(MenuState.PAUSE_MENU);
        }

        public static void Remind()
        {
            if (!CoopWorld.IsHost) return;
            // 0.6.14: a joiner who connects while the host is in its menus gets READY UP straight away (0.6.13 only offered it once the
            // host pressed start). The marks carry into the PLAY_GAME gate, so ready players don't wait there again.
            if (!s_on && !s_released && Joiners() > 0 && HostInLobby())
            {
                s_on = true; s_after_level = false; s_ready.Clear(); s_shown_ready = -1; s_shown_total = -1; s_next_remind = 0f;
                CoopLog.Write("FLOW", "host: " + Joiners() + " joiner(s) connected in the menus; offering READY UP");
            }
            if (s_on && !s_after_level && Joiners() == 0) { CoopLog.Write("FLOW", "host: no joiners left; menu ready check dropped"); End(); return; }
            if (!s_on || Time.realtimeSinceStartup < s_next_remind) return;
            s_next_remind = Time.realtimeSinceStartup + 3f;
            string next = s_after_level ? NextScene() : null;
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId))
                {
                    c.Send(FNet.Status, new UnityEngine.Networking.NetworkSystem.IntegerMessage(3)); // also to ready ones: "WAITING FOR HOST"
                    if (next != null && !s_ready.Contains(c.connectionId)) c.Send(FNet.Offer, new UpgradeOfferMsg { scene = next, points1 = s_points1, points2 = s_points2 });
                }
        }

        public static void Mark(int conn)
        {
            if (!s_on || !s_ready.Add(conn)) return;
            CoopLog.Write("FLOW", "host: joiner conn " + conn + " is ready for the next level");
        }

        /// MenuManager.PlayGameUpdate on the host after a level end. False = hold on this screen.
        public static bool Gate(bool returningFromSecret)
        {
            // PlayGameUpdate runs every frame of INIT (LoadLevel), ACTIVE (loading) and START; only INIT, before the load, may hold it.
            if (MenuManager.m_menu_sub_state != MenuSubState.INIT) return true;
            if (s_on && !s_after_level && returningFromSecret) { End(); return true; }
            if (!s_on && !s_released && !returningFromSecret && Joiners() > 0)
            {
                // 0.6.10: also before a level the host didn't just finish (new campaign / saved game, first level of the session):
                // joiners get READY UP and the level starts when they're ready
                s_on = true; s_after_level = false; s_ready.Clear(); s_shown_ready = -1; s_shown_total = -1;
                CoopLog.Write("FLOW", "host: starting a level with " + Joiners() + " joiner(s) connected; waiting until they're ready");
            }
            if (!s_on) return true;
            int total = 0, ready = 0;
            foreach (var c in NetworkServer.connections)
            {
                if (c == null || c.connectionId == 0 || !c.isConnected || !Session.CoopHost.Verified.Contains(c.connectionId)) continue;
                total++; if (s_ready.Contains(c.connectionId)) ready++;
            }
            if (ready >= total)
            {
                CoopLog.Write("FLOW", "host: all joiners ready (" + ready + "/" + total + "); starting the next level");
                s_on = false; s_released = true; CoopStatus.Clear();
                return true;
            }
            if (ready != s_shown_ready || total != s_shown_total)
            {
                s_shown_ready = ready; s_shown_total = total;
                CoopStatus.Set(0, "WAITING FOR PLAYERS - " + ready + " OF " + total + " READY");
            }
            return false;
        }
    }

    /// UNET NotReady (msg 36) has no handler on Overload clients. The host's SendScene sends NotReady, then config/level-info/
    /// SceneLoad (48)/SceneLoaded (49) in the same burst; UNET aborts a batch at an unknown message id, so everything after it was
    /// dropped ("Unknown message ID 36", 09:49:33 - joiners never got the level; also the 0.3.10 "dropped scene message").
    /// A no-op handler keeps the rest of the batch.
    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class P2_IgnoreNotReady
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(36, msg => { CoopLog.Write("JOIN", "host marked us not-ready (level change)"); });
        }
    }

/// P3: while a joiner is on its end-of-level screens, a leftover death pause must not freeze the menus (MenuManager.Update skips
    /// menu handling while PlayerShip.DeathPaused).
    [HarmonyPatch(typeof(MenuManager), "Update")]
    static class P3_NoDeathPauseInPostLevel
    {
        static bool s_logged;
        static void Prefix()
        {
            try { PostLevel.ReadyTick(); HostReady.Remind(); } catch (Exception ex) { CoopLog.Error("ReadyTick", ex); }
            if (HostReady.On && HostReady.AfterLevel && MenuManager.m_menu_state == MenuState.MAIN_MENU) { CoopLog.Write("FLOW", "host: back at the main menu; ready check dropped"); HostReady.End(); }
            bool post = PostLevel.Active || (CoopWorld.IsHost && (HostReady.On || HostReady.DeadFinish));
            if (!post || !PlayerShip.DeathPaused) return;
            PlayerShip.DeathPaused = false;
            if (!s_logged) { s_logged = true; CoopLog.Write("FLOW", "cleared a leftover death pause on the end-of-level screens"); }
        }
    }

        [HarmonyPatch(typeof(MenuManager), "PlayGameUpdate")]
    static class P1_JoinerPlayGate
    {
        static bool Prefix(bool returning_from_secret)
        {
            if (!CoopWorld.Active) return true;
            if (CoopWorld.IsHost) { try { return HostReady.Gate(returning_from_secret); } catch (Exception ex) { CoopLog.Error("P1 host", ex); return true; } }
            if (!CoopConfig.IsJoiner) return true;
            try { return PostLevel.Gate(); } catch (Exception ex) { CoopLog.Error("P1", ex); return true; }
        }
    }

    public class UpgradeOfferMsg : MessageBase
    {
        public string scene = ""; public int points1, points2;
        public override void Serialize(NetworkWriter w) { w.Write(scene ?? ""); w.Write(points1); w.Write(points2); }
        public override void Deserialize(NetworkReader r) { scene = r.ReadString(); points1 = r.ReadInt32(); points2 = r.ReadInt32(); }
    }
}
