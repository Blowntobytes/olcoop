# Prints the Overload game folder (the folder with Overload.exe and UnityPlayer.dll). Used by install.bat / uninstall.bat.
# 0.8.0: olcoop brings its own olmod, so only the game itself has to be found. Looks in: Steam's and GOG's install records,
# every Steam library (libraryfolders.vdf), common Steam/game folders on every drive, then shortcuts to Overload.exe / olmod.exe.
$ErrorActionPreference = 'SilentlyContinue'
function Test-Game($dir) { $dir -and (Test-Path -LiteralPath (Join-Path $dir 'Overload.exe')) -and (Test-Path -LiteralPath (Join-Path $dir 'UnityPlayer.dll')) }
function Done($dir) { (Resolve-Path -LiteralPath $dir).Path; exit 0 }

# 1. install records (the same ones olmod.exe checks)
foreach ($k in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 448850',
               'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 448850') {
  $v = Get-ItemProperty $k; if (Test-Game $v.InstallLocation) { Done $v.InstallLocation }
}
foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\1309632191', 'HKLM:\SOFTWARE\GOG.com\Games\1309632191') {
  $v = Get-ItemProperty $k; if (Test-Game $v.path) { Done $v.path }
}

# 2. Steam libraries and common folders
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
  foreach ($sub in 'SteamLibrary', 'Steam', 'Program Files (x86)\Steam', 'Program Files\Steam', 'Games\Steam', 'Games\SteamLibrary', 'Games', 'GOG Games', 'Program Files (x86)\GOG Galaxy\Games') { $libs += (Join-Path $r $sub) }
}
foreach ($l in ($libs | Select-Object -Unique)) {
  foreach ($o in (Join-Path $l 'steamapps\common\Overload'), (Join-Path $l 'Overload')) { if (Test-Game $o) { Done $o } }
}

# 3. shortcuts to Overload.exe or olmod.exe (olmod is usually inside the game folder)
$shell = New-Object -ComObject WScript.Shell
$lnkDirs = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'),
             [Environment]::GetFolderPath('StartMenu'), [Environment]::GetFolderPath('CommonStartMenu'),
             (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'))
foreach ($d in $lnkDirs) {
  if (-not $d -or -not (Test-Path -LiteralPath $d)) { continue }
  foreach ($f in (Get-ChildItem -LiteralPath $d -Filter *.lnk -Recurse -Depth 3)) {
    $t = $shell.CreateShortcut($f.FullName).TargetPath
    if ($t -and ((Split-Path $t -Leaf) -in 'Overload.exe', 'olmod.exe')) { $dir = Split-Path $t -Parent; if (Test-Game $dir) { Done $dir } }
  }
}
