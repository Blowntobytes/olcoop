// Auto-op fabricators (robot matcens) and cryotubes on joiners (0.6.10).
//  - RobotMatcen: ScriptActivateMatcen is host-only, so a joiner's fabricator never ran ActivateMatcen (glow effects, flare, active
//    light) and never played the spawn countdown (spawn_mini_flash1 + matcen_trail1 + cues 356/355) or the spawn flash
//    (spawn_flash1) - those come from MatcenFrame, which also spawns the robot and so must stay host-only. The host now sends
//    activation (on change and every 5 s for late joiners) and every countdown effect / spawn (msg 207); joiners show them.
//    Joiners' MatcenFrame is skipped (it would spawn local robots once activated).
//  - PropCryotube: each machine collects a cryotube when ITS copy of a ship touches it, so a joiner whose copy of the collecting ship
//    missed the trigger got no pod effect and no "PICKED UP CRYOTUBE" message. The host sends every collection (msg 208, index);
//    joiners run the stock Collect() once.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    public class MatcenFxMsg : MessageBase
    {
        public byte id, kind; public float num; public bool sound;
        public override void Serialize(NetworkWriter w) { w.Write(id); w.Write(kind); w.Write(num); w.Write(sound); }
        public override void Deserialize(NetworkReader r) { id = r.ReadByte(); kind = r.ReadByte(); num = r.ReadSingle(); sound = r.ReadBoolean(); }
    }

    public static class CoopWorldFx
    {
        public const short MsgMatcen = 207;   // H->J MatcenFxMsg: kind 0 activate, 1 countdown effect, 2 spawn flash
        public const short MsgCryo = 208;     // H->J IntegerMessage: cryotube index collected
        public const byte KindActivate = 0, KindMini = 1, KindSpawn = 2;

        static readonly FieldInfo f_next_mini = AccessTools.Field(typeof(RobotMatcen), "m_next_mini_effect");
        static readonly FieldInfo f_spawn_timer = AccessTools.Field(typeof(RobotMatcen), "m_spawn_timer");
        static readonly FieldInfo f_total = AccessTools.Field(typeof(RobotMatcen), "m_total_spawns");

        static List<RobotMatcen> s_matcens;
        static float s_next_activation;
        static int s_logged;

        public static void ResetForLevel() { s_matcens = null; s_next_activation = 0f; s_logged = 0; }

        static bool InScene(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }

        /// Same order on every machine: by position.
        static List<RobotMatcen> Matcens()
        {
            if (s_matcens != null) return s_matcens;
            var l = new List<RobotMatcen>();
            foreach (var m in Resources.FindObjectsOfTypeAll<RobotMatcen>()) if (InScene(m)) l.Add(m);
            l.Sort((a, b) =>
            {
                Vector3 p = a.transform.position, q = b.transform.position;
                int c = p.x.CompareTo(q.x); if (c != 0) return c;
                c = p.y.CompareTo(q.y); if (c != 0) return c;
                return p.z.CompareTo(q.z);
            });
            s_matcens = l;
            return l;
        }

        static void SendAll(short type, MessageBase m)
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(type, m);
        }

        static void Send(RobotMatcen m, byte kind, float num, bool sound)
        {
            int id = Matcens().IndexOf(m);
            if (id < 0 || id > 255) return;
            SendAll(MsgMatcen, new MatcenFxMsg { id = (byte)id, kind = kind, num = num, sound = sound });
            if (kind != KindMini && s_logged++ < 40) CoopLog.Write("FX", "host: fabricator " + id + (kind == KindActivate ? " activated" : " spawned a robot"));
        }

        // ---------------------------------------------------------------- host
        public struct FrameState { public float mini; public int total; public bool soundReady; public bool valid; }

        public static FrameState Before(RobotMatcen m)
        {
            if (!CoopWorld.IsHost || f_next_mini == null || f_total == null) return default(FrameState);
            return new FrameState { mini = (float)f_next_mini.GetValue(m), total = (int)f_total.GetValue(m), soundReady = m.m_sound_ready, valid = true };
        }

        public static void After(RobotMatcen m, FrameState b)
        {
            if (!b.valid) return;
            int total = (int)f_total.GetValue(m);
            if (total > b.total) { Send(m, KindSpawn, 0f, false); return; }
            float mini = (float)f_next_mini.GetValue(m);
            if (mini < b.mini) Send(m, KindMini, (float)f_spawn_timer.GetValue(m) * 4f, b.soundReady);
        }

        public static void HostActivated(RobotMatcen m) { if (CoopWorld.IsHost) Send(m, KindActivate, 0f, false); }

        /// Host: resend activations every 5 s (late joiners; activation also runs before joiners have the level).
        public static void HostTick()
        {
            if (!CoopWorld.IsHost || !GameplayManager.LevelIsLoaded || Time.realtimeSinceStartup < s_next_activation) return;
            s_next_activation = Time.realtimeSinceStartup + 5f;
            var l = Matcens();
            for (int i = 0; i < l.Count && i < 256; i++)
                if (l[i] != null && l[i].m_matcen_activated && !l[i].m_destroyed)
                    SendAll(MsgMatcen, new MatcenFxMsg { id = (byte)i, kind = KindActivate });
        }

        public static void HostCryo(PropCryotube c)
        {
            if (!CoopWorld.IsHost) return;
            SendAll(MsgCryo, new UnityEngine.Networking.NetworkSystem.IntegerMessage(c.m_index));
            CoopLog.Write("FX", "host: cryotube " + c.m_index + " collected; sent to joiners");
        }

        // ---------------------------------------------------------------- joiner
        public static bool JoinerMirror { get { return CoopWorld.IsJoiner && CoopWorld.Matched; } }

        public static void OnMatcen(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<MatcenFxMsg>();
                if (!JoinerMirror || !GameplayManager.LevelIsLoaded) return;
                var l = Matcens();
                if (m.id >= l.Count || l[m.id] == null || l[m.id].m_destroyed) return;
                var mc = l[m.id];
                if (!mc.m_matcen_activated) { mc.ActivateMatcen(); CoopLog.Write("FX", "joiner: fabricator " + m.id + " activated (host)"); }
                if (m.kind == KindMini)
                {
                    if (m.sound) { GameManager.m_audio.PlayCuePos(356, mc.m_spawn_pos); GameManager.m_audio.PlayCuePos(355, mc.m_spawn_pos); }
                    var fwd = mc.c_transform != null ? mc.c_transform.forward : mc.transform.forward;
                    ExplosionManager.CreateExpElementFromResources(FXExpElement.spawn_mini_flash1, mc.m_spawn_pos - fwd * m.num + UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0f, 0.3f), mc.m_spawn_rot);
                    ExplosionManager.CreateExpElementFromResources(FXExpElement.matcen_trail1, mc.m_spawn_pos - fwd * m.num, mc.m_spawn_rot);
                }
                else if (m.kind == KindSpawn)
                {
                    ExplosionManager.CreateExpElementFromResources(FXExpElement.spawn_flash1, mc.m_spawn_pos, mc.m_spawn_rot);
                }
            }
            catch (Exception ex) { CoopLog.Error("OnMatcen", ex); }
        }

        public static void OnCryo(NetworkMessage msg)
        {
            try
            {
                int idx = msg.ReadMessage<UnityEngine.Networking.NetworkSystem.IntegerMessage>().value;
                if (!CoopWorld.IsJoiner || !GameplayManager.LevelIsLoaded) return;
                // Resources.FindObjectsOfTypeAll: pods outside our visible rooms are switched off (FindObjectsOfType skips them -
                // 0.6.10 run 14:31: "cryotube 5 not found")
                foreach (var c in Resources.FindObjectsOfTypeAll<PropCryotube>())
                {
                    if (c == null || !InScene(c) || c.m_index != idx) continue;
                    if (c.m_has_been_collected) { CoopLog.Write("FX", "joiner: cryotube " + idx + " already collected here"); return; }
                    c.Collect();
                    CoopLog.Write("FX", "joiner: cryotube " + idx + " collected (host)");
                    return;
                }
                CoopLog.Write("FX", "joiner: cryotube " + idx + " not found");
            }
            catch (Exception ex) { CoopLog.Error("OnCryo", ex); }
        }
    }

    // ================================================================= patches

    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class FX0_Reset
    {
        static void Prefix() { if (CoopConfig.Active) CoopWorldFx.ResetForLevel(); }
    }

    /// FX1: host sends countdown effects / spawns; joiners don't run the fabricator (it would spawn local robots).
    [HarmonyPatch(typeof(RobotMatcen), "MatcenFrame")]
    static class FX1_MatcenFrame
    {
        static bool Prefix(RobotMatcen __instance, out CoopWorldFx.FrameState __state)
        {
            __state = default(CoopWorldFx.FrameState);
            if (CoopWorldFx.JoinerMirror) return false;
            try { __state = CoopWorldFx.Before(__instance); } catch (Exception ex) { CoopLog.Error("FX1 pre", ex); }
            return true;
        }
        static void Postfix(RobotMatcen __instance, CoopWorldFx.FrameState __state)
        {
            try { CoopWorldFx.After(__instance, __state); } catch (Exception ex) { CoopLog.Error("FX1", ex); }
        }
    }

    [HarmonyPatch(typeof(RobotMatcen), "ActivateMatcen")]
    static class FX2_MatcenActivate
    {
        static void Postfix(RobotMatcen __instance) { try { CoopWorldFx.HostActivated(__instance); } catch (Exception ex) { CoopLog.Error("FX2", ex); } }
    }

    [HarmonyPatch(typeof(PropCryotube), "Collect")]
    static class FX3_CryoCollect
    {
        static void Postfix(PropCryotube __instance) { try { CoopWorldFx.HostCryo(__instance); } catch (Exception ex) { CoopLog.Error("FX3", ex); } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class FX4_HostTick
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { CoopWorldFx.HostTick(); } catch (Exception ex) { CoopLog.Error("FX4", ex); }
        }
    }

    /// 0.7.4: door states on join. A saved game restores the host's doors (DoorBase.Deserialize: LockType, OpenForever, HasBeenOpened)
    /// - e.g. a door unlocked by a script before the save - but a joiner loads the level fresh: its copy stayed LOCKED, wouldn't open and
    /// the joiner half-slid through the host's open door. The host sends every door's lock state after the world manifest (msg 213).
    public class DoorStatesMsg : MessageBase
    {
        public byte[] data = new byte[0]; // per door (sorted by position): LockType, flags (1 OpenForever, 2 HasBeenOpened)
        public override void Serialize(NetworkWriter w) { w.WriteBytesAndSize(data, data.Length); }
        public override void Deserialize(NetworkReader r) { data = r.ReadBytesAndSize() ?? new byte[0]; }
    }

    public static class CoopDoors
    {
        public const short MsgDoors = 213;

        static bool InScene(Component c) { return c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded; }
        static List<DoorBase> Doors()
        {
            var l = new List<DoorBase>();
            foreach (var d in Resources.FindObjectsOfTypeAll<DoorBase>()) if (InScene(d)) l.Add(d);
            l.Sort((a, b) =>
            {
                Vector3 p = a.transform.position, q = b.transform.position;
                int c = p.x.CompareTo(q.x); if (c != 0) return c;
                c = p.y.CompareTo(q.y); if (c != 0) return c;
                return p.z.CompareTo(q.z);
            });
            return l;
        }

        public static void SendTo(NetworkConnection conn)
        {
            var doors = Doors();
            var data = new byte[doors.Count * 2];
            for (int i = 0; i < doors.Count; i++)
            {
                data[i * 2] = (byte)doors[i].LockType;
                data[i * 2 + 1] = (byte)((doors[i].OpenForever ? 1 : 0) | (doors[i].HasBeenOpened ? 2 : 0));
            }
            conn.Send(MsgDoors, new DoorStatesMsg { data = data });
            CoopLog.Write("WORLD", "host: sent " + doors.Count + " door states to conn " + conn.connectionId);
        }

        public static void OnDoors(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<DoorStatesMsg>();
                var doors = Doors();
                int n = m.data.Length / 2;
                if (n != doors.Count) { CoopLog.Write("WORLD", "joiner: host has " + n + " doors, we have " + doors.Count + "; door states not applied"); return; }
                int changed = 0;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    var d = doors[i];
                    var lt = (DoorLock)m.data[i * 2]; bool forever = (m.data[i * 2 + 1] & 1) != 0, opened = (m.data[i * 2 + 1] & 2) != 0;
                    if (d.LockType == lt && d.OpenForever == forever && d.HasBeenOpened == opened) continue;
                    if (changed < 8) sb.Append(' ').Append(d.gameObject.name).Append(':').Append(d.LockType).Append("->").Append(lt);
                    d.LockType = lt; d.OpenForever = forever; d.HasBeenOpened = opened;
                    changed++;
                }
                CoopLog.Write("WORLD", "joiner: door states from the host: " + changed + " of " + n + " changed" + sb);
            }
            catch (Exception ex) { CoopLog.Error("CoopDoors.OnDoors", ex); }
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class FX5_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(CoopWorldFx.MsgMatcen, CoopWorldFx.OnMatcen);
            Client.GetClient().RegisterHandler(CoopWorldFx.MsgCryo, CoopWorldFx.OnCryo);
            Client.GetClient().RegisterHandler(CoopDoors.MsgDoors, CoopDoors.OnDoors);
        }
    }
}
