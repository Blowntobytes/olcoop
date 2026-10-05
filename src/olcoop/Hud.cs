using System;
using System.Collections.Generic;
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
                var ship = GameManager.m_player_ship;
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
