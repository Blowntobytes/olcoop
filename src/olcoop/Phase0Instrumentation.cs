using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.Instrumentation
{
    /// <summary>
    /// Phase 0: read-only Harmony postfixes that log the game state we will later have to
    /// synchronise. Nothing here changes game behaviour; every patch swallows its own errors.
    /// </summary>
    public static class StateDump
    {
        static float s_next_dump;

        public static string Describe(Player p)
        {
            if (p == null) return "<null>";
            try
            {
                var ship = p.c_player_ship;
                string pos = ship != null ? ship.transform.position.ToString("F1") : "-";
                string seg = ship != null ? ship.SegmentIndex.ToString() : "-";
                return string.Format("{{name='{0}' netId={1} local={2} server={3} hp={4:F0} dying={5} dead={6} pos={7} seg={8}}}",
                    p.m_mp_name, p.netId, p.isLocalPlayer, p.isServer, (float)p.m_hitpoints,
                    ship != null && (bool)ship.m_dying, ship != null && (bool)ship.m_dead, pos, seg);
            }
            catch (Exception ex) { return "<err " + ex.Message + ">"; }
        }

        public static string LevelSummary()
        {
            var li = GameplayManager.m_level_info;
            if (li == null) return "level=<none>";
            string mission = li.Mission != null ? li.Mission.FileName + "/" + li.Mission.Type : "?";
            return string.Format("level={0} num={1} scene={2} mission={3} gameType={4} diff={5}",
                li.FileName, li.LevelNum, li.SceneName, mission, GameplayManager.m_game_type, (int)GameplayManager.DifficultyLevel);
        }

        public static string NetSummary()
        {
            return string.Format("netServer={0} netClient={1} isServerFn={2} headless={3} matchState={4} mpActive={5} isMP={6}",
                NetworkServer.active, NetworkClient.active, Overload.NetworkManager.IsServer(),
                Overload.NetworkManager.IsHeadless(), NetworkMatch.GetMatchState(),
                GameplayManager.IsMultiplayerActive, GameplayManager.IsMultiplayer);
        }

        public static string RobotSummary()
        {
            var list = RobotManager.m_master_robot_list;
            if (list == null) return "robots=<null>";
            int total = 0, active = 0, dying = 0, bosses = 0;
            float hp = 0f;
            var byType = new Dictionary<string, int>();
            foreach (var r in list)
            {
                if (r == null) continue;
                total++;
                if (r.gameObject.activeInHierarchy) active++;
                if (r.m_dying) dying++;
                if (r.m_is_boss) bosses++;
                hp += r.m_hp;
                string t = r.robot_type.ToString();
                int c; byType.TryGetValue(t, out c); byType[t] = c + 1;
            }
            var sb = new StringBuilder();
            foreach (var kv in byType) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(' ');
            return string.Format("robots total={0} active={1} dying={2} bosses={3} totalHp={4:F0} aiEnabled={5} killAll={6} killedMission={7} types=[{8}]",
                total, active, dying, bosses, hp, RobotManager.m_AI_enabled, RobotManager.m_kill_all_robots,
                GameplayManager.m_robots_killed_mission, sb.ToString().TrimEnd());
        }

        public static string ObjectiveSummary()
        {
            return string.Format("mustEscape={0} escapeTimer={1:F1} cryosLevel={2} cryosMission={3} gameTime={4:F1}",
                GameplayManager.MustEscape, GameplayManager.EscapeTimer, GameplayManager.m_cryos_picked_up,
                GameplayManager.m_cryos_picked_up_mission, (float)GameplayManager.m_game_time);
        }

        public static string PlayersSummary()
        {
            var sb = new StringBuilder();
            sb.Append("localPlayer=").Append(Describe(GameManager.m_local_player));
            var players = Overload.NetworkManager.m_Players;
            sb.Append(" netPlayers=").Append(players != null ? players.Count : -1);
            if (players != null)
                foreach (var p in players) sb.Append("\n      ").Append(Describe(p));
            return sb.ToString();
        }

        public static void Full(string reason)
        {
            try
            {
                CoopLog.Write("DUMP", reason +
                    "\n    gmState=" + GameManager.m_game_state + " gpState=" + GameplayManager.m_gameplay_state +
                    "\n    " + LevelSummary() +
                    "\n    " + NetSummary() +
                    "\n    " + PlayersSummary() +
                    "\n    " + RobotSummary() +
                    "\n    " + ObjectiveSummary());
            }
            catch (Exception ex) { CoopLog.Error("StateDump.Full", ex); }
        }

        public static void Tick()
        {
            if (!CoopConfig.LoggingEnabled) return;
            if (GameManager.m_game_state != GameManager.GameState.GAMEPLAY) return;
            if (GameplayManager.m_level_info == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < s_next_dump) return;
            s_next_dump = now + CoopConfig.DumpInterval;
            Full("periodic");
        }
    }

    // ---- periodic ----
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class P0_GameplayUpdate
    {
        static void Postfix() { try { StateDump.Tick(); } catch (Exception ex) { CoopLog.Error("Tick", ex); } }
    }

    // ---- state machine ----
    [HarmonyPatch(typeof(GameplayManager), "ChangeGameplayState")]
    static class P0_ChangeGameplayState
    {
        static void Prefix(GameplayState new_state)
        {
            try { CoopLog.Write("STATE", "gameplay " + GameplayManager.m_gameplay_state + " -> " + new_state); } catch { }
        }
    }

    [HarmonyPatch(typeof(NetworkMatch), "SetMatchState")]
    static class P0_SetMatchState
    {
        static void Prefix(MatchState state)
        {
            try { CoopLog.Write("STATE", "match " + NetworkMatch.GetMatchState() + " -> " + state); } catch { }
        }
    }

    // ---- level lifecycle ----
    [HarmonyPatch(typeof(GameplayManager), "CreateNewGame")]
    static class P0_CreateNewGame
    {
        static void Postfix(Mission mission, int level_num, bool saved_game)
        {
            try
            {
                CoopLog.Write("LEVEL", "CreateNewGame mission=" + (mission != null ? mission.FileName + "/" + mission.Type : "null") +
                    " level_num=" + level_num + " saved=" + saved_game + " -> gameType=" + GameplayManager.m_game_type);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class P0_LoadLevel
    {
        static void Prefix(LevelInfo level_info)
        {
            try { CoopLog.Write("LEVEL", "LoadLevel scene=" + (level_info != null ? level_info.SceneName : "null")); } catch { }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
    static class P0_StartLevel
    {
        static void Postfix() { StateDump.Full("StartLevel (post)"); }
    }

    [HarmonyPatch(typeof(Player), "OnStartLocalPlayer")]
    static class P0_OnStartLocalPlayer
    {
        static void Postfix(Player __instance) { try { CoopLog.Write("PLAYER", "OnStartLocalPlayer " + StateDump.Describe(__instance)); } catch { } }
    }

    [HarmonyPatch(typeof(Overload.NetworkManager), "LoadScene")]
    static class P0_NetLoadScene
    {
        static void Prefix(string name) { try { CoopLog.Write("NET", "NetworkManager.LoadScene " + name); } catch { } }
    }

    // ---- objectives / endings ----
    [HarmonyPatch(typeof(GameplayManager), "ReactorDestroyed")]
    static class P0_ReactorDestroyed
    {
        static void Postfix(bool long_countdown) { CoopLog.Write("OBJ", "ReactorDestroyed long=" + long_countdown); StateDump.Full("reactor"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "EscapeStart")]
    static class P0_EscapeStart
    {
        static void Postfix(float timer) { CoopLog.Write("OBJ", "EscapeStart timer=" + timer); }
    }

    [HarmonyPatch(typeof(GameplayManager), "Level12EscapeStart")]
    static class P0_Level12EscapeStart
    {
        static void Postfix() { CoopLog.Write("OBJ", "Level12EscapeStart"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "ExitSequenceStart")]
    static class P0_ExitSequenceStart
    {
        static void Postfix() { CoopLog.Write("OBJ", "ExitSequenceStart"); StateDump.Full("exit"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "TeleportSequenceStart")]
    static class P0_TeleportSequenceStart
    {
        static void Postfix(bool alien_warp) { CoopLog.Write("OBJ", "TeleportSequenceStart alien=" + alien_warp); StateDump.Full("teleport"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "EscapeLevel")]
    static class P0_EscapeLevel
    {
        static float s_last;
        // the joiner's blocked EscapeLevel is retried every frame while it waits for the host: log it at most once per 10 s
        static void Prefix() { if (Time.realtimeSinceStartup - s_last < 10f) return; s_last = Time.realtimeSinceStartup; CoopLog.Write("OBJ", "EscapeLevel"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
    static class P0_DoneLevel
    {
        static void Prefix(GameplayManager.DoneReason reason) { CoopLog.Write("OBJ", "DoneLevel reason=" + reason); StateDump.Full("doneLevel"); }
    }

    [HarmonyPatch(typeof(GameplayManager), "PlayerHasDied")]
    static class P0_PlayerHasDied
    {
        static void Prefix() { CoopLog.Write("OBJ", "PlayerHasDied"); }
    }

    // ---- combat ----
    [HarmonyPatch(typeof(Robot), "ExplodeNow")]
    static class P0_RobotExplode
    {
        static void Prefix(Robot __instance)
        {
            try
            {
                CoopLog.Write("ROBOT", "ExplodeNow type=" + __instance.robot_type + " boss=" + __instance.m_is_boss +
                    " pos=" + __instance.transform.position.ToString("F1"));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ApplyDamage")]
    static class P0_PlayerDamage
    {
        static void Prefix(PlayerShip __instance, DamageInfo di)
        {
            try
            {
                string src = di.robot_owner != null ? "robot:" + di.robot_owner.robot_type
                    : (di.owner != null ? "obj:" + di.owner.name : "?");
                CoopLog.Write("DMG", "player netId=" + (__instance.c_player != null ? __instance.c_player.netId.ToString() : "?") +
                    " dmg=" + di.damage.ToString("F1") + " from=" + src);
            }
            catch { }
        }
    }
}
