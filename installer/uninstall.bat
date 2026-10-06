@echo off
setlocal
if "%OLPATH%"=="" set "OLPATH=C:\Program Files (x86)\Steam\steamapps\common\Overload"
del /Q "%OLPATH%\Mod-olcoop.dll" 2>nul
del /Q "%OLPATH%\olcoop.bat" "%OLPATH%\olcoop-vr.bat" "%OLPATH%\olcoop-steamvr.bat" "%OLPATH%\olcoop-oculus.bat" "%OLPATH%\olcoop-host.bat" "%OLPATH%\olcoop-join.bat" 2>nul
del /Q "%OLPATH%\olcoop.ico" "%OLPATH%\olcoop.lnk" "%OLPATH%\olcoop VR.lnk" "%OLPATH%\olcoop SteamVR.lnk" "%OLPATH%\olcoop Oculus.lnk" 2>nul
powershell -NoProfile -Command "foreach($n in 'olcoop.lnk','olcoop VR.lnk','olcoop SteamVR.lnk','olcoop Oculus.lnk'){ Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) $n) -ErrorAction SilentlyContinue }" >nul 2>nul
echo Removed olcoop. olmod and the game are untouched. (Logs in "%OLPATH%\olcoop_logs" were kept.)
pause
