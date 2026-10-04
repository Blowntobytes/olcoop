// olcoop 0.4.11-0.4.14: combat parity.
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
    public static class LNet { public const short Loadout = 170; public const short MissileGrant = 174; } // 170 J->H loadout; 174 H->J missile pickup

    public class MissileGrantMsg : MessageBase
    {
        public byte mt; public int delta;
        public override void Serialize(NetworkWriter w) { w.Write(mt); w.Write(delta); }
        public override void Deserialize(NetworkReader r) { mt = r.ReadByte(); delta = r.ReadInt32(); }
    }

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

        // Carry-over (0.4.13): a joiner's game starts each co-op level with that level's default campaign loadout (07:38/07:47 runs:
        // both joiners IMPULSE/FALCON, ammo 200, missiles 10/60/8/24), so pickups were lost between levels. The loadout at the end of
        // the exit flight is kept and re-applied when the next level's ship starts (this game session only).
        static LoadoutMsg s_carry;
        static bool s_carry_taken;
        static string s_last_state;
        static float s_next_poll;

        /// Joiner: end-of-level screens done (after upgrades): keep this loadout for the next level.
        public static void CaptureForNextLevel(string why)
        {
            var lp = GameManager.m_local_player;
            if (lp == null) return;
            s_carry_taken = true; s_carry = Capture(lp);
            CoopLog.Write("COMBAT", "joiner: " + why + ", keeping loadout for the next level: " + s_carry.Describe());
        }

        /// Joiner: our ship just started in a co-op level; send our loadout to the host shortly (its copy of us must exist first).
        public static void JoinerShipStarted() { s_send_at = Time.realtimeSinceStartup + 1.0f; s_carry_taken = false; s_last_state = null; }

        public static void JoinerTick()
        {
            var lp = GameManager.m_local_player;
            if (!s_carry_taken && OlCoop.World.CoopFlow.Waiting && lp != null)
            {
                s_carry_taken = true; s_carry = Capture(lp);
                CoopLog.Write("COMBAT", "joiner: level complete, keeping loadout for the next level: " + s_carry.Describe());
            }
            if (lp != null && Time.realtimeSinceStartup >= s_next_poll) { s_next_poll = Time.realtimeSinceStartup + 1f; LogChanges(lp); }
            if (s_send_at < 0f || Time.realtimeSinceStartup < s_send_at) return;
            s_send_at = -1f;
            var p = GameManager.m_local_player;
            var c = Client.GetClient();
            if (p == null || c == null || !c.isConnected) return;
            if (s_carry != null)
            {
                Merge(p, s_carry);
                CoopLog.Write("COMBAT", "joiner: restored loadout from the previous level: " + s_carry.Describe());
                s_carry = null;
            }
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

        /// Joiner diagnostics: log the local weapon/missile state whenever it changes (Devastator selection report, 07:47 run).
        static void LogChanges(Player p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("weapon=").Append(p.m_weapon_type).Append(" missile=").Append(p.m_missile_type).Append(" wlevel=[");
            for (int i = 0; i < p.m_weapon_level.Length; i++) sb.Append(i > 0 ? "," : "").Append((int)p.m_weapon_level[i]);
            sb.Append("] mlevel=[");
            for (int i = 0; i < p.m_missile_level.Length; i++) sb.Append(i > 0 ? "," : "").Append((int)p.m_missile_level[i]);
            sb.Append("] missiles=[");
            for (int i = 0; i < p.m_missile_ammo.Length; i++) sb.Append(i > 0 ? "," : "").Append((int)p.m_missile_ammo[i]);
            sb.Append("] ammo=").Append((int)p.m_ammo);
            string st = sb.ToString();
            if (st == s_last_state) return;
            s_last_state = st;
            CoopLog.Write("COMBAT", "joiner: local loadout now " + st);
        }

        /// Joiner, next level: keep what the new level gave us AND what we carried (08:10 run: the restore wiped the Flak the new level
        /// had unlocked). Unlock levels / picked-up flags / ship upgrades: the higher of the two. Ammo, missile counts, energy and the
        /// selected weapon/missile: the carried values (the campaign carries them; it doesn't refill).
        static void Merge(Player p, LoadoutMsg c)
        {
            for (int i = 0; i < Math.Min(c.wlevel.Length, p.m_weapon_level.Length); i++) if (c.wlevel[i] > (byte)p.m_weapon_level[i]) p.m_weapon_level[i] = (WeaponUnlock)c.wlevel[i];
            for (int i = 0; i < Math.Min(c.mlevel.Length, p.m_missile_level.Length); i++) if (c.mlevel[i] > (byte)p.m_missile_level[i]) p.m_missile_level[i] = (WeaponUnlock)c.mlevel[i];
            for (int i = 0; i < Math.Min(c.wpicked.Length, p.m_weapon_picked_up.Length); i++) if (c.wpicked[i]) p.m_weapon_picked_up[i] = true;
            var uf = UnlockFields;
            for (int i = 0; i < Math.Min(c.unlocks.Length, uf.Length); i++) if (uf[i] != null && c.unlocks[i]) uf[i].SetValue(p, true);
            for (int i = 0; i < Math.Min(c.mammo.Length, p.m_missile_ammo.Length); i++) p.m_missile_ammo[i] = c.mammo[i];
            p.m_ammo = c.ammo;
            p.m_energy = c.energy;
            if (p.m_weapon_level[c.weapon] != WeaponUnlock.LOCKED) p.m_weapon_type = (WeaponType)c.weapon;
            if (p.m_missile_level[c.missile] != WeaponUnlock.LOCKED) p.m_missile_type = (MissileType)c.missile;
            try { p.UpdateCurrentWeaponName(); p.UpdateCurrentMissileName(); } catch { }
        }

        // ------------------------------------------------------------ missile pickups (0.4.14)
        // 08:09 run: a joiner picked up a Devastator. The host unlocked it and added 1 (host copy), the unlock (RpcSetMissileLevel)
        // reached the joiner, but the ammo never did (olmod's sniper path for missile ammo didn't deliver), so the joiner had
        // Devastator unlocked with 0 rounds and the game wouldn't select it. The host now sends missile pickups to the joiner itself.
        public static void HostMissileAdded(Player p, MissileType mt, int delta)
        {
            if (p == null || p.isLocalPlayer || p.connectionToClient == null || delta <= 0) return;
            p.connectionToClient.Send(LNet.MissileGrant, new MissileGrantMsg { mt = (byte)mt, delta = delta });
            CoopLog.Write("COMBAT", "host: sent " + delta + " " + mt + " to netId=" + p.netId.Value);
        }

        public static void OnMissileGrant(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<MissileGrantMsg>();
                var p = GameManager.m_local_player;
                if (p == null) return;
                int i = m.mt; if (i < 0 || i >= p.m_missile_ammo.Length) return;
                if (p.m_missile_level[i] == WeaponUnlock.LOCKED) p.m_missile_level[i] = WeaponUnlock.LEVEL_0;
                int before = (int)p.m_missile_ammo[i];
                int max = 999; try { max = p.GetMaxMissileAmmo((MissileType)i); } catch { }
                p.m_missile_ammo[i] = Math.Min(max, before + m.delta);
                try { p.UpdateCurrentMissileName(); } catch { }
                CoopLog.Write("COMBAT", "joiner: picked up " + m.delta + " " + (MissileType)i + ": " + before + " -> " + (int)p.m_missile_ammo[i]);
            }
            catch (Exception ex) { CoopLog.Error("OnMissileGrant", ex); }
        }

        /// Write a loadout into a Player (joiner: its own; host: its copy of that joiner).
        static void Apply(Player target, LoadoutMsg m, bool local)
        {
            for (int i = 0; i < Math.Min(m.wlevel.Length, target.m_weapon_level.Length); i++) target.m_weapon_level[i] = (WeaponUnlock)m.wlevel[i];
            for (int i = 0; i < Math.Min(m.mlevel.Length, target.m_missile_level.Length); i++) target.m_missile_level[i] = (WeaponUnlock)m.mlevel[i];
            for (int i = 0; i < Math.Min(m.wpicked.Length, target.m_weapon_picked_up.Length); i++) target.m_weapon_picked_up[i] = m.wpicked[i];
            for (int i = 0; i < Math.Min(m.mammo.Length, target.m_missile_ammo.Length); i++) target.m_missile_ammo[i] = m.mammo[i];
            var uf = UnlockFields;
            for (int i = 0; i < Math.Min(m.unlocks.Length, uf.Length); i++) if (uf[i] != null) uf[i].SetValue(target, m.unlocks[i]);
            target.m_ammo = m.ammo;
            target.m_energy = m.energy;
            if (local)
            {
                // local player: switch through the normal (Command) path so the host hears it too
                target.m_weapon_type = (WeaponType)m.weapon; target.m_missile_type = (MissileType)m.missile;
                try { target.UpdateCurrentWeaponName(); target.UpdateCurrentMissileName(); } catch { }
            }
            else
            {
                target.Networkm_weapon_type = (WeaponType)m.weapon;
                target.Networkm_missile_type = (MissileType)m.missile;
            }
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
                Apply(target, m, false);
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

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class L4_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            Client.GetClient().RegisterHandler(LNet.MissileGrant, CoopLoadout.OnMissileGrant);
        }
    }

    // ===================================================================== olmod sniper-packet clock
    /// olmod's sniper packets (client-side shots) rate-check every shot on the server against NetworkMatch.m_match_elapsed_seconds
    /// (MPSniperPacketsServerHandlers.OnSniperPacket). That clock only advances in NetworkMatch.ProcessPlaying, which returns at once
    /// unless a multiplayer scene is loaded - never in a co-op campaign level. With the clock stuck the host dropped joiner shots as
    /// "client is bursting" (07:47 run: 278 dropped - 138 impulse, 70 missile pod, 32 hunter, 20 creeper, 18 falcon). Advance it here.
    /// Its other readers are MP-only (time limit inside ProcessPlaying, MP scoreboards, olmod race/stat log).
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class K1_MatchClock
    {
        static bool s_logged;
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || !GameplayManager.IsMultiplayerActive) return;
            try
            {
                if (Overload.NetworkManager.IsMultiplayerSceneLoaded()) return;
                NetworkMatch.m_match_elapsed_seconds += RUtility.FRAMETIME_GAME;
                if (!s_logged) { s_logged = true; CoopLog.Write("COMBAT", "co-op: advancing the match clock (olmod shot rate check)"); }
            }
            catch (Exception ex) { CoopLog.Error("K1", ex); }
        }
    }

    // ===================================================================== exit: ships pass through each other
    /// 07:47 run (user): a joiner going through the exit first clogs the pipe. During an exit/teleport sequence every player ship
    /// ignores collisions with every other player ship (on each peer; the level ends anyway).
    [HarmonyPatch]
    static class X1_ExitNoShipCollisions
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameplayManager), "ExitSequenceStart");
            yield return AccessTools.Method(typeof(GameplayManager), "TeleportSequenceStart");
        }
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try
            {
                var ships = new List<PlayerShip>();
                foreach (var p in Overload.NetworkManager.m_Players) if (p != null && p.c_player_ship != null) ships.Add(p.c_player_ship);
                int pairs = 0;
                for (int i = 0; i < ships.Count; i++)
                    for (int j = i + 1; j < ships.Count; j++)
                        foreach (var a in ships[i].GetComponentsInChildren<Collider>(true))
                            foreach (var b in ships[j].GetComponentsInChildren<Collider>(true))
                                if (a != null && b != null && !a.isTrigger && !b.isTrigger) { Physics.IgnoreCollision(a, b, true); pairs++; }
                CoopLog.Write("COMBAT", "exit: " + ships.Count + " ships pass through each other (" + pairs + " collider pairs)");
            }
            catch (Exception ex) { CoopLog.Error("X1", ex); }
        }
    }

    // ===================================================================== host diagnostics for joiner weapons/missiles
    [HarmonyPatch(typeof(Player), "UnlockMissile")]
    static class D1_LogUnlockMissile
    {
        static void Postfix(Player __instance, MissileType mt)
        { if (CoopConfig.Active && !__instance.isLocalPlayer && Server.IsActive()) CoopLog.Write("COMBAT", "host: netId=" + __instance.netId.Value + " unlocked missile " + mt + " level=" + __instance.m_missile_level[(int)mt]); }
    }

    [HarmonyPatch(typeof(Player), "AddMissileAmmo")]
    static class D2_MissilePickup
    {
        static void Prefix(Player __instance, MissileType mt, out int __state) { __state = (int)__instance.m_missile_ammo[(int)mt]; }
        static void Postfix(Player __instance, int amt, MissileType mt, int __state)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || __instance.isLocalPlayer || !Server.IsActive()) return;
            try
            {
                int now = (int)__instance.m_missile_ammo[(int)mt];
                CoopLog.Write("COMBAT", "host: netId=" + __instance.netId.Value + " +" + amt + " " + mt + " -> " + now);
                CoopLoadout.HostMissileAdded(__instance, mt, now - __state);
            }
            catch (Exception ex) { CoopLog.Error("D2", ex); }
        }
    }

    [HarmonyPatch(typeof(Player), "UnlockWeapon")]
    static class D3_LogUnlockWeapon
    {
        static void Postfix(Player __instance, WeaponType wt, bool __result)
        { if (CoopConfig.Active && !__instance.isLocalPlayer && Server.IsActive()) CoopLog.Write("COMBAT", "host: netId=" + __instance.netId.Value + " unlock weapon " + wt + " ok=" + __result); }
    }

    [HarmonyPatch(typeof(Player), "CmdSetCurrentMissile")]
    static class D4_LogSelectMissile
    {
        static void Postfix(Player __instance, MissileType missile_type)
        { if (CoopConfig.Active && !__instance.isLocalPlayer && Server.IsActive()) CoopLog.Write("COMBAT", "host: netId=" + __instance.netId.Value + " selected missile " + missile_type + " (level " + __instance.m_missile_level[(int)missile_type] + ", ammo " + (int)__instance.m_missile_ammo[(int)missile_type] + ")"); }
    }
}
