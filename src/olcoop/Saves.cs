// 0.10.0: co-op saved loadouts (user: "like ProperSave in Risk of Rain 2" - the host leaves, rehosts, the same players join and
// everyone starts the level with the loadout they had).
// The stock campaign save only holds the host's own ship. Whenever the host's game saves a slot (between-level autosave, quicksave,
// checkpoint, manual slot - SaveLoad.SaveGame(int, bool)), the host also writes every joiner's loadout, read from its own copy of
// that joiner's ship (the host's copy is authoritative for ammo/energy and runs the pickups), to olcoop-saves\<pilot>-slot<N>.txt
// next to the mod. When the host loads that slot (SaveLoad.LoadGame(int)), each returning player - matched by Steam ID, or by pilot
// name for an IP joiner - gets their saved loadout (msg 216) as soon as their ship starts in that level. Players who weren't in the
// save start with the level's normal loadout. The between-level autosave restores to the start of the next level, like the host.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Combat
{
    public static class CoopSaves
    {
        public const short MsgRestore = 216; // H->J LoadoutMsg: your loadout from the host's saved game

        static readonly Dictionary<string, LoadoutMsg> s_latest = new Dictionary<string, LoadoutMsg>();
        static readonly Dictionary<string, string> s_latest_name = new Dictionary<string, string>();
        static float s_next_cache;

        // pending restore after the host loaded a slot
        static Dictionary<string, LoadoutMsg> s_pending;
        static string s_pending_level;
        static int s_pending_slot = -1;
        static readonly HashSet<string> s_restored = new HashSet<string>();

        static string Dir
        {
            get
            {
                string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(string.IsNullOrEmpty(d) ? "." : d, "olcoop-saves");
            }
        }
        static string FileFor(int slot)
        {
            string pilot = PilotManager.PilotName ?? "pilot";
            foreach (char c in Path.GetInvalidFileNameChars()) pilot = pilot.Replace(c, '_');
            return Path.Combine(Dir, pilot + "-slot" + slot + ".txt");
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

        // ------------------------------------------------------------ host: keep the joiners' current loadouts
        /// Every 2 s in a level (and right before a save): our copies of the joiners' ships. A cache, because the between-level
        /// autosave runs at the end of a frame after the level ended, and a joiner may already be gone by then.
        public static void HostTick(bool force)
        {
            if (!CoopConfig.IsHost || !NetworkServer.active || !GameplayManager.LevelIsLoaded || GameplayManager.IsChallengeMode) return;
            if (!force && Time.realtimeSinceStartup < s_next_cache) return;
            s_next_cache = Time.realtimeSinceStartup + 2f;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.isLocalPlayer || p.connectionToClient == null) continue;
                string id = IdOf(p.connectionToClient);
                if (id == null) continue;
                try { s_latest[id] = CoopLoadout.CaptureOf(p); s_latest_name[id] = OlCoop.Hud.CoopHud.NameOf(p) ?? "?"; } catch { }
            }
        }

        /// SaveLoad.SaveGame(slot) succeeded on the host: write the joiners' loadouts next to it.
        public static void HostSaved(int slot, bool betweenLevels)
        {
            if (!CoopConfig.IsHost) return;
            HostTick(true);
            var lvl = betweenLevels ? GameplayManager.GetNextLevel() : GameplayManager.Level;
            string file = FileFor(slot);
            try
            {
                Directory.CreateDirectory(Dir);
                if (s_latest.Count == 0) { if (File.Exists(file)) File.Delete(file); CoopLog.Write("SAVE", "host: saved slot " + slot + " with no joiners; no co-op loadouts stored"); return; }
                using (var w = new StreamWriter(file, false))
                {
                    w.WriteLine("# olcoop co-op loadouts for " + PilotManager.PilotName + " slot " + slot + " (" + CoopVersion.Full + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
                    w.WriteLine("level=" + (lvl != null ? lvl.FileName : "?"));
                    foreach (var kv in s_latest)
                    {
                        var nw = new NetworkWriter(); kv.Value.Serialize(nw);
                        string name; s_latest_name.TryGetValue(kv.Key, out name);
                        w.WriteLine(kv.Key + "|" + (name ?? "?").Replace("|", "/") + "|" + Convert.ToBase64String(nw.ToArray()));
                    }
                }
                CoopLog.Write("SAVE", "host: saved slot " + slot + (betweenLevels ? " (between levels)" : "") + " for level '" + (lvl != null ? lvl.FileName : "?") + "' with " + s_latest.Count + " joiner loadout(s): " + string.Join(", ", new List<string>(s_latest_name.Values).ToArray()));
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.HostSaved", ex); }
        }

        /// SaveLoad.LoadGame(slot) on the host: read the joiners' loadouts that belong to it.
        public static void HostLoaded(int slot)
        {
            s_pending = null; s_pending_level = null; s_pending_slot = -1; s_restored.Clear();
            if (!CoopConfig.IsHost) return;
            string file = FileFor(slot);
            try
            {
                if (!File.Exists(file)) { CoopLog.Write("SAVE", "host: loaded slot " + slot + "; no co-op loadouts stored with it"); return; }
                var d = new Dictionary<string, LoadoutMsg>();
                string level = null;
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.StartsWith("#") || line.Trim() == "") continue;
                    if (line.StartsWith("level=")) { level = line.Substring(6).Trim(); continue; }
                    var parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    var m = new LoadoutMsg();
                    m.Deserialize(new NetworkReader(Convert.FromBase64String(parts[2])));
                    d[parts[0]] = m;
                }
                s_pending = d; s_pending_level = level; s_pending_slot = slot;
                CoopLog.Write("SAVE", "host: loaded slot " + slot + " (level '" + level + "'); " + d.Count + " co-op loadout(s) waiting for their players");
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.HostLoaded", ex); }
        }

        public static void ClearCache() { s_latest.Clear(); s_latest_name.Clear(); }

        /// A new campaign (not from a save) drops anything pending.
        public static void NewGame() { if (s_pending != null) CoopLog.Write("SAVE", "host: new game; stored co-op loadouts not used"); s_pending = null; s_pending_level = null; s_restored.Clear(); s_latest.Clear(); s_latest_name.Clear(); }

        /// Host, CoopLoadout.OnLoadout: a joiner's ship reported its loadout. The first report in the loaded level gets the saved one back.
        public static void HostOnLoadout(NetworkConnection conn)
        {
            if (s_pending == null || conn == null) return;
            var lvl = GameplayManager.Level;
            if (lvl == null || string.Compare(lvl.FileName, s_pending_level, true) != 0)
            {
                CoopLog.Write("SAVE", "host: now in '" + (lvl != null ? lvl.FileName : "?") + "', not the saved level '" + s_pending_level + "'; stored co-op loadouts dropped");
                s_pending = null; return;
            }
            string id = IdOf(conn);
            LoadoutMsg m;
            if (id == null || s_restored.Contains(id) || !s_pending.TryGetValue(id, out m)) return;
            s_restored.Add(id);
            conn.Send(MsgRestore, m);
            CoopLog.Write("SAVE", "host: gave " + id + " (conn " + conn.connectionId + ") its loadout from slot " + s_pending_slot + ": " + m.Describe());
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
                GameplayManager.AddHUDMessage("CO-OP: YOUR LOADOUT FROM THE SAVED GAME IS BACK", -1, true);
                CoopLog.Write("SAVE", "joiner: loadout from the host's saved game applied: " + m.Describe());
            }
            catch (Exception ex) { CoopLog.Error("CoopSaves.OnRestore", ex); }
        }
    }

    [HarmonyPatch]
    static class SV1_HostSaved
    {
        static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(SaveLoad), "SaveGame", new Type[] { typeof(int), typeof(bool) }); }
        static void Prefix() { try { CoopSaves.HostTick(true); } catch { } }
        static void Postfix(int slot_num, bool between_levels, SaveLoad.SaveLoadResult __result)
        {
            if (!CoopConfig.IsHost || __result != SaveLoad.SaveLoadResult.Success) return;
            try { CoopSaves.HostSaved(slot_num, between_levels); } catch (Exception ex) { CoopLog.Error("SV1", ex); }
        }
    }

    [HarmonyPatch]
    static class SV2_HostLoaded
    {
        static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(SaveLoad), "LoadGame", new Type[] { typeof(int) }); }
        static readonly FieldInfo f_last = AccessTools.Field(typeof(SaveLoad), "last_load_slot");
        static void Postfix(int slot_num, SaveLoad.SaveLoadResult __result)
        {
            if (!CoopConfig.IsHost) return;
            if (__result != SaveLoad.SaveLoadResult.Success && __result != SaveLoad.SaveLoadResult.Between_Levels_Success) return;
            try { CoopSaves.HostLoaded(slot_num == -1 ? (int)f_last.GetValue(null) : slot_num); } catch (Exception ex) { CoopLog.Error("SV2", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "CreateNewGame")]
    static class SV3_NewGame
    {
        static void Postfix(bool saved_game) { if (CoopConfig.IsHost && !saved_game && !GameplayManager.IsChallengeMode) { try { CoopSaves.NewGame(); } catch { } } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class SV4_HostCache
    {
        static void Postfix() { if (CoopConfig.IsHost) { try { CoopSaves.HostTick(false); } catch (Exception ex) { CoopLog.Error("SV4", ex); } } }
    }

    /// A new level: the cache only holds players who are in it (the save at the end of this level reads it).
    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class SV6_LevelCache
    {
        static void Prefix() { if (CoopConfig.IsHost) CoopSaves.ClearCache(); }
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
