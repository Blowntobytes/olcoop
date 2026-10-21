// 0.8.2: secret levels in co-op (user, 10-20: finding a secret level broke the game for all three players).
// Stock flow (GameplayManager): ScriptSecretLevel -> SwitchToSecret: save the main level to secret.dat, DoneLevel(Secret), queue the
// secret level from Mission.SecretLevels (NOT Mission.Levels), InitCMLevelForSecret (countdown challenge rules), PLAY_GAME.
// The secret level ends with a teleport out (time up, or the "quick exit" instead of dying); TeleportSequenceUpdate then calls
// ReturnFromSecret: DoneLevel(Secret), load secret.dat, PLAY_GAME_RETURN_FROM_SECRET (the main level continues where it was).
// In co-op (20:22 run, 0.8.1): the host's ready gate held the secret level for joiners who were never told anything, and a joiner
// could not have loaded it anyway (C1 looks the scene up in Mission.Levels only).
// Now: the host tells joiners (status 5) and skips the ready gate for both switches; joiners load the secret level from
// SecretLevels, never run ReturnFromSecret themselves (they have no secret.dat), and come back with the host's next scene.
// A joiner touching the secret trigger asks the host (ExitRequest kind 3), like the shared exits.
using System;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.World
{
    public static class CoopSecret
    {
        static readonly System.Reflection.MethodInfo m_set_pending = AccessTools.Method(typeof(GameplayManager), "SetPendingLevelLoad");
        static readonly System.Reflection.FieldInfo f_player_data = AccessTools.Field(typeof(GameplayManager), "m_player_data");
        static readonly System.Reflection.FieldInfo f_secret_levels = AccessTools.Field(typeof(Mission), "SecretLevels");
        static bool s_requested;
        public static void ResetForLevel() { s_requested = false; }

        public static LevelInfo FindSecret(Mission m, string name)
        {
            if (m == null || f_secret_levels == null) return null;
            var list = f_secret_levels.GetValue(m) as System.Collections.Generic.List<LevelInfo>;
            if (list == null) return null;
            foreach (var li in list) if (li != null && string.Compare(li.FileName, name, true) == 0) return li;
            return null;
        }

        /// Joiner, C1: the host's scene is a secret level of the campaign. Same steps as SwitchToSecret, without secret.dat.
        public static bool TryLoad(string name, bool inLevel)
        {
            var story = GameManager.StoryMission;
            if (FindSecret(story, name) == null) return false;
            CoopLog.Write("JOIN", "loading host's SECRET level '" + name + "'" + (inLevel ? " from inside a level" : ""));
            try { OlCoop.Combat.CoopLoadout.CaptureForNextLevel("secret level"); } catch (Exception ex) { CoopLog.Error("secret capture", ex); }
            if (inLevel)
            {
                try { if (f_player_data != null && GameManager.m_local_player != null) f_player_data.SetValue(null, GameManager.m_local_player.Serialize()); } catch (Exception ex) { CoopLog.Error("secret player data", ex); }
                OlCoop.Death.Spectate.Stop(null);
                GameplayManager.DoneLevel(GameplayManager.DoneReason.Quit);
            }
            UIManager.DestroyAll(true);
            if (OlCoop.Robots.CoopRobots.HostDifficulty >= 0) GameplayManager.DifficultyLevel = OlCoop.Robots.CoopRobots.HostDifficulty;
            MenuManager.m_selected_mission = story;
            var li = story.OpenSecretLevel(name);
            m_set_pending.Invoke(null, new object[] { li });
            GameplayManager.InitCMLevelForSecret();
            PostLevel.AllowPlay = true;
            PostLevel.ManualReady = false;
            if (inLevel) { GameplayManager.m_between_level_start = Time.realtimeSinceStartup; GameplayManager.SwitchToMenu(MenuState.PLAY_GAME); }
            else MenuManager.ChangeMenuState(MenuState.PLAY_GAME);
            return true;
        }

        /// Joiner: status 5 - the host is switching to (or back from) a secret level.
        public static void OnHostSwitch()
        {
            bool inSecret = GameplayManager.Level != null && GameplayManager.Level.IsSecret;
            string t = inSecret ? "SECRET LEVEL OVER - RETURNING WITH THE HOST..." : "THE TEAM FOUND A SECRET LEVEL - FOLLOWING THE HOST...";
            CoopStatus.Set(2, t);
            GameplayManager.AddHUDMessage("CO-OP: " + t, -1, true);
            PostLevel.HostWaiting = false; PostLevel.ClearOffer();
            if (!Session.CoopClient.Awaiting) Session.CoopClient.AwaitLevel();
            CoopLog.Write("FLOW", "joiner: host is " + (inSecret ? "returning from" : "switching to") + " a secret level; waiting for its scene");
        }

        /// Joiner reached the secret trigger: ask the host (once per level).
        public static void JoinerRequest()
        {
            if (s_requested) return;
            var c = Client.GetClient();
            if (c == null || !Client.IsConnected()) return;
            s_requested = true;
            c.Send(FNet.ExitRequest, new IntegerMessage(FNet.KindSecret));
            GameplayManager.AddHUDMessage("CO-OP: SECRET FOUND - THE WHOLE TEAM GOES", -1, true);
            CoopLog.Write("FLOW", "joiner: reached the secret level trigger; asked the host to take everyone there");
        }

        /// Host: a joiner reached the secret trigger.
        public static void HostRequest(int conn)
        {
            var lvl = GameplayManager.Level;
            if (lvl == null || lvl.IsSecret || lvl.SecretLevel == null || GameplayManager.m_gameplay_state != GameplayState.PLAYING)
            {
                CoopLog.Write("FLOW", "host: joiner conn " + conn + " reached a secret trigger; ignored (level " + (lvl == null ? "none" : lvl.FileName) + " secret=" + (lvl != null && lvl.IsSecret) + " state=" + GameplayManager.m_gameplay_state + ")");
                return;
            }
            CoopLog.Write("FLOW", "host: joiner conn " + conn + " found the secret level; everyone goes");
            GameplayManager.SwitchToSecret(null);
        }
    }

    /// SW1: joiners never switch to a secret level on their own (their trigger may run when their world doesn't match the host's).
    [HarmonyPatch(typeof(GameplayManager), "SwitchToSecret")]
    static class SW1_JoinerSwitchToSecret
    {
        static bool Prefix()
        {
            if (!CoopWorld.Active) return true;
            if (CoopWorld.IsHost)
            {
                try { CoopWorld.StashForSecret(GameplayManager.Level != null ? GameplayManager.Level.FileName : "?"); } catch (Exception ex) { CoopLog.Error("SW1 stash", ex); }
                CoopLog.Write("FLOW", "host: switching to the secret level (" + (GameplayManager.Level != null ? GameplayManager.Level.SecretLevel : "?") + ")");
                return true;
            }
            if (!CoopConfig.IsJoiner) return true;
            try { CoopSecret.JoinerRequest(); } catch (Exception ex) { CoopLog.Error("SW1", ex); }
            return false;
        }
    }

    /// SW2: a joiner's own end of the secret level (teleport out) would load secret.dat - a file only the host has. Wait for the host.
    [HarmonyPatch(typeof(GameplayManager), "ReturnFromSecret")]
    static class SW2_JoinerNoReturnFromSecret
    {
        static bool s_logged;
        static bool Prefix()
        {
            if (!CoopWorld.Active) return true;
            if (CoopWorld.IsHost) { s_logged = false; CoopLog.Write("FLOW", "host: secret level over; returning to the main level"); return true; }
            if (!CoopConfig.IsJoiner) return true;
            try
            {
                UIManager.SetScreenFade(1f);
                var ship = GameManager.m_player_ship;
                if (ship != null && ship.c_rigidbody != null) { ship.c_rigidbody.velocity = Vector3.zero; ship.c_rigidbody.angularVelocity = Vector3.zero; }
                if (!s_logged)
                {
                    s_logged = true;
                    CoopStatus.Set(2, "SECRET LEVEL OVER - WAITING FOR THE HOST");
                    CoopLog.Write("FLOW", "joiner: our secret level teleport finished; not loading secret.dat (host's); screen held black until the host's level");
                }
            }
            catch (Exception ex) { CoopLog.Error("SW2", ex); }
            return false;
        }
        public static void Reset() { s_logged = false; }
    }

    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class SW3_Reset
    {
        static void Prefix() { CoopSecret.ResetForLevel(); SW2_JoinerNoReturnFromSecret.Reset(); }
    }
}
