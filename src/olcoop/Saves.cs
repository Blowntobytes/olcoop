// 0.10.1: co-op saves for an exact group of players (user, 10-21; replaces the 0.10.0 slot-based version):
//  - No between-level save. At the start of every campaign level the host records every player's loadout as the level began
//    (the host's own GameplayManager.m_level_start_player_data - the same snapshot RESTART LEVEL uses - and each joiner's first
//    loadout report of the level, which already includes the carry-over and the upgrades bought between levels).
//  - The record belongs to the exact group: host + joiners, by Steam account (IP joiners: pilot name). Bits/Josh/Void and Bits/Josh
//    are two different saves; a group that never played together has none.
//  - LOAD CO-OP GAME (host, CO-OP screen) is offered only when the players in the session are exactly a saved group. It always
//    starts that level from the beginning, and every player gets the loadout they had at the beginning of it.
//  - The stock LOAD SAVED GAME / LOAD GAME is greyed out while a co-op session has joiners (saves are single-player).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Combat
{
    public static class CoopSaves
    {
        public const short MsgRestore = 216; // H->J LoadoutMsg: your loadout at the start of this level, from the group's co-op save

        public class Party
        {
            public string Key, Names, Mission, LevelFile, LevelName, Saved; public int LevelNum, Difficulty;
            public string HostData; // Player.Serialize() JSON
            public Dictionary<string, LoadoutMsg> Joiners = new Dictionary<string, LoadoutMsg>();
        }

        static string Dir
        {
            get
            {
                string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(string.IsNullOrEmpty(d) ? "." : d, "olcoop-saves");
            }
        }

        /// Who a connection is, across sessions: the Steam account, or (IP joiners) the pilot name.
        public static string IdOf(NetworkConnection c)
        {
            if (c == null) return null;
            var sc = c as OlCoop.SteamNet.SteamConnection;
            if (sc != null) return "steam:" + sc.Peer.m_SteamID;
            var n = OlCoop.Hud.CoopHud.NameOf(c.connectionId);
            return string.IsNullOrEmpty(n) ? null : "pilot:" + n.ToUpperInvariant();
        }
        static string MyId()
        {
            try { if (OlCoop.SteamNet.SteamLink.Available) return "steam:" + Steamworks.SteamUser.GetSteamID().m_SteamID; } catch { }
            return "pilot:" + (PilotManager.PilotName ?? "?").ToUpperInvariant();
        }

        static List<NetworkConnection> Joiners()
        {
            var l = new List<NetworkConnection>();
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && OlCoop.Session.CoopHost.Verified.Contains(c.connectionId)) l.Add(c);
            return l;
        }

        /// The exact group in the session right now (host + verified joiners), sorted. Null if someone can't be identified yet.
        public static string CurrentKey(out string names)
        {
            names = OlCoop.Hud.CoopHud.NameOf(0) ?? PilotManager.PilotName ?? "HOST";
            var ids = new List<string> { MyId() };
            foreach (var c in Joiners())
            {
                string id = IdOf(c);
                if (id == null) return null;
                ids.Add(id);
                names += " / " + (OlCoop.Hud.CoopHud.NameOf(c.connectionId) ?? "?");
            }
            if (ids.Count < 2) return null; // co-op saves need at least one joiner
            ids.Sort(StringComparer.Ordinal);
            return string.Join(",", ids.ToArray());
        }
        static string FileFor(string key)
        {
            uint h = 2166136261;
            foreach (char ch in key) { h ^= ch; h *= 16777619; }
            return Path.Combine(Dir, "party-" + h.ToString("x8") + ".txt");
        }

        // ------------------------------------------------------------ host: record each level's start
        static string s_rec_level; static float s_rec_since; static bool s_rec_done;
        static string s_host_start;
        static readonly Dictionary<string, LoadoutMsg> s_rec = new Dictionary<string, LoadoutMsg>();
        static readonly Dictionary<string, string> s_rec_name = new Dictionary<string, string>();

        static bool Recordable
        {
            get
            {
                var l = GameplayManager.Level;
                return CoopConfig.IsHost && NetworkServer.active && GameplayManager.LevelIsLoaded && l != null && !l.IsSecret &&
                       GameplayManager.IsMission && !GameplayManager.IsChallengeMode && l.Mission != null && l.Mission.FileName != "_EDITOR";
            }
        }

        public static void SkipThisLevel(string why) { s_rec_level = GameplayManager.Level != null ? GameplayManager.Level.FileName : null; s_rec_since = Time.realtimeSinceStartup; s_rec_done = true; CoopLog.Write("SAVE", "host: no co-op save this time: " + why); }
        public static void ResetForLevel() { s_rec_level = null; s_rec_done = false; s_rec.Clear(); s_rec_name.Clear(); s_host_start = null; }

        /// Host, every frame in a level: once every joiner reported its start loadout (or after 20 s), write the group's save.
        public static void HostTick()
        {
            if (!Recordable) return;
            if (GameplayManager.m_level_start_player_data != null && GameManager.m_local_player != null) HostShipStarted();
            var lvl = GameplayManager.Level;
            if (s_rec_level != lvl.FileName) { s_rec_level = lvl.FileName; s_rec_since = Time.realtimeSinceStartup; s_rec_done = false; }
            if (s_rec_done) return;
            if (s_host_start == null && GameplayManager.m_level_start_player_data != null) s_host_start = GameplayManager.m_level_start_player_data.ToString(Newtonsoft.Json.Formatting.None);
            if (s_restored_level == lvl.FileName) { s_rec_done = true; CoopLog.Write("SAVE", "host: level loaded from the co-op save; its start loadouts are already saved"); return; }
            var js = Joiners();
            int have = 0;
            foreach (var c in js) { string id = IdOf(c); if (id != null && s_rec.ContainsKey(id)) have++; }
            bool all = js.Count > 0 && have == js.Count;
            if (!all && Time.realtimeSinceStartup - s_rec_since < 20f) return;
            s_rec_done = true;
            if (have == 0 || s_host_start == null) { CoopLog.Write("SAVE", "host: no co-op save for '" + lvl.FileName + "' (" + (have == 0 ? "no joiner loadouts" : "no host start data") + ")"); return; }
            Write(lvl, js);
        }

        static void Write(LevelInfo lvl, List<NetworkConnection> js)
        {
            var ids = new List<string> { MyId() };
            string names = OlCoop.Hud.CoopHud.NameOf(0) ?? PilotManager.PilotName ?? "HOST";
            var lines = new List<string>();
            foreach (var c in js)
            {
                string id = IdOf(c); LoadoutMsg m;
                if (id == null || !s_rec.TryGetValue(id, out m)) continue; // joined too late for this level's start: not in the group
                ids.Add(id);
                string n; s_rec_name.TryGetValue(id, out n);
                names += " / " + (n ?? "?");
                var nw = new NetworkWriter(); m.Serialize(nw);
                lines.Add("joiner=" + id + "|" + (n ?? "?").Replace("|", "/") + "|" + Convert.ToBase64String(nw.ToArray()));
            }
            ids.Sort(StringComparer.Ordinal);
            string key = string.Join(",", ids.ToArray());
            string file = FileFor(key);
            try
            {
                Directory.CreateDirectory(Dir);
                using (var w = new StreamWriter(file, false))
                {
                    w.WriteLine("# olcoop co-op save (" + CoopVersion.Full + ") - " + names);
                    w.WriteLine("party=" + key);
                    w.WriteLine("names=" + names);
                    w.WriteLine("saved=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    w.WriteLine("mission=" + lvl.Mission.FileName);
                    w.WriteLine("level=" + lvl.LevelNum + "|" + lvl.FileName + "|" + lvl.DisplayName);
                    w.WriteLine("difficulty=" + (int)GameplayManager.DifficultyLevel);
                    w.WriteLine("host=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s_host_start)));
                    foreach (var l in lines) w.WriteLine(l);
                }
                CoopLog.Write("SAVE", "host: co-op save for " + names + " = start of '" + lvl.FileName + "' (" + file + ")");
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.Write", ex); }
        }

        /// Host, CoopLoadout.OnLoadout: a joiner's ship reported its loadout. The first report of a level is its start loadout;
        /// in a level loaded from the co-op save it gets the saved one back instead.
        public static void HostOnLoadout(NetworkConnection conn, LoadoutMsg m)
        {
            string id = IdOf(conn);
            if (id == null) return;
            if (s_pending != null && GameplayManager.Level != null && string.Compare(GameplayManager.Level.FileName, s_pending.LevelFile, true) == 0)
            {
                LoadoutMsg saved;
                if (!s_restored.Contains(id) && s_pending.Joiners.TryGetValue(id, out saved))
                {
                    s_restored.Add(id);
                    conn.Send(MsgRestore, saved);
                    CoopLog.Write("SAVE", "host: gave " + id + " (conn " + conn.connectionId + ") its start-of-level loadout: " + saved.Describe());
                }
                return;
            }
            if (!Recordable || s_rec_done || s_rec.ContainsKey(id)) return;
            s_rec[id] = m;
            s_rec_name[id] = OlCoop.Hud.CoopHud.NameOf(conn.connectionId) ?? "?";
        }

        // ------------------------------------------------------------ host: load
        static Party s_pending;
        static string s_restored_level;
        static readonly HashSet<string> s_restored = new HashSet<string>();
        static Party s_menu_party; static string s_menu_key; static float s_menu_next;

        public static Party Read(string file)
        {
            if (!File.Exists(file)) return null;
            var p = new Party();
            foreach (var line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                if (k == "party") p.Key = v;
                else if (k == "names") p.Names = v;
                else if (k == "saved") p.Saved = v;
                else if (k == "mission") p.Mission = v;
                else if (k == "difficulty") int.TryParse(v, out p.Difficulty);
                else if (k == "host") p.HostData = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(v));
                else if (k == "level") { var a = v.Split('|'); int.TryParse(a[0], out p.LevelNum); p.LevelFile = a.Length > 1 ? a[1] : ""; p.LevelName = a.Length > 2 ? a[2] : p.LevelFile; }
                else if (k == "joiner")
                {
                    var a = v.Split('|');
                    if (a.Length < 3) continue;
                    var m = new LoadoutMsg(); m.Deserialize(new NetworkReader(Convert.FromBase64String(a[2])));
                    p.Joiners[a[0]] = m;
                }
            }
            return p.Key != null && p.HostData != null && p.Mission != null ? p : null;
        }

        /// CO-OP screen: the save of the exact group in the session (checked twice a second), or null.
        public static Party SaveForSession()
        {
            if (!CoopConfig.IsHost) return null;
            if (Time.realtimeSinceStartup < s_menu_next) return s_menu_party;
            s_menu_next = Time.realtimeSinceStartup + 0.5f;
            string names;
            string key = CurrentKey(out names);
            if (key == s_menu_key) return s_menu_party;
            s_menu_key = key;
            s_menu_party = null;
            if (key != null) { try { var p = Read(FileFor(key)); if (p != null && p.Key == key) s_menu_party = p; } catch (Exception ex) { CoopLog.Error("CoopSaves.SaveForSession", ex); } }
            CoopLog.Write("SAVE", "host: session group " + (key == null ? "(no joiners)" : names) + " -> " + (s_menu_party != null ? "co-op save at '" + s_menu_party.LevelFile + "' from " + s_menu_party.Saved : "no co-op save"));
            return s_menu_party;
        }

        /// LOAD CO-OP GAME: start the saved level from the beginning; loadouts are handed back as each ship starts.
        public static bool Load()
        {
            var p = SaveForSession();
            if (p == null) return false;
            string err = null;
            var mission = GameManager.OpenMission(p.Mission, ref err);
            if (mission == null) { OlCoop.SteamNet.SteamLink.LastStatus = "CAN'T OPEN THE SAVED CAMPAIGN (" + p.Mission + ")"; CoopLog.Write("SAVE", "host: load failed, mission " + p.Mission + ": " + err); return false; }
            int idx = mission.FindLevelIndex(p.LevelFile);
            if (idx < 0) idx = p.LevelNum;
            GameplayManager.DifficultyLevel = p.Difficulty;
            MenuManager.m_selected_mission = mission;
            GameplayManager.CreateNewGame(mission, idx);
            s_pending = p; s_restored.Clear(); s_restored_level = p.LevelFile; s_host_restored = false;
            CoopLog.Write("SAVE", "host: LOAD CO-OP GAME for " + p.Names + ": '" + p.LevelFile + "' (index " + idx + ") difficulty " + p.Difficulty);
            UIManager.DestroyAll();
            MenuManager.ChangeMenuState(MenuState.PLAY_GAME);
            return true;
        }
        static bool s_host_restored;

        /// Host's ship started in the loaded level: its start-of-level state back (the same call RESTART LEVEL makes).
        public static void HostShipStarted()
        {
            if (s_pending == null || s_host_restored || GameplayManager.Level == null || string.Compare(GameplayManager.Level.FileName, s_pending.LevelFile, true) != 0) return;
            s_host_restored = true;
            try
            {
                GameManager.m_local_player.Deserialize(JObject.Parse(s_pending.HostData));
                GameplayManager.m_level_start_player_data = GameManager.m_local_player.Serialize();
                CoopLog.Write("SAVE", "host: our start-of-level loadout restored from the co-op save");
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.HostShipStarted", ex); }
        }

        /// A different level (or a new game): the loaded save is done.
        public static void LevelChanged()
        {
            if (s_pending != null && (GameplayManager.Level == null || string.Compare(GameplayManager.Level.FileName, s_pending.LevelFile, true) != 0)) { s_pending = null; s_restored_level = null; }
            ResetForLevel();
        }

        // ------------------------------------------------------------ joiner
        public static void OnRestore(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LoadoutMsg>();
                var lp = GameManager.m_local_player;
                if (lp == null) return;
                CoopLoadout.ApplyLocal(lp, m);
                CoopLoadout.SendSoon(); // the host's copy of us must match
                GameplayManager.AddHUDMessage("CO-OP: SAVED GAME LOADED - YOUR LOADOUT IS BACK", -1, true);
                CoopLog.Write("SAVE", "joiner: start-of-level loadout from the co-op save applied: " + m.Describe());
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.OnRestore", ex); }
        }

        /// The stock single-player load is off while a co-op session has joiners (or for a joiner).
        public static bool StockLoadBlocked { get { return CoopConfig.IsJoiner || (CoopConfig.IsHost && Joiners().Count > 0); } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class SV4_HostRecord
    {
        static void Postfix() { if (CoopConfig.IsHost) { try { CoopSaves.HostTick(); } catch (Exception ex) { CoopLog.Error("SV4", ex); } } }
    }

    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class SV6_LevelChanged
    {
        static bool s_from_secret;
        static void Prefix() { s_from_secret = GameplayManager.Level != null && GameplayManager.Level.IsSecret; }
        static void Postfix() { if (CoopConfig.IsHost) { try { CoopSaves.LevelChanged(); if (s_from_secret) CoopSaves.SkipThisLevel("back from the secret level (mid-level, not a level start)"); } catch (Exception ex) { CoopLog.Error("SV6", ex); } } }
    }

    /// Stock LOAD SAVED GAME (main menu 5) and LOAD GAME (pause menu 4) greyed out in a co-op session with joiners.
    [HarmonyPatch(typeof(UIElement), "SelectAndDrawItem", new[] { typeof(string), typeof(Vector2), typeof(int), typeof(bool), typeof(float), typeof(float) })]
    static class SV8_GreyStockLoad
    {
        static void Prefix(UIElement __instance, int selection, ref bool fade)
        {
            if (!CoopConfig.Active) return;
            if (((__instance.m_type == UIElementType.MAIN_MENU && selection == 5) || (__instance.m_type == UIElementType.PAUSE_MENU && selection == 4)) && CoopSaves.StockLoadBlocked) fade = true;
        }
    }

    [HarmonyPatch(typeof(MenuManager), "ChangeMenuState")]
    static class SV9_NoStockLoad
    {
        static bool s_logged;
        static bool Prefix(MenuState new_state)
        {
            if (new_state != MenuState.LOAD_MENU || !CoopConfig.Active || !CoopSaves.StockLoadBlocked) return true;
            if (!s_logged) { s_logged = true; CoopLog.Write("SAVE", "stock LOAD SAVED GAME blocked in a co-op session (host: CO-OP > LOAD GAME)"); }
            OlCoop.SteamNet.SteamLink.LastStatus = "IN CO-OP USE CO-OP > LOAD GAME";
            GameplayManager.AddHUDMessage("CO-OP: USE CO-OP > LOAD GAME", -1, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class SV5_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(CoopSaves.MsgRestore, CoopSaves.OnRestore);
        }
    }
}
