using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;

namespace OlCoop.World
{
    /// <summary>
    /// SR (0.5.8): the host makes sure every item a saved game lists exists after the save is loaded.
    /// 0.5.7 runs 17:25/17:26 (both 0.5.7, host continued a save of sp_outer_02 / Tarvos Outpost): security keys and an audio log
    /// missing for everyone; SV1 never fired, so the restored items were not removed by olmod's netId-0 rule. Log facts: the
    /// restored keys (negative instance ids, at the key positions) are destroyed while inactive in the same frame as the load, and the
    /// host's [KEY] census 3 s later finds 0 keys. The exact remover is still being traced (SR3 below logs the Destroy call stack).
    /// Fix that does not depend on the remover:
    ///  SR1 SaveLoad.CompleteGameLoad prefix (host, co-op): copy the save's "Items" list (type, position, rotation, index, secret,
    ///      amount, super) before the game clears SaveLoad.m_game_root at the end of the load.
    ///  SR2 1.5 s after the level runs: for every saved item with no live item of the same type within 1.5 u, create it the way the
    ///      loader does (SaveLoad.GetPrefabFromItemType + Instantiate, saved transform and fields) and network-spawn it with the
    ///      game's NetworkSpawnItem.Spawn (netId, so olmod keeps it and joiners receive it - the same path as robot drops).
    ///  SR3 UnityEngine.Object.Destroy(Object) prefix, for 10 s from the load: logs the call stack when a key or audio-log item is
    ///      destroyed (Destroy is synchronous here, unlike Item.OnDestroy at the end of the frame).
    /// </summary>
    public static class SaveItems
    {
        public struct Saved
        {
            public int type, index, amount; public bool secret, super; public Vector3 pos; public Quaternion rot;
        }

        static readonly List<Saved> s_saved = new List<Saved>();
        static bool s_pending;
        static float s_check_at = -1f;
        public static float TraceUntil = -1f;

        static readonly System.Reflection.FieldInfo s_root_field = AccessTools.Field(typeof(SaveLoad), "m_game_root");

        public static void Capture()
        {
            s_saved.Clear(); s_pending = false;
            if (!CoopConfig.IsHost || GameplayManager.IsMultiplayer) return;
            JObject root = s_root_field != null ? s_root_field.GetValue(null) as JObject : null;
            JArray items = root != null ? root["Items"] as JArray : null;
            if (items == null) { CoopLog.Write("ITEM", "host: save has no Items list"); return; }
            var counts = new Dictionary<int, int>();
            foreach (var tok in items)
            {
                var o = tok as JObject;
                if (o == null) continue;
                var it = o["Item"];
                var tr = o["gameObject"] != null ? o["gameObject"]["transform"] : null;
                if (it == null || tr == null) continue;
                var s = new Saved
                {
                    type = JsonExtensions.GetInt(it["type"], -1),
                    index = JsonExtensions.GetInt(it["index"], 0),
                    amount = JsonExtensions.GetInt(it["amount"], 0),
                    secret = JsonExtensions.GetBool(it["secret"], false),
                    super = JsonExtensions.GetBool(it["super"], false),
                    pos = JsonExtensions.GetVector3(tr["localPosition"], null),
                    rot = JsonExtensions.GetQuat(tr["localRotation"], null),
                };
                if (s.type < 0) continue;
                s_saved.Add(s);
                int c; counts.TryGetValue(s.type, out c); counts[s.type] = c + 1;
            }
            var sb = new StringBuilder();
            foreach (var kv in counts) sb.Append(' ').Append((ItemType)kv.Key).Append('=').Append(kv.Value);
            CoopLog.Write("ITEM", "host: loading a saved game; save lists " + s_saved.Count + " items:" + sb);
            s_pending = true; s_check_at = -1f; TraceUntil = Time.unscaledTime + 10f;
        }

        public static void Tick()
        {
            if (!s_pending || !CoopConfig.IsHost || !GameplayManager.LevelIsLoaded || !UnityEngine.Networking.NetworkServer.active) return;
            if (GameplayManager.m_gameplay_state.ToString() != "PLAYING") return;
            if (s_check_at < 0f) { s_check_at = Time.unscaledTime + 1.5f; return; }
            if (Time.unscaledTime < s_check_at) return;
            s_pending = false;
            try { Restore(); } catch (Exception ex) { CoopLog.Error("SaveItems.Restore", ex); }
        }

        static void Restore()
        {
            var live = new List<Item>();
            foreach (var it in UnityEngine.Object.FindObjectsOfType<Item>()) if (it != null && it.gameObject.activeInHierarchy) live.Add(it);
            int ok = 0, made = 0, failed = 0;
            foreach (var s in s_saved)
            {
                bool found = false;
                foreach (var it in live)
                    if ((int)it.m_type == s.type && (it.transform.position - s.pos).sqrMagnitude < 1.5f * 1.5f) { found = true; break; }
                if (found) { ok++; continue; }
                try
                {
                    var prefab = SaveLoad.GetPrefabFromItemType(s.type);
                    if (prefab == null) { failed++; CoopLog.Write("ITEM", "host: no prefab for saved " + (ItemType)s.type); continue; }
                    var go = UnityEngine.Object.Instantiate(prefab, s.pos, s.rot);
                    var item = go.GetComponent<Item>();
                    if (item != null) { item.m_index = s.index; item.m_secret = s.secret; item.m_super = s.super; if (s.amount > 0) item.m_amount = s.amount; }
                    go.SetActive(true);
                    NetworkSpawnItem.Spawn(go);
                    made++;
                    CoopLog.Write("ITEM", "host: restored missing saved " + (ItemType)s.type + " at " + s.pos.ToString("F0") +
                        " index=" + s.index + " netId=" + (item != null ? item.netId.Value.ToString() : "?"));
                }
                catch (Exception ex) { failed++; CoopLog.Error("SaveItems.Restore " + (ItemType)s.type, ex); }
            }
            CoopLog.Write("ITEM", "host: saved-item check: " + ok + " present, " + made + " re-created and network-spawned, " + failed + " failed");
        }

        public static void TraceDestroy(UnityEngine.Object obj)
        {
            var go = obj as GameObject;
            Item it = go != null ? go.GetComponent<Item>() : obj as Item;
            if (it == null) return;
            int t = (int)it.m_type;
            if (t != 25 && t != 27) return; // KEY_SECURITY, LOG_ENTRY
            var lines = Environment.StackTrace.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0, n = 0; i < lines.Length && n < 12; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.Contains("System.Environment") || l.Contains("SaveItems.TraceDestroy") || l.Contains("SR3_")) continue;
                sb.Append(" | ").Append(l); n++;
            }
            CoopLog.Write("ITEM", "Destroy(" + (ItemType)t + " id=" + it.GetInstanceID() + " netId=" + it.netId.Value + " active=" + it.gameObject.activeSelf +
                " pos=" + it.transform.position.ToString("F0") + ") by:" + sb);
        }
    }

    [HarmonyPatch(typeof(SaveLoad), "CompleteGameLoad")]
    static class SR1_CaptureSave
    {
        static void Prefix()
        {
            CoopConfig.EnsureInit();
            try { SaveItems.Capture(); } catch (Exception ex) { CoopLog.Error("SR1", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class SR2_RestoreTick
    {
        static void Postfix() { try { SaveItems.Tick(); } catch (Exception ex) { CoopLog.Error("SR2", ex); } }
    }

    [HarmonyPatch]
    static class SR3_TraceDestroy
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UnityEngine.Object), "Destroy", new Type[] { typeof(UnityEngine.Object) });
        }

        static void Prefix(UnityEngine.Object obj)
        {
            if (SaveItems.TraceUntil < 0f || obj == null) return;
            if (Time.unscaledTime > SaveItems.TraceUntil) { SaveItems.TraceUntil = -1f; return; }
            try { SaveItems.TraceDestroy(obj); } catch { }
        }
    }
}
