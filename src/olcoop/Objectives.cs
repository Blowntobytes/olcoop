using System;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    /// <summary>
    /// Level objective counters on joiners.
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
        // 0.7.6 (protocol 30): the host's escape countdown. 0 = none, 1 = reactor, 2 = reactor (long countdown), 3 = other escape
        public byte escape; public float escapeTimer;
        public override void Serialize(NetworkWriter w) { w.Write(killed); w.Write(custom); w.Write(objective); w.Write(escape); w.Write(escapeTimer); }
        public override void Deserialize(NetworkReader r) { killed = r.ReadInt32(); custom = r.ReadInt32(); objective = r.ReadInt32(); escape = r.ReadByte(); escapeTimer = r.ReadSingle(); }
    }

    public static class CoopObjectives
    {
        // host
        static int s_sent_killed = int.MinValue, s_sent_custom = int.MinValue;
        static byte s_sent_escape, s_host_escape;
        /// Host: Reactor.Update -> GameplayManager.ReactorDestroyed(long) on the host's copy of the reactor.
        public static void HostReactorDestroyed(bool longCountdown) { s_host_escape = longCountdown ? (byte)2 : (byte)1; s_next_send = 0f; }
        public static void ResetForLevel() { s_sent_escape = 0; s_host_escape = 0; s_escape_logs = 0; }
        static int s_escape_logs;
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
            byte esc = GameplayManager.MustEscape ? (s_host_escape != 0 ? s_host_escape : (byte)3) : (byte)0;
            bool changed = k != s_sent_killed || c != s_sent_custom || esc != s_sent_escape;
            if (!changed && Time.unscaledTime < s_next_send) return;
            s_next_send = Time.unscaledTime + 1f;
            if (changed && obj != 0)
                CoopLog.Write("OBJ", "host: objective=" + obj + " robotsKilled=" + k + " customCount=" + c +
                    (obj == 1 ? " operatorsLeft=" + (c - k) : ""));
            if (esc != s_sent_escape) CoopLog.Write("OBJ", "host: escape " + esc + " timer " + GameplayManager.EscapeTimer.ToString("F1") + " sent to joiners");
            s_sent_killed = k; s_sent_custom = c; s_sent_escape = esc;
            var m = new ObjMsg { killed = k, custom = c, objective = obj, escape = esc, escapeTimer = GameplayManager.EscapeTimer };
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
                JoinerEscape(m);
                if (changed && m.objective != 0 && s_logged++ < 200)
                    CoopLog.Write("OBJ", "joiner: host objective=" + m.objective + " robotsKilled=" + m.killed + " customCount=" + m.custom +
                        " (local objective=" + (int)LevelCustomInfo.Objective + " robotsKilled=" + GameplayManager.m_total_robots_killed + " customCount=" + LevelCustomInfo.CustomCount + ")");
            }
            catch (Exception ex) { CoopLog.Error("CoopObjectives.OnState", ex); }
        }

        /// 0.7.6: joiners started the reactor escape only when their own copy of the reactor died (Reactor.Update). In the 20:27 run one
        /// joiner's copy died 35 s late, after the exit door: he had no countdown during the escape, then a fresh 40 s one in the exit
        /// flight. The host now starts it (ReactorDestroyed: message, sound, security level, timer) and keeps the timer in step.
        static void JoinerEscape(ObjMsg m)
        {
            if (m.escape == 0 || !GameplayManager.LevelIsLoaded) return;
            if (!GameplayManager.MustEscape)
            {
                if (GameplayManager.m_gameplay_state != GameplayState.PLAYING) return; // exit flight / menus: too late to matter
                if (m.escape == 1 || m.escape == 2)
                {
                    HostStarted = true;
                    try { GameplayManager.ReactorDestroyed(m.escape == 2); } finally { HostStarted = false; }
                    CoopLog.Write("OBJ", "joiner: reactor escape started by the host (long=" + (m.escape == 2) + ", host timer " + m.escapeTimer.ToString("F1") + ")");
                }
                else return; // other escapes (boss, alien) start from the same events on every machine
            }
            if (m.escapeTimer > 0f && Mathf.Abs(GameplayManager.EscapeTimer - m.escapeTimer) > 1f && GameplayManager.m_gameplay_state == GameplayState.PLAYING)
            {
                if (s_escape_logs++ < 10) CoopLog.Write("OBJ", "joiner: escape timer " + GameplayManager.EscapeTimer.ToString("F1") + " -> host's " + m.escapeTimer.ToString("F1"));
                GameplayManager.EscapeTimer = m.escapeTimer;
            }
        }
        public static bool HostStarted;
    }

    /// OB5 (0.7.6): reactor escape is the host's call. Host: note it for the joiners. Joiner: once started (by the host or its own reactor
    /// copy), a second ReactorDestroyed must not restart the countdown; and none at all after the exit door (exit flight, menus).
    [HarmonyPatch(typeof(GameplayManager), "ReactorDestroyed")]
    static class OB5_ReactorEscape
    {
        static bool Prefix(bool long_countdown)
        {
            try
            {
                if (CoopConfig.IsHost && CoopWorld.Active) { CoopObjectives.HostReactorDestroyed(long_countdown); return true; }
                if (!CoopWorld.IsJoiner || CoopObjectives.HostStarted) return true;
                if (GameplayManager.MustEscape || GameplayManager.m_gameplay_state != GameplayState.PLAYING)
                {
                    CoopLog.Write("OBJ", "joiner: our reactor copy died (escape " + (GameplayManager.MustEscape ? "already running" : "too late, state " + GameplayManager.m_gameplay_state) + "); kept the host's countdown");
                    return false;
                }
            }
            catch (Exception ex) { CoopLog.Error("OB5", ex); }
            return true;
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

    /// <summary>
    /// OB4: single-player score block on the HUD in co-op.
    /// UIElement.DrawHUDScoreInfo draws, when GameplayManager.IsMultiplayerActive, the MP block (match time, ping, anarchy/team
    /// mini scoreboard; olmod's MPScoreboards prefix adds its PvP variants), otherwise the single-player block: the
    /// DESTROYED counter (m_total_robots_killed) or, for LevelCustomInfo.Objective == DESTROY_BOTS, the OPERATORS counter.
    /// Co-op needs IsMultiplayerActive for the player netcode, so it is switched off only for the duration of this call (same
    /// pattern as Phase1Fixes SpRulesScope). With OB2 the counters show the host's (team) values.
    /// </summary>
    [HarmonyPatch(typeof(UIElement), "DrawHUDScoreInfo")]
    [HarmonyPriority(Priority.First)]
    static class OB4_SinglePlayerScoreInfo
    {
        static int s_logged;
        static void Prefix(out bool __state)
        {
            __state = false;
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || !GameplayManager.IsMultiplayerActive) return;
            GameplayManager.IsMultiplayerActive = false;
            __state = true;
            if (s_logged++ == 0) CoopLog.Write("OBJ", "HUD: single-player score block in co-op (objective=" + (int)LevelCustomInfo.Objective + ")");
        }

        static void Finalizer(bool __state)
        {
            if (__state) GameplayManager.IsMultiplayerActive = true;
        }
    }
}
