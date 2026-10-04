// olcoop 0.4.21: headlight state sync.
// 11:34 run (0.4.20): spectators followed a live ship whose copy on their machine had headlightsOn=False (all three headlight lights
// off, fill light at 0.1), so they saw a dark level and no headlights. Stock Overload only toggles remote copies through
// RpcToggleHeadlights, which (a) is only sent for ships the host doesn't own locally - the host's own toggles never reach joiners,
// (b) is a toggle, so one missed/ignored message leaves a copy inverted for good, and (c) is dropped on clients unless
// NetworkMatch.InGameplay. Respawns also reset it (after respawn logs: headlights=False on remote copies).
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

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class LT1_Tick
    {
        static void Postfix() { try { CoopLights.Tick(); } catch (Exception ex) { CoopLog.Error("LT1", ex); } }
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
        static void Prefix() { if (CoopConfig.Active) CoopLights.Reset(); }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class LT4_ServerHandlers
    {
        static void Postfix() { CoopConfig.EnsureInit(); if (CoopConfig.Active) NetworkServer.RegisterHandler(HNet.Report, CoopLights.OnReport); }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class LT5_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (CoopConfig.Active && Client.GetClient() != null) Client.GetClient().RegisterHandler(HNet.State, CoopLights.OnState);
        }
    }
}
