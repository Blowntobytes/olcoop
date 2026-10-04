// olcoop 0.4.11-0.4.12: combat parity.
//  - Melee robots (claw/blade "Shredder", detonator, charger) damage the ship they actually hit, not always the host.
//  - Joiner loadout (weapons, upgrade levels, ammo, missiles, energy) is applied to the host's copy of that joiner, so the host
//    stops refusing the joiner's ammo-weapon and missile shots.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Combat
{
    // ===================================================================== melee
    /// Robot.ApplyClawImpulse / ApplyDetonatorImpulse / ApplyChargerImpulse push `target_rigidbody` (the mod's per-robot target) but
    /// then call `GameManager.m_player_ship.ApplyDamage(di)` - on the host that is always the host's ship (IL; docs/phase2a-design.md
    /// "melee damage"). Each `ldsfld GameManager::m_player_ship` in those three methods is replaced with `MeleeVictim(this)`.
    [HarmonyPatch]
    static class M1_MeleeVictim
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Robot), "ApplyClawImpulse");
            yield return AccessTools.Method(typeof(Robot), "ApplyDetonatorImpulse");
            yield return AccessTools.Method(typeof(Robot), "ApplyChargerImpulse");
        }

        static readonly FieldInfo f_local_ship = AccessTools.Field(typeof(GameManager), "m_player_ship");
        static readonly FieldInfo f_target_rb = AccessTools.Field(typeof(Robot), "target_rigidbody");
        static readonly MethodInfo m_victim = AccessTools.Method(typeof(M1_MeleeVictim), nameof(MeleeVictim));
        static int s_logged;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int n = 0;
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldsfld && f_local_ship.Equals(ins.operand))
                {
                    var a = new CodeInstruction(OpCodes.Ldarg_0) { labels = ins.labels, blocks = ins.blocks };
                    yield return a;
                    yield return new CodeInstruction(OpCodes.Call, m_victim);
                    n++;
                    continue;
                }
                yield return ins;
            }
            CoopLog.Write("INIT", "melee victim: " + __originalMethod.Name + " " + n + " site(s) rerouted");
        }

        /// The ship the robot's melee is aimed at (host only - joiners don't run robot AI). Falls back to the local ship.
        public static PlayerShip MeleeVictim(Robot r)
        {
            var local = GameManager.m_player_ship;
            try
            {
                if (r == null || !CoopConfig.Active || GameplayManager.IsMultiplayer) return local;
                var rb = f_target_rb.GetValue(r) as Rigidbody;
                var ship = rb != null ? rb.GetComponent<PlayerShip>() : null;
                if (ship == null || ship == local) return local;
                if (s_logged < 20) { s_logged++; CoopLog.Write("COMBAT", "melee " + r.robot_type + " hits netId=" + ship.c_player.netId.Value + " (not the host)"); }
                return ship;
            }
            catch (Exception ex) { CoopLog.Error("MeleeVictim", ex); return local; }
        }
    }

    // ===================================================================== loadout
    public static class LNet { public const short Loadout = 170; } // J->H LoadoutMsg (once per spawn)

    public class LoadoutMsg : MessageBase
    {
        public byte weapon, missile;
        public byte[] wlevel, mlevel; public bool[] wpicked;
        public int ammo; public int[] mammo; public float energy;
        public bool[] unlocks; // CoopLoadout.UnlockFieldNames order (ship upgrades: boost, boost speed/heatsink, headlight, ...)
        public override void Serialize(NetworkWriter w)
        {
            w.Write(weapon); w.Write(missile);
            w.Write((byte)wlevel.Length); foreach (var b in wlevel) w.Write(b);
            w.Write((byte)mlevel.Length); foreach (var b in mlevel) w.Write(b);
            w.Write((byte)wpicked.Length); foreach (var b in wpicked) w.Write(b);
            w.Write(ammo); w.Write((byte)mammo.Length); foreach (var a in mammo) w.Write(a); w.Write(energy);
            w.Write((byte)unlocks.Length); foreach (var b in unlocks) w.Write(b);
        }
        public override void Deserialize(NetworkReader r)
        {
            weapon = r.ReadByte(); missile = r.ReadByte();
            wlevel = new byte[r.ReadByte()]; for (int i = 0; i < wlevel.Length; i++) wlevel[i] = r.ReadByte();
            mlevel = new byte[r.ReadByte()]; for (int i = 0; i < mlevel.Length; i++) mlevel[i] = r.ReadByte();
            wpicked = new bool[r.ReadByte()]; for (int i = 0; i < wpicked.Length; i++) wpicked[i] = r.ReadBoolean();
            ammo = r.ReadInt32(); mammo = new int[r.ReadByte()]; for (int i = 0; i < mammo.Length; i++) mammo[i] = r.ReadInt32();
            energy = r.ReadSingle();
            unlocks = new bool[r.ReadByte()]; for (int i = 0; i < unlocks.Length; i++) unlocks[i] = r.ReadBoolean();
        }
        public string Describe()
        {
            return "weapon=" + (WeaponType)weapon + " missile=" + (MissileType)missile + " wlevel=[" + string.Join(",", Array.ConvertAll(wlevel, b => b.ToString())) +
                   "] mlevel=[" + string.Join(",", Array.ConvertAll(mlevel, b => b.ToString())) + "] ammo=" + ammo +
                   " missiles=[" + string.Join(",", Array.ConvertAll(mammo, a => a.ToString())) + "] energy=" + energy.ToString("F0") + " upgrades=" + CoopLoadout.UnlockNames(unlocks);
        }
    }

    public static class CoopLoadout
    {
        /// Ship upgrades (Player.m_unlock_*). The host simulates every ship, and boosting is gated by m_unlock_boost in
        /// PlayerShip.FixedUpdateProcessControlsInternal: without it the host never boosts a joiner, so no one sees the joiner's boost
        /// (RpcSetBoosting is only sent when the host's copy boosts) and the joiner's own boost is corrected away.
        public static readonly string[] UnlockFieldNames = {
            "m_unlock_boost", "m_unlock_boost_speed", "m_unlock_boost_heatsink", "m_unlock_headlight", "m_unlock_accessory_free",
            "m_unlock_headlight_red", "m_unlock_flare_sticky", "m_unlock_selfdamage_reduction", "m_unlock_item_duration",
            "m_unlock_smash_damage", "m_unlock_fast_forward", "m_unlock_blast_damage" };
        static FieldInfo[] s_unlock_fields;
        static FieldInfo[] UnlockFields
        {
            get
            {
                if (s_unlock_fields == null)
                {
                    s_unlock_fields = new FieldInfo[UnlockFieldNames.Length];
                    for (int i = 0; i < UnlockFieldNames.Length; i++) s_unlock_fields[i] = AccessTools.Field(typeof(Player), UnlockFieldNames[i]);
                }
                return s_unlock_fields;
            }
        }
        public static string UnlockNames(bool[] u)
        {
            var on = new List<string>();
            for (int i = 0; u != null && i < u.Length && i < UnlockFieldNames.Length; i++) if (u[i]) on.Add(UnlockFieldNames[i].Substring(9));
            return on.Count == 0 ? "none" : string.Join(",", on.ToArray());
        }

        // ------------------------------------------------------------ joiner
        static float s_send_at = -1f;

        /// Joiner: our ship just started in a co-op level; send our loadout to the host shortly (its copy of us must exist first).
        public static void JoinerShipStarted() { s_send_at = Time.realtimeSinceStartup + 1.0f; }

        public static void JoinerTick()
        {
            if (s_send_at < 0f || Time.realtimeSinceStartup < s_send_at) return;
            s_send_at = -1f;
            var p = GameManager.m_local_player;
            var c = Client.GetClient();
            if (p == null || c == null || !c.isConnected) return;
            var m = Capture(p);
            c.Send(LNet.Loadout, m);
            CoopLog.Write("COMBAT", "joiner: sent loadout to host: " + m.Describe());
        }

        static LoadoutMsg Capture(Player p)
        {
            var m = new LoadoutMsg
            {
                weapon = (byte)p.m_weapon_type, missile = (byte)p.m_missile_type,
                wlevel = new byte[p.m_weapon_level.Length], mlevel = new byte[p.m_missile_level.Length],
                wpicked = (bool[])p.m_weapon_picked_up.Clone(),
                ammo = (int)p.m_ammo, mammo = new int[p.m_missile_ammo.Length], energy = (float)p.m_energy
            };
            for (int i = 0; i < m.wlevel.Length; i++) m.wlevel[i] = (byte)p.m_weapon_level[i];
            for (int i = 0; i < m.mlevel.Length; i++) m.mlevel[i] = (byte)p.m_missile_level[i];
            for (int i = 0; i < m.mammo.Length; i++) m.mammo[i] = (int)p.m_missile_ammo[i];
            var uf = UnlockFields; m.unlocks = new bool[uf.Length];
            for (int i = 0; i < uf.Length; i++) m.unlocks[i] = uf[i] != null && (bool)uf[i].GetValue(p);
            return m;
        }

        // ------------------------------------------------------------ host
        /// Host: apply a joiner's loadout to our copy of that joiner. Afterwards the game's own server-side sync (Player.Update sends
        /// ammo/energy RPCs, pickups run on the host) keeps both sides in step.
        public static void OnLoadout(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LoadoutMsg>();
                Player target = null;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && !p.isLocalPlayer && p.connectionToClient != null && p.connectionToClient.connectionId == msg.conn.connectionId) target = p;
                if (target == null) { CoopLog.Write("COMBAT", "host: loadout from conn " + msg.conn.connectionId + " but no player for it yet"); return; }
                for (int i = 0; i < Math.Min(m.wlevel.Length, target.m_weapon_level.Length); i++) target.m_weapon_level[i] = (WeaponUnlock)m.wlevel[i];
                for (int i = 0; i < Math.Min(m.mlevel.Length, target.m_missile_level.Length); i++) target.m_missile_level[i] = (WeaponUnlock)m.mlevel[i];
                for (int i = 0; i < Math.Min(m.wpicked.Length, target.m_weapon_picked_up.Length); i++) target.m_weapon_picked_up[i] = m.wpicked[i];
                for (int i = 0; i < Math.Min(m.mammo.Length, target.m_missile_ammo.Length); i++) target.m_missile_ammo[i] = m.mammo[i];
                var uf = UnlockFields;
                for (int i = 0; i < Math.Min(m.unlocks.Length, uf.Length); i++) if (uf[i] != null) uf[i].SetValue(target, m.unlocks[i]);
                target.m_ammo = m.ammo;
                target.m_energy = m.energy;
                target.Networkm_weapon_type = (WeaponType)m.weapon;
                target.Networkm_missile_type = (MissileType)m.missile;
                CoopLog.Write("COMBAT", "host: applied loadout of conn " + msg.conn.connectionId + " to netId=" + target.netId.Value + ": " + m.Describe());
            }
            catch (Exception ex) { CoopLog.Error("OnLoadout", ex); }
        }
    }

    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class L1_JoinerShipStarted
    {
        static void Postfix()
        {
            try { if (OlCoop.World.CoopWorld.IsJoiner) CoopLoadout.JoinerShipStarted(); } catch (Exception ex) { CoopLog.Error("L1", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class L2_JoinerTick
    {
        static void Postfix()
        {
            if (!OlCoop.World.CoopWorld.IsJoiner) return;
            try { CoopLoadout.JoinerTick(); } catch (Exception ex) { CoopLog.Error("L2", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class L3_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(LNet.Loadout, CoopLoadout.OnLoadout);
        }
    }
}
