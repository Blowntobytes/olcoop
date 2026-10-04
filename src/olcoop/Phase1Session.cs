using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.Session
{
    /// <summary>
    /// Phase 1, "approach A" (docs/phase1-design.md): a second game joins a single-player host.
    /// The host keeps playing the campaign as a normal MISSION game; its built-in UNET server
    /// (present even in SP) accepts the joiner, tells it which campaign level to load, and spawns a
    /// networked ship for it. Both sides switch on the player netcode (IsMultiplayerActive + PLAYING).
    /// Everything here is inert unless the game was started with -coophost or -coopjoin.
    /// </summary>
    public static class CoopNet
    {
        public const short MsgHello = 160;   // joiner -> host: "<protocol>|<version>"
        public const short MsgWelcome = 161; // host -> joiner: "OK|<host version>" or "REJECT|<reason>"
    }

    // ===================================================================== HOST

    public static class CoopHost
    {
        public static readonly HashSet<int> Verified = new HashSet<int>();
        /// Verified joiners still waiting for the host's level (e.g. the host was mid-load during a restart).
        public static readonly HashSet<int> Pending = new HashSet<int>();
        /// Earliest time (realtime) a pending conn may get the scene. After its old ship is destroyed, the joiner drops a scene message
        /// that arrives in the same burst (runs 6 and 7), so wait a moment.
        static readonly Dictionary<int, float> s_not_before = new Dictionary<int, float>();

        public static void FlushPending()
        {
            if (Pending.Count == 0 || !GameplayManager.LevelIsLoaded || !(GameplayManager.m_gameplay_state == GameplayState.PLAYING || GameplayManager.m_gameplay_state == GameplayState.AUTOMAP)) return;
            foreach (var id in new List<int>(Pending))
            {
                float nb; if (s_not_before.TryGetValue(id, out nb) && Time.realtimeSinceStartup < nb) continue;
                s_not_before.Remove(id);
                NetworkConnection c = null;
                foreach (var x in NetworkServer.connections) if (x != null && x.connectionId == id) c = x;
                if (c == null || !c.isConnected || !Verified.Contains(id)) { Pending.Remove(id); continue; }
                CoopLog.Write("HOST", "level is playing now; sending it to waiting conn " + id);
                SendScene(c);
            }
        }
        public static bool SpawnPosValid;
        public static Vector3 SpawnPos;
        public static Quaternion SpawnRot;
        static int s_spawned;

        public static bool Enabled { get { CoopConfig.EnsureInit(); return CoopConfig.IsHost; } }

        public static void SendScene(NetworkConnection conn)
        {
            var li = GameplayManager.m_level_info;
            if (li == null || !GameplayManager.LevelIsLoaded || !(GameplayManager.m_gameplay_state == GameplayState.PLAYING || GameplayManager.m_gameplay_state == GameplayState.AUTOMAP))
            {
                if (Pending.Add(conn.connectionId)) CoopLog.Write("HOST", "conn " + conn.connectionId + " verified; host not in a level yet, will send scene once the level is playing");
                return;
            }
            float nb0; if (s_not_before.TryGetValue(conn.connectionId, out nb0) && Time.realtimeSinceStartup < nb0) { Pending.Add(conn.connectionId); return; }
            Pending.Remove(conn.connectionId);
            string scene = li.FileName;
            if (conn.playerControllers != null && conn.playerControllers.Count > 0)
            {
                CoopLog.Write("HOST", "conn " + conn.connectionId + " still had " + conn.playerControllers.Count + " player(s) from the previous level; clearing, scene follows in 1.5 s");
                NetworkServer.DestroyPlayersForConnection(conn);
                s_not_before[conn.connectionId] = Time.realtimeSinceStartup + 1.5f;
                Pending.Add(conn.connectionId);
                return;
            }
            NetworkServer.SetClientNotReady(conn);
            NetworkServer.SendToClient(conn.connectionId, OlCoop.Death.DNet.Config, new OlCoop.Death.ConfigMsg { mode = (byte)OlCoop.Death.CoopSettings.Mode, delay = OlCoop.Death.CoopSettings.RespawnDelay, ff = OlCoop.Death.CoopSettings.FriendlyFire });
            NetworkServer.SendToClient(conn.connectionId, OlCoop.Robots.RNet.LevelInfo, new IntegerMessage((int)GameplayManager.DifficultyLevel));
            NetworkServer.SendToClient(conn.connectionId, 48, new StringMessage(scene));
            NetworkServer.SendToClient(conn.connectionId, 49, new StringMessage(scene));
            CoopLog.Write("HOST", "sent SceneLoad/SceneLoaded '" + scene + "' to conn " + conn.connectionId);
        }

        public static void OnHello(NetworkMessage msg)
        {
            try
            {
                string text = msg.ReadMessage<StringMessage>().value ?? "";
                string expected = CoopVersion.Protocol + "|" + CoopVersion.Full;
                if (text != expected)
                {
                    CoopLog.Write("HOST", "REJECT conn " + msg.conn.connectionId + ": client '" + text + "' != host '" + expected + "'");
                    msg.conn.Send(CoopNet.MsgWelcome, new StringMessage("REJECT|Host runs " + CoopVersion.Full + ", you run " + text.Substring(text.IndexOf('|') + 1)));
                    msg.conn.FlushChannels();
                    msg.conn.Disconnect();
                    return;
                }
                Verified.Add(msg.conn.connectionId);
                msg.conn.Send(CoopNet.MsgWelcome, new StringMessage("OK|" + CoopVersion.Full));
                CoopLog.Write("HOST", "conn " + msg.conn.connectionId + " (" + msg.conn.address + ") verified " + text);
                GameplayManager.AddHUDMessage("CO-OP: PLAYER JOINING", -1, true);
                SendScene(msg.conn);
            }
            catch (Exception ex) { CoopLog.Error("CoopHost.OnHello", ex); }
        }

        /// A point is usable if it lies inside a level segment (Physics alone can't tell "behind a wall" from open space,
        /// because level geometry is a hollow shell), there is clear line of sight from the level start, and it has room.
        const int GEOM_MASK = 67256321; // what robots use for line-of-sight: level geometry + doors, not ships

        static bool SpawnOk(Vector3 anchor, Vector3 p, out string why)
        {
            int seg = RobotManager.FindSegmentContainingWorldPosition(p, -1, false);
            if (seg < 0) { why = "outside level"; return false; }
            RaycastHit hit;
            if (Physics.Linecast(anchor, p, out hit, GEOM_MASK, QueryTriggerInteraction.Ignore)) { why = "blocked by " + hit.collider.name; return false; }
            if (Physics.CheckSphere(p, 1.6f, GEOM_MASK, QueryTriggerInteraction.Ignore)) { why = "no room"; return false; }
            if (Occupied(p, out why)) return false;
            why = "seg " + seg; return true;
        }

        // ---------------------------------------------------------------- occupancy (0.4.10)
        // 3-player run 06:33 (0.4.9): two joiners were placed on the same point at level load, respawn, lockdown and exit, because each
        // search only rotated its start in the same candidate list. Overlapping ships jam each other (joiners could only rotate).
        const float SHIP_GAP = 2.4f;
        /// The ship being placed (its current position doesn't count as occupied).
        public static PlayerShip IgnoreShip;
        static readonly List<KeyValuePair<Vector3, float>> s_reserved = new List<KeyValuePair<Vector3, float>>();
        static void Reserve(Vector3 p) { s_reserved.Add(new KeyValuePair<Vector3, float>(p, Time.realtimeSinceStartup)); }
        static bool Occupied(Vector3 p, out string why)
        {
            float now = Time.realtimeSinceStartup;
            s_reserved.RemoveAll(r => now - r.Value > 3f);
            foreach (var r in s_reserved) if ((r.Key - p).sqrMagnitude < SHIP_GAP * SHIP_GAP) { why = "taken by a ship just placed"; return true; }
            foreach (var pl in Overload.NetworkManager.m_Players)
            {
                if (pl == null || pl.c_player_ship == null || pl.c_player_ship == IgnoreShip || (bool)pl.c_player_ship.m_dead) continue;
                if ((pl.c_player_ship.c_transform.position - p).sqrMagnitude < SHIP_GAP * SHIP_GAP) { why = "occupied by netId=" + pl.netId.Value; return true; }
            }
            why = null; return false;
        }

        /// Joiners spawn next to the host's ship (where the action is), falling back to the level start.
        public static void NextSpawnPoint(ref LevelData.SpawnPoint result)
        {
            var anchors = new List<KeyValuePair<string, Transform>>();
            var hs = GameManager.m_player_ship;
            if (hs != null && !(bool)hs.m_dying && !(bool)hs.m_dead) anchors.Add(new KeyValuePair<string, Transform>("host ship", hs.c_transform));
            int start = s_spawned++;
            foreach (var a in anchors) if (FindNear(a.Key, a.Value.position, a.Value.rotation, start, ref result)) return;
            if (TryAround("level start", SpawnPos, SpawnRot, start, ref result)) return;
            result = new LevelData.SpawnPoint(SpawnPos, SpawnRot, 0);
            CoopLog.Write("HOST", "joiner spawn: no safe offset, using the level start itself (ships may overlap briefly)");
        }

        public static bool TryAroundPublic(string label, Vector3 anchor, Quaternion rot, int start, ref LevelData.SpawnPoint result) { return FindNear(label, anchor, rot, start, ref result); }

        /// Close offsets around the anchor first; if they're all in walls (tight spots such as some checkpoints), the nearest open
        /// segment centre reachable from the anchor's segment without passing a door.
        public static bool FindNear(string label, Vector3 anchor, Quaternion rot, int start, ref LevelData.SpawnPoint result)
        {
            if (TryAround(label, anchor, rot, start, ref result)) return true;
            return TrySegments(label, anchor, rot, start, ref result);
        }

        static bool TrySegments(string label, Vector3 anchor, Quaternion rot, int start, ref LevelData.SpawnPoint result)
        {
            var ld = GameManager.m_level_data;
            if (ld == null) return false;
            var segs = ld.Segments; var portals = ld.Portals;
            int seg0 = RobotManager.FindSegmentContainingWorldPosition(anchor, -1, false);
            if (seg0 < 0 || segs == null || seg0 >= segs.Length) { CoopLog.Write("HOST", "  segment search near " + label + ": anchor not in a segment"); return false; }
            var depth = new Dictionary<int, int> { { seg0, 0 } };
            var queue = new Queue<int>(); queue.Enqueue(seg0);
            var found = new List<int>(); var loose = new List<int>(); string occ;
            while (queue.Count > 0 && depth.Count < 64)
            {
                int s = queue.Dequeue(); int d = depth[s];
                var sd = segs[s];
                if (sd != null && RobotManager.FindSegmentContainingWorldPosition(sd.Center, -1, false) >= 0 && !Occupied(sd.Center, out occ))
                {
                    if (!Physics.CheckSphere(sd.Center, 1.6f, GEOM_MASK, QueryTriggerInteraction.Ignore)) found.Add(s);
                    else if (!Physics.CheckSphere(sd.Center, 1.0f, GEOM_MASK, QueryTriggerInteraction.Ignore)) loose.Add(s); // tight but open
                }
                if (d >= 4 || sd == null || sd.Portals == null) continue;
                foreach (int pi in sd.Portals)
                {
                    if (pi < 0 || portals == null || pi >= portals.Length) continue;
                    var pd = portals[pi];
                    if (pd == null || pd.DoorData != null) continue; // never through a door (could be locked)
                    int n = pd.MasterSegmentIndex == s ? pd.SlaveSegmentIndex : pd.MasterSegmentIndex;
                    if (n < 0 || n >= segs.Length || depth.ContainsKey(n)) continue;
                    depth[n] = d + 1; queue.Enqueue(n);
                }
            }
            if (found.Count == 0) found = loose;
            if (found.Count == 0) { CoopLog.Write("HOST", "  segment search near " + label + ": no free open segment within 4 steps of seg " + seg0); return false; }
            found.Sort((x, y) => (segs[x].Center - anchor).sqrMagnitude.CompareTo((segs[y].Center - anchor).sqrMagnitude));
            int pick = found[start % Math.Min(found.Count, 3)];
            Vector3 p = segs[pick].Center; Reserve(p);
            Quaternion r = Quaternion.LookRotation((anchor - p).sqrMagnitude > 0.01f ? (anchor - p).normalized : rot * Vector3.forward, rot * Vector3.up);
            result = new LevelData.SpawnPoint(p, r, 0);
            CoopLog.Write("HOST", "joiner spawn at " + p.ToString("F1") + " (centre of seg " + pick + ", " + depth[pick] + " step(s) from seg " + seg0 + ", " + (p - anchor).magnitude.ToString("F1") + "u) near " + label);
            return true;
        }

        static bool TryAround(string label, Vector3 anchor, Quaternion rot, int start, ref LevelData.SpawnPoint result)
        {
            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
            var candidates = new[] { -fwd * 4f, right * 4f, -right * 4f, up * 3f, -up * 3f, fwd * 4f, -fwd * 7f, right * 2.5f, -right * 2.5f, up * 2f, -fwd * 2.5f, fwd * 7f };
            for (int k = 0; k < candidates.Length; k++)
            {
                Vector3 off = candidates[(start + k) % candidates.Length];
                Vector3 p = anchor + off;
                string why;
                bool ok = SpawnOk(anchor, p, out why);
                CoopLog.Write("HOST", "  spawn candidate near " + label + " " + off.ToString("F1") + ": " + (ok ? "OK " : "rejected, ") + why);
                if (ok) { Reserve(p); result = new LevelData.SpawnPoint(p, rot, 0); CoopLog.Write("HOST", "joiner spawn at " + p.ToString("F1") + " near " + label); return true; }
            }
            return false;
        }

        public static void ResetForLevel() { SpawnPosValid = false; s_spawned = 0; }
    }

    /// H1: fixed listen port so joiners know where to connect.
    [HarmonyPatch(typeof(Server), "Listen")]
    static class H1_ServerListen
    {
        static void Prefix(ref int port)
        {
            if (!CoopHost.Enabled || port != 0) return;
            port = CoopConfig.Port;
            CoopLog.Write("HOST", "listening on fixed UDP port " + port);
        }
        static void Postfix(bool __result, int port)
        {
            if (CoopHost.Enabled && !__result)
                CoopLog.Write("ERROR", "host could not listen on UDP port " + port + " (in use?). Co-op joins will fail.");
        }
    }

    /// Register the handshake handler on the server.
    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class H2_ServerRegisterHandlers
    {
        static void Postfix()
        {
            if (!CoopHost.Enabled) return;
            NetworkServer.RegisterHandler(CoopNet.MsgHello, CoopHost.OnHello);
            CoopLog.Write("HOST", "registered co-op handshake handler");
        }
    }

    [HarmonyPatch(typeof(Server), "OnConnect")]
    static class H2b_ServerOnConnect
    {
        static void Postfix(NetworkMessage msg)
        {
            if (!CoopHost.Enabled || msg.conn.connectionId == 0) return;
            CoopLog.Write("HOST", "remote connection " + msg.conn.connectionId + " from " + msg.conn.address + " (awaiting handshake)");
        }
    }

    [HarmonyPatch(typeof(Server), "OnDisconnect")]
    static class H2c_ServerOnDisconnect
    {
        static void Postfix(NetworkMessage msg)
        {
            if (!CoopHost.Enabled || msg.conn.connectionId == 0) return;
            CoopHost.Verified.Remove(msg.conn.connectionId);
            CoopHost.Pending.Remove(msg.conn.connectionId);
            OlCoop.Hud.CoopHud.Forget(msg.conn.connectionId);
            CoopLog.Write("HOST", "remote connection " + msg.conn.connectionId + " disconnected");
            GameplayManager.AddHUDMessage("CO-OP: PLAYER LEFT", -1, true);
        }
    }

    /// H3: when the host enters a level, send it to every verified joiner.
    [HarmonyPatch(typeof(Server), "OnSceneLoad")]
    static class H3_ServerOnSceneLoad
    {
        static void Postfix(string name)
        {
            if (!CoopHost.Enabled || GameplayManager.IsMultiplayer) return;
            foreach (var conn in NetworkServer.connections)
                if (conn != null && conn.connectionId != 0 && conn.isConnected && CoopHost.Verified.Contains(conn.connectionId))
                    CoopHost.SendScene(conn);
        }
    }

    /// H4: remember where the level puts the host (LegacyPlayerStart) so joiners spawn next to it.
    [HarmonyPatch(typeof(PlayerShip), "SetSpawnPosAndRotation")]
    static class H4_RecordLevelStart
    {
        static void Prefix(GameObject legacy_player_start)
        {
            if (!CoopHost.Enabled || legacy_player_start == null) return;
            CoopHost.SpawnPos = legacy_player_start.transform.position;
            CoopHost.SpawnRot = legacy_player_start.transform.rotation;
            CoopHost.SpawnPosValid = true;
            CoopLog.Write("HOST", "level start recorded at " + CoopHost.SpawnPos.ToString("F1"));
        }
    }

    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class H4b_LevelAwakeReset
    {
        static void Prefix() { if (CoopHost.Enabled) CoopHost.ResetForLevel(); }
    }

    /// H5: joiners spawn beside the host instead of on top of it.
    /// Postfix (always runs, regardless of olmod's skipping prefix): joiners spawn beside the host.
    [HarmonyPatch(typeof(NetworkSpawnPoints), "ChooseSpawnPoint")]
    static class H5_ChooseSpawnPoint
    {
        static void Postfix(MpTeam team, ref LevelData.SpawnPoint __result)
        {
            if (!CoopHost.Enabled) return;
            CoopLog.Write("HOST", "ChooseSpawnPoint team=" + team + " isMP=" + GameplayManager.IsMultiplayer + " startValid=" + CoopHost.SpawnPosValid +
                " stock=" + (__result != null ? __result.position.ToString("F1") : "null") + " players=" + Overload.NetworkManager.m_Players.Count);
            if (GameplayManager.IsMultiplayer || !CoopHost.SpawnPosValid) return;
            try { CoopHost.NextSpawnPoint(ref __result); } catch (Exception ex) { CoopLog.Error("H5", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "OnAddPlayerMessage")]
    static class H6_OnAddPlayerLog
    {
        static void Prefix(NetworkMessage msg)
        {
            if (CoopHost.Enabled) CoopLog.Write("HOST", "AddPlayer request from conn " + msg.conn.connectionId);
        }
    }

    // ===================================================================== BOTH

    /// S1: switch on player netcode in a MISSION game on both peers (ship physics must match on both ends).
    [HarmonyPatch(typeof(NetworkMatch), "StartPreGame")]
    static class S1_StartPreGame
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            GameplayManager.IsMultiplayerActive = true;
            NetworkMatch.SetMatchState(MatchState.PLAYING);
            CoopSession.InLevel = true;
            GameplayManager.AddHUDMessage((CoopVersion.Full + (CoopConfig.IsHost ? " - HOST" : " - JOINED")).ToUpperInvariant(), -1, true);
            CoopLog.Write("NET", "co-op netcode ON (IsMultiplayerActive=true, match PLAYING) for " + (GameplayManager.m_level_info != null ? GameplayManager.m_level_info.FileName : "?"));
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
    static class S1b_DoneLevel
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            CoopSession.InLevel = false;
            GameplayManager.IsMultiplayerActive = false;
            CoopLog.Write("NET", "co-op netcode OFF (level done)");
        }
    }

    /// Our own "in a co-op level" flag, independent of game fields that stock code may reset.
    public static class CoopSession
    {
        public static bool InLevel;
        static int s_fixes;

        /// Watchdog: stock/olmod code (e.g. a MatchStart message) can reset IsMultiplayerActive or the match state mid-level.
        /// That silently turned off co-op death handling on a joiner (single-player death menu on 2nd death).
        public static void Watchdog()
        {
            if (!InLevel || !CoopConfig.Active || GameplayManager.IsMultiplayer || !GameplayManager.LevelIsLoaded) return;
            bool fixedSomething = false;
            if (!GameplayManager.IsMultiplayerActive) { GameplayManager.IsMultiplayerActive = true; fixedSomething = true; }
            if (NetworkMatch.GetMatchState() != MatchState.PLAYING) { NetworkMatch.SetMatchState(MatchState.PLAYING); fixedSomething = true; }
            if (fixedSomething && s_fixes++ < 20)
                CoopLog.Write("NET", "watchdog: co-op netcode had been switched off by the game; re-enabled (" + s_fixes + ")");
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class S2_Watchdog
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            try { CoopSession.Watchdog(); if (CoopHost.Enabled) CoopHost.FlushPending(); if (CoopClient.Enabled) CoopClient.AwaitTick(); } catch (Exception ex) { CoopLog.Error("S2", ex); }
        }
    }

    // ===================================================================== JOINER

    public static class CoopClient
    {
        public static bool ConnectIssued;
        public static bool Welcomed;
        static float s_next_try;
        static int s_tries;
        static float s_menu_since = -1f;

        public static bool Enabled { get { CoopConfig.EnsureInit(); return CoopConfig.IsJoiner; } }

        static float s_next_rejoin;
        static int s_rejoin_msgs;

        /// Ask for the host's level 1 s after reaching the main menu instead of 3 s.
        public static void RejoinSoon() { s_next_rejoin = Time.realtimeSinceStartup + 1f; s_rejoin_msgs = 0; }

        // Waiting inside the old level for the host's reloaded one (Hardcore / team wipe).
        static float s_await_next = -1f; static int s_await_tries;
        public static bool Awaiting { get { return s_await_next >= 0f; } }
        public static void AwaitLevel()
        {
            s_await_next = Time.realtimeSinceStartup + 2f; s_await_tries = 0;
            GameplayManager.AddHUDMessage("CO-OP: WAITING FOR THE HOST'S RESTARTED LEVEL", -1, true);
        }
        public static void LevelArrived() { if (s_await_next >= 0f) CoopLog.Write("JOIN", "host's level arrived after " + s_await_tries + " request(s)"); s_await_next = -1f; }
        public static void AwaitTick()
        {
            if (s_await_next < 0f || Time.realtimeSinceStartup < s_await_next) return;
            if (!Welcomed || !Client.IsConnected() || Client.GetClient() == null) { s_await_next = -1f; return; }
            if (s_await_tries++ >= 20) { CoopLog.Write("JOIN", "host's level never arrived; giving up"); s_await_next = -1f; return; }
            s_await_next = Time.realtimeSinceStartup + 3f;
            Client.GetClient().Send(CoopNet.MsgHello, new StringMessage(CoopVersion.Protocol + "|" + CoopVersion.Full));
            CoopLog.Write("JOIN", "still waiting for the host's level; asked again (" + s_await_tries + ")");
        }

        public static void MenuTick()
        {
            if (Welcomed && Client.IsConnected())
            {
                // Connected but back in the main menu (e.g. quit the level): ask the host to send us its level again.
                if (MenuManager.m_menu_state != MenuState.MAIN_MENU || MenuManager.m_menu_sub_state != MenuSubState.ACTIVE) { s_next_rejoin = -1f; s_rejoin_msgs = 0; return; }
                float t = Time.realtimeSinceStartup;
                if (s_next_rejoin < 0f) { s_next_rejoin = t + 3f; return; }
                // (RejoinSoon sets it before we get here, so a restart rejoins after ~1 s)
                if (t < s_next_rejoin) return;
                s_next_rejoin = t + 10f;
                string hello = CoopVersion.Protocol + "|" + CoopVersion.Full;
                Client.GetClient().Send(CoopNet.MsgHello, new StringMessage(hello));
                CoopLog.Write("JOIN", "in the main menu while connected; asked the host for its level again");
                if (s_rejoin_msgs++ == 0) GameplayManager.AddHUDMessage("CO-OP: REJOINING THE HOST'S LEVEL", -1, true);
                return;
            }
            if (Client.IsConnected() && ConnectIssued) { s_menu_since = -1f; return; }
            if (MenuManager.m_menu_sub_state != MenuSubState.ACTIVE || string.IsNullOrEmpty(PilotManager.ActivePilot)) { s_menu_since = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (s_menu_since < 0f) { s_menu_since = now; s_next_try = now + 1f; }
            if (now < s_next_try) return;
            if (s_tries >= 24) { if (s_tries == 24) { CoopLog.Write("JOIN", "giving up after 24 attempts"); GameplayManager.AddHUDMessage("CO-OP: COULD NOT REACH HOST", -1, true); s_tries++; } return; }
            s_tries++;
            s_next_try = now + 5f;
            ConnectIssued = true;
            CoopLog.Write("JOIN", "connecting to " + CoopConfig.JoinIp + ":" + CoopConfig.Port + " (attempt " + s_tries + ")");
            Client.Connect(CoopConfig.JoinIp, CoopConfig.Port);
        }

        public static void OnWelcome(NetworkMessage msg)
        {
            string text = msg.ReadMessage<StringMessage>().value ?? "";
            if (text.StartsWith("OK|"))
            {
                Welcomed = true;
                s_tries = 0;
                CoopLog.Write("JOIN", "host accepted us: " + text);
                OlCoop.Hud.CoopHud.SendMyName();
                GameplayManager.AddHUDMessage("CO-OP: CONNECTED, WAITING FOR HOST'S LEVEL", -1, true);
            }
            else
            {
                s_tries = 99; // do not retry against a mismatched host
                CoopLog.Write("JOIN", "host rejected us: " + text);
                GameplayManager.AddHUDMessage("CO-OP: " + text.Replace("REJECT|", "").ToUpperInvariant(), -1, true);
            }
        }

        public static void RetargetRobotsToLocalShip()
        {
            var ship = GameManager.m_player_ship;
            if (ship == null) return;
            Robot.c_target_transform = ship.transform;
            var f = AccessTools.Field(typeof(Robot), "m_player_ship");
            if (f != null) f.SetValue(null, ship);
            var tf = AccessTools.Field(typeof(PropReactorTurret), "c_target_transform");
            var tg = AccessTools.Field(typeof(PropReactorTurret), "c_target_go");
            int n = 0;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<PropReactorTurret>())
            {
                if (tf != null) tf.SetValue(t, ship.transform);
                if (tg != null) tg.SetValue(t, ship.gameObject);
                n++;
            }
            CoopLog.Write("JOIN", "robots/turrets (" + n + ") retargeted to our networked ship netId=" + ship.c_player.netId);
        }
    }

    /// C2: connect to the host from the main menu.
    [HarmonyPatch(typeof(MenuManager), "MainMenuUpdate")]
    static class C2_MainMenuConnect
    {
        static void Postfix()
        {
            if (!CoopClient.Enabled) return;
            try { CoopClient.MenuTick(); } catch (Exception ex) { CoopLog.Error("C2", ex); }
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class C2b_ClientRegisterHandlers
    {
        static void Postfix()
        {
            if (!CoopClient.Enabled || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(CoopNet.MsgWelcome, CoopClient.OnWelcome);
        }
    }

    [HarmonyPatch(typeof(Client), "OnConnectMsg")]
    static class C2c_ClientOnConnect
    {
        static void Postfix()
        {
            if (!CoopClient.Enabled || Overload.NetworkManager.IsServer() || Client.GetClient() == null) return;
            string hello = CoopVersion.Protocol + "|" + CoopVersion.Full;
            Client.GetClient().Send(CoopNet.MsgHello, new StringMessage(hello));
            CoopLog.Write("JOIN", "connected; sent handshake " + hello);
        }
    }

    [HarmonyPatch(typeof(Client), "OnDisconnectMsg")]
    static class C2d_ClientOnDisconnect
    {
        static void Postfix()
        {
            if (!CoopClient.Enabled || !CoopClient.ConnectIssued) return;
            bool wasWelcomed = CoopClient.Welcomed;
            CoopClient.Welcomed = false;
            CoopLog.Write("JOIN", "disconnected from host" + (wasWelcomed ? "" : " (before handshake)"));
            if (wasWelcomed) GameplayManager.AddHUDMessage("CO-OP: DISCONNECTED FROM HOST", -1, true);
        }
    }

    /// C5: when the host reloads a level it destroys every ship, including ours. Stock code reads "my ship was unspawned" as
    /// "match over" (ExitMatchToMainMenu: disconnect, IsMultiplayerActive off), which broke the joiner in Hardcore restarts.
    /// While we're connected to the host, stay put: the host sends its new level and C1 moves us into it.
    [HarmonyPatch(typeof(NetworkMatch), "ExitMatchToMainMenu")]
    static class C5_NoExitOnHostReload
    {
        static bool Prefix()
        {
            if (!C5b_UnspawnScope.Inside || !CoopClient.Enabled || !CoopClient.Welcomed || !Client.IsConnected()) return true;
            CoopLog.Write("JOIN", "host removed our ship (level reload); staying connected and waiting for the host's level");
            OlCoop.Death.Spectate.Stop(null);
            CoopClient.AwaitLevel();
            return false;
        }
    }

    /// Marks the stock unspawn handler, the only caller C5 should override (a joiner choosing QUIT still exits normally).
    [HarmonyPatch(typeof(NetworkSpawnPlayer), "NetworkUnspawnPlayerHandler")]
    static class C5b_UnspawnScope
    {
        public static bool Inside;
        static void Prefix() { Inside = true; }
        static Exception Finalizer(Exception __exception) { Inside = false; return __exception; }
    }

    /// C3: menus must not restart the joiner's own local server (that would drop the host connection).
    [HarmonyPatch(typeof(Overload.NetworkManager), "StartServerWithLocalConnection")]
    static class C3_NoLocalServerWhileJoined
    {
        static bool Prefix()
        {
            if (!CoopClient.Enabled || !CoopClient.ConnectIssued) return true;
            CoopLog.Write("JOIN", "blocked StartServerWithLocalConnection (keeping host connection)");
            return false;
        }
    }

    /// C1: resolve the host's campaign level in the story mission instead of the MP map list.
    [HarmonyPatch(typeof(Overload.NetworkManager), "LoadScene")]
    [HarmonyPriority(Priority.First)]
    static class C1_LoadSceneStory
    {
        static string s_last_scene; static float s_last_scene_at = -100f;
        static bool Prefix(string name)
        {
            if (!CoopClient.Enabled || name == null || name.Contains(":")) return true;
            var story = GameManager.StoryMission;
            if (story == null) { CoopLog.Write("JOIN", "LoadScene '" + name + "': no story mission loaded"); return true; }
            int idx = story.FindLevelIndex(name);
            if (idx < 0) return true;
            if (name == s_last_scene && Time.realtimeSinceStartup - s_last_scene_at < 6f)
            {
                CoopLog.Write("JOIN", "ignoring duplicate LoadScene '" + name + "' (already loading it)");
                return false;
            }
            s_last_scene = name; s_last_scene_at = Time.realtimeSinceStartup;
            CoopClient.LevelArrived();
            bool inLevel = GameManager.m_game_state == GameManager.GameState.GAMEPLAY || GameplayManager.LevelIsLoaded;
            CoopLog.Write("JOIN", "loading host's campaign level '" + name + "' (story index " + idx + ")" + (inLevel ? " from inside a level" : ""));
            if (inLevel) { OlCoop.Death.Spectate.Stop(null); GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit); }
            UIManager.DestroyAll(true);
            if (OlCoop.Robots.CoopRobots.HostDifficulty >= 0 && OlCoop.Robots.CoopRobots.HostDifficulty != (int)GameplayManager.DifficultyLevel)
            {
                CoopLog.Write("JOIN", "difficulty " + (int)GameplayManager.DifficultyLevel + " -> host's " + OlCoop.Robots.CoopRobots.HostDifficulty);
                GameplayManager.DifficultyLevel = OlCoop.Robots.CoopRobots.HostDifficulty;
            }
            GameplayManager.CreateNewGame(story, idx);
            if (inLevel) { GameplayManager.m_between_level_start = Time.realtimeSinceStartup; GameplayManager.SwitchToMenu(MenuState.PLAY_GAME); }
            else MenuManager.ChangeMenuState(MenuState.PLAY_GAME);
            return false;
        }
    }

    /// S3: a new local ship in a co-op level starts clean: no leftover MP death pause (static flag set by StartDying, survives the
    /// level reload - the Hardcore "panorama" view), no cinematic bars or screen fade. Logs where the camera is.
    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class S3_CleanLocalStart
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(Player __instance)
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try
            {
                bool dp = PlayerShip.DeathPaused, pg = __instance.m_pregame;
                PlayerShip.DeathPaused = false;
                __instance.m_pregame = false;
                UIManager.ShowCinematicBars(false);
                UIManager.SetScreenFade(0f);
                var cam = Camera.main; var ship = __instance.c_player_ship;
                CoopLog.Write("NET", "local ship start netId=" + __instance.netId.Value + ": cleared deathPaused=" + dp + " pregame=" + pg +
                    " cam=" + (cam != null ? cam.name + " parent=" + (cam.transform.parent != null ? cam.transform.parent.name : "null") : "null") +
                    " shipCam=" + (ship != null && cam != null && cam.transform == ship.c_camera_transform));
            }
            catch (Exception ex) { CoopLog.Error("S3", ex); }
        }
    }

    /// C4: after our networked ship replaces the temporary SP ship, re-aim local robots at it.
    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class C4_RetargetRobots
    {
        static void Postfix()
        {
            if (!CoopClient.Enabled || Server.IsActive()) return;
            try { CoopClient.RetargetRobotsToLocalShip(); } catch (Exception ex) { CoopLog.Error("C4", ex); }
        }
    }

    /// S4: the map key. Stock UpdateReadImmediateControls ignores VIEW_MAP whenever IsMultiplayerActive, which co-op sets for the
    /// netcode, so the automap was unreachable. Re-open it in co-op levels (the map never pauses the game in co-op).
    [HarmonyPatch(typeof(PlayerShip), "UpdateReadImmediateControls")]
    static class S4_CoopAutomap
    {
        static void Postfix(PlayerShip __instance)
        {
            try
            {
                if (!CoopConfig.Active || GameplayManager.IsMultiplayer || !CoopSession.InLevel || !__instance.isLocalPlayer) return;
                if ((int)__instance.m_wheel_select_state != 0 || (bool)__instance.m_dying || (bool)__instance.m_dead) return;
                if (GameplayManager.m_gameplay_state != GameplayState.PLAYING || GameplayManager.m_automap == null) return;
                if (!Controls.JustPressed(CCInput.VIEW_MAP)) return;
                CoopLog.Write("NET", "map key: opening automap (co-op)");
                GameplayManager.OpenAutomap();
            }
            catch (Exception ex) { CoopLog.Error("S4", ex); }
        }
    }
}
