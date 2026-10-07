@echo off
setlocal
rem olcoop lives in the olmod folder (usually the Overload folder). Same search as install.bat.
if "%OLPATH%"=="" if exist "%~dp0..\olmod.exe" set "OLPATH=%~dp0.."
if "%OLPATH%"=="" for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0find-overload.ps1"`) do set "OLPATH=%%p"
if "%OLPATH%"=="" set /p "OLPATH=Folder that contains olmod.exe and Mod-olcoop.dll: "
set "OLPATH=%OLPATH:"=%"
if not exist "%OLPATH%\Mod-olcoop.dll" echo olcoop is not installed in "%OLPATH%" - nothing to remove there.
del /Q "%OLPATH%\Mod-olcoop.dll" 2>nul
del /Q "%OLPATH%\olcoop.bat" "%OLPATH%\olcoop-vr.bat" "%OLPATH%\olcoop-steamvr.bat" "%OLPATH%\olcoop-oculus.bat" "%OLPATH%\olcoop-host.bat" "%OLPATH%\olcoop-join.bat" 2>nul
del /Q "%OLPATH%\olcoop.ico" "%OLPATH%\olcoop.lnk" "%OLPATH%\olcoop VR.lnk" "%OLPATH%\olcoop SteamVR.lnk" "%OLPATH%\olcoop Oculus.lnk" 2>nul
powershell -NoProfile -Command "foreach($n in 'olcoop.lnk','olcoop VR.lnk','olcoop SteamVR.lnk','olcoop Oculus.lnk'){ Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) $n) -ErrorAction SilentlyContinue }" >nul 2>nul
echo Removed olcoop. olmod and the game are untouched. (Logs in "%OLPATH%\olcoop_logs" were kept.)
pause
