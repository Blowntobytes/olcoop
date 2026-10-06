// Flare colors (0.6.8). Each player picks a flare color in CO-OP OPTIONS (own setting, saved as flarecolor=); every player sees
// each flare in its owner's color.
//  - The flare prefab (resources.assets, proj_flare / proj_flare_sticky): main Light (0.353, 0.647, 1) intensity set every frame by
//    Projectile.UpdateDynamic, child _fill_light (0.551, 0.703, 1, intensity 0.4, range 20), particle glows _glow/_glow2/_glow3, and a
//    ProFlare lens flare on _lens_flare (GlobalTintColor). All of them are recolored; ORIGINAL restores the prefab values (pooled).
//  - "Just as bright": the lights' intensity is scaled by (luminance of the original color / luminance of the new one), at least 1,
//    so red and purple light the level as much as the original; particle glows keep their original brightness (max component).
// Network: msg 202 J->H own color (byte), msg 203 H->J table {netId, color} (2 s and on change). The host's own color is in the table.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Overload;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace OlCoop.World
{
    public class FlareTableMsg : MessageBase
    {
        public List<KeyValuePair<uint, byte>> e = new List<KeyValuePair<uint, byte>>();
        public override void Serialize(NetworkWriter w) { w.Write((byte)e.Count); foreach (var kv in e) { w.WritePackedUInt32(kv.Key); w.Write(kv.Value); } }
        public override void Deserialize(NetworkReader r) { int n = r.ReadByte(); e.Clear(); for (int i = 0; i < n; i++) { uint id = r.ReadPackedUInt32(); e.Add(new KeyValuePair<uint, byte>(id, r.ReadByte())); } }
    }

    public static class CoopFlares
    {
        public const short MsgMine = 202, MsgTable = 203;
        static readonly string[] Names = { "ORIGINAL", "RED", "ORANGE", "YELLOW", "GREEN", "PURPLE" };
        static readonly Color[] Tints = { Color.white, new Color(1f, 0.2f, 0.15f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.95f, 0.3f), new Color(0.3f, 1f, 0.35f), new Color(0.8f, 0.35f, 1f) };
        static readonly Color OriginalLight = new Color(0.353f, 0.6467f, 1f);
        public static int Count { get { return Names.Length; } }
        public static string Name(int i) { return Names[Mathf.Clamp(i, 0, Names.Length - 1)]; }

        static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
        static float Boost(int i) { return i <= 0 ? 1f : Mathf.Clamp(Lum(OriginalLight) / Mathf.Max(0.05f, Lum(Tints[i])), 1f, 2f); }

        // ---- who has which color
        static readonly Dictionary<uint, byte> s_table = new Dictionary<uint, byte>();
        static readonly Dictionary<int, byte> s_by_conn = new Dictionary<int, byte>(); // host: joiner connection -> color
        static float s_next_send;

        static int Mine { get { return Mathf.Clamp(OlCoop.Death.CoopSettings.FlareColor, 0, Names.Length - 1); } }

        public static int ColorOf(GameObject owner)
        {
            if (owner == null) return 0;
            var ship = owner.GetComponent<PlayerShip>() ?? owner.GetComponentInParent<PlayerShip>();
            if (ship == null || ship.c_player == null) return 0;
            if (ship.isLocalPlayer) return Mine;
            byte c; return s_table.TryGetValue(ship.c_player.netId.Value, out c) ? Mathf.Clamp(c, 0, Names.Length - 1) : 0;
        }

        public static void MyColorChanged() { s_next_send = 0f; }

        public static void OnMine(NetworkMessage msg)   // host
        {
            try { s_by_conn[msg.conn.connectionId] = (byte)msg.ReadMessage<IntegerMessage>().value; s_next_send = 0f; }
            catch (Exception ex) { CoopLog.Error("Flare OnMine", ex); }
        }

        public static void OnTable(NetworkMessage msg)  // joiner
        {
            try { foreach (var kv in msg.ReadMessage<FlareTableMsg>().e) s_table[kv.Key] = kv.Value; }
            catch (Exception ex) { CoopLog.Error("Flare OnTable", ex); }
        }

        public static void Tick()
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer || !GameplayManager.LevelIsLoaded) return;
            if (Time.realtimeSinceStartup < s_next_send) return;
            s_next_send = Time.realtimeSinceStartup + 2f;
            if (CoopConfig.IsJoiner)
            {
                var c = Client.GetClient();
                if (c != null && c.isConnected) c.Send(MsgMine, new IntegerMessage(Mine));
            }
            else if (CoopConfig.IsHost && NetworkServer.active)
            {
                var m = new FlareTableMsg();
                foreach (var p in Overload.NetworkManager.m_Players)
                {
                    if (p == null) continue;
                    byte col = 0;
                    if (p.isLocalPlayer) col = (byte)Mine;
                    else if (p.connectionToClient != null) s_by_conn.TryGetValue(p.connectionToClient.connectionId, out col);
                    s_table[p.netId.Value] = col;
                    m.e.Add(new KeyValuePair<uint, byte>(p.netId.Value, col));
                }
                foreach (var c in NetworkServer.connections)
                    if (c != null && c.connectionId != 0 && c.isConnected && OlCoop.Session.CoopHost.Verified.Contains(c.connectionId)) c.Send(MsgTable, m);
            }
        }

        // ---- recoloring a flare (pooled: originals remembered per projectile)
        class Orig
        {
            public Color fill; public float fillIntensity; public Light fillLight; public Color mainColor;
            public List<KeyValuePair<ParticleSystem, ParticleSystem.MinMaxGradient>> ps = new List<KeyValuePair<ParticleSystem, ParticleSystem.MinMaxGradient>>();
            public ProFlare flare; public Color flareTint;
            public int applied;
        }
        static readonly Dictionary<int, Orig> s_orig = new Dictionary<int, Orig>();
        static int s_logged;

        static Orig Get(Projectile p)
        {
            Orig o; int key = p.GetInstanceID();
            if (s_orig.TryGetValue(key, out o)) return o;
            o = new Orig { mainColor = p.c_light != null ? p.c_light.color : OriginalLight };
            var t = p.transform;
            var fill = t.Find("_fill_light");
            if (fill != null) { o.fillLight = fill.GetComponent<Light>(); if (o.fillLight != null) { o.fill = o.fillLight.color; o.fillIntensity = o.fillLight.intensity; } }
            foreach (var ps in p.GetComponentsInChildren<ParticleSystem>(true)) o.ps.Add(new KeyValuePair<ParticleSystem, ParticleSystem.MinMaxGradient>(ps, ps.main.startColor));
            o.flare = p.GetComponentInChildren<ProFlare>(true);
            if (o.flare != null) o.flareTint = o.flare.GlobalTintColor;
            s_orig[key] = o;
            return o;
        }

        static Color Tinted(Color tint, Color orig)
        {
            float b = Mathf.Max(orig.r, Mathf.Max(orig.g, orig.b));
            return new Color(tint.r * b, tint.g * b, tint.b * b, orig.a);
        }

        static ParticleSystem.MinMaxGradient Tint(ParticleSystem.MinMaxGradient g, Color tint)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Tinted(tint, g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Tinted(tint, g.colorMin), Tinted(tint, g.colorMax));
                default: return new ParticleSystem.MinMaxGradient(Tinted(tint, g.colorMax.a > 0f ? g.colorMax : Color.white));
            }
        }

        public static void Apply(Projectile p, int color)
        {
            var o = Get(p);
            if (color == 0 && o.applied == 0) return;   // never tinted: nothing to undo
            Color tint = Tints[Mathf.Clamp(color, 0, Tints.Length - 1)];
            bool orig = color == 0;
            if (p.c_light != null) p.c_light.color = orig ? o.mainColor : tint;
            if (o.fillLight != null) { o.fillLight.color = orig ? o.fill : tint; o.fillLight.intensity = o.fillIntensity * Boost(color); }
            foreach (var kv in o.ps)
            {
                if (kv.Key == null) continue;
                var main = kv.Key.main;
                main.startColor = orig ? kv.Value : Tint(kv.Value, tint);
                // particles already emitted when the flare was switched on
                var parts = new ParticleSystem.Particle[kv.Key.main.maxParticles];
                int n = kv.Key.GetParticles(parts);
                if (n > 0)
                {
                    Color32 c = orig ? (Color32)(kv.Value.mode == ParticleSystemGradientMode.Color ? kv.Value.color : kv.Value.colorMax) : (Color32)Tinted(tint, kv.Value.mode == ParticleSystemGradientMode.Color ? kv.Value.color : kv.Value.colorMax);
                    for (int i = 0; i < n; i++) { var c0 = parts[i].startColor; parts[i].startColor = new Color32(c.r, c.g, c.b, c0.a); }
                    kv.Key.SetParticles(parts, n);
                }
            }
            if (o.flare != null) o.flare.GlobalTintColor = orig ? o.flareTint : new Color(tint.r, tint.g, tint.b, o.flareTint.a);
            o.applied = color;
            if (color != 0 && s_logged++ < 5) CoopLog.Write("FLARE", "flare colored " + Names[color] + " (light x" + Boost(color).ToString("F2") + ", " + o.ps.Count + " glows" + (o.flare != null ? ", lens flare" : "") + ")");
        }

        /// UpdateDynamic resets the main light's intensity every frame; scale it for the color's brightness.
        public static void AfterUpdate(Projectile p)
        {
            Orig o;
            if (p.c_light == null || !s_orig.TryGetValue(p.GetInstanceID(), out o) || o.applied == 0) return;
            p.c_light.intensity *= Boost(o.applied);
        }

        public static void ResetForLevel() { s_orig.Clear(); }
        public static void ForgetConn(int conn) { s_by_conn.Remove(conn); }
    }

    [HarmonyPatch(typeof(Projectile), "Fire")]
    static class FL1_ColorFlares
    {
        static void Postfix(Projectile __instance, GameObject owner)
        {
            if (!CoopConfig.Active || GameplayManager.IsMultiplayer) return;
            if (__instance.m_type != ProjPrefab.proj_flare && __instance.m_type != ProjPrefab.proj_flare_sticky) return;
            try { CoopFlares.Apply(__instance, CoopFlares.ColorOf(owner)); } catch (Exception ex) { CoopLog.Error("FL1", ex); }
        }
    }

    [HarmonyPatch(typeof(Projectile), "UpdateDynamic")]
    static class FL2_FlareBrightness
    {
        static void Postfix(Projectile __instance)
        {
            if (!CoopConfig.Active || (__instance.m_type != ProjPrefab.proj_flare && __instance.m_type != ProjPrefab.proj_flare_sticky)) return;
            try { CoopFlares.AfterUpdate(__instance); } catch (Exception ex) { CoopLog.Error("FL2", ex); }
        }
    }

    [HarmonyPatch(typeof(GameplayManager), "Update")]
    static class FL3_Tick
    {
        static void Postfix() { try { CoopFlares.Tick(); } catch (Exception ex) { CoopLog.Error("FL3", ex); } }
    }

    [HarmonyPatch(typeof(GameplayManager), "StartLevel")]
    static class FL4_Reset
    {
        static void Postfix() { CoopFlares.ResetForLevel(); }
    }

    [HarmonyPatch(typeof(Server), "RegisterHandlers")]
    static class FL5_ServerHandlers
    {
        static void Postfix() { CoopConfig.EnsureInit(); if (CoopConfig.IsHost) NetworkServer.RegisterHandler(CoopFlares.MsgMine, CoopFlares.OnMine); }
    }

    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    static class FL6_ClientHandlers
    {
        static void Postfix()
        {
            CoopConfig.EnsureInit();
            if (CoopConfig.IsJoiner && Client.GetClient() != null) Client.GetClient().RegisterHandler(CoopFlares.MsgTable, CoopFlares.OnTable);
        }
    }
}
