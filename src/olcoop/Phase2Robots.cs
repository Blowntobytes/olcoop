using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.Robots
{
    /// <summary>
    /// Phase 2a (docs/phase2a-design.md): host-authoritative robots.
    /// Host: runs robot AI, targets the nearest player, streams robot state/spawns/deaths/fire to joiners.
    /// Joiner: robots are kinematic puppets driven by host state; local AI, damage, deaths, drops and matcen spawns are off.
    /// </summary>
    public static class RNet
    {
        public const short LevelInfo = 171;     // H->J int difficulty (sent before SceneLoad)
        public const short Manifest = 162;      // H->J reply to RegistryReady
        public const short RegistryReady = 163; // J->H joiner built its registry
        public const short StateBatch = 165;    // H->J unreliable-sequenced
        public const short Spawn = 166;         // H->J reliable
        public const short Death = 167;         // H->J reliable
        public const short Explode = 168;       // H->J reliable
        public const short FireBatch = 169;     // H->J unreliable

        public const int ChReliable = 0, ChUnrelSeq = 1, ChUnrel = 2;
    }

    // ----------------------------------------------------------------- messages
    public class RegistryReadyMsg : MessageBase
    {
        public uint hash; public ushort count;
        public override void Serialize(NetworkWriter w) { w.Write(hash); w.Write(count); }
        public override void Deserialize(NetworkReader r) { hash = r.ReadUInt32(); count = r.ReadUInt16(); }
    }

    public class ManifestMsg : MessageBase
    {
        public uint hash; public ushort count; public byte[] alive = new byte[0];
        public override void Serialize(NetworkWriter w) { w.Write(hash); w.Write(count); w.WriteBytesAndSize(alive, alive.Length); }
        public override void Deserialize(NetworkReader r) { hash = r.ReadUInt32(); count = r.ReadUInt16(); alive = r.ReadBytesAndSize(); }
    }

    public struct RState
    {
        public ushort id; public byte flags, mode, sub, dmgFlash; public ushort hp;
        public Vector3 pos, vel; public Quaternion rot;
        public const byte F_ACTIVE = 1, F_CLOAK = 2, F_HEADLIGHT = 4, F_STASIS = 8, F_REVEALED = 16;
    }

    public class StateBatchMsg : MessageBase
    {
        public float hostTime; public List<RState> e = new List<RState>();
        public override void Serialize(NetworkWriter w)
        {
            w.Write(hostTime); w.Write((byte)e.Count);
            foreach (var s in e)
            {
                w.Write(s.id); w.Write(s.flags); w.Write(s.mode); w.Write(s.sub);
                w.Write(s.pos); w.Write(s.rot);
                w.Write((short)Mathf.Clamp(s.vel.x * 100f, -32767, 32767)); w.Write((short)Mathf.Clamp(s.vel.y * 100f, -32767, 32767)); w.Write((short)Mathf.Clamp(s.vel.z * 100f, -32767, 32767));
                w.Write(s.hp); w.Write(s.dmgFlash);
            }
        }
        public override void Deserialize(NetworkReader r)
        {
            hostTime = r.ReadSingle(); int n = r.ReadByte(); e.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = new RState();
                s.id = r.ReadUInt16(); s.flags = r.ReadByte(); s.mode = r.ReadByte(); s.sub = r.ReadByte();
                s.pos = r.ReadVector3(); s.rot = r.ReadQuaternion();
                s.vel = new Vector3(r.ReadInt16() / 100f, r.ReadInt16() / 100f, r.ReadInt16() / 100f);
                s.hp = r.ReadUInt16(); s.dmgFlash = r.ReadByte();
                e.Add(s);
            }
        }
    }

    public class SpawnMsg : MessageBase
    {
        public ushort id; public int etype; public bool super_, variant, hidden; public Vector3 pos; public Quaternion rot;
        public override void Serialize(NetworkWriter w) { w.Write(id); w.Write(etype); w.Write(super_); w.Write(variant); w.Write(hidden); w.Write(pos); w.Write(rot); }
        public override void Deserialize(NetworkReader r) { id = r.ReadUInt16(); etype = r.ReadInt32(); super_ = r.ReadBoolean(); variant = r.ReadBoolean(); hidden = r.ReadBoolean(); pos = r.ReadVector3(); rot = r.ReadQuaternion(); }
    }

    public class DeathMsg : MessageBase
    {
        public ushort id; public int explosionType; public bool instant, exploded; public uint killer; public int weapon; public Vector3 pos, vel;
        public override void Serialize(NetworkWriter w) { w.Write(id); w.Write(explosionType); w.Write(instant); w.Write(exploded); w.Write(killer); w.Write(weapon); w.Write(pos); w.Write(vel); }
        public override void Deserialize(NetworkReader r) { id = r.ReadUInt16(); explosionType = r.ReadInt32(); instant = r.ReadBoolean(); exploded = r.ReadBoolean(); killer = r.ReadUInt32(); weapon = r.ReadInt32(); pos = r.ReadVector3(); vel = r.ReadVector3(); }
    }

    public class ExplodeMsg : MessageBase
    {
        public ushort id; public Vector3 pos;
        public override void Serialize(NetworkWriter w) { w.Write(id); w.Write(pos); }
        public override void Deserialize(NetworkReader r) { id = r.ReadUInt16(); pos = r.ReadVector3(); }
    }

    public struct RFire { public ushort id; public int proj, team, upgrade; public float strength; public bool savePos; public Vector3 pos; public Quaternion rot; }

    public class FireBatchMsg : MessageBase
    {
        public List<RFire> e = new List<RFire>();
        public override void Serialize(NetworkWriter w)
        {
            w.Write((byte)e.Count);
            foreach (var f in e) { w.Write(f.id); w.Write((byte)f.proj); w.Write((byte)f.team); w.Write((byte)f.upgrade); w.Write(f.strength); w.Write(f.savePos); w.Write(f.pos); w.Write(f.rot); }
        }
        public override void Deserialize(NetworkReader r)
        {
            int n = r.ReadByte(); e.Clear();
            for (int i = 0; i < n; i++)
            {
                var f = new RFire();
                f.id = r.ReadUInt16(); f.proj = r.ReadByte(); f.team = r.ReadByte(); f.upgrade = r.ReadByte(); f.strength = r.ReadSingle(); f.savePos = r.ReadBoolean(); f.pos = r.ReadVector3(); f.rot = r.ReadQuaternion();
                e.Add(f);
            }
        }
    }

    // ----------------------------------------------------------------- registry + shared state
    public class Puppet
    {
        public float t0 = -1f, t1 = -1f; public Vector3 p0, p1, v1; public Quaternion r0, r1;
        public byte lastMode = 255;
    }

    public static class CoopRobots
    {
        public static readonly Dictionary<ushort, Robot> ById = new Dictionary<ushort, Robot>();
        public static readonly Dictionary<int, ushort> IdOf = new Dictionary<int, ushort>(); // key: Robot.GetInstanceID()
        public static readonly Dictionary<ushort, Puppet> Puppets = new Dictionary<ushort, Puppet>();
        public static readonly HashSet<ushort> DeathSent = new HashSet<ushort>();
        public static readonly HashSet<int> LocallyDying = new HashSet<int>();
        public static ushort PlacedCount;
        public static ushort NextDynamic = 0x8000;
        public static uint Hash;
        public static bool ApplyingRemoteSpawn, ApplyingRemoteFire, InJoinerExplode;
        public static int HostDifficulty = -1;

        // counters (RSYNC log every 5 s)
        public static int TxEntries, TxMsgs, TxBytes, RxEntries, RxUnknown, RxFire, TxFire, SuppressedSpews, Retargets, HostRobotHits, HostRobotFires;
        static float s_next_report;

        static readonly FieldInfo f_explosion_type = AccessTools.Field(typeof(Robot), "m_explosion_type");
        static readonly FieldInfo f_instant = AccessTools.Field(typeof(Robot), "m_instant_explode");
        static readonly FieldInfo f_hide_frames = AccessTools.Field(typeof(Robot), "m_hide_frames");
        static readonly FieldInfo f_spawn_effect_on = AccessTools.Field(typeof(Robot), "m_spawn_effect_on");
        static readonly MethodInfo m_start_exploding = AccessTools.Method(typeof(Robot), "StartExploding");
        static readonly MethodInfo m_draw_spawn = AccessTools.Method(typeof(Robot), "DrawSpawnMesh");

        public static bool Active { get { CoopConfig.EnsureInit(); return CoopConfig.Active && !GameplayManager.IsMultiplayer; } }
        public static bool IsHost { get { return Active && CoopConfig.IsHost && Server.IsActive(); } }
        public static bool IsJoiner { get { return Active && CoopConfig.IsJoiner && !Server.IsActive(); } }

        public static bool TryId(Robot r, out ushort id) { id = 0; return r != null && IdOf.TryGetValue(r.GetInstanceID(), out id); }
        public static bool IsPuppet(Robot r) { ushort id; return IsJoiner && !r.m_is_guide_bot && TryId(r, out id); }

        public static void Register(Robot r, ushort id)
        {
            ById[id] = r; IdOf[r.GetInstanceID()] = id;
            if (CoopConfig.IsJoiner && r.c_rigidbody != null) { r.c_rigidbody.isKinematic = true; r.c_rigidbody.interpolation = RigidbodyInterpolation.None; }
        }

        static string HierPath(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name + "#" + t.GetSiblingIndex()); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        public static void BuildRegistry()
        {
            ById.Clear(); IdOf.Clear(); Puppets.Clear(); DeathSent.Clear(); LocallyDying.Clear();
            NextDynamic = 0x8000;
            var list = new List<KeyValuePair<string, Robot>>();
            foreach (var r in Resources.FindObjectsOfTypeAll<Robot>())
            {
                if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.scene.isLoaded) continue;
                if (r.m_is_guide_bot || r.robot_type == EnemyType.GUIDEBOT) continue;
                list.Add(new KeyValuePair<string, Robot>(HierPath(r.transform) + "|" + (int)r.robot_type, r));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            uint h = 2166136261;
            for (int i = 0; i < list.Count; i++)
            {
                Register(list[i].Value, (ushort)i);
                foreach (char c in list[i].Key) { h ^= c; h *= 16777619; }
            }
            Hash = h; PlacedCount = (ushort)list.Count;
            int inactive = 0; foreach (var kv in list) if (!kv.Value.gameObject.activeInHierarchy) inactive++;
            CoopLog.Write("RSYNC", "registry n=" + list.Count + " hash=" + Hash.ToString("X8") + " inactiveAtInit=" + inactive +
                " role=" + (CoopConfig.IsHost ? "host" : "joiner") + (list.Count > 0 ? " first='" + list[0].Key + "'" : ""));
        }

        public static void Report()
        {
            float now = Time.realtimeSinceStartup;
            if (now < s_next_report) return;
            s_next_report = now + 5f;
            if (IsHost)
                CoopLog.Write("RSYNC", "host tx entries=" + TxEntries + " msgs=" + TxMsgs + " bytes/s=" + (TxBytes / 5) + " fire=" + TxFire + " robotShots=" + HostRobotFires + " robotHitsTaken=" + HostRobotHits + " retargets=" + Retargets + " registered=" + ById.Count);
            else if (IsJoiner)
                CoopLog.Write("RSYNC", "joiner rx entries=" + RxEntries + " unknownId=" + RxUnknown + " fire=" + RxFire + " suppressedSpews=" + SuppressedSpews + " registered=" + ById.Count + " dying=" + LocallyDying.Count);
            TxEntries = TxMsgs = TxBytes = RxEntries = RxUnknown = RxFire = TxFire = SuppressedSpews = Retargets = HostRobotHits = HostRobotFires = 0;
        }

        // ---------- joiner-side helpers
        public static void CosmeticTick(Robot r)
        {
            int hf = (int)f_hide_frames.GetValue(r);
            if (hf > 0) { hf--; f_hide_frames.SetValue(r, hf); if (hf == 0) r.UnhideMesh(); }
            if ((bool)f_spawn_effect_on.GetValue(r)) m_draw_spawn.Invoke(r, null);
            if (GameManager.m_player_ship != null) r.UpdateTargetInfo();
            r.UpdateMaterialGlows();
        }

        public static void ApplyDeath(DeathMsg m)
        {
            Robot r;
            if (!ById.TryGetValue(m.id, out r) || r == null) { CoopLog.Write("RSYNC", "death for unknown id " + m.id); return; }
            if (!r.alive || r.m_dying) return;
            bool visible = r.gameObject.activeInHierarchy;
            CoopLog.Write("RSYNC", "apply death id=" + m.id + " type=" + r.robot_type + " visible=" + visible + " killer=" + m.killer);
            if (!visible)
            {
                RobotManager.m_master_robot_list.Remove(r);
                r.alive = false;
                UnityEngine.Object.Destroy(r.gameObject);
                return;
            }
            r.m_hp = -1f;
            r.AI_mode = AIModeType.DYING;
            f_instant.SetValue(r, m.instant);
            if (r.c_rigidbody != null) { r.c_rigidbody.isKinematic = false; r.c_rigidbody.velocity = m.vel; }
            LocallyDying.Add(r.GetInstanceID());
            var di = new DamageInfo { damage = 1f, pos = m.pos, weapon = (ProjPrefab)m.weapon };
            m_start_exploding.Invoke(r, new object[] { di });
            f_explosion_type.SetValue(r, Enum.ToObject(f_explosion_type.FieldType, m.explosionType));
            if (m.exploded && r.alive) r.ExplodeNow();
        }

        public static int GetExplosionType(Robot r) { return Convert.ToInt32(f_explosion_type.GetValue(r)); }
        public static bool GetInstant(Robot r) { return (bool)f_instant.GetValue(r); }
    }

    // ----------------------------------------------------------------- host: targeting
    public static class CoopTargets
    {
        class Choice { public PlayerShip ship; public float next; public PlayerShip lastHitBy; public float lastHitTime = -100f; }
        static readonly Dictionary<int, Choice> s_choice = new Dictionary<int, Choice>();
        static readonly FieldInfo f_player_ship = AccessTools.Field(typeof(Robot), "m_player_ship");
        static readonly FieldInfo f_target_rb = AccessTools.Field(typeof(Robot), "target_rigidbody");
        static readonly FieldInfo f_believed_loc = AccessTools.Field(typeof(Robot), "m_believed_player_location");
        static readonly FieldInfo f_last_vis_seg = AccessTools.Field(typeof(Robot), "AI_last_visible_player_segment");
        const int LOS_MASK = 67256321;
        const float RECHECK = 0.4f, HIT_MEMORY = 4f, HIT_WEIGHT = 0.45f, STICKY = 0.75f;

        struct Saved { public Transform t; public Vector3 p; public object ship; public bool valid; }
        [ThreadStatic] static Saved s_saved;

        static Choice Get(Robot r)
        {
            Choice c; int key = r.GetInstanceID();
            if (!s_choice.TryGetValue(key, out c)) { c = new Choice(); s_choice[key] = c; }
            return c;
        }

        /// Host: remember who last damaged this robot (aggro).
        public static void NoteHit(Robot r, GameObject owner)
        {
            if (r == null || owner == null) return;
            var ps = owner.GetComponent<PlayerShip>();
            if (ps == null) { var p = owner.GetComponent<Player>(); if (p != null) ps = p.c_player_ship; }
            if (ps == null) return;
            var c = Get(r);
            c.lastHitBy = ps; c.lastHitTime = Time.time;
            c.next = 0f; // re-evaluate right away
        }

        /// Threat score (lower = more interesting): distance, halved-ish for whoever shot us recently, sticky for the current target.
        static PlayerShip Choose(Robot r)
        {
            var c = Get(r);
            float now = Time.time;
            if (c.ship != null && now < c.next && Usable(c.ship)) return c.ship;
            c.next = now + RECHECK;
            Vector3 rp = r.c_transform.position;
            PlayerShip best = null, nearest = null; float bestScore = float.MaxValue, nearestD = float.MaxValue;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.c_player_ship == null || !Usable(p.c_player_ship)) continue;
                var ship = p.c_player_ship;
                float d = Vector3.Distance(ship.c_transform.position, rp);
                if (d < nearestD) { nearestD = d; nearest = ship; }
                bool visible = !Physics.Linecast(rp, ship.c_transform.position, LOS_MASK, QueryTriggerInteraction.Ignore);
                bool hitMe = c.lastHitBy == ship && now - c.lastHitTime < HIT_MEMORY;
                if (!visible && !hitMe) continue;
                float score = d;
                if (hitMe) score *= HIT_WEIGHT;
                if (ship == c.ship) score *= STICKY;
                if (score < bestScore) { bestScore = score; best = ship; }
            }
            var chosen = best ?? (c.ship != null && Usable(c.ship) ? c.ship : nearest) ?? GameManager.m_player_ship;
            if (chosen != c.ship)
            {
                bool had = c.ship != null;
                c.ship = chosen;
                if (had)
                {
                    CoopRobots.Retargets++;
                    // point the robot's memory at the new target so it turns/paths toward it
                    if (f_believed_loc != null) f_believed_loc.SetValue(r, chosen.c_transform.position);
                    if (f_last_vis_seg != null && chosen.c_moving_object != null) f_last_vis_seg.SetValue(r, chosen.c_moving_object.CurrentSegmentIndex);
                }
            }
            return chosen;
        }

        static bool Usable(PlayerShip s) { return s != null && !(bool)s.m_dying && !(bool)s.m_dead && s.gameObject.activeInHierarchy; }

        public static void Push(Robot r)
        {
            s_saved.valid = false;
            if (Overload.NetworkManager.m_Players.Count < 2) return; // solo: stock behaviour
            var target = Choose(r);
            if (target == null) return;
            // always keep the per-robot fields pointing at the chosen target (they persist between ticks)
            if (r.target_go != target.gameObject) { r.target_go = target.gameObject; f_target_rb.SetValue(r, target.c_rigidbody); }
            if (target == GameManager.m_player_ship) return;
            s_saved.t = Robot.c_target_transform; s_saved.p = Robot.c_target_transform_position; s_saved.ship = f_player_ship.GetValue(null); s_saved.valid = true;
            Robot.c_target_transform = target.c_transform;
            Robot.c_target_transform_position = target.c_transform.position;
            f_player_ship.SetValue(null, target);
        }

        public static void Pop(Robot r)
        {
            if (!s_saved.valid) return;
            Robot.c_target_transform = s_saved.t; Robot.c_target_transform_position = s_saved.p; f_player_ship.SetValue(null, s_saved.ship);
            s_saved.valid = false;
        }

        public static void Clear() { s_choice.Clear(); }
    }

    // ----------------------------------------------------------------- host: sending
    public static class RobotHostNet
    {
        static float s_next_send, s_next_key;
        static readonly List<RFire> s_fire = new List<RFire>();
        static readonly List<ushort> s_pending_spawn = new List<ushort>();
        static readonly Dictionary<ushort, RState> s_last = new Dictionary<ushort, RState>();

        static IEnumerable<NetworkConnection> Joiners()
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) yield return c;
        }

        static bool HasJoiners() { foreach (var c in Joiners()) return true; return false; }

        static void SendAll(short type, MessageBase msg, int ch)
        {
            foreach (var c in Joiners()) c.SendByChannel(type, msg, ch);
        }

        public static void QueueFire(RFire f) { if (HasJoiners()) s_fire.Add(f); }
        public static void QueueSpawn(ushort id) { s_pending_spawn.Add(id); }

        public static void SendSpawnNow(NetworkConnection only, ushort id, Robot r)
        {
            var m = new SpawnMsg { id = id, etype = (int)r.robot_type, super_ = r.m_super, variant = r.m_variant, hidden = r.m_init_hidden, pos = r.c_transform.position, rot = r.c_transform.rotation };
            if (only != null) only.SendByChannel(RNet.Spawn, m, RNet.ChReliable); else SendAll(RNet.Spawn, m, RNet.ChReliable);
            if (only == null) CoopLog.Write("RSYNC", "spawn id=0x" + id.ToString("X4") + " etype=" + r.robot_type + " pos=" + m.pos.ToString("F1"));
        }

        public static void SendDeath(Robot r, ushort id, uint killer, int weapon, bool exploded)
        {
            if (!CoopRobots.DeathSent.Add(id)) return;
            var m = new DeathMsg { id = id, explosionType = CoopRobots.GetExplosionType(r), instant = CoopRobots.GetInstant(r), exploded = exploded, killer = killer, weapon = weapon, pos = r.c_transform.position, vel = r.c_rigidbody != null ? r.c_rigidbody.velocity : Vector3.zero };
            SendAll(RNet.Death, m, RNet.ChReliable);
            CoopLog.Write("RSYNC", "death id=" + id + " type=" + r.robot_type + " killer=" + killer + (exploded ? " (instant)" : ""));
        }

        public static void SendExplode(ushort id, Vector3 pos) { SendAll(RNet.Explode, new ExplodeMsg { id = id, pos = pos }, RNet.ChReliable); }

        static RState Capture(ushort id, Robot r)
        {
            var s = new RState { id = id, pos = r.c_transform.position, rot = r.c_transform.rotation, vel = r.c_rigidbody != null ? r.c_rigidbody.velocity : Vector3.zero,
                mode = (byte)r.AI_mode, sub = (byte)r.AI_submode, hp = (ushort)Mathf.Clamp(Mathf.Ceil(r.m_hp), 0, 65535), dmgFlash = (byte)Mathf.Clamp(r.m_dmg_flash * 100f, 0, 255) };
            if (r.gameObject.activeInHierarchy) s.flags |= RState.F_ACTIVE;
            if (r.m_cloaked_robot) s.flags |= RState.F_CLOAK;
            if (r.m_headlight_on) s.flags |= RState.F_HEADLIGHT;
            if (r.m_stasis) s.flags |= RState.F_STASIS;
            if (!r.m_init_hidden) s.flags |= RState.F_REVEALED;
            return s;
        }

        static bool Changed(RState a, RState b)
        {
            return a.flags != b.flags || a.mode != b.mode || a.sub != b.sub || a.hp != b.hp || a.dmgFlash != b.dmgFlash ||
                   (a.pos - b.pos).sqrMagnitude > 0.0004f || Quaternion.Angle(a.rot, b.rot) > 1f;
        }

        public static void Tick()
        {
            if (!HasJoiners()) { s_fire.Clear(); s_pending_spawn.Clear(); return; }
            // spawns first (reliable channel keeps order with deaths)
            foreach (var id in s_pending_spawn) { Robot r; if (CoopRobots.ById.TryGetValue(id, out r) && r != null) SendSpawnNow(null, id, r); }
            s_pending_spawn.Clear();

            if (s_fire.Count > 0)
            {
                for (int i = 0; i < s_fire.Count; i += 30)
                {
                    var m = new FireBatchMsg(); m.e.AddRange(s_fire.GetRange(i, Math.Min(30, s_fire.Count - i)));
                    SendAll(RNet.FireBatch, m, RNet.ChUnrel);
                    CoopRobots.TxFire += m.e.Count;
                }
                s_fire.Clear();
            }

            float now = Time.realtimeSinceStartup;
            if (now < s_next_send) return;
            s_next_send = now + 0.05f; // 20 Hz
            bool key = now >= s_next_key; if (key) s_next_key = now + 1f;
            var batch = new StateBatchMsg { hostTime = now };
            foreach (var kv in CoopRobots.ById)
            {
                var r = kv.Value;
                if (r == null || !r.alive) continue;
                bool active = r.gameObject.activeInHierarchy;
                RState last; bool had = s_last.TryGetValue(kv.Key, out last);
                if (!active && (!had || (last.flags & RState.F_ACTIVE) == 0)) continue;
                var s = Capture(kv.Key, r);
                if (!key && had && !Changed(s, last)) continue;
                s_last[kv.Key] = s;
                batch.e.Add(s);
                if (batch.e.Count == 20) { Flush(batch); batch = new StateBatchMsg { hostTime = now }; }
            }
            if (batch.e.Count > 0) Flush(batch);
        }

        static void Flush(StateBatchMsg b)
        {
            SendAll(RNet.StateBatch, b, RNet.ChUnrelSeq);
            CoopRobots.TxMsgs++; CoopRobots.TxEntries += b.e.Count; CoopRobots.TxBytes += 5 + b.e.Count * 43;
        }

        public static void ResetForLevel() { s_last.Clear(); s_fire.Clear(); s_pending_spawn.Clear(); CoopTargets.Clear(); }

        // ---- handlers
        /// The joiner discards its own robots and rebuilds the host's exact current set (works for saves, mid-level joins, NG+).
        public static void OnRegistryReady(NetworkMessage msg)
        {
            var m = msg.ReadMessage<RegistryReadyMsg>();
            msg.conn.Send(RNet.Manifest, new ManifestMsg { hash = CoopRobots.Hash, count = CoopRobots.PlacedCount });
            int sent = 0, hidden = 0;
            foreach (var kv in CoopRobots.ById)
            {
                var r = kv.Value;
                if (r == null || !r.alive || r.m_dying) continue;
                SendSpawnNow(msg.conn, kv.Key, r);
                sent++; if (r.m_init_hidden) hidden++;
            }
            CoopLog.Write("RSYNC", "world sync -> conn " + msg.conn.connectionId + ": sent " + sent + " robots (" + hidden + " hidden) of " + CoopRobots.ById.Count +
                " registered; joiner had " + m.count + " local");
        }
    }

    // ----------------------------------------------------------------- joiner: receiving
    public static class RobotJoinNet
    {
        static float s_offset; static bool s_offset_valid;
        static readonly Dictionary<ushort, RState> s_pending = new Dictionary<ushort, RState>();

        static float HostNow() { return Time.realtimeSinceStartup - s_offset; }

        public static void SendRegistryReady()
        {
            var c = Client.GetClient();
            if (c == null || !c.isConnected) return;
            c.Send(RNet.RegistryReady, new RegistryReadyMsg { hash = CoopRobots.Hash, count = CoopRobots.PlacedCount });
            CoopLog.Write("RSYNC", "registry ready sent hash=" + CoopRobots.Hash.ToString("X8") + " n=" + CoopRobots.PlacedCount);
        }

        public static void OnLevelInfo(NetworkMessage msg)
        {
            CoopRobots.HostDifficulty = msg.ReadMessage<IntegerMessage>().value;
            CoopLog.Write("RSYNC", "host difficulty " + CoopRobots.HostDifficulty);
        }

        /// Host is about to send its full robot set: remove every robot we loaded locally.
        public static void OnManifest(NetworkMessage msg)
        {
            msg.ReadMessage<ManifestMsg>();
            int removed = 0;
            foreach (var r in new List<Robot>(CoopRobots.ById.Values))
            {
                if (r == null) continue;
                RobotManager.m_master_robot_list.Remove(r);
                r.alive = false;
                UnityEngine.Object.Destroy(r.gameObject);
                removed++;
            }
            CoopRobots.ById.Clear(); CoopRobots.IdOf.Clear(); CoopRobots.Puppets.Clear(); CoopRobots.LocallyDying.Clear();
            WorldSynced = true;
            CoopLog.Write("RSYNC", "world sync: removed " + removed + " local robots, waiting for host's set");
        }

        public static bool WorldSynced;

        public static void OnState(NetworkMessage msg)
        {
            var m = msg.ReadMessage<StateBatchMsg>();
            float sample = Time.realtimeSinceStartup - m.hostTime;
            if (!s_offset_valid || sample < s_offset) { s_offset = sample; s_offset_valid = true; } else s_offset += (sample - s_offset) * 0.01f;
            foreach (var s in m.e)
            {
                CoopRobots.RxEntries++;
                Robot r;
                if (!CoopRobots.ById.TryGetValue(s.id, out r) || r == null) { CoopRobots.RxUnknown++; continue; }
                Puppet p;
                if (!CoopRobots.Puppets.TryGetValue(s.id, out p)) { p = new Puppet(); CoopRobots.Puppets[s.id] = p; }
                if (p.t1 < 0f) { p.t0 = m.hostTime - 0.05f; p.p0 = s.pos; p.r0 = s.rot; }
                else { p.t0 = p.t1; p.p0 = p.p1; p.r0 = p.r1; }
                p.t1 = m.hostTime; p.p1 = s.pos; p.r1 = s.rot; p.v1 = s.vel;
                ApplyFields(r, s, p);
            }
        }

        static void ApplyFields(Robot r, RState s, Puppet p)
        {
            if (r.m_dying || !r.alive) return;
            var mode = (AIModeType)s.mode;
            if (r.AI_mode != mode) r.AI_mode = mode;
            r.AI_submode = (AISubmodeType)s.sub;
            r.m_hp = s.hp;
            r.m_dmg_flash = Mathf.Max(r.m_dmg_flash, s.dmgFlash / 100f);
            r.m_cloaked_robot = (s.flags & RState.F_CLOAK) != 0;
            r.m_headlight_on = (s.flags & RState.F_HEADLIGHT) != 0;
            if ((s.flags & RState.F_REVEALED) != 0 && r.m_init_hidden) { r.RevealRobot(); CoopLog.Write("RSYNC", "revealed id=" + s.id); }
            p.lastMode = s.mode;
        }

        public static void Tick()
        {
            if (!s_offset_valid) return;
            float render = HostNow() - 0.1f;
            foreach (var kv in CoopRobots.Puppets)
            {
                Robot r;
                if (!CoopRobots.ById.TryGetValue(kv.Key, out r) || r == null || !r.alive || r.m_dying) continue;
                var p = kv.Value;
                if (p.t1 < 0f) continue;
                Vector3 pos; Quaternion rot;
                if (render <= p.t1 && p.t1 > p.t0)
                {
                    float t = Mathf.Clamp01((render - p.t0) / (p.t1 - p.t0));
                    pos = Vector3.Lerp(p.p0, p.p1, t); rot = Quaternion.Slerp(p.r0, p.r1, t);
                }
                else
                {
                    float ex = Mathf.Clamp(render - p.t1, 0f, 0.2f);
                    pos = p.p1 + p.v1 * ex; rot = p.r1;
                }
                if ((r.c_transform.position - pos).sqrMagnitude > 0.25f) r.m_believed_valid_current_segment = false;
                r.c_transform.position = pos; r.c_transform.rotation = rot;
                r.c_transform_position = pos; r.c_transform_rotation = rot;
                r.c_transform_forward = rot * Vector3.forward; r.c_transform_right = rot * Vector3.right; r.c_transform_up = rot * Vector3.up;
            }
        }

        public static void OnSpawn(NetworkMessage msg)
        {
            var m = msg.ReadMessage<SpawnMsg>();
            Robot existing;
            if (CoopRobots.ById.TryGetValue(m.id, out existing) && existing != null) return;
            GameObject go;
            CoopRobots.ApplyingRemoteSpawn = true;
            try { go = RobotManager.SpawnNewRobotNoParent(m.pos, m.etype, true); }
            finally { CoopRobots.ApplyingRemoteSpawn = false; }
            if (go == null) { CoopLog.Write("RSYNC", "remote spawn failed id=" + m.id); return; }
            var r = go.GetComponent<Robot>();
            go.transform.rotation = m.rot;
            if (m.super_ && !r.m_super) r.MakeRobotSuper();
            if (m.variant && !r.m_variant) r.MakeRobotVariant();
            CoopRobots.Register(r, m.id);
            if (m.hidden) { r.m_init_hidden = true; go.SetActive(false); }
            s_spawned++;
            if (m.id >= 0x8000 || s_spawned % 25 == 0) CoopLog.Write("RSYNC", "apply spawn id=0x" + m.id.ToString("X4") + " etype=" + (EnemyType)m.etype + " total=" + s_spawned + (m.hidden ? " hidden" : ""));
        }

        public static void OnDeath(NetworkMessage msg)
        {
            try { CoopRobots.ApplyDeath(msg.ReadMessage<DeathMsg>()); } catch (Exception ex) { CoopLog.Error("OnDeath", ex); }
        }

        public static void OnExplode(NetworkMessage msg)
        {
            var m = msg.ReadMessage<ExplodeMsg>();
            Robot r;
            if (CoopRobots.ById.TryGetValue(m.id, out r) && r != null && r.alive)
            {
                if (!r.gameObject.activeInHierarchy) { RobotManager.m_master_robot_list.Remove(r); r.alive = false; UnityEngine.Object.Destroy(r.gameObject); return; }
                try { r.ExplodeNow(); } catch (Exception ex) { CoopLog.Error("OnExplode", ex); }
            }
        }

        public static void OnFire(NetworkMessage msg)
        {
            var m = msg.ReadMessage<FireBatchMsg>();
            foreach (var f in m.e)
            {
                Robot r;
                if (!CoopRobots.ById.TryGetValue(f.id, out r) || r == null || !r.alive) continue;
                CoopRobots.RxFire++;
                CoopRobots.ApplyingRemoteFire = true;
                try { ProjectileManager.FireProjectileRobot(r, (ProjPrefab)f.proj, f.pos, f.rot, r.c_go, f.strength, (ProjTeam)f.team, (WeaponUnlock)f.upgrade, f.savePos); }
                catch (Exception ex) { CoopLog.Error("OnFire", ex); }
                finally { CoopRobots.ApplyingRemoteFire = false; }
            }
        }

        static int s_spawned;
        public static void ResetForLevel() { s_pending.Clear(); s_spawned = 0; WorldSynced = false; }
    }

    // ================================================================= patches

    /// Handler registration (piggybacks on the Phase 1 registration points).
    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class R0_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(RNet.RegistryReady, RobotHostNet.OnRegistryReady);
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class R0_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            var c = Client.GetClient();
            c.RegisterHandler(RNet.LevelInfo, RobotJoinNet.OnLevelInfo);
            c.RegisterHandler(RNet.Manifest, RobotJoinNet.OnManifest);
            c.RegisterHandler(RNet.StateBatch, RobotJoinNet.OnState);
            c.RegisterHandler(RNet.Spawn, RobotJoinNet.OnSpawn);
            c.RegisterHandler(RNet.Death, RobotJoinNet.OnDeath);
            c.RegisterHandler(RNet.Explode, RobotJoinNet.OnExplode);
            c.RegisterHandler(RNet.FireBatch, RobotJoinNet.OnFire);
        }
    }

    /// P1: build the id registry on both peers once the level's robots exist.
    [HarmonyPatch(typeof(RobotManager), "InitializeForNewLevel")]
    static class P1_Registry
    {
        static void Postfix(bool loading_saved_game)
        {
            if (!CoopRobots.Active) return;
            try
            {
                RobotHostNet.ResetForLevel(); RobotJoinNet.ResetForLevel();
                CoopRobots.BuildRegistry();
                if (loading_saved_game) CoopLog.Write("RSYNC", "WARNING: level loaded from a save; joiners may not match robot ids");
                if (CoopRobots.IsJoiner) RobotJoinNet.SendRegistryReady();
            }
            catch (Exception ex) { CoopLog.Error("P1", ex); }
        }
    }

    /// P2: robots spawned during play. Host assigns a dynamic id; joiner destroys any local (non-host) spawn.
    [HarmonyPatch(typeof(RobotManager), "SpawnNewRobotNoParent")]
    static class P2_DynamicSpawn
    {
        static void Postfix(int etype, GameObject __result)
        {
            if (!CoopRobots.Active || __result == null || etype == (int)EnemyType.GUIDEBOT) return;
            try
            {
                var r = __result.GetComponent<Robot>();
                if (r == null) return;
                if (CoopRobots.IsHost)
                {
                    ushort id = CoopRobots.NextDynamic++;
                    CoopRobots.Register(r, id);
                    RobotHostNet.QueueSpawn(id);
                }
                else if (CoopRobots.IsJoiner && !CoopRobots.ApplyingRemoteSpawn)
                {
                    RobotManager.m_master_robot_list.Remove(r);
                    r.alive = false;
                    UnityEngine.Object.Destroy(__result);
                    CoopLog.Write("RSYNC", "orphan local spawn destroyed etype=" + (EnemyType)etype);
                }
            }
            catch (Exception ex) { CoopLog.Error("P2", ex); }
        }
    }

    /// P3: joiner never spawns from matcens (host does, and replicates).
    [HarmonyPatch(typeof(RobotMatcen), "SpawnRobot")]
    static class P3_NoJoinerMatcen
    {
        static bool Prefix() { return !CoopRobots.IsJoiner; }
    }

    /// P4: Robot.Update. Joiner: cosmetic tick only. Host: per-robot target swap.
    [HarmonyPatch(typeof(Robot), "Update")]
    static class P4_RobotUpdate
    {
        static bool Prefix(Robot __instance)
        {
            if (!CoopRobots.Active) return true;
            if (CoopRobots.IsJoiner)
            {
                if (!CoopRobots.IsPuppet(__instance) || CoopRobots.LocallyDying.Contains(__instance.GetInstanceID())) return true;
                if (GameplayManager.GamePaused) return false;
                try { CoopRobots.CosmeticTick(__instance); } catch (Exception ex) { CoopLog.Error("P4 cosmetic", ex); }
                return false;
            }
            if (CoopRobots.IsHost && !__instance.m_is_guide_bot) CoopTargets.Push(__instance);
            return true;
        }
        static Exception Finalizer(Robot __instance, Exception __exception)
        {
            if (CoopRobots.IsHost) CoopTargets.Pop(__instance);
            return __exception;
        }
    }

    /// P5: Robot.FixedUpdate. Joiner: no AI/physics/pathing unless dying locally. Host: per-robot target swap.
    [HarmonyPatch(typeof(Robot), "FixedUpdate")]
    static class P5_RobotFixedUpdate
    {
        static bool Prefix(Robot __instance)
        {
            if (!CoopRobots.Active) return true;
            if (CoopRobots.IsJoiner)
                return !CoopRobots.IsPuppet(__instance) || CoopRobots.LocallyDying.Contains(__instance.GetInstanceID());
            if (CoopRobots.IsHost && !__instance.m_is_guide_bot) CoopTargets.Push(__instance);
            return true;
        }
        static Exception Finalizer(Robot __instance, Exception __exception)
        {
            if (CoopRobots.IsHost) CoopTargets.Pop(__instance);
            return __exception;
        }
    }

    /// P6: joiner never applies damage to robots (host is authoritative). Keep a little hit flash for feedback.
    [HarmonyPatch(typeof(Robot), "ApplyDamage")]
    static class P6_NoJoinerRobotDamage
    {
        static bool Prefix(Robot __instance, DamageInfo di, ref bool __result)
        {
            if (CoopRobots.IsHost) { CoopRobots.HostRobotHits++; CoopTargets.NoteHit(__instance, di.owner); return true; }
            if (!CoopRobots.IsPuppet(__instance)) return true;
            __instance.m_dmg_flash = Mathf.Min(1f, __instance.m_dmg_flash + 0.1f);
            __result = false;
            return false;
        }
    }

    /// P7: host broadcasts a robot's death as soon as it starts exploding.
    [HarmonyPatch(typeof(Robot), "StartExploding")]
    static class P7_HostDeath
    {
        static void Postfix(Robot __instance, DamageInfo di)
        {
            if (!CoopRobots.IsHost) return;
            ushort id;
            if (!CoopRobots.TryId(__instance, out id)) return;
            try
            {
                uint killer = 0;
                if (di.owner != null) { var p = di.owner.GetComponent<Player>(); if (p != null) killer = p.netId.Value; }
                RobotHostNet.SendDeath(__instance, id, killer, (int)di.weapon, false);
            }
            catch (Exception ex) { CoopLog.Error("P7", ex); }
        }
    }

    /// P8: ExplodeNow. Joiner: idempotent and suppresses local item drops. Host: broadcast the explosion.
    [HarmonyPatch(typeof(Robot), "ExplodeNow")]
    static class P8_ExplodeNow
    {
        static bool Prefix(Robot __instance)
        {
            if (!CoopRobots.IsJoiner) return true;
            if (!__instance.alive) return false;
            CoopRobots.InJoinerExplode = true;
            return true;
        }
        static void Postfix(Robot __instance)
        {
            if (CoopRobots.IsJoiner) { CoopRobots.InJoinerExplode = false; CoopRobots.LocallyDying.Remove(__instance.GetInstanceID()); return; }
            if (!CoopRobots.IsHost) return;
            ushort id;
            if (!CoopRobots.TryId(__instance, out id)) return;
            try
            {
                RobotHostNet.SendDeath(__instance, id, 0, 0, true); // no-op if StartExploding already sent it
                RobotHostNet.SendExplode(id, __instance.c_transform.position);
            }
            catch (Exception ex) { CoopLog.Error("P8", ex); }
        }
    }

    /// P9: on the joiner, drops come from the host as networked items; drop the local spew.
    [HarmonyPatch(typeof(Item), "Spew")]
    static class P9_NoJoinerDeathDrops
    {
        static bool Prefix()
        {
            if (CoopRobots.IsJoiner && CoopRobots.InJoinerExplode) { CoopRobots.SuppressedSpews++; return false; }
            return true;
        }
    }

    /// P10: host forwards every robot shot so joiners see it (cosmetic there; damage is host-side).
    [HarmonyPatch(typeof(ProjectileManager), "FireProjectileRobot")]
    static class P10_HostRobotFire
    {
        static void Postfix(Robot robot, ProjPrefab type, Vector3 pos, Quaternion rot, float strength, ProjTeam proj_team, WeaponUnlock upgrade_lvl, bool save_pos)
        {
            if (!CoopRobots.IsHost || robot == null || robot.m_dying) return;
            CoopRobots.HostRobotFires++;
            ushort id;
            if (!CoopRobots.TryId(robot, out id)) return;
            RobotHostNet.QueueFire(new RFire { id = id, proj = (int)type, team = (int)proj_team, upgrade = (int)upgrade_lvl, strength = strength, savePos = save_pos, pos = pos, rot = rot });
        }
    }

    /// P11: host simulates robots relevant to ANY player, not just the host ship.
    [HarmonyPatch(typeof(RobotManager), "RobotInRelevantSegment")]
    static class P11_RelevantToAnyPlayer
    {
        static void Postfix(Robot robot, ref bool __result)
        {
            if (__result || !CoopRobots.IsHost || robot == null) return;
            int seg = robot.m_current_segment;
            if (seg < 0) return;
            var vis = GameManager.m_level_data.m_segment_visibility;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.isLocalPlayer || p.c_player_ship == null || p.c_player_ship.c_moving_object == null) continue;
                int s = p.c_player_ship.c_moving_object.CurrentSegmentIndex;
                if (s >= 0 && vis[s, seg] > 0) { __result = true; return; }
            }
        }
    }

    /// P14: per-frame network work. Host: send. Joiner: interpolate puppets.
    [HarmonyPatch(typeof(RobotManager), "Update")]
    static class P14_RobotNetTick
    {
        static void Postfix()
        {
            if (!CoopRobots.Active) return;
            try
            {
                if (CoopRobots.IsHost) RobotHostNet.Tick();
                else if (CoopRobots.IsJoiner) RobotJoinNet.Tick();
                CoopRobots.Report();
            }
            catch (Exception ex) { CoopLog.Error("P14", ex); }
        }
    }
}
