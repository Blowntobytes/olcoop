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

    /// <summary>
    /// Pickups, sound and resources.
    ///
    /// Host's own pickups were silent: in multiplayer Item.OnTriggerEnter runs on the server and announces a pickup with
    /// Player.CallRpcPlayItemPickupFX, whose handler skips the local player (in real MP the picker already played the effect through
    /// its client-side TryFakePickup). The host is the server, so it never fakes a pickup and got no sound, shake or flash. IT3 plays
    /// the item's own PlayItemPickupFX for the host's ship, exactly like single player.
    ///
    /// Joiners got no energy or ammo from pickups: they run olmod's sniper packets (ammo/energy owned by the client, server
    /// RpcSetAmmo/RpcSetEnergy ignored). olmod's server then reports pickups with msg 135 (PlayerAddResourceMessage), but only to
    /// connections whose capabilities include "sniper". Clients send those capabilities (msg 119) in NetworkMatch.OnAcceptedToLobby,
    /// the stock MP lobby, which co-op never goes through, so the host had none for its joiners and nothing was delivered.
    /// IT4 answers MPTweaks.ClientHasTweak(conn, "sniper") = true for verified joiners on a co-op host (the tweak table is also cleared
    /// by MPTweaks.InitMatch, so the lookup is patched rather than the table). Missile ammo keeps our own msg 174 (Combat D2, tested in
    /// 0.4.14); IT5 skips olmod's missile 135 in co-op so missiles aren't added twice.
    /// </summary>
    public static class CoopPickups
    {
        internal static Item Current;   // the item whose OnTriggerEnter is running (host)
        internal static readonly System.Reflection.MethodInfo m_fx = AccessTools.Method(typeof(Item), "PlayItemPickupFX");
        static int s_logged;

        internal static void HostLocalFx(Player p)
        {
            if (!CoopConfig.IsHost || p == null || !p.isLocalPlayer || !NetworkServer.active) return;
            var it = Current;
            if (it == null || m_fx == null) return;
            m_fx.Invoke(it, new object[] { p });
            if (s_logged < 5) { s_logged++; CoopLog.Write("ITEM", "host: pickup sound for my " + it.m_type + (s_logged == 5 ? " (further ones not logged)" : "")); }
        }

        // ------------------------------------------------------------ upgrade points
        // Upgrade items (UPGRADE_L1/L2) are one per level, shared like every pickup, so the point is given to every player (user,
        // 19:34-20:53 run: points collected by the host never reached the joiner). Player.AddUpgradePoint runs on the server and also
        // shows the HUD message + sound there, so a joiner's own pickup used to announce itself on the host's screen and stay silent on
        // the joiner's. For the host's copies of joiners IT6 only raises the SyncVar (which syncs to that joiner) and sends msg 195;
        // the joiner shows the message itself.
        static bool s_sharing;

        internal static bool HostAddUpgradePoint(Player p, bool super, bool delayed)
        {
            if (!CoopConfig.IsHost || !NetworkServer.active || GameplayManager.IsMultiplayer) return true;
            var it = Current;
            if (!s_sharing && it != null && (it.m_type == ItemType.UPGRADE_L1 || it.m_type == ItemType.UPGRADE_L2))
            {
                s_sharing = true;
                try
                {
                    foreach (var other in Overload.NetworkManager.m_Players)
                        if (other != null && other != p) other.AddUpgradePoint(super, delayed);
                }
                finally { s_sharing = false; }
                CoopLog.Write("ITEM", "host: " + it.m_type + " picked up by netId=" + p.netId.Value + "; point given to every player");
            }
            if (p.isLocalPlayer) return true; // host's own ship: stock code (SyncVar, HUD message, sound)
            if (super) p.Networkm_upgrade_points2 = p.m_upgrade_points2 + 1; else p.Networkm_upgrade_points1 = p.m_upgrade_points1 + 1;
            int total = super ? p.m_upgrade_points2 : p.m_upgrade_points1;
            if (p.connectionToClient != null)
                p.connectionToClient.Send(OlCoop.Combat.LNet.UpgradePoint, new UpgradePointMsg { super = super, total = total, shared = s_sharing });
            CoopLog.Write("ITEM", "host: netId=" + p.netId.Value + " +1 " + (super ? "super " : "") + "upgrade point (" + total + " total" + (s_sharing ? ", shared" : "") + ")");
            return false;
        }

        public static void OnUpgradePoint(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<UpgradePointMsg>();
                string who = m.shared ? " (TEAMMATE)" : "";
                if (m.super)
                {
                    GameplayManager.AddHUDMessage(string.Format(Loc.LS("SUPER UPGRADE POINT ACQUIRED! ({0} TOTAL)"), m.total) + who, -1, true);
                    SFXCueManager.PlayRawSoundEffect2D(SoundEffect.ui_upgrade, 0.8f, 0.1f, 0.1f);
                    SFXCueManager.PlayRawSoundEffect2D(SoundEffect.ui_upgrade, 0.8f, 0.1f, 0.1f);
                }
                else
                {
                    GameplayManager.AddHUDMessage(string.Format(Loc.LS("UPGRADE POINT ACQUIRED! ({0} TOTAL)"), m.total) + who, -1, true);
                    SFXCueManager.PlayRawSoundEffect2D(SoundEffect.ui_upgrade, 0.8f, -0.05f, 0.1f);
                }
                CoopLog.Write("ITEM", "joiner: +1 " + (m.super ? "super " : "") + "upgrade point (" + m.total + " total" + (m.shared ? ", teammate's pickup" : "") + ")");
            }
            catch (Exception ex) { CoopLog.Error("OnUpgradePoint", ex); }
        }

        internal static bool IsCoopJoinerConn(int connId)
        {
            return CoopConfig.IsHost && connId != 0 && OlCoop.Session.CoopHost.Verified.Contains(connId);
        }
    }

    /// IT7: the item list the hologuide searches (RobotManager.m_master_item_list: security keys for "next objective", powerups for
    /// "find energy/armor") is built once at level start from the active items; afterwards Item.Start only adds spewed items. On a
    /// joiner every level item arrives later by network spawn (and keys/audio logs only when the host's game switches them on), and
    /// on the host the items re-created from a save are new instances, so the guide could not find them. Add any item that starts
    /// in a co-op level and isn't listed.
    [HarmonyPatch(typeof(Item), "Start")]
    static class IT7_GuideItemList
    {
        static int s_added;
        static void Postfix(Item __instance)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || __instance == null) return;
            try
            {
                var list = RobotManager.m_master_item_list;
                if (list == null || list.Contains(__instance)) return;
                RobotManager.AddItemToList(__instance);
                if (__instance.m_type == ItemType.KEY_SECURITY || s_added < 3)
                {
                    s_added++;
                    CoopLog.Write("ITEM", (CoopConfig.IsHost ? "host" : "joiner") + ": " + __instance.m_type + " added to the hologuide's item list (" + list.Count + " items)");
                }
            }
            catch (Exception ex) { CoopLog.Error("IT7", ex); }
        }
    }

    /// IT8: keep the list clean. Only the server's pickup removes an item from it (RobotManager.RemoveItemFromList); items a joiner
    /// loses through a network destroy stayed listed, and Pathfinding.FindPowerupNearPlayer has no null check.
    [HarmonyPatch(typeof(Item), "OnDestroy")]
    static class IT8_GuideItemListRemove
    {
        static void Prefix(Item __instance)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { var list = RobotManager.m_master_item_list; if (list != null) list.Remove(__instance); }
            catch (Exception ex) { CoopLog.Error("IT8", ex); }
        }
    }

    /// IT9: armor/energy cap. Player.Awake sets the static Player.MAX_HITPOINTS = MAX_ENERGY to 200 (single player) or 120
    /// (IsMultiplayerActive). The host's Player is created in the main menu (200) and a joiner's Player.Awake on the host flips the
    /// static to 120 mid-level, so the host carried 200 into co-op levels from its saves while refills stopped at 120 (07:03 run:
    /// started Tarvos at 200, later refilled to exactly 120 twice). User decision (2026-10-05): multiplayer rules for everyone -
    /// 120 cap, set at every co-op level start and Player.Awake, and anything above it clamped.
    public static class CoopCaps
    {
        public const float Max = 120f;
        static int s_logged;

        public static void Apply(string why)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            Player.MAX_HITPOINTS = Max; Player.MAX_ENERGY = Max;
            foreach (var p in Overload.NetworkManager.m_Players) Clamp(p, why);
            Clamp(GameManager.m_local_player, why);
        }

        public static void Clamp(Player p, string why)
        {
            if (p == null) return;
            float hp = p.m_hitpoints, en = p.m_energy;
            if (NetworkServer.active && hp > Max) p.m_hitpoints = Max;            // armor: server-owned
            if ((p.isLocalPlayer || NetworkServer.active) && en > Max) p.m_energy = Max; // energy: owned by the player (olmod sniper)
            if ((hp > Max || en > Max) && s_logged++ < 10)
                CoopLog.Write("ITEM", "cap " + Max + " (" + why + "): netId=" + p.netId.Value + " armor " + hp.ToString("F0") + " energy " + en.ToString("F0") + " clamped");
        }
    }

    [HarmonyPatch(typeof(Player), "Awake")]
    static class IT9_CapOnAwake
    {
        static void Postfix() { try { CoopCaps.Apply("player created"); } catch (Exception ex) { CoopLog.Error("IT9", ex); } }
    }

    [HarmonyPatch(typeof(SaveLoad), "CompleteGameLoad")]
    static class IT9c_CapAfterSave
    {
        static void Postfix() { try { CoopCaps.Apply("saved game loaded"); } catch (Exception ex) { CoopLog.Error("IT9c", ex); } }
    }

    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class IT9b_CapOnShipStart
    {
        static void Postfix() { try { CoopCaps.Apply("level start"); } catch (Exception ex) { CoopLog.Error("IT9b", ex); } }
    }

    public class UpgradePointMsg : MessageBase
    {
        public bool super, shared; public int total;
        public override void Serialize(NetworkWriter w) { w.Write(super); w.Write(shared); w.Write(total); }
        public override void Deserialize(NetworkReader r) { super = r.ReadBoolean(); shared = r.ReadBoolean(); total = r.ReadInt32(); }
    }

    [HarmonyPatch(typeof(Player), "AddUpgradePoint")]
    static class IT6_UpgradePoints
    {
        static bool Prefix(Player __instance, bool super, bool delayed)
        {
            try { return CoopPickups.HostAddUpgradePoint(__instance, super, delayed); }
            catch (Exception ex) { CoopLog.Error("IT6", ex); return true; }
        }
    }

    [HarmonyPatch(typeof(Item), "OnTriggerEnter")]
    static class IT3a_TrackItem
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Item __instance) { CoopPickups.Current = __instance; }
        static void Finalizer() { CoopPickups.Current = null; }
    }

    [HarmonyPatch(typeof(Player), "CallRpcPlayItemPickupFX")]
    static class IT3_HostPickupFx
    {
        static void Postfix(Player __instance)
        {
            try { CoopPickups.HostLocalFx(__instance); } catch (Exception ex) { CoopLog.Error("IT3", ex); }
        }
    }

    [HarmonyPatch]
    static class IT4_JoinersAreSniperClients
    {
        public const string OlmodTarget = "GameMod.MPTweaks:ClientHasTweak";
        static bool Prepare() { return AccessTools.TypeByName("GameMod.MPTweaks") != null; }
        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("GameMod.MPTweaks"), "ClientHasTweak"); }
        static bool Prefix(int connectionId, string tweak, ref bool __result)
        {
            if (tweak != "sniper" || !CoopPickups.IsCoopJoinerConn(connectionId)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    static class IT5_NoOlmodMissileGrant
    {
        public const string OlmodTarget = "GameMod.MPSniperPacketsAddMissileAmmo:Prefix";
        static bool Prepare() { return AccessTools.TypeByName("GameMod.MPSniperPacketsAddMissileAmmo") != null; }
        static System.Reflection.MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("GameMod.MPSniperPacketsAddMissileAmmo"), "Prefix"); }
        static bool Prefix() { return !CoopConfig.Active; }
    }
}
