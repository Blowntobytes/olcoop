// Offline check: every [HarmonyPatch(type, "Method")] in the mod resolves to a real method,
// and every Prefix/Postfix parameter name exists on the target (or is a Harmony special).
using System; using System.Linq; using System.Reflection; using System.IO;
static class V {
  static int Main(string[] a) {
    var managed = a[1];
    AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
      var n = new AssemblyName(e.Name).Name;
      foreach (var d in new[]{ managed, Path.GetDirectoryName(a[0]), a.Length>2?a[2]:"" }) {
        var p = Path.Combine(d, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
      return null; };
    var mod = Assembly.LoadFrom(a[0]); int bad = 0, ok = 0;
    foreach (var t in mod.GetTypes()) {
      foreach (var at in t.GetCustomAttributesData().Where(x => x.AttributeType.Name == "HarmonyPatch")) {
        var args = at.ConstructorArguments;
        if (args.Count == 0) {
          // [HarmonyPatch] + TargetMethod(): class must declare const OlmodTarget = "Namespace.Type[+Nested]:Method".
          var tf = t.GetField("OlmodTarget", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
          if (tf == null) { Console.WriteLine("BAD  {0}: no OlmodTarget const", t.Name); bad++; continue; }
          var spec = (string)tf.GetRawConstantValue(); var parts = spec.Split(':');
          var olmod = Assembly.LoadFrom(Path.Combine(a.Length>2?a[2]:"", "GameMod.dll"));
          var tt = olmod.GetType(parts[0]);
          var pm = tt == null ? null : tt.GetMethod(parts[1], BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
          if (pm == null) { Console.WriteLine("BAD  {0}: olmod target {1} not found", t.Name, spec); bad++; } else ok++;
          continue;
        }
        var target = args[0].Value as Type; var name = args.Count > 1 ? args[1].Value as string : null;
        var ms = target.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                       .Where(m => m.Name == name).ToArray();
        if (ms.Length != 1) { Console.WriteLine("BAD  {0}: {1}.{2} matches {3} methods", t.Name, target.Name, name, ms.Length); bad++; continue; }
        var pn = ms[0].GetParameters().Select(p => p.Name).ToList();
        foreach (var hm in t.GetMethods(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Where(m => m.Name=="Prefix"||m.Name=="Postfix"))
          foreach (var p in hm.GetParameters())
            if (!p.Name.StartsWith("__") && !pn.Contains(p.Name)) { Console.WriteLine("BAD  {0}.{1}: param '{2}' not on {3}.{4}({5})", t.Name, hm.Name, p.Name, target.Name, name, string.Join(",", pn)); bad++; }
            else if (p.Name=="__instance" && ms[0].IsStatic) { Console.WriteLine("BAD  {0}: __instance on static", t.Name); bad++; }
        ok++;
      }
    }
    Console.WriteLine("checked {0} patches, {1} problems", ok, bad); return bad == 0 ? 0 : 1;
  }
}
