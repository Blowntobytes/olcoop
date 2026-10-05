// Session roster ("who is actually in my lobby") and round-trip time.
//  - Ping (msg 197, unreliable channel, both directions, 2/s): the sender stamps its own clock, the other side echoes it, the
//    sender computes the round trip. Host: per joiner connection. Joiner: to the host (used by the robot prediction in Robots.cs).
//  - Roster (msg 196, host -> joiners, 1/s and when it changes): every connected player with pilot name, Steam name, state and
//    ping. Shown on the CO-OP screen (main menu) and in the F8 window, for the host and for joiners.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Session
{
    public static class LNet2 { public const short Roster = 196, Ping = 197; public const int ChUnrel = 2; }

    public class PingMsg : MessageBase
    {
        public bool reply; public float t;
        public override void Serialize(NetworkWriter w) { w.Write(reply); w.Write(t); }
        public override void Deserialize(NetworkReader r) { reply = r.ReadBoolean(); t = r.ReadSingle(); }
    }

    public enum RosterState : byte { Connecting = 0, InMenus = 1, InLevel = 2, Dead = 3, Results = 4, Ready = 5 }

    public struct RosterEntry { public string pilot, steam; public RosterState state; public ushort ping; public bool host; }

    public class RosterMsg : MessageBase
    {
        public List<RosterEntry> e = new List<RosterEntry>();
        public override void Serialize(NetworkWriter w)
        {
            w.Write((byte)e.Count);
            foreach (var x in e) { w.Write(x.pilot ?? ""); w.Write(x.steam ?? ""); w.Write((byte)x.state); w.Write(x.ping); w.Write(x.host); }
        }
        public override void Deserialize(NetworkReader r)
        {
            int n = r.ReadByte(); e.Clear();
            for (int i = 0; i < n; i++)
                e.Add(new RosterEntry { pilot = r.ReadString(), steam = r.ReadString(), state = (RosterState)r.ReadByte(), ping = r.ReadUInt16(), host = r.ReadBoolean() });
        }
    }

    public static class CoopLobby
    {
        public const int MaxPlayers = 4; // Steam lobby size (host + 3)

        // ---- round trip
        static readonly Dictionary<int, float> s_rtt = new Dictionary<int, float>(); // host: conn id -> seconds (smoothed)
        static float s_my_rtt = -1f;                                                   // joiner: to the host
        static float s_next_ping, s_next_roster, s_next_log;
        static string s_last_roster_sig;

        /// Joiner: smoothed round trip to the host in seconds, or -1 before the first answer.
        public static float MyRtt { get { return s_my_rtt; } }
        public static int PingMs(int conn) { float v; return s_rtt.TryGetValue(conn, out v) ? Mathf.RoundToInt(v * 1000f) : -1; }

        static void Smooth(ref float acc, float sample) { acc = acc < 0f ? sample : acc + (sample - acc) * 0.2f; }

        public static void OnPing(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<PingMsg>();
                if (!m.reply) { msg.conn.SendByChannel(LNet2.Ping, new PingMsg { reply = true, t = m.t }, LNet2.ChUnrel); return; }
                float sample = Time.realtimeSinceStartup - m.t;
                if (sample < 0f || sample > 5f) return;
                if (CoopConfig.IsHost)
                {
                    float acc; if (!s_rtt.TryGetValue(msg.conn.connectionId, out acc)) acc = -1f;
                    Smooth(ref acc, sample); s_rtt[msg.conn.connectionId] = acc;
                }
                else Smooth(ref s_my_rtt, sample);
            }
            catch (Exception ex) { CoopLog.Error("OnPing", ex); }
        }

        // ---- roster (host builds; joiners receive)
        static List<RosterEntry> s_roster = new List<RosterEntry>();
        static float s_roster_time = -100f;

        public static void OnRoster(NetworkMessage msg)
        {
            try { s_roster = msg.ReadMessage<RosterMsg>().e; s_roster_time = Time.realtimeSinceStartup; }
            catch (Exception ex) { CoopLog.Error("OnRoster", ex); }
        }

        static RosterState HostOwnState()
        {
            if (GameManager.m_game_state == GameManager.GameState.GAMEPLAY && GameplayManager.LevelIsLoaded)
                return GameManager.m_player_ship != null && (bool)GameManager.m_player_ship.m_dead ? RosterState.Dead : RosterState.InLevel;
            return OlCoop.World.HostReady.On ? RosterState.Results : RosterState.InMenus;
        }

        static string SteamNameOf(NetworkConnection c)
        {
            var sc = c as OlCoop.SteamNet.SteamConnection;
            if (sc != null) { try { return OlCoop.SteamNet.SteamLink.Name(sc.Peer); } catch { } }
            return c.address ?? "";
        }

        public static List<RosterEntry> BuildHostRoster()
        {
            var l = new List<RosterEntry>();
            string me = OlCoop.Hud.CoopHud.Clean(PilotManager.PilotName);
            string mySteam = "";
            try { if (OlCoop.SteamNet.SteamLink.Available) mySteam = SteamFriends.GetPersonaName(); } catch { }
            l.Add(new RosterEntry { pilot = me, steam = mySteam, state = HostOwnState(), ping = 0, host = true });
            if (!NetworkServer.active) return l;
            bool inLevel = GameManager.m_game_state == GameManager.GameState.GAMEPLAY && GameplayManager.LevelIsLoaded;
            foreach (var c in NetworkServer.connections)
            {
                if (c == null || c.connectionId == 0 || !c.isConnected) continue;
                int id = c.connectionId;
                var st = RosterState.Connecting;
                if (CoopHost.Verified.Contains(id))
                {
                    st = RosterState.InMenus;
                    Player pl = null;
                    foreach (var p in Overload.NetworkManager.m_Players)
                        if (p != null && !p.isLocalPlayer && p.connectionToClient != null && p.connectionToClient.connectionId == id) pl = p;
                    if (OlCoop.World.HostReady.On) st = OlCoop.World.HostReady.IsReady(id) ? RosterState.Ready : RosterState.Results;
                    else if (pl != null && inLevel) st = pl.c_player_ship != null && (bool)pl.c_player_ship.m_dead ? RosterState.Dead : RosterState.InLevel;
                }
                int ms = PingMs(id);
                l.Add(new RosterEntry { pilot = OlCoop.Hud.CoopHud.NameOf(id) ?? "", steam = SteamNameOf(c), state = st,
                    ping = (ushort)Mathf.Clamp(ms < 0 ? 0 : ms, 0, 65535), host = false });
            }
            return l;
        }

        /// Rows to show: the host builds them live; a joiner shows the host's last roster (empty if none for 5 s).
        public static List<RosterEntry> Current()
        {
            if (CoopConfig.IsHost) return BuildHostRoster();
            if (CoopConfig.IsJoiner && Time.realtimeSinceStartup - s_roster_time < 5f) return s_roster;
            return new List<RosterEntry>();
        }

        public static string StateText(RosterState s)
        {
            switch (s)
            {
                case RosterState.Connecting: return "CONNECTING";
                case RosterState.InLevel: return "IN LEVEL";
                case RosterState.Dead: return "DEAD";
                case RosterState.Results: return "LEVEL RESULTS";
                case RosterState.Ready: return "READY";
                default: return "IN MENUS";
            }
        }

        public static string ShortName(RosterEntry e)
        {
            string n = string.IsNullOrEmpty(e.pilot) ? (e.steam ?? "?").ToUpperInvariant() : e.pilot;
            return n.Length > 18 ? n.Substring(0, 17) + "." : n;
        }

        public static string Line(RosterEntry e)
        {
            string name = string.IsNullOrEmpty(e.pilot) ? (e.steam ?? "?").ToUpperInvariant() : e.pilot;
            if (!string.IsNullOrEmpty(e.steam) && !string.IsNullOrEmpty(e.pilot) && e.steam.ToUpperInvariant() != e.pilot) name += " (" + e.steam.ToUpperInvariant() + ")";
            if (name.Length > 30) name = name.Substring(0, 29) + ".";
            string ping = e.host ? "HOST" : (e.ping > 0 ? e.ping + " MS" : "-");
            return name + "  -  " + StateText(e.state) + "  -  " + ping;
        }

        // ---- per frame (menus and levels)
        public static void Tick()
        {
            if (!CoopConfig.Active) return;
            float now = Time.realtimeSinceStartup;
            if (now >= s_next_ping)
            {
                s_next_ping = now + 0.5f;
                var ping = new PingMsg { reply = false, t = now };
                if (CoopConfig.IsHost && NetworkServer.active)
                {
                    foreach (var c in NetworkServer.connections)
                        if (c != null && c.connectionId != 0 && c.isConnected && CoopHost.Verified.Contains(c.connectionId)) c.SendByChannel(LNet2.Ping, ping, LNet2.ChUnrel);
                }
                else if (CoopConfig.IsJoiner)
                {
                    var cl = Client.GetClient();
                    if (cl != null && cl.isConnected && CoopClient.Welcomed) cl.SendByChannel(LNet2.Ping, ping, LNet2.ChUnrel);
                }
            }
            if (CoopConfig.IsHost && NetworkServer.active && now >= s_next_roster)
            {
                s_next_roster = now + 1f;
                var r = BuildHostRoster();
                var m = new RosterMsg { e = r };
                foreach (var c in NetworkServer.connections)
                    if (c != null && c.connectionId != 0 && c.isConnected && CoopHost.Verified.Contains(c.connectionId)) c.Send(LNet2.Roster, m);
                var sb = new System.Text.StringBuilder();
                foreach (var e in r) sb.Append(e.pilot).Append('/').Append(e.steam).Append('=').Append(StateText(e.state)).Append("; ");
                string sig = sb.ToString();
                if (sig != s_last_roster_sig) { s_last_roster_sig = sig; CoopLog.Write("LOBBY", r.Count + "/" + MaxPlayers + ": " + sig); }
            }
            if (now >= s_next_log)
            {
                s_next_log = now + 30f;
                if (CoopConfig.IsJoiner && s_my_rtt > 0f) CoopLog.Write("LOBBY", "joiner: round trip to host " + Mathf.RoundToInt(s_my_rtt * 1000f) + " ms");
                if (CoopConfig.IsHost && s_rtt.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var kv in s_rtt) sb.Append("conn ").Append(kv.Key).Append('=').Append(Mathf.RoundToInt(kv.Value * 1000f)).Append(" ms ");
                    CoopLog.Write("LOBBY", "host: round trip " + sb);
                }
            }
        }

        public static void Forget(int connId) { s_rtt.Remove(connId); }
        public static void ResetJoiner() { s_my_rtt = -1f; s_roster = new List<RosterEntry>(); s_roster_time = -100f; }
    }

    [HarmonyPatch(typeof(Overload.NetworkManager), "Update")]
    static class LB1_Tick
    {
        static void Postfix() { try { CoopLobby.Tick(); } catch (Exception ex) { CoopLog.Error("LB1", ex); } }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class LB2_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(LNet2.Ping, CoopLobby.OnPing);
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class LB3_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            CoopLobby.ResetJoiner();
            Client.GetClient().RegisterHandler(LNet2.Ping, CoopLobby.OnPing);
            Client.GetClient().RegisterHandler(LNet2.Roster, CoopLobby.OnRoster);
        }
    }
}
