// Small driver for ICSharpCode.Decompiler (ILSpy engine, MIT). Built by tools/decompile/build-and-run.sh. Output goes to refs/ (git-ignored).
using System; using System.IO; using System.Linq;
using ICSharpCode.Decompiler; using ICSharpCode.Decompiler.CSharp; using ICSharpCode.Decompiler.Metadata; using ICSharpCode.Decompiler.TypeSystem;
static class P { static void Main(string[] a) {
  // dec <dll> <refdir...> -- <TypeFullName> [...]  |  dec <dll> <refdir> --all <outdir>
  var dll=a[0]; var sep=Array.IndexOf(a,"--"); 
  var file=new PEFile(dll); var res=new UniversalAssemblyResolver(dll,false,file.DetectTargetFrameworkId());
  foreach(var d in a.Skip(1).Take((sep<0?a.Length:sep)-1)) res.AddSearchDirectory(d);
  var s=new DecompilerSettings(LanguageVersion.CSharp7_3){ThrowOnAssemblyResolveErrors=false};
  var dc=new CSharpDecompiler(file,res,s);
  if(a[sep+1]=="--all"){ var o=a[sep+2]; Directory.CreateDirectory(o);
    foreach(var t in dc.TypeSystem.MainModule.TypeDefinitions.Where(t=>t.DeclaringTypeDefinition==null)){
      try{ File.WriteAllText(Path.Combine(o,(t.FullName.Replace('<','_').Replace('>','_').Replace('`','_'))+".cs"), dc.DecompileTypeAsString(t.FullTypeName)); }catch(Exception e){Console.Error.WriteLine(t.FullName+": "+e.Message);} }
    return; }
  foreach(var n in a.Skip(sep+1)) Console.WriteLine(dc.DecompileTypeAsString(new FullTypeName(n)));
}}
