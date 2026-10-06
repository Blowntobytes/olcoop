using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.Hud
{
    /// <summary>
    /// HUD additions:
    ///  - the multiplayer-style yellow respawn countdown (same digits, colour and size as a PVP match) during a co-op RESPAWN cooldown
    ///  - pilot names above teammates (stock MP name tag, which olmod's team-health bar sits under), with a local SHOW PLAYER NAMES option
    /// The stock co-op HUD pass is UIManager.DrawMultiplayerNames, which already runs every frame in co-op (IsMultiplayerActive).
    /// </summary>
    public static class CoopHud
    {
        // ---------------------------------------------------------------- respawn countdown
        static float s_respawn_at = -1f;
        static bool s_logged_draw;

        public static void SetRespawnAt(float t) { s_respawn_at = t; s_logged_draw = false; }
        public static void ClearRespawn() { s_respawn_at = -1f; try { EnsureOverlay(); } catch { } }
        public static bool TimerWanted
        {
            get
            {
                if (s_respawn_at < 0f || !OlCoop.Death.CoopDeath.Active || OlCoop.Death.CoopSettings.Mode != OlCoop.Death.DeathMode.Respawn) return false;
                var ship = SpectateHud.RealShip ?? GameManager.m_player_ship;
                return ship != null && ((bool)ship.m_dead || (bool)ship.m_dying) && s_respawn_at - Time.time >= 0f;
            }
        }

        /// Like a PVP death: the stock game draws the respawn timer from an overlay element (UIManager.CreateOverlayElement(zero, 1,
        /// MP_DEATH_OVERLAY, -1) in PlayerShip.RpcApplyDamageWhenDying), which the overlay camera renders above the death fade and the HUD.
        /// We use the same overlay slot with our own element type so none of the PVP scoreboard/loadout UI appears.
        public static readonly UIElementType uiRespawnOverlay = (UIElementType)121;
        const int OverlaySlot = 1;
        static bool s_overlay_on;

        public static void EnsureOverlay()
        {
            bool want = TimerWanted;
            if (want && !s_overlay_on)
            {
                UIManager.CreateOverlayElement(Vector2.zero, OverlaySlot, uiRespawnOverlay, -1f);
                s_overlay_on = true;
                CoopLog.Write("HUD", "respawn timer overlay created (slot " + OverlaySlot + ")");
            }
            else if (!want && s_overlay_on)
            {
                UIManager.ClearOverlayElement(OverlaySlot);
                s_overlay_on = false;
                CoopLog.Write("HUD", "respawn timer overlay cleared");
            }
        }

        /// Called from our overlay element's Draw (same layer and call as the stock MP death overlay's timer).
        public static void DrawRespawnTimer(UIElement uie)
        {
            if (!TimerWanted) return;
            float left = s_respawn_at - Time.time;
            int whole = (int)left;
            float frac = left - whole;
            Color c = Color.Lerp(UIManager.m_col_hi0, UIManager.m_col_hi7, frac * 1.1f);
            uie.DrawDigitsVariable(Vector2.zero, whole + 1, 1.5f + frac * 0.1f, StringOffset.CENTER, c, frac);
            if (!s_logged_draw) { s_logged_draw = true; CoopLog.Write("HUD", "drawing respawn timer " + (whole + 1) + " (overlay alpha " + uie.m_alpha.ToString("F2") + ")"); }
        }

        /// Health bar under the name tag, same look as olmod's team-health bar. olmod only draws it in team matches; co-op isn't one.
        public static void DrawHealthBar(Player player, Vector2 offset)
        {
            if (player == null || player.c_player_ship == null) return;
            var me = GameManager.m_local_player;
            if (me != null && player.m_mp_team == me.m_mp_team && (int)player.m_mp_team != 2) return; // olmod draws its own then
            float w = 3.5f, h = 0.5f, fade = player.m_mp_data.vis_fade;
            if (fade <= 0f) return;
            offset.y -= 3f;
            // Same values as olmod's MPObserver team-health bar.
            Color fg = Color.Lerp(HSBColor.ConvertToColor(0.4f, 0.85f, 0.1f), HSBColor.ConvertToColor(0.4f, 0.8f, 0.15f), UnityEngine.Random.value * UIElement.FLICKER);
            UIManager.DrawQuadBarHorizontal(offset, w + 0.25f, h + 0.25f, 0f, HSBColor.ConvertToColor(0.4f, 0.1f, 0.1f), 7);
            float fill = Mathf.Min((float)player.m_hitpoints, 100f) / 100f * w;
            offset.x = w - fill;
            UIManager.DrawQuadUIInner(offset, fill, h, fg, 1f, 11, 1f);
        }

        // ---------------------------------------------------------------- player names
        public const short MsgName = 176; // J->H: pilot name

        /// Host: pilot name per connection id (0 = the host itself).
        static readonly Dictionary<int, string> s_names = new Dictionary<int, string>();
        static float s_next_apply;

        public static string Clean(string n)
        {
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Trim().ToUpperInvariant();
            return n.Length > 16 ? n.Substring(0, 16) : n;
        }

        public static void OnName(NetworkMessage msg)
        {
            try
            {
                string n = Clean(msg.ReadMessage<StringMessage>().value);
                s_names[msg.conn.connectionId] = n;
                CoopLog.Write("NAME", "conn " + msg.conn.connectionId + " is '" + n + "'");
                s_next_apply = 0f;
            }
            catch (Exception ex) { CoopLog.Error("CoopHud.OnName", ex); }
        }

        public static void Forget(int connId) { s_names.Remove(connId); }
        public static string NameOf(int connId) { string n; return s_names.TryGetValue(connId, out n) ? n : null; }

        public static void SendMyName()
        {
            var c = Client.GetClient();
            if (c == null) return;
            string n = Clean(PilotManager.PilotName);
            c.Send(MsgName, new StringMessage(n));
            CoopLog.Write("NAME", "sent our pilot name '" + n + "'");
        }

        /// Host: m_mp_name is a SyncVar, so setting it here shows the name on every peer.
        public static void HostApplyNames()
        {
            if (!CoopConfig.IsHost || !NetworkServer.active || Time.time < s_next_apply) return;
            s_next_apply = Time.time + 1f;
            s_names[0] = Clean(PilotManager.PilotName);
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null) continue;
                int id = p.connectionToClient != null ? p.connectionToClient.connectionId : 0;
                string n;
                if (!s_names.TryGetValue(id, out n) || string.IsNullOrEmpty(n)) n = "PLAYER " + p.netId.Value;
                if (p.m_mp_name != n)
                {
                    p.Networkm_mp_name = n;
                    CoopLog.Write("NAME", "netId=" + p.netId.Value + " conn " + id + " named '" + n + "'");
                }
            }
        }

        public static string NameOf(Player p)
        {
            if (p == null) return "?";
            return string.IsNullOrEmpty(p.m_mp_name) ? "PLAYER " + p.netId.Value : p.m_mp_name;
        }
    }

    [HarmonyPatch(typeof(UIManager), "DrawMultiplayerNames")]
    static class HUD1_DrawMultiplayerNames
    {
        /// While our own ship is dead the stock name pass places tags relative to the dead ship, not the spectate camera; skip it.
        static bool Prefix(out int __state)
        {
            __state = -1;
            if (!OlCoop.Death.CoopDeath.Active) return true;
            var s = GameManager.m_player_ship;
            if (s != null && ((bool)s.m_dead || (bool)s.m_dying)) return false;
            // Stock only tags same-team players in team matches, or everyone when "show enemy names" is ALWAYS (2).
            __state = (int)NetworkMatch.m_show_enemy_names;
            NetworkMatch.m_show_enemy_names = (MatchShowEnemyNames)2;
            return true;
        }
        static void Postfix(int __state)
        {
            if (__state >= 0) NetworkMatch.m_show_enemy_names = (MatchShowEnemyNames)__state;
        }
    }

    /// SHOW PLAYER NAMES off: skip only the stock name text; olmod's team-health bar (a postfix on this method) still draws.
    [HarmonyPatch(typeof(UIManager), "DrawMpPlayerName")]
    static class HUD2_DrawMpPlayerName
    {
        static bool Prefix()
        {
            return !(OlCoop.Death.CoopDeath.Active && !OlCoop.Death.CoopSettings.ShowNames);
        }
        static void Postfix(Player player, Vector2 offset)
        {
            if (!OlCoop.Death.CoopDeath.Active) return;
            try { CoopHud.DrawHealthBar(player, offset); } catch (Exception ex) { CoopLog.Error("HUD2", ex); }
        }
    }

    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class HUD5_HudElementDraw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != CoopHud.uiRespawnOverlay) return;
            try { CoopHud.DrawRespawnTimer(__instance); } catch (Exception ex) { CoopLog.Error("HUD5", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class HUD3_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(CoopHud.MsgName, CoopHud.OnName);
            NetworkServer.RegisterHandler(SpectateHud.MsgMine, SpectateHud.OnMine);
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class HUD4_ApplyNames
    {
        static void Postfix()
        {
            if (!OlCoop.Death.CoopDeath.Active) return;
            try { CoopHud.HostApplyNames(); CoopHud.EnsureOverlay(); } catch (Exception ex) { CoopLog.Error("HUD4", ex); }
        }
    }
}

namespace OlCoop.Hud
{
    /// <summary>
    /// Spectating shows the followed player's own HUD (0.6.5 HUD9 draws the stock HUD with GameManager.m_player_ship/m_local_player
    /// swapped to that ship). 0.6.9: the values it draws are the player's live ones. Before, they came from the host's copies 4x/s, and
    /// a joiner's energy/ammo/missiles on the host's copy are not its real values (olmod sniper packets: the joiner owns them), so a
    /// spectated joiner's HUD didn't drain. Now each joiner reports its own HUD values to the host 10x/s (msg 205); the host sends
    /// everyone's (armor from its own copies, which are authoritative) 10x/s (msg 200); while spectating, HUD9 draws with the newest
    /// values for the followed ship written into its Player/PlayerShip for that draw call only.
    /// </summary>
    public static class SpectateHud
    {
        public const short MsgStats = 200;   // H->J every player's HUD values (unreliable, 10/s)
        public const short MsgMine = 205;    // J->H our own HUD values (unreliable, 10/s)
        public static readonly UIElementType uiSpectOverlay = (UIElementType)124;
        const int Slot = 2, N = 8;
        const float PERIOD = 0.1f;
        static bool s_on, s_logged;
        static float s_next_send, s_next_mine;

        public class Stats
        {
            public float hp, energy, heat, overheat; public int ammo, weapon, missile; public bool boosting;
            public int[] ma = new int[N]; public byte[] wl = new byte[N]; public byte[] ml = new byte[N];
            public float time;
            public Stats Copy()
            {
                var c = (Stats)MemberwiseClone();
                c.ma = (int[])ma.Clone(); c.wl = (byte[])wl.Clone(); c.ml = (byte[])ml.Clone();
                return c;
            }
            public void Write(NetworkWriter w)
            {
                w.Write((short)Mathf.RoundToInt(hp)); w.Write(energy); w.Write((short)ammo); w.Write((byte)weapon); w.Write((byte)missile);
                w.Write(heat); w.Write(overheat); w.Write(boosting);
                for (int i = 0; i < N; i++) { w.Write((short)ma[i]); w.Write(wl[i]); w.Write(ml[i]); }
            }
            public static Stats Read(NetworkReader r)
            {
                var s = new Stats { hp = r.ReadInt16(), energy = r.ReadSingle(), ammo = r.ReadInt16(), weapon = r.ReadByte(), missile = r.ReadByte(),
                    heat = r.ReadSingle(), overheat = r.ReadSingle(), boosting = r.ReadBoolean() };
                for (int i = 0; i < N; i++) { s.ma[i] = r.ReadInt16(); s.wl[i] = r.ReadByte(); s.ml[i] = r.ReadByte(); }
                return s;
            }
        }
        static readonly Dictionary<uint, Stats> s_stats = new Dictionary<uint, Stats>();   // newest per netId (joiner: from the host)
        static readonly Dictionary<uint, Stats> s_reported = new Dictionary<uint, Stats>(); // host: joiners' own reports

        public class StatsMsg : MessageBase
        {
            public List<KeyValuePair<uint, Stats>> e = new List<KeyValuePair<uint, Stats>>();
            public override void Serialize(NetworkWriter w) { w.Write((byte)e.Count); foreach (var kv in e) { w.WritePackedUInt32(kv.Key); kv.Value.Write(w); } }
            public override void Deserialize(NetworkReader r) { int n = r.ReadByte(); e.Clear(); for (int i = 0; i < n; i++) { uint id = r.ReadPackedUInt32(); e.Add(new KeyValuePair<uint, Stats>(id, Stats.Read(r))); } }
        }

        static Stats Read(Player p)
        {
            var s = new Stats { hp = p.m_hitpoints, energy = p.m_energy, ammo = p.m_ammo, weapon = (int)p.m_weapon_type, missile = (int)p.m_missile_type };
            for (int i = 0; i < N; i++)
            {
                if (i < p.m_missile_ammo.Length) s.ma[i] = p.m_missile_ammo[i];
                if (i < p.m_weapon_level.Length) s.wl[i] = (byte)p.m_weapon_level[i];
                if (i < p.m_missile_level.Length) s.ml[i] = (byte)p.m_missile_level[i];
            }
            var ship = p.c_player_ship;
            if (ship != null) { s.heat = ship.m_boost_heat; s.overheat = ship.m_boost_overheat_timer; s.boosting = ship.m_boosting; }
            return s;
        }

        static bool InLevel { get { return CoopConfig.Active && !GameplayManager.IsMultiplayer && GameplayManager.LevelIsLoaded; } }

        /// Joiner: our own values to the host.
        public static void JoinerTick()
        {
            if (!CoopConfig.IsJoiner || !InLevel || Time.realtimeSinceStartup < s_next_mine) return;
            var me = GameManager.m_local_player; var c = Client.GetClient();
            if (me == null || c == null || !Client.IsConnected()) return;
            s_next_mine = Time.realtimeSinceStartup + PERIOD;
            var m = new StatsMsg(); m.e.Add(new KeyValuePair<uint, Stats>(me.netId.Value, Read(me)));
            c.SendByChannel(MsgMine, m, OlCoop.Session.LNet2.ChUnrel);
        }

        /// Host: a joiner's own values (only accepted for that connection's own player).
        public static void OnMine(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<StatsMsg>();
                foreach (var p in Overload.NetworkManager.m_Players)
                {
                    if (p == null || p.connectionToClient == null || p.connectionToClient.connectionId != msg.conn.connectionId) continue;
                    foreach (var kv in m.e) if (kv.Key == p.netId.Value) { kv.Value.time = Time.realtimeSinceStartup; s_reported[kv.Key] = kv.Value; }
                }
            }
            catch (Exception ex) { CoopLog.Error("SpectateHud.OnMine", ex); }
        }

        public static void HostTick()
        {
            if (!CoopConfig.IsHost || !NetworkServer.active || !InLevel) return;
            if (Time.realtimeSinceStartup < s_next_send) return;
            s_next_send = Time.realtimeSinceStartup + PERIOD;
            var m = new StatsMsg();
            float now = Time.realtimeSinceStartup;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null) continue;
                var st = Read(p);
                Stats rep;
                if (!p.isLocalPlayer && s_reported.TryGetValue(p.netId.Value, out rep) && now - rep.time < 1f)
                {
                    float hp = st.hp; st = rep.Copy(); st.hp = hp; // the joiner's own values, armor from the host (authoritative)
                }
                st.time = now;
                s_stats[p.netId.Value] = st;
                m.e.Add(new KeyValuePair<uint, Stats>(p.netId.Value, st));
            }
            if (m.e.Count < 2) return;
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && OlCoop.Session.CoopHost.Verified.Contains(c.connectionId))
                    c.SendByChannel(MsgStats, m, OlCoop.Session.LNet2.ChUnrel);
        }

        public static void OnStats(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<StatsMsg>();
                float now = Time.realtimeSinceStartup;
                foreach (var kv in m.e) { var st = kv.Value; st.time = now; s_stats[kv.Key] = st; ApplyToCopy(kv.Key, st); }
            }
            catch (Exception ex) { CoopLog.Error("OnStats", ex); }
        }

        /// Joiner: write the host's values into our copies of the OTHER players (never our own ship: we own our energy/ammo), so the
        /// name-tag health bars show real numbers.
        static void ApplyToCopy(uint netId, Stats s)
        {
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.isLocalPlayer || p.netId.Value != netId) continue;
                p.m_hitpoints = s.hp; p.m_energy = s.energy; p.m_ammo = s.ammo;
                if (s.weapon >= 0 && s.weapon < p.m_weapon_level.Length) p.m_weapon_type = (WeaponType)s.weapon;
                if (s.missile >= 0 && s.missile < p.m_missile_ammo.Length) { p.m_missile_type = (MissileType)s.missile; p.m_missile_ammo[s.missile] = s.ma[s.missile]; }
            }
        }

        // ---------------------------------------------------------------- HUD9: draw with the followed player's live values
        public class Saved
        {
            public PlayerShip ship; public Player player; public Player target;
            public Stats orig;
        }

        static void Write(Player p, Stats s)
        {
            p.m_hitpoints = s.hp; p.m_energy = s.energy; p.m_ammo = s.ammo;
            if (s.weapon < p.m_weapon_level.Length) p.m_weapon_type = (WeaponType)s.weapon;
            if (s.missile < p.m_missile_ammo.Length) p.m_missile_type = (MissileType)s.missile;
            for (int i = 0; i < N; i++)
            {
                if (i < p.m_missile_ammo.Length) p.m_missile_ammo[i] = s.ma[i];
                if (i < p.m_weapon_level.Length) p.m_weapon_level[i] = (WeaponUnlock)s.wl[i];
                if (i < p.m_missile_level.Length) p.m_missile_level[i] = (WeaponUnlock)s.ml[i];
            }
            var ship = p.c_player_ship;
            if (ship != null) { ship.m_boost_heat = s.heat; ship.m_boost_overheat_timer = s.overheat; ship.m_boosting = s.boosting; }
        }

        static int s_live_logged;
        public static Saved SwapIn()
        {
            var t = OlCoop.Death.Spectate.Target;
            if (t == null || t.c_player == null || !CoopConfig.Active) return null;
            var sv = new Saved { ship = GameManager.m_player_ship, player = GameManager.m_local_player, target = t.c_player };
            Stats st;
            if (s_stats.TryGetValue(t.c_player.netId.Value, out st) && Time.realtimeSinceStartup - st.time < 1.5f)
            {
                sv.orig = Read(t.c_player);
                try { Write(t.c_player, st); }
                catch { Write(t.c_player, sv.orig); throw; }
                if (s_live_logged++ == 0) CoopLog.Write("SPECT", "HUD drawn with netId=" + t.c_player.netId.Value + "'s live values (armor " + st.hp.ToString("F0") + " energy " + st.energy.ToString("F0") + " ammo " + st.ammo + ")");
            }
            RealShip = sv.ship;
            GameManager.m_player_ship = t; GameManager.m_local_player = t.c_player;
            return sv;
        }

        /// 0.6.14: our own ship while the followed one is swapped in (the respawn timer must test OUR death, not theirs).
        public static PlayerShip RealShip;

        public static void SwapOut(Saved sv)
        {
            if (sv == null) return;
            GameManager.m_player_ship = sv.ship; GameManager.m_local_player = sv.player; RealShip = null;
            if (sv.orig != null && sv.target != null) Write(sv.target, sv.orig);
        }

        static PlayerShip Target { get { return OlCoop.Death.Spectate.Target; } }

        static readonly FieldInfo f_bars = AccessTools.Field(typeof(UIManager), "m_overlay_show_bars");
        static float s_next_ui_log;
        /// Diagnostics while spectating (every 5 s): what decides what is on screen.
        public static void LogUi()
        {
            if (Time.realtimeSinceStartup < s_next_ui_log) return;
            s_next_ui_log = Time.realtimeSinceStartup + 5f;
            float hudAlpha = -1f;
            for (int i = 0; i < UIManager.m_num_elements && i < UIManager.m_ui_element.Length; i++)
            {
                var e = UIManager.m_ui_element[i];
                if (e != null && e.m_type == UIElementType.HUD) { hudAlpha = e.m_alpha; break; }
            }
            var cam = Camera.main; var v = GameManager.m_viewer;
            var ui = v != null ? v.c_ui_mesh_transform : null;
            var mr = ui != null ? ui.GetComponent<MeshRenderer>() : null;
            string extra = " viewerOnMainCam=" + (v != null && cam != null && v.gameObject == cam.gameObject) +
                " uiLayerSeen=" + (ui != null && cam != null && (cam.cullingMask & (1 << ui.gameObject.layer)) != 0) +
                " uiRenderer=" + (mr != null ? mr.enabled.ToString() : "none") +
                " camToUi=" + (ui != null && cam != null ? (ui.position - cam.transform.position).magnitude.ToString("F2") : "?") +
                " camNear=" + (cam != null ? cam.nearClipPlane.ToString("F2") : "?");
            CoopLog.Write("SPECT", "ui: hudElement=" + UIManager.TypeExists(UIElementType.HUD) + " hudAlpha=" + hudAlpha.ToString("F2") + " HUD_ALPHA=" + UIElement.HUD_ALPHA.ToString("F2") +
                " bars=" + (f_bars != null ? f_bars.GetValue(null) : "?") + " bgDark=" + UIManager.ui_bg_dark + " bgFade=" + UIManager.ui_bg_fade.ToString("F2") +
                " blackOut=" + UIManager.ui_dying_black_out.ToString("F2") + " vr=" + GameplayManager.VRActive + " elements=" + UIManager.m_num_elements +
                " uiMesh=" + (ui != null ? (ui.parent != null ? ui.parent.name : "null") + " " + ui.localPosition.ToString("F2") + " active=" + ui.gameObject.activeInHierarchy : "null") +
                " cam=" + (cam != null ? (cam.transform.parent != null ? cam.transform.parent.name : "null") + " " + cam.transform.localPosition.ToString("F2") : "null") + extra);
        }

        public static void Ensure()
        {
            bool want = OlCoop.Death.Spectate.On && Target != null;
            if (want && !s_on) { UIManager.CreateOverlayElement(Vector2.zero, Slot, uiSpectOverlay, -1f); s_on = true; s_logged = false; s_live_logged = 0; }
            else if (!want && s_on) { UIManager.ClearOverlayElement(Slot); s_on = false; }
        }

        public static void Draw(UIElement uie)
        {
            var t = Target;
            if (t == null || t.c_player == null) return;
            string name = CoopHud.NameOf(t.c_player);
            var pos = new Vector2(0f, -250f);
            uie.DrawStringSmall("SPECTATING " + name, pos, 0.6f, StringOffset.CENTER, UIManager.m_col_hi4, 1f, -1f);
            if (!s_logged) { s_logged = true; CoopLog.Write("SPECT", "drawing spectator readout for netId=" + t.c_player.netId.Value + " (stock HUD shows its values)"); }
        }
    }

    /// HUD9 (0.6.11): while spectating, the WHOLE UI frame (UIManager.Draw: element loop incl. the HUD, full-screen effects, bars,
    /// names) is drawn as if the followed ship were ours: GameManager.m_player_ship / m_local_player swapped and its live values written
    /// in, restored afterwards. 0.6.5-0.6.10 swapped only around UIElement.DrawHUD, but UIManager.Draw itself decides from the local
    /// ship (our wreck, m_dying) whether HUD elements and screen effects are drawn at all, and the death view stayed on screen.
    [HarmonyPatch(typeof(UIManager), "Draw")]
    static class HUD9_SpectatedUi
    {
        static bool s_logged;
        static void Prefix(out SpectateHud.Saved __state)
        {
            __state = null;
            try
            {
                if (OlCoop.Death.Spectate.Target == null) return;
                if (GameplayManager.m_gameplay_state != GameplayState.PLAYING) { OlCoop.Death.Spectate.KeepHud(); return; } // menus draw as stock
                OlCoop.Death.Spectate.NormalView();
                if (!UIManager.TypeExists(UIElementType.HUD)) { UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 7000, UIElementType.HUD); CoopLog.Write("SPECT", "HUD element was gone; re-created"); }
                SpectateHud.LogUi(); // before the swap: our own viewer / camera
                __state = SpectateHud.SwapIn();
                if (__state != null && !s_logged) { s_logged = true; CoopLog.Write("SPECT", "drawing the UI as netId=" + __state.target.netId.Value); }
            }
            catch (Exception ex) { CoopLog.Error("HUD9 pre", ex); }
        }
        static Exception Finalizer(SpectateHud.Saved __state, Exception __exception)
        {
            try { SpectateHud.SwapOut(__state); } catch (Exception ex) { CoopLog.Error("HUD9 post", ex); }
            if (__exception != null) CoopLog.Error("HUD9", __exception);
            return null;
        }
    }

    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class HUD6_SpectateDraw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != SpectateHud.uiSpectOverlay) return;
            try { SpectateHud.Draw(__instance); } catch (Exception ex) { CoopLog.Error("HUD6", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class HUD7_SpectateTick
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { SpectateHud.HostTick(); SpectateHud.JoinerTick(); SpectateHud.Ensure(); } catch (Exception ex) { CoopLog.Error("HUD7", ex); }
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class HUD8_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(SpectateHud.MsgStats, SpectateHud.OnStats);
        }
    }
}
