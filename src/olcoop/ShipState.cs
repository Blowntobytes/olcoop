// Ship state sync: headlights and boost.
// Stock Overload only toggles remote headlight copies through
// RpcToggleHeadlights, which (a) is only sent for ships the host doesn't own locally - the host's own toggles never reach joiners,
// (b) is a toggle, so one missed/ignored message leaves a copy inverted for good, and (c) is dropped on clients unless
// NetworkMatch.InGameplay. Respawns also reset it.
// Now every player reports its own headlight state to the host, the host sends the state of every ship to everyone, and each copy
// is set (not toggled) to match. The stock RPC is ignored in co-op.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Lights
{
    public static class HNet
    {
        public const short Report = 190; // J->H LightMsg (own ship)
        public const short State = 191;  // H->J LightMsg per ship
        public const short BoostReport = 192; // J->H LightMsg (own ship boosting)
        public const short BoostState = 193;  // H->J LightMsg per ship
    }

    public class LightMsg : MessageBase
    {
        public uint netId;
        public bool on;
        public override void Serialize(NetworkWriter w) { w.Write(netId); w.Write(on); }
        public override void Deserialize(NetworkReader r) { netId = r.ReadUInt32(); on = r.ReadBoolean(); }
    }

    public static class CoopLights
    {
        static readonly MethodInfo m_toggle = AccessTools.Method(typeof(PlayerShip), "ToggleHeadlights");
        static readonly Dictionary<uint, bool> s_reported = new Dictionary<uint, bool>(); // host: joiner ship netId -> state
        static float s_next_send;
        static bool s_last_local, s_have_local;
        static readonly HashSet<uint> s_logged = new HashSet<uint>();

        public static bool Active { get { return OlCoop.World.CoopWorld.Active && GameplayManager.LevelIsLoaded; } }

        static PlayerShip ShipOf(uint netId)
        {
            foreach (var p in Overload.NetworkManager.m_Players)
                if (p != null && p.netId.Value == netId) return p.c_player_ship;
            return null;
        }

        /// Set a ship copy's headlights to `on` (ToggleHeadlights keeps lights, flares and brightness consistent).
        public static void Set(PlayerShip s, bool on, string why)
        {
            if (s == null || s.m_headlights_on == on || m_toggle == null) return;
            m_toggle.Invoke(s, null);
            uint id = s.c_player.netId.Value;
            if (s_logged.Add(id) || why != "sync") CoopLog.Write("LIGHT", "netId=" + id + " headlights " + (on ? "ON" : "OFF") + " (" + why + ")");
        }

        public static void Tick()
        {
            if (!Active) return;
            var me = GameManager.m_player_ship;
            bool changed = me != null && (!s_have_local || me.m_headlights_on != s_last_local);
            if (me != null) { s_last_local = me.m_headlights_on; s_have_local = true; }
            if (!changed && Time.realtimeSinceStartup < s_next_send) return;
            s_next_send = Time.realtimeSinceStartup + 1f;

            if (OlCoop.World.CoopWorld.IsHost)
            {
                foreach (var p in Overload.NetworkManager.m_Players)
                {
                    if (p == null || p.c_player_ship == null) continue;
                    var s = p.c_player_ship;
                    uint id = p.netId.Value;
                    bool on;
                    if (p.isLocalPlayer) on = s.m_headlights_on;
                    else if (s_reported.TryGetValue(id, out on)) Set(s, on, "sync");
                    else continue;
                    var m = new LightMsg { netId = id, on = on };
                    foreach (var c in NetworkServer.connections)
                        if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(HNet.State, m);
                }
            }
            else if (OlCoop.World.CoopWorld.IsJoiner && me != null)
            {
                var c = Client.GetClient();
                if (c != null && Client.IsConnected()) c.Send(HNet.Report, new LightMsg { netId = me.c_player.netId.Value, on = me.m_headlights_on });
            }
        }

        /// Host: a joiner reports its own headlights.
        public static void OnReport(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LightMsg>();
                bool prev;
                bool isNew = !s_reported.TryGetValue(m.netId, out prev) || prev != m.on;
                s_reported[m.netId] = m.on;
                if (isNew) { Set(ShipOf(m.netId), m.on, "joiner report"); s_next_send = 0f; }
            }
            catch (Exception ex) { CoopLog.Error("Lights.OnReport", ex); }
        }

        /// Joiner: the host's view of every ship's headlights. Our own ship is ours to control.
        public static void OnState(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LightMsg>();
                var me = GameManager.m_local_player;
                if (me != null && me.netId.Value == m.netId) return;
                Set(ShipOf(m.netId), m.on, "sync");
            }
            catch (Exception ex) { CoopLog.Error("Lights.OnState", ex); }
        }

        public static void Reset() { s_reported.Clear(); s_logged.Clear(); s_have_local = false; s_next_send = 0f; }
    }

    /// boost flames/sound on other players' ships. Stock sends RpcSetBoosting only from the host's simulated copy, which can
    /// disagree with what the owner actually does (the host decides a joiner's boost from forwarded inputs, heat and unlocks), and
    /// remote copies on clients can be overwritten by snapshots. Now each player's own boost state is reported to the host and
    /// relayed to everyone; every non-local copy shows exactly the owner's state (start/stop effects like the stock RPC).
    public static class CoopBoost
    {
        static readonly Dictionary<uint, bool> s_want = new Dictionary<uint, bool>();   // netId -> owner's boost state
        static readonly Dictionary<uint, int> s_changes = new Dictionary<uint, int>();
        static bool s_last, s_have;
        static float s_next;

        static bool s_seen_on, s_dbg_last; static int s_dbg_n;
        /// Local ship, every physics step (not re-simulation): remember a boost even if it ended before the frame.
        public static void SeenStep(PlayerShip s) { if (s.m_boosting) s_seen_on = true; }
        static bool PressingBoost(PlayerShip s)
        {
            var p = s.c_player;
            return p != null && p.m_unlock_boost && p.IsPressed(CCInput.USE_BOOST) && s.m_boost_overheat_timer <= 0f && !(bool)s.m_dying && !(bool)s.m_dead;
        }

        public static bool Wanted(uint id, out bool on) { return s_want.TryGetValue(id, out on); }

        static PlayerShip ShipOf(uint netId)
        {
            foreach (var p in Overload.NetworkManager.m_Players)
                if (p != null && p.netId.Value == netId) return p.c_player_ship;
            return null;
        }

        /// Show `on` on a non-local copy, with the stock start/stop effects.
        public static void Apply(PlayerShip s)
        {
            if (s == null || s.isLocalPlayer) return;
            bool on;
            if (!s_want.TryGetValue(s.c_player.netId.Value, out on) || s.m_boosting == on) return;
            if (on) s.UpdateBoostLoop(); else s.BoostStopped();
            s.m_boosting = on;
        }

        static void Set(uint id, bool on, string why)
        {
            bool prev;
            if (s_want.TryGetValue(id, out prev) && prev == on) return;
            s_want[id] = on;
            int n; s_changes.TryGetValue(id, out n); s_changes[id] = ++n;
            if (n <= 6) CoopLog.Write("BOOST", "netId=" + id + " boost " + (on ? "ON" : "off") + " (" + why + ")" + (n == 6 ? " (further changes not logged)" : ""));
            Apply(ShipOf(id));
        }

        public static void Tick()
        {
            if (!CoopLights.Active) return;
            var me = GameManager.m_player_ship;
            // 0.5.2: 13:33/13:44 runs (0.5.1) never reported a boost ON from anyone - sampling m_boosting once per frame missed it.
            // Use what the physics steps saw since the last frame, plus the boost button itself (same conditions as the game).
            bool now = me != null && (s_seen_on || me.m_boosting || PressingBoost(me));
            s_seen_on = false;
            if (me != null && now != s_dbg_last && s_dbg_n < 8)
            {
                s_dbg_n++; s_dbg_last = now;
                CoopLog.Write("BOOST", "my boost " + (now ? "ON" : "off") + " (flag=" + me.m_boosting + " button=" + me.c_player.IsPressed(CCInput.USE_BOOST) +
                    " unlock=" + me.c_player.m_unlock_boost + " overheat=" + me.m_boost_overheat_timer.ToString("F1") + ")" + (s_dbg_n == 8 ? " (further changes not logged)" : ""));
            }
            bool changed = me != null && (!s_have || now != s_last);
            if (me != null) { s_last = now; s_have = true; }
            if (!changed && Time.realtimeSinceStartup < s_next) return;
            s_next = Time.realtimeSinceStartup + 1f;
            if (OlCoop.World.CoopWorld.IsHost)
            {
                if (me != null) s_want[me.c_player.netId.Value] = s_last;
                foreach (var kv in s_want)
                {
                    var m = new LightMsg { netId = kv.Key, on = kv.Value };
                    foreach (var c in NetworkServer.connections)
                        if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(HNet.BoostState, m);
                }
            }
            else if (OlCoop.World.CoopWorld.IsJoiner && me != null)
            {
                var c = Client.GetClient();
                if (c != null && Client.IsConnected()) c.Send(HNet.BoostReport, new LightMsg { netId = me.c_player.netId.Value, on = s_last });
            }
        }

        public static void OnReport(NetworkMessage msg)
        {
            try { var m = msg.ReadMessage<LightMsg>(); Set(m.netId, m.on, "joiner"); }
            catch (Exception ex) { CoopLog.Error("Boost.OnReport", ex); }
        }

        public static void OnState(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LightMsg>();
                var me = GameManager.m_local_player;
                if (me != null && me.netId.Value == m.netId) return;
                Set(m.netId, m.on, "host");
            }
            catch (Exception ex) { CoopLog.Error("Boost.OnState", ex); }
        }

        public static void Reset() { s_want.Clear(); s_changes.Clear(); s_have = false; s_next = 0f; s_dbg_n = 0; }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class LT1_Tick
    {
        static void Postfix()
        {
            try { CoopLights.Tick(); } catch (Exception ex) { CoopLog.Error("LT1", ex); }
            try { CoopBoost.Tick(); } catch (Exception ex) { CoopLog.Error("LT1 boost", ex); }
        }
    }

    /// Stock boost RPC: ignored for ships whose owner state we have (we set it ourselves).
    [HarmonyPatch(typeof(Player), "RpcSetBoosting")]
    static class LT6_NoBoostRpc
    {
        static bool Prefix(Player __instance)
        {
            bool on;
            return !(OlCoop.World.CoopWorld.Active && __instance != null && CoopBoost.Wanted(__instance.netId.Value, out on));
        }
    }

    /// Host simulates joiner ships: keep the copy's boost flag (flames, sound) at the owner's state after the control step.
    [HarmonyPatch(typeof(PlayerShip), "FixedUpdateProcessControlsInternal")]
    static class LT7_KeepBoostAfterControls
    {
        static void Postfix(PlayerShip __instance)
        {
            if (__instance == null || !OlCoop.World.CoopWorld.Active) return;
            if (__instance.isLocalPlayer) { if (!NetworkSim.m_resimulating) CoopBoost.SeenStep(__instance); return; }
            bool on;
            if (__instance.c_player != null && CoopBoost.Wanted(__instance.c_player.netId.Value, out on)) __instance.m_boosting = on;
        }
    }

    /// Every peer, every frame before the ship's visuals update: non-local copies show the owner's boost state.
    [HarmonyPatch(typeof(PlayerShip), "Update")]
    static class LT8_BoostVisuals
    {
        static readonly HashSet<uint> s_logged = new HashSet<uint>();
        static void Prefix(PlayerShip __instance)
        {
            if (__instance == null || __instance.isLocalPlayer || __instance.c_player == null || !OlCoop.World.CoopWorld.Active) return;
            // 0.5.4: PlayerShip.Update only draws a ship's thruster flames (and their boost size) when c_player.m_remote_player is set
            // and m_pregame is not. Stock sets m_remote_player in Player.PrepareForMP (multiplayer spawn), which co-op ships don't go
            // through, so other players' ships never showed thrusters or boost flames (14:18 run: boost state arrived, nothing drawn).
            var p = __instance.c_player;
            if (!p.m_remote_player || p.m_pregame)
            {
                if (s_logged.Add(p.netId.Value))
                    CoopLog.Write("BOOST", "netId=" + p.netId.Value + ": thruster flames were off for this copy (remote=" + p.m_remote_player + " pregame=" + p.m_pregame + "); turned on");
                p.m_remote_player = true; p.m_pregame = false;
            }
            try { CoopBoost.Apply(__instance); } catch (Exception ex) { CoopLog.Error("LT8", ex); }
        }
    }

    /// Stock toggle RPC: ignored in co-op (state sync above sets every copy).
    [HarmonyPatch(typeof(PlayerShip), "RpcToggleHeadlights")]
    static class LT2_NoToggleRpc
    {
        static bool Prefix() { return !OlCoop.World.CoopWorld.Active; }
    }

    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class LT3_Reset
    {
        static void Prefix() { if (CoopConfig.Active) { CoopLights.Reset(); CoopBoost.Reset(); } }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class LT4_ServerHandlers
    {
        static void Postfix()
        {
            // Registered whenever the server registers its handlers: the role can be chosen later in the game.
            NetworkServer.RegisterHandler(HNet.Report, CoopLights.OnReport);
            NetworkServer.RegisterHandler(HNet.BoostReport, CoopBoost.OnReport);
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class LT5_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(HNet.State, CoopLights.OnState);
            Client.GetClient().RegisterHandler(HNet.BoostState, CoopBoost.OnState);
        }
    }
}
