using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.Death
{
    /// <summary>
    /// Co-op death modes (docs/death-design.md), chosen by the host in the F8 options window:
    ///  SPECTATE - a dead player watches living teammates until the level ends; all dead = level restart.
    ///  RESPAWN  - a dead player respawns next to a living teammate after a cooldown; all dead at once = level restart.
    ///  HARDCORE - any death restarts the level for everyone.
    /// The host decides everything; joiners only display it.
    /// </summary>
    public enum DeathMode { Spectate = 0, Respawn = 1, Hardcore = 2 }

    public static class CoopSettings
    {
        public static DeathMode Mode = DeathMode.Respawn;
        public static float RespawnDelay = 10f;
        public static bool FriendlyFire = false;
        /// Local preference (not set by the host): pilot names above teammates.
        public static bool ShowNames = true;
        public const float MinDelay = 3f, MaxDelay = 60f;
        public static bool FromHost; // joiner: values came from the host

        static string FilePath
        {
            get
            {
                string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(string.IsNullOrEmpty(d) ? "." : d, "olcoop-settings.txt");
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var kv = line.Split('=');
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim();
                    if (k == "deathmode") { try { Mode = (DeathMode)Enum.Parse(typeof(DeathMode), v, true); } catch { } }
                    if (k == "shownames") ShowNames = !(v == "0" || v.ToLowerInvariant() == "false" || v.ToLowerInvariant() == "off");
                    if (k == "friendlyfire") FriendlyFire = v == "1" || v.ToLowerInvariant() == "true" || v.ToLowerInvariant() == "on";
                    if (k == "respawndelay")
                    {
                        float f;
                        if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f))
                            RespawnDelay = Mathf.Clamp(f, MinDelay, MaxDelay);
                    }
                }
                CoopLog.Write("SETTINGS", "loaded " + Describe());
            }
            catch (Exception ex) { CoopLog.Error("CoopSettings.Load", ex); }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, "# olcoop co-op options (host decides for everyone)\r\ndeathmode=" + Mode + "\r\nrespawndelay=" +
                    RespawnDelay.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "\r\nfriendlyfire=" + (FriendlyFire ? "on" : "off") +
                    "\r\n# shownames is your own setting, not the host's\r\nshownames=" + (ShowNames ? "on" : "off") + "\r\n");
            }
            catch (Exception ex) { CoopLog.Error("CoopSettings.Save", ex); }
        }

        /// Host: push the current settings to every verified joiner.
        public static void Broadcast()
        {
            if (!CoopConfig.IsHost || !NetworkServer.active) return;
            var m = new ConfigMsg { mode = (byte)Mode, delay = RespawnDelay, ff = FriendlyFire };
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(DNet.Config, m);
            if (GameplayManager.LevelIsLoaded) GameplayManager.AddHUDMessage("CO-OP: " + Describe().ToUpperInvariant() + (FriendlyFire ? " - FRIENDLY FIRE ON" : ""), -1, true);
            FriendlyFireRules.Apply();
        }

        public static string Describe()
        {
            switch (Mode)
            {
                case DeathMode.Spectate: return "SPECTATE (dead players watch until the level ends)";
                case DeathMode.Respawn: return "RESPAWN after " + RespawnDelay.ToString("0") + "s";
                default: return "HARDCORE (any death restarts the level)";
            }
        }
    }

    // ----------------------------------------------------------------- messages
    public static class DNet
    {
        public const short Config = 172;       // H->J  mode + delay
        public const short RespawnTimer = 173; // H->J  netId + seconds (or -1 = waiting for a living teammate)
        public const short TeamReset = 175;    // H->J  reason
    }

    public class ConfigMsg : MessageBase
    {
        public byte mode; public float delay; public bool ff;
        public override void Serialize(NetworkWriter w) { w.Write(mode); w.Write(delay); w.Write(ff); }
        public override void Deserialize(NetworkReader r) { mode = r.ReadByte(); delay = r.ReadSingle(); ff = r.ReadBoolean(); }
    }

    public class TimerMsg : MessageBase
    {
        public uint netId; public float seconds;
        public override void Serialize(NetworkWriter w) { w.Write(netId); w.Write(seconds); }
        public override void Deserialize(NetworkReader r) { netId = r.ReadUInt32(); seconds = r.ReadSingle(); }
    }

    // ----------------------------------------------------------------- shared state
    public static class CoopDeath
    {
        public static bool Active { get { CoopConfig.EnsureInit(); return CoopConfig.Active && !GameplayManager.IsMultiplayer && (Session.CoopSession.InLevel || GameplayManager.IsMultiplayerActive); } }
        public static bool IsHost { get { return Active && CoopConfig.IsHost && Server.IsActive(); } }

        // host bookkeeping
        static readonly Dictionary<uint, float> s_death_time = new Dictionary<uint, float>();
        static readonly HashSet<uint> s_timer_sent = new HashSet<uint>();
        static float s_reset_at = -1f;
        static bool s_reset_latched;
        /// an exit has started this level. Exits win over deaths: no respawn timers, no hardcore/team-wipe restart; dead
        /// players are revived next to the exit by CoopFlow instead.
        public static bool ExitInProgress;

        // local display
        static float s_local_respawn_at = -1f; static int s_last_shown = -1;

        public static void ResetForLevel()
        {
            s_death_time.Clear(); s_timer_sent.Clear(); s_reset_at = -1f; s_reset_latched = false; ExitInProgress = false;
            s_local_respawn_at = -1f; s_last_shown = -1;
            Hud.CoopHud.ClearRespawn();
            Spectate.Stop(null);
        }

        static bool Alive(PlayerShip s) { return s != null && !(bool)s.m_dying && !(bool)s.m_dead; }

        public static List<PlayerShip> Ships()
        {
            var l = new List<PlayerShip>();
            foreach (var p in Overload.NetworkManager.m_Players) if (p != null && p.c_player_ship != null) l.Add(p.c_player_ship);
            return l;
        }

        // ---------------- host tick
        public static void HostTick()
        {
            // Not PLAYING-only: the host may have the map (AUTOMAP) or the Esc menu (MENUS) open; nothing is paused in co-op.
            if (!IsHost || !GameplayManager.LevelIsLoaded || GameplayManager.m_gameplay_state == GameplayState.EXIT || ExitInProgress) return;
            float now = Time.time;
            var ships = Ships();
            int alive = 0;
            foreach (var s in ships)
            {
                uint id = s.c_player.netId.Value;
                if (Alive(s)) { alive++; s_death_time.Remove(id); s_timer_sent.Remove(id); continue; }
                if (!s_death_time.ContainsKey(id))
                {
                    s_death_time[id] = now;
                    CoopLog.Write("DEATH", "player netId=" + id + " died; mode=" + CoopSettings.Mode + " alive=" + ships.FindAll(Alive).Count);
                    if (CoopSettings.Mode == DeathMode.Hardcore) ScheduleReset("HARDCORE - A PLAYER DIED");
                }
            }
            if (s_reset_at < 0f && ships.Count > 0 && alive == 0) ScheduleReset("TEAM WIPED");

            if (s_reset_at >= 0f)
            {
                if (now >= s_reset_at) DoReset();
                return;
            }

            if (CoopSettings.Mode != DeathMode.Respawn) return;
            foreach (var s in ships)
            {
                if (Alive(s)) continue;
                uint id = s.c_player.netId.Value;
                float t0; if (!s_death_time.TryGetValue(id, out t0)) continue;
                float left = t0 + CoopSettings.RespawnDelay - now;
                if (!s_timer_sent.Contains(id)) { SendTimer(id, left); s_timer_sent.Add(id); }
                if (left > 0f || !(bool)s.m_dead) continue; // wait for the cooldown and the death animation
                var anchor = PickAnchor(s);
                if (anchor == null) continue;
                ServerRespawn(s, anchor);
                s_death_time.Remove(id); s_timer_sent.Remove(id);
            }
        }

        static PlayerShip PickAnchor(PlayerShip dead)
        {
            PlayerShip best = null; float bd = float.MaxValue;
            foreach (var s in Ships())
            {
                if (s == dead || !Alive(s)) continue;
                float d = (s.c_transform.position - dead.c_transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        static void ServerRespawn(PlayerShip s, PlayerShip anchor)
        {
            var sp = new LevelData.SpawnPoint(anchor.c_transform.position - anchor.c_transform.forward * 4f, anchor.c_transform.rotation, 0);
            Session.CoopHost.IgnoreShip = s;
            try { Session.CoopHost.TryAroundPublic("teammate", anchor.c_transform.position, anchor.c_transform.rotation, UnityEngine.Random.Range(0, 12), ref sp); }
            finally { Session.CoopHost.IgnoreShip = null; }
            s.c_transform.position = sp.position; s.c_transform.rotation = sp.orientation;
            NetworkSpawnPlayer.StartSpawnInvul(s.c_player);
            s.c_player.m_input_deficit = 0;
            s.m_death_stats_recorded = false;
            Server.RespawnPlayer(s.c_player, sp.position, sp.orientation);
            CoopLog.Write("DEATH", "respawned netId=" + s.c_player.netId.Value + " near netId=" + anchor.c_player.netId.Value + " at " + sp.position.ToString("F1"));
        }

        /// Host: an exit started. Cancel a pending hardcore/team-wipe restart and stop death handling for the rest of the level.
        public static void CancelForExit()
        {
            if (ExitInProgress) return;
            ExitInProgress = true;
            if (s_reset_at >= 0f) CoopLog.Write("DEATH", "level restart cancelled: the team is exiting");
            s_reset_at = -1f; s_reset_latched = true;
        }

        /// Host: bring a dead ship back at an exact spot (used to put dead players into the exit sequence).
        public static void RespawnAt(PlayerShip s, Vector3 pos, Quaternion rot, string why)
        {
            s.c_transform.position = pos; s.c_transform.rotation = rot;
            NetworkSpawnPlayer.StartSpawnInvul(s.c_player);
            s.c_player.m_input_deficit = 0;
            s.m_death_stats_recorded = false;
            Server.RespawnPlayer(s.c_player, pos, rot);
            s_death_time.Remove(s.c_player.netId.Value); s_timer_sent.Remove(s.c_player.netId.Value);
            CoopLog.Write("DEATH", "revived netId=" + s.c_player.netId.Value + " at " + pos.ToString("F1") + " (" + why + ")");
        }

        /// Joiner: the team is exiting; don't leave for a restart that the host has cancelled.
        public static void CancelLeave()
        {
            if (s_leave_at < 0f) return;
            s_leave_at = -1f;
            CoopLog.Write("DEATH", "restart leave cancelled: the team is exiting");
        }

        static void SendTimer(uint netId, float seconds)
        {
            var m = new TimerMsg { netId = netId, seconds = seconds };
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(DNet.RespawnTimer, m);
            OnTimer(netId, seconds); // host's own display
        }

        static void ScheduleReset(string reason)
        {
            if (s_reset_latched) return;
            s_reset_latched = true;
            s_reset_at = Time.time + 4f;
            CoopLog.Write("DEATH", "level reset scheduled: " + reason);
            var m = new StringMessage(reason);
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(DNet.TeamReset, m);
            GameplayManager.AddHUDMessage("CO-OP: " + reason + " - RESTARTING LEVEL", -1, true);
        }

        static void DoReset()
        {
            s_reset_at = -1f;
            CoopLog.Write("DEATH", "restarting level for everyone");
            Spectate.Stop(GameManager.m_player_ship);
            GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit);
            GameplayManager.CreateRestartGame();
            GameplayManager.m_between_level_start = Time.realtimeSinceStartup;
            GameplayManager.SwitchToMenu(MenuState.PLAY_GAME);
        }

        static float s_leave_at = -1f;

        /// Joiner: the host is about to reload the level. Leave ours cleanly (the host's reload would otherwise unspawn our ship and
        /// leave the client half torn down), wait in the main menu while connected, and let the rejoin request fetch the new level.
        public static void JoinerLeaveForRestart()
        {
            if (!CoopConfig.IsJoiner) return;
            s_leave_at = Time.realtimeSinceStartup + 2f; // let the message be seen first; the host reloads after 4 s
        }

        public static void JoinerLeaveTick()
        {
            if (s_leave_at < 0f || Time.realtimeSinceStartup < s_leave_at) return;
            s_leave_at = -1f;
            if (!GameplayManager.LevelIsLoaded && GameManager.m_game_state != GameManager.GameState.GAMEPLAY) return;
            CoopLog.Write("DEATH", "leaving the level for the host's restart; will rejoin from the main menu");
            Spectate.Stop(null);
            Hud.CoopHud.ClearRespawn();
            GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit);
            UIManager.DestroyAll(true);
            GameplayManager.SwitchToMenu(MenuState.MAIN_MENU);
            Session.CoopClient.RejoinSoon();
        }

        // ---------------- every peer
        public static void OnTimer(uint netId, float seconds)
        {
            var me = GameManager.m_local_player;
            if (me == null || me.netId.Value != netId) return;
            s_local_respawn_at = Time.time + Mathf.Max(0f, seconds);
            s_last_shown = -1;
            Hud.CoopHud.SetRespawnAt(s_local_respawn_at);
        }

        public static void LocalTick()
        {
            var ship = GameManager.m_player_ship;
            if (ship == null) return;
            // Only an alive ship clears the countdown: the host sends it when the ship starts DYING, before m_dead is set.
            if (!(bool)ship.m_dead && !(bool)ship.m_dying) { s_local_respawn_at = -1f; Hud.CoopHud.ClearRespawn(); return; }
            // The countdown itself is the yellow MP-style digits (Hud.CoopHud); only the end needs a message.
            if (CoopSettings.Mode == DeathMode.Respawn && s_local_respawn_at > 0f && s_last_shown != 0 && Time.time >= s_local_respawn_at)
            {
                s_last_shown = 0;
                GameplayManager.AddHUDMessage("RESPAWNING NEXT TO A TEAMMATE...", -1, true);
            }
        }

        // Called from our DeadUpdate replacement once the death fade finishes.
        public static void EnterDeadState(PlayerShip ship)
        {
            if (!ship.isLocalPlayer) return;
            PlayerShip.DeathPaused = false; // stock MP death pause (static, survives level loads); co-op never uses it
            UIManager.SetScreenFade(0f);
            UIManager.ShowCinematicBars(false);
            CoopLog.Write("DEATH", "local dead state reached; mode=" + CoopSettings.Mode);
            if (CoopSettings.Mode == DeathMode.Hardcore) return;
            Spectate.Start(ship);
            if (CoopSettings.Mode == DeathMode.Spectate)
                GameplayManager.AddHUDMessage("YOU DIED - SPECTATING UNTIL THE LEVEL ENDS (FIRE TO SWITCH)", -1, true);
        }



        /// Replacement for PlayerShip.DeadUpdate in co-op: never ends the level, keeps the ship dead until the host respawns it.
        public static void DeadUpdate(PlayerShip s)
        {
            if (s.m_dead_timer > 0f)
            {
                s.m_dead_timer -= RUtility.FRAMETIME_GAME;
                s.c_rigidbody.velocity *= 0.95f;
                if (s.isLocalPlayer) UIManager.SetScreenFade(Mathf.Clamp01(1f - s.m_dead_timer * 4f));
                if (s.m_dead_timer <= 0f) { s.m_dead_timer = -1f; EnterDeadState(s); }
            }
            if (s.isLocalPlayer) Spectate.Tick(s);
        }

        static readonly Dictionary<int, List<MeshRenderer>> s_hidden = new Dictionary<int, List<MeshRenderer>>();

        /// Hide only renderers that are currently visible, and later re-show exactly those (some ship meshes are normally off).
        public static void HideShip(PlayerShip s, bool hide)
        {
            if (s == null) return;
            int key = s.GetInstanceID();
            List<MeshRenderer> list;
            if (hide)
            {
                if (!s_hidden.TryGetValue(key, out list)) { list = new List<MeshRenderer>(); s_hidden[key] = list; }
                foreach (var r in s.GetComponentsInChildren<MeshRenderer>(true)) if (r.enabled) { r.enabled = false; list.Add(r); }
            }
            else if (s_hidden.TryGetValue(key, out list))
            {
                foreach (var r in list) if (r != null) r.enabled = true;
                s_hidden.Remove(key);
            }
        }

        // ---- things the death sequence turns off that a mid-level respawn must turn back on
        static readonly Dictionary<int, bool> s_headlights = new Dictionary<int, bool>();
        static readonly MethodInfo m_toggle_headlights = AccessTools.Method(typeof(PlayerShip), "ToggleHeadlights");
        static readonly FieldInfo f_blur = AccessTools.Field(typeof(Viewer), "m_damage_blur_strength");
        static readonly FieldInfo f_overbright = AccessTools.Field(typeof(Viewer), "m_damage_overbrighten");

        public static void NoteDying(PlayerShip s) { if (s != null) s_headlights[s.GetInstanceID()] = s.m_headlights_on; }

        public static void FixAfterRespawn(PlayerShip s)
        {
            bool hl;
            if (s_headlights.TryGetValue(s.GetInstanceID(), out hl) && hl && !s.m_headlights_on && m_toggle_headlights != null)
                m_toggle_headlights.Invoke(s, null);
            s_headlights.Remove(s.GetInstanceID());

            if (s.c_mesh_collider != null) s.c_mesh_collider.enabled = true;
            if (s.c_level_collider != null) s.c_level_collider.enabled = true;
            if (s.c_rigidbody != null) { s.c_rigidbody.isKinematic = false; s.c_rigidbody.detectCollisions = true; }
            if (s.c_mesh_collider_trans != null && s.c_mesh_collider_trans.parent == null) s.c_mesh_collider_trans.position = s.c_transform.position;

            if (s.isLocalPlayer)
            {
                UIManager.SetScreenFade(0f);
                UIManager.ShowCinematicBars(false);
                var v = s.c_viewer ?? GameManager.m_viewer;
                if (v != null) { if (f_blur != null) f_blur.SetValue(v, 0f); if (f_overbright != null) f_overbright.SetValue(v, 0f); }
            }
            CoopLog.Write("DEATH", "after respawn netId=" + s.c_player.netId.Value + " local=" + s.isLocalPlayer +
                " meshCol=" + (s.c_mesh_collider != null && s.c_mesh_collider.enabled) + " levelCol=" + (s.c_level_collider != null && s.c_level_collider.enabled) +
                " kinematic=" + (s.c_rigidbody != null && s.c_rigidbody.isKinematic) + " detect=" + (s.c_rigidbody != null && s.c_rigidbody.detectCollisions) +
                " layer=" + s.gameObject.layer + " meshLayer=" + (s.c_mesh_collider != null ? s.c_mesh_collider.gameObject.layer : -1) +
                " detached=" + (s.c_mesh_collider_trans != null && s.c_mesh_collider_trans.parent == null) + " headlights=" + s.m_headlights_on);
        }
    }

    // ----------------------------------------------------------------- spectate camera
    public static class Spectate
    {
        static GameObject s_rig;
        static PlayerShip s_target;
        static PlayerShip s_owner;

        public static bool On { get { return s_rig != null; } }

        // 0.4.20: spectators saw a darker level and no headlights from the ship they followed (11:13 run). A remote ship's lights are
        // ordinary Auto lights competing for the few per-pixel light slots, while our own ship's lights are dead/off. While following a
        // ship, its lights render per-pixel and death screen effects are cleared; its light state is logged to confirm the cause.
        static readonly List<KeyValuePair<Light, LightRenderMode>> s_lights = new List<KeyValuePair<Light, LightRenderMode>>();
        static readonly FieldInfo f_blur = AccessTools.Field(typeof(Viewer), "m_damage_blur_strength");
        static readonly FieldInfo f_over = AccessTools.Field(typeof(Viewer), "m_damage_overbrighten");

        static void BoostLights(PlayerShip t)
        {
            RestoreLights();
            if (t == null || t.c_lights == null) return;
            foreach (var l in t.c_lights)
                if (l != null) { s_lights.Add(new KeyValuePair<Light, LightRenderMode>(l, l.renderMode)); l.renderMode = LightRenderMode.ForcePixel; }
            LogLights("following", t);
        }

        static void RestoreLights()
        {
            foreach (var kv in s_lights) if (kv.Key != null) kv.Key.renderMode = kv.Value;
            s_lights.Clear();
        }

        static void KeepLights(PlayerShip t)
        {
            if (t == null || t.c_lights == null) return;
            var v = GameManager.m_viewer;
            if (v != null) { try { if (f_blur != null) f_blur.SetValue(v, 0f); if (f_over != null) f_over.SetValue(v, 0f); } catch { } }
        }

        static void LogLights(string why, PlayerShip t)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(why).Append(" netId=").Append(t.c_player.netId.Value).Append(" headlightsOn=").Append(t.m_headlights_on)
                  .Append(" unlock=").Append(t.c_player.m_unlock_headlight).Append(" pixelLights=").Append(QualitySettings.pixelLightCount).Append(" lights=");
                for (int i = 0; i < t.c_lights.Length; i++)
                {
                    var l = t.c_lights[i];
                    if (l == null) { sb.Append("[null]"); continue; }
                    sb.Append("[").Append(i).Append(' ').Append(l.type).Append(" on=").Append(l.enabled && l.gameObject.activeInHierarchy)
                      .Append(" i=").Append(l.intensity.ToString("F2")).Append(" r=").Append(l.range.ToString("F0")).Append(' ').Append(l.renderMode).Append("]");
                }
                CoopLog.Write("SPECT", sb.ToString());
            }
            catch (Exception ex) { CoopLog.Error("LogLights", ex); }
        }

        static int s_last_frame = -1;
        static float s_next_log, s_next_light_log;
        static int s_reparents;

        static Transform Cam()
        {
            var c = Camera.main;
            if (c != null) return c.transform;
            return s_owner != null ? s_owner.c_camera_transform : null;
        }

        static void Attach()
        {
            var cam = Cam();
            if (cam == null || s_rig == null || cam.parent == s_rig.transform) return;
            if (s_reparents++ < 5)
                CoopLog.Write("SPECT", "camera re-attached (was parent=" + (cam.parent != null ? cam.parent.name : "null") + ")");
            cam.parent = s_rig.transform;
            cam.localPosition = Vector3.zero;
            cam.localRotation = Quaternion.identity;
        }

        public static void Start(PlayerShip owner)
        {
            if (s_rig != null) return;
            s_owner = owner;
            s_reparents = 0;
            PlayerShip.DeathPaused = false;
            s_rig = new GameObject("olcoop_spectate_cam");
            s_rig.transform.position = owner.c_transform.position;
            s_rig.transform.rotation = owner.c_transform.rotation;
            var cam = Cam();
            CoopLog.Write("SPECT", "start owner=" + owner.c_player.netId.Value + " cam=" + (cam != null ? cam.name : "null") +
                " camIsShipCam=" + (cam == owner.c_camera_transform) + " ships=" + CoopDeath.Ships().Count);
            Attach();
            Next(+1);
        }

        public static void Stop(PlayerShip owner)
        {
            if (s_rig == null) return;
            var o = owner ?? s_owner;
            var cam = Cam();
            if (o != null && cam != null)
            {
                cam.parent = o.c_cam_controller != null ? o.c_cam_controller : o.m_camera_parent;
                cam.localPosition = Vector3.zero;
                cam.localRotation = Quaternion.identity;
                o.ResetCameraPosition();
            }
            CoopLog.Write("SPECT", "stop");
            if (s_target != null) CoopDeath.HideShip(s_target, false);
            RestoreLights();
            // Never destroy the game's camera with the rig: move anything still parented to it back to the ship first.
            var home = o != null ? (o.c_cam_controller != null ? o.c_cam_controller : o.m_camera_parent) : null;
            for (int i = s_rig.transform.childCount - 1; i >= 0; i--)
            {
                var ch = s_rig.transform.GetChild(i);
                CoopLog.Write("SPECT", "rescued '" + ch.name + "' from the spectate rig");
                ch.parent = home;
                ch.localPosition = Vector3.zero; ch.localRotation = Quaternion.identity;
            }
            UnityEngine.Object.Destroy(s_rig);
            s_rig = null; s_target = null; s_owner = null;
        }

        static void Next(int dir)
        {
            var ships = CoopDeath.Ships();
            var living = ships.FindAll(x => x != s_owner && !(bool)x.m_dying && !(bool)x.m_dead);
            if (living.Count == 0) { if (Time.time >= s_next_log) CoopLog.Write("SPECT", "no living teammate to follow (ships=" + ships.Count + ")"); return; }
            int i = Mathf.Max(-1, living.IndexOf(s_target));
            var t = living[((i + dir) % living.Count + living.Count) % living.Count];
            if (s_target != null && s_target != t) CoopDeath.HideShip(s_target, false);
            s_target = t;
            CoopDeath.HideShip(s_target, true); // first-person view from inside their ship
            BoostLights(s_target);
            GameplayManager.AddHUDMessage("SPECTATING " + Hud.CoopHud.NameOf(s_target.c_player), -1, true);
            CoopLog.Write("SPECT", "following netId=" + s_target.c_player.netId.Value);
        }

        public static void Tick(PlayerShip owner)
        {
            if (s_rig == null) return;
            if (s_last_frame == Time.frameCount) return; // called from DeadUpdate and from the per-frame hook
            s_last_frame = Time.frameCount;
            PlayerShip.DeathPaused = false;
            Attach(); // stock code (dying camera, Esc-while-dead pause) can steal the camera
            if (Time.time >= s_next_log)
            {
                s_next_log = Time.time + 5f;
                var cam = Cam();
                CoopLog.Write("SPECT", "tick target=" + (s_target != null ? s_target.c_player.netId.Value.ToString() : "none") +
                    " cam=" + (cam != null ? cam.position.ToString("F1") + " parent=" + (cam.parent != null ? cam.parent.name : "null") : "null") +
                    " camEnabled=" + (Camera.main != null && Camera.main.enabled));
            }
            if (s_target == null || (bool)s_target.m_dying || (bool)s_target.m_dead) { if (s_target != null) CoopDeath.HideShip(s_target, false); s_target = null; Next(+1); }
            if (Controls.JustPressed(CCInput.FIRE_WEAPON)) Next(+1);
            if (s_target == null) return;
            KeepLights(s_target);
            if (Time.time >= s_next_light_log) { s_next_light_log = Time.time + 15f; LogLights("tick", s_target); }
            s_rig.transform.position = s_target.c_transform.position;
            s_rig.transform.rotation = Quaternion.Slerp(s_rig.transform.rotation, s_target.c_transform.rotation, 0.35f);
        }
    }

    // ================================================================= patches

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class D0_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            var c = Client.GetClient();
            c.RegisterHandler(DNet.Config, msg =>
            {
                var m = msg.ReadMessage<ConfigMsg>();
                CoopSettings.Mode = (DeathMode)m.mode; CoopSettings.RespawnDelay = m.delay; CoopSettings.FriendlyFire = m.ff; CoopSettings.FromHost = true;
                CoopLog.Write("SETTINGS", "host settings: " + CoopSettings.Describe() + " friendlyFire=" + m.ff);
                FriendlyFireRules.Apply();
            });
            c.RegisterHandler(DNet.RespawnTimer, msg => { var m = msg.ReadMessage<TimerMsg>(); CoopDeath.OnTimer(m.netId, m.seconds); });
            c.RegisterHandler(DNet.TeamReset, msg =>
            {
                var m = msg.ReadMessage<StringMessage>();
                CoopLog.Write("DEATH", "host: " + m.value);
                GameplayManager.AddHUDMessage("CO-OP: " + m.value + " - RESTARTING LEVEL", -1, true);
            });
        }
    }

    /// D1: co-op replacement for the single-player DeadUpdate (which ended the level on EVERY peer for ANY ship).
    [HarmonyPatch(typeof(PlayerShip), "DeadUpdate")]
    static class D1_DeadUpdate
    {
        static bool Prefix(PlayerShip __instance)
        {
            if (!CoopDeath.Active) return true;
            try { CoopDeath.DeadUpdate(__instance); } catch (Exception ex) { CoopLog.Error("D1", ex); }
            return false;
        }
    }

    /// D2: safety net - nothing in co-op may open the single-player death menu.
    [HarmonyPatch(typeof(GameplayManager), "PlayerHasDied")]
    static class D2_NoSpDeathMenu
    {
        static bool Prefix()
        {
            // Also while our in-level flag is set: stock code can flip game type / IsMultiplayerActive mid-level (host restart).
            if (!CoopDeath.Active && !(CoopConfig.Active && Session.CoopSession.InLevel)) return true;
            CoopLog.Write("DEATH", "blocked single-player death menu (co-op)");
            return false;
        }
    }

    /// D3: keep the ship's mesh intact (it is reused on respawn); just hide it.
    [HarmonyPatch(typeof(PlayerShip), "ExplodeCockpit")]
    static class D3_NoCockpitBreakup
    {
        static bool Prefix(PlayerShip __instance)
        {
            if (!CoopDeath.Active) return true;
            CoopDeath.HideShip(__instance, true);
            return false;
        }
    }

    /// D4: hide the MP dead-player input block (loadout toggle, MP death pause, scoreboard) for the local dead ship.
    [HarmonyPatch(typeof(PlayerShip), "Update")]
    static class D4_DeadInputSpRules
    {
        static void Prefix(PlayerShip __instance, out bool __state)
        {
            __state = false;
            if (__instance.isLocalPlayer && ((bool)__instance.m_dying || (bool)__instance.m_dead) && CoopDeath.Active)
                __state = Session.SpRulesScope.Enter();
        }
        static Exception Finalizer(bool __state, Exception __exception) { Session.SpRulesScope.Exit(__state); return __exception; }
    }

    /// D6: no MP-style self respawn.
    [HarmonyPatch(typeof(PlayerShip), "CmdReadyToRespawn")]
    static class D6_NoSelfRespawn
    {
        static bool Prefix() { return !CoopDeath.Active; }
    }

    /// D7: corpses take no damage (stops repeated death RPCs/stats).
    [HarmonyPatch(typeof(PlayerShip), "ApplyDamage")]
    static class D7_NoDamageWhenDead
    {
        static bool Prefix(PlayerShip __instance) { return !(CoopDeath.Active && (bool)__instance.m_dead); }
    }

    /// D8: keep the campaign loadout on respawn (MP loadout would lock all weapons; host uses lobby id 0).
    [HarmonyPatch(typeof(NetworkSpawnPlayer), "SetMultiplayerLoadout")]
    static class D8_NoMpLoadout
    {
        static bool Prefix() { return !(CoopConfig.Active && !GameplayManager.IsMultiplayer); }
    }

    /// D9: respawn restore keeps ammo, shows the ship again, ends spectating.
    [HarmonyPatch(typeof(Player), "RestorePlayerShipDataAfterRespawn")]
    static class D9_RestoreKeepAmmo
    {
        static void Prefix(Player __instance, out int __state) { __state = __instance.m_ammo; }
        static void Postfix(Player __instance, int __state)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            __instance.m_ammo = __state;
            var s = __instance.c_player_ship;
            if (s == null) return;
            CoopDeath.HideShip(s, false);
            CoopDeath.FixAfterRespawn(s);
            if (s.isLocalPlayer)
            {
                Spectate.Stop(s);
                if (!UIManager.TypeExists(UIElementType.HUD)) UIManager.CreateUIElement(UIManager.SCREEN_CENTER, 1500, UIElementType.HUD);
                GameplayManager.AddHUDMessage("RESPAWNED", -1, true);
            }
            CoopLog.Write("DEATH", "restore after respawn netId=" + __instance.netId.Value + " local=" + s.isLocalPlayer);
        }
    }

    /// Per-level reset of death state.
    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class D14_LevelReset
    {
        static void Prefix() { if (CoopConfig.Active) CoopDeath.ResetForLevel(); }
    }

    /// Host decisions + local HUD countdown, every frame.
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class D11_Tick
    {
        static void Postfix()
        {
            try { CoopDeath.JoinerLeaveTick(); } catch (Exception ex) { CoopLog.Error("D11 leave", ex); }
            if (!CoopDeath.Active) return;
            try { CoopDeath.HostTick(); CoopDeath.LocalTick(); var ls = GameManager.m_player_ship; if (ls != null && (bool)ls.m_dead) Spectate.Tick(ls); } catch (Exception ex) { CoopLog.Error("D11", ex); }
        }
    }

    /// Settings are loaded at startup; the host announces them when the level starts.
    [HarmonyPatch(typeof(NetworkMatch), "StartPreGame")]
    static class D12_AnnounceMode
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            GameplayManager.AddHUDMessage("CO-OP DEATH MODE: " + CoopSettings.Describe().ToUpperInvariant(), -1, true);
            GameplayManager.AddHUDMessage("FRIENDLY FIRE " + (CoopSettings.FriendlyFire ? "ON" : "OFF"), -1, true);
            FriendlyFireRules.Apply();
        }
    }

    /// Remember headlight state when a ship starts dying (death turns them off; respawn turns them back on).
    [HarmonyPatch(typeof(PlayerShip), "StartDying")]
    static class D16_NoteDying
    {
        static void Prefix(PlayerShip __instance) { if (CoopDeath.Active) CoopDeath.NoteDying(__instance); }
    }

    // ================================================================= friendly fire
    /// FRIENDLY FIRE OFF: players never damage each other (shots, splash, ramming). ON: they do; direct hits are enabled by
    /// letting player-shot layers collide with other players' ship colliders, while a shot always ignores its own ship.
    public static class FriendlyFireRules
    {
        static int s_proj_layer = -1, s_ship_layer = -1;
        static bool s_orig_ignore, s_changed;

        public static bool IsTeammateDamage(PlayerShip victim, GameObject owner)
        {
            if (owner == null || victim == null) return false;
            var p = owner.GetComponent<Player>() ?? owner.GetComponentInParent<Player>();
            if (p == null) { var ps = owner.GetComponent<PlayerShip>() ?? owner.GetComponentInParent<PlayerShip>(); if (ps != null) p = ps.c_player; }
            return p != null && p != victim.c_player;
        }

        public static void LearnLayers(Projectile proj)
        {
            if (proj == null || proj.c_collider == null || proj.m_owner_player == null) return;
            if (s_proj_layer < 0) s_proj_layer = proj.c_collider.gameObject.layer;
            if (s_ship_layer < 0 && proj.m_owner_player.c_player_ship != null && proj.m_owner_player.c_player_ship.c_mesh_collider != null)
                s_ship_layer = proj.m_owner_player.c_player_ship.c_mesh_collider.gameObject.layer;
            Apply();
        }

        public static void Apply()
        {
            if (s_proj_layer < 0 || s_ship_layer < 0) return;
            bool want = CoopConfig.Active && !GameplayManager.IsMultiplayer && CoopSettings.FriendlyFire;
            if (want && !s_changed)
            {
                s_orig_ignore = Physics.GetIgnoreLayerCollision(s_proj_layer, s_ship_layer);
                Physics.IgnoreLayerCollision(s_proj_layer, s_ship_layer, false);
                s_changed = true;
                CoopLog.Write("FF", "friendly fire ON: layer " + s_proj_layer + " (player shots) now collides with " + s_ship_layer + " (ships), was ignore=" + s_orig_ignore);
            }
            else if (!want && s_changed)
            {
                Physics.IgnoreLayerCollision(s_proj_layer, s_ship_layer, s_orig_ignore);
                s_changed = false;
                CoopLog.Write("FF", "friendly fire OFF: layer collision restored");
            }
        }

        /// A shot never hits its own ship. Projectiles are pooled and reused, so first clear any ignore pairs left over from a
        /// previous owner (otherwise a recycled shot could never hit that player - the "inconsistent" friendly fire).
        public static void IgnoreOwner(Projectile proj)
        {
            if (proj == null || proj.c_collider == null) return;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.c_player_ship == null) continue;
                bool own = proj.m_owner_player != null && p == proj.m_owner_player;
                var ship = p.c_player_ship;
                if (ship.c_mesh_collider != null) Physics.IgnoreCollision(proj.c_collider, ship.c_mesh_collider, own);
                if (ship.c_level_collider != null) Physics.IgnoreCollision(proj.c_collider, ship.c_level_collider, own);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ApplyDamage")]
    static class FF1_NoTeammateDamage
    {
        /// FRIENDLY FIRE ON: teammates take half damage from each other (user request after the 19:34-20:53 run).
        public const float TeammateDamageScale = 0.5f;

        static bool Prefix(PlayerShip __instance, ref DamageInfo di)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return true;
            if (!FriendlyFireRules.IsTeammateDamage(__instance, di.owner)) return true;
            if (!CoopSettings.FriendlyFire) return false;
            di.damage *= TeammateDamageScale;
            return true;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Fire")]
    static class FF2_ProjectileFire
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(Projectile __instance)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { FriendlyFireRules.LearnLayers(__instance); FriendlyFireRules.IgnoreOwner(__instance); }
            catch (Exception ex) { CoopLog.Error("FF2", ex); }
        }
    }
}
