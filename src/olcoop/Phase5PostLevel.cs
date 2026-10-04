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
