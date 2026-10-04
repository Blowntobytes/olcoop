using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace OlCoop.World
{
    /// <summary>
    /// Security key trace (0.5.6, diagnostics). User report: on sp_outer_01 (Ymir Outpost) the second security key could not be
    /// found in co-op (15:07 run: the first key was picked up at 15:13:29, team security level 1; the second key's pickup script
    /// never fired). Static check of all 17 campaign scenes (tools: UnityPy on the level files, see HANDOFF 0.5.6): every key is a
    /// placed Item (KEY_SECURITY) with a NetworkIdentity, inactive in the file and switched on by the scene spawn like every other
    /// pickup, under &lt;level&gt;/_container_placed_entities/ITEM. Nothing in the level data differs for Ymir. The cause has to be
    /// found at runtime, so this logs, on host and joiners:
    ///  - [KEY] census 3 s after the level starts: every key with netId, active state, position, segment and the nearest doors
    ///    (secret / locked / open);
    ///  - every 10 s: a line only when a key appeared, disappeared, or changed active state;
    ///  - Item.OnDestroy of a key with the call stack; key trigger touches (who, throttled); Player.AddKey (who, new level);
    ///  - DoorAnimating.OpenDoor of secret doors (first time per door) - both Ymir keys sit behind secret walls.
    /// </summary>
    public static class KeyTrace
    {
        const int KEY = 25; // ItemType.KEY_SECURITY
        static string s_level;
        static float s_census_at = -1f, s_next_poll;
        static bool s_census_done;
        static readonly Dictionary<int, string> s_state = new Dictionary<int, string>();
        static readonly HashSet<int> s_secret_logged = new HashSet<int>();
        static readonly Dictionary<int, float> s_touch_logged = new Dictionary<int, float>();

        public static bool IsKey(Item it) { return it != null && (int)it.m_type == KEY; }

        static bool InScene(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }

        static List<Item> Keys()
        {
            var l = new List<Item>();
            foreach (var it in Resources.FindObjectsOfTypeAll<Item>()) if (InScene(it) && IsKey(it)) l.Add(it);
            return l;
        }

        static string P(Vector3 v) { return "(" + v.x.ToString("F0") + "," + v.y.ToString("F0") + "," + v.z.ToString("F0") + ")"; }

        static string NetId(Item it)
        {
            try { return it.netId.Value.ToString(); } catch { return "?"; }
        }

        static string State(Item it)
        {
            return "netId=" + NetId(it) + " activeSelf=" + it.gameObject.activeSelf + " inHierarchy=" + it.gameObject.activeInHierarchy +
                   " pos=" + P(it.transform.position);
        }

        static string Doors(Vector3 pos)
        {
            var near = new List<KeyValuePair<float, DoorAnimating>>();
            foreach (var d in UnityEngine.Object.FindObjectsOfType<DoorAnimating>())
            {
                float dist = Vector3.Distance(d.transform.position, pos);
                if (dist < 45f) near.Add(new KeyValuePair<float, DoorAnimating>(dist, d));
            }
            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            var sb = new StringBuilder();
            for (int i = 0; i < near.Count && i < 4; i++)
            {
                var d = near[i].Value;
                bool secret = false, open = false; string lk = "?";
                try { secret = Traverse.Create(d).Field("m_is_secret").GetValue<bool>(); } catch { }
                try { open = d.IsOpen(); } catch { }
                try { var f = Traverse.Create(d).Field("m_lock_level"); if (f.FieldExists()) lk = f.GetValue().ToString(); } catch { }
                sb.Append(" [").Append(d.gameObject.name).Append(" d=").Append(near[i].Key.ToString("F0")).Append(secret ? " SECRET" : "")
                  .Append(open ? " open" : " closed").Append(lk != "?" ? " lock=" + lk : "").Append(']');
            }
            return near.Count == 0 ? " (no door within 45u)" : sb.ToString();
        }

        public static void Tick()
        {
            if (!CoopConfig.Active || !GameplayManager.LevelIsLoaded || GameplayManager.m_level_info == null) return;
            string lv = GameplayManager.m_level_info.FileName;
            if (lv != s_level)
            {
                s_level = lv; s_census_done = false; s_census_at = Time.unscaledTime + 3f; s_state.Clear(); s_secret_logged.Clear(); s_touch_logged.Clear();
            }
            if (!s_census_done && Time.unscaledTime >= s_census_at)
            {
                s_census_done = true; s_next_poll = Time.unscaledTime + 10f;
                var keys = Keys();
                CoopLog.Write("KEY", (CoopConfig.IsHost ? "host" : "joiner") + " " + lv + ": " + keys.Count + " security key(s); team level=" +
                    (GameManager.m_local_player != null ? ((int)GameManager.m_local_player.m_unlock_level).ToString() : "?"));
                foreach (var k in keys)
                {
                    s_state[k.GetInstanceID()] = State(k);
                    CoopLog.Write("KEY", "  key " + k.GetInstanceID() + " '" + k.gameObject.name + "' " + State(k) + " seg=" + k.m_current_segment + " doors:" + Doors(k.transform.position));
                }
                return;
            }
            if (s_census_done && Time.unscaledTime >= s_next_poll)
            {
                s_next_poll = Time.unscaledTime + 10f;
                var seen = new HashSet<int>();
                foreach (var k in Keys())
                {
                    int id = k.GetInstanceID(); seen.Add(id);
                    string st = State(k), old;
                    if (!s_state.TryGetValue(id, out old)) CoopLog.Write("KEY", "key " + id + " APPEARED: " + st);
                    else if (old.Split(' ')[1] != st.Split(' ')[1] || old.Split(' ')[2] != st.Split(' ')[2]) CoopLog.Write("KEY", "key " + id + " changed: " + st);
                    s_state[id] = st;
                }
                foreach (var id in new List<int>(s_state.Keys))
                    if (!seen.Contains(id)) { CoopLog.Write("KEY", "key " + id + " GONE (last: " + s_state[id] + ")"); s_state.Remove(id); }
            }
        }

        public static void OnKeyDestroyed(Item it)
        {
            string stack = Environment.StackTrace;
            var lines = stack.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0, n = 0; i < lines.Length && n < 10; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.Contains("KeyTrace") || l.Contains("System.Environment")) continue;
                sb.Append(" | ").Append(l); n++;
            }
            CoopLog.Write("KEY", "key " + it.GetInstanceID() + " DESTROYED " + State(it) + " levelLoaded=" + GameplayManager.LevelIsLoaded + " stack:" + sb);
        }

        public static void OnKeyTouched(Item it, Collider other)
        {
            int id = it.GetInstanceID(); float last;
            if (s_touch_logged.TryGetValue(id, out last) && Time.unscaledTime - last < 2f) return;
            s_touch_logged[id] = Time.unscaledTime;
            string who = "?";
            try
            {
                var rb = other != null ? other.attachedRigidbody : null;
                var ps = rb != null ? rb.GetComponent<PlayerShip>() : null;
                who = ps != null ? ("ship netId=" + ps.netId.Value + (ps.isLocalPlayer ? " (local)" : "")) : (other != null ? other.gameObject.name : "null");
            }
            catch { }
            CoopLog.Write("KEY", "key " + id + " touched by " + who + " server=" + Overload.NetworkManager.IsServer() + " " + State(it));
        }

        public static void OnAddKey(Player p)
        {
            string who = "?";
            try { who = "netId=" + p.netId.Value + (p.isLocalPlayer ? " (local)" : ""); } catch { }
            CoopLog.Write("KEY", "AddKey " + who + " -> level " + (int)p.m_unlock_level);
        }

        public static void OnOpenDoor(DoorAnimating d)
        {
            try
            {
                if (!Traverse.Create(d).Field("m_is_secret").GetValue<bool>()) return;
                if (!s_secret_logged.Add(d.GetInstanceID())) return;
                CoopLog.Write("KEY", "secret door '" + d.gameObject.name + "' opened at " + P(d.transform.position));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class KT1_Tick
    {
        static void Postfix() { try { KeyTrace.Tick(); } catch (Exception ex) { CoopLog.Error("KT1", ex); } }
    }

    [HarmonyPatch(typeof(Item), "OnDestroy")]
    static class KT2_KeyDestroyed
    {
        static void Prefix(Item __instance)
        {
            if (!CoopConfig.Active || !KeyTrace.IsKey(__instance)) return;
            try { KeyTrace.OnKeyDestroyed(__instance); } catch (Exception ex) { CoopLog.Error("KT2", ex); }
        }
    }

    [HarmonyPatch(typeof(Item), "OnTriggerEnter")]
    [HarmonyPriority(Priority.First)]
    static class KT3_KeyTouched
    {
        static void Prefix(Item __instance, Collider other)
        {
            if (!CoopConfig.Active || !KeyTrace.IsKey(__instance)) return;
            try { KeyTrace.OnKeyTouched(__instance, other); } catch (Exception ex) { CoopLog.Error("KT3", ex); }
        }
    }

    [HarmonyPatch(typeof(Player), "AddKey")]
    static class KT4_AddKey
    {
        static void Postfix(Player __instance)
        {
            if (!CoopConfig.Active) return;
            try { KeyTrace.OnAddKey(__instance); } catch (Exception ex) { CoopLog.Error("KT4", ex); }
        }
    }

    [HarmonyPatch(typeof(DoorAnimating), "OpenDoor")]
    static class KT5_SecretDoor
    {
        static void Postfix(DoorAnimating __instance)
        {
            if (!CoopConfig.Active) return;
            KeyTrace.OnOpenDoor(__instance);
        }
    }
}
