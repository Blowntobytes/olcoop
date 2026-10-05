using System;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using OlCoop.Death;

namespace OlCoop.UI
{
    /// <summary>
    /// Co-op Options window, toggled with F8 (works in menus and in game).
    /// Host (or anyone not yet joined) edits the settings; they are saved to olcoop-settings.txt and pushed to joiners.
    /// Joiners see the host's settings read-only.
    /// </summary>
    public class CoopMenu : MonoBehaviour
    {
        static CoopMenu s_inst;
        bool m_open;
        Rect m_rect = new Rect(0, 0, 520, 520);
        bool m_prev_visible; CursorLockMode m_prev_lock;
        GUIStyle m_title, m_body, m_small;

        public static void Ensure()
        {
            if (s_inst != null) return;
            var go = new GameObject("olcoop_menu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_inst = go.AddComponent<CoopMenu>();
            CoopLog.Write("UI", "co-op options window ready (F8)");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) Toggle();
            if (m_open) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        }

        void Toggle()
        {
            m_open = !m_open;
            if (m_open)
            {
                m_prev_visible = Cursor.visible; m_prev_lock = Cursor.lockState;
                m_rect.x = (Screen.width - m_rect.width) / 2f; m_rect.y = (Screen.height - m_rect.height) / 2f;
            }
            else { Cursor.visible = m_prev_visible; Cursor.lockState = m_prev_lock; }
        }

        static bool CanEdit { get { return !CoopConfig.IsJoiner; } }

        void OnGUI()
        {
            if (!m_open) return;
            if (m_title == null)
            {
                m_title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                m_body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                m_small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            }
            GUI.depth = -1000;
            m_rect = GUI.Window(0x0C0091, m_rect, DrawWindow, "olcoop - Co-op Options");
        }

        void DrawWindow(int id)
        {
            GUILayout.Space(6);
            GUILayout.Label(CoopVersion.Full + "   " + RoleText(), m_small);
            GUILayout.Space(8);
            GUILayout.Label("When a player dies:", m_title);

            GUI.enabled = CanEdit;
            var before = CoopSettings.Mode; float beforeDelay = CoopSettings.RespawnDelay;
            if (GUILayout.Toggle(CoopSettings.Mode == DeathMode.Spectate, "  Spectate - watch living teammates until the level is completed; respawn at the start of the next level", m_body))
                CoopSettings.Mode = DeathMode.Spectate;
            if (GUILayout.Toggle(CoopSettings.Mode == DeathMode.Respawn, "  Respawn - come back next to a living teammate after a cooldown", m_body))
                CoopSettings.Mode = DeathMode.Respawn;
            if (CoopSettings.Mode == DeathMode.Respawn)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(28);
                GUILayout.Label("Cooldown: " + CoopSettings.RespawnDelay.ToString("0") + " s", m_body, GUILayout.Width(130));
                CoopSettings.RespawnDelay = Mathf.Round(GUILayout.HorizontalSlider(CoopSettings.RespawnDelay, CoopSettings.MinDelay, CoopSettings.MaxDelay, GUILayout.Width(300)));
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Toggle(CoopSettings.Mode == DeathMode.Hardcore, "  Hardcore - if anyone dies, the level restarts for everyone", m_body))
                CoopSettings.Mode = DeathMode.Hardcore;
            GUI.enabled = true;

            GUILayout.Space(8);
            GUILayout.Label("In every mode, if all players are dead at the same time the level restarts for everyone.", m_small);
            if (!CanEdit) GUILayout.Label("These are the host's settings. Only the host can change them.", m_small);

            if (CanEdit && (before != CoopSettings.Mode || !Mathf.Approximately(beforeDelay, CoopSettings.RespawnDelay)))
            {
                CoopSettings.Save();
                Broadcast();
                CoopLog.Write("SETTINGS", "changed to " + CoopSettings.Describe());
            }

            if (CoopConfig.Active)
            {
                GUILayout.Space(10);
                var roster = Session.CoopLobby.Current();
                GUILayout.Label("In this session (" + roster.Count + "/" + Session.CoopLobby.MaxPlayers + "):", m_title);
                if (roster.Count == 0) GUILayout.Label(CoopConfig.IsJoiner ? "waiting for the host..." : "nobody yet", m_small);
                foreach (var e in roster) GUILayout.Label("  " + Session.CoopLobby.Line(e), m_body);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close (F8)", GUILayout.Width(120), GUILayout.Height(28))) Toggle();
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        static string RoleText()
        {
            if (CoopConfig.IsJoiner) return "JOINED (" + CoopConfig.JoinIp + ")";
            if (CoopConfig.IsHost)
            {
                int n = 0;
                foreach (var c in NetworkServer.connections) if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) n++;
                return "HOSTING - " + n + " joined";
            }
            return "not in co-op (settings are saved for when you host)";
        }

        static void Broadcast()
        {
            if (!CoopConfig.IsHost || !NetworkServer.active) return;
            var m = new ConfigMsg { mode = (byte)CoopSettings.Mode, delay = CoopSettings.RespawnDelay, ff = CoopSettings.FriendlyFire }; // ff was missing: changing the mode here switched friendly fire off on joiners
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(DNet.Config, m);
            if (GameplayManager.LevelIsLoaded) GameplayManager.AddHUDMessage("CO-OP DEATH MODE: " + CoopSettings.Describe().ToUpperInvariant(), -1, true);
        }
    }
}
