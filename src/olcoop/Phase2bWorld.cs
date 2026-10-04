using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.World
{
    /// <summary>
    /// Phase 2b (docs/phase2b-design.md): host-authoritative level logic.
    ///  - The level's event graph (triggers, switches, pickups, robot deaths, destroyables -> "ActivateScriptLink" -> Script*) runs on
    ///    the host only. Every script activation on the host is sent to joiners by a stable id; joiners run only the client-visible
    ///    scripts (doors, comm/objective messages, music, forcefields, lights, objects) and never the robot/level-flow ones.
    ///  - Destroyables (e.g. shoot-to-open buttons) are damaged and destroyed only on the host; joiners replay the destruction.
    ///  - Security keys are a team resource: with co-op netcode on, only the host runs real pickups (AddKey on the host's copy of the
    ///    player), so the host sends the team's best key level to everyone.
    /// If the joiner's world registry hash doesn't match the host's, the joiner falls back to running its own level logic (pre-0.4).
    /// </summary>
    public static class WNet
    {
        public const short Script = 177;     // H->J ushort script id (reliable)
        public const short Destroy = 178;    // H->J ushort destroyable id (reliable)
        public const short Keys = 180;       // H->J int team unlock level (reliable)
        public const short Hit = 179;        // J->H ushort destroyable id + float damage + int damage type (joiner's local hit)
        public const short Ready = 182;      // J->H uint world hash (after the joiner built its registry)
        public const short Manifest = 183;   // H->J uint host world hash + catch-up (destroyed ids, state-script history)
    }

    public class IdMsg : MessageBase
    {
        public ushort id;
        public override void Serialize(NetworkWriter w) { w.Write(id); }
        public override void Deserialize(NetworkReader r) { id = r.ReadUInt16(); }
    }

    public class HitMsg : MessageBase
    {
        public ushort id; public float damage; public int type; public Vector3 pos;
        public override void Serialize(NetworkWriter w) { w.Write(id); w.Write(damage); w.Write(type); w.Write(pos); }
        public override void Deserialize(NetworkReader r) { id = r.ReadUInt16(); damage = r.ReadSingle(); type = r.ReadInt32(); pos = r.ReadVector3(); }
    }

    public class WManifestMsg : MessageBase
    {
        public uint hash; public ushort[] destroyed = new ushort[0]; public ushort[] scripts = new ushort[0]; public int keys;
        public override void Serialize(NetworkWriter w)
        {
            w.Write(hash); w.Write(keys);
            w.Write((ushort)destroyed.Length); foreach (var d in destroyed) w.Write(d);
            w.Write((ushort)scripts.Length); foreach (var s in scripts) w.Write(s);
        }
        public override void Deserialize(NetworkReader r)
        {
            hash = r.ReadUInt32(); keys = r.ReadInt32();
            destroyed = new ushort[r.ReadUInt16()]; for (int i = 0; i < destroyed.Length; i++) destroyed[i] = r.ReadUInt16();
            scripts = new ushort[r.ReadUInt16()]; for (int i = 0; i < scripts.Length; i++) scripts[i] = r.ReadUInt16();
        }
    }

    public static class CoopWorld
    {
        public static bool Active { get { CoopConfig.EnsureInit(); return CoopConfig.Active && !GameplayManager.IsMultiplayer; } }
        public static bool IsHost { get { return Active && CoopConfig.IsHost && Server.IsActive(); } }
        public static bool IsJoiner { get { return Active && CoopConfig.IsJoiner && !Server.IsActive(); } }

        /// Joiner: host confirmed the same world registry; local level logic is suppressed and host events are applied.
        public static bool Matched;
        /// Joiner: currently applying a host event (lets the suppressed entry points run).
        public static bool Injecting;

        static readonly Dictionary<ushort, ScriptBase> s_scripts = new Dictionary<ushort, ScriptBase>();
        static readonly Dictionary<int, ushort> s_script_id = new Dictionary<int, ushort>();
        static readonly Dictionary<ushort, Destroyable> s_destroy = new Dictionary<ushort, Destroyable>();
        static readonly Dictionary<int, ushort> s_destroy_id = new Dictionary<int, ushort>();
        public static uint Hash;

        // host
        static readonly List<ushort> s_history = new List<ushort>();      // state scripts, in activation order (catch-up)
        static readonly HashSet<ushort> s_destroyed = new HashSet<ushort>();
        static int s_team_keys = -1;
        static float s_next_keys;
        static int s_suppressed;

        /// Scripts that drive robots, counters, saves or level flow: host only, never replayed on joiners.
        static readonly HashSet<string> HostOnly = new HashSet<string> {
            "ScriptActivateMatcen", "ScriptRobotAttack", "ScriptLockdownRobot", "ScriptLockdownMaster",
            "ScriptRevealRobot", "ScriptOnCount", "ScriptOnDestroy", "ScriptOnPickup", "ScriptOnRobotKills", "ScriptCheckpointSave",
            "ScriptSecretLevel", "ScriptTeleportOut", "ScriptAnalyticsChokepoint", "ScriptLevel1", "ScriptLevel12", "ScriptLevel16" };
        /// Scripts that only make sound/text: played live, not replayed to a late joiner.
        static readonly HashSet<string> LiveOnly = new HashSet<string> {
            "ScriptCommMessage", "ScriptObjectiveMessage", "ScriptTutorialMessage", "ScriptFadeMusic", "ScriptHologuidePosition" };

        public static bool IsHostOnly(ScriptBase s) { return HostOnly.Contains(s.GetType().Name); }

        static string HierPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name + "#" + t.GetSiblingIndex()); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static bool InScene(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }

        public static void ResetForLevel()
        {
            s_scripts.Clear(); s_script_id.Clear(); s_destroy.Clear(); s_destroy_id.Clear();
            s_history.Clear(); s_destroyed.Clear(); s_team_keys = -1; s_suppressed = 0; Matched = false; Injecting = false; Hash = 0;
        }

        public static void BuildRegistry()
        {
            ResetForLevel();
            var keys = new List<KeyValuePair<string, Component>>();
            foreach (var s in Resources.FindObjectsOfTypeAll<ScriptBase>()) if (InScene(s)) keys.Add(new KeyValuePair<string, Component>("S|" + HierPath(s.transform) + "|" + s.GetType().Name, s));
            foreach (var d in Resources.FindObjectsOfTypeAll<Destroyable>()) if (InScene(d)) keys.Add(new KeyValuePair<string, Component>("D|" + HierPath(d.transform), d));
            keys.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            uint h = 2166136261; ushort ns = 0, nd = 0;
            for (int i = 0; i < keys.Count && i < 0xFFFF; i++)
            {
                foreach (char c in keys[i].Key) { h ^= c; h *= 16777619; }
                var sb = keys[i].Value as ScriptBase;
                if (sb != null) { s_scripts[(ushort)i] = sb; s_script_id[sb.GetInstanceID()] = (ushort)i; ns++; }
                else { var d = (Destroyable)keys[i].Value; s_destroy[(ushort)i] = d; s_destroy_id[d.GetInstanceID()] = (ushort)i; nd++; }
            }
            Hash = h;
            CoopLog.Write("WORLD", "registry scripts=" + ns + " destroyables=" + nd + " hash=" + Hash.ToString("X8") + " role=" + (CoopConfig.IsHost ? "host" : "joiner"));
        }

        public static bool TryScriptId(ScriptBase s, out ushort id) { id = 0; return s != null && s_script_id.TryGetValue(s.GetInstanceID(), out id); }
        public static bool TryDestroyId(Destroyable d, out ushort id) { id = 0; return d != null && s_destroy_id.TryGetValue(d.GetInstanceID(), out id); }

        static IEnumerable<NetworkConnection> Joiners()
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) yield return c;
        }
        static void SendAll(short type, MessageBase m) { foreach (var c in Joiners()) c.Send(type, m); }

        static string Describe(ScriptBase s) { return s.GetType().Name + " '" + s.gameObject.name + "'"; }

        // ---------------------------------------------------------------- host
        public static void HostScriptActivated(ScriptBase s)
        {
            ushort id;
            if (!TryScriptId(s, out id)) { CoopLog.Write("WORLD", "host: unregistered script activated " + Describe(s)); return; }
            if (!IsHostOnly(s) && !LiveOnly.Contains(s.GetType().Name)) s_history.Add(id);
            SendAll(WNet.Script, new IdMsg { id = id });
            CoopLog.Write("WORLD", "host: script " + id + " " + Describe(s) + (IsHostOnly(s) ? " (host-only)" : ""));
            string tn = s.GetType().Name;
            if (tn == "ScriptLockdownMaster" || tn == "ScriptLockdownBoss") { try { CoopFlow.HostLockdown(s); } catch (Exception ex) { CoopLog.Error("HostLockdown", ex); } }
        }

        public static void HostDestroyed(Destroyable d)
        {
            ushort id;
            if (!TryDestroyId(d, out id) || !s_destroyed.Add(id)) return;
            SendAll(WNet.Destroy, new IdMsg { id = id });
            CoopLog.Write("WORLD", "host: destroyable " + id + " '" + d.gameObject.name + "' destroyed");
        }

        public static int TeamKeys()
        {
            int best = 0;
            foreach (var p in Overload.NetworkManager.m_Players) if (p != null) best = Math.Max(best, (int)p.m_unlock_level);
            if (GameManager.m_local_player != null) best = Math.Max(best, (int)GameManager.m_local_player.m_unlock_level);
            return best;
        }

        public static void HostTick()
        {
            if (!IsHost || !GameplayManager.LevelIsLoaded || Time.time < s_next_keys) return;
            s_next_keys = Time.time + 0.25f;
            int k = TeamKeys();
            if (k == s_team_keys) return;
            bool first = s_team_keys < 0;
            s_team_keys = k;
            ApplyKeys(k, false);
            if (!first) CoopLog.Write("WORLD", "host: team security level now " + k);
            SendAll(WNet.Keys, new IntegerMessage(k));
        }

        public static void OnReadyRaw(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<IdHashMsg>();
                bool ok = m.hash == Hash;
                CoopLog.Write("WORLD", "host: joiner conn " + msg.conn.connectionId + " world hash " + m.hash.ToString("X8") + (ok ? " matches" : " MISMATCH (host " + Hash.ToString("X8") + "); joiner keeps its own level logic") +
                    "; catch-up destroyed=" + s_destroyed.Count + " scripts=" + s_history.Count);
                msg.conn.Send(WNet.Manifest, new WManifestMsg { hash = Hash, destroyed = s_destroyed.ToArray(), scripts = s_history.ToArray(), keys = Math.Max(0, s_team_keys) });
            }
            catch (Exception ex) { CoopLog.Error("CoopWorld.OnReady", ex); }
        }

        /// Host: apply a joiner's destroyable hit, credited to that joiner's ship.
        public static void OnHit(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<HitMsg>();
                Destroyable d;
                if (!s_destroy.TryGetValue(m.id, out d) || d == null || (bool)d.m_dying) return;
                GameObject owner = null;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && p.connectionToClient != null && p.connectionToClient.connectionId == msg.conn.connectionId && p.c_player_ship != null) owner = p.c_player_ship.gameObject;
                var di = new DamageInfo { damage = m.damage, type = (DamageType)m.type, pos = m.pos, owner = owner };
                s_applying_remote_hit = true;
                try { d.ApplyDamage(di); } finally { s_applying_remote_hit = false; }
                CoopLog.Write("WORLD", "host: joiner conn " + msg.conn.connectionId + " hit destroyable " + m.id + " '" + d.gameObject.name + "' dmg=" + m.damage.ToString("F1") + (owner == null ? " (no ship found)" : ""));
            }
            catch (Exception ex) { CoopLog.Error("CoopWorld.OnHit", ex); }
        }
        static bool s_applying_remote_hit;
        static int s_host_hits_logged;

        /// Host diagnostic: who hits destroyables here (tells us whether joiner shots reach destroyables on the host by themselves).
        public static void HostNoteHit(Destroyable d, DamageInfo di)
        {
            if (s_applying_remote_hit || s_host_hits_logged++ >= 30) return;
            ushort id; TryDestroyId(d, out id);
            var ps = di.owner != null ? (di.owner.GetComponent<PlayerShip>() ?? di.owner.GetComponentInParent<PlayerShip>()) : null;
            CoopLog.Write("WORLD", "host: destroyable " + id + " '" + d.gameObject.name + "' hit dmg=" + di.damage.ToString("F1") + " type=" + di.type +
                " by " + (ps != null ? "netId=" + ps.c_player.netId.Value + (ps.isLocalPlayer ? " (host)" : " (joiner ship on host)") : (di.owner != null ? di.owner.name : "?")));
        }

        // ---------------------------------------------------------------- shared
        public static void ApplyKeys(int k, bool announce)
        {
            int before = GameManager.m_local_player != null ? (int)GameManager.m_local_player.m_unlock_level : 0;
            foreach (var p in Overload.NetworkManager.m_Players) if (p != null && (int)p.m_unlock_level < k) p.m_unlock_level = (DoorLock)k;
            if (GameManager.m_local_player != null && (int)GameManager.m_local_player.m_unlock_level < k) GameManager.m_local_player.m_unlock_level = (DoorLock)k;
            try { if (GameplayManager.m_gm != null && GameplayManager.m_gm.m_security_manager != null) GameplayManager.m_gm.m_security_manager.UpdateSecurityLevel(); } catch (Exception ex) { CoopLog.Error("UpdateSecurityLevel", ex); }
            foreach (var d in UnityEngine.Object.FindObjectsOfType<DoorAnimating>()) { try { d.UpdateLock(); } catch { } }
            if (announce && k > before)
            {
                GameplayManager.InfoPopup(Loc.LS("SECURITY ACCESS GRANTED!"), Loc.LS("SECURITY KEY ACQUIRED!"), 8f);
                PlayKeySound();
            }
        }

        static readonly MethodInfo m_sfx2d = AccessTools.Method(typeof(SFXCueManager), "PlayRawSoundEffect2D");
        /// Same cue Player.AddKey plays (369) - AddKey only runs on the host in co-op.
        static void PlayKeySound()
        {
            try
            {
                if (m_sfx2d == null) return;
                var ps = m_sfx2d.GetParameters();
                object cue = ps[0].ParameterType.IsEnum ? Enum.ToObject(ps[0].ParameterType, 369) : (object)369;
                m_sfx2d.Invoke(null, new object[] { cue, 1f, 0f, 0.1f, false });
            }
            catch (Exception ex) { CoopLog.Error("PlayKeySound", ex); }
        }

        // ---------------------------------------------------------------- joiner
        /// Joiner: our shot hit a destroyable on our screen. The host is authoritative, so send it the hit.
        public static void SendHit(Destroyable d, DamageInfo di)
        {
            ushort id;
            var c = Client.GetClient();
            if (c == null || !TryDestroyId(d, out id) || (bool)d.m_dying) return;
            c.Send(WNet.Hit, new HitMsg { id = id, damage = di.damage, type = (int)di.type, pos = di.pos });
            if (s_hits_logged++ < 20) CoopLog.Write("WORLD", "joiner: hit destroyable " + id + " '" + d.gameObject.name + "' dmg=" + di.damage.ToString("F1") + " -> host");
        }
        static int s_hits_logged;

        public static void SendReady()
        {
            var c = Client.GetClient();
            if (c == null || !Client.IsConnected()) return;
            c.Send(WNet.Ready, new IdHashMsg { hash = Hash });
            CoopLog.Write("WORLD", "joiner: registry ready sent hash=" + Hash.ToString("X8"));
        }

        public static void OnManifest(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<WManifestMsg>();
                Matched = m.hash == Hash && Hash != 0;
                CoopLog.Write("WORLD", "joiner: host world hash " + m.hash.ToString("X8") + (Matched ? " matches; host now runs the level logic" : " MISMATCH (ours " + Hash.ToString("X8") + "); keeping local level logic") +
                    " catch-up destroyed=" + m.destroyed.Length + " scripts=" + m.scripts.Length + " keys=" + m.keys);
                if (!Matched) return;
                foreach (var d in m.destroyed) ApplyDestroy(d, true);
                foreach (var s in m.scripts) ApplyScript(s, true);
                ApplyKeys(m.keys, false);
            }
            catch (Exception ex) { CoopLog.Error("CoopWorld.OnManifest", ex); }
        }

        public static void ApplyScript(ushort id, bool catchUp)
        {
            ScriptBase s;
            if (!s_scripts.TryGetValue(id, out s) || s == null) { CoopLog.Write("WORLD", "joiner: unknown script id " + id); return; }
            if (IsHostOnly(s)) { CoopLog.Write("WORLD", "joiner: script " + id + " " + Describe(s) + " is host-only; not run here"); return; }
            Injecting = true;
            try
            {
                // Call this component's own entry point (SendMessage would hit every script on the GameObject).
                var m = AccessTools.Method(s.GetType(), "ActivateScriptLink", Type.EmptyTypes);
                if (m != null) m.Invoke(s, null); else CoopLog.Write("WORLD", "joiner: " + Describe(s) + " has no ActivateScriptLink");
            }
            catch (Exception ex) { CoopLog.Error("ApplyScript " + id, ex); }
            finally { Injecting = false; }
            CoopLog.Write("WORLD", "joiner: ran script " + id + " " + Describe(s) + (catchUp ? " (catch-up)" : ""));
        }

        public static void ApplyDestroy(ushort id, bool catchUp)
        {
            Destroyable d;
            if (!s_destroy.TryGetValue(id, out d) || d == null) { CoopLog.Write("WORLD", "joiner: unknown destroyable id " + id); return; }
            if ((bool)d.m_dying) return;
            Injecting = true;
            try { d.StartExploding(); }
            catch (Exception ex) { CoopLog.Error("ApplyDestroy " + id, ex); }
            finally { Injecting = false; }
            CoopLog.Write("WORLD", "joiner: destroyed " + id + " '" + d.gameObject.name + "'" + (catchUp ? " (catch-up)" : ""));
        }

        public static void NoteSuppressed(string what)
        {
            if (s_suppressed++ < 40) CoopLog.Write("WORLD", "joiner: suppressed local " + what + " (host decides)");
        }
    }

    public class IdHashMsg : MessageBase
    {
        public uint hash;
        public override void Serialize(NetworkWriter w) { w.Write(hash); }
        public override void Deserialize(NetworkReader r) { hash = r.ReadUInt32(); }
    }

    // ================================================================= patches

    /// W0: build the registry with the robots' (same point in level start on both peers).
    [HarmonyPatch(typeof(RobotManager), "InitializeForNewLevel")]
    static class W0_Registry
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix()
        {
            if (!CoopWorld.Active) return;
            try { CoopWorld.BuildRegistry(); if (CoopWorld.IsJoiner) CoopWorld.SendReady(); }
            catch (Exception ex) { CoopLog.Error("W0", ex); }
        }
    }

    /// W1: every Script*.ActivateScriptLink. Host: broadcast. Joiner (matched): run only host-sent activations.
    [HarmonyPatch]
    static class W1_ScriptLink
    {
        static readonly string[] ScriptTypes = { "ScriptActivateAlienWarp","ScriptActivateMatcen","ScriptActivateObject","ScriptAlienDoorLink","ScriptAnalyticsChokepoint","ScriptCheckpointSave","ScriptCommMessage","ScriptDeactivateObject","ScriptDisableForcefield","ScriptDisableOcclusion","ScriptDisableShield","ScriptDoorLock","ScriptDoorOpen","ScriptDoorUnlock","ScriptFadeMusic","ScriptLevel1","ScriptLevel12","ScriptLockdownBoss","ScriptLockdownDoor","ScriptLockdownLight","ScriptLockdownMaster","ScriptLockdownRobot","ScriptObjectiveMessage","ScriptOnCount","ScriptOnDestroy","ScriptOnPickup","ScriptOnRobotKills","ScriptRevealRobot","ScriptRobotAttack","ScriptSecretLevel","ScriptTeleportOut","ScriptTutorialMessage" };

        static IEnumerable<MethodBase> TargetMethods()
        {
            // Every Script type that declares ActivateScriptLink (from the game assembly, Overload 1.1.1886).
            foreach (var n in ScriptTypes)
            {
                var t = AccessTools.TypeByName(n);
                if (t == null) { CoopLog.Write("WORLD", "script type " + n + " not found"); continue; }
                var m = t.GetMethod("ActivateScriptLink", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (m != null) yield return m;
            }
        }

        static bool Prefix(ScriptBase __instance)
        {
            if (!CoopWorld.IsJoiner || !CoopWorld.Matched) return true;
            if (CoopWorld.Injecting) return true;
            CoopWorld.NoteSuppressed(__instance.GetType().Name + " '" + __instance.gameObject.name + "'");
            return false;
        }

        static void Postfix(ScriptBase __instance)
        {
            if (!CoopWorld.IsHost) return;
            try { CoopWorld.HostScriptActivated(__instance); } catch (Exception ex) { CoopLog.Error("W1", ex); }
        }
    }

    /// W2: destroyables take damage only on the host.
    [HarmonyPatch(typeof(Destroyable), "ApplyDamage")]
    static class W2_DestroyableDamage
    {
        static bool Prefix(Destroyable __instance, DamageInfo di)
        {
            if (CoopWorld.IsHost) { try { CoopWorld.HostNoteHit(__instance, di); } catch { } return true; }
            if (!(CoopWorld.IsJoiner && CoopWorld.Matched)) return true;
            try
            {
                var me = GameManager.m_player_ship;
                bool mine = me != null && di.owner != null && (di.owner == me.gameObject || di.owner.transform.IsChildOf(me.transform));
                if (mine) CoopWorld.SendHit(__instance, di); // other players' shots are applied on the host already
            }
            catch (Exception ex) { CoopLog.Error("W2", ex); }
            return false;
        }
    }

    /// W3: destroyable destruction. Host broadcasts; joiner only destroys on the host's word.
    [HarmonyPatch(typeof(Destroyable), "StartExploding")]
    static class W3_DestroyableExplode
    {
        static bool Prefix(Destroyable __instance)
        {
            if (CoopWorld.IsJoiner && CoopWorld.Matched && !CoopWorld.Injecting) { CoopWorld.NoteSuppressed("destroyable '" + __instance.gameObject.name + "'"); return false; }
            return true;
        }
        static void Postfix(Destroyable __instance)
        {
            if (!CoopWorld.IsHost) return;
            try { CoopWorld.HostDestroyed(__instance); } catch (Exception ex) { CoopLog.Error("W3", ex); }
        }
    }

    /// W4: team keys (host), every frame.
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class W4_Tick
    {
        static void Postfix()
        {
            if (!CoopWorld.IsHost) return;
            try { CoopWorld.HostTick(); } catch (Exception ex) { CoopLog.Error("W4", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class W5_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(WNet.Ready, CoopWorld.OnReadyRaw);
            NetworkServer.RegisterHandler(WNet.Hit, CoopWorld.OnHit);
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class W6_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            var c = Client.GetClient();
            c.RegisterHandler(WNet.Manifest, CoopWorld.OnManifest);
            c.RegisterHandler(WNet.Script, m => { if (CoopWorld.Matched) CoopWorld.ApplyScript(m.ReadMessage<IdMsg>().id, false); });
            c.RegisterHandler(WNet.Destroy, m => { if (CoopWorld.Matched) CoopWorld.ApplyDestroy(m.ReadMessage<IdMsg>().id, false); });
            c.RegisterHandler(WNet.Keys, m => { int k = m.ReadMessage<IntegerMessage>().value; CoopLog.Write("WORLD", "joiner: team security level " + k); CoopWorld.ApplyKeys(k, true); });
        }
    }

    /// W7: player shots to the other players. Stock ProjectileManager.FireProjectile only calls Server.SendProjectileFiredToClients when
    /// GameplayManager.IsMultiplayer (game type MULTIPLAYER); co-op is a campaign game, so joiners never saw the host's shots (and with 3
    /// players, joiners wouldn't see each other's). The host now sends every player shot (msg 70, as stock MP/olmod "sniper packets") to
    /// everyone except the shooter, who already shows its own. Robot shots are separate (Phase 2a FireBatch).
    [HarmonyPatch(typeof(ProjectileManager), "FireProjectile")]
    static class W7_ShareShots
    {
        static ConstructorInfo s_ctor;
        static int s_logged;

        static void Postfix(object[] __args)
        {
            if (!CoopWorld.IsHost) return;
            try
            {
                var owner = __args[3] as GameObject;
                if (owner == null) return;
                var shooter = owner.GetComponent<Player>();
                if (shooter == null) return; // robots, turrets etc.
                if (s_ctor == null) s_ctor = typeof(FireProjectileToClientMessage).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 7);
                if (s_ctor == null) return;
                var msg = (MessageBase)s_ctor.Invoke(new object[] { shooter.netId, __args[0], __args[1], __args[2], __args[6], __args[7], -1 });
                int sent = 0;
                foreach (var p in Overload.NetworkManager.m_Players)
                {
                    if (p == null || p.isLocalPlayer || p.m_spectator || p == shooter || p.connectionToClient == null) continue;
                    if (!Session.CoopHost.Verified.Contains(p.connectionToClient.connectionId)) continue;
                    p.connectionToClient.SendByChannel(70, msg, 2);
                    sent++;
                }
                if (sent > 0 && s_logged++ < 10) CoopLog.Write("WORLD", "host: shared shot " + __args[0] + " from netId=" + shooter.netId.Value + " with " + sent + " player(s)");
            }
            catch (Exception ex) { if (s_logged++ < 10) CoopLog.Error("W7", ex); }
        }
    }
}
