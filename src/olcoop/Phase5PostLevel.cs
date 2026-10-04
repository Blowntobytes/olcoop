// olcoop 0.4.15: joiners run the stock end-of-level screens (results, stats, upgrade menu, briefing) like the host.
// Before: a joiner's EscapeLevel was blocked (F6) and it waited, black, in the old level for the host's next one.
// Now: F6 lets EscapeLevel/DoneLevel(Escaped) run, so the joiner's own menus come up. When its menu flow reaches PLAY_GAME it would
// load the next campaign level by itself (MenuManager.PlayGameUpdate -> GameplayManager.LoadLevel); P1 holds it there instead,
// keeps the loadout (with any upgrades just bought) for the next level, and waits for the host's level (C1). If the host's level
// arrives while the joiner is still in its menus, C1 defers it until the joiner reaches that gate.
using System;
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
                OlCoop.Death.Spectate.Stop(null);
                // 10:18 run (0.4.17): the dead joiner reached the results screen but it never responded. MenuManager.Update skips menu
                // handling while PlayerShip.DeathPaused is set (set by StartDying, normally cleared on respawn). Clear the death state
                // the way the game does when leaving its death menu, and drop our respawn countdown overlay.
                PlayerShip.DeathPaused = false;
                try { OlCoop.Hud.CoopHud.ClearRespawn(); UIManager.ClearOverlayElement(1); } catch { }
                try { UIManager.SetScreenFade(0f); } catch { }
                try { AccessTools.Method(typeof(MenuManager), "RecoverFromDeathMenu").Invoke(null, new object[] { false, false }); }
                catch (Exception ex) { CoopLog.Write("FLOW", "joiner: RecoverFromDeathMenu failed: " + ex.GetType().Name); }
                GameplayManager.EscapeLevel();   // F6 lets it run and calls Begin()
                PlayerShip.DeathPaused = false;
                CoopLog.Write("FLOW", "joiner: death state cleared for the end-of-level screens (DeathPaused=" + PlayerShip.DeathPaused + ")");
            }
            catch (Exception ex) { CoopLog.Error("DeadJoinerExit", ex); CoopFlow.ShowWaiting(); }
        }

        public static void Begin()
        {
            if (s_active) return;
            s_active = true; s_at_gate = false; s_pending = null; s_logged_defer = false;
            CoopLog.Write("FLOW", "joiner: level complete - running the end-of-level screens (results, upgrades)");
        }

        public static void Reset() { s_active = false; s_at_gate = false; s_pending = null; AllowPlay = false; }

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
            if (!PostLevel.Active || !PlayerShip.DeathPaused) return;
            PlayerShip.DeathPaused = false;
            if (!s_logged) { s_logged = true; CoopLog.Write("FLOW", "joiner: cleared a leftover death pause on the end-of-level screens"); }
        }
    }

        [HarmonyPatch(typeof(MenuManager), "PlayGameUpdate")]
    static class P1_JoinerPlayGate
    {
        static bool Prefix()
        {
            if (!CoopWorld.Active || !CoopConfig.IsJoiner) return true;
            try { return PostLevel.Gate(); } catch (Exception ex) { CoopLog.Error("P1", ex); return true; }
        }
    }
}
