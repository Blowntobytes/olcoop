using System;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    /// <summary>
    /// Level objective counters on joiners (0.5.5).
    /// The HUD (UIElement.DrawHUD -> DrawHUDScoreInfo) draws two level objective counters from statics that only change where the
    /// level is simulated, i.e. on the host:
    ///  - LevelCustomInfo.Objective == 1 ("OPERATORS", e.g. Ymir Outpost): CustomCount - GameplayManager.m_total_robots_killed.
    ///    m_total_robots_killed is incremented by GameplayManager.AddStatsRobotKilled, which runs where the robot dies (host).
    ///  - LevelCustomInfo.Objective == 3 ("CORES REMAINING"): CustomCount, decremented by AlienPower.OnCollisionEnter (host).
    /// The host sends both values (msg 194, on change + 1 Hz). Joiners swap them in only while the HUD draws, so their own
    /// stats (results screen robots killed) are untouched. The escape countdown (MustEscape/EscapeTimer) already reaches joiners
    /// (0.4.6/0.4.10 joiner logs), and the objective popups are ScriptObjectiveMessage (replayed live by CoopWorld).
    /// </summary>
    public static class ObjNet
    {
        public const short State = 194; // H->J ObjMsg
    }

    public class ObjMsg : MessageBase
    {
        public int killed; public int custom; public int objective;
        public override void Serialize(NetworkWriter w) { w.Write(killed); w.Write(custom); w.Write(objective); }
        public override void Deserialize(NetworkReader r) { killed = r.ReadInt32(); custom = r.ReadInt32(); objective = r.ReadInt32(); }
    }

    public static class CoopObjectives
    {
        // host
        static int s_sent_killed = int.MinValue, s_sent_custom = int.MinValue;
        static float s_next_send;
        // joiner
        static int s_killed, s_custom, s_objective;
        static float s_heard = -100f;
        static int s_logged;

        /// Host's values are used for 5 s after the last message (a new level or a lost host falls back to local values).
        public static bool Fresh { get { return CoopConfig.IsJoiner && Time.unscaledTime - s_heard < 5f; } }
        public static int HostKilled { get { return s_killed; } }
        public static int HostCustom { get { return s_custom; } }

        public static void HostTick()
        {
            if (!CoopConfig.IsHost || !GameplayManager.LevelIsLoaded) return;
            int k = GameplayManager.m_total_robots_killed, c = LevelCustomInfo.CustomCount, obj = (int)LevelCustomInfo.Objective;
            bool changed = k != s_sent_killed || c != s_sent_custom;
            if (!changed && Time.unscaledTime < s_next_send) return;
            s_next_send = Time.unscaledTime + 1f;
            if (changed && obj != 0)
                CoopLog.Write("OBJ", "host: objective=" + obj + " robotsKilled=" + k + " customCount=" + c +
                    (obj == 1 ? " operatorsLeft=" + (c - k) : ""));
            s_sent_killed = k; s_sent_custom = c;
            var m = new ObjMsg { killed = k, custom = c, objective = obj };
            foreach (var conn in NetworkServer.connections)
                if (conn != null && conn.connectionId != 0 && conn.isConnected && Session.CoopHost.Verified.Contains(conn.connectionId))
                    conn.Send(ObjNet.State, m);
        }

        public static void OnState(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<ObjMsg>();
                bool changed = m.killed != s_killed || m.custom != s_custom || m.objective != s_objective || !Fresh;
                s_killed = m.killed; s_custom = m.custom; s_objective = m.objective;
                s_heard = Time.unscaledTime;
                if (changed && m.objective != 0 && s_logged++ < 200)
                    CoopLog.Write("OBJ", "joiner: host objective=" + m.objective + " robotsKilled=" + m.killed + " customCount=" + m.custom +
                        " (local objective=" + (int)LevelCustomInfo.Objective + " robotsKilled=" + GameplayManager.m_total_robots_killed + " customCount=" + LevelCustomInfo.CustomCount + ")");
            }
            catch (Exception ex) { CoopLog.Error("CoopObjectives.OnState", ex); }
        }
    }

    /// OB1: host sends the objective counters (every frame check, sends on change + 1 Hz).
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class OB1_HostTick
    {
        static void Postfix()
        {
            if (!CoopConfig.IsHost) return;
            try { CoopObjectives.HostTick(); } catch (Exception ex) { CoopLog.Error("OB1", ex); }
        }
    }

    /// OB2: joiner HUD draws the host's counters (swapped in for the duration of DrawHUD only).
    [HarmonyPatch(typeof(UIElement), "DrawHUD")]
    static class OB2_HudCounters
    {
        struct Saved { public bool on; public int killed, custom; }

        static void Prefix(out Saved __state)
        {
            __state = new Saved();
            if (!CoopObjectives.Fresh) return;
            __state.on = true;
            __state.killed = GameplayManager.m_total_robots_killed;
            __state.custom = LevelCustomInfo.CustomCount;
            GameplayManager.m_total_robots_killed = CoopObjectives.HostKilled;
            LevelCustomInfo.CustomCount = CoopObjectives.HostCustom;
        }

        static void Finalizer(Saved __state)
        {
            if (!__state.on) return;
            GameplayManager.m_total_robots_killed = __state.killed;
            LevelCustomInfo.CustomCount = __state.custom;
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class OB3_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(ObjNet.State, CoopObjectives.OnState);
        }
    }
}
