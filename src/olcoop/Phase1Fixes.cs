using System;
using System.Reflection;
using HarmonyLib;
using Overload;
using UnityEngine;

namespace OlCoop.Session
{
    /// <summary>
    /// Fixes for side effects of switching IsMultiplayerActive on inside a campaign game
    /// (found in the first two-instance test, 2026-10-03: host froze from per-frame exceptions).
    /// </summary>
    public static class CoopNetcodeSideEffects
    {
        /// Called whenever co-op netcode turns on. The MP quick-chat buffer is only initialised by the MP lobby;
        /// in a campaign game its colour slots default to non-NONE with null sender strings and the HUD throws every frame.
        static bool? s_saved_client_physics;

        public static void OnNetcodeOn()
        {
            NetworkMessageManager.ClearQCMessages();
            CoopLog.Write("NET", "cleared MP quick-chat buffer");
            // olmod's "client-side physics" input protocol only works against dedicated olmod servers ("cphysics" tweak);
            // a campaign host decodes the legacy input packet. Use legacy input on both peers while co-op is on.
            if (s_saved_client_physics == null) s_saved_client_physics = GameMod.MPServerOptimization.enabled;
            GameMod.MPServerOptimization.enabled = false;
            CoopLog.Write("NET", "olmod client-side physics OFF for co-op (was " + s_saved_client_physics + ")");
        }

        public static void OnNetcodeOff()
        {
            if (s_saved_client_physics == null) return;
            GameMod.MPServerOptimization.enabled = s_saved_client_physics.Value;
            s_saved_client_physics = null;
            CoopLog.Write("NET", "olmod client-side physics restored");
        }
    }

    /// olmod's client-physics sender (MPServerOptimization_FixedUpdateProcessControlsInternal.Postfix) assumes a ship that is
    /// both local and on a server never exists (olmod servers are dedicated). Our co-op host is exactly that, and its
    /// per-tick NRE aborted PlayerShip.FixedUpdateAll, so the joiner's ship was never simulated on the host.
    /// On the host the local ship needs no "send input to server" step, so skip it there.
    [HarmonyPatch]
    static class F1_OlmodPhysicsSendOnHost
    {
        public const string OlmodTarget = "GameMod.MPServerOptimization+MPServerOptimization_FixedUpdateProcessControlsInternal:Postfix";
        static Type s_type;

        static bool Prepare()
        {
            s_type = AccessTools.TypeByName("GameMod.MPServerOptimization+MPServerOptimization_FixedUpdateProcessControlsInternal");
            if (s_type == null) CoopLog.Write("INIT", "olmod MPServerOptimization physics postfix not found; F1 not needed");
            return s_type != null;
        }

        static MethodBase TargetMethod() { return AccessTools.Method(s_type, "Postfix"); }

        static bool Prefix()
        {
            if (!CoopConfig.Active || !Server.IsActive()) return true;
            var force = AccessTools.Field(s_type, "force");
            var torque = AccessTools.Field(s_type, "torque");
            if (force != null) force.SetValue(null, Vector3.zero);
            if (torque != null) torque.SetValue(null, Vector3.zero);
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "StartPreGame")]
    static class F2_ClearQcOnNetcodeOn
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || !GameplayManager.IsMultiplayerActive) return;
            try { CoopNetcodeSideEffects.OnNetcodeOn(); } catch (Exception ex) { CoopLog.Error("F2", ex); }
        }
    }

    /// MP detaches each ship's mesh collider in PlayerShip.Start (only when IsMultiplayer) and keeps it in sync every tick
    /// (PlayerShip.cs:3983 / olmod interpolation). In a campaign game that never happens, c_mesh_collider_trans stays null,
    /// and olmod's remote-ship interpolation throws every frame (run 3: joiner stuck loading).
    [HarmonyPatch(typeof(PlayerShip), "Start")]
    static class F3_DetachMeshColliderLikeMP
    {
        static void Postfix(PlayerShip __instance)
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            // Host keeps the single-player layout: robot line-of-sight (Robot.VisibilityRaycast) only counts a hit on the
            // ship's own GameObject, and a detached collider made every robot blind (Phase 2a run 2). Joiners need the MP
            // layout for olmod's remote-ship smoothing and run no robot AI.
            if (Server.IsActive()) return;
            if (__instance.c_mesh_collider == null || __instance.c_mesh_collider_trans != null) return;
            __instance.c_mesh_collider_trans = __instance.c_mesh_collider.transform;
            __instance.c_mesh_collider_trans.parent = null;
            CoopLog.Write("NET", "ship " + (__instance.c_player != null ? __instance.c_player.netId.ToString() : "?") + ": mesh collider detached (MP layout)");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "OnDestroy")]
    static class F4_CleanupDetachedCollider
    {
        static void Postfix(PlayerShip __instance)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            var t = __instance.c_mesh_collider_trans;
            if (t != null && t.parent == null) UnityEngine.Object.Destroy(t.gameObject);
        }
    }

    /// Safety net: in co-op, an exception in olmod's remote-ship smoothing must not abort GameManager.Update
    /// (which also runs the level/menu state machine). Swallow and log (rate-limited).
    [HarmonyPatch]
    static class F5_GuardOlmodShipReckoning
    {
        public const string OlmodTarget = "GameMod.MPClientShipReckoning:updatePlayerPositions";
        static int s_count;

        static bool Prepare() { return AccessTools.TypeByName("GameMod.MPClientShipReckoning") != null; }
        static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("GameMod.MPClientShipReckoning"), "updatePlayerPositions"); }

        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !CoopConfig.Active || GameplayManager.IsMultiplayer) return __exception;
            s_count++;
            if (s_count <= 5 || s_count % 1000 == 0) CoopLog.Write("ERROR", "olmod ship smoothing threw (#" + s_count + ", suppressed): " + __exception);
            return null;
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
    static class F6_RestoreOnLevelDone
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { CoopNetcodeSideEffects.OnNetcodeOff(); } catch (Exception ex) { CoopLog.Error("F6", ex); }
        }
    }

    /// Projectiles are created/collide under single-player rules in co-op. With IsMultiplayerActive the game makes every
    /// projectile team ENEMY and puts player shots on the MP projectile layer (Projectile.Fire, Projectile.cs:1360/1859),
    /// so player shots could not damage robots; ProcessCollision also skips waking robots (567). Damage tuning is SP too.
    public static class SpRulesScope
    {
        public static bool Enter()
        {
            if (!GameplayManager.IsMultiplayerActive || !CoopConfig.Active || GameplayManager.IsMultiplayer) return false;
            GameplayManager.IsMultiplayerActive = false;
            return true;
        }
        public static void Exit(bool entered) { if (entered) GameplayManager.IsMultiplayerActive = true; }
    }

    [HarmonyPatch(typeof(Projectile), "Fire")]
    static class F7_ProjectileFireSpRules
    {
        static void Prefix(out bool __state) { __state = SpRulesScope.Enter(); }
        static Exception Finalizer(bool __state, Exception __exception) { SpRulesScope.Exit(__state); return __exception; }
    }

    [HarmonyPatch(typeof(Projectile), "ProcessCollision")]
    static class F8_ProjectileCollisionSpRules
    {
        static void Prefix(out bool __state) { __state = SpRulesScope.Enter(); }
        static Exception Finalizer(bool __state, Exception __exception) { SpRulesScope.Exit(__state); return __exception; }
    }

    /// olmod's projectile extrapolation (MPClientExtrapolation.LerpProjectile, called from FireProjectile) dereferences
    /// c_proj.m_owner_player, which is null for robot shots, and uses legacy Network.isServer (always false under UNET).
    /// Its throw aborted Robot.MaybeFire before the fire timer reset, so the robot fired every frame (Phase 2a run 3).
    [HarmonyPatch]
    static class F9_OlmodLerpProjectileGuard
    {
        public const string OlmodTarget = "GameMod.MPClientExtrapolation:LerpProjectile";
        static bool Prepare() { return AccessTools.TypeByName("GameMod.MPClientExtrapolation") != null; }
        static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("GameMod.MPClientExtrapolation"), "LerpProjectile"); }
        static bool Prefix(Projectile c_proj)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return true;
            return c_proj != null && c_proj.m_owner_player != null && !Server.IsActive();
        }
    }

    /// The MP kill feed is drawn whenever IsMultiplayerActive and throws IndexOutOfRange on robot kills. Co-op has no kill feed.
    [HarmonyPatch(typeof(UIElement), "DrawRecentKillsMP")]
    static class F10_NoMpKillFeed
    {
        static bool Prefix() { return !(CoopConfig.Active && !GameplayManager.IsMultiplayer); }
    }
}
