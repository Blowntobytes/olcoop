using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.World
{
    /// <summary>
    /// Level flow: audio logs, exiting together, other players on the automap.
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
        public const short Ready = 189;       // J->H IntegerMessage 1: end-of-level screens done, ready for the next level
        public const short Status = 188;      // H->J IntegerMessage: 1 = host on the level summary, 2 = host loading the next level
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
        /// A free spot next to the anchor for `ship` (never on top of another ship). False = none; then the ship is not moved.
        static bool SpotNear(PlayerShip anchor, PlayerShip ship, int idx, out LevelData.SpawnPoint sp, bool exitLane = false)
        {
            var t = anchor.c_transform;
            sp = new LevelData.SpawnPoint(t.position, t.rotation, 0);
            Session.CoopHost.IgnoreShip = ship;
            try
            {
                if (exitLane && Session.CoopHost.TryLane("exiting netId=" + anchor.c_player.netId.Value, t.position, t.rotation, ref sp)) return true;
                return Session.CoopHost.TryAroundPublic("teammate", t.position, t.rotation, idx, ref sp);
            }
            finally { Session.CoopHost.IgnoreShip = null; }
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
                if (!Alive(ship))
                {
                    if (msgType == FNet.Exit) QueueRevive(ship);
                    continue;
                }
                if (minDist > 0f && (ship.c_transform.position - anchor.c_transform.position).magnitude < minDist) continue;
                LevelData.SpawnPoint sp;
                if (!SpotNear(anchor, ship, idx++, out sp, msgType == FNet.Exit))
                {
                    CoopLog.Write("FLOW", "host: " + why + ": no free spot next to netId=" + anchor.c_player.netId.Value + "; netId=" + p.netId.Value + " stays where it is");
                    if (msgType == FNet.Exit && !p.isLocalPlayer && p.connectionToClient != null)
                        p.connectionToClient.Send(msgType, new PoseMsg { kind = kind }); // pos zero = exit from where you are
                    continue;
                }
                MoveShip(ship, sp.position, sp.orientation);
                if (!p.isLocalPlayer && p.connectionToClient != null)
                    p.connectionToClient.Send(msgType, new PoseMsg { kind = kind, pos = sp.position, rot = sp.orientation });
                moved++;
                CoopLog.Write("FLOW", "host: " + why + ": moved netId=" + p.netId.Value + " next to netId=" + anchor.c_player.netId.Value + " at " + sp.position.ToString("F1"));
            }
            // dead players (joiners and the host) are revived next to the exit and sent into it by HostTick
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
            Regroup(anchor, FNet.Teleport, 0, 0f, "lockdown");
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
        public static bool Waiting { get { return s_wait_shown; } }

        public static void ResetForLevel() { PostLevel.Reset(); if (s_wait_shown) { try { UIManager.SetScreenFade(0f); } catch { } } CoopStatus.Clear(); s_lockdowns_done.Clear(); s_last_trigger_ship = null; s_exit_anchor = null; s_pending_pose = null; s_pending_exit = -1; s_dead_wait_until = -1f; s_revive.Clear(); s_revive_anchor = null; s_revive_until = -1f; s_requested = false; s_exit_sent = false; s_wait_shown = false; ApplyingExit = false; ApplyingLog = false; }

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
                PlayerShip anchor = null;
                foreach (var p in Overload.NetworkManager.m_Players)
                    if (p != null && p.connectionToClient != null && p.connectionToClient.connectionId == msg.conn.connectionId) anchor = p.c_player_ship;
                s_exit_anchor = anchor;
                if (!LocalAlive())
                {
                    // 0.4.19: host dead/spectating (10:45 run: the host finished the level dead and its menus froze). ExitSequenceStart
                    // refuses a dead ship, so send the joiners out now and revive the host next to the exit; HostTick starts its exit flight.
                    CoopLog.Write("FLOW", "host: dead when a joiner reached the exit; reviving the host into the exit sequence");
                    SendExit(kind);
                    QueueRevive(GameManager.m_player_ship);
                    return;
                }
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
            try { OlCoop.Death.CoopDeath.CancelForExit(); } catch (Exception ex) { CoopLog.Error("CancelForExit", ex); }
            var anchor = s_exit_anchor != null ? s_exit_anchor : GameManager.m_player_ship;
            s_revive_anchor = anchor; s_revive_kind = kind;
            if (anchor == null) { SendAll(FNet.Exit, new PoseMsg { kind = kind }); return; }
            Regroup(anchor, FNet.Exit, kind, 0f, KindName(kind));
            CoopLog.Write("FLOW", "host: " + KindName(kind) + " sequence started at netId=" + anchor.c_player.netId.Value + "; told joiners to exit too");
        }

        /// Host, joiner-triggered exit: put the host's own ship next to that joiner before the host's exit sequence starts.
        public static void HostPlaceSelfForExit()
        {
            var me = GameManager.m_player_ship;
            if (s_exit_anchor == null || me == null || s_exit_anchor == me || !Alive(me)) return;
            LevelData.SpawnPoint sp;
            if (!SpotNear(s_exit_anchor, me, 0, out sp, true)) { CoopLog.Write("FLOW", "host: no free spot next to netId=" + s_exit_anchor.c_player.netId.Value + "; host exits from where it is"); return; }
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
                try { OlCoop.Death.CoopDeath.CancelLeave(); } catch { }
                if (GameplayManager.m_gameplay_state == GameplayState.EXIT) return;
                if (!LocalAlive())
                {
                    // 0.4.19: the host revives us next to the exit right before this message; wait for the respawn, then exit.
                    s_pending_exit = kind; s_pending_pose = pm; s_dead_wait_until = Time.realtimeSinceStartup + 3f;
                    CoopLog.Write("FLOW", "joiner: exit while dead; waiting for the host's revive");
                    return;
                }
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
            if (s_pending_exit < 0) return;
            if (!LocalAlive())
            {
                if (s_dead_wait_until < 0f) s_dead_wait_until = Time.realtimeSinceStartup + 3f;
                if (Time.realtimeSinceStartup < s_dead_wait_until) return;
                s_pending_exit = -1; s_dead_wait_until = -1f;
                CoopLog.Write("FLOW", "joiner: not revived in time for the exit; finishing the level from the death screen");
                PostLevel.DeadJoinerExit();
                return;
            }
            if (GameplayManager.m_gameplay_state != GameplayState.PLAYING) return;
            byte k = (byte)s_pending_exit; s_pending_exit = -1; s_dead_wait_until = -1f;
            CoopLog.Write("FLOW", "joiner: running the deferred " + KindName(k));
            if (s_pending_pose != null && s_pending_pose.pos != Vector3.zero) MoveShip(GameManager.m_player_ship, s_pending_pose.pos, s_pending_pose.rot);
            RunExit(k);
        }

        // ------------------------------------------------------------ 0.4.19: dead players are revived into the exit
        static float s_dead_wait_until = -1f;
        static readonly System.Collections.Generic.List<PlayerShip> s_revive = new System.Collections.Generic.List<PlayerShip>();
        static readonly System.Collections.Generic.HashSet<PlayerShip> s_revived = new System.Collections.Generic.HashSet<PlayerShip>();
        static PlayerShip s_revive_anchor; static byte s_revive_kind; static float s_revive_until = -1f;

        static void QueueRevive(PlayerShip s)
        {
            if (s == null || s_revive.Contains(s)) return;
            s_revive.Add(s); s_revived.Remove(s);
            s_revive_until = Time.realtimeSinceStartup + 8f; // dying ships finish their death animation first
            CoopLog.Write("FLOW", "host: netId=" + s.c_player.netId.Value + " is dead at the exit; reviving it into the exit sequence");
        }

        static PlayerShip LivingAnchor(PlayerShip not)
        {
            if (Alive(s_revive_anchor) && s_revive_anchor != not) return s_revive_anchor;
            foreach (var p in Overload.NetworkManager.m_Players)
                if (p != null && p.c_player_ship != not && Alive(p.c_player_ship)) return p.c_player_ship;
            return null;
        }

        /// Host, every frame (any gameplay state): revive queued dead ships next to the exit and start their exit.
        public static void HostTick()
        {
            if (s_revive.Count == 0) return;
            bool timeout = Time.realtimeSinceStartup > s_revive_until;
            for (int i = s_revive.Count - 1; i >= 0; i--)
            {
                var s = s_revive[i];
                if (s == null || s.c_player == null) { s_revive.RemoveAt(i); continue; }
                bool local = s.isLocalPlayer;
                if (Alive(s) && s_revived.Contains(s))
                {
                    s_revive.RemoveAt(i);
                    if (local)
                    {
                        if (GameplayManager.m_gameplay_state == GameplayState.EXIT) continue;
                        CoopLog.Write("FLOW", "host: revived; starting our own " + KindName(s_revive_kind));
                        if (s_revive_kind == FNet.KindDoor) GameplayManager.ExitSequenceStart();
                        else GameplayManager.TeleportSequenceStart(s_revive_kind == FNet.KindWarp);
                    }
                    else if (s.c_player.connectionToClient != null)
                    {
                        s.c_player.connectionToClient.Send(FNet.Exit, new PoseMsg { kind = s_revive_kind, pos = s.c_transform.position, rot = s.c_transform.rotation });
                        CoopLog.Write("FLOW", "host: revived netId=" + s.c_player.netId.Value + " sent into the " + KindName(s_revive_kind));
                    }
                    continue;
                }
                if (!s_revived.Contains(s) && (bool)s.m_dead)
                {
                    var anchor = LivingAnchor(s);
                    if (anchor != null)
                    {
                        LevelData.SpawnPoint sp;
                        if (!SpotNear(anchor, s, i, out sp, true)) sp = new LevelData.SpawnPoint(anchor.c_transform.position - anchor.c_transform.forward * 4f, anchor.c_transform.rotation, 0);
                        try { OlCoop.Death.CoopDeath.RespawnAt(s, sp.position, sp.orientation, "exit"); s_revived.Add(s); }
                        catch (Exception ex) { CoopLog.Error("revive for exit", ex); }
                        continue;
                    }
                }
                if (!timeout) continue;
                // could not revive in time: old behaviour (finish the level from the death screen)
                s_revive.RemoveAt(i);
                CoopLog.Write("FLOW", "host: could not revive netId=" + s.c_player.netId.Value + " for the exit (dying=" + (bool)s.m_dying + " dead=" + (bool)s.m_dead + "); finishing without the exit flight");
                if (local) { PostLevel.ClearDeathForMenus("host"); GameplayManager.EscapeLevel(); }
                else if (s.c_player.connectionToClient != null) s.c_player.connectionToClient.Send(FNet.Exit, new PoseMsg { kind = s_revive_kind });
            }
        }

        public static void ShowWaiting()
        {
            if (s_wait_shown) return;
            s_wait_shown = true;
            GameplayManager.AddHUDMessage("LEVEL COMPLETE - WAITING FOR THE HOST", -1, true);
            if (CoopStatus.Code < 1) CoopStatus.Set(0, "LEVEL COMPLETE - WAITING FOR THE HOST");
            CoopLog.Write("FLOW", "joiner: level complete; waiting for the host's next level (own EscapeLevel blocked)");
        }

        // ------------------------------------------------------------ host status while joiners wait between levels
        public static void HostSendStatus(int code)
        {
            if (!CoopWorld.IsHost) return;
            SendAll(FNet.Status, new IntegerMessage(code));
            CoopLog.Write("FLOW", "host: told joiners status " + code + (code == 1 ? " (level summary)" : " (loading next level)"));
        }

        public static void OnStatus(NetworkMessage msg)
        {
            try
            {
                int code = msg.ReadMessage<IntegerMessage>().value;
                CoopLog.Write("FLOW", "joiner: host status " + code);
                if (code == 1) CoopStatus.Set(1, "LEVEL COMPLETE - THE HOST IS ON THE LEVEL SUMMARY");
                else if (code == 2)
                {
                    CoopStatus.Set(2, "THE HOST IS STARTING THE NEXT LEVEL...");
                    GameplayManager.AddHUDMessage("CO-OP: THE HOST IS STARTING THE NEXT LEVEL", -1, true);
                    if (!Session.CoopClient.Awaiting) Session.CoopClient.AwaitLevel();
                }
            }
            catch (Exception ex) { CoopLog.Error("OnStatus", ex); }
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
            // 0.4.15: joiners get the stock end-of-level screens (results, stats, upgrades). Their own "start next level" step is
            // held at the PLAY_GAME gate (PostLevel.P1) until the host's next level arrives.
            PostLevel.Begin();
            return true;
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

    /// F18: closing the map. Automap.Update (INIT) collects the "_automap" markers of every Key-tagged object (security keys,
    /// audio logs), cryotube and PropGeneric (incl. destructible switches) and the Door objects; EXIT calls SetActive(false) /
    /// GetComponentInChildren on each. In co-op another player can pick up or destroy one of them while this map is open (the host's
    /// pickup network-destroys it here), so EXIT threw a MissingReferenceException every frame and never returned true: the map
    /// stayed open with a frozen picture (19:34 run, key picked up by the host 4 s after the joiner opened the map).
    /// Drop destroyed entries before the stock EXIT code runs.
    [HarmonyPatch(typeof(Automap), "Update")]
    static class F18_AutomapCloseSafe
    {
        static readonly System.Reflection.FieldInfo f_state = AccessTools.Field(typeof(Automap), "m_automap_state");
        static readonly System.Reflection.FieldInfo f_objects = AccessTools.Field(typeof(Automap), "m_automap_objects");
        static readonly System.Reflection.FieldInfo f_doors = AccessTools.Field(typeof(Automap), "m_doors");

        static void Prefix(Automap __instance)
        {
            if (!CoopConfig.Active) return;
            try
            {
                if ((int)f_state.GetValue(__instance) != (int)Automap.AutomapState.EXIT) return;
                int gone = 0;
                var objs = f_objects.GetValue(__instance) as List<GameObject>;
                if (objs != null) gone += objs.RemoveAll(o => o == null);
                var doors = f_doors.GetValue(__instance) as GameObject[];
                if (doors != null)
                {
                    var kept = new List<GameObject>(doors.Length);
                    foreach (var d in doors) if (d != null) kept.Add(d);
                    if (kept.Count != doors.Length) { gone += doors.Length - kept.Count; f_doors.SetValue(__instance, kept.ToArray()); }
                }
                if (gone > 0) CoopLog.Write("FLOW", "automap: closing; skipped " + gone + " map marker(s) removed while the map was open");
            }
            catch (Exception ex) { CoopLog.Error("F18", ex); }
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
            if (CoopWorld.IsHost) { try { CoopFlow.HostTick(); } catch (Exception ex) { CoopLog.Error("F11 host", ex); } return; }
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
            NetworkServer.RegisterHandler(FNet.Ready, PostLevel.OnReady);
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
            c.RegisterHandler(FNet.Status, CoopFlow.OnStatus);
        }
    }

    /// Persistent status line for joiners between levels (overlay slot 3, drawn like the respawn timer so it shows over the exit fade).
    public static class CoopStatus
    {
        public static readonly UIElementType uiStatusOverlay = (UIElementType)122;
        const int Slot = 3;
        public static int Code = -1;
        static string s_text;
        static bool s_on, s_logged;

        public static void Set(int code, string text)
        {
            Code = code; s_text = text; s_logged = false;
            CoopLog.Write("HUD", "status banner: " + text);
            Ensure();
        }
        public static void Clear() { Code = -1; s_text = null; Ensure(); }
        public static void Ensure()
        {
            bool want = s_text != null;
            if (want && !s_on) { UIManager.CreateOverlayElement(Vector2.zero, Slot, uiStatusOverlay, -1f); s_on = true; }
            else if (!want && s_on) { UIManager.ClearOverlayElement(Slot); s_on = false; }
        }
        public static void Draw(UIElement uie)
        {
            if (s_text == null) return;
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 3f);
            uie.DrawStringSmall(s_text, new Vector2(0f, -200f), 0.75f, StringOffset.CENTER, UIManager.m_col_hi5, pulse, -1f);
            if (!s_logged) { s_logged = true; CoopLog.Write("HUD", "drawing status banner"); }
        }
    }

    [HarmonyPatch(typeof(UIElement), "Draw")]
    static class F13_StatusDraw
    {
        static void Postfix(UIElement __instance)
        {
            if (__instance.m_type != CoopStatus.uiStatusOverlay) return;
            try { CoopStatus.Draw(__instance); } catch (Exception ex) { CoopLog.Error("F13", ex); }
        }
    }

    /// F14: joiner exit flight. Every physics tick stock code rewinds the local ship to the host's copy and replays inputs
    /// (PlayerShip.FixedUpdateAll -> Client.ReconcileServerPlayerState). The host's copy doesn't fly the exit path, so the joiner's exit
    /// sequence was dragged back until the host left the level (0.4.6 Goliath run: 26.6 s vs 14.4 s). Skip reconciliation during EXIT.
    [HarmonyPatch(typeof(Client), "ReconcileServerPlayerState")]
    static class F14_NoReconcileInExit
    {
        static System.Reflection.FieldInfo s_q;
        static bool s_logged;
        static bool Prefix()
        {
            if (!CoopWorld.IsJoiner || GameplayManager.m_gameplay_state != GameplayState.EXIT) { s_logged = false; return true; }
            try
            {
                if (s_q == null) s_q = AccessTools.Field(typeof(Client), "m_PendingPlayerStateMessages");
                var q = s_q.GetValue(null);
                if (q != null) q.GetType().GetMethod("Clear").Invoke(q, null);
                if (!s_logged) { s_logged = true; CoopLog.Write("FLOW", "joiner: exit flight - host position corrections paused"); }
            }
            catch (Exception ex) { CoopLog.Error("F14", ex); }
            return false;
        }
    }

    /// F15: host tells waiting joiners where it is between levels.
    [HarmonyPatch(typeof(GameplayManager), "DoneLevel")]
    static class F15_HostDoneLevel
    {
        static void Prefix(GameplayManager.DoneReason reason)
        {
            if (!CoopWorld.IsHost || reason != GameplayManager.DoneReason.Escaped) return;
            try { CoopFlow.HostSendStatus(1); HostReady.Begin(); } catch (Exception ex) { CoopLog.Error("F15", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "LoadLevel")]
    static class F16_HostLoadLevel
    {
        static void Prefix()
        {
            if (!CoopWorld.IsHost) return;
            try { HostReady.End(); CoopFlow.HostSendStatus(2); } catch (Exception ex) { CoopLog.Error("F16", ex); }
        }
    }

    /// F17: joiner after its exit flight. Stock ExitSequenceFrame, once the exit timer runs out, re-parents the camera to the ship, calls
    /// EscapeLevel (blocked on joiners, F6), sets the screen fade back to 0 and keeps flying the ship along the exit path - every frame,
    /// until the host's next level arrives. That left joiners watching a tumbling ship (user, 0.4.8; rough in VR). While waiting: hold a
    /// black screen (the status line is on the overlay layer, above the fade) and keep the ship still.
    [HarmonyPatch(typeof(GameplayManager), "ExitSequenceFrame")]
    static class F17_JoinerHoldAfterExit
    {
        static bool s_logged;
        static bool Prefix()
        {
            if (!CoopWorld.IsJoiner || !CoopFlow.Waiting) { s_logged = false; return true; }
            try
            {
                UIManager.SetScreenFade(1f);
                var ship = GameManager.m_player_ship;
                if (ship != null && ship.c_rigidbody != null) { ship.c_rigidbody.velocity = Vector3.zero; ship.c_rigidbody.angularVelocity = Vector3.zero; }
                if (!s_logged) { s_logged = true; CoopLog.Write("FLOW", "joiner: exit flight done; screen held black, ship held still until the host's next level"); }
            }
            catch (Exception ex) { CoopLog.Error("F17", ex); return true; }
            return false;
        }
    }
}
