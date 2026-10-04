using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OlCoop
{
    /// <summary>
    /// Mod identity and global switches. Loaded by olmod as Mod-olcoop.dll when the game
    /// is started with "olmod.exe -modded". Removing the DLL fully uninstalls the mod.
    /// </summary>
    public static class CoopConfig
    {
        /// <summary>Phase 0 logging is read-only; it can be turned off with -coopnolog.</summary>
        public static bool LoggingEnabled = true;
        /// <summary>Seconds between periodic state dumps while a level is running.</summary>
        public static float DumpInterval = 5f;

        /// <summary>-coophost: this instance hosts a co-op session (listens on Port).</summary>
        public static bool IsHost;
        /// <summary>-coopjoin &lt;ip&gt;: this instance joins the host at JoinIp:Port from the main menu.</summary>
        public static string JoinIp;
        /// <summary>-coopport &lt;n&gt; (default 7777).</summary>
        public static int Port = 7777;
        public static bool IsJoiner { get { return !string.IsNullOrEmpty(JoinIp); } }
        /// <summary>True only when started with -coophost or -coopjoin. Every behaviour change is gated on this.</summary>
        public static bool Active { get { return IsHost || IsJoiner; } }

        static bool s_init;
        public static void EnsureInit()
        {
            if (s_init) return;
            s_init = true;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-coopnolog") LoggingEnabled = false;
                if (args[i] == "-coophost") IsHost = true;
                if (args[i] == "-coopjoin" && i + 1 < args.Length) JoinIp = args[i + 1];
                if (args[i] == "-coopport" && i + 1 < args.Length) int.TryParse(args[i + 1], out Port);
                if (args[i] == "-coopdump" && i + 1 < args.Length)
                {
                    float f;
                    if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out f) && f > 0.2f)
                        DumpInterval = f;
                }
            }
            CoopLog.Open();
            OlCoop.Death.CoopSettings.Load();
            CoopLog.Write("INIT", CoopVersion.Full + " protocol=" + CoopVersion.Protocol +
                " game=" + typeof(Overload.GameManager).Assembly.GetName().Version +
                " logging=" + LoggingEnabled + " dumpInterval=" + DumpInterval +
                " coop=" + (IsHost ? "HOST" : IsJoiner ? "JOIN " + JoinIp : "off") + " port=" + Port);
            if (IsHost && IsJoiner) { CoopLog.Write("INIT", "both -coophost and -coopjoin given; acting as joiner only"); IsHost = false; }
        }
    }

    /// <summary>
    /// Writes to Overload\olcoop_logs\olcoop-YYYYMMDD-HHMMSS.log and mirrors to the Unity log
    /// (output_log.txt / Player.log) with an [olcoop] prefix. Never throws.
    /// </summary>
    public static class CoopLog
    {
        static StreamWriter s_writer;
        public static string FilePath;

        public static void Open()
        {
            if (!CoopConfig.LoggingEnabled || s_writer != null) return;
            try
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(baseDir)) baseDir = ".";
                string dir = Path.Combine(baseDir, "olcoop_logs");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, "olcoop-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-pid" +
                    System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
                s_writer = new StreamWriter(FilePath, false);
                s_writer.AutoFlush = true;
            }
            catch (Exception ex)
            {
                Debug.Log("[olcoop] could not open log file: " + ex.Message);
            }
        }

        public static void Write(string tag, string msg)
        {
            CoopConfig.EnsureInit();
            if (!CoopConfig.LoggingEnabled) return;
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + tag + "] " + msg;
                if (s_writer != null) s_writer.WriteLine(line);
                Debug.Log("[olcoop] " + line);
            }
            catch { }
        }

        public static void Error(string where, Exception ex)
        {
            Write("ERROR", where + ": " + ex);
        }
    }

    /// <summary>Harmony entry point: olmod calls PatchAll on this assembly; this prefix runs our init once.</summary>
    [HarmonyPatch(typeof(Overload.GameManager), "Awake")]
    static class CoopInitPatch
    {
        static void Postfix()
        {
            try { CoopConfig.EnsureInit(); } catch (Exception ex) { Debug.Log("[olcoop] init failed: " + ex); }
        }
    }
}
