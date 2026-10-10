@echo off
setlocal
rem Removes olcoop (Overload\olcoop with its own olmod copy) and its shortcuts. Your regular olmod and the game are untouched.
rem Logs (olcoop\olcoop_logs) and co-op settings (olcoop\olcoop-settings.txt) are kept.
if "%OLPATH%"=="" if exist "%~dp0..\Overload.exe" set "OLPATH=%~dp0.."
if "%OLPATH%"=="" for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0find-overload.ps1"`) do set "OLPATH=%%p"
if "%OLPATH%"=="" set /p "OLPATH=Overload folder (the one with Overload.exe): "
set "OLPATH=%OLPATH:"=%"
if "%OLPATH:~-1%"=="\" set "OLPATH=%OLPATH:~0,-1%"
set "DST=%OLPATH%\olcoop"
if not exist "%DST%\Mod-olcoop.dll" if not exist "%OLPATH%\Mod-olcoop.dll" echo olcoop is not installed in "%OLPATH%" - nothing to remove there.
for %%f in (Mod-olcoop.dll olmod.exe GameMod.dll 0Harmony.dll olmod-LICENSE.txt olmod-README.txt olmodchanges.txt olcoop.bat olcoop-steamvr.bat olcoop-oculus.bat olcoop.ico "olcoop.lnk" "olcoop SteamVR.lnk" "olcoop Oculus.lnk") do del /Q "%DST%\%%~f" 2>nul
rem pre-0.8.0 installs in the Overload folder
del /Q "%OLPATH%\Mod-olcoop.dll" "%OLPATH%\olcoop.bat" "%OLPATH%\olcoop-vr.bat" "%OLPATH%\olcoop-steamvr.bat" "%OLPATH%\olcoop-oculus.bat" "%OLPATH%\olcoop-host.bat" "%OLPATH%\olcoop-join.bat" 2>nul
del /Q "%OLPATH%\olcoop.ico" "%OLPATH%\olcoop.lnk" "%OLPATH%\olcoop VR.lnk" "%OLPATH%\olcoop SteamVR.lnk" "%OLPATH%\olcoop Oculus.lnk" 2>nul
powershell -NoProfile -Command "foreach($n in 'olcoop.lnk','olcoop VR.lnk','olcoop SteamVR.lnk','olcoop Oculus.lnk'){ Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) $n) -ErrorAction SilentlyContinue }" >nul 2>nul
rd "%DST%" 2>nul
echo Removed olcoop. Your regular olmod and the game are untouched. (Logs and settings in "%DST%" were kept.)
pause
