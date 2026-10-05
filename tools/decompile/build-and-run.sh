#!/usr/bin/env bash
# Decompiles the game / olmod to readable C# in refs/ (git-ignored; never commit or redistribute the output).
# NuGet is blocked in the cloud workspace, so this builds ILSpy's decompiler engine (ICSharpCode.Decompiler, MIT) from its
# GitHub source with the .NET 8 SDK and no package restore.
#   apt-get install -y dotnet-sdk-8.0
#   bash tools/decompile/build-and-run.sh            # uses the staged game folder (same defaults as build.sh)
set -euo pipefail
cd "$(dirname "$0")/../.."
GAME_MANAGED="${GAME_MANAGED:-/mnt/user-data/uploads/Overload/Overload_Data/Managed}"
OLMOD_DLL="${OLMOD_DLL:-/mnt/user-data/uploads/Overload/GameMod.dll}"
WORK="${WORK:-/tmp/olcoop-decompiler}"
SRC="$WORK/ilspy"
if [ ! -d "$SRC" ]; then
  GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 -b v8.2 https://github.com/icsharpcode/ilspy "$SRC"
  # .NET 8 has its own Enumerable.MaxBy; call ILSpy's explicitly (only ambiguity left with NETCORE defined)
  sed -i '299s/switchInst\.Sections\.MaxBy(/ICSharpCode.Decompiler.Util.CollectionExtensions.MaxBy(switchInst.Sections, /' \
      "$SRC/ICSharpCode.Decompiler/IL/Transforms/ReduceNestingTransform.cs"
fi
D="$SRC/ICSharpCode.Decompiler"
mkdir -p "$WORK/app"
sed 's/\$INSERTVERSION\$/8.2.0.0/g; s/\$INSERTMAJORVERSION\$/8/g; s/\$INSERT[A-Z]*\$/0/g' "$D/Properties/DecompilerVersionInfo.template.cs" > "$WORK/app/Ver.cs"
cp tools/decompile/Main.cs "$WORK/app/Main.cs"
cat > "$WORK/app/nuget.config" <<'X'
<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>
X
cat > "$WORK/app/dec.csproj" <<X
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>latest</LangVersion><Nullable>disable</Nullable>
  <DefineConstants>\$(DefineConstants);NETCORE</DefineConstants><AllowUnsafeBlocks>true</AllowUnsafeBlocks><EnableDefaultItems>false</EnableDefaultItems>
  <NoWarn>\$(NoWarn);CS8632;CS0436;CS1591;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup>
  <ItemGroup><Compile Include="$D/**/*.cs" Exclude="$D/Properties/DecompilerVersionInfo.template.cs;$D/obj/**;$D/bin/**" /><Compile Include="Ver.cs" /><Compile Include="Main.cs" /></ItemGroup>
</Project>
X
[ -f "$WORK/app/out/dec.dll" ] || (cd "$WORK/app" && dotnet build -c Release -o out -v q)
DEC="dotnet $WORK/app/out/dec.dll"
$DEC "$GAME_MANAGED/Assembly-CSharp.dll" "$GAME_MANAGED" -- --all refs/cs-game
$DEC "$GAME_MANAGED/Assembly-CSharp-firstpass.dll" "$GAME_MANAGED" -- --all refs/cs-firstpass
$DEC "$GAME_MANAGED/UnityEngine.Networking.dll" "$GAME_MANAGED" -- --all refs/cs-unet
$DEC "$OLMOD_DLL" "$GAME_MANAGED" "$(dirname "$OLMOD_DLL")" lib -- --all refs/cs-olmod
echo "Decompiled to refs/cs-game, refs/cs-firstpass, refs/cs-unet, refs/cs-olmod"
