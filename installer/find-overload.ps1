# Prints the olmod folder (the folder with olmod.exe and GameMod.dll) - where olcoop has to go. Used by install.bat / uninstall.bat.
# olmod loads Mod-*.dll from its own folder, which is usually the Overload folder but doesn't have to be.
# Looks in: Overload folders (Steam's library list on any drive, common Steam folders), shortcuts to olmod.exe (Desktop,
# Start Menu, taskbar), then a few usual places (user folders, drive roots).
$ErrorActionPreference = 'SilentlyContinue'
function Test-Olmod($dir) { $dir -and (Test-Path -LiteralPath (Join-Path $dir 'olmod.exe')) -and (Test-Path -LiteralPath (Join-Path $dir 'GameMod.dll')) }
function Done($dir) { (Resolve-Path -LiteralPath $dir).Path; exit 0 }

# 1. Overload folders (the standard olmod install)
$roots = @()
foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
  $v = Get-ItemProperty $k
  if ($v.SteamPath)   { $roots += $v.SteamPath }
  if ($v.InstallPath) { $roots += $v.InstallPath }
}
$libs = @()
foreach ($s in $roots) {
  $libs += $s
  $vdf = Join-Path $s 'steamapps\libraryfolders.vdf'
  if (Test-Path $vdf) {
    foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"')) { $libs += ($m.Matches[0].Groups[1].Value -replace '\\\\', '\') }
  }
}
$drives = Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Root }
foreach ($r in $drives) {
  foreach ($sub in 'SteamLibrary', 'Steam', 'Program Files (x86)\Steam', 'Program Files\Steam', 'Games\Steam', 'Games\SteamLibrary', 'Games') { $libs += (Join-Path $r $sub) }
}
foreach ($l in ($libs | Select-Object -Unique)) {
  foreach ($o in (Join-Path $l 'steamapps\common\Overload'), (Join-Path $l 'Overload')) { if (Test-Olmod $o) { Done $o } }
}

# 2. shortcuts that start olmod.exe (most people start olmod from its folder or a shortcut made from it)
$shell = New-Object -ComObject WScript.Shell
$lnkDirs = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'),
             [Environment]::GetFolderPath('StartMenu'), [Environment]::GetFolderPath('CommonStartMenu'),
             (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'))
foreach ($d in $lnkDirs) {
  if (-not $d -or -not (Test-Path -LiteralPath $d)) { continue }
  foreach ($f in (Get-ChildItem -LiteralPath $d -Filter *.lnk -Recurse -Depth 3)) {
    $t = $shell.CreateShortcut($f.FullName).TargetPath
    if ($t -and ((Split-Path $t -Leaf) -ieq 'olmod.exe')) { $dir = Split-Path $t -Parent; if (Test-Olmod $dir) { Done $dir } }
  }
}

# 3. usual places for an unzipped olmod
$places = @()
foreach ($u in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), (Join-Path $env:USERPROFILE 'Downloads'), $env:USERPROFILE) { $places += $u }
foreach ($r in $drives) { $places += $r; $places += (Join-Path $r 'Games') }
foreach ($p in ($places | Select-Object -Unique)) {
  if (-not (Test-Path -LiteralPath $p)) { continue }
  if (Test-Olmod $p) { Done $p }
  foreach ($c in (Get-ChildItem -LiteralPath $p -Directory -Filter '*olmod*' -Depth 1)) { if (Test-Olmod $c.FullName) { Done $c.FullName } }
}
