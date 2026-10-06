// Map pings (0.6.5).
//  - In the map, the view centre shows a small white sphere (child of MapCamera.m_map_focus, the map's own pulsing focus point).
//  - Setting the map marker (stock key: FIRE FLARE while the map is open) also pings that spot for every player: "<NAME> PINGED",
//    a sound, and a white marker visible through walls for 15 s, in the level and on the map. One ping per player.
//  - Hologuide: while a ping is less than 60 s old, the CRYOTUBE slot of the guide wheel reads PING; choosing it leads to the ping
//    (the stock cryotube lead mode with the ping's segment as goal; reached within 8 u).
// Network: msg 201 PingMsg {pos, name}: joiner -> host -> every other player; the host's own ping -> every joiner.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace OlCoop.World
{
    public class PingMsg : MessageBase
    {
        public Vector3 pos; public string name;
        public override void Serialize(NetworkWriter w) { w.Write(pos); w.Write(name ?? ""); }
        public override void Deserialize(NetworkReader r) { pos = r.ReadVector3(); name = r.ReadString(); }
    }

    public static class CoopPing
    {
        public const short MsgPing = 201;
        public const float Lifetime = 15f, GuideWindow = 60f, ReachDistance = 8f;

        class Marker { public GameObject world, map; public float until; public Vector3 pos; }
        static readonly Dictionary<string, Marker> s_markers = new Dictionary<string, Marker>();
        static Material s_mat;
        static GameObject s_focus_sphere;

        public static Vector3 LastPos; public static float LastTime = -1000f; public static string LastName;
        public static bool GuideActive { get { return Time.time - LastTime < GuideWindow; } }

        static Material Mat()
        {
            if (s_mat != null) return s_mat;
            var sh = Shader.Find("Hidden/Internal-Colored");
            s_mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            s_mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); s_mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            s_mat.SetInt("_Cull", (int)CullMode.Off); s_mat.SetInt("_ZWrite", 0);
            s_mat.SetInt("_ZTest", (int)CompareFunction.Always); // through walls
            s_mat.color = new Color(0f, 0.8f, 0.75f, 0.9f); // teal (0.6.6, user)
            s_mat.renderQueue = 4000;
            return s_mat;
        }

        static GameObject Sphere(string name, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>(); if (col != null) UnityEngine.Object.Destroy(col);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = Mat(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.allowOcclusionWhenDynamic = false; // occlusion culling would skip it behind walls before _ZTest Always matters
            go.layer = layer;
            return go;
        }

        // ---- map focus sphere
        public static void MapTick()
        {
            bool want = CoopConfig.Active && !GameplayManager.IsMultiplayer && GameplayManager.m_gameplay_state == GameplayState.AUTOMAP && MapCamera.m_map_focus != null;
            if (want && s_focus_sphere == null)
            {
                s_focus_sphere = Sphere("olcoop_map_focus", MapCamera.m_map_focus.gameObject.layer);
                s_focus_sphere.transform.SetParent(MapCamera.m_map_focus, false);
                s_focus_sphere.transform.localPosition = Vector3.zero;
                s_focus_sphere.transform.localScale = Vector3.one * 0.3f; // 75% smaller (0.6.6)
            }
            else if (!want && s_focus_sphere != null) { UnityEngine.Object.Destroy(s_focus_sphere); s_focus_sphere = null; }
        }

        // ---- sending
        public static void LocalPing(Vector3 pos)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            string me = OlCoop.Hud.CoopHud.Clean(PilotManager.PilotName);
            var m = new PingMsg { pos = pos, name = me };
            if (CoopConfig.IsHost && NetworkServer.active) SendToJoiners(m, -1);
            else { var c = Client.GetClient(); if (c != null && c.isConnected) c.Send(MsgPing, m); }
            Show(m, true);
        }

        static void SendToJoiners(PingMsg m, int exceptConn)
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.connectionId != exceptConn && c.isConnected && OlCoop.Session.CoopHost.Verified.Contains(c.connectionId))
                    c.Send(MsgPing, m);
        }

        // ---- receiving
        public static void OnHostPing(NetworkMessage msg)   // host: from a joiner
        {
            try { var m = msg.ReadMessage<PingMsg>(); SendToJoiners(m, msg.conn.connectionId); Show(m, false); }
            catch (Exception ex) { CoopLog.Error("OnHostPing", ex); }
        }

        public static void OnJoinerPing(NetworkMessage msg)  // joiner: from the host
        {
            try { Show(msg.ReadMessage<PingMsg>(), false); } catch (Exception ex) { CoopLog.Error("OnJoinerPing", ex); }
        }

        static void Show(PingMsg m, bool mine)
        {
            if (!GameplayManager.LevelIsLoaded) return;
            string who = string.IsNullOrEmpty(m.name) ? "?" : m.name;
            Marker mk;
            if (!s_markers.TryGetValue(who, out mk)) { mk = new Marker(); s_markers[who] = mk; }
            if (mk.world == null) mk.world = Sphere("olcoop_ping_" + who, 0);
            if (mk.map == null && MapCamera.m_map_focus != null) mk.map = Sphere("olcoop_ping_map_" + who, MapCamera.m_map_focus.gameObject.layer);
            mk.pos = m.pos; mk.until = Time.time + Lifetime;
            mk.world.transform.position = m.pos;
            if (mk.map != null) { mk.map.transform.position = m.pos; mk.map.transform.localScale = Vector3.one * 0.625f; }
            LastPos = m.pos; LastTime = Time.time; LastName = who;
            GameplayManager.AddHUDMessage(mine ? "YOU PINGED A SPOT" : who + " PINGED", -1, true);
            try { SFXCueManager.PlayCue2D(SFXCue.hud_weapon_cycle_picker, 0.8f, 0.3f); } catch { }
            CoopLog.Write("PING", (mine ? "sent" : "from " + who) + " at " + m.pos.ToString("F0"));
        }

        /// Per frame: fade/scale markers (constant on-screen size), expire after 15 s; hologuide arrival.
        public static void Tick()
        {
            if (s_markers.Count > 0)
            {
                var cam = Camera.main;
                List<string> dead = null;
                foreach (var kv in s_markers)
                {
                    var mk = kv.Value;
                    if (Time.time > mk.until || !GameplayManager.LevelIsLoaded)
                    {
                        if (mk.world != null) UnityEngine.Object.Destroy(mk.world);
                        if (mk.map != null) UnityEngine.Object.Destroy(mk.map);
                        (dead ?? (dead = new List<string>())).Add(kv.Key);
                        continue;
                    }
                    if (mk.world != null && cam != null)
                    {
                        // Always drawn, from anywhere: beyond the camera's far clip the marker is moved along the same line of sight
                        // to just inside it (same direction on screen, same apparent size).
                        Vector3 cp = cam.transform.position, to = mk.pos - cp;
                        float d = to.magnitude, maxD = cam.farClipPlane * 0.9f;
                        mk.world.transform.position = d > maxD && d > 0.01f ? cp + to / d * maxD : mk.pos;
                        float shown = Mathf.Min(d, maxD);
                        float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
                        mk.world.transform.localScale = Vector3.one * Mathf.Max(0.15f, d * 0.00625f) * (shown / Mathf.Max(d, 0.01f)) * pulse; // 75% smaller (0.6.6)
                    }
                }
                if (dead != null) foreach (var k in dead) s_markers.Remove(k);
            }
            GuideTick();
        }

        public static void ClearAll()
        {
            foreach (var mk in s_markers.Values) { if (mk.world != null) UnityEngine.Object.Destroy(mk.world); if (mk.map != null) UnityEngine.Object.Destroy(mk.map); }
            s_markers.Clear(); LastTime = -1000f; s_guiding = false;
        }

        // ---- hologuide
        static bool s_guiding;
        static string s_cryo_label;

        /// Wheel label: CRYOTUBE slot (3) reads PING while a recent ping exists.
        public static void UpdateWheelLabel()
        {
            var a = PlayerShip.GuidebotCommandStrings;
            if (a == null || a.Length < 4) return;
            if (s_cryo_label == null && a[3] != "PING") s_cryo_label = a[3];
            string want = GuideActive && CoopConfig.Active ? "PING" : (s_cryo_label ?? a[3]);
            if (a[3] != want) a[3] = want;
        }

        public static void OnWheelCommand()
        {
            if (Robot.m_guidebot_command == 3 && GuideActive && CoopConfig.Active) { s_guiding = true; CoopLog.Write("PING", "hologuide: lead to " + LastName + "'s ping"); }
        }

        /// Replaces the cryotube search while leading to a ping. Returns true when a path to the ping exists.
        public static bool FindPingSegment(Robot guide, out bool handled)
        {
            handled = false;
            if (!s_guiding) return false;
            handled = true;
            int seg = GameManager.m_level_data.FindSegmentContainingWorldPosition(LastPos);
            float dist;
            if (seg < 0 || !guide.CreatePathToSegment(seg, 9999f, out dist)) { s_guiding = false; CoopLog.Write("PING", "hologuide: no path to the ping"); return false; }
            Robot.m_guidebot_special_goal_segment = seg;
            guide.HoloGuideHUDMessage("FOLLOW ME TO THE PING");
            return true;
        }

        public static void CancelGuide() { s_guiding = false; }

        static void GuideTick()
        {
            if (!s_guiding) return;
            if (Robot.m_guidebot == null) { s_guiding = false; return; }
            if (Robot.AI_guidebot_submode == AIGuidebotSubmodeType.GO_TO_CRYOTUBE) Robot.GuidebotStatusString = "FINDING PING";
            var ship = GameManager.m_player_ship;
            if (ship != null && Vector3.Distance(ship.c_transform.position, LastPos) < ReachDistance)
            {
                Robot.m_player_reached_cryotube = true;   // stock GO_TO_CRYOTUBE ends the lead
                s_guiding = false;
                CoopLog.Write("PING", "hologuide: reached the ping");
            }
        }
    }

    [HarmonyPatch(typeof(MapCamera), "SetMarker")]
    static class PG1_MarkerPings
    {
        static void Postfix() { try { CoopPing.LocalPing(AutomapMarker.MarkerPosition); } catch (Exception ex) { CoopLog.Error("PG1", ex); } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class PG2_Tick
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { CoopPing.MapTick(); CoopPing.Tick(); CoopPing.UpdateWheelLabel(); } catch (Exception ex) { CoopLog.Error("PG2", ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "IssueGuidebotCommandFromWheel")]
    static class PG3_WheelCommand
    {
        static void Prefix() { try { CoopPing.OnWheelCommand(); } catch (Exception ex) { CoopLog.Error("PG3", ex); } }
        static void Postfix(bool __result) { if (!__result) CoopPing.CancelGuide(); }
    }

    [HarmonyPatch(typeof(Robot), "FindSegmentContainingCryotube")]
    static class PG4_GuideToPing
    {
        static bool Prefix(Robot __instance, ref bool __result)
        {
            try { bool handled; bool ok = CoopPing.FindPingSegment(__instance, out handled); if (handled) { __result = ok; return false; } }
            catch (Exception ex) { CoopLog.Error("PG4", ex); }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
    static class PG5_Reset
    {
        static void Postfix() { CoopPing.ClearAll(); }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class PG6_ServerHandlers
    {
        static void Postfix() { CoopConfig.EnsureInit(); if (CoopConfig.IsHost) NetworkServer.RegisterHandler(CoopPing.MsgPing, CoopPing.OnHostPing); }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class PG7_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (CoopConfig.IsJoiner && Client.GetClient() != null) Client.GetClient().RegisterHandler(CoopPing.MsgPing, CoopPing.OnJoinerPing);
        }
    }
}
