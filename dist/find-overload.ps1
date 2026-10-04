# Prints the Overload install folder (prefers one with olmod installed). Used by install.bat.
# Looks in: Steam's library list (any drive), then common Steam folders on every drive.
$roots = @()
foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
  $v = Get-ItemProperty $k -ErrorAction SilentlyContinue
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
foreach ($d in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
  $r = $d.Root
  foreach ($sub in 'SteamLibrary', 'Steam', 'Program Files (x86)\Steam', 'Program Files\Steam', 'Games\Steam', 'Games\SteamLibrary', 'Games') {
    $libs += (Join-Path $r $sub)
  }
}
$found = $null
foreach ($l in ($libs | Select-Object -Unique)) {
  foreach ($o in (Join-Path $l 'steamapps\common\Overload'), (Join-Path $l 'Overload')) {
    if (Test-Path -LiteralPath (Join-Path $o 'Overload.exe')) {
      if (Test-Path -LiteralPath (Join-Path $o 'olmod.exe')) { (Resolve-Path -LiteralPath $o).Path; exit 0 }
      if (-not $found) { $found = (Resolve-Path -LiteralPath $o).Path }
    }
  }
}
if ($found) { $found }
