using System;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.World
{
    /// <summary>
    /// Phase 2b, part 2: audio logs, exiting together, other players on the automap.
    ///  - Audio logs (LOG_ENTRY items): GameplayManager.PickupLogEntry only runs on the host (real pickups are server-only in co-op).
    ///    The host tells joiners, and they run the same PickupLogEntry (same next-log counter, chime, voice/text queue).
    ///  - Exits: DoorExit only reacts to the machine's own ship, and a joiner used to finish the level alone. Now joiners never exit by
    ///    themselves: reaching an exit door or alien warp asks the host; the host starts its exit/teleport sequence and every joiner plays
    ///    the same one. Joiners then wait at the end of the sequence for the host's next level (their own EscapeLevel is blocked).
    ///  - Automap: Automap.Open/Update swap the local ship for its map icon (PlayerShip.AutomapOn/Off); do the same for other ships.
    /// </summary>
    public static class FNet
    {
        public const short LogEntry = 184;    // H->J (empty)
        public const short ExitRequest = 185; // J->H byte kind
        public const short Exit = 186;        // H->J PoseMsg: kind + where to put your ship before the exit sequence
        public const short Teleport = 187;    // H->J PoseMsg: regroup (lockdown) - move your ship here
        public const byte KindDoor = 0, KindWarp = 1, KindTeleport = 2;
    }

    public class PoseMsg : MessageBase
    {
        public byte kind; public Vector3 pos; public Quaternion rot;
        public override void Serialize(NetworkWriter w) { w.Write(kind); w.Write(pos); w.Write(rot); }
        public override void Deserialize(NetworkReader r) { kind = r.ReadByte(); pos = r.ReadVector3(); rot = r.ReadQuaternion(); }
    }

    public static class CoopFlow
    {
        static readonly System.Reflection.FieldInfo f_next_log = AccessTools.Field(typeof(GameplayManager), "m_next_log_entry");
        static string NextLog() { try { return f_next_log != null ? f_next_log.GetValue(null).ToString() : "?"; } catch { return "?"; } }
        static int s_pending_exit = -1;
        static PoseMsg s_pending_pose;
        static PlayerShip s_exit_anchor;
        static PlayerShip s_last_trigger_ship; static float s_last_trigger_time = -100f;
        static readonly System.Collections.Generic.HashSet<int> s_lockdowns_done = new System.Collections.Generic.HashSet<int>();

        // ------------------------------------------------------------ placing ships next to another player
        public static void MoveShip(PlayerShip s, Vector3 p, Quaternion r)
        {
            if (s == null) return;
            s.c_transform.position = p; s.c_transform.rotation = r;
            if (s.c_rigidbody != null) { s.c_rigidbody.position = p; s.c_rigidbody.rotation = r; s.c_rigidbody.velocity = Vector3.zero; s.c_rigidbody.angularVelocity = Vector3.zero; }
        }

        /// A free spot next to the anchor ship (same checks as joiner spawns: inside the level, line of sight, room).
        static LevelData.SpawnPoint SpotNear(PlayerShip anchor, int idx)
        {
            var t = anchor.c_transform;
            var sp = new LevelData.SpawnPoint(t.position - t.forward * 4f, t.rotation, 0);
            Session.CoopHost.TryAroundPublic("teammate", t.position, t.rotation, idx, ref sp);
            return sp;
        }

        static bool Alive(PlayerShip s) { return s != null && !(bool)s.m_dying && !(bool)s.m_dead; }

        public static void NoteTrigger(Collider other)
        {
            if (other == null) return;
            var ps = other.GetComponentInParent<PlayerShip>();
            if (ps == null) return;
            s_last_trigger_ship = ps; s_last_trigger_time = Time.time;
        }

        /// Host: bring every other living player next to the anchor. Host moves its own ship directly; joiners get a Teleport/Exit pose.
        static void Regroup(PlayerShip anchor, short msgType, byte kind, float minDist, string why)
        {
            int idx = 0, moved = 0;
            foreach (var p in Overload.NetworkManager.m_Players)
            {
                if (p == null || p.c_player_ship == null) continue;
                var ship = p.c_player_ship;
                if (ship == anchor)
                {
                    if (msgType == FNet.Exit && !p.isLocalPlayer && p.connectionToClient != null)
                        p.connectionToClient.Send(msgType, new PoseMsg { kind = kind, pos = ship.c_transform.position, rot = ship.c_transform.rotation });
                    continue;
                }
                if (!Alive(ship)) continue;
                if (minDist > 0f && (ship.c_transform.position - anchor.c_transform.position).magnitude < minDist) continue;
                var sp = SpotNear(anchor, idx++);
                MoveShip(ship, sp.position, sp.orientation);
                if (!p.isLocalPlayer && p.connectionToClient != null)
                    p.connectionToClient.Send(msgType, new PoseMsg { kind = kind, pos = sp.position, rot = sp.orientation });
                moved++;
                CoopLog.Write("FLOW", "host: " + why + ": moved netId=" + p.netId.Value + " next to netId=" + anchor.c_player.netId.Value + " at " + sp.position.ToString("F1"));
            }
            if (msgType == FNet.Exit)
            {
                // joiners that were not moved (dead, or the anchor itself handled above) still need the exit message
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && !p.isLocalPlayer && p.connectionToClient != null && p.c_player_ship != null && p.c_player_ship != anchor && !Alive(p.c_player_ship))
                        p.connectionToClient.Send(msgType, new PoseMsg { kind = kind, pos = anchor.c_transform.position, rot = anchor.c_transform.rotation });
            }
            if (msgType == FNet.Teleport && moved > 0) GameplayManager.AddHUDMessage("CO-OP: LOCKDOWN - TEAM REGROUPED", -1, true);
        }

        /// Host: a lockdown started (ScriptLockdownMaster / ScriptLockdownBoss). Teleport everyone else next to whoever triggered it.
        public static void HostLockdown(ScriptBase s)
        {
            if (!s_lockdowns_done.Add(s.GetInstanceID())) return;
            PlayerShip anchor = (Time.time - s_last_trigger_time < 10f && Alive(s_last_trigger_ship)) ? s_last_trigger_ship : null;
            if (anchor == null)
            {
                float best = float.MaxValue;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && Alive(p.c_player_ship))
                    {
                        float d = (p.c_player_ship.c_transform.position - s.transform.position).sqrMagnitude;
                        if (d < best) { best = d; anchor = p.c_player_ship; }
                    }
            }
            if (anchor == null) { CoopLog.Write("FLOW", "host: lockdown " + s.GetType().Name + " but no living player to regroup at"); return; }
            CoopLog.Write("FLOW", "host: lockdown " + s.GetType().Name + " '" + s.gameObject.name + "' triggered near netId=" + anchor.c_player.netId.Value + "; regrouping");
            Regroup(anchor, FNet.Teleport, 0, 25f, "lockdown");
        }

        public static void OnTeleport(NetworkMessage msg)
        {
            try
            {
                var m = msg.ReadMessage<PoseMsg>();
                var me = GameManager.m_player_ship;
                if (!Alive(me)) return;
                MoveShip(me, m.pos, m.rot);
                GameplayManager.AddHUDMessage("CO-OP: LOCKDOWN - TELEPORTED TO YOUR TEAMMATE", -1, true);
                CoopLog.Write("FLOW", "joiner: lockdown regroup, moved to " + m.pos.ToString("F1"));
            }
            catch (Exception ex) { CoopLog.Error("OnTeleport", ex); }
        }
        public static bool ApplyingExit, ApplyingLog;
        static bool s_requested, s_exit_sent, s_wait_shown;

        public static void ResetForLevel() { s_lockdowns_done.Clear(); s_last_trigger_ship = null; s_exit_anchor = null; s_pending_pose = null; s_pending_exit = -1; s_requested = false; s_exit_sent = false; s_wait_shown = false; ApplyingExit = false; ApplyingLog = false; }

        static bool LocalAlive()
        {
            var s = GameManager.m_player_ship;
            return s != null && !(bool)s.m_dying && !(bool)s.m_dead;
        }

        static void SendAll(short type, MessageBase m)
        {
            foreach (var c in NetworkServer.connections)
                if (c != null && c.connectionId != 0 && c.isConnected && Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(type, m);
        }

        static string KindName(byte k) { return k == FNet.KindDoor ? "exit door" : k == FNet.KindWarp ? "alien warp" : "teleport"; }

        // ------------------------------------------------------------ audio logs
        public static void HostLogPicked()
        {
            SendAll(FNet.LogEntry, new EmptyMessage());
            CoopLog.Write("FLOW", "host: audio log picked up (next log now " + NextLog() + "); sent to joiners");
        }

        public static void OnLogEntry(NetworkMessage msg)
        {
            if (!CoopWorld.Matched) return;
            ApplyingLog = true;
            try { GameplayManager.PickupLogEntry(); CoopLog.Write("FLOW", "joiner: audio log from host played (next log now " + NextLog() + ")"); }
            catch (Exception ex) { CoopLog.Error("OnLogEntry", ex); }
            finally { ApplyingLog = false; }
        }

        // ------------------------------------------------------------ exits
        /// Joiner reached an exit/warp: ask the host (once per level).
        public static void JoinerRequestExit(byte kind)
        {
            if (s_requested) return;
            var c = Client.GetClient();
            if (c == null || !Client.IsConnected()) return;
            s_requested = true;
            c.Send(FNet.ExitRequest, new IntegerMessage(kind));
            GameplayManager.AddHUDMessage("CO-OP: EXIT REACHED - EVERYONE LEAVES TOGETHER", -1, true);
            CoopLog.Write("FLOW", "joiner: reached " + KindName(kind) + "; asked the host to end the level for everyone");
        }

        /// Host: a joiner reached an exit. Run the same exit here (which broadcasts it via the postfixes below).
        public static void OnExitRequest(NetworkMessage msg)
        {
            try
            {
                byte kind = (byte)msg.ReadMessage<IntegerMessage>().value;
                CoopLog.Write("FLOW", "host: joiner conn " + msg.conn.connectionId + " reached " + KindName(kind) + "; gameplay=" + GameplayManager.m_gameplay_state + " hostAlive=" + LocalAlive());
                if (GameplayManager.m_gameplay_state == GameplayState.EXIT || s_exit_sent) return;
                if (!LocalAlive())
                {
                    // ExitSequenceStart refuses a dying/dead ship (e.g. host spectating): send the exit to joiners and finish the level directly.
                    SendExit(kind);
                    CoopLog.Write("FLOW", "host: dead/spectating, finishing the level without the exit flight");
                    GameplayManager.EscapeLevel();
                    return;
                }
                PlayerShip anchor = null;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && p.connectionToClient != null && p.connectionToClient.connectionId == msg.conn.connectionId) anchor = p.c_player_ship;
                s_exit_anchor = anchor;
                if (kind == FNet.KindDoor) GameplayManager.ExitSequenceStart();
                else GameplayManager.TeleportSequenceStart(kind == FNet.KindWarp);
            }
            catch (Exception ex) { CoopLog.Error("OnExitRequest", ex); }
            finally { s_exit_anchor = null; }
        }

        /// Host: send the exit to joiners. Everyone is first put next to the player who reached the exit, so every exit/teleport
        /// sequence plays from the same spot and looks the same for everybody.
        public static void SendExit(byte kind)
        {
            if (s_exit_sent) return;
            s_exit_sent = true;
            var anchor = s_exit_anchor != null ? s_exit_anchor : GameManager.m_player_ship;
            if (anchor == null) { SendAll(FNet.Exit, new PoseMsg { kind = kind }); return; }
            Regroup(anchor, FNet.Exit, kind, 0f, KindName(kind));
            CoopLog.Write("FLOW", "host: " + KindName(kind) + " sequence started at netId=" + anchor.c_player.netId.Value + "; told joiners to exit too");
        }

        /// Host, joiner-triggered exit: put the host's own ship next to that joiner before the host's exit sequence starts.
        public static void HostPlaceSelfForExit()
        {
            var me = GameManager.m_player_ship;
            if (s_exit_anchor == null || me == null || s_exit_anchor == me || !Alive(me)) return;
            var sp = SpotNear(s_exit_anchor, 0);
            MoveShip(me, sp.position, sp.orientation);
            CoopLog.Write("FLOW", "host: moved own ship next to netId=" + s_exit_anchor.c_player.netId.Value + " for the exit");
        }

        /// Joiner: the host is exiting; play the same exit here.
        public static void OnExit(NetworkMessage msg)
        {
            try
            {
                var pm = msg.ReadMessage<PoseMsg>();
                byte kind = pm.kind;
                CoopLog.Write("FLOW", "joiner: host started " + KindName(kind) + "; gameplay=" + GameplayManager.m_gameplay_state + " alive=" + LocalAlive() + " moveTo=" + pm.pos.ToString("F1"));
                if (GameplayManager.m_gameplay_state == GameplayState.EXIT) return;
                if (!LocalAlive()) { ShowWaiting(); return; }
                if (GameplayManager.m_gameplay_state != GameplayState.PLAYING)
                {
                    // map or menu open: start the exit as soon as the player is back in normal play
                    s_pending_exit = kind; s_pending_pose = pm;
                    GameplayManager.AddHUDMessage("CO-OP: THE TEAM IS LEAVING THE LEVEL", -1, true);
                    return;
                }
                if (pm.pos != Vector3.zero) MoveShip(GameManager.m_player_ship, pm.pos, pm.rot);
                RunExit(kind);
            }
            catch (Exception ex) { CoopLog.Error("OnExit", ex); }
        }

        static void RunExit(byte kind)
        {
            ApplyingExit = true;
            try
            {
                if (kind == FNet.KindDoor) GameplayManager.ExitSequenceStart();
                else GameplayManager.TeleportSequenceStart(kind == FNet.KindWarp);
            }
            finally { ApplyingExit = false; }
        }

        /// Joiner tick: run a deferred exit once back in normal play.
        public static void Tick()
        {
            if (s_pending_exit < 0 || GameplayManager.m_gameplay_state != GameplayState.PLAYING) return;
            byte k = (byte)s_pending_exit; s_pending_exit = -1;
            if (!LocalAlive()) { ShowWaiting(); return; }
            CoopLog.Write("FLOW", "joiner: running the deferred " + KindName(k));
            if (s_pending_pose != null && s_pending_pose.pos != Vector3.zero) MoveShip(GameManager.m_player_ship, s_pending_pose.pos, s_pending_pose.rot);
            RunExit(k);
        }

        public static void ShowWaiting()
        {
            if (s_wait_shown) return;
            s_wait_shown = true;
            GameplayManager.AddHUDMessage("LEVEL COMPLETE - WAITING FOR THE HOST", -1, true);
            CoopLog.Write("FLOW", "joiner: level complete; waiting for the host's next level (own EscapeLevel blocked)");
        }

        public static bool IsLocalShip(Collider other)
        {
            if (other == null) return false;
            var ps = other.GetComponentInParent<PlayerShip>();
            if (ps == null && other.attachedRigidbody != null) ps = other.attachedRigidbody.GetComponent<PlayerShip>();
            return ps != null && ps.isLocalPlayer;
        }
    }

    // ================================================================= patches

    [HarmonyPatch(typeof(LevelData), "Awake")]
    static class F0_Reset
    {
        static void Prefix() { if (CoopConfig.Active) CoopFlow.ResetForLevel(); }
    }

    /// F1: audio log picked up on the host -> joiners play it too.
    [HarmonyPatch(typeof(GameplayManager), "PickupLogEntry")]
    static class F1_LogEntry
    {
        static void Postfix()
        {
            if (!CoopWorld.IsHost) return;
            try { CoopFlow.HostLogPicked(); } catch (Exception ex) { CoopLog.Error("F1", ex); }
        }
    }

    /// F2: exit door. Joiners never exit on their own: they ask the host.
    [HarmonyPatch(typeof(DoorExit), "OnTriggerEnter")]
    static class F2_DoorExit
    {
        static bool Prefix(Collider other)
        {
            if (!CoopWorld.IsJoiner) return true;
            try { if (CoopFlow.IsLocalShip(other) && GameplayManager.m_gameplay_state == GameplayState.PLAYING) CoopFlow.JoinerRequestExit(FNet.KindDoor); }
            catch (Exception ex) { CoopLog.Error("F2", ex); }
            return false;
        }
    }

    /// F3: alien warp. Stock reacts to ANY ship; on joiners ask the host instead.
    [HarmonyPatch(typeof(AlienWarp), "OnTriggerEnter")]
    static class F3_AlienWarp
    {
        static bool Prefix(AlienWarp __instance, Collider other)
        {
            if (!CoopWorld.IsJoiner) return true;
            try { if (__instance.m_active && CoopFlow.IsLocalShip(other) && GameplayManager.m_gameplay_state == GameplayState.PLAYING) CoopFlow.JoinerRequestExit(FNet.KindWarp); }
            catch (Exception ex) { CoopLog.Error("F3", ex); }
            return false;
        }
    }

    /// F4: host started an exit flight -> joiners follow (everyone placed next to whoever reached the exit).
    [HarmonyPatch(typeof(GameplayManager), "ExitSequenceStart")]
    static class F4_ExitStart
    {
        static void Prefix() { if (CoopWorld.IsHost) { try { CoopFlow.HostPlaceSelfForExit(); } catch (Exception ex) { CoopLog.Error("F4 pre", ex); } } }
        static void Postfix()
        {
            if (!CoopWorld.IsHost || GameplayManager.m_gameplay_state != GameplayState.EXIT) return;
            try { CoopFlow.SendExit(FNet.KindDoor); } catch (Exception ex) { CoopLog.Error("F4", ex); }
        }
    }

    /// F5: host started a teleport (alien warp, or ScriptTeleportOut) -> joiners follow.
    [HarmonyPatch(typeof(GameplayManager), "TeleportSequenceStart")]
    static class F5_TeleportStart
    {
        static void Prefix() { if (CoopWorld.IsHost) { try { CoopFlow.HostPlaceSelfForExit(); } catch (Exception ex) { CoopLog.Error("F5 pre", ex); } } }
        static void Postfix(bool alien_warp)
        {
            if (!CoopWorld.IsHost || GameplayManager.m_gameplay_state != GameplayState.EXIT) return;
            try { CoopFlow.SendExit(alien_warp ? FNet.KindWarp : FNet.KindTeleport); } catch (Exception ex) { CoopLog.Error("F5", ex); }
        }
    }

    /// F6: joiners don't finish the level themselves; they wait for the host's next level.
    [HarmonyPatch(typeof(GameplayManager), "EscapeLevel")]
    static class F6_JoinerNoEscape
    {
        static bool Prefix()
        {
            if (!CoopWorld.IsJoiner) return true;
            CoopFlow.ShowWaiting();
            return false;
        }
    }

    /// F7/F8: other players' map icons while the local map is open.
    [HarmonyPatch(typeof(PlayerShip), "AutomapOn")]
    static class F7_AutomapOn
    {
        static bool s_in;
        static void Postfix(PlayerShip __instance)
        {
            if (s_in || !__instance.isLocalPlayer || !CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            s_in = true;
            try
            {
                int n = 0;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && p.c_player_ship != null && p.c_player_ship != __instance && !(bool)p.c_player_ship.m_dead) { p.c_player_ship.AutomapOn(); n++; }
                CoopLog.Write("FLOW", "automap: showing " + n + " other player(s)");
            }
            catch (Exception ex) { CoopLog.Error("F7", ex); }
            finally { s_in = false; }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "AutomapOff")]
    static class F8_AutomapOff
    {
        static bool s_in;
        static void Postfix(PlayerShip __instance)
        {
            if (s_in || !__instance.isLocalPlayer || !CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            s_in = true;
            try
            {
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && p.c_player_ship != null && p.c_player_ship != __instance) p.c_player_ship.AutomapOff();
            }
            catch (Exception ex) { CoopLog.Error("F8", ex); }
            finally { s_in = false; }
        }
    }

    /// F12: remember which ship last set off a trigger (host) - the lockdown regroup point.
    [HarmonyPatch(typeof(TriggerBase), "OnTrigger")]
    static class F12_NoteTrigger
    {
        static void Prefix(Collider other) { if (CoopWorld.IsHost) { try { CoopFlow.NoteTrigger(other); } catch { } } }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class F11_Tick
    {
        static void Postfix()
        {
            if (!CoopWorld.IsJoiner) return;
            try { CoopFlow.Tick(); } catch (Exception ex) { CoopLog.Error("F11", ex); }
        }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class F9_ServerHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsHost) return;
            NetworkServer.RegisterHandler(FNet.ExitRequest, CoopFlow.OnExitRequest);
        }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class F10_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.IsJoiner || Client.GetClient() == null) return;
            var c = Client.GetClient();
            c.RegisterHandler(FNet.LogEntry, CoopFlow.OnLogEntry);
            c.RegisterHandler(FNet.Exit, CoopFlow.OnExit);
            c.RegisterHandler(FNet.Teleport, CoopFlow.OnTeleport);
        }
    }
}
