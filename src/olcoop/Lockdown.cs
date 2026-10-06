// Lockdowns and the boss/reactor health bar on joiners (0.6.9).
//  - ScriptLockdownMaster used to be host-only, so joiners never ran GameplayManager.LockdownBegin: no LOCKDOWN / TARGETS REMAINING
//    counter, no alarm (cues 361/351), no warning popup, and the lockdown doors stayed open on the joiner while they were closed on
//    the host (whose copy of the joiner's ship is authoritative: an invisible wall). Joiners now run the script themselves (doors,
//    popup, sounds, counter) with the robot side switched off (LockdownSpawnBot / LockdownUpdate / LockdownDestroyedBot), and the
//    host sends its lockdown state (msg 204): the counter follows the host's, a kill plays the stock bot-died sound, and the host's
//    LockdownEnd ends it here too. Boss lockdowns (ScriptLockdownBoss, already run on joiners since 0.4.6) keep their own count:
//    the boss copies' deaths end them and start the escape on each machine.
//  - The boss / reactor bar (UIElement.DrawHUD: ReactorShowHP, ReactorHPPct, ReactorName) is set where the boss takes damage (host)
//    and by each machine's Reactor.Update from its own copy (never damaged on joiners). The host's values come with msg 204 and are
//    applied on joiners right before the HUD draws.
using System;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.World
{
    public class LevelHudMsg : MessageBase
    {
        public bool lockActive, lockBoss, showHP, clear = true; public short remaining; public float hpPct; public string name = ""; public ushort script = 0xFFFF;
        public override void Serialize(NetworkWriter w)
        {
            w.Write((byte)((lockActive ? 1 : 0) | (lockBoss ? 2 : 0) | (showHP ? 4 : 0) | (clear ? 8 : 0))); w.Write(remaining); w.Write(hpPct); w.Write(name ?? ""); w.Write(script);
        }
        public override void Deserialize(NetworkReader r)
        {
            byte f = r.ReadByte(); lockActive = (f & 1) != 0; lockBoss = (f & 2) != 0; showHP = (f & 4) != 0; clear = (f & 8) != 0;
            remaining = r.ReadInt16(); hpPct = r.ReadSingle(); name = r.ReadString(); script = r.ReadUInt16();
        }
        public string Key() { return (clear ? "C" : "-") + (lockActive ? "L" : "-") + (lockBoss ? "B" : "-") + (showHP ? "H" : "-") + remaining + "|" + Mathf.RoundToInt(hpPct * 200f) + "|" + name + "|" + script; }
    }

    public static class CoopLockdown
    {
        public const short Msg = 204; // H->J LevelHudMsg: on change (max 10/s) and 1/s

        // ---------------------------------------------------------------- host
        static ushort s_script = 0xFFFF;
        /// Host: how its last lockdown ended (LockdownEnd(clear): false = the safety timer ran out, "EMERGENCY BACKUP").
        public static bool LastClear = true;
        static string s_last_key;
        static float s_next_any, s_next_change;
        static int s_logged;

        public static void ResetForLevel()
        {
            s_script = 0xFFFF; LastClear = true; s_last_key = null; s_next_any = 0f; s_next_change = 0f;
            s_have = false; s_applied_script = 0xFFFF; s_logged = 0;
        }

        /// Host: a lockdown script ran (W1 postfix): remember which, so late joiners can run the same one.
        public static void HostNoteScript(ScriptBase s) { ushort id; if (CoopWorld.TryScriptId(s, out id)) s_script = id; }

        public static void HostTick()
        {
            if (!CoopWorld.IsHost || !GameplayManager.LevelIsLoaded) return;
            float now = Time.realtimeSinceStartup;
            var m = new LevelHudMsg
            {
                lockActive = GameplayManager.LockdownActive, lockBoss = GameplayManager.LockdownBoss, remaining = (short)GameplayManager.LockdownRobotsRemaining,
                showHP = GameplayManager.ReactorShowHP, hpPct = GameplayManager.ReactorHPPct, name = GameplayManager.ReactorName ?? "",
                script = GameplayManager.LockdownActive ? s_script : (ushort)0xFFFF, clear = LastClear
            };
            string key = m.Key();
            bool changed = key != s_last_key;
            if (changed ? now < s_next_change : now < s_next_any) return;
            s_next_change = now + 0.1f; s_next_any = now + 1f;
            if (changed && s_logged++ < 60) CoopLog.Write("LOCK", "host: lockdown=" + m.lockActive + " boss=" + m.lockBoss + " remaining=" + m.remaining + " bar=" + (m.showHP ? m.name + " " + (m.hpPct * 100f).ToString("F0") + "%" : "off"));
            s_last_key = key;
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(Msg, m);
        }

        // ---------------------------------------------------------------- joiner
        static bool s_have; static LevelHudMsg s_hud; static float s_hud_time;
        static ushort s_applied_script = 0xFFFF;
        /// Joiner: true while we are applying the host's lockdown (lets LockdownEnd's own effects run).
        public static bool Applying;

        public static bool JoinerMirror { get { return CoopWorld.IsJoiner && CoopWorld.Matched; } }

        public static void OnMsg(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<LevelHudMsg>();
                s_hud = m; s_have = true; s_hud_time = Time.realtimeSinceStartup;
                if (!CoopWorld.Matched || !GameplayManager.LevelIsLoaded) return;
                Applying = true;
                try
                {
                    if (m.lockActive && !GameplayManager.LockdownActive && m.script != 0xFFFF && s_applied_script != m.script)
                    {
                        // our copy of the script didn't run (joined during the lockdown, or the script message came later): run it now
                        s_applied_script = m.script;
                        CoopLog.Write("LOCK", "joiner: host is in a lockdown we haven't started; running script " + m.script);
                        CoopWorld.ApplyScript(m.script, false);
                    }
                    if (m.lockBoss || GameplayManager.LockdownBoss)
                    {
                        // boss lockdowns: our own boss copies' deaths count down and start the escape here (LockdownBossDestroyed ->
                        // LockdownBossEnd); Goliath is two bosses, so overriding the count could end it early. Only the bar is mirrored.
                    }
                    else if (m.lockActive && GameplayManager.LockdownActive)
                    {
                        int before = GameplayManager.LockdownRobotsRemaining;
                        if (m.remaining < before)
                        {
                            try { SFXCueManager.PlayCue2D(SFXCue.hud_notify_bot_died); SFXCueManager.PlayCue2D(SFXCue.hud_notify_bot_died, 1f, -0.25f); } catch { }
                        }
                        if (before != m.remaining && s_logged++ < 60) CoopLog.Write("LOCK", "joiner: targets remaining " + before + " -> " + m.remaining + " (host)");
                        GameplayManager.LockdownRobotsRemaining = m.remaining;
                    }
                    else if (!m.lockActive && GameplayManager.LockdownActive)
                    {
                        CoopLog.Write("LOCK", "joiner: host ended the lockdown (" + (m.clear ? "area clear" : "safety timer") + ")");
                        GameplayManager.LockdownEnd(m.clear);
                    }
                }
                finally { Applying = false; }
            }
            catch (Exception ex) { CoopLog.Error("CoopLockdown.OnMsg", ex); }
        }

        /// Joiner, right before the HUD draws: the host's boss/reactor bar (our Reactor.Update and boss copy never see damage).
        public static void ApplyBar()
        {
            if (!s_have || !JoinerMirror || Time.realtimeSinceStartup - s_hud_time > 3f) return;
            GameplayManager.ReactorShowHP = s_hud.showHP;
            GameplayManager.ReactorHPPct = s_hud.hpPct;
            if (!string.IsNullOrEmpty(s_hud.name)) GameplayManager.ReactorName = s_hud.name;
        }
    }

    // ================================================================= patches

    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class LK0_Reset
    {
        static void Prefix() { if (CoopConfig.Active) CoopLockdown.ResetForLevel(); }
    }

    /// LK1-LK3: on joiners the host decides lockdown robots: no spawns, no timer-driven end, no local kill count.
    [HarmonyPatch(typeof(GameplayManager), "LockdownSpawnBot")]
    static class LK1_NoJoinerSpawns
    {
        static bool Prefix() { return !CoopLockdown.JoinerMirror; }
    }

    [HarmonyPatch(typeof(GameplayManager), "LockdownUpdate")]
    static class LK2_NoJoinerUpdate
    {
        static bool Prefix() { return !CoopLockdown.JoinerMirror; }
    }

    [HarmonyPatch(typeof(GameplayManager), "LockdownDestroyedBot")]
    static class LK3_NoJoinerCount
    {
        static bool Prefix() { return !CoopLockdown.JoinerMirror; }
    }

    /// LK7: host remembers how its lockdown ended, for the joiners' LockdownEnd.
    [HarmonyPatch(typeof(GameplayManager), "LockdownEnd")]
    static class LK7_HostEnd
    {
        static void Prefix(bool clear) { if (CoopWorld.IsHost) CoopLockdown.LastClear = clear; }
    }

    /// LK4: host state tick (runs in every gameplay state).
    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class LK4_HostTick
    {
        static void Postfix()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            try { CoopLockdown.HostTick(); } catch (Exception ex) { CoopLog.Error("LK4", ex); }
        }
    }

    /// LK5: joiner applies the host's boss/reactor bar right before the HUD reads it.
    [HarmonyPatch(typeof(UIElement), "DrawHUD")]
    static class LK5_BossBar
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() { try { CoopLockdown.ApplyBar(); } catch (Exception ex) { CoopLog.Error("LK5", ex); } }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class LK6_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (CoopConfig.IsJoiner && Client.GetClient() != null) Client.GetClient().RegisterHandler(CoopLockdown.Msg, CoopLockdown.OnMsg);
        }
    }
}
