// olcoop 0.5.0: play over the internet through Steam - no port forwarding.
//
// Transport: Overload's networking is Unity UNET. UNET can carry a connection whose bytes travel by any means ("external
// connection"): we subclass NetworkConnection, send its bytes with SteamNetworking.SendP2PPacket (Steam picks a direct route or its
// relay network, so routers/NAT don't matter) and feed received bytes back with TransportReceive. Everything above the transport -
// Overload's own messages, olmod's and ours - is unchanged.
//   Host:   a Steam peer that sends us a packet (and is a friend or in our lobby) gets a SteamConnection added to the running server
//           with NetworkServer.AddExternalConnection (which fires the normal "connect" handler).
//   Joiner: Overload.Client's NetworkClient is created around a SteamConnection (NetworkClient(conn) starts "connected") and the
//           normal connect handler runs, which sends our co-op handshake as usual.
//   Packet format: UNET bytes + 1 trailing byte = UNET channel id (Steam channel 0 for everything).
//
// Lobby/invites: the host creates a friends-only Steam lobby. Friends can be invited from the CO-OP screen (Steam sends them a chat
// invite) or join from the CO-OP screen's friend list; accepting a Steam invite while the game runs joins automatically
// (GameLobbyJoinRequested). The lobby owner is the host to connect to.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Overload;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace OlCoop.SteamNet
{
    /// A UNET connection whose bytes travel over Steam P2P.
    public class SteamConnection : NetworkConnection
    {
        public CSteamID Peer;
        public bool[] Reliable;
        public long BytesOut, BytesIn, PacketsOut, PacketsIn;
        static byte[] s_send = new byte[4096];

        public void Setup(CSteamID peer, string address, int hostId, int connId, HostTopology topo)
        {
            Peer = peer;
            Initialize(address, hostId, connId, topo);
            var ch = topo.DefaultConfig.Channels;
            Reliable = new bool[ch.Count];
            for (int i = 0; i < ch.Count; i++)
            {
                var q = ch[i].QOS;
                Reliable[i] = q == QosType.Reliable || q == QosType.ReliableFragmented || q == QosType.ReliableSequenced ||
                              q == QosType.ReliableStateUpdate || q == QosType.AllCostDelivery || q == QosType.ReliableFragmentedSequenced;
            }
        }

        public override bool TransportSend(byte[] bytes, int numBytes, int channelId, out byte error)
        {
            error = 0;
            if (numBytes + 1 > s_send.Length) s_send = new byte[numBytes + 256];
            Buffer.BlockCopy(bytes, 0, s_send, 0, numBytes);
            s_send[numBytes] = (byte)channelId;
            bool rel = channelId >= 0 && channelId < Reliable.Length && Reliable[channelId];
            // Steam's unreliable packets are limited to 1200 bytes; anything bigger goes reliable.
            var mode = (rel || numBytes + 1 > 1200) ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay;
            bool ok = SteamNetworking.SendP2PPacket(Peer, s_send, (uint)(numBytes + 1), mode, 0);
            if (ok) { BytesOut += numBytes; PacketsOut++; }
            else error = (byte)NetworkError.NoResources;
            return ok;
        }
    }

    public static class SteamLink
    {
        public const int SteamHostIdMarker = 0;      // NetworkConnection.isConnected is "hostId != -1"
        const int FirstServerConnId = 20;            // above the UDP connection ids (max 16 players)
        public const uint OverloadAppId = 448850;

        static PropertyInfo s_initProp;
        public static bool Available
        {
            get
            {
                try
                {
                    if (s_initProp == null) s_initProp = AccessTools.Property(AccessTools.TypeByName("SteamManager"), "Initialized");
                    return s_initProp != null && (bool)s_initProp.GetValue(null, null);
                }
                catch { return false; }
            }
        }

        // ---------------------------------------------------------------- state
        static readonly Dictionary<ulong, SteamConnection> s_server = new Dictionary<ulong, SteamConnection>();
        static readonly Dictionary<ulong, List<byte[]>> s_waiting = new Dictionary<ulong, List<byte[]>>(); // host: packets before the server runs
        static SteamConnection s_client;
        static byte[] s_buf = new byte[65536];
        static bool s_init;
        static Callback<P2PSessionRequest_t> s_cbRequest;
        static Callback<P2PSessionConnectFail_t> s_cbFail;
        static Callback<GameLobbyJoinRequested_t> s_cbJoinReq;
        static Callback<LobbyEnter_t> s_cbEnter;
        static CallResult<LobbyCreated_t> s_crCreated;
        public static CSteamID Lobby = CSteamID.Nil;
        public static string LastStatus = "";
        static float s_next_stats;

        public static void Init()
        {
            if (s_init || !Available) return;
            s_init = true;
            SteamNetworking.AllowP2PPacketRelay(true);
            s_cbRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            s_cbFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionFail);
            s_cbJoinReq = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            s_cbEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            s_crCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            CoopLog.Write("STEAM", "steam networking ready; me=" + SteamUser.GetSteamID().m_SteamID + " (" + SteamFriends.GetPersonaName() + ")");
            // Launched by a Steam invite while the game was closed: "+connect_lobby <id>"
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                ulong id;
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out id))
                {
                    CoopLog.Write("STEAM", "started with +connect_lobby " + id + "; joining");
                    JoinLobby(new CSteamID(id));
                }
            }
        }

        static HostTopology Topology() { return new HostTopology(Overload.NetworkManager.GetConnectionConfig(), 16); }

        public static string Name(CSteamID id)
        {
            try { return SteamFriends.GetFriendPersonaName(id); } catch { return id.m_SteamID.ToString(); }
        }

        // ---------------------------------------------------------------- lobby
        public static void CreateLobby()
        {
            if (!Available) return;
            Init();
            if (Lobby != CSteamID.Nil) { LeaveLobby(); }
            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4);
            s_crCreated.Set(call, OnLobbyCreated);
            LastStatus = "CREATING STEAM LOBBY...";
            CoopLog.Write("STEAM", "creating friends-only lobby");
        }

        static void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK)
            {
                LastStatus = "STEAM LOBBY FAILED (" + r.m_eResult + ")";
                CoopLog.Write("STEAM", "lobby creation failed: " + r.m_eResult + " io=" + ioFailure);
                return;
            }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, "olcoop", CoopVersion.Full);
            SteamMatchmaking.SetLobbyData(Lobby, "protocol", CoopVersion.Protocol.ToString());
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", "Hosting Overload co-op");
            LastStatus = "HOSTING - FRIENDS CAN JOIN OR BE INVITED";
            CoopLog.Write("STEAM", "lobby " + Lobby.m_SteamID + " created; friends can join");
        }

        public static void Invite(CSteamID friend)
        {
            if (Lobby == CSteamID.Nil) { CoopLog.Write("STEAM", "invite: no lobby yet"); return; }
            bool ok = SteamMatchmaking.InviteUserToLobby(Lobby, friend);
            LastStatus = ok ? "INVITE SENT TO " + Name(friend).ToUpperInvariant() : "INVITE FAILED";
            CoopLog.Write("STEAM", "invited " + Name(friend) + " (" + friend.m_SteamID + "): " + ok);
        }

        public static void OpenInviteOverlay()
        {
            if (Lobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        public static void JoinLobby(CSteamID lobby)
        {
            if (!Available) return;
            Init();
            LastStatus = "JOINING...";
            CoopLog.Write("STEAM", "joining lobby " + lobby.m_SteamID);
            SteamMatchmaking.JoinLobby(lobby);
        }

        public static void LeaveLobby()
        {
            if (Lobby == CSteamID.Nil) return;
            try { SteamMatchmaking.LeaveLobby(Lobby); SteamFriends.ClearRichPresence(); } catch { }
            CoopLog.Write("STEAM", "left lobby " + Lobby.m_SteamID);
            Lobby = CSteamID.Nil;
        }

        static void OnJoinRequested(GameLobbyJoinRequested_t r)
        {
            CoopLog.Write("STEAM", "accepted an invite from " + Name(r.m_steamIDFriend) + " to lobby " + r.m_steamIDLobby.m_SteamID);
            if (CoopConfig.IsHost) { CoopLog.Write("STEAM", "ignored: we are hosting"); return; }
            JoinLobby(r.m_steamIDLobby);
        }

        static void OnLobbyEnter(LobbyEnter_t r)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (CoopConfig.IsHost) { Lobby = lobby; return; } // our own lobby
            if (r.m_EChatRoomEnterResponse != 1) // k_EChatRoomEnterResponseSuccess
            {
                LastStatus = "COULD NOT JOIN (" + r.m_EChatRoomEnterResponse + ")";
                CoopLog.Write("STEAM", "lobby enter failed: response " + r.m_EChatRoomEnterResponse);
                return;
            }
            string ver = SteamMatchmaking.GetLobbyData(lobby, "olcoop");
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            CoopLog.Write("STEAM", "entered lobby " + lobby.m_SteamID + " owner=" + Name(owner) + " (" + owner.m_SteamID + ") host version '" + ver + "'");
            if (!string.IsNullOrEmpty(ver) && ver != CoopVersion.Full)
            {
                LastStatus = "HOST RUNS " + ver.ToUpperInvariant() + ", YOU RUN " + CoopVersion.Full.ToUpperInvariant();
                SteamMatchmaking.LeaveLobby(lobby);
                return;
            }
            Lobby = lobby;
            LastStatus = "JOINING " + Name(owner).ToUpperInvariant() + "...";
            CoopConfig.SetJoinSteam(owner.m_SteamID);
        }

        // ---------------------------------------------------------------- joiner connect
        /// Replaces Client.Connect(ip, port) for a Steam host.
        public static void ClientConnect(ulong hostId)
        {
            Init();
            var host = new CSteamID(hostId);
            if (NetworkServer.active) NetworkServer.Shutdown();
            if (Client.IsConnected()) Client.Disconnect();
            CloseClient();
            var topo = Topology();
            var conn = new SteamConnection();
            conn.Setup(host, "steam:" + hostId, SteamHostIdMarker, 1, topo);
            var nc = new NetworkClient(conn);
            nc.Configure(topo);
            AccessTools.Field(typeof(Client), "m_network_client").SetValue(null, nc);
            AccessTools.Method(typeof(Client), "RegisterHandlers").Invoke(null, null);
            s_client = conn;
            CoopLog.Write("STEAM", "connecting to host " + Name(host) + " (" + hostId + ") over Steam");
            conn.InvokeHandlerNoData(MsgType.Connect); // Overload's connect handler + our handshake
        }

        static void CloseClient()
        {
            if (s_client == null) return;
            try { SteamNetworking.CloseP2PSessionWithUser(s_client.Peer); } catch { }
            s_client = null;
        }

        /// Joiner gives up the session (LEAVE on the CO-OP screen).
        public static void Leave()
        {
            SayBye();
            CloseClient();
            foreach (var c in new List<SteamConnection>(s_server.Values)) DropServerConn(c, "host left co-op");
            LeaveLobby();
        }

        // ---------------------------------------------------------------- host side
        static bool Allowed(CSteamID who)
        {
            if (!CoopConfig.IsHost) return false;
            if (Lobby != CSteamID.Nil)
            {
                int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
                for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i) == who) return true;
            }
            return SteamFriends.HasFriend(who, EFriendFlags.k_EFriendFlagImmediate);
        }

        static void OnSessionRequest(P2PSessionRequest_t r)
        {
            bool ok = Allowed(r.m_steamIDRemote);
            CoopLog.Write("STEAM", "session request from " + Name(r.m_steamIDRemote) + " (" + r.m_steamIDRemote.m_SteamID + "): " + (ok ? "accepted" : "refused (not hosting, or not a friend/lobby member)"));
            if (ok) SteamNetworking.AcceptP2PSessionWithUser(r.m_steamIDRemote);
        }

        static void OnSessionFail(P2PSessionConnectFail_t r)
        {
            CoopLog.Write("STEAM", "session with " + Name(r.m_steamIDRemote) + " failed/closed (error " + r.m_eP2PSessionError + ")");
            SteamConnection c;
            if (s_server.TryGetValue(r.m_steamIDRemote.m_SteamID, out c)) DropServerConn(c, "steam session ended");
            if (s_client != null && s_client.Peer == r.m_steamIDRemote) HostGone("steam session failed");
        }

        static int NextServerConnId()
        {
            int id = FirstServerConnId;
            var conns = NetworkServer.connections;
            while (id < conns.Count && conns[id] != null) id++;
            return id;
        }

        static SteamConnection ServerConn(CSteamID peer)
        {
            SteamConnection c;
            if (s_server.TryGetValue(peer.m_SteamID, out c))
            {
                if (NetworkServer.connections.Contains(c)) return c;
                s_server.Remove(peer.m_SteamID); // server restarted since; make a new one
                CoopLog.Write("STEAM", "server restarted; re-adding " + Name(peer));
            }
            int hostId = NetworkServer.serverHostId >= 0 ? NetworkServer.serverHostId : 0;
            c = new SteamConnection();
            c.Setup(peer, "steam:" + peer.m_SteamID, hostId, NextServerConnId(), Topology());
            s_server[peer.m_SteamID] = c;
            CoopLog.Write("STEAM", "player " + Name(peer) + " connected over Steam as conn " + c.connectionId);
            if (!NetworkServer.AddExternalConnection(c)) // also runs the server's connect handler
            {
                CoopLog.Write("STEAM", "AddExternalConnection refused conn " + c.connectionId);
                s_server.Remove(peer.m_SteamID);
                return null;
            }
            return c;
        }

        static void DropServerConn(SteamConnection c, string why)
        {
            s_server.Remove(c.Peer.m_SteamID);
            s_last_heard.Remove(c.Peer.m_SteamID);
            CoopLog.Write("STEAM", "dropping conn " + c.connectionId + " (" + Name(c.Peer) + "): " + why);
            try { SteamNetworking.CloseP2PSessionWithUser(c.Peer); } catch { }
            try
            {
                if (NetworkServer.connections.Contains(c))
                {
                    c.InvokeHandlerNoData(MsgType.Disconnect);
                    NetworkServer.RemoveExternalConnection(c.connectionId);
                }
            }
            catch (Exception ex) { CoopLog.Error("DropServerConn", ex); }
        }

        public static bool IsSteamConn(NetworkConnection c) { return c is SteamConnection; }

        // ---------------------------------------------------------------- pump (every frame)
        // ---------------------------------------------------------------- 0.5.1: liveness (Steam channel 1)
        // UNET's own timeouts belong to its UDP transport, which a Steam connection doesn't use, so a player whose game closed or
        // crashed stayed in the game for everyone else (13:20 report). Every peer sends a 1-byte ping per second on Steam channel 1;
        // 20 s without any packet = gone. A clean quit or LEAVE sends BYE so the others drop the player at once.
        const byte CTL_PING = 1, CTL_BYE = 2;
        const float TIMEOUT = 20f;
        static readonly Dictionary<ulong, float> s_last_heard = new Dictionary<ulong, float>();
        static readonly byte[] s_ctl = new byte[1];
        static float s_next_ping, s_last_pump = -1f;
        /// Set once Steam is shutting down (SteamManager.OnDestroy / application quit): no Steam call may run after that
        /// (12:51 crash: access violation in SteamAPI_RunCallbacks after the Steam API was shut down).
        public static bool ShuttingDown;

        static void Heard(CSteamID id) { s_last_heard[id.m_SteamID] = Time.realtimeSinceStartup; }

        static void SendCtl(CSteamID to, byte what)
        {
            s_ctl[0] = what;
            SteamNetworking.SendP2PPacket(to, s_ctl, 1, what == CTL_BYE ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliableNoDelay, 1);
        }

        /// Tell everyone we're leaving (quit, LEAVE, STOP HOSTING).
        public static void SayBye()
        {
            if (!s_init || ShuttingDown) return;
            try
            {
                if (s_client != null) SendCtl(s_client.Peer, CTL_BYE);
                foreach (var c in s_server.Values) SendCtl(c.Peer, CTL_BYE);
                if (s_client != null || s_server.Count > 0) CoopLog.Write("STEAM", "told the other players we're leaving");
            }
            catch (Exception ex) { CoopLog.Error("SayBye", ex); }
        }

        static void PeerGone(CSteamID who, string why)
        {
            s_last_heard.Remove(who.m_SteamID);
            SteamConnection c;
            if (s_server.TryGetValue(who.m_SteamID, out c)) DropServerConn(c, why); // stock disconnect removes the ship for everyone
            if (s_waiting.Remove(who.m_SteamID)) CoopLog.Write("STEAM", Name(who) + " stopped waiting: " + why);
            if (s_client != null && s_client.Peer == who) HostGone(why);
        }

        /// Joiner: the host is gone. Run the stock disconnect (back to the main menu) and stop trying to rejoin.
        static void HostGone(string why)
        {
            var conn = s_client; s_client = null;
            CoopLog.Write("STEAM", "lost the host " + Name(conn.Peer) + ": " + why);
            try { SteamNetworking.CloseP2PSessionWithUser(conn.Peer); } catch { }
            try { conn.InvokeHandlerNoData(MsgType.Disconnect); } catch (Exception ex) { CoopLog.Error("steam client disconnect", ex); }
            try { if (Client.IsConnected()) Client.Disconnect(); } catch (Exception ex) { CoopLog.Error("steam client close", ex); }
            LeaveLobby();
            CoopConfig.ClearRole();
            LastStatus = "THE HOST LEFT THE GAME";
            GameplayManager.AddHUDMessage("CO-OP: THE HOST LEFT THE GAME", -1, true);
        }

        static void Liveness()
        {
            float now = Time.realtimeSinceStartup;
            // our own frame stalled (level load): don't blame the others for the silence
            if (s_last_pump >= 0f && now - s_last_pump > 2f)
                foreach (var k in new List<ulong>(s_last_heard.Keys)) s_last_heard[k] = now;
            s_last_pump = now;

            if (now >= s_next_ping)
            {
                s_next_ping = now + 1f;
                if (s_client != null) SendCtl(s_client.Peer, CTL_PING);
                foreach (var c in s_server.Values) SendCtl(c.Peer, CTL_PING);
                foreach (var id in s_waiting.Keys) SendCtl(new CSteamID(id), CTL_PING); // joiners waiting for our campaign to start
            }
            var gone = new List<CSteamID>();
            if (s_client != null) { float t; if (s_last_heard.TryGetValue(s_client.Peer.m_SteamID, out t) && now - t > TIMEOUT) gone.Add(s_client.Peer); else if (!s_last_heard.ContainsKey(s_client.Peer.m_SteamID)) s_last_heard[s_client.Peer.m_SteamID] = now; }
            foreach (var c in s_server.Values) { float t; if (!s_last_heard.TryGetValue(c.Peer.m_SteamID, out t)) s_last_heard[c.Peer.m_SteamID] = now; else if (now - t > TIMEOUT) gone.Add(c.Peer); }
            foreach (var g in gone) PeerGone(g, "no packets for " + TIMEOUT.ToString("0") + " s");
        }

        static void PumpControl()
        {
            uint size; int guard = 0;
            while (SteamNetworking.IsP2PPacketAvailable(out size, 1) && guard++ < 200)
            {
                uint read; CSteamID from;
                if (size > s_buf.Length) s_buf = new byte[size + 1024];
                if (!SteamNetworking.ReadP2PPacket(s_buf, size, out read, out from, 1) || read < 1) continue;
                Heard(from);
                if (s_buf[0] == CTL_BYE) PeerGone(from, "left the game");
            }
        }

        public static void Pump()
        {
            if (ShuttingDown) return;
            if (!s_init) { if (!Available) return; Init(); }
            PumpControl();
            uint size;
            int guard = 0;
            while (SteamNetworking.IsP2PPacketAvailable(out size, 0) && guard++ < 2000)
            {
                if (size > s_buf.Length) s_buf = new byte[size + 1024];
                uint read; CSteamID from;
                if (!SteamNetworking.ReadP2PPacket(s_buf, size, out read, out from, 0) || read < 1) continue;
                int ch = s_buf[read - 1];
                int n = (int)read - 1;
                Heard(from);
                if (s_client != null && from == s_client.Peer)
                {
                    s_client.BytesIn += n; s_client.PacketsIn++;
                    try { s_client.TransportReceive(s_buf, n, ch); } catch (Exception ex) { CoopLog.Error("steam client receive", ex); }
                    continue;
                }
                if (!CoopConfig.IsHost) continue;
                if (!NetworkServer.active)
                {
                    // host still in the menus: keep the first packets (handshake) for when the server runs
                    List<byte[]> q;
                    if (!s_waiting.TryGetValue(from.m_SteamID, out q)) { q = new List<byte[]>(); s_waiting[from.m_SteamID] = q; CoopLog.Write("STEAM", Name(from) + " is connecting; waiting for our server (start or continue the campaign)"); }
                    if (q.Count < 64) { var b = new byte[read]; Buffer.BlockCopy(s_buf, 0, b, 0, (int)read); q.Add(b); }
                    continue;
                }
                var c = ServerConn(from);
                if (c == null) continue;
                FlushWaiting(from, c);
                c.BytesIn += n; c.PacketsIn++;
                try { c.TransportReceive(s_buf, n, ch); } catch (Exception ex) { CoopLog.Error("steam server receive", ex); }
            }
            // queued packets once the server is up
            if (CoopConfig.IsHost && NetworkServer.active && s_waiting.Count > 0)
                foreach (var id in new List<ulong>(s_waiting.Keys))
                {
                    var c = ServerConn(new CSteamID(id));
                    if (c != null) FlushWaiting(new CSteamID(id), c);
                }
            Liveness();
            // UNET only flushes a client's send buffers from its transport update, which a Steam client never runs.
            if (s_client != null) { try { s_client.FlushChannels(); } catch (Exception ex) { CoopLog.Error("steam flush", ex); } }

            if (Time.realtimeSinceStartup >= s_next_stats && (s_client != null || s_server.Count > 0))
            {
                s_next_stats = Time.realtimeSinceStartup + 30f;
                if (s_client != null) CoopLog.Write("STEAM", "client link: out " + s_client.PacketsOut + " pkts/" + s_client.BytesOut + " B, in " + s_client.PacketsIn + " pkts/" + s_client.BytesIn + " B" + Route(s_client.Peer));
                foreach (var c in s_server.Values) CoopLog.Write("STEAM", "conn " + c.connectionId + " (" + Name(c.Peer) + "): out " + c.PacketsOut + "/" + c.BytesOut + " B, in " + c.PacketsIn + "/" + c.BytesIn + " B" + Route(c.Peer));
            }
        }

        static string Route(CSteamID peer)
        {
            P2PSessionState_t st;
            if (!SteamNetworking.GetP2PSessionState(peer, out st)) return "";
            return " active=" + st.m_bConnectionActive + " relay=" + st.m_bUsingRelay + " err=" + st.m_eP2PSessionError;
        }

        static void FlushWaiting(CSteamID from, SteamConnection c)
        {
            List<byte[]> q;
            if (!s_waiting.TryGetValue(from.m_SteamID, out q)) return;
            s_waiting.Remove(from.m_SteamID);
            CoopLog.Write("STEAM", "delivering " + q.Count + " early packet(s) from " + Name(from));
            foreach (var b in q) { try { c.TransportReceive(b, b.Length - 1, b[b.Length - 1]); } catch (Exception ex) { CoopLog.Error("steam early packet", ex); } }
        }

        // ---------------------------------------------------------------- friends (CO-OP screen)
        public struct Friend { public CSteamID Id; public string Name; public bool InOverload; public CSteamID Lobby; public bool Online; }

        public static List<Friend> Friends()
        {
            var l = new List<Friend>();
            if (!Available) return l;
            int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                var st = SteamFriends.GetFriendPersonaState(id);
                if (st == EPersonaState.k_EPersonaStateOffline) continue;
                var f = new Friend { Id = id, Name = SteamFriends.GetFriendPersonaName(id), Online = true, Lobby = CSteamID.Nil };
                FriendGameInfo_t gi;
                if (SteamFriends.GetFriendGamePlayed(id, out gi) && gi.m_gameID.AppID().m_AppId == OverloadAppId)
                {
                    f.InOverload = true;
                    if (gi.m_steamIDLobby.IsValid() && gi.m_steamIDLobby != CSteamID.Nil) f.Lobby = gi.m_steamIDLobby;
                }
                l.Add(f);
            }
            // hosting friends first, then friends in Overload, then by name
            l.Sort((a, b) =>
            {
                int ka = a.Lobby != CSteamID.Nil ? 0 : a.InOverload ? 1 : 2, kb = b.Lobby != CSteamID.Nil ? 0 : b.InOverload ? 1 : 2;
                return ka != kb ? ka.CompareTo(kb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return l;
        }
    }

    // ===================================================================== patches

    /// Steam packets are read every frame (menus included), before the game's own network update.
    [HarmonyPatch(typeof(Overload.NetworkManager), "Update")]
    static class ST1_Pump
    {
        static void Prefix() { try { SteamLink.Pump(); } catch (Exception ex) { CoopLog.Error("ST1", ex); } }
    }

    /// Steam goes away with SteamManager (game quitting): say goodbye first, then never touch Steam again.
    [HarmonyPatch]
    static class ST3_SteamShutdown
    {
        public const string OlmodTarget = "game:SteamManager:OnDestroy"; // SteamManager is internal; checked by tools/VerifyPatches
        static MethodBase TargetMethod() { return AccessTools.Method(AccessTools.TypeByName("SteamManager"), "OnDestroy"); }
        static void Prefix()
        {
            try { SteamLink.SayBye(); } catch { }
            SteamLink.ShuttingDown = true;
            CoopLog.Write("STEAM", "steam shutting down; co-op steam link stopped");
        }
    }

    /// Overload also runs SteamAPI.RunCallbacks from a 1 s System.Timers thread, besides SteamManager.Update on the main thread.
    /// That runs Steam callbacks (ours included) off the main thread and can run after Steam has shut down at quit - the 12:51
    /// access violation in SteamAPI_RunCallbacks. SteamManager.Update already runs them every frame, so the timer is skipped.
    [HarmonyPatch(typeof(Overload.Steam), "CallbackTimerTick")]
    static class ST4_NoTimerThreadCallbacks
    {
        static bool s_logged;
        static bool Prefix()
        {
            if (!s_logged) { s_logged = true; CoopLog.Write("STEAM", "skipping Overload's timer-thread Steam callbacks (main thread runs them)"); }
            return false;
        }
    }

    /// NetworkConnection.Disconnect on a Steam connection would call NetworkTransport.Disconnect for a connection the UDP transport
    /// doesn't have. Close the Steam session instead and do the rest of the stock work.
    [HarmonyPatch(typeof(NetworkConnection), "Disconnect")]
    static class ST2_SteamDisconnect
    {
        static readonly MethodInfo m_removeObservers = AccessTools.Method(typeof(NetworkConnection), "RemoveObservers");
        static bool Prefix(NetworkConnection __instance)
        {
            var sc = __instance as SteamConnection;
            if (sc == null) return true;
            try
            {
                sc.isReady = false;
                SteamNetworking.CloseP2PSessionWithUser(sc.Peer);
                if (m_removeObservers != null) m_removeObservers.Invoke(sc, null);
                CoopLog.Write("STEAM", "closed steam connection " + sc.connectionId + " (" + SteamLink.Name(sc.Peer) + ")");
            }
            catch (Exception ex) { CoopLog.Error("ST2", ex); }
            return false;
        }
    }
}
