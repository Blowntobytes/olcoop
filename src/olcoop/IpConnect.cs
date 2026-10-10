// 0.8.1: CONNECT BY IP (port forwarding) - for players who can't use Steam friends / invites.
// The host already listens on UDP CoopConfig.Port (7777) next to Steam (H1_ServerListen); a joiner connects with Client.Connect
// (CoopClient.MenuTick). This file adds the UI side: the host's address info, and the joiner's IP field on the CO-OP screen.
// No network messages change (protocol unchanged); the host's version/protocol check in the handshake still applies.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Overload;
using UnityEngine;

namespace OlCoop.UI
{
    public static class IpLink
    {
        /// The CO-OP screen shows the IP panel instead of the friend list.
        public static bool Panel;
        /// Joiner: what is typed in the field ("1.2.3.4", "1.2.3.4:7777" or a host name).
        public static string Typed = "";
        public static string Status = "";
        public static string LanIp = "", PublicIp = "";
        static volatile string s_public_result;
        static bool s_loaded;

        static string FilePath
        {
            get
            {
                string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(string.IsNullOrEmpty(d) ? "." : d, "olcoop-lastip.txt");
            }
        }

        public static void Open()
        {
            Panel = true; Status = "";
            if (!s_loaded) { s_loaded = true; try { if (File.Exists(FilePath)) Typed = File.ReadAllText(FilePath).Trim(); } catch { } }
            if (CoopConfig.IsHost) FetchAddresses();
        }

        /// Host: LAN address (the one to forward the port to) and the internet address to give to friends.
        public static void FetchAddresses()
        {
            LanIp = "";
            try
            {
                // the address Windows would use for internet traffic (no packet is sent by a UDP "connect")
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 53);
                    LanIp = ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { }
            if (LanIp == "")
                try { foreach (var a in Dns.GetHostAddresses(Dns.GetHostName())) if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) { LanIp = a.ToString(); break; } } catch { }
            if (PublicIp == "" || PublicIp == "?")
            {
                PublicIp = "LOOKING UP...";
                s_public_result = null;
                var t = new Thread(() =>
                {
                    string r = "?";
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("http://api.ipify.org");
                        req.Timeout = 5000;
                        using (var resp = req.GetResponse())
                        using (var sr = new StreamReader(resp.GetResponseStream()))
                        {
                            string x = sr.ReadToEnd().Trim();
                            IPAddress ip;
                            if (IPAddress.TryParse(x, out ip)) r = x;
                        }
                    }
                    catch { }
                    s_public_result = r;
                });
                t.IsBackground = true;
                t.Start();
            }
            CoopLog.Write("IP", "host address info: LAN " + LanIp + ", port UDP " + CoopConfig.Port);
        }

        public static void Tick()
        {
            if (s_public_result != null) { PublicIp = s_public_result; s_public_result = null; CoopLog.Write("IP", "internet address lookup: " + (PublicIp == "?" ? "failed" : "ok")); }
        }

        /// Joiner typing in the field (same rules as the game's own MP password field). Returns true on Enter.
        public static bool ProcessInput()
        {
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
            {
                string clip = (GUIUtility.systemCopyBuffer ?? "").Trim();
                if (clip.Length <= 64) Typed = Filter(clip);
                return false;
            }
            foreach (char c in Input.inputString)
            {
                if (c == '\b') { if (Typed.Length > 0) { Typed = Typed.Substring(0, Typed.Length - 1); SFXCueManager.PlayRawSoundEffect2D(SoundEffect.hud_notify_bot_died, 0.4f, UnityEngine.Random.Range(-0.15f, -0.05f)); } }
                else if (c == '\n' || c == '\r') return true;
                else if (Typed.Length < 64 && Allowed(c)) { Typed += c; SFXCueManager.PlayRawSoundEffect2D(SoundEffect.hud_notify_bot_died, 0.5f, UnityEngine.Random.Range(0.1f, 0.2f)); }
            }
            return false;
        }

        static bool Allowed(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == ':' || c == '-'; }
        static string Filter(string s) { var b = new System.Text.StringBuilder(); foreach (char c in s) if (Allowed(c)) b.Append(c); return b.ToString(); }

        /// Joiner: parse "host[:port]", resolve a name to IPv4, then join like -coopjoin.
        public static void Connect()
        {
            string t = (Typed ?? "").Trim();
            int port = 7777;
            string host = t;
            int colon = t.LastIndexOf(':');
            if (colon > 0)
            {
                host = t.Substring(0, colon);
                if (!int.TryParse(t.Substring(colon + 1), out port) || port < 1 || port > 65535) { Status = "BAD PORT - USE 1.2.3.4 OR 1.2.3.4:7777"; return; }
            }
            if (host == "") { Status = "TYPE THE HOST'S IP ADDRESS FIRST"; return; }
            IPAddress ip;
            if (!IPAddress.TryParse(host, out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                ip = null;
                try { foreach (var a in Dns.GetHostAddresses(host)) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; } } catch { }
                if (ip == null) { Status = "CAN'T FIND " + host.ToUpperInvariant(); return; }
            }
            try { File.WriteAllText(FilePath, t); } catch { }
            CoopConfig.Port = port;
            CoopConfig.SetJoinIp(ip.ToString());
            OlCoop.SteamNet.SteamLink.LastStatus = "JOINING " + ip + ":" + port + " - THE HOST MUST FORWARD UDP PORT " + port;
            CoopLog.Write("IP", "join by IP: " + (host == ip.ToString() ? host : host + " -> " + ip) + ":" + port);
            Status = ""; Panel = false;
        }

        /// Drawn in place of the friend list.
        public static void Draw(UIElement uie, Vector2 pos, int idField, int idGo)
        {
            Tick();
            if (CoopConfig.IsHost)
            {
                Color hi = UIManager.m_col_hi4, ui = UIManager.m_col_ui1, dim = UIManager.m_col_ui2;
                uie.DrawStringSmall("CONNECT BY IP (PORT FORWARDING)", pos + Vector2.up * 4f, 0.45f, StringOffset.CENTER, dim, 1f, -1f);
                pos.y += 34f;
                uie.DrawStringSmall("GIVE YOUR FRIENDS:  " + (PublicIp == "" ? "?" : PublicIp) + (CoopConfig.Port != 7777 ? ":" + CoopConfig.Port : ""), pos, 0.5f, StringOffset.CENTER, hi, 1f, -1f);
                pos.y += 32f;
                uie.DrawStringSmall("IN YOUR ROUTER, FORWARD UDP PORT " + CoopConfig.Port + " TO THIS PC (" + (LanIp == "" ? "?" : LanIp) + ")", pos, 0.4f, StringOffset.CENTER, ui, 1f, -1f);
                pos.y += 24f;
                uie.DrawStringSmall("ALLOW OVERLOAD WHEN WINDOWS FIREWALL ASKS (PRIVATE AND PUBLIC NETWORKS)", pos, 0.4f, StringOffset.CENTER, ui, 1f, -1f);
                pos.y += 24f;
                uie.DrawStringSmall("SAME HOUSE / LAN: FRIENDS USE " + (LanIp == "" ? "?" : LanIp) + " - NO FORWARDING NEEDED", pos, 0.4f, StringOffset.CENTER, ui, 1f, -1f);
                pos.y += 24f;
                uie.DrawStringSmall("ONLY SHARE YOUR IP WITH PEOPLE YOU TRUST. STEAM INVITES NEED NONE OF THIS.", pos, 0.38f, StringOffset.CENTER, dim, 1f, -1f);
                return;
            }
            uie.DrawStringSmall("JOIN BY IP - THE HOST'S ADDRESS (THEIR CO-OP SCREEN > CONNECT BY IP SHOWS IT)", pos + Vector2.up * 4f, 0.42f, StringOffset.CENTER, UIManager.m_col_ui2, 1f, -1f);
            pos.y += 40f;
            uie.DrawTextEntryLabeled(pos, idField, "HOST IP", Typed, false);
            pos.y += 50f;
            uie.SelectAndDrawItem("CONNECT", pos, idGo, false, 1f, 0.75f);
            pos.y += 46f;
            uie.DrawStringSmall(Status != "" ? Status : "EXAMPLE: 203.0.113.7  OR  203.0.113.7:7777  -  CTRL+V PASTES", pos, 0.38f, StringOffset.CENTER,
                Status != "" ? UIManager.m_col_damage : UIManager.m_col_ui2, 1f, -1f);
        }
    }
}
