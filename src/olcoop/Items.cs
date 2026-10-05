using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    /// <summary>
    /// Level items (pickups, security keys, audio logs) in co-op. Host only.
    ///
    /// Saved games. Continuing a save makes SaveLoad.CompleteGameLoad remove every placed item and re-create the saved ones as
    /// plain local instances (netId 0). olmod's UpdateDynamicManager_AddItem prefix deletes netId-0 items while
    /// IsMultiplayerActive (in real multiplayer the server spawns all items), and co-op runs with IsMultiplayerActive, so every
    /// restored item vanished. The host copies the save's item list before the load clears it and, once the level runs,
    /// re-creates whatever is missing exactly as the loader does, then network-spawns it (netId, so olmod keeps it and joiners get it).
    ///
    /// Late-activated items. Overload keeps many items inactive until a player gets near. UNET only sends a joining client the
    /// objects that are active at that moment and never re-checks, so joiners never saw those items (pickups still worked, they run
    /// on the host). The host adds every ready joiner as an observer as soon as an item is switched on, which sends its spawn.
    /// </summary>
    public static class CoopItems
    {
        struct SavedItem
        {
            public int type, index, amount; public bool secret, super; public Vector3 pos; public Quaternion rot;
        }

        const float MatchRadius = 1.5f;     // a live item of the same type this close counts as the saved one
        const float RestoreDelay = 1.5f;    // seconds after gameplay starts
        const float VisibilityPeriod = 0.5f;

        static readonly System.Reflection.FieldInfo f_game_root = AccessTools.Field(typeof(SaveLoad), "m_game_root");
        static readonly List<SavedItem> s_saved = new List<SavedItem>();
        static bool s_restore_pending;
        static float s_restore_at = -1f, s_next_visibility;

        static bool HostInLevel
        {
            get { return CoopConfig.IsHost && !GameplayManager.IsMultiplayer && NetworkServer.active && GameplayManager.LevelIsLoaded; }
        }

        // ------------------------------------------------------------------ saved games

        /// Called before SaveLoad.CompleteGameLoad, while SaveLoad.m_game_root still holds the save.
        public static void CaptureSavedItems()
        {
            s_saved.Clear(); s_restore_pending = false; s_restore_at = -1f;
            if (!CoopConfig.IsHost || GameplayManager.IsMultiplayer) return;
            var root = f_game_root != null ? f_game_root.GetValue(null) as JObject : null;
            var items = root != null ? root["Items"] as JArray : null;
            if (items == null) { CoopLog.Write("ITEM", "host: save has no item list"); return; }
            foreach (var tok in items)
            {
                var o = tok as JObject;
                var it = o != null ? o["Item"] : null;
                var tr = o != null && o["gameObject"] != null ? o["gameObject"]["transform"] : null;
                if (it == null || tr == null) continue;
                var s = new SavedItem
                {
                    type = JsonExtensions.GetInt(it["type"], -1),
                    index = JsonExtensions.GetInt(it["index"], 0),
                    amount = JsonExtensions.GetInt(it["amount"], 0),
                    secret = JsonExtensions.GetBool(it["secret"], false),
                    super = JsonExtensions.GetBool(it["super"], false),
                    pos = JsonExtensions.GetVector3(tr["localPosition"], null),
                    rot = JsonExtensions.GetQuat(tr["localRotation"], null),
                };
                if (s.type >= 0) s_saved.Add(s);
            }
            CoopLog.Write("ITEM", "host: loading a saved game with " + s_saved.Count + " items");
            s_restore_pending = true;
        }

        static void RestoreTick()
        {
            if (!s_restore_pending || GameplayManager.m_gameplay_state.ToString() != "PLAYING") return;
            if (s_restore_at < 0f) { s_restore_at = Time.unscaledTime + RestoreDelay; return; }
            if (Time.unscaledTime < s_restore_at) return;
            s_restore_pending = false;
            RestoreMissing();
        }

        static void RestoreMissing()
        {
            var live = new List<Item>();
            foreach (var it in UnityEngine.Object.FindObjectsOfType<Item>()) if (it != null && it.gameObject.activeInHierarchy) live.Add(it);
            int present = 0, made = 0, failed = 0;
            var restored = new StringBuilder();
            foreach (var s in s_saved)
            {
                if (live.Exists(it => (int)it.m_type == s.type && (it.transform.position - s.pos).sqrMagnitude < MatchRadius * MatchRadius)) { present++; continue; }
                try
                {
                    var prefab = SaveLoad.GetPrefabFromItemType(s.type);
                    if (prefab == null) { failed++; continue; }
                    var go = UnityEngine.Object.Instantiate(prefab, s.pos, s.rot);
                    var item = go.GetComponent<Item>();
                    if (item != null) { item.m_index = s.index; item.m_secret = s.secret; item.m_super = s.super; if (s.amount > 0) item.m_amount = s.amount; }
                    go.SetActive(true);
                    NetworkSpawnItem.Spawn(go);
                    made++;
                    if (s.type == (int)ItemType.KEY_SECURITY || s.type == (int)ItemType.LOG_ENTRY) restored.Append(' ').Append((ItemType)s.type).Append('@').Append(s.pos.ToString("F0"));
                }
                catch (Exception ex) { failed++; CoopLog.Error("CoopItems.RestoreMissing " + (ItemType)s.type, ex); }
            }
            CoopLog.Write("ITEM", "host: saved items: " + present + " present, " + made + " re-created, " + failed + " failed;" + restored);
        }

        // ------------------------------------------------------------------ late-activated items

        static void VisibilityTick()
        {
            if (Time.unscaledTime < s_next_visibility) return;
            s_next_visibility = Time.unscaledTime + VisibilityPeriod;
            var ready = new List<NetworkConnection>();
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && c.isReady) ready.Add(c);
            if (ready.Count == 0) return;
            foreach (var it in Item.m_ItemList)
            {
                if (it == null || !it.gameObject.activeSelf) continue;
                var id = it.GetComponent<NetworkIdentity>();
                if (id == null || id.netId.Value == 0) continue;
                var observers = id.observers;
                if (observers != null && ready.TrueForAll(c => observers.Contains(c))) continue;
                try
                {
                    id.RebuildObservers(true);
                    if (it.m_type == ItemType.KEY_SECURITY || it.m_type == ItemType.LOG_ENTRY)
                        CoopLog.Write("ITEM", "host: sent " + it.m_type + " netId=" + id.netId.Value + " to joiners (switched on after they joined)");
                }
                catch (Exception ex) { CoopLog.Error("CoopItems.Visibility " + it.m_type, ex); }
            }
        }

        public static void HostTick()
        {
            if (!HostInLevel) return;
            RestoreTick();
            VisibilityTick();
        }
    }

    [HarmonyPatch(typeof(SaveLoad), "CompleteGameLoad")]
    static class IT1_CaptureSavedItems
    {
        static void Prefix()
        {
            CoopConfig.EnsureInit();
            try { CoopItems.CaptureSavedItems(); } catch (Exception ex) { CoopLog.Error("IT1", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class IT2_HostTick
    {
        static void Postfix() { try { CoopItems.HostTick(); } catch (Exception ex) { CoopLog.Error("IT2", ex); } }
    }
}
