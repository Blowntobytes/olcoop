@echo off
setlocal
title olcoop installer
rem 0.8.0 standalone: installs olcoop WITH its own olmod 0.5.14 into Overload\olcoop and VERIFIES every file.
rem Your own olmod (if any) is not touched; olcoop's olmod loads only the mods in Overload\olcoop.
rem Finds Overload: OLPATH if set, the folder above this one, then find-overload.ps1 (Steam/GOG records, every Steam library,
rem common folders on every drive, shortcuts), then asks.

if not exist "%~dp0olcoop\olmod.exe" (
  echo [X] The "olcoop" folder from the zip is missing next to install.bat. Unzip the WHOLE zip and run install.bat from there.
  goto :fail
)
if not "%OLPATH%"=="" goto :check
if exist "%~dp0..\Overload.exe" (set "OLPATH=%~dp0.." & goto :check)

echo Looking for Overload on all drives...
for /f "usebackq delims=" %%p in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0find-overload.ps1"`) do set "OLPATH=%%p"
if not "%OLPATH%"=="" echo Found Overload in: %OLPATH%

if "%OLPATH%"=="" (
  echo Overload was not found automatically.
  echo Type the full path of the folder that contains Overload.exe
  echo ^(Steam: right-click Overload, Manage, Browse local files, copy the address bar^).
  set /p "OLPATH=Overload folder: "
)

:check
set "OLPATH=%OLPATH:"=%"
if "%OLPATH:~-1%"=="\" set "OLPATH=%OLPATH:~0,-1%"
if not exist "%OLPATH%\Overload.exe" (
  echo [X] Overload.exe not found in "%OLPATH%".
  goto :fail
)
set "DST=%OLPATH%\olcoop"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%~dp0olcoop\Mod-olcoop.dll').VersionInfo.ProductVersion"`) do set "NEWVER=%%v"
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "foreach($p in @('%DST%\Mod-olcoop.dll','%OLPATH%\Mod-olcoop.dll')){ if (Test-Path -LiteralPath $p) { (Get-Item -LiteralPath $p).VersionInfo.ProductVersion; exit } }; 'not installed'"`) do set "OLDVER=%%v"
echo.
echo   Installing olcoop %NEWVER% (standalone, with olmod 0.5.14)
echo   Currently installed: %OLDVER%
echo   Target folder: %DST%
echo.
if not exist "%DST%" mkdir "%DST%"
if not exist "%DST%" (
  echo [X] Could not create "%DST%". Right-click install.bat and choose "Run as administrator".
  goto :fail
)
copy /Y "%~dp0olcoop\*.*" "%DST%\" >nul
if errorlevel 1 (
  echo [X] Could not copy the files. Close Overload, or right-click install.bat and choose "Run as administrator".
  goto :fail
)
if not exist "%DST%\olcoop_logs" mkdir "%DST%\olcoop_logs"

rem Older olcoop versions (up to 0.7.x) lived next to the regular olmod in the Overload folder. Remove them there, so the
rem regular olmod no longer loads olcoop. Your co-op settings move along; old logs stay in Overload\olcoop_logs.
if exist "%OLPATH%\olcoop-settings.txt" if not exist "%DST%\olcoop-settings.txt" copy /Y "%OLPATH%\olcoop-settings.txt" "%DST%\" >nul
set "OLD=0"
if exist "%OLPATH%\Mod-olcoop.dll" set "OLD=1"
del /Q "%OLPATH%\Mod-olcoop.dll" "%OLPATH%\olcoop.bat" "%OLPATH%\olcoop-vr.bat" "%OLPATH%\olcoop-steamvr.bat" "%OLPATH%\olcoop-oculus.bat" 2>nul
del /Q "%OLPATH%\olcoop-host.bat" "%OLPATH%\olcoop-join.bat" "%OLPATH%\olcoop.ico" "%OLPATH%\olcoop-settings.txt" 2>nul
del /Q "%OLPATH%\olcoop.lnk" "%OLPATH%\olcoop VR.lnk" "%OLPATH%\olcoop SteamVR.lnk" "%OLPATH%\olcoop Oculus.lnk" 2>nul
if "%OLD%"=="1" echo   Removed the old olcoop from the Overload folder (your regular olmod no longer loads it).

rem Shortcuts with the olcoop icon (a .bat file itself cannot have an icon): Desktop + Overload\olcoop.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; $t=Join-Path $env:OLPATH 'olcoop'; foreach($d in @([Environment]::GetFolderPath('Desktop'), $t)){ Remove-Item -LiteralPath (Join-Path $d 'olcoop VR.lnk') -ErrorAction SilentlyContinue; foreach($v in @(@('olcoop','olcoop.bat','Overload co-op (olcoop)'),@('olcoop SteamVR','olcoop-steamvr.bat','Overload co-op (olcoop) in VR - SteamVR'),@('olcoop Oculus','olcoop-oculus.bat','Overload co-op (olcoop) in VR - Oculus'))){ $l=$w.CreateShortcut((Join-Path $d ($v[0]+'.lnk'))); $l.TargetPath=(Join-Path $t $v[1]); $l.WorkingDirectory=$t; $l.IconLocation=(Join-Path $t 'olcoop.ico'); $l.Description=$v[2]; $l.Save() } }" >nul 2>nul
if exist "%DST%\olcoop.lnk" (echo   Shortcuts "olcoop", "olcoop SteamVR" and "olcoop Oculus" created on your Desktop and in Overload\olcoop.) else (echo   Note: could not create the shortcuts; start olcoop.bat, olcoop-steamvr.bat or olcoop-oculus.bat in "%DST%".)

set "BAD="
for %%f in ("%~dp0olcoop\*.*") do (
  fc /b "%%~ff" "%DST%\%%~nxf" >nul 2>nul || set "BAD=%%~nxf"
)
if defined BAD (
  echo [X] VERIFY FAILED: "%BAD%" in "%DST%" differs from the zip. Is the game still running? Close it and retry.
  goto :fail
)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item -LiteralPath '%DST%\Mod-olcoop.dll').VersionInfo.ProductVersion"`) do set "GOTVER=%%v"
echo   =====================================================
echo     OK - olcoop %GOTVER% and olmod 0.5.14 are installed and verified.
echo   =====================================================
echo.
echo   Play: with Steam running, start the olcoop shortcut (Desktop) or olcoop.bat in "%DST%".
echo   VR:   SteamVR headsets: start SteamVR, then "olcoop SteamVR".
echo         Oculus/Meta headsets: start the Oculus app, then "olcoop Oculus".
echo   Main menu: CO-OP: HOST / JOIN  (bottom right).
echo   Your regular olmod and its shortcuts are unchanged - use them for normal olmod multiplayer.
echo.
pause
exit /b 0
:fail
echo.
echo   INSTALL DID NOT COMPLETE.
pause
exit /b 1
